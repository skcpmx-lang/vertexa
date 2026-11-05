namespace Merqo.Vertexa.Windows.Wmi;

/// <summary>Driver metadata joined onto a device instance from Win32_PnPSignedDriver.</summary>
internal readonly record struct WmiDriverInfo(string? Version, string? Date, string? Provider, string? InfName);
