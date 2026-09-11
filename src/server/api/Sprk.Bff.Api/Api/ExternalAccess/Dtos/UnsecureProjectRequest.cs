namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/unsecure-project — the reverse of secure-project
/// provisioning (design.md §5.1 "the designation is reversible", spec FR-28).
/// </summary>
/// <param name="ProjectId">The Dataverse <c>sprk_project</c> GUID to remove the secure designation from.</param>
/// <param name="ReassignToSystemUserId">
/// Optional. The <c>systemuser</c> to hand ownership to. When omitted the owner is taken from
/// configuration (<c>SecureProject:UnsecureOwnerUserId</c>), and failing that the calling user — who
/// has already had to prove Write on the record to reach this route.
/// </param>
public record UnsecureProjectRequest(
    Guid ProjectId,
    Guid? ReassignToSystemUserId = null);
