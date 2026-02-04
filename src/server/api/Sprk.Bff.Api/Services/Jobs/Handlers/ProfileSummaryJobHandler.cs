using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for profile summary extraction from documents.
/// Processes AI Document Profile analysis for Office add-in saved content.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Follows ADR-001 for BackgroundService pattern.
/// Uses AppOnlyAnalysisService for Document Intelligence + OpenAI analysis.
/// </summary>
/// <remarks>
/// This handler is triggered after a document is saved from the Office add-in.
/// It:
/// 1. Checks if AI processing was enabled in the save request
/// 2. Retrieves document content from SPE
/// 3. Runs Document Intelligence for text extraction
/// 4. Runs OpenAI for summary and entity extraction
/// 5. Updates Document metadata in Dataverse
/// 6. Queues the next stage (indexing) if configured
///
/// Idempotency key pattern: profile-{documentId}
///
/// Part of the SDAP Office Integration project (Task 062).
/// </remarks>
public class ProfileSummaryJobHandler : IJobHandler
{
    private readonly IAppOnlyAnalysisService _analysisService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly DocumentTelemetry _telemetry;
    private readonly ILogger<ProfileSummaryJobHandler> _logger;
    private readonly string _indexingQueueName;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing profile jobs.
    /// </summary>
    public const string JobTypeName = "ProfileSummary";

    /// <summary>
    /// Job type for the next stage (indexing).
    /// </summary>
    public const string IndexingJobType = "RagIndexing";

