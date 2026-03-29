using Sprk.Bff.Api.Api.Workspace;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Defines system-provided workspace layouts that are always available to all users.
/// System layouts are code constants — never stored in or read from Dataverse.
/// </summary>
/// <remarks>
/// R1 ships a single system layout: "Corporate Workspace" (mirrors the current
/// hardcoded 3-row-mixed template with all 5 sections).
/// System layouts use well-known GUIDs so they can be referenced by ID across requests.
/// </remarks>
public static class SystemWorkspaceLayouts
{
    /// <summary>
    /// Well-known GUID for the Corporate Workspace system layout.
    /// This GUID is stable across all environments and never changes.
    /// </summary>
    public static readonly Guid CorporateWorkspaceId =
        new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// The Corporate Workspace system layout — mirrors the current LegalWorkspace
    /// default: 3-row-mixed template with all 5 sections (Get Started, Quick Summary,
    /// Latest Updates, My To Do List, My Documents).
    /// </summary>
    public static readonly WorkspaceLayoutDto CorporateWorkspace = new()
    {
        Id = CorporateWorkspaceId,
        Name = "Corporate Workspace",
        LayoutTemplateId = "3-row-mixed",
        SectionsJson = """
            {
              "schemaVersion": 1,
              "rows": [
                {
                  "id": "row-1",
                  "columns": "1fr 1fr",
                  "columnsSmall": "1fr",
                  "sections": ["get-started", "quick-summary"]
                },
                {
                  "id": "row-2",
                  "columns": "1fr",
                  "columnsSmall": "1fr",
                  "sections": ["latest-updates"]
                },
                {
                  "id": "row-3",
                  "columns": "1fr 1fr",
                  "columnsSmall": "1fr",
                  "sections": ["todo", "documents"]
                }
              ]
            }
            """,
        IsDefault = false,
        SortOrder = 0,
        IsSystem = true
    };

    /// <summary>
    /// All system layouts. Returned alongside user layouts in list operations.
    /// </summary>
    public static readonly IReadOnlyList<WorkspaceLayoutDto> All = [CorporateWorkspace];

    /// <summary>
    /// Returns the system layout matching the specified ID, or null if not a system layout.
    /// </summary>
    public static WorkspaceLayoutDto? GetById(Guid id)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == id)
                return All[i];
        }

        return null;
    }

    /// <summary>
    /// Returns true if the specified ID belongs to a system layout.
    /// </summary>
    public static bool IsSystemLayout(Guid id)
        => GetById(id) is not null;
}
