using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// =============================================================================
// GateDecision v2 — the gate OUTCOME contract (spaarke-ai-architecture-redesign-r2
// task 012 · FR-A0-04 · design D-F1). Phase-A0 walking skeleton: the versioned,
// tolerant-reader shape the deterministic Policy v2 engine (FR-A1-03 / task 032)
// PRODUCES and clients + Compose r2 CONSUME (FR-05 / FR-28).
//
// It is the OUTCOME of the ONE existing Confirmation Gate
// (SideEffectGateAIFunction + PendingPlanManager), NOT a second gate. The engine
// projects a GateDecision from catalog-declared DATA (ADR-039) plus the stored
// Gate-ledger status (ADR-040) — this contract carries that projection.
//
// Two framing invariants (policy-v2-origin-classification-decision-tree.md §0):
//   1. Risk tier + origin factors are catalog-declared DATA — NEVER runtime LLM
//      judgment. Modelled here by GateRiskProvenance; a decision whose tier/origin
//      is RuntimeModelJudged is rejected (Validate throws — ADR-039).
//   2. Confirmation state is a Gate-LEDGER property (stored). A CONFIRMED decision
//      renders NO second ask — structurally, regardless of the carried Outcome
//      (GateDecisionConsumer.RequiresUserPrompt — ADR-040; kills the R3-1 loop).
//
// Tolerant-reader / versioning rules (additive-only):
//   - SchemaVersion defaults to CurrentSchemaVersion (2). Readers accept a payload
//     whose version is <= their own; unknown EXTRA fields are ignored (default
//     System.Text.Json behavior — asserted by the contract test).
//   - New capability arrives as NEW OPTIONAL fields (nullable / defaulted), never
//     as a renamed or removed field. Enum-value evolution rides a version bump.
// =============================================================================

/// <summary>
/// Catalog-declared risk tier (policy-v2 note §1). The sub-tier and its risk
/// factors (reversibility, external visibility, deadline impact, confidentiality/
/// privilege impact, record-of-truth impact) are DECLARED properties on the
/// catalog row (the ADR-039 <c>side_effect_class</c> pattern, extended) — never a
/// runtime model judgment.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateRiskTier
{
    /// <summary>Read / search / explain — always executes (D-F0(b)).</summary>
    Tier0,

    /// <summary>Draft-only, no system mutation — executes.</summary>
    Tier1,

    /// <summary>Private/internal reversible create — execute + Undo chip when explicit+complete, else confirm.</summary>
    Tier2a,

    /// <summary>Matter-scoped system-of-record create/update — execute (Undo) when explicit+complete, else ONE dialog.</summary>
    Tier2b,

    /// <summary>Document creation / versioning — preview/confirm in r2 (revisit post-G-R2-A).</summary>
    Tier2c,

    /// <summary>Legal-operational risk (deadline, obligation, cross-user assignment) — always dialog.</summary>
    Tier3,

    /// <summary>External / irreversible (email SEND, filing, delete/supersede) — always dialog.</summary>
    Tier4,
}

/// <summary>
/// Deterministic request origin (policy-v2 note §3). Fail-closed default is
/// <see cref="Inferred"/>; the model never decides its own request's origin.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateRequestOrigin
{
    /// <summary>User-explicit by construction (click path, or utterance naming the action verb + its invocation).</summary>
    Explicit,

    /// <summary>Inferred / non-utterance origin (document content, tool result, undecidable, model-initiated) — fail-closed.</summary>
    Inferred,
}

/// <summary>Whether the gated invocation's required args are complete (policy-v2 note overlay 3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateArgCompleteness
{
    /// <summary>All declared required args are present.</summary>
    Complete,

    /// <summary>At least one declared required arg is missing — triggers ONE elicitation turn, then re-evaluate.</summary>
    Incomplete,
}

