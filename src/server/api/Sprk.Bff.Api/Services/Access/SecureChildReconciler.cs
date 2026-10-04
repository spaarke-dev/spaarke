// unified-access-control-r2 task 148 (C10 part 2, transitions + backfill; GitHub #1070).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing re-owns the EXISTING children of a root. ProvisionProjectEndpoint and UnsecureProjectEndpoint move
//       the ROOT row only (grep sprk_documents|sprk_events|sprk_todos in both: none). IRecordOwnershipResolver decides the
//       owner of a row being CREATED or RE-FILED (task 146); SecureChildShareSynchronizer mirrors SHARES onto children that
//       are already Secure-team-owned (task 149); AssignCascadeChildOwners snapshots and restores the platform's own Assign
//       cascade rows (task 133). The reconciliation jobs (ExternalAccessReconciliationJob, MembershipReconciliationJob,
//       SecureChildShareReconciliationJob) reconcile grant rows, membership junctions and child SHARES — not child owners.
//   (2) Extension — Not in the endpoints: provisioning, unsecure and the backfill sweep need the SAME pass, and a copy per
//       trigger is the drift the write-path architecture's L1/L4 rule forbids (DATAVERSE-WRITE-PATH-ARCHITECTURE §3: inline
//       and reconciliation call the same invariant owner). Not in the resolver: it answers "which owner", it does not walk a
//       root's descendants or write. Not in the synchronizer: it owns POA shares on Secure-team-owned rows, and its scoped
//       walk deliberately never passes through an ordinary-team-owned row — which is exactly where a not-yet-secured child is.
//       This class composes the three: the resolver decides, this class assigns and reports, the synchronizer shares, the
//       cascade primitive places the platform-cascade rows. No rule is re-implemented here.
//   (3) Cost-of-doing-nothing — a project, matter or work assignment made secure after it has children leaves every one of
//       them readable by ordinary users; an unsecured record leaves its children owned by the memberless Secure team and
//       reachable by nobody (owner round 11 item 3 made that a ship gate); every record secure today keeps its children
//       exposed permanently.
//
// Placement (bff-extensions.md §A/§D; ADR-052): in the BFF. Two triggers run inside the provisioning / unsecure request (the
// caller is told what happened to the children); the third is an in-process scheduled job (SecureChildReconciliationJob,
// ADR-036). BFF identity, BFF domain code, the resolver and synchronizer it composes are BFF services. No package, no
// endpoint, no column. Concrete scoped class (ADR-010: one implementation, no interface; scoped because the synchronizer is).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>Whether a reconcile pass writes.</summary>
public enum SecureChildReconcileMode
{
    /// <summary>Re-own, read back, share (the transitions; the sweep with writes enabled).</summary>
    Apply,

    /// <summary>Read and decide only — report every change that WOULD be made, write nothing (the sweep's default).</summary>
    ReportOnly,
}

/// <summary>How a reconcile pass of one root ended.</summary>
public enum SecureChildReconcileStatus
{
    /// <summary>Every child in scope is (or, report-only, could be decided to be) in its invariant state.</summary>
    Completed,

    /// <summary>At least one child is not in its invariant state: refused, failed, or its shares not in line (counts say which).</summary>
    Incomplete,

    /// <summary>Nothing to do: this environment has no Secure Record owner team, so no record can be secure.</summary>
    NotApplicable,

    /// <summary>The root, the Secure Record owner team or the root's descendants could not be read: nothing was decided.</summary>
    Failed,
}

/// <summary>What happened to one child row.</summary>
public enum SecureChildRowOutcome
{
    /// <summary>Already owned by the owner the rule gives it.</summary>
    AlreadyCorrect,

    /// <summary>Re-owned and read back as the owner the rule gives it.</summary>
    Changed,

    /// <summary>Report-only: would be re-owned.</summary>
    WouldChange,

    /// <summary>
    /// Not this transition's to move: the rule gives an ordinary row another ordinary team (neither side is the Secure team),
    /// or the row names no parent the rule reads (it keeps its owner). Never written.
    /// </summary>
    Untouched,

    /// <summary>The ownership rule refused (an unreadable or flagged-but-not-isolated parent, …): never written.</summary>
    Refused,

    /// <summary>A read or the write failed, or the write did not read back: the row is not in its invariant state.</summary>
    Failed,
}

