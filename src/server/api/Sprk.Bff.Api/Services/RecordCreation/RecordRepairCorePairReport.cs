using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The repair pass's core-pair report (field-mapping-server-write-path-r1 task 052d; task 044(d); ontology D-204, D-210): for
/// Signal and Decision Record rows created at or after go-live, it REPORTS each row that holds one half of the core pair only
/// (<see cref="FindingHalf"/>) and each update of either pair column (<see cref="FindingChanged"/>), read from the column audit
/// history. It writes nothing to either table, in any case: it sends only reads (<c>RetrieveMultiple</c> and metadata), and it
/// reads through a <see cref="WriteRefusingEntityService"/>, so a write member throws before anything is sent.
/// </summary>
/// <remarks>
/// <para><b>Half pair.</b> Per table, rows with <c>createdon</c> in [max(go-live, watermark − overlap), run start − settle time),
/// filtered server-side to "type without id, or id without type", ordered by id, at most <see cref="MaxRowsPerScan"/> per run;
/// each row is asked again in code (created at or after go-live; exactly one half present) before it is reported.</para>
/// <para><b>Changed after create.</b> Per table, the <c>audit</c> rows of the table's NUMERIC object type code (read from the
/// table's metadata each run; Dataverse refuses the logical name) and operation Update (<see cref="AuditOperationUpdate"/>) or
/// Upsert (<see cref="AuditOperationUpsert"/>) whose own <c>createdon</c> (the change time) is in [max(go-live, audit watermark −
/// overlap), run start − settle time), ordered by <c>auditid</c>, at most <see cref="MaxAuditRowsPerScan"/> per run. An Upsert
/// audit that is not after the row's own <c>createdon</c> is the row's creation, not a change. An audit row changed a pair column when its
/// <c>attributemask</c> (the changed columns' <c>ColumnNumber</c>s) names either pair column's number, read from the table's
/// column metadata once per run. A mask that cannot be read is reported <see cref="FindingChangeUndecided"/>, never "no change".
/// The candidate rows' <c>createdon</c> is then read in batches (<see cref="RowReadChunk"/> ids per query): a row created before
/// go-live is not reported; a row that is not returned (deleted) is counted and logged, not reported.</para>
/// <para><b>Auditing must be on (every run, before the audit read).</b> The organization's <c>isauditenabled</c>, the table's
/// <c>IsAuditEnabled</c> and both pair columns' <c>IsAuditEnabled</c> must each be on; a flag that is off or not stated is an
/// audit fault (one error line per table per run, the run partial, the audit watermark not moved). A history that was never
/// written reads as no rows, which would otherwise be taken as "no change" and the watermark moved past it.</para>
/// <para><b>State.</b> Its own entry in the job's state store (<c>record-repair-pass:{table}:report:state</c>), never the
/// repair state: two watermarks, two stopped-sweep cursors, and the keys of the rows and audit rows already handled whose time
/// lies within the overlap, so a finding is reported once per window it falls in (the overlap re-reads it; the key skips it).
/// A capped scan keeps its window and continues after its last id next run. A fault (scan, audit, metadata, row read or state)
/// leaves that sweep's watermark where it was and makes the run partial.</para>
/// <para><b>What a finding carries.</b> Table, row id, finding code, and for an audit finding the audit time and the changing
/// user's id. Never a column value, a core-record id or row content: in the result JSON (the first
/// <see cref="MaxFindingsInResult"/>) and in one structured log line per finding.</para>
/// <para>Component Justification (CLAUDE.md §11): (1) Existing — the repair pass excludes both tables; nothing reads the column
/// audit history. (2) Extension — it runs inside the pass's scheduled run, with its claim store, state store, metadata cache and
/// result; no new job, route or store. (3) Cost of doing nothing — task 044 removed field security from the pair on the condition
/// that an edit outside the server is detected (D-210); without this a half or altered pair on append-only evidence goes unseen.</para>
/// </remarks>
internal sealed class RecordRepairCorePairReport
{
    /// <summary>The tables reported on (the core-pair tables), never written.</summary>
    internal static readonly IReadOnlyList<string> Tables = ["sprk_signal", "sprk_decisionrecord"];

