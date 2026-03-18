namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after inviting an external user via Azure AD B2B.
/// </summary>
/// <param name="ContactId">The Dataverse Contact record ID (created or resolved by email).</param>
/// <param name="InviteRedeemUrl">The Azure AD B2B redemption URL to send to the user. Empty if user already exists in the tenant.</param>
/// <param name="Status">Invitation status from Microsoft Graph (e.g., "PendingAcceptance", "Completed").</param>
public record InviteExternalUserResponse(
    Guid ContactId,
    string InviteRedeemUrl,
    string Status);
