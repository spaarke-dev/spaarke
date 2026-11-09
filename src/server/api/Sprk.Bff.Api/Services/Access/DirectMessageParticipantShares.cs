// unified-access-control-r2 task 198 Part C (coordinator rulings 2026-11-09): the durable re-grant of a Direct 1:1 message owned by
// the Direct-message owner team (fm task 054a). The team has no members, so the participants' own shares are the only way anyone
// reads such a message.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — DirectThreadAccessService.GrantMessageAccessAsync grants the participants once, at the join, best-effort;
//       SecureChildShareSynchronizer keeps SECURE children's shares equal to their root's; RestrictedFilingShareBar removes
//       barred users' shares on a filed communication. None of them repairs a Direct-team-owned message whose grant did not land,
//       whose content rows appeared after the grant, or that returned to the Direct team (unfiled, or out of secure).
//   (2) Extension — It reuses the live grant's audience rule (the thread's user owner and POA systemuser sharees), the ONE
//       Direct-team check (moved here from DirectThreadAccessService, which now calls it), the ONE Secure check
//       (SecureChildShareSynchronizer.IsSecureOwnerTeamAsync), the ONE eligibility rule (RestrictedFilingShareBar.
//       EligibleForGrantAsync over InternalShareEndpoints.IsBarredOnRestricted) and the one share seam. It is not a method of
//       DirectThreadAccessService because its two callers (the step every re-file runs, SecureChildReconciler.AfterRefileAsync,
//       and the two-minute share job, SecureChildShareReconciliationJob) read the message's own state rather than a join, and
//       they hold the probe and user reader it needs; constructing it from what they hold keeps it out of DI.
//   (3) Cost of doing nothing — a failed or skipped participant grant on a Direct-team-owned message leaves that participant
//       (the sender included) unable to read the message, with nothing that ever retries it; after an unfile or a secure exit
//       the participants hold no share at all, and the anchor record's grantees keep theirs.
//
// Placement (bff-extensions.md; ADR-052): BFF domain code, no DI registration, no package, no endpoint; its scheduled host is the
// EXISTING SecureChildShareReconciliationJob (ADR-036 IScheduledJob) — no new job or BackgroundService.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// Keeps a Direct-team-owned message readable by EXACTLY its thread's participants: Read on the message and on its content rows
/// the same team owns, for every participant the Restricted bar allows; and on the message, no direct user share for anyone
/// else (the anchor record's grantees an unfile leaves behind). Idempotent: it writes only the difference it reads.
/// </summary>
/// <remarks>
/// <para><b>The audience is re-derived, never trusted from a column the owner can write.</b> (1) The message must be owned by the
/// Direct-message owner team — an owner only the BFF assigns, on a row whose owner team has no members, so no participant holds
/// more than Read on it. (2) Its thread is the message's own <c>sprk_communicationthread</c> lookup on that row, which no
/// participant can write. (3) The participants are that thread's user owner and its POA systemuser sharees — the two-party list the
/// Direct join created and the live grant uses — read STRICTLY (a share-read fault grants and revokes nothing). The thread's
/// <c>sprk_threadtype</c> (Direct) and <c>sprk_isdefaultthread</c> (not the per-user master thread) only NARROW: a thread that is
/// not Direct, or is the master thread, grants nothing.</para>
/// <para><b>Fail closed, every unknown retried.</b> Whether the owner is the Direct team, or the Secure team, that cannot be told:
/// nothing is written and the outcome is incomplete. A Secure answer is the synchronizer's alone: nothing is written. A user the
/// flag read does not return, a user-flag fault, an undecidable Restricted value, a capped or faulted content-row read, a share
/// read or grant fault, or an unconfirmed revoke: the outcome is incomplete. The share job carries an incomplete message to its next
/// run; nothing is ever granted beyond the participants.</para>
/// </remarks>
internal sealed class DirectMessageParticipantShares
{
    internal const string CommunicationTable = "sprk_communication";
    internal const string ThreadTable = "sprk_communicationthread";

    /// <summary>The message's thread lookup (the same column the thread filing sweep reads).</summary>
    internal const string ThreadLookup = "sprk_communicationthread";

    internal const string ThreadTypeColumn = "sprk_threadtype";
    internal const string DefaultThreadColumn = "sprk_isdefaultthread";

