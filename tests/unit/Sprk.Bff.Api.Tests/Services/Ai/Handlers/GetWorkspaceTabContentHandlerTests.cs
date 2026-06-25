using System.Text.Json;
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
/// Unit tests for <see cref="GetWorkspaceTabContentHandler"/>
/// (chat-routing-redesign-r1 task 118b / FR-57 / architecture §6.5 + §11.1).
/// </summary>
/// <remarks>
/// <para>
/// Coverage matrix:
/// </para>
/// <list type="bullet">
///   <item>Happy path: tabId resolves → returns composed widget state keyed by section name</item>
///   <item>sectionName provided → returns single-entry sections map</item>
///   <item>sectionName provided + missing → graceful <c>section_missing</c> status (not error)</item>
///   <item>tabId not present → graceful <c>not_found</c> status (not error)</item>
///   <item>Read-only invariant: NO UpsertTabAsync / PinTabAsync / CloseTabAsync calls</item>
///   <item>ADR-014 tenant forwarding: TenantId propagates into GetTabsAsync</item>
///   <item>ADR-014 defense-in-depth tenant-mismatch refusal</item>
///   <item>ADR-015 tier-1 telemetry shape (validation_failed on missing tabId)</item>
///   <item>OperationCanceledException → CANCELLED ToolResult</item>
///   <item>Variant-aware projection: Summary / Document / Dashboard / Table all surface sections</item>
/// </list>
/// </remarks>
[Trait("status", "new")]
[Trait("project", "chat-routing-redesign-r1")]
[Trait("task", "118b")]
public sealed class GetWorkspaceTabContentHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IWorkspaceStateService> _workspaceStateService = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);

    private GetWorkspaceTabContentHandler CreateHandler() => new(
        _workspaceStateService.Object,
        _timeProvider,
        CreateLogger<GetWorkspaceTabContentHandler>());

    private static AnalysisTool BuildReadTool() =>
        BuildAnalysisTool(handlerClass: nameof(GetWorkspaceTabContentHandler), toolType: ToolType.Custom);

    private static WorkspaceTab BuildSummaryTab(
        string tabId,
        string tenantId,
        string sessionId,
        string body = "the contract terminates 2027-01-01",
        string? tldr = "Terminates 2027-01-01")
    {
        return new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "Summary",
            WidgetData = new SummaryTabWidgetData
            {
                Body = body,
                Tldr = tldr,
                HasUserEdits = false
            },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "playbook",
                CreatedBy = "playbook:summarize-document-for-workspace",
                CreatedAt = "2026-06-25T11:55:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-06-25T11:55:00Z",
            UpdatedAt = "2026-06-25T11:55:00Z"
        };
    }

    private static WorkspaceTab BuildDocumentViewerTab(
        string tabId,
        string tenantId,
        string sessionId)
    {
        return new WorkspaceTab
        {
            Id = tabId,
            WidgetType = "DocumentViewer",
            WidgetData = new DocumentViewerTabWidgetData
            {
                DocumentId = "doc-1",
                Filename = "nda.pdf",
                MimeType = "application/pdf",
                SizeBytes = 12345
            },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "user",
                CreatedBy = "user:00000000-0000-0000-0000-000000000001",
                CreatedAt = "2026-06-25T11:55:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = false,
            LastUserEditAt = null,
            CreatedAt = "2026-06-25T11:55:00Z",
            UpdatedAt = "2026-06-25T11:55:00Z"
        };
    }

    private static WorkspaceTab BuildDashboardTab(string tabId, string tenantId, string sessionId)
        => new()
        {
            Id = tabId,
            WidgetType = "Dashboard",
            WidgetData = new DashboardTabWidgetData
            {
                LayoutId = Guid.NewGuid().ToString("D"),
                DashboardName = "Corporate Workspace",
                LastViewedSection = "matters"
            },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "agent:test",
                CreatedAt = "2026-06-25T11:55:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = false,
            LastUserEditAt = null,
            CreatedAt = "2026-06-25T11:55:00Z",
            UpdatedAt = "2026-06-25T11:55:00Z"
        };

    private static WorkspaceTab BuildTableTab(string tabId, string tenantId, string sessionId)
        => new()
        {
            Id = tabId,
            WidgetType = "Table",
            WidgetData = new TableTabWidgetData
            {
                RowCount = 42,
                FilteredColumns = new[] { "status", "createdOn" },
                SelectedRows = new[] { "row-1" },
                SortColumn = "createdOn",
                SortDirection = "desc"
            },
            SessionId = sessionId,
            TenantId = tenantId,
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "agent:test",
                CreatedAt = "2026-06-25T11:55:00Z"
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.Empty.ToString("D"),
                MatterName = "Unattached"
            },
            IsPinned = false,
            CanEdit = false,
            LastUserEditAt = null,
            CreatedAt = "2026-06-25T11:55:00Z",
            UpdatedAt = "2026-06-25T11:55:00Z"
        };

    private static string BuildArgsJson(string tabId, string? sectionName = null)
    {
        var sectionFragment = sectionName is null
            ? ""
            : $",\"sectionName\":\"{sectionName}\"";
        return $$"""
                 {
                   "tabId": "{{tabId}}"
                   {{sectionFragment}}
                 }
                 """;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Happy path: tabId resolves → returns composed widget state
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Succeeds_ReturnsComposedSections_OnHappyPath()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be(GetWorkspaceTabContentHandler.StatusOk);
        payload.TabId.Should().Be("tab-1");
        payload.WidgetType.Should().Be("Summary");
        payload.Sections.Should().ContainKey("body");
        payload.Sections["body"].GetString().Should().Be("the contract terminates 2027-01-01");
        payload.Sections.Should().ContainKey("tldr");
        payload.Sections["tldr"].GetString().Should().Be("Terminates 2027-01-01");
        payload.SectionCount.Should().Be(payload.Sections.Count);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // sectionName provided → returns single-entry sections map
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Succeeds_ReturnsSingleSection_WhenSectionNameProvided()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1", sectionName: "body"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.Status.Should().Be(GetWorkspaceTabContentHandler.StatusOk);
        payload.SectionCount.Should().Be(1);
        payload.Sections.Should().HaveCount(1);
        payload.Sections.Should().ContainKey("body");
        payload.Sections["body"].GetString().Should().Be("the contract terminates 2027-01-01");
        payload.Sections.Should().NotContainKey("tldr",
            because: "sectionName-scoped reads return only the requested section");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // sectionName not present → graceful section_missing (not error)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Returns_SectionMissing_WhenSectionAbsent()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1", sectionName: "does-not-exist"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "section_missing is a re-actable status, not an error");
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.Status.Should().Be(GetWorkspaceTabContentHandler.StatusSectionMissing);
        payload.WidgetType.Should().Be("Summary");
        payload.Sections.Should().BeEmpty();
        payload.SectionCount.Should().Be(0);
        payload.Message.Should().Contain("does-not-exist");
        // Available sections list mentioned in message (so the LLM can re-attempt with a valid name).
        payload.Message.Should().Contain("body");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // tabId not present → graceful not_found (not error)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Returns_NotFound_WhenTabIdAbsent()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("ghost-tab"));
        var sessionId = ctx.ChatSessionId.ToString("N");

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorkspaceTab>());

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue(
            because: "not_found is a re-actable status, not an error");
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.Status.Should().Be(GetWorkspaceTabContentHandler.StatusNotFound);
        payload.TabId.Should().Be("ghost-tab");
        payload.WidgetType.Should().BeNull();
        payload.Sections.Should().BeEmpty();
        payload.SectionCount.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Read-only invariant: NO writes to workspace state
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_NeverInvokesWriteMethods_OnAnyPath()
    {
        // Happy path
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        // NFR-A1 explicit T2→T3 promotion binding: this handler is READ-ONLY.
        _workspaceStateService.Verify(
            s => s.UpsertTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<WorkspaceTab>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _workspaceStateService.Verify(
            s => s.PinTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _workspaceStateService.Verify(
            s => s.CloseTabAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-014 tenant forwarding
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ForwardsTenantId_ToWorkspaceState()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1"),
            tenantId: "tenant-iso");
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", "tenant-iso", sessionId);

        string? capturedTenant = null;
        _workspaceStateService
            .Setup(s => s.GetTabsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((tenant, _, _) => capturedTenant = tenant)
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        capturedTenant.Should().Be("tenant-iso");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-014 defense-in-depth: tab tenantId must match context tenantId
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsError_WhenResolvedTabTenantMismatchesContext()
    {
        var ctx = BuildChatInvocationContext(
            toolArgumentsJson: BuildArgsJson("tab-1"),
            tenantId: "tenant-A");
        var sessionId = ctx.ChatSessionId.ToString("N");
        // Foreign-tenant tab (simulated cache-layer routing bug — defense in depth).
        var tab = BuildSummaryTab("tab-1", "tenant-FOREIGN", sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync("tenant-A", sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 + validation: missing tabId returns validation_failed (not a throw)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Fails_WhenTabIdMissing()
    {
        const string argsJson = """{"sectionName":"body"}""";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildReadTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("tabId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsValidationFailed_WhenToolArgumentsMalformed()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: "{not-valid-json");

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Variant-aware projection: DocumentViewer
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ProjectsDocumentViewerVariant_AsSections()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-doc"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildDocumentViewerTab("tab-doc", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.WidgetType.Should().Be("DocumentViewer");
        payload.Sections.Should().ContainKey("documentId");
        payload.Sections.Should().ContainKey("filename");
        payload.Sections.Should().ContainKey("mimeType");
        payload.Sections.Should().ContainKey("sizeBytes");
        payload.Sections["filename"].GetString().Should().Be("nda.pdf");
        payload.Sections["sizeBytes"].GetInt64().Should().Be(12345);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Variant-aware projection: Dashboard
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ProjectsDashboardVariant_AsSections()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-dash"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildDashboardTab("tab-dash", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.WidgetType.Should().Be("Dashboard");
        payload.Sections.Should().ContainKey("layoutId");
        payload.Sections.Should().ContainKey("dashboardName");
        payload.Sections.Should().ContainKey("lastViewedSection");
        payload.Sections["dashboardName"].GetString().Should().Be("Corporate Workspace");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Variant-aware projection: Table
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ProjectsTableVariant_AsSections()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-table"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildTableTab("tab-table", ctx.TenantId, sessionId);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<GetWorkspaceTabContentHandler.GetWorkspaceTabContentPayload>();
        payload!.WidgetType.Should().Be("Table");
        payload.Sections.Should().ContainKey("rowCount");
        payload.Sections["rowCount"].GetInt32().Should().Be(42);
        payload.Sections.Should().ContainKey("filteredColumns");
        payload.Sections.Should().ContainKey("sortColumn");
        payload.Sections["sortColumn"].GetString().Should().Be("createdOn");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // OperationCanceledException → CANCELLED ToolResult
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_ReturnsCancelled_OnOperationCanceled()
    {
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ExecuteAsync (playbook path) is rejected — chat-context-only handler
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteAsync_PlaybookPath_IsRejected()
    {
        var execCtx = BuildToolExecutionContext();
        var handler = CreateHandler();
        var result = await handler.ExecuteAsync(execCtx, BuildReadTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed,
            because: "the handler is chat-context-only; playbook invocation must be rejected");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015 telemetry hygiene: no widget body content in captured logs
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_TelemetryRespectsAdr015_NeverLogsWidgetBody()
    {
        const string secretBody = "engagement-letter clause 42.b — VERY SENSITIVE LEGAL TEXT THAT MUST NOT APPEAR IN LOGS";
        var ctx = BuildChatInvocationContext(toolArgumentsJson: BuildArgsJson("tab-1"));
        var sessionId = ctx.ChatSessionId.ToString("N");
        var tab = BuildSummaryTab("tab-1", ctx.TenantId, sessionId, body: secretBody);

        _workspaceStateService
            .Setup(s => s.GetTabsAsync(ctx.TenantId, sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WorkspaceTab> { tab });

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildReadTool(), CancellationToken.None);

        // ADR-015 binding: widget body content MUST NOT appear in any captured log message,
        // even though it IS legitimately returned in the ToolResult.Data to the LLM.
        AssertTelemetryRespectsAdr015(secretBody);
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
