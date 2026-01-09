using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ScopeResolverService - scope resolution service.
/// Tests Phase 1 stub behavior and action lookup.
/// </summary>
public class ScopeResolverServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<ScopeResolverService>> _loggerMock;
    private readonly ScopeResolverService _service;

    // Known stub action IDs from ScopeResolverService
    private static readonly Guid SummarizeActionId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid ReviewAgreementActionId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    public ScopeResolverServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<ScopeResolverService>>();
        _service = new ScopeResolverService(_dataverseServiceMock.Object, _loggerMock.Object);
    }

    #region ResolveScopesAsync Tests

    [Fact]
    public async Task ResolveScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var knowledgeIds = new[] { Guid.NewGuid() };
        var toolIds = new[] { Guid.NewGuid() };

        // Act
        var result = await _service.ResolveScopesAsync(skillIds, knowledgeIds, toolIds, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveScopesAsync_EmptyArrays_ReturnsEmptyScopes()
    {
        // Arrange & Act
        var result = await _service.ResolveScopesAsync([], [], [], CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
    }

    #endregion

    #region ResolvePlaybookScopesAsync Tests

    [Fact]
    public async Task ResolvePlaybookScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var result = await _service.ResolvePlaybookScopesAsync(playbookId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    #endregion

    #region ResolveNodeScopesAsync Tests

    [Fact]
    public async Task ResolveNodeScopesAsync_Phase1_ReturnsEmptyScopes()
    {
        // Arrange
        var nodeId = Guid.NewGuid();

        // Act
        var result = await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Skills.Should().BeEmpty();
        result.Knowledge.Should().BeEmpty();
        result.Tools.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveNodeScopesAsync_AnyNodeId_ReturnsValidResolvedScopes()
    {
        // Arrange - different node IDs should all return valid (empty) scopes
        var nodeIds = new[] { Guid.NewGuid(), Guid.Empty, Guid.Parse("11111111-1111-1111-1111-111111111111") };

        foreach (var nodeId in nodeIds)
        {
            // Act
            var result = await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Skills.Should().NotBeNull();
            result.Knowledge.Should().NotBeNull();
            result.Tools.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ResolveNodeScopesAsync_LogsNodeId()
    {
        // Arrange
        var nodeId = Guid.NewGuid();

        // Act
        await _service.ResolveNodeScopesAsync(nodeId, CancellationToken.None);

        // Assert - Verify logging occurred
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Resolving scopes from node")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region GetActionAsync Tests

    [Fact]
    public async Task GetActionAsync_KnownSummarizeAction_ReturnsStubAction()
    {
        // Act
        var result = await _service.GetActionAsync(SummarizeActionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(SummarizeActionId);
        result.Name.Should().Be("Summarize Document");
        result.SystemPrompt.Should().Contain("summary");
        result.SortOrder.Should().Be(1);
    }

    [Fact]
    public async Task GetActionAsync_KnownReviewAction_ReturnsStubAction()
    {
        // Act
        var result = await _service.GetActionAsync(ReviewAgreementActionId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(ReviewAgreementActionId);
        result.Name.Should().Be("Review Agreement");
        result.SystemPrompt.Should().Contain("legal");
        result.SortOrder.Should().Be(2);
    }

    [Fact]
    public async Task GetActionAsync_UnknownAction_ReturnsDefaultAction()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _service.GetActionAsync(unknownId, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(unknownId);
        result.Name.Should().Be("Default Analysis");
        result.SystemPrompt.Should().Contain("AI assistant");
        result.SortOrder.Should().Be(0);
    }

    [Fact]
    public async Task GetActionAsync_DefaultAction_HasValidSystemPrompt()
    {
        // Arrange
        var unknownId = Guid.NewGuid();

        // Act
        var result = await _service.GetActionAsync(unknownId, CancellationToken.None);

        // Assert
        result!.SystemPrompt.Should().NotBeNullOrEmpty();
        result.SystemPrompt.Should().Contain("thorough");
        result.SystemPrompt.Should().Contain("accurate");
    }

    [Fact]
    public async Task GetActionAsync_StubActions_HaveRequiredFields()
    {
        // Act
        var summarize = await _service.GetActionAsync(SummarizeActionId, CancellationToken.None);
        var review = await _service.GetActionAsync(ReviewAgreementActionId, CancellationToken.None);

        // Assert - All stub actions should have required fields
        summarize!.Id.Should().NotBeEmpty();
        summarize.Name.Should().NotBeNullOrEmpty();
        summarize.SystemPrompt.Should().NotBeNullOrEmpty();
        summarize.Description.Should().NotBeNullOrEmpty();

        review!.Id.Should().NotBeEmpty();
        review.Name.Should().NotBeNullOrEmpty();
        review.SystemPrompt.Should().NotBeNullOrEmpty();
        review.Description.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task ResolveScopesAsync_LogsRequestedCounts()
    {
        // Arrange
        var skillIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        // Act
        await _service.ResolveScopesAsync(skillIds, [], [], CancellationToken.None);

        // Assert - Verify logging occurred (using loose verification)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Resolving scopes")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}

/// <summary>
/// Tests for scope resolution model types.
/// </summary>
public class ScopeModelsTests
{
    [Fact]
    public void ResolvedScopes_CanBeCreated()
    {
        // Arrange & Act
        var scopes = new ResolvedScopes([], [], []);

        // Assert
        scopes.Skills.Should().NotBeNull();
        scopes.Knowledge.Should().NotBeNull();
        scopes.Tools.Should().NotBeNull();
    }

    [Fact]
    public void ResolvedScopes_WithData_ContainsAllItems()
    {
        // Arrange
        var skills = new[] { new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill1", PromptFragment = "Do X" } };
        var knowledge = new[] { new AnalysisKnowledge { Id = Guid.NewGuid(), Name = "K1", Type = KnowledgeType.Inline } };
        var tools = new[] { new AnalysisTool { Id = Guid.NewGuid(), Name = "Tool1", Type = ToolType.EntityExtractor } };

        // Act
        var scopes = new ResolvedScopes(skills, knowledge, tools);

        // Assert
        scopes.Skills.Should().HaveCount(1);
        scopes.Knowledge.Should().HaveCount(1);
        scopes.Tools.Should().HaveCount(1);
    }

    [Fact]
    public void AnalysisAction_RequiredFieldsAreSet()
    {
        // Arrange & Act
        var action = new AnalysisAction
        {
            Id = Guid.NewGuid(),
            Name = "Test Action",
            SystemPrompt = "You are helpful.",
            SortOrder = 5
        };

        // Assert
        action.Id.Should().NotBeEmpty();
        action.Name.Should().Be("Test Action");
        action.SystemPrompt.Should().Be("You are helpful.");
        action.SortOrder.Should().Be(5);
        action.Description.Should().BeNull();
    }

    [Fact]
    public void KnowledgeType_HasExpectedValues()
    {
        // Assert
        ((int)KnowledgeType.Inline).Should().Be(0);
        ((int)KnowledgeType.Document).Should().Be(1);
        ((int)KnowledgeType.RagIndex).Should().Be(2);
    }

    [Fact]
    public void ToolType_HasExpectedValues()
    {
        // Assert
        ((int)ToolType.EntityExtractor).Should().Be(0);
        ((int)ToolType.ClauseAnalyzer).Should().Be(1);
        ((int)ToolType.Custom).Should().Be(2);
    }
}
