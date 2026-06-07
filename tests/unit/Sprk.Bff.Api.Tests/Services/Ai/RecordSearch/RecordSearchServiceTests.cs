using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.RecordMatching;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.RecordSearch;

/// <summary>
/// Unit tests for RecordSearchService.
/// Tests hybrid search modes, embedding cache, Redis caching, OData filter generation,
/// result processing, and fallback behavior.
/// </summary>
public class RecordSearchServiceTests
{
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<SearchClient> _searchClientMock;
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IEmbeddingCache> _embeddingCacheMock;
    private readonly Mock<IDistributedCache> _distributedCacheMock;
    private readonly Mock<ILogger<RecordSearchService>> _loggerMock;
    // multi-container-multi-index-r1 FR-BFF-07 (part 3) — explicit-index resolver dependency.
    // Required by the new 8-arg ctor but exercised only on the explicit-SearchIndexName path;
    // existing direct-path tests do not configure setups on this mock and their assertions are unchanged.
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly IOptions<DocumentIntelligenceOptions> _docIntelOptions;

    // Test embedding (3072 dimensions like text-embedding-3-large)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    private const string TestIndexName = "spaarke-records-index";

    public RecordSearchServiceTests()
    {
        _searchIndexClientMock = new Mock<SearchIndexClient>();
        _searchClientMock = new Mock<SearchClient>();
        _openAiClientMock = new Mock<IOpenAiClient>();
        _embeddingCacheMock = new Mock<IEmbeddingCache>();
        _distributedCacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<RecordSearchService>>();
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();

        _docIntelOptions = Options.Create(new DocumentIntelligenceOptions
        {
            AiSearchIndexName = TestIndexName
        });

        // Create a test embedding vector (3072 dimensions for text-embedding-3-large)
        var embedding = new float[3072];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);

        // Wire SearchIndexClient.GetSearchClient to return mock SearchClient
        _searchIndexClientMock
            .Setup(x => x.GetSearchClient(TestIndexName))
            .Returns(_searchClientMock.Object);

        // Default: embedding cache returns content hash
        _embeddingCacheMock
            .Setup(x => x.ComputeContentHash(It.IsAny<string>()))
            .Returns("test-hash-abc");

