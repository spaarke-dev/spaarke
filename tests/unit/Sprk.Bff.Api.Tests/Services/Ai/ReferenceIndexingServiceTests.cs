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
using Sprk.Bff.Api.Services.Ai.Indexing;
using Xunit;
using AzureIndexingResult = Azure.Search.Documents.Models.IndexingResult;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for <see cref="ReferenceIndexingService"/> after the Task 025 (W3.5) parameterization refactor.
///
/// Two test groups:
///   1. REGRESSION — existing <c>spaarke-rag-references</c> behavior unchanged:
///      • Convenience wrapper <c>IndexKnowledgeSourceAsync</c> uses the configured RagReferencesIndexName.
///      • Existing chunks deleted before re-indexing (idempotency).
///      • Document IDs follow <c>{knowledgeSourceId}_ref_{chunkIndex}</c> format.
///      • Result records chunk counts correctly.
///   2. EXTENSION — new generic <see cref="ReferenceIndexingService.IndexIntoAsync{TDoc}"/> overload:
///      • Targets caller-supplied index name (not hard-coded RagReferencesIndexName).
///      • Uses caller-supplied schema mapper (mock Observation row in this test).
///      • Honors caller-supplied chunking options.
/// </summary>
public class ReferenceIndexingServiceTests
{
    // -------------------------------------------------------------------------
    // Fields + constants
    // -------------------------------------------------------------------------

    private readonly Mock<ITextChunkingService> _chunkingServiceMock = new();
    private readonly Mock<SearchIndexClient> _searchIndexClientMock = new();
    private readonly Mock<SearchClient> _searchClientMock = new();
    private readonly Mock<IOpenAiClient> _openAiClientMock = new();
    private readonly Mock<IScopeResolverService> _scopeResolverServiceMock = new();
    private readonly Mock<ILogger<ReferenceIndexingService>> _loggerMock = new();
    private readonly IOptions<AiSearchOptions> _aiSearchOptions;

    private const string RagReferencesIndexName = "spaarke-rag-references";
    private const string KnowledgeSourceId = "ks-test-001";
    private const string KnowledgeSourceName = "Test Knowledge";
    private const string Domain = "legal";

    private readonly ReadOnlyMemory<float> _testEmbedding;

    public ReferenceIndexingServiceTests()
    {
        _aiSearchOptions = Options.Create(new AiSearchOptions
        {
            RagReferencesIndexName = RagReferencesIndexName
        });

        // 3072-dim embedding aligned with the real reference profile.
        var floats = new float[3072];
        for (int i = 0; i < floats.Length; i++) floats[i] = (float)(i % 10) / 10f;
        _testEmbedding = new ReadOnlyMemory<float>(floats);

        // Any GetSearchClient(name) returns the shared SearchClient mock.
        _searchIndexClientMock
            .Setup(x => x.GetSearchClient(It.IsAny<string>()))
            .Returns(_searchClientMock.Object);

        // Default: openai returns the test embedding.
        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        // Default: search returns no existing documents (so delete returns 0).
        SetupSearchEmpty();
    }

    private ReferenceIndexingService CreateService() => new(
        _chunkingServiceMock.Object,
        _searchIndexClientMock.Object,
        _openAiClientMock.Object,
        _scopeResolverServiceMock.Object,
        _aiSearchOptions,
        _loggerMock.Object);

