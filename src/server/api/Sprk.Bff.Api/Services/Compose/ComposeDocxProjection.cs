namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Outcome of a server-side DOCX → editor projection (Phase 1, design
/// <c>notes/design-server-side-docx-html-conversion.md</c> §3.3). Replaces the client-side mammoth
/// convert + position-based paraId stamping — the two-engine drift that produced the recurring
/// "w14:paraId matches no paragraph in the retained original" save failures.
/// </summary>
/// <remarks>
/// Fail-closed (F-04 / GPT §11): the client MUST NOT infer success from <see cref="Html"/> being
/// non-empty. A projection <b>failure</b> (vs. a legitimately empty document) must not mount a blank
/// editable surface over a non-empty retained baseline — the client keys off <see cref="Status"/> +
/// <see cref="CanEdit"/>. Load still returns the source bytes; only the projection fails closed.
/// </remarks>
public sealed record ComposeDocxProjection
{
    /// <summary>Success = fully projected; Partial = projected with fidelity warnings; Failed = could not project.</summary>
    public required ComposeProjectionStatus Status { get; init; }

    /// <summary>
    /// False ⇒ the client mounts a read-only / "Open in Word" state rather than an editable document.
    /// True only when the projection produced editable content the save path can safely delta against.
    /// </summary>
    public required bool CanEdit { get; init; }

    /// <summary>
    /// paraId-tagged, TipTap-shaped HTML (<c>data-paraid</c> on every block). The editor's paraId
    /// extension parses <c>data-paraid</c> on <c>setContent</c> — no client stamping. Tier-3 content
    /// (document text) — NEVER logged. Empty when <see cref="Status"/> is <see cref="ComposeProjectionStatus.Failed"/>.
    /// </summary>
    public string Html { get; init; } = string.Empty;

    /// <summary>
    /// The ordered <c>w14:paraId</c> map, one entry per body paragraph in <c>Descendants&lt;Paragraph&gt;()</c>
    /// document order — the SAME sequence the HTML blocks carry (produced from the same paragraph instances,
    /// NOT an ordinal re-join). Consumed by the save-side <see cref="ComposeBaselineParaIdStamper"/> and by
    /// <see cref="ComposeService"/>'s imported-revision/comment paraId resolution. Empty (never null) for a
    /// body-less document.
    /// </summary>
    public IReadOnlyList<ParaIdMapEntry> ParaIdMap { get; init; } = Array.Empty<ParaIdMapEntry>();

    /// <summary>
    /// FR-01 (task 011) — the INTRA-PARAGRAPH OFFSET-ADDRESSING TABLE: one <see cref="ParaOffsetMap"/> per
    /// body paragraph, index-aligned with <see cref="ParaIdMap"/> (same order, same paraIds, same
    /// <c>Descendants&lt;Paragraph&gt;()</c> walk). It is the D2 FINE anchor's server-side resolver: given a
    /// paraId (the coarse anchor) and an <b>editor-offset</b> within that paragraph, the map resolves the exact
    /// <c>(runIndex, run-local-offset)</c> OOXML run split the Patch Engine (task 030) applies — WITHOUT
    /// re-walking the DOM or any text-search (I-3 / I-7 / design §5.0 "offset→run mapping").
    /// <para>
    /// The run sequence is the paragraph's <b>editor-visible run flatten</b> in document order — the SAME
    /// descent <see cref="ComposeDocxProjectionBuilder"/> uses to emit the HTML (into <c>w:hyperlink</c>,
    /// <c>w:ins</c>, <c>w:del</c>, <c>w:sdt</c>), so the offset space matches EXACTLY what the client measures
    /// over the projection. Runs nested in pre-existing tracked changes are therefore addressable and are
    /// tagged (<see cref="RunBoundary.TrackChange"/>); the table records the structural fact, it does not
    /// decide the tracked-change merge semantic (that is the Patch Engine's, task 030). On a track-changes-
    /// clean, hyperlink-free paragraph this flatten coincides with <c>para.Elements&lt;Run&gt;()</c> — the
    /// enumeration the task-005 applier spike used. Empty (never null) when there is no body.
    /// </para>
    /// </summary>
    public IReadOnlyList<ParaOffsetMap> OffsetAddressingTable { get; init; } = Array.Empty<ParaOffsetMap>();

    /// <summary>
    /// FR-02 (task 012) — the OPAQUE BLOCK ATOMS: one <see cref="ComposeBlockAtom"/> per whole-construct
    /// non-renderable content control (a block-level <c>w:sdt</c> whose declared type is genuinely non-text
    /// — see <see cref="ComposeDocxProjectionBuilder"/>'s <c>IsSpecialSdtControl</c>). Each entry's
    /// <see cref="ComposeBlockAtom.AtomId"/> is minted from the SAME 8-hex, collision-checked pool as
    /// paragraph <c>w14:paraId</c>s (format-consistent, never colliding with one) but is tracked in this
    /// SEPARATE list rather than <see cref="ParaIdMap"/> — so the established "every <c>data-paraid</c> in
    /// the HTML has a <see cref="ParaIdMap"/> entry" contract (the F-01 single-walk invariant) stays intact.
    /// The HTML carries the atom's id via a DISTINCT <c>data-atomid</c> attribute (never <c>data-paraid</c>)
    /// on a non-editable placeholder (<c>contenteditable="false"</c>), preserving document order by
    /// construction (emitted in the SAME single render pass as paragraphs). Inline-scoped atoms (fields,
    /// inline content controls, complex/floating objects — all nested WITHIN a paragraph's own runs) do not
    /// need a separate identity here; they carry their containing paragraph's <c>w14:paraId</c> and are
    /// signaled via <see cref="RunBoundary.AtomKind"/> in <see cref="OffsetAddressingTable"/> instead. Empty
    /// (never null) when the document has no whole-construct atoms.
    /// </summary>
    public IReadOnlyList<ComposeBlockAtom> BlockAtoms { get; init; } = Array.Empty<ComposeBlockAtom>();

    /// <summary>Machine-readable, user-presentable fidelity warnings — codes + counts only (no document content).</summary>
    public IReadOnlyList<ComposeProjectionWarning> Warnings { get; init; } = Array.Empty<ComposeProjectionWarning>();

    /// <summary>
    /// Projection contract version. Phase 1 is <c>compose-html-v1</c> (HTML, transitional). A future
    /// TipTap-JSON / in-pass-marks projection (design §11 Phase 2/3) is a versioned change, not a silent one.
    /// </summary>
    public string SchemaVersion { get; init; } = "compose-html-v1";

    /// <summary>A closed (never editable) failure projection carrying an optional diagnostic code.</summary>
    public static ComposeDocxProjection Failed(string? code = null) => new()
    {
        Status = ComposeProjectionStatus.Failed,
        CanEdit = false,
        Html = string.Empty,
        Warnings = code is null
            ? Array.Empty<ComposeProjectionWarning>()
            : new[] { new ComposeProjectionWarning(code, 1) },
    };
}

/// <summary>Phase-1 projection status. Drives the client's fail-closed mount decision (design §4).</summary>
public enum ComposeProjectionStatus
{
    /// <summary>Fully projected; mount editable.</summary>
    Success,

    /// <summary>Projected with fidelity gaps (see <see cref="ComposeDocxProjection.Warnings"/>); mount editable with a banner.</summary>
    Partial,

    /// <summary>Could not project; client must NOT mount an editable blank over the source.</summary>
    Failed,
}

/// <summary>
/// A single fidelity warning: a stable <paramref name="Code"/> (e.g. <c>multi-level-numbering</c>,
/// <c>unrendered-paragraphs</c>, <c>numbering-unresolved</c>, <c>content-control</c>), the
/// <paramref name="Count"/> of occurrences, and an optional non-content <paramref name="Detail"/>.
/// Carries NO document text (Tier-1 safe).
/// </summary>
public sealed record ComposeProjectionWarning(string Code, int Count, string? Detail = null);

/// <summary>
/// Task 020 (spaarkeai-compose-r6, FR-03) — the result of projecting a <c>.docx</c> into the CANONICAL
/// content model (<see cref="ComposeContentModel"/>) via
/// <see cref="ComposeDocxProjectionBuilder.BuildContentModel"/>: the render-on-save hub's imported-doc
/// SOURCE. Sibling of <see cref="ComposeDocxProjection"/> (the HTML read/browse projection) — same
/// builder, same traversal idioms, different output shape. NOT a second body model (root §11 / the
/// task's extension-only escalation trigger): <see cref="Model"/> IS the one existing
/// <see cref="ComposeContentModel"/>; this record is only the status/warnings envelope around it,
/// mirroring <see cref="ComposeDocxProjection"/>'s own envelope convention.
/// </summary>
/// <remarks>
/// TOTAL / lenient by construction (the ADR-049 Path-B flatten-tier posture): projection NEVER throws —
/// an unreadable source degrades to <see cref="ComposeProjectionStatus.Failed"/> with an empty model,
/// and every construct the thin model cannot carry flattens to its nearest editable form (or is dropped)
/// with a counted <see cref="Warnings"/> entry, never a hard-fail. Per-feature fidelity (numbering
/// labels, tables, headers/footers, tracked-changes, hard-tier) is widened by tasks 021–026 THROUGH this
/// same model; the flatten warning codes are the seam those tasks retire one by one.
/// </remarks>
public sealed record ComposeCanonicalModelProjection
{
    /// <summary>Mirrors <see cref="ComposeDocxProjection.Status"/>: Success (clean), Partial (projected
    /// with counted fidelity warnings — still renderable), Failed (unreadable/empty/over-cap source;
    /// <see cref="Model"/> is empty and MUST NOT be rendered over a non-empty original).</summary>
    public required ComposeProjectionStatus Status { get; init; }

    /// <summary>The canonical content model — the single hub every source projects INTO and
    /// <see cref="ComposeDocumentRenderer.SynthesizeDocument"/> renders OUT of. Empty when
    /// <see cref="Status"/> is <see cref="ComposeProjectionStatus.Failed"/>.</summary>
    public ComposeContentModel Model { get; init; } = new();

    /// <summary>Counted flatten/degradation warnings (codes + counts only — Tier-1 safe, no document
    /// text). Non-empty ⇒ <see cref="Status"/> is <see cref="ComposeProjectionStatus.Partial"/>.</summary>
    public IReadOnlyList<ComposeProjectionWarning> Warnings { get; init; } = Array.Empty<ComposeProjectionWarning>();

    /// <summary>Fail-closed factory — mirrors <see cref="ComposeDocxProjection.Failed"/>.</summary>
    public static ComposeCanonicalModelProjection Failed(string code) => new()
    {
        Status = ComposeProjectionStatus.Failed,
        Warnings = new[] { new ComposeProjectionWarning(code, 1) },
    };
}

// =============================================================================================
// FR-01 (task 011) — intra-paragraph OFFSET-ADDRESSING TABLE types.
// The D2 fine anchor's deterministic server-side resolver: (paraId, editor-offset) → run split.
// Pure records + integer offset math (no document text, Tier-1 safe; no AI type, no Graph type).
// =============================================================================================

/// <summary>
/// The pre-existing tracked-change wrapper (if any) a <see cref="RunBoundary"/> sits inside in the loaded
/// document. The offset-addressing table INCLUDES tracked-change runs (their text is editor-visible per the
/// projection's F-02 revision flattening) so an editor-offset that lands in pre-existing inserted/deleted
/// content still resolves to a run split. This tag records the STRUCTURAL fact only; how the Patch Engine
/// (task 030) reconciles a new edit that lands inside an existing <c>w:ins</c>/<c>w:del</c> is its concern,
/// not the table's.
/// </summary>
public enum RunTrackChange
{
    /// <summary>Settled run (direct paragraph/hyperlink content, not inside a revision wrapper).</summary>
    None,

    /// <summary>Run nested in a pre-existing <c>w:ins</c> (<see cref="DocumentFormat.OpenXml.Wordprocessing.InsertedRun"/>).</summary>
    Inserted,

    /// <summary>Run nested in a pre-existing <c>w:del</c> (<see cref="DocumentFormat.OpenXml.Wordprocessing.DeletedRun"/>).</summary>
    Deleted,
}

/// <summary>
/// One editor-visible run's boundary within a paragraph's editor-offset space. <see cref="RunIndex"/> is the
/// run's 0-based position in the paragraph's document-order editor-visible run flatten (the canonical anchor
/// run-index space for the whole bridge — the Patch Engine indexes the same flatten). The run occupies the
/// half-open editor-offset interval <c>[StartOffset, StartOffset + Length)</c>; <see cref="Length"/> is the
/// count of editor-visible characters the run contributes (its <c>w:t</c>/<c>w:delText</c> text length, plus
/// one per <c>w:br</c>/<c>w:tab</c>/<c>w:noBreakHyphen</c> glyph — matching what the projection emits).
/// </summary>
public sealed record RunBoundary(int RunIndex, int StartOffset, int Length, RunTrackChange TrackChange, ComposeAtomKind? AtomKind = null)
{
    /// <summary>
    /// True when this run is an OPAQUE ATOM (FR-02, task 012) — a Word field, an inline non-text content
    /// control, or a complex/floating object — never opened/reinterpreted, so it survives save byte-for-
    /// byte by construction (I-4). The projection SIGNALS this here; a caller (the client schema of task
    /// 021, a future Patch Engine) must never generate an operation that targets strictly INSIDE the run —
    /// only at its boundaries (<see cref="StartOffset"/> / <see cref="StartOffset"/>+<see cref="Length"/>).
    /// </summary>
    public bool IsAtom => AtomKind is not null;
}

/// <summary>
/// The offset-addressing entry for ONE paragraph: its <see cref="ParaId"/> plus the ordered
/// <see cref="Runs"/> boundary list, and the deterministic <c>(editor-offset) → (runIndex, run-local-offset)</c>
/// resolver the Patch Engine consumes. Resolution is LEFT-biased at run boundaries and REJECTS an offset past
/// the paragraph end (never clamps) — see <see cref="TryResolve"/> / <see cref="Resolve"/>.
/// </summary>
public sealed record ParaOffsetMap
{
    /// <summary>The paragraph's <c>w14:paraId</c> (8-hex, uppercased) — the coarse anchor this map is keyed to.</summary>
    public required string ParaId { get; init; }

    /// <summary>The paragraph's editor-visible runs, in document order (see <see cref="RunBoundary"/>).</summary>
    public required IReadOnlyList<RunBoundary> Runs { get; init; }

    /// <summary>
    /// The paragraph's total editor-visible length (sum of run lengths). The valid closed offset domain is
    /// <c>[0, TotalLength]</c>; <c>TotalLength</c> itself addresses the point immediately after the last
    /// character (paragraph end); anything greater is out of range.
    /// </summary>
    public int TotalLength => Runs.Count == 0 ? 0 : Runs[^1].StartOffset + Runs[^1].Length;

    /// <summary>
    /// Deterministically resolves an <paramref name="editorOffset"/> within this paragraph to the exact
    /// <c>(runIndex, run-local-offset)</c> run split. Returns <c>false</c> (rejects, does NOT clamp) when
    /// the offset is negative or past <see cref="TotalLength"/>. At an interior run boundary the offset is
    /// LEFT-biased onto the following non-empty run's start (both encode the same physical split point).
    /// The terminal offset (<c>== TotalLength</c>) resolves to the last non-empty run's end.
    /// </summary>
    public bool TryResolve(int editorOffset, out RunOffsetResolution resolution)
    {
        resolution = default;
        if (editorOffset < 0 || editorOffset > TotalLength)
        {
            return false; // FR-01 negative: out of range is REJECTED, never silently clamped.
        }

        // Interior: the first run whose half-open span [Start, Start+Length) strictly contains the offset.
        // Zero-length runs are structurally present (they preserve run-index alignment) but can never
        // contain an offset, so they are skipped here and only matter for indexing.
        foreach (var run in Runs)
        {
            if (run.Length > 0 && editorOffset < run.StartOffset + run.Length)
            {
                resolution = new RunOffsetResolution(run.RunIndex, editorOffset - run.StartOffset);
                return true;
            }
        }

        // Terminal (editorOffset == TotalLength): the point after the last character → last non-empty run's end.
        for (var i = Runs.Count - 1; i >= 0; i--)
        {
            if (Runs[i].Length > 0)
            {
                resolution = new RunOffsetResolution(Runs[i].RunIndex, Runs[i].Length);
                return true;
            }
        }

        // Degenerate: an empty paragraph (no runs, or only zero-length runs) — only offset 0 reaches here.
        resolution = new RunOffsetResolution(0, 0);
        return true;
    }

    /// <summary>
    /// Strict variant of <see cref="TryResolve"/>: resolves the split or throws
    /// <see cref="ArgumentOutOfRangeException"/> for an offset past the paragraph end (the FR-01 refusal
    /// surfaced as an exception for callers that treat out-of-range as a hard error).
    /// </summary>
    public RunOffsetResolution Resolve(int editorOffset)
    {
        if (!TryResolve(editorOffset, out var resolution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(editorOffset), editorOffset,
                $"Editor offset {editorOffset} is out of range for paraId {ParaId} (valid domain [0, {TotalLength}]).");
        }

        return resolution;
    }
}

/// <summary>
/// The resolved D2 fine anchor: the 0-based <see cref="RunIndex"/> into the paragraph's editor-visible run
/// flatten and the <see cref="RunLocalOffset"/> within that run at which the Patch Engine splits. A value
/// type — pure integer data, no document content.
/// </summary>
public readonly record struct RunOffsetResolution(int RunIndex, int RunLocalOffset);

// =============================================================================================
// FR-02 (task 012) — OPAQUE ATOMS. Non-renderable OOXML constructs (SDT/content controls, fields,
// complex/floating objects) never opened/reinterpreted, so their subtree is byte-identical after a no-op
// save by construction (I-4) — the Patch Engine only ever touches subtrees an operation targets, and no
// operation can target inside an atom (see RunBoundary.IsAtom / ComposeBlockAtom above). Pure classification
// data only (no document text beyond the atom's own cached display value — Tier-3, never logged as such).
// =============================================================================================

/// <summary>Which non-renderable OOXML construct an opaque atom represents (FR-02, task 012).</summary>
public enum ComposeAtomKind
{
    /// <summary>A content control (<c>w:sdt</c>) whose declared type is not plain/rich text.</summary>
    Sdt,

    /// <summary>A Word field — <c>w:fldSimple</c>, or a <c>w:fldChar</c> begin/instrText/separate/end run sequence.</summary>
    Field,

    /// <summary>A complex or floating object — <c>w:drawing</c> (image/shape) or <c>w:object</c> (OLE embed).</summary>
    ComplexObject,
}

/// <summary>
/// A WHOLE-CONSTRUCT opaque atom (FR-02, task 012) — a block-level <c>w:sdt</c> whose content is not
/// ordinary editable prose (see <see cref="ComposeDocxProjectionBuilder"/>'s <c>IsSpecialSdtControl</c>
/// escalation-boundary rule). <see cref="AtomId"/> is an 8-hex id minted from the same collision-checked
/// pool as paragraph <c>w14:paraId</c>s but tracked separately from <see cref="ComposeDocxProjection.ParaIdMap"/>
/// (see that property's remarks for why). Carried in <see cref="ComposeDocxProjection.BlockAtoms"/>, document
/// order preserved by construction (emitted in the same single render pass as paragraphs).
/// </summary>
public sealed record ComposeBlockAtom(string AtomId, ComposeAtomKind Kind);
