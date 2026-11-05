using System.Globalization;

namespace Merqo.Vertexa.Shared;

/// <summary>
/// The single contract every measurement in MERQO VERTEXA travels through.
/// Construction requires an explicit source and, for unavailable states, a reason —
/// which makes fabricated or anonymously-sourced values impossible by design.
/// </summary>
public sealed class DiagnosticValue<T> : IDiagnosticMetric
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private DiagnosticValue(
        string name,
        T? value,
        string unit,
        AvailabilityState availability,
        ConfidenceLevel confidence,
        VerificationState verification,
        string source,
        string method,
        DateTimeOffset collectedAt,
        string? unavailableReason,
        string? limitation,
        IReadOnlyList<string> notes)
    {
        Name = name;
        Value = value;
        Unit = unit ?? string.Empty;
        Availability = availability;
        Confidence = confidence;
        Verification = verification;
        Source = source ?? string.Empty;
        DetectionMethod = method ?? string.Empty;
        CollectedAt = collectedAt;
        UnavailableReason = unavailableReason;
        Limitation = limitation;
        Notes = notes ?? Array.Empty<string>();
    }

    public string Name { get; }
    public T? Value { get; }
    public string Unit { get; }
    public AvailabilityState Availability { get; }
    public ConfidenceLevel Confidence { get; }
    public VerificationState Verification { get; }
    public string Source { get; }
    public string DetectionMethod { get; }
    public DateTimeOffset CollectedAt { get; }
    public string? UnavailableReason { get; }
    public string? Limitation { get; }
    public IReadOnlyList<string> Notes { get; }

    public bool HasData => Availability == AvailabilityState.Available && Value is not null;

    public string DisplayValue => HasData ? Format(Value) : Availability.Label();

    /// <summary>Value followed by unit, for tables and reports. Never shown for non-data states.</summary>
    public string DisplayWithUnit => HasData && Unit.Length > 0 ? DisplayValue + " " + Unit : DisplayValue;

    public static DiagnosticValue<T> FromAvailable(
        string name,
        T value,
        string source,
        string method,
        string unit = "",
        ConfidenceLevel confidence = ConfidenceLevel.High,
        VerificationState verification = VerificationState.SingleSource,
        DateTimeOffset? collectedAt = null,
        string? limitation = null,
        IReadOnlyList<string>? notes = null)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A diagnostic value must name its source.", nameof(source));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("A diagnostic value must name its detection method.", nameof(method));
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Use FromUnavailable for absent values.");
        return new DiagnosticValue<T>(name, value, unit, AvailabilityState.Available, confidence,
            verification, source, method, collectedAt ?? DateTimeOffset.UtcNow, null, limitation, notes ?? Array.Empty<string>());
    }

    public static DiagnosticValue<T> FromUnavailable(
        string name,
        AvailabilityState state,
        string reason,
        string source = "",
        string method = "",
        string unit = "",
        string? limitation = null,
        IReadOnlyList<string>? notes = null)
    {
        if (state == AvailabilityState.Available)
            throw new ArgumentException("Unavailable factory cannot produce an Available state.", nameof(state));
        if (string.IsNullOrWhiteSpace(reason))
            reason = "No data source returned a usable value.";
        return new DiagnosticValue<T>(name, default, unit, state, ConfidenceLevel.Unknown,
            VerificationState.Unverified, source, method, DateTimeOffset.UtcNow, reason, limitation,
            notes ?? Array.Empty<string>());
    }

    public static DiagnosticValue<T> NotTested(string name, string reason) =>
        FromUnavailable(name, AvailabilityState.NotTested, reason);

    private string Format(T? value)
    {
        object? boxed = value;
        return boxed switch
        {
            null => Availability.Label(),
            string s => s,
            bool b => b ? "Yes" : "No",
            double d => d.ToString(Math.Abs(d) >= 100 ? "N0" : "N1", Inv),
            float f => f.ToString("N1", Inv),
            decimal m => m.ToString("N1", Inv),
            int i => i.ToString("N0", Inv),
            long l => l.ToString("N0", Inv),
            uint ui => ui.ToString("N0", Inv),
            ulong ul => ul.ToString("N0", Inv),
            TimeSpan t => Units.Duration(t),
            DateTimeOffset dto => dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Inv),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
            _ => Convert.ToString(boxed, Inv) ?? string.Empty,
        };
    }

    public override string ToString() => $"{Name}: {DisplayWithUnit} [{Availability.ShortCode()}/{Source}]";
}

/// <summary>Factories for the common "nothing here" shapes, with consistent reasons.</summary>
public static class DiagnosticValue
{
    public static DiagnosticValue<T> NotSupportedByPlatform<T>(string name, string detail) =>
        DiagnosticValue<T>.FromUnavailable(name, AvailabilityState.Unsupported,
            detail, limitation: detail);

    public static DiagnosticValue<T> NeedsAdmin<T>(string name, string detail) =>
        DiagnosticValue<T>.FromUnavailable(name, AvailabilityState.RequiresAdministrator, detail);

    public static DiagnosticValue<T> Absent<T>(string name, string detail) =>
        DiagnosticValue<T>.FromUnavailable(name, AvailabilityState.NotApplicable, detail);
}
