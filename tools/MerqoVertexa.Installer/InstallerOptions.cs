namespace Merqo.Vertexa.Installer;

internal sealed class InstallerOptions
{
    public bool Silent { get; private init; }
    public bool Uninstall { get; private init; }
    public bool DesktopShortcut { get; private init; }
    public string? InstallDirectory { get; private init; }

    public string ResolveInstallDirectory() =>
        string.IsNullOrWhiteSpace(InstallDirectory) ? InstallerEngine.DefaultInstallDirectory : InstallDirectory!;

    public static InstallerOptions Parse(string[] args)
    {
        var silent = false;
        var uninstall = false;
        var desktop = false;
        string? directory = null;

        foreach (var raw in args)
        {
            var arg = raw.Trim();
            if (arg.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/VERYSILENT", StringComparison.OrdinalIgnoreCase)) { silent = true; continue; }
            if (arg.Equals("/UNINSTALL", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)) { uninstall = true; continue; }
            if (arg.Equals("/DESKTOP", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--desktop", StringComparison.OrdinalIgnoreCase)) { desktop = true; continue; }
            if (arg.StartsWith("/D=", StringComparison.OrdinalIgnoreCase)) directory = arg[3..].Trim('"');
            else if (arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase)) directory = arg[6..].Trim('"');
        }

        if (directory is not null)
        {
            directory = Path.GetFullPath(directory);
            if (directory.Length < 4 || !Path.IsPathFullyQualified(directory))
                throw new ArgumentException("Install directory must be a fully qualified path outside the drive root.");
        }

        return new InstallerOptions { Silent = silent, Uninstall = uninstall, DesktopShortcut = desktop, InstallDirectory = directory };
    }
}
