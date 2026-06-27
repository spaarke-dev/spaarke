using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Memory;
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

    // ── R5 task 033 — UploadedFiles surfacing in ChatContext ──────────────────────

    /// <summary>
    /// When the session manifest carries uploaded files, the provider MUST surface them
    /// verbatim on the returned <see cref="ChatContext.UploadedFiles"/> so downstream
    /// agent construction (<see cref="SprkChatAgentFactory"/>) can build a Session Files
    /// manifest suffix on the system prompt. R5 task 033 acceptance criterion.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_SurfacesUploadedFiles_WhenSessionHasFiles()
    {
        // Arrange
        SetupEmptyScopes();

        var manifest = new List<ChatSessionFile>
        {
            new(
                FileId: "file-001",
                FileName: "contract.pdf",
                ContentType: "application/pdf",
                SizeBytes: 12_345,
                SearchDocumentIdsCsv: "idx-001",
                UploadedAt: DateTimeOffset.UtcNow),
            new(
                FileId: "file-002",
                FileName: "schedule.docx",
                ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                SizeBytes: 6_789,
                SearchDocumentIdsCsv: "idx-002,idx-003",
                UploadedAt: DateTimeOffset.UtcNow)
        };

        var sut = CreateProvider();

        // Act
        var context = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            hostContext: null,
            additionalDocumentIds: null,
            uploadedFiles: manifest,
            cancellationToken: CancellationToken.None);

        // Assert — manifest forwarded verbatim onto returned ChatContext.
        context.UploadedFiles.Should().NotBeNull();
        context.UploadedFiles!.Should().HaveCount(2);
        context.UploadedFiles.Should().Contain(f => f.FileId == "file-001" && f.FileName == "contract.pdf");
        context.UploadedFiles.Should().Contain(f => f.FileId == "file-002" && f.FileName == "schedule.docx");
    }

    /// <summary>
    /// When the session has no uploaded files (null or empty manifest), the returned
    /// <see cref="ChatContext.UploadedFiles"/> MUST be <c>null</c> — backward-compatible
    /// with pre-R5 callers and standalone chat. R5 task 033 acceptance criterion.
    /// </summary>
    [Fact]
    public async Task GetContextAsync_LeavesUploadedFilesNull_WhenSessionHasNone()
    {
        // Arrange
        SetupEmptyScopes();
        var sut = CreateProvider();

        // Act — case 1: null manifest (default param, mimicking pre-R5 callers)
        var contextWhenNull = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            cancellationToken: CancellationToken.None);

        // Act — case 2: explicit empty manifest (defensive: treated identically to null)
        var contextWhenEmpty = await sut.GetContextAsync(
            TestDocumentId,
            TestTenantId,
            TestPlaybookId,
            hostContext: null,
            additionalDocumentIds: null,
            uploadedFiles: Array.Empty<ChatSessionFile>(),
            cancellationToken: CancellationToken.None);

        // Assert — both treatments are equivalent: null surface on returned context.
        contextWhenNull.UploadedFiles.Should().BeNull();
        contextWhenEmpty.UploadedFiles.Should().BeNull();
    }

    // ─── FR-27 + FR-45 binding-invariant regression (chat-routing-redesign-r1 task 078) ──
    //
    // These tests pin two contracts the per-turn composition seam MUST never lose:
    //
    //   FR-45: PlaybookChatContextProvider.GetContextAsync MUST call
    //          IMatterMemoryService.ToSystemPromptFragmentAsync (cross-session matter
    //          memory activation). Architecture §11.1.
    //
    //   FR-27: there is exactly ONE per-turn composition seam — this method. There MUST
    //          NOT be a parallel composer in SprkChatAgentFactory that bypasses
    //          IChatContextProvider.GetContextAsync.
    //
    // Both tests use the source-file regex pattern (mirrors MatterPreFillServiceTests
    // NFR-07 invariants). Line numbers are intentionally NOT pinned — the invariant is
    // the call's existence, not its position. Task 080 escalates this with a stricter
    // regression test scoped to the binding pair.

    [Fact]
    public void GetContextAsync_PreservesMatterMemoryServiceInvocation_FR45()
    {
        // FR-45 BINDING: PlaybookChatContextProvider MUST invoke
        // IMatterMemoryService.ToSystemPromptFragmentAsync to activate cross-session
        // matter memory (architecture §11.1). Source-text check pins the invocation
        // so future refactors that accidentally drop the wiring fail this test loudly.
        var source = File.ReadAllText(LocatePlaybookChatContextProviderSource());
        source.Should().Contain("_matterMemoryService.ToSystemPromptFragmentAsync(",
            "FR-45 BINDING — chat-routing-redesign-r1 task 078: the per-turn composition " +
            "seam MUST call IMatterMemoryService.ToSystemPromptFragmentAsync to activate " +
            "cross-session matter memory. Architecture §11.1. Do NOT regress.");
    }

    [Fact]
    public void GetContextAsync_IsSinglePerTurnCompositionSeam_FR27()
    {
        // FR-27 BINDING: chat-routing-redesign-r1 requires "no parallel pipelines."
        // PlaybookChatContextProvider.GetContextAsync is the ONLY per-turn composition
        // seam. SprkChatAgentFactory must consume it via IChatContextProvider and only
        // append suffix blocks under the shared budget tracker — it must NOT contain
        // a parallel composer that bypasses the provider.
        //
        // Mechanism: verify the factory still resolves IChatContextProvider and calls
        // GetContextAsync (single composer wiring). If anyone introduces a second
        // composer in the factory, the call-site count grows or the seam name changes —
        // either drift fails this test.
        var factorySource = File.ReadAllText(LocateSprkChatAgentFactorySource());
        factorySource.Should().Contain("contextProvider.GetContextAsync(",
            "FR-27 BINDING — SprkChatAgentFactory MUST consume the single composition " +
            "seam via IChatContextProvider.GetContextAsync. A second composer in the " +
            "factory violates the single-pipeline contract.");
        factorySource.Should().Contain("GetRequiredService<IChatContextProvider>()",
            "FR-27 BINDING — SprkChatAgentFactory MUST resolve IChatContextProvider from " +
            "the per-turn scope (not bypass it via a direct composer). Architecture §4.3.");
    }

    private static string LocatePlaybookChatContextProviderSource()
        => LocateBffSource("Services", "Ai", "Chat", "PlaybookChatContextProvider.cs");

    private static string LocateSprkChatAgentFactorySource()
        => LocateBffSource("Services", "Ai", "Chat", "SprkChatAgentFactory.cs");

    private static string LocateBffSource(params string[] tailSegments)
    {
        // Resolve from the test assembly's parent directory tree. The test project lives
        // at tests/unit/Sprk.Bff.Api.Tests; the source lives at
        // src/server/api/Sprk.Bff.Api/Services/Ai/Chat/...
        var assemblyPath = typeof(PlaybookChatContextProviderTests).Assembly.Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "server")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("repo root must be locatable from the test assembly path");

        var segments = new[]
        {
            dir!.FullName, "src", "server", "api", "Sprk.Bff.Api"
        }.Concat(tailSegments).ToArray();

        var path = Path.Combine(segments);
        File.Exists(path).Should().BeTrue($"expected source file at {path}");
        return path;
    }

    #region Setup helpers

    private PlaybookChatContextProvider CreateProvider()
        => new(
            _scopeResolverMock.Object,
            _playbookServiceMock.Object,
            _dataverseServiceMock.Object,
            _loggerMock.Object,
            // R6 task 069 follow-up housekeeping: task 068 made IMatterMemoryService a
            // required ctor param without migrating this fixture. Pass a default mock so
            // the matter-memory append path is a no-op for these tests (no matter context
            // is set up in the test fixtures below).
            new Mock<IMatterMemoryService>().Object);

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
