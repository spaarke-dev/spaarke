// unified-access-control-r2 task 149 (C10 part 2 — sharees; GitHub #1071). Ships together with task 146.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — IDataverseRecordShareService is the only POA client. Its callers share ROOTS (InternalShareEndpoints,
//       ProvisionProjectEndpoint, UnsecureProjectEndpoint), playbooks (PlaybookSharingService) and — the one other writer
//       on a CHILD table — DirectThreadAccessService: Read on a Direct thread it creates user-owned (never a secure child),
//       and Read on each message for its thread's participants, which since task 149 r1 it SKIPS for a message the Secure
//       Record Owners team owns (that message's shares are this synchronizer's alone). Nothing else mirrors a root's
//       shares onto its children, and no relationship cascades Share/Unshare/Reparent (live metadata, 2026-10-02:
//       NoCascade on every root→child relationship).
//   (2) Extension — Not inside IDataverseRecordShareService: it is a pass-through testing seam by its own remarks
//       (ADR-010), and the batched read this task needed WAS added there. Not inside InternalShareEndpoints: the same
//       logic serves provisioning and the scheduled reconcile (and tasks 147/148 call it). The ownership resolver
//       decides an owner BEFORE a row exists; mirroring needs the row, so it cannot live there either.
//   (3) Cost-of-doing-nothing — after task 146 every child of a secure project, matter or work assignment is owned by
//       the memberless Secure Record Owners team, so every internal user shared on the root — its creator included —
//       loses every document, event, to-do and communication on it in MDA and Office; and an MDA Share/Unshare of a
//       secure root never reaches its children (an Unshare that does not is an over-share).
//
// Round r3 (merged after task 143): the No Access guard (SecureShareNoAccessGuard, task 143's ONE write-time check) is
// a constructor dependency, consulted before every child grant or widening — no new component. It is registered SCOPED,
// so this synchronizer is scoped too (every consumer resolves it from a request or job scope).
//
// Placement (bff-extensions.md §A/§D; ADR-052): in the BFF. The fan-out runs inside the /share-user and /unshare-user
// request (the caller is told how many children were and were not updated); the safety net is an in-process scheduled
// job (SecureChildShareReconciliationJob, ADR-036). BFF identity, BFF domain code, low volume. No package, no endpoint.
// The in-request fan-out reads only the root's own descendants (a downward walk), so its cost does not grow with the
// secure volume of the rest of the environment; only the scheduled reconcile reads every secure child.

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

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
/// <param name="ChildrenInScope">Secure-team-owned children examined (under the root, or all of them), including those whose
/// filing could not be read — those also count as <see cref="ChildrenNotUpdated"/>, so that count never exceeds this one.</param>
/// <param name="ChildrenUpdated">Children whose shares were changed, and read back as the mirror.</param>
/// <param name="ChildrenUnchanged">Children whose shares already matched.</param>
/// <param name="ChildrenNotUpdated">Children not brought into line: their filing, a share read or a write failed. The next run
/// retries.</param>
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
/// What taking the mirrored shares off one child did (task 148, <see cref="SecureChildShareSynchronizer.RemoveMirrorAsync"/>):
/// <see cref="SecureChildShareSyncStatus.Completed"/> (none left), <see cref="SecureChildShareSyncStatus.NotApplicable"/>
/// (the child no longer exists), <see cref="SecureChildShareSyncStatus.Incomplete"/> (a share is left, or the child is still
/// isolated and was refused) or <see cref="SecureChildShareSyncStatus.Failed"/> (nothing could be decided).
/// </summary>
public sealed record SecureChildMirrorRemoval(SecureChildShareSyncStatus Status, int SharesRevoked, string? Detail)
{
    /// <summary>True when no mirrored share is left on the child (or it no longer exists).</summary>
    public bool IsComplete => Status is SecureChildShareSyncStatus.Completed or SecureChildShareSyncStatus.NotApplicable;
}

