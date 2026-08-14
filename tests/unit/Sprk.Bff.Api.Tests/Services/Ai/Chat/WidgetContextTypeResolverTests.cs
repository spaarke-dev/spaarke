using FluentAssertions;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-12 tool economy (spaarkeai-assistant-enhancements-r3 task 030) — the server-side C# mirror of the
/// client widget-registry <c>WidgetMetadata.contextType</c> map. Verifies the deterministic
/// widgetType → closed-<c>WidgetContextType</c> resolution that feeds the ADR-039 open-tab tool-economy
/// pre-filter. Pure static data map — never a classifier (no utterance, no model).
/// </summary>
public class WidgetContextTypeResolverTests
{
    [Theory]
    [InlineData("email", "email")]
    [InlineData("matters-list", "matter-grid")]
    [InlineData("documents-list", "matter-grid")]
    [InlineData("my-tasks-list", "matter-grid")]
    [InlineData("document-viewer", "document")]
    [InlineData("ContractComparison", "document")]
    [InlineData("compose", "compose-doc")]
    [InlineData("workspace", "dashboard")]
    [InlineData("calendar", "calendar")]
    public void Resolve_KnownWidgetType_ReturnsMirroredContextType(string widgetType, string expected)
    {
        WidgetContextTypeResolver.Resolve(widgetType, kind: null).Should().Be(expected,
            "the server map mirrors the client widget-registry contextType declaration for this widgetType");
    }

    [Fact]
    public void Resolve_UnknownWidgetType_ReturnsNull_HonestNoFit()
    {
        WidgetContextTypeResolver.Resolve("analysis-editor", kind: null).Should().BeNull(
            "a widget with no honest fit among the six values maps to null (mirrors the client 'undefined')");
    }

    [Fact]
    public void Resolve_UnknownWidgetType_FallsBackToTypedKind()
    {
        // A tab whose fine-grained widgetType is unmapped but whose typed Pillar-9 category is unambiguous.
        WidgetContextTypeResolver.Resolve("some-future-grid", kind: "Table").Should().Be("matter-grid");
        WidgetContextTypeResolver.Resolve("some-future-email", kind: "Email").Should().Be("email");
    }

    [Fact]
    public void Resolve_SummaryKind_ReturnsNull_NotInvented()
    {
        // Summary widgets declare NO contextType on the client — the server must not invent one.
        WidgetContextTypeResolver.Resolve(widgetType: null, kind: "Summary").Should().BeNull();
    }

    [Fact]
    public void ResolveOpenTabContextTypes_ChangingTabSet_ChangesResolvedSetDeterministically()
    {
        var matterTab = MakeTab("matters-list");
        var emailTab = MakeTab("email");

        // Only the matters grid open.
        WidgetContextTypeResolver.ResolveOpenTabContextTypes(new[] { matterTab })
            .Should().BeEquivalentTo(new[] { "matter-grid" });

        // Open the email tab too.
        WidgetContextTypeResolver.ResolveOpenTabContextTypes(new[] { matterTab, emailTab })
            .Should().BeEquivalentTo(new[] { "matter-grid", "email" });

        // Close the email tab.
        WidgetContextTypeResolver.ResolveOpenTabContextTypes(new[] { matterTab })
            .Should().BeEquivalentTo(new[] { "matter-grid" });
    }

    [Fact]
    public void ResolveOpenTabContextTypes_DuplicateContextTypes_AreDeduplicated()
    {
        var tabs = new[] { MakeTab("matters-list"), MakeTab("documents-list"), MakeTab("projects-list") };

        WidgetContextTypeResolver.ResolveOpenTabContextTypes(tabs)
            .Should().BeEquivalentTo(new[] { "matter-grid" },
                "three matter-grid grids collapse to a single context-type (a set)");
    }

    private static WorkspaceTab MakeTab(string widgetType) => new()
    {
        Id = System.Guid.NewGuid().ToString(),
        WidgetType = widgetType,
        WidgetData = new SummaryTabWidgetData { Body = "x" },
        SessionId = "session-1",
        TenantId = "tenant-1",
        VisibleToAssistant = true,
        SourceProvenance = new WorkspaceTabSourceProvenance
        {
            Source = "user",
            CreatedBy = System.Guid.NewGuid().ToString(),
            CreatedAt = "2026-08-11T00:00:00Z",
        },
        MatterContext = new WorkspaceTabMatterContext { MatterId = "m1", MatterName = "M" },
        IsPinned = false,
        CanEdit = true,
        CreatedAt = "2026-08-11T00:00:00Z",
        UpdatedAt = "2026-08-11T00:00:00Z",
    };
}
