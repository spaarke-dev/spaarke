using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

// ---------------------------------------------------------------------------
// FR-19 (task 020) — models for IComposeEditValidator / POST /api/compose/edit-batch/validate.
//
// Wire-contract note: ProposedEdit / EditSource use the SAME snake_case keys as the
// compose-draft-alternative catalog action's structured output payload (HANDOFF §1 —
// notes/HANDOFF-core-r2-A0-contract-requirements.md; design.md §6.1), so
// /api/compose/edit-batch/validate can consume the LLM's edit payload directly with no
// translation layer. All other (BFF-authored, non-LLM-facing) shapes below use the BFF's
// normal camelCase convention (see ComposeEndpoints.cs response DTOs).
//
// Document-projection contract: the `documentText` this validator is called with (see
// IComposeEditValidator) is a PLAINTEXT PROJECTION of the editor/DOCX content — the same
// projection the `compose-selection` JPS scope payload builds. Every offset in these models
// (ResolvedMatch.Offset, MatchExample.Offset) is relative to THAT projection, not to the DOCX
// byte stream or the TipTap JSON document model. Mapping back into TipTap JSON / DOCX for
// apply is the caller's job (FR-20 ComposeEditBatch), not this validator's.
// ---------------------------------------------------------------------------

/// <summary>
/// Serializes an enum to/from its lower-case wire form (e.g. <c>Strict</c> &lt;-&gt;
/// <c>"strict"</c>) via <see cref="JsonNamingPolicy.CamelCase"/> — single-word enum members
/// camelCase to all-lowercase, matching both the LLM catalog-payload contract (<c>match_mode</c>
/// values) and the BFF's normal camelCase JSON convention (<c>EditErrorKind</c> values).
/// </summary>
internal sealed class CamelCaseStringEnumConverter : JsonStringEnumConverter
{
    public CamelCaseStringEnumConverter() : base(JsonNamingPolicy.CamelCase)
    {
    }
}

/// <summary>
/// <c>match_mode</c> — how precisely a <see cref="ProposedEdit.TargetText"/> must match the
/// document before <see cref="IComposeEditValidator"/> resolves it. The LLM declares its match
/// precision; the engine owns correctness (design.md §6.1; notes/spikes/spike-2-edit-validator.md
/// §2). Serializes as <c>"strict"</c> / <c>"first"</c> / <c>"all"</c>.
/// </summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum MatchMode
{
    /// <summary>Exactly one match required; zero or N&gt;1 matches both refuse. The safe
    /// default — never applies to the wrong occurrence silently.</summary>
    Strict,

    /// <summary>Resolves the earliest occurrence in document order. Zero matches still
    /// refuses.</summary>
    First,

    /// <summary>Resolves every occurrence. Zero matches still refuses.</summary>
    All,
}

/// <summary>
/// One provenance source cited by the LLM for a proposed edit (HANDOFF §1 <c>sources[]</c>) —
/// surfaced in the Context pane per design.md §2.0 provenance pattern. Not consumed by the
/// validator itself; carried through unchanged so the same payload the catalog action emits
/// round-trips through <c>/edit-batch/validate</c>.
/// </summary>
public sealed record EditSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("snippet")] string? Snippet = null);

/// <summary>
/// One LLM-proposed find-and-replace edit — mirrors the <c>compose-draft-alternative</c>
/// catalog action's structured output payload (HANDOFF §1 / design.md §6.1) verbatim so
/// <c>POST /api/compose/edit-batch/validate</c> consumes the same JSON the catalog action
/// emits, with no translation layer.
/// </summary>
public sealed record ProposedEdit(
    [property: JsonPropertyName("target_text")] string TargetText,
    [property: JsonPropertyName("new_text")] string NewText,
    [property: JsonPropertyName("match_mode")] MatchMode Mode,
    [property: JsonPropertyName("rationale")] string? Rationale = null,
    [property: JsonPropertyName("sources")] IReadOnlyList<EditSource>? Sources = null);

/// <summary>
/// A resolved span in <c>documentText</c> — <c>[Offset, Offset+Length)</c>, addressable for
/// downstream apply (FR-20 <c>ComposeEditBatch</c>). Relative to the same plaintext projection
/// <see cref="IComposeEditValidator.Validate"/> was called with (see file header).
/// </summary>
public sealed record ResolvedMatch(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("length")] int Length);

/// <summary>
/// One example occurrence with adeu's ±50-char pre/post context window, newlines flattened to
/// a single space for a single-line, copy-pasteable display
/// (notes/spikes/spike-2-edit-validator.md §3).
/// </summary>
public sealed record MatchExample(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("contextBefore")] string ContextBefore,
    [property: JsonPropertyName("matched")] string Matched,
    [property: JsonPropertyName("contextAfter")] string ContextAfter);

/// <summary>The kind of structured refusal a validated edit (or batch) can carry.</summary>
[JsonConverter(typeof(CamelCaseStringEnumConverter))]
public enum EditErrorKind
{
    /// <summary><c>match_mode:strict</c> resolved N&gt;1 matches — the core safety refusal
    /// FR-19 exists to enforce.</summary>
    Ambiguous,

    /// <summary>Zero matches for <c>target_text</c>, in any match_mode.</summary>
    NoMatch,

    /// <summary><c>target_text</c> was empty or whitespace-only.</summary>
    EmptyTarget,

    /// <summary>Two edits in the same batch resolved to overlapping spans.</summary>
    Overlap,
}

/// <summary>
/// Structured, actionable refusal returned instead of a silently-wrong match. Carries an
/// honest match count, up to 5 example occurrences with surrounding context, and a
/// copy-pasteable <see cref="ResolutionHint"/> the LLM can act on without re-reasoning (adeu
/// <c>format_ambiguity_error</c>; notes/spikes/spike-2-edit-validator.md §3).
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
    [property: JsonPropertyName("error")] EditValidationError? Error);

/// <summary>
/// Batch outcome: one <see cref="EditVerdict"/> per proposed edit, plus any cross-edit overlap
/// errors detected at the batch level. Offset-drift / apply-ordering across the batch is FR-20's
/// job (<c>ComposeEditBatch</c>, task 021) — this validator's only batch-level duty is flagging
/// overlap (see <see cref="EditErrorKind.Overlap"/>).
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
    [property: JsonPropertyName("edits")] IReadOnlyList<ProposedEdit> Edits);

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
/// — <c>not-found</c> / <c>ambiguous</c> / <c>empty-target</c>) is FATAL: <see cref="Committed"/>
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
