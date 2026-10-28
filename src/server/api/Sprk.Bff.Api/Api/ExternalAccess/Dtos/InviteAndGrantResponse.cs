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
/// <param name="NotificationFailed">
/// <c>true</c> when the contact was already onboarded, the grant gave it access, and the grant email could not be sent
/// (task 036, FR-07). The grant stands. Additive; <c>false</c> for a contact onboarded by this request (its CIAM
/// onboarding email is the notice) and for a grant that gave no new access.
/// </param>
/// <param name="EmailSent">
/// Task 185 (adversarial pass 2, F2): whether the invite email carrying the code was sent — <c>true</c> sent, <c>false</c>
/// attempted and not sent (the grant stands, but nobody holds the code: a re-invite sends a new one). Slice C2c: never
/// <c>null</c> for a contact that already has an account ("AlreadyProvisioned"): no invite email was due, so <c>false</c>
/// with <see cref="ContactBindingDecision.InviteContactAlreadyHasAccount"/>; its grant email is
/// <paramref name="NotificationFailed"/>, and the grant stands. §12 I-10: the same on the new-account and the
/// existing-account branch. Additive.
/// </param>
/// <param name="EmailErrorCode">
/// Why <paramref name="EmailSent"/> is <c>false</c>: <see cref="GrantInviteStatus.EmailNotSentReasonCode"/> (attempted, not
/// sent) or <see cref="ContactBindingDecision.InviteContactAlreadyHasAccount"/> (none due); otherwise <c>null</c>. Additive.
/// </param>
/// <param name="SharedWithUser">
/// Task 193 (#2338): set when the email named an existing contact that represents exactly ONE internal user, so the record
/// was SHARED with that user — never a CIAM invite, an email or a grant row. Then <paramref name="OnboardStatus"/> is
/// <c>"SharedWithInternalUser"</c>, <paramref name="AccessRecordId"/> is <see cref="Guid.Empty"/>, <paramref name="PortalUrl"/>
/// is empty (the colleague uses the model-driven app), <paramref name="EmailSent"/> is <c>null</c>, and the level fields
/// describe the share. <c>null</c> otherwise. Additive.
/// </param>
public record InviteAndGrantResponse(
    Guid ContactId,
    string OnboardStatus,
    Guid AccessRecordId,
    string PortalUrl,
    ExternalAccessLevel? GrantedAccessLevel = null,
    bool Narrowed = false,
    bool NotificationFailed = false,
    bool? EmailSent = null,
    string? EmailErrorCode = null,
    SharedWithUser? SharedWithUser = null);
