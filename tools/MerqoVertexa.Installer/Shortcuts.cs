using System.Reflection;

namespace Merqo.Vertexa.Installer;

/// <summary>
/// Creates .lnk files through the Windows Script Host object model using late binding, so the
/// installer needs no COM interop assemblies and no extra dependency.
/// </summary>
internal static class Shortcuts
{
    public static void Create(string linkPath, string targetPath, string workingDirectory, string description, string iconPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable; cannot create shortcuts.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            shortcut.IconLocation = iconPath + ",0";
            shortcut.WindowStyle = 1;
            shortcut.Save();
            InstallerEngine.Log("shortcut created: " + linkPath);
        }
        finally
        {
            if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    public static void Delete(string linkPath)
    {
        try
        {
            if (File.Exists(linkPath))
            {
                File.Delete(linkPath);
                InstallerEngine.Log("shortcut removed: " + linkPath);
            }
        }
        catch (Exception ex)
        {
            InstallerEngine.Log("shortcut removal failed: " + ex.Message);
        }
    }

    public static string StartMenuPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), InstallerEngine.ProductName, InstallerEngine.ProductName + ".lnk");

    public static string DesktopPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), InstallerEngine.ProductName + ".lnk");
}