    public ProfileSummaryJobHandler(
        IAppOnlyAnalysisService analysisService,
        IIdempotencyService idempotencyService,
        ServiceBusClient serviceBusClient,
        DocumentTelemetry telemetry,
        IOptions<ServiceBusOptions> serviceBusOptions,
        ILogger<ProfileSummaryJobHandler> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _indexingQueueName = options.QueueName;
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing profile summary job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.DocumentId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for profile summary job {JobId}", job.JobId);
                _telemetry.RecordAnalysisJobFailure("invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var documentId = payload.DocumentId;

            // Check if AI processing was enabled for this save
            if (!payload.TriggerAiProcessing)
            {
                _logger.LogInformation(
                    "AI processing not enabled for document {DocumentId}, skipping profile extraction",
                    documentId);

                // Still queue indexing if configured
                if (payload.QueueIndexing)
                {
                    await QueueIndexingJobAsync(job, payload, ct);
                }

                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogDebug(
                "Processing profile for document {DocumentId}, ContentType: {ContentType}, Source: {Source}",
                documentId, payload.ContentType, payload.Source ?? "Office");

            // Build idempotency key - per ADR-004 pattern: profile-{docId}
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"profile-{documentId}";
            }

            // Check idempotency - prevent duplicate processing
            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Document {DocumentId} profile already processed (idempotency key: {IdempotencyKey})",
                    documentId, idempotencyKey);

                _telemetry.RecordAnalysisJobSkippedDuplicate();

                // Still queue indexing for consistency
                if (payload.QueueIndexing)
                {
                    await QueueIndexingJobAsync(job, payload, ct);
                }

                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(15), ct))
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
                // Determine the appropriate playbook based on content type
                var playbookName = GetPlaybookName(payload.ContentType);

                // Call AppOnlyAnalysisService to perform analysis
                // This handles:
                // 1. Retrieving document from SPE
                // 2. Running Document Intelligence for text extraction
                // 3. Running OpenAI for summary/entity extraction
                // 4. Updating Dataverse Document record with profile data
                var result = await _analysisService.AnalyzeDocumentAsync(documentId, playbookName, ct);

                if (!result.IsSuccess)
                {
                    _logger.LogError(
                        "Profile extraction failed for document {DocumentId}: {Error}",
                        documentId, result.ErrorMessage);

                    // Check if this is a permanent or transient failure
                    if (IsPermanentFailure(result.ErrorMessage))
                    {
                        _telemetry.RecordAnalysisJobFailure("permanent_failure", result.AnalysisId);

                        // Still queue indexing with partial data
                        if (payload.QueueIndexing)
                        {
                            await QueueIndexingJobAsync(job, payload, ct);
                        }

                        return JobOutcome.Poisoned(
                            job.JobId, JobType,
                            $"Profile extraction failed: {result.ErrorMessage}",
                            job.Attempt, stopwatch.Elapsed);
                    }

                    // Transient failure - allow retry
                    _telemetry.RecordAnalysisJobFailure("transient_failure", result.AnalysisId);
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result.ErrorMessage ?? "Profile extraction failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                // Queue indexing job as next stage
                if (payload.QueueIndexing)
                {
                    await QueueIndexingJobAsync(job, payload, ct);
                }

                _telemetry.RecordAnalysisJobSuccess(stopwatch.Elapsed, result.AnalysisId);

                _logger.LogInformation(
                    "Profile summary job {JobId} completed in {Duration}ms for document {DocumentId}, AnalysisId {AnalysisId}",
                    job.JobId, stopwatch.ElapsedMilliseconds, documentId, result.AnalysisId);

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
            _logger.LogError(ex, "Profile summary job {JobId} failed: {Error}", job.JobId, ex.Message);

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

    /// <summary>
    /// Queues the next stage (indexing) job via Service Bus.
    /// </summary>
    private async Task QueueIndexingJobAsync(JobContract parentJob, ProfileSummaryPayload payload, CancellationToken ct)
    {
        try
        {
            var indexingJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = IndexingJobType,
                SubjectId = parentJob.SubjectId,
                CorrelationId = parentJob.CorrelationId,
                IdempotencyKey = $"index-{payload.DocumentId}",
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    DocumentId = payload.DocumentId,
                    ContentType = payload.ContentType,
                    Source = "ProfileSummary",
                    ParentJobId = parentJob.JobId
                }))
            };

            var sender = _serviceBusClient.CreateSender(_indexingQueueName);
            var message = new ServiceBusMessage(JsonSerializer.Serialize(indexingJob, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }))
            {
                MessageId = indexingJob.JobId.ToString(),
                CorrelationId = parentJob.CorrelationId,
                ContentType = "application/json"
            };

            await sender.SendMessageAsync(message, ct);
            await sender.DisposeAsync();

            _logger.LogInformation(
                "Queued indexing job {IndexingJobId} for document {DocumentId}",
                indexingJob.JobId, payload.DocumentId);
        }
        catch (Exception ex)
        {
            // Log but don't fail the profile job - indexing can be retried separately
            _logger.LogWarning(ex,
                "Failed to queue indexing job for document {DocumentId}: {Error}",
                payload.DocumentId, ex.Message);
        }
    }

    /// <summary>
    /// Gets the appropriate playbook name based on content type.
    /// </summary>
    private static string GetPlaybookName(string? contentType)
    {
        // Default to Document Profile playbook
        if (string.IsNullOrEmpty(contentType))
        {
            return IAppOnlyAnalysisService.DefaultPlaybookName;
        }

        // Email content uses different playbook
        return contentType switch
        {
            "Email" => "Email Analysis",
            "Attachment" => IAppOnlyAnalysisService.DefaultPlaybookName,
            "Document" => IAppOnlyAnalysisService.DefaultPlaybookName,
            _ => IAppOnlyAnalysisService.DefaultPlaybookName
        };
    }

    private ProfileSummaryPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ProfileSummaryPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse profile summary job payload");
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
               errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("invalid document", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("access denied", StringComparison.OrdinalIgnoreCase);
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
               exceptionName.Contains("Transient", StringComparison.OrdinalIgnoreCase) ||
               ex is TaskCanceledException ||
               ex is OperationCanceledException;
    }
}

/// <summary>
/// Payload structure for profile summary jobs.
/// </summary>
public class ProfileSummaryPayload
{
    /// <summary>
    /// The Dataverse document ID to analyze.
    /// </summary>
    public Guid DocumentId { get; set; }

    /// <summary>
    /// Content type from the Office add-in save request: Email, Attachment, or Document.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Whether AI processing was enabled in the save request.
    /// If false, this job will complete immediately and queue indexing.
    /// </summary>
    public bool TriggerAiProcessing { get; set; } = true;

    /// <summary>
    /// Whether to queue the indexing job after profile extraction.
    /// </summary>
    public bool QueueIndexing { get; set; } = true;

    /// <summary>
    /// Source of the profile request: "OutlookSave", "WordSave", "Manual", etc.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// The ProcessingJob ID for status updates.
    /// </summary>
    public Guid? ProcessingJobId { get; set; }

    /// <summary>
    /// When the job was enqueued.
    /// </summary>
    public DateTimeOffset? EnqueuedAt { get; set; }

    /// <summary>
    /// Additional metadata from the save request.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