/// <summary>
/// The secure roots above a set of child rows (task 147, <see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>).
/// <see cref="SecureChildShareSyncStatus.Completed"/> means every row was walked. NotApplicable means the environment has
/// no Secure Record owner team. Failed means the team is ambiguous and nothing was decided.
/// </summary>
/// <param name="Status">How the walk ended.</param>
/// <param name="Roots">The distinct isolated roots found, as (table, id) ordered by table then id.</param>
/// <param name="Undetermined">Rows whose roots could not all be determined from the data, each with the reason: a missing
/// ancestor, a root flagged secure but not isolated, or a chain deeper than the walk.</param>
/// <param name="Detail">Why the walk was Failed or NotApplicable.</param>
/// <param name="UndeterminedRows">The rows of <paramref name="Undetermined"/>, as (table, id), in the same order — so a caller
/// can look at exactly those rows again (task 147 r1: the reconciliation job carries them to its next run).</param>
public sealed record SecureRootsAbove(
    SecureChildShareSyncStatus Status,
    IReadOnlyList<(string Table, Guid Id)> Roots,
    IReadOnlyList<string> Undetermined,
    string? Detail,
    IReadOnlyList<(string Table, Guid Id)>? UndeterminedRows = null)
{
    /// <summary>The undetermined rows as (table, id); empty when none.</summary>
    public IReadOnlyList<(string Table, Guid Id)> UndeterminedRowRefs => UndeterminedRows ?? Array.Empty<(string, Guid)>();
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
/// Share, never Assign (owner round 11 item 4: "ShareAccess is NOT mirrored onto children"). A child under SEVERAL secure
/// roots gets the INTERSECTION: a principal must be shared on every one of them, at the lowest rights (owner round 11
/// item 4, fail closed). Every direct share on the child outside the mirror is revoked; a wider one is narrowed; a
/// missing one is granted. Inherited-only POA rows (mask 0) are neither read as shares nor touched.</para>
/// <para><b>The No Access list</b> (task 143, owner Q4; wired here because this task merged second — AC6). Before any
/// child GRANT or WIDENING, every system user that <see cref="SecureShareNoAccessGuard"/> refuses for ANY of the child's
/// secure roots is dropped: walled, or the check could not be answered (ADR-003). Such a principal keeps only the
/// narrowing part of its change, and an unanswered check leaves the child not updated (retried next run). So a walled
/// user is never put on a child, even while their ROOT share still stands (the enforcer has not acted yet, or owner S5
/// kept it as the last reader). Removal stays the enforcer's: after it removes a root share it calls
/// <see cref="SyncRootAsync"/>. Teams are not asked about: an entry cannot name a team, and a team share is never the
/// wall's to remove (owner N2).</para>
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
/// request), after secure provisioning, after the No Access enforcer removes a root share, and on a short schedule
/// (<see cref="ReconcileAllAsync"/>, which catches creates, re-files, client-side writes and out-of-the-box MDA sharing of
/// a secure root). Owner round 11 item 2 accepted that schedule as the mechanism: at most 2 minutes for MDA Share/Unshare
/// and for new or re-filed children, the job shipping with writes on, and no table-wide platform cascade.</para>
/// <para><b>What each trigger reads.</b> <see cref="SyncRootAsync"/> first proves from the record's own row that it can be
/// secure (an ordinary record is answered without consulting the Secure Record business unit), then finds its descendants
/// by walking the lineage lookups DOWNWARD from it — through the same rows the upward walk follows (Secure-team-owned and
/// user-owned, never ordinary-team-owned), at most <see cref="MaxLineageDepth"/> levels — so a request reads the root's own
/// subtree, never the rest of the environment. Each candidate's secure roots are still decided by the upward walk (a
/// candidate also under a second secure root gets the intersection). <see cref="ReconcileAllAsync"/> reads every
/// Secure-team-owned row of every child table: it is the net for the writers the endpoints never see.</para>
/// </remarks>
public sealed class SecureChildShareSynchronizer
{
    /// <summary>Rows per page of each child-table read.</summary>
    internal const int PageSize = 5000;

    /// <summary>Page ceiling per child table (100,000 rows); past it the run fails rather than mirroring part of a table.</summary>
    internal const int MaxPages = 20;

    /// <summary>How many lookups the upward walk follows from a child before its roots count as undetermined.</summary>
    internal const int MaxLineageDepth = 6;

    /// <summary>Parent ids per query of the scoped downward walk — far below SQL Server's parameter ceiling.</summary>
    internal const int DescendantConditionsPerQuery = 200;

    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";
    private const string IsSecureColumn = "sprk_issecure";
    private const int OwnerTeamType = SecureRecordOwnerTeam.OwnerTeamType;

    private readonly IGenericEntityService _dataverse;
    private readonly IDataverseRecordShareService _recordShare;
    private readonly SecureShareNoAccessGuard _noAccessGuard;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureChildShareSynchronizer> _logger;

    public SecureChildShareSynchronizer(
        IGenericEntityService dataverse,
        IDataverseRecordShareService recordShare,
        SecureShareNoAccessGuard noAccessGuard,
        IConfiguration configuration,
        ILogger<SecureChildShareSynchronizer> logger)
    {
        _dataverse = dataverse;
        _recordShare = recordShare;
        _noAccessGuard = noAccessGuard;
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

    /// <summary>
    /// Task 147 r1: whether <paramref name="teamId"/> is this environment's Secure Record owner team (the browser re-file
    /// route asks before it takes the mirror off a row it moved out). An ambiguous or absent team answers no; a Dataverse
    /// fault propagates.
    /// </summary>
    public async Task<bool> IsSecureOwnerTeamAsync(Guid teamId, CancellationToken ct) =>
        teamId != Guid.Empty
        && (await ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false)).TeamId == teamId;

    /// <summary>Brings EVERY secure child in the environment into line — the scheduled reconcile.</summary>
    public Task<SecureChildShareSyncResult> ReconcileAllAsync(CancellationToken ct) => RunAsync(scope: null, ct);

    /// <summary>
    /// unified-access-control-r2 task 147 r1 (owner round 28 item 1): mirrors ONE child — the row a browser writer just
    /// created or re-filed through the BFF — inline, so the people the secure record is shared with see it at once rather
    /// than at the next two-minute reconcile (owner round 11 item 2 remains the backstop). The same mirror the scheduled
    /// reconcile computes for that row: the INTERSECTION of its secure roots' sharees, never Share or Assign, a No Access
    /// entry honoured. A row that is not Secure-team-owned answers <see cref="SecureChildShareSyncStatus.NotApplicable"/>
    /// and nothing is written; a fault or an ambiguous Secure team writes nothing (<see cref="SecureChildShareSyncStatus.Failed"/>).
    /// </summary>
    public async Task<SecureChildShareSyncResult> SyncChildAsync(string childLogicalName, Guid childId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childLogicalName);
        if (!SecureChildLineage.Children.TryGetValue(childLogicalName.Trim(), out var table))
            throw new ArgumentOutOfRangeException(nameof(childLogicalName), childLogicalName, "Not a secure-child table.");

        Guid secureTeamId;
        try
        {
            var team = await ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return SecureChildShareSyncResult.Failed(refusal);
            if (team.TeamId is not { } id)
                return SecureChildShareSyncResult.NotApplicable("this environment has no Secure Record owner team, so no record is secure");
            secureTeamId = id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-SHARES] The Secure Record owner team could not be read; {Table} {Id} was not mirrored.",
                table.LogicalName, childId);
            return SecureChildShareSyncResult.Failed("the Secure Record owner team could not be read");
        }

        var run = new Run(this, secureTeamId, ct);
        try
        {
            if (!await run.LoadSecureChildAsync(table, childId).ConfigureAwait(false))
            {
                return SecureChildShareSyncResult.NotApplicable(
                    $"{table.LogicalName} {childId:D} is not owned by the Secure Record owner team, so it is not mirrored");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-SHARES] {Table} {Id} could not be read; it was not mirrored.", table.LogicalName, childId);
            return SecureChildShareSyncResult.Failed("the record could not be read");
        }

        return await run.SynchronizeAsync(scope: null).ConfigureAwait(false);
    }

    /// <summary>
    /// unified-access-control-r2 task 148 — takes the mirrored shares off ONE child that a pass has just re-owned OUT of the
    /// Secure Record owner team (owner round 11 item 3: "148 re-owns the children, then calls <see cref="SyncRootAsync"/>";
    /// after an unsecure the root's share set is going away, so "in line with the root" means "carrying none of its
    /// sharees"). EVERY direct share on the child is revoked: the child was owned by the Secure team when the pass began, and
    /// every direct share on a Secure-team-owned child is this synchronizer's mirror (it revokes anything else). Owner round
    /// 22: a child that was NOT isolated when the pass began is never given to this method — a share on an ordinary,
    /// never-isolated child is its user's own intent and is kept.
    /// </summary>
    /// <param name="childLogicalName">One of the codified child tables (<see cref="SecureChildLineage"/>).</param>
    /// <param name="childId">The child.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// <para><b>Ownership first, always</b> (the unsecure endpoint's own ordering rule). A child still owned by the Secure
    /// team is REFUSED: removing its shares would leave it readable by nobody. So a removal can only ever follow the
    /// re-own that made the child reachable through its business unit.</para>
    /// <para><b>Fail closed.</b> A child whose owner or shares cannot be read is not written (<see
    /// cref="SecureChildShareSyncStatus.Failed"/>); a revoke that fails, or a share still present on the read-back, is
    /// <see cref="SecureChildShareSyncStatus.Incomplete"/>. Removing access is never the unsafe direction, so nothing is
    /// re-checked before a revoke.</para>
    /// </remarks>
    public async Task<SecureChildMirrorRemoval> RemoveMirrorAsync(string childLogicalName, Guid childId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childLogicalName);
        if (!SecureChildLineage.Children.TryGetValue(childLogicalName, out var table))
            throw new ArgumentOutOfRangeException(nameof(childLogicalName), childLogicalName, "Not a secure-child table.");

        var child = new RowRef(table.LogicalName, childId);
        Guid? owningTeam;
        try
        {
            var query = new QueryExpression(table.LogicalName)
            {
                ColumnSet = new ColumnSet(OwningTeamColumn),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(table.IdColumn, ConditionOperator.Equal, childId);
            var row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (row is null)
                return new SecureChildMirrorRemoval(SecureChildShareSyncStatus.NotApplicable, 0, $"{child} no longer exists");
            owningTeam = row.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;

            var team = await ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Failed, 0, refusal);
            if (team.TeamId is { } secureTeam && owningTeam == secureTeam)
            {
                _logger.LogWarning(
                    "[SECURE-CHILD-SHARES] {Child} is still owned by the Secure Record owner team; its shares are NOT removed " +
                    "(that would leave it readable by nobody). Re-own it first.", child);
                return new SecureChildMirrorRemoval(
                    SecureChildShareSyncStatus.Incomplete, 0, $"{child} is still owned by the Secure Record owner team");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-CHILD-SHARES] {Child} could not be read; its shares were not removed.", child);
            return new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Failed, 0, $"{child} could not be read");
        }

        IReadOnlyList<DataversePrincipalAccess> shares;
        try
        {
            shares = await _recordShare.GetPrincipalAccessOrThrowAsync(table.LogicalName, childId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-CHILD-SHARES] The shares on {Child} could not be read; none was removed.", child);
            return new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Failed, 0, $"the shares on {child} could not be read");
        }

        var targets = shares.Where(s => s.AccessRightsMask != 0).Select(s => s.Principal).Distinct().ToList();
        if (targets.Count == 0)
            return new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Completed, 0, null);

        var revoked = 0;
        var failed = false;
        foreach (var principal in targets)
        {
            try
            {
                await _recordShare.RevokeAccessAsync(table.EntitySet, childId, principal, ct).ConfigureAwait(false);
                revoked++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failed = true;
                _logger.LogWarning(ex, "[SECURE-CHILD-SHARES] revoke for {Principal} on {Child} failed (unsecure).", principal, child);
            }
        }

        try
        {
            var after = await _recordShare.GetPrincipalAccessOrThrowAsync(table.LogicalName, childId, ct).ConfigureAwait(false);
            if (after.Any(s => s.AccessRightsMask != 0))
            {
                _logger.LogWarning("[SECURE-CHILD-SHARES] {Child} still carries a mirrored share after the unsecure removal.", child);
                failed = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-CHILD-SHARES] {Child} could not be read back after the unsecure removal.", child);
            failed = true;
        }

        return failed
            ? new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Incomplete, revoked, $"not every mirrored share on {child} was removed")
            : new SecureChildMirrorRemoval(SecureChildShareSyncStatus.Completed, revoked, null);
    }

    /// <summary>
    /// unified-access-control-r2 task 147: the secure roots that the given child rows sit under. These are the records whose
    /// reconcile pass (<see cref="SecureChildReconciler.ReconcileAsync"/>) brings those rows into line. The upward walk is
    /// the same one the mirror uses (<c>LineageOfAsync</c>): through Secure-team-owned and user-owned rows, never through an
    /// ordinary team's, at most <see cref="MaxLineageDepth"/> levels. That is the ownership resolver's own rule, so a row is
    /// placed under a root here exactly when the resolver would give it the Secure team. The rows' own owners do not matter.
    /// Each row is passed as read, carrying its lineage lookups (<see cref="SecureChildLineage.Table.Lookups"/>) and owner
    /// columns. A lookup the caller did not read counts as absent, so it can only leave a record out.
    /// </summary>
    /// <remarks>
    /// The secure-child reconciliation job's recent-changes pass calls this (task 147). That pass is the L4 net for children
    /// written outside the product (out-of-the-box forms, quick create, grid edits, imports, flows) and for every client
    /// writer still on <c>Xrm.WebApi</c>. Fail closed: if the Secure Record owner team is ambiguous, the answer is
    /// <see cref="SecureChildShareSyncStatus.Failed"/>, and a Dataverse fault propagates. Either way the caller decides
    /// nothing. A row whose roots cannot all be determined is listed in <see cref="SecureRootsAbove.Undetermined"/>, never
    /// silently dropped.
    /// </remarks>
    public async Task<SecureRootsAbove> SecureRootsAboveAsync(IReadOnlyCollection<Entity> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (var row in rows)
        {
            if (!SecureChildLineage.IsChild(row.LogicalName))
                throw new ArgumentOutOfRangeException(nameof(rows), row.LogicalName, "Not a secure-child table.");
        }

        var team = await ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
        if (team.Refusal is { } refusal)
            return new SecureRootsAbove(SecureChildShareSyncStatus.Failed, Array.Empty<(string, Guid)>(), Array.Empty<string>(), refusal);
        if (team.TeamId is not { } secureTeamId)
        {
            return new SecureRootsAbove(SecureChildShareSyncStatus.NotApplicable, Array.Empty<(string, Guid)>(),
                Array.Empty<string>(), "this environment has no Secure Record owner team, so no record is secure");
        }

        var run = new Run(this, secureTeamId, ct);
        var roots = new HashSet<RowRef>();
        var undetermined = new List<string>();
        var undeterminedRows = new List<(string Table, Guid Id)>();
        foreach (var entity in rows.DistinctBy(r => (r.LogicalName.ToLowerInvariant(), r.Id)))
        {
            var (reference, lineage) = await run.LineageOfReadRowAsync(entity).ConfigureAwait(false);
            roots.UnionWith(lineage.SecureRoots);
            if (lineage.Undetermined is { } why)
            {
                undetermined.Add($"{reference}: {why}");
                undeterminedRows.Add((reference.Table, reference.Id));
            }
        }

        return new SecureRootsAbove(
            SecureChildShareSyncStatus.Completed,
            roots.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).Select(r => (r.Table, r.Id)).ToList(),
            undetermined,
            null,
            undeterminedRows);
    }

    private async Task<SecureChildShareSyncResult> RunAsync(RowRef? scope, CancellationToken ct)
    {
        // A scoped run first proves from the record's OWN row that it can be secure, so an ordinary record is answered
        // without consulting the Secure Record business unit — whose ambiguity, or a fault reading it, is no reason to tell
        // a caller that an ordinary record's related records "could not be read".
        RootFacts? scopeFacts = null;
        if (scope is { } scopeRoot)
        {
            try
            {
                scopeFacts = await ReadRootFactsAsync(_dataverse, scopeRoot, ct).ConfigureAwait(false);
                if (scopeFacts?.OwningTeam is not { } scopeOwner
                    || await CannotBeSecureOwnerTeamAsync(_dataverse, _configuration, scopeOwner, ct).ConfigureAwait(false))
                {
                    return SecureChildShareSyncResult.NotApplicable(
                        $"{scopeRoot.Table} {scopeRoot.Id:D} is not a secure record, so its children are not mirrored");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SECURE-CHILD-SHARES] {Root} could not be read; nothing was synchronized.", scopeRoot);
                return SecureChildShareSyncResult.Failed("the record could not be read");
            }
        }

        Guid secureTeamId;
        try
        {
            var team = await ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct).ConfigureAwait(false);
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
                if (scopeFacts!.OwningTeam != secureTeamId)
                {
                    return SecureChildShareSyncResult.NotApplicable(
                        $"{root.Table} {root.Id:D} is not a secure record, so its children are not mirrored");
                }

                run.Remember(root, scopeFacts);
                await run.LoadSecureChildrenUnderAsync(root).ConfigureAwait(false);
            }
            else
            {
                await run.LoadSecureChildrenAsync().ConfigureAwait(false);
            }
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
    /// are secure). A Dataverse fault propagates.
    /// </summary>
    /// <remarks>Also used by <c>DirectThreadAccessService</c> (task 149 r1), which must not grant a thread participant Read
    /// on a message whose shares this synchronizer owns.</remarks>
    internal static async Task<(Guid? TeamId, string? Refusal)> ResolveSecureOwnerTeamAsync(
        IGenericEntityService dataverse, IConfiguration configuration, CancellationToken ct)
    {
        var buQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 2, NoLock = true };
        buQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SecureRecordOwnerTeam.BusinessUnitName(configuration));
        var businessUnits = (await dataverse.RetrieveMultipleAsync(buQuery, ct).ConfigureAwait(false)).Entities;
        if (businessUnits.Count > 1)
            return (null, "more than one business unit carries the Secure Record name");
        if (businessUnits.Count == 0 || businessUnits[0].Id == Guid.Empty)
            return (null, null);

        var teamQuery = new QueryExpression("team") { ColumnSet = new ColumnSet("teamid"), TopCount = 2, NoLock = true };
        teamQuery.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, businessUnits[0].Id);
        teamQuery.Criteria.AddCondition("name", ConditionOperator.Equal, SecureRecordOwnerTeam.OwnerTeamName(configuration));
        teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);
        teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, false);
        var teams = (await dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false)).Entities;
        if (teams.Count > 1)
            return (null, "more than one Secure Record owner team carries the configured name");
        return teams.Count == 0 || teams[0].Id == Guid.Empty ? (null, null) : (teams[0].Id, null);
    }

    /// <summary>
    /// <c>true</c> when the team's OWN row proves it is not the Secure Record owner team: another name, its business
    /// unit's default team, or not an Owner team — none of which <see cref="ResolveSecureOwnerTeamAsync"/> can ever
    /// select. <c>false</c> means "it could be" (the full resolution decides), including a team row that is not found. A
    /// Dataverse fault propagates.
    /// </summary>
    /// <remarks>Names compare trimmed and case-insensitively, as Dataverse's own <c>name</c> equality does. Any doubt
    /// answers <c>false</c>, which costs only the full resolution — never a wrong "not secure".</remarks>
    internal static async Task<bool> CannotBeSecureOwnerTeamAsync(
        IGenericEntityService dataverse, IConfiguration configuration, Guid teamId, CancellationToken ct)
    {
        var query = new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("name", "isdefault", "teamtype"),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        var team = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        if (team is null)
            return false;

        if (team.GetAttributeValue<string>("name") is { } name
            && !string.Equals(name.Trim(), SecureRecordOwnerTeam.OwnerTeamName(configuration).Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        if (team.GetAttributeValue<bool?>("isdefault") == true)
            return true;
        return team.GetAttributeValue<OptionSetValue>("teamtype") is { } type && type.Value != OwnerTeamType;
    }

    /// <summary>A root's owner team and flag, or <c>null</c> when the row does not exist. A fault propagates.</summary>
    private static async Task<RootFacts?> ReadRootFactsAsync(IGenericEntityService dataverse, RowRef root, CancellationToken ct)
    {
        var query = new QueryExpression(root.Table)
        {
            ColumnSet = new ColumnSet(OwningTeamColumn, IsSecureColumn),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition(root.Table + "id", ConditionOperator.Equal, root.Id);
        var entity = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();

        // An EMPTY sprk_issecure fails CLOSED (task 150, round 17 item 3), as in the ownership resolver: since the task 150
        // backfill every row holds true or false and the column is field-secured, so empty means this identity lost its
        // field-level Read and the real value was masked. Read as flagged: a root that is not isolated then leaves its
        // child's lineage UNDETERMINED (shares only narrowed, never granted) instead of contributing nothing.
        return entity is null
            ? null
            : new RootFacts(
                entity.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id is { } team && team != Guid.Empty ? team : null,
                entity.GetAttributeValue<bool?>(IsSecureColumn) ?? true);
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
        private readonly Dictionary<(RowRef Root, Guid User), SecureShareWallOutcome> _walls = new();

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
                };
                query.Criteria.AddCondition(OwningTeamColumn, ConditionOperator.Equal, _secureTeamId);

                foreach (var entity in await ReadAllPagesAsync(table, query).ConfigureAwait(false))
                {
                    var row = ToRow(table, entity);
                    _secureChildren[row.Ref] = row;
                }
            }
        }

        /// <summary>
        /// Task 147 r1: ONE row, read with its lineage lookups and owner. True (and loaded as the run's only secure child)
        /// when it is Secure-team-owned; false when it is not, or does not exist. A fault propagates.
        /// </summary>
        public async Task<bool> LoadSecureChildAsync(SecureChildLineage.Table table, Guid id)
        {
            var query = new QueryExpression(table.LogicalName)
            {
                ColumnSet = new ColumnSet(table.Lookups.Keys.Append(OwningTeamColumn).Append(OwningUserColumn).ToArray()),
                NoLock = true,
            };
            query.Criteria.AddCondition(table.IdColumn, ConditionOperator.Equal, id);
            var entity = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (entity is null)
                return false;

            var row = ToRow(table, entity);
            if (row.OwningTeam != _secureTeamId)
                return false;

            _secureChildren[row.Ref] = row;
            return true;
        }

        /// <summary>
        /// The Secure-team-owned DESCENDANTS of one root (the scoped run): the lineage lookups walked downward, level by
        /// level, through exactly the rows the upward walk follows — Secure-team-owned, and user-owned looked through, never
        /// an ordinary team's — for at most <see cref="MaxLineageDepth"/> levels, which is as deep as the upward walk can
        /// reach a root from. One query per child table per level (its lookups into the previous level, OR-ed, chunked),
        /// paged; an incomplete read throws. Rows elsewhere in the environment are never read.
        /// </summary>
        public async Task LoadSecureChildrenUnderAsync(RowRef root)
        {
            var seen = new HashSet<RowRef> { root };
            IReadOnlyList<RowRef> frontier = new[] { root };

            for (var level = 1; level <= MaxLineageDepth && frontier.Count > 0; level++)
            {
                var idsByTable = frontier
                    .GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToArray(), StringComparer.OrdinalIgnoreCase);
                var next = new List<RowRef>();

                foreach (var table in SecureChildLineage.Children.Values)
                {
                    // Every (lookup column, parent id) pair of this table that points into the previous level.
                    var pairs = table.Lookups
                        .Where(l => idsByTable.ContainsKey(l.Value))
                        .SelectMany(l => idsByTable[l.Value].Select(id => (Column: l.Key, Id: id)))
                        .ToArray();

                    foreach (var chunk in pairs.Chunk(DescendantConditionsPerQuery))
                    {
                        var query = new QueryExpression(table.LogicalName)
                        {
                            ColumnSet = new ColumnSet(table.Lookups.Keys.Append(OwningTeamColumn).Append(OwningUserColumn).ToArray()),
                            NoLock = true,
                        };
                        var anyParent = new FilterExpression(LogicalOperator.Or);
                        foreach (var column in chunk.GroupBy(p => p.Column, StringComparer.OrdinalIgnoreCase))
                            anyParent.AddCondition(column.Key, ConditionOperator.In, column.Select(p => (object)p.Id).ToArray());
                        query.Criteria.AddFilter(anyParent);

                        foreach (var entity in await ReadAllPagesAsync(table, query).ConfigureAwait(false))
                        {
                            var row = ToRow(table, entity);
                            if (!seen.Add(row.Ref))
                                continue;

                            if (row.OwningTeam == _secureTeamId)
                            {
                                _secureChildren[row.Ref] = row;
                                next.Add(row.Ref);
                            }
                            else
                            {
                                // Kept for the upward walk; walked through only when the upward walk would follow it.
                                _otherRows[row.Ref] = row;
                                if (row.OwningTeam is null && row.UserOwned)
                                    next.Add(row.Ref);
                            }
                        }
                    }
                }

                frontier = next;
            }
        }

        /// <summary>Every page of one child-table query; past <see cref="MaxPages"/> it throws rather than read part of it.</summary>
        private async Task<IReadOnlyList<Entity>> ReadAllPagesAsync(SecureChildLineage.Table table, QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
            var rows = new List<Entity>();
            for (var page = 1; ; page++)
            {
                if (page > MaxPages)
                    throw new InvalidOperationException(
                        $"{table.LogicalName} still had rows to read after {MaxPages} pages of {PageSize}; " +
                        "mirroring part of a table is not attempted.");

                var result = await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false);
                rows.AddRange(result.Entities);

                if (!result.MoreRecords)
                    return rows;

                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        /// <summary>Seeds the run's cache with a root already read (the scoped run's own root).</summary>
        public void Remember(RowRef root, RootFacts? facts) => _roots[root] = facts;

        /// <summary>A root's owner and flag, or <c>null</c> when the row does not exist. A fault propagates.</summary>
        public async Task<RootFacts?> RootAsync(RowRef root)
        {
            if (_roots.TryGetValue(root, out var cached))
                return cached;

            var facts = await ReadRootFactsAsync(_owner._dataverse, root, _ct).ConfigureAwait(false);
            _roots[root] = facts;
            return facts;
        }

        /// <summary>A child row not already loaded (an intermediate, or a Secure-team-owned parent outside a scoped run's
        /// descendants), or <c>null</c> when it does not exist.</summary>
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

        /// <summary>
        /// Task 147: the secure roots above ONE child row the caller has already read, whatever that row's own owner is. A
        /// fault propagates.
        /// </summary>
        public async Task<(RowRef Reference, Lineage Lineage)> LineageOfReadRowAsync(Entity entity)
        {
            var row = ToRow(SecureChildLineage.Children[entity.LogicalName], entity);
            return (row.Ref, await LineageOfAsync(row).ConfigureAwait(false));
        }

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
            var faulted = 0;

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
                    // A fault is not an answer: nothing is written on the child, and it counts as examined AND not updated
                    // (so "N of M" never has N > M). In a scoped run every candidate was found BELOW the root by the
                    // downward walk, so the fault is about one of the root's own related records.
                    Log.LogWarning(ex, "[SECURE-CHILD-SHARES] The filing of {Child} could not be read; it is not synchronized.", row.Ref);
                    faulted++;
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
                scope?.ToString() ?? "all", status, inScope.Count + faulted, updated, unchanged, notUpdated, held, outside,
                _granted, _changed, _revoked);

            return new SecureChildShareSyncResult(
                status, inScope.Count + faulted, updated, unchanged, notUpdated, held, outside, _granted, _changed, _revoked, null);
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
                    // Nothing is added from a stale read. A MIXED change (it adds some rights and removes others) keeps its
                    // narrowing part — the rights the roots no longer give still go — and only its widening part is
                    // dropped (task 149 r3, verifier finding 2).
                    grants.Clear();
                    modifies = NarrowingPartOnly(modifies, have, _ => true, revokes);
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

            // Task 143 (AC6; this task merged second): a system user the No Access list walls off ANY of the child's secure
            // roots is never given a right on the child — not even while their ROOT share still stands (the enforcer has not
            // acted yet, or kept it under owner S5). Asked only for what ADDS a right; such a principal keeps only the
            // narrowing part of its change. A check that cannot be answered refuses too (ADR-003), and the child is then
            // not updated, so the next run asks again.
            var adding = grants.Select(g => g.Principal)
                .Concat(modifies.Where(m => (m.Mask & ~have[m.Principal]) != 0).Select(m => m.Principal))
                .Where(p => p.Kind == DataversePrincipalKind.SystemUser)
                .Distinct()
                .ToList();
            if (adding.Count > 0)
            {
                var refused = new HashSet<DataversePrincipalRef>();
                foreach (var principal in adding)
                {
                    var wall = await WallAsync(lineage, principal.Id).ConfigureAwait(false);
                    if (wall is SecureShareWallOutcome.Walled or SecureShareWallOutcome.Unverifiable)
                    {
                        refused.Add(principal);
                        failed |= wall == SecureShareWallOutcome.Unverifiable;
                        Log.LogWarning(
                            "[SECURE-CHILD-SHARES] {Principal} is {Wall} for a secure root of {Child}; nothing is added for " +
                            "them there.",
                            principal,
                            wall == SecureShareWallOutcome.Walled ? "on the No Access list" : "not verifiable against the No Access list",
                            row.Ref);
                    }
                }

                if (refused.Count > 0)
                {
                    grants.RemoveAll(g => refused.Contains(g.Principal));
                    modifies = NarrowingPartOnly(modifies, have, refused.Contains, revokes);
                }
            }

            if (revokes.Count == 0 && modifies.Count == 0 && grants.Count == 0)
                return failed ? ChildOutcome.NotUpdated : heldBack ? ChildOutcome.Held : ChildOutcome.Unchanged;

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

        /// <summary>
        /// Each change <paramref name="restrict"/> selects, cut down to the part that only REMOVES rights: its mask AND what
        /// the share already carries. A change that would only add is dropped; one left with no Read becomes a revoke
        /// (added to <paramref name="revokes"/>). Changes not selected are kept as they are.
        /// </summary>
        private static List<(DataversePrincipalRef Principal, int Mask)> NarrowingPartOnly(
            IEnumerable<(DataversePrincipalRef Principal, int Mask)> modifies,
            IReadOnlyDictionary<DataversePrincipalRef, int> have,
            Func<DataversePrincipalRef, bool> restrict,
            List<DataversePrincipalRef> revokes)
        {
            var kept = new List<(DataversePrincipalRef Principal, int Mask)>();
            foreach (var (principal, mask) in modifies)
            {
                if (!restrict(principal))
                {
                    kept.Add((principal, mask));
                    continue;
                }

                var narrowed = mask & have[principal];
                if (narrowed == have[principal])
                    continue; // nothing to remove: the change only added rights
                if (RecordShareLevels.CanRead(narrowed))
                    kept.Add((principal, narrowed));
                else
                    revokes.Add(principal);
            }

            return kept;
        }

        /// <summary>
        /// Whether the No Access list refuses <paramref name="systemUserId"/> on the child's secure roots (task 143's ONE
        /// write-time question, asked of each root and cached for the run): <see cref="SecureShareWallOutcome.Unverifiable"/>
        /// when any root's answer could not be read, else <see cref="SecureShareWallOutcome.Walled"/> when any root walls them,
        /// else <see cref="SecureShareWallOutcome.NotWalled"/>.
        /// </summary>
        private async Task<SecureShareWallOutcome> WallAsync(Lineage lineage, Guid systemUserId)
        {
            var walled = false;
            foreach (var root in lineage.SecureRoots.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id))
            {
                if (!_walls.TryGetValue((root, systemUserId), out var outcome))
                {
                    var decision = await _owner._noAccessGuard
                        .CheckAsync(root.Table, root.Id, systemUserId, _ct).ConfigureAwait(false);
                    outcome = decision.Outcome;
                    _walls[(root, systemUserId)] = outcome;
                }

                if (outcome == SecureShareWallOutcome.Unverifiable)
                    return SecureShareWallOutcome.Unverifiable;
                walled |= outcome == SecureShareWallOutcome.Walled;
            }

            return walled ? SecureShareWallOutcome.Walled : SecureShareWallOutcome.NotWalled;
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
