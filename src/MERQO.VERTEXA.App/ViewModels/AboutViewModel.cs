using Merqo.Vertexa.App.Services;

namespace Merqo.Vertexa.App.ViewModels;

public sealed class AboutViewModel : PageViewModelBase
{
    private readonly AppHost _host;

    public AboutViewModel(AppHost host)
    {
        _host = host;
        ProductName = "MERQO VERTEXA";
        Brand = "MERQO";
        Version = $"Version {host.AppVersion}";
        Build = $"Build {host.BuildStamp}";
        Founder = "Shohan Khan";
        Contact = "merqoonline@gmail.com";
        Category = "Advanced PC & Laptop Diagnostics Software";
        Copyright = "© 2026 Shohan Khan (MERQO). All rights reserved.";
        LicenceSummary = "Proprietary licence. Personal use on hardware you own or administer; redistribution of binaries is not permitted. See LICENSE in the repository.";
        UnsignedNotice =
            "This build is not code-signed. Windows may show 'Unknown publisher' or a SmartScreen prompt the first time it runs. " +
            "Compare the SHA-256 hash published with the release against the file you downloaded instead of disabling any security feature.";
        ThirdPartyNotices = new[]
        {
            ".NET 8 runtime and WPF — MIT (Microsoft Corporation)",
            "System.Management 8.0.0 — MIT (Microsoft Corporation)",
            "Microsoft.Data.Sqlite 8.0.0 / SQLite — MIT (Microsoft); SQLite is public domain",
            "xUnit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk — Apache-2.0 / MIT (test-only dependencies)",
            "No third-party UI, charting or PDF libraries are used: those components are implemented in this repository.",
        };
        PrivacySummary = new[]
        {
            "VERTEXA performs no network requests while diagnosing. There is no account, no telemetry and no cloud component.",
            "Everything read is hardware and operating-system state exposed by Windows; documents, media and browser data are never accessed.",
            "Serial numbers and MAC addresses are redacted in exported reports unless you explicitly opt out.",
            "Microphone recordings exist only in memory unless you choose to save them, and are never uploaded.",
            "Diagnostic history and logs live in %LOCALAPPDATA%\\MERQO\\VERTEXA and can be deleted from Settings.",
        };
        Platform = $"Windows build {Environment.OSVersion.Version.Build} · {(Environment.Is64BitOperatingSystem ? "x64" : "x86")} · {(host.IsElevated ? "elevated session" : "standard session")}";
        DataLocation = host.DataDirectory;
    }

    public override string Key => "about";
    public override string Title => "About";
    public override string Subtitle => "Product identity, privacy and third-party notices.";

    public string ProductName { get; }
    public string Brand { get; }
    public string Version { get; }
    public string Build { get; }
    public string Founder { get; }
    public string Contact { get; }
    public string Category { get; }
    public string Copyright { get; }
    public string LicenceSummary { get; }
    public string UnsignedNotice { get; }
    public string[] ThirdPartyNotices { get; }
    public string[] PrivacySummary { get; }
    public string Platform { get; }
    public string DataLocation { get; }
}
