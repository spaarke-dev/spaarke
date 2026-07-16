using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Per-rung telemetry tests (DEC-8 / NFR-05): the engine emits one structured record per rung attempt
/// (fired/skipped/error, latency, match count, AI-rung flag) plus a single per-envelope "resolving rung"
/// summary (tier + which rung(s) produced the winning target). Asserted against a spy <see cref="ILogger{T}"/>
/// capturing the real logging-sink boundary — no new telemetry framework (§11).
/// </summary>
public class RungTelemetryTests
{
    private const int RungTelemetryEventId = 4501;
    private const int ResolvingRungEventId = 4502;

    private readonly Mock<IDataverseService> _dv = new();
    private static readonly Guid CommunicationId = Guid.NewGuid();

    private (IncomingAssociationResolver engine, CapturingLogger<IncomingAssociationResolver> logger) BuildEngine(
        params IAssociationRung[] rungs)
    {
        _dv.Setup(d => d.UpdateAsync("sprk_communication", It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new CapturingLogger<IncomingAssociationResolver>();
        var engine = new IncomingAssociationResolver(
            rungs, _dv.Object, _dv.Object, AssociationTestSupport.Mapper(), logger);
        return (engine, logger);
    }

    private static RungMatch MatterMatch(RungKind rung, double confidence) => new()
    {
        RegardingFieldName = "sprk_regardingmatter",
        Target = new EntityReference("sprk_matter", Guid.NewGuid()) { Name = "Acme" },
        Confidence = confidence,
        Provenance = $"{rung}:match",
        Rung = rung,
    };

    private IEnumerable<LogEntry> RungRecords(CapturingLogger<IncomingAssociationResolver> logger) =>
        logger.Entries.Where(e => e.EventId.Id == RungTelemetryEventId);

    [Fact]
    public async Task Resolve_EmitsPerRungTelemetry_ForBothFiredAndSkippedRungs()
    {
        // One deterministic rung fires (returns a match), one skips (returns nothing). Both run in the
        // deterministic pass, so BOTH must produce a telemetry record.
        var fired = new FakeRung(RungKind.ExplicitReference, 0, MatterMatch(RungKind.ExplicitReference, 0.95));
        var skipped = new FakeRung(RungKind.ThreadContinuity, 1 /* no matches */);

        var (engine, logger) = BuildEngine(fired, skipped);
        await engine.ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var firedRecord = RungRecords(logger).Should()
            .ContainSingle(e => Equals(e.Field("Rung"), RungKind.ExplicitReference)).Subject;
        firedRecord.Field("Outcome").Should().Be("fired");
        firedRecord.Field("MatchCount").Should().Be(1);
        firedRecord.Field("IsAiRung").Should().Be(false);
        firedRecord.Field("ElapsedMs").Should().BeOfType<long>().Which.Should().BeGreaterThanOrEqualTo(0);
        firedRecord.Field("CommunicationId").Should().Be(CommunicationId);

        var skippedRecord = RungRecords(logger).Should()
            .ContainSingle(e => Equals(e.Field("Rung"), RungKind.ThreadContinuity)).Subject;
        skippedRecord.Field("Outcome").Should().Be("skipped");
        skippedRecord.Field("MatchCount").Should().Be(0);
    }

    [Fact]
    public async Task Resolve_EmitsPerEnvelopeResolvingRungSummary_WithTierAndResolvingRung()
    {
        var fired = new FakeRung(RungKind.ExplicitReference, 0, MatterMatch(RungKind.ExplicitReference, 0.95));

        var (engine, logger) = BuildEngine(fired);
        await engine.ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var summary = logger.Entries.Should()
            .ContainSingle(e => e.EventId.Id == ResolvingRungEventId).Subject;
        summary.Field("Tier").Should().Be("Resolved");
        summary.Field("AutoFiled").Should().Be(true);
        summary.Field("ResolvingRungs").Should().Be("ExplicitReference");    // the rung that won the tier
        summary.Field("RungsFired").Should().BeOfType<string>()
            .Which.Should().Contain("ExplicitReference");
    }

    [Fact]
    public async Task Resolve_EmitsResolvingRungSummary_WithNoneResolvingRung_WhenNothingResolves()
    {
        // Nothing matches ⇒ Pending Review; the summary is still emitted (dashboard-able) with "(none)".
        var (engine, logger) = BuildEngine(new FakeRung(RungKind.ExplicitReference, 0));
        await engine.ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var summary = logger.Entries.Should()
            .ContainSingle(e => e.EventId.Id == ResolvingRungEventId).Subject;
        summary.Field("Tier").Should().Be("Pending Review");
        summary.Field("AutoFiled").Should().Be(false);
        summary.Field("ResolvingRungs").Should().Be("(none)");
    }

    [Fact]
    public async Task Resolve_FlagsAiRungTelemetry_WhenAiRungEvaluatedOnDeterministicMiss()
    {
        // Deterministic miss ⇒ AI escalation. A rung partitioned as AI (SemanticMatch) must be telemetered
        // with IsAiRung=true, so a dashboard can separate deterministic vs AI cost.
        var detMiss = new FakeRung(RungKind.ExplicitReference, 0 /* no matches */);
        var aiRung = new FakeRung(RungKind.SemanticMatch, 4, MatterMatch(RungKind.SemanticMatch, 0.70));

        var (engine, logger) = BuildEngine(detMiss, aiRung);
        await engine.ResolveAsync(
            CommunicationId, RungTestSupport.Envelope(), new AssociationContext(), CancellationToken.None);

        var aiRecord = RungRecords(logger).Should()
            .ContainSingle(e => Equals(e.Field("Rung"), RungKind.SemanticMatch)).Subject;
        aiRecord.Field("IsAiRung").Should().Be(true);
        aiRecord.Field("Outcome").Should().Be("fired");
    }
}