    /// <summary><c>sprk_communicationthread.sprk_threadtype</c> Direct (task 004 schema).</summary>
    internal const int ThreadTypeDirect = 100000001;

    private const int ReadMask = 1;
    private const string ReadAccessRights = "ReadAccess";
    private const string LogPrefix = "[DIRECT-SHARES]";

    /// <summary>The content rows of a Direct message its readers need — the set the live grant and the Restricted bar use.</summary>
    internal static IReadOnlyList<(string Table, string MessageLookup)> ContentRows => RestrictedFilingShareBar.ContentRows;

    /// <summary>Bounds each content-row read of one message (a capped read is logged and reported incomplete).</summary>
    internal const int MaxRowsPerTable = RestrictedFilingShareBar.MaxRowsPerTable;

    private readonly IGenericEntityService _dataverse;
    private readonly IDataverseRecordShareService? _shares;
    private readonly DirectMessageOwnerTeam? _directTeam;
    private readonly IConfiguration _configuration;
    private readonly RestrictedFilingShareBar _bar;
    private readonly ILogger _logger;

    // One Secure answer per team per instance: the share job builds one instance per run, the re-file step one per call.
    private readonly Dictionary<Guid, (bool? Secure, string? Undetermined)> _secureByTeam = new();

    /// <param name="dataverse">Reads (app-only).</param>
    /// <param name="shares">The one share seam. <c>null</c>: nothing can be read or written (incomplete).</param>
    /// <param name="directTeam">fm 054a's Direct-message owner team lookup. <c>null</c>: a message not listed by the job with the
    /// team's id cannot be decided (incomplete, nothing written).</param>
    /// <param name="configuration">The Secure Record settings (the Secure check).</param>
    /// <param name="probe">Which columns a table has (the Restricted bar's filing walk).</param>
    /// <param name="webApi">The user-flag read (the Restricted bar).</param>
    /// <param name="logger">The caller's logger.</param>
    internal DirectMessageParticipantShares(
        IGenericEntityService dataverse,
        IDataverseRecordShareService? shares,
        DirectMessageOwnerTeam? directTeam,
        IConfiguration configuration,
        CoreAncestorResolver.EntityColumnProbe? probe,
        DataverseWebApiClient? webApi,
        ILogger logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _shares = shares;
        _directTeam = directTeam;
        _bar = new RestrictedFilingShareBar(dataverse, probe, shares, webApi, logger);
    }

    /// <summary>What one reconcile of one message decided.</summary>
    internal enum Status
    {
        /// <summary>Not owned by the Direct team (user-owned, another team, or not found): nothing is this class's.</summary>
        NotDirect,

        /// <summary>Owned by the Secure team: the synchronizer's alone.</summary>
        Secure,

        /// <summary>Whether it is Direct- or Secure-owned, or who its participants are, cannot be told now: nothing written.</summary>
        Unknown,

        /// <summary>Direct-owned, but its thread names no audience (none, not Direct, or the per-user master thread).</summary>
        NoAudience,

        /// <summary>Direct-owned and reconciled (see <see cref="Outcome.Complete"/>).</summary>
        Reconciled,
    }

    /// <summary>What one reconcile did.</summary>
    /// <param name="Status">What was decided.</param>
    /// <param name="Granted">Grants made: (table, row, user).</param>
    /// <param name="Revoked">Confirmed revokes on the message: users.</param>
    /// <param name="Complete">Nothing was left undecided, unwritten or unconfirmed; <c>false</c> = retry the message.</param>
    /// <param name="Detail">Why, when not reconciled.</param>
    internal sealed record Outcome(
        Status Status, IReadOnlyList<(string Table, Guid RowId, Guid UserId)> Granted, IReadOnlyList<Guid> Revoked, bool Complete,
        string? Detail = null)
    {
        internal static Outcome Settled(Status status, string? detail = null) => new(status, [], [], true, detail);

        internal static Outcome Retry(string detail) => new(Status.Unknown, [], [], false, detail);
    }

    // ── The ONE Direct-team check (task 198 Part B rule Q5; K3) ─────────────────────────────────────────────────────

    /// <summary>
    /// K3 (fm's review): a resolution names <paramref name="owner"/> only when it IS resolved and both ids are non-empty and equal.
    /// A refusal carries no id, so it never matches any owner, <see cref="Guid.Empty"/> included.
    /// </summary>
    internal static bool Matches(DirectMessageOwnerTeamResolution resolution, Guid owner) =>
        resolution is not null
        && resolution.IsResolved
        && resolution.TeamId is { } team && team != Guid.Empty
        && owner != Guid.Empty
        && team == owner;

