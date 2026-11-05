using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Storage;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

/// <summary>SQLite storage runs on Linux too, so the persistence layer is covered in the unit job.</summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vertexa-db-test-" + Guid.NewGuid().ToString("N")[..8]);

    public PersistenceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void Migration_creates_schema_and_is_idempotent()
    {
        using var database = new VertexaDatabase(_directory);
        var first = database.Migrate();
        var second = database.Migrate();

        Assert.True(first >= 3);
        Assert.Equal(first, second);
        Assert.True(File.Exists(database.DatabasePath));
    }

    [Fact]
    public void Settings_round_trip_and_validation_clamps_hostile_values()
    {
        using var database = new VertexaDatabase(_directory);
        database.Migrate();
        var repository = new SettingsRepository(database);

        var settings = new AppSettings { Theme = AppTheme.Light, MonitoringIntervalMs = 5, ThermalAbortCelsius = 999, RedactSerials = false };
        repository.Save(settings);

        var loaded = repository.Load();
        Assert.Equal(AppTheme.Light, loaded.Theme);
        Assert.Equal(250, loaded.MonitoringIntervalMs);   // clamped to the floor
        Assert.Equal(110, loaded.ThermalAbortCelsius);    // clamped to the ceiling
        Assert.False(loaded.RedactSerials);
    }

    [Fact]
    public void History_entries_are_stored_listed_and_pruned()
    {
        using var database = new VertexaDatabase(_directory);
        database.Migrate();
        var history = new HistoryRepository(database);

        for (var i = 0; i < 5; i++)
            history.Save($"RUN{i}", DateTimeOffset.UtcNow.AddMinutes(-i), "1.0.0", "TEST-PC", "Full", 82, "Available", "{}");

        var entries = history.List(10);
        Assert.Equal(5, entries.Count);
        Assert.Equal("RUN0", entries[0].RunId);
        Assert.Equal(82, entries[0].HealthScore);

        var removed = history.Prune(2);
        Assert.True(removed >= 3);
        Assert.Equal(2, history.List(10).Count);
    }

    [Fact]
    public void Deleting_history_removes_every_row()
    {
        using var database = new VertexaDatabase(_directory);
        database.Migrate();
        var history = new HistoryRepository(database);
        history.Save("A", DateTimeOffset.UtcNow, "1.0.0", "PC", "Quick", null, "Unknown", "{}");

        Assert.Equal(1, history.DeleteAll());
        Assert.Empty(history.List(10));
    }
}

public sealed class SecurityTests
{
    [Fact]
    public void Mac_addresses_and_guids_are_masked()
    {
        var redaction = new RedactionService();
        var text = redaction.Redact("adapter 3C:52:82:11:AA:09 and guid 8f1c2d3e-4a5b-6c7d-8e9f-0a1b2c3d4e5f");
        Assert.DoesNotContain("3C:52:82:11:AA:09", text);
        Assert.DoesNotContain("8f1c2d3e-4a5b-6c7d-8e9f-0a1b2c3d4e5f", text);
        Assert.Contains("••", text);
    }

    [Fact]
    public void Redaction_can_be_disabled_by_explicit_choice()
    {
        var redaction = new RedactionService(enabled: false);
        Assert.Equal("3C:52:82:11:AA:09", redaction.Redact("3C:52:82:11:AA:09"));
    }

    [Fact]
    public void Short_serials_are_fully_masked()
    {
        Assert.Equal("••", new RedactionService().MaskSerial("AB"));
        Assert.Equal("••••H1", new RedactionService().MaskSerial("XYZH1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_paths_are_rejected(string path)
    {
        Assert.False(PathGuard.ValidateFileTarget(path).IsValid);
    }

    [Fact]
    public void Parent_directory_traversal_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), "reports", "..", "..", "Windows", "system32", "evil.dll");
        var result = PathGuard.ValidateFileTarget(path);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Target_outside_the_chosen_root_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "vertexa-root");
        var target = Path.Combine(Path.GetTempPath(), "elsewhere", "report.pdf");
        Assert.False(PathGuard.ValidateFileTarget(target, root).IsValid);
        Assert.True(PathGuard.ValidateFileTarget(Path.Combine(root, "report.pdf"), root).IsValid);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("LPT1.txt")]
    public void Reserved_device_names_are_rejected(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        Assert.False(PathGuard.ValidateFileTarget(path).IsValid);
    }

    [Fact]
    public void Unique_names_never_overwrite_an_existing_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vertexa-unique-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "report.pdf");
            File.WriteAllText(first, "existing");
            var second = PathGuard.MakeUnique(first);
            Assert.NotEqual(first, second);
            Assert.False(File.Exists(second));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public sealed class SettingsValidationTests
{
    [Fact]
    public void Defaults_are_already_valid()
    {
        Assert.Empty(new AppSettings().Validate());
    }

    [Fact]
    public void Every_out_of_range_value_is_clamped_and_reported()
    {
        var settings = new AppSettings
        {
            MonitoringIntervalMs = 0,
            MonitoringHistoryPoints = 5,
            ThermalAbortCelsius = 10,
            DefaultStressDurationSeconds = 1,
            DefaultStorageTestSizeMb = 1,
            HistoryLimit = 1,
            Language = "zz",
        };

        var warnings = settings.Validate();

        Assert.NotEmpty(warnings);
        Assert.InRange(settings.MonitoringIntervalMs, 250, 10000);
        Assert.InRange(settings.MonitoringHistoryPoints, 20, 1000);
        Assert.InRange(settings.ThermalAbortCelsius, 60, 110);
        Assert.InRange(settings.DefaultStressDurationSeconds, 10, 3600);
        Assert.InRange(settings.DefaultStorageTestSizeMb, 16, 8192);
        Assert.InRange(settings.HistoryLimit, 10, 1000);
        Assert.Equal("en", settings.Language);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var original = new AppSettings { Theme = AppTheme.Light };
        var clone = original.Clone();
        clone.Theme = AppTheme.Dark;
        Assert.Equal(AppTheme.Light, original.Theme);
    }
}
