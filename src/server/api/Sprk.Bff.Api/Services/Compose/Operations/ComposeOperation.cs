using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose.Operations;

// =============================================================================
// FR-11 — The shared, versioned OPERATION SCHEMA (spaarkeai-compose-r4 task 003)
// -----------------------------------------------------------------------------
// This file is the SPINE of R4: the single contract that BOTH the client
// (ProseMirror step -> operation interceptor, task 020) AND the server
// (ComposeShadowPatchEngine, task 030) implement IDENTICALLY. A serialized
// operation log MUST round-trip client -> server -> client without loss.
//
// It MIRRORS EXACTLY the client discriminated union at
//   src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.ts
// Any change here is a breaking change to that file and vice-versa.
//
// SHAPE (borrowed from Slate, per notes/bridge-prior-art.md finding #2):
//   { type, anchor, properties } — a small CLOSED discriminated union with a
//   `type` discriminator. We borrow the SHAPE, NOT Slate's identity model:
//   Slate's session-only `path` (array indices) is replaced by our DURABLE key
//   `w14:paraId` (design.md §0 D2; bridge-prior-art finding #4/#6).
//
// ANCHOR MODEL (design.md §0 D2 — REFINED 2026-07-22):
//   The durable anchor is `(paraId, runIndex, run-local-offset)`:
//     * paraId          — the Word-native `w14:paraId` node id (coarse, durable)
//     * runIndex        — 0-based index of the run WITHIN that paragraph
//     * offset          — RUN-LOCAL character offset within that run
//   NEVER run-*ids* (don't survive Word) and NEVER absolute editor positions
//   (die across server round-trips). Run boundaries are re-derived on apply by
//   the Open XML SDK (split-run-at-offset) at patch time — they are NOT encoded
//   as durable ids here.
//
// STRUCTURAL-OP SECOND-PARAGRAPH REFERENCE (op-shape decision, task 003):
//   splitParagraph / mergeParagraph / insertParagraph reference a SECOND
//   paragraph (`newParaId` / `targetParaId`). This is NOT an anchor
//   insufficiency (the escalation trigger): the anchor LOCATES the op; the
//   second paragraph is carried in the op-payload PROPERTIES slot (Slate's
//   `properties`), and it is itself a `w14:paraId` — a durable Word-native id,
//   NOT a run-id and NOT an absolute position. So it fully obeys D2/I-3.
//   See projects/spaarkeai-compose-r4/notes/task-003-operation-schema-decisions.md.
//
// PURITY (ADR-013 Tier-1 NetArchTest + ADR-007):
//   Pure records + enums + System.Text.Json ONLY. NO AI-internal type
//   (IOpenAiClient / a Services/Ai/Nodes executor / IConsumerRoutingService)
//   and NO Microsoft.Graph type may appear in this namespace. The AI redline
//   path produces ops via the facade, out-of-band — never by referencing this
//   contract's producers.
// =============================================================================

/// <summary>
/// The operation-schema contract version stamped on every
/// <see cref="ComposeOperationLog"/> envelope. Bump only on a
/// backward-INCOMPATIBLE change to the op set / anchor / property shape; both
/// ends validate the version they receive against the one they compile against.
/// Mirrors the client constant <c>COMPOSE_OPERATION_SCHEMA_VERSION</c>.
/// </summary>
public static class ComposeOperationSchema
{
    /// <summary>Current schema version — <c>compose-ops-v2</c> (R5 task 012, G12/FR-11). Bumped from
    /// <c>compose-ops-v1</c> when the closed catalog gained the <c>acceptRevision</c>/<c>rejectRevision</c>
    /// revision-reconciliation ops: adding op types is backward-INCOMPATIBLE (a v1 engine can't
    /// deserialize the new discriminators), so the bump makes a shared-deploy skew fail as a deterministic
    /// <see cref="ComposePatchErrorKind.UnsupportedSchemaVersion"/> 400 rather than a confusing 500
    /// (task-004 op-schema design §4.3).</summary>
    public const string Version = "compose-ops-v2";
}

