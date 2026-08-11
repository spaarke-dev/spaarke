using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Unit tests for <see cref="ChatEndpoints.MapStoredTabsToWorkspaceTabs"/> — the re-point
/// linchpin (R3 task 011, FR-03) between the write-through <c>StoredSession.Tabs</c> store
/// (client PATCH /sessions/{id}/tabs) and the <see cref="SprkChatAgentFactory.BuildWorkspaceStateBlock"/>
/// prompt block. <see cref="ChatEndpoints.MapStoredTabsToWorkspaceTabs"/> is <c>internal</c>
/// (not <c>private</c>) specifically so this mapping is testable directly via
/// <c>InternalsVisibleTo</c> (see .csproj) — the same precedent as
/// <see cref="ChatEndpoints.SelectMostRecentSession"/>
/// (<c>ChatEndpointsSessionByAnalysisTests</c>); tests CLAUDE.md B8 bans reflection into
/// PRIVATE members, not direct calls to an internal member.
///
/// Covers:
///   - Empty stored tabs → empty list, no throw.
///   - A layout/compose tab (opaque <c>widgetData</c> carrying no <c>kind</c> discriminator)
///     maps to a <see cref="WorkspaceTab"/> with a null <c>WidgetData</c> but a preserved
///     <c>DisplayName</c> — and that mapped tab is then proven, end-to-end, to surface in
///     <see cref="SprkChatAgentFactory.BuildWorkspaceStateBlock"/> by its DisplayName
///     (the task-010 layout-identity + task-011 re-point seam).
///   - A typed (Email) tab maps id/widgetType/displayName plus its typed widget data.
///   - Mapping preserves the client's left-to-right tab order via descending synthetic
///     <c>UpdatedAt</c> timestamps (index 0 = most recent).
/// </summary>
public class ChatEndpointsMapStoredTabsTests
{
    private const string SessionId = "session-map-tests";
    private const string TenantId = "tenant-map-tests";

    private static StoredWorkspaceTab MakeStoredTab(
        string id,
        string widgetType,
        string? displayName,
        string? widgetDataJson = null)
    {
        JsonElement? widgetData = widgetDataJson is null
            ? null
            : JsonDocument.Parse(widgetDataJson).RootElement;

        return new StoredWorkspaceTab(
            Id: id,
            WidgetType: widgetType,
            WidgetData: widgetData,
            DisplayName: displayName ?? string.Empty);
    }

    // ---------------------------------------------------------------------
    // (d) Empty stored tabs → empty list, no throw.
    // ---------------------------------------------------------------------

