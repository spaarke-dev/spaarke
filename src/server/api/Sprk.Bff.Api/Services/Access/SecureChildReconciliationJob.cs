using System.Text.Json;
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
/// with writes on, it is the one-time backfill (operator runbook: SECURE-PROJECT-ENVIRONMENT-SETUP.md §7b, script
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
/// <c>ResultJson</c> records the same position (<c>resumeAfter</c>), and every root is a progress line in the log before it
/// is processed, so an interrupted run's position is visible. Because every step of a pass is keyed on observed state,
/// re-processing a root is harmless: a restart only starts the sweep over.</para>
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

    /// <summary>Planned / applied row changes listed in <c>ResultJson</c> (the complete list is in the per-row log lines).</summary>
    internal const int MaxSampledChanges = 200;

    internal const string ModeReportOnly = "report-only";
    internal const string ModeWrite = "write";

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
        "gives it: owned by the Secure Record owner team, shared with exactly its record's sharees (task 148). Report-only " +
        "unless SecureChild:Reconciliation:WritesEnabled is true.";

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

        // The secure records, in a fixed order. A listing that cannot complete decides nothing: ADR-036 A1 rule 4.
        var roots = await ListSecureRootsAsync(dataverse, cancellationToken).ConfigureAwait(false);

        // Continue after the last record the previous run reached (by ORDER, not position: records secured or unsecured
        // in between never make the window skip one). Past the end — the previous run finished the pass — start over.
        (string Table, Guid Id)? resumeAfter;
        lock (_cursorGate)
            resumeAfter = _cursor;
        var start = resumeAfter is { } after ? roots.FindIndex(r => Compare((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var batch = roots.Skip(start).Take(cap).ToList();

        var totals = new int[7];
        var incompleteRoots = new List<string>();
        var sampled = new List<object>();
        var sharesWritten = 0;
        var mirrorsRevoked = 0;

        for (var i = 0; i < batch.Count; i++)
        {
            var (table, id, key) = batch[i];
            _logger.LogInformation(
                "[SECURE-CHILD-RECONCILE] progress run={RunId} mode={Mode} root={Root} position={Position}/{Total} " +
                "(of {All} secure records).",
                context.RunId, mode, key, start + i + 1, start + batch.Count, roots.Count);

            var report = await reconciler.ReconcileAsync(table, id, mode, unsecuring: false, cancellationToken)
                .ConfigureAwait(false);

            foreach (var t in report.Tables)
            {
                totals[0] += t.Examined; totals[1] += t.AlreadyCorrect; totals[2] += t.Changed; totals[3] += t.WouldChange;
                totals[4] += t.Untouched; totals[5] += t.Refused; totals[6] += t.Failed;
            }

            if (report.Shares is { } s)
                sharesWritten += s.SharesGranted + s.SharesChanged + s.SharesRevoked;
            mirrorsRevoked += report.MirrorSharesRevoked;

            if (!report.IsComplete)
                incompleteRoots.Add($"{key}: {report.Status}{(report.Detail is null ? "" : " — " + report.Detail)}");

            foreach (var change in report.Changes)
            {
                if (sampled.Count >= MaxSampledChanges)
                    break;
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
            _cursor = passComplete || batch.Count == 0 ? null : (batch[^1].Table, batch[^1].Id);
        var duration = _timeProvider.GetElapsedTime(started);
        var success = incompleteRoots.Count == 0;

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            success ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-CHILD-RECONCILE] heartbeat mode={Mode} roots={Roots}/{All} passComplete={PassComplete} " +
            "resumeAfter={ResumeAfter} examined={Examined} alreadyCorrect={AlreadyCorrect} changed={Changed} " +
            "wouldChange={WouldChange} untouched={Untouched} refused={Refused} failed={Failed} sharesWritten={SharesWritten} " +
            "mirrorsRevoked={MirrorsRevoked} incompleteRoots={IncompleteRoots} attempt={Attempt} durationMs={DurationMs} " +
            "trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            mode, batch.Count, roots.Count, passComplete, passComplete ? null : lastKey, totals[0], totals[1], totals[2],
            totals[3], totals[4], totals[5], totals[6], sharesWritten, mirrorsRevoked, incompleteRoots.Count,
            context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: success,
            ErrorMessage: success
                ? null
                : $"{incompleteRoots.Count} secure record(s) not fully reconciled; the next run revisits them: "
                  + string.Join("; ", incompleteRoots.Take(20)),
            ProcessedItems: batch.Count,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                mode = writes ? ModeWrite : ModeReportOnly,
                rootsInRun = batch.Count,
                rootsTotal = roots.Count,
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
                sharesWritten,
                mirrorsRevoked,
                incompleteRoots = incompleteRoots.Take(50).ToArray(),
                changes = sampled,
                attempt = context.Attempt,
            }, ResultJsonOptions));
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
}
