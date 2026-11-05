using System.Collections.ObjectModel;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Models;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class DisplayPattern
{
    public DisplayPattern(string name, string purpose, string? solid = null, bool gradient = false, bool grid = false)
    {
        Name = name;
        Purpose = purpose;
        Solid = solid;
        IsGradient = gradient;
        IsGrid = grid;
    }

    public string Name { get; }
    public string Purpose { get; }
    public string? Solid { get; }
    public bool IsGradient { get; }
    public bool IsGrid { get; }
}

/// <summary>
/// Display inspection lab. Software cannot detect pixel faults, so this page provides
/// calibrated patterns and records the operator's findings — and says so plainly.
/// </summary>
public sealed class DisplayLabViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private int _index;
    private string _notes = string.Empty;

    public DisplayLabViewModel(AppHost host)
    {
        _host = host;
        Patterns = new ObservableCollection<DisplayPattern>
        {
            new("White", "Backlight uniformity, dust under the panel, shadowing", "#FFFFFF"),
            new("Black", "Backlight bleed, IPS glow, clouding", "#000000"),
            new("Red", "Stuck red sub-pixels, tint", "#FF0000"),
            new("Green", "Stuck green sub-pixels, tint", "#00FF00"),
            new("Blue", "Stuck blue sub-pixels, tint", "#0000FF"),
            new("Cyan", "Channel balance", "#00FFFF"),
            new("Magenta", "Channel balance", "#FF00FF"),
            new("Yellow", "Channel balance", "#FFFF00"),
            new("Gradient", "Banding, gamma, colour depth", null, gradient: true),
            new("Grid", "Sharpness, scaling artefacts, geometry", null, grid: true),
        };
        NextCommand = new RelayCommand(() => Index = (Index + 1) % Patterns.Count);
        PreviousCommand = new RelayCommand(() => Index = (Index - 1 + Patterns.Count) % Patterns.Count);
        FirstCommand = new RelayCommand(() => Index = 0);
        Displays = new ObservableCollection<MetricRowVm>();
    }

    public override string Key => "display";
    public override string Title => "Display lab";
    public override string Subtitle => "Full-screen patterns with the operator's recorded findings.";

    public ObservableCollection<DisplayPattern> Patterns { get; }
    public ObservableCollection<MetricRowVm> Displays { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand FirstCommand { get; }

    public int Index
    {
        get => _index;
        set
        {
            var clamped = Math.Clamp(value, 0, Patterns.Count - 1);
            if (!SetProperty(ref _index, clamped)) return;
            OnPropertyChanged(nameof(CurrentPattern));
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(Hint));
        }
    }

    public DisplayPattern CurrentPattern => Patterns[Index];
    public string Position => $"{Index + 1} / {Patterns.Count}";
    public string Hint => CurrentPattern.Purpose;

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public override Task RefreshAsync()
    {
        Displays.Clear();
        var snapshot = Snapshot;
        if (snapshot is null) return Task.CompletedTask;
        var display = snapshot.Find(SectionKeys.Display);
        if (display is not null)
            foreach (var metric in display.Metrics.Take(24)) Displays.Add(new MetricRowVm(metric));
        return Task.CompletedTask;
    }
}
