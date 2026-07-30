using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pure domain-logic unit tests (ADR-038 <c>tests/unit/**</c> KEEP category — no mocks, no DI, no I/O) for
/// <see cref="RiConfidenceScorer"/>, the FR-04 email-specific RI-confidence blend
/// (email-communication-intelligence-r1 task 024). Proves the urgency×deterministic-agreement combination,
/// the priority/urgency label mappings (D-08 — email-specific, not the Workspace/Portfolio scorer), and the
/// clamp/boundary behavior.
/// </summary>
public class RiConfidenceScorerTests
{
    /// <summary>The shipped default gate threshold (<see cref="CommsPolicyOptions.DefaultConfidenceThreshold"/>)
    /// — used to prove the blend actually clears/misses the REAL threshold, not an arbitrary test constant.</summary>
    private static readonly double DefaultThreshold = new CommsPolicyOptions().DefaultConfidenceThreshold;

    // ── Compute: the core blend ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, 0.95, 0.95)]   // Urgent + strong deterministic agreement (rung 0/1) → high score
    [InlineData(0.75, 0.9, 0.675)]  // High + strong agreement → moderate score
    [InlineData(0.5, 0.6, 0.3)]     // Medium + moderate agreement → low-moderate score
    [InlineData(0.25, 0.3, 0.075)]  // Low + weak agreement (noise) → very low score
    [InlineData(0.0, 0.9, 0.0)]     // zero urgency weight zeroes the product regardless of agreement
    [InlineData(1.0, 0.0, 0.0)]     // zero deterministic agreement zeroes the product regardless of urgency
    public void Compute_GivenUrgencyAndAgreement_ReturnsExpectedProduct(
        double urgencyWeight, double deterministicAgreement, double expected)
    {
        RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement)
            .Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void Compute_UrgentWithStrongDeterministicAgreement_ClearsDefaultGateThreshold()
    {
        // The acceptance-criterion scenario: a high-urgency, well-associated email must clear the shipped
        // default rule-gate threshold (0.8) so CommunicationRuleGate authorizes it.
        var score = RiConfidenceScorer.Compute(
            RiConfidenceScorer.UrgencyWeightFromPriority("Urgent"),
            deterministicAgreement: 0.95);

        score.Should().BeGreaterThanOrEqualTo(DefaultThreshold,
            "an Urgent, well-associated email must clear the shipped default confidence threshold");
    }

    [Fact]
    public void Compute_LowUrgencyWithWeakAgreement_MissesDefaultGateThreshold()
    {
        // The negative acceptance-criterion scenario: noise must not clear the threshold.
        var score = RiConfidenceScorer.Compute(
            RiConfidenceScorer.UrgencyWeightFromPriority("Low"),
            deterministicAgreement: 0.3);

        score.Should().BeLessThan(DefaultThreshold,
            "low urgency + weak association (noise) must NOT clear the shipped default confidence threshold");
    }

    // ── Clamp / boundary behavior ─────────────────────────────────────────────────

    [Theory]
    [InlineData(-1.0, 0.9)]  // negative urgency weight clamps to 0
    [InlineData(1.0, -0.5)]  // negative agreement clamps to 0
    public void Compute_WithOutOfRangeInput_ClampsToZeroFloor(double urgencyWeight, double deterministicAgreement)
    {
        RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement).Should().Be(0.0);
    }

    [Fact]
    public void Compute_WithBothInputsAboveOne_ClampsToOneCeiling()
    {
        RiConfidenceScorer.Compute(urgencyWeight: 5.0, deterministicAgreement: 5.0).Should().Be(1.0);
    }

    // ── Priority label mapping (CommunicationTriageResult.Priority closed set) ───

    [Theory]
    [InlineData("Urgent", 1.0)]
    [InlineData("High", 0.75)]
    [InlineData("Medium", 0.5)]
    [InlineData("Low", 0.25)]
    [InlineData("urgent", 1.0)]   // case-insensitive
    public void UrgencyWeightFromPriority_GivenClosedSetLabel_ReturnsExpectedWeight(string priority, double expected)
    {
        RiConfidenceScorer.UrgencyWeightFromPriority(priority).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Whenever")]
    public void UrgencyWeightFromPriority_GivenMissingOrUnrecognizedLabel_ReturnsNeutralDefault(string? priority)
    {
        RiConfidenceScorer.UrgencyWeightFromPriority(priority).Should().Be(0.5,
            "an unresolved priority must neither auto-suppress nor auto-boost the notification path");
    }

    // ── Urgency label mapping (CommunicationClassificationResult.Urgency open-text fallback) ─

    [Theory]
    [InlineData("urgent", 1.0)]
    [InlineData("elevated", 0.75)]
    [InlineData("routine", 0.25)]
    public void UrgencyWeightFromClassification_GivenKnownLabel_ReturnsExpectedWeight(string urgency, double expected)
    {
        RiConfidenceScorer.UrgencyWeightFromClassification(urgency).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unspecified")]
    public void UrgencyWeightFromClassification_GivenMissingOrUnspecifiedLabel_ReturnsNeutralDefault(string? urgency)
    {
        RiConfidenceScorer.UrgencyWeightFromClassification(urgency).Should().Be(0.5);
    }

    // ── D-08: email-specific — no cross-vocabulary bleed beyond the shared four-tier scale ─

    [Fact]
    public void UrgencyWeightFromPriority_AndFromClassification_ProduceSameScaleForEquivalentTiers()
    {
        // Both vocabularies (closed-set Priority vs open-text classification Urgency) must land on the SAME
        // four-tier scale so the blend is consistent regardless of which source supplied urgency.
        RiConfidenceScorer.UrgencyWeightFromPriority("Urgent")
            .Should().Be(RiConfidenceScorer.UrgencyWeightFromClassification("urgent"));
        RiConfidenceScorer.UrgencyWeightFromPriority("High")
            .Should().Be(RiConfidenceScorer.UrgencyWeightFromClassification("elevated"));
        RiConfidenceScorer.UrgencyWeightFromPriority("Low")
            .Should().Be(RiConfidenceScorer.UrgencyWeightFromClassification("routine"));
    }
}
