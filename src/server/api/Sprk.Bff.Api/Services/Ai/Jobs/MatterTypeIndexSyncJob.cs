// matter-type-filter (GitHub #3165; ADR-036 A1, ADR-052).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — FileIndexingService stamps matterTypeId / matterTypeSourceId on a document's chunks when it is indexed, and
//       RecordSyncJob / DataverseIndexSyncService write matterTypeId on matter and project records. Nothing revisits chunks that were
//       indexed before the fields existed, and nothing carries a later change of a matter's or project's type to the chunks of its
//       documents (no Dataverse change event reaches the BFF; ADR-002 rules out a plugin).
//   (2) Extension — the indexing paths run per document at index time and know nothing of "every chunk filed under record X"; the
//       ADR-036 host already owns schedules, leases, retries and heartbeats, so this is one IScheduledJob on it, not a new mechanism.
//   (3) Cost of doing nothing — the Matter Type filter returns nothing for every document indexed before this change, and returns the
//       previous type's documents (and omits the new type's) after anyone edits a matter's type.
//
// Placement (bff-extensions.md): BFF, on the ADR-036 host (ADR-052: no Function or Container Apps Job signal — short, idempotent, index
// merges and Dataverse reads only; the work is bounded per run). No endpoint, no package.

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Jobs;

/// <summary>Configuration of <see cref="MatterTypeIndexSyncJob"/> (section <c>MatterTypeIndexSync</c>).</summary>
public sealed class MatterTypeIndexSyncOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MatterTypeIndexSync";

    /// <summary>Whether the job is enabled at startup. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The most documents the backfill resolves per run. Default 500.</summary>
    public int BackfillDocumentsPerRun { get; set; } = 500;
}

/// <summary>
/// Keeps the Matter Type search fields current: every 30 minutes it (a) carries a changed matter or project type onto the chunks of its
/// documents and onto its records-index document, and (b) resolves a bounded number of documents whose chunks were indexed before the
/// fields existed (or whose type could not be read at index time).
/// </summary>
/// <remarks>
/// <para><b>(a) Changes.</b> Matters and projects modified since a stored watermark (all of them on the first run) are read with their
/// <c>sprk_mattertype</c>. For each, the files index is queried for chunks whose <c>matterTypeSourceId</c> is that record and whose
/// <c>matterTypeId</c> differs from the current type, and the new <c>matterTypeId</c> is merged onto them (key and field only: no
/// re-embedding, no content change). The records-index document of the record gets the same merge; a missing document is skipped. The
/// watermark advances only past a fully processed entity type, so a faulted record is revisited by the next run.</para>
/// <para><b>(b) Backfill.</b> Chunks with <c>matterTypeSourceId eq null</c> are grouped by document; each document is resolved with the
/// parent its chunks already carry (<see cref="DocumentMatterTypeResolver"/>) and both fields are merged onto all its chunks. A document
/// that cannot be resolved (a failed read) is left untouched and is found again by the next run, so the backfill drains itself.</para>
/// <para><b>Tenant isolation.</b> Every files-index query carries <c>tenantId eq '{tenant}'</c> (the BFF's <c>AzureAd:TenantId</c>, the
/// value indexing stamps); merges address chunks by the ids those queries returned.</para>
/// <para><b>ADR-036 A1.</b> Registered through <c>AddScheduledJob</c> (rule 6); one dispatch per tick under the host's lease (rule 1);
/// one heartbeat per attempt (rule 5). Rule 3, idempotency: every write SETS a value (a merge of the id and the two fields), so a re-run, a
/// retried attempt or a manual "Run Now" converges on the same state and a run over current data writes nothing; the completion marker is
/// the stored <c>matterTypeSourceId</c> itself. Rule 4, retry: it THROWS when no progress is possible (the Dataverse change query or an
/// index query faults) and otherwise counts unit faults and completes, because the next run revisits them.</para>
/// </remarks>
public sealed class MatterTypeIndexSyncJob : IScheduledJob
{
    public const string JobIdConstant = "matter-type-index-sync";

