using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

// unified-access-control-r2 task 114 (owner round 67, 2026-10-06, amendments 3 and 4(b)): a Restricted record is for
// internal use only, so a direct POA share held by a system user flagged sprk_isexternal = true is removed — when the
// record becomes Restricted (the record's save: the Assigned-To sync route), and by the Assigned-To reconciliation job as
// the backstop for a share made afterwards through the platform's own Share dialog.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — the Restricted veto (AccessibleRecordSetService.ApplyVetoPipeline, RootRecordFlags
//       .RemovesContactSourcedAccess) is READ-time and removes contact-sourced access only; a POA share is enforced by
//       Dataverse natively on the model-driven app, so no read-time rule can take it away. NoAccessShareEnforcer removes
//       shares, but per No Access ENTRY (subject x object) with N5/N2 rules that do not apply here. The Assigned-To
//       materializer touches only the subjects named in Assigned columns. Nothing removes "every external-flagged share
//       on a Restricted record".
//   (2) Extension — Not inside NoAccessShareEnforcer: its unit is an entry, its author check (N5) and residual-access
//       reporting (N2) have nothing to apply to here, and a second entry point there would give it two reasons to change.
//       Not inside the materializer: its invariant is the Assigned-To ledger; this rule has no subject and no ledger. It
//       REUSES their mechanics and is called from the same two places they are (the sync route, the 5-minute job): the
//       one share seam, the strict read and read-back, the root-set cache clear, task 143's per-record lease, and task 149's
//       child sync after a removal.
//   (3) Cost-of-doing-nothing — a person flagged external keeps opening, editing and re-sharing a record after it is made
//       Restricted, through the model-driven app, which only a POA revoke can stop (owner round 67 item 4).
//
// Placement (bff-extensions.md; ADR-052): BFF domain code over BFF-owned rules, BFF identity, invoked by an existing route
// and an existing in-process job. No package, no column, no plugin (ADR-002), no new endpoint, no new job.

/// <summary>Stable outcome values of <see cref="RestrictedExternalShareReport.Outcome"/>.</summary>
public static class RestrictedExternalShareOutcome
{
    /// <summary>The record is not Restricted: nothing to do (nothing was read beyond its flags).</summary>
    public const string NotRestricted = "not-restricted";

    /// <summary>The record is Restricted and its shares were evaluated; see the lists.</summary>
    public const string Evaluated = "evaluated";

    /// <summary>Whether the record is Restricted could not be read: nothing was removed (ADR-003).</summary>
    public const string FlagsUnreadable = "flags-unreadable";

    /// <summary>The record's shares, or the users holding them, could not be read: nothing was removed.</summary>
    public const string Failed = "failed";
}

/// <summary>Something the remover could not do or could not confirm. Never reported as "removed".</summary>
public sealed record RestrictedExternalShareFailure(Guid? SystemUserId, string Kind, string Message);

/// <summary>What one pass over one record did (task 114).</summary>
/// <param name="RecordType">The record's logical name.</param>
/// <param name="RecordId">The record.</param>
/// <param name="Outcome">One of <see cref="RestrictedExternalShareOutcome"/>.</param>
/// <param name="Removed">Users flagged external whose direct share was removed and confirmed gone by read-back.</param>
/// <param name="Failures">What could not be done.</param>
public sealed record RestrictedExternalShareReport(
    string RecordType,
    Guid RecordId,
    string Outcome,
    IReadOnlyList<Guid> Removed,
    IReadOnlyList<RestrictedExternalShareFailure> Failures)
{
    /// <summary>
    /// Owner round 67, item 3 (decided 2026-10-06: RESTRICTED WINS over the last-reader rule): <c>true</c> when, after this
    /// pass, the SECURE Restricted record has no enabled internal user with a direct share that can read it — an
    /// administrator (who still sees it) must share it with an internal user. A decision for a person, not a failure: it does
    /// not make the pass incomplete. Kind <see cref="NoInternalReaderKind"/>.
    /// </summary>
    public bool NoInternalReader { get; init; }

    /// <summary>The stable kind of <see cref="NoInternalReader"/> as reports and logs name it.</summary>
    public const string NoInternalReaderKind = "no-internal-reader";

    /// <summary>
    /// The record's OWNING user, when that user is flagged external (<see cref="OwnerIsExternalKind"/>). Ownership confers
    /// access that no share revoke can take away (and Dataverse refuses an app-only revoke of the owner's own share,
    /// 0x80040223), so their share is not touched: an administrator reassigns the record. A decision for a person, not a
    /// failure — it does not make the pass incomplete. <c>null</c> otherwise.
    /// </summary>
    public Guid? OwnerIsExternal { get; init; }

    /// <summary>The stable kind of <see cref="OwnerIsExternal"/> as reports and logs name it.</summary>
    public const string OwnerIsExternalKind = "owner-is-external";

    /// <summary>
    /// Nothing could not be done: the record is not Restricted, or it was evaluated with no failure. An external OWNER and a
    /// record left with no internal reader are decisions for an administrator, not failures — callers report them on their
    /// own.
    /// </summary>
    public bool Complete =>
        Outcome is RestrictedExternalShareOutcome.NotRestricted or RestrictedExternalShareOutcome.Evaluated
        && Failures.Count == 0;

    internal static RestrictedExternalShareReport Terminal(
        string recordType, Guid recordId, string outcome, RestrictedExternalShareFailure? failure = null)
        => new(recordType, recordId, outcome, Array.Empty<Guid>(),
            failure is null ? Array.Empty<RestrictedExternalShareFailure>() : new[] { failure });
}

