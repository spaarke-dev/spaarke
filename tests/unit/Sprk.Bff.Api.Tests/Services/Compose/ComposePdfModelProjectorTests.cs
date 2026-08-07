using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — <see cref="ComposePdfModelProjector"/>: the PDF intake's
/// DocumentLayout → canonical-content-model mapping (pure domain logic with branches). Anchors the
/// honest-lossiness contract: role mapping, page-chrome drop, list/table approximation, span
/// reconstruction, the always-Partial posture, and the empty→Failed guard. The round-trip test at the
/// bottom proves the acceptance criterion directly: the projected model renders through the ONE
/// renderer and re-projects through the SAME docx canonical-model builder the docx path uses.
/// </summary>
public class ComposePdfModelProjectorTests
{
    private readonly ComposePdfModelProjector _sut = new();

    private static DocumentLayoutBlock Para(string text, DocumentLayoutParagraphRole role = DocumentLayoutParagraphRole.Body, int page = 1)
        => new() { Paragraph = new DocumentLayoutParagraph(text, role, page) };

    // ---------------------------------------------------------------------
    // Role mapping
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_TitleAndSectionHeading_MapToHeadingLevels1And2()
    {
        var layout = new DocumentLayout
        {
            PageCount = 2,
            Blocks = new[]
            {
                Para("MUTUAL NON-DISCLOSURE AGREEMENT", DocumentLayoutParagraphRole.Title),
                Para("1. Confidential Information", DocumentLayoutParagraphRole.SectionHeading),
                Para("Each party agrees to hold in confidence…"),
            },
        };

        var result = _sut.Project(layout);

        result.Status.Should().Be(ComposeProjectionStatus.Partial);
        result.Model.Blocks.Should().HaveCount(3);
        result.Model.Blocks[0].Kind.Should().Be(ComposeBlockKind.Heading);
        result.Model.Blocks[0].Level.Should().Be(1);
        result.Model.Blocks[1].Kind.Should().Be(ComposeBlockKind.Heading);
        result.Model.Blocks[1].Level.Should().Be(2);
        result.Model.Blocks[1].Runs[0].Text.Should().Be("1. Confidential Information");
        result.Model.Blocks[2].Kind.Should().Be(ComposeBlockKind.Paragraph);
    }

