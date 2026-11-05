namespace Merqo.Vertexa.UnitTests.Helpers;

/// <summary>
/// Mirrors the plausibility check used by the ATA reader so that the rule itself is
/// covered on a platform where raw disk access is impossible (the reader lives in the
/// Windows-only assembly).
/// </summary>
public static class SmartValidator
{
    public static bool IsPlausible(ReadOnlySpan<byte> block)
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
}
