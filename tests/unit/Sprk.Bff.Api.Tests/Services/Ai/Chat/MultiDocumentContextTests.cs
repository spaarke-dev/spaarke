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
/// Unit tests for the multi-document context feature (AdditionalDocumentIds).
///
/// Covers:
/// - ChatKnowledgeScope validation: max 5 cap, default null, boundary cases
/// - Normalization: blanks removed, duplicates deduplicated
/// - PlaybookChatContextProvider: passes additional IDs through to ChatKnowledgeScope
/// - Construction: verify the record captures additional document IDs correctly
/// </summary>
public class MultiDocumentContextTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestActionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string TestDocumentId = "doc-primary-001";
    private const string TestTenantId = "tenant-test-123";

    #region ChatKnowledgeScope — AdditionalDocumentIds validation

    [Fact]
    public void AdditionalDocumentIds_Default_IsNull()
    {
        // Arrange & Act
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001");

        // Assert — default parameter value is null
        scope.AdditionalDocumentIds.Should().BeNull();
    }

    [Fact]
    public void AdditionalDocumentIds_WithEmptyList_IsValid()
    {
        // Arrange & Act
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001",
            AdditionalDocumentIds: new List<string>());

        // Assert
        scope.ValidateAdditionalDocuments().Should().BeTrue();
        scope.AdditionalDocumentIds.Should().BeEmpty();
    }

    [Fact]
    public void AdditionalDocumentIds_WithFiveDocuments_IsValid()
    {
        // Arrange — exactly at the MaxAdditionalDocuments cap
        var additionalIds = new List<string> { "doc-1", "doc-2", "doc-3", "doc-4", "doc-5" };

        // Act
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001",
            AdditionalDocumentIds: additionalIds);

        // Assert
        scope.ValidateAdditionalDocuments().Should().BeTrue();
        scope.AdditionalDocumentIds.Should().HaveCount(5);
    }

    [Fact]
    public void AdditionalDocumentIds_ExceedsFiveDocuments_FailsValidation()
    {
        // Arrange — exceeds MaxAdditionalDocuments cap
        var additionalIds = new List<string> { "doc-1", "doc-2", "doc-3", "doc-4", "doc-5", "doc-6" };

        // Act
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001",
            AdditionalDocumentIds: additionalIds);

        // Assert
        scope.ValidateAdditionalDocuments().Should().BeFalse(
            "additional documents exceeding MaxAdditionalDocuments (5) should fail validation");
    }

    [Fact]
    public void AdditionalDocumentIds_WithNull_PassesValidation()
    {
        // Arrange
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001",
            AdditionalDocumentIds: null);

        // Assert — null is valid (no additional documents)
        scope.ValidateAdditionalDocuments().Should().BeTrue();
    }

    [Fact]
    public void MaxAdditionalDocuments_IsExactlyFive()
    {
        // Assert — verify the constant value
        ChatKnowledgeScope.MaxAdditionalDocuments.Should().Be(5);
    }

    [Fact]
    public void AdditionalDocumentIds_WithOneDocument_IsValid()
    {
        // Arrange
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001",
            AdditionalDocumentIds: new List<string> { "doc-extra" });

        // Assert
        scope.ValidateAdditionalDocuments().Should().BeTrue();
        scope.AdditionalDocumentIds.Should().HaveCount(1);
    }

    #endregion

    #region ChatKnowledgeScope — construction with additional documents

    [Fact]
    public void ChatKnowledgeScope_WithAdditionalDocs_PreservesAllProperties()
    {
        // Arrange
        var ragIds = new List<string> { "rag-1", "rag-2" };
        var additionalIds = new List<string> { "doc-extra-1", "doc-extra-2" };

        // Act
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: ragIds,
            InlineContent: "Some inline content",
            SkillInstructions: "Do analysis",
            ActiveDocumentId: "doc-primary",
            ParentEntityType: "matter",
            ParentEntityId: "entity-123",
            AdditionalDocumentIds: additionalIds);

        // Assert — all properties correctly set
        scope.RagKnowledgeSourceIds.Should().BeEquivalentTo(ragIds);
        scope.InlineContent.Should().Be("Some inline content");
        scope.SkillInstructions.Should().Be("Do analysis");
        scope.ActiveDocumentId.Should().Be("doc-primary");
        scope.ParentEntityType.Should().Be("matter");
        scope.ParentEntityId.Should().Be("entity-123");
        scope.AdditionalDocumentIds.Should().BeEquivalentTo(additionalIds);
    }

    [Fact]
    public void ChatKnowledgeScope_WithRecord_SupportsWithExpression()
    {
        // Arrange
        var original = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-001");

        // Act — use with-expression to add additional documents
        var updated = original with
        {
            AdditionalDocumentIds = new List<string> { "doc-extra-1", "doc-extra-2" }
        };

        // Assert
        updated.AdditionalDocumentIds.Should().HaveCount(2);
        original.AdditionalDocumentIds.Should().BeNull("original should be unchanged");
    }

    #endregion

    #region PlaybookChatContextProvider — additional document passthrough

    [Fact]
    public async Task GetContextAsync_WithAdditionalDocumentIds_IncludesInKnowledgeScope()
    {
        // Arrange
        var additionalDocs = new List<string> { "doc-extra-1", "doc-extra-2" };
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        // Setup scope resolver to return knowledge with skills to trigger scope creation
        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Test Skill", PromptFragment = "Test fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — additional document IDs should be passed through to the knowledge scope
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().BeEquivalentTo(additionalDocs);
    }

    [Fact]
    public async Task GetContextAsync_WithNullAdditionalDocumentIds_ScopeHasNullAdditionalDocs()
    {
        // Arrange
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: null);

        // Assert — null additional docs should remain null in scope
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().BeNull();
    }

    [Fact]
    public async Task GetContextAsync_WithBlankDocumentIds_NormalizesAwayBlanks()
    {
        // Arrange — additional document IDs include blank and whitespace entries
        var additionalDocs = new List<string> { "doc-valid-1", "", "  ", "doc-valid-2", null! };
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — blank/null entries should be filtered out
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().NotBeNull();
        result.KnowledgeScope.AdditionalDocumentIds!.Should().OnlyContain(
            id => !string.IsNullOrWhiteSpace(id),
            "blank and null document IDs must be normalized away");
        result.KnowledgeScope.AdditionalDocumentIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetContextAsync_WithDuplicateDocumentIds_DeduplicatesIds()
    {
        // Arrange — additional document IDs include duplicates
        var additionalDocs = new List<string> { "doc-1", "doc-2", "doc-1", "doc-3", "doc-2" };
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — duplicates should be removed
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().NotBeNull();
        result.KnowledgeScope.AdditionalDocumentIds!.Should().OnlyHaveUniqueItems(
            "duplicate document IDs must be deduplicated");
        result.KnowledgeScope.AdditionalDocumentIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetContextAsync_WithMoreThanFiveValidIds_CapsAtFive()
    {
        // Arrange — provider should cap at MaxAdditionalDocuments even if more are passed
        var additionalDocs = new List<string>
        {
            "doc-1", "doc-2", "doc-3", "doc-4", "doc-5", "doc-6", "doc-7"
        };
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — should be capped at MaxAdditionalDocuments (5)
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().NotBeNull();
        result.KnowledgeScope.AdditionalDocumentIds!.Should().HaveCountLessOrEqualTo(
            ChatKnowledgeScope.MaxAdditionalDocuments,
            "provider must enforce the max cap of 5 additional documents");
    }

    [Fact]
    public async Task GetContextAsync_WithEmptyAdditionalDocumentList_ScopeHasNullAdditionalDocs()
    {
        // Arrange — empty list after normalization should become null
        var additionalDocs = new List<string>();
        var (sut, scopeResolverMock, _, _) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — empty list after normalization should result in null
        result.KnowledgeScope.Should().NotBeNull();
        result.KnowledgeScope!.AdditionalDocumentIds.Should().BeNull(
            "an empty additional document list should normalize to null");
    }

    [Fact]
    public async Task GetContextAsync_WithAdditionalDocs_StillReturnsPrimaryDocumentContext()
    {
        // Arrange — additional documents should not interfere with the primary document context
        var additionalDocs = new List<string> { "doc-extra-1" };
        var (sut, scopeResolverMock, _, dataverseServiceMock) = CreateProviderWithMocks();

        scopeResolverMock
            .Setup(s => s.ResolvePlaybookScopesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedScopes(
                Skills: new[]
                {
                    new AnalysisSkill { Id = Guid.NewGuid(), Name = "Skill", PromptFragment = "Fragment" }
                },
                Knowledge: Array.Empty<AnalysisKnowledge>(),
                Tools: Array.Empty<AnalysisTool>()));

        dataverseServiceMock
            .Setup(d => d.GetDocumentAsync(TestDocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentEntity
            {
                Id = TestDocumentId,
                Name = "Primary Contract.pdf",
                Summary = "A primary contract document.",
                DocumentType = "Contract"
            });

        // Act
        var result = await sut.GetContextAsync(
            TestDocumentId, TestTenantId, TestPlaybookId,
            additionalDocumentIds: additionalDocs);

        // Assert — primary document context should still be present
        result.DocumentSummary.Should().Be("A primary contract document.");
        result.AnalysisMetadata.Should().ContainKey("documentType");
    }

    #endregion

    #region ChatContext — validation integration

    [Fact]
    public void ChatContext_WithKnowledgeScope_PreservesAdditionalDocumentIds()
    {
        // Arrange
        var additionalIds = new List<string> { "doc-a", "doc-b" };
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-primary",
            AdditionalDocumentIds: additionalIds);

        // Act
        var context = new ChatContext(
            SystemPrompt: "You are a helpful assistant.",
            DocumentSummary: "Test summary.",
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId,
            KnowledgeScope: scope);

        // Assert
        context.KnowledgeScope.Should().NotBeNull();
        context.KnowledgeScope!.AdditionalDocumentIds.Should().BeEquivalentTo(additionalIds);
        context.KnowledgeScope.ValidateAdditionalDocuments().Should().BeTrue();
    }

    [Fact]
    public void ChatContext_WithExcessiveAdditionalDocs_FailsKnowledgeScopeValidation()
    {
        // Arrange
        var tooManyIds = Enumerable.Range(1, 10).Select(i => $"doc-{i}").ToList();
        var scope = new ChatKnowledgeScope(
            RagKnowledgeSourceIds: new List<string>(),
            InlineContent: null,
            SkillInstructions: null,
            ActiveDocumentId: "doc-primary",
            AdditionalDocumentIds: tooManyIds);

        // Act
        var context = new ChatContext(
            SystemPrompt: "You are a helpful assistant.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId,
            KnowledgeScope: scope);

        // Assert
        context.KnowledgeScope!.ValidateAdditionalDocuments().Should().BeFalse(
            "10 additional documents exceeds the MaxAdditionalDocuments cap of 5");
    }

    #endregion

    #region Private helpers

    /// <summary>
    /// Creates a PlaybookChatContextProvider with mocked dependencies.
    /// Returns the SUT and individual mocks for verification.
    /// </summary>
    private (
        PlaybookChatContextProvider sut,
        Mock<IScopeResolverService> scopeResolver,
        Mock<IPlaybookService> playbookService,
        Mock<IDataverseService> dataverseService)
        CreateProviderWithMocks()
    {
        var scopeResolverMock = new Mock<IScopeResolverService>();
        var playbookServiceMock = new Mock<IPlaybookService>();
        var dataverseServiceMock = new Mock<IDataverseService>();
        var loggerMock = new Mock<ILogger<PlaybookChatContextProvider>>();

        // Default: playbook has one action with a system prompt
        playbookServiceMock
            .Setup(p => p.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookResponse
            {
                Id = TestPlaybookId,
                Name = "Test Playbook",
                ActionIds = new[] { TestActionId },
                IsActive = true
            });

        scopeResolverMock
            .Setup(s => s.GetActionAsync(TestActionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction
            {
                Id = TestActionId,
                Name = "Test Action",
                SystemPrompt = "You are a test assistant."
            });

        var sut = new PlaybookChatContextProvider(
            scopeResolverMock.Object,
            playbookServiceMock.Object,
            dataverseServiceMock.Object,
            loggerMock.Object);

        return (sut, scopeResolverMock, playbookServiceMock, dataverseServiceMock);
    }

    #endregion
}
