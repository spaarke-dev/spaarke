using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-01a / FR-27 (task 026, E1 born-in-editor) — the from-scratch, high-fidelity OOXML AUTHORING engine.
/// Materializes a well-formed, Word-openable <c>.docx</c> from a server-received editor
/// <see cref="ComposeContentModel"/> (paragraphs / clause headings / lists / tables + inline runs, each
/// paragraph paraId-keyed). This is the server-side replacement for the removed client <c>docx.js</c>
/// exporter and the reason the client can stop authoring bytes entirely (task 027): the SERVER becomes the
/// single authority for ALL <c>.docx</c> authoring — delta-onto-original for LOADED docs
/// (<see cref="ComposeParagraphRedlineSynthesizer"/>, task 022), full render for BORN-IN-EDITOR docs (this).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: a document born in the editor — AI-drafted (a <c>initialHtml</c> seed), blank, or
/// browse-local — has NO retained load-time original to delta against. Before R3 it was saved by the lossy
/// client <c>tipTapToDocxBytes</c>, which flattened true multi-level numbering, real styles, and firm
/// formatting into a stripped approximation — degrading an AI-drafted LEGAL document the moment it was first
/// saved. This engine authors the OOXML deterministically instead. Research-validated: this is how Harvey
/// authors documents — deterministic backend code owns the OOXML; the LLM/editor only supplies text +
/// structure (the "adeu asymmetry"). Client JS exporters are generators, not fidelity round-trippers.
/// </para>
/// <para>
/// <b>The keystone — style-linked multi-level numbering (FR-27)</b>: legal clause schemes (1 / 1.1 / 1.1.1)
/// are authored as ONE multi-level <c>w:abstractNum</c> (ilvl 0-8, <c>%N</c> <c>lvlText</c> cascade)
/// STYLE-LINKED to the <c>Heading1..6</c> paragraph styles — the heading STYLE carries the <c>w:numPr</c>,
/// and each abstract level back-references its style via <c>w:pStyle</c>. A clause paragraph then uses ONLY
/// <c>w:pStyle</c> (no DIRECT paragraph <c>numId</c>), so Word numbers it through the style — the single
/// mechanism that avoids the double/ghost numbering a direct-numId-alongside-a-style-numId produces. This
/// mirrors the real firm-template idiom (Common Paper CSA fixture <c>word/numbering.xml</c>). Ordered/bullet
/// LISTS are distinct: <c>ListParagraph</c> carries no style numbering, so a list item legitimately gets a
/// DIRECT <c>numPr</c> (not double-numbering); an ordered list that restarts at 1 gets a fresh <c>w:num</c>
/// instance with a <c>lvlOverride/startOverride</c>. Numbering MUST be instance-clean + style-linked AT
/// BIRTH — a malformed or double numId stays invisible until a later tracked-change edit (task 022) layers
/// on and corrupts the redline; it cannot be cheaply retrofit.
/// </para>
/// <para>
/// <b>E2 substrate</b>: every emitted <c>w:p</c> (incl. table-cell + nested-table paragraphs) carries a
/// unique, OOXML-valid <c>w14:paraId</c> — carried verbatim from the client model when present, else minted
/// using the SAME <c>ST_LongHexNumber</c> scheme as <see cref="ParaIdPreParser"/> (<c>0 &lt; x &lt;
/// 0x80000000</c>, 8-hex uppercase, dedup). So the rendered doc is a first-class substrate: the NEXT edit's
/// <see cref="ComposeParagraphRedlineSynthesizer"/> finds its paragraphs by those ids.
/// </para>
/// <para>
/// <b>Pure — <c>ComposeContentModel</c> in / <c>byte[]</c> out (ADR-007 / ADR-013 / NFR-05 / NFR-01)</b>: no
/// I/O, no AI, no <c>Microsoft.Graph</c> type, no routing type (Tier-1 NetArchTest <c>ADR013_ComposeFacade</c>
/// enforces); the SPE write stays behind the <c>SpeFileStore</c> facade (the renderer produces bytes;
/// <see cref="ComposeService.SaveAsync"/> persists them). No external NuGet — <c>DocumentFormat.OpenXml</c>
/// 3.5.1 is already referenced (Server-side render-from-a-content-model is deterministic OOXML authoring, NOT
/// an AI dispatch — design §11, ADR-039 complied). Thread-safe stateless — a shared singleton (ADR-010).
/// </para>
/// </remarks>
public sealed partial class ComposeDocumentRenderer
{
    // ── style ids ────────────────────────────────────────────────────────────────────────────────
    private const string NormalStyleId = "Normal";
    private const string ListParagraphStyleId = "ListParagraph";
    private const int MaxHeadingLevel = 6;               // Heading1..6 (TipTap heading levels)