    /// <summary>
    /// THE one check that <paramref name="owningTeam"/> is the Direct-message owner team. <c>true</c>: it is; <c>false</c>: it is
    /// not; <c>null</c>: it cannot be told, and the caller grants nothing (and is retried).
    /// <list type="bullet">
    /// <item>An empty id: <c>false</c> (no team).</item>
    /// <item>The team's own row proves it is not the Direct team (<see cref="DirectMessageOwnerTeam.CannotBeDirectOwnerTeamAsync"/>):
    /// <c>false</c>. Its <c>false</c> is "not proven", never "safe": the full resolution decides.</item>
    /// <item>Resolved: <c>true</c> exactly when <see cref="Matches"/>.</item>
    /// <item>Refused as <see cref="DirectMessageOwnerTeamRefusal.NotConfigured"/> with no fault: <c>false</c> (no Direct team can
    /// exist, and fm's writer assigns none).</item>
    /// <item>Any other refusal, or a fault (in the one-row check or the resolution): <c>null</c>.</item>
    /// </list>
    /// </summary>
    internal static async Task<bool?> IsDirectOwnerTeamAsync(
        DirectMessageOwnerTeam lookup, Guid owningTeam, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (owningTeam == Guid.Empty)
            return false;

        try
        {
            if (await lookup.CannotBeDirectOwnerTeamAsync(owningTeam, ct).ConfigureAwait(false))
                return false;

            var direct = await lookup.ResolveDirectMessageOwnerTeamAsync(ct).ConfigureAwait(false);
            if (direct.IsResolved)
                return Matches(direct, owningTeam);
            if (direct.IsNotConfigured && direct.Fault is null)
                return false;
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Whether team {TeamId} is the Direct-message owner team could not be read; treated as unknown.", owningTeam);
            return null;
        }
    }

    // ── The reconcile ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The step after a re-file (<see cref="SecureChildReconciler.AfterRefileAsync(string, Guid, Func{Task{bool}}, CancellationToken)"/>):
    /// a message that is now Direct-team-owned (unfiled back to the Direct team, from an ordinary or a secure record) has its
    /// participants granted and every other user's direct share on it revoked. Any other message is left as it is. Never throws
    /// (cancellation aside).
    /// </summary>
    internal Task<Outcome> AfterRefileAsync(Guid communicationId, CancellationToken ct) =>
        ReconcileAsync(communicationId, listedDirectTeam: null, ct);

