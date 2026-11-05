using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Settings;

namespace Merqo.Vertexa.App.ViewModels;

/// <summary>Settings bound to a validated copy of the persisted configuration.</summary>
public sealed class SettingsViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private string _status = "Changes apply when you save.";
    private string _warnings = string.Empty;

    public SettingsViewModel(AppHost host)
    {
        _host = host;
        _settings = host.Settings.Clone();
        Themes = new ObservableCollection<AppTheme>(Enum.GetValues<AppTheme>());
        Formats = new ObservableCollection<ExportFormat>(Enum.GetValues<ExportFormat>());
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(ResetToDefaults);
        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);
        DeleteHistoryCommand = new RelayCommand(DeleteHistory);
        ElevationHint = host.IsElevated
            ? "This session is elevated: all providers can run."
            : "Standard session. Some providers (raw device access, certain WMI classes) will report 'access denied' until you restart VERTEXA as administrator.";
    }

    public override string Key => "settings";
    public override string Title => "Settings";
    public override string Subtitle => "Appearance, diagnostics, safety and local data.";

    public ObservableCollection<AppTheme> Themes { get; }
    public ObservableCollection<ExportFormat> Formats { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand DeleteHistoryCommand { get; }
    public string ElevationHint { get; }
    public string DataPath => _host.DataDirectory;
    public string LogPath => _host.LogDirectory;

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Warnings { get => _warnings; private set => SetProperty(ref _warnings, value); }

    public AppTheme Theme
    {
        get => _settings.Theme;
        set { if (_settings.Theme != value) { _settings.Theme = value; OnPropertyChanged(); } }
    }

    public bool ReducedMotion
    {
        get => _settings.Motion == MotionPreference.Reduced;
        set
        {
            _settings.Motion = value ? MotionPreference.Reduced : MotionPreference.Full;
            OnPropertyChanged();
        }
    }

    public bool RedactSerials
    {
        get => _settings.RedactSerials;
        set { _settings.RedactSerials = value; OnPropertyChanged(); }
    }

    public int MonitoringIntervalMs
    {
        get => _settings.MonitoringIntervalMs;
        set { _settings.MonitoringIntervalMs = value; OnPropertyChanged(); }
    }

    public bool MonitoringAutoStart
    {
        get => _settings.MonitoringAutoStart;
        set { _settings.MonitoringAutoStart = value; OnPropertyChanged(); }
    }

    public double ThermalAbortCelsius
    {
        get => _settings.ThermalAbortCelsius;
        set { _settings.ThermalAbortCelsius = value; OnPropertyChanged(); }
    }

    public int DefaultStressDurationSeconds
    {
        get => _settings.DefaultStressDurationSeconds;
        set { _settings.DefaultStressDurationSeconds = value; OnPropertyChanged(); }
    }

    public int DefaultStorageTestSizeMb
    {
        get => _settings.DefaultStorageTestSizeMb;
        set { _settings.DefaultStorageTestSizeMb = value; OnPropertyChanged(); }
    }

    public ExportFormat DefaultExportFormat
    {
        get => _settings.DefaultExportFormat;
        set { _settings.DefaultExportFormat = value; OnPropertyChanged(); }
    }

    public string? DefaultExportDirectory
    {
        get => _settings.DefaultExportDirectory;
        set { _settings.DefaultExportDirectory = value; OnPropertyChanged(); }
    }

    public bool KeepDiagnosticHistory
    {
        get => _settings.KeepDiagnosticHistory;
        set { _settings.KeepDiagnosticHistory = value; OnPropertyChanged(); }
    }

    public int HistoryLimit
    {
        get => _settings.HistoryLimit;
        set { _settings.HistoryLimit = value; OnPropertyChanged(); }
    }

    public bool StartupCheckForUpdates
    {
        get => _settings.StartupCheckForUpdates;
        set { _settings.StartupCheckForUpdates = value; OnPropertyChanged(); }
    }

    public bool ShowAdvancedMetrics
    {
        get => _settings.ShowAdvancedMetrics;
        set { _settings.ShowAdvancedMetrics = value; OnPropertyChanged(); }
    }

    private void Save()
    {
        try
        {
            var warnings = _host.SaveSettings(_settings);
            Warnings = warnings.Count == 0 ? string.Empty : string.Join(" ", warnings);
            ThemeManager.Apply(_settings.Theme);
            Status = "Settings saved" + (warnings.Count == 0 ? "." : " with adjustments.");
        }
        catch (Exception ex)
        {
            Status = "Could not save settings: " + ex.Message;
        }
    }

    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        var warnings = _host.SaveSettings(defaults);
        Status = "Settings reset to safe defaults.";
        Warnings = warnings.Count == 0 ? string.Empty : string.Join(" ", warnings);
        ThemeManager.Apply(defaults.Theme);
        OnPropertyChanged(string.Empty);
    }

    private void OpenDataFolder() => Open(_host.DataDirectory);
    private void OpenLogFolder() => Open(_host.LogDirectory);

    private void DeleteHistory()
    {
        try
        {
            var removed = _host.History.DeleteAll();
            Status = $"Deleted {removed} history record(s).";
        }
        catch (Exception ex)
        {
            Status = "Could not delete history: " + ex.Message;
        }
    }

    private static void Open(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Opening a folder is a convenience; a failure is not an application error.
        }
    }
}
