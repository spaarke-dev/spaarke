namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for revoking external access from a Contact for a specific Project.
/// </summary>
/// <param name="AccessRecordId">The ID of the sprk_externalrecordaccess record to deactivate.</param>
/// <param name="ContactId">The Dataverse Contact ID whose access is being revoked.</param>
/// <param name="ProjectId">The Dataverse Project ID the access record belongs to.</param>
/// <param name="ContainerId">Optional SPE container ID. If provided, the Contact will be removed from the container.</param>
public record RevokeAccessRequest(
    Guid AccessRecordId,
    Guid ContactId,
    Guid ProjectId,
    Guid? ContainerId);
