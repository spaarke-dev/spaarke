using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ClarificationService.
/// Tests clarification generation for low-confidence intents,
/// ambiguous entities, and scope resolution scenarios.
/// </summary>
public class ClarificationServiceTests
{
    private readonly Mock<ILogger<ClarificationService>> _mockLogger;
    private readonly ClarificationService _service;

    public ClarificationServiceTests()
    {
        _mockLogger = new Mock<ILogger<ClarificationService>>();
        _service = new ClarificationService(_mockLogger.Object);
    }

    #region Intent Clarification Tests

    [Fact]
    public void GenerateIntentClarification_ReturnsQuestionForLowConfidence()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            Intent = BuilderIntentCategory.AddNode,
            Confidence = 0.5,
            Reasoning = "User said 'add something'"
        };

        // Act
        var result = _service.GenerateIntentClarification(classification);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClarificationType.Intent, result.Type);
        // When there are 3+ alternatives, the question uses "Which of these actions"
        Assert.True(
            result.Question.Contains("Did you mean") ||
            result.Question.Contains("Which of these actions"),
            $"Expected clarification question, got: {result.Question}");
        Assert.True(result.AllowFreeText);
    }

    [Fact]
    public void GenerateIntentClarification_IncludesAlternatives()
    {
        // Arrange
        var classification = new IntentClassificationResult
        {
            Intent = BuilderIntentCategory.AddNode,
            Confidence = 0.4,
            Reasoning = "add new"
        };

        // Act
        var result = _service.GenerateIntentClarification(classification);

        // Assert
        Assert.NotEmpty(result.Options);
        Assert.Contains(result.Options, o => o.Id == "AddNode");
    }

    [Fact]
    public void GenerateIntentClarification_ThrowsOnNullClassification()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.GenerateIntentClarification(null!));
    }

    #endregion

    #region Entity Clarification Tests

    [Fact]
    public void GenerateEntityClarification_ReturnsOptionsForMultipleCandidates()
    {
        // Arrange
        var resolution = new EntityResolutionResult
        {
            OriginalReference = "the analysis node",
            EntityType = EntityType.Node,
            Confidence = 0.6,
            CandidateMatches =
            [
                new EntityMatch { Id = "node_001", Label = "TL;DR Analysis", Confidence = 0.6, Type = "aiAnalysis" },
                new EntityMatch { Id = "node_002", Label = "Compliance Analysis", Confidence = 0.5, Type = "aiAnalysis" }
            ]
        };

        // Act
        var result = _service.GenerateEntityClarification(resolution);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClarificationType.Entity, result.Type);
        Assert.Contains("Which node", result.Question);
        Assert.Equal(2, result.Options.Length);
        Assert.Contains(result.Options, o => o.Id == "node_001");
        Assert.Contains(result.Options, o => o.Id == "node_002");
    }

    [Fact]
    public void GenerateEntityClarification_HandlesEmptyCandidates()
    {
        // Arrange
        var resolution = new EntityResolutionResult
        {
            OriginalReference = "nonexistent node",
            EntityType = EntityType.Node,
            Confidence = 0,
            CandidateMatches = []
        };

        // Act
        var result = _service.GenerateEntityClarification(resolution);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("couldn't find", result.Question);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void GenerateEntityClarification_ThrowsOnNullResolution()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.GenerateEntityClarification(null!));
    }

    #endregion

    #region Scope Clarification Tests

    [Fact]
    public void GenerateScopeClarification_IncludesCreateNewOption()
    {
        // Arrange
        var resolution = new EntityResolutionResult
        {
            OriginalReference = "compliance checker",
            EntityType = EntityType.Scope,
            ScopeCategory = ScopeCategory.Skill,
            Confidence = 0.5,
            CandidateMatches =
            [
                new EntityMatch { Id = "skill_001", Label = "Compliance Review", Confidence = 0.5, Type = "skill" }
            ]
        };

        // Act
        var result = _service.GenerateScopeClarification(resolution, "compliance checker");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ClarificationType.Scope, result.Type);
        Assert.Equal(ScopeCategory.Skill, result.ScopeCategory);
        Assert.Contains(result.Options, o => o.Id == ClarificationOptionIds.CreateNew);
        Assert.Contains(result.Options, o => o.Label.Contains("Create new"));
    }

    [Fact]
    public void GenerateScopeClarification_UsesCorrectCategoryName()
    {
        // Arrange - Knowledge scope
        var resolution = new EntityResolutionResult
        {
            OriginalReference = "terms reference",
            EntityType = EntityType.Scope,
            ScopeCategory = ScopeCategory.Knowledge,
            Confidence = 0.3,
            CandidateMatches = []
        };

        // Act
        var result = _service.GenerateScopeClarification(resolution, "terms reference");

        // Assert
        Assert.Contains("knowledge source", result.Question);
    }

    [Fact]
    public void GenerateScopeClarification_ThrowsOnNullResolution()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.GenerateScopeClarification(null!, "test"));
    }

    [Fact]
    public void GenerateScopeClarification_ThrowsOnEmptyReference()
    {
        // Arrange
        var resolution = new EntityResolutionResult
        {
            OriginalReference = "test",
            EntityType = EntityType.Scope,
            Confidence = 0.5
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            _service.GenerateScopeClarification(resolution, ""));
    }

    #endregion

    #region NeedsClarification Tests

    [Theory]
    [InlineData(0.8, null, false)]  // Intent high, no entity - no clarification
    [InlineData(0.7, null, true)]   // Intent below threshold - needs clarification
    [InlineData(0.9, 0.85, false)]  // Both high - no clarification
    [InlineData(0.9, 0.75, true)]   // Entity below threshold - needs clarification
    [InlineData(0.5, 0.5, true)]    // Both low - needs clarification
    public void NeedsClarification_ReturnsCorrectResult(
        double intentConfidence,
        double? entityConfidence,
        bool expectedNeedsClarification)
    {
        // Act
        var result = _service.NeedsClarification(intentConfidence, entityConfidence);

        // Assert
        Assert.Equal(expectedNeedsClarification, result);
    }

    #endregion

    #region FormatForStreaming Tests

    [Fact]
    public void FormatForStreaming_FormatsQuestionOnly()
    {
        // Arrange
        var clarification = new ClarificationRequest
        {
            Type = ClarificationType.Intent,
            Question = "What would you like to do?",
            Options = []
        };

        // Act
        var result = _service.FormatForStreaming(clarification);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(BuilderChunkType.Clarification, result.Type);
        Assert.Equal("What would you like to do?", result.Text);
    }

    [Fact]
    public void FormatForStreaming_FormatsWithOptions()
    {
        // Arrange
        var clarification = new ClarificationRequest
        {
            Type = ClarificationType.Intent,
            Question = "Which action?",
            Options =
            [
                new ClarifyOption { Id = "1", Label = "Add node" },
                new ClarifyOption { Id = "2", Label = "Connect nodes" }
            ]
        };

        // Act
        var result = _service.FormatForStreaming(clarification);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Which action?", result.Text);
        Assert.Contains("Add node", result.Text);
        Assert.Contains("Connect nodes", result.Text);
    }

    [Fact]
    public void FormatForStreaming_ThrowsOnNullClarification()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _service.FormatForStreaming(null!));
    }

    #endregion

    #region GetLikelyAlternatives Tests

    [Fact]
    public void GetLikelyAlternatives_IncludesPrimaryIntent()
    {
        // Act
        var result = _service.GetLikelyAlternatives(BuilderIntentCategory.AddNode, "add something");

        // Assert
        Assert.Contains(result, a => a.Intent == BuilderIntentCategory.AddNode);
    }

    [Fact]
    public void GetLikelyAlternatives_InfersFromAddKeyword()
    {
        // Act
        var result = _service.GetLikelyAlternatives(BuilderIntentCategory.Unclear, "add new node");

        // Assert
        Assert.Contains(result, a => a.Intent == BuilderIntentCategory.AddNode);
    }

    [Fact]
    public void GetLikelyAlternatives_InfersFromConnectKeyword()
    {
        // Act
        var result = _service.GetLikelyAlternatives(BuilderIntentCategory.Unclear, "connect these");

        // Assert
        Assert.Contains(result, a => a.Intent == BuilderIntentCategory.ConnectNodes);
    }

    [Fact]
    public void GetLikelyAlternatives_LimitsToFourOptions()
    {
        // Act
        var result = _service.GetLikelyAlternatives(
            BuilderIntentCategory.Unclear,
            "add create new connect link set configure");

        // Assert
        Assert.True(result.Length <= 4);
    }

    [Fact]
    public void GetLikelyAlternatives_InfersQuestionAsQuery()
    {
        // Act
        var result = _service.GetLikelyAlternatives(BuilderIntentCategory.Unclear, "what is this?");

        // Assert
        Assert.Contains(result, a => a.Intent == BuilderIntentCategory.QueryStatus);
    }

    #endregion
}
