namespace Merqo.Vertexa.Core.Models;

/// <summary>One PnP device as reported by Windows, kept as a typed row for tables and reports.</summary>
public sealed record DeviceRecord(
    string Name,
    string? PnpClass,
    string? InstanceId,
    string? Manufacturer,
    string? DriverVersion,
    string? DriverDate,
    string? DriverProvider,
    int? ProblemCode,
    string Status,
    bool IsUsb,
    string? PhysicalObject)
{
    public bool HasProblem => ProblemCode is not null and not 0;

    public string ProblemText => ProblemCode switch
    {
        null or 0 => "Working",
        3 => "Driver not installed or corrupted (code 3)",
        10 => "Device cannot start (code 10)",
        12 => "Resource conflict (code 12)",
        14 => "Needs restart (code 14)",
        18 => "Reinstall drivers (code 18)",
        19 => "Registry/configuration problem (code 19)",
        21 => "Windows is removing the device (code 21)",
        22 => "Device disabled (code 22)",
        24 => "Device not present or not working (code 24)",
        28 => "Drivers not installed (code 28)",
        31 => "Device not working properly (code 31)",
        32 => "Driver service disabled (code 32)",
        37 => "Driver failed to initialise (code 37)",
        39 => "Driver corrupted or missing (code 39)",
        43 => "Device stopped after reporting a problem (code 43)",
        45 => "Device not connected (code 45)",
        52 => "Driver signature not verified (code 52)",
        _ => $"Problem code {ProblemCode}",
    };
}
