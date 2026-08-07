using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — projects a PDF's STRUCTURED layout
/// (<see cref="DocumentLayout"/>, obtained via the <see cref="IComposePdfIntakeSource"/> PublicContracts
/// facade) into the SAME canonical content model (<see cref="ComposeContentModel"/>, task 020) that
/// <c>.docx</c> projects into — the render-on-save hub's second intake source. Pure, stateless,
/// deterministic (no AI type, no Graph type — ADR-013/007); the ONLY consumer of the facade DTO on the
/// Compose side.
/// </summary>
/// <remarks>
/// <para>
/// <b>Honest lossiness (spec FR-06)</b>: PDF is FIXED-LAYOUT; a flow-document model cannot reproduce
/// it faithfully. Every degradation is counted LOUDLY as a <see cref="ComposeProjectionWarning"/>
/// (<c>pdf-intake-*</c> codes, mirror of the docx projection's counted-warning posture) and the intake
/// NEVER hard-fails on a construct — the only Failed outcome is a document with no projectable content
/// at all (mounting an empty editor over a non-empty PDF would be a silent lie).
/// </para>
/// <para>
/// Mapping (best fidelity on common cases; rare shapes degrade loudly):
/// <list type="bullet">
///   <item><c>title</c> → <see cref="ComposeBlockKind.Heading"/> level 1; <c>sectionHeading</c> →
///     Heading level 2 (Word's style-linked numbering takes over on render).</item>
///   <item>Body prose → <see cref="ComposeBlockKind.Paragraph"/> (one run; the layout model carries no
///     character formatting — part of the general reflow degradation, not separately counted).</item>
///   <item>Leading bullet glyphs (•◦▪●‣·⁃) → bullet <see cref="ComposeBlockKind.ListItem"/> (glyph
///     stripped; Word renders its own bullet) — counted <c>pdf-intake-list-approximated</c>. Numbered
///     text is intentionally NOT converted (legal numbering like "1.2" / "(a)" is prose-significant;
///     the literal number stays in the text — no fake auto-numbering).</item>
///   <item>Tables → <see cref="ComposeTable"/> in born-in-editor mode (Borders null → the renderer's
///     default single-border chrome; PDF border styling is not reliably extractable) — counted
///     <c>pdf-intake-table-style-approximated</c>. Row/column spans are reconstructed (anchor
///     <c>GridSpan</c> + synthesized <c>vMerge</c> continuation cells) so merged layouts survive.</item>
///   <item>Page headers/footers/page numbers (repeat-per-page chrome) → DROPPED, counted
///     <c>pdf-intake-page-chrome-dropped</c> (a flow document has one header per section, not N
///     per-page paragraphs; duplicating them into the body would corrupt the prose).</item>
///   <item>Footnotes → inlined as body paragraphs at document position, counted
///     <c>pdf-intake-footnote-inlined</c> (the text is preserved; the footnote apparatus is not).</item>
///   <item>Formula blocks → plain-text paragraphs, counted <c>pdf-intake-formula-flattened</c>.</item>
///   <item>Pagination itself → NOT reproduced (no synthetic page breaks; the docx reflows) — the
///     document-level fact is counted once as <c>pdf-intake-fixed-layout-reflowed</c> (count = source
///     page count), the driver for the client's honest-lossiness banner (task 041).</item>
/// </list>
/// ParaIds are left null throughout — <see cref="ComposeDocumentRenderer.SynthesizeDocument"/> mints
/// OOXML-valid ids at render, after which the standard docx pipeline owns identity.
/// </para>
/// </remarks>
public sealed class ComposePdfModelProjector
{
    /// <summary>Document-level reflow fact (count = source page count) — always emitted; drives the
    /// client's honest-lossiness expectation.</summary>
    public const string WarningFixedLayoutReflowed = "pdf-intake-fixed-layout-reflowed";

