using Sprk.Bff.Api.Models.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// FR-12 tool economy (spaarkeai-assistant-enhancements-r3 task 030) — resolves a workspace tab's
/// closed-vocabulary <c>WidgetContextType</c> from its <c>widgetType</c> (and, as a coarse fallback, its
/// typed <c>WidgetData.Kind</c>), so the ADR-039 deterministic tool-list pre-filter can scope parity
/// capabilities to the tabs actually open.
/// </summary>
/// <remarks>
/// <para>
/// <b>C# mirror of a client contract (not a parallel registry)</b>: the authoritative widget-type →
/// context-type map lives on the client widget registry as each widget's declared
/// <c>WidgetMetadata.contextType</c> (task 020), surfaced by <c>getWidgetContextTypeMap()</c> in
/// <c>src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts</c> and assigned per
/// widget in <c>register-workspace-widgets.ts</c> / <c>register-document-viewer-widget.ts</c>. The BFF
/// cannot run that JS registry, so this static map is its server-side mirror — the same "C# mirror of the
/// TypeScript contract" pattern used by <see cref="WorkspaceTab"/> and
/// <see cref="WorkspaceTabWidgetData"/>. Values are the closed <c>WidgetContextType</c> vocabulary
/// (<c>email</c>, <c>document</c>, <c>compose-doc</c>, <c>matter-grid</c>, <c>dashboard</c>,
/// <c>calendar</c>), aligned to <see cref="PublicContracts.Binding.ContextTypeTags"/>.
/// </para>
/// <para>
/// <b>ADR-039</b>: this is DATA for deterministic context-scoping pre-filtering — a static structural
/// map from a widget's declared category, NOT a classifier, reranker, keyword/intent map, or any decider.
/// A widget with no honest fit among the six values resolves to <c>null</c> (mirroring the client's
/// <c>undefined</c>) — an absence, not a guess.
/// </para>
/// <para>
/// <b>ADR-015</b>: reads only the tab's <c>widgetType</c> string and typed payload category — never item
/// content.
/// </para>
/// </remarks>
internal static class WidgetContextTypeResolver
{
    /// <summary>
    /// Fine-grained mirror keyed by the widget-registry id the client persists into
    /// <c>StoredWorkspaceTab.widgetType</c>. Ordinal-case-insensitive. Covers the full closed vocabulary,
    /// including the values (<c>compose-doc</c>, <c>calendar</c>) that the coarse Pillar-9 category cannot
    /// express. Kept in sync with the client <c>WidgetMetadata.contextType</c> declarations (task 020/022).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> WidgetTypeToContextType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // email
            ["email"] = WidgetContextTypes.Email,

            // document (single-doc viewers + clause comparison)
            ["document-viewer"] = WidgetContextTypes.Document,
            ["redline-viewer"] = WidgetContextTypes.Document,
            ["ContractComparison"] = WidgetContextTypes.Document,

            // compose-doc (Compose / ADR-049 drafting surface)
            ["compose"] = WidgetContextTypes.ComposeDoc,

            // matter-grid (entity-view grids all share one context-type)
            ["documents-list"] = WidgetContextTypes.MatterGrid,
            ["matters-list"] = WidgetContextTypes.MatterGrid,
            ["projects-list"] = WidgetContextTypes.MatterGrid,
            ["invoices-list"] = WidgetContextTypes.MatterGrid,
            ["work-assignments-list"] = WidgetContextTypes.MatterGrid,
            ["my-tasks-list"] = WidgetContextTypes.MatterGrid,
            ["communications-list"] = WidgetContextTypes.MatterGrid,

            // dashboard (embedded LegalWorkspaceApp layouts)
            ["workspace"] = WidgetContextTypes.Dashboard,
            ["matters-dashboard"] = WidgetContextTypes.Dashboard,

            // calendar (Pattern D calendar layout tab)
            ["calendar"] = WidgetContextTypes.Calendar,
        };

    /// <summary>
    /// Coarse fallback keyed by the server-side typed Pillar-9 <c>WidgetData.Kind</c> discriminator
    /// (<see cref="WorkspaceTabWidgetData"/>, 5 closed values). Used only when a tab's fine-grained
    /// <c>widgetType</c> is not in <see cref="WidgetTypeToContextType"/> but its typed payload category is
    /// unambiguous. <c>Summary</c> is deliberately absent — the summary widgets declare NO context-type on
    /// the client (honest "no fit"); mapping it here would invent a relevance the client does not assert.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KindToContextType =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Email"] = WidgetContextTypes.Email,
            ["DocumentViewer"] = WidgetContextTypes.Document,
            ["Table"] = WidgetContextTypes.MatterGrid,
            ["Dashboard"] = WidgetContextTypes.Dashboard,
        };

    /// <summary>
    /// Resolves the closed <c>WidgetContextType</c> for a tab identified by its
    /// <paramref name="widgetType"/> (primary, authoritative) with an optional typed-category
    /// <paramref name="kind"/> fallback. Returns <c>null</c> when neither maps to one of the six values.
    /// </summary>
    public static string? Resolve(string? widgetType, string? kind)
    {
        if (!string.IsNullOrWhiteSpace(widgetType)
            && WidgetTypeToContextType.TryGetValue(widgetType, out var byType))
        {
            return byType;
        }

        if (!string.IsNullOrWhiteSpace(kind)
            && KindToContextType.TryGetValue(kind, out var byKind))
        {
            return byKind;
        }

        return null;
    }

    /// <summary>Resolves the closed <c>WidgetContextType</c> for a live <see cref="WorkspaceTab"/>.</summary>
    public static string? Resolve(WorkspaceTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        return Resolve(tab.WidgetType, tab.WidgetData?.Kind);
    }

    /// <summary>
    /// Projects a set of live open tabs to the deterministic set of their context-types (the FR-12
    /// <c>OpenTabContextTypes</c> pre-filter fact). Ordinal-case-insensitive; unmapped tabs contribute
    /// nothing. Order-independent (a set) so the projection is prompt-cache-stable (NFR-04).
    /// </summary>
    public static IReadOnlyCollection<string> ResolveOpenTabContextTypes(IEnumerable<WorkspaceTab> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in tabs)
        {
            if (tab is null)
            {
                continue;
            }

            var contextType = Resolve(tab);
            if (contextType is not null)
            {
                set.Add(contextType);
            }
        }

        return set;
    }
}

/// <summary>
/// The closed <c>WidgetContextType</c> vocabulary (task 020) — the C# mirror of the client
/// <c>WidgetContextType</c> union in
/// <c>src/client/shared/Spaarke.AI.Widgets/src/types/shared.ts</c>. Aligned to the
/// <see cref="PublicContracts.Binding.ContextTypeTags"/> tag vocabulary. Do not widen ad hoc.
/// </summary>
internal static class WidgetContextTypes
{
    public const string Email = "email";
    public const string Document = "document";
    public const string ComposeDoc = "compose-doc";
    public const string MatterGrid = "matter-grid";
    public const string Dashboard = "dashboard";
    public const string Calendar = "calendar";
}
