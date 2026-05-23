using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Jobs;

// ─────────────────────────────────────────────────────────────────────────────
// Configuration
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Configuration options for the RecordSyncJob background service.
/// Bind from the "RecordSync" configuration section.
/// </summary>
public class RecordSyncOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "RecordSync";

    /// <summary>
    /// Kill switch — set to false to disable the job entirely without redeploying.
    /// Default: false (opt-in).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Polling interval in minutes. Default: 30.
    /// </summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Azure AI Search endpoint, e.g. https://spaarke-search-dev.search.windows.net.
    /// </summary>
    public string AiSearchEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Azure AI Search admin key (pulled from Key Vault at startup).
    /// </summary>
    public string AiSearchApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com.
    /// Defaults to Dataverse:EnvironmentUrl when empty.
    /// </summary>
    public string DataverseEnvironmentUrl { get; set; } = string.Empty;
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

    [JsonPropertyName("dataverseRecordId")]
    public string DataverseRecordId { get; set; } = string.Empty;

    [JsonPropertyName("dataverseEntityName")]
    public string DataverseEntityName { get; set; } = string.Empty;

    [JsonPropertyName("privilege_group_ids")]
    public List<string> PrivilegeGroupIds { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────────────────────
// BackgroundService
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Background service that incrementally synchronises Dataverse records into the
/// spaarke-records-index Azure AI Search index.
///
/// <para>
/// Supported entity types: sprk_matter, sprk_project, contact, account.
/// Only records modified since the last successful sync watermark are queried
/// (incremental sync).  Watermarks are persisted in the distributed cache
/// (Redis in production, in-memory in development) under the key
/// <c>recordsync:watermark:{entityType}</c>.
/// </para>
///
/// <para>
/// ADR-001: Uses BackgroundService + PeriodicTimer — no Azure Functions.
/// ADR-009: Watermark stored in IDistributedCache (Redis-first).
/// ADR-017: Exponential backoff on HTTP 429 from AI Search (3 retries max).
/// </para>
/// </summary>
public class RecordSyncJob : BackgroundService
{
    // ── Constants ────────────────────────────────────────────────────────────

    private const string IndexName = "spaarke-records-index";
    private const string WatermarkKeyPrefix = "recordsync:watermark:";
    private const int AiSearchBatchSize = 50;
    private const int DataversePageSize = 200;
    private const int MaxRetries = 3;

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
            SelectFields:      "sprk_matterid,sprk_mattername,sprk_matterdescription,sprk_matternumber,modifiedon"),

        new EntityConfig(
            EntityLogicalName: "sprk_project",
            EntitySetName:     "sprk_projects",
            IdField:           "sprk_projectid",
            NameField:         "sprk_projectname",
            DescriptionField:  "sprk_projectdescription",
            ReferenceField:    "sprk_projectnumber",
            SelectFields:      "sprk_projectid,sprk_projectname,sprk_projectdescription,sprk_projectnumber,modifiedon"),

        new EntityConfig(
            EntityLogicalName: "contact",
            EntitySetName:     "contacts",
            IdField:           "contactid",
            NameField:         "fullname",
            DescriptionField:  "description",
            ReferenceField:    null,
            SelectFields:      "contactid,fullname,description,jobtitle,parentcustomerid,modifiedon"),

        new EntityConfig(
            EntityLogicalName: "account",
            EntitySetName:     "accounts",
            IdField:           "accountid",
            NameField:         "name",
            DescriptionField:  "description",
            ReferenceField:    "accountnumber",
            SelectFields:      "accountid,name,description,accountnumber,modifiedon"),
    };

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly IDistributedCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
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
        ILogger<RecordSyncJob> logger)
    {
        _cache             = cache             ?? throw new ArgumentNullException(nameof(cache));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration     = configuration     ?? throw new ArgumentNullException(nameof(configuration));
        _options           = options?.Value    ?? throw new ArgumentNullException(nameof(options));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
    }

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
            if (_currentToken is { } cached && cached.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return cached.Token;
            }

            // Prefer the BFF's configured MI client ID. Falls back to default credential
            // resolution if the setting is missing (covers local dev with env-var creds).
            var managedIdentityClientId = _configuration["ManagedIdentity:ClientId"];
            var credential = string.IsNullOrEmpty(managedIdentityClientId)
                ? new DefaultAzureCredential()
                : new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = managedIdentityClientId
                });

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService entry point
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "RecordSyncJob is disabled. Set RecordSync:Enabled=true to enable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.AiSearchEndpoint))
        {
            _logger.LogError(
                "RecordSyncJob requires RecordSync:AiSearchEndpoint. Service will not start.");
            return;
        }

        _searchClient = BuildSearchClient();

        var interval = TimeSpan.FromMinutes(_options.IntervalMinutes);

        _logger.LogInformation(
            "RecordSyncJob starting — interval {IntervalMinutes} min, index {IndexName}, entities [{Entities}]",
            _options.IntervalMinutes,
            IndexName,
            string.Join(", ", EntityConfigs.Select(e => e.EntityLogicalName)));

        using var timer = new PeriodicTimer(interval);

        // Initial run immediately, then wait for timer ticks.
        do
        {
            try
            {
                await RunSyncCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("RecordSyncJob stopping due to cancellation.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RecordSyncJob cycle failed — will retry on next interval ({IntervalMinutes} min).",
                    _options.IntervalMinutes);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("RecordSyncJob stopped.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One full sync cycle across all entity types
    // ─────────────────────────────────────────────────────────────────────────

    public virtual async Task RunSyncCycleAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        _logger.LogInformation("RecordSyncJob cycle started at {StartedAt:O}", started);

        int totalIndexed = 0;

        foreach (var entity in EntityConfigs)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                int count = await SyncEntityAsync(entity, ct);
                totalIndexed += count;

                _logger.LogInformation(
                    "RecordSyncJob synced {Count} records for {EntityType}",
                    count, entity.EntityLogicalName);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // bubble up to ExecuteAsync
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "RecordSyncJob failed for entity {EntityType} — skipping to next entity type.",
                    entity.EntityLogicalName);
                // Continue with remaining entity types — don't let one failure stop the whole cycle.
            }
        }

        _logger.LogInformation(
            "RecordSyncJob cycle complete — {TotalIndexed} total records indexed in {Elapsed:F1}s",
            totalIndexed,
            (DateTimeOffset.UtcNow - started).TotalSeconds);
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

        // Transform all records to search documents.
        var documents = records
            .Select(r => MapToSearchDocument(r, entity))
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

        var odataFilter   = Uri.EscapeDataString($"modifiedon gt {watermarkStr}");
        var odataSelect   = Uri.EscapeDataString(entity.SelectFields);
        var odataOrderBy  = Uri.EscapeDataString("modifiedon asc");

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

    public static RecordSearchDocument MapToSearchDocument(JsonElement record, EntityConfig entity)
    {
        var recordId   = GetString(record, entity.IdField);
        var recordName = GetString(record, entity.NameField);
        var desc       = entity.DescriptionField is not null ? GetString(record, entity.DescriptionField) : string.Empty;
        var reference  = entity.ReferenceField   is not null ? GetString(record, entity.ReferenceField)  : string.Empty;

        DateTimeOffset lastModified = DateTimeOffset.UtcNow;
        if (record.TryGetProperty("modifiedon", out var modifiedProp) &&
            modifiedProp.ValueKind == JsonValueKind.String)
        {
            _ = DateTimeOffset.TryParse(modifiedProp.GetString(), out lastModified);
        }

        // keywords: name + reference for keyword search (mirrors the PS1 script logic)
        var keywordParts = new List<string>();
        if (!string.IsNullOrEmpty(recordName)) keywordParts.Add(recordName);
        if (!string.IsNullOrEmpty(reference))  keywordParts.Add(reference);
        var keywords = string.Join(" ", keywordParts);

        var refNumbers = new List<string>();
        if (!string.IsNullOrEmpty(reference)) refNumbers.Add(reference);

        return new RecordSearchDocument
        {
            Id                  = $"{entity.EntityLogicalName}_{recordId}",
            RecordType          = entity.EntityLogicalName,
            RecordName          = recordName,
            RecordDescription   = desc,
            Organizations       = new List<string>(),  // TODO: expand lookup joins
            People              = new List<string>(),
            ReferenceNumbers    = refNumbers,
            Keywords            = keywords,
            LastModified        = lastModified,
            DataverseRecordId   = recordId,
            DataverseEntityName = entity.EntityLogicalName,
            PrivilegeGroupIds   = new List<string>(),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AI Search upsert with exponential backoff on 429
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Uploads a batch to AI Search with exponential backoff retry on HTTP 429.
    /// Calls <see cref="DoUploadBatchAsync"/> for the actual AI Search call — override
    /// that method in tests to simulate throttling without touching the retry logic.
    /// </summary>
    public async Task UploadBatchWithRetryAsync(
        List<RecordSearchDocument> batch,
        string entityType,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await DoUploadBatchAsync(batch, entityType, ct);
                return; // success
            }
            catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.TooManyRequests)
            {
                if (attempt == MaxRetries)
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

                await Task.Delay(delay, ct);
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

        var result = await _searchClient!.IndexDocumentsAsync(
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
    // Watermark persistence via IDistributedCache
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
        var key  = $"{WatermarkKeyPrefix}{entityType}";
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
        await _cache.SetStringAsync(key, watermark.ToString("O"), new DistributedCacheEntryOptions(), ct);

        _logger.LogDebug(
            "RecordSyncJob: watermark for {EntityType} advanced to {Watermark:O}",
            entityType, watermark);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private SearchClient BuildSearchClient()
    {
        var endpoint   = new Uri(_options.AiSearchEndpoint);
        var credential = new AzureKeyCredential(_options.AiSearchApiKey);
        return new SearchClient(endpoint, IndexName, credential);
    }

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
