param(
    [string]$BaseUrl = "http://127.0.0.1:5080"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http

$script:Client = [System.Net.Http.HttpClient]::new()
$script:Client.Timeout = [TimeSpan]::FromSeconds(30)
$script:Passed = 0
$script:Prefix = "step09-" + [Guid]::NewGuid().ToString("N")

function New-ApiMessage {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Actor = "",
        $Payload = $null
    )

    $uri = $BaseUrl.TrimEnd("/") + $Path
    $message = [System.Net.Http.HttpRequestMessage]::new()
    $message.Method = [System.Net.Http.HttpMethod]::new($Method)
    $message.RequestUri = [Uri]$uri
    if ($Actor) {
        $message.Headers.Add("X-User-Id", $Actor)
    }
    if ($null -ne $Payload) {
        $json = ConvertTo-Json -InputObject $Payload -Depth 12 -Compress
        $message.Content = [System.Net.Http.StringContent]::new(
            $json, [System.Text.Encoding]::UTF8, "application/json")
    }
    return $message
}

function Read-ApiResponse {
    param([System.Net.Http.HttpResponseMessage]$Response)

    try {
        $content = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $body = $null
        if ($content) {
            $body = ConvertFrom-Json -InputObject $content
        }
        return [PSCustomObject]@{
            Status = [int]$Response.StatusCode
            Body = $body
            HasTrace = $Response.Headers.Contains("X-Request-Id")
        }
    }
    finally {
        $Response.Dispose()
    }
}

function Send-Api {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Actor = "",
        $Payload = $null
    )

    $message = New-ApiMessage -Method $Method -Path $Path -Actor $Actor -Payload $Payload
    try {
        $response = $script:Client.SendAsync($message).GetAwaiter().GetResult()
        return Read-ApiResponse -Response $response
    }
    finally {
        $message.Dispose()
    }
}

function Send-ConcurrentPair {
    param(
        [string]$Path,
        [string]$Actor,
        $FirstPayload,
        $SecondPayload
    )

    $first = New-ApiMessage -Method "POST" -Path $Path -Actor $Actor -Payload $FirstPayload
    $second = New-ApiMessage -Method "POST" -Path $Path -Actor $Actor -Payload $SecondPayload
    try {
        # Start BOTH requests before waiting for either response.
        $firstTask = $script:Client.SendAsync($first)
        $secondTask = $script:Client.SendAsync($second)
        $one = Read-ApiResponse -Response ($firstTask.GetAwaiter().GetResult())
        $two = Read-ApiResponse -Response ($secondTask.GetAwaiter().GetResult())
        return @($one, $two)
    }
    finally {
        $first.Dispose()
        $second.Dispose()
    }
}

function New-Payload {
    param(
        [string]$Suffix,
        [string]$Application = "crm",
        [string]$Environment = "NonProduction",
        [string]$Access = "Read"
    )
    return @{
        client_request_id = $script:Prefix + "-" + $Suffix
        application_id = $Application
        environment = $Environment
        access_level = $Access
        justification = "Local assessment API verification"
    }
}

function Assert-Case {
    param([string]$Name, [bool]$Condition)
    if (-not $Condition) {
        throw "FAIL: $Name"
    }
    $script:Passed++
    Write-Host "PASS: $Name"
}

