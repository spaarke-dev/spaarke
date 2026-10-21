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
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    bool WriteAccessPerRecord = false);

/// <summary>
/// A single project access entry in the external user context response.
/// </summary>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID.</param>
/// <param name="AccessLevel">The access level string: "ViewOnly", "Collaborate", or "FullAccess".</param>
public record ProjectAccessEntry(
    Guid ProjectId,
    string AccessLevel);
