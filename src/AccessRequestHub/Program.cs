using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessRequestHub.Domain;
using AccessRequestHub.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024);

// Binding failures must pass through the same JSON error middleware in every environment.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.MaxDepth = 16;
});

var configuredPath = Environment.GetEnvironmentVariable("HUB_DB_PATH")
    ?? builder.Configuration["Hub:DatabasePath"]
    ?? "data/hub.sqlite3";
var databasePath = Path.IsPathRooted(configuredPath)
    ? configuredPath
    : Path.Combine(builder.Environment.ContentRootPath, configuredPath);
var migrationPath = Path.Combine(builder.Environment.ContentRootPath, "migrations", "001_initial.sql");

builder.Services.AddSingleton(new HubDatabase(databasePath, migrationPath));
builder.Services.AddScoped<HubService>();

var app = builder.Build();
var database = app.Services.GetRequiredService<HubDatabase>();
await database.MigrateAsync();
app.Logger.LogInformation("SQLite ready. Database: {Path}", database.DatabasePath);

app.Use(async (context, next) =>
{
    var requestId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    context.TraceIdentifier = requestId;
    var timer = Stopwatch.StartNew();

    // OnStarting reapplies these headers even when an error clears the response.
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Request-Id"] = requestId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        return Task.CompletedTask;
    });

    try
    {
        await next();
    }
    catch (ApiException exception)
    {
        await WriteErrorAsync(context, exception.StatusCode, exception.Code, exception.Message, exception.Details);
    }
    catch (BadHttpRequestException exception)
    {
        var code = exception.StatusCode switch
        {
            StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
            StatusCodes.Status413PayloadTooLarge => "payload_too_large",
            _ => "invalid_request"
        };
        await WriteErrorAsync(context, exception.StatusCode, code,
            "Body atau request tidak valid. Gunakan JSON dengan field dan tipe data yang sesuai.");
    }
    catch (JsonException)
    {
        await WriteErrorAsync(context, 400, "invalid_json", "JSON tidak valid atau memiliki field yang tidak dikenal.");
    }
    catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
    {
        app.Logger.LogWarning("SQLite lock timeout. RequestId={RequestId}", requestId);
        await WriteErrorAsync(context, 503, "database_busy",
            "Database sedang sibuk. Retry menggunakan ClientRequestId atau version semula.");
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        context.Abort();
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Unhandled API error. RequestId={RequestId}", requestId);
        await WriteErrorAsync(context, 500, "internal_error", "Terjadi kesalahan internal. Gunakan request_id untuk penelusuran.");
    }
    finally
    {
        // No justification, rejection reason, query string or raw headers in logs.
        app.Logger.LogInformation("{Method} {Path} -> {Status} in {ElapsedMs}ms. RequestId={RequestId}",
            context.Request.Method, context.Request.Path.Value,
            context.Response.StatusCode, timer.ElapsedMilliseconds, requestId);
    }
});

app.Use(async (context, next) =>
{
    if (!IsLoopbackHost(context.Request.Host.Host))
    {
        throw new ApiException(403, "forbidden_host", "Host tidak diizinkan untuk demo lokal.");
    }

    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers.ContainsKey("Origin"))
    {
        var origins = context.Request.Headers["Origin"];
        var expected = new Uri($"{context.Request.Scheme}://{context.Request.Host}");
        if (origins.Count != 1
            || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || !IsLoopbackHost(origin.Host)
            || Uri.Compare(origin, expected, UriComponents.SchemeAndServer,
                UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw new ApiException(403, "forbidden_origin", "Origin tidak diizinkan.");
        }
    }

    await next();
});

// Serve wwwroot/index.html at / and its same-origin assets.
// Keep these after the logging/security middleware and before API endpoints.
app.UseDefaultFiles();
app.UseStaticFiles();

// Keep the step 8 health endpoint; counts now increase when test requests are created.
app.MapGet("/api/health", async (HubDatabase db, CancellationToken cancellationToken) =>
{
    await using var connection = await db.OpenConnectionAsync(cancellationToken);
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT (SELECT COUNT(*) FROM users),
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

app.MapGet("/api/bootstrap", async (HttpContext context, HubService hub, CancellationToken cancellationToken) =>
    Results.Ok(new { data = await hub.BootstrapAsync(Actor(context), cancellationToken) }));

app.MapGet("/api/requests", async (
    HttpContext context, HubService hub, string? scope, CancellationToken cancellationToken) =>
    Results.Ok(new { data = await hub.ListAsync(Actor(context), scope ?? "mine", cancellationToken) }));

app.MapGet("/api/requests/{id}", async (
    HttpContext context, HubService hub, string id, CancellationToken cancellationToken) =>
    Results.Ok(new { data = await hub.DetailAsync(Actor(context), id, cancellationToken) }));

app.MapPost("/api/requests", async (
    HttpContext context, HubService hub, CreateRequestCommand payload, CancellationToken cancellationToken) =>
{
    var outcome = await hub.CreateAsync(Actor(context), payload, cancellationToken);
    if (!outcome.Replayed)
    {
        context.Response.Headers["Location"] = $"/api/requests/{outcome.Request.Id}";
    }
    return Results.Json(new { data = outcome.Request, replayed = outcome.Replayed },
        statusCode: outcome.Replayed ? 200 : 201);
});

app.MapPost("/api/requests/{id}/approve", async (
    HttpContext context, HubService hub, string id, DecisionCommand payload, CancellationToken cancellationToken) =>
    Results.Ok(new { data = await hub.DecideAsync(Actor(context), id, "approve", payload, cancellationToken) }));

app.MapPost("/api/requests/{id}/reject", async (
    HttpContext context, HubService hub, string id, DecisionCommand payload, CancellationToken cancellationToken) =>
    Results.Ok(new { data = await hub.DecideAsync(Actor(context), id, "reject", payload, cancellationToken) }));

app.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"],
    (HttpContext context) => Results.Json(
        new ErrorEnvelope(new ErrorDetails("not_found", "Endpoint tidak ditemukan."), context.TraceIdentifier),
        statusCode: 404));

app.Run();

static string Actor(HttpContext context)
{
    var values = context.Request.Headers["X-User-Id"];
    if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
    {
        throw new ApiException(401, "unauthenticated", "Pilih satu demo user yang valid melalui X-User-Id.");
    }
    return values[0]!;
}

static bool IsLoopbackHost(string host) =>
    host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
    || host is "127.0.0.1" or "::1" or "[::1]";

static async Task WriteErrorAsync(
    HttpContext context, int status, string code, string message, object? details = null)
{
    if (context.Response.HasStarted)
    {
        context.Abort();
        return;
    }
    context.Response.Clear();
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(
        new ErrorEnvelope(new ErrorDetails(code, message, details), context.TraceIdentifier));
}

public partial class Program
{
}
