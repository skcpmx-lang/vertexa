using System.Text.Json;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Shared;
using Microsoft.Data.Sqlite;

namespace Merqo.Vertexa.Storage;

/// <summary>Settings persistence with validation on both read and write.</summary>
public sealed class SettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
    private readonly VertexaDatabase _database;
    private readonly IAppLogger _log;

    public SettingsRepository(VertexaDatabase database, IAppLogger? log = null)
    {
        _database = database;
        _log = log ?? NullLogger.Instance;
    }

    public IReadOnlyList<string> LastLoadWarnings { get; private set; } = Array.Empty<string>();

    public AppSettings Load()
    {
        var warnings = new List<string>();
        try
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT json FROM settings WHERE id=1;";
            var json = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            warnings.AddRange(settings.Validate());
            LastLoadWarnings = warnings;
            return settings;
        }
        catch (Exception ex)
        {
            _log.Error("settings", "could not load settings; using safe defaults", ex);
            warnings.Add("Settings could not be read and were reset to defaults: " + ex.Message);
            LastLoadWarnings = warnings;
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var warnings = settings.Validate();
        if (warnings.Count > 0)
            _log.Warn("settings", "settings adjusted on save: " + string.Join(" ", warnings));

        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (id, json, updated_at) VALUES (1, $json, $updated)
            ON CONFLICT(id) DO UPDATE SET json=$json, updated_at=$updated;
            """;
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings, JsonOptions));
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void ResetToDefaults() => Save(new AppSettings());
}
