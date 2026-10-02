using FluentAssertions;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Pure domain-logic unit tests (ADR-038 <c>tests/unit/**</c> KEEP category — no mocks, no DI, no I/O) for
/// <see cref="RiConfidenceScorer"/>, the FR-04 email-specific RI-confidence blend
/// (email-communication-intelligence-r1 task 024). Proves the urgency/deterministic-agreement blend, the
/// priority/urgency label mappings (D-08 — email-specific, not the Workspace/Portfolio scorer), and the
/// clamp/boundary behavior.
///
/// 🔴 <b>Updated 2026-09-29</b> (<c>spaarke-ontology-platform-r1</c>): the blend is a weighted SUM
/// (<c>0.7×urgency + 0.3×agreement</c>), not a product. The product form let a zero in either factor veto the
/// other, which took the RI notification path dark for every email whose association had not deterministically
/// resolved. See <c>Compute_WhenOneFactorIsZero_StillReturnsNonZero_SoNeitherFactorVetoesTheOther</c> — that is
/// the regression guard, not merely a boundary case.
/// </summary>
public class RiConfidenceScorerTests
{
    /// <summary>The shipped default gate threshold (<see cref="CommsPolicyOptions.DefaultConfidenceThreshold"/>)
    /// — used to prove the blend actually clears/misses the REAL threshold, not an arbitrary test constant.</summary>
    private static readonly double DefaultThreshold = new CommsPolicyOptions().DefaultConfidenceThreshold;

    // ── Compute: the core blend ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0, 0.95, 0.985)]   // Urgent + strong deterministic agreement (rung 0/1) → high score
    [InlineData(0.75, 0.9, 0.795)]   // High + strong agreement → clears the default gate
    [InlineData(0.5, 0.6, 0.53)]     // Medium + moderate agreement
    [InlineData(0.25, 0.3, 0.265)]   // Low + weak agreement (noise) → below the default gate
    public void Compute_GivenUrgencyAndAgreement_ReturnsExpectedWeightedSum(
        double urgencyWeight, double deterministicAgreement, double expected)
    {
        RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement)
            .Should().BeApproximately(expected, 1e-9);
    }

    // ── 🔴 REGRESSION GUARD: neither factor may veto the other ────────────────────
    // These four cases are the whole reason the formula changed from a product to a weighted sum on
    // 2026-09-29. Under the product form, EVERY one of them returned 0.0 — which meant a communication
    // whose association had not deterministically resolved could never be notified about, no matter how
    // urgent it was. Live evidence: an email asking approval of ~$140-145k of extra work scored exactly
    // 0.000 (High urgency x 0.0 agreement) and produced no task, no ping and no notification, even though
    // the RecordNameMatch rung had identified its matter at 0.97 confidence and written it.
    // If any of these ever returns 0.0 again, the product form has been reintroduced.

    [Theory]
    [InlineData(1.0, 0.0, 0.70)]    // Urgent, association unresolved  → MUST still surface
    [InlineData(0.75, 0.0, 0.525)]  // High, association unresolved    → MUST still surface
    [InlineData(0.5, 0.0, 0.35)]    // Medium, association unresolved  → non-zero but below the 0.45 gate
    [InlineData(0.0, 0.9, 0.27)]    // no urgency signal, strong association → non-zero, below the gate
    public void Compute_WhenOneFactorIsZero_StillReturnsNonZero_SoNeitherFactorVetoesTheOther(
        double urgencyWeight, double deterministicAgreement, double expected)
    {
        var score = RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement);

        score.Should().BeApproximately(expected, 1e-9);
        score.Should().BeGreaterThan(0.0,
            "a zero in one factor must not zero the score — that is the multiplicative veto this formula " +
            "was changed to remove (see RiConfidenceScorer type remarks, 2026-09-29)");
    }

    [Fact]
    public void Compute_HighUrgencyWithNoDeterministicAgreement_ClearsDefaultGateThreshold()
    {
        // The live failure this change fixes: High priority, association only "Suggested" (agreement 0).
        var score = RiConfidenceScorer.Compute(
            RiConfidenceScorer.UrgencyWeightFromPriority("High"),
            deterministicAgreement: 0.0);

        score.Should().BeGreaterThanOrEqualTo(DefaultThreshold,
            "a High-priority email must be surfaced even when the association engine could not deterministically " +
            "resolve its matter — those are precisely the ones a human needs to see");
    }

    [Fact]
    public void Compute_UrgentWithStrongDeterministicAgreement_ClearsDefaultGateThreshold()
    {
        // The acceptance-criterion scenario: a high-urgency, well-associated email must clear the shipped
        // default rule-gate threshold so CommunicationRuleGate authorizes it. Reads the threshold from
        // CommsPolicyOptions rather than hardcoding it, so lowering the default (0.8 → 0.45, 2026-09-29)
        // does not silently turn this assertion into a tautology or a false failure.
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
    [InlineData(-1.0, 0.9, 0.27)]   // negative urgency weight clamps to 0 → only the agreement term remains
    [InlineData(1.0, -0.5, 0.70)]   // negative agreement clamps to 0 → only the urgency term remains
    [InlineData(-1.0, -1.0, 0.0)]   // BOTH clamp to 0 → the only way the blend legitimately reaches 0.0
    public void Compute_WithOutOfRangeInput_ClampsEachFactorToZeroFloorIndependently(
        double urgencyWeight, double deterministicAgreement, double expected)
    {
        RiConfidenceScorer.Compute(urgencyWeight, deterministicAgreement)
            .Should().BeApproximately(expected, 1e-9);
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
