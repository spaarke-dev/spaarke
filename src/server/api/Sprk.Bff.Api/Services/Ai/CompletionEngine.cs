using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// <b>Completion Engine</b> (spaarke-ai-architecture-redesign-r2 task 035, spec FR-A1-06,
/// design D-F2) — the single composer that yields an <see cref="OutcomeCard"/> (the task-011
/// v1 contract) on EVERY side-effect path: gated (confirm-resume), auto-executed
/// (<see cref="OutputRouter"/>), and event-path (<c>EventRulesService</c>). It generalizes the
/// two shipped R1 primitives — the audience-split summary (<c>ToolResult</c>
/// <see cref="ToolResultMetadataKeys.UserSummary"/>, ToolResult.cs:326) and the SERVER-composed
/// record link (<c>HandoffUrlBuilder</c> / the entityrecord deep-link composer) — from the gated
/// path ONLY into one disposition-level contract across all paths. It adds next-step chips (from
/// the Binding's declared <see cref="Binding.ChipTransitions"/>) and a trace reference to each card.
/// </summary>
/// <remarks>
/// <para>
/// <b>Component Justification (CLAUDE.md §11)</b>:
/// (1) <i>Existing</i> — overlaps nothing that already yields a per-outcome disposition card;
/// R1 emitted the primitives ONLY inline on the gated resume path
/// (<c>GateOutcomeProducer.BuildGateOutcomeMessage</c> markdown) and never on the auto-executed or event paths.
/// (2) <i>Extension</i> — it does NOT fork <see cref="OutcomeCard"/>: it is a thin composer OVER
/// the task-011 guard factory (<see cref="OutcomeCard.ForStoredOutcome"/>) and the task-036
/// <see cref="JobAwareOutcomeProjection"/>; it invents no card shape and changes nothing about how
/// <c>UserSummary</c>/<c>HandoffUrlBuilder</c> behave.
/// (3) <i>Cost-of-doing-nothing</i> — without one composer, the auto-executed + event paths
/// complete card-less (NFR-09 breach) and each emit site re-derives an ad-hoc outcome shape.
/// </para>
/// <para>
/// <b>Store-before-render (ADR-040)</b>: every entry point builds the card FROM a STORED
/// <see cref="SessionOutput"/> — the card can only be produced after the ledger write (the
/// factory requires a non-empty <see cref="SessionOutput.Key"/>). This type performs NO storage,
/// opens no cache, and holds no state — it is a pure projection over already-stored ledger data,
/// so it can never render ahead of the store.
/// </para>
/// <para>
/// <b>ADR-039 single-rendering-contract</b>: the card RIDES the existing disposition surface. It
/// introduces no second dispatch or rendering mechanism — it is a disposition-level shape derived
/// from the <see cref="SessionOutput"/> the disposition surface already stored.
/// </para>
/// <para>
/// <b>NFR-07</b>: the card carries only identifiers + user-facing summary text + declared chip
/// labels + a trace/ledger ref — never verbatim tool arguments or raw document content.
/// </para>
/// </remarks>
public static class CompletionEngine
{
    /// <summary>
    /// Composes an <see cref="OutcomeCard"/> for a single-shot side-effect that has been STORED
    /// as <paramref name="entry"/> (the auto-executed + event + refusal-capability paths, all of
    /// which route through <see cref="OutputRouter"/>). The user-facing sentence is derived from
    /// the STORED payload (audience split); next-step chips come from the Binding's declared
    /// transitions; the trace ref defaults to the ledger key.
    /// </summary>
    /// <param name="entry">The stored ledger output (store-before-render — supplies the key + payload).</param>
    /// <param name="binding">The Binding that produced the output (supplies declared chip transitions).</param>
    /// <param name="link">Optional server-composed record/deep link. MUST be server-composed (task-011 guard).</param>
    /// <param name="traceRef">Optional trace correlation id; defaults to the ledger key when null.</param>
    public static OutcomeCard ComposeForRoutedOutput(
        SessionOutput entry,
        Binding binding,
        OutcomeCardLink? link = null,
        string? traceRef = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(binding);

        var userFacing = DeriveUserFacingSummary(entry.Payload, entry.Disposition);

        return OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: entry.Key,
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary(userFacing),
            link: link,
            nextSteps: MapNextStepChips(binding),
            completion: OutcomeCompletion.SingleShot(),
            traceRef: traceRef ?? entry.Key);
    }

    /// <summary>
    /// Composes an <see cref="OutcomeCard"/> for the GATED confirm-resume path — the typed-handler
    /// resume (<c>TypedHandlerResumeExecutor</c>) which stores its own <c>loop@t{n}</c> ledger
    /// output directly (not via <see cref="OutputRouter"/>). Carries the audience-split summary
    /// (<paramref name="userFacing"/> from the handler's <see cref="ToolResultMetadataKeys.UserSummary"/>,
    /// <paramref name="internalDetail"/> from the model-facing summary) and the server-composed
    /// record link. No Binding rides this path (the resume stores under the reserved <c>loop</c>
    /// binding), so there are no declared chip transitions.
    /// </summary>
    /// <param name="ledgerOutputKey">The stored <c>loop@t{n}</c> key (store-before-render — non-empty).</param>
    /// <param name="userFacing">The user-facing outcome sentence (rendered verbatim).</param>
    /// <param name="internalDetail">Optional model/internal-facing detail (never shown to the user).</param>
    /// <param name="link">Optional server-composed record link (e.g. the MDA entityrecord deep link).</param>
    /// <param name="traceRef">Optional trace correlation id; defaults to the ledger key when null.</param>
    public static OutcomeCard ComposeForGateResume(
        string ledgerOutputKey,
        string userFacing,
        string? internalDetail = null,
        OutcomeCardLink? link = null,
        string? traceRef = null)
    {
        return OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: ledgerOutputKey,
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary(
                string.IsNullOrWhiteSpace(userFacing) ? "Completed." : userFacing,
                internalDetail),
            link: link,
            nextSteps: Array.Empty<NextStepChip>(),
            completion: OutcomeCompletion.SingleShot(),
            traceRef: traceRef ?? ledgerOutputKey);
    }

    /// <summary>
    /// Composes an <see cref="OutcomeCard"/> for the INLINE gate AUTO-EXECUTE path — a Tier 0/1/2a/2b
    /// side-effect the deterministic Confirmation Policy v2 engine resolved to
    /// <see cref="GateOutcome.Execute"/> / <see cref="GateOutcome.ExecuteWithUndo"/> and that
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SideEffectGateAIFunction"/> ran WITHOUT a confirmation
    /// dialog (spaarke-ai-architecture-redesign-r2 task 044, gate G-R2-A). Mirror of
    /// <see cref="ComposeForGateResume"/> (same store-before-render contract, same audience-split
    /// summary, same server-composed link) but carries the DECLARED affordance chips the auto-execute
    /// disposition offers — the Undo chip for a reversible Tier 2a/2b create, or the review/send chip
    /// for a Tier-1 email draft handed off to the human. No Binding rides this path (the gate stores
    /// under the reserved <c>loop</c> binding), so chips are passed explicitly rather than mapped from
    /// <see cref="Binding.ChipTransitions"/>.
    /// </summary>
    /// <param name="ledgerOutputKey">The stored <c>loop@t{n}</c> key (store-before-render — non-empty).</param>
    /// <param name="userFacing">The user-facing outcome sentence (rendered verbatim).</param>
    /// <param name="internalDetail">Optional model/internal-facing detail (never shown to the user).</param>
    /// <param name="link">Optional server-composed record link (Undo target / email review-and-send record).</param>
    /// <param name="nextSteps">Declared affordance chips (e.g. the Undo chip) the surface may render.</param>
    /// <param name="traceRef">Optional trace correlation id; defaults to the ledger key when null.</param>
    public static OutcomeCard ComposeForGateAutoExecute(
        string ledgerOutputKey,
        string userFacing,
        string? internalDetail = null,
        OutcomeCardLink? link = null,
        IReadOnlyList<NextStepChip>? nextSteps = null,
        string? traceRef = null)
    {
        return OutcomeCard.ForStoredOutcome(
            ledgerOutputKey: ledgerOutputKey,
            status: OutcomeStatus.Succeeded,
            summary: new OutcomeSummary(
                string.IsNullOrWhiteSpace(userFacing) ? "Completed." : userFacing,
                internalDetail),
            link: link,
            nextSteps: nextSteps ?? Array.Empty<NextStepChip>(),
            completion: OutcomeCompletion.SingleShot(),
            traceRef: traceRef ?? ledgerOutputKey);
    }

    /// <summary>
    /// Composes a JOB-AWARE <see cref="OutcomeCard"/> for a side-effect backed by an async job
    /// aggregate (document creation / analysis / indexing / Compose save-back). Delegates to the
    /// task-036 <see cref="JobAwareOutcomeProjection.ForJobAwareOutcome"/> so the operation-level
    /// <see cref="OutcomeStatus"/> is DERIVED from the job aggregate (NFR-12 ingestion parity: a
    /// record whose downstream steps are still pending is <see cref="OutcomeStatus.Partial"/>,
    /// never Succeeded). Next-step chips still come from the Binding's declared transitions.
    /// </summary>
    public static OutcomeCard ComposeJobAware(
        SessionOutput entry,
        Binding binding,
        JobAwareCompletionState state,
        OutcomeCardLink? link = null,
        IReadOnlyDictionary<string, string>? stepLabels = null,
        string? traceRef = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(state);

        var userFacing = DeriveUserFacingSummary(entry.Payload, entry.Disposition);

        return JobAwareOutcomeProjection.ForJobAwareOutcome(
            ledgerOutputKey: entry.Key,
            state: state,
            summary: new OutcomeSummary(userFacing),
            link: link,
            nextSteps: MapNextStepChips(binding),
            stepLabels: stepLabels,
            traceRef: traceRef ?? entry.Key);
    }

    /// <summary>
    /// Maps the Binding's DECLARED next-step transitions (<c>sprk_chiptransitions</c>, catalog
    /// data — never model invention, per FR-A1-06 constraint) onto the task-011
    /// <see cref="NextStepChip"/> vocabulary. Transitions with no label are dropped (a chip with
    /// no caption is not renderable). A transition that names a target Binding is an
    /// <c>invoke_capability</c> chip carrying that Binding's GUID as
    /// <see cref="NextStepChip.TargetBindingId"/> — the resolution datum the Click path's
    /// <c>dispatchConsumer(bindingId, args)</c> helper needs (task 062 gap close: the OutcomeCard
    /// chip previously carried no dispatchable target, unlike the parallel
    /// <c>AnalysisChunkChip</c>/<c>EventChip</c> next-step wire shapes). A bare label with no
    /// declared target Binding is a <c>navigate</c> placeholder with a null
    /// <see cref="NextStepChip.TargetBindingId"/> (NFR-07: no target is invented at completion
    /// time — the ONLY source is <see cref="Binding.ChipTransitions"/>, so a chip can never carry
    /// a target the Binding did not declare).
    /// </summary>
    public static IReadOnlyList<NextStepChip> MapNextStepChips(Binding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.ChipTransitions is not { Count: > 0 })
        {
            return Array.Empty<NextStepChip>();
        }

        var chips = new List<NextStepChip>(binding.ChipTransitions.Count);
        foreach (var transition in binding.ChipTransitions)
        {
            var label = transition.ChipLabel;
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            var targetBindingId = string.IsNullOrWhiteSpace(transition.TargetBindingId)
                ? null
                : transition.TargetBindingId!.Trim();

            var actionKind = targetBindingId is null ? "navigate" : "invoke_capability";

            chips.Add(new NextStepChip(label!.Trim(), actionKind, TargetBindingId: targetBindingId));
        }

        return chips.Count > 0 ? chips : Array.Empty<NextStepChip>();
    }

    /// <summary>
    /// Derives the user-facing outcome sentence (the audience split) from a STORED ledger payload.
    /// Prefers an explicit <c>userSummary</c>, then a <c>summary</c>, then a <c>refusal</c> string
    /// field (the shapes the platform's capability/refusal outputs already carry); falls back to a
    /// neutral disposition-derived sentence. Never throws on shape drift — a completion sentence is
    /// always available (NFR-09: no path completes card-less for want of a summary).
    /// </summary>
    internal static string DeriveUserFacingSummary(JsonElement payload, string? disposition)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in new[] { "userSummary", "summary", "refusal" })
            {
                if (payload.TryGetProperty(field, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                {
                    return text;
                }
            }
        }

        return disposition switch
        {
            "email" => "The message was prepared.",
            "work_product" => "The work product was saved.",
            "notification" => "A notification was created.",
            "record" => "The record was updated.",
            _ => "Done.",
        };
    }
}
