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
/// Unit tests for <see cref="CloseWorkspaceTabHandler"/> (R6 Pillar 6b / task 056).
/// </summary>
/// <remarks>
/// Covers: success path (close unpinned), missing required argument, tenant isolation,
/// downstream service throws → graceful error, refuse on pinned tab.
/// </remarks>
public sealed class CloseWorkspaceTabHandlerTests : TypedToolHandlerTestFixture
{
    private readonly Mock<IWorkspaceStateService> _workspaceStateService = new();

    private CloseWorkspaceTabHandler CreateHandler() => new(
        _workspaceStateService.Object,
        CreateLogger<CloseWorkspaceTabHandler>());

    private static AnalysisTool BuildCloseTool() =>
        BuildAnalysisTool(handlerClass: nameof(CloseWorkspaceTabHandler), toolType: ToolType.Custom);

    private static WorkspaceTab BuildExistingTab(
        string tabId,
        string tenantId,
        string sessionId,
        bool isPinned = false)
    {
        return new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData { Body = "body" },
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
            IsPinned = isPinned,
            CanEdit = false,
            LastUserEditAt = null,
            CreatedAt = "2026-06-01T00:00:00Z",
            UpdatedAt = "2026-06-01T00:00:00Z"
        };
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Success path
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ClosesUnpinnedTab_OnHappyPath()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tabId":"tab-1"}""");
        var sessionId = ctx.ChatSessionId.ToString("D");
        var tab = BuildExistingTab("tab-1", ctx.TenantId, sessionId, isPinned: false);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        _workspaceStateService
            .Setup(s => s.CloseTabAsync(ctx.TenantId, sessionId, "tab-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildCloseTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<CloseWorkspaceTabHandler.CloseWorkspaceTabPayload>();
        payload!.Decision.Should().Be(CloseWorkspaceTabHandler.DecisionClosed);
        _workspaceStateService.Verify(
            s => s.CloseTabAsync(ctx.TenantId, sessionId, "tab-1", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Missing required parameter
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Fails_WhenTabIdMissing()
    {
        const string argsJson = "{}";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildCloseTool());

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
            toolArgumentsJson: """{"tabId":"tab-1"}""",
            tenantId: "tenant-iso");
        var sessionId = ctx.ChatSessionId.ToString("D");

        string? capturedTenant = null;
        _workspaceStateService
            .Setup(s => s.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((tenant, _, _) => capturedTenant = tenant)
            .ReturnsAsync(new List<WorkspaceTab>());

        _workspaceStateService
            .Setup(s => s.CloseTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildCloseTool(), CancellationToken.None);

        capturedTenant.Should().Be("tenant-iso",
            because: "ADR-014: tenant id must flow into every workspace-state call");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Pin guard
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_RefusesClose_WhenTabIsPinned()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tabId":"tab-pin"}""");
        var sessionId = ctx.ChatSessionId.ToString("D");
        var pinnedTab = BuildExistingTab("tab-pin", ctx.TenantId, sessionId, isPinned: true);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { pinnedTab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildCloseTool(), CancellationToken.None);

        result.Success.Should().BeTrue(because: "refusal is a re-actable response, not an error");
        var payload = result.GetData<CloseWorkspaceTabHandler.CloseWorkspaceTabPayload>();
        payload!.Decision.Should().Be(CloseWorkspaceTabHandler.DecisionRefusedPinned);

        _workspaceStateService.Verify(
            s => s.CloseTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        // Rationale: pin guard must prevent mutation.
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Downstream failure
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenCloseTabAsyncThrows()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: """{"tabId":"tab-1"}""");
        var sessionId = ctx.ChatSessionId.ToString("D");

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab>());

        _workspaceStateService
            .Setup(s => s.CloseTabAsync(ctx.TenantId, sessionId, "tab-1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Redis unavailable"));

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildCloseTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.InternalError);
    }
}
