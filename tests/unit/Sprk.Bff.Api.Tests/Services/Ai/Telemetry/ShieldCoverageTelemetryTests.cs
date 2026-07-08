using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Telemetry;

/// <summary>
/// AI-ARCHITECTURE assessment rec 2a (work/safety-perimeter-hygiene) — the shield-coverage
/// counter contract. The PromptShield perimeter fails OPEN on timeout/429/5xx, so the
/// fail-open rate computed by <c>scripts/kql/ai-metering/shield-coverage.kql</c> is the
/// operational safety-coverage signal. These tests anchor the instrument name + dimension
/// keys at the meter boundary (captured measurements — ADR-038: no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no mocked collaborators), following the
/// <see cref="AiMeteringTelemetryTests"/> pattern. NFR-07 is asserted structurally: the
/// dimension key set is closed and carries identifiers/counts only.
/// </summary>
public class ShieldCoverageTelemetryTests : IDisposable
{
    private readonly AiTelemetry _telemetry = new();
    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();

    public ShieldCoverageTelemetryTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Sprk.Bff.Api.Ai" &&
                instrument.Name == "ai.safety.shield_evaluations")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                tagDict[tag.Key] = tag.Value;
            }
            _measurements.Add((instrument.Name, value, tagDict));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _telemetry.Dispose();
    }

    /// <summary>Measurements scoped to THIS test's unique tenant (parallel-safe).</summary>
    private List<(string Instrument, long Value, Dictionary<string, object?> Tags)> For(string tenantId)
        => _measurements.Where(m => m.Tags.TryGetValue("tenant.id", out var t) &&
                                    (string?)t == tenantId).ToList();

    [Theory]
    [InlineData(AiTelemetry.ShieldOutcomeCompleted, "completed")]
    [InlineData(AiTelemetry.ShieldOutcomeBlocked, "blocked")]
    [InlineData(AiTelemetry.ShieldOutcomeFailedOpenTimeout, "failed_open_timeout")]
    [InlineData(AiTelemetry.ShieldOutcomeFailedOpenError, "failed_open_error")]
    public void RecordShieldEvaluation_EmitsOneIncrementWithTheOutcomeDimension(string outcome, string expectedWireValue)
    {
        var tenantId = Guid.NewGuid().ToString();

        _telemetry.RecordShieldEvaluation(outcome, tenantId);

        var emitted = For(tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Value.Should().Be(1,
            "every ScanAsync is exactly one evaluation so failed_open_* / total is the fail-open rate");
        emitted[0].Tags["outcome"].Should().Be(expectedWireValue,
            "the KQL pack (shield-coverage.kql) sumif()s on these exact wire values");
    }

    [Fact]
    public void RecordShieldEvaluation_WithNullTenant_ResolvesTenantFromTheAmbientMeteringScope()
    {
        // PromptShieldService has no tenant parameter — attribution MUST flow from the
        // AiMeteringContext scope set at the entry seams (same plumbing as token metering).
        var tenantId = Guid.NewGuid().ToString();

        using (AiMeteringContext.Begin(tenantId, userId: null, AiMeteringContext.EntryPathText))
        {
            _telemetry.RecordShieldEvaluation(AiTelemetry.ShieldOutcomeCompleted);
        }

        var emitted = For(tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Tags["outcome"].Should().Be("completed");
    }

    [Fact]
    public void RecordShieldEvaluation_WithNoTenantAndNoScope_OmitsTheTenantDimension()
    {
        var before = _measurements.Count(m => !m.Tags.ContainsKey("tenant.id"));

        _telemetry.RecordShieldEvaluation(AiTelemetry.ShieldOutcomeFailedOpenTimeout);

        var mine = _measurements.Where(m => !m.Tags.ContainsKey("tenant.id")).ToList();
        mine.Should().HaveCountGreaterThan(before,
            "omission (not a sentinel) is the null representation so KQL can filter empties explicitly");
    }

    [Fact]
    public void RecordShieldEvaluation_EmitsOnlyTheClosedIdentifierDimensionSet()
    {
        var tenantId = Guid.NewGuid().ToString();

        _telemetry.RecordShieldEvaluation(AiTelemetry.ShieldOutcomeBlocked, tenantId);
        _telemetry.RecordShieldEvaluation(AiTelemetry.ShieldOutcomeFailedOpenError, tenantId);

        var mine = For(tenantId);
        mine.Should().NotBeEmpty();
        mine.SelectMany(m => m.Tags.Keys).Distinct()
            .Should().BeSubsetOf(new[] { "outcome", "tenant.id" },
                "NFR-07/ADR-015: shield-coverage dimensions carry identifiers/outcomes ONLY — " +
                "never prompt or document content");
    }
}
