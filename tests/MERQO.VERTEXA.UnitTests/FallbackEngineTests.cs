using Merqo.Vertexa.Core.Contracts;
using Merqo.Vertexa.Shared;
using Xunit;

namespace Merqo.Vertexa.UnitTests;

public sealed class FallbackEngineTests
{
    private sealed class FakeProvider : IDiagnosticProvider<int>
    {
        private readonly ProviderOutcome<int> _outcome;
        private readonly int _delayMs;
        private readonly bool _ready;
        private readonly bool _throws;

        public FakeProvider(string id, int priority, ProviderOutcome<int> outcome, int delayMs = 0,
            bool ready = true, bool throws = false)
        {
            Id = id;
            Priority = priority;
            _outcome = outcome;
            _delayMs = delayMs;
            _ready = ready;
            _throws = throws;
        }

        public string Id { get; }
        public string Source => "fake:" + Id;
        public string DetectionMethod => "unit test double";
        public int Priority { get; }

        public ValueTask<ProviderAvailability> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(_ready ? ProviderAvailability.Ready() : ProviderAvailability.NotReady("probe says no"));

        public async ValueTask<ProviderOutcome<int>> CollectAsync(CancellationToken cancellationToken)
        {
            if (_delayMs > 0) await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
            if (_throws) throw new InvalidOperationException("boom");
            return _outcome;
        }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Highest_priority_provider_wins()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("secondary", 2, ProviderOutcome<int>.Success(4)),
            new FakeProvider("primary", 0, ProviderOutcome<int>.Success(8)),
        }, Timeout);

        Assert.True(result.Value.HasData);
        Assert.Equal(8, result.Value.Value);
        Assert.Equal("fake:primary", result.Value.Source);
    }

    [Fact]
    public async Task Failing_provider_falls_through_to_the_next()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("primary", 0, ProviderOutcome<int>.Failed("class missing")),
            new FakeProvider("secondary", 1, ProviderOutcome<int>.Success(6)),
        }, Timeout);

        Assert.True(result.Value.HasData);
        Assert.Equal(6, result.Value.Value);
        Assert.Contains(result.Attempts, a => !a.Accepted && a.ProviderId == "primary");
    }

    [Fact]
    public async Task Rejected_value_does_not_stop_the_chain()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("bogus", 0, ProviderOutcome<int>.Success(-1)),
            new FakeProvider("sane", 1, ProviderOutcome<int>.Success(4)),
        }, Timeout, validate: value => value > 0);

        Assert.Equal(4, result.Value.Value);
        Assert.Contains(result.Attempts, a => !a.Accepted && a.ProviderId == "bogus");
    }

    [Fact]
    public async Task Timeout_is_recorded_and_the_next_provider_is_tried()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("slow", 0, ProviderOutcome<int>.Success(2), delayMs: 600),
            new FakeProvider("fast", 1, ProviderOutcome<int>.Success(3)),
        }, TimeSpan.FromMilliseconds(120));

        Assert.Equal(3, result.Value.Value);
        Assert.Contains(result.Attempts, a => a.Kind == OutcomeKind.Timeout);
    }

    [Fact]
    public async Task Throwing_provider_is_contained()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("explodes", 0, ProviderOutcome<int>.Success(1), throws: true),
            new FakeProvider("works", 1, ProviderOutcome<int>.Success(5)),
        }, Timeout);

        Assert.Equal(5, result.Value.Value);
        Assert.Contains("boom", string.Join(" ", result.Attempts.Select(a => a.Message)));
    }

    [Fact]
    public async Task All_providers_failing_produces_unavailable_with_every_reason()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("first", 0, ProviderOutcome<int>.Unavailable("class absent")),
            new FakeProvider("second", 1, ProviderOutcome<int>.Unsupported("hardware lacks it")),
        }, Timeout);

        Assert.False(result.Value.HasData);
        Assert.Equal(AvailabilityState.Unavailable, result.Value.Availability);
        Assert.Contains("class absent", result.Value.UnavailableReason);
        Assert.Contains("hardware lacks it", result.Value.UnavailableReason);
    }

    [Fact]
    public async Task Denial_produces_access_denied_state()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("SMART", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("ata", 0, ProviderOutcome<int>.Denied("needs elevation")),
        }, Timeout);

        Assert.Equal(AvailabilityState.AccessDenied, result.Value.Availability);
    }

    [Fact]
    public async Task Disagreement_between_sources_is_preserved_not_resolved_silently()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("wmi", 1, ProviderOutcome<int>.Success(8)),
            new FakeProvider("registry", 2, ProviderOutcome<int>.Success(16)),
        }, Timeout);

        Assert.True(result.Disagreement);
        Assert.Equal(VerificationState.Disputed, result.Value.Verification);
        Assert.Equal(2, result.Alternates.Count);
    }

    [Fact]
    public async Task Not_ready_provider_is_skipped_without_collecting()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", new IDiagnosticProvider<int>[]
        {
            new FakeProvider("probe-fails", 0, ProviderOutcome<int>.Success(2), ready: false),
            new FakeProvider("ok", 1, ProviderOutcome<int>.Success(7)),
        }, Timeout);

        Assert.Equal(7, result.Value.Value);
        Assert.Contains("probe says no", string.Join(" ", result.Attempts.Select(a => a.Message)));
    }

    [Fact]
    public async Task Empty_chain_is_reported_honestly()
    {
        var engine = new FallbackEngine();
        var result = await engine.ResolveAsync("Cores", "", Array.Empty<IDiagnosticProvider<int>>(), Timeout);

        Assert.False(result.Value.HasData);
        Assert.Contains("No provider", result.Value.UnavailableReason);
    }
}
