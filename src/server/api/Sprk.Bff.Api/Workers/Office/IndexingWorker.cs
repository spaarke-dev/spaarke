using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;
using Sprk.Bff.Api.Workers.Office.Messages;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Worker that indexes documents in Azure AI Search for RAG retrieval.
/// Handles the Indexing stage of the Office document processing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This worker processes documents after the profile extraction stage and adds them
/// to the Azure AI Search index for AI-powered retrieval. The pipeline flow is:
/// </para>
/// <list type="number">
/// <item>Check if RAG indexing is enabled in job options</item>
/// <item>Skip if not enabled (optional processing per spec)</item>
/// <item>Retrieve document content from SPE</item>
/// <item>Chunk document content for embedding</item>
/// <item>Generate embeddings via Azure OpenAI</item>
/// <item>Index chunks in Azure AI Search</item>
/// <item>Update job status to "Indexed"</item>
/// </list>
/// <para>
/// Per spec.md and ADR-004, the indexing worker is optional - if it fails, the document
/// remains accessible but won't be found via AI search.
/// </para>
/// <para>
/// Follows ADR-001 (BackgroundService pattern), ADR-004 (job contract),
/// and ADR-015 (data governance - no document content in logs).
/// </para>
/// </remarks>
public class IndexingWorker : IOfficeJobHandler
{
    private readonly IFileIndexingService _fileIndexingService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IOfficeJobStatusService _jobStatusService;
    private readonly RagTelemetry _telemetry;
    private readonly ILogger<IndexingWorker> _logger;

    /// <summary>
    /// Processing stage this handler is responsible for.
    /// </summary>
    public OfficeJobType JobType => OfficeJobType.Indexing;

