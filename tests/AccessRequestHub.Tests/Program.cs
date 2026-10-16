using AccessRequestHub.Domain;
using AccessRequestHub.Infrastructure;
using Microsoft.Data.Sqlite;

var tests = new (string Name, Func<Task> Run)[]
{
    ("migration and seed are reproducible", MigrationAndSeed),
    ("concurrent migration applies schema once", ConcurrentMigration),
    ("failed migration rolls back schema and seed", MigrationRollback),
    ("standard request is approved by manager", StandardFlow),
    ("production request requires system owner", ProductionFlow),
    ("admin access requires application owner", AdminFlow),
    ("unauthorized approval is forbidden", UnauthorizedApproval),
    ("requester cannot self approve as owner", SelfApproval),
    ("reject requires reason and is terminal", RejectFlow),
    ("assigned owner can reject with an audit reason", OwnerReject),
    ("idempotent replay returns one request", IdempotentReplay),
    ("idempotency payload conflict is rejected", IdempotencyConflict),
    ("another actor cannot reuse an idempotency key", IdempotencyActorConflict),
    ("concurrent submit creates one business row", ConcurrentSubmit),
    ("concurrent decision permits one transition", ConcurrentDecision),
    ("concurrent approve versus reject permits one decision", ConcurrentApproveReject),
    ("audit is append-only", AuditAppendOnly),
    ("audit failure rolls back business transition", AuditRollback),
    ("audit failure rolls back request creation", AuditCreateRollback),
    ("foreign keys and immutable request facts are enforced", DatabaseConstraints),
    ("policy and assigned approver snapshots survive master changes", PolicyAndSnapshots),
    ("invalid business input creates no request or audit", InvalidInput),
    ("auditor visibility does not grant approval permission", AuditorVisibility)
};

var passed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        passed++;
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"FAIL  {test.Name}\n      {exception}");
    }
}

Console.WriteLine($"\n{passed}/{tests.Length} tests passed.");
return passed == tests.Length ? 0 : 1;

static async Task MigrationAndSeed()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "preserved", "crm", "NonProduction", "Read");
    await context.Database.MigrateAsync();
    var reopened = new HubDatabase(context.Database.DatabasePath, TestContext.FindMigration());
    await reopened.MigrateAsync();
    var service = new HubService(reopened);
    var bootstrap = await service.BootstrapAsync("alice");
    Equal(5, bootstrap.Users.Count);
    Equal(2, bootstrap.Applications.Count);
    Equal("v1", bootstrap.PolicyVersion);
    Equal(1L, await context.CountAsync("schema_migrations"));
    Equal(created.Request.Id, (await service.ListAsync("alice", "mine")).Single().Id);
    Equal(1L, await context.CountAsync("audit_events"));
}

static async Task ConcurrentMigration()
{
    await using var context = await TestContext.CreateAsync(migrate: false);
    var second = new HubDatabase(context.Database.DatabasePath, TestContext.FindMigration());
    _ = await InParallelAsync(
        async () => { await context.Database.MigrateAsync(); return true; },
        async () => { await second.MigrateAsync(); return true; });
    Equal(5L, await context.CountAsync("users"));
    Equal(2L, await context.CountAsync("applications"));
    Equal(1L, await context.CountAsync("schema_migrations"));
}

static async Task MigrationRollback()
{
    await using var context = await TestContext.CreateAsync(migrate: false);
    var brokenPath = Path.Combine(Path.GetDirectoryName(context.Database.DatabasePath)!, "broken.sql");
    var schema = await File.ReadAllTextAsync(TestContext.FindMigration());
    await File.WriteAllTextAsync(brokenPath, schema + "\nTHIS IS NOT VALID SQL;\n");
    var broken = new HubDatabase(context.Database.DatabasePath, brokenPath);
    await ExpectSqliteAsync(() => broken.MigrateAsync());
    Equal(0L, await context.ScalarAsync(
        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('users','applications','access_requests','audit_events','schema_migrations');"));
    await context.Database.MigrateAsync();
    Equal(5L, await context.CountAsync("users"));
    Equal(1L, await context.CountAsync("schema_migrations"));
}

static async Task StandardFlow()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "standard", "crm", "NonProduction", "Read");
    Equal("AwaitingManager", created.Request.Status);
    var approved = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("Approved", approved.Status);
    Equal(2, approved.Version);
    Equal(2, approved.Audit!.Count);
}

