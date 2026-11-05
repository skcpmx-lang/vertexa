namespace Merqo.Vertexa.Core.Contracts;

public readonly record struct ProviderAvailability(bool IsReady, string? Reason)
{
    public static ProviderAvailability Ready() => new(true, null);
    public static ProviderAvailability NotReady(string reason) => new(false, reason);
}

public enum OutcomeKind
{
    Success = 0,
    Unavailable = 1,
    Denied = 2,
    Unsupported = 3,
    Failed = 4,
    Timeout = 5,
}

public readonly record struct ProviderOutcome<T>(OutcomeKind Kind, T? Value, string? Message)
{
    public static ProviderOutcome<T> Success(T value, string? note = null) => new(OutcomeKind.Success, value, note);
    public static ProviderOutcome<T> Unavailable(string reason) => new(OutcomeKind.Unavailable, default, reason);
    public static ProviderOutcome<T> Denied(string reason) => new(OutcomeKind.Denied, default, reason);
    public static ProviderOutcome<T> Unsupported(string reason) => new(OutcomeKind.Unsupported, default, reason);
    public static ProviderOutcome<T> Failed(string reason) => new(OutcomeKind.Failed, default, reason);
    public static ProviderOutcome<T> TimedOut(int milliseconds) => new(OutcomeKind.Timeout, default, $"provider exceeded {milliseconds} ms");
    public bool IsSuccess => Kind == OutcomeKind.Success && Value is not null;
}

/// <summary>
/// One way of obtaining one fact. Several providers may exist for the same capability;
/// they are ordered by <see cref="Priority"/> (0 = most trusted) and tried in turn.
/// </summary>
public interface IDiagnosticProvider<out T>
{
    string Id { get; }
    string Source { get; }
    string DetectionMethod { get; }
    int Priority { get; }
    ValueTask<ProviderAvailability> ProbeAsync(CancellationToken cancellationToken);
    ValueTask<ProviderOutcome<T>> CollectAsync(CancellationToken cancellationToken);
}

public sealed record ProviderAttempt(string ProviderId, string Source, OutcomeKind Kind, string? Message, bool Accepted)
{
    public string Describe() =>
        $"{ProviderId}: {(Accepted ? "accepted" : Kind.ToString().ToLowerInvariant())}{(Message is null ? "" : " — " + Message)}";
}
