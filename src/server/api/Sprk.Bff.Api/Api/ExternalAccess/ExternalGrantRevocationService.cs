using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>What <see cref="ExternalGrantRevocationService.RevokeAsync"/> did.</summary>
internal enum GrantRevocationStatus
{
    /// <summary>The grant's rows were swept; the SPE cleanup, the cache invalidation and the Assigned-To marking ran.</summary>
    Revoked,

    /// <summary>The access record does not exist. Nothing was changed.</summary>
    NotFound,

    /// <summary>The access record names no root or no grantee, so its sibling rows cannot be found. Nothing was changed.</summary>
    NoGrantKey,

    /// <summary>Reading or deactivating the grant's rows failed. Some rows may remain active.</summary>
    DeactivationFailed,
}

/// <summary>
/// The outcome of one grant revoke. Only <see cref="GrantRevocationStatus.Revoked"/> carries the counts and the SPE
/// outcome; the caller decides the response (<see cref="RevokeExternalAccessEndpoint"/>: a 200, or the M2 500 when the
/// container permission could not be confirmed removed).
/// </summary>
internal sealed record GrantRevocation(
    GrantRevocationStatus Status,
    int DeactivatedCount = 0,
    SpeContainerRevokeOutcome SpeOutcome = SpeContainerRevokeOutcome.NotAttempted,
    SpeOrgMemberCleanupSummary? OrgCleanup = null,
    bool OrganizationTooLarge = false,
    int PermissionsAmbiguous = 0,
    IReadOnlyList<string>? ResidualAccessTerms = null)
{
    internal static GrantRevocation Refused(GrantRevocationStatus status) => new(status);
}

/// <summary>
/// The ONE revoke of an external grant: deactivate every active row of the logical grant, remove the revoked grantee's
/// SPE container permission(s), invalidate the grant cache and mark an Assigned-To auto grant revoked. Moved unchanged from
/// <see cref="RevokeExternalAccessEndpoint"/> (slice C1), which is now a thin caller that turns the outcome into a
/// response, so every surface that revokes a grant runs the same steps.
/// </summary>
/// <remarks>
/// <para><b>CLAUDE.md §11.</b></para>
/// <list type="number">
/// <item><b>Existing:</b> the revoke steps inside <see cref="RevokeExternalAccessEndpoint.RevokeAccessAsync"/>; project
/// closure shares only the identity key (<see cref="RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync"/>).</item>
/// <item><b>Extension:</b> it is that code moved, not new behaviour; the endpoint builds it from the dependencies it already
/// injects and calls it.</item>
/// <item><b>Cost of doing nothing:</b> a second surface that removes a person's grants would have to copy the sweep, the
/// SPE cleanup, the cache invalidation and the ledger marking, and the copies would drift.</item>
/// </list>
/// </remarks>
internal sealed class ExternalGrantRevocationService
{
    private readonly DataverseWebApiClient _dataverse;
    private readonly SpeContainerMembershipService _speContainerMembership;
    private readonly ExternalParticipationService _participations;
    private readonly AssignedAccessMaterializer _assignedAccess;
    private readonly RecordContainerResolver _containerResolver;
    private readonly TimeProvider _time;
    private readonly IContactIdentityStore? _identityStore;
    private readonly ILogger _logger;

    /// <param name="identityStore">
    /// The identity binding store for THIS environment: a contact whose binding read EMPTY and who has no email has
    /// "nothing to clear" only while the binding columns' read is proven. Without it that case fails closed.
    /// </param>
    public ExternalGrantRevocationService(
        DataverseWebApiClient dataverse,
        SpeContainerMembershipService speContainerMembership,
        ExternalParticipationService participations,
        AssignedAccessMaterializer assignedAccess,
        RecordContainerResolver containerResolver,
        TimeProvider time,
        IContactIdentityStore? identityStore,
        ILogger logger)
    {
        _dataverse = dataverse;
        _speContainerMembership = speContainerMembership;
        _participations = participations;
        _assignedAccess = assignedAccess;
        _containerResolver = containerResolver;
        _time = time;
        _identityStore = identityStore;
        _logger = logger;
    }

