namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response from POST /api/v1/external-access/unsecure-project.
/// </summary>
/// <param name="ProjectId">The project whose secure designation was removed.</param>
/// <param name="NewOwnerSystemUserId">
/// The <c>systemuser</c> that now owns the record. Ownership moving off the Secure Project business
/// unit's owner team is what actually ends the isolation — the <c>sprk_issecure</c> flag is a label,
/// the ownership is the mechanism.
/// </param>
/// <param name="SharesRevoked">
/// How many POA shares were removed from the record. A secure project's access came entirely from
/// explicit shares; once normal ownership and business-unit access apply, leaving them behind would
/// keep a second, invisible access path alive.
/// </param>
/// <param name="AlreadyUnsecure">
/// <c>true</c> when the project was not secure to begin with. The call is idempotent: a repeat is a
/// 200 that changed nothing, not a 409.
/// </param>
public record UnsecureProjectResponse(
    Guid ProjectId,
    Guid NewOwnerSystemUserId,
    int SharesRevoked,
    bool AlreadyUnsecure);
