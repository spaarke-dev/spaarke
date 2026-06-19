using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="UpdateWorkspaceTabHandler"/> (R6 Pillar 6b / task 055).
/// </summary>
/// <remarks>
/// Covers success path, missing required argument, tenant isolation, downstream failure,
/// and Q8 stale-write refusal (USER WINS conflict resolution).
/// </remarks>
public sealed class UpdateWorkspaceTabHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceStateService> _workspaceStateService = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);

    private UpdateWorkspaceTabHandler CreateHandler() => new(
        _workspaceStateService.Object,
        _timeProvider,
        CreateLogger<UpdateWorkspaceTabHandler>());

    private static AnalysisTool BuildUpdateTool() =>
        BuildAnalysisTool(handlerClass: nameof(UpdateWorkspaceTabHandler), toolType: ToolType.Custom);

    private static WorkspaceTab BuildExistingTab(
        string tabId,
        string tenantId,
        string sessionId,
        string? lastUserEditAt = null,
        bool canEdit = true)
    {
        return new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData { Body = "original body" },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "agent:test",
                CreatedAt = "2026-06-01T00:00:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = canEdit,
            LastUserEditAt = lastUserEditAt,
            CreatedAt = "2026-06-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z"
        };
    }

    private static string BuildArgsJson(string tabId, string? expectedLastUserEditAt = null)
    {
        var expectedFragment = expectedLastUserEditAt is null
            ? ""
            : $",\"expectedLastUserEditAt\":\"{expectedLastUserEditAt}\"";
        return $$"""
                 {
                   "tabId": "{{tabId}}",
                   "widgetData": { "kind": "Summary", "body": "new body" }
                   {{expectedFragment}}
                 }
                 """;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Success path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Succeeds_AndPersistsMutation_OnHappyPath()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildExistingTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        WorkspaceTab? captured = null;
        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(ctx.TenantId, sessionId, It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, WorkspaceTab, CancellationToken>((_, _, t, _) => captured = t)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        payload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusApplied);
        captured.Should().NotBeNull();
        captured!.WidgetData.Should().BeOfType<SummaryTabWidgetData>();
        ((SummaryTabWidgetData)captured.WidgetData).Body.Should().Be("new body");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Missing required parameter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Fails_WhenTabIdMissing()
    {
        const string argsJson = """{"widgetData":{"kind":"Summary","body":"x"}}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildUpdateTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("tabId", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Tenant isolation
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ForwardsTenantId_ToWorkspaceState()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1"),
            tenantId: "tenant-iso");
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildExistingTab("tab-1", "tenant-iso", sessionId);

        string? capturedTenant = null;
        _workspaceStateService
            .Setup(s => s.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((tenant, _, _) => capturedTenant = tenant)
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        capturedTenant.Should().Be("tenant-iso");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Downstream failure
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenWorkspaceStateThrowsOnUpsert()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildExistingTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        _workspaceStateService
            .Setup(s => s.UpsertTabAsync(ctx.TenantId, sessionId, It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Cosmos unavailable"));

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Q8 stale-write refusal (USER WINS)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Refuses_WhenLastUserEditAtIsNewerThanExpected()
    {
        // Q8 binding: stored.LastUserEditAt > expected → refused_stale_read; no mutation.
        const string storedLastUserEdit = "2026-06-10T13:00:00Z";
        const string agentExpectedLastUserEdit = "2026-06-10T11:00:00Z"; // agent's stale view

        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1", expectedLastUserEditAt: agentExpectedLastUserEdit));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildExistingTab("tab-1", ctx.TenantId, sessionId, lastUserEditAt: storedLastUserEdit);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildUpdateTool(), CancellationToken.None);

        result.Success.Should().BeTrue(because: "stale-read is a re-actable response, not an error");
        var payload = result.GetData<UpdateWorkspaceTabHandler.UpdateWorkspaceTabPayload>();
        payload!.Status.Should().Be(UpdateWorkspaceTabHandler.StatusStaleRead);
        payload.CurrentLastUserEditAt.Should().Be(storedLastUserEdit,
            because: "the agent must see the current value so its next-turn read is fresh");

        _workspaceStateService.Verify(
            s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Rationale: Q8 USER WINS — no mutation occurs on stale-read refusal.
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
