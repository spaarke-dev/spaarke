using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

// ---------------------------------------------------------------------------
// Models for the ANCHOR-FIRST compose edit pass (ComposeEditAnchorPass) and
// POST /api/compose/edit-batch/validate.
//
// FR-C04 (spaarkeai-compose-r8 task 052) — the R2-era text-search validator these models were
// born with (`IComposeEditValidator` / `ComposeEditValidator` / `FindAll`, plus
// `ProposedEdit.TargetText` + `match_mode`) is DELETED. An edit names its target by anchor
// (`target_para_id` or `target_ref`) or it is refused; nothing here searches document prose for a
// placement (ADR-049 I-7).
//
// Wire-contract note: ProposedEdit / EditSource use the SAME snake_case keys as the
// compose-draft-alternative catalog action's structured output payload (HANDOFF §1 —
// notes/HANDOFF-core-r2-A0-contract-requirements.md; design.md §6.1), so
// /api/compose/edit-batch/validate can consume the LLM's edit payload directly with no
// translation layer. All other (BFF-authored, non-LLM-facing) shapes below use the BFF's
// normal camelCase convention (see ComposeEndpoints.cs response DTOs).
//
// Offset contract (ResolvedMatch / MatchExample): these describe spans in the PLAINTEXT
// PROJECTION of the editor/DOCX content — the same projection the `compose-selection` JPS scope
// payload builds — NOT the DOCX byte stream or the TipTap JSON document model. They are the
// apply-side vocabulary of FR-20's ComposeEditBatch. The anchor pass itself never produces them:
// an anchored verdict carries `ResolvedParaId` and an EMPTY `Matches`, because the paraId IS the
// address (see EditVerdict.ResolvedParaId).
// ---------------------------------------------------------------------------

/// <summary>
/// Serializes an enum to/from its lower-case wire form (e.g. <c>Overlap</c> &lt;-&gt;
/// <c>"overlap"</c>) via <see cref="JsonNamingPolicy.CamelCase"/> — single-word enum members
/// camelCase to all-lowercase, matching the BFF's normal camelCase JSON convention
/// (<see cref="EditErrorKind"/> values, the one remaining enum on this wire surface).
/// </summary>
internal sealed class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter() : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>
/// One provenance source cited by the LLM for a proposed edit (HANDOFF §1 <c>sources[]</c>) —
/// surfaced in the Context pane per design.md §2.0 provenance pattern. Not consumed by the anchor
/// pass itself; carried through unchanged so the same payload the catalog action emits
/// round-trips through <c>/edit-batch/validate</c>.
/// </summary>
public sealed record EditSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("snippet")] string? Snippet = null);

/// <summary>
/// One LLM-proposed replacement edit — mirrors the <c>compose-draft-alternative</c>
/// catalog action's structured output payload (HANDOFF §1 / design.md §6.1) verbatim so
/// <c>POST /api/compose/edit-batch/validate</c> consumes the same JSON the catalog action
/// emits, with no translation layer.
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
/// A resolved span in <c>documentText</c> — <c>[Offset, Offset+Length)</c>, addressable for
/// downstream apply (FR-20 <c>ComposeEditBatch</c>). Relative to the plaintext projection the
/// caller applies against (see the offset contract in the file header).
/// </summary>
public sealed record ResolvedMatch(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("length")] int Length);

/// <summary>
/// One example span with a ±50-char pre/post context window, newlines flattened to a single space for a
/// single-line, copy-pasteable display. Part of the <see cref="EditValidationError"/> wire shape and
/// produced today only by batch-level <see cref="EditErrorKind.Overlap"/> reporting on the apply side:
/// an ANCHOR refusal deliberately carries none, because inventing "nearest" occurrences would reintroduce
/// exactly the guessing FR-C04 removed.
/// </summary>
public sealed record MatchExample(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("contextBefore")] string ContextBefore,
    [property: JsonPropertyName("matched")] string Matched,
    [property: JsonPropertyName("contextAfter")] string ContextAfter);

