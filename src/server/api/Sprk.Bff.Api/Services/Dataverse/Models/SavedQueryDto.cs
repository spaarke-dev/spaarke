namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Projection of a single Dataverse <c>savedquery</c> row for client consumption.
/// </summary>
/// <param name="EntityName">The logical name of the entity the saved query targets (e.g., <c>sprk_matter</c>).</param>
/// <param name="FetchXml">The FetchXML payload describing the query.</param>
/// <param name="LayoutXml">The LayoutXML describing the column layout/order.</param>
/// <param name="Name">The display name of the saved query.</param>
/// <remarks>
/// Returned by <c>GET /api/dataverse/savedquery/{savedQueryId}</c> per FR-BFF-01.
/// </remarks>
public sealed record SavedQueryDto(
    string EntityName,
    string FetchXml,
    string LayoutXml,
    string Name);
