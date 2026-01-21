using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

// Explicit alias for Services.Ai.OperationType (ModelSelector's enum)
using SvcOperationType = Sprk.Bff.Api.Services.Ai.OperationType;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for BuildPlanGenerationService.
/// Tests build plan generation for CREATE_PLAYBOOK intent,
/// validation, and error handling.
/// </summary>
public class BuildPlanGenerationServiceTests
{
    private readonly Mock<IOpenAiClient> _mockOpenAiClient;
    private readonly Mock<IModelSelector> _mockModelSelector;
    private readonly Mock<ILogger<BuildPlanGenerationService>> _mockLogger;
    private readonly BuildPlanGenerationService _service;

    public BuildPlanGenerationServiceTests()
    {
        _mockOpenAiClient = new Mock<IOpenAiClient>();
        _mockModelSelector = new Mock<IModelSelector>();
        _mockLogger = new Mock<ILogger<BuildPlanGenerationService>>();

        // Default model selection returns o1-mini for plan generation
        _mockModelSelector
            .Setup(x => x.SelectModel(SvcOperationType.PlanGeneration))
            .Returns("o1-mini");

        _service = new BuildPlanGenerationService(
            _mockOpenAiClient.Object,
            _mockModelSelector.Object,
            _mockLogger.Object);
    }

