using System.Runtime.InteropServices;

namespace Merqo.Vertexa.Windows.Audio;

/// <summary>
/// Microphone capture through winmm waveIn. Recording starts only when the user presses
/// Record, is limited in duration, stays in memory (single optional in-memory WAV), and
/// nothing is written to disk or transmitted.
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    private const int SampleRate = 44100;
    private const int BufferCount = 4;
    private const int BufferBytes = SampleRate * 2 / 2; // ~250 ms of 16-bit mono

    private IntPtr _handle;
    private readonly List<IntPtr> _headers = new();
    private readonly List<IntPtr> _buffers = new();
    private bool _started;

    public bool IsSupported { get; private set; } = true;
    public string? LastError { get; private set; }

    public bool Start()
    {
        if (_started) return true;
        var format = new WAVEFORMATEX
        {
            wFormatTag = 1,
            nChannels = 1,
            nSamplesPerSec = SampleRate,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = SampleRate * 2,
            cbSize = 0,
        };

        var result = waveInOpen(out _handle, 0, ref format, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != 0)
        {
            LastError = DescribeWaveInError(result);
            IsSupported = result is not (32 or 2); // MMSYSERR_NODRIVER / MMSYSERR_BADDEVICEID
            return false;
        }

        for (var i = 0; i < BufferCount; i++)
        {
            var buffer = Marshal.AllocHGlobal(BufferBytes);
            _buffers.Add(buffer);
            var header = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            var hdr = new WAVEHDR
            {
                lpData = buffer,
                dwBufferLength = BufferBytes,
                dwBytesRecorded = 0,
                dwFlags = 0,
                dwLoops = 0,
                dwUser = IntPtr.Zero,
            };
            Marshal.StructureToPtr(hdr, header, false);
            _headers.Add(header);
            waveInPrepareHeader(_handle, header, Marshal.SizeOf<WAVEHDR>());
            waveInAddBuffer(_handle, header, Marshal.SizeOf<WAVEHDR>());
        }

        if (waveInStart(_handle) != 0)
        {
            LastError = "the capture device did not start";
            CleanUp();
            return false;
        }
        _started = true;
        return true;
    }

    /// <summary>Stops capture and returns the recorded PCM as 16-bit mono samples.</summary>
    public short[] StopAndCollect()
    {
        if (!_started) return Array.Empty<short>();
        waveInStop(_handle);
        waveInReset(_handle);

        var samples = new List<short>(BufferCount * BufferBytes / 2);
        foreach (var header in _headers)
        {
            var hdr = Marshal.PtrToStructure<WAVEHDR>(header);
            waveInUnprepareHeader(_handle, header, Marshal.SizeOf<WAVEHDR>());
            var recorded = hdr.dwBytesRecorded;
            if (recorded == 0 || hdr.lpData == IntPtr.Zero) continue;
            var data = new byte[recorded];
            Marshal.Copy(hdr.lpData, data, 0, (int)recorded);
            for (var i = 0; i + 1 < data.Length; i += 2)
                samples.Add(BitConverter.ToInt16(data, i));
        }

        _started = false;
        CleanUp();
        return samples.ToArray();
    }

    private void CleanUp()
    {
        foreach (var buffer in _buffers) Marshal.FreeHGlobal(buffer);
        foreach (var header in _headers) Marshal.FreeHGlobal(header);
        _buffers.Clear();
        _headers.Clear();
        if (_handle != IntPtr.Zero)
        {
            waveInClose(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static string DescribeWaveInError(int code) => code switch
    {
        1 => "waveIn: invalid device handle",
        2 => "waveIn: bad device id (no capture device)",
        4 => "waveIn: the capture device is already in use",
        5 => "waveIn: unsupported format",
        32 => "waveIn: no audio driver is installed",
        33 => "waveIn: the device does not support this call",
        _ => "waveIn error " + code,
    };

    public void Dispose()
    {
        if (_started) StopAndCollect();
        else CleanUp();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX
    {
        internal ushort wFormatTag;
        internal ushort nChannels;
        internal uint nSamplesPerSec;
        internal uint nAvgBytesPerSec;
        internal ushort nBlockAlign;
        internal ushort wBitsPerSample;
        internal ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        internal IntPtr lpData;
        internal uint dwBufferLength;
        internal uint dwBytesRecorded;
        internal IntPtr dwUser;
        internal uint dwFlags;
        internal uint dwLoops;
        internal IntPtr lpNext;
        internal IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveInOpen(out IntPtr handle, uint deviceId, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveInPrepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInUnprepareHeader(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInAddBuffer(IntPtr handle, IntPtr header, int headerSize);

    [DllImport("winmm.dll")]
    private static extern int waveInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    private static extern int waveInClose(IntPtr handle);
}