/// <summary>
/// The kind of structured refusal a validated edit (or batch) can carry.
/// <para>
/// FR-C04 (task 052) removed <c>Ambiguous</c> / <c>NoMatch</c> / <c>EmptyTarget</c> with the text-search
/// validator that was their only producer — all three described an outcome of matching <c>target_text</c>
/// against document prose, which no longer happens. <see cref="Overlap"/> survives because it is a
/// BATCH-level span collision detected on the apply side, not a text-matching outcome.
/// </para>
/// </summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum EditErrorKind
{
    /// <summary>Two edits in the same batch resolved to overlapping spans.</summary>
    Overlap,

    // ── FR-C02/FR-C03/FR-C04 anchor rejections. All are LOUD refusals: the edit named a target the
    // document does not have, named it two ways at once, or named none at all. None fall back to a
    // search — after task 052 there is no search to fall back to. ──

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
/// Structured, actionable refusal returned instead of a silently-wrong placement. Carries an honest
/// <see cref="MatchCount"/>, any example spans with surrounding context, and a copy-pasteable
/// <see cref="ResolutionHint"/> the LLM can act on without re-reasoning. For an anchor refusal
/// <see cref="MatchCount"/> is 0 and <see cref="Examples"/> is empty by construction (see
/// <see cref="MatchExample"/>); the hint names the anchor the edit should have supplied.
/// </summary>
public sealed record EditValidationError(
    [property: JsonPropertyName("kind")] EditErrorKind Kind,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("matchCount")] int MatchCount,
    [property: JsonPropertyName("examples")] IReadOnlyList<MatchExample> Examples,
    [property: JsonPropertyName("resolutionHint")] string ResolutionHint);

/// <summary>
/// Per-edit verdict: either resolved spans OR a structured error — never a silent wrong match.
/// <see cref="EditIndex"/> is the edit's position in the request batch (used to build
/// "Edit N: ..." messages so only the failing edit needs resubmission).
/// </summary>
public sealed record EditVerdict(
    [property: JsonPropertyName("editIndex")] int EditIndex,
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("matches")] IReadOnlyList<ResolvedMatch> Matches,
    [property: JsonPropertyName("error")] EditValidationError? Error,
    /// <summary>
    /// FR-C01/C02/C03 — the paragraph this edit ANCHORED to, from its <c>target_para_id</c> or
    /// <c>target_ref</c>. Non-null on EVERY valid verdict the anchor pass produces, and
    /// <see cref="Matches"/> is then empty: the paraId IS the address, and mapping it to a live span is
    /// the editor's job (it already does this for imported comments and revisions). Null only on a
    /// refusal — after FR-C04 there is no text-resolved verdict for it to mean instead.
    /// </summary>
    [property: JsonPropertyName("resolvedParaId")] string? ResolvedParaId = null);

/// <summary>
/// Batch outcome: one <see cref="EditVerdict"/> per proposed edit, plus any cross-edit overlap
/// errors detected at the batch level. Offset-drift / apply-ordering across the batch is FR-20's
/// job (<c>ComposeEditBatch</c>, task 021); <see cref="ComposeEditAnchorPass"/> itself produces no
/// batch-level errors, since anchored verdicts carry no spans to collide.
/// </summary>
public sealed record BatchValidationResult(
    [property: JsonPropertyName("verdicts")] IReadOnlyList<EditVerdict> Verdicts,
    [property: JsonPropertyName("batchErrors")] IReadOnlyList<EditValidationError> BatchErrors)
{
    /// <summary><c>true</c> only when every edit resolved AND no batch-level overlap was
    /// flagged.</summary>
    [JsonPropertyName("isValid")]
    public bool IsValid => Verdicts.All(v => v.IsValid) && BatchErrors.Count == 0;
}

/// <summary>Request body for <c>POST /api/compose/edit-batch/validate</c>.</summary>
public sealed record EditBatchValidateRequest(
    [property: JsonPropertyName("documentText")] string DocumentText,
    [property: JsonPropertyName("edits")] IReadOnlyList<ProposedEdit> Edits,
    /// <summary>
    /// FR-C02/FR-C03 (task 051) — the document's paraId reference map: BOTH the closed set an anchored
    /// edit's <c>target_para_id</c> is validated against, and the numbering data
    /// <see cref="CitationResolver"/> reads to resolve a <c>target_ref</c>. This is the SAME map the Load
    /// response already returns (<c>ParaIdMap</c>) and the session already persists
    /// (<c>ChatSession.ReferenceMap</c>) — passed in rather than fetched so this endpoint resolves only
    /// against a map the caller is entitled to. Optional: omit it for a legacy text-only batch; supply it
    /// for any batch carrying anchors, or every anchored edit is refused with
    /// <see cref="EditErrorKind.NoReferenceMap"/> rather than silently text-searched.
    /// </summary>
    [property: JsonPropertyName("referenceMap")] IReadOnlyList<ParaIdMapEntry>? ReferenceMap = null);