/// <summary>
/// The precedence-ordered overlays (policy-v2 note §2). Evaluated strictly
/// top-to-bottom; the FIRST that fires decides, BEFORE origin/tier are consulted.
/// The numeric order encodes the precedence.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateOverlay
{
    /// <summary>Overlay 1 — injection-suspect (dispatchUncertain / content-safety flag / untrusted-doc origin). Always wins ⇒ dialog + suspicion surfaced.</summary>
    InjectionSuspect,

    /// <summary>Overlay 2 — safety-perimeter degradation (PromptShield fail-open). Gated writes degrade to confirm-required; reads stay fail-open.</summary>
    SafetyPerimeterDegraded,

    /// <summary>Overlay 3 — incomplete args ⇒ ONE elicitation turn, then re-evaluate from the top.</summary>
    IncompleteArgs,
}

/// <summary>
/// Confirmation state — a projection of the stored ADR-040 Gate-ledger status
/// (<see cref="SessionGate.Status"/> vocabulary). This is what makes a second ask
/// STRUCTURALLY impossible: the consumer reads stored state, it never re-derives it.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateConfirmationState
{
    /// <summary>No gate raised (yet) — e.g. Tier 0/1 auto-execute, or before suspension.</summary>
    None,

    /// <summary>A gate is pending the user's explicit decision.</summary>
    Pending,

    /// <summary>The user has confirmed. A confirmed gate is NEVER re-asked (structural).</summary>
    Confirmed,

    /// <summary>The user rejected the gate.</summary>
    Rejected,

    /// <summary>The resumable payload's TTL lapsed before resolution.</summary>
    Expired,

    /// <summary>The user broke out (hard slash / explicit restart / new work).</summary>
    Superseded,
}

/// <summary>
/// The resolved gate outcome the engine carries and the consumer renders/acts on.
/// This is the deterministic decision — the consumer does NOT re-classify.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateOutcome
{
    /// <summary>Run now, no gate (reads; explicit+complete low-risk).</summary>
    Execute,

    /// <summary>Run now with a ✅ card carrying an Undo chip (Tier 2a/2b, explicit+complete).</summary>
    ExecuteWithUndo,

    /// <summary>Suspend into the ONE ActionConfirmationDialog (never a chat-loop re-ask).</summary>
    ConfirmDialog,

    /// <summary>Run ONE elicitation turn to capture missing args, then re-evaluate.</summary>
    Elicit,

    /// <summary>Fail-closed honest block — cannot execute (gate unavailable / unexecutable target).</summary>
    HonestBlock,
}

/// <summary>
/// Provenance of the tier/origin values (ADR-039 enforcement hook). The contract
/// MUST carry catalog-declared data; a decision derived from runtime LLM risk
/// judgment is the second-intent mechanism ADR-039 bans.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateRiskProvenance
{
    /// <summary>Tier + origin derive from catalog-declared DATA (ADR-039 compliant).</summary>
    CatalogDeclared,

    /// <summary>FORBIDDEN — tier/origin derived from a runtime model risk judgment. Rejected by <see cref="GateDecisionV2.Validate"/>.</summary>
    RuntimeModelJudged,
}

/// <summary>Parent-association target types the one gate dialog can offer as an associate-to picker.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GateAssociationTargetType
{
    /// <summary>No association (explicit "none" choice — the default).</summary>
    None,

    /// <summary><c>sprk_matter</c>.</summary>
    Matter,

    /// <summary><c>sprk_project</c>.</summary>
    Project,

    /// <summary><c>sprk_invoice</c>.</summary>
    Invoice,

    /// <summary>Work assignment.</summary>
    WorkAssignment,
}

/// <summary>
/// The precedence-ordered overlay evaluation result. The three flags carry which
/// overlays are active; <see cref="Decisive"/> is the first one that fired
/// (null when none did), computed by <see cref="Resolve"/> so the precedence has
/// a single source of truth matching the Policy v2 note.
/// </summary>
public sealed record GateOverlayResult
{
    /// <summary>Overlay 1 active — injection-suspect.</summary>
    public bool InjectionSuspect { get; init; }

