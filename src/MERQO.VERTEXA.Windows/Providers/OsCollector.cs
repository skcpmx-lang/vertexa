using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Microsoft.Win32;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>Windows edition/build/architecture/activation and update inventory.</summary>
internal sealed class OsCollector : WindowsCollectorBase
{
    private const string RegSource = @"REG:HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string RegMethod = "Registry read";

    public OsCollector(IAppLogger? log = null) : base(log) { }

    public override string SectionKey => SectionKeys.Os;
    public override string Title => "Windows";
    public override int Order => 10;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        if (key is null)
        {
            section.Missing<string>("Windows edition", AvailabilityState.AccessDenied, "CurrentVersion key is not readable", RegSource, RegMethod);
        }
        else
        {
            var productName = key.GetValue("ProductName") as string;
            var edition = key.GetValue("EditionID") as string;
            var displayVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
            var build = key.GetValue("CurrentBuildNumber") as string;
            var ubr = key.GetValue("UBR")?.ToString();
            var installDate = ReadInstallDate();

            if (productName is not null) section.Metric("Windows edition", productName, RegSource, RegMethod);
            else section.Missing<string>("Windows edition", AvailabilityState.Unavailable, "ProductName value missing", RegSource, RegMethod);

            if (displayVersion is not null) section.Metric("Version", displayVersion, RegSource, RegMethod);
            if (build is not null)
                section.Metric("Build", ubr is null ? build : $"{build}.{ubr}", RegSource, RegMethod);
            if (edition is not null) section.Metric("Edition ID", edition, RegSource, RegMethod);

            if (installDate is not null)
                section.Metric("Install date", installDate.Value, RegSource, RegMethod, confidence: ConfidenceLevel.Medium);
        }

        var os = Wmi("SELECT Caption, Version, BuildNumber, OSArchitecture, SerialNumber, InstallDate, LastBootUpTime, TotalVisibleMemorySize, FreePhysicalMemory, WindowsDirectory FROM Win32_OperatingSystem");
        var row = os.First;
        if (row is null)
        {
            MissingFromQuery<string>(section, "OS caption", os, "WMI:Win32_OperatingSystem", "CIM query");
        }
        else
        {
            if (row.Text("Caption") is { } caption) section.Metric("OS caption", caption, "WMI:Win32_OperatingSystem", "CIM query");
            if (row.Text("Version") is { } version) section.Metric("Version string", version, "WMI:Win32_OperatingSystem", "CIM query");
            if (row.Text("OSArchitecture") is { } arch) section.Metric("Architecture", arch, "WMI:Win32_OperatingSystem", "CIM query");
            if (row.Text("WindowsDirectory") is { } winDir) section.Metric("Windows directory", winDir, "WMI:Win32_OperatingSystem", "CIM query");
        }

        section.Metric("Process architecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ".NET RuntimeInformation", "Process query", confidence: ConfidenceLevel.High);
        section.Metric("OS architecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            ".NET RuntimeInformation", "Process query");

        // Activation: SARA/SoftwareLicensingProduct gives a coarse, legitimate status.
        var licensing = Wmi("SELECT Name, LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'");
        if (licensing.First is { } licence)
        {
            var status = licence.Int("LicenseStatus");
            var text = status switch
            {
                0 => "Unlicensed",
                1 => "Licensed",
                2 => "Out-of-box grace period",
                3 => "Out-of-tolerance grace period",
                4 => "Non-genuine grace period",
                5 => "Notification (not activated)",
                6 => "Extended grace period",
                _ => "Unknown state",
            };
            section.Metric("Activation status", text, "WMI:SoftwareLicensingProduct", "CIM query",
                confidence: status is null ? ConfidenceLevel.Low : ConfidenceLevel.Medium,
                limitation: "Only the licence status is read; no product keys are retrieved.");
        }
        else
        {
            section.Missing<string>("Activation status", AvailabilityState.Unavailable,
                licensing.Error ?? "licensing service did not answer", "WMI:SoftwareLicensingProduct", "CIM query");
        }

        var hotfixes = Wmi("SELECT HotFixID, InstalledOn FROM Win32_QuickFixEngineering");
        if (hotfixes.Ok)
        {
            section.Metric("Installed updates (enumerated)", hotfixes.Rows.Count, "WMI:Win32_QuickFixEngineering", "CIM query",
                confidence: ConfidenceLevel.Medium,
                limitation: "Lists servicing-stack updates; feature updates are not included.");
            var latest = hotfixes.Rows
                .Select(r => (Id: r.Text("HotFixID"), On: r.Text("InstalledOn")))
                .Where(r => r.Id is not null)
                .OrderByDescending(r => r.On, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (latest.Id is not null)
                section.Metric("Most recent update", latest.On is null ? latest.Id : $"{latest.Id} ({latest.On})",
                    "WMI:Win32_QuickFixEngineering", "CIM query");
        }
        else
        {
            MissingFromQuery<int>(section, "Installed updates (enumerated)", hotfixes, "WMI:Win32_QuickFixEngineering", "CIM query");
        }

        // Firmware boot mode — decisive for several later findings.
        if (Native.NativeMethods.GetFirmwareType(out var firmwareType))
        {
            section.Metric("Firmware boot mode", firmwareType switch
            {
                1 => "Legacy BIOS",
                2 => "UEFI",
                3 => "Maximum (UEFI)",
                _ => "Unknown",
            }, "Kernel32!GetFirmwareType", "Native API");
        }
        else
        {
            section.Missing<string>("Firmware boot mode", AvailabilityState.Unsupported, "GetFirmwareType failed",
                "Kernel32!GetFirmwareType", "Native API");
        }
    }

    private static DateTime? ReadInstallDate()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key?.GetValue("InstallDate") is int seconds && seconds > 0)
                return DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
            if (key?.GetValue("InstallTime") is long fileTime && fileTime > 0)
                return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch
        {
            // Non-fatal: install date simply stays unavailable.
        }
        return null;
    }
}
