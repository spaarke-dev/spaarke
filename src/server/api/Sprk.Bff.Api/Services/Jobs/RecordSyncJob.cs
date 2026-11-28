// Record sync (AIPU2-041). GitHub #3321 (split from #3306): the job also removes the rows of records deleted in Dataverse, and runs on
// the ADR-036 scheduled-job host instead of its own timer (ADR-052: a hand-rolled timer migrates when its behaviour next changes).
//
// Component Justification (CLAUDE.md §11), for the removal pass:
//   (1) Existing — this job adds and updates records-index rows; DataverseIndexSyncService.RemoveRecordAsync can delete one row but
//       nothing calls it. No Dataverse change event reaches the BFF and ADR-002 rules out a plugin, so no write path sees a delete.
//   (2) Extension — this job already owns the records index for the five record types and reads Dataverse with the stamp identity;
//       the removal is one more pass over the same types, not a new mechanism.
//   (3) Cost of doing nothing — record search keeps returning deleted records (DEV: ~200–250 matter rows for 54 matters).
//
// Placement (bff-extensions.md): BFF, on the ADR-036 host (ADR-052: no Function or Container Apps Job signal — low volume, index writes
// and Dataverse reads with the stamp identity, bounded per run). No endpoint, no package.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Spaarke.Scheduling;

namespace Sprk.Bff.Api.Services.Jobs;

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for <see cref="RecordSyncJob"/>.
/// Bind from the "RecordSync" configuration section.
/// </summary>
public class RecordSyncOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RecordSync";

    /// <summary>
    /// Whether the job is registered enabled. Read at startup; the admin job endpoints enable or disable it at run time.
    /// Default: false (opt-in).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Minutes between runs. A divisor of 60 (1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30) or 60; any other value runs every 30 minutes.
    /// Read at startup. Default: 30.
    /// </summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Azure AI Search endpoint, e.g. https://spaarke-search-dev.search.windows.net.
    /// </summary>
    public string AiSearchEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure AI Search key, when one is configured (pulled from Key Vault at startup); absent selects the managed identity.
    /// </summary>
    public string AiSearchApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com.
    /// </summary>
    public string DataverseEnvironmentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Hours between passes that remove the rows of records deleted in Dataverse, per record type (GitHub #3321). A pass reads every
    /// record id of the type, so it runs less often than the sync of changes. 0 or less turns removal off. Default: 6.
    /// </summary>
    public int DeletedRecordSweepHours { get; set; } = 6;
}

// ─────────────────────────────────────────────────────────────────────────────
// Entity configuration — mirrors Sync-RecordsToIndex.ps1
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Describes how to query and map a single Dataverse entity type.</summary>
public sealed record EntityConfig(
    string EntityLogicalName,
    string EntitySetName,
    string IdField,
    string NameField,
    string? DescriptionField,
    string? ReferenceField,
    string SelectFields);

