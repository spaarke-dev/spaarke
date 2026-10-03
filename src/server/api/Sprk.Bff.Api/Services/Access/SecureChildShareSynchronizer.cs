// unified-access-control-r2 task 149 (C10 part 2 — sharees; GitHub #1071). Ships together with task 146.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — IDataverseRecordShareService is the only POA client, and every caller shares ROOTS
//       (InternalShareEndpoints, ProvisionProjectEndpoint, UnsecureProjectEndpoint, PlaybookSharingService,
//       DirectThreadAccessService). Nothing mirrors a root's shares onto its children, and no relationship cascades
//       Share/Unshare/Reparent (live metadata, 2026-10-02: NoCascade on every root→child relationship).
//   (2) Extension — Not inside IDataverseRecordShareService: it is a pass-through testing seam by its own remarks
//       (ADR-010), and the batched read this task needed WAS added there. Not inside InternalShareEndpoints: the same
//       logic serves provisioning and the scheduled reconcile (and tasks 147/148 call it). The ownership resolver
//       decides an owner BEFORE a row exists; mirroring needs the row, so it cannot live there either.
//   (3) Cost-of-doing-nothing — after task 146 every child of a secure project, matter or work assignment is owned by
//       the memberless Secure Record Owners team, so every internal user shared on the root — its creator included —
//       loses every document, event, to-do and communication on it in MDA and Office; and an MDA Share/Unshare of a
//       secure root never reaches its children (an Unshare that does not is an over-share).
//
// Placement (bff-extensions.md §A/§D; ADR-052): in the BFF. The fan-out runs inside the /share-user and /unshare-user
// request (the caller is told how many children were and were not updated); the safety net is an in-process scheduled
// job (SecureChildShareReconciliationJob, ADR-036). BFF identity, BFF domain code, low volume. No package, no endpoint.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>How a synchronization ended.</summary>
public enum SecureChildShareSyncStatus
{
    /// <summary>Every child in scope matches its root's share set.</summary>
    Completed,

    /// <summary>At least one child in scope could not be brought into line this run (counts say how many).</summary>
    Incomplete,

    /// <summary>Nothing to do: the record is not a secure root, or this environment has no Secure Record owner team.</summary>
    NotApplicable,

    /// <summary>The children or the Secure Record owner team could not be read, so nothing was decided.</summary>
    Failed,
}

/// <summary>What one synchronization did.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="ChildrenInScope">Secure-team-owned children examined (under the root, or all of them).</param>
/// <param name="ChildrenUpdated">Children whose shares were changed, and read back as the mirror.</param>
/// <param name="ChildrenUnchanged">Children whose shares already matched.</param>
/// <param name="ChildrenNotUpdated">Children not brought into line: a share read or write failed. The next run retries.</param>
/// <param name="ChildrenHeld">Children whose secure roots could not be determined from the data (a missing ancestor, a root
/// flagged secure but not isolated, a chain too deep): their shares were only ever narrowed, never widened.</param>
/// <param name="ChildrenOutsideSecureRoots">Secure-team-owned rows under no secure root (for example the children of a root
/// that has since been made ordinary): left untouched — task 148 owns that transition.</param>
/// <param name="SharesGranted">GrantAccess writes made.</param>
/// <param name="SharesChanged">ModifyAccess writes made.</param>
/// <param name="SharesRevoked">RevokeAccess writes made.</param>
/// <param name="Detail">Why the run is <see cref="SecureChildShareSyncStatus.NotApplicable"/> or
/// <see cref="SecureChildShareSyncStatus.Failed"/>.</param>
public sealed record SecureChildShareSyncResult(
    SecureChildShareSyncStatus Status,
    int ChildrenInScope,
    int ChildrenUpdated,
    int ChildrenUnchanged,
    int ChildrenNotUpdated,
    int ChildrenHeld,
    int ChildrenOutsideSecureRoots,
    int SharesGranted,
    int SharesChanged,
    int SharesRevoked,
    string? Detail)
{
    /// <summary>True when nothing in scope is left out of line.</summary>
    public bool IsComplete => Status is SecureChildShareSyncStatus.Completed or SecureChildShareSyncStatus.NotApplicable;

    /// <summary>Children in scope that do not yet match: <see cref="ChildrenNotUpdated"/> + <see cref="ChildrenHeld"/>.</summary>
    public int ChildrenLeftOutOfLine => ChildrenNotUpdated + ChildrenHeld;

    internal static SecureChildShareSyncResult NotApplicable(string detail) =>
        new(SecureChildShareSyncStatus.NotApplicable, 0, 0, 0, 0, 0, 0, 0, 0, 0, detail);

    internal static SecureChildShareSyncResult Failed(string detail) =>
        new(SecureChildShareSyncStatus.Failed, 0, 0, 0, 0, 0, 0, 0, 0, 0, detail);
}

