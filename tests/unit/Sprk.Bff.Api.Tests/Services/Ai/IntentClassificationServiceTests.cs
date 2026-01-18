using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for IntentClassificationService.
/// Tests intent classification for all 11 categories, confidence scoring,
/// entity extraction, and fallback behavior.
/// </summary>
public class IntentClassificationServiceTests
{
    private readonly Mock<IOpenAiClient> _mockOpenAiClient;
    private readonly Mock<ILogger<IntentClassificationService>> _mockLogger;
    private readonly IntentClassificationService _service;

    public IntentClassificationServiceTests()
    {
        _mockOpenAiClient = new Mock<IOpenAiClient>();
        _mockLogger = new Mock<ILogger<IntentClassificationService>>();
        _service = new IntentClassificationService(
            _mockOpenAiClient.Object,
            _mockLogger.Object);
    }

    #region Intent Classification Tests

    [Theory]
    [InlineData("CREATE_PLAYBOOK", BuilderIntentCategory.CreatePlaybook)]
    [InlineData("ADD_NODE", BuilderIntentCategory.AddNode)]
    [InlineData("REMOVE_NODE", BuilderIntentCategory.RemoveNode)]
    [InlineData("CONNECT_NODES", BuilderIntentCategory.ConnectNodes)]
    [InlineData("CONFIGURE_NODE", BuilderIntentCategory.ConfigureNode)]
    [InlineData("LINK_SCOPE", BuilderIntentCategory.LinkScope)]
    [InlineData("CREATE_SCOPE", BuilderIntentCategory.CreateScope)]
    [InlineData("QUERY_STATUS", BuilderIntentCategory.QueryStatus)]
    [InlineData("MODIFY_LAYOUT", BuilderIntentCategory.ModifyLayout)]
    [InlineData("UNDO", BuilderIntentCategory.Undo)]
    [InlineData("UNCLEAR", BuilderIntentCategory.Unclear)]
    public async Task ClassifyAsync_ReturnsCorrectIntent_ForAllCategories(
        string intentString,
        BuilderIntentCategory expectedIntent)
    {
        // Arrange
        var aiResponse = $$"""
            {
                "intent": "{{intentString}}",
                "confidence": 0.92,
                "entities": null,
                "needsClarification": false,
                "reasoning": "Test classification"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync("test message", null);

        // Assert
        Assert.Equal(expectedIntent, result.Intent);
        Assert.Equal(0.92, result.Confidence);
        Assert.False(result.NeedsClarification);
    }

    [Fact]
    public async Task ClassifyAsync_ExtractsNodeEntities()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "ADD_NODE",
                "confidence": 0.95,
                "entities": {
                    "nodeType": "aiAnalysis",
                    "nodeLabel": "Compliance Check",
                    "position": { "x": 300, "y": 200 }
                },
                "needsClarification": false,
                "reasoning": "User wants to add an AI analysis node"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync(
            "Add a compliance check node", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.AddNode, result.Intent);
        Assert.NotNull(result.Entities);
        Assert.Equal("aiAnalysis", result.Entities.NodeType);
        Assert.Equal("Compliance Check", result.Entities.NodeLabel);
        Assert.NotNull(result.Entities.Position);
        Assert.Equal(300, result.Entities.Position.X);
        Assert.Equal(200, result.Entities.Position.Y);
    }

    [Fact]
    public async Task ClassifyAsync_ExtractsConnectionEntities()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "CONNECT_NODES",
                "confidence": 0.88,
                "entities": {
                    "sourceNode": "node_001",
                    "targetNode": "node_002"
                },
                "needsClarification": false,
                "reasoning": "User wants to connect two nodes"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync(
            "Connect the TL;DR to compliance", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.ConnectNodes, result.Intent);
        Assert.NotNull(result.Entities);
        Assert.Equal("node_001", result.Entities.SourceNode);
        Assert.Equal("node_002", result.Entities.TargetNode);
    }

    [Fact]
    public async Task ClassifyAsync_ExtractsScopeEntities()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "LINK_SCOPE",
                "confidence": 0.90,
                "entities": {
                    "scopeType": "action",
                    "scopeName": "Standard Compliance Check",
                    "nodeId": "node_003"
                },
                "needsClarification": false,
                "reasoning": "User wants to link an action scope to a node"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync(
            "Use the standard compliance action", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.LinkScope, result.Intent);
        Assert.NotNull(result.Entities);
        Assert.Equal("action", result.Entities.ScopeType);
        Assert.Equal("Standard Compliance Check", result.Entities.ScopeName);
        Assert.Equal("node_003", result.Entities.NodeId);
    }

    #endregion

    #region Confidence and Clarification Tests

    [Fact]
    public async Task ClassifyAsync_TriggersClarification_WhenConfidenceBelowThreshold()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "ADD_NODE",
                "confidence": 0.60,
                "entities": null,
                "needsClarification": true,
                "clarificationQuestion": "Which type of node would you like to add?",
                "reasoning": "Ambiguous request"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync("add a node", null);

        // Assert
        Assert.True(result.NeedsClarification);
        Assert.Equal("Which type of node would you like to add?", result.ClarificationQuestion);
    }

    [Fact]
    public async Task ClassifyAsync_ProvidesClarificationOptions_WhenMultipleMatches()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "REMOVE_NODE",
                "confidence": 0.55,
                "entities": {},
                "needsClarification": true,
                "clarificationQuestion": "Which analysis node did you mean?",
                "clarificationOptions": [
                    { "id": "node_001", "label": "Compliance Analysis" },
                    { "id": "node_002", "label": "Risk Analysis" },
                    { "id": "node_003", "label": "Financial Analysis" }
                ],
                "reasoning": "Multiple nodes match 'analysis'"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync(
            "remove the analysis node", null);

        // Assert
        Assert.True(result.NeedsClarification);
        Assert.NotNull(result.ClarificationOptions);
        Assert.Equal(3, result.ClarificationOptions.Length);
        Assert.Equal("Compliance Analysis", result.ClarificationOptions[0].Label);
    }

    [Fact]
    public async Task ClassifyAsync_ForcesClarification_WhenConfidenceBelow075()
    {
        // Arrange - AI says no clarification needed, but confidence is 0.70
        var aiResponse = """
            {
                "intent": "ADD_NODE",
                "confidence": 0.70,
                "entities": { "nodeType": "action" },
                "needsClarification": false,
                "reasoning": "Likely wants to add a node"
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync("add something", null);

        // Assert
        // Should force clarification even though AI said no, because confidence < 0.75
        Assert.True(result.NeedsClarification);
        Assert.NotNull(result.ClarificationQuestion);
    }

    #endregion

    #region Canvas Context Tests

    [Fact]
    public async Task ClassifyAsync_IncludesCanvasContext_InPrompt()
    {
        // Arrange
        string capturedPrompt = "";
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) =>
                capturedPrompt = prompt)
            .ReturnsAsync("""{"intent": "ADD_NODE", "confidence": 0.9}""");

