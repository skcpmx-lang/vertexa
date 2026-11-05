using Merqo.Vertexa.Core.Audio;
using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

public sealed class AudioAnalyzerTests
{
    [Fact]
    public void Digital_silence_reports_the_floor()
    {
        var analysis = AudioAnalyzer.Analyse(new short[4410], 44100);
        Assert.Equal(-100, analysis.PeakDbfs);
        Assert.Equal("Almost silent — check the microphone is not muted or disabled", analysis.Verdict);
    }

    [Fact]
    public void Full_scale_signal_reports_clipping()
    {
        var samples = new short[4410];
        for (var i = 0; i < samples.Length; i++) samples[i] = short.MinValue;
        var analysis = AudioAnalyzer.Analyse(samples, 44100);
        Assert.True(analysis.ClippingPercent > 1);
        Assert.Contains("clipping", analysis.Verdict);
        Assert.Equal(0, analysis.PeakDbfs);
    }

    [Fact]
    public void A_moderate_tone_is_reported_as_healthy()
    {
        var tone = AudioAnalyzer.GenerateTone(440, 250, 44100, 0.5);
        // Down-mix the stereo buffer to mono for analysis.
        var mono = new short[tone.Length / 2];
        for (var i = 0; i < mono.Length; i++) mono[i] = tone[i * 2];
        var analysis = AudioAnalyzer.Analyse(mono, 44100);
        Assert.Equal("Healthy input level", analysis.Verdict);
        Assert.InRange(analysis.PeakDbfs, -12, -4);
    }

    [Fact]
    public void Generated_tone_has_the_requested_duration_and_channel_isolation()
    {
        var left = AudioAnalyzer.GenerateTone(1000, 100, 44100, 0.5, channel: 0);
        Assert.Equal(4410 * 2, left.Length);
        Assert.Contains(left.Where((_, i) => i % 2 == 1), sample => sample == 0);
        var rightEnergy = 0;
        for (var i = 1; i < left.Length; i += 2) rightEnergy += Math.Abs(left[i]);
        Assert.Equal(0, rightEnergy);
    }

    [Fact]
    public void Wav_header_is_valid_riff_with_matching_sizes()
    {
        var wav = AudioAnalyzer.BuildWav(new short[100], 44100, 2);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(100 * 2, BitConverter.ToInt32(wav, 40));
        Assert.Equal(wav.Length, BitConverter.ToInt32(wav, 4) + 8);
    }

    [Theory]
    [InlineData(0.0, -100)]
    [InlineData(1.0, 0)]
    [InlineData(0.5, -6)]
    public void Dbfs_conversion_is_standard(double amplitude, double expected)
    {
        Assert.Equal(expected, AudioAnalyzer.ToDbfs(amplitude), 1);
    }
}

public sealed class FormattingTests
{
    [Theory]
    [InlineData(0UL, "0 B")]
    [InlineData(1023UL, "1,023 B")]
    [InlineData(1024UL, "1.0 KB")]
    [InlineData(1073741824UL, "1.0 GB")]
    [InlineData(1099511627776UL, "1.0 TB")]
    public void Byte_sizes_use_binary_units(ulong bytes, string expected)
    {
        Assert.Equal(expected, Units.BytesToHuman(bytes));
    }

    [Fact]
    public void Durations_are_human_readable()
    {
        Assert.Equal("250 ms", Units.Duration(TimeSpan.FromMilliseconds(250)));
        Assert.Equal("1.5 s", Units.Duration(TimeSpan.FromMilliseconds(1500)));
        Assert.StartsWith("2 min", Units.Duration(TimeSpan.FromSeconds(125)));
        Assert.StartsWith("1 h", Units.Duration(TimeSpan.FromMinutes(61)));
    }

    [Fact]
    public void Availability_labels_are_honest()
    {
        Assert.Equal("Unavailable", AvailabilityState.Unavailable.Label());
        Assert.Equal("Physical test required", AvailabilityState.RequiresPhysicalHardware.Label());
        Assert.Equal("Needs admin", AvailabilityState.RequiresAdministrator.Label());
    }
}
