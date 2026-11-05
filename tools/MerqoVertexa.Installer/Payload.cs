using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace Merqo.Vertexa.Installer;

/// <summary>
/// The application payload, embedded in this executable. It is hashed before extraction and the
/// digest must match the value baked in at packaging time; a mismatch aborts the installation so a
/// corrupted or tampered download can never be installed silently.
/// </summary>
internal static class Payload
{
    public static string? ExpectedSha256 =>
        typeof(Payload).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PayloadSha256")?.Value;

    public static byte[] ReadEmbedded()
    {
        var assembly = typeof(Payload).Assembly;
        using var stream = assembly.GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException(
                "This setup executable was built without an application payload. Build it through scripts/package.ps1.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static void VerifyOrThrow(byte[] payload)
    {
        var expected = ExpectedSha256;
        if (string.IsNullOrWhiteSpace(expected))
        {
            InstallerEngine.Log("payload hash: not recorded at build time, skipping digest check");
            return;
        }

        var actual = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        if (!string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException("Payload integrity check failed: the embedded application does not match its published SHA-256 digest.");

        InstallerEngine.Log($"payload hash verified: {actual}");
    }

    public static void ExtractTo(byte[] payload, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        using var archive = new ZipArchive(new MemoryStream(payload, writable: false), ZipArchiveMode.Read);
        var root = Path.GetFullPath(targetDirectory) + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // directory entry
            var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive contains an entry outside the destination folder: " + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        InstallerEngine.Log($"extracted {archive.Entries.Count} entries to {targetDirectory}");
    }
}
