// unified-access-control-r2 task 198 Part D (owner 2026-11-09, "Yes keep access": after a message is filed to a record its
// participants keep their shares, except on secure or Restricted targets).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — RestrictedExternalShareRemover removes external-flagged shares on a Restricted ROOT (project, matter, work
//       assignment) and its secure children through SyncRootAsync; NoAccessShareEnforcer works per No Access entry. Neither
//       reaches a communication filed under an ORDINARY Restricted root, whose shares no other writer touches.
//   (2) Extension — It reuses the remover's ONE predicate (InternalShareEndpoints.IsBarredOnRestricted) and its user-flag read
//       (RestrictedExternalShareRemover.ReadUsersAsync), and the ONE inherited Access Permission rule
//       (InheritedAccessPermission over ParentLineageWalk). It is not a method of the remover: the remover's unit is a root
//       under task 143's per-record lease with its last-reader report and root-set cache eviction, none of which applies to a
//       communication; the bar's unit is a message and its content rows. It is called from the one step every re-file writer
//       runs (SecureChildReconciler.AfterRefileAsync) and from the job that revisits filed communications
//       (ChildAccessPermissionReconciler inside SecureChildReconciliationJob).
//   (3) Cost of doing nothing — the rule "a user flagged external holds no share on a Restricted record's content" has no writer
//       for a communication (and its attachment, participant, review-log and document rows) filed under an ordinary record that
//       is Restricted or becomes Restricted.
//
// Placement (bff-extensions.md; ADR-052): BFF domain code, no DI registration (constructed by its two callers from what they
// already hold), no package, no endpoint, no job, no plugin.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// The Restricted bar on a FILED communication: when the record it is filed under is Restricted (its own
/// <c>sprk_accesspermission</c> = Restricted, or inherited through its filing, <see cref="InheritedAccessPermission"/>), every
/// system user holding a direct POA share on the message or on its content rows for whom
/// <see cref="InternalShareEndpoints.IsBarredOnRestricted"/> is true loses those shares. Every communication, not only Direct
/// ones: it is one rule.
/// </summary>
/// <remarks>
/// <para><b>Fail closed, never widening.</b> Nothing here grants. Whether the target is Restricted that cannot be decided removes
/// nothing (logged), as the remover does for unreadable flags. A user is barred only on a STORED <c>sprk_isexternal = true</c>
/// (a blank flag is internal, owner round 67); a user-flag read that faults removes nothing; a user the read did not return is
/// logged and left for the next pass. A share read that faults leaves that row as it is. A revoke is confirmed by a strict
/// read-back; one not confirmed is logged and reported incomplete.</para>
/// <para><b>Content rows</b> are the message's attachment, participant, email review log and document rows
/// (<see cref="ContentRows"/>), read up to <see cref="MaxRowsPerTable"/> each (a capped read is logged and reported incomplete).
/// Gate Decision Records are not here: on every communication re-file their shares are revoked by
/// <c>GateDecisionRecordSharing.ReownAfterFilingAsync</c> (D-169).</para>
/// </remarks>
internal sealed class RestrictedFilingShareBar
{
    internal const string CommunicationTable = "sprk_communication";

    /// <summary>The message's content rows: (table, the lookup to the message). The same set task 198 Part B grants.</summary>
    internal static readonly IReadOnlyList<(string Table, string MessageLookup)> ContentRows =
    [
        ("sprk_communicationattachment", "sprk_communication"),
        ("sprk_communicationparticipant", "sprk_communication"),
        ("sprk_emailreviewlog", "sprk_communication"),
        ("sprk_document", "sprk_relatedcommunication"),
    ];

    /// <summary>Bounds each content-row read of one message (a capped read is logged and reported incomplete).</summary>
    internal const int MaxRowsPerTable = 250;

    private readonly IGenericEntityService _dataverse;
    private readonly CoreAncestorResolver.EntityColumnProbe? _probe;
    private readonly IDataverseRecordShareService? _shares;
    private readonly DataverseWebApiClient? _webApi;
    private readonly ILogger _logger;

