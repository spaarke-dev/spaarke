using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
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
/// record. The watermark is the run's start time minus <see cref="RecentChangesOverlap"/>. It lives in this singleton, per
/// instance, like the cursor. It moves only past a listing that completed. A changed row the walk cannot place (under a
/// record flagged secure but not isolated, or under a missing ancestor) is listed in <c>recentChanges.undetermined</c>, and
/// the run is not a success.</para>
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
/// (<c>unresolvable</c>) and never moved or deleted.</para>
/// <para><b>The inherited Access Permission (task 173, owner rounds 81/84).</b> Each run also keeps <c>sprk_accesspermission</c>
/// on To Do, Event, Communication and Document equal to the most restrictive value of the records at the top of their
/// filing (<see cref="ChildAccessPermissionReconciler"/>): the rows changed since its own watermark and everything filed
/// under them, the rows it carried, and a capped sweep window until this instance has covered every row once. A parentless
/// row is never written. Its listing, watermark and carried rows are its own, so its faults never hold the secure pass's
/// window (#1378). Reported in <c>ResultJson.accessPermission</c>; a listing fault or a failed write makes the run
/// unsuccessful.</para>
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
    /// Task 173: the per-run cap on the inherited Access Permission SWEEP window (the rows of To Do, Event, Communication and
    /// Document read in order while this instance's sweep has not covered them all).
    /// </summary>
    internal const string MaxAccessPermissionRowsPerRunConfigKey = "SecureChild:Reconciliation:MaxAccessPermissionRowsPerRun";

    internal const int DefaultMaxAccessPermissionRowsPerRun = 1000;

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
        "whose related records changed since the last run are reconciled first (task 147). Report-only unless " +
        "SecureChild:Reconciliation:WritesEnabled is true. Also keeps the Access Permission of To Do, Event, " +
        "Communication and Document equal to their parents' (task 173).";

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

        // The secure records, in a fixed order. A listing that cannot complete decides nothing: ADR-036 A1 rule 4.
        var roots = await ListSecureRootsAsync(dataverse, cancellationToken).ConfigureAwait(false);

        // Task 147, the RECENT-CHANGES pass: the secure records whose children changed since the last run (the watermark).
        // This catches children written outside the product (out-of-the-box forms, quick create, grid edits, imports,
        // flows) and by the client writers still on Xrm.WebApi within one run, rather than only when the capped sweep
        // window next reaches their record. They are reconciled first, by the same pass.
        var startedAt = _timeProvider.GetUtcNow();
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

        var firstRoots = recent.Roots
            .Concat(retriedRoots)
            .Distinct()
            .ToList();
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

        // Round 46 item 2: the Make Secure file backstop, after the children (each document's container derives from its
        // now-secure parents). Its writes follow the recent-changes pass.
        var relocations = await SettleMakeSecureRelocationsAsync(scope.ServiceProvider, dataverse, recentWrites, cancellationToken)
            .ConfigureAwait(false);

        // Task 173 (owner rounds 81/84): the inherited Access Permission of To Do, Event, Communication and Document. Its own
        // listing, watermark and carried rows, so a fault there never holds the secure pass's window (#1378). Its writes
        // follow the recent-changes pass (on unless that pass's emergency stop is set).
        var accessPermission = await ReconcileChildAccessPermissionAsync(
            scope.ServiceProvider, dataverse, recentWrites, startedAt, context.RunId, cancellationToken).ConfigureAwait(false);

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

        var duration = _timeProvider.GetElapsedTime(started);

        // A recently changed row the pass could not place is reported and makes the run unsuccessful, never silently
        // skipped (task 147, ADR-003). Examples are a row under a record flagged secure but not isolated, or under a
        // missing ancestor.
        var success = incompleteRoots.Count == 0 && recent.Failure is null && recent.Undetermined.Count == 0
                      && relocations.Failure is null && (relocations.Files?.Incomplete ?? 0) == 0
                      && accessPermission.Run?.Failure is null && (accessPermission.Run?.Failed ?? 0) == 0;
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
            "recentUndetermined={RecentUndetermined} recentFailure={RecentFailure} attempt={Attempt} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            mode, batch.Count, roots.Count, passComplete, passComplete ? null : lastKey, totals[0], totals[1], totals[2],
            totals[3], totals[4], totals[5], totals[6], totals[7], sharesWritten, mirrorsRevoked, incompleteRoots.Count,
            since, recent.RowsChanged, recent.Roots.Count, recent.Undetermined.Count, recent.Failure,
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
                  (accessPermission.Run is { } ap && (ap.Failure is not null || ap.Failed > 0)
                      ? $", and the inherited Access Permission pass {(ap.Failure ?? $"could not write {ap.Failed} row(s)")}"
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
                // `since`. They were reconciled before the sweep window, and their changes are in the totals above.
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
                    unresolvable = relocations.Files?.Unresolvable ?? 0,
                    counts = relocations.Files?.Counts,
                    incompleteDocuments = relocations.Files?.IncompleteDocuments,
                    unresolvableDocuments = relocations.Files?.UnresolvableDocuments,
                    sourceChangedAfterMove = relocations.Files?.SourceChangedAfterMove ?? 0,
                    versionsTruncated = relocations.Files?.VersionsTruncated ?? 0,
                    sourceKeptForOtherRecords = relocations.Files?.SourceKeptForOtherRecords ?? 0,
                    passComplete = relocations.PassComplete,
                    failure = relocations.Failure,
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
                        problems = apRun.Problems,
                        changes = apRun.Changes.Select(c => new { table = c.Table, id = c.Id, from = c.From, to = c.To }).ToArray(),
                        carriedRows = accessPermission.CarriedRows,
                        tablesWithoutColumn = apRun.TablesWithoutColumn,
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

    /// <summary>What the inherited Access Permission pass did in one run (task 173); <see cref="Run"/> null when it did not run.</summary>
    private sealed record AccessPermissionRunReport(
        string Mode, ChildAccessPermissionRun? Run, DateTimeOffset? Since, int CarriedRows, bool SweepRan, bool SweepComplete);

    /// <summary>
    /// Task 173 — runs <see cref="ChildAccessPermissionReconciler"/> over the window since this instance's own Access
    /// Permission watermark, the rows it carried, and (until this instance has covered every row of the four tables once)
    /// a capped sweep window. The watermark moves only past a pass that listed completely, in a run that wrote; undecided
    /// and failed rows are carried (at most <see cref="MaxCarriedRows"/>; beyond that the watermark stays). Never throws
    /// (cancellation aside). "unavailable" when the host has no <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"/>
    /// (a test composition).
    /// </summary>
    private async Task<AccessPermissionRunReport> ReconcileChildAccessPermissionAsync(
        IServiceProvider services, IGenericEntityService dataverse, bool writes, DateTimeOffset startedAt, Guid runId,
        CancellationToken ct)
    {
        var mode = writes ? ModeWrite : ModeReportOnly;
        var coreAncestors = services.GetService<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>();
        if (coreAncestors is null)
            return new AccessPermissionRunReport("unavailable", null, null, 0, false, false);

        var cap = int.TryParse(_configuration[MaxAccessPermissionRowsPerRunConfigKey], out var configured) && configured > 0
            ? Math.Min(configured, 4999)
            : DefaultMaxAccessPermissionRowsPerRun;

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
            dataverse, (table, token) => coreAncestors.ProbeColumnsAsync(table, token), _logger);
        ChildAccessPermissionRun run;
        try
        {
            run = await reconciler.RunAsync(since, carried, sweepAfter, cap, writes, ct).ConfigureAwait(false);
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
            var overflow = run.Carry.Count > MaxCarriedRows;
            if (run.Failure is null && writes && !overflow)
                _accessPermissionWatermark = startedAt - RecentChangesOverlap;

            _accessPermissionPending.Clear();
            _accessPermissionPending.UnionWith(run.Carry.Take(MaxCarriedRows));

            // The sweep advances only in a run that listed completely and wrote (a report-only run corrected nothing).
            if (sweepAfter is not null && run.Failure is null && writes)
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
            "undetermined={Undetermined} failed={Failed} carried={Carried} sweepRows={SweepRows} sweepComplete={SweepComplete} " +
            "failure={Failure}",
            runId, mode, since, run.RowsChanged, run.Examined, run.AlreadyCorrect, run.Changed, run.WouldChange, run.Parentless,
            run.Undetermined, run.Failed, carriedRows, run.SweepRows, sweepComplete, run.Failure);

        return new AccessPermissionRunReport(mode, run, since, carriedRows, sweepAfter is not null, sweepComplete);
    }

    /// <summary>What the Make Secure file backstop did in one run (round 46 item 2).</summary>
    internal sealed record MakeSecureRelocationRun(
        string Mode, int LedgerOwing, int Stranded, MakeSecureFilesSummary? Files, bool PassComplete, string? Failure);

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

        var files = MakeSecureFilesSummary.From(DocumentRelocationBatchResult.From(outcomes));
        _logger.Log(
            files.Incomplete == 0 ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-RECONCILE] make-secure-relocations mode={Mode} ledgerOwing={LedgerOwing} stranded={Stranded} " +
            "examined={Examined} moved={Moved} incomplete={Incomplete} unresolvable={Unresolvable} passComplete={PassComplete}",
            mode, owing.Count, stranded.Count, files.Examined, files.Moved, files.Incomplete, files.Unresolvable, passComplete);
        return new MakeSecureRelocationRun(mode, owing.Count, stranded.Count, files, passComplete, null);
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
    private static async Task<List<(string Table, Guid Id, string Key)>> ListSecureRootsAsync(
        IGenericEntityService dataverse, CancellationToken ct)
    {
        var roots = new List<(string, Guid, string)>();
        foreach (var table in SecureChildLineage.Roots.OrderBy(t => t, StringComparer.Ordinal))
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(table + "id"),
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
                ids.AddRange(result.Entities.Select(e => e.Id));
                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }

            roots.AddRange(ids.Distinct().OrderBy(id => id).Select(id => (table, id, $"{table}:{id:D}")));
        }

        return roots;
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

    /// <summary>Which part of a run a record is reconciled in (task 147 r1c).</summary>
    private enum WorkGroup
    {
        /// <summary>A record whose related records changed since the watermark, or one an earlier run left unfinished.</summary>
        Recent,

        /// <summary>The once-per-instance walk of every secure record after a start.</summary>
        CatchUp,

        /// <summary>The task 148 sweep window (the backfill), in the sweep's own mode.</summary>
        Sweep,
    }
}
