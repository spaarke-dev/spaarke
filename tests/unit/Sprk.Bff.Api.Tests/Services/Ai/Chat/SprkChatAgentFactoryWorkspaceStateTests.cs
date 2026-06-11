using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for R6 task 053 (Pillar 6a / FR-34) — Workspace State block in
/// <see cref="SprkChatAgentFactory.BuildWorkspaceStateBlock(IReadOnlyList{WorkspaceTab}, string)"/>.
///
/// Covers:
///   - Empty / all-hidden inputs → empty string (no-op)
///   - Single visible tab → "Tab 1 (active)" line with widget type
///   - Multiple visible tabs → most-recent UpdatedAt labeled "(active)"
///   - Pinned tabs carry "user-pinned" marker
///   - Truncation when block would exceed <see cref="SprkChatAgentFactory.WorkspaceStateBlockMaxChars"/>
///   - ADR-015: NO raw user message text leaks (block carries widget type + matterName only)
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
        string updatedAt = "2026-06-10T12:00:00Z")
    {
        return new WorkspaceTab
        {
            Id = id,
            WidgetType = widgetType,
            WidgetData = new SummaryTabWidgetData
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
        result.Should().Contain("Tab 1 (active): Summary");
        result.Should().Contain("matter: Acme v. Beta");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_MultipleVisible_MostRecentLabeledActive()
    {
        var factory = CreateFactory();
        var tabs = new[]
        {
            MakeTab("older", widgetType: "DocumentViewer", updatedAt: "2026-06-09T10:00:00Z"),
            MakeTab("newer", widgetType: "Summary", updatedAt: "2026-06-10T15:00:00Z"),
        };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // First listed (Tab 1) = active = most recent UpdatedAt = "Summary"
        result.Should().Contain("Tab 1 (active): Summary");
        // Second listed = non-active = "DocumentViewer"
        result.Should().Contain("Tab 2: DocumentViewer");
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
    public void BuildWorkspaceStateBlock_StaysWithinCharBudget_WhenManyTabs()
    {
        var factory = CreateFactory();
        // 30 tabs with long matter names — likely to exceed the 500-char cap.
        var longName = new string('M', 80);
        var tabs = Enumerable.Range(0, 30)
            .Select(i => MakeTab($"t{i}", widgetType: "Summary", matterName: longName))
            .ToList();

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // Block respects the budget — does not balloon past the cap by a lot.
        // (Allow some slack for the header line, but cap is the binding contract.)
        result.Length.Should().BeLessOrEqualTo(SprkChatAgentFactory.WorkspaceStateBlockMaxChars + 100);
        // First tab is always included.
        result.Should().Contain("Tab 1 (active)");
    }

    [Fact]
    public void BuildWorkspaceStateBlock_NeverIncludesArbitraryUserText_ADR015()
    {
        var factory = CreateFactory();
        // Construct a tab with a "user-injected" matter name to verify the block
        // structurally CANNOT carry raw user message text. The matter name IS
        // allowed (it's a deterministic identifier), but no widgetData payload
        // ever appears in the block.
        var tabs = new[] { MakeTab("t1", widgetType: "Summary", matterName: "PROBE_USER_INJECT_SHOULD_NOT_LEAK_AS_PROMPT") };

        var result = factory.BuildWorkspaceStateBlock(tabs, TestSessionId);

        // Matter name does appear by design (it's the deterministic anchor).
        result.Should().Contain("PROBE_USER_INJECT_SHOULD_NOT_LEAK_AS_PROMPT");
        // But the block must not carry any of these structural payload markers
        // — verifies no widget-data serialization leaked into the prompt.
        result.Should().NotContain("widgetData");
        result.Should().NotContain("hasUserEdits");
        result.Should().NotContain("recommendations");
        // No "Tldr"/"Summary" field-name leakage from widgetData either.
        result.Should().NotMatch("*\"tldr\"*");
        result.Should().NotMatch("*\"summary\"*");
    }

    /// <summary>
    /// Test subclass that exposes the protected ctor.
    /// </summary>
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }
}