/// <summary>
/// A RUN-LOCAL point anchor within a paragraph: the <paramref name="RunIndex"/>-th
/// run of the containing paragraph, at run-local character <paramref name="Offset"/>.
/// NEVER an absolute editor position; run boundaries are re-derived on apply
/// (design.md §0 D2). Both fields are 0-based; <c>offset == run text length</c>
/// addresses the point immediately after the last character of the run.
/// </summary>
/// <param name="RunIndex">0-based index of the run within its paragraph.</param>
/// <param name="Offset">Run-local character offset within that run (0-based).</param>
public sealed record ComposeRunPoint(
    [property: JsonPropertyName("runIndex")] int RunIndex,
    [property: JsonPropertyName("offset")] int Offset);

/// <summary>
/// A RUN-LOCAL range anchor within a SINGLE paragraph, from <paramref name="Start"/>
/// (inclusive) to <paramref name="End"/> (exclusive). Cross-paragraph spans are
/// NOT expressed as one range — they decompose into a per-paragraph
/// <see cref="DeleteRangeOperation"/> plus a <see cref="MergeParagraphOperation"/>
/// (invariant I-4 / design.md §5), keeping every range intra-paragraph so the
/// anchor stays durable.
/// </summary>
/// <param name="Start">Inclusive run-local start point.</param>
/// <param name="End">Exclusive run-local end point.</param>
public sealed record ComposeRunRange(
    [property: JsonPropertyName("start")] ComposeRunPoint Start,
    [property: JsonPropertyName("end")] ComposeRunPoint End);

/// <summary>
/// The closed set of inline character-formatting marks an op may apply/clear.
/// Mirrors the existing <c>ComposeInlineRun</c> mark surface (bold/italic/underline)
/// and the client StarterKit (MIT) marks — the honest current surface, kept
/// minimal + closed per the FR-11 "resist adding beyond the set" note. Serialized
/// as its PascalCase member name (same convention as <c>ComposeBlockKind</c> /
/// <c>ComposeParagraphAlignment</c>); the client union mirrors those literals.
/// Value-carrying marks (link/color) are a deliberate future extension via the
/// op properties slot, not part of v1.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeMarkType
{
    /// <summary><c>w:b</c> — bold.</summary>
    Bold,

    /// <summary><c>w:i</c> — italic.</summary>
    Italic,

    /// <summary><c>w:u val="single"</c> — single underline.</summary>
    Underline,
}

/// <summary>
/// The closed set of paragraph-level (block) attributes a
/// <see cref="SetBlockAttrOperation"/> may set. Paragraph-scoped — anchored by
/// paraId alone (no run offset). The <see cref="SetBlockAttrOperation.Value"/> is
/// interpreted per attribute (see each member). Mirrors the block-level fields
/// already on <c>ComposeBlock</c> (Kind/Level/Ordered/Alignment). Serialized as
/// its PascalCase member name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeBlockAttr
{
    /// <summary>Paragraph alignment; value = a <c>ComposeParagraphAlignment</c> name
    /// (<c>Default</c>/<c>Left</c>/<c>Center</c>/<c>Right</c>/<c>Justify</c>).</summary>
    Alignment,

    /// <summary>Paragraph style; value = a style name (<c>Normal</c>/<c>Heading1</c>..<c>Heading6</c>/
    /// <c>ListParagraph</c>). Drives the block kind + numbering the engine applies.</summary>
    Style,

    /// <summary>List ordering; value = <c>"true"</c> (ordered/numbered) or <c>"false"</c> (bullet).</summary>
    ListOrdered,

    /// <summary>List nesting depth; value = a 0-based integer as a string (<c>"0"</c>..<c>"8"</c>).</summary>
    ListLevel,
}

