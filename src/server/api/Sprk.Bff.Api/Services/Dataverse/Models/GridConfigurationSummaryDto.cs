namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Lightweight summary of a <c>sprk_gridconfiguration</c> row used to populate
/// the WorkspaceLayoutWizard's per-instance configId picker (DEF-002 of
/// <c>spaarke-dataset-grid-framework-r2</c>).
/// </summary>
/// <param name="Id">The primary id (<c>sprk_gridconfigurationid</c>).</param>
/// <param name="Name">The display name (<c>sprk_name</c>).</param>
/// <param name="EntityLogicalName">Logical name of the target entity (<c>sprk_entitylogicalname</c>).</param>
/// <param name="IsDefault">True when this row is marked default (<c>sprk_isdefault</c>).</param>
/// <param name="SortOrder">Sort order for picker display (<c>sprk_sortorder</c>).</param>
/// <remarks>
/// Returned by <c>GET /api/dataverse/gridconfigurations/{entityLogicalName}</c>
/// (DEF-002). Deliberately narrow — omits fetchxml / layoutxml / configjson so the
/// payload stays small enough for a picker dropdown.
/// </remarks>
public sealed record GridConfigurationSummaryDto(
    Guid Id,
    string Name,
    string EntityLogicalName,
    bool IsDefault,
    int SortOrder);