    public IndexingWorker(
        IFileIndexingService fileIndexingService,
        IIdempotencyService idempotencyService,
        IOfficeJobStatusService jobStatusService,
        RagTelemetry telemetry,
        ILogger<IndexingWorker> logger)
    {
        _fileIndexingService = fileIndexingService ?? throw new ArgumentNullException(nameof(fileIndexingService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _jobStatusService = jobStatusService ?? throw new ArgumentNullException(nameof(jobStatusService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<JobOutcome> ProcessAsync(OfficeJobMessage message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Processing indexing job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
            message.JobId, message.SubjectId, message.Attempt, message.CorrelationId);

        try
        {
            // Step 1: Parse and validate payload
            var payload = ParsePayload(message.Payload);
            if (payload == null)
            {
                _logger.LogError("Invalid payload for indexing job {JobId}", message.JobId);
                _telemetry.RecordRagIndexingJobFailure("invalid_payload");
                return JobOutcome.Failure("OFFICE_001", "Invalid job payload");
            }

            // Step 2: Check if RAG indexing is enabled
            if (!payload.AiOptions?.RagIndex ?? false)
            {
                _logger.LogInformation(
                    "RAG indexing skipped for job {JobId} - option not enabled",
                    message.JobId);

                // Update status to complete (skipped)
                await _jobStatusService.UpdateJobPhaseAsync(
                    message.JobId,
                    "Indexed",
                    "Skipped",
                    cancellationToken);

                return JobOutcome.Success();
            }

            // Step 3: Check idempotency - prevent duplicate processing
            var idempotencyKey = message.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"rag-index-{payload.DriveId}-{payload.ItemId}";
            }

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, cancellationToken))
            {
                _logger.LogInformation(
                    "Document already indexed (idempotency key: {IdempotencyKey})",
                    idempotencyKey);

                _telemetry.RecordRagIndexingJobSkippedDuplicate();
                return JobOutcome.Success();
            }

            // Step 4: Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(10), cancellationToken))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for document (idempotency key: {IdempotencyKey})",
                    idempotencyKey);

                // Return success to prevent retry - another instance is processing
                _telemetry.RecordRagIndexingJobSkippedDuplicate();
                return JobOutcome.Success();
            }

            try
            {
                // Step 5: Update job status to running
                await _jobStatusService.UpdateJobPhaseAsync(
                    message.JobId,
                    "Indexing",
                    "Running",
                    cancellationToken);

                // Step 6: Build file index request and execute indexing pipeline
                // FileIndexingService handles: download -> text extraction -> chunking -> embedding -> indexing
                var indexRequest = new FileIndexRequest
                {
                    TenantId = payload.TenantId,
                    DriveId = payload.DriveId,
                    ItemId = payload.ItemId,
                    FileName = payload.FileName,
                    DocumentId = payload.DocumentId,
                    KnowledgeSourceId = payload.KnowledgeSourceId,
                    KnowledgeSourceName = payload.KnowledgeSourceName,
                    Metadata = payload.Metadata
                };

                var result = await _fileIndexingService.IndexFileAppOnlyAsync(indexRequest, cancellationToken);

                if (!result.Success)
                {
                    _logger.LogError(
                        "RAG indexing failed for document {DocumentId}: {Error}",
                        payload.DocumentId, result.ErrorMessage);

                    // Update job status with error
                    await _jobStatusService.UpdateJobPhaseAsync(
                        message.JobId,
                        "Indexing",
                        "Failed",
                        cancellationToken,
                        result.ErrorMessage);

                    // Check if this is a permanent or transient failure
                    if (IsPermanentFailure(result.ErrorMessage))
                    {
                        _telemetry.RecordRagIndexingJobFailure("permanent_failure");
                        return JobOutcome.Failure("OFFICE_012", result.ErrorMessage ?? "RAG indexing failed");
                    }

                    // Transient failure - allow retry
                    _telemetry.RecordRagIndexingJobFailure("transient_failure");
                    return JobOutcome.Failure("OFFICE_012", result.ErrorMessage ?? "RAG indexing failed", retryable: true);
                }

                // Step 7: Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    cancellationToken);

                // Step 8: Update job status to complete
                await _jobStatusService.UpdateJobPhaseAsync(
                    message.JobId,
                    "Indexed",
                    "Completed",
                    cancellationToken);

                // Step 9: Record telemetry
                _telemetry.RecordRagIndexingJobSuccess(stopwatch.Elapsed, result.ChunksIndexed);

                _logger.LogInformation(
                    "Indexing job {JobId} completed in {Duration}ms, {ChunksIndexed} chunks indexed",
                    message.JobId, stopwatch.ElapsedMilliseconds, result.ChunksIndexed);

                return JobOutcome.Success();
            }
            finally
            {
                // Always release the lock
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Indexing job {JobId} failed with exception", message.JobId);

            // Update job status with error
            await _jobStatusService.UpdateJobPhaseAsync(
                message.JobId,
                "Indexing",
                "Failed",
                cancellationToken,
                ex.Message);

            // Check for retryable vs permanent failures
            var isRetryable = IsRetryableException(ex);
            _telemetry.RecordRagIndexingJobFailure(isRetryable ? "transient_error" : "permanent_error");

            return JobOutcome.Failure(
                "OFFICE_012",
                ex.Message,
                retryable: isRetryable);
        }
    }

    /// <summary>
    /// Parses the job payload to extract indexing parameters.
    /// </summary>
    private IndexingJobPayload? ParsePayload(JsonElement payload)
    {
        try
        {
            return JsonSerializer.Deserialize<IndexingJobPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse indexing job payload");
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
               errorMessage.Contains("empty", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("invalid", StringComparison.OrdinalIgnoreCase);
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

        // TaskCanceledException can indicate timeout
        if (ex is TaskCanceledException or OperationCanceledException)
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
/// Payload structure for indexing jobs.
/// Contains all necessary information to index a document for RAG retrieval.
/// </summary>
public record IndexingJobPayload
{
    /// <summary>
    /// Tenant ID for multi-tenant isolation.
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded drive ID.
    /// </summary>
    public string DriveId { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded item ID.
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// File name for display and extension detection.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// Dataverse document ID (sprk_document).
    /// </summary>
    public string? DocumentId { get; init; }

    /// <summary>
    /// Optional knowledge source ID (sprk_analysisknowledge).
    /// </summary>
    public string? KnowledgeSourceId { get; init; }

    /// <summary>
    /// Optional knowledge source display name.
    /// </summary>
    public string? KnowledgeSourceName { get; init; }

    /// <summary>
    /// Optional metadata dictionary for extensibility.
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// AI processing options.
    /// </summary>
    public AiProcessingOptions? AiOptions { get; init; }
}
