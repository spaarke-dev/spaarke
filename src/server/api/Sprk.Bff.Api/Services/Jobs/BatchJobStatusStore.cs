using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Email;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Service for storing and retrieving batch job status in distributed cache.
/// Supports Task 041 status endpoint and Task 042 batch job handler.
/// </summary>
public class BatchJobStatusStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<BatchJobStatusStore> _logger;
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromDays(7);
    private const string KeyPrefix = "batch:job:";

    public BatchJobStatusStore(IDistributedCache cache, ILogger<BatchJobStatusStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Creates initial status record when a batch job is submitted.
    /// </summary>
    public async Task CreateJobStatusAsync(
        string jobId,
        BatchFiltersApplied filters,
        int estimatedTotalEmails = 0,
        CancellationToken cancellationToken = default)
    {
        var record = new BatchJobStatusRecord
        {
            JobId = jobId,
            Status = BatchJobState.Pending,
            TotalEmails = estimatedTotalEmails,
            ProcessedCount = 0,
            ErrorCount = 0,
            SkippedCount = 0,
            SubmittedAt = DateTime.UtcNow,
            Filters = filters
        };

        await SaveRecordAsync(record, cancellationToken);
        _logger.LogInformation("Created batch job status record for {JobId}", jobId);
    }

    /// <summary>
    /// Gets current status of a batch job.
    /// </summary>
    public async Task<BatchJobStatusResponse?> GetJobStatusAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetKey(jobId);
            var data = await _cache.GetStringAsync(key, cancellationToken);

            if (string.IsNullOrEmpty(data))
            {
                _logger.LogDebug("Batch job status not found for {JobId}", jobId);
                return null;
            }

            var record = JsonSerializer.Deserialize<BatchJobStatusRecord>(data);
            if (record == null)
            {
                _logger.LogWarning("Failed to deserialize batch job status for {JobId}", jobId);
                return null;
            }

            return MapToResponse(record);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch job status for {JobId}", jobId);
            return null;
        }
    }

    /// <summary>
    /// Marks a job as started (InProgress).
    /// </summary>
    public async Task MarkJobStartedAsync(
        string jobId,
        int totalEmails,
        CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(jobId, cancellationToken);
        if (record == null)
        {
            _logger.LogWarning("Cannot mark job as started - record not found for {JobId}", jobId);
            return;
        }

        record.Status = BatchJobState.InProgress;
        record.StartedAt = DateTime.UtcNow;
        record.TotalEmails = totalEmails;

        await SaveRecordAsync(record, cancellationToken);
        _logger.LogInformation("Marked batch job {JobId} as started with {TotalEmails} emails", jobId, totalEmails);
    }

    /// <summary>
    /// Updates progress during processing.
    /// </summary>
    public async Task UpdateProgressAsync(
        string jobId,
        int processedCount,
        int errorCount,
        int skippedCount,
        CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(jobId, cancellationToken);
        if (record == null)
        {
            _logger.LogWarning("Cannot update progress - record not found for {JobId}", jobId);
            return;
        }

        record.ProcessedCount = processedCount;
        record.ErrorCount = errorCount;
        record.SkippedCount = skippedCount;

        await SaveRecordAsync(record, cancellationToken);
        _logger.LogDebug("Updated batch job {JobId} progress: {Processed}/{Total}",
            jobId, processedCount, record.TotalEmails);
    }

    /// <summary>
    /// Records an error during processing.
    /// </summary>
    public async Task RecordErrorAsync(
        string jobId,
        string? emailId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(jobId, cancellationToken);
        if (record == null)
        {
            _logger.LogWarning("Cannot record error - record not found for {JobId}", jobId);
            return;
        }

        record.Errors.Add(new BatchJobError
        {
            EmailId = emailId,
            Message = errorMessage,
            OccurredAt = DateTime.UtcNow
        });

        // Keep only the last 10 errors
        if (record.Errors.Count > 10)
        {
            record.Errors = record.Errors.TakeLast(10).ToList();
        }

        record.ErrorCount++;
        await SaveRecordAsync(record, cancellationToken);
    }

    /// <summary>
    /// Marks job as completed (success, partial, or failed).
    /// </summary>
    public async Task MarkJobCompletedAsync(
        string jobId,
        BatchJobState finalState,
        CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(jobId, cancellationToken);
        if (record == null)
        {
            _logger.LogWarning("Cannot mark job as completed - record not found for {JobId}", jobId);
            return;
        }

        record.Status = finalState;
        record.CompletedAt = DateTime.UtcNow;

        await SaveRecordAsync(record, cancellationToken);
        _logger.LogInformation("Marked batch job {JobId} as {Status}", jobId, finalState);
    }

    private async Task<BatchJobStatusRecord?> GetRecordAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = GetKey(jobId);
            var data = await _cache.GetStringAsync(key, cancellationToken);

            if (string.IsNullOrEmpty(data))
            {
                return null;
            }

            return JsonSerializer.Deserialize<BatchJobStatusRecord>(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch job record for {JobId}", jobId);
            return null;
        }
    }

    private async Task SaveRecordAsync(
        BatchJobStatusRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            var key = GetKey(record.JobId);
            var data = JsonSerializer.Serialize(record);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = DefaultExpiration
            };

            await _cache.SetStringAsync(key, data, options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving batch job record for {JobId}", record.JobId);
            throw;
        }
    }

    private static BatchJobStatusResponse MapToResponse(BatchJobStatusRecord record)
    {
        var totalHandled = record.ProcessedCount + record.ErrorCount + record.SkippedCount;
        var progressPercent = record.TotalEmails > 0
            ? (int)Math.Round((double)totalHandled / record.TotalEmails * 100)
            : 0;

        TimeSpan? estimatedRemaining = null;
        TimeSpan? avgProcessingTime = null;

        if (record.Status == BatchJobState.InProgress &&
            record.StartedAt.HasValue &&
            totalHandled > 0)
        {
            var elapsed = DateTime.UtcNow - record.StartedAt.Value;
            avgProcessingTime = TimeSpan.FromTicks(elapsed.Ticks / totalHandled);

            var remaining = record.TotalEmails - totalHandled;
            if (remaining > 0)
            {
                estimatedRemaining = TimeSpan.FromTicks(avgProcessingTime.Value.Ticks * remaining);
            }
        }

        return new BatchJobStatusResponse
        {
            JobId = record.JobId,
            Status = record.Status,
            ProgressPercent = progressPercent,
            TotalEmails = record.TotalEmails,
            ProcessedCount = record.ProcessedCount,
            ErrorCount = record.ErrorCount,
            SkippedCount = record.SkippedCount,
            SubmittedAt = record.SubmittedAt,
            StartedAt = record.StartedAt,
            CompletedAt = record.CompletedAt,
            EstimatedTimeRemaining = estimatedRemaining,
            AverageProcessingTime = avgProcessingTime,
            RecentErrors = record.Errors.TakeLast(5).ToList(),
            Message = GetStatusMessage(record)
        };
    }

    private static string GetStatusMessage(BatchJobStatusRecord record)
    {
        return record.Status switch
        {
            BatchJobState.Pending => "Job is queued and waiting to be processed.",
            BatchJobState.InProgress => $"Processing emails: {record.ProcessedCount + record.SkippedCount} of {record.TotalEmails} handled.",
            BatchJobState.Completed => $"Job completed successfully. {record.ProcessedCount} emails converted.",
            BatchJobState.PartiallyCompleted => $"Job completed with errors. {record.ProcessedCount} converted, {record.ErrorCount} failed.",
            BatchJobState.Failed => $"Job failed. {record.ErrorCount} errors encountered.",
            BatchJobState.Cancelled => "Job was cancelled.",
            _ => "Unknown status."
        };
    }

    private static string GetKey(string jobId) => $"{KeyPrefix}{jobId}";
}
