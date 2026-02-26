using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Xunit;

// Alias to avoid ambiguity with Azure.Search.Documents.SearchOptions
using AppSearchOptions = Sprk.Bff.Api.Models.Ai.SemanticSearch.SearchOptions;

namespace Sprk.Bff.Api.Tests.Services.Ai.SemanticSearch;

/// <summary>
/// Unit tests for SemanticSearchService.
/// Tests hybrid search modes, embedding fallback, result enrichment, and timing metrics.
/// </summary>
public class SemanticSearchServiceTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock;
    private readonly Mock<IQueryPreprocessor> _queryPreprocessorMock;
    private readonly Mock<IResultPostprocessor> _resultPostprocessorMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<SemanticSearchService>> _loggerMock;

    private const string TestTenantId = "test-tenant-123";
    private const string TestEntityType = "matter";
    private const string TestEntityId = "00000000-0000-0000-0000-000000000001";

    // Test embedding (3072 dimensions like text-embedding-3-large)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    public SemanticSearchServiceTests()
    {
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _embeddingCacheMock = new Mock<IEmbeddingCache>();
        _queryPreprocessorMock = new Mock<IQueryPreprocessor>();
        _resultPostprocessorMock = new Mock<IResultPostprocessor>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<SemanticSearchService>>();

        // Create a test embedding vector (3072 dimensions for text-embedding-3-large)
        var embedding = new float[3072];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        // Setup default preprocessor behavior (pass-through)
        _queryPreprocessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticSearchRequest req, string tenant, CancellationToken ct) =>
                new QueryPreprocessorResult(req, req.Query, WasModified: false));

        // Setup default postprocessor behavior (pass-through)
        _resultPostprocessorMock
            .Setup(x => x.ProcessAsync(It.IsAny<SemanticSearchResponse>(), It.IsAny<SemanticSearchRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticSearchResponse resp, SemanticSearchRequest req, string tenant, CancellationToken ct) =>
                new ResultPostprocessorResult(resp, WasModified: false));
    }

    private SemanticSearchService CreateService()
    {
        return new SemanticSearchService(
            _deploymentServiceMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _queryPreprocessorMock.Object,
            _resultPostprocessorMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object);
    }

    private SemanticSearchRequest CreateValidRequest(string? hybridMode = null)
    {
        return new SemanticSearchRequest
        {
            Query = "test search query",
            Scope = SearchScope.Entity,
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            Options = hybridMode != null
                ? new AppSearchOptions { HybridMode = hybridMode }
                : null
        };
    }

    #region SearchAsync - Input Validation Tests

    [Fact]
    public async Task SearchAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SearchAsync(null!, TestTenantId));
    }

    [Fact]
    public async Task SearchAsync_NullTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act & Assert - ArgumentNullException derives from ArgumentException
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await service.SearchAsync(request, null!));
    }

    [Fact]
    public async Task SearchAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync(request, string.Empty));
    }

    [Fact]
    public async Task SearchAsync_WhitespaceTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.SearchAsync(request, "   "));
    }

    #endregion

    #region SearchAsync - RRF Hybrid Mode Tests

    [Fact]
    public async Task SearchAsync_RrfMode_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Embedding should be generated for RRF mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(request.Query!, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_RrfModeDefault_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        // No hybridMode specified - defaults to RRF
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.Entity,
            EntityType = TestEntityType,
            EntityId = TestEntityId
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Embedding should be generated (default is RRF)
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_RrfMode_ExecutedModeIsRrf()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.ExecutedMode.Should().Be(HybridSearchMode.Rrf);
    }

    #endregion

    #region SearchAsync - VectorOnly Mode Tests

    [Fact]
    public async Task SearchAsync_VectorOnlyMode_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.VectorOnly);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Embedding should be generated for vector-only mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(request.Query!, It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorOnlyMode_ExecutedModeIsVectorOnly()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.VectorOnly);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.ExecutedMode.Should().Be(HybridSearchMode.VectorOnly);
    }

    #endregion

    #region SearchAsync - KeywordOnly Mode Tests

    [Fact]
    public async Task SearchAsync_KeywordOnlyMode_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Embedding should NOT be generated for keyword-only mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_KeywordOnlyMode_ExecutedModeIsKeywordOnly()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.ExecutedMode.Should().Be(HybridSearchMode.KeywordOnly);
    }

    [Fact]
    public async Task SearchAsync_KeywordOnlyMode_EmbeddingDurationIsZero()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.EmbeddingDurationMs.Should().Be(0);
    }

    #endregion

    #region SearchAsync - Embedding Fallback Tests

    [Fact]
    public async Task SearchAsync_EmbeddingFails_FallsBackToKeywordOnly()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        // Setup embedding to throw exception
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert - Should fall back to keyword-only
        result.Metadata.ExecutedMode.Should().Be(HybridSearchMode.KeywordOnly);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_ReturnsEmbeddingFallbackWarning()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        // Setup embedding to throw exception
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert - Should have EMBEDDING_FALLBACK warning
        result.Metadata.Warnings.Should().NotBeNullOrEmpty();
        result.Metadata.Warnings!.Should().Contain(w => w.Code == SearchWarningCode.EmbeddingFallback);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_WarningHasCorrectMessage()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.VectorOnly);

        // Setup embedding to throw exception
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        var warning = result.Metadata.Warnings!.First(w => w.Code == SearchWarningCode.EmbeddingFallback);
        warning.Message.Should().Contain("keyword");
    }

    #endregion

    #region SearchAsync - Embedding Cache Tests

    [Fact]
    public async Task SearchAsync_CacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        SetupMockEmbeddingCacheHit();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Should NOT call OpenAI when cache hits
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_CacheMiss_CachesGeneratedEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert - Should cache the generated embedding
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync(request.Query!, It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SearchAsync - Timing Metrics Tests

    [Fact]
    public async Task SearchAsync_ReturnsSearchDurationMetrics()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.SearchDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmbeddingDurationMetrics()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.EmbeddingDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SearchAsync_WithVectorSearch_EmbeddingDurationIsPopulated()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(HybridSearchMode.VectorOnly);

        // Add slight delay to ensure timing is measurable
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert - Embedding duration should be recorded
        result.Metadata.EmbeddingDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region SearchAsync - Result Enrichment Tests

    [Fact]
    public async Task SearchAsync_ReturnsResultsWithDocumentInfo()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Results.Should().NotBeEmpty();
        result.Results[0].DocumentId.Should().NotBeNullOrEmpty();
        result.Results[0].Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchAsync_ResultsIncludeScores()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Results.Should().NotBeEmpty();
        result.Results[0].CombinedScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_ResultsIncludeParentEntityInfo()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Results.Should().NotBeEmpty();
        result.Results[0].ParentEntityType.Should().NotBeNullOrEmpty();
        result.Results[0].ParentEntityId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchAsync_ReturnsTotalResultsCount()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.TotalResults.Should().BeGreaterThan(0);
        result.Metadata.ReturnedResults.Should().BeGreaterThan(0);
    }

    #endregion

    #region SearchAsync - Scope=All Tests

    [Fact]
    public async Task SearchAsync_AllScope_ReturnsSuccessResponse()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_AllScope_AppliedFiltersScopeIsAll()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.AppliedFilters.Should().NotBeNull();
        result.Metadata.AppliedFilters!.Scope.Should().Be(SearchScope.All);
        result.Metadata.AppliedFilters.EntityType.Should().BeNull();
        result.Metadata.AppliedFilters.EntityId.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_AllScope_WithOptionalFilters_Works()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                DocumentTypes = new List<string> { "contract" },
                FileTypes = new List<string> { "pdf" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.AppliedFilters!.DocumentTypes.Should().Contain("contract");
        result.Metadata.AppliedFilters.FileTypes.Should().Contain("pdf");
    }

    [Fact]
    public async Task CountAsync_AllScope_ReturnsCount()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All
        };

        SetupMockSearchClientForCount(15);

        // Act
        var result = await service.CountAsync(request, TestTenantId);

        // Assert
        result.Count.Should().Be(15);
        result.AppliedFilters!.Scope.Should().Be(SearchScope.All);
    }

    #endregion

    #region SearchAsync - EntityTypes Filter Tests

    [Fact]
    public async Task SearchAsync_AllScope_WithEntityTypesFilter_ReturnsSuccess()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                EntityTypes = new List<string> { "matter", "project" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.AppliedFilters!.Scope.Should().Be(SearchScope.All);
        result.Metadata.AppliedFilters.EntityTypes.Should().NotBeNull();
        result.Metadata.AppliedFilters.EntityTypes.Should().Contain("matter");
        result.Metadata.AppliedFilters.EntityTypes.Should().Contain("project");
    }

    [Fact]
    public async Task SearchAsync_AllScope_WithSingleEntityType_ReturnsAppliedFilter()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                EntityTypes = new List<string> { "invoice" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.AppliedFilters!.EntityTypes.Should().HaveCount(1);
        result.Metadata.AppliedFilters.EntityTypes.Should().Contain("invoice");
    }

    [Fact]
    public async Task SearchAsync_AllScope_WithNullEntityTypes_NoEntityTypeFilter()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                EntityTypes = null
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.AppliedFilters!.EntityTypes.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_AllScope_WithEmptyEntityTypes_NoEntityTypeFilter()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                EntityTypes = new List<string>()
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Should().NotBeNull();
        result.Metadata.AppliedFilters!.EntityTypes.Should().BeEmpty();
    }

    [Fact]
    public async Task CountAsync_AllScope_WithEntityTypesFilter_ReturnsCount()
    {
        // Arrange
        var service = CreateService();
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.All,
            Filters = new SearchFilters
            {
                EntityTypes = new List<string> { "matter", "contact" }
            }
        };

        SetupMockSearchClientForCount(7);

        // Act
        var result = await service.CountAsync(request, TestTenantId);

        // Assert
        result.Count.Should().Be(7);
        result.AppliedFilters!.EntityTypes.Should().Contain("matter");
        result.AppliedFilters.EntityTypes.Should().Contain("contact");
    }

    #endregion

    #region SearchAsync - Applied Filters Tests

    [Fact]
    public async Task SearchAsync_EntityScope_ReturnsAppliedFilters()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.AppliedFilters.Should().NotBeNull();
        result.Metadata.AppliedFilters!.Scope.Should().Be(SearchScope.Entity);
        result.Metadata.AppliedFilters.EntityType.Should().Be(TestEntityType);
        result.Metadata.AppliedFilters.EntityId.Should().Be(TestEntityId);
    }

    [Fact]
    public async Task SearchAsync_DocumentIdsScope_ReturnsDocumentIdCount()
    {
        // Arrange
        var service = CreateService();
        var documentIds = new List<string> { "doc1", "doc2", "doc3" };
        var request = new SemanticSearchRequest
        {
            Query = "test query",
            Scope = SearchScope.DocumentIds,
            DocumentIds = documentIds
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request, TestTenantId);

        // Assert
        result.Metadata.AppliedFilters.Should().NotBeNull();
        result.Metadata.AppliedFilters!.Scope.Should().Be(SearchScope.DocumentIds);
        result.Metadata.AppliedFilters.DocumentIdCount.Should().Be(3);
    }

    #endregion

    #region SearchAsync - Preprocessor and Postprocessor Tests

    [Fact]
    public async Task SearchAsync_CallsQueryPreprocessor()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert
        _queryPreprocessorMock.Verify(
            x => x.ProcessAsync(request, TestTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_CallsResultPostprocessor()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request, TestTenantId);

        // Assert
        _resultPostprocessorMock.Verify(
            x => x.ProcessAsync(It.IsAny<SemanticSearchResponse>(), request, TestTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region CountAsync Tests

    [Fact]
    public async Task CountAsync_NullRequest_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.CountAsync(null!, TestTenantId));
    }

    [Fact]
    public async Task CountAsync_NullTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act & Assert - ArgumentNullException derives from ArgumentException
        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await service.CountAsync(request, null!));
    }

    [Fact]
    public async Task CountAsync_ReturnsCount()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockSearchClientForCount(42);

        // Act
        var result = await service.CountAsync(request, TestTenantId);

        // Assert
        result.Count.Should().Be(42);
    }

    [Fact]
    public async Task CountAsync_DoesNotGenerateEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockSearchClientForCount(10);

        // Act
        await service.CountAsync(request, TestTenantId);

        // Assert - Count should not generate embedding
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CountAsync_ReturnsAppliedFilters()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockSearchClientForCount(10);

        // Act
        var result = await service.CountAsync(request, TestTenantId);

        // Assert
        result.AppliedFilters.Should().NotBeNull();
        result.AppliedFilters!.Scope.Should().Be(SearchScope.Entity);
    }

    [Fact]
    public async Task CountAsync_UsesCorrectTenant()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockSearchClientForCount(10);

        // Act
        await service.CountAsync(request, TestTenantId);

        // Assert
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(TestTenantId, It.IsAny<CancellationToken>()),
            Times.Once);
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
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockEmbeddingCacheHit()
    {
        // Setup cache as hit (returns cached embedding)
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
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
                It.IsAny<Azure.Search.Documents.SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupMockSearchClientWithResults()
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create test document
        var testDocument = new KnowledgeDocument
        {
            Id = "chunk-1",
            DocumentId = "doc-123",
            TenantId = TestTenantId,
            SpeFileId = "file-123",
            FileName = "TestDocument.pdf",
            DocumentType = "contract",
            FileType = "pdf",
            Content = "Test content for searching",
            ParentEntityType = TestEntityType,
            ParentEntityId = TestEntityId,
            ParentEntityName = "Test Matter",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Create search result
        var searchResult = SearchModelFactory.SearchResult<KnowledgeDocument>(
            testDocument,
            score: 0.95,
            highlights: new Dictionary<string, IList<string>>
            {
                ["content"] = new List<string> { "Test <em>content</em> for searching" }
            });

        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>> { searchResult },
            totalCount: 1,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<Azure.Search.Documents.SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupMockSearchClientForCount(long count)
    {
        var searchClientMock = new Mock<SearchClient>();

        // Create empty search results with count
        var searchResults = SearchModelFactory.SearchResults<KnowledgeDocument>(
            values: new List<SearchResult<KnowledgeDocument>>(),
            totalCount: count,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<KnowledgeDocument>(
                It.IsAny<string>(),
                It.IsAny<Azure.Search.Documents.SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    #endregion
}