    // ── numbering ids (see NumberingPlan) ────────────────────────────────────────────────────────
    private const int HeadingAbstractNumId = 0;          // style-linked clause scheme (ilvl 0-8)
    private const int OrderedAbstractNumId = 1;           // decimal list scheme (direct numPr)
    private const int BulletAbstractNumId = 2;            // bullet list scheme (direct numPr)
    private const int HeadingNumInstanceId = 1;           // the ONE num instance the Heading styles reference
    private const int FirstListNumInstanceId = 2;         // list num instances are allocated from here up

    // The largest permitted w14:paraId is 0x7FFFFFFF (ST_LongHexNumber, 0 < x < 0x80000000) — mirrors
    // ParaIdPreParser (task 010), the canonical mint scheme. Kept in lockstep so a rendered doc and a
    // load-time pre-parse produce interchangeable ids.
    private const uint MaxParaId = 0x80000000u;
    private const int MintRetryLimit = 1000;

    private readonly Func<uint> _mint;

    /// <summary>Production constructor — mints paraIds from a cryptographic RNG.</summary>
    public ComposeDocumentRenderer() : this(DefaultMint) { }

    /// <summary>
    /// Test seam: inject a deterministic id generator so a golden-file test can assert stable ids. Internal —
    /// exposed to the test assembly via <c>InternalsVisibleTo</c>.
    /// </summary>
    internal ComposeDocumentRenderer(Func<uint> mint) => _mint = mint;

