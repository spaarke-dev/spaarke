using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// spaarke-ontology-platform-r1 task 148 (D-135 item 2, "follow the assignee"; shape approved by uac-r2 2026-10-12, F1-F3).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: SignalReevaluationJob re-owns only OPEN hit Signals, nightly, and no Decision Record; CoreAncestorStamp-
//     ReconciliationJob (stamps) and SecureChildReconciliationJob (moves across the secure wall) are uac-r2's; none re-owns a
//     personal To Do's rows to its CURRENT assignee's team.
//   Extension: a pass inside a UAC job would put ontology ownership rules in UAC files; a pass in the nightly Signal job misses
//     the Decision Record half and the 5-minute cadence. This class reuses that job's scaffold, the scheduler host and its
//     admin surface, and re-owns ONLY through uac-r2's SecureChildReconciler.ReownOrdinaryRowAsync (F3).
//   Cost of doing nothing: after an Assigned To change on the client form (no BFF hook), or the assignee's business-unit move,
//     a personal To Do's Signals and Decision Records stay with the old team indefinitely (D-135 item 2), so the person the
//     To Do is assigned to cannot read them.
// =====================================================================================================================

/// <summary>
/// Keeps every Signal and Decision Record about a PERSONAL To Do (a <c>sprk_todo</c> filed under nothing, owner K2) owned by
/// the D-134 answer for the To Do's CURRENT state: the business-unit team of its Assigned To's one eligible user, else the
/// To Do's own team (the resolver's answer for the To Do as the row's parent).
/// </summary>
/// <remarks>
/// <para><b>Two passes (uac-r2 F2).</b> Every run: the To Dos modified since this instance's watermark (an Assigned To change
/// on the form, a filing or un-filing), plus the To Dos an earlier run left unsettled. Nightly (the first run after
/// <see cref="NightlyHourUtc"/> UTC each day, or an admin trigger with <c>pass=full</c>): EVERY Signal and Decision Record
/// whose <c>sprk_regardingtodo</c> is set, recomputed against the CURRENT answer, which catches what changes no To Do (the
/// assignee's business unit, a disabled or re-enabled user, a relinked <c>sprk_primarycontact</c>). Both passes are bounded
/// per run (page cap); the nightly one carries a cursor across runs (the distributed cache) until it has read every row.</para>
/// <para><b>Live filing decides (uac-r2 F1, D-34).</b> Rows are selected by <c>sprk_regardingtodo</c>. A To Do filed under
/// anything is not personal: under a core record (<see cref="CoreAncestorResolver"/>) its rows are the re-file rule's
/// (<c>SkippedCored</c>); under a non-core record it keeps its own team (D-135 K2), so its rows take the To Do's own team, as
/// the writers give them (<c>FiledNonCore</c>; a row the assignee pass moved earlier comes back). A row whose own live
/// <c>sprk_regarding{matter,project,workassignment,servicerequest}</c> stamp is set is skipped as cored too. The immutable
/// decision context (<c>sprk_corerecordid</c>, <c>sprk_corerecordtype</c>) is never read.</para>
/// <para><b>Secure wins (D-134 item 7).</b> Before a row is written its parents are resolved together
/// (<see cref="RecordOwnershipContext.ForParents"/>: the To Do first, then every ownership-parent lookup the row carries,
/// and for a Decision Record the subjects of the Signals it resolved, as the writers ask): a secure answer is
/// <c>SkippedSecure</c>, a refusal <c>SkippedRefused</c>. A row owned by the Secure Record owner team is never touched; a row
/// owned by a user is <c>SkippedUserOwned</c>. Only a row whose owner differs from the answer is written (uac-r2 K3: each
/// re-own costs several round trips).</para>
/// <para><b>One write path (uac-r2 F3).</b> Every re-own, Signal and Decision Record alike, is
/// <see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/>: Reowned; AlreadyOwned → <c>AlreadyApplied</c>; RefusedSecure →
/// <c>SkippedSecure</c>; RefusedNotTeamOwned → <c>SkippedUserOwned</c>; NotFound → <c>SkippedGone</c>; Conflict →
/// <c>SkippedConflict</c> (nothing written; the To Do is carried and re-evaluated next run); Failed → <c>RepairFailures</c>,
/// except Dataverse's privilege refusal (0x80040220 / 0x80040299 — the target team's role lacks Read, F2), which is
/// <c>AccessDenied</c> and held back for <see cref="AccessDeniedBackoff"/>: a defined outcome, never a retry storm.</para>
/// <para><b>Report-only by default.</b> Writes happen only when <see cref="WritesEnabledConfigKey"/> is exactly
/// <c>true</c>; turning it on is an owner decision after a report-only run uac-r2 has reviewed. Report-only lists the
/// would-be re-owns in <c>sampleStale</c> and calls nothing that writes.</para>
/// <para><b>Fail closed.</b> A scan that fails is a FAILED run (never "0 stale"). A To Do whose assignee, core or owner
/// cannot be read writes nothing for that To Do and makes the run partial; it is carried to the next run.
/// <b>Idempotency</b> (ADR-036 A1 rule 3): a claim per row and target, a completion marker per run (the scaffold's).
/// <b>Retry</b>: it does not throw; the next 5-minute tick is the retry. <b>Heartbeat</b>: one line per attempt.</para>
/// <para><b>Placement</b> (ADR-052, CLAUDE.md §10): in the BFF on the in-process <c>Spaarke.Scheduling</c> host, beside the
/// writers whose rule it re-applies (<see cref="PersonalToDoAssignee"/>, <see cref="IRecordOwnershipResolver"/>), over
/// BFF-owned tables with the BFF identity, low volume (personal To Dos with Signals or Decision Records). No new scheduler,
/// store, package or identity.</para>
/// </remarks>
public sealed class TodoAssigneeOwnerReconciliationJob : IScheduledJob
{
    public const string JobIdConstant = "todo-assignee-owner-reconciliation";

    /// <summary>Every 5 minutes (uac-r2 F2: the watermark pass); the nightly recompute rides the first run after 02:00 UTC.</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>The write switch: writes only when exactly <c>true</c>. Absent, <c>false</c> or unparseable = report-only.</summary>
    internal const string WritesEnabledConfigKey = "Ontology:TodoAssigneeOwner:WritesEnabled";

    /// <summary>The admin-trigger parameter that asks for the full recompute now (<c>pass=full</c>).</summary>
    internal const string PassParameter = "pass";

    /// <summary>The UTC hour from which the day's full recompute is due.</summary>
    internal const int NightlyHourUtc = 2;

    internal const int PageSize = 500;
    internal const int MaxPages = 10;

    /// <summary>To Do ids per <c>in</c> condition when the rows of a set of To Dos are read.</summary>
    internal const int IdBatchSize = 100;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string LogPrefix = "[TODO-ASSIGNEE-OWNER]";

    internal const string SignalTable = "sprk_signal";
    internal const string DecisionRecordTable = "sprk_decisionrecord";
    internal const string RegardingTodoColumn = "sprk_regardingtodo";
    private const string TodoTable = PersonalToDoAssignee.Table;
    private const string ModifiedOnColumn = "modifiedon";
    private const string OwningTeamColumn = "owningteam";
    private const string OwningUserColumn = "owninguser";
    private const string OwningBusinessUnitColumn = "owningbusinessunit";
    private const string SignalDecisionRecordColumn = "sprk_decisionrecord";

    /// <summary>How many would-be / done re-owns <see cref="JobRunResult.ResultJson"/> lists; every one is in the log.</summary>
    internal const int MaxSampledIds = 200;

