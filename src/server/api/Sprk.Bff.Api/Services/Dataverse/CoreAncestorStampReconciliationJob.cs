using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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
/// fails writes nothing and the RUN IS RECORDED FAILED — never "0 stale". A source whose root cannot be derived is not
/// guessed: its children are counted unverified and the run is partial.</para>
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

            var stale = await FindStaleAsync(entityService, counts, problems, context, cancellationToken)
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
                 && (counts.RepairFailures > 0 || counts.Unverified > 0 || counts.Truncated || counts.ClaimHeld > 0))
        {
            status = StatusPartial;
        }

        if (counts.Truncated)
        {
            problems.Add($"A scan stopped after {MaxPages} pages of {PageSize}; later rows were not checked.");
        }

        var duration = _timeProvider.GetElapsedTime(started);

        _logger.Log(
            status == StatusOk ? LogLevel.Information : LogLevel.Warning,
            "{Prefix} heartbeat status={Status} mode={Mode} attempt={Attempt} scanned={Scanned} stale={Stale} "
            + "repaired={Repaired} cascaded={Cascaded} alreadyApplied={AlreadyApplied} claimHeld={ClaimHeld} "
            + "repairFailures={RepairFailures} unverified={Unverified} scanFailures={ScanFailures} truncated={Truncated} "
            + "durationMs={DurationMs} trigger={Trigger} runId={RunId} correlationId={CorrelationId}",
            LogPrefix, status, writesEnabled ? "write" : "report-only", context.Attempt, counts.Scanned, counts.Stale,
            counts.Repaired, counts.Cascaded, counts.AlreadyApplied, counts.ClaimHeld, counts.RepairFailures,
            counts.Unverified, counts.ScanFailures, counts.Truncated, (long)duration.TotalMilliseconds, context.Trigger,
            context.RunId, context.CorrelationId);

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
                    counts.ScanFailures,
                    counts.Truncated,
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
        RunCounts counts,
        List<string> problems,
        JobRunContext context,
        CancellationToken ct)
    {
        var stale = new List<StaleRow>();
        var roots = new Dictionary<(string, Guid), CoreAncestorResult>();
        Dictionary<Guid, string>? recordTypes = null;

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

            List<Entity> rows;
            try
            {
                rows = await ScanAsync(entityService, childTable, columns, sourceColumns, stampColumns,
                    host.Contains(CoreAncestorResolver.RegardingRecordTypeColumn), counts, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.ScanFailures++;
                problems.Add($"{childTable}: the scan failed, so its rows were not checked ({ex.Message}).");
                _logger.LogError(ex, "{Prefix} Scan of {Table} failed; nothing was written for it and the run is FAILED. correlationId={CorrelationId}",
                    LogPrefix, childTable, context.CorrelationId);
                continue;
            }

            foreach (var row in rows)
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
                    // F-051-6 candidate: the pair names an intermediate by TYPE, and its typed column is empty.
                    recordTypes ??= await LoadRecordTypesAsync(entityService, ct).ConfigureAwait(false);
                    if (!TryOrphanSource(row, childTable, recordTypes, out intermediate, out intermediateId))
                    {
                        continue;
                    }

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
    /// Rows of <paramref name="childTable"/> filed under an intermediate (any source column set), plus — when the table
    /// carries the pair's type — rows filed under none whose pair names a type and that still carry a stamp (F-051-6).
    /// Pages with a cookie to <see cref="MaxPages"/>; past it the run reports TRUNCATED rather than a silent prefix.
    /// </summary>
    private static async Task<List<Entity>> ScanAsync(
        IGenericEntityService entityService,
        string childTable,
        string[] columns,
        string[] sourceColumns,
        string[] stampColumns,
        bool hasPairType,
        RunCounts counts,
        CancellationToken ct)
    {
        var rows = new List<Entity>();
        string? cookie = null;

        for (var page = 1; page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            var result = await entityService
                .RetrieveMultipleAsync(new FetchExpression(
                    BuildScanFetchXml(childTable, columns, sourceColumns, stampColumns, hasPairType, page, cookie)), ct)
                .ConfigureAwait(false);

            rows.AddRange(result.Entities);

            if (!result.MoreRecords)
            {
                return rows;
            }

            cookie = result.PagingCookie;
        }

        counts.Truncated = true;
        return rows;
    }

    /// <summary>The scan FetchXML for one child table (see <see cref="ScanAsync"/>). Ordered by the row id for stable paging.</summary>
    internal static string BuildScanFetchXml(
        string childTable,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> sourceColumns,
        IReadOnlyList<string> stampColumns,
        bool hasPairType,
        int page,
        string? pagingCookie)
    {
        var filedUnder = new XElement("filter", new XAttribute("type", "or"),
            sourceColumns.Select(c => Condition(c, "not-null")));

        var filter = new XElement("filter", new XAttribute("type", "or"), filedUnder);

        if (hasPairType)
        {
            filter.Add(new XElement("filter", new XAttribute("type", "and"),
                sourceColumns.Select(c => Condition(c, "null")),
                Condition(CoreAncestorResolver.RegardingRecordTypeColumn, "not-null"),
                new XElement("filter", new XAttribute("type", "or"), stampColumns.Select(c => Condition(c, "not-null")))));
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
            new XAttribute("count", PageSize),
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

    /// <summary>The <c>sprk_recordtype_ref</c> rows (live: 14), loaded once per run when an F-051-6 candidate needs one.</summary>
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

    private static bool TryOrphanSource(
        Entity row, string childTable, Dictionary<Guid, string> recordTypes, out string intermediate, out Guid intermediateId)
    {
        intermediate = string.Empty;
        intermediateId = Guid.Empty;

        var raw = row.GetAttributeValue<string>(CoreAncestorResolver.RegardingRecordIdColumn);
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw.Trim(), out var pairId) || pairId == Guid.Empty
            || row.GetAttributeValue<EntityReference>(CoreAncestorResolver.RegardingRecordTypeColumn) is not { } typeRef
            || !recordTypes.TryGetValue(typeRef.Id, out var pairEntity)
            || !CoreAncestorResolver.StampSourceColumns[childTable].Any(s =>
                string.Equals(s.Intermediate, pairEntity, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        intermediate = pairEntity;
        intermediateId = pairId;
        return true;
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
        public int ScanFailures;
        public bool Truncated;
        public List<string> StaleSample { get; } = [];
    }
}
