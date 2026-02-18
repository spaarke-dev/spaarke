using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Email;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for bulk RAG document indexing.
/// Queries documents from Dataverse matching criteria, processes them with bounded concurrency,
/// and updates job status as progress is made.
///
/// Follows ADR-001 (BackgroundService pattern) and ADR-004 (async job contract).
/// Uses FileIndexingService for the actual indexing pipeline.
/// </summary>
/// <remarks>
/// Entry points:
/// - POST /api/ai/rag/admin/bulk-index (manual admin action)
/// - Scheduled timer job (periodic indexing of unindexed documents)
///
/// This handler enables:
/// - Bulk reindexing of existing documents
/// - Scheduled catch-up indexing for documents not indexed on upload
/// - Admin-initiated full matter/project reindex
/// </remarks>
public class BulkRagIndexingJobHandler : IJobHandler
{
    private readonly IFileIndexingService _fileIndexingService;
    private readonly BatchJobStatusStore _statusStore;
    private readonly IIdempotencyService _idempotencyService;
    private readonly RagTelemetry _telemetry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BulkRagIndexingJobHandler> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly string _apiUrl;

    private AccessToken? _currentToken;

    /// <summary>
    /// Job type constant - matches the JobType used when enqueuing bulk RAG indexing jobs.
    /// </summary>
    public const string JobTypeName = "BulkRagIndexing";

    // Default batch processing settings
    private const int DefaultMaxConcurrency = 5;
    private const int ProgressUpdateInterval = 10; // Update status every N documents
    private const int DefaultMaxDocuments = 1000;

    public BulkRagIndexingJobHandler(
        IFileIndexingService fileIndexingService,
        BatchJobStatusStore statusStore,
        IIdempotencyService idempotencyService,
        RagTelemetry telemetry,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<BulkRagIndexingJobHandler> logger)
    {
        _fileIndexingService = fileIndexingService ?? throw new ArgumentNullException(nameof(fileIndexingService));
        _statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _httpClient = httpClientFactory.CreateClient("DataverseBatch");

        var dataverseUrl = configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl configuration is required");

        _apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";

        var tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID configuration is required");
        var clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID configuration is required");
        var clientSecret = configuration["Dataverse:ClientSecret"]
            ?? throw new InvalidOperationException("Dataverse:ClientSecret configuration is required");

        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);

