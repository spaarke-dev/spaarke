using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ScopeManagementService - scope CRUD operations.
/// Tests ownership validation and duplicate name handling.
/// </summary>
public class ScopeManagementServiceTests
{
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<ILogger<ScopeManagementService>> _loggerMock;
    private readonly ScopeManagementService _service;

    public ScopeManagementServiceTests()
    {
        _dataverseServiceMock = new Mock<IDataverseService>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _loggerMock = new Mock<ILogger<ScopeManagementService>>();

        // Setup default empty list responses
        _scopeResolverMock.Setup(s => s.ListActionsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisAction> { Items = [], TotalCount = 0, Page = 1, PageSize = 1000 });
        _scopeResolverMock.Setup(s => s.ListSkillsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisSkill> { Items = [], TotalCount = 0, Page = 1, PageSize = 1000 });
        _scopeResolverMock.Setup(s => s.ListKnowledgeAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisKnowledge> { Items = [], TotalCount = 0, Page = 1, PageSize = 1000 });
        _scopeResolverMock.Setup(s => s.ListToolsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisTool> { Items = [], TotalCount = 0, Page = 1, PageSize = 1000 });

        _service = new ScopeManagementService(
            _dataverseServiceMock.Object,
            _scopeResolverMock.Object,
            _loggerMock.Object);
    }

    #region IsSystemScope Tests

    [Theory]
    [InlineData("SYS-Document-Analysis", true)]
    [InlineData("SYS-BUILDER-001", true)]
    [InlineData("sys-lowercase", true)]
    [InlineData("CUST-My-Action", false)]
    [InlineData("My Custom Action", false)]
    [InlineData("Custom-SYS-Like", false)]
    public void IsSystemScope_ReturnsCorrectResult(string name, bool expected)
    {
        // Act
        var result = _service.IsSystemScope(name);

        // Assert
        result.Should().Be(expected);
    }

    #endregion

    #region Ownership Validation Tests

