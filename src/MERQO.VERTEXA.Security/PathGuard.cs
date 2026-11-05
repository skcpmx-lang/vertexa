namespace Merqo.Vertexa.Security;

/// <summary>
/// Validates user-supplied export paths: absolute, normalised, no traversal, no device
/// names, and (for writes) inside a directory the user actually picked.
/// </summary>
public static class PathGuard
{
    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] InvalidFileChars = Path.GetInvalidFileNameChars();

    public sealed record ValidationResult(bool IsValid, string? SanitisedPath, string? Error);

    public static ValidationResult ValidateFileTarget(string? requestedPath, string? allowedRoot = null)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            return new ValidationResult(false, null, "No file path was provided.");

        string full;
        try
        {
            full = Path.GetFullPath(requestedPath.Trim());
        }
        catch (Exception ex)
        {
            return new ValidationResult(false, null, "The path could not be resolved: " + ex.Message);
        }

        var fileName = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(fileName))
            return new ValidationResult(false, null, "The path does not name a file.");
        if (fileName.IndexOfAny(InvalidFileChars) >= 0)
            return new ValidationResult(false, null, "The file name contains characters Windows does not allow.");

        var bareName = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();
        if (ReservedNames.Contains(bareName))
            return new ValidationResult(false, null, "'" + bareName + "' is a reserved Windows device name.");
        if (fileName.EndsWith('.') || fileName.EndsWith(' '))
            return new ValidationResult(false, null, "File names may not end with a dot or a space.");
        if (full.Contains(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return new ValidationResult(false, null, "The path contains a parent-directory traversal.");

        if (!string.IsNullOrWhiteSpace(allowedRoot))
        {
            var root = Path.GetFullPath(allowedRoot);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return new ValidationResult(false, null, "The target must be inside the selected folder.");
        }

        return new ValidationResult(true, full, null);
    }

    /// <summary>Guarantees a unique file name so an export can never clobber user data silently.</summary>
    public static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}
