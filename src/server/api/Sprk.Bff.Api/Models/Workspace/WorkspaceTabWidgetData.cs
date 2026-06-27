using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Workspace;

/// <summary>
/// Discriminated union of per-variant workspace-tab widget-data shapes (R6 Pillar 6a).
///
/// <para>
/// Mirrors the TypeScript <c>WorkspaceTabWidgetData</c> union in
/// <c>src/client/shared/Spaarke.AI.Widgets/src/types/WorkspaceTab.ts</c>.
/// </para>
///
/// <para>
/// Polymorphism: System.Text.Json reads the <c>kind</c> JSON property as the type
/// discriminator. Round-trip serialization preserves the concrete subtype.
/// </para>
///
/// <para>
/// Pillar 9 prompt builder consumes typed instances of these subtypes to produce per-tab
/// agent-visible state snapshots (4-variant prompt categories).
/// </para>
/// </summary>
[JsonPolymorphic(
    TypeDiscriminatorPropertyName = "kind",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
    IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(SummaryTabWidgetData), typeDiscriminator: "Summary")]
[JsonDerivedType(typeof(DocumentViewerTabWidgetData), typeDiscriminator: "DocumentViewer")]
[JsonDerivedType(typeof(DashboardTabWidgetData), typeDiscriminator: "Dashboard")]
[JsonDerivedType(typeof(TableTabWidgetData), typeDiscriminator: "Table")]
public abstract class WorkspaceTabWidgetData
{
    /// <summary>
    /// Discriminator literal — MUST equal the parent tab's <c>widgetType</c>. Read-only
    /// (not serialized; the <c>"kind"</c> wire property is emitted by the System.Text.Json
    /// polymorphism metadata layer based on the concrete subtype). Provided for in-process
    /// pattern matching + the <see cref="WorkspaceTab.WidgetType"/> consistency check.
    /// </summary>
    [JsonIgnore]
    public abstract string Kind { get; }
}

/// <summary>
/// Widget data for a <c>Summary</c> tab (TL;DR + markdown body). Pillar 9 visible state:
/// <c>{ widgetType, summary, tldr, hasUserEdits }</c>.
/// </summary>
public sealed class SummaryTabWidgetData : WorkspaceTabWidgetData
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => "Summary";

    /// <summary>Optional TL;DR line presented at the top of the tab.</summary>
    [JsonPropertyName("tldr")]
    public string? Tldr { get; init; }

    /// <summary>Full summary body (markdown). Pillar 9 may truncate for token budget.</summary>
    [JsonPropertyName("body")]
    public required string Body { get; init; }

    /// <summary>True when the user has edited the body after agent generation.</summary>
    [JsonPropertyName("hasUserEdits")]
    public bool? HasUserEdits { get; init; }
}

/// <summary>
/// Widget data for a <c>DocumentViewer</c> tab. Pillar 9 visible state:
/// <c>{ widgetType, filename, mimeType, sizeBytes, hasSelection, selectionText? }</c>.
/// </summary>
public sealed class DocumentViewerTabWidgetData : WorkspaceTabWidgetData
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => "DocumentViewer";

    /// <summary>Dataverse document record id (or transient id for unsaved uploads).</summary>
    [JsonPropertyName("documentId")]
    public required string DocumentId { get; init; }

    /// <summary>Display filename (e.g. <c>engagement-letter.docx</c>).</summary>
    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    /// <summary>MIME type (e.g. <c>application/pdf</c>).</summary>
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; init; }

    /// <summary>File size in bytes (may be 0 for stream-only previews).</summary>
    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    /// <summary>True when the user has a live non-empty text selection.</summary>
    [JsonPropertyName("hasSelection")]
    public bool? HasSelection { get; init; }

    /// <summary>
    /// Selection text when <see cref="HasSelection"/> is true. Pillar 9 includes this in
    /// the agent prompt only when <c>visibleToAssistant === true</c>.
    /// </summary>
    [JsonPropertyName("selectionText")]
    public string? SelectionText { get; init; }
}

/// <summary>
/// Widget data for a <c>Dashboard</c> tab (LegalWorkspaceApp embedded mode). Pillar 9
/// visible state: <c>{ widgetType, dashboardName, lastViewedSection }</c> — deliberately
/// NOT chart data (payload minimization, NFR-10).
/// </summary>
public sealed class DashboardTabWidgetData : WorkspaceTabWidgetData
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => "Dashboard";

    /// <summary>Dataverse <c>sprk_workspacelayout</c> GUID.</summary>
    [JsonPropertyName("layoutId")]
    public required string LayoutId { get; init; }

    /// <summary>
    /// Display name of the layout (e.g. <c>"Corporate Workspace"</c>, <c>"Calendar"</c>).
    /// </summary>
    [JsonPropertyName("dashboardName")]
    public required string DashboardName { get; init; }

    /// <summary>Last section id the user interacted with — Pillar 9 visible.</summary>
    [JsonPropertyName("lastViewedSection")]
    public string? LastViewedSection { get; init; }
}

/// <summary>
/// Widget data for a <c>Table</c> tab (sortable/filterable grid). Pillar 9 visible state:
/// <c>{ widgetType, rowCount, sortColumn, filteredColumns, selectedRows[] }</c> — NOT the
/// raw rows (token economy).
/// </summary>
public sealed class TableTabWidgetData : WorkspaceTabWidgetData
{
    /// <inheritdoc/>
    [JsonIgnore]
    public override string Kind => "Table";

    /// <summary>Total row count after filtering.</summary>
    [JsonPropertyName("rowCount")]
    public required int RowCount { get; init; }

    /// <summary>Current sort column id (e.g. <c>"createdOn"</c>). Null if unsorted.</summary>
    [JsonPropertyName("sortColumn")]
    public string? SortColumn { get; init; }

    /// <summary>Current sort direction (<c>"asc"</c> | <c>"desc"</c>). Null if unsorted.</summary>
    [JsonPropertyName("sortDirection")]
    public string? SortDirection { get; init; }

    /// <summary>Column ids with active filters applied.</summary>
    [JsonPropertyName("filteredColumns")]
    public required IReadOnlyList<string> FilteredColumns { get; init; }

    /// <summary>Row ids currently selected by the user (empty when no selection).</summary>
    [JsonPropertyName("selectedRows")]
    public required IReadOnlyList<string> SelectedRows { get; init; }

    /// <summary>Optional stable data-source id (e.g. FetchXML id) for re-fetch on restore.</summary>
    [JsonPropertyName("dataSourceId")]
    public string? DataSourceId { get; init; }
}
