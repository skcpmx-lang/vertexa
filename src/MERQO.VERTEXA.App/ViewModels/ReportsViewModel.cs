using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Settings;
using Merqo.Vertexa.Reporting;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Storage;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class ReportTypeOption
{
    public ReportTypeOption(ReportType type) => Type = type;
    public ReportType Type { get; }
    public string Title => Type.Title();
    public string Description => Type switch
    {
        ReportType.Full => "Every collected section, findings and limitations.",
        ReportType.BuyerInspection => "What a used-machine buyer needs: identity, wear, health, warnings.",
        ReportType.Technician => "Deep inventory incl. drivers, thermal and peripherals.",
        ReportType.BatteryHealth => "Battery capacity, wear and power behaviour.",
        ReportType.StorageHealth => "Drive reliability telemetry and wear.",
        ReportType.Performance => "Measured benchmarks with workload definitions.",
        ReportType.DriverInspection => "Devices, drivers and problem codes.",
        _ => "A single test result.",
    };
}

public sealed class ReportsViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private ReportTypeOption _selectedType;
    private ExportFormat _selectedFormat;
    private string _preview = "Select a report type to build a preview from the current scan.";
    private string _status = "Ready.";
    private string _exportDirectory;

    public ReportsViewModel(AppHost host)
    {
        _host = host;
        _exportDirectory = host.DefaultReportDirectory;
        Types = new ObservableCollection<ReportTypeOption>(Enum.GetValues<ReportType>().Select(t => new ReportTypeOption(t)));
        Formats = new ObservableCollection<ExportFormat>(Enum.GetValues<ExportFormat>());
        History = new ObservableCollection<HistoryEntry>();
        _selectedType = Types[0];
        _selectedFormat = host.Settings.DefaultExportFormat;
        BuildPreviewCommand = new RelayCommand(BuildPreview);
        ExportCommand = new RelayCommand(Export, () => Snapshot is not null);
        OpenFolderCommand = new RelayCommand(OpenFolder);
        ReloadHistoryCommand = new RelayCommand(RefreshHistory);
    }

    public override string Key => "reports";
    public override string Title => "Reports";
    public override string Subtitle => "PDF, HTML, JSON and CSV generated from real collected data.";

    public ObservableCollection<ReportTypeOption> Types { get; }
    public ObservableCollection<ExportFormat> Formats { get; }
    public ObservableCollection<HistoryEntry> History { get; }
    public ICommand BuildPreviewCommand { get; }
    public RelayCommand ExportCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReloadHistoryCommand { get; }

    public ReportTypeOption SelectedType
    {
        get => _selectedType;
        set
        {
            if (!SetProperty(ref _selectedType, value)) return;
            BuildPreview();
        }
    }

    public ExportFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (!SetProperty(ref _selectedFormat, value)) return;
            BuildPreview();
        }
    }

    public string Preview { get => _preview; private set => SetProperty(ref _preview, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    public string ExportDirectory
    {
        get => _exportDirectory;
        set => SetProperty(ref _exportDirectory, value);
    }

    public override Task RefreshAsync()
    {
        BuildPreview();
        RefreshHistory();
        ExportCommand.RaiseCanExecuteChanged();
        return Task.CompletedTask;
    }

    private void BuildPreview()
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Preview = "No scan yet — the report can only contain data that was actually collected.";
            return;
        }

        var document = _host.ReportBuilder.Build(snapshot, SelectedType.Type, _host.AppVersion);
        if (SelectedFormat == ExportFormat.Pdf)
        {
            Preview = $"PDF preview is rendered on export.\n\n{document.Title}\n{document.Subtitle}\n\n" +
                      $"{document.Sections.Count} sections · {document.RowCount} metrics · {document.Findings.Count} findings · {document.Limitations.Count} limitations\n\n" +
                      string.Join("\n", document.Sections.Select(s => $"· {s.Title} ({s.Rows.Count} metrics)")) +
                      "\n\nFindings:\n" + string.Join("\n", document.Findings.Take(10).Select(f => "· " + f));
        }
        else
        {
            Preview = _host.Exporter.RenderToString(document, SelectedFormat);
        }
    }

    private void Export()
    {
        var snapshot = Snapshot;
        if (snapshot is null) return;
        var document = _host.ReportBuilder.Build(snapshot, SelectedType.Type, _host.AppVersion);
        var result = _host.Exporter.Export(document, SelectedFormat, ExportDirectory);
        if (result.Success)
        {
            Status = $"Exported {document.Title} → {result.Path} ({Units.BytesToHuman(result.Bytes)})";
            RefreshHistory();
        }
        else
        {
            Status = "Export failed: " + result.Error;
        }
    }

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var entry in _host.History.List(25)) History.Add(entry);
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(ExportDirectory);
            Process.Start(new ProcessStartInfo(ExportDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status = "Could not open the folder: " + ex.Message;
        }
    }
}
