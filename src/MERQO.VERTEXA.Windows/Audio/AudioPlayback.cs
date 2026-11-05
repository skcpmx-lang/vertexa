using System.Runtime.InteropServices;
using Merqo.Vertexa.Core.Audio;

namespace Merqo.Vertexa.Windows.Audio;

/// <summary>
/// Plays generated test tones through the default output device using winmm's
/// PlaySound with SND_MEMORY — no external codec, no network, no recording.
/// </summary>
public static class AudioPlayback
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_MEMORY = 0x0004;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_PURGE = 0x0040;

    [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(byte[]? data, IntPtr module, uint flags);

    /// <summary>
    /// The WAV buffer must stay alive for the duration of asynchronous playback, so the
    /// caller keeps a reference (stored in <see cref="LastBuffer"/>).
    /// </summary>
    public static byte[]? LastBuffer { get; private set; }

    public static bool PlayTone(double frequencyHz, int milliseconds, int channel, out string? error)
    {
        error = null;
        try
        {
            var samples = AudioAnalyzer.GenerateTone(frequencyHz, milliseconds, 44100, 0.35, channel);
            var wav = AudioAnalyzer.BuildWav(samples, 44100, 2);
            LastBuffer = wav;
            if (!PlaySound(wav, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
            {
                error = "the default audio output device rejected the playback request";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void Stop()
    {
        try { PlaySound(null, IntPtr.Zero, SND_PURGE); }
        catch { /* nothing to stop */ }
        LastBuffer = null;
    }

    public static void PlayWav(byte[] wav)
    {
        LastBuffer = wav;
        PlaySound(wav, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
    }
}