    /// <summary>
    /// Reconciles one message. <paramref name="listedDirectTeam"/> is the RESOLVED Direct team the share job listed it under (its
    /// owner is re-read and compared by id); <c>null</c> asks the one Direct-team check. Never throws (cancellation aside).
    /// </summary>
    internal async Task<Outcome> ReconcileAsync(Guid communicationId, Guid? listedDirectTeam, CancellationToken ct)
    {
        try
        {
            return await ReconcileCoreAsync(communicationId, listedDirectTeam, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} {Table} {Id}: the participant reconcile faulted; it is retried.", LogPrefix, CommunicationTable,
                communicationId);
            return Outcome.Retry("the reconcile faulted");
        }
    }

    private async Task<Outcome> ReconcileCoreAsync(Guid communicationId, Guid? listedDirectTeam, CancellationToken ct)
    {
        // ── 1. The message and its owner ──
        var message = await ReadRowAsync(CommunicationTable, communicationId, ["owningteam", ThreadLookup], ct).ConfigureAwait(false);
        if (message is null)
            return Outcome.Settled(Status.NotDirect, "not found");

        // User-owned (a Direct message from before fm 054a, unfiled inbound email, …): unchanged.
        if (message.GetAttributeValue<EntityReference>("owningteam")?.Id is not { } owner || owner == Guid.Empty)
            return Outcome.Settled(Status.NotDirect, "user-owned");

        // ── 2. Is the owner the Direct team? ──
        bool? direct;
        if (listedDirectTeam is { } listed && listed != Guid.Empty)
            direct = owner == listed;
        else if (_directTeam is null)
            return Outcome.Retry("no Direct-team lookup is available");
        else
            direct = await IsDirectOwnerTeamAsync(_directTeam, owner, _logger, ct).ConfigureAwait(false);

        if (direct == false)
            return Outcome.Settled(Status.NotDirect, "owned by another team");
        if (direct is null)
        {
            _logger.LogWarning("{Prefix} {Table} {Id}: whether its owner team is the Direct-message owner team cannot be determined; " +
                "nothing is granted now, and it is retried.", LogPrefix, CommunicationTable, communicationId);
            return Outcome.Retry("the Direct team cannot be determined");
        }

        // ── 3. Secure is never granted here ──
        var (secure, undetermined) = await SecureOfAsync(owner, ct).ConfigureAwait(false);
        if (secure == true)
            return Outcome.Settled(Status.Secure);
        if (secure is null)
        {
            _logger.LogWarning("{Prefix} {Table} {Id}: whether its owner team is the Secure Record team cannot be determined ({Reason}); " +
                "nothing is granted now, and it is retried.", LogPrefix, CommunicationTable, communicationId, undetermined);
            return Outcome.Retry("the Secure team cannot be determined");
        }

        if (_shares is null)
            return Outcome.Retry("no share seam is available");

        // ── 4. The audience: the message's own thread's two-party list ──
        if (message.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } threadId || threadId == Guid.Empty)
            return NoAudience(communicationId, "it has no thread");

        var thread = await ReadRowAsync(ThreadTable, threadId, ["ownerid", ThreadTypeColumn, DefaultThreadColumn], ct).ConfigureAwait(false);
        if (thread is null)
            return NoAudience(communicationId, "its thread was not found");
        if (thread.GetAttributeValue<OptionSetValue>(ThreadTypeColumn)?.Value != ThreadTypeDirect)
            return NoAudience(communicationId, "its thread is not a Direct thread");
        if (thread.GetAttributeValue<bool?>(DefaultThreadColumn) == true)
            return NoAudience(communicationId, "its thread is the per-user master thread");

        var participants = new HashSet<Guid>();
        if (thread.GetAttributeValue<EntityReference>("ownerid") is { } threadOwner
            && string.Equals(threadOwner.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase)
            && threadOwner.Id != Guid.Empty)
        {
            participants.Add(threadOwner.Id);
        }

        try
        {
            participants.UnionWith(DirectUsers(await _shares.GetPrincipalAccessOrThrowAsync(ThreadTable, threadId, ct).ConfigureAwait(false)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} {Table} {Id}: its thread's shares could not be read, so its participants are unknown; " +
                "nothing is granted or revoked now, and it is retried.", LogPrefix, CommunicationTable, communicationId);
            return Outcome.Retry("the thread's shares could not be read");
        }

        if (participants.Count == 0)
            return NoAudience(communicationId, "its thread names no participant");

        // ── 5. Who may be granted: the Restricted bar ──
        var (eligibleList, complete) = await _bar.EligibleForGrantAsync(communicationId, participants.ToList(), ct).ConfigureAwait(false);
        var eligible = eligibleList.ToHashSet();

        // ── 6. The rows: the message, then its content rows the Direct team owns ──
        var rows = new List<(string Table, string EntitySet, Guid Id)>
        {
            (CommunicationTable, SecureChildLineage.Children[CommunicationTable].EntitySet, communicationId),
        };
        foreach (var (table, lookup) in ContentRows)
        {
            try
            {
                var query = new QueryExpression(table) { ColumnSet = new ColumnSet(false), TopCount = MaxRowsPerTable, NoLock = true };
                query.Criteria.AddCondition(lookup, ConditionOperator.Equal, communicationId);
                query.Criteria.AddCondition("owningteam", ConditionOperator.Equal, owner);
                var ids = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities
                    .Select(e => e.Id).Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
                if (ids.Count >= MaxRowsPerTable)
                {
                    complete = false;
                    _logger.LogWarning("{Prefix} {Table} {Id}: the {Content} read reached its cap of {Cap}; rows beyond it are not " +
                        "granted now, and it is retried.", LogPrefix, CommunicationTable, communicationId, table, MaxRowsPerTable);
                }

                rows.AddRange(ids.Select(id => (table, SecureChildLineage.Children[table].EntitySet, id)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                complete = false;
                _logger.LogWarning(ex, "{Prefix} {Table} {Id}: its {Content} rows could not be read; they are retried.", LogPrefix,
                    CommunicationTable, communicationId, table);
            }
        }

        var granted = new List<(string Table, Guid RowId, Guid UserId)>();
        var revoked = new List<Guid>();
        foreach (var (table, entitySet, id) in rows)
        {
            IReadOnlyList<DataversePrincipalAccess> access;
            try
            {
                access = await _shares.GetPrincipalAccessOrThrowAsync(table, id, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                complete = false;
                _logger.LogWarning(ex, "{Prefix} {Table} {RowId} (of {Communication} {Id}): its shares could not be read; it is retried.",
                    LogPrefix, table, id, CommunicationTable, communicationId);
                continue;
            }

            var readers = access
                .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && (s.AccessRightsMask & ReadMask) != 0)
                .Select(s => s.Principal.Id)
                .ToHashSet();

            // Grant the missing ones: never anyone but an eligible participant.
            foreach (var user in eligible.Where(u => !readers.Contains(u)))
            {
                try
                {
                    await _shares.GrantAccessAsync(entitySet, id, DataversePrincipalRef.User(user), ReadAccessRights, ct).ConfigureAwait(false);
                    granted.Add((table, id, user));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    complete = false;
                    _logger.LogWarning(ex, "{Prefix} Granting {UserId} Read on {Table} {RowId} (of {Communication} {Id}) failed; it is " +
                        "retried.", LogPrefix, user, table, id, CommunicationTable, communicationId);
                }
            }

            // On the message: no direct user share for anyone who is not a participant (an unfile's anchor grantees).
            if (table != CommunicationTable)
                continue;

            foreach (var other in DirectUsers(access).Where(u => !participants.Contains(u)))
            {
                try
                {
                    await _shares.RevokeAccessAsync(entitySet, id, DataversePrincipalRef.User(other), ct).ConfigureAwait(false);
                    var after = await _shares.GetPrincipalAccessOrThrowAsync(table, id, ct).ConfigureAwait(false);
                    if (DirectUsers(after).Contains(other))
                    {
                        complete = false;
                        _logger.LogError("{Prefix} Removing {UserId}'s share on {Table} {Id} (not a participant) was NOT confirmed: it " +
                            "is still there; it is retried.", LogPrefix, other, CommunicationTable, communicationId);
                        continue;
                    }

                    revoked.Add(other);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    complete = false;
                    _logger.LogError(ex, "{Prefix} Removing {UserId}'s share on {Table} {Id} (not a participant) failed or could not be " +
                        "confirmed; it is retried.", LogPrefix, other, CommunicationTable, communicationId);
                }
            }
        }

        if (granted.Count > 0 || revoked.Count > 0 || !complete)
        {
            _logger.LogInformation("{Prefix} {Table} {Id}: granted {Granted} share(s) to its participants and removed {Revoked} other " +
                "user share(s); complete {Complete}.", LogPrefix, CommunicationTable, communicationId, granted.Count, revoked.Count,
                complete);
        }

        return new Outcome(Status.Reconciled, granted, revoked, complete);
    }

    private Outcome NoAudience(Guid communicationId, string reason)
    {
        _logger.LogWarning("{Prefix} {Table} {Id} is owned by the Direct-message owner team, but {Reason}; no one is granted.",
            LogPrefix, CommunicationTable, communicationId, reason);
        return Outcome.Settled(Status.NoAudience, reason);
    }

    /// <summary>The ONE Secure check for a team, asked once per team per instance (an unknown is not remembered past it).</summary>
    private async Task<(bool? Secure, string? Undetermined)> SecureOfAsync(Guid team, CancellationToken ct)
    {
        if (_secureByTeam.TryGetValue(team, out var known))
            return known;

        (bool? Secure, string? Undetermined) answer;
        try
        {
            answer = await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(_dataverse, _configuration, team, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} Whether team {TeamId} is the Secure Record team could not be read.", LogPrefix, team);
            answer = (null, "the Secure Record team could not be read");
        }

        _secureByTeam[team] = answer;
        return answer;
    }

    private async Task<Entity?> ReadRowAsync(string table, Guid id, string[] columns, CancellationToken ct)
    {
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
        return (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
    }

    /// <summary>The system users holding a direct share with any right (a zero mask carries none).</summary>
    private static HashSet<Guid> DirectUsers(IReadOnlyList<DataversePrincipalAccess> access) =>
        access.Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && s.AccessRightsMask != 0)
            .Select(s => s.Principal.Id)
            .ToHashSet();
}
