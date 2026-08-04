namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// The resolved workforce collaboration principal returned by
/// <c>GET /api/v1/collab/me</c> (ADR-028 Amendment A2 · teams-app-r1 FR-04). Lets a Teams-host
/// client confirm which identity plane the caller resolved to before rendering the collaboration
/// surface. The accessible-record set itself is composed downstream (tasks 021/022).
/// </summary>
/// <param name="Kind">The resolved plane: <c>"systemuser"</c> or <c>"contact"</c>.</param>
/// <param name="SystemUserId">The Dataverse systemuserid; non-null only for a systemuser principal.</param>
/// <param name="ContactId">The Dataverse contactid: the anchor for a contact-only principal, or the
/// derived contact (may be null) for a systemuser principal.</param>
public sealed record WorkforcePrincipalContextResponse(
    string Kind,
    Guid? SystemUserId,
    Guid? ContactId);
