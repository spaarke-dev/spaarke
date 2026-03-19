using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// End-to-end tests for <see cref="DocumentContextService"/> covering:
/// - Chunking accuracy: relevant chunks are selected for a given query (verified by content inspection)
/// - Token budget enforcement: NFR-05 128K total, 30K document budget never exceeded
/// - Multi-document aggregation with 5+ documents within shared 30K budget
/// - Conversation-aware re-selection: different queries produce different chunk selections
/// - Graceful degradation when budget is exceeded
///
/// All tests use mock document content and mock embeddings for deterministic results.
/// No real Azure AI Search or Dataverse calls are made.
/// </summary>
public class DocumentContextE2ETests
{
    private const int MaxDocumentBudget = 30_000;

    // -------------------------------------------------------------------
    // Helpers: build mock infrastructure
    // -------------------------------------------------------------------

    /// <summary>
    /// Creates a <see cref="DocumentContextService"/> with fully mocked dependencies.
    /// The mock document service, file operations, text extractor, and OpenAI client
    /// are pre-configured to return deterministic content for testing.
    /// </summary>
    private static (
        DocumentContextService Sut,
        Mock<IDocumentDataverseService> DocService,
        Mock<ISpeFileOperations> SpeOps,
        Mock<ITextExtractor> TextExtractor,
        Mock<IOpenAiClient> OpenAiClient)
        CreateServiceWithMocks()
    {
        var docService = new Mock<IDocumentDataverseService>();
        var speOps = new Mock<ISpeFileOperations>();
        var textExtractor = new Mock<ITextExtractor>();
        var openAiClient = new Mock<IOpenAiClient>();
        var logger = new Mock<ILogger>();

        var sut = new DocumentContextService(
            docService.Object,
            speOps.Object,
            textExtractor.Object,
            openAiClient.Object,
            logger.Object);

        return (sut, docService, speOps, textExtractor, openAiClient);
    }