    internal const string FindingHalf = "core_pair_half";
    internal const string FindingChanged = "core_pair_changed_after_create";
    internal const string FindingChangeUndecided = "core_pair_change_undecided";

    /// <summary>Half-pair rows read per table per run; a fuller window continues next run.</summary>
    internal const int MaxRowsPerScan = 500;

    /// <summary>Update audit rows read per table per run; a fuller window continues next run.</summary>
    internal const int MaxAuditRowsPerScan = 1000;

    /// <summary>Candidate row ids per <c>createdon</c> read.</summary>
    internal const int RowReadChunk = 100;

    /// <summary>Findings listed in the result JSON; every finding is still counted and logged.</summary>
    internal const int MaxFindingsInResult = 50;

    /// <summary>The <c>audit.operation</c> value of an update.</summary>
    internal const int AuditOperationUpdate = 2;

    /// <summary>The <c>audit.operation</c> value of an upsert (an upsert that created the row is its creation, not a change).</summary>
    internal const int AuditOperationUpsert = 5;

    /// <summary>The platform's organization table, read for its <c>isauditenabled</c> flag.</summary>
    internal const string OrganizationTable = "organization";

    internal const string OrganizationAuditColumn = "isauditenabled";

    internal const string AuditTable = "audit";

    private const string HalfKeyPrefix = "h:";
    private const string AuditKeyPrefix = "a:";

    private static readonly TimeSpan ClaimDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StateLifetime = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly WriteRefusingEntityService _entities;
    private readonly Dictionary<string, TableAuditMetadata> _tables = new(StringComparer.OrdinalIgnoreCase);
    private bool? _organizationAudit;
    private readonly IIdempotencyService _claims;
    private readonly IDistributedCache _store;
    private readonly RecordRepairPassContext _pass;
    private readonly ILogger _logger;
    private readonly JobRunContext _context;

    internal RecordRepairCorePairReport(
        IGenericEntityService entities, IIdempotencyService claims, IDistributedCache store, RecordRepairPassContext pass, ILogger logger,
        JobRunContext context)
    {
        // "No write" is a property of the type: every write member of this service throws before anything is sent.
        _entities = new WriteRefusingEntityService(entities);
        _claims = claims;
        _store = store;
        _pass = pass;
        _logger = logger;
        _context = context;
    }

    /// <summary>The report's state key of <paramref name="table"/> (beside, never inside, the repair state).</summary>
    internal static string StateKeyOf(string table) => $"{RecordRepairPassJob.JobIdConstant}:{table}:report:state";

    /// <summary>The report's claim key of <paramref name="table"/>.</summary>
    internal static string ClaimKeyOf(string table) => $"{RecordRepairPassJob.JobIdConstant}:{table}:report";

    /// <summary>Reports every table over rows and audit rows up to <paramref name="until"/>.</summary>
    internal async Task<Result> RunAsync(DateTimeOffset until, CancellationToken ct)
    {
        var result = new Result();
        foreach (var table in Tables)
        {
            ct.ThrowIfCancellationRequested();
            await RunTableAsync(table, until, result, ct).ConfigureAwait(false);
        }

        return result;
    }

