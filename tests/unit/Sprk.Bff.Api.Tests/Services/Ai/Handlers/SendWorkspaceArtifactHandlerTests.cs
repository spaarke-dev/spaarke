using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Workspace;
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

    private readonly Mock<IWorkspaceStateService> _workspaceStateService = new();
    private readonly Mock<IGuidProvider> _guidProvider = new();
    private readonly Mock<IGenericEntityService> _entityService = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);

    public SendWorkspaceArtifactHandlerTests()
    {
        _guidProvider.Setup(g => g.NewGuid()).Returns(DeterministicTabGuid);
        // Default: no Dataverse layouts — GetLayoutsAsync still yields the hard-coded
        // system layouts. Individual facts add the "Compose" system row when needed.
        _entityService
            .Setup(s => s.RetrieveMultipleAsync(
                It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.EntityCollection());
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

    private SendWorkspaceArtifactHandler CreateHandler() => new(
        _workspaceStateService.Object,
        _guidProvider.Object,
        _timeProvider,
        new WorkspaceLayoutService(_entityService.Object, CreateLogger<WorkspaceLayoutService>()),
        CreateLogger<SendWorkspaceArtifactHandler>());

    private static AnalysisTool BuildArtifactTool() =>
        BuildAnalysisTool(handlerClass: nameof(SendWorkspaceArtifactHandler), toolType: ToolType.Custom);

    private static string BuildSummaryArgsJson(string? title = "Untitled", string? matterId = null) =>
        $$"""
          {
            "widgetType": "Summary",
            "title": "{{title}}",
            "widgetData": {
              "kind": "Summary",
              "body": "Body text"
            }{{(matterId is null ? "" : $",\"matterId\":\"{matterId}\"")}}
          }
          """;

    // ═════════════════════════════════════════════════════════════════════════════
    // Success path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Succeeds_AndPersistsTab_OnHappyPath()
    {
        WorkspaceTab? capturedTab = null;
        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, WorkspaceTab, CancellationToken>((_, _, tab, _) => capturedTab = tab)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildSummaryArgsJson(title: "Engagement Summary"));
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedTab.Should().NotBeNull();
        capturedTab!.WidgetType.Should().Be("Summary");
        capturedTab.TenantId.Should().Be(DefaultTenantId);
        capturedTab.VisibleToAssistant.Should().BeTrue(because: "agent-created tabs default to visible per Pillar 9");
        capturedTab.IsPinned.Should().BeFalse();
        capturedTab.CanEdit.Should().BeFalse(because: "agent-created tabs default to non-editable");

        var payload = result.GetData<SendWorkspaceArtifactHandler.SendWorkspaceArtifactPayload>();
        payload.Should().NotBeNull();
        payload!.TabId.Should().Be(DeterministicTabGuid.ToString("N"));
        payload.WidgetType.Should().Be("Summary");
        payload.Title.Should().Be("Engagement Summary");

        // G-P3 UAT round-2 R2-D honesty pin (2026-07-07): the legacy artifact
        // variants persist to a store the post-046 client never reads — the
        // result the MODEL sees must not claim a visible tab (the round-2
        // fabrications built on over-claiming tool results).
        result.Summary.Should().Contain("NOT visible");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Missing required parameter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Fails_WhenWidgetTypeMissing()
    {
        var handler = CreateHandler();
        // Missing widgetType.
        const string argsJson = """{"title":"X","widgetData":{"kind":"Summary","body":"b"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("widgetType");

        _workspaceStateService.Verify(
            s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Rationale: no persistence should occur when arg validation fails.
    }

    [Fact]
    public void ValidateChat_Fails_WhenTitleMissing()
    {
        var handler = CreateHandler();
        const string argsJson = """{"widgetType":"Summary","widgetData":{"kind":"Summary","body":"b"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var tool = BuildArtifactTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Tenant isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ForwardsTenantId_ToWorkspaceStateService()
    {
        string? capturedTenant = null;
        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, WorkspaceTab, CancellationToken>((tenant, _, _, _) => capturedTenant = tenant)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildSummaryArgsJson(),
            tenantId: "tenant-isolated-42");
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedTenant.Should().Be("tenant-isolated-42",
            because: "ADR-014: every workspace-state call must carry the invoking tenant id");
    }

    [Fact]
    public void ValidateChat_Fails_WhenTenantIdMissing()
    {
        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildSummaryArgsJson(),
            tenantId: "");
        var tool = BuildArtifactTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("TenantId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Downstream service failure
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenWorkspaceServiceThrows()
    {
        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis unavailable"));

        var handler = CreateHandler();
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildSummaryArgsJson());
        var tool = BuildArtifactTool();

        var result = await handler.ExecuteChatAsync(ctx, tool, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError,
            because: "downstream persistence failures must surface as graceful tool errors, not propagated exceptions");
    }

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
        dto.ContextWidgetType.Should().Be(SendWorkspaceArtifactHandler.ClientWorkspaceWidgetKey,
            because: "the client registry key for a layout tab is the plain 'workspace' widget");
        dto.ContextDisplayName.Should().Be("Compose");
        dto.ContextTabId.Should().Be(DeterministicTabGuid.ToString("N"));

        using var widgetData = JsonDocument.Parse(dto.ContextWidgetDataJson!);
        widgetData.RootElement.GetProperty("layoutId").GetString().Should().Be(ComposeLayoutId.ToString("D"));
        widgetData.RootElement.GetProperty("layoutName").GetString().Should().Be("Compose");

        // The client owns tab persistence (PATCH /sessions/{id}/tabs); the orphaned
        // R6 workspace state store is NOT written for layout tabs.
        _workspaceStateService.Verify(
            s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    [Fact]
    public void ValidateChat_WorkspaceKind_RequiresLayoutNameOrLayoutId()
    {
        var handler = CreateHandler();
        const string argsJson =
            """{"widgetType":"Workspace","title":"Compose","widgetData":{"kind":"Workspace"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);
        var tool = BuildArtifactTool();

        var result = handler.ValidateChat(ctx, tool);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("layoutName", StringComparison.Ordinal));
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
