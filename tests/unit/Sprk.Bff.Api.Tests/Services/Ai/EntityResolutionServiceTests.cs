using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

// Explicit alias for Services.Ai.OperationType (ModelSelector's enum)
using SvcOperationType = Sprk.Bff.Api.Services.Ai.OperationType;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for EntityResolutionService.
/// Tests node and scope resolution, confidence scoring,
/// and ambiguity handling.
/// </summary>
public class EntityResolutionServiceTests
{
    private readonly Mock<IOpenAiClient> _mockOpenAiClient;
    private readonly Mock<IModelSelector> _mockModelSelector;
    private readonly Mock<IScopeResolverService> _mockScopeResolver;
    private readonly Mock<ILogger<EntityResolutionService>> _mockLogger;
    private readonly EntityResolutionService _service;

    public EntityResolutionServiceTests()
    {
        _mockOpenAiClient = new Mock<IOpenAiClient>();
        _mockModelSelector = new Mock<IModelSelector>();
        _mockScopeResolver = new Mock<IScopeResolverService>();
        _mockLogger = new Mock<ILogger<EntityResolutionService>>();

        _mockModelSelector
            .Setup(x => x.SelectModel(SvcOperationType.EntityResolution))
            .Returns("gpt-4o-mini");

        _service = new EntityResolutionService(
            _mockOpenAiClient.Object,
            _mockModelSelector.Object,
            _mockScopeResolver.Object,
            _mockLogger.Object);
    }

    #region Node Resolution Tests