    /// <summary>Overlay 2 active — safety-perimeter degraded (PromptShield fail-open).</summary>
    public bool SafetyPerimeterDegraded { get; init; }

    /// <summary>Overlay 3 active — incomplete args.</summary>
    public bool IncompleteArgs { get; init; }

    /// <summary>The first overlay that fired in precedence order, or null when none fired.</summary>
    public GateOverlay? Decisive { get; init; }

    /// <summary>No overlay fired — origin/tier decide.</summary>
    public static readonly GateOverlayResult None = new();

    /// <summary>
    /// Computes the overlay result with its decisive overlay set by strict
    /// precedence: injection-suspect → safety-degradation → incomplete-args.
    /// </summary>
    public static GateOverlayResult Resolve(bool injectionSuspect, bool safetyPerimeterDegraded, bool incompleteArgs)
    {
        GateOverlay? decisive =
            injectionSuspect ? GateOverlay.InjectionSuspect
            : safetyPerimeterDegraded ? GateOverlay.SafetyPerimeterDegraded
            : incompleteArgs ? GateOverlay.IncompleteArgs
            : null;

        return new GateOverlayResult
        {
            InjectionSuspect = injectionSuspect,
            SafetyPerimeterDegraded = safetyPerimeterDegraded,
            IncompleteArgs = incompleteArgs,
            Decisive = decisive,
        };
    }
}

/// <summary>
/// OPTIONAL parent-association selection affordance (task 012 acceptance criterion 7).
/// Lets the ONE gate dialog host an associate-to picker (matter / project / invoice /
/// work-assignment / none) WITHOUT a bespoke confirmation banner — Compose r2 renders
/// it inside the existing ActionConfirmationDialog.
/// </summary>
public sealed record GateAssociationAffordance
{
    /// <summary>The target types the picker offers. Always includes <see cref="GateAssociationTargetType.None"/> as an escape.</summary>
    public IReadOnlyList<GateAssociationTargetType> AllowedTargets { get; init; } = new[] { GateAssociationTargetType.None };

    /// <summary>The currently selected target type. Defaults to <see cref="GateAssociationTargetType.None"/> (not associated).</summary>
    public GateAssociationTargetType Selected { get; init; } = GateAssociationTargetType.None;

    /// <summary>The selected record id when a concrete (non-None) target is chosen; null otherwise.</summary>
    public string? SelectedRecordId { get; init; }

    /// <summary>When true, the user MUST pick a non-None target before the gated action can proceed.</summary>
    public bool Required { get; init; }

    /// <summary>True when a concrete association target is selected with a record id.</summary>
    [JsonIgnore]
    public bool HasSelection => Selected != GateAssociationTargetType.None && !string.IsNullOrWhiteSpace(SelectedRecordId);
}

/// <summary>
/// GateDecision v2 — the versioned gate-outcome contract. See file header for the
/// full framing. Immutable record; construct via <see cref="GateDecisionProjector"/>
/// (the reference producer) or directly for tests, then read via
/// <see cref="GateDecisionConsumer"/> (the reference consumer).
/// </summary>
public sealed record GateDecisionV2
{
    /// <summary>Current schema version emitted by producers on this build.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Contract schema version. Tolerant readers accept any payload whose version is
    /// &lt;= their own and ignore unknown extra fields. Defaults to
    /// <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Catalog-declared risk tier.</summary>
    public required GateRiskTier Tier { get; init; }

    /// <summary>Deterministic request origin (fail-closed <see cref="GateRequestOrigin.Inferred"/>).</summary>
    public required GateRequestOrigin Origin { get; init; }

    /// <summary>Whether the gated invocation's required args are complete.</summary>
    public required GateArgCompleteness Completeness { get; init; }

    /// <summary>The precedence-ordered overlay evaluation result.</summary>
    public GateOverlayResult Overlays { get; init; } = GateOverlayResult.None;

