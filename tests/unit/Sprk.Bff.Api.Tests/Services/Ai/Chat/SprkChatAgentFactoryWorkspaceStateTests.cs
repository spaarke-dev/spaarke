using System.Linq;
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
/// Unit tests for the Workspace State block in
/// <see cref="SprkChatAgentFactory.BuildWorkspaceStateBlock"/>.
///
/// <para>
/// <b>R3 task 011 (FR-03 + FR-04)</b> RE-BASELINES this suite. FR-03 TRIMS the per-tab emission to
/// EXACTLY <c>{type, label, active}</c> — no ambient widget content of any kind (this SUPERSEDES the
/// R6 task 053/074 + R2 rich per-widget-field contract these tests originally characterized). FR-04
/// threads the single <c>{id,type,label}</c> active-item handle. The id-not-content boundary
/// (ADR-015 Path A) is the governance invariant the project rests on.
/// </para>
///
/// Covers:
///   - Empty / all-hidden inputs → empty string (no-op)
///   - Trimmed per-tab shape: {type, label, active} only — no content fields
///   - Label resolution: DisplayName (live-tab title) → derived identity name → widgetType
///   - Task 010 layout-tab identity variant preserved (composes with the trim)
///   - The re-point: a live tab (DisplayName-only, no derivable typed state) still appears
///   - Pinned durable rows union alongside live tabs
///   - FR-04 single active-item slot ({id,type,label}; exactly one; empty when none)
///   - Governance: NO item content anywhere; the handle is structurally {id,type,label}
///   - Preserved helper contracts (TryDeriveVisibleState, FormatVisibleStateFields, budget)
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
        WorkspaceTabWidgetData? widgetData = null,
        string? displayName = null)
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
            DisplayName = displayName,
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
    // No-op inputs
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

    // ---------------------------------------------------------------------
    // FR-03 — trimmed per-tab shape: {type, label, active} ONLY
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_SingleVisibleTab_EmitsTypeAndLabelAndActive()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "Summary", displayName: "My Summary") };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("## Workspace State");
        // {type, active} + {label}. No matter, no pinned marker, no content.
        result.Should().Contain("Tab 1 (active): widgetType=Summary label=\"My Summary\"");
        result.Should().NotContain("matter=");
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

        // First listed (Tab 1) = active = most recent UpdatedAt = "Summary".
        result.Should().Contain("Tab 1 (active): widgetType=Summary");
        // Second listed = non-active = "DocumentViewer".
        result.Should().Contain("Tab 2: widgetType=DocumentViewer");
        result.Should().NotContain("Tab 2 (active)");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_PinnedTab_DoesNotEmitPinnedMarker_Trimmed()
    {
        // FR-03: the trimmed {type,label,active} shape drops the R6 "user-pinned" marker (ambient
        // metadata is no longer emitted). The tab still appears by identity.
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "Summary", isPinned: true, displayName: "Pinned Tab") };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("Tab 1 (active): widgetType=Summary label=\"Pinned Tab\"");
        result.Should().NotContain("user-pinned");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_LabelPrefersDisplayName_OverDerivedIdentityName()
    {
        // Live tabs carry a DisplayName (the tab-strip title). It is the primary label source and
        // wins over the derived identity name.
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "DocumentViewer", displayName: "Engagement Letter",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "engagement-letter.docx",
                    MimeType = "application/pdf",
                    SizeBytes = 1,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("label=\"Engagement Letter\"");
        // The filename (derived identity) is NOT emitted as a separate content field.
        result.Should().NotContain("filename:");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NoDisplayName_LabelFallsBackToDerivedIdentityName()
    {
        // No DisplayName → label falls back to the derived identity name (filename here).
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("t1", widgetType: "DocumentViewer",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1",
                    Filename = "brief.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 1,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("label=\"brief.pdf\"");
    }

    // ---------------------------------------------------------------------
    // FR-03 re-point — a LIVE tab (DisplayName-only, no derivable typed state)
    // still appears (proves live tabs surface even when their opaque widgetData
    // carries no `kind`). Test (a).
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_LiveTabWithDisplayNameButNoDerivableState_StillAppears()
    {
        // A live open tab mapped from StoredWorkspaceTab whose opaque widgetData did not deserialize
        // (null → no typed state) but which carries a DisplayName. Pre-re-point such a tab would be
        // filtered out (null state); the re-point lists it by {type,label} so live tabs are visible.
        var factory = CreateFactory();
        var tab = new WorkspaceTab
        {
            Id = "live-only-1",
            WidgetType = "redline-viewer",
            WidgetData = null!,
            DisplayName = "Redline — Master Services Agreement",
            SessionId = TestSessionId,
            TenantId = "tenant-test",
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance
            {
                Source = "user",
                CreatedBy = "workspace-live-tab",
                CreatedAt = "2026-08-10T09:00:00Z",
            },
            MatterContext = new WorkspaceTabMatterContext { MatterId = "", MatterName = "" },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-08-10T09:00:00Z",
            UpdatedAt = "2026-08-10T09:00:00Z",
        };

        var result = factory.BuildWorkspaceStateBlock(new[] { tab }, TestSessionId);

        result.Should().Contain("widgetType=redline-viewer");
        result.Should().Contain("label=\"Redline — Master Services Agreement\"");
    }

    // ---------------------------------------------------------------------
    // Test (b) — pinned durable rows union alongside live tabs
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_LiveTabAndPinnedDurableRow_BothAppear()
    {
        // The CreateAgentAsync union feeds BuildWorkspaceStateBlock a combined list of live tabs +
        // still-valid pinned durable rows. The block lists both.
        var factory = CreateFactory();
        var liveTab = MakeTab("live-1", widgetType: "email-workspace", displayName: "Re: NDA review");
        var pinnedDurableRow = MakeTab("pinned-1", widgetType: "Dashboard", isPinned: true,
            widgetData: new DashboardTabWidgetData { LayoutId = "l1", DashboardName = "Corporate Workspace" });

        var result = factory.BuildWorkspaceStateBlock(new[] { liveTab, pinnedDurableRow }, TestSessionId);

        result.Should().Contain("label=\"Re: NDA review\"");
        result.Should().Contain("label=\"Corporate Workspace\"");
    }

    // ---------------------------------------------------------------------
    // R3 task 010 (FR-01/FR-02) — layout-tab identity variant PRESERVED under the trim
    // ---------------------------------------------------------------------

    [Fact]
    public void TryDeriveVisibleState_WorkspaceLayoutTab_NoLongerNull_DerivesDashboardIdentity()
    {
        var tab = MakeTab("briefing-1", widgetType: "workspace",
            widgetData: new DashboardTabWidgetData
            {
                LayoutId = "layout-briefing",
                DashboardName = "Daily Briefing",
            });

        var state = SprkChatAgentFactory.TryDeriveVisibleState(tab);

        state.Should().NotBeNull(because: "FR-01 — layout tabs must no longer derive null");
        state.Should().BeOfType<WorkspaceTabVisibleState.Dashboard>();
        ((WorkspaceTabVisibleState.Dashboard)state!).DashboardName.Should().Be("Daily Briefing");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_DailyBriefingAndCalendarTabsOpen_ListsEachByTypeAndLabel()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("briefing-1", widgetType: "workspace", updatedAt: "2026-08-10T09:00:00Z",
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-briefing", DashboardName = "Daily Briefing" }),
            MakeTab("calendar-1", widgetType: "workspace", updatedAt: "2026-08-10T10:00:00Z",
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-calendar", DashboardName = "Calendar" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // {type, label}: type = raw widgetType ("workspace"), label = derived dashboardName.
        result.Should().Contain("widgetType=workspace");
        result.Should().Contain("label=\"Daily Briefing\"");
        result.Should().Contain("label=\"Calendar\"");
        // No ambient content field name leaks.
        result.Should().NotContain("dashboardName:");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_WorkspaceLayoutTab_AskDoYouSeeDailyBriefing_AnswerableFromBlock()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("briefing-1", widgetType: "workspace", visibleToAssistant: true,
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-briefing", DashboardName = "Daily Briefing" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("Daily Briefing");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_WorkspaceLayoutTab_ToggleOff_StaysHiddenUnlessActive_FR02()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("other", widgetType: "Summary", visibleToAssistant: true, updatedAt: "2026-08-10T11:00:00Z"),
            MakeTab("briefing-hidden", widgetType: "workspace", visibleToAssistant: false,
                updatedAt: "2026-08-10T08:00:00Z",
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-briefing", DashboardName = "TOGGLED_OFF_PROBE Daily Briefing" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: "other");

        result.Should().NotContain("TOGGLED_OFF_PROBE");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_WorkspaceLayoutTab_ToggleOn_StaysVisible_FR02()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("calendar-visible", widgetType: "workspace", visibleToAssistant: true,
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-calendar", DashboardName = "Calendar" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("label=\"Calendar\"");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_WorkspaceLayoutTab_ActiveButNotOptedIn_VisibleViaConsent_PathA()
    {
        // Active-tab-as-consent (R2 Path A) still governs visibility — preserved under the trim.
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("briefing-active", widgetType: "workspace", visibleToAssistant: false,
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-briefing", DashboardName = "Daily Briefing" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: "briefing-active");

        result.Should().Contain("Tab 1 (active): widgetType=workspace label=\"Daily Briefing\"");
    }

    [Fact]
    public void TryDeriveVisibleState_WorkspaceLayoutTab_ContractGapNoServerReadableName_DerivesGracefulDefault_NeverNull()
    {
        var tab = new WorkspaceTab
        {
            Id = "workspace-no-kind",
            WidgetType = "workspace",
            WidgetData = null!,
            SessionId = TestSessionId,
            TenantId = "tenant-test",
            VisibleToAssistant = true,
            SourceProvenance = new WorkspaceTabSourceProvenance { Source = "user", CreatedBy = "user-001", CreatedAt = "2026-08-10T09:00:00Z" },
            MatterContext = new WorkspaceTabMatterContext { MatterId = "matter-001", MatterName = "Acme v. Beta" },
            IsPinned = false,
            CanEdit = true,
            LastUserEditAt = null,
            CreatedAt = "2026-08-10T09:00:00Z",
            UpdatedAt = "2026-08-10T09:00:00Z",
        };

        var state = SprkChatAgentFactory.TryDeriveVisibleState(tab);

        state.Should().NotBeNull(because: "the contract-gap fallback must never silently drop the tab");
        ((WorkspaceTabVisibleState.Dashboard)state!).DashboardName.Should().Be(SprkChatAgentFactory.WorkspaceLayoutDefaultLabel);
    }

    // ---------------------------------------------------------------------
    // Compose tab ("the flip") — label derives from the DocumentViewer filename
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_ComposeTab_LabelIsDocumentFilename()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("compose-1", widgetType: "compose",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-compose-1",
                    Filename = "engagement-letter-draft.docx",
                    MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    SizeBytes = 45678,
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=compose");
        result.Should().Contain("label=\"engagement-letter-draft.docx\"");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_ComposeTab_NoServerReadableFilename_LabelIsGracefulDefault()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("compose-2", widgetType: "compose",
                widgetData: new DashboardTabWidgetData { LayoutId = "layout-compose", DashboardName = "Compose", LastViewedSection = null }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().Contain("widgetType=compose");
        result.Should().Contain($"label=\"{SprkChatAgentFactory.ComposeDefaultFilename}\"");
    }

    // ---------------------------------------------------------------------
    // FR-58/FR-59 privacy filter — preserved (visible + derivable-state-or-DisplayName)
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_VisibleButNoStateNoDisplayName_FilteredOut_FR59_PrivacyDefault()
    {
        var factory = CreateFactory();
        // Summary with NEITHER tldr NOR body AND no DisplayName → no derivable state, no label → drop.
        var tabs = new[]
        {
            MakeTab("visible-no-state", visibleToAssistant: true,
                widgetData: new SummaryTabWidgetData { Body = "", Tldr = null }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().BeEmpty(because: "no derivable state AND no DisplayName → nothing to identify the tab by");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NotVisible_FilteredOut_FR59_PrivacyDefault()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("not-visible", visibleToAssistant: false,
                widgetData: new SummaryTabWidgetData { Body = "rich body", Tldr = "rich tldr" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Should().BeEmpty(because: "FR-59 privacy default — visibleToAssistant=false MUST NOT appear");
    }

    // ---------------------------------------------------------------------
    // Truncation against fallback ceiling
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_StaysWithinFallbackCeiling_WhenManyTabs()
    {
        var factory = CreateFactory();
        var tabs = Enumerable.Range(0, 50)
            .Select(i => MakeTab($"t{i}", widgetType: "Summary", displayName: $"Summary Tab {i}",
                widgetData: new SummaryTabWidgetData { Body = $"summary body {i}", Tldr = $"tldr-{i}" }))
            .ToList();

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        result.Length.Should().BeLessOrEqualTo(SprkChatAgentFactory.WorkspaceStateBlockMaxCharsRich + 200);
        result.Should().Contain("Tab 1 (active)");
    }

    // ---------------------------------------------------------------------
    // FR-A3 focus-stamp preference — preserved (label-only, no content)
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_FocusStamp_LabelsStampedTabActive_NotMostRecent()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("older", widgetType: "DocumentViewer", updatedAt: "2026-06-09T10:00:00Z",
                widgetData: new DocumentViewerTabWidgetData { DocumentId = "doc-1", Filename = "focused-doc.pdf", MimeType = "application/pdf", SizeBytes = 12345 }),
            MakeTab("newer", widgetType: "Summary", updatedAt: "2026-06-10T15:00:00Z",
                widgetData: new SummaryTabWidgetData { Body = "agent summary body", Tldr = "tldr line" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: "older");

        result.Should().Contain("Tab 1 (active): widgetType=DocumentViewer label=\"focused-doc.pdf\"");
        result.Should().Contain("Tab 2: widgetType=Summary");
        result.Should().NotContain("Tab 2 (active)");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NoFocusStamp_FallsBackToUpdatedAtMostRecent()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("older", widgetType: "DocumentViewer", updatedAt: "2026-06-09T10:00:00Z",
                widgetData: new DocumentViewerTabWidgetData { DocumentId = "doc-1", Filename = "engagement-letter.docx", MimeType = "application/pdf", SizeBytes = 12345 }),
            MakeTab("newer", widgetType: "Summary", updatedAt: "2026-06-10T15:00:00Z",
                widgetData: new SummaryTabWidgetData { Body = "agent summary body", Tldr = "tldr line" }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: null);

        result.Should().Contain("Tab 1 (active): widgetType=Summary");
        result.Should().Contain("Tab 2: widgetType=DocumentViewer");
        result.Should().NotContain("Tab 2 (active)");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_ActiveTabNotOptedIn_IsVisibleAsActive_PathA_NoContent()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("active-doc", widgetType: "DocumentViewer", visibleToAssistant: false,
                updatedAt: "2026-08-07T10:00:00Z",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-active",
                    Filename = "corteva-nda.pdf",
                    MimeType = "application/pdf",
                    SizeBytes = 5000,
                    HasSelection = true,
                    SelectionText = "ACTIVE_SELECTION_PROBE indemnification",
                }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: "active-doc");

        // Present + active + identity label (active-tab-as-consent), but NO selection content.
        result.Should().Contain("Tab 1 (active): widgetType=DocumentViewer label=\"corteva-nda.pdf\"");
        result.Should().NotContain("ACTIVE_SELECTION_PROBE");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_BackgroundTabNotOptedIn_StaysExcluded_PathA()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("active", widgetType: "Summary", visibleToAssistant: true, updatedAt: "2026-08-07T11:00:00Z"),
            MakeTab("bg-hidden", widgetType: "DocumentViewer", visibleToAssistant: false,
                updatedAt: "2026-08-07T09:00:00Z",
                widgetData: new DocumentViewerTabWidgetData { DocumentId = "doc-hidden", Filename = "HIDDEN_PROBE-secret.pdf", MimeType = "application/pdf", SizeBytes = 3000 }),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeContextTabId: "active");

        result.Should().Contain("(active): widgetType=Summary");
        result.Should().NotContain("HIDDEN_PROBE-secret.pdf");
    }

    // ---------------------------------------------------------------------
    // FR-04 — single active-item {id,type,label} handle slot. Tests (d), (e).
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_ActiveItemHandleSupplied_EmitsExactlyOneSlot_WithIdTypeLabel()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "email-workspace", displayName: "Inbox") };
        var handle = new WorkspaceActiveItemHandle(Id: "email-789", Type: "email", Label: "Re: NDA review");

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeItem: handle);

        // Exactly one active-item section.
        System.Text.RegularExpressions.Regex.Matches(result, "### Active Item").Count.Should().Be(1);
        result.Should().Contain("- id: email-789");
        result.Should().Contain("- type: email");
        result.Should().Contain("- label: \"Re: NDA review\"");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NoActiveItemHandle_EmptyActiveItemSlot()
    {
        var factory = CreateFactory();
        var tabs = new[] { MakeTab("t1", widgetType: "email-workspace", displayName: "Inbox") };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeItem: null);

        result.Should().NotContain("### Active Item");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_ActiveItemHandleOnly_NoVisibleTabs_StillEmitsTheSingleSlot()
    {
        var factory = CreateFactory();
        var handle = new WorkspaceActiveItemHandle(Id: "doc-42", Type: "document", Label: "MSA.pdf");

        var result = factory.BuildWorkspaceStateBlock(Array.Empty<WorkspaceTab>(), TestSessionId, activeItem: handle);

        System.Text.RegularExpressions.Regex.Matches(result, "### Active Item").Count.Should().Be(1);
        result.Should().Contain("- id: doc-42");
        result.Should().Contain("- type: document");
        result.Should().Contain("- label: \"MSA.pdf\"");
    }

    // ---------------------------------------------------------------------
    // FR-03/FR-04 governance — NO item content anywhere. Test (c).
    // ---------------------------------------------------------------------

    [Fact]
    public void BuildWorkspaceStateBlock_GovernanceSnapshot_NoItemContentFields_OnlyTypeLabelActivePerTab_PlusOneHandle()
    {
        var factory = CreateFactory();
        // A rich, content-bearing tab of every widget type — every content probe must be ABSENT.
        var tabs = new[]
        {
            MakeTab("summary", widgetType: "Summary", displayName: "Summary Tab",
                widgetData: new SummaryTabWidgetData { Body = "BODY_CONTENT_PROBE", Tldr = "TLDR_CONTENT_PROBE", HasUserEdits = true }),
            MakeTab("doc", widgetType: "DocumentViewer", displayName: "Doc Tab",
                widgetData: new DocumentViewerTabWidgetData
                {
                    DocumentId = "doc-1", Filename = "file.pdf", MimeType = "application/pdf", SizeBytes = 99999,
                    HasSelection = true, SelectionText = "SELECTION_CONTENT_PROBE",
                }),
            MakeTab("table", widgetType: "Table", displayName: "Table Tab",
                widgetData: new TableTabWidgetData
                {
                    RowCount = 42, SortColumn = "createdOn", SortDirection = "desc",
                    FilteredColumns = new[] { "FILTER_COL_PROBE" }, SelectedRows = new[] { "ROW_ID_PROBE" },
                }),
            MakeTab("email", widgetType: "Email", displayName: "Email Tab",
                widgetData: new EmailTabWidgetData
                {
                    EmlDocumentId = "EML_HANDLE_PROBE", Subject = "Subject Line", From = "alice@acme.com",
                    Date = "2026-08-01T10:00:00Z", ThreadId = "THREAD_PROBE", Snippet = "SNIPPET_CONTENT_PROBE",
                }),
            MakeTab("dash", widgetType: "Dashboard", displayName: "Dash Tab",
                widgetData: new DashboardTabWidgetData { LayoutId = "LAYOUT_ID_PROBE", DashboardName = "Corp", LastViewedSection = "SECTION_PROBE" }),
        };
        var handle = new WorkspaceActiveItemHandle(Id: "item-1", Type: "email", Label: "Handle Label");

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId, activeItem: handle);

        // Content probes — NONE may appear anywhere in the block.
        foreach (var probe in new[]
        {
            "BODY_CONTENT_PROBE", "TLDR_CONTENT_PROBE", "SELECTION_CONTENT_PROBE", "SNIPPET_CONTENT_PROBE",
            "FILTER_COL_PROBE", "ROW_ID_PROBE", "LAYOUT_ID_PROBE", "SECTION_PROBE", "THREAD_PROBE", "EML_HANDLE_PROBE",
        })
        {
            result.Should().NotContain(probe, because: "FR-03/ADR-015 — the block carries NO item content");
        }

        // Content-bearing field NAMES must not leak either.
        foreach (var field in new[]
        {
            "tldr:", "summary:", "selectionText:", "snippet:", "mimeType:", "sizeBytes:", "hasSelection:",
            "rowCount:", "filteredColumns:", "selectedRows:", "dashboardName:", "lastViewedSection:",
            "subject:", "from:", "date:", "hasUserEdits:", "widgetData",
        })
        {
            result.Should().NotContain(field, because: "FR-03 — only {type,label,active} + one {id,type,label} handle");
        }

        // Positive: identity labels + the single handle ARE present.
        result.Should().Contain("label=\"Summary Tab\"");
        result.Should().Contain("label=\"Email Tab\"");
        result.Should().Contain("- id: item-1");
        System.Text.RegularExpressions.Regex.Matches(result, "### Active Item").Count.Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Test (f) — a content field on the handle would fail governance. The handle
    // is STRUCTURALLY {id,type,label} only (no content member can exist).
    // ---------------------------------------------------------------------

    [Fact]
    public void WorkspaceActiveItemHandle_HasExactlyIdTypeLabel_NoContentField()
    {
        var props = typeof(WorkspaceActiveItemHandle)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        props.Should().BeEquivalentTo(new[] { "Id", "Label", "Type" },
            because: "ADR-015 id-not-content — the handle carries EXACTLY {id,type,label}; a content field here is a governance defect");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_HandleNeverEmitsContent_OnlyIdTypeLabel()
    {
        // Even if the label/type strings resemble content, the slot emits ONLY id/type/label lines —
        // there is no code path that appends a content field for the handle.
        var factory = CreateFactory();
        var handle = new WorkspaceActiveItemHandle(Id: "id-1", Type: "email", Label: "L");

        var result = factory.BuildWorkspaceStateBlock(Array.Empty<WorkspaceTab>(), TestSessionId, activeItem: handle);

        var activeItemSection = result[result.IndexOf("### Active Item", StringComparison.Ordinal)..];
        // The active-item section has EXACTLY three data ("- ") lines: id, type, label — proving
        // no content line can be appended for the handle.
        var dataLines = activeItemSection
            .Split('\n')
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .ToArray();
        dataLines.Should().HaveCount(3);
        dataLines.Should().Contain("- id: id-1");
        dataLines.Should().Contain("- type: email");
        dataLines.Should().Contain("- label: \"L\"");
    }

    // ---------------------------------------------------------------------
    // Preserved helper contracts (TryDeriveVisibleState / FormatVisibleStateFields
    // are unchanged by task 011 — the block just no longer emits their content).
    // ---------------------------------------------------------------------

    [Fact]
    public void WorkspaceTabVisibleState_Email_IsStructurallyDistinctVariant_NoDashboardFallback()
    {
        WorkspaceTabVisibleState state = new WorkspaceTabVisibleState.Email(
            Subject: "Re: NDA review", From: "alice@acme.com", Date: "2026-08-01T10:00:00Z",
            ThreadId: "thread-123", Snippet: "Please review the attached draft.");

        state.Should().BeOfType<WorkspaceTabVisibleState.Email>();
        state.WidgetType.Should().Be("Email");
        state.Should().NotBeOfType<WorkspaceTabVisibleState.Dashboard>();
    }

    [Fact]
    public void FormatVisibleStateFields_EmailActiveTab_EmitsSubjectFromDateThreadAndSnippet()
    {
        var state = new WorkspaceTabVisibleState.Email(
            Subject: "Re: NDA review", From: "alice@acme.com", Date: "2026-08-01T10:00:00Z",
            ThreadId: "thread-123", Snippet: "Please review the attached draft.");

        var result = SprkChatAgentFactory.FormatVisibleStateFields(state, contentVisible: true);

        result.Should().Contain("subject: Re: NDA review");
        result.Should().Contain("from: alice@acme.com");
        result.Should().Contain("date: 2026-08-01T10:00:00Z");
        result.Should().Contain("threadId: thread-123");
        result.Should().Contain("snippet: Please review the attached draft.");
    }

    [Fact]
    public void TryDeriveVisibleState_EmailWidgetData_DerivesEmailVisibleState_WithCappedSnippet()
    {
        var longSnippet = new string('a', 250);
        var tab = MakeTab("email-1", widgetType: "Email",
            widgetData: new EmailTabWidgetData
            {
                EmlDocumentId = "eml-doc-999",
                Subject = "Re: NDA review",
                From = "alice@acme.com",
                Date = "2026-08-01T10:00:00Z",
                ThreadId = "thread-123",
                Snippet = longSnippet,
            });

        var state = SprkChatAgentFactory.TryDeriveVisibleState(tab);

        state.Should().BeOfType<WorkspaceTabVisibleState.Email>();
        var email = (WorkspaceTabVisibleState.Email)state!;
        email.Subject.Should().Be("Re: NDA review");
        email.Snippet.Should().HaveLength(201); // 200 chars + ellipsis
        email.Snippet.Should().StartWith(new string('a', 200));
    }

    [Fact]
    public void EmailTabWidgetData_JsonRoundTrip_KindDiscriminatorDeserializesToEmailTabWidgetData()
    {
        WorkspaceTabWidgetData original = new EmailTabWidgetData
        {
            EmlDocumentId = "eml-doc-999",
            Subject = "Re: NDA review",
            From = "alice@acme.com",
            Date = "2026-08-01T10:00:00Z",
            ThreadId = "thread-123",
            Snippet = "Please review the attached draft.",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        json.Should().Contain("\"kind\":\"Email\"");

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<WorkspaceTabWidgetData>(json);

        roundTripped.Should().BeOfType<EmailTabWidgetData>();
        ((EmailTabWidgetData)roundTripped!).Subject.Should().Be("Re: NDA review");
    }

    [Fact]
    public void TryReservePromptBudget_DeniesWhenOverLimit_FragmentMustBeOmitted()
    {
        var stubTracker = new StubTrackerOverBudget();
        var bigFragment = string.Join(' ', Enumerable.Repeat("token", 200));

        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: stubTracker, layer: "workspace-state", fragment: bigFragment,
            sessionId: Guid.NewGuid(), tenantId: "tenant-test");

        granted.Should().BeFalse();
        stubTracker.LastLayer.Should().Be("workspace-state");
        stubTracker.LastRequestedTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryReservePromptBudget_GrantsWhenWithinLimit()
    {
        var stubTracker = new StubTrackerWithBudget(remaining: 10_000);

        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: stubTracker, layer: "workspace-state", fragment: "short block",
            sessionId: Guid.NewGuid(), tenantId: "tenant-test");

        granted.Should().BeTrue();
        stubTracker.LastLayer.Should().Be("workspace-state");
    }

    [Fact]
    public void TryReservePromptBudget_NullTracker_PassesThrough_LegacyBehavior()
    {
        var granted = SprkChatAgentFactory.TryReservePromptBudget(
            tracker: null, layer: "workspace-state", fragment: "anything", sessionId: null, tenantId: null);

        granted.Should().BeTrue();
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

    /// <summary>Test subclass that exposes the protected ctor.</summary>
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }
}