/// <summary>
/// Keeps every CHILD of a secure record shared with exactly the internal principals its secure root is shared with —
/// never wider (unified-access-control-r2 task 149; owner round 7 item 5: "each child's principals and rights equal the
/// root's POA share set ... never wider than the root's").
/// </summary>
/// <remarks>
/// <para><b>Which rows.</b> Rows owned by the Secure Record Owners team (task 144) of the child tables in
/// <see cref="SecureChildLineage"/> — the rows task 146 makes secure. A row's secure roots are the Secure-team-owned
/// project / matter / work-assignment rows reachable upward through its lookups: through Secure-team-owned children, and
/// through children owned by a USER (a run-as-user, client-created or pre-146 row — looked through, as the ownership
/// resolver does); never through a child owned by an ordinary team (its owner already decided it is not secure). Children
/// of non-secure roots are never touched. Roots are never mirrored: a root's own shares are provisioning's and the share
/// endpoints'.</para>
/// <para><b>The mirror.</b> For each principal (system user or team) with a direct share on the root: the root's rights
/// restricted to <see cref="RecordShareLevels.ChildMirrorableMask"/> — Read, Write, Append, AppendTo, Delete; never
/// Share (escalation trigger 6, the recommended default), never Assign. A child under SEVERAL secure roots gets the
/// INTERSECTION: a principal must be shared on every one of them, at the lowest rights (escalation trigger 2, fail closed
/// until the owner decides). Every direct share on the child outside the mirror is revoked; a wider one is narrowed; a
/// missing one is granted. Inherited-only POA rows (mask 0) are neither read as shares nor touched.</para>
/// <para><b>Fail closed</b> (ADR-003 / WP-6).
/// <list type="bullet">
/// <item>A root's shares that cannot be read: NO write on any child that needs that root (the strict read; never the soft
/// one).</item>
/// <item>A child's shares that cannot be read: no write on that child.</item>
/// <item>A child whose secure roots cannot be determined from the data (a missing ancestor, a root flagged
/// <c>sprk_issecure</c> but not isolated, a chain deeper than <see cref="MaxLineageDepth"/>): its shares are only NARROWED —
/// revoked when the known roots do not share it, reduced to what they share — and never granted or widened ("held").</item>
/// <item>A Dataverse fault is not an answer: it ends the run as <see cref="SecureChildShareSyncStatus.Failed"/> or counts the
/// child as not updated, and the next run retries.</item>
/// </list>
/// Writes run revokes first, then narrowings, then grants, and every changed child is read back.</para>
/// <para><b>Triggers</b> (notes/task-149 §4): after a BFF share/unshare on a root (<see cref="SyncRootAsync"/>, in the
/// request), after secure provisioning, and on a short schedule (<see cref="ReconcileAllAsync"/>, which catches creates,
/// re-files, client-side writes and out-of-the-box MDA sharing of a secure root).</para>
/// </remarks>
public sealed class SecureChildShareSynchronizer
{
    /// <summary>Rows per page of each child-table read.</summary>
    internal const int PageSize = 5000;

    /// <summary>Page ceiling per child table (100,000 rows); past it the run fails rather than mirroring part of a table.</summary>
    internal const int MaxPages = 20;

    /// <summary>How many lookups the upward walk follows from a child before its roots count as undetermined.</summary>
    internal const int MaxLineageDepth = 6;

    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";
    private const string IsSecureColumn = "sprk_issecure";
    private const int OwnerTeamType = SecureRecordOwnerTeam.OwnerTeamType;