    /// <summary>Confirmation state projected from the ADR-040 Gate ledger. Structural "no second ask" hinges on this.</summary>
    public GateConfirmationState ConfirmationState { get; init; } = GateConfirmationState.None;

    /// <summary>The resolved outcome the consumer renders/acts on.</summary>
    public required GateOutcome Outcome { get; init; }

    /// <summary>
    /// Provenance of <see cref="Tier"/> / <see cref="Origin"/>. MUST be
    /// <see cref="GateRiskProvenance.CatalogDeclared"/> (ADR-039). Defaults to it;
    /// a <see cref="GateRiskProvenance.RuntimeModelJudged"/> decision is rejected by
    /// <see cref="Validate"/>.
    /// </summary>
    public GateRiskProvenance TierProvenance { get; init; } = GateRiskProvenance.CatalogDeclared;

    /// <summary>
    /// The gate id correlating this decision to its stored ADR-040 ledger entry, when
    /// a gate was raised (null for Tier 0/1 auto-execute). Identifier only (NFR-07).
    /// </summary>
    public string? GateId { get; init; }

    /// <summary>
    /// OPTIONAL parent-association picker the one gate dialog can host. Null when the
    /// action offers no associate-to affordance.
    /// </summary>
    public GateAssociationAffordance? Association { get; init; }

    /// <summary>True when tier/origin are catalog-grounded (ADR-039). Tolerant readers use this to flag a non-compliant payload without throwing.</summary>
    [JsonIgnore]
    public bool IsCatalogGrounded => TierProvenance == GateRiskProvenance.CatalogDeclared;

    /// <summary>
    /// Fail-closed validation (ADR-039). Throws when tier/origin claim runtime
    /// model-judged provenance — risk is DATA, never a runtime LLM judgment.
    /// </summary>
    /// <exception cref="InvalidOperationException">Provenance is <see cref="GateRiskProvenance.RuntimeModelJudged"/>.</exception>
    public GateDecisionV2 Validate()
    {
        if (TierProvenance == GateRiskProvenance.RuntimeModelJudged)
        {
            throw new InvalidOperationException(
                "GateDecision v2 rejected (ADR-039): tier/origin are catalog-declared DATA and MUST NOT " +
                "be derived from a runtime LLM risk judgment. A model-judged risk classification is the " +
                "second intent mechanism ADR-039 bans.");
        }

        return this;
    }
}

/// <summary>
/// Reference PRODUCER — projects a <see cref="GateDecisionV2"/> from catalog-declared
/// DATA plus the stored Gate-ledger status. This is the thin producer for the walking
/// skeleton (task 012 step 2); the full Policy v2 engine (FR-A1-03 / task 032) replaces
/// the body but MUST emit this same shape.
///
/// It emits FROM the EXISTING gate: inputs are the tool row's catalog metadata and the
/// ADR-040 ledger status string (<c>PendingPlanManager.GateStatus*</c> vocabulary) — no
/// second gate is introduced, and no runtime model judgment is consulted (the method
/// signature only accepts enums, so model risk-judgment is not even expressible).
/// </summary>
public static class GateDecisionProjector
{
    /// <summary>
    /// Projects the outcome from tier × origin × completeness × overlays, following the
    /// Policy v2 precedence (overlays first, then origin+tier). Deterministic and pure.
    /// </summary>
    public static GateDecisionV2 Project(
        GateRiskTier tier,
        GateRequestOrigin origin,
        GateArgCompleteness completeness,
        bool injectionSuspect = false,
        bool safetyPerimeterDegraded = false,
        string? ledgerGateStatus = null,
        string? gateId = null,
        GateAssociationAffordance? association = null)
    {
        var overlays = GateOverlayResult.Resolve(
            injectionSuspect,
            safetyPerimeterDegraded,
            incompleteArgs: completeness == GateArgCompleteness.Incomplete);

        var confirmationState = MapLedgerStatus(ledgerGateStatus);
        var outcome = ResolveOutcome(tier, origin, overlays, confirmationState);

        return new GateDecisionV2
        {
            SchemaVersion = GateDecisionV2.CurrentSchemaVersion,
            Tier = tier,
            Origin = origin,
            Completeness = completeness,
            Overlays = overlays,
            ConfirmationState = confirmationState,
            Outcome = outcome,
            TierProvenance = GateRiskProvenance.CatalogDeclared, // structural: this producer only accepts catalog DATA
            GateId = gateId,
            Association = association,
        }.Validate();
    }

