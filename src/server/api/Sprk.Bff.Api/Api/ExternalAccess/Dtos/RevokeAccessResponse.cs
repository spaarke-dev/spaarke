namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after revoking external access from a Contact.
/// </summary>
/// <param name="SpeContainerMembershipRevoked">Whether the Contact was successfully removed from the SPE container.</param>
/// <param name="WebRoleRemoved">Whether the "Secure Project Participant" web role was removed from the Contact (only true when Contact has no remaining active participations).</param>
public record RevokeAccessResponse(
    bool SpeContainerMembershipRevoked,
    bool WebRoleRemoved);