    /// <summary>Per-page chrome (headers/footers/page numbers) dropped.</summary>
    public const string WarningPageChromeDropped = "pdf-intake-page-chrome-dropped";

    /// <summary>Footnote text inlined as a body paragraph.</summary>
    public const string WarningFootnoteInlined = "pdf-intake-footnote-inlined";

    /// <summary>Formula block flattened to plain text.</summary>
    public const string WarningFormulaFlattened = "pdf-intake-formula-flattened";

    /// <summary>Bullet-glyph paragraph approximated as a bullet list item.</summary>
    public const string WarningListApproximated = "pdf-intake-list-approximated";

    /// <summary>Table emitted with the renderer's default chrome (PDF border styling not extractable).</summary>
    public const string WarningTableStyleApproximated = "pdf-intake-table-style-approximated";

    /// <summary>Step-9.5 MEDIUM-4: a table anchor cell overlapped a position already covered by another
    /// anchor's span (Azure DI emits inconsistent spans on complex merged tables) — its text was
    /// CONSOLIDATED into the covering cell rather than silently dropped.</summary>
    public const string WarningTableCellConsolidated = "pdf-intake-table-cell-consolidated";

    /// <summary>Step-9.5 A-LOW-1 (041 review): a table anchor cell sat OUTSIDE the reported grid
    /// (analysis noise) — there is no covering cell to consolidate into, so its text could not be
    /// placed. Counted honestly under its own code (never conflated with consolidation).</summary>
    public const string WarningTableCellDropped = "pdf-intake-table-cell-dropped";

    /// <summary>No projectable content — the only Failed outcome.</summary>
    public const string WarningEmpty = "pdf-intake-empty";

    // Closed glyph set for bullet approximation — deliberately conservative (no '-'/'–': legal prose
    // uses dashes; a mis-fired list conversion is worse than a literal glyph).
    private static readonly char[] BulletGlyphs = { '•', '◦', '▪', '●', '‣', '·', '⁃' };

