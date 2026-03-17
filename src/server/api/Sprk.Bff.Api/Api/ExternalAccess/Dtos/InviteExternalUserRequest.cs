namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for inviting an external user to a Secure Project via Azure AD B2B.
/// </summary>
/// <param name="Email">The external user's email address. Used to send the Azure AD B2B invitation and look up / create their Contact record.</param>
/// <param name="ProjectId">The Dataverse Project ID the user is being invited to access.</param>
/// <param name="AccessLevel">Access level to grant (100000000=ViewOnly, 100000001=Collaborate, 100000002=FullAccess).</param>
/// <param name="FirstName">Optional first name for Contact creation if the Contact does not yet exist.</param>
/// <param name="LastName">Optional last name for Contact creation if the Contact does not yet exist.</param>
/// <param name="ExpiryDate">Optional expiry date for the access record. No expiry if not specified.</param>
/// <param name="AccountId">Optional Account ID associated with the Contact (for firm-level scoping).</param>
public record InviteExternalUserRequest(
    string Email,
    Guid ProjectId,
    int AccessLevel,
    string? FirstName,
    string? LastName,
    DateOnly? ExpiryDate,
    Guid? AccountId);