    /// <summary>How long a row whose re-own Dataverse refused for lack of a privilege is left alone (one attempt a day).</summary>
    internal static readonly TimeSpan AccessDeniedBackoff = TimeSpan.FromHours(24);

    /// <summary>The first run on an instance looks back this far; anything older is the nightly recompute's.</summary>
    internal static readonly TimeSpan InitialLookback = TimeSpan.FromHours(6);

    /// <summary>Each watermark is the run's start minus this (clock skew, and a write that committed while it listed).</summary>
    internal static readonly TimeSpan WatermarkOverlap = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan CursorLifetime = TimeSpan.FromDays(2);
    private static readonly TimeSpan RepairClaimDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RepairMarkerLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly string[] Tables = [SignalTable, DecisionRecordTable];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TodoAssigneeOwnerReconciliationJob> _logger;

    // Per-instance state between runs (a singleton; the host runs one attempt at a time). Lost on restart: the first run
    // looks back InitialLookback and the nightly recompute covers the rest.
    private DateTime? _watermark;
    private Guid? _watermarkAfter;
    private readonly HashSet<Guid> _carried = [];
    private DateOnly? _lastFullPass;

    public TodoAssigneeOwnerReconciliationJob(
        IServiceScopeFactory scopeFactory,
        CoreAncestorResolver coreAncestors,
        IRecordOwnershipResolver ownership,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<TodoAssigneeOwnerReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Rows per scan page. Settable only so a test can reach the page bound.</summary>
    internal int ScanPageSize { get; init; } = PageSize;

    /// <summary>Pages per scan per run. Settable only so a test can reach the page bound.</summary>
    internal int ScanMaxPages { get; init; } = MaxPages;

    public string JobId => JobIdConstant;

    public string DisplayName => "To Do Assignee Owner Reconciliation";

    public string Description =>
        "Re-owns the Signals and Decision Records of a personal To Do (filed under nothing) to its current Assigned To's "
        + "business-unit team, or the To Do's own team when there is no single eligible assignee (D-134/D-135). Every 5 "
        + "minutes for recently changed To Dos, nightly for all. Report-only unless " + WritesEnabledConfigKey + "=true.";

    /// <summary>Writes only when the switch says exactly <c>true</c> (owner: report-only unless explicitly on).</summary>
    internal bool WritesEnabled => bool.TryParse(_configuration[WritesEnabledConfigKey], out var enabled) && enabled;

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var runStart = _timeProvider.GetUtcNow().UtcDateTime;
        var writesEnabled = WritesEnabled;
        var counts = new RunCounts();
        var problems = new List<string>();
        var status = StatusOk;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var run = new Run(
                scope.ServiceProvider.GetRequiredService<IGenericEntityService>(),
                scope.ServiceProvider.GetRequiredService<AssignedAccessStore>(),
                scope.ServiceProvider.GetRequiredService<SecureChildReconciler>(),
                counts, problems, context);
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
            var cursors = scope.ServiceProvider.GetService<IDistributedCache>();

            // ── Which rows: the watermark window and the carried To Dos, then (when due) every row. ──────────────────
            var rows = new Dictionary<(string Table, Guid Id), Entity>();
            var hosts = await ProbeHostsAsync(run, cancellationToken).ConfigureAwait(false);

            // The carried set is replaced only when the run completes; a cancelled or failed run keeps it.
            var carried = _carried.ToList();
            var window = await ListRecentToDosAsync(run, runStart, cancellationToken).ConfigureAwait(false);
            var recent = window.ToDos.Concat(carried).Distinct().ToList();
            counts.RecentToDos = window.ToDos.Count;
            counts.CarriedToDos = carried.Count;
            var unreadToDos = await ReadRowsOfToDosAsync(run, hosts, recent, rows, cancellationToken).ConfigureAwait(false);

            var full = await IsFullPassDueAsync(cursors, context, runStart, cancellationToken).ConfigureAwait(false);
            var fullFinished = false;
            if (full)
            {
                counts.FullPass = true;
                fullFinished = await ScanAllRowsAsync(run, hosts, cursors, rows, cancellationToken).ConfigureAwait(false);
            }

            // A To Do whose rows could not all be read is not decided this run (no write for it); it is carried.
            var decidable = rows.Values
                .Where(r => r.GetAttributeValue<EntityReference>(RegardingTodoColumn) is not { } t || !unreadToDos.Contains(t.Id))
                .ToList();

            // ── Decide every row against its To Do's CURRENT answer. ────────────────────────────────────────────────
            var stale = await DecideAsync(run, decidable, cancellationToken).ConfigureAwait(false);
            counts.Stale = stale.Count;
            foreach (var row in stale)
            {
                // The before-state, for EVERY stale row, in BOTH modes, before anything is written (reversal evidence).
                _logger.LogInformation(
                    "{Prefix} stale mode={Mode} entity={Entity} id={Id} todo={TodoId} fallback={Fallback} owner={Before} -> team={After} correlationId={CorrelationId}",
                    LogPrefix, writesEnabled ? "write" : "report-only", row.Table, row.Id, row.TodoId, row.Fallback,
                    row.FromTeam, row.ToTeam, context.CorrelationId);
                if (counts.StaleSample.Count < MaxSampledIds)
                    counts.StaleSample.Add($"{row.Table}:{row.Id:D}:{row.FromTeam:D}->{row.ToTeam:D}");
            }

            if (writesEnabled)
            {
                foreach (var row in stale)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RepairAsync(run, idempotency, row, cancellationToken).ConfigureAwait(false);
                }
            }

            _carried.Clear();
            _carried.UnionWith(unreadToDos);
            _carried.UnionWith(run.Unsettled);

            // Bookkeeping only once the run's work is done: a cancelled or failed run keeps the old watermark.
            if (window.Listed)
            {
                _watermark = window.NextWatermark;
                _watermarkAfter = window.NextAfter;
            }

