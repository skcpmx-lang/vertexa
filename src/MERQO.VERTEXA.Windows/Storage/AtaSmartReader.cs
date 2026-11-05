using Microsoft.Win32.SafeHandles;
using Merqo.Vertexa.Core.Storage;
using Merqo.Vertexa.Windows.Native;

namespace Merqo.Vertexa.Windows.Storage;

/// <summary>
/// Reads ATA S.M.A.R.T. data (SMART READ DATA, 0xD0) and the predict-failure flag.
/// Read-only pass-through; the drive is never asked to run a self-test that changes state.
/// </summary>
internal static class AtaSmartReader
{
    internal enum ReadStatus { Success, AccessDenied, NotSupported, Failed }

    internal static ReadStatus TryReadSmartData(int physicalDriveNumber, out byte[]? smartData, out string? error)
    {
        smartData = null;
        error = null;
        var path = @"\\.\PHYSICALDRIVE" + physicalDriveNumber;

        SafeFileHandle handle;
        try
        {
            handle = NativeMethods.CreateFileW(path, NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
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
            error = code == 5 ? "S.M.A.R.T. pass-through requires administrator rights" : "cannot open device: " + NativeMethods.DescribeLastError();
            handle.Dispose();
            return code == 5 ? ReadStatus.AccessDenied : ReadStatus.Failed;
        }

        using (handle)
        {
            // SENDCMDINPARAMS (32 bytes) for SMART READ DATA.
            var input = new byte[32];
            WriteUInt32(input, 0, 512);   // dwBufferSize
            input[4] = 0xD0;              // bFeaturesReg = SMART_READ_DATA
            input[5] = 0x01;              // bSectorCountReg = 1
            input[6] = 0x01;              // bSectorNumberReg = 1
            input[7] = 0x4F;              // bCylLowReg = SMART_LOW
            input[8] = 0xC2;              // bCylHighReg = SMART_HIGH
            input[9] = (byte)(0xA0 | ((physicalDriveNumber & 1) << 4)); // bDriveHeadReg = master/slave
            input[10] = 0xB0;             // bCommandReg = SMART
            input[11] = 0;                // bReserved
            input[12] = (byte)physicalDriveNumber; // bDriveNumber

            var output = new byte[560];
            if (!NativeMethods.DeviceIoControl(handle, NativeMethods.SMART_RCV_DRIVE_DATA,
                    input, (uint)input.Length, output, (uint)output.Length, out var returned, IntPtr.Zero))
            {
                var code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                error = code == 5
                    ? "access denied by the storage driver (elevation or a USB/RAID bridge)"
                    : "SMART pass-through not supported: " + NativeMethods.DescribeLastError();
                return code == 5 ? ReadStatus.AccessDenied : ReadStatus.NotSupported;
            }

            // The data payload follows a status header whose exact size varies by driver
            // version, so validate candidate offsets instead of trusting one.
            foreach (var offset in new[] { 12, 16, 0 })
            {
                if (returned < offset + 512) continue;
                var candidate = new byte[512];
                Array.Copy(output, offset, candidate, 0, 512);
                if (LooksLikeSmartData(candidate))
                {
                    smartData = candidate;
                    return ReadStatus.Success;
                }
            }

            error = "the drive answered but returned no valid S.M.A.R.T. table (virtual or bridged disk)";
            return ReadStatus.NotSupported;
        }
    }

    /// <summary>
    /// A valid SMART data block starts with revision 0x0001/0x0010 followed by attributes
    /// whose ids are non-zero and whose normalised values are 1..253.
    /// </summary>
    internal static bool LooksLikeSmartData(ReadOnlySpan<byte> block)
    {
        if (block.Length < 362) return false;
        var revision = (ushort)(block[0] | (block[1] << 8));
        if (revision is not (0x0001 or 0x0010 or 0x0011)) return false;
        var plausible = 0;
        for (var i = 0; i < 30; i++)
        {
            var offset = 2 + (i * 12);
            var id = block[offset];
            if (id == 0) continue;
            var value = block[offset + 3];
            if (value is >= 1 and <= 253) plausible++;
        }
        return plausible >= 3;
    }

    internal static ReadStatus TryReadPredictFailure(int physicalDriveNumber, out bool failing, out string? error)
    {
        failing = false;
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
            handle.Dispose();
            error = NativeMethods.DescribeLastError();
            return code == 5 ? ReadStatus.AccessDenied : ReadStatus.Failed;
        }

        using (handle)
        {
            var output = new byte[512];
            if (!NativeMethods.DeviceIoControl(handle, NativeMethods.IOCTL_STORAGE_PREDICT_FAILURE,
                    null, 0, output, (uint)output.Length, out var returned, IntPtr.Zero) || returned < 4)
            {
                error = NativeMethods.DescribeLastError();
                return ReadStatus.NotSupported;
            }
            // STORAGE_PREDICT_FAILURE { PredictFailure (ULONG), VendorSpecific[512] }
            failing = (output[0] | (output[1] << 8) | (output[2] << 16) | (output[3] << 24)) != 0;
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
}