// ─────────────────────────────────────────────────────────────────────────────
// AI Search document model — must match spaarke-records-index schema
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents a single document in the spaarke-records-index Azure AI Search index.
/// Field names match the index JSON schema exactly.
/// </summary>
public sealed class RecordSearchDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD tenant ID (the user's home tenant) — enforces tenant isolation at the index level (FR-12).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("recordType")]
    public string RecordType { get; set; } = string.Empty;

    [JsonPropertyName("recordName")]
    public string RecordName { get; set; } = string.Empty;

    [JsonPropertyName("recordDescription")]
    public string RecordDescription { get; set; } = string.Empty;

    [JsonPropertyName("organizations")]
    public List<string> Organizations { get; set; } = new();

    [JsonPropertyName("people")]
    public List<string> People { get; set; } = new();

    [JsonPropertyName("referenceNumbers")]
    public List<string> ReferenceNumbers { get; set; } = new();

    [JsonPropertyName("keywords")]
    public string Keywords { get; set; } = string.Empty;

    [JsonPropertyName("lastModified")]
    public DateTimeOffset LastModified { get; set; }

    /// <summary>
    /// Record id of the matter type (<c>sprk_mattertype</c>) of a matter or project; null for other record types or when
    /// the record has no type. Backs the Matter Type filter of record search.
    /// </summary>
    /// <remarks>Omitted from JSON when null so a BFF writing this document does not fail an index that predates the field.</remarks>
    [JsonPropertyName("matterTypeId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MatterTypeId { get; set; }

    [JsonPropertyName("dataverseRecordId")]
    public string DataverseRecordId { get; set; } = string.Empty;

    [JsonPropertyName("dataverseEntityName")]
    public string DataverseEntityName { get; set; } = string.Empty;

    [JsonPropertyName("privilege_group_ids")]
    public List<string> PrivilegeGroupIds { get; set; } = new();
}

/// <summary>The two fields of a records-index row that the removal pass reads (GitHub #3321).</summary>
public sealed class IndexedRecordRow
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("dataverseRecordId")]
    public string? DataverseRecordId { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Scheduled job
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Keeps the spaarke-records-index current with Dataverse for sprk_matter, sprk_project, sprk_invoice, contact and account.
/// </summary>
/// <remarks>
/// <para><b>Changes.</b> Each run reads the records modified since the type's watermark (all of them on the first run) and upserts
/// their rows. The watermark is kept in the distributed cache (<c>recordsync:watermark:{entityType}</c>, ADR-009) and advances only
/// after every batch of the type is written, so a failed type is read again by the next run.</para>
/// <para><b>Deleted records (GitHub #3321).</b> At most once per <see cref="RecordSyncOptions.DeletedRecordSweepHours"/> per type, the
/// run lists the type's rows in this tenant (in record-id order, any number of rows), then every id of the type that Dataverse still
/// holds. Each row whose record is missing from that list is read back on its own, and only a "not found" answer makes it deleted. The
/// index is read first, so a record created during the pass is never taken for deleted. Nothing is removed when Dataverse cannot be
/// read, when it returns no records of a type whose rows the index holds, when a record missing from the list still exists (the list
/// was incomplete), or when a read-back is refused. The pass is marked done (<c>recordsync:sweep:{entityType}</c>) only when every
/// delete succeeded; otherwise the next run repeats it.</para>
/// <para><b>ADR-036 A1.</b> Registered through <c>AddScheduledJob</c> (rule 6); one dispatch per tick under the host's lease (rule 1),
/// where the old timer ran on every instance; one heartbeat per attempt (rule 5). Rule 3, idempotency: upserts SET a row and deletes of
/// a missing row succeed, so a re-run converges. Rule 4, retry: it THROWS when no type could be synced (no progress was possible) and
/// otherwise counts the faults and completes, because the next run revisits them.</para>
/// <para>ADR-017: exponential backoff on HTTP 429 from AI Search (3 tries).</para>
/// </remarks>
public class RecordSyncJob : IScheduledJob
{
    // ── Constants ────────────────────────────────────────────────────────────

    public const string JobIdConstant = "record-sync";

    /// <summary>The per-attempt heartbeat.</summary>
    public static readonly EventId Heartbeat = new(9321, "RecordSyncHeartbeat");

    private const string IndexName = "spaarke-records-index";
    private const string WatermarkKeyPrefix = "recordsync:watermark:";
    private const string SweepKeyPrefix = "recordsync:sweep:";
    private const int AiSearchBatchSize = 50;
    private const int DeleteBatchSize = 1000;
    private const int IndexPageSize = 1000;
    private const int DataversePageSize = 200;
    private const int DataverseIdPageSize = 5000;
    private const int MaxRetries = 3;

    private static readonly JsonSerializerOptions ResultJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Entity catalogue (mirrors Sync-RecordsToIndex.ps1) ──────────────────

    private static readonly IReadOnlyList<EntityConfig> EntityConfigs = new[]
    {
        new EntityConfig(
            EntityLogicalName: "sprk_matter",
            EntitySetName:     "sprk_matters",
            IdField:           "sprk_matterid",
            NameField:         "sprk_mattername",
            DescriptionField:  "sprk_matterdescription",
            ReferenceField:    "sprk_matternumber",
            SelectFields:      "sprk_matterid,sprk_mattername,sprk_matterdescription,sprk_matternumber,modifiedon,_sprk_mattertype_value"),

        new EntityConfig(
            EntityLogicalName: "sprk_project",
            EntitySetName:     "sprk_projects",
            IdField:           "sprk_projectid",
            NameField:         "sprk_projectname",
            DescriptionField:  "sprk_projectdescription",
            ReferenceField:    "sprk_projectnumber",
            SelectFields:      "sprk_projectid,sprk_projectname,sprk_projectdescription,sprk_projectnumber,modifiedon,_sprk_mattertype_value"),

        // Invoice (email-r4 Phase 2, 2026-07-18): indexing invoices makes them matchable by the Association
        // Engine's record-name/number + semantic rungs (previously matter/project only). Name field is
        // sprk_name; reference number is sprk_invoicenumber (FinanceSummaryService is the field-name authority).
        new EntityConfig(
            EntityLogicalName: "sprk_invoice",
            EntitySetName:     "sprk_invoices",
            IdField:           "sprk_invoiceid",
            NameField:         "sprk_name",
            DescriptionField:  "sprk_description",
            ReferenceField:    "sprk_invoicenumber",
            SelectFields:      "sprk_invoiceid,sprk_name,sprk_description,sprk_invoicenumber,modifiedon"),

        new EntityConfig(
            EntityLogicalName: "contact",
            EntitySetName:     "contacts",
            IdField:           "contactid",
            NameField:         "fullname",
            DescriptionField:  "description",
            ReferenceField:    null,
            // Web API selects a lookup via its `_<name>_value` form; the raw
            // `parentcustomerid` 400s with 0x80060888 ("Could not find a property named
            // 'parentcustomerid'"), which failed the contacts sync every run (every ~15 min).
            // Confirmed via App Insights 2026-08-31. Pre-existing on master.
            SelectFields:      "contactid,fullname,description,jobtitle,_parentcustomerid_value,modifiedon"),

        new EntityConfig(
            EntityLogicalName: "account",
            EntitySetName:     "accounts",
            IdField:           "accountid",
            NameField:         "name",
            DescriptionField:  "description",
            ReferenceField:    "accountnumber",
            SelectFields:      "accountid,name,description,accountnumber,modifiedon"),
    };

    /// <summary>The record types this job syncs, in sync order.</summary>
    internal static IReadOnlyList<EntityConfig> Entities => EntityConfigs;

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecordSyncJob> _logger;
    private readonly RecordSyncOptions _options;

    private SearchClient? _searchClient;

    // Dataverse token caching (mirrors ExternalParticipationService pattern per ADR-028).
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    // ─────────────────────────────────────────────────────────────────────────

    public RecordSyncJob(
        IDistributedCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IOptions<RecordSyncOptions> options,
        TokenCredential credential,
        TimeProvider timeProvider,
        ILogger<RecordSyncJob> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobId => JobIdConstant;

    public string DisplayName => "Record Sync";

    public string Description =>
        "Keeps the records search index current with Dataverse matters, projects, invoices, contacts and accounts: writes changed "
        + "records and removes the rows of records deleted in Dataverse (GitHub #3321).";

    /// <summary>The cron schedule for <paramref name="intervalMinutes"/> (see <see cref="RecordSyncOptions.IntervalMinutes"/>).</summary>
    public static string CronFor(int intervalMinutes) => intervalMinutes switch
    {
        60 => "0 * * * *",
        > 0 and < 60 when 60 % intervalMinutes == 0 => $"*/{intervalMinutes} * * * *",
        _ => "*/30 * * * *",
    };

    private SearchClient Search => _searchClient ??= BuildSearchClient();

    /// <summary>
    /// Acquires a Dataverse access token via Managed Identity (canonical per ADR-028).
    /// Tokens are cached until 5 minutes before expiry. Concurrent callers are serialized
    /// behind a semaphore so a token refresh fires at most once.
    /// </summary>
    /// <remarks>
    /// Requires the BFF Managed Identity to be registered as a Dataverse Application User
    /// in the target environment — see docs/guides/auth-deployment-setup.md §6.
    /// </remarks>
    private async Task<string> GetDataverseTokenAsync(string dataverseUrl, CancellationToken ct)
    {
        await _tokenSemaphore.WaitAsync(ct);
        try
        {
            if (_currentToken is { } cached && cached.ExpiresOn > _timeProvider.GetUtcNow().AddMinutes(5))
            {
                return cached.Token;
            }

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IScheduledJob entry point
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var started = _timeProvider.GetTimestamp();
        var counts = new RecordSyncCounts();

        if (string.IsNullOrWhiteSpace(_options.AiSearchEndpoint))
        {
            _logger.LogError("RecordSyncJob requires RecordSync:AiSearchEndpoint; the run is skipped.");
            counts.SkippedReason = "no-ai-search-endpoint";
            LogHeartbeat(context, counts);
            return new JobRunResult(true, null, 0, _timeProvider.GetElapsedTime(started), Serialize(counts), Skipped: true);
        }

        try
        {
            await RunSyncCycleAsync(counts, cancellationToken);
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
            counts.EntityFaults + counts.SweepFaults > 0
                ? $"{counts.EntityFaults} record types failed to sync and {counts.SweepFaults} failed the deleted-record pass; the next run revisits them."
                : null,
            counts.Indexed + counts.Removed,
            _timeProvider.GetElapsedTime(started),
            Serialize(counts));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One run across all entity types
    // ─────────────────────────────────────────────────────────────────────────

    internal async Task RunSyncCycleAsync(RecordSyncCounts counts, CancellationToken ct)
    {
        foreach (var entity in EntityConfigs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var count = await SyncEntityAsync(entity, ct);
                counts.Indexed += count;

                _logger.LogInformation(
                    "RecordSyncJob synced {Count} records for {EntityType}",
                    count, entity.EntityLogicalName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.EntityFaults++;
                _logger.LogError(ex,
                    "RecordSyncJob failed for entity {EntityType} — skipping to next entity type.",
                    entity.EntityLogicalName);
            }

            try
            {
                counts.Removed += await SweepDeletedRecordsAsync(entity, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                counts.SweepFaults++;
                _logger.LogError(ex,
                    "RecordSyncJob could not remove the rows of deleted {EntityType} records; the next run tries again.",
                    entity.EntityLogicalName);
            }
        }

        if (counts.EntityFaults == EntityConfigs.Count)
        {
            // A1-4: no record type could be synced, so this attempt made no progress; throw so the host retries.
            throw new InvalidOperationException("RecordSyncJob: no record type could be synced in this attempt.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sync a single entity type
    // ─────────────────────────────────────────────────────────────────────────

    public virtual async Task<int> SyncEntityAsync(EntityConfig entity, CancellationToken ct)
    {
        var watermark = await ReadWatermarkAsync(entity.EntityLogicalName, ct);

        _logger.LogInformation(
            "RecordSyncJob querying {EntitySetName} modified after {Watermark:O}",
            entity.EntitySetName, watermark);

        var records = await QueryDataverseAsync(entity, watermark, ct);

        if (records.Count == 0)
        {
            _logger.LogInformation(
                "RecordSyncJob: no new records for {EntityType} since {Watermark:O}",
                entity.EntityLogicalName, watermark);
            return 0;
        }

        _logger.LogInformation(
            "RecordSyncJob: {Count} changed records found for {EntityType}",
            records.Count, entity.EntityLogicalName);

        // Track the latest modifiedon across all returned records so we can advance the
        // watermark after a successful full-batch upload.
        var newWatermark = records
            .Select(r => r.GetProperty("modifiedon").GetDateTimeOffset())
            .Max();

        // FR-12: resolve tenantId once per entity batch from Azure AD tenant context.
        // The BFF runs against a single Azure AD tenant; AzureAd:TenantId is the canonical
        // source for background services with no user context (matches the pattern used by
        // Email, Workers/Office, Communication services).
        var tenantId = _configuration["AzureAd:TenantId"] ?? string.Empty;

        // Transform all records to search documents.
        var documents = records
            .Select(r => MapToSearchDocument(r, entity, tenantId))
            .ToList();

        // Push in batches of AiSearchBatchSize, respecting cancellation between batches.
        int indexed = 0;
        for (int offset = 0; offset < documents.Count; offset += AiSearchBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = documents.Skip(offset).Take(AiSearchBatchSize).ToList();
            await UploadBatchWithRetryAsync(batch, entity.EntityLogicalName, ct);
            indexed += batch.Count;
        }

        // Only advance the watermark after all batches have been successfully uploaded.
        await WriteWatermarkAsync(entity.EntityLogicalName, newWatermark, ct);

        return indexed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Remove the rows of deleted records (GitHub #3321)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the type's pass is due, deletes the type's rows in this tenant whose Dataverse record no longer exists, and returns how many
    /// rows were removed. See the class remarks for the order of reads and the cases in which nothing is removed.
    /// </summary>
    public virtual async Task<int> SweepDeletedRecordsAsync(EntityConfig entity, CancellationToken ct)
    {
        if (_options.DeletedRecordSweepHours <= 0)
        {
            return 0;
        }

        var now = _timeProvider.GetUtcNow();
        var lastSweep = await ReadSweepMarkerAsync(entity.EntityLogicalName, ct);
        if (lastSweep is { } last && now - last < TimeSpan.FromHours(_options.DeletedRecordSweepHours))
        {
            return 0;
        }

        var tenantId = _configuration["AzureAd:TenantId"];
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning(
                "RecordSyncJob: AzureAd:TenantId is not set, so the rows of deleted {EntityType} records are not removed.",
                entity.EntityLogicalName);
            return 0;
        }

        // The index first, then Dataverse: a row listed here belongs to a record that existed before the Dataverse read below, so a
        // record created during the pass is never taken for deleted.
        var rows = await ListIndexRowsAsync(entity, tenantId, ct);
        if (rows.Count == 0)
        {
            await WriteSweepMarkerAsync(entity.EntityLogicalName, now, ct);
            return 0;
        }

        var liveIds = await QueryLiveRecordIdsAsync(entity, ct);
        if (liveIds is null)
        {
            _logger.LogWarning(
                "RecordSyncJob: RecordSync:DataverseEnvironmentUrl is not set, so the rows of deleted {EntityType} records are not removed.",
                entity.EntityLogicalName);
            return 0;
        }

        if (liveIds.Count == 0)
        {
            _logger.LogWarning(
                "RecordSyncJob: Dataverse returned no {EntityType} records while the index holds {RowCount} rows of that type; nothing is removed.",
                entity.EntityLogicalName, rows.Count);
            return 0;
        }

        var candidates = rows
            .Select(row => (row.Id, Parsed: Guid.TryParse(row.DataverseRecordId, out var recordId), RecordId: recordId))
            .Where(row => row.Parsed && !liveIds.Contains(row.RecordId))
            .ToList();

        // A listing can be incomplete without failing (the identity lost read access to part of the table). Every candidate is read
        // back on its own before anything is deleted: only "not found" counts as deleted. One candidate that is still there means the
        // listing missed records, and a refused read throws, so in both cases nothing is removed.
        var staleKeys = new List<string>(candidates.Count);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await RecordExistsAsync(entity, candidate.RecordId, ct))
            {
                // Thrown, not returned: the run counts it as a failed pass, so it shows in the run result and the heartbeat.
                throw new InvalidOperationException(
                    $"A {entity.EntityLogicalName} record missing from the Dataverse listing still exists, so the listing is incomplete; nothing is removed.");
            }

            staleKeys.Add(candidate.Id);
        }

        var removed = 0;
        for (var offset = 0; offset < staleKeys.Count; offset += DeleteBatchSize)
        {
            ct.ThrowIfCancellationRequested();
            var batch = staleKeys.Skip(offset).Take(DeleteBatchSize).ToList();
            removed += await WithThrottleRetryAsync(() => DoDeleteBatchAsync(batch, entity.EntityLogicalName, ct), entity.EntityLogicalName, ct);
        }

        _logger.LogInformation(
            "RecordSyncJob removed {Removed} of {Stale} rows of deleted {EntityType} records ({RowCount} rows checked)",
            removed, staleKeys.Count, entity.EntityLogicalName, rows.Count);

        // Marked done only when every stale row went; otherwise the next run repeats the pass.
        if (removed == staleKeys.Count)
        {
            await WriteSweepMarkerAsync(entity.EntityLogicalName, now, ct);
        }

        return removed;
    }

    /// <summary>
    /// The type's rows in this tenant that carry a record id (key and record id only), read in record-id order: each page asks for
    /// the ids from the last one read onward, so no offset limit applies and rows added or removed meanwhile do not shift the pages.
    /// </summary>
    internal async Task<List<IndexedRecordRow>> ListIndexRowsAsync(EntityConfig entity, string tenantId, CancellationToken ct)
    {
        var baseFilter = $"recordType eq '{EscapeODataLiteral(entity.EntityLogicalName)}' and tenantId eq '{EscapeODataLiteral(tenantId)}' and dataverseRecordId ne null";
        var rows = new List<IndexedRecordRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? from = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // "ge" re-reads the rows of the last id; the key set drops them, so a row sharing its record id with another is still read.
            var options = new SearchOptions
            {
                Filter = from is null ? baseFilter : $"{baseFilter} and dataverseRecordId ge '{EscapeODataLiteral(from)}'",
                Size = IndexPageSize,
            };
            options.Select.Add("id");
            options.Select.Add("dataverseRecordId");
            options.OrderBy.Add("dataverseRecordId asc");

            var response = await Search.SearchAsync<IndexedRecordRow>("*", options, ct);
            var pageCount = 0;
            var added = 0;
            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                pageCount++;
                if (seen.Add(result.Document.Id))
                {
                    rows.Add(result.Document);
                    added++;
                    from = result.Document.DataverseRecordId;
                }
            }

            if (pageCount < IndexPageSize || added == 0)
            {
                return rows;
            }
        }
    }

    /// <summary>
    /// Reads one record by id: true when it exists, false when Dataverse answers "not found". Any other answer (a refused read, an
    /// outage) throws, so the pass removes nothing.
    /// </summary>
    public virtual async Task<bool> RecordExistsAsync(EntityConfig entity, Guid recordId, CancellationToken ct)
    {
        var baseUrl = _options.DataverseEnvironmentUrl;
        using var http = _httpClientFactory.CreateClient("RecordSyncDataverse");
        var dataverseToken = await GetDataverseTokenAsync(baseUrl, ct);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl.TrimEnd('/')}/api/data/v9.2/{entity.EntitySetName}({recordId:D})?$select={Uri.EscapeDataString(entity.IdField)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Dataverse returned {(int)response.StatusCode} reading one {entity.EntitySetName} record");
        }

        return true;
    }

    /// <summary>
    /// Every id of the type that Dataverse holds, or null when no Dataverse URL is configured. Throws when Dataverse answers with an
    /// error, so a failed read never looks like an empty table.
    /// </summary>
    public virtual async Task<HashSet<Guid>?> QueryLiveRecordIdsAsync(EntityConfig entity, CancellationToken ct)
    {
        var baseUrl = _options.DataverseEnvironmentUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        using var http = _httpClientFactory.CreateClient("RecordSyncDataverse");
        var dataverseToken = await GetDataverseTokenAsync(baseUrl, ct);

        var url = $"{baseUrl.TrimEnd('/')}/api/data/v9.2/{entity.EntitySetName}?$select={Uri.EscapeDataString(entity.IdField)}";
        var ids = new HashSet<Guid>();

        while (!string.IsNullOrEmpty(url))
        {
            ct.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("Prefer", $"odata.maxpagesize={DataverseIdPageSize}");
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "RecordSyncJob: Dataverse id query for {EntitySetName} returned {StatusCode}",
                    entity.EntitySetName, (int)response.StatusCode);
                throw new HttpRequestException(
                    $"Dataverse returned {(int)response.StatusCode} for {entity.EntitySetName}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueArray))
            {
                foreach (var record in valueArray.EnumerateArray())
                {
                    if (Guid.TryParse(GetString(record, entity.IdField), out var id))
                    {
                        ids.Add(id);
                    }
                }
            }

            url = root.TryGetProperty("@odata.nextLink", out var nextLink)
                ? nextLink.GetString() ?? string.Empty
                : string.Empty;
        }

        return ids;
    }

    /// <summary>Deletes one batch of rows by key and returns how many deletes the index accepted.</summary>
    public virtual async Task<int> DoDeleteBatchAsync(IReadOnlyList<string> keys, string entityType, CancellationToken ct)
    {
        var result = await Search.IndexDocumentsAsync(
            IndexDocumentsBatch.Delete("id", keys),
            new IndexDocumentsOptions { ThrowOnAnyError = false },
            ct);

        var failed = 0;
        foreach (var r in result.Value.Results.Where(r => !r.Succeeded))
        {
            failed++;
            _logger.LogWarning(
                "RecordSyncJob: AI Search did not delete document {Key} (status {Status})",
                r.Key, r.Status);
        }

        return keys.Count - failed;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Dataverse OData query
    // ─────────────────────────────────────────────────────────────────────────

    public virtual async Task<List<JsonElement>> QueryDataverseAsync(
        EntityConfig entity,
        DateTimeOffset watermark,
        CancellationToken ct)
    {
        var baseUrl = _options.DataverseEnvironmentUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning(
                "RecordSyncJob: RecordSync:DataverseEnvironmentUrl not set — " +
                "Dataverse queries will fail. Configure this setting.");
            return new List<JsonElement>();
        }

        using var http = _httpClientFactory.CreateClient("RecordSyncDataverse");

        // Acquire Dataverse MI token once per cycle. The cache inside GetDataverseTokenAsync
        // serves all entity queries in this cycle and refreshes only if expiry is near.
        var dataverseToken = await GetDataverseTokenAsync(baseUrl, ct);

        // ISO 8601 OData-compatible datetime literal (no timezone suffix — Dataverse expects UTC Z)
        var watermarkStr = watermark.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var odataFilter = Uri.EscapeDataString($"modifiedon gt {watermarkStr}");
        var odataSelect = Uri.EscapeDataString(entity.SelectFields);
        var odataOrderBy = Uri.EscapeDataString("modifiedon asc");

        var url = $"{baseUrl.TrimEnd('/')}/api/data/v9.2/{entity.EntitySetName}" +
                  $"?$filter={odataFilter}" +
                  $"&$select={odataSelect}" +
                  $"&$orderby={odataOrderBy}" +
                  $"&$top={DataversePageSize}";

        var allRecords = new List<JsonElement>();

        while (!string.IsNullOrEmpty(url))
        {
            ct.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dataverseToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("Prefer",
                "odata.include-annotations=OData.Community.Display.V1.FormattedValue," +
                $"odata.maxpagesize={DataversePageSize}");
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");

            using var response = await http.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError(
                    "RecordSyncJob: Dataverse query for {EntitySetName} returned {StatusCode}: {Body}",
                    entity.EntitySetName, (int)response.StatusCode, body);
                throw new HttpRequestException(
                    $"Dataverse returned {(int)response.StatusCode} for {entity.EntitySetName}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueArray))
            {
                foreach (var record in valueArray.EnumerateArray())
                {
                    // Clone so the element survives doc disposal.
                    allRecords.Add(record.Clone());
                }
            }

            // OData next-link paging
            url = root.TryGetProperty("@odata.nextLink", out var nextLink)
                ? nextLink.GetString() ?? string.Empty
                : string.Empty;
        }

        return allRecords;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Field mapping — Dataverse record → AI Search document
    // ─────────────────────────────────────────────────────────────────────────

    public static RecordSearchDocument MapToSearchDocument(JsonElement record, EntityConfig entity, string tenantId = "")
    {
        var recordId = GetString(record, entity.IdField);
        var recordName = GetString(record, entity.NameField);
        var desc = entity.DescriptionField is not null ? GetString(record, entity.DescriptionField) : string.Empty;
        var reference = entity.ReferenceField is not null ? GetString(record, entity.ReferenceField) : string.Empty;

        DateTimeOffset lastModified = DateTimeOffset.UtcNow;
        if (record.TryGetProperty("modifiedon", out var modifiedProp) &&
            modifiedProp.ValueKind == JsonValueKind.String)
        {
            _ = DateTimeOffset.TryParse(modifiedProp.GetString(), out lastModified);
        }

        // keywords: name + reference for keyword search (mirrors the PS1 script logic)
        var keywordParts = new List<string>();
        if (!string.IsNullOrEmpty(recordName)) keywordParts.Add(recordName);
        if (!string.IsNullOrEmpty(reference)) keywordParts.Add(reference);
        var keywords = string.Join(" ", keywordParts);

        var refNumbers = new List<string>();
        if (!string.IsNullOrEmpty(reference)) refNumbers.Add(reference);

        return new RecordSearchDocument
        {
            Id = $"{entity.EntityLogicalName}_{recordId}",
            TenantId = tenantId,  // FR-12: populated from Azure AD tenant context
            RecordType = entity.EntityLogicalName,
            RecordName = recordName,
            RecordDescription = desc,
            Organizations = new List<string>(),  // TODO: expand lookup joins
            People = new List<string>(),
            ReferenceNumbers = refNumbers,
            Keywords = keywords,
            LastModified = lastModified,
            // Matter and project select the matter type column; other entity types do not (-> null).
            MatterTypeId = GetString(record, "_sprk_mattertype_value") is { Length: > 0 } matterType ? matterType.ToLowerInvariant() : null,
            DataverseRecordId = recordId,
            DataverseEntityName = entity.EntityLogicalName,
            PrivilegeGroupIds = new List<string>(),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AI Search writes with exponential backoff on 429
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a batch to AI Search with exponential backoff retry on HTTP 429.
    /// Calls <see cref="DoUploadBatchAsync"/> for the actual AI Search call — override
    /// that method in tests to simulate throttling without touching the retry logic.
    /// </summary>
    public Task UploadBatchWithRetryAsync(
        List<RecordSearchDocument> batch,
        string entityType,
        CancellationToken ct) =>
        WithThrottleRetryAsync(async () =>
        {
            await DoUploadBatchAsync(batch, entityType, ct);
            return 0;
        }, entityType, ct);

    private async Task<T> WithThrottleRetryAsync<T>(Func<Task<T>> call, string entityType, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await call();
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.TooManyRequests)
            {
                if (attempt >= MaxRetries)
                {
                    _logger.LogError(ex,
                        "RecordSyncJob: AI Search 429 after {MaxRetries} retries for {EntityType} — giving up on this batch.",
                        MaxRetries, entityType);
                    throw;
                }

                var delay = GetRetryDelay(attempt);
                _logger.LogWarning(
                    "RecordSyncJob: AI Search 429 for {EntityType} (attempt {Attempt}/{MaxRetries}). " +
                    "Retrying in {DelaySeconds}s.",
                    entityType, attempt, MaxRetries, delay.TotalSeconds);

                await Task.Delay(delay, _timeProvider, ct);
            }
        }
    }

    /// <summary>
    /// Returns the delay to wait after a 429 before the next retry attempt.
    /// Virtual so tests can return <see cref="TimeSpan.Zero"/> to avoid slow delays.
    /// </summary>
    public virtual TimeSpan GetRetryDelay(int attempt) =>
        TimeSpan.FromSeconds(Math.Pow(2, attempt));

    /// <summary>
    /// Performs the actual AI Search IndexDocuments call for one batch.
    /// Virtual so tests can override to simulate 429 or other failures without
    /// replacing the retry orchestration in <see cref="UploadBatchWithRetryAsync"/>.
    /// </summary>
    public virtual async Task DoUploadBatchAsync(
        List<RecordSearchDocument> batch,
        string entityType,
        CancellationToken ct)
    {
        var actions = batch
            .Select(IndexDocumentsAction.MergeOrUpload)
            .ToList();

        var indexBatch = IndexDocumentsBatch.Create(actions.ToArray());

        var result = await Search.IndexDocumentsAsync(
            indexBatch,
            new IndexDocumentsOptions { ThrowOnAnyError = false },
            ct);

        // Log any per-document failures (non-fatal — the whole batch was accepted by AI Search)
        foreach (var r in result.Value.Results.Where(r => !r.Succeeded))
        {
            _logger.LogWarning(
                "RecordSyncJob: AI Search rejected document {Key} (status {Status}): {Message}",
                r.Key, r.Status, r.ErrorMessage);
        }

        _logger.LogDebug(
            "RecordSyncJob: uploaded batch of {Count} documents for {EntityType}",
            batch.Count, entityType);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Watermark and sweep-marker persistence via IDistributedCache
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Earliest watermark Dataverse will accept on a `modifiedon gt {value}` filter.
    /// Dataverse's CrmDateTime minimum is 1753-01-01; DateTime.MinValue (year 0001)
    /// is rejected with error 0x80040239: "DateTime is less than minimum value supported
    /// by CrmDateTime." 1900-01-01 is a comfortable cushion above the floor and matches
    /// the practical age of any record we'd be syncing.
    /// </summary>
    private static readonly DateTimeOffset DataverseSafeMinWatermark =
        new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public virtual async Task<DateTimeOffset> ReadWatermarkAsync(string entityType, CancellationToken ct)
    {
        var key = $"{WatermarkKeyPrefix}{entityType}";
        // SYSTEM-LEVEL EXCEPTION (NFR-08): watermark is a durable system-wide bookmark for cross-tenant Dataverse sync per entity type; tenant-scoping would fragment the bookmark.
        var data = await _cache.GetStringAsync(key, ct);

        if (string.IsNullOrEmpty(data) || !DateTimeOffset.TryParse(data, out var watermark))
        {
            // Default: 1900-01-01 — forces a full initial sync for this entity type.
            // DateTime.MinValue (year 0001) is rejected by Dataverse CrmDateTime.
            return DataverseSafeMinWatermark;
        }

        return watermark;
    }

    public virtual async Task WriteWatermarkAsync(
        string entityType,
        DateTimeOffset watermark,
        CancellationToken ct)
    {
        var key = $"{WatermarkKeyPrefix}{entityType}";

        // Persist watermark indefinitely (no sliding expiry — this is a durable bookmark).
        // SYSTEM-LEVEL EXCEPTION (NFR-08): watermark is a durable system-wide bookmark for cross-tenant Dataverse sync per entity type; tenant-scoping would fragment the bookmark.
        await _cache.SetStringAsync(key, watermark.ToString("O"), new DistributedCacheEntryOptions(), ct);

        _logger.LogDebug(
            "RecordSyncJob: watermark for {EntityType} advanced to {Watermark:O}",
            entityType, watermark);
    }

    private async Task<DateTimeOffset?> ReadSweepMarkerAsync(string entityType, CancellationToken ct)
    {
        // SYSTEM-LEVEL EXCEPTION (NFR-08): the time of the last deleted-record pass per entity type is a system-wide bookmark, like the watermark.
        var data = await _cache.GetStringAsync($"{SweepKeyPrefix}{entityType}", ct);
        return DateTimeOffset.TryParse(data, out var last) ? last : null;
    }

    private Task WriteSweepMarkerAsync(string entityType, DateTimeOffset completedAt, CancellationToken ct) =>
        // SYSTEM-LEVEL EXCEPTION (NFR-08): the time of the last deleted-record pass per entity type is a system-wide bookmark, like the watermark.
        _cache.SetStringAsync($"{SweepKeyPrefix}{entityType}", completedAt.ToString("O"), new DistributedCacheEntryOptions(), ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Creates the records-index client. Virtual so a test can point the real SDK client at an in-memory index.</summary>
    protected virtual SearchClient BuildSearchClient()
    {
        // auth-v4 task 053 (FR-E4): an absent RecordSync:AiSearchApiKey selects Entra (managed
        // identity) through the shared factory rather than producing an empty-key credential.
        return Sprk.Bff.Api.Infrastructure.Auth.SearchClientFactory.CreateSearchClient(
            new Uri(_options.AiSearchEndpoint), IndexName, _options.AiSearchApiKey,
            _configuration, _credential);
    }

    private void LogHeartbeat(JobRunContext context, RecordSyncCounts counts) =>
        _logger.LogInformation(
            Heartbeat,
            "RecordSyncJob heartbeat: attempt {Attempt}, indexed {Indexed}, removed {Removed}, sync faults {EntityFaults}, removal faults {SweepFaults}, skipped {SkippedReason}, failed {Failed}",
            context.Attempt, counts.Indexed, counts.Removed, counts.EntityFaults, counts.SweepFaults, counts.SkippedReason, counts.Failed);

    private static string Serialize(RecordSyncCounts counts) => JsonSerializer.Serialize(counts, ResultJsonOptions);

    private static string EscapeODataLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string GetString(JsonElement record, string field)
    {
        if (record.TryGetProperty(field, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>What one run of <see cref="RecordSyncJob"/> did; the heartbeat and the run's result JSON.</summary>
internal sealed class RecordSyncCounts
{
    public int Indexed { get; set; }

    public int Removed { get; set; }

    public int EntityFaults { get; set; }

    public int SweepFaults { get; set; }

    public string? SkippedReason { get; set; }

    public bool Failed { get; set; }
}
