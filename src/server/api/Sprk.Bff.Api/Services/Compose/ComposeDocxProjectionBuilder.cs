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
    internal const int MaxParagraphs = 100_000;
    internal const int MaxOutputChars = 16_000_000;

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
                    offsetTable.Add(ComposeParaOffsetMapBuilder.BuildParaOffsetMap(paragraphs[i], id));

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







    // ── inline (runs / marks / hyperlinks / revision flattening) ─────────────────────────────────────

    private void RenderInline(OpenXmlElement container, BuildContext ctx)
    {
        // FR-02 (task 012): mirrors ComposeParaOffsetMapBuilder.CollectRunBoundaries' field-scan exactly (its own ComposeOoxmlPrimitives.FieldScanState
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
                    // invisible to every coordinate space: ComposeParaOffsetMapBuilder.RunEditorLength already counted a tab as 1, and the
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
        AppendSpacingDeclarations(p, ref decls);

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
    /// <summary>
    /// UAT round 2 (spaarkeai-compose-r8, 2026-09-02) — emits the paragraph's OWN <c>w:spacing</c> as CSS so
    /// the editor shows the document's real line spacing instead of a generic default.
    ///
    /// <para><b>READ PATH ONLY.</b> This carries spacing OUT for display; it adds no content-model field and
    /// the renderer still never authors <c>w:spacing</c>. That asymmetry is deliberate: spacing on an
    /// untouched block survives by cloning, and on an EDITED block it survives as an unmodeled property
    /// through <c>ComposeBlockMerge.InheritProperties</c>. The moment the model owned spacing, that
    /// inheritance rule would have to change, and getting it wrong flattens spacing on every edited
    /// paragraph — the same shape as the `paragraph-style-flattened` defect this release fixed. Making
    /// spacing EDITABLE is a separate, larger step and is scoped with the numbering work.</para>
    ///
    /// <para><b>Line rule mapping.</b> <c>w:line</c> means different things depending on <c>w:lineRule</c>:
    /// <c>auto</c> (the common case) is a MULTIPLE in 240ths of a line, so 360 = 1.5× — emitted unitless so
    /// it scales with the element's own font size. <c>exact</c>/<c>atLeast</c> are TWIPS, an absolute
    /// height, emitted in points. Treating them alike would render a 1.5-spaced paragraph at 18pt leading
    /// or an exact-18pt one at 18× line height — both badly wrong, in opposite directions.</para>
    /// </summary>
    private static void AppendSpacingDeclarations(Paragraph p, ref List<string>? decls)
    {
        var spacing = p.ParagraphProperties?.SpacingBetweenLines;
        if (spacing is null) return;

        var line = spacing.Line?.Value;
        if (!string.IsNullOrEmpty(line)
            && int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineValue)
            && lineValue > 0)
        {
            // Word omits w:lineRule when it means `auto`, so ABSENT must map to the multiple reading.
            var rule = spacing.LineRule?.Value;
            if (rule == LineSpacingRuleValues.Exact || rule == LineSpacingRuleValues.AtLeast)
            {
                (decls ??= new List<string>(4)).Add($"line-height:{FormatPt(lineValue / 20.0)}");
            }
            else
            {
                var multiple = lineValue / 240.0;
                (decls ??= new List<string>(4)).Add(
                    $"line-height:{multiple.ToString("0.##", CultureInfo.InvariantCulture)}");
            }
        }

        var beforePt = TwipsToPoints(spacing.Before?.Value);
        if (beforePt is not null) (decls ??= new List<string>(4)).Add($"margin-top:{FormatPt(beforePt.Value)}");

        var afterPt = TwipsToPoints(spacing.After?.Value);
        if (afterPt is not null) (decls ??= new List<string>(4)).Add($"margin-bottom:{FormatPt(afterPt.Value)}");
    }

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

    /// <summary>
    /// Projects <paramref name="docx"/> into the canonical <see cref="ComposeContentModel"/> — the
    /// render-on-save hub's imported-doc SOURCE (see
    /// <c>projects/spaarkeai-compose-r6/notes/020-canonical-hub-design.md</c>). Never throws: an
    /// unreadable source degrades to <see cref="ComposeProjectionStatus.Failed"/> with an empty model.
    /// </summary>
    /// <remarks>
    /// The walk itself lives in <see cref="ComposeContentModelProjector"/> (task 071). It is a SEPARATE
    /// pipeline from <see cref="Build"/>, not a variant of it: the two are deliberately mirrored and
    /// never merged — <see cref="Build"/> emits display HTML carrying atom/anchoring concerns, while this
    /// one emits the EDITABLE model <c>ComposeDocumentRenderer</c> authors a fresh docx from. This method
    /// remains here so the public surface, and therefore the DI registration, is unchanged (ADR-010).
    /// </remarks>
    public ComposeCanonicalModelProjection BuildContentModel(
        ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default) =>
        ComposeContentModelProjector.Project(docx, cancellationToken);


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
                // `data-projected-list` marks a list that came from the SOURCE DOCUMENT, as opposed to one
                // the user later creates in the editor. The client suppresses the native `<ol>` marker for
                // THESE lists only: their number must come from the 031-computed label (or, for an
                // unresolvable `numId`, be absent — the F-3 "never fabricate a number" posture). An
                // editor-created list carries no such marker, has no server-computed label, and would
                // otherwise render with NO number at all, which is what UAT round 1 item 4 reported.
                Append(ordered ? "<ol data-projected-list=\"1\">" : "<ul data-projected-list=\"1\">");
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
