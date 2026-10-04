using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;

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
/// <para><b>Ships disabled AND report-only</b> (the <c>ExternalAccessReconciliationJob</c> posture). The registration is
/// <c>AddScheduledJob&lt;&gt;(cron, enabled: false)</c>, and writes need <see cref="WritesEnabledConfigKey"/> = <c>true</c>:
/// an absent, empty or unparseable value writes nothing, so every way of getting the configuration wrong lands on "report".
/// A manual admin trigger of the disabled job reports. In report-only mode every change the run WOULD make is logged per row
/// with the row's current owner, and summarized in the run's <c>ResultJson</c>.</para>
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
/// <para><b>Standing schedule (task 147): pending the owner.</b> The job still ships disabled and report-only. Turning it on
/// as the L4 net makes its interval the window in which a non-product child of a secure record sits in its creator's
/// business unit. Task 147's escalation puts that interval to the owner (notes/task-147-client-child-writers.md §6).</para>
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

    /// <summary>Every 15 minutes once enabled; task 147 owns the standing cadence. Registered disabled.</summary>
    internal const string DefaultCronSchedule = "*/15 * * * *";

    /// <summary>The owner switch that turns report-only into writes. Absent, empty or unparseable = report-only.</summary>
    internal const string WritesEnabledConfigKey = "SecureChild:Reconciliation:WritesEnabled";

    /// <summary>The per-run root cap.</summary>
    internal const string MaxRootsPerRunConfigKey = "SecureChild:Reconciliation:MaxRootsPerRun";

    internal const int DefaultMaxRootsPerRun = 50;

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
        "SecureChild:Reconciliation:WritesEnabled is true.";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var writes = bool.TryParse(_configuration[WritesEnabledConfigKey], out var enabled) && enabled;
        var mode = writes ? SecureChildReconcileMode.Apply : SecureChildReconcileMode.ReportOnly;
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
        lock (_cursorGate)
            since = _recentChangesWatermark ?? startedAt - InitialRecentChangesLookback;
        var recent = await FindRecentlyChangedRootsAsync(dataverse, synchronizer, since, cancellationToken)
            .ConfigureAwait(false);

        // Continue after the last record the previous run reached (by ORDER, not position: records secured or unsecured
        // in between never make the window skip one). Past the end — the previous run finished the pass — start over.
        (string Table, Guid Id)? resumeAfter;
        lock (_cursorGate)
            resumeAfter = _cursor;
        var start = resumeAfter is { } after ? roots.FindIndex(r => Compare((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var batch = roots.Skip(start).Take(cap).ToList();

        // The recent-changes roots go first. A record in both lists is reconciled once, and it still counts toward the
        // sweep window's position.
        var recentKeys = recent.Roots.Select(r => (r.Table, r.Id)).ToHashSet();
        var work = recent.Roots
            .Select(r => (r.Table, r.Id, Key: $"{r.Table}:{r.Id:D}", Position: (int?)null))
            .Concat(batch.Select((b, i) => (b.Table, b.Id, b.Key, Position: (int?)(start + i + 1)))
                .Where(b => !recentKeys.Contains((b.Table, b.Id))))
            .ToList();

        var totals = new int[8];
        var incompleteRoots = new List<string>();
        var sampled = new List<object>();
        var changesTotal = 0;
        var sharesWritten = 0;
        var mirrorsRevoked = 0;

        foreach (var (table, id, key, position) in work)
        {
            if (position is { } at)
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] progress run={RunId} mode={Mode} root={Root} position={Position}/{Total} " +
                    "(of {All} secure records).",
                    context.RunId, mode, key, at, start + batch.Count, roots.Count);
            }
            else
            {
                _logger.LogInformation(
                    "[SECURE-CHILD-RECONCILE] recent-changes run={RunId} mode={Mode} root={Root}: a related record changed " +
                    "since {Since:o}.",
                    context.RunId, mode, key, since);
            }

            var report = await reconciler.ReconcileAsync(table, id, mode, SecureChildPassTrigger.Sweep, cancellationToken)
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
                incompleteRoots.Add($"{key}: {report.Status}{(report.Detail is null ? "" : " — " + report.Detail)}");

            foreach (var change in report.Changes)
            {
                // Counted in full (task 148 r2): the listed sample is capped, the total is not — so a reader can tell
                // when the list is incomplete (changesTotal > changesListed).
                changesTotal++;
                if (sampled.Count >= MaxSampledChanges)
                    continue;
                sampled.Add(new
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

        var lastKey = batch.Count > 0 ? batch[^1].Key : null;
        var passComplete = start + batch.Count >= roots.Count;
        lock (_cursorGate)
        {
            _cursor = passComplete || batch.Count == 0 ? null : (batch[^1].Table, batch[^1].Id);

            // The watermark moves only past a listing that COMPLETED, in a run that WROTE. After a failed listing, the
            // next run looks at the same window again. A report-only run corrects nothing, so it must not move the
            // watermark either: the first run with writes on then still sees the changes the report-only runs listed
            // (back to the initial lookback). A record whose pass was incomplete is revisited by the sweep, as every
            // secure record is.
            if (recent.Failure is null && writes)
                _recentChangesWatermark = startedAt - RecentChangesOverlap;
        }

        var duration = _timeProvider.GetElapsedTime(started);

        // A recently changed row the pass could not place is reported and makes the run unsuccessful, never silently
        // skipped (task 147, ADR-003). Examples are a row under a record flagged secure but not isolated, or under a
        // missing ancestor.
        var success = incompleteRoots.Count == 0 && recent.Failure is null && recent.Undetermined.Count == 0;
        var recentOnly = work.Count - batch.Count(b => !recentKeys.Contains((b.Table, b.Id)));

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
                : $"{incompleteRoots.Count} secure record(s) not fully reconciled and {recent.Undetermined.Count} recently " +
                  "changed related record(s) not placed; the next run revisits them: " + string.Join("; ", problems),
            ProcessedItems: batch.Count + recentOnly,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                mode = writes ? ModeWrite : ModeReportOnly,
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
                    since = since.UtcDateTime,
                    rowsChanged = recent.RowsChanged,
                    rootsFound = recent.Roots.Count,
                    roots = recent.Roots.Take(50).Select(r => $"{r.Table}:{r.Id:D}").ToArray(),
                    undetermined = recent.Undetermined.Take(50).ToArray(),
                    failure = recent.Failure,
                },
                attempt = context.Attempt,
            }, ResultJsonOptions));
    }

    /// <summary>What the recent-changes pass found (task 147).</summary>
    private sealed record RecentChanges(
        int RowsChanged, IReadOnlyList<(string Table, Guid Id)> Roots, IReadOnlyList<string> Undetermined, string? Failure);

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
        IGenericEntityService dataverse, SecureChildShareSynchronizer synchronizer, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var team = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(dataverse, _configuration, ct)
                .ConfigureAwait(false);
            if (team.Refusal is { } refusal)
                return new RecentChanges(0, Array.Empty<(string, Guid)>(), Array.Empty<string>(), refusal);
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

            if (changed.Count == 0)
                return new RecentChanges(0, Array.Empty<(string, Guid)>(), Array.Empty<string>(), null);

            var above = await synchronizer.SecureRootsAboveAsync(changed, ct).ConfigureAwait(false);
            return above.Status switch
            {
                SecureChildShareSyncStatus.Completed => new RecentChanges(changed.Count, above.Roots, above.Undetermined, null),
                SecureChildShareSyncStatus.NotApplicable => new RecentChanges(changed.Count, Array.Empty<(string, Guid)>(), Array.Empty<string>(), null),
                _ => new RecentChanges(changed.Count, Array.Empty<(string, Guid)>(), Array.Empty<string>(),
                    above.Detail ?? "the records above the changed related records could not be determined"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[SECURE-CHILD-RECONCILE] The recently changed related records could not be listed or " +
                "placed since {Since:o}; that window is looked at again next run.", since);
            return new RecentChanges(0, Array.Empty<(string, Guid)>(), Array.Empty<string>(),
                "the recently changed related records could not be listed or placed");
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

    // Task 147: the recent-changes watermark, with the same per-instance, in-singleton reasoning as the cursor. A restart
    // looks back InitialRecentChangesLookback. A change older than that is reached by the full sweep.
    private DateTimeOffset? _recentChangesWatermark;
}