    [Fact]
    public void Project_AnyLayout_AlwaysCarriesFixedLayoutReflowedWarningWithPageCount()
    {
        var layout = new DocumentLayout { PageCount = 7, Blocks = new[] { Para("text") } };

        var result = _sut.Project(layout);

        // A PDF projection is Partial at best BY DESIGN — the reflow fact is structural.
        result.Status.Should().Be(ComposeProjectionStatus.Partial);
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningFixedLayoutReflowed)
            .Which.Count.Should().Be(7);
    }

    // ---------------------------------------------------------------------
    // Page chrome / footnotes / formulas
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_PageChrome_IsDroppedAndCountedLoudly()
    {
        var layout = new DocumentLayout
        {
            PageCount = 2,
            Blocks = new[]
            {
                Para("CONFIDENTIAL — Corteva NDA", DocumentLayoutParagraphRole.PageHeader),
                Para("Body prose."),
                Para("Page 1 of 9", DocumentLayoutParagraphRole.PageNumber),
                Para("© 2022 All rights reserved", DocumentLayoutParagraphRole.PageFooter),
            },
        };

        var result = _sut.Project(layout);

        result.Model.Blocks.Should().ContainSingle().Which.Runs[0].Text.Should().Be("Body prose.");
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningPageChromeDropped)
            .Which.Count.Should().Be(3);
    }

    [Fact]
    public void Project_FootnoteAndFormula_AreInlinedAsParagraphsWithWarnings()
    {
        var layout = new DocumentLayout
        {
            PageCount = 1,
            Blocks = new[]
            {
                Para("Body."),
                Para("1 As defined in Section 4.2.", DocumentLayoutParagraphRole.Footnote),
                Para("E = mc2", DocumentLayoutParagraphRole.Formula),
            },
        };

        var result = _sut.Project(layout);

        result.Model.Blocks.Should().HaveCount(3);
        result.Model.Blocks.Should().OnlyContain(b => b.Kind == ComposeBlockKind.Paragraph);
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningFootnoteInlined)
            .Which.Count.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningFormulaFlattened)
            .Which.Count.Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // List approximation (conservative bullet-glyph-only contract)
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_BulletGlyphParagraph_BecomesBulletListItemWithGlyphStripped()
    {
        var layout = new DocumentLayout
        {
            PageCount = 1,
            Blocks = new[]
            {
                Para("• Trade secrets and know-how"),
                Para("• Financial information"),
            },
        };

        var result = _sut.Project(layout);

        result.Model.Blocks.Should().HaveCount(2);
        result.Model.Blocks.Should().OnlyContain(b => b.Kind == ComposeBlockKind.ListItem && !b.Ordered && b.Level == 0);
        result.Model.Blocks[0].Runs[0].Text.Should().Be("Trade secrets and know-how");
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningListApproximated)
            .Which.Count.Should().Be(2);
    }

    [Fact]
    public void Project_NumberedOrDashedProse_StaysLiteralParagraph()
    {
        // Legal numbering ("1.2", "(a)") and dashes are prose-significant — the projector must NOT
        // invent auto-numbering or convert dash lines to list items.
        var layout = new DocumentLayout
        {
            PageCount = 1,
            Blocks = new[]
            {
                Para("1. Definitions. In this Agreement…"),
                Para("(a) \"Confidential Information\" means…"),
                Para("- a dash-led line stays prose"),
            },
        };

        var result = _sut.Project(layout);

        result.Model.Blocks.Should().OnlyContain(b => b.Kind == ComposeBlockKind.Paragraph);
        result.Model.Blocks[0].Runs[0].Text.Should().Be("1. Definitions. In this Agreement…");
        result.Warnings.Should().NotContain(w => w.Code == ComposePdfModelProjector.WarningListApproximated);
    }

    // ---------------------------------------------------------------------
    // Tables: grid reconstruction (spans, merges, headers)
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_TableWithColumnAndRowSpans_ReconstructsGridSpanAndVMerge()
    {
        // 3x3 grid: (0,0) spans 2 columns; (1,0) spans 2 rows; the rest are plain cells.
        var table = new DocumentLayoutTable(
            RowCount: 3,
            ColumnCount: 3,
            Cells: new[]
            {
                new DocumentLayoutTableCell(0, 0, 1, 2, "Wide header", IsHeader: true),
                new DocumentLayoutTableCell(0, 2, 1, 1, "H3", IsHeader: true),
                new DocumentLayoutTableCell(1, 0, 2, 1, "Tall", IsHeader: false),
                new DocumentLayoutTableCell(1, 1, 1, 1, "B", IsHeader: false),
                new DocumentLayoutTableCell(1, 2, 1, 1, "C", IsHeader: false),
                new DocumentLayoutTableCell(2, 1, 1, 1, "E", IsHeader: false),
                new DocumentLayoutTableCell(2, 2, 1, 1, "F", IsHeader: false),
            },
            PageNumber: 1);
        var layout = new DocumentLayout
        {
            PageCount = 1,
            Blocks = new[] { new DocumentLayoutBlock { Table = table } },
        };

        var result = _sut.Project(layout);

        var composeTable = result.Model.Blocks.Should().ContainSingle().Which.Table!;
        composeTable.Borders.Should().BeNull("PDF tables use born-in-editor chrome (border styling not extractable)");

        // Row 0: wide header (GridSpan 2) + H3 — the covered column emits no cell.
        composeTable.Rows[0].Cells.Should().HaveCount(2);
        composeTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        composeTable.Rows[0].Cells[0].IsHeader.Should().BeTrue();
        composeTable.Rows[0].RepeatAsHeaderRow.Should().BeTrue();

        // Row 1: rowspan anchor restarts the vertical merge.
        composeTable.Rows[1].Cells[0].VMerge.Should().Be(ComposeVerticalMerge.Restart);

        // Row 2: a synthesized Continue cell keeps the grid aligned under the rowspan.
        composeTable.Rows[2].Cells.Should().HaveCount(3);
        composeTable.Rows[2].Cells[0].VMerge.Should().Be(ComposeVerticalMerge.Continue);
        composeTable.Rows[2].Cells[0].Blocks.Should().BeEmpty();

        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningTableStyleApproximated)
            .Which.Count.Should().Be(1);
    }

    [Fact]
    public void Project_TableWithAnalysisHoles_EmitsEmptyCellsToKeepGridRectangular()
    {
        // The analysis reported only 2 of 4 cells — the grid must still be rectangular.
        var table = new DocumentLayoutTable(
            RowCount: 2,
            ColumnCount: 2,
            Cells: new[]
            {
                new DocumentLayoutTableCell(0, 0, 1, 1, "A", IsHeader: false),
                new DocumentLayoutTableCell(1, 1, 1, 1, "D", IsHeader: false),
            },
            PageNumber: 1);
        var layout = new DocumentLayout { PageCount = 1, Blocks = new[] { new DocumentLayoutBlock { Table = table } } };

        var result = _sut.Project(layout);

        var composeTable = result.Model.Blocks.Single().Table!;
        composeTable.Rows.Should().HaveCount(2);
        composeTable.Rows[0].Cells.Should().HaveCount(2);
        composeTable.Rows[1].Cells.Should().HaveCount(2);
        composeTable.Rows[0].Cells[1].Blocks.Should().BeEmpty();
        composeTable.Rows[1].Cells[0].Blocks.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // Failure posture
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_NothingProjectable_FailsWithPdfIntakeEmpty()
    {
        var layout = new DocumentLayout
        {
            PageCount = 3,
            Blocks = new[]
            {
                Para("Page 1", DocumentLayoutParagraphRole.PageNumber),
                Para("", DocumentLayoutParagraphRole.Body),
            },
        };

        var result = _sut.Project(layout);

        result.Status.Should().Be(ComposeProjectionStatus.Failed);
        result.Model.Blocks.Should().BeEmpty();
        result.Warnings.Should().ContainSingle(w => w.Code == ComposePdfModelProjector.WarningEmpty);
    }

    // ---------------------------------------------------------------------
    // The acceptance criterion, end-to-end: PDF layout → canonical model →
    // SynthesizeDocument (the ONE renderer) → BuildContentModel (the SAME
    // docx projection the docx intake uses) — the hub round-trip.
    // ---------------------------------------------------------------------

    [Fact]
    public void Project_ThenSynthesizeThenReproject_RoundTripsThroughTheSameCanonicalHubAsDocx()
    {
        var table = new DocumentLayoutTable(
            RowCount: 1,
            ColumnCount: 2,
            Cells: new[]
            {
                new DocumentLayoutTableCell(0, 0, 1, 1, "Term", IsHeader: true),
                new DocumentLayoutTableCell(0, 1, 1, 1, "Two (2) years", IsHeader: false),
            },
            PageNumber: 1);
        var layout = new DocumentLayout
        {
            PageCount = 2,
            Blocks = new[]
            {
                Para("MUTUAL NON-DISCLOSURE AGREEMENT", DocumentLayoutParagraphRole.Title),
                Para("1. Confidential Information", DocumentLayoutParagraphRole.SectionHeading),
                Para("Each party agrees to hold in confidence all Confidential Information."),
                Para("• Trade secrets and know-how"),
                new DocumentLayoutBlock { Table = table },
            },
        };

        var projected = _sut.Project(layout);
        projected.Status.Should().Be(ComposeProjectionStatus.Partial);

        // Render through the ONE renderer (render-on-save hub) …
        var renderer = new ComposeDocumentRenderer();
        var docxBytes = renderer.SynthesizeDocument(projected.Model, author: "test");

        // … and re-project through the SAME canonical-model builder the docx intake uses.
        var reprojected = new ComposeDocxProjectionBuilder().BuildContentModel(docxBytes);

        reprojected.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        var blocks = reprojected.Model.Blocks;
        blocks.Should().HaveCount(5);
        blocks[0].Kind.Should().Be(ComposeBlockKind.Heading);
        blocks[0].Level.Should().Be(1);
        blocks[0].Runs.Select(r => r.Text).Should().ContainSingle().Which.Should().Contain("MUTUAL NON-DISCLOSURE AGREEMENT");
        blocks[1].Kind.Should().Be(ComposeBlockKind.Heading);
        blocks[1].Level.Should().Be(2);
        blocks[2].Kind.Should().Be(ComposeBlockKind.Paragraph);
        blocks[3].Kind.Should().Be(ComposeBlockKind.ListItem);
        blocks[3].Ordered.Should().BeFalse();
        blocks[3].Runs.Select(r => r.Text).Should().ContainSingle().Which.Should().Contain("Trade secrets");
        blocks[4].Kind.Should().Be(ComposeBlockKind.Table);
        blocks[4].Table!.Rows.Should().ContainSingle().Which.Cells.Should().HaveCount(2);
        blocks[4].Table!.Rows[0].Cells[1].Blocks.Single().Runs.Single().Text.Should().Be("Two (2) years");

        // Every rendered paragraph carries a minted w14:paraId — the synthesized docx is a
        // first-class imported carrier for the next edit.
        blocks.Where(b => b.Kind != ComposeBlockKind.Table)
            .Should().OnlyContain(b => !string.IsNullOrEmpty(b.ParaId));
    }
}
