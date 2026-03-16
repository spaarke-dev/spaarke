namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after creating a Power Pages portal invitation.
/// </summary>
/// <param name="InvitationId">The ID of the created adx_invitation record.</param>
/// <param name="InvitationCode">The invitation code (from adx_invitationcode) the Contact uses to redeem access.</param>
/// <param name="ExpiryDate">The expiry date of the invitation, if set.</param>
public record InviteExternalUserResponse(
    Guid InvitationId,
    string InvitationCode,
    DateOnly? ExpiryDate);
