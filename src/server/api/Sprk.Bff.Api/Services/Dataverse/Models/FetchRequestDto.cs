namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Request payload for <c>POST /api/dataverse/fetch</c> (FR-BFF-04).
/// </summary>
/// <param name="EntityName">
/// Logical name of the primary entity in the FetchXML. Used by the authorization filter
/// to validate the request body matches the FetchXML primary entity (defense-in-depth
/// against routing/body mismatch). Errors with <c>DV_FETCHXML_ENTITY_MISMATCH</c> if
/// the body value disagrees with the FetchXML root.
/// </param>
/// <param name="FetchXml">
/// The FetchXML query string to execute. Parsed by the cross-entity privilege check
/// (<c>FetchXmlEntityExtractor</c>); malformed XML returns 400 with
/// <c>DV_FETCHXML_MALFORMED</c>.
/// </param>
/// <param name="PagingCookie">
/// Optional Dataverse paging cookie from a prior page. When present, the service
/// injects it into the FetchXML root <c>paging-cookie</c> attribute before execution.
/// Cookies have a 60-minute server-side expiry; callers should refetch from page 1
/// on idle.
/// </param>
public sealed record FetchRequestDto(
    string EntityName,
    string FetchXml,
    string? PagingCookie);
