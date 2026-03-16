using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for granting external access to a Contact for a specific Project.
/// </summary>
/// <param name="ContactId">The Dataverse Contact ID to grant access to.</param>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID to grant access for.</param>
/// <param name="AccessLevel">The access level to grant (ViewOnly, Collaborate, or FullAccess).</param>
/// <param name="ExpiryDate">Optional expiry date for the access grant.</param>
/// <param name="AccountId">Optional Account ID associated with the Contact (for record-keeping).</param>
public record GrantAccessRequest(
    Guid ContactId,
    Guid ProjectId,
    ExternalAccessLevel AccessLevel,
    DateOnly? ExpiryDate,
    Guid? AccountId);
