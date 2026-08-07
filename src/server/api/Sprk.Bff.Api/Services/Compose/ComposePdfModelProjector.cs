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

        foreach (var block in layout.Blocks)
        {
            if (block.Table is { } table)
            {
                var composeTable = ProjectTable(table);
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

        if (blocks.Count == 0)
        {
            // The only hard outcome: nothing projectable. NEVER mount an empty editor over a
            // non-empty PDF — fail the open loudly instead (projection contract: Failed model
            // MUST NOT be rendered).
            return new ComposeCanonicalModelProjection
            {
                Status = ComposeProjectionStatus.Failed,
                Model = new ComposeContentModel(),
                Warnings = new[] { new ComposeProjectionWarning(WarningEmpty, 1) },
            };
        }

        // Document-level honest-lossiness fact, always present (count = source page count so the
        // client can phrase "reflowed from N fixed pages").
        warnings.Add(new ComposeProjectionWarning(WarningFixedLayoutReflowed, Math.Max(1, layout.PageCount)));
        if (pageChromeDropped > 0) warnings.Add(new ComposeProjectionWarning(WarningPageChromeDropped, pageChromeDropped));
        if (footnotesInlined > 0) warnings.Add(new ComposeProjectionWarning(WarningFootnoteInlined, footnotesInlined));
        if (formulasFlattened > 0) warnings.Add(new ComposeProjectionWarning(WarningFormulaFlattened, formulasFlattened));
        if (listsApproximated > 0) warnings.Add(new ComposeProjectionWarning(WarningListApproximated, listsApproximated));
        if (tablesApproximated > 0) warnings.Add(new ComposeProjectionWarning(WarningTableStyleApproximated, tablesApproximated));

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

    /// <summary>
    /// Projects a layout table into a <see cref="ComposeTable"/>, reconstructing the full grid from
    /// anchor cells: <c>ColumnSpan</c> → <see cref="ComposeTableCell.GridSpan"/>; <c>RowSpan</c> →
    /// <see cref="ComposeVerticalMerge.Restart"/> on the anchor plus synthesized
    /// <see cref="ComposeVerticalMerge.Continue"/> cells in the covered rows (Word requires the
    /// continuation cells to exist for the grid to align). Returns null for a degenerate table
    /// (no rows/columns/cells) — the caller simply skips it.
    /// </summary>
    private static ComposeTable? ProjectTable(DocumentLayoutTable table)
    {
        if (table.RowCount <= 0 || table.ColumnCount <= 0 || table.Cells.Count == 0)
        {
            return null;
        }

        var rowCount = table.RowCount;
        var columnCount = table.ColumnCount;

        // Grid of projected cells; null = not yet covered.
        var grid = new ComposeTableCell?[rowCount, columnCount];

        foreach (var cell in table.Cells)
        {
            var r = cell.RowIndex;
            var c = cell.ColumnIndex;
            if (r < 0 || r >= rowCount || c < 0 || c >= columnCount || grid[r, c] is not null)
            {
                continue; // out-of-grid / duplicate anchors are analysis noise — skip, grid fill below heals the hole
            }

            var rowSpan = Math.Clamp(cell.RowSpan, 1, rowCount - r);
            var colSpan = Math.Clamp(cell.ColumnSpan, 1, columnCount - c);

            grid[r, c] = new ComposeTableCell
            {
                Blocks = cell.Text.Length == 0
                    ? Array.Empty<ComposeBlock>()
                    : new[] { ParagraphBlock(cell.Text) },
                IsHeader = cell.IsHeader,
                GridSpan = colSpan,
                VMerge = rowSpan > 1 ? ComposeVerticalMerge.Restart : ComposeVerticalMerge.None,
            };

            // Horizontal coverage on the anchor row: no cell entries (GridSpan absorbs the columns).
            for (var cc = c + 1; cc < c + colSpan; cc++)
            {
                grid[r, cc] ??= HorizontalCoverSentinel;
            }

            // Vertical coverage: synthesized Continue cells (same GridSpan so columns stay aligned).
            for (var rr = r + 1; rr < r + rowSpan; rr++)
            {
                grid[rr, c] ??= new ComposeTableCell
                {
                    Blocks = Array.Empty<ComposeBlock>(),
                    GridSpan = colSpan,
                    VMerge = ComposeVerticalMerge.Continue,
                };
                for (var cc = c + 1; cc < c + colSpan; cc++)
                {
                    grid[rr, cc] ??= HorizontalCoverSentinel;
                }
            }
        }

        var rows = new List<ComposeTableRow>(rowCount);
        for (var r = 0; r < rowCount; r++)
        {
            var cells = new List<ComposeTableCell>(columnCount);
            for (var c = 0; c < columnCount; c++)
            {
                var cell = grid[r, c];
                if (ReferenceEquals(cell, HorizontalCoverSentinel))
                {
                    continue; // absorbed by a GridSpan to the left
                }

                // Analysis holes (positions no anchor covered) become empty cells so the grid stays
                // rectangular — Word requires every row to span the full grid.
                cells.Add(cell ?? new ComposeTableCell { Blocks = Array.Empty<ComposeBlock>() });
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

    // Sentinel marking a grid position absorbed horizontally by a GridSpan (never emitted).
    private static readonly ComposeTableCell HorizontalCoverSentinel = new();
}
