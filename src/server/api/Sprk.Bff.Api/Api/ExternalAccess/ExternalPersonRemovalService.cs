using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>What <see cref="ExternalPersonRemovalService"/> did.</summary>
internal enum PersonAdministrationStatus
{
    /// <summary>Every step completed.</summary>
    Completed,

    /// <summary>At least one step did not complete; the steps say which. The call is idempotent: retry it.</summary>
    Incomplete,

    /// <summary>The contact does not exist. Nothing was written.</summary>
    NotFound,

    /// <summary>The contact could not be read. Nothing was written.</summary>
    ContactUnreadable,
}

/// <summary>The outcome of one removal or reinstatement.</summary>
/// <param name="StateCodeAtStart">The contact's statecode when the call read it (audit only; neither call changes it).</param>
internal sealed record PersonAdministrationOutcome(
    PersonAdministrationStatus Status,
    int? StateCodeAtStart,
    IReadOnlyList<ExternalPersonStepResult> Steps,
    int GrantsRevoked = 0,
    IReadOnlyList<Guid>? GrantsNotCompleted = null,
    bool MarkerCleared = false)
{
    internal static PersonAdministrationOutcome Refused(PersonAdministrationStatus status)
        => new(status, null, Array.Empty<ExternalPersonStepResult>());
}

/// <summary>
/// An administrator's removal of a person's external access, and its reinstatement. The removed marker
/// (<c>sprk_externalaccessremovedon</c>) alone refuses the person everywhere, whatever the contact's statecode, so the
/// marker is written FIRST and cleared only by an explicit reinstatement. Neither operation changes the contact's state: it
/// is an ordinary business contact used across the platform.
/// </summary>
/// <remarks>
/// <para><b>Removal, in order</b> (a partial failure leaves the person refused, never half-removed):</para>
/// <list type="number">
/// <item>the removed marker (kept as it is when it is already set);</item>
/// <item>every active grant the contact holds as its grantee, revoked through the ONE revoke
/// (<see cref="ExternalGrantRevocationService"/>: the row sweep, the SPE container cleanup, the grant-cache invalidation for
/// the root, the Assigned-To marking);</item>
/// <item>the person's own container permission on every secure root an organization they belong to is granted
/// (<see cref="ExternalGrantRevocationService.RemovePersonOrganizationGrantPermissionsAsync"/>; the organization grants and
/// the other members are untouched);</item>
/// <item>cache eviction: the contact's grant sets, and the cached identity of every systemuser that links the contact
/// (<see cref="IMembershipCacheInvalidator.InvalidateUserAccessAsync"/>);</item>
/// <item>the invite state and the access permission baseline cleared, so no outstanding invite link or stamp can bind;</item>
/// <item>the standing grant cleared, in a write of its own (the application user may update it only through its direct
/// membership of the standing-grant administrators profile; a refused write is reported as not completed).</item>
/// </list>
/// <para>The binding (the object id, its plane and its key mirror) is never cleared: the person is still found by their object
/// id and refused, and the same object id can never be keyed onto another contact.</para>
/// <para><b>Reinstatement</b> clears the marker and nothing else: no grant is restored (staff grant again) and no invite is
/// sent. A contact that carries no marker is a no-op.</para>
/// <para><b>Staleness.</b> Composition and binding read the marker live on every request, so the person is refused on their
/// next request once the marker is written (0 seconds); the cache evictions are belt and braces.</para>
/// <para><b>CLAUDE.md §11.</b> Existing: <see cref="ExternalGrantRevocationService"/> revokes one grant; the identity store
/// reads the marker. Extension: this calls that revoke once per grant and adds the three contact writes to the store; it holds
/// no revoke logic of its own. Cost of doing nothing: removing a person would mean revoking each grant by hand, with no marker,
/// so a later grant, invite or Assigned-To pass would give the person access again.</para>
/// </remarks>
internal sealed class ExternalPersonRemovalService
{
    internal const string MarkerStep = "removed_marker";
    internal const string GrantsStep = "grants_revoked";
    internal const string OrganizationGrantPermissionsStep = "organization_grant_permissions_removed";
    internal const string CachesStep = "caches_evicted";
    internal const string InviteStateStep = "invite_state_cleared";
    internal const string StandingGrantStep = "standing_grant_cleared";
    internal const string MarkerClearedStep = "removed_marker_cleared";

    /// <summary>
    /// Slice C2b: the most grants one removal reads. The read asks for one more; when more come back, the rows read are
    /// revoked and the grants step is reported not completed, because more may remain. A retry reads the rest (revoked rows
    /// are no longer active). Below Dataverse's 5000-row page, so the bounded read is never cut short by paging.
    /// </summary>
    internal const int MaxGrantsPerRemoval = 4999;

