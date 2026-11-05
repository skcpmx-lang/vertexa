using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Benchmarks;

public sealed class SafetyLimits
{
    public double MaxTemperatureCelsius { get; init; } = 95;
    public TimeSpan MaxDuration { get; init; } = TimeSpan.FromMinutes(10);
    public bool AllowWhenTemperatureUnknown { get; init; } = true;
}

public sealed record SafetyDecision(bool Allowed, string Reason, string? Warning = null);

/// <summary>
/// Guards every intensive test. It can only *allow* a run when the evidence it can see
/// says it is safe; it never claims to be able to prevent hardware failure.
/// </summary>
public sealed class SafetyGovernor
{
    private readonly IAppLogger _log;
    public SafetyGovernor(IAppLogger? log = null) => _log = log ?? NullLogger.Instance;

    public SafetyDecision EvaluateStart(double? currentTemperatureCelsius, SafetyLimits limits)
    {
        if (currentTemperatureCelsius is null)
        {
            return limits.AllowWhenTemperatureUnknown
                ? new SafetyDecision(true, "Temperature telemetry unavailable; conservative duration applies.",
                    "No temperature sensor is exposed, so VERTEXA cannot abort on heat. Keep the configured duration short.")
                : new SafetyDecision(false, "Temperature telemetry unavailable and the policy requires it.");
        }

        if (currentTemperatureCelsius >= limits.MaxTemperatureCelsius)
            return new SafetyDecision(false,
                $"Current temperature {currentTemperatureCelsius:0.0} °C is at or above the configured ceiling of {limits.MaxTemperatureCelsius:0} °C.");

        if (currentTemperatureCelsius >= limits.MaxTemperatureCelsius - 10)
            return new SafetyDecision(true, "Starting warm.",
                $"Already at {currentTemperatureCelsius:0.0} °C; the test will abort if it reaches {limits.MaxTemperatureCelsius:0} °C.");

        return new SafetyDecision(true, "Within thermal limits.");
    }

    /// <summary>Evaluated every second while a test runs.</summary>
    public SafetyDecision EvaluateDuringRun(double? currentTemperatureCelsius, TimeSpan elapsed, SafetyLimits limits)
    {
        if (elapsed >= limits.MaxDuration)
            return new SafetyDecision(false, $"Duration limit of {limits.MaxDuration.TotalMinutes:0} minutes reached.");

        if (currentTemperatureCelsius is { } temperature && temperature >= limits.MaxTemperatureCelsius)
        {
            _log.Warn("safety", $"aborting test: {temperature:0.0} °C >= {limits.MaxTemperatureCelsius:0} °C");
            return new SafetyDecision(false, $"Thermal ceiling reached ({temperature:0.0} °C). Test aborted automatically.");
        }

        if (currentTemperatureCelsius is { } warm && warm >= limits.MaxTemperatureCelsius - 5)
            return new SafetyDecision(true, "Approaching thermal ceiling.", $"At {warm:0.0} °C; next check may abort.");

        return new SafetyDecision(true, "Running.");
    }
}
