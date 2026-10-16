using Microsoft.Data.Sqlite;

namespace AccessRequestHub.Infrastructure;

/// <summary>
/// Opens a separate connection per operation and applies the initial SQL migration.
/// No external database server is required for this local assessment.
/// </summary>
public sealed class HubDatabase
{
    private const string InitialMigration = "001_initial";
    private readonly string _connectionString;
    private readonly string _migrationPath;

    public HubDatabase(string databasePath, string migrationPath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _migrationPath = Path.GetFullPath(migrationPath);

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 10,
            Pooling = false
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        // Acquire the write lock BEFORE checking migration history. Two application
        // instances starting together cannot both apply the initial schema.
        using var transaction = connection.BeginTransaction(deferred: false);

        await using (var history = connection.CreateCommand())
        {
            history.Transaction = transaction;
            history.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version TEXT PRIMARY KEY,
                    applied_at TEXT NOT NULL
                );
                """;
            await history.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version=$version;";
            check.Parameters.AddWithValue("$version", InitialMigration);

            var alreadyApplied = Convert.ToInt64(
                await check.ExecuteScalarAsync(cancellationToken)) > 0;

            if (alreadyApplied)
            {
                transaction.Commit();
                return;
            }
        }

        if (!File.Exists(_migrationPath))
        {
            throw new FileNotFoundException(
                "Migration SQL tidak ditemukan. Periksa folder migrations.",
                _migrationPath);
        }

        var script = await File.ReadAllTextAsync(_migrationPath, cancellationToken);

        await using (var migration = connection.CreateCommand())
        {
            migration.Transaction = transaction;
            migration.CommandText = script;
            await migration.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText = """
                INSERT INTO schema_migrations (version, applied_at)
                VALUES ($version, $applied_at);
                """;
            record.Parameters.AddWithValue("$version", InitialMigration);
            record.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToString("O"));
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        // Disposing an uncommitted transaction rolls back schema, seed and history.
        transaction.Commit();
    }
}
