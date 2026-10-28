namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after admin-initiated onboarding of an external user via Entra External ID (CIAM).
/// </summary>
/// <param name="ContactId">The Dataverse Contact record ID (created or resolved by email).</param>
/// <param name="InviteRedeemUrl">The external Secure Project Workspace portal (login) URL the user is directed to. (CIAM has no B2B redemption URL; the temp password is set via SSPR "Forgot password".)</param>
/// <param name="Status">Provisioning status: "Provisioned" (CIAM account created) or "AlreadyProvisioned" (idempotent — an oid was already bound).</param>
/// <param name="EmailSent">
/// Task 185 (adversarial pass 2, F2): whether the invite email was sent. <c>true</c> when it was, <c>false</c> when it was not.
/// Slice C2b: never <c>null</c> on <c>/invite</c>. A contact that already has an account ("AlreadyProvisioned") is sent
/// nothing: <c>false</c> with <see cref="ContactBindingDecision.InviteContactAlreadyHasAccount"/>. §12 I-10: the same on the
/// new-account and the existing-account branch. Additive; older clients ignore it.
/// </param>
/// <param name="EmailErrorCode">
/// Why <paramref name="EmailSent"/> is <c>false</c>: <see cref="GrantInviteStatus.EmailNotSentReasonCode"/> (attempted, not
/// sent) or <see cref="ContactBindingDecision.InviteContactAlreadyHasAccount"/> (none due); otherwise <c>null</c>. Additive.
/// </param>
/// <param name="StaffMessage">
/// Slice C2b: text for staff when no invite was sent because the person already has an account; otherwise <c>null</c>.
/// Additive.
/// </param>
/// <param name="SignInUrl">
/// Slice C2b: with <paramref name="StaffMessage"/>, the portal sign-in link for staff to pass on: the link the work-account
/// notice carries (the portal with this deployment's customer key, never a code). Otherwise <c>null</c>. Additive.
/// </param>
public record InviteExternalUserResponse(
    Guid ContactId,
    string InviteRedeemUrl,
    string Status,
    bool? EmailSent = null,
    string? EmailErrorCode = null,
    string? StaffMessage = null,
    string? SignInUrl = null);