    /// <summary>
    /// Projects the layout into the canonical content model. Never throws on content shape; Failed
    /// ONLY when no block is projectable (see <see cref="WarningEmpty"/>).
    /// </summary>
    public ComposeCanonicalModelProjection Project(DocumentLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var warnings = new List<ComposeProjectionWarning>();
        var blocks = new List<ComposeBlock>();

        var pageChromeDropped = 0;
        var footnotesInlined = 0;
        var formulasFlattened = 0;
        var listsApproximated = 0;
        var tablesApproximated = 0;
        var tableCellsConsolidated = 0;
        var tableCellsDropped = 0;

        foreach (var block in layout.Blocks)
        {
            if (block.Table is { } table)
            {
                var composeTable = ProjectTable(table, ref tableCellsConsolidated, ref tableCellsDropped);
                if (composeTable is not null)
                {
                    blocks.Add(new ComposeBlock { Kind = ComposeBlockKind.Table, Table = composeTable });
                    tablesApproximated++;
                }
                continue;
            }

            if (block.Paragraph is not { } paragraph || paragraph.Text.Length == 0)
            {
                continue;
            }

            switch (paragraph.Role)
            {
                case DocumentLayoutParagraphRole.PageHeader:
                case DocumentLayoutParagraphRole.PageFooter:
                case DocumentLayoutParagraphRole.PageNumber:
                    pageChromeDropped++;
                    continue;

                case DocumentLayoutParagraphRole.Title:
                    blocks.Add(HeadingBlock(paragraph.Text, level: 1));
                    continue;

                case DocumentLayoutParagraphRole.SectionHeading:
                    blocks.Add(HeadingBlock(paragraph.Text, level: 2));
                    continue;

                case DocumentLayoutParagraphRole.Footnote:
                    footnotesInlined++;
                    blocks.Add(ParagraphBlock(paragraph.Text));
                    continue;

                case DocumentLayoutParagraphRole.Formula:
                    formulasFlattened++;
                    blocks.Add(ParagraphBlock(paragraph.Text));
                    continue;

                default:
                    if (TryStripBulletGlyph(paragraph.Text, out var itemText))
                    {
                        listsApproximated++;
                        blocks.Add(new ComposeBlock
                        {
                            Kind = ComposeBlockKind.ListItem,
                            Level = 0,
                            Ordered = false,
                            Runs = new[] { new ComposeInlineRun { Text = itemText } },
                        });
                    }
                    else
                    {
                        blocks.Add(ParagraphBlock(paragraph.Text));
                    }
                    continue;
            }
        }

        // The counted per-construct facts — shared by BOTH outcomes (Step-9.5 LOW-8: a Failed-empty
        // result keeps its diagnostics — "why it was empty" must not be thrown away).
        if (pageChromeDropped > 0) warnings.Add(new ComposeProjectionWarning(WarningPageChromeDropped, pageChromeDropped));
        if (footnotesInlined > 0) warnings.Add(new ComposeProjectionWarning(WarningFootnoteInlined, footnotesInlined));
        if (formulasFlattened > 0) warnings.Add(new ComposeProjectionWarning(WarningFormulaFlattened, formulasFlattened));
        if (listsApproximated > 0) warnings.Add(new ComposeProjectionWarning(WarningListApproximated, listsApproximated));
        if (tablesApproximated > 0) warnings.Add(new ComposeProjectionWarning(WarningTableStyleApproximated, tablesApproximated));
        if (tableCellsConsolidated > 0) warnings.Add(new ComposeProjectionWarning(WarningTableCellConsolidated, tableCellsConsolidated));
        if (tableCellsDropped > 0) warnings.Add(new ComposeProjectionWarning(WarningTableCellDropped, tableCellsDropped));

        if (blocks.Count == 0)
        {
            // The only hard outcome: nothing projectable. NEVER mount an empty editor over a
            // non-empty PDF — fail the open loudly instead (projection contract: Failed model
            // MUST NOT be rendered). The diagnostic counters above ride along (LOW-8).
            warnings.Insert(0, new ComposeProjectionWarning(WarningEmpty, 1));
            return new ComposeCanonicalModelProjection
            {
                Status = ComposeProjectionStatus.Failed,
                Model = new ComposeContentModel(),
                Warnings = warnings,
            };
        }

        // Document-level honest-lossiness fact, always present (count = source page count so the
        // client can phrase "reflowed from N fixed pages"). First in the list — the banner leads with it.
        warnings.Insert(0, new ComposeProjectionWarning(WarningFixedLayoutReflowed, Math.Max(1, layout.PageCount)));

        return new ComposeCanonicalModelProjection
        {
            // A PDF projection is ALWAYS Partial at best: the fixed-layout reflow warning above is
            // structural, so Success (clean) is unreachable by design — honest by construction.
            Status = ComposeProjectionStatus.Partial,
            Model = new ComposeContentModel { Blocks = blocks },
            Warnings = warnings,
        };
    }

