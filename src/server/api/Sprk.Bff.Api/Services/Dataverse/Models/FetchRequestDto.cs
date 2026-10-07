namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Request payload for <c>POST /api/v1/external/api/dataverse/fetch</c> (the external module seam,
/// <c>ExternalModuleDataEndpoints</c>), executed by <c>FetchService.ExecuteAsync</c>. Originally the payload of
/// the internal <c>POST /api/dataverse/fetch</c> (FR-BFF-04), deleted by unified-access-control-r2 task 160.
/// </summary>
/// <param name="EntityName">
/// Logical name of the primary entity in the FetchXML.
/// </param>
/// <param name="FetchXml">
/// The FetchXML query string to execute. Parsed by <c>FetchXmlEntityExtractor</c> in the seam's
/// FetchXML guard; malformed XML returns 400.
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