        // Default: distributed cache returns null (cache miss)
        _distributedCacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
    }

    private RecordSearchService CreateService()
    {
        return new RecordSearchService(
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _distributedCacheMock.Object,
            _docIntelOptions,
            _loggerMock.Object,
            _deploymentServiceMock.Object,
            _httpContextAccessorMock.Object);
    }

    private static RecordSearchRequest CreateValidRequest(string? hybridMode = null)
    {
        return new RecordSearchRequest
        {
            Query = "test search query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            Options = hybridMode != null
                ? new RecordSearchOptions { HybridMode = hybridMode }
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
            async () => await service.SearchAsync(null!));
    }

    #endregion

    #region SearchAsync - Valid Request Returns Response

    [Fact]
    public async Task SearchAsync_WithValidRequest_ReturnsResponse()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Results.Should().NotBeNull();
        result.Metadata.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithValidRequest_ReturnsMetadataWithHybridMode()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Metadata.HybridMode.Should().Be(RecordHybridSearchMode.Rrf);
    }

    [Fact]
    public async Task SearchAsync_WithValidRequest_ReturnsSearchTimeMetric()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Metadata.SearchTime.Should().BeGreaterOrEqualTo(0);
    }

    #endregion

    #region SearchAsync - Redis Cache Tests

    [Fact]
    public async Task SearchAsync_WithCachedResult_ReturnsCachedResponseWithoutCallingSearch()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        var cachedResponse = new RecordSearchResponse
        {
            Results = new List<RecordSearchResult>
            {
                new RecordSearchResult
                {
                    RecordId = "cached-id",
                    RecordType = RecordEntityType.Matter,
                    RecordName = "Cached Matter",
                    ConfidenceScore = 0.85
                }
            },
            Metadata = new RecordSearchMetadata
            {
                TotalCount = 1,
                SearchTime = 10,
                HybridMode = RecordHybridSearchMode.Rrf
            }
        };

        // Setup distributed cache to return serialized response
        var json = System.Text.Json.JsonSerializer.Serialize(cachedResponse);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        _distributedCacheMock
            .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await service.SearchAsync(request);

        // Assert - Should return cached result
        result.Results.Should().HaveCount(1);
        result.Results[0].RecordId.Should().Be("cached-id");
        result.Results[0].RecordName.Should().Be("Cached Matter");

        // Verify search was NOT called
        _searchClientMock.Verify(
            x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify embedding was NOT generated
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_StoresResultInCache()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Should write to distributed cache
        _distributedCacheMock.Verify(
            x => x.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SearchAsync - Embedding and Hybrid Mode Tests

    [Fact]
    public async Task SearchAsync_RrfMode_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Embedding should be generated for RRF mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(
                request.Query,
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_VectorOnlyMode_GeneratesEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.VectorOnly);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Embedding should be generated for vector-only mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(
                request.Query,
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_KeywordOnlyMode_SkipsEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Embedding should NOT be generated for keyword-only mode
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_KeywordOnlyMode_ReportsKeywordOnlyInMetadata()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Metadata.HybridMode.Should().Be(RecordHybridSearchMode.KeywordOnly);
    }

    [Fact]
    public async Task SearchAsync_DefaultMode_IsRrf()
    {
        // Arrange
        var service = CreateService();
        // No hybrid mode specified - should default to RRF
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Metadata.HybridMode.Should().Be(RecordHybridSearchMode.Rrf);
    }

    #endregion

    #region SearchAsync - Embedding Fallback Tests

    [Fact]
    public async Task SearchAsync_EmbeddingFails_FallsBackToKeywordOnly()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.Rrf);

        // Setup embedding cache miss
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        // Setup embedding generation to throw
        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert - Should fall back to keyword-only
        result.Metadata.HybridMode.Should().Be(RecordHybridSearchMode.KeywordOnly);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingFails_StillReturnsResults()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.VectorOnly);

        // Setup embedding cache miss
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        // Setup embedding generation to throw
        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OpenAI service unavailable"));

        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert - Should still return results from keyword fallback
        result.Should().NotBeNull();
        result.Results.Should().NotBeNull();
    }

    #endregion

    #region SearchAsync - Embedding Cache Tests

    [Fact]
    public async Task SearchAsync_EmbeddingCacheHit_DoesNotCallOpenAi()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.Rrf);

        // Setup embedding cache hit
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Should NOT call OpenAI when embedding cache hits
        _openAiClientMock.Verify(
            x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_EmbeddingCacheMiss_CachesGeneratedEmbedding()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.Rrf);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Should cache the generated embedding
        _embeddingCacheMock.Verify(
            x => x.SetEmbeddingForContentAsync(
                request.Query,
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SearchAsync - RecordType Filter Tests

    [Fact]
    public async Task SearchAsync_WithSingleRecordType_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        _searchClientMock.Verify(
            x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithMultipleRecordTypes_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter, RecordEntityType.Project, RecordEntityType.Invoice }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
        _searchClientMock.Verify(
            x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region SearchAsync - Organizations Filter Tests

    [Fact]
    public async Task SearchAsync_WithOrganizationsFilter_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            Filters = new RecordSearchFilters
            {
                Organizations = new List<string> { "Acme Corp", "Globex" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region SearchAsync - Result Processing Tests

    [Fact]
    public async Task SearchAsync_WithResults_ReturnsTotalCount()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Metadata.TotalCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_WithResults_ReturnsRecordDetails()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Results.Should().NotBeEmpty();
        result.Results[0].RecordId.Should().NotBeNullOrEmpty();
        result.Results[0].RecordType.Should().NotBeNullOrEmpty();
        result.Results[0].RecordName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithResults_ReturnsNormalizedConfidenceScore()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClientWithResults();

        // Act
        var result = await service.SearchAsync(request);

        // Assert - Confidence score should be normalized to 0-1 range
        result.Results.Should().NotBeEmpty();
        result.Results[0].ConfidenceScore.Should().BeGreaterOrEqualTo(0.0);
        result.Results[0].ConfidenceScore.Should().BeLessOrEqualTo(1.0);
    }

    [Fact]
    public async Task SearchAsync_WithNoResults_ReturnsEmptyResultList()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        SetupMockEmbedding();
        SetupMockSearchClient(); // Returns empty results

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Results.Should().BeEmpty();
        result.Metadata.TotalCount.Should().Be(0);
    }

    #endregion

    #region SearchAsync - Azure Search Failure Tests

    [Fact]
    public async Task SearchAsync_WhenSearchFails_ThrowsRequestFailedException()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.KeywordOnly);

        // Setup SearchClient to throw RequestFailedException
        _searchClientMock
            .Setup(x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "Internal search error"));

        // Act & Assert
        await Assert.ThrowsAsync<RequestFailedException>(
            async () => await service.SearchAsync(request));
    }

    #endregion

    #region SearchAsync - Pagination Options Tests

    [Fact]
    public async Task SearchAsync_WithCustomPagination_PassesOptionsToSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            Options = new RecordSearchOptions
            {
                Limit = 10,
                Offset = 20,
                HybridMode = RecordHybridSearchMode.KeywordOnly
            }
        };

        SearchOptions? capturedOptions = null;
        _searchClientMock
            .Setup(x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SearchOptions, CancellationToken>((text, opts, ct) => capturedOptions = opts)
            .ReturnsAsync(CreateEmptySearchResponse());

        // Act
        await service.SearchAsync(request);

        // Assert
        capturedOptions.Should().NotBeNull();
        capturedOptions!.Size.Should().Be(10);
        capturedOptions.Skip.Should().Be(20);
        capturedOptions.IncludeTotalCount.Should().BeTrue();
    }

    #endregion

    #region SearchAsync - Uses Correct Index Name Tests

    [Fact]
    public async Task SearchAsync_UsesConfiguredIndexName()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(RecordHybridSearchMode.KeywordOnly);

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Should get search client with the configured index name
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(TestIndexName),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_FallsBackToDefaultIndexName_WhenConfigEmpty()
    {
        // Arrange
        var emptyOptions = Options.Create(new DocumentIntelligenceOptions
        {
            AiSearchIndexName = "" // empty
        });

        var service = new RecordSearchService(
            _searchIndexClientMock.Object,
            _openAiClientMock.Object,
            _embeddingCacheMock.Object,
            _distributedCacheMock.Object,
            emptyOptions,
            _loggerMock.Object,
            _deploymentServiceMock.Object,
            _httpContextAccessorMock.Object);

        var request = CreateValidRequest(RecordHybridSearchMode.KeywordOnly);

        // Setup the default index name
        _searchIndexClientMock
            .Setup(x => x.GetSearchClient("spaarke-records-index"))
            .Returns(_searchClientMock.Object);

        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert - Should fall back to default index name
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient("spaarke-records-index"),
            Times.Once);
    }

    #endregion

    #region SearchAsync - Optional Filters Tests

    [Fact]
    public async Task SearchAsync_WithPeopleFilter_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            Filters = new RecordSearchFilters
            {
                People = new List<string> { "John Doe", "Jane Smith" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithReferenceNumbersFilter_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            Filters = new RecordSearchFilters
            {
                ReferenceNumbers = new List<string> { "MAT-2024-001", "INV-99" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_WithAllFilters_ExecutesSearch()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter, RecordEntityType.Project },
            Filters = new RecordSearchFilters
            {
                Organizations = new List<string> { "Acme Corp" },
                People = new List<string> { "John Doe" },
                ReferenceNumbers = new List<string> { "REF-001" }
            }
        };

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        var result = await service.SearchAsync(request);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion

    #region SearchAsync - Cache Key Tests

    [Fact]
    public async Task SearchAsync_DifferentQueries_UseDifferentCacheKeys()
    {
        // Arrange
        var service = CreateService();
        var hashes = new List<string>();

        // Return different hashes for different content
        _embeddingCacheMock
            .Setup(x => x.ComputeContentHash(It.IsAny<string>()))
            .Returns<string>(s => $"hash-{s.GetHashCode()}");

        SetupMockEmbedding();
        SetupMockSearchClient();

        var request1 = new RecordSearchRequest
        {
            Query = "first query",
            RecordTypes = new List<string> { RecordEntityType.Matter }
        };

        var request2 = new RecordSearchRequest
        {
            Query = "second query",
            RecordTypes = new List<string> { RecordEntityType.Matter }
        };

        // Act
        await service.SearchAsync(request1);
        await service.SearchAsync(request2);

        // Assert - Should have called cache get twice with different keys
        _distributedCacheMock.Verify(
            x => x.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region SearchAsync - SearchIndexName Resolver Routing Tests (FR-BFF-07 part 3)

    // multi-container-multi-index-r1 task 015. These tests verify that
    // RecordSearchService routes through IKnowledgeDeploymentService.GetSearchClientAsync(
    //   tenantId, indexName, ct) when RecordSearchRequest.SearchIndexName is supplied,
    // and that the existing direct path is preserved verbatim when it is not (NFR-02).
    // The allow-list rejection path is verified by propagating a SdapProblemException
    // thrown by the mocked resolver — RecordSearchService MUST NOT swallow or transform
    // it (FR-BFF-02 / NFR-08 single-source-of-truth invariant).

    [Fact]
    public async Task SearchAsync_WithExplicitSearchIndexName_RoutesThroughResolver()
    {
        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            SearchIndexName = "spaarke-file-index"
        };

        // Wire HttpContext so the service can derive tenantId for the resolver cache key
        var tenantId = "tenant-abc-123";
        var claims = new[]
        {
            new System.Security.Claims.Claim("tid", tenantId)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "test");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Resolver returns the mock SearchClient
        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_searchClientMock.Object);

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(
                tenantId,
                "spaarke-file-index",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Direct path must NOT be taken when SearchIndexName is supplied
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithoutSearchIndexName_UsesDirectPath()
    {
        // NFR-02 regression guard: when SearchIndexName is null (existing callers), the
        // service MUST continue to invoke SearchIndexClient.GetSearchClient(indexName)
        // directly, never the resolver. This is the existing behavior preserved.

        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(); // SearchIndexName is null

        SetupMockEmbedding();
        SetupMockSearchClient();

        // Act
        await service.SearchAsync(request);

        // Assert — direct path used
        _searchIndexClientMock.Verify(
            x => x.GetSearchClient(TestIndexName),
            Times.Once);

        // Resolver MUST NOT be called
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _deploymentServiceMock.Verify(
            x => x.GetSearchClientAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithRejectedSearchIndexName_PropagatesAllowListException()
    {
        // FR-BFF-02 / NFR-08: when the resolver rejects an index name not in the allow-list
        // it throws SdapProblemException(INDEX_NOT_ALLOWED, 400). RecordSearchService MUST
        // surface that exception verbatim — the catch (RequestFailedException) branch must
        // not match it; no swallowing, no transformation.

        // Arrange
        var service = CreateService();
        var request = new RecordSearchRequest
        {
            Query = "test query",
            RecordTypes = new List<string> { RecordEntityType.Matter },
            SearchIndexName = "not-in-allow-list"
        };

        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    new[] { new System.Security.Claims.Claim("tid", "tenant-xyz") }, "test"))
        };
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        // Resolver throws as if the index were not in the allow-list (mirrors
        // KnowledgeDeploymentService.ValidateAllowedIndex behavior from task 010).
        var rejection = new SdapProblemException(
            code: "INDEX_NOT_ALLOWED",
            title: "AI Search index not allowed",
            detail: "The requested AI Search index 'not-in-allow-list' is not in the configured allow-list (AiSearch:AllowedIndexes). Contact your administrator to enable this index.",
            statusCode: 400,
            extensions: new Dictionary<string, object>
            {
                ["indexName"] = "not-in-allow-list"
            });

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(rejection);

        SetupMockEmbedding();

        // Act & Assert
        var actual = await Assert.ThrowsAsync<SdapProblemException>(
            async () => await service.SearchAsync(request));

        actual.Code.Should().Be("INDEX_NOT_ALLOWED");
        actual.StatusCode.Should().Be(400);
        actual.Extensions.Should().ContainKey("indexName")
            .WhoseValue.Should().Be("not-in-allow-list");
    }

    #endregion

    #region Helper Methods

    private void SetupMockEmbedding()
    {
        // Setup embedding cache miss
        _embeddingCacheMock
            .Setup(x => x.GetEmbeddingForContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReadOnlyMemory<float>?)null);

        _openAiClientMock
            .Setup(x => x.GenerateEmbeddingAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEmbedding);
    }

    private void SetupMockSearchClient()
    {
        var response = CreateEmptySearchResponse();

        _searchClientMock
            .Setup(x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private void SetupMockSearchClientWithResults()
    {
        // Create test document
        var testDocument = new SearchIndexDocument
        {
            Id = "sprk_matter_abc123",
            RecordType = RecordEntityType.Matter,
            RecordName = "Test Matter 001",
            RecordDescription = "A test matter for unit testing",
            DataverseRecordId = "abc-123-def-456",
            DataverseEntityName = RecordEntityType.Matter,
            Organizations = new List<string> { "Acme Corp" },
            People = new List<string> { "John Doe" },
            Keywords = "legal,contract,test",
            LastModified = DateTimeOffset.UtcNow
        };

        // Create search result with score
        var searchResult = SearchModelFactory.SearchResult<SearchIndexDocument>(
            testDocument,
            score: 3.2, // Semantic reranker score (0-4 range)
            highlights: new Dictionary<string, IList<string>>
            {
                ["recordName"] = new List<string> { "Test <em>Matter</em> 001" }
            });

        var searchResults = SearchModelFactory.SearchResults<SearchIndexDocument>(
            values: new List<SearchResult<SearchIndexDocument>> { searchResult },
            totalCount: 1,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var response = Response.FromValue(searchResults, null!);

        _searchClientMock
            .Setup(x => x.SearchAsync<SearchIndexDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
    }

    private static Response<SearchResults<SearchIndexDocument>> CreateEmptySearchResponse()
    {
        var searchResults = SearchModelFactory.SearchResults<SearchIndexDocument>(
            values: new List<SearchResult<SearchIndexDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        return Response.FromValue(searchResults, null!);
    }

    #endregion
}
