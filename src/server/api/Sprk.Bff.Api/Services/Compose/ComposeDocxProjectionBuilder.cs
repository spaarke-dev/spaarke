using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Phase-1 server-authoritative DOCX → editor projection (design
/// <c>notes/design-server-side-docx-html-conversion.md</c>). Walks the source <c>.docx</c> ONCE and emits
/// paraId-tagged, TipTap-shaped HTML — replacing the client-side mammoth convert + position-based paraId
/// stamping that caused the recurring save-abort bug class.
/// </summary>
/// <remarks>
/// <para>
/// <b>The single-walk invariant (the whole point).</b> The recurring
/// <c>"w14:paraId matches no paragraph in the retained original"</c> save failures came from TWO engines
/// walking the same document (server <see cref="ParaIdPreParser"/> vs. client mammoth) and joining their
/// outputs by ordinal paragraph index. This builder is the single engine: it assigns each paragraph's
/// <c>w14:paraId</c> and emits that paragraph's editor block from the SAME <see cref="Paragraph"/> instance.
/// The id is looked up by object identity (<see cref="Dictionary{TKey,TValue}"/> keyed on the paragraph
/// reference), NEVER by <c>map[index]</c> — so drift, which requires positional correspondence, is
/// structurally impossible.
/// </para>
/// <para>
/// <b>Reader alignment.</b> The <see cref="ParaIdMap"/> is produced in <c>body.Descendants&lt;Paragraph&gt;()</c>
/// document order — the SAME order <see cref="DocxAnnotationReader"/> uses for its <c>ParagraphHint</c> — so
/// <see cref="ComposeService"/>'s imported-revision/comment paraId resolution keeps working unchanged.
/// </para>
/// <para>
/// <b>Revision flattening (F-02, normative — independent of mammoth).</b> Native tracked changes are emitted
/// as settled prose with all text present and wrappers stripped: <c>w:ins</c> runs → plain text; <c>w:del</c>
/// runs (<c>w:delText</c>) → plain text (present, so the client deletion overlay can anchor); a
/// paragraph-mark-deleted <c>&lt;w:p&gt;</c> is still emitted with its <c>data-paraid</c> (empty content) so
/// the count/id sequence never breaks.
/// </para>
/// <para>
/// <b>Fail-closed (F-04 / GPT §11).</b> A malformed/unreadable source returns <see cref="ComposeProjectionStatus.Failed"/>
/// with an empty HTML and <c>CanEdit=false</c> — the client must not mount a blank editable doc over a
/// non-empty baseline. This never throws (Load still returns the source bytes).
/// </para>
/// <para>
/// <b>Opaque atoms (FR-02, task 012).</b> Non-renderable constructs — SDT/content controls with a genuinely
/// non-text declared type (<c>IsSpecialSdtControl</c>), Word fields (<c>w:fldSimple</c>, or a
/// <c>w:fldChar</c> begin/instrText/separate/end run sequence), and complex/floating objects (<c>w:drawing</c>,
/// <c>w:object</c>) — are rendered as non-editable ATOM placeholders instead of being opened/reinterpreted.
/// Inline atoms (fields, inline content controls, complex objects — all nested inside a paragraph's runs)
/// carry their containing paragraph's <c>w14:paraId</c> and are signaled via <see cref="RunBoundary.AtomKind"/>
/// in the offset-addressing table. Whole-construct atoms (a block-level SDT whose content is not ordinary
/// editable prose) get their own minted id, tracked in <see cref="ComposeDocxProjection.BlockAtoms"/> — NOT
/// in <see cref="ComposeDocxProjection.ParaIdMap"/>, so the F-01 single-walk invariant ("every data-paraid
/// has a ParaIdMap entry") is untouched. Because an atom's inner OOXML is never opened, its subtree is
/// byte-identical after a no-op save by construction (I-4) — the escalation-boundary decision for the
/// SDT-wraps-editable-paragraphs case is documented on <c>IsSpecialSdtControl</c>.
/// </para>
/// <para>
/// <b>Zero package delta (NFR-01).</b> <c>DocumentFormat.OpenXml</c> is already referenced; no SkiaSharp, no
/// OpenXmlPowerTools. <b>Pure</b> — bytes in / record out; no I/O, no Graph, no AI types (Tier-1, mirrors
/// <see cref="ParaIdPreParser"/>). <b>Privacy</b>: produces Tier-3 content — never logged; warnings carry
/// codes/counts only. Thread-safe stateless singleton (ADR-010).
/// </para>
/// </remarks>
public sealed class ComposeDocxProjectionBuilder
{
    // ST_LongHexNumber: 0 < x < 0x80000000, 8-hex uppercase — mirrors ParaIdPreParser / ComposeDocumentRenderer.
    private const uint MaxParaId = 0x80000000u;
    private const int MintRetryLimit = 1000;

    // Resource caps (GPT §13, scoped to an OBO-fetched tenant document — sane guards, not anonymous-upload hardening).
    private const int MaxParagraphs = 100_000;
    private const int MaxOutputChars = 16_000_000;

    // Heading style ids Heading1..Heading6 (mirrors ComposeDocumentRenderer's MaxHeadingLevel=6).
    private const int MaxHeadingLevel = 6;

    private readonly Func<uint> _mint;

    /// <summary>Production constructor — mints ids from a cryptographic RNG.</summary>
    public ComposeDocxProjectionBuilder() : this(DefaultMint) { }

    /// <summary>Test seam: inject a deterministic id generator (forced-collision fixtures). Internal via InternalsVisibleTo.</summary>
    internal ComposeDocxProjectionBuilder(Func<uint> mint) => _mint = mint;

    private static uint DefaultMint() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    /// <summary>
    /// Projects <paramref name="docx"/> to a paraId-tagged HTML editor representation + ordered paraId map.
    /// Never throws — an unreadable source degrades to <see cref="ComposeProjectionStatus.Failed"/>.
    /// </summary>
    public ComposeDocxProjection Build(ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default)
    {
        if (docx.IsEmpty)
        {
            return ComposeDocxProjection.Failed("empty-source");
        }

        WordprocessingDocument doc;
        MemoryStream buffer;
        try
        {
            buffer = new MemoryStream(docx.Length);
            buffer.Write(docx.Span);
            buffer.Position = 0;
            doc = WordprocessingDocument.Open(buffer, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            return ComposeDocxProjection.Failed("unreadable-source");
        }

        try
        {
            using (doc)
            using (buffer)
            {
                var mainPart = doc.MainDocumentPart;
                var body = mainPart?.Document?.Body;
                if (mainPart is null || body is null)
                {
                    // A body-less package is a legitimately empty document — editable-empty, not a failure.
                    return new ComposeDocxProjection { Status = ComposeProjectionStatus.Success, CanEdit = true, Html = string.Empty };
                }

                // Pass 1 (identity): assign every body paragraph a w14:paraId in Descendants order (reader-aligned),
                // keyed by INSTANCE so the render pass looks it up by identity, never by ordinal index.
                var paragraphs = body.Descendants<Paragraph>().ToList();
                if (paragraphs.Count > MaxParagraphs)
                {
                    return ComposeDocxProjection.Failed("resource-limit-paragraphs");
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in paragraphs)
                {
                    var id = p.ParagraphId?.Value;
                    if (!string.IsNullOrEmpty(id)) seen.Add(id!);
                }

                var map = new List<ParaIdMapEntry>(paragraphs.Count);
                var offsetTable = new List<ParaOffsetMap>(paragraphs.Count);
                var idByParagraph = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < paragraphs.Count; i++)
                {
                    var existing = paragraphs[i].ParagraphId?.Value;
                    string id;
                    bool minted;
                    if (!string.IsNullOrEmpty(existing))
                    {
                        id = existing!.ToUpperInvariant();
                        minted = false;
                    }
                    else
                    {
                        id = MintUnique(seen);
                        seen.Add(id);
                        minted = true;
                    }
                    map.Add(new ParaIdMapEntry(i, id, minted));
                    idByParagraph[paragraphs[i]] = id;

                    // FR-01 (task 011): emit this paragraph's intra-paragraph offset-addressing entry in the
                    // SAME pass and SAME Descendants<Paragraph>() order, so the table stays index-aligned with
                    // ParaIdMap by construction (cannot drift). The run flatten mirrors the render walk below.
                    offsetTable.Add(BuildParaOffsetMap(paragraphs[i], id));
                }

                // Pass 2 (render): ONE structural tree walk emits HTML, pulling each paragraph's id by instance.
                // FR-02 (task 012): whole-construct atoms mint from the SAME collision-checked `seen` pool
                // paragraph ids use (format-consistent, never colliding) — ctx gets that pool + the mint
                // delegate so it can allocate atom ids lazily, in document order, during this single pass.
                var ctx = new BuildContext(mainPart, idByParagraph, cancellationToken, seen, MintUnique);
                RenderBlockChildren(body, ctx);
                ctx.CloseOpenList();

                // Runtime alignment guard (F-03): emitted blocks vs. id map. A shortfall means some enumerated
                // paragraph (e.g. text-box / unsupported container) was not rendered — degrade to Partial + warn,
                // never silently. Counts only (privacy).
                if (ctx.EmittedParagraphCount != map.Count)
                {
                    ctx.AddWarning("unrendered-paragraphs", Math.Abs(map.Count - ctx.EmittedParagraphCount));
                }

                var warnings = ctx.Warnings;
                var status = warnings.Count == 0 ? ComposeProjectionStatus.Success : ComposeProjectionStatus.Partial;

                return new ComposeDocxProjection
                {
                    Status = status,
                    CanEdit = true, // Partial is still editable (save is paraId-keyed delta onto the retained original).
                    Html = ctx.Html,
                    ParaIdMap = map,
                    OffsetAddressingTable = offsetTable,
                    BlockAtoms = ctx.BlockAtoms,
                    Warnings = warnings,
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Defensive: any unexpected projection error fails closed rather than throwing out of Load.
            return ComposeDocxProjection.Failed("projection-error");
        }
    }

    // ── structural walk ────────────────────────────────────────────────────────────────────────────

    private void RenderBlockChildren(OpenXmlElement container, BuildContext ctx)
    {
        foreach (var child in container.Elements())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (child)
            {
                case Paragraph p:
                    RenderParagraph(p, ctx);
                    break;
                case Table t:
                    ctx.CloseOpenList();
                    RenderTable(t, ctx);
                    break;
                case SdtBlock sdt:
                    if (IsSpecialSdtControl(sdt.SdtProperties))
                    {
                        // FR-02 (task 012): a non-text declared type (date/dropdown/combo/picture/doc-part
                        // gallery/equation/citation/bibliography/group) is not ordinary editable prose — the
                        // WHOLE block becomes one opaque atom, never opened (I-4).
                        EmitBlockAtom(sdt, ctx);
                    }
                    else
                    {
                        // Plain/rich-text content control (or no declared type, the OOXML default): the
                        // SHELL stays transparent so wrapped paragraphs remain editable — the FR-02
                        // escalation-boundary decision (see IsSpecialSdtControl remarks). Treating every SDT
                        // as opaque would silently regress real, currently-editable prose with no corpus
                        // construct forcing that tradeoff.
                        ctx.AddWarning("content-control", 1);
                        var sdtContent = sdt.GetFirstChild<SdtContentBlock>();
                        if (sdtContent is not null) RenderBlockChildren(sdtContent, ctx);
                    }
                    break;
                default:
                    // sectPr, bookmarks, and other non-block markup: no editable block. (Paragraphs nested in
                    // text boxes/drawings are reached only via a Run, so they are intentionally not rendered as
                    // top-level blocks; the F-03 guard reports the count shortfall.)
                    break;
            }
        }
    }

    private void RenderParagraph(Paragraph p, BuildContext ctx)
    {
        if (!ctx.TryGetParaId(p, out var paraId))
        {
            // A paragraph not in the identity map is a nested (e.g. text-box) paragraph reached structurally —
            // do not emit it as a top-level block; the guard accounts for it.
            return;
        }

        var headingLevel = HeadingLevel(p, ctx);
        var listInfo = headingLevel is null ? ListInfo(p, ctx) : null;

        if (listInfo is not null)
        {
            ctx.EnsureList(listInfo.Ordered);
            if (listInfo.Level > 0) ctx.AddWarning("multi-level-numbering", 1);
            ctx.Append("<li><p");
            ctx.AppendParaIdAttr(paraId);
            AppendAlignment(p, ctx);
            ctx.Append(">");
            RenderInline(p, ctx);
            ctx.Append("</p></li>");
            ctx.EmittedParagraphCount++;
            return;
        }

        ctx.CloseOpenList();

        var tag = headingLevel is int lvl ? $"h{lvl}" : "p";
        ctx.Append($"<{tag}");
        ctx.AppendParaIdAttr(paraId);
        AppendAlignment(p, ctx);
        ctx.Append(">");
        RenderInline(p, ctx);
        ctx.Append($"</{tag}>");
        ctx.EmittedParagraphCount++;
    }

    private void RenderTable(Table table, BuildContext ctx)
    {
        ctx.Append("<table><tbody>");
        foreach (var row in table.Elements<TableRow>())
        {
            ctx.Append("<tr>");
            foreach (var cell in row.Elements<TableCell>())
            {
                ctx.Append("<td>");
                RenderBlockChildren(cell, ctx); // cell paragraphs continue the same id sequence
                ctx.CloseOpenList();            // a list must not leak past the cell boundary
                ctx.Append("</td>");
            }
            ctx.Append("</tr>");
        }
        ctx.Append("</tbody></table>");
    }

    // ── opaque block atom (FR-02, task 012) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Emits ONE non-editable placeholder for a whole <paramref name="sdt"/> classified by
    /// <see cref="IsSpecialSdtControl"/> as a genuinely non-text control. The atom's inner OOXML is NEVER
    /// read for display (maximally safe under I-4 — nothing about it is opened, not even for a preview) and
    /// it gets its OWN minted id (<see cref="BuildContext.MintAtomId"/>) exposed via <c>data-atomid</c> —
    /// deliberately NOT <c>data-paraid</c>, so <see cref="ComposeDocxProjection.ParaIdMap"/>'s "one entry
    /// per body paragraph" contract (and the F-01 single-walk invariant test) stay intact. Paragraphs nested
    /// inside <paramref name="sdt"/> still receive a real <c>w14:paraId</c> in Pass 1 (the identity walk
    /// enumerates ALL <c>Descendants&lt;Paragraph&gt;()</c>) but are never separately emitted here — the
    /// existing F-03 "unrendered-paragraphs" guard already accounts for that shortfall (same mechanism the
    /// pre-existing text-box case relies on).
    /// </summary>
    private void EmitBlockAtom(SdtBlock sdt, BuildContext ctx)
    {
        ctx.CloseOpenList();
        var atomId = ctx.MintAtomId();
        ctx.Append("<div class=\"compose-atom\" data-atom-kind=\"sdt\" data-atomid=\"");
        ctx.AppendEscapedAttr(atomId);
        ctx.Append("\" contenteditable=\"false\"></div>");
        ctx.AddBlockAtom(atomId, ComposeAtomKind.Sdt);
        ctx.AddWarning("opaque-atom-sdt", 1);
    }

    // ── offset-addressing table (FR-01, task 011) ───────────────────────────────────────────────────

    /// <summary>
    /// Builds one paragraph's intra-paragraph offset-addressing entry: the ordered editor-visible run
    /// flatten and each run's editor-offset boundary. The descent MIRRORS <see cref="RenderInline"/> exactly
    /// (into <c>w:hyperlink</c>, <c>w:ins</c>, <c>w:del</c>, <c>w:sdt</c>) so the run sequence and per-run
    /// character length match the editor-visible text the projection emits — the offset space the client
    /// measures over. Runs inside pre-existing tracked changes are included (their text is editor-visible)
    /// and tagged with their <see cref="RunTrackChange"/> context. Pure — reads run text lengths only, emits
    /// no document content. Static so it is trivially unit-testable in isolation.
    /// </summary>
    internal static ParaOffsetMap BuildParaOffsetMap(Paragraph paragraph, string paraId)
    {
        var runs = new List<RunBoundary>();
        var runIndex = 0;
        var cumOffset = 0;
        CollectRunBoundaries(paragraph, RunTrackChange.None, runs, ref runIndex, ref cumOffset);
        return new ParaOffsetMap { ParaId = paraId, Runs = runs };
    }

    private static void CollectRunBoundaries(
        OpenXmlElement container, RunTrackChange trackChange, List<RunBoundary> runs, ref int runIndex, ref int cumOffset)
    {
        // FR-02 (task 012): field-scan state is scoped to THIS container invocation only — a
        // w:fldChar begin/instrText/separate/result/end sequence is assumed to be direct siblings (see
        // FieldScanState remarks; not exercised by the corpus, documented simplification).
        var field = new FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            // The outermost w:fldChar end just closed — emit ONE atom spanning the whole
                            // field, length = its cached RESULT text (never its instrText field code).
                            var atomLen = ExtractRunsDisplayText(field.ResultRuns).Length;
                            runs.Add(new RunBoundary(runIndex, cumOffset, atomLen, trackChange, ComposeAtomKind.Field));
                            runIndex++;
                            cumOffset += atomLen;
                            field.Reset();
                        }
                        break; // consumed as part of the field span either way
                    }
                    if (IsComplexObjectRun(r))
                    {
                        // A drawing/embedded-object run occupies one non-editable atom position — never
                        // opened, so it never silently vanishes from the offset space (I-4).
                        runs.Add(new RunBoundary(runIndex, cumOffset, 1, trackChange, ComposeAtomKind.ComplexObject));
                        runIndex++;
                        cumOffset += 1;
                        break;
                    }
                    var normalLen = RunEditorLength(r);
                    runs.Add(new RunBoundary(runIndex, cumOffset, normalLen, trackChange));
                    runIndex++;
                    cumOffset += normalLen;
                    break;
                case SimpleField sf:
                    // w:fldSimple — its cached display value becomes the atom's content; the field's own
                    // run structure is never reinterpreted as separately editable runs.
                    var sfLen = ExtractAtomDisplayText(sf).Length;
                    runs.Add(new RunBoundary(runIndex, cumOffset, sfLen, trackChange, ComposeAtomKind.Field));
                    runIndex++;
                    cumOffset += sfLen;
                    break;
                case Hyperlink h:
                    CollectRunBoundaries(h, trackChange, runs, ref runIndex, ref cumOffset);
                    break;
                case InsertedRun ins:
                    CollectRunBoundaries(ins, RunTrackChange.Inserted, runs, ref runIndex, ref cumOffset);
                    break;
                case DeletedRun del:
                    CollectRunBoundaries(del, RunTrackChange.Deleted, runs, ref runIndex, ref cumOffset);
                    break;
                case SdtRun sdtRun:
                    if (IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        // An inline content control with a genuinely non-text declared type is an atom.
                        var sdtLen = ExtractAtomDisplayText(sdtRun).Length;
                        runs.Add(new RunBoundary(runIndex, cumOffset, sdtLen, trackChange, ComposeAtomKind.Sdt));
                        runIndex++;
                        cumOffset += sdtLen;
                    }
                    else
                    {
                        var sdtContent = sdtRun.GetFirstChild<SdtContentRun>();
                        if (sdtContent is not null) CollectRunBoundaries(sdtContent, trackChange, runs, ref runIndex, ref cumOffset);
                    }
                    break;
                default:
                    // ParagraphProperties, bookmarks, proofErr, etc. — no editor-visible run.
                    break;
            }
        }
    }

    // ── opaque atom detection helpers (FR-02, task 012) ─────────────────────────────────────────────

    private enum FieldPhase { None, Code, Result }

    /// <summary>
    /// Per-container scan state for a <c>w:fldChar</c> begin/instrText/separate/result/end run sequence.
    /// <see cref="Depth"/> tracks NESTING (e.g. <c>{ IF {PAGE} = 1 ... }</c>) so only the OUTERMOST
    /// <c>end</c> closes the atom; an inner field's own separate/result folds into the outer atom's display
    /// text rather than being modeled precisely (documented simplification — not exercised by the corpus).
    /// Each of <see cref="CollectRunBoundaries"/> and <see cref="RenderInline"/> keeps its OWN instance, so
    /// the two mirrored walks stay independent but deterministic over the same input (this file's existing
    /// parallel-walk pattern — see <see cref="RunEditorLength"/> vs <see cref="RenderRun"/>).
    /// </summary>
    private sealed class FieldScanState
    {
        public int Depth;
        public FieldPhase Phase = FieldPhase.None;
        public readonly List<Run> ResultRuns = new();

        public void Reset()
        {
            Depth = 0;
            Phase = FieldPhase.None;
            ResultRuns.Clear();
        }
    }

    /// <summary>
    /// Advances <paramref name="field"/> for one candidate <paramref name="run"/>. Returns <c>true</c> when
    /// <paramref name="run"/> was consumed as part of a field (control markup, a swallowed field-code run,
    /// or an accumulated result run); <paramref name="closed"/> distinguishes "the outermost end just
    /// closed — emit the atom now" from "still mid-field, nothing to emit yet". A run NOT participating in
    /// a field returns <c>false</c> and MUST be handled as ordinary content by the caller. Never throws — a
    /// stray separate/end with no matching begin fails open (returns <c>false</c>, treated as ordinary
    /// empty content) rather than corrupting the scan state (F-04 fail-closed philosophy).
    /// </summary>
    private static bool TryAdvanceFieldScan(Run run, FieldScanState field, out bool closed)
    {
        closed = false;
        var fldChar = run.GetFirstChild<FieldChar>();
        if (fldChar is not null)
        {
            var type = fldChar.FieldCharType?.Value;
            if (type == FieldCharValues.Begin)
            {
                if (field.Depth == 0)
                {
                    field.Phase = FieldPhase.Code;
                    field.ResultRuns.Clear();
                }
                field.Depth++;
                return true;
            }
            if (type == FieldCharValues.Separate)
            {
                if (field.Depth == 0) return false; // stray separate outside any field — fail open
                if (field.Phase == FieldPhase.Code) field.Phase = FieldPhase.Result;
                return true;
            }
            if (type == FieldCharValues.End)
            {
                if (field.Depth == 0) return false; // stray end outside any field — fail open
                field.Depth--;
                if (field.Depth == 0) closed = true;
                return true;
            }
            return false; // unrecognized/absent FieldCharType — treat as ordinary (empty) content
        }

        if (field.Depth > 0)
        {
            if (field.Phase == FieldPhase.Result)
            {
                field.ResultRuns.Add(run); // cached result content — becomes the atom's display text
            }
            // Phase == Code: field-code (w:instrText) runs are never editor-visible — swallowed.
            return true;
        }

        return false;
    }

    /// <summary>A run carrying a complex/floating object (image/shape drawing or an OLE embed) — VML
    /// <c>w:pict</c> is out of scope for Phase 1 (not present in the corpus; a candidate for future
    /// coverage, noted rather than silently unhandled).</summary>
    private static bool IsComplexObjectRun(Run run) =>
        run.GetFirstChild<Drawing>() is not null || run.GetFirstChild<EmbeddedObject>() is not null;

    /// <summary>
    /// The editor-visible display text an opaque atom shows — the SAME text/glyph counting convention as
    /// <see cref="RunEditorLength"/> (so an atom's offset-table length always equals the exact character
    /// count the HTML render emits inside its placeholder — the two mirrored walks stay consistent by
    /// construction, not by sharing state). Never returns document formatting, only text content.
    /// </summary>
    private static string ExtractRunsDisplayText(IEnumerable<Run> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs)
        {
            foreach (var child in run.Elements())
            {
                switch (child)
                {
                    case Text t: sb.Append(t.Text); break;
                    case DeletedText dt: sb.Append(dt.Text); break;
                    case Break or TabChar or NoBreakHyphen: sb.Append(' '); break;
                    default: break;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Convenience overload of <see cref="ExtractRunsDisplayText(IEnumerable{Run})"/> over every
    /// <see cref="Run"/> descendant of <paramref name="container"/> (a <see cref="SimpleField"/> or a
    /// special <see cref="SdtRun"/>) — used for BOTH the atom's offset-table length and its HTML display
    /// text, so the two can never diverge.</summary>
    private static string ExtractAtomDisplayText(OpenXmlElement container) =>
        ExtractRunsDisplayText(container.Descendants<Run>());

    /// <summary>
    /// FR-02 (task 012) escalation-boundary decision: an SDT/content-control becomes a whole-construct
    /// opaque atom ONLY when its declared type is genuinely non-text (date, dropdown list, combo box,
    /// picture, doc-part gallery, equation, citation, bibliography, group) — content that cannot be
    /// faithfully shown as editable prose. A plain-text or rich-text control (or one with no declared type
    /// at all, the OOXML default) wraps ordinary editable paragraphs, so its shell stays TRANSPARENT (see
    /// <see cref="RenderBlockChildren"/>'s <c>SdtBlock</c> case and <see cref="CollectRunBoundaries"/>'s
    /// <c>SdtRun</c> case) — treating every SDT as opaque would silently regress real, currently-editable
    /// content, and no corpus construct forces that tradeoff (the escalation trigger this task's POML names
    /// is resolved by this structural rule rather than applied silently — see task notes for the writeup).
    /// </summary>
    private static bool IsSpecialSdtControl(SdtProperties? props)
    {
        if (props is null) return false;
        return props.GetFirstChild<SdtContentDate>() is not null
            || props.GetFirstChild<SdtContentDropDownList>() is not null
            || props.GetFirstChild<SdtContentComboBox>() is not null
            || props.GetFirstChild<SdtContentPicture>() is not null
            || props.GetFirstChild<SdtContentDocPartObject>() is not null
            || props.GetFirstChild<SdtContentDocPartList>() is not null
            || props.GetFirstChild<SdtContentEquation>() is not null
            || props.GetFirstChild<SdtContentCitation>() is not null
            || props.GetFirstChild<SdtContentBibliography>() is not null
            || props.GetFirstChild<SdtContentGroup>() is not null;
    }

    /// <summary>
    /// The number of editor-visible characters a run contributes to the paragraph offset space: its
    /// <c>w:t</c>/<c>w:delText</c> text length, plus one per <c>w:br</c>/<c>w:tab</c>/<c>w:noBreakHyphen</c>
    /// glyph — mirroring exactly what <see cref="RenderRun"/> emits (each maps to one editor position).
    /// </summary>
    private static int RunEditorLength(Run run)
    {
        var length = 0;
        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t:
                    length += t.Text?.Length ?? 0;
                    break;
                case DeletedText dt:
                    length += dt.Text?.Length ?? 0;
                    break;
                case Break:
                case TabChar:
                case NoBreakHyphen:
                    length += 1;
                    break;
                default:
                    break;
            }
        }

        return length;
    }

    // ── inline (runs / marks / hyperlinks / revision flattening) ─────────────────────────────────────

    private void RenderInline(OpenXmlElement container, BuildContext ctx)
    {
        // FR-02 (task 012): mirrors CollectRunBoundaries' field-scan exactly (its own FieldScanState
        // instance — see that method's remarks) so the HTML atom span's text always matches the
        // offset-table's atom length.
        var field = new FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            ctx.AppendAtom(ComposeAtomKind.Field, ExtractRunsDisplayText(field.ResultRuns));
                            field.Reset();
                        }
                        break;
                    }
                    if (IsComplexObjectRun(r))
                    {
                        ctx.AppendAtom(ComposeAtomKind.ComplexObject, null);
                        break;
                    }
                    RenderRun(r, ctx);
                    break;
                case SimpleField sf:
                    ctx.AppendAtom(ComposeAtomKind.Field, ExtractAtomDisplayText(sf));
                    break;
                case Hyperlink h:
                    RenderHyperlink(h, ctx);
                    break;
                case InsertedRun ins:
                    RenderInline(ins, ctx); // F-02: emit inserted text, wrapper stripped
                    break;
                case DeletedRun del:
                    RenderInline(del, ctx); // F-02: emit deleted text (present) so the overlay can anchor
                    break;
                case SdtRun sdtRun:
                    if (IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        ctx.AppendAtom(ComposeAtomKind.Sdt, ExtractAtomDisplayText(sdtRun));
                    }
                    else
                    {
                        var sdtContent = sdtRun.GetFirstChild<SdtContentRun>();
                        if (sdtContent is not null) RenderInline(sdtContent, ctx);
                    }
                    break;
                default:
                    // ParagraphProperties, bookmarks, proofErr, etc. — no inline text.
                    break;
            }
        }
    }

    private void RenderRun(Run run, BuildContext ctx)
    {
        var rPr = run.RunProperties;
        var bold = IsOn(rPr?.Bold);
        var italic = IsOn(rPr?.Italic);
        var underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None;
        var strike = IsOn(rPr?.Strike);

        if (bold) ctx.Append("<strong>");
        if (italic) ctx.Append("<em>");
        if (underline) ctx.Append("<u>");
        if (strike) ctx.Append("<s>");

        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t:
                    ctx.AppendEscaped(t.Text);
                    break;
                case DeletedText dt:
                    ctx.AppendEscaped(dt.Text); // F-02: deleted text present as plain text
                    break;
                case TabChar:
                    // Non-collapsing tab representation (GPT §9.1) — never a bare "\t".
                    ctx.Append("<span class=\"compose-tab\"> </span>");
                    break;
                case Break:
                    ctx.Append("<br>");
                    break;
                case NoBreakHyphen:
                    ctx.Append("‑");
                    break;
                default:
                    break;
            }
        }

        if (strike) ctx.Append("</s>");
        if (underline) ctx.Append("</u>");
        if (italic) ctx.Append("</em>");
        if (bold) ctx.Append("</strong>");
    }

    private void RenderHyperlink(Hyperlink h, BuildContext ctx)
    {
        var href = ResolveHyperlinkHref(h, ctx);
        if (href is null)
        {
            RenderInline(h, ctx); // unsafe/unknown target → emit the text without a link
            return;
        }
        ctx.Append("<a href=\"");
        ctx.AppendEscapedAttr(href);
        ctx.Append("\">");
        RenderInline(h, ctx);
        ctx.Append("</a>");
    }

    private static string? ResolveHyperlinkHref(Hyperlink h, BuildContext ctx)
    {
        // Internal anchor (bookmark) — safe, no relationship needed.
        if (!string.IsNullOrEmpty(h.Anchor?.Value))
        {
            return "#" + h.Anchor!.Value;
        }
        var rid = h.Id?.Value;
        if (string.IsNullOrEmpty(rid)) return null;
        try
        {
            var rel = ctx.MainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == rid);
            var uri = rel?.Uri?.ToString();
            if (string.IsNullOrEmpty(uri)) return null;
            // Protocol allowlist (GPT §13): http/https/mailto only. Never resolve/fetch external relationships.
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
            return null; // javascript:/data:/file:/custom → neutralized
        }
        catch
        {
            return null;
        }
    }

    // ── classification helpers ───────────────────────────────────────────────────────────────────────

    private static int? HeadingLevel(Paragraph p, BuildContext ctx)
    {
        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(styleId)) return null;
        // Heading1..Heading6 (and "Heading 1" tolerant) → h1..h6.
        var digits = new string(styleId!.Where(char.IsDigit).ToArray());
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl)
            && lvl is >= 1 and <= MaxHeadingLevel)
        {
            return lvl;
        }
        return null;
    }

    private sealed record ListItemInfo(bool Ordered, int Level);

    private static ListItemInfo? ListInfo(Paragraph p, BuildContext ctx)
    {
        // A list item carries a DIRECT paragraph w:numPr. Style-linked heading numbering (numPr on the STYLE)
        // is intentionally NOT treated as a list — it is a heading (mirrors ComposeDocumentRenderer's model).
        var numPr = p.ParagraphProperties?.NumberingProperties;
        var numId = numPr?.NumberingId?.Val;
        if (numId is null) return null;
        var ilvl = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
        var ordered = ResolveOrdered(numId.Value, ilvl, ctx);
        return new ListItemInfo(ordered, ilvl);
    }

    private static bool ResolveOrdered(int numId, int ilvl, BuildContext ctx)
    {
        try
        {
            var numbering = ctx.MainPart.NumberingDefinitionsPart?.Numbering;
            if (numbering is null) { ctx.AddWarning("numbering-unresolved", 1); return true; }

            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractNumId = instance?.AbstractNumId?.Val?.Value;
            if (abstractNumId is null) { ctx.AddWarning("numbering-unresolved", 1); return true; }

            var abstractNum = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
            var level = abstractNum?.Elements<Level>().FirstOrDefault(l => (l.LevelIndex?.Value ?? 0) == ilvl)
                        ?? abstractNum?.Elements<Level>().FirstOrDefault();
            var fmt = level?.NumberingFormat?.Val?.Value;
            // Bullet → unordered; anything else (decimal, lowerLetter, lowerRoman, …) → ordered.
            return fmt != NumberFormatValues.Bullet;
        }
        catch
        {
            ctx.AddWarning("numbering-unresolved", 1);
            return true;
        }
    }

    private static void AppendAlignment(Paragraph p, BuildContext ctx)
    {
        var just = p.ParagraphProperties?.Justification?.Val?.Value;
        if (just is null) return;
        string? css = null;
        if (just.Value == JustificationValues.Center) css = "center";
        else if (just.Value == JustificationValues.Right) css = "right";
        else if (just.Value == JustificationValues.Both) css = "justify";
        if (css is not null)
        {
            ctx.Append(" style=\"text-align:");
            ctx.Append(css);
            ctx.Append("\"");
        }
    }

    private static bool IsOn(OnOffType? toggle)
    {
        if (toggle is null) return false;
        // OnOffType.Val null ⇒ present-means-on; explicit false ⇒ off.
        return toggle.Val is null || toggle.Val.Value;
    }

    private string MintUnique(HashSet<string> seen)
    {
        for (var attempt = 0; attempt < MintRetryLimit; attempt++)
        {
            var candidate = _mint();
            if (candidate == 0 || candidate >= MaxParaId) continue;
            var hex = candidate.ToString("X8");
            if (!seen.Contains(hex)) return hex;
        }
        throw new InvalidOperationException($"Unable to mint a unique w14:paraId after {MintRetryLimit} attempts.");
    }

    // ── build context (per-call render state) ────────────────────────────────────────────────────────

    private sealed class BuildContext
    {
        private readonly StringBuilder _sb = new(4096);
        private readonly Dictionary<Paragraph, string> _idByParagraph;
        private readonly Dictionary<string, int> _warnings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenIds;
        private readonly Func<HashSet<string>, string> _mintUnique;
        private readonly List<ComposeBlockAtom> _blockAtoms = new();
        private bool _listOpen;
        private bool _listOrdered;

        public BuildContext(
            MainDocumentPart mainPart, Dictionary<Paragraph, string> idByParagraph, CancellationToken ct,
            HashSet<string> seenIds, Func<HashSet<string>, string> mintUnique)
        {
            MainPart = mainPart;
            _idByParagraph = idByParagraph;
            CancellationToken = ct;
            _seenIds = seenIds;
            _mintUnique = mintUnique;
        }

        public MainDocumentPart MainPart { get; }
        public CancellationToken CancellationToken { get; }
        public int EmittedParagraphCount { get; set; }
        public string Html => _sb.ToString();
        public IReadOnlyList<ComposeBlockAtom> BlockAtoms => _blockAtoms;

        public IReadOnlyList<ComposeProjectionWarning> Warnings =>
            _warnings.Select(kv => new ComposeProjectionWarning(kv.Key, kv.Value)).ToList();

        public bool TryGetParaId(Paragraph p, out string paraId) => _idByParagraph.TryGetValue(p, out paraId!);

        public void AddWarning(string code, int count)
        {
            _warnings.TryGetValue(code, out var existing);
            _warnings[code] = existing + count;
        }

        public void Append(string s)
        {
            if (_sb.Length + s.Length > MaxOutputChars)
            {
                throw new InvalidOperationException("Compose projection exceeded the maximum output size.");
            }
            _sb.Append(s);
        }

        public void AppendParaIdAttr(string paraId)
        {
            Append(" data-paraid=\"");
            AppendEscapedAttr(paraId);
            Append("\"");
        }

        /// <summary>FR-02 (task 012): mints a whole-construct atom identity from the SAME collision-checked,
        /// 8-hex pool paragraph <c>w14:paraId</c>s use (format-consistent, never colliding with one) — but
        /// tracked separately (see <see cref="ComposeDocxProjection.BlockAtoms"/> remarks), never added to
        /// the paragraph identity map.</summary>
        public string MintAtomId()
        {
            var id = _mintUnique(_seenIds);
            _seenIds.Add(id);
            return id;
        }

        public void AddBlockAtom(string atomId, ComposeAtomKind kind) => _blockAtoms.Add(new ComposeBlockAtom(atomId, kind));

        /// <summary>
        /// Emits a non-editable INLINE atom placeholder — the run-level counterpart to
        /// <see cref="ComposeDocxProjectionBuilder.EmitBlockAtom"/>. Carries no separate identity of its own
        /// (it sits inside the paragraph's own <c>data-paraid</c> block); <see cref="RunBoundary.AtomKind"/>
        /// in the offset-addressing table is the matching signal that no intra-atom operation may target it.
        /// </summary>
        public void AppendAtom(ComposeAtomKind kind, string? displayText)
        {
            Append("<span class=\"compose-atom\" data-atom-kind=\"");
            Append(AtomKindToken(kind));
            Append("\" contenteditable=\"false\">");
            if (!string.IsNullOrEmpty(displayText)) AppendEscaped(displayText);
            Append("</span>");
        }

        private static string AtomKindToken(ComposeAtomKind kind) => kind switch
        {
            ComposeAtomKind.Sdt => "sdt",
            ComposeAtomKind.Field => "field",
            ComposeAtomKind.ComplexObject => "object",
            _ => "unknown",
        };

        public void AppendEscaped(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&': Append("&amp;"); break;
                    case '<': Append("&lt;"); break;
                    case '>': Append("&gt;"); break;
                    default: _sb.Append(ch); break;
                }
            }
        }

        public void AppendEscapedAttr(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&': Append("&amp;"); break;
                    case '<': Append("&lt;"); break;
                    case '>': Append("&gt;"); break;
                    case '"': Append("&quot;"); break;
                    default: _sb.Append(ch); break;
                }
            }
        }

        public void EnsureList(bool ordered)
        {
            if (_listOpen && _listOrdered != ordered) CloseOpenList();
            if (!_listOpen)
            {
                Append(ordered ? "<ol>" : "<ul>");
                _listOpen = true;
                _listOrdered = ordered;
            }
        }

        public void CloseOpenList()
        {
            if (!_listOpen) return;
            Append(_listOrdered ? "</ol>" : "</ul>");
            _listOpen = false;
        }
    }
}
