using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace Merqo.Vertexa.Installer;

internal static class InstallerEngine
{
    public const string ProductName = "MERQO VERTEXA";
    public const string Publisher = "MERQO";
    public const int ExitOk = 0;
    public const int ExitCancelled = 1;
    public const int ExitFailed = 2;

    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MERQOVERTEXA";
    private const string AppPathKey = @"Software\MERQO\VERTEXA\Install";

    public static string Version
    {
        get
        {
            var metadata = typeof(InstallerEngine).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .Cast<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ProductVersion")?.Value;
            return string.IsNullOrWhiteSpace(metadata) ? "1.0.0" : metadata!;
        }
    }

    public static string DefaultInstallDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", ProductName);

    public static string LogPath => Path.Combine(Path.GetTempPath(), "MerqoVertexaSetup.log");

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the installation.
        }
    }

    public static int Install(InstallerOptions options)
    {
        var target = options.ResolveInstallDirectory();
        Log($"install requested → {target} (silent={options.Silent}, desktop={options.DesktopShortcut})");

        var payload = Payload.ReadEmbedded();
        Payload.VerifyOrThrow(payload);

        if (Directory.Exists(target))
        {
            var running = Process.GetProcessesByName("MerqoVertexa");
            if (running.Length > 0)
                throw new InvalidOperationException("MERQO VERTEXA is running. Close the application and run the installer again.");
        }

        Payload.ExtractTo(payload, target);

        var exePath = Path.Combine(target, "MerqoVertexa.exe");
        if (!File.Exists(exePath)) throw new InvalidOperationException("The payload did not contain MerqoVertexa.exe.");
        Log($"application version on disk: {FileVersionInfo.GetVersionInfo(exePath).FileVersion}");

        // A copy of this setup executable becomes the uninstaller so the entry in
        // Apps & features always has a valid target.
        var uninstaller = Path.Combine(target, "Uninstall.exe");
        var self = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(self) && File.Exists(self) && !Path.GetFullPath(self).Equals(Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
            File.Copy(self, uninstaller, overwrite: true);

        Directory.CreateDirectory(Path.GetDirectoryName(Shortcuts.StartMenuPath)!);
        Shortcuts.Create(Shortcuts.StartMenuPath, exePath, target, "MERQO VERTEXA — Advanced PC & Laptop Diagnostics", exePath);
        if (options.DesktopShortcut)
            Shortcuts.Create(Shortcuts.DesktopPath, exePath, target, "MERQO VERTEXA — Advanced PC & Laptop Diagnostics", exePath);
        else
            Shortcuts.Delete(Shortcuts.DesktopPath);

        InstallDateAndRegistry(target, exePath, uninstaller);
        Log("installation completed");
        return ExitOk;
    }

    private static void InstallDateAndRegistry(string target, string exePath, string uninstaller)
    {
        var estimatedKb = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length) / 1024;

        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath, writable: true))
        {
            Assert(key is not null, "uninstall registry key");
            key!.SetValue("DisplayName", ProductName);
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", Publisher);
            key.SetValue("DisplayIcon", exePath + ",0");
            key.SetValue("InstallLocation", target);
            key.SetValue("UninstallString", $"\"{uninstaller}\" /UNINSTALL");
            key.SetValue("QuietUninstallString", $"\"{uninstaller}\" /UNINSTALL /S");
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("EstimatedSize", (int)estimatedKb, RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("URLInfoAbout", "mailto:merqoonline@gmail.com");
            key.SetValue("Comments", "Advanced PC & Laptop Diagnostics Software. Unsigned build; see the release notes for the SmartScreen explanation.");
        }

        using var appKey = Registry.CurrentUser.CreateSubKey(AppPathKey, writable: true);
        appKey?.SetValue("InstallPath", target);
        appKey?.SetValue("Version", Version);
        Log("registry entries written under HKCU");
    }

    private static void Assert(bool condition, string what)
    {
        if (!condition) throw new SecurityException("Could not write the " + what + " registry entry.");
    }

    public static int Uninstall(InstallerOptions options)
    {
        var target = ResolveInstalledDirectory(options);
        Log($"uninstall requested ← {target}");

        var running = Process.GetProcessesByName("MerqoVertexa");
        if (running.Length > 0)
            throw new InvalidOperationException("MERQO VERTEXA is running. Close the application and try again.");

        Shortcuts.Delete(Shortcuts.StartMenuPath);
        Shortcuts.Delete(Shortcuts.DesktopPath);
        TryDeleteKey(UninstallKeyPath);
        TryDeleteKey(AppPathKey);

        if (Directory.Exists(target))
        {
            var self = Environment.ProcessPath;
            foreach (var file in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (self is not null && Path.GetFullPath(file).Equals(Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase))
                    {
                        ScheduleDeleteOnReboot(file);
                        continue;
                    }
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log("could not delete " + file + ": " + ex.Message);
                }
            }

            foreach (var directory in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(directory); } catch { }
            }

            try { Directory.Delete(target); } catch { /* the uninstaller copy may still hold the folder */ }
        }

        Log("uninstall completed; user data in %LOCALAPPDATA%\\MERQO\\VERTEXA was preserved");
        if (!options.Silent)
            Program.Message($"{ProductName} has been removed.\n\nYour diagnostic history and settings were kept in:\n%LOCALAPPDATA%\\MERQO\\VERTEXA\n\nDelete that folder to remove them as well.", Program.MbIconInformation);
        return ExitOk;
    }

    private static string ResolveInstalledDirectory(InstallerOptions options)
    {
        if (options.InstallDirectory is not null) return options.InstallDirectory;
        using var key = Registry.CurrentUser.OpenSubKey(AppPathKey);
        if (key?.GetValue("InstallPath") is string installed && !string.IsNullOrWhiteSpace(installed))
            return installed;
        return DefaultInstallDirectory;
    }

    private static void TryDeleteKey(string path)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            Log("registry key removed: HKCU\\" + path);
        }
        catch (Exception ex)
        {
            Log("registry key removal failed for " + path + ": " + ex.Message);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string existingFileName, string? newFileName, uint flags);

    private static void ScheduleDeleteOnReboot(string path)
    {
        const uint DelayUntilReboot = 0x4;
        if (!MoveFileExW(path, null, DelayUntilReboot))
            Log("could not schedule deletion of " + path);
    }
}