/// <summary>
/// Removes the direct POA shares that system users flagged external (<c>sprk_isexternal = true</c>) hold on a RESTRICTED
/// project, matter or work assignment (owner round 67, 2026-10-06).
/// </summary>
/// <remarks>
/// <para><b>What is removed.</b> Only a DIRECT system-user share, and only when the user's flag is a stored <c>true</c> —
/// a blank flag is not external (owner round 67 item 3). No other share is removed (item 4): not an internal user's, not a
/// team's (a team's members are not read here; removing a team share would strip every other member), and nothing on a
/// record that is not Restricted. Each removal is the <c>InternalShareEndpoints.UnshareAsync</c> discipline: a strict read
/// first (a failed read is never "no share"), the revoke through the one share seam, a strict read-back (a share still
/// there is a failure, never "removed"), and the user's impersonated root-set cache cleared under every tenant key their
/// reads write (in a <c>finally</c>: a revoke that threw may have applied).</para>
///
/// <para><b>Restricted wins over the last-reader rule</b> (owner round 67 item 3, decided 2026-10-06). An external-flagged
/// share is removed even when its holder is the last person who can open a secure Restricted record. When the pass leaves
/// such a record with no enabled internal reader it reports <see cref="RestrictedExternalShareReport.NoInternalReader"/>
/// (<c>no-internal-reader — an administrator must share it with an internal user</c>) with a warning naming the record;
/// administrators still see it. Not a failure.</para>
///
/// <para><b>Serialized with task 143's No Access enforcer</b> per record: the shares are read, the readers counted and the revokes made
/// under the SAME per-record lease that enforcer takes (<see cref="NoAccessShareEnforcer.RecordLockId"/> on
/// <see cref="IScheduledJobLease"/>), renewed immediately before every revoke — so the two can never each remove "the other"
/// last reader. A lease held elsewhere, or a lease store that cannot be reached, removes nothing and is a failure (the
/// 5-minute job retries). <c>/unshare-user</c> takes the same lease.</para>
///
/// <para><b>An external OWNER</b> keeps access by ownership, which no share revoke removes (and Dataverse refuses an
/// app-only revoke of the owner's own share, 0x80040223): their share is left alone and reported once as
/// <see cref="RestrictedExternalShareReport.OwnerIsExternal"/> — "reassign the record" — with a warning naming the record.
/// Ownership is never changed here. When the owner cannot be read the revokes of the proven-external sharers still
/// proceed, but the pass reports <c>owner-unreadable</c> and is INCOMPLETE (so the 5-minute job retries); a refused
/// revoke is a failure too.</para>
///
/// <para><b>Its children follow.</b> After any removal, <see cref="SecureChildShareSynchronizer.SyncRootAsync"/> mirrors
/// the record's remaining shares onto its secure children at once (a no-op for an ordinary record); a fan-out that could
/// not finish is a failure (<c>children-incomplete</c>) — the 2-minute reconcile completes it.</para>
///
/// <para><b>Fails closed</b> (ADR-003): flags that cannot be read, shares that cannot be read, or users whose flag cannot
/// be read remove nothing and are reported. Nothing here ever grants.</para>
/// </remarks>
public sealed class RestrictedExternalShareRemover
{
    private readonly ExternalParticipationService _participations;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly DataverseWebApiClient _dataverse;
    private readonly ITenantCache _cache;
    private readonly SecureChildShareSynchronizer _secureChildShares;
    private readonly IScheduledJobLease _recordLock;
    private readonly ILogger<RestrictedExternalShareRemover> _logger;

