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
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Documents;

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

/// <summary>
/// GitHub #2091: whether <see cref="SecureChildReconciler.AfterRefileAsync(string, Guid, Func{Task{bool}}, RefileThreadStep, CancellationToken)"/>
/// lets a re-filed COMMUNICATION's thread follow the filing (<see cref="Sprk.Bff.Api.Services.Communication.IThreadResolver.ReconcileThreadForFilingAsync"/>).
/// </summary>
/// <remarks>
/// Component Justification (CLAUDE.md §11): (1) Existing — <c>AfterRefileAsync</c> ran the thread reconcile on every communication
/// re-file, with no way to leave it out. (2) Extension — this IS the extension: one argument of the existing step, not a second
/// copy of its other steps. (3) Cost of doing nothing — the thread writers (a message joining a record thread, respond-into-thread)
/// either skip the step (a row joined into a Secure record's thread unmirrored, its gate Decision Records left user-owned and
/// shared) or run the reconcile on the join they just made, which can move the message straight back out of that thread.
/// </remarks>
public enum RefileThreadStep
{
    /// <summary>The communication's thread follows the filing (every writer that moves a filing lookup).</summary>
    Reconcile,

    /// <summary>
    /// The writer IS the communication's thread placement — a message joining a record thread (<c>ThreadResolver</c>'s join,
    /// <c>CommunicationService</c>'s respond-into-thread). Every other step runs; the thread reconcile does not.
    /// </summary>
    PlacedByWriter,
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

    /// <summary>
    /// UAC task 194: the owner writes an ordinary-parent-move pass ATTEMPTED (<see cref="SecureChildReconciler.ReconcileOrdinaryParentMoveAsync"/>,
    /// Apply only) — what the job counts against its per-run cap. Zero for every other pass.
    /// </summary>
    public int OrdinaryWritesAttempted { get; init; }

    /// <summary>
    /// UAC task 194 F1-a: the documents of an ordinary-parent-move pass's walk whose FILE the job may hand to its re-file pass
    /// (any owner): every <c>sprk_document</c> of the walk except one owned in isolation or in the Secure Record business unit,
    /// one the rule gives the Secure team (it is also filed under a Secure record, so its container answer is a Secure
    /// record's), and one whose owner could not be decided or the rule refused. Empty for every other pass.
    /// </summary>
    public IReadOnlyList<Guid> OrdinaryHandOffDocumentIds { get; init; } = Array.Empty<Guid>();

    /// <summary>
    /// UAC task 194b R4b-2: the restricted-content owner team as this pass identified it — its status and the member check
    /// (<see cref="Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity.MembersFound"/>) — when a hard-deny
    /// row of the pass asked for it; <c>null</c> when none did.
    /// </summary>
    public Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeamIdentity? RestrictedContentOwnerTeam { get; init; }

    /// <summary>Rows re-owned by this pass.</summary>
    public int ChildrenReowned => Tables.Sum(t => t.Changed);

    /// <summary>
    /// UAC task 099b review round 3 (K2): a grant row of this pass was left where it is because the team the rule gives it
    /// may not own one — a repeat call does not complete the pass until an administrator adds the privilege.
    /// </summary>
    public bool BlockedByGrantOwnerPrivilege => Changes.Any(SecureChildReconciler.IsGrantOwnerPrivilegeRefusal);

    /// <summary>Isolated rows the rule would release but this pass may not (owner round 24 item 2): only unsecure does.</summary>
    public int ChildrenNeedingF3 => Tables.Sum(t => t.NeedsF3);

    /// <summary>
    /// UAC task 194b fix round R (A2): the rows this pass HELD in isolation although the rule gives them an ordinary team (K3:
    /// the To Do job's no-core rule; F1-d: a Decision Record whose question is incomplete), each with why (its
    /// <see cref="SecureChildRowChange.Detail"/>). A pass that may release took their mirrored shares off (a row whose removal
    /// did not complete is <see cref="SecureChildRowOutcome.Failed"/>). Holding does not make a pass incomplete.
    /// </summary>
    public IReadOnlyList<SecureChildRowChange> HeldInIsolation { get; init; } = Array.Empty<SecureChildRowChange>();

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
/// How <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/> ended (UAC task 183). A refusal is never a fault: ontology
/// task 148 counts <see cref="RefusedSecure"/> as <c>SkippedSecure</c>, <see cref="RefusedNotTeamOwned"/> as
/// <c>SkippedUserOwned</c>, <see cref="NotFound"/> as <c>SkippedGone</c> and <see cref="Conflict"/> as <c>SkippedConflict</c>,
/// and only <see cref="Failed"/> as a repair failure. (Values are appended, never renumbered.)
/// </summary>
public enum OrdinaryRowReownOutcome
{
    /// <summary>Re-owned to the team, and read back as owned by it.</summary>
    Reowned,

    /// <summary>Already owned by the team: nothing was written.</summary>
    AlreadyOwned,

    /// <summary>
    /// The row's current owner or the target team is in the Secure Record business unit: nothing was written. Moving a row
    /// into or out of isolation is the secure pass's alone (owner round 96).
    /// </summary>
    RefusedSecure,

    /// <summary>The row is owned by a user, not a team: nothing was written.</summary>
    RefusedNotTeamOwned,

    /// <summary>
    /// A read failed or could not tell (the row, a team, or which business unit is the Secure Record one — ambiguous is
    /// never read as "not secure"), the target team does not exist, or the write did not read back.
    /// <see cref="OrdinaryRowReownResult.Detail"/> says which.
    /// </summary>
    Failed,

    /// <summary>The row does not exist (on the guard read, or by the time of the write): nothing was written.</summary>
    NotFound,

    /// <summary>
    /// The row changed after the guard read it - another writer (the secure pass moving it into isolation, a re-file, a
    /// re-own) landed first - so Dataverse refused the conditional owner write and nothing was written. The guard's answer
    /// is stale; the caller re-reads on its next pass (UAC task 183 fix round, verifier F1).
    /// </summary>
    Conflict,
}

/// <summary>The typed result of <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/>.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Detail">Why it was refused or failed; <c>null</c> for <see cref="OrdinaryRowReownOutcome.Reowned"/> and
/// <see cref="OrdinaryRowReownOutcome.AlreadyOwned"/>.</param>
public sealed record OrdinaryRowReownResult(OrdinaryRowReownOutcome Outcome, string? Detail)
{
    /// <summary>
    /// The HTTP status of the Dataverse owner write that failed, when it failed with one (a 403 is a privilege refusal);
    /// <c>null</c> otherwise. Additive (ontology task 143 follow-up): <see cref="Detail"/> keeps only the exception message, and
    /// the shared Web API client's message for a 403 carries no Dataverse error code, so a caller cannot classify the refusal
    /// from the text alone.
    /// </summary>
    public System.Net.HttpStatusCode? WriteFailureStatusCode { get; init; }

    /// <summary>True when the row is owned by the team now (re-owned, or already).</summary>
    public bool IsOwnedByTeam => Outcome is OrdinaryRowReownOutcome.Reowned or OrdinaryRowReownOutcome.AlreadyOwned;
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
/// its record thread's filing (S6, <c>RecordThreadFiling</c> — the re-file path's rule, by the thread's type (round 7d: a Direct
/// thread adds nothing; an unset or unknown type counts as a record thread); #2143 round 6: a thread outside the
/// pass is read, and a message whose thread cannot be read is left undecided); a row that names no parent keeps
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
    private const string OwningBusinessUnitColumn = "owningbusinessunit";
    private const string VersionNumberColumn = "versionnumber";
    private const string OwnerBindColumn = "ownerid@odata.bind";
    private const string OwnerColumn = "ownerid";
    private const string IsSecureColumn = "sprk_issecure";
    private const string CommunicationTable = "sprk_communication";

    /// <summary>
    /// UAC task 099b review round 2: the grant table. Its rows of the root, in EVERY state (review round 3, F1), are owned by
    /// the root's team (D-11), so they follow the root across the isolation boundary in this pass. Not a lineage child (no row
    /// is filed under a grant, and the share mirror never covers it); loaded beside the descendants, decided and written by
    /// the same rule and sequence.
    /// </summary>
    internal const string GrantTable = "sprk_externalrecordaccess";

    /// <summary>The grant table's entity set (the owner-change eviction names it).</summary>
    private const string GrantEntitySet = "sprk_externalrecordaccesses";

    /// <summary>
    /// UAC task 099b review round 3 (K2): the detail of a grant row left where it is because the team the rule gives it may
    /// not own one (its roles lack Read on the grant table). Unlike any other failed row, a repeat pass cannot move it until an
    /// administrator adds the privilege, so the endpoints say so (<see cref="IsGrantOwnerPrivilegeRefusal"/>).
    /// </summary>
    private static readonly string GrantOwnerPrivilegeMissingDetail =
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode
        + ": the owner team may not own an access grant (its security roles lack Read on sprk_externalrecordaccess); it is "
        + "left as it is until an administrator adds that privilege";

    /// <summary>Whether <paramref name="change"/> is a grant row this pass could not move for want of the owner privilege.</summary>
    internal static bool IsGrantOwnerPrivilegeRefusal(SecureChildRowChange change) =>
        change.Outcome == SecureChildRowOutcome.Failed
        && string.Equals(change.Table, GrantTable, StringComparison.OrdinalIgnoreCase)
        && string.Equals(change.Detail, GrantOwnerPrivilegeMissingDetail, StringComparison.Ordinal);

    private readonly IGenericEntityService _dataverse;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly SecureChildShareSynchronizer _shares;
    private readonly DataverseWebApiClient _webApi;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureChildReconciler> _logger;
    private readonly Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator _accessCacheInvalidator;
    private readonly Sprk.Bff.Api.Services.Communication.IThreadResolver? _threads;

    /// <summary>#2159 slice 2: the ONE writer of a document's relocation ledger, which records an F3 re-file's release of its file.</summary>
    private readonly DocumentContainerRelocator? _relocator;
    private readonly Sprk.Bff.Api.Services.Dataverse.Privileges.IDataversePrivilegeChecker? _ownerPrivileges;
    private readonly Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing _gateDecisionRecords;

    /// <summary>UAC task 194b: derives a Signal's or Decision Record's subject stamps for its ownership question
    /// (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>), the same derivation their writers use.</summary>
    private readonly CoreAncestorResolver? _coreAncestors;

    /// <summary>UAC task 194b fix round (F1-c): the To Do assignee links, so a no-core Signal's or Decision Record's question
    /// asks the To Do job's own predicate (<c>TodoAssigneeOwnerReconciliationJob.SelectsAsync</c>).</summary>
    private readonly Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessStore? _links;

