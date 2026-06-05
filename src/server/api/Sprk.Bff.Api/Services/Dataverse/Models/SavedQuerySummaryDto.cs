namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Lightweight summary of a <c>savedquery</c> row used to populate view-selector UIs.
/// </summary>
/// <param name="Id">The savedquery primary id.</param>
/// <param name="Name">The display name of the saved query.</param>
/// <param name="IsDefault">True when this saved query is the default view for its entity.</param>
/// <param name="QueryType">The Dataverse query type code (0 = User Owned, 64 = Main App View, etc.).</param>
/// <remarks>
/// Returned by <c>GET /api/dataverse/savedqueries/{entityLogicalName}</c> per FR-BFF-02.
/// </remarks>
public sealed record SavedQuerySummaryDto(
    Guid Id,
    string Name,
    bool IsDefault,
    int QueryType);
