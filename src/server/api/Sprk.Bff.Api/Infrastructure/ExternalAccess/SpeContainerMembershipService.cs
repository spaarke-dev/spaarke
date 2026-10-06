using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Manages SPE (SharePoint Embedded) container membership for external users.
/// When a Contact is granted access to a Secure Project, they are added to
/// the project's SPE container so they can access files.
///
/// This service uses app-only Graph authentication since container permission management requires
/// elevated permissions beyond what OBO provides. Its client comes from
/// <see cref="SpeContainerOwnershipGuard"/>: a container this stamp does not own is refused (404) before Graph is called, so a forged <c>sprk_containerid</c> cannot add an external user to another
/// customer's container (task 227d).
/// </summary>
public class SpeContainerMembershipService
{
    private readonly SpeContainerOwnershipGuard _ownership;
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
        SpeContainerOwnershipGuard ownership,
        ILogger<SpeContainerMembershipService> logger)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
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

        // Ownership outside the try: a container this stamp does not own is refused (404 spe_container_not_owned) before Graph (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct);

        try
        {
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
            // Ownership INSIDE the try (task 227d): a refusal (a container this stamp does not own) or a marker read that
            // got no answer is a failed result, like any other — never an exception. A revoke caller keeps going (its grant
            // cache invalidation must still run) and reports the contact as not confirmed removed.
            var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct);
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

        // Ownership inside the try (task 227d): a refusal or an unanswered marker read fails every contact, like a client
        // that cannot be obtained — every member gets an outcome (the closure / revoke callers report them and carry on).
        GraphServiceClient graphClient;
        PermissionReadResult read;
        try
        {
            graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct);
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
