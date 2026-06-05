namespace Sprk.Bff.Api.Services.Dataverse.Models;

/// <summary>
/// Response payload for <c>POST /api/dataverse/fetch</c> (FR-BFF-04).
/// </summary>
/// <param name="Entities">
/// Result rows as a list of attribute-name → value dictionaries. Each dictionary
/// preserves the Dataverse <see cref="Microsoft.Xrm.Sdk.Entity.Attributes"/> contents
/// (primitives, <see cref="Microsoft.Xrm.Sdk.EntityReference"/>,
/// <see cref="Microsoft.Xrm.Sdk.OptionSetValue"/>, <see cref="Microsoft.Xrm.Sdk.Money"/>,
/// etc.). The default ASP.NET Core JSON serializer projects these onto their public
/// properties; the client consumes them via <c>BffDataverseClient</c>.
/// </param>
/// <param name="MoreRecords">
/// True if Dataverse has additional pages available; the caller must supply
/// <paramref name="PagingCookie"/> on the next request to continue paging.
/// </param>
/// <param name="PagingCookie">
/// Opaque cookie from Dataverse used to retrieve the next page. Null when
/// <paramref name="MoreRecords"/> is false. Cookies expire ~60 minutes after issue;
/// callers should restart from page 1 after idle.
/// </param>
public sealed record FetchResponseDto(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Entities,
    bool MoreRecords,
    string? PagingCookie);