static async Task ProductionFlow()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "production", "crm", "Production", "Read");
    var manager = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("AwaitingOwner", manager.Status);
    Equal("Carol", manager.AssignedApprover);
    var owner = await context.Hub.DecideAsync("carol", created.Request.Id, "approve", new(2, null));
    Equal("Approved", owner.Status);
    Equal(3, owner.Audit!.Count);
}

static async Task AdminFlow()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "admin", "finance", "NonProduction", "Admin");
    var manager = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("AwaitingOwner", manager.Status);
    await ExpectApiAsync(403, "forbidden", () =>
        context.Hub.DecideAsync("carol", created.Request.Id, "approve", new(2, null)));
    var owner = await context.Hub.DecideAsync("dana", created.Request.Id, "approve", new(2, null));
    Equal("Approved", owner.Status);
}

static async Task UnauthorizedApproval()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "unauthorized", "crm", "NonProduction", "Read");
    await ExpectApiAsync(403, "forbidden", () =>
        context.Hub.DecideAsync("carol", created.Request.Id, "approve", new(1, null)));
    var detail = await context.Hub.DetailAsync("alice", created.Request.Id);
    Equal("AwaitingManager", detail.Status);
    Equal(1, detail.Audit!.Count);
}

static async Task SelfApproval()
{
    await using var context = await TestContext.CreateAsync();
    await context.ExecuteAsync("INSERT INTO applications(id,name,owner_id) VALUES('self','Self Service','alice');");
    var created = await CreateAsync(context, "self-approval", "self", "Production", "Read");
    var manager = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("AwaitingOwner", manager.Status);
    await ExpectApiAsync(403, "forbidden", () =>
        context.Hub.DecideAsync("alice", created.Request.Id, "approve", new(2, null)));
}

static async Task RejectFlow()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "reject", "crm", "NonProduction", "Read");
    await ExpectApiAsync(422, "validation", () =>
        context.Hub.DecideAsync("bob", created.Request.Id, "reject", new(1, " ")));
    var rejected = await context.Hub.DecideAsync("bob", created.Request.Id, "reject", new(1, "Scope terlalu luas"));
    Equal("Rejected", rejected.Status);
    Equal("Scope terlalu luas", rejected.Audit!.Last().Reason);
    await ExpectApiAsync(409, "terminal_state", () =>
        context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(2, null)));
}

static async Task OwnerReject()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "owner-reject", "crm", "Production", "Read");
    await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    var rejected = await context.Hub.DecideAsync("carol", created.Request.Id, "reject", new(2, "Scope too broad"));
    Equal("Rejected", rejected.Status);
    Equal(3, rejected.Version);
    Equal(3, rejected.Audit!.Count);
    Equal("carol", rejected.Audit.Last().ActorId);
    Equal("Scope too broad", rejected.Audit.Last().Reason);
    await ExpectApiAsync(409, "terminal_state", () =>
        context.Hub.DecideAsync("carol", created.Request.Id, "approve", new(3, null)));
}

static async Task IdempotentReplay()
{
    await using var context = await TestContext.CreateAsync();
    var first = await CreateAsync(context, "same-key", "crm", "NonProduction", "Read");
    var second = await CreateAsync(context, "same-key", "crm", "NonProduction", "Read");
    False(first.Replayed);
    True(second.Replayed);
    Equal(first.Request.Id, second.Request.Id);
    var mine = await context.Hub.ListAsync("alice", "mine");
    Equal(1, mine.Count);
    Equal(1L, await context.CountAsync("audit_events"));
    await context.Hub.DecideAsync("bob", first.Request.Id, "approve", new(1, null));
    var replayAfterApproval = await CreateAsync(context, "same-key", "crm", "NonProduction", "Read");
    True(replayAfterApproval.Replayed);
    Equal("Approved", replayAfterApproval.Request.Status);
    Equal(2L, await context.CountAsync("audit_events"));
}

