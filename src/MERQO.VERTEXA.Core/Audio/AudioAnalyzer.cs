namespace Merqo.Vertexa.Core.Audio;

/// <summary>Signal statistics for a captured PCM buffer. Pure arithmetic, unit-tested.</summary>
public sealed record AudioAnalysis(
    double PeakDbfs,
    double RmsDbfs,
    double NoiseFloorDbfs,
    double PeakAmplitude,
    double RmsAmplitude,
    double ClippingPercent,
    int SampleCount,
    string Verdict)
{
    public string Summary =>
        $"peak {PeakDbfs:0.0} dBFS · RMS {RmsDbfs:0.0} dBFS · noise floor {NoiseFloorDbfs:0.0} dBFS · clipping {ClippingPercent:0.0}%";
}

public static class AudioAnalyzer
{
    private const double SilenceDbfs = -100.0;

    /// <summary>Analyses 16-bit signed PCM samples.</summary>
    public static AudioAnalysis Analyse(ReadOnlySpan<short> samples, int sampleRate, int channels = 1)
    {
        if (samples.Length == 0)
            return new AudioAnalysis(SilenceDbfs, SilenceDbfs, SilenceDbfs, 0, 0, 0, 0, "No audio captured");

        double peak = 0;
        double sumSquares = 0;
        var clipped = 0;

        foreach (var sample in samples)
        {
            var normalised = sample / 32768.0;
            var magnitude = Math.Abs(normalised);
            if (magnitude > peak) peak = magnitude;
            if (magnitude >= 0.999) clipped++;
            sumSquares += normalised * normalised;
        }

        // Noise floor: quietest 10 ms frame peak, a robust proxy for hiss/room noise.
        var frameSize = Math.Max(1, sampleRate / 100);
        var frameCount = Math.Max(1, samples.Length / frameSize);
        var framePeaks = new double[frameCount];
        for (var f = 0; f < frameCount; f++)
        {
            var start = f * frameSize;
            var end = Math.Min(start + frameSize, samples.Length);
            double localPeak = 0;
            for (var i = start; i < end; i++)
            {
                var magnitude = Math.Abs(samples[i] / 32768.0);
                if (magnitude > localPeak) localPeak = magnitude;
            }
            framePeaks[f] = localPeak;
        }
        Array.Sort(framePeaks);
        var noiseFloor = framePeaks[Math.Min(frameCount - 1, frameCount / 10)];

        var rms = Math.Sqrt(sumSquares / samples.Length);
        var peakDbfs = ToDbfs(peak);
        var rmsDbfs = ToDbfs(rms);
        var noiseDbfs = ToDbfs(noiseFloor);
        var clippingPercent = 100.0 * clipped / samples.Length;

        var verdict = clippingPercent > 1
            ? "Input is clipping — lower the input level"
            : peakDbfs < -60
                ? "Almost silent — check the microphone is not muted or disabled"
                : rmsDbfs < -40
                    ? "Signal present but quiet"
                    : "Healthy input level";

        return new AudioAnalysis(peakDbfs, rmsDbfs, noiseDbfs, peak, rms, clippingPercent, samples.Length, verdict);
    }

    public static double ToDbfs(double amplitude) => amplitude <= 0 ? SilenceDbfs : Math.Round(20.0 * Math.Log10(amplitude), 1);

    /// <summary>Builds a PCM WAV file in memory (used by the tone generator and playback).</summary>
    public static byte[] BuildWav(short[] samples, int sampleRate, int channels)
    {
        using var stream = new MemoryStream(44 + (samples.Length * 2));
        using var writer = new BinaryWriter(stream);
        var byteRate = sampleRate * channels * 2;
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write(sample);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Generates a sine tone. <paramref name="channel"/> −1 = both, 0 = left, 1 = right.</summary>
    public static short[] GenerateTone(double frequencyHz, int milliseconds, int sampleRate = 44100, double amplitude = 0.35, int channel = -1)
    {
        const int channels = 2;
        var totalSamples = (int)(sampleRate * (milliseconds / 1000.0));
        var samples = new short[totalSamples * channels];
        var fade = Math.Min(totalSamples / 20, sampleRate / 50);
        for (var i = 0; i < totalSamples; i++)
        {
            var envelope = 1.0;
            if (fade > 0)
            {
                if (i < fade) envelope = (double)i / fade;
                else if (i > totalSamples - fade) envelope = (double)(totalSamples - i) / fade;
            }
            var value = (short)(Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate) * amplitude * envelope * short.MaxValue);
            samples[i * channels] = channel is -1 or 0 ? value : (short)0;
            samples[(i * channels) + 1] = channel is -1 or 1 ? value : (short)0;
        }
        return samples;
    }
}
