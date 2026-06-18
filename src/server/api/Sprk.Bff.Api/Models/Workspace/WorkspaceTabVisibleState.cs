namespace Sprk.Bff.Api.Models.Workspace;

/// <summary>
/// R6 Task 074 (Pillar 9 / FR-57) — discriminated union of agent-visible state shapes
/// derived from <see cref="WorkspaceTabWidgetData"/> at the BFF. Mirrors the frontend
/// <c>SerializedWidgetState</c> contract returned by per-widget
/// <c>getAgentVisibleState()</c> implementations (task 073).
///
/// <para>
/// <b>Why server-side derivation (OPTION A) over persisted-state (OPTION B)</b>:
/// <list type="bullet">
///   <item>The <see cref="WorkspaceTabWidgetData"/> polymorphic union ALREADY carries
///   the deterministic fields needed for the FR-57 shapes — XML doc comments on every
///   subtype quote the exact shape.</item>
///   <item>Server-derivation makes the FR-57 contract structurally enforced at the BFF:
///   a new widget category cannot accidentally leak more than the closed-union models
///   permit because the derivation switch requires explicit handling.</item>
///   <item>No schema change, no frontend wiring, no migration story for existing
///   persisted tabs.</item>
/// </list>
/// Trade-off accepted: per-widget mapping logic exists in both client (task 073) and
/// server (this file). Both must be kept in sync; the FR-57 spec is the single source.
/// </para>
///
/// <para>
/// <b>ADR-015 BINDING</b>: only the FR-57 deterministic fields appear in subtype
/// definitions. Summary's full <c>body</c> is NOT a field — only a truncated
/// <c>summary</c> projection alongside <c>tldr</c> + <c>hasUserEdits</c>. DocumentViewer's
/// <c>selectionText</c> is the only content-bearing field; respected with a 200-char cap
/// per task 073's frontend contract. Dashboard never carries chart data. Table never
/// carries raw rows or selected-row IDs — only counts.
/// </para>
///
/// <para>
/// <b>Privacy default (FR-59)</b>: <see cref="SprkChatAgentFactory.TryDeriveVisibleState"/>
/// returns <c>null</c> for tabs whose widget data lacks renderable state (e.g., Summary
/// with both <c>Tldr</c> and <c>Body</c> empty). Null-returning tabs are filtered OUT of
/// the per-turn prompt block alongside tabs whose <c>VisibleToAssistant</c> is false.
/// </para>
/// </summary>
public abstract record WorkspaceTabVisibleState
{
    /// <summary>Closed-union discriminator (kept as the frontend expects).</summary>
    public abstract string WidgetType { get; }

    /// <summary>
    /// Visible state for a Summary tab. Shape: <c>{ widgetType, summary, tldr,
    /// hasUserEdits }</c>. <see cref="SummaryText"/> is a truncated projection of the body
    /// (NOT the raw body).
    /// </summary>
    public sealed record Summary(string? Tldr, string? SummaryText, bool HasUserEdits) : WorkspaceTabVisibleState
    {
        public override string WidgetType => "Summary";
    }

    /// <summary>
    /// Visible state for a DocumentViewer tab. Shape: <c>{ widgetType, filename,
    /// mimeType, sizeBytes, hasSelection, selectionText? }</c>. <see cref="SelectionText"/>
    /// is the only content-bearing field; capped at 200 chars upstream.
    /// </summary>
    public sealed record DocumentViewer(
        string Filename,
        string MimeType,
        long SizeBytes,
        bool HasSelection,
        string? SelectionText) : WorkspaceTabVisibleState
    {
        public override string WidgetType => "DocumentViewer";
    }

    /// <summary>
    /// Visible state for a Dashboard tab. Shape: <c>{ widgetType, dashboardName,
    /// lastViewedSection }</c>. Deliberately omits chart data per FR-57.
    /// </summary>
    public sealed record Dashboard(string DashboardName, string? LastViewedSection) : WorkspaceTabVisibleState
    {
        public override string WidgetType => "Dashboard";
    }

    /// <summary>
    /// Visible state for a Table tab. Shape: <c>{ widgetType, rowCount, sortColumn,
    /// filteredColumns, selectedRows: number }</c>. <see cref="SelectedRows"/> is a COUNT
    /// (NOT a list of row IDs) — stricter than POML's <c>selectedRows[]</c> per the task
    /// 074 binding ("count, NOT row IDs — stricter than POML" per token economy).
    /// </summary>
    public sealed record Table(
        int RowCount,
        string? SortColumn,
        IReadOnlyList<string> FilteredColumns,
        int SelectedRows) : WorkspaceTabVisibleState
    {
        public override string WidgetType => "Table";
    }
}
