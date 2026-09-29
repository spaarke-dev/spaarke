using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

// ---------------------------------------------------------------------------
// Models for the ANCHOR-ONLY compose edit validation pass (ComposeEditAnchorPass).
//
// FR-C04 (spaarkeai-compose-r8 task 052) — the R2-era text-SEARCH validator these models were born
// with (`IComposeEditValidator` / `ComposeEditValidator` / `FindAll`, plus `ProposedEdit.TargetText`
// + `match_mode`) is DELETED. An edit names its target by anchor (`target_para_id` or `target_ref`)
// or it is refused; nothing here searches document prose for a placement (ADR-049 I-7).
//
// Task 064 (owner decision 2026-08-25) — the R2-era text-OFFSET apply half is DELETED too:
// `ComposeEditBatch`, `ComposeEditTransaction`, `POST /api/compose/edit-batch/validate`, and every
// model that served only them (`ResolvedMatch`, `MatchExample`, `AppliedEdit`, `SkippedEdit`,
// `ComposeEditBatchResult`, `ComposeEditTransactionResult`, `EditBatchValidateRequest`). Their sole
// span producer died with the validator in task 052, so nothing could ever populate them again, and
// the endpoint never had a client caller (a repo-wide grep for `edit-batch` returns zero .ts/.tsx
// hits — real placement happens client-side in `usePendingRedline`).
//
// The consequence worth stating: with the span vocabulary gone, ADR-049 I-7 is a property of the
// TYPE SYSTEM on this surface rather than of a comment. No type declared here can express a
// character offset into document prose, so no edit can be placed by one. The always-default
// remnants of that vocabulary went with it — `EditVerdict.Matches`, `EditValidationError.Examples`
// and `.MatchCount`, `EditErrorKind.Overlap`, `BatchValidationResult.BatchErrors` — because a field
// that can only ever hold its default value is a fossil, not a contract.
// Per-symbol consumer check + the member-level reasoning:
// projects/spaarkeai-compose-r8/notes/064-orphan-retirement-decisions.md.
//
// Wire-contract note: ProposedEdit / EditSource use the SAME snake_case keys as the
// compose-draft-alternative catalog action's structured output payload (HANDOFF §1 —
// notes/HANDOFF-core-r2-A0-contract-requirements.md; design.md §6.1), so the model's edit payload
// binds straight onto ProposedEdit with no translation layer — pinned by
// ComposeEditActionAnchorContractSeamTests. All other (BFF-authored, non-LLM-facing) shapes below
// use the BFF's normal camelCase convention.
// ---------------------------------------------------------------------------

/// <summary>
/// Serializes an enum to/from its lower-case wire form (e.g. <c>NoAnchor</c> &lt;-&gt;
/// <c>"noAnchor"</c>) via <see cref="JsonNamingPolicy.CamelCase"/>, matching the BFF's normal
/// camelCase JSON convention.
/// </summary>
/// <remarks>
/// Declared here for historical reasons, but NOT scoped to this file: it is also applied by
/// <c>IComposeService.cs</c>, <c>DocxAnnotationReader.cs</c> and <c>AnnotationReanchorService.cs</c>,
/// which is why it survives the task-064 retirement of the edit-batch surface.
/// </remarks>
internal sealed class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter() : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>
/// One provenance source cited by the LLM for a proposed edit (HANDOFF §1 <c>sources[]</c>) —
/// surfaced in the Context pane per design.md §2.0 provenance pattern. Not consumed by the anchor
/// pass itself; it is part of the catalog action's payload mirror, so it is carried through
/// unchanged rather than dropped at the boundary.
/// </summary>
public sealed record EditSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("snippet")] string? Snippet = null);

/// <summary>
/// One LLM-proposed replacement edit — mirrors the <c>compose-draft-alternative</c> catalog action's
/// structured output payload (HANDOFF §1 / design.md §6.1) verbatim, so the JSON the catalog action
/// emits binds directly onto this record with no translation layer.
/// </summary>
/// <remarks>
/// <b>FR-C01/C02/C03/C04 — the ANCHOR members are the ONLY targeting channel.</b>
/// <see cref="TargetParaId"/> and <see cref="TargetRef"/> name the target paragraph deterministically,
/// and <see cref="ComposeEditAnchorPass"/> resolves them through the request's reference map. There is no
/// second channel: task 052 (FR-C04) deleted <c>target_text</c> + <c>match_mode</c> along with the
/// whole-document search that consumed them, so an edit that supplies neither anchor is refused
/// (<see cref="EditErrorKind.NoAnchor"/>) rather than searched for (ADR-049 I-7). Supplying BOTH anchors is
/// allowed only while they agree; disagreement is <see cref="EditErrorKind.ConflictingAnchors"/>.
/// </remarks>
public sealed record ProposedEdit(
    [property: JsonPropertyName("new_text")] string NewText,
    [property: JsonPropertyName("rationale")] string? Rationale = null,
    [property: JsonPropertyName("sources")] IReadOnlyList<EditSource>? Sources = null,
    /// <summary>FR-C01/FR-C03 — the exact <c>w14:paraId</c> this edit targets: captured from the user's
    /// selection at dispatch time, or returned by the model from the enumerated closed set. Validated for
    /// membership in that set; an id outside it is rejected loudly, never searched for.</summary>
    [property: JsonPropertyName("target_para_id")] string? TargetParaId = null,
    /// <summary>FR-C02 — the target named as a legal citation ("clause 4.2", "4.2(b)(iii)"), resolved by
    /// <see cref="CitationResolver"/> through the numbering engine. Never a text search.</summary>
    [property: JsonPropertyName("target_ref")] string? TargetRef = null);