    private readonly IGenericEntityService _dataverse;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureChildShareSynchronizer> _logger;

    public SecureChildShareSynchronizer(
        IGenericEntityService dataverse,
        IDataverseRecordShareService recordShare,
        IConfiguration configuration,
        ILogger<SecureChildShareSynchronizer> logger)
    {
        _dataverse = dataverse;
        _recordShare = recordShare;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Brings every secure child of ONE root into line with the root's current shares — the fan-out after a share,
    /// a change or an unshare on the root. A root that is not secure answers <see cref="SecureChildShareSyncStatus.NotApplicable"/>
    /// and nothing is read or written below it.
    /// </summary>
    public Task<SecureChildShareSyncResult> SyncRootAsync(string rootLogicalName, Guid rootId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLogicalName);
        if (!SecureChildLineage.IsRoot(rootLogicalName))
            throw new ArgumentOutOfRangeException(nameof(rootLogicalName), rootLogicalName, "Not a secure-root table.");

        return RunAsync(new RowRef(rootLogicalName.ToLowerInvariant(), rootId), ct);
    }

    /// <summary>Brings EVERY secure child in the environment into line — the scheduled reconcile.</summary>
    public Task<SecureChildShareSyncResult> ReconcileAllAsync(CancellationToken ct) => RunAsync(scope: null, ct);

    private async Task<SecureChildShareSyncResult> RunAsync(RowRef? scope, CancellationToken ct)
    {
        Guid secureTeamId;
        try
        {
            var team = await ResolveSecureOwnerTeamAsync(ct).ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return SecureChildShareSyncResult.Failed(refusal);
            if (team.TeamId is not { } id)
                return SecureChildShareSyncResult.NotApplicable(
                    "this environment has no Secure Record owner team, so no record is secure");
            secureTeamId = id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-SHARES] The Secure Record owner team could not be read; nothing was synchronized.");
            return SecureChildShareSyncResult.Failed("the Secure Record owner team could not be read");
        }

