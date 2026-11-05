using System.Collections.ObjectModel;
using System.Windows.Input;
using Merqo.Vertexa.App.Mvvm;
using Merqo.Vertexa.App.Services;
using Merqo.Vertexa.Core.Audio;
using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Windows.Audio;

namespace Merqo.Vertexa.App.ViewModels;

/// <summary>
/// Audio lab: locally generated tones for channel and distortion checks, plus a
/// user-initiated microphone recording with real signal analysis.
/// </summary>
public sealed class AudioLabViewModel : PageViewModelBase
{
    private readonly AppHost _host;
    private MicrophoneRecorder? _recorder;
    private short[] _lastRecording = Array.Empty<short>();
    private string _analysis = "No recording yet.";
    private string _status = "Ready.";
    private bool _isRecording;
    private double _frequency = 440;

    public AudioLabViewModel(AppHost host)
    {
        _host = host;
        Devices = new ObservableCollection<MetricRowVm>();
        PlayLeftCommand = new RelayCommand(() => Play("Left channel", 0));
        PlayRightCommand = new RelayCommand(() => Play("Right channel", 1));
        PlayStereoCommand = new RelayCommand(() => Play("Stereo", -1));
        PlayMonoCommand = new RelayCommand(() => Play("Mono (both channels at half level)", -1, 0.18));
        PlaySweepCommand = new AsyncRelayCommand(RunSweepAsync);
        StopCommand = new RelayCommand(() =>
        {
            AudioPlayback.Stop();
            Status = "Playback stopped.";
        });
        RecordCommand = new AsyncRelayCommand(() => RecordAsync(3));
        StopRecordingCommand = new RelayCommand(StopRecording, () => IsRecording);
        PlayRecordingCommand = new RelayCommand(PlayRecording, () => _lastRecording.Length > 0);
        DiscardCommand = new RelayCommand(Discard, () => _lastRecording.Length > 0);
    }

    public override string Key => "audio";
    public override string Title => "Audio lab";
    public override string Subtitle => "Generated tones, channel checks and microphone analysis — all local.";

    public ObservableCollection<MetricRowVm> Devices { get; }
    public ICommand PlayLeftCommand { get; }
    public ICommand PlayRightCommand { get; }
    public ICommand PlayStereoCommand { get; }
    public ICommand PlayMonoCommand { get; }
    public AsyncRelayCommand PlaySweepCommand { get; }
    public ICommand StopCommand { get; }
    public AsyncRelayCommand RecordCommand { get; }
    public RelayCommand StopRecordingCommand { get; }
    public RelayCommand PlayRecordingCommand { get; }
    public RelayCommand DiscardCommand { get; }

    public double Frequency
    {
        get => _frequency;
        set => SetProperty(ref _frequency, Math.Clamp(value, 50, 12000));
    }

    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Analysis { get => _analysis; private set => SetProperty(ref _analysis, value); }
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (!SetProperty(ref _isRecording, value)) return;
            StopRecordingCommand.RaiseCanExecuteChanged();
        }
    }

    public override Task RefreshAsync()
    {
        Devices.Clear();
        var snapshot = Snapshot;
        if (snapshot is null) return Task.CompletedTask;
        var peripherals = snapshot.Find(SectionKeys.Peripherals);
        if (peripherals is not null)
            foreach (var metric in peripherals.Metrics.Where(m => m.Name.StartsWith("Audio", StringComparison.Ordinal))) Devices.Add(new MetricRowVm(metric));
        if (Devices.Count == 0)
            Status = "No audio endpoint was enumerated in the last scan — playback may still work through the default device.";
        return Task.CompletedTask;
    }

    private void Play(string label, int channel, double amplitude = 0.35)
    {
        if (AudioPlayback.PlayTone(Frequency, 1200, channel, out var error))
            Status = $"{label}: {Frequency:0} Hz for 1.2 s. Listen for a clean tone with no crackle or dropout.";
        else
            Status = "Playback failed: " + (error ?? "unknown error");
    }

    private async Task RunSweepAsync()
    {
        Status = "Frequency sweep 200 Hz → 4000 Hz…";
        for (var frequency = 200.0; frequency <= 4000; frequency *= 1.26)
        {
            AudioPlayback.PlayTone(frequency, 140, -1, out var error);
            if (error is not null)
            {
                Status = "Sweep stopped: " + error;
                return;
            }
            await Task.Delay(160).ConfigureAwait(true);
        }
        Status = "Sweep complete. Listen for buzzing, rattle or missing bands.";
    }

    private async Task RecordAsync(int seconds)
    {
        if (IsRecording) return;
        _recorder?.Dispose();
        _recorder = new MicrophoneRecorder();
        if (!_recorder.Start())
        {
            Status = "Microphone unavailable: " + (_recorder.LastError ?? "no capture device");
            _recorder.Dispose();
            _recorder = null;
            return;
        }
        IsRecording = true;
        Status = $"Recording for {seconds} seconds — speak normally toward the microphone.";
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(true);
        StopRecording();
    }

    private void StopRecording()
    {
        if (_recorder is null) return;
        _lastRecording = _recorder.StopAndCollect();
        _recorder.Dispose();
        _recorder = null;
        IsRecording = false;
        if (_lastRecording.Length == 0)
        {
            Analysis = "No samples captured — the device opened but returned no data.";
            Status = "Recording produced no data.";
        }
        else
        {
            var analysis = AudioAnalyzer.Analyse(_lastRecording, 44100);
            Analysis = analysis.Summary + " — " + analysis.Verdict;
            Status = $"Captured {analysis.SampleCount:N0} samples ({analysis.SampleCount / 44100.0:0.0} s).";
        }
        PlayRecordingCommand.RaiseCanExecuteChanged();
        DiscardCommand.RaiseCanExecuteChanged();
    }

    private void PlayRecording()
    {
        if (_lastRecording.Length == 0) return;
        AudioPlayback.PlayWav(AudioAnalyzer.BuildWav(_lastRecording, 44100, 1));
        Status = "Playing the recording back through the default output device.";
    }

    private void Discard()
    {
        _lastRecording = Array.Empty<short>();
        Analysis = "Recording discarded. Nothing was written to disk.";
        Status = "Recording discarded.";
        PlayRecordingCommand.RaiseCanExecuteChanged();
        DiscardCommand.RaiseCanExecuteChanged();
    }

    public override void OnDeactivated()
    {
        AudioPlayback.Stop();
        if (IsRecording) StopRecording();
        _recorder?.Dispose();
        _recorder = null;
    }
}
