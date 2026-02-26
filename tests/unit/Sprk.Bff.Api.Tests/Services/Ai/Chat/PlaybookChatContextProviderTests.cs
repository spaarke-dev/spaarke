using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="PlaybookChatContextProvider"/>.
///
/// Verifies:
/// - System prompt loaded from playbook Action record
/// - Knowledge scope resolution: inline, RAG index, and document types partitioned correctly
/// - Skill prompt fragments composed into system prompt
/// - Soft failure when scope resolution fails
/// - Document summary loaded from Dataverse
/// </summary>
public class PlaybookChatContextProviderTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;

    public PlaybookChatContextProviderTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        // Default: playbook with action, no scopes
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction("You are a legal assistant.");
        SetupEmptyScopes();
    }

    [Fact]
    public async Task GetContextAsync_ReturnsKnowledgeScope_WithRagSourceIds()
    {
        // Arrange
        var knw1 = Guid.NewGuid();
        var knw2 = Guid.NewGuid();
        SetupScopes(
            knowledge:
            [
                CreateKnowledge(knw1, "NDA Templates", KnowledgeType.RagIndex),
                CreateKnowledge(knw2, "Contract Clauses", KnowledgeType.RagIndex)
            ]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.RagKnowledgeSourceIds.Should().HaveCount(2);
        context.KnowledgeScope.RagKnowledgeSourceIds.Should().Contain(knw1.ToString());
        context.KnowledgeScope.RagKnowledgeSourceIds.Should().Contain(knw2.ToString());
    }

    [Fact]
    public async Task GetContextAsync_InjectsInlineKnowledge_IntoSystemPrompt()
    {
        // Arrange
        SetupScopes(
            knowledge:
            [
                CreateKnowledge(Guid.NewGuid(), "Legal Glossary", KnowledgeType.Inline,
                    content: "NDA: Non-Disclosure Agreement\nSLA: Service Level Agreement")
            ]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert — inline content should appear in system prompt
        context.SystemPrompt.Should().Contain("## Reference Materials");
        context.SystemPrompt.Should().Contain("Legal Glossary");
        context.SystemPrompt.Should().Contain("NDA: Non-Disclosure Agreement");
    }

    [Fact]
    public async Task GetContextAsync_ComposesSkillInstructions_IntoSystemPrompt()
    {
        // Arrange
        SetupScopes(
            skills:
            [
                new AnalysisSkill
                {
                    Id = Guid.NewGuid(),
                    Name = "Contract Analysis",
                    PromptFragment = "Focus on identifying key obligations and deadlines."
                },
                new AnalysisSkill
                {
                    Id = Guid.NewGuid(),
                    Name = "Risk Detection",
                    PromptFragment = "Flag clauses with unusual indemnification terms."
                }
            ]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert — skill fragments should appear in system prompt
        context.SystemPrompt.Should().Contain("## Specialized Instructions");
        context.SystemPrompt.Should().Contain("Focus on identifying key obligations");
        context.SystemPrompt.Should().Contain("Flag clauses with unusual indemnification");
    }

    [Fact]
    public async Task GetContextAsync_PartitionsKnowledgeByType_Correctly()
    {
        // Arrange — mix of all three knowledge types
        var ragId = Guid.NewGuid();
        SetupScopes(
            knowledge:
            [
                CreateKnowledge(ragId, "NDA Templates", KnowledgeType.RagIndex),
                CreateKnowledge(Guid.NewGuid(), "Legal Terms", KnowledgeType.Inline, content: "Terms glossary"),
                CreateKnowledge(Guid.NewGuid(), "Sample Doc", KnowledgeType.Document)
            ]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert — only RagIndex types in RagKnowledgeSourceIds, inline in prompt
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.RagKnowledgeSourceIds.Should().HaveCount(1);
        context.KnowledgeScope.RagKnowledgeSourceIds.Should().Contain(ragId.ToString());
        context.KnowledgeScope.InlineContent.Should().Contain("Terms glossary");
    }

    [Fact]
    public async Task GetContextAsync_ReturnsNullScope_WhenNoKnowledgeOrSkills()
    {
        // Arrange — empty scopes
        SetupEmptyScopes();

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert
        context.KnowledgeScope.Should().BeNull();
    }

    [Fact]
    public async Task GetContextAsync_ReturnsNullScope_WhenScopeResolutionFails()
    {
        // Arrange — scope resolution throws
        _scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var sut = CreateProvider();

        // Act — should not throw (soft failure)
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert — scope is null but context is still valid
        context.Should().NotBeNull();
        context.KnowledgeScope.Should().BeNull();
        context.SystemPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetContextAsync_SetsActiveDocumentId_OnKnowledgeScope()
    {
        // Arrange
        SetupScopes(
            knowledge: [CreateKnowledge(Guid.NewGuid(), "Templates", KnowledgeType.RagIndex)]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.ActiveDocumentId.Should().Be(TestDocumentId);
    }

    [Fact]
    public async Task GetContextAsync_WithHostContext_FlowsEntityToKnowledgeScope()
    {
        // Arrange
        var ragId = Guid.NewGuid();
        SetupScopes(
            knowledge: [CreateKnowledge(ragId, "NDA Templates", KnowledgeType.RagIndex)]);

        var hostContext = new ChatHostContext(EntityType: "matter", EntityId: "entity-456");
        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext, cancellationToken: CancellationToken.None);

        // Assert
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.ParentEntityType.Should().Be("matter");
        context.KnowledgeScope.ParentEntityId.Should().Be("entity-456");
    }

    [Fact]
    public async Task GetContextAsync_WithoutHostContext_NullEntityFieldsOnScope()
    {
        // Arrange
        var ragId = Guid.NewGuid();
        SetupScopes(
            knowledge: [CreateKnowledge(ragId, "NDA Templates", KnowledgeType.RagIndex)]);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, cancellationToken: CancellationToken.None);

        // Assert
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.ParentEntityType.Should().BeNull();
        context.KnowledgeScope.ParentEntityId.Should().BeNull();
    }

    #region Setup helpers

    private PlaybookChatContextProvider CreateProvider()
        => new(
            _scopeResolverMock.Object,
            _playbookServiceMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object);

    private void SetupPlaybook(Guid[]? actionIds = null)
    {
        _playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = TestPlaybookId,
                Name = "Test Playbook",
                ActionIds = actionIds ?? [],
                SkillIds = [],
                KnowledgeIds = [],
                ToolIds = []
            });
    }

    private void SetupAction(string systemPrompt)
    {
        _scopeResolverMock
            .Setup(s => s.GetActionAsync(TestActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = TestActionId,
                Name = "Test Action",
                SystemPrompt = systemPrompt
            });
    }

    private void SetupEmptyScopes()
    {
        _scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes([], [], []));
    }

    private void SetupScopes(
        AnalysisKnowledge[]? knowledge = null,
        AnalysisSkill[]? skills = null)
    {
        _scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(skills ?? [], knowledge ?? [], []));
    }

    private static AnalysisKnowledge CreateKnowledge(
        Guid id, string name, KnowledgeType type, string? content = null)
        => new()
        {
            Id = id,
            Name = name,
            Type = type,
            Content = content
        };

    #endregion
}