/// <summary>
/// Base of the CLOSED operation discriminated union (FR-11). Exactly twelve derived
/// types, tagged by the <c>type</c> discriminator with the FR-11 literal names.
/// Every op carries a <see cref="ParaId"/> — the durable <c>w14:paraId</c> coarse
/// anchor. System.Text.Json polymorphic (de)serialization is driven by
/// <see cref="JsonPolymorphicAttribute"/> below; the discriminator writes/reads
/// as the first property named <c>type</c>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(InsertTextOperation), "insertText")]
[JsonDerivedType(typeof(DeleteRangeOperation), "deleteRange")]
[JsonDerivedType(typeof(ReplaceRangeOperation), "replaceRange")]
[JsonDerivedType(typeof(SetMarkOperation), "setMark")]
[JsonDerivedType(typeof(ClearMarkOperation), "clearMark")]
[JsonDerivedType(typeof(SplitParagraphOperation), "splitParagraph")]
[JsonDerivedType(typeof(MergeParagraphOperation), "mergeParagraph")]
[JsonDerivedType(typeof(InsertParagraphOperation), "insertParagraph")]
[JsonDerivedType(typeof(DeleteParagraphOperation), "deleteParagraph")]
[JsonDerivedType(typeof(SetBlockAttrOperation), "setBlockAttr")]
[JsonDerivedType(typeof(AcceptRevisionOperation), "acceptRevision")]
[JsonDerivedType(typeof(RejectRevisionOperation), "rejectRevision")]
public abstract record ComposeOperation
{
    /// <summary>
    /// The durable <c>w14:paraId</c> of the paragraph this op is anchored to (8-hex
    /// <c>ST_LongHexNumber</c>). Always present. The coarse anchor resolved O(1) at
    /// patch time; the fine anchor (runIndex + run-local offset) rides the op's
    /// point/range field where applicable.
    /// </summary>
    [JsonPropertyName("paraId")]
    public required string ParaId { get; init; }
}

/// <summary>
/// <c>insertText</c> — insert <see cref="Text"/> at the run-local point <see cref="At"/>
/// within paragraph <see cref="ComposeOperation.ParaId"/>, optionally carrying inline
/// <see cref="Marks"/>. The patch engine splits the run at the offset and inserts as
/// native <c>w:ins</c>.
/// </summary>
public sealed record InsertTextOperation : ComposeOperation
{
    /// <summary>Run-local insertion point.</summary>
    [JsonPropertyName("at")]
    public required ComposeRunPoint At { get; init; }

    /// <summary>The text to insert (never null; may be empty for a no-op guard).</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Inline marks to apply to the inserted text. Empty = inherit run formatting.</summary>
    [JsonPropertyName("marks")]
    public IReadOnlyList<ComposeMarkType> Marks { get; init; } = Array.Empty<ComposeMarkType>();
}

/// <summary>
/// <c>deleteRange</c> — delete the intra-paragraph run-local <see cref="Range"/> within
/// paragraph <see cref="ComposeOperation.ParaId"/>. Emitted natively as <c>w:del</c>.
/// </summary>
public sealed record DeleteRangeOperation : ComposeOperation
{
    /// <summary>The run-local range to delete (intra-paragraph).</summary>
    [JsonPropertyName("range")]
    public required ComposeRunRange Range { get; init; }
}

/// <summary>
/// <c>replaceRange</c> — replace the intra-paragraph run-local <see cref="Range"/> with
/// <see cref="Text"/> (optionally carrying <see cref="Marks"/>). Equivalent to a
/// <see cref="DeleteRangeOperation"/> + <see cref="InsertTextOperation"/> at the range
/// start, kept as one op so the client interceptor can emit it from a single
/// ProseMirror <c>ReplaceStep</c>.
/// </summary>
public sealed record ReplaceRangeOperation : ComposeOperation
{
    /// <summary>The run-local range to replace (intra-paragraph).</summary>
    [JsonPropertyName("range")]
    public required ComposeRunRange Range { get; init; }

