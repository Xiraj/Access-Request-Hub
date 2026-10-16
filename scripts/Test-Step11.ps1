param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateRange(1024, 65535)]
    [int]$Port = 5081,
    [switch]$SkipFrontend
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot "tests\AccessRequestHub.Tests\AccessRequestHub.Tests.csproj"
$appRoot = Join-Path $repoRoot "src\AccessRequestHub"
$appDll = Join-Path $appRoot "bin\$Configuration\net10.0\AccessRequestHub.dll"
$apiScript = Join-Path $PSScriptRoot "Test-Step09.ps1"
$frontendScript = Join-Path $PSScriptRoot "Test-Step10.mjs"
$baseUrl = "http://127.0.0.1:$Port"
$dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

foreach ($required in @($testProject, $apiScript)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file is missing: $required. Copy the step 11 files and retain Test-Step09.ps1."
    }
}

$runDirectory = Join-Path ([IO.Path]::GetTempPath()) ("accesshub-step11-" + [Guid]::NewGuid().ToString("N"))
$databasePath = Join-Path $runDirectory "verification.sqlite3"
$server = $null
$serverStarted = $false
$stdoutTask = $null
$stderrTask = $null
$finished = $false
$frontendStatus = "SKIPPED"
$script:SmokePassed = 0
$script:Step11Client = [System.Net.Http.HttpClient]::new()
$script:Step11Client.Timeout = [TimeSpan]::FromSeconds(2)

function Read-Probe {
    param([string]$Path)
    $response = $script:Step11Client.GetAsync($baseUrl + $Path).GetAwaiter().GetResult()
    try {
        return [PSCustomObject]@{
            Status = [int]$response.StatusCode
            Text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            ContentType = [string]$response.Content.Headers.ContentType
            HasTrace = $response.Headers.Contains("X-Request-Id")
            HasCsp = $response.Headers.Contains("Content-Security-Policy")
        }
    }
    finally { $response.Dispose() }
}

function Assert-Smoke {
    param([string]$Name, [bool]$Condition)
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:SmokePassed++
    Write-Host "PASS: $Name"
}