    /// <summary>Every 30 minutes, off the half hour.</summary>
    internal const string DefaultCronSchedule = "11,41 * * * *";

    /// <summary>The per-attempt heartbeat.</summary>
    public static readonly EventId Heartbeat = new(9164, "MatterTypeIndexSyncHeartbeat");

    internal const string RecordsIndexName = "spaarke-records-index";
    internal const string WatermarkKeyPrefix = "mattertypesync:watermark:";

    private const int SearchPageSize = 1000;
    private const int MaxMergePagesPerSource = 20;

    /// <summary>Records per Dataverse page (a test may lower it).</summary>
    internal int PageSize { get; init; } = 500;

    /// <summary>Most Dataverse pages per entity type per run; later records wait for the next run (a test may lower it).</summary>
    internal int MaxPages { get; init; } = 20;

    /// <summary>Most chunk-search pages the backfill reads per run (a test may lower it).</summary>
    internal int MaxBackfillPages { get; init; } = 10;

    private static readonly string[] SourceTables = ["sprk_matter", "sprk_project"];
    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<MatterTypeIndexSyncOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MatterTypeIndexSyncJob> _logger;

    public MatterTypeIndexSyncJob(
        IServiceScopeFactory scopeFactory,
        IDistributedCache cache,
        IConfiguration configuration,
        IOptionsMonitor<MatterTypeIndexSyncOptions> options,
        TimeProvider timeProvider,
        ILogger<MatterTypeIndexSyncJob> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Matter Type Index Sync";

    public string Description =>
        "Carries changes of a matter's or project's type onto the search index (document chunks and the records index) and resolves "
        + "the matter type of documents indexed before the Matter Type filter existed (GitHub #3165).";

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var counts = new SyncCounts();

        var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            counts.SkippedReason = "no-tenant-configured";
            LogHeartbeat(context, counts);
            return new JobRunResult(true, null, 0, _timeProvider.GetElapsedTime(started), Serialize(counts), Skipped: true);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            // AI search is registered only when AI is on; without it there is no index to keep current.
            var deployments = services.GetService<IKnowledgeDeploymentService>();
            if (deployments is null)
            {
                counts.SkippedReason = "ai-search-unavailable";
                LogHeartbeat(context, counts);
                return new JobRunResult(true, null, 0, _timeProvider.GetElapsedTime(started), Serialize(counts), Skipped: true);
            }

            // A fault from here on means no progress is possible this attempt (A1-4: throw, the host retries).
            var files = await deployments.GetSearchClientAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var records = services.GetService<SearchIndexClient>()?.GetSearchClient(RecordsIndexName);
            var entities = services.GetRequiredService<IGenericEntityService>();
            var resolver = services.GetRequiredService<DocumentMatterTypeResolver>();

            foreach (var table in SourceTables)
            {
                await PropagateChangesAsync(table, entities, files, records, tenantId, counts, cancellationToken).ConfigureAwait(false);
            }

            await BackfillAsync(files, resolver, tenantId, counts, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            counts.Failed = true;
            LogHeartbeat(context, counts);
            throw;
        }

        LogHeartbeat(context, counts);
        return new JobRunResult(
            true,
            counts.UnitFaults > 0 ? $"{counts.UnitFaults} units faulted and were skipped; the next run revisits them." : null,
            counts.ChunksUpdated + counts.DocumentsBackfilled,
            _timeProvider.GetElapsedTime(started),
            Serialize(counts));
    }

    // ── (a) changes ──────────────────────────────────────────────────────────────────────────────────────────────