    #region Successful Generation Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_ReturnsSuccessfulResult_WithValidPlan()
    {
        // Arrange
        var aiResponse = CreateValidLeaseAnalysisPlanResponse();

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a lease analysis playbook",
            DocumentTypes = ["LEASE"]
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Plan);
        Assert.Equal("Real Estate Lease Analysis", result.Plan.Summary);
        Assert.Equal(5, result.Plan.Steps.Length);
        Assert.Equal(3, result.Plan.EstimatedNodeCount);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_UsesCorrectModel_ForPlanGeneration()
    {
        // Arrange
        var aiResponse = CreateValidLeaseAnalysisPlanResponse();
        string? capturedModel = null;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, model, _) =>
                capturedModel = model)
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a lease analysis playbook"
        };

        // Act
        await _service.GenerateBuildPlanAsync(context);

        // Assert
        _mockModelSelector.Verify(x => x.SelectModel(SvcOperationType.PlanGeneration), Times.Once);
        Assert.Equal("o1-mini", capturedModel);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_IncludesGoal_InPrompt()
    {
        // Arrange
        var goal = "Create a contract review playbook for NDAs";
        string capturedPrompt = "";

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) =>
                capturedPrompt = prompt)
            .ReturnsAsync(CreateValidLeaseAnalysisPlanResponse());

        var context = new BuildPlanGenerationContext
        {
            Goal = goal
        };

        // Act
        await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.Contains(goal, capturedPrompt);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_IncludesDocumentTypes_InPrompt()
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
            .ReturnsAsync(CreateValidLeaseAnalysisPlanResponse());

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook",
            DocumentTypes = ["LEASE", "CONTRACT"]
        };

        // Act
        await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.Contains("LEASE", capturedPrompt);
        Assert.Contains("CONTRACT", capturedPrompt);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_IncludesAvailableScopes_InPrompt()
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
            .ReturnsAsync(CreateValidLeaseAnalysisPlanResponse());

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook",
            AvailableScopes = new AvailableScopes
            {
                Actions =
                [
                    new AvailableScope { Id = "act-1", Name = "TL;DR Summary", Description = "Create a brief summary" }
                ],
                Skills =
                [
                    new AvailableScope { Id = "skl-1", Name = "Legal Analysis" }
                ]
            }
        };

        // Act
        await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.Contains("TL;DR Summary", capturedPrompt);
        Assert.Contains("Legal Analysis", capturedPrompt);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_IncludesExistingCanvas_InPrompt()
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
            .ReturnsAsync(CreateValidLeaseAnalysisPlanResponse());

        var context = new BuildPlanGenerationContext
        {
            Goal = "Extend this playbook",
            CurrentCanvas = new CanvasStateSummary
            {
                NodeCount = 2,
                EdgeCount = 1,
                Description = "Basic playbook with TL;DR",
                ExistingNodes =
                [
                    new ExistingNodeSummary { Id = "node_001", Type = "aiAnalysis", Label = "TL;DR" }
                ]
            }
        };

        // Act
        await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.Contains("2 nodes", capturedPrompt);
        Assert.Contains("1 edges", capturedPrompt);
        Assert.Contains("TL;DR", capturedPrompt);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_HandlesMarkdownCodeBlocks()
    {
        // Arrange
        var aiResponse = $"""
            ```json
            {CreateValidLeaseAnalysisPlanResponse()}
            ```
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Plan);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_ValidatesStepActions()
    {
        // Arrange - invalid action type
        var aiResponse = """
            {
                "summary": "Test plan",
                "estimatedNodeCount": 1,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "invalidAction",
                        "description": "Invalid step"
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unknown action", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ValidatesAddNodeRequiresNodeSpec()
    {
        // Arrange - addNode without nodeSpec
        var aiResponse = """
            {
                "summary": "Test plan",
                "estimatedNodeCount": 1,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add a node without spec"
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("addNode requires nodeSpec", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ValidatesCreateEdgeRequiresEdgeSpec()
    {
        // Arrange - createEdge without edgeSpec
        var aiResponse = """
            {
                "summary": "Test plan",
                "estimatedNodeCount": 1,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Test" }
                    },
                    {
                        "order": 2,
                        "action": "createEdge",
                        "description": "Create edge without spec"
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("createEdge requires edgeSpec", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ValidatesLinkScopeRequiresScopeReference()
    {
        // Arrange - linkScope without scopeReference
        var aiResponse = """
            {
                "summary": "Test plan",
                "estimatedNodeCount": 1,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Test" }
                    },
                    {
                        "order": 2,
                        "action": "linkScope",
                        "description": "Link scope without reference"
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("linkScope requires scopeReference", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_DetectsCircularDependencies()
    {
        // Arrange - step 1 depends on step 2
        var aiResponse = """
            {
                "summary": "Test plan",
                "estimatedNodeCount": 2,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "First" },
                        "dependsOn": ["step_2"]
                    },
                    {
                        "order": 2,
                        "action": "addNode",
                        "description": "Add second node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Second" }
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("circular or forward dependency", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ReturnsWarnings_ForMissingDeliverOutput()
    {
        // Arrange - plan without deliverOutput at end
        var aiResponse = """
            {
                "summary": "Analysis plan",
                "estimatedNodeCount": 2,
                "confidence": 0.85,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add analysis",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Analysis" }
                    },
                    {
                        "order": 2,
                        "action": "addNode",
                        "description": "Add more analysis",
                        "nodeSpec": { "type": "aiAnalysis", "label": "More Analysis" }
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("deliverOutput"));
    }

    #endregion

    #region Confirmation Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_RequiresConfirmation_WhenConfidenceLow()
    {
        // Arrange - low confidence plan
        var aiResponse = """
            {
                "summary": "Maybe a lease analysis",
                "estimatedNodeCount": 3,
                "confidence": 0.65,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Analysis" }
                    },
                    {
                        "order": 2,
                        "action": "addNode",
                        "description": "Add output",
                        "nodeSpec": { "type": "deliverOutput", "label": "Output" }
                    },
                    {
                        "order": 3,
                        "action": "addNode",
                        "description": "Extra node",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Extra" }
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build something"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RequiresConfirmation);
        Assert.NotNull(result.ConfirmationMessage);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_RequiresConfirmation_WhenManyNodes()
    {
        // Arrange - plan with more than 10 nodes
        var steps = Enumerable.Range(1, 12).Select(i =>
            @$"{{ ""order"": {i}, ""action"": ""addNode"", ""description"": ""Add node {i}"", ""nodeSpec"": {{ ""type"": ""aiAnalysis"", ""label"": ""Node {i}"" }} }}");

        var aiResponse = $@"{{
            ""summary"": ""Large plan"",
            ""estimatedNodeCount"": 12,
            ""confidence"": 0.90,
            ""steps"": [{string.Join(",", steps)}]
        }}";

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a complex playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.RequiresConfirmation);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_NoConfirmation_WhenHighConfidenceAndSmallPlan()
    {
        // Arrange - high confidence, few nodes
        var aiResponse = """
            {
                "summary": "Simple analysis",
                "estimatedNodeCount": 2,
                "confidence": 0.92,
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Add analysis",
                        "nodeSpec": { "type": "aiAnalysis", "label": "Analysis" }
                    },
                    {
                        "order": 2,
                        "action": "addNode",
                        "description": "Add output",
                        "nodeSpec": { "type": "deliverOutput", "label": "Output" }
                    }
                ]
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a simple playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.RequiresConfirmation);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_ReturnsFailed_WhenJsonInvalid()
    {
        // Arrange
        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON");

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("parse", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ReturnsFailed_WhenNoSteps()
    {
        // Arrange
        var aiResponse = """
            {
                "summary": "Empty plan",
                "estimatedNodeCount": 0,
                "confidence": 0.5,
                "steps": []
            }
            """;

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a playbook"
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("at least one", result.Error);
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ThrowsOnNullContext()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.GenerateBuildPlanAsync(null!));
    }

    [Fact]
    public async Task GenerateBuildPlanAsync_ThrowsOnEmptyGoal()
    {
        // Arrange
        var context = new BuildPlanGenerationContext
        {
            Goal = ""
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.GenerateBuildPlanAsync(context));
    }

    #endregion

    #region Model Constants Tests

    [Theory]
    [InlineData("addNode", true)]
    [InlineData("removeNode", true)]
    [InlineData("createEdge", true)]
    [InlineData("removeEdge", true)]
    [InlineData("linkScope", true)]
    [InlineData("createScope", true)]
    [InlineData("updateConfig", true)]
    [InlineData("autoLayout", true)]
    [InlineData("invalidAction", false)]
    [InlineData("", false)]
    public void ExecutionStepActions_IsValidAction_ReturnsCorrectResult(
        string action, bool expectedValid)
    {
        // Act
        var result = ExecutionStepActions.IsValidAction(action);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    [Theory]
    [InlineData("aiAnalysis", true)]
    [InlineData("aiCompletion", true)]
    [InlineData("condition", true)]
    [InlineData("deliverOutput", true)]
    [InlineData("createTask", true)]
    [InlineData("sendEmail", true)]
    [InlineData("wait", true)]
    [InlineData("invalidType", false)]
    [InlineData("", false)]
    public void PlaybookNodeTypes_IsValidNodeType_ReturnsCorrectResult(
        string nodeType, bool expectedValid)
    {
        // Act
        var result = PlaybookNodeTypes.IsValidNodeType(nodeType);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    [Theory]
    [InlineData("action", true)]
    [InlineData("skill", true)]
    [InlineData("knowledge", true)]
    [InlineData("tool", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public void ScopeTypes_IsValidScopeType_ReturnsCorrectResult(
        string scopeType, bool expectedValid)
    {
        // Act
        var result = ScopeTypes.IsValidScopeType(scopeType);

        // Assert
        Assert.Equal(expectedValid, result);
    }

    #endregion

    #region Build Plan Model Tests

    [Fact]
    public void BuildPlan_GeneratesUniqueId()
    {
        // Act
        var plan1 = new BuildPlan { Summary = "Plan 1", Steps = [] };
        var plan2 = new BuildPlan { Summary = "Plan 2", Steps = [] };

        // Assert
        Assert.NotEqual(plan1.Id, plan2.Id);
        Assert.NotEmpty(plan1.Id);
    }

    [Fact]
    public void BuildPlan_SetsGeneratedAtTimestamp()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow;

        // Act
        var plan = new BuildPlan { Summary = "Test", Steps = [] };

        // Assert
        Assert.True(plan.GeneratedAt >= before);
        Assert.True(plan.GeneratedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void BuildPlanGenerationResult_SuccessfulCreation()
    {
        // Arrange
        var plan = new BuildPlan { Summary = "Test", Steps = [] };

        // Act
        var result = BuildPlanGenerationResult.Successful(
            plan,
            requiresConfirmation: true,
            confirmationMessage: "Proceed?",
            warnings: ["Warning 1"]);

        // Assert
        Assert.True(result.Success);
        Assert.Same(plan, result.Plan);
        Assert.True(result.RequiresConfirmation);
        Assert.Equal("Proceed?", result.ConfirmationMessage);
        Assert.Single(result.Warnings!);
    }

    [Fact]
    public void BuildPlanGenerationResult_FailedCreation()
    {
        // Act
        var result = BuildPlanGenerationResult.Failed("Something went wrong");

        // Assert
        Assert.False(result.Success);
        Assert.Null(result.Plan);
        Assert.Equal("Something went wrong", result.Error);
    }

    #endregion

    #region Lease Analysis Scenario Tests

    [Fact]
    public async Task GenerateBuildPlanAsync_ProducesValidPlan_ForLeaseAnalysis()
    {
        // Arrange
        var aiResponse = CreateValidLeaseAnalysisPlanResponse();

        _mockOpenAiClient
            .Setup(x => x.GetCompletionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        var context = new BuildPlanGenerationContext
        {
            Goal = "Build a lease analysis playbook that extracts key terms, checks compliance, and assesses risk",
            DocumentTypes = ["LEASE", "SUBLEASE"],
            MatterTypes = ["REAL_ESTATE"]
        };

        // Act
        var result = await _service.GenerateBuildPlanAsync(context);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Plan);

        // Verify plan structure
        var plan = result.Plan;
        Assert.Equal("Real Estate Lease Analysis", plan.Summary);
        Assert.Equal("lease-analysis", plan.Pattern);
        Assert.Contains("LEASE", plan.DocumentTypes ?? []);

        // Verify step structure
        var addNodeSteps = plan.Steps.Where(s => s.Action == ExecutionStepActions.AddNode).ToList();
        Assert.Equal(3, addNodeSteps.Count);

        // Verify first node has proper spec
        var firstNode = addNodeSteps.First().NodeSpec;
        Assert.NotNull(firstNode);
        Assert.Equal("aiAnalysis", firstNode.Type);
        Assert.Equal("TL;DR Summary", firstNode.Label);
        Assert.NotNull(firstNode.Position);

        // Verify edges exist
        var edgeSteps = plan.Steps.Where(s => s.Action == ExecutionStepActions.CreateEdge).ToList();
        Assert.Equal(2, edgeSteps.Count);

        // Verify scope requirements
        Assert.NotNull(plan.ScopeRequirements);
        Assert.NotNull(plan.ScopeRequirements.Actions);
        Assert.NotEmpty(plan.ScopeRequirements.Actions);
    }

    #endregion

    #region Helper Methods

    private static string CreateValidLeaseAnalysisPlanResponse()
    {
        return """
            {
                "summary": "Real Estate Lease Analysis",
                "description": "Comprehensive lease analysis playbook for extracting terms and assessing compliance",
                "documentTypes": ["LEASE", "SUBLEASE"],
                "matterTypes": ["REAL_ESTATE"],
                "estimatedNodeCount": 3,
                "confidence": 0.88,
                "pattern": "lease-analysis",
                "reasoning": "Standard lease analysis pattern with TL;DR, compliance, and delivery",
                "steps": [
                    {
                        "order": 1,
                        "action": "addNode",
                        "description": "Create TL;DR summary node",
                        "nodeSpec": {
                            "type": "aiAnalysis",
                            "label": "TL;DR Summary",
                            "position": { "x": 200, "y": 100 },
                            "configuration": {
                                "outputVariable": "tldrSummary"
                            }
                        },
                        "dependsOn": []
                    },
                    {
                        "order": 2,
                        "action": "addNode",
                        "description": "Create compliance check node",
                        "nodeSpec": {
                            "type": "aiAnalysis",
                            "label": "Compliance Check",
                            "position": { "x": 200, "y": 300 },
                            "configuration": {
                                "outputVariable": "complianceResult"
                            }
                        },
                        "dependsOn": ["step_1"]
                    },
                    {
                        "order": 3,
                        "action": "createEdge",
                        "description": "Connect TL;DR to Compliance",
                        "edgeSpec": {
                            "sourceRef": "step_1",
                            "targetRef": "step_2"
                        },
                        "dependsOn": ["step_1", "step_2"]
                    },
                    {
                        "order": 4,
                        "action": "addNode",
                        "description": "Create delivery output node",
                        "nodeSpec": {
                            "type": "deliverOutput",
                            "label": "Deliver Report",
                            "position": { "x": 200, "y": 500 }
                        },
                        "dependsOn": ["step_2"]
                    },
                    {
                        "order": 5,
                        "action": "createEdge",
                        "description": "Connect Compliance to Delivery",
                        "edgeSpec": {
                            "sourceRef": "step_2",
                            "targetRef": "step_4"
                        },
                        "dependsOn": ["step_2", "step_4"]
                    }
                ],
                "scopeRequirements": {
                    "actions": [
                        { "name": "TL;DR Summary", "exists": true, "reason": "Standard summary action" },
                        { "name": "Compliance Check", "exists": true, "reason": "Standard compliance action" }
                    ],
                    "skills": [
                        { "name": "Legal Analysis", "exists": true, "reason": "General legal analysis skill" }
                    ],
                    "knowledge": [],
                    "tools": []
                }
            }
            """;
    }

    #endregion
}