            if (fullFinished)
                await MarkFullPassDoneAsync(cursors, runStart, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every stale row was decided or re-owned.");
        }
        // A scheduled job's last line of defence: record the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "{Prefix} Run failed; reconciliation did not complete. correlationId={CorrelationId}",
                LogPrefix, context.CorrelationId);
        }

        if (counts.ScanFailures > 0)
        {
            // ADR-003 for a writer: a scan that failed is a FAILED run, never "nothing stale".
            status = StatusError;
        }
        else if (status == StatusOk
                 && (counts.RepairFailures > 0 || counts.Unverified > 0 || counts.Truncated || counts.Resumed
                     || counts.ClaimHeld > 0 || counts.AccessDenied > 0 || counts.AccessDeniedHeld > 0
                     || counts.ConfigurationRefusals > 0 || counts.AssigneeTeamRefusals > 0))
        {
            // A run that did not decide every row, or did not settle every re-own it attempted, is never reported clean.
            // Conflict and Gone are not here: nothing was owed (the row changed or went), and a Conflict is carried.
            status = StatusPartial;
        }

        if (status == StatusPartial)
        {
            // ADR-036: a run that is not a success says why in its ErrorMessage, not only in ResultJson counts.
            problems.Add(string.Create(CultureInfo.InvariantCulture,
                $"Partial: {counts.Unverified} row(s) not decided, {counts.RepairFailures} re-own(s) failed, "
                + $"{counts.AccessDenied + counts.AccessDeniedHeld} refused or held for a missing privilege, "
                + $"{counts.ClaimHeld} claimed by another run, {counts.ConfigurationRefusals} refused by configuration "
                + $"(Secure Record unit or team, or a unit's default team), {counts.AssigneeTeamRefusals} not moved because the "
                + $"assignee's business-unit team was refused (no single default team, or the Secure Record unit); not-decided "
                + $"To Dos are carried to the next run."));
        }

        var duration = _timeProvider.GetElapsedTime(started);
        var mode = writesEnabled ? "write" : "report-only";

        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} mode={Mode} attempt={Attempt} fullPass={FullPass} recentToDos={RecentToDos} "
            + "carriedToDos={CarriedToDos} scanned={Scanned} stale={Stale} reowned={Reowned} alreadyApplied={AlreadyApplied} "
            + "skippedCored={SkippedCored} filedNonCore={FiledNonCore} skippedSecure={SkippedSecure} "
            + "skippedRefused={SkippedRefused} configurationRefusals={ConfigurationRefusals} assigneeTeamRefusals={AssigneeTeamRefusals} skippedUserOwned={SkippedUserOwned} skippedGone={SkippedGone} "
            + "skippedConflict={SkippedConflict} fallbackNoAssignee={FallbackNoAssignee} fallbackAmbiguous={FallbackAmbiguous} "
            + "fallbackDisabled={FallbackDisabled} accessDenied={AccessDenied} accessDeniedHeld={AccessDeniedHeld} "
            + "claimHeld={ClaimHeld} repairFailures={RepairFailures} unverified={Unverified} scanFailures={ScanFailures} "
            + "truncated={Truncated} resumed={Resumed} durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, mode, context.Attempt, counts.FullPass, counts.RecentToDos, counts.CarriedToDos, counts.Scanned,
            counts.Stale, counts.Reowned, counts.AlreadyApplied, counts.SkippedCored, counts.FiledNonCore,
            counts.SkippedSecure, counts.SkippedRefused, counts.ConfigurationRefusals, counts.AssigneeTeamRefusals, counts.SkippedUserOwned, counts.SkippedGone, counts.SkippedConflict,
            counts.FallbackNoAssignee, counts.FallbackAmbiguous, counts.FallbackDisabled, counts.AccessDenied,
            counts.AccessDeniedHeld, counts.ClaimHeld, counts.RepairFailures, counts.Unverified, counts.ScanFailures,
            counts.Truncated, counts.Resumed, (long)duration.TotalMilliseconds, context.Trigger, context.RunId,
            context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: counts.Reowned,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    mode,
                    writesEnabled,
                    attempt = context.Attempt,
                    counts.FullPass,
                    counts.RecentToDos,
                    counts.CarriedToDos,
                    counts.Scanned,
                    counts.Stale,
                    counts.Reowned,
                    counts.AlreadyApplied,
                    counts.SkippedCored,
                    counts.FiledNonCore,
                    counts.SkippedSecure,
                    counts.SkippedRefused,
                    counts.ConfigurationRefusals,
                    counts.AssigneeTeamRefusals,
                    counts.SkippedUserOwned,
                    counts.SkippedGone,
                    counts.SkippedConflict,
                    counts.FallbackNoAssignee,
                    counts.FallbackAmbiguous,
                    counts.FallbackDisabled,
                    counts.AccessDenied,
                    counts.AccessDeniedHeld,
                    counts.ClaimHeld,
                    counts.RepairFailures,
                    counts.Unverified,
                    counts.ScanFailures,
                    counts.Truncated,
                    counts.Resumed,
                    sampleStale = counts.StaleSample,
                },
                ResultJsonOptions));
    }

    // ── Listing ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each table's columns present in this environment (a missing column would fault the scan): the row's lineage lookups
    /// (<see cref="SecureChildLineage"/>, its ownership parents) and its live core stamps. An unreadable table is a scan
    /// failure and is left out of both passes.
    /// </summary>
    private async Task<Dictionary<string, HostColumns>> ProbeHostsAsync(Run run, CancellationToken ct)
    {
        var hosts = run.HostMap;
        foreach (var table in Tables)
        {
            IReadOnlySet<string> host;
            try
            {
                host = await _coreAncestors.ProbeColumnsAsync(table, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                run.ScanFailed($"{table}: column metadata unavailable, so its rows were not checked ({ex.Message}).", ex, _logger);
                continue;
            }

            if (!host.Contains(RegardingTodoColumn))
            {
                run.ScanFailed($"{table}: has no {RegardingTodoColumn} column, so its rows were not checked.", null, _logger);
                continue;
            }

            var lineage = SecureChildLineage.Children[table].Lookups;
            var parents = lineage
                .Where(l => host.Contains(l.Key) && RecordOwnershipResolver.IsOwnershipParent(l.Value))
                .Select(l => l.Key)
                .ToArray();
            var stamps = CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute).Where(host.Contains).ToArray();
            hosts[table] = new HostColumns(parents, stamps);
        }

        return hosts;
    }

    /// <summary>
    /// The To Dos modified since this instance's watermark (continuing after the last id when an earlier run stopped at the
    /// page bound). Returns the next watermark only when the listing finished; the caller commits it after the run.
    /// </summary>
    private async Task<RecentWindow> ListRecentToDosAsync(Run run, DateTime runStart, CancellationToken ct)
    {
        var since = _watermark ?? runStart - InitialLookback;
        var after = _watermarkAfter;
        try
        {
            var query = new QueryExpression(TodoTable) { ColumnSet = new ColumnSet(TodoTable + "id"), NoLock = true };
            query.Criteria.AddCondition(ModifiedOnColumn, ConditionOperator.GreaterEqual, since);
            if (after is { } a)
                query.Criteria.AddCondition(TodoTable + "id", ConditionOperator.GreaterThan, a);
            query.AddOrder(TodoTable + "id", OrderType.Ascending);

            var (entities, truncated) = await PageAsync(run.Entities, query, ct).ConfigureAwait(false);
            var ids = entities.Select(e => e.Id).Where(id => id != Guid.Empty).ToList();
            if (truncated)
            {
                run.Counts.Truncated = true;
                run.Problems.Add($"{TodoTable}: the recent-changes listing stopped after {ScanMaxPages} pages of {ScanPageSize}; "
                                 + "the next run continues after the last To Do read.");
                return new RecentWindow(ids, Listed: true, NextWatermark: since, NextAfter: ids.Count > 0 ? ids[^1] : after);
            }

            return new RecentWindow(ids, Listed: true, NextWatermark: runStart - WatermarkOverlap, NextAfter: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            run.ScanFailed($"{TodoTable}: the To Dos changed since {since:O} could not be listed; nothing was decided for them ({ex.Message}).",
                ex, _logger);
            return new RecentWindow([], Listed: false, NextWatermark: since, NextAfter: after);
        }
    }

    /// <summary>The Signal and Decision Record rows of <paramref name="todoIds"/>. Returns the To Dos whose rows could not
    /// all be read (a scan failure; carried to the next run).</summary>
    private async Task<HashSet<Guid>> ReadRowsOfToDosAsync(
        Run run, IReadOnlyDictionary<string, HostColumns> hosts, IReadOnlyList<Guid> todoIds,
        Dictionary<(string Table, Guid Id), Entity> rows, CancellationToken ct)
    {
        var unread = new HashSet<Guid>();
        foreach (var batch in todoIds.Chunk(IdBatchSize))
        {
            foreach (var table in Tables)
            {
                if (!hosts.TryGetValue(table, out var host))
                {
                    unread.UnionWith(batch);
                    continue;
                }

                try
                {
                    var query = RowQuery(table, host);
                    query.Criteria.AddCondition(RegardingTodoColumn, ConditionOperator.In, batch.Cast<object>().ToArray());
                    var (entities, truncated) = await PageAsync(run.Entities, query, ct).ConfigureAwait(false);
                    foreach (var e in entities)
                        rows.TryAdd((table, e.Id), e);
                    if (truncated)
                    {
                        // A bound, not a fault: every row read is decided on its own (each row's owner is decided per row),
                        // and the rest wait for the nightly recompute, whose cursor reaches them. Excluding the batch instead
                        // would also drop the rows the nightly pass read, so a batch that always overflows would never be decided.
                        run.Counts.Truncated = true;
                        run.Problems.Add($"{table}: the rows of {batch.Length} recently changed To Do(s) exceeded the page bound; "
                                         + "the rows read were decided, the rest wait for the nightly recompute.");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    unread.UnionWith(batch);
                    run.ScanFailed($"{table}: the rows of {batch.Length} recently changed To Do(s) could not be read ({ex.Message}).",
                        ex, _logger);
                }
            }
        }

        return unread;
    }

    /// <summary>
    /// The nightly recompute's listing: every row of each table with <c>sprk_regardingtodo</c> set, by id, continuing where
    /// an earlier run stopped. True when both tables were read to the end (the day's recompute is done).
    /// </summary>
    private async Task<bool> ScanAllRowsAsync(
        Run run, IReadOnlyDictionary<string, HostColumns> hosts, IDistributedCache? cursors,
        Dictionary<(string Table, Guid Id), Entity> rows, CancellationToken ct)
    {
        var finished = true;
        foreach (var table in Tables)
        {
            if (!hosts.TryGetValue(table, out var host))
            {
                finished = false;
                continue;
            }

            var cursorKey = CursorKey(table);
            var after = await ReadCursorAsync(cursors, cursorKey, ct).ConfigureAwait(false);
            List<Entity> entities;
            bool truncated;
            try
            {
                var query = RowQuery(table, host);
                query.Criteria.AddCondition(RegardingTodoColumn, ConditionOperator.NotNull);
                if (after is { } a)
                    query.Criteria.AddCondition(table + "id", ConditionOperator.GreaterThan, a);
                (entities, truncated) = await PageAsync(run.Entities, query, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                finished = false;
                run.ScanFailed($"{table}: the nightly recompute could not read its rows ({ex.Message}).", ex, _logger);
                continue;
            }

            foreach (var e in entities)
                rows.TryAdd((table, e.Id), e);

            if (truncated)
            {
                finished = false;
                run.Counts.Truncated = true;
                var saved = await WriteCursorAsync(cursors, cursorKey, entities.Count > 0 ? entities[^1].Id : after, ct)
                    .ConfigureAwait(false);
                run.Problems.Add(saved
                    ? $"{table}: the nightly recompute stopped after {ScanMaxPages} pages of {ScanPageSize}; the next run continues."
                    : $"{table}: the nightly recompute stopped after {ScanMaxPages} pages of {ScanPageSize} and where it stopped "
                      + "could not be recorded, so the next run starts from the top again.");
            }
            else if (after is not null)
            {
                run.Counts.Resumed = true;
                await ClearCursorAsync(cursors, cursorKey, ct).ConfigureAwait(false);
                run.Problems.Add($"{table}: this run finished a nightly recompute an earlier run stopped (rows after {after:D}).");
            }
        }

        return finished;
    }

    private static QueryExpression RowQuery(string table, HostColumns host)
    {
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(new[] { table + "id", RegardingTodoColumn, OwningTeamColumn, OwningUserColumn, OwningBusinessUnitColumn }
                .Concat(host.Parents).Concat(host.Stamps).Distinct(StringComparer.Ordinal).ToArray()),
            NoLock = true,
        };
        query.AddOrder(table + "id", OrderType.Ascending);
        return query;
    }

    /// <summary>Pages a query to <see cref="ScanMaxPages"/>; past it the result says TRUNCATED, never a silent prefix.</summary>
    private async Task<(List<Entity> Rows, bool Truncated)> PageAsync(IGenericEntityService entities, QueryExpression query, CancellationToken ct)
    {
        var rows = new List<Entity>();
        string? cookie = null;
        for (var page = 1; page <= ScanMaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            query.PageInfo = new PagingInfo { Count = ScanPageSize, PageNumber = page, PagingCookie = cookie };
            var result = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);
            if (!result.MoreRecords)
                return (rows, false);
            cookie = result.PagingCookie;
        }

        return (rows, true);
    }

    // ── Deciding ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every row against its To Do's current answer; the rows whose owner differs from it are returned.</summary>
    private async Task<List<StaleRow>> DecideAsync(Run run, IEnumerable<Entity> rows, CancellationToken ct)
    {
        var stale = new List<StaleRow>();
        foreach (var group in rows.GroupBy(r => r.GetAttributeValue<EntityReference>(RegardingTodoColumn)?.Id ?? Guid.Empty))
        {
            ct.ThrowIfCancellationRequested();
            if (group.Key == Guid.Empty)
                continue; // the regarding was cleared between the listing and now: not a To Do's row any more

            var todo = await AnswerForToDoAsync(run, group.Key, ct).ConfigureAwait(false);
            foreach (var row in group)
            {
                run.Counts.Scanned++;
                if (await DecideRowAsync(run, todo, row, ct).ConfigureAwait(false) is { } plan)
                    stale.Add(plan);
            }
        }

        return stale;
    }

    /// <summary>
    /// The D-134 answer for one To Do, once per run: whether it is personal, and when it is, its assignee's team or the
    /// fallback reason. A fault reading the To Do, its links, its core or the assignee's team is <see cref="ToDoKind.Unverified"/>:
    /// nothing is written for it (it is not known whether a user exists, so it must not fall back).
    /// </summary>
    private async Task<ToDoAnswer> AnswerForToDoAsync(Run run, Guid todoId, CancellationToken ct)
    {
        if (run.ToDos.TryGetValue(todoId, out var memo))
            return memo;

        ToDoAnswer answer;
        try
        {
            var todo = await ClassifyToDoAsync(run.Entities, run.Links, _coreAncestors, todoId, ct).ConfigureAwait(false);
            if (todo.Kind == ToDoKind.CoreRefused)
            {
                _logger.LogWarning("{Prefix} To Do {TodoId}: its core record cannot be derived ({Error}); its rows are refused, not "
                    + "written. correlationId={CorrelationId}", LogPrefix, todoId, todo.Detail, run.Context.CorrelationId);
            }

            answer = todo.Kind == ToDoKind.Assignee
                ? await AssigneeAnswerAsync(run, todoId, todo.AssigneeUserId!.Value, ct).ConfigureAwait(false)
                : new ToDoAnswer(todoId, todo.Kind, Fallback: todo.Fallback, Detail: todo.Detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} To Do {TodoId}: its Assigned To, core or owner could not be read; nothing is written for "
                + "its rows this run. correlationId={CorrelationId}", LogPrefix, todoId, run.Context.CorrelationId);
            answer = new ToDoAnswer(todoId, ToDoKind.Unverified, Detail: ex.Message);
        }

        switch (answer.Fallback)
        {
            case FallbackReason.NoAssignee: run.Counts.FallbackNoAssignee++; break;
            case FallbackReason.Ambiguous: run.Counts.FallbackAmbiguous++; break;
            case FallbackReason.Disabled: run.Counts.FallbackDisabled++; break;
            case FallbackReason.FiledNonCore: run.Counts.FiledNonCore++; break;
        }

        run.ToDos[todoId] = answer;
        return answer;
    }

    /// <summary>
    /// What this job makes of one To Do (D-134, owner K2), with no write and no owner question: <see cref="ToDoKind.Gone"/>; a
    /// To Do filed under a core record is the re-file rule's (<see cref="ToDoKind.Cored"/>), one whose core cannot be derived for a
    /// data reason a retry does not change (<see cref="CoreAncestorResult.IsPermanent"/>) is <see cref="ToDoKind.CoreRefused"/>, one
    /// filed under a non-core record keeps its own team (<see cref="FallbackReason.FiledNonCore"/>); a PERSONAL To Do (filed under
    /// nothing) is its one eligible assignee's (<see cref="ToDoKind.Assignee"/>, <see cref="ToDoClass.AssigneeUserId"/> set) or
    /// falls back to its own team. Which core is the To Do's CURRENT one (<see cref="CoreAncestorResolver"/>), never a row's
    /// decision context. UAC task 194b fix round (F1-c): static and shared, so the ownership reconciler asks exactly this job's
    /// question (<see cref="SelectsAsync"/>).
    /// </summary>
    /// <exception cref="Exception">A Dataverse fault reading the To Do, its links or its core propagates (Unverified).</exception>
    internal static async Task<ToDoClass> ClassifyToDoAsync(
        IGenericEntityService entities, AssignedAccessStore links, CoreAncestorResolver coreAncestors, Guid todoId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(coreAncestors);
        var assignee = await PersonalToDoAssignee.ClassifyAsync(entities, links, todoId, ct).ConfigureAwait(false);
        switch (assignee.Outcome)
        {
            case PersonalToDoAssignee.Outcome.ToDoNotFound:
                return new ToDoClass(ToDoKind.Gone);
            case PersonalToDoAssignee.Outcome.Filed:
            {
                // Read-only (GitHub #2002): the answer only classifies the To Do; nothing is written against it, so an unfiled one is not a Warning.
                var core = await coreAncestors.ResolveStampsAsync(TodoTable, todoId, ct, CoreAncestorIntent.ReadOnly).ConfigureAwait(false);
                if (!core.Succeeded && core.IsPermanent)
                    return new ToDoClass(ToDoKind.CoreRefused, Detail: core.Error);
                if (!core.Succeeded)
                    throw new InvalidOperationException($"the core record of the To Do could not be derived ({core.Error})");
                return core.Stamps.Count > 0
                    ? new ToDoClass(ToDoKind.Cored)
                    : new ToDoClass(ToDoKind.Fallback, FallbackReason.FiledNonCore);
            }

            case PersonalToDoAssignee.Outcome.NoAssignedTo or PersonalToDoAssignee.Outcome.NoLinkedUser:
                return new ToDoClass(ToDoKind.Fallback, FallbackReason.NoAssignee);
            case PersonalToDoAssignee.Outcome.SeveralUsers:
                return new ToDoClass(ToDoKind.Fallback, FallbackReason.Ambiguous);
            case PersonalToDoAssignee.Outcome.OnlyIneligibleUsers:
                return new ToDoClass(ToDoKind.Fallback, FallbackReason.Disabled);
            default:
                return new ToDoClass(ToDoKind.Assignee, AssigneeUserId: assignee.UserId!.Value);
        }
    }

    /// <summary>Whether this job keeps the rows of a To Do it classified so (<see cref="ClassifyToDoAsync"/>): a personal To Do's
    /// and a non-core-filed To Do's, never a cored, refused, gone or unverified one's.</summary>
    internal static bool KeepsRowsOf(ToDoKind kind) => kind is ToDoKind.Assignee or ToDoKind.Fallback;

    /// <summary>Uac-r2 F1: a row whose own live core stamp (<paramref name="stampColumns"/>, the
    /// <c>sprk_regarding{matter,project,workassignment,servicerequest}</c> lookups) is set is filed: the re-file rule owns it.</summary>
    internal static bool HasLiveCoreStamp(Entity row, IEnumerable<string> stampColumns) =>
        stampColumns.Any(c => row.GetAttributeValue<EntityReference>(c) is { } r && r.Id != Guid.Empty);

    /// <summary>A user-owned row is skipped: this job never re-owns one (it keeps its creator).</summary>
    internal static bool IsOwnedByUser(Entity row) =>
        row.GetAttributeValue<EntityReference>(OwningUserColumn) is { } user && user.Id != Guid.Empty;

    /// <summary>
    /// UAC task 194b fix round (F1-c): THE predicate — whether this job keeps <paramref name="row"/> (a Signal or Decision Record,
    /// read with every column): it names a To Do (<see cref="RegardingTodoColumn"/>), carries no live core stamp
    /// (<see cref="HasLiveCoreStamp"/>), is not owned by a user (<see cref="IsOwnedByUser"/>), and its To Do is one whose rows
    /// this job keeps (<see cref="ClassifyToDoAsync"/>, <see cref="KeepsRowsOf"/>) — the steps <see cref="DecideRowAsync"/>
    /// takes for each row before it asks the row's parents, with no memo. The
    /// ownership reconciler (<c>RecordOwnershipContext.ForExistingRowAsync</c>) reports such a row and never moves it.
    /// </summary>
    /// <exception cref="Exception">A Dataverse fault propagates (the row is not decided: the job's Unverified).</exception>
    internal static async Task<bool> SelectsAsync(
        Entity row, IGenericEntityService entities, AssignedAccessStore links, CoreAncestorResolver coreAncestors, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.GetAttributeValue<EntityReference>(RegardingTodoColumn) is not { } todo || todo.Id == Guid.Empty)
            return false;
        if (HasLiveCoreStamp(row, CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute)) || IsOwnedByUser(row))
            return false;
        return KeepsRowsOf((await ClassifyToDoAsync(entities, links, coreAncestors, todo.Id, ct).ConfigureAwait(false)).Kind);
    }

    /// <summary>The assignee's business-unit team (D-134 via <see cref="RecordOwnershipContext.ForUserTeam"/>), once per user per run.</summary>
    private async Task<ToDoAnswer> AssigneeAnswerAsync(Run run, Guid todoId, Guid userId, CancellationToken ct)
    {
        if (!run.UserTeams.TryGetValue(userId, out var team))
        {
            team = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForUserTeam(userId), ct).ConfigureAwait(false);
            run.UserTeams[userId] = team;
        }

        return new ToDoAnswer(todoId, ToDoKind.Assignee, AssigneeUserId: userId, AssigneeTeam: team);
    }

    /// <summary>One row: a stale plan, or <c>null</c> (counted) when it is not this job's to write or already right.</summary>
    private async Task<StaleRow?> DecideRowAsync(Run run, ToDoAnswer todo, Entity row, CancellationToken ct)
    {
        var table = row.LogicalName;
        var counts = run.Counts;
        switch (todo.Kind)
        {
            case ToDoKind.Gone: counts.SkippedGone++; return null;
            case ToDoKind.Cored: counts.SkippedCored++; return null;
            case ToDoKind.CoreRefused: counts.SkippedRefused++; return null;
            case ToDoKind.Unverified:
                counts.Unverified++;
                run.Unsettled.Add(todo.TodoId);
                return null;
        }

        // F1: the row's own live core stamp means it is filed; the re-file rule owns it.
        if (!KeepsRowsOf(todo.Kind) || HasLiveCoreStamp(row, run.Hosts(table).Stamps))
        {
            counts.SkippedCored++;
            return null;
        }

        if (IsOwnedByUser(row))
        {
            counts.SkippedUserOwned++;
            return null;
        }

        if (row.GetAttributeValue<EntityReference>(OwningTeamColumn) is not { } owning || owning.Id == Guid.Empty)
        {
            counts.Unverified++;
            run.Unsettled.Add(todo.TodoId);
            _logger.LogWarning("{Prefix} {Table} {Id}: its owner could not be read; not decided.", LogPrefix, table, row.Id);
            return null;
        }

        try
        {
            // Secure wins (D-134 item 7): every parent the row sits under, asked together, the To Do first (primary).
            var parents = ParentsOf(todo.TodoId, row, run.Hosts(table).Parents);
            var answer = await ParentsAnswerAsync(run, parents, ct).ConfigureAwait(false);
            var plan = Expected(run, todo, answer, table, row.Id);
            if (plan is not { } expected)
                return null;

            if (owning.Id == expected)
            {
                counts.AlreadyApplied++;
                return null;
            }

            if (await IsOwnedInSecureBusinessUnitAsync(run, row, owning.Id, ct).ConfigureAwait(false))
            {
                counts.SkippedSecure++;
                return null;
            }

            if (string.Equals(table, DecisionRecordTable, StringComparison.Ordinal))
            {
                // The Decision Record writer also asks about the subject of every Signal the record resolved (uac-r2 F1): one
                // of them under a secure record makes the record secure. Read before any write; only for a row about to move.
                var subjects = await ResolvedSignalSubjectsAsync(run, row.Id, ct).ConfigureAwait(false);
                var widened = parents.Concat(subjects).Distinct().ToList();
                if (widened.Count > parents.Count)
                {
                    answer = await ParentsAnswerAsync(run, widened, ct).ConfigureAwait(false);
                    if (Expected(run, todo, answer, table, row.Id) is not { } narrowed)
                        return null;
                    expected = narrowed;
                    if (owning.Id == expected)
                    {
                        counts.AlreadyApplied++;
                        return null;
                    }
                }
            }

            return new StaleRow(table, row.Id, todo.TodoId, owning.Id, expected, todo.Fallback?.ToString() ?? "assignee");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.Unverified++;
            run.Unsettled.Add(todo.TodoId);
            _logger.LogWarning(ex, "{Prefix} {Table} {Id}: its parents' owner or the Secure Record team could not be read; not "
                + "decided. correlationId={CorrelationId}", LogPrefix, table, row.Id, run.Context.CorrelationId);
            return null;
        }
    }

    /// <summary>
    /// The team the row should have, or <c>null</c> (counted) when nothing may be written: the parents' answer refuses or is
    /// secure (secure wins), or the assignee's team was refused. Otherwise the assignee's team for a personal To Do with one
    /// eligible assignee, else the parents' answer (the To Do's own team).
    /// </summary>
    private Guid? Expected(Run run, ToDoAnswer todo, RecordOwnerResolution parents, string table, Guid id)
    {
        if (parents.IsSecureOwner)
        {
            run.Counts.SkippedSecure++;
            return null;
        }

        if (parents.IsRefused || !parents.IsOwned)
        {
            run.Counts.SkippedRefused++;
            if (IsConfigurationRefusal(parents.RefusalCode))
                run.Counts.ConfigurationRefusals++;
            _logger.LogWarning("{Prefix} {Table} {Id}: the ownership resolver refused its parents ({Code}: {Reason}); not written.",
                LogPrefix, table, id, parents.RefusalCode, parents.Reason);
            return null;
        }

        if (todo.Kind != ToDoKind.Assignee)
            return parents.OwningTeamId;

        if (todo.AssigneeTeam is { IsOwned: true, IsSecureOwner: false } team)
            return team.OwningTeamId;

        // The assignee's own business-unit team cannot be used (no single default team, a unit the resolver refuses, or an
        // assignee in the Secure Record unit): the To Do's rows cannot follow its assignee, which is this job's whole point, so
        // the run is partial and says so (uac-r2 K4), never a clean "nothing to do".
        run.Counts.SkippedRefused++;
        run.Counts.AssigneeTeamRefusals++;
        if (IsConfigurationRefusal(todo.AssigneeTeam?.RefusalCode))
            run.Counts.ConfigurationRefusals++;
        _logger.LogWarning("{Prefix} {Table} {Id}: the business-unit team of the assignee {UserId} was refused ({Code}: {Reason}); "
            + "not written.", LogPrefix, table, id, todo.AssigneeUserId, todo.AssigneeTeam?.RefusalCode, todo.AssigneeTeam?.Reason);
        return null;
    }

    /// <summary>The To Do first (the primary parent: an ordinary answer is its business unit's team), then every ownership-parent
    /// lookup the row carries.</summary>
    private static List<RecordOwnershipParent> ParentsOf(Guid todoId, Entity row, IReadOnlyList<string> parentColumns)
    {
        var parents = new List<RecordOwnershipParent> { new(TodoTable, todoId) };
        foreach (var column in parentColumns)
        {
            if (row.GetAttributeValue<EntityReference>(column) is { } r && r.Id != Guid.Empty)
                parents.Add(new RecordOwnershipParent(SecureChildLineage.Children[row.LogicalName].Lookups[column], r.Id));
        }

        return parents.Distinct().ToList();
    }

    /// <summary>The resolver's answer for one parent list, once per run (the primary parent first in the key).</summary>
    private async Task<RecordOwnerResolution> ParentsAnswerAsync(Run run, IReadOnlyList<RecordOwnershipParent> parents, CancellationToken ct)
    {
        var key = parents[0] + "|" + string.Join(",", parents.Skip(1).Select(p => p.ToString()).OrderBy(s => s, StringComparer.Ordinal));
        if (!run.ParentAnswers.TryGetValue(key, out var answer))
        {
            answer = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForParents(parents), ct).ConfigureAwait(false);
            run.ParentAnswers[key] = answer;
        }

        return answer;
    }

    /// <summary>
    /// uac-r2's Secure guard (<see cref="SecureChildReconciler.ReownOrdinaryRowAsync"/>: "never OUT of the Secure Record business
    /// unit"), asked BEFORE a row is listed stale, so report-only never lists a re-own the reconciler would refuse: the row's
    /// own business unit, or its owning team's, is the Secure Record unit (ANY team there, a per-family team included, not
    /// only the named owner team), with the unit identified exactly as the reconciler identifies it
    /// (<see cref="SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync"/>). The named owner team is also checked, as
    /// before. More than one unit carrying the Secure Record name, or a fault, throws: the row is not decided.
    /// </summary>
    private async Task<bool> IsOwnedInSecureBusinessUnitAsync(Run run, Entity row, Guid owningTeamId, CancellationToken ct)
    {
        if (!run.SecureBuRead)
        {
            var bu = await SecureChildShareSynchronizer.ResolveSecureBusinessUnitAsync(run.Entities, _configuration, ct)
                .ConfigureAwait(false);
            if (bu.Ambiguous)
                throw new InvalidOperationException("more than one business unit carries the Secure Record name, so whether the row is "
                    + "owned in it cannot be told");
            run.SecureBu = bu.Id;
            run.SecureBuRead = true;
        }

        if (run.SecureBu is { } secureBu)
        {
            if (row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn)?.Id == secureBu)
                return true;

            if (!run.TeamUnits.TryGetValue(owningTeamId, out var teamBu))
            {
                var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 1, NoLock = true };
                query.Criteria.AddCondition("teamid", ConditionOperator.Equal, owningTeamId);
                teamBu = (await run.Entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault()
                    ?.GetAttributeValue<EntityReference>("businessunitid")?.Id;
                run.TeamUnits[owningTeamId] = teamBu;
            }

            if (teamBu == secureBu)
                return true;
        }

        return await IsSecureTeamAsync(run, owningTeamId, ct).ConfigureAwait(false);
    }

    /// <summary>Whether a team is the Secure Record owner team, once per team per run. A fault propagates (not decided).</summary>
    private static async Task<bool> IsSecureTeamAsync(Run run, Guid teamId, CancellationToken ct)
    {
        if (!run.SecureTeams.TryGetValue(teamId, out var secure))
        {
            secure = await run.Reconciler.IsSecureOwnerTeamAsync(teamId, ct).ConfigureAwait(false);
            run.SecureTeams[teamId] = secure;
        }

        return secure;
    }

    /// <summary>The subjects of every Signal a Decision Record resolved (each Signal's ownership-parent lookups).</summary>
    private static async Task<List<RecordOwnershipParent>> ResolvedSignalSubjectsAsync(Run run, Guid recordId, CancellationToken ct)
    {
        // Without the Signal table's columns the subjects cannot be asked about, and a record with none would read as ordinary:
        // not decided (Unverified), never written past an unchecked secure subject.
        if (!run.HostMap.ContainsKey(SignalTable))
            throw new InvalidOperationException("the Signal columns were not probed, so the record's resolved subjects cannot be read");
        var lineage = SecureChildLineage.Children[SignalTable].Lookups;
        var columns = run.Hosts(SignalTable).Parents;
        var query = new QueryExpression(SignalTable) { ColumnSet = new ColumnSet(columns.ToArray()), NoLock = true };
        query.Criteria.AddCondition(SignalDecisionRecordColumn, ConditionOperator.Equal, recordId);
        var signals = await run.Entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return signals.Entities
            .SelectMany(s => columns
                .Select(c => s.GetAttributeValue<EntityReference>(c) is { } r && r.Id != Guid.Empty
                    ? new RecordOwnershipParent(lineage[c], r.Id)
                    : null))
            .OfType<RecordOwnershipParent>()
            .ToList();
    }

    // ── Writing ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One re-own through uac-r2's entry point, under the scaffold's claim and per-run marker.</summary>
    private async Task RepairAsync(Run run, IIdempotencyService idempotency, StaleRow row, CancellationToken ct)
    {
        var counts = run.Counts;
        var claimKey = $"{JobIdConstant}:{row.Table}:{row.Id:N}:{row.ToTeam:N}";
        var markerKey = $"{claimKey}:{run.Context.RunId:N}";
        var deniedKey = $"{JobIdConstant}:denied:{row.Table}:{row.Id:N}:{row.ToTeam:N}";

        if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
        {
            counts.AlreadyApplied++;
            return;
        }

        if (await idempotency.IsEventProcessedAsync(deniedKey, ct).ConfigureAwait(false))
        {
            // 0x80040299 is a defined outcome: one attempt per row and team a day, never every tick.
            counts.AccessDeniedHeld++;
            return;
        }

        if (!await idempotency.TryAcquireProcessingLockAsync(claimKey, RepairClaimDuration, ct).ConfigureAwait(false))
        {
            counts.ClaimHeld++;
            run.Unsettled.Add(row.TodoId);
            return;
        }

        var completed = false;
        var denied = false;
        try
        {
            // Check again UNDER the claim: the claim is check-then-set and fails open (#984).
            if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
            {
                counts.AlreadyApplied++;
                return;
            }

            OrdinaryRowReownResult result;
            try
            {
                result = await run.Reconciler.ReownOrdinaryRowAsync(row.Table, row.Id, row.ToTeam, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.RepairFailures++;
                run.Unsettled.Add(row.TodoId);
                _logger.LogError(ex, "{Prefix} Re-own of {Table} {Id} to team {Team} faulted. correlationId={CorrelationId}",
                    LogPrefix, row.Table, row.Id, row.ToTeam, run.Context.CorrelationId);
                return;
            }

            switch (result.Outcome)
            {
                case OrdinaryRowReownOutcome.Reowned:
                    counts.Reowned++;
                    completed = true;
                    _logger.LogInformation("{Prefix} re-owned {Table} {Id}: team {From} -> team {To} (To Do {TodoId}). correlationId={CorrelationId}",
                        LogPrefix, row.Table, row.Id, row.FromTeam, row.ToTeam, row.TodoId, run.Context.CorrelationId);
                    break;
                case OrdinaryRowReownOutcome.AlreadyOwned:
                    counts.AlreadyApplied++;
                    completed = true;
                    break;
                case OrdinaryRowReownOutcome.RefusedSecure:
                    counts.SkippedSecure++;
                    completed = true;
                    break;
                case OrdinaryRowReownOutcome.RefusedNotTeamOwned:
                    counts.SkippedUserOwned++;
                    completed = true;
                    break;
                case OrdinaryRowReownOutcome.NotFound:
                    counts.SkippedGone++;
                    completed = true;
                    break;
                case OrdinaryRowReownOutcome.Conflict:
                    // Nothing written: the row changed after UAC's guard read it. Transient; re-evaluated next run.
                    counts.SkippedConflict++;
                    run.Unsettled.Add(row.TodoId);
                    break;
                default:
                    if (IsPrivilegeRefusal(result.Detail))
                    {
                        counts.AccessDenied++;
                        denied = true;
                        _logger.LogWarning("{Prefix} Re-own of {Table} {Id} to team {Team} was refused by Dataverse for lack of a "
                            + "privilege ({Detail}); held for {Hours} h. correlationId={CorrelationId}", LogPrefix, row.Table, row.Id,
                            row.ToTeam, result.Detail, AccessDeniedBackoff.TotalHours, run.Context.CorrelationId);
                    }
                    else
                    {
                        counts.RepairFailures++;
                        run.Unsettled.Add(row.TodoId);
                        _logger.LogError("{Prefix} Re-own of {Table} {Id} to team {Team} failed: {Detail}. correlationId={CorrelationId}",
                            LogPrefix, row.Table, row.Id, row.ToTeam, result.Detail, run.Context.CorrelationId);
                    }

                    break;
            }
        }
        finally
        {
            try
            {
                if (completed)
                    await idempotency.MarkEventAsProcessedAsync(markerKey, RepairMarkerLifetime, CancellationToken.None).ConfigureAwait(false);
                if (denied)
                    await idempotency.MarkEventAsProcessedAsync(deniedKey, AccessDeniedBackoff, CancellationToken.None).ConfigureAwait(false);
                await idempotency.ReleaseProcessingLockAsync(claimKey, CancellationToken.None).ConfigureAwait(false);
            }
            // The marker and the claim are advisory: a write that stuck is never undone because they did not.
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "{Prefix} Claim bookkeeping for {Key} failed; it expires on its own", LogPrefix, claimKey);
            }
        }
    }

    /// <summary>
    /// A refusal that comes from the tenant's configuration, not from one row's data (the Secure Record unit or its team cannot
    /// be named; a business unit has no single default team). It refuses every row alike, so a run that meets one is partial,
    /// never a clean "nothing to do".
    /// </summary>
    private static bool IsConfigurationRefusal(string? code) =>
        code is RecordOwnerRefusal.SecureBusinessUnitAmbiguous
            or RecordOwnerRefusal.SecureOwnerTeamUnresolved
            or RecordOwnerRefusal.NoDefaultOwnerTeam;

    /// <summary>
    /// Whether a Failed re-own is Dataverse's privilege refusal (F2: the target team's role lacks Read). The reconciler's
    /// conditional PATCH (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>) ends in <c>EnsureSuccessStatusCode</c>, so on
    /// the Web API path the refusal reaches <c>Detail</c> as the HTTP status text "403 (Forbidden)", with no 0x80040299 code;
    /// an SDK-path fault carries the code or "Privilege Check" text (<see cref="SignalWriter.IsAccessDeniedMessage"/>).
    /// </summary>
    internal static bool IsPrivilegeRefusal(string? detail) =>
        SignalWriter.IsAccessDeniedMessage(detail)
        || (detail is not null && detail.Contains("403 (Forbidden)", StringComparison.OrdinalIgnoreCase));

    // ── The nightly recompute's schedule and cursors ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Due when an admin asks (<c>pass=full</c>), when an earlier run left a cursor (continue it), or on the first run at or after
    /// <see cref="NightlyHourUtc"/> UTC on a day whose recompute is not yet done.
    /// </summary>
    private async Task<bool> IsFullPassDueAsync(IDistributedCache? cursors, JobRunContext context, DateTime runStart, CancellationToken ct)
    {
        if (context.Trigger == JobRunTrigger.ManualAdmin
            && context.Parameters.TryGetValue(PassParameter, out var pass)
            && string.Equals(pass?.ToString(), "full", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var table in Tables)
        {
            if (await ReadCursorAsync(cursors, CursorKey(table), ct).ConfigureAwait(false) is not null)
                return true;
        }

        if (runStart.Hour < NightlyHourUtc)
            return false;

        var today = DateOnly.FromDateTime(runStart);
        if (_lastFullPass == today)
            return false;

        if (cursors is not null)
        {
            try
            {
                var done = await cursors.GetStringAsync(FullPassDoneKey, ct).ConfigureAwait(false);
                if (DateOnly.TryParseExact(done, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day == today)
                {
                    _lastFullPass = today;
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "{Prefix} Whether today's recompute ran could not be read; this instance's memory decides.", LogPrefix);
            }
        }

        return true;
    }

    private async Task MarkFullPassDoneAsync(IDistributedCache? cursors, DateTime runStart, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(runStart);
        _lastFullPass = today;
        if (cursors is null)
            return;

        try
        {
            await cursors.SetStringAsync(FullPassDoneKey, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CursorLifetime }, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} Today's recompute could not be recorded as done; another instance may repeat it.", LogPrefix);
        }
    }

    private static string CursorKey(string table) => $"{JobIdConstant}:cursor:{table}";

    private const string FullPassDoneKey = JobIdConstant + ":full-pass-done";

    /// <summary>Where an earlier run's nightly scan of a table stopped, or <c>null</c> (start at the top).</summary>
    private async Task<Guid?> ReadCursorAsync(IDistributedCache? cursors, string key, CancellationToken ct)
    {
        if (cursors is null)
            return null;

        try
        {
            var raw = await cursors.GetStringAsync(key, ct).ConfigureAwait(false);
            return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} The scan cursor {Key} could not be read; this run starts at the top.", LogPrefix, key);
            return null;
        }
    }

    private async Task<bool> WriteCursorAsync(IDistributedCache? cursors, string key, Guid? lastId, CancellationToken ct)
    {
        if (cursors is null || lastId is not { } id)
            return false;

        try
        {
            await cursors.SetStringAsync(key, id.ToString("D", CultureInfo.InvariantCulture),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CursorLifetime }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "{Prefix} The scan cursor {Key} could not be saved; the next run starts at the top.", LogPrefix, key);
            return false;
        }
    }

    private async Task ClearCursorAsync(IDistributedCache? cursors, string key, CancellationToken ct)
    {
        if (cursors is null)
            return;

        try
        {
            await cursors.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "{Prefix} The scan cursor {Key} could not be cleared; it expires on its own.", LogPrefix, key);
        }
    }

    // ── Types ───────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>One run's reads, memo and counts.</summary>
    private sealed class Run(
        IGenericEntityService entities, AssignedAccessStore links, SecureChildReconciler reconciler,
        RunCounts counts, List<string> problems, JobRunContext context)
    {
        public IGenericEntityService Entities { get; } = entities;
        public AssignedAccessStore Links { get; } = links;
        public SecureChildReconciler Reconciler { get; } = reconciler;
        public RunCounts Counts { get; } = counts;
        public List<string> Problems { get; } = problems;
        public JobRunContext Context { get; } = context;
        public Dictionary<Guid, ToDoAnswer> ToDos { get; } = [];
        public Dictionary<Guid, RecordOwnerResolution> UserTeams { get; } = [];
        public Dictionary<string, RecordOwnerResolution> ParentAnswers { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, bool> SecureTeams { get; } = [];
        public Dictionary<Guid, Guid?> TeamUnits { get; } = [];

        /// <summary>The Secure Record business unit, read once per run (<see cref="SecureBuRead"/>), as the reconciler reads it.</summary>
        public Guid? SecureBu { get; set; }
        public bool SecureBuRead { get; set; }

        /// <summary>To Dos with work this run could not settle: carried to the next run.</summary>
        public HashSet<Guid> Unsettled { get; } = [];

        public Dictionary<string, HostColumns> HostMap { get; } = new(StringComparer.Ordinal);

        public HostColumns Hosts(string table) => HostMap.TryGetValue(table, out var h) ? h : HostColumns.Empty;

        public void ScanFailed(string problem, Exception? ex, ILogger logger)
        {
            Counts.ScanFailures++;
            Problems.Add(problem);
            logger.LogError(ex, "{Prefix} {Problem} The run is FAILED. correlationId={CorrelationId}", LogPrefix, problem, Context.CorrelationId);
        }
    }

    private sealed record HostColumns(IReadOnlyList<string> Parents, IReadOnlyList<string> Stamps)
    {
        public static readonly HostColumns Empty = new([], []);
    }

    private sealed record RecentWindow(List<Guid> ToDos, bool Listed, DateTime NextWatermark, Guid? NextAfter);

    internal enum ToDoKind
    {
        /// <summary>Personal, with one eligible assignee: their team.</summary>
        Assignee,

        /// <summary>The To Do's own team: personal with no single eligible assignee, or filed under a non-core record (K2).</summary>
        Fallback,

        /// <summary>Filed under a core record: the re-file rule's.</summary>
        Cored,

        /// <summary>Its core cannot be derived for a data reason a retry does not change (two cores of one root, a missing
        /// row): refused, not carried.</summary>
        CoreRefused,

        /// <summary>The To Do no longer exists.</summary>
        Gone,

        /// <summary>A read faulted: nothing is written.</summary>
        Unverified,
    }

    internal enum FallbackReason
    {
        NoAssignee,
        Ambiguous,
        Disabled,

        /// <summary>Not personal: filed under a non-core record, so it keeps its own team (D-135 K2).</summary>
        FiledNonCore,
    }

    /// <summary>A To Do as <see cref="ClassifyToDoAsync"/> classified it (no owner question asked).</summary>
    internal sealed record ToDoClass(ToDoKind Kind, FallbackReason? Fallback = null, Guid? AssigneeUserId = null, string? Detail = null);

    private sealed record ToDoAnswer(
        Guid TodoId,
        ToDoKind Kind,
        FallbackReason? Fallback = null,
        Guid? AssigneeUserId = null,
        RecordOwnerResolution? AssigneeTeam = null,
        string? Detail = null);

    private sealed record StaleRow(string Table, Guid Id, Guid TodoId, Guid FromTeam, Guid ToTeam, string Fallback);

    private sealed class RunCounts
    {
        public bool FullPass;
        public int RecentToDos;
        public int CarriedToDos;
        public int Scanned;
        public int Stale;
        public int Reowned;
        public int AlreadyApplied;
        public int SkippedCored;
        public int FiledNonCore;
        public int SkippedSecure;
        public int SkippedRefused;
        public int ConfigurationRefusals;
        public int AssigneeTeamRefusals;
        public int SkippedUserOwned;
        public int SkippedGone;
        public int SkippedConflict;
        public int FallbackNoAssignee;
        public int FallbackAmbiguous;
        public int FallbackDisabled;
        public int AccessDenied;
        public int AccessDeniedHeld;
        public int ClaimHeld;
        public int RepairFailures;
        public int Unverified;
        public int ScanFailures;
        public bool Truncated;
        public bool Resumed;
        public List<string> StaleSample { get; } = [];
    }
}
