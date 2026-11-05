using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Merqo.Vertexa.Windows.Native;

internal static class NativeMethods
{
    internal const uint RSMB = 0x52534D42; // 'RSMB' — raw SMBIOS firmware table provider

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId, IntPtr pFirmwareTableBuffer, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetFirmwareType(out uint firmwareType);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(string lpFileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        byte[]? inBuffer, uint inBufferSize, byte[]? outBuffer, uint outBufferSize,
        out uint bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    internal static extern void GetSystemTimeAsFileTime(out long fileTime);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    internal static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint deviceInstance, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Get_Device_ID_Size(out uint bufferLength, uint deviceInstance, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
    {
        internal uint Length;
        internal uint MemoryLoad;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemPowerStatus
    {
        internal byte AclineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal int BatteryLifeTime;
        internal int BatteryFullLifeTime;
    }

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>IOCTL_STORAGE_QUERY_PROPERTY</summary>
    internal const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    /// <summary>IOCTL_STORAGE_PREDICT_FAILURE</summary>
    internal const uint IOCTL_STORAGE_PREDICT_FAILURE = 0x002D1100;
    /// <summary>SMART_RCV_DRIVE_DATA (ATA pass-through receive)</summary>
    internal const uint SMART_RCV_DRIVE_DATA = 0x0007C088;
    /// <summary>IOCTL_STORAGE_GET_DEVICE_NUMBER</summary>
    internal const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    internal const uint StorageDeviceProtocolType = 0x02;

    internal static MemoryStatusEx ReadMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        GlobalMemoryStatusEx(ref status);
        return status;
    }

    internal static bool TryGetSystemPowerStatus(out SystemPowerStatus status) => GetSystemPowerStatus(out status);

    internal static string DescribeLastError() => Marshal.GetLastWin32Error() switch
    {
        5 => "access denied",
        87 => "invalid parameter",
        1 => "device not supported",
        31 => "device not recognisable",
        var code => "Win32 error " + code,
    };

    internal static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