static async Task IdempotencyConflict()
{
    await using var context = await TestContext.CreateAsync();
    _ = await CreateAsync(context, "conflict-key", "crm", "NonProduction", "Read");
    await ExpectApiAsync(409, "idempotency_conflict", () =>
        CreateAsync(context, "conflict-key", "finance", "NonProduction", "Read"));
}

static async Task IdempotencyActorConflict()
{
    await using var context = await TestContext.CreateAsync();
    var command = new CreateRequestCommand("actor-key", "crm", "NonProduction", "Read", "Business reporting");
    await context.Hub.CreateAsync("alice", command);
    await ExpectApiAsync(409, "idempotency_conflict", () => context.Hub.CreateAsync("bob", command));
    Equal(1L, await context.CountAsync("access_requests"));
    Equal(1L, await context.CountAsync("audit_events"));
}

static async Task ConcurrentSubmit()
{
    await using var context = await TestContext.CreateAsync();
    var command = new CreateRequestCommand("concurrent-create", "crm", "NonProduction", "Read", "Business reporting");
    var results = await InParallelAsync(
        () => context.Hub.CreateAsync("alice", command),
        () => context.Hub.CreateAsync("alice", command));
    Equal(results[0].Request.Id, results[1].Request.Id);
    Equal(1, results.Count(result => !result.Replayed));
    Equal(1, results.Count(result => result.Replayed));
    Equal(1, (await context.Hub.ListAsync("alice", "mine")).Count);
    Equal(1L, await context.CountAsync("audit_events"));
}

static async Task ConcurrentDecision()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "concurrent-decision", "crm", "NonProduction", "Read");
    var outcomes = await InParallelAsync(
        () => CaptureAsync(() => context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null))),
        () => CaptureAsync(() => context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null))));
    Equal(1, outcomes.Count(item => item is RequestView));
    Equal(1, outcomes.Count(item => item is ApiException { StatusCode: 409, Code: "stale_version" }));
    var detail = await context.Hub.DetailAsync("alice", created.Request.Id);
    Equal("Approved", detail.Status);
    Equal(2, detail.Version);
    Equal(2, detail.Audit!.Count);
}

static async Task ConcurrentApproveReject()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "mixed-decision", "crm", "NonProduction", "Read");
    var outcomes = await InParallelAsync(
        () => CaptureAsync(() => context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null))),
        () => CaptureAsync(() => context.Hub.DecideAsync("bob", created.Request.Id, "reject", new(1, "Not required"))));
    Equal(1, outcomes.Count(item => item is RequestView));
    Equal(1, outcomes.Count(item => item is ApiException { StatusCode: 409, Code: "stale_version" }));
    var winner = outcomes.OfType<RequestView>().Single();
    var detail = await context.Hub.DetailAsync("alice", created.Request.Id);
    Equal(winner.Status, detail.Status);
    True(detail.Status is "Approved" or "Rejected");
    Equal(2, detail.Version);
    Equal(2, detail.Audit!.Count);
    Equal(detail.Status, detail.Audit.Last().ToStatus);
    Equal(detail.Status == "Rejected" ? "Not required" : null, detail.Audit.Last().Reason);
}

static async Task AuditAppendOnly()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "append-only", "crm", "NonProduction", "Read");
    await ExpectSqliteAsync(() => context.ExecuteAsync(
        $"UPDATE audit_events SET action='rejected' WHERE request_id='{created.Request.Id}';"));
    await ExpectSqliteAsync(() => context.ExecuteAsync(
        $"DELETE FROM audit_events WHERE request_id='{created.Request.Id}';"));
    Equal(1L, await context.CountAsync("audit_events"));
}

