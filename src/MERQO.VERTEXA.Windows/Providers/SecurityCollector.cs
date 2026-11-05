using Merqo.Vertexa.Core.Models;
using Merqo.Vertexa.Shared;
using Merqo.Vertexa.Windows.Wmi;
using Microsoft.Win32;

namespace Merqo.Vertexa.Windows.Providers;

/// <summary>Secure Boot, TPM and virtualisation-based security — read-only inspection.</summary>
internal sealed class SecurityCollector : WindowsCollectorBase
{
    public SecurityCollector(IAppLogger? log = null) : base(log) { }
    public override string SectionKey => SectionKeys.Security;
    public override string Title => "Security platform";
    public override int Order => 30;

    protected override void CollectCore(DiagnosticSnapshot snapshot, CancellationToken cancellationToken)
    {
        var section = Section(snapshot);

        try
        {
            using var stateKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            var value = stateKey?.GetValue("UEFISecureBootEnabled");
            if (value is int enabled)
                section.Metric("Secure Boot state", enabled == 1 ? "Enabled" : "Disabled",
                    @"REG:HKLM\SYSTEM\...\SecureBoot\State", "Registry read");
            else
                section.Missing<string>("Secure Boot state", AvailabilityState.Unsupported,
                    "SecureBoot\\State key is absent (legacy BIOS boot)", @"REG:HKLM\SYSTEM\...\SecureBoot\State", "Registry read");
        }
        catch (Exception ex)
        {
            section.Missing<string>("Secure Boot state", AvailabilityState.AccessDenied, ex.Message, "registry", "Registry read");
        }

        var tpm = Wmi("SELECT SpecVersion, ManufacturerId, ManufacturerVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm", WmiQuery.TpmScope);
        if (tpm.First is { } tpmRow)
        {
            var present = tpmRow.Text("SpecVersion") is not null;
            section.Metric("TPM present", present ? "Present" : "Not present", "root\\cimv2\\security\\microsofttpm:Win32_Tpm", "CIM query");
            if (tpmRow.Text("SpecVersion") is { } spec)
                section.Metric("TPM specification", spec.Trim(), "Win32_Tpm", "CIM query", confidence: ConfidenceLevel.Medium);
            if (tpmRow.Text("ManufacturerVersion") is { } version)
                section.Metric("TPM firmware", version.Trim(), "Win32_Tpm", "CIM query", confidence: ConfidenceLevel.Medium);
            if (tpmRow.Bool("IsEnabled_InitialValue") is { } enabled) section.Metric("TPM enabled", enabled, "Win32_Tpm", "CIM query");
        }
        else
        {
            section.Missing<string>("TPM present", AvailabilityState.Unavailable,
                tpm.Error ?? "TPM WMI provider not available (may require elevation or a discrete TPM)",
                "root\\cimv2\\security\\microsofttpm", WmiQuery.TpmScope);
        }

        try
        {
            using var vbsKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            var enabled = vbsKey?.GetValue("EnableVirtualizationBasedSecurity");
            if (enabled is int vbsEnabled)
                section.Metric("Virtualisation-based security", vbsEnabled == 1 ? "Enabled" : "Not enabled",
                    @"REG:HKLM\SYSTEM\...\DeviceGuard", "Registry read", confidence: ConfidenceLevel.Medium);
            else
                section.Missing<string>("Virtualisation-based security", AvailabilityState.Unavailable,
                    "DeviceGuard configuration value absent", "registry", "Registry read");

            using var runningKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            var hvci = runningKey?.GetValue("Enabled");
            if (hvci is int hvciValue)
                section.Metric("Memory integrity (HVCI)", hvciValue == 1 ? "Running" : "Not running",
                    @"REG:HKLM\SYSTEM\...\HypervisorEnforcedCodeIntegrity", "Registry read", confidence: ConfidenceLevel.Medium);
        }
        catch
        {
            section.Missing<string>("Virtualisation-based security", AvailabilityState.AccessDenied, "DeviceGuard key not readable", "registry", "Registry read");
        }

        var defender = Wmi("SELECT AntivirusEnabled, RealTimeProtectionEnabled, SignatureVersion, AntivirusSignatureLastUpdated, AMServiceEnabled FROM MSFT_MpComputerStatus",
            @"root\Microsoft\Windows\Defender");
        if (defender.First is { } mp)
        {
            if (mp.Bool("RealTimeProtectionEnabled") is { } rtp)
                section.Metric("Defender real-time protection", rtp, "root\\Microsoft\\Windows\\Defender:MSFT_MpComputerStatus", "CIM query",
                    confidence: ConfidenceLevel.Medium);
            if (mp.Text("SignatureVersion") is { } signature)
                section.Metric("Defender signature version", signature, "MSFT_MpComputerStatus", "CIM query", confidence: ConfidenceLevel.Medium);
        }
        else
        {
            section.Missing<bool>("Defender real-time protection", AvailabilityState.Unavailable,
                defender.Error ?? "Defender WMI provider did not answer (third-party AV or disabled service)",
                "root\\Microsoft\\Windows\\Defender", "CIM query");
        }

        var devices = Wmi("SELECT DeviceName, VirtualizationBasedSecurityStatus FROM Win32_DeviceGuard", WmiQuery.DefaultScope);
        if (devices.First?.Int("VirtualizationBasedSecurityStatus") is { } status)
        {
            section.Metric("Device Guard VBS status", status switch
            {
                0 => "Not enabled",
                1 => "Enabled but not running",
                2 => "Running",
                _ => "Unknown state " + status,
            }, "WMI:Win32_DeviceGuard", "CIM query", confidence: ConfidenceLevel.Medium);
        }
    }
}
