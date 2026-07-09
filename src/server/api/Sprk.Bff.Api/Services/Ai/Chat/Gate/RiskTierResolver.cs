using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat.Gate;

/// <summary>
/// Deterministic, fail-closed resolver from a catalog-declared <see cref="GateRiskProfile"/>
/// to the effective <see cref="GateRiskTier"/> (Policy v2 note §1). Pure DATA → DATA; there
/// is NO model judgment anywhere in this class (the ADR-039 one-intent boundary — a runtime
/// model-judged risk classification is the banned second intent mechanism, FR-A1-03).
///
/// <para>
/// <b>The rule</b>: the effective tier is the STRICTER (higher-severity) of the declared
/// tier and a <i>factor floor</i> computed from the five risk factors. The floor only ever
/// ESCALATES — it never down-ranks a declared tier. So:
/// </para>
/// <list type="bullet">
///   <item>a correctly-declared row resolves to exactly its declared tier;</item>
///   <item>an UNDER-declared row (e.g. an externally-visible action mistakenly marked
///   Tier 2a) is escalated to the floor the factors demand — a wrong write can never slip
///   through at a weaker tier because of an authoring mistake.</item>
/// </list>
///
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>: (1) <i>Existing</i> — nothing maps risk
/// factors to a tier; <c>GateDecisionProjector</c> consumes an already-resolved tier.
/// (2) <i>Extension</i> — the projector deliberately takes tier as an input (it is the
/// outcome mapper, not the risk classifier); folding factor-floor logic into it would blur
/// "classify risk" with "decide outcome". (3) <i>Cost of doing nothing</i> — without this,
/// the tier would have to be a single hand-authored value with no cross-check, so an
/// under-declared side effect (Policy v2's exact failure mode) executes at the weaker tier.
/// </para>
/// </summary>
public static class RiskTierResolver
{
    /// <summary>
    /// Resolves the effective risk tier for a catalog-declared profile: the stricter of the
    /// declared tier and the factor floor (fail-closed escalation, never a down-rank).
    /// </summary>
    public static GateRiskTier Resolve(GateRiskProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var floor = FactorFloor(profile);
        return Severity(floor) > Severity(profile.DeclaredTier) ? floor : profile.DeclaredTier;
    }

    /// <summary>
    /// The minimum tier the risk factors alone justify (Policy v2 note §1 columns). Returns
    /// <see cref="GateRiskTier.Tier0"/> when no escalating factor is present, so the declared
    /// tier stands for the low-risk rows (Tier 0/1/2a/2c) the factors do not force upward.
    /// </summary>
    internal static GateRiskTier FactorFloor(GateRiskProfile p)
    {
        // Tier 4 — external / irreversible destruction. Reversibility only escalates in
        // COMBINATION with a real side effect (record-of-truth destruction, external visibility);
        // an irreversibility flag on a non-mutating action — e.g. a read, where reversibility is
        // meaningless — is NOT a side effect and does not escalate on its own.
        if (p.ExternallyVisible || (!p.Reversible && p.RecordOfTruthImpact))
        {
            return GateRiskTier.Tier4;
        }

        // Tier 3 — legal-operational risk (deadline / obligation, confidentiality / privilege).
        if (p.DeadlineImpact || p.ConfidentialityImpact)
        {
            return GateRiskTier.Tier3;
        }

        // Tier 2b — reversible system-of-record create/update.
        if (p.RecordOfTruthImpact)
        {
            return GateRiskTier.Tier2b;
        }

        // No escalating factor — the declared tier (0 / 1 / 2a / 2c) is authoritative.
        return GateRiskTier.Tier0;
    }

    /// <summary>
    /// Total-order severity rank for a tier (2a &lt; 2b &lt; 2c &lt; 3 &lt; 4). Explicit so
    /// the "stricter wins" comparison never leans on the enum's underlying integer values.
    /// </summary>
    internal static int Severity(GateRiskTier tier) => tier switch
    {
        GateRiskTier.Tier0 => 0,
        GateRiskTier.Tier1 => 1,
        GateRiskTier.Tier2a => 2,
        GateRiskTier.Tier2b => 3,
        GateRiskTier.Tier2c => 4,
        GateRiskTier.Tier3 => 5,
        GateRiskTier.Tier4 => 6,
        _ => int.MaxValue, // unknown ⇒ maximally strict (fail-closed)
    };
}
