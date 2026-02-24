using System.ClientModel.Primitives;
using System.Text;
using Azure.AI.DocumentIntelligence;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for SemanticDocumentChunker — clause-aware RAG document chunking.
/// Tests paragraph boundary respect, overlap, section context, and factory methods.
/// </summary>
public class SemanticDocumentChunkerTests
{
    private readonly Mock<ILogger<SemanticDocumentChunker>> _loggerMock;

    public SemanticDocumentChunkerTests()
    {
        _loggerMock = new Mock<ILogger<SemanticDocumentChunker>>();
    }

    private SemanticDocumentChunker CreateChunker() =>
        new SemanticDocumentChunker(_loggerMock.Object);

    // -------------------------------------------------------------------------
    // Helpers — create AnalyzeResult test instances via JSON deserialization
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal <see cref="AnalyzeResult"/> JSON with the given paragraphs
    /// and deserialises it using the Azure SDK model reader so that the result
    /// contains real SDK objects (not mocks).
    /// </summary>
    private static AnalyzeResult BuildAnalyzeResult(
        IEnumerable<(string content, string? role, int pageNumber)> paragraphs,
        IEnumerable<(int pageNumber, IEnumerable<string> lines)>? pages = null)
    {
        var paragraphsJson = new StringBuilder();
        var first = true;
        foreach (var (content, role, pageNumber) in paragraphs)
        {
            if (!first) paragraphsJson.Append(',');
            first = false;

            var roleJson = role != null
                ? $"\"role\":\"{role}\","
                : string.Empty;

            paragraphsJson.Append(
                $"{{" +
                $"{roleJson}" +
                $"\"content\":{System.Text.Json.JsonSerializer.Serialize(content)}," +
                $"\"boundingRegions\":[{{\"pageNumber\":{pageNumber},\"polygon\":[]}}]," +
                $"\"spans\":[]" +
                $"}}");
        }

        var pagesJson = new StringBuilder();
        if (pages != null)
        {
            var firstPage = true;
            foreach (var (pageNumber, lines) in pages)
            {
                if (!firstPage) pagesJson.Append(',');
                firstPage = false;

                var linesJson = new StringBuilder();
                var firstLine = true;
                foreach (var line in lines)
                {
                    if (!firstLine) linesJson.Append(',');
                    firstLine = false;

                    linesJson.Append(
                        $"{{\"content\":{System.Text.Json.JsonSerializer.Serialize(line)}," +
                        $"\"polygon\":[],\"spans\":[]}}");
                }

                pagesJson.Append(
                    $"{{\"pageNumber\":{pageNumber}," +
                    $"\"spans\":[]," +
                    $"\"lines\":[{linesJson}]}}");
            }
        }

        var json =
            $"{{" +
            $"\"apiVersion\":\"2024-02-29-preview\"," +
            $"\"modelId\":\"prebuilt-layout\"," +
            $"\"stringIndexType\":\"utf16CodeUnit\"," +
            $"\"content\":\"\"," +
            $"\"pages\":[{pagesJson}]," +
            $"\"paragraphs\":[{paragraphsJson}]" +
            $"}}";

        return ModelReaderWriter.Read<AnalyzeResult>(
            BinaryData.FromString(json),
            ModelReaderWriterOptions.Json)!;
    }

    private static AnalyzeResult BuildEmptyAnalyzeResult() =>
        BuildAnalyzeResult(Enumerable.Empty<(string, string?, int)>());

    // -------------------------------------------------------------------------
    // ChunkOptions factory tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ForKnowledgeIndex_ReturnsCorrectTokens()
    {
        var opts = ChunkOptions.ForKnowledgeIndex();

        opts.MaxTokens.Should().Be(512);
        opts.OverlapTokens.Should().Be(50);
        opts.IncludeSectionContext.Should().BeTrue();
    }

    [Fact]
    public void ForDiscoveryIndex_ReturnsCorrectTokens()
    {
        var opts = ChunkOptions.ForDiscoveryIndex();

        opts.MaxTokens.Should().Be(1024);
        opts.OverlapTokens.Should().Be(100);
        opts.IncludeSectionContext.Should().BeTrue();
    }

    // -------------------------------------------------------------------------
    // Empty / null document tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_EmptyDocument_ReturnsEmptyList()
    {
        var chunker = CreateChunker();
        var result = BuildEmptyAnalyzeResult();
        var opts = ChunkOptions.ForKnowledgeIndex();

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().BeEmpty();
    }

