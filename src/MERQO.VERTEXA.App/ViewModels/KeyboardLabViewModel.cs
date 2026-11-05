using System.Collections.ObjectModel;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;

namespace Merqo.Vertexa.App.ViewModels;

public enum KeyTestState { Untested = 0, Tested = 1, NotDeliverable = 2 }

public sealed class KeyItem
{
    public KeyItem(string id, string label, string vk, double widthUnits = 1, bool notDeliverable = false, string? note = null)
    {
        Id = id;
        Label = label;
        VirtualKey = vk;
        WidthUnits = widthUnits;
        NotDeliverable = notDeliverable;
        Note = note ?? (notDeliverable ? "Windows or the firmware handles this key; desktop apps do not receive it." : string.Empty);
        State = notDeliverable ? KeyTestState.NotDeliverable : KeyTestState.Untested;
    }

    public string Id { get; }
    public string Label { get; }
    public string VirtualKey { get; }
    public double WidthUnits { get; }
    public bool NotDeliverable { get; }
    public string Note { get; }
    public KeyTestState State { get; set; }
    public double Width => 42 * WidthUnits;
    public string Background => State switch
    {
        KeyTestState.Tested => "#1E5F45",
        KeyTestState.NotDeliverable => "#2A2F38",
        _ => "#18212E",
    };
    public string Foreground => State == KeyTestState.NotDeliverable ? "#6B7C8F" : "#E8EEF6";
}

