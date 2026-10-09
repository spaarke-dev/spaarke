using System.Text.Json;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 158 (owner round 6; R3/R4: "the background job is only a safety net, running at ≤5 min")
/// — finds every work assignment and project FILED UNDER a secure matter or project and makes it secure through
/// <see cref="SecureRootInheritance"/> (provisioning's own steps, for its creator), and gives every secure one its secure
/// parents' sharees.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> The BFF's create and re-file paths secure a filed record inline, and provisioning secures
/// the records filed under the record it secures. A record filed OUTSIDE the BFF — a wizard's <c>Xrm.WebApi</c> create, a
/// form edit, an import, a flow — or one whose inline securing failed, is reached only here. It reads the DATA, not the
/// path that wrote it, so no writer can hide a filed record from it (POML escalation trigger: does not fire). It is also
/// where a parent's later share change (a model-driven-app Share, a <c>/share-user</c>) reaches its filed records.</para>
/// <para><b>Writes ON, enabled</b> (owner R3/R4: minutes, never hourly; round 6: such a record IS secure). Every write is
/// provisioning's own (the record's creator shared first, read back, compensated) or the synchronizer's add-only mirror.</para>
/// <para><b>Both ways, with a floor (task 175: owner round 84, refined by round 87).</b> After the securing loop, every work
/// assignment and project is decided over the one batched walk (<see cref="SecureRootInheritance.FollowParentsPassAsync"/>):
/// its stored values become max(own, floor) — the floor its parents set, and what was set on it by hand (its access record,
/// <c>sprk_accessinheritance</c>). An inherited Secure whose parents are no longer secure follows them out of isolation
/// through the unsecure endpoint's own steps (ownership to the parents' business unit's team first, the flag cleared last);
/// an inherited Access Permission follows its parents both ways; an own value stays; a re-file never loosens. Only what
/// differs is written; a project changed here sends what is filed under it round again in the same run; un-secures are
/// bounded (<see cref="MaxUnsecuresPerRun"/>, a cursor like the provisionings') and so are the other writes
/// (<see cref="MaxRecordWritesPerRun"/>). A record whose step did not complete stays at the more restrictive state and
/// fails the run, named; the next run completes it. A record that cannot be decided is left as it is and reported in
/// <c>followParents.undetermined</c> / <c>problems</c> without failing the run (task 173's precedent: one bad row must not
/// fail every run; enforcement already treats it as secure and Restricted).</para>
/// <para><b>ADR-036 A1.</b> Rule 3: each record's step is idempotent and read back (an isolated record is only given a
/// missing sharee), so no claim marker. Rule 4: a run that could not LIST the parents or the filed records throws — a
/// failed scan is a failed run, nothing decided; a run in which some record could not be secured, or some record was
/// deferred past the bound, returns <c>Success = false</c> and the next run continues. Rule 5: one heartbeat per attempt.
/// Rule 6: registered through <c>AddScheduledJob</c> in <c>ExternalAccessModule</c>. Rule 7: no dependency on the
/// scheduler's store.</para>
/// <para><b>Bounded, resumable (task 158 r1).</b> At most <see cref="MaxProvisioningsPerRun"/> records are PROVISIONED per
/// run (each creates an SPE container), taken in a fixed order (table, then id) starting AFTER the last record the previous
/// run reached — a cursor in this singleton, per instance (task 148's job precedent) — so a record that keeps failing never
/// starves the ones behind it. Any deferral makes the run <c>Success = false</c> (with <c>deferred</c> and
/// <c>resumeAfter</c> in its ResultJson); the next run continues from the cursor. Isolated records only have their sharees
/// (and the provenance of those, owner round 30) checked, which every run does for all of them.</para>
/// <para><b>Provenance of records no longer filed under their source</b> (task 158 r1, owner round 30). A record re-filed
/// AWAY from a secure parent keeps what that parent passed on, and is no longer listed under it; so after the per-record
/// pass, each secure parent's own inherited-share rows on records this run did not visit are checked
/// (<see cref="SecureRootInheritance.ReconcileUnvisitedProvenanceAsync"/>): one the parent no longer shares (an unshare
/// made outside the BFF) is ended by the reverse rule. Anything not done fails the run.</para>
/// <para><b>An EMPTY parent flag is reported, never skipped</b> (owner round 17 item 3). The scan lists the matters and
/// projects flagged secure AND those whose flag is empty; a record filed only under an empty-flagged parent is
/// unverifiable (nothing is written to it) and fails the run, naming it — so a parent the repair script has not reached is
/// visible, not silently outside the rule.</para>
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process scheduler — BFF identity, the same
/// inheritance the endpoints call (L1 and L4 run ONE invariant owner), low volume (the filed records of an environment's
/// secure records). No package, store, column or interface.</para>
/// </remarks>
public sealed class SecureRootInheritanceJob : IScheduledJob
{
    /// <summary>Stable job id — the scheduler's run history and admin endpoints key off it.</summary>
    public const string JobIdConstant = "secure-root-inheritance";

