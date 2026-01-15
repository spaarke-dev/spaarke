using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for email analysis jobs.
/// Processes comprehensive email + attachment AI analysis requests from Service Bus.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Uses AppOnlyAnalysisService.AnalyzeEmailAsync for combined email+attachment analysis.
/// </summary>
/// <remarks>
/// This handler enables background AI analysis of emails including:
/// - Email metadata (subject, from, to, date)
/// - Email body text
/// - All attachment text extraction and combination
/// - Results stored on the main .eml Document record (FR-11, FR-12)
///
/// Idempotency key pattern: emailanalysis-{emailId}
/// </remarks>
public class EmailAnalysisJobHandler : IJobHandler
{
    private readonly IAppOnlyAnalysisService _analysisService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly DocumentTelemetry _telemetry;
    private readonly ILogger<EmailAnalysisJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing email analysis jobs.
    /// </summary>
    public const string JobTypeName = "EmailAnalysis";

    public EmailAnalysisJobHandler(
        IAppOnlyAnalysisService analysisService,
        IIdempotencyService idempotencyService,
        DocumentTelemetry telemetry,
        ILogger<EmailAnalysisJobHandler> logger)
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
                "Processing email analysis job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get emailId
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.EmailId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for email analysis job {JobId}", job.JobId);
                _telemetry.RecordAnalysisJobFailure("invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var emailId = payload.EmailId;

            _logger.LogDebug("Processing email analysis for email activity {EmailId}", emailId);

            // Build idempotency key - per ADR-004 pattern: emailanalysis-{emailId}
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"emailanalysis-{emailId}";
            }

            // Check idempotency - prevent duplicate processing
            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Email {EmailId} analysis already processed (idempotency key: {IdempotencyKey})",
                    emailId, idempotencyKey);

                _telemetry.RecordAnalysisJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(10), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for email {EmailId} (idempotency key: {IdempotencyKey})",
                    emailId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                _telemetry.RecordAnalysisJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Call AppOnlyAnalysisService to perform email analysis
                var result = await _analysisService.AnalyzeEmailAsync(emailId, ct);

                if (!result.IsSuccess)
                {
                    _logger.LogError(
                        "Email analysis failed for email {EmailId}: {Error}",
                        emailId, result.ErrorMessage);

                    // Check if this is a permanent or transient failure
                    if (IsPermanentFailure(result.ErrorMessage))
                    {
                        _telemetry.RecordAnalysisJobFailure("permanent_failure");
                        return JobOutcome.Poisoned(
                            job.JobId, JobType,
                            $"Email analysis failed: {result.ErrorMessage}",
                            job.Attempt, stopwatch.Elapsed);
                    }

                    // Transient failure - allow retry
                    _telemetry.RecordAnalysisJobFailure("transient_failure");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result.ErrorMessage ?? "Email analysis failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                _telemetry.RecordAnalysisJobSuccess(stopwatch.Elapsed);

                _logger.LogInformation(
                    "Email analysis job {JobId} completed in {Duration}ms for email {EmailId}, attachments analyzed: {AttachmentCount}",
                    job.JobId, stopwatch.ElapsedMilliseconds, emailId, result.AttachmentsAnalyzed);

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
            _logger.LogError(ex, "Email analysis job {JobId} failed: {Error}", job.JobId, ex.Message);

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

    private EmailAnalysisPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<EmailAnalysisPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse email analysis job payload");
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
               errorMessage.Contains("No .eml document", StringComparison.OrdinalIgnoreCase) ||
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
/// Payload structure for email analysis jobs.
/// </summary>
public class EmailAnalysisPayload
{
    /// <summary>
    /// The Dataverse email activity ID to analyze.
    /// </summary>
    public Guid EmailId { get; set; }

    /// <summary>
    /// Source of the analysis request: "EmailToDocument", "Manual", "RibbonButton", etc.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// When the job was enqueued.
    /// </summary>
    public DateTimeOffset? EnqueuedAt { get; set; }
}
