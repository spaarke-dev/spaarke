using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for R6 task 053 (Pillar 6a / FR-34) + task 074 (Pillar 9 / FR-57/58/59) —
/// Workspace State block in
/// <see cref="SprkChatAgentFactory.BuildWorkspaceStateBlock(IReadOnlyList{WorkspaceTab}, string)"/>.
///
/// Task 053 originally covered the minimal text-summary format. Task 074 EXTENDS this
/// suite for the rich per-tab visible-state composition.
///
/// Covers:
///   - Empty / all-hidden inputs → empty string (no-op)
///   - Single visible tab → header + per-widget fields, "Tab 1 (active)" labeled
///   - Multiple visible tabs → most-recent UpdatedAt labeled "(active)"
///   - Pinned tabs carry "user-pinned" marker
///   - Truncation when block would exceed fallback ceiling
///   - ADR-015: NO raw user body bytes / no widget-data field-name leakage
///   - FR-57 per-widget shapes (Summary, DocumentViewer, Dashboard, Table)
///   - FR-58 + FR-59 privacy filter: visible + impl required (BOTH)
///   - Token-budget rejection: TryReservePromptBudget denies when over budget
/// </summary>
public class SprkChatAgentFactoryWorkspaceStateTests
{
    private const string TestSessionId = "session-abc";

    private static SprkChatAgentFactory CreateFactory()
    {
        // Minimal factory — we only need an instance to call BuildWorkspaceStateBlock.
        // The protected ctor accepts an ILogger and bypasses the AI-dep chain entirely.
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SprkChatAgentFactory>>(NullLogger<SprkChatAgentFactory>.Instance);
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SprkChatAgentFactory>>();
        return new TestableSprkChatAgentFactory(logger);
    }

