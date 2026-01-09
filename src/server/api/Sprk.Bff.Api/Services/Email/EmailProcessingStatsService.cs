using System.Collections.Concurrent;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// In-memory service for tracking email processing statistics.
/// Provides readable counters for the admin monitoring PCF control.
///
/// This service complements EmailTelemetry (which writes to OpenTelemetry)
/// by keeping running totals that can be queried via API.
/// </summary>
public class EmailProcessingStatsService
{
    private readonly DateTime _serviceStartTime = DateTime.UtcNow;

    // Conversion stats
    private long _conversionRequests;
    private long _conversionSuccesses;
    private long _conversionFailures;
    private readonly ConcurrentBag<double> _conversionDurations = [];

    // Webhook stats
    private long _webhookReceived;
    private long _webhookEnqueued;
    private long _webhookRejected;
    private readonly ConcurrentBag<double> _webhookDurations = [];

    // Polling stats
    private long _pollingRuns;
    private long _pollingEmailsFound;
    private long _pollingEmailsEnqueued;

    // Filter stats
    private long _filterEvaluations;
    private long _filterMatched;
    private long _filterDefaultAction;

    // Job stats
    private long _jobsProcessed;
    private long _jobsSucceeded;
    private long _jobsFailed;
    private long _jobsSkippedDuplicate;
    private readonly ConcurrentBag<double> _jobDurations = [];

    // File stats
    private long _attachmentsProcessed;
    private readonly ConcurrentBag<long> _emlFileSizes = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // Recording Methods (called by EmailTelemetry)
    // ═══════════════════════════════════════════════════════════════════════════

    public void RecordConversionRequest() => Interlocked.Increment(ref _conversionRequests);

    public void RecordConversionSuccess(double durationMs)
    {
        Interlocked.Increment(ref _conversionSuccesses);
        _conversionDurations.Add(durationMs);
    }

    public void RecordConversionFailure(double durationMs)
    {
        Interlocked.Increment(ref _conversionFailures);
        _conversionDurations.Add(durationMs);
    }

    public void RecordWebhookReceived() => Interlocked.Increment(ref _webhookReceived);

    public void RecordWebhookEnqueued(double durationMs)
    {
        Interlocked.Increment(ref _webhookEnqueued);
        _webhookDurations.Add(durationMs);
    }

    public void RecordWebhookRejected(double durationMs)
    {
        Interlocked.Increment(ref _webhookRejected);
        _webhookDurations.Add(durationMs);
    }

    public void RecordPollingRun(int emailsFound, int emailsEnqueued)
    {
        Interlocked.Increment(ref _pollingRuns);
        Interlocked.Add(ref _pollingEmailsFound, emailsFound);
        Interlocked.Add(ref _pollingEmailsEnqueued, emailsEnqueued);
    }

    public void RecordFilterEvaluation(bool ruleMatched)
    {
        Interlocked.Increment(ref _filterEvaluations);
        if (ruleMatched)
        {
            Interlocked.Increment(ref _filterMatched);
        }
        else
        {
            Interlocked.Increment(ref _filterDefaultAction);
        }
    }

    public void RecordJobProcessed() => Interlocked.Increment(ref _jobsProcessed);

    public void RecordJobSuccess(double durationMs)
    {
        Interlocked.Increment(ref _jobsSucceeded);
        _jobDurations.Add(durationMs);
    }

    public void RecordJobFailure(double durationMs)
    {
        Interlocked.Increment(ref _jobsFailed);
        _jobDurations.Add(durationMs);
    }

    public void RecordJobSkippedDuplicate() => Interlocked.Increment(ref _jobsSkippedDuplicate);

    public void RecordAttachmentsProcessed(int count) => Interlocked.Add(ref _attachmentsProcessed, count);

    public void RecordEmlFileSize(long sizeBytes) => _emlFileSizes.Add(sizeBytes);

    // ═══════════════════════════════════════════════════════════════════════════
    // Query Methods (called by API endpoint)
    // ═══════════════════════════════════════════════════════════════════════════

