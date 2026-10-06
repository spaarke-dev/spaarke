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

/// <summary>
/// What started a pass — which decides whether it may take a child OUT of isolation (owner round 24 item 2, task 148 r2):
/// only an unsecure, the act of a Full Access holder or the creator (F3 — owner round 10 item 7), releases an isolated
/// child to its business unit. Provisioning (Write-gated) and the sweep never do: a child they find isolated that the rule
/// would hand an ordinary team stays isolated and is reported <see cref="SecureChildRowOutcome.NeedsF3"/> (round 6, "never
/// auto-unsecure").
/// </summary>
public enum SecureChildPassTrigger
{
    /// <summary><c>/provision-project</c> (Step 8, or the already-provisioned branch): into isolation only.</summary>
    Provisioning,

    /// <summary>The <c>secure-child-reconciliation</c> sweep: into isolation only.</summary>
    Sweep,

    /// <summary>
    /// <c>/unsecure-project</c> mid-transition: the root has been moved off the Secure team and read back, its flag is still
    /// set (cleared last) — the resolver is told so (<see cref="RecordOwnershipContext.UnsecuringRoot"/>). Releases.
    /// </summary>
    Unsecure,

    /// <summary>
    /// <c>/unsecure-project</c> on a record already not secure: completes the children an earlier unsecure left isolated.
    /// Releases; the root is not mid-transition, so no exemption and its platform-cascade rows move only across the
    /// boundary.
    /// </summary>
    UnsecureCompletion,
}

/// <summary>How a reconcile pass of one root ended.</summary>
public enum SecureChildReconcileStatus
{
    /// <summary>
    /// Every child in scope is (or, report-only, could be decided to be) in its invariant state — or is held isolated for an
    /// F3 holder's act (<see cref="SecureChildRowOutcome.NeedsF3"/>, counted: this pass may not release it, and no call of
    /// the same trigger ever will, so it is not work this pass left undone).
    /// </summary>
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

    /// <summary>
    /// Isolated, and the rule would hand it an ordinary team (every record it is filed under is ordinary), but this pass may
    /// not release it (owner round 24 item 2): only <c>/unsecure-project</c> — an F3 holder's act — takes a child out of
    /// isolation. Left isolated (an under-share, never an over-share) with its shares, never written, and reported.
    /// </summary>
    NeedsF3,
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
    string Table, int Examined, int AlreadyCorrect, int Changed, int WouldChange, int Untouched, int Refused, int Failed,
    int NeedsF3);

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
/// <param name="MirrorRemovalsIncomplete">Children leaving isolation whose mirrored shares could not all be removed — each put
/// back on the Secure team and counted <see cref="SecureChildRowOutcome.Failed"/>.</param>
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

    /// <summary>
    /// The <c>sprk_document</c> rows of this pass that end it ISOLATED (owned by the Secure Record owner team; planned, for
    /// a report-only pass) — the files a Make Secure moves into the record's own container (round 26 item 3, wired at the
    /// batch-4 integration: provisioning hands exactly these to <c>DocumentContainerRelocator</c>). Empty for an ended
    /// pass. A document the pass leaves ordinary is not listed: its derived container is not the record's.
    /// </summary>
    public IReadOnlyList<Guid> IsolatedDocumentIds { get; init; } = [];

    /// <summary>Rows re-owned by this pass.</summary>
    public int ChildrenReowned => Tables.Sum(t => t.Changed);

    /// <summary>Isolated rows the rule would release but this pass may not (owner round 24 item 2): only unsecure does.</summary>
    public int ChildrenNeedingF3 => Tables.Sum(t => t.NeedsF3);

    /// <summary>
    /// Rows NOT in their invariant state after this pass: refused + failed (+ planned, report-only) — a child whose mirrored
    /// shares could not be removed is among the failed — plus children whose shares are not in line.
    /// </summary>
    public int ChildrenRemaining =>
        Tables.Sum(t => t.Refused + t.Failed + t.WouldChange)
        + (Shares is { IsComplete: false } s ? Math.Max(s.ChildrenLeftOutOfLine, 1) : 0);

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
/// its owner (<see cref="UnfiledOwnership.KeepCreator"/>). Rows are decided shallowest first and
/// REPEATED TO A FIXPOINT (task 148 r1; report-only too since r2, over planned owners): a row whose parent sits at its own level (an FR-26-stamped analysis of a document, a
/// stamped communication regarding an event) may be decided before that parent moves, so every row a move could change —
/// through any chain of rows of the pass — is decided again until nothing is left to move. Each round applies one kind of
/// move: OUT of isolation first, and only for rows that do not rest on a row still waiting to move IN (a move out widens
/// access, so it is made once it is certain); otherwise the moves IN (narrowing). So a row is never left isolated by a
/// parent that left after it, never pulled into isolation behind a parent about to leave, and never released and pulled
/// back; a row the pass did pull in whose support then left goes back to its start owner. A row is moved only when the
/// move crosses the isolation boundary — INTO the Secure team, or OUT of it; an ordinary row the rule would hand another
/// ordinary team is not this transition's (<see cref="SecureChildRowOutcome.Untouched"/>). OUT of it only when an unsecure
/// asked (owner round 24 item 2, <see cref="SecureChildPassTrigger"/>): provisioning and the sweep never release a child
/// that was isolated — they hold it and report it <see cref="SecureChildRowOutcome.NeedsF3"/>. The
/// platform-cascade rows belong to the root alone and always take the rule's owner (owner round 13 item 1). A report-only
/// pass runs the SAME rounds and writes nothing (task 148 r2): each move is planned instead, and the resolver reads a
/// planned row as owned by its planned team (<see cref="RecordOwnershipContext.PlannedOwningTeams"/>), so a grandchild
/// whose only route into isolation is a parent the same pass would move is planned too — the dry run lists every owner
/// change the writing pass would make (assuming each write lands; a write Dataverse refuses is the apply's to report).</para>
/// <para><b>Ordering.</b> INTO isolation: every child re-owned, then <see cref="SecureChildShareSynchronizer.SyncRootAsync"/>
/// mirrors the root's sharees (149 mirrors only Secure-team-owned rows, so the mirror follows the re-own — the transient is an
/// UNDER-share of the root's sharees for the length of the pass, never an over-share: no principal that could not read a
/// child before can read it after a re-own; sanctioned by owner round 11 item 3, "148 re-owns the children, then calls
/// SyncRootAsync"). OUT of isolation (whichever trigger): every child re-owned first (so it is reachable through its
/// business unit), then its mirrored shares removed (<see cref="SecureChildShareSynchronizer.RemoveMirrorAsync"/>) — ONLY from
/// a child the Secure team owned when the pass read it (owner round 22); a child found already ordinary keeps every share. A
/// child whose mirror cannot all be removed is put back on the Secure team (an under-share), so no later pass can mistake it
/// for a never-isolated child and leave its former sharees on it. The caller does the root's own steps around this
/// pass.</para>
/// <para><b>Every assign</b> is its own operation, never folded into a field update, read back, and preceded by a log line
/// naming the row's previous owner (reversal evidence); the report carries the same.</para>
/// <para><b>Fail closed</b> (ADR-003). An unreadable root, Secure team or descendant set decides nothing and writes nothing
/// (<see cref="SecureChildReconcileStatus.Failed"/>). A refusal or failure on one row leaves THAT row as it was (one whose
/// mirror could not be removed is put back in isolation) and makes the pass <see cref="SecureChildReconcileStatus.Incomplete"/>;
/// the next pass (a repeated provisioning or unsecure call, or
/// the sweep) completes it — every step is keyed on observed state, so a pass is idempotent.</para>
/// </remarks>
public sealed class SecureChildReconciler
{
    private const string OwningTeamColumn = "owningteam";
    private const string DocumentTable = "sprk_document";
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
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator _accessCacheInvalidator;

