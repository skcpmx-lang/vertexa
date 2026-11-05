using Merqo.Vertexa.Shared;

namespace Merqo.Vertexa.Core.Contracts;

/// <summary>Outcome of trying every provider for one capability.</summary>
public sealed class ResolveResult<T>
{
    public ResolveResult(DiagnosticValue<T> value, IReadOnlyList<ProviderAttempt> attempts, bool disagreement,
        IReadOnlyList<(string Source, string Display)> alternates)
    {
        Value = value;
        Attempts = attempts;
        Disagreement = disagreement;
        Alternates = alternates;
    }

    public DiagnosticValue<T> Value { get; }
    public IReadOnlyList<ProviderAttempt> Attempts { get; }
    public bool Disagreement { get; }
    public IReadOnlyList<(string Source, string Display)> Alternates { get; }

    public string AttemptSummary => string.Join("; ", Attempts.Select(a => a.Describe()));
}

/// <summary>
/// The multi-method diagnostic fallback engine. For a capability it walks an ordered
/// provider chain, validates each answer, keeps every attempt for the report, and
/// never silently replaces a missing value.
/// </summary>
public sealed class FallbackEngine
{
    private readonly IAppLogger _log;

    public FallbackEngine(IAppLogger? log = null) => _log = log ?? NullLogger.Instance;

    public async Task<ResolveResult<T>> ResolveAsync<T>(
        string metricName,
        string unit,
        IReadOnlyList<IDiagnosticProvider<T>> chain,
        TimeSpan timeout,
        Func<T, bool>? validate = null,
        Func<T, string>? format = null,
        CancellationToken cancellationToken = default)
    {
        var attempts = new List<ProviderAttempt>();
        var accepted = new List<(IDiagnosticProvider<T> Provider, T Value)>();
        var denial = false;

        foreach (var provider in chain.OrderBy(p => p.Priority))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProviderAvailability availability;
            try
            {
                availability = await provider.ProbeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Failed, "probe threw: " + ex.GetType().Name, false));
                _log.Warn("fallback", $"probe failed for {provider.Id}", ex);
                continue;
            }

            if (!availability.IsReady)
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Unavailable, availability.Reason, false));
                continue;
            }

            ProviderOutcome<T> outcome;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                outcome = await provider.CollectAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Timeout, $"exceeded {(int)timeout.TotalMilliseconds} ms", false));
                continue;
            }
            catch (Exception ex)
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Failed, ex.GetType().Name + ": " + ex.Message, false));
                _log.Warn("fallback", $"collect failed for {provider.Id}", ex);
                continue;
            }

            if (!outcome.IsSuccess)
            {
                if (outcome.Kind is OutcomeKind.Denied) denial = true;
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, outcome.Kind, outcome.Message, false));
                continue;
            }

            var value = outcome.Value!;
            if (validate is not null && !validate(value))
            {
                attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Failed, "value rejected by validation", false));
                continue;
            }

            accepted.Add((provider, value));
            attempts.Add(new ProviderAttempt(provider.Id, provider.Source, OutcomeKind.Success, outcome.Message, true));

            // An authoritative (priority 0) provider that answered cleanly ends the walk.
            if (provider.Priority == 0) break;
        }

        if (accepted.Count == 0)
        {
            var state = denial ? AvailabilityState.AccessDenied : DecideEmptyState(attempts);
            var reason = attempts.Count == 0
                ? "No provider is registered for this capability."
                : string.Join("; ", attempts.Select(a => $"{a.ProviderId}: {a.Message ?? a.Kind.ToString()}"));
            return new ResolveResult<T>(
                DiagnosticValue<T>.FromUnavailable(metricName, state, reason, unit: unit),
                attempts, false, Array.Empty<(string, string)>());
        }

        var chosen = accepted[0];
        var alternates = accepted
            .Select(a => (a.Provider.Source, format is null ? a.Value?.ToString() ?? string.Empty : format(a.Value)))
            .ToList();

        var disagreement = alternates
            .Select(a => a.Item2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

        var notes = new List<string>();
        if (accepted.Count > 1)
        {
            notes.Add($"Corroborated by {accepted.Count} providers.");
            foreach (var alt in alternates.Skip(1))
                notes.Add($"{alt.Item1} reported {alt.Item2}.");
        }

        var verification = disagreement
            ? VerificationState.Disputed
            : accepted.Count > 1 ? VerificationState.Corroborated : VerificationState.SingleSource;

        var confidence = disagreement ? ConfidenceLevel.Medium
            : accepted.Count > 1 ? ConfidenceLevel.High
            : chosen.Provider.Priority == 0 ? ConfidenceLevel.High : ConfidenceLevel.Medium;

        var result = DiagnosticValue<T>.FromAvailable(
            metricName, chosen.Value, chosen.Provider.Source, chosen.Provider.DetectionMethod,
            unit: unit, confidence: confidence, verification: verification, notes: notes);

        return new ResolveResult<T>(result, attempts, disagreement, alternates);
    }

    private static AvailabilityState DecideEmptyState(IReadOnlyList<ProviderAttempt> attempts)
    {
        if (attempts.Any(a => a.Kind == OutcomeKind.Denied)) return AvailabilityState.AccessDenied;
        if (attempts.All(a => a.Kind == OutcomeKind.Unsupported)) return AvailabilityState.Unsupported;
        if (attempts.Any(a => a.Kind == OutcomeKind.Timeout)) return AvailabilityState.Unavailable;
        return AvailabilityState.Unavailable;
    }
}
