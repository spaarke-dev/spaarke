namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for GET /api/v1/external/me — the authenticated portal user's project access summary.
/// </summary>
/// <param name="ContactId">The Dataverse Contact ID for the authenticated external user.</param>
/// <param name="Email">The external user's email address from the portal token.</param>
/// <param name="Projects">List of projects the Contact has access to, with access levels.</param>
/// <param name="SystemUserId">
/// The caller's OWN Dataverse systemuserid (<see cref="Infrastructure.ExternalAccess.CallerPrincipal.SystemUserId"/>),
/// or <see langword="null"/> when the caller is not a licensed workforce systemuser (a CIAM partner or a
/// workforce contact). Never <see cref="Guid.Empty"/>. The SPA compares it with a row's
/// <c>sprk_createdbyperson</c> stamp to decide whether to offer Delete (FR-26); the server's delete check
/// (<c>ExternalDataService.IsCreatedByCaller</c>) stays the authority. It is the caller's own id only, so it
/// discloses nothing new.
/// </param>
/// <param name="Firms">
/// ebilling-r1 task 051 (additive; the fields above are unchanged). The firms the caller can submit invoices for: the
/// caller contact's CONFERRING organization memberships, id and name only, the same set the invoice submission routes
/// resolve <c>firmId</c> from. Empty when the caller belongs to no firm, and for a workforce systemuser (internal users submit
/// through the internal route). <see langword="null"/>, and then OMITTED from the JSON (<c>WhenWritingNull</c>, so ordinary /me
/// responses do not carry <c>"firms":null</c>), when it is not known: eBilling is off (the list is not read at all), or the
/// memberships or names could not be read. A reader treats an absent field as "eBilling off, not applicable, or unknown",
/// never as "no firm". Only the caller's own firms are ever listed.
/// </param>
/// <param name="WriteAccessPerRecord">
/// <see langword="true"/> for a caller whose access Dataverse decides (<c>CallerPrincipal.DataverseConfirmsWrites</c>,
/// unified-access-control-r2 task 036 fix round 1, F4). Their <see cref="Projects"/> levels then say only what they may
/// SEE (the impersonated set confers Read, so most read as ViewOnly); whether they may create under a record is decided
/// per record, as Dataverse decides it in MDA. The client then takes its create/upload controls from the record's own
/// list (<c>canCreate</c>) instead of the level. Omitted when <see langword="false"/>, so every other caller's response
/// is byte-for-byte what it was.
/// </param>
public record ExternalUserContextResponse(
    Guid ContactId,
    string Email,
    IReadOnlyList<ProjectAccessEntry> Projects,
    Guid? SystemUserId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ExternalFirmEntry>? Firms = null,
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool WriteAccessPerRecord = false);

/// <summary>One firm the caller belongs to (see <see cref="ExternalUserContextResponse.Firms"/>).</summary>
/// <param name="FirmId">The Dataverse <c>sprk_organizationid</c>; the value to send as <c>firmId</c> when submitting.</param>
/// <param name="Name">The organization's display name.</param>
public record ExternalFirmEntry(Guid FirmId, string Name);

/// <summary>
/// A single project access entry in the external user context response.
/// </summary>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID.</param>
/// <param name="AccessLevel">The access level string: "ViewOnly", "Collaborate", or "FullAccess".</param>
public record ProjectAccessEntry(
    Guid ProjectId,
    string AccessLevel);
