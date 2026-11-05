using System.Text;
using Merqo.Vertexa.Core.Firmware;
using Merqo.Vertexa.Core.Storage;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

/// <summary>
/// Byte-exact parser tests. Firmware and drive telemetry arrive as binary blobs, so the
/// offsets are verified against hand-built fixtures rather than a live machine.
/// </summary>
public sealed class ParserTests
{
    private static byte[] BuildSmbiosTable()
    {
        var structures = new List<byte[]>();

        // Type 0 — BIOS information: vendor/version/date strings at 0x04/0x05/0x08.
        var type0 = new byte[] { 0, 0x1A, 1, 0, 1, 2, 3, 0x7F, 0x7F, 4, 5, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        structures.Add(type0);
        structures.Add(Encoding.ASCII.GetBytes("Contoso\0BIOS 3.1\0""04/01/2024\0\0"));

        // Type 1 — System: manufacturer/product/version/serial/SKU.
        var type1 = new byte[0x1B];
        type1[0] = 1;
        type1[1] = 0x1B;
        type1[2] = 2;
        type1[3] = 0;
        type1[4] = 1;
        type1[5] = 2;
        type1[6] = 3;
        type1[7] = 4;
        type1[0x19] = 5;
        structures.Add(type1);
        structures.Add(Encoding.ASCII.GetBytes("Merqo Systems\0VX-15 Pro\0Rev A\0SN-TEST-0042\0SKU-77\0\0"));

        // Type 17 — Memory device: 16 GB, DDR4, SODIMM, 3200 MT/s.
        var type17 = new byte[0x28];
        type17[0] = 17;
        type17[1] = 0x28;
        type17[2] = 3;
        type17[3] = 0;
        type17[0x0C] = 0x00;
        type17[0x0D] = 0x40; // 0x4000 = 16384 MB
        type17[0x0E] = 0x0D; // SODIMM
        type17[0x10] = 1;    // device locator string
        type17[0x12] = 0x1A; // DDR4
        type17[0x15] = 0x80;
        type17[0x16] = 0x0C; // 3200
        type17[0x17] = 1;    // manufacturer
        type17[0x1A] = 2;    // part number
        structures.Add(type17);
        structures.Add(Encoding.ASCII.GetBytes("DIMM_A1\0Acme Memory\0AC16G4S32\0\0"));

        // End of table.
        structures.Add(new byte[] { 127, 4, 4, 0, 0, 0 });

        var header = new byte[8];
        header[4] = 3;
        header[5] = 5;
        var body = structures.SelectMany(s => s).ToArray();
        BitConverter.GetBytes((uint)(body.Length + 8)).CopyTo(header, 0);
        return header.Concat(body).ToArray();
    }

    [Fact]
    public void Smbios_table_is_parsed_into_typed_facts()
    {
        var structures = SmbiosParser.Parse(BuildSmbiosTable());

        Assert.Equal("Contoso", SmbiosParser.BiosVendor(structures));
        Assert.Equal("BIOS 3.1", SmbiosParser.BiosVersion(structures));
        Assert.Equal("04/01/2024", SmbiosParser.BiosDate(structures));
        Assert.Equal("Merqo Systems", SmbiosParser.SystemManufacturer(structures));
        Assert.Equal("VX-15 Pro", SmbiosParser.SystemProduct(structures));
        Assert.Equal("SKU-77", SmbiosParser.SystemSku(structures));
        Assert.DoesNotContain(structures, s => s.Type == 200);
    }

    [Fact]
    public void Memory_devices_are_decoded_with_type_speed_and_size()
    {
        var devices = SmbiosParser.MemoryDevices(SmbiosParser.Parse(BuildSmbiosTable()));

        var device = Assert.Single(devices);
        Assert.Equal("DIMM_A1", device.Locator);
        Assert.Equal("Acme Memory", device.Manufacturer);
        Assert.Equal("AC16G4S32", device.PartNumber);
        Assert.Equal("DDR4", device.TypeName);
        Assert.Equal("SODIMM", device.FormFactor);
        Assert.Equal(3200, device.SpeedMtPerSecond);
        Assert.Equal(16UL * 1024 * 1024 * 1024, device.SizeBytes);
    }

    [Fact]
    public void Truncated_firmware_table_never_throws()
    {
        var table = BuildSmbiosTable();
        var truncated = table.Take(30).ToArray();
        var structures = SmbiosParser.Parse(truncated);
        Assert.NotNull(structures);
    }

    [Fact]
    public void Empty_or_header_only_table_returns_no_structures()
    {
        Assert.Empty(SmbiosParser.Parse(Array.Empty<byte>()));
        Assert.Empty(SmbiosParser.Parse(new byte[8]));
    }

    private static byte[] BuildNvmeLog()
    {
        var log = new byte[512];
        log[0] = 0x04;                 // critical warning: reliability degraded
        log[1] = 0x2C;                  // kelvin low byte  (0x012C = 300 K)
        log[2] = 0x01;                  // kelvin high byte (300 K -> 26.85 °C)
        log[3] = 92;                    // available spare %
        log[4] = 10;                    // spare threshold %
        log[5] = 37;                    // percentage used
        BitConverter.GetBytes(1_000_000UL).CopyTo(log, 32);  // data units read
        BitConverter.GetBytes(2_500_000UL).CopyTo(log, 48);  // data units written
        BitConverter.GetBytes(12_345UL).CopyTo(log, 112);    // power cycles
        BitConverter.GetBytes(9_876UL).CopyTo(log, 128);     // power-on hours
        BitConverter.GetBytes(7UL).CopyTo(log, 144);         // unsafe shutdowns
        BitConverter.GetBytes(3UL).CopyTo(log, 160);         // media errors
        return log;
    }

    [Fact]
    public void Nvme_health_log_is_decoded_with_correct_offsets_and_units()
    {
        Assert.True(NvmeHealthParser.TryParse(BuildNvmeLog(), out var health, out var error));
        Assert.Null(error);

        Assert.Equal(0x04, health.CriticalWarning);
        Assert.True(health.HasCriticalWarning);
        Assert.Equal(26.9, health.CompositeTemperatureCelsius);
        Assert.Equal(92, health.AvailableSparePercent);
        Assert.Equal(10, health.AvailableSpareThresholdPercent);
        Assert.Equal(37, health.PercentageUsed);
        Assert.Equal(1_000_000UL * NvmeHealthLog.DataUnitBytes, health.BytesRead);
        Assert.Equal(2_500_000UL * NvmeHealthLog.DataUnitBytes, health.BytesWritten);
        Assert.Equal(9_876UL, health.PowerOnHours);
        Assert.Equal(7UL, health.UnsafeShutdowns);
        Assert.Equal(3UL, health.MediaErrors);
    }

    [Fact]
    public void Nvme_log_that_is_too_short_is_rejected_with_a_reason()
    {
        Assert.False(NvmeHealthParser.TryParse(new byte[64], out _, out var error));
        Assert.Contains("expected at least 192", error);
    }

    [Fact]
    public void Critical_warning_bits_are_explained_in_words()
    {
        var log = BuildNvmeLog();
        log[0] = 0x01;
        NvmeHealthParser.TryParse(log, out var health, out _);
        Assert.Contains("available spare below threshold", health!.CriticalWarningText);
    }

    [Fact]
    public void Smart_attributes_are_parsed_and_non_zero_reallocations_are_critical()
    {
        var block = new byte[512];
        block[0] = 0x00;
        block[1] = 0x01; // revision 0x0100
        var offset = 2;
        void Attribute(byte id, byte value, ulong raw)
        {
            block[offset] = id;
            block[offset + 3] = value;
            block[offset + 4] = value;
            for (var i = 0; i < 6; i++) block[offset + 5 + i] = (byte)(raw >> (8 * i));
            offset += 12;
        }
        Attribute(0x05, 100, 3);   // reallocated sectors
        Attribute(0x09, 90, 1234); // power-on hours
        Attribute(0xC2, 60, 45);   // temperature 45 °C

        var attributes = SmartInterpreter.ParseAttributes(block);

        var reallocated = attributes.Single(a => a.Id == 0x05);
        Assert.Equal(SmartSeverity.Critical, reallocated.Severity);
        Assert.Equal(3UL, reallocated.RawValue);
        Assert.Contains("vendor-specific", attributes.Single(a => a.Id == 0x09).Interpretation);

        var temperature = attributes.Single(a => a.Id == 0xC2);
        Assert.Equal(SmartSeverity.Ok, temperature.Severity); // 45 °C is normal
    }

    [Fact]
    public void Temperature_attribute_above_70_degrees_is_critical()
    {
        var block = new byte[512];
        block[1] = 0x01;
        block[2] = 0xC2;
        block[5] = 100;
        block[6] = 100;
        block[7] = 75; // raw byte 0 = 75 °C
        var attributes = SmartInterpreter.ParseAttributes(block);
        Assert.Equal(SmartSeverity.Critical, Assert.Single(attributes).Severity);
    }

    [Fact]
    public void Empty_smart_block_yields_no_attributes()
    {
        Assert.Empty(SmartInterpreter.ParseAttributes(new byte[10]));
    }

    [Fact]
    public void Smart_reader_validator_accepts_a_plausible_block_and_rejects_garbage()
    {
        var block = new byte[512];
        block[1] = 0x01;
        var offset = 2;
        for (var i = 0; i < 5; i++)
        {
            block[offset] = (byte)(0x09 + i);
            block[offset + 3] = 100;
            offset += 12;
        }

        Assert.True(Merqo.Vertexa.UnitTests.Helpers.SmartValidator.IsPlausible(block));
        Assert.False(Merqo.Vertexa.UnitTests.Helpers.SmartValidator.IsPlausible(new byte[512]));
    }
}
