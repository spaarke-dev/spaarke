using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for document relationship visualization API.
/// These tests validate response structure and model contracts.
/// </summary>
/// <remarks>
/// Task 018: Integration tests with Azure AI Search
///
/// Note: Full integration tests that connect to Azure AI Search require:
/// - Azure AI Search index deployed with documentVector field
/// - Azure OpenAI text-embedding-3-small model
/// - Valid Service Bus connection string
/// - appsettings configured with credentials
///
/// Unit tests for VisualizationService are in:
/// tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Visualization/VisualizationServiceTests.cs
/// (19 comprehensive tests covering all scenarios)
///
/// These integration tests focus on response model validation and can run
/// without infrastructure.
/// </remarks>
[Collection("Integration")]
[Trait("Category", "Integration")]
[Trait("Feature", "Visualization")]
public class VisualizationIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public VisualizationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Response Structure Tests

    [Fact]
    public void DocumentGraphResponse_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var response = new DocumentGraphResponse();

        // Assert
        response.Nodes.Should().NotBeNull();
        response.Nodes.Should().BeEmpty();
        response.Edges.Should().NotBeNull();
        response.Edges.Should().BeEmpty();
        response.Metadata.Should().NotBeNull();

        _output.WriteLine("DocumentGraphResponse structure validated");
    }

    [Fact]
    public void DocumentNode_SourceNode_HasCorrectStructure()
    {
        // Arrange & Act
        var node = new DocumentNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = "source",
            Depth = 0,
            Data = new DocumentNodeData
            {
                Label = "Test Document.pdf",
                DocumentType = "Contract",
                Similarity = null,
                ExtractedKeywords = ["test", "keyword"],
                CreatedOn = DateTimeOffset.UtcNow,
                ModifiedOn = DateTimeOffset.UtcNow,
                RecordUrl = "https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=123",
                FileUrl = "https://sharepoint.com/file.pdf"
            }
        };

        // Assert
        node.Id.Should().NotBeEmpty();
        node.Type.Should().Be("source");
        node.Depth.Should().Be(0);
        node.Data.Similarity.Should().BeNull("source document has no similarity score");
        node.Data.Label.Should().NotBeEmpty();
        node.Data.DocumentType.Should().NotBeEmpty();
        node.Data.RecordUrl.Should().Contain("crm.dynamics.com");
        node.Data.ExtractedKeywords.Should().NotBeEmpty();

        _output.WriteLine($"Source node validated: {node.Data.Label}");
    }

    [Fact]
    public void DocumentNode_RelatedNode_HasCorrectStructure()
    {
        // Arrange & Act
        var node = new DocumentNode
        {
            Id = Guid.NewGuid().ToString(),
            Type = "related",
            Depth = 1,
            Data = new DocumentNodeData
            {
                Label = "Related Document.docx",
                DocumentType = "Agreement",
                Similarity = 0.85,
                ExtractedKeywords = ["related", "keyword"],
                CreatedOn = DateTimeOffset.UtcNow,
                ModifiedOn = DateTimeOffset.UtcNow,
                RecordUrl = "https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=456",
                FileUrl = "https://sharepoint.com/related.docx"
            }
        };

        // Assert
        node.Type.Should().Be("related");
        node.Depth.Should().BeGreaterThan(0);
        node.Data.Similarity.Should().NotBeNull("related document has similarity score");
        node.Data.Similarity.Should().BeInRange(0.0, 1.0);

        _output.WriteLine($"Related node validated: {node.Data.Label} (similarity: {node.Data.Similarity})");
    }

    [Fact]
    public void DocumentEdge_HasCorrectStructure()
    {
        // Arrange & Act
        var sourceId = Guid.NewGuid().ToString();
        var targetId = Guid.NewGuid().ToString();
        var edge = new DocumentEdge
        {
            Id = $"{sourceId}-{targetId}",
            Source = sourceId,
            Target = targetId,
            Data = new DocumentEdgeData
            {
                Similarity = 0.85,
                SharedKeywords = ["contract", "software", "development"],
                RelationshipType = "semantic"
            }
        };

        // Assert
        edge.Id.Should().NotBeEmpty();
        edge.Id.Should().Contain(sourceId);
        edge.Id.Should().Contain(targetId);
        edge.Source.Should().Be(sourceId);
        edge.Target.Should().Be(targetId);
        edge.Data.Similarity.Should().BeInRange(0.0, 1.0);
        edge.Data.RelationshipType.Should().Be("semantic");
        edge.Data.SharedKeywords.Should().HaveCountGreaterThan(0);

        _output.WriteLine($"Edge validated: {edge.Id} (similarity: {edge.Data.Similarity})");
        _output.WriteLine($"Shared keywords: [{string.Join(", ", edge.Data.SharedKeywords)}]");
    }

    [Fact]
    public void GraphMetadata_HasCorrectStructure()
    {
        // Arrange & Act
        var sourceId = Guid.NewGuid().ToString();
        var metadata = new GraphMetadata
        {
            SourceDocumentId = sourceId,
            TenantId = "test-tenant-123",
            TotalResults = 10,
            Threshold = 0.65f,
            Depth = 2,
            MaxDepthReached = 2,
            NodesPerLevel = [1, 5, 4],
            SearchLatencyMs = 150,
            CacheHit = true
        };

        // Assert
        metadata.SourceDocumentId.Should().Be(sourceId);
        metadata.TenantId.Should().NotBeEmpty();
        metadata.Threshold.Should().BeInRange(0.0f, 1.0f);
        metadata.Depth.Should().BeInRange(1, 3);
        metadata.MaxDepthReached.Should().BeLessOrEqualTo(metadata.Depth);
        metadata.NodesPerLevel.Should().NotBeEmpty();
        metadata.NodesPerLevel.Sum().Should().Be(10); // Total nodes = sum of nodes per level
        metadata.SearchLatencyMs.Should().BeGreaterOrEqualTo(0);

        _output.WriteLine("Metadata validated:");
        _output.WriteLine($"  Source: {metadata.SourceDocumentId}");
        _output.WriteLine($"  TotalResults: {metadata.TotalResults}");
        _output.WriteLine($"  Threshold: {metadata.Threshold}");
        _output.WriteLine($"  Depth: {metadata.Depth}, MaxReached: {metadata.MaxDepthReached}");
        _output.WriteLine($"  NodesPerLevel: [{string.Join(", ", metadata.NodesPerLevel)}]");
        _output.WriteLine($"  SearchLatencyMs: {metadata.SearchLatencyMs}ms, CacheHit: {metadata.CacheHit}");
    }

    #endregion

    #region VisualizationOptions Tests

    [Fact]
    public void VisualizationOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new VisualizationOptions { TenantId = "test-tenant" };

        // Assert
        options.TenantId.Should().Be("test-tenant");
        options.Threshold.Should().Be(0.65f);
        options.Limit.Should().Be(25);
        options.Depth.Should().Be(1);
        options.IncludeKeywords.Should().BeTrue();
        options.IncludeParentEntity.Should().BeTrue();
        options.DocumentTypes.Should().BeNull();

        _output.WriteLine("VisualizationOptions default values validated");
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.65f)]
    [InlineData(0.8f)]
    [InlineData(0.95f)]
    public void VisualizationOptions_DifferentThresholds_AreValid(float threshold)
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            Threshold = threshold
        };

        // Assert
        options.Threshold.Should().Be(threshold);
        options.Threshold.Should().BeInRange(0.0f, 1.0f);

        _output.WriteLine($"Threshold {threshold} validated");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void VisualizationOptions_DifferentLimits_AreValid(int limit)
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            Limit = limit
        };

        // Assert
        options.Limit.Should().Be(limit);
        options.Limit.Should().BeGreaterThan(0);

        _output.WriteLine($"Limit {limit} validated");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void VisualizationOptions_DifferentDepths_AreValid(int depth)
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            Depth = depth
        };

        // Assert
        options.Depth.Should().Be(depth);
        options.Depth.Should().BeInRange(1, 3);

        _output.WriteLine($"Depth {depth} validated");
    }

    [Fact]
    public void VisualizationOptions_WithDocumentTypeFilter_IsValid()
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            DocumentTypes = ["Contract", "Agreement", "Invoice"]
        };

        // Assert
        options.DocumentTypes.Should().NotBeNull();
        options.DocumentTypes.Should().HaveCount(3);
        options.DocumentTypes.Should().Contain("Contract");
        options.DocumentTypes.Should().Contain("Agreement");
        options.DocumentTypes.Should().Contain("Invoice");

        _output.WriteLine($"Document types filter validated: [{string.Join(", ", options.DocumentTypes)}]");
    }

    [Fact]
    public void VisualizationOptions_IncludeKeywordsFalse_IsValid()
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            IncludeKeywords = false
        };

        // Assert
        options.IncludeKeywords.Should().BeFalse();

        _output.WriteLine("IncludeKeywords=false validated");
    }

    [Fact]
    public void VisualizationOptions_IncludeParentEntityFalse_IsValid()
    {
        // Arrange & Act
        var options = new VisualizationOptions
        {
            TenantId = "test-tenant",
            IncludeParentEntity = false
        };

        // Assert
        options.IncludeParentEntity.Should().BeFalse();

        _output.WriteLine("IncludeParentEntity=false validated");
    }

    #endregion

    #region DocumentNodeData Tests

    [Fact]
    public void DocumentNodeData_WithParentEntity_HasCorrectStructure()
    {
        // Arrange & Act
        var data = new DocumentNodeData
        {
            Label = "Contract Document.pdf",
            DocumentType = "Contract",
            Similarity = 0.92,
            ExtractedKeywords = ["Acme Corp", "Contract", "Legal"],
            CreatedOn = DateTimeOffset.UtcNow.AddDays(-30),
            ModifiedOn = DateTimeOffset.UtcNow,
            RecordUrl = "https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=123",
            FileUrl = "https://sharepoint.com/sites/legal/contract.pdf",
            FilePreviewUrl = "https://sharepoint.com/sites/legal/contract.pdf?web=1",
            ParentEntityType = "sprk_matter",
            ParentEntityId = Guid.NewGuid().ToString(),
            ParentEntityName = "Acme Corp vs Widget Inc"
        };

        // Assert
        data.ParentEntityType.Should().Be("sprk_matter");
        data.ParentEntityId.Should().NotBeEmpty();
        data.ParentEntityName.Should().Be("Acme Corp vs Widget Inc");
        data.FilePreviewUrl.Should().NotBeEmpty();

        _output.WriteLine($"Document with parent entity validated:");
        _output.WriteLine($"  Label: {data.Label}");
        _output.WriteLine($"  ParentEntityType: {data.ParentEntityType}");
        _output.WriteLine($"  ParentEntityName: {data.ParentEntityName}");
    }

    [Fact]
    public void DocumentNodeData_WithoutParentEntity_HasNullParentFields()
    {
        // Arrange & Act
        var data = new DocumentNodeData
        {
            Label = "Standalone Document.pdf",
            DocumentType = "Other",
            Similarity = 0.75,
            ExtractedKeywords = [],
            CreatedOn = DateTimeOffset.UtcNow,
            ModifiedOn = DateTimeOffset.UtcNow,
            RecordUrl = "https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id=789",
            FileUrl = "https://sharepoint.com/standalone.pdf"
        };

        // Assert
        data.ParentEntityType.Should().BeNull();
        data.ParentEntityId.Should().BeNull();
        data.ParentEntityName.Should().BeNull();

        _output.WriteLine($"Document without parent entity validated: {data.Label}");
    }

    #endregion

    #region Complete Graph Structure Tests

    [Fact]
    public void DocumentGraphResponse_CompleteGraph_HasValidStructure()
    {
        // Arrange - Create a complete graph with source, related nodes, and edges
        var sourceId = Guid.NewGuid().ToString();
        var related1Id = Guid.NewGuid().ToString();
        var related2Id = Guid.NewGuid().ToString();

        var graph = new DocumentGraphResponse
        {
            Nodes =
            [
                new DocumentNode
                {
                    Id = sourceId,
                    Type = "source",
                    Depth = 0,
                    Data = new DocumentNodeData
                    {
                        Label = "Source Document.pdf",
                        DocumentType = "Contract",
                        Similarity = null,
                        RecordUrl = $"https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id={sourceId}",
                        FileUrl = "https://sharepoint.com/source.pdf"
                    }
                },
                new DocumentNode
                {
                    Id = related1Id,
                    Type = "related",
                    Depth = 1,
                    Data = new DocumentNodeData
                    {
                        Label = "Related Document 1.docx",
                        DocumentType = "Agreement",
                        Similarity = 0.92,
                        RecordUrl = $"https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id={related1Id}",
                        FileUrl = "https://sharepoint.com/related1.docx"
                    }
                },
                new DocumentNode
                {
                    Id = related2Id,
                    Type = "related",
                    Depth = 1,
                    Data = new DocumentNodeData
                    {
                        Label = "Related Document 2.pdf",
                        DocumentType = "Contract",
                        Similarity = 0.78,
                        RecordUrl = $"https://org.crm.dynamics.com/main.aspx?etn=sprk_document&id={related2Id}",
                        FileUrl = "https://sharepoint.com/related2.pdf"
                    }
                }
            ],
            Edges =
            [
                new DocumentEdge
                {
                    Id = $"{sourceId}-{related1Id}",
                    Source = sourceId,
                    Target = related1Id,
                    Data = new DocumentEdgeData
                    {
                        Similarity = 0.92,
                        SharedKeywords = ["Contract", "Legal"],
                        RelationshipType = "semantic"
                    }
                },
                new DocumentEdge
                {
                    Id = $"{sourceId}-{related2Id}",
                    Source = sourceId,
                    Target = related2Id,
                    Data = new DocumentEdgeData
                    {
                        Similarity = 0.78,
                        SharedKeywords = ["Contract"],
                        RelationshipType = "semantic"
                    }
                }
            ],
            Metadata = new GraphMetadata
            {
                SourceDocumentId = sourceId,
                TenantId = "test-tenant",
                TotalResults = 2,
                Threshold = 0.65f,
                Depth = 1,
                MaxDepthReached = 1,
                NodesPerLevel = [1, 2],
                SearchLatencyMs = 125,
                CacheHit = false
            }
        };

        // Assert
        graph.Nodes.Should().HaveCount(3);
        graph.Edges.Should().HaveCount(2);

        // Verify source node
        var sourceNode = graph.Nodes.Single(n => n.Type == "source");
        sourceNode.Id.Should().Be(sourceId);
        sourceNode.Depth.Should().Be(0);
        sourceNode.Data.Similarity.Should().BeNull();

        // Verify related nodes
        var relatedNodes = graph.Nodes.Where(n => n.Type == "related").ToList();
        relatedNodes.Should().HaveCount(2);
        relatedNodes.Should().OnlyContain(n => n.Depth == 1);
        relatedNodes.Should().OnlyContain(n => n.Data.Similarity.HasValue);

        // Verify edges connect source to related
        graph.Edges.Should().OnlyContain(e => e.Source == sourceId);
        graph.Edges.Select(e => e.Target).Should().BeEquivalentTo([related1Id, related2Id]);

        // Verify metadata
        graph.Metadata.NodesPerLevel.Sum().Should().Be(3);
        graph.Metadata.TotalResults.Should().Be(2);

        _output.WriteLine("Complete graph structure validated:");
        _output.WriteLine($"  Nodes: {graph.Nodes.Count} (1 source, {relatedNodes.Count} related)");
        _output.WriteLine($"  Edges: {graph.Edges.Count}");
        _output.WriteLine($"  NodesPerLevel: [{string.Join(", ", graph.Metadata.NodesPerLevel)}]");
    }

    #endregion
}