    /// <summary>
    /// Revokes the logical grant (root x grantee) that <paramref name="accessRecordId"/> belongs to. Never reports
    /// <see cref="GrantRevocationStatus.Revoked"/> while a row of the grant may remain active.
    /// </summary>
    public async Task<GrantRevocation> RevokeAsync(Guid accessRecordId, CancellationToken ct)
    {
        // ── Step 1: Deactivate EVERY active row for this logical grant ───────
        //
        // Revoke used to deactivate exactly ONE row, by AccessRecordId (finding A-11). Because /grant
        // created unconditionally, two identical grants produced two active rows — so revoking "the"
        // grant left a sibling standing and access survived revocation. The participation surface could
        // not reveal it either: QueryGrantSetAsync collapses duplicates with GroupBy(root).Max(level) and
        // never returns access-record ids.
        //
        // The revocation target now identifies a LOGICAL grant (root × grantee), and every active row on
        // that key is deactivated. Task 010 / spec FR-09.
        int deactivatedCount;
        // Hoisted out of the try (task 020): the SPE step needs to know WHICH grantee this grant names.
        // An organization grant confers access on every active member, so its container cleanup is a
        // many-identity sweep — and the organization id lives on the row, not on the request.
        ExternalGrantKey grantKey;
        try
        {
            var targetRow = await ExternalGrantLifecycle.RetrieveRowAsync(_dataverse, accessRecordId, ct);

            if (targetRow is null)
            {
                return GrantRevocation.Refused(GrantRevocationStatus.NotFound);
            }

            var key = ExternalGrantLifecycle.DeriveKey(targetRow);

            if (key is null)
            {
                // FAIL LOUDLY. Per this task's ADR-003 constraint, /revoke must never report success
                // while any matching active row remains unqueried — and a row with no derivable root or
                // grantee has no queryable siblings. Deactivating only the target would be precisely the
                // silent partial revocation A-11 describes, so refusing is the fail-closed answer.
                _logger.LogError(
                    "[EXT-REVOKE] Access record {AccessRecordId} has no derivable grant key (no root " +
                    "and/or no grantee lookup). Refusing to revoke: sibling rows cannot be identified, so " +
                    "success cannot be guaranteed.", accessRecordId);

                return GrantRevocation.Refused(GrantRevocationStatus.NoGrantKey);
            }

            grantKey = key.Value;

            // Sweep by KEY, not by id — this is what makes the revoke complete. Note the target row is
            // swept too when active, and that an ALREADY-INACTIVE target still sweeps live siblings:
            // "the row you named is already off" is not the same as "this grant confers nothing".
            var activeRows = await ExternalGrantLifecycle.QueryActiveRowsAsync(_dataverse, key.Value, ct);

            deactivatedCount = await ExternalGrantLifecycle.DeactivateAsync(
                _dataverse, activeRows.Select(r => r.Id), _logger, ct);

            _logger.LogInformation(
                "[EXT-REVOKE] Revoked grant {Key}: deactivated {Count} active row(s) (target {AccessRecordId}).",
                key.Value, deactivatedCount, accessRecordId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return GrantRevocation.Refused(GrantRevocationStatus.NotFound);
        }
        catch (Exception ex)
        {
            // Covers the sibling-row query AND any partial sweep. Reporting success here would be the
            // worst outcome available: the caller believes access is gone while rows remain active.
            _logger.LogError(ex,
                "[EXT-REVOKE] Failed to revoke grant for access record {AccessRecordId}. Some rows may " +
                "remain active.", accessRecordId);
            return GrantRevocation.Refused(GrantRevocationStatus.DeactivationFailed);
        }

        // ── Step 2: Remove the revoked grantee's SPE container permission(s) ──
        var (speOutcome, orgCleanup, organizationTooLarge, permissionsAmbiguous) = await RemoveSpeContainerPermissionsAsync(
            accessRecordId, grantKey, ExternalGrantLifecycle.TodayUtc(_time), ct);

        // ── Step 3: Invalidate the grant cache — the ONE routine (task 137) ───
        // Keyed on the REVOKED ROW's grantee (the same key Step 1 swept), not on request.ContactId: a contact grant
        // revoked without a ContactId on the request is still invalidated, and an organization grant expands to
        // every ACTIVE member. Every tenant a grant set can be cached under is cleared — the CIAM one too, which a
        // removal under the admin's own tid alone used to miss for the 60-second TTL. Non-fatal by construction:
        // the routine never throws, so a failed invalidation cannot change this response.
        await _participations.InvalidateGrantSetsAsync(
            grantKey.ContactId is { } revokedContact ? new[] { revokedContact } : Array.Empty<Guid>(),
            grantKey.IsOrganizationGrant && grantKey.OrganizationId is { } revokedOrganization
                ? new[] { revokedOrganization }
                : Array.Empty<Guid>(),
            CancellationToken.None);

        // ── Task 142: an operator removal of an Assigned-To auto grant STICKS (owner round 2 item 5) ──
        // The subject's ledger rows on this record become Declined, so no trigger re-creates the grant while the
        // assignment persists (a manual /grant afterwards still succeeds). Ledger-only and never thrown: the revoke above
        // stands whatever happens here, and a lost marker is repaired by the next pass's out-of-band rule. For a CONTACT
        // auto grant the response also names the read-time terms that still bring it to the record (owner A2: standing
        // and organization access stay) — criterion 17: never "removed" while access silently remains.
        var residualAccessTerms = await _assignedAccess.MarkGrantRevokedAsync(
            grantKey.RootType, grantKey.RootId, grantKey.ContactId,
            grantKey.IsOrganizationGrant ? grantKey.OrganizationId : null, CancellationToken.None);

        return new GrantRevocation(
            GrantRevocationStatus.Revoked,
            deactivatedCount,
            speOutcome,
            orgCleanup,
            organizationTooLarge,
            permissionsAmbiguous,
            residualAccessTerms);
    }

    /// <summary>
    /// Removes the revoked Contact's SPE container permission, and reports honestly what happened.
    /// </summary>
    /// <remarks>
    /// <para><b>Finding A-13 (task 017).</b> This used to be a private re-implementation of
    /// <see cref="SpeContainerMembershipService.RevokeMembershipAsync"/> that matched a permission by
    /// looking for the contact's <b>GUID</b> inside <c>userPrincipalName</c>. But membership is written
    /// with <c>userPrincipalName</c> = the contact's <b>email</b>, and an email never contains a GUID — so
    /// the predicate matched nothing, ever. It then returned <c>true</c> on no-match ("may have already
    /// been removed"), so <c>/revoke</c> reported SPE success while the ACL entry stayed in place.</para>
    ///
    /// <para>The fix is deletion, not repair: the service already had a correct email matcher with zero
    /// callers (CLAUDE.md §11 — reuse, don't fork). What remains here is the endpoint's own job — turning
    /// a contact id into its identity key (#2105: bound object id and email, <see cref="RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync"/>),
    /// and mapping the result to an honest outcome.</para>
    ///
    /// <para><b>Broker-only context.</b> Nothing in this codebase ADDS a container permission
    /// (<c>GrantMembershipAsync</c> has no callers; <c>/grant</c> reports
    /// <c>SpeContainerMembershipGranted: false</c>). So this is a CLEANUP path for ACLs created by legacy
    /// versions or by admins outside Spaarke — not the counterpart of a grant-time write. That is why
    /// <see cref="SpeContainerRevokeOutcome.NoPermissionFound"/> is the ordinary, healthy answer rather
    /// than a problem.</para>
    ///
    /// <para><b>WHICH container — task 166 (route-authorization sweep amendment (b)).</b> It used to be
    /// <c>request.ContainerId</c>, a client-chosen value, while <c>DelegationRuleFilter</c> authorized the grant
    /// row's ROOT — the same "authorized against one id, mutates another" shape as close-project (S-39). The field
    /// is deleted and the container is derived from the grant's root through
    /// <see cref="ProjectClosureEndpoint.DeriveRecordOwnContainerAsync"/>: a SECURE root's own container is cleaned;
    /// a non-secure root's derived container is the SHARED business-unit container, which a single revoke must not
    /// sweep (the grantee may hold other grants it serves), so that case is <c>NotAttempted</c>; an undecidable
    /// container is <c>Failed</c> (→ the M2 500). Note the shipped Manage Access modal never sent a container, so
    /// until this change secure-root revokes never cleaned up SPE at all.</para>
    ///
    /// <para><b>Overlap — task 166 r1.</b> Permissions are keyed by IDENTITY, not by grant, so one person can hold the
    /// same container permission on the strength of two grants on this root (a direct contact grant AND membership of
    /// an organization granted the same root). Revoking one of them must not strip a permission the other still
    /// justifies. So before removing, the root's REMAINING active, unexpired grants are read and every contact they
    /// still entitle (direct grantees, and the active members of granted organizations) is kept. If that answer
    /// cannot be established (a read fault, or more rows than one revoke may read) the removal proceeds for everyone
    /// the revoked grant named — fail closed (ADR-003): an entitlement that cannot be verified is not honoured.</para>
    /// </remarks>
    private async Task<(SpeContainerRevokeOutcome Outcome, SpeOrgMemberCleanupSummary? OrgCleanup, bool OrganizationTooLarge, int PermissionsAmbiguous)>
        RemoveSpeContainerPermissionsAsync(
            Guid accessRecordId,
            ExternalGrantKey grantKey,
            DateOnly today,
            CancellationToken ct)
    {
        var (derivedContainerId, decided) = await ProjectClosureEndpoint.DeriveRecordOwnContainerAsync(
            _containerResolver, ExternalGrantRoot.LogicalNameFor(grantKey.RootType), grantKey.RootId, _logger, ct);

        if (!decided)
        {
            _logger.LogError(
                "[EXT-REVOKE] The container of grant root {RootType} {RootId} could not be determined; the SPE "
                + "permission cleanup cannot run and the grantee may RETAIN file access.",
                grantKey.RootType, grantKey.RootId);
            return (SpeContainerRevokeOutcome.Failed, grantKey.IsOrganizationGrant ? UnknownMembership : null, false, 0);
        }

        if (derivedContainerId is null)
        {
            _logger.LogInformation(
                "[EXT-REVOKE] Grant root {RootType} {RootId} is not a secure record with its own container — no "
                + "record-owned container permission to remove.", grantKey.RootType, grantKey.RootId);
            return (SpeContainerRevokeOutcome.NotAttempted, null, false, 0);
        }

        var containerId = derivedContainerId;

        // ── Task 166 r1: who does ANOTHER live grant on this root still entitle? ──
        // null = could not be established; the removal then proceeds for everyone (fail closed).
        var stillEntitled = await ResolveStillEntitledContactsAsync(
            _dataverse, grantKey.RootType, grantKey.RootId, today, _logger, ct);

        // ── Which grantee? The ROW decides, not the request ───────────────────
        //
        // Task 020 (FR-16b). Dispatching on the derived grant KEY rather than on request.ContactId is
        // deliberate: the row is what the Dataverse sweep in Step 1 acted on, so keying the SPE cleanup
        // off anything else would let the two halves of a revoke disagree about who was revoked — which
        // is finding A-11's shape, one layer down. It also means an org revoke is recognised as one even
        // if a caller supplies some incidental ContactId.
        if (grantKey.IsOrganizationGrant)
        {
            // Unreachable by construction — ExternalGrantKey.ForOrganization requires an organization id,
            // and IsOrganizationGrant is exactly "no contact". Handled rather than asserted with `!`
            // because the fail-closed answer to "an org grant with no organization" is "we cannot
            // identify the members", not a silent fall-through to the single-contact path, which would
            // clean up nobody while reporting an outcome.
            if (grantKey.OrganizationId is not { } organizationId)
            {
                _logger.LogError(
                    "[EXT-REVOKE] Access record {AccessRecordId} derives an organization grant with no " +
                    "organization id. Members cannot be identified; no SPE container permission on " +
                    "{ContainerId} was removed.", accessRecordId, containerId);
                return (SpeContainerRevokeOutcome.Failed, UnknownMembership, false, 0);
            }

            return await RemoveOrganizationMembersSpePermissionsAsync(
                containerId, organizationId, stillEntitled, ct);
        }

        // A CONTACT grant: the ROW's contact is the one revoked (pass-1 K3), never request.ContactId — a request naming
        // nobody, or a different contact, must not change whose permission is cleared. Unreachable when null:
        // IsOrganizationGrant is exactly "no contact". Handled fail closed (Failed), never as a silent NotAttempted.
        if (grantKey.ContactId is not { } contactId)
        {
            _logger.LogError(
                "[EXT-REVOKE] Access record {AccessRecordId} derives a contact grant with no contact. The grantee "
                + "cannot be identified; no SPE container permission on {ContainerId} was removed.",
                accessRecordId, containerId);
            return (SpeContainerRevokeOutcome.Failed, null, false, 0);
        }

        if (stillEntitled is not null && stillEntitled.Contains(contactId))
        {
            _logger.LogInformation(
                "[EXT-REVOKE] Contact {ContactId} is still entitled to {RootType} {RootId} through another active grant "
                + "on it; their container permission on {ContainerId} is KEPT (task 166).",
                contactId, grantKey.RootType, grantKey.RootId, containerId);
            return (SpeContainerRevokeOutcome.NotAttempted, null, false, 0);
        }

        var (contactOutcome, contactAmbiguous) = await RemoveContactSpePermissionAsync(
            contactId, containerId, ct);
        return (contactOutcome, null, false, contactAmbiguous ? 1 : 0);
    }

    /// <summary>The most remaining grant rows on one root a single revoke reads for the overlap check.</summary>
    internal const int MaxRemainingGrantsPerOverlapCheck = 200;

    /// <summary>
    /// Task 166 r1: the contacts that the root's REMAINING active, unexpired grants still entitle — each direct
    /// contact grantee, and every active member of each granted organization — or <see langword="null"/> when that
    /// set cannot be established (a read fault, more than <see cref="MaxRemainingGrantsPerOverlapCheck"/> rows, or an
    /// organization whose membership cannot be read or exceeds its sweep bound).
    /// </summary>
    /// <remarks>
    /// Runs AFTER Step 1, so the revoked grant's own rows are already inactive and are not counted. Expiry is applied
    /// in memory with <see cref="ExternalParticipationService.ConfersAccessOn"/>, the one mirror of the read side's
    /// predicate, so a lapsed grant keeps no one's permission. Organization membership is the statecode-active set the
    /// revoke sweep itself uses (<see cref="ExternalOrganizationMembership"/>). <c>internal</c> for the tests.
    /// </remarks>
    internal static async Task<IReadOnlySet<Guid>?> ResolveStillEntitledContactsAsync(
        DataverseWebApiClient dataverseClient,
        ExternalGrantRootType rootType,
        Guid rootId,
        DateOnly today,
        ILogger logger,
        CancellationToken ct)
    {
        List<ExternalGrantRow> remaining;
        try
        {
            remaining = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                dataverseClient, rootType, rootId, MaxRemainingGrantsPerOverlapCheck + 1, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Could not read the remaining grants on {RootType} {RootId}; no permission is kept on the "
                + "strength of another grant (fail closed).", rootType, rootId);
            return null;
        }

        if (remaining.Count > MaxRemainingGrantsPerOverlapCheck)
        {
            logger.LogWarning(
                "[EXT-REVOKE] {RootType} {RootId} has more than {Bound} remaining active grants; the overlap check "
                + "cannot be completed, so no permission is kept on the strength of another grant (fail closed).",
                rootType, rootId, MaxRemainingGrantsPerOverlapCheck);
            return null;
        }

        var entitled = new HashSet<Guid>();
        foreach (var row in remaining)
        {
            if (!ExternalParticipationService.ConfersAccessOn(row.ExpiresDate, today))
            {
                continue;
            }

            if (row.ContactId is { } contactId)
            {
                entitled.Add(contactId);
                continue;
            }

            if (row.OrganizationId is not { } organizationId)
            {
                continue;
            }

            OrganizationMemberSet members;
            try
            {
                members = await ExternalOrganizationMembership.QueryActiveMembersAsync(dataverseClient, organizationId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "[EXT-REVOKE] Could not read the members of organization {OrganizationId}, which still holds a grant "
                    + "on {RootType} {RootId}; the overlap check fails closed.", organizationId, rootType, rootId);
                return null;
            }

            if (members.ExceededBound)
            {
                logger.LogWarning(
                    "[EXT-REVOKE] Organization {OrganizationId} (still granted {RootType} {RootId}) exceeds the member "
                    + "sweep bound; the overlap check fails closed.", organizationId, rootType, rootId);
                return null;
            }

            entitled.UnionWith(members.ContactIds);
        }

        return entitled;
    }

