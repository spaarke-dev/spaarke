using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The safety net for the core-ancestor stamp (unified-access-control-r2 task 156; owner round 3 R3/R4 — access changes
/// take minutes, never hours; owner round 4 item 5 — option b): every 5 minutes, find each child whose stamp differs from
/// the CURRENT root of the record it is filed under, and re-stamp it.
/// </summary>
/// <remarks>
/// <para><b>What it repairs.</b> The BFF's own re-file paths re-stamp in the same operation
/// (<see cref="CoreAncestorRestamper.AfterWriteAsync"/>). Everything else is this job's: writes outside the BFF (ADR-002
/// WP-5 — a model-driven form, the Web API, a flow, an import, the client wizards' <c>Xrm.WebApi</c> writes and the
/// client stamp mirror, which does not derive the task-156 intermediates), a cascade that failed or was truncated, and
/// task 051 F-051-6 — a source lookup CLEARED on a form, leaving the copy behind (cleared here when it still equals the
/// cleared record's root).</para>
///
/// <para><b>One rule.</b> A row is classified with <see cref="CoreAncestorResolver.ClassifyStampSource"/> and planned
/// with <see cref="CoreAncestorRestamper.PlanStamp"/> — the same code the cascade and the storage resolver's comparison
/// use — and repaired with <see cref="CoreAncestorRestamper.RestampChildAsync"/>, which re-reads the row, re-derives its
/// source's root at write time and cascades to the row's own children. So a stale event and the to-dos under it converge
/// in one run, and a second run changes nothing.</para>
///
/// <para><b>Fail direction</b> (ADR-003, inverted for a writer, as <c>ExternalAccessReconciliationJob</c>): a scan that
/// fails writes nothing and the RUN IS RECORDED FAILED — never "0 stale" (that includes the <c>sprk_recordtype_ref</c>
/// read the orphan clause is bounded by). A source whose root cannot be derived is not guessed: its children are counted
/// unverified and the run is partial.</para>
///
/// <para><b>What it reads</b> (verifier round 1 item 4): every row filed under an intermediate (a source column set — the
/// rows that carry a copy), plus the F-051-6 candidates only — a row filed under nothing whose pair names an intermediate
/// by type, or carries a pair id with no type (the communication / agreement shape). Rows a regarding builder filed
/// directly to a root are never read. A scan stopped by the page bound records where it stopped (the distributed cache)
/// and the next run continues there, so every candidate is checked within a few runs however large the table; such a run
/// reports partial, never ok.</para>
///
/// <para><b>Idempotency</b> (ADR-036 A1 rule 3): each repair takes an atomic claim keyed by the row AND the stamp it
/// plans, writes, then marks the repair complete FOR THE RUN; a failed repair releases the claim. A retry of the same run
/// does not re-apply a marked repair; a LATER run repairs a row that was put back out of band after the marked repair
/// (a new staleness, not a duplicate). <b>Retry</b> (A1 rule 4): it does not throw — a failed scan or repair is picked up
/// whole by the next tick 5 minutes later, which is sooner than the retry policy's backoff would add value. <b>Heartbeat</b>
/// (A1 rule 5): one structured line per attempt, including one with nothing to do.</para>
///
/// <para><b>Writes are on by default</b> (the owner wants stamps fresh within minutes). For a dry run set
/// <see cref="WritesEnabledConfigKey"/> to <c>false</c>: the job then reports every stale row (with its before / after
/// state) and writes nothing. An unparseable value is report-only too. The admin <c>disable</c> endpoint stops it
/// (ADR-036).</para>
///
/// <para><b>Placement</b> (ADR-052; CLAUDE.md §10): in the BFF on the in-process <c>Spaarke.Scheduling</c> host — BFF
/// domain code (<see cref="CoreAncestorResolver"/>, the restamper) over BFF-owned tables, BFF identity, low volume (live
/// 2026-10-02: 14 to-dos, 1 event, 5 communications and 0 analyses are filed under an intermediate at all). The one
/// Functions-leaning signal, one dispatch per schedule, is met by the host's lease. No new scheduler, store or package.</para>
/// </remarks>
public sealed class CoreAncestorStampReconciliationJob : IScheduledJob
{
    public const string JobIdConstant = "core-ancestor-stamp-reconciliation";

    /// <summary>Every 5 minutes (owner round 3 R3/R4: the safety net runs at most every 5 minutes).</summary>
    internal const string DefaultCronSchedule = "*/5 * * * *";

    /// <summary>The dry-run switch: <c>false</c> (or an unparseable value) = report only. Absent = writes on.</summary>
    internal const string WritesEnabledConfigKey = "CoreAncestor:StampReconciliation:WritesEnabled";

    internal const int PageSize = 1000;
    internal const int MaxPages = 20;

    /// <summary>
    /// How long a table's continuation point survives (see <see cref="ScanAsync"/>): far longer than the 5-minute cadence,
    /// short enough that a cursor nobody resumes (the job disabled) is forgotten and the next scan starts from the top.
    /// </summary>
    private static readonly TimeSpan CursorLifetime = TimeSpan.FromHours(6);

    /// <summary>Rows per scan page (<see cref="PageSize"/>). Settable only so a test can reach the page bound.</summary>
    internal int ScanPageSize { get; init; } = PageSize;

    /// <summary>Pages per table per run (<see cref="MaxPages"/>). Settable only so a test can reach the page bound.</summary>
    internal int ScanMaxPages { get; init; } = MaxPages;

    internal const string StatusOk = "ok";
    internal const string StatusPartial = "partial";
    internal const string StatusError = "error";
    internal const string StatusCancelled = "cancelled";

    internal const string LogPrefix = "[CORE-ANCESTOR-RECON]";

    /// <summary>How many stale row ids <see cref="JobRunResult.ResultJson"/> lists; the full list is in the log lines.</summary>
    internal const int MaxSampledIds = 200;

    private static readonly TimeSpan RepairClaimDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RepairMarkerLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly CoreAncestorRestamper _restamper;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CoreAncestorStampReconciliationJob> _logger;

    public CoreAncestorStampReconciliationJob(
        IServiceScopeFactory scopeFactory,
        CoreAncestorResolver coreAncestors,
        CoreAncestorRestamper restamper,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<CoreAncestorStampReconciliationJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Core-Ancestor Stamp Reconciliation";

    public string Description =>
        "Re-stamps every child record whose project / matter / work assignment copy differs from the current root of the "
        + "record it is filed under (writes made outside the BFF, failed cascades, regarding lookups cleared on a form). "
        + "Runs every 5 minutes; set " + WritesEnabledConfigKey + "=false for a report-only run.";

    /// <summary>Writes are on unless the switch is present and says otherwise (an unparseable value is report-only).</summary>
    internal bool WritesEnabled
    {
        get
        {
            var raw = _configuration[WritesEnabledConfigKey];
            if (raw is null)
            {
                return true;
            }

            return bool.TryParse(raw, out var enabled) && enabled;
        }
    }

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var started = _timeProvider.GetTimestamp();
        var writesEnabled = WritesEnabled;
        var counts = new RunCounts();
        var problems = new List<string>();
        var status = StatusOk;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var entityService = scope.ServiceProvider.GetRequiredService<IGenericEntityService>();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

            // Where a truncated scan continues next run (the BFF's distributed cache — the store the idempotency claims
            // already use). Absent or failing → every run starts at the top (reported, never silent).
            var cursors = scope.ServiceProvider.GetService<IDistributedCache>();

            var stale = await FindStaleAsync(entityService, cursors, counts, problems, context, cancellationToken)
                .ConfigureAwait(false);

            counts.Stale = stale.Count;
            foreach (var row in stale)
            {
                // The before-state, for EVERY stale row, in BOTH modes, before anything is written.
                _logger.LogInformation(
                    "{Prefix} stale mode={Mode} entity={Entity} id={Id} source={Source} before=[{Before}] after=[{After}] correlationId={CorrelationId}",
                    LogPrefix, writesEnabled ? "write" : "report-only", row.Entity, row.Id, row.Source, row.Before,
                    row.After, context.CorrelationId);
            }

            if (writesEnabled)
            {
                foreach (var row in stale)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RepairAsync(idempotency, row, counts, context, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            status = StatusCancelled;
            problems.Add("Cancelled before every stale row was repaired.");
        }
        // A scheduled job's last line of defence: record the failure, never let it escape unrecorded.
        catch (Exception ex)
        {
            status = StatusError;
            problems.Add(ex.Message);
            _logger.LogError(ex, "{Prefix} Run failed — reconciliation did not complete. correlationId={CorrelationId}",
                LogPrefix, context.CorrelationId);
        }

        if (counts.ScanFailures > 0)
        {
            // ADR-003 for a writer: a scan that failed is a FAILED run, never "nothing stale".
            status = StatusError;
        }
        else if (status == StatusOk
                 && (counts.RepairFailures > 0 || counts.Unverified > 0 || counts.Truncated || counts.Resumed
                     || counts.ClaimHeld > 0))
        {
            // A run that did not check every candidate row of every table (a page bound, a continuation, an
            // unverifiable source) or did not finish every repair is never reported as a clean run.
            status = StatusPartial;
        }

        var duration = _timeProvider.GetElapsedTime(started);

        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} mode={Mode} attempt={Attempt} scanned={Scanned} stale={Stale} "
            + "repaired={Repaired} cascaded={Cascaded} alreadyApplied={AlreadyApplied} claimHeld={ClaimHeld} "
            + "repairFailures={RepairFailures} unverified={Unverified} orphanSourceGone={OrphanSourceGone} "
            + "scanFailures={ScanFailures} truncated={Truncated} resumed={Resumed} "
            + "durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, writesEnabled ? "write" : "report-only", context.Attempt, counts.Scanned, counts.Stale,
            counts.Repaired, counts.Cascaded, counts.AlreadyApplied, counts.ClaimHeld, counts.RepairFailures,
            counts.Unverified, counts.OrphanSourceGone, counts.ScanFailures, counts.Truncated, counts.Resumed,
            (long)duration.TotalMilliseconds, context.Trigger, context.RunId, context.CorrelationId);

        return new JobRunResult(
            Success: status == StatusOk,
            ErrorMessage: problems.Count > 0 ? string.Join(" ", problems) : null,
            ProcessedItems: counts.Repaired,
            Duration: duration,
            ResultJson: JsonSerializer.Serialize(
                new
                {
                    status,
                    mode = writesEnabled ? "write" : "report-only",
                    writesEnabled,
                    attempt = context.Attempt,
                    counts.Scanned,
                    counts.Stale,
                    counts.Repaired,
                    counts.Cascaded,
                    counts.AlreadyApplied,
                    counts.ClaimHeld,
                    counts.RepairFailures,
                    counts.Unverified,
                    counts.OrphanSourceGone,
                    counts.ScanFailures,
                    counts.Truncated,
                    counts.Resumed,
                    sampleStale = counts.StaleSample,
                },
                ResultJsonOptions));
    }

    /// <summary>
    /// Scan every stamped child table for rows filed under an intermediate (and rows whose source was cleared), and keep
    /// the ones whose stamp differs from their source's CURRENT root.
    /// </summary>
    private async Task<List<StaleRow>> FindStaleAsync(
        IGenericEntityService entityService,
        IDistributedCache? cursors,
        RunCounts counts,
        List<string> problems,
        JobRunContext context,
        CancellationToken ct)
    {
        var stale = new List<StaleRow>();
        var roots = new Dictionary<(string, Guid), CoreAncestorResult>();

        // The sprk_recordtype_ref rows, read ONCE per run, first: the F-051-6 scan clause is bounded by them (only a pair
        // TYPE that names an intermediate is a candidate), so an unreadable type table is a FAILED run — the copies a
        // typed pair orphaned were not looked for (never "nothing orphaned"). The other clauses still run.
        Dictionary<Guid, string>? recordTypes;
        try
        {
            recordTypes = await LoadRecordTypesAsync(entityService, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            recordTypes = null;
            counts.ScanFailures++;
            problems.Add("sprk_recordtype_ref: the regarding types could not be read, so copies orphaned by a regarding "
                         + $"cleared on a form (a typed pair) were not looked for ({ex.Message}).");
            _logger.LogError(ex, "{Prefix} The regarding types could not be read; typed F-051-6 orphans were not looked for "
                                 + "and the run is FAILED. correlationId={CorrelationId}", LogPrefix, context.CorrelationId);
        }

        foreach (var (childTable, sources) in CoreAncestorResolver.StampSourceColumns)
        {
            IReadOnlySet<string> host;
            try
            {
                host = await _coreAncestors.ProbeColumnsAsync(childTable, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.ScanFailures++;
                problems.Add($"{childTable}: column metadata unavailable, so its rows were not checked ({ex.Message}).");
                continue;
            }

            var sourceColumns = sources.Select(s => s.Column).Where(host.Contains).ToArray();
            var stampColumns = CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute)
                .Where(host.Contains).ToArray();
            if (sourceColumns.Length == 0 || stampColumns.Length == 0)
            {
                continue;
            }

            var columns = CoreAncestorRestamper.ChildColumns(childTable, host, withPairType: true);
            var parties = CoreAncestorResolver.PartyRegardingColumnNames(childTable);
            var orphans = OrphanClauseFor(sources, host, recordTypes);

            var cursorKey = $"{JobIdConstant}:cursor:{childTable}";
            var after = await ReadCursorAsync(cursors, cursorKey, ct).ConfigureAwait(false);

            ScanResult scan;
            try
            {
                scan = await ScanAsync(entityService, childTable, columns, sourceColumns, stampColumns, orphans, after, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.ScanFailures++;
                problems.Add($"{childTable}: the scan failed, so its rows were not checked ({ex.Message}).");
                _logger.LogError(ex, "{Prefix} Scan of {Table} failed; nothing was written for it and the run is FAILED. correlationId={CorrelationId}",
                    LogPrefix, childTable, context.CorrelationId);
                continue;
            }

            // Paging ACROSS runs: a scan stopped by the page bound records where it stopped, and the next run continues
            // from there — so every candidate row is checked within a few runs, however large the table, instead of the
            // same prefix forever. A run that stopped, or that only continued, did not check the whole table: partial.
            if (scan.Truncated)
            {
                counts.Truncated = true;
                var saved = await WriteCursorAsync(cursors, cursorKey, scan.LastId, ct).ConfigureAwait(false);
                problems.Add(saved
                    ? $"{childTable}: the scan stopped after {ScanMaxPages} pages of {ScanPageSize}; the next run continues after row {scan.LastId:D}."
                    : $"{childTable}: the scan stopped after {ScanMaxPages} pages of {ScanPageSize} and where it stopped could not be "
                      + "recorded, so the next run starts from the top again.");
            }
            else if (after is not null)
            {
                counts.Resumed = true;
                await ClearCursorAsync(cursors, cursorKey, ct).ConfigureAwait(false);
                problems.Add($"{childTable}: this run continued a scan an earlier run stopped (rows after {after:D}); the next run starts from the top.");
            }

            foreach (var row in scan.Rows)
            {
                counts.Scanned++;
                var decision = CoreAncestorResolver.ClassifyStampSource(childTable, row, parties);

                string intermediate;
                Guid intermediateId;
                bool orphan;

                if (decision.Kind == StampSourceKind.Source)
                {
                    intermediate = decision.Source!.Intermediate;
                    intermediateId = decision.Source.Id;
                    orphan = false;
                }
                else if (decision.Kind == StampSourceKind.NotFiledUnderAnIntermediate)
                {
                    // F-051-6 candidate: its pair names an intermediate (by type, or — untyped — by id) whose typed column
                    // is empty. The one lookup the restamper uses too.
                    var cleared = await _restamper.FindClearedSourceAsync(childTable, row, recordTypes, ct).ConfigureAwait(false);
                    if (cleared.Outcome == ClearedSourceOutcome.Gone)
                    {
                        // Deleted: the copy cannot be shown to be its copy (interpretation xvi) — counted, never a partial
                        // run that would repeat on every tick.
                        counts.OrphanSourceGone++;
                        _logger.LogWarning(
                            "{Prefix} {Entity} {Id}: its pair names {PairId}, which no intermediate holds any more; its stamp "
                            + "cannot be shown to be a copy and is left alone.", LogPrefix, childTable, row.Id, cleared.PairId);
                        continue;
                    }

                    if (cleared.Outcome == ClearedSourceOutcome.Unreadable)
                    {
                        counts.Unverified++;
                        _logger.LogWarning("{Prefix} {Entity} {Id}: {Error}; not repaired.", LogPrefix, childTable, row.Id, cleared.Error);
                        continue;
                    }

                    if (cleared.Outcome != ClearedSourceOutcome.Found)
                    {
                        continue;
                    }

                    intermediate = cleared.Intermediate!;
                    intermediateId = cleared.PairId;
                    orphan = true;
                }
                else
                {
                    continue; // a direct link, an ambiguous or an inconsistent row: nothing here is a copy to repair
                }

                if (!roots.TryGetValue((intermediate, intermediateId), out var root))
                {
                    root = await _coreAncestors.ResolveStampsAsync(intermediate, intermediateId, ct).ConfigureAwait(false);
                    roots[(intermediate, intermediateId)] = root;
                }

                if (!root.Succeeded)
                {
                    counts.Unverified++;
                    _logger.LogWarning(
                        "{Prefix} {Entity} {Id}: the root of its {Source} {SourceId} could not be derived ({Error}); not repaired.",
                        LogPrefix, childTable, row.Id, intermediate, intermediateId, root.Error);
                    continue;
                }

                var plan = orphan
                    ? CoreAncestorRestamper.PlanOrphanClear(row, root.Stamps, host, intermediateId)
                    : CoreAncestorRestamper.PlanStamp(row, CoreAncestorResolver.CarriableRootTypes(intermediate), root.Stamps, host);

                if (plan is null)
                {
                    continue;
                }

                stale.Add(new StaleRow(
                    childTable, row.Id, $"{intermediate} {intermediateId}{(orphan ? " (cleared)" : string.Empty)}",
                    Describe(row, plan.Keys), Describe(plan), PlanHash(plan)));

                if (counts.StaleSample.Count < MaxSampledIds)
                {
                    counts.StaleSample.Add($"{childTable}:{row.Id:D}");
                }
            }
        }

        return stale;
    }

    private async Task RepairAsync(
        IIdempotencyService idempotency, StaleRow row, RunCounts counts, JobRunContext context, CancellationToken ct)
    {
        // The CLAIM is keyed by the row and the value it plans, so no two runs write the same repair at once. The completion
        // MARKER is keyed to THIS run as well (the host's retries of a run share its RunId): a row put back out of band AFTER
        // a run repaired it is a new staleness, and the next run must repair it — a marker keyed by row and value alone
        // would suppress exactly that repair for the marker's whole lifetime.
        var claimKey = $"{JobIdConstant}:{row.Entity}:{row.Id:N}:{row.PlanHash}";
        var markerKey = $"{claimKey}:{context.RunId:N}";

        if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
        {
            counts.AlreadyApplied++;
            return;
        }

        if (!await idempotency.TryAcquireProcessingLockAsync(claimKey, RepairClaimDuration, ct).ConfigureAwait(false))
        {
            counts.ClaimHeld++;
            return;
        }

        var completed = false;
        try
        {
            // Check again UNDER the claim: the claim is check-then-set and fails open (#984).
            if (await idempotency.IsEventProcessedAsync(markerKey, ct).ConfigureAwait(false))
            {
                counts.AlreadyApplied++;
                return;
            }

            var report = await _restamper.RestampChildAsync(row.Entity, row.Id, ct).ConfigureAwait(false);

            if (report.ChangedRecords.Contains((row.Entity, row.Id)))
            {
                counts.Repaired++;
            }

            counts.Cascaded += report.ChangedRecords.Count(r => r != (row.Entity, row.Id));

            if (!report.Complete)
            {
                counts.RepairFailures++;
                _logger.LogWarning(
                    "{Prefix} Repair of {Entity} {Id} incomplete: {Failures}{Truncated}. correlationId={CorrelationId}",
                    LogPrefix, row.Entity, row.Id,
                    string.Join("; ", report.Failures.Select(f => $"{f.Entity} {f.Id}: {f.Reason}")),
                    report.Truncated ? " (truncated)" : string.Empty, context.CorrelationId);
                return;
            }

            completed = true;
        }
        finally
        {
            try
            {
                if (completed)
                {
                    await idempotency.MarkEventAsProcessedAsync(markerKey, RepairMarkerLifetime, CancellationToken.None)
                        .ConfigureAwait(false);
                }

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
    /// The F-051-6 part of one table's scan (verifier round 1 item 4): a row filed under NO intermediate is a candidate
    /// only when its pair could name one — so the scan reads the rows that may hold an orphaned copy, not every row a
    /// regarding builder filed directly to a project or matter (live 2026-10-02: 24 of 50 to-dos and 62 communications
    /// are filed that way, against 14 and 5 filed under an intermediate).
    /// </summary>
    /// <remarks>
    /// <para><b>Typed</b>: the pair's type is one of the <c>sprk_recordtype_ref</c> rows naming an intermediate this table
    /// is filed under (an <c>in</c> on their ids). <b>Untyped</b>: a pair id with NO type — the shape a communication or an
    /// agreement leaves, because neither has a <c>sprk_recordtype_ref</c> row (live, 2026-10-02), so the client regarding
    /// writer sets the id alone. A row filed directly to a root carries the root's type (matter, project, work assignment
    /// and service request all have one), so neither clause reads it.</para>
    /// <para>No orphan clause on a table without the pair's id or type column (the column would fault the scan). With the
    /// types unreadable (<paramref name="recordTypes"/> null — already a FAILED run) only the untyped clause runs.</para>
    /// </remarks>
    internal static OrphanClause OrphanClauseFor(
        IReadOnlyList<(string Column, string Intermediate)> sources,
        IReadOnlySet<string> host,
        IReadOnlyDictionary<Guid, string>? recordTypes)
    {
        if (!host.Contains(CoreAncestorResolver.RegardingRecordIdColumn)
            || !host.Contains(CoreAncestorResolver.RegardingRecordTypeColumn))
        {
            return OrphanClause.None;
        }

        var intermediates = sources.Select(s => s.Intermediate).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var typed = recordTypes is null
            ? []
            : recordTypes.Where(t => intermediates.Contains(t.Value)).Select(t => t.Key).OrderBy(id => id).ToArray();

        return new OrphanClause(typed, Untyped: true);
    }

    /// <summary>
    /// Rows of <paramref name="childTable"/> filed under an intermediate (any source column set), plus the F-051-6
    /// candidates <paramref name="orphans"/> describes. Ordered by the row id; continues after <paramref name="after"/>
    /// when an earlier run stopped there. Pages with a cookie to <see cref="ScanMaxPages"/>; past it the result says
    /// TRUNCATED (and where), never a silent prefix.
    /// </summary>
    private async Task<ScanResult> ScanAsync(
        IGenericEntityService entityService,
        string childTable,
        string[] columns,
        string[] sourceColumns,
        string[] stampColumns,
        OrphanClause orphans,
        Guid? after,
        CancellationToken ct)
    {
        var rows = new List<Entity>();
        string? cookie = null;

        for (var page = 1; page <= ScanMaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await entityService
                .RetrieveMultipleAsync(new FetchExpression(
                    BuildScanFetchXml(childTable, columns, sourceColumns, stampColumns, orphans, after, ScanPageSize, page, cookie)), ct)
                .ConfigureAwait(false);

            rows.AddRange(result.Entities);

            if (!result.MoreRecords)
            {
                return new ScanResult(rows, Truncated: false, LastId: null);
            }

            cookie = result.PagingCookie;
        }

        return new ScanResult(rows, Truncated: true, LastId: rows.Count > 0 ? rows[^1].Id : after);
    }

    /// <summary>The scan FetchXML for one child table (see <see cref="ScanAsync"/>). Ordered by the row id for stable paging.</summary>
    internal static string BuildScanFetchXml(
        string childTable,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<string> stampColumns,
        OrphanClause orphans,
        Guid? after,
        int pageSize,
        int page,
        string? pagingCookie)
    {
        var filedUnder = new XElement("filter", new XAttribute("type", "or"),
            sourceColumns.Select(c => Condition(c, "not-null")));

        var candidates = new XElement("filter", new XAttribute("type", "or"), filedUnder);

        // A row filed under nothing that still carries a stamp, whose pair could name the intermediate that was cleared.
        XElement OrphanBase() => new("filter", new XAttribute("type", "and"),
            sourceColumns.Select(c => Condition(c, "null")),
            new XElement("filter", new XAttribute("type", "or"), stampColumns.Select(c => Condition(c, "not-null"))));

        if (orphans.TypedIntermediateTypes.Count > 0)
        {
            var typed = OrphanBase();
            typed.Add(new XElement("condition",
                new XAttribute("attribute", CoreAncestorResolver.RegardingRecordTypeColumn),
                new XAttribute("operator", "in"),
                orphans.TypedIntermediateTypes.Select(id =>
                    new XElement("value", id.ToString("D", CultureInfo.InvariantCulture)))));
            candidates.Add(typed);
        }

        if (orphans.Untyped)
        {
            var untyped = OrphanBase();
            untyped.Add(Condition(CoreAncestorResolver.RegardingRecordIdColumn, "not-null"));
            untyped.Add(Condition(CoreAncestorResolver.RegardingRecordTypeColumn, "null"));
            candidates.Add(untyped);
        }

        var filter = candidates;
        if (after is { } continueAfter)
        {
            filter = new XElement("filter", new XAttribute("type", "and"),
                new XElement("condition",
                    new XAttribute("attribute", childTable + "id"),
                    new XAttribute("operator", "gt"),
                    new XAttribute("value", continueAfter.ToString("D", CultureInfo.InvariantCulture))),
                candidates);
        }

        var entity = new XElement("entity", new XAttribute("name", childTable),
            new XElement("attribute", new XAttribute("name", childTable + "id")),
            columns.Select(c => new XElement("attribute", new XAttribute("name", c))),
            new XElement("order", new XAttribute("attribute", childTable + "id")),
            filter);

        var fetch = new XElement("fetch",
            new XAttribute("version", "1.0"),
            new XAttribute("mapping", "logical"),
            new XAttribute("no-lock", "true"),
            new XAttribute("count", pageSize),
            new XAttribute("page", page));

        if (pagingCookie is not null)
        {
            fetch.Add(new XAttribute("paging-cookie", pagingCookie));
        }

        fetch.Add(entity);
        return fetch.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Condition(string attribute, string op)
        => new("condition", new XAttribute("attribute", attribute), new XAttribute("operator", op));

    /// <summary>Where an earlier run's scan of a table stopped, or <see langword="null"/> (start at the top).</summary>
    private async Task<Guid?> ReadCursorAsync(IDistributedCache? cursors, string key, CancellationToken ct)
    {
        if (cursors is null)
        {
            return null;
        }

        try
        {
            var raw = await cursors.GetStringAsync(key, ct).ConfigureAwait(false);
            return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Start at the top: every row is still checked, just not past the page bound this run.
            _logger.LogWarning(ex, "{Prefix} The scan cursor {Key} could not be read; this run starts at the top.", LogPrefix, key);
            return null;
        }
    }

    private async Task<bool> WriteCursorAsync(IDistributedCache? cursors, string key, Guid? lastId, CancellationToken ct)
    {
        if (cursors is null || lastId is not { } id)
        {
            return false;
        }

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
        {
            return;
        }

        try
        {
            await cursors.RemoveAsync(key, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Harmless: the cursor expires, and a stale one only makes the next run continue instead of starting over.
            _logger.LogDebug(ex, "{Prefix} The scan cursor {Key} could not be cleared; it expires on its own.", LogPrefix, key);
        }
    }

    /// <summary>The <c>sprk_recordtype_ref</c> rows (live: 14), loaded once at the start of every run.</summary>
    private static async Task<Dictionary<Guid, string>> LoadRecordTypesAsync(IGenericEntityService entityService, CancellationToken ct)
    {
        var result = await entityService.RetrieveMultipleAsync(
            new QueryExpression("sprk_recordtype_ref")
            {
                ColumnSet = new ColumnSet("sprk_recordlogicalname"),
            },
            ct).ConfigureAwait(false);

        return result.Entities
            .Where(e => e.Id != Guid.Empty && !string.IsNullOrWhiteSpace(e.GetAttributeValue<string>("sprk_recordlogicalname")))
            .ToDictionary(e => e.Id, e => e.GetAttributeValue<string>("sprk_recordlogicalname")!.Trim().ToLowerInvariant());
    }

    private static string Describe(Entity row, IEnumerable<string> columns)
        => string.Join(" ", columns.Select(c =>
            $"{c}={row.GetAttributeValue<EntityReference>(c)?.Id.ToString("D", CultureInfo.InvariantCulture) ?? "(null)"}"));

    private static string Describe(Dictionary<string, object> plan)
        => string.Join(" ", plan.Select(p =>
            $"{p.Key}={(p.Value is EntityReference r ? r.Id.ToString("D", CultureInfo.InvariantCulture) : "(null)")}"));

    /// <summary>The claim key's content part: the planned columns and values, order-independent.</summary>
    private static string PlanHash(Dictionary<string, object> plan)
    {
        var text = string.Join(",", plan
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={(p.Value is EntityReference r ? r.Id.ToString("N", CultureInfo.InvariantCulture) : "-")}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private sealed record StaleRow(string Entity, Guid Id, string Source, string Before, string After, string PlanHash);

    /// <summary>One table's scan: the rows, and — when the page bound stopped it — the last row id read.</summary>
    private sealed record ScanResult(List<Entity> Rows, bool Truncated, Guid? LastId);

    /// <summary>
    /// The F-051-6 clauses of one table's scan (<see cref="OrphanClauseFor"/>): the <c>sprk_recordtype_ref</c> ids that
    /// name an intermediate the table is filed under, and whether a pair id with no type is read.
    /// </summary>
    internal sealed record OrphanClause(IReadOnlyList<Guid> TypedIntermediateTypes, bool Untyped)
    {
        public static readonly OrphanClause None = new([], Untyped: false);
    }

    private sealed class RunCounts
    {
        public int Scanned;
        public int Stale;
        public int Repaired;
        public int Cascaded;
        public int AlreadyApplied;
        public int ClaimHeld;
        public int RepairFailures;
        public int Unverified;
        public int OrphanSourceGone;
        public int ScanFailures;
        public bool Truncated;
        public bool Resumed;
        public List<string> StaleSample { get; } = [];
    }
}
