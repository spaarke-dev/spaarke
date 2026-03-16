namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for sending a Power Pages portal invitation to an external Contact.
/// </summary>
/// <param name="ContactId">The Dataverse Contact ID to invite.</param>
/// <param name="ProjectId">The Dataverse Project ID the Contact is being invited to access.</param>
/// <param name="ExpiryDate">Optional expiry date for the invitation. Defaults to 30 days if not specified.</param>
/// <param name="AccountId">Optional Account ID associated with the Contact (for record-keeping).</param>
public record InviteExternalUserRequest(
    Guid ContactId,
    Guid ProjectId,
    DateOnly? ExpiryDate,
    Guid? AccountId);
