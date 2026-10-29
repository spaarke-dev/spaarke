using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 148 (C10 part 2, backfill) — sweeps every record flagged <c>sprk_issecure = true</c>
/// (projects, matters, work assignments) through <see cref="SecureChildReconciler"/>: every existing child of every secure
/// record brought into the state task 146's rule gives it (and, with writes on, its sharees mirrored by task 149).
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Provisioning and unsecure reconcile the children of the ONE record they act on. The records
/// that were already secure before task 148 — and any child a pass left incomplete — are reached only by a sweep. Run once
/// with writes on, it is the one-time backfill (operator runbook: SECURE-PROJECT-ENVIRONMENT-SETUP.md §7c.1, script
/// <c>scripts/Invoke-SecureChildBackfill.ps1</c>); task 147 schedules it as the standing L4 safety net.</para>
/// <para><b>Two parts, two switches (task 147, round 28 item 2).</b> The job is ENABLED every 2 minutes. Its
/// <b>recent-changes pass</b> (below) is the L4 net for writes made outside the product and WRITES unless
/// <see cref="RecentChangesWritesEnabledConfigKey"/> parses to <c>false</c> (an emergency stop). Its <b>sweep</b> over every
/// secure record — the task 148 backfill — keeps the <c>ExternalAccessReconciliationJob</c> posture: writes need
/// <see cref="WritesEnabledConfigKey"/> = <c>true</c>; an absent, empty or unparseable value writes nothing. A scheduled
/// tick runs the sweep window only while the sweep writes; a manual admin trigger (the backfill script) always runs it, in
/// its own mode. In report-only mode every change a part WOULD make is logged per row with the row's current owner, and
/// summarized in the run's <c>ResultJson</c>.</para>
/// <para><b>Ordered, capped, resumable.</b> Roots are taken in a fixed order (table, then id), at most
/// <see cref="MaxRootsPerRunConfigKey"/> per run (default <see cref="DefaultMaxRootsPerRun"/>). The next run continues
/// after the last root the previous one reached (a cursor in this singleton, per instance — the task 143 job's precedent),
/// until a run reaches the end (<c>passComplete: true</c>) and the next starts again from the beginning. Each run's
/// <c>ResultJson</c> records the same position (<c>startPosition</c>, <c>resumeAfter</c>), and every root is a progress line
/// in the log before it is processed, so an interrupted run's position is visible — and a reader can tell a pass that
/// covered the whole list (its first run began at position 1) from one that covered only a tail. The cursor and the run
/// history are per App Service instance, so the backfill runbook runs on ONE instance. Because every step of a pass is keyed on observed state,
/// re-processing a root is harmless: a restart only starts the sweep over.</para>
/// <para><b>Recent changes first (task 147).</b> Before the sweep window, each run lists the child rows of every lineage
/// table modified since its watermark that are not already owned by the Secure team. It finds the secure records those rows
/// sit under (<see cref="SecureChildShareSynchronizer.SecureRootsAboveAsync"/>, the resolver's own upward walk) and
/// reconciles those records first, with the same pass and the same Sweep trigger, so a run never releases anything. A child
/// written outside the product (out-of-the-box form, quick create, grid edit, import, flow) or by a client writer still on
/// <c>Xrm.WebApi</c> is therefore corrected within one run. It does not wait for the capped sweep window to reach its
/// record. A record made secure by a direct write (Web API, script) changes no child row, so the pass also reconciles
/// every secure record whose OWN <c>modifiedon</c> is in the same window, read by the sweep's listing of the secure records
/// (#1943). One with no <c>modifiedon</c> counts as changed. These are counted in <c>recentChanges.rootsChanged</c>, and
/// one left incomplete is carried like any other. At most <see cref="MaxRootsPerRunConfigKey"/> of them a run, oldest
/// change first; the rest re-open the catch-up (below) and are counted in <c>recentChanges.rootsChangedOverflow</c>
/// (#1943). The watermark is the run's start time, taken before the secure records are listed, minus
/// <see cref="RecentChangesOverlap"/>. It lives in this singleton, per instance, like the cursor. It moves only past a
/// listing that completed. A changed row the walk cannot place (under a record flagged secure but not isolated, or under a
/// missing ancestor) is listed in <c>recentChanges.undetermined</c>, and the run is not a success.</para>
/// <para><b>Standing schedule (task 147, round 28 item 2, decided 2026-10-04).</b> Every 2 minutes, writes on for the
/// recent-changes pass, in every environment where task 148 is deployed: the interval is the window in which a child
/// written OUTSIDE the product under a secure record sits in its creator's business unit. In-product creates no longer
/// open that window: the product's writers create through the BFF (task 147, G5), and the model-driven native creates
/// under a secure parent are replaced by BFF-backed commands. Each correction is listed in <c>ResultJson.changes</c> with
/// <c>pass: "recent"</c> and its previous owner, and counted in <c>recentChanges.corrected</c> — the standing correction
/// report. A record whose recent pass came back incomplete, and a changed row the walk could not place, are carried to the
/// next run and looked at again in every run until finished (task 147 r1). That carried state, the watermark and the
/// cursor live in this singleton, so a start (a restart, a deployment, a scale-out) loses them: a new instance therefore
/// walks every secure record ONCE on its scheduled ticks — the CATCH-UP, the same reconcile, <c>MaxRootsPerRun</c> records
/// a run — before its watermark alone decides (task 147 r1c). Nothing an earlier instance carried is lost with it.</para>
/// <para><b>The Make Secure file backstop (round 46 item 2, wired at the batch-4 integration).</b> After the reconcile
/// passes, each run also settles the PENDING Make Secure relocations through the ONE <see cref="DocumentContainerRelocator"/>
/// (purpose MakeSecure; the target is each document's own derived container): every document whose relocation ledger
/// (<c>sprk_relocationpending</c>) still owes a step, and every ISOLATED document (owned by the Secure Record owner team)
/// whose file still sits in a business unit's shared container — the move a <c>files_incomplete</c> left unmade, which no
/// ledger entry records because nothing was re-pointed. Both are read from state, so nothing is lost with this instance.
/// At most <see cref="MaxRelocationsPerRunConfigKey"/> documents a run (default <see cref="DefaultMaxRelocationsPerRun"/>),
/// in id order after a per-instance cursor so a row that cannot be finished never starves the others. Its writes follow the
/// recent-changes pass (on unless that pass's emergency stop is set). Reported in <c>ResultJson.makeSecureRelocations</c>;
/// a document still owing a step makes the run unsuccessful. A row that names no file it can resolve is reported
/// (<c>unresolvable</c>) and never moved or deleted. Neither listing moves an isolated document whose derivation is not a
/// secure record's own container (its filing edited outside the BFF): the relocator's widening guard holds it for every
/// purpose (#2159; <c>heldIsolated</c>, counted apart and not incomplete (B7): only an F3 act or a re-file under a secure
/// record releases it), while any ledger entry it owes is still settled. A checked-out document is counted apart too
/// (<c>checkedOut</c>, B4). Only a document the backstop settled or held is skipped by the re-file pass (B1).</para>
/// <para><b>The re-file pass (#2159, #2151).</b> Last in the run, every <c>sprk_document</c> modified since this pass's own
/// watermark — any owner, so every writer, the BFF and outside it — or filed under a matter, project or work assignment
/// modified since then, whose pointer's drive is not among its derived containers is relocated through the same relocator
/// with purpose <c>Refile</c>: only the row's BFF-bound item, at most <see cref="MaxRelocationsPerRunConfigKey"/> a run (its
/// own budget), at most <see cref="RefileExaminationBudgetConfigKey"/> examined and for at most
/// <see cref="RefileTimeSlice"/>, in id order after a per-instance cursor that holds the watermark until the window drains;
/// failed and undecidable rows carried (an undecidable one fails the run). A checked-out document is listed apart
/// (<c>checkedOut</c>) and left. A re-file never widens: a row still owned by the Secure Record owner team keeps its file in the
/// secure container while its filing points elsewhere (<c>heldIsolated</c>, released only by an F3 act); an unfiled
/// document stays in a declared home container (#2151), and one whose file is in no business unit's container and no
/// declared home is reported (<c>unfiledHomeUnknown</c>) and left. A document the backstop settled or held this run is not
/// relocated twice. Reported in <c>ResultJson.refileRelocations</c>; a failed or still-owing move makes the run
/// unsuccessful. Until the pass reaches it, a re-filed document is refused under the strict rule (about two minutes plus
/// the run). Known limit: its window is per instance and the catch-up does not walk documents, so a move a previous
/// instance carried is reached again only when the row next changes or by <c>DocumentContainerMigrationJob</c> (the repair;
/// workaround: any write to the row puts it back in the window).</para>
/// <para><b>The inherited Access Permission (task 173, owner rounds 81/84).</b> Each run also keeps <c>sprk_accesspermission</c>
/// on To Do, Event, Communication and Document equal to the most restrictive value of the records at the top of their
/// filing (<see cref="ChildAccessPermissionReconciler"/>): the rows changed since its own watermark and everything filed
/// under them, the rows it carried, and a capped sweep window until this instance has covered every row once. A parentless
/// row is never written. Its listing, watermark and carried rows are its own, so its faults never hold the secure pass's
/// window (#1378). Reported in <c>ResultJson.accessPermission</c>; a listing fault or a failed write makes the run
/// unsuccessful.</para>
/// <para><b>Ordinary parent moves (UAC task 194, #2339; rule I-6).</b> After the secure work and before the file passes,
/// each run lists the ORDINARY projects, matters and work assignments modified since this pass's own watermark (an MDA Assign
/// changes the record's <c>modifiedon</c>) and moves every child a business unit's default team owns to the owner the rule
/// gives it now (<see cref="SecureChildReconciler.ReconcileOrdinaryParentMoveAsync"/>). It never touches a row owned in the
/// Secure Record business unit and never moves a row into or out of isolation; user-owned rows are reported, not moved. A
/// re-owned document's file follows through the re-file pass below in the same run. Report-only unless
/// <see cref="OrdinaryMovesWritesEnabledConfigKey"/> is <c>true</c>; capped by <see cref="MaxRootsPerRunConfigKey"/> and
/// <see cref="MaxOrdinaryMovesPerRunConfigKey"/>. Service requests are ordinary roots too (F4-b), and a per-instance catch-up
/// sweep walks every ordinary root once in each mode (F4-a), so a move made before this pass existed is corrected too.
/// Reported in <c>ResultJson.ordinaryMoves</c>.</para>
/// <para><b>ADR-036 A1.</b> Rule 3: the unit of work is one root's pass, idempotent and read back, so no claim marker is
/// needed. Rule 4: only a run that could not LIST the secure records throws (nothing was decided; a retry this tick can do
/// the work); a run in which some roots are incomplete returns <c>Success = false</c> — the next run revisits them. Rule 5:
/// one heartbeat per attempt. Rule 6: registered through <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — BFF identity, the same
/// reconciler the provisioning and unsecure endpoints call (L1 and L4 run ONE invariant owner), low volume (the secure
/// records of one environment). No package, store, column or interface.</para>
/// </remarks>
public sealed class SecureChildReconciliationJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "secure-child-reconciliation";

    /// <summary>
    /// Every 2 minutes, ENABLED (task 147, round 28 item 2: the recent-changes pass is the L4 net for writes made outside the
    /// product, "every 2 minutes with writes ON in every environment where 148 is deployed" — the same window owner round 11
    /// item 2 accepted for 149's shares).
    /// </summary>
    internal const string DefaultCronSchedule = "*/2 * * * *";

    /// <summary>
    /// The owner switch that turns the SWEEP (the backfill over every secure record) from report-only into writes. Absent,
    /// empty or unparseable = report-only.
    /// </summary>
    internal const string WritesEnabledConfigKey = "SecureChild:Reconciliation:WritesEnabled";

    /// <summary>
    /// Task 147 (round 28 item 2): the recent-changes pass WRITES unless this parses to <c>false</c> — an emergency stop,
    /// not an opt-in. Absent, empty or unparseable = writes on.
    /// </summary>
    internal const string RecentChangesWritesEnabledConfigKey = "SecureChild:Reconciliation:RecentChangesWritesEnabled";

    /// <summary>The per-run root cap.</summary>
    internal const string MaxRootsPerRunConfigKey = "SecureChild:Reconciliation:MaxRootsPerRun";

    internal const int DefaultMaxRootsPerRun = 50;

    /// <summary>The per-run cap on the Make Secure file backstop (round 46 item 2).</summary>
    internal const string MaxRelocationsPerRunConfigKey = "SecureChild:Reconciliation:MaxRelocationsPerRun";

    internal const int DefaultMaxRelocationsPerRun = 25;

    /// <summary>
    /// UAC task 194 (#2339, owner round 128 item 3): the switch that turns the ORDINARY-PARENT-MOVE pass from report-only into
    /// writes. Absent, empty or unparseable = report-only (it is an opt-in, set only after UAC reads a report-only run and the
    /// owner gives the go). The recent-changes emergency stop (<see cref="RecentChangesWritesEnabledConfigKey"/> = false)
    /// stops it too.
    /// </summary>
    internal const string OrdinaryMovesWritesEnabledConfigKey = "SecureChild:Reconciliation:OrdinaryMovesWritesEnabled";

    /// <summary>UAC task 194: the most owner writes the ordinary-parent-move pass makes a run.</summary>
    internal const string MaxOrdinaryMovesPerRunConfigKey = "SecureChild:Reconciliation:MaxOrdinaryMovesPerRun";

    internal const int DefaultMaxOrdinaryMovesPerRun = 200;

    /// <summary>
    /// #2159 B2: the most documents the re-file pass EXAMINES (derives) a run — in place or not. The rest of its window waits
    /// behind a cursor, and the watermark is held until the window drains, so a window of N documents drains in
    /// ceil(N / budget) runs.
    /// </summary>
    internal const string RefileExaminationBudgetConfigKey = "SecureChild:Reconciliation:RefileExaminationBudget";

    internal const int DefaultRefileExaminationBudget = 500;

    /// <summary>#2159 B2: the re-file pass stops examining this long after it started (the rest waits behind the cursor).</summary>
    internal static readonly TimeSpan RefileTimeSlice = TimeSpan.FromSeconds(60);

    /// <summary>
    /// UAC task 194b: the ordinary-parent-move pass stops walking roots this long after it started; a root not reached is carried
    /// (recent) or holds the sweep cursor (swept), exactly as a root the write cap stops, and is walked first next run.
    /// </summary>
    internal static readonly TimeSpan OrdinaryTimeSlice = TimeSpan.FromSeconds(60);

    /// <summary>#2159 B6: an undecidable document is carried (and fails the run) for at most this many consecutive runs.</summary>
    internal const int MaxUndecidableRuns = 5;

    /// <summary>
    /// #2159 B5: a document whose relocation ledger is unreadable for this many consecutive runs of the backstop becomes
    /// TERMINAL (<see cref="DocumentContainerRelocator.TerminalLedgerUnreadable"/>): reported once, never failing a later run.
    /// A per-instance count and a constant, like <see cref="MaxUndecidableRuns"/>: a restart only adds retries.
    /// </summary>
    internal const int MaxUnreadableLedgerRuns = 5;

    /// <summary>
    /// Task 173: the per-run cap on the inherited Access Permission SWEEP window (the rows of To Do, Event, Communication and
    /// Document read in order while this instance's sweep has not covered them all).
    /// </summary>
    internal const string MaxAccessPermissionRowsPerRunConfigKey = "SecureChild:Reconciliation:MaxAccessPermissionRowsPerRun";

    internal const int DefaultMaxAccessPermissionRowsPerRun = 1000;

    /// <summary>
    /// Task 173 (verifier F3): the most <c>sprk_accesspermission</c> writes one run makes. A matter with thousands of children
    /// turning Restricted would otherwise write all of them, one at a time, in one run — holding the job and spending the
    /// BFF's Dataverse service-protection budget. Past the cap the pass stops writing and keeps its window (the watermark and
    /// the sweep cursor stay), so the next run lists the same rows again and writes the next ones; only rows whose value
    /// differs are written, so it converges.
    /// </summary>
    internal const string MaxAccessPermissionWritesPerRunConfigKey = "SecureChild:Reconciliation:MaxAccessPermissionWritesPerRun";

    internal const int DefaultMaxAccessPermissionWritesPerRun = 500;

    /// <summary>
    /// Planned / applied row changes listed in <c>ResultJson</c>; <c>changesTotal</c> counts all of them, and the complete
    /// list is the per-row log lines (<c>plan:</c> report-only, <c>reassign:</c> with writes).
    /// </summary>
    internal const int MaxSampledChanges = 200;

    internal const string ModeReportOnly = "report-only";
    internal const string ModeWrite = "write";

    /// <summary>
    /// Task 147: how far back the FIRST run on an instance looks for recently changed children, before it has a watermark.
    /// A child changed before that is still reached by the full sweep.
    /// </summary>
    internal static readonly TimeSpan InitialRecentChangesLookback = TimeSpan.FromMinutes(60);

    /// <summary>
    /// Task 147: each run's watermark is its own start time minus this overlap. The overlap absorbs clock skew between this
    /// instance and Dataverse's <c>modifiedon</c>, and a write that committed while the previous run was listing. Seeing a
    /// row twice is harmless, because every pass is keyed on observed state.
    /// </summary>
    internal static readonly TimeSpan RecentChangesOverlap = TimeSpan.FromMinutes(1);

    /// <summary>The Dataverse column the recent-changes pass filters on.</summary>
    private const string ModifiedOnColumn = "modifiedon";

    private const int PageSize = 5000;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // Everything beyond the framework is resolved per run from a scope (the run history included), as the sibling jobs do:
    // constructing the job — which the scheduler's registry does at startup — then needs nothing a module test lacks.
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecureChildReconciliationJob> _logger;

    public SecureChildReconciliationJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<SecureChildReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Secure Child Reconciliation (backfill)";

    /// <inheritdoc />
    public string Description =>
        "Brings every existing child of every secure project, matter and work assignment into the state the ownership rule " +
        "gives it: owned by the Secure Record owner team, shared with exactly its record's sharees (task 148). Records " +
        "whose related records changed since the last run are reconciled first (task 147), with the records that " +
        "themselves changed (#1943). Report-only unless " +
        "SecureChild:Reconciliation:WritesEnabled is true. Also keeps the Access Permission of To Do, Event, " +
        "Communication and Document equal to their parents' (task 173). The children of an ordinary record reassigned to " +
        "another owner follow it (task 194; report-only unless SecureChild:Reconciliation:OrdinaryMovesWritesEnabled is true).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        // The SWEEP (task 148: every secure record in order, the backfill) writes only when the owner switch says so.
        var writes = bool.TryParse(_configuration[WritesEnabledConfigKey], out var enabled) && enabled;
        var mode = writes ? SecureChildReconcileMode.Apply : SecureChildReconcileMode.ReportOnly;

        // The RECENT-CHANGES pass (task 147, decided by round 28 item 2) writes in every environment this job runs in:
        // only an explicit "false" turns it off. Its writes are bounded by the rule — the Sweep trigger never releases an
        // isolated row (owner round 24 item 2) — so they only ever move a child INTO isolation under a secure record.
        var recentWrites = !(bool.TryParse(_configuration[RecentChangesWritesEnabledConfigKey], out var recentEnabled)
                             && !recentEnabled);
        var recentMode = recentWrites ? SecureChildReconcileMode.Apply : SecureChildReconcileMode.ReportOnly;

        // A SCHEDULED tick runs the sweep window only when the sweep writes. With the sweep off, a scheduled tick is the L4
        // net alone (recent changes + carried work), so the backfill's report-only sweep is not re-planned every two minutes
        // and the backfill script's per-instance cursor is not moved by the schedule. A manual trigger (the backfill
        // script, an operator) always runs the sweep window, in the sweep's own mode.
        var runSweep = writes || context.Trigger != JobRunTrigger.Scheduled;
        var cap = int.TryParse(_configuration[MaxRootsPerRunConfigKey], out var configured) && configured > 0
            ? configured
            : DefaultMaxRootsPerRun;

        using var scope = _scopeFactory.CreateScope();
        var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
        var reconciler = scope.ServiceProvider.GetRequiredService<SecureChildReconciler>();
        var synchronizer = scope.ServiceProvider.GetRequiredService<SecureChildShareSynchronizer>();

        // #1943 (review F1): the run's start time is taken BEFORE the secure records are listed. The watermark derives from
        // it, so a record made secure while a slow listing ran (paging, throttling) is still inside the next run's window.
        var startedAt = _timeProvider.GetUtcNow();

        // The secure records, in a fixed order. A listing that cannot complete decides nothing: ADR-036 A1 rule 4.
        var listing = await ListSecureRootsAsync(dataverse, cancellationToken).ConfigureAwait(false);
        var roots = listing.Roots;

        // Task 147, the RECENT-CHANGES pass: the secure records whose children changed since the last run (the watermark),
        // and those that themselves changed (#1943). This catches children written outside the product (out-of-the-box
        // forms, quick create, grid edits, imports, flows) and by the client writers still on Xrm.WebApi within one run,
        // rather than only when the capped sweep window next reaches their record. They are reconciled first, by the same
        // pass.
        DateTimeOffset since;
        IReadOnlyList<(string Table, Guid Id)> retryRoots;
        IReadOnlyList<(string Table, Guid Id)> retryRows;
        lock (_cursorGate)
        {
            since = _recentChangesWatermark ?? startedAt - InitialRecentChangesLookback;
            // Task 147 r1 (verifier item 4): what the previous runs could not finish is looked at again in EVERY run until
            // it is finished — never reported once and then left to the capped sweep (or, for a row the walk could not
            // place, never revisited at all).
            retryRoots = _pendingRoots.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
            retryRows = _pendingRows.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        }

        var recent = await FindRecentlyChangedRootsAsync(dataverse, synchronizer, since, retryRows, cancellationToken)
            .ConfigureAwait(false);

        // Continue after the last record the previous run reached (by ORDER, not position: records secured or unsecured
        // in between never make the window skip one). Past the end — the previous run finished the pass — start over.
        (string Table, Guid Id)? resumeAfter;
        lock (_cursorGate)
            resumeAfter = _cursor;
        var start = resumeAfter is { } after ? roots.FindIndex(r => Compare((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var batch = runSweep ? roots.Skip(start).Take(cap).ToList() : new List<(string Table, Guid Id, string Key)>();

        // Task 147 r1c — the CATCH-UP: what an instance carries (the watermark, the unfinished records, the unplaced rows)
        // lives in this singleton, so a restart, a deployment or a scale-out starts an instance that knows none of it. For
        // that instance "everything since the last run" is unknown, so instead of listing every changed row ever, it walks
        // every secure record ONCE — the same recent-changes reconcile (its mode, the Sweep trigger that never releases),
        // `cap` records a run, from the first — and only then trusts its watermark alone. A record whose reconcile an
        // earlier instance left unfinished is therefore reconciled again within ceil(records / cap) runs of any restart,
        // never lost with the memory that carried it. While the SWEEP itself writes, its window covers every record in
        // order, so the catch-up stands aside. It advances only in a run whose recent-changes pass writes. It is part of
        // the SCHEDULED net only: a manual trigger is the backfill script's review run (§7c.1), whose report-only plan must
        // not be applied by the catch-up while an operator reads it. Deployment order (G147-2): the task 148 backfill is
        // applied and verified in an environment BEFORE a task 147 BFF reaches it, so a catch-up only ever finds drift.
        bool catchUpPending;
        (string Table, Guid Id)? catchUpAfter;
        lock (_cursorGate)
            (catchUpPending, catchUpAfter) = (!_catchUpComplete, _catchUpCursor);
        var catchUpRuns = catchUpPending && context.Trigger != JobRunTrigger.ManualAdmin && !(runSweep && writes);
        var catchUpStart = catchUpAfter is { } cAfter ? roots.FindIndex(r => Compare((r.Table, r.Id), cAfter) > 0) : 0;
        if (catchUpStart < 0)
            catchUpStart = roots.Count;
        var catchUpBatch = catchUpRuns
            ? roots.Skip(catchUpStart).Take(cap).ToList()
            : new List<(string Table, Guid Id, string Key)>();

        // The recent-changes roots go first, with every record a previous run left incomplete (task 147 r1), then the
        // catch-up window (r1c), then the sweep window. A record in more than one list is reconciled once, and it still
        // counts toward the sweep window's position. A carried record that is no longer flagged secure is not carried on:
        // the sweep list is the authority on which records are.
        var secureKeys = roots.Select(r => (r.Table, r.Id)).ToHashSet();
        var retriedRoots = retryRoots.Where(secureKeys.Contains).ToList();
        // Task 147 r1c-v1: a carried record unsecured since (its F3 holder's /unsecure-project, or its flag cleared) is
        // dropped — never reconciled again by this net, whose Sweep trigger only moves rows INTO isolation — and named in
        // the report, so a record never leaves the carried set silently.
        var droppedRoots = retryRoots.Where(r => !secureKeys.Contains(r)).ToList();
        foreach (var (droppedTable, droppedId) in droppedRoots)
        {
            _logger.LogInformation(
                "[SECURE-CHILD-RECONCILE] carried run={RunId} root={Table}:{Id}: no longer flagged secure, so it is no longer " +
                "carried.", context.RunId, droppedTable, droppedId);
        }

        // #1943: a record made secure by a direct write (Web API, script) changes no child row, so the listing above cannot
        // find it. Every secure record whose OWN modifiedon is in the same window (one with none counts as changed) is
        // reconciled with them, in the same mode and by the same Sweep trigger, which only moves a child INTO isolation.
        // Left incomplete, it is carried like any other record of this group.
        var rootsChanged = listing.ChangedSince(since);

        // #1943 (review F1): at most `cap` of them a run, oldest change first (a burst, or a watermark held back, must not
        // make one run unbounded); one already found through a changed child or carried costs nothing. The rest re-open
        // the catch-up, as an instance start does (saved below): it walks every secure record, `cap` a run, so the ones
        // left out are reconciled although the watermark moves on.
        var alreadyFirst = recent.Roots.Concat(retriedRoots).ToHashSet();
        var rootsChangedTaken = rootsChanged.Where(alreadyFirst.Contains)
            .Concat(rootsChanged.Where(r => !alreadyFirst.Contains(r)).Take(cap))
            .ToList();
        var rootsChangedOverflow = rootsChanged.Count - rootsChangedTaken.Count;
        if (rootsChangedOverflow > 0)
        {
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] recent-changes run={RunId}: {Overflow} of {RootsChanged} secure records changed " +
                "since {Since:o} are past the per-run cap of {Cap}; the catch-up is re-opened, so it reconciles them in " +
                "later runs.",
                context.RunId, rootsChangedOverflow, rootsChanged.Count, since, cap);
        }

        var firstRoots = recent.Roots
            .Concat(rootsChangedTaken)
            .Concat(retriedRoots)
            .Distinct()
            .ToList();
        var rootsChangedKeys = rootsChangedTaken.ToHashSet();
        var recentKeys = firstRoots.ToHashSet();
        var catchUpKeys = catchUpBatch.Select(c => (c.Table, c.Id)).Where(k => !recentKeys.Contains(k)).ToHashSet();
        var work = firstRoots
            .Select(r => (r.Table, r.Id, Key: $"{r.Table}:{r.Id:D}", Group: WorkGroup.Recent, Position: (int?)null))
            .Concat(catchUpBatch.Where(c => catchUpKeys.Contains((c.Table, c.Id)))
                .Select(c => (c.Table, c.Id, c.Key, Group: WorkGroup.CatchUp, Position: (int?)null)))
            .Concat(batch.Select((b, i) => (b.Table, b.Id, b.Key, Group: WorkGroup.Sweep, Position: (int?)(start + i + 1)))
                .Where(b => !recentKeys.Contains((b.Table, b.Id)) && !catchUpKeys.Contains((b.Table, b.Id))))
            .ToList();

        var totals = new int[8];
        var incompleteRoots = new List<string>();
        var stillIncomplete = new List<(string Table, Guid Id)>();
        var recentCorrections = 0;
        var sampled = new List<object>();
        var changesTotal = 0;
        var sharesWritten = 0;
        var mirrorsRevoked = 0;

        foreach (var (table, id, key, group, position) in work)
        {
            if (group == WorkGroup.Sweep)
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] progress run={RunId} mode={Mode} root={Root} position={Position}/{Total} " +
                    "(of {All} secure records).",
                    context.RunId, mode, key, position, start + batch.Count, roots.Count);
            }
            else if (group == WorkGroup.CatchUp)
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] catch-up run={RunId} mode={Mode} root={Root}: this instance has not yet walked " +
                    "every secure record since it started.",
                    context.RunId, recentMode, key);
            }
            else if (rootsChangedKeys.Contains((table, id)))
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] recent-changes run={RunId} mode={Mode} root={Root}: the record itself changed " +
                    "since {Since:o}.",
                    context.RunId, recentMode, key, since);
            }
            else
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] recent-changes run={RunId} mode={Mode} root={Root}: a related record changed " +
                    "since {Since:o}.",
                    context.RunId, recentMode, key, since);
            }

            var report = await reconciler.ReconcileAsync(
                    table, id, group == WorkGroup.Sweep ? mode : recentMode, SecureChildPassTrigger.Sweep, cancellationToken)
                .ConfigureAwait(false);

            foreach (var t in report.Tables)
            {
                totals[0] += t.Examined; totals[1] += t.AlreadyCorrect; totals[2] += t.Changed; totals[3] += t.WouldChange;
                totals[4] += t.Untouched; totals[5] += t.Refused; totals[6] += t.Failed; totals[7] += t.NeedsF3;
            }

            if (report.Shares is { } s)
                sharesWritten += s.SharesGranted + s.SharesChanged + s.SharesRevoked;
            mirrorsRevoked += report.MirrorSharesRevoked;

            if (!report.IsComplete)
            {
                incompleteRoots.Add($"{key}: {report.Status}{(report.Detail is null ? "" : " — " + report.Detail)}");

                // Carried from the recent-changes and catch-up groups (the L4 net): a sweep-window record that is
                // incomplete is the sweep's to revisit, in the sweep's own mode (a carried record is reconciled in the
                // recent-changes mode).
                if (group != WorkGroup.Sweep)
                    stillIncomplete.Add((table, id));
            }

            foreach (var change in report.Changes)
            {
                // Counted in full (task 148 r2): the listed sample is capped, the total is not — so a reader can tell
                // when the list is incomplete (changesTotal > changesListed).
                changesTotal++;
                if (group != WorkGroup.Sweep && change.Outcome == SecureChildRowOutcome.Changed)
                    recentCorrections++;
                if (sampled.Count >= MaxSampledChanges)
                    continue;
                sampled.Add(new
                {
                    // Task 147: which part of the run made it — "recent" or "catch-up" (the L4 net's standing correction
                    // report) or "sweep" (the backfill window).
                    pass = group switch { WorkGroup.Recent => "recent", WorkGroup.CatchUp => "catch-up", _ => "sweep" },
                    root = key,
                    table = change.Table,
                    id = change.Id,
                    previousOwner = change.PreviousOwner is { } p ? $"{p.Kind.ToEntitySet()}({p.Id:D})" : null,
                    targetTeam = change.TargetTeamId,
                    outcome = change.Outcome.ToString(),
                    detail = change.Detail,
                });
            }
        }

        // UAC task 194 (#2339): the ORDINARY-PARENT-MOVE pass — the children of an ordinary record reassigned to another owner
        // follow it (I-6). After the secure work (it never touches an isolated row) and BEFORE the re-file pass, so a document
        // it re-owns has its file moved by that pass in the same run. Its own switch (report-only by default), window and carry.
        var ordinary = await ReconcileOrdinaryParentMovesAsync(
            reconciler, dataverse, recentWrites, cap, startedAt, context.RunId, cancellationToken).ConfigureAwait(false);

        // Round 46 item 2: the Make Secure file backstop, after the children (each document's container derives from its
        // now-secure parents). Its writes follow the recent-changes pass.
        var relocations =await SettleMakeSecureRelocationsAsync(scope.ServiceProvider, dataverse, recentWrites, cancellationToken)
            .ConfigureAwait(false);


        var lastKey = batch.Count > 0 ? batch[^1].Key : null;
        var passComplete = runSweep && start + batch.Count >= roots.Count;
        lock (_cursorGate)
        {
            if (runSweep)
                _cursor = passComplete || batch.Count == 0 ? null : (batch[^1].Table, batch[^1].Id);

            // The watermark moves only past a listing that COMPLETED, in a run that WROTE. After a failed listing, the
            // next run looks at the same window again. A report-only run corrects nothing, so it must not move the
            // watermark either: the first run with writes on then still sees the changes the report-only runs listed
            // (back to the initial lookback). A record whose pass was incomplete is carried (below).
            if (recent.Failure is null && recentWrites && !recent.PendingOverflow)
                _recentChangesWatermark = startedAt - RecentChangesOverlap;

            // Task 147 r1c: the catch-up advances only in a run that wrote (a report-only run corrected nothing). Past the
            // last secure record this instance has walked them all once; from then on the watermark alone decides.
            if (catchUpRuns && recentWrites)
            {
                var reached = catchUpStart + catchUpBatch.Count;
                if (reached >= roots.Count)
                {
                    _catchUpComplete = true;
                    _catchUpCursor = null;
                }
                else
                {
                    _catchUpCursor = (catchUpBatch[^1].Table, catchUpBatch[^1].Id);
                }
            }

            // #1943 (review F1): changed records past the cap re-open the catch-up the way an instance start does — from the
            // first secure record — so every one left out of this run is reconciled within ceil(records / cap) runs.
            if (rootsChangedOverflow > 0)
            {
                _catchUpComplete = false;
                _catchUpCursor = null;
            }

            // Task 147 r1 (verifier item 4): carry forward, to be looked at first in the NEXT run, every record whose pass
            // came back incomplete in THIS run (a refused or failed re-own, a record that cannot be decided) and every
            // changed row the walk could not place. Each is reported again in every run until it is finished, so a refused
            // re-own is retried within one interval rather than after ceil(records / cap) runs, and a row under a missing
            // ancestor is never forgotten. A carried row that has since been isolated, deleted or placed drops out on its
            // own. After a failed listing nothing was decided about the carried rows, so they stay carried.
            _pendingRoots.Clear();
            _pendingRoots.UnionWith(stillIncomplete);
            if (recent.Failure is null)
            {
                _pendingRows.Clear();
                _pendingRows.UnionWith(recent.UndeterminedRows.Take(MaxCarriedRows));
            }
        }

        // Task 173 (owner rounds 81/84): the inherited Access Permission of To Do, Event, Communication and Document. Its own
        // listing, watermark and carried rows, so a fault there never holds the secure pass's window (#1378). It runs AFTER
        // the secure pass has saved its cursor and watermark (verifier K2): a long or cancelled access pass can never make
        // the secure pass redo its work. Its writes follow the recent-changes pass (on unless that pass's emergency stop is
        // set) and are capped per run (MaxAccessPermissionWritesPerRunConfigKey).
        var accessPermission = await ReconcileChildAccessPermissionAsync(
            scope.ServiceProvider, dataverse, recentWrites, startedAt, context.RunId, cancellationToken).ConfigureAwait(false);

        // #2159: the RE-FILE pass — a document whose filing changed (through the BFF or outside it) has its file moved into
        // its new derived container, never out of isolation and never from an unknown home. After the backstop (a document
        // it settled or held is not relocated twice) and LAST (B2): it is the pass with the time slice, so a long or
        // cancelled re-file pass never makes the secure or the access pass redo its work (K2). Its writes follow the
        // recent-changes pass.
        var refile = await RelocateRefiledDocumentsAsync(
            scope.ServiceProvider, dataverse, recentWrites, startedAt, relocations.Taken, context.RunId, cancellationToken)
            .ConfigureAwait(false);

        var duration = _timeProvider.GetElapsedTime(started);

        // A recently changed row the pass could not place is reported and makes the run unsuccessful, never silently
        // skipped (task 147, ADR-003). Examples are a row under a record flagged secure but not isolated, or under a
        // missing ancestor.
        var success = incompleteRoots.Count == 0 && recent.Failure is null && recent.Undetermined.Count == 0
                      && relocations.Failure is null && (relocations.Files?.Incomplete ?? 0) == 0
                      && !refile.Incomplete
                      && accessPermission.Run?.Failure is null && (accessPermission.Run?.Failed ?? 0) == 0
                      // UAC task 194: only a WRITING ordinary pass fails the run (report-only is the owner's reading).
                      && !ordinary.FailsTheRun;
        int carriedRoots, carriedRows;
        bool catchUpComplete;
        (string Table, Guid Id)? catchUpResumeAfter;
        lock (_cursorGate)
        {
            (carriedRoots, carriedRows) = (_pendingRoots.Count, _pendingRows.Count);
            (catchUpComplete, catchUpResumeAfter) = (_catchUpComplete, _catchUpCursor);
        }

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            success ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-RECONCILE] heartbeat mode={Mode} roots={Roots}/{All} passComplete={PassComplete} " +
            "resumeAfter={ResumeAfter} examined={Examined} alreadyCorrect={AlreadyCorrect} changed={Changed} " +
            "wouldChange={WouldChange} untouched={Untouched} refused={Refused} failed={Failed} needsF3={NeedsF3} " +
            "sharesWritten={SharesWritten} mirrorsRevoked={MirrorsRevoked} incompleteRoots={IncompleteRoots} " +
            "recentSince={RecentSince:o} recentRowsChanged={RecentRows} recentRoots={RecentRoots} " +
            "recentRootsChanged={RecentRootsChanged} " +
            "recentUndetermined={RecentUndetermined} recentFailure={RecentFailure} attempt={Attempt} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            mode, batch.Count, roots.Count, passComplete, passComplete ? null : lastKey, totals[0], totals[1], totals[2],
            totals[3], totals[4], totals[5], totals[6], totals[7], sharesWritten, mirrorsRevoked, incompleteRoots.Count,
            since, recent.RowsChanged, recent.Roots.Count, rootsChanged.Count, recent.Undetermined.Count, recent.Failure,
            context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        var problems = incompleteRoots.Take(20)
            .Concat(recent.Failure is { } failure ? new[] { "recently changed related records: " + failure } : [])
            .Concat(recent.Undetermined.Take(20).Select(u => "recently changed, not placed: " + u))
            .ToList();

        return new JobRunResult(
            Success: success,
            ErrorMessage: success
                ? null
                : $"{incompleteRoots.Count} secure record(s) not fully reconciled, {recent.Undetermined.Count} recently " +
                  $"changed related record(s) not placed and {relocations.Files?.Incomplete ?? 0} Make Secure file " +
                  $"relocation(s) still owed{(relocations.Failure is null ? "" : $" ({relocations.Failure})")}" +
                  (refile.Incomplete
                      ? $", {refile.Failed.Count + refile.StillOwed.Count} re-filed document file move(s) not finished" +
                        (refile.Failure is null ? "" : $" ({refile.Failure})")
                      : "") +
                  (accessPermission.Run is { } ap && (ap.Failure is not null || ap.Failed > 0)
                      ? $", and the inherited Access Permission pass {(ap.Failure ?? $"could not write {ap.Failed} row(s)")}"
                      : "") +
                  (ordinary.FailsTheRun
                      ? $", {ordinary.IncompleteRoots.Count} reassigned ordinary record(s) whose children were not all moved" +
                        (ordinary.Failure is null ? "" : $" ({ordinary.Failure})")
                      : "") +
                  "; the next run revisits them: " + string.Join("; ", problems),
            ProcessedItems: work.Count,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                mode = writes ? ModeWrite : ModeReportOnly,
                // Task 147: false on a scheduled tick while the sweep is report-only — that tick was the L4 net alone.
                sweepRan = runSweep,
                rootsInRun = batch.Count,
                rootsTotal = roots.Count,
                // Where in the ordered list this run began (1 = the first secure record). A pass is covered only from a run
                // that began at 1 — the cursor is shared with every other trigger of this job on this instance, so a run
                // can begin mid-list; Invoke-SecureChildBackfill.ps1 -Verify refuses a pass that did not.
                startPosition = start + 1,
                passComplete,
                // The progress log: the next run continues after this root. Null when this run reached the end.
                resumeAfter = passComplete ? null : lastKey,
                examined = totals[0],
                alreadyCorrect = totals[1],
                changed = totals[2],
                wouldChange = totals[3],
                untouched = totals[4],
                refused = totals[5],
                failed = totals[6],
                // Isolated rows the rule would release, held for an F3 holder's act (owner round 24 item 2): only
                // /unsecure-project releases one. Listed in `changes` with outcome NeedsF3.
                needsF3 = totals[7],
                sharesWritten,
                mirrorsRevoked,
                incompleteRoots = incompleteRoots.Take(50).ToArray(),
                // Every row change of the run (planned, made, refused or failed) — `changes` lists the first
                // MaxSampledChanges of them; the complete list is the per-row "plan:" / "reassign:" log lines.
                changesTotal,
                changesListed = sampled.Count,
                changes = sampled,
                // Task 147: the recent-changes pass. These are the secure records whose related records changed since
                // `since`, or that themselves changed (#1943). They were reconciled before the sweep window, and their
                // changes are in the totals above.
                recentChanges = new
                {
                    mode = recentWrites ? ModeWrite : ModeReportOnly,
                    // The standing correction report (round 28 item 2): children moved into isolation by this run's
                    // recent-changes pass; each is listed in `changes` with pass "recent" and its previous owner.
                    corrected = recentCorrections,
                    since = since.UtcDateTime,
                    rowsChanged = recent.RowsChanged,
                    rootsFound = recent.Roots.Count,
                    roots = recent.Roots.Take(50).Select(r => $"{r.Table}:{r.Id:D}").ToArray(),
                    // #1943: secure records whose OWN modifiedon is in the window (none counts as changed) — a record made
                    // secure by a direct write changes no child, so it is not among the roots found above.
                    rootsChanged = rootsChanged.Count,
                    // #1943 (review F1): those past the per-run cap, left to the re-opened catch-up (catchUp.complete false).
                    rootsChangedOverflow,
                    undetermined = recent.Undetermined.Take(50).ToArray(),
                    failure = recent.Failure,
                    // Task 147 r1: what earlier runs left unfinished and this run looked at again — records whose pass was
                    // incomplete, and changed rows that could not be placed — and what is carried to the next run.
                    retriedRoots = retriedRoots.Take(50).Select(r => $"{r.Table}:{r.Id:D}").ToArray(),
                    // Task 147 r1c-v1: carried records no longer flagged secure — not reconciled again, not carried on.
                    droppedRoots = droppedRoots.Take(50).Select(r => $"{r.Table}:{r.Id:D}").ToArray(),
                    retriedRows = recent.RetriedRows,
                    carriedRoots,
                    carriedRows,
                    // More unplaced rows than the job carries: the watermark stays, so the whole window is listed again.
                    pendingOverflow = recent.PendingOverflow,
                    // Task 147 r1c: the once-per-instance walk of every secure record after a start (a restart, deploy or
                    // scale-out loses what the previous instance carried). `complete` once this instance has walked them all.
                    catchUp = new
                    {
                        ran = catchUpRuns,
                        complete = catchUpComplete,
                        rootsInRun = work.Count(w => w.Group == WorkGroup.CatchUp),
                        startPosition = catchUpRuns ? catchUpStart + 1 : (int?)null,
                        resumeAfter = catchUpResumeAfter is { } c ? $"{c.Table}:{c.Id:D}" : null,
                    },
                },
                // UAC task 194 (#2339): the ordinary-parent-move pass — the ordinary records reassigned since `since` whose
                // default-team-owned children were moved (or, report-only, would be) to the owner the rule gives them now.
                // `changes` lists moves, refusals, failures and the rows reported but not moved (user-owned, a non-default
                // owner team, a Secure answer), at most MaxSampledChanges; the counts are complete.
                ordinaryMoves = new
                {
                    mode = ordinary.Writes ? ModeWrite : ModeReportOnly,
                    since = ordinary.Since.UtcDateTime,
                    rootsListed = ordinary.RootsListed,
                    rootsInRun = ordinary.RootsInRun,
                    rootsOverflow = ordinary.RootsOverflow,
                    retriedRoots = ordinary.RetriedRoots,
                    examined = ordinary.Totals[0],
                    alreadyCorrect = ordinary.Totals[1],
                    moved = ordinary.Totals[2],
                    wouldMove = ordinary.Totals[3],
                    untouched = ordinary.Totals[4],
                    refused = ordinary.Totals[5],
                    failed = ordinary.Totals[6],
                    reported = ordinary.Reported,
                    writesAttempted = ordinary.WritesAttempted,
                    capReached = ordinary.CapReached,
                    timeSliceReached = ordinary.TimeSliceReached,
                    // 194b R4-3 (B4): where the next report-only run resumes (null: from the first root of its window).
                    reportCursor = ordinary.ReportCursor,
                    incompleteRoots = ordinary.IncompleteRoots.Take(50).ToArray(),
                    carriedRoots = ordinary.CarriedRoots,
                    // F1-a: the walk's document files handed to the re-file pass, and the roots deferred for want of room.
                    documentsHandedOff = ordinary.DocumentsHandedOff,
                    documentsToHandOff = ordinary.DocumentsToHandOff,
                    handOffsPending = ordinary.HandOffsPending,
                    deferredRoots = ordinary.DeferredRoots.Take(50).ToArray(),
                    // F4-a: the per-instance catch-up sweep over every ordinary root (once in each mode).
                    sweep = new { ran = ordinary.SweepRan, roots = ordinary.SweepRoots, complete = ordinary.SweepComplete },
                    changesTotal = ordinary.ChangesTotal,
                    changesListed = ordinary.Changes.Count,
                    changes = ordinary.Changes,
                    failure = ordinary.Failure,
                },
                // Round 46 item 2: the Make Secure file backstop. `mode` "write" is what Set-AccessRibbon.ps1
                // -SecureTransitionDeployed requires of the latest run before it ships Make Secure.
                makeSecureRelocations = new
                {
                    mode = relocations.Mode,
                    ledgerOwing = relocations.LedgerOwing,
                    stranded = relocations.Stranded,
                    examined = relocations.Files?.Examined ?? 0,
                    moved = relocations.Files?.Moved ?? 0,
                    incomplete = relocations.Files?.Incomplete ?? 0,
                    heldIsolated = relocations.HeldIsolated.Count,
                    checkedOut = relocations.CheckedOut.Count,
                    unresolvable = relocations.Files?.Unresolvable ?? 0,
                    counts = relocations.Files?.Counts,
                    incompleteDocuments = relocations.Files?.IncompleteDocuments,
                    unresolvableDocuments = relocations.Files?.UnresolvableDocuments,
                    heldIsolatedDocuments = relocations.HeldIsolated.Take(MaxSampledRefileIds).ToArray(),
                    checkedOutDocuments = relocations.CheckedOut.Take(MaxSampledRefileIds).ToArray(),
                    sourceChangedAfterMove = relocations.Files?.SourceChangedAfterMove ?? 0,
                    versionsTruncated = relocations.Files?.VersionsTruncated ?? 0,
                    sourceKeptForOtherRecords = relocations.Files?.SourceKeptForOtherRecords ?? 0,
                    // #2159 slice 2c: documents whose F3 release expired this run, unused — still held (ids only).
                    releaseExpiredDocuments = relocations.ReleaseExpired.Take(MaxSampledRefileIds).ToArray(),
                    terminal = relocations.Terminal,
                    terminalDocuments = relocations.TerminalNew.Take(MaxSampledRefileIds).ToArray(),
                    passComplete = relocations.PassComplete,
                    failure = relocations.Failure,
                },
                // #2159: the re-file pass — documents modified since `since` whose file is not in a derived container,
                // relocated with purpose Refile. Counts are complete; each list names at most MaxSampledRefileIds ids.
                refileRelocations = new
                {
                    mode = refile.Mode,
                    since = refile.Since?.UtcDateTime,
                    listed = refile.Listed,
                    listedUnderChangedRecords = refile.ListedUnderChangedRecords,
                    retriedRows = refile.RetriedRows,
                    examined = refile.Examined,
                    windowDrained = refile.WindowDrained,
                    watermarkHeld = refile.WatermarkHeld,
                    inPlace = refile.InPlace,
                    skippedForMakeSecure = refile.SkippedForMakeSecure,
                    moved = refile.Moved.Count,
                    wouldMove = refile.WouldMove.Count,
                    stillOwed = refile.StillOwed.Count,
                    heldIsolated = refile.HeldIsolated.Count,
                    checkedOut = refile.CheckedOut.Count,
                    unfiledHomeUnknown = refile.UnfiledHomeUnknown.Count,
                    sourceUnverified = refile.SourceUnverified.Count,
                    fileMissing = refile.FileMissing.Count,
                    undecidable = refile.Undecidable.Count,
                    undecidableDropped = refile.UndecidableDropped.Count,
                    failed = refile.Failed.Count,
                    deferred = refile.Deferred.Count,
                    capReached = refile.CapReached,
                    carried = refile.Carried,
                    movedDocuments = refile.Moved.Take(MaxSampledRefileIds).ToArray(),
                    wouldMoveDocuments = refile.WouldMove.Take(MaxSampledRefileIds).ToArray(),
                    stillOwedDocuments = refile.StillOwed.Take(MaxSampledRefileIds).ToArray(),
                    heldIsolatedDocuments = refile.HeldIsolated.Take(MaxSampledRefileIds).ToArray(),
                    checkedOutDocuments = refile.CheckedOut.Take(MaxSampledRefileIds).ToArray(),
                    unfiledHomeUnknownDocuments = refile.UnfiledHomeUnknown.Take(MaxSampledRefileIds).ToArray(),
                    sourceUnverifiedDocuments = refile.SourceUnverified.Take(MaxSampledRefileIds).ToArray(),
                    fileMissingDocuments = refile.FileMissing.Take(MaxSampledRefileIds).ToArray(),
                    undecidableDocuments = refile.Undecidable.Take(MaxSampledRefileIds).ToArray(),
                    undecidableDroppedDocuments = refile.UndecidableDropped.Take(MaxSampledRefileIds).ToArray(),
                    failedDocuments = refile.Failed.Take(MaxSampledRefileIds).ToArray(),
                    deferredDocuments = refile.Deferred.Take(MaxSampledRefileIds).ToArray(),
                    releaseExpiredDocuments = refile.ReleaseExpired.Take(MaxSampledRefileIds).ToArray(),
                    terminal = refile.Terminal,
                    terminalDocuments = refile.TerminalNew.Take(MaxSampledRefileIds).ToArray(),
                    failure = refile.Failure,
                },
                // Task 173 (owner rounds 81/84): To Do, Event, Communication and Document show the most restrictive Access
                // Permission of what they are filed under. `changes` lists the rows set (or, report-only, to be set).
                accessPermission = accessPermission.Run is not { } apRun
                    ? (object)new { mode = accessPermission.Mode }
                    : new
                    {
                        mode = accessPermission.Mode,
                        since = accessPermission.Since?.UtcDateTime,
                        rowsChanged = apRun.RowsChanged,
                        examined = apRun.Examined,
                        alreadyCorrect = apRun.AlreadyCorrect,
                        changed = apRun.Changed,
                        wouldChange = apRun.WouldChange,
                        parentless = apRun.Parentless,
                        undetermined = apRun.Undetermined,
                        failed = apRun.Failed,
                        // F3: rows left for the next run because this one reached the write cap (its window was kept).
                        deferred = apRun.Deferred,
                        writeCapReached = apRun.WriteCapReached,
                        problems = apRun.Problems,
                        changes = apRun.Changes.Select(c => new { table = c.Table, id = c.Id, from = c.From, to = c.To }).ToArray(),
                        carriedRows = accessPermission.CarriedRows,
                        tablesWithoutColumn = apRun.TablesWithoutColumn,
                        // Task 189 (D7 T7a): documents' own-narrowing records written, documents held above their parents by
                        // their own narrowing, and documents that changed between read and write (nothing written, carried).
                        recordsWritten = apRun.RecordsWritten,
                        ownKept = apRun.OwnKept,
                        changedConcurrently = apRun.ChangedConcurrently,
                        // Task 189 fix round C (K1+K2): documents whose own narrowing could not be known, raised to what their
                        // parents give (value only; never lowered).
                        raisedOnly = apRun.RaisedOnly,
                        // Owner round 122 fix round 2 (K2): save-time Restricted documents not released because the
                        // document link columns are not field-secured. Not carried, so they never hold the window.
                        releaseRefusedLinkColumnsNotSecured = apRun.ReleaseRefusedLinkColumnsNotSecured,
                        failure = apRun.Failure,
                        sweep = new
                        {
                            ran = accessPermission.SweepRan,
                            rows = apRun.SweepRows,
                            complete = accessPermission.SweepComplete,
                        },
                    },
                attempt = context.Attempt,
            }, ResultJsonOptions));
    }

    /// <summary>
    /// UAC task 194b R4-3 (B4): the carried roots, least-recently-attempted first (a root without a stamp first), then table
    /// and id — so a root a slice or a cap left behind moves forward every run and is reached within as many runs as there
    /// are carried roots ahead of it.
    /// </summary>
    internal static List<(string Table, Guid Id)> OrderLeastRecentlyAttempted(
        IEnumerable<(string Table, Guid Id)> carried, IReadOnlyDictionary<(string Table, Guid Id), long> stamps) =>
        carried
            .OrderBy(r => stamps.TryGetValue(r, out var stamp) ? stamp : long.MinValue)
            .ThenBy(r => r.Table, StringComparer.Ordinal)
            .ThenBy(r => r.Id)
            .ToList();

    /// <summary>
    /// UAC task 194b R4-3 (B4): <paramref name="ordered"/> (change time, table, id) starting after <paramref name="after"/>, then
    /// the rest from the start: the report-only cursor. Null: unchanged.
    /// </summary>
    internal static List<(string Table, Guid Id, DateTime ModifiedOn)> ResumeAfter(
        List<(string Table, Guid Id, DateTime ModifiedOn)> ordered, (DateTime ModifiedOn, string Table, Guid Id)? after)
    {
        if (after is not { } cursor)
            return ordered;

        bool IsAfter((string Table, Guid Id, DateTime ModifiedOn) r)
        {
            var byTime = r.ModifiedOn.CompareTo(cursor.ModifiedOn);
            if (byTime != 0)
                return byTime > 0;
            var byTable = StringComparer.Ordinal.Compare(r.Table, cursor.Table);
            return byTable != 0 ? byTable > 0 : r.Id.CompareTo(cursor.Id) > 0;
        }

        return ordered.Where(IsAfter).Concat(ordered.Where(r => !IsAfter(r))).ToList();
    }

    /// <summary>What the ordinary-parent-move pass did in one run (UAC task 194).</summary>
    internal sealed record OrdinaryMovesRun(bool Writes, DateTimeOffset Since)
    {
        public int RootsListed { get; init; }
        public int RootsInRun { get; init; }

        /// <summary>Listed or carried roots past <see cref="MaxRootsPerRunConfigKey"/>: carried to the next run (writes on).</summary>
        public int RootsOverflow { get; init; }
        public int RetriedRoots { get; init; }

        /// <summary>examined, alreadyCorrect, changed, wouldChange, untouched, refused, failed (the reconciler's per-table counts).</summary>
        public int[] Totals { get; } = new int[7];

        /// <summary>Rows reported and not moved (user-owned, a non-default owner team, a Secure answer).</summary>
        public int Reported { get; set; }
        public int WritesAttempted { get; set; }
        public bool CapReached { get; set; }

        /// <summary>194b: the pass stopped at <see cref="OrdinaryTimeSlice"/>; the roots it did not reach are carried or held.</summary>
        public bool TimeSliceReached { get; set; }

        /// <summary>194b R4-3 (B4): where the next report-only run resumes (<c>table:id</c>), or null (from its first root).</summary>
        public string? ReportCursor { get; set; }

        /// <summary>Roots whose pass did not complete for a reason other than the write cap (a refusal, a failure).</summary>
        public List<string> IncompleteRoots { get; } = new();
        public int CarriedRoots { get; set; }

        /// <summary>F1-a: documents handed to the re-file pass this run (writes on), or that would be (report-only).</summary>
        public int DocumentsHandedOff { get; set; }
        public int DocumentsToHandOff { get; set; }

        /// <summary>F1-a: roots whose documents did not all fit the re-file carry: carried, or held by the sweep cursor.</summary>
        public List<string> DeferredRoots { get; } = new();
        public int HandOffsPending { get; set; }
        public int ChangesTotal { get; set; }
        public List<object> Changes { get; } = new();
        public string? Failure { get; init; }

        /// <summary>F4-a: whether the catch-up sweep ran this run, how many roots its window held, and whether it has now
        /// covered every ordinary root once in this mode.</summary>
        public bool SweepRan { get; init; }
        public int SweepRoots { get; init; }
        public bool SweepComplete { get; set; }

        /// <summary>Only a WRITING pass fails the run: a listing that failed, or a root left incomplete (not merely capped).</summary>
        public bool FailsTheRun => Writes && (Failure is not null || IncompleteRoots.Count > 0);
    }

    /// <summary>
    /// UAC task 194 (#2339, owner round 128 item 3) — the ORDINARY-PARENT-MOVE pass: every ordinary project, matter and work
    /// assignment (<c>sprk_issecure</c> = false; an empty flag is not listed — it fails closed) whose <c>modifiedon</c> is at or
    /// after this pass's watermark, with the roots earlier runs carried, goes through
    /// <see cref="SecureChildReconciler.ReconcileOrdinaryParentMoveAsync"/>: its default-team-owned children follow it to the
    /// owner the rule gives them now (an MDA Assign changes the record's <c>modifiedon</c> — verified on dev, see
    /// notes/task-194-ordinary-parent-move.md). Never an isolated row, never out of isolation.
    /// </summary>
    /// <remarks>
    /// <para><b>Report-only unless</b> <see cref="OrdinaryMovesWritesEnabledConfigKey"/> is <c>true</c> and the recent-changes
    /// emergency stop is not set. At most <see cref="MaxRootsPerRunConfigKey"/> roots (carried first, least-recently-attempted
    /// first; then oldest change first) and <see cref="MaxOrdinaryMovesPerRunConfigKey"/> owner writes a run.</para>
    /// <para><b>No starvation (194b R4-3, B4).</b> The time slice never stops a group (listed or swept) before its first root, so
    /// every run advances at least one root of each; a carried root is reached within as many runs as there are carried roots
    /// ahead of it. A report-only run, which keeps no carry, resumes after the last listed root the previous report-only run
    /// reached and wraps around (the report-only cursor, per instance: a restart starts its window from the first root).</para>
    /// <para><b>Window and carry</b> (the secure pass's rule): the watermark moves only in a run that WROTE and listed
    /// completely, so a report-only run looks back <see cref="InitialRecentChangesLookback"/> every time. With writes on, a
    /// root left incomplete, stopped by the write cap, or past the root cap is carried and looked at first next run (at most
    /// <see cref="MaxCarriedRows"/>; beyond that the watermark stays). Per instance, like every window of this job: a restart
    /// looks back <see cref="InitialRecentChangesLookback"/>.</para>
    /// <para><b>Catch-up sweep (F4-a).</b> Until this instance has walked every ordinary root once in the current mode, each
    /// run also walks the next <see cref="MaxRootsPerRunConfigKey"/> roots after a per-instance cursor (table, then id), so a
    /// reassignment older than the window (made before this pass existed, or carried by an instance that restarted) is found.
    /// A switch of <see cref="OrdinaryMovesWritesEnabledConfigKey"/> starts it over: a report-only cover never stands in for a
    /// writing one. A root of the window that the write cap stops is carried; a carry past <see cref="MaxCarriedRows"/> holds
    /// the cursor where it is.</para>
    /// <para><b>Files (F1-a).</b> With writes on, every document of a root's walk that may move
    /// (<see cref="SecureChildReconcileReport.OrdinaryHandOffDocumentIds"/>: any owner, never one held in isolation or answered
    /// Secure) is handed to the re-file pass as a carried row, so it is examined and moved this run or later under the
    /// Refile purpose's guards, <see cref="MaxRelocationsPerRunConfigKey"/> and the examination budget. At most the carry's
    /// free capacity (<see cref="MaxCarriedRows"/> minus the re-file carry and the hand-offs not yet reached); a root whose
    /// documents do not all fit is deferred: carried, or (swept) held by the cursor, never dropped, and walked again.</para>
    /// <para>Never throws (cancellation aside): a listing fault is reported and the window is listed again next run.</para>
    /// </remarks>
    private async Task<OrdinaryMovesRun> ReconcileOrdinaryParentMovesAsync(
        SecureChildReconciler reconciler, IGenericEntityService dataverse, bool recentWrites, int rootCap,
        DateTimeOffset startedAt, Guid runId, CancellationToken ct)
    {
        var passStart = _timeProvider.GetTimestamp();
        var writes = recentWrites
                     && bool.TryParse(_configuration[OrdinaryMovesWritesEnabledConfigKey], out var enabled) && enabled;
        var mode = writes ? SecureChildReconcileMode.Apply : SecureChildReconcileMode.ReportOnly;
        var writeCap = int.TryParse(_configuration[MaxOrdinaryMovesPerRunConfigKey], out var configured) && configured > 0
            ? configured
            : DefaultMaxOrdinaryMovesPerRun;

        DateTimeOffset since;
        List<(string Table, Guid Id)> carried;
        bool sweepRuns;
        (string Table, Guid Id) sweepAfter;
        long runNumber;
        (DateTime ModifiedOn, string Table, Guid Id)? reportAfter;
        lock (_cursorGate)
        {
            since = _ordinaryMovesWatermark ?? startedAt - InitialRecentChangesLookback;
            runNumber = ++_ordinaryRunNumber;

            // 194b R4-3 (B4): least-recently-attempted first (a root never stamped — carried by an earlier instance's state,
            // which a start loses — ranks first), then table and id for a stable order.
            carried = OrderLeastRecentlyAttempted(_pendingOrdinaryRoots, _ordinaryAttemptStamps);

            // A writing run carries what it does not reach; the report-only cursor is for report-only runs alone.
            if (writes)
                _ordinaryReportCursor = null;
            reportAfter = _ordinaryReportCursor;

            // F4-a: a switch of the mode starts the sweep over, so a report-only cover never stands in for a writing one.
            if (_ordinarySweepWrites != writes)
            {
                _ordinarySweepWrites = writes;
                _ordinarySweepCursor = null;
                _ordinarySweepComplete = false;
            }

            sweepRuns = !_ordinarySweepComplete;
            sweepAfter = _ordinarySweepCursor ?? ChildAccessPermissionReconciler.SweepFromStart;
        }

        List<(string Table, Guid Id, DateTime ModifiedOn)> changed;
        (List<(string Table, Guid Id)> Rows, bool ReachedEnd) sweep = (new(), false);
        try
        {
            changed = await ListChangedOrdinaryRootsAsync(dataverse, since, ct).ConfigureAwait(false);
            if (sweepRuns)
                sweep = await OrdinarySweepWindowAsync(dataverse, sweepAfter, rootCap, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] ordinary-moves run={RunId}: the ordinary records changed since " +
                "{Since:o} could not be listed; that window is looked at again next run.", runId, since);
            return new OrdinaryMovesRun(writes, since)
            {
                Failure = "the reassigned ordinary records could not be listed",
                CarriedRoots = carried.Count,
            };
        }

        // 194b R4-3 (B4): a report-only run resumes after the root the previous one reached, then wraps around.
        var changedInOrder = ResumeAfter(
            changed.OrderBy(r => r.ModifiedOn).ThenBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList(),
            reportAfter);
        var changedAt = changedInOrder.ToDictionary(r => (r.Table, r.Id), r => r.ModifiedOn);
        var all = carried
            .Concat(changedInOrder.Select(r => (r.Table, r.Id)))
            .Distinct()
            .ToList();
        var take = all.Take(rootCap).ToList();
        var overflow = all.Skip(rootCap).ToList();
        // F4-a: the sweep window after the recent and carried roots (a root in both is walked once).
        var listedNow = take.Concat(overflow).ToHashSet();
        var sweepBatch = sweep.Rows.Where(r => !listedNow.Contains(r)).ToList();
        var run = new OrdinaryMovesRun(writes, since)
        {
            RootsListed = changed.Count,
            RootsInRun = take.Count + sweepBatch.Count,
            RootsOverflow = overflow.Count,
            RetriedRoots = carried.Count,
            SweepRan = sweepRuns,
            SweepRoots = sweep.Rows.Count,
        };

        var carry = new List<(string Table, Guid Id)>();
        (string Table, Guid Id)? firstDeferredSweep = null;
        var remaining = writeCap;

        // 194b R4-3 (B4): the roots walked this run (stamped least-recently-attempted), how many of each group, and the last
        // recent root walked (the report-only cursor). The time slice never stops a group before its first root, so every
        // run advances at least one root of each: no root starves behind a slice spent by the listing or by one long root.
        var walked = new List<(string Table, Guid Id)>();
        var walkedListed = 0;
        var walkedSwept = 0;
        var anyListedNotReached = overflow.Count > 0;
        (string Table, Guid Id)? lastListedWalked = null;
        foreach (var (table, id, swept) in take.Select(r => (r.Table, r.Id, false))
                     .Concat(sweepBatch.Select(r => (r.Table, r.Id, true))))
        {
            ct.ThrowIfCancellationRequested();
            var key = $"{table}:{id:D}";
            if (writes && remaining == 0)
            {
                // The write cap is spent: the rest are carried unread (each is walked again first next run).
                carry.Add((table, id));
                run.CapReached = true;
                anyListedNotReached |= !swept;
                continue;
            }

            // 194b: the time slice is spent. A recent root is carried, a swept one holds the cursor (as a deferred one does);
            // none is dropped. Report-only keeps no carry; the report-only cursor resumes after the last root it reached.
            // R4-3 (B4): never before the group's first root, so each run advances at least one root of each group.
            var sliceSpent = _timeProvider.GetElapsedTime(passStart) >= OrdinaryTimeSlice;
            if (sliceSpent && (swept ? walkedSwept : walkedListed) > 0)
            {
                run.TimeSliceReached = true;
                anyListedNotReached |= !swept;
                if (swept)
                    firstDeferredSweep ??= (table, id);
                else
                    carry.Add((table, id));
                continue;
            }

            walked.Add((table, id));
            if (swept)
            {
                walkedSwept++;
            }
            else
            {
                walkedListed++;
                if (changedAt.ContainsKey((table, id)))
                    lastListedWalked = (table, id);
            }

            _logger.LogInformation(
                "[SECURE-CHILD-RECONCILE] ordinary-moves run={RunId} mode={Mode} root={Root} swept={Swept}: an ordinary record " +
                "changed since {Since:o}, carried, or reached by the catch-up sweep; its children follow its owner.",
                runId, mode, key, swept, since);
            var report = await reconciler.ReconcileOrdinaryParentMoveAsync(table, id, mode, remaining, ct).ConfigureAwait(false);
            remaining = Math.Max(0, remaining - report.OrdinaryWritesAttempted);
            run.WritesAttempted += report.OrdinaryWritesAttempted;
            foreach (var t in report.Tables)
            {
                run.Totals[0] += t.Examined; run.Totals[1] += t.AlreadyCorrect; run.Totals[2] += t.Changed;
                run.Totals[3] += t.WouldChange; run.Totals[4] += t.Untouched; run.Totals[5] += t.Refused; run.Totals[6] += t.Failed;
            }

            // F1-a: the files of the walk's documents (any owner, never one held in isolation or answered Secure) go to the
            // re-file pass, at most the carry's free capacity. A root whose documents do not all fit is deferred: carried (or,
            // swept, held by the cursor), never dropped; it is walked again and only the rest are handed on.
            var deferred = false;
            if (writes)
            {
                lock (_cursorGate)
                {
                    var pending = new HashSet<Guid>(_refilePending);
                    pending.UnionWith(_ordinaryHandOffs);
                    var fresh = report.OrdinaryHandOffDocumentIds.Where(d => !pending.Contains(d)).ToList();
                    var free = Math.Max(0, MaxCarriedRows - pending.Count);
                    foreach (var documentId in fresh.Take(free))
                        _ordinaryHandOffs.Add(documentId);
                    run.DocumentsHandedOff += Math.Min(fresh.Count, free);
                    deferred = fresh.Count > free;
                }
            }
            else
            {
                run.DocumentsToHandOff += report.OrdinaryHandOffDocumentIds.Count;
            }

            if (deferred)
            {
                run.DeferredRoots.Add(key);
                if (swept)
                    firstDeferredSweep ??= (table, id);
                else if (report.IsComplete)
                    carry.Add((table, id));
            }

            var capped = report.Changes.Any(c => c.Detail == SecureChildReconciler.OrdinaryMoveCapDetail);
            run.CapReached |= capped;
            if (!report.IsComplete)
            {
                carry.Add((table, id));
                var onlyCapped = report.Status == SecureChildReconcileStatus.Incomplete
                                 && report.Changes.Where(c => c.Outcome is SecureChildRowOutcome.Failed or SecureChildRowOutcome.Refused)
                                     .All(c => c.Detail == SecureChildReconciler.OrdinaryMoveCapDetail);
                if (!onlyCapped)
                    run.IncompleteRoots.Add($"{key}: {report.Status}{(report.Detail is null ? "" : " — " + report.Detail)}");
            }

            foreach (var change in report.Changes)
            {
                run.ChangesTotal++;
                if (change.Outcome == SecureChildRowOutcome.Untouched)
                    run.Reported++;
                if (run.Changes.Count >= MaxSampledChanges)
                    continue;
                run.Changes.Add(new
                {
                    root = key,
                    table = change.Table,
                    id = change.Id,
                    previousOwner = change.PreviousOwner is { } p ? $"{p.Kind.ToEntitySet()}({p.Id:D})" : null,
                    targetTeam = change.TargetTeamId,
                    outcome = change.Outcome.ToString(),
                    detail = change.Detail,
                });
            }
        }

        lock (_cursorGate)
        {
            // Only a run that WROTE moves the window and keeps a carry (a report-only run corrected nothing — the secure
            // pass's rule). Past MaxCarriedRows the watermark stays, so the whole window is listed again.
            if (writes)
            {
                var toCarry = carry.Concat(overflow).Distinct().ToList();
                _pendingOrdinaryRoots.Clear();
                _pendingOrdinaryRoots.UnionWith(toCarry.Take(MaxCarriedRows));
                if (toCarry.Count <= MaxCarriedRows)
                    _ordinaryMovesWatermark = startedAt - RecentChangesOverlap;
            }

            // 194b R4-3 (B4): stamp the walked roots last, and a root first carried now just before them; keep only the
            // carried set's stamps.
            foreach (var root in walked)
                _ordinaryAttemptStamps[root] = (runNumber * 2) + 1;
            foreach (var root in _pendingOrdinaryRoots)
                _ordinaryAttemptStamps.TryAdd(root, runNumber * 2);
            foreach (var stale in _ordinaryAttemptStamps.Keys.Where(k => !_pendingOrdinaryRoots.Contains(k)).ToList())
                _ordinaryAttemptStamps.Remove(stale);

            // 194b R4-3 (B4): a report-only run that did not reach every listed root resumes after the last one it walked.
            if (!writes)
            {
                _ordinaryReportCursor = anyListedNotReached && lastListedWalked is { } last
                    ? (changedAt[last], last.Table, last.Id)
                    : anyListedNotReached ? _ordinaryReportCursor : null;
            }

            run.ReportCursor = _ordinaryReportCursor is { } cursor ? $"{cursor.Table}:{cursor.Id:D}" : null;

            // F4-a: the sweep moves past its window (a root of it the write cap stopped, or left incomplete, is carried
            // above). Report-only it moves too: one report of every root, then it stops until the mode changes. A carry past
            // MaxCarriedRows holds it where it is.
            if (sweepRuns && (!writes || carry.Concat(overflow).Distinct().Count() <= MaxCarriedRows))
            {
                // F1-a: a swept root whose documents did not all fit holds the cursor just before it (the carry is full:
                // the sweep does not advance past roots it could not hand off).
                var held = firstDeferredSweep is { } first ? sweep.Rows.IndexOf(first) : -1;
                if (held >= 0)
                {
                    if (held > 0)
                        _ordinarySweepCursor = sweep.Rows[held - 1];
                }
                else if (sweep.ReachedEnd)
                {
                    _ordinarySweepComplete = true;
                    _ordinarySweepCursor = null;
                }
                else if (sweep.Rows.Count > 0)
                {
                    _ordinarySweepCursor = sweep.Rows[^1];
                }
            }

            run.SweepComplete = _ordinarySweepComplete;
            run.CarriedRoots = _pendingOrdinaryRoots.Count;
            run.HandOffsPending = _ordinaryHandOffs.Count;
        }

        _logger.Log(
            run.FailsTheRun ? LogLevel.Warning : LogLevel.Information,
            "[SECURE-CHILD-RECONCILE] ordinary-moves run={RunId} mode={Mode} since={Since:o} rootsListed={Listed} " +
            "rootsInRun={InRun} rootsOverflow={Overflow} examined={Examined} alreadyCorrect={AlreadyCorrect} moved={Moved} " +
            "wouldMove={WouldMove} reported={Reported} refused={Refused} failed={Failed} writesAttempted={Writes} " +
            "capReached={CapReached} timeSliceReached={TimeSliceReached} incompleteRoots={Incomplete} carriedRoots={Carried} sweepRan={SweepRan} " +
            "sweepRoots={SweepRoots} sweepComplete={SweepComplete} documentsHandedOff={HandedOff} deferredRoots={Deferred}",
            runId, writes ? ModeWrite : ModeReportOnly, since, run.RootsListed, run.RootsInRun, run.RootsOverflow, run.Totals[0],
            run.Totals[1], run.Totals[2], run.Totals[3], run.Reported, run.Totals[5], run.Totals[6], run.WritesAttempted,
            run.CapReached, run.TimeSliceReached, run.IncompleteRoots.Count, run.CarriedRoots, run.SweepRan, run.SweepRoots, run.SweepComplete,
            run.DocumentsHandedOff, run.DeferredRoots.Count);
        return run;
    }

    /// <summary>
    /// UAC task 194 F4-b: the ordinary roots' tables, in sweep order: the three secure-flaggable roots and the service request
    /// (which carries no <c>sprk_issecure</c> and is never secure).
    /// </summary>
    internal static readonly IReadOnlyList<string> OrdinaryRootTables = SecureChildLineage.Roots
        .Append("sprk_servicerequest")
        .OrderBy(t => t, StringComparer.Ordinal)
        .ToArray();

    /// <summary>Only the three flaggable roots are filtered on <c>sprk_issecure = false</c> (an empty flag fails closed).</summary>
    private static void OnlyOrdinary(QueryExpression query, string table)
    {
        if (SecureChildLineage.IsRoot(table))
            query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Equal, false);
    }

    /// <summary>
    /// UAC task 194 F4-a: the next window of the catch-up sweep: at most <paramref name="cap"/> ordinary roots after
    /// <paramref name="after"/>, in table then id order (the Access Permission sweep's paging), and whether it reached the
    /// end. A fault propagates (the caller reports it).
    /// </summary>
    private static async Task<(List<(string Table, Guid Id)> Rows, bool ReachedEnd)> OrdinarySweepWindowAsync(
        IGenericEntityService dataverse, (string Table, Guid Id) after, int cap, CancellationToken ct)
    {
        var rows = new List<(string Table, Guid Id)>();
        foreach (var table in OrdinaryRootTables)
        {
            if (string.CompareOrdinal(table, after.Table) < 0)
                continue;
            var remaining = cap - rows.Count;
            if (remaining <= 0)
                return (rows, false);

            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(table + "id"), NoLock = true, TopCount = remaining + 1 };
            query.AddOrder(table + "id", OrderType.Ascending);
            OnlyOrdinary(query, table);
            if (string.Equals(table, after.Table, StringComparison.Ordinal) && after.Id != Guid.Empty)
                query.Criteria.AddCondition(table + "id", ConditionOperator.GreaterThan, after.Id);

            var page = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities
                .Where(e => e.Id != Guid.Empty)
                .ToList();
            rows.AddRange(page.Take(remaining).Select(e => (table, e.Id)));
            if (page.Count > remaining)
                return (rows, false);
        }

        return (rows, true);
    }

    /// <summary>
    /// UAC task 194: the ordinary projects, matters and work assignments (<c>sprk_issecure</c> = false), and (F4-b) the
    /// service requests, modified at or after <paramref name="since"/>. A fault propagates (the caller reports it).
    /// </summary>
    private static async Task<List<(string Table, Guid Id, DateTime ModifiedOn)>> ListChangedOrdinaryRootsAsync(
        IGenericEntityService dataverse, DateTimeOffset since, CancellationToken ct)
    {
        var roots = new List<(string, Guid, DateTime)>();
        foreach (var table in OrdinaryRootTables)
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(table + "id", ModifiedOnColumn), NoLock = true };
            query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
            OnlyOrdinary(query, table);
            foreach (var row in await ReadAllAsync(dataverse, query, ct).ConfigureAwait(false))
            {
                if (row.Id != Guid.Empty)
                    roots.Add((table, row.Id, row.GetAttributeValue<DateTime?>(ModifiedOnColumn) ?? DateTime.MinValue));
            }
        }

        return roots;
    }

    /// <summary>What the inherited Access Permission pass did in one run (task 173); <see cref="Run"/> null when it did not run.</summary>
    private sealed record AccessPermissionRunReport(
        string Mode, ChildAccessPermissionRun? Run, DateTimeOffset? Since, int CarriedRows, bool SweepRan, bool SweepComplete);

    /// <summary>
    /// Task 173 — runs <see cref="ChildAccessPermissionReconciler"/> over the window since this instance's own Access
    /// Permission watermark, the rows it carried, and (until this instance has covered every row of the four tables once)
    /// a capped sweep window. The watermark moves only past a pass that listed completely, in a run that wrote; undecided
    /// and failed rows are carried (at most <see cref="MaxCarriedRows"/>; beyond that the watermark stays). Never throws
    /// (cancellation aside). Past <see cref="MaxAccessPermissionWritesPerRunConfigKey"/> writes the window is kept (the
    /// watermark and the sweep cursor stay) so the next run continues. <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"/>
    /// is registered unconditionally (<c>AddCoreAncestorResolver</c>), so it is required here like every other service of the
    /// run.
    /// </summary>
    private async Task<AccessPermissionRunReport> ReconcileChildAccessPermissionAsync(
        IServiceProvider services, IGenericEntityService dataverse, bool writes, DateTimeOffset startedAt, Guid runId,
        CancellationToken ct)
    {
        var mode = writes ? ModeWrite : ModeReportOnly;
        var coreAncestors = services.GetRequiredService<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>();

        var cap = int.TryParse(_configuration[MaxAccessPermissionRowsPerRunConfigKey], out var configured) && configured > 0
            ? Math.Min(configured, 4999)
            : DefaultMaxAccessPermissionRowsPerRun;
        var writeCap = int.TryParse(_configuration[MaxAccessPermissionWritesPerRunConfigKey], out var configuredWrites)
                       && configuredWrites > 0
            ? configuredWrites
            : DefaultMaxAccessPermissionWritesPerRun;

        DateTimeOffset since;
        List<(string Table, Guid Id)> carried;
        (string Table, Guid Id)? sweepAfter;
        lock (_cursorGate)
        {
            since = _accessPermissionWatermark ?? startedAt - InitialRecentChangesLookback;
            carried = _accessPermissionPending.OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
            sweepAfter = _accessPermissionSweepComplete
                ? null
                : _accessPermissionSweepCursor ?? ChildAccessPermissionReconciler.SweepFromStart;
        }

        var reconciler = new ChildAccessPermissionReconciler(
            dataverse, (table, token) => coreAncestors.ProbeColumnsAsync(table, token), _logger,
            token => DocumentLinkColumnsSecuredAsync(services, token));
        ChildAccessPermissionRun run;
        try
        {
            run = await reconciler.RunAsync(since, carried, sweepAfter, cap, writes, writeCap, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[CHILD-ACCESS-PERMISSION] run={RunId}: the pass faulted; its window is looked at again next run.",
                runId);
            run = new ChildAccessPermissionRun { Failure = "the pass faulted", Carry = carried };
        }

        int carriedRows;
        bool sweepComplete;
        lock (_cursorGate)
        {
            // The window moves only past a pass that listed completely, wrote, and did not stop at the write cap (F3): a
            // capped run's unwritten rows are listed again from the same watermark next run.
            var overflow = run.Carry.Count > MaxCarriedRows;
            if (run.Failure is null && writes && !overflow && !run.WriteCapReached)
                _accessPermissionWatermark = startedAt - RecentChangesOverlap;

            _accessPermissionPending.Clear();
            _accessPermissionPending.UnionWith(run.Carry.Take(MaxCarriedRows));

            // The sweep advances only in a run that listed completely and wrote (a report-only run corrected nothing).
            if (sweepAfter is not null && run.Failure is null && writes && !run.WriteCapReached)
            {
                if (run.SweepReachedEnd)
                {
                    _accessPermissionSweepComplete = true;
                    _accessPermissionSweepCursor = null;
                }
                else if (run.SweepResumeAfter is { } resume)
                {
                    _accessPermissionSweepCursor = resume;
                }
            }

            (carriedRows, sweepComplete) = (_accessPermissionPending.Count, _accessPermissionSweepComplete);
        }

        _logger.Log(
            run.Failure is null && run.Failed == 0 ? LogLevel.Information : LogLevel.Warning,
            "[CHILD-ACCESS-PERMISSION] run={RunId} mode={Mode} since={Since:o} rowsChanged={RowsChanged} examined={Examined} " +
            "alreadyCorrect={AlreadyCorrect} changed={Changed} wouldChange={WouldChange} parentless={Parentless} " +
            "undetermined={Undetermined} failed={Failed} deferred={Deferred} writeCapReached={WriteCapReached} carried={Carried} " +
            "sweepRows={SweepRows} sweepComplete={SweepComplete} recordsWritten={RecordsWritten} ownKept={OwnKept} " +
            "changedConcurrently={ChangedConcurrently} raisedOnly={RaisedOnly} " +
            "releaseRefusedLinkColumnsNotSecured={ReleaseRefusedLinkColumnsNotSecured} failure={Failure}",
            runId, mode, since, run.RowsChanged, run.Examined, run.AlreadyCorrect, run.Changed, run.WouldChange, run.Parentless,
            run.Undetermined, run.Failed, run.Deferred, run.WriteCapReached, carriedRows, run.SweepRows, sweepComplete,
            run.RecordsWritten, run.OwnKept, run.ChangedConcurrently, run.RaisedOnly, run.ReleaseRefusedLinkColumnsNotSecured,
            run.Failure);

        return new AccessPermissionRunReport(mode, run, since, carriedRows, sweepAfter is not null, sweepComplete);
    }

    /// <summary>What the Make Secure file backstop did in one run (round 46 item 2).</summary>
    internal sealed record MakeSecureRelocationRun(
        string Mode, int LedgerOwing, int Stranded, MakeSecureFilesSummary? Files, bool PassComplete, string? Failure)
    {
        /// <summary>
        /// #2159 B1: the documents this run SETTLED or HELD (isolated, or checked out) — the re-file pass does not relocate them
        /// again. One it failed on, could not decide or left owing is not taken: the re-file pass examines it (and carries it).
        /// </summary>
        public IReadOnlySet<Guid> Taken { get; init; } = new HashSet<Guid>();

        /// <summary>#2159 B7: held by the widening guard — counted apart, not incomplete (only an F3 act releases it).</summary>
        public IReadOnlyList<Guid> HeldIsolated { get; init; } = [];

        /// <summary>#2159 B4: checked out — left alone until the check-in; counted apart, not incomplete.</summary>
        public IReadOnlyList<Guid> CheckedOut { get; init; } = [];

        /// <summary>#2159 B5: the terminal documents of this run (permanent failures), counted apart, never incomplete.</summary>
        public int Terminal { get; init; }

        /// <summary>#2159 B5: the terminal documents not reported before on this instance, with their cause (reported once).</summary>
        public IReadOnlyList<TerminalDocument> TerminalNew { get; init; } = [];

        /// <summary>#2159 slice 2c: the documents whose F3 release had expired, unused, and was dropped this run.</summary>
        public IReadOnlyList<Guid> ReleaseExpired { get; init; } = [];
    }

    /// <summary>
    /// Round 46 item 2 — settles the PENDING Make Secure relocations through the ONE <see cref="DocumentContainerRelocator"/>:
    /// the documents whose relocation ledger still owes a step, and the ISOLATED documents whose file still sits in a
    /// business unit's shared container (a move a <c>files_incomplete</c> left unmade). Capped, in id order after a
    /// per-instance cursor. A listing that cannot complete is reported (<see cref="MakeSecureRelocationRun.Failure"/>) and
    /// never throws: the reconcile passes this run made stand.
    /// </summary>
    private async Task<MakeSecureRelocationRun> SettleMakeSecureRelocationsAsync(
        IServiceProvider services, IGenericEntityService dataverse, bool writes, CancellationToken ct)
    {
        var mode = writes ? ModeWrite : ModeReportOnly;
        var relocator = services.GetService<DocumentContainerRelocator>();
        if (relocator is null)
            return new MakeSecureRelocationRun("unavailable", 0, 0, null, true, null);

        var cap = int.TryParse(_configuration[MaxRelocationsPerRunConfigKey], out var configured) && configured > 0
            ? configured
            : DefaultMaxRelocationsPerRun;

        HashSet<Guid> owing, stranded;
        try
        {
            owing = await ListDocumentsAsync(dataverse, NotNull(DocumentContainerRelocator.RelocationLedgerColumn), ct)
                .ConfigureAwait(false);
            stranded = await ListStrandedIsolatedDocumentsAsync(dataverse, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] The pending Make Secure file relocations could not be listed.");
            return new MakeSecureRelocationRun(mode, 0, 0, null, false, "the pending Make Secure file relocations could not be listed");
        }

        var candidates = owing.Concat(stranded).Distinct().OrderBy(id => id).ToList();
        Guid? after;
        lock (_cursorGate)
            after = _relocationCursor;
        var take = candidates.Where(id => after is not { } a || id.CompareTo(a) > 0).Take(cap).ToList();
        var passComplete = candidates.Count(id => after is not { } a || id.CompareTo(a) > 0) <= cap;
        lock (_cursorGate)
            _relocationCursor = passComplete || take.Count == 0 ? null : take[^1];

        var outcomes = new List<DocumentRelocationOutcome>(take.Count);
        foreach (var documentId in take)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                outcomes.Add(await relocator.RelocateIfMisplacedAsync(documentId, writes, RelocationPurpose.MakeSecure, ct)
                    .ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] Make Secure relocation of document {DocumentId} faulted.", documentId);
                outcomes.Add(new DocumentRelocationOutcome(
                    documentId, RelocationState.Failed, null, null, null, null, $"faulted ({ex.GetType().Name})"));
            }
        }

        // B5: a permanent failure (too large, a full ledger, a ledger unreadable for MaxUnreadableLedgerRuns runs) is
        // TERMINAL: reported once, never incomplete, never retried as unfinished work.
        var terminal = new Dictionary<Guid, string>();
        lock (_cursorGate)
        {
            foreach (var o in outcomes)
            {
                if (o.Pending.Any(p => p.StartsWith(DocumentContainerRelocator.LedgerUnreadablePendingPrefix, StringComparison.Ordinal)))
                {
                    var runs = _unreadableLedgerRuns.GetValueOrDefault(o.DocumentId) + 1;
                    _unreadableLedgerRuns[o.DocumentId] = runs;
                    if (runs >= MaxUnreadableLedgerRuns)
                        terminal[o.DocumentId] = DocumentContainerRelocator.TerminalLedgerUnreadable;
                }
                else
                {
                    _unreadableLedgerRuns.Remove(o.DocumentId);
                }

                foreach (var t in o.MovedAlong.Prepend(o).Where(t => t.TerminalCause is not null))
                    terminal.TryAdd(t.DocumentId, t.TerminalCause!);
            }
        }

        var terminalNew = ReportTerminal(outcomes.SelectMany(o => o.MovedAlong.Prepend(o)).Select(o => o.DocumentId), terminal);

        // B7/B4: a held or checked-out document that owes nothing is waiting on a person, not unfinished work: counted apart.
        var batch = DocumentRelocationBatchResult.From(outcomes);
        var files = MakeSecureFilesSummary.From(batch with
        {
            Incomplete = batch.Incomplete.Where(o => !Waits(o) && !terminal.ContainsKey(o.DocumentId)).ToList(),
        });
        var held = outcomes.Where(o => o.State == RelocationState.HeldIsolated && Waits(o)).Select(o => o.DocumentId).ToList();
        var checkedOut = outcomes.Where(o => o.State == RelocationState.CheckedOut && Waits(o)).Select(o => o.DocumentId).ToList();
        var releaseExpired = outcomes.Where(o => o.ReleaseExpired).Select(o => o.DocumentId).ToList();
        if (releaseExpired.Count > 0)
        {
            _logger.LogWarning(
                "[SECURE-CHILD-RECONCILE] make-secure-relocations: the F3 release of {Count} document(s) expired unused; they "
                + "stay held until a new F3 re-file: {Documents}", releaseExpired.Count, string.Join(", ", releaseExpired));
        }

        _logger.Log(
            files.Incomplete == 0 ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-RECONCILE] make-secure-relocations mode={Mode} ledgerOwing={LedgerOwing} stranded={Stranded} " +
            "examined={Examined} moved={Moved} incomplete={Incomplete} heldIsolated={HeldIsolated} checkedOut={CheckedOut} " +
            "unresolvable={Unresolvable} passComplete={PassComplete}",
            mode, owing.Count, stranded.Count, files.Examined, files.Moved, files.Incomplete, held.Count, checkedOut.Count,
            files.Unresolvable, passComplete);
        return new MakeSecureRelocationRun(mode, owing.Count, stranded.Count, files, passComplete, null)
        {
            Taken = outcomes.Where(o => DocumentRelocationBatchResult.IsSettled(o)
                                        || o.State is RelocationState.HeldIsolated or RelocationState.CheckedOut
                                        || terminal.ContainsKey(o.DocumentId))
                .Select(o => o.DocumentId).ToHashSet(),
            ReleaseExpired = releaseExpired,
            Terminal = terminal.Count,
            TerminalNew = terminalNew,
            HeldIsolated = held,
            CheckedOut = checkedOut,
        };

        static bool Waits(DocumentRelocationOutcome o)
            => o.State is RelocationState.HeldIsolated or RelocationState.CheckedOut && o.Pending.Count == 0
               && o.MovedAlong.All(DocumentRelocationBatchResult.IsSettled);
    }

    /// <summary>What the re-file pass did in one run (#2159). Lists hold document ids (at most <see cref="MaxSampledRefileIds"/> each).</summary>
    internal sealed record RefileRelocationRun(string Mode)
    {
        public DateTimeOffset? Since { get; init; }
        public int Listed { get; init; }

        /// <summary>A3: documents listed because the matter, project or work assignment they are filed under changed.</summary>
        public int ListedUnderChangedRecords { get; init; }
        public int RetriedRows { get; init; }

        /// <summary>B2: the documents this run derived (at most the examination budget).</summary>
        public int Examined { get; set; }

        /// <summary>B2: every document of the window was examined; the cursor is reset.</summary>
        public bool WindowDrained { get; set; }

        /// <summary>B2/B6: the watermark did not move (the window did not drain, the carry overflowed, report-only, a failed listing).</summary>
        public bool WatermarkHeld { get; set; }
        public int InPlace { get; set; }
        public int SkippedForMakeSecure { get; init; }
        public List<Guid> Moved { get; } = new();
        public List<Guid> WouldMove { get; } = new();
        public List<Guid> StillOwed { get; } = new();
        public List<Guid> HeldIsolated { get; } = new();

        /// <summary>B4: checked out — left alone; reached again when the check-in modifies the row. Not carried, not failed.</summary>
        public List<Guid> CheckedOut { get; } = new();

        /// <summary>B5: permanent failures this run (too large, a full ledger): not carried, not failed.</summary>
        public int Terminal { get; set; }

        /// <summary>B5: the terminal documents not reported before on this instance, with their cause (reported once).</summary>
        public IReadOnlyList<TerminalDocument> TerminalNew { get; set; } = [];
        public List<Guid> UnfiledHomeUnknown { get; } = new();
        public List<Guid> SourceUnverified { get; } = new();
        public List<Guid> FileMissing { get; } = new();
        public List<Guid> Undecidable { get; } = new();

        /// <summary>B6: undecidable for <see cref="MaxUndecidableRuns"/> consecutive runs — reported once and no longer carried.</summary>
        public List<Guid> UndecidableDropped { get; } = new();
        public List<Guid> Failed { get; } = new();
        public List<Guid> Deferred { get; } = new();

        /// <summary>#2159 slice 2c: documents whose F3 release had expired, unused, and was dropped — still held.</summary>
        public List<Guid> ReleaseExpired { get; } = new();
        public int Carried { get; set; }
        public bool CapReached { get; set; }
        public string? Failure { get; init; }

        /// <summary>
        /// Not a finished run: the listing failed, a move failed, a move still owes a step, or a document could not be decided
        /// (B6). A held or checked-out document is not unfinished work (B7, B4): no run of this pass can release it.
        /// </summary>
        public bool Incomplete => Failure is not null || Failed.Count > 0 || StillOwed.Count > 0 || Undecidable.Count > 0;
    }

    /// <summary>#2159 B5: a terminal document and its cause (<see cref="DocumentRelocationOutcome.TerminalCause"/>).</summary>
    internal sealed record TerminalDocument(Guid Id, string Cause);

    /// <summary>
    /// #2159 B5: the terminal documents among <paramref name="seen"/> not reported before on this instance (reported once,
    /// logged as an error for an administrator); a seen document that is no longer terminal may be reported again later.
    /// </summary>
    private List<TerminalDocument> ReportTerminal(IEnumerable<Guid> seen, IReadOnlyDictionary<Guid, string> terminal)
    {
        var fresh = new List<TerminalDocument>();
        lock (_cursorGate)
        {
            foreach (var id in seen.Distinct())
            {
                if (!terminal.TryGetValue(id, out var cause))
                    _terminalReported.Remove(id);
                else if (_terminalReported.Add(id))
                    fresh.Add(new TerminalDocument(id, cause));
            }
        }

        foreach (var t in fresh)
        {
            _logger.LogError(
                "[SECURE-CHILD-RECONCILE] document {DocumentId} cannot be relocated by any repeat run ({Cause}); it is reported "
                + "once and no longer retried — an administrator must act (too-large: #2176; ledger-full or ledger-unreadable: "
                + "settle or repair its sprk_relocationpending).", t.Id, t.Cause);
        }

        return fresh;
    }

    /// <summary>How many document ids each list of the re-file report carries (the counts are complete).</summary>
    internal const int MaxSampledRefileIds = 50;

    /// <summary>
    /// #2159 — the RE-FILE pass: every <c>sprk_document</c> modified since this pass's watermark (any owner, the BFF's writes
    /// and every write outside it), with the rows earlier runs carried, whose pointer's drive is NOT among its derived
    /// containers (<see cref="RecordContainerResolver.DeriveDocumentContainersAsync"/>) is relocated through the ONE
    /// <see cref="DocumentContainerRelocator"/> with <see cref="RelocationPurpose.Refile"/>: only the row's BFF-bound item,
    /// never out of isolation (the relocator's widening guard, which holds for every purpose, the backstop's too), and
    /// never an unfiled document whose home is unknown. A document the Make Secure backstop handled this run is skipped.
    /// </summary>
    /// <remarks>
    /// <para>The window is the documents modified since the watermark, with those filed directly under a matter, project or
    /// work assignment modified since then (A3, the #1943 pattern). It is walked in id order after a per-instance cursor; a
    /// run examines (derives) at most <see cref="RefileExaminationBudgetConfigKey"/> documents, stops
    /// <see cref="RefileTimeSlice"/> after the pass started, and makes at most <see cref="MaxRelocationsPerRunConfigKey"/>
    /// relocator calls (the backstop's setting, its own budget). Where it stops, the cursor holds its place and the
    /// watermark is held until the window drains; then the watermark moves to the start of the run that began the window
    /// (B2), so a row modified while the window was walked is listed again. The carried rows are examined after the window:
    /// a failed move (an unreadable owner, a held lock, a copy fault) and an undecidable derivation, which fails the run and
    /// is dropped after <see cref="MaxUndecidableRuns"/> consecutive runs (B6). At most <see cref="MaxCarriedRows"/>; beyond
    /// that the watermark stays. A cancelled pass keeps its cursor and carry. Its own watermark, moved only in a run that
    /// wrote — so a fault here never holds the secure pass's window (#1378). Every step is keyed on observed state (ADR-036
    /// A1 rule 3): a moved document derives in place on the next run. Never throws (cancellation aside).</para>
    /// <para><b>Known limit.</b> The watermark, cursor and carried rows are per instance. A new instance looks back
    /// <see cref="InitialRecentChangesLookback"/>; a document re-filed earlier whose move never completed (it was carried by a
    /// previous instance) is reached again only when the row next changes, or by the operator-run
    /// <see cref="DocumentContainerMigrationJob"/>, which is the repair (#2159 B3: it accepts the BFF-bound-item proof). The
    /// workaround without it: any write to the row changes its <c>modifiedon</c> and puts it back in the window. The catch-up
    /// does not walk documents: a full walk of every document's derivation is not a two-minute net's cost.</para>
    /// </remarks>
    private async Task<RefileRelocationRun> RelocateRefiledDocumentsAsync(
        IServiceProvider services, IGenericEntityService dataverse, bool writes, DateTimeOffset startedAt,
        IReadOnlySet<Guid> handledByMakeSecure, Guid runId, CancellationToken ct)
    {
        var passStart = _timeProvider.GetTimestamp();
        var mode = writes ? ModeWrite : ModeReportOnly;
        var relocator = services.GetService<DocumentContainerRelocator>();
        var resolver = services.GetService<RecordContainerResolver>();
        if (relocator is null || resolver is null)
            return new RefileRelocationRun("unavailable");

        var cap = int.TryParse(_configuration[MaxRelocationsPerRunConfigKey], out var configured) && configured > 0
            ? configured
            : DefaultMaxRelocationsPerRun;
        var budget = int.TryParse(_configuration[RefileExaminationBudgetConfigKey], out var configuredBudget) && configuredBudget > 0
            ? configuredBudget
            : DefaultRefileExaminationBudget;

        DateTimeOffset since;
        List<Guid> carried;
        Guid? after;
        DateTimeOffset windowStartedAt;
        Dictionary<Guid, int> undecidableRuns;
        lock (_cursorGate)
        {
            since = _refileWatermark ?? startedAt - InitialRecentChangesLookback;
            // F1-a: the ordinary pass's hand-offs are carried rows too (read by id whatever their modifiedon).
            carried = _refilePending.Union(_ordinaryHandOffs).OrderBy(id => id).ToList();
            after = _refileCursor;
            // B2: the window a cursor walks started with the run that found no cursor; the watermark moves to THAT start
            // when the window drains, so a row modified while the window was being walked is listed again.
            windowStartedAt = after is null ? startedAt : _refileWindowStartedAt ?? startedAt;
            undecidableRuns = new Dictionary<Guid, int>(_refileUndecidableRuns);
        }

        Dictionary<Guid, string> candidates;
        HashSet<Guid> window;
        int listed, underChangedRecords;
        try
        {
            var query = new QueryExpression("sprk_document") { ColumnSet = new ColumnSet("sprk_graphdriveid"), NoLock = true };
            query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
            query.Criteria.AddCondition("sprk_graphdriveid", ConditionOperator.NotNull);
            candidates = new Dictionary<Guid, string>();
            foreach (var row in await ReadAllAsync(dataverse, query, ct).ConfigureAwait(false))
                AddCandidate(candidates, row);
            listed = candidates.Count;

            // A3 (the #1943 pattern): a matter, project or work assignment changed in the window can change what the
            // documents filed under it derive to without touching their own modifiedon — they are in the window too.
            foreach (var row in await ListDocumentsUnderChangedRecordsAsync(dataverse, since, ct).ConfigureAwait(false))
                AddCandidate(candidates, row);
            underChangedRecords = candidates.Count - listed;
            window = candidates.Keys.ToHashSet();

            // The rows earlier runs carried, read again by id whatever their modifiedon; a row gone, or without a file, drops out.
            var retry = carried.Where(id => !candidates.ContainsKey(id)).ToList();
            if (retry.Count > 0)
            {
                var again = new QueryExpression("sprk_document") { ColumnSet = new ColumnSet("sprk_graphdriveid"), NoLock = true };
                again.Criteria.AddCondition("sprk_documentid", ConditionOperator.In, retry.Select(id => (object)id).ToArray());
                foreach (var row in await ReadAllAsync(dataverse, again, ct).ConfigureAwait(false))
                    AddCandidate(candidates, row);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] refile run={RunId}: the re-filed documents since {Since:o} could not " +
                "be listed; that window is looked at again next run.", runId, since);
            return new RefileRelocationRun(mode)
            {
                Since = since,
                Failure = "the re-filed documents could not be listed",
                Carried = carried.Count,
                WatermarkHeld = true,
            };
        }

        // B2: the window in id order after its cursor, then the carried rows it does not hold ahead of the cursor (failed or
        // undecidable earlier, or in the window behind the cursor). A deferred window row needs no carry: the cursor stops
        // before it and the watermark is held until the window drains.
        var windowAhead = window.Where(id => after is not { } a || id.CompareTo(a) > 0).OrderBy(id => id).ToList();
        var ahead = windowAhead.ToHashSet();
        var carriedSet = carried.ToHashSet();
        var carriedOrder = candidates.Keys.Where(id => carriedSet.Contains(id) && !ahead.Contains(id)).OrderBy(id => id).ToList();
        var pendingCarried = carriedOrder.ToHashSet();

        var run = new RefileRelocationRun(mode)
        {
            Since = since,
            Listed = listed,
            ListedUnderChangedRecords = underChangedRecords,
            RetriedRows = candidates.Count - window.Count,
            SkippedForMakeSecure = candidates.Keys.Count(handledByMakeSecure.Contains),
        };
        var carry = new List<Guid>();
        var terminal = new Dictionary<Guid, string>();
        var terminalSeen = new List<Guid>();
        var undecidableNow = new Dictionary<Guid, int>();
        var inPlace = 0;
        var calls = 0;
        var examined = 0;
        var windowHandled = 0;
        var cursor = after;

        void Handled(Guid id, bool inWindow)
        {
            if (inWindow)
            {
                windowHandled++;
                cursor = id;
            }
            else
            {
                pendingCarried.Remove(id);
            }
        }

        void Undecidable(Guid id)
        {
            // B6: an undecidable row fails the run and is carried; after MaxUndecidableRuns consecutive runs it is dropped
            // (reported once) — a row whose parent is gone for good must not hold the carry for ever.
            run.Undecidable.Add(id);
            var runs = undecidableRuns.GetValueOrDefault(id) + 1;
            if (runs >= MaxUndecidableRuns)
            {
                run.UndecidableDropped.Add(id);
                return;
            }

            undecidableNow[id] = runs;
            carry.Add(id);
        }

        try
        {
            foreach (var (documentId, inWindow) in windowAhead.Select(id => (id, true)).Concat(carriedOrder.Select(id => (id, false))))
            {
                ct.ThrowIfCancellationRequested();

                // The Make Secure backstop already took it this run (settled or held it): not twice.
                if (handledByMakeSecure.Contains(documentId))
                {
                    Handled(documentId, inWindow);
                    continue;
                }

                // B2: the examination budget and the time slice bound the pass; the rest waits behind the cursor.
                if (examined >= budget || _timeProvider.GetElapsedTime(passStart) >= RefileTimeSlice)
                    break;

                examined++;
                var derivation = await resolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
                if (!derivation.Decided)
                {
                    Undecidable(documentId);
                    Handled(documentId, inWindow);
                    continue;
                }

                if (derivation.Allows(candidates[documentId]))
                {
                    inPlace++;
                    Handled(documentId, inWindow);
                    continue;
                }

                if (calls >= cap)
                {
                    run.Deferred.Add(documentId);
                    run.CapReached = true;
                    break;
                }

                calls++;
                DocumentRelocationOutcome outcome;
                try
                {
                    outcome = await relocator.RelocateIfMisplacedAsync(documentId, writes, RelocationPurpose.Refile, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] refile run={RunId}: document {DocumentId} faulted.", runId, documentId);
                    outcome = new DocumentRelocationOutcome(
                        documentId, RelocationState.Failed, null, null, null, null, $"faulted ({ex.GetType().Name})");
                }

                _logger.Log(
                    outcome.State is RelocationState.Failed or RelocationState.Undecidable ? LogLevel.Warning : LogLevel.Information,
                    "[SECURE-CHILD-RECONCILE] refile run={RunId} mode={Mode} document={DocumentId} state={State} " +
                    "from={SourceDrive} to={TargetDrive}: {Detail}",
                    runId, mode, documentId, outcome.State, outcome.SourceDrive, outcome.TargetDrive, outcome.Detail);
                if (outcome.ReleaseExpired)
                {
                    _logger.LogWarning(
                        "[SECURE-CHILD-RECONCILE] refile run={RunId}: the F3 release of document {DocumentId} expired unused; it "
                        + "stays held until a new F3 re-file.", runId, documentId);
                    run.ReleaseExpired.Add(documentId);
                }

                switch (outcome.State)
                {
                    case RelocationState.Relocated or RelocationState.RelocatedSourceKeptForOtherRecords:
                        run.Moved.Add(documentId);
                        break;
                    case RelocationState.RelocationPending:
                        // Re-pointed; its ledger owes a step, which the Make Secure backstop lists and settles every run.
                        run.Moved.Add(documentId);
                        run.StillOwed.Add(documentId);
                        break;
                    case RelocationState.WouldRelocate:
                        run.WouldMove.Add(documentId);
                        break;
                    case RelocationState.InPlace or RelocationState.NoFile:
                        inPlace++;
                        break;
                    case RelocationState.HeldIsolated:
                        run.HeldIsolated.Add(documentId);
                        break;
                    case RelocationState.CheckedOut:
                        // B4: left alone while its holder edits it; the check-in modifies the row, so the window reaches it again.
                        run.CheckedOut.Add(documentId);
                        break;
                    case RelocationState.UnfiledHomeUnknown:
                        run.UnfiledHomeUnknown.Add(documentId);
                        break;
                    case RelocationState.SourceUnverified:
                        run.SourceUnverified.Add(documentId);
                        break;
                    case RelocationState.FileMissing:
                        run.FileMissing.Add(documentId);
                        break;
                    case RelocationState.Undecidable:
                        Undecidable(documentId);
                        break;
                    case RelocationState.Failed when outcome.TerminalCause is { } cause:
                        // B5: a permanent failure — reported once, never carried, never failing a run.
                        terminal[documentId] = cause;
                        break;
                    default:
                        run.Failed.Add(documentId);
                        carry.Add(documentId);
                        break;
                }

                terminalSeen.Add(documentId);

                Handled(documentId, inWindow);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // B2: a cancelled pass keeps its place — the cursor, the carry (what it carried and had not reached, and what it
            // carried this run) and the undecidable counts — and holds the watermark.
            lock (_cursorGate)
            {
                SaveRefileState(windowDone: false, moveWatermark: false);
            }

            throw;
        }

        var drained = windowHandled == windowAhead.Count;
        bool watermarkMoved;
        lock (_cursorGate)
        {
            var overflow = carry.Count + pendingCarried.Count > MaxCarriedRows;
            watermarkMoved = drained && writes && !overflow;
            SaveRefileState(drained, watermarkMoved);
            run.Carried = _refilePending.Count;
        }

        run.Examined = examined;
        run.InPlace = inPlace;
        run.Terminal = terminal.Count;
        run.TerminalNew = ReportTerminal(terminalSeen, terminal);
        run.WindowDrained = drained;
        run.WatermarkHeld = !watermarkMoved;
        var result = run;
        _logger.Log(
            result.Incomplete ? LogLevel.Warning : LogLevel.Information,
            "[SECURE-CHILD-RECONCILE] refile-relocations run={RunId} mode={Mode} since={Since:o} listed={Listed} " +
            "underChangedRecords={UnderChangedRecords} examined={Examined} windowDrained={WindowDrained} " +
            "watermarkHeld={WatermarkHeld} inPlace={InPlace} moved={Moved} wouldMove={WouldMove} heldIsolated={HeldIsolated} " +
            "checkedOut={CheckedOut} unfiledHomeUnknown={UnfiledHomeUnknown} sourceUnverified={SourceUnverified} " +
            "undecidable={Undecidable} undecidableDropped={UndecidableDropped} failed={Failed} deferred={Deferred} carried={Carried}",
            runId, mode, since, result.Listed, result.ListedUnderChangedRecords, result.Examined, result.WindowDrained,
            result.WatermarkHeld, result.InPlace, result.Moved.Count, result.WouldMove.Count, result.HeldIsolated.Count,
            result.CheckedOut.Count, result.UnfiledHomeUnknown.Count, result.SourceUnverified.Count, result.Undecidable.Count,
            result.UndecidableDropped.Count, result.Failed.Count, result.Deferred.Count, result.Carried);
        return result;

        // Under _cursorGate.
        void SaveRefileState(bool windowDone, bool moveWatermark)
        {
            if (moveWatermark)
                _refileWatermark = windowStartedAt - RecentChangesOverlap;
            // F1-a: a hand-off this pass did not reach stays a hand-off (never truncated); one it reached is dropped (a
            // failure is in `carry`, like any carried row's); one without a file, or not read back, has nothing to move.
            _ordinaryHandOffs.IntersectWith(pendingCarried);
            _refilePending.Clear();
            _refilePending.UnionWith(carry.Concat(pendingCarried.Where(id => !_ordinaryHandOffs.Contains(id))).Take(MaxCarriedRows));
            _refileCursor = windowDone ? null : cursor;
            _refileWindowStartedAt = windowDone ? null : windowStartedAt;
            _refileUndecidableRuns.Clear();
            foreach (var (id, runs) in undecidableNow)
                _refileUndecidableRuns[id] = runs;
            foreach (var id in pendingCarried.Where(undecidableRuns.ContainsKey))
                _refileUndecidableRuns[id] = undecidableRuns[id];
        }

        static void AddCandidate(Dictionary<Guid, string> into, Entity row)
        {
            var drive = row.GetAttributeValue<string>("sprk_graphdriveid")?.Trim();
            if (row.Id != Guid.Empty && !string.IsNullOrEmpty(drive))
                into[row.Id] = drive;
        }
    }

    /// <summary>
    /// The ISOLATED documents (owned by the Secure Record owner team) whose pointer still names a business unit's shared
    /// container. An environment with no Secure Record owner team, or no business-unit container, has none.
    /// </summary>
    private async Task<HashSet<Guid>> ListStrandedIsolatedDocumentsAsync(IGenericEntityService dataverse, CancellationToken ct)
    {
        var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(dataverse, _configuration, ct)
            .ConfigureAwait(false);
        if (team.Refusal is { } refusal)
            throw new InvalidOperationException(refusal);
        if (team.TeamId is not { } secureTeamId)
            return [];

        var units = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("sprk_containerid"), NoLock = true };
        units.Criteria.AddCondition("sprk_containerid", ConditionOperator.NotNull);
        var shared = (await ReadAllAsync(dataverse, units, ct).ConfigureAwait(false))
            .Select(u => u.GetAttributeValue<string>("sprk_containerid")?.Trim())
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<object>()
            .ToArray();
        if (shared.Length == 0)
            return [];

        var filter = new FilterExpression(LogicalOperator.And);
        filter.AddCondition("owningteam", ConditionOperator.Equal, secureTeamId);
        filter.AddCondition("sprk_graphdriveid", ConditionOperator.In, shared);
        return await ListDocumentsAsync(dataverse, filter, ct).ConfigureAwait(false);
    }

    private static FilterExpression NotNull(string column)
    {
        var filter = new FilterExpression(LogicalOperator.And);
        filter.AddCondition(column, ConditionOperator.NotNull);
        return filter;
    }

    /// <summary>The records whose own change re-derives the documents filed directly under them (#2159 A3), with the lookup.</summary>
    private static readonly (string Table, string DocumentLookup)[] RefileParentRecords =
    [
        ("sprk_matter", "sprk_matter"),
        ("sprk_project", "sprk_project"),
        ("sprk_workassignment", "sprk_workassignment"),
    ];

    /// <summary>How many ids one <c>In</c> condition carries.</summary>
    private const int InConditionChunk = 500;

    /// <summary>
    /// #2159 A3 (the #1943 pattern): the documents (with a file) filed directly under a matter, project or work assignment
    /// whose OWN <c>modifiedon</c> is at or after <paramref name="since"/>. A record re-filed or made secure by a direct
    /// write changes no document row, yet what its documents derive to can change.
    /// </summary>
    private static async Task<List<Entity>> ListDocumentsUnderChangedRecordsAsync(
        IGenericEntityService dataverse, DateTimeOffset since, CancellationToken ct)
    {
        var rows = new List<Entity>();
        foreach (var (table, lookup) in RefileParentRecords)
        {
            var changed = new QueryExpression(table) { ColumnSet = new ColumnSet($"{table}id"), NoLock = true };
            changed.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
            var ids = (await ReadAllAsync(dataverse, changed, ct).ConfigureAwait(false))
                .Select(e => e.Id).Where(id => id != Guid.Empty).Distinct().ToList();
            foreach (var chunk in ids.Chunk(InConditionChunk))
            {
                var documents = new QueryExpression("sprk_document") { ColumnSet = new ColumnSet("sprk_graphdriveid"), NoLock = true };
                documents.Criteria.AddCondition(lookup, ConditionOperator.In, chunk.Select(id => (object)id).ToArray());
                documents.Criteria.AddCondition("sprk_graphdriveid", ConditionOperator.NotNull);
                rows.AddRange(await ReadAllAsync(dataverse, documents, ct).ConfigureAwait(false));
            }
        }

        return rows;
    }

    private static async Task<HashSet<Guid>> ListDocumentsAsync(
        IGenericEntityService dataverse, FilterExpression filter, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_document") { ColumnSet = new ColumnSet("sprk_documentid"), NoLock = true };
        query.Criteria.AddFilter(filter);
        return (await ReadAllAsync(dataverse, query, ct).ConfigureAwait(false)).Select(e => e.Id).Where(id => id != Guid.Empty)
            .ToHashSet();
    }

    private static async Task<IReadOnlyList<Entity>> ReadAllAsync(
        IGenericEntityService dataverse, QueryExpression query, CancellationToken ct)
    {
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };
        var rows = new List<Entity>();
        for (var page = 1; ; page++)
        {
            if (page > MaxPages)
                throw new InvalidOperationException($"{query.EntityName} still had rows after {MaxPages} pages.");
            var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
                return rows;
            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }
    }

    /// <summary>What the recent-changes pass found (task 147; carried rows task 147 r1).</summary>
    private sealed record RecentChanges(
        int RowsChanged, IReadOnlyList<(string Table, Guid Id)> Roots, IReadOnlyList<string> Undetermined, string? Failure)
    {
        /// <summary>The undetermined rows as (table, id), to be carried to the next run.</summary>
        public IReadOnlyList<(string Table, Guid Id)> UndeterminedRows { get; init; } = Array.Empty<(string, Guid)>();

        /// <summary>How many rows carried from earlier runs were read again.</summary>
        public int RetriedRows { get; init; }

        /// <summary>More undetermined rows than <see cref="MaxCarriedRows"/>: the watermark must not move.</summary>
        public bool PendingOverflow => UndeterminedRows.Count > MaxCarriedRows;
    }

    private static RecentChanges FailedRecentChanges(string failure) =>
        new(0, Array.Empty<(string, Guid)>(), Array.Empty<string>(), failure);

    /// <summary>
    /// Task 147: lists the child rows of every lineage table modified since <paramref name="since"/> that are NOT already
    /// owned by the Secure Record owner team. Only those rows can need a move into isolation. An isolated row's shares are
    /// kept in line by task 149's two-minute job, and that job reports one that has left every secure record. The pass
    /// then returns the secure records those rows sit under. A listing or walk that cannot complete returns a Failure and
    /// no roots. It never throws: the sweep window still runs.
    /// </summary>
    /// <remarks>
    /// <para><b>Every child table, not only the client writers' tables.</b> A row written outside the product can belong
    /// to any table the lineage map knows: agreements, budgets, spend signals and so on. Each query is one filtered,
    /// paged read, so the cost is one query per table per run.</para>
    /// <para>Past the page ceiling, the listing fails rather than reconcile part of a window, and the watermark stays where
    /// it was.</para>
    /// </remarks>
    private async Task<RecentChanges> FindRecentlyChangedRootsAsync(
        IGenericEntityService dataverse, SecureChildShareSynchronizer synchronizer, DateTimeOffset since,
        IReadOnlyList<(string Table, Guid Id)> carriedRows, CancellationToken ct)
    {
        try
        {
            var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return FailedRecentChanges(refusal);
            if (team.TeamId is not { } secureTeamId)
                return new RecentChanges(0, Array.Empty<(string, Guid)>(), Array.Empty<string>(), null); // no record can be secure

            // Each changed row comes back with its lineage lookups and owner, so the upward walk starts from it with no
            // second read of the row.
            var changed = new List<Entity>();
            foreach (var table in SecureChildLineage.Children.Values.OrderBy(t => t.LogicalName, StringComparer.Ordinal))
            {
                var query = new QueryExpression(table.LogicalName)
                {
                    ColumnSet = new ColumnSet(table.Lookups.Keys.Append("owningteam").Append("owninguser").ToArray()),
                    NoLock = true,
                    PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
                };
                query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);

                for (var page = 1; ; page++)
                {
                    if (page > MaxPages)
                        throw new InvalidOperationException(
                            $"{table.LogicalName} still had changed rows to list after {MaxPages} pages; the window is not " +
                            "reconciled in part.");

                    var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                    changed.AddRange(result.Entities
                        .Where(e => e.GetAttributeValue<EntityReference>("owningteam")?.Id != secureTeamId));
                    if (!result.MoreRecords)
                        break;
                    query.PageInfo.PageNumber++;
                    query.PageInfo.PagingCookie = result.PagingCookie;
                }
            }

            var rowsChanged = changed.Count;

            // Task 147 r1: the rows earlier runs could not place, read again by id whatever their modifiedon. A row that is
            // now Secure-team-owned has been placed, and a row that no longer exists is gone; both drop out.
            var listed = changed.Select(e => (e.LogicalName, e.Id)).ToHashSet();
            var retried = 0;
            foreach (var group in carriedRows.Where(r => !listed.Contains(r)).GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase))
            {
                if (!SecureChildLineage.Children.TryGetValue(group.Key, out var table))
                    continue;
                var query = new QueryExpression(table.LogicalName)
                {
                    ColumnSet = new ColumnSet(table.Lookups.Keys.Append("owningteam").Append("owninguser").ToArray()),
                    NoLock = true,
                };
                query.Criteria.AddCondition(table.IdColumn, ConditionOperator.In, group.Select(r => (object)r.Id).ToArray());
                var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                retried += group.Count();
                changed.AddRange(result.Entities
                    .Where(e => e.GetAttributeValue<EntityReference>("owningteam")?.Id != secureTeamId));
            }

            if (changed.Count == 0)
                return new RecentChanges(rowsChanged, Array.Empty<(string, Guid)>(), Array.Empty<string>(), null) { RetriedRows = retried };

            var above = await synchronizer.SecureRootsAboveAsync(changed, ct).ConfigureAwait(false);
            return above.Status switch
            {
                SecureChildShareSyncStatus.Completed => new RecentChanges(rowsChanged, above.Roots, above.Undetermined, null)
                {
                    UndeterminedRows = above.UndeterminedRowRefs,
                    RetriedRows = retried,
                },
                SecureChildShareSyncStatus.NotApplicable => new RecentChanges(
                    rowsChanged, Array.Empty<(string, Guid)>(), Array.Empty<string>(), null) { RetriedRows = retried },
                // Failed (an ambiguous Secure team) or anything else: nothing is decided, and the window stays.
                _ => FailedRecentChanges(above.Detail ?? "the records above the changed related records could not be determined"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] The recently changed related records could not be listed or " +
                "placed since {Since:o}; that window is looked at again next run.", since);
            return FailedRecentChanges("the recently changed related records could not be listed or placed");
        }
    }

    /// <summary>
    /// Every record flagged <c>sprk_issecure = true</c>, as (table, id, key), ordered by table then id. Paged; past the page
    /// ceiling it throws rather than sweep part of a table.
    /// </summary>
    private static async Task<SecureRootListing> ListSecureRootsAsync(
        IGenericEntityService dataverse, CancellationToken ct)
    {
        var roots = new List<(string, Guid, string)>();
        var modifiedOn = new Dictionary<(string Table, Guid Id), DateTime?>();
        foreach (var table in SecureChildLineage.Roots.OrderBy(t => t, StringComparer.Ordinal))
        {
            var query = new QueryExpression(table)
            {
                // #1943: modifiedon too, so the recent-changes pass finds a record made secure by a direct write, which
                // changes no child row — read in this same listing, with no second query per table.
                ColumnSet = new ColumnSet(table + "id", ModifiedOnColumn),
                NoLock = true,
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
            };
            query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Equal, true);

            var ids = new List<Guid>();
            for (var page = 1; ; page++)
            {
                if (page > MaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had secure records to list after {MaxPages} pages; the sweep does not run on part of them.");

                var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                foreach (var entity in result.Entities)
                {
                    ids.Add(entity.Id);
                    // A record listed twice (across pages) keeps its latest modifiedon; one with none keeps none, so it
                    // counts as changed (#1943: never silently dropped from the recent-changes pass).
                    var at = entity.GetAttributeValue<DateTime?>(ModifiedOnColumn);
                    modifiedOn[(table, entity.Id)] = modifiedOn.TryGetValue((table, entity.Id), out var seen)
                        ? seen is null || at is null ? null : (at > seen ? at : seen)
                        : at;
                }

                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }

            roots.AddRange(ids.Distinct().OrderBy(id => id).Select(id => (table, id, $"{table}:{id:D}")));
        }

        return new SecureRootListing(roots, modifiedOn);
    }

    /// <summary>
    /// The secure records in sweep order, and each one's own <c>modifiedon</c> (null when Dataverse returned none) — the
    /// recent-changes pass reconciles every one changed since its window opened (#1943).
    /// </summary>
    private sealed record SecureRootListing(
        List<(string Table, Guid Id, string Key)> Roots, IReadOnlyDictionary<(string Table, Guid Id), DateTime?> ModifiedOn)
    {
        /// <summary>
        /// The secure records whose own <c>modifiedon</c> is at or after <paramref name="since"/>, oldest change first, then
        /// table (ordinal), then id. A record with no <c>modifiedon</c> counts as changed: it is included, first, never
        /// silently left out (fail closed). The order decides which ones a capped run takes (#1943).
        /// </summary>
        public List<(string Table, Guid Id)> ChangedSince(DateTimeOffset since) =>
            Roots
                .Select(r => (r.Table, r.Id, At: ModifiedOn.TryGetValue((r.Table, r.Id), out var at) && at is { } value
                    ? AsUtc(value)
                    : (DateTime?)null))
                .Where(r => r.At is not { } at || at >= since.UtcDateTime)
                .OrderBy(r => r.At.HasValue)
                .ThenBy(r => r.At)
                .ThenBy(r => r.Table, StringComparer.Ordinal)
                .ThenBy(r => r.Id)
                .Select(r => (r.Table, r.Id))
                .ToList();

        // Dataverse answers modifiedon in UTC; a local value is converted, an unspecified one read as UTC.
        private static DateTime AsUtc(DateTime value) =>
            value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    /// <summary>The sweep order: table (ordinal), then id — the order <see cref="ListSecureRootsAsync"/> returns.</summary>
    private static int Compare((string Table, Guid Id) a, (string Table, Guid Id) b)
    {
        var byTable = string.CompareOrdinal(a.Table, b.Table);
        return byTable != 0 ? byTable : a.Id.CompareTo(b.Id);
    }

    // The progress cursor: the last record a run reached while a pass is in progress (null between passes). It lives in this
    // singleton, per instance — the task 143 NoAccessShareReconciliationJob precedent: the scheduler lease runs one tick at a
    // time, a job must not depend on the scheduler's store (ADR-052 §5 / ADR-036 A1 rule 7, WorkloadPlacementGuardTests), and
    // a restart only starts the sweep over, which is harmless because every step of a pass is keyed on observed state. Each
    // run's ResultJson and its progress log lines carry the same position for an operator.
    private readonly object _cursorGate = new();
    private (string Table, Guid Id)? _cursor;

    /// <summary>Round 46 item 2: where the Make Secure file backstop continues (per instance, like the sweep cursor).</summary>
    private Guid? _relocationCursor;

    // #2159: the re-file pass's own window, carried documents and cursor (the same per-instance, in-singleton reasoning as
    // the cursor; its own watermark so its faults never hold the secure pass's window, #1378).
    private DateTimeOffset? _refileWatermark;
    private readonly HashSet<Guid> _refilePending = new();
    private Guid? _refileCursor;

    // #2159 B2/B6: when the window the cursor walks started, and how many consecutive runs each carried row was undecidable.
    private DateTimeOffset? _refileWindowStartedAt;
    private readonly Dictionary<Guid, int> _refileUndecidableRuns = new();

    // #2159 B5: the terminal documents already reported (by either pass) and the backstop's consecutive unreadable-ledger
    // runs, per instance. A document leaves the reported set when an outcome for it is no longer terminal.
    private readonly HashSet<Guid> _terminalReported = new();
    private readonly Dictionary<Guid, int> _unreadableLedgerRuns = new();

    // Task 147: the recent-changes watermark, with the same per-instance, in-singleton reasoning as the cursor. A restart
    // looks back InitialRecentChangesLookback. A change older than that is reached by the full sweep.
    private DateTimeOffset? _recentChangesWatermark;

    // Task 147 r1 (verifier item 4): what a run could not finish, looked at first by the next run on this instance — the
    // records whose pass was incomplete and the changed rows that could not be placed. Same per-instance, in-singleton
    // reasoning as the cursor (ADR-052 §5). A restart drops them; the new instance's catch-up (task 147 r1c) walks every
    // secure record once, so a carried RECORD is reconciled again regardless. A carried ROW sits under no isolated record
    // (flagged-not-isolated: its record's provisioning re-entry completes it, and the catch-up walks that record and
    // reports it incomplete; a missing ancestor: no secure record above it at all) and is named in the run history of
    // every run that carried it.
    private readonly HashSet<(string Table, Guid Id)> _pendingRoots = new();
    private readonly HashSet<(string Table, Guid Id)> _pendingRows = new();

    // UAC task 194: the ordinary-parent-move pass's own window and carried roots (the same per-instance, in-singleton
    // reasoning; its own watermark, so it never holds the secure pass's window, #1378).
    private DateTimeOffset? _ordinaryMovesWatermark;
    private readonly HashSet<(string Table, Guid Id)> _pendingOrdinaryRoots = new();

    // UAC task 194b R4-3 (B4): when each carried root was last attempted, so carried roots run least-recently-attempted
    // first. A stamp is (run number × 2) + 1 for a root walked in that run, and run number × 2 for a root first carried in it
    // without a walk, so within one run an unwalked root ranks before a walked one. Pruned to the carried set each run.
    private long _ordinaryRunNumber;
    private readonly Dictionary<(string Table, Guid Id), long> _ordinaryAttemptStamps = new();

    // UAC task 194b R4-3 (B4): the REPORT-ONLY cursor. A report-only run keeps no carry and lists the same window again, so
    // without it a slice or the root cap would stop every run at the same root and the tail would never be reported. The
    // last recent root a report-only run walked (its change time, table, id); the next report-only run starts after it and
    // wraps around. Cleared once a report-only run reaches every root of its window, and by a writing run (its carry does
    // this work). Per instance, like every window of this job: a restart starts the window from its first root.
    private (DateTime ModifiedOn, string Table, Guid Id)? _ordinaryReportCursor;

    // UAC task 194 F1-a: the documents the ordinary pass handed to the re-file pass that it has not reached yet. The re-file
    // pass reads them with its own carried rows and drops each once it examines it; they are never truncated. The ordinary
    // pass hands off at most the free capacity (MaxCarriedRows minus both sets), so the hand-offs alone never exceed
    // MaxCarriedRows; the re-file carry can still fill to its own cap after them, so both together stay under twice it.
    private readonly HashSet<Guid> _ordinaryHandOffs = new();

    // UAC task 194 F4-a: the ordinary catch-up sweep — per instance, like the secure catch-up. It walks every ordinary root
    // once IN THE CURRENT MODE (a switch of OrdinaryMovesWritesEnabled starts it over), then only the watermark decides.
    private (string Table, Guid Id)? _ordinarySweepCursor;
    private bool _ordinarySweepComplete;
    private bool? _ordinarySweepWrites;

    /// <summary>
    /// Task 147 r1: the most unplaced rows the job carries between runs. Beyond it the watermark does not move, so the whole
    /// window is listed again: nothing is dropped, it is only re-listed more broadly.
    /// </summary>
    internal const int MaxCarriedRows = 1000;

    // Task 147 r1c: the catch-up (see ExecuteAsync) — whether this instance has walked every secure record once since it
    // started, and where its walk resumes. Per instance and in this singleton, deliberately: what it repairs is exactly
    // the loss of the other per-instance state on a start.
    private bool _catchUpComplete;
    private (string Table, Guid Id)? _catchUpCursor;

    // Task 173: the inherited Access Permission pass's own window, carried rows and sweep (the same per-instance, in-singleton
    // reasoning as above). Its own watermark, so a fault in it never holds the secure pass's window (#1378). A restart
    // loses them; the new instance's sweep then covers every row of the four tables once.
    private DateTimeOffset? _accessPermissionWatermark;
    private readonly HashSet<(string Table, Guid Id)> _accessPermissionPending = new();
    private bool _accessPermissionSweepComplete;
    private (string Table, Guid Id)? _accessPermissionSweepCursor;

    // Owner round 122 fix round 1 (K1): both document link columns were found field-secured. Cached for the process once
    // true (this job is a singleton); a "not secured" answer or a failed read is asked again on the next run that needs it,
    // so applying Set-DocumentNarrowingFieldSecurity.ps1 takes effect without a restart. REVERTING it does not: the cached
    // "secured" stands until the BFF restarts (fix round 2; I-19).
    private volatile bool _documentLinkColumnsSecured;

    /// <summary>
    /// Owner round 122 fix round 1 (K1): whether <c>sprk_document</c>'s two link columns
    /// (<see cref="ChildAccessPermissionReconciler.ReleaseLinkColumns"/>) are field-secured, read from attribute metadata by
    /// the same reader as the access record's check (<see cref="SecureRootInheritance.ColumnFieldSecurityAsync"/>). The
    /// reconciler asks only when a release is due. A read fault THROWS (the reconciler then releases nothing this run).
    /// </summary>
    private async Task<bool> DocumentLinkColumnsSecuredAsync(IServiceProvider services, CancellationToken ct)
    {
        if (_documentLinkColumnsSecured)
            return true;

        var webApi = services.GetRequiredService<DataverseWebApiClient>();
        foreach (var column in ChildAccessPermissionReconciler.ReleaseLinkColumns)
        {
            var (secured, _) = await SecureRootInheritance.ColumnFieldSecurityAsync(webApi, "sprk_document", column, ct)
                .ConfigureAwait(false);
            if (!secured)
                return false;
        }

        return _documentLinkColumnsSecured = true;
    }

    /// <summary>Which part of a run a record is reconciled in (task 147 r1c).</summary>
    private enum WorkGroup
    {
        /// <summary>
        /// A record whose related records changed since the watermark, one that itself changed since then (#1943), or one an
        /// earlier run left unfinished.
        /// </summary>
        Recent,

        /// <summary>The once-per-instance walk of every secure record after a start.</summary>
        CatchUp,

        /// <summary>The task 148 sweep window (the backfill), in the sweep's own mode.</summary>
        Sweep,
    }
}