    /// <param name="dataverse">Reads (app-only): the filing walk and the content rows.</param>
    /// <param name="probe">Which columns a table has (the walk's metadata probe). <c>null</c>: the target cannot be decided.</param>
    /// <param name="shares">The one share seam (strict read, revoke). <c>null</c>: nothing can be read or removed.</param>
    /// <param name="webApi">The user-flag read's client. <c>null</c>: no user can be read, so nothing is removed.</param>
    /// <param name="logger">The caller's logger.</param>
    internal RestrictedFilingShareBar(
        IGenericEntityService dataverse,
        CoreAncestorResolver.EntityColumnProbe? probe,
        IDataverseRecordShareService? shares,
        DataverseWebApiClient? webApi,
        ILogger logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _probe = probe;
        _shares = shares;
        _webApi = webApi;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>What one pass over one message did.</summary>
    /// <param name="TargetRestricted"><c>true</c> Restricted; <c>false</c> not (or not filed); <c>null</c> not decided.</param>
    /// <param name="Removed">Confirmed removals: (table, row, user).</param>
    /// <param name="Complete">Nothing was left undecided or unconfirmed.</param>
    internal sealed record Outcome(bool? TargetRestricted, IReadOnlyList<(string Table, Guid RowId, Guid UserId)> Removed, bool Complete);

    /// <summary>
    /// After a re-file: decides whether the message's target is Restricted (the inherited Access Permission over its live
    /// filing) and, when it is, removes the barred users' shares. Never throws (cancellation aside).
    /// </summary>
    internal async Task<Outcome> AfterFilingAsync(Guid communicationId, CancellationToken ct)
    {
        bool? restricted;
        try
        {
            restricted = await TargetIsRestrictedAsync(communicationId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {Id}: whether the record it is filed under is Restricted could not be " +
                "read; no share was removed.", CommunicationTable, communicationId);
            restricted = null;
        }

        if (restricted is null)
            return new Outcome(null, [], Complete: false);
        if (restricted == false)
            return new Outcome(false, [], Complete: true);

        return await RemoveBarredSharesAsync(communicationId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// uac-r2 task 198 Part C: the users among <paramref name="candidates"/> who may be GRANTED a share on this communication —
    /// every grant path of a Direct message (the live grant, the re-grant after a re-file, the scheduled backstop) asks here,
    /// so a barred user is never granted. A user is eligible when the user-flag read returned them and
    /// <see cref="InternalShareEndpoints.IsBarredOnRestricted"/> is false against the message's OWN Access Permission or the one
    /// it inherits through its filing (the same predicate and inherited rule <see cref="RemoveBarredSharesAsync"/> enforces).
    /// Fail closed: a user the read does not return is not eligible; a user-flag read fault makes nobody eligible; when an
    /// external-flagged candidate exists and whether the message is Restricted cannot be decided, that user is barred
    /// (<c>rootIsRestricted: null</c>). Each of those reports <c>Complete = false</c>, so the caller retries. Never throws
    /// (cancellation aside).
    /// </summary>
    internal async Task<(IReadOnlyList<Guid> Eligible, bool Complete)> EligibleForGrantAsync(
        Guid communicationId, IReadOnlyList<Guid> candidates, CancellationToken ct)
    {
        var ids = candidates.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return ([], true);

        if (_webApi is null)
        {
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: no user reader is available, so no user is granted.",
                CommunicationTable, communicationId);
            return ([], false);
        }

        Dictionary<Guid, InternalShareEndpoints.SystemUserRow> users;
        try
        {
            users = await RestrictedExternalShareRemover.ReadUsersAsync(_webApi, ids, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {Id}: the users to be granted could not be read; no user is granted.",
                CommunicationTable, communicationId);
            return ([], false);
        }

        var complete = true;
        foreach (var unread in ids.Where(id => !users.ContainsKey(id)))
        {
            complete = false;
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: user {UserId} could not be read; it is not granted.",
                CommunicationTable, communicationId, unread);
        }

        var read = ids.Where(users.ContainsKey).ToList();

        // Only a user flagged external can be barred, so the message's Access Permission is read only when one is a candidate.
        bool? restricted = false;
        if (read.Any(id => users[id].IsExternal == true))
        {
            try
            {
                restricted = await TargetIsRestrictedAsync(communicationId, includeOwnValue: true, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {Id}: whether it is Restricted could not be read; no user flagged " +
                    "external is granted.", CommunicationTable, communicationId);
                restricted = null;
            }

            if (restricted is null)
                complete = false;
        }

        var eligible = read
            .Where(id => !InternalShareEndpoints.IsBarredOnRestricted(users[id].IsExternal, restricted))
            .ToList();
        if (eligible.Count < read.Count)
        {
            _logger.LogInformation("[RESTRICTED-FILING] {Table} {Id}: {Barred} user(s) flagged external are not granted (Restricted " +
                "{Restricted}).", CommunicationTable, communicationId, read.Count - eligible.Count, restricted);
        }

        return (eligible, complete);
    }

    /// <summary>
    /// <c>true</c> when the message is filed under a Restricted record (inherited through its filing) — or, with
    /// <paramref name="includeOwnValue"/>, when its own <c>sprk_accesspermission</c> is Restricted; <c>false</c> when it is
    /// filed under none or the value is not Restricted; <c>null</c> when that cannot be decided (logged).
    /// </summary>
    private async Task<bool?> TargetIsRestrictedAsync(Guid communicationId, CancellationToken ct) =>
        await TargetIsRestrictedAsync(communicationId, includeOwnValue: false, ct).ConfigureAwait(false);

    private async Task<bool?> TargetIsRestrictedAsync(Guid communicationId, bool includeOwnValue, CancellationToken ct)
    {
        if (_probe is null)
        {
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: no column probe is available, so whether its target is " +
                "Restricted cannot be decided; no share was removed.", CommunicationTable, communicationId);
            return null;
        }

        var walk = new ParentLineageWalk(_dataverse, _probe, [InheritedAccessPermission.Column]);
        var row = (await walk.RowsAsync(CommunicationTable, [communicationId], ct).ConfigureAwait(false)).FirstOrDefault();
        if (row is null)
        {
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id} was not found; no share was removed.", CommunicationTable, communicationId);
            return null;
        }