    public EmailProcessingStatsResponse GetStats()
    {
        var conversionTotal = Interlocked.Read(ref _conversionRequests);
        var conversionSuccess = Interlocked.Read(ref _conversionSuccesses);
        var conversionFail = Interlocked.Read(ref _conversionFailures);

        var webhookTotal = Interlocked.Read(ref _webhookReceived);
        var webhookEnqueue = Interlocked.Read(ref _webhookEnqueued);
        var webhookReject = Interlocked.Read(ref _webhookRejected);

        var jobTotal = Interlocked.Read(ref _jobsProcessed);
        var jobSuccess = Interlocked.Read(ref _jobsSucceeded);
        var jobFail = Interlocked.Read(ref _jobsFailed);
        var jobSkip = Interlocked.Read(ref _jobsSkippedDuplicate);

        var filterTotal = Interlocked.Read(ref _filterEvaluations);
        var filterMatch = Interlocked.Read(ref _filterMatched);
        var filterDefault = Interlocked.Read(ref _filterDefaultAction);

        return new EmailProcessingStatsResponse
        {
            LastUpdated = DateTime.UtcNow,
            ServiceStartTime = _serviceStartTime,
            Conversion = new ConversionStatsDto
            {
                TotalRequests = conversionTotal,
                Successes = conversionSuccess,
                Failures = conversionFail,
                SuccessRate = conversionTotal > 0 ? (double)conversionSuccess / conversionTotal : 0,
                AverageDurationMs = _conversionDurations.Count > 0
                    ? _conversionDurations.Average()
                    : 0
            },
            Webhook = new WebhookStatsDto
            {
                TotalReceived = webhookTotal,
                Enqueued = webhookEnqueue,
                Rejected = webhookReject,
                AcceptRate = webhookTotal > 0 ? (double)webhookEnqueue / webhookTotal : 0,
                AverageDurationMs = _webhookDurations.Count > 0
                    ? _webhookDurations.Average()
                    : 0
            },
            Polling = new PollingStatsDto
            {
                TotalRuns = Interlocked.Read(ref _pollingRuns),
                EmailsFound = Interlocked.Read(ref _pollingEmailsFound),
                EmailsEnqueued = Interlocked.Read(ref _pollingEmailsEnqueued)
            },
            Filter = new FilterStatsDto
            {
                TotalEvaluations = filterTotal,
                Matched = filterMatch,
                DefaultAction = filterDefault,
                MatchRate = filterTotal > 0 ? (double)filterMatch / filterTotal : 0
            },
            Job = new JobStatsDto
            {
                TotalProcessed = jobTotal,
                Succeeded = jobSuccess,
                Failed = jobFail,
                SkippedDuplicate = jobSkip,
                SuccessRate = jobTotal > 0 ? (double)jobSuccess / jobTotal : 0,
                AverageDurationMs = _jobDurations.Count > 0
                    ? _jobDurations.Average()
                    : 0
            },
            File = new FileStatsDto
            {
                TotalAttachmentsProcessed = Interlocked.Read(ref _attachmentsProcessed),
                AverageEmlSizeBytes = _emlFileSizes.Count > 0
                    ? (long)_emlFileSizes.Average()
                    : 0
            }
        };
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Response DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Response containing email processing statistics for admin monitoring.
/// </summary>
public record EmailProcessingStatsResponse
{
    /// <summary>Timestamp when this response was generated.</summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>When the API service started (stats reset on restart).</summary>
    public DateTime ServiceStartTime { get; init; }

    /// <summary>Email conversion statistics.</summary>
    public required ConversionStatsDto Conversion { get; init; }

    /// <summary>Webhook processing statistics.</summary>
    public required WebhookStatsDto Webhook { get; init; }

    /// <summary>Polling service statistics.</summary>
    public required PollingStatsDto Polling { get; init; }

    /// <summary>Filter rule evaluation statistics.</summary>
    public required FilterStatsDto Filter { get; init; }

    /// <summary>Background job processing statistics.</summary>
    public required JobStatsDto Job { get; init; }

    /// <summary>File processing statistics.</summary>
    public required FileStatsDto File { get; init; }
}

public record ConversionStatsDto
{
    public long TotalRequests { get; init; }
    public long Successes { get; init; }
    public long Failures { get; init; }
    public double SuccessRate { get; init; }
    public double AverageDurationMs { get; init; }
}

public record WebhookStatsDto
{
    public long TotalReceived { get; init; }
    public long Enqueued { get; init; }
    public long Rejected { get; init; }
    public double AcceptRate { get; init; }
    public double AverageDurationMs { get; init; }
}

public record PollingStatsDto
{
    public long TotalRuns { get; init; }
    public long EmailsFound { get; init; }
    public long EmailsEnqueued { get; init; }
}

public record FilterStatsDto
{
    public long TotalEvaluations { get; init; }
    public long Matched { get; init; }
    public long DefaultAction { get; init; }
    public double MatchRate { get; init; }
}

public record JobStatsDto
{
    public long TotalProcessed { get; init; }
    public long Succeeded { get; init; }
    public long Failed { get; init; }
    public long SkippedDuplicate { get; init; }
    public double SuccessRate { get; init; }
    public double AverageDurationMs { get; init; }
}

public record FileStatsDto
{
    public long TotalAttachmentsProcessed { get; init; }
    public long AverageEmlSizeBytes { get; init; }
}
