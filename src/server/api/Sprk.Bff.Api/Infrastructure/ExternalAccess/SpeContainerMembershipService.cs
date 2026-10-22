using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spaarke.Dataverse;
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
    /// of <see cref="RevokeMembershipAsync"/>: a permission it writes (<c>userPrincipalName</c> = contact email) is one
    /// the revoke matcher (<see cref="FindContactPermissions"/>: the bound object id, then the email against the
    /// permission's <c>email</c> and <c>userPrincipalName</c>) finds. Do NOT wire it up without revisiting the
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
        // #2111: the contact's email is the Graph identity here but never a log field (CODE-QUALITY-RUBRIC D9).
        _logger.LogInformation(
            "Granting SPE container membership: containerId={ContainerId}, accessLevel={AccessLevel}",
            containerId, accessLevel);

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
                    "Graph API returned null or missing ID when granting membership: containerId={ContainerId}",
                    containerId);
                return new SpeContainerMembershipResult(false, null, "Graph API returned null permission after grant.");
            }

            _logger.LogInformation(
                "Successfully granted SPE membership: containerId={ContainerId}, permissionId={PermissionId}",
                containerId, createdPermission.Id);

            return new SpeContainerMembershipResult(true, createdPermission.Id, null);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex,
                "Graph API error granting SPE membership: containerId={ContainerId}, status={StatusCode}",
                containerId, ex.ResponseStatusCode);
            return new SpeContainerMembershipResult(false, null, $"Graph API error ({ex.ResponseStatusCode}): {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error granting SPE membership: containerId={ContainerId}",
                containerId);
            return new SpeContainerMembershipResult(false, null, $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Revokes a Contact's membership from an SPE container.
    /// Finds the contact's permission entry by its identity key (<see cref="FindContactPermissions"/>) and deletes it.
    /// </summary>
    /// <param name="containerId">The SPE container ID (GUID format).</param>
    /// <param name="contact">The contact's identity key: its bound Entra object id and/or its email.</param>
    /// <param name="directory">The Dataverse the matched permission's holder is checked against: a permission held by a
    /// <c>systemuser</c> of this environment is an internal user's, never the contact's (#2104 round 2, K3).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Success with the deleted permission id; or failure whose <c>Error</c> distinguishes
    /// "no permission found for this user" from a Graph error, an ambiguous match, an unverifiable holder or a failed
    /// internal-user check.
    /// </returns>
    /// <remarks>
    /// <para>This is the canonical SPE revoke. It runs the SAME removal as <see cref="RemoveMembershipsAsync"/> for one
    /// key (<see cref="RemoveCoreAsync"/>), so both apply one set of rules: #2105's identity match (the contact's bound
    /// object id first, then its email against both the permission's <c>email</c> and its <c>userPrincipalName</c>),
    /// never a Spaarke-managed (marker-owned) permission, never an internal user's own permission, and never a guess
    /// between two matches.</para>
    ///
    /// <para><b>Task 017 / finding A-13</b>: <c>RevokeExternalAccessEndpoint</c> had its own private copy of
    /// this logic that searched for the contact's <b>GUID</b> inside the UPN. An email never contains a
    /// GUID, so it never matched — and it then returned <c>true</c> ("may have already been removed"),
    /// reporting revoke success while leaving the ACL entry in place. The fork is deleted; the endpoint
    /// calls this method.</para>
    ///
    /// <para><c>virtual</c> is the substitution seam (ADR-038 §4), matching the
    /// <c>DataverseWebApiClient</c> precedent — it lets the endpoint's revoke path be tested without
    /// mocking Graph transport (ban B1).</para>
    /// </remarks>
    public virtual async Task<SpeContainerMembershipResult> RevokeMembershipAsync(
        string containerId,
        SpeMembershipKey contact,
        DataverseWebApiClient directory,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Revoking SPE container membership: containerId={ContainerId}, contact={Contact}",
            containerId, contact);

        if (contact.IsEmpty)
        {
            // Callers decide "no key" BEFORE asking (RevokeExternalAccessEndpoint.HasNoContainerMembershipKey). An empty
            // key reaching here was never looked for, so it is a failure — never the benign "no permission found".
            return new SpeContainerMembershipResult(
                false, null, "No identity key (object id or email) to match a container permission by.");
        }

        var removal = await RemoveCoreAsync(containerId, [contact], directory, confirmNoContactPermission: false, ct);

        return removal.Results.TryGetValue(contact, out var result)
            ? result
            : new SpeContainerMembershipResult(false, null, "No outcome was produced for the contact.");
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
    // RemoveMembershipsAsync below (the single-grant revoke's identity-keyed mechanism: bound object id, then email —
    // FindContactPermissions). Its result type
    // (SpeBulkRemovalResult) went with it. Do not re-add an "everyone with a user identity" sweep.

    /// <summary>
    /// Removes MANY contacts' permissions from one container using a SINGLE paged read of the
    /// permission collection.
    /// </summary>
    /// <param name="containerId">The SPE container ID.</param>
    /// <param name="contacts">The contacts' identity keys. Duplicates (email casing included) and empty keys are tolerated;
    /// an empty key gets no entry.</param>
    /// <param name="directory">The Dataverse a matched permission's holder is checked against (K3, see
    /// <see cref="RevokeMembershipAsync"/>).</param>
    /// <param name="confirmNoContactPermission">#2104 round 2 (F1): also establish
    /// <see cref="SpeMembershipRemoval.ContactPermissionState"/> when no key matched — the container's grant markers are
    /// then read even though nothing matched (one extra container GET), the read is made even with no key at all, and
    /// (round 4) every unmanaged user permission's holder joins the one batched internal-user lookup.
    /// Close-project asks for it when a revoked grantee could not be enumerated (an organization over the sweep bound)
    /// and so can only be confirmed absent by the container itself holding no contact permission.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// One <see cref="SpeContainerMembershipResult"/> per DISTINCT key (<see cref="SpeMembershipKey"/> equality: the
    /// email compares case-insensitively) — the same shape <see cref="RevokeMembershipAsync"/> returns, so callers
    /// classify it identically — and whether any contact permission remains on the container.
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
    /// <para><b>Failure is per-caller-visible, not aggregated.</b> A failure to READ makes every key
    /// unanswerable, so each one gets a failed result rather than the method throwing — that preserves
    /// the caller's "every member gets an outcome, and the failures are counted" contract from tasks
    /// 016/017. An incomplete enumeration flows through <see cref="ClassifyRevokeResult"/>, so a member
    /// who is merely UNSEEN is never reported as genuinely absent.</para>
    /// </remarks>
    public virtual async Task<SpeMembershipRemoval> RemoveMembershipsAsync(
        string containerId,
        IReadOnlyCollection<SpeMembershipKey> contacts,
        DataverseWebApiClient directory,
        bool confirmNoContactPermission = false,
        CancellationToken ct = default)
    {
        var distinct = contacts
            .Where(k => k is not null && !k.IsEmpty)
            .Distinct()
            .ToList();

        if (distinct.Count == 0 && !confirmNoContactPermission)
        {
            return new SpeMembershipRemoval(
                new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>(), SpeContainerContactPermissionState.Unknown);
        }

        _logger.LogInformation(
            "Removing {Count} contact permission(s) from container {ContainerId} via one paged read",
            distinct.Count, containerId);

        return await RemoveCoreAsync(containerId, distinct, directory, confirmNoContactPermission, ct);
    }

    /// <summary>
    /// The one removal both <see cref="RevokeMembershipAsync"/> and <see cref="RemoveMembershipsAsync"/> run: one paged
    /// read, then per key at most ONE deletion, under these rules (#2105, #2104 round 2):
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item><b>Never a Spaarke-managed permission</b> — one a <c>SprkStd</c> / <c>SprkJit</c> / <c>SprkDemo</c> marker
    /// records. Markers unreadable (404) while a key matched: nothing is deleted, every key Failed.</item>
    /// <item><b>Never an internal user's own permission (K3).</b> Every permission a key matches is a candidate; their
    /// holders' object ids are looked up in ONE batched <c>systemusers</c> query
    /// (<see cref="QueryInternalUserObjectIdsAsync"/>), and a candidate held by a systemuser is ineligible — that user's
    /// access is internal, governed by their roles and markers, not by a contact grant. The lookup failing: nothing is
    /// deleted, every key Failed. A candidate whose holder carries no object id cannot be checked, so it is never
    /// deleted either: that key is Failed (<see cref="UnverifiableHolderError"/>).</item>
    /// <item><b>Never a guess (K2).</b> More than one eligible permission matching one key: nothing is deleted for it,
    /// Failed (<see cref="AmbiguousMatchError"/>) — removing "the first" could remove somebody else's access.</item>
    /// <item>A key whose only matches were excluded by rules 1–2 holds no permission of its OWN: on a complete read that is
    /// the healthy <see cref="NoPermissionFoundError"/>.</item>
    /// </list>
    /// <para>Two keys can name ONE permission (one contact's oid, another's email): it is deleted once, and both get that
    /// deletion's outcome rather than the second reading a "not found" off a permission the first just removed.</para>
    /// <para>Never throws: every fault becomes a per-key failed result (the closure / revoke callers report them and
    /// carry on).</para>
    /// </remarks>
    private async Task<SpeMembershipRemoval> RemoveCoreAsync(
        string containerId,
        IReadOnlyList<SpeMembershipKey> keys,
        DataverseWebApiClient directory,
        bool confirmNoContactPermission,
        CancellationToken ct)
    {
        var results = new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>();

        // Ownership inside the try (task 227d): a refusal or an unanswered marker read fails every contact, like a client
        // that cannot be obtained — every member gets an outcome.
        GraphServiceClient graphClient;
        PermissionReadResult read;
        IReadOnlySet<string>? managed;
        bool anyMatch;
        bool markersRead;
        try
        {
            graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct);
            read = await ReadPermissionsAsync(graphClient, containerId, ct);

            // The marker exclusion can only remove a candidate, so the markers are read only when some key matches some
            // permission — the common broker-only answer (nobody matches) costs no extra Graph call — or when the caller
            // needs to know whether a contact permission remains at all (F1).
            anyMatch = keys.Any(k => CandidatePermissions(read.Permissions, k, NoManagedPermissions).Count > 0);
            markersRead = anyMatch
                || (confirmNoContactPermission && read.EnumerationComplete && read.Permissions.Any(IsUserPermission));
            managed = markersRead
                ? await ReadManagedPermissionIdsAsync(graphClient, containerId, ct)
                : NoManagedPermissions;
        }
        catch (Exception ex)
        {
            // Nobody can be confirmed absent, so nobody is. One failure, reported N times, because the
            // caller reports per member.
            _logger.LogError(ex,
                "Could not read permissions for container {ContainerId}; NONE of the {Count} contact(s) " +
                "can be confirmed removed. They may RETAIN file access.",
                containerId, keys.Count);

            foreach (var key in keys)
            {
                results[key] = new SpeContainerMembershipResult(
                    false, null, $"Container permissions could not be read: {ex.Message}");
            }

            return new SpeMembershipRemoval(results, SpeContainerContactPermissionState.Unknown);
        }

        if (managed is null)
        {
            // #2105: without the marker store no matched permission can be told apart from a Spaarke-managed one —
            // nothing is deleted, and nobody is claimed absent. (Read only for F1, with nothing matched: every key's own
            // answer stands, but whether a contact permission remains is unknown.)
            foreach (var key in keys)
            {
                results[key] = anyMatch
                    ? MarkersUnreadable(containerId, key)
                    : ClassifyRevokeResult(read.EnumerationComplete, null, key.ToString());
            }

            return new SpeMembershipRemoval(results, SpeContainerContactPermissionState.Unknown);
        }

        // K3: every permission some key matches (managed ones aside) is a candidate; one batched lookup tells which are
        // held by an internal user.
        var candidates = keys
            .SelectMany(k => CandidatePermissions(read.Permissions, k, managed))
            .DistinctBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
        // #2104 round 4: when the caller asks whether a contact permission remains, every OTHER unmanaged user permission's
        // holder is checked in the same batched lookup — an internal user's hand-granted role is not a contact permission,
        // so it must not make the answer "one remains". (Only on a complete read: an incomplete one is Unknown anyway.)
        var lookedUpObjectIds = candidates.SelectMany(PermissionUserObjectIds)
            .Concat(confirmNoContactPermission && read.EnumerationComplete
                ? read.Permissions
                    .Where(p => IsUserPermission(p) && !(p.Id is { } id && managed.Contains(id)))
                    .SelectMany(PermissionUserObjectIds)
                : [])
            .Distinct()
            .ToList();

        IReadOnlySet<Guid> internalObjectIds = NoInternalUsers;
        if (lookedUpObjectIds.Count > 0)
        {
            try
            {
                internalObjectIds = await QueryInternalUserObjectIdsAsync(directory, lookedUpObjectIds, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Could not check whether the {Count} permission holder(s) on container {ContainerId} are "
                    + "internal users; NO permission was removed and none of the {Keys} contact(s) can be confirmed "
                    + "removed (K3).", lookedUpObjectIds.Count, containerId, keys.Count);

                foreach (var key in keys)
                {
                    results[key] = new SpeContainerMembershipResult(
                        false, null,
                        $"{InternalUserCheckFailedError}: the matched permission holders could not be checked against "
                        + $"this environment's users, so no permission for user '{key}' was removed or can be confirmed "
                        + "absent. They may RETAIN file access; retry the revoke.");
                }

                return new SpeMembershipRemoval(results, SpeContainerContactPermissionState.Unknown);
            }
        }

        var ineligible = new HashSet<string>(managed, StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (PermissionUserObjectIds(candidate).Any(internalObjectIds.Contains))
            {
                ineligible.Add(candidate.Id!);
            }
        }

        var deletions = new Dictionary<string, SpeContainerMembershipResult>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var matches = FindContactPermissions(read.Permissions, key, ineligible);

            if (matches.Count == 0)
            {
                LogExcludedMatchesLeftInPlace(read.Permissions, key, managed, ineligible, containerId);

                // Whether this is a benign absence or a failure depends on whether we finished LOOKING.
                if (!read.EnumerationComplete)
                {
                    _logger.LogError(
                        "SPE permissions for container {ContainerId} could not be fully enumerated; the " +
                        "absence of a permission for {Contact} is UNPROVEN. They may RETAIN file access.",
                        containerId, key);
                }

                results[key] = ClassifyRevokeResult(read.EnumerationComplete, null, key.ToString());
                continue;
            }

            if (matches.Count > 1)
            {
                _logger.LogError(
                    "{Count} permissions on container {ContainerId} ({PermissionIds}) match {Contact}; NONE was removed, "
                    + "because removing one could remove somebody else's access. They may RETAIN file access until an "
                    + "administrator removes the right one in the SPE admin app (K2).",
                    matches.Count, containerId, string.Join(", ", matches.Select(m => m.Id)), key);

                results[key] = new SpeContainerMembershipResult(
                    false, null,
                    $"{AmbiguousMatchError}: {matches.Count} permissions on the container match user '{key}', so none "
                    + "was removed. They may RETAIN file access; an administrator must remove the right one.");
                continue;
            }

            var target = matches[0];

            if (!PermissionUserObjectIds(target).Any())
            {
                _logger.LogError(
                    "Permission {PermissionId} on container {ContainerId} matches {Contact} but names no holder object id, "
                    + "so it cannot be checked against this environment's internal users; it was NOT removed (K3).",
                    target.Id, containerId, key);

                results[key] = new SpeContainerMembershipResult(
                    false, null,
                    $"{UnverifiableHolderError}: the permission matching user '{key}' names no object id, so it cannot be "
                    + "told apart from an internal user's and was not removed. They may RETAIN file access.");
                continue;
            }

            if (deletions.TryGetValue(target.Id!, out var earlier))
            {
                results[key] = earlier;
                continue;
            }

            try
            {
                await graphClient.Storage.FileStorage
                    .Containers[containerId].Permissions[target.Id]
                    .DeleteAsync(cancellationToken: ct);

                _logger.LogInformation(
                    "Removed SPE permission {PermissionId} of {Contact} from container {ContainerId}",
                    target.Id, key, containerId);

                results[key] = deletions[target.Id!] = ClassifyRevokeResult(read.EnumerationComplete, target.Id, key.ToString());
            }
            catch (Exception ex)
            {
                // One member's delete failing must not abandon the rest — stopping early leaves
                // strictly MORE access in place (tasks 016/017).
                _logger.LogError(ex,
                    "Failed to delete permission {PermissionId} for {Contact} on container {ContainerId}. " +
                    "They may RETAIN file access. Continuing with the rest.",
                    target.Id, key, containerId);

                results[key] = deletions[target.Id!] = new SpeContainerMembershipResult(
                    false, null, $"Graph error deleting permission: {ex.Message}");
            }
        }

        return new SpeMembershipRemoval(
            results,
            ContactPermissionStateAfter(read, markersRead, managed, deletions, internalObjectIds, lookedUpObjectIds, results));
    }

    /// <summary>
    /// F1 (#2104 round 2; three-state since round 4): after a removal, is any permission left that could be a contact's?
    /// Per user permission on the read: Spaarke-managed, deleted here, or held by an internal user — not a contact's. A
    /// failed delete, or a permission not judged (markers not read, holder not looked up) —
    /// <see cref="SpeContainerContactPermissionState.Unknown"/>: a retry may change it. Any other — its holder checked and
    /// not internal, or naming no object id to check — definitely
    /// <see cref="SpeContainerContactPermissionState.ContactPermissionRemains"/>, as does a key's ambiguous or unverifiable
    /// match. A definite remainder wins over an unknown, and an incomplete read is at best unknown.
    /// </summary>
    private static SpeContainerContactPermissionState ContactPermissionStateAfter(
        PermissionReadResult read,
        bool markersRead,
        IReadOnlySet<string> managed,
        IReadOnlyDictionary<string, SpeContainerMembershipResult> deletions,
        IReadOnlySet<Guid> internalObjectIds,
        IReadOnlyCollection<Guid> lookedUpObjectIds,
        IReadOnlyDictionary<SpeMembershipKey, SpeContainerMembershipResult> results)
    {
        var remains = results.Values.Any(IsAmbiguousPermissionMatch);
        var unknown = !read.EnumerationComplete;

        foreach (var permission in read.Permissions.Where(IsUserPermission))
        {
            if (permission.Id is { } id)
            {
                if (managed.Contains(id))
                {
                    continue;
                }

                if (deletions.TryGetValue(id, out var deletion))
                {
                    // Deleted: gone. Failed: still there, but a retry may delete it.
                    unknown |= !deletion.Success;
                    continue;
                }
            }

            var holders = PermissionUserObjectIds(permission).ToList();
            if (holders.Any(internalObjectIds.Contains))
            {
                continue;
            }

            if (markersRead && holders.All(lookedUpObjectIds.Contains))
            {
                remains = true;
            }
            else
            {
                unknown = true;
            }
        }

        return remains
            ? SpeContainerContactPermissionState.ContactPermissionRemains
            : unknown
                ? SpeContainerContactPermissionState.Unknown
                : SpeContainerContactPermissionState.Clean;
    }

    // =========================================================================
    // MARKED grants — unified-access-control-r2 task 171 (owner rounds 69 + 70)
    //
    // The ONLY code that grants a user an SPE container role. Three kinds, one mechanism:
    //   * STANDING writers on an environment / business-unit container (round 70 option (c)) — every enabled,
    //     internal person user of the business unit(s) that container serves, kept by SpeContainerMembershipSyncJob.
    //   * JUST-IN-TIME writers on a per-record SECURE container (round 69 (2), round 70) — a user with Write on the
    //     secure record, granted on an Office edit-open by OfficeEditAccessService and removed by the same job.
    //   * DEMO writers on a demo environment's container (#1363) — granted by demo self-registration (Step 8) and
    //     removed by demo expiry. The sync never reads or changes them (see DemoWriterMarkerPrefix).
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

    /// <summary>
    /// Marker prefix of a DEMO self-registration writer grant (#1363). Unlike the other two kinds, the key names the
    /// holder's ENTRA object id, not a systemuserid: the demo user's systemuser lives in the demo environment's
    /// Dataverse, which is not the one this BFF's sync reads, so a systemuserid key could never be resolved there. Demo
    /// expiry owns the grant's whole lifecycle (it has the object id on the registration request); the standing and
    /// just-in-time passes parse only their own prefixes and so never count, keep or remove one.
    /// </summary>
    public const string DemoWriterMarkerPrefix = "SprkDemo";

    /// <summary>The narrowest container role that lets Office edit a file (step 0 (b)).</summary>
    internal const string WriterRole = "writer";

    /// <summary>
    /// <c>Prefer</c> value that makes a container-permission DELETE leave the identity's ITEM-level permissions alone
    /// (by default the delete also removes access to every item in the container). A marked grant is a container role,
    /// so that is all its removal takes away.
    /// </summary>
    internal const string OnlyContainerScopedPrefer = "onlyRemoveContainerScopedPermission";

    /// <summary>
    /// Is <paramref name="propertyName"/> a grant marker (any of the three prefixes, case-insensitive)? The SPE admin custom-property
    /// editor RESERVES these names (task 171, adversarial finding 7): a marker edited by hand would make a hand-granted
    /// role removable — or make a marked grant unremovable — so no admin route may create, change, delete or show one.
    /// </summary>
    public static bool IsGrantMarkerName(string? propertyName)
    {
        var name = propertyName?.Trim();
        return !string.IsNullOrEmpty(name)
               && (name.StartsWith(StandingWriterMarkerPrefix, StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith(JitWriterMarkerPrefix, StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith(DemoWriterMarkerPrefix, StringComparison.OrdinalIgnoreCase));
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
    /// (<see cref="StandingWriterMarkerPrefix"/> / <see cref="JitWriterMarkerPrefix"/> / <see cref="DemoWriterMarkerPrefix"/>
    /// custom properties).
    /// Returns <see langword="null"/> when the container does not exist. A container this stamp does not own is refused
    /// with <see cref="Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException"/> <c>spe_container_not_owned</c> (404) before Graph is called (task 227d).
    /// Other faults propagate.
    /// </summary>
    public virtual async Task<ContainerAccess?> ReadAccessAsync(string containerId, CancellationToken ct = default)
    {
        // One ownership check for both reads (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct).ConfigureAwait(false);

        var markers = await ReadMarkersCoreAsync(graphClient, containerId, ct).ConfigureAwait(false);
        if (markers is null)
        {
            return null;
        }

        var read = await ReadPermissionsAsync(graphClient, containerId, ct).ConfigureAwait(false);
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
    /// pass takes at every secure container. <see langword="null"/> when the container does not exist. A container this
    /// stamp does not own is refused (<c>spe_container_not_owned</c>, 404) before Graph is called; other faults propagate.
    /// </summary>
    public virtual async Task<IReadOnlyDictionary<string, string>?> ReadMarkersAsync(string containerId, CancellationToken ct = default)
    {
        // Ownership outside any try: a refusal is never "does not exist" (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct).ConfigureAwait(false);
        return await ReadMarkersCoreAsync(graphClient, containerId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether <paramref name="ex"/>, propagated by the container read of <see cref="ReadMarkersAsync"/>, is Graph saying
    /// the container id does not RESOLVE in this tenancy (<c>400 invalidRequest</c>, e.g. "Invalid hostname for this
    /// tenancy" when the container's SharePoint site is gone) — an answer that will not change on retry — rather than a
    /// failure to answer.
    /// </summary>
    /// <remarks>
    /// <para>The one shared rule, <see cref="SpeAdminGraphService.IsUnresolvableContainerId"/> (measured on dev 2026-10-06,
    /// for the single-container GET only): only the <c>invalidRequest</c> code counts; a malformed query answers
    /// <c>400 BadRequest</c> and stays a fault, as do 429, 5xx and anything that is not a Graph <c>ODataError</c>. It is
    /// not a verdict on a later read of a container whose id has already resolved (the permission collection and its
    /// <c>@odata.nextLink</c> pages): a 400 there is a fault. The ownership guard's own read maps every 400 to "not owned"
    /// (a refusal the JIT sweep also reports without counting a failure), so in the JIT sweep both paths give the same
    /// outcome for an id that does not resolve.</para>
    /// <para><b>Deliberately NOT mapped to <see langword="null"/> in the reads</b> (#1830, task 192): their other callers treat
    /// null as "the container does not exist, nothing to revoke" (<c>DemoExpirationJob</c>), which would fail OPEN on an id
    /// Graph merely cannot resolve. Only the just-in-time removal sweep classifies it, only for a fault from its FIRST read
    /// of a container, and only to report it rather than count it failed — it still touches nothing there.</para>
    /// </remarks>
    internal static bool IsUnresolvableContainer(Exception ex)
        => ex is Microsoft.Graph.Models.ODataErrors.ODataError odata && SpeAdminGraphService.IsUnresolvableContainerId(odata);

    private static async Task<IReadOnlyDictionary<string, string>?> ReadMarkersCoreAsync(
        GraphServiceClient graphClient, string containerId, CancellationToken ct)
    {
        Microsoft.Graph.Models.FileStorageContainer? container;
        try
        {
            container = await graphClient.Storage.FileStorage.Containers[containerId]
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
                        || p.Name.StartsWith(JitWriterMarkerPrefix, StringComparison.Ordinal)
                        || p.Name.StartsWith(DemoWriterMarkerPrefix, StringComparison.Ordinal))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>
    /// Grants <paramref name="userPrincipalName"/> a WRITER role on <paramref name="containerId"/> and records it under
    /// <c><paramref name="markerPrefix"/><paramref name="systemUserId"/></c>. Graph 409 (the user already holds a role)
    /// is <see cref="MarkedGrantOutcome.AlreadyHeld"/> and records nothing. A marker that cannot be written undoes the
    /// grant (<see cref="MarkedGrantOutcome.Failed"/>), so every grant this code leaves standing is removable.
    /// </summary>
    /// <param name="systemUserId">The holder id the marker key names: the systemuserid for the standing and just-in-time
    /// prefixes, the Entra object id for <see cref="DemoWriterMarkerPrefix"/>.</param>
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

        // Ownership before the try: a container this stamp does not own is refused (404), not reported as Failed (task 227d).
        var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct).ConfigureAwait(false);
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
        try
        {
            // Inside the try (#1830, task 192): a guard that cannot answer (SpaarkeStorageException on a throttled or failed
            // ownership read, an unresolved customer identity) fails THIS grant closed — Failed, its marker kept — instead of
            // throwing out of the caller's loop and skipping the container's remaining grants until the next run. A refusal
            // (404 spe_container_not_owned) still propagates, not reported as Failed (task 227d): see the catch filter.
            var graphClient = await _ownership.ForOwnedContainerAsync(containerId, ct).ConfigureAwait(false);

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
        catch (Exception ex) when (ex is not OperationCanceledException
                                   && ex is not Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException
                                   {
                                       Code: SpeContainerOwnershipGuard.NotOwnedErrorCode,
                                   })
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
    /// <param name="contact">The contact whose permission was sought (for the message only).</param>
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
        string contact)
    {
        if (deletedPermissionId != null)
        {
            // A deletion that happened, happened. An unread tail cannot retract it.
            return new SpeContainerMembershipResult(true, deletedPermissionId, null);
        }

        if (enumerationComplete)
        {
            return new SpeContainerMembershipResult(
                false, null, $"{NoPermissionFoundError} for user '{contact}' in container.");
        }

        return new SpeContainerMembershipResult(
            false, null,
            $"Container permissions could not be fully enumerated, so no permission for user " +
            $"'{contact}' can be confirmed absent. They may RETAIN file access; retry the revoke.");
    }

    /// <summary>
    /// #2105: the ids of the Spaarke-managed permissions on <paramref name="containerId"/> — every permission a grant
    /// marker (<see cref="StandingWriterMarkerPrefix"/>, <see cref="JitWriterMarkerPrefix"/>,
    /// <see cref="DemoWriterMarkerPrefix"/>) records as its value (<see cref="GrantMarkedWriterAsync"/> writes
    /// <c>&lt;prefix&gt;&lt;holder&gt; = &lt;permission id&gt;</c>; the sync and demo expiry own their removal).
    /// <see cref="RemoveCoreAsync"/> reads them only when some key matches some permission (the exclusion can only remove
    /// a candidate, so the common broker-only answer costs no extra Graph call) or when its caller asked whether a contact
    /// permission remains.
    /// </summary>
    /// <returns>The managed ids (empty when no marker exists), or <see langword="null"/> when the container answered 404
    /// to the marker read — the store could not be read. Other faults propagate (to the caller's fail-closed catch).</returns>
    private static async Task<IReadOnlySet<string>?> ReadManagedPermissionIdsAsync(
        GraphServiceClient graphClient,
        string containerId,
        CancellationToken ct)
    {
        var markers = await ReadMarkersCoreAsync(graphClient, containerId, ct).ConfigureAwait(false);

        return markers?.Values
            .Where(permissionId => !string.IsNullOrWhiteSpace(permissionId))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static readonly IReadOnlySet<string> NoManagedPermissions = new HashSet<string>(StringComparer.Ordinal);

    private static readonly IReadOnlySet<Guid> NoInternalUsers = new HashSet<Guid>();

    /// <summary>
    /// #2104 round 2 (K2): the error text when more than one eligible permission matches one contact's key. Nothing is
    /// deleted — removing "the first" could remove somebody else's access. A failure, never the benign absence.
    /// </summary>
    internal const string AmbiguousMatchError = "Ambiguous permission match";

    /// <summary>
    /// #2104 round 2 (K3): the error text when the one matching permission names no holder object id, so it cannot be
    /// checked against this environment's internal users. Not deleted; a failure, never the benign absence.
    /// </summary>
    internal const string UnverifiableHolderError = "Unverifiable permission holder";

    /// <summary>
    /// #2104 round 2 (K3): the error text when the internal-user lookup failed. Nothing is deleted; a failure, never the
    /// benign absence.
    /// </summary>
    internal const string InternalUserCheckFailedError = "Internal-user check failed";

    /// <summary>
    /// #2104 round 3: is <paramref name="result"/> a failure a RETRY cannot fix — an ambiguous match
    /// (<see cref="AmbiguousMatchError"/>) or an unverifiable holder (<see cref="UnverifiableHolderError"/>)? A retry reads
    /// the same permissions and reaches the same answer until an administrator resolves the container's permissions in
    /// the SPE admin app, so the revoke and the closure name it (<c>…incomplete.permission_ambiguous</c>) instead of
    /// "retry". Every other failure (a read, a lookup, a marker read or a delete that faulted) stays retryable.
    /// </summary>
    internal static bool IsAmbiguousPermissionMatch(SpeContainerMembershipResult result)
        => !result.Success
           && result.Error is { } error
           && (error.StartsWith(AmbiguousMatchError, StringComparison.Ordinal)
               || error.StartsWith(UnverifiableHolderError, StringComparison.Ordinal));

    /// <summary>The Dataverse entity set of internal users.</summary>
    internal const string SystemUserEntitySet = "systemusers";

    /// <summary>How many holder object ids one <see cref="QueryInternalUserObjectIdsAsync"/> request carries (keeps the
    /// <c>$filter</c> URL short). The candidates of one removal are the permissions its keys match, so this is one request
    /// in practice.</summary>
    internal const int InternalUserLookupBatchSize = 50;

    /// <summary>
    /// #2104 round 2 (K3): which of <paramref name="objectIds"/> is the Entra object id
    /// (<c>systemuser.azureactivedirectoryobjectid</c>) of a <c>systemuser</c> in this environment — the holders whose
    /// permissions are an internal user's own and never a contact cleanup's to remove. One query per
    /// <see cref="InternalUserLookupBatchSize"/> ids, never one per permission. Faults propagate.
    /// </summary>
    private static async Task<IReadOnlySet<Guid>> QueryInternalUserObjectIdsAsync(
        DataverseWebApiClient directory,
        IReadOnlyList<Guid> objectIds,
        CancellationToken ct)
    {
        var found = new HashSet<Guid>();

        foreach (var batch in objectIds.Chunk(InternalUserLookupBatchSize))
        {
            // includes disabled: a disabled internal user is still an internal user, and their own permission is still
            // not a contact grant's to remove (K3) — the check excludes, it never admits.
            var filter = string.Join(" or ", batch.Select(id => $"azureactivedirectoryobjectid eq {id:D}"));

            var users = await directory.QueryAsync<SystemUserObjectIdRow>(
                SystemUserEntitySet,
                filter: filter,
                select: "systemuserid,azureactivedirectoryobjectid",
                cancellationToken: ct).ConfigureAwait(false);

            foreach (var user in users)
            {
                if (user.azureactivedirectoryobjectid is { } objectId && batch.Contains(objectId))
                {
                    found.Add(objectId);
                }
            }
        }

        return found;
    }

    /// <summary>The <c>systemuser</c> columns <see cref="QueryInternalUserObjectIdsAsync"/> reads.</summary>
    internal sealed class SystemUserObjectIdRow
    {
        public Guid? systemuserid { get; set; }
        public Guid? azureactivedirectoryobjectid { get; set; }
    }

    /// <summary>The fail-closed result when the marker store could not be read (#2105): not removed, not absent.</summary>
    private SpeContainerMembershipResult MarkersUnreadable(string containerId, SpeMembershipKey contact)
    {
        _logger.LogError(
            "The grant markers of container {ContainerId} could not be read, so no permission there can be told apart "
            + "from a Spaarke-managed one; the permission of {Contact} was NOT removed and may remain (#2105).",
            containerId, contact);

        return new SpeContainerMembershipResult(
            false, null,
            $"The container's grant markers could not be read, so no permission for user '{contact}' can be removed "
            + "or confirmed absent. They may RETAIN file access; retry the revoke.");
    }

    /// <summary>Logs, without acting on them, the permissions that matched <paramref name="contact"/> but are Spaarke-managed
    /// (#2105) or an internal user's own (K3).</summary>
    private void LogExcludedMatchesLeftInPlace(
        IReadOnlyList<Permission> permissions,
        SpeMembershipKey contact,
        IReadOnlySet<string> managed,
        IReadOnlySet<string> ineligible,
        string containerId)
    {
        foreach (var excluded in CandidatePermissions(permissions, contact, NoManagedPermissions)
                     .Where(p => ineligible.Contains(p.Id!)))
        {
            _logger.LogWarning(
                "Permission {PermissionId} on container {ContainerId} matches {Contact} but is {Kind}; a contact revoke "
                + "never removes it — its own lifecycle does (#2105, K3).",
                excluded.Id, containerId, contact,
                managed.Contains(excluded.Id!)
                    ? "a Spaarke-managed grant (a standing, just-in-time or demo writer role, recorded by a marker)"
                    : "an internal user's own permission (its holder is a systemuser of this environment)");
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// THE container-permission matcher for a contact revoke (the single-grant revoke, each member of an organization
    /// revoke, and project closure): the eligible permissions <paramref name="contact"/>'s DECIDING key matches — at most
    /// one in a healthy container. <see cref="RemoveCoreAsync"/> deletes only when exactly one is returned.
    /// </summary>
    /// <param name="permissions">The container's permissions as read.</param>
    /// <param name="contact">The contact's identity key.</param>
    /// <param name="ineligiblePermissionIds">Never matched: the Spaarke-managed permission ids (the marker values,
    /// <see cref="ReadManagedPermissionIdsAsync"/>) and the permissions held by an internal user (K3).</param>
    /// <remarks>
    /// <para><b>#2105 and #2104 round 2, four rules.</b></para>
    /// <list type="number">
    /// <item><b>Never an ineligible permission.</b> The standing, just-in-time and demo writer roles belong to a
    /// systemuser (or a demo user) and to the sync / demo expiry that own their lifecycle; an internal user's own
    /// hand-granted role is governed by their internal roles. When an internal user is also a contact whose email equals
    /// their UPN, the email match used to DELETE that role. A contact grant never created one, so a contact revoke never
    /// removes one.</item>
    /// <item><b>Strongest key first: the bound Entra object id</b> (<c>contact.sprk_externalobjectid</c>, written by
    /// <c>ContactBindingDecision</c>'s <c>CreateByOid</c> / binds), against the permission's
    /// <c>grantedToV2.user.id</c> or <c>grantedTo.user.id</c> — the shape <see cref="ReadAccessAsync"/> already reads
    /// as the holder's object id. When the object id matches some eligible permission, it decides; the email is not
    /// consulted. A CIAM or home-tenant oid is not a principal in this tenant and simply matches nothing (object ids are
    /// globally unique), so the email rule below still applies to those contacts.</item>
    /// <item><b>Then the email</b>, trimmed and case-insensitively, against BOTH the permission's <c>email</c> AND its
    /// <c>userPrincipalName</c>. A B2B guest's UPN is <c>alice_contoso.com#EXT#@tenant…</c> while its <c>email</c> is
    /// <c>alice@contoso.com</c>; comparing the UPN alone (the old first-wins read of UPN, then email) missed every
    /// such guest and reported "no permission found" while they kept file access.</item>
    /// <item><b>Every match is returned, never the first (K2).</b> Two eligible permissions under the deciding key is an
    /// ambiguity the caller must not resolve by guessing.</item>
    /// </list>
    /// <para><b>Graph shape — an assumption, stated.</b> SPE container permissions carry the user under
    /// <c>grantedToV2.user</c> (the legacy <c>grantedTo.user</c> is read too) with <c>id</c> typed and <c>email</c> /
    /// <c>userPrincipalName</c> in <c>AdditionalData</c> as strings (Kiota returns JSON scalars as CLR scalars). Only
    /// <c>userPrincipalName</c> is proven by this codebase's writes; <c>id</c> as the holder's object id is the
    /// assumption <see cref="ContainerUserRole.IsFor"/> already makes and the documented list-permissions example shows,
    /// and <c>email</c> follows the documented <c>sharePointIdentity</c> shape. A permission missing a field simply does
    /// not match on it.</para>
    /// <para><b><c>internal</c> for testability (task 025, finding H6).</b> The matcher IS finding A-13 (it once compared
    /// the contact's GUID, matched nothing, and <c>/revoke</c> reported success). A pure function over Graph models, so
    /// it is tested directly — no transport, no <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1), no reflection (B8).</para>
    /// </remarks>
    internal static IReadOnlyList<Permission> FindContactPermissions(
        IReadOnlyList<Permission>? permissions,
        SpeMembershipKey contact,
        IReadOnlySet<string> ineligiblePermissionIds)
    {
        if (permissions == null || contact.IsEmpty) return [];

        bool Eligible(Permission p) => !string.IsNullOrEmpty(p.Id) && !ineligiblePermissionIds.Contains(p.Id);

        if (contact.ObjectId is { } objectId)
        {
            var byObjectId = permissions
                .Where(p => Eligible(p) && MatchesObjectId(p, objectId))
                .DistinctBy(p => p.Id, StringComparer.Ordinal)
                .ToList();

            if (byObjectId.Count > 0)
            {
                return byObjectId;
            }
        }

        return contact.Email is { } email
            ? permissions
                .Where(p => Eligible(p) && MatchesEmail(p, email))
                .DistinctBy(p => p.Id, StringComparer.Ordinal)
                .ToList()
            : [];
    }

    /// <summary>
    /// Every eligible (not <paramref name="ineligiblePermissionIds"/>) permission <paramref name="contact"/> matches under
    /// EITHER key — the candidates whose holders <see cref="RemoveCoreAsync"/> checks against the internal users (K3)
    /// before <see cref="FindContactPermissions"/> decides.
    /// </summary>
    private static IReadOnlyList<Permission> CandidatePermissions(
        IReadOnlyList<Permission> permissions,
        SpeMembershipKey contact,
        IReadOnlySet<string> ineligiblePermissionIds)
    {
        if (contact.IsEmpty) return [];

        return permissions
            .Where(p => !string.IsNullOrEmpty(p.Id) && !ineligiblePermissionIds.Contains(p.Id)
                && ((contact.ObjectId is { } objectId && MatchesObjectId(p, objectId))
                    || (contact.Email is { } email && MatchesEmail(p, email))))
            .DistinctBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static bool MatchesObjectId(Permission permission, Guid objectId)
        => PermissionUserObjectIds(permission).Contains(objectId);

    private static bool MatchesEmail(Permission permission, string email)
        => PermissionUserAddresses(permission).Any(a => string.Equals(a.Trim(), email, StringComparison.OrdinalIgnoreCase));

    /// <summary>Does the permission name a user (<c>grantedToV2.user</c> or the legacy <c>grantedTo.user</c>) — the only
    /// kind a contact's permission can be?</summary>
    private static bool IsUserPermission(Permission permission) => PermissionUsers(permission).Any();

    /// <summary>The user identities a permission names: <c>grantedToV2.user</c>, then the legacy <c>grantedTo.user</c>.</summary>
    private static IEnumerable<Identity> PermissionUsers(Permission permission)
    {
        if (permission.GrantedToV2?.User is { } v2) yield return v2;
        if (permission.GrantedTo?.User is { } v1) yield return v1;
    }

    /// <summary>The Entra object ids (<c>user.id</c>) a permission names.</summary>
    private static IEnumerable<Guid> PermissionUserObjectIds(Permission permission)
    {
        foreach (var user in PermissionUsers(permission))
        {
            if (Guid.TryParse(user.Id, out var objectId) && objectId != Guid.Empty)
            {
                yield return objectId;
            }
        }
    }

    /// <summary>The addresses (<c>user.email</c> and <c>user.userPrincipalName</c>) a permission names.</summary>
    private static IEnumerable<string> PermissionUserAddresses(Permission permission)
    {
        foreach (var user in PermissionUsers(permission))
        {
            if (user.AdditionalData is not { } data) continue;

            foreach (var field in new[] { "email", "userPrincipalName" })
            {
                if (data.TryGetValue(field, out var value) && value is string address && !string.IsNullOrWhiteSpace(address))
                {
                    yield return address;
                }
            }
        }
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

/// <summary>
/// The outcome of <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/>: one result per distinct key, and what
/// the one paged read established about the container as a whole.
/// </summary>
/// <param name="Results">One <see cref="SpeContainerMembershipResult"/> per distinct key.</param>
/// <param name="ContactPermissionState">#2104 round 2 (F1), three-state since round 4: whether, after this call, the
/// container holds a permission a contact grant could account for — <see cref="SpeContainerContactPermissionState"/>.</param>
public sealed record SpeMembershipRemoval(
    IReadOnlyDictionary<SpeMembershipKey, SpeContainerMembershipResult> Results,
    SpeContainerContactPermissionState ContactPermissionState);

/// <summary>
/// #2104 round 4 (F1): what <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> established about the
/// container as a whole — whether any permission is left that a contact grant could account for. Three states, because
/// "one is left" and "could not tell" call for different answers: a retry cannot change the first, and can the second.
/// </summary>
public enum SpeContainerContactPermissionState
{
    /// <summary>Not established: the read faulted or was incomplete, the markers or the internal-user lookup could not be
    /// read, a contact's delete failed, or a remaining permission was not checked. A retry may answer it. The default, so
    /// an unset value never reads as clean.</summary>
    Unknown = 0,

    /// <summary>The collection was read to its end and every user permission on it is Spaarke-managed (marker-owned), was
    /// deleted by this call, or is held by an internal user (a <c>systemuser</c> of this environment) — so no permission
    /// is left that a contact grant could account for, and every revoked grantee, whoever they are, is absent.</summary>
    Clean = 1,

    /// <summary>A permission that could be a contact's is definitely left, and re-reading would find it again: a holder
    /// checked and not an internal user (or naming no object id, so it cannot be checked), or a key's ambiguous or
    /// unverifiable match (<see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/>). Only an administrator
    /// changes it. Wins over <see cref="Unknown"/>: a fault elsewhere cannot make such a permission go away.</summary>
    ContactPermissionRemains = 2,
}

/// <summary>
/// #2105: the identity a contact's container permission is matched by (<see cref="SpeContainerMembershipService.FindContactPermissions"/>)
/// — the contact's bound Entra object id, its email, or both. Empty when it has neither, which is
/// <c>RevokeExternalAccessEndpoint.HasNoContainerMembershipKey</c>'s "nothing to clear".
/// </summary>
/// <remarks>
/// A blank email and an all-zero object id are normalised to <see langword="null"/>; an email is trimmed. Equality compares the email
/// case-insensitively (addresses are), so keys work as dictionary keys for <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/>'s results.
/// </remarks>
public sealed record SpeMembershipKey
{
    public SpeMembershipKey(Guid? objectId, string? email)
    {
        ObjectId = objectId is { } oid && oid != Guid.Empty ? oid : null;
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    /// <summary>The contact's bound Entra object id (<c>contact.sprk_externalobjectid</c>), when it has a readable one.</summary>
    public Guid? ObjectId { get; }

    /// <summary>The contact's <c>emailaddress1</c>, when it has one.</summary>
    public string? Email { get; }

    /// <summary>Neither key: no container permission can be attributed to this contact.</summary>
    public bool IsEmpty => ObjectId is null && Email is null;

    public bool Equals(SpeMembershipKey? other) =>
        other is not null
        && ObjectId == other.ObjectId
        && string.Equals(Email, other.Email, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(ObjectId, Email is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Email));

    /// <summary>
    /// Log form (#2111): the object id, and only WHETHER an email key is present — never the address. This text goes into
    /// <see cref="SpeContainerMembershipService"/>'s log lines and into the result <c>Error</c> text the revoke and the
    /// closure log, so it carries no PII (CODE-QUALITY-RUBRIC D9). Callers log the contact id beside it.
    /// </summary>
    public override string ToString() => (ObjectId, Email) switch
    {
        ({ } oid, not null) => $"oid {oid} (email key set)",
        ({ } oid, null) => $"oid {oid}",
        (null, not null) => "email key only (no bound oid)",
        _ => "(no key)",
    };
}