/// <summary>
/// Keyboard lab. Captures keys only while the lab is open, marks non-deliverable keys
/// explicitly, and never writes keystrokes anywhere.
/// </summary>
public sealed class KeyboardLabViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private string _layout = "ANSI (US)";
    private int _tested;
    private int _total;
    private string _lastKey = "none";
    private int _maxSimultaneous;
    private readonly HashSet<string> _down = new(StringComparer.OrdinalIgnoreCase);

    public KeyboardLabViewModel(AppHost host)
    {
        _host = host;
        Rows = new ObservableCollection<ObservableCollection<KeyItem>>();
        Layouts = new ObservableCollection<string> { "ANSI (US)", "ISO (UK)", "JIS (JP)" };
        ResetCommand = new RelayCommand(Reset);
        BuildLayout();
    }

    public override string Key => "keyboard";
    public override string Title => "Keyboard lab";
    public override string Subtitle => "Live key verification with explicit non-deliverable keys.";

    public ObservableCollection<string> Layouts { get; }
    public ObservableCollection<ObservableCollection<KeyItem>> Rows { get; }

    public string Layout
    {
        get => _layout;
        set
        {
            if (!SetProperty(ref _layout, value)) return;
            BuildLayout();
        }
    }

    public ICommand ResetCommand { get; }
    public string LastKey { get => _lastKey; private set => SetProperty(ref _lastKey, value); }
    public string ProgressText => $"{_tested} of {_total} deliverable keys confirmed";
    public string RolloverText => _maxSimultaneous <= 1 ? "Press several keys together to observe rollover." : $"Maximum simultaneous keys observed: {_maxSimultaneous}";
    public string PendingText
    {
        get
        {
            var pending = Rows.SelectMany(r => r).Where(k => k.State == KeyTestState.Untested).Select(k => k.Label).Take(12).ToList();
            return pending.Count == 0 ? "All deliverable keys confirmed." : "Not yet pressed: " + string.Join(", ", pending);
        }
    }

    public void RegisterKeyDown(string virtualKey, string label)
    {
        if (string.IsNullOrEmpty(virtualKey)) return;
        _down.Add(virtualKey);
        if (_down.Count > _maxSimultaneous) _maxSimultaneous = _down.Count;
        var item = Rows.SelectMany(r => r).FirstOrDefault(k => string.Equals(k.VirtualKey, virtualKey, StringComparison.OrdinalIgnoreCase));
        LastKey = label;
        if (item is null || item.NotDeliverable) return;
        if (item.State == KeyTestState.Tested) return;
        item.State = KeyTestState.Tested;
        Recalculate();
    }

    public void RegisterKeyUp(string virtualKey)
    {
        if (!string.IsNullOrEmpty(virtualKey)) _down.Remove(virtualKey);
    }

    public void Reset()
    {
        foreach (var key in Rows.SelectMany(r => r)) key.State = key.NotDeliverable ? KeyTestState.NotDeliverable : KeyTestState.Untested;
        _maxSimultaneous = 0;
        LastKey = "none";
        RebuildRows();
        Recalculate();
    }

    private void Recalculate()
    {
        var all = Rows.SelectMany(r => r).ToList();
        _total = all.Count(k => !k.NotDeliverable);
        _tested = all.Count(k => k.State == KeyTestState.Tested);
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(PendingText));
        OnPropertyChanged(nameof(RolloverText));
        RebuildRows();
    }

    private void RebuildRows()
    {
        var snapshot = Rows.Select(r => r.ToList()).ToList();
        Rows.Clear();
        foreach (var row in snapshot) Rows.Add(new ObservableCollection<KeyItem>(row));
    }

    private void BuildLayout()
    {
        Rows.Clear();
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("esc", "Esc", "ESCAPE"), new("f1", "F1", "F1"), new("f2", "F2", "F2"), new("f3", "F3", "F3"),
            new("f4", "F4", "F4"), new("f5", "F5", "F5"), new("f6", "F6", "F6"), new("f7", "F7", "F7"),
            new("f8", "F8", "F8"), new("f9", "F9", "F9"), new("f10", "F10", "F10"), new("f11", "F11", "F11"), new("f12", "F12", "F12"),
            new("prtsc", "PrtSc", "SNAPSHOT", 1, false, "Captured by the OS screenshot service in some configurations."),
            new("scroll", "Scroll", "SCROLL", 1, false),
            new("pause", "Pause", "PAUSE", 1, false),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("tilde", "` ~", "OEM_3"), new("d1", "1", "D1"), new("d2", "2", "D2"), new("d3", "3", "D3"),
            new("d4", "4", "D4"), new("d5", "5", "D5"), new("d6", "6", "D6"), new("d7", "7", "D7"),
            new("d8", "8", "D8"), new("d9", "9", "D9"), new("d0", "0", "D0"), new("minus", "- _", "OEM_MINUS"),
            new("equals", "= +", "OEM_PLUS"), new("backspace", "Backspace", "BACK", 2),
            new("insert", "Ins", "INSERT"), new("home", "Home", "HOME"), new("pgup", "PgUp", "PRIOR"),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("tab", "Tab", "TAB", 1.5), new("q", "Q", "Q"), new("w", "W", "W"), new("e", "E", "E"), new("r", "R", "R"),
            new("t", "T", "T"), new("y", "Y", "Y"), new("u", "U", "U"), new("i", "I", "I"), new("o", "O", "O"),
            new("p", "P", "P"), new("lbracket", "[ {", "OEM_4"), new("rbracket", "] }", "OEM_6"),
            new("backslash", "\\ |", "OEM_5", 1.5), new("delete", "Del", "DELETE"), new("end", "End", "END"), new("pgdn", "PgDn", "NEXT"),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("caps", "Caps", "CAPITAL", 1.75), new("a", "A", "A"), new("s", "S", "S"), new("d", "D", "D"), new("f", "F", "F"),
            new("g", "G", "G"), new("h", "H", "H"), new("j", "J", "J"), new("k", "K", "K"), new("l", "L", "L"),
            new("semicolon", "; :", "OEM_1"), new("quote", "' \"", "OEM_7"), new("enter", "Enter", "RETURN", 2.25),
            new("numlock", "NumLk", "NUMLOCK", 1, false), new("num7", "7", "NUMPAD7"), new("num8", "8", "NUMPAD8"), new("num9", "9", "NUMPAD9"),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("lshift", "Shift", "LSHIFT", 2.25), new("z", "Z", "Z"), new("x", "X", "X"), new("c", "C", "C"), new("v", "V", "V"),
            new("b", "B", "B"), new("n", "N", "N"), new("m", "M", "M"), new("comma", ", <", "OEM_COMMA"), new("period", ". >", "OEM_PERIOD"),
            new("slash", "/ ?", "OEM_2"), new("rshift", "Shift", "RSHIFT", 1.75), new("up", "Up", "UP"),
            new("num4", "4", "NUMPAD4"), new("num5", "5", "NUMPAD5"), new("num6", "6", "NUMPAD6"),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("lctrl", "Ctrl", "LCONTROL", 1.5), new("lwin", "Win", "LWIN", 1, true), new("lalt", "Alt", "LMENU", 1.5),
            new("space", "Space", "SPACE", 5), new("ralt", "Alt", "RMENU", 1.5), new("rwin", "Win", "RWIN", 1, true),
            new("rctrl", "Ctrl", "RCONTROL", 1.5), new("left", "Left", "LEFT"), new("down", "Down", "DOWN"), new("right", "Right", "RIGHT"),
            new("num1", "1", "NUMPAD1"), new("num2", "2", "NUMPAD2"), new("num3", "3", "NUMPAD3"), new("num0", "0", "NUMPAD0", 2), new("numdot", ".", "DECIMAL"),
        });
        Rows.Add(new ObservableCollection<KeyItem>
        {
            new("media", "Play/Pause", "MEDIAPLAY", 1, true, "Media keys are consumed by the OS media session."),
            new("volup", "Vol +", "VOLUMEUP", 1, true, "Volume keys are handled by the system audio stack."),
            new("voldown", "Vol −", "VOLUMEDOWN", 1, true, "Volume keys are handled by the system audio stack."),
            new("brightness", "Bright +", "BROWSER_BACK", 1, true, "Brightness keys are usually handled by the embedded controller (Fn)."),
            new("fn", "Fn", "FN", 1, true, "Fn combinations are resolved by firmware and never reach the OS as keystrokes."),
            new("sas", "Ctrl+Alt+Del", "SAS", 1.5, true, "Secure Attention Sequence is reserved by Windows."),
        });
        Recalculate();
    }
}
