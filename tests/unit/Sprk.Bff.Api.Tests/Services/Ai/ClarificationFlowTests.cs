using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Testing;
using Xunit;
using ClarificationRequest = Sprk.Bff.Api.Models.Ai.ClarificationRequest;
using ClarificationType = Sprk.Bff.Api.Models.Ai.ClarificationType;
// Type aliases to resolve namespace conflicts with Models.Ai and Services.Ai types
using OperationType = Sprk.Bff.Api.Models.Ai.OperationType;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for clarification flow in the AI Playbook Builder.
/// Tests the interaction between ProcessMessageAsync, GenerateClarificationAsync,
/// and ReClassifyWithContextAsync.
/// </summary>
public class ClarificationFlowTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IMockTestExecutor> _mockTestExecutorMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<ILogger<AiPlaybookBuilderService>> _loggerMock;
    private readonly AiPlaybookBuilderService _service;

    public ClarificationFlowTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _mockTestExecutorMock = new Mock<IMockTestExecutor>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _loggerMock = new Mock<ILogger<AiPlaybookBuilderService>>();

        _service = new AiPlaybookBuilderService(
            _openAiClientMock.Object,
            _scopeResolverMock.Object,
            _memoryCache,
            _mockTestExecutorMock.Object,
            _playbookServiceMock.Object,
            _loggerMock.Object);
    }

    #region GenerateClarificationAsync Tests

    [Fact]
    public async Task GenerateClarificationAsync_ReturnsQuestion_WhenAIResponseIsValid()
    {
        // Arrange
        var message = "Add a node";
        var canvasContext = new CanvasContext
        {
            NodeCount = 1,
            NodeTypes = ["aiAnalysis"],
            IsSaved = false
        };
        var lowConfidenceResult = new AiIntentResult
        {
            Operation = OperationType.Build,
            Action = IntentAction.AddNode,
            Confidence = 0.6
        };

        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                    "question": "What type of node would you like to add?",
                    "type": "INTENT_DISAMBIGUATION",
                    "options": [
                        { "id": "aiAnalysis", "label": "AI Analysis", "description": "Add an AI analysis node" },
                        { "id": "condition", "label": "Condition", "description": "Add a conditional branch" }
                    ],
                    "suggestions": ["Add an AI analysis node", "Add a condition node"],
                    "understoodContext": "You want to add a node",
                    "ambiguityReason": "The node type was not specified"
                }
                """);

        // Act
        var result = await _service.GenerateClarificationAsync(
            message, canvasContext, lowConfidenceResult, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Text.Should().Be("What type of node would you like to add?");
        result.Type.Should().Be(ClarificationType.IntentDisambiguation);
        result.Options.Should().HaveCount(2);
        result.Suggestions.Should().HaveCount(2);
        result.UnderstoodContext.Should().Be("You want to add a node");
        result.AmbiguityReason.Should().Be("The node type was not specified");
    }

    [Fact]
    public async Task GenerateClarificationAsync_ReturnsFallback_WhenAIFails()
    {
        // Arrange
        var message = "Add a node";
        var lowConfidenceResult = new AiIntentResult
        {
            Operation = OperationType.Build,
            Action = IntentAction.AddNode,
            Confidence = 0.6
        };

        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI service error"));

        // Act
        var result = await _service.GenerateClarificationAsync(
            message, null, lowConfidenceResult, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Text.Should().NotBeNullOrEmpty();
        result.AllowFreeText.Should().BeTrue();
        // Fallback should have node type options for AddNode intent
        result.Options.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateClarificationAsync_IncludesSelectedNodeOption_WhenNodeIsSelected()
    {
        // Arrange
        var message = "Remove this";
        var canvasContext = new CanvasContext
        {
            NodeCount = 3,
            NodeTypes = ["aiAnalysis", "condition"],
            IsSaved = false,
            SelectedNodeId = "node_001"
        };
        var lowConfidenceResult = new AiIntentResult
        {
            Operation = OperationType.Modify,
            Action = IntentAction.RemoveNode,
            Confidence = 0.5
        };

        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("AI service error"));

        // Act
        var result = await _service.GenerateClarificationAsync(
            message, canvasContext, lowConfidenceResult, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Options.Should().Contain(o => o.Id == "selected");
    }

    #endregion

    #region ReClassifyWithContextAsync Tests

    [Fact]
    public async Task ReClassifyWithContextAsync_ReturnsCancelled_WhenUserCancels()
    {
        // Arrange
        var originalMessage = "Add something";
        var clarificationResponse = new ClarificationResponse
        {
            SessionId = "test-session",
            ResponseType = ClarificationResponseType.Cancelled
        };

        // Act
        var result = await _service.ReClassifyWithContextAsync(
            originalMessage, clarificationResponse, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Operation.Should().Be(OperationType.Clarify);
        result.Confidence.Should().Be(1.0);
        result.Reasoning.Should().Contain("cancelled");
    }

    [Fact]
    public async Task ReClassifyWithContextAsync_BoostsConfidence_WhenUserConfirms()
    {
        // Arrange
        var originalMessage = "Remove the node";
        var originalClassification = new AiIntentResult
        {
            Operation = OperationType.Modify,
            Action = IntentAction.RemoveNode,
            Confidence = 0.65,
            Reasoning = "User wants to remove a node"
        };
        var clarificationResponse = new ClarificationResponse
        {
            SessionId = "test-session",
            ResponseType = ClarificationResponseType.Confirmed,
            OriginalClassification = originalClassification
        };

        // Act
        var result = await _service.ReClassifyWithContextAsync(
            originalMessage, clarificationResponse, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Action.Should().Be(IntentAction.RemoveNode);
        result.Confidence.Should().BeGreaterThanOrEqualTo(0.80);
        result.Reasoning.Should().Contain("Confirmed by user");
    }

    [Fact]
    public async Task ReClassifyWithContextAsync_RequestsNewClarification_WhenUserRejects()
    {
        // Arrange
        var originalMessage = "Add something";
        var clarificationResponse = new ClarificationResponse
        {
            SessionId = "test-session",
            ResponseType = ClarificationResponseType.Rejected
        };

        // Act
        var result = await _service.ReClassifyWithContextAsync(
            originalMessage, clarificationResponse, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Operation.Should().Be(OperationType.Clarify);
        result.Action.Should().Be(IntentAction.RequestClarification);
        result.Clarification.Should().NotBeNull();
        result.Clarification!.Question.Should().Contain("instead");
    }

    [Fact]
    public async Task ReClassifyWithContextAsync_CombinesContext_WhenUserProvidesFreeTex()
    {
        // Arrange
        var originalMessage = "Add a node";
        var clarificationResponse = new ClarificationResponse
        {
            SessionId = "test-session",
            ResponseType = ClarificationResponseType.FreeText,
            FreeTextResponse = "I want to add a condition node"
        };

        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                    "operation": "BUILD",
                    "action": "ADD_NODE",
                    "confidence": 0.95,
                    "parameters": {
                        "addNode": {
                            "nodeType": "condition",
                            "label": "Condition"
                        }
                    },
                    "reasoning": "User wants to add a condition node"
                }
                """);

        // Act
        var result = await _service.ReClassifyWithContextAsync(
            originalMessage, clarificationResponse, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Action.Should().Be(IntentAction.AddNode);
        result.Confidence.Should().BeGreaterThan(0.8);
    }

    [Fact]
    public async Task ReClassifyWithContextAsync_SelectsOption_WhenUserChoosesOption()
    {
        // Arrange
        var originalMessage = "Add a node";
        var originalClassification = new AiIntentResult
        {
            Operation = OperationType.Build,
            Action = IntentAction.AddNode,
            Confidence = 0.6,
            Clarification = new ClarificationRequest
            {
                Question = "What type?",
                Type = ClarificationType.Selection,
                Options =
                [
                    new ClarificationOption { Id = "aiAnalysis", Label = "AI Analysis" },
                    new ClarificationOption { Id = "condition", Label = "Condition" }
                ]
            }
        };
        var clarificationResponse = new ClarificationResponse
        {
            SessionId = "test-session",
            ResponseType = ClarificationResponseType.OptionSelected,
            SelectedOptionId = "condition",
            OriginalClassification = originalClassification
        };

        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                    "operation": "BUILD",
                    "action": "ADD_NODE",
                    "confidence": 0.95,
                    "parameters": {
                        "addNode": {
                            "nodeType": "condition",
                            "label": "Condition"
                        }
                    },
                    "reasoning": "User selected condition node"
                }
                """);

        // Act
        var result = await _service.ReClassifyWithContextAsync(
            originalMessage, clarificationResponse, null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Action.Should().Be(IntentAction.AddNode);
        _openAiClientMock.Verify(
            c => c.GetCompletionAsync(It.Is<string>(s => s.Contains("Condition")), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region ClarificationQuestion Tests

    [Fact]
    public void ClarificationQuestion_HasRequiredFields()
    {
        // Arrange & Act
        var question = new ClarificationQuestion
        {
            Id = "q1",
            Text = "What would you like to do?",
            Type = ClarificationType.General
        };

        // Assert
        question.Id.Should().Be("q1");
        question.Text.Should().Be("What would you like to do?");
        question.Type.Should().Be(ClarificationType.General);
        question.AllowFreeText.Should().BeTrue(); // Default
    }

    [Fact]
    public void ClarificationQuestion_SupportsOptions()
    {
        // Arrange & Act
        var question = new ClarificationQuestion
        {
            Id = "q2",
            Text = "Which node type?",
            Type = ClarificationType.Selection,
            Options =
            [
                new ClarificationOption { Id = "opt1", Label = "Option 1" },
                new ClarificationOption { Id = "opt2", Label = "Option 2" }
            ]
        };

        // Assert
        question.Options.Should().HaveCount(2);
        question.Options![0].Id.Should().Be("opt1");
    }

    #endregion

    #region ClarificationResponse Tests

    [Fact]
    public void ClarificationResponse_HandlesOptionSelection()
    {
        // Arrange & Act
        var response = new ClarificationResponse
        {
            SessionId = "session-123",
            ResponseType = ClarificationResponseType.OptionSelected,
            SelectedOptionId = "opt1",
            OriginalMessage = "Add a node"
        };

        // Assert
        response.SessionId.Should().Be("session-123");
        response.ResponseType.Should().Be(ClarificationResponseType.OptionSelected);
        response.SelectedOptionId.Should().Be("opt1");
        response.OriginalMessage.Should().Be("Add a node");
    }

    [Fact]
    public void ClarificationResponse_HandlesFreeText()
    {
        // Arrange & Act
        var response = new ClarificationResponse
        {
            SessionId = "session-123",
            ResponseType = ClarificationResponseType.FreeText,
            FreeTextResponse = "I want a condition node"
        };

        // Assert
        response.ResponseType.Should().Be(ClarificationResponseType.FreeText);
        response.FreeTextResponse.Should().Be("I want a condition node");
    }

    [Theory]
    [InlineData(ClarificationResponseType.Cancelled)]
    [InlineData(ClarificationResponseType.Confirmed)]
    [InlineData(ClarificationResponseType.Rejected)]
    public void ClarificationResponse_SupportsAllResponseTypes(ClarificationResponseType responseType)
    {
        // Arrange & Act
        var response = new ClarificationResponse
        {
            SessionId = "session-123",
            ResponseType = responseType
        };

        // Assert
        response.ResponseType.Should().Be(responseType);
    }

    #endregion

    #region ProcessMessageAsync Clarification Integration Tests

    [Fact]
    public async Task ProcessMessageAsync_StreamsClarification_WhenConfidenceIsLow()
    {
        // Arrange
        var request = new BuilderRequest
        {
            Message = "Add something",
            CanvasState = new CanvasState
            {
                Nodes = [],
                Edges = []
            }
        };

        // Mock low confidence classification
        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                    "operation": "CLARIFY",
                    "action": "REQUEST_CLARIFICATION",
                    "confidence": 0.5,
                    "reasoning": "Unclear what to add"
                }
                """);

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Clarification);
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Complete);
    }

    [Fact]
    public async Task ProcessMessageAsync_ProcessesClarificationResponse_WhenProvided()
    {
        // Arrange
        var originalClassification = new AiIntentResult
        {
            Operation = OperationType.Build,
            Action = IntentAction.AddNode,
            Confidence = 0.6
        };
        var request = new BuilderRequest
        {
            Message = "I want to add a condition node",
            CanvasState = new CanvasState
            {
                Nodes = [],
                Edges = []
            },
            ClarificationResponse = new ClarificationResponse
            {
                SessionId = "session-123",
                ResponseType = ClarificationResponseType.FreeText,
                FreeTextResponse = "I want to add a condition node",
                OriginalMessage = "Add a node",
                OriginalClassification = originalClassification
            }
        };

        // Mock high confidence classification after context
        _openAiClientMock
            .Setup(c => c.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                {
                    "operation": "BUILD",
                    "action": "ADD_NODE",
                    "confidence": 0.95,
                    "parameters": {
                        "addNode": {
                            "nodeType": "condition",
                            "label": "Condition"
                        }
                    },
                    "reasoning": "User wants to add a condition node"
                }
                """);

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        // Should not request another clarification
        chunks.Should().NotContain(c => c.Type == BuilderChunkType.Clarification);
        // Should process the intent
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Message);
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Complete);
    }

    [Fact]
    public async Task ProcessMessageAsync_HandlesCancellation_WhenUserCancels()
    {
        // Arrange
        var request = new BuilderRequest
        {
            Message = "",
            CanvasState = new CanvasState
            {
                Nodes = [],
                Edges = []
            },
            ClarificationResponse = new ClarificationResponse
            {
                SessionId = "session-123",
                ResponseType = ClarificationResponseType.Cancelled
            }
        };

        // Act
        var chunks = new List<BuilderStreamChunk>();
        await foreach (var chunk in _service.ProcessMessageAsync(request, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        // Assert
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Message && c.Text!.Contains("Let me know"));
        chunks.Should().Contain(c => c.Type == BuilderChunkType.Complete);
    }

    #endregion
}
