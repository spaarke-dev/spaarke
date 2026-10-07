using Spaarke.Dataverse;
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
//       one share seam, the strict read and read-back, the root-set cache clear, the S5 last-reader rule, and task 149's
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
/// <param name="KeptAsLastReader">Users flagged external whose share was KEPT because removing it would leave a secure
/// record with nobody who can open it (owner S5). Manage Access shows them as "External user — no access".</param>
/// <param name="Failures">What could not be done.</param>
public sealed record RestrictedExternalShareReport(
    string RecordType,
    Guid RecordId,
    string Outcome,
    IReadOnlyList<Guid> Removed,
    IReadOnlyList<Guid> KeptAsLastReader,
    IReadOnlyList<RestrictedExternalShareFailure> Failures)
{
    /// <summary>
    /// Nothing could not be done: the record is not Restricted, or it was evaluated with no failure. A share KEPT by S5 is
    /// a decision, not a failure (as task 143's enforcer reports it) — callers report it on its own.
    /// </summary>
    public bool Complete =>
        Outcome is RestrictedExternalShareOutcome.NotRestricted or RestrictedExternalShareOutcome.Evaluated
        && Failures.Count == 0;

    internal static RestrictedExternalShareReport Terminal(
        string recordType, Guid recordId, string outcome, RestrictedExternalShareFailure? failure = null)
        => new(recordType, recordId, outcome, Array.Empty<Guid>(), Array.Empty<Guid>(),
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
/// <para><b>Never the last reader of a secure record</b> (owner S5 — the rule <c>/unshare-user</c> and task 143's enforcer
/// apply). A Restricted record that is ALSO secure is reachable only through its shares. When no enabled internal user
/// with a readable share would remain, nothing is removed and every external-flagged sharer is reported in
/// <see cref="RestrictedExternalShareReport.KeptAsLastReader"/>: the operator shares the record with an internal person,
/// and the next pass removes them. Not serialized against a concurrent removal by another component (task 143's per-record
/// lock is that enforcer's own); <c>/unshare-user</c> takes no lock either.</para>
///
/// <para><b>Its children follow.</b> After any removal, <see cref="SecureChildShareSynchronizer.SyncRootAsync"/> mirrors
/// the record's remaining shares onto its secure children at once (a no-op for an ordinary record); a fan-out that could
/// not finish is a failure (<c>children-incomplete</c>) — the 2-minute reconcile completes it.</para>
///
/// <para><b>Not removable here, and why.</b> Access that is not a direct share — the record's OWNER (Dataverse refuses an
/// app-only revoke of the owner's own share, 0x80040223, and ownership confers access regardless), a team share, a role —
/// stays. A revoke Dataverse refuses is reported as a failure naming the user, so the run is never "complete" while such
/// access remains.</para>
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
    private readonly ILogger<RestrictedExternalShareRemover> _logger;

    public RestrictedExternalShareRemover(
        ExternalParticipationService participations,
        IDataverseRecordShareService recordShare,
        DataverseWebApiClient dataverse,
        ITenantCache cache,
        SecureChildShareSynchronizer secureChildShares,
        ILogger<RestrictedExternalShareRemover> logger)
    {
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _recordShare = recordShare ?? throw new ArgumentNullException(nameof(recordShare));
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _secureChildShares = secureChildShares ?? throw new ArgumentNullException(nameof(secureChildShares));
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
            var read = await _participations.GetRootRecordFlagsAsync(logical, new[] { recordId }, ct).ConfigureAwait(false);
            if (!read.TryGetValue(recordId, out flags) || flags.IsUnreadable)
                return FlagsUnreadable(logical, recordId, null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return FlagsUnreadable(logical, recordId, ex);
        }

        if (!flags.IsRestricted)
            return RestrictedExternalShareReport.Terminal(logical, recordId, RestrictedExternalShareOutcome.NotRestricted);

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
        if (userMasks.Count == 0)
            return Evaluated(logical, recordId, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<RestrictedExternalShareFailure>());

        // ── Who among them is flagged external ─────────────────────────────────
        Dictionary<Guid, InternalShareEndpoints.SystemUserRow> users;
        try
        {
            users = await ReadUsersAsync(userMasks.Keys.ToList(), ct).ConfigureAwait(false);
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

        // Only a STORED true is external (owner round 67 item 3: a blank flag is not).
        var external = userMasks.Keys
            .Where(id => users.TryGetValue(id, out var u) && u.IsExternal == true)
            .OrderBy(id => id)
            .ToList();
        if (external.Count == 0)
            return Evaluated(logical, recordId, Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<RestrictedExternalShareFailure>());

        // ── S5: a secure record keeps someone who can open it ──────────────────
        if (flags.IsSecure)
        {
            var internalReaderRemains = userMasks.Any(p =>
                !external.Contains(p.Key)
                && RecordShareLevels.CanRead(p.Value)
                && users.TryGetValue(p.Key, out var u) && u.IsDisabled is false);
            if (!internalReaderRemains)
            {
                _logger.LogWarning(
                    "[RESTRICTED-EXTERNAL] Restricted secure {Type} {RecordId}: removing the {Count} external user share(s) would " +
                    "leave nobody internal who can open it; they were KEPT (S5). Share it with an internal person first.",
                    logical, recordId, external.Count);
                return Evaluated(logical, recordId, Array.Empty<Guid>(), external, Array.Empty<RestrictedExternalShareFailure>());
            }
        }

        // ── Remove each, confirm by read-back ──────────────────────────────────
        var entitySet = ExternalGrantRoot.BindFor(rootType).EntitySet;
        var removed = new List<Guid>();
        var failures = new List<RestrictedExternalShareFailure>();
        foreach (var userId in external)
        {
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

        // ── Task 149: the record's secure children follow at once ──────────────
        if (removed.Count > 0)
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
                failures.Add(new RestrictedExternalShareFailure(null, "children-incomplete",
                    "External users' access to this Restricted record was removed, but not every related record (documents, " +
                    "events, to-dos, communications) could be updated yet. The scheduled safety net finishes it within a few minutes."));
            }
        }

        _logger.LogInformation(
            "[RESTRICTED-EXTERNAL] Restricted {Type} {RecordId}: {External} external user share(s); removed {Removed}, " +
            "failures {Failures}.", logical, recordId, external.Count, removed.Count, failures.Count);
        return Evaluated(logical, recordId, removed, Array.Empty<Guid>(), failures);
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
        string logical, Guid recordId, IReadOnlyList<Guid> removed, IReadOnlyList<Guid> kept,
        IReadOnlyList<RestrictedExternalShareFailure> failures)
        => new(logical, recordId, RestrictedExternalShareOutcome.Evaluated, removed, kept, failures);
}
