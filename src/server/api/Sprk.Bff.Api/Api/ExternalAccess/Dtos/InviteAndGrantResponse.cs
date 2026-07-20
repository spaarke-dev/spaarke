namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for the core-user "Invite to Secure Workspace" action (task 029) — one action that
/// onboards (idempotent CIAM provision) AND grants an attorney Contact access to a Project.
/// </summary>
/// <param name="ContactId">The Dataverse Contact (person) that was onboarded and granted access.</param>
/// <param name="OnboardStatus">"Provisioned" (a CIAM account was created) or "AlreadyProvisioned" (idempotent — the Contact already had an oid bound).</param>
/// <param name="AccessRecordId">The created sprk_externalrecordaccess grant id (audited via sprk_grantedby).</param>
/// <param name="PortalUrl">The external Secure Project Workspace portal (login) URL the attorney is directed to.</param>
public record InviteAndGrantResponse(
    Guid ContactId,
    string OnboardStatus,
    Guid AccessRecordId,
    string PortalUrl);
