using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.ReviewMemo;

/// <summary>
/// Pure domain-logic tests (ADR-038 §2 path #6 — no mocks, no DI, no I/O) for
/// <see cref="ReviewMemoDocumentBuilder"/> (ai-advanced-capabilities-agreements-r1 task 051, spec FR-14).
/// Verifies the memo layout — title, doc/analysis metadata, per-section table {location, before, after,
/// why, golden-ref} — is built exactly from the persisted <see cref="ReviewMemoDocument"/>, with no
/// fabricated content (render-from-persisted).
/// </summary>
public class ReviewMemoDocumentBuilderTests
{
    private static ReviewMemoSection MakeSection(string location, string before, string after, string why, string standardRef) =>
        new(location, before, after, why, FlaggedClause: before, StandardRef: standardRef, RiskLevel: "High");

    [Fact]
    public void Build_MemoWithTwoSections_EmitsTitleMetadataAndTableMatchingRecordExactly()
    {
        // Arrange
        var memo = new ReviewMemoDocument(
            SchemaVersion: "review-memo-v1",
            OverallRisk: "High",
            SectionCount: 2,
            Sections: new[]
            {
                MakeSection(
                    "Section 4.2, para 2 (p. 3)",
                    before: "Confidential Information means information marked Confidential in writing.",
                    after: "Confidential Information means any information disclosed, whether or not marked.",
                    why: "Materially narrower than the standard.",
                    standardRef: "B5 - Use & disclosure obligations"),
                MakeSection(
                    "Section 6.1 (p. 4)",
                    before: "This Agreement shall be governed by the laws of Delaware.",
                    after: "This Agreement shall be governed by the laws of Delaware.",
                    why: "Standard governing-law clause; no deviation found worth negotiating.",
                    standardRef: "B9 - Governing law"),
            });

        // Act
        var model = ReviewMemoDocumentBuilder.Build(memo, documentName: "MSA - Acme Corp.docx", analysisName: "Acme MSA Review");

        // Assert — title heading present
        model.Blocks.Should().Contain(b => b.Kind == ComposeBlockKind.Heading && b.Runs.Any(r => r.Text == "Review Summary Memo"));

        // Assert — doc/analysis metadata paragraph carries the document name, analysis name, overall risk, and count
        var metadataBlock = model.Blocks.Should().ContainSingle(b =>
            b.Kind == ComposeBlockKind.Paragraph && b.Runs.Any(r => r.Text.Contains("Document:"))).Subject;
        var metadataText = string.Join("", metadataBlock.Runs.Select(r => r.Text));
        metadataText.Should().Contain("MSA - Acme Corp.docx");
        metadataText.Should().Contain("Acme MSA Review");
        metadataText.Should().Contain("High");
        metadataText.Should().Contain("2");

        // Assert — the table has exactly 1 header row + 1 row per section, 5 columns each
        var tableBlock = model.Blocks.Should().ContainSingle(b => b.Kind == ComposeBlockKind.Table).Subject;
        tableBlock.Table.Should().NotBeNull();
        tableBlock.Table!.Rows.Should().HaveCount(3); // header + 2 sections
        tableBlock.Table.Rows[0].Cells.Should().HaveCount(5);
        tableBlock.Table.Rows[0].Cells[0].IsHeader.Should().BeTrue();

        // Assert — each data row's cell text matches the persisted record's {location, before, after, why, golden-ref}
        // EXACTLY (render-from-persisted — no derived/fabricated text).
        for (var i = 0; i < memo.Sections.Count; i++)
        {
            var section = memo.Sections[i];
            var row = tableBlock.Table.Rows[i + 1];
            CellText(row.Cells[0]).Should().Be(section.Location);
            CellText(row.Cells[1]).Should().Be(section.Before);
            CellText(row.Cells[2]).Should().Be(section.After);
            CellText(row.Cells[3]).Should().Be(section.Why);
            CellText(row.Cells[4]).Should().Be(section.StandardRef);
        }
    }

    [Fact]
    public void Build_MemoWithZeroSections_EmitsHonestNoFindingsParagraph_NoTable()
    {
        // Arrange — defensive: the persist path requires >=1 section, but the builder must never throw
        // or emit an empty/misleading table when handed an edge-case zero-section record.
        var memo = new ReviewMemoDocument("review-memo-v1", "Low", 0, Array.Empty<ReviewMemoSection>());

        // Act
        var model = ReviewMemoDocumentBuilder.Build(memo, documentName: null, analysisName: null);

        // Assert
        model.Blocks.Should().NotContain(b => b.Kind == ComposeBlockKind.Table);
        model.Blocks.Should().Contain(b => b.Kind == ComposeBlockKind.Paragraph && b.Runs.Any(r => r.Text == "No flagged sections."));
    }

    [Fact]
    public void Build_NoDocumentOrAnalysisName_MetadataLineOmitsThemButKeepsRiskAndCount()
    {
        // Arrange — a memo read back with no resolvable analysis/document name (e.g. the analysis record
        // was subsequently deleted) must not crash or emit "Document: " with a blank value.
        var memo = new ReviewMemoDocument("review-memo-v1", "Medium", 1, new[]
        {
            MakeSection("Section 1", "Before text.", "After text.", "Why text.", "Standard ref."),
        });

        // Act
        var model = ReviewMemoDocumentBuilder.Build(memo, documentName: null, analysisName: "");

        // Assert
        var metadataBlock = model.Blocks.Should().ContainSingle(b =>
            b.Kind == ComposeBlockKind.Paragraph && b.Runs.Any(r => r.Text.Contains("Overall Risk"))).Subject;
        var metadataText = string.Join("", metadataBlock.Runs.Select(r => r.Text));
        metadataText.Should().NotContain("Document:");
        metadataText.Should().NotContain("Analysis:");
        metadataText.Should().Contain("Medium");
    }

    private static string CellText(ComposeTableCell cell) =>
        string.Join("", cell.Blocks.SelectMany(b => b.Runs).Select(r => r.Text));
}