    private async Task PropagateChangesAsync(
        string table, IGenericEntityService entities, SearchClient files, SearchClient? records, string tenantId, SyncCounts counts, CancellationToken ct)
    {
        var watermark = await ReadWatermarkAsync(table, ct).ConfigureAwait(false);
        var query = new QueryExpression(table)
        {
            ColumnSet = new ColumnSet("sprk_mattertype", "modifiedon"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 },
        };

        // ge, not gt: records that share the watermark's instant are revisited, which is harmless (every write below sets a value).
        if (watermark is { } since)
        {
            query.Criteria.AddCondition("modifiedon", ConditionOperator.GreaterEqual, since);
        }

        query.AddOrder("modifiedon", OrderType.Ascending);
        query.AddOrder(table + "id", OrderType.Ascending);

        DateTime? newWatermark = null;
        var faultsBefore = counts.UnitFaults;
        var pagesRead = 0;
        var complete = true;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            pagesRead++;

            foreach (var row in page.Entities)
            {
                ct.ThrowIfCancellationRequested();
                counts.RecordsExamined++;
                var typeId = row.GetAttributeValue<EntityReference>("sprk_mattertype") is { } type && type.Id != Guid.Empty ? type.Id.ToString("D") : null;
                await ApplyChangeAsync(table, row.Id, typeId, files, records, tenantId, counts, ct).ConfigureAwait(false);

                if (row.GetAttributeValue<DateTime?>("modifiedon") is { } modified && (newWatermark is null || modified > newWatermark))
                {
                    newWatermark = modified;
                }
            }

            if (!page.MoreRecords)
            {
                break;
            }

            if (pagesRead >= MaxPages)
            {
                complete = false;
                counts.Truncated = true;
                _logger.LogWarning("Matter type sync read {Pages} pages of {PageSize} {Table} records and stopped; later records wait for the next run.", MaxPages, PageSize, table);
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        // Only a run that handled every record it read moves the watermark; a faulted record keeps it where the next run revisits it.
        if (newWatermark is { } advance && counts.UnitFaults == faultsBefore)
        {
            await WriteWatermarkAsync(table, advance, ct).ConfigureAwait(false);
        }
        else if (!complete || counts.UnitFaults != faultsBefore)
        {
            _logger.LogInformation("Matter type sync: watermark for {Table} not advanced (truncated={Truncated}, faults={Faults}).", table, !complete, counts.UnitFaults - faultsBefore);
        }
    }

    /// <summary>Sets <paramref name="typeId"/> on every chunk filed under the record that holds another value, and on its records-index document.</summary>
    private async Task ApplyChangeAsync(
        string table, Guid recordId, string? typeId, SearchClient files, SearchClient? records, string tenantId, SyncCounts counts, CancellationToken ct)
    {
        var sourceId = recordId.ToString("D");
        try
        {
            // Chunks filed under this record whose stored type is not the current one (a null current type matches any stored value).
            var differs = typeId is null ? "matterTypeId ne null" : $"(matterTypeId ne '{Escape(typeId)}' or matterTypeId eq null)";
            var filter = $"tenantId eq '{Escape(tenantId)}' and matterTypeSourceId eq '{Escape(sourceId)}' and {differs}";

            for (var round = 0; round < MaxMergePagesPerSource; round++)
            {
                var keys = await SearchKeysAsync(files, filter, ct).ConfigureAwait(false);
                if (keys.Count == 0)
                {
                    break;
                }

                var failed = await MergeAsync(files, keys.Select(k => Merge(k, ("matterTypeId", typeId))), ct).ConfigureAwait(false);
                counts.ChunksUpdated += keys.Count - failed;
                if (failed > 0)
                {
                    counts.UnitFaults++;
                    break;
                }

                if (keys.Count < SearchPageSize)
                {
                    break;
                }
            }

            if (records is not null)
            {
                // A record that was never indexed has no document to merge into: not an error.
                await MergeAsync(records, [Merge($"{table}_{sourceId}", ("matterTypeId", typeId))], ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            counts.UnitFaults++;
            _logger.LogWarning("Matter type sync: {Table} {RecordId} faulted ({ExceptionType}); kept for the next run.", table, sourceId, ex.GetType().Name);
        }
    }

    // ── (b) backfill ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task BackfillAsync(SearchClient files, DocumentMatterTypeResolver resolver, string tenantId, SyncCounts counts, CancellationToken ct)
    {
        var limit = Math.Max(1, _options.CurrentValue.BackfillDocumentsPerRun);

        // Candidate documents: chunks never resolved. Collected across pages BEFORE any merge, so skip-paging is stable.
        var groups = new Dictionary<string, BackfillGroup>(StringComparer.Ordinal);
        var tenantFilter = $"tenantId eq '{Escape(tenantId)}' and matterTypeSourceId eq null";
        for (var page = 0; page < MaxBackfillPages && groups.Count < limit; page++)
        {
            ct.ThrowIfCancellationRequested();
            var options = new SearchOptions
            {
                Filter = tenantFilter,
                Size = SearchPageSize,
                Skip = page * SearchPageSize,
                Select = { "id", "documentId", "speFileId", "parentEntityType", "parentEntityId" },
            };

            var response = await files.SearchAsync<KnowledgeDocument>("*", options, ct).ConfigureAwait(false);
            var rows = 0;
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct).ConfigureAwait(false))
            {
                rows++;
                var chunk = result.Document;
                var byDocument = !string.IsNullOrEmpty(chunk.DocumentId);
                var key = byDocument ? "d:" + chunk.DocumentId : "f:" + chunk.SpeFileId;
                if (key.Length > 2 && !groups.ContainsKey(key) && groups.Count < limit)
                {
                    groups[key] = new BackfillGroup(byDocument, byDocument ? chunk.DocumentId! : chunk.SpeFileId!, chunk.ParentEntityType, chunk.ParentEntityId);
                }
            }

            if (rows < SearchPageSize)
            {
                break;
            }
        }

        foreach (var group in groups.Values)
        {
            ct.ThrowIfCancellationRequested();
            counts.BackfillExamined++;
            try
            {
                var parent = !string.IsNullOrEmpty(group.ParentType) && !string.IsNullOrEmpty(group.ParentId)
                    ? new ParentEntityContext(group.ParentType, group.ParentId, string.Empty)
                    : null;
                var resolution = await resolver.ResolveAsync(group.ByDocument ? group.Key : null, parent, ct).ConfigureAwait(false);
                if (!resolution.IsResolved)
                {
                    counts.Unresolved++;
                    continue;
                }

                var keys = await SearchAllKeysAsync(files, group, tenantId, ct).ConfigureAwait(false);
                if (keys.Count == 0)
                {
                    continue;
                }

                var failed = await MergeAsync(
                    files,
                    keys.Select(k => Merge(k, ("matterTypeId", resolution.MatterTypeId), ("matterTypeSourceId", resolution.SourceId))),
                    ct).ConfigureAwait(false);
                counts.ChunksUpdated += keys.Count - failed;
                if (failed > 0)
                {
                    counts.UnitFaults++;
                }
                else
                {
                    counts.DocumentsBackfilled++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                counts.UnitFaults++;
                _logger.LogWarning("Matter type backfill: a document faulted ({ExceptionType}); kept for the next run.", ex.GetType().Name);
            }
        }
    }

    private async Task<List<string>> SearchAllKeysAsync(SearchClient files, BackfillGroup group, string tenantId, CancellationToken ct)
    {
        var field = group.ByDocument ? "documentId" : "speFileId";
        var filter = $"tenantId eq '{Escape(tenantId)}' and {field} eq '{Escape(group.Key)}'";
        var keys = new List<string>();
        for (var page = 0; page < MaxMergePagesPerSource; page++)
        {
            var options = new SearchOptions { Filter = filter, Size = SearchPageSize, Skip = page * SearchPageSize, Select = { "id" } };
            var response = await files.SearchAsync<KnowledgeDocument>("*", options, ct).ConfigureAwait(false);
            var rows = 0;
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct).ConfigureAwait(false))
            {
                rows++;
                if (!string.IsNullOrEmpty(result.Document.Id))
                {
                    keys.Add(result.Document.Id);
                }
            }

            if (rows < SearchPageSize)
            {
                break;
            }
        }

        return keys;
    }

