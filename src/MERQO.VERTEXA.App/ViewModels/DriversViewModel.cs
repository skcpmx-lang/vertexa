using System.Collections.ObjectModel;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Models;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class DriversViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private string _summary = string.Empty;
    private string _filter = string.Empty;
    private bool _problemsOnly = true;
    private readonly List<DeviceRow> _all = new();

    public DriversViewModel(AppHost host)
    {
        _host = host;
        Rows = new ObservableCollection<MetricRowVm>();
        Devices = new ObservableCollection<DeviceRow>();
    }

    public override string Key => "drivers";
    public override string Title => "Drivers & devices";
    public override string Subtitle => "Device tree with driver versions and Windows problem codes.";

    public ObservableCollection<MetricRowVm> Rows { get; }
    public ObservableCollection<DeviceRow> Devices { get; }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value)) ApplyFilter();
        }
    }

    public bool ProblemsOnly
    {
        get => _problemsOnly;
        set
        {
            if (SetProperty(ref _problemsOnly, value)) ApplyFilter();
        }
    }

    public override Task RefreshAsync()
    {
        Rows.Clear();
        _all.Clear();
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Summary = "No scan data yet.";
            return Task.CompletedTask;
        }

        foreach (var row in RowsOf(snapshot.Find(SectionKeys.Drivers))) Rows.Add(row);
        foreach (var device in snapshot.Devices) _all.Add(new DeviceRow(device));
        ApplyFilter();

        var problems = _all.Count(d => d.HasProblem);
        Summary = $"{_all.Count} devices enumerated · {problems} with a problem code · " +
                  "driver updates are never installed automatically";
        return Task.CompletedTask;
    }

    private void ApplyFilter()
    {
        var query = (_filter ?? string.Empty).Trim();
        Devices.Clear();
        IEnumerable<DeviceRow> source = _all;
        if (ProblemsOnly) source = source.Where(d => d.HasProblem);
        if (query.Length > 0)
            source = source.Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                       || (d.Class ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                                       || d.Version.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var row in source.OrderByDescending(d => d.HasProblem).ThenBy(d => d.Name).Take(400)) Devices.Add(row);
    }

    private static IEnumerable<MetricRowVm> RowsOf(MetricSection? section) =>
        section is null ? Array.Empty<MetricRowVm>() : section.Metrics.Select(m => new MetricRowVm(m));
}

public sealed class DeviceRow
{
    public DeviceRow(DeviceRecord device)
    {
        Name = device.Name;
        Class = device.PnpClass ?? "—";
        Version = device.DriverVersion ?? "no driver";
        Date = device.DriverDate ?? "—";
        Provider = device.DriverProvider ?? "—";
        Status = device.ProblemText;
        HasProblem = device.HasProblem;
        Usb = device.IsUsb ? "USB" : string.Empty;
        InstanceId = device.InstanceId ?? string.Empty;
    }

    public string Name { get; }
    public string Class { get; }
    public string Version { get; }
    public string Date { get; }
    public string Provider { get; }
    public string Status { get; }
    public bool HasProblem { get; }
    public string Usb { get; }
    public string InstanceId { get; }
}
