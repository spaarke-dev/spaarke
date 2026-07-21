namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after admin-initiated onboarding of an external user via Entra External ID (CIAM).
/// </summary>
/// <param name="ContactId">The Dataverse Contact record ID (created or resolved by email).</param>
/// <param name="InviteRedeemUrl">The external Secure Project Workspace portal (login) URL the user is directed to. (CIAM has no B2B redemption URL; the temp password is set via SSPR "Forgot password".)</param>
/// <param name="Status">Provisioning status: "Provisioned" (CIAM account created) or "AlreadyProvisioned" (idempotent — an oid was already bound).</param>
public record InviteExternalUserResponse(
    Guid ContactId,
    string InviteRedeemUrl,
    string Status);
