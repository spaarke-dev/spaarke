using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// task 113 (UAT-R4/R5 defects 5 + 6/7) vertical-slice seam tests — the definition-of-done for the
/// session-scoped ACTIVE-DOCUMENT bridge in the chat→Compose direction. The REAL
/// <see cref="SendWorkspaceArtifactHandler"/> is driven over the REAL
/// <see cref="ChatSessionManager"/> (production type, over an in-memory tenant cache with
/// production JSON serialization) and the REAL <see cref="WorkspaceLayoutService"/>. Only the
/// external boundaries are doubled: the UI-action ack coordinator, the user-OBO Dataverse client
/// (stored-doc resolution), and the layout data source. A contract-shape test would NOT satisfy
/// this DoD — the handler must actually resolve the mount seed FROM the session's active document
/// and emit the workspace_open_tab frame carrying it (ADR-043 seam-category governance).
/// </summary>
/// <remarks>
/// Defect 5 = "edit in Compose" after a fresh upload must mount THAT upload, not a stale stored
/// doc — proven by seeding a session <see cref="ActiveDocumentIdentity"/> and asserting the emitted
/// frame's compose seed points at it when the LLM supplies NO explicit pointer. Defect 6/7 = an
/// open-tab call targeting Compose with NO layout arg must default to 'Compose' and mount — proven
/// by omitting layoutName/layoutId and asserting the resolved layout is Compose.
/// </remarks>
public sealed class ActiveDocumentBridgeSeamTests : TypedToolHandlerTestFixture
{
    private static readonly Guid DeterministicTabGuid = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ComposeLayoutId = new("c09d26be-e173-f111-ab0e-7ced8ddc4a05");
    private static readonly DateTimeOffset DeterministicNow = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private const string TenantId = "tenant-active-doc-seam";
    private const string ActiveSessionFileId = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