    /// <param name="accessCacheInvalidator">Batch-4 integration, 148 × 132. Every child OWNER change this pass makes
    /// (provisioning's child pass, the unsecure's Step 3.5 and completion branch, a re-file, <c>SecureChildReconciliationJob</c>)
    /// is evicted through task 132's ONE owner-change hook. It is optional so this service's test compositions keep
    /// compiling. The host registers the hook unconditionally, with its Null-Object.</param>
    /// <param name="threads">spaarkeai-word-add-in-r1 task 129: after a re-filed COMMUNICATION, its thread follows the filing
    /// (<see cref="Sprk.Bff.Api.Services.Communication.IThreadResolver.ReconcileThreadForFilingAsync"/>) — here, the one step
    /// every re-file writer runs, so it runs once per filing. Optional so the test compositions keep compiling; the host
    /// registers it unconditionally.</param>
    /// <param name="ownerPrivileges">UAC task 099b review round 2: before a grant row is assigned to a team, whether that team's
    /// roles hold Read on the grant table (<c>GrantExternalAccessEndpoint.CheckGrantOwnerPrivilegeAsync</c>, the ONE helper the
    /// grant routes use). Optional so the test compositions keep compiling; the host registers it unconditionally
    /// (<c>AddDataverseSavedQueryServices</c>). Without it, Dataverse's own owner check on the write still refuses such an
    /// assignment, and the row is reported Failed the same way.</param>
    /// <param name="gateDecisionRecords">Ontology task 042b (D-169, uac-r2 choice A): after a re-filed COMMUNICATION, its gate Decision
    /// Records still owned by a user are re-owned to the resolver's team (<see cref="AfterRefileAsync"/>).</param>
    /// <param name="coreAncestors">UAC task 194b: the ONE core-ancestor derivation, so a Signal's or Decision Record's ownership
    /// question names the same stamps its writer did (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>). Optional so the
    /// test compositions keep compiling; the host registers it unconditionally (<c>AddCoreAncestorResolver</c>). Without it such
    /// a row with a core record is not decided (fail closed).</param>
    /// <param name="links">UAC task 194b fix round (F1-c): the To Do assignee links, so whether the To Do job keeps a no-core
    /// row is asked with that job's own predicate. Optional so the test compositions keep compiling; the host registers it
    /// (<c>AddScoped&lt;AssignedAccessStore&gt;</c>). Without it a no-core row about a To Do is not decided (fail closed).</param>
    public SecureChildReconciler(
        IGenericEntityService dataverse,
        IRecordOwnershipResolver ownership,
        SecureChildShareSynchronizer shares,
        DataverseWebApiClient webApi,
        IConfiguration configuration,
        ILogger<SecureChildReconciler> logger,
        Sprk.Bff.Api.Services.Signals.GateDecisionRecordSharing gateDecisionRecords,
        Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator? accessCacheInvalidator = null,
        Sprk.Bff.Api.Services.Communication.IThreadResolver? threads = null,
        Sprk.Bff.Api.Services.Dataverse.Privileges.IDataversePrivilegeChecker? ownerPrivileges = null,
        DocumentContainerRelocator? relocator = null,
        CoreAncestorResolver? coreAncestors = null,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessStore? links = null)
    {
        _relocator = relocator;
        _coreAncestors = coreAncestors;
        _links = links;
        _threads = threads;
        _ownerPrivileges = ownerPrivileges;
        _gateDecisionRecords = gateDecisionRecords ?? throw new ArgumentNullException(nameof(gateDecisionRecords));
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

    /// <summary>
    /// UAC task 194 (#2339, owner round 128 item 3; rule I-6) — the ORDINARY-PARENT-MOVE pass: an ordinary (not secure)
    /// project, matter or work assignment was reassigned to another owner (an MDA Assign, a direct write), so every child
    /// filed under it that a business unit's DEFAULT team owns is re-owned to the team <see cref="IRecordOwnershipResolver"/>
    /// answers now (the primary parent's business unit's default team). Called by <see cref="SecureChildReconciliationJob"/>'s
    /// recent-changes pass for the ordinary roots modified since its watermark.
    /// </summary>
    /// <remarks>
    /// <para><b>The same engine.</b> The same descendant walk (lineage lookups, FR-26 stamps, every further hop), the same
    /// resolver input per row (<c>ContextFor</c>) and the same fixpoint rounds, so a grandchild is decided again after the
    /// child it rests on moves (report-only: against the planned owner). Each move is a conditional re-own through the
    /// guarded entry point's own member (<see cref="ReownOrdinaryRowCoreAsync"/>: If-Match on the version read, the previous
    /// owner logged first, evicted, read back).</para>
    /// <para><b>Never isolation.</b> A row owned by the Secure Record owner team or by ANY team in the Secure Record business
    /// unit (or whose own business unit is that one) is never decided or written here; a row the rule gives the Secure team
    /// is left to the secure pass (reported). Nothing is ever moved INTO or OUT OF isolation by this pass, and the write
    /// re-checks both sides against the Secure Record business unit, fresh. A root that is secure, isolated, or whose flag
    /// cannot be read is <see cref="SecureChildReconcileStatus.NotApplicable"/>.</para>
    /// <para><b>Reported, not moved.</b> A user-owned row, and a row owned by a team that is not a business unit's default
    /// team, whose rule answer is in another business unit (I-6 interim: a create outside the product keeps its creator); a
    /// refusal of the rule (<see cref="SecureChildRowOutcome.Refused"/>). The grant rows and the platform-cascade rows of
    /// the root are not this pass's.</para>
    /// <para><b>Service requests (F4-b).</b> A <c>sprk_servicerequest</c> is an ordinary root here too (it carries no
    /// <c>sprk_issecure</c> and is never secure; one owned in the Secure Record business unit is still left alone). Its walk
    /// also follows each row's service-request lookup (<see cref="OrdinaryOnlyServiceRequestLookups"/>); the secure pass's
    /// walk never does, and the secure pass still refuses a service request as its root.</para>
    /// <para><b>Capped.</b> At most <paramref name="maxWrites"/> owner writes (Apply only); a row past the cap is
    /// <see cref="SecureChildRowOutcome.Failed"/> ("carried"), so the pass is Incomplete and the job carries the root.</para>
    /// <para>A re-owned document's FILE follows by the job's existing re-file pass (#2159, <see cref="RelocationPurpose.Refile"/>):
    /// the re-own changes the row's <c>modifiedon</c>, which puts it in that pass's window in the same run.</para>
    /// </remarks>
    public async Task<SecureChildReconcileReport> ReconcileOrdinaryParentMoveAsync(
        string rootLogicalName, Guid rootId, SecureChildReconcileMode mode, int maxWrites, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootLogicalName);
        ArgumentOutOfRangeException.ThrowIfNegative(maxWrites);
        var rootTable = rootLogicalName.Trim().ToLowerInvariant();
        // F4-b: a service request is an ordinary root too (it can never be secure: it carries no sprk_issecure).
        var serviceRequest = string.Equals(rootTable, ServiceRequestTable, StringComparison.Ordinal);
        if (!SecureChildLineage.IsRoot(rootTable) && !serviceRequest)
            throw new ArgumentOutOfRangeException(nameof(rootLogicalName), rootLogicalName, "Not a core-record table.");

        Guid secureTeamId;
        Guid? secureBu;
        Entity? root;
        try
        {
            var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId, refusal);
            // No Secure Record owner team: no row is isolated, and the ordinary rule still holds (Guid.Empty owns nothing).
            secureTeamId = team.TeamId ?? Guid.Empty;

            var bu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (bu.Ambiguous)
                return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                    "more than one business unit carries the Secure Record name, so whether a team is in it cannot be told");
            secureBu = bu.Id;

