using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ladder tests for the confidence→status mapper (R4 FR-11) — the highest-consequence decision in the
/// Association Engine. Each test names a concrete ladder band, reinforcement rule, AI-never-auto-file
/// guarantee, conflict outcome, or provenance-shape contract that would break in production if deleted.
/// </summary>
public class AssociationStatusMapperTests
{
    private const string MatterField = "sprk_regardingmatter";
    private const string PersonField = "sprk_regardingperson";

    private static RungMatch Match(
        string field, Guid targetId, double confidence, RungKind rung,
        string entity = "sprk_matter", string provenance = "test")
        => new()
        {
            RegardingFieldName = field,
            Target = new EntityReference(entity, targetId),
            Confidence = confidence,
            Provenance = provenance,
            Rung = rung,
        };

    // ── Ladder bands ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_DeterministicAtOrAboveThreshold_KillSwitchOn_ResolvesAndAutoFiles()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: true, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites[MatterField].Id.Should().Be(matterId);
    }

    [Fact]
    public void Decide_SameDeterministicMatch_KillSwitchOff_DowngradesToSuggested()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(enabled: false, threshold: 0.85);

        var decision = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        // The proposed target is still written so the review UI can surface + one-click confirm it.
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.Provenance.Decision.Reason.Should().Contain("kill-switch");
    }

    [Fact]
    public void Decide_MidConfidence_LandsSuggested()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(PersonField, Guid.NewGuid(), 0.70, RungKind.ParticipantCorrelation, entity: "contact") },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().ContainKey(PersonField);
    }

    [Fact]
    public void Decide_BelowSuggestFloor_LandsPendingReviewWithNoWrites()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(MatterField, Guid.NewGuid(), 0.40, RungKind.ParticipantCorrelation) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.PendingReview);
        decision.RegardingWrites.Should().BeEmpty();
    }

    [Fact]
    public void Decide_NoMatches_LandsPendingReview()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            Array.Empty<RungMatch>(), CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.PendingReview);
        decision.RegardingWrites.Should().BeEmpty();
    }

    // ── AI never auto-files ──────────────────────────────────────────────────────

    [Fact]
    public void Decide_AiRungHighConfidenceAlone_NeverAutoFiles_LandsSuggested()
    {
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[] { Match(MatterField, Guid.NewGuid(), 0.95, RungKind.AiClassification) },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.Provenance.Decision.AiInvolved.Should().BeTrue();
    }

    [Fact]
    public void Decide_DeterministicPlusAiOnSameTarget_DeterministicBelowThreshold_DoesNotAutoFile()
    {
        // det 0.70 + AI 0.80 on the SAME matter → full reinforced ~0.94 (≥ threshold) but deterministic
        // alone is 0.70 (< threshold). AI must NOT be able to push the target to auto-file.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation),
                Match(MatterField, matterId, 0.80, RungKind.SemanticMatch),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.AutoFiled.Should().BeFalse();
        decision.Provenance.Decision.TopConfidence.Should().BeGreaterThanOrEqualTo(0.85);
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeLessThan(0.85);
    }

    [Fact]
    public void Decide_DeterministicAboveThreshold_WithAiAgreeing_StillAutoFiles()
    {
        // det 0.90 (≥ threshold) with an AI rung also agreeing must still auto-file — AI agreement never
        // BLOCKS a deterministic target that already clears the bar.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                Match(MatterField, matterId, 0.80, RungKind.SemanticMatch),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
    }

    // ── Signal reinforcement (owner priority (b)) ────────────────────────────────

    [Fact]
    public void Decide_TwoIndependentDeterministicRungsAgree_ReinforceOverThreshold_AutoFiles()
    {
        // Neither 0.70 nor 0.65 alone reaches 0.85; noisy-OR = 1-(0.30)(0.35) = 0.895 ⇒ auto-file.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation),
                Match(MatterField, matterId, 0.65, RungKind.ExplicitReference),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.AutoFiled.Should().BeTrue();
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeApproximately(0.895, 0.01);
    }

    [Fact]
    public void Decide_SameRungKindTwice_DoesNotReinforce_TakesMaxOnly()
    {
        // Two matches from the SAME rung kind on the same target must NOT noisy-OR (that would let a rung
        // inflate its own confidence). Max = 0.70 (< threshold) ⇒ Suggested, not Resolved.
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation),
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Suggested);
        decision.Provenance.Decision.TopDeterministicConfidence.Should().BeApproximately(0.70, 0.001);
    }

    // ── Conflict → Ambiguous (owner priority (c)) ────────────────────────────────

    [Fact]
    public void Decide_TwoHighConfidenceTargetsOnSameField_LandsAmbiguousWithNoWrites()
    {
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, Guid.NewGuid(), 0.90, RungKind.ThreadContinuity),
                Match(MatterField, Guid.NewGuid(), 0.90, RungKind.ExplicitReference),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Ambiguous);
        decision.AutoFiled.Should().BeFalse();
        decision.RegardingWrites.Should().BeEmpty();
    }

    [Fact]
    public void Decide_TwoTargetsSameField_OnlyOneHighConfidence_ClearWinnerNoConflict()
    {
        // Winner 0.90, runner-up 0.60 (< threshold) ⇒ not a conflict; the clear winner auto-files.
        var winnerId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, winnerId, 0.90, RungKind.ThreadContinuity),
                Match(MatterField, Guid.NewGuid(), 0.60, RungKind.ParticipantCorrelation),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.RegardingWrites[MatterField].Id.Should().Be(winnerId);
    }

    // ── Complementary fields ─────────────────────────────────────────────────────

    [Fact]
    public void Decide_ComplementaryFields_WritesBothWhenResolved()
    {
        var matterId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                Match(PersonField, personId, 0.70, RungKind.ParticipantCorrelation, entity: "contact"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.RegardingWrites.Should().ContainKey(MatterField);
        decision.RegardingWrites.Should().ContainKey(PersonField);
    }

    // ── Per-tenant kill-switch ───────────────────────────────────────────────────

    [Fact]
    public void Decide_PerTenantKillSwitchOff_DowngradesToSuggested()
    {
        var matterId = Guid.NewGuid();
        var mapper = new AssociationStatusMapper(
            AssociationTestSupport.Gate(
                enabled: true, threshold: 0.85,
                tenants: new Dictionary<string, Configuration.AutoFileTenantOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["tenant-b"] = new Configuration.AutoFileTenantOverride { Enabled = false },
                }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AssociationStatusMapper>.Instance);

        var global = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: null);
        var tenantB = mapper.Decide(
            new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) },
            CommunicationDirection.Incoming, tenantKey: "tenant-b");

        global.Status.Should().Be(AssociationStatusCodes.Resolved);
        tenantB.Status.Should().Be(AssociationStatusCodes.Suggested);
    }

    // ── Direction symmetry ───────────────────────────────────────────────────────

    [Fact]
    public void Decide_InboundAndOutbound_ProduceIdenticalStatusAndWrites()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper();
        var matches = new[] { Match(MatterField, matterId, 0.90, RungKind.ExplicitReference) };

        var inbound = mapper.Decide(matches, CommunicationDirection.Incoming, tenantKey: null);
        var outbound = mapper.Decide(matches, CommunicationDirection.Outgoing, tenantKey: null);

        inbound.Status.Should().Be(outbound.Status);
        inbound.AutoFiled.Should().Be(outbound.AutoFiled);
        inbound.RegardingWrites.Keys.Should().BeEquivalentTo(outbound.RegardingWrites.Keys);
        inbound.Provenance.Direction.Should().Be("Incoming");
        outbound.Provenance.Direction.Should().Be("Outgoing");
    }

    // ── Provenance shape ─────────────────────────────────────────────────────────

    [Fact]
    public void Decide_WritesProvenance_WithRungsFiredContributorsAndDecision()
    {
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper(threshold: 0.85);

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.70, RungKind.ParticipantCorrelation, provenance: "sender:membership"),
                Match(MatterField, matterId, 0.65, RungKind.ExplicitReference, provenance: "subject:token"),
            },
            CommunicationDirection.Incoming, tenantKey: null);

        var json = JsonSerializer.Serialize(decision.Provenance,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        json.Should().Contain("\"rungsFired\"");
        json.Should().Contain("\"candidates\"");
        json.Should().Contain("\"contributors\"");
        json.Should().Contain("\"autoFiled\"");

        decision.Provenance.RungsFired.Should().Contain("ParticipantCorrelation");
        decision.Provenance.RungsFired.Should().Contain("ExplicitReference");
        decision.Provenance.Candidates.Should().ContainSingle();
        decision.Provenance.Candidates[0].Contributors.Should().HaveCount(2);
        decision.Provenance.Candidates[0].Written.Should().BeTrue();
    }

    [Fact]
    public void Decide_RecordsMetadataOnlySignals_RegardlessOfAssociationOutcome()
    {
        // A structural detector metadata-only match (no target) is recorded as a signal even when a
        // separate rung resolves the association (carry-forward #3: always-run detector pass).
        var matterId = Guid.NewGuid();
        var mapper = AssociationTestSupport.Mapper();

        var decision = mapper.Decide(
            new[]
            {
                Match(MatterField, matterId, 0.90, RungKind.ThreadContinuity),
                new RungMatch
                {
                    RegardingFieldName = null,
                    Target = null,
                    Confidence = 0.80,
                    Provenance = "structural:invoice-number",
                    Rung = RungKind.StructuralDetector,
                    Category = "invoice",
                    Obligations = new[] { "payment-review" },
                },
            },
            CommunicationDirection.Incoming, tenantKey: null);

        decision.Status.Should().Be(AssociationStatusCodes.Resolved);
        decision.Provenance.Signals.Should().ContainSingle(s => s.Category == "invoice");
        decision.Provenance.Signals[0].Obligations.Should().Contain("payment-review");
    }

    // ── NoisyOr unit ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(new[] { 0.70, 0.65 }, 0.895)]
    [InlineData(new[] { 1.0, 0.80 }, 1.0)]
    [InlineData(new[] { 0.50 }, 0.50)]
    [InlineData(new double[] { }, 0.0)]
    public void NoisyOr_CombinesIndependentConfidences_Bounded(double[] inputs, double expected)
        => AssociationStatusMapper.NoisyOr(inputs).Should().BeApproximately(expected, 0.001);
}
