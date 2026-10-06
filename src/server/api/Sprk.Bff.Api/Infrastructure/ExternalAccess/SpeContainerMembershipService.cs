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

    /// <summary>
    /// How many permission pages one read may follow before it stops and declares itself INCOMPLETE.
    /// </summary>
    /// <remarks>
    /// <para>A detector, not a cap on work — the same idea as
    /// <c>ProjectClosureEndpoint.MaxMembersPerSweep</c>. Its job is to convert an unbounded loop into a
    /// <b>sayable</b> condition, so hitting it sets <see cref="PermissionReadResult.EnumerationComplete"/>
    /// to <c>false</c> and every caller treats that as failure. The bound must NEVER be reported as a
    /// complete read; that is the entire point of having it.</para>
    /// </remarks>
    internal const int MaxPermissionPages = 50;

    /// <summary>
    /// The error text <see cref="RevokeMembershipAsync"/> returns when the container's permissions were
    /// enumerated in FULL and the contact genuinely holds none.
    /// </summary>
    /// <remarks>
    /// <c>RevokeExternalAccessEndpoint</c> matches on this prefix to tell a benign absence from a
    /// failure, so it is a contract between the two — hence a shared constant rather than a literal
    /// repeated at both ends. An incomplete enumeration MUST NOT produce this text (see
    /// <see cref="ClassifyRevokeResult"/>).
    /// </remarks>
    internal const string NoPermissionFoundError = "No permission found";

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

            var read = await ReadPermissionsAsync(graphClient, containerId, ct);

            var targetPermission = FindPermissionByEmail(read.Permissions, contactEmail);

            if (targetPermission == null)
            {
                // Whether this is a benign absence or a failure depends on whether we finished LOOKING.
                if (read.EnumerationComplete)
                {
                    _logger.LogWarning(
                        "No SPE permission found for revocation: containerId={ContainerId}, email={Email}",
                        containerId, contactEmail);
                }
                else
                {
                    _logger.LogError(
                        "SPE permissions for container {ContainerId} could not be fully enumerated; the " +
                        "absence of a permission for {Email} is UNPROVEN. They may RETAIN file access.",
                        containerId, contactEmail);
                }

                return ClassifyRevokeResult(read.EnumerationComplete, null, contactEmail);
            }

            await graphClient.Storage.FileStorage
                .Containers[containerId].Permissions[targetPermission.Id]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation(
                "Successfully revoked SPE membership: containerId={ContainerId}, email={Email}, permissionId={PermissionId}",
                containerId, contactEmail, targetPermission.Id);

            return ClassifyRevokeResult(read.EnumerationComplete, targetPermission.Id, contactEmail);
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

    // ListExternalMembersAsync, its worker ReadExternalMembersAsync, ToContainerMember and the SpeContainerMember record
    // were DELETED 2026-10-04 by unified-access-control-r2 task 166 f1 (verifier item 14): their only caller,
    // RemoveAllExternalMembersAsync, was deleted by task 166 (below), and no other code listed members. The honesty rules
    // they carried live on in the one paged reader every remaining path uses (ReadPermissionsAsync — an incomplete read
    // is never an absence), pinned by SpeContainerPagingTests through RemoveMembershipsAsync and RevokeMembershipAsync.
    // "External" there meant "has a user identity", which on SPE includes internal users — do not rebuild a removal on
    // such a list.

    // RemoveAllExternalMembersAsync DELETED 2026-10-03 — unified-access-control-r2 task 166 (route-authorization
    // sweep amendment (e)). Its only caller was ProjectClosureEndpoint, and it deleted EVERY container permission
    // carrying a user identity — which on SPE is every individual grant, INTERNAL users included (demo provisioning
    // and the SPE admin console both add internal users). Closing a project revokes EXTERNAL access; it must not
    // lock the project's own people out of its files. Closure now removes exactly the revoked grantees through
    // RemoveMembershipsAsync below (the single-grant revoke's email-keyed mechanism). Its result type
    // (SpeBulkRemovalResult) went with it. Do not re-add an "everyone with a user identity" sweep.

    /// <summary>
    /// Removes MANY contacts' permissions from one container using a SINGLE paged read of the
    /// permission collection.
    /// </summary>
    /// <param name="containerId">The SPE container ID.</param>
    /// <param name="contactEmails">The emails to remove. Duplicates and casing are tolerated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// One <see cref="SpeContainerMembershipResult"/> per DISTINCT email, keyed case-insensitively —
    /// the same shape <see cref="RevokeMembershipAsync"/> returns, so callers classify it identically.
    /// </returns>
    /// <remarks>
    /// <para><b>ISS-004 (#968), closed by task 024.</b> The organization-grant revoke called
    /// <see cref="RevokeMembershipAsync"/> once per member, and each of those calls read the container's
    /// ENTIRE permission collection: N members = N full reads. Task 024's paging made that strictly
    /// worse — N reads became N × pages — so consolidating went from a nicety to the thing that keeps
    /// the sweep affordable at the 200-member bound.</para>
    ///
    /// <para><b>Why not a whole-container sweep</b> (task 020's warning, preserved; the sweep itself,
    /// <c>RemoveAllExternalMembersAsync</c>, was deleted by task 166): it removed EVERY permission carrying a user
    /// identity, not just the target grantees' — other organizations' people AND internal users. A revoke or a
    /// closure must evict exactly the people whose grants it revoked. Project closure uses this method too.</para>
    ///
    /// <para><b>Failure is per-caller-visible, not aggregated.</b> A failure to READ makes every email
    /// unanswerable, so each one gets a failed result rather than the method throwing — that preserves
    /// the caller's "every member gets an outcome, and the failures are counted" contract from tasks
    /// 016/017. An incomplete enumeration flows through <see cref="ClassifyRevokeResult"/>, so a member
    /// who is merely UNSEEN is never reported as genuinely absent.</para>
    /// </remarks>
    public virtual async Task<IReadOnlyDictionary<string, SpeContainerMembershipResult>>
        RemoveMembershipsAsync(
            string containerId,
            IReadOnlyCollection<string> contactEmails,
            CancellationToken ct = default)
    {
        var distinct = contactEmails
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var results = new Dictionary<string, SpeContainerMembershipResult>(StringComparer.OrdinalIgnoreCase);

        if (distinct.Count == 0)
        {
            return results;
        }

        _logger.LogInformation(
            "Removing {Count} contact permission(s) from container {ContainerId} via one paged read",
            distinct.Count, containerId);

        GraphServiceClient graphClient;
        PermissionReadResult read;
        try
        {
            graphClient = _graphClientFactory.ForApp();
            read = await ReadPermissionsAsync(graphClient, containerId, ct);
        }
        catch (Exception ex)
        {
            // Nobody can be confirmed absent, so nobody is. One failure, reported N times, because the
            // caller reports per member.
            _logger.LogError(ex,
                "Could not read permissions for container {ContainerId}; NONE of the {Count} contact(s) " +
                "can be confirmed removed. They may RETAIN file access.",
                containerId, distinct.Count);

            foreach (var email in distinct)
            {
                results[email] = new SpeContainerMembershipResult(
                    false, null, $"Container permissions could not be read: {ex.Message}");
            }

            return results;
        }

        foreach (var email in distinct)
        {
            var target = FindPermissionByEmail(read.Permissions, email);

            if (target == null)
            {
                results[email] = ClassifyRevokeResult(read.EnumerationComplete, null, email);
                continue;
            }

            try
            {
                await graphClient.Storage.FileStorage
                    .Containers[containerId].Permissions[target.Id]
                    .DeleteAsync(cancellationToken: ct);

                results[email] = ClassifyRevokeResult(read.EnumerationComplete, target.Id, email);
            }
            catch (Exception ex)
            {
                // One member's delete failing must not abandon the rest — stopping early leaves
                // strictly MORE access in place (tasks 016/017).
                _logger.LogError(ex,
                    "Failed to delete permission {PermissionId} for {Email} on container {ContainerId}. " +
                    "They may RETAIN file access. Continuing with the rest.",
                    target.Id, email, containerId);

                results[email] = new SpeContainerMembershipResult(
                    false, null, $"Graph error deleting permission: {ex.Message}");
            }
        }

        return results;
    }

    // =========================================================================
    // MARKED grants — unified-access-control-r2 task 171 (owner rounds 69 + 70)
    //
    // The ONLY code that grants a user an SPE container role. Two kinds, one mechanism:
    //   * STANDING writers on an environment / business-unit container (round 70 option (c)) — every enabled,
    //     internal person user of the business unit(s) that container serves, kept by SpeContainerMembershipSyncJob.
    //   * JUST-IN-TIME writers on a per-record SECURE container (round 69 (2), round 70) — a user with Write on the
    //     secure record, granted on an Office edit-open by OfficeEditAccessService and removed by the same job.
    //
    // How a grant is told apart from a hand-granted or owner role (step 0, no Dataverse schema): the grant is POSTed
    // as a plain POST (the container-permission API takes no conflict parameter; Graph answers 409 when the user already
    // holds a role — pinned live by the task-171 gate D2), so a user who ALREADY holds a role answers 409 and is never
    // recorded — a role this code did not create is never removed. On 201 the permission id is recorded in ONE
    // container custom property per grant (<prefix><systemuserid:N> = <permission id>). If that record cannot be
    // written the permission is deleted again, so no unrecorded grant is ever left behind. Removal deletes only a
    // permission whose id the marker names, and only while it is still a plain writer role.
    // =========================================================================

    /// <summary>Marker prefix of a STANDING business-unit writer grant (round 70).</summary>
    public const string StandingWriterMarkerPrefix = "SprkStd";

    /// <summary>Marker prefix of a just-in-time Office-edit writer grant on a secure container (round 69 / 70).</summary>
    public const string JitWriterMarkerPrefix = "SprkJit";

    /// <summary>The narrowest container role that lets Office edit a file (step 0 (b)).</summary>
    internal const string WriterRole = "writer";

    /// <summary>
    /// <c>Prefer</c> value that makes a container-permission DELETE leave the identity's ITEM-level permissions alone
    /// (by default the delete also removes access to every item in the container). A marked grant is a container role,
    /// so that is all its removal takes away.
    /// </summary>
    internal const string OnlyContainerScopedPrefer = "onlyRemoveContainerScopedPermission";

    /// <summary>
    /// Is <paramref name="propertyName"/> a grant marker (either prefix, case-insensitive)? The SPE admin custom-property
    /// editor RESERVES these names (task 171, adversarial finding 7): a marker edited by hand would make a hand-granted
    /// role removable — or make a marked grant unremovable — so no admin route may create, change, delete or show one.
    /// </summary>
    public static bool IsGrantMarkerName(string? propertyName)
    {
        var name = propertyName?.Trim();
        return !string.IsNullOrEmpty(name)
               && (name.StartsWith(StandingWriterMarkerPrefix, StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith(JitWriterMarkerPrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The marker key for <paramref name="systemUserId"/> under <paramref name="prefix"/>.</summary>
    public static string MarkerKey(string prefix, Guid systemUserId) => prefix + systemUserId.ToString("N");

    /// <summary>The system user a marker key names, when <paramref name="key"/> is a marker key of <paramref name="prefix"/>.</summary>
    public static bool TryParseMarkerKey(string? key, string prefix, out Guid systemUserId)
    {
        systemUserId = Guid.Empty;
        return key is not null
               && key.StartsWith(prefix, StringComparison.Ordinal)
               && key.Length == prefix.Length + 32
               && Guid.TryParseExact(key[prefix.Length..], "N", out systemUserId)
               && systemUserId != Guid.Empty;
    }

    /// <summary>One user's role on a container, as Graph lists it.</summary>
    public sealed record ContainerUserRole(string PermissionId, IReadOnlyList<string> Roles, string? UserPrincipalName, string? UserObjectId)
    {
        /// <summary>Is this the user named by <paramref name="upn"/> or <paramref name="objectId"/>?</summary>
        public bool IsFor(string? upn, Guid? objectId)
            => (!string.IsNullOrWhiteSpace(upn) && string.Equals(UserPrincipalName, upn, StringComparison.OrdinalIgnoreCase))
               || (objectId is { } oid && oid != Guid.Empty && Guid.TryParse(UserObjectId, out var mine) && mine == oid);

        /// <summary>Exactly one role, writer — the only shape a marked grant takes and the only one removal deletes.</summary>
        public bool IsPlainWriter => Roles.Count == 1 && string.Equals(Roles[0], WriterRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>Does the role let Office edit (writer, manager or owner — a reader cannot)?</summary>
        public bool CanEdit => Roles.Any(r => r.Equals(WriterRole, StringComparison.OrdinalIgnoreCase)
                                              || r.Equals("manager", StringComparison.OrdinalIgnoreCase)
                                              || r.Equals("owner", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A container's user roles and the grant markers on it. <see cref="RolesComplete"/> false means the role list is a
    /// PREFIX (the page bound was hit, or Graph returned no body): nothing may be concluded from an absence.
    /// </summary>
    public sealed record ContainerAccess(
        IReadOnlyList<ContainerUserRole> Roles,
        bool RolesComplete,
        IReadOnlyDictionary<string, string> Markers);

    /// <summary>The outcome of <see cref="GrantMarkedWriterAsync"/>.</summary>
    public enum MarkedGrantOutcome
    {
        /// <summary>A new writer role was created and its marker recorded.</summary>
        Granted,

        /// <summary>The user already held a role (Graph 409). Nothing was created or recorded.</summary>
        AlreadyHeld,

        /// <summary>No grant stands: Graph refused it, or the marker could not be recorded and the grant was undone.</summary>
        Failed,
    }

    /// <summary>The outcome of <see cref="RemoveMarkedGrantAsync"/>.</summary>
    public enum MarkedRemovalOutcome
    {
        /// <summary>The marked writer role was deleted and its marker cleared.</summary>
        Removed,

        /// <summary>The permission no longer exists, or is no longer a plain writer role (someone changed it): only the marker was cleared.</summary>
        MarkerCleared,

        /// <summary>Nothing changed (a read or write failed); the next pass retries.</summary>
        Failed,
    }

    /// <summary>
    /// Reads <paramref name="containerId"/>'s user roles (paged, honest about completeness) and its grant markers
    /// (<see cref="StandingWriterMarkerPrefix"/> / <see cref="JitWriterMarkerPrefix"/> custom properties).
    /// Returns <see langword="null"/> when the container does not exist. Faults propagate.
    /// </summary>
    public virtual async Task<ContainerAccess?> ReadAccessAsync(string containerId, CancellationToken ct = default)
    {
        var markers = await ReadMarkersAsync(containerId, ct).ConfigureAwait(false);
        if (markers is null)
        {
            return null;
        }

        var read = await ReadPermissionsAsync(_graphClientFactory.ForApp(), containerId, ct).ConfigureAwait(false);
        var roles = read.Permissions
            .Where(p => !string.IsNullOrEmpty(p.Id) && p.GrantedToV2?.User is not null)
            .Select(p => new ContainerUserRole(
                p.Id!,
                (IReadOnlyList<string>)(p.Roles?.Where(r => !string.IsNullOrWhiteSpace(r)).ToList() ?? new List<string>()),
                GetUpnFromPermission(p),
                p.GrantedToV2!.User!.Id))
            .ToList();

        return new ContainerAccess(roles, read.EnumerationComplete, markers);
    }

    /// <summary>
    /// Only the grant markers on <paramref name="containerId"/> (one container read) — the cheap first look the removal
    /// pass takes at every secure container. <see langword="null"/> when the container does not exist; faults propagate.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, string>?> ReadMarkersAsync(string containerId, CancellationToken ct = default)
    {
        Microsoft.Graph.Models.FileStorageContainer? container;
        try
        {
            container = await _graphClientFactory.ForApp().Storage.FileStorage.Containers[containerId]
                .GetAsync(c => c.QueryParameters.Select = new[] { "id", "customProperties" }, ct)
                .ConfigureAwait(false);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }

        if (container is null)
        {
            return null;
        }

        return SpeAdminGraphService.ReadCustomProperties(container)
            .Where(p => p.Name.StartsWith(StandingWriterMarkerPrefix, StringComparison.Ordinal)
                        || p.Name.StartsWith(JitWriterMarkerPrefix, StringComparison.Ordinal))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>
    /// Grants <paramref name="userPrincipalName"/> a WRITER role on <paramref name="containerId"/> and records it under
    /// <c><paramref name="markerPrefix"/><paramref name="systemUserId"/></c>. Graph 409 (the user already holds a role)
    /// is <see cref="MarkedGrantOutcome.AlreadyHeld"/> and records nothing. A marker that cannot be written undoes the
    /// grant (<see cref="MarkedGrantOutcome.Failed"/>), so every grant this code leaves standing is removable.
    /// </summary>
    public virtual async Task<MarkedGrantOutcome> GrantMarkedWriterAsync(
        string containerId,
        string markerPrefix,
        Guid systemUserId,
        string userPrincipalName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(containerId) || string.IsNullOrWhiteSpace(userPrincipalName) || systemUserId == Guid.Empty)
        {
            return MarkedGrantOutcome.Failed;
        }

        var graphClient = _graphClientFactory.ForApp();
        string permissionId;
        try
        {
            var created = await graphClient.Storage.FileStorage.Containers[containerId].Permissions
                .PostAsync(new Permission
                {
                    Roles = [WriterRole],
                    GrantedToV2 = new SharePointIdentitySet
                    {
                        User = new SharePointIdentity
                        {
                            AdditionalData = new Dictionary<string, object> { ["userPrincipalName"] = userPrincipalName },
                        },
                    },
                }, cancellationToken: ct)
                .ConfigureAwait(false);

            if (string.IsNullOrEmpty(created?.Id))
            {
                _logger.LogError(
                    "[SPE-MEMBERSHIP] Graph returned no permission id granting writer on {ContainerId} to user {SystemUserId}.",
                    containerId, systemUserId);
                return MarkedGrantOutcome.Failed;
            }

            permissionId = created.Id;
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 409)
        {
            // The user already holds a role on this container — hand-granted, an owner, or a grant of ours made
            // concurrently. It is not recorded, so nothing here will ever remove it.
            _logger.LogInformation(
                "[SPE-MEMBERSHIP] User {SystemUserId} already holds a role on container {ContainerId}; nothing granted.",
                systemUserId, containerId);
            return MarkedGrantOutcome.AlreadyHeld;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[SPE-MEMBERSHIP] Granting writer on container {ContainerId} to user {SystemUserId} failed.",
                containerId, systemUserId);
            return MarkedGrantOutcome.Failed;
        }

        var key = MarkerKey(markerPrefix, systemUserId);
        try
        {
            await WriteMarkerAsync(graphClient, containerId, key, permissionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An unrecorded grant could never be told apart from a hand-granted one, so it must not stand.
            _logger.LogError(ex,
                "[SPE-MEMBERSHIP] The grant on container {ContainerId} for user {SystemUserId} could not be recorded; undoing it.",
                containerId, systemUserId);
            try
            {
                await DeleteContainerScopedPermissionAsync(graphClient, containerId, permissionId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception undoEx)
            {
                _logger.LogCritical(undoEx,
                    "[SPE-MEMBERSHIP] UNRECORDED writer permission {PermissionId} for user {SystemUserId} remains on container "
                    + "{ContainerId} and could not be removed; an operator must remove it.",
                    permissionId, systemUserId, containerId);
            }

            return MarkedGrantOutcome.Failed;
        }

        _logger.LogInformation(
            "[SPE-MEMBERSHIP] Granted writer on container {ContainerId} to user {SystemUserId} ({Marker}).",
            containerId, systemUserId, markerPrefix);
        return MarkedGrantOutcome.Granted;
    }

    /// <summary>
    /// Removes the grant the marker <paramref name="markerKey"/> records (permission <paramref name="permissionId"/>):
    /// deletes it only while it is still a plain WRITER role, with <see cref="OnlyContainerScopedPrefer"/>, then clears
    /// the marker. A permission that is gone, or was changed by someone else, only loses its marker.
    /// </summary>
    public virtual async Task<MarkedRemovalOutcome> RemoveMarkedGrantAsync(
        string containerId,
        string markerKey,
        string permissionId,
        ContainerAccess access,
        CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.ForApp();
        try
        {
            var current = access.Roles.FirstOrDefault(r => string.Equals(r.PermissionId, permissionId, StringComparison.Ordinal));
            if (current is null && !access.RolesComplete)
            {
                // Not seen, but the list was not read to its end: unproven — leave both and retry next pass.
                return MarkedRemovalOutcome.Failed;
            }

            var outcome = MarkedRemovalOutcome.MarkerCleared;
            if (current is not null && current.IsPlainWriter)
            {
                await DeleteContainerScopedPermissionAsync(graphClient, containerId, permissionId, ct).ConfigureAwait(false);
                outcome = MarkedRemovalOutcome.Removed;
            }
            else if (current is not null)
            {
                _logger.LogWarning(
                    "[SPE-MEMBERSHIP] Permission {PermissionId} on container {ContainerId} is no longer a plain writer role "
                    + "(someone changed it); it is no longer treated as this code's grant — only its marker is cleared.",
                    permissionId, containerId);
            }

            await WriteMarkerAsync(graphClient, containerId, markerKey, value: null, ct).ConfigureAwait(false);
            return outcome;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[SPE-MEMBERSHIP] Removing marked grant {MarkerKey} (permission {PermissionId}) from container {ContainerId} failed.",
                markerKey, permissionId, containerId);
            return MarkedRemovalOutcome.Failed;
        }
    }

    private static async Task WriteMarkerAsync(
        GraphServiceClient graphClient, string containerId, string key, string? value, CancellationToken ct)
    {
        // PATCH /containers/{id}/customProperties merges; a null value REMOVES the property (both proven live,
        // SpeAdminGraphService.UpdateCustomPropertiesAsync, 2026-08-28).
        var body = new Dictionary<string, object?>
        {
            [key] = value is null ? null : new Dictionary<string, object> { ["value"] = value, ["isSearchable"] = false },
        };
        var url = $"{SpeAdminGraphService.ResolveGraphBaseUrl(graphClient)}/storage/fileStorage/containers/"
                  + $"{Uri.EscapeDataString(containerId)}/customProperties";
        using var _ = await SpeAdminGraphService.SendGraphJsonAsync(
            graphClient, HttpMethod.Patch, url, System.Text.Json.JsonSerializer.Serialize(body), ct).ConfigureAwait(false);
    }

    private static async Task DeleteContainerScopedPermissionAsync(
        GraphServiceClient graphClient, string containerId, string permissionId, CancellationToken ct)
    {
        try
        {
            await graphClient.Storage.FileStorage.Containers[containerId].Permissions[permissionId]
                .DeleteAsync(rc => rc.Headers.Add("Prefer", OnlyContainerScopedPrefer), ct)
                .ConfigureAwait(false);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Already gone — the state the caller wanted.
        }
    }

    // =========================================================================
    // Paged reading (task 024, finding M1)
    // =========================================================================

    /// <summary>
    /// Every container permission this read could see, AND whether it saw all of them.
    /// </summary>
    /// <param name="Permissions">The permissions actually retrieved. May be a PREFIX of the container's set.</param>
    /// <param name="EnumerationComplete">
    /// <c>true</c> only when the collection was followed to its end. <c>false</c> means the page bound was
    /// hit, or Graph returned no body — in which case <paramref name="Permissions"/> is a partial view and
    /// <b>the absence of anything from it proves nothing</b>.
    /// </param>
    internal sealed record PermissionReadResult(
        IReadOnlyList<Permission> Permissions,
        bool EnumerationComplete);

    /// <summary>
    /// Reads a container's permission collection, FOLLOWING <c>@odata.nextLink</c> to the end.
    /// </summary>
    /// <remarks>
    /// <para><b>Finding M1 (review 2026-08-24), fixed by task 024.</b> Both reads in this class used to
    /// issue a single <c>.GetAsync()</c> and use <c>permissions?.Value</c> directly, ignoring
    /// <c>@odata.nextLink</c> entirely. Anything past the first page was invisible — so a partially
    /// cleared container could report clean, which defeats the exact guard tasks 016 and 017 were built
    /// to provide.</para>
    ///
    /// <para><b>On the severity, stated honestly (task 024 step 0).</b> Whether this endpoint currently
    /// emits <c>@odata.nextLink</c> is <b>not established</b>: the SPE docs list <c>$skip</c>/<c>$top</c>
    /// (client-driven) and not <c>$skiptoken</c>, and the documented sample response carries no
    /// <c>nextLink</c>. A live probe needs the BFF's own app identity and is filed to task 047. So this is
    /// <b>not</b> demonstrably a live hole today. It is fixed regardless, because ignoring
    /// <c>@odata.nextLink</c> is an OData <i>protocol</i> violation independent of current server
    /// behaviour — a service may begin server-driven paging at any time, and doing so is explicitly not a
    /// breaking change. Correctness here is against the protocol, not against one observed response.</para>
    ///
    /// <para><b>Shape copied from <c>SpeAdminGraphService.ListContainerPermissionsAsync</c></b>, which
    /// already pages THIS collection correctly. Deliberately NOT the <c>PageIterator</c> of
    /// <c>PrivilegeGroupResolver.cs:202</c>: that one collects page 1 in a <c>foreach</c> and then hands
    /// the same response to <c>CreatePageIterator</c>, double-counting it (ISS-001), and a page BOUND is
    /// far more directly expressible in a plain loop than through an iterator callback.</para>
    /// </remarks>
    private async Task<PermissionReadResult> ReadPermissionsAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct)
    {
        var collected = new List<Permission>();

        var response = await graphClient.Storage.FileStorage
            .Containers[containerId].Permissions
            .GetAsync(cancellationToken: ct);

        if (response == null)
        {
            // Not "an empty container" — Graph gave us no body at all, so we never read the set.
            // Claiming completeness here is the false-clean report ADR-003 forbids.
            _logger.LogError(
                "Graph returned no permission collection for container {ContainerId}; the member set is " +
                "UNREAD and must not be reported as empty.", containerId);
            return new PermissionReadResult(collected, EnumerationComplete: false);
        }

        var pagesRead = 0;

        while (true)
        {
            if (response.Value != null)
            {
                collected.AddRange(response.Value);
            }

            pagesRead++;

            if (string.IsNullOrEmpty(response.OdataNextLink))
            {
                return new PermissionReadResult(collected, EnumerationComplete: true);
            }

            if (pagesRead >= MaxPermissionPages)
            {
                _logger.LogError(
                    "Permission enumeration for container {ContainerId} hit the {Bound}-page bound with " +
                    "more pages remaining; {Count} read so far. The set is INCOMPLETE — nothing may be " +
                    "reported absent or cleared on the strength of it.",
                    containerId, MaxPermissionPages, collected.Count);
                return new PermissionReadResult(collected, EnumerationComplete: false);
            }

            var nextLink = response.OdataNextLink;

            _logger.LogDebug(
                "Following permission nextLink for container {ContainerId} (page {Page})",
                containerId, pagesRead + 1);

            response = await graphClient.Storage.FileStorage
                .Containers[containerId].Permissions
                .WithUrl(nextLink)
                .GetAsync(cancellationToken: ct);

            if (response == null)
            {
                _logger.LogError(
                    "Graph returned no body while following a permission nextLink for container " +
                    "{ContainerId}; the set is INCOMPLETE after {Count} entries.",
                    containerId, collected.Count);
                return new PermissionReadResult(collected, EnumerationComplete: false);
            }
        }
    }

    /// <summary>
    /// The task-024 honesty rule, as a pure function: what a revoke may CLAIM, given how much of the
    /// container it managed to read and whether the target was among it.
    /// </summary>
    /// <param name="enumerationComplete">Whether the permission collection was followed to its end.</param>
    /// <param name="deletedPermissionId">The permission actually deleted, or <c>null</c> if none matched.</param>
    /// <param name="contactEmail">The contact whose permission was sought (for the message only).</param>
    /// <remarks>
    /// <para><b>The vocabulary was already correct; the implementation lied about it.</b>
    /// <c>SpeContainerRevokeOutcome.NoPermissionFound</c> is documented as <i>"the container's permissions
    /// were read successfully and this Contact holds none — genuinely absent"</i>. Under a single-page
    /// read that sentence was FALSE: the read succeeded but was partial, so "genuinely absent" was
    /// unprovable. No enum member is added here — this makes the code honest to a contract that already
    /// exists.</para>
    ///
    /// <list type="table">
    ///   <item><term>complete + found</term><description>success — removed</description></item>
    ///   <item><term>complete + not found</term><description>benign absence (<see cref="NoPermissionFoundError"/>)</description></item>
    ///   <item><term>INCOMPLETE + found</term><description>success — the deletion is a FACT regardless of what we did not read</description></item>
    ///   <item><term>INCOMPLETE + not found</term><description>🔴 FAILURE — never a benign absence. We did not finish looking.</description></item>
    /// </list>
    ///
    /// <para>Pure and exhaustive so the rule is testable without Graph, and so that re-swallowing the
    /// incomplete case (the perturbation) fails a test rather than passing silently.</para>
    /// </remarks>
    internal static SpeContainerMembershipResult ClassifyRevokeResult(
        bool enumerationComplete,
        string? deletedPermissionId,
        string contactEmail)
    {
        if (deletedPermissionId != null)
        {
            // A deletion that happened, happened. An unread tail cannot retract it.
            return new SpeContainerMembershipResult(true, deletedPermissionId, null);
        }

        if (enumerationComplete)
        {
            return new SpeContainerMembershipResult(
                false, null, $"{NoPermissionFoundError} for user '{contactEmail}' in container.");
        }

        return new SpeContainerMembershipResult(
            false, null,
            $"Container permissions could not be fully enumerated, so no permission for user " +
            $"'{contactEmail}' can be confirmed absent. They may RETAIN file access; retry the revoke.");
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Finds a permission entry by matching the grantedTo user's UPN from AdditionalData.
    /// </summary>
    /// <summary>
    /// Finds the container permission belonging to <paramref name="email"/>, matching on the SAME key
    /// membership is written with — <c>userPrincipalName</c>.
    /// </summary>
    /// <remarks>
    /// <para><b><c>internal</c> for testability (task 025, finding H6).</b> This matcher IS finding
    /// A-13: the defect was that it compared against the contact's GUID, an email never contains a
    /// GUID, so it matched nothing and <c>/revoke</c> reported success while the ACL entry stayed.
    /// It was nonetheless executed by NO test — <c>SpeRevokeMatcherTests</c> substitutes
    /// <see cref="SpeContainerMembershipService"/> wholesale, which proves the CALLER and never the
    /// callee, so replacing this body with <c>return false</c> failed zero tests.</para>
    ///
    /// <para>Widened from <c>private</c> rather than reached by reflection (ADR-038 bans B8), matching
    /// the <c>ExternalParticipationService.ExpiryPredicate</c> precedent — the same "the predicate is
    /// the thing that broke, so test the predicate" shape. It is a pure function over a Graph model
    /// list, so testing it needs no transport and no <c>Mock&lt;HttpMessageHandler&gt;</c> (ban B1).</para>
    /// </remarks>
    internal static Permission? FindPermissionByEmail(IReadOnlyList<Permission>? permissions, string email)
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

}

/// <summary>
/// Result of an SPE container membership operation (grant or revoke).
/// </summary>
public sealed record SpeContainerMembershipResult(
    bool Success,
    string? PermissionId,
    string? Error);
