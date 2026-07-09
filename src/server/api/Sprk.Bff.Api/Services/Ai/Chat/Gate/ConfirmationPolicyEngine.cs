using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat.Gate;

/// <summary>
/// One evaluation of the Confirmation Policy v2 gate engine (Policy v2 note §1/§2/§3). Every
/// field is a structural DATA input — a catalog-declared risk profile, provenance-flagged
/// origin, argument completeness, deterministic safety-signal flags, and the ADR-040 Gate
/// ledger. There is no free-text or model-opinion field: the engine cannot make a runtime
/// risk judgment because none is expressible (ADR-039 one-intent boundary, FR-A1-03).
/// </summary>
public sealed record PolicyEvaluationContext
{
    /// <summary>The catalog-declared risk profile for the invoked capability (task-020 seed DATA).</summary>
    public required GateRiskProfile RiskProfile { get; init; }

    /// <summary>The deterministic origin-classifier input.</summary>
    public required GateOriginRequest Origin { get; init; }

    /// <summary>True when every declared required argument is present (overlay 3 fires when false).</summary>
    public bool ArgsComplete { get; init; } = true;

    /// <summary>
    /// Overlay 1: the dispatcher self-reported routing uncertainty (the ask-when-uncertain
    /// signal — E-6). Forces a dialog even on an otherwise-explicit, complete request.
    /// </summary>
    public bool DispatchUncertain { get; init; }

    /// <summary>Overlay 1: a content-safety / Prompt Shields flag fired for this turn (injection-suspect).</summary>
    public bool ContentSafetyFlagged { get; init; }

    /// <summary>
    /// Overlay 2: the safety perimeter degraded — Prompt Shields failed OPEN (timeout / 429 /
    /// 5xx) this turn. Gated WRITES degrade to confirm-required; READS stay fail-open (D-F0(b)).
    /// </summary>
    public bool SafetyPerimeterDegraded { get; init; }

    /// <summary>
    /// The session's ADR-040 Gate ledger (append-only). The engine reads the CURRENT status of
    /// <see cref="GateId"/> from here so confirmation state is a stored ledger property — a
    /// CONFIRMED gate is never re-asked (structural; kills the R3-1 loop). Null ⇒ no gate yet.
    /// </summary>
    public IReadOnlyList<SessionGate>? Ledger { get; init; }

    /// <summary>
    /// The gate id correlating this evaluation to its ledger entry, when a gate was raised.
    /// Null for the first evaluation of a low-risk (Tier 0/1) invocation.
    /// </summary>
    public string? GateId { get; init; }

    /// <summary>
    /// OPTIONAL parent-association picker to host inside the ONE confirmation dialog for Tier 2c
    /// document create/version (task 012 affordance). The container is resolved DETERMINISTICALLY
    /// by the caller — the picker only lets the user OPT to associate; it is never prompted for a
    /// value the engine must guess.
    /// </summary>
    public GateAssociationAffordance? Association { get; init; }
}

/// <summary>
/// The deterministic Confirmation Policy v2 gate ENGINE (FR-A1-03 / task 032). It is the
/// PRODUCER of <see cref="GateDecisionV2"/> from (risk tier × request origin × argument
/// completeness) with the precedence-ordered overlays — the full Policy v2 note §1/§2/§3
/// mechanized as ruled behavior. It builds ON the ONE existing Confirmation Gate
/// (<see cref="SideEffectGateAIFunction"/> + <see cref="PendingPlanManager"/>); it is NOT a
/// second gate.
///
/// <para>
/// <b>How the pieces compose (no logic is duplicated):</b>
/// </para>
/// <list type="number">
///   <item><see cref="RiskTierResolver"/> resolves the effective tier from catalog DATA
///   (fail-closed escalation).</item>
///   <item><see cref="RequestOriginClassifier"/> classifies origin deterministically
///   (fail-closed inferred).</item>
///   <item><see cref="PendingPlanManager.GetCurrentGateStatus"/> reads the CURRENT ADR-040
///   ledger status for the gate id — binding confirmation state to the ledger.</item>
///   <item><see cref="GateDecisionProjector.Project"/> (the task-012 reference producer)
///   maps tier × origin × completeness × overlays × ledger-status to the outcome — the SAME
///   precedence table the contract test locks, reused verbatim so there is one source of
///   truth for the outcome mapping.</item>
/// </list>
///
/// <para>
/// <b>Two invariants (Policy v2 note §0):</b> risk is catalog DATA (the engine has no model
/// input) and confirmation state is a ledger property (a confirmed gate renders no second
/// ask). Both are enforced structurally by the types above — not by convention.
/// </para>
/// </summary>
public static class ConfirmationPolicyEngine
{
    /// <summary>
    /// Produces the deterministic <see cref="GateDecisionV2"/> for one invocation. Pure given
    /// its context. The returned decision is already <c>Validate()</c>-checked by the projector
    /// (catalog-grounded provenance is structural — model-judged risk is not expressible).
    /// </summary>
    public static GateDecisionV2 Evaluate(PolicyEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var tier = RiskTierResolver.Resolve(context.RiskProfile);
        var origin = RequestOriginClassifier.Classify(context.Origin);
        var completeness = context.ArgsComplete ? GateArgCompleteness.Complete : GateArgCompleteness.Incomplete;

        // Overlay 1 (injection-suspect) fires on dispatch uncertainty (E-6) OR a content-safety
        // flag. Origin and injection stay LAYERED (E-3): an explicit origin can still be
        // overridden here — the projector applies overlay precedence before origin/tier.
        var injectionSuspect = context.DispatchUncertain || context.ContentSafetyFlagged;

        // Confirmation state is a stored ledger property (ADR-040): read the current status of
        // this gate id from the append-only ledger. A confirmed gate never re-prompts.
        var ledgerStatus = PendingPlanManager.GetCurrentGateStatus(context.Ledger, context.GateId);

        // Tier 2c hosts the OPTIONAL association picker inside the ONE dialog (no bespoke banner).
        var association = ResolveAssociation(tier, context.Association);

        return GateDecisionProjector.Project(
            tier: tier,
            origin: origin,
            completeness: completeness,
            injectionSuspect: injectionSuspect,
            safetyPerimeterDegraded: context.SafetyPerimeterDegraded,
            ledgerGateStatus: ledgerStatus,
            gateId: context.GateId,
            association: association);
    }

    /// <summary>
    /// The association affordance rides ONLY the Tier 2c document create/version dialog (task
    /// 012 acceptance criterion 7 / step 5b). For every other tier the picker is null — the
    /// engine never invents an associate-to affordance where the capability declares none.
    /// </summary>
    private static GateAssociationAffordance? ResolveAssociation(
        GateRiskTier tier, GateAssociationAffordance? declared)
    {
        if (declared is null || tier != GateRiskTier.Tier2c)
        {
            return tier == GateRiskTier.Tier2c ? declared : null;
        }

        return declared;
    }
}