    private static ComposeBlock HeadingBlock(string text, int level) => new()
    {
        Kind = ComposeBlockKind.Heading,
        Level = level,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock ParagraphBlock(string text) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static bool TryStripBulletGlyph(string text, out string itemText)
    {
        itemText = text;
        if (text.Length < 2 || Array.IndexOf(BulletGlyphs, text[0]) < 0)
        {
            return false;
        }

        var stripped = text[1..].TrimStart();
        if (stripped.Length == 0)
        {
            return false; // a bare glyph is decoration, not a list item
        }

        itemText = stripped;
        return true;
    }

    /// <summary>How a grid position is occupied during reconstruction.</summary>
    private enum SlotKind
    {
        /// <summary>An anchor cell (emits a real cell).</summary>
        Anchor,

        /// <summary>Absorbed horizontally by an anchor's GridSpan (emits nothing).</summary>
        HorizontalCover,

        /// <summary>Synthesized vertical-merge continuation under an anchor's RowSpan (emits a
        /// <see cref="ComposeVerticalMerge.Continue"/> cell with the anchor's GridSpan).</summary>
        VerticalContinue,
    }

    /// <summary>Mutable reconstruction slot — <see cref="OwnerOrSelf"/> is the ANCHOR builder that
    /// covers this position (an anchor owns itself; cover/continue slots point at their anchor), the
    /// consolidation target for overlapping analysis anchors.</summary>
    private sealed class Slot
    {
        public required SlotKind Kind { get; init; }

        /// <summary>The covering anchor for cover/continue slots; null for an anchor (see
        /// <see cref="OwnerOrSelf"/>).</summary>
        public Slot? Owner { get; init; }

        public Slot OwnerOrSelf => Owner ?? this;

        public List<ComposeBlock> Blocks { get; } = new();

        public bool IsHeader { get; set; }

        public int GridSpan { get; set; } = 1;

        public bool IsMergeRestart { get; set; }
    }

    /// <summary>
    /// Projects a layout table into a <see cref="ComposeTable"/>, reconstructing the full grid from
    /// anchor cells: <c>ColumnSpan</c> → <see cref="ComposeTableCell.GridSpan"/>; <c>RowSpan</c> →
    /// <see cref="ComposeVerticalMerge.Restart"/> on the anchor plus synthesized
    /// <see cref="ComposeVerticalMerge.Continue"/> cells in the covered rows (Word requires the
    /// continuation cells to exist for the grid to align). Anchors are processed in READING ORDER
    /// (row, then column — Step-9.5 MEDIUM-4: the analysis does not guarantee order, and span coverage
    /// is order-sensitive). An anchor whose position is already covered by another anchor's span
    /// (overlapping/inconsistent analysis spans on complex merged tables) has its TEXT CONSOLIDATED
    /// into the covering anchor's cell — never silently dropped — and is counted via
    /// <paramref name="consolidatedCells"/> (→ <see cref="WarningTableCellConsolidated"/>).
    /// Returns null for a degenerate table (no rows/columns/cells) — the caller simply skips it.
    /// </summary>
    private static ComposeTable? ProjectTable(DocumentLayoutTable table, ref int consolidatedCells, ref int droppedCells)
    {
        if (table.RowCount <= 0 || table.ColumnCount <= 0 || table.Cells.Count == 0)
        {
            return null;
        }

        var rowCount = table.RowCount;
        var columnCount = table.ColumnCount;

        // Grid of reconstruction slots; null = hole (no anchor covered it).
        var grid = new Slot?[rowCount, columnCount];

        // Reading order — coverage is order-sensitive (see doc comment).
        var orderedCells = table.Cells
            .OrderBy(c => c.RowIndex)
            .ThenBy(c => c.ColumnIndex)
            .ToList();

        foreach (var cell in orderedCells)
        {
            var r = cell.RowIndex;
            var c = cell.ColumnIndex;
            if (r < 0 || r >= rowCount || c < 0 || c >= columnCount)
            {
                // A-LOW-1: out-of-grid anchors are analysis noise; there is NO covering cell to
                // consolidate into — the text is unplaceable. Counted under its own DROPPED code so
                // the user-facing count never lies about what happened.
                if (cell.Text.Length > 0)
                {
                    droppedCells++;
                }
                continue;
            }

            if (grid[r, c] is { } taken)
            {
                // MEDIUM-4: overlapping/duplicate anchor — consolidate its text into the COVERING
                // anchor's cell rather than dropping it.
                if (cell.Text.Length > 0)
                {
                    taken.OwnerOrSelf.Blocks.Add(ParagraphBlock(cell.Text));
                    consolidatedCells++;
                }
                continue;
            }

            var rowSpan = Math.Clamp(cell.RowSpan, 1, rowCount - r);
            var colSpan = Math.Clamp(cell.ColumnSpan, 1, columnCount - c);

            // Step-9.5 A-MEDIUM-2 (041 review): shrink spans to the CONTIGUOUS FREE run. An
            // already-occupied slot inside the sweep (e.g. a prior anchor's vertical Continue) must
            // not be double-covered — the old `??=` skip left THIS anchor's GridSpan at full width
            // while the occupied slot still emitted its own cell, over-widening the row (an invalid
            // Word grid). Geometry-only approximation (no text involved) — covered by the table's
            // pdf-intake-table-style-approximated posture.
            var freeCols = 1;
            while (freeCols < colSpan && grid[r, c + freeCols] is null)
            {
                freeCols++;
            }
            colSpan = freeCols;
            var freeRows = 1;
            while (freeRows < rowSpan && grid[r + freeRows, c] is null)
            {
                freeRows++;
            }
            rowSpan = freeRows;

            var anchor = new Slot { Kind = SlotKind.Anchor };
            anchor.IsHeader = cell.IsHeader;
            anchor.GridSpan = colSpan;
            anchor.IsMergeRestart = rowSpan > 1;
            if (cell.Text.Length > 0)
            {
                anchor.Blocks.Add(ParagraphBlock(cell.Text));
            }

            grid[r, c] = anchor;

            // Horizontal coverage on the anchor row (GridSpan absorbs these columns).
            for (var cc = c + 1; cc < c + colSpan; cc++)
            {
                grid[r, cc] ??= new Slot { Kind = SlotKind.HorizontalCover, Owner = anchor };
            }

            // Vertical coverage: synthesized Continue cells (same GridSpan so columns stay aligned).
            for (var rr = r + 1; rr < r + rowSpan; rr++)
            {
                grid[rr, c] ??= new Slot { Kind = SlotKind.VerticalContinue, Owner = anchor };
                for (var cc = c + 1; cc < c + colSpan; cc++)
                {
                    grid[rr, cc] ??= new Slot { Kind = SlotKind.HorizontalCover, Owner = anchor };
                }
            }
        }

        var rows = new List<ComposeTableRow>(rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            var cells = new List<ComposeTableCell>(columnCount);
            for (var c = 0; c < columnCount; c++)
            {
                var slot = grid[r, c];
                if (slot?.Kind == SlotKind.HorizontalCover)
                {
                    continue; // absorbed by a GridSpan to the left
                }

                if (slot is null)
                {
                    // Analysis holes become empty cells so the grid stays rectangular — Word requires
                    // every row to span the full grid.
                    cells.Add(new ComposeTableCell { Blocks = Array.Empty<ComposeBlock>() });
                }
                else if (slot.Kind == SlotKind.VerticalContinue)
                {
                    cells.Add(new ComposeTableCell
                    {
                        Blocks = Array.Empty<ComposeBlock>(),
                        GridSpan = slot.OwnerOrSelf.GridSpan,
                        VMerge = ComposeVerticalMerge.Continue,
                    });
                }
                else
                {
                    cells.Add(new ComposeTableCell
                    {
                        Blocks = slot.Blocks.Count == 0 ? Array.Empty<ComposeBlock>() : slot.Blocks.ToArray(),
                        IsHeader = slot.IsHeader,
                        GridSpan = slot.GridSpan,
                        VMerge = slot.IsMergeRestart ? ComposeVerticalMerge.Restart : ComposeVerticalMerge.None,
                    });
                }
            }

            var repeatAsHeader = r == 0 && cells.Count > 0 && cells.All(cc => cc.IsHeader || cc.VMerge == ComposeVerticalMerge.Continue);
            rows.Add(new ComposeTableRow { Cells = cells, RepeatAsHeaderRow = repeatAsHeader });
        }

        return new ComposeTable
        {
            Rows = rows,
            // Borders null = born-in-editor mode: the renderer applies its default single-border
            // chrome (PDF border styling is not reliably extractable — counted
            // pdf-intake-table-style-approximated by the caller).
        };
    }

}
