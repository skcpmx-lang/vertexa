using System.IO;
using System.Reflection;
using Merqo.Vertexa.Core.Logging;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Diagnostics;
using Merqo.Vertexa.Reporting;
using Merqo.Vertexa.Security;
using Merqo.Vertexa.Storage;
using Merqo.Vertexa.Windows;

namespace Merqo.Vertexa.App.Services;

/// <summary>
/// Composition root. Every long-lived service is created exactly once here so the
/// lifetime of the database, the monitor loop and the scan engine is unambiguous.
/// </summary>
public sealed class AppHost : IDisposable
{
    public AppHost()
    {
        AppVersion = ReadVersion();
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MERQO", "VERTEXA");
        LogDirectory = Path.Combine(DataDirectory, "logs");
        DefaultReportDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MERQO VERTEXA Reports");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);

        Logger = new FileLogger(LogDirectory);
        Logger.Info("app", $"MERQO VERTEXA {AppVersion} starting on Windows build {Environment.OSVersion.Version.Build}");

        Database = new VertexaDatabase(DataDirectory, Logger);
        try
        {
            Database.Migrate();
        }
        catch (Exception ex)
        {
            Logger.Error("storage", "database migration failed", ex);
        }

        SettingsRepository = new SettingsRepository(Database, Logger);
        Settings = SettingsRepository.Load();
        foreach (var warning in SettingsRepository.LastLoadWarnings) Logger.Warn("settings", warning);

        Redaction = new RedactionService(Settings.RedactSerials);
        var collectors = WindowsServices.CreateCollectors(Logger);
        var orchestrator = new Core.Contracts.ScanOrchestrator(collectors, Logger);
        History = new HistoryRepository(Database, Logger);
        ReportBuilder = new ReportBuilder(Redaction);
        Scan = new ScanService(orchestrator, History, ReportBuilder, Logger, AppVersion);
        Monitoring = WindowsServices.CreateMonitoring(Logger);
        Benchmarks = new BenchmarkService(Monitoring, Logger);
        Exporter = new ReportExporter();
        IsElevated = WindowsServices.IsProcessElevated();

        Logger.Info("app", $"composition complete: {collectors.Count} collectors, elevated={IsElevated}, redaction={Redaction.Enabled}");
    }

    public string AppVersion { get; }
    public string DataDirectory { get; }
    public string LogDirectory { get; }
    public string DefaultReportDirectory { get; }
    public FileLogger Logger { get; }
    public VertexaDatabase Database { get; }
    public SettingsRepository SettingsRepository { get; }
    public AppSettings Settings { get; private set; }
    public HistoryRepository History { get; }
    public RedactionService Redaction { get; }
    public ReportBuilder ReportBuilder { get; }
    public ReportExporter Exporter { get; }
    public ScanService Scan { get; }
    public Core.Contracts.IMonitoringService Monitoring { get; }
    public BenchmarkService Benchmarks { get; }
    public bool IsElevated { get; }

    public IReadOnlyList<string> SaveSettings(AppSettings settings)
    {
        var warnings = settings.Validate().ToList();
        Settings = settings;
        Redaction.Enabled = settings.RedactSerials;
        SettingsRepository.Save(settings);
        return warnings;
    }

    public void Shutdown()
    {
        try
        {
            Monitoring.Stop();
            Monitoring.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Warn("app", "monitor shutdown: " + ex.Message);
        }
        Logger.Info("app", "shutdown complete");
        Logger.Dispose();
        Database.Dispose();
    }

    public void Dispose() => Shutdown();

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    public string BuildStamp
    {
        get
        {
            var path = Assembly.GetExecutingAssembly().Location;
            var built = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
            return built.ToString("yyyy-MM-dd HH:mm 'UTC'");
        }
    }
}
