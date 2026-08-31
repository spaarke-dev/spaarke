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
/// non-text declared type (<c>ComposeOoxmlPrimitives.IsSpecialSdtControl</c>), Word fields (<c>w:fldSimple</c>, or a
/// <c>w:fldChar</c> begin/instrText/separate/end run sequence), and complex/floating objects (<c>w:drawing</c>,
/// <c>w:object</c>) — are rendered as non-editable ATOM placeholders instead of being opened/reinterpreted.
/// Inline atoms (fields, inline content controls, complex objects — all nested inside a paragraph's runs)
/// carry their containing paragraph's <c>w14:paraId</c> and are signaled via <see cref="RunBoundary.AtomKind"/>
/// in the offset-addressing table. Whole-construct atoms (a block-level SDT whose content is not ordinary
/// editable prose) get their own minted id, tracked in <see cref="ComposeDocxProjection.BlockAtoms"/> — NOT
/// in <see cref="ComposeDocxProjection.ParaIdMap"/>, so the F-01 single-walk invariant ("every data-paraid
/// has a ParaIdMap entry") is untouched. Because an atom's inner OOXML is never opened, its subtree is
/// byte-identical after a no-op save by construction (I-4) — the escalation-boundary decision for the
/// SDT-wraps-editable-paragraphs case is documented on <c>ComposeOoxmlPrimitives.IsSpecialSdtControl</c>.
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
                var numberingModel = ComposeNumbering.BuildNumberingModel(mainPart);
                var numberingByParagraph = new Dictionary<Paragraph, ComposeNumbering.ParagraphNumberingRef>(ReferenceEqualityComparer.Instance);
                // Task 032 (WS-3, FR-13): the 031-computed label, keyed by paragraph INSTANCE so Pass 2
                // (render) can attach it to the emitted `<p>`/`<h#>` tag as a DATA ATTRIBUTE (never as text
                // content — the text-exactness harness compares source-run text to projected text, and the
                // computed label is not source text). Populated in the SAME Pass-1 loop below; no second walk.
                var computedNumberByParagraph = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
                var numberingDiagnostics = new List<string>();

                // Task 031 (WS-3, FR-11..FR-14): the deterministic numbering COMPUTATION engine — replays
                // Word's per-(numId, level) instance-scoped counter algorithm over the 030 model IN THIS SAME
                // document-order Pass-1 walk. Because counters are carried forward across the whole pass, an
                // interrupted numbered run (heading/body/table between clauses) does NOT reset to 1 — the
                // core defect this fixes. Read-time only (no auto-renumber on edit; that is R5 G3 / FR-14).
                var numberingEngine = new ComposeNumbering.NumberingComputationEngine(numberingModel);

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
                    int? numberingLevel = null;
                    IReadOnlyList<int>? listPath = null;
                    var numberingRef = ComposeNumbering.ResolveParagraphNumbering(paragraphs[i], numberingModel);
                    if (numberingRef is not null)
                    {
                        numberingByParagraph[paragraphs[i]] = numberingRef;

                        // Task 031: advance the counters (in doc order) and compute the displayed label +
                        // the level's ordinal chain (task 040, WS-4/FR-16 — the SAME engine call, no
                        // recomputation). MUST run for EVERY numbered paragraph in order — including
                        // style-linked headings ListInfo excludes from list treatment — so the per-(numId,
                        // level) instance-scoped counter is exact (FR-11/FR-12). The engine mutates its
                        // counters here; the returned label + chain attach to this paragraph's ParaIdMap
                        // entry for 032 render + WS-4 (FR-13/FR-16, the reference layer 041/042 build on).
                        var computation = numberingEngine.Compute(numberingRef);
                        if (computation is not null)
                        {
                            computedNumber = computation.Value.Label;
                            computedNumberByParagraph[paragraphs[i]] = computedNumber;
                            // Un-numbered / unresolvable paragraphs never reach here — never a fabricated
                            // level or chain (matches ComputedNumber's own fail-closed null convention).
                            numberingLevel = numberingRef.Ilvl;
                            listPath = computation.Value.ListPath;
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

                    // Task 040 (WS-4, FR-16): the heading OUTLINE level (Heading1..Heading6 → 1..6; null for
                    // a non-heading paragraph) — independent of numbering (a heading may or may not carry
                    // style-linked w:numPr; a numbered list item is never a heading — see RenderParagraph's
                    // headingLevel/listInfo mutual exclusion). Computed here (not deferred to Pass 2) so it
                    // lands on the SAME ParaIdMap entry as computedNumber/numberingLevel/listPath — the full
                    // per-paragraph reference set FR-16 requires, all populated from this single doc-order walk.
                    var headingLevel = ComposeOoxmlPrimitives.HeadingLevel(paragraphs[i]);

                    map.Add(new ParaIdMapEntry(i, id, minted, computedNumber, numberingLevel, listPath, headingLevel));
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
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdt.SdtProperties))
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
                        // escalation-boundary decision (see ComposeOoxmlPrimitives.IsSpecialSdtControl remarks). Treating every SDT
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

        var headingLevel = ComposeOoxmlPrimitives.HeadingLevel(p);
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
    /// <see cref="ComposeOoxmlPrimitives.IsSpecialSdtControl"/> as a genuinely non-text control. The atom's inner OOXML is NEVER
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
        // ComposeOoxmlPrimitives.FieldScanState remarks; not exercised by the corpus, documented simplification).
        var field = new ComposeOoxmlPrimitives.FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (ComposeOoxmlPrimitives.TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            // The outermost w:fldChar end just closed — emit ONE atom spanning the whole
                            // field, length = its cached RESULT text (never its instrText field code).
                            var atomLen = ComposeOoxmlPrimitives.ExtractRunsDisplayText(field.ResultRuns).Length;
                            runs.Add(new RunBoundary(runIndex, cumOffset, atomLen, trackChange, ComposeAtomKind.Field));
                            runIndex++;
                            cumOffset += atomLen;
                            field.Reset();
                        }
                        break; // consumed as part of the field span either way
                    }
                    if (ComposeOoxmlPrimitives.IsComplexObjectRun(r))
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
                    var sfLen = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sf).Length;
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
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        // An inline content control with a genuinely non-text declared type is an atom.
                        var sdtLen = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sdtRun).Length;
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





    /// <summary>
    /// Task 049: the self-describing payload an inline FIELD atom carries back to the client, or <c>null</c>
    /// when the field is not carryable.
    /// </summary>
    /// <remarks>
    /// <para>The PRESENCE of <c>data-field-instr</c> is the contract: it means "this field can be handed
    /// back verbatim on save". A nested or instruction-less field gets no payload at all, so a client cannot
    /// accidentally return a construct the server would have to refuse — the gate lives in one place
    /// (<see cref="TryCarryField"/>'s rule, mirrored here) rather than being restated as client policy.</para>
    /// <para>Emitting this is the read half of the carry. Without it the write half is unreachable from the
    /// editor: an edited paragraph is rebuilt from the client's own nodes, and a field atom that carries
    /// nothing contributes nothing.</para>
    /// </remarks>
    private static (string Name, string Value)[]? FieldAtomDataAttributes(
        string instruction, bool complex, bool locked, bool dirty, bool nested)
    {
        if (nested || string.IsNullOrWhiteSpace(instruction))
        {
            return null;
        }

        var attributes = new List<(string, string)>(4)
        {
            ("data-field-instr", instruction),
        };
        if (complex) attributes.Add(("data-field-complex", "1"));
        if (locked) attributes.Add(("data-field-locked", "1"));
        if (dirty) attributes.Add(("data-field-dirty", "1"));
        return attributes.ToArray();
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
                    length += ComposeOoxmlPrimitives.ExtractRunsDisplayText(ComposeOoxmlPrimitives.RubyBaseRuns(ruby)).Length;
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
        // FR-02 (task 012): mirrors CollectRunBoundaries' field-scan exactly (its own ComposeOoxmlPrimitives.FieldScanState
        // instance — see that method's remarks) so the HTML atom span's text always matches the
        // offset-table's atom length.
        var field = new ComposeOoxmlPrimitives.FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (ComposeOoxmlPrimitives.TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            // Task 049: the atom's DISPLAY is unchanged — still the field's cached result,
                            // still a non-editable leaf. What is new is the self-describing payload beside
                            // it, so a client that rebuilds an edited paragraph can hand the field back
                            // instead of dropping it. Same mechanism (and same I-2 argument) as w:sym's
                            // font + code point: scalars only, no markup crosses the wire.
                            ctx.AppendAtom(
                                ComposeAtomKind.Field,
                                ComposeOoxmlPrimitives.ExtractRunsDisplayText(field.ResultRuns),
                                dataAttributes: FieldAtomDataAttributes(
                                    field.Instruction.ToString(), complex: true,
                                    locked: field.Locked, dirty: field.Dirty, nested: field.MaxDepth > 1));
                            field.Reset();
                        }
                        break;
                    }
                    if (ComposeOoxmlPrimitives.IsComplexObjectRun(r))
                    {
                        ctx.AppendAtom(ComposeAtomKind.ComplexObject, null);
                        break;
                    }
                    RenderRun(r, ctx);
                    break;
                case SimpleField sf:
                    ctx.AppendAtom(
                        ComposeAtomKind.Field,
                        ComposeOoxmlPrimitives.ExtractAtomDisplayText(sf),
                        dataAttributes: FieldAtomDataAttributes(
                            sf.Instruction?.Value ?? string.Empty, complex: false,
                            locked: sf.FieldLock?.Value == true, dirty: sf.Dirty?.Value == true,
                            nested: sf.Descendants<SimpleField>().Any() || sf.Descendants<FieldChar>().Any()));
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
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        ctx.AppendAtom(ComposeAtomKind.Sdt, ComposeOoxmlPrimitives.ExtractAtomDisplayText(sdtRun));
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
        var bold = ComposeOoxmlPrimitives.IsOn(rPr?.Bold);
        var italic = ComposeOoxmlPrimitives.IsOn(rPr?.Italic);
        var underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None;
        var strike = ComposeOoxmlPrimitives.IsOn(rPr?.Strike);

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
                    //
                    // Task 048: now emitted as an ATOM, keeping the `compose-tab` class and the SAME em-space
                    // content so it looks exactly as it did. That the content is unchanged is what makes this
                    // invisible to every coordinate space: RunEditorLength already counted a tab as 1, and the
                    // atom node is a ProseMirror leaf of size 1 contributing that same one character. The only
                    // thing that changed is that the client can now tell this em space from a typed one.
                    ctx.AppendAtom(ComposeAtomKind.Tab, " ", extraClass: "compose-tab");
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
                    // VERIFIED mapping exists (ComposeOoxmlPrimitives.ResolveSymbolGlyph / KnownSymbolGlyphMap); otherwise emit a
                    // visible placeholder (U+FFFD) AND raise the intra-run glyph-loss warning — F-1 never
                    // allows a silent drop. This was previously the FR-06 characterization gap pinned by
                    // ComposeDocxProjectionBuilderTests.Build_ParagraphWithSymbolCharRun_..., now flipped.
                    //
                    // Task 048: the glyph is unchanged — this is still exactly what the user sees — but it is
                    // now wrapped in an ATOM carrying the font + code point verbatim, so a save re-emits the
                    // ORIGINAL w:sym rather than the resolved look-alike. That matters most in precisely the
                    // unmapped case: without this the U+FFFD placeholder, which exists to be honest about a
                    // glyph we could not resolve for DISPLAY, would have been written back into the document
                    // as the user's content.
                    var glyph = ComposeOoxmlPrimitives.ResolveSymbolGlyph(sym, out var mapped);
                    ctx.AppendAtom(ComposeAtomKind.Symbol, glyph, dataAttributes: new[]
                    {
                        ("data-sym-font", sym.Font?.Value ?? string.Empty),
                        ("data-sym-char", sym.Char?.Value ?? string.Empty),
                    });
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
                    ctx.AppendEscaped(ComposeOoxmlPrimitives.ExtractRunsDisplayText(ComposeOoxmlPrimitives.RubyBaseRuns(ruby)));
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



    private void RenderHyperlink(Hyperlink h, BuildContext ctx)
    {
        var href = ComposeOoxmlPrimitives.ResolveHyperlinkHref(h, ctx.MainPart);
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


    // ── classification helpers ───────────────────────────────────────────────────────────────────────


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

    // ── canonical-model projection (task 020, spaarkeai-compose-r6 FR-03) ──────────────────────────────
    // The docx → ComposeContentModel projector — the render-on-save hub's missing imported-doc SOURCE
    // (see projects/spaarkeai-compose-r6/notes/020-canonical-hub-design.md). Same class, same traversal
    // idioms as the HTML read walk above (RenderBlockChildren / RenderInline / RenderRun — kept mirrored,
    // never merged: the read walk emits display HTML with atoms/overlog anchoring concerns; this walk
    // emits the EDITABLE canonical model the renderer authors a fresh docx from). TOTAL / lenient by
    // construction (ADR-049 Path-B flatten-tier): never throws; constructs the thin model cannot carry
    // flatten to their nearest editable form (field → cached result text, opaque SDT → display text,
    // tracked ins/del → settled prose kept) or drop (drawings/objects/pictures + their text boxes) with a
    // counted warning — never a hard-fail, never a 422. Tasks 021–026 widen per-feature fidelity THROUGH
    // this same model and retire the flatten warnings one by one; task 011 generalizes the renderer to
    // author this model onto a preserved carrier package.

    /// <summary>
    /// Projects <paramref name="docx"/> into the canonical <see cref="ComposeContentModel"/> — the model
    /// <see cref="ComposeDocumentRenderer.SynthesizeDocument"/> renders back out on the render-on-save
    /// path. Never throws — an unreadable/empty/over-cap source degrades to
    /// <see cref="ComposeProjectionStatus.Failed"/> with an empty model (mirrors <see cref="Build"/>'s
    /// fail-closed posture; the caller must not render an empty model over a non-empty original).
    /// </summary>
    public ComposeCanonicalModelProjection BuildContentModel(ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default)
    {
        if (docx.IsEmpty)
        {
            return ComposeCanonicalModelProjection.Failed("empty-source");
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
            return ComposeCanonicalModelProjection.Failed("unreadable-source");
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
                    // A body-less package is a legitimately empty document — an empty model renders an
                    // empty (valid) docx, mirroring Build()'s editable-empty case.
                    return new ComposeCanonicalModelProjection { Status = ComposeProjectionStatus.Success };
                }

                var totalParagraphs = body.Descendants<Paragraph>().Count();
                if (totalParagraphs > MaxParagraphs)
                {
                    return ComposeCanonicalModelProjection.Failed("resource-limit-paragraphs");
                }

                // Reuse the R4.5 numbering model (numbering.xml + styles.xml side-part read) for
                // ordered-vs-bullet classification — the SAME closed model the ComposeNumbering.NumberingComputationEngine
                // replays Word's algorithm over, so this walk and the read walk never disagree on a
                // paragraph's numbering source. (Label COMPUTATION is display data, not model data — the
                // model carries the numbering-instance IDENTITY (ComposeBlock.NumId, task 021) and the
                // renderer references the carrier's scheme through it; golden-label parity through the
                // round-trip is proven by ComposeNumberingRoundTripSeamTests.)
                var numbering = ComposeNumbering.BuildNumberingModel(mainPart);
                var ctx = new ModelWalkContext(mainPart, numbering, cancellationToken);

                // Task 024 Step-9.5 F5/F6: PRE-SCAN comment range markers so (a) a bare w:commentReference
                // occurring BEFORE its range (non-canonical order) still folds instead of duplicating
                // anchors, and (b) an id with a BLOCK-level range element (unrepresentable) is suppressed
                // ATOMICALLY — its inline partner must not emit an orphan start/end.
                foreach (var rangeStart in body.Descendants<CommentRangeStart>())
                {
                    if (!TryParseCommentId(rangeStart.Id, out var rangeStartId)) continue;
                    ctx.CommentRangesSeen.Add(rangeStartId);
                    if (rangeStart.Parent is not Paragraph) ctx.SuppressedCommentIds.Add(rangeStartId);
                }
                foreach (var rangeEnd in body.Descendants<CommentRangeEnd>())
                {
                    if (!TryParseCommentId(rangeEnd.Id, out var rangeEndId)) continue;
                    ctx.CommentRangesSeen.Add(rangeEndId);
                    if (rangeEnd.Parent is not Paragraph) ctx.SuppressedCommentIds.Add(rangeEndId);
                }

                var blocks = new List<ComposeBlock>();
                ProjectBlockChildren(body, blocks, new ListContinuity(), ctx);

                // Alignment guard (mirrors Build()'s F-03): paragraphs enumerated but never visited by the
                // structural walk (text-box / AlternateContent-nested content) are a counted shortfall —
                // degraded loudly, never silently (F-1). Task 026 widens the hard-tier surface.
                if (ctx.VisitedParagraphs != totalParagraphs)
                {
                    ctx.AddWarning("unrendered-paragraphs", Math.Abs(totalParagraphs - ctx.VisitedParagraphs));
                }

                var comments = ProjectComments(mainPart, ctx);

                var warnings = ctx.Warnings;
                return new ComposeCanonicalModelProjection
                {
                    Status = warnings.Count == 0 ? ComposeProjectionStatus.Success : ComposeProjectionStatus.Partial,
                    Model = new ComposeContentModel { Blocks = blocks, Comments = comments },
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
            // Defensive: any unexpected projection error fails closed rather than throwing out of the
            // save path (the total-function contract — see ComposeCanonicalModelProjection remarks).
            return ComposeCanonicalModelProjection.Failed("projection-error");
        }
    }

    /// <summary>Per-container ordered-list continuity state (task 021 simplification). Every projected list
    /// item now carries its SOURCE <see cref="ComposeBlock.NumId"/>, and the renderer treats that identity as
    /// authoritative — same source numId ⇒ same rendered instance, so an interrupted/interleaved ordered run
    /// CONTINUES on render exactly as Word's per-numId counters do (retires the <c>ordered-list-continuity-lost</c>
    /// warning; review finding 020-R1 closed). <see cref="ComposeBlock.StartsNewList"/> therefore reduces to
    /// "first appearance of this numId in this container" — informative for model consumers, ignored by the
    /// renderer when <c>NumId</c> is present. Table cells get fresh state (mirrors the read walk's
    /// CloseOpenList at cell edges); transparent SDT/customXml recursion shares the parent's (paragraphs
    /// continue the flow).</summary>
    private sealed class ListContinuity
    {
        /// <summary>Every ordered numId already seen in this container — first appearance sets
        /// <see cref="ComposeBlock.StartsNewList"/>.</summary>
        public readonly HashSet<int> SeenOrderedNumIds = new();
    }

    private void ProjectBlockChildren(OpenXmlElement container, List<ComposeBlock> sink, ListContinuity lists, ModelWalkContext ctx)
    {
        foreach (var child in container.Elements())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (child)
            {
                case Paragraph p:
                    sink.Add(ProjectParagraph(p, lists, ctx));
                    break;
                case Table t:
                    var tableBlock = ProjectTable(t, ctx);
                    if (tableBlock is not null) sink.Add(tableBlock);
                    break;
                case CustomXmlBlock cxb:
                    // w:customXml block wrapper: transparent — its paragraphs are ordinary editable prose;
                    // recursing keeps the text (review finding 020-R10; dropping would be a silent loss).
                    ProjectBlockChildren(cxb, sink, lists, ctx);
                    break;
                case SdtBlock sdt:
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdt.SdtProperties))
                    {
                        // Hard-tier accept-flatten (baseline; task 026 adds the user-visible warning
                        // surface): the control's cached DISPLAY text is preserved as a plain paragraph —
                        // visible content survives, the control chrome does not. Never a hard-fail.
                        var text = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sdt);
                        if (text.Length > 0)
                        {
                            sink.Add(new ComposeBlock
                            {
                                Kind = ComposeBlockKind.Paragraph,
                                Runs = new[] { new ComposeInlineRun { Text = ctx.ClampText(text) } },
                            });
                        }
                        ctx.AddWarning("hard-tier-sdt-flattened", 1);
                    }
                    else
                    {
                        // Plain/rich-text content control: shell transparent, wrapped paragraphs stay
                        // editable — the SAME escalation-boundary rule as the read walk (ComposeOoxmlPrimitives.IsSpecialSdtControl
                        // remarks); same warning code so the two paths report consistently.
                        ctx.AddWarning("content-control", 1);
                        var sdtContent = sdt.GetFirstChild<SdtContentBlock>();
                        if (sdtContent is not null) ProjectBlockChildren(sdtContent, sink, lists, ctx);
                    }
                    break;
                case CommentRangeStart or CommentRangeEnd:
                    // Task 024: a BLOCK-level comment range anchor (between paragraphs — rare authoring
                    // shape): the model's anchor surface is inline-only, so this anchor flattens LOUDLY
                    // (the comment itself stays in ComposeContentModel.Comments; 026-shaped if it surfaces).
                    ctx.AddWarning("comment-anchor-flattened", 1);
                    break;
                case AlternateContent blockAlternate:
                    // Task 026 (FR-04): a block-level mc:AlternateContent (floating shapes/text boxes)
                    // ACCEPT-FLATTENS: the visible text of ONE branch is preserved as a degraded plain
                    // paragraph — counted `text-box-flattened`; text-free wrappers keep the loud drop.
                    var blockBoxText = ExtractTextBoxDisplayText(blockAlternate, ctx);
                    if (blockBoxText.Length > 0)
                    {
                        var clamped = ctx.ClampText(blockBoxText);
                        if (clamped.Length > 0)
                        {
                            sink.Add(new ComposeBlock
                            {
                                Kind = ComposeBlockKind.Paragraph,
                                Runs = new[] { new ComposeInlineRun { Text = clamped } },
                            });
                        }
                        ctx.AddWarning("text-box-flattened", 1);
                    }
                    else
                    {
                        ctx.AddWarning("complex-object-dropped", 1);
                    }
                    break;
                default:
                    // sectPr, bookmarks, and other non-block markup: nothing to model. (Text-box paragraphs
                    // are reached only via a Drawing/AlternateContent run — dropped there with
                    // complex-object-dropped + the unrendered-paragraphs guard.)
                    break;
            }
        }
    }

    private ComposeBlock ProjectParagraph(Paragraph p, ListContinuity lists, ModelWalkContext ctx)
    {
        ctx.VisitedParagraphs++;
        var runs = new List<ComposeInlineRun>();
        ProjectInline(p, runs, href: null, ctx);

        var paraId = p.ParagraphId?.Value?.ToUpperInvariant(); // renderer dedups/mints (AssignParaIds)
        var alignment = MapAlignment(p);

        // Uncounted-flatten closure (review finding 020-R3): the model has no indentation field (the read
        // walk renders w:ind via AppendIndentDeclarations — re-lost on save until a widening task carries
        // it).
        if (p.ParagraphProperties?.Indentation is not null) ctx.AddWarning("indentation-dropped", 1);

        // Task 025 (020-R11): paragraph-MARK revisions are MODEL data — w:pPr/w:rPr/w:del (mark pending-
        // deleted; accepting merges with the next paragraph) or w:ins (paragraph created while tracking).
        // Retires `tracked-paragraph-mark-flattened`. Both present (invalid stacking) → Deleted wins.
        // Step-9.5 F2: a MOVED paragraph's mark (w:pPr/w:rPr/w:moveFrom|w:moveTo) downgrades to the
        // matching del/ins kind — LOUD, symmetric with the run-level move downgrade. A tracked change OF
        // the mark's formatting (w:pPr/w:rPr/w:rPrChange) stays out of the thin tier — counted, never
        // silent. Dates normalize through the render-side xsd gate at CAPTURE (Step-9.5 F5), so the model
        // is canonical and the round-trip fixed point holds for degenerate source attribution.
        var markRpr = p.ParagraphProperties?.ParagraphMarkRunProperties;
        ComposeRevision? markRevision = markRpr switch
        {
            { Deleted: { } markDel } => CaptureRevision(ComposeRevisionKind.Deleted, markDel.Author?.Value, markDel.Date?.InnerText),
            { Inserted: { } markIns } => CaptureRevision(ComposeRevisionKind.Inserted, markIns.Author?.Value, markIns.Date?.InnerText),
            _ => null,
        };
        if (markRevision is null && markRpr is not null)
        {
            if (markRpr.GetFirstChild<MoveFrom>() is { } markMoveFrom)
            {
                ctx.AddWarning("tracked-move-downgraded", 1);
                markRevision = CaptureRevision(ComposeRevisionKind.Deleted, markMoveFrom.Author?.Value, markMoveFrom.Date?.InnerText);
            }
            else if (markRpr.GetFirstChild<MoveTo>() is { } markMoveTo)
            {
                ctx.AddWarning("tracked-move-downgraded", 1);
                markRevision = CaptureRevision(ComposeRevisionKind.Inserted, markMoveTo.Author?.Value, markMoveTo.Date?.InnerText);
            }
        }
        if (markRpr?.Elements().Any(e => e.LocalName == "rPrChange") == true)
        {
            ctx.AddWarning("tracked-format-change-flattened", 1);
        }

        // Task 025: a tracked paragraph-FORMATTING change (w:pPr/w:pPrChange) — identity + the previous
        // pPr carried opaquely (SDK-parse-gated at render; see ComposeFormatChange).
        ComposeFormatChange? propertiesChange = null;
        if (p.ParagraphProperties?.GetFirstChild<ParagraphPropertiesChange>() is { } pPrChange)
        {
            propertiesChange = new ComposeFormatChange
            {
                Author = pPrChange.Author?.Value ?? string.Empty,
                Date = ComposeDocumentRenderer.NormalizeXsdDateTime(pPrChange.Date?.InnerText),
                PreviousPropertiesXml = pPrChange.GetFirstChild<ParagraphPropertiesExtended>()?.OuterXml,
            };
        }

        // Task 023: an INTERIOR section break (pPr-nested w:sectPr — this paragraph ends a section) is not
        // model data; on render its content joins the FINAL section's page setup — a real pagination/
        // header-scope change, counted LOUDLY. Full multi-section modeling is a follow-up (the trailing
        // body-level sectPr is preserved by RenderIntoCarrier and is not this warning's subject).
        // EXCEPTION (review 023-F1): the 011-P1 promotion shape — the FINAL section's sectPr parked in the
        // LAST body paragraph's pPr with NO body-level sectPr (third-party generators) — loses NOTHING:
        // RenderIntoCarrier promotes a clone to body level, so warning would be a false loss report (and a
        // spurious Partial status). The predicate mirrors the renderer's promotion condition exactly.
        if (p.ParagraphProperties?.SectionProperties is not null && !IsPromotedTrailingSectPr(p))
        {
            ctx.AddWarning("section-break-flattened", 1);
        }

        // Task 023: paragraph-level page break — model data (w:pPr/w:pageBreakBefore, OnOff semantics).
        var pageBreakBefore = ComposeOoxmlPrimitives.IsOn(p.ParagraphProperties?.PageBreakBefore);

        // Classification mirrors RenderParagraph exactly: heading style wins; then a DIRECT paragraph
        // w:numPr makes a list item (style-linked heading numbering is a heading, not a list).
        var headingLevel = ComposeOoxmlPrimitives.HeadingLevel(p);
        var numPr = p.ParagraphProperties?.NumberingProperties;
        var numId = numPr?.NumberingId?.Val;

        if (headingLevel is int lvl)
        {
            // A heading carrying a DIRECT w:numPr: heading wins (read-walk rule) and the direct numbering
            // is flattened — counted, never silent (review finding 020-R3).
            if (numId is not null) ctx.AddWarning("heading-direct-numbering-dropped", 1);
            return new ComposeBlock
            {
                Kind = ComposeBlockKind.Heading,
                ParaId = paraId,
                Level = lvl,
                Runs = runs,
                Alignment = alignment,
                PageBreakBefore = pageBreakBefore,
                MarkRevision = markRevision,
                PropertiesChange = propertiesChange,
            };
        }

        if (numId is not null)
        {
            var ilvl = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
            var ordered = ResolveOrderedFromModel(numId.Value, ilvl, ctx);
            // Task 021: the SOURCE numbering-instance identity travels in the model (ComposeBlock.NumId),
            // and the renderer keys instance selection on it — same source numId ⇒ same rendered instance,
            // so interruption-continuity survives the round-trip by construction (retired the
            // ordered-list-continuity-lost warning; 020-R1/R2 closed). StartsNewList reduces to "first
            // appearance of this numId" (HashSet.Add returns true exactly then).
            var startsNew = ordered && lists.SeenOrderedNumIds.Add(numId.Value);
            return new ComposeBlock
            {
                Kind = ComposeBlockKind.ListItem,
                ParaId = paraId,
                Level = Math.Clamp(ilvl, 0, 8),
                Ordered = ordered,
                StartsNewList = startsNew,
                NumId = numId.Value,
                Runs = runs,
                Alignment = alignment,
                PageBreakBefore = pageBreakBefore,
                MarkRevision = markRevision,
                PropertiesChange = propertiesChange,
            };
        }

        // Style-linked numbering on a NON-Heading style (FR-12 — e.g. a firm template's "ClauseL1"): the
        // thin model cannot carry the CUSTOM STYLE identity the number rides on, so the number is lost on
        // this path — counted, never silent (review finding 020-R7). Task 021 deliberately scoped this OUT:
        // it carries the numbering-INSTANCE identity (ComposeBlock.NumId) for direct/heading-style numbering
        // (the §1.5 exemplar surface), while custom/localized paragraph-STYLE identity — which this number
        // rides on — is task 026's style-identity scope (with 011-P8 below).
        if (ComposeNumbering.ResolveParagraphNumbering(p, ctx.Numbering) is { StyleLinked: true })
        {
            ctx.AddWarning("style-linked-numbering-dropped", 1);
        }

        // Custom/localized paragraph-style identity (review finding 011-P8 — e.g. German "Überschrift1"
        // headings, which ComposeOoxmlPrimitives.HeadingLevel's "Heading" prefix cannot classify) cannot be carried by the thin
        // model: the render path emits Normal, so heading-ness/outline/custom look flattens — counted,
        // never silent. Localized heading-id mapping is a 021/026-shaped follow-up.
        var flattenedStyleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (!string.IsNullOrEmpty(flattenedStyleId)
            && !string.Equals(flattenedStyleId, "Normal", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(flattenedStyleId, "ListParagraph", StringComparison.OrdinalIgnoreCase))
        {
            ctx.AddWarning("paragraph-style-flattened", 1);
        }

        return new ComposeBlock
        {
            Kind = ComposeBlockKind.Paragraph,
            ParaId = paraId,
            Runs = runs,
            Alignment = alignment,
            PageBreakBefore = pageBreakBefore,
            MarkRevision = markRevision,
            PropertiesChange = propertiesChange,
        };
    }

    /// <summary>Task 024: parses an OOXML comment id attribute (decimal string per Word's convention) —
    /// a non-decimal id is outside the model's id contract and its construct flattens with a counted
    /// warning at the call site.</summary>
    private static bool TryParseCommentId(StringValue? id, out int value) =>
        int.TryParse(id?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Task 024: projects <c>word/comments.xml</c> into the model's <see cref="ComposeContentModel.Comments"/>
    /// — identity + attribution + plain text (paragraphs joined by <c>\n</c>; rich content flattens by the
    /// near-term-tier contract). The <c>Date</c> keeps the RAW authored string (InnerText) for byte-faithful
    /// re-authoring. A non-decimal id cannot join the anchor id contract — counted, skipped.
    /// </summary>
    private static IReadOnlyList<ComposeComment> ProjectComments(MainDocumentPart mainPart, ModelWalkContext ctx)
    {
        var comments = mainPart.WordprocessingCommentsPart?.Comments;
        if (comments is null)
        {
            return Array.Empty<ComposeComment>();
        }

        var result = new List<ComposeComment>();
        foreach (var comment in comments.Elements<Comment>())
        {
            // Step-9.5 F8: same resource discipline as the body walk — cancellable per comment, text
            // through the shared output budget (a comment-heavy part must not project unbounded).
            ctx.CancellationToken.ThrowIfCancellationRequested();
            if (!TryParseCommentId(comment.Id, out var id))
            {
                ctx.AddWarning("comment-flattened", 1);
                continue;
            }
            result.Add(new ComposeComment
            {
                Id = id,
                Author = comment.Author?.Value ?? string.Empty,
                Initials = comment.Initials?.Value,
                Date = comment.Date?.InnerText,
                Text = ctx.ClampText(string.Join("\n", comment.Elements<Paragraph>().Select(p => p.InnerText))),
            });
        }
        return result;
    }

    /// <summary>Review 023-F1: whether <paramref name="p"/> is the LAST direct body paragraph of a body
    /// with NO body-level <c>sectPr</c> — the 011-P1 shape whose pPr-nested <c>sectPr</c> the renderer
    /// PROMOTES to body level (nothing flattens; mirrors <c>RenderIntoCarrier</c>'s promotion predicate).</summary>
    private static bool IsPromotedTrailingSectPr(Paragraph p) =>
        p.Parent is Body body
        && !body.Elements<SectionProperties>().Any()
        && ReferenceEquals(body.Elements<Paragraph>().LastOrDefault(), p);

    /// <summary>Ordered-vs-bullet from the R4.5 <see cref="ComposeNumbering.NumberingModel"/> (override-aware via
    /// <see cref="ComposeNumbering.NumberingModel.ResolveLevel"/>), with <see cref="ResolveOrdered"/>'s tolerant posture:
    /// an unresolvable numId warns and defaults to ordered. When the exact ilvl is undefined, probes the
    /// nearest LOWER defined level first, then any HIGHER one — so a doc defining only a higher level (the
    /// read walk's FirstOrDefault fallback territory, review finding 020-R8) still classifies from a real
    /// numFmt instead of warning + defaulting, keeping the two walks from disagreeing on the same paragraph.</summary>
    private static bool ResolveOrderedFromModel(int numId, int ilvl, ModelWalkContext ctx)
    {
        var def = ctx.Numbering.ResolveLevel(numId, ilvl);
        for (var probe = ilvl - 1; def is null && probe >= 0; probe--)
        {
            def = ctx.Numbering.ResolveLevel(numId, probe);
        }
        for (var probe = ilvl + 1; def is null && probe <= 8; probe++)
        {
            def = ctx.Numbering.ResolveLevel(numId, probe);
        }
        if (def is null)
        {
            ctx.AddWarning("numbering-unresolved", 1);
            return true;
        }
        return def.NumFmt is null || def.NumFmt.Value != NumberFormatValues.Bullet;
    }

    private ComposeBlock? ProjectTable(Table table, ModelWalkContext ctx)
    {
        var rows = new List<ComposeTableRow>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<ComposeTableCell>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var cellBlocks = new List<ComposeBlock>();
                // Fresh ListContinuity: a list never continues across a cell boundary (read walk's
                // CloseOpenList at cell edges).
                ProjectBlockChildren(cell, cellBlocks, new ListContinuity(), ctx);
                cells.Add(ProjectCellFacts(cell, cellBlocks, ctx));
            }
            rows.Add(new ComposeTableRow
            {
                Cells = cells,
                RepeatAsHeaderRow = row.TableRowProperties?.GetFirstChild<TableHeader>() is not null,
            });
            // Catch-all loudness (review 022-F2): EVERY trPr child outside the modeled set counts —
            // trHeight, cantSplit, jc, hidden, cnfStyle, … — never a silent drop. Row-level tblPrEx
            // (legacy per-row table-property exceptions) likewise.
            var unmodeledRowProps = row.TableRowProperties?.ChildElements.Count(e => e is not TableHeader) ?? 0;
            if (unmodeledRowProps > 0)
            {
                ctx.AddWarning("table-formatting-flattened", unmodeledRowProps);
            }
            if (row.GetFirstChild<TablePropertyExceptions>() is not null)
            {
                ctx.AddWarning("table-formatting-flattened", 1);
            }
        }
        if (rows.Count == 0)
        {
            // Schema-degenerate w:tbl with no rows: the RENDERER skips zero-row tables (Word requires ≥1
            // row), so emitting a Table block would break the model→docx→model fixed point (review finding
            // 020-R12) — dropped, counted.
            ctx.AddWarning("empty-table-dropped", 1);
            return null;
        }
        return new ComposeBlock
        {
            Kind = ComposeBlockKind.Table,
            Table = ProjectTableFacts(table, rows, ctx),
        };
    }

    // ── task 022: table structural-fact capture (spans/merges/widths/borders/style identity) ───────
    // The CLOSED near-term set the renderer reproduces; everything else in tblPr/trPr/tcPr flattens
    // LOUDLY (one `table-formatting-flattened` count per dropped construct — F-1, never silent).

    private static ComposeTable ProjectTableFacts(Table table, List<ComposeTableRow> rows, ModelWalkContext ctx)
    {
        var tblPr = table.GetFirstChild<TableProperties>();

        // Explicit grid widths (document order); null when any gridCol lacks @w:w (renderer then emits a
        // width-less grid sized to the widest row's span total).
        IReadOnlyList<string>? gridWidths = null;
        var grid = table.GetFirstChild<TableGrid>();
        if (grid is not null)
        {
            var widths = grid.Elements<GridColumn>().Select(g => g.Width?.Value).ToList();
            if (widths.Count > 0 && widths.All(w => !string.IsNullOrEmpty(w)))
            {
                gridWidths = widths!;
            }
            else if (widths.Any(w => !string.IsNullOrEmpty(w)))
            {
                // Review 022-F7: a PARTIALLY-widthed grid is discarded wholesale (the renderer regenerates
                // a width-less grid) — counted, never silent.
                ctx.AddWarning("table-formatting-flattened", 1);
            }
        }

        // Catch-all loudness (review 022-F2): EVERY tblPr child outside the modeled set counts — tblpPr
        // (floating), jc, shd, tblInd, tblCellMar, tblCellSpacing, tblLayout (fixed-layout reflows!),
        // bidiVisual, band sizes, caption/description, … — never a silent drop. Widening is 026 scope.
        if (tblPr is not null)
        {
            var unmodeled = tblPr.ChildElements.Count(e =>
                e is not TableStyle and not TableWidth and not TableBorders and not TableLook);
            if (unmodeled > 0)
            {
                ctx.AddWarning("table-formatting-flattened", unmodeled);
            }
        }

        return new ComposeTable
        {
            Rows = rows,
            StyleId = tblPr?.TableStyle?.Val?.Value,
            Width = ProjectWidth(tblPr?.GetFirstChild<TableWidth>()),
            // ALWAYS non-null for a projected table (tri-state contract): an empty instance = borderless —
            // the renderer must NOT apply its born-in-editor border chrome to a source table.
            Borders = ProjectTableBorders(tblPr?.GetFirstChild<TableBorders>()),
            GridColumnWidthsTwips = gridWidths,
            LookHex = ProjectLookHex(tblPr?.GetFirstChild<TableLook>()),
        };
    }

    /// <summary>
    /// Review 022-F3: <c>w:tblLook</c> exists in two representations — a <c>@w:val</c> hex bitmask
    /// (transitional) and/or the six boolean attributes (strict). Val wins when present; otherwise the
    /// hex is SYNTHESIZED from the booleans (0x20 firstRow · 0x40 lastRow · 0x80 firstColumn ·
    /// 0x100 lastColumn · 0x200 noHBand · 0x400 noVBand) so style banding survives either authoring.
    /// An element carrying neither is semantically empty — nothing to carry.
    /// </summary>
    private static string? ProjectLookHex(TableLook? look)
    {
        if (look is null)
        {
            return null;
        }
        if (look.Val?.Value is { } val)
        {
            return val;
        }
        var bits = 0;
        if (look.FirstRow?.Value == true) bits |= 0x20;
        if (look.LastRow?.Value == true) bits |= 0x40;
        if (look.FirstColumn?.Value == true) bits |= 0x80;
        if (look.LastColumn?.Value == true) bits |= 0x100;
        if (look.NoHorizontalBand?.Value == true) bits |= 0x200;
        if (look.NoVerticalBand?.Value == true) bits |= 0x400;
        return bits == 0 ? null : bits.ToString("X4", CultureInfo.InvariantCulture);
    }

    private ComposeTableCell ProjectCellFacts(TableCell cell, List<ComposeBlock> blocks, ModelWalkContext ctx)
    {
        var tcPr = cell.TableCellProperties;

        var vMerge = ComposeVerticalMerge.None;
        if (tcPr?.GetFirstChild<VerticalMerge>() is { } merge)
        {
            // Per ECMA-376 a w:vMerge with no @w:val (or val="continue") CONTINUES; val="restart" starts.
            vMerge = merge.Val is not null && merge.Val.Value == MergedCellValues.Restart
                ? ComposeVerticalMerge.Restart
                : ComposeVerticalMerge.Continue;
        }

        // Catch-all loudness (review 022-F2): EVERY tcPr child outside the modeled set counts — shd,
        // tcBorders, tcMar, textDirection, hMerge (legacy horizontal merge — STRUCTURAL), noWrap,
        // tcFitText, hideMark, cnfStyle, … — never a silent drop. Widening is 026 scope.
        if (tcPr is not null)
        {
            var unmodeled = tcPr.ChildElements.Count(e =>
                e is not GridSpan and not VerticalMerge and not TableCellWidth and not TableCellVerticalAlignment);
            if (unmodeled > 0)
            {
                ctx.AddWarning("table-formatting-flattened", unmodeled);
            }
        }

        return new ComposeTableCell
        {
            Blocks = blocks,
            GridSpan = Math.Max(1, tcPr?.GetFirstChild<GridSpan>()?.Val?.Value ?? 1),
            VMerge = vMerge,
            Width = ProjectWidth(tcPr?.GetFirstChild<TableCellWidth>()),
            // Source value only — NULL when the cell carries no direct vAlign (review 022-F4: stamping an
            // explicit "top" would OVERRIDE a table-style-inherited center/bottom; the renderer's
            // source-faithful mode emits nothing for null, letting the style chain govern exactly as in
            // the source).
            VerticalAlignment = MapVerticalAlignment(tcPr?.GetFirstChild<TableCellVerticalAlignment>()),
        };
    }

    private static ComposeTableWidth? ProjectWidth(TableWidthType? width)
    {
        if (width is null)
        {
            return null;
        }
        if (width.Type is null)
        {
            // Review 022-F6: a type-less width with a numeric @w:w is Word's legacy dxa idiom — keep it
            // rather than silently dropping the width. Type-less AND value-less carries nothing.
            return width.Width?.Value is { Length: > 0 } bare
                ? new ComposeTableWidth { Type = "dxa", Value = bare }
                : null;
        }
        var type = width.Type.Value;
        return new ComposeTableWidth
        {
            Type = type == TableWidthUnitValues.Dxa ? "dxa"
                : type == TableWidthUnitValues.Pct ? "pct"
                : type == TableWidthUnitValues.Nil ? "nil"
                : "auto",
            Value = width.Width?.Value ?? "0",
        };
    }

    private static ComposeTableBorders ProjectTableBorders(TableBorders? borders) => new()
    {
        Top = ProjectBorderEdge(borders?.TopBorder),
        Left = ProjectBorderEdge(borders?.LeftBorder),
        Bottom = ProjectBorderEdge(borders?.BottomBorder),
        Right = ProjectBorderEdge(borders?.RightBorder),
        InsideHorizontal = ProjectBorderEdge(borders?.InsideHorizontalBorder),
        InsideVertical = ProjectBorderEdge(borders?.InsideVerticalBorder),
    };

    private static ComposeTableBorderEdge? ProjectBorderEdge(BorderType? edge) =>
        edge?.Val is null
            ? null
            : new ComposeTableBorderEdge
            {
                // IEnumValue.Value is the raw XML token ("single", "double", …) — the 3.x enum structs'
                // ToString() is NOT the token; the renderer re-mints the struct from this token.
                Val = ((DocumentFormat.OpenXml.IEnumValue)edge.Val.Value).Value,
                Size = edge.Size?.Value,
                Color = edge.Color?.Value,
            };

    private static string? MapVerticalAlignment(TableCellVerticalAlignment? vAlign)
    {
        if (vAlign?.Val is null)
        {
            return null;
        }
        if (vAlign.Val.Value == TableVerticalAlignmentValues.Center) return "center";
        if (vAlign.Val.Value == TableVerticalAlignmentValues.Bottom) return "bottom";
        return "top";
    }

    private void ProjectInline(OpenXmlElement container, List<ComposeInlineRun> sink, string? href, ModelWalkContext ctx, ComposeRevision? revision = null)
    {
        // Mirrors RenderInline's field-scan exactly (own ComposeOoxmlPrimitives.FieldScanState instance — the established
        // parallel-walk pattern, see ComposeOoxmlPrimitives.FieldScanState remarks).
        var field = new ComposeOoxmlPrimitives.FieldScanState();
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    if (ComposeOoxmlPrimitives.TryAdvanceFieldScan(r, field, out var fieldClosed))
                    {
                        if (fieldClosed)
                        {
                            // Task 049: the field is CARRIED when its instruction can be reproduced exactly
                            // — see TryCarryField. Otherwise it flattens to its cached RESULT text as plain
                            // prose, which is what every field did before this task: the visible value
                            // survives, the dynamic behaviour does not, and the loss is named.
                            var complexResult = ComposeOoxmlPrimitives.ExtractRunsDisplayText(field.ResultRuns);
                            var complexNested = field.MaxDepth > 1;
                            if (!TryCarryField(
                                    sink, ctx, href, revision,
                                    instruction: field.Instruction.ToString(),
                                    cachedResult: complexResult,
                                    complex: true,
                                    locked: field.Locked,
                                    dirty: field.Dirty,
                                    nested: complexNested,
                                    firstResultRun: field.ResultRuns.FirstOrDefault(),
                                    spanXml: complexNested ? TryCaptureFieldSpanXml(field.SpanRuns) : null))
                            {
                                AddPlainRun(sink, complexResult, href, ctx, revision);
                                ctx.AddWarning("field-flattened-to-text", 1);
                            }
                            field.Reset();
                        }
                        break;
                    }
                    if (ComposeOoxmlPrimitives.IsComplexObjectRun(r))
                    {
                        // Task 026 (FR-04): a TEXT-CARRYING complex object (DrawingML/VML text box — the
                        // NDA's signature blocks) ACCEPT-FLATTENS: its visible text is preserved as a
                        // degraded plain run at the anchor position, only the box chrome is lost —
                        // counted `text-box-flattened`. A text-free object (image/OLE/shape) keeps the
                        // established loud drop. Step-9.5 F3: a transitional MIXED run (own w:t text
                        // ALONGSIDE the object) keeps its direct text too — ComposeOoxmlPrimitives.ExtractRunsDisplayText walks
                        // only the run's DIRECT text children (Picture/Drawing fall through), so the box
                        // text is never doubled here.
                        var runBoxText = ExtractTextBoxDisplayText(r, ctx);
                        var directRunText = ComposeOoxmlPrimitives.ExtractRunsDisplayText(new[] { r });

                        // Task 056 (FR-A10 residual): a TEXT-FREE object — a picture, chart, shape or OLE
                        // embed — is CARRIED as its own subtree instead of being dropped. Only the text-free
                        // case: a box that carries text is accept-flattened into the paragraph as prose just
                        // above, so carrying it as well would put the same words in the document twice.
                        if (runBoxText.Length == 0
                            && TryCarryEmbeddedObjects(sink, ctx, href, revision, r, directRunText))
                        {
                            break;
                        }

                        var combined = directRunText.Length > 0 && runBoxText.Length > 0
                            ? directRunText + " " + runBoxText
                            : directRunText.Length > 0 ? directRunText : runBoxText;
                        if (combined.Length > 0)
                        {
                            AddPlainRun(sink, combined, href, ctx, revision);
                        }
                        ctx.AddWarning(runBoxText.Length > 0 ? "text-box-flattened" : "complex-object-dropped", 1);
                        break;
                    }
                    ProjectRun(r, sink, href, ctx, revision);
                    break;
                case SimpleField sf:
                    // Task 049: the compact form. Its instruction is an attribute rather than a code phase,
                    // so it is read directly — but the carryability gate is the same one the complex form
                    // uses, including the nesting exclusion (a w:fldSimple may itself contain a field).
                    var simpleResult = ComposeOoxmlPrimitives.ExtractAtomDisplayText(sf);
                    var simpleNested = sf.Descendants<SimpleField>().Any() || sf.Descendants<FieldChar>().Any();
                    if (!TryCarryField(
                            sink, ctx, href, revision,
                            instruction: sf.Instruction?.Value ?? string.Empty,
                            cachedResult: simpleResult,
                            complex: false,
                            locked: sf.FieldLock?.Value == true,
                            dirty: sf.Dirty?.Value == true,
                            nested: simpleNested,
                            firstResultRun: sf.Descendants<Run>().FirstOrDefault(),
                            spanXml: simpleNested ? TryCaptureSimpleFieldSpanXml(sf) : null))
                    {
                        AddPlainRun(sink, simpleResult, href, ctx, revision);
                        ctx.AddWarning("field-flattened-to-text", 1);
                    }
                    break;
                case Hyperlink h:
                    var resolved = ComposeOoxmlPrimitives.ResolveHyperlinkHref(h, ctx.MainPart);
                    // UAT 2026-08-26 (D-1): the internal-bookmark branch that used to sit here NULLED
                    // `resolved` and warned `internal-link-flattened`, on the premise — stated in its own
                    // comment — that "the read walk still renders it as a live #anchor href — read-path
                    // only". That premise was FALSE: ComposeEditor does `setContent(projection.html)`, so
                    // the read walk's HTML *is* the editable document. The result was a read/write
                    // asymmetry with two compounding costs:
                    //   1. The editor held `href="#Section2"` while the model held null, so
                    //      `formattingUnchanged` could NEVER match — an untouched paragraph containing a
                    //      cross-reference fell to the rebuild tier, its canonical key diverged from the
                    //      base, and the merge planned Render instead of Clone. That paragraph therefore
                    //      lost the byte-verbatim clone guarantee ON EVERY SAVE — taking any footnote ref
                    //      / inline w:sdt / text box sharing it along. That is a direct breach of the R8
                    //      invariant "untouched blocks are preserved", not merely an edited-block loss.
                    //   2. On save the "#Section2" href failed `Uri.TryCreate(..., Absolute)` in the
                    //      renderer and was reported to the user as `hyperlink-target-dropped`.
                    // Carrying the anchor is ADR-049 I-2-clean (a self-contained scalar, no markup on the
                    // wire) and the bookmark it names survives independently — see the renderer's
                    // ResolveHyperlinkRelationships for why it cannot dangle.
                    if (resolved is null && (h.Id?.Value is { Length: > 0 } || h.DocLocation?.Value is { Length: > 0 }))
                    {
                        // Unresolvable relationship, protocol-neutralized target (GPT §13 allowlist), or a
                        // docLocation-only link (Step-9.5 F9): text kept, link dropped LOUDLY (was silent).
                        ctx.AddWarning("hyperlink-target-dropped", 1);
                    }
                    ProjectInline(h, sink, resolved ?? href, ctx, revision);
                    break;
                case CommentRangeStart commentStart:
                    // Task 024: comment range anchors are MODEL data (marker runs at exact positions).
                    // A suppressed id (block-level partner, F6) flattens atomically — counted, no orphan.
                    if (TryParseCommentId(commentStart.Id, out var startId) && !ctx.SuppressedCommentIds.Contains(startId))
                    {
                        sink.Add(new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = startId } });
                    }
                    else
                    {
                        ctx.AddWarning("comment-anchor-flattened", 1);
                    }
                    break;
                case CommentRangeEnd commentEnd:
                    if (TryParseCommentId(commentEnd.Id, out var endId) && !ctx.SuppressedCommentIds.Contains(endId))
                    {
                        // The w:commentReference run is FOLDED into this End marker (the renderer authors
                        // rangeEnd + reference together — Word's canonical adjacency).
                        sink.Add(new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = endId } });
                    }
                    else
                    {
                        ctx.AddWarning("comment-anchor-flattened", 1);
                    }
                    break;
                case InsertedRun ins:
                    // Task 025: a tracked INSERTION is MODEL data — the nested runs carry the revision
                    // identity (kind/author/date) and the renderer re-authors a Word-valid w:ins wrapper
                    // (server-minted id) on the render-on-save path. Retires `tracked-insert-flattened`.
                    ProjectInline(ins, sink, href, ctx,
                        NestRevision(ComposeRevisionKind.Inserted, ins.Author?.Value, ins.Date?.InnerText, revision, ctx));
                    break;
                case DeletedRun del:
                    // Task 025: a tracked DELETION is MODEL data — pending-deleted text is carried as run
                    // TEXT tagged Deleted (the renderer authors w:del/w:delText), so the redline survives
                    // with real accept/reject in Word. Retires `tracked-delete-flattened-kept`.
                    ProjectInline(del, sink, href, ctx,
                        NestRevision(ComposeRevisionKind.Deleted, del.Author?.Value, del.Date?.InnerText, revision, ctx));
                    break;
                case MoveFromRun moveFrom:
                    // Task 025: MOVE markup downgrades to plain ins/del (moveFrom = the deletion half;
                    // accepting/rejecting both halves reproduces accept/reject of the move — the move
                    // IDENTITY is lost, counted LOUDLY; typed move carry is 026-shaped if it surfaces).
                    ctx.AddWarning("tracked-move-downgraded", 1);
                    ProjectInline(moveFrom, sink, href, ctx,
                        NestRevision(ComposeRevisionKind.Deleted, moveFrom.Author?.Value, moveFrom.Date?.InnerText, revision, ctx));
                    break;
                case MoveToRun moveTo:
                    ctx.AddWarning("tracked-move-downgraded", 1);
                    ProjectInline(moveTo, sink, href, ctx,
                        NestRevision(ComposeRevisionKind.Inserted, moveTo.Author?.Value, moveTo.Date?.InnerText, revision, ctx));
                    break;
                case SdtRun sdtRun:
                    if (ComposeOoxmlPrimitives.IsSpecialSdtControl(sdtRun.SdtProperties))
                    {
                        AddPlainRun(sink, ComposeOoxmlPrimitives.ExtractAtomDisplayText(sdtRun), href, ctx, revision);
                        ctx.AddWarning("hard-tier-sdt-flattened", 1);
                    }
                    else
                    {
                        var sdtContent = sdtRun.GetFirstChild<SdtContentRun>();
                        if (sdtContent is not null) ProjectInline(sdtContent, sink, href, ctx, revision);
                    }
                    break;
                case AlternateContent inlineAlternate:
                    // Task 026 (FR-04): inline mc:AlternateContent (the NDA's text-box signature blocks —
                    // task 004's confirmed breaker) ACCEPT-FLATTENS: the visible text of ONE branch
                    // (Choice preferred; the Fallback duplicates it) is preserved as a degraded plain run
                    // at the anchor position — counted `text-box-flattened`. Text-free wrappers keep the
                    // established loud drop.
                    var inlineBoxText = ExtractTextBoxDisplayText(inlineAlternate, ctx);
                    if (inlineBoxText.Length > 0)
                    {
                        AddPlainRun(sink, inlineBoxText, href, ctx, revision);
                        ctx.AddWarning("text-box-flattened", 1);
                    }
                    else
                    {
                        ctx.AddWarning("complex-object-dropped", 1);
                    }
                    break;
                case CustomXmlRun cxr:
                    // w:customXml inline wrapper: transparent — nested runs are ordinary editable prose;
                    // recursing keeps the text (review finding 020-R10).
                    ProjectInline(cxr, sink, href, ctx, revision);
                    break;
                default:
                    // ParagraphProperties, bookmarks, proofErr, move-range markers (covered by the
                    // per-container `tracked-move-downgraded` count), etc. — no inline content.
                    break;
            }
        }

        if (field.Depth > 0)
        {
            // Unterminated / container-spanning field (a w:fldChar begin with no end in this container —
            // ComposeOoxmlPrimitives.FieldScanState's documented per-container simplification): flush the accumulated RESULT text
            // rather than discarding it, and count the anomaly (review finding 020-R6 — this walk's
            // never-silent contract is stronger than the read walk's).
            AddPlainRun(sink, ComposeOoxmlPrimitives.ExtractRunsDisplayText(field.ResultRuns), href, ctx, revision);
            ctx.AddWarning("field-unterminated", 1);
        }
    }

    /// <summary>Task 025: builds the revision context a tracked container's children project under. When
    /// containers STACK (e.g. a <c>w:del</c> inside a <c>w:ins</c> — text inserted then deleted, both
    /// tracked), the model's single per-run revision cannot represent both layers: the INNERMOST wins
    /// (for the common ins⊃del the surviving Deleted layer keeps accept-the-deletion working; rejecting
    /// it settles the text instead of restoring the pending-insert state) — counted LOUDLY, never silent.
    /// This is the R4 "barfoo" warned-flatten baseline pending operator sign-off.</summary>
    private static ComposeRevision NestRevision(ComposeRevisionKind kind, string? author, string? date, ComposeRevision? outer, ModelWalkContext ctx)
    {
        if (outer is not null)
        {
            ctx.AddWarning("tracked-nested-revision-simplified", 1);
        }
        return CaptureRevision(kind, author, date);
    }

    /// <summary>Step-9.5 F5: revision facts enter the model in CANONICAL form — the date passes the same
    /// xsd:dateTime gate the renderer applies (junk/empty → null at capture), so project → render →
    /// re-project is a fixed point even for degenerate source attribution, and record equality (the
    /// renderer's wrapper-grouping key) is not split by empty-vs-null noise.</summary>
    private static ComposeRevision CaptureRevision(ComposeRevisionKind kind, string? author, string? date) =>
        new() { Kind = kind, Author = author ?? string.Empty, Date = ComposeDocumentRenderer.NormalizeXsdDateTime(date) };

    /// <summary>
    /// Task 026 (FR-04 graceful degradation — the NDA breaker): the visible TEXT inside a text-box
    /// construct (DrawingML <c>wps:txbx</c>, VML <c>v:textbox</c>, or an <c>mc:AlternateContent</c>
    /// wrapper), extracted for ACCEPT-FLATTEN — the box chrome is unrepresentable, but its text is
    /// user-visible legal content (the NDA's signature blocks) and MUST NOT drop with it.
    /// <c>mc:AlternateContent</c> duplicates the SAME box across the Choice (DrawingML) and Fallback
    /// (VML) branches — exactly ONE branch is extracted (Choice preferred) or the text (and its
    /// duplicated <c>w14:paraId</c>s — the NDA's dup-id class) would double. Box paragraphs join with a
    /// single space (paragraph structure inside the box is part of the degraded chrome — counted via the
    /// call site's <c>text-box-flattened</c>). Extracted paragraphs count as VISITED so the
    /// unrendered-paragraphs guard does not double-report content that IS (degradedly) represented.
    /// </summary>
    private static string ExtractTextBoxDisplayText(OpenXmlElement construct, ModelWalkContext ctx)
    {
        var scope = construct;
        if (construct is AlternateContent)
        {
            var choice = construct.Elements<AlternateContentChoice>()
                .FirstOrDefault(c => c.Descendants<TextBoxContent>().Any());
            var fallback = construct.Elements<AlternateContentFallback>()
                .FirstOrDefault(f => f.Descendants<TextBoxContent>().Any());
            scope = (OpenXmlElement?)choice ?? fallback ?? construct;
        }

        if (!scope.Descendants<TextBoxContent>().Any())
        {
            return string.Empty; // not a text-carrying construct (pure image/shape/OLE)
        }

        // Step-9.5 F2 (026): the UNCHOSEN AlternateContent branches duplicate the chosen one — their
        // paragraphs are REPRESENTED (via the chosen branch's extraction), so they count as visited too,
        // or the unrendered-paragraphs guard would falsely report the dedup'd Fallback as lost content
        // (the NDA fired ×3 exactly this way).
        if (!ReferenceEquals(scope, construct))
        {
            ctx.VisitedParagraphs += construct.Descendants<Paragraph>().Count() - scope.Descendants<Paragraph>().Count();
        }

        var sb = new StringBuilder();
        foreach (var paragraph in scope.Descendants<Paragraph>())
        {
            ctx.VisitedParagraphs++;
            // Step-9.5 F1 (026): each run belongs to its NEAREST paragraph — a nested paragraph
            // (box-inside-box; a branch wrapping the box run in its own w:p) would otherwise emit its
            // runs twice (once via the outer paragraph's deep walk, once via its own).
            var text = ComposeOoxmlPrimitives.ExtractRunsDisplayText(
                paragraph.Descendants<Run>().Where(r => ReferenceEquals(r.Ancestors<Paragraph>().First(), paragraph)));
            if (text.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(text);
        }
        return sb.ToString();
    }

    private void ProjectRun(Run run, List<ComposeInlineRun> sink, string? href, ModelWalkContext ctx, ComposeRevision? revision = null)
    {
        var rPr = run.RunProperties;
        var bold = ComposeOoxmlPrimitives.IsOn(rPr?.Bold);
        var italic = ComposeOoxmlPrimitives.IsOn(rPr?.Italic);
        var underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None;

        // Task 025: a tracked run-formatting change (w:rPr/w:rPrChange) — identity + the previous rPr
        // carried opaquely (SDK-parse-gated at render). Attached to the FIRST flushed model run only, so a
        // page-break split does not duplicate the change record.
        var formatChange = CaptureRunFormatChange(rPr);

        var sb = new StringBuilder();
        var strikeWarned = false;

        // Flushes the accumulated text as one model run (task 023: page breaks split a source run at the
        // break's exact inline position, so the text before/after lands in separate model runs and the
        // break run sits between them).
        void FlushText()
        {
            if (sb.Length == 0) return;
            if (ComposeOoxmlPrimitives.IsOn(rPr?.Strike) && !strikeWarned)
            {
                // The model has no strikethrough mark (025 models real deletions; decorative strike is out
                // of the thin tier) — text kept, decoration dropped, counted once per source run.
                ctx.AddWarning("strikethrough-flattened", 1);
                strikeWarned = true;
            }
            var text = ctx.ClampText(sb.ToString());
            sb.Clear();
            if (text.Length == 0) return;
            sink.Add(new ComposeInlineRun { Text = text, Bold = bold, Italic = italic, Underline = underline, Href = href, Revision = revision, FormatChange = formatChange });
            formatChange = null; // first-flush-only (see capture above)
        }

        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                case DeletedText dt:
                    sb.Append(dt.Text); // kept — see DeletedRun's no-text-loss rationale
                    break;
                case TabChar:
                case PositionalTab:
                    // Task 048: a tab is model data now (ComposeInlineRun.IsTab), emitted at its exact inline
                    // position — mirroring the break markers below. It was previously flattened to a space
                    // with a counted warning, so any edit to the paragraph collapsed its alignment:
                    // definitions lists, signature blocks and table-of-contents lines are all held together by
                    // exactly these tabs. Budget-guarded like the breaks (a clipped projection must not trail
                    // stray tabs).
                    //
                    // w:ptab (PositionalTab) degrades to a plain w:tab, as it did when both flattened to the
                    // same space. Its absolute-position attributes are not modeled — recorded on the residual
                    // loss list rather than silently implied to round-trip.
                    FlushText();
                    if (ctx.HasOutputBudget)
                    {
                        sink.Add(new ComposeInlineRun { IsTab = true, Revision = revision });
                    }
                    break;
                case Break pageBreak when pageBreak.Type is not null && pageBreak.Type.Value == BreakValues.Page:
                    // Task 023: a MANUAL PAGE BREAK is model data (ComposeInlineRun.IsPageBreak) — emitted
                    // at its exact inline position, no longer a line-break-flattened space. Once the output
                    // budget is exhausted the break is dropped with the (already-counted) resource-limit
                    // degradation — a clipped projection must not trail blank pages (review 023-F4).
                    FlushText();
                    if (ctx.HasOutputBudget)
                    {
                        // Task 025: the break carries the run's revision context (a page break can itself
                        // be part of a tracked insertion/deletion — the renderer groups it into the
                        // wrapper like any other run).
                        sink.Add(new ComposeInlineRun { IsPageBreak = true, Revision = revision });
                    }
                    break;
                case Break:
                case CarriageReturn:
                    // Task 046: a SOFT line/column break is model data too (ComposeInlineRun.IsLineBreak),
                    // emitted at its exact inline position — mirroring the manual page break above. It was
                    // previously flattened to a space with a counted warning, which meant any edit to the
                    // paragraph collapsed its line structure: address blocks, party blocks and signature
                    // blocks all lost their layout. Budget-guarded like the page break, for the same reason
                    // (a clipped projection must not trail stray breaks).
                    FlushText();
                    if (ctx.HasOutputBudget)
                    {
                        sink.Add(new ComposeInlineRun { IsLineBreak = true, Revision = revision });
                    }
                    break;
                case NoBreakHyphen:
                    sb.Append('‑');
                    break;
                case SymbolChar sym:
                    // Task 048: a symbol is model data now (ComposeInlineRun.Symbol) — the font + code point
                    // verbatim, NOT the glyph the reader resolved for display. That distinction is the whole
                    // point: § in a legal document is usually Symbol-font F0A7, and re-authoring the resolved
                    // look-alike (or, for an unmapped code point, the U+FFFD placeholder) is a wrong glyph in
                    // a legal document — the exact failure ComposeOoxmlPrimitives.ResolveSymbolGlyph's curation exists to avoid.
                    //
                    // The unmapped-symbol-char warning still fires: it describes the READ (what the editor
                    // shows the user), which is unchanged. The WRITE is now lossless either way, which is why
                    // "symbol-flattened" left ReportableConstructs.
                    FlushText();
                    if (ctx.HasOutputBudget)
                    {
                        ComposeOoxmlPrimitives.ResolveSymbolGlyph(sym, out var symMapped);
                        if (!symMapped) ctx.AddWarning("unmapped-symbol-char", 1);
                        sink.Add(new ComposeInlineRun
                        {
                            Symbol = new ComposeSymbol
                            {
                                Font = sym.Font?.Value ?? string.Empty,
                                CharCode = sym.Char?.Value ?? string.Empty,
                            },
                            Revision = revision,
                        });
                    }
                    break;
                case Ruby ruby:
                    sb.Append(ComposeOoxmlPrimitives.ExtractRunsDisplayText(ComposeOoxmlPrimitives.RubyBaseRuns(ruby)));
                    ctx.AddWarning("ruby-phonetic-guide-dropped", 1);
                    break;
                case CommentReference commentRef:
                    // Task 024: a reference whose range exists in the body (pre-scanned — F5, order-
                    // independent) is FOLDED into the End marker (the renderer re-authors it there). A
                    // BARE reference (point comment, no range) projects as an adjacent Start+End pair at
                    // this exact position. A SUPPRESSED id (F6) flattens with its range — counted.
                    if (TryParseCommentId(commentRef.Id, out var pointId))
                    {
                        if (ctx.SuppressedCommentIds.Contains(pointId))
                        {
                            ctx.AddWarning("comment-anchor-flattened", 1);
                        }
                        else if (!ctx.CommentRangesSeen.Contains(pointId))
                        {
                            FlushText();
                            sink.Add(new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = pointId } });
                            sink.Add(new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = pointId } });
                        }
                    }
                    break;
                case FootnoteReference:
                    ctx.AddWarning("unrepresented-footnote-reference", 1);
                    break;
                case EndnoteReference:
                    ctx.AddWarning("unrepresented-endnote-reference", 1);
                    break;
                case AlternateContent runAlternate:
                    // Task 026 (FR-04): run-nested mc:AlternateContent (ComposeOoxmlPrimitives.IsComplexObjectRun only sees a
                    // DIRECT w:drawing/w:object/w:pict child, so the wrapped form lands here) — same
                    // accept-flatten as the inline case: text preserved at the anchor position, chrome
                    // dropped loudly.
                    FlushText();
                    var nestedBoxText = ExtractTextBoxDisplayText(runAlternate, ctx);
                    if (nestedBoxText.Length > 0)
                    {
                        AddPlainRun(sink, nestedBoxText, href, ctx, revision);
                        ctx.AddWarning("text-box-flattened", 1);
                    }
                    else
                    {
                        ctx.AddWarning("complex-object-dropped", 1);
                    }
                    break;
                default:
                    break;
            }
        }

        FlushText(); // trailing text after the last break (or the whole run when no break split it)
    }

    /// <summary>
    /// Task 049 (FR-A10 residual): adds the field as a <see cref="ComposeInlineRun.Field"/> marker run when
    /// it can be reproduced EXACTLY, and returns <c>false</c> when it cannot — the caller then flattens it
    /// to its cached display text exactly as every field did before this task.
    /// </summary>
    /// <remarks>
    /// <para><b>The gate is structural, not a keyword allow-list.</b> The obvious-looking alternative was to
    /// carry <c>PAGE</c>/<c>DATE</c> (harmless on re-evaluation) and freeze <c>REF</c>/<c>PAGEREF</c> (which
    /// show Word's broken-reference text if their target bookmark did not survive). Three findings ruled
    /// that out, recorded in <c>notes/049-field-carry-decisions.md</c>:</para>
    /// <list type="number">
    /// <item><description><b>The target survives.</b> Bookmarks are carried since task 041 — an untouched
    /// block is cloned verbatim and an edited one gets <c>ComposeBlockMerge.CarryBookmarks</c>. The
    /// renderer's own 011-P4/P9 remark ("the model does not carry bookmarks") predates that and has been
    /// corrected. So the hazard the allow-list existed to dodge is closed, and measured:
    /// <c>ComposeFieldCarrySeamTests.EditedBookmarkParagraph_StillCarriesTheTarget_SoACarriedRefResolves</c>.</description></item>
    /// <item><description><b>A keyword allow-list makes one document behave two ways.</b> Freezing the
    /// <c>DATE</c> in the paragraph a user edited while the other 39 pages keep live <c>DATE</c> fields is
    /// an inconsistency nothing on screen explains — worse than either uniform outcome.</description></item>
    /// <item><description><b>Freezing is not the null action.</b> A flattened <c>REF</c> keeps printing
    /// "Section 4" after the agreement renumbers to 5. A visible broken reference is a worse-looking failure
    /// and a better one: silence in a legal document is the failure nobody catches.</description></item>
    /// </list>
    /// <para>What genuinely cannot be reproduced is excluded here, and only that: a field with no
    /// instruction at all. A field whose begin/end straddle paragraphs never closes, so it never reaches
    /// this method — it keeps its own <c>field-unterminated</c> anomaly on the read side and flattens on
    /// the write side.</para>
    /// <para><b>Task 058 — the nested case takes the other door.</b> A NESTED field still has no single
    /// recoverable instruction (the outer scan's accumulation is a concatenation of two fields' code
    /// phases), so it does not take the instruction path above and never will. It is carried instead by
    /// <see cref="ComposeField.SpanXml"/>: the span's own OOXML, captured by
    /// <see cref="TryCaptureFieldSpanXml"/> and re-emitted verbatim. Nothing about the instruction argument
    /// changes — the field is carried by not being taken apart.</para>
    /// <para><b>Run properties come from the field's RESULT run</b> — a cross-reference is routinely bold or
    /// italic, and that formatting lives there. Same rule as <c>IsTab</c>: the marker replaces the content,
    /// never the properties.</para>
    /// </remarks>
    private static bool TryCarryField(
        List<ComposeInlineRun> sink,
        ModelWalkContext ctx,
        string? href,
        ComposeRevision? revision,
        string instruction,
        string cachedResult,
        bool complex,
        bool locked,
        bool dirty,
        bool nested,
        Run? firstResultRun,
        string? spanXml = null)
    {
        if (!ctx.HasOutputBudget)
        {
            return false;
        }

        if (nested)
        {
            // Task 058: a nested field has no single instruction, so the instruction carry above cannot
            // describe it — and task 049 was right that inventing one would author a different field. It is
            // carried by its own OOXML instead (see ComposeField.SpanXml). Instruction is left EMPTY on
            // purpose: if the render-time gate refuses the span, the renderer finds nothing to author and
            // flattens to the cached result — today's outcome, never a substitution.
            if (spanXml is null)
            {
                return false;
            }

            var nestedRPr = firstResultRun?.RunProperties;
            sink.Add(new ComposeInlineRun
            {
                Field = new ComposeField
                {
                    Instruction = string.Empty,
                    SpanXml = spanXml,
                    CachedResult = ctx.ClampText(cachedResult),
                    Complex = complex,
                    Locked = locked,
                    Dirty = dirty,
                },
                Bold = ComposeOoxmlPrimitives.IsOn(nestedRPr?.Bold),
                Italic = ComposeOoxmlPrimitives.IsOn(nestedRPr?.Italic),
                Underline = nestedRPr?.Underline is { Val: not null } nu && nu.Val!.Value != UnderlineValues.None,
                Href = href,
                Revision = revision,
            });
            return true;
        }

        if (string.IsNullOrWhiteSpace(instruction))
        {
            return false;
        }

        var rPr = firstResultRun?.RunProperties;

        sink.Add(new ComposeInlineRun
        {
            Field = new ComposeField
            {
                Instruction = instruction,
                // Clamped like any other projected text: the result is document prose and shares the same
                // output budget. The INSTRUCTION is never clamped — a truncated instruction is a different
                // field, so an oversized one is refused above rather than shortened.
                CachedResult = ctx.ClampText(cachedResult),
                Complex = complex,
                Locked = locked,
                Dirty = dirty,
            },
            Bold = ComposeOoxmlPrimitives.IsOn(rPr?.Bold),
            Italic = ComposeOoxmlPrimitives.IsOn(rPr?.Italic),
            Underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None,
            Href = href,
            Revision = revision,
        });

        return true;
    }

    /// <summary>
    /// Task 058 (FR-A10 residual): captures a NESTED <c>w:fldChar</c> field span as the verbatim OOXML the
    /// renderer re-emits, or <c>null</c> when the span cannot be captured safely and the field keeps
    /// flattening.
    /// </summary>
    /// <remarks>
    /// <para><b>The contiguity check is the whole safety argument.</b> The scan consumes RUNS, but the
    /// container it walks may hold other children between them — a <c>w:bookmarkStart</c>, a
    /// <c>w:commentRangeStart</c>, a <c>w:proofErr</c>, a <c>w:hyperlink</c> — and each of those is emitted
    /// by its OWN arm of <see cref="ProjectInline"/>, at its own position. Capturing just the runs of such a
    /// span would carry the field while the interleaved element was emitted somewhere else, silently
    /// reordering the paragraph. A span whose runs are not consecutive siblings is therefore refused and
    /// keeps today's flatten — a smaller, already-described loss than a paragraph whose parts moved.</para>
    /// <para><b>A holder <c>w:p</c>, not a bare fragment.</b> The captured runs are cloned into a fresh
    /// paragraph and that paragraph's <c>OuterXml</c> is what travels: the SDK emits the namespace
    /// declarations the fragment needs on the holder (the same mechanism <see cref="ComposeEmbeddedObject"/>
    /// relies on), and a single root means ONE parse gate at render serves both this and the
    /// <c>w:fldSimple</c> form.</para>
    /// <para>Capped by the shared opaque-carry limit and REFUSED rather than truncated over it — half a
    /// field is not the construct the document contained.</para>
    /// </remarks>
    private static string? TryCaptureFieldSpanXml(IReadOnlyList<Run> spanRuns)
    {
        if (spanRuns.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < spanRuns.Count - 1; i++)
        {
            if (!ReferenceEquals(spanRuns[i].NextSibling(), spanRuns[i + 1]))
            {
                return null;
            }
        }

        return CaptureInHolderParagraph(spanRuns);
    }

    /// <summary>
    /// Task 058: the compact form's capture — a <c>w:fldSimple</c> that itself contains a field is ONE
    /// element, so there is no contiguity question to answer; it is cloned into the same holder shape as the
    /// complex span so both forms meet the same render-time gate.
    /// </summary>
    private static string? TryCaptureSimpleFieldSpanXml(SimpleField field) =>
        CaptureInHolderParagraph(new OpenXmlElement[] { field });

    private static string? CaptureInHolderParagraph(IEnumerable<OpenXmlElement> children)
    {
        var holder = new Paragraph();
        foreach (var child in children)
        {
            holder.AppendChild(child.CloneNode(true));
        }

        var xml = holder.OuterXml;
        return xml.Length == 0 || xml.Length > ComposeDocumentRenderer.MaxOpaqueCarryXmlChars ? null : xml;
    }

    /// <summary>
    /// Task 056 (FR-A10 residual): captures a run's embedded objects (<c>w:drawing</c> / <c>w:object</c> /
    /// <c>w:pict</c>) as <see cref="ComposeInlineRun.EmbeddedObject"/> marker runs, and returns <c>false</c>
    /// when they cannot be carried — the caller then drops them exactly as every object did before this task.
    /// </summary>
    /// <remarks>
    /// <para><b>The subtree is carried VERBATIM rather than modelled.</b> A <c>w:drawing</c> is a DrawingML
    /// document in its own right (extents, effect extents, frame locks, a <c>pic:pic</c> with fill, geometry
    /// and transform — or a chart reference, or an entire VML shape). Any typed model of that would silently
    /// discard every property it failed to enumerate, which is the exact failure this project exists to end.
    /// Carrying the bytes preserves properties nobody enumerated, for the same reason cloning an untouched
    /// block does. What makes that SAFE is the renderer's two gates — the shared SDK parse gate and the
    /// relationship-resolution check — not anything asserted here.</para>
    /// <para><b>All or nothing per run.</b> A run with two objects where only one is carryable would emit
    /// one and drop the other while the merge's count reported a single loss with no way to say WHICH. The
    /// whole run falls back to the flatten instead, which is a state the taxonomy already describes.</para>
    /// <para><b>Ordering caveat, stated rather than discovered.</b> A TRANSITIONAL run carrying its own
    /// <c>w:t</c> text ALONGSIDE the object (the 026 F3 shape) emits the text first and the object second,
    /// regardless of their order inside the source run. The alternative — walking the run's children to
    /// interleave them exactly — buys correct ordering for a shape the corpus does not contain, at the cost
    /// of a second content walk in the hottest method in this file.</para>
    /// </remarks>
    private static bool TryCarryEmbeddedObjects(
        List<ComposeInlineRun> sink,
        ModelWalkContext ctx,
        string? href,
        ComposeRevision? revision,
        Run run,
        string directRunText)
    {
        if (!ctx.HasOutputBudget)
        {
            return false;
        }

        // The SAME carryability rule the base-side carry applies (ComposeBlockMerge.IsCarryableEmbeddedObject
        // — text boxes excluded, because their text is accept-flattened into the paragraph above and
        // carrying the box as well would put the same words in the document twice). One rule, one place: if
        // the two sides could disagree, the disagreement would show up as a DUPLICATED sentence in a saved
        // legal document, which is a bad way to learn that a boolean drifted.
        var objects = run.Elements().Where(ComposeBlockMerge.IsCarryableEmbeddedObject).ToList();
        if (objects.Count == 0)
        {
            return false;
        }

        var rPr = run.RunProperties;
        var carried = new List<ComposeInlineRun>(objects.Count);
        foreach (var element in objects)
        {
            var xml = element.OuterXml;
            if (xml.Length == 0 || xml.Length > ComposeDocumentRenderer.MaxOpaqueCarryXmlChars)
            {
                // Over the shared opaque-carry cap (or unserializable). Refused, never truncated — a
                // truncated subtree is not the construct the document contained.
                return false;
            }

            carried.Add(new ComposeInlineRun
            {
                EmbeddedObject = new ComposeEmbeddedObject { Xml = xml },
                Bold = ComposeOoxmlPrimitives.IsOn(rPr?.Bold),
                Italic = ComposeOoxmlPrimitives.IsOn(rPr?.Italic),
                Underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None,
                Href = href,
                Revision = revision,
            });
        }

        AddPlainRun(sink, directRunText, href, ctx, revision);
        sink.AddRange(carried);
        return true;
    }

    private static void AddPlainRun(List<ComposeInlineRun> sink, string text, string? href, ModelWalkContext ctx, ComposeRevision? revision = null)
    {
        if (text.Length == 0) return;
        var clamped = ctx.ClampText(text);
        if (clamped.Length == 0) return;
        sink.Add(new ComposeInlineRun { Text = clamped, Href = href, Revision = revision });
    }

    /// <summary>Task 025: captures a run's <c>w:rPrChange</c> (tracked formatting change) — identity plus
    /// the previous-<c>rPr</c> child carried as OuterXml (see <see cref="ComposeFormatChange"/>). Null when
    /// the run carries no change record.</summary>
    private static ComposeFormatChange? CaptureRunFormatChange(RunProperties? rPr)
    {
        var change = rPr?.GetFirstChild<RunPropertiesChange>();
        if (change is null) return null;
        return new ComposeFormatChange
        {
            Author = change.Author?.Value ?? string.Empty,
            Date = ComposeDocumentRenderer.NormalizeXsdDateTime(change.Date?.InnerText),
            PreviousPropertiesXml = change.GetFirstChild<PreviousRunProperties>()?.OuterXml,
        };
    }

    private static ComposeParagraphAlignment MapAlignment(Paragraph p)
    {
        var j = p.ParagraphProperties?.Justification?.Val;
        if (j is null) return ComposeParagraphAlignment.Default;
        if (j.Value == JustificationValues.Center) return ComposeParagraphAlignment.Center;
        if (j.Value == JustificationValues.Right) return ComposeParagraphAlignment.Right;
        if (j.Value == JustificationValues.Both || j.Value == JustificationValues.Distribute) return ComposeParagraphAlignment.Justify;
        if (j.Value == JustificationValues.Left) return ComposeParagraphAlignment.Left;
        return ComposeParagraphAlignment.Default; // start/end/highKashida/… — inherit from style
    }

    /// <summary>Per-call state for the canonical-model walk: the main part (hyperlink relationships), the
    /// R4.5 numbering model, counted warnings (codes only — Tier-1 safe), and the output-text budget
    /// (mirrors <see cref="MaxOutputChars"/>; once exhausted, further text is dropped with exactly ONE
    /// <c>resource-limit-output</c> warning — <c>Count</c> stays 1, review finding 020-R13 — rather than
    /// aborting the whole projection).</summary>
    private sealed class ModelWalkContext
    {
        private readonly Dictionary<string, int> _warnings = new(StringComparer.Ordinal);
        private int _textBudget = MaxOutputChars;
        private bool _outputWarned;

        public ModelWalkContext(MainDocumentPart mainPart, ComposeNumbering.NumberingModel numbering, CancellationToken cancellationToken)
        {
            MainPart = mainPart;
            Numbering = numbering;
            CancellationToken = cancellationToken;
        }

        public MainDocumentPart MainPart { get; }
        public ComposeNumbering.NumberingModel Numbering { get; }
        public CancellationToken CancellationToken { get; }

        /// <summary>Paragraphs the structural walk actually visited — compared against the body's total
        /// <c>Descendants&lt;Paragraph&gt;()</c> count for the unrendered-paragraphs guard (F-03 parity).</summary>
        public int VisitedParagraphs { get; set; }

        /// <summary>Task 024: comment ids with range markers in the body (PRE-SCANNED before the walk,
        /// Step-9.5 F5 — order-independent) — a <c>w:commentReference</c> for a seen id is folded into the
        /// End marker; an UNSEEN id is a POINT comment and projects its own adjacent Start+End pair.</summary>
        public HashSet<int> CommentRangesSeen { get; } = new();

        /// <summary>Task 024 Step-9.5 F6: comment ids suppressed ATOMICALLY because one of their range
        /// elements is BLOCK-level (unrepresentable) — the inline partner flattens too (counted), never an
        /// orphan start/end.</summary>
        public HashSet<int> SuppressedCommentIds { get; } = new();

        public IReadOnlyList<ComposeProjectionWarning> Warnings =>
            _warnings.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new ComposeProjectionWarning(kv.Key, kv.Value))
                .ToList();

        public void AddWarning(string code, int count)
        {
            _warnings.TryGetValue(code, out var existing);
            _warnings[code] = existing + count;
        }

        /// <summary>Review 023-F4: whether the output-text budget still has room — page-break runs stop
        /// being emitted once it is exhausted (a clipped projection must not trail blank pages).</summary>
        public bool HasOutputBudget => _textBudget > 0;

        public string ClampText(string text)
        {
            if (_textBudget <= 0)
            {
                WarnOutputLimitOnce();
                return string.Empty;
            }
            if (text.Length <= _textBudget)
            {
                _textBudget -= text.Length;
                return text;
            }
            var clip = _textBudget;
            // Never split a surrogate pair at the clip boundary — a lone high surrogate would survive the
            // renderer's control-char sanitizer and make the rendered package unserializable (review
            // finding 020-R14).
            if (char.IsHighSurrogate(text[clip - 1])) clip--;
            var clipped = text[..clip];
            _textBudget = 0;
            WarnOutputLimitOnce();
            return clipped;
        }

        private void WarnOutputLimitOnce()
        {
            if (_outputWarned) return;
            _outputWarned = true;
            AddWarning("resource-limit-output", 1);
        }
    }

    // ── build context (per-call render state) ────────────────────────────────────────────────────────

    private sealed class BuildContext
    {
        private readonly StringBuilder _sb = new(4096);
        private readonly Dictionary<Paragraph, string> _idByParagraph;
        private readonly Dictionary<Paragraph, ComposeNumbering.ParagraphNumberingRef> _numberingByParagraph;
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
            ComposeNumbering.NumberingModel numbering, Dictionary<Paragraph, ComposeNumbering.ParagraphNumberingRef> numberingByParagraph,
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
        public ComposeNumbering.NumberingModel Numbering { get; }

        public IReadOnlyList<ComposeProjectionWarning> Warnings =>
            _warnings.Select(kv => new ComposeProjectionWarning(kv.Key, kv.Value)).ToList();

        public bool TryGetParaId(Paragraph p, out string paraId) => _idByParagraph.TryGetValue(p, out paraId!);

        /// <summary>The numbering source resolved for <paramref name="p"/> in Pass 1 (task 030) — direct
        /// `w:numPr` or style-linked via `pStyle` (FR-12). False when <paramref name="p"/> is not a
        /// numbered paragraph.</summary>
        public bool TryGetParagraphNumbering(Paragraph p, out ComposeNumbering.ParagraphNumberingRef numbering) =>
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
        /// <param name="extraClass">
        /// Task 048: an additional class on the span, so a RENDERABLE atom keeps the exact appearance it had
        /// before it became one (the tab's existing <c>compose-tab</c> rule). Styling only — never identity.
        /// </param>
        /// <param name="dataAttributes">
        /// Task 048: extra <c>data-*</c> attributes carrying an atom's self-describing payload back to the
        /// client, so the client can return it on save without ever handling OOXML (ADR-049 I-2). Today only
        /// <c>w:sym</c>'s font + code point use this. Names MUST be literal <c>data-*</c> tokens; values are
        /// attribute-escaped.
        /// </param>
        public void AppendAtom(
            ComposeAtomKind kind,
            string? displayText,
            string? extraClass = null,
            (string Name, string Value)[]? dataAttributes = null)
        {
            Append("<span class=\"compose-atom");
            if (!string.IsNullOrEmpty(extraClass))
            {
                Append(" ");
                Append(extraClass);
            }
            Append("\" data-atom-kind=\"");
            Append(AtomKindToken(kind));
            Append("\"");
            foreach (var (name, value) in dataAttributes ?? Array.Empty<(string, string)>())
            {
                Append(" ");
                Append(name);
                Append("=\"");
                AppendEscapedAttr(value);
                Append("\"");
            }
            Append(" contenteditable=\"false\">");
            if (!string.IsNullOrEmpty(displayText)) AppendEscaped(displayText);
            Append("</span>");
        }

        private static string AtomKindToken(ComposeAtomKind kind) => kind switch
        {
            ComposeAtomKind.Sdt => "sdt",
            ComposeAtomKind.Field => "field",
            ComposeAtomKind.ComplexObject => "object",
            ComposeAtomKind.Tab => "tab",
            ComposeAtomKind.Symbol => "symbol",
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
