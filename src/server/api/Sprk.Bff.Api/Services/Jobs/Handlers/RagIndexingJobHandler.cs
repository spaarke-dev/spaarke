using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for RAG document indexing jobs.
/// Processes document indexing requests from Service Bus using app-only authentication.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Uses FileIndexingService for the actual indexing pipeline.
/// </summary>
/// <remarks>
/// This handler enables background RAG indexing for documents via:
/// - Email-to-document automation (FR-10)
/// - Document events (Phase 4)
/// - Bulk import operations
///
/// Idempotency key pattern: rag-index-{driveId}-{itemId}
/// </remarks>
public class RagIndexingJobHandler : IJobHandler
{
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IDocumentDataverseService _documentService;
    private readonly ISearchIndexNameResolver _searchIndexNameResolver;
    private readonly AnalysisOptions _analysisOptions;
    private readonly RagTelemetry _telemetry;
    private readonly ILogger<RagIndexingJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing RAG indexing jobs.
    /// </summary>
    public const string JobTypeName = "RagIndexing";

    public RagIndexingJobHandler(
        IFileIndexingService fileIndexingService,
        IIdempotencyService idempotencyService,
        IDocumentDataverseService documentService,
        ISearchIndexNameResolver searchIndexNameResolver,
        IOptions<AnalysisOptions> analysisOptions,
        RagTelemetry telemetry,
        ILogger<RagIndexingJobHandler> logger)
    {
        _fileIndexingService = fileIndexingService ?? throw new ArgumentNullException(nameof(fileIndexingService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _searchIndexNameResolver = searchIndexNameResolver ?? throw new ArgumentNullException(nameof(searchIndexNameResolver));
        _analysisOptions = analysisOptions?.Value ?? throw new ArgumentNullException(nameof(analysisOptions));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing RAG indexing job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get indexing parameters
            var payload = ParsePayload(job.Payload);
            if (payload == null || string.IsNullOrEmpty(payload.DriveId) || string.IsNullOrEmpty(payload.ItemId))
            {
                _logger.LogError("Invalid payload for RAG indexing job {JobId}", job.JobId);
                _telemetry.RecordRagIndexingJobFailure("invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            _logger.LogDebug(
                "Processing RAG indexing for file {FileName} (DriveId: {DriveId}, ItemId: {ItemId})",
                payload.FileName, payload.DriveId, payload.ItemId);

            // Build idempotency key - per ADR-004 pattern: rag-index-{driveId}-{itemId}
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"rag-index-{payload.DriveId}-{payload.ItemId}";
            }

            // Check idempotency - prevent duplicate processing
            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "File {FileName} already indexed (idempotency key: {IdempotencyKey})",
                    payload.FileName, idempotencyKey);

                _telemetry.RecordRagIndexingJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(10), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for file {FileName} (idempotency key: {IdempotencyKey})",
                    payload.FileName, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                _telemetry.RecordRagIndexingJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — resolve the
                // per-record sprk_searchindexname for the indexing batch. Precedence:
                //   (a) payload.SearchIndexName (set by enqueueing site — preferred);
                //   (b) ISearchIndexNameResolver chain (document → parent → BU) — defensive
                //       fall-back when enqueueing site did not set it;
                //   (c) null → tenant default (FR-BFF-04 backward-compat).
                var resolvedSearchIndexName = payload.SearchIndexName;
                if (string.IsNullOrWhiteSpace(resolvedSearchIndexName))
                {
                    resolvedSearchIndexName = await _searchIndexNameResolver.ResolveAsync(
                        payload.DocumentId,
                        payload.ParentEntity?.EntityType,
                        payload.ParentEntity?.EntityId,
                        ct);
                }

                // Build the file index request
                var request = new FileIndexRequest
                {
                    TenantId = payload.TenantId,
                    DriveId = payload.DriveId,
                    ItemId = payload.ItemId,
                    FileName = payload.FileName,
                    DocumentId = payload.DocumentId,
                    KnowledgeSourceId = payload.KnowledgeSourceId,
                    KnowledgeSourceName = payload.KnowledgeSourceName,
                    Metadata = payload.Metadata,
                    ParentEntity = payload.ParentEntity,
                    SearchIndexName = resolvedSearchIndexName
                };

                // Call FileIndexingService using app-only authentication.
                // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — per the user-
                // confirmed decision in the task brief, background jobs default-fall-back when
                // the allow-list rejects the resolved searchIndexName: log a WARN and retry the
                // call with SearchIndexName=null so the batch still lands in the tenant-default
                // index. The OBO path (RagEndpoints.IndexFile) still hard-fails — that contract
                // (400 ProblemDetails INDEX_NOT_ALLOWED) is preserved upstream.
                FileIndexingResult result;
                try
                {
                    result = await _fileIndexingService.IndexFileAppOnlyAsync(request, ct);
                }
                catch (SdapProblemException ex) when (ex.Code == "INDEX_NOT_ALLOWED")
                {
                    _logger.LogWarning(
                        "RAG indexing rejected by AI Search allow-list: requested SearchIndexName={RejectedIndex} (DocumentId={DocumentId}). Falling back to tenant default for job {JobId}.",
                        request.SearchIndexName ?? "(null)", payload.DocumentId ?? "(null)", job.JobId);

                    var fallbackRequest = request with { SearchIndexName = null };
                    result = await _fileIndexingService.IndexFileAppOnlyAsync(fallbackRequest, ct);
                }

                if (!result.Success)
                {
                    _logger.LogError(
                        "RAG indexing failed for file {FileName}: {Error}",
                        payload.FileName, result.ErrorMessage);

                    // Check if this is a permanent or transient failure
                    if (IsPermanentFailure(result.ErrorMessage))
                    {
                        _telemetry.RecordRagIndexingJobFailure("permanent_failure");
                        return JobOutcome.Poisoned(
                            job.JobId, JobType,
                            $"RAG indexing failed: {result.ErrorMessage}",
                            job.Attempt, stopwatch.Elapsed);
                    }

                    // Transient failure - allow retry
                    _telemetry.RecordRagIndexingJobFailure("transient_failure");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result.ErrorMessage ?? "RAG indexing failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                // Update Dataverse tracking fields when DocumentId is provided
                // Same pattern as RagEndpoints.cs manual indexing.
                // R3 FR-3H3.2 dual-write: set new sprk_searchindexcompletedon AND keep legacy
                // sprk_searchindexed=true + sprk_searchindexedon for the transition window
                // (R3 + one sprint per spec assumption line 366). Removal deferred to R4.
                if (!string.IsNullOrEmpty(payload.DocumentId))
                {
                    try
                    {
                        var completedAt = DateTime.UtcNow;
                        var updateRequest = new UpdateDocumentRequest
                        {
                            // New canonical lifecycle marker (R3+)
                            SearchIndexCompletedOn = completedAt,
                            // Legacy dual-write (preserved during transition)
                            SearchIndexed = true,
                            SearchIndexedOn = completedAt,
                            // Index routing (unchanged)
                            SearchIndexName = _analysisOptions.SharedIndexName
                        };

                        await _documentService.UpdateDocumentAsync(payload.DocumentId, updateRequest, ct);

                        _logger.LogInformation(
                            "Updated Dataverse search index tracking for document {DocumentId}: SearchIndexCompletedOn={CompletedAt}, SearchIndexed=true (dual-write), IndexName={IndexName}",
                            payload.DocumentId, completedAt, _analysisOptions.SharedIndexName);
                    }
                    catch (Exception ex)
                    {
                        // Log but don't fail - indexing succeeded, Dataverse update is non-critical
                        _logger.LogWarning(ex,
                            "Failed to update Dataverse search index tracking for document {DocumentId}: {Error}. Indexing was successful.",
                            payload.DocumentId, ex.Message);
                    }
                }

                _telemetry.RecordRagIndexingJobSuccess(stopwatch.Elapsed, result.ChunksIndexed);

                _logger.LogInformation(
                    "RAG indexing job {JobId} completed in {Duration}ms for file {FileName}, {ChunksIndexed} chunks indexed",
                    job.JobId, stopwatch.ElapsedMilliseconds, payload.FileName, result.ChunksIndexed);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            finally
            {
                // Always release the lock
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RAG indexing job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Check for retryable vs permanent failures
            var isRetryable = IsRetryableException(ex);
            _telemetry.RecordRagIndexingJobFailure(isRetryable ? "transient_error" : "permanent_error");

            if (isRetryable)
            {
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private RagIndexingJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<RagIndexingJobPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse RAG indexing job payload");
            return null;
        }
    }

    /// <summary>
    /// Determines if the failure is permanent (should not retry).
    /// </summary>
    private static bool IsPermanentFailure(string? errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
            return false;

        // Permanent failures that should not be retried
        return errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("not supported", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("extraction failed", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if an exception represents a transient failure that should be retried.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), timeouts
        if (ex is HttpRequestException)
        {
            return true;
        }

        // Check for known transient exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Transient", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for RAG indexing jobs.
/// </summary>
public class RagIndexingJobPayload
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded drive ID.
    /// </summary>
    public string DriveId { get; set; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded item ID.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// File name for display and extension detection.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Optional Dataverse document ID (sprk_document).
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// Optional knowledge source ID (sprk_analysisknowledge).
    /// </summary>
    public string? KnowledgeSourceId { get; set; }

    /// <summary>
    /// Optional knowledge source display name.
    /// </summary>
    public string? KnowledgeSourceName { get; set; }

    /// <summary>
    /// Optional metadata dictionary for extensibility.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Optional parent entity context for entity-scoped search.
    /// When provided, enables filtering search results by the business entity
    /// (Matter, Project, Invoice, Account, Contact) that owns this document.
    /// </summary>
    public ParentEntityContext? ParentEntity { get; set; }

    /// <summary>
    /// Source of the indexing request: "EmailAttachment", "DocumentEvent", "Manual", etc.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// When the job was enqueued.
    /// </summary>
    public DateTimeOffset? EnqueuedAt { get; set; }

    /// <summary>
    /// multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — optional explicit
    /// Azure AI Search index name to route this batch to. When set by the enqueueing site
    /// (e.g., Office Add-in's UploadFinalizationWorker or the Email-to-Document
    /// IncomingCommunicationProcessor), the handler passes it verbatim to
    /// <see cref="FileIndexRequest.SearchIndexName"/>. When null/empty, the handler invokes
    /// <see cref="ISearchIndexNameResolver"/> as a defensive fall-back (chain:
    /// sprk_document → parent record → parent's owning BU). Allow-list validation surfaces
    /// in <see cref="IKnowledgeDeploymentService"/> (FR-BFF-02 / NFR-08); the handler's
    /// per-task brief mandates default-fall-back behavior for background jobs (catch
    /// SdapProblemException(INDEX_NOT_ALLOWED) and retry with SearchIndexName=null).
    /// </summary>
    public string? SearchIndexName { get; set; }
}