    /// <summary>
    /// The per-CONTACT half of the SPE cleanup: one identity, one permission. <paramref name="contactId"/> is the
    /// revoked ROW's contact (pass-1 K3), never the request's. <c>Ambiguous</c> (#2104 round 3): the Failed outcome is
    /// one a retry cannot fix (<see cref="SpeContainerMembershipService.IsAmbiguousPermissionMatch"/>).
    /// </summary>
    private async Task<(SpeContainerRevokeOutcome Outcome, bool Ambiguous)> RemoveContactSpePermissionAsync(
        Guid contactId,
        string containerId,
        CancellationToken ct)
    {
        SpeMembershipKey contactKey;
        try
        {
            contactKey = await RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync(_dataverse, _identityStore, contactId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-REVOKE] Could not read the identity key (object id / email) of Contact {ContactId}; the SPE " +
                "container permission on {ContainerId} could NOT be matched and may remain.",
                contactId, containerId);
            return (SpeContainerRevokeOutcome.Failed, false);
        }

        if (RevokeExternalAccessEndpoint.HasNoContainerMembershipKey(contactKey))
        {
            // #2103: the shared decision (see RevokeExternalAccessEndpoint.HasNoContainerMembershipKey) — nothing on the container can be this
            // contact's, so there is nothing to clear. It used to be Failed → a 500 telling the caller to retry, which
            // could never succeed: a missing key is permanent.
            _logger.LogInformation(
                "[EXT-REVOKE] Contact {ContactId} has neither a bound object id nor an emailaddress1 — the keys a " +
                "container permission is matched by — so no permission on {ContainerId} can be attributed to them; " +
                "container step {Outcome} (#2103).",
                contactId, containerId, RevokeExternalAccessEndpoint.NoContainerMembershipKeyOutcome);
            return (RevokeExternalAccessEndpoint.NoContainerMembershipKeyOutcome, false);
        }

