using System.Globalization;
using Microsoft.Data.Sqlite;
using AccessRequestHub.Infrastructure;

namespace AccessRequestHub.Domain;

public sealed class HubService(HubDatabase database)
{
    public const string PolicyVersion = "v1";

    private const string SelectRequest = """
        SELECT r.id,r.client_request_id,r.requester_id,r.manager_id,r.owner_id,
               r.application_id,a.name AS application_name,u.name AS requester_name,
               u.email AS requester_email,m.name AS manager_name,o.name AS owner_name,
               r.environment,r.access_level,r.justification,r.status,r.policy_version,
               r.version,r.created_at,r.updated_at
        FROM access_requests r
        JOIN applications a ON a.id=r.application_id
        JOIN users u ON u.id=r.requester_id
        JOIN users m ON m.id=r.manager_id
        JOIN users o ON o.id=r.owner_id
        """;

    public async Task<UserView> GetUserAsync(string? userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ApiException(401, "unauthenticated", "Pilih demo user yang valid.");
        }

        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        var user = await FindUserAsync(connection, userId, cancellationToken);
        return user ?? throw new ApiException(401, "unauthenticated", "Pilih demo user yang valid.");
    }

    public async Task<BootstrapView> BootstrapAsync(string actorId, CancellationToken cancellationToken = default)
    {
        _ = await GetUserAsync(actorId, cancellationToken);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);

        var users = new List<UserView>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,email,role,manager_id FROM users ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(ReadUser(reader));
            }
        }

        var applications = new List<ApplicationView>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,owner_id FROM applications ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applications.Add(new ApplicationView(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        return new BootstrapView(
            users,
            applications,
            ["NonProduction", "Production"],
            ["Read", "Admin"],
            PolicyVersion);
    }

    public async Task<IReadOnlyList<RequestView>> ListAsync(
        string actorId,
        string scope,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetUserAsync(actorId, cancellationToken);
        if (scope is not ("mine" or "inbox" or "all"))
        {
            throw new ApiException(422, "validation", "Scope tidak valid.");
        }

        if (scope == "all" && actor.Role != "Auditor")
        {
            throw new ApiException(403, "forbidden", "Hanya auditor boleh melihat semua permintaan.");
        }

        var sql = SelectRequest;
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        if (scope == "mine")
        {
            sql += " WHERE r.requester_id=$actor";
            command.Parameters.AddWithValue("$actor", actorId);
        }
        else if (scope == "inbox")
        {
            sql += """
                 WHERE r.requester_id<>$actor
                   AND ((r.status='AwaitingManager' AND r.manager_id=$actor)
                     OR (r.status='AwaitingOwner' AND r.owner_id=$actor))
                """;
            command.Parameters.AddWithValue("$actor", actorId);
        }

        command.CommandText = sql + " ORDER BY r.created_at DESC,r.id;";
        var rows = new List<RequestView>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ToView(ReadRequest(reader), actorId));
        }

        return rows;
    }

    public async Task<RequestView> DetailAsync(
        string actorId,
        string requestId,
        CancellationToken cancellationToken = default)
    {
        var actor = await GetUserAsync(actorId, cancellationToken);
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        // A read-only transaction keeps request state and audit on one snapshot.
        using var transaction = connection.BeginTransaction(deferred: true);

        var row = await FindRequestAsync(connection, requestId, transaction, cancellationToken)
            ?? throw new ApiException(404, "not_found", "Permintaan tidak ditemukan.");

        if (actor.Role != "Auditor" && actorId != row.RequesterId && actorId != row.ManagerId && actorId != row.OwnerId)
        {
            throw new ApiException(403, "forbidden", "Anda tidak berhak melihat permintaan ini.");
        }

        var audit = new List<AuditEventView>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT e.id,e.request_id,e.actor_id,u.name,e.action,e.from_status,e.to_status,
                       e.reason,e.request_version,e.policy_version,e.occurred_at
                FROM audit_events e
                JOIN users u ON u.id=e.actor_id
                WHERE e.request_id=$request_id
                ORDER BY e.id;
                """;
            command.Parameters.AddWithValue("$request_id", requestId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                audit.Add(new AuditEventView(
                    reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt32(8), reader.GetString(9),
                    reader.GetString(10)));
            }
        }

        transaction.Commit();
        return ToView(row, actorId, audit);
    }

    public async Task<CreateOutcome> CreateAsync(
        string actorId,
        CreateRequestCommand payload,
        CancellationToken cancellationToken = default)
    {
        _ = await GetUserAsync(actorId, cancellationToken);
        var clientRequestId = RequiredText(payload.ClientRequestId, "client_request_id", 100);
        var applicationId = RequiredText(payload.ApplicationId, "application_id", 50);
        var justification = RequiredText(payload.Justification, "justification", 2000);
        var environment = RequiredEnum(payload.Environment, "environment", "NonProduction", "Production");
        var accessLevel = RequiredEnum(payload.AccessLevel, "access_level", "Read", "Admin");

        var transactionResult = await database.InImmediateTransactionAsync(async (connection, transaction) =>
        {
            var existing = await FindRequestByClientIdAsync(connection, clientRequestId, transaction, cancellationToken);
            if (existing is not null)
            {
                var identical = existing.RequesterId == actorId
                    && existing.ApplicationId == applicationId
                    && existing.Environment == environment
                    && existing.AccessLevel == accessLevel
                    && existing.Justification == justification;
                if (!identical)
                {
                    throw new ApiException(
                        409,
                        "idempotency_conflict",
                        "ClientRequestId sudah digunakan dengan user atau isi berbeda.");
                }

                return (existing.Id, true);
            }

            var actor = await FindUserAsync(connection, actorId, cancellationToken, transaction)
                ?? throw new ApiException(401, "unauthenticated", "Pilih demo user yang valid.");

            if (actor.ManagerId is null)
            {
                throw new ApiException(422, "validation", "User belum memiliki manager. Gunakan Alice untuk demo create.");
            }

            var ownerId = await FindApplicationOwnerAsync(connection, applicationId, transaction, cancellationToken)
                ?? throw new ApiException(422, "validation", "Aplikasi tidak valid.");

            var id = Guid.NewGuid().ToString();
            var timestamp = UtcNow();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO access_requests(
                    id,client_request_id,requester_id,manager_id,owner_id,application_id,
                    environment,access_level,justification,status,policy_version,version,
                    created_at,updated_at,last_actor_id,last_action,last_reason)
                VALUES(
                    $id,$client_id,$requester,$manager,$owner,$application,
                    $environment,$access,$justification,'AwaitingManager',$policy,1,
                    $created,$updated,$actor,'submitted',NULL);
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$client_id", clientRequestId);
            command.Parameters.AddWithValue("$requester", actorId);
            command.Parameters.AddWithValue("$manager", actor.ManagerId);
            command.Parameters.AddWithValue("$owner", ownerId);
            command.Parameters.AddWithValue("$application", applicationId);
            command.Parameters.AddWithValue("$environment", environment);
            command.Parameters.AddWithValue("$access", accessLevel);
            command.Parameters.AddWithValue("$justification", justification);
            command.Parameters.AddWithValue("$policy", PolicyVersion);
            command.Parameters.AddWithValue("$created", timestamp);
            command.Parameters.AddWithValue("$updated", timestamp);
            command.Parameters.AddWithValue("$actor", actorId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return (id, false);
        }, cancellationToken);

        var detail = await DetailAsync(actorId, transactionResult.Item1, cancellationToken);
        return new CreateOutcome(detail, transactionResult.Item2);
    }

    public async Task<RequestView> DecideAsync(
        string actorId,
        string requestId,
        string action,
        DecisionCommand payload,
        CancellationToken cancellationToken = default)
    {
        _ = await GetUserAsync(actorId, cancellationToken);
        if (action is not ("approve" or "reject"))
        {
            throw new ApiException(404, "not_found", "Action tidak ditemukan.");
        }

        if (payload.Version < 1)
        {
            throw new ApiException(422, "validation", "version harus integer positif.");
        }

        string? reason = null;
        if (action == "reject")
        {
            reason = RequiredText(payload.Reason, "reason", 2000);
        }
        else if (payload.Reason is not null)
        {
            throw new ApiException(422, "validation", "Reason hanya untuk rejection.");
        }

        await database.InImmediateTransactionAsync(async (connection, transaction) =>
        {
            var row = await FindRequestAsync(connection, requestId, transaction, cancellationToken)
                ?? throw new ApiException(404, "not_found", "Permintaan tidak ditemukan.");

            if (actorId == row.RequesterId || (actorId != row.ManagerId && actorId != row.OwnerId))
            {
                throw new ApiException(403, "forbidden", "Anda bukan approver yang ditugaskan; self-approval dilarang.");
            }

            if (row.Version != payload.Version)
            {
                throw new ApiException(
                    409,
                    "stale_version",
                    "Data telah berubah. Muat ulang sebelum mengambil keputusan.",
                    new { current_version = row.Version });
            }

            if (row.Status is "Approved" or "Rejected")
            {
                throw new ApiException(409, "terminal_state", "Permintaan sudah final dan tidak dapat diproses lagi.");
            }

            if (!CanAct(actorId, row))
            {
                throw new ApiException(403, "forbidden", "Anda bukan approver untuk tahap saat ini.");
            }

            var highRisk = row.Environment == "Production" || row.AccessLevel == "Admin";
            var status = action == "reject"
                ? "Rejected"
                : row.Status == "AwaitingManager" && highRisk
                    ? "AwaitingOwner"
                    : "Approved";

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE access_requests
                SET status=$status,
                    version=version+1,
                    updated_at=$updated,
                    last_actor_id=$actor,
                    last_action=$action,
                    last_reason=$reason
                WHERE id=$id AND version=$version;
                """;
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$updated", UtcNow());
            command.Parameters.AddWithValue("$actor", actorId);
            command.Parameters.AddWithValue("$action", action == "approve" ? "approved" : "rejected");
            command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            command.Parameters.AddWithValue("$id", requestId);
            command.Parameters.AddWithValue("$version", payload.Version);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                throw new ApiException(409, "stale_version", "Data telah berubah. Muat ulang permintaan.");
            }

            return true;
        }, cancellationToken);

        return await DetailAsync(actorId, requestId, cancellationToken);
    }

    private static string RequiredText(string? value, string field, int maximum)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > maximum)
        {
            throw new ApiException(
                422,
                "validation",
                $"{field} wajib diisi (maksimum {maximum} karakter).",
                new Dictionary<string, string> { [field] = "invalid" });
        }

        return trimmed;
    }

    private static string RequiredEnum(string? value, string field, params string[] allowed)
    {
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
        {
            throw new ApiException(422, "validation", $"{field} tidak valid.");
        }

        return value;
    }

    private static bool CanAct(string actorId, RequestRow row) =>
        actorId != row.RequesterId
        && ((row.Status == "AwaitingManager" && actorId == row.ManagerId)
            || (row.Status == "AwaitingOwner" && actorId == row.OwnerId));

    private static RequestView ToView(
        RequestRow row,
        string actorId,
        IReadOnlyList<AuditEventView>? audit = null)
    {
        var highRisk = row.Environment == "Production" || row.AccessLevel == "Admin";
        var assigned = row.Status switch
        {
            "AwaitingManager" => row.ManagerName,
            "AwaitingOwner" => row.OwnerName,
            _ => null
        };

        return new RequestView(
            row.Id, row.ClientRequestId, row.RequesterId, row.ManagerId, row.OwnerId,
            row.ApplicationId, row.ApplicationName, row.RequesterName, row.RequesterEmail,
            row.ManagerName, row.OwnerName, row.Environment, row.AccessLevel, row.Justification,
            row.Status, row.PolicyVersion, row.Version, row.CreatedAt, row.UpdatedAt,
            highRisk, CanAct(actorId, row), assigned, audit);
    }

    private static async Task<UserView?> FindUserAsync(
        SqliteConnection connection,
        string id,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id,name,email,role,manager_id FROM users WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadUser(reader) : null;
    }

    private static UserView ReadUser(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4));

    private static async Task<string?> FindApplicationOwnerAsync(
        SqliteConnection connection,
        string applicationId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT owner_id FROM applications WHERE id=$id;";
        command.Parameters.AddWithValue("$id", applicationId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<RequestRow?> FindRequestByClientIdAsync(
        SqliteConnection connection,
        string clientRequestId,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SelectRequest + " WHERE r.client_request_id=$client_id;";
        command.Parameters.AddWithValue("$client_id", clientRequestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRequest(reader) : null;
    }

    private static async Task<RequestRow?> FindRequestAsync(
        SqliteConnection connection,
        string requestId,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SelectRequest + " WHERE r.id=$id;";
        command.Parameters.AddWithValue("$id", requestId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRequest(reader) : null;
    }

    private static RequestRow ReadRequest(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
        reader.GetString(8), reader.GetString(9), reader.GetString(10), reader.GetString(11),
        reader.GetString(12), reader.GetString(13), reader.GetString(14), reader.GetString(15),
        reader.GetInt32(16), reader.GetString(17), reader.GetString(18));

    private static string UtcNow() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private sealed record RequestRow(
        string Id,
        string ClientRequestId,
        string RequesterId,
        string ManagerId,
        string OwnerId,
        string ApplicationId,
        string ApplicationName,
        string RequesterName,
        string RequesterEmail,
        string ManagerName,
        string OwnerName,
        string Environment,
        string AccessLevel,
        string Justification,
        string Status,
        string PolicyVersion,
        int Version,
        string CreatedAt,
        string UpdatedAt);
}
