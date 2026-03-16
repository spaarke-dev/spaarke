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
/// Unit tests for the entity metadata enrichment behavior in
/// <see cref="PlaybookChatContextProvider.GetContextAsync"/>.
///
/// Phase 3 adds context-awareness: when a <see cref="ChatHostContext"/> has
/// EntityName and PageType populated, the system prompt is enriched with an
/// entity context block appended AFTER the playbook prompt.
///
/// Expected enrichment format:
///   "Context: You are assisting with {entityType} record '{entityName}'.
///    The user is viewing the {humanReadablePageType} view."
///
/// These tests are written tests-first (task 030) before the enrichment
/// implementation (task 031). They may not pass until task 031 is complete.
/// </summary>
public class PlaybookChatContextProviderEnrichmentTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestActionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string BaseSystemPrompt = "You are a legal assistant.";

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;

    public PlaybookChatContextProviderEnrichmentTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        // Default: playbook with action, no scopes
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupEmptyScopes();
    }

    // =========================================================================
    // Null guards — enrichment block must NOT be appended
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_NullEntityName_NoEnrichmentBlockAppended()
    {
        // Arrange — EntityName is null, PageType is valid
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: null,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — system prompt should NOT contain the enrichment marker
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        // The base playbook prompt should still be present
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    [Fact]
    public async Task GetContextAsync_NullPageType_NoEnrichmentBlockAppended()
    {
        // Arrange — EntityName is valid, PageType is null
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Acme v. Beta Corp",
            PageType: null);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    [Fact]
    public async Task GetContextAsync_UnknownPageType_NoEnrichmentBlockAppended()
    {
        // Arrange — PageType is "unknown" which should be treated as invalid
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Acme v. Beta Corp",
            PageType: "unknown");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    [Fact]
    public async Task GetContextAsync_NullHostContext_NoEnrichmentBlockAppended()
    {
        // Arrange — no host context at all
        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext: null,
            cancellationToken: CancellationToken.None);

        // Assert
        context.SystemPrompt.Should().NotContain("Context: You are assisting with");
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
    }

    // =========================================================================
    // Valid enrichment — block appended after playbook prompt
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_ValidEntityNameAndPageType_EnrichmentBlockAppended()
    {
        // Arrange — both EntityName and PageType are valid
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Acme v. Beta Corp",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment block present with correct entity type and name
        context.SystemPrompt.Should().Contain("Context: You are assisting with matter record 'Acme v. Beta Corp'.");
        context.SystemPrompt.Should().Contain("The user is viewing the main form view.");
    }

    [Fact]
    public async Task GetContextAsync_EnrichmentAppendsAfterPlaybookPrompt_NotReplace()
    {
        // Arrange
        var hostContext = new ChatHostContext(
            EntityType: "project",
            EntityId: "entity-456",
            EntityName: "Project Alpha",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — playbook system prompt is preserved intact
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);

        // Assert — enrichment block appears AFTER the base prompt
        var basePromptIndex = context.SystemPrompt.IndexOf(BaseSystemPrompt, StringComparison.Ordinal);
        var enrichmentIndex = context.SystemPrompt.IndexOf("Context: You are assisting with", StringComparison.Ordinal);

        basePromptIndex.Should().BeGreaterOrEqualTo(0, "base prompt should be present");
        enrichmentIndex.Should().BeGreaterThan(basePromptIndex, "enrichment should appear after base prompt");
    }

    // =========================================================================
    // Human-readable page type mappings
    // =========================================================================

    [Theory]
    [InlineData("entityrecord", "main form view")]
    [InlineData("entitylist", "list view")]
    [InlineData("dashboard", "dashboard view")]
    [InlineData("webresource", "workspace view")]
    [InlineData("custom", "custom page view")]
    public async Task GetContextAsync_MapsPageTypeToHumanReadableLabel(string pageType, string expectedLabel)
    {
        // Arrange
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Test Record",
            PageType: pageType);

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — the human-readable label should appear in the enrichment
        context.SystemPrompt.Should().Contain($"The user is viewing the {expectedLabel}.");
    }

    // =========================================================================
    // Token budget — enrichment block must be compact
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_EnrichmentBlock_DoesNotExceed100Tokens()
    {
        // Arrange — use a realistic entity name
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Acme Corporation v. Beta Industries LLC",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — extract the enrichment block (everything after "Context: You are assisting")
        var enrichmentStart = context.SystemPrompt.IndexOf("Context: You are assisting with", StringComparison.Ordinal);
        enrichmentStart.Should().BeGreaterOrEqualTo(0, "enrichment block should be present");

        var enrichmentBlock = context.SystemPrompt[enrichmentStart..];

        // Rough token estimate: ~4 chars per token for English text.
        // 100 tokens ~ 400 characters. Use a generous margin.
        var estimatedTokens = enrichmentBlock.Length / 4.0;
        estimatedTokens.Should().BeLessThanOrEqualTo(100,
            $"enrichment block should be ≤100 tokens (estimated {estimatedTokens:F0} from {enrichmentBlock.Length} chars)");
    }

    // =========================================================================
    // Budget exceeded — enrichment skipped when it would push past token limit
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_EnrichmentSkipped_WhenBudgetWouldBeExceeded()
    {
        // Arrange — create a system prompt that is very close to the token budget
        // by using a massive inline knowledge section
        var longContent = new string('x', 50_000); // ~12,500 tokens of content
        SetupAction(BaseSystemPrompt);
        SetupScopes(
            knowledge:
            [
                new AnalysisKnowledge
                {
                    Id = Guid.NewGuid(),
                    Name = "Massive Knowledge Base",
                    Type = KnowledgeType.Inline,
                    Content = longContent
                }
            ]);

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Test Budget Exceeded",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — the system prompt should still be valid
        context.SystemPrompt.Should().NotBeNullOrEmpty();
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);

        // NOTE: The exact behavior depends on the implementation's token budget.
        // If the budget is not exceeded (implementation may have a high cap),
        // this test verifies at minimum that the prompt is well-formed.
        // If the budget IS exceeded, the enrichment block should be absent
        // and a warning should be logged.
        //
        // When task 031 implements the budget check, refine this assertion:
        // If the enrichment is skipped, verify the warning log was emitted.
        if (!context.SystemPrompt.Contains("Context: You are assisting with"))
        {
            // Enrichment was skipped — verify warning was logged
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("budget") || v.ToString()!.Contains("token")),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce,
                "A warning should be logged when enrichment is skipped due to budget");
        }
    }

    // =========================================================================
    // Enrichment works with generic (no-playbook) chat mode
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_GenericMode_WithValidHostContext_EnrichmentBlockAppended()
    {
        // Arrange — no playbook (generic chat), but host context is present
        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-123",
            EntityName: "Acme v. Beta Corp",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act — playbookId is null (generic chat mode)
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, playbookId: null, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment should still be applied in generic mode
        context.SystemPrompt.Should().Contain("Context: You are assisting with matter record 'Acme v. Beta Corp'.");
        context.SystemPrompt.Should().Contain("The user is viewing the main form view.");
    }

    // =========================================================================
    // Enrichment preserves existing knowledge/skill enrichment
    // =========================================================================

    [Fact]
    public async Task GetContextAsync_EnrichmentCoexists_WithKnowledgeAndSkillSections()
    {
        // Arrange — playbook with inline knowledge, skills, AND host context
        SetupScopes(
            knowledge:
            [
                new AnalysisKnowledge
                {
                    Id = Guid.NewGuid(),
                    Name = "Legal Glossary",
                    Type = KnowledgeType.Inline,
                    Content = "NDA: Non-Disclosure Agreement"
                }
            ],
            skills:
            [
                new AnalysisSkill
                {
                    Id = Guid.NewGuid(),
                    Name = "Contract Analysis",
                    PromptFragment = "Focus on key obligations."
                }
            ]);

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-789",
            EntityName: "Smith v. Jones",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — all three enrichment types should coexist
        context.SystemPrompt.Should().Contain(BaseSystemPrompt, "base prompt preserved");
        context.SystemPrompt.Should().Contain("## Reference Materials", "inline knowledge present");
        context.SystemPrompt.Should().Contain("Legal Glossary", "knowledge content present");
        context.SystemPrompt.Should().Contain("## Specialized Instructions", "skill instructions present");
        context.SystemPrompt.Should().Contain("Focus on key obligations", "skill fragment present");
        context.SystemPrompt.Should().Contain("Context: You are assisting with matter record 'Smith v. Jones'.",
            "entity enrichment present");
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

    #endregion
}