    private async Task RunTableAsync(string table, DateTimeOffset until, Result result, CancellationToken ct)
    {
        var claimKey = ClaimKeyOf(table);
        var owner = _context.RunId.ToString("N", CultureInfo.InvariantCulture);
        if (!await _claims.TryAcquireProcessingLockAsync(claimKey, owner, ClaimDuration, ct).ConfigureAwait(false))
        {
            result.ClaimHeld++;
            result.Problems.Add($"{table}: another runner holds its core-pair report; not read this run.");
            return;
        }

        try
        {
            var state = await ReadStateAsync(table, ct).ConfigureAwait(false);
            if (state is null)
            {
                result.Failures++;
                result.Problems.Add($"{table}: its core-pair report state could not be read; not read this run.");
                return;
            }

            await HalfSweepAsync(table, state, until, result, ct).ConfigureAwait(false);
            await AuditSweepAsync(table, state, until, result, ct).ConfigureAwait(false);

            if (!await WriteStateAsync(table, state, ct).ConfigureAwait(false))
            {
                result.Failures++;
                result.Problems.Add($"{table}: its core-pair report state could not be saved; the next run reads the same window again.");
            }
        }
        finally
        {
            try
            {
                await _claims.ReleaseProcessingLockAsync(claimKey, owner, CancellationToken.None).ConfigureAwait(false);
            }
            // The claim is advisory: the host's lease holds the tick, and the report writes nothing to the table.
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "{Prefix} Claim bookkeeping for {Key} failed; it expires on its own", RecordRepairPassJob.LogPrefix, claimKey);
            }
        }
    }

    // ── Half pair ────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task HalfSweepAsync(string table, ReportState state, DateTimeOffset until, Result result, CancellationToken ct)
    {
        var since = WindowStart(state.HalfWatermark, until);
        var windowEnd = state.HalfSweepUntil ?? until;
        EntityCollection page;
        try
        {
            page = await _entities.RetrieveMultipleAsync(BuildHalfPairQuery(table, since, windowEnd, state.HalfSweepAfter, MaxRowsPerScan), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            result.Failures++;
            result.Problems.Add($"{table}: the half-pair scan failed; its window is read again next run.");
            _logger.LogError(ex, "{Prefix} The core-pair half scan of {Table} failed; its watermark did not move. correlationId={CorrelationId}",
                RecordRepairPassJob.LogPrefix, table, _context.CorrelationId);
            return;
        }

        var rows = page.Entities.Take(MaxRowsPerScan).ToList();
        foreach (var row in rows)
        {
            result.RowsRead++;
            if (RecordRepairPassContext.CreatedOnOf(row) is not { } createdOn || createdOn < _pass.GoLive || !HoldsOneHalfOnly(row))
                continue;

            if (!state.Seen.TryAdd(HalfKeyPrefix + row.Id.ToString("N", CultureInfo.InvariantCulture), createdOn))
                continue; // already reported in this window's overlap

            Report(result, new Finding(table, row.Id, FindingHalf, AuditTime: null, ChangedBy: null));
        }

        if (rows.Count > 0 && (page.MoreRecords || page.Entities.Count > MaxRowsPerScan))
        {
            result.Truncated++;
            state.HalfSweepUntil = windowEnd;
            state.HalfSweepAfter = rows[^1].Id;
            result.Problems.Add($"{table}: more than {MaxRowsPerScan} half-pair rows in the report's window; the next run continues.");
        }
        else
        {
            state.HalfWatermark = windowEnd;
            state.HalfSweepUntil = null;
            state.HalfSweepAfter = null;
            Prune(state, HalfKeyPrefix, windowEnd);
        }
    }

    /// <summary>Exactly one of the two pair columns holds a value (a lookup with an id; text that is not blank).</summary>
    internal static bool HoldsOneHalfOnly(Entity row)
    {
        var type = row.GetAttributeValue<EntityReference>(RecordOwnershipContext.CoreRecordTypeColumn) is { Id: var t } && t != Guid.Empty;
        var id = !string.IsNullOrWhiteSpace(row.GetAttributeValue<string>(RecordOwnershipContext.CoreRecordIdColumn));
        return type != id;
    }

    /// <summary>The half-pair scan of one table: created in [since, until), one half of the pair only, after an id, ordered by id.</summary>
    internal static QueryExpression BuildHalfPairQuery(string table, DateTimeOffset since, DateTimeOffset until, Guid? after, int cap)
    {
        var idColumn = table + "id";
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet(idColumn, RecordRepairPassContext.CreatedOnColumn,
                RecordOwnershipContext.CoreRecordTypeColumn, RecordOwnershipContext.CoreRecordIdColumn),
            NoLock = true,
            PageInfo = new PagingInfo { Count = cap, PageNumber = 1 },
        };

        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.LessThan, until.UtcDateTime);
        if (after is { } continueAfter)
            query.Criteria.AddCondition(idColumn, ConditionOperator.GreaterThan, continueAfter);

        var half = new FilterExpression(LogicalOperator.Or);
        var typeOnly = half.AddFilter(LogicalOperator.And);
        typeOnly.AddCondition(RecordOwnershipContext.CoreRecordTypeColumn, ConditionOperator.NotNull);
        typeOnly.AddCondition(RecordOwnershipContext.CoreRecordIdColumn, ConditionOperator.Null);
        var idOnly = half.AddFilter(LogicalOperator.And);
        idOnly.AddCondition(RecordOwnershipContext.CoreRecordTypeColumn, ConditionOperator.Null);
        idOnly.AddCondition(RecordOwnershipContext.CoreRecordIdColumn, ConditionOperator.NotNull);
        query.Criteria.AddFilter(half);

        query.AddOrder(idColumn, OrderType.Ascending);
        return query;
    }

    // ── Changed after create (column audit history) ─────────────────────────────────────────────────────────────────

    private async Task AuditSweepAsync(string table, ReportState state, DateTimeOffset until, Result result, CancellationToken ct)
    {
        var since = WindowStart(state.AuditWatermark, until);
        var windowEnd = state.AuditSweepUntil ?? until;

        // Every run, before any history is read (also when there are no audit rows): auditing must be on, or an empty history
        // would be read as "no change" and the watermark moved past a window that was never recorded.
        var stage = "the audit settings could not be read";
        AuditFacts facts;
        try
        {
            facts = await AuditFactsAsync(table, what => stage = what, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            AuditFault(table, result, stage, ex);
            return;
        }

        if (facts.Off.Count > 0)
        {
            AuditFault(table, result, "auditing is not on (" + string.Join("; ", facts.Off) + ")", ex: null);
            return;
        }

        List<Entity> audits;
        bool more;
        try
        {
            var page = await _entities.RetrieveMultipleAsync(
                    BuildAuditQuery(facts.ObjectTypeCode, since, windowEnd, state.AuditSweepAfter, MaxAuditRowsPerScan), ct)
                .ConfigureAwait(false);
            audits = page.Entities.Take(MaxAuditRowsPerScan).ToList();
            more = audits.Count > 0 && (page.MoreRecords || page.Entities.Count > MaxAuditRowsPerScan);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            AuditFault(table, result, "the audit history read failed", ex);
            return;
        }

        var candidates = new List<(string Key, Guid? RowId, DateTimeOffset AuditTime, Guid? ChangedBy, string Finding, int? Operation)>();
        foreach (var audit in audits)
        {
            result.AuditRowsRead++;
            var auditId = audit.GetAttributeValue<Guid?>("auditid") is { } aid && aid != Guid.Empty ? aid : audit.Id;
            var key = AuditKeyPrefix + auditId.ToString("N", CultureInfo.InvariantCulture);
            var auditTime = RecordRepairPassContext.CreatedOnOf(audit);
            if (auditTime is null)
            {
                AuditFault(table, result, "an audit row carries no time", ex: null);
                return;
            }

            if (state.Seen.ContainsKey(key))
                continue; // already handled in this window's overlap

            var finding = PairChangeOf(audit.GetAttributeValue<string>("attributemask"), facts.Numbers);
            if (finding is null)
                continue;

            candidates.Add((key, audit.GetAttributeValue<EntityReference>("objectid")?.Id is { } r && r != Guid.Empty ? r : null,
                auditTime.Value, audit.GetAttributeValue<EntityReference>("userid")?.Id, finding,
                audit.GetAttributeValue<OptionSetValue>("operation")?.Value));
        }

        // Forward-only: the changed rows' createdon, read in batches; nothing is reported until every batch read.
        Dictionary<Guid, DateTimeOffset?> createdOn;
        try
        {
            createdOn = await CreatedOnOfAsync(table, candidates.Where(c => c.RowId is not null).Select(c => c.RowId!.Value).Distinct().ToList(), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            AuditFault(table, result, "the changed rows could not be read", ex);
            return;
        }

        foreach (var candidate in candidates)
        {
            state.Seen[candidate.Key] = candidate.AuditTime;
            if (candidate.RowId is not { } rowId || !createdOn.TryGetValue(rowId, out var created) || created is null)
            {
                // Not shown to be created at or after go-live (no row id, or the row is not returned): counted, not reported.
                result.RowsUnread++;
                _logger.LogWarning(
                    "{Prefix} core-pair report: table={Table} id={Id} auditTime={AuditTime} changedBy={ChangedBy}: an update of a pair column "
                    + "whose row could not be read; not reported. runId={RunId} correlationId={CorrelationId}",
                    RecordRepairPassJob.LogPrefix, table, candidate.RowId?.ToString("D", CultureInfo.InvariantCulture) ?? "-",
                    candidate.AuditTime.ToString("O", CultureInfo.InvariantCulture),
                    candidate.ChangedBy?.ToString("D", CultureInfo.InvariantCulture) ?? "-", _context.RunId, _context.CorrelationId);
                continue;
            }

            if (created.Value < _pass.GoLive)
                continue; // created before go-live: never reported

            // An upsert that created the row is audited as an upsert whose mask names every column it set: not after the row's
            // own createdon, it is the creation, not a change. (An update audit is a change whenever it happened.)
            if (candidate.Operation == AuditOperationUpsert && candidate.AuditTime <= created.Value)
                continue;

            Report(result, new Finding(table, rowId, candidate.Finding, candidate.AuditTime, candidate.ChangedBy));
        }

        if (more)
        {
            result.Truncated++;
            state.AuditSweepUntil = windowEnd;
            state.AuditSweepAfter = audits[^1].GetAttributeValue<Guid?>("auditid") is { } last && last != Guid.Empty ? last : audits[^1].Id;
            result.Problems.Add($"{table}: more than {MaxAuditRowsPerScan} update audit rows in the report's window; the next run continues.");
        }
        else
        {
            state.AuditWatermark = windowEnd;
            state.AuditSweepUntil = null;
            state.AuditSweepAfter = null;
            Prune(state, AuditKeyPrefix, windowEnd);
        }
    }

    /// <summary>
    /// What an update audit's <paramref name="mask"/> says about the pair: <see cref="FindingChanged"/> when it names either pair
    /// column's number, <c>null</c> when it names only other columns, <see cref="FindingChangeUndecided"/> when it cannot be read
    /// (absent, or an entry that is not a number) — never read as "no change".
    /// </summary>
    internal static string? PairChangeOf(string? mask, (int Type, int Id) numbers)
    {
        if (string.IsNullOrWhiteSpace(mask))
            return FindingChangeUndecided;

        var changed = false;
        foreach (var entry in mask.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(entry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var column))
                return FindingChangeUndecided;
            changed |= column == numbers.Type || column == numbers.Id;
        }

        return changed ? FindingChanged : null;
    }

    /// <summary>
    /// The update and upsert audit rows of the table with the numeric <paramref name="objectTypeCode"/> (Dataverse refuses the
    /// logical name there) whose change time is in [since, until), after an audit id, ordered by audit id.
    /// </summary>
    internal static QueryExpression BuildAuditQuery(int objectTypeCode, DateTimeOffset since, DateTimeOffset until, Guid? after, int cap)
    {
        var query = new QueryExpression(AuditTable)
        {
            ColumnSet = new ColumnSet("auditid", RecordRepairPassContext.CreatedOnColumn, "objectid", "userid", "attributemask", "operation"),
            NoLock = true,
            PageInfo = new PagingInfo { Count = cap, PageNumber = 1 },
        };

        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, objectTypeCode);
        query.Criteria.AddCondition("operation", ConditionOperator.In, AuditOperationUpdate, AuditOperationUpsert);
        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.GreaterEqual, since.UtcDateTime);
        query.Criteria.AddCondition(RecordRepairPassContext.CreatedOnColumn, ConditionOperator.LessThan, until.UtcDateTime);
        if (after is { } continueAfter)
            query.Criteria.AddCondition("auditid", ConditionOperator.GreaterThan, continueAfter);

        query.AddOrder("auditid", OrderType.Ascending);
        return query;
    }

    /// <summary>What the audit sweep needs to know about one table, and which audit settings are not on.</summary>
    private sealed record AuditFacts(int ObjectTypeCode, (int Type, int Id) Numbers, IReadOnlyList<string> Off);

    /// <summary>
    /// The table's numeric object type code, the pair columns' numbers, and every audit setting that is not on (the organization,
    /// the table, each pair column), read every run. A setting the metadata does not state counts as not on. A fault, a missing
    /// code or a missing column number is thrown (<paramref name="stage"/> says which read it was).
    /// </summary>
    private async Task<AuditFacts> AuditFactsAsync(string table, Action<string> stage, CancellationToken ct)
    {
        var off = new List<string>();

        stage("the organization's audit setting could not be read");
        if (!await OrganizationAuditEnabledAsync(ct).ConfigureAwait(false))
            off.Add("the organization");

        stage("the table's audit settings could not be read");
        if (!_tables.TryGetValue(table, out var meta))
        {
            meta = await _entities.GetTableAuditMetadataAsync(table, ct).ConfigureAwait(false);
            _tables[table] = meta;
        }

        if (meta.ObjectTypeCode is not { } code)
            throw new InvalidOperationException($"The metadata of {table} does not state its object type code.");

        if (meta.IsAuditEnabled != true)
            off.Add("the table");

        stage("the pair columns' numbers could not be read");
        if (!_pass.TryGetColumns(table, out var columns))
        {
            _pass.CountMetadataRead();
            columns = await _entities.GetColumnMetadataAsync(table, ct).ConfigureAwait(false);
            _pass.KeepColumns(table, columns);
        }

        if (!(columns.TryGetValue(RecordOwnershipContext.CoreRecordTypeColumn, out var type) && type.ColumnNumber is { } typeNumber
              && columns.TryGetValue(RecordOwnershipContext.CoreRecordIdColumn, out var id) && id.ColumnNumber is { } idNumber))
        {
            throw new InvalidOperationException($"The column metadata of {table} does not number both core pair columns.");
        }

        if (type.IsAuditEnabled != true)
            off.Add(RecordOwnershipContext.CoreRecordTypeColumn);
        if (id.IsAuditEnabled != true)
            off.Add(RecordOwnershipContext.CoreRecordIdColumn);

        return new AuditFacts(code, (typeNumber, idNumber), off);
    }

    /// <summary>Whether the organization's auditing is on (read once per run; a flag not stated is not on).</summary>
    private async Task<bool> OrganizationAuditEnabledAsync(CancellationToken ct)
    {
        if (_organizationAudit is { } known)
            return known;

        var query = new QueryExpression(OrganizationTable) { ColumnSet = new ColumnSet(OrganizationAuditColumn), NoLock = true, TopCount = 1 };
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var on = rows.Entities.FirstOrDefault()?.GetAttributeValue<bool?>(OrganizationAuditColumn) == true;
        _organizationAudit = on;
        return on;
    }

    /// <summary>The <c>createdon</c> of each of <paramref name="ids"/> the read returns (a row it does not return is absent).</summary>
    private async Task<Dictionary<Guid, DateTimeOffset?>> CreatedOnOfAsync(string table, IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = new Dictionary<Guid, DateTimeOffset?>();
        var idColumn = table + "id";
        foreach (var chunk in ids.Chunk(RowReadChunk))
        {
            var query = new QueryExpression(table) { ColumnSet = new ColumnSet(idColumn, RecordRepairPassContext.CreatedOnColumn), NoLock = true };
            query.Criteria.AddCondition(idColumn, ConditionOperator.In, chunk.Cast<object>().ToArray());
            var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            foreach (var row in rows.Entities)
                found[row.Id] = RecordRepairPassContext.CreatedOnOf(row);
        }

        return found;
    }

    // ── Shared ───────────────────────────────────────────────────────────────────────────────────────────────────────

    private DateTimeOffset WindowStart(DateTimeOffset? watermark, DateTimeOffset until)
    {
        var start = watermark is { } mark && mark <= until ? mark - RecordRepairPassJob.WindowOverlap : _pass.GoLive;
        return start >= _pass.GoLive ? start : _pass.GoLive;
    }

    /// <summary>Forgets the handled keys of <paramref name="prefix"/> that the next window (starting an overlap before
    /// <paramref name="watermark"/>) no longer reaches.</summary>
    private static void Prune(ReportState state, string prefix, DateTimeOffset watermark)
    {
        var reach = watermark - RecordRepairPassJob.WindowOverlap;
        foreach (var key in state.Seen.Where(s => s.Key.StartsWith(prefix, StringComparison.Ordinal) && s.Value < reach).Select(s => s.Key).ToList())
            state.Seen.Remove(key);
    }

    private void AuditFault(string table, Result result, string what, Exception? ex)
    {
        result.Failures++;
        result.Problems.Add($"{table}: {what}; the core-pair change report's window is read again next run.");
        _logger.LogError(ex, "{Prefix} The core-pair change report of {Table} faulted ({What}); its watermark did not move and no change "
                             + "is assumed. correlationId={CorrelationId}", RecordRepairPassJob.LogPrefix, table, what, _context.CorrelationId);
    }

    /// <summary>One finding: counted, logged (one structured line), and listed in the result while there is room.</summary>
    private void Report(Result result, Finding finding)
    {
        switch (finding.Code)
        {
            case FindingHalf:
                result.Half++;
                break;
            case FindingChanged:
                result.Changed++;
                break;
            default:
                result.Undecided++;
                break;
        }

        if (result.Findings.Count < MaxFindingsInResult)
            result.Findings.Add(finding);
        else
            result.FindingsTruncated = true;

        _logger.LogWarning(
            "{Prefix} core-pair report: table={Table} id={Id} finding={Finding} auditTime={AuditTime} changedBy={ChangedBy} "
            + "runId={RunId} correlationId={CorrelationId}",
            RecordRepairPassJob.LogPrefix, finding.Table, finding.Id.ToString("D", CultureInfo.InvariantCulture), finding.Code,
            finding.AuditTime?.ToString("O", CultureInfo.InvariantCulture) ?? "-",
            finding.ChangedBy?.ToString("D", CultureInfo.InvariantCulture) ?? "-", _context.RunId, _context.CorrelationId);
    }

    private async Task<ReportState?> ReadStateAsync(string table, CancellationToken ct)
    {
        try
        {
            var raw = await _store.GetStringAsync(StateKeyOf(table), ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(raw))
                return new ReportState();

            var state = JsonSerializer.Deserialize<ReportState>(raw, JsonOptions);
            return state is null ? null : state with { Seen = state.Seen ?? [] };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} The core-pair report state of {Table} could not be read", RecordRepairPassJob.LogPrefix, table);
            return null;
        }
    }

    private async Task<bool> WriteStateAsync(string table, ReportState state, CancellationToken ct)
    {
        try
        {
            await _store.SetStringAsync(StateKeyOf(table), JsonSerializer.Serialize(state, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = StateLifetime }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "{Prefix} The core-pair report state of {Table} could not be saved", RecordRepairPassJob.LogPrefix, table);
            return false;
        }
    }

    /// <summary>One reported row: table, row id, finding code, and for an audit finding the change time and the changing user.</summary>
    internal sealed record Finding(string Table, Guid Id, string Code, DateTimeOffset? AuditTime, Guid? ChangedBy);

    /// <summary>One table's report state between runs (see the remarks).</summary>
    internal sealed record ReportState
    {
        public DateTimeOffset? HalfWatermark { get; set; }

        public DateTimeOffset? HalfSweepUntil { get; set; }

        public Guid? HalfSweepAfter { get; set; }

        public DateTimeOffset? AuditWatermark { get; set; }

        public DateTimeOffset? AuditSweepUntil { get; set; }

        public Guid? AuditSweepAfter { get; set; }

        /// <summary>The keys of the rows ("h:") and audit rows ("a:") already handled whose time the next window still reaches.</summary>
        public Dictionary<string, DateTimeOffset> Seen { get; init; } = [];
    }

    /// <summary>One run's report: counts, findings (the first <see cref="MaxFindingsInResult"/>) and problems.</summary>
    internal sealed class Result
    {
        public int Half;
        public int Changed;
        public int Undecided;
        public int RowsUnread;
        public int RowsRead;
        public int AuditRowsRead;
        public int Truncated;
        public int Failures;
        public int ClaimHeld;
        public bool FindingsTruncated;

        public List<Finding> Findings { get; } = [];

        public List<string> Problems { get; } = [];

        /// <summary>No fault, no cap reached, no claim held: the windows were read to their end.</summary>
        public bool IsClean => Failures == 0 && Truncated == 0 && ClaimHeld == 0 && !FindingsTruncated;
    }
}
