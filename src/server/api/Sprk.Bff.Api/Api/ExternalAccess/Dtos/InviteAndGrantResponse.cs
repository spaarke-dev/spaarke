using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for the core-user "Invite to Secure Workspace" action (task 029) — one action that
/// onboards (idempotent CIAM provision) AND grants an attorney Contact access to a Project.
/// </summary>
/// <param name="ContactId">The Dataverse Contact (person) that was onboarded and granted access.</param>
/// <param name="OnboardStatus">"Provisioned" (a CIAM account was created) or "AlreadyProvisioned" (idempotent — the Contact already had an oid bound).</param>
/// <param name="AccessRecordId">The created sprk_externalrecordaccess grant id (audited via sprk_grantedby).</param>
/// <param name="PortalUrl">The external Secure Project Workspace portal (login) URL the attorney is directed to.</param>
/// <param name="GrantedAccessLevel">
/// The level actually written — the requested level capped at the caller's own level on the record (task 139).
/// Additive; older clients ignore it.
/// </param>
/// <param name="Narrowed">
/// <c>true</c> when the caller's own level was below the requested one, so the grant was written at
/// <paramref name="GrantedAccessLevel"/> instead (task 139). Additive.
/// </param>
public record InviteAndGrantResponse(
    Guid ContactId,
    string OnboardStatus,
    Guid AccessRecordId,
    string PortalUrl,
    ExternalAccessLevel? GrantedAccessLevel = null,
    bool Narrowed = false);
