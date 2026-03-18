namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response for POST /api/v1/external-access/close-project.
/// Reports the outcome of closing a Secure Project.
/// </summary>
/// <param name="AccessRecordsRevoked">
/// Number of sprk_externalrecordaccess records deactivated (statecode set to 1).
/// </param>
/// <param name="SpeContainerMembersRemoved">
/// Number of external SPE container permission entries removed.
/// Zero if no ContainerId was provided or if the container had no external members.
/// </param>
/// <param name="AffectedContactIds">
/// List of Contact IDs whose Redis participation cache entries were invalidated.
/// </param>
public record CloseProjectResponse(
    int AccessRecordsRevoked,
    int SpeContainerMembersRemoved,
    IReadOnlyList<Guid> AffectedContactIds);