    /// <summary>The replacement text (never null; may be empty ⇒ pure delete).</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    /// <summary>Inline marks to apply to the replacement text. Empty = inherit.</summary>
    [JsonPropertyName("marks")]
    public IReadOnlyList<ComposeMarkType> Marks { get; init; } = Array.Empty<ComposeMarkType>();
}

/// <summary>
/// <c>setMark</c> — apply the inline <see cref="Mark"/> over the intra-paragraph
/// run-local <see cref="Range"/> within paragraph <see cref="ComposeOperation.ParaId"/>.
/// </summary>
public sealed record SetMarkOperation : ComposeOperation
{
    /// <summary>The run-local range to mark.</summary>
    [JsonPropertyName("range")]
    public required ComposeRunRange Range { get; init; }

    /// <summary>The mark to apply.</summary>
    [JsonPropertyName("mark")]
    public required ComposeMarkType Mark { get; init; }
}

/// <summary>
/// <c>clearMark</c> — remove the inline <see cref="Mark"/> over the intra-paragraph
/// run-local <see cref="Range"/> within paragraph <see cref="ComposeOperation.ParaId"/>.
/// </summary>
public sealed record ClearMarkOperation : ComposeOperation
{
    /// <summary>The run-local range to clear.</summary>
    [JsonPropertyName("range")]
    public required ComposeRunRange Range { get; init; }

    /// <summary>The mark to remove.</summary>
    [JsonPropertyName("mark")]
    public required ComposeMarkType Mark { get; init; }
}

/// <summary>
/// <c>splitParagraph</c> — split paragraph <see cref="ComposeOperation.ParaId"/> at the
/// run-local point <see cref="At"/>. Content BEFORE <see cref="At"/> stays in
/// <see cref="ComposeOperation.ParaId"/>; content AT/AFTER moves into a NEW paragraph
/// identified by <see cref="NewParaId"/>, inserted immediately AFTER
/// <see cref="ComposeOperation.ParaId"/>.
/// </summary>
/// <remarks>
/// <see cref="NewParaId"/> is a minted <c>w14:paraId</c> (a durable Word-native id,
/// NOT a run-id / absolute position) carried in the op-payload properties slot — the
/// task-003 op-shape decision (D2-compliant). The anchor (<see cref="At"/>) locates
/// WHERE to split.
/// </remarks>
public sealed record SplitParagraphOperation : ComposeOperation
{
    /// <summary>The run-local split point.</summary>
    [JsonPropertyName("at")]
    public required ComposeRunPoint At { get; init; }

    /// <summary>The minted <c>w14:paraId</c> for the new (trailing) paragraph.</summary>
    [JsonPropertyName("newParaId")]
    public required string NewParaId { get; init; }
}

/// <summary>
/// <c>mergeParagraph</c> — append the entire content of paragraph
/// <see cref="ComposeOperation.ParaId"/> onto the end of <see cref="TargetParaId"/>
/// (its predecessor) and REMOVE <see cref="ComposeOperation.ParaId"/>.
/// <see cref="TargetParaId"/> is the surviving paragraph.
/// </summary>
/// <remarks>
/// The paragraph-mark deletion is emitted as <c>w:del</c> on the para-mark glyph
/// (bridge-prior-art finding #6, EDGE-hard case). <see cref="TargetParaId"/> is a
/// durable <c>w14:paraId</c> carried in the op properties slot (task-003 op-shape
/// decision; D2-compliant — NOT a run-id / absolute position).
/// </remarks>
public sealed record MergeParagraphOperation : ComposeOperation
{
    /// <summary>The surviving predecessor paragraph's <c>w14:paraId</c> that receives the content.</summary>
    [JsonPropertyName("targetParaId")]
    public required string TargetParaId { get; init; }
}