    private readonly DataverseWebApiClient _dataverse;
    private readonly ExternalGrantRevocationService? _revocation;
    private readonly ExternalParticipationService _participations;
    private readonly IContactIdentityStore _identityStore;
    private readonly IMembershipCacheInvalidator _membershipInvalidator;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <param name="revocation">The ONE revoke; required by <see cref="RemoveAsync"/>, unused (null) for a reinstatement.</param>
    public ExternalPersonRemovalService(
        DataverseWebApiClient dataverse,
        ExternalGrantRevocationService? revocation,
        ExternalParticipationService participations,
        IContactIdentityStore identityStore,
        IMembershipCacheInvalidator membershipInvalidator,
        TimeProvider time,
        ILogger logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _revocation = revocation;
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
        _membershipInvalidator = membershipInvalidator ?? throw new ArgumentNullException(nameof(membershipInvalidator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Removes <paramref name="contactId"/>'s external access (see the class remarks for the order).</summary>
    public async Task<PersonAdministrationOutcome> RemoveAsync(Guid contactId, string? correlationId, CancellationToken ct)
    {
        // A wiring fault, thrown before anything is read or written.
        var revocation = _revocation ?? throw new InvalidOperationException("A removal needs the shared grant revoke.");

        var (found, stateCode) = await ReadContactAsync(contactId, ct).ConfigureAwait(false);
        if (found is not PersonAdministrationStatus.Completed)
            return PersonAdministrationOutcome.Refused(found);

        var steps = new List<ExternalPersonStepResult>();

        // ── 1. The marker FIRST: from here on every path refuses the person, whatever else completes ─────────────────
        var marker = await ReadMarkerAsync(contactId, ct).ConfigureAwait(false);
        if (marker == RemovedMarkerRead.Removed)
        {
            steps.Add(new ExternalPersonStepResult(MarkerStep, true, "The person was already marked as removed."));
        }
        else
        {
            var written = await _identityStore.WriteRemovedMarkerAsync(contactId, _time.GetUtcNow(), ct).ConfigureAwait(false);
            if (written.Status != StoreWriteStatus.Written)
            {
                _logger.LogError(
                    "[EXT-REMOVE-PERSON] The removed marker of contact {ContactId} could not be written ({Status}); nothing else " +
                    "was changed.", contactId, written.Status);
                steps.Add(new ExternalPersonStepResult(MarkerStep, false,
                    "The person could not be marked as removed, so nothing else was changed. Retry the removal."));
                return new PersonAdministrationOutcome(PersonAdministrationStatus.Incomplete, stateCode, steps,
                    GrantsNotCompleted: Array.Empty<Guid>());
            }

            steps.Add(new ExternalPersonStepResult(MarkerStep, true));
        }

        // ── 2. Every active grant the person holds, through the ONE revoke ───────────────────────────────────────────
        var (revoked, notCompleted, grantsStep) = await RevokeGrantsAsync(revocation, contactId, ct).ConfigureAwait(false);
        steps.Add(grantsStep);

        // ── 2b. Slice C2c: the person's own permission on the containers of their organizations' grants ─────────────
        steps.Add(await RemoveOrganizationGrantPermissionsAsync(revocation, contactId, ct).ConfigureAwait(false));

        // ── 3. Cache eviction (the marker is read live, so this is belt and braces) ────────────────────────────────
        steps.Add(await EvictCachesAsync(contactId, correlationId).ConfigureAwait(false));

        // ── 4. The invite state and the access permission baseline ───────────────────────────────────────────────────
        var cleared = await _identityStore.ClearRemovedPersonAccessStateAsync(contactId, ct).ConfigureAwait(false);
        steps.Add(cleared.Status == StoreWriteStatus.Written
            ? new ExternalPersonStepResult(InviteStateStep, true)
            : new ExternalPersonStepResult(InviteStateStep, false,
                "The person's invite and access permission could not be cleared. They stay removed; retry the removal."));

        // ── 5. The standing grant, in a write of its own ────────────────────────────────────────────────────────────
        var standing = await _identityStore.ClearStandingGrantAsync(contactId, ct).ConfigureAwait(false);
        steps.Add(standing.Status switch
        {
            StoreWriteStatus.Written => new ExternalPersonStepResult(StandingGrantStep, true),
            StoreWriteStatus.Forbidden => new ExternalPersonStepResult(StandingGrantStep, false,
                "Dataverse refused to clear the person's standing grant: the service account is not a direct member of the " +
                "Standing Grant Administrators profile. The person stays removed; an administrator must fix that membership " +
                "and retry the removal."),
            _ => new ExternalPersonStepResult(StandingGrantStep, false,
                "The person's standing grant could not be cleared. They stay removed; retry the removal."),
        });

        var complete = steps.All(s => s.Completed);
        return new PersonAdministrationOutcome(
            complete ? PersonAdministrationStatus.Completed : PersonAdministrationStatus.Incomplete,
            stateCode, steps, revoked, notCompleted);
    }

    /// <summary>
    /// Reinstates <paramref name="contactId"/>: clears the removed marker and nothing else. No grant is restored and no invite
    /// is sent; a contact without the marker is a no-op.
    /// </summary>
    public async Task<PersonAdministrationOutcome> ReinstateAsync(Guid contactId, CancellationToken ct)
    {
        var (found, stateCode) = await ReadContactAsync(contactId, ct).ConfigureAwait(false);
        if (found is not PersonAdministrationStatus.Completed)
            return PersonAdministrationOutcome.Refused(found);

        var marker = await ReadMarkerAsync(contactId, ct).ConfigureAwait(false);
        if (marker == RemovedMarkerRead.NotRemoved)
        {
            return new PersonAdministrationOutcome(PersonAdministrationStatus.Completed, stateCode,
                new[] { new ExternalPersonStepResult(MarkerClearedStep, true, "The person was not marked as removed; nothing was changed.") });
        }

        // Removed, or a marker that cannot be read: the administrator asked to reinstate, so the clear is sent either way.
        var written = await _identityStore.WriteRemovedMarkerAsync(contactId, null, ct).ConfigureAwait(false);
        if (written.Status != StoreWriteStatus.Written)
        {
            _logger.LogError(
                "[EXT-REINSTATE-PERSON] The removed marker of contact {ContactId} could not be cleared ({Status}).",
                contactId, written.Status);
            return new PersonAdministrationOutcome(PersonAdministrationStatus.Incomplete, stateCode,
                new[] { new ExternalPersonStepResult(MarkerClearedStep, false,
                    "The removed marker could not be cleared, so the person is still removed. Retry the reinstatement.") });
        }

        return new PersonAdministrationOutcome(PersonAdministrationStatus.Completed, stateCode,
            new[] { new ExternalPersonStepResult(MarkerClearedStep, true) }, MarkerCleared: true);
    }

    private async Task<(PersonAdministrationStatus Status, int? StateCode)> ReadContactAsync(Guid contactId, CancellationToken ct)
    {
        ContactLookup lookup;
        try
        {
            lookup = await _identityStore.GetContactAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-REMOVE-PERSON] Contact {ContactId} could not be read; nothing was written.", contactId);
            return (PersonAdministrationStatus.ContactUnreadable, null);
        }

        if (lookup.Status != LookupStatus.Read)
            return (PersonAdministrationStatus.ContactUnreadable, null);

        return lookup.Rows.FirstOrDefault(r => r.ContactId == contactId) is { } row
            ? (PersonAdministrationStatus.Completed, row.StateCode)
            : (PersonAdministrationStatus.NotFound, null);
    }

    private async Task<RemovedMarkerRead> ReadMarkerAsync(Guid contactId, CancellationToken ct)
    {
        try
        {
            return await _identityStore.ReadRemovedMarkerAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-REMOVE-PERSON] The removed marker of contact {ContactId} could not be read.", contactId);
            return RemovedMarkerRead.Unreadable;
        }
    }

    private async Task<(int Revoked, IReadOnlyList<Guid> NotCompleted, ExternalPersonStepResult Step)> RevokeGrantsAsync(
        ExternalGrantRevocationService revocationService, Guid contactId, CancellationToken ct)
    {
        List<ExternalGrantRow> rows;
        try
        {
            rows = await ExternalGrantLifecycle.QueryActiveRowsForContactAsync(
                _dataverse, contactId, MaxGrantsPerRemoval + 1, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-REMOVE-PERSON] The active grants of contact {ContactId} could not be read; none was revoked.", contactId);
            return (0, Array.Empty<Guid>(), new ExternalPersonStepResult(GrantsStep, false,
                "The person's grants could not be read, so none was revoked. They stay removed; retry the removal."));
        }

        var revoked = 0;
        var notCompleted = new List<Guid>();
        var seen = new HashSet<ExternalGrantKey>();
        foreach (var row in rows)
        {
            if (row.Id == Guid.Empty || ExternalGrantLifecycle.DeriveKey(row) is not { } key)
            {
                notCompleted.Add(row.Id);
                continue;
            }

            // One revoke per LOGICAL grant: it sweeps every active row on the key, so a duplicate row is already handled.
            if (!seen.Add(key))
                continue;

            try
            {
                var revocation = await revocationService.RevokeAsync(row.Id, ct).ConfigureAwait(false);
                if (revocation.Status == GrantRevocationStatus.Revoked && revocation.SpeOutcome != SpeContainerRevokeOutcome.Failed)
                {
                    revoked++;
                }
                else
                {
                    _logger.LogWarning(
                        "[EXT-REMOVE-PERSON] The revoke of grant {Key} (access record {AccessRecordId}) did not complete: {Status}, " +
                        "SPE {SpeOutcome}.", key, row.Id, revocation.Status, revocation.SpeOutcome);
                    notCompleted.Add(row.Id);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EXT-REMOVE-PERSON] The revoke of grant {Key} threw.", key);
                notCompleted.Add(row.Id);
            }
        }

        // Slice C2b: a read above the bound may not hold every grant, so the step is never reported complete.
        if (rows.Count > MaxGrantsPerRemoval)
        {
            _logger.LogWarning(
                "[EXT-REMOVE-PERSON] Contact {ContactId} holds more than {Bound} active grant rows; the rows read were revoked " +
                "and the removal must be retried for the rest.", contactId, MaxGrantsPerRemoval);
            return (revoked, notCompleted, new ExternalPersonStepResult(GrantsStep, false,
                $"{revoked} grant(s) revoked; the person holds more than {MaxGrantsPerRemoval} grant rows, so more may remain. " +
                "The person stays removed; retry the removal."));
        }

        return (revoked, notCompleted, notCompleted.Count == 0
            ? new ExternalPersonStepResult(GrantsStep, true, $"{revoked} grant(s) revoked.")
            : new ExternalPersonStepResult(GrantsStep, false,
                $"{revoked} grant(s) revoked; {notCompleted.Count} did not complete. The person stays removed; retry the removal."));
    }

    private async Task<ExternalPersonStepResult> RemoveOrganizationGrantPermissionsAsync(
        ExternalGrantRevocationService revocationService, Guid contactId, CancellationToken ct)
    {
        PersonOrganizationGrantSpeCleanup cleanup;
        try
        {
            cleanup = await revocationService.RemovePersonOrganizationGrantPermissionsAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-REMOVE-PERSON] The organization-grant container cleanup of contact {ContactId} threw.", contactId);
            cleanup = new PersonOrganizationGrantSpeCleanup(false, null, 0, 0, 0, 0, false);
        }

        if (cleanup.Completed)
        {
            return new ExternalPersonStepResult(OrganizationGrantPermissionsStep, true,
                $"{cleanup.PermissionsRemoved} organization-grant container permission(s) removed.");
        }

        var detail = cleanup.Ambiguous > 0
            ? $"{cleanup.Ambiguous} container permission(s) could not be matched to this person with certainty, so they were " +
              "left in place; an administrator must review them on the container. The person stays removed."
            : "The person's container permissions from their organizations' grants could not all be checked or removed. The " +
              "person stays removed; retry the removal.";
        return new ExternalPersonStepResult(OrganizationGrantPermissionsStep, false, detail);
    }

    private async Task<ExternalPersonStepResult> EvictCachesAsync(Guid contactId, string? correlationId)
    {
        // Never throws (its contract), and not cancelled: a removal that already happened is not left half cleaned up.
        await _participations.InvalidateGrantSetsAsync(new[] { contactId }, Array.Empty<Guid>(), CancellationToken.None)
            .ConfigureAwait(false);

        ReferenceLookup linking;
        try
        {
            linking = await _identityStore.FindSystemUsersLinkingAsync(new[] { contactId }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-REMOVE-PERSON] The systemusers linking contact {ContactId} could not be read.", contactId);
            linking = ReferenceLookup.Failed;
        }

        if (linking.Status != LookupStatus.Read)
        {
            return new ExternalPersonStepResult(CachesStep, false,
                "The internal users linked to this person could not be read, so their cached access was not cleared. The " +
                "person stays removed; retry the removal.");
        }

        foreach (var reference in linking.References.Where(r => r.LinkedContactId == contactId))
        {
            await _membershipInvalidator.InvalidateUserAccessAsync(reference.SystemUserId, correlationId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        return new ExternalPersonStepResult(CachesStep, true);
    }
}
