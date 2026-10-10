using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after granting external access to a Contact.
/// </summary>
/// <param name="AccessRecordId">The ID of the created sprk_externalrecordaccess record.</param>
/// <param name="SpeContainerMembershipGranted">Whether the Contact was successfully added to the SPE container.</param>
/// <param name="GrantedAccessLevel">
/// The level actually written — the requested level capped at the caller's own level on the record (task 139, owner
/// Q1: every grant is capped at the grantor's level). Additive; older clients ignore it.
/// </param>
/// <param name="Narrowed">
/// <c>true</c> when the caller's own level was below the requested one, so the grant was written at
/// <paramref name="GrantedAccessLevel"/> instead (task 139). Additive.
/// </param>
/// <param name="NotificationFailed">
/// <c>true</c> when the grant gave access to a contact that represents an internal user and that user could not be sent
/// the in-app notification (task 181, owner round 89). The grant itself stands. Additive.
/// </param>
public record GrantAccessResponse(
    Guid AccessRecordId,
    bool SpeContainerMembershipGranted,
    ExternalAccessLevel? GrantedAccessLevel = null,
    bool Narrowed = false,
    bool NotificationFailed = false);