static async Task AuditRollback()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "audit-rollback", "crm", "NonProduction", "Read");
    await context.ExecuteAsync("""
        CREATE TRIGGER fail_approved_audit
        BEFORE INSERT ON audit_events
        WHEN NEW.action='approved'
        BEGIN
            SELECT RAISE(ABORT, 'injected audit failure');
        END;
        """);
    await ExpectSqliteAsync(() =>
        context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null)));
    var detail = await context.Hub.DetailAsync("alice", created.Request.Id);
    Equal("AwaitingManager", detail.Status);
    Equal(1, detail.Version);
    Equal(1, detail.Audit!.Count);
    await context.ExecuteAsync("DROP TRIGGER fail_approved_audit;");
    var recovered = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("Approved", recovered.Status);
    Equal(2, recovered.Audit!.Count);
}

static async Task AuditCreateRollback()
{
    await using var context = await TestContext.CreateAsync();
    await context.ExecuteAsync("""
        CREATE TRIGGER fail_submitted_audit
        BEFORE INSERT ON audit_events
        WHEN NEW.action='submitted'
        BEGIN
            SELECT RAISE(ABORT, 'injected create audit failure');
        END;
        """);
    await ExpectSqliteAsync(() => CreateAsync(context, "create-rollback", "crm", "NonProduction", "Read"));
    Equal(0L, await context.CountAsync("access_requests"));
    Equal(0L, await context.CountAsync("audit_events"));
    await context.ExecuteAsync("DROP TRIGGER fail_submitted_audit;");
    var recovered = await CreateAsync(context, "create-rollback", "crm", "NonProduction", "Read");
    False(recovered.Replayed);
    Equal(1, recovered.Request.Audit!.Count);
}

static async Task DatabaseConstraints()
{
    await using var context = await TestContext.CreateAsync();
    Equal(1L, await context.ScalarAsync("PRAGMA foreign_keys;"));
    await ExpectSqliteAsync(() => context.ExecuteAsync(
        "INSERT INTO applications(id,name,owner_id) VALUES('invalid','Invalid','missing-user');"));
    var created = await CreateAsync(context, "immutable", "crm", "NonProduction", "Read");
    // ID is generated by the application; these statements execute only in this isolated fixture.
    await ExpectSqliteAsync(() => context.ExecuteAsync(
        $"UPDATE access_requests SET justification='Changed',version=2 WHERE id='{created.Request.Id}';"));
    await ExpectSqliteAsync(() => context.ExecuteAsync(
        $"UPDATE access_requests SET status='AwaitingOwner',version=2,last_actor_id='bob',last_action='approved' WHERE id='{created.Request.Id}';"));
    var detail = await context.Hub.DetailAsync("alice", created.Request.Id);
    Equal("Business reporting", detail.Justification);
    Equal("AwaitingManager", detail.Status);
    Equal(1, detail.Version);
    Equal(1, detail.Audit!.Count);
}

static async Task PolicyAndSnapshots()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "snapshots", "crm", "Production", "Read");
    await context.ExecuteAsync("UPDATE users SET manager_id='dana' WHERE id='alice';");
    await context.ExecuteAsync("UPDATE applications SET owner_id='dana' WHERE id='crm';");
    await ExpectApiAsync(403, "forbidden", () =>
        context.Hub.DecideAsync("dana", created.Request.Id, "approve", new(1, null)));
    var manager = await context.Hub.DecideAsync("bob", created.Request.Id, "approve", new(1, null));
    Equal("bob", manager.ManagerId);
    Equal("carol", manager.OwnerId);
    Equal("v1", manager.PolicyVersion);
    var owner = await context.Hub.DecideAsync("carol", created.Request.Id, "approve", new(2, null));
    Equal("Approved", owner.Status);
    True(owner.Audit!.All(item => item.PolicyVersion == "v1"));
}

static async Task InvalidInput()
{
    await using var context = await TestContext.CreateAsync();
    await ExpectApiAsync(422, "validation", () =>
        context.Hub.CreateAsync("alice", new("invalid-blank", "crm", "NonProduction", "Read", "   ")));
    await ExpectApiAsync(422, "validation", () =>
        context.Hub.CreateAsync("alice", new("invalid-env", "crm", "production", "Read", "Business reporting")));
    await ExpectApiAsync(401, "unauthenticated", () =>
        context.Hub.CreateAsync("unknown", new("invalid-actor", "crm", "NonProduction", "Read", "Business reporting")));
    Equal(0L, await context.CountAsync("access_requests"));
    Equal(0L, await context.CountAsync("audit_events"));
}

