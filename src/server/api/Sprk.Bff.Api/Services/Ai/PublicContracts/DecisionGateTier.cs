using Sprk.Bff.Api.Services.Ai.Chat.Gate;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// The declared risk of one decision action (ontology platform R1, task 043; D-52): the same five factors and declared tier an
/// AI tool's catalog row carries (<c>riskProfile</c>), declared here as code because the decision catalog is code (spec A-2).
/// </summary>
/// <param name="DeclaredTier">The tier the action author declares.</param>
/// <param name="Reversible">The effect can be undone.</param>
/// <param name="ExternallyVisible">The effect is seen outside the platform (an email send).</param>
/// <param name="DeadlineImpact">The effect sets or moves a date someone works to.</param>
/// <param name="ConfidentialityImpact">The effect touches confidential or privileged material.</param>
/// <param name="RecordOfTruthImpact">The effect creates or changes a system-of-record row.</param>
public sealed record DecisionGateFactors(
    GateRiskTier DeclaredTier,
    bool Reversible,
    bool ExternallyVisible = false,
    bool DeadlineImpact = false,
    bool ConfidentialityImpact = false,
    bool RecordOfTruthImpact = false);

/// <summary>
/// The gate tier of a human decision, as a PURE function (D-52): no chat session, no <c>ConfirmationPolicyEngine</c> state, no
/// I/O. The decision wizard's Confirm step IS the human confirmation, so the tier is recorded (<c>sprk_gatetier</c>), not used
/// to open a gate. The tier rule is the chat gate's own (<see cref="RiskTierResolver"/>: a declared tier that under-states its
/// factors is raised, never lowered), reached through this facade so no AI-internal type leaves the AI module (ADR-013).
/// </summary>
/// <remarks>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="RiskTierResolver"/> (pure, internal to the
/// chat gate) and <c>ConfirmationPolicyEngine</c> (session-bound). Extension: this is a thin facade over the resolver; nothing
/// in the tier rule is re-implemented. Cost of doing nothing: the commit route would have to reference a Chat.Gate type
/// (an ADR-013 breach) or invent its own tier rule, and sprk_gatetier could disagree with the chat gate for the same risk.</para>
/// </remarks>
public static class DecisionGateTier
{
    /// <summary>The tier one action's declared factors give. Pure; never throws for a non-null argument.</summary>
    public static GateRiskTier Resolve(DecisionGateFactors factors)
    {
        ArgumentNullException.ThrowIfNull(factors);
        return RiskTierResolver.Resolve(new GateRiskProfile
        {
            DeclaredTier = factors.DeclaredTier,
            Reversible = factors.Reversible,
            ExternallyVisible = factors.ExternallyVisible,
            DeadlineImpact = factors.DeadlineImpact,
            ConfidentialityImpact = factors.ConfidentialityImpact,
            RecordOfTruthImpact = factors.RecordOfTruthImpact,
        });
    }

    /// <summary>The strictest of <paramref name="tiers"/> by the chat gate's own severity order, or null when there are none.</summary>
    public static GateRiskTier? Strictest(IEnumerable<GateRiskTier> tiers)
    {
        ArgumentNullException.ThrowIfNull(tiers);
        GateRiskTier? strictest = null;
        foreach (var tier in tiers)
        {
            if (strictest is null || RiskTierResolver.Severity(tier) > RiskTierResolver.Severity(strictest.Value))
            {
                strictest = tier;
            }
        }

        return strictest;
    }

    /// <summary>The text written to <c>sprk_gatetier</c>: the tier's name (<c>Tier4</c>), the same token the chat gate serializes.</summary>
    public static string ToText(GateRiskTier tier) => tier.ToString();
}
