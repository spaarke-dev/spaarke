using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Manages SPE (SharePoint Embedded) container membership for external users.
/// When a Contact is granted access to a Secure Project, they are added to
/// the project's SPE container so they can access files.
///
/// This service uses app-only Graph authentication (ForApp) since container
/// permission management requires elevated permissions beyond what OBO provides.
/// </summary>
public class SpeContainerMembershipService
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<SpeContainerMembershipService> _logger;

    /// <summary>
    /// Maps ExternalAccessLevel to SPE permission roles.
    /// ViewOnly → reader, Collaborate → writer, FullAccess → writer.
    /// </summary>
    private static readonly Dictionary<ExternalAccessLevel, string[]> AccessLevelRoleMap = new()
    {
        [ExternalAccessLevel.ViewOnly] = ["reader"],
        [ExternalAccessLevel.Collaborate] = ["writer"],
        [ExternalAccessLevel.FullAccess] = ["writer"],
    };

    public SpeContainerMembershipService(
        IGraphClientFactory graphClientFactory,
        ILogger<SpeContainerMembershipService> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Grants a Contact membership to an SPE container.
    /// Uses the Contact's UPN (email) from Entra External ID to identify the user in Graph API.
    /// </summary>
    /// <param name="containerId">The SPE container ID (GUID format).</param>
    /// <param name="contactEmail">The Contact's email / UPN in Entra External ID.</param>
    /// <param name="accessLevel">The access level determining the SPE role to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result indicating success with the new permissionId, or failure with an error message.</returns>
    /// <remarks>
    /// ⚠️ <b>Currently has NO callers, by design — Spaarke is broker-only.</b> External access confers
    /// file access through the broker, not through per-contact container ACLs:
    /// <c>GrantExternalAccessEndpoint</c> returns <c>SpeContainerMembershipGranted: false</c> and never
    /// calls this, and neither invite endpoint touches SPE. It is retained as the documented counterpart
    /// of <see cref="RevokeMembershipAsync"/> and as the definition of the identity key the revoke matcher
    /// must match (<c>userPrincipalName</c> = contact email). Do NOT wire it up without revisiting the
    /// broker-only decision — and note that <see cref="RevokeMembershipAsync"/> still exists and matters
    /// regardless, because container ACLs created by legacy versions or by admins outside this codebase
    /// must still be cleanable.
    /// </remarks>
    public async Task<SpeContainerMembershipResult> GrantMembershipAsync(
        string containerId,
        string contactEmail,
        ExternalAccessLevel accessLevel,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Granting SPE container membership: containerId={ContainerId}, email={Email}, accessLevel={AccessLevel}",
            containerId, contactEmail, accessLevel);

        try
        {
            var graphClient = _graphClientFactory.ForApp();
            var roles = AccessLevelRoleMap[accessLevel];

            // Graph SDK 5.x: use SharePointIdentity with userPrincipalName in AdditionalData
            // to identify the external user by email/UPN.
            // GrantedToV2 is the preferred field for SPE container permissions.
            var permission = new Permission
            {
                Roles = [.. roles],
                GrantedToV2 = new SharePointIdentitySet
                {
                    User = new SharePointIdentity
                    {
                        AdditionalData = new Dictionary<string, object>
                        {
                            ["userPrincipalName"] = contactEmail
                        }
                    }
                }
            };

            var createdPermission = await graphClient.Storage.FileStorage
                .Containers[containerId].Permissions
                .PostAsync(permission, cancellationToken: ct);

            if (createdPermission?.Id == null)
            {
                _logger.LogError(
                    "Graph API returned null or missing ID when granting membership: containerId={ContainerId}, email={Email}",
                    containerId, contactEmail);
                return new SpeContainerMembershipResult(false, null, "Graph API returned null permission after grant.");
            }

            _logger.LogInformation(
                "Successfully granted SPE membership: containerId={ContainerId}, email={Email}, permissionId={PermissionId}",
                containerId, contactEmail, createdPermission.Id);

            return new SpeContainerMembershipResult(true, createdPermission.Id, null);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Graph API error granting SPE membership: containerId={ContainerId}, email={Email}, status={StatusCode}",
                containerId, contactEmail, ex.ResponseStatusCode);
            return new SpeContainerMembershipResult(false, null, $"Graph API error ({ex.ResponseStatusCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error granting SPE membership: containerId={ContainerId}, email={Email}",
                containerId, contactEmail);
            return new SpeContainerMembershipResult(false, null, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Revokes a Contact's membership from an SPE container.
    /// Finds the permission entry by the Contact's email and deletes it.
    /// </summary>
    /// <param name="containerId">The SPE container ID (GUID format).</param>
    /// <param name="contactEmail">The Contact's email / UPN to find and remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success with the deleted permission id; or failure whose <c>Error</c> distinguishes
    /// "no permission found for this user" from a Graph error.
    /// </returns>
    /// <remarks>
    /// <para>This is the canonical SPE revoke. It matches on the SAME key membership is WRITTEN with —
    /// <c>userPrincipalName</c> = the contact's email (see <see cref="GrantMembershipAsync"/>) — via
    /// <c>FindPermissionByEmail</c>.</para>
    ///
    /// <para><b>Task 017 / finding A-13</b>: <c>RevokeExternalAccessEndpoint</c> had its own private copy of
    /// this logic that searched for the contact's <b>GUID</b> inside the UPN. An email never contains a
    /// GUID, so it never matched — and it then returned <c>true</c> ("may have already been removed"),
    /// reporting revoke success while leaving the ACL entry in place. The fork is deleted; the endpoint
    /// calls this method. Nothing here needed fixing: the correct implementation already existed and had
    /// zero callers.</para>
    ///
    /// <para><c>virtual</c> is the substitution seam (ADR-038 §4), matching the
    /// <c>DataverseWebApiClient</c> precedent — it lets the endpoint's revoke path be tested without
    /// mocking Graph transport (ban B1).</para>
    /// </remarks>
    public virtual async Task<SpeContainerMembershipResult> RevokeMembershipAsync(
        string containerId,
        string contactEmail,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Revoking SPE container membership: containerId={ContainerId}, email={Email}",
            containerId, contactEmail);

        try
        {
            var graphClient = _graphClientFactory.ForApp();

            var permissions = await graphClient.Storage.FileStorage
                .Containers[containerId].Permissions
                .GetAsync(cancellationToken: ct);

            var targetPermission = FindPermissionByEmail(permissions?.Value, contactEmail);

            if (targetPermission == null)
            {
                _logger.LogWarning(
                    "No SPE permission found for revocation: containerId={ContainerId}, email={Email}",
                    containerId, contactEmail);
                return new SpeContainerMembershipResult(false, null, $"No permission found for user '{contactEmail}' in container.");
            }

            await graphClient.Storage.FileStorage
                .Containers[containerId].Permissions[targetPermission.Id]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation(
                "Successfully revoked SPE membership: containerId={ContainerId}, email={Email}, permissionId={PermissionId}",
                containerId, contactEmail, targetPermission.Id);

            return new SpeContainerMembershipResult(true, targetPermission.Id, null);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Graph API error revoking SPE membership: containerId={ContainerId}, email={Email}, status={StatusCode}",
                containerId, contactEmail, ex.ResponseStatusCode);
            return new SpeContainerMembershipResult(false, null, $"Graph API error ({ex.ResponseStatusCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error revoking SPE membership: containerId={ContainerId}, email={Email}",
                containerId, contactEmail);
            return new SpeContainerMembershipResult(false, null, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Lists all external members of an SPE container.
    /// Returns only external members (those with a user identity in their permission grant).
    /// </summary>
    /// <param name="containerId">The SPE container ID (GUID format).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of external container members. Empty means the container genuinely has none.</returns>
    /// <exception cref="ServiceException">Graph could not be reached or refused the request.</exception>
    /// <remarks>
    /// <para><b>Failures propagate (task 017, filed by task 016).</b> This method used to catch both
    /// <see cref="ServiceException"/> and <see cref="Exception"/> and return <c>[]</c> in each — so an
    /// unreachable Graph was indistinguishable from an empty container. Its only caller,
    /// <see cref="RemoveAllExternalMembersAsync"/>, therefore answered "0 removed" either way, and
    /// close-project reported <c>200 OK</c> while every external user might still hold file permission on
    /// the container. That is FR-15's own acceptance ("no participant retains access post-closure")
    /// failing silently on the SPE half.</para>
    ///
    /// <para>An empty list now means one thing only: the container has no external members. Callers that
    /// need to keep going on failure should catch deliberately — and report the failure, not absorb it.</para>
    /// </remarks>
    public virtual async Task<IReadOnlyList<SpeContainerMember>> ListExternalMembersAsync(
        string containerId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Listing external SPE members: containerId={ContainerId}", containerId);

        var graphClient = _graphClientFactory.ForApp();

        var permissions = await graphClient.Storage.FileStorage
            .Containers[containerId].Permissions
            .GetAsync(cancellationToken: ct);

        if (permissions?.Value == null)
        {
            _logger.LogInformation("No permissions found for container {ContainerId}", containerId);
            return [];
        }

        // External members are those with a GrantedToV2.User (individual user grants).
        // System / app permissions and container-type-level grants do not have a User identity.
        var externalMembers = permissions.Value
            .Where(p => p.GrantedToV2?.User != null)
            .Select(ToContainerMember)
            .Where(m => m != null)
            .Cast<SpeContainerMember>()
            .ToList()
            .AsReadOnly();

        _logger.LogInformation(
            "Found {Count} external members in container {ContainerId}",
            externalMembers.Count, containerId);

        return externalMembers;
    }

    /// <summary>
    /// Removes all external members from an SPE container.
    /// Used when a project is closed (task 016 - Project Closure).
    /// </summary>
    /// <param name="containerId">The SPE container ID (GUID format).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many members were removed AND how many could not be.</returns>
    /// <exception cref="ServiceException">The member list could not be read at all — nothing was removed.</exception>
    /// <remarks>
    /// <para><b>Returns a count of FAILURES too (task 017, filed by task 016).</b> This used to return a
    /// bare <c>int</c> of successes while swallowing every per-member error, so "3 of 12 removed" and
    /// "12 of 12 removed" were both just a number the caller could not interpret — and a caller that
    /// treats any completed call as success reports a closed project while nine people keep file access.</para>
    ///
    /// <para>Per-member failures still do not abort the loop: every other member should lose access, and
    /// stopping at the first error would leave strictly more access in place. They are counted instead.
    /// A failure to LIST propagates, because then nothing was removed and there is nothing to report.</para>
    /// </remarks>
    public virtual async Task<SpeBulkRemovalResult> RemoveAllExternalMembersAsync(
        string containerId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Removing all external members from container {ContainerId}", containerId);

        var externalMembers = await ListExternalMembersAsync(containerId, ct);

        if (externalMembers.Count == 0)
        {
            _logger.LogInformation("No external members to remove from container {ContainerId}", containerId);
            return new SpeBulkRemovalResult(0, 0);
        }

        _logger.LogInformation(
            "Removing {Count} external members from container {ContainerId}",
            externalMembers.Count, containerId);

        var graphClient = _graphClientFactory.ForApp();
        int removedCount = 0;
        int failedCount = 0;

        foreach (var member in externalMembers)
        {
            try
            {
                await graphClient.Storage.FileStorage
                    .Containers[containerId].Permissions[member.PermissionId]
                    .DeleteAsync(cancellationToken: ct);

                removedCount++;
                _logger.LogDebug(
                    "Removed external member: containerId={ContainerId}, permissionId={PermissionId}",
                    containerId, member.PermissionId);
            }
            catch (ServiceException ex)
            {
                failedCount++;
                _logger.LogError(ex,
                    "Failed to remove external member: containerId={ContainerId}, permissionId={PermissionId}, " +
                    "status={StatusCode}. They RETAIN file access. Continuing with the rest.",
                    containerId, member.PermissionId, ex.ResponseStatusCode);
            }
            catch (Exception ex)
            {
                failedCount++;
                _logger.LogError(ex,
                    "Unexpected error removing external member: containerId={ContainerId}, " +
                    "permissionId={PermissionId}. They RETAIN file access. Continuing with the rest.",
                    containerId, member.PermissionId);
            }
        }

        if (failedCount > 0)
        {
            _logger.LogError(
                "INCOMPLETE removal of external members from container {ContainerId}: {Removed}/{Total} " +
                "removed, {Failed} RETAIN file access.",
                containerId, removedCount, externalMembers.Count, failedCount);
        }
        else
        {
            _logger.LogInformation(
                "Completed removal of external members from container {ContainerId}: {Removed}/{Total} removed",
                containerId, removedCount, externalMembers.Count);
        }

        return new SpeBulkRemovalResult(removedCount, failedCount);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Finds a permission entry by matching the grantedTo user's UPN from AdditionalData.
    /// </summary>
    private static Permission? FindPermissionByEmail(IList<Permission>? permissions, string email)
    {
        if (permissions == null) return null;

        return permissions.FirstOrDefault(p =>
        {
            var upn = GetUpnFromPermission(p);
            return string.Equals(upn, email, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Extracts the UPN from a Graph Permission object via AdditionalData on the user identity.
    /// Graph SDK 5.x Identity type does not expose Email or LoginName directly;
    /// these values are returned in AdditionalData when present.
    /// </summary>
    private static string? GetUpnFromPermission(Permission permission)
    {
        var user = permission.GrantedToV2?.User;
        if (user == null) return null;

        // Graph may return userPrincipalName or email in AdditionalData
        if (user.AdditionalData == null) return null;

        if (user.AdditionalData.TryGetValue("userPrincipalName", out var upn) && upn is string upnStr)
            return upnStr;

        if (user.AdditionalData.TryGetValue("email", out var emailVal) && emailVal is string emailStr)
            return emailStr;

        return null;
    }

    /// <summary>
    /// Converts a Graph Permission to a SpeContainerMember.
    /// Uses the user's DisplayName as a fallback identifier when UPN is unavailable.
    /// Returns null if the permission lacks a valid ID.
    /// </summary>
    private static SpeContainerMember? ToContainerMember(Permission permission)
    {
        if (string.IsNullOrEmpty(permission.Id)) return null;

        var user = permission.GrantedToV2?.User;
        if (user == null) return null;

        // Prefer UPN from AdditionalData; fall back to DisplayName as identifier
        var upn = GetUpnFromPermission(permission);
        var identifier = upn ?? user.DisplayName ?? user.Id ?? string.Empty;

        var roles = permission.Roles?.ToList() ?? [];

        return new SpeContainerMember(permission.Id, identifier, roles.AsReadOnly());
    }
}

/// <summary>
/// Result of an SPE container membership operation (grant or revoke).
/// </summary>
public sealed record SpeContainerMembershipResult(
    bool Success,
    string? PermissionId,
    string? Error);

/// <summary>
/// Outcome of removing every external member from a container.
/// </summary>
/// <param name="Removed">Members whose permission was deleted.</param>
/// <param name="Failed">
/// Members whose permission could NOT be deleted. Non-zero means those people still have file access,
/// so a caller must not report the container cleared.
/// </param>
public sealed record SpeBulkRemovalResult(int Removed, int Failed)
{
    /// <summary>True only when every external member was removed.</summary>
    public bool IsComplete => Failed == 0;
}

/// <summary>
/// Represents an external member of an SPE container.
/// Email contains the user's UPN / email when available, or their DisplayName as a fallback.
/// </summary>
public sealed record SpeContainerMember(
    string PermissionId,
    string Email,
    IReadOnlyList<string> Roles);
