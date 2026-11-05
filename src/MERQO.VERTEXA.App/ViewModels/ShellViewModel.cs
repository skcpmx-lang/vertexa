using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Core.Settings;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class NavItem : ObservableObject
{
    private bool _isSelected;

    public NavItem(string key, string title, string glyph, string group, Func<PageViewModelBase> factory)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        Group = group;
        Factory = factory;
    }

    public string Key { get; }
    public string Title { get; }
    public string Glyph { get; }
    public string Group { get; }
    public Func<PageViewModelBase> Factory { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private PageViewModelBase? _page;
    public PageViewModelBase Page => _page ??= Factory();
}

/// <summary>
/// Shell orchestrator: navigation, scan lifecycle, command palette and live status.
/// One place owns the "current snapshot" so no page can show stale data by accident.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly AppHost _host;
    private NavItem? _selected;
    private object? _currentPage;
    private bool _isScanning;
    private string _statusText = "Ready";
    private string _progressText = string.Empty;
    private bool _isPaletteOpen;
    private string _paletteQuery = string.Empty;
    private CancellationTokenSource? _scanCts;

    public ShellViewModel(AppHost host)
    {
        _host = host;

        Items = new ObservableCollection<NavItem>
        {
            new("dashboard", "Dashboard", "\uE80F", "Overview", () => new DashboardViewModel(host)),
            new("system", "System", "\uE770", "Hardware", () => new MetricPageViewModel(host, "system", "System",
                "Machine identity, Windows build and firmware facts.", SectionKeys.System, SectionKeys.Os, SectionKeys.Firmware)),
            new("cpu", "Processor", "\uE950", "Hardware", () => new MetricPageViewModel(host, "cpu", "Processor",
                "Identity, topology, clocks and thermal telemetry.", SectionKeys.Cpu, SectionKeys.Thermal)),
            new("gpu", "Graphics", "\uE7F4", "Hardware", () => new MetricPageViewModel(host, "gpu", "Graphics",
                "Adapters, drivers and video memory.", SectionKeys.Gpu)),
            new("memory", "Memory", "\uE8B7", "Hardware", () => new MetricPageViewModel(host, "memory", "Memory",
                "Modules, speeds, slots and live pressure.", SectionKeys.Memory)),
            new("storage", "Storage", "\uEDA2", "Hardware", () => new StorageViewModel(host)),
            new("battery", "Battery & power", "\uE83F", "Hardware", () => new BatteryViewModel(host)),
            new("drivers", "Drivers", "\uE7B8", "Hardware", () => new DriversViewModel(host)),
            new("network", "Network", "\uE968", "Hardware", () => new MetricPageViewModel(host, "network", "Network",
                "Adapters, link state and wireless devices.", SectionKeys.Network)),
            new("peripherals", "Peripherals", "\uE7F8", "Hardware", () => new MetricPageViewModel(host, "peripherals", "Peripherals",
                "USB, cameras, audio endpoints and sensors.", SectionKeys.Peripherals, SectionKeys.Sensors)),
            new("display", "Display lab", "\uE7F4", "Interactive tests", () => new DisplayLabViewModel(host)),
            new("keyboard", "Keyboard lab", "\uE7F4", "Interactive tests", () => new KeyboardLabViewModel(host)),
            new("audio", "Audio lab", "\uE767", "Interactive tests", () => new AudioLabViewModel(host)),
            new("monitor", "Live monitor", "\uE9D9", "Performance", () => new MonitorViewModel(host)),
            new("performance", "Benchmarks", "\uE9D2", "Performance", () => new PerformanceViewModel(host)),
            new("reports", "Reports", "\uE8A5", "Output", () => new ReportsViewModel(host)),
            new("settings", "Settings", "\uE713", "Application", () => new SettingsViewModel(host)),
            new("about", "About", "\uE946", "Application", () => new AboutViewModel(host)),
        };

        Groups = new ObservableCollection<NavGroup>();
        RebuildGroups();

        NavigateCommand = new RelayCommand(parameter =>
        {
            if (parameter is NavItem item) Navigate(item);
            else if (parameter is string key) Navigate(Items.FirstOrDefault(i => i.Key == key));
        });
        QuickScanCommand = new AsyncRelayCommand(() => RunScanAsync(ScanScope.Quick));
        FullScanCommand = new AsyncRelayCommand(() => RunScanAsync(ScanScope.Full));
        CancelScanCommand = new RelayCommand(CancelScan, () => IsScanning);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        TogglePaletteCommand = new RelayCommand(() => IsPaletteOpen = !IsPaletteOpen);
        ClosePaletteCommand = new RelayCommand(() => IsPaletteOpen = false);
        ExecutePaletteCommand = new RelayCommand(ExecutePaletteSelection);

        AppVersion = $"v{host.AppVersion}";
        ElevationLabel = host.IsElevated ? "Elevated session" : "Standard session";
        MachineLabel = Environment.MachineName;
        StatusText = "Ready — run a scan to populate diagnostics.";
        Navigate(Items[0]);
    }

    public ObservableCollection<NavItem> Items { get; }
    public ObservableCollection<NavGroup> Groups { get; }
    public ICommand NavigateCommand { get; }
    public AsyncRelayCommand QuickScanCommand { get; }
    public AsyncRelayCommand FullScanCommand { get; }
    public RelayCommand CancelScanCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand TogglePaletteCommand { get; }
    public ICommand ClosePaletteCommand { get; }
    public ICommand ExecutePaletteCommand { get; }

    public string AppVersion { get; }
    public string MachineLabel { get; }
    public string ElevationLabel { get; }
    public DiagnosticSnapshot? Snapshot { get; private set; }

    public NavItem? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null) Navigate(value);
        }
    }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            CancelScanCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public bool IsPaletteOpen
    {
        get => _isPaletteOpen;
        set
        {
            if (!SetProperty(ref _isPaletteOpen, value)) return;
            if (value) PaletteQuery = string.Empty;
        }
    }

    public string PaletteQuery
    {
        get => _paletteQuery;
        set
        {
            if (!SetProperty(ref _paletteQuery, value)) return;
            OnPropertyChanged(nameof(PaletteResults));
        }
    }

    public IReadOnlyList<NavItem> PaletteResults
    {
        get
        {
            var query = (_paletteQuery ?? string.Empty).Trim();
            if (query.Length == 0) return Items.ToList();
            return Items.Where(i => i.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                                    || i.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
                                    || i.Group.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await RunScanAsync(ScanScope.Quick).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = "Initial scan failed: " + ex.Message;
        }
    }

    private void RebuildGroups()
    {
        Groups.Clear();
        foreach (var group in Items.GroupBy(i => i.Group))
            Groups.Add(new NavGroup(group.Key, group.ToList()));
    }

    private void Navigate(NavItem? item)
    {
        if (item is null) return;
        foreach (var navItem in Items) navItem.IsSelected = ReferenceEquals(navItem, item);
        if (!ReferenceEquals(_selected, item))
        {
            _selected = item;
            OnPropertyChanged(nameof(SelectedItem));
        }
        if (CurrentPage is PageViewModelBase previous && !ReferenceEquals(previous, item.Page))
            previous.OnDeactivated();

        var page = item.Page;
        if (page.Snapshot is null && Snapshot is not null) page.Snapshot = Snapshot;
        CurrentPage = page;
        _ = ActivatePageAsync(page);
    }

    private async Task ActivatePageAsync(PageViewModelBase page)
    {
        try
        {
            if (page.Snapshot is null && Snapshot is not null) page.Snapshot = Snapshot;
            await page.InitializeAsync().ConfigureAwait(true);
            await page.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            page.StatusText = "Page initialisation failed: " + ex.Message;
        }
    }

    private async Task RunScanAsync(ScanScope scope)
    {
        if (IsScanning) return;
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        ProgressText = "Starting scan…";
        StatusText = scope == ScanScope.Full ? "Full scan running…" : "Quick scan running…";

        var progress = new Progress<(string Section, int Done, int Total)>(p =>
            ProgressText = $"{p.Section} — {p.Done}/{p.Total}");

        try
        {
            var snapshot = await _host.Scan.ScanAsync(scope, _host.IsElevated, _host.Settings.RedactSerials, progress, _scanCts.Token)
                .ConfigureAwait(true);
            Snapshot = snapshot;
            StatusText = BuildStatus(snapshot);
            ProgressText = string.Empty;

            foreach (var item in Items)
            {
                if (item.Page is { } page && page.Snapshot is null) page.Snapshot = snapshot;
                else if (item.Page is { } withData) withData.Snapshot = snapshot;
            }

            if (CurrentPage is PageViewModelBase current)
            {
                current.Snapshot = snapshot;
                await current.RefreshAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
            ProgressText = string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed: " + ex.Message;
            ProgressText = string.Empty;
            _host.Logger.Error("shell", "scan failed", ex);
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private static string BuildStatus(DiagnosticSnapshot snapshot)
    {
        var health = snapshot.Health is null ? "health unavailable" : snapshot.Health.OverallDisplay;
        var findings = snapshot.Recommendations.Count(r => r.Severity >= Core.Recommendations.Severity.Warning);
        return $"Scan complete · {snapshot.AvailableMetricCount}/{snapshot.TotalMetricCount} metrics · health {health} · {findings} warning(s) · {snapshot.Warnings.Count} scan note(s)";
    }

    private void CancelScan()
    {
        _scanCts?.Cancel();
        StatusText = "Cancelling scan…";
    }

    private void ToggleTheme()
    {
        var settings = _host.Settings;
        settings.Theme = settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _host.SaveSettings(settings);
        ThemeManager.Apply(settings.Theme);
        StatusText = $"Theme: {settings.Theme}";
    }

    private void ExecutePaletteSelection()
    {
        var first = PaletteResults.FirstOrDefault();
        if (first is null) return;
        IsPaletteOpen = false;
        Navigate(first);
    }
}

public sealed class NavGroup : ObservableCollection<NavItem>
{
    public NavGroup(string title, IEnumerable<NavItem> items) : base(items.ToList()) => Title = title;
    public string Title { get; }
}