            var query = new QueryExpression(rootTable)
            {
                ColumnSet = serviceRequest
                    ? new ColumnSet(OwningTeamColumn, OwningUserColumn, OwningBusinessUnitColumn)
                    : new ColumnSet(OwningTeamColumn, OwningUserColumn, OwningBusinessUnitColumn, IsSecureColumn),
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
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] ordinary move {Table} {RootId}: the record or the Secure Record " +
                "owner team or business unit could not be read; nothing was decided.", rootTable, rootId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                "the record or the Secure Record owner team or business unit could not be read");
        }

        // A secure record's children are the secure pass's alone. An EMPTY flag fails closed (task 150): read as secure.
        // A service request has no flag; one owned in the Secure Record business unit is still left alone.
        var rootTeam = root.GetAttributeValue<EntityReference>(OwningTeamColumn)?.Id;
        var rootBu = root.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;
        if (!serviceRequest && root.GetAttributeValue<bool?>(IsSecureColumn) != false
            || rootTeam is { } owningTeam && owningTeam == secureTeamId
            || secureBu is { } secure && rootBu == secure)
        {
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.NotApplicable, mode, rootTable, rootId,
                "the record is secure, isolated, or its secure flag cannot be read: its children are the secure pass's");
        }

        var pass = new Pass(this, mode, new RecordOwnershipParent(rootTable, rootId), rootIsolated: false, unsecuring: false,
            mayRelease: false, secureTeamId, ct, new OrdinaryMove(secureBu, maxWrites));
        try
        {
            await pass.LoadDescendantsAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] ordinary move {Table} {RootId}: its related records could not be " +
                "read; nothing was written.", rootTable, rootId);
            return SecureChildReconcileReport.Ended(SecureChildReconcileStatus.Failed, mode, rootTable, rootId,
                "the record's related records could not be read");
        }

        return await pass.RunAsync().ConfigureAwait(false);
    }

    /// <summary>UAC task 194: the detail of a row the ordinary-parent-move pass did not move because its write cap was spent.</summary>
    internal const string OrdinaryMoveCapDetail =
        "the per-run cap on ordinary parent moves was reached; it is carried to the next run";

    /// <summary>UAC task 194: the ordinary-parent-move pass's settings — the Secure Record business unit, and its write cap.</summary>
    private sealed record OrdinaryMove(Guid? SecureBusinessUnit, int MaxWrites);

    private const string ServiceRequestTable = "sprk_servicerequest";

    /// <summary>
    /// UAC task 194 F4-b: the lookups that file a row of <paramref name="table"/> under a service request — the solution's
    /// 1:N relationships from <c>sprk_servicerequest</c> (<see cref="ExternalDataService.ServiceRequestRelations"/>, the one
    /// declaration, which <c>RecordOwnershipResolver.FilingParentOf</c> reads too). Walked by the ORDINARY-parent-move pass
    /// only: a service request is deliberately not in the secure family (<see cref="SecureChildLineage"/> remarks), so the
    /// secure pass's walk never follows them.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, string>> OrdinaryOnlyServiceRequestLookups(string table) =>
        ExternalDataService.ServiceRequestRelations
            .Where(r => string.Equals(r.EntityLogicalName, table, StringComparison.OrdinalIgnoreCase))
            .Select(r => new KeyValuePair<string, string>(r.LookupAttribute, ServiceRequestTable));

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

    /// <summary>The two tables <see cref="ReownOrdinaryRowAsync"/> re-owns (UAC task 183, owner round 96).</summary>
    internal static readonly IReadOnlySet<string> OrdinaryReownTables =
        new HashSet<string>(StringComparer.Ordinal) { "sprk_decisionrecord", "sprk_signal" };

    /// <summary>
    /// UAC task 183 (owner round 96, D-135): re-owns ONE ordinary Decision Record or Signal row to an ordinary TEAM — the
    /// entry point ontology task 148's follow-the-assignee job re-owns both tables through, so the Secure refusals live in
    /// one place (no identity is given Write on a Decision Record). It never moves a row into or out of the Secure Record
    /// business unit: that stays this class's own pass.
    /// </summary>
    /// <remarks>
    /// <para><b>Refuses without writing</b> when the row's CURRENT owner, or <paramref name="teamId"/>, is a team in the Secure
    /// Record business unit (<see cref="OrdinaryRowReownOutcome.RefusedSecure"/>) — ANY team there, not only the named owner
    /// team, so the guard holds once each secure family has its own owner team (task 178); when the row is user-owned
    /// (<see cref="OrdinaryRowReownOutcome.RefusedNotTeamOwned"/>); and when the row, a team, or which business unit is the
    /// Secure Record one cannot be read or told (<see cref="OrdinaryRowReownOutcome.Failed"/>: two business units with the
    /// name are ambiguous, as in <c>RecordOwnershipResolver</c>, and a Dataverse fault is never read as "not secure"). The
    /// row's owner and both teams' business units are read fresh. A row already owned by the team is
    /// <see cref="OrdinaryRowReownOutcome.AlreadyOwned"/>, unwritten.</para>
    /// <para><b>Otherwise</b> it makes the pass's own owner write (<see cref="WriteOwnerAndReadBackAsync"/>, shared with
    /// <c>AssignAsync</c>): an owner-only update, the owner-change eviction once per write attempt, and the read-back
    /// decides — a write that faulted but landed is <see cref="OrdinaryRowReownOutcome.Reowned"/>. The update is CONDITIONAL
    /// on the version the guard read (<c>If-Match</c>, <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>): a row changed
    /// in between is <see cref="OrdinaryRowReownOutcome.Conflict"/>, a row deleted in between
    /// <see cref="OrdinaryRowReownOutcome.NotFound"/> — nothing written, nothing evicted, no read-back. The application identity
    /// and client are the reconciler's own; nothing new is granted.</para>
    /// </remarks>
    /// <param name="table"><c>sprk_decisionrecord</c> or <c>sprk_signal</c>; anything else throws.</param>
    /// <param name="id">The row (not <see cref="Guid.Empty"/>).</param>
    /// <param name="teamId">The ordinary team to own it (not <see cref="Guid.Empty"/>).</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentException">Another table, or an empty id or team.</exception>
    public Task<OrdinaryRowReownResult> ReownOrdinaryRowAsync(string table, Guid id, Guid teamId, CancellationToken ct) =>
        ReownOrdinaryRowCoreAsync(table, id, teamId, anyLineageChild: false, ct);

    /// <summary>
    /// The guarded conditional re-own behind <see cref="ReownOrdinaryRowAsync"/> (UAC task 183) — every guard and the write in
    /// one member. UAC task 194 (#2339): the ordinary-parent-move pass (<see cref="ReconcileOrdinaryParentMoveAsync"/>) re-owns
    /// a row of ANY lineage child table through it (<paramref name="anyLineageChild"/>), so its moves take exactly the same
    /// Secure refusals, user-owned refusal and If-Match write. The public entry point keeps its two-table contract.
    /// </summary>
    private async Task<OrdinaryRowReownResult> ReownOrdinaryRowCoreAsync(
        string? table, Guid id, Guid teamId, bool anyLineageChild, CancellationToken ct)
    {
        var name = table?.Trim().ToLowerInvariant();
        if (name is null || !(OrdinaryReownTables.Contains(name) || anyLineageChild && SecureChildLineage.IsChild(name)))
            throw new ArgumentException("Only a Decision Record or a Signal is re-owned here.", nameof(table));
        if (id == Guid.Empty)
            throw new ArgumentException("The row id is empty.", nameof(id));
        if (teamId == Guid.Empty)
            throw new ArgumentException("The team id is empty.", nameof(teamId));

        static OrdinaryRowReownResult Result(OrdinaryRowReownOutcome outcome, string? detail = null) => new(outcome, detail);

        Guid? secureBu;
        Entity? row;
        Guid? targetBu;
        Guid? ownerTeamBu = null;
        try
        {
            var bu = await SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync(_dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (bu.Ambiguous)
            {
                _logger.LogError("[ORDINARY-REOWN] {Table} {Id}: more than one business unit carries the Secure Record name, so " +
                    "whether a team is in it cannot be told; refused.", name, id);
                return Result(OrdinaryRowReownOutcome.Failed,
                    "refused: more than one business unit carries the Secure Record name, so whether a team is in it cannot be told");
            }

            secureBu = bu.Id;

            var query = new QueryExpression(name)
            {
                ColumnSet = new ColumnSet(OwningTeamColumn, OwningUserColumn, OwningBusinessUnitColumn, VersionNumberColumn),
                TopCount = 1,
                NoLock = true,
            };
            query.Criteria.AddCondition(name + "id", ConditionOperator.Equal, id);
            row = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (row is null)
                return Result(OrdinaryRowReownOutcome.NotFound, "the row does not exist; nothing was written");

            targetBu = await ReadTeamBusinessUnitAsync(teamId, ct).ConfigureAwait(false);
            if (OwnerOf(row) is { Kind: DataversePrincipalKind.Team } owner && owner.Id != teamId)
                ownerTeamBu = await ReadTeamBusinessUnitAsync(owner.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[ORDINARY-REOWN] {Table} {Id}: the row, a team or the Secure Record business unit could not be " +
                "read; nothing was written.", name, id);
            return Result(OrdinaryRowReownOutcome.Failed,
                "refused: the row, a team or the Secure Record business unit could not be read");
        }

        var current = OwnerOf(row);
        var rowBu = row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;
        var currentTeamBu = current is { Kind: DataversePrincipalKind.Team } owning
            ? owning.Id == teamId ? targetBu : ownerTeamBu
            : null;

        // ── The Secure guard: never OUT of the Secure Record business unit (the row's owner, or its own business unit —
        //    which can differ from its owner's where records are owned across business units), never INTO it. ────────
        if (secureBu is { } secureOut && (currentTeamBu == secureOut || rowBu == secureOut))
        {
            return Result(OrdinaryRowReownOutcome.RefusedSecure,
                "the row is owned in the Secure Record business unit; only the secure pass moves it");
        }

        if (targetBu is null)
            return Result(OrdinaryRowReownOutcome.Failed, $"refused: team {teamId:D} does not exist or has no business unit");
        if (secureBu is { } secureIn && targetBu == secureIn)
        {
            return Result(OrdinaryRowReownOutcome.RefusedSecure,
                $"team {teamId:D} is in the Secure Record business unit; only the secure pass moves a row into it");
        }

        if (current is { Kind: DataversePrincipalKind.SystemUser })
            return Result(OrdinaryRowReownOutcome.RefusedNotTeamOwned, "the row is owned by a user, not a team");
        if (current is not { Kind: DataversePrincipalKind.Team } currentTeam)
            return Result(OrdinaryRowReownOutcome.Failed, "refused: the row's owner could not be read");
        if (currentTeamBu is null)
            return Result(OrdinaryRowReownOutcome.Failed,
                $"refused: the row's owning team {currentTeam.Id:D} does not exist or has no business unit");
        if (currentTeam.Id == teamId)
            return Result(OrdinaryRowReownOutcome.AlreadyOwned);

        // F1 (verifier, fix round): the write is CONDITIONAL on the version the guard read. Another writer landing between
        // the guard and the write (the secure pass moving the row INTO isolation, a re-file, another re-own) changes the
        // version, so Dataverse refuses this write (412) and nothing is written - the guard's answer is never acted on
        // stale. Through the reconciler's own Web API client (the one its cascade placement writes with): no new identity.
        if (row.GetAttributeValue<long?>(VersionNumberColumn) is not { } version
            || !SecureChildLineage.Children.TryGetValue(name, out var lineage))
            return Result(OrdinaryRowReownOutcome.Failed, "refused: the row's version could not be read");
        var etag = "W/\"" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"";

        // Reversal evidence BEFORE the write (as every assign of this class).
        _logger.LogInformation("[ORDINARY-REOWN] reassign: {Table} {Id} owner {Previous} -> team {Target} (if at version {Version}).",
            name, id, Describe(current), teamId, version);
        var ownerBind = new Dictionary<string, object> { [OwnerBindColumn] = $"/teams({teamId:D})" };
        try
        {
            System.Net.HttpStatusCode? writeStatus = null;
            var fault = await WriteOwnerAndReadBackAsync(
                name, id, DataversePrincipalRef.Team(teamId),
                c => _webApi.UpdateIfMatchAsync(lineage.EntitySet, id, ownerBind, etag, c),
                lineage.EntitySet, $"reown-ordinary-row:{name}:{id:D}", ct,
                decisiveRefusal: ex => ex is System.Data.DBConcurrencyException or KeyNotFoundException,
                onWriteFault: ex => writeStatus = (ex as System.Net.Http.HttpRequestException)?.StatusCode).ConfigureAwait(false);
            return fault is null
                ? Result(OrdinaryRowReownOutcome.Reowned)
                : Result(OrdinaryRowReownOutcome.Failed, fault) with { WriteFailureStatusCode = writeStatus };
        }
        catch (System.Data.DBConcurrencyException)
        {
            _logger.LogWarning("[ORDINARY-REOWN] {Table} {Id} changed after its owner was checked (version {Version}); not " +
                "re-owned (conflict).", name, id, version);
            return Result(OrdinaryRowReownOutcome.Conflict, "the row changed after its owner was checked; nothing was written");
        }
        catch (KeyNotFoundException)
        {
            return Result(OrdinaryRowReownOutcome.NotFound, "the row no longer exists; nothing was written");
        }
    }

    /// <summary>A team's business unit, or <c>null</c> when the team does not exist or names none. A fault propagates.</summary>
    private async Task<Guid?> ReadTeamBusinessUnitAsync(Guid teamId, CancellationToken ct)
    {
        var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 1, NoLock = true };
        query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        var team = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
        return team?.GetAttributeValue<EntityReference>("businessunitid")?.Id is { } bu && bu != Guid.Empty ? bu : null;
    }

    /// <summary>
    /// The ONE owner-write sequence of this class (UAC task 183 factored it out of the pass's <c>AssignAsync</c> and
    /// <c>RestoreStartOwnerAsync</c>, so <see cref="ReownOrdinaryRowAsync"/> cannot drift from them): the caller's owner-only
    /// write, the owner-change eviction once per write ATTEMPT (a write that reports failure can have committed), then the
    /// read-back decides (the task 133 restore rule). <c>null</c> on success, else what went wrong.
    /// </summary>
    /// <remarks>
    /// <para>Each caller builds its <c>ownerid</c> value itself, inside its own member, so the owner-write census attributes
    /// every write to the member that decided its owner (<c>RecordOwnerAssignmentCensusTests</c>), and names the owner it
    /// expects to read back (<paramref name="expected"/>: a user, for a put-back onto a user-owned start owner).</para>
    /// <para><paramref name="decisiveRefusal"/> (the re-own entry point's conditional write only; the pass passes none, so its
    /// sequence is unchanged): a fault for which it answers <c>true</c> PROVES nothing was written (a 412 version mismatch,
    /// a 404), so it propagates to the caller with no eviction and no read-back. Every other fault keeps the read-back
    /// rule.</para>
    /// </remarks>
    private async Task<string?> WriteOwnerAndReadBackAsync(
        string table, Guid id, DataversePrincipalRef expected, Func<CancellationToken, Task> write, string? entitySet,
        string evictionReason, CancellationToken ct, Func<Exception, bool>? decisiveRefusal = null,
        Action<Exception>? onWriteFault = null)
    {
        string? writeFault = null;
        try
        {
            await write(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when ((ex is not OperationCanceledException || !ct.IsCancellationRequested)
                                   && decisiveRefusal?.Invoke(ex) != true)
        {
            writeFault = ex.Message;
            onWriteFault?.Invoke(ex);
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] Dataverse refused the owner write on {Table} {Id} ({Owner}).",
                table, id, Describe(expected));
        }

        await EvictOwnerChangeAsync(table, id, entitySet, evictionReason).ConfigureAwait(false);
        return await ReadBackAsync(table, id, expected, writeFault, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Batch-4 integration, 148 × 132: an owner write stales the access caches exactly as a root's re-own does. This evicts
    /// through the ONE hook (<see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync"/>)
    /// once per write ATTEMPT, whatever its read-back says, because a write that reports failure can have committed. It
    /// runs on <see cref="CancellationToken.None"/>, so a disconnected caller cannot leave the clean-up half done. It
    /// never fails the caller: the hook does not throw by contract, and a fault anyway is logged and swallowed. The TTLs are
    /// the backstop.
    /// </summary>
    private async Task EvictOwnerChangeAsync(string table, Guid id, string? entitySet, string reason)
    {
        var set = entitySet
            ?? (SecureChildLineage.Children.TryGetValue(table, out var lineage) ? lineage.EntitySet : null)
            // UAC task 099b review round 2: the pass also re-owns the root's grant rows, which are not lineage children.
            ?? (string.Equals(table, GrantTable, StringComparison.OrdinalIgnoreCase) ? GrantEntitySet : null);
        if (set is null)
        {
            _logger.LogError("[SECURE-CHILD-RECONCILE] {Table} {Id} was re-owned, but its entity set is not known, so its cached " +
                             "access snapshot was not evicted (the TTL backstop applies).", table, id);
            return;
        }

        try
        {
            await _accessCacheInvalidator.InvalidateRecordOwnerChangeAsync(table, set, id, reason, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SECURE-CHILD-RECONCILE] Evicting the caches after re-owning {Table} {Id} failed; the TTLs " +
                               "are the backstop.", table, id);
        }
    }

    /// <summary>Whether a row reads as owned by <paramref name="owner"/> after an owner write; <c>null</c> when it does.</summary>
    private async Task<string?> ReadBackAsync(
        string table, Guid id, DataversePrincipalRef owner, string? writeFault, CancellationToken ct)
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
            var after = (await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (after is not null && OwnerOf(after) == owner)
                return null;

            return writeFault is null
                ? "the re-own was accepted but did not read back"
                : $"Dataverse refused the re-own ({writeFault})";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id} could not be read back after its re-own.", table, id);
            return "the re-own could not be read back";
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

    /// <summary>
    /// unified-access-control-r2 task 147 r1c (owner round 28 item 1; round 36: "then 148/149's child pass runs when the event
    /// moves under or out of a secure parent") — the ONE step after a re-file, for EVERY re-file writer: the browser routes
    /// (<c>PATCH /api/v1/child-records/{table}/{id}</c>, the event and communication filing routes, <c>PUT /api/v1/documents/{id}</c>),
    /// the chat update tool, the Office reconciled-email filing, the playbook update node, the output orchestrator's generic
    /// update and the field-mapping push (#2091 round 1-4), and a message joining a record thread — <c>ThreadResolver</c>'s join
    /// and <c>CommunicationService</c>'s respond-into-thread, through <c>ThreadResolver.JoinThreadAsync</c> with
    /// <see cref="RefileThreadStep.PlacedByWriter"/> (#2091). <c>RefileWriterAfterRefileGuardTests</c> holds every writer to it.
    /// A writer runs it after a successful write that moved a filing lookup, and not when the reparent answered
    /// <see cref="RecordOwnerResolution.ParentsUnchanged"/>. Never throws; the re-file it follows stands.
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
    /// <para><b>A re-filed communication</b> also has its thread follow the filing — FIRST, before (1) (#2110: a join the
    /// reconcile makes can re-own the email into a Secure record's isolation, and (1), (2) and the gate step must see it; a row
    /// that LEFT isolation has its old mirror taken off just before the reconcile, whose read grant to the new record the
    /// removal would otherwise take off too); skipped when the writer placed the thread itself —
    /// <see cref="RefileThreadStep.PlacedByWriter"/>. Last, ONCE, its gate Decision Records are re-owned (ontology task 042b,
    /// D-169) — on EVERY communication re-file, whichever <see cref="RefileThreadStep"/>.</para>
    /// </remarks>
    /// <param name="childLogicalName">The re-filed row's table.</param>
    /// <param name="childId">The re-filed row.</param>
    /// <param name="wasIsolated">Whether the row was owned by the Secure Record owner team BEFORE the re-file.</param>
    /// <param name="ct">Cancellation (writers pass <see cref="CancellationToken.None"/>: the write has landed).</param>
    /// <returns>The descendants' pass, or <c>null</c> when none ran.</returns>
    public Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, CancellationToken ct) =>
        AfterRefileCoreAsync(childLogicalName, childId, wasIsolated, RefileThreadStep.Reconcile, secureExitCaller: null, reparent: null, ct);

    /// <summary>
    /// <see cref="AfterRefileAsync(string, Guid, Func{Task{bool}}, CancellationToken)"/> for a writer that holds the reparent's
    /// answer (<see cref="IRecordOwnershipResolver.ReparentAsync"/>). #2159 slice 2: when a re-filed DOCUMENT left a secure
    /// root with the caller's F3 permission (<see cref="RecordOwnerResolution.SecureExitPermitted"/>), the release that lets its
    /// file follow it from S1's own container into S2's is recorded FIRST (<see cref="DocumentContainerRelocator.RecordReleaseAsync"/>);
    /// without it the widening guard holds the file in S1 (A1). Never throws; the re-file stands either way.
    /// </summary>
    public Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RecordOwnerResolution? reparent, CancellationToken ct) =>
        AfterRefileCoreAsync(childLogicalName, childId, wasIsolated, RefileThreadStep.Reconcile, secureExitCaller: null, reparent, ct);

    /// <summary>
    /// <see cref="AfterRefileAsync(string, Guid, Func{Task{bool}}, CancellationToken)"/>, saying whether a re-filed communication's
    /// thread follows the filing (#2091): a writer that placed the message in its thread passes
    /// <see cref="RefileThreadStep.PlacedByWriter"/> and every other step runs.
    /// </summary>
    public Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RefileThreadStep thread, CancellationToken ct) =>
        AfterRefileCoreAsync(childLogicalName, childId, wasIsolated, thread, secureExitCaller: null, reparent: null, ct);

    /// <summary>
    /// <see cref="AfterRefileAsync(string, Guid, Func{Task{bool}}, RefileThreadStep, CancellationToken)"/> for a re-file a PERSON
    /// made (#2143 round 7, F1): <paramref name="secureExitCaller"/> is that re-file's
    /// <see cref="Sprk.Bff.Api.Services.Dataverse.RecordReparent.SecureExitCaller"/>, carried into the thread reconcile
    /// (<see cref="Sprk.Bff.Api.Services.Communication.IThreadResolver.ReconcileThreadForFilingAsync(Guid, Sprk.Bff.Api.Services.Communication.FilingReconcileHost, SecureRemovalCaller?, Func{CancellationToken, Task}?, CancellationToken)"/>),
    /// so a re-filed communication's move off a secure record's thread is decided on the rights the re-file was checked
    /// against. Every user-initiated re-file writer calls this overload, or the one that also takes the reparent's answer;
    /// <c>null</c> acts for nobody.
    /// </summary>
    public Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RefileThreadStep thread,
        SecureRemovalCaller? secureExitCaller, CancellationToken ct) =>
        AfterRefileCoreAsync(childLogicalName, childId, wasIsolated, thread, secureExitCaller, reparent: null, ct);

    /// <summary>
    /// <see cref="AfterRefileAsync(string, Guid, Func{Task{bool}}, RefileThreadStep, SecureRemovalCaller?, CancellationToken)"/>
    /// for a writer that also holds the reparent's answer (<see cref="IRecordOwnershipResolver.ReparentAsync"/>): the thread
    /// reconcile acts for <paramref name="secureExitCaller"/> (#2143 round 7), and a re-file that left a secure root with that
    /// caller's F3 permission (<see cref="RecordOwnerResolution.SecureExitPermitted"/>) records the release of its document's
    /// file — and of the files of the documents filed under it — first (#2159 slice 2).
    /// </summary>
    public Task<SecureChildReconcileReport?> AfterRefileAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RefileThreadStep thread,
        SecureRemovalCaller? secureExitCaller, RecordOwnerResolution? reparent, CancellationToken ct) =>
        AfterRefileCoreAsync(childLogicalName, childId, wasIsolated, thread, secureExitCaller, reparent, ct);

    private async Task<SecureChildReconcileReport?> AfterRefileCoreAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RefileThreadStep thread,
        SecureRemovalCaller? secureExitCaller, RecordOwnerResolution? reparent, CancellationToken ct)
    {
        var permit = reparent?.SecureExitPermitted is { RootsLeft.Count: > 0 } given ? given : null;
        if (permit is not null && string.Equals(childLogicalName.Trim(), DocumentTable, StringComparison.OrdinalIgnoreCase))
        {
            await RecordFileReleaseAsync(childId, permit, "re-filed", ct).ConfigureAwait(false);
        }

        var report = await RefileStepsAsync(childLogicalName, childId, wasIsolated, thread, secureExitCaller, ct)
            .ConfigureAwait(false);

        // #2159 slice 2c: the documents filed under the re-filed row followed it out of S1 under the same F3 act (owner round
        // 24 item 2), so each of their files is released the same way — each checked by the relocator against the roots
        // this re-file left (a document isolated under another secure record stays held). Slice 4: their lock waits share
        // one budget (DocumentContainerRelocator.DescendantReleaseLockBudget).
        if (permit is not null && report is { IsolatedDocumentIds.Count: > 0 })
        {
            var descendants = report.IsolatedDocumentIds.Where(id => id != childId).Distinct().ToList();
            if (descendants.Count > 0)
            {
                await RecordDescendantReleasesAsync(childLogicalName.Trim(), childId, descendants, permit, ct).ConfigureAwait(false);
            }
        }

        // Ontology task 042b (D-169, uac-r2 choice A): after UAC's own steps, on EVERY communication re-file, its gate Decision Records
        // still owned by a user are re-owned to the resolver's team (a Secure answer is left to this engine). Never thrown.
        if (string.Equals(childLogicalName.Trim(), CommunicationTable, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await _gateDecisionRecords.ReownAfterFilingAsync(childId).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed, but its gate Decision Records were not re-owned.",
                    CommunicationTable, childId);
            }
        }

        return report;
    }

    /// <summary>
    /// #2159 slice 2 — the F3 release of one document's file (the re-filed document, or one filed under the re-filed row):
    /// the re-file left at least one secure root with the caller's F3 permission, so the ONE ledger writer
    /// (<see cref="DocumentContainerRelocator.RecordReleaseAsync"/>) records it under the document's lock, after checking the
    /// file's secure record is one of those roots. Only a written re-file's own permission. Never throws — a release not
    /// recorded inline (lock held, a fault) is retried through the job queue (<see cref="RelocationReleaseRetryJobHandler"/>);
    /// until it is recorded, or when it cannot be (another secure record, no retry queued), the file stays held where it is
    /// (fail closed); the re-file stands.
    /// </summary>
    private async Task RecordDescendantReleasesAsync(
        string table, Guid rowId, IReadOnlyList<Guid> documentIds, SecureExitPermit permit, CancellationToken ct)
    {
        if (_relocator is null)
        {
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] {Table} {Id} left {Count} secure record(s) with F3, but no relocator is available to "
                + "record the releases of the files of the documents filed under it ({Documents}); they stay held where they are.",
                table, rowId, permit.RootsLeft.Count, string.Join(", ", documentIds));
            return;
        }

        IReadOnlyList<(Guid DocumentId, RelocationReleaseOutcome Outcome)> outcomes;
        try
        {
            outcomes = await _relocator.RecordReleasesAsync(documentIds, permit, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed out of a secure record with F3, but the releases of the files "
                + "of the documents filed under it could not be recorded and no retry was queued ({Documents}); their files stay "
                + "held where they are. Recovery for each: {Recovery}.", table, rowId, string.Join(", ", documentIds),
                DocumentContainerRelocator.UnreleasedRecovery);
            return;
        }

        foreach (var (documentId, release) in outcomes)
        {
            _logger.Log(
                release.State is RelocationReleaseState.Recorded or RelocationReleaseState.NotNeeded ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CHILD-RECONCILE] sprk_document {Id} (filed under re-filed {Table} {RowId}) left {Count} secure record(s) "
                + "with F3: release {State} ({Detail}).", documentId, table, rowId, permit.RootsLeft.Count, release.State, release.Detail);
        }

        // A file left held because its lock was busy or a write failed is retried through the job queue; one whose retry could
        // not be queued is released only by a new F3 re-file out of the secure record holding it: name them all, by id.
        var notReleased = outcomes
            .Where(o => o.Outcome.State is RelocationReleaseState.LockHeld or RelocationReleaseState.Failed)
            .ToList();
        var queued = notReleased.Where(o => o.Outcome.RetryQueued).Select(o => o.DocumentId).ToList();
        var stranded = notReleased.Where(o => !o.Outcome.RetryQueued).Select(o => o.DocumentId).ToList();
        if (queued.Count > 0)
        {
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed with F3, but the files of {Count} document(s) filed under it "
                + "were not released yet (a busy lock or a failed write); a retry is queued for each: {Documents}.",
                table, rowId, queued.Count, string.Join(", ", queued));
        }

        if (stranded.Count > 0)
        {
            _logger.LogError(
                "[SECURE-CHILD-RECONCILE] {Table} {Id} was re-filed with F3, but the files of {Count} document(s) filed under it "
                + "were NOT released and no retry could be queued; they stay held: {Documents}. Recovery for each: {Recovery}.",
                table, rowId, stranded.Count, string.Join(", ", stranded), DocumentContainerRelocator.UnreleasedRecovery);
        }
    }

    private async Task RecordFileReleaseAsync(Guid childId, SecureExitPermit permit, string how, CancellationToken ct)
    {
        if (_relocator is null)
        {
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] sprk_document {Id} left {Count} secure record(s) with F3, but no relocator is available "
                + "to record the release of its file; the file stays held where it is.", childId, permit.RootsLeft.Count);
            return;
        }

        try
        {
            var release = await _relocator.RecordReleaseAsync(childId, permit, ct).ConfigureAwait(false);
            _logger.Log(
                release.State is RelocationReleaseState.Recorded or RelocationReleaseState.NotNeeded ? LogLevel.Information : LogLevel.Warning,
                "[SECURE-CHILD-RECONCILE] sprk_document {Id} ({How}) left {Count} secure record(s) with F3: release {State} ({Detail}).",
                childId, how, permit.RootsLeft.Count, release.State, release.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "[SECURE-CHILD-RECONCILE] sprk_document {Id} was re-filed out of a secure record with F3, but the release of its "
                + "file could not be recorded and no retry was queued; the file stays held where it is. Recovery: {Recovery}.",
                childId, DocumentContainerRelocator.UnreleasedRecovery);
        }
    }

    private async Task<SecureChildReconcileReport?> RefileStepsAsync(
        string childLogicalName, Guid childId, Func<Task<bool>> wasIsolated, RefileThreadStep thread,
        SecureRemovalCaller? secureExitCaller, CancellationToken ct)
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

        Func<Task<bool>> isolatedBefore =
            () => beforeFault is null ? Task.FromResult(before!.Value) : Task.FromException<bool>(beforeFault);

        // spaarkeai-word-add-in-r1 task 129: a re-filed communication's thread follows the filing, so the email shows on the
        // record's Messages panel (the panel lists threads by the thread's own regarding). Never throws. #2091: not when the
        // writer IS the thread placement (a join) — the reconcile could move the message straight back out of the thread it
        // just joined. #2110 (UAC F1): FIRST, so the mirror, the pass and the gate step below all see the thread the email ends
        // in — a join it makes (a threadless email joining the thread its conversation key finds, anchored to another record)
        // re-owns the email to that record's owner, a Secure one included.
        var reconcilesThread = table == CommunicationTable && _threads is not null && thread == RefileThreadStep.Reconcile;
        var mirrorTakenOff = false;
        var aGrantFollowedTheHook = false;

        // The one thing the mirror did first that must stay first: a row that LEFT isolation loses every direct share it holds
        // (SecureChildShareSynchronizer.RemoveMirrorAsync), so taken off after the reconcile it would also take off the #1629
        // read grant the reconcile gives the record the email now sits under. Once, before the reconcile when the re-file
        // itself took the row out; else (#2143 round 8, F1-A) inside the reconcile, between the join that took it out and
        // that grant — a re-file off S while the email sits in S's thread keeps it Secure until its thread follows the filing.
        // Round 9 (F2): latched only by a removal that COMPLETED — a read that faulted, a row still isolated (mirrored again)
        // or a removal that did not finish leaves it open, so the next chance (the hook, then the last step) tries again.
        async Task TakeTheMirrorOffIfItLeftIsolationAsync()
        {
            if (mirrorTakenOff || before != true || !await LeftIsolationAsync(table, childId, ct).ConfigureAwait(false))
                return;
            // #2143 round 11 (F2-1): not the last try — the last step below retries — so an exhausted removal is a Warning here.
            mirrorTakenOff = await _shares.AfterRefileAsync(table, childId, isolatedBefore, LogLevel.Warning, ct).ConfigureAwait(false);
        }

        if (reconcilesThread)
        {
            await TakeTheMirrorOffIfItLeftIsolationAsync().ConfigureAwait(false);

            // Guarded (#2110 ontology re-check K3): it now runs before the mirror, the pass and the gate step, so a fault here
            // must not skip them. The resolver is best-effort and does not throw today; a fault leaves the thread for the sweep.
            try
            {
                await _threads!.ReconcileThreadForFilingAsync(
                    childId, Sprk.Bff.Api.Services.Communication.FilingReconcileHost.AfterRefileStep, secureExitCaller,
                    _ =>
                    {
                        aGrantFollowedTheHook = true;
                        return TakeTheMirrorOffIfItLeftIsolationAsync();
                    },
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: the thread reconcile after its re-file failed; its " +
                    "mirror, descendants and gate step still run, and the filing sweep reconciles the thread.", table, childId);
            }
        }

        // (1) The row's own mirror, on the owner and thread the reconcile left — one implementation (task 149's synchronizer);
        // it logs a fault reading `before`. After the removal above it only mirrors a row a join took back into isolation.
        var removedNow = await _shares.AfterRefileAsync(
            table, childId, mirrorTakenOff ? static () => Task.FromResult(false) : isolatedBefore, ct).ConfigureAwait(false);
        if (removedNow && aGrantFollowedTheHook)
        {
            // #2143 round 9 (F2): the removal could not be made before the thread move's #1629 grant, so it came off here —
            // and, as it revokes every direct share, the grant with it. Fail closed: an under-share, never a secure share left.
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] {Table} {Id} left isolation, but its secure-child mirror could not be taken off before " +
                "its thread reconcile's read grant, so it was taken off after it: every direct share on it was REVOKED, including " +
                "any read grant that move gave. The record's members cannot read it until it is granted again (#2143).",
                table, childId);
        }

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

    /// <summary>
    /// #2110: whether a row that was isolated before its re-file is no longer owned by the Secure team — it LEFT isolation, so
    /// its mirror comes off before the thread reconcile (<see cref="RefileStepsAsync"/>). <c>true</c> when that cannot be read:
    /// the mirror step then runs first, as it always did, and decides on its own read.
    /// </summary>
    private async Task<bool> LeftIsolationAsync(string table, Guid childId, CancellationToken ct)
    {
        try
        {
            return !await _shares.IsSecureTeamOwnedAsync(table, childId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: whether it is still isolated could not be read before its " +
                "thread reconcile; its mirror step runs first.", table, childId);
            return true;
        }
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

        // #2143 round 6: the record threads this pass's messages sit in that are NOT rows of the pass (a thread regarding a
        // root outside it), read once by RecordThreadFiling, and the ones whose read faulted (thread id -> why).
        private readonly Dictionary<Guid, Entity> _threadsOutsidePass = new();
        private readonly Dictionary<Guid, string> _threadReadFaults = new();
        private readonly Dictionary<string, int[]> _counts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SecureChildRowChange> _changes = new();

        // Report-only (task 148 r2): the owning team each planned move would give its row — what the resolver reads for
        // that row instead of its stored owner (RecordOwnershipContext.PlannedOwningTeams). Always empty when the pass writes.
        private readonly Dictionary<RecordOwnershipParent, Guid> _planned = new();

        // UAC task 099b review round 5 (K perf): this pass's latest answer, per target team, to "may it own a grant row"
        // (GrantOwnerRefusalAsync), so N grant rows to one team cost one privilege read, not N. Local to the pass, never the
        // shared cache: it lives exactly as long as one decision over one root's rows.
        private readonly Dictionary<Guid, string?> _grantOwnerAnswers = new();

        // UAC task 194: set for the ordinary-parent-move pass (ReconcileOrdinaryParentMoveAsync) only; null for every
        // secure transition, whose behaviour it never changes. Its team facts (business unit, default) are read once per team.
        private readonly OrdinaryMove? _ordinary;
        private readonly Dictionary<Guid, (Guid? BusinessUnit, bool IsDefault)> _teamFacts = new();
        private int _ordinaryWritesAttempted;

        // UAC task 194b V-fix (verifier B): the Secure Record business unit and each business unit's default owner team, read
        // once for this pass and handed to every owner question it asks (RecordOwnershipContext.LookupCache). It lives exactly as
        // long as the pass, so a business-unit or team change is seen by the next one.
        private readonly RecordOwnerLookupCache _lookupCache = new();

        // UAC task 194 F1-a: each document's latest ordinary decision — whether its file may be handed to the re-file pass.
        private readonly Dictionary<Guid, bool> _documentHandOff = new();

        // UAC task 194b: each row's ownership question (RecordOwnershipContext.ForExistingRowAsync), built once per pass from the
        // row as the pass read it: its parents, primary first, and whether its owner comes from another rule or cannot be asked.
        private readonly Dictionary<(string Table, Guid Id), ExistingRowOwnership> _questions = new();

        // UAC task 194b fix round (K5): what those questions read — the catalog by record type, the stamps by subject — once
        // per pass.
        private ExistingRowReads? _reads;

        private ExistingRowReads Reads => _reads ??= new ExistingRowReads(_owner._dataverse, _owner._coreAncestors, _owner._links);

        public Pass(
            SecureChildReconciler owner, SecureChildReconcileMode mode, RecordOwnershipParent root, bool rootIsolated,
            bool unsecuring, bool mayRelease, Guid secureTeamId, CancellationToken ct, OrdinaryMove? ordinary = null)
        {
            _ordinary = ordinary;
            if (ordinary is not null)
            {
                // The ordinary pass has just read the Secure Record business unit (not ambiguous, or it would have ended):
                // its questions reuse that answer rather than reading it again per row.
                _lookupCache.SecureBusinessUnit = (ordinary.SecureBusinessUnit, false);
            }

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
                    // F4-b: the ordinary pass also follows a row's service-request lookup; the secure walk never does.
                    var lookups = _ordinary is null
                        ? table.Lookups
                        : table.Lookups.Concat(OrdinaryOnlyServiceRequestLookups(table.LogicalName));
                    var pairs = lookups
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

            // UAC task 194: a grant row is not a lineage child and follows its root only across the isolation boundary here;
            // the ordinary-parent-move pass leaves it (a known limit, notes/task-194-ordinary-parent-move.md).
            if (_ordinary is null)
                await LoadGrantRowsAsync().ConfigureAwait(false);
            await LoadThreadsOutsidePassAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// #2143 round 6: every record thread a message of this pass sits in that is not itself a row of the pass, read the way
        /// the re-file path reads it (<see cref="RecordThreadFiling.ReadThreadAsync"/>: committed, every column) — so a message in
        /// a thread regarding a secure root OUTSIDE this pass is still filed under that root (<see cref="ContextFor"/>). A
        /// preloaded thread is never read again. A read that faults is recorded, not thrown: its messages are left undecided
        /// (<see cref="DecideRowAsync"/> reports them failed and writes nothing), never decided as if the thread filed nothing.
        /// Round 7 (R7e): read in batches like <see cref="LoadDescendantsAsync"/> — one paged <c>id IN (…)</c> query per chunk of
        /// <see cref="SecureChildShareSynchronizer.DescendantConditionsPerQuery"/> ids, not one query per thread. A chunk whose
        /// read faults records every thread of it as faulted (none of them is known); a thread the chunk does not return
        /// (deleted, or not readable) is recorded as faulted too, as the single read did.
        /// </summary>
        private async Task LoadThreadsOutsidePassAsync()
        {
            var threadIds = _descendants
                .Select(d => RecordThreadFiling.ThreadOf(d.Row))
                .OfType<Guid>()
                .Distinct()
                .Where(id => !_byRef.ContainsKey((RecordThreadFiling.ThreadTable, id)))
                .ToArray();

            foreach (var chunk in threadIds.Chunk(SecureChildShareSynchronizer.DescendantConditionsPerQuery))
            {
                var query = new QueryExpression(RecordThreadFiling.ThreadTable) { ColumnSet = new ColumnSet(true), NoLock = false };
                query.Criteria.AddCondition(
                    $"{RecordThreadFiling.ThreadTable}id", ConditionOperator.In, chunk.Select(id => (object)id).ToArray());
                try
                {
                    foreach (var thread in await ReadAllPagesAsync(RecordThreadFiling.ThreadTable, query).ConfigureAwait(false))
                        _threadsOutsidePass[thread.Id] = thread;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Count} record threads (first {ThreadId}): they could not be read, "
                        + "so the messages in them are left as they are (#2143).", chunk.Length, chunk[0]);
                    foreach (var threadId in chunk)
                        _threadReadFaults[threadId] = "its record thread could not be read";
                    continue;
                }

                foreach (var threadId in chunk.Where(id => !_threadsOutsidePass.ContainsKey(id)))
                {
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] record thread {ThreadId}: it could not be read, so the messages in "
                        + "it are left as they are (#2143).", threadId);
                    _threadReadFaults[threadId] = "its record thread could not be read";
                }
            }
        }

        /// <summary>
        /// UAC task 099b review round 2 (F1, Make Secure): the root's <c>sprk_externalrecordaccess</c> rows, as rows of this
        /// pass at level 1. A grant row is owned by its root's team (D-11, 099b), so a transition that moves the root across the
        /// isolation boundary moves them too — otherwise a grant created on an ordinary root stays on that business unit's
        /// default team after Make Secure, readable by every user of the unit (the secure record's name, the contact, the
        /// level), and one on a secure root stays on the memberless Secure team after Unsecure. Review round 3 (F1): in EVERY
        /// state, both directions — an inactive (revoked or expired) grant row still names the record and the contact, so it is
        /// as exposed as an active one, and restoring it later updates it in place without re-owning it. Only a ROOT pass (a
        /// grant is filed under a root, never a child) and only the three grant roots. Paged like every other read here; an
        /// incomplete read throws, so the pass writes nothing.
        /// </summary>
        private async Task LoadGrantRowsAsync()
        {
            if (!_anchorIsRoot)
                return;
            var rootType = Enum.GetValues<Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType>()
                .Where(t => string.Equals(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRoot.LogicalNameFor(t),
                    _root.EntityLogicalName, StringComparison.OrdinalIgnoreCase))
                .Select(t => (Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType?)t)
                .FirstOrDefault();
            if (rootType is null)
                return;

            var query = new QueryExpression(GrantTable) { ColumnSet = new ColumnSet(true), NoLock = true };
            // The grant's root lookup is the root table's own logical name (sprk_project / sprk_matter / sprk_workassignment).
            query.Criteria.AddCondition(_root.EntityLogicalName, ConditionOperator.Equal, _root.RecordId);

            foreach (var entity in await ReadAllPagesAsync(GrantTable, query).ConfigureAwait(false))
            {
                var key = (GrantTable, entity.Id);
                if (_byRef.ContainsKey(key))
                    continue;
                _descendants.Add((1, entity));
                _byRef[key] = entity;
            }
        }

        /// <summary>A grant row of this pass (<see cref="LoadGrantRowsAsync"/>).</summary>
        private static bool IsGrant(string table) => string.Equals(table, GrantTable, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// UAC task 099b review round 2: before a grant row is assigned to <paramref name="target"/>, whether that team may own
        /// one — the grant routes' ONE helper (<c>GrantExternalAccessEndpoint.CheckGrantOwnerPrivilegeAsync</c>). <c>null</c>
        /// when it may (or no reader was given — Dataverse's own owner check then decides at the write); otherwise why not, so
        /// the row is reported Failed WITHOUT a write and the pass is incomplete (children_incomplete), like any row this pass
        /// could not move.
        /// </summary>
        /// <param name="fresh">Review round 5 (K2): <c>true</c> after Dataverse refused the assignment on a privilege check —
        /// asked past the shared cache (as the grant core classifies its refused create), so a YES remembered before an
        /// administrator removed the privilege neither names the refusal wrongly nor stays cached. Otherwise the pass's own
        /// answer for <paramref name="target"/> is reused (one read per team per pass, review round 5 K perf).</param>
        private async Task<string?> GrantOwnerRefusalAsync(Guid target, bool fresh = false)
        {
            if (_owner._ownerPrivileges is not { } privileges)
                return null;
            if (!fresh && _grantOwnerAnswers.TryGetValue(target, out var known))
                return known;
            var rootType = Enum.GetValues<Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRootType>()
                .First(t => string.Equals(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantRoot.LogicalNameFor(t),
                    _root.EntityLogicalName, StringComparison.OrdinalIgnoreCase));
            var refusal = await Sprk.Bff.Api.Api.ExternalAccess.GrantExternalAccessEndpoint.CheckGrantOwnerPrivilegeAsync(
                privileges, target, rootType, _root.RecordId, key: null, fresh, Log, _ct).ConfigureAwait(false);
            // Review round 3 (K2): the detail reaches the endpoint's problem detail, so it names no team id (the helper has
            // logged it); the privilege refusal's detail is the one IsGrantOwnerPrivilegeRefusal recognises.
            var answer = refusal is null
                ? null
                : refusal.ReasonCode == Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalGrantLifecycle.GrantOwnerPrivilegeMissingReasonCode
                    ? GrantOwnerPrivilegeMissingDetail
                    : $"{refusal.ReasonCode}: whether the owner team may own an access grant could not be read; it is left as it is";
            _grantOwnerAnswers[target] = answer;
            return answer;
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
            //    UAC task 194: not in the ordinary-parent-move pass — the root's own Assign already cascaded them.
            if (_anchorIsRoot && _ordinary is null)
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
            await LoadOwnershipQuestionsAsync(ordered).ConfigureAwait(false);
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
                // UAC task 194: ordinary-to-ordinary moves — proposed only by the ordinary-parent-move pass, never with an
                // In or Out (that pass decides no isolation move), so a secure pass's rounds are unchanged.
                var ordinaries = keys.Where(k => Open(k) && decisions[k].Move == RowMove.Ordinary).ToList();
                if (ins.Count == 0 && outs.Count == 0 && ordinaries.Count == 0)
                    break;

                if (round > maxRounds)
                {
                    foreach (var k in ins.Concat(outs).Concat(ordinaries))
                        failures[k] = "its owner did not settle within this pass; it is left as it is for the next pass";
                    break;
                }

                var certainOuts = outs.Where(k => !ancestors[k].Overlaps(ins)).ToList();
                var batch = certainOuts.Count > 0 ? certainOuts
                    : ins.Count > 0 ? keys.Where(ins.Contains).ToList()
                    : outs.Count > 0 ? outs
                    : ordinaries;

                foreach (var key in batch)
                {
                    var decision = decisions[key];
                    var target = decision.Target!.Value;
                    if (decision.Move == RowMove.Ordinary)
                    {
                        if (await MoveOrdinaryAsync(key, target, current[key]).ConfigureAwait(false) is { } refused)
                        {
                            failures[key] = refused;
                            continue;
                        }

                        current[key] = DataversePrincipalRef.Team(target);
                        decisions[key] = decision with { Move = RowMove.Stay };
                        written.Add(key);
                        continue;
                    }

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
                        // UAC task 099b review round 2: a grant row goes only to a team allowed to own one — asked first, so a
                        // missing privilege is reported with its reason and no write is attempted.
                        if (IsGrant(key.Table) && await GrantOwnerRefusalAsync(target).ConfigureAwait(false) is { } refused)
                        {
                            failures[key] = refused;
                            continue;
                        }

                        // Reversal evidence BEFORE the write (constraint "every assign").
                        Log.LogInformation("[SECURE-CHILD-RECONCILE] reassign: {Table} {Id} owner {Previous} -> team {Target}.",
                            key.Table, key.Id, Describe(current[key]), target);
                        Exception? writeFault = null;
                        var fault = await AssignAsync(key.Table, key.Id, decision.Resolution!, ex => writeFault = ex)
                            .ConfigureAwait(false);
                        if (fault is not null)
                        {
                            // Review round 5 (K2): a grant row's assignment refused on a privilege check is confirmed FRESH, as
                            // the grant core confirms its refused create: named owner_privilege_missing only when the team
                            // really lacks Read (and the stale YES that let the write through is dropped); a team that holds
                            // it, or a check that faults, keeps the generic failure (fail closed, the retry still promised).
                            failures[key] = IsGrant(key.Table)
                                && writeFault is not null
                                && Sprk.Bff.Api.Services.Signals.SignalWriter.IsDataverseAccessDenied(writeFault)
                                && await GrantOwnerRefusalAsync(target, fresh: true).ConfigureAwait(false)
                                    == GrantOwnerPrivilegeMissingDetail
                                    ? GrantOwnerPrivilegeMissingDetail
                                    : fault;
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
                    // UAC task 099b review round 2: a grant row never carries the mirror (task 149 mirrors lineage children
                    // only), so there is nothing to take off it.
                    if (IsGrant(row.LogicalName))
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

                // 3a'. HELD in isolation by a pass that may release (fix round R, A2): the row stays on the Secure team, but
                // the record that isolated it is no longer secure for it, so its mirror of that record's sharees comes off,
                // exactly as a released row's does: no sharee keeps access through a held row. Holding only removes access.
                foreach (var row in ordered)
                {
                    var key = (row.LogicalName, row.Id);
                    if (!decisions[key].HeldInIsolation || results[key].Outcome == SecureChildRowOutcome.Failed
                        || !IsIsolated(current[key]) || IsGrant(row.LogicalName))
                    {
                        continue;
                    }

                    var removal = await _owner._shares.RemoveHeldMirrorAsync(row.LogicalName, row.Id, _ct).ConfigureAwait(false);
                    mirrorRevoked += removal.SharesRevoked;
                    if (removal.IsComplete)
                        continue;

                    mirrorIncomplete++;
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: held in isolation, but its mirrored shares were not " +
                        "all removed ({Status}: {Detail}); the next pass removes the rest.", row.LogicalName, row.Id,
                        removal.Status, removal.Detail);
                    results[key] = results[key] with
                    {
                        Outcome = SecureChildRowOutcome.Failed,
                        Detail = $"held in isolation ({results[key].Detail}), but its mirrored shares could not all be removed " +
                                 $"({removal.Detail}); the next pass removes the rest",
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
                // UAC task 194: an untouched row WITH a detail is one the ordinary-parent-move pass reports and does not move
                // (user-owned, a non-default owner team, a Secure answer); a secure pass never gives an untouched row one.
                if (d.Outcome is not (SecureChildRowOutcome.AlreadyCorrect or SecureChildRowOutcome.Untouched) || d.Detail is not null)
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
                "shares={Shares} mirrorRevoked={MirrorRevoked} mirrorIncomplete={MirrorIncomplete} held={Held}",
                _root.EntityLogicalName, _root.RecordId, _mode, _rootIsolated, _unsecuring, _mayRelease, status,
                tables.Sum(t => t.Examined), tables.Sum(t => t.AlreadyCorrect), tables.Sum(t => t.Changed),
                tables.Sum(t => t.WouldChange), tables.Sum(t => t.Untouched), tables.Sum(t => t.Refused),
                tables.Sum(t => t.Failed), tables.Sum(t => t.NeedsF3), shares?.Status.ToString() ?? "-", mirrorRevoked,
                mirrorIncomplete, keys.Count(k => decisions[k].HeldInIsolation));

            return new SecureChildReconcileReport(
                status, _mode, _root.EntityLogicalName, _root.RecordId, _rootIsolated, tables, _changes, shares,
                mirrorRevoked, mirrorIncomplete, null)
            {
                HeldInIsolation = ordered
                    .Where(r => decisions[(r.LogicalName, r.Id)].HeldInIsolation)
                    .Select(r => (Row: r, Result: results[(r.LogicalName, r.Id)]))
                    .Select(h => new SecureChildRowChange(
                        h.Row.LogicalName, h.Row.Id, h.Result.Previous, h.Result.Target, h.Result.Outcome, h.Result.Detail))
                    .ToList(),
                OrdinaryWritesAttempted = _ordinaryWritesAttempted,
                RestrictedContentOwnerTeam = _lookupCache.RestrictedContentOwnerTeam,
                OrdinaryHandOffDocumentIds = _documentHandOff.Where(d => d.Value).Select(d => d.Key).OrderBy(id => id).ToList(),
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
                // V-fix 3: a Secure-only parent (a copy's canonical) is one its answer rests on too.
                r => ContextFor(r).Parents.Concat(ContextFor(r).SecureOnlyParents)
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
            if (RecordThreadFiling.ThreadOf(row) is { } threadId && _threadReadFaults.TryGetValue(threadId, out var threadFault))
            {
                // #2143 round 6: its record thread's filing is not known — undecided, written nothing, left for the next pass.
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, threadFault);
            }

            if (_ordinary is { } ordinary)
                return await DecideOrdinaryRowAsync(row, current, ordinary).ConfigureAwait(false);

            var question = QuestionFor(row);
            if (question.Undetermined is { } undetermined)
            {
                // 194b fix round (F1-b): the whole question cannot be asked, but the parents the row STORES can. Secure-if-any
                // over parents that are already secure is secure over every parent the row has, so a secure answer still takes
                // it INTO isolation (or keeps it there). Any other answer — out, ordinary, a refusal, a fault — decides nothing.
                if (await SecureOnStoredParentsAsync(row, undetermined).ConfigureAwait(false) is { } secure)
                {
                    return IsIsolated(current)
                        ? new(RowMove.Stay, SecureChildRowOutcome.AlreadyCorrect, secure, null)
                        : new(RowMove.In, SecureChildRowOutcome.Untouched, secure, null);
                }

                Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: its owner cannot be decided ({Why}); it is left as it is.",
                    table, row.Id, undetermined);
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, undetermined);
            }

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

            // 194b fix round: a row isolated when the pass began that no rule here may take out — even Unsecure (a row this pass
            // moved in may still go back to its own start owner: that undoes the pass's own move). K3: the To Do job keeps a
            // no-core row's owner (its own predicate); F1-d (fix round R): a Decision Record whose question is incomplete — in
            // every pass, the re-file pass included. Held isolated and reported; fix round R (A2): a pass that may release takes
            // its mirrored shares off as it does a released row's (HeldInIsolation), so no sharee keeps access through it.
            if (move == RowMove.Out && IsIsolated(start)
                && (question.NoCoreOwnerRule
                    ?? RecordOwnershipContext.DecisionRecordHeldInIsolation(table, ownedInIsolation: true, resolution, question.Incomplete))
                is { } held)
            {
                Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: the rule gives it team {Target}, out of isolation, but it " +
                    "is held isolated ({Why}).", table, row.Id, target, held);
                return new(RowMove.Stay, SecureChildRowOutcome.Untouched, resolution, held, HeldInIsolation: true);
            }

            var kind = current == DataversePrincipalRef.Team(target)
                ? SecureChildRowOutcome.AlreadyCorrect
                : SecureChildRowOutcome.Untouched;
            return new(move, kind, resolution, null);
        }

        /// <summary>
        /// UAC task 194b fix round (F1-b): the rule's answer over the parents an undetermined row STORES
        /// (<see cref="ContextFor"/>, from its question's context), when that answer is the Secure Record owner team; otherwise
        /// <c>null</c> (an ordinary answer, a refusal or a fault decides nothing for such a row).
        /// </summary>
        private async Task<RecordOwnerResolution?> SecureOnStoredParentsAsync(Entity row, string undetermined)
        {
            try
            {
                var stored = await _owner._ownership.ResolveOwnerAsync(ContextFor(row), _ct).ConfigureAwait(false);
                if (stored.IsOwned && stored.OwningTeamId == _secureTeamId)
                {
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] {Table} {Id}: its whole owner question cannot be asked ({Why}), but " +
                        "the parents it stores are secure: it belongs in isolation (secure wins).", row.LogicalName, row.Id, undetermined);
                    return stored;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: the parents it stores could not be asked about either.",
                    row.LogicalName, row.Id);
            }

            return null;
        }

        /// <summary>
        /// UAC task 194b fix round (F1-d): the ordinary pass never decides a row held in isolation, but a Decision Record held
        /// there whose stored parents now give an ordinary team is REPORTED (the one never-out rule,
        /// <see cref="RecordOwnershipContext.DecisionRecordHeldInIsolation"/>); <c>null</c> for every other row, and when its
        /// question cannot be asked or the rule cannot answer (logged).
        /// </summary>
        private async Task<RowDecision?> ReportHeldDecisionRecordAsync(Entity row)
        {
            if (!string.Equals(row.LogicalName, RecordOwnershipContext.DecisionRecordTable, StringComparison.OrdinalIgnoreCase)
                || QuestionFor(row).Undetermined is not null)
            {
                return null;
            }

            try
            {
                var answer = await _owner._ownership.ResolveOwnerAsync(ContextFor(row), _ct).ConfigureAwait(false);
                if (RecordOwnershipContext.DecisionRecordHeldInIsolation(
                        row.LogicalName, ownedInIsolation: true, answer, QuestionFor(row).Incomplete) is { } held)
                {
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: {Why}.", row.LogicalName, row.Id, held);
                    return new(RowMove.Stay, SecureChildRowOutcome.Untouched, answer, held);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: held in isolation; its owner could not be " +
                    "asked about (a read failed).", row.LogicalName, row.Id);
            }

            return null;
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

            return new(decision.Kind, start, decision.Target, decision.Detail, false);
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

            /// <summary>
            /// UAC task 194: from one business unit's default team to another's — the ordinary-parent-move pass only, never
            /// across the isolation boundary.
            /// </summary>
            Ordinary,
        }

        /// <summary>
        /// UAC task 194 — the ordinary-parent-move pass's decision for one row against the owner it has NOW (report-only: would
        /// have). It proposes <see cref="RowMove.Ordinary"/> only for a row owned by a business unit's DEFAULT team that is not
        /// in the Secure Record business unit, whose rule answer is another default team that is not in it either. A row owned
        /// in the Secure Record business unit is never decided (untouched, unreported: the secure pass owns it); a row the
        /// rule gives the Secure team is reported and left to the secure pass; a user-owned row, or one owned by a team that is
        /// not a business unit's default team, is reported when its answer is in another business unit, and never moved.
        /// </summary>
        private async Task<RowDecision> DecideOrdinaryRowAsync(Entity row, DataversePrincipalRef? current, OrdinaryMove ordinary)
        {
            var decision = await DecideOrdinaryRowCoreAsync(row, current, ordinary).ConfigureAwait(false);
            if (string.Equals(row.LogicalName, DocumentTable, StringComparison.OrdinalIgnoreCase))
                _documentHandOff[row.Id] = MayHandOffFile(decision, ordinary);
            return decision;
        }

        /// <summary>
        /// F1-a: whether a document's file may go to the re-file pass — never one held in isolation or in the Secure Record
        /// business unit (decided Stay with no rule answer), never one the rule gives the Secure team (its container answer
        /// is a Secure record's), never one whose owner could not be decided or the rule refused. The relocator's own
        /// refusals still hold for every file handed on.
        /// </summary>
        private bool MayHandOffFile(RowDecision decision, OrdinaryMove ordinary)
        {
            if (decision.Kind is SecureChildRowOutcome.Failed or SecureChildRowOutcome.Refused)
                return false;
            if (decision.Move == RowMove.Stay && decision.Resolution is null)
                return false;
            return decision.Resolution?.OwningTeamId is not { } target
                   || target != _secureTeamId
                   && !(ordinary.SecureBusinessUnit is { } secureBu
                        && _teamFacts.TryGetValue(target, out var facts) && facts.BusinessUnit == secureBu);
        }

        private async Task<RowDecision> DecideOrdinaryRowCoreAsync(Entity row, DataversePrincipalRef? current, OrdinaryMove ordinary)
        {
            var table = row.LogicalName;
            var secureBu = ordinary.SecureBusinessUnit;
            var rowBu = row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id;
            if (IsIsolated(current) || secureBu is { } securedRow && rowBu == securedRow)
                return await ReportHeldDecisionRecordAsync(row).ConfigureAwait(false) ?? new(RowMove.Stay, SecureChildRowOutcome.Untouched, null, null);
            if (current is null)
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, "its owner could not be read");

            // 194b: a row whose question cannot be asked is not decided; a Signal or Decision Record with no core record that
            // TodoAssigneeOwnerReconciliationJob keeps (its own predicate, fix round F1-c) is owned by its assignee's team:
            // reported, never moved here. Every other no-core row is decided over its parents, as its writer decided it.
            var question = QuestionFor(row);
            if (question.Undetermined is { } undetermined)
            {
                Log.LogWarning("[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: its owner cannot be decided ({Why}); it is " +
                    "left as it is.", table, row.Id, undetermined);
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, undetermined);
            }

            if (question.NoCoreOwnerRule is { } otherRule)
                return new(RowMove.Stay, SecureChildRowOutcome.Untouched, null, otherRule);

            (Guid? BusinessUnit, bool IsDefault)? owningTeam = null;
            RecordOwnerResolution resolution;
            Guid? targetBu;
            try
            {
                if (current is { Kind: DataversePrincipalKind.Team } team)
                {
                    owningTeam = await TeamFactsAsync(team.Id).ConfigureAwait(false);
                    if (owningTeam.Value.BusinessUnit is null)
                        return new(RowMove.None, SecureChildRowOutcome.Failed, null,
                            $"its owning team {team.Id:D} does not exist or has no business unit");
                    if (secureBu is { } securedTeam && owningTeam.Value.BusinessUnit == securedTeam)
                        return await ReportHeldDecisionRecordAsync(row).ConfigureAwait(false) ?? new(RowMove.Stay, SecureChildRowOutcome.Untouched, null, null);
                }

                resolution = await _owner._ownership.ResolveOwnerAsync(ContextFor(row), _ct).ConfigureAwait(false);
                if (resolution.IsRefused)
                {
                    Log.LogWarning("[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: the ownership rule refused ({Code}: " +
                        "{Reason}); it is left as it is.", table, row.Id, resolution.RefusalCode, resolution.Reason);
                    return new(RowMove.None, SecureChildRowOutcome.Refused, null, $"{resolution.RefusalCode}: {resolution.Reason}");
                }

                if (!resolution.IsOwned)
                    return new(RowMove.None, SecureChildRowOutcome.Untouched, null, null); // names no parent the rule reads

                targetBu = (await TeamFactsAsync(resolution.OwningTeamId!.Value).ConfigureAwait(false)).BusinessUnit;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: its owner could not be decided (a read " +
                    "failed); it is left as it is.", table, row.Id);
                return new(RowMove.None, SecureChildRowOutcome.Failed, null, "its owner could not be decided");
            }

            var target = resolution.OwningTeamId!.Value;
            if (target == _secureTeamId || secureBu is { } securedTarget && targetBu == securedTarget)
            {
                return new(RowMove.Stay, SecureChildRowOutcome.Untouched, resolution,
                    "the rule gives it the Secure Record owner team: only the secure pass moves a row into isolation");
            }

            // 194b V-fix (verifier A F1): an ordinary owner is never written on PART of a question (a Decision Record whose
            // resolved Signals cannot all be read): an unread Signal's subject could be the secure parent. Not decided: the
            // row fails, so the root is carried and asked again — the rule TodoAssigneeOwnerReconciliationJob applies too.
            if (question.Incomplete is { } incomplete)
            {
                Log.LogWarning("[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id}: its question is incomplete ({Why}); no " +
                    "ordinary owner is written on part of it.", table, row.Id, incomplete);
                return new(RowMove.None, SecureChildRowOutcome.Failed, null,
                    $"its question is incomplete ({incomplete}): no ordinary owner is written on part of it");
            }

            if (current == DataversePrincipalRef.Team(target))
                return new(RowMove.Stay, SecureChildRowOutcome.AlreadyCorrect, resolution, null);
            if (targetBu is null)
            {
                return new(RowMove.None, SecureChildRowOutcome.Failed, null,
                    $"the team the rule gives it ({target:D}) does not exist or has no business unit");
            }

            // R4b-2: a hard-deny row's answer is the restricted-content owner team (root business unit). A row this pass never
            // moves (user-owned, or owned by a non-default team) is still compared with the business unit its ordinary answer
            // names — the default team that team replaced — so its report is the one it had before (rule 1: keep the creator).
            var ordinaryBu = resolution.InPlaceOfDefaultTeamId is { } replaced
                ? (await TeamFactsAsync(replaced).ConfigureAwait(false)).BusinessUnit
                : targetBu;

            if (current.Value.Kind != DataversePrincipalKind.Team)
            {
                return new(RowMove.Stay, SecureChildRowOutcome.Untouched, resolution, rowBu == ordinaryBu
                    ? null
                    : "owned by a user, and the rule gives it a team in another business unit: reported, not moved (I-6 " +
                      "interim — a row created outside the product keeps its creator)");
            }

            if (!owningTeam!.Value.IsDefault)
            {
                return new(RowMove.Stay, SecureChildRowOutcome.Untouched, resolution, owningTeam.Value.BusinessUnit == ordinaryBu
                    ? null
                    : "owned by a team that is not a business unit's default team, and the rule gives it a team in another " +
                      "business unit: reported, not moved");
            }

            return new(RowMove.Ordinary, SecureChildRowOutcome.Untouched, resolution, null);
        }

        /// <summary>A team's business unit (<c>null</c> when it does not exist or names none) and whether it is a default team.</summary>
        private async Task<(Guid? BusinessUnit, bool IsDefault)> TeamFactsAsync(Guid teamId)
        {
            if (_teamFacts.TryGetValue(teamId, out var known))
                return known;
            var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid", "isdefault"), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
            var team = (await _owner._dataverse.RetrieveMultipleAsync(query, _ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            var bu = team?.GetAttributeValue<EntityReference>("businessunitid")?.Id is { } id && id != Guid.Empty ? id : (Guid?)null;
            var facts = (bu, team?.GetAttributeValue<bool?>("isdefault") == true);
            _teamFacts[teamId] = facts;
            return facts;
        }

        /// <summary>
        /// UAC task 194 — one ordinary move: written through the guarded conditional re-own
        /// (<see cref="SecureChildReconciler.ReownOrdinaryRowCoreAsync"/>, which logs the previous owner first, refuses a Secure
        /// owner or target fresh, writes If-Match, evicts and reads back), or, report-only, planned. <c>null</c> on success,
        /// else why the row was not moved (it is then reported failed and the root carried). Past the pass's write cap
        /// nothing is written.
        /// </summary>
        private async Task<string?> MoveOrdinaryAsync((string Table, Guid Id) key, Guid target, DataversePrincipalRef? previous)
        {
            if (_mode == SecureChildReconcileMode.ReportOnly)
            {
                Log.LogInformation(
                    "[SECURE-CHILD-RECONCILE] plan: {Table} {Id} owner {Previous} -> team {Target} (ordinary parent move, report-only).",
                    key.Table, key.Id, Describe(previous), target);
                _planned[PlannedKey(key)] = target;
                return null;
            }

            if (_ordinaryWritesAttempted >= _ordinary!.MaxWrites)
                return OrdinaryMoveCapDetail;
            _ordinaryWritesAttempted++;

            Log.LogInformation("[SECURE-CHILD-RECONCILE] reassign: {Table} {Id} owner {Previous} -> team {Target} (ordinary parent move).",
                key.Table, key.Id, Describe(previous), target);
            OrdinaryRowReownResult result;
            try
            {
                result = await _owner.ReownOrdinaryRowCoreAsync(key.Table, key.Id, target, anyLineageChild: true, _ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
            {
                Log.LogError(ex, "[SECURE-CHILD-RECONCILE] ordinary move {Table} {Id} faulted; it is left as it is.", key.Table, key.Id);
                return "its ordinary move faulted";
            }

            return result.IsOwnedByTeam ? null : $"{result.Outcome}: {result.Detail}";
        }

        /// <summary>One round's decision for one row: the move, and the resolution the rule answered (when it gave an owner).</summary>
        /// <param name="HeldInIsolation">Fix round R (A2): the rule gives the row an ordinary team, but a rule of
        /// <see cref="DecideRowAsync"/> holds it in isolation (K3, F1-d) — in a pass that may release, its mirror comes off.</param>
        private sealed record RowDecision(
            RowMove Move, SecureChildRowOutcome Kind, RecordOwnerResolution? Resolution, string? Detail, bool HeldInIsolation = false)
        {
            public Guid? Target => Resolution is { IsOwned: true } r ? r.OwningTeamId : null;
        }

        /// <summary>One row's outcome over the whole pass.</summary>
        private sealed record RowResult(
            SecureChildRowOutcome Outcome, DataversePrincipalRef? Previous, Guid? Target, string? Detail, bool LeftIsolation);

        /// <summary>
        /// Assigns the row to the team the ownership rule gave it (<paramref name="resolution"/>), in its own update, and reads
        /// it back. <c>null</c> on success, else what went wrong. A write that reports failure may still have landed, so the
        /// read-back decides (the task 133 restore rule). The write, eviction and read-back are the class's ONE sequence
        /// (<see cref="WriteOwnerAndReadBackAsync"/>, UAC task 183), shared with <see cref="ReownOrdinaryRowAsync"/>.
        /// <paramref name="onWriteFault"/> (review round 5, K2) sees the write's fault, so a refused grant assignment can be
        /// classified; it has no default (the owner-write census reads a parameter default as an assignment).
        /// </summary>
        private async Task<string?> AssignAsync(
            string table, Guid id, RecordOwnerResolution resolution, Action<Exception> onWriteFault)
        {
            var teamId = resolution.OwningTeamId!.Value;
            var ownerUpdate = new Dictionary<string, object> { [OwnerColumn] = new EntityReference("team", teamId) };
            return await _owner.WriteOwnerAndReadBackAsync(
                table, id, DataversePrincipalRef.Team(teamId), c => _owner._dataverse.UpdateAsync(table, id, ownerUpdate, c),
                entitySet: null, EvictionReason, _ct, onWriteFault: onWriteFault).ConfigureAwait(false);
        }

        /// <summary>The reason this pass's owner-change evictions carry (batch-4 integration, 148 × 132).</summary>
        private string EvictionReason => $"secure-child-reconcile:{_root.EntityLogicalName}:{_root.RecordId:D}";

        /// <summary>
        /// Batch-4 integration, 148 × 132: an owner write on a row of the pass evicts through the ONE hook — see the class's
        /// <see cref="SecureChildReconciler.EvictOwnerChangeAsync"/> — with this pass's reason.
        /// </summary>
        private Task EvictOwnerChangeAsync(string table, Guid id, string? entitySet = null) =>
            _owner.EvictOwnerChangeAsync(table, id, entitySet, EvictionReason);

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
        /// pass's owner-write sequence (<c>WriteOwnerAndReadBackAsync</c>: write, evict, read back) with <c>AssignAsync</c>;
        /// the put-back on the Web API client would be a second path for the same rows. It is census kind <c>Restore</c>, asserted to write only the start owner its pass recorded.
        /// </remarks>
        private async Task<string?> RestoreStartOwnerAsync(string table, Guid id, DataversePrincipalRef? startOwner)
        {
            if (startOwner is not { } owner)
                return "its owner when this pass began is not known";

            Log.LogInformation("[SECURE-CHILD-RECONCILE] put back: {Table} {Id} -> {Owner} (its owner when this pass began).",
                table, id, Describe(owner));
            var ownerRef = new EntityReference(owner.Kind == DataversePrincipalKind.Team ? "team" : "systemuser", owner.Id);
            var ownerUpdate = new Dictionary<string, object> { [OwnerColumn] = ownerRef };
            return await _owner.WriteOwnerAndReadBackAsync(
                table, id, owner, c => _owner._dataverse.UpdateAsync(table, id, ownerUpdate, c), entitySet: null,
                EvictionReason, _ct).ConfigureAwait(false);
        }

        /// <summary>
        /// The resolver's input for an existing row — the reparent's input (task 146): every ownership-parent lookup it
        /// carries; a message in a record thread also takes that thread's filing (S6); a row naming no parent keeps its
        /// owner; and, mid-unsecure, the root is named as the one whose flag is not a failed provisioning. A GRANT row takes its
        /// root lookup only (UAC task 099b review round 3, K1): the parent the grant core hands the resolver when it creates
        /// the row (<c>GrantExternalAccessEndpoint.ResolveGrantOwnerAsync</c>), so a transition and a create cannot disagree on
        /// its owner — its other lookups (<c>sprk_invoice</c> is an ownership parent) do not file it. A citation lookup (#2084: a
        /// Signal's rule, <see cref="RecordOwnershipResolver.IsCitation"/>) files nothing and is left out.
        /// </summary>
        private RecordOwnershipContext ContextFor(Entity row)
        {
            // UAC task 194b: the row's parents in its writer's order (RecordOwnershipContext.ForExistingRowAsync), so the primary
            // that decides an ordinary row's business unit never depends on the order its attributes were read in.
            var parents = IsGrant(row.LogicalName)
                ? GrantRootParent(row)
                : QuestionFor(row).Context.Parents.ToList();
            // #2143 round 6: the ONE rule for a message's thread parents (RecordThreadFiling — round 7d: by the thread's type, a
            // Direct thread adding nothing), from the pass's preloaded row or,
            // for a thread outside the pass, the one LoadThreadsOutsidePassAsync read (a faulted read decides nothing here:
            // DecideRowAsync never resolves such a row).
            // UAC task 194b R4: keyed under the column that files the message there (sprk_communicationthread), through the one
            // builder — the question RecordCreationService's Repair asks, ranked after the message's own lookups.
            if (!IsGrant(row.LogicalName)
                && RecordThreadFiling.ThreadOf(row) is { } threadId
                && (_byRef.TryGetValue((RecordThreadFiling.ThreadTable, threadId), out var thread)
                    || _threadsOutsidePass.TryGetValue(threadId, out thread)))
            {
                parents = RecordThreadFiling.QuestionWithThreadFiling(row, RecordThreadFiling.ParentsOf(thread)).Parents.ToList();
            }

            return new RecordOwnershipContext
            {
                Parents = parents.Distinct().ToArray(),
                // UAC task 194b V-fix 3: the row's Secure-only parents (a copy's canonical) join secure-if-any only, as the writer's
                // question holds them (RecordOwnershipContext.ForRow) — never the primary, so an unfiled copy keeps its creator.
                SecureOnlyParents = IsGrant(row.LogicalName)
                    ? Array.Empty<RecordOwnershipParent>()
                    : QuestionFor(row).Context.SecureOnlyParents,
                // R4b-2: a hard-deny row's ordinary answer is the restricted-content owner team (RecordOwnershipResolver).
                ChildEntityLogicalName = row.LogicalName,
                WhenUnfiled = UnfiledOwnership.KeepCreator,
                UnsecuringRoot = _unsecuring ? _root : null,
                PlannedOwningTeams = _planned.Count > 0 ? new Dictionary<RecordOwnershipParent, Guid>(_planned) : null,
                LookupCache = _lookupCache,
            };
        }

        /// <summary>
        /// UAC task 194b: every row's ownership question (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>), built once
        /// from the row as this pass read it. A read that faults leaves that row undetermined (it is reported failed, never
        /// decided on a guess); the rest of the pass goes on.
        /// </summary>
        private async Task LoadOwnershipQuestionsAsync(IEnumerable<Entity> rows)
        {
            foreach (var row in rows)
            {
                var key = (row.LogicalName, row.Id);
                if (_questions.ContainsKey(key))
                    continue;
                try
                {
                    _questions[key] = await RecordOwnershipContext.ForExistingRowAsync(row, Reads, _ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !_ct.IsCancellationRequested)
                {
                    Log.LogWarning(ex, "[SECURE-CHILD-RECONCILE] {Table} {Id}: its ownership question could not be built (a read " +
                        "failed).", row.LogicalName, row.Id);
                    // UAC task 194b R3b-2: the one builder over the row's own lookups (ForChild → ForRow: its table's order).
                    _questions[key] = new ExistingRowOwnership(RecordOwnershipContext.ForChild(row))
                    {
                        Undetermined = "its core record could not be read",
                    };
                }
            }
        }

        /// <summary>The row's ownership question (<see cref="LoadOwnershipQuestionsAsync"/>).</summary>
        private ExistingRowOwnership QuestionFor(Entity row) =>
            _questions.TryGetValue((row.LogicalName, row.Id), out var question)
                ? question
                : throw new InvalidOperationException(
                    $"No ownership question was built for {row.LogicalName} {row.Id:D} before it was decided (194b).");

        /// <summary>
        /// A grant row's root, read from its own lookup to the pass's root table (the lookup <see cref="LoadGrantRowsAsync"/>
        /// selects it by); none when it names none, so the row keeps its owner.
        /// </summary>
        private List<RecordOwnershipParent> GrantRootParent(Entity row) =>
            row.GetAttributeValue<EntityReference>(_root.EntityLogicalName) is { Id: var rootId } && rootId != Guid.Empty
                ? [new RecordOwnershipParent(_root.EntityLogicalName, rootId)]
                : [];

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
                        LookupCache = _lookupCache,
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
    }
}