        var result = await _speContainerMembership.RevokeMembershipAsync(containerId, contactKey, _dataverse, ct);

        if (result.Success)
        {
            _logger.LogInformation(
                "[EXT-REVOKE] Removed SPE container permission {PermissionId} for Contact {ContactId} on {ContainerId}",
                result.PermissionId, contactId, containerId);
            return (SpeContainerRevokeOutcome.PermissionRemoved, false);
        }

        // The service distinguishes "nobody matched" from "Graph refused". Only the former is benign:
        // under the broker-only model most contacts have no container ACL at all.
        if (result.Error?.StartsWith(SpeContainerMembershipService.NoPermissionFoundError, StringComparison.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation(
                "[EXT-REVOKE] No SPE container permission exists for Contact {ContactId} on {ContainerId} " +
                "— nothing to remove (expected under the broker-only model).",
                contactId, containerId);
            return (SpeContainerRevokeOutcome.NoPermissionFound, false);
        }

        _logger.LogError(
            "[EXT-REVOKE] Failed to remove the SPE container permission for Contact {ContactId} on " +
            "{ContainerId}: {Error}. They may RETAIN file access.",
            contactId, containerId, result.Error);
        return (SpeContainerRevokeOutcome.Failed, SpeContainerMembershipService.IsAmbiguousPermissionMatch(result));
    }