        var run = new Run(this, secureTeamId, ct);
        try
        {
            if (scope is { } root)
            {
                var facts = await run.RootAsync(root).ConfigureAwait(false);
                if (facts is null || facts.OwningTeam != secureTeamId)
                {
                    return SecureChildShareSyncResult.NotApplicable(
                        $"{root.Table} {root.Id:D} is not a secure record, so its children are not mirrored");
                }
            }

            await run.LoadSecureChildrenAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-CHILD-SHARES] The secure children could not be read (scope {Scope}); nothing was synchronized.",
                scope?.ToString() ?? "all");
            return SecureChildShareSyncResult.Failed("the secure children could not be read");
        }

        return await run.SynchronizeAsync(scope).ConfigureAwait(false);
    }

    /// <summary>
    /// The Secure Record owner team's id: the configured business unit (TOP 2) and its NAMED, non-default Owner team
    /// (TOP 2) — the same rule as <c>RecordOwnershipResolver</c> and <see cref="SecureRecordOwnerTeam"/>. No business unit
    /// or no team: no secure records can exist (<c>TeamId = null</c>). Two of either: a refusal (cannot tell which rows
    /// are secure).
    /// </summary>
    private async Task<(Guid? TeamId, string? Refusal)> ResolveSecureOwnerTeamAsync(CancellationToken ct)
    {
        var buQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 2, NoLock = true };
        buQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SecureRecordOwnerTeam.BusinessUnitName(_configuration));
        var businessUnits = (await _dataverse.RetrieveMultipleAsync(buQuery, ct).ConfigureAwait(false)).Entities;
        if (businessUnits.Count > 1)
            return (null, "more than one business unit carries the Secure Record name");
        if (businessUnits.Count == 0 || businessUnits[0].Id == Guid.Empty)
            return (null, null);

        var teamQuery = new QueryExpression("team") { ColumnSet = new ColumnSet("teamid"), TopCount = 2, NoLock = true };
        teamQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, businessUnits[0].Id);
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SecureRecordOwnerTeam.OwnerTeamName(_configuration));
        teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, false);
        var teams = (await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false)).Entities;
        if (teams.Count > 1)
            return (null, "more than one Secure Record owner team carries the configured name");
        return teams.Count == 0 || teams[0].Id == Guid.Empty ? (null, null) : (teams[0].Id, null);
    }

    /// <summary>A row of a known table.</summary>
    internal readonly record struct RowRef(string Table, Guid Id)
    {
        public override string ToString() => $"{Table} {Id:D}";
    }

    /// <summary>What one row says about its ownership and filing.</summary>
    private sealed record Row(RowRef Ref, Guid? OwningTeam, bool UserOwned, IReadOnlyList<RowRef> Parents);

    /// <summary>A root's ownership and flag.</summary>
    private sealed record RootFacts(Guid? OwningTeam, bool FlaggedSecure);

    /// <summary>A child's secure roots, and why they are not fully known when they are not.</summary>
    private sealed record Lineage(IReadOnlySet<RowRef> SecureRoots, string? Undetermined);

    /// <summary>One run's state: every read is cached for the run and nothing outlives it.</summary>
    private sealed class Run
    {
        private readonly SecureChildShareSynchronizer _owner;
        private readonly Guid _secureTeamId;
        private readonly CancellationToken _ct;

        private readonly Dictionary<RowRef, Row> _secureChildren = new();
        private readonly Dictionary<RowRef, Row?> _otherRows = new();
        private readonly Dictionary<RowRef, RootFacts?> _roots = new();
        private readonly Dictionary<RowRef, IReadOnlyDictionary<DataversePrincipalRef, int>?> _rootMirrors = new();

        private int _granted, _changed, _revoked;

        public Run(SecureChildShareSynchronizer owner, Guid secureTeamId, CancellationToken ct)
        {
            _owner = owner;
            _secureTeamId = secureTeamId;
            _ct = ct;
        }

        private ILogger Log => _owner._logger;

        // ── Reads ──────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Every Secure-team-owned row of every child table, paged; an incomplete read throws.</summary>
        public async Task LoadSecureChildrenAsync()
        {
            foreach (var table in SecureChildLineage.Children.Values)
            {
                var query = new QueryExpression(table.LogicalName)
                {
                    ColumnSet = new ColumnSet(table.Lookups.Keys.Append(OwningTeamColumn).ToArray()),
                    NoLock = true,
                    PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
                };
                query.Criteria.AddCondition(OwningTeamColumn, ConditionOperator.Equal, _secureTeamId);

                for (var page = 1; ; page++)
                {
                    if (page > MaxPages)
                        throw new InvalidOperationException(
                            $"{table.LogicalName} still had Secure-team-owned rows after {MaxPages} pages of {PageSize}; " +
                            "mirroring part of a table is not attempted.");

                    var result = await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false);
                    foreach (var entity in result.Entities)
                    {
                        var row = ToRow(table, entity);
                        _secureChildren[row.Ref] = row;
                    }

                    if (!result.MoreRecords)
                        break;

                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = result.PagingCookie;
                }
            }
        }

        /// <summary>A root's owner and flag, or <c>null</c> when the row does not exist. A fault propagates.</summary>
        public async Task<RootFacts?> RootAsync(RowRef root)
        {
            if (_roots.TryGetValue(root, out var cached))
                return cached;

            var query = new QueryExpression(root.Table)
            {
                ColumnSet = new ColumnSet(OwningTeamColumn, IsSecureColumn),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(root.Table + "id", ConditionOperator.Equal, root.Id);
            var entity = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();

            // NULL sprk_issecure is "No" (owner decision Q1, 2026-10-01), as in the ownership resolver.
            var facts = entity is null
                ? null
                : new RootFacts(
                    entity.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id,
                    entity.GetAttributeValue<bool?>(IsSecureColumn) == true);
            _roots[root] = facts;
            return facts;
        }

        /// <summary>A child row that is not Secure-team-owned (an intermediate), or <c>null</c> when it does not exist.</summary>
        private async Task<Row?> OtherRowAsync(RowRef reference)
        {
            if (_otherRows.TryGetValue(reference, out var cached))
                return cached;

            var table = SecureChildLineage.Children[reference.Table];
            var query = new QueryExpression(table.LogicalName)
            {
                ColumnSet = new ColumnSet(table.Lookups.Keys.Append(OwningTeamColumn).Append(OwningUserColumn).ToArray()),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(table.IdColumn, ConditionOperator.Equal, reference.Id);
            var entity = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();

            var row = entity is null ? null : ToRow(table, entity);
            _otherRows[reference] = row;
            return row;
        }

        private static Row ToRow(SecureChildLineage.Table table, Entity entity)
        {
            var parents = new List<RowRef>();
            foreach (var (column, target) in table.Lookups)
            {
                if (entity.GetAttributeValue<EntityReference>(column) is { } reference && reference.Id != Guid.Empty)
                    parents.Add(new RowRef(target, reference.Id));
            }

            var owningTeam = entity.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;
            var owningUser = entity.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id;
            return new Row(
                new RowRef(table.LogicalName, entity.Id),
                owningTeam is { } t && t != Guid.Empty ? t : null,
                UserOwned: owningUser is { } u && u != Guid.Empty,
                parents.Distinct().ToArray());
        }

        // ── Lineage ────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>The secure roots a row descends from, walking its lookups upward (see the class remarks).</summary>
        private async Task<Lineage> LineageOfAsync(Row row)
        {
            var roots = new HashSet<RowRef>();
            string? undetermined = null;
            await WalkAsync(row, depth: 0, new HashSet<RowRef> { row.Ref }).ConfigureAwait(false);
            return new Lineage(roots, undetermined);

            async Task WalkAsync(Row current, int depth, HashSet<RowRef> path)
            {
                foreach (var parent in current.Parents)
                {
                    if (path.Contains(parent))
                        continue; // a cycle (a document's current version points back at the document)

                    if (SecureChildLineage.IsRoot(parent.Table))
                    {
                        var facts = await RootAsync(parent).ConfigureAwait(false);
                        if (facts is null)
                            undetermined ??= $"its parent {parent} does not exist";
                        else if (facts.OwningTeam == _secureTeamId)
                            roots.Add(parent);
                        else if (facts.FlaggedSecure)
                            undetermined ??= $"its parent {parent} is marked secure but is not isolated";
                        continue; // an ordinary root contributes nothing
                    }

                    if (depth + 1 >= MaxLineageDepth)
                    {
                        undetermined ??= $"its filing runs deeper than {MaxLineageDepth} levels";
                        continue;
                    }

                    var next = _secureChildren.TryGetValue(parent, out var secure)
                        ? secure
                        : await OtherRowAsync(parent).ConfigureAwait(false);
                    if (next is null)
                    {
                        undetermined ??= $"its parent {parent} does not exist";
                        continue;
                    }

                    // A child owned by an ORDINARY team has been decided not secure by its owner; one owned by the Secure
                    // team or by a user (looked through, as the ownership resolver does) is followed.
                    var follow = next.OwningTeam == _secureTeamId || (next.OwningTeam is null && next.UserOwned);
                    if (!follow)
                        continue;

                    var nextPath = new HashSet<RowRef>(path) { parent };
                    await WalkAsync(next, depth + 1, nextPath).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// The child mirror of one root: principal → mirrored mask, or <c>null</c> when the root's shares could not be
        /// read (the strict read). Principals whose mirror carries no Read are dropped.
        /// </summary>
        private async Task<IReadOnlyDictionary<DataversePrincipalRef, int>?> RootMirrorAsync(RowRef root)
        {
            if (_rootMirrors.TryGetValue(root, out var cached))
                return cached;

            IReadOnlyDictionary<DataversePrincipalRef, int>? mirror;
            try
            {
                mirror = await ReadRootMirrorAsync(root).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex,
                    "[SECURE-CHILD-SHARES] The shares on secure root {Root} could not be read; no child that needs it is written.",
                    root);
                mirror = null;
            }

            _rootMirrors[root] = mirror;
            return mirror;
        }

        /// <summary>One root's mirror from the strict read, uncached. Throws when the shares cannot be read.</summary>
        private async Task<Dictionary<DataversePrincipalRef, int>> ReadRootMirrorAsync(RowRef root)
        {
            var shares = await _owner._recordShare.GetPrincipalAccessOrThrowAsync(root.Table, root.Id, _ct).ConfigureAwait(false);
            return DirectMasks(shares)
                .Where(p => !(p.Key.Kind == DataversePrincipalKind.Team && p.Key.Id == _secureTeamId))
                .Select(p => (p.Key, Mask: RecordShareLevels.ChildMirrorMask(p.Value)))
                .Where(p => RecordShareLevels.CanRead(p.Mask))
                .ToDictionary(p => p.Key, p => p.Mask);
        }

        /// <summary>
        /// The intersection of the child's roots' mirrors, read FRESH (no run cache), or <c>null</c> when any root cannot be
        /// read. Consulted immediately before a grant or a widening, so a root unshare that landed after this run read its
        /// roots — the endpoint's own fan-out racing the scheduled reconcile — is not undone by a stale grant.
        /// </summary>
        private async Task<Dictionary<DataversePrincipalRef, int>?> FreshDesiredAsync(Lineage lineage)
        {
            Dictionary<DataversePrincipalRef, int>? desired = null;
            foreach (var root in lineage.SecureRoots)
            {
                Dictionary<DataversePrincipalRef, int> mirror;
                try
                {
                    mirror = await ReadRootMirrorAsync(root).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "[SECURE-CHILD-SHARES] Re-reading secure root {Root} before a grant failed; nothing is granted.", root);
                    return null;
                }

                desired = desired is null
                    ? mirror
                    : desired
                        .Where(p => mirror.ContainsKey(p.Key))
                        .Select(p => (p.Key, Mask: p.Value & mirror[p.Key]))
                        .Where(p => RecordShareLevels.CanRead(p.Mask))
                        .ToDictionary(p => p.Key, p => p.Mask);
            }

            return desired ?? new Dictionary<DataversePrincipalRef, int>();
        }

        /// <summary>Direct shares by principal, rows OR-ed; inherited-only rows (mask 0) are not shares.</summary>
        private static Dictionary<DataversePrincipalRef, int> DirectMasks(IEnumerable<DataversePrincipalAccess> shares) =>
            shares
                .Where(s => s.AccessRightsMask != 0)
                .GroupBy(s => s.Principal)
                .ToDictionary(g => g.Key, g => g.Aggregate(0, (mask, s) => mask | s.AccessRightsMask));

        // ── Synchronize ────────────────────────────────────────────────────────────────────────────────────────

        public async Task<SecureChildShareSyncResult> SynchronizeAsync(RowRef? scope)
        {
            var updated = 0;
            var unchanged = 0;
            var notUpdated = 0;
            var held = 0;
            var outside = 0;

            // 1. Each secure child's roots; keep those in scope.
            var inScope = new List<(Row Row, Lineage Lineage)>();
            foreach (var row in _secureChildren.Values)
            {
                Lineage lineage;
                try
                {
                    lineage = await LineageOfAsync(row).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    // A fault is not an answer. Out of scope is unknown too, so a scoped run counts it against itself.
                    Log.LogWarning(ex, "[SECURE-CHILD-SHARES] The filing of {Child} could not be read; it is not synchronized.", row.Ref);
                    notUpdated++;
                    continue;
                }

                if (scope is { } root && !lineage.SecureRoots.Contains(root))
                    continue;

                if (lineage.SecureRoots.Count == 0 && lineage.Undetermined is null)
                {
                    outside++;
                    continue;
                }

                inScope.Add((row, lineage));
            }

            // 2. Each child's current shares, batched per table.
            var current = new Dictionary<RowRef, IReadOnlyList<DataversePrincipalAccess>>();
            var unreadable = new HashSet<RowRef>();
            foreach (var group in inScope.GroupBy(c => c.Row.Ref.Table))
            {
                var ids = group.Select(c => c.Row.Ref.Id).ToArray();
                try
                {
                    var shares = await _owner._recordShare
                        .GetPrincipalAccessForRecordsOrThrowAsync(group.Key, ids, _ct).ConfigureAwait(false);
                    foreach (var id in ids)
                    {
                        if (shares.TryGetValue(id, out var list))
                            current[new RowRef(group.Key, id)] = list;
                        else
                            unreadable.Add(new RowRef(group.Key, id));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex,
                        "[SECURE-CHILD-SHARES] The shares on {Count} {Table} rows could not be read; none of them is written.",
                        ids.Length, group.Key);
                    foreach (var id in ids)
                        unreadable.Add(new RowRef(group.Key, id));
                }
            }

            // 3. Mirror each child.
            foreach (var (row, lineage) in inScope)
            {
                if (unreadable.Contains(row.Ref))
                {
                    notUpdated++;
                    continue;
                }

                var outcome = await MirrorAsync(row, lineage, current[row.Ref]).ConfigureAwait(false);
                switch (outcome)
                {
                    case ChildOutcome.Updated: updated++; break;
                    case ChildOutcome.Unchanged: unchanged++; break;
                    case ChildOutcome.Held: held++; break;
                    default: notUpdated++; break;
                }
            }

            var status = notUpdated == 0 && held == 0
                ? SecureChildShareSyncStatus.Completed
                : SecureChildShareSyncStatus.Incomplete;

            Log.Log(
                status == SecureChildShareSyncStatus.Completed ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CHILD-SHARES] scope={Scope} status={Status} inScope={InScope} updated={Updated} unchanged={Unchanged} " +
                "notUpdated={NotUpdated} held={Held} outsideSecureRoots={Outside} granted={Granted} changed={Changed} revoked={Revoked}",
                scope?.ToString() ?? "all", status, inScope.Count, updated, unchanged, notUpdated, held, outside,
                _granted, _changed, _revoked);

            return new SecureChildShareSyncResult(
                status, inScope.Count, updated, unchanged, notUpdated, held, outside, _granted, _changed, _revoked, null);
        }

        private enum ChildOutcome { Updated, Unchanged, NotUpdated, Held }

        /// <summary>One child: compute the mirror, write the difference (revokes, narrowings, grants), read it back.</summary>
        private async Task<ChildOutcome> MirrorAsync(Row row, Lineage lineage, IReadOnlyList<DataversePrincipalAccess> shares)
        {
            // The intersection of every secure root's mirror. A root whose shares cannot be read stops every write here.
            Dictionary<DataversePrincipalRef, int>? desired = null;
            foreach (var root in lineage.SecureRoots.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id))
            {
                var mirror = await RootMirrorAsync(root).ConfigureAwait(false);
                if (mirror is null)
                    return ChildOutcome.NotUpdated;

                desired = desired is null
                    ? new Dictionary<DataversePrincipalRef, int>(mirror)
                    : desired
                        .Where(p => mirror.ContainsKey(p.Key))
                        .Select(p => (p.Key, Mask: p.Value & mirror[p.Key]))
                        .Where(p => RecordShareLevels.CanRead(p.Mask))
                        .ToDictionary(p => p.Key, p => p.Mask);
            }

            desired ??= new Dictionary<DataversePrincipalRef, int>();
            var heldBack = lineage.Undetermined is not null;
            var have = DirectMasks(shares);

            var revokes = new List<DataversePrincipalRef>();
            var modifies = new List<(DataversePrincipalRef Principal, int Mask)>();
            var grants = new List<(DataversePrincipalRef Principal, int Mask)>();

            foreach (var (principal, mask) in have)
            {
                if (!desired.TryGetValue(principal, out var want))
                {
                    revokes.Add(principal);
                    continue;
                }

                // Held: never widen — the target is what the share already carries AND what the known roots allow.
                var target = heldBack ? mask & want : want;
                if (target == mask)
                    continue;
                if (!RecordShareLevels.CanRead(target))
                    revokes.Add(principal);
                else
                    modifies.Add((principal, target));
            }

            if (!heldBack)
            {
                foreach (var (principal, want) in desired)
                {
                    if (!have.ContainsKey(principal))
                        grants.Add((principal, want));
                }
            }

            if (heldBack)
            {
                Log.LogWarning(
                    "[SECURE-CHILD-SHARES] {Child} is held: {Reason}. Its shares are only narrowed ({Revokes} revoke(s), " +
                    "{Narrowings} narrowing(s)); nobody is added until its secure roots can be determined.",
                    row.Ref, lineage.Undetermined, revokes.Count, modifies.Count);
            }

            if (revokes.Count == 0 && modifies.Count == 0 && grants.Count == 0)
                return heldBack ? ChildOutcome.Held : ChildOutcome.Unchanged;

            var failed = false;

            // Anything that ADDS a right is re-checked against the roots read fresh, right before it is written: this run
            // read the roots earlier, and an unshare on a root may have landed since (the endpoint's fan-out racing the
            // scheduled reconcile). A principal the roots no longer share is revoked instead; a right they no longer give
            // is not added. Revokes and narrowings need no re-check — removing access is never the unsafe direction.
            if (grants.Count > 0 || modifies.Any(m => (m.Mask & ~have[m.Principal]) != 0))
            {
                var fresh = await FreshDesiredAsync(lineage).ConfigureAwait(false);
                if (fresh is null)
                {
                    grants.Clear();
                    modifies.RemoveAll(m => (m.Mask & ~have[m.Principal]) != 0);
                    failed = true;
                }
                else
                {
                    grants = grants
                        .Where(g => fresh.ContainsKey(g.Principal) && RecordShareLevels.CanRead(g.Mask & fresh[g.Principal]))
                        .Select(g => (g.Principal, g.Mask & fresh[g.Principal]))
                        .ToList();
                    var rechecked = new List<(DataversePrincipalRef Principal, int Mask)>();
                    foreach (var (principal, mask) in modifies)
                    {
                        var allowed = fresh.TryGetValue(principal, out var f) ? mask & f : 0;
                        if (RecordShareLevels.CanRead(allowed))
                            rechecked.Add((principal, allowed));
                        else
                            revokes.Add(principal);
                    }

                    modifies = rechecked;
                }
            }

            var entitySet = SecureChildLineage.Children[row.Ref.Table].EntitySet;

            foreach (var principal in revokes)
                failed |= !await TryWriteAsync(row.Ref, "revoke", principal,
                    () => _owner._recordShare.RevokeAccessAsync(entitySet, row.Ref.Id, principal, _ct), () => _revoked++);

            foreach (var (principal, mask) in modifies)
                failed |= !await TryWriteAsync(row.Ref, "modify", principal,
                    () => _owner._recordShare.ModifyAccessAsync(
                        entitySet, row.Ref.Id, principal, RecordShareLevels.ChildMirrorRights(mask).AccessRightsCsv, _ct),
                    () => _changed++);

            foreach (var (principal, mask) in grants)
                failed |= !await TryWriteAsync(row.Ref, "grant", principal,
                    () => _owner._recordShare.GrantAccessAsync(
                        entitySet, row.Ref.Id, principal, RecordShareLevels.ChildMirrorRights(mask).AccessRightsCsv, _ct),
                    () => _granted++);

            // Read back: the child must now carry exactly the planned shares.
            var expected = new Dictionary<DataversePrincipalRef, int>(have);
            foreach (var principal in revokes)
                expected.Remove(principal);
            foreach (var (principal, mask) in modifies.Concat(grants))
                expected[principal] = mask;

            try
            {
                var after = DirectMasks(await _owner._recordShare
                    .GetPrincipalAccessOrThrowAsync(row.Ref.Table, row.Ref.Id, _ct).ConfigureAwait(false));
                if (after.Count != expected.Count || after.Any(p => !expected.TryGetValue(p.Key, out var m) || m != p.Value))
                {
                    Log.LogWarning("[SECURE-CHILD-SHARES] {Child} did not read back as its mirror after the writes.", row.Ref);
                    failed = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-SHARES] {Child} could not be read back after the writes.", row.Ref);
                failed = true;
            }

            if (failed)
                return ChildOutcome.NotUpdated;
            return heldBack ? ChildOutcome.Held : ChildOutcome.Updated;
        }

        private async Task<bool> TryWriteAsync(
            RowRef child, string action, DataversePrincipalRef principal, Func<Task> write, Action counted)
        {
            try
            {
                await write().ConfigureAwait(false);
                counted();
                Log.LogDebug("[SECURE-CHILD-SHARES] {Action} {Principal} on {Child}.", action, principal, child);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-SHARES] {Action} for {Principal} on {Child} failed.", action, principal, child);
                return false;
            }
        }
    }
}