        var canvasContext = new ClassificationCanvasContext
        {
            NodeCount = 3,
            EdgeCount = 2,
            IsSaved = true,
            SelectedNodeId = "node_002",
            Nodes =
            [
                new CanvasNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "TL;DR" },
                new CanvasNodeSummary { Id = "node_002", Type = "aiAnalysis", Label = "Compliance" },
                new CanvasNodeSummary { Id = "node_003", Type = "deliverOutput", Label = "Output" }
            ]
        };

        // Act
        await _service.ClassifyAsync("add a node", canvasContext);

        // Assert
        Assert.Contains("3 nodes", capturedPrompt);
        Assert.Contains("2 edges", capturedPrompt);
        Assert.Contains("Saved", capturedPrompt);
        Assert.Contains("TL;DR", capturedPrompt);
        Assert.Contains("Compliance", capturedPrompt);
    }

    [Fact]
    public async Task ClassifyAsync_HandlesEmptyCanvas()
    {
        // Arrange
        string capturedPrompt = "";
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) =>
                capturedPrompt = prompt)
            .ReturnsAsync("""{"intent": "CREATE_PLAYBOOK", "confidence": 0.95}""");

        // Act
        await _service.ClassifyAsync("build a lease analysis playbook", null);

        // Assert
        Assert.Contains("Canvas is empty", capturedPrompt);
        Assert.Contains("No nodes", capturedPrompt);
    }

    #endregion

    #region Fallback and Error Handling Tests

    [Fact]
    public async Task ClassifyAsync_UsesFallback_WhenJsonParsingFails()
    {
        // Arrange
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON");

        // Act
        var result = await _service.ClassifyAsync(
            "create a new playbook for contracts", null);

        // Assert
        // Should fall back to rule-based classification
        Assert.Equal(BuilderIntentCategory.CreatePlaybook, result.Intent);
        Assert.Contains("Fallback", result.Reasoning);
    }

    [Fact]
    public async Task ClassifyAsync_FallbackDetectsAddNode()
    {
        // Arrange
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid response");

        // Act
        var result = await _service.ClassifyAsync("add a node please", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.AddNode, result.Intent);
    }

    [Fact]
    public async Task ClassifyAsync_FallbackDetectsUndo()
    {
        // Arrange
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("oops");

        // Act
        var result = await _service.ClassifyAsync("undo that", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.Undo, result.Intent);
    }

    [Fact]
    public async Task ClassifyAsync_HandlesMarkdownCodeBlocks()
    {
        // Arrange
        var aiResponse = """
            ```json
            {
                "intent": "ADD_NODE",
                "confidence": 0.90,
                "entities": null,
                "needsClarification": false
            }
            ```
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync("add a node", null);

        // Assert
        Assert.Equal(BuilderIntentCategory.AddNode, result.Intent);
        Assert.Equal(0.90, result.Confidence);
    }

    [Fact]
    public async Task ClassifyAsync_ThrowsOnEmptyMessage()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.ClassifyAsync("", null));
    }

    [Fact]
    public async Task ClassifyAsync_ThrowsOnNullMessage()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.ClassifyAsync(null!, null));
    }

    #endregion

    #region Entity Dictionary Mapping Tests

    [Fact]
    public async Task ClassifyAsync_MapsEntitiesToDictionary()
    {
        // Arrange
        var aiResponse = """
            {
                "intent": "CONFIGURE_NODE",
                "confidence": 0.85,
                "entities": {
                    "nodeId": "node_001",
                    "configKey": "outputVariable",
                    "configValue": "complianceResult"
                },
                "needsClarification": false
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _service.ClassifyAsync(
            "set the output variable to complianceResult", null);

        // Assert
        Assert.NotNull(result.EntityDictionary);
        Assert.Equal("node_001", result.EntityDictionary["nodeId"]);
        Assert.Equal("outputVariable", result.EntityDictionary["configKey"]);
        Assert.Equal("complianceResult", result.EntityDictionary["configValue"]);
    }

    #endregion

    #region Intent Description Tests

    [Theory]
    [InlineData(BuilderIntentCategory.CreatePlaybook, "create a new playbook")]
    [InlineData(BuilderIntentCategory.AddNode, "add a node")]
    [InlineData(BuilderIntentCategory.RemoveNode, "remove a node")]
    [InlineData(BuilderIntentCategory.ConnectNodes, "connect nodes")]
    [InlineData(BuilderIntentCategory.Undo, "undo the last action")]
    public void IntentClassificationResult_ReturnsCorrectDescription(
        BuilderIntentCategory intent,
        string expectedDescription)
    {
        // Arrange
        var result = new IntentClassificationResult
        {
            Intent = intent,
            Confidence = 1.0
        };

        // Act & Assert
        Assert.Equal(expectedDescription, result.IntentDescription);
    }

    #endregion
}