        if (includeOwnValue
            && row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value == InheritedAccessPermission.Restricted)
            return true;

        var parents = await walk.ParentsOfAsync(row, ct).ConfigureAwait(false);
        if (parents.Count == 0)
            return false; // filed under nothing: no target, no bar

        var answer = await InheritedAccessPermission.ResolveAsync(walk, parents, ct, (CommunicationTable, communicationId))
            .ConfigureAwait(false);
        if (answer.Status == ParentTopsStatus.NoParent)
            return false;
        if (answer.Status != ParentTopsStatus.Found || answer.Value is not { } value)
        {
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: the Access Permission of the record it is filed under cannot be " +
                "decided ({Reason}); no share was removed.", CommunicationTable, communicationId, answer.Reason);
            return null;
        }

        return value == InheritedAccessPermission.Restricted;
    }

    /// <summary>
    /// The message's target IS Restricted (the caller decided it): removes every barred user's direct share on the message and
    /// its content rows. Never throws (cancellation aside).
    /// </summary>
    internal async Task<Outcome> RemoveBarredSharesAsync(Guid communicationId, CancellationToken ct)
    {
        if (_shares is null || _webApi is null)
        {
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id} is filed under a Restricted record, but no share seam or user " +
                "reader is available; no share was removed.", CommunicationTable, communicationId);
            return new Outcome(true, [], Complete: false);
        }

        var complete = true;

        // ── The rows: the message, then its content rows ──
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
                var ids = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false))?.Entities
                    .Select(e => e.Id).Where(id => id != Guid.Empty).Distinct().ToList() ?? [];
                if (ids.Count >= MaxRowsPerTable)
                {
                    complete = false;
                    _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: the {Content} read reached its cap of {Cap}; rows beyond it " +
                        "are not checked here.", CommunicationTable, communicationId, table, MaxRowsPerTable);
                }

                rows.AddRange(ids.Select(id => (table, SecureChildLineage.Children[table].EntitySet, id)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                complete = false;
                _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {Id}: its {Content} rows could not be read; their shares are left " +
                    "as they are.", CommunicationTable, communicationId, table);
            }
        }

        // ── Each row's direct user shares, from the STRICT read ──
        var holders = new List<(string Table, string EntitySet, Guid Id, Guid UserId)>();
        foreach (var (table, entitySet, id) in rows)
        {
            try
            {
                var access = await _shares.GetPrincipalAccessOrThrowAsync(table, id, ct).ConfigureAwait(false);
                holders.AddRange(DirectUsers(access).Select(user => (table, entitySet, id, user)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                complete = false;
                _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {RowId} (of {Communication} {Id}): its shares could not be read; " +
                    "they are left as they are.", table, id, CommunicationTable, communicationId);
            }
        }

        if (holders.Count == 0)
            return new Outcome(true, [], complete);

        // ── Who among them is barred: the remover's user-flag read and the ONE predicate ──
        Dictionary<Guid, InternalShareEndpoints.SystemUserRow> users;
        var userIds = holders.Select(h => h.UserId).Distinct().ToList();
        try
        {
            users = await RestrictedExternalShareRemover.ReadUsersAsync(_webApi, userIds, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[RESTRICTED-FILING] {Table} {Id} is filed under a Restricted record, but the users holding its " +
                "shares could not be read; no share was removed.", CommunicationTable, communicationId);
            return new Outcome(true, [], Complete: false);
        }

        foreach (var unread in userIds.Where(id => !users.ContainsKey(id)))
        {
            complete = false;
            _logger.LogWarning("[RESTRICTED-FILING] {Table} {Id}: user {UserId} holds a share but could not be read; it is left for " +
                "the next pass.", CommunicationTable, communicationId, unread);
        }

        bool Barred(Guid id) =>
            users.TryGetValue(id, out var u) && InternalShareEndpoints.IsBarredOnRestricted(u.IsExternal, rootIsRestricted: true);

        // ── Remove each barred share, confirmed by a strict read-back ──
        var removed = new List<(string Table, Guid RowId, Guid UserId)>();
        foreach (var (table, entitySet, id, userId) in holders.Where(h => Barred(h.UserId)))
        {
            try
            {
                await _shares.RevokeAccessAsync(entitySet, id, DataversePrincipalRef.User(userId), ct).ConfigureAwait(false);
                var after = await _shares.GetPrincipalAccessOrThrowAsync(table, id, ct).ConfigureAwait(false);
                if (DirectUsers(after).Contains(userId))
                {
                    complete = false;
                    _logger.LogError("[RESTRICTED-FILING] Removing external user {UserId}'s share on {Table} {RowId} (of {Communication} " +
                        "{Id}) was NOT confirmed: it is still there.", userId, table, id, CommunicationTable, communicationId);
                    continue;
                }

                removed.Add((table, id, userId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                complete = false;
                _logger.LogError(ex, "[RESTRICTED-FILING] Removing external user {UserId}'s share on {Table} {RowId} (of {Communication} " +
                    "{Id}) failed or could not be confirmed.", userId, table, id, CommunicationTable, communicationId);
            }
        }

        _logger.LogInformation("[RESTRICTED-FILING] {Table} {Id} is filed under a Restricted record: removed {Removed} share(s) of " +
            "users flagged external; complete {Complete}.", CommunicationTable, communicationId, removed.Count, complete);
        return new Outcome(true, removed, complete);
    }

    /// <summary>The system users holding a direct share with any right (a zero mask carries none).</summary>
    private static HashSet<Guid> DirectUsers(IReadOnlyList<DataversePrincipalAccess> access) =>
        access.Where(s => s.Principal.Kind == DataversePrincipalKind.SystemUser && s.AccessRightsMask != 0)
            .Select(s => s.Principal.Id)
            .ToHashSet();
}