/// <summary>Where a <see cref="InsertParagraphOperation"/> places the new paragraph
/// relative to its reference paragraph. Serialized as its PascalCase member name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeParagraphPosition
{
    /// <summary>Insert the new paragraph immediately BEFORE the reference paragraph.</summary>
    Before,

    /// <summary>Insert the new paragraph immediately AFTER the reference paragraph.</summary>
    After,
}

/// <summary>
/// <c>insertParagraph</c> — insert a NEW empty paragraph identified by
/// <see cref="NewParaId"/> at <see cref="Position"/> relative to the reference
/// paragraph <see cref="ComposeOperation.ParaId"/>. Content is added by subsequent
/// <see cref="InsertTextOperation"/>s targeting <see cref="NewParaId"/>; optional
/// initial block formatting is applied by a following <see cref="SetBlockAttrOperation"/>.
/// Distinct from <see cref="SplitParagraphOperation"/> (which divides an existing
/// paragraph); this creates a brand-new adjacent one.
/// </summary>
/// <remarks>
/// <see cref="NewParaId"/> is a minted durable <c>w14:paraId</c> in the op properties
/// slot (task-003 op-shape decision; D2-compliant).
/// </remarks>
public sealed record InsertParagraphOperation : ComposeOperation
{
    /// <summary>The minted <c>w14:paraId</c> for the new paragraph.</summary>
    [JsonPropertyName("newParaId")]
    public required string NewParaId { get; init; }

    /// <summary>Whether the new paragraph goes before or after the reference paragraph.</summary>
    [JsonPropertyName("position")]
    public required ComposeParagraphPosition Position { get; init; }
}

/// <summary>
/// <c>deleteParagraph</c> — remove the entire paragraph
/// <see cref="ComposeOperation.ParaId"/> (its runs and its paragraph mark). Emitted as
/// native <c>w:del</c> over the paragraph content + para-mark.
/// </summary>
public sealed record DeleteParagraphOperation : ComposeOperation
{
    // No fields beyond ParaId — the paragraph id fully identifies the target.
}

/// <summary>
/// <c>setBlockAttr</c> — set the paragraph-level <see cref="Attr"/> to <see cref="Value"/>
/// on paragraph <see cref="ComposeOperation.ParaId"/>. Paragraph-scoped, anchored by
/// paraId alone (no run offset). <see cref="Value"/> is interpreted per
/// <see cref="ComposeBlockAttr"/> (see that enum); a <c>null</c> value clears the
/// attribute back to its style default.
/// </summary>
public sealed record SetBlockAttrOperation : ComposeOperation
{
    /// <summary>Which block attribute to set.</summary>
    [JsonPropertyName("attr")]
    public required ComposeBlockAttr Attr { get; init; }

    /// <summary>The attribute value (interpreted per <see cref="Attr"/>); <c>null</c> clears to style default.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; init; }
}

/// <summary>
/// The scope of an <see cref="AcceptRevisionOperation"/> / <see cref="RejectRevisionOperation"/>: reconcile
/// a SINGLE imported revision (addressed by its native OOXML <c>w:id</c>) or ALL tracked revisions in the
/// document (batch, deterministic document order). Serialized as its PascalCase member name (same convention
/// as <see cref="ComposeBlockAttr"/> / <see cref="ComposeParagraphPosition"/>); the client union mirrors the
/// literals. R5 task 004 op-schema design §3; task 012 implements <see cref="Single"/>, task 013 adds
/// <see cref="All"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeRevisionScope
{
    /// <summary>Reconcile exactly one revision, addressed by <c>revisionId</c> (its native <c>w:id</c>).</summary>
    Single,

    /// <summary>Reconcile EVERY tracked revision in the document, in document order (batch — task 013).</summary>
    All,
}

