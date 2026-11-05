using Microsoft.Win32.SafeHandles;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Windows.Native;

namespace Merqo.Vertexa.Windows.Storage;

/// <summary>
/// Reads the NVMe SMART/Health Information log page (0x02) through
/// IOCTL_STORAGE_QUERY_PROPERTY with StorageDeviceProtocolSpecificProperty.
/// Read-only: no device command mutates the drive.
/// </summary>
internal static class NvmeHealthReader
{
    private const uint StorageDeviceProtocolSpecificProperty = 49;
    private const uint PropertyStandardQuery = 0;
    private const uint ProtocolTypeNvme = 2;
    private const uint NvmeDataTypeLogPage = 2;
    private const int ProtocolSpecificDataSize = 40;

    internal enum ReadStatus { Success, AccessDenied, NotSupported, Failed }

    internal static ReadStatus TryRead(int physicalDriveNumber, out NvmeHealthLog? health, out string? error)
    {
        health = null;
        error = null;
        var path = @"\\.\PHYSICALDRIVE" + physicalDriveNumber;

        SafeFileHandle handle;
        try
        {
            handle = NativeMethods.CreateFileW(path, NativeMethods.GENERIC_READ,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE, IntPtr.Zero,
                NativeMethods.OPEN_EXISTING, 0, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return ReadStatus.Failed;
        }

        if (handle.IsInvalid)
        {
            var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = code == 5
                ? "direct device access requires administrator rights"
                : "cannot open device: " + NativeMethods.DescribeLastError();
            handle.Dispose();
            return code == 5 ? ReadStatus.AccessDenied : ReadStatus.Failed;
        }

        using (handle)
        {
            // STORAGE_PROPERTY_QUERY { PropertyId, QueryType, STORAGE_PROTOCOL_SPECIFIC_DATA }
            var query = new byte[8 + ProtocolSpecificDataSize];
            WriteUInt32(query, 0, StorageDeviceProtocolSpecificProperty);
            WriteUInt32(query, 4, PropertyStandardQuery);
            WriteUInt32(query, 8, ProtocolTypeNvme);            // ProtocolType
            WriteUInt32(query, 12, NvmeHealth.LogPageIdentifier); // ProtocolDataRequestValue = log page 0x02
            WriteUInt32(query, 16, 0);                          // ProtocolDataRequestSubValue
            WriteUInt32(query, 20, ProtocolSpecificDataSize);   // ProtocolDataOffset
            WriteUInt32(query, 24, NvmeHealthParser.LogPageSize); // ProtocolDataLength
            WriteUInt32(query, 28, 0);
            WriteUInt32(query, 32, 0);
            WriteUInt32(query, 36, 0);

            var output = new byte[8 + ProtocolSpecificDataSize + NvmeHealthParser.LogPageSize + 64];
            if (!NativeMethods.DeviceIoControl(handle, NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                    query, (uint)query.Length, output, (uint)output.Length, out var returned, IntPtr.Zero))
            {
                var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                error = code == 5 ? "access denied by the device driver" : "DeviceIoControl failed: " + NativeMethods.DescribeLastError();
                return code == 5 ? ReadStatus.AccessDenied : ReadStatus.NotSupported;
            }

            // STORAGE_PROTOCOL_DATA_DESCRIPTOR { Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA, Data[] }
            if (returned < 8 + ProtocolSpecificDataSize) { error = "descriptor too small"; return ReadStatus.Failed; }
            var dataOffset = ReadUInt32(output, 8 + 12);
            var dataLength = ReadUInt32(output, 8 + 16);
            var absolute = 8L + dataOffset;
            if (absolute < 8 || absolute + NvmeHealthParser.LogPageSize > returned || dataLength < 192)
            {
                error = "log page not returned by this controller";
                return ReadStatus.NotSupported;
            }

            var page = new byte[NvmeHealthParser.LogPageSize];
            Array.Copy(output, (int)absolute, page, 0, page.Length);
            if (!NvmeHealthParser.TryParse(page, out var parsed, out var parseError))
            {
                error = parseError;
                return ReadStatus.Failed;
            }
            health = parsed;
            return ReadStatus.Success;
        }
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
}
