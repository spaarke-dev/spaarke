namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/close-project.
/// Closes a Secure Project by revoking all external access and removing SPE container members.
/// </summary>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID to close.</param>
/// <param name="ContainerId">
/// Optional SPE container ID. If provided, all external container members will be removed
/// from the SharePoint Embedded container in addition to deactivating Dataverse access records.
/// </param>
public record CloseProjectRequest(Guid ProjectId, string? ContainerId);
