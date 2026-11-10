// unified-access-control-r2 task 198 Part C (coordinator rulings 2026-11-09): the durable re-grant of a Direct 1:1 message owned by
// the Direct-message owner team (fm task 054a). The team has no members, so the participants' own shares are the only way anyone
// reads such a message.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — DirectThreadAccessService.GrantMessageAccessAsync grants the participants once, at the join, best-effort;
//       SecureChildShareSynchronizer keeps SECURE children's shares equal to their root's; RestrictedFilingShareBar removes
//       barred users' shares on a filed communication. None of them repairs a Direct-team-owned message whose grant did not land,
//       whose content rows appeared after the grant, or that returned to the Direct team (unfiled, or out of secure).
//   (2) Extension — It reuses the live grant's audience rule (fix round 1 R2: the message's BFF-stamped sprk_sentby and
//       sprk_directrecipient, ParticipantsOf), the ONE
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
/// Keeps a Direct-team-owned message readable by EXACTLY its participants: Read on the message and on its content rows the same
/// team owns, for every participant the Restricted bar allows; and on the message, no direct user share for anyone else (the
/// anchor record's grantees an unfile leaves behind). Idempotent: it writes only the difference it reads.
/// </summary>
/// <remarks>
/// <para><b>The audience is a record of the pair only the BFF writes.</b> (1) The message must be owned by the Direct-message
/// owner team — an owner only the BFF assigns, on a row whose owner team has no members, so no participant holds more than Read
/// on it. (2) Fix round 1 R2: the participants are the message's own <c>sprk_sentby</c> and <c>sprk_directrecipient</c>, read
/// by name in the same row read as the owner (<see cref="ParticipantsOf"/>): exactly two distinct, non-empty <c>systemuser</c>
/// ids, or nobody — nothing granted, nothing revoked, settled (a later stamp moves <c>modifiedon</c> and the share job lists the
/// message again). There is no fallback to the thread's owner or shares. (3) The message's thread (its
/// <c>sprk_communicationthread</c> lookup), its <c>sprk_threadtype</c> (Direct) and <c>sprk_isdefaultthread</c> (not the per-user
/// master thread) only NARROW: a message with no thread, a thread that is not Direct, or the master thread, grants nothing. A
/// disabled participant is not granted (settled), and does not stop the other's grant.</para>
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

    /// <summary>Fix round 1 R2: the message's sender, a lookup to <c>systemuser</c> the BFF stamps (fm task 054).</summary>
    internal const string SentByColumn = "sprk_sentby";

    /// <summary>Fix round 1 R2: the Direct message's recipient, a lookup to <c>systemuser</c> the BFF stamps (fm task 054a).</summary>
    internal const string DirectRecipientColumn = "sprk_directrecipient";

    /// <summary>
    /// The ONE row read of the message: its owner, its thread and its participant pair, each selected by name. An attribute the
    /// SDK does not return reads as absent, which names no participant (nothing granted or revoked): it fails safe.
    /// </summary>
    internal static readonly string[] MessageColumns = ["owningteam", ThreadLookup, SentByColumn, DirectRecipientColumn];

    private const string SystemUserTable = "systemuser";

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

        /// <summary>Direct-owned, but it names no audience: no thread, a thread that is not Direct, the per-user master thread, or
        /// (fix round 1 R2) no valid stamped pair. Settled: nothing granted or revoked, not retried.</summary>
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
        IGenericEntityService dataverse, DirectMessageOwnerTeam lookup, Guid owningTeam, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(lookup);
        if (owningTeam == Guid.Empty)
            return false;

        // Fix round 1 R1(a): the team's own row decides first. The Direct team is a named, non-default Owner team, so a
        // default team (every business unit's, the owner of ordinary records) or an Access/Group team is never it — settled
        // with no lookup read, so a lookup that faults or refuses can never turn an ordinary record's message into "unknown".
        // A fault here is not an answer: the lookup decides.
        try
        {
            var query = new QueryExpression("team") { ColumnSet = new ColumnSet("isdefault", "teamtype"), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, owningTeam);
            var team = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
            if (team is not null
                && (team.GetAttributeValue<bool?>("isdefault") == true
                    || (team.GetAttributeValue<OptionSetValue>("teamtype") is { } type && type.Value != SecureRecordOwnerTeam.OwnerTeamType)))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Team {TeamId}'s own row could not be read; the Direct-message lookup decides.", owningTeam);
        }

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
        // ── 1. The message: its owner, its thread and its participant pair, in one read ──
        var message = await ReadRowAsync(CommunicationTable, communicationId, MessageColumns, ct).ConfigureAwait(false);
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
            direct = await IsDirectOwnerTeamAsync(_dataverse, _directTeam, owner, _logger, ct).ConfigureAwait(false);

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

        // ── 4. The thread only NARROWS: it must be a Direct thread that is not the per-user master thread ──
        // Fix round 1 R2: the thread supplies no participant (its owner and shares are not read).
        if (message.GetAttributeValue<EntityReference>(ThreadLookup)?.Id is not { } threadId || threadId == Guid.Empty)
            return NoAudience(communicationId, "it has no thread");

        var thread = await ReadRowAsync(ThreadTable, threadId, [ThreadTypeColumn, DefaultThreadColumn], ct).ConfigureAwait(false);
        if (thread is null)
            return NoAudience(communicationId, "its thread was not found");
        if (thread.GetAttributeValue<OptionSetValue>(ThreadTypeColumn)?.Value != ThreadTypeDirect)
            return NoAudience(communicationId, "its thread is not a Direct thread");
        if (thread.GetAttributeValue<bool?>(DefaultThreadColumn) == true)
            return NoAudience(communicationId, "its thread is the per-user master thread");

        // ── 5. The audience: the message's own stamped pair, from the row read in step 1 ──
        var derived = ParticipantsOf(message);
        if (derived.Participants is not { } participants)
        {
            // An invalid pair is settled, not retried: a later stamp (a repair, fm 054c's backfill) moves modifiedon and the share
            // job lists the message again. fm 054a logs the missing recipient at create, so this is not an error here.
            _logger.LogInformation("{Prefix} {Table} {Id} is owned by the Direct-message owner team, but {Reason}; nothing is granted " +
                "or revoked.", LogPrefix, CommunicationTable, communicationId, derived.NoPair);
            return Outcome.Settled(Status.NoAudience, derived.NoPair);
        }

        // ── 6. Who may be granted: the Restricted bar; a disabled participant is not granted (and not retried) ──
        var (eligibleList, complete) = await _bar.EligibleForGrantAsync(communicationId, participants.ToList(), excludeDisabled: true, ct)
            .ConfigureAwait(false);
        var eligible = eligibleList.ToHashSet();

        // ── 7. The rows: the message, then its content rows the Direct team owns ──
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
            // Fix round 1 R5: the message may have been re-filed (re-owned) since step 1. Its owner is read again right before
            // its own grant, and again before its revokes; a changed or unreadable owner writes nothing more and is retried.
            if (table == CommunicationTable && !await OwnerUnchangedAsync(communicationId, owner, "grant", ct).ConfigureAwait(false))
                return Outcome.Retry("the message's owner changed before its grant");

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

            // 198 pass 2 F1: a content row is granted, then its owner is read back by id; a row no longer owned by exactly this
            // Direct team (a filing into a Secure or Restricted record in between), or one whose owner cannot be read, has THIS
            // call's grants revoked and is retried.
            if (table != CommunicationTable)
            {
                var (rowGranted, rowComplete) = await GrantContentRowAndConfirmAsync(_dataverse, _shares, table, id, owner,
                    eligible, access, communicationId, _logger, ct).ConfigureAwait(false);
                granted.AddRange(rowGranted.Select(user => (table, id, user)));
                complete &= rowComplete;
                continue;
            }

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
            var others = DirectUsers(access).Where(u => !participants.Contains(u)).ToList();
            if (others.Count > 0 && !await OwnerUnchangedAsync(communicationId, owner, "revoke", ct).ConfigureAwait(false))
                return new Outcome(Status.Unknown, granted, revoked, false, "the message's owner changed before its revokes");

            foreach (var other in others)
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

    /// <summary>
    /// Who a Direct-team-owned message's participants are. Exactly one of: <see cref="Participants"/> (the two users);
    /// <see cref="NoPair"/> (the message names no valid pair: settled, nothing granted or revoked); <see cref="Unknown"/> (it could
    /// not be read: retried, nothing granted or revoked).
    /// </summary>
    internal sealed record ParticipantDerivation(IReadOnlySet<Guid>? Participants, string? Unknown, string? NoPair = null)
    {
        internal static ParticipantDerivation Of(Guid sender, Guid recipient) => new(new HashSet<Guid> { sender, recipient }, null);

        internal static ParticipantDerivation NotKnown(string reason) => new(null, reason);

        internal static ParticipantDerivation None(string reason) => new(null, null, reason);
    }

    /// <summary>
    /// THE participant rule of a Direct-team-owned message (fix round 1 R2), over the message row read with
    /// <see cref="MessageColumns"/>: its BFF-stamped <see cref="SentByColumn"/> and <see cref="DirectRecipientColumn"/>. The pair is
    /// valid only as exactly two distinct, non-empty <c>systemuser</c> ids; anything else (either one absent or empty, both equal,
    /// or a lookup to another table) is <see cref="ParticipantDerivation.None"/>. There is no fallback: the thread's owner and its
    /// shares are never a source. The live grant, the re-file step and the share job all apply this one rule.
    /// </summary>
    internal static ParticipantDerivation ParticipantsOf(Entity message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (sender, senderProblem) = UserOf(message, SentByColumn, "sender");
        var (recipient, recipientProblem) = UserOf(message, DirectRecipientColumn, "recipient");
        if ((recipientProblem ?? senderProblem) is { } problem)
            return ParticipantDerivation.None(problem);
        if (sender is not { } from || recipient is not { } to)
        {
            return ParticipantDerivation.None(sender is null && recipient is null ? "no sender or recipient stamped"
                : recipient is null ? "no recipient stamped" : "no sender stamped");
        }

        return from == to
            ? ParticipantDerivation.None("its sender and recipient are the same user")
            : ParticipantDerivation.Of(from, to);
    }

    /// <summary>
    /// The live grant's form of <see cref="ParticipantsOf"/>: reads the message row (<see cref="MessageColumns"/>) and applies the
    /// one rule. A read that faults, or a message not found, is <see cref="ParticipantDerivation.NotKnown"/> (nothing granted; the
    /// share job retries it). Never throws (cancellation aside).
    /// </summary>
    internal static async Task<ParticipantDerivation> DeriveParticipantsAsync(
        IGenericEntityService dataverse, Guid communicationId, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        try
        {
            var query = new QueryExpression(CommunicationTable) { ColumnSet = new ColumnSet(MessageColumns), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition(CommunicationTable + "id", ConditionOperator.Equal, communicationId);
            var message = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault();
            return message is null ? ParticipantDerivation.NotKnown("the message was not found") : ParticipantsOf(message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Prefix} {Table} {Id}: the message could not be read, so its participants are unknown.", LogPrefix,
                CommunicationTable, communicationId);
            return ParticipantDerivation.NotKnown("the message could not be read");
        }
    }

    /// <summary>
    /// The non-empty <c>systemuser</c> id a lookup names. <c>(null, null)</c>: not stamped (absent from the row, null, or an empty
    /// id). <c>(null, problem)</c>: a value that is not a lookup to <c>systemuser</c>.
    /// </summary>
    private static (Guid? Id, string? Problem) UserOf(Entity row, string column, string role)
    {
        if (!row.Attributes.TryGetValue(column, out var value) || value is null)
            return (null, null);
        if (value is not EntityReference reference || !string.Equals(reference.LogicalName, SystemUserTable, StringComparison.OrdinalIgnoreCase))
            return (null, $"its {role} is not a system user");
        return reference.Id == Guid.Empty ? (null, null) : (reference.Id, null);
    }

    /// <summary>
    /// Fix round 1 R5: <c>true</c> when the message is still owned by <paramref name="owner"/>; <c>false</c> (logged) when it is
    /// not, is gone, or cannot be read. Never throws (cancellation aside).
    /// </summary>
    internal static Task<bool> OwnerUnchangedAsync(
        IGenericEntityService dataverse, Guid communicationId, Guid owner, string step, ILogger logger, CancellationToken ct) =>
        RowOwnedByAsync(dataverse, CommunicationTable, communicationId, owner, step, logger, ct);

    /// <summary>
    /// <c>true</c> when the row of <paramref name="table"/> is owned by exactly the team <paramref name="owner"/>, compared by id;
    /// <c>false</c> (logged) when it is not, is gone, or cannot be read. Never throws (cancellation aside).
    /// </summary>
    private static async Task<bool> RowOwnedByAsync(
        IGenericEntityService dataverse, string table, Guid id, Guid owner, string step, ILogger logger, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet("owningteam"), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
            var now = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities.FirstOrDefault()
                ?.GetAttributeValue<EntityReference>("owningteam")?.Id;
            if (now is { } current && current != Guid.Empty && current == owner)
                return true;

            logger.LogWarning("{Prefix} {Table} {Id}: its owner changed before its {Step} (was team {Owner}, now {Now}); nothing more is " +
                "written, and it is retried.", LogPrefix, table, id, step, owner, now);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Prefix} {Table} {Id}: its owner could not be read again before its {Step}; nothing more is written, " +
                "and it is retried.", LogPrefix, table, id, step);
            return false;
        }
    }

    /// <summary>
    /// 198 pass 2 F1: grants Read on one content row of a Direct message to <paramref name="users"/> (the ones that do not hold it;
    /// the caller read the row's shares), then reads the row's owner back by id. When it is no longer exactly
    /// <paramref name="directTeam"/> (a filing into a Secure or Restricted record re-owned it between the listing and the grant), or
    /// cannot be read, every grant ATTEMPTED here is revoked (a share held before the call is never touched) and the answer is
    /// incomplete, so the caller retries the message. Grant-then-confirm, not check-then-grant: a re-own that lands after the
    /// read-back lands after the grant too, so the re-owning path (the Secure synchronizer, the Restricted bar) sees the grant.
    /// <para>198 fix round K1: a grant that throws may still have committed (a timeout after the server applied it), so every
    /// ATTEMPTED user counts: the read-back runs whenever one was attempted, and its undo covers them all (each was verified by the
    /// caller to hold no Read before the grant, so revoking it removes only what this call may have added). The undo runs in a
    /// <c>finally</c> on its own short token (<see cref="UndoTimeout"/>), so a cancellation after a grant does not skip it; the
    /// caller's cancellation still propagates after it.</para>
    /// <para>198 fix round K2: only a participant holding NO share on the row is granted, so the undo never removes a share held
    /// before the call. A participant already holding Read needs nothing. A participant holding a share without Read (Write only,
    /// for example) is not granted Read on this path at all (granting it and then never revoking it would leave Read on a row the
    /// read-back may find Secure): logged, and the row is incomplete, so it is retried.</para>
    /// Never throws (cancellation aside).
    /// </summary>
    /// <param name="participants">The participants who may be granted (the caller applied the Restricted bar).</param>
    /// <param name="existingShares">The row's shares, read strictly by the caller BEFORE this call. A share read that faults is never
    /// "no shares": the caller grants nothing and does not call this.</param>
    /// <returns>The users granted and kept, and whether the row is settled.</returns>
    internal static async Task<(IReadOnlyList<Guid> Granted, bool Complete)> GrantContentRowAndConfirmAsync(
        IGenericEntityService dataverse, IDataverseRecordShareService shares, string table, Guid rowId, Guid directTeam,
        IReadOnlyCollection<Guid> participants, IReadOnlyList<DataversePrincipalAccess> existingShares, Guid communicationId,
        ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(existingShares);
        var entitySet = SecureChildLineage.Children[table].EntitySet;
        var holders = DirectUsers(existingShares);
        var readers = existingShares
            .Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && (s.AccessRightsMask & ReadMask) != 0)
            .Select(s => s.Principal.Id)
            .ToHashSet();
        var complete = true;
        var users = new List<Guid>();
        foreach (var participant in participants.Distinct())
        {
            if (readers.Contains(participant))
                continue;
            if (holders.Contains(participant))
            {
                complete = false;
                logger.LogWarning("{Prefix} {UserId} holds a share without Read on {Table} {RowId} (of {Communication} {Id}); Read is not " +
                    "granted on this path and that share is never revoked here; it is retried.", LogPrefix, participant, table, rowId,
                    CommunicationTable, communicationId);
                continue;
            }

            users.Add(participant);
        }

        var attempted = new List<Guid>();
        var granted = new List<Guid>();
        var confirmed = false;
        try
        {
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                attempted.Add(user); // before the call: a grant that throws may still have committed
                try
                {
                    await shares.GrantAccessAsync(entitySet, rowId, DataversePrincipalRef.User(user), ReadAccessRights, ct).ConfigureAwait(false);
                    granted.Add(user);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    complete = false;
                    logger.LogWarning(ex, "{Prefix} Granting {UserId} Read on {Table} {RowId} (of {Communication} {Id}) failed; it is " +
                        "retried.", LogPrefix, user, table, rowId, CommunicationTable, communicationId);
                }
            }

            confirmed = attempted.Count == 0
                || await RowOwnedByAsync(dataverse, table, rowId, directTeam, "grant read-back", logger, ct).ConfigureAwait(false);
            if (confirmed)
                return (granted, complete);
        }
        finally
        {
            if (!confirmed && attempted.Count > 0)
                await UndoAttemptedGrantsAsync(shares, entitySet, table, rowId, attempted, communicationId, logger).ConfigureAwait(false);
        }

        return ([], false);
    }

    /// <summary>198 fix round K1: bounds the undo of an unconfirmed content-row grant, which runs on its own token.</summary>
    internal static readonly TimeSpan UndoTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 198 fix round K1: revokes every attempted grant on a content row whose owner was not confirmed as the Direct team, or whose
    /// grant or read-back was cancelled. Runs on its own token (<see cref="UndoTimeout"/>), never the caller's, and never throws: it
    /// runs in a <c>finally</c>, so a fault here must not replace the caller's exception.
    /// </summary>
    private static async Task UndoAttemptedGrantsAsync(
        IDataverseRecordShareService shares, string entitySet, string table, Guid rowId, IReadOnlyList<Guid> attempted,
        Guid communicationId, ILogger logger)
    {
        using var undo = new CancellationTokenSource(UndoTimeout);
        foreach (var user in attempted)
        {
            try
            {
                await shares.RevokeAccessAsync(entitySet, rowId, DataversePrincipalRef.User(user), undo.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Prefix} {Table} {RowId} (of {Communication} {Id}) is not confirmed as the Direct team's, and removing " +
                    "the Read attempted for {UserId} failed; the owning path's own reconcile removes it.", LogPrefix, table, rowId,
                    CommunicationTable, communicationId, user);
            }
        }
    }

    private Task<bool> OwnerUnchangedAsync(Guid communicationId, Guid owner, string step, CancellationToken ct) =>
        OwnerUnchangedAsync(_dataverse, communicationId, owner, step, _logger, ct);

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