/// <summary>One row the pass re-owned, would re-own, or could not — with the owner it had BEFORE (reversal evidence).</summary>
/// <param name="Table">Logical name.</param>
/// <param name="Id">Row id.</param>
/// <param name="PreviousOwner">The owner read before any write (<c>null</c> when it could not be read).</param>
/// <param name="TargetTeamId">The team the ownership rule gives it (<c>null</c> when the rule refused).</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">The refusal reason or the failure, when there is one.</param>
public sealed record SecureChildRowChange(
    string Table, Guid Id, DataversePrincipalRef? PreviousOwner, Guid? TargetTeamId, SecureChildRowOutcome Outcome, string? Detail);

/// <summary>Per-table counts of one pass.</summary>
public sealed record SecureChildTableCounts(
    string Table, int Examined, int AlreadyCorrect, int Changed, int WouldChange, int Untouched, int Refused, int Failed);

/// <summary>What one reconcile pass of one root did.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="Mode">Apply or report-only.</param>
/// <param name="RootLogicalName">The root's table.</param>
/// <param name="RootId">The root.</param>
/// <param name="RootIsolated">Whether the root reads as owned by the Secure Record owner team (children go IN) or not (OUT).</param>
/// <param name="Tables">Per-table counts, the platform-cascade tables included.</param>
/// <param name="Changes">Every row re-owned, to be re-owned, refused or failed, with its previous owner.</param>
/// <param name="Shares">The share synchronization run for an isolated root after its children were re-owned (Apply only).</param>
/// <param name="MirrorSharesRevoked">Mirrored child shares taken off children leaving isolation (Apply only).</param>
/// <param name="MirrorRemovalsIncomplete">Children leaving isolation whose mirrored shares could not all be removed.</param>
/// <param name="Detail">Why the pass is <see cref="SecureChildReconcileStatus.Failed"/> or NotApplicable.</param>
public sealed record SecureChildReconcileReport(
    SecureChildReconcileStatus Status,
    SecureChildReconcileMode Mode,
    string RootLogicalName,
    Guid RootId,
    bool RootIsolated,
    IReadOnlyList<SecureChildTableCounts> Tables,
    IReadOnlyList<SecureChildRowChange> Changes,
    SecureChildShareSyncResult? Shares,
    int MirrorSharesRevoked,
    int MirrorRemovalsIncomplete,
    string? Detail)
{
    /// <summary>True when nothing is left out of its invariant state (or there was nothing to do).</summary>
    public bool IsComplete => Status is SecureChildReconcileStatus.Completed or SecureChildReconcileStatus.NotApplicable;

    /// <summary>Rows re-owned by this pass.</summary>
    public int ChildrenReowned => Tables.Sum(t => t.Changed);

    /// <summary>
    /// Rows NOT in their invariant state after this pass: refused + failed (+ planned, report-only), plus children whose
    /// shares are not in line and children whose mirrored shares could not be removed.
    /// </summary>
    public int ChildrenRemaining =>
        Tables.Sum(t => t.Refused + t.Failed + t.WouldChange)
        + (Shares is { IsComplete: false } s ? Math.Max(s.ChildrenLeftOutOfLine, 1) : 0)
        + MirrorRemovalsIncomplete;

    /// <summary>True when the pass wrote anything: an owner, a child share, or a mirrored share removed.</summary>
    public bool WroteAnything =>
        ChildrenReowned > 0
        || MirrorSharesRevoked > 0
        || Shares is { } s && (s.SharesGranted + s.SharesChanged + s.SharesRevoked) > 0;

    internal static SecureChildReconcileReport Ended(
        SecureChildReconcileStatus status, SecureChildReconcileMode mode, string table, Guid id, string detail) =>
        new(status, mode, table, id, false, Array.Empty<SecureChildTableCounts>(), Array.Empty<SecureChildRowChange>(),
            null, 0, 0, detail);
}