        _httpClient.BaseAddress = new Uri(_apiUrl);
        _httpClient.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        _httpClient.DefaultRequestHeaders.Add("OData-Version", "4.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var jobIdStr = job.JobId.ToString();

        try
        {
            _logger.LogInformation(
                "Starting bulk RAG indexing job {JobId}, CorrelationId {CorrelationId}",
                job.JobId, job.CorrelationId);

            // Parse bulk job payload
            var payload = ParsePayload(job.Payload);
            if (payload == null)
            {
                _logger.LogError("Invalid payload for bulk RAG indexing job {JobId}", job.JobId);
                await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Failed, ct);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            await EnsureAuthenticatedAsync(ct);

            // Query matching documents from Dataverse
            _logger.LogInformation(
                "Querying documents for bulk RAG indexing job {JobId}: Filter={Filter}, MatterId={MatterId}, MaxDocuments={MaxDocuments}",
                job.JobId, payload.Filter, payload.MatterId, payload.MaxDocuments);

            var documents = await QueryDocumentsAsync(payload, ct);

            if (documents.Count == 0)
            {
                _logger.LogInformation("No documents found matching bulk indexing criteria for job {JobId}", job.JobId);
                await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Completed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "Found {Count} documents to index for bulk RAG job {JobId}",
                documents.Count, job.JobId);

            // Mark job as started with actual document count
            await _statusStore.MarkJobStartedAsync(jobIdStr, documents.Count, ct);

            // Process documents with bounded concurrency
            var result = await ProcessDocumentsWithConcurrencyAsync(
                job, payload, documents, ct);

            // Determine final status
            var finalState = DetermineFinalState(result);
            await _statusStore.MarkJobCompletedAsync(jobIdStr, finalState, ct);

            _logger.LogInformation(
                "Bulk RAG indexing job {JobId} completed: {Processed} processed, {Errors} errors, {Skipped} skipped. Final state: {State}",
                job.JobId, result.ProcessedCount, result.ErrorCount, result.SkippedCount, finalState);

            _telemetry.RecordBulkRagIndexingCompleted(
                result.ProcessedCount,
                result.ErrorCount,
                result.SkippedCount,
                stopwatch.Elapsed);

            return finalState == BatchJobState.Failed
                ? JobOutcome.Failure(job.JobId, JobType, $"Bulk indexing failed with {result.ErrorCount} errors", job.Attempt, stopwatch.Elapsed)
                : JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk RAG indexing job {JobId} failed: {Error}", job.JobId, ex.Message);
            await _statusStore.MarkJobCompletedAsync(jobIdStr, BatchJobState.Failed, ct);
            return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private async Task<BulkIndexResult> ProcessDocumentsWithConcurrencyAsync(
        JobContract batchJob,
        BulkRagIndexingPayload payload,
        List<DocumentInfo> documents,
        CancellationToken ct)
    {
        var jobIdStr = batchJob.JobId.ToString();
        var maxConcurrency = payload.MaxConcurrency > 0
            ? payload.MaxConcurrency
            : DefaultMaxConcurrency;

        var result = new BulkIndexResult();
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var processingTasks = new List<Task>();
        var processedLock = new object();

        _logger.LogDebug(
            "Processing {Count} documents with max concurrency {MaxConcurrency}",
            documents.Count, maxConcurrency);

        foreach (var doc in documents)
        {
            ct.ThrowIfCancellationRequested();

            await semaphore.WaitAsync(ct);

            var task = Task.Run(async () =>
            {
                try
                {
                    var outcome = await ProcessSingleDocumentAsync(
                        batchJob, doc, payload, ct);

                    lock (processedLock)
                    {
                        switch (outcome)
                        {
                            case DocumentIndexOutcome.Success:
                                result.ProcessedCount++;
                                break;
                            case DocumentIndexOutcome.Skipped:
                                result.SkippedCount++;
                                break;
                            case DocumentIndexOutcome.Failed:
                                result.ErrorCount++;
                                break;
                        }

                        // Update status periodically
                        var totalHandled = result.ProcessedCount + result.ErrorCount + result.SkippedCount;
                        if (totalHandled % ProgressUpdateInterval == 0 || totalHandled == documents.Count)
                        {
                            _ = _statusStore.UpdateProgressAsync(
                                jobIdStr,
                                result.ProcessedCount,
                                result.ErrorCount,
                                result.SkippedCount,
                                ct);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct);

            processingTasks.Add(task);
        }

        // Wait for all tasks to complete
        await Task.WhenAll(processingTasks);

        // Final status update
        await _statusStore.UpdateProgressAsync(
            jobIdStr,
            result.ProcessedCount,
            result.ErrorCount,
            result.SkippedCount,
            ct);

        return result;
    }

    private async Task<DocumentIndexOutcome> ProcessSingleDocumentAsync(
        JobContract batchJob,
        DocumentInfo doc,
        BulkRagIndexingPayload payload,
        CancellationToken ct)
    {
        var jobIdStr = batchJob.JobId.ToString();
        var idempotencyKey = $"rag-index-{doc.DriveId}-{doc.ItemId}";

        try
        {
            // Check if already processed (skip duplicates) unless force reindex
            if (!payload.ForceReindex &&
                await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogDebug("Skipping already indexed document {DocumentId} ({FileName})", doc.DocumentId, doc.FileName);
                return DocumentIndexOutcome.Skipped;
            }

            // Skip documents without file references
            if (string.IsNullOrEmpty(doc.DriveId) || string.IsNullOrEmpty(doc.ItemId))
            {
                _logger.LogDebug("Skipping document {DocumentId} without SPE file reference", doc.DocumentId);
                return DocumentIndexOutcome.Skipped;
            }

            // Build parent entity context if matter is associated
            ParentEntityContext? parentEntity = null;
            if (!string.IsNullOrEmpty(doc.MatterId))
            {
                parentEntity = new ParentEntityContext(
                    EntityType: "matter",
                    EntityId: doc.MatterId,
                    EntityName: doc.MatterName ?? "Unknown Matter"
                );
            }


            // Build the file index request
            var request = new FileIndexRequest
            {
                TenantId = payload.TenantId,
                DriveId = doc.DriveId,
                ItemId = doc.ItemId,
                FileName = doc.FileName,
                DocumentId = doc.DocumentId,
                ParentEntity = parentEntity,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "BulkIndexing",
                    ["batchJobId"] = batchJob.JobId.ToString()
                }
            };

            // Call FileIndexingService using app-only authentication
            var result = await _fileIndexingService.IndexFileAppOnlyAsync(request, ct);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Failed to index document {DocumentId} ({FileName}): {Error}",
                    doc.DocumentId, doc.FileName, result.ErrorMessage);

                await _statusStore.RecordErrorAsync(
                    jobIdStr,
                    doc.DocumentId,
                    result.ErrorMessage ?? "Indexing failed",
                    ct);

                return DocumentIndexOutcome.Failed;
            }

            // Mark as processed
            await _idempotencyService.MarkEventAsProcessedAsync(
                idempotencyKey,
                TimeSpan.FromDays(7),
                ct);

            _logger.LogDebug(
                "Successfully indexed document {DocumentId} ({FileName}), {ChunksIndexed} chunks",
                doc.DocumentId, doc.FileName, result.ChunksIndexed);

            return DocumentIndexOutcome.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process document {DocumentId} in bulk indexing", doc.DocumentId);
            await _statusStore.RecordErrorAsync(
                jobIdStr,
                doc.DocumentId,
                ex.Message,
                ct);
            return DocumentIndexOutcome.Failed;
        }
    }

    private async Task<List<DocumentInfo>> QueryDocumentsAsync(
        BulkRagIndexingPayload payload,
        CancellationToken ct)
    {
        var documents = new List<DocumentInfo>();
        var maxDocuments = payload.MaxDocuments > 0 ? payload.MaxDocuments : DefaultMaxDocuments;

        // Build OData filter
        var filters = new List<string>
        {
            "sprk_hasfile eq true" // Only documents with associated files
        };

        // Filter by unindexed (default behavior) - skip this filter when ForceReindex is true
        if (!payload.ForceReindex && (payload.Filter == "unindexed" || string.IsNullOrEmpty(payload.Filter)))
        {
            // sprk_ragindexedon is null means not indexed
            filters.Add("sprk_ragindexedon eq null");
        }

        // Filter by Matter ID
        if (!string.IsNullOrEmpty(payload.MatterId))
        {
            filters.Add($"_sprk_matterid_value eq {payload.MatterId}");
        }

        // Filter by date range
        if (payload.CreatedAfter.HasValue)
        {
            filters.Add($"createdon ge {payload.CreatedAfter.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }
        if (payload.CreatedBefore.HasValue)
        {
            filters.Add($"createdon le {payload.CreatedBefore.Value:yyyy-MM-ddTHH:mm:ssZ}");
        }

        // Filter by document type
        if (!string.IsNullOrEmpty(payload.DocumentType))
        {
            filters.Add($"sprk_filetype eq '{payload.DocumentType}'");
        }

        var filterStr = string.Join(" and ", filters);

        // Select fields needed for indexing, including parent entity (matter) via $expand
        var selectFields = "sprk_documentid,sprk_filename,sprk_graphdriveid,sprk_graphitemid,_sprk_matterid_value";
        var expandClause = "sprk_Matter($select=sprk_name,sprk_matterid)";
        var url = $"sprk_documents?$select={selectFields}&$expand={expandClause}&$filter={filterStr}&$top={maxDocuments}&$orderby=createdon asc";

        _logger.LogInformation("Querying documents with URL: {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "Dataverse query failed with status {StatusCode}: {ErrorBody}",
                response.StatusCode, errorBody);
            throw new HttpRequestException($"Dataverse query failed: {response.StatusCode} - {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        _logger.LogInformation("Dataverse query returned {Length} bytes", json.Length);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        if (data.TryGetProperty("value", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                // Extract parent entity (matter) from expanded navigation property
                string? matterId = null;
                string? matterName = null;

                if (item.TryGetProperty("sprk_Matter", out var matterObj) &&
                    matterObj.ValueKind == JsonValueKind.Object)
                {
                    matterId = matterObj.TryGetProperty("sprk_matterid", out var mId)
                        ? mId.GetString()
                        : null;
                    matterName = matterObj.TryGetProperty("sprk_name", out var mName)
                        ? mName.GetString()
                        : null;
                }

                // Fallback to lookup value if expand didn't work
                if (string.IsNullOrEmpty(matterId) &&
                    item.TryGetProperty("_sprk_matterid_value", out var lookupValue))
                {
                    matterId = lookupValue.GetString();
                }


                var docInfo = new DocumentInfo
                {
                    DocumentId = item.TryGetProperty("sprk_documentid", out var id)
                        ? id.GetString() ?? string.Empty
                        : string.Empty,
                    FileName = item.TryGetProperty("sprk_filename", out var name)
                        ? name.GetString() ?? string.Empty
                        : string.Empty,
                    DriveId = item.TryGetProperty("sprk_graphdriveid", out var driveId)
                        ? driveId.GetString() ?? string.Empty
                        : string.Empty,
                    ItemId = item.TryGetProperty("sprk_graphitemid", out var itemId)
                        ? itemId.GetString() ?? string.Empty
                        : string.Empty,
                    MatterId = matterId,
                    MatterName = matterName
                };

                if (!string.IsNullOrEmpty(docInfo.DocumentId) &&
                    !string.IsNullOrEmpty(docInfo.DriveId) &&
                    !string.IsNullOrEmpty(docInfo.ItemId))
                {
                    documents.Add(docInfo);
                }
            }
        }

        return documents;
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
        {
            var scope = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/') + "/.default";
            _currentToken = await _credential.GetTokenAsync(
                new TokenRequestContext([scope!]),
                ct);

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);

            _logger.LogDebug("Refreshed Dataverse authentication token for bulk RAG indexing");
        }
    }

    private BulkRagIndexingPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<BulkRagIndexingPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse bulk RAG indexing job payload");
            return null;
        }
    }

    private static BatchJobState DetermineFinalState(BulkIndexResult result)
    {
        if (result.ProcessedCount == 0 && result.ErrorCount > 0)
        {
            return BatchJobState.Failed;
        }
        if (result.ErrorCount > 0)
        {
            return BatchJobState.PartiallyCompleted;
        }
        return BatchJobState.Completed;
    }

    private enum DocumentIndexOutcome
    {
        Success,
        Skipped,
        Failed
    }

    private class BulkIndexResult
    {
        public int ProcessedCount { get; set; }
        public int ErrorCount { get; set; }
        public int SkippedCount { get; set; }
    }

    private class DocumentInfo
    {
        public string DocumentId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string DriveId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;

        /// <summary>
        /// Parent entity (matter) ID for entity-scoped search.
        /// </summary>
        public string? MatterId { get; set; }

        /// <summary>
        /// Parent entity (matter) display name for search result presentation.
        /// </summary>
        public string? MatterName { get; set; }
    }
}

/// <summary>
/// Payload structure for bulk RAG indexing jobs.
/// </summary>
public class BulkRagIndexingPayload
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Filter type: "unindexed" (default), "all", or custom filter.
    /// </summary>
    public string Filter { get; set; } = "unindexed";

    /// <summary>
    /// Optional Matter ID to filter documents.
    /// </summary>
    public string? MatterId { get; set; }

    /// <summary>
    /// Optional: Only index documents created after this date.
    /// </summary>
    public DateTime? CreatedAfter { get; set; }

    /// <summary>
    /// Optional: Only index documents created before this date.
    /// </summary>
    public DateTime? CreatedBefore { get; set; }

    /// <summary>
    /// Optional document type filter (e.g., ".pdf", ".docx").
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>
    /// Maximum number of documents to process in this batch.
    /// </summary>
    public int MaxDocuments { get; set; } = 1000;

    /// <summary>
    /// Maximum concurrent document processing.
    /// </summary>
    public int MaxConcurrency { get; set; } = 5;

    /// <summary>
    /// If true, reindex documents even if they have been indexed before.
    /// </summary>
    public bool ForceReindex { get; set; } = false;

    /// <summary>
    /// Source identifier for tracking (e.g., "Admin", "Scheduled", "API").
    /// </summary>
    public string? Source { get; set; }
}