    /// <summary>
    /// The per-ORGANIZATION half of the SPE cleanup: expand the organization to its ACTIVE member
    /// contacts and remove each one's container permission, reporting per-member arithmetic.
    /// </summary>
    /// <remarks>
    /// <para><b>Finding A-13's org half (task 020, spec FR-16b), filed by task 017 §6.</b> An org revoke
    /// deactivates the grant for every member in Dataverse, but attempted NO container cleanup at all —
    /// there is no single grantee, so no single email, so nothing to match. It returned
    /// <c>NotAttempted</c>: honest, but it left every member's ACL entry in place for a grant the caller
    /// had been told was revoked.</para>
    ///
    /// <para><b>Why sweeping by <c>statecode</c> alone.</b> The junction also carries
    /// <c>sprk_enddate</c> and <c>sprk_startdate</c>, and this ignores both — deliberately. Since task 109
    /// the read side (<c>ExternalParticipationService.ReadOrganizationMembershipsAsync</c>) confers
    /// inherited access only through memberships that are date-current under an active organization, a
    /// SUBSET of the <c>statecode</c>-active rows swept here. So this sweep is a superset of everyone the
    /// read side can still let in: over-including on a revoke removes more access (fail-closed);
    /// under-including does not. (Before task 109 the read side conferred on <c>statecode</c> alone, and
    /// this sweep matched it exactly; the superset relation is what has to hold, and still does.)</para>
    ///
    /// <para><b>What a per-member <c>NoPermissionFound</c> rests on.</b> It is only as good as the one paged read
    /// <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> makes. That read follows Graph's
    /// <c>@odata.nextLink</c> (task 024) up to its page bound and reports whether it reached the end; a member not
    /// seen on an INCOMPLETE read is a failure, never "holds no permission". The match itself is the service's one
    /// matcher (<see cref="SpeContainerMembershipService.FindContactPermissions"/>, #2105 / #2104 round 2): the bound
    /// object id, then the email against the permission's <c>email</c> and <c>userPrincipalName</c>; never a
    /// Spaarke-managed or an internal user's permission; and never a guess — two eligible matches, or a match whose
    /// holder cannot be verified, remove nothing and are counted Failed, which the response names as
    /// <see cref="RevokeExternalAccessEndpoint.RevokePermissionAmbiguousReason"/> (#2104 round 3).</para>
    ///
    /// <para><b>An organization over the sweep bound (#2104 round 3)</b> is not swept member by member; the container is
    /// asked whether it holds any contact permission at all (<see cref="ContainerContactPermissionStateAsync"/>). None: a
    /// 200. One definitely left: <see cref="RevokeExternalAccessEndpoint.RevokeOrganizationTooLargeReason"/> (a retry meets the same bound). Not
    /// established (round 4): the retryable <see cref="RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason"/> — a retry may prove it clean.</para>
    /// </remarks>
    private async Task<(SpeContainerRevokeOutcome Outcome, SpeOrgMemberCleanupSummary? OrgCleanup, bool OrganizationTooLarge, int PermissionsAmbiguous)>
        RemoveOrganizationMembersSpePermissionsAsync(
            string containerId,
            Guid organizationId,
            IReadOnlySet<Guid>? stillEntitled,
            CancellationToken ct)
    {
        // ── Enumerate ────────────────────────────────────────────────────────
        OrganizationMemberSet memberSet;
        try
        {
            memberSet = await ExternalOrganizationMembership.QueryActiveMembersAsync(
                _dataverse, organizationId, ct);
        }
        catch (Exception ex)
        {
            // We do not know WHO to clean up, so we cannot claim anything was cleaned up. Attempting a
            // partial sweep off an unknown member list would be worse than doing nothing: it would
            // produce counts that read like a complete answer.
            _logger.LogError(ex,
                "[EXT-REVOKE] Could not enumerate the active members of Organization {OrganizationId}; " +
                "NO SPE container permission on {ContainerId} was removed and members may RETAIN file access.",
                organizationId, containerId);
            return (SpeContainerRevokeOutcome.Failed, UnknownMembership, false, 0);
        }

        if (memberSet.ExceededBound)
        {
            // #2104 round 3: the members cannot be swept one by one, but the container itself can still prove that none
            // of them holds a permission there — the rule close-project applies (round 2, F1): a COMPLETE read on which
            // every user permission is Spaarke-managed or an internal user's (SpeContainerContactPermissionState.Clean).
            // Then nobody, member or not, holds a permission a contact grant could account for: a 200.
            var containerState = await ContainerContactPermissionStateAsync(
                containerId, organizationId, ct);

            if (containerState == SpeContainerContactPermissionState.Clean)
            {
                _logger.LogInformation(
                    "[EXT-REVOKE] Organization {OrganizationId} has more than {Bound} active members, more than one " +
                    "revoke may sweep, but container {ContainerId} holds no permission a contact grant could account for " +
                    "(read in full; every user permission is Spaarke-managed or an internal user's), so no member can hold one there " +
                    "(#2104 round 3).",
                    organizationId, ExternalOrganizationMembership.MaxMembersPerSweep, containerId);
                return (SpeContainerRevokeOutcome.NoPermissionFound, UnknownMembership, false, 0);
            }

            if (containerState == SpeContainerContactPermissionState.Unknown)
            {
                // #2104 round 4 (F1): the container could not answer (a read, marker or lookup fault, an incomplete read, a
                // throw). A retry may prove it clean, so this is the retryable container_not_cleared — never the
                // "retrying will not change this" of organization_too_large.
                _logger.LogError(
                    "[EXT-REVOKE] Organization {OrganizationId} has more than {Bound} active members, and whether container "
                    + "{ContainerId} still holds a contact permission could not be established; members may RETAIN file "
                    + "access. Retry the revoke.",
                    organizationId, ExternalOrganizationMembership.MaxMembersPerSweep, containerId);
                return (SpeContainerRevokeOutcome.Failed, UnknownMembership, false, 0);
            }

            // Task 020's escalation trigger, enforced in code rather than assumed away: a sweep that
            // silently truncates and reports success is the exact failure class this project exists to
            // remove. Refusing, loudly, hands the operator a decision instead of a false assurance.
            _logger.LogError(
                "[EXT-REVOKE] Organization {OrganizationId} has more than {Bound} active members — more " +
                "than one revoke request may sweep. NO SPE container permission on {ContainerId} was " +
                "removed; escalate for a bulk cleanup rather than retrying.",
                organizationId, ExternalOrganizationMembership.MaxMembersPerSweep, containerId);
            // #2103 follow-up: flagged so the response names THIS condition (organization_too_large) instead of the
            // retryable container_not_cleared — a retry meets the same bound.
            return (SpeContainerRevokeOutcome.Failed, UnknownMembership, true, 0);
        }

        // ── Resolve identities ───────────────────────────────────────────────
        // A permission is matched by the member's bound object id, then their email (#2105,
        // SpeContainerMembershipService.FindContactPermissions). Resolve them all first, then do ONE container read.
        //
        // ISS-004 (#968), closed by task 024: this loop used to call RevokeMembershipAsync per member,
        // and each of those calls read the container's ENTIRE permission collection — N members = N
        // full reads. Task 024's paging made that N × pages. The container is now read ONCE.
        var removed = 0;
        var notFound = 0;
        var failed = 0;
        var retained = 0;
        var noKey = 0;
        // #2104 round 3: members whose Failed is an ambiguous or unverifiable match (counted in `failed` too) — the part
        // a retry cannot fix.
        var ambiguous = 0;

        var resolved = new List<(Guid ContactId, SpeMembershipKey Key)>();

        foreach (var memberContactId in memberSet.ContactIds)
        {
            // Task 166 r1: a member another live grant on this root still entitles keeps their permission.
            if (stillEntitled is not null && stillEntitled.Contains(memberContactId))
            {
                retained++;
                _logger.LogInformation(
                    "[EXT-REVOKE] Member Contact {ContactId} of Organization {OrganizationId} is still entitled through "
                    + "another active grant on this root; their permission on {ContainerId} is KEPT.",
                    memberContactId, organizationId, containerId);
                continue;
            }

            // Per-member failure must NOT abort the loop (mirrors tasks 016/017): stopping early leaves
            // strictly MORE access in place. Every member gets an outcome, and the failures are counted.
            try
            {
                var memberKey = await RevokeExternalAccessEndpoint.ResolveContactMembershipKeyAsync(_dataverse, _identityStore, memberContactId, ct);

                if (RevokeExternalAccessEndpoint.HasNoContainerMembershipKey(memberKey))
                {
                    // #2103: the shared decision — no permission can be this member's, so nothing to clear, and
                    // not a failure (it used to be, which made every revoke of this organization a permanent 500).
                    noKey++;
                    _logger.LogInformation(
                        "[EXT-REVOKE] Member Contact {ContactId} of Organization {OrganizationId} has neither a bound " +
                        "object id nor an emailaddress1 — the keys a container permission is matched by — so no " +
                        "permission on {ContainerId} can be attributed to them; nothing to clear (#2103).",
                        memberContactId, organizationId, containerId);
                    continue;
                }

                resolved.Add((memberContactId, memberKey));
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "[EXT-REVOKE] Could not resolve the identity key for member Contact {ContactId} of " +
                    "Organization {OrganizationId}; their permission on {ContainerId} cannot be matched " +
                    "and may remain. Continuing with the rest.",
                    memberContactId, organizationId, containerId);
            }
        }