    // ── index helpers ────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<List<string>> SearchKeysAsync(SearchClient files, string filter, CancellationToken ct)
    {
        var response = await files.SearchAsync<KnowledgeDocument>("*", new SearchOptions { Filter = filter, Size = SearchPageSize, Select = { "id" } }, ct).ConfigureAwait(false);
        var keys = new List<string>();
        await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrEmpty(result.Document.Id))
            {
                keys.Add(result.Document.Id);
            }
        }

        return keys;
    }

    /// <summary>A merge document: the key and the named fields only (an explicit null clears the field).</summary>
    private static SearchDocument Merge(string key, params (string Field, string? Value)[] fields)
    {
        var document = new SearchDocument { ["id"] = key };
        foreach (var (field, value) in fields)
        {
            document[field] = value;
        }

        return document;
    }

    /// <summary>Merges the documents in batches; returns how many were rejected (a missing document, status 404, is not a rejection).</summary>
    private static async Task<int> MergeAsync(SearchClient client, IEnumerable<SearchDocument> documents, CancellationToken ct)
    {
        var failed = 0;
        foreach (var batch in documents.Chunk(SearchPageSize))
        {
            var response = await client.MergeDocumentsAsync(batch, new IndexDocumentsOptions { ThrowOnAnyError = false }, ct).ConfigureAwait(false);
            failed += response.Value.Results.Count(r => !r.Succeeded && r.Status != 404);
        }

        return failed;
    }

    private static string Escape(string value) => value.Replace("'", "''");

    // ── watermark (IDistributedCache, as RecordSyncJob keeps its own) ────────────────────────────────────────────

    private async Task<DateTime?> ReadWatermarkAsync(string table, CancellationToken ct)
    {
        // SYSTEM-LEVEL EXCEPTION (NFR-08): watermark is a durable system-wide bookmark for cross-tenant Dataverse sync per entity type; tenant-scoping would fragment the bookmark.
        var data = await _cache.GetStringAsync(WatermarkKeyPrefix + table, ct).ConfigureAwait(false);
        return !string.IsNullOrEmpty(data) && DateTime.TryParse(data, null, System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value.ToUniversalTime()
            : null;
    }

    private async Task WriteWatermarkAsync(string table, DateTime watermark, CancellationToken ct)
    {
        // Persist indefinitely (no sliding expiry): a durable bookmark.
        // SYSTEM-LEVEL EXCEPTION (NFR-08): watermark is a durable system-wide bookmark for cross-tenant Dataverse sync per entity type; tenant-scoping would fragment the bookmark.
        await _cache.SetStringAsync(WatermarkKeyPrefix + table, watermark.ToUniversalTime().ToString("O"), new DistributedCacheEntryOptions(), ct).ConfigureAwait(false);
    }

    private void LogHeartbeat(JobRunContext context, SyncCounts counts) =>
        _logger.LogInformation(
            Heartbeat,
            "Matter type index sync attempt {Attempt}: failed={Failed} skipped={Skipped} recordsExamined={Records} chunksUpdated={Chunks} "
            + "backfillExamined={BackfillExamined} backfilled={Backfilled} unresolved={Unresolved} unitFaults={UnitFaults} truncated={Truncated} correlationId={CorrelationId}",
            context.Attempt, counts.Failed, counts.SkippedReason ?? "no", counts.RecordsExamined, counts.ChunksUpdated,
            counts.BackfillExamined, counts.DocumentsBackfilled, counts.Unresolved, counts.UnitFaults, counts.Truncated, context.CorrelationId);

    private static string Serialize(SyncCounts counts) => JsonSerializer.Serialize(counts, ResultJsonOptions);

    private sealed record BackfillGroup(bool ByDocument, string Key, string? ParentType, string? ParentId);

    /// <summary>The run's counts (ResultJson).</summary>
    internal sealed class SyncCounts
    {
        public string? SkippedReason { get; set; }
        public bool Failed { get; set; }
        public int RecordsExamined { get; set; }
        public int ChunksUpdated { get; set; }
        public int BackfillExamined { get; set; }
        public int DocumentsBackfilled { get; set; }
        public int Unresolved { get; set; }
        public int UnitFaults { get; set; }
        public bool Truncated { get; set; }
    }
}