static async Task AuditorVisibility()
{
    await using var context = await TestContext.CreateAsync();
    var created = await CreateAsync(context, "visibility", "crm", "NonProduction", "Read");
    Equal(1, (await context.Hub.ListAsync("erin", "all")).Count);
    False((await context.Hub.DetailAsync("erin", created.Request.Id)).CanAct);
    await ExpectApiAsync(403, "forbidden", () => context.Hub.ListAsync("alice", "all"));
    await ExpectApiAsync(403, "forbidden", () => context.Hub.DetailAsync("dana", created.Request.Id));
    await ExpectApiAsync(403, "forbidden", () =>
        context.Hub.DecideAsync("erin", created.Request.Id, "approve", new(1, null)));
}

static async Task<T[]> InParallelAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
{
    // Microsoft.Data.Sqlite async commands execute synchronously. Task.Run +
    // a shared start gate avoids a sequential-only Task.WhenAll check.
    var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var arrived = 0;
    async Task<T> Worker(Func<Task<T>> action)
    {
        if (Interlocked.Increment(ref arrived) == 2) ready.TrySetResult(true);
        await start.Task;
        return await action();
    }
    var one = Task.Run(() => Worker(first));
    var two = Task.Run(() => Worker(second));
    await ready.Task;
    start.SetResult(true);
    return await Task.WhenAll(one, two);
}

static Task<CreateOutcome> CreateAsync(
    TestContext context,
    string clientId,
    string application,
    string environment,
    string access) =>
    context.Hub.CreateAsync(
        "alice",
        new(clientId, application, environment, access, "Business reporting"));

static async Task<object> CaptureAsync(Func<Task<RequestView>> action)
{
    try
    {
        return await action();
    }
    catch (ApiException exception)
    {
        return exception;
    }
}

static async Task ExpectApiAsync(int status, string code, Func<Task> action)
{
    try
    {
        await action();
        throw new Exception($"Expected ApiException {status}/{code}.");
    }
    catch (ApiException exception)
    {
        Equal(status, exception.StatusCode);
        Equal(code, exception.Code);
    }
}

static async Task ExpectSqliteAsync(Func<Task> action)
{
    try
    {
        await action();
        throw new Exception("Expected SqliteException.");
    }
    catch (SqliteException)
    {
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"Expected <{expected}> but was <{actual}>.");
    }
}

static void True(bool value)
{
    if (!value) throw new Exception("Expected true.");
}

static void False(bool value)
{
    if (value) throw new Exception("Expected false.");
}

sealed class TestContext : IAsyncDisposable
{
    private readonly string _directory;

    private TestContext(string directory, HubDatabase database)
    {
        _directory = directory;
        Database = database;
        Hub = new HubService(database);
    }

    public HubDatabase Database { get; }
    public HubService Hub { get; }

    public static async Task<TestContext> CreateAsync(bool migrate = true)
    {
        var directory = Path.Combine(Path.GetTempPath(), "accesshub-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = new HubDatabase(
            Path.Combine(directory, "test.sqlite3"),
            FindMigration());
        var context = new TestContext(directory, database);
        try
        {
            if (migrate) await database.MigrateAsync();
            return context;
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    public Task<long> CountAsync(string table)
    {
        if (table is not ("schema_migrations" or "users" or "applications" or "access_requests" or "audit_events"))
            throw new ArgumentException("Unexpected test table.", nameof(table));
        return ScalarAsync($"SELECT COUNT(*) FROM {table};");
    }

    public async Task<long> ScalarAsync(string sql)
    {
        await using var connection = await Database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cleanup pending: {_directory}. {exception.Message}");
        }

        return ValueTask.CompletedTask;
    }

    public static string FindMigration()
    {
        var copiedMigration = Path.Combine(AppContext.BaseDirectory, "migrations", "001_initial.sql");
        if (File.Exists(copiedMigration)) return copiedMigration;
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "AccessRequestHub", "migrations", "001_initial.sql");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("migrations/001_initial.sql tidak ditemukan. Jalankan test dari repository.");
    }
}

