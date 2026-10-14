namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after admin-initiated onboarding of an external user via Entra External ID (CIAM).
/// </summary>
/// <param name="ContactId">The Dataverse Contact record ID (created or resolved by email).</param>
/// <param name="InviteRedeemUrl">The external Secure Project Workspace portal (login) URL the user is directed to. (CIAM has no B2B redemption URL; the temp password is set via SSPR "Forgot password".)</param>
/// <param name="Status">Provisioning status: "Provisioned" (CIAM account created) or "AlreadyProvisioned" (idempotent — an oid was already bound).</param>
/// <param name="EmailSent">
/// Task 185 (adversarial pass 2, F2): whether the invite email carrying the code was sent. <c>true</c> when it was, <c>false</c>
/// when it was attempted and did not send (nobody holds the code, so the contact cannot bind until a re-invite sends a new
/// one), <c>null</c> when no invite email was due ("AlreadyProvisioned"). §12 I-10: the same on the new-account and the
/// existing-account branch. Additive; older clients ignore it.
/// </param>
/// <param name="EmailErrorCode">
/// <see cref="GrantInviteStatus.EmailNotSentReasonCode"/> when <paramref name="EmailSent"/> is <c>false</c>; otherwise
/// <c>null</c>. Additive.
/// </param>
public record InviteExternalUserResponse(
    Guid ContactId,
    string InviteRedeemUrl,
    string Status,
    bool? EmailSent = null,
    string? EmailErrorCode = null);
