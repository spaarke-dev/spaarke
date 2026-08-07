using Azure.AI.DocumentIntelligence;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Task 040 Step-9.5 fix (MEDIUM-6) — <c>TextExtractorService.MapAnalyzeResultToLayout</c>: the
/// span-based mapping from an Azure DI <c>prebuilt-layout</c> <see cref="AnalyzeResult"/> to the
/// neutral <see cref="DocumentLayout"/> contract. Pure static logic exercised through the SDK's
/// <see cref="DocumentIntelligenceModelFactory"/>: table-cell paragraph DEDUP (the layout model
/// reports cell content twice — as loose paragraphs AND table cells), offset-based document-order
/// interleave, and missing-span end-placement.
/// </summary>
public class TextExtractorLayoutMappingTests
{
    private static DocumentSpan Span(int offset, int length)
        => DocumentIntelligenceModelFactory.DocumentSpan(offset, length);

    private static DocumentParagraph Paragraph(string content, int offset, ParagraphRole? role = null)
        => DocumentIntelligenceModelFactory.DocumentParagraph(
            role: role,
            content: content,
            boundingRegions: null,
            spans: new[] { Span(offset, content.Length) });

    [Fact]
    public void MapAnalyzeResultToLayout_TableCellParagraphs_AreDedupedIntoTheTableBlock()
    {
        // Document text layout: "Intro. " (0-6) | table cells "Term"(7-10) "Two years"(12-20) | "Outro."(22-27)
        var tableCell = DocumentIntelligenceModelFactory.DocumentTableCell(
            kind: DocumentTableCellKind.ColumnHeader,
            rowIndex: 0, columnIndex: 0, rowSpan: 1, columnSpan: 1,
            content: "Term",
            boundingRegions: null,
            spans: new[] { Span(7, 4) });
        var tableCell2 = DocumentIntelligenceModelFactory.DocumentTableCell(
            kind: null,
            rowIndex: 0, columnIndex: 1, rowSpan: 1, columnSpan: 1,
            content: "Two years",
            boundingRegions: null,
            spans: new[] { Span(12, 9) });
        var table = DocumentIntelligenceModelFactory.DocumentTable(
            rowCount: 1, columnCount: 2,
            cells: new[] { tableCell, tableCell2 },
            boundingRegions: null,
            spans: new[] { Span(7, 14) });

        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            modelId: "prebuilt-layout",
            content: "Intro. Term Two years Outro.",
            pages: new[]
            {
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: new[] { Span(0, 28) }),
            },
            paragraphs: new[]
            {
                Paragraph("Intro.", 0),
                // The layout model ALSO reports the cell contents as paragraphs — these two must dedupe.
                Paragraph("Term", 7),
                Paragraph("Two years", 12),
                Paragraph("Outro.", 22),
            },
            tables: new[] { table });

        var layout = TextExtractorService.MapAnalyzeResultToLayout(result);

        layout.PageCount.Should().Be(1);
        layout.Blocks.Should().HaveCount(3, "the two cell paragraphs dedupe into the table block");
        layout.Blocks[0].Paragraph!.Text.Should().Be("Intro.");
        layout.Blocks[1].Table.Should().NotBeNull();
        layout.Blocks[1].Table!.Cells.Should().HaveCount(2);
        layout.Blocks[1].Table!.Cells[0].IsHeader.Should().BeTrue();
        layout.Blocks[2].Paragraph!.Text.Should().Be("Outro.");
    }

    [Fact]
    public void MapAnalyzeResultToLayout_RolesAndOrder_MapAndInterleaveByOffset()
    {
        var result = DocumentIntelligenceModelFactory.AnalyzeResult(
            modelId: "prebuilt-layout",
            content: "ignored",
            pages: new[]
            {
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 1, spans: new[] { Span(0, 60) }),
                DocumentIntelligenceModelFactory.DocumentPage(pageNumber: 2, spans: new[] { Span(60, 40) }),
            },
            paragraphs: new[]
            {
                // Deliberately out of offset order — the mapper must sort.
                Paragraph("Body prose.", 30),
                Paragraph("NDA", 0, ParagraphRole.Title),
                Paragraph("1. Term", 10, ParagraphRole.SectionHeading),
                Paragraph("Page 1", 50, ParagraphRole.PageNumber),
            },
            tables: null);

        var layout = TextExtractorService.MapAnalyzeResultToLayout(result);

        layout.PageCount.Should().Be(2);
        layout.Blocks.Should().HaveCount(4);
        layout.Blocks.Select(b => b.Paragraph!.Role).Should().ContainInOrder(
            DocumentLayoutParagraphRole.Title,
            DocumentLayoutParagraphRole.SectionHeading,
            DocumentLayoutParagraphRole.Body,
            DocumentLayoutParagraphRole.PageNumber);
    }
}
