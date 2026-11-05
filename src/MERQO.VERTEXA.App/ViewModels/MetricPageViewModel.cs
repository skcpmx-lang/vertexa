using System.Collections.ObjectModel;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Models;

namespace Merqo.Vertexa.App.ViewModels;

/// <summary>
/// Generic, data-driven diagnostics page. It renders whichever sections it is given, so a
/// new provider immediately appears in the UI and in reports without new view code.
/// </summary>
public sealed class MetricPageViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private readonly string[] _sectionKeys;
    private readonly string _key;
    private readonly string _title;
    private readonly string _subtitle;
    private string _aggregate = string.Empty;

    public MetricPageViewModel(AppHost host, string key, string title, string subtitle, params string[] sectionKeys)
    {
        _host = host;
        _key = key;
        _title = title;
        _subtitle = subtitle;
        _sectionKeys = sectionKeys;
        Groups = new ObservableCollection<MetricGroup>();
    }

    public override string Key => _key;
    public override string Title => _title;
    public override string Subtitle => _subtitle;

    public ObservableCollection<MetricGroup> Groups { get; }

    public string Aggregate { get => _aggregate; private set => SetProperty(ref _aggregate, value); }

    public override Task RefreshAsync()
    {
        Groups.Clear();
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            Aggregate = "No scan data yet — run a scan from the top bar.";
            return Task.CompletedTask;
        }

        var available = 0;
        var total = 0;
        foreach (var sectionKey in _sectionKeys)
        {
            var section = snapshot.Find(sectionKey);
            if (section is null || section.TotalCount == 0) continue;
            var rows = section.Metrics.Select(m => new MetricRowVm(m)).ToList();
            Groups.Add(new MetricGroup(section.Title, section.Description, rows));
            available += section.AvailableCount;
            total += section.TotalCount;
        }

        if (Groups.Count == 0)
        {
            Aggregate = "This section could not be collected in the last run. Check the scan notes on the dashboard.";
            return Task.CompletedTask;
        }

        Aggregate = $"{available} of {total} metrics available · sources are shown per metric";
        return Task.CompletedTask;
    }
}

public sealed class MetricGroup
{
    public MetricGroup(string title, string description, IReadOnlyList<MetricRowVm> rows)
    {
        Title = title;
        Description = description;
        Rows = rows;
        Available = rows.Count(r => r.HasData);
        Total = rows.Count;
        Summary = $"{Available}/{Total} available";
    }

    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<MetricRowVm> Rows { get; }
    public int Available { get; }
    public int Total { get; }
    public string Summary { get; }
}
