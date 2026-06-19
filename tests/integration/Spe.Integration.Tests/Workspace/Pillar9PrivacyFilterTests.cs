// R6 task 074 / D-C-29 + D-C-30 — Pillar 9 per-turn agent prompt builder privacy
// default end-to-end integration test.
//
// Validates the FR-58 + FR-59 BINDING privacy filter end-to-end through the
// SprkChatAgentFactory's BuildWorkspaceStateBlock composition path:
//
//   tab.visibleToAssistant === true  AND  widget has derivable visible state
//   ────────────────────────────────  AND  ──────────────────────────────────
//                                          (TryDeriveVisibleState returns non-null)
//
// 3-tab scenario:
//   - Tab 1: visibleToAssistant=true + widget HAS state (Summary w/ tldr+body) → IN prompt
//   - Tab 2: visibleToAssistant=true + widget has NO state (Summary w/ empty)  → NOT in prompt
//   - Tab 3: visibleToAssistant=false + widget HAS state (DocumentViewer)      → NOT in prompt
//
// Why this lives in Spe.Integration.Tests (not unit tests):
//   - Exercises the BFF prompt-composition path end-to-end through the factory's
//     BuildWorkspaceStateBlock + TryDeriveVisibleState pipeline.
//   - Validates the deterministic prompt-fragment structure that the chat agent
//     observes (header line + per-widget FR-57 fields) so a regression on either
//     side of the filter is caught here, not via mock-trust.
//   - Mirrors the pattern from ConflictResolutionTests.cs (task 058).

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Spe.Integration.Tests.Workspace;

[Trait("Category", "Integration")]
[Trait("Feature", "WorkspacePrivacyFilter")]
public sealed class Pillar9PrivacyFilterTests
{
    private const string TenantId = "tenant-pillar9";
    private const string SessionId = "session-pillar9";

    [Fact]
    public void ThreeTabScenario_OnlyVisibleAndHasStateAppearsInPrompt()
    {
        // Arrange — three tabs covering all combinations of the FR-58 + FR-59 filter.
        var tabs = new[]
        {
            // Tab 1: visible + has-state → MUST appear in prompt.
            BuildTab(
                tabId: "tab-1-include-me",
                widgetType: "Summary",
                visibleToAssistant: true,
                matterName: "Tab1_Matter_INCLUDE",
                widgetData: new SummaryTabWidgetData
                {
                    Body = "The agent has summarized that the contract is silent on indemnification.",
                    Tldr = "Tab1_TLDR_INCLUDE",
                    HasUserEdits = false,
                }),

            // Tab 2: visible + NO state (privacy default — widget didn't opt in)
            //   → MUST NOT appear in prompt.
            BuildTab(
                tabId: "tab-2-no-state-exclude",
                widgetType: "Summary",
                visibleToAssistant: true,
                matterName: "Tab2_Matter_EXCLUDE_NoState",
                widgetData: new SummaryTabWidgetData
                {
                    Body = "",
                    Tldr = null,
                    HasUserEdits = false,
                }),

            // Tab 3: NOT visible (privacy default — user didn't opt-in)
            //   → MUST NOT appear in prompt even though widget HAS state.
            BuildTab(
                tabId: "tab-3-hidden-exclude",
                widgetType: "DocumentViewer",
                visibleToAssistant: false,
                matterName: "Tab3_Matter_EXCLUDE_Hidden",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-3",
                    Filename = "tab3-PRIVILEGED-do-not-leak.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 999_999,
                    HasSelection = true,
                    SelectionText = "Tab3 selection MUST_NOT_LEAK to agent",
                }),
        };

        var factory = CreateFactory();

        // Act — compose the per-turn block via the SAME path used by CreateAgentAsync.
        var block = factory.BuildWorkspaceStateBlock(tabs, SessionId);

        // Assert — Tab 1 IS in the prompt.
        block.Should().NotBeEmpty();
        block.Should().Contain("Tab1_TLDR_INCLUDE",
            because: "Tab 1 satisfies visibleToAssistant=true AND has derivable Summary state");
        block.Should().Contain("Tab1_Matter_INCLUDE");
        block.Should().Contain("widgetType=Summary");
        block.Should().Contain("hasUserEdits: false");

        // Assert — Tab 2 (visible + no state) is filtered OUT.
        block.Should().NotContain("Tab2_Matter_EXCLUDE_NoState",
            because: "FR-59 privacy default — widget without derivable state does NOT appear");