    private static WorkspaceTab MakeTab(
        string id,
        string widgetType = "Summary",
        bool visibleToAssistant = true,
        bool isPinned = false,
        string matterName = "Acme v. Beta",
        string updatedAt = "2026-06-10T12:00:00Z",
        WorkspaceTabWidgetData? widgetData = null)
    {
        return new WorkspaceTab
        {
            Id = id,
            WidgetType = widgetType,
            WidgetData = widgetData ?? new SummaryTabWidgetData
            {
                Body = "x",
                Tldr = null,
                HasUserEdits = false,
            },
            SessionId = TestSessionId,
            TenantId = "tenant-test",
            VisibleToAssistant = visibleToAssistant,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "playbook-001",
                CreatedAt = updatedAt,
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = "matter-001",
                MatterName = matterName,
            },
            IsPinned = isPinned,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
        };
    }

    // ---------------------------------------------------------------------
    // Legacy task 053 contract — preserved
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_EmptyList_ReturnsEmptyString()
    {
        var factory = CreateFactory();

        var result = factory.BuildWorkspaceStateBlock(Array.Empty<WorkspaceTab>(), TestSessionId);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildWorkspaceStateBlock_AllTabsHiddenFromAssistant_ReturnsEmptyString()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", visibleToAssistant: false),
            MakeTab("t2", visibleToAssistant: false),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildWorkspaceStateBlock_SingleVisibleTab_FormatsAsActive()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "Summary") };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("## Workspace State");
        result.Should().Contain("Tab 1 (active): widgetType=Summary");
        result.Should().Contain("matter=\"Acme v. Beta\"");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_MultipleVisible_MostRecentLabeledActive()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("older", widgetType: "DocumentViewer", updatedAt: "2026-06-09T10:00:00Z",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "engagement-letter.docx",
                    MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes = 12345,
                }),
            MakeTab("newer", widgetType: "Summary", updatedAt: "2026-06-10T15:00:00Z",
                widgetData: new SummaryTabWidgetData { Body = "agent summary body", Tldr = "tldr line" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // First listed (Tab 1) = active = most recent UpdatedAt = "Summary"
        result.Should().Contain("Tab 1 (active): widgetType=Summary");
        // Second listed = non-active = "DocumentViewer"
        result.Should().Contain("Tab 2: widgetType=DocumentViewer");
        result.Should().NotContain("Tab 2 (active)");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_PinnedTab_CarriesUserPinnedMarker()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "Summary", isPinned: true) };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("user-pinned");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_UnpinnedTab_NoPinnedMarker()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "Summary", isPinned: false) };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().NotContain("user-pinned");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NeverIncludesArbitraryUserText_ADR015()
    {
        var factory = CreateFactory();
        // Construct a Summary tab whose Body contains a "user-injected" probe string.
        // Per ADR-015, the BODY participates in the prompt (it's the agent summary text
        // the user can quote), BUT only via the normalized projection — and no widget-
        // data field-name markers may leak.
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "Summary", matterName: "Probe Matter",
                widgetData: new SummaryTabWidgetData
                {
                    Body = "agent generated summary text",
                    Tldr = "PROBE_TLDR_LINE",
                    HasUserEdits = true,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // TL;DR is the FR-57 deterministic field — does appear.
        result.Should().Contain("PROBE_TLDR_LINE");
        // The matter name is allowed (deterministic anchor).
        result.Should().Contain("Probe Matter");
        // But no widget-data structural markers should leak.
        result.Should().NotContain("widgetData");
        result.Should().NotContain("widgetType=Summary,");
        // hasUserEdits field name is intentional (FR-57); body field name is NOT.
        result.Should().NotContain("\"body\"");
    }

    // ---------------------------------------------------------------------
    // Task 074 — FR-57 per-widget shape verification
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_SummaryWidget_ContainsFR57Fields()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "Summary",
                widgetData: new SummaryTabWidgetData
                {
                    Body = "The contract is materially silent on indemnification scope.",
                    Tldr = "Indemnification silent",
                    HasUserEdits = false,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=Summary");
        result.Should().Contain("tldr: Indemnification silent");
        result.Should().Contain("summary: The contract is materially silent");
        result.Should().Contain("hasUserEdits: false");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_DocumentViewerWidget_ContainsFR57Fields()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "DocumentViewer",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "engagement-letter.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 102400,
                    HasSelection = true,
                    SelectionText = "shall not be liable for indirect damages.",
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=DocumentViewer");
        result.Should().Contain("filename: engagement-letter.pdf");
        result.Should().Contain("mimeType: application/pdf");
        result.Should().Contain("sizeBytes: 102400");
        result.Should().Contain("hasSelection: true");
        result.Should().Contain("selectionText: shall not be liable for indirect damages.");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_DocumentViewer_NoSelection_OmitsSelectionText()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "DocumentViewer",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "doc.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 1024,
                    HasSelection = false,
                    SelectionText = "should-not-appear",
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("hasSelection: false");
        // When HasSelection is false, selectionText MUST NOT appear (privacy).
        result.Should().NotContain("should-not-appear");
        result.Should().NotContain("selectionText:");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_DocumentViewer_SelectionText_CappedAt200Chars()
    {
        var factory = CreateFactory();
        var longSelection = new string('S', 500);
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "DocumentViewer",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "doc.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 1024,
                    HasSelection = true,
                    SelectionText = longSelection,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // Selection text is capped at 200 chars + ellipsis. The 500-char input must NOT
        // appear in full — verify by looking for a too-long run of S's. Allowed runs are
        // up to ~200 (cap), plus possible trailing characters from the line. We assert
        // the FULL 250-char run does NOT appear.
        var tooLongRun = new string('S', 250);
        result.Should().NotContain(tooLongRun);
        // The truncation marker should appear.
        result.Should().Contain("…");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_DashboardWidget_OmitsChartData()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "Dashboard",
                widgetData: new DashboardTabWidgetData
                {
                    LayoutId = "layout-1",
                    DashboardName = "Corporate Workspace",
                    LastViewedSection = "calendar",
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=Dashboard");
        result.Should().Contain("dashboardName: Corporate Workspace");
        result.Should().Contain("lastViewedSection: calendar");
        // Dashboard MUST NOT carry chart data.
        result.Should().NotContain("chart");
        result.Should().NotContain("layoutId");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_TableWidget_EmitsCountNotIds()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "Table",
                widgetData: new TableTabWidgetData
                {
                    RowCount = 42,
                    SortColumn = "createdOn",
                    SortDirection = "desc",
                    FilteredColumns = new[] { "status", "owner" },
                    SelectedRows = new[] { "row-id-aaa-111", "row-id-bbb-222", "row-id-ccc-333" },
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=Table");
        result.Should().Contain("rowCount: 42");
        result.Should().Contain("sortColumn: createdOn");
        result.Should().Contain("filteredColumns: [status, owner]");
        // Selected rows COUNT (stricter than POML's selectedRows[]).
        result.Should().Contain("selectedRows: 3");
        // Row IDs MUST NOT appear (token economy + privacy).
        result.Should().NotContain("row-id-aaa-111");
        result.Should().NotContain("row-id-bbb-222");
        result.Should().NotContain("row-id-ccc-333");
    }

    // ---------------------------------------------------------------------
    // Task 074 — FR-58 + FR-59 privacy filter (BOTH conditions required)
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_VisibleAndHasState_AppearsInPrompt_FR58()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("visible-with-state",
                visibleToAssistant: true,
                widgetData: new SummaryTabWidgetData { Body = "real summary", Tldr = "real tldr" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("Tab 1 (active)");
        result.Should().Contain("real tldr");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_VisibleButNoState_FilteredOut_FR59_PrivacyDefault()
    {
        var factory = CreateFactory();
        // Summary with NEITHER tldr NOR body → no derivable visible state → privacy default.
        var tabs = new[]
        {
            MakeTab("visible-no-state",
                visibleToAssistant: true,
                widgetData: new SummaryTabWidgetData { Body = "", Tldr = null }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // No state derivable AND it's the only tab → empty block.
        result.Should().BeEmpty(because: "FR-59 privacy default — widgets that don't return state DO NOT appear");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NotVisible_FilteredOut_FR59_PrivacyDefault()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("not-visible",
                visibleToAssistant: false,
                widgetData: new SummaryTabWidgetData { Body = "rich body", Tldr = "rich tldr" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().BeEmpty(because: "FR-59 privacy default — visibleToAssistant=false MUST NOT appear");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_ThreeTabScenario_OnlyVisibleWithStateAppears()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1-visible-with-state",
                widgetType: "Summary",
                visibleToAssistant: true,
                matterName: "Tab1 Matter",
                widgetData: new SummaryTabWidgetData { Body = "t1 body", Tldr = "t1 tldr" }),
            MakeTab("t2-visible-no-state",
                widgetType: "Summary",
                visibleToAssistant: true,
                matterName: "Tab2 Matter",
                widgetData: new SummaryTabWidgetData { Body = "", Tldr = null }),
            MakeTab("t3-not-visible",
                widgetType: "DocumentViewer",
                visibleToAssistant: false,
                matterName: "Tab3 Matter",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-3",
                    Filename = "hidden-doc.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 9999,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // Only the (visible + has-state) tab appears.
        result.Should().Contain("Tab1 Matter");
        result.Should().NotContain("Tab2 Matter", because: "visible but no derivable state — FR-59 privacy default");
        result.Should().NotContain("Tab3 Matter", because: "not visible — FR-59 privacy default");
        result.Should().NotContain("hidden-doc.pdf");
    }

    // ---------------------------------------------------------------------
    // Task 074 — Budget enforcement via shared tracker (NFR-10 / FR-46)
    // ---------------------------------------------------------------------

    [Fact]
    public void TryReservePromptBudget_DeniesWhenOverLimit_FragmentMustBeOmitted()
    {
        // Build a fragment whose token-cost exceeds a tiny tracker budget. Verify the
        // helper returns false → call site MUST omit the block.
        var stubTracker = new StubTrackerOverBudget();

        var bigFragment = string.Join(' ', Enumerable.Repeat("token", 200));
        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: stubTracker,
            layer: "workspace-state",
            fragment: bigFragment,
            sessionId: Guid.NewGuid(),
            tenantId: "tenant-test");

        granted.Should().BeFalse(because: "the tracker rejected the reservation → call site must drop the block");
        stubTracker.LastLayer.Should().Be("workspace-state");
        stubTracker.LastRequestedTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryReservePromptBudget_GrantsWhenWithinLimit()
    {
        var stubTracker = new StubTrackerWithBudget(remaining: 10_000);

        var fragment = "short workspace state block";
        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: stubTracker,
            layer: "workspace-state",
            fragment: fragment,
            sessionId: Guid.NewGuid(),
            tenantId: "tenant-test");

        granted.Should().BeTrue();
        stubTracker.LastLayer.Should().Be("workspace-state");
    }

    [Fact]
    public void TryReservePromptBudget_NullTracker_PassesThrough_LegacyBehavior()
    {
        // Pre-task-068 envs have no tracker — helper returns true (legacy behavior).
        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: null,
            layer: "workspace-state",
            fragment: "anything",
            sessionId: null,
            tenantId: null);

        granted.Should().BeTrue();
    }

    // ---------------------------------------------------------------------
    // Task 074 — Truncation against fallback ceiling when many tabs
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_StaysWithinFallbackCeiling_WhenManyTabs()
    {
        var factory = CreateFactory();
        // 50 tabs with rich Summary content — would balloon past the ceiling unchecked.
        var tabs = Enumerable.Range(0, 50)
            .Select(i => MakeTab($"t{i}", widgetType: "Summary",
                widgetData: new SummaryTabWidgetData
                {
                    Body = $"summary body for tab {i} with reasonable length content for prompt assembly",
                    Tldr = $"tldr-{i}",
                }))
            .ToList();

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // Block respects the ~2 KB fallback ceiling (some slack for header).
        result.Length.Should().BeLessOrEqualTo(SprkChatAgentFactory.WorkspaceStateBlockMaxCharsRich + 200);
        // First tab is always included.
        result.Should().Contain("Tab 1 (active)");
    }

    // ---------------------------------------------------------------------
    // Test stubs
    // ---------------------------------------------------------------------

    private sealed class StubTrackerOverBudget : IPromptBudgetTracker
    {
        public string? LastLayer { get; private set; }
        public int LastRequestedTokens { get; private set; }
        public int TotalBudget => 1;
        public int UsedBudget => 1;
        public int Remaining => 0;
        public bool TryReserve(string layer, int requestedTokens, Guid? sessionId, string? tenantId)
        {
            LastLayer = layer;
            LastRequestedTokens = requestedTokens;
            return false;
        }
    }

    private sealed class StubTrackerWithBudget : IPromptBudgetTracker
    {
        public string? LastLayer { get; private set; }
        public int TotalBudget { get; }
        public int UsedBudget => 0;
        public int Remaining { get; }
        public StubTrackerWithBudget(int remaining)
        {
            TotalBudget = remaining;
            Remaining = remaining;
        }
        public bool TryReserve(string layer, int requestedTokens, Guid? sessionId, string? tenantId)
        {
            LastLayer = layer;
            return requestedTokens <= Remaining;
        }
    }

    /// <summary>
    /// Test subclass that exposes the protected ctor.
    /// </summary>
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }
}