        // ── Sweep: ONE paged read, then match every member against it ────────
        // ⚠️ Behaviour note: results are keyed by IDENTITY KEY, so two contact rows sharing one key both report
        // the outcome of the single permission it names (and the service deletes a permission two keys name only
        // once). Previously the second call found the permission already deleted and reported NoPermissionFound,
        // which read as "this person never had access".
        IReadOnlyDictionary<SpeMembershipKey, SpeContainerMembershipResult> speResults =
            resolved.Count == 0
                ? new Dictionary<SpeMembershipKey, SpeContainerMembershipResult>()
                : (await _speContainerMembership.RemoveMembershipsAsync(
                    containerId, resolved.Select(r => r.Key).ToList(), _dataverse,
                    confirmNoContactPermission: false, ct)).Results;

        foreach (var (memberContactId, memberKey) in resolved)
        {
            if (!speResults.TryGetValue(memberKey, out var result))
            {
                // A member we asked about but got no answer for is an unknown, not an absence.
                failed++;
                _logger.LogError(
                    "[EXT-REVOKE] No SPE outcome was returned for member Contact {ContactId} of " +
                    "Organization {OrganizationId} on {ContainerId}. Treating as FAILED — they may " +
                    "RETAIN file access.",
                    memberContactId, organizationId, containerId);
                continue;
            }

            if (result.Success)
            {
                removed++;
                _logger.LogInformation(
                    "[EXT-REVOKE] Removed SPE container permission {PermissionId} for member Contact " +
                    "{ContactId} of Organization {OrganizationId} on {ContainerId}",
                    result.PermissionId, memberContactId, organizationId, containerId);
            }
            else if (result.Error?.StartsWith(SpeContainerMembershipService.NoPermissionFoundError, StringComparison.OrdinalIgnoreCase) == true)
            {
                notFound++;
            }
            else
            {
                failed++;
                if (SpeContainerMembershipService.IsAmbiguousPermissionMatch(result))
                {
                    ambiguous++;
                }

                _logger.LogError(
                    "[EXT-REVOKE] Failed to remove the SPE container permission for member Contact " +
                    "{ContactId} of Organization {OrganizationId} on {ContainerId}: {Error}. They may " +
                    "RETAIN file access. Continuing with the rest.",
                    memberContactId, organizationId, containerId, result.Error);
            }
        }

