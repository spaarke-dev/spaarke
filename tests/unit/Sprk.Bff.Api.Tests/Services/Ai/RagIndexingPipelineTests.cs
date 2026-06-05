using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using AzureIndexingResult = Azure.Search.Documents.Models.IndexingResult;
// Alias to distinguish our IndexingResult from Azure SDK's Azure.Search.Documents.Models.IndexingResult
using PipelineIndexingResult = Sprk.Bff.Api.Models.Ai.IndexingResult;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="RagIndexingPipeline"/>.
///
/// Acceptance criteria (from AIPL-013):
///   1. Chunks are sent to both the knowledge index and the discovery index.
///   2. Existing chunks are deleted before re-indexing (idempotency per ADR-004).
///   3. <see cref="PipelineIndexingResult"/> contains correct chunk counts.
///   4. Cancellation token is propagated to all async operations.
/// </summary>
public class RagIndexingPipelineTests
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly Mock<ITextChunkingService> _chunkingServiceMock;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _discoverySearchClientMock;
    private readonly Mock<SearchClient> _sessionFilesSearchClientMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly IOptions<AiSearchOptions> _aiSearchOptions;
    private readonly Mock<ILogger<RagIndexingPipeline>> _loggerMock;

    private const string TenantId = "tenant-abc";
    private const string DocumentId = "doc-xyz";
    private const string FileName = "contract.pdf";
    private const string SpeFileId = "spe-file-123";
    private const string DiscoveryIndexName = "test-discovery-index";
    private const string SessionFilesIndexName = "test-session-files-index";
    private const string SessionId = "test-session-001";

    private readonly ReadOnlyMemory<float> _testEmbedding;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public RagIndexingPipelineTests()
    {
        _chunkingServiceMock = new Mock<ITextChunkingService>();
        _ragServiceMock = new Mock<IRagService>();
        _searchIndexClientMock = new Mock<SearchIndexClient>();
        _discoverySearchClientMock = new Mock<SearchClient>();
        _sessionFilesSearchClientMock = new Mock<SearchClient>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _loggerMock = new Mock<ILogger<RagIndexingPipeline>>();

        _aiSearchOptions = Options.Create(new AiSearchOptions
        {
            DiscoveryIndexName = DiscoveryIndexName,
            KnowledgeIndexName = "test-knowledge-index",
            SessionFilesIndexName = SessionFilesIndexName
        });

        // 3072-dimension test embedding
        var embedding = new float[3072];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        // Wire SearchIndexClient.GetSearchClient to return the discovery mock
        _searchIndexClientMock
            .Setup(x => x.GetSearchClient(DiscoveryIndexName))
            .Returns(_discoverySearchClientMock.Object);

        // R5 (task 003) — wire SearchIndexClient.GetSearchClient(SessionFilesIndexName)
        // to return the session-files mock so IndexSessionFileAsync tests can capture
        // the upload payload.
        _searchIndexClientMock
            .Setup(x => x.GetSearchClient(SessionFilesIndexName))
            .Returns(_sessionFilesSearchClientMock.Object);
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    private RagIndexingPipeline CreatePipeline() =>
        new RagIndexingPipeline(
            _chunkingServiceMock.Object,
            _ragServiceMock.Object,
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _aiSearchOptions,
            _loggerMock.Object);

    private static ParsedDocument CreateParsedDocument(string text = "Hello world. This is a test document for chunking.") =>
        new ParsedDocument
        {
            Text = text,
            Pages = 1,
            ParserUsed = DocumentParser.DocumentIntelligence,
            ExtractedAt = DateTimeOffset.UtcNow
        };

    private static IReadOnlyList<TextChunk> CreateTextChunks(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => new TextChunk
            {
                Content = $"Chunk content {i}",
                Index = i,
                StartPosition = i * 100,
                EndPosition = (i + 1) * 100
            })
            .ToList();
    }

    // -------------------------------------------------------------------------
    // Helper: Setup default mocks for a full successful run
    // -------------------------------------------------------------------------

    private void SetupSuccessfulRun(int knowledgeChunkCount = 2, int discoveryChunkCount = 1)
    {
        var knowledgeChunks = CreateTextChunks(knowledgeChunkCount);
        var discoveryChunks = CreateTextChunks(discoveryChunkCount);

        // Chunking service returns different chunk sets for different options
        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 2048),  // Knowledge options
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(knowledgeChunks);

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 4096),  // Discovery options
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveryChunks);

        // Embedding generation returns test embedding for any text
        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        // Knowledge index delete returns 0 (no old chunks)
        _ragServiceMock
            .Setup(x => x.DeleteBySourceDocumentAsync(
                DocumentId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Discovery index search for delete returns empty results
        SetupDiscoverySearchEmpty();

        // Knowledge index upload succeeds for all documents
        SetupKnowledgeIndexUploadSuccess(knowledgeChunkCount);

        // Discovery index upload succeeds for all documents
        SetupDiscoveryIndexUploadSuccess(discoveryChunkCount);
    }

    private void SetupDiscoverySearchEmpty()
    {
        var emptyResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _discoverySearchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(emptyResults, null!));
    }

    private void SetupKnowledgeIndexUploadSuccess(int chunkCount)
    {
        var successResults = Enumerable.Range(0, chunkCount)
            .Select(i => SearchModelFactory.IndexingResult($"{DocumentId}_k_{i}", null, true, 201))
            .ToList();

        var indexDocumentsResult = SearchModelFactory.IndexDocumentsResult(successResults);

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Enumerable.Range(0, chunkCount)
                    .Select(i => Sprk.Bff.Api.Services.Ai.IndexResult.Success($"{DocumentId}_k_{i}"))
                    .ToList());
    }

    private void SetupDiscoveryIndexUploadSuccess(int chunkCount)
    {
        var successResults = Enumerable.Range(0, chunkCount)
            .Select(i => SearchModelFactory.IndexingResult($"{DocumentId}_d_{i}", null, true, 201))
            .ToList();

        var indexDocumentsResult = SearchModelFactory.IndexDocumentsResult(successResults);

        _discoverySearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexDocumentsResult, null!));
    }

    // -------------------------------------------------------------------------
    // Test: Chunks are sent to both knowledge and discovery indexes (AC-1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_ChunksSentToBothIndexes()
    {
        // Arrange
        SetupSuccessfulRun(knowledgeChunkCount: 3, discoveryChunkCount: 2);
        var pipeline = CreatePipeline();
        var document = CreateParsedDocument();

        // Act
        var result = await pipeline.IndexDocumentAsync(document, DocumentId, TenantId, FileName, SpeFileId);

        // Assert — knowledge index received chunks
        _ragServiceMock.Verify(
            x => x.IndexDocumentsBatchAsync(
                It.Is<IEnumerable<KnowledgeDocument>>(docs => docs.Count() == 3),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Knowledge index should receive 3 chunks");

        // Assert — discovery index received chunks
        _discoverySearchClientMock.Verify(
            x => x.MergeOrUploadDocumentsAsync(
                It.Is<IEnumerable<KnowledgeDocument>>(docs => docs.Count() == 2),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Discovery index should receive 2 chunks");

        result.KnowledgeChunksIndexed.Should().Be(3);
        result.DiscoveryChunksIndexed.Should().Be(2);
    }

    // -------------------------------------------------------------------------
    // Test: Idempotency — old chunks deleted before re-indexing (AC-2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_DeletesExistingChunksBeforeIndexing_Idempotent()
    {
        // Arrange
        // Set up discovery index to return 2 existing chunks to delete
        var existingChunkId1 = $"{DocumentId}_d_0";
        var existingChunkId2 = $"{DocumentId}_d_1";

        var existingChunks = new List<SearchResult<KnowledgeDocument>>
        {
            SearchModelFactory.SearchResult(
                new KnowledgeDocument { Id = existingChunkId1, DocumentId = DocumentId, TenantId = TenantId },
                score: 1.0, highlights: null),
            SearchModelFactory.SearchResult(
                new KnowledgeDocument { Id = existingChunkId2, DocumentId = DocumentId, TenantId = TenantId },
                score: 1.0, highlights: null)
        };

        var existingResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: existingChunks,
            totalCount: 2,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _discoverySearchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.Is<SearchOptions>(o => o.Filter != null && o.Filter.Contains(DocumentId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(existingResults, null!));

        // Discovery delete returns success
        var deleteResult = SearchModelFactory.IndexDocumentsResult(new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult(existingChunkId1, null, true, 200),
            SearchModelFactory.IndexingResult(existingChunkId2, null, true, 200)
        });

        _discoverySearchClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(deleteResult, null!));

        // Knowledge delete
        _ragServiceMock
            .Setup(x => x.DeleteBySourceDocumentAsync(DocumentId, TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        // Set up chunking and embeddings
        var knowledgeChunks = CreateTextChunks(2);
        var discoveryChunks = CreateTextChunks(1);

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 2048),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(knowledgeChunks);

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 4096),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveryChunks);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        SetupKnowledgeIndexUploadSuccess(2);
        SetupDiscoveryIndexUploadSuccess(1);

        var pipeline = CreatePipeline();
        var document = CreateParsedDocument();

        // Act
        await pipeline.IndexDocumentAsync(document, DocumentId, TenantId, FileName, SpeFileId);

        // Assert — knowledge delete was called first (before indexing)
        _ragServiceMock.Verify(
            x => x.DeleteBySourceDocumentAsync(DocumentId, TenantId, It.IsAny<CancellationToken>()),
            Times.Once,
            "Existing knowledge chunks should be deleted before re-indexing");

        // Assert — discovery search was performed to find old chunks
        _discoverySearchClientMock.Verify(
            x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Discovery index should be searched for existing chunks to delete");

        // Assert — discovery delete was called
        _discoverySearchClientMock.Verify(
            x => x.DeleteDocumentsAsync(
                "id",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Existing discovery chunks should be deleted before re-indexing");
    }

    // -------------------------------------------------------------------------
    // Test: IndexingResult contains correct chunk counts (AC-3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_ReturnsCorrectChunkCounts()
    {
        // Arrange
        const int knowledgeChunks = 4;
        const int discoveryChunks = 2;

        SetupSuccessfulRun(knowledgeChunkCount: knowledgeChunks, discoveryChunkCount: discoveryChunks);
        var pipeline = CreatePipeline();
        var document = CreateParsedDocument(
            text: string.Join(" ", Enumerable.Repeat("This is a long paragraph to create enough text for chunking.", 50)));

        // Act
        var result = await pipeline.IndexDocumentAsync(document, DocumentId, TenantId, FileName, SpeFileId);

        // Assert
        result.Should().NotBeNull();
        result.DocumentId.Should().Be(DocumentId);
        result.KnowledgeChunksIndexed.Should().Be(knowledgeChunks,
            "because {0} knowledge chunks were uploaded successfully", knowledgeChunks);
        result.DiscoveryChunksIndexed.Should().Be(discoveryChunks,
            "because {0} discovery chunks were uploaded successfully", discoveryChunks);
        result.DurationMs.Should().BeGreaterOrEqualTo(0,
            "DurationMs should be a non-negative wall-clock measurement");
    }

    // -------------------------------------------------------------------------
    // Test: Cancellation token propagated (AC-4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_CancellationTokenPropagatedToChunkingService()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel

        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.IsAny<ChunkingOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, ChunkingOptions?, CancellationToken>((text, opts, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<TextChunk>>(new List<TextChunk>());
            });

        var pipeline = CreatePipeline();
        var document = CreateParsedDocument();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pipeline.IndexDocumentAsync(document, DocumentId, TenantId, FileName, SpeFileId, cts.Token));
    }

    // -------------------------------------------------------------------------
    // Test: Both indexes use tenantId (ADR-014 compliance)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_AllDocumentsCarryTenantId()
    {
        // Arrange
        SetupSuccessfulRun(knowledgeChunkCount: 2, discoveryChunkCount: 1);
        var pipeline = CreatePipeline();
        var document = CreateParsedDocument();

        IEnumerable<KnowledgeDocument>? capturedKnowledgeDocs = null;
        IEnumerable<KnowledgeDocument>? capturedDiscoveryDocs = null;

        _ragServiceMock
            .Setup(x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeDocument>, CancellationToken>((docs, _) =>
                capturedKnowledgeDocs = docs.ToList())
            .ReturnsAsync(
                Enumerable.Range(0, 2)
                    .Select(i => Sprk.Bff.Api.Services.Ai.IndexResult.Success($"{DocumentId}_k_{i}"))
                    .ToList());

        _discoverySearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeDocument>, IndexDocumentsOptions?, CancellationToken>((docs, _, _) =>
                capturedDiscoveryDocs = docs.ToList())
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(new List<AzureIndexingResult>
                {
                    SearchModelFactory.IndexingResult($"{DocumentId}_d_0", null, true, 201)
                }),
                null!));

        // Act
        await pipeline.IndexDocumentAsync(document, DocumentId, TenantId, FileName, SpeFileId);

        // Assert — all knowledge documents have tenantId
        capturedKnowledgeDocs.Should().NotBeNull();
        capturedKnowledgeDocs!.Should().AllSatisfy(doc =>
            doc.TenantId.Should().Be(TenantId, "ADR-014: all indexed documents must carry tenantId"));

        // Assert — all discovery documents have tenantId
        capturedDiscoveryDocs.Should().NotBeNull();
        capturedDiscoveryDocs!.Should().AllSatisfy(doc =>
            doc.TenantId.Should().Be(TenantId, "ADR-014: all indexed documents must carry tenantId"));
    }

    // -------------------------------------------------------------------------
    // Test: Argument validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexDocumentAsync_NullDocument_ThrowsArgumentNullException()
    {
        var pipeline = CreatePipeline();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => pipeline.IndexDocumentAsync(null!, DocumentId, TenantId, FileName, SpeFileId));
    }

    [Fact]
    public async Task IndexDocumentAsync_EmptyDocumentId_ThrowsArgumentException()
    {
        var pipeline = CreatePipeline();
        await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IndexDocumentAsync(CreateParsedDocument(), string.Empty, TenantId, FileName, SpeFileId));
    }

    [Fact]
    public async Task IndexDocumentAsync_EmptyTenantId_ThrowsArgumentException()
    {
        var pipeline = CreatePipeline();
        await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IndexDocumentAsync(CreateParsedDocument(), DocumentId, string.Empty, FileName, SpeFileId));
    }

    // -------------------------------------------------------------------------
    // R5 task 003 — IndexSessionFileAsync tests (session-files write path)
    //
    // Acceptance criteria (from task 003 POML §goal + §acceptance-criteria):
    //   1. Every emitted document carries BOTH tenantId AND sessionId (ADR-014).
    //   2. Write-path isolation: knowledge + discovery indexes NEVER touched.
    //   3. Target index resolved from AiSearchOptions.SessionFilesIndexName.
    //   4. Required-parameter contract: null/empty sessionId throws ArgumentException.
    //   5. Idempotency: delete-then-upload sequence using (documentId, tenantId, sessionId).
    //   6. PipelineIndexingResult reports session-files chunks via KnowledgeChunksIndexed
    //      with DiscoveryChunksIndexed = 0 (this path never writes discovery).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Helper: configure mocks for a successful IndexSessionFileAsync run.
    /// </summary>
    private void SetupSuccessfulSessionFileRun(int chunkCount)
    {
        var chunks = CreateTextChunks(chunkCount);

        // Session-files writes use the knowledge-granularity chunking profile (2048-char)
        // ONLY — single granularity per design.md §4.2.
        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 2048),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(chunks);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        // Session-files search for delete returns empty (no prior chunks).
        var emptyResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _sessionFilesSearchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(emptyResults, null!));

        // Session-files upload succeeds for all documents.
        var successResults = Enumerable.Range(0, chunkCount)
            .Select(i => SearchModelFactory.IndexingResult($"{DocumentId}_s_{i}", null, true, 201))
            .ToList();
        var indexDocumentsResult = SearchModelFactory.IndexDocumentsResult(successResults);

        _sessionFilesSearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexDocumentsResult, null!));
    }

    [Fact]
    public async Task IndexSessionFile_emits_tenantId_and_sessionId_on_every_doc()
    {
        // Arrange
        const int chunkCount = 3;
        SetupSuccessfulSessionFileRun(chunkCount);

        IEnumerable<KnowledgeDocument>? capturedDocs = null;
        _sessionFilesSearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeDocument>, IndexDocumentsOptions?, CancellationToken>(
                (docs, _, _) => capturedDocs = docs.ToList())
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(
                    Enumerable.Range(0, chunkCount)
                        .Select(i => SearchModelFactory.IndexingResult($"{DocumentId}_s_{i}", null, true, 201))
                        .ToList()),
                null!));

        var pipeline = CreatePipeline();

        // Act
        await pipeline.IndexSessionFileAsync(
            CreateParsedDocument(), DocumentId, TenantId, SessionId, FileName, SpeFileId);

        // Assert — ADR-014 invariant: every doc carries tenantId AND sessionId
        capturedDocs.Should().NotBeNull();
        capturedDocs!.Should().HaveCount(chunkCount);
        capturedDocs!.Should().AllSatisfy(doc =>
        {
            doc.TenantId.Should().Be(TenantId,
                "ADR-014: every session-files document MUST carry tenantId");
            doc.SessionId.Should().Be(SessionId,
                "R5 FR-09 + ADR-014: every session-files document MUST carry sessionId");
        });
    }

    [Fact]
    public async Task IndexSessionFile_does_not_call_knowledge_or_discovery_indexes()
    {
        // Arrange
        SetupSuccessfulSessionFileRun(chunkCount: 2);
        var pipeline = CreatePipeline();

        // Act
        await pipeline.IndexSessionFileAsync(
            CreateParsedDocument(), DocumentId, TenantId, SessionId, FileName, SpeFileId);

        // Assert — knowledge-index path NEVER invoked
        _ragServiceMock.Verify(
            x => x.IndexDocumentsBatchAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "session-files path MUST NOT write to the knowledge index");

        _ragServiceMock.Verify(
            x => x.DeleteBySourceDocumentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "session-files path MUST NOT call the knowledge-index delete helper");

        // Assert — discovery-index client NEVER resolved or invoked
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(DiscoveryIndexName),
            Times.Never,
            "session-files path MUST NOT resolve the discovery-index SearchClient");

        _discoverySearchClientMock.Verify(
            x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "session-files path MUST NOT upload to the discovery index");
    }

    [Fact]
    public async Task IndexSessionFile_calls_GetSearchClient_with_SessionFilesIndexName()
    {
        // Arrange
        SetupSuccessfulSessionFileRun(chunkCount: 1);
        var pipeline = CreatePipeline();

        // Act
        await pipeline.IndexSessionFileAsync(
            CreateParsedDocument(), DocumentId, TenantId, SessionId, FileName, SpeFileId);

        // Assert — target index resolved from AiSearchOptions.SessionFilesIndexName
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(SessionFilesIndexName),
            Times.AtLeastOnce,
            "session-files writes MUST target AiSearchOptions.SessionFilesIndexName");
    }

    [Fact]
    public async Task IndexSessionFile_throws_on_missing_sessionId()
    {
        // Arrange
        var pipeline = CreatePipeline();

        // Act + Assert — ADR-014 contract enforced at runtime (compile-time positional too).
        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentNullException for null and
        // ArgumentException for empty; both inherit from ArgumentException.
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => pipeline.IndexSessionFileAsync(
                CreateParsedDocument(), DocumentId, TenantId, sessionId: string.Empty, FileName, SpeFileId));

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => pipeline.IndexSessionFileAsync(
                CreateParsedDocument(), DocumentId, TenantId, sessionId: null!, FileName, SpeFileId));
    }

    [Fact]
    public async Task IndexSessionFile_throws_on_missing_tenantId()
    {
        // Arrange
        var pipeline = CreatePipeline();

        // Act + Assert — ADR-014 contract: tenantId required for session-files writes
        await Assert.ThrowsAsync<ArgumentException>(
            () => pipeline.IndexSessionFileAsync(
                CreateParsedDocument(), DocumentId, tenantId: string.Empty, SessionId, FileName, SpeFileId));
    }

    [Fact]
    public async Task IndexSessionFile_idempotency_deletes_before_upload()
    {
        // Arrange — set up prior chunks to be deleted
        var existingIds = new[] { $"{DocumentId}_s_0", $"{DocumentId}_s_1" };
        var existingChunks = existingIds
            .Select(id => SearchModelFactory.SearchResult(
                new KnowledgeDocument
                {
                    Id = id,
                    DocumentId = DocumentId,
                    TenantId = TenantId,
                    SessionId = SessionId
                },
                score: 1.0,
                highlights: null))
            .ToList();

        var existingResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: existingChunks,
            totalCount: existingChunks.Count,
            facets: null,
            coverage: null,
            rawResponse: null!);

        SearchOptions? capturedSearchOptions = null;
        _sessionFilesSearchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((_, opts, _) => capturedSearchOptions = opts)
            .ReturnsAsync(Response.FromValue(existingResults, null!));

        IEnumerable<string>? capturedDeleteIds = null;
        _sessionFilesSearchClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id",
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, IndexDocumentsOptions?, CancellationToken>(
                (_, ids, _, _) => capturedDeleteIds = ids.ToList())
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(existingIds
                    .Select(id => SearchModelFactory.IndexingResult(id, null, true, 200))
                    .ToList()),
                null!));

        // New chunks to upload after delete
        var newChunks = CreateTextChunks(1);
        _chunkingServiceMock
            .Setup(x => x.ChunkTextAsync(
                It.IsAny<string>(),
                It.Is<ChunkingOptions>(o => o.ChunkSize == 2048),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(newChunks);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        _sessionFilesSearchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.IndexDocumentsResult(new List<AzureIndexingResult>
                {
                    SearchModelFactory.IndexingResult($"{DocumentId}_s_0", null, true, 201)
                }),
                null!));

        var pipeline = CreatePipeline();

        // Act
        await pipeline.IndexSessionFileAsync(
            CreateParsedDocument(), DocumentId, TenantId, SessionId, FileName, SpeFileId);

        // Assert — search filter contains all three predicates (documentId, tenantId, sessionId)
        capturedSearchOptions.Should().NotBeNull();
        capturedSearchOptions!.Filter.Should().NotBeNullOrEmpty();
        capturedSearchOptions!.Filter.Should().Contain($"documentId eq '{DocumentId}'");
        capturedSearchOptions!.Filter.Should().Contain($"tenantId eq '{TenantId}'");
        capturedSearchOptions!.Filter.Should().Contain($"sessionId eq '{SessionId}'",
            "R5 FR-09 + ADR-014: session-files delete MUST scope by sessionId");

        // Assert — delete called with stale IDs (idempotency)
        capturedDeleteIds.Should().NotBeNull();
        capturedDeleteIds!.Should().BeEquivalentTo(existingIds,
            "ADR-004 idempotency: stale chunks must be deleted before re-upload");

        // Assert — upload then happened (delete-before-upload sequence)
        _sessionFilesSearchClientMock.Verify(
            x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexSessionFile_result_reports_chunks_via_KnowledgeChunksIndexed_with_zero_discovery()
    {
        // Arrange
        const int chunkCount = 4;
        SetupSuccessfulSessionFileRun(chunkCount);
        var pipeline = CreatePipeline();

        // Act
        var result = await pipeline.IndexSessionFileAsync(
            CreateParsedDocument(), DocumentId, TenantId, SessionId, FileName, SpeFileId);

        // Assert — result shape (existing PipelineIndexingResult is reused per task POML §step 7)
        result.Should().NotBeNull();
        result.DocumentId.Should().Be(DocumentId);
        result.KnowledgeChunksIndexed.Should().Be(chunkCount,
            "session-files chunks are reported via KnowledgeChunksIndexed (no new field per task scope)");
        result.DiscoveryChunksIndexed.Should().Be(0,
            "session-files path NEVER writes to the discovery index");
        result.DurationMs.Should().BeGreaterOrEqualTo(0);
    }
}
