namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for GET /api/v1/external/me — the authenticated portal user's project access summary.
/// </summary>
/// <param name="ContactId">The Dataverse Contact ID for the authenticated external user.</param>
/// <param name="Email">The external user's email address from the portal token.</param>
/// <param name="Projects">List of projects the Contact has access to, with access levels.</param>
public record ExternalUserContextResponse(
    Guid ContactId,
    string Email,
    IReadOnlyList<ProjectAccessEntry> Projects);

/// <summary>
/// A single project access entry in the external user context response.
/// </summary>
/// <param name="ProjectId">The Dataverse Project (sprk_project) ID.</param>
/// <param name="AccessLevel">The access level string: "ViewOnly", "Collaborate", or "FullAccess".</param>
public record ProjectAccessEntry(
    Guid ProjectId,
    string AccessLevel);
