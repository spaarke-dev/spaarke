namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after granting external access to a Contact.
/// </summary>
/// <param name="AccessRecordId">The ID of the created sprk_externalrecordaccess record.</param>
/// <param name="SpeContainerMembershipGranted">Whether the Contact was successfully added to the SPE container.</param>
public record GrantAccessResponse(
    Guid AccessRecordId,
    bool SpeContainerMembershipGranted);
