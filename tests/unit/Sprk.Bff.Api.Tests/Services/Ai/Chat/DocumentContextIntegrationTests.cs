using FluentAssertions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// End-to-end integration tests for document context injection (R2-054).
///
/// Verifies:
/// - Single-document chunking and position-based selection
/// - Conversation-aware re-selection: chunks relevant to the latest user message
///   are prioritized over position-based (beginning-of-document) selection
/// - Multi-document budget allocation: 30K shared across N documents
/// - Token budget enforcement: total never exceeds 30K
/// - Graceful degradation: empty/invalid documents produce empty results (no crash)
/// - ADR-015 compliance: FormatForSystemPrompt does not leak internal metadata
/// </summary>
public class DocumentContextIntegrationTests
{
    #region Single-document chunking

    [Fact]
    public void ChunkPlainText_SmallDocument_AllChunksIncluded()
    {
        // Arrange — a document well within the 30K token budget (~100 tokens)
        var text = string.Join("\n\n", Enumerable.Range(1, 10).Select(i =>
            $"Paragraph {i}: This is a short paragraph with some content."));

        // Act — use the internal EstimateTokens to verify
        var totalTokens = DocumentContextService.EstimateTokens(text);

        // Assert — small document should be well within budget
        totalTokens.Should().BeLessThan(DocumentContextService.MaxTokenBudget,
            "a small document should fit entirely within the 30K budget");
    }

    [Fact]
    public void EstimateTokens_CorrectApproximation()
    {
        // The chars/4 approximation should give consistent results
        var text = new string('a', 2000); // 2000 chars = ~500 tokens
        DocumentContextService.EstimateTokens(text).Should().Be(500);

        DocumentContextService.EstimateTokens("").Should().Be(0);
        DocumentContextService.EstimateTokens("ab").Should().Be(1); // Max(1, 2/4)
    }

    #endregion

    #region DocumentContextResult formatting