    /// <param name="recordLock">The atomic lease task 143's enforcer serializes its per-record removals on (the scheduler's
    /// lease store reused as a keyed mutex under <c>no-access-enforce:{table}:{id}</c> — never a job's dispatch key). The
    /// same project-scoped §6.5 path-A exception (design.md §9, task 143 r2) covers this second user of it.</param>
    public RestrictedExternalShareRemover(
        ExternalParticipationService participations,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverse,
        ITenantCache cache,
        SecureChildShareSynchronizer secureChildShares,
        IScheduledJobLease recordLock,
        ILogger<RestrictedExternalShareRemover> logger)
    {
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _recordShare = recordShare ?? throw new ArgumentNullException(nameof(recordShare));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _secureChildShares = secureChildShares ?? throw new ArgumentNullException(nameof(secureChildShares));
        _recordLock = recordLock ?? throw new ArgumentNullException(nameof(recordLock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Applies the rule to one record now. Never throws a read or write fault: the report carries it.</summary>
    /// <param name="rootType">The record's type (project, matter or work assignment).</param>
    /// <param name="recordId">The record.</param>
    /// <param name="cacheTenants">The tenant namespaces a removed user's root-set cache is cleared under (the caller's
    /// <c>tid</c> and/or the deployment's — never "anonymous"). Empty: nothing is cleared, and the cached set lapses within
    /// its TTL.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<RestrictedExternalShareReport> RemoveForRecordAsync(
        ExternalGrantRootType rootType, Guid recordId, IReadOnlyCollection<string> cacheTenants, CancellationToken ct)
    {
        var logical = ExternalGrantRoot.LogicalNameFor(rootType);

        // ── Is the record Restricted? ─────────────────────────────────────────
        RootRecordFlags flags;
        try
        {
            // Task 174 (owner round 84): Restricted through a parent counts — the EFFECTIVE flags.
            var read = await _participations.GetEffectiveRootRecordFlagsAsync(logical, new[] { recordId }, ct).ConfigureAwait(false);
            if (!read.TryGetValue(recordId, out flags) || flags.IsUnreadable)
                return FlagsUnreadable(logical, recordId, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return FlagsUnreadable(logical, recordId, ex);
        }

        if (!flags.IsRestricted)
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.NotRestricted);

        // ── Task 143's per-record lease: the reads, the reader count and the revokes are decided under it ──
        var lockId = NoAccessShareEnforcer.RecordLockId(logical, recordId);
        ScheduledJobLeaseGrant grant;
        try
        {
            grant = await _recordLock.TryAcquireAsync(lockId, occurrenceUtc: null, NoAccessShareEnforcer.RecordLockDuration, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-EXTERNAL] The removal lock for {Type} {RecordId} could not be taken.", logical, recordId);
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.Failed,
                new RestrictedExternalShareFailure(null, "record-lock-unavailable",
                    "External users' access to this Restricted record was not checked: the lock that keeps a record from " +
                    "losing its last reader could not be taken. Try again."));
        }

        if (grant.Status != ScheduledJobLeaseStatus.Granted || grant.Token is null)
        {
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.Failed,
                new RestrictedExternalShareFailure(null, "record-busy",
                    "External users' access to this Restricted record was not checked: another access change is under way on " +
                    "it. Try again in a moment; the 5-minute safety net retries too."));
        }

        RestrictedExternalShareReport report;
        try
        {
            report = await RemoveUnderLockAsync(rootType, logical, recordId, flags, (lockId, grant.Token), cacheTenants, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _recordLock.ReleaseAsync(lockId, grant.Token, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lease expires on its own (NoAccessShareEnforcer.RecordLockDuration).
                _logger.LogWarning(ex, "[RESTRICTED-EXTERNAL] The removal lock for {Type} {RecordId} could not be released.",
                    logical, recordId);
            }
        }

        // ── Task 149: the record's secure children follow at once (outside the lease: no share of the root is decided) ──
        if (report.Removed.Count > 0)
        {
            SecureChildShareSyncResult children;
            try
            {
                children = await _secureChildShares.SyncRootAsync(logical, recordId, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[RESTRICTED-EXTERNAL] The children of {Type} {RecordId} could not be updated.", logical, recordId);
                children = SecureChildShareSyncResult.Failed("the children could not be updated");
            }

            if (!children.IsComplete)
            {
                _logger.LogWarning(
                    "[RESTRICTED-EXTERNAL] {Type} {RecordId}: external users' shares removed, but its children are {Status} " +
                    "({Detail}).", logical, recordId, children.Status, children.Detail);
                report = report with
                {
                    Failures = report.Failures.Append(new RestrictedExternalShareFailure(null, "children-incomplete",
                        "External users' access to this Restricted record was removed, but not every related record (documents, " +
                        "events, to-dos, communications) could be updated yet. The scheduled safety net finishes it within a few " +
                        "minutes.")).ToList(),
                };
            }
        }

        _logger.LogInformation(
            "[RESTRICTED-EXTERNAL] Restricted {Type} {RecordId}: removed {Removed}, no internal reader {NoInternalReader}, " +
            "external owner {Owner}, failures {Failures}.", logical, recordId, report.Removed.Count, report.NoInternalReader,
            report.OwnerIsExternal?.ToString() ?? "none", report.Failures.Count);
        return report;
    }

    private async Task<RestrictedExternalShareReport> RemoveUnderLockAsync(
        ExternalGrantRootType rootType, string logical, Guid recordId, RootRecordFlags flags,
        (string Id, string Token) recordLock, IReadOnlyCollection<string> cacheTenants, CancellationToken ct)
    {
        // ── Its direct user shares, from the STRICT read ───────────────────────
        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-EXTERNAL] The shares on Restricted {Type} {RecordId} could not be read; nothing was removed.",
                logical, recordId);
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.Failed,
                new RestrictedExternalShareFailure(null, "shares-unreadable",
                    "The shares on this Restricted record could not be read, so no external user's access was removed. Try again."));
        }

        var userMasks = DirectUserMasks(shares);

        // ── The record's OWNER, read on EVERY Restricted pass (verifier V3): an external owner keeps access by ownership
        //    whether or not they also hold a share, so it is reported either way ──
        var (owner, ownerRead) = await ReadOwningUserAsync(rootType, recordId, ct).ConfigureAwait(false);
        // An unreadable owner is a pass that cannot say whether an external user owns the record — not a complete one.
        var ownerFailures = ownerRead
            ? Array.Empty<RestrictedExternalShareFailure>()
            : new[]
            {
                new RestrictedExternalShareFailure(null, "owner-unreadable",
                    "Who owns this Restricted record could not be read, so whether an external user owns it is unknown. Try again."),
            };
        if (userMasks.Count == 0 && owner is null)
            return Evaluated(logical, recordId, Array.Empty<Guid>(), ownerFailures);

        // ── Who among them (and the owner) is flagged external ─────────────────
        Dictionary<Guid, InternalShareEndpoints.SystemUserRow> users;
        try
        {
            var toRead = userMasks.Keys.ToList();
            if (owner is { } ownerToRead && !toRead.Contains(ownerToRead))
                toRead.Add(ownerToRead);
            users = await ReadUsersAsync(toRead, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[RESTRICTED-EXTERNAL] The users holding shares on Restricted {Type} {RecordId} could not be read; nothing was removed.",
                logical, recordId);
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.Failed,
                new RestrictedExternalShareFailure(null, "users-unreadable",
                    "The people this Restricted record is shared with could not be read, so no external user's access was " +
                    "removed. Try again."));
        }

        // The ONE predicate (InternalShareEndpoints.IsBarredOnRestricted): a STORED true — a blank flag is not external (owner
        // round 67 item 3) — whether or not the user is enabled or a person (verifier V4).
        bool Barred(Guid id) =>
            users.TryGetValue(id, out var u) && InternalShareEndpoints.IsBarredOnRestricted(u.IsExternal, rootIsRestricted: true);

        // ── An external OWNER: ownership is not a share — reassign the record (an administrator's act) ──
        Guid? externalOwner = owner is { } o && Barred(o) ? o : null;
        if (externalOwner is { } ownerId)
        {
            _logger.LogWarning(
                "[RESTRICTED-EXTERNAL] {Kind}: Restricted {Type} {RecordId} is OWNED by {UserId}, who is flagged external. " +
                "Ownership confers access no share revoke can remove — reassign the record to an internal owner.",
                RestrictedExternalShareReport.OwnerIsExternalKind, logical, recordId, ownerId);
        }

        var external = userMasks.Keys
            .Where(id => Barred(id) && id != externalOwner)
            .OrderBy(id => id)
            .ToList();
        if (external.Count == 0)
            return Evaluated(logical, recordId, Array.Empty<Guid>(), ownerFailures)
                with { OwnerIsExternal = externalOwner };

        // ── Restricted wins over the last-reader rule (owner round 67 item 3): whether anyone internal remains is REPORTED ──
        // An enabled internal (not external-flagged) user with a readable direct share. Only a SECURE record depends on its
        // shares; an ordinary one is reachable through its business unit.
        var internalReaderRemains = !flags.IsSecure || userMasks.Any(p =>
            !external.Contains(p.Key) && p.Key != externalOwner
            && RecordShareLevels.CanRead(p.Value)
            && users.TryGetValue(p.Key, out var u) && u.IsDisabled is false && !Barred(p.Key));

        // ── Remove each, confirm by read-back — the lease proven ours before every revoke ──
        var entitySet = ExternalGrantRoot.BindFor(rootType).EntitySet;
        var removed = new List<Guid>();
        var failures = new List<RestrictedExternalShareFailure>(ownerFailures);
        foreach (var userId in external)
        {
            if (!await RenewLockAsync(recordLock, logical, recordId, userId, failures, ct).ConfigureAwait(false))
                break; // nothing more is removed without the lease

            var principal = DataversePrincipalRef.User(userId);
            int? remaining = null;
            Exception? failure = null;
            try
            {
                await _recordShare.RevokeAccessAsync(entitySet, recordId, principal, ct).ConfigureAwait(false);
                remaining = DirectUserMasks(
                        await _recordShare.GetPrincipalAccessOrThrowAsync(logical, recordId, ct).ConfigureAwait(false))
                    .GetValueOrDefault(userId);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failure = ex;
            }
            finally
            {
                foreach (var tenant in cacheTenants.Where(t => !string.IsNullOrWhiteSpace(t) && t != "anonymous").Distinct())
                {
                    await ImpersonatedRootSetSource.InvalidateForTenantAsync(_cache, tenant, userId, logical, _logger)
                        .ConfigureAwait(false);
                }
            }

            if (failure is not null || remaining != 0)
            {
                _logger.LogError(failure,
                    "[RESTRICTED-EXTERNAL] Removing external user {UserId}'s share on Restricted {Type} {RecordId} was NOT " +
                    "confirmed (held {Previous}; read-back {Remaining}).",
                    userId, logical, recordId, userMasks[userId], remaining?.ToString() ?? "unreadable");
                failures.Add(new RestrictedExternalShareFailure(userId, "revoke-not-confirmed",
                    $"This record is Restricted, but removing the access of external user {userId} could not be confirmed. " +
                    "Open the record's Manage Access to see whether they still have access, then try again."));
                continue;
            }

            removed.Add(userId);
        }

        var noInternalReader = !internalReaderRemains;
        if (noInternalReader)
        {
            _logger.LogWarning(
                "[RESTRICTED-EXTERNAL] {Kind}: Restricted secure {Type} {RecordId} has no internal user who can open it after the " +
                "external users' shares were removed (Restricted wins over the last-reader rule). An administrator must share " +
                "it with an internal user.", RestrictedExternalShareReport.NoInternalReaderKind, logical, recordId);
        }

        return Evaluated(logical, recordId, removed, failures) with
        {
            OwnerIsExternal = externalOwner,
            NoInternalReader = noInternalReader,
        };
    }

    /// <summary>
    /// Renews the lease immediately before a revoke (task 143 r2's rule): <c>false</c>, with a failure recorded, when it is no
    /// longer ours or cannot be renewed.
    /// </summary>
    private async Task<bool> RenewLockAsync(
        (string Id, string Token) recordLock, string logical, Guid recordId, Guid userId,
        List<RestrictedExternalShareFailure> failures, CancellationToken ct)
    {
        try
        {
            if (await _recordLock.RenewAsync(recordLock.Id, recordLock.Token, NoAccessShareEnforcer.RecordLockDuration, ct)
                    .ConfigureAwait(false))
                return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-EXTERNAL] The removal lock for {Type} {RecordId} could not be renewed.", logical, recordId);
        }

        failures.Add(new RestrictedExternalShareFailure(userId, "record-lock-lost",
            "Not every external user's access to this Restricted record was removed: the hold on the record could not be " +
            "kept. The 5-minute safety net finishes it."));
        return false;
    }

    /// <summary>
    /// The record's OWNING user (<c>null</c> when it is team-owned), and whether the read succeeded — a failed read is
    /// logged and reported as a failure by the caller, never taken as "no external owner".
    /// </summary>
    private async Task<(Guid? Owner, bool Read)> ReadOwningUserAsync(ExternalGrantRootType rootType, Guid recordId, CancellationToken ct)
    {
        var logical = ExternalGrantRoot.LogicalNameFor(rootType);
        try
        {
            var rows = await _dataverse.QueryAsync<OwnerRow>(
                ExternalGrantRoot.BindFor(rootType).EntitySet,
                filter: $"{logical}id eq {recordId}",
                select: "_owninguser_value",
                top: 1,
                cancellationToken: ct).ConfigureAwait(false);
            return (rows.FirstOrDefault()?.OwningUser is { } user && user != Guid.Empty ? user : null, true);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[RESTRICTED-EXTERNAL] The owner of {Type} {RecordId} could not be read; external users' shares are removed " +
                "regardless and the pass is reported incomplete.", logical, recordId);
            return (null, false);
        }
    }

    /// <summary>Each system user's DIRECT share mask (rows OR-ed; a zero mask carries no direct rights and is left out).</summary>
    private static Dictionary<Guid, int> DirectUserMasks(IReadOnlyList<DataversePrincipalAccess> shares)
        => shares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && s.AccessRightsMask != 0)
            .GroupBy(s => s.Principal.Id)
            .ToDictionary(g => g.Key, g => g.Aggregate(0, (mask, s) => mask | s.AccessRightsMask));

    /// <summary>The users' eligibility columns (incl. <c>sprk_isexternal</c>), in batches. Exceptions propagate.</summary>
    private async Task<Dictionary<Guid, InternalShareEndpoints.SystemUserRow>> ReadUsersAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var users = new Dictionary<Guid, InternalShareEndpoints.SystemUserRow>();
        foreach (var batch in ids.Chunk(InternalShareEndpoints.NameBatchSize))
        {
            var rows = await _dataverse.QueryAsync<InternalShareEndpoints.SystemUserRow>(
                InternalShareEndpoints.SystemUserEntitySet,
                filter: string.Join(" or ", batch.Select(id => $"systemuserid eq {id}")),
                select: InternalShareEndpoints.SystemUserSelect,
                top: batch.Length,
                cancellationToken: ct).ConfigureAwait(false);

            // Compared as well as filtered on: a row for any other user is not an answer about these.
            foreach (var row in rows.Where(r => batch.Contains(r.Id)))
                users[row.Id] = row;
        }

        return users;
    }

    private RestrictedExternalShareReport FlagsUnreadable(string logical, Guid recordId, Exception? ex)
    {
        _logger.LogError(ex,
            "[RESTRICTED-EXTERNAL] Whether {Type} {RecordId} is Restricted could not be read; nothing was removed.", logical, recordId);
        return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.FlagsUnreadable,
            new RestrictedExternalShareFailure(null, "flags-unreadable",
                "Whether this record is Restricted could not be read, so no external user's access was checked. Try again."));
    }

    private static RestrictedExternalShareReport Evaluated(
        string logical, Guid recordId, IReadOnlyList<Guid> removed, IReadOnlyList<RestrictedExternalShareFailure> failures)
        => new(logical, recordId, RestrictedExternalShareOutcome.Evaluated, removed, failures);

    /// <summary>The owner projection (a LOOKUP is <c>_x_value</c> in <c>$select</c> — FAILURE-MODES G-13).</summary>
    private sealed class OwnerRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("_owninguser_value")]
        public Guid? OwningUser { get; set; }
    }
}
