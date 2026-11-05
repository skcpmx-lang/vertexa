using System.Runtime.InteropServices;

namespace Merqo.Vertexa.Installer;

/// <summary>
/// MERQO VERTEXA per-user installer.
///
/// Design constraints (see docs/BUILD_AND_RELEASE.md):
///   * no administrator rights required — everything is written under %LOCALAPPDATA% and HKCU;
///   * the application payload is embedded in this executable and verified against a SHA-256
///     digest baked in at build time before a single file is written;
///   * no network access, no bundled downloader, no telemetry, no shell execution.
///
/// Usage:
///   MerqoVertexaSetup.exe                 interactive install
///   MerqoVertexaSetup.exe /S              silent install
///   MerqoVertexaSetup.exe /S /D=C:\path   silent install to a custom directory
///   MerqoVertexaSetup.exe /S /DESKTOP     also create a desktop shortcut
///   MerqoVertexaSetup.exe /UNINSTALL /S   silent uninstall
/// </summary>
internal static class Program
{
    private const uint MbYesNo = 0x00000004;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbIconError = 0x00000010;
    internal const uint MbIconInformation = 0x00000040;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    internal static void Message(string text, uint icon) =>
        MessageBoxW(IntPtr.Zero, text, InstallerEngine.ProductName, icon);

    internal static bool Confirm(string text) =>
        MessageBoxW(IntPtr.Zero, text, InstallerEngine.ProductName, MbYesNo | MbIconQuestion) == IdYes;

    private static int Main(string[] args)
    {
        var options = InstallerOptions.Parse(args);
        try
        {
            if (options.Uninstall)
            {
                if (!options.Silent && !Confirm($"Remove {InstallerEngine.ProductName} from this computer?\n\n"
                        + $"Installation folder: {options.ResolveInstallDirectory()}\n\n"
                        + "Your diagnostic history and settings in %LOCALAPPDATA%\\MERQO\\VERTEXA are kept."))
                    return InstallerEngine.ExitCancelled;

                return InstallerEngine.Uninstall(options);
            }

            if (!options.Silent && !Confirm($"Install {InstallerEngine.ProductName} {InstallerEngine.Version} for the current user?\n\n"
                    + $"Destination: {options.ResolveInstallDirectory()}\n\n"
                    + "No administrator rights are required. No system settings are modified. "
                    + "The installer works offline and never contacts the internet."))
                return InstallerEngine.ExitCancelled;

            var result = InstallerEngine.Install(options);
            if (!options.Silent)
            {
                Message(result == 0
                        ? $"{InstallerEngine.ProductName} {InstallerEngine.Version} is installed.\n\n"
                          + $"Launch it from the Start Menu, or run:\n{Path.Combine(options.ResolveInstallDirectory(), "MerqoVertexa.exe")}\n\n"
                          + "The build is unsigned, so Windows SmartScreen may ask for confirmation the first time."
                        : $"Installation failed. Details were written to:\n{InstallerEngine.LogPath}",
                    result == 0 ? MbIconInformation : MbIconError);
            }
            return result;
        }
        catch (Exception ex)
        {
            InstallerEngine.Log("FATAL " + ex);
            if (!options.Silent) Message("Installation failed:\n\n" + ex.Message + "\n\nLog: " + InstallerEngine.LogPath, MbIconError);
            return InstallerEngine.ExitFailed;
        }
    }
}
