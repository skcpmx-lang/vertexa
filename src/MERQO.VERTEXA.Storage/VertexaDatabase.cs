using Merqo.Vertexa.Shared;
using Microsoft.Data.Sqlite;

namespace Merqo.Vertexa.Storage;

/// <summary>
/// Local SQLite store with forward-only migrations. Everything is per-user; no data
/// leaves the machine. A failed migration leaves the previous schema intact.
/// </summary>
public sealed class VertexaDatabase : IDisposable
{
    private const int CurrentSchemaVersion = 3;
    private readonly IAppLogger _log;
    private readonly string _connectionString;

    public VertexaDatabase(string? dataDirectory = null, IAppLogger? log = null)
    {
        _log = log ?? NullLogger.Instance;
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MERQO", "VERTEXA");
        Directory.CreateDirectory(DataDirectory);
        DatabasePath = Path.Combine(DataDirectory, "vertexa.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public string DataDirectory { get; }
    public string DatabasePath { get; }
    public string BackupDirectory => Path.Combine(DataDirectory, "backups");

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=4000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    public int Migrate()
    {
        using var connection = Open();
        var version = ReadUserVersion(connection);
        if (version >= CurrentSchemaVersion) return version;

        Backup(version);

        using var transaction = connection.BeginTransaction();
        try
        {
            if (version < 1)
            {
                Execute(connection, transaction, """
                    CREATE TABLE IF NOT EXISTS settings (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        json TEXT NOT NULL,
                        updated_at TEXT NOT NULL
                    );
                    """);
            }

            if (version < 2)
            {
                Execute(connection, transaction, """
                    CREATE TABLE IF NOT EXISTS history_runs (
                        run_id TEXT PRIMARY KEY,
                        created_at TEXT NOT NULL,
                        app_version TEXT NOT NULL,
                        machine TEXT NOT NULL,
                        scope TEXT NOT NULL,
                        health_score REAL NULL,
                        health_state TEXT NOT NULL,
                        payload TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS ix_history_created ON history_runs(created_at DESC);
                    """);
            }

            if (version < 3)
            {
                Execute(connection, transaction, """
                    CREATE TABLE IF NOT EXISTS reports_meta (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        run_id TEXT NOT NULL,
                        report_type TEXT NOT NULL,
                        format TEXT NOT NULL,
                        path TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    """);
            }

            Execute(connection, transaction, $"PRAGMA user_version={CurrentSchemaVersion};");
            transaction.Commit();
            _log.Info("storage", $"database migrated from schema {version} to {CurrentSchemaVersion}");
            return CurrentSchemaVersion;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _log.Error("storage", "migration failed; previous schema preserved", ex);
            throw;
        }
    }

    private void Backup(int fromVersion)
    {
        try
        {
            if (!File.Exists(DatabasePath)) return;
            Directory.CreateDirectory(BackupDirectory);
            var target = Path.Combine(BackupDirectory, $"vertexa-v{fromVersion}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
            File.Copy(DatabasePath, target, overwrite: false);
            var old = new DirectoryInfo(BackupDirectory).GetFiles("vertexa-v*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc).Skip(3);
            foreach (var file in old) file.Delete();
        }
        catch (Exception ex)
        {
            _log.Warn("storage", "database backup skipped: " + ex.Message);
        }
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => SqliteConnection.ClearAllPools();
}
