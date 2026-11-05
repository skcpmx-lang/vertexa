namespace Merqo.Vertexa.Shared;

/// <summary>
/// Availability of a measured fact. The product rule is that a metric is never
/// rendered without one of these states; there is no "assume zero" path.
/// </summary>
public enum AvailabilityState
{
    Available = 0,
    /// <summary>Some fields of a composite metric resolved, others did not.</summary>
    PartiallyAvailable = 1,
    /// <summary>The source exists but returned nothing usable this time.</summary>
    Unavailable = 2,
    /// <summary>The platform/hardware/driver does not expose this at all.</summary>
    Unsupported = 3,
    /// <summary>Requires a permission (usually elevation) the process does not hold.</summary>
    AccessDenied = 4,
    /// <summary>Legitimately not applicable to this machine (e.g. battery on a desktop).</summary>
    NotApplicable = 5,
    /// <summary>Test not executed yet — an explicit, honest state, not a zero.</summary>
    NotTested = 6,
    RequiresUserInteraction = 7,
    RequiresAdministrator = 8,
    /// <summary>Only a human/physical action can establish this fact.</summary>
    RequiresPhysicalHardware = 9,
    /// <summary>A source answered but the value failed plausibility validation.</summary>
    Invalid = 10,
}

/// <summary>Evidence quality of a value. Not a statistical probability.</summary>
public enum ConfidenceLevel
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>How the value was corroborated.</summary>
public enum VerificationState
{
    Unverified = 0,
    SingleSource = 1,
    /// <summary>Two or more independent providers agreed within tolerance.</summary>
    Corroborated = 2,
    /// <summary>Providers disagreed; both values are preserved.</summary>
    Disputed = 3,
    UserConfirmed = 4,
}

public static class AvailabilityText
{
    public static string Label(this AvailabilityState state) => state switch
    {
        AvailabilityState.Available => "Available",
        AvailabilityState.PartiallyAvailable => "Partial",
        AvailabilityState.Unavailable => "Unavailable",
        AvailabilityState.Unsupported => "Unsupported",
        AvailabilityState.AccessDenied => "Access denied",
        AvailabilityState.NotApplicable => "Not applicable",
        AvailabilityState.NotTested => "Not tested",
        AvailabilityState.RequiresUserInteraction => "Needs interaction",
        AvailabilityState.RequiresAdministrator => "Needs admin",
        AvailabilityState.RequiresPhysicalHardware => "Physical test required",
        AvailabilityState.Invalid => "Invalid reading",
        _ => "Unknown",
    };

    public static string ShortCode(this AvailabilityState state) => state switch
    {
        AvailabilityState.Available => "OK",
        AvailabilityState.PartiallyAvailable => "PART",
        AvailabilityState.Unavailable => "N/A",
        AvailabilityState.Unsupported => "UNSUP",
        AvailabilityState.AccessDenied => "DENIED",
        AvailabilityState.NotApplicable => "NAP",
        AvailabilityState.NotTested => "UNTESTED",
        AvailabilityState.RequiresUserInteraction => "USER",
        AvailabilityState.RequiresAdministrator => "ADMIN",
        AvailabilityState.RequiresPhysicalHardware => "PHYS",
        AvailabilityState.Invalid => "INVALID",
        _ => "UNK",
    };
}
