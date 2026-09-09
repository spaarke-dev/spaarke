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
        var (members, enumerationComplete) = await ReadExternalMembersAsync(containerId, ct);

        if (!enumerationComplete)
        {
            // This signature can return a list or throw — it has no way to say "here are SOME of them".
            // Returning the partial list would recreate the very defect task 016 filed, one layer up:
            // the caller would read a short list as the whole truth. Task 024 / finding M1.
            throw new InvalidOperationException(
                $"External members of container '{containerId}' could not be fully enumerated " +
                $"({members.Count} read before the read gave out). A partial list must not be returned " +
                $"here: an empty or short list is indistinguishable from the whole set to the caller.");
        }

        _logger.LogInformation(
            "Found {Count} external members in container {ContainerId}", members.Count, containerId);

        return members;
    }

    /// <summary>
    /// The worker behind <see cref="ListExternalMembersAsync"/>: the external members that were read,
    /// AND whether the underlying permission collection was enumerated to its end.
    /// </summary>
    /// <remarks>
    /// Separate from the public method because the two callers need different things from a partial read.
    /// <see cref="ListExternalMembersAsync"/> returns a bare list, so it cannot express partiality and
    /// must throw. <see cref="RemoveAllExternalMembersAsync"/> SHOULD still remove everyone it managed to
    /// see — aborting would leave strictly MORE access in place, which is the same reasoning tasks 016
    /// and 017 used for not aborting the loop on a per-member failure — and then report the read as
    /// incomplete so the closure guard fails.
    /// </remarks>
    internal async Task<(IReadOnlyList<SpeContainerMember> Members, bool EnumerationComplete)>
        ReadExternalMembersAsync(string containerId, CancellationToken ct)
    {
        _logger.LogInformation("Listing external SPE members: containerId={ContainerId}", containerId);

        var graphClient = _graphClientFactory.ForApp();

        var read = await ReadPermissionsAsync(graphClient, containerId, ct);

        // External members are those with a GrantedToV2.User (individual user grants).
        // System / app permissions and container-type-level grants do not have a User identity.
        var externalMembers = read.Permissions
            .Where(p => p.GrantedToV2?.User != null)
            .Select(ToContainerMember)
            .Where(m => m != null)
            .Cast<SpeContainerMember>()
            .ToList()
            .AsReadOnly();

        return (externalMembers, read.EnumerationComplete);
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

        // The worker, not ListExternalMembersAsync: a partial read must still have its members removed
        // (aborting leaves strictly MORE access in place), with the incompleteness reported afterwards.
        var (externalMembers, enumerationComplete) = await ReadExternalMembersAsync(containerId, ct);

        if (!enumerationComplete)
        {
            _logger.LogError(
                "Container {ContainerId} permissions could not be fully enumerated; removing the {Count} " +
                "external member(s) that WERE read, but the container CANNOT be reported cleared.",
                containerId, externalMembers.Count);
        }

        if (externalMembers.Count == 0)
        {
            _logger.LogInformation("No external members to remove from container {ContainerId}", containerId);
            return new SpeBulkRemovalResult(0, 0, enumerationComplete);
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

        return new SpeBulkRemovalResult(removedCount, failedCount, enumerationComplete);
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
/// <param name="EnumerationComplete">
/// Whether the container's permission collection was read to its END (task 024, finding M1).
/// <para><c>false</c> means members may exist that this sweep never saw, so <c>Removed</c> and
/// <c>Failed</c> describe only the part that was read. <b>A guard that could not finish its check must
/// report failure, not success</b> — reporting clean on an unenumerated set is worse than having no
/// guard, because a clean report gets acted on. Defaults to <c>true</c> so the existing two-argument
/// construction keeps its meaning.</para>
/// </param>
public sealed record SpeBulkRemovalResult(int Removed, int Failed, bool EnumerationComplete = true)
{
    /// <summary>
    /// True only when every external member was seen AND removed.
    /// </summary>
    /// <remarks>
    /// Both conjuncts are load-bearing. <c>Failed == 0</c> alone once meant "cleared" — which was a false
    /// clean whenever the member list itself was a partial read, since members nobody enumerated cannot
    /// fail to be removed. <c>ProjectClosureEndpoint</c> maps this straight onto
    /// <c>container_not_cleared</c>, so an incomplete enumeration now surfaces there with no change at
    /// the endpoint.
    /// </remarks>
    public bool IsComplete => Failed == 0 && EnumerationComplete;
}

/// <summary>
/// Represents an external member of an SPE container.
/// Email contains the user's UPN / email when available, or their DisplayName as a fallback.
/// </summary>
public sealed record SpeContainerMember(
    string PermissionId,
    string Email,
    IReadOnlyList<string> Roles);
