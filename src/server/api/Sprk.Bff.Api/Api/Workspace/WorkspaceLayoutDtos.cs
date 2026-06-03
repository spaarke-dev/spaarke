namespace Sprk.Bff.Api.Api.Workspace;

// ---------------------------------------------------------------------------
// Workspace Layout DTOs
// ---------------------------------------------------------------------------

/// <summary>
/// DTO representing a user's workspace layout configuration from Dataverse.
/// Maps to sprk_workspacelayout entity.
/// </summary>
public record WorkspaceLayoutDto
{
    /// <summary>Unique identifier of the workspace layout (sprk_workspacelayoutid).</summary>
    public Guid Id { get; init; }

    /// <summary>Display name of the layout.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Identifier of the layout template used (e.g., "2-column", "3-column").</summary>
    public string LayoutTemplateId { get; init; } = string.Empty;

    /// <summary>JSON-serialized array of section placements (sectionId + slotIndex pairs).</summary>
    public string SectionsJson { get; init; } = string.Empty;

    /// <summary>Whether this is the user's default layout.</summary>
    public bool IsDefault { get; init; }

    /// <summary>Sort order for layout display. Null if unordered.</summary>
    public int? SortOrder { get; init; }

    /// <summary>
    /// Whether this is a system-provided (non-editable) layout.
    /// SERVER-CONTROLLED. The property uses an init accessor so it can be
    /// populated by <see cref="Services.Workspace.WorkspaceLayoutService"/>
    /// during DTO construction. The request DTOs
    /// (<see cref="CreateWorkspaceLayoutRequest"/> and
    /// <see cref="UpdateWorkspaceLayoutRequest"/>) do NOT include this
    /// property — so clients have no way to set it on POST/PUT. The service
    /// populates this flag based on the data source:
    ///   - true when the record originates from <c>SystemWorkspaceLayouts.cs</c>
    ///     (hard-coded code constants such as "Corporate Workspace"), OR when
    ///     the Dataverse record's <c>sprk_issystem</c> column is true (the four
    ///     layouts seeded by <c>scripts/Deploy-SystemWorkspaceLayouts.ps1</c>).
    ///   - false for ordinary user-owned Dataverse records.
    /// Write protection: <see cref="Services.Workspace.WorkspaceLayoutService"/>
    /// rejects PUT/DELETE against any record whose IsSystem is true (403 Forbidden)
    /// as a defense-in-depth complement to client-side disable affordances.
    /// </summary>
    public bool IsSystem { get; init; }

    /// <summary>
    /// When the layout was last modified, as a UTC-anchored DateTimeOffset.
    /// SERVER-CONTROLLED — Dataverse maintains <c>modifiedon</c> automatically;
    /// the DTO surfaces it so the Manage Workspaces pane can render "Modified ..."
    /// per FR-07 (R4 task 053 / B-4) and so a future PATCH/If-Match concurrency
    /// surface (R4 task 054 / B-5) can use it as a strong validator / ETag value.
    ///
    /// Wire shape: ISO-8601 string (e.g., "2026-05-26T14:23:11+00:00"). The
    /// frontend type is <c>string</c>; consumer-side code formats with
    /// <c>Date.parse</c> + locale-aware formatting (do NOT pre-format on the
    /// server).
    ///
    /// Backfill behavior: <see cref="SystemWorkspaceLayouts.CorporateWorkspace"/>
    /// is a code constant (never persisted in Dataverse) so its <c>ModifiedOn</c>
    /// is initialized to the process start time as a deterministic-per-build
    /// placeholder. Dataverse-sourced records (system-seeded or user-owned)
    /// carry the real <c>modifiedon</c> attribute mapped in
    /// <see cref="Services.Workspace.WorkspaceLayoutService"/>.<c>MapToDto</c>.
    /// </summary>
    public DateTimeOffset ModifiedOn { get; init; }
}

// ---------------------------------------------------------------------------
// Request DTOs
// ---------------------------------------------------------------------------

/// <summary>
/// Request body for creating a new workspace layout.
/// </summary>
public record CreateWorkspaceLayoutRequest
{
    /// <summary>Display name of the layout.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Identifier of the layout template to use.</summary>
    public string LayoutTemplateId { get; init; } = string.Empty;

    /// <summary>JSON-serialized array of section placements.</summary>
    public string SectionsJson { get; init; } = string.Empty;

    /// <summary>Whether this should be set as the default layout.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// Request body for updating an existing workspace layout.
/// </summary>
public record UpdateWorkspaceLayoutRequest
{
    /// <summary>Display name of the layout.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Identifier of the layout template to use.</summary>
    public string LayoutTemplateId { get; init; } = string.Empty;

    /// <summary>JSON-serialized array of section placements.</summary>
    public string SectionsJson { get; init; } = string.Empty;

    /// <summary>Whether this should be set as the default layout.</summary>
    public bool IsDefault { get; init; }
}

// ---------------------------------------------------------------------------
// Section and Template DTOs
// ---------------------------------------------------------------------------

/// <summary>
/// DTO representing a workspace section available for placement in a layout.
/// </summary>
public record SectionDto
{
    /// <summary>Unique identifier of the section (e.g., "documents", "events").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display label for the section.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Description of the section's purpose.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Category grouping for the section (e.g., "core", "ai", "finance").</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Fluent UI icon name for the section.</summary>
    public string IconName { get; init; } = string.Empty;

    /// <summary>Default height hint for the section (e.g., "300px", "auto"). Null if no default.</summary>
    public string? DefaultHeight { get; init; }
}

/// <summary>
/// DTO representing a layout template that defines the grid structure.
/// </summary>
public record LayoutTemplateDto
{
    /// <summary>Unique identifier of the template (e.g., "2-column", "3-column").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Display name of the template.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Description of the template layout.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Row definitions for the template grid.</summary>
    public LayoutTemplateRowDto[] Rows { get; init; } = [];
}

/// <summary>
/// DTO representing a single row in a layout template grid.
/// </summary>
public record LayoutTemplateRowDto
{
    /// <summary>Unique identifier of the row within the template.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>CSS grid column definition for standard viewports (e.g., "1fr 1fr").</summary>
    public string Columns { get; init; } = string.Empty;

    /// <summary>CSS grid column definition for small viewports (e.g., "1fr").</summary>
    public string ColumnsSmall { get; init; } = string.Empty;

    /// <summary>Number of section slots available in this row.</summary>
    public int SlotCount { get; init; }
}
