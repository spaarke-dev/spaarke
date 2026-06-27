using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
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
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceStateService> _workspaceStateService = new();
    private readonly Mock<IGuidProvider> _guidProvider = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);

    public SendWorkspaceArtifactHandlerTests()
    {
        _guidProvider.Setup(g => g.NewGuid()).Returns(DeterministicTabGuid);
    }

    private SendWorkspaceArtifactHandler CreateHandler() => new(
        _workspaceStateService.Object,
        _guidProvider.Object,
        _timeProvider,
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
    // Test helper: deterministic TimeProvider
    // ═════════════════════════════════════════════════════════════════════════════

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