    /// <summary>
    /// Maps the ADR-040 ledger Gate-status vocabulary (the
    /// <c>PendingPlanManager.GateStatus*</c> constants) to
    /// <see cref="GateConfirmationState"/>. Confirmed-unexecutable and dispatch-failed
    /// both project to <see cref="GateConfirmationState.Confirmed"/> — the user's
    /// confirmation stands even when execution could not complete, so a second ask is
    /// still structurally impossible.
    /// </summary>
    public static GateConfirmationState MapLedgerStatus(string? ledgerGateStatus) => ledgerGateStatus switch
    {
        null or "" => GateConfirmationState.None,
        "pending" => GateConfirmationState.Pending,
        "confirmed" => GateConfirmationState.Confirmed,
        "confirmed-unexecutable" => GateConfirmationState.Confirmed,
        "dispatch-failed" => GateConfirmationState.Confirmed,
        "rejected" => GateConfirmationState.Rejected,
        "expired" => GateConfirmationState.Expired,
        "superseded" => GateConfirmationState.Superseded,
        _ => GateConfirmationState.Pending, // tolerant: an unknown terminal status is treated as still-gated, never auto-executed
    };

    /// <summary>
    /// The deterministic outcome mapping (Policy v2 note §1/§2). Overlays are evaluated
    /// FIRST (the decisive overlay wins), then origin+tier.
    /// </summary>
    private static GateOutcome ResolveOutcome(
        GateRiskTier tier,
        GateRequestOrigin origin,
        GateOverlayResult overlays,
        GateConfirmationState confirmationState)
    {
        // An already-confirmed gate never re-decides toward a prompt (ADR-040). Reads still execute.
        if (confirmationState == GateConfirmationState.Confirmed)
        {
            return tier == GateRiskTier.Tier0 ? GateOutcome.Execute : GateOutcome.ExecuteWithUndo;
        }

        // Overlay precedence — first that fires decides, before origin/tier.
        switch (overlays.Decisive)
        {
            case GateOverlay.InjectionSuspect:
                return GateOutcome.ConfirmDialog;      // + suspicion surfaced (dialog carries the flag)
            case GateOverlay.SafetyPerimeterDegraded:
                // Reads stay fail-open; gated writes (Tier >= 2) degrade to confirm-required.
                return tier <= GateRiskTier.Tier1 ? ExecuteFor(tier) : GateOutcome.ConfirmDialog;
            case GateOverlay.IncompleteArgs:
                return tier <= GateRiskTier.Tier1 ? ExecuteFor(tier) : GateOutcome.Elicit;
        }

        // Origin + tier (policy-v2 note §1 table).
        return tier switch
        {
            GateRiskTier.Tier0 or GateRiskTier.Tier1 => ExecuteFor(tier),

            // Tier 2a/2b: explicit ⇒ execute + Undo; inferred/model-initiated ⇒ ONE dialog.
            GateRiskTier.Tier2a or GateRiskTier.Tier2b =>
                origin == GateRequestOrigin.Explicit ? GateOutcome.ExecuteWithUndo : GateOutcome.ConfirmDialog,

            // Tier 2c: preview/confirm in r2.
            GateRiskTier.Tier2c => GateOutcome.ConfirmDialog,

            // Tier 3/4: always dialog, regardless of origin.
            GateRiskTier.Tier3 or GateRiskTier.Tier4 => GateOutcome.ConfirmDialog,

            _ => GateOutcome.ConfirmDialog, // fail-closed default
        };
    }

