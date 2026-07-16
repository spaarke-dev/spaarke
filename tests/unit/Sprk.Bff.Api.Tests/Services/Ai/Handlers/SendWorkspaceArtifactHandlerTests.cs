using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="SendWorkspaceArtifactHandler"/> (R6 Pillar 6b / task 054).
/// </summary>
/// <remarks>
/// Covers success path, missing required arguments, tenant isolation enforcement, and
/// downstream service failures degrading into a graceful tool-result error.
///
/// Mocks: <see cref="IWorkspaceStateService"/>, <see cref="IGuidProvider"/>,
/// <see cref="TimeProvider"/>. Pattern after KnowledgeRetrievalHandlerTests.
/// </remarks>
public sealed class SendWorkspaceArtifactHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly Guid DeterministicTabGuid = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ComposeLayoutId = new("c09d26be-e173-f111-ab0e-7ced8ddc4a05");
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IGuidProvider> _guidProvider = new();
    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);
    // R4-2 (2026-07-07): user-OBO Dataverse client for the Compose pre-seed
    // sprk_document → SPE pointer resolution.
    private readonly Mock<Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.IDataverseUserClient> _dataverse = new();
    // D-F3 UI-action truthfulness (FR-A1-08 / task AIR2-037): defaults to immediate
    // Acknowledged so every pre-existing success-path test (authored before ack-gating
    // landed) keeps passing without per-test setup. Ack-timeout behavior is exercised by
    // its own dedicated facts below, which override this default.
    private readonly Mock<IUiActionAckCoordinator> _ackCoordinator = new();

    public SendWorkspaceArtifactHandlerTests()
    {
        _guidProvider.Setup(g => g.NewGuid()).Returns(DeterministicTabGuid);
        // Default: no Dataverse layouts — GetLayoutsAsync still yields the hard-coded
        // system layouts. Individual facts add the "Compose" system row when needed.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection());
        _ackCoordinator
            .Setup(a => a.WaitForAckAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UiActionAckOutcome.Acknowledged);
    }

    /// <summary>Adds the "Compose" system layout row to the mocked Dataverse layout query.</summary>
    private void SeedComposeSystemLayout()
    {
        var entity = new Microsoft.Xrm.Sdk.Entity("sprk_workspacelayout", ComposeLayoutId);
        entity["sprk_workspacelayoutid"] = ComposeLayoutId;
        entity["sprk_name"] = "Compose";
        entity["sprk_issystem"] = true;
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection(new List<Microsoft.Xrm.Sdk.Entity> { entity }));
    }

    // task 113: real ChatSessionManager over an in-memory tenant cache — the handler reads the
    // session-scoped ActiveDocument to resolve the Compose mount when the LLM sends no pointer.
    // Empty by default (GetSessionAsync → null), so pre-existing tests keep their behavior; a test
    // that needs an active document seeds one via SeedActiveDocumentAsync below.
    private readonly ChatSessionManager _sessionManager = new(
        new InMemoryTenantCache(),
        Mock.Of<IChatDataverseRepository>(),
        Mock.Of<ILogger<ChatSessionManager>>());

    private SendWorkspaceArtifactHandler CreateHandler() => new(
        _guidProvider.Object,
        _timeProvider,
        new WorkspaceLayoutService(_entityService.Object, CreateLogger<WorkspaceLayoutService>()),
        _dataverse.Object,
        _ackCoordinator.Object,
        _sessionManager,
        CreateLogger<SendWorkspaceArtifactHandler>());

    private static AnalysisTool BuildArtifactTool() =>
        BuildAnalysisTool(handlerClass: nameof(SendWorkspaceArtifactHandler), toolType: ToolType.Custom);

    // ═════════════════════════════════════════════════════════════════════════════
    // Playbook-context rejection (chat-only handler)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_Playbook_ReturnsValidationError()
    {
        var handler = CreateHandler();
        var ctx = BuildToolExecutionContext();
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed,
            because: "the handler is chat-context-only; playbook invocation must be rejected");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Workspace layout tab — live open (G-P3 UAT round-2 R2-D, 2026-07-07)
    // ═════════════════════════════════════════════════════════════════════════════

    private static string BuildWorkspaceArgsJson(string layoutName = "Compose", string title = "Compose") =>
        $$"""
          {
            "widgetType": "Workspace",
            "title": "{{title}}",
            "widgetData": {
              "kind": "Workspace",
              "layoutName": "{{layoutName}}"
            }
          }
          """;

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_EmitsWorkspaceOpenTabFrame_AndSkipsStateUpsert()
    {
        SeedComposeSystemLayout();
        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJson()) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("Opened the 'Compose' workspace",
            because: "the tool result is the model's ONLY grounding for a UI-action claim (R2-D)");

        emitted.Should().HaveCount(1);
        emitted[0].Type.Should().Be("context_event");
        var dto = emitted[0].Data.Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        dto.ContextEventType.Should().Be(SendWorkspaceArtifactHandler.WorkspaceOpenTabEventType);
        dto.ContextWidgetType.Should().Be(SendWorkspaceArtifactHandler.ClientComposeWidgetKey,
            because: "spaarkeai-compose-r2: a Compose mount dispatches the first-class DIRECT 'compose' widget so ComposeWorkspace mounts unconditionally (never LegalWorkspaceApp/dashboard)");
        dto.ContextDisplayName.Should().Be("Compose");
        dto.ContextTabId.Should().Be(DeterministicTabGuid.ToString("N"));

        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        widgetData.RootElement.GetProperty("layoutId").GetString().Should().Be(ComposeLayoutId.ToString("D"));
        widgetData.RootElement.GetProperty("layoutName").GetString().Should().Be("Compose");
    }

    [Fact]
    public async Task ExecuteChatAsync_NonComposeLayout_KeepsWorkspaceLayoutDoor()
    {
        // Guardrail (spaarkeai-compose-r2): ONLY the Compose layout flips to the DIRECT
        // 'compose' widget. A non-Compose layout (the hard-coded "Corporate Workspace"
        // system row) MUST keep the LAYOUT door ('workspace') so it still mounts via
        // LegalWorkspaceApp — the flip must not regress Daily Briefing / other layouts.
        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildWorkspaceArgsJson(layoutName: "Corporate Workspace", title: "Corporate Workspace")) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        var dto = emitted.Should().ContainSingle().Subject.Data
            .Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        dto.ContextWidgetType.Should().Be(SendWorkspaceArtifactHandler.ClientWorkspaceWidgetKey,
            because: "a non-Compose layout keeps the LAYOUT door — only Compose flips to the DIRECT 'compose' widget");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // D-F3 UI-action truthfulness — client-ack gating (FR-A1-08 / NFR-08 / task AIR2-037)
    //
    // The tool result must complete ONLY on a client ack referencing the emitted frame
    // id (the SSE frame's tabId), or fail honestly on timeout (R2-D: no fabricated
    // "I opened the tab" when no backing client event exists).
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_WaitsForAck_ReferencingTheEmittedFrameId()
    {
        SeedComposeSystemLayout();
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJson()) with
        {
            SseWriter = (_, _) => Task.CompletedTask
        };
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        // The frame id the ack MUST reference is the SAME tabId the SSE frame carried —
        // never a different/local identifier (that would let a stale or unrelated ack
        // falsely complete this tool result).
        _ackCoordinator.Verify(
            a => a.WaitForAckAsync(
                ctx.ChatSessionId.ToString("N"),
                DeterministicTabGuid.ToString("N"),
                SendWorkspaceArtifactHandler.WorkspaceTabAckTimeout,
                It.IsAny<CancellationToken>()),
            Times.Once,
            failMessage: "the tool result must gate on an ack referencing the exact frame id emitted on the SSE frame");
    }

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_AckTimesOut_FailsHonestly_NeverClaimsTabOpened()
    {
        SeedComposeSystemLayout();
        _ackCoordinator
            .Setup(a => a.WaitForAckAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UiActionAckOutcome.TimedOut);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJson()) with
        {
            SseWriter = (_, _) => Task.CompletedTask
        };
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        // R2-D structural prevention: NO client ack within the timeout means the tool
        // result MUST be an honest failure — never the success summary a fabricating
        // model would relay as "I opened the tab".
        result.Success.Should().BeFalse(
            because: "FR-A1-08: an un-acked UI-affecting tool call must fail honestly, not fabricate success");
        result.ErrorCode.Should().Be(ToolErrorCodes.Timeout);
        result.ErrorMessage.Should().Contain("Could not confirm");
        result.ErrorMessage.Should().NotContain("Opened the",
            because: "the honest-failure path must never emit the success-claim wording (negative R2-D guard)");
    }

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_WithoutSseWriter_FailsHonest()
    {
        SeedComposeSystemLayout();
        var handler = CreateHandler();
        // No SseWriter on the context — the tab CANNOT reach the client.
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJson());
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("NOT opened",
            because: "fail-honest: without a live SSE stream the model must never be told the tab opened");
    }

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_UnknownLayoutName_ReturnsAvailableLayoutNames()
    {
        // Only the hard-coded system layouts exist (no Dataverse rows seeded).
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJson(layoutName: "Nonexistent")) with
        {
            SseWriter = (_, _) => Task.CompletedTask
        };
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("No workspace layout named 'Nonexistent'");
        result.ErrorMessage.Should().Contain("Available layouts:",
            because: "the model needs the real layout names to ground a retry or an honest answer");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Compose document pre-seed (G-P3 UAT round-4 R4-2, 2026-07-07)
    // ═════════════════════════════════════════════════════════════════════════════

    private static string BuildWorkspaceArgsJsonWithDocument(string documentId) =>
        $$"""
          {
            "widgetType": "Workspace",
            "title": "Compose",
            "widgetData": {
              "kind": "Workspace",
              "layoutName": "Compose",
              "documentId": "{{documentId}}"
            }
          }
          """;

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_WithResolvableDocument_CarriesComposeSeedInWidgetData()
    {
        SeedComposeSystemLayout();
        var documentId = Guid.Parse("d0c00000-1111-2222-3333-444444444444");
        _dataverse
            .Setup(d => d.GetAsync(It.Is<string>(p => p.Contains($"sprk_documents({documentId:D})")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.DataverseUserResponse.Ok(200,
                JsonSerializer.SerializeToElement(new
                {
                    sprk_documentid = documentId.ToString("D"),
                    sprk_documentname = "NDA - Acme.docx",
                    sprk_filename = "nda-acme.docx",
                    sprk_graphdriveid = "b!driveId",
                    sprk_graphitemid = "01ITEMID",
                })));

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJsonWithDocument(documentId.ToString("D"))) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };

        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildArtifactTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("pre-seeded",
            because: "the tool result grounds the model's claim that Compose opened WITH the document");

        var dto = emitted.Should().ContainSingle().Subject.Data
            .Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        var compose = widgetData.RootElement.GetProperty("compose");
        compose.GetProperty("sprkDocumentId").GetString().Should().Be(documentId.ToString("D"));
        compose.GetProperty("speDriveItemId").GetString().Should().Be("01ITEMID");
        compose.GetProperty("speDriveId").GetString().Should().Be("b!driveId");
        compose.GetProperty("fileName").GetString().Should().Be("NDA - Acme.docx");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // FR-03 (spaarkeai-compose-r2 task 012) — session-uploaded file transient mount
    // ═════════════════════════════════════════════════════════════════════════════

    private static string BuildWorkspaceArgsJsonWithSessionFile(string sessionFileId) =>
        $$"""
          {
            "widgetType": "Workspace",
            "title": "Compose",
            "widgetData": {
              "kind": "Workspace",
              "layoutName": "Compose",
              "sessionFileId": "{{sessionFileId}}"
            }
          }
          """;

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_WithSessionFileId_CarriesUploadSeed_AndNoStoredDocumentSeed()
    {
        SeedComposeSystemLayout();
        const string sessionFileId = "a1b2c3d4e5f60718293a4b5c6d7e8f90";

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJsonWithSessionFile(sessionFileId)) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };

        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildArtifactTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("transient working draft",
            because: "FR-03: an uploaded file mounts transiently (create-on-save); the tool result grounds that claim");

        var dto = emitted.Should().ContainSingle().Subject.Data
            .Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        var compose = widgetData.RootElement.GetProperty("compose");
        var upload = compose.GetProperty("upload");
        upload.GetProperty("sessionFileId").GetString().Should().Be(sessionFileId);
        upload.GetProperty("sessionId").GetString().Should().Be(ctx.ChatSessionId.ToString("D"),
            because: "the client fetches the retained bytes from POST /api/compose/upload keyed by the chat session id");

        // The upload path carries NO stored-document (sprk_document / SPE) seed — those are
        // mutually exclusive with the transient upload mount.
        compose.TryGetProperty("sprkDocumentId", out _).Should().BeFalse();
        compose.TryGetProperty("speDriveItemId", out _).Should().BeFalse();

        // Deterministic file handling — no Dataverse resolution on the upload path (ADR-013).
        _dataverse.Verify(
            d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ValidateChat_Workspace_WithBothDocumentIdAndSessionFileId_ReturnsFailure()
    {
        var argsJson = $$"""
          {
            "widgetType": "Workspace",
            "title": "Compose",
            "widgetData": {
              "kind": "Workspace",
              "layoutName": "Compose",
              "documentId": "d0c00000-1111-2222-3333-444444444444",
              "sessionFileId": "a1b2c3d4e5f60718293a4b5c6d7e8f90"
            }
          }
          """;
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);

        var result = CreateHandler().ValidateChat(ctx, BuildArtifactTool());

        result.IsValid.Should().BeFalse(
            because: "documentId (a stored sprk_document) and sessionFileId (an uploaded chat file) are mutually exclusive");
    }

    [Fact]
    public void ValidateChat_Workspace_WithSessionFileIdOnly_Succeeds()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildWorkspaceArgsJsonWithSessionFile("a1b2c3d4e5f60718293a4b5c6d7e8f90"));

        var result = CreateHandler().ValidateChat(ctx, BuildArtifactTool());

        result.IsValid.Should().BeTrue(
            because: "FR-03 removes the session-upload refusal for the Compose-mount path");
    }

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_DocumentWithoutStoredFile_OpensTabEmpty_AndSaysSoHonestly()
    {
        SeedComposeSystemLayout();
        var documentId = Guid.NewGuid();
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.DataverseUserResponse.Ok(200,
                JsonSerializer.SerializeToElement(new
                {
                    sprk_documentid = documentId.ToString("D"),
                    sprk_documentname = "Metadata-only doc",
                    // no sprk_graphitemid — a fileless sprk_document row
                })));

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJsonWithDocument(documentId.ToString("D"))) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };

        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildArtifactTool(), CancellationToken.None);

        // The TAB still opens (partial success) — the pre-seed failure is stated honestly.
        result.Success.Should().BeTrue();
        result.Summary.Should().Contain("EMPTY",
            because: "fail-honest: the model must relay that the document could not be loaded");
        result.Summary.Should().Contain("NO stored file");

        var dto = emitted.Should().ContainSingle().Subject.Data
            .Should().BeOfType<Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseEventDto>().Subject;
        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        widgetData.RootElement.TryGetProperty("compose", out _).Should().BeFalse(
            because: "no seed rides the frame when the document has no SPE file");
    }

    [Fact]
    public async Task ExecuteChatAsync_WorkspaceLayout_DocumentNotAccessible_OpensTabEmpty_AndSaysSoHonestly()
    {
        SeedComposeSystemLayout();
        _dataverse
            .Setup(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sprk.Bff.Api.Services.Ai.Handlers.Dataverse.DataverseUserResponse.Fail(
                404, "DATAVERSE_NOT_FOUND", "The record was not found (or you lack access)."));

        var emitted = new List<Sprk.Bff.Api.Api.Ai.ChatSseEvent>();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildWorkspaceArgsJsonWithDocument(Guid.NewGuid().ToString("D"))) with
        {
            SseWriter = (evt, _) => { emitted.Add(evt); return Task.CompletedTask; }
        };

        var result = await CreateHandler().ExecuteChatAsync(ctx, BuildArtifactTool(), CancellationToken.None);

        result.Success.Should().BeTrue("the tab itself opened — only the pre-seed degraded");
        result.Summary.Should().Contain("EMPTY");
        result.Summary.Should().Contain("not found or not accessible");
    }

    [Fact]
    public void ValidateChat_WorkspaceKind_NonGuidDocumentId_RejectedWithSessionFileGuidance()
    {
        // Session-uploaded chat files (fileId shape, not a GUID) carry NO SPE pointer —
        // the pre-seed is genuinely impossible for them (round-2/round-4 reality check).
        var handler = CreateHandler();
        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace","layoutName":"Compose","documentId":"session-file-abc123"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);

        var result = handler.ValidateChat(ctx, BuildArtifactTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("sessionFileId", StringComparison.Ordinal),
            because: "the rejection teaches the model the honest alternative — mount the upload via widgetData.sessionFileId");
    }

    [Fact]
    public void ValidateChat_WorkspaceKind_MissingLayout_IsNowValid_DefaultsToComposeAtExecute()
    {
        // task 113 (UAT defects 6/7): layoutName/layoutId are NO LONGER required at validation.
        // A missing layout passes ValidateChat and the execute path defaults it to 'Compose'
        // (proven end-to-end in the seam suite), so a literal-following model never has to
        // synthesize a layout id. This supersedes the prior "layout required" contract.
        var handler = CreateHandler();
        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var tool = BuildArtifactTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeTrue(
            because: "R5 removed the hard layout requirement; the execute path defaults a missing layout to 'Compose'");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Test helper: deterministic TimeProvider
    // ═════════════════════════════════════════════════════════════════════════════

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