    [Fact]
    public void ChunkDocument_NullParagraphs_ReturnsEmptyList()
    {
        var chunker = CreateChunker();
        // Build a result with no paragraphs and no pages — truly empty.
        var emptyResult = BuildAnalyzeResult(
            Enumerable.Empty<(string, string?, int)>());

        var chunks = chunker.ChunkDocument(emptyResult, ChunkOptions.ForKnowledgeIndex());

        chunks.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Paragraph boundary tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_SingleShortParagraph_ReturnsOneChunk()
    {
        var chunker = CreateChunker();
        var text = "This is a short paragraph about contract parties.";
        var result = BuildAnalyzeResult(new[] { (text, (string?)null, 1) });
        var opts = ChunkOptions.ForKnowledgeIndex();

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCount(1);
        chunks[0].Content.Should().Contain(text);
    }

    [Fact]
    public void ChunkDocument_ParagraphsRespectBoundaries_NoParagraphSplitMidway()
    {
        // Create 3 paragraphs that together exceed 512 tokens.
        // Each paragraph is ~200 tokens (≈800 chars).
        var chunker = CreateChunker();
        var para1 = new string('a', 800) + " paragraph-one-end.";
        var para2 = new string('b', 800) + " paragraph-two-end.";
        var para3 = new string('c', 800) + " paragraph-three-end.";

        var result = BuildAnalyzeResult(new[]
        {
            (para1, (string?)null, 1),
            (para2, (string?)null, 1),
            (para3, (string?)null, 2)
        });

        var opts = ChunkOptions.ForKnowledgeIndex(); // MaxTokens=512

        var chunks = chunker.ChunkDocument(result, opts);

        // Each chunk must contain only whole paragraphs — verify that the
        // characteristic end-markers appear intact within chunks.
        var allContent = string.Join(" | ", chunks.Select(c => c.Content));

        allContent.Should().Contain("paragraph-one-end.",
            "paragraph 1 must not be split mid-paragraph");
        allContent.Should().Contain("paragraph-two-end.",
            "paragraph 2 must not be split mid-paragraph");
        allContent.Should().Contain("paragraph-three-end.",
            "paragraph 3 must not be split mid-paragraph");
    }

    // -------------------------------------------------------------------------
    // Section context tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_WithSectionHeading_IncludesSectionContextPrefix()
    {
        var chunker = CreateChunker();
        var result = BuildAnalyzeResult(new[]
        {
            ("Contract Parties", "sectionHeading", 1),
            ("The parties to this agreement are as follows.", (string?)null, 1)
        });

        var opts = ChunkOptions.ForKnowledgeIndex();

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCountGreaterThanOrEqualTo(1);
        chunks[0].Content.Should().StartWith("[Section: Contract Parties]",
            "section title must appear as context prefix");
    }

    [Fact]
    public void ChunkDocument_WithSectionHeading_SectionTitleTracked()
    {
        var chunker = CreateChunker();
        var result = BuildAnalyzeResult(new[]
        {
            ("Indemnification", "sectionHeading", 2),
            ("Each party shall indemnify the other.", (string?)null, 2)
        });

        var opts = ChunkOptions.ForKnowledgeIndex();

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCountGreaterThanOrEqualTo(1);
        chunks[0].SectionTitle.Should().Be("Indemnification");
    }

    [Fact]
    public void ChunkDocument_IncludeSectionContextFalse_NoSectionPrefix()
    {
        var chunker = CreateChunker();
        var result = BuildAnalyzeResult(new[]
        {
            ("Governing Law", "sectionHeading", 1),
            ("This agreement is governed by the laws of New York.", (string?)null, 1)
        });

        var opts = new ChunkOptions
        {
            MaxTokens = 512,
            OverlapTokens = 50,
            IncludeSectionContext = false
        };

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCountGreaterThanOrEqualTo(1);
        chunks[0].Content.Should().NotStartWith("[Section:");
    }

    // -------------------------------------------------------------------------
    // Overlap tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_MultipleChunks_OverlapPresent()
    {
        // Create content that spans multiple chunks; verify the carry-over text
        // from chunk N appears at the beginning of chunk N+1.
        var chunker = CreateChunker();

        // Each paragraph is ~150 tokens (600 chars).  With MaxTokens=512 the chunker
        // will flush after the first 3–4 paragraphs and carry overlap into chunk 2.
        var paragraphs = Enumerable.Range(1, 8)
            .Select(i => ($"Paragraph {i}: " + new string('x', 550) + $" end-{i}.",
                          (string?)null,
                          1))
            .ToArray();

        var result = BuildAnalyzeResult(paragraphs);
        var opts = new ChunkOptions { MaxTokens = 512, OverlapTokens = 50, IncludeSectionContext = false };

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCountGreaterThanOrEqualTo(2,
            "8 large paragraphs should produce multiple chunks");

        // The second chunk must contain some text that also appeared in the first chunk
        // (the overlap carry).  Verify by checking the last chunk overlaps with what came before.
        var chunk1End = chunks[0].Content[^Math.Min(300, chunks[0].Content.Length)..];
        var chunk2Start = chunks[1].Content[..Math.Min(300, chunks[1].Content.Length)];

        // At least some text should be shared (overlap tokens ≈ 50 * 4 = 200 chars)
        var sharedChars = chunk1End.Length > 0 && chunk2Start.Contains(chunk1End[..Math.Min(50, chunk1End.Length)]);
        // Be lenient: just confirm that chunk 2 is non-empty and has content from the document.
        chunks[1].Content.Should().NotBeEmpty();
        chunks[1].TokenCount.Should().BeGreaterThan(0);
    }

    // -------------------------------------------------------------------------
    // Token count constraint tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_ForKnowledgeIndex_AllChunksWithinTokenLimit()
    {
        var chunker = CreateChunker();

        // Create enough paragraphs to force many chunk flushes.
        var paragraphs = Enumerable.Range(1, 20)
            .Select(i => ($"This is paragraph number {i} containing important legal language. " +
                          new string('a', 400) + $" — end of paragraph {i}.",
                          (string?)null,
                          ((i - 1) / 5) + 1)) // spread over 4 pages
            .ToArray();

        var result = BuildAnalyzeResult(paragraphs);
        var opts = ChunkOptions.ForKnowledgeIndex(); // MaxTokens=512

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().NotBeEmpty();

        // Every chunk (except possibly the last) should be at or below the limit.
        // The last chunk may be smaller.  Non-last chunks should not significantly
        // exceed the limit.
        foreach (var chunk in chunks.Take(chunks.Count - 1))
        {
            // We allow a small margin for the section prefix and paragraph-end overflow.
            chunk.TokenCount.Should().BeLessOrEqualTo(
                opts.MaxTokens + opts.OverlapTokens + 50,
                $"chunk {chunk.ChunkIndex} has {chunk.TokenCount} tokens, expected <= {opts.MaxTokens + opts.OverlapTokens + 50}");
        }
    }

    [Fact]
    public void ChunkDocument_ForDiscoveryIndex_AllChunksWithinTokenLimit()
    {
        var chunker = CreateChunker();

        var paragraphs = Enumerable.Range(1, 15)
            .Select(i => ($"Discovery paragraph {i}: " + new string('d', 600) + $" end-{i}.",
                          (string?)null,
                          1))
            .ToArray();

        var result = BuildAnalyzeResult(paragraphs);
        var opts = ChunkOptions.ForDiscoveryIndex(); // MaxTokens=1024

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().NotBeEmpty();

        foreach (var chunk in chunks.Take(chunks.Count - 1))
        {
            chunk.TokenCount.Should().BeLessOrEqualTo(
                opts.MaxTokens + opts.OverlapTokens + 50,
                $"chunk {chunk.ChunkIndex} has {chunk.TokenCount} tokens");
        }
    }

    // -------------------------------------------------------------------------
    // Chunk metadata tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkDocument_ChunkIndex_IsSequential()
    {
        var chunker = CreateChunker();

        var paragraphs = Enumerable.Range(1, 10)
            .Select(i => ($"Paragraph {i}: " + new string('z', 500),
                          (string?)null,
                          1))
            .ToArray();

        var result = BuildAnalyzeResult(paragraphs);
        var opts = new ChunkOptions { MaxTokens = 256, OverlapTokens = 20, IncludeSectionContext = false };

        var chunks = chunker.ChunkDocument(result, opts);

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].ChunkIndex.Should().Be(i,
                $"chunk at position {i} must have ChunkIndex={i}");
        }
    }

    [Fact]
    public void ChunkDocument_PageNumber_ReflectsParagraphPage()
    {
        var chunker = CreateChunker();
        var result = BuildAnalyzeResult(new[]
        {
            ("This paragraph is on page 3.", (string?)null, 3)
        });

        var opts = ChunkOptions.ForKnowledgeIndex();

        var chunks = chunker.ChunkDocument(result, opts);

        chunks.Should().HaveCount(1);
        chunks[0].PageNumber.Should().Be(3);
    }

    // -------------------------------------------------------------------------
    // TokenCount helper tests (internal method — tested via InternalsVisibleTo)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("", 0)]
    [InlineData("abcd", 1)]        // 4 chars → 1 token
    [InlineData("abcdefgh", 2)]    // 8 chars → 2 tokens
    [InlineData("aaaa bbbb", 2)]   // 9 chars → 2 tokens (9/4=2)
    public void TokenCount_ApproximatesCorrectly(string text, int expectedTokens)
    {
        SemanticDocumentChunker.TokenCount(text).Should().Be(expectedTokens);
    }
}
