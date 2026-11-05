using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.Core.Models;

namespace Merqo.Vertexa.App.ViewModels;

public abstract class PageViewModelBase : ObservableObject
{
    private DiagnosticSnapshot? _snapshot;
    private bool _isBusy;
    private string _statusText = string.Empty;

    public abstract string Key { get; }
    public abstract string Title { get; }
    public virtual string Subtitle => string.Empty;

    public DiagnosticSnapshot? Snapshot
    {
        get => _snapshot;
        set
        {
            if (SetProperty(ref _snapshot, value)) OnSnapshotChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public bool HasSnapshot => _snapshot is not null;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual Task RefreshAsync() => Task.CompletedTask;

    /// <summary>Called when the page becomes visible (used to start/stop live telemetry).</summary>
    public virtual void OnActivated() { }

    /// <summary>Called when another page replaces this one.</summary>
    public virtual void OnDeactivated() { }

    protected virtual void OnSnapshotChanged() => OnPropertyChanged(nameof(HasSnapshot));

    protected static IEnumerable<MetricRowVm> Rows(MetricSection? section) =>
        section is null ? Array.Empty<MetricRowVm>() : section.Metrics.Select(m => new MetricRowVm(m));
}