    private static uint DefaultMint() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    /// <summary>
    /// Authors a high-fidelity <c>.docx</c> from <paramref name="model"/>: a <c>StyleDefinitionsPart</c>
    /// catalog (Normal + Heading1-6 + ListParagraph), a <c>NumberingDefinitionsPart</c> with the style-linked
    /// multi-level clause scheme + list schemes, native <c>w:tbl</c> tables, inline <c>w:b</c>/<c>w:i</c>/
    /// <c>w:u</c> runs, and a unique <c>w14:paraId</c> on every <c>w:p</c>. Returns the <c>.docx</c> bytes.
    /// </summary>
    /// <param name="model">The editor content model (the born-in-editor authoring source).</param>
    /// <param name="author">Document author, written to core properties (<c>dc:creator</c>). A blank value
    /// falls back to a stable product label.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    public byte[] SynthesizeDocument(ComposeContentModel model, string author)
    {
        ArgumentNullException.ThrowIfNull(model);
        var creator = string.IsNullOrWhiteSpace(author) ? "Spaarke Compose" : author.Trim();

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // A NumberingPlan accumulates the ordered/bullet num instances the body render allocates, so the
            // numbering part authored afterwards references exactly the ids the body used.
            var plan = new NumberingPlan();
            RenderBlocks(body, model.Blocks, plan);

            // Word requires a trailing sectPr for a valid single-section document.
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));

            AddStyleDefinitions(mainPart);
            AddNumberingDefinitions(mainPart, plan);

            // Mint a unique w14:paraId on every paragraph lacking a valid one — AFTER the body is fully built
            // so the dedup pass sees every client-carried id (mirrors ParaIdPreParser's collect-then-mint).
            AssignParaIds(body);

            AddCoreProperties(document, creator);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Body render
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private void RenderBlocks(OpenXmlElement container, IReadOnlyList<ComposeBlock> blocks, NumberingPlan plan)
    {
        // Ordered-list continuity: a run of consecutive ordered ListItem blocks shares one num instance;
        // any non-ordered-list block (or an ordered item flagged StartsNewList) breaks the run so the next
        // ordered list restarts at 1 via a fresh instance.
        int? currentOrderedNumId = null;

        foreach (var block in blocks)
        {
            switch (block.Kind)
            {
                case ComposeBlockKind.Heading:
                    currentOrderedNumId = null;
                    container.AppendChild(BuildHeading(block));
                    break;

                case ComposeBlockKind.ListItem when block.Ordered:
                    if (currentOrderedNumId is null || block.StartsNewList)
                    {
                        currentOrderedNumId = plan.NewOrderedInstance();
                    }
                    container.AppendChild(BuildListItem(block, currentOrderedNumId.Value));
                    break;

                case ComposeBlockKind.ListItem: // bullet
                    currentOrderedNumId = null;
                    container.AppendChild(BuildListItem(block, plan.BulletInstance()));
                    break;

                case ComposeBlockKind.Table:
                    currentOrderedNumId = null;
                    if (block.Table is { Rows.Count: > 0 })
                    {
                        container.AppendChild(BuildTable(block.Table, plan));
                    }
                    break;

                case ComposeBlockKind.Paragraph:
                default:
                    currentOrderedNumId = null;
                    container.AppendChild(BuildParagraph(block));
                    break;
            }
        }
    }

    private static Paragraph BuildParagraph(ComposeBlock block)
    {
        var pPr = new ParagraphProperties();
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    private static Paragraph BuildHeading(ComposeBlock block)
    {
        var level = Math.Clamp(block.Level <= 0 ? 1 : block.Level, 1, MaxHeadingLevel);
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = HeadingStyleId(level) });
        // NO w:numPr here — the number is supplied by the Heading{level} STYLE's numPr (style-linked). A
        // direct numId here would double-number (FR-27).
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    private static Paragraph BuildListItem(ComposeBlock block, int numInstanceId)
    {
        var ilvl = Math.Clamp(block.Level, 0, 8);
        var pPr = new ParagraphProperties(
            new ParagraphStyleId { Val = ListParagraphStyleId },
            // DIRECT numPr: ListParagraph carries no style numbering, so this is not double-numbering.
            new NumberingProperties(
                new NumberingLevelReference { Val = ilvl },
                new NumberingId { Val = numInstanceId }));
        ApplyAlignment(pPr, block.Alignment);
        return AssembleParagraph(pPr, block);
    }

    /// <summary>Assembles a paragraph from its <paramref name="pPr"/> + the block's inline runs, carrying a
    /// client paraId when the model supplied a valid one (else left for <see cref="AssignParaIds"/>).</summary>
    private static Paragraph AssembleParagraph(ParagraphProperties pPr, ComposeBlock block)
    {
        var paragraph = new Paragraph();
        CarryClientParaId(paragraph, block.ParaId);
        paragraph.AppendChild(pPr);

        if (block.Runs.Count == 0)
        {
            // An empty paragraph is valid (Word renders a blank line). No run needed.
            return paragraph;
        }

        foreach (var run in block.Runs)
        {
            paragraph.AppendChild(BuildRun(run));
        }

        return paragraph;
    }

    private static Run BuildRun(ComposeInlineRun run)
    {
        var element = new Run();
        if (run.Bold || run.Italic || run.Underline)
        {
            var rPr = new RunProperties();
            if (run.Bold) rPr.AppendChild(new Bold());
            if (run.Italic) rPr.AppendChild(new Italic());
            if (run.Underline) rPr.AppendChild(new Underline { Val = UnderlineValues.Single });
            element.AppendChild(rPr);
        }

        element.AppendChild(new Text(SanitizeText(run.Text)) { Space = SpaceProcessingModeValues.Preserve });
        return element;
    }

    // ── tables ───────────────────────────────────────────────────────────────────────────────────

    private Table BuildTable(ComposeTable model, NumberingPlan plan)
    {
        var table = new Table();
        table.AppendChild(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            // CT_TblBorders order: top, left, bottom, right, insideH, insideV.
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "auto" }),
            new TableLook { Val = "04A0", FirstRow = true, LastRow = false, FirstColumn = true, LastColumn = false, NoHorizontalBand = false, NoVerticalBand = true }));

        // A w:tblGrid (column definitions) MUST follow tblPr and precede the rows (schema order). Width the
        // grid to the widest row so every column is defined.
        var columnCount = model.Rows.Max(r => r.Cells.Count);
        var grid = new TableGrid();
        for (var c = 0; c < columnCount; c++)
        {
            grid.AppendChild(new GridColumn());
        }
        table.AppendChild(grid);

        foreach (var row in model.Rows)
        {
            var tableRow = new TableRow();
            foreach (var cell in row.Cells)
            {
                tableRow.AppendChild(BuildTableCell(cell, plan));
            }
            table.AppendChild(tableRow);
        }

        return table;
    }

    private TableCell BuildTableCell(ComposeTableCell cell, NumberingPlan plan)
    {
        var tableCell = new TableCell();
        tableCell.AppendChild(new TableCellProperties(
            new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }));

        if (cell.Blocks.Count == 0)
        {
            // Word requires each cell to contain at least one paragraph.
            tableCell.AppendChild(new Paragraph());
            return tableCell;
        }

        // A header cell bolds its runs (cosmetic). Render the cell's nested blocks recursively so nested
        // tables + lists inside a cell are supported.
        var blocks = cell.IsHeader ? cell.Blocks.Select(EmphasizeBlock).ToList() : cell.Blocks;
        RenderBlocks(tableCell, blocks, plan);
        return tableCell;
    }

    private static ComposeBlock EmphasizeBlock(ComposeBlock block) =>
        block.Kind == ComposeBlockKind.Table
            ? block
            : block with { Runs = block.Runs.Select(r => r with { Bold = true }).ToList() };

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Style catalog (StyleDefinitionsPart)
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void AddStyleDefinitions(MainDocumentPart mainPart)
    {
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        var styles = new Styles();

        // Normal — the default paragraph style every other style is based on.
        styles.AppendChild(new Style(
            new StyleName { Val = "Normal" },
            new PrimaryStyle())
        {
            Type = StyleValues.Paragraph,
            StyleId = NormalStyleId,
            Default = true,
        });

        // Heading1..6 — each carries a w:numPr referencing the ONE heading num instance at its own ilvl
        // (the STYLE side of the style-link) + an outlineLvl so the doc has a navigable outline. Descending
        // sizes; all bold; keepNext so a heading stays with its following paragraph.
        var headingSizes = new[] { "32", "28", "26", "24", "22", "22" }; // half-points: 16pt..11pt
        for (var level = 1; level <= MaxHeadingLevel; level++)
        {
            styles.AppendChild(BuildHeadingStyle(level, headingSizes[level - 1]));
        }

        // ListParagraph — indent only; NO numbering (list items supply a direct numPr).
        styles.AppendChild(new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 34 },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new Indentation { Left = "720" },
                new ContextualSpacing()))
        {
            Type = StyleValues.Paragraph,
            StyleId = ListParagraphStyleId,
        });

        stylesPart.Styles = styles;
        stylesPart.Styles.Save();
    }

    private static Style BuildHeadingStyle(int level, string sizeHalfPoints)
    {
        var ilvl = level - 1;
        return new Style(
            new StyleName { Val = $"heading {level}" },
            new BasedOn { Val = NormalStyleId },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            // CT_PPrBase child order: keepNext precedes numPr precedes spacing precedes outlineLvl.
            new StyleParagraphProperties(
                new KeepNext(),
                new NumberingProperties(
                    new NumberingLevelReference { Val = ilvl },
                    new NumberingId { Val = HeadingNumInstanceId }),
                new SpacingBetweenLines { Before = "240", After = "120" },
                new OutlineLevel { Val = ilvl }),
            new StyleRunProperties(
                new Bold(),
                new FontSize { Val = sizeHalfPoints },
                new FontSizeComplexScript { Val = sizeHalfPoints }))
        {
            Type = StyleValues.Paragraph,
            StyleId = HeadingStyleId(level),
        };
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Numbering (NumberingDefinitionsPart) — the keystone
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void AddNumberingDefinitions(MainDocumentPart mainPart, NumberingPlan plan)
    {
        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        // AbstractNum elements MUST precede Num elements (schema order).
        numbering.AppendChild(BuildHeadingAbstractNum());
        numbering.AppendChild(BuildOrderedAbstractNum());
        numbering.AppendChild(BuildBulletAbstractNum());

        // The ONE heading num instance the Heading styles reference (numId 1 → heading abstract).
        numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = HeadingAbstractNumId }) { NumberID = HeadingNumInstanceId });

        // The shared bullet instance (allocated only if a bullet list was rendered).
        if (plan.BulletInstanceId is { } bulletId)
        {
            numbering.AppendChild(new NumberingInstance(new AbstractNumId { Val = BulletAbstractNumId }) { NumberID = bulletId });
        }

        // One ordered instance per restart-scoped ordered list, each with a startOverride so it restarts at 1.
        foreach (var orderedId in plan.OrderedInstanceIds)
        {
            var instance = new NumberingInstance(new AbstractNumId { Val = OrderedAbstractNumId }) { NumberID = orderedId };
            instance.AppendChild(new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 });
            numbering.AppendChild(instance);
        }

        numberingPart.Numbering = numbering;
        numberingPart.Numbering.Save();
    }

    /// <summary>
    /// The style-linked multi-level clause scheme (FR-27): ONE multilevel abstractNum, 9 levels (ilvl 0-8),
    /// each a decimal <c>%N</c> cascade (<c>%1</c> / <c>%1.%2</c> / <c>%1.%2.%3</c> …). Levels 0-5 back-link
    /// their <c>Heading1..6</c> style via <c>w:pStyle</c>; levels 6-8 are numbered (for completeness) but
    /// unlinked (headings only reach level 6). Each level restarts its counter after a higher level advances.
    /// </summary>
    private static AbstractNum BuildHeadingAbstractNum()
    {
        // CT_AbstractNum order: nsid precedes multiLevelType precedes the levels.
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0000" },
            new MultiLevelType { Val = MultiLevelValues.Multilevel })
        { AbstractNumberId = HeadingAbstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var cascade = string.Join(".", Enumerable.Range(1, ilvl + 1).Select(k => $"%{k}"));
            var level = new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = cascade },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            };

            // Style-link levels 0-5 → Heading1..6 (the abstract side of the link). Schema order places
            // w:pStyle after w:numFmt and BEFORE w:lvlText (matches the real CSA numbering.xml idiom).
            if (ilvl < MaxHeadingLevel)
            {
                level.InsertBefore(new ParagraphStyleIdInLevel { Val = HeadingStyleId(ilvl + 1) }, level.GetFirstChild<LevelText>());
            }

            abstractNum.AppendChild(level);
        }

        return abstractNum;
    }

    /// <summary>The ordered-list scheme: 9 decimal levels (<c>%N.</c>), consumed via a DIRECT numPr on
    /// ListParagraph items. No style link (lists are not styled-numbered).</summary>
    private static AbstractNum BuildOrderedAbstractNum()
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0001" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = OrderedAbstractNumId };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Decimal },
                new LevelText { Val = $"%{ilvl + 1}." },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    /// <summary>The bullet-list scheme: 9 bullet levels (Symbol-font glyphs), consumed via a DIRECT numPr.</summary>
    private static AbstractNum BuildBulletAbstractNum()
    {
        var abstractNum = new AbstractNum(
            new Nsid { Val = "0E7D0002" },
            new MultiLevelType { Val = MultiLevelValues.HybridMultilevel })
        { AbstractNumberId = BulletAbstractNumId };

        // Cycle the three classic Word bullet glyphs across depths.
        var glyphs = new[] { "", "o", "" }; // • (Symbol), o (Courier), ▪ (Wingdings)
        var fonts = new[] { "Symbol", "Courier New", "Wingdings" };

        for (var ilvl = 0; ilvl <= 8; ilvl++)
        {
            var pick = ilvl % 3;
            abstractNum.AppendChild(new Level(
                new StartNumberingValue { Val = 1 },
                new NumberingFormat { Val = NumberFormatValues.Bullet },
                new LevelText { Val = glyphs[pick] },
                new LevelJustification { Val = LevelJustificationValues.Left },
                new PreviousParagraphProperties(
                    new Indentation { Left = (720 * (ilvl + 1)).ToString(CultureInfo.InvariantCulture), Hanging = "360" }),
                new NumberingSymbolRunProperties(
                    new RunFonts { Ascii = fonts[pick], HighAnsi = fonts[pick], Hint = FontTypeHintValues.Default }))
            {
                LevelIndex = ilvl,
            });
        }

        return abstractNum;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // paraId minting (E2 substrate) — mirrors ParaIdPreParser's ST_LongHexNumber scheme
    // ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Carries a client-supplied paraId onto <paramref name="paragraph"/> when it is a valid
    /// <c>ST_LongHexNumber</c>; otherwise leaves it null for <see cref="AssignParaIds"/> to mint.</summary>
    private static void CarryClientParaId(Paragraph paragraph, string? clientParaId)
    {
        if (TryNormalizeParaId(clientParaId, out var normalized))
        {
            paragraph.ParagraphId = new HexBinaryValue(normalized);
        }
    }

    /// <summary>
    /// Post-pass: every body paragraph (incl. table-cell + nested-table paragraphs — <c>Descendants</c> is
    /// recursive) gets a UNIQUE <c>w14:paraId</c>. Two-pass, mirroring <see cref="ParaIdPreParser"/>: pass 1
    /// keeps the FIRST occurrence of each client-carried id and DROPS any later duplicate (so a malformed model
    /// that repeats a paraId can never emit a doc with a duplicate splice key); pass 2 mints for every
    /// paragraph left without one, collision-checked against the kept ids.
    /// </summary>
    private void AssignParaIds(Body body)
    {
        var paragraphs = body.Descendants<Paragraph>().ToList();

        // Pass 1: keep the first occurrence of each client id; null out duplicates so pass 2 re-mints them.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paragraphs)
        {
            var existing = p.ParagraphId?.Value;
            if (string.IsNullOrEmpty(existing))
            {
                continue;
            }

            if (!seen.Add(existing!))
            {
                p.ParagraphId = null; // duplicate client-carried id — drop it, mint a fresh unique one below
            }
        }

        // Pass 2: mint for every paragraph without an id.
        foreach (var p in paragraphs)
        {
            if (!string.IsNullOrEmpty(p.ParagraphId?.Value))
            {
                continue;
            }

            var minted = MintUnique(seen);
            seen.Add(minted);
            p.ParagraphId = new HexBinaryValue(minted);
        }
    }

    private string MintUnique(HashSet<string> seen)
    {
        for (var attempt = 0; attempt < MintRetryLimit; attempt++)
        {
            var candidate = _mint();
            if (candidate == 0 || candidate >= MaxParaId)
            {
                continue;
            }

            var hex = candidate.ToString("X8");
            if (!seen.Contains(hex))
            {
                return hex;
            }
        }

        throw new InvalidOperationException($"Unable to mint a unique w14:paraId after {MintRetryLimit} attempts.");
    }

    private static bool TryNormalizeParaId(string? candidate, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (!ParaIdHexPattern().IsMatch(trimmed))
        {
            return false;
        }

        // Enforce the ST_LongHexNumber range (0 < x < 0x80000000).
        if (!uint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            || value == 0 || value >= MaxParaId)
        {
            return false;
        }

        normalized = value.ToString("X8");
        return true;
    }

    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────────────────────

    private static void ApplyAlignment(ParagraphProperties pPr, ComposeParagraphAlignment alignment)
    {
        var value = alignment switch
        {
            ComposeParagraphAlignment.Left => JustificationValues.Left,
            ComposeParagraphAlignment.Center => JustificationValues.Center,
            ComposeParagraphAlignment.Right => JustificationValues.Right,
            ComposeParagraphAlignment.Justify => JustificationValues.Both,
            _ => (JustificationValues?)null,
        };

        if (value is { } jc)
        {
            pPr.AppendChild(new Justification { Val = jc });
        }
    }

    private static string HeadingStyleId(int level) => $"Heading{level}";

    private static void AddCoreProperties(WordprocessingDocument document, string creator)
    {
        var props = document.AddCoreFilePropertiesPart();
        using var writer = new StreamWriter(props.GetStream(FileMode.Create));
        writer.Write($@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties""
                   xmlns:dc=""http://purl.org/dc/elements/1.1/"">
    <dc:creator>{EscapeXml(creator)}</dc:creator>
    <dc:description>Authored by Spaarke Compose</dc:description>
</cp:coreProperties>");
    }

    private static string EscapeXml(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;");

    /// <summary>Strips XML-illegal control characters so Word never reports "unreadable content"
    /// (mirrors <c>DocxExportService.SanitizeText</c>; the content model text is already plain, so no HTML
    /// decode is needed). Tab / LF / CR are preserved (valid in XML).</summary>
    private static string SanitizeText(string value) =>
        string.IsNullOrEmpty(value) ? string.Empty : XmlInvalidCharPattern().Replace(value, string.Empty);

    [GeneratedRegex(@"^[0-9A-Fa-f]{1,8}$")]
    private static partial Regex ParaIdHexPattern();

    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex XmlInvalidCharPattern();

    /// <summary>
    /// Accumulates the list <c>w:num</c> instances a body render allocates: a single shared bullet instance
    /// (lazily) and one instance per restart-scoped ordered list. The heading instance (numId 1) is fixed and
    /// authored unconditionally, so it is not tracked here.
    /// </summary>
    private sealed class NumberingPlan
    {
        private int _nextNumId = FirstListNumInstanceId;

        /// <summary>The allocated ordered-list instance ids, in allocation order (each restarts at 1).</summary>
        public List<int> OrderedInstanceIds { get; } = new();

        /// <summary>The shared bullet-list instance id, or null when no bullet list was rendered.</summary>
        public int? BulletInstanceId { get; private set; }

        /// <summary>Allocates a fresh ordered-list instance (a new numbered list that restarts at 1).</summary>
        public int NewOrderedInstance()
        {
            var id = _nextNumId++;
            OrderedInstanceIds.Add(id);
            return id;
        }

        /// <summary>Returns the shared bullet-list instance id, allocating it on first use.</summary>
        public int BulletInstance() => BulletInstanceId ??= _nextNumId++;
    }
}