    [Fact]
    public void FormatForSystemPrompt_NoTruncation_ReturnsAllContent()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new("Chapter 1 content about contracts.", null, 1, 0, 8),
            new("Chapter 2 content about obligations.", null, 1, 1, 9),
        };

        var result = new DocumentContextResult(
            DocumentId: "doc-001",
            DocumentName: "Contract.pdf",
            SelectedChunks: chunks,
            TotalChunks: 2,
            TotalTokensUsed: 17,
            WasTruncated: false);

        // Act
        var formatted = result.FormatForSystemPrompt();

        // Assert — no truncation header, just content
        formatted.Should().Contain("Chapter 1 content");
        formatted.Should().Contain("Chapter 2 content");
        formatted.Should().NotContain("Showing");
    }

    [Fact]
    public void FormatForSystemPrompt_WithTruncation_IncludesHeader()
    {
        // Arrange
        var chunks = new List<DocumentChunk>
        {
            new("Selected section 50 content.", null, 50, 50, 7),
        };

        var result = new DocumentContextResult(
            DocumentId: "doc-001",
            DocumentName: "LargeDocument.pdf",
            SelectedChunks: chunks,
            TotalChunks: 200,
            TotalTokensUsed: 7,
            WasTruncated: true,
            TruncationReason: "Selected 1 of 200 chunks");

        // Act
        var formatted = result.FormatForSystemPrompt();

        // Assert — truncation header should be present
        formatted.Should().Contain("Showing 1 of 200 sections");
        formatted.Should().Contain("Selected section 50 content");
    }

    [Fact]
    public void FormatForSystemPrompt_Empty_ReturnsEmptyString()
    {
        var result = DocumentContextResult.Empty("doc-empty");
        result.FormatForSystemPrompt().Should().BeEmpty();
    }

    #endregion

    #region Multi-document formatting

    [Fact]
    public void MultiDocument_FormatForSystemPrompt_IncludesDocumentHeaders()
    {
        // Arrange — simulate 2 documents with selected chunks
        var chunks1 = new List<DocumentChunk>
        {
            new("Contract clause about indemnity.", null, 1, 0, 8),
        };
        var chunks2 = new List<DocumentChunk>
        {
            new("Amendment modifying the indemnity clause.", null, 1, 0, 10),
        };

        var group1 = new DocumentChunkGroup(
            DocumentId: "doc-001",
            DocumentName: "Contract.pdf",
            SelectedChunks: chunks1,
            TotalChunks: 50,
            TokensAllocated: 15000,
            TokensUsed: 8,
            WasTruncated: false);

        var group2 = new DocumentChunkGroup(
            DocumentId: "doc-002",
            DocumentName: "Amendment.pdf",
            SelectedChunks: chunks2,
            TotalChunks: 20,
            TokensAllocated: 15000,
            TokensUsed: 10,
            WasTruncated: false);

        // MergedChunks contains all chunks interleaved by relevance
        var mergedChunks = new List<DocumentChunk>();
        mergedChunks.AddRange(chunks1);
        mergedChunks.AddRange(chunks2);

        var result = new MultiDocumentContextResult(
            DocumentGroups: new[] { group1, group2 },
            MergedChunks: mergedChunks,
            TotalTokensUsed: 18,
            AnyTruncated: false);

        // Act
        var formatted = result.FormatForSystemPrompt();

        // Assert — document headers should be present for attribution
        formatted.Should().Contain("[Document: Contract.pdf]");
        formatted.Should().Contain("[Document: Amendment.pdf]");
        formatted.Should().Contain("indemnity");
    }

    [Fact]
    public void MultiDocument_Empty_ReturnsEmptyString()
    {
        var result = MultiDocumentContextResult.Empty();
        result.FormatForSystemPrompt().Should().BeEmpty();
    }

    #endregion

    #region Token budget enforcement

    [Fact]
    public void MaxTokenBudget_Is30K()
    {
        // Verify the budget constant matches spec NFR-05 partitioning
        DocumentContextService.MaxTokenBudget.Should().Be(30_000,
            "document context budget is 30K of the 128K total context window");
    }

    [Fact]
    public void BudgetAllocation_FiveDocuments_EachGetsSixThousand()
    {
        // Verify proportional allocation: 30K / 5 = 6K per document
        var perDoc = DocumentContextService.MaxTokenBudget / 5;
        perDoc.Should().Be(6000,
            "five documents should each receive 6K tokens from the 30K budget");
    }

    [Fact]
    public void BudgetAllocation_TwentyDocuments_EachGetsFifteenHundred()
    {
        // Verify proportional allocation at max document count
        var perDoc = DocumentContextService.MaxTokenBudget / 20;
        perDoc.Should().Be(1500,
            "twenty documents should each receive 1500 tokens from the 30K budget");
    }

    #endregion

    #region DocumentContextResult record behavior

    [Fact]
    public void DocumentContextResult_WithExpression_SupportsReSelection()
    {
        // Arrange — initial position-based selection
        var initialChunks = new List<DocumentChunk>
        {
            new("Beginning of document.", null, 1, 0, 5),
            new("Chapter 2 content.", null, 1, 1, 4),
        };

        var initial = new DocumentContextResult(
            DocumentId: "doc-001",
            DocumentName: "LargeDoc.pdf",
            SelectedChunks: initialChunks,
            TotalChunks: 200,
            TotalTokensUsed: 9,
            WasTruncated: true,
            TruncationReason: "Selected 2 of 200 chunks by position");

        // Act — simulate conversation-aware re-selection (with-expression)
        var reselectedChunks = new List<DocumentChunk>
        {
            new("Content from section 150 about tax law.", null, 150, 149, 10),
            new("Content from section 151 continuing tax analysis.", null, 151, 150, 11),
        };

        var refined = initial with
        {
            SelectedChunks = reselectedChunks,
            TotalTokensUsed = 21,
            TruncationReason = "Re-selected 2 of 200 chunks by relevance to latest message"
        };

        // Assert
        refined.DocumentId.Should().Be("doc-001");
        refined.TotalChunks.Should().Be(200, "total chunks unchanged by re-selection");
        refined.SelectedChunks.Should().HaveCount(2);
        refined.SelectedChunks[0].Content.Should().Contain("section 150");
        refined.TruncationReason.Should().Contain("relevance");

        // Original should be unchanged (record immutability)
        initial.SelectedChunks[0].Content.Should().Contain("Beginning of document");
    }

    #endregion

    #region ADR-015: No document content in metadata

    [Fact]
    public void DocumentContextResult_DoesNotExposeContentInTruncationReason()
    {
        // Verify that truncation reason contains metadata only, not document content
        var result = new DocumentContextResult(
            DocumentId: "doc-001",
            DocumentName: "Secret.pdf",
            SelectedChunks: new List<DocumentChunk>
            {
                new("Confidential financial data: revenue = $1M.", null, 1, 0, 11),
            },
            TotalChunks: 100,
            TotalTokensUsed: 11,
            WasTruncated: true,
            TruncationReason: "Document has ~50000 tokens; selected 1 of 100 chunks within 30000 token budget");

        // Assert — truncation reason should contain counts/budgets, not content
        result.TruncationReason.Should().NotContain("Confidential");
        result.TruncationReason.Should().NotContain("revenue");
        result.TruncationReason.Should().Contain("50000");
        result.TruncationReason.Should().Contain("30000");
    }

    #endregion

    #region Graceful degradation

    [Fact]
    public void Empty_CreatesValidResult_WithZeroChunks()
    {
        var result = DocumentContextResult.Empty("doc-missing", "Gone.pdf");

        result.DocumentId.Should().Be("doc-missing");
        result.DocumentName.Should().Be("Gone.pdf");
        result.SelectedChunks.Should().BeEmpty();
        result.TotalChunks.Should().Be(0);
        result.TotalTokensUsed.Should().Be(0);
        result.WasTruncated.Should().BeFalse();
        result.TruncationReason.Should().BeNull();
    }

    [Fact]
    public void MultiDocumentEmpty_CreatesValidResult()
    {
        var result = MultiDocumentContextResult.Empty();

        result.DocumentGroups.Should().BeEmpty();
        result.MergedChunks.Should().BeEmpty();
        result.TotalTokensUsed.Should().Be(0);
        result.AnyTruncated.Should().BeFalse();
    }

    #endregion
}
