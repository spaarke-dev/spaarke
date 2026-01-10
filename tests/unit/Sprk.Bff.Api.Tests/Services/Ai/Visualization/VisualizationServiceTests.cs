using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Visualization;

/// <summary>
/// Unit tests for VisualizationService - Document relationship visualization.
/// Tests graph building, similarity scoring, filtering, and error handling.
/// </summary>
public class VisualizationServiceTests
{
    private readonly Mock<IKnowledgeDeploymentService> _deploymentServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<VisualizationService>> _loggerMock;
    private readonly IOptions<DataverseOptions> _dataverseOptions;

    // Test embedding (1536 dimensions like text-embedding-3-small)
    private readonly ReadOnlyMemory<float> _testEmbedding;

    // Test document IDs
    private readonly Guid _sourceDocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly string _tenantId = "test-tenant-123";

    public VisualizationServiceTests()
    {
        _deploymentServiceMock = new Mock<IKnowledgeDeploymentService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<VisualizationService>>();
        _dataverseOptions = Options.Create(new DataverseOptions
        {
            EnvironmentUrl = "https://testorg.crm.dynamics.com"
        });

        // Create a test embedding vector (1536 dimensions)
        var embedding = new float[1536];
        for (int i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(i % 10) / 10f;
        }
        _testEmbedding = new ReadOnlyMemory<float>(embedding);
    }

    private VisualizationService CreateService()
    {
        return new VisualizationService(
            _deploymentServiceMock.Object,
            _dataverseServiceMock.Object,
            _dataverseOptions,
            _loggerMock.Object);
    }