    // -------------------------------------------------------------------------
    // Mock helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<TextChunk> CreateChunks(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new TextChunk
            {
                Content = $"chunk-{i}-content",
                Index = i,
                StartPosition = i * 100,
                EndPosition = (i + 1) * 100
            })
            .ToList();

    private void SetupChunking(int count, ChunkingOptions? expectedOptions = null)
    {
        var chunks = CreateChunks(count);
        if (expectedOptions is null)
        {
            _chunkingServiceMock
                .Setup(x => x.ChunkTextAsync(
                    It.IsAny<string>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(chunks);
        }
        else
        {
            _chunkingServiceMock
                .Setup(x => x.ChunkTextAsync(
                    It.IsAny<string>(),
                    It.Is<ChunkingOptions?>(o => o != null && o.ChunkSize == expectedOptions.ChunkSize),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(chunks);
        }
    }

    private void SetupSearchEmpty()
    {
        var emptyResults = SearchModelFactory.SearchResults<SearchDocument>(
            values: new List<SearchResult<SearchDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        _searchClientMock
            .Setup(x => x.SearchAsync<SearchDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(emptyResults, null!));
    }

    private void SetupUploadSuccess<TDoc>(int chunkCount) where TDoc : class
    {
        var successResults = Enumerable.Range(0, chunkCount)
            .Select(i => SearchModelFactory.IndexingResult($"doc-{i}", null, true, 201))
            .ToList();
        var indexResult = SearchModelFactory.IndexDocumentsResult(successResults);

        _searchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<TDoc>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(indexResult, null!));
    }

    // -------------------------------------------------------------------------
    // REGRESSION — existing IndexKnowledgeSourceAsync behavior
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IndexKnowledgeSourceAsync_UsesRagReferencesIndexName_FromOptions()
    {
        // Arrange — 3 chunks expected.
        SetupChunking(3);
        SetupUploadSuccess<KnowledgeDocument>(3);
        var service = CreateService();

        // Act
        var result = await service.IndexKnowledgeSourceAsync(
            KnowledgeSourceId, "some content", KnowledgeSourceName, Domain, new[] { "tag1" });

        // Assert — index name resolved from AiSearchOptions (NOT a hard-coded literal).
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(RagReferencesIndexName),
            Times.AtLeastOnce,
            "Convenience wrapper must continue to target the configured RagReferencesIndexName");

        result.KnowledgeSourceId.Should().Be(KnowledgeSourceId);
        result.ChunksIndexed.Should().Be(3);
        result.ChunksDeleted.Should().Be(0);
    }

    [Fact]
    public async Task IndexKnowledgeSourceAsync_ProducesKnowledgeDocumentsWithExpectedIdFormat()
    {
        // Arrange — capture documents passed to MergeOrUploadDocumentsAsync.
        SetupChunking(2);
        IEnumerable<KnowledgeDocument>? captured = null;
        var successResults = new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult($"{KnowledgeSourceId}_ref_0", null, true, 201),
            SearchModelFactory.IndexingResult($"{KnowledgeSourceId}_ref_1", null, true, 201),
        };
        _searchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeDocument>, IndexDocumentsOptions?, CancellationToken>(
                (docs, _, _) => captured = docs.ToList())
            .ReturnsAsync(Response.FromValue(SearchModelFactory.IndexDocumentsResult(successResults), null!));

        var service = CreateService();

        // Act
        await service.IndexKnowledgeSourceAsync(
            KnowledgeSourceId, "some content", KnowledgeSourceName, Domain, Array.Empty<string>());

        // Assert — document IDs preserve the original {sourceId}_ref_{chunkIndex} format.
        captured.Should().NotBeNull();
        var list = captured!.ToList();
        list.Should().HaveCount(2);
        list[0].Id.Should().Be($"{KnowledgeSourceId}_ref_0");
        list[1].Id.Should().Be($"{KnowledgeSourceId}_ref_1");
        list[0].KnowledgeSourceId.Should().Be(KnowledgeSourceId);
        list[0].KnowledgeSourceName.Should().Be(KnowledgeSourceName);
        list[0].DocumentType.Should().Be(Domain);
        list[0].TenantId.Should().Be("system");
        list[0].ChunkCount.Should().Be(2);
        list[0].ContentVector.Length.Should().Be(3072);
    }

    [Fact]
    public async Task IndexKnowledgeSourceAsync_EmitsZeroChunks_WhenChunkerReturnsEmpty()
    {
        // Arrange — chunker returns empty list (degenerate path).
        SetupChunking(0);
        var service = CreateService();

        // Act
        var result = await service.IndexKnowledgeSourceAsync(
            KnowledgeSourceId, "x", KnowledgeSourceName, Domain, Array.Empty<string>());

        // Assert
        result.ChunksIndexed.Should().Be(0);
        _searchClientMock.Verify(
            x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "No upload should occur when there are no chunks");
    }

    [Fact]
    public async Task IndexKnowledgeSourceAsync_DeletesExistingChunksBeforeIndexing_Idempotent()
    {
        // Arrange — existing search returns 2 docs, so deleteSearch + deleteDocuments are both called.
        SetupChunking(2);
        SetupUploadSuccess<KnowledgeDocument>(2);

        var existing = new List<SearchResult<SearchDocument>>
        {
            SearchModelFactory.SearchResult<SearchDocument>(
                new SearchDocument(new Dictionary<string, object> { ["id"] = $"{KnowledgeSourceId}_ref_0" }),
                score: 1.0, highlights: null),
            SearchModelFactory.SearchResult<SearchDocument>(
                new SearchDocument(new Dictionary<string, object> { ["id"] = $"{KnowledgeSourceId}_ref_1" }),
                score: 1.0, highlights: null),
        };
        var results = SearchModelFactory.SearchResults<SearchDocument>(
            values: existing, totalCount: 2, facets: null, coverage: null, rawResponse: null!);

        _searchClientMock
            .Setup(x => x.SearchAsync<SearchDocument>(
                It.IsAny<string>(),
                It.Is<SearchOptions>(o => o.Filter != null && o.Filter.Contains(KnowledgeSourceId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(results, null!));

        var deleteResult = SearchModelFactory.IndexDocumentsResult(new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult($"{KnowledgeSourceId}_ref_0", null, true, 200),
            SearchModelFactory.IndexingResult($"{KnowledgeSourceId}_ref_1", null, true, 200),
        });
        _searchClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id", It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(deleteResult, null!));

        var service = CreateService();

        // Act
        var result = await service.IndexKnowledgeSourceAsync(
            KnowledgeSourceId, "content", KnowledgeSourceName, Domain, Array.Empty<string>());

        // Assert — delete-search happened against the references index and yielded 2 deletions.
        result.ChunksDeleted.Should().Be(2);
        _searchClientMock.Verify(
            x => x.DeleteDocumentsAsync("id", It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "Existing chunks must be deleted before re-indexing for idempotency");
    }

    [Fact]
    public async Task DeleteKnowledgeSourceAsync_TargetsRagReferencesIndex()
    {
        // Arrange — empty search means delete returns 0 without invoking DeleteDocumentsAsync.
        var service = CreateService();

        // Act
        var deleted = await service.DeleteKnowledgeSourceAsync(KnowledgeSourceId);

        // Assert — wrapper resolved the index via options.
        deleted.Should().Be(0);
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(RagReferencesIndexName),
            Times.AtLeastOnce);
    }

    // -------------------------------------------------------------------------
    // EXTENSION — new IndexIntoAsync<TDoc> overload
    // -------------------------------------------------------------------------

    /// <summary>
    /// Mock document type representing a per-field Observation row written to <c>spaarke-insights-index</c>
    /// (the schema D-P11 will adopt). Plain POCO with whatever fields the schema needs.
    /// </summary>
    public sealed class ObservationDocument
    {
        public string Id { get; set; } = string.Empty;
        public string ArtifactType { get; set; } = "Observation";
        public string PrecedentId { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int ChunkIndex { get; set; }
        public ReadOnlyMemory<float> ContentVector { get; set; }
    }

    private sealed class ObservationSchemaMapper : ISchemaMapper<ObservationDocument>
    {
        public string BuildSourceFilter(string sourceId) =>
            $"precedentId eq '{sourceId}' and artifactType eq 'Observation'";

        public IReadOnlyList<ObservationDocument> BuildDocuments(
            IReadOnlyList<TextChunk> chunks,
            IReadOnlyList<ReadOnlyMemory<float>> embeddings,
            string sourceId,
            SchemaMappingContext context)
        {
            var fieldName = context.Extras?["fieldName"] as string ?? "unknown";
            var docs = new List<ObservationDocument>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                docs.Add(new ObservationDocument
                {
                    Id = $"{sourceId}_obs_{fieldName}_{chunks[i].Index}",
                    PrecedentId = sourceId,
                    FieldName = fieldName,
                    Content = chunks[i].Content,
                    ChunkIndex = chunks[i].Index,
                    ContentVector = i < embeddings.Count ? embeddings[i] : ReadOnlyMemory<float>.Empty
                });
            }
            return docs;
        }
    }

    [Fact]
    public async Task IndexIntoAsync_TargetsArbitraryIndexAndUsesSuppliedMapper()
    {
        // Arrange — distinct index name + custom mapper for an Observation schema.
        const string targetIndex = "spaarke-insights-index";
        const string precedentId = "prec-abc-123";
        var chunkOpts = new ChunkingOptions { ChunkSize = 4096, Overlap = 200 };

        SetupChunking(2, chunkOpts);

        IEnumerable<ObservationDocument>? captured = null;
        var successResults = new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult($"{precedentId}_obs_settlement_amount_0", null, true, 201),
            SearchModelFactory.IndexingResult($"{precedentId}_obs_settlement_amount_1", null, true, 201),
        };
        _searchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<ObservationDocument>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ObservationDocument>, IndexDocumentsOptions?, CancellationToken>(
                (docs, _, _) => captured = docs.ToList())
            .ReturnsAsync(Response.FromValue(SearchModelFactory.IndexDocumentsResult(successResults), null!));

        var service = CreateService();
        var mapper = new ObservationSchemaMapper();
        var context = new SchemaMappingContext
        {
            Name = "Settlement Amount Observation",
            Domain = "Observation",
            Extras = new Dictionary<string, object?> { ["fieldName"] = "settlement_amount" }
        };

        // Act
        var result = await service.IndexIntoAsync(
            indexName: targetIndex,
            sourceId: precedentId,
            content: "observation content",
            schemaMapper: mapper,
            context: context,
            chunkingOptions: chunkOpts);

        // Assert — index name routed correctly.
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(targetIndex),
            Times.AtLeastOnce,
            "IndexIntoAsync must route to the caller-supplied index name");

        // Assert — never touched the rag-references index.
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(RagReferencesIndexName),
            Times.Never,
            "IndexIntoAsync must NOT route to the references index when a different name is supplied");

        // Assert — custom mapper produced custom docs with custom ID format.
        captured.Should().NotBeNull();
        var docs = captured!.ToList();
        docs.Should().HaveCount(2);
        docs[0].Id.Should().Be($"{precedentId}_obs_settlement_amount_0");
        docs[0].PrecedentId.Should().Be(precedentId);
        docs[0].FieldName.Should().Be("settlement_amount");
        docs[0].ArtifactType.Should().Be("Observation");
        docs[0].ContentVector.Length.Should().Be(3072);

        result.KnowledgeSourceId.Should().Be(precedentId);
        result.ChunksIndexed.Should().Be(2);
    }

    [Fact]
    public async Task IndexIntoAsync_UsesMapperSourceFilter_ForIdempotentDelete()
    {
        // Arrange — pre-existing docs in the target index, mapper supplies a custom filter expression.
        const string targetIndex = "spaarke-insights-index";
        const string precedentId = "prec-xyz-789";

        SetupChunking(1);
        SetupUploadSuccess<ObservationDocument>(1);

        string? capturedFilter = null;
        var existing = new List<SearchResult<SearchDocument>>
        {
            SearchModelFactory.SearchResult<SearchDocument>(
                new SearchDocument(new Dictionary<string, object> { ["id"] = "stale-doc-id" }),
                score: 1.0, highlights: null),
        };
        var results = SearchModelFactory.SearchResults<SearchDocument>(
            values: existing, totalCount: 1, facets: null, coverage: null, rawResponse: null!);

        _searchClientMock
            .Setup(x => x.SearchAsync<SearchDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>(
                (_, opts, _) => capturedFilter = opts.Filter)
            .ReturnsAsync(Response.FromValue(results, null!));

        var deleteResult = SearchModelFactory.IndexDocumentsResult(new List<AzureIndexingResult>
        {
            SearchModelFactory.IndexingResult("stale-doc-id", null, true, 200),
        });
        _searchClientMock
            .Setup(x => x.DeleteDocumentsAsync(
                "id", It.IsAny<IEnumerable<string>>(),
                It.IsAny<IndexDocumentsOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(deleteResult, null!));

        var service = CreateService();

        // Act
        var result = await service.IndexIntoAsync(
            indexName: targetIndex,
            sourceId: precedentId,
            content: "x",
            schemaMapper: new ObservationSchemaMapper(),
            context: new SchemaMappingContext
            {
                Extras = new Dictionary<string, object?> { ["fieldName"] = "f1" }
            });

        // Assert — mapper's filter expression (not the KnowledgeDocument's) was used.
        capturedFilter.Should().NotBeNull();
        capturedFilter.Should().Contain("precedentId eq");
        capturedFilter.Should().Contain("artifactType eq 'Observation'");
        capturedFilter.Should().NotContain("knowledgeSourceId");
        result.ChunksDeleted.Should().Be(1);
    }

    [Fact]
    public async Task DeleteFromAsync_DeletesFromArbitraryIndex_UsingMapperFilter()
    {
        // Arrange — empty search → 0 deletions, no DeleteDocumentsAsync call.
        const string targetIndex = "spaarke-insights-index";
        var service = CreateService();

        string? capturedFilter = null;
        _searchClientMock
            .Setup(x => x.SearchAsync<SearchDocument>(
                It.IsAny<string>(), It.IsAny<SearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>(
                (_, opts, _) => capturedFilter = opts.Filter)
            .ReturnsAsync(Response.FromValue(
                SearchModelFactory.SearchResults<SearchDocument>(
                    values: new List<SearchResult<SearchDocument>>(),
                    totalCount: 0, facets: null, coverage: null, rawResponse: null!),
                null!));

        // Act
        var deleted = await service.DeleteFromAsync(
            targetIndex, "src-1", new ObservationSchemaMapper());

        // Assert
        deleted.Should().Be(0);
        _searchIndexClientMock.Verify(x => x.GetSearchClient(targetIndex), Times.AtLeastOnce);
        capturedFilter.Should().Contain("precedentId eq 'src-1'");
    }
}