        // Assert — Tab 3 (NOT visible) is filtered OUT.
        block.Should().NotContain("Tab3_Matter_EXCLUDE_Hidden",
            because: "FR-59 privacy default — visibleToAssistant=false does NOT appear");
        block.Should().NotContain("tab3-PRIVILEGED-do-not-leak.pdf",
            because: "ADR-015 — non-visible filenames MUST NOT reach the agent");
        block.Should().NotContain("MUST_NOT_LEAK",
            because: "ADR-015 — non-visible selectionText MUST NOT reach the agent");
    }

    [Fact]
    public void AllThreeWidgetCategories_VisibleWithState_AppearInPrompt()
    {
        // Coverage gate — verify the three non-Summary widget categories also pass
        // through the BuildWorkspaceStateBlock path when visible.
        var tabs = new[]
        {
            BuildTab(
                tabId: "t-doc",
                widgetType: "DocumentViewer",
                visibleToAssistant: true,
                matterName: "DocMatter",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "d-1",
                    Filename = "indemnity-clause.docx",
                    MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes = 24_576,
                    HasSelection = false,
                }),

            BuildTab(
                tabId: "t-dash",
                widgetType: "Dashboard",
                visibleToAssistant: true,
                matterName: "DashMatter",
                widgetData: new DashboardTabWidgetData
                {
                    LayoutId = "layout-corp",
                    DashboardName = "Corporate Workspace",
                    LastViewedSection = "calendar",
                }),

            BuildTab(
                tabId: "t-table",
                widgetType: "Table",
                visibleToAssistant: true,
                matterName: "TableMatter",
                widgetData: new TableTabWidgetData
                {
                    RowCount = 12,
                    SortColumn = "name",
                    SortDirection = "asc",
                    FilteredColumns = new[] { "status" },
                    SelectedRows = new[] { "row-A", "row-B" },
                }),
        };

        var factory = CreateFactory();
        var block = factory.BuildWorkspaceStateBlock(tabs, SessionId);

        block.Should().Contain("widgetType=DocumentViewer");
        block.Should().Contain("filename: indemnity-clause.docx");
        block.Should().Contain("widgetType=Dashboard");
        block.Should().Contain("dashboardName: Corporate Workspace");
        block.Should().Contain("widgetType=Table");
        block.Should().Contain("rowCount: 12");
        // Selected rows COUNT (stricter than POML).
        block.Should().Contain("selectedRows: 2");
        // Row IDs must NOT leak.
        block.Should().NotContain("row-A");
        block.Should().NotContain("row-B");
    }

    [Fact]
    public void EmptyResult_WhenAllTabsExcludedByPrivacyFilter()
    {
        // 2-tab scenario where BOTH tabs fail the filter (one not visible, one no state).
        var tabs = new[]
        {
            BuildTab(
                tabId: "t-hidden",
                widgetType: "Summary",
                visibleToAssistant: false,
                matterName: "HiddenMatter",
                widgetData: new SummaryTabWidgetData { Body = "real body", Tldr = "real tldr" }),

            BuildTab(
                tabId: "t-no-state",
                widgetType: "Summary",
                visibleToAssistant: true,
                matterName: "NoStateMatter",
                widgetData: new SummaryTabWidgetData { Body = "", Tldr = null }),
        };

        var factory = CreateFactory();
        var block = factory.BuildWorkspaceStateBlock(tabs, SessionId);

        block.Should().BeEmpty(
            because: "FR-59 privacy default — when EVERY tab fails the filter, the entire block is omitted");
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static SprkChatAgentFactory CreateFactory()
    {
        // Direct construction via the protected test ctor — exercises the real
        // BuildWorkspaceStateBlock + TryDeriveVisibleState code paths without
        // the full AI-dep chain.
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<SprkChatAgentFactory>>(NullLogger<SprkChatAgentFactory>.Instance);
        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<SprkChatAgentFactory>>();
        return new TestableSprkChatAgentFactory(logger);
    }

    private static WorkspaceTab BuildTab(
        string tabId,
        string widgetType,
        bool visibleToAssistant,
        string matterName,
        WorkspaceTabWidgetData widgetData) => new()
        {
            Id = tabId,
            WidgetType = widgetType,
            WidgetData = widgetData,
            SessionId = SessionId,
            TenantId = TenantId,
            VisibleToAssistant = visibleToAssistant,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "agent",
                CreatedBy = "playbook-pillar9-test",
                CreatedAt = "2026-06-18T12:00:00Z",
            },
            MatterContext = new WorkspaceTabMatterContext
            {
                MatterId = Guid.NewGuid().ToString("D"),
                MatterName = matterName,
            },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-06-18T12:00:00Z",
            UpdatedAt = "2026-06-18T12:00:00Z",
        };

    /// <summary>
    /// Test subclass exposing the protected ctor — mirrors the unit-test pattern in
    /// <c>SprkChatAgentFactoryWorkspaceStateTests</c>.
    /// </summary>
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }
}