/// <summary>
/// <c>acceptRevision</c> — ACCEPT a pre-existing IMPORTED Word tracked change (native <c>w:ins</c>/<c>w:del</c>
/// recovered on Load as an <c>ImportedRevision</c>), reconciling it into settled content so a subsequent Save
/// no longer refuses with <see cref="ComposePatchErrorKind.TrackedChangeReconciliationUnsupported"/> (the ET-2
/// gap / FR-11). Accepting a <c>w:ins</c> STRIPS the insertion wrapper keeping the inserted run as normal
/// content; accepting a <c>w:del</c> REMOVES the run entirely. The revision is located BY <see cref="RevisionId"/>
/// (its native <c>w:id</c>) within paragraph <see cref="ComposeOperation.ParaId"/> — NEVER by a text/content
/// match (invariant I-7 / NFR-02).
/// </summary>
public sealed record AcceptRevisionOperation : ComposeOperation
{
    /// <summary><see cref="ComposeRevisionScope.Single"/> = one revision by <see cref="RevisionId"/>;
    /// <see cref="ComposeRevisionScope.All"/> = every tracked revision (task 013 batch).</summary>
    [JsonPropertyName("scope")]
    public required ComposeRevisionScope Scope { get; init; }

    /// <summary>The native OOXML <c>w:ins</c>/<c>w:del</c> <c>w:id</c> to accept (= <c>ImportedRevision.id</c>).
    /// Required (non-null) for <see cref="ComposeRevisionScope.Single"/>; <c>null</c> for
    /// <see cref="ComposeRevisionScope.All"/>.</summary>
    [JsonPropertyName("revisionId")]
    public string? RevisionId { get; init; }
}

/// <summary>
/// <c>rejectRevision</c> — REJECT a pre-existing imported Word tracked change (native <c>w:ins</c>/<c>w:del</c>),
/// the INVERSE of <see cref="AcceptRevisionOperation"/>: rejecting a <c>w:ins</c> REMOVES the inserted run;
/// rejecting a <c>w:del</c> RESTORES the deleted run as normal content (<c>w:delText</c> → <c>w:t</c>, wrapper
/// stripped). Located BY <see cref="RevisionId"/> within paragraph <see cref="ComposeOperation.ParaId"/> —
/// never by text/content match (I-7 / NFR-02). Structurally identical to
/// <see cref="AcceptRevisionOperation"/> (two discriminators, one per semantic action — the catalog's
/// existing <c>setMark</c>/<c>clearMark</c> convention).
/// </summary>
public sealed record RejectRevisionOperation : ComposeOperation
{
    /// <summary><see cref="ComposeRevisionScope.Single"/> = one revision by <see cref="RevisionId"/>;
    /// <see cref="ComposeRevisionScope.All"/> = every tracked revision (task 013 batch).</summary>
    [JsonPropertyName("scope")]
    public required ComposeRevisionScope Scope { get; init; }

    /// <summary>The native OOXML <c>w:ins</c>/<c>w:del</c> <c>w:id</c> to reject (= <c>ImportedRevision.id</c>).
    /// Required (non-null) for <see cref="ComposeRevisionScope.Single"/>; <c>null</c> for
    /// <see cref="ComposeRevisionScope.All"/>.</summary>
    [JsonPropertyName("revisionId")]
    public string? RevisionId { get; init; }
}

/// <summary>
/// The op-log ENVELOPE (FR-11): an ORDERED sequence of <see cref="Operations"/> plus the
/// <see cref="SchemaVersion"/> both ends validate against. This is the artifact the
/// client capture path (task 020) produces and the server patch engine (task 030)
/// consumes; it round-trips client → server → client without loss.
/// </summary>
public sealed record ComposeOperationLog
{
    /// <summary>The operation-schema contract version (defaults to <see cref="ComposeOperationSchema.Version"/>).</summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = ComposeOperationSchema.Version;

    /// <summary>The ordered operations to apply, in author (rebased) order.</summary>
    [JsonPropertyName("operations")]
    public IReadOnlyList<ComposeOperation> Operations { get; init; } = Array.Empty<ComposeOperation>();
}