/// <summary>
/// The kind of structured refusal a validated edit can carry. Every member is a PER-EDIT anchor
/// rejection, and every one is a LOUD refusal: the edit named a target the document does not have,
/// named it two ways at once, or named none at all. None falls back to a search — there is no search
/// left to fall back to.
/// <para>
/// FR-C04 (task 052) removed <c>Ambiguous</c> / <c>NoMatch</c> / <c>EmptyTarget</c> with the text-search
/// validator that was their only producer — all three described an outcome of matching <c>target_text</c>
/// against document prose, which no longer happens. Task 064 removed the last non-anchor member,
/// <c>Overlap</c> (a batch-level SPAN collision detected on the apply side), with the apply side itself.
/// </para>
/// </summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum EditErrorKind
{
    /// <summary>
    /// FR-C04 — the edit supplied NO anchor: neither <c>target_para_id</c> nor <c>target_ref</c>. Before
    /// task 052 this fell through to a whole-document <c>target_text</c> search; it is now a deterministic
    /// refusal, because an edit that cannot say which paragraph it targets is not placeable (ADR-049 I-7).
    /// </summary>
    NoAnchor,

    /// <summary>FR-C03 — <c>target_para_id</c> is not a member of the document's paraId reference map.</summary>
    UnknownParaId,

    /// <summary>FR-C02 — <c>target_ref</c> parsed but names no paragraph in this document's numbering.</summary>
    UnresolvedReference,

    /// <summary>FR-C02 — <c>target_ref</c> names more than one paragraph (a range); one edit addresses one paragraph.</summary>
    AmbiguousReference,

    /// <summary>Both anchors were supplied and they resolve to different paragraphs; neither is preferred.</summary>
    ConflictingAnchors,

    /// <summary>An anchor was supplied but no reference map accompanied the request, so nothing could validate it.</summary>
    NoReferenceMap,
}

/// <summary>
/// Structured, actionable refusal returned instead of a silently-wrong placement: what went wrong
/// (<see cref="Kind"/>), which edit it was (<see cref="Message"/>), and a copy-pasteable
/// <see cref="ResolutionHint"/> the caller can act on without re-reasoning.
/// </summary>
/// <remarks>
/// Task 064 removed <c>MatchCount</c> + <c>Examples</c>. They reported what a <c>target_text</c>
/// search had found, and had been pinned at 0/empty since task 052 deleted that search; the hint now
/// names the anchor the edit should have supplied instead. Their absence IS the guarantee: an anchor
/// refusal cannot offer "nearest" occurrences, because there is no longer a type here in which to
/// express one, and inventing them would reintroduce exactly the guessing FR-C04 removed.
/// </remarks>
public sealed record EditValidationError(
    [property: JsonPropertyName("kind")] EditErrorKind Kind,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("resolutionHint")] string ResolutionHint);

/// <summary>
/// Per-edit verdict: either the paragraph the edit anchored to OR a structured error — never a silent
/// wrong match. <see cref="EditIndex"/> is the edit's position in the request batch (used to build
/// "Edit N: ..." messages so only the failing edit needs resubmission).
/// </summary>
/// <remarks>
/// Task 064 removed <c>Matches</c> (an <c>IReadOnlyList&lt;ResolvedMatch&gt;</c> of character spans).
/// It had been empty on every verdict since task 052, because the paraId IS the address; mapping it to
/// a live span is the editor's job, which it already does for imported comments and revisions.
/// </remarks>
public sealed record EditVerdict(
    [property: JsonPropertyName("editIndex")] int EditIndex,
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("error")] EditValidationError? Error,
    /// <summary>
    /// FR-C01/C02/C03 — the paragraph this edit ANCHORED to, from its <c>target_para_id</c> or
    /// <c>target_ref</c>. Non-null on EVERY valid verdict the anchor pass produces; null only on a refusal.
    /// </summary>
    [property: JsonPropertyName("resolvedParaId")] string? ResolvedParaId = null);

/// <summary>
/// Batch outcome: one <see cref="EditVerdict"/> per proposed edit, in request order.
/// </summary>
/// <remarks>
/// Task 064 removed <c>BatchErrors</c>. It carried cross-edit span collisions for the apply side, which
/// is gone; with <c>EditErrorKind.Overlap</c> removed there is no error kind left that describes
/// a batch-level condition at all, so the channel was not merely empty but untypeable. Every failure
/// this surface can report is a per-edit anchor refusal, reported on that edit's own verdict.
/// </remarks>
public sealed record BatchValidationResult(
    [property: JsonPropertyName("verdicts")] IReadOnlyList<EditVerdict> Verdicts)
{
    /// <summary><c>true</c> only when every edit resolved to a paragraph.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid => Verdicts.All(v => v.IsValid);
}