try {
    Write-Host "Testing $BaseUrl"
    Write-Host "This script creates local demo requests. It never deletes existing records."

    $health = Send-Api -Method GET -Path "/api/health"
    Assert-Case "Health and seeded users/applications" (
        $health.Status -eq 200 -and $health.Body.status -eq "ok" -and
        $health.Body.users -eq 5 -and $health.Body.applications -eq 2)

    $bootstrap = Send-Api -Method GET -Path "/api/bootstrap" -Actor alice
    Assert-Case "Bootstrap master data" (
        $bootstrap.Status -eq 200 -and @($bootstrap.Body.data.users).Count -eq 5 -and
        @($bootstrap.Body.data.applications).Count -eq 2)

    $anonymous = Send-Api -Method GET -Path "/api/bootstrap"
    Assert-Case "Missing identity returns 401 with trace" (
        $anonymous.Status -eq 401 -and $anonymous.Body.error.code -eq "unauthenticated" -and $anonymous.HasTrace)

    $invalidUser = Send-Api -Method GET -Path "/api/bootstrap" -Actor unknown
    Assert-Case "Unknown seeded identity returns 401" ($invalidUser.Status -eq 401)

    $bad = New-Payload -Suffix "invalid"
    $bad.justification = "   "
    $validation = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $bad
    Assert-Case "Blank justification returns 422" ($validation.Status -eq 422)

    $bad = New-Payload -Suffix "spoofed-requester"
    $bad.requester_id = "bob"
    $spoofed = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $bad
    Assert-Case "Unknown requester payload field rejected" ($spoofed.Status -eq 400)

    $standardPayload = New-Payload -Suffix "standard"
    $created = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $standardPayload
    Assert-Case "Create standard request returns 201" (
        $created.Status -eq 201 -and $created.Body.data.status -eq "AwaitingManager" -and
        $created.Body.data.version -eq 1 -and @($created.Body.data.audit).Count -eq 1)
    $standardId = $created.Body.data.id

    $replay = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $standardPayload
    Assert-Case "Identical retry returns same request and one submit audit" (
        $replay.Status -eq 200 -and $replay.Body.replayed -eq $true -and
        $replay.Body.data.id -eq $standardId -and @($replay.Body.data.audit).Count -eq 1)

    $changed = $standardPayload.Clone()
    $changed.justification = "Different payload"
    $conflict = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $changed
    Assert-Case "Same ID with changed payload returns conflict" (
        $conflict.Status -eq 409 -and $conflict.Body.error.code -eq "idempotency_conflict")

    $wrongActor = Send-Api -Method POST -Path "/api/requests" -Actor bob -Payload $standardPayload
    Assert-Case "Another actor cannot reuse ClientRequestId" (
        $wrongActor.Status -eq 409 -and $wrongActor.Body.error.code -eq "idempotency_conflict")

    $self = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor alice -Payload @{ version = 1 }
    Assert-Case "Self approval forbidden" ($self.Status -eq 403)

    $earlyOwner = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor carol -Payload @{ version = 1 }
    Assert-Case "Owner cannot bypass Manager stage" ($earlyOwner.Status -eq 403)

    $auditorDecision = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor erin -Payload @{ version = 1 }
    Assert-Case "Auditor is not an automatic approver" ($auditorDecision.Status -eq 403)

    $notVisible = Send-Api -Method GET -Path "/api/requests/$standardId" -Actor dana
    Assert-Case "Unrelated owner cannot view CRM request" ($notVisible.Status -eq 403)

    $wrongVersionType = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor bob -Payload @{ version = "1" }
    Assert-Case "String version rejected" ($wrongVersionType.Status -eq 400)

    $inbox = Send-Api -Method GET -Path "/api/requests?scope=inbox" -Actor bob
    Assert-Case "Manager inbox includes pending direct report request" (
        $inbox.Status -eq 200 -and @($inbox.Body.data | Where-Object { $_.id -eq $standardId }).Count -eq 1)

    $approved = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor bob -Payload @{ version = 1 }
    Assert-Case "Standard request approved with two audit events" (
        $approved.Status -eq 200 -and $approved.Body.data.status -eq "Approved" -and
        $approved.Body.data.version -eq 2 -and @($approved.Body.data.audit).Count -eq 2)

    $stale = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor bob -Payload @{ version = 1 }
    Assert-Case "Stale decision returns 409" ($stale.Status -eq 409 -and $stale.Body.error.code -eq "stale_version")

    $terminal = Send-Api -Method POST -Path "/api/requests/$standardId/approve" -Actor bob -Payload @{ version = 2 }
    Assert-Case "Terminal request cannot be processed again" (
        $terminal.Status -eq 409 -and $terminal.Body.error.code -eq "terminal_state")

    $replayFinal = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload $standardPayload
    Assert-Case "Idempotent replay still works after approval" (
        $replayFinal.Status -eq 200 -and $replayFinal.Body.data.id -eq $standardId -and
        $replayFinal.Body.data.status -eq "Approved" -and @($replayFinal.Body.data.audit).Count -eq 2)

    $production = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload (
        New-Payload -Suffix "production" -Environment Production)
    $productionId = $production.Body.data.id
    $manager = Send-Api -Method POST -Path "/api/requests/$productionId/approve" -Actor bob -Payload @{ version = 1 }
    Assert-Case "Production waits for Carol after Manager approval" (
        $manager.Status -eq 200 -and $manager.Body.data.status -eq "AwaitingOwner" -and
        $manager.Body.data.assigned_approver -eq "Carol")
    $owner = Send-Api -Method POST -Path "/api/requests/$productionId/approve" -Actor carol -Payload @{ version = 2 }
    Assert-Case "Carol completes Production request with three audit events" (
        $owner.Status -eq 200 -and $owner.Body.data.status -eq "Approved" -and @($owner.Body.data.audit).Count -eq 3)

    $admin = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload (
        New-Payload -Suffix "admin" -Application finance -Access Admin)
    $adminId = $admin.Body.data.id
    $adminManager = Send-Api -Method POST -Path "/api/requests/$adminId/approve" -Actor bob -Payload @{ version = 1 }
    Assert-Case "NonProduction Admin waits for Dana" (
        $adminManager.Status -eq 200 -and $adminManager.Body.data.status -eq "AwaitingOwner" -and
        $adminManager.Body.data.assigned_approver -eq "Dana")
    $wrongOwner = Send-Api -Method POST -Path "/api/requests/$adminId/approve" -Actor carol -Payload @{ version = 2 }
    Assert-Case "Carol cannot approve Finance Portal request" ($wrongOwner.Status -eq 403)
    $financeOwner = Send-Api -Method POST -Path "/api/requests/$adminId/approve" -Actor dana -Payload @{ version = 2 }
    Assert-Case "Dana completes Finance Admin request" (
        $financeOwner.Status -eq 200 -and $financeOwner.Body.data.status -eq "Approved")

    $reject = Send-Api -Method POST -Path "/api/requests" -Actor alice -Payload (New-Payload -Suffix "reject")
    $rejectId = $reject.Body.data.id
    $noReason = Send-Api -Method POST -Path "/api/requests/$rejectId/reject" -Actor bob -Payload @{ version = 1 }
    Assert-Case "Reject requires reason" ($noReason.Status -eq 422)
    $rejected = Send-Api -Method POST -Path "/api/requests/$rejectId/reject" -Actor bob -Payload @{
        version = 1
        reason = "Insufficient business justification"
    }
    $rejectAudit = @($rejected.Body.data.audit)
    Assert-Case "Rejected status and reason recorded in audit" (
        $rejected.Status -eq 200 -and $rejected.Body.data.status -eq "Rejected" -and
        $rejectAudit[-1].reason -eq "Insufficient business justification")

    $all = Send-Api -Method GET -Path "/api/requests?scope=all" -Actor erin
    Assert-Case "Auditor sees created requests" (
        $all.Status -eq 200 -and @($all.Body.data | Where-Object { $_.id -eq $standardId }).Count -eq 1)
    $forbiddenAll = Send-Api -Method GET -Path "/api/requests?scope=all" -Actor alice
    Assert-Case "Requester cannot use auditor listing" ($forbiddenAll.Status -eq 403)
    $missing = Send-Api -Method GET -Path "/api/requests/does-not-exist" -Actor alice
    Assert-Case "Missing request returns 404" ($missing.Status -eq 404)

    $concurrentPayload = New-Payload -Suffix "concurrent-create"
    $pair = @(Send-ConcurrentPair -Path "/api/requests" -Actor alice -FirstPayload $concurrentPayload -SecondPayload $concurrentPayload)
    Assert-Case "Concurrent duplicate submit returns one create and one replay" (
        @($pair | Where-Object { $_.Status -eq 201 }).Count -eq 1 -and
        @($pair | Where-Object { $_.Status -eq 200 }).Count -eq 1 -and
        $pair[0].Body.data.id -eq $pair[1].Body.data.id)
    $concurrentId = $pair[0].Body.data.id

    $decisionPair = @(Send-ConcurrentPair -Path "/api/requests/$concurrentId/approve" -Actor bob -FirstPayload @{ version = 1 } -SecondPayload @{ version = 1 })
    Assert-Case "Concurrent decisions allow exactly one success and one conflict" (
        @($decisionPair | Where-Object { $_.Status -eq 200 }).Count -eq 1 -and
        @($decisionPair | Where-Object { $_.Status -eq 409 }).Count -eq 1)
    $finalDetail = Send-Api -Method GET -Path "/api/requests/$concurrentId" -Actor alice
    Assert-Case "Concurrent decision produces version 2 and two audit events" (
        $finalDetail.Status -eq 200 -and $finalDetail.Body.data.version -eq 2 -and
        @($finalDetail.Body.data.audit).Count -eq 2)

    Write-Host ""
    Write-Host "$script:Passed API checks passed."
    Write-Host "Record this actual output in docs/VALIDATION.md before committing stage 9."
}
finally {
    $script:Client.Dispose()
}
