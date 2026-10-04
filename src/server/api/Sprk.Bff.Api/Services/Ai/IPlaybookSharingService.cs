using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for managing playbook sharing and access control.
/// Integrates with Dataverse sharing model for team-based access.
/// </summary>
public interface IPlaybookSharingService
{
    /// <summary>
    /// Share a playbook with teams or organization.
    /// </summary>
    /// <param name="playbookId">Playbook to share.</param>
    /// <param name="request">Sharing configuration.</param>
    /// <param name="userId">User performing the share operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the sharing operation.</returns>
    Task<ShareOperationResult> SharePlaybookAsync(
        Guid playbookId,
        SharePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke sharing from teams.
    /// </summary>
    /// <param name="playbookId">Playbook to revoke sharing from.</param>
    /// <param name="request">Revoke configuration.</param>
    /// <param name="userId">User performing the revoke operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the revoke operation.</returns>
    Task<ShareOperationResult> RevokeShareAsync(
        Guid playbookId,
        RevokeShareRequest request,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get sharing information for a playbook.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sharing information or null if not found.</returns>
    Task<PlaybookSharingInfo?> GetSharingInfoAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user has access to a playbook through sharing.
    /// Checks team membership and organization-wide access.
    /// </summary>
    /// <param name="playbookId">Playbook ID.</param>
    /// <param name="userId">
    /// The user's Dataverse <c>systemuserid</c> — compared with <c>_ownerid_value</c> and used as the
    /// <c>teammemberships</c> key — NEVER the Entra <c>oid</c> (owner round 12 item 6). Since
    /// unified-access-control-r2 task 164 no route calls this: PlaybookAuthorizationFilter asks Dataverse's own
    /// answer (the caller's Read on the row), which already covers ownership and team GrantAccess shares.
    /// </param>
    /// <param name="requiredRights">Required access rights.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if user has the required access.</returns>
    Task<bool> UserHasSharedAccessAsync(
        Guid playbookId,
        Guid userId,
        PlaybookAccessRights requiredRights = PlaybookAccessRights.Read,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user's teams from Dataverse.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of team IDs the user belongs to.</returns>
    Task<Guid[]> GetUserTeamsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
