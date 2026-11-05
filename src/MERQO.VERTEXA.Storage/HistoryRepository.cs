using Merqo.Vertexa.Shared;
using Microsoft.Data.Sqlite;

namespace Merqo.Vertexa.Storage;

public sealed record HistoryEntry(
    string RunId,
    DateTimeOffset CreatedAt,
    string AppVersion,
    string Machine,
    string Scope,
    double? HealthScore,
    string HealthState);

/// <summary>Diagnostic history: metadata is queryable, the full payload is stored as JSON.</summary>
public sealed class HistoryRepository
{
    private readonly VertexaDatabase _database;
    private readonly IAppLogger _log;

    public HistoryRepository(VertexaDatabase database, IAppLogger? log = null)
    {
        _database = database;
        _log = log ?? NullLogger.Instance;
    }

    public void Save(string runId, DateTimeOffset createdAt, string appVersion, string machine, string scope,
        double? healthScore, string healthState, string payloadJson)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history_runs (run_id, created_at, app_version, machine, scope, health_score, health_state, payload)
            VALUES ($id, $created, $version, $machine, $scope, $health, $state, $payload)
            ON CONFLICT(run_id) DO UPDATE SET payload=$payload, health_score=$health, health_state=$state;
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$version", appVersion);
        command.Parameters.AddWithValue("$machine", machine);
        command.Parameters.AddWithValue("$scope", scope);
        command.Parameters.AddWithValue("$health", (object?)healthScore ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", healthState);
        command.Parameters.AddWithValue("$payload", payloadJson);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<HistoryEntry> List(int limit = 50)
    {
        var entries = new List<HistoryEntry>();
        try
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT run_id, created_at, app_version, machine, scope, health_score, health_state
                FROM history_runs ORDER BY created_at DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                entries.Add(new HistoryEntry(
                    reader.GetString(0),
                    DateTimeOffset.TryParse(reader.GetString(1), out var created) ? created : DateTimeOffset.MinValue,
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.GetString(6)));
            }
        }
        catch (Exception ex)
        {
            _log.Warn("history", "could not read history: " + ex.Message);
        }
        return entries;
    }

    public string? GetPayload(string runId)
    {
        try
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM history_runs WHERE run_id=$id;";
            command.Parameters.AddWithValue("$id", runId);
            return command.ExecuteScalar() as string;
        }
        catch (Exception ex)
        {
            _log.Warn("history", "could not read run payload: " + ex.Message);
            return null;
        }
    }

    public int DeleteAll()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history_runs;";
        return command.ExecuteNonQuery();
    }

    public int Prune(int keep)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM history_runs WHERE run_id NOT IN (
                SELECT run_id FROM history_runs ORDER BY created_at DESC LIMIT $keep
            );
            """;
        command.Parameters.AddWithValue("$keep", Math.Max(1, keep));
        return command.ExecuteNonQuery();
    }
}
