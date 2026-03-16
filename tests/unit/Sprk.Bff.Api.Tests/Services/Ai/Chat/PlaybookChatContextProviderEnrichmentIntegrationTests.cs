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
/// Integration-level tests for the <see cref="PlaybookChatContextProvider"/>
/// entity metadata enrichment pipeline.
///
/// These tests verify the FULL GetContextAsync pipeline end-to-end:
///   1. Playbook resolution (or generic/null playbook fallback)
///   2. Scope resolution (knowledge + skills)
///   3. System prompt composition (base + inline knowledge + skill instructions)
///   4. Entity enrichment appended after all other prompt sections
///   5. Privacy: EntityName must NOT leak into structured log properties
///
/// Unlike the unit-level enrichment tests that focus on individual guard
/// clauses, these tests exercise the complete pipeline with realistic
/// multi-section system prompts and verify enrichment coexists correctly.
/// </summary>
public class PlaybookChatContextProviderEnrichmentIntegrationTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TestActionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string TestDocumentId = "doc-integration-001";
    private const string TestTenantId = "tenant-integration";
    private const string BaseSystemPrompt = "You are a legal assistant specializing in corporate transactions.";

    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<PlaybookChatContextProvider>> _loggerMock;

    public PlaybookChatContextProviderEnrichmentIntegrationTests()
    {
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _playbookServiceMock = new Mock<IPlaybookService>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        // Default: suppress document service calls (not relevant to enrichment tests)
        _dataverseServiceMock
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentEntity?)null);
    }

    // =========================================================================
    // Full pipeline: playbook mode with enrichment
    // =========================================================================

    [Fact]
    public async Task FullPipeline_PlaybookMode_EnrichmentAppendedAfterAllSections()
    {
        // Arrange — playbook with action, inline knowledge, skill, AND host context
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupScopes(
            knowledge:
            [
                new AnalysisKnowledge
                {
                    Id = Guid.NewGuid(),
                    Name = "Contract Terminology",
                    Type = KnowledgeType.Inline,
                    Content = "Indemnity: A contractual obligation to compensate for loss or damage."
                }
            ],
            skills:
            [
                new AnalysisSkill
                {
                    Id = Guid.NewGuid(),
                    Name = "Due Diligence Review",
                    PromptFragment = "Focus on risk factors and material adverse conditions."
                }
            ]);

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-integration-001",
            EntityName: "Acme Corp v. Globex Industries",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — all four sections present in correct order
        var prompt = context.SystemPrompt;

        // 1. Base playbook prompt
        prompt.Should().Contain(BaseSystemPrompt, "base playbook prompt must be present");

        // 2. Inline knowledge section
        prompt.Should().Contain("## Reference Materials", "knowledge section header must be present");
        prompt.Should().Contain("Contract Terminology", "knowledge content must be present");
        prompt.Should().Contain("Indemnity:", "inline knowledge content must be present");

        // 3. Skill instructions section
        prompt.Should().Contain("## Specialized Instructions", "skill section header must be present");
        prompt.Should().Contain("Focus on risk factors", "skill fragment must be present");

        // 4. Entity enrichment block
        prompt.Should().Contain(
            "Context: You are assisting with matter record 'Acme Corp v. Globex Industries'.",
            "entity enrichment must be present");
        prompt.Should().Contain(
            "The user is viewing the main form view.",
            "human-readable page type must be present");

        // 5. Verify ordering: base → knowledge → skills → enrichment
        var baseIdx = prompt.IndexOf(BaseSystemPrompt, StringComparison.Ordinal);
        var knowledgeIdx = prompt.IndexOf("## Reference Materials", StringComparison.Ordinal);
        var skillIdx = prompt.IndexOf("## Specialized Instructions", StringComparison.Ordinal);
        var enrichmentIdx = prompt.IndexOf("Context: You are assisting with", StringComparison.Ordinal);

        baseIdx.Should().BeLessThan(knowledgeIdx, "base prompt should precede knowledge section");
        knowledgeIdx.Should().BeLessThan(skillIdx, "knowledge section should precede skill section");
        skillIdx.Should().BeLessThan(enrichmentIdx, "skill section should precede entity enrichment");
    }

    [Fact]
    public async Task FullPipeline_PlaybookMode_CorrectEntityReference()
    {
        // Arrange — verify the enrichment includes exact entity type and name
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupEmptyScopes();

        var hostContext = new ChatHostContext(
            EntityType: "project",
            EntityId: "proj-999",
            EntityName: "Solar Panel Installation Phase 2",
            PageType: "entitylist");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — entity type and name in enrichment block
        context.SystemPrompt.Should().Contain(
            "Context: You are assisting with project record 'Solar Panel Installation Phase 2'.");
        context.SystemPrompt.Should().Contain(
            "The user is viewing the list view.");
    }

    // =========================================================================
    // Full pipeline: generic (null playbook) mode with enrichment
    // =========================================================================

    [Fact]
    public async Task FullPipeline_GenericMode_EnrichmentAppendedToDefaultPrompt()
    {
        // Arrange — no playbook, but host context is present
        var hostContext = new ChatHostContext(
            EntityType: "invoice",
            EntityId: "inv-001",
            EntityName: "INV-2026-0042",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act — playbookId is null (generic chat mode)
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, playbookId: null, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — default prompt present (generic mode uses BuildDefaultSystemPrompt)
        context.SystemPrompt.Should().Contain("You are an AI assistant",
            "generic mode should use the default system prompt");

        // Assert — enrichment block still appended
        context.SystemPrompt.Should().Contain(
            "Context: You are assisting with invoice record 'INV-2026-0042'.",
            "enrichment must work in generic mode");
        context.SystemPrompt.Should().Contain(
            "The user is viewing the main form view.");

        // Assert — playbook ID is null in response
        context.PlaybookId.Should().BeNull("generic mode has no playbook");
    }

    [Fact]
    public async Task FullPipeline_GenericMode_AllPageTypes_ProduceCorrectLabels()
    {
        // Arrange + Act + Assert — verify all 5 page type labels in generic mode
        var pageTypeMappings = new Dictionary<string, string>
        {
            ["entityrecord"] = "main form view",
            ["entitylist"] = "list view",
            ["dashboard"] = "dashboard view",
            ["webresource"] = "workspace view",
            ["custom"] = "custom page view"
        };

        foreach (var (pageType, expectedLabel) in pageTypeMappings)
        {
            var hostContext = new ChatHostContext(
                EntityType: "matter",
                EntityId: "entity-multi",
                EntityName: "Test Record",
                PageType: pageType);

            var sut = CreateProvider();

            var context = await sut.GetContextAsync(
                TestDocumentId, TestTenantId, playbookId: null, hostContext,
                cancellationToken: CancellationToken.None);

            context.SystemPrompt.Should().Contain(
                $"The user is viewing the {expectedLabel}.",
                $"page type '{pageType}' should map to '{expectedLabel}'");
        }
    }

    // =========================================================================
    // Privacy: EntityName must NOT appear in structured log properties
    // =========================================================================

    [Fact]
    public async Task FullPipeline_EntityName_NotLoggedAsStructuredProperty()
    {
        // Arrange — use a distinctive entity name that would be easy to find in logs
        const string sensitiveEntityName = "CONFIDENTIAL-Merger-Alpha-Corp-2026";

        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupEmptyScopes();

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-privacy-test",
            EntityName: sensitiveEntityName,
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment is present (the name IS in the prompt, which is expected)
        context.SystemPrompt.Should().Contain(sensitiveEntityName);

        // Assert — EntityName must NOT appear in any structured log property.
        // Structured log properties are the template parameters (e.g., {DocumentId}, {PlaybookId}).
        // We verify by inspecting all log calls and checking that none of the
        // structured property values contain the entity name.
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    HasStructuredPropertyWithValue(state, sensitiveEntityName)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            $"EntityName '{sensitiveEntityName}' must not appear as a structured log property value (privacy requirement)");
    }

    [Fact]
    public async Task FullPipeline_GenericMode_EntityName_NotLoggedAsStructuredProperty()
    {
        // Arrange — same privacy check for generic (null playbook) mode
        const string sensitiveEntityName = "SECRET-Acquisition-Target-2026";

        var hostContext = new ChatHostContext(
            EntityType: "account",
            EntityId: "acct-privacy-test",
            EntityName: sensitiveEntityName,
            PageType: "dashboard");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, playbookId: null, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment present
        context.SystemPrompt.Should().Contain(sensitiveEntityName);

        // Assert — not in structured log properties
        _loggerMock.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    HasStructuredPropertyWithValue(state, sensitiveEntityName)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never,
            $"EntityName '{sensitiveEntityName}' must not appear as a structured log property value in generic mode");
    }

    // =========================================================================
    // Pipeline robustness: enrichment with edge cases
    // =========================================================================

    [Fact]
    public async Task FullPipeline_PlaybookWithNoAction_EnrichmentStillApplied()
    {
        // Arrange — playbook exists but has no actions (uses default prompt)
        SetupPlaybook(actionIds: []);
        SetupEmptyScopes();

        var hostContext = new ChatHostContext(
            EntityType: "contact",
            EntityId: "contact-001",
            EntityName: "Jane Smith",
            PageType: "entityrecord");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — default prompt used, enrichment still appended
        context.SystemPrompt.Should().Contain("You are an AI assistant",
            "should fall back to default system prompt");
        context.SystemPrompt.Should().Contain(
            "Context: You are assisting with contact record 'Jane Smith'.",
            "enrichment should still be appended after default prompt");
    }

    [Fact]
    public async Task FullPipeline_PlaybookMode_EnrichmentAbsent_WhenHostContextNull()
    {
        // Arrange — playbook with full scopes, but NO host context
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupScopes(
            knowledge:
            [
                new AnalysisKnowledge
                {
                    Id = Guid.NewGuid(),
                    Name = "Industry Guide",
                    Type = KnowledgeType.Inline,
                    Content = "Key regulations for financial compliance."
                }
            ]);

        var sut = CreateProvider();

        // Act — no host context
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext: null,
            cancellationToken: CancellationToken.None);

        // Assert — base prompt and knowledge present, but NO enrichment
        context.SystemPrompt.Should().Contain(BaseSystemPrompt);
        context.SystemPrompt.Should().Contain("## Reference Materials");
        context.SystemPrompt.Should().Contain("Industry Guide");
        context.SystemPrompt.Should().NotContain("Context: You are assisting with",
            "no enrichment when host context is null");
    }

    [Fact]
    public async Task FullPipeline_DocumentServiceFailure_EnrichmentStillApplied()
    {
        // Arrange — document service throws, but enrichment should still work
        SetupPlaybook(actionIds: [TestActionId]);
        SetupAction(BaseSystemPrompt);
        SetupEmptyScopes();

        _dataverseServiceMock
            .Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Document service unavailable"));

        var hostContext = new ChatHostContext(
            EntityType: "matter",
            EntityId: "entity-doc-fail",
            EntityName: "Resilience Test Corp",
            PageType: "webresource");

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId, hostContext,
            cancellationToken: CancellationToken.None);

        // Assert — enrichment is present despite document service failure
        context.SystemPrompt.Should().Contain(
            "Context: You are assisting with matter record 'Resilience Test Corp'.");
        context.SystemPrompt.Should().Contain(
            "The user is viewing the workspace view.");

        // Document summary should be null due to service failure
        context.DocumentSummary.Should().BeNull();
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
                Name = "Integration Test Playbook",
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
                Name = "Integration Test Action",
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

    /// <summary>
    /// Inspects the structured log state to determine if any property VALUE
    /// contains the specified sensitive string. This is used to verify that
    /// EntityName is not leaked into structured log properties.
    /// </summary>
    /// <remarks>
    /// Microsoft.Extensions.Logging structured state is typically an
    /// IReadOnlyList of KeyValuePair&lt;string, object?&gt;. We check each
    /// value (not the key) for the sensitive string. The "{OriginalFormat}"
    /// key is the message template and is excluded since the entity name
    /// would only appear there if it were interpolated (not templated).
    /// </remarks>
    private static bool HasStructuredPropertyWithValue(object state, string sensitiveValue)
    {
        if (state is IReadOnlyList<KeyValuePair<string, object?>> properties)
        {
            foreach (var kvp in properties)
            {
                // Skip the message template itself — we only care about property values
                if (kvp.Key == "{OriginalFormat}")
                    continue;

                if (kvp.Value?.ToString()?.Contains(sensitiveValue, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
        }

        return false;
    }

    #endregion
}
