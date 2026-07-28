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

                // Task 030 (WS-3, FR-11/FR-12): parse the numbering MODEL once — a side-part read of
                // numbering.xml/styles.xml, NOT a second body walk. Per-paragraph resolution is folded
                // into the Pass-1 loop below (already visits every paragraph once) so 031's future
                // computation has a ready per-paragraph lookup with no extra pass.
                var numberingModel = BuildNumberingModel(mainPart);
                var numberingByParagraph = new Dictionary<Paragraph, ParagraphNumberingRef>(ReferenceEqualityComparer.Instance);
                // Task 032 (WS-3, FR-13): the 031-computed label, keyed by paragraph INSTANCE so Pass 2
                // (render) can attach it to the emitted `<p>`/`<h#>` tag as a DATA ATTRIBUTE (never as text
                // content — the text-exactness harness compares source-run text to projected text, and the
                // computed label is not source text). Populated in the SAME Pass-1 loop below; no second walk.
                var computedNumberByParagraph = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
                var numberingDiagnostics = new List<string>();

                // Task 031 (WS-3, FR-11..FR-14): the deterministic numbering COMPUTATION engine — replays
                // Word's per-(abstractNumId, level) counter algorithm over the 030 model IN THIS SAME
                // document-order Pass-1 walk. Because counters are carried forward across the whole pass, an
                // interrupted numbered run (heading/body/table between clauses) does NOT reset to 1 — the
                // core defect this fixes. Read-time only (no auto-renumber on edit; that is R5 G3 / FR-14).
                var numberingEngine = new NumberingComputationEngine(numberingModel);

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
                    idByParagraph[paragraphs[i]] = id;

                    // FR-01 (task 011): emit this paragraph's intra-paragraph offset-addressing entry in the
                    // SAME pass and SAME Descendants<Paragraph>() order, so the table stays index-aligned with
                    // ParaIdMap by construction (cannot drift). The run flatten mirrors the render walk below.
                    offsetTable.Add(BuildParaOffsetMap(paragraphs[i], id));

                    // Task 030 (FR-11/FR-12): resolve THIS paragraph's numbering in the SAME Pass-1 pass —
                    // direct w:numPr or style-linked via pStyle. Only escalate (warn) for constructs
                    // genuinely OUTSIDE the model (an actually-used numStyleLink chain or picture bullet) —
                    // never for numbering.xml cruft a paragraph never references (common in Word-authored
                    // docs and not itself a fidelity defect).
                    string? computedNumber = null;
                    var numberingRef = ResolveParagraphNumbering(paragraphs[i], numberingModel);
                    if (numberingRef is not null)
                    {
                        numberingByParagraph[paragraphs[i]] = numberingRef;

                        // Task 031: advance the counters (in doc order) and compute the displayed label.
                        // MUST run for EVERY numbered paragraph in order — including style-linked headings
                        // ListInfo excludes from list treatment — so the single per-(abstractNumId, level)
                        // counter is exact (FR-11/FR-12). The engine mutates its counters here; the returned
                        // label is attached to this paragraph's ParaIdMap entry for 032 render + WS-4 (FR-13).
                        computedNumber = numberingEngine.Compute(numberingRef);
                        if (computedNumber is not null)
                        {
                            computedNumberByParagraph[paragraphs[i]] = computedNumber;
                        }

                        if (numberingModel.AbstractNumIdByNumId.TryGetValue(numberingRef.NumId, out var absId))
                        {
                            if (numberingModel.UnresolvedNumStyleLinkAbstractNumIds.Contains(absId))
                            {
                                numberingDiagnostics.Add("numstylelink-unresolved");
                            }
                            else if (numberingModel.ResolveLevel(numberingRef.NumId, numberingRef.Ilvl)?.HasPictureBullet == true)
                            {
                                numberingDiagnostics.Add("picture-bullet-unresolved");
                            }
                        }
                    }

                    map.Add(new ParaIdMapEntry(i, id, minted, computedNumber));
                }

                // Pass 2 (render): ONE structural tree walk emits HTML, pulling each paragraph's id by instance.
                // FR-02 (task 012): whole-construct atoms mint from the SAME collision-checked `seen` pool
                // paragraph ids use (format-consistent, never colliding) — ctx gets that pool + the mint
                // delegate so it can allocate atom ids lazily, in document order, during this single pass.
                var ctx = new BuildContext(mainPart, idByParagraph, cancellationToken, seen, MintUnique, numberingModel, numberingByParagraph, computedNumberByParagraph);
                foreach (var diag in numberingDiagnostics) ctx.AddWarning(diag, 1);
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
            ctx.AppendNumberingAttrs(p);
            AppendParagraphStyle(p, ctx);
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
        ctx.AppendNumberingAttrs(p);
        AppendParagraphStyle(p, ctx);
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

    /// <summary>A run carrying a complex/floating object — DrawingML (<c>w:drawing</c>, image/shape), an
    /// OLE embed (<c>w:object</c>), or a legacy VML fallback picture (<c>w:pict</c>, task 022 WS-2
    /// construct audit: previously fell through <see cref="RenderRun"/>'s default case with zero HTML,
    /// zero offset-table length, and no warning — a genuine silent drop per F-1; now the same non-editable
    /// atom placeholder as <c>w:drawing</c>/<c>w:object</c>, since it is equally an opaque image/shape
    /// construct, never opened for display (I-4)).</summary>
    private static bool IsComplexObjectRun(Run run) =>
        run.GetFirstChild<Drawing>() is not null
        || run.GetFirstChild<EmbeddedObject>() is not null
        || run.GetFirstChild<Picture>() is not null;

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
                    case Break or TabChar or NoBreakHyphen or CarriageReturn or PositionalTab: sb.Append(' '); break;
                    case Ruby ruby:
                        // Task 022 WS-2 construct audit: mirrors RenderRun's Ruby case — the base text is
                        // real editor-visible prose (not the phonetic guide), so this atom-display-text path
                        // must include it too, or a field/SDT wrapping a ruby run would silently drop it.
                        sb.Append(ExtractRunsDisplayText(RubyBaseRuns(ruby)));
                        break;
                    case SymbolChar sym:
                        // FR-06: represent (mapped glyph or placeholder) rather than drop — same resolver
                        // RenderRun uses. This atom-display-text path has no BuildContext to raise a
                        // warning through (documented limitation, same shape as this file's other
                        // "not exercised by the corpus" simplifications, e.g. FieldScanState remarks); an
                        // unmapped w:sym inside a field RESULT/SDT display text is not present in the
                        // corpus today.
                        sb.Append(ResolveSymbolGlyph(sym, out _));
                        break;
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
    /// <c>w:t</c>/<c>w:delText</c> text length, plus one per <c>w:br</c>/<c>w:cr</c>/<c>w:tab</c>/
    /// <c>w:noBreakHyphen</c>/<c>w:sym</c> glyph — mirroring exactly what <see cref="RenderRun"/> emits
    /// (each maps to one editor position). A <c>w:sym</c> contributes exactly 1 regardless of whether it
    /// resolves to a mapped Unicode glyph or an unmapped placeholder (FR-06/FR-10) — both are ONE
    /// editor-visible character, so the offset table never diverges from the HTML render either way.
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
                case CarriageReturn:
                case SymbolChar:
                case PositionalTab:
                    length += 1;
                    break;
                case Ruby ruby:
                    // Task 022 WS-2 construct audit: the base text RenderRun now emits — kept length-aligned
                    // with the offset-addressing table per this file's parallel-walk invariant.
                    length += ExtractRunsDisplayText(RubyBaseRuns(ruby)).Length;
                    break;
                default:
                    break;
            }
        }

        return length;
    }

    /// <summary>The <c>w:ruby</c> base-text runs (<c>w:rubyBase</c>'s direct <see cref="Run"/> children) —
    /// the real editor-visible prose. The phonetic guide (<c>w:rt</c>) is deliberately excluded: it is a
    /// supplementary pronunciation annotation, not the document's own text (task 022 WS-2 construct
    /// audit).</summary>
    private static IEnumerable<Run> RubyBaseRuns(Ruby ruby) =>
        ruby.GetFirstChild<RubyBase>()?.Elements<Run>() ?? Enumerable.Empty<Run>();

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
                case PositionalTab:
                    // Non-collapsing tab representation (GPT §9.1) — never a bare "\t".
                    ctx.Append("<span class=\"compose-tab\"> </span>");
                    break;
                case Break br:
                    // Task 022 WS-2 construct audit (design section 4: "w:br type=page - currently a line
                    // break"): a page/column break still renders as <br> (this editor has no
                    // page/pagination concept - F-5/WS-5 is a separate, deferred spike) but the semantic
                    // downgrade from "hard page/column break" to "soft line break" is now surfaced as a
                    // warning rather than silently absorbed into the default TextWrapping-break case.
                    if (br.Type?.Value == BreakValues.Page) ctx.AddWarning("page-break-rendered-as-line-break", 1);
                    else if (br.Type?.Value == BreakValues.Column) ctx.AddWarning("column-break-rendered-as-line-break", 1);
                    ctx.Append("<br>");
                    break;
                case CarriageReturn:
                    // FR-05: w:cr carries the same "line break, not a paragraph break" intent as w:br —
                    // mirror the existing Break handling exactly rather than dropping it (WS-2 fix; this
                    // was previously the FR-05 characterization gap pinned by
                    // ComposeDocxProjectionBuilderTests.Build_ParagraphWithCarriageReturnRun_..., now flipped).
                    ctx.Append("<br>");
                    break;
                case NoBreakHyphen:
                    ctx.Append("‑");
                    break;
                case SymbolChar sym:
                    // FR-06/FR-10: map the symbol-font code point to its Unicode equivalent where a
                    // VERIFIED mapping exists (ResolveSymbolGlyph / KnownSymbolGlyphMap); otherwise emit a
                    // visible placeholder (U+FFFD) AND raise the intra-run glyph-loss warning — F-1 never
                    // allows a silent drop. This was previously the FR-06 characterization gap pinned by
                    // ComposeDocxProjectionBuilderTests.Build_ParagraphWithSymbolCharRun_..., now flipped.
                    var glyph = ResolveSymbolGlyph(sym, out var mapped);
                    ctx.AppendEscaped(glyph);
                    if (!mapped) ctx.AddWarning("unmapped-symbol-char", 1);
                    break;
                case Ruby ruby:
                    // Task 022 WS-2 construct audit: w:ruby (East-Asian phonetic-guide annotation) wraps TWO
                    // text groups - w:rubyBase (the actual document prose) and w:rt (the phonetic guide).
                    // Previously fell through to default (silently dropped BOTH - a genuine F-1 violation,
                    // since rubyBase is real, verbatim, 100%-recoverable text, not a construct requiring
                    // interpretation/guessing like w:sym). The base text is now rendered verbatim (same
                    // AppendEscaped path as w:t); the phonetic guide is deliberately omitted (it is a
                    // supplementary pronunciation annotation, not the document's own words) and that
                    // omission is surfaced via a warning so the simplification stays auditable.
                    ctx.AppendEscaped(ExtractRunsDisplayText(RubyBaseRuns(ruby)));
                    ctx.AddWarning("ruby-phonetic-guide-dropped", 1);
                    break;
                case FootnoteReference:
                    // Task 022 WS-2 construct audit: the footnote reference mark carries no text of its own
                    // (Word computes its displayed number from position in word/footnotes.xml, a separate
                    // part this Phase-1 body-only projection does not open - the same architectural boundary
                    // as headers/footers). Fabricating a number here risks the exact "wrong glyph in a legal
                    // document" failure task 020's w:sym escalation reasoning warns against, so this is
                    // warn-only (never a silent drop) rather than a guessed placeholder.
                    ctx.AddWarning("unrepresented-footnote-reference", 1);
                    break;
                case EndnoteReference:
                    ctx.AddWarning("unrepresented-endnote-reference", 1);
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

    /// <summary>
    /// FR-06 symbol-font → Unicode mapping (verified, HAND-CURATED, deliberately small). Today covers
    /// only the corpus's confirmed case — Symbol-font PUA code point <c>F0A7</c> maps to § (U+00A7, the
    /// legal section mark; corpus-manifest.md row 12). Deliberately does NOT include an algorithmic
    /// "subtract 0xF000 and treat the remainder as a Latin-1/legacy-Symbol-charset code point" fallback:
    /// that heuristic holds for SOME Symbol-font glyphs but is unverified across the font, and a WRONG
    /// mapped glyph in a legal document is worse than an honest, warned placeholder (F-1 / this task's
    /// escalation trigger — a wrong § is a wrong legal document, same failure class as a missing one).
    /// Extend this table only with entries verified against a real corpus/owner document. Mirrors the
    /// identically-scoped map in <c>ComposeReadFidelityHarnessSeamTests.KnownSymbolGlyphMap</c> — keep
    /// the two in sync if either changes.
    /// </summary>
    private static readonly Dictionary<(string Font, string Char), string> KnownSymbolGlyphMap = new()
    {
        [("Symbol", "F0A7")] = "§",
    };

    /// <summary>
    /// Resolves a <c>w:sym</c> run child to its display glyph. <paramref name="mapped"/> is <c>true</c>
    /// when <see cref="KnownSymbolGlyphMap"/> has a verified entry for the run's <c>(font, char)</c> pair
    /// (the returned glyph IS the correct Unicode target); <c>false</c> means no verified mapping exists
    /// and the caller must treat the returned replacement character as a placeholder, never as content —
    /// FR-06/FR-10 require the caller to also raise a warning in that branch.
    /// </summary>
    private static string ResolveSymbolGlyph(SymbolChar sym, out bool mapped)
    {
        var font = sym.Font?.Value;
        var code = sym.Char?.Value;
        if (font is not null && code is not null && KnownSymbolGlyphMap.TryGetValue((font, code), out var glyph))
        {
            mapped = true;
            return glyph;
        }

        mapped = false;
        return "�"; // REPLACEMENT CHARACTER — visible placeholder, never a silent drop (FR-06).
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

    // ── numbering model (task 030, spaarkeai-compose-fidelity-r4.5 WS-3, FR-11/FR-12) ─────────────────
    // The read-side numbering MODEL — the closed input the WS-3 computation engine (task 031) replays
    // Word's algorithm over (design §4 WS-3 "Algorithm"). Parses `numbering.xml` (`w:num` →
    // `w:abstractNumId` → `w:abstractNum`, per-level `w:numFmt`/`w:lvlText`/`w:start`/`w:lvlRestart`/
    // `w:isLgl`/`w:lvlOverride`/`w:startOverride`) and resolves STYLE-LINKED numbering from `styles.xml`
    // (FR-12: a paragraph style — e.g. Heading2 — carrying the `w:numPr`, exercised by
    // heading-style-numbering.docx / corpus-manifest.md row 10). NO number is computed here — that is
    // task 031; this region only builds + exposes the model and resolves each paragraph's effective
    // `(numId, ilvl)`. Model construction is a side-part read (numbering.xml / styles.xml are NOT the
    // document body) and per-paragraph resolution is wired into the EXISTING Pass-1 identity-assignment
    // loop in <see cref="Build"/> (`:134-171` at time of writing) — so this adds NO second full-document
    // walk; the single document-order paragraph walk invariant (`:18-26`) is untouched.

    /// <summary>One level's authored numbering definition. Mirrors exactly the field set
    /// <c>ComposeDocumentRenderer</c> (the write-side mirror) authors via <c>StartNumberingValue</c>/
    /// <c>LevelText</c>/<c>NumberingFormat</c>/<c>ParagraphStyleIdInLevel</c> (`:570-640`), so task 033's
    /// read↔write round-trip test can compare like-for-like.</summary>
    internal sealed record NumberingLevelDef(
        int AbstractNumId,
        int Level,
        NumberFormatValues? NumFmt,
        string? LvlText,
        int? Start,
        int? LvlRestart,
        bool IsLgl,
        string? ParagraphStyleIdInLevel,
        bool HasPictureBullet);

    /// <summary>A `numId`-scoped level override (`w:lvlOverride`) — either a `w:startOverride` (restart
    /// THIS numId's counter at a value, without redefining the level — other numIds sharing the same
    /// abstractNum keep its own `w:start`) or a full replacement `w:lvl` (<see cref="FullLevelOverride"/>
    /// non-null), which supersedes the abstractNum's own level definition for this numId.</summary>
    internal sealed record NumberingLevelOverrideDef(int? StartOverride, NumberingLevelDef? FullLevelOverride);

    /// <summary>A paragraph's resolved numbering source — direct `w:numPr` (<see cref="StyleLinked"/> =
    /// false, mirrors <see cref="ListInfo"/>'s (numId,ilvl) extraction exactly so the two never disagree
    /// on the direct case) or style-linked via `pStyle` (FR-12, <see cref="StyleLinked"/> = true).
    /// Resolved for EVERY numbered paragraph — including style-linked headings <see cref="ListInfo"/>
    /// deliberately excludes from list treatment — because 031 needs both cases to replay Word's single
    /// per-(abstractNumId, level) counter across the whole document.</summary>
    internal sealed record ParagraphNumberingRef(int NumId, int Ilvl, bool StyleLinked, string? SourceStyleId);

    /// <summary>
    /// The read-side numbering MODEL (task 030) — the closed input task 031 walks. Built ONCE per
    /// document by <see cref="BuildNumberingModel"/>.
    /// </summary>
    internal sealed class NumberingModel
    {
        public required IReadOnlyDictionary<int, int> AbstractNumIdByNumId { get; init; }
        public required IReadOnlyDictionary<(int AbstractNumId, int Level), NumberingLevelDef> Levels { get; init; }
        public required IReadOnlyDictionary<(int NumId, int Level), NumberingLevelOverrideDef> Overrides { get; init; }
        public required IReadOnlyDictionary<string, ParagraphNumberingRef> StyleLinkedNumbering { get; init; }

        /// <summary>AbstractNum ids whose `w:numStyleLink` indirection (FR-12's harder chain case — the
        /// design's flagged "numStyleLink as a potential uncovered construct") could NOT be resolved to a
        /// concrete level set. Escalation surface: a paragraph resolving to one of these ids means the
        /// doc uses a numbering construct outside this model (this task's escalation trigger).</summary>
        public required IReadOnlySet<int> UnresolvedNumStyleLinkAbstractNumIds { get; init; }

        /// <summary>Resolves the effective level definition for `(numId, ilvl)`, honoring a full
        /// `w:lvlOverride` before falling back to the abstractNum's own level — the same precedence
        /// <c>ComposeDocumentRenderer</c> authors (an override always wins). Null means the numId is
        /// unresolvable or that exact level is not defined (031's call whether to fall back to the
        /// nearest lower level, mirroring <see cref="ResolveOrdered"/>'s existing tolerant fallback).</summary>
        public NumberingLevelDef? ResolveLevel(int numId, int ilvl)
        {
            if (Overrides.TryGetValue((numId, ilvl), out var over) && over.FullLevelOverride is not null)
            {
                return over.FullLevelOverride;
            }
            return AbstractNumIdByNumId.TryGetValue(numId, out var abstractNumId) && Levels.TryGetValue((abstractNumId, ilvl), out var def)
                ? def
                : null;
        }

        /// <summary>A `w:startOverride` for `(numId, ilvl)`, or null if this numId does not override the
        /// level's start.</summary>
        public int? ResolveStartOverride(int numId, int ilvl) =>
            Overrides.TryGetValue((numId, ilvl), out var over) ? over.StartOverride : null;
    }

    /// <summary>
    /// Parses `numbering.xml` into a <see cref="NumberingModel"/> (FR-11) and resolves STYLE-LINKED
    /// numbering from `styles.xml` (FR-12). Pure / static / never throws on a malformed numbering or
    /// styles part (mirrors <see cref="ResolveOrdered"/>'s fail-open posture) — a defect in one part
    /// degrades that part of the model to empty rather than aborting the whole projection (F-04, Build()
    /// never throws). `internal` (not private) so task 031 and this task's own sanity tests can drive it
    /// directly over the corpus fixtures without a full <see cref="Build"/> round-trip.
    /// </summary>
    internal static NumberingModel BuildNumberingModel(MainDocumentPart mainPart)
    {
        var abstractNumIdByNumId = new Dictionary<int, int>();
        var levels = new Dictionary<(int, int), NumberingLevelDef>();
        var overrides = new Dictionary<(int, int), NumberingLevelOverrideDef>();
        var unresolvedNumStyleLink = new HashSet<int>();

        // "numbering style" (`w:style w:type="numbering"`) -> the numId it authors — needed BEFORE the
        // numbering.xml pass to chase an abstractNum's `w:numStyleLink` indirection.
        var numberingStyleNumId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stylesPartForLink = mainPart.StyleDefinitionsPart?.Styles;
            if (stylesPartForLink is not null)
            {
                foreach (var style in stylesPartForLink.Elements<Style>())
                {
                    var sid = style.StyleId?.Value;
                    var numId = style.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value;
                    if (sid is not null && numId is not null) numberingStyleNumId[sid] = numId.Value;
                }
            }
        }
        catch { /* fail-open — numStyleLink chains simply won't resolve */ }

        try
        {
            var numbering = mainPart.NumberingDefinitionsPart?.Numbering;
            if (numbering is not null)
            {
                var abstractNumsById = numbering.Elements<AbstractNum>()
                    .Where(a => a.AbstractNumberId?.Value is not null)
                    .GroupBy(a => a.AbstractNumberId!.Value)
                    .ToDictionary(g => g.Key, g => g.First()); // GroupBy: a malformed doc could repeat an id; first wins, never throws

                foreach (var (abstractNumId, abstractNum) in abstractNumsById)
                {
                    var effective = abstractNum;
                    var numStyleLink = abstractNum.NumberingStyleLink?.Val?.Value;
                    if (!string.IsNullOrEmpty(numStyleLink))
                    {
                        var resolved = ResolveNumStyleLinkTarget(numStyleLink!, numberingStyleNumId, numbering, abstractNumsById);
                        if (resolved is not null) { effective = resolved; }
                        else { unresolvedNumStyleLink.Add(abstractNumId); continue; }
                    }

                    foreach (var level in effective.Elements<Level>())
                    {
                        var lvl = level.LevelIndex?.Value ?? 0;
                        levels[(abstractNumId, lvl)] = ToLevelDef(abstractNumId, lvl, level);
                    }
                }

                foreach (var instance in numbering.Elements<NumberingInstance>())
                {
                    var numId = instance.NumberID?.Value;
                    var abstractNumId = instance.AbstractNumId?.Val?.Value;
                    if (numId is null || abstractNumId is null) continue;
                    abstractNumIdByNumId[numId.Value] = abstractNumId.Value;

                    foreach (var lvlOverride in instance.Elements<LevelOverride>())
                    {
                        var lvl = lvlOverride.LevelIndex?.Value ?? 0;
                        var startOverride = lvlOverride.StartOverrideNumberingValue?.Val?.Value;
                        var fullOverride = lvlOverride.Level is { } overrideLevel
                            ? ToLevelDef(abstractNumId.Value, lvl, overrideLevel)
                            : null;
                        overrides[(numId.Value, lvl)] = new NumberingLevelOverrideDef(startOverride, fullOverride);
                    }
                }
            }
        }
        catch { /* fail-open — see class remarks */ }

        var styleLinked = new Dictionary<string, ParagraphNumberingRef>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var stylesPart = mainPart.StyleDefinitionsPart?.Styles;
            if (stylesPart is not null)
            {
                var stylesById = stylesPart.Elements<Style>()
                    .Where(s => s.StyleId?.Value is not null)
                    .GroupBy(s => s.StyleId!.Value!)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var (styleId, style) in stylesById)
                {
                    var resolved = ResolveStyleNumbering(style, stylesById);
                    if (resolved is not null) styleLinked[styleId] = resolved;
                }
            }
        }
        catch { /* fail-open — see class remarks */ }

        return new NumberingModel
        {
            AbstractNumIdByNumId = abstractNumIdByNumId,
            Levels = levels,
            Overrides = overrides,
            StyleLinkedNumbering = styleLinked,
            UnresolvedNumStyleLinkAbstractNumIds = unresolvedNumStyleLink,
        };
    }

    private static NumberingLevelDef ToLevelDef(int abstractNumId, int lvl, Level level) => new(
        AbstractNumId: abstractNumId,
        Level: lvl,
        NumFmt: level.NumberingFormat?.Val?.Value,
        LvlText: level.LevelText?.Val?.Value,
        Start: level.StartNumberingValue?.Val?.Value,
        LvlRestart: level.LevelRestart?.Val?.Value,
        IsLgl: IsOn(level.IsLegalNumberingStyle),
        ParagraphStyleIdInLevel: level.ParagraphStyleIdInLevel?.Val?.Value,
        HasPictureBullet: level.LevelPictureBulletId is not null);

    /// <summary>Chases an abstractNum's `w:numStyleLink` (FR-12 chain case): the target is a "numbering
    /// style" (`w:style w:type="numbering"`) whose OWN `w:numPr` names the numId carrying the REAL level
    /// definitions — one more indirection than the ordinary numId→abstractNum lookup. Bounded hop count +
    /// a visited-set cycle guard (never observed in the corpus, but the model must not hang on one).
    /// Returns null — unresolved — if the chain bottoms out without reaching a concrete abstractNum; the
    /// caller records that abstractNumId in <see cref="NumberingModel.UnresolvedNumStyleLinkAbstractNumIds"/>.</summary>
    private static AbstractNum? ResolveNumStyleLinkTarget(
        string targetStyleId,
        IReadOnlyDictionary<string, int> numberingStyleNumId,
        Numbering numbering,
        IReadOnlyDictionary<int, AbstractNum> abstractNumsById,
        int maxHops = 8)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentStyleId = targetStyleId;
        for (var hop = 0; hop < maxHops; hop++)
        {
            if (!visited.Add(currentStyleId)) return null; // cycle
            if (!numberingStyleNumId.TryGetValue(currentStyleId, out var numId)) return null;

            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractNumId = instance?.AbstractNumId?.Val?.Value;
            if (abstractNumId is null || !abstractNumsById.TryGetValue(abstractNumId.Value, out var abstractNum))
            {
                return null;
            }

            var nextLink = abstractNum.NumberingStyleLink?.Val?.Value;
            if (string.IsNullOrEmpty(nextLink)) return abstractNum; // concrete — chain resolved
            currentStyleId = nextLink!;
        }
        return null; // hop budget exhausted — treat as unresolved rather than risk a pathological loop
    }

    /// <summary>Resolves a paragraph STYLE's effective numbering (FR-12) — the style's own `w:numPr`, or
    /// (if absent) the nearest `w:basedOn` ancestor's, mirroring Word's paragraph-property style
    /// inheritance. Bounded hop count + visited-set cycle guard against a malformed `w:basedOn` chain.
    /// <see cref="ParagraphNumberingRef.SourceStyleId"/> in the returned ref is the style that ACTUALLY
    /// carries the `w:numPr` (which may be an ancestor of <paramref name="style"/>, not <paramref
    /// name="style"/> itself, when resolved via `w:basedOn`).</summary>
    private static ParagraphNumberingRef? ResolveStyleNumbering(
        Style style, IReadOnlyDictionary<string, Style> stylesById, int maxHops = 20)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Style? current = style;
        for (var hop = 0; hop < maxHops && current is not null; hop++)
        {
            var sid = current.StyleId?.Value;
            if (sid is not null && !visited.Add(sid)) return null; // cycle

            var numPr = current.StyleParagraphProperties?.NumberingProperties;
            var numId = numPr?.NumberingId?.Val?.Value;
            if (numId is not null)
            {
                var ilvl = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
                return new ParagraphNumberingRef(numId.Value, ilvl, StyleLinked: true, SourceStyleId: sid);
            }

            var basedOnId = current.BasedOn?.Val?.Value;
            current = basedOnId is not null && stylesById.TryGetValue(basedOnId, out var parent) ? parent : null;
        }
        return null;
    }

    /// <summary>Resolves a paragraph's effective `(numId, ilvl)` for the WS-3 numbering model (FR-11/
    /// FR-12) — direct `w:numPr` first (mirrors <see cref="ListInfo"/>'s extraction exactly), else the
    /// paragraph's `pStyle` resolved through <see cref="NumberingModel.StyleLinkedNumbering"/>
    /// (heading-style-numbering.docx's Heading1/Heading2, corpus-manifest.md row 10). `internal` so 031
    /// calls this from the SAME per-paragraph call site <see cref="Build"/>'s Pass-1 loop already visits
    /// (no second walk) and so this task's tests can assert the resolution directly.</summary>
    internal static ParagraphNumberingRef? ResolveParagraphNumbering(Paragraph p, NumberingModel model)
    {
        var directNumPr = p.ParagraphProperties?.NumberingProperties;
        var directNumId = directNumPr?.NumberingId?.Val?.Value;
        if (directNumId is not null)
        {
            var ilvl = directNumPr!.NumberingLevelReference?.Val?.Value ?? 0;
            return new ParagraphNumberingRef(
                directNumId.Value, ilvl, StyleLinked: false, SourceStyleId: p.ParagraphProperties?.ParagraphStyleId?.Val?.Value);
        }

        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (styleId is not null && model.StyleLinkedNumbering.TryGetValue(styleId, out var styleRef))
        {
            return styleRef;
        }

        return null;
    }

    // ── numbering computation engine (task 031, spaarkeai-compose-fidelity-r4.5 WS-3, FR-11..FR-14) ────
    // THE flagship of R4.5. Replays Word's numbering algorithm over the task-030 model (design §4 WS-3),
    // producing the EXACT displayed label per numbered paragraph — decimal / lowerLetter / upperLetter /
    // lowerRoman / upperRoman / bullet formats, multi-level "%1.%2.%3" composition, w:isLgl (legal →
    // decimal), w:start / w:lvlRestart / w:lvlOverride / w:startOverride. Deterministic (NFR-06): identical
    // inputs → identical labels, no reliance on render order. Read-time only (FR-14): computes the label as
    // READ; it does NOT auto-renumber on edit — live renumber-on-insert/delete is R5 G3, which builds on
    // THIS shared engine (recorded so R5 does not fork the model). It is the read-side twin of the write-side
    // ComposeDocumentRenderer (task 033 proves the two agree).

    /// <summary>
    /// Deterministic replay of Word's numbering algorithm (FR-11) over a <see cref="NumberingModel"/>, run
    /// once per document across the projection's SINGLE document-order Pass-1 walk. A single instance is
    /// created before the walk and <see cref="Compute"/> is called — in document order — for each numbered
    /// paragraph; the instance carries the running counters forward, so an interrupted numbered run
    /// continues (never restarts at 1). Stateful within one <c>Build</c> call, never shared across documents.
    /// </summary>
    /// <remarks>
    /// <para><b>Counter model.</b> One running counter per <c>(abstractNumId, level)</c> (design §4 WS-3
    /// step 1). On each numbered paragraph the paragraph's level increments (or initializes to
    /// <c>w:start</c> / <c>w:startOverride</c> on first use since a reset), then all DEEPER levels reset per
    /// Word's default (a more-significant level increment restarts deeper levels) as modified by
    /// <c>w:lvlRestart</c> (<c>0</c> ⇒ never restart). The label is then composed from the level's
    /// <c>w:lvlText</c> template, substituting each <c>%n</c> with the counter at level <c>n-1</c> formatted
    /// per that level's <c>w:numFmt</c> — unless the current level is <c>w:isLgl</c> (legal), which forces
    /// EVERY inserted level reference to decimal.</para>
    /// <para><b>Purity / determinism (NFR-06 / ADR-007/013).</b> Pure OOXML computation over the model — no
    /// I/O, no Graph, no AI type, no RNG. Deeper-level resets remove dictionary keys (order-independent), so
    /// the emitted label depends only on document-order inputs, never on hash-iteration order.</para>
    /// </remarks>
    internal sealed class NumberingComputationEngine
    {
        private readonly NumberingModel _model;

        // The running ordinal per (abstractNumId, level) — the spec/design "counter per (abstractNumId,
        // level)". Absence of a key means the level has not been used since its last reset (next use
        // initializes to w:start / w:startOverride).
        private readonly Dictionary<(int AbstractNumId, int Level), int> _counters = new();

        // (numId, level) whose numId-scoped w:startOverride has already been consumed. A w:startOverride
        // seeds the FIRST use of that numId's level; after a subsequent reset the level falls back to the
        // abstractNum's own w:start (Word's common behavior). Tracked so the override applies exactly once.
        private readonly HashSet<(int NumId, int Level)> _appliedStartOverrides = new();

        public NumberingComputationEngine(NumberingModel model) => _model = model;

        /// <summary>
        /// Advances the counters for one numbered paragraph (in document order) and returns the label Word
        /// displays for it, or <c>null</c> when the numbering is unresolvable (the <c>numId</c> is not in the
        /// model — no corpus doc hits this; it is the escalation-relevant "construct outside the model" case,
        /// already surfaced as a warning by <see cref="Build"/>). Must be called exactly once per numbered
        /// paragraph, in document order, for the single-counter replay to be exact.
        /// </summary>
        public string? Compute(ParagraphNumberingRef numbering)
        {
            var numId = numbering.NumId;
            var ilvl = numbering.Ilvl;
            if (!_model.AbstractNumIdByNumId.TryGetValue(numId, out var abstractNumId))
            {
                return null; // unresolvable numId — cannot compute a faithful label; do not fabricate one.
            }

            var levelDef = _model.ResolveLevel(numId, ilvl);

            // 1) increment this level's counter (initialize on first use since the last reset).
            var key = (abstractNumId, ilvl);
            if (_counters.TryGetValue(key, out var current))
            {
                _counters[key] = current + 1;
            }
            else
            {
                _counters[key] = InitialValue(numId, ilvl, levelDef);
            }

            // 2) reset deeper levels of this abstractNum (Word default, honoring w:lvlRestart).
            ResetDeeperLevels(abstractNumId, ilvl);

            // 3) format + compose the displayed label.
            return ComposeLabel(numId, abstractNumId, ilvl, levelDef);
        }

        private int InitialValue(int numId, int ilvl, NumberingLevelDef? levelDef)
        {
            // A numId-scoped w:startOverride seeds the FIRST use of this level (once), independent of the
            // abstractNum's own w:start. HashSet.Add returns false if already applied — then fall through.
            var startOverride = _model.ResolveStartOverride(numId, ilvl);
            if (startOverride.HasValue && _appliedStartOverrides.Add((numId, ilvl)))
            {
                return startOverride.Value;
            }
            return levelDef?.Start ?? 1; // w:start default is 1 (ECMA-376 §17.9.25).
        }

        private void ResetDeeperLevels(int abstractNumId, int incrementedLevel)
        {
            // Snapshot the keys first (cannot mutate _counters while enumerating). Removal order is
            // irrelevant to the result — determinism holds regardless of hash-iteration order.
            var deeper = _counters.Keys
                .Where(k => k.AbstractNumId == abstractNumId && k.Level > incrementedLevel)
                .ToList();
            foreach (var k in deeper)
            {
                if (ShouldRestart(abstractNumId, k.Level, incrementedLevel))
                {
                    _counters.Remove(k);
                }
            }
        }

        /// <summary>
        /// Whether a deeper level restarts when <paramref name="incrementedLevel"/> increments. Default
        /// (no <c>w:lvlRestart</c>): a deeper level restarts whenever ANY more-significant level increments
        /// (Word's behavior; the corpus exemplars use exactly this). <c>w:lvlRestart="0"</c> ⇒ the level
        /// NEVER restarts. A specific value <c>N</c> (1-based level number) ⇒ restart only when a level
        /// whose 1-based number is ≤ <c>N</c> increments (reduces to default when <c>N</c> equals the
        /// deeper level's own number).
        /// </summary>
        private bool ShouldRestart(int abstractNumId, int deeperLevel, int incrementedLevel)
        {
            var lvlRestart = _model.Levels.TryGetValue((abstractNumId, deeperLevel), out var def) ? def.LvlRestart : null;
            if (lvlRestart is null) return true;      // default: restart on any more-significant increment
            if (lvlRestart.Value <= 0) return false;  // w:lvlRestart="0" ⇒ never restart
            return (incrementedLevel + 1) <= lvlRestart.Value;
        }

        private string ComposeLabel(int numId, int abstractNumId, int ilvl, NumberingLevelDef? levelDef)
        {
            var numFmt = levelDef?.NumFmt;
            var lvlText = levelDef?.LvlText;

            // Bullet: the lvlText IS the literal marker glyph (no %n substitution) — return it verbatim
            // (the displayed marker; WS-4 treats a non-numeric marker as a non-citation).
            if (numFmt == NumberFormatValues.Bullet)
            {
                return lvlText ?? string.Empty;
            }

            // No template (rare/malformed): fall back to the bare formatted current counter.
            if (string.IsNullOrEmpty(lvlText))
            {
                var v = _counters.TryGetValue((abstractNumId, ilvl), out var cv) ? cv : 1;
                return FormatCounter(v, numFmt);
            }

            var legal = levelDef?.IsLgl == true; // legal (w:isLgl) forces decimal for EVERY inserted level ref.
            return SubstituteLvlText(lvlText!, numId, abstractNumId, legal);
        }

        /// <summary>
        /// Substitutes each <c>%n</c> (n = 1..9) placeholder in a <c>w:lvlText</c> template with the counter
        /// at level <c>n-1</c>, formatted per that level's <c>w:numFmt</c> (or decimal when
        /// <paramref name="legal"/>). Literal characters (".", "(", ")", spaces, "Article ", …) are copied
        /// verbatim — so "%1.%2.%3" → "4.2.1", "(%2)" → "(b)", "Article %1" → "Article IV". Depth reaches
        /// the current level, giving WS-4 the sub-item granularity it needs ("4.2(b)(iii)").
        /// </summary>
        private string SubstituteLvlText(string lvlText, int numId, int abstractNumId, bool legal)
        {
            var sb = new StringBuilder(lvlText.Length + 4);
            for (var i = 0; i < lvlText.Length; i++)
            {
                var c = lvlText[i];
                if (c == '%' && i + 1 < lvlText.Length && lvlText[i + 1] is >= '1' and <= '9')
                {
                    var refLevel = (lvlText[i + 1] - '0') - 1; // %n references level n-1 (0-based)
                    var refDef = _model.ResolveLevel(numId, refLevel);
                    var value = _counters.TryGetValue((abstractNumId, refLevel), out var cv)
                        ? cv
                        : (refDef?.Start ?? 1); // a not-yet-counted referenced level shows its start (valid docs reference only 0..current)
                    var fmt = legal ? NumberFormatValues.Decimal : refDef?.NumFmt;
                    sb.Append(FormatCounter(value, fmt));
                    i++; // consume the digit
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Formats one level's counter per <c>w:numFmt</c> (design §4 WS-3 step 3). The five named
        /// legal schemes plus decimal; any other format (decimalZero, ordinal, cardinalText, …) is not used
        /// by the legal corpus and degrades to decimal — the honest, never-throw fallback.</summary>
        private static string FormatCounter(int value, NumberFormatValues? fmt)
        {
            if (fmt == NumberFormatValues.LowerLetter) return ToLetters(value, upper: false);
            if (fmt == NumberFormatValues.UpperLetter) return ToLetters(value, upper: true);
            if (fmt == NumberFormatValues.LowerRoman) return ToRoman(value, upper: false);
            if (fmt == NumberFormatValues.UpperRoman) return ToRoman(value, upper: true);
            return value.ToString(CultureInfo.InvariantCulture); // decimal + safe fallback
        }

        /// <summary>Bijective base-26 spreadsheet-style letters: 1→a, 26→z, 27→aa, 28→ab, … (FR-11
        /// lowerLetter/upperLetter, incl. the z→aa overflow). Non-positive input degrades to decimal.</summary>
        internal static string ToLetters(int n, bool upper)
        {
            if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
            var baseChar = upper ? 'A' : 'a';
            var sb = new StringBuilder(4);
            while (n > 0)
            {
                n--;
                sb.Insert(0, (char)(baseChar + (n % 26)));
                n /= 26;
            }
            return sb.ToString();
        }

        private static readonly (int Value, string Upper, string Lower)[] RomanNumerals =
        {
            (1000, "M", "m"), (900, "CM", "cm"), (500, "D", "d"), (400, "CD", "cd"),
            (100, "C", "c"), (90, "XC", "xc"), (50, "L", "l"), (40, "XL", "xl"),
            (10, "X", "x"), (9, "IX", "ix"), (5, "V", "v"), (4, "IV", "iv"), (1, "I", "i"),
        };

        /// <summary>Standard subtractive roman numerals: 1→i, 4→iv, 9→ix, 40→xl, … (FR-11 lowerRoman/
        /// upperRoman). Out-of-range input (≤0 or &gt;3999) degrades to decimal.</summary>
        internal static string ToRoman(int n, bool upper)
        {
            if (n <= 0 || n > 3999) return n.ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder(8);
            foreach (var (value, u, l) in RomanNumerals)
            {
                while (n >= value)
                {
                    sb.Append(upper ? u : l);
                    n -= value;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// FR-09's alignment emit (was <c>AppendAlignment</c>) plus FR-07 (task 021) <c>w:ind</c> emit, combined
    /// into ONE <c>style="…"</c> attribute — an HTML element cannot carry two <c>style</c> attributes, so
    /// alignment and indentation MUST share a single call site here rather than each appending their own.
    /// Called once per projected paragraph (both the plain/heading path and the list-item path).
    /// </summary>
    private static void AppendParagraphStyle(Paragraph p, BuildContext ctx)
    {
        List<string>? decls = null;

        var just = p.ParagraphProperties?.Justification?.Val?.Value;
        if (just is not null)
        {
            string? align = null;
            if (just.Value == JustificationValues.Center) align = "center";
            else if (just.Value == JustificationValues.Right) align = "right";
            else if (just.Value == JustificationValues.Both) align = "justify";
            if (align is not null) (decls ??= new List<string>(2)).Add($"text-align:{align}");
        }

        AppendIndentDeclarations(p, ref decls);

        if (decls is { Count: > 0 })
        {
            ctx.Append(" style=\"");
            ctx.Append(string.Join(";", decls));
            ctx.Append("\"");
        }
    }

    /// <summary>
    /// FR-07 (task 021): emits <c>w:ind</c> (<c>@w:left</c>/<c>@w:firstLine</c>/<c>@w:hanging</c>, all
    /// stored in twips — 1/1440 inch) as <c>margin-left</c>/<c>text-indent</c> CSS on the projected
    /// paragraph. Today <c>w:ind</c> is dropped entirely, so indented legal clauses render flush-left
    /// (design §1/§4 WS-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Unit choice — pt, not px.</b> OOXML twips convert EXACTLY to CSS points: 1pt == 20 twips by
    /// definition (both are point-based units), so <c>twips / 20.0</c> is a lossless conversion with no
    /// assumed reference DPI — unlike px, which would require picking a DPI (96 is the common web default
    /// but is not itself part of the OOXML unit system, so it would be an added assumption, not a fact).
    /// </para>
    /// <para>
    /// <b>OOXML semantics mirrored:</b> <c>w:left</c> is the paragraph's base left indent, emitted as
    /// <c>margin-left</c>. <c>w:firstLine</c> is an ADDITIONAL positive offset applied only to the first
    /// line, emitted as a positive <c>text-indent</c> (on top of <c>margin-left</c>, matching CSS
    /// <c>text-indent</c>'s own semantics). <c>w:hanging</c> is the inverse — the first line is OUTDENTED
    /// relative to the rest of the paragraph — emitted as a NEGATIVE <c>text-indent</c> equal to
    /// <c>-hanging</c>. Per ECMA-376 §17.3.1.12, <c>w:hanging</c> and <c>w:firstLine</c> are mutually
    /// exclusive on one <c>w:ind</c>; if a malformed source somehow carries both, <c>w:hanging</c> takes
    /// precedence (Word's own resolution), so it is checked first.
    /// </para>
    /// </remarks>
    private static void AppendIndentDeclarations(Paragraph p, ref List<string>? decls)
    {
        var ind = p.ParagraphProperties?.Indentation;
        if (ind is null) return;

        var leftPt = TwipsToPoints(ind.Left?.Value);
        if (leftPt is not null) (decls ??= new List<string>(2)).Add($"margin-left:{FormatPt(leftPt.Value)}");

        var hangingPt = TwipsToPoints(ind.Hanging?.Value);
        if (hangingPt is not null)
        {
            (decls ??= new List<string>(2)).Add($"text-indent:{FormatPt(-hangingPt.Value)}");
        }
        else
        {
            var firstLinePt = TwipsToPoints(ind.FirstLine?.Value);
            if (firstLinePt is not null) (decls ??= new List<string>(2)).Add($"text-indent:{FormatPt(firstLinePt.Value)}");
        }
    }

    private static double? TwipsToPoints(string? twips) =>
        !string.IsNullOrEmpty(twips) && int.TryParse(twips, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v / 20.0
            : null;

    private static string FormatPt(double pt) => pt.ToString("0.##", CultureInfo.InvariantCulture) + "pt";

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
        private readonly Dictionary<Paragraph, ParagraphNumberingRef> _numberingByParagraph;
        private readonly Dictionary<Paragraph, string> _computedNumberByParagraph;
        private readonly Dictionary<string, int> _warnings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenIds;
        private readonly Func<HashSet<string>, string> _mintUnique;
        private readonly List<ComposeBlockAtom> _blockAtoms = new();
        private bool _listOpen;
        private bool _listOrdered;

        public BuildContext(
            MainDocumentPart mainPart, Dictionary<Paragraph, string> idByParagraph, CancellationToken ct,
            HashSet<string> seenIds, Func<HashSet<string>, string> mintUnique,
            NumberingModel numbering, Dictionary<Paragraph, ParagraphNumberingRef> numberingByParagraph,
            Dictionary<Paragraph, string> computedNumberByParagraph)
        {
            MainPart = mainPart;
            _idByParagraph = idByParagraph;
            CancellationToken = ct;
            _seenIds = seenIds;
            _mintUnique = mintUnique;
            Numbering = numbering;
            _numberingByParagraph = numberingByParagraph;
            _computedNumberByParagraph = computedNumberByParagraph;
        }

        public MainDocumentPart MainPart { get; }
        public CancellationToken CancellationToken { get; }
        public int EmittedParagraphCount { get; set; }
        public string Html => _sb.ToString();
        public IReadOnlyList<ComposeBlockAtom> BlockAtoms => _blockAtoms;

        /// <summary>Task 030 (WS-3, FR-11/FR-12): the read-side numbering MODEL for this document,
        /// resolved once in <see cref="Build"/>'s Pass 1. Task 031's computation engine consumes this
        /// (plus <see cref="TryGetParagraphNumbering"/>) from the SAME single document-order walk.</summary>
        public NumberingModel Numbering { get; }

        public IReadOnlyList<ComposeProjectionWarning> Warnings =>
            _warnings.Select(kv => new ComposeProjectionWarning(kv.Key, kv.Value)).ToList();

        public bool TryGetParaId(Paragraph p, out string paraId) => _idByParagraph.TryGetValue(p, out paraId!);

        /// <summary>The numbering source resolved for <paramref name="p"/> in Pass 1 (task 030) — direct
        /// `w:numPr` or style-linked via `pStyle` (FR-12). False when <paramref name="p"/> is not a
        /// numbered paragraph.</summary>
        public bool TryGetParagraphNumbering(Paragraph p, out ParagraphNumberingRef numbering) =>
            _numberingByParagraph.TryGetValue(p, out numbering!);

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

        /// <summary>
        /// Task 032 (WS-3, FR-13): emits the 031-computed numbering label as a PARAGRAPH DATA ATTRIBUTE
        /// (<c>data-computed-number</c>) plus its zero-based level (<c>data-numbering-level</c>) on the
        /// paragraph's own <c>&lt;p&gt;</c>/<c>&lt;h#&gt;</c> tag — never as text content. The computed
        /// label is COMPUTED, not source text: injecting it into the run text would break the
        /// text-exactness harness (source-run text == projected text). The client
        /// (<c>composeNumberAtomExtension.ts</c>) reads this attribute and renders the visible,
        /// non-editable number-atom prefix; this method only carries the data. No-op for a paragraph with
        /// no computed label (unnumbered, or FR-13's "unresolvable numId" fail-closed case — never
        /// fabricate a number).
        /// </summary>
        public void AppendNumberingAttrs(Paragraph p)
        {
            if (!_computedNumberByParagraph.TryGetValue(p, out var number) || string.IsNullOrEmpty(number))
            {
                return;
            }

            Append(" data-computed-number=\"");
            AppendEscapedAttr(number);
            Append("\"");

            if (_numberingByParagraph.TryGetValue(p, out var numberingRef))
            {
                Append(" data-numbering-level=\"");
                Append(numberingRef.Ilvl.ToString(CultureInfo.InvariantCulture));
                Append("\"");
            }
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