// ---------------------------------------------------------------------------
// FR-20 (task 021) — result models for ComposeEditBatch.Apply. BFF-authored (not
// LLM-facing), so these use the BFF's normal camelCase JSON convention (see file header).
// ---------------------------------------------------------------------------

/// <summary>
/// One edit that was actually applied by <see cref="ComposeEditBatch"/> — the resolved span it
/// replaced and the replacement text (task 021 / FR-20 §Phase 4).
/// </summary>
public sealed record AppliedEdit(
    [property: JsonPropertyName("editIndex")] int EditIndex,
    [property: JsonPropertyName("match")] ResolvedMatch Match,
    [property: JsonPropertyName("newText")] string NewText);

/// <summary>
/// One edit that was skipped because its resolved span overlapped an already-accepted span in
/// the same batch. This is the NON-FATAL failure path (Spike 3 §2 —
/// <c>notes/spikes/spike-3-edit-batch.md</c>): the batch still commits when only overlaps are
/// present; see <see cref="ComposeEditBatchResult.ValidationErrors"/> for the FATAL counterpart.
/// </summary>
public sealed record SkippedEdit(
    [property: JsonPropertyName("editIndex")] int EditIndex,
    [property: JsonPropertyName("match")] ResolvedMatch Match,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Outcome of <see cref="ComposeEditBatch.Apply"/>. Two DISTINCT failure semantics, deliberately
/// kept as separate code paths (Spike 3 §2, the whole point of task 021):
/// <list type="bullet">
/// <item><b>Validation failure</b> (a resolved <see cref="EditVerdict.IsValid"/> is <c>false</c>
/// — any anchor refusal, e.g. <see cref="EditErrorKind.NoAnchor"/>) is FATAL: <see cref="Committed"/>
/// is <c>false</c>, <see cref="DocumentText"/> equals the untouched input verbatim, NOTHING
/// applies, and <see cref="ValidationErrors"/> names every failing edit.</item>
/// <item><b>Within-batch span overlap</b> is NON-FATAL: the later-claimed span lands in
/// <see cref="Skipped"/>, every other edit still applies, and <see cref="Committed"/> is
/// <c>true</c>.</item>
/// </list>
/// </summary>
public sealed record ComposeEditBatchResult(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("documentText")] string DocumentText,
    [property: JsonPropertyName("applied")] IReadOnlyList<AppliedEdit> Applied,
    [property: JsonPropertyName("skipped")] IReadOnlyList<SkippedEdit> Skipped,
    [property: JsonPropertyName("validationErrors")] IReadOnlyList<EditValidationError> ValidationErrors);

// ---------------------------------------------------------------------------
// FR-21 (task 022) — result model for ComposeEditTransaction.Execute / .Rollback.
// BFF-authored (not LLM-facing), so this uses the BFF's normal camelCase JSON convention
// (see file header).
// ---------------------------------------------------------------------------

/// <summary>
/// Outcome of <see cref="ComposeEditTransaction.Execute"/> (or a subsequent
/// <see cref="ComposeEditTransaction.Rollback"/>). Wraps <see cref="ComposeEditBatchResult"/>
/// with explicit snapshot/commit semantics (task 022 / FR-21; Spike 3 §5.2):
/// <list type="bullet">
/// <item><see cref="Snapshot"/> is the pre-batch document — captured BEFORE
/// <see cref="ComposeEditBatch.Apply"/> runs. Strings are immutable in .NET, so holding this
/// reference IS the snapshot; no deep clone is needed (unlike adeu's DOM <c>cloneNode</c>,
/// Spike 3 §5.2 note 2).</item>
/// <item><see cref="Committed"/> <c>false</c> means <see cref="DocumentText"/> equals
/// <see cref="Snapshot"/> byte-identically — either because the underlying batch's FATAL
/// validation gate rolled back automatically (task 021 / FR-20), or because a caller invoked
/// <see cref="ComposeEditTransaction.Rollback"/> after inspecting a committed result.</item>
/// <item><see cref="Batch"/> is the underlying FR-20 result (Applied/Skipped/ValidationErrors)
/// for full reporting, even when the transaction itself was later rolled back.</item>
/// </list>
/// </summary>
public sealed record ComposeEditTransactionResult(
    [property: JsonPropertyName("committed")] bool Committed,
    [property: JsonPropertyName("documentText")] string DocumentText,
    [property: JsonPropertyName("snapshot")] string Snapshot,
    [property: JsonPropertyName("batch")] ComposeEditBatchResult Batch);
