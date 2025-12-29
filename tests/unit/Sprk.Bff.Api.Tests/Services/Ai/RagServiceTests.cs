using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for RagService - Hybrid RAG search implementation.
/// Tests hybrid search (keyword + vector), semantic ranking, and document indexing.
/// </summary>
public class RagServiceTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock;
    private readonly Mock<ILogger<RagService>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;

    // Test embedding (1536 dimensions like text-embedding-3-small)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public RagServiceTests()
    {
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _embeddingCacheMock = new Mock<IEmbeddingCache>();
        _loggerMock = new Mock<ILogger<RagService>>();
        _options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Shared,
            MaxKnowledgeResults = 5,
            MinRelevanceScore = 0.7f
        });

        // Create a test embedding vector (1536 dimensions)
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);
    }

    private RagService CreateService()
    {
        return new RagService(
            _deploymentServiceMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _options,
            _loggerMock.Object);
    }

    #region SearchAsync Tests

    [Fact]
    public async Task SearchAsync_NullQuery_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync(null!, options));
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync(string.Empty, options));
    }

    [Fact]
    public async Task SearchAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync("test query", null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = string.Empty };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync("test query", options));
    }

    [Fact]
    public async Task SearchAsync_ValidQuery_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test search query";
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Embedding should be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(query, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorSearchDisabled_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test search query";
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            UseVectorSearch = false
        };

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Embedding should NOT be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_UsesCorrectTenant_ForSearchClient()
    {
        // Arrange
        var service = CreateService();
        var tenantId = "tenant-123";
        var options = new RagSearchOptions { TenantId = tenantId };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should get SearchClient for correct tenant
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(tenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithDeploymentId_UsesDeploymentIdRouting()
    {
        // Arrange
        var service = CreateService();
        var deploymentId = Guid.NewGuid();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            DeploymentId = deploymentId
        };

        SetupMockEmbedding();
        SetupMockSearchClientByDeployment();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should get SearchClient by deployment ID
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientByDeploymentAsync(deploymentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ReturnsSearchDurationMetrics()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.Should().NotBeNull();
        result.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
        result.EmbeddingDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SearchAsync_ReturnsOriginalQuery()
    {
        // Arrange
        var service = CreateService();
        var query = "my test query";
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(query, options);

        // Assert
        result.Query.Should().Be(query);
    }

    #endregion

    #region IndexDocumentAsync Tests

    [Fact]
    public async Task IndexDocumentAsync_NullDocument_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentAsync(null!));
    }

    [Fact]
    public async Task IndexDocumentAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument { TenantId = string.Empty };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.IndexDocumentAsync(document));
    }

    [Fact]
    public async Task IndexDocumentAsync_DocumentWithoutVector_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            Content = "Test document content"
            // ContentVector is empty
        };

        SetupMockEmbedding();
        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentAsync(document);

        // Assert - Embedding should be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync("Test document content", null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentAsync_DocumentWithVector_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            Content = "Test document content",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentAsync(document);

        // Assert - Embedding should NOT be generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexDocumentAsync_SetsTimestamps()
    {
        // Arrange
        var service = CreateService();
        var document = new KnowledgeDocument
        {
            Id = "doc-1",
            TenantId = "tenant-1",
            Content = "Test content",
            ContentVector = _testEmbedding
        };

        SetupMockSearchClientForIndexing();

        // Act
        var beforeIndex = DateTimeOffset.UtcNow;
        var result = await service.IndexDocumentAsync(document);
        var afterIndex = DateTimeOffset.UtcNow;

        // Assert
        result.CreatedAt.Should().BeOnOrAfter(beforeIndex);
        result.CreatedAt.Should().BeOnOrBefore(afterIndex);
        result.UpdatedAt.Should().BeOnOrAfter(beforeIndex);
        result.UpdatedAt.Should().BeOnOrBefore(afterIndex);
    }

    #endregion

    #region IndexDocumentsBatchAsync Tests

    [Fact]
    public async Task IndexDocumentsBatchAsync_NullDocuments_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.IndexDocumentsBatchAsync(null!));
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_EmptyList_ReturnsEmptyResults()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.IndexDocumentsBatchAsync(Array.Empty<KnowledgeDocument>());

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IndexDocumentsBatchAsync_MultipleDocuments_GeneratesBatchEmbeddings()
    {
        // Arrange
        var service = CreateService();
        var documents = new[]
        {
            new KnowledgeDocument { Id = "doc-1", TenantId = "tenant-1", Content = "Content 1" },
            new KnowledgeDocument { Id = "doc-2", TenantId = "tenant-1", Content = "Content 2" }
        };

        SetupMockBatchEmbeddings(2);
        SetupMockSearchClientForIndexing();

        // Act
        await service.IndexDocumentsBatchAsync(documents);

        // Assert - Batch embedding should be called
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingsAsync(It.Is<IEnumerable<string>>(e => e.Count() == 2), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region DeleteDocumentAsync Tests

    [Fact]
    public async Task DeleteDocumentAsync_NullDocumentId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.DeleteDocumentAsync(null!, "tenant-1"));
    }

    [Fact]
    public async Task DeleteDocumentAsync_NullTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.DeleteDocumentAsync("doc-1", null!));
    }

    #endregion

    #region GetEmbeddingAsync Tests

    [Fact]
    public async Task GetEmbeddingAsync_NullText_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.GetEmbeddingAsync(null!));
    }

    [Fact]
    public async Task GetEmbeddingAsync_EmptyText_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetEmbeddingAsync(string.Empty));
    }

    [Fact]
    public async Task GetEmbeddingAsync_ValidText_ReturnsEmbedding()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbedding();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert
        result.Length.Should().Be(1536);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbeddingCacheHit();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert - Should NOT call OpenAI (cache hit)
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        result.Length.Should().Be(1536);
    }

    [Fact]
    public async Task GetEmbeddingAsync_CacheMiss_CallsOpenAiAndCaches()
    {
        // Arrange
        var service = CreateService();
        SetupMockEmbedding();

        // Act
        var result = await service.GetEmbeddingAsync("test text");

        // Assert - Should call OpenAI and cache the result
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync("test text", null, It.IsAny<CancellationToken>()),
            Times.Once);
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync("test text", It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Embedding Cache Tests

    [Fact]
    public async Task SearchAsync_CacheHit_ReturnsEmbeddingCacheHitTrue()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbeddingCacheHit();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.EmbeddingCacheHit.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_ReturnsEmbeddingCacheHitFalse()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync("test query", options);

        // Assert
        result.EmbeddingCacheHit.Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_CacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbeddingCacheHit();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should NOT call OpenAI when cache hits
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_CachesGeneratedEmbedding()
    {
        // Arrange
        var service = CreateService();
        var query = "test query";
        var options = new RagSearchOptions { TenantId = "tenant-1", UseVectorSearch = true };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(query, options);

        // Assert - Should cache the generated embedding
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync(query, It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorSearchDisabled_DoesNotUseCache()
    {
        // Arrange
        var service = CreateService();
        var options = new RagSearchOptions
        {
            TenantId = "tenant-1",
            UseVectorSearch = false,
            UseKeywordSearch = true
        };

        SetupMockSearchClient();

        // Act
        await service.SearchAsync("test query", options);

        // Assert - Should NOT check cache when vector search is disabled
        _embeddingCacheMock.Verify(
            x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region RagSearchOptions Tests

    [Fact]
    public void RagSearchOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new RagSearchOptions { TenantId = "tenant-1" };

        // Assert
        options.TopK.Should().Be(5);
        options.MinScore.Should().Be(0.7f);
        options.UseSemanticRanking.Should().BeTrue();
        options.UseVectorSearch.Should().BeTrue();
        options.UseKeywordSearch.Should().BeTrue();
        options.DeploymentId.Should().BeNull();
        options.KnowledgeSourceId.Should().BeNull();
        options.DocumentType.Should().BeNull();
        options.Tags.Should().BeNull();
    }

    #endregion

    #region RagSearchResponse Tests

    [Fact]
    public void RagSearchResponse_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var response = new RagSearchResponse();

        // Assert
        response.Query.Should().BeEmpty();
        response.Results.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        response.SearchDurationMs.Should().Be(0);
        response.EmbeddingDurationMs.Should().Be(0);
        response.EmbeddingCacheHit.Should().BeFalse();
    }

    #endregion

    #region IndexResult Tests

    [Fact]
    public void IndexResult_Success_HasCorrectProperties()
    {
        // Arrange & Act
        var result = IndexResult.Success("doc-123");

        // Assert
        result.Id.Should().Be("doc-123");
        result.Succeeded.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void IndexResult_Failure_HasCorrectProperties()
    {
        // Arrange & Act
        var result = IndexResult.Failure("doc-123", "Index failed");

        // Assert
        result.Id.Should().Be("doc-123");
        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("Index failed");
    }

    #endregion

    #region Helper Methods

    private void SetupMockEmbedding()
    {
        // Setup cache as miss (returns null)
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockEmbeddingCacheHit()
    {
        // Setup cache as hit (returns cached embedding)
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockBatchEmbeddings(int count)
    {
        var embeddings = new List<ReadOnlyMemory<float>>();
        for (int i = 0; i < count; i++)
        {
            embeddings.Add(_testEmbedding);
        }

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(embeddings);
    }

    private void SetupMockSearchClient()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create empty search results
        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupMockSearchClientByDeployment()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create empty search results
        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientByDeploymentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupMockSearchClientForIndexing()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create successful index result (key, errorMessage, succeeded, status)
        var indexResult = SearchModelFactory.IndexDocumentsResult(
            new List<IndexingResult>
            {
                SearchModelFactory.IndexingResult("doc-1", null, true, 201)
            });

        var responseMock = Response.FromValue(indexResult, null!);

        searchClientMock
            .Setup(x => x.MergeOrUploadDocumentsAsync(
                It.IsAny<IEnumerable<KnowledgeDocument>>(),
                It.IsAny<IndexDocumentsOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    #endregion
}