/// <summary>
/// unified-access-control-r2 task 148 (C10 part 2) — brings every EXISTING child of ONE secure-capable root into the state
/// task 146's rule gives it: re-owned to the owner <see cref="IRecordOwnershipResolver"/> answers (the named Secure Record
/// owner team under an isolated root; the root's business-unit team once it is not), with the root's sharees mirrored
/// through task 149's <see cref="SecureChildShareSynchronizer"/>. Called by provisioning, by unsecure, and by the sweep job.
/// </summary>
/// <remarks>
/// <para><b>Which rows.</b> The root's descendants through the lineage lookups of <see cref="SecureChildLineage"/> (direct
/// root lookups, the FR-26 <c>sprk_regarding{core}</c> stamps, and every further hop — a to-do regarding a document on the
/// root is found through the document), walked DOWNWARD level by level through rows of ANY owner (a not-yet-secured child is
/// owned by an ordinary team), at most <see cref="SecureChildShareSynchronizer.MaxLineageDepth"/> levels. Plus the rows the
/// platform's own Assign cascade moves with the root (<see cref="AssignCascadeChildOwners"/>: SharePoint document locations
/// and documents). Roots are never children here — a work assignment under a project is its own root (task 158), and
/// neither it nor its own children are reached. An unfiled row is never reached.</para>
/// <para><b>The rule is the resolver's, with 146's inputs.</b> Each row's parents are every ownership-parent lookup it
/// carries (<see cref="RecordOwnershipContext.ParentsOf"/> over all its columns — the reparent's input), plus, for a message,
/// its record thread's filing (S6, as <c>AssignToThreadReconcilingOwnerAsync</c> passes it); a row that names no parent keeps
/// its owner (<see cref="UnfiledOwnership.KeepCreator"/>). Rows are processed shallowest first, so a grandchild is decided
/// after its parent moved. A row is moved only when the move crosses the isolation boundary — INTO the Secure team, or OUT of
/// it; an ordinary row the rule would hand another ordinary team is not this transition's (<see
/// cref="SecureChildRowOutcome.Untouched"/>). The platform-cascade rows belong to the root alone and always take the rule's
/// owner (owner round 13 item 1).</para>
/// <para><b>Ordering.</b> INTO isolation: every child re-owned, then <see cref="SecureChildShareSynchronizer.SyncRootAsync"/>
/// mirrors the root's sharees (149 mirrors only Secure-team-owned rows, so the mirror follows the re-own — the transient is an
/// UNDER-share of the root's sharees for the length of the pass, never an over-share: no principal that could not read a
/// child before can read it after a re-own). OUT of isolation: every child re-owned first (so it is reachable through its
/// business unit), then its mirrored shares removed (<see cref="SecureChildShareSynchronizer.RemoveMirrorAsync"/>). The
/// caller does the root's own steps around this pass.</para>
/// <para><b>Every assign</b> is its own operation, never folded into a field update, read back, and preceded by a log line
/// naming the row's previous owner (reversal evidence); the report carries the same.</para>
/// <para><b>Fail closed</b> (ADR-003). An unreadable root, Secure team or descendant set decides nothing and writes nothing
/// (<see cref="SecureChildReconcileStatus.Failed"/>). A refusal or failure on one row leaves THAT row as it was and makes
/// the pass <see cref="SecureChildReconcileStatus.Incomplete"/>; the next pass (a repeated provisioning or unsecure call, or
/// the sweep) completes it — every step is keyed on observed state, so a pass is idempotent.</para>
/// </remarks>
public sealed class SecureChildReconciler
{
    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";
    private const string OwnerColumn = "ownerid";
    private const string IsSecureColumn = "sprk_issecure";
    private const string CommunicationTable = "sprk_communication";
    private const string ThreadLookupOnCommunication = "sprk_communicationthread";

    private readonly IGenericEntityService _dataverse;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly SecureChildShareSynchronizer _shares;
    private readonly DataverseWebApiClient _webApi;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureChildReconciler> _logger;

    public SecureChildReconciler(
        IGenericEntityService dataverse,
        IRecordOwnershipResolver ownership,
        SecureChildShareSynchronizer shares,
        DataverseWebApiClient webApi,
        IConfiguration configuration,
        ILogger<SecureChildReconciler> logger)
    {
        _dataverse = dataverse;
        _ownership = ownership;
        _shares = shares;
        _webApi = webApi;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Reconciles every existing child of one root. <paramref name="unsecuring"/> is set ONLY by the unsecure endpoint, after
    /// it moved the root off the Secure team and read the move back: the root's <c>sprk_issecure</c> is still <c>true</c>
    /// (cleared last), so the resolver is told this one root is mid-transition (<see cref="RecordOwnershipContext.UnsecuringRoot"/>).
    /// A root that still reads as Secure-team-owned is refused that exemption (Failed).
    /// </summary>
    public async Task<SecureChildReconcileReport> ReconcileAsync(
        string rootLogicalName, Guid rootId, SecureChildReconcileMode mode, bool unsecuring, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLogicalName);
        var rootTable = rootLogicalName.Trim().ToLowerInvariant();
        if (!SecureChildLineage.IsRoot(rootTable))
            throw new ArgumentOutOfRangeException(nameof(rootLogicalName), rootLogicalName, "Not a secure-root table.");

        // ── The Secure Record owner team, then the root ─────────────────────────────────────────────────────────────
        // An environment with no Secure Record owner team has no secure record and no isolated child: nothing to do.
        Entity? root;
        Guid secureTeamId;
        try
        {
            var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId, refusal);
            if (team.TeamId is not { } id)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.NotApplicable, mode, rootTable, rootId,
                    "this environment has no Secure Record owner team, so no record is secure");
            secureTeamId = id;