    /// <summary>Every 5 minutes (owner R3/R4: the safety net runs at ≤ 5 minutes).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>Provisionings per run (each creates a container); the rest wait for the next run.</summary>
    internal const int MaxProvisioningsPerRun = 25;

    /// <summary>Task 175: un-secures per run (each moves ownership and revokes shares); the rest wait for the next run.</summary>
    internal const int MaxUnsecuresPerRun = 25;

    /// <summary>Task 175: other followed records per run (an Access Permission or an access record, one column each).</summary>
    internal const int MaxRecordWritesPerRun = 500;

    /// <summary>Records listed in <c>ResultJson</c> (the per-record log lines are complete).</summary>
    internal const int MaxSampledRecords = 200;

    private const int PageSize = 5000;
    private const int MaxPages = 20;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SecureRootInheritanceJob> _logger;

    public SecureRootInheritanceJob(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SecureRootInheritanceJob> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public string JobId => JobIdConstant;

    /// <inheritdoc />
    public string DisplayName => "Secure Root Inheritance";

    /// <inheritdoc />
    public string Description =>
        "Makes every work assignment and project filed under a secure matter or project secure itself (its own owner team, " +
        "container and creator share), and gives each its secure parents' sharees (task 158, owner round 6); un-secures one " +
        "whose secure parents are no longer secure and keeps every filed one's Access Permission equal to its parents' " +
        "(task 175, owner round 84).";

    /// <inheritdoc />
    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();

        using var scope = _scopeFactory.CreateScope();
        var dataverse = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
        var inheritance = scope.ServiceProvider.GetRequiredService<SecureRootInheritance>();

        // A scan that cannot complete decides nothing: ADR-036 A1 rule 4 — it throws, and the run is a failed run. The
        // parents flagged secure, and those whose flag is EMPTY (owner round 17 item 3: never read as "not secure") — every
        // record filed under one is decided below, so one filed only under an empty-flagged parent is reported.
        var parents = await ListParentsAsync(dataverse, ConditionOperator.Equal, cancellationToken).ConfigureAwait(false);
        var emptyFlagParents = await ListParentsAsync(dataverse, ConditionOperator.Null, cancellationToken).ConfigureAwait(false);
        var scanned = parents.Concat(emptyFlagParents).Distinct().ToList();
        var filed = scanned.Count == 0
            ? Array.Empty<FiledRootRef>()
            : await inheritance.ListFiledRootsAsync(scanned, cancellationToken).ConfigureAwait(false);

        var counts = new Dictionary<SecureRootInheritOutcome, int>();
        var incomplete = new List<string>();
        var sampled = new List<object>();
        var provisionings = 0;
        var deferred = 0;
        var sharesWritten = 0;
        var traceId = $"job:{context.RunId}";

        // The not-yet-secure records (they need provisioning) in a fixed order, starting after the previous run's cursor;
        // then the secure ones (their sharees), every run.
        (string Table, Guid Id)? resumeAfter;
        lock (_cursorGate)
            resumeAfter = _cursor;
        var notYet = filed.Where(r => !r.FlaggedSecure).OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
        var start = resumeAfter is { } after ? notYet.FindIndex(r => Compare((r.Table, r.Id), after) > 0) : 0;
        if (start < 0)
            start = 0;
        var ordered = notYet.Skip(start).Concat(notYet.Take(start))
            .Concat(filed.Where(r => r.FlaggedSecure).OrderBy(r => r.Table, StringComparer.Ordinal).ThenBy(r => r.Id))
            .ToList();
        (string Table, Guid Id)? lastProvisioned = null;
        var visited = new HashSet<(string Table, Guid Id)>();

        foreach (var record in ordered)
        {
            if (!record.FlaggedSecure && provisionings >= MaxProvisioningsPerRun)
            {
                deferred++;
                continue;
            }

            visited.Add((record.Table, record.Id));
            var result = await inheritance.SecureIfFiledUnderSecureAsync(record.Table, record.Id, traceId, cancellationToken)
                .ConfigureAwait(false);
            if (!record.FlaggedSecure)
                lastProvisioned = (record.Table, record.Id);
            if (result.Outcome is SecureRootInheritOutcome.Secured or SecureRootInheritOutcome.Refused or SecureRootInheritOutcome.Failed)
                provisionings++;

            counts[result.Outcome] = counts.TryGetValue(result.Outcome, out var n) ? n + 1 : 1;
            if (result.Shares is { } shares)
                sharesWritten += shares.SharesGranted + shares.SharesChanged;

            _logger.LogInformation(
                "[SECURE-INHERIT] run={RunId} {Table} {RecordId}: {Outcome} {Code} {Detail}",
                context.RunId, record.Table, record.Id, result.WireOutcome, result.ReasonCode, result.Detail);

            if (!result.IsComplete)
                incomplete.Add($"{record.Table}:{record.Id:D}: {result.WireOutcome} ({result.ReasonCode})");

            if (result.Outcome != SecureRootInheritOutcome.NotFiledUnderSecure && sampled.Count < MaxSampledRecords)
            {
                sampled.Add(new
                {
                    table = record.Table,
                    id = record.Id,
                    outcome = result.WireOutcome,
                    reasonCode = result.IsComplete ? null : result.ReasonCode,
                    sharesWritten = result.Shares is { } s ? s.SharesGranted + s.SharesChanged : 0,
                });
            }
        }

        // Owner round 30 ("the L4 job also reconciles provenance"): what each secure parent passed on to records NOT visited
        // above — re-filed away from it, still secure — is ended when the parent no longer shares the principal (an unshare
        // made outside the BFF). Never throws; anything not done fails the run, naming it.
        var unvisited = await inheritance.ReconcileUnvisitedProvenanceAsync(parents, visited, cancellationToken)
            .ConfigureAwait(false);
        if (!unvisited.IsComplete)
            incomplete.Add($"inherited shares of records no longer filed under their source: {unvisited.NotDone} not ended ({unvisited.Detail})");

        // The cursor: past the bound, the next run continues after the last record this one provisioned; a run that reached
        // every record starts the next from the beginning.
        lock (_cursorGate)
            _cursor = deferred > 0 ? lastProvisioned : null;

        // Task 175 (owner round 84): the other direction, and the Access Permission. A listing that cannot complete throws
        // (ADR-036 A1 rule 4) — a failed run, nothing decided on part of it.
        (string Table, Guid Id)? unsecureAfter;
        lock (_cursorGate)
            unsecureAfter = _unsecureCursor;
        var follow = await inheritance.FollowParentsPassAsync(
            traceId, unsecureAfter, MaxUnsecuresPerRun, MaxRecordWritesPerRun, cancellationToken).ConfigureAwait(false);
        lock (_cursorGate)
            _unsecureCursor = follow.UnsecureResumeAfter;
        if (follow.Untrusted is { } untrusted)
        {
            // Task 175 fix round 2: access records could not be trusted (the column is not field-secured, or the BFF's read
            // of it is not proven) — nothing was followed on them; the run fails until the schema script fixes it.
            incomplete.Add($"access records not trusted ({untrusted}): {string.Join("; ", follow.Problems.Take(5))}");
        }

        if (follow.NotCompleted > 0)
        {
            incomplete.Add($"{follow.NotCompleted} filed record(s) not brought into step with their parents (left at the more " +
                           $"restrictive state; the next run retries): {string.Join("; ", follow.Problems.Take(20))}");
        }

        _logger.Log(
            follow.IsComplete ? LogLevel.Information : LogLevel.Warning,
            "[FOLLOW-PARENT] run={RunId} listed={Listed} parentless={Parentless} inStep={InStep} unsecured={Unsecured} " +
            "permissions={Permissions} undetermined={Undetermined} notCompleted={NotCompleted} deferred={Deferred}",
            context.RunId, follow.Listed, follow.Parentless, follow.InStep, follow.Unsecured, follow.PermissionsChanged,
            follow.Undetermined, follow.NotCompleted, follow.Deferred);

        var duration = _timeProvider.GetElapsedTime(started);
        var success = incomplete.Count == 0 && deferred == 0 && follow.Deferred == 0;
        int Count(SecureRootInheritOutcome outcome) => counts.TryGetValue(outcome, out var n) ? n : 0;

        // THE HEARTBEAT (ADR-036 A1 rule 5) — every attempt, including one with nothing to do.
        _logger.Log(
            success ? LogLevel.Information : LogLevel.Warning,
            "[SECURE-INHERIT] heartbeat secureParents={Parents} emptyFlagParents={EmptyFlag} filed={Filed} secured={Secured} " +
            "alreadySecure={Already} unverifiable={Unverifiable} refused={Refused} failed={Failed} deferred={Deferred} " +
            "resumeAfter={ResumeAfter} sharesWritten={Shares} attempt={Attempt} durationMs={DurationMs} trigger={Trigger} " +
            "runId={RunId} correlationId={CorrelationId}",
            parents.Count, emptyFlagParents.Count, filed.Count, Count(SecureRootInheritOutcome.Secured),
            Count(SecureRootInheritOutcome.AlreadySecure), Count(SecureRootInheritOutcome.Unverifiable),
            Count(SecureRootInheritOutcome.Refused), Count(SecureRootInheritOutcome.Failed), deferred,
            deferred > 0 && lastProvisioned is { } cursorAt ? $"{cursorAt.Table}:{cursorAt.Id:D}" : null, sharesWritten,
            context.Attempt, (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: success,
            ErrorMessage: success
                ? null
                : string.Join(" ", new[]
                {
                    incomplete.Count == 0 ? null
                        : $"{incomplete.Count} filed record(s) not secure yet (or not given their parents' sharees); the next run " +
                          "revisits them: " + string.Join("; ", incomplete.Take(20)) + ".",
                    deferred == 0 ? null
                        : $"{deferred} filed record(s) were deferred past this run's bound of {MaxProvisioningsPerRun} " +
                          "provisionings; the next run continues from where this one stopped.",
                    follow.Deferred == 0 ? null
                        : $"{follow.Deferred} filed record(s) were not brought into step with their parents past this run's bounds " +
                          $"({MaxUnsecuresPerRun} un-secures, {MaxRecordWritesPerRun} other records); the next run continues.",
                }.Where(m => m is not null)),
            ProcessedItems: filed.Count - deferred,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(new
            {
                secureParents = parents.Count,
                emptyFlagParents = emptyFlagParents.Count,
                filed = filed.Count,
                secured = Count(SecureRootInheritOutcome.Secured),
                alreadySecure = Count(SecureRootInheritOutcome.AlreadySecure),
                notFiledUnderSecure = Count(SecureRootInheritOutcome.NotFiledUnderSecure),
                unverifiable = Count(SecureRootInheritOutcome.Unverifiable),
                refused = Count(SecureRootInheritOutcome.Refused),
                failed = Count(SecureRootInheritOutcome.Failed),
                deferred,
                // The progress cursor: the next run continues after this record. Null when this run reached every record.
                resumeAfter = deferred > 0 && lastProvisioned is { } resume ? $"{resume.Table}:{resume.Id:D}" : null,
                sharesWritten,
                // Task 158 r1: inherited shares on records no longer filed under the parent that passed them on.
                unfiledProvenance = new { ended = unvisited.Rows, removed = unvisited.Removed, kept = unvisited.Kept, notDone = unvisited.NotDone },
                // Task 175 (owner round 84): every filed record brought into step with its parents, both ways.
                followParents = new
                {
                    listed = follow.Listed,
                    parentless = follow.Parentless,
                    inStep = follow.InStep,
                    unsecured = follow.Unsecured,
                    permissionsChanged = follow.PermissionsChanged,
                    accessRecordsWritten = follow.MarkersWritten,
                    changedConcurrently = follow.Conflicts,
                    untrusted = follow.Untrusted,
                    undetermined = follow.Undetermined,
                    notCompleted = follow.NotCompleted,
                    deferred = follow.Deferred,
                    resumeAfter = follow.UnsecureResumeAfter is { } u ? $"{u.Table}:{u.Id:D}" : null,
                    problems = follow.Problems,
                    changes = follow.Changes,
                },
                incomplete = incomplete.Take(50).ToArray(),
                records = sampled,
                attempt = context.Attempt,
            }, ResultJsonOptions));
    }

    /// <summary>
    /// Every matter and project whose <c>sprk_issecure</c> is TRUE (<paramref name="flag"/> =
    /// <see cref="ConditionOperator.Equal"/>) or EMPTY (<see cref="ConditionOperator.Null"/>), paged; past the page ceiling it
    /// throws rather than decide on part of them.
    /// </summary>
    private static async Task<List<(string Table, Guid Id)>> ListParentsAsync(
        IGenericEntityService dataverse, ConditionOperator flag, CancellationToken ct)
    {
        var parents = new List<(string, Guid)>();
        foreach (var table in new[] { SecureRootInheritance.Matter, SecureRootInheritance.Project })
        {
            var query = new QueryExpression(table)
            {
                ColumnSet = new ColumnSet(table + "id"),
                NoLock = true,
                PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
            };
            if (flag == ConditionOperator.Null)
                query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Null);
            else
                query.Criteria.AddCondition("sprk_issecure", ConditionOperator.Equal, true);

            for (var page = 1; ; page++)
            {
                if (page > MaxPages)
                    throw new InvalidOperationException(
                        $"{table} still had {(flag == ConditionOperator.Null ? "empty-flagged" : "secure")} records to list after " +
                        $"{MaxPages} pages; nothing is decided on part of them.");

                var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
                parents.AddRange(result.Entities.Select(e => (table, e.Id)));
                if (!result.MoreRecords)
                    break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = result.PagingCookie;
            }
        }

        return parents.Distinct().ToList();
    }

    /// <summary>The provisioning order: table (ordinal), then id.</summary>
    private static int Compare((string Table, Guid Id) a, (string Table, Guid Id) b)
    {
        var byTable = string.CompareOrdinal(a.Table, b.Table);
        return byTable != 0 ? byTable : a.Id.CompareTo(b.Id);
    }

    // The progress cursor (task 158 r1): the last not-yet-secure record a run provisioned while it deferred others; null
    // between full passes. In this singleton, per instance — the task 143 / 148 jobs' precedent (ADR-036 A1 rule 7: no
    // dependency on the scheduler's store). A restart only starts the order over, which is harmless: every step is keyed on
    // observed state.
    private readonly object _cursorGate = new();
    private (string Table, Guid Id)? _cursor;

    // Task 175: the un-secure pass's cursor — the last record it tried while it deferred others (same rules as _cursor).
    private (string Table, Guid Id)? _unsecureCursor;
}
