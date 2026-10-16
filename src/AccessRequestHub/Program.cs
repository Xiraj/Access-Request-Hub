using AccessRequestHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var configuredPath = Environment.GetEnvironmentVariable("HUB_DB_PATH")
    ?? builder.Configuration["Hub:DatabasePath"]
    ?? "data/hub.sqlite3";

var databasePath = Path.IsPathRooted(configuredPath)
    ? configuredPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredPath);

var migrationPath = Path.Combine(
    builder.Environment.ContentRootPath,
    "migrations",
    "001_initial.sql");

builder.Services.AddSingleton(new HubDatabase(databasePath, migrationPath));

var app = builder.Build();

// Schema and seed are ready before the application starts accepting requests.
var database = app.Services.GetRequiredService<HubDatabase>();
await database.MigrateAsync();

app.Logger.LogInformation("SQLite migration and seed ready. Database: {Path}", database.DatabasePath);

// Stage 8 exposes a database readiness check only. Workflow endpoints follow in stage 9.
app.MapGet("/", () => Results.Redirect("/api/health"));

app.MapGet("/api/health", async (HubDatabase db, CancellationToken cancellationToken) =>
{
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = connection.CreateCommand();

    command.CommandText = """
        SELECT
            (SELECT COUNT(*) FROM users),
            (SELECT COUNT(*) FROM applications),
            (SELECT COUNT(*) FROM access_requests),
            (SELECT COUNT(*) FROM audit_events),
            (SELECT version FROM schema_migrations WHERE version='001_initial');
        """;

    await using var reader = await command.ExecuteReaderAsync(cancellationToken);

    if (!await reader.ReadAsync(cancellationToken))
    {
        throw new InvalidOperationException("Database readiness query tidak mengembalikan hasil.");
    }

    return Results.Ok(new
    {
        status = "ok",
        database = "SQLite",
        users = reader.GetInt64(0),
        applications = reader.GetInt64(1),
        requests = reader.GetInt64(2),
        audit_events = reader.GetInt64(3),
        migration = reader.IsDBNull(4) ? null : reader.GetString(4)
    });
});

app.Run();