    /// <summary>
    /// Generates a plain-text document with N distinct sections.
    /// Each section is uniquely identifiable by its content string, enabling
    /// content-based verification of chunk selection.
    /// </summary>
    private static string GenerateDocumentWithSections(int sectionCount, int wordsPerSection = 100)
    {
        var sb = new StringBuilder();
        for (var i = 1; i <= sectionCount; i++)
        {
            sb.AppendLine($"=== Section {i}: Topic-{i} ===");
            sb.AppendLine();
            // Generate distinct content per section so we can verify which sections were selected
            for (var w = 0; w < wordsPerSection; w++)
            {
                sb.Append($"section{i}word{w} ");
            }
            sb.AppendLine();
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates a large document that exceeds the 30K token budget when chunked.
    /// Each paragraph has distinct content for chunk selection verification.
    /// </summary>
    private static string GenerateLargeDocument(int targetTokens)
    {
        // Each char is ~0.25 tokens, so multiply target by 4 for char count
        var sb = new StringBuilder();
        var section = 0;
        while (DocumentContextService.EstimateTokens(sb.ToString()) < targetTokens)
        {
            section++;
            sb.AppendLine($"=== Large Document Section {section} ===");
            sb.AppendLine();
            // ~2000 chars = ~500 tokens per paragraph
            for (var w = 0; w < 200; w++)
            {
                sb.Append($"largesection{section}word{w} ");
            }
            sb.AppendLine();
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Configures mocks so that a document with the given ID returns the specified text content.
    /// </summary>
    private static void SetupDocumentMocks(
        Mock<IDocumentDataverseService> docService,
        Mock<ISpeFileOperations> speOps,
        Mock<ITextExtractor> textExtractor,
        string documentId,
        string documentName,
        string textContent)
    {
        docService
            .Setup(d => d.GetDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = documentId,
                Name = documentName,
                FileName = documentName,
                GraphDriveId = $"drive-{documentId}",
                GraphItemId = $"item-{documentId}"
            });

        speOps
            .Setup(s => s.DownloadFileAsync(
                $"drive-{documentId}", $"item-{documentId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes(textContent)));

        textExtractor
            .Setup(t => t.ExtractAsync(It.IsAny<Stream>(), documentName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TextExtractionResult.Succeeded(textContent, TextExtractionMethod.Native));
    }

    /// <summary>
    /// Sets up mock embeddings that produce deterministic cosine similarity scores.
    /// Chunks containing <paramref name="targetKeyword"/> get high similarity (close to 1.0),
    /// while other chunks get low similarity (close to 0.0). This enables testing
    /// conversation-aware chunk selection deterministically.
    /// </summary>
    private static void SetupEmbeddingMocks(
        Mock<IOpenAiClient> openAiClient,
        string targetKeyword,
        int totalChunkCount)
    {
        // User message embedding: a unit vector along dimension 0
        var messageEmbedding = new float[128];
        messageEmbedding[0] = 1.0f;

        openAiClient
            .Setup(o => o.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadOnlyMemory<float>(messageEmbedding));

        // Chunk embeddings: chunks matching the keyword get aligned vector,
        // others get orthogonal vector
        openAiClient
            .Setup(o => o.GenerateEmbeddingsAsync(
                It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, string?, int?, CancellationToken>((texts, _, _, _) =>
            {
                var textList = texts.ToList();
                var embeddings = new List<ReadOnlyMemory<float>>();
                foreach (var text in textList)
                {
                    var embedding = new float[128];
                    if (text.Contains(targetKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        // High similarity: aligned with message embedding
                        embedding[0] = 0.95f;
                        embedding[1] = 0.05f;
                    }
                    else
                    {
                        // Low similarity: orthogonal to message embedding
                        embedding[0] = 0.05f;
                        embedding[1] = 0.95f;
                    }
                    embeddings.Add(new ReadOnlyMemory<float>(embedding));
                }
                return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(embeddings);
            });
    }

    // -------------------------------------------------------------------
    // Test: RelevantChunksSelected
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that when a user queries about section 3 content, the returned chunks
    /// contain section 3 text (conversation-aware chunk selection).
    /// </summary>
    [Fact]
    public async Task RelevantChunksSelected_QueryForSection3_ReturnsSection3Content()
    {
        // Arrange — create a large document that exceeds 30K budget with 100 distinct sections
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentText = GenerateLargeDocument(50_000); // exceeds 30K budget
        SetupDocumentMocks(docService, speOps, textExtractor, "doc-001", "Contract.pdf", documentText);

        // Mock embeddings: chunks containing "largesection3" get high similarity
        SetupEmbeddingMocks(openAiClient, "largesection3", totalChunkCount: 200);

        // Act — query about section 3
        var result = await sut.InjectDocumentContextAsync(
            "doc-001",
            httpContext: null,
            latestUserMessage: "Tell me about section 3");

        // Assert — selected chunks should contain section 3 content
        result.SelectedChunks.Should().NotBeEmpty("chunks should be selected for the query");
        result.WasTruncated.Should().BeTrue("document exceeds 30K budget");

        // At least one selected chunk should contain the target section content
        result.SelectedChunks
            .Should().Contain(c => c.Content.Contains("largesection3"),
                "conversation-aware selection should prioritize chunks matching the query topic");

        // Token budget enforcement
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "selected chunks must stay within the 30K document token budget");
    }

    // -------------------------------------------------------------------
    // Test: ConversationAwareReselection
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that different user messages produce different chunk selections (FR-03).
    /// First query targets section 1 content, second query targets section 90 content.
    /// The two selections should differ.
    /// </summary>
    [Fact]
    public async Task ConversationAwareReselection_DifferentQueries_ProduceDifferentChunkSelections()
    {
        // Arrange — create a document that exceeds budget
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentText = GenerateLargeDocument(60_000);
        SetupDocumentMocks(docService, speOps, textExtractor, "doc-resel", "LargeContract.pdf", documentText);

        // --- Query 1: about section 1 ---
        SetupEmbeddingMocks(openAiClient, "largesection1", totalChunkCount: 200);

        var result1 = await sut.InjectDocumentContextAsync(
            "doc-resel", httpContext: null, latestUserMessage: "Explain section 1");

        // --- Query 2: about section 50 ---
        SetupEmbeddingMocks(openAiClient, "largesection50", totalChunkCount: 200);

        // Re-setup document mocks since stream was consumed
        SetupDocumentMocks(docService, speOps, textExtractor, "doc-resel", "LargeContract.pdf", documentText);

        var result2 = await sut.InjectDocumentContextAsync(
            "doc-resel", httpContext: null, latestUserMessage: "Explain section 50");

        // Assert — different queries should select different chunks
        result1.SelectedChunks.Should().NotBeEmpty();
        result2.SelectedChunks.Should().NotBeEmpty();

        // Extract chunk indices for comparison
        var indices1 = result1.SelectedChunks.Select(c => c.ChunkIndex).ToHashSet();
        var indices2 = result2.SelectedChunks.Select(c => c.ChunkIndex).ToHashSet();

        // The two chunk selections should NOT be identical
        indices1.Should().NotBeEquivalentTo(indices2,
            "different user messages should produce different chunk selections (FR-03 conversation-aware re-selection)");

        // Result 1 should contain section 1 content
        result1.SelectedChunks
            .Should().Contain(c => c.Content.Contains("largesection1"),
                "first query should select section 1 chunks");

        // Result 2 should contain section 50 content
        result2.SelectedChunks
            .Should().Contain(c => c.Content.Contains("largesection50"),
                "second query should select section 50 chunks");
    }

    // -------------------------------------------------------------------
    // Test: TokenBudgetNeverExceeded
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that even with very large documents (exceeding 128K total),
    /// the injected document context never exceeds the 30K document token budget (NFR-05).
    /// </summary>
    [Fact]
    public async Task TokenBudgetNeverExceeded_LargeDocument_StaysWithin30KBudget()
    {
        // Arrange — create a document with ~150K tokens (far exceeding any budget)
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var hugeDocumentText = GenerateLargeDocument(150_000);
        var actualTokens = DocumentContextService.EstimateTokens(hugeDocumentText);
        actualTokens.Should().BeGreaterThan(128_000,
            "test document must exceed 128K total context to test budget enforcement");

        SetupDocumentMocks(docService, speOps, textExtractor, "doc-huge", "Massive.pdf", hugeDocumentText);
        SetupEmbeddingMocks(openAiClient, "largesection10", totalChunkCount: 500);

        // Act — inject the oversized document
        var result = await sut.InjectDocumentContextAsync(
            "doc-huge", httpContext: null, latestUserMessage: "Tell me about section 10");

        // Assert — budget enforcement
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "NFR-05: document context injection must never exceed the 30K token budget");
        result.WasTruncated.Should().BeTrue(
            "a 150K-token document must be truncated to fit within 30K budget");
        result.TruncationReason.Should().NotBeNullOrWhiteSpace(
            "truncation reason should explain why content was reduced");

        // Verify the 128K total budget partitioning: 30K document is part of the 128K total
        // The service enforces 30K; the overall 128K is enforced at the agent level
        DocumentContextService.MaxTokenBudget.Should().BeLessOrEqualTo(128_000,
            "document budget must be a subset of the 128K total context window");
    }

    // -------------------------------------------------------------------
    // Test: DocumentBudgetPerDoc_30K
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that single document injection stays within the 30K token budget
    /// even when the document is exactly at or slightly above the limit.
    /// </summary>
    [Fact]
    public async Task DocumentBudgetPerDoc_30K_SingleDocumentStaysWithinBudget()
    {
        // Arrange — create a document that is just over 30K tokens
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentText = GenerateLargeDocument(35_000); // slightly over 30K
        SetupDocumentMocks(docService, speOps, textExtractor, "doc-30k", "JustOver30K.pdf", documentText);

        // Position-based selection (no user message)
        // Act
        var result = await sut.InjectDocumentContextAsync(
            "doc-30k", httpContext: null, latestUserMessage: null);

        // Assert
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "single document injection must stay within the 30K token budget");
        result.WasTruncated.Should().BeTrue(
            "a 35K-token document should be truncated to fit within 30K");
    }

    // -------------------------------------------------------------------
    // Test: MultiDocumentAggregation_5Docs
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that when 5 documents are injected, all 5 contribute to the context
    /// and the total stays within the shared 30K document token budget (FR-12).
    /// </summary>
    [Fact]
    public async Task MultiDocumentAggregation_5Docs_AllContributeWithinBudget()
    {
        // Arrange — 5 documents, each small enough to fit individually
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentIds = new List<string>();
        var contents = new Dictionary<string, string>();
        for (var i = 1; i <= 5; i++)
        {
            var docId = $"doc-multi-{i}";
            var fileName = $"Document{i}.pdf";
            documentIds.Add(docId);

            var content = GenerateDocumentWithSections(5, wordsPerSection: 50);
            contents[fileName] = content;

            docService
                .Setup(d => d.GetDocumentAsync(docId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity
                {
                    Id = docId,
                    Name = fileName,
                    FileName = fileName,
                    GraphDriveId = $"drive-{docId}",
                    GraphItemId = $"item-{docId}"
                });

            // Return a new MemoryStream each time (streams are consumed on read)
            var contentBytes = Encoding.UTF8.GetBytes(content);
            speOps
                .Setup(s => s.DownloadFileAsync(
                    $"drive-{docId}", $"item-{docId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(contentBytes));
        }

        // Setup text extractor to return content based on the filename
        textExtractor
            .Setup(t => t.ExtractAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, string, CancellationToken>((stream, fileName, _) =>
            {
                if (contents.TryGetValue(fileName, out var text))
                    return Task.FromResult(TextExtractionResult.Succeeded(text, TextExtractionMethod.Native));
                return Task.FromResult(TextExtractionResult.Failed("Not found", TextExtractionMethod.NotSupported));
            });

        // Act
        var result = await sut.InjectMultiDocumentContextAsync(
            documentIds, httpContext: null, latestUserMessage: null);

        // Assert — all 5 documents should contribute
        result.DocumentGroups.Should().HaveCount(5,
            "all 5 documents should be represented in the result");

        // At least some chunks from each document should be present
        foreach (var group in result.DocumentGroups)
        {
            group.SelectedChunks.Should().NotBeEmpty(
                $"document {group.DocumentId} should contribute at least some chunks");
        }

        // Total budget enforcement
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "multi-document aggregation must stay within the shared 30K token budget");

        // Merged chunks should contain content from multiple documents
        result.MergedChunks.Should().NotBeEmpty("merged cross-document chunks should exist");
    }

    /// <summary>
    /// Verifies that multi-document aggregation with 5 large documents that individually
    /// exceed the budget still enforces the total 30K limit (proportional allocation).
    /// </summary>
    [Fact]
    public async Task MultiDocumentAggregation_5LargeDocs_ProportionalAllocationEnforcesBudget()
    {
        // Arrange — 5 documents, each ~20K tokens (total ~100K far exceeds 30K)
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentIds = new List<string>();
        var contents = new Dictionary<string, string>();
        for (var i = 1; i <= 5; i++)
        {
            var docId = $"doc-large-{i}";
            var fileName = $"LargeDoc{i}.pdf";
            documentIds.Add(docId);

            // Each doc ~20K tokens (will be truncated to ~6K per doc = 30K/5)
            var content = GenerateLargeDocument(20_000);
            contents[fileName] = content;

            docService
                .Setup(d => d.GetDocumentAsync(docId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentEntity
                {
                    Id = docId,
                    Name = fileName,
                    FileName = fileName,
                    GraphDriveId = $"drive-{docId}",
                    GraphItemId = $"item-{docId}"
                });

            var contentBytes = Encoding.UTF8.GetBytes(content);
            speOps
                .Setup(s => s.DownloadFileAsync(
                    $"drive-{docId}", $"item-{docId}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(contentBytes));
        }

        // Setup text extractor to return content based on the filename
        textExtractor
            .Setup(t => t.ExtractAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, string, CancellationToken>((stream, fileName, _) =>
            {
                if (contents.TryGetValue(fileName, out var text))
                    return Task.FromResult(TextExtractionResult.Succeeded(text, TextExtractionMethod.Native));
                return Task.FromResult(TextExtractionResult.Failed("Not found", TextExtractionMethod.NotSupported));
            });

        // Act
        var result = await sut.InjectMultiDocumentContextAsync(
            documentIds, httpContext: null, latestUserMessage: null);

        // Assert
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "5 large documents must still fit within the shared 30K budget");

        // Verify each document's usage respects its allocation
        foreach (var group in result.DocumentGroups)
        {
            group.TokensUsed.Should().BeLessOrEqualTo(group.TokensAllocated,
                $"document {group.DocumentId} should not exceed its allocated budget");
        }

        // At least one document should have selected chunks
        result.DocumentGroups.Should().Contain(g => g.SelectedChunks.Count > 0,
            "at least some documents should contribute chunks");
        result.MergedChunks.Should().NotBeEmpty(
            "merged chunks should exist from the large documents");
    }

    // -------------------------------------------------------------------
    // Test: BudgetExceeded_GracefulDegradation
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that when the budget is exceeded, the service returns a valid truncated
    /// result with a truncation notice — never null or an exception.
    /// </summary>
    [Fact]
    public async Task BudgetExceeded_GracefulDegradation_ReturnsTruncationNotice()
    {
        // Arrange — document that massively exceeds budget
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var massiveText = GenerateLargeDocument(200_000);
        SetupDocumentMocks(docService, speOps, textExtractor, "doc-massive", "Enormous.pdf", massiveText);

        // Act — should NOT throw, should return a valid truncated result
        var result = await sut.InjectDocumentContextAsync(
            "doc-massive", httpContext: null, latestUserMessage: null);

        // Assert — graceful degradation
        result.Should().NotBeNull("service should never return null even for oversized documents");
        result.SelectedChunks.Should().NotBeEmpty("some chunks should still be selected within budget");
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget,
            "budget must be enforced even with massive documents");
        result.WasTruncated.Should().BeTrue("massive document must report truncation");
        result.TruncationReason.Should().NotBeNullOrWhiteSpace(
            "truncation reason must explain the budget limitation");
        result.TotalChunks.Should().BeGreaterThan(result.SelectedChunks.Count,
            "total chunks should exceed selected chunks when truncated");
    }

    /// <summary>
    /// Verifies graceful degradation for multi-document: when one document fails to load,
    /// the remaining documents still produce a valid result.
    /// </summary>
    [Fact]
    public async Task BudgetExceeded_MultiDoc_PartialFailure_OtherDocsStillContribute()
    {
        // Arrange — 3 documents: doc-1 succeeds, doc-2 not found, doc-3 succeeds
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var contents = new Dictionary<string, string>();

        var content1 = GenerateDocumentWithSections(5, wordsPerSection: 50);
        contents["Good1.pdf"] = content1;
        docService
            .Setup(d => d.GetDocumentAsync("doc-ok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = "doc-ok-1", Name = "Good1.pdf", FileName = "Good1.pdf",
                GraphDriveId = "drive-doc-ok-1", GraphItemId = "item-doc-ok-1"
            });
        var bytes1 = Encoding.UTF8.GetBytes(content1);
        speOps
            .Setup(s => s.DownloadFileAsync("drive-doc-ok-1", "item-doc-ok-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes1));

        // doc-2: not found in Dataverse
        docService
            .Setup(d => d.GetDocumentAsync("doc-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);

        var content3 = GenerateDocumentWithSections(5, wordsPerSection: 50);
        contents["Good3.pdf"] = content3;
        docService
            .Setup(d => d.GetDocumentAsync("doc-ok-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = "doc-ok-3", Name = "Good3.pdf", FileName = "Good3.pdf",
                GraphDriveId = "drive-doc-ok-3", GraphItemId = "item-doc-ok-3"
            });
        var bytes3 = Encoding.UTF8.GetBytes(content3);
        speOps
            .Setup(s => s.DownloadFileAsync("drive-doc-ok-3", "item-doc-ok-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes3));

        // Text extractor keyed by filename
        textExtractor
            .Setup(t => t.ExtractAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, string, CancellationToken>((stream, fileName, _) =>
            {
                if (contents.TryGetValue(fileName, out var text))
                    return Task.FromResult(TextExtractionResult.Succeeded(text, TextExtractionMethod.Native));
                return Task.FromResult(TextExtractionResult.Failed("Not found", TextExtractionMethod.NotSupported));
            });

        // Act
        var result = await sut.InjectMultiDocumentContextAsync(
            new List<string> { "doc-ok-1", "doc-missing", "doc-ok-3" },
            httpContext: null,
            latestUserMessage: null);

        // Assert — result should be valid with partial content
        result.Should().NotBeNull();
        result.DocumentGroups.Should().HaveCount(3,
            "all 3 documents should have groups (even the failed one)");
        result.MergedChunks.Should().NotBeEmpty(
            "successful documents should contribute chunks even when one fails");
        result.TotalTokensUsed.Should().BeLessOrEqualTo(MaxDocumentBudget);

        // The failed document should have an empty chunk group
        var failedGroup = result.DocumentGroups.FirstOrDefault(g => g.DocumentId == "doc-missing");
        failedGroup.Should().NotBeNull();
        failedGroup!.SelectedChunks.Should().BeEmpty("missing document should have no chunks");
    }

    // -------------------------------------------------------------------
    // Test: Chunking accuracy — content inspection
    // -------------------------------------------------------------------

    /// <summary>
    /// Verifies that the chunking algorithm produces chunks whose content matches
    /// the original document text (no data corruption during chunking).
    /// </summary>
    [Fact]
    public async Task ChunkingAccuracy_SmallDocument_AllContentPreserved()
    {
        // Arrange — small document that fits within budget
        var (sut, docService, speOps, textExtractor, openAiClient) = CreateServiceWithMocks();

        var documentText = "Section Alpha: Important contract terms.\n\n" +
                           "Section Beta: Liability and indemnification.\n\n" +
                           "Section Gamma: Termination clauses.";

        SetupDocumentMocks(docService, speOps, textExtractor, "doc-small", "Small.pdf", documentText);

        // Act
        var result = await sut.InjectDocumentContextAsync(
            "doc-small", httpContext: null, latestUserMessage: null);

        // Assert — all content should be present in chunks
        result.WasTruncated.Should().BeFalse("small document should fit within budget");

        var allContent = string.Join(" ", result.SelectedChunks.Select(c => c.Content));
        allContent.Should().Contain("Section Alpha");
        allContent.Should().Contain("Section Beta");
        allContent.Should().Contain("Section Gamma");
        allContent.Should().Contain("contract terms");
        allContent.Should().Contain("indemnification");
        allContent.Should().Contain("Termination clauses");
    }

    /// <summary>
    /// Verifies that the 128K total context budget constant is correctly set
    /// and that the 30K document budget is a valid partition of it.
    /// </summary>
    [Fact]
    public void TotalContextBudget_DocumentBudgetIsValidPartition()
    {
        // The 128K total context budget is enforced at the agent level;
        // DocumentContextService enforces the 30K document partition.
        const int totalContextBudget = 128_000;

        DocumentContextService.MaxTokenBudget.Should().Be(30_000,
            "document budget should be exactly 30K tokens");

        DocumentContextService.MaxTokenBudget.Should().BeLessThan(totalContextBudget,
            "document budget must be a proper subset of the 128K total context window");

        // Budget partitioning: 8K playbook + 30K document + ~40K history + ~50K response = 128K
        var playbook = 8_000;
        var document = DocumentContextService.MaxTokenBudget;
        var historyAndResponse = totalContextBudget - playbook - document;
        historyAndResponse.Should().BeGreaterThan(0,
            "remaining budget after playbook and document should be positive for history and response");
    }
}