    private static GateOutcome ExecuteFor(GateRiskTier tier) =>
        tier is GateRiskTier.Tier2a or GateRiskTier.Tier2b ? GateOutcome.ExecuteWithUndo : GateOutcome.Execute;
}

/// <summary>
/// The rendering action a consumer takes for a <see cref="GateDecisionV2"/>.
/// </summary>
public enum GateRenderAction
{
    /// <summary>Run the action now (with or without an Undo card, per <see cref="GateDecisionV2.Outcome"/>).</summary>
    AutoExecute,

    /// <summary>Render the ONE ActionConfirmationDialog (optionally hosting the association picker).</summary>
    ShowConfirmDialog,

    /// <summary>Render a single elicitation turn to capture missing args.</summary>
    Elicit,

    /// <summary>Render an honest "cannot execute" block.</summary>
    HonestBlock,
}

/// <summary>
/// Reference CONSUMER — acts on a <see cref="GateDecisionV2"/> (task 012 step 3). It
/// does NOT re-classify; it renders the carried outcome and enforces the ADR-040
/// structural invariant: a CONFIRMED decision NEVER prompts a second time. Compose r2
/// consumes this shape via <c>Services/Ai/PublicContracts/</c> with zero local variant
/// (ADR-013).
/// </summary>
public static class GateDecisionConsumer
{
    /// <summary>
    /// Whether this decision requires prompting the user. Returns false for an
    /// already-CONFIRMED gate REGARDLESS of the carried <see cref="GateOutcome"/> — a
    /// second ask for an already-confirmed action is structurally impossible (ADR-040).
    /// Rejected/expired/superseded gates also do not prompt (they are terminal).
    /// </summary>
    public static bool RequiresUserPrompt(GateDecisionV2 decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        // ADR-039 fail-closed: a non-catalog-grounded decision is never rendered as a live action.
        if (!decision.IsCatalogGrounded)
        {
            return false;
        }

        // ADR-040 structural: stored terminal states never re-prompt.
        if (decision.ConfirmationState is
            GateConfirmationState.Confirmed or
            GateConfirmationState.Rejected or
            GateConfirmationState.Expired or
            GateConfirmationState.Superseded)
        {
            return false;
        }

        return decision.Outcome is GateOutcome.ConfirmDialog or GateOutcome.Elicit;
    }

    /// <summary>
    /// Resolves the concrete render action. A non-catalog-grounded decision (ADR-039) or
    /// an <see cref="GateOutcome.HonestBlock"/> renders an honest block; a confirmed/terminal
    /// gate auto-executes (or blocks) without a second prompt.
    /// </summary>
    public static GateRenderAction Resolve(GateDecisionV2 decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!decision.IsCatalogGrounded || decision.Outcome == GateOutcome.HonestBlock)
        {
            return GateRenderAction.HonestBlock;
        }

        if (decision.ConfirmationState == GateConfirmationState.Rejected ||
            decision.ConfirmationState == GateConfirmationState.Expired ||
            decision.ConfirmationState == GateConfirmationState.Superseded)
        {
            // Terminal non-confirmed — nothing runs, nothing re-asks.
            return GateRenderAction.HonestBlock;
        }

        if (decision.ConfirmationState == GateConfirmationState.Confirmed)
        {
            // The user already confirmed — execute, never ask again.
            return GateRenderAction.AutoExecute;
        }

        return decision.Outcome switch
        {
            GateOutcome.Execute or GateOutcome.ExecuteWithUndo => GateRenderAction.AutoExecute,
            GateOutcome.ConfirmDialog => GateRenderAction.ShowConfirmDialog,
            GateOutcome.Elicit => GateRenderAction.Elicit,
            _ => GateRenderAction.HonestBlock,
        };
    }
}
