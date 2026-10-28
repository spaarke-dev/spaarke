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

    /// <summary>
    /// A reinstatement refused because the contact is inactive. The marker is unchanged and nothing was written: staff
    /// reactivate the contact first, then reinstate.
    /// </summary>
    RefusedContactInactive,

    /// <summary>
    /// Slice C5: a reinstatement refused because the person still holds an active grant row (a removal that did not finish,
    /// or a grant written around it). The marker is unchanged and nothing was written: staff re-run the removal first, then
    /// reinstate. CLAUDE.md §11: Existing — <see cref="RefusedContactInactive"/> and <see cref="Incomplete"/>. Extension —
    /// neither names the action that unblocks it (re-run the removal). Cost of doing nothing: clearing the marker would make
    /// those rows effective again, giving back access the removal was meant to end.
    /// </summary>
    RefusedGrantsPendingRevoke,

    /// <summary>
    /// Slice C5: the call ended with an exception (a cancelled request, or a fault) before it returned an outcome, so the
    /// audit record says so instead of being lost. Steps already taken stay taken; every step is idempotent, so a retry
    /// finishes the work. CLAUDE.md §11: Existing — <see cref="Incomplete"/>. Extension — Incomplete carries the step results,
    /// and an interrupted call has none to report. Cost of doing nothing: an interrupted removal leaves no audit record.
    /// </summary>
    Interrupted,

    /// <summary>
    /// Slice C5: a conditional marker write found the contact changed since the caller read it (HTTP 412), so nothing was
    /// written. The caller does not retry blindly: the removal sweep reports the contact and decides it again on its next run;
    /// a reinstatement is refused and staff retry it.
    /// CLAUDE.md §11: Existing — <see cref="Incomplete"/>. Extension — Incomplete means "marked, retry"; this contact was not
    /// marked at all and must be decided again from a fresh read. Cost of doing nothing: a stale decision is counted as a
    /// failed removal, or retried on a row whose state it no longer knows.
    /// </summary>
    RowChangedSinceRead,
}