        var summary = new SpeOrgMemberCleanupSummary(
            MembersEnumerated: memberSet.ContactIds.Count,
            PermissionsRemoved: removed,
            PermissionsNotFound: notFound,
            Failed: failed,
            RetainedByOtherGrant: retained,
            NoMembershipKey: noKey);

        var outcome = RevokeExternalAccessEndpoint.AggregateOrgOutcome(summary);

        _logger.LogInformation(
            "[EXT-REVOKE] Organization {OrganizationId} SPE cleanup on {ContainerId}: {Members} active " +
            "member(s), {Removed} permission(s) removed, {NotFound} with none, {NoKey} with no identity key, " +
            "{Failed} FAILED → {Outcome}",
            organizationId, containerId, summary.MembersEnumerated, removed, notFound, noKey, failed, outcome);

        return (outcome, summary, false, ambiguous);
    }

    /// <summary>
    /// #2104 round 3: does <paramref name="containerId"/> hold any permission a contact grant could account for? The
    /// container-wide answer <see cref="SpeContainerMembershipService.RemoveMembershipsAsync"/> gives with no key and
    /// <c>confirmNoContactPermission</c> (<see cref="SpeMembershipRemoval.ContactPermissionState"/>). Three-state since
    /// round 4 (F1): a throw is <see cref="SpeContainerContactPermissionState.Unknown"/> — never clean, and never "one
    /// remains" either, which would tell the caller a retry cannot help. Removes nothing (there is no key).
    /// </summary>
    private async Task<SpeContainerContactPermissionState> ContainerContactPermissionStateAsync(
        string containerId,
        Guid organizationId,
        CancellationToken ct)
    {
        try
        {
            var removal = await _speContainerMembership.RemoveMembershipsAsync(
                containerId, Array.Empty<SpeMembershipKey>(), _dataverse, confirmNoContactPermission: true, ct);
            return removal.ContactPermissionState;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EXT-REVOKE] Could not establish whether container {ContainerId} holds any contact permission; the "
                + "oversized organization {OrganizationId}'s members are not confirmed absent (fail closed).",
                containerId, organizationId);
            return SpeContainerContactPermissionState.Unknown;
        }
    }

    /// <summary>
    /// The summary reported when the member list could not be established at all. Distinguished from
    /// "the organization has no members" by <c>MembersEnumerated == null</c>, which the counts cannot say.
    /// </summary>
    private static SpeOrgMemberCleanupSummary UnknownMembership =>
        new(MembersEnumerated: null, PermissionsRemoved: 0, PermissionsNotFound: 0, Failed: 0);
}
