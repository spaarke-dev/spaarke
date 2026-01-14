using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for app-only document analysis jobs.
/// Processes AI Document Profile analysis requests from Service Bus using app-only authentication.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Uses AppOnlyAnalysisService for analysis without requiring user context.
/// </summary>
/// <remarks>
/// This handler enables background AI analysis for documents created via:
/// - Email-to-document automation (FR-10)
/// - Bulk import operations
/// - System integrations
///
/// Idempotency key pattern: analysis-{docId}-documentprofile
/// </remarks>
public class AppOnlyDocumentAnalysisJobHandler : IJobHandler
{
    private readonly IAppOnlyAnalysisService _analysisService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly DocumentTelemetry _telemetry;
    private readonly ILogger<AppOnlyDocumentAnalysisJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing analysis jobs.
    /// </summary>
    public const string JobTypeName = "AppOnlyDocumentAnalysis";

    public AppOnlyDocumentAnalysisJobHandler(
        IAppOnlyAnalysisService analysisService,
        IIdempotencyService idempotencyService,
        DocumentTelemetry telemetry,
        ILogger<AppOnlyDocumentAnalysisJobHandler> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
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
                "Processing app-only document analysis job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get documentId and optional playbook name
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.DocumentId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for analysis job {JobId}", job.JobId);
                _telemetry.RecordAnalysisJobFailure("invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var documentId = payload.DocumentId;
            var playbookName = payload.PlaybookName; // Optional - will use default if null

            _logger.LogDebug(
                "Processing analysis for document {DocumentId}, playbook: {PlaybookName}",
                documentId, playbookName ?? AppOnlyAnalysisService.DefaultPlaybookName);

            // Build idempotency key - per ADR-004 pattern: analysis-{docId}-{analysisType}
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"analysis-{documentId}-documentprofile";
            }

            // Check idempotency - prevent duplicate processing
            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Document {DocumentId} analysis already processed (idempotency key: {IdempotencyKey})",
                    documentId, idempotencyKey);

                _telemetry.RecordAnalysisJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(10), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for document {DocumentId} (idempotency key: {IdempotencyKey})",
                    documentId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                _telemetry.RecordAnalysisJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Call AppOnlyAnalysisService to perform analysis
                var result = await _analysisService.AnalyzeDocumentAsync(documentId, playbookName, ct);

                if (!result.IsSuccess)
                {
                    _logger.LogError(
                        "Analysis failed for document {DocumentId}: {Error}",
                        documentId, result.ErrorMessage);

                    // Check if this is a permanent or transient failure
                    if (IsPermanentFailure(result.ErrorMessage))
                    {
                        _telemetry.RecordAnalysisJobFailure("permanent_failure");
                        return JobOutcome.Poisoned(
                            job.JobId, JobType,
                            $"Analysis failed: {result.ErrorMessage}",
                            job.Attempt, stopwatch.Elapsed);
                    }

                    // Transient failure - allow retry
                    _telemetry.RecordAnalysisJobFailure("transient_failure");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result.ErrorMessage ?? "Analysis failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                _telemetry.RecordAnalysisJobSuccess(stopwatch.Elapsed);

                _logger.LogInformation(
                    "App-only document analysis job {JobId} completed in {Duration}ms for document {DocumentId}",
                    job.JobId, stopwatch.ElapsedMilliseconds, documentId);

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
            _logger.LogError(ex, "App-only document analysis job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Check for retryable vs permanent failures
            var isRetryable = IsRetryableException(ex);
            _telemetry.RecordAnalysisJobFailure(isRetryable ? "transient_error" : "permanent_error");

            if (isRetryable)
            {
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private AppOnlyDocumentAnalysisPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<AppOnlyDocumentAnalysisPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse app-only analysis job payload");
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
               errorMessage.Contains("no file reference", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("Playbook", StringComparison.OrdinalIgnoreCase) &&
               errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase);
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
/// Payload structure for app-only document analysis jobs.
/// </summary>
public class AppOnlyDocumentAnalysisPayload
{
    /// <summary>
    /// The Dataverse document ID to analyze.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Optional playbook name override. If null, uses default "Document Profile".
    /// </summary>
    public string? PlaybookName { get; set; }

    /// <summary>
    /// Source of the analysis request: "EmailAttachment", "BulkImport", "Manual", etc.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// When the job was enqueued.
    /// </summary>
    public DateTimeOffset? EnqueuedAt { get; set; }
}
