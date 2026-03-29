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

    /// <summary>Whether this is a system-provided (non-editable) layout.</summary>
    public bool IsSystem { get; init; }
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