    [Fact]
    public async Task ResolveNodeAsync_ExactLabelMatch_ReturnsHighConfidence()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "TL;DR",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 3,
                Nodes =
                [
                    new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "TL;DR" },
                    new CanvasNodeSummary { Id = "node_002", Type = "aiAnalysis", Label = "Compliance Check" },
                    new CanvasNodeSummary { Id = "node_003", Type = "deliverOutput", Label = "Output" }
                ]
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.True(result.IsResolved);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("node_001", result.BestMatch.Id);
        Assert.Equal("TL;DR", result.BestMatch.Label);
        Assert.True(result.Confidence >= 0.80);
    }

    [Fact]
    public async Task ResolveNodeAsync_PartialMatch_ReturnsLowerConfidence()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "compliance",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 3,
                Nodes =
                [
                    new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "TL;DR Summary" },
                    new CanvasNodeSummary { Id = "node_002", Type = "aiAnalysis", Label = "Compliance Check" },
                    new CanvasNodeSummary { Id = "node_003", Type = "deliverOutput", Label = "Final Output" }
                ]
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.NotNull(result.BestMatch);
        Assert.Equal("node_002", result.BestMatch.Id);
        Assert.Contains("Compliance", result.BestMatch.Label);
    }

    [Fact]
    public async Task ResolveNodeAsync_SelectedNodeReference_ReturnsSelectedNode()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "this node",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 2,
                SelectedNodeId = "node_002",
                Nodes =
                [
                    new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "Analysis" },
                    new CanvasNodeSummary { Id = "node_002", Type = "deliverOutput", Label = "Output" }
                ]
            },
            SelectedNodeId = "node_002"
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.True(result.IsResolved);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("node_002", result.BestMatch.Id);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public async Task ResolveNodeAsync_NoSelectedNode_ReturnsNoMatch()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "the selected node",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 2,
                SelectedNodeId = null,
                Nodes =
                [
                    new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "Analysis" },
                    new CanvasNodeSummary { Id = "node_002", Type = "deliverOutput", Label = "Output" }
                ]
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(0.0, result.Confidence);
        Assert.Contains("no node", result.Reasoning?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public async Task ResolveNodeAsync_EmptyCanvas_ReturnsNoMatch()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "the analysis node",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 0,
                Nodes = []
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(0.0, result.Confidence);
    }

    [Fact]
    public async Task ResolveNodeAsync_NodeIdMatch_ReturnsHighConfidence()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "node_002",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 2,
                Nodes =
                [
                    new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "First" },
                    new CanvasNodeSummary { Id = "node_002", Type = "deliverOutput", Label = "Second" }
                ]
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.True(result.IsResolved);
        Assert.NotNull(result.BestMatch);
        Assert.Equal("node_002", result.BestMatch.Id);
        Assert.True(result.Confidence >= 0.90);
    }

    [Fact]
    public async Task ResolveNodeAsync_ThrowsOnNullRequest()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveNodeAsync(null!));
    }

    [Fact]
    public async Task ResolveNodeAsync_ThrowsOnEmptyReference()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "",
            CanvasContext = new ClassificationCanvasContext { NodeCount = 0, Nodes = [] }
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ResolveNodeAsync(request));
    }

    #endregion

    #region Scope Resolution Tests

    [Fact]
    public async Task ResolveScopeAsync_ExactNameMatch_ReturnsHighConfidence()
    {
        // Arrange
        SetupScopeResolverMock();

        var request = new ScopeResolutionRequest
        {
            Reference = "Legal Analysis",
            ExpectedCategory = ScopeCategory.Skill
        };

        // Act
        var result = await _service.ResolveScopeAsync(request);

        // Assert
        Assert.NotNull(result.BestMatch);
        Assert.Equal("Legal Analysis", result.BestMatch.Label);
        Assert.True(result.Confidence >= 0.80);
    }

    [Fact]
    public async Task ResolveScopeAsync_PartialMatch_ReturnsCandidate()
    {
        // Arrange
        SetupScopeResolverMock();

        var request = new ScopeResolutionRequest
        {
            Reference = "legal",
            ExpectedCategory = ScopeCategory.Skill
        };

        // Act
        var result = await _service.ResolveScopeAsync(request);

        // Assert
        Assert.NotNull(result.BestMatch);
        Assert.Contains("Legal", result.BestMatch.Label);
    }

    [Fact]
    public async Task ResolveScopeAsync_NoMatchingScopes_ReturnsEmpty()
    {
        // Arrange
        _mockScopeResolver
            .Setup(x => x.ListSkillsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisSkill>
            {
                Items = [],
                TotalCount = 0,
                Page = 1,
                PageSize = 100
            });

        var request = new ScopeResolutionRequest
        {
            Reference = "nonexistent skill",
            ExpectedCategory = ScopeCategory.Skill
        };

        // Act
        var result = await _service.ResolveScopeAsync(request);

        // Assert
        Assert.False(result.IsResolved);
        Assert.Equal(0.0, result.Confidence);
    }

    [Fact]
    public async Task ResolveScopeAsync_SearchAllCategories_SearchesAll()
    {
        // Arrange
        SetupScopeResolverMock();

        var request = new ScopeResolutionRequest
        {
            Reference = "Document Summarizer",
            SearchAllCategories = true
        };

        // Act
        var result = await _service.ResolveScopeAsync(request);

        // Assert
        // Should find the tool "Document Summarizer"
        Assert.NotNull(result.BestMatch);
        Assert.Contains("Summarizer", result.BestMatch.Label);

        // Verify all categories were searched
        _mockScopeResolver.Verify(x =>
            x.ListActionsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockScopeResolver.Verify(x =>
            x.ListSkillsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockScopeResolver.Verify(x =>
            x.ListKnowledgeAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockScopeResolver.Verify(x =>
            x.ListToolsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ResolveScopeAsync_ThrowsOnNullRequest()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _service.ResolveScopeAsync(null!));
    }

    [Fact]
    public async Task ResolveScopeAsync_ThrowsOnEmptyReference()
    {
        // Arrange
        var request = new ScopeResolutionRequest
        {
            Reference = "  ",
            ExpectedCategory = ScopeCategory.Action
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ResolveScopeAsync(request));
    }

    #endregion

    #region Entity Type Tests

    [Fact]
    public async Task ResolveNodeAsync_ReturnsCorrectEntityType()
    {
        // Arrange
        var request = new NodeResolutionRequest
        {
            Reference = "analysis",
            CanvasContext = new ClassificationCanvasContext
            {
                NodeCount = 1,
                Nodes = [new CanvasNodeSummary { Id = "n1", Type = "aiAnalysis", Label = "Analysis" }]
            }
        };

        // Act
        var result = await _service.ResolveNodeAsync(request);

        // Assert
        Assert.Equal(EntityType.Node, result.EntityType);
    }

    [Fact]
    public async Task ResolveScopeAsync_ReturnsCorrectEntityType()
    {
        // Arrange
        SetupScopeResolverMock();

        var request = new ScopeResolutionRequest
        {
            Reference = "Legal Analysis",
            ExpectedCategory = ScopeCategory.Skill
        };

        // Act
        var result = await _service.ResolveScopeAsync(request);

        // Assert
        Assert.Equal(EntityType.Scope, result.EntityType);
        Assert.Equal(ScopeCategory.Skill, result.ScopeCategory);
    }

    #endregion

    #region Helper Methods

    private void SetupScopeResolverMock()
    {
        _mockScopeResolver
            .Setup(x => x.ListActionsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisAction>
            {
                Items =
                [
                    new AnalysisAction
                    {
                        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        Name = "Summarize Document",
                        Description = "Generate a summary"
                    }
                ],
                TotalCount = 1,
                Page = 1,
                PageSize = 100
            });

        _mockScopeResolver
            .Setup(x => x.ListSkillsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisSkill>
            {
                Items =
                [
                    new AnalysisSkill
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                        Name = "Legal Analysis",
                        Category = "Legal"
                    },
                    new AnalysisSkill
                    {
                        Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
                        Name = "Financial Analysis",
                        Category = "Finance"
                    }
                ],
                TotalCount = 2,
                Page = 1,
                PageSize = 100
            });

        _mockScopeResolver
            .Setup(x => x.ListKnowledgeAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisKnowledge>
            {
                Items =
                [
                    new AnalysisKnowledge
                    {
                        Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                        Name = "Standard Terms Reference",
                        Type = KnowledgeType.Inline
                    }
                ],
                TotalCount = 1,
                Page = 1,
                PageSize = 100
            });

        _mockScopeResolver
            .Setup(x => x.ListToolsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisTool>
            {
                Items =
                [
                    new AnalysisTool
                    {
                        Id = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                        Name = "Document Summarizer",
                        Type = ToolType.Summary
                    }
                ],
                TotalCount = 1,
                Page = 1,
                PageSize = 100
            });
    }

    #endregion
}
