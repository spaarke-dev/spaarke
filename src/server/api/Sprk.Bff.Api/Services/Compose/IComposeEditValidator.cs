namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-19 deterministic edit-validation engine — the foundation of Phase 2's LLM Editing
/// Patterns (design.md §6.1). Resolves each LLM-proposed <see cref="ProposedEdit"/>'s
/// <c>target_text</c> against caller-supplied document plaintext according to its declared
/// <see cref="MatchMode"/>, and REFUSES ambiguity with a structured, actionable error instead
/// of silently applying the wrong match. This collapses the LLM's job to find-and-replace
/// (which LLMs do reliably) while the engine owns correctness — the "adeu <c>match_mode</c>
/// pattern" (design.md §6.1; validated in notes/spikes/spike-2-edit-validator.md).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-013 facade boundary</b>: this is pure, deterministic text logic. Implementations
/// MUST NOT inject <c>IOpenAiClient</c>, executor types, or <c>IConsumerRoutingService</c> —
/// the validator never reaches the model; it only resolves spans in a string the caller
/// already has. Tier-1 NetArchTest enforces this boundary (task 025).
/// </para>
/// <para>
/// <b>Document-projection contract</b>: <c>documentText</c> is a PLAINTEXT PROJECTION of the
/// editor/DOCX content — the same plaintext projection the <c>compose-selection</c> JPS scope
/// payload builds (design.md §6.1 "Patterns to Avoid": TipTap JSON stays canonical; plaintext
/// is projected only for this kind of read). Offsets this validator returns
/// (<see cref="ResolvedMatch"/>, <see cref="MatchExample"/>) are relative to THAT projection,
/// not to the DOCX byte stream or the TipTap JSON document model. Callers that need to apply
/// an edit back into TipTap JSON or the DOCX are responsible for mapping these offsets through
/// that same projection.
/// </para>
/// <para>
/// <b>Batch-apply ordering is NOT this validator's job.</b> Every edit in a single
/// <see cref="Validate"/> call resolves against the SAME original <c>documentText</c> —
/// offset drift across a batch of dependent edits, and the actual apply-time ordering, are
/// FR-20's concern (<c>ComposeEditBatch</c>, task 021: resolve → sort descending → skip
/// overlap → apply bottom-up). This validator's only batch-level duty is flagging cross-edit
/// span overlap (see <see cref="EditErrorKind.Overlap"/>) so FR-20/FR-21 can refuse the batch
/// atomically; it does not decide what happens next.
/// </para>
/// </remarks>
public interface IComposeEditValidator
{
    /// <summary>
    /// Validates a batch of proposed edits against <paramref name="documentText"/>.
    /// </summary>
    /// <param name="documentText">
    /// The plaintext projection of the current document — see the document-projection
    /// contract in the <see cref="IComposeEditValidator"/> remarks for which projection this
    /// is and why offsets are relative to it.
    /// </param>
    /// <param name="edits">The LLM-proposed edits to resolve, in caller order.</param>
    /// <returns>
    /// A <see cref="BatchValidationResult"/> carrying one <see cref="EditVerdict"/> per edit
    /// (resolved spans, or a structured <see cref="EditValidationError"/>) plus any batch-level
    /// overlap errors. <see cref="BatchValidationResult.IsValid"/> is <c>true</c> only when
    /// every edit resolved AND no cross-edit overlap was detected.
    /// </returns>
    BatchValidationResult Validate(string documentText, IReadOnlyList<ProposedEdit> edits);
}