    #region Parameter Validation Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.GetRelatedDocumentsAsync(_sourceDocumentId, null!));
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = string.Empty };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetRelatedDocumentsAsync(_sourceDocumentId, options));
    }

    // Note: Whitespace TenantId validation is handled at the endpoint level (VisualizationEndpoints.cs)
    // The service assumes valid inputs from callers

    #endregion

    #region Source Document Not Found Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_SourceNotFound_ReturnsEmptyGraph()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupEmptySearchClient();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Should().NotBeNull();
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Metadata.TotalResults.Should().Be(0);
        result.Metadata.SourceDocumentId.Should().Be(_sourceDocumentId.ToString());
    }

    #endregion

    #region Valid Query Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_ValidQuery_ReturnsGraphWithSourceNode()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupSearchClientWithSourceDocument();
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Should().NotBeNull();
        result.Nodes.Should().HaveCountGreaterOrEqualTo(1);
        result.Nodes[0].Type.Should().Be("source");
        result.Nodes[0].Depth.Should().Be(0);
        result.Nodes[0].Id.Should().Be(_sourceDocumentId.ToString());
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_WithRelatedDocuments_ReturnsNodesAndEdges()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Threshold = 0.5f };

        SetupSearchClientWithRelatedDocuments(3, 0.8);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Nodes.Should().HaveCount(4); // 1 source + 3 related
        result.Edges.Should().HaveCount(3); // 3 edges from source to related

        // Verify source node
        var sourceNode = result.Nodes.First(n => n.Type == "source");
        sourceNode.Depth.Should().Be(0);

        // Verify related nodes
        var relatedNodes = result.Nodes.Where(n => n.Type == "related").ToList();
        relatedNodes.Should().HaveCount(3);
        relatedNodes.Should().OnlyContain(n => n.Depth == 1);

        // Verify edges
        result.Edges.Should().OnlyContain(e => e.Source == _sourceDocumentId.ToString());
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_ReturnsMetadataWithSearchLatency()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupSearchClientWithSourceDocument();
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Metadata.Should().NotBeNull();
        result.Metadata.SearchLatencyMs.Should().BeGreaterOrEqualTo(0);
        result.Metadata.TenantId.Should().Be(_tenantId);
        result.Metadata.SourceDocumentId.Should().Be(_sourceDocumentId.ToString());
    }

    #endregion

    #region Threshold Filtering Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_ThresholdFilter_ExcludesLowScoreDocuments()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Threshold = 0.7f };

        // Setup: 3 documents - scores 0.9, 0.6, 0.8
        // Only 2 should pass the 0.7 threshold
        SetupSearchClientWithMixedScores(0.7f);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert - Only documents with score >= 0.7 should be included
        var relatedNodes = result.Nodes.Where(n => n.Type == "related").ToList();
        relatedNodes.Should().HaveCount(2); // 0.9 and 0.8 pass, 0.6 filtered

        relatedNodes.Should().OnlyContain(n => n.Data.Similarity >= 0.7);
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_HighThreshold_ReturnsOnlySourceNode()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Threshold = 0.99f };

        SetupSearchClientWithRelatedDocuments(3, 0.7); // All scores at 0.7
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert - No documents pass 0.99 threshold
        result.Nodes.Should().HaveCount(1); // Only source node
        result.Edges.Should().BeEmpty();
    }

    #endregion

    #region Limit (Node Capping) Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_LimitOption_CapsNodeCount()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Limit = 5, Threshold = 0.5f };

        // Setup: 10 related documents, but limit is 5
        SetupSearchClientWithRelatedDocuments(10, 0.8);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        var relatedNodes = result.Nodes.Where(n => n.Type == "related").ToList();
        relatedNodes.Should().HaveCount(5); // Capped at limit
        result.Edges.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_LimitGreaterThanResults_ReturnsAllResults()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Limit = 50, Threshold = 0.5f };

        // Setup: Only 3 related documents
        SetupSearchClientWithRelatedDocuments(3, 0.8);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        var relatedNodes = result.Nodes.Where(n => n.Type == "related").ToList();
        relatedNodes.Should().HaveCount(3); // All 3 returned
    }

    #endregion

    #region Document Type Filtering Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_DocumentTypeFilter_BuildsCorrectFilter()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions
        {
            TenantId = _tenantId,
            DocumentTypes = new List<string> { "Contract", "NDA" }
        };

        var searchClientMock = SetupSearchClientWithSourceDocument();
        SetupDataverseMetadata();

        // Act
        await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert - Verify SearchAsync was called (filter is built correctly)
        searchClientMock.Verify(
            x => x.SearchAsync<It.IsAnyType>(
                It.IsAny<string>(),
                It.Is<SearchOptions>(so => so.Filter != null && so.Filter.Contains("documentType")),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    #endregion

    #region Shared Keywords Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_IncludeKeywords_EdgesContainSharedKeywords()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, IncludeKeywords = true };

        SetupSearchClientWithRelatedDocuments(1, 0.8);
        SetupDataverseMetadataWithKeywords("Acme Corp, Contract, Legal", "Acme Corp, Agreement, Legal");

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Edges.Should().HaveCount(1);
        result.Edges[0].Data.SharedKeywords.Should().Contain("Acme Corp");
        result.Edges[0].Data.SharedKeywords.Should().Contain("Legal");
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_NoIncludeKeywords_EdgesHaveEmptyKeywords()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, IncludeKeywords = false };

        SetupSearchClientWithRelatedDocuments(1, 0.8);
        SetupDataverseMetadataWithKeywords("Acme Corp, Contract", "Acme Corp, Agreement");

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Edges.Should().HaveCount(1);
        result.Edges[0].Data.SharedKeywords.Should().BeEmpty();
    }

    #endregion

    #region Edge Data Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_EdgeData_ContainsSimilarityAndRelationshipType()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId, Threshold = 0.5f };

        SetupSearchClientWithRelatedDocuments(1, 0.85);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Edges.Should().HaveCount(1);
        var edge = result.Edges[0];
        edge.Data.Similarity.Should().BeApproximately(0.85, 0.01);
        edge.Data.RelationshipType.Should().Be("semantic");
    }

    #endregion

    #region Node Data Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_NodeData_ContainsDocumentInfo()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupSearchClientWithSourceDocument();
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        var sourceNode = result.Nodes.First(n => n.Type == "source");
        sourceNode.Data.Should().NotBeNull();
        sourceNode.Data.Label.Should().NotBeEmpty();
        sourceNode.Data.RecordUrl.Should().Contain("crm.dynamics.com");
        sourceNode.Data.RecordUrl.Should().Contain(_sourceDocumentId.ToString());
    }

    #endregion

    #region Metadata Tests

    [Fact]
    public async Task GetRelatedDocumentsAsync_Metadata_ContainsNodesPerLevel()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupSearchClientWithRelatedDocuments(3, 0.8);
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Metadata.NodesPerLevel.Should().HaveCount(2); // Level 0 and Level 1
        result.Metadata.NodesPerLevel[0].Should().Be(1); // Source node at level 0
        result.Metadata.NodesPerLevel[1].Should().Be(3); // 3 related at level 1
    }

    [Fact]
    public async Task GetRelatedDocumentsAsync_NoRelatedDocuments_MetadataShowsDepthZero()
    {
        // Arrange
        var service = CreateService();
        var options = new VisualizationOptions { TenantId = _tenantId };

        SetupSearchClientWithSourceDocumentOnly();
        SetupDataverseMetadata();

        // Act
        var result = await service.GetRelatedDocumentsAsync(_sourceDocumentId, options);

        // Assert
        result.Metadata.MaxDepthReached.Should().Be(0);
        result.Metadata.TotalResults.Should().Be(0);
    }

    #endregion

    #region VisualizationOptions Default Values Tests

    [Fact]
    public void VisualizationOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new VisualizationOptions { TenantId = "test" };

        // Assert
        options.Threshold.Should().Be(0.65f);
        options.Limit.Should().Be(25);
        options.Depth.Should().Be(1);
        options.IncludeKeywords.Should().BeTrue();
        options.IncludeParentEntity.Should().BeTrue();
        options.DocumentTypes.Should().BeNull();
    }

    #endregion

    #region DocumentGraphResponse Default Values Tests

    [Fact]
    public void DocumentGraphResponse_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var response = new DocumentGraphResponse();

        // Assert
        response.Nodes.Should().BeEmpty();
        response.Edges.Should().BeEmpty();
        response.Metadata.Should().NotBeNull();
    }

    #endregion

    #region Helper Methods

    private void SetupEmptySearchClient()
    {
        var searchClientMock = new Mock<SearchClient>();

        var searchResults = SearchModelFactory.SearchResults<VisualizationDocument>(
            values: new List<SearchResult<VisualizationDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        var responseMock = Response.FromValue(searchResults, null!);

        searchClientMock
            .Setup(x => x.SearchAsync<VisualizationDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock);

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private Mock<SearchClient> SetupSearchClientWithSourceDocument()
    {
        var searchClientMock = new Mock<SearchClient>();
        var callCount = 0;

        searchClientMock
            .Setup(x => x.SearchAsync<VisualizationDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, SearchOptions options, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First call: Return source document
                    return CreateSourceDocumentResponse();
                }
                else
                {
                    // Second call: Return empty related documents
                    return CreateEmptySearchResponse();
                }
            });

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);

        return searchClientMock;
    }

    private void SetupSearchClientWithSourceDocumentOnly()
    {
        var searchClientMock = new Mock<SearchClient>();
        var callCount = 0;

        searchClientMock
            .Setup(x => x.SearchAsync<VisualizationDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, SearchOptions options, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateSourceDocumentResponse();
                }
                else
                {
                    return CreateEmptySearchResponse();
                }
            });

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupSearchClientWithRelatedDocuments(int count, double score)
    {
        var searchClientMock = new Mock<SearchClient>();
        var callCount = 0;

        searchClientMock
            .Setup(x => x.SearchAsync<VisualizationDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, SearchOptions options, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateSourceDocumentResponse();
                }
                else
                {
                    return CreateRelatedDocumentsResponse(count, score);
                }
            });

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupSearchClientWithMixedScores(float threshold)
    {
        var searchClientMock = new Mock<SearchClient>();
        var callCount = 0;

        searchClientMock
            .Setup(x => x.SearchAsync<VisualizationDocument>(
                It.IsAny<string>(),
                It.IsAny<SearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string query, SearchOptions options, CancellationToken ct) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return CreateSourceDocumentResponse();
                }
                else
                {
                    // Return documents with scores 0.9, 0.6, 0.8
                    return CreateMixedScoreResponse();
                }
            });

        _deploymentServiceMock
            .Setup(x => x.GetSearchClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchClientMock.Object);
    }

    private void SetupDataverseMetadata()
    {
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken ct) => new DocumentEntity
            {
                Id = id,
                Name = $"Document {id[..8]}",
                GraphDriveId = "test-drive-id",
                GraphItemId = "test-item-id",
                Keywords = "keyword1, keyword2",
                CreatedOn = DateTime.UtcNow.AddDays(-10),
                ModifiedOn = DateTime.UtcNow
            });
    }

    private void SetupDataverseMetadataWithKeywords(string sourceKeywords, string relatedKeywords)
    {
        var callCount = 0;
        _dataverseServiceMock
            .Setup(x => x.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken ct) =>
            {
                callCount++;
                return new DocumentEntity
                {
                    Id = id,
                    Name = $"Document {id[..8]}",
                    GraphDriveId = "test-drive-id",
                    GraphItemId = "test-item-id",
                    Keywords = callCount == 1 ? sourceKeywords : relatedKeywords,
                    CreatedOn = DateTime.UtcNow.AddDays(-10),
                    ModifiedOn = DateTime.UtcNow
                };
            });
    }

    private Response<SearchResults<VisualizationDocument>> CreateEmptySearchResponse()
    {
        var searchResults = SearchModelFactory.SearchResults<VisualizationDocument>(
            values: new List<SearchResult<VisualizationDocument>>(),
            totalCount: 0,
            facets: null,
            coverage: null,
            rawResponse: null!);

        return Response.FromValue(searchResults, null!);
    }

    private Response<SearchResults<VisualizationDocument>> CreateSourceDocumentResponse()
    {
        var sourceDoc = new VisualizationDocument
        {
            Id = Guid.NewGuid().ToString(),
            DocumentId = _sourceDocumentId.ToString(),
            DocumentName = "Source Document.pdf",
            DocumentType = "Contract",
            TenantId = _tenantId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow,
            DocumentVector = _testEmbedding
        };

        var searchResult = SearchModelFactory.SearchResult(sourceDoc, 1.0, null);
        var searchResults = SearchModelFactory.SearchResults<VisualizationDocument>(
            values: new List<SearchResult<VisualizationDocument>> { searchResult },
            totalCount: 1,
            facets: null,
            coverage: null,
            rawResponse: null!);

        return Response.FromValue(searchResults, null!);
    }

    private Response<SearchResults<VisualizationDocument>> CreateRelatedDocumentsResponse(int count, double score)
    {
        var results = new List<SearchResult<VisualizationDocument>>();

        for (int i = 0; i < count; i++)
        {
            var doc = new VisualizationDocument
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = Guid.NewGuid().ToString(),
                DocumentName = $"Related Document {i + 1}.pdf",
                DocumentType = "Contract",
                TenantId = _tenantId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow,
                DocumentVector = _testEmbedding
            };

            var searchResult = SearchModelFactory.SearchResult(doc, score, null);
            results.Add(searchResult);
        }

        var searchResults = SearchModelFactory.SearchResults<VisualizationDocument>(
            values: results,
            totalCount: count,
            facets: null,
            coverage: null,
            rawResponse: null!);

        return Response.FromValue(searchResults, null!);
    }

    private Response<SearchResults<VisualizationDocument>> CreateMixedScoreResponse()
    {
        var scores = new[] { 0.9, 0.6, 0.8 };
        var results = new List<SearchResult<VisualizationDocument>>();

        for (int i = 0; i < scores.Length; i++)
        {
            var doc = new VisualizationDocument
            {
                Id = Guid.NewGuid().ToString(),
                DocumentId = Guid.NewGuid().ToString(),
                DocumentName = $"Mixed Score Doc {i + 1}.pdf",
                DocumentType = "Contract",
                TenantId = _tenantId,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow,
                DocumentVector = _testEmbedding
            };

            var searchResult = SearchModelFactory.SearchResult(doc, scores[i], null);
            results.Add(searchResult);
        }

        var searchResults = SearchModelFactory.SearchResults<VisualizationDocument>(
            values: results,
            totalCount: scores.Length,
            facets: null,
            coverage: null,
            rawResponse: null!);

        return Response.FromValue(searchResults, null!);
    }

    #endregion
}