    [Fact]
    public async Task UpdateActionAsync_SystemScope_ThrowsScopeOwnershipException()
    {
        // Arrange
        var systemActionId = Guid.NewGuid();
        var systemAction = new AnalysisAction
        {
            Id = systemActionId,
            Name = "SYS-Document-Analysis",
            SystemPrompt = "System prompt"
        };

        _scopeResolverMock.Setup(s => s.GetActionAsync(systemActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemAction);

        var request = new UpdateActionRequest { Description = "New description" };

        // Act & Assert
        var act = () => _service.UpdateActionAsync(systemActionId, request, CancellationToken.None);
        await act.Should().ThrowAsync<ScopeOwnershipException>()
            .WithMessage("*SYS-Document-Analysis*")
            .WithMessage("*update*");
    }

    [Fact]
    public async Task DeleteActionAsync_SystemScope_ThrowsScopeOwnershipException()
    {
        // Arrange
        var systemActionId = Guid.NewGuid();
        var systemAction = new AnalysisAction
        {
            Id = systemActionId,
            Name = "SYS-Builder-Intent",
            SystemPrompt = "System prompt"
        };

        _scopeResolverMock.Setup(s => s.GetActionAsync(systemActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(systemAction);

        // Act & Assert
        var act = () => _service.DeleteActionAsync(systemActionId, CancellationToken.None);
        await act.Should().ThrowAsync<ScopeOwnershipException>()
            .WithMessage("*delete*");
    }

    [Fact]
    public async Task UpdateSkillAsync_SystemScope_ThrowsScopeOwnershipException()
    {
        // Arrange
        var systemSkillId = Guid.NewGuid();
        var systemSkill = new AnalysisSkill
        {
            Id = systemSkillId,
            Name = "SYS-Legal-Analysis",
            PromptFragment = "Skill prompt"
        };

        _scopeResolverMock.Setup(s => s.ListSkillsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisSkill>
            {
                Items = [systemSkill],
                TotalCount = 1,
                Page = 1,
                PageSize = 1000
            });

        var request = new UpdateSkillRequest { Description = "New description" };

        // Act & Assert
        var act = () => _service.UpdateSkillAsync(systemSkillId, request, CancellationToken.None);
        await act.Should().ThrowAsync<ScopeOwnershipException>();
    }

    #endregion

    #region Customer Prefix Tests

    [Fact]
    public async Task CreateActionAsync_AddsCustomerPrefix()
    {
        // Arrange
        var request = new CreateActionRequest
        {
            Name = "My Custom Action",
            SystemPrompt = "Do something"
        };

        // Act
        var result = await _service.CreateActionAsync(request, CancellationToken.None);

        // Assert
        result.Name.Should().StartWith("CUST-");
        result.Name.Should().Be("CUST-My Custom Action");
    }

    [Fact]
    public async Task CreateActionAsync_PreservesExistingCustomerPrefix()
    {
        // Arrange
        var request = new CreateActionRequest
        {
            Name = "CUST-Already Prefixed",
            SystemPrompt = "Do something"
        };

        // Act
        var result = await _service.CreateActionAsync(request, CancellationToken.None);

        // Assert
        result.Name.Should().Be("CUST-Already Prefixed");
    }

    [Fact]
    public async Task CreateActionAsync_RejectsSystemPrefix()
    {
        // Arrange
        var request = new CreateActionRequest
        {
            Name = "SYS-Fake-System",
            SystemPrompt = "Do something"
        };

        // Act & Assert
        var act = () => _service.CreateActionAsync(request, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system prefix*");
    }

    #endregion

    #region Duplicate Name Handling Tests

    [Fact]
    public async Task GenerateUniqueNameAsync_NoDuplicate_ReturnsOriginal()
    {
        // Arrange - empty list already set up in constructor

        // Act
        var result = await _service.GenerateUniqueNameAsync(ScopeType.Action, "New Action", CancellationToken.None);

        // Assert
        result.Should().Be("New Action");
    }

    [Fact]
    public async Task GenerateUniqueNameAsync_WithDuplicate_AddsSuffix()
    {
        // Arrange
        var existingActions = new[]
        {
            new AnalysisAction { Id = Guid.NewGuid(), Name = "CUST-My Action", SystemPrompt = "Prompt" }
        };

        _scopeResolverMock.Setup(s => s.ListActionsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisAction>
            {
                Items = existingActions,
                TotalCount = 1,
                Page = 1,
                PageSize = 1000
            });

        // Act
        var result = await _service.GenerateUniqueNameAsync(ScopeType.Action, "CUST-My Action", CancellationToken.None);

        // Assert
        result.Should().Be("CUST-My Action (1)");
    }

    [Fact]
    public async Task GenerateUniqueNameAsync_WithMultipleDuplicates_IncrementsCounter()
    {
        // Arrange
        var existingActions = new[]
        {
            new AnalysisAction { Id = Guid.NewGuid(), Name = "CUST-My Action", SystemPrompt = "Prompt" },
            new AnalysisAction { Id = Guid.NewGuid(), Name = "CUST-My Action (1)", SystemPrompt = "Prompt" },
            new AnalysisAction { Id = Guid.NewGuid(), Name = "CUST-My Action (2)", SystemPrompt = "Prompt" }
        };

        _scopeResolverMock.Setup(s => s.ListActionsAsync(It.IsAny<ScopeListOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopeListResult<AnalysisAction>
            {
                Items = existingActions,
                TotalCount = 3,
                Page = 1,
                PageSize = 1000
            });

        // Act
        var result = await _service.GenerateUniqueNameAsync(ScopeType.Action, "CUST-My Action", CancellationToken.None);

        // Assert
        result.Should().Be("CUST-My Action (3)");
    }

    #endregion

    #region Create Operations Tests

    [Fact]
    public async Task CreateSkillAsync_ReturnsNewSkill()
    {
        // Arrange
        var request = new CreateSkillRequest
        {
            Name = "Legal Analysis",
            Description = "Analyze legal documents",
            PromptFragment = "Focus on legal terms",
            Category = "Legal"
        };

        // Act
        var result = await _service.CreateSkillAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("CUST-Legal Analysis");
        result.Description.Should().Be("Analyze legal documents");
        result.PromptFragment.Should().Be("Focus on legal terms");
        result.Category.Should().Be("Legal");
    }

    [Fact]
    public async Task CreateKnowledgeAsync_ReturnsNewKnowledge()
    {
        // Arrange
        var request = new CreateKnowledgeRequest
        {
            Name = "Reference Guide",
            Description = "Important reference material",
            Type = KnowledgeType.Inline,
            Content = "Reference content here"
        };

        // Act
        var result = await _service.CreateKnowledgeAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("CUST-Reference Guide");
        result.Type.Should().Be(KnowledgeType.Inline);
        result.Content.Should().Be("Reference content here");
    }

    [Fact]
    public async Task CreateToolAsync_ReturnsNewTool()
    {
        // Arrange
        var request = new CreateToolRequest
        {
            Name = "Custom Extractor",
            Description = "Extract custom data",
            Type = ToolType.Custom,
            HandlerClass = "CustomExtractorHandler"
        };

        // Act
        var result = await _service.CreateToolAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Name.Should().Be("CUST-Custom Extractor");
        result.Type.Should().Be(ToolType.Custom);
        result.HandlerClass.Should().Be("CustomExtractorHandler");
    }

    #endregion

    #region ScopeOwnershipException Tests

    [Fact]
    public void ScopeOwnershipException_ContainsCorrectInfo()
    {
        // Arrange & Act
        var exception = new ScopeOwnershipException("SYS-Test-Scope", "delete");

        // Assert
        exception.ScopeName.Should().Be("SYS-Test-Scope");
        exception.Operation.Should().Be("delete");
        exception.Message.Should().Contain("SYS-Test-Scope");
        exception.Message.Should().Contain("delete");
        exception.Message.Should().Contain("immutable");
    }

    #endregion

    #region N:N Link Operations Tests

    [Fact]
    public async Task LinkScopeToPlaybookAsync_CreatesLink()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        // Act
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Action, actionId, CancellationToken.None);

        // Assert
        var isLinked = await _service.IsLinkedAsync(playbookId, ScopeType.Action, actionId, CancellationToken.None);
        isLinked.Should().BeTrue();
    }

    [Fact]
    public async Task LinkScopeToPlaybookAsync_DuplicateLink_ThrowsException()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var skillId = Guid.NewGuid();

        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Skill, skillId, CancellationToken.None);

        // Act & Assert
        var act = () => _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Skill, skillId, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already linked*");
    }

    [Fact]
    public async Task LinkScopeToPlaybookAsync_DifferentScopeTypes_AllowsSameId()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var scopeId = Guid.NewGuid(); // Same ID used for different scope types

        // Act - Link same ID as different scope types (this is valid)
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Action, scopeId, CancellationToken.None);
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Skill, scopeId, CancellationToken.None);

        // Assert
        var actionLinked = await _service.IsLinkedAsync(playbookId, ScopeType.Action, scopeId, CancellationToken.None);
        var skillLinked = await _service.IsLinkedAsync(playbookId, ScopeType.Skill, scopeId, CancellationToken.None);

        actionLinked.Should().BeTrue();
        skillLinked.Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkScopeFromPlaybookAsync_RemovesLink()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var knowledgeId = Guid.NewGuid();

        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Knowledge, knowledgeId, CancellationToken.None);

        // Act
        await _service.UnlinkScopeFromPlaybookAsync(playbookId, ScopeType.Knowledge, knowledgeId, CancellationToken.None);

        // Assert
        var isLinked = await _service.IsLinkedAsync(playbookId, ScopeType.Knowledge, knowledgeId, CancellationToken.None);
        isLinked.Should().BeFalse();
    }

    [Fact]
    public async Task UnlinkScopeFromPlaybookAsync_NonExistentLink_DoesNotThrow()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var toolId = Guid.NewGuid();

        // Act - Unlink without ever linking (should be idempotent)
        var act = () => _service.UnlinkScopeFromPlaybookAsync(playbookId, ScopeType.Tool, toolId, CancellationToken.None);

        // Assert - Should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task IsLinkedAsync_NoLink_ReturnsFalse()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var outputId = Guid.NewGuid();

        // Act
        var isLinked = await _service.IsLinkedAsync(playbookId, ScopeType.Output, outputId, CancellationToken.None);

        // Assert
        isLinked.Should().BeFalse();
    }

    [Fact]
    public async Task GetLinkedScopeIdsAsync_ReturnsAllLinkedScopes()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var action1 = Guid.NewGuid();
        var action2 = Guid.NewGuid();
        var action3 = Guid.NewGuid();

        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Action, action1, CancellationToken.None);
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Action, action2, CancellationToken.None);
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Action, action3, CancellationToken.None);

        // Act
        var linkedIds = await _service.GetLinkedScopeIdsAsync(playbookId, ScopeType.Action, CancellationToken.None);

        // Assert
        linkedIds.Should().HaveCount(3);
        linkedIds.Should().Contain(action1);
        linkedIds.Should().Contain(action2);
        linkedIds.Should().Contain(action3);
    }

    [Fact]
    public async Task GetLinkedScopeIdsAsync_NoLinks_ReturnsEmptyList()
    {
        // Arrange
        var playbookId = Guid.NewGuid();

        // Act
        var linkedIds = await _service.GetLinkedScopeIdsAsync(playbookId, ScopeType.Skill, CancellationToken.None);

        // Assert
        linkedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLinkedScopeIdsAsync_AfterUnlink_ExcludesUnlinkedScope()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var tool1 = Guid.NewGuid();
        var tool2 = Guid.NewGuid();

        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Tool, tool1, CancellationToken.None);
        await _service.LinkScopeToPlaybookAsync(playbookId, ScopeType.Tool, tool2, CancellationToken.None);

        // Act - Unlink tool1
        await _service.UnlinkScopeFromPlaybookAsync(playbookId, ScopeType.Tool, tool1, CancellationToken.None);
        var linkedIds = await _service.GetLinkedScopeIdsAsync(playbookId, ScopeType.Tool, CancellationToken.None);

        // Assert
        linkedIds.Should().HaveCount(1);
        linkedIds.Should().Contain(tool2);
        linkedIds.Should().NotContain(tool1);
    }

    [Theory]
    [InlineData(ScopeType.Action)]
    [InlineData(ScopeType.Skill)]
    [InlineData(ScopeType.Knowledge)]
    [InlineData(ScopeType.Tool)]
    [InlineData(ScopeType.Output)]
    public async Task LinkScopeToPlaybookAsync_AllScopeTypes_Work(ScopeType scopeType)
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();

        // Act
        await _service.LinkScopeToPlaybookAsync(playbookId, scopeType, scopeId, CancellationToken.None);

        // Assert
        var isLinked = await _service.IsLinkedAsync(playbookId, scopeType, scopeId, CancellationToken.None);
        isLinked.Should().BeTrue();
    }

    #endregion
}