    /// <param name="accessCacheInvalidator">Batch-4 integration, 148 × 132. Every child OWNER change this pass makes
    /// (provisioning's child pass, the unsecure's Step 3.5 and completion branch, a re-file, <c>SecureChildReconciliationJob</c>)
    /// is evicted through task 132's ONE owner-change hook. It is optional so this service's test compositions keep
    /// compiling. The host registers the hook unconditionally, with its Null-Object.</param>
    public SecureChildReconciler(
        IGenericEntityService dataverse,
        IRecordOwnershipResolver ownership,
        SecureChildShareSynchronizer shares,
        DataverseWebApiClient webApi,
        IConfiguration configuration,
        ILogger<SecureChildReconciler> logger,
        Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator? accessCacheInvalidator = null)
    {
        _dataverse = dataverse;
        _ownership = ownership;
        _shares = shares;
        _webApi = webApi;
        _configuration = configuration;
        _logger = logger;
        _accessCacheInvalidator = accessCacheInvalidator ?? new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance);
    }

    /// <summary>
    /// Reconciles every existing child of one root. <paramref name="trigger"/> says who asked (owner round 24 item 2): only the
    /// unsecure endpoint (<see cref="SecureChildPassTrigger.Unsecure"/> / <see cref="SecureChildPassTrigger.UnsecureCompletion"/>)
    /// may release an isolated child; provisioning and the sweep report such a child <see cref="SecureChildRowOutcome.NeedsF3"/>
    /// and leave it isolated. <see cref="SecureChildPassTrigger.Unsecure"/> is passed ONLY after the endpoint moved the root
    /// off the Secure team and read the move back: the root's <c>sprk_issecure</c> is still <c>true</c> (cleared last), so the
    /// resolver is told this one root is mid-transition (<see cref="RecordOwnershipContext.UnsecuringRoot"/>). A root that
    /// still reads as Secure-team-owned is refused that exemption (Failed).
    /// </summary>
    public async Task<SecureChildReconcileReport> ReconcileAsync(
        string rootLogicalName, Guid rootId, SecureChildReconcileMode mode, SecureChildPassTrigger trigger, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLogicalName);
        if (!Enum.IsDefined(trigger))
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown pass trigger.");
        var unsecuring = trigger == SecureChildPassTrigger.Unsecure;
        var mayRelease = trigger is SecureChildPassTrigger.Unsecure or SecureChildPassTrigger.UnsecureCompletion;
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
        var pass = new Pass(this, mode, rootRef, rootIsolated, unsecuring, mayRelease, secureTeamId, ct);

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

    /// <summary>How many times the child pass after a re-file runs before it reports (every pass is idempotent).</summary>
    internal const int RefileChildPassAttempts = 2;

    /// <summary>
    /// Task 147 r1c: whether ONE row is owned by the Secure Record owner team right now — a re-file writer asks BEFORE its
    /// write, for <see cref="AfterRefileAsync"/>'s <c>wasIsolated</c>. See <see cref="SecureChildShareSynchronizer.IsSecureTeamOwnedAsync"/>.
    /// </summary>
    public Task<bool> IsSecureTeamOwnedAsync(string childLogicalName, Guid childId, CancellationToken ct) =>
        _shares.IsSecureTeamOwnedAsync(childLogicalName, childId, ct);

    /// <summary>Task 147 r1c: whether <paramref name="teamId"/> is the Secure Record owner team (a writer that read the row's
    /// owning team before its write). See <see cref="SecureChildShareSynchronizer.IsSecureOwnerTeamAsync"/>.</summary>
    public Task<bool> IsSecureOwnerTeamAsync(Guid teamId, CancellationToken ct) => _shares.IsSecureOwnerTeamAsync(teamId, ct);

    /// <summary>
    /// unified-access-control-r2 task 147 r1c (owner round 28 item 1; round 36: "then 148/149's child pass runs when the event
    /// moves under or out of a secure parent") — the ONE step after a re-file, for EVERY re-file writer (the event and
    /// communication filing routes, <c>PATCH /api/v1/child-records/{table}/{id}</c>, <c>PUT /api/v1/documents/{id}</c> and
    /// the chat update tool). Never throws; the re-file it follows stands.
    /// </summary>
    /// <remarks>
    /// <para><b>(1) The row itself</b> — task 149's mirror, inline (<see cref="SecureChildShareSynchronizer.AfterRefileAsync"/>):
    /// shared with its secure roots' sharees when it is isolated now; its mirrored shares removed when it LEFT isolation.</para>
    /// <para><b>(2) Everything filed under it</b> — when the row was isolated before the re-file or is isolated now (it moved
    /// under, out of, or between secure records), this engine's pass runs over the row's own descendants (to-dos, memos, event
    /// logs, documents and their analyses, …): each re-decided by the ONE ownership rule against the owners its parents have
    /// NOW, re-owned across the isolation boundary, read back, its mirror brought in line (an isolated descendant mirrors ITS
    /// OWN secure roots' sharees — <see cref="SecureChildShareSynchronizer.SyncChildAsync"/> — since the row is not a root);
    /// one that leaves isolation loses its mirror (owner round 22). Without it a re-filed event's to-dos would stay readable
    /// by the business unit under the secure record it moved into until the next sweep, and stay isolated for good under the
    /// ordinary record it moved out to.</para>
    /// <para><b>Release (owner round 24 item 2).</b> A descendant leaves isolation only when the ROW left it in this re-file —
    /// a move the re-file core admitted only after the caller's F3 (Full Access on every secure record left, or the row's
    /// creator; owner round 10 item 7). Its descendants were isolated only through it, so they follow it out under the same
    /// act. A row that was not isolated before releases nothing: an isolated descendant whose every parent is ordinary is held
    /// and reported <see cref="SecureChildRowOutcome.NeedsF3"/>, as the sweep does. Whether the row was isolated before is
    /// read by the writer BEFORE its write (<paramref name="wasIsolated"/>); when it cannot be read nothing is released.</para>
    /// <para><b>Fail closed (ADR-003).</b> A pass that does not complete is run again once (<see cref="RefileChildPassAttempts"/>);
    /// then it is logged as an ERROR naming the row. Every row it could not move is left as it was — an isolated row stays
    /// isolated (an under-share); an ordinary row under a secure record is moved in by the two-minute recent-changes pass,
    /// which sees the re-filed row's modification.</para>
    /// </remarks>
    /// <param name="childLogicalName">The re-filed row's table.</param>
    /// <param name="childId">The re-filed row.</param>
    /// <param name="wasIsolated">Whether the row was owned by the Secure Record owner team BEFORE the re-file.</param>
    /// <param name="ct">Cancellation (writers pass <see cref="CancellationToken.None"/>: the write has landed).</param>
    /// <returns>The descendants' pass, or <c>null</c> when none ran.</returns>
    public async Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(childLogicalName);
        ArgumentNullException.ThrowIfNull(wasIsolated);
        var table = childLogicalName.Trim().ToLowerInvariant();

        bool? before = null;
        Exception? beforeFault = null;
        try
        {
            before = await wasIsolated().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            beforeFault = ex;
        }

        // (1) The row's own mirror — one implementation (task 149's synchronizer); it logs a fault reading `before`.
        await _shares.AfterRefileAsync(
            table, childId,
            () => beforeFault is null ? Task.FromResult(before!.Value) : Task.FromException<bool>(beforeFault),
            ct).ConfigureAwait(false);

        if (!SecureChildLineage.IsChild(table))
            return null;

        bool now;
        try
        {
            now = await _shares.IsSecureTeamOwnedAsync(table, childId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed, but its owner could not be read back, so " +
                "the records filed under it were NOT reconciled; the two-minute recent-changes pass moves them into isolation " +
                "if it is isolated, and nothing is released.", table, childId);
            return null;
        }

        if (before != true && !now)
            return null; // neither under a secure record before nor now: nothing filed under it crosses the boundary

        var mayRelease = before == true && !now;
        var anchor = new RecordOwnershipParent(table, childId);
        SecureChildReconcileReport? report = null;
        try
        {
            for (var attempt = 1; attempt <= RefileChildPassAttempts; attempt++)
            {
                report = await ReconcileBelowAsync(anchor, now, mayRelease, ct).ConfigureAwait(false);
                if (report.IsComplete)
                    return report;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Never thrown: the re-file it follows has landed. Every row the pass had not moved is as it was.
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed, but the pass over the records filed " +
                "under it faulted; every row it had not moved was left as it was.", table, childId);
            return report;
        }

        _logger.LogError(
            "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed ({Direction}), but the records filed under it were not all " +
            "brought in line after {Attempts} passes ({Status}: refused={Refused} failed={Failed} needsF3={NeedsF3}; {Detail}). " +
            "Every row not moved was left as it was.", table, childId,
            mayRelease ? "out of isolation" : now ? "into or within isolation" : "unchanged", RefileChildPassAttempts,
            report!.Status, report.Tables.Sum(t => t.Refused), report.Tables.Sum(t => t.Failed),
            report.Tables.Sum(t => t.NeedsF3), report.Detail ?? "-");
        return report;
    }

    /// <summary>One pass over the descendants of a re-filed CHILD row (never its platform-cascade rows: only a root has those).</summary>
    private async Task<SecureChildReconcileReport> ReconcileBelowAsync(
        RecordOwnershipParent anchor, bool anchorIsolated, bool mayRelease, CancellationToken ct)
    {
        const SecureChildReconcileMode mode = SecureChildReconcileMode.Apply;
        Guid secureTeamId;
        try
        {
            var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, anchor.EntityLogicalName,
                    anchor.RecordId, refusal);
            if (team.TeamId is not { } id)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.NotApplicable, mode, anchor.EntityLogicalName,
                    anchor.RecordId, "this environment has no Secure Record owner team, so no record is secure");
            secureTeamId = id;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: the Secure Record owner team could not be read; nothing " +
                "filed under it was decided.", anchor.EntityLogicalName, anchor.RecordId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, anchor.EntityLogicalName,
                anchor.RecordId, "the Secure Record owner team could not be read");
        }

        var pass = new Pass(this, mode, anchor, anchorIsolated, unsecuring: false, mayRelease, secureTeamId, ct);
        try
        {
            await pass.LoadDescendantsAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: the records filed under it could not be read; nothing " +
                "was written.", anchor.EntityLogicalName, anchor.RecordId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, anchor.EntityLogicalName,
                anchor.RecordId, "the records filed under it could not be read");
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

        // Task 147 r1c: a pass below a re-filed CHILD row (AfterRefileAsync) — no platform-cascade rows, and each isolated
        // descendant mirrors its own secure roots' sharees (there is no root to sync from).
        private readonly bool _anchorIsRoot;

        // Owner round 24 item 2: only an unsecure (an F3 holder's act) takes a child out of isolation.
        private readonly bool _mayRelease;
        private readonly Guid _secureTeamId;
        private readonly CancellationToken _ct;

        private readonly List<(int Level, Entity Row)> _descendants = new();
        private readonly Dictionary<(string Table, Guid Id), Entity> _byRef = new();
        private readonly Dictionary<string, int[]> _counts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SecureChildRowChange> _changes = new();

        // Report-only (task 148 r2): the owning team each planned move would give its row — what the resolver reads for
        // that row instead of its stored owner (RecordOwnershipContext.PlannedOwningTeams). Always empty when the pass writes.
        private readonly Dictionary<RecordOwnershipParent, Guid> _planned = new();

        public Pass(
            SecureChildReconciler owner, SecureChildReconcileMode mode, RecordOwnershipParent root, bool rootIsolated,
            bool unsecuring, bool mayRelease, Guid secureTeamId, CancellationToken ct)
        {
            _owner = owner;
            _mode = mode;
            _root = root;
            _rootIsolated = rootIsolated;
            _unsecuring = unsecuring;
            _anchorIsRoot = SecureChildLineage.IsRoot(root.EntityLogicalName);
            _mayRelease = mayRelease;
            _secureTeamId = secureTeamId;
            _ct = ct;
        }

        private ILogger Log => _owner._logger;

        // Indexes into a table's counter array.
        private const int Examined = 0, AlreadyCorrect = 1, Changed = 2, WouldChange = 3, Untouched = 4, Refused = 5, Failed = 6,
            NeedsF3 = 7;

        private void Count(string table, int slot)
        {
            if (!_counts.TryGetValue(table, out var c))
                _counts[table] = c = new int[8];
            c[slot]++;
        }

        private const string NeedsF3Detail =
            "isolated, and every record it is filed under is ordinary: only Unsecure (a Full Access holder or the record's " +
            "creator) takes it out of isolation — left isolated (owner round 24)";

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
            //    Only a root has them (a pass below a re-filed child row has none).
            if (_anchorIsRoot)
                await PlaceCascadeRowsAsync().ConfigureAwait(false);

            // 2. Every descendant, decided by the rule against the owners its parents have NOW (report-only: would have) —
            //    repeated until nothing is left to move (a fixpoint). One ordered sweep is not enough: a row's parents can sit
            //    at its OWN level (an FR-26-stamped analysis of a document, a stamped communication regarding an event), so a
            //    row decided before such a parent reads that parent's OLD owner. Leaving isolation, that kept a row on the
            //    Secure team (secure-if-any) — left isolated, with its mirror, by a pass that reported Completed — and pulled
            //    an ordinary row INTO isolation behind a parent about to leave it.
            //
            //    So each round decides the rows a move could have changed, then applies ONE kind of move:
            //      - moves OUT of isolation first, but only those that do not rest on a row still waiting to move IN (its
            //        support may be on the way); a move out widens access, so it is made only once it is certain;
            //      - otherwise the moves INTO isolation (narrowing — the safe direction);
            //    and decides again. A row this pass moved IN whose support then left goes back to the owner it had when the
            //    pass began (not to an ordinary team the rule picks — it was never this transition's), once; a row asked to
            //    move after that does not settle and is reported failed. So the rounds end; a ceiling backs that up, and
            //    whatever is still waiting at the ceiling is reported failed (fail closed, left as it is).
            var ordered = _descendants
                .OrderBy(d => d.Level)
                .ThenBy(d => d.Row.LogicalName, StringComparer.Ordinal)
                .ThenBy(d => d.Row.Id)
                .Select(d => d.Row)
                .ToList();
            var keys = ordered.Select(r => (Table: r.LogicalName, r.Id)).ToList();
            var rows = ordered.ToDictionary(r => (r.LogicalName, r.Id));
            var ancestors = AncestorsInPass(ordered);

            // Owner round 22: the owner each row had when this pass began — before any re-own — decides whether it WAS
            // isolated (and so whether its shares are task 149's mirror). `current` follows this pass's own writes.
            var start = ordered.ToDictionary(r => (r.LogicalName, r.Id), OwnerOf);
            var current = new Dictionary<(string Table, Guid Id), DataversePrincipalRef?>(start);
            var decisions = new Dictionary<(string Table, Guid Id), RowDecision>();
            var failures = new Dictionary<(string Table, Guid Id), string>();
            var written = new HashSet<(string Table, Guid Id)>();
            var movedIn = new HashSet<(string Table, Guid Id)>();
            var restored = new HashSet<(string Table, Guid Id)>();
            var maxRounds = 2 * ordered.Count + 2;

            IReadOnlySet<(string Table, Guid Id)> toDecide = keys.ToHashSet();
            for (var round = 1; ; round++)
            {
                foreach (var key in keys.Where(toDecide.Contains))
                    decisions[key] = await DecideRowAsync(rows[key], current[key], start[key]).ConfigureAwait(false);

                // Report-only runs the SAME rounds (task 148 r2): it writes nothing, so each move is PLANNED instead — kept
                // in _planned, which the resolver reads as that row's owner (RecordOwnershipContext.PlannedOwningTeams) —
                // and the rows resting on it are decided again against the planned owner. A grandchild whose only route
                // into isolation is a parent the pass would move is therefore planned too: the dry run lists every change
                // the writing pass makes (it assumes each write succeeds — a write Dataverse refuses is the apply's to
                // report).
                bool Open((string Table, Guid Id) k) => !failures.ContainsKey(k);
                var ins = keys.Where(k => Open(k) && decisions[k].Move == RowMove.In).ToHashSet();
                var outs = keys.Where(k => Open(k) && decisions[k].Move == RowMove.Out).ToList();
                if (ins.Count == 0 && outs.Count == 0)
                    break;

                if (round > maxRounds)
                {
                    foreach (var k in ins.Concat(outs))
                        failures[k] = "its owner did not settle within this pass; it is left as it is for the next pass";
                    break;
                }

                var certainOuts = outs.Where(k => !ancestors[k].Overlaps(ins)).ToList();
                var batch = certainOuts.Count > 0 ? certainOuts : ins.Count > 0 ? keys.Where(ins.Contains).ToList() : outs;

                foreach (var key in batch)
                {
                    var decision = decisions[key];
                    var target = decision.Target!.Value;
                    if (restored.Contains(key))
                    {
                        // Already put back once in this pass and asked to move again: it does not settle — left as it is.
                        failures[key] = "its owner did not settle within this pass; it is left as it is for the next pass";
                        continue;
                    }

                    if (decision.Move == RowMove.Out && movedIn.Contains(key))
                    {
                        // Moved INTO isolation by this pass, now ruled out of it: the isolated row it rested on has since
                        // left. It was NOT isolated when the pass began, so it goes back to the owner it had then (never to
                        // a team the rule picks for an ordinary row — that is not this transition's), and keeps every share
                        // it had (owner round 22). Report-only: the plan drops its move IN (it reads as its stored owner).
                        string? back = null;
                        if (_mode == SecureChildReconcileMode.Apply)
                            back = await RestoreStartOwnerAsync(key.Table, key.Id, start[key]).ConfigureAwait(false);
                        else
                            _planned.Remove(PlannedKey(key));
                        if (back is not null)
                        {
                            failures[key] = $"this pass moved it into isolation, its support has since left, and it could not " +
                                $"be put back on its owner ({back}); it stays isolated until the next pass";
                            continue;
                        }

                        current[key] = start[key];
                        decisions[key] = decision with
                        {
                            Move = RowMove.Stay,
                            Kind = start[key] == DataversePrincipalRef.Team(target)
                                ? SecureChildRowOutcome.AlreadyCorrect
                                : SecureChildRowOutcome.Untouched,
                        };
                        restored.Add(key);
                        continue;
                    }

                    if (_mode == SecureChildReconcileMode.Apply)
                    {
                        // Reversal evidence BEFORE the write (constraint "every assign").
                        Log.LogInformation("[SECURE-CHILD-RECONCILE] reassign: {Table} {Id} owner {Previous} -> team {Target}.",
                            key.Table, key.Id, Describe(current[key]), target);
                        var fault = await AssignAsync(key.Table, key.Id, decision.Resolution!).ConfigureAwait(false);
                        if (fault is not null)
                        {
                            failures[key] = fault;
                            continue;
                        }
                    }
                    else
                    {
                        // Report-only: planned, not written — every planned change is a log line, the complete list.
                        Log.LogInformation(
                            "[SECURE-CHILD-RECONCILE] plan: {Table} {Id} owner {Previous} -> team {Target} (report-only).",
                            key.Table, key.Id, Describe(current[key]), target);
                        _planned[PlannedKey(key)] = target;
                    }

                    current[key] = DataversePrincipalRef.Team(target);
                    decisions[key] = decision with { Move = RowMove.Stay };
                    written.Add(key);
                    if (decision.Move == RowMove.In && !IsIsolated(start[key]))
                        movedIn.Add(key);
                }

                // Decide again every open row a move of this round could change (its parents run, through rows of this
                // pass, to a row that moved). The rest keep their decision.
                var batchSet = batch.ToHashSet();
                toDecide = keys.Where(k => Open(k) && ancestors[k].Overlaps(batchSet)).ToHashSet();
            }

            var results = keys.ToDictionary(
                k => k, k => Outcome(decisions[k], start[k], current[k], failures.GetValueOrDefault(k), written.Contains(k)));

            SecureChildShareSyncResult? shares = null;
            var mirrorRevoked = 0;
            var mirrorIncomplete = 0;

            if (_mode == SecureChildReconcileMode.Apply)
            {
                // 3a. OUT of isolation — whichever trigger moved the row (an unsecure; a pre-148 unsecure completed; or a
                // sweep over an isolated root whose rule hands one row an ordinary team): after its re-own, its mirrored
                // shares come off. Owner round 22: ONLY a row the Secure team owned when this pass began (it was isolated, so
                // every direct share on it is task 149's mirror — the synchronizer revokes anything else); a row found
                // already ordinary is never moved here and keeps every share it has (a user's own share on a never-isolated
                // child is that user's intent).
                foreach (var row in ordered)
                {
                    var key = (row.LogicalName, row.Id);
                    if (results[key] is not { LeftIsolation: true } left)
                        continue;

                    var removal = await _owner._shares.RemoveMirrorAsync(row.LogicalName, row.Id, _ct).ConfigureAwait(false);
                    mirrorRevoked += removal.SharesRevoked;
                    if (removal.IsComplete)
                        continue;

                    // The mirror could not all be removed. Left OUT of isolation the row would carry its former sharees'
                    // shares into its business unit — and the next pass, which (round 22) treats a row it finds ordinary as
                    // never isolated, would leave them there for good: an over-share once the record's own shares go. So
                    // it is put BACK on the Secure team (read back): isolated again with part of its mirror — an
                    // under-share, never an over-share — and the next pass, finding it isolated, moves it out again.
                    mirrorIncomplete++;
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: its mirrored shares were not all removed " +
                        "({Status}: {Detail}); putting it back on the Secure Record owner team.",
                        row.LogicalName, row.Id, removal.Status, removal.Detail);
                    var back = await RestoreStartOwnerAsync(row.LogicalName, row.Id, left.Previous).ConfigureAwait(false);
                    if (back is not null)
                    {
                        Log.LogCritical("[SECURE-CHILD-RECONCILE] {Table} {Id} is OUT of isolation with mirrored shares left on " +
                            "it and could not be put back ({Back}). Revoke every direct share on it by hand.",
                            row.LogicalName, row.Id, back);
                    }

                    results[key] = left with
                    {
                        Outcome = SecureChildRowOutcome.Failed,
                        Detail = back is null
                            ? $"its mirrored shares could not all be removed ({removal.Detail}); it was put back on the " +
                              "Secure Record owner team for the next pass"
                            : $"its mirrored shares could not all be removed ({removal.Detail}) and it could not be put back " +
                              $"({back}): revoke every direct share on it by hand",
                    };
                }

                // 3b. INTO isolation: the root's sharees mirrored onto every Secure-team-owned child (task 149) — the rows
                // put back above included.
                if (_anchorIsRoot && _rootIsolated)
                    shares = await _owner._shares.SyncRootAsync(_root.EntityLogicalName, _root.RecordId, _ct).ConfigureAwait(false);
                else if (!_anchorIsRoot)
                    shares = await MirrorIsolatedDescendantsAsync(ordered, current, results).ConfigureAwait(false);
            }

            foreach (var row in ordered)
            {
                var d = results[(row.LogicalName, row.Id)];
                Count(row.LogicalName, Examined);
                Count(row.LogicalName, d.Outcome switch
                {
                    SecureChildRowOutcome.AlreadyCorrect => AlreadyCorrect,
                    SecureChildRowOutcome.Changed => Changed,
                    SecureChildRowOutcome.WouldChange => WouldChange,
                    SecureChildRowOutcome.Untouched => Untouched,
                    SecureChildRowOutcome.Refused => Refused,
                    SecureChildRowOutcome.NeedsF3 => NeedsF3,
                    _ => Failed,
                });
                if (d.Outcome is not (SecureChildRowOutcome.AlreadyCorrect or SecureChildRowOutcome.Untouched))
                    _changes.Add(new(row.LogicalName, row.Id, d.Previous, d.Target, d.Outcome, d.Detail));
            }

            var tables = _counts
                .OrderBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => new SecureChildTableCounts(
                    c.Key, c.Value[Examined], c.Value[AlreadyCorrect], c.Value[Changed], c.Value[WouldChange],
                    c.Value[Untouched], c.Value[Refused], c.Value[Failed], c.Value[NeedsF3]))
                .ToList();

            // A row held for an F3 holder (NeedsF3) is reported, and is not work this pass left undone: no call of the same
            // trigger may ever release it (owner round 24 item 2), so it does not make the pass Incomplete.
            var rowsOk = tables.All(t => t.Refused == 0 && t.Failed == 0);
            var complete = rowsOk && mirrorIncomplete == 0 && shares is not { IsComplete: false };
            var status = complete ? SecureChildReconcileStatus.Completed : SecureChildReconcileStatus.Incomplete;

            Log.Log(
                complete ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CHILD-RECONCILE] root={Table} {RootId} mode={Mode} isolated={Isolated} unsecuring={Unsecuring} " +
                "mayRelease={MayRelease} status={Status} examined={Examined} alreadyCorrect={AlreadyCorrect} changed={Changed} " +
                "wouldChange={WouldChange} untouched={Untouched} refused={Refused} failed={Failed} needsF3={NeedsF3} " +
                "shares={Shares} mirrorRevoked={MirrorRevoked} mirrorIncomplete={MirrorIncomplete}",
                _root.EntityLogicalName, _root.RecordId, _mode, _rootIsolated, _unsecuring, _mayRelease, status,
                tables.Sum(t => t.Examined), tables.Sum(t => t.AlreadyCorrect), tables.Sum(t => t.Changed),
                tables.Sum(t => t.WouldChange), tables.Sum(t => t.Untouched), tables.Sum(t => t.Refused),
                tables.Sum(t => t.Failed), tables.Sum(t => t.NeedsF3), shares?.Status.ToString() ?? "-", mirrorRevoked,
                mirrorIncomplete);

            return new SecureChildReconcileReport(
                status, _mode, _root.EntityLogicalName, _root.RecordId, _rootIsolated, tables, _changes, shares,
                mirrorRevoked, mirrorIncomplete, null)
            {
                IsolatedDocumentIds = ordered
                    .Where(r => string.Equals(r.LogicalName, DocumentTable, StringComparison.OrdinalIgnoreCase)
                                && IsIsolated(current[(r.LogicalName, r.Id)]))
                    .Select(r => r.Id)
                    .ToList(),
            };
        }

        /// <summary>
        /// Task 147 r1c — below a re-filed CHILD row: every descendant the Secure team owns after the moves gets the mirror task
        /// 149 computes for that row (<see cref="SecureChildShareSynchronizer.SyncChildAsync"/> — the intersection of ITS secure
        /// roots' sharees), so a row that moved between secure records loses the old record's sharees and gains the new one's.
        /// A row whose move failed is left as the next pass finds it. <c>null</c> when no row is isolated.
        /// </summary>
        private async Task<SecureChildShareSyncResult?> MirrorIsolatedDescendantsAsync(
            IReadOnlyList<Entity> ordered,
            IReadOnlyDictionary<(string Table, Guid Id), DataversePrincipalRef?> current,
            IReadOnlyDictionary<(string Table, Guid Id), RowResult> results)
        {
            var synced = new List<SecureChildShareSyncResult>();
            foreach (var row in ordered)
            {
                var key = (row.LogicalName, row.Id);
                if (!IsIsolated(current[key]) || results[key].Outcome == SecureChildRowOutcome.Failed)
                    continue;

                try
                {
                    synced.Add(await _owner._shares.SyncChildAsync(row.LogicalName, row.Id, _ct).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: its mirror faulted.", row.LogicalName, row.Id);
                    synced.Add(SecureChildShareSyncResult.Failed("the mirror faulted"));
                }
            }

            if (synced.Count == 0)
                return null;

            var open = synced.Where(r => !r.IsComplete).ToList();
            return new SecureChildShareSyncResult(
                open.Count == 0 ? SecureChildShareSyncStatus.Completed : SecureChildShareSyncStatus.Incomplete,
                synced.Count,
                synced.Sum(r => r.ChildrenUpdated),
                synced.Sum(r => r.ChildrenUnchanged),
                synced.Sum(r => r.ChildrenNotUpdated) + open.Count(r => r.ChildrenLeftOutOfLine == 0),
                synced.Sum(r => r.ChildrenHeld),
                synced.Sum(r => r.ChildrenOutsideSecureRoots),
                synced.Sum(r => r.SharesGranted),
                synced.Sum(r => r.SharesChanged),
                synced.Sum(r => r.SharesRevoked),
                open.Select(r => r.Detail).FirstOrDefault(d => d is not null));
        }

        /// <summary>
        /// For every row of the pass, the rows of the pass it rests on: its ownership parents (<see cref="ContextFor"/> — a
        /// message's record-thread filing included) that are themselves rows of this pass, and theirs, transitively (the
        /// resolver reads each parent FRESH, and looks through a user-owned parent to its own filing). A row whose parents
        /// are all outside the pass (the root, another record) rests on nothing a move inside it can change.
        /// </summary>
        private Dictionary<(string Table, Guid Id), HashSet<(string Table, Guid Id)>> AncestorsInPass(IReadOnlyList<Entity> ordered)
        {
            var inPass = ordered.Select(r => (r.LogicalName, r.Id)).ToHashSet();
            var parentsOf = ordered.ToDictionary(
                r => (r.LogicalName, r.Id),
                r => ContextFor(r).Parents
                    .Select(p => (p.EntityLogicalName.ToLowerInvariant(), p.RecordId))
                    .Where(inPass.Contains)
                    .ToArray());

            var ancestors = new Dictionary<(string Table, Guid Id), HashSet<(string Table, Guid Id)>>();
            foreach (var key in parentsOf.Keys)
            {
                var found = new HashSet<(string Table, Guid Id)>();
                var stack = new Stack<(string Table, Guid Id)>(parentsOf[key]);
                while (stack.Count > 0)
                {
                    var next = stack.Pop();
                    if (next != key && found.Add(next))
                    {
                        foreach (var parent in parentsOf[next])
                            stack.Push(parent);
                    }
                }

                ancestors[key] = found;
            }

            return ancestors;
        }

        /// <summary>
        /// The rule's answer for one row against the owner it has NOW (<paramref name="current"/>) — no write. A move is
        /// proposed only across the isolation boundary: IN (the rule gives the Secure team, the row is not on it) or OUT (the
        /// row is on it, the rule gives an ordinary team); an ordinary row the rule would hand another ordinary team is not
        /// this transition's (<see cref="SecureChildRowOutcome.Untouched"/>). A move OUT of a row that was isolated when the
        /// pass began (<paramref name="start"/>) is made only by a pass that may release one (owner round 24 item 2); any
        /// other pass holds the row isolated and reports it <see cref="SecureChildRowOutcome.NeedsF3"/>. (A row this pass
        /// itself moved IN and whose support then left is not held: it goes back to its own start owner — the pass undoing
        /// its own move, never a release of a child that was isolated.)
        /// </summary>
        private async Task<RowDecision> DecideRowAsync(Entity row, DataversePrincipalRef? current, DataversePrincipalRef? start)
        {
            var table = row.LogicalName;
            RecordOwnerResolution resolution;
            try
            {
                resolution = await _owner._ownership.ResolveOwnerAsync(ContextFor(row), _ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: its owner could not be decided (a read failed); " +
                    "it is left as it is.", table, row.Id);
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, "its owner could not be decided");
            }

            if (resolution.IsRefused)
            {
                Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: the ownership rule refused ({Code}: {Reason}); it is " +
                    "left as it is.", table, row.Id, resolution.RefusalCode, resolution.Reason);
                return new(RowMove.None, SecureChildRowOutcome.Refused, null, $"{resolution.RefusalCode}: {resolution.Reason}");
            }

            if (!resolution.IsOwned)
                return new(RowMove.None, SecureChildRowOutcome.Untouched, null, null); // names no parent the rule reads

            var target = resolution.OwningTeamId!.Value;
            var targetIsSecure = target == _secureTeamId;
            var move = (targetIsSecure, IsIsolated(current)) switch
            {
                (true, false) => RowMove.In,
                (false, true) => RowMove.Out,
                _ => RowMove.Stay,
            };

            if (move == RowMove.Out && !_mayRelease && IsIsolated(start))
            {
                Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: the rule gives it team {Target}, out of isolation, but " +
                    "only Unsecure releases an isolated child (owner round 24); it stays isolated (needs-f3).", table, row.Id, target);
                return new(RowMove.Stay, SecureChildRowOutcome.NeedsF3, resolution, NeedsF3Detail);
            }

            var kind = current == DataversePrincipalRef.Team(target)
                ? SecureChildRowOutcome.AlreadyCorrect
                : SecureChildRowOutcome.Untouched;
            return new(move, kind, resolution, null);
        }

        /// <summary>
        /// What happened to one row over the whole pass, reported against the owner it had when the pass began
        /// (<paramref name="start"/> — the reversal evidence). <c>LeftIsolation</c>: owned by the Secure team when the pass
        /// began and no longer — its mirrored shares must come off (owner round 22).
        /// </summary>
        private RowResult Outcome(
            RowDecision decision, DataversePrincipalRef? start, DataversePrincipalRef? current, string? failure, bool written)
        {
            // In report-only nothing was written: `written` means planned, and nothing left isolation for real.
            var applied = _mode == SecureChildReconcileMode.Apply;
            var left = applied && written && IsIsolated(start) && !IsIsolated(current);
            if (failure is not null)
                return new(SecureChildRowOutcome.Failed, start, decision.Target, failure, left);
            if (decision.Kind is SecureChildRowOutcome.Refused or SecureChildRowOutcome.Failed or SecureChildRowOutcome.NeedsF3)
                return new(decision.Kind, start, decision.Target, decision.Detail, left);
            if (written && current != start)
                return new(applied ? SecureChildRowOutcome.Changed : SecureChildRowOutcome.WouldChange, start, current?.Id, null, left);
            if (decision.Move is RowMove.In or RowMove.Out)
            {
                // Not reached: the rounds end only when no open row has a move left. Fail closed if it ever is.
                return new(SecureChildRowOutcome.Failed, start, decision.Target, "its move was not made in this pass", false);
            }

            return new(decision.Kind, start, decision.Target, null, false);
        }

        private bool IsIsolated(DataversePrincipalRef? owner) =>
            owner is { Kind: DataversePrincipalKind.Team } team && team.Id == _secureTeamId;

        /// <summary>The move a round's decision proposes.</summary>
        private enum RowMove
        {
            /// <summary>No owner to give: refused, failed, or names no parent the rule reads.</summary>
            None,

            /// <summary>Already where the rule puts it, or not this transition's to move.</summary>
            Stay,

            /// <summary>Into the Secure Record owner team.</summary>
            In,

            /// <summary>Out of the Secure Record owner team, to the team the rule gives it.</summary>
            Out,
        }

        /// <summary>One round's decision for one row: the move, and the resolution the rule answered (when it gave an owner).</summary>
        private sealed record RowDecision(RowMove Move, SecureChildRowOutcome Kind, RecordOwnerResolution? Resolution, string? Detail)
        {
            public Guid? Target => Resolution is { IsOwned: true } r ? r.OwningTeamId : null;
        }

        /// <summary>One row's outcome over the whole pass.</summary>
        private sealed record RowResult(
            SecureChildRowOutcome Outcome, DataversePrincipalRef? Previous, Guid? Target, string? Detail, bool LeftIsolation);

        /// <summary>
        /// Assigns the row to the team the ownership rule gave it (<paramref name="resolution"/>), in its own update, and reads
        /// it back. <c>null</c> on success, else what went wrong. A write that reports failure may still have landed, so the
        /// read-back decides (the task 133 restore rule).
        /// </summary>
        private async Task<string?> AssignAsync(string table, Guid id, RecordOwnerResolution resolution)
        {
            var teamId = resolution.OwningTeamId!.Value;
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

            await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);
            return await ReadBackAsync(table, id, DataversePrincipalRef.Team(teamId), writeFault).ConfigureAwait(false);
        }

        /// <summary>
        /// Batch-4 integration, 148 × 132: an owner write on a child of the pass stales the access caches exactly as a root's
        /// re-own does. This evicts through the ONE hook (<see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync"/>)
        /// once per write ATTEMPT, whatever its read-back says, because a write that reports failure can have committed. It
        /// runs on <see cref="CancellationToken.None"/>, so a disconnected caller cannot leave the clean-up half done. It
        /// never fails the pass: the hook does not throw by contract, and a fault anyway is logged and swallowed. The TTLs are
        /// the backstop.
        /// </summary>
        private async Task EvictOwnerChangeAsync(string table, Guid id, string? entitySet = null)
        {
            var set = entitySet
                ?? (SecureChildLineage.Children.TryGetValue(table, out var lineage) ? lineage.EntitySet : null);
            if (set is null)
            {
                Log.LogError("[SECURE-CHILD-RECONCILE] {Table} {Id} was re-owned, but its entity set is not known, so its cached " +
                             "access snapshot was not evicted (the TTL backstop applies).", table, id);
                return;
            }

            try
            {
                await _owner._accessCacheInvalidator.InvalidateRecordOwnerChangeAsync(
                    table, set, id, $"secure-child-reconcile:{_root.EntityLogicalName}:{_root.RecordId:D}", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] Evicting the caches after re-owning {Table} {Id} failed; the TTLs " +
                                   "are the backstop.", table, id);
            }
        }

        /// <summary>
        /// Puts a row back on the owner it had when THIS pass began — a compensation for a later step of the same pass, never
        /// an owner decided here: a row taken out of isolation whose mirrored shares could not all be removed goes back on
        /// the Secure Record owner team; a row this pass moved INTO isolation whose support then left goes back to its
        /// ordinary owner. Its own update, read back; <c>null</c> on success, else what went wrong.
        /// </summary>
        /// <remarks>
        /// Why not task 133's <see cref="AssignCascadeChildOwners.RestoreAsync"/> (task 148 r2, verifier item 4)? That primitive
        /// is table-generic in shape (<see cref="CascadeChild"/> carries the table, entity set, id column, id and owner), so
        /// the shape is not the reason. The reasons: (1) owner round 10 item 4 binds the restore of the rows a ROOT's reverse
        /// Assign cascades to — this pass does reuse it for exactly those (<c>PlaceCascadeRowsAsync</c>); this member puts back
        /// a <c>sprk_*</c> child the pass itself moved, which no Assign cascade touches. (2) The owner-write census classifies
        /// <c>RestoreOneAsync</c> as kind <c>Root</c> (its rows are a root's cascade rows — never a <c>sprk_*</c> child); routing
        /// children through it would make that classification false, and it cannot take the <c>Restore</c> kind instead,
        /// whose assertion requires the written value to be the pass's start-owner snapshot — while this pass also calls it
        /// with the resolver's owner for the cascade rows (owner round 13 item 1). (3) Every other read and write of these rows
        /// in the pass goes through <see cref="IGenericEntityService"/> (the resolver's own client), and this member shares the
        /// pass's read-back (<c>ReadBackAsync</c>) with <c>AssignAsync</c>; the put-back on the Web API client would be a second
        /// path for the same rows. It is census kind <c>Restore</c>, asserted to write only the start owner its pass recorded.
        /// </remarks>
        private async Task<string?> RestoreStartOwnerAsync(string table, Guid id, DataversePrincipalRef? startOwner)
        {
            if (startOwner is not { } owner)
                return "its owner when this pass began is not known";

            Log.LogInformation("[SECURE-CHILD-RECONCILE] put back: {Table} {Id} -> {Owner} (its owner when this pass began).",
                table, id, Describe(owner));
            string? writeFault = null;
            try
            {
                var ownerRef = new EntityReference(owner.Kind == DataversePrincipalKind.Team ? "team" : "systemuser", owner.Id);
                await _owner._dataverse.UpdateAsync(
                    table, id, new Dictionary<string, object> { [OwnerColumn] = ownerRef }, _ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                writeFault = ex.Message;
                Log.LogError(ex, "[SECURE-CHILD-RECONCILE] Dataverse refused putting {Table} {Id} back on {Owner}.",
                    table, id, Describe(owner));
            }

            await EvictOwnerChangeAsync(table, id).ConfigureAwait(false);
            return await ReadBackAsync(table, id, owner, writeFault).ConfigureAwait(false);
        }

        /// <summary>Whether a row reads as owned by <paramref name="owner"/> after an owner write; <c>null</c> when it does.</summary>
        private async Task<string?> ReadBackAsync(string table, Guid id, DataversePrincipalRef owner, string? writeFault)
        {
            try
            {
                var query = new QueryExpression(table)
                {
                    ColumnSet = new ColumnSet(OwningTeamColumn, OwningUserColumn),
                    TopCount = 1,
                    NoLock = true,
                };
                query.Criteria.AddCondition(table + "id", ConditionOperator.Equal, id);
                var after = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();
                if (after is not null && OwnerOf(after) == owner)
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
                PlannedOwningTeams = _planned.Count > 0 ? new Dictionary<RecordOwnershipParent, Guid>(_planned) : null,
            };
        }

        /// <summary>A row of the pass as the resolver names a parent (lower-case table).</summary>
        private static RecordOwnershipParent PlannedKey((string Table, Guid Id) key) =>
            new(key.Table.ToLowerInvariant(), key.Id);

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
                // Owner round 24 item 2 needs no hold here: these rows are decided for the ROOT alone, and a pass that may
                // not release (provisioning, the sweep) only ever runs over an isolated root — whose rows the rule puts ON
                // the Secure team — or a flagged-not-isolated one, which the rule refuses. Only an unsecure moves them out.
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
                    Log.LogInformation(
                        "[SECURE-CHILD-RECONCILE] plan: {Table} {Id} owner {Previous} -> team {Target} (report-only).",
                        child.LogicalName, child.Id, Describe(child.Owner), target);
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
                if (result.Outcome is CascadeChildRestoreOutcome.Restored or CascadeChildRestoreOutcome.Refused
                    or CascadeChildRestoreOutcome.NotApplied or CascadeChildRestoreOutcome.Unverified)
                {
                    // A write was attempted (the same rule as provisioning's EvictRestoredChildrenAsync). For these tables the
                    // hook builds no pattern; the call stays because the HOOK decides what an owner change made stale.
                    await EvictOwnerChangeAsync(result.Child.LogicalName, result.Child.Id, result.Child.EntitySet).ConfigureAwait(false);
                }

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