    private readonly Mock<IGuidProvider> _guidProvider = new();
    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly Mock<IDataverseUserClient> _dataverse = new();
    private readonly Mock<IUiActionAckCoordinator> _ackCoordinator = new();
    private readonly ChatSessionManager _sessions = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    public ActiveDocumentBridgeSeamTests()
    {
        _guidProvider.Setup(g => g.NewGuid()).Returns(DeterministicTabGuid);
        // Seed the "Compose" system layout so WorkspaceLayoutService resolves it by name.
        var entity = new Microsoft.Xrm.Sdk.Entity("sprk_workspacelayout", ComposeLayoutId);
        entity["sprk_workspacelayoutid"] = ComposeLayoutId;
        entity["sprk_name"] = "Compose";
        entity["sprk_issystem"] = true;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection(
                new List<Microsoft.Xrm.Sdk.Entity> { entity }));
        _ackCoordinator
            .Setup(a => a.WaitForAckAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UiActionAckOutcome.Acknowledged);
    }

    private SendWorkspaceArtifactHandler CreateHandler() => new(
        _guidProvider.Object,
        new FixedTimeProvider(DeterministicNow),
        new WorkspaceLayoutService(_entityService.Object, CreateLogger<WorkspaceLayoutService>()),
        _dataverse.Object,
        _ackCoordinator.Object,
        _sessions,
        CreateLogger<SendWorkspaceArtifactHandler>());

    private static AnalysisTool Tool() =>
        BuildAnalysisTool(handlerClass: nameof(SendWorkspaceArtifactHandler), toolType: ToolType.Custom);

    /// <summary>Seeds a chat session (keyed by the "N" form the manager uses) carrying the given active document.</summary>
    private async Task<Guid> SeedSessionWithActiveDocumentAsync(
        ActiveDocumentIdentity activeDocument, ChatSessionFile? uploadedFile = null)
    {
        var sessionGuid = Guid.NewGuid();
        var session = new ChatSession(
            SessionId: sessionGuid.ToString("N"),
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DeterministicNow,
            LastActivity: DeterministicNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: uploadedFile is null ? null : new[] { uploadedFile })
        {
            ActiveDocument = activeDocument,
        };
        await _sessions.UpdateSessionCacheAsync(session);
        return sessionGuid;
    }

    private ChatInvocationContext ContextFor(Guid sessionGuid, string argsJson, List<Sprk.Bff.Api.Api.Ai.ChatSseEvent> sink) =>
        BuildChatInvocationContext(toolArgumentsJson: argsJson, tenantId: TenantId) with
        {
            ChatSessionId = sessionGuid,
            SseWriter = (evt, _) => { sink.Add(evt); return Task.CompletedTask; },
        };

    private static JsonElement ComposeSeed(Sprk.Bff.Api.Api.Ai.ChatSseEvent frame)
    {
        var dto = frame.Data.Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        return widgetData.RootElement.Clone();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (1) DEFECT 5 — no explicit pointer → mount resolves from the active session-upload
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_NoExplicitPointer_ResolvesComposeMount_FromActiveSessionUpload()
    {
        var sessionGuid = await SeedSessionWithActiveDocumentAsync(
            new ActiveDocumentIdentity(
                Source: ActiveDocumentIdentity.SourceComposeDirect,
                SessionFileId: ActiveSessionFileId,
                FileName: "just-uploaded.docx",
                RegisteredAt: DeterministicNow),
            uploadedFile: new ChatSessionFile(
                FileId: ActiveSessionFileId,
                FileName: "just-uploaded.docx",
                ContentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                SizeBytes: 128,
                SearchDocumentIdsCsv: $"{ActiveSessionFileId}_s_0",
                UploadedAt: DeterministicNow)
            {
                ExtractedText = "The just-uploaded document body.",
            });

        // "edit in Compose" with the layout named but NO documentId / NO sessionFileId — exactly
        // the case where the LLM lost the pointer after a fresh upload (UAT defect 5).
        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace","layoutName":"Compose"}}""";

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var result = await CreateHandler().ExecuteChatAsync(
            ContextFor(sessionGuid, argsJson, emitted), Tool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var compose = ComposeSeed(emitted.Should().ContainSingle().Subject).GetProperty("compose");
        compose.GetProperty("upload").GetProperty("sessionFileId").GetString().Should().Be(ActiveSessionFileId,
            because: "with no explicit pointer the handler resolves the mount from the session's ACTIVE document — the just-uploaded file, not a stale stored one (defect 5)");
        compose.GetProperty("upload").GetProperty("sessionId").GetString().Should().Be(sessionGuid.ToString("D"));
        // Deterministic upload mount — no stored-document Dataverse resolution on this path.
        _dataverse.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (2) DEFECT 5 — a stored active document resolves to the Compose SPE pre-seed
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_NoExplicitPointer_ResolvesComposeMount_FromActiveStoredDocument()
    {
        var storedDocId = Guid.Parse("d0c00000-aaaa-bbbb-cccc-444444444444");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains($"sprk_documents({storedDocId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new
            {
                sprk_documentid = storedDocId.ToString("D"),
                sprk_documentname = "Active-NDA.docx",
                sprk_graphdriveid = "b!activeDrive",
                sprk_graphitemid = "01ACTIVEITEM",
            })));

        var sessionGuid = await SeedSessionWithActiveDocumentAsync(
            new ActiveDocumentIdentity(
                Source: ActiveDocumentIdentity.SourceStored,
                SprkDocumentId: storedDocId.ToString("D"),
                RegisteredAt: DeterministicNow));

        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace","layoutName":"Compose"}}""";

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var result = await CreateHandler().ExecuteChatAsync(
            ContextFor(sessionGuid, argsJson, emitted), Tool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var compose = ComposeSeed(emitted.Should().ContainSingle().Subject).GetProperty("compose");
        compose.GetProperty("sprkDocumentId").GetString().Should().Be(storedDocId.ToString("D"),
            because: "a stored active document resolves to the Compose SPE pre-seed under OBO (defect 5, stored variant)");
        compose.GetProperty("speDriveItemId").GetString().Should().Be("01ACTIVEITEM");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (3) DEFECT 6/7 — NO layout arg defaults to 'Compose' and mounts
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_NoLayoutArg_DefaultsToCompose_AndMountsActiveDocument()
    {
        var sessionGuid = await SeedSessionWithActiveDocumentAsync(
            new ActiveDocumentIdentity(
                Source: ActiveDocumentIdentity.SourceComposeDirect,
                SessionFileId: ActiveSessionFileId,
                FileName: "another.docx",
                RegisteredAt: DeterministicNow));

        // "open the file in compose tab" after closing tabs — NO layoutName, NO layoutId, NO pointer.
        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace"}}""";

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var result = await CreateHandler().ExecuteChatAsync(
            ContextFor(sessionGuid, argsJson, emitted), Tool(), CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "R5: a missing layout defaults to 'Compose' rather than erroring — no layout-id prompt (defects 6/7)");
        result.Summary.Should().Contain("Compose");

        var frame = emitted.Should().ContainSingle().Subject;
        var dto = frame.Data.Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        dto.ContextDisplayName.Should().Be("Compose");
        var root = ComposeSeed(frame);
        root.GetProperty("layoutName").GetString().Should().Be("Compose");
        root.GetProperty("layoutId").GetString().Should().Be(ComposeLayoutId.ToString("D"));
        // And the active document still resolves onto the defaulted-Compose tab (defects 5 + 6/7 together).
        root.GetProperty("compose").GetProperty("upload").GetProperty("sessionFileId").GetString()
            .Should().Be(ActiveSessionFileId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (4) PRESERVED — an EXPLICIT pointer from the LLM overrides the active document
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteChatAsync_ExplicitDocumentId_OverridesActiveDocument_StoredOpenThisSpecificDoc()
    {
        var explicitDocId = Guid.Parse("e0e00000-1234-5678-9abc-def012345678");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains($"sprk_documents({explicitDocId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new
            {
                sprk_documentid = explicitDocId.ToString("D"),
                sprk_documentname = "Explicit-Choice.docx",
                sprk_graphdriveid = "b!explicitDrive",
                sprk_graphitemid = "01EXPLICITITEM",
            })));

        // The session's ACTIVE document is an upload — but the LLM explicitly asks for a stored doc.
        var sessionGuid = await SeedSessionWithActiveDocumentAsync(
            new ActiveDocumentIdentity(
                Source: ActiveDocumentIdentity.SourceComposeDirect,
                SessionFileId: ActiveSessionFileId,
                RegisteredAt: DeterministicNow));

        var argsJson =
            "{\"widgetType\":\"Workspace\",\"title\":\"Compose\",\"widgetData\":{\"kind\":\"Workspace\",\"layoutName\":\"Compose\",\"documentId\":\""
            + explicitDocId.ToString("D") + "\"}}";

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var result = await CreateHandler().ExecuteChatAsync(
            ContextFor(sessionGuid, argsJson, emitted), Tool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var compose = ComposeSeed(emitted.Should().ContainSingle().Subject).GetProperty("compose");
        compose.GetProperty("sprkDocumentId").GetString().Should().Be(explicitDocId.ToString("D"),
            because: "an explicit pointer is the 'open this specific doc' override — the active document must NOT win over it");
        compose.TryGetProperty("upload", out _).Should().BeFalse(
            because: "the explicit stored pointer wins; the active upload is not mounted");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