try {
    # Check availability only; never stop an existing listener or application.
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $listener.Start() }
    catch { throw "Port $Port is in use. Choose another port with -Port 5082." }
    finally { $listener.Stop() }

    Write-Host "Building the test project and its application reference..."
    & $dotnet build $testProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed. Stop dotnet run first if an executable is locked." }

    Write-Host "Running isolated C# service/database integration checks..."
    & $dotnet run --project $testProject --configuration $Configuration --no-build --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "C# integration checks failed." }

    if ($SkipFrontend) {
        $frontendStatus = "SKIPPED by -SkipFrontend"
    }
    else {
        if (-not (Test-Path -LiteralPath $frontendScript -PathType Leaf)) {
            throw "Test-Step10.mjs is missing. Copy it from step 10 or use -SkipFrontend and document the skip."
        }
        $node = Get-Command node -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $node) {
            throw "Node.js 20+ is required for the frontend checks. Install it or use -SkipFrontend and document the skip."
        }
        Write-Host "Running JavaScript checks with simulated DOM/HTTP..."
        & $node.Source --test $frontendScript
        if ($LASTEXITCODE -ne 0) { throw "Frontend JavaScript checks failed." }
        $frontendStatus = "PASSED (simulated DOM/HTTP)"
    }

    # Only this child process receives HUB_DB_PATH. The existing demo DB is not used.
    [IO.Directory]::CreateDirectory($runDirectory) | Out-Null
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnet
    $startInfo.Arguments = '"{0}" --urls "{1}"' -f $appDll, $baseUrl
    $startInfo.WorkingDirectory = $appRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables["HUB_DB_PATH"] = $databasePath
    $server = [Diagnostics.Process]::new()
    $server.StartInfo = $startInfo
    if (-not $server.Start()) { throw "Unable to start the isolated test server." }
    $serverStarted = $true
    $stdoutTask = $server.StandardOutput.ReadToEndAsync()
    $stderrTask = $server.StandardError.ReadToEndAsync()
    Write-Host "Started isolated API server at $baseUrl (PID $($server.Id))."

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $health = $null
    while ($timer.Elapsed.TotalSeconds -lt 30) {
        if ($server.HasExited) { throw "Test server exited before readiness." }
        try {
            $probe = Read-Probe -Path "/api/health"
            if ($probe.Status -eq 200) {
                $candidate = ConvertFrom-Json -InputObject $probe.Text
                if ($candidate.status -eq "ok") { $health = $candidate; break }
            }
        }
        catch { Write-Verbose $_.Exception.Message }
        Start-Sleep -Milliseconds 200
    }
    if ($null -eq $health) { throw "Test server did not become ready within 30 seconds." }
    Assert-Smoke "Fresh database migrated and seeded" (
        $health.users -eq 5 -and $health.applications -eq 2 -and
        $health.requests -eq 0 -and $health.audit_events -eq 0 -and
        $health.migration -eq "001_initial" -and (Test-Path -LiteralPath $databasePath))

    $page = Read-Probe -Path "/"
    Assert-Smoke "Dashboard HTML, trace and CSP are served" (
        $page.Status -eq 200 -and $page.ContentType -match "^text/html" -and
        $page.Text -match 'id="user-select"' -and $page.HasTrace -and $page.HasCsp)
    $javascript = Read-Probe -Path "/app.js"
    Assert-Smoke "Frontend JavaScript is served" (
        $javascript.Status -eq 200 -and $javascript.ContentType -match "^(text|application)/(java|ecma)script")
    $stylesheet = Read-Probe -Path "/styles.css"
    Assert-Smoke "Frontend stylesheet is served" (
        $stylesheet.Status -eq 200 -and $stylesheet.ContentType -match "^text/css")
    $favicon = Read-Probe -Path "/favicon.svg"
    Assert-Smoke "Favicon is served" ($favicon.Status -eq 200 -and $favicon.ContentType -match "^image/svg\+xml")
    $missing = Read-Probe -Path "/api/not-an-endpoint-step11"
    $missingBody = ConvertFrom-Json -InputObject $missing.Text
    Assert-Smoke "Unknown API route retains JSON 404" (
        $missing.Status -eq 404 -and $missingBody.error.code -eq "not_found" -and $missing.HasTrace)

    Write-Host "Running the existing API regression checks on the temporary database..."
    & $apiScript -BaseUrl $baseUrl
    $after = ConvertFrom-Json -InputObject (Read-Probe -Path "/api/health").Text
    Assert-Smoke "API regression created demo records without changing seed counts" (
        $after.users -eq 5 -and $after.applications -eq 2 -and
        $after.requests -ge 5 -and $after.audit_events -ge $after.requests)
    $finished = $true
}
finally {
    try {
        if ($serverStarted) {
            if (-not $server.HasExited) { $server.Kill() }
            if ($server.WaitForExit(10000)) {
                $stdout = $stdoutTask.GetAwaiter().GetResult()
                $stderr = $stderrTask.GetAwaiter().GetResult()
                if (-not $finished) {
                    Write-Host "Test server stdout:"
                    Write-Host $stdout
                    Write-Host "Test server stderr:"
                    Write-Host $stderr
                }
            }
            else { Write-Warning "Test server PID $($server.Id) has not exited. Temporary files may remain." }
        }
    }
    catch { Write-Warning "Test server cleanup: $($_.Exception.Message)" }
    finally {
        $script:Step11Client.Dispose()
        if ($null -ne $server) { $server.Dispose() }
    }
    if (Test-Path -LiteralPath $runDirectory) {
        try { [IO.Directory]::Delete($runDirectory, $true) }
        catch { Write-Warning "Temporary test directory remains: $runDirectory" }
    }
}

if ($finished) {
    Write-Host ""
    Write-Host "Step 11 automated verification completed."
    Write-Host "C# integration checks: PASSED"
    Write-Host "API regression checks: PASSED"
    Write-Host "Hosting/database smoke checks: $script:SmokePassed passed"
    Write-Host "Frontend checks: $frontendStatus"
    Write-Host "Record actual output and complete manual browser QA before finalizing Phase 1."
}
