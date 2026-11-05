namespace Merqo.Vertexa.Core.Settings;

public enum AppTheme { Dark = 0, Light = 1, HighContrast = 2 }
public enum MotionPreference { Full = 0, Reduced = 1 }
public enum ExportFormat { Pdf = 0, Html = 1, Json = 2, Csv = 3 }

/// <summary>
/// All user preferences. Validated on load and on save; a corrupt or hostile settings
/// file can never put the app into an invalid state (values are clamped with a warning).
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public MotionPreference Motion { get; set; } = MotionPreference.Full;
    public string Language { get; set; } = "en";
    public bool RedactSerials { get; set; } = true;
    public bool IncludeBenchmarksInQuickScan { get; set; } = false;
    public int MonitoringIntervalMs { get; set; } = 1000;
    public bool MonitoringAutoStart { get; set; } = false;
    public int MonitoringHistoryPoints { get; set; } = 120;
    public double ThermalAbortCelsius { get; set; } = 95;
    public int DefaultStressDurationSeconds { get; set; } = 60;
    public int DefaultStorageTestSizeMb { get; set; } = 256;
    public string? DefaultExportDirectory { get; set; }
    public ExportFormat DefaultExportFormat { get; set; } = ExportFormat.Pdf;
    public bool ConfirmBeforeStressTests { get; set; } = true;
    public bool StartupCheckForUpdates { get; set; } = false;
    public bool KeepDiagnosticHistory { get; set; } = true;
    public int HistoryLimit { get; set; } = 100;
    public string LogLevel { get; set; } = "Info";
    public bool ShowAdvancedMetrics { get; set; } = true;
    public string? CustomShortcutPalette { get; set; }

    public IReadOnlyList<string> Validate()
    {
        var warnings = new List<string>();
        if (MonitoringIntervalMs is < 250 or > 10000)
        {
            MonitoringIntervalMs = Math.Clamp(MonitoringIntervalMs, 250, 10000);
            warnings.Add($"Monitoring interval clamped to {MonitoringIntervalMs} ms.");
        }
        if (MonitoringHistoryPoints is < 20 or > 1000)
        {
            MonitoringHistoryPoints = Math.Clamp(MonitoringHistoryPoints, 20, 1000);
            warnings.Add($"Monitoring history clamped to {MonitoringHistoryPoints} points.");
        }
        if (ThermalAbortCelsius is < 60 or > 110)
        {
            ThermalAbortCelsius = Math.Clamp(ThermalAbortCelsius, 60, 110);
            warnings.Add($"Thermal abort threshold clamped to {ThermalAbortCelsius:0} °C (conservative safety limit).");
        }
        if (DefaultStressDurationSeconds is < 10 or > 3600)
        {
            DefaultStressDurationSeconds = Math.Clamp(DefaultStressDurationSeconds, 10, 3600);
            warnings.Add("Stress duration clamped to 10–3600 seconds.");
        }
        if (DefaultStorageTestSizeMb is < 16 or > 8192)
        {
            DefaultStorageTestSizeMb = Math.Clamp(DefaultStorageTestSizeMb, 16, 8192);
            warnings.Add("Storage test size clamped to 16–8192 MB.");
        }
        if (HistoryLimit is < 10 or > 1000)
        {
            HistoryLimit = Math.Clamp(HistoryLimit, 10, 1000);
            warnings.Add("History limit clamped to 10–1000.");
        }
        if (Language is not ("en"))
        {
            warnings.Add($"Language '{Language}' has no translation bundle yet; falling back to English.");
            Language = "en";
        }
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Dark;
        if (!Enum.IsDefined(DefaultExportFormat)) DefaultExportFormat = ExportFormat.Pdf;
        if (string.IsNullOrWhiteSpace(LogLevel)) LogLevel = "Info";
        return warnings;
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
