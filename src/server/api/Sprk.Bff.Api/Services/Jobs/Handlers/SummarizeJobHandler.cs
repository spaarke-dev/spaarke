using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for ai-summarize background jobs.
/// Processes summarization requests that were enqueued when client disconnects or for batch processing.
/// Follows ADR-004 for job contract patterns.
/// </summary>
public class SummarizeJobHandler : IJobHandler
{
    private readonly ISummarizeService _summarizeService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<SummarizeJobHandler> _logger;

    public SummarizeJobHandler(
        ISummarizeService summarizeService,
        IIdempotencyService idempotencyService,
        IDataverseService dataverseService,
        ILogger<SummarizeJobHandler> logger)
    {
        _summarizeService = summarizeService;
        _idempotencyService = idempotencyService;
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public string JobType => "ai-summarize";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing ai-summarize job {JobId} for document {SubjectId}, Attempt {Attempt}",
                job.JobId, job.SubjectId, job.Attempt);

            // Check idempotency - prevent duplicate processing
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"summarize:{job.SubjectId}:{job.JobId}";
            }

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Job {JobId} already processed (idempotency key: {IdempotencyKey})",
                    job.JobId, idempotencyKey);

                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(5), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for job {JobId} (idempotency key: {IdempotencyKey})",
                    job.JobId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Parse job payload
                var payload = ParsePayload(job.Payload);
                if (payload == null)
                {
                    throw new ArgumentException("Invalid payload for ai-summarize job");
                }

                // Call SummarizeService (non-streaming for background processing)
                var request = payload.ToRequest();
                var result = await _summarizeService.SummarizeAsync(request, ct);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "Summarization completed for document {DocumentId}, SummaryLength={Length}",
                        payload.DocumentId, result.Summary?.Length ?? 0);

                    // Update Dataverse with the summary
                    await UpdateDocumentSummaryAsync(payload.DocumentId, result, ct);
                }
                else
                {
                    _logger.LogWarning(
                        "Summarization failed for document {DocumentId}: {Error}",
                        payload.DocumentId, result.ErrorMessage);

                    // Update Dataverse with failure status
                    await UpdateDocumentSummaryStatusAsync(payload.DocumentId, SummaryStatus.Failed, ct);
                }

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                stopwatch.Stop();

                _logger.LogInformation(
                    "Job {JobId} completed in {Duration}ms",
                    job.JobId, stopwatch.ElapsedMilliseconds);

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
            stopwatch.Stop();
            _logger.LogError(ex, "ai-summarize job {JobId} failed", job.JobId);
            throw; // Let the job processor handle retry logic
        }
    }

    private SummarizeJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<SummarizeJobPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse ai-summarize job payload");
            return null;
        }
    }

    private async Task UpdateDocumentSummaryAsync(
        Guid documentId,
        SummarizeResult result,
        CancellationToken ct)
    {
        // Suppress IDE0060 - result will be used when UpdateDocumentRequest is extended for summary fields
        _ = result;

        try
        {
            // Update Dataverse with summary fields
            await _dataverseService.UpdateDocumentAsync(
                documentId.ToString(),
                new UpdateDocumentRequest
                {
                    // Note: UpdateDocumentRequest may need to be extended to include summary fields
                    // For now, we'll handle this via the Web API directly if needed
                },
                ct);

            _logger.LogDebug(
                "Updated Dataverse summary for document {DocumentId}",
                documentId);
        }
        catch (Exception ex)
        {
            // Log but don't fail the job - the summary was generated successfully
            _logger.LogWarning(ex,
                "Failed to update Dataverse summary for document {DocumentId}",
                documentId);
        }
    }

    private async Task UpdateDocumentSummaryStatusAsync(
        Guid documentId,
        SummaryStatus status,
        CancellationToken ct)
    {
        // Suppress IDE0060 - ct will be used when UpdateDocumentRequest is extended
        _ = ct;

        try
        {
            // Update status only
            _logger.LogDebug(
                "Updating summary status for document {DocumentId} to {Status}",
                documentId, status);

            // TODO: Implement when UpdateDocumentRequest is extended
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update summary status for document {DocumentId}",
                documentId);
        }
    }
}

/// <summary>
/// Summary status values matching Dataverse sprk_filesummarystatus choice.
/// </summary>
public enum SummaryStatus
{
    None = 0,
    Pending = 1,
    Completed = 2,
    OptedOut = 3,
    Failed = 4
}