/// <summary>
/// Slice C5: the contact's row version as a removal sweep read it with its decision. A removal given one writes the marker
/// only on the row as it was read (<see cref="IContactIdentityStore.WriteRemovedMarkerIfUnchangedAsync"/>); a null
/// <paramref name="ETag"/> writes nothing. An administrator's removal is given none and marks the row as it is.
/// </summary>
internal sealed record DecisionRowVersion(string? ETag);

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

    /// <summary>Slice C5: what the audit records when the call threw before it returned an outcome.</summary>
    internal static PersonAdministrationOutcome Interrupted { get; } = Refused(PersonAdministrationStatus.Interrupted);
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
/// the root, the Assigned-To marking); a row that names no record (its record was deleted) confers nothing and is
/// deactivated by its id;</item>
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
/// <para><b>Reinstatement</b> reads the contact ONCE (statecode, marker and row version from the same row) and clears the
/// marker with <c>If-Match</c> on that row version; a row changed since is refused, never cleared unconditionally. It clears
/// the marker and nothing else: no grant is restored (staff grant again) and no invite is
/// sent. A contact that carries no marker is a no-op. A person who still holds an active grant row is refused with nothing
/// written (re-run the removal first), so a reinstatement never makes a leftover grant effective. Only an ACTIVE contact is
/// reinstated: the statecode is read live first,
/// an inactive contact is refused with nothing written (reactivate it first, then reinstate), and a statecode that cannot be
/// read refuses as an unreadable contact. So a reinstatement never leaves an inactive contact unmarked for the removal sweep
/// (<see cref="Sprk.Bff.Api.Services.ExternalAccess.ExternalAccessRemovalSweep"/>) to mark again. Residual, intended: a
/// contact deactivated after its reinstatement and before the next sweep is marked again by that sweep. That is a fresh
/// removal, not a defect.</para>
/// <para><b>Staleness.</b> Composition and binding read the marker live on every request, so the person is refused on their
/// next request once the marker is written (0 seconds); the cache evictions are belt and braces.</para>
/// <para><b>Cancellation.</b> The caller's token is honoured up to the marker write. Once the marker is in place every
/// remaining step runs on <see cref="CancellationToken.None"/>, as the cache eviction always did. A caller that sees the call
/// throw still writes its audit record, with <see cref="PersonAdministrationStatus.Interrupted"/>.</para>
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
    /// <param name="markOnlyIfUnchanged">
    /// Slice C5, the removal sweep only: the row version its decision read. The marker is then written only on that row
    /// version; a row changed since is <see cref="PersonAdministrationStatus.RowChangedSinceRead"/> with nothing written.
    /// </param>
    public async Task<PersonAdministrationOutcome> RemoveAsync(
        Guid contactId, string? correlationId, CancellationToken ct, DecisionRowVersion? markOnlyIfUnchanged = null)
    {
        // A wiring fault, thrown before anything is read or written.
        var revocation = _revocation ?? throw new InvalidOperationException("A removal needs the shared grant revoke.");

        var (found, contact) = await ReadContactAsync(contactId, ct).ConfigureAwait(false);
        if (found is not PersonAdministrationStatus.Completed)
            return PersonAdministrationOutcome.Refused(found);

        var stateCode = contact!.StateCode;
        var steps = new List<ExternalPersonStepResult>();

        // ── 1. The marker FIRST: from here on every path refuses the person, whatever else completes ─────────────────
        var marker = await ReadMarkerAsync(contactId, ct).ConfigureAwait(false);
        if (marker == RemovedMarkerRead.Removed)
        {
            steps.Add(new ExternalPersonStepResult(MarkerStep, true, "The person was already marked as removed."));
        }
        else
        {
            var written = markOnlyIfUnchanged is { } read
                ? await _identityStore.WriteRemovedMarkerIfUnchangedAsync(contactId, read.ETag, _time.GetUtcNow(), ct)
                    .ConfigureAwait(false)
                : await _identityStore.WriteRemovedMarkerAsync(contactId, _time.GetUtcNow(), ct).ConfigureAwait(false);
            if (written.Status == StoreWriteStatus.PreconditionFailed && markOnlyIfUnchanged is not null)
            {
                _logger.LogInformation(
                    "[EXT-REMOVE-PERSON] Contact {ContactId} changed since its decision was read, so it was not marked; " +
                    "nothing was changed.", contactId);
                steps.Add(new ExternalPersonStepResult(MarkerStep, false,
                    "The contact changed since it was read, so it was not marked and nothing was changed."));
                return new PersonAdministrationOutcome(PersonAdministrationStatus.RowChangedSinceRead, stateCode, steps,
                    GrantsNotCompleted: Array.Empty<Guid>());
            }

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

        // Slice C5: once the marker is in place every remaining step runs to its end, whatever the caller's token says, so a
        // cancelled request never leaves a marked person with their grants, invite state or standing grant in place.
        var afterMarker = CancellationToken.None;

        // ── 2. Every active grant the person holds, through the ONE revoke ───────────────────────────────────────────
        var (revoked, notCompleted, grantsStep) = await RevokeGrantsAsync(revocation, contactId, afterMarker).ConfigureAwait(false);
        steps.Add(grantsStep);

        // ── 2b. Slice C2c: the person's own permission on the containers of their organizations' grants ─────────────
        steps.Add(await RemoveOrganizationGrantPermissionsAsync(revocation, contactId, afterMarker).ConfigureAwait(false));

        // ── 3. Cache eviction (the marker is read live, so this is belt and braces) ────────────────────────────────
        steps.Add(await EvictCachesAsync(contactId, correlationId).ConfigureAwait(false));

        // ── 4. The invite state and the access permission baseline ───────────────────────────────────────────────────
        var cleared = await _identityStore.ClearRemovedPersonAccessStateAsync(contactId, afterMarker).ConfigureAwait(false);
        steps.Add(cleared.Status == StoreWriteStatus.Written
            ? new ExternalPersonStepResult(InviteStateStep, true)
            : new ExternalPersonStepResult(InviteStateStep, false,
                "The person's invite and access permission could not be cleared. They stay removed; retry the removal."));

        // ── 5. The standing grant, in a write of its own ────────────────────────────────────────────────────────────
        var standing = await _identityStore.ClearStandingGrantAsync(contactId, afterMarker).ConfigureAwait(false);
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
    /// is sent; a contact without the marker is a no-op. Only an active contact is reinstated: an inactive one is
    /// <see cref="PersonAdministrationStatus.RefusedContactInactive"/> and a statecode that cannot be read is
    /// <see cref="PersonAdministrationStatus.ContactUnreadable"/>, both with nothing written.
    /// </summary>
    public async Task<PersonAdministrationOutcome> ReinstateAsync(Guid contactId, CancellationToken ct)
    {
        // Slice C5: ONE read. The statecode, the marker and the row version all come from this row, and the clear below is
        // made only on this row version, so the reinstatement never acts on a state it did not read.
        var (found, contact) = await ReadContactAsync(contactId, ct).ConfigureAwait(false);
        if (found is not PersonAdministrationStatus.Completed)
            return PersonAdministrationOutcome.Refused(found);

        var stateCode = contact!.StateCode;

        // Reinstate only an active contact: the removal sweep marks an inactive one again, so clearing its marker would not hold.
        if (stateCode is null)
            return PersonAdministrationOutcome.Refused(PersonAdministrationStatus.ContactUnreadable);
        if (stateCode != 0)
        {
            return new PersonAdministrationOutcome(PersonAdministrationStatus.RefusedContactInactive, stateCode,
                Array.Empty<ExternalPersonStepResult>());
        }

        // The marker as read with the row: empty on a proven read is "not removed"; set, or empty without that proof, is removed.
        if (!contact.IsRemoved)
        {
            return new PersonAdministrationOutcome(PersonAdministrationStatus.Completed, stateCode,
                new[] { new ExternalPersonStepResult(MarkerClearedStep, true, "The person was not marked as removed; nothing was changed.") });
        }

        // Slice C5: no active grant row may sit on the person when the marker goes: clearing it would make those rows effective.
        if (await HoldsActiveGrantAsync(contactId, ct).ConfigureAwait(false) is not { } holds)
            return PersonAdministrationOutcome.Refused(PersonAdministrationStatus.ContactUnreadable);
        if (holds)
        {
            _logger.LogWarning(
                "[EXT-REINSTATE-PERSON] Contact {ContactId} still holds an active grant row; the reinstatement is refused and " +
                "the marker is kept. Re-run the removal first.", contactId);
            return new PersonAdministrationOutcome(PersonAdministrationStatus.RefusedGrantsPendingRevoke, stateCode,
                Array.Empty<ExternalPersonStepResult>());
        }

        // Removed, or a marker whose read is not proven: the administrator asked to reinstate, so the clear is sent either way,
        // but only on the row version read above (never If-Match: *).
        var written = await _identityStore.WriteRemovedMarkerIfUnchangedAsync(contactId, contact.ETag, null, ct)
            .ConfigureAwait(false);
        if (written.Status == StoreWriteStatus.PreconditionFailed)
        {
            _logger.LogWarning(
                "[EXT-REINSTATE-PERSON] Contact {ContactId} changed since it was read; the marker was not cleared. Retry.",
                contactId);
            return new PersonAdministrationOutcome(PersonAdministrationStatus.RowChangedSinceRead, stateCode,
                new[] { new ExternalPersonStepResult(MarkerClearedStep, false,
                    "The contact changed while it was being reinstated, so the person is still removed. Retry the reinstatement.") });
        }

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

    /// <summary>The contact's row, read once. <see cref="PersonAdministrationStatus.Completed"/> carries the row.</summary>
    private async Task<(PersonAdministrationStatus Status, ContactBindingRow? Row)> ReadContactAsync(
        Guid contactId, CancellationToken ct)
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
            ? (PersonAdministrationStatus.Completed, row)
            : (PersonAdministrationStatus.NotFound, null);
    }

    /// <summary>
    /// Slice C5: whether the person holds at least one active grant row as grantee, read live. Null when the read failed
    /// (logged): a reinstatement then refuses, never assumes "none".
    /// </summary>
    private async Task<bool?> HoldsActiveGrantAsync(Guid contactId, CancellationToken ct)
    {
        try
        {
            var rows = await ExternalGrantLifecycle.QueryActiveRowsForContactAsync(_dataverse, contactId, 1, ct)
                .ConfigureAwait(false);
            return rows.Count > 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-REINSTATE-PERSON] The active grants of contact {ContactId} could not be read; nothing was written.", contactId);
            return null;
        }
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
            if (row.Id == Guid.Empty)
            {
                notCompleted.Add(row.Id);
                continue;
            }

            if (ExternalGrantLifecycle.DeriveKey(row) is not { } key)
            {
                // A row naming no record confers nothing and has no logical grant to revoke, so the person-wide removal
                // deactivates that row itself, by its id.
                if (await DeactivateRecordlessRowAsync(row.Id, ct).ConfigureAwait(false))
                    revoked++;
                else
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

    /// <summary>
    /// Slice C5: deactivates one of the person's grant rows that names no record (its record was deleted). The row confers
    /// nothing, so ending it needs no container cleanup and no cache eviction; leaving it active would keep the removal from
    /// ever completing. True when the row was deactivated.
    /// </summary>
    private async Task<bool> DeactivateRecordlessRowAsync(Guid accessRecordId, CancellationToken ct)
    {
        try
        {
            await ExternalGrantLifecycle.DeactivateAsync(_dataverse, new[] { accessRecordId }, _logger, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[EXT-REMOVE-PERSON] Access record {AccessRecordId} names no record; it was deactivated by its id.", accessRecordId);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-REMOVE-PERSON] Access record {AccessRecordId} names no record and could not be deactivated.", accessRecordId);
            return false;
        }
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