            var query = new QueryExpression(rootTable)
            {
                ColumnSet = new ColumnSet(OwningTeamColumn, OwningUserColumn, IsSecureColumn),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(rootTable + "id", ConditionOperator.Equal, rootId);
            root = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (root is null)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                    "the record does not exist");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {RootId}: the record or the Secure Record owner team could " +
                "not be read; nothing was decided.", rootTable, rootId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                "the record or the Secure Record owner team could not be read");
        }

        var rootIsolated = root.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id == secureTeamId;
        if (unsecuring && rootIsolated)
        {
            // The exemption is for a root whose move OFF the Secure team has landed; one still on it is not mid-unsecure.
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                "the record is still owned by the Secure Record owner team, so its children are not taken out of isolation");
        }

        var rootRef = new RecordOwnershipParent(rootTable, rootId);
        var pass = new Pass(this, mode, rootRef, rootIsolated, unsecuring, secureTeamId, ct);

        // ── The descendants ─────────────────────────────────────────────────────────────────────────────────────────
        try
        {
            await pass.LoadDescendantsAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {RootId}: its related records could not be read; nothing " +
                "was written.", rootTable, rootId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                "the record's related records could not be read");
        }

        return await pass.RunAsync().ConfigureAwait(false);
    }

    /// <summary>One pass's state; nothing outlives it.</summary>
    private sealed class Pass
    {
        private readonly SecureChildReconciler _owner;
        private readonly SecureChildReconcileMode _mode;
        private readonly RecordOwnershipParent _root;
        private readonly bool _rootIsolated;
        private readonly bool _unsecuring;
        private readonly Guid _secureTeamId;
        private readonly CancellationToken _ct;

        private readonly List<(int Level, Entity Row)> _descendants = new();
        private readonly Dictionary<(string Table, Guid Id), Entity> _byRef = new();
        private readonly Dictionary<string, int[]> _counts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SecureChildRowChange> _changes = new();

        public Pass(
            SecureChildReconciler owner, SecureChildReconcileMode mode, RecordOwnershipParent root, bool rootIsolated,
            bool unsecuring, Guid secureTeamId, CancellationToken ct)
        {
            _owner = owner;
            _mode = mode;
            _root = root;
            _rootIsolated = rootIsolated;
            _unsecuring = unsecuring;
            _secureTeamId = secureTeamId;
            _ct = ct;
        }

        private ILogger Log => _owner._logger;

        // Indexes into a table's counter array.
        private const int Examined = 0, AlreadyCorrect = 1, Changed = 2, WouldChange = 3, Untouched = 4, Refused = 5, Failed = 6;

        private void Count(string table, int slot)
        {
            if (!_counts.TryGetValue(table, out var c))
                _counts[table] = c = new int[7];
            c[slot]++;
        }

        /// <summary>
        /// Every descendant of the root through the lineage lookups, level by level, through rows of ANY owner, at most
        /// <see cref="SecureChildShareSynchronizer.MaxLineageDepth"/> levels; every column (the resolver reads a row's parents
        /// from whatever lookups it carries). Paged, chunked; an incomplete read throws.
        /// </summary>
        public async Task LoadDescendantsAsync()
        {
            var seen = new HashSet<(string, Guid)> { (_root.EntityLogicalName, _root.RecordId) };
            IReadOnlyList<(string Table, Guid Id)> frontier = new[] { (_root.EntityLogicalName, _root.RecordId) };

            for (var level = 1; level <= SecureChildShareSynchronizer.MaxLineageDepth && frontier.Count > 0; level++)
            {
                var idsByTable = frontier
                    .GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
                var next = new List<(string, Guid)>();

                foreach (var table in SecureChildLineage.Children.Values.OrderBy(t => t.LogicalName, StringComparer.Ordinal))
                {
                    var pairs = table.Lookups
                        .Where(l => idsByTable.ContainsKey(l.Value))
                        .SelectMany(l => idsByTable[l.Value].Select(id => (Column: l.Key, Id: id)))
                        .ToArray();

                    foreach (var chunk in pairs.Chunk(SecureChildShareSynchronizer.DescendantConditionsPerQuery))
                    {
                        var query = new QueryExpression(table.LogicalName) { ColumnSet = new ColumnSet(true), NoLock = true };
                        var anyParent = new FilterExpression(LogicalOperator.Or);
                        foreach (var column in chunk.GroupBy(p => p.Column, StringComparer.OrdinalIgnoreCase))
                            anyParent.AddCondition(column.Key, ConditionOperator.In, column.Select(p => (object)p.Id).ToArray());
                        query.Criteria.AddFilter(anyParent);

                        foreach (var entity in await ReadAllPagesAsync(table.LogicalName, query).ConfigureAwait(false))
                        {
                            var key = (table.LogicalName, entity.Id);
                            if (!seen.Add(key))
                                continue;
                            _descendants.Add((level, entity));
                            _byRef[key] = entity;
                            next.Add(key);
                        }
                    }
                }

                frontier = next;
            }
        }

        private async Task<IReadOnlyList<Entity>> ReadAllPagesAsync(string table, QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = SecureChildShareSynchronizer.PageSize, PageNumber = 1 };
            var rows = new List<Entity>();
            for (var page = 1; ; page++)
            {
                if (page > SecureChildShareSynchronizer.MaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had rows to read after {SecureChildShareSynchronizer.MaxPages} pages; a partial set " +
                        "of a record's related records is not reconciled.");

                var result = await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false);
                rows.AddRange(result.Entities);
                if (!result.MoreRecords)
                    return rows;

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        public async Task<SecureChildReconcileReport> RunAsync()
        {
            // 1. The rows the root's own Assign cascades to: the rule's owner for a child of the root (owner round 13 item 1).
            await PlaceCascadeRowsAsync().ConfigureAwait(false);

            // 2. Every descendant, shallowest first (a grandchild is decided after its parent moved).
            var leaving = new List<(string Table, Guid Id, bool WasIsolated)>();
            foreach (var (_, row) in _descendants
                         .OrderBy(d => d.Level)
                         .ThenBy(d => d.Row.LogicalName, StringComparer.Ordinal)
                         .ThenBy(d => d.Row.Id))
            {
                var outcome = await ReconcileRowAsync(row).ConfigureAwait(false);
                if (outcome is { } leave)
                    leaving.Add(leave);
            }

            SecureChildShareSyncResult? shares = null;
            var mirrorRevoked = 0;
            var mirrorIncomplete = 0;
            var faulted = false;

            if (_mode == SecureChildReconcileMode.Apply)
            {
                if (_rootIsolated)
                {
                    // 3a. INTO isolation: the root's sharees mirrored onto every Secure-team-owned child (task 149).
                    shares = await _owner._shares.SyncRootAsync(_root.EntityLogicalName, _root.RecordId, _ct).ConfigureAwait(false);
                }
                else if (leaving.Count > 0)
                {
                    // 3b. OUT of isolation: after the re-owns, the mirrored shares come off. A child that WAS isolated loses
                    // every direct share (all of them were the mirror); one found already ordinary during an unsecure loses
                    // only the root's sharees' shares (a resumed pass, or a child the root never isolated).
                    IReadOnlySet<DataversePrincipalRef>? rootSharees = null;
                    if (leaving.Any(l => !l.WasIsolated))
                        rootSharees = await RootShareesAsync().ConfigureAwait(false);

                    foreach (var (table, id, wasIsolated) in leaving)
                    {
                        if (!wasIsolated && rootSharees is null)
                        {
                            mirrorIncomplete++;
                            faulted = true;
                            continue;
                        }

                        var removal = await _owner._shares
                            .RemoveMirrorAsync(table, id, wasIsolated ? null : rootSharees, _ct).ConfigureAwait(false);
                        mirrorRevoked += removal.SharesRevoked;
                        if (!removal.IsComplete)
                        {
                            mirrorIncomplete++;
                            Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: its mirrored shares were not all removed " +
                                "({Status}: {Detail}).", table, id, removal.Status, removal.Detail);
                        }
                    }
                }
            }

            var tables = _counts
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new SecureChildTableCounts(
                    c.Key, c.Value[Examined], c.Value[AlreadyCorrect], c.Value[Changed], c.Value[WouldChange],
                    c.Value[Untouched], c.Value[Refused], c.Value[Failed]))
                .ToList();

            var rowsOk = tables.All(t => t.Refused == 0 && t.Failed == 0);
            var complete = rowsOk && !faulted && mirrorIncomplete == 0 && shares is not { IsComplete: false };
            var status = complete ? SecureChildReconcileStatus.Completed : SecureChildReconcileStatus.Incomplete;

            Log.Log(
                complete ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CHILD-RECONCILE] root={Table} {RootId} mode={Mode} isolated={Isolated} unsecuring={Unsecuring} " +
                "status={Status} examined={Examined} alreadyCorrect={AlreadyCorrect} changed={Changed} wouldChange={WouldChange} " +
                "untouched={Untouched} refused={Refused} failed={Failed} shares={Shares} mirrorRevoked={MirrorRevoked} " +
                "mirrorIncomplete={MirrorIncomplete}",
                _root.EntityLogicalName, _root.RecordId, _mode, _rootIsolated, _unsecuring, status,
                tables.Sum(t => t.Examined), tables.Sum(t => t.AlreadyCorrect), tables.Sum(t => t.Changed),
                tables.Sum(t => t.WouldChange), tables.Sum(t => t.Untouched), tables.Sum(t => t.Refused),
                tables.Sum(t => t.Failed), shares?.Status.ToString() ?? "-", mirrorRevoked, mirrorIncomplete);

            return new SecureChildReconcileReport(
                status, _mode, _root.EntityLogicalName, _root.RecordId, _rootIsolated, tables, _changes, shares,
                mirrorRevoked, mirrorIncomplete, null);
        }

        /// <summary>
        /// One descendant. Returns the row when it LEFT (or, mid-unsecure, sits outside) isolation and must have its mirrored
        /// shares removed; <c>WasIsolated</c> says whether it was owned by the Secure team when this pass read it.
        /// </summary>
        private async Task<(string Table, Guid Id, bool WasIsolated)?> ReconcileRowAsync(Entity row)
        {
            var table = row.LogicalName;
            Count(table, Examined);
            var previous = OwnerOf(row);
            var wasIsolated = previous is { Kind: DataversePrincipalKind.Team } p && p.Id == _secureTeamId;

            RecordOwnerResolution resolution;
            try
            {
                resolution = await _owner._ownership.ResolveOwnerAsync(ContextFor(row), _ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: its owner could not be decided (a read failed); " +
                    "it is left as it is.", table, row.Id);
                Count(table, Failed);
                _changes.Add(new(table, row.Id, previous, null, SecureChildRowOutcome.Failed, "its owner could not be decided"));
                return null;
            }

            if (resolution.IsRefused)
            {
                Count(table, Refused);
                _changes.Add(new(table, row.Id, previous, null, SecureChildRowOutcome.Refused,
                    $"{resolution.RefusalCode}: {resolution.Reason}"));
                Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: the ownership rule refused ({Code}: {Reason}); it is " +
                    "left as it is.", table, row.Id, resolution.RefusalCode, resolution.Reason);
                return null;
            }

            if (!resolution.IsOwned)
            {
                Count(table, Untouched); // names no parent the rule reads: it keeps its owner
                return null;
            }

            var target = resolution.OwningTeamId!.Value;
            var targetIsSecure = target == _secureTeamId;

            if (previous == DataversePrincipalRef.Team(target))
            {
                Count(table, AlreadyCorrect);
                return !targetIsSecure && _unsecuring ? (table, row.Id, false) : null;
            }

            // Only a move across the isolation boundary is this transition's.
            if (!targetIsSecure && !wasIsolated)
            {
                Count(table, Untouched);
                return !targetIsSecure && _unsecuring ? (table, row.Id, false) : null;
            }

            if (_mode == SecureChildReconcileMode.ReportOnly)
            {
                Count(table, WouldChange);
                _changes.Add(new(table, row.Id, previous, target, SecureChildRowOutcome.WouldChange, null));
                Log.LogInformation("[SECURE-CHILD-RECONCILE] plan: {Table} {Id} owner {Previous} -> team {Target} (report-only).",
                    table, row.Id, Describe(previous), target);
                return null;
            }

            // Reversal evidence BEFORE the write (constraint "every assign").
            Log.LogInformation("[SECURE-CHILD-RECONCILE] reassign: {Table} {Id} owner {Previous} -> team {Target}.",
                table, row.Id, Describe(previous), target);

            var moved = await AssignAsync(table, row.Id, target).ConfigureAwait(false);
            if (moved is null)
            {
                Count(table, Changed);
                _changes.Add(new(table, row.Id, previous, target, SecureChildRowOutcome.Changed, null));
                // (A deeper row's decision reads this row FRESH through the resolver, so it sees the new owner — which is
                // why rows are taken shallowest first.)
                return targetIsSecure ? null : (table, row.Id, wasIsolated);
            }

            Count(table, Failed);
            _changes.Add(new(table, row.Id, previous, target, SecureChildRowOutcome.Failed, moved));
            return null;
        }

        /// <summary>
        /// Assigns the row to the team in its own update and reads it back. <c>null</c> on success, else what went wrong. A
        /// write that reports failure may still have landed, so the read-back decides (the task 133 restore rule).
        /// </summary>
        private async Task<string?> AssignAsync(string table, Guid id, Guid teamId)
        {
            string? writeFault = null;
            try
            {
                await _owner._dataverse.UpdateAsync(
                    table, id, new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", teamId) }, _ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                writeFault = ex.Message;
                Log.LogError(ex, "[SECURE-CHILD-RECONCILE] Dataverse refused re-owning {Table} {Id} to team {Team}.",
                    table, id, teamId);
            }

            try
            {
                var query = new QueryExpression(table) { ColumnSet = new ColumnSet(OwningTeamColumn), TopCount = 1, NoLock = true };
                query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                var after = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();
                if (after?.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id == teamId)
                    return null;

                return writeFault is null
                    ? "the re-own was accepted but did not read back"
                    : $"Dataverse refused the re-own ({writeFault})";
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id} could not be read back after its re-own.", table, id);
                return "the re-own could not be read back";
            }
        }

        /// <summary>
        /// The resolver's input for an existing row — the reparent's input (task 146): every ownership-parent lookup it
        /// carries; a message in a record thread also takes that thread's filing (S6); a row naming no parent keeps its
        /// owner; and, mid-unsecure, the root is named as the one whose flag is not a failed provisioning.
        /// </summary>
        private RecordOwnershipContext ContextFor(Entity row)
        {
            var parents = RecordOwnershipContext.ParentsOf(row.Attributes).ToList();
            if (string.Equals(row.LogicalName, CommunicationTable, StringComparison.OrdinalIgnoreCase)
                && row.GetAttributeValue<EntityReference>(ThreadLookupOnCommunication) is { Id: var threadId } && threadId != Guid.Empty
                && _byRef.TryGetValue(("sprk_communicationthread", threadId), out var thread))
            {
                parents.AddRange(RecordOwnershipContext.ParentsOf(thread.Attributes));
            }

            return new RecordOwnershipContext
            {
                Parents = parents.Distinct().ToArray(),
                WhenUnfiled = UnfiledOwnership.KeepCreator,
                UnsecuringRoot = _unsecuring ? _root : null,
            };
        }

        /// <summary>
        /// The rows the root's own Assign cascades to (task 133's primitive — snapshot, then put each on an owner, read
        /// back): each takes the owner the rule gives a child filed under the root alone.
        /// </summary>
        private async Task PlaceCascadeRowsAsync()
        {
            if (AssignCascadeChildOwners.TablesFor(_root.EntityLogicalName).Count == 0)
                return; // a work assignment cascades nothing

            var snapshot = await AssignCascadeChildOwners.SnapshotAsync(
                _owner._webApi, _root.EntityLogicalName, _root.RecordId, _ct).ConfigureAwait(false);
            if (snapshot.Snapshot is not { } read)
            {
                var name = snapshot.FailedTable?.LogicalName ?? "sharepointdocumentlocation";
                Count(name, Examined);
                Count(name, Failed);
                _changes.Add(new(name, Guid.Empty, null, null, SecureChildRowOutcome.Failed,
                    $"the rows the record's Assign cascades to could not be read ({snapshot.Failure})"));
                Log.LogWarning(snapshot.Fault, "[SECURE-CHILD-RECONCILE] {Table} {RootId}: its {Cascade} rows could not be read " +
                    "({Failure}); they are left as they are.", _root.EntityLogicalName, _root.RecordId, name, snapshot.Failure);
                return;
            }

            if (read.Children.Count == 0)
                return;

            RecordOwnerResolution resolution;
            try
            {
                resolution = await _owner._ownership.ResolveOwnerAsync(
                    new RecordOwnershipContext
                    {
                        Parents = new[] { _root },
                        WhenUnfiled = UnfiledOwnership.KeepCreator,
                        UnsecuringRoot = _unsecuring ? _root : null,
                    },
                    _ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] The owner of {Table} {RootId}'s cascade rows could not be decided.",
                    _root.EntityLogicalName, _root.RecordId);
                resolution = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUnresolved, "a read failed");
            }

            foreach (var child in read.Children)
            {
                Count(child.LogicalName, Examined);
                if (!resolution.IsOwned)
                {
                    Count(child.LogicalName, Refused);
                    _changes.Add(new(child.LogicalName, child.Id, child.Owner, null, SecureChildRowOutcome.Refused,
                        $"{resolution.RefusalCode}: {resolution.Reason}"));
                    continue;
                }

                var target = resolution.OwningTeamId!.Value;
                if (child.Owner == DataversePrincipalRef.Team(target))
                {
                    Count(child.LogicalName, AlreadyCorrect);
                    continue;
                }

                // Only a transition moves these rows: into isolation, out of it, or — mid-unsecure — off the owning USER
                // the root's own Assign just cascaded them to (owner round 13 item 1). An ordinary record's rows on another
                // ordinary owner are not this pass's (a repeat unsecure on a record that was never secure changes nothing).
                var crossing = target == _secureTeamId
                    || child.Owner == DataversePrincipalRef.Team(_secureTeamId)
                    || _unsecuring;
                if (!crossing)
                {
                    Count(child.LogicalName, Untouched);
                    continue;
                }

                if (_mode == SecureChildReconcileMode.ReportOnly)
                {
                    Count(child.LogicalName, WouldChange);
                    _changes.Add(new(child.LogicalName, child.Id, child.Owner, target, SecureChildRowOutcome.WouldChange, null));
                    continue;
                }

                Log.LogInformation("[SECURE-CHILD-RECONCILE] reassign: {Table} {Id} owner {Previous} -> team {Target}.",
                    child.LogicalName, child.Id, Describe(child.Owner), target);

                // Task 133's restore, aimed at the rule's owner: read, assign only when different, read back.
                var placed = await AssignCascadeChildOwners.RestoreAsync(
                    _owner._webApi,
                    read with { Children = new[] { child with { Owner = DataversePrincipalRef.Team(target) } } },
                    Log, _ct).ConfigureAwait(false);
                var result = placed.Children.Single();
                if (result.IsBack)
                {
                    Count(child.LogicalName, result.Outcome == CascadeChildRestoreOutcome.AlreadyOwned ? AlreadyCorrect : Changed);
                    if (result.Outcome != CascadeChildRestoreOutcome.AlreadyOwned)
                        _changes.Add(new(child.LogicalName, child.Id, child.Owner, target, SecureChildRowOutcome.Changed, null));
                }
                else
                {
                    Count(child.LogicalName, Failed);
                    _changes.Add(new(child.LogicalName, child.Id, child.Owner, target, SecureChildRowOutcome.Failed,
                        $"{result.Outcome}; to place it by hand: {result.Child.RestoreCall}"));
                }
            }
        }

        /// <summary>The root's direct sharees (strict read), or <c>null</c> when they cannot be read.</summary>
        private async Task<IReadOnlySet<DataversePrincipalRef>?> RootShareesAsync()
        {
            try
            {
                var shares = await _owner._shares.ReadRootShareesAsync(_root.EntityLogicalName, _root.RecordId, _ct)
                    .ConfigureAwait(false);
                return shares;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] The shares on {Table} {RootId} could not be read; children found " +
                    "outside isolation keep their shares this pass.", _root.EntityLogicalName, _root.RecordId);
                return null;
            }
        }

        private static DataversePrincipalRef? OwnerOf(Entity row) =>
            row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty
                ? DataversePrincipalRef.Team(team)
                : row.GetAttributeValue<EntityReference>(OwningUserColumn)?.Id is { } user && user != Guid.Empty
                    ? DataversePrincipalRef.User(user)
                    : null;

        private static string Describe(DataversePrincipalRef? owner) =>
            owner is { } o ? $"{o.Kind.ToEntitySet()}({o.Id:D})" : "(unknown)";
    }
}