    [Fact]
    public void MapStoredTabsToWorkspaceTabs_EmptyList_ReturnsEmptyList()
    {
        var result = ChatEndpoints.MapStoredTabsToWorkspaceTabs(
            Array.Empty<StoredWorkspaceTab>(), SessionId, TenantId);

        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // (a) Layout tab, kind-less widgetData → survives mapping by DisplayName.
    // ---------------------------------------------------------------------

    [Fact]
    public void MapStoredTabsToWorkspaceTabs_LayoutTabWithNoKindWidgetData_MapsWithNullWidgetDataAndDisplayName()
    {
        // Layout tabs persist an opaque shape with no `kind` discriminator (see the
        // production doc comment on MapStoredTabsToWorkspaceTabs) — polymorphic
        // deserialization against WorkspaceTabWidgetData fails and is caught, yielding a
        // null WidgetData. The tab must still round-trip its identity fields.
        var stored = MakeStoredTab(
            id: "tab-layout-1",
            widgetType: "workspace",
            displayName: "My Layout",
            widgetDataJson: """{"columns":2,"panes":["a","b"]}""");

        var result = ChatEndpoints.MapStoredTabsToWorkspaceTabs(
            new[] { stored }, SessionId, TenantId);

        result.Should().HaveCount(1);
        var mapped = result[0];
        mapped.Id.Should().Be("tab-layout-1");
        mapped.WidgetType.Should().Be("workspace");
        mapped.DisplayName.Should().Be("My Layout");
        mapped.WidgetData.Should().BeNull();
        mapped.SessionId.Should().Be(SessionId);
        mapped.TenantId.Should().Be(TenantId);
        // No visibleToAssistant field on StoredWorkspaceTab → live tabs default visible
        // (see the production doc comment's "visibleToAssistant reconciliation" remarks).
        mapped.VisibleToAssistant.Should().BeTrue();
    }

    [Fact]
    public void MapStoredTabsToWorkspaceTabs_LayoutTabOutput_SurfacesInWorkspaceStateBlockByDisplayName()
    {
        // End-to-end proof of the re-point: a tab present ONLY via the mapped
        // session.Tabs output — with a state-less (null) WidgetData — still appears in the
        // Workspace State block, labeled by its DisplayName. This is exactly the case that
        // motivated FR-03: layout tabs have no derivable typed visible state, so without the
        // DisplayName arm in BuildWorkspaceStateBlock's filter they would silently vanish
        // from the block despite being open in the client's tab strip.
        var stored = MakeStoredTab(
            id: "tab-layout-1",
            widgetType: "workspace",
            displayName: "My Layout",
            widgetDataJson: """{"columns":2}""");

        var mapped = ChatEndpoints.MapStoredTabsToWorkspaceTabs(
            new[] { stored }, SessionId, TenantId);

        var factory = CreateFactory();
        var block = factory.BuildWorkspaceStateBlock(mapped, SessionId);

        block.Should().Contain("## Workspace State");
        block.Should().Contain("widgetType=workspace label=\"My Layout\"");
    }

    // ---------------------------------------------------------------------
    // (b) Typed (Email) tab — id/widgetType/displayName + typed widget data.
    // ---------------------------------------------------------------------

    [Fact]
    public void MapStoredTabsToWorkspaceTabs_EmailTab_MapsIdWidgetTypeDisplayNameAndTypedWidgetData()
    {
        var stored = MakeStoredTab(
            id: "tab-email-1",
            widgetType: "Email",
            displayName: "Fwd: Contract Review",
            widgetDataJson: """{"kind":"Email","subject":"Contract Review","from":"jane@example.com","date":"2026-08-01T10:00:00Z"}""");

        var result = ChatEndpoints.MapStoredTabsToWorkspaceTabs(
            new[] { stored }, SessionId, TenantId);

        result.Should().HaveCount(1);
        var mapped = result[0];
        mapped.Id.Should().Be("tab-email-1");
        mapped.WidgetType.Should().Be("Email");
        mapped.DisplayName.Should().Be("Fwd: Contract Review");
        mapped.WidgetData.Should().BeOfType<EmailTabWidgetData>();
        ((EmailTabWidgetData)mapped.WidgetData).Subject.Should().Be("Contract Review");
        ((EmailTabWidgetData)mapped.WidgetData).From.Should().Be("jane@example.com");
    }

    // ---------------------------------------------------------------------
    // (c) Order preservation via synthetic descending UpdatedAt.
    // ---------------------------------------------------------------------

    [Fact]
    public void MapStoredTabsToWorkspaceTabs_MultipleTabs_PreservesClientOrderViaDescendingSyntheticTimestamps()
    {
        // The client's left-to-right tab-strip order must survive BuildWorkspaceStateBlock's
        // OrderByDescending(UpdatedAt) — so the mapping stamps synthetic, strictly-descending
        // timestamps in list order (earlier index = later timestamp).
        var stored = new[]
        {
            MakeStoredTab("tab-a", "Summary", "First"),
            MakeStoredTab("tab-b", "Summary", "Second"),
            MakeStoredTab("tab-c", "Summary", "Third"),
        };

        var result = ChatEndpoints.MapStoredTabsToWorkspaceTabs(stored, SessionId, TenantId);

        result.Should().HaveCount(3);
        result[0].Id.Should().Be("tab-a");
        result[1].Id.Should().Be("tab-b");
        result[2].Id.Should().Be("tab-c");
        // Strictly descending — index 0's UpdatedAt sorts after (later than) index 1's, etc.
        string.CompareOrdinal(result[0].UpdatedAt, result[1].UpdatedAt).Should().BeGreaterThan(0);
        string.CompareOrdinal(result[1].UpdatedAt, result[2].UpdatedAt).Should().BeGreaterThan(0);
    }

    private static SprkChatAgentFactory CreateFactory()
    {
        return new TestableSprkChatAgentFactory(NullLogger<SprkChatAgentFactory>.Instance);
    }

    /// <summary>Test subclass that exposes the protected ctor (mirrors SprkChatAgentFactoryWorkspaceStateTests).</summary>
    private sealed class TestableSprkChatAgentFactory : SprkChatAgentFactory
    {
        public TestableSprkChatAgentFactory(ILogger<SprkChatAgentFactory> logger) : base(logger) { }
    }
}
