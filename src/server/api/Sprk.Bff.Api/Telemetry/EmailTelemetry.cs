using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sprk.Bff.Api.Services.Email;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for email-to-document processing (OpenTelemetry-compatible).
/// Tracks: conversion requests, webhook triggers, filter evaluations, job processing.
///
/// Also delegates to EmailProcessingStatsService for in-memory stats readable via API.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Email" for OpenTelemetry configuration
/// - Metrics: email.conversion.*, email.webhook.*, email.filter.*, email.job.*
/// - Dimensions: email.trigger (manual/webhook/polling), email.status (success/failed/filtered)
///
/// Application Insights custom queries:
/// - Conversion success rate: customMetrics | where name startswith "email.conversion" | summarize count() by customDimensions["email.status"]
/// - Webhook latency: customMetrics | where name == "email.webhook.duration" | summarize avg(value), percentile(value, 95)
/// - Filter rule hits: customMetrics | where name == "email.filter.matched" | summarize count() by customDimensions["email.filter.action"]
/// </summary>
public class EmailTelemetry : IDisposable
{
    private readonly Meter _meter;
    private readonly EmailProcessingStatsService? _statsService;

    // Conversion metrics
    private readonly Counter<long> _conversionRequests;
    private readonly Counter<long> _conversionSuccesses;
    private readonly Counter<long> _conversionFailures;
    private readonly Histogram<double> _conversionDuration;

    // Webhook metrics
    private readonly Counter<long> _webhookReceived;
    private readonly Counter<long> _webhookEnqueued;
    private readonly Counter<long> _webhookRejected;
    private readonly Histogram<double> _webhookDuration;

    // Polling metrics
    private readonly Counter<long> _pollingRuns;
    private readonly Counter<long> _pollingEmailsFound;
    private readonly Counter<long> _pollingEmailsEnqueued;

    // Filter metrics
    private readonly Counter<long> _filterEvaluations;
    private readonly Counter<long> _filterMatched;
    private readonly Counter<long> _filterDefaultAction;

    // Job processing metrics
    private readonly Counter<long> _jobsProcessed;
    private readonly Counter<long> _jobsSucceeded;
    private readonly Counter<long> _jobsFailed;
    private readonly Counter<long> _jobsSkippedDuplicate;
    private readonly Histogram<double> _jobDuration;

    // File metrics
    private readonly Histogram<long> _emlFileSize;
    private readonly Counter<long> _attachmentsProcessed;

    // AI job enqueueing metrics
    private readonly Counter<long> _aiJobsEnqueued;
    private readonly Counter<long> _aiJobEnqueueFailures;

    // DLQ metrics
    private readonly Counter<long> _dlqListOperations;
    private readonly Counter<long> _dlqRedriveAttempts;
    private readonly Counter<long> _dlqRedriveSuccesses;
    private readonly Counter<long> _dlqRedriveFailures;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Email";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public EmailTelemetry(EmailProcessingStatsService? statsService = null)
    {
        _statsService = statsService;
        _meter = new Meter(MeterName, "1.0.0");

        // ═══════════════════════════════════════════════════════════════════════════
        // Conversion Metrics (Manual save endpoint)
        // ═══════════════════════════════════════════════════════════════════════════
        _conversionRequests = _meter.CreateCounter<long>(
            name: "email.conversion.requests",
            unit: "{request}",
            description: "Total number of email-to-document conversion requests");

        _conversionSuccesses = _meter.CreateCounter<long>(
            name: "email.conversion.successes",
            unit: "{request}",
            description: "Number of successful email conversions");

        _conversionFailures = _meter.CreateCounter<long>(
            name: "email.conversion.failures",
            unit: "{request}",
            description: "Number of failed email conversions");

        _conversionDuration = _meter.CreateHistogram<double>(
            name: "email.conversion.duration",
            unit: "ms",
            description: "Email conversion duration in milliseconds");

        // ═══════════════════════════════════════════════════════════════════════════
        // Webhook Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _webhookReceived = _meter.CreateCounter<long>(
            name: "email.webhook.received",
            unit: "{request}",
            description: "Total webhook requests received");

        _webhookEnqueued = _meter.CreateCounter<long>(
            name: "email.webhook.enqueued",
            unit: "{job}",
            description: "Jobs successfully enqueued from webhooks");

        _webhookRejected = _meter.CreateCounter<long>(
            name: "email.webhook.rejected",
            unit: "{request}",
            description: "Webhook requests rejected (invalid signature, disabled, etc.)");

        _webhookDuration = _meter.CreateHistogram<double>(
            name: "email.webhook.duration",
            unit: "ms",
            description: "Webhook processing duration in milliseconds");

        // ═══════════════════════════════════════════════════════════════════════════
        // Polling Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _pollingRuns = _meter.CreateCounter<long>(
            name: "email.polling.runs",
            unit: "{run}",
            description: "Total polling service runs");

        _pollingEmailsFound = _meter.CreateCounter<long>(
            name: "email.polling.emails_found",
            unit: "{email}",
            description: "Emails found during polling");

        _pollingEmailsEnqueued = _meter.CreateCounter<long>(
            name: "email.polling.emails_enqueued",
            unit: "{email}",
            description: "Emails enqueued during polling");

        // ═══════════════════════════════════════════════════════════════════════════
        // Filter Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _filterEvaluations = _meter.CreateCounter<long>(
            name: "email.filter.evaluations",
            unit: "{evaluation}",
            description: "Total filter rule evaluations");

        _filterMatched = _meter.CreateCounter<long>(
            name: "email.filter.matched",
            unit: "{match}",
            description: "Emails that matched a filter rule");

        _filterDefaultAction = _meter.CreateCounter<long>(
            name: "email.filter.default_action",
            unit: "{evaluation}",
            description: "Emails that used default action (no rule matched)");

        // ═══════════════════════════════════════════════════════════════════════════
        // Job Processing Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _jobsProcessed = _meter.CreateCounter<long>(
            name: "email.job.processed",
            unit: "{job}",
            description: "Total email processing jobs handled");

        _jobsSucceeded = _meter.CreateCounter<long>(
            name: "email.job.succeeded",
            unit: "{job}",
            description: "Successfully completed email processing jobs");

        _jobsFailed = _meter.CreateCounter<long>(
            name: "email.job.failed",
            unit: "{job}",
            description: "Failed email processing jobs");

        _jobsSkippedDuplicate = _meter.CreateCounter<long>(
            name: "email.job.skipped_duplicate",
            unit: "{job}",
            description: "Jobs skipped due to idempotency (already processed)");

        _jobDuration = _meter.CreateHistogram<double>(
            name: "email.job.duration",
            unit: "ms",
            description: "Job processing duration in milliseconds");

        // ═══════════════════════════════════════════════════════════════════════════
        // File Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _emlFileSize = _meter.CreateHistogram<long>(
            name: "email.eml.file_size",
            unit: "By",
            description: "Size of generated .eml files");

        _attachmentsProcessed = _meter.CreateCounter<long>(
            name: "email.attachments.processed",
            unit: "{attachment}",
            description: "Total attachments processed");

        // ═══════════════════════════════════════════════════════════════════════════
        // AI Job Enqueueing Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _aiJobsEnqueued = _meter.CreateCounter<long>(
            name: "email.ai_job.enqueued",
            unit: "{job}",
            description: "AI analysis jobs enqueued for email documents");

        _aiJobEnqueueFailures = _meter.CreateCounter<long>(
            name: "email.ai_job.enqueue_failures",
            unit: "{failure}",
            description: "Failed attempts to enqueue AI analysis jobs");

        // ═══════════════════════════════════════════════════════════════════════════
        // DLQ Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _dlqListOperations = _meter.CreateCounter<long>(
            name: "email.dlq.list_operations",
            unit: "{operation}",
            description: "Total DLQ list/peek operations");

        _dlqRedriveAttempts = _meter.CreateCounter<long>(
            name: "email.dlq.redrive_attempts",
            unit: "{attempt}",
            description: "Total DLQ re-drive attempts");

        _dlqRedriveSuccesses = _meter.CreateCounter<long>(
            name: "email.dlq.redrive_successes",
            unit: "{message}",
            description: "Messages successfully re-driven from DLQ");

        _dlqRedriveFailures = _meter.CreateCounter<long>(
            name: "email.dlq.redrive_failures",
            unit: "{message}",
            description: "Failed DLQ re-drive operations");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Conversion Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record the start of a conversion request.
    /// </summary>
    public Stopwatch RecordConversionStart(string trigger = "manual")
    {
        _conversionRequests.Add(1, new KeyValuePair<string, object?>("email.trigger", trigger));
        _statsService?.RecordConversionRequest();
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful email conversion.
    /// </summary>
    public void RecordConversionSuccess(
        Stopwatch stopwatch,
        string trigger = "manual",
        long? emlSizeBytes = null,
        int attachmentCount = 0)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "email.trigger", trigger },
            { "email.status", "success" }
        };

        _conversionSuccesses.Add(1, tags);
        _conversionDuration.Record(durationMs, tags);
        _statsService?.RecordConversionSuccess(durationMs);

        if (emlSizeBytes.HasValue)
        {
            _emlFileSize.Record(emlSizeBytes.Value, tags);
            _statsService?.RecordEmlFileSize(emlSizeBytes.Value);
        }

        if (attachmentCount > 0)
        {
            _attachmentsProcessed.Add(attachmentCount, tags);
            _statsService?.RecordAttachmentsProcessed(attachmentCount);
        }
    }

    /// <summary>
    /// Record failed email conversion.
    /// </summary>
    public void RecordConversionFailure(Stopwatch stopwatch, string errorCode, string trigger = "manual")
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "email.trigger", trigger },
            { "email.status", "failed" },
            { "email.error_code", errorCode }
        };

        _conversionFailures.Add(1, tags);
        _conversionDuration.Record(durationMs, tags);
        _statsService?.RecordConversionFailure(durationMs);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Webhook Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record webhook request received.
    /// </summary>
    public Stopwatch RecordWebhookReceived()
    {
        _webhookReceived.Add(1);
        _statsService?.RecordWebhookReceived();
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record webhook successfully enqueued a job.
    /// </summary>
    public void RecordWebhookEnqueued(Stopwatch stopwatch, Guid emailId)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _webhookEnqueued.Add(1);
        _webhookDuration.Record(durationMs,
            new KeyValuePair<string, object?>("email.status", "enqueued"));
        _statsService?.RecordWebhookEnqueued(durationMs);
    }

    /// <summary>
    /// Record webhook request rejected.
    /// </summary>
    public void RecordWebhookRejected(Stopwatch stopwatch, string reason)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _webhookRejected.Add(1, new KeyValuePair<string, object?>("email.rejection_reason", reason));
        _webhookDuration.Record(durationMs,
            new KeyValuePair<string, object?>("email.status", "rejected"),
            new KeyValuePair<string, object?>("email.rejection_reason", reason));
        _statsService?.RecordWebhookRejected(durationMs);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Polling Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record polling service run.
    /// </summary>
    public void RecordPollingRun(int emailsFound, int emailsEnqueued)
    {
        _pollingRuns.Add(1);
        _pollingEmailsFound.Add(emailsFound);
        _pollingEmailsEnqueued.Add(emailsEnqueued);
        _statsService?.RecordPollingRun(emailsFound, emailsEnqueued);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Filter Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record filter evaluation result.
    /// </summary>
    public void RecordFilterEvaluation(Services.Email.EmailFilterAction action, bool ruleMatched, string? ruleName = null)
    {
        _filterEvaluations.Add(1);

        var actionStr = action switch
        {
            Services.Email.EmailFilterAction.AutoSave => "process",
            Services.Email.EmailFilterAction.Ignore => "ignore",
            Services.Email.EmailFilterAction.ReviewRequired => "review",
            _ => "unknown"
        };

        if (ruleMatched)
        {
            _filterMatched.Add(1,
                new KeyValuePair<string, object?>("email.filter.action", actionStr),
                new KeyValuePair<string, object?>("email.filter.rule", ruleName ?? "unknown"));
        }
        else
        {
            _filterDefaultAction.Add(1,
                new KeyValuePair<string, object?>("email.filter.action", actionStr));
        }

        _statsService?.RecordFilterEvaluation(ruleMatched);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Job Processing Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record start of job processing.
    /// </summary>
    public Stopwatch RecordJobStart()
    {
        _jobsProcessed.Add(1);
        _statsService?.RecordJobProcessed();
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful job completion.
    /// </summary>
    public void RecordJobSuccess(Stopwatch stopwatch, long? emlSizeBytes = null, int attachmentCount = 0)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _jobsSucceeded.Add(1);
        _jobDuration.Record(durationMs,
            new KeyValuePair<string, object?>("email.status", "success"));
        _statsService?.RecordJobSuccess(durationMs);

        if (emlSizeBytes.HasValue)
        {
            _emlFileSize.Record(emlSizeBytes.Value);
            _statsService?.RecordEmlFileSize(emlSizeBytes.Value);
        }

        if (attachmentCount > 0)
        {
            _attachmentsProcessed.Add(attachmentCount);
            _statsService?.RecordAttachmentsProcessed(attachmentCount);
        }
    }

    /// <summary>
    /// Record failed job.
    /// </summary>
    public void RecordJobFailure(Stopwatch stopwatch, string errorCode)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;
        _jobsFailed.Add(1, new KeyValuePair<string, object?>("email.error_code", errorCode));
        _jobDuration.Record(durationMs,
            new KeyValuePair<string, object?>("email.status", "failed"),
            new KeyValuePair<string, object?>("email.error_code", errorCode));
        _statsService?.RecordJobFailure(durationMs);
    }

    /// <summary>
    /// Record job skipped due to idempotency.
    /// </summary>
    public void RecordJobSkippedDuplicate()
    {
        _jobsSkippedDuplicate.Add(1);
        _statsService?.RecordJobSkippedDuplicate();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Batch Processing Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record batch job submission.
    /// </summary>
    public void RecordBatchJobSubmitted(DateTime startDate, DateTime endDate, int maxEmails)
    {
        // Using job processed counter with batch tag
        _jobsProcessed.Add(1,
            new KeyValuePair<string, object?>("email.job_type", "batch"),
            new KeyValuePair<string, object?>("email.batch.max_emails", maxEmails));
    }

    /// <summary>
    /// Record batch job completion with detailed metrics.
    /// </summary>
    public void RecordBatchJobCompleted(int processedCount, int errorCount, int skippedCount, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "email.job_type", "batch" },
            { "email.status", errorCount == 0 ? "success" : (processedCount == 0 ? "failed" : "partial") }
        };

        _jobDuration.Record(duration.TotalMilliseconds, tags);

        if (processedCount > 0)
        {
            _jobsSucceeded.Add(processedCount, new KeyValuePair<string, object?>("email.job_type", "batch"));
        }

        if (errorCount > 0)
        {
            _jobsFailed.Add(errorCount, new KeyValuePair<string, object?>("email.job_type", "batch"));
        }

        if (skippedCount > 0)
        {
            _jobsSkippedDuplicate.Add(skippedCount, new KeyValuePair<string, object?>("email.job_type", "batch"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Attachment Processing Methods (FR-04)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record detailed attachment processing metrics.
    /// Tracks extraction, filtering, upload success, and failures separately.
    /// </summary>
    public void RecordAttachmentProcessing(int extractedCount, int filteredCount, int uploadedCount, int failedCount)
    {
        if (extractedCount > 0)
        {
            _attachmentsProcessed.Add(extractedCount,
                new KeyValuePair<string, object?>("email.attachment.operation", "extracted"));
        }

        if (filteredCount > 0)
        {
            _attachmentsProcessed.Add(filteredCount,
                new KeyValuePair<string, object?>("email.attachment.operation", "filtered"));
        }

        if (uploadedCount > 0)
        {
            _attachmentsProcessed.Add(uploadedCount,
                new KeyValuePair<string, object?>("email.attachment.operation", "uploaded"));
            _statsService?.RecordAttachmentsProcessed(uploadedCount);
        }

        if (failedCount > 0)
        {
            _attachmentsProcessed.Add(failedCount,
                new KeyValuePair<string, object?>("email.attachment.operation", "failed"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DLQ Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record a DLQ list/peek operation.
    /// </summary>
    public void RecordDlqListOperation(int messagesReturned)
    {
        _dlqListOperations.Add(1,
            new KeyValuePair<string, object?>("email.dlq.messages_returned", messagesReturned));
    }

    /// <summary>
    /// Record a DLQ re-drive operation.
    /// </summary>
    public void RecordDlqRedriveOperation(int successCount, int failureCount)
    {
        _dlqRedriveAttempts.Add(1);

        if (successCount > 0)
        {
            _dlqRedriveSuccesses.Add(successCount);
        }

        if (failureCount > 0)
        {
            _dlqRedriveFailures.Add(failureCount);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // AI Job Enqueueing Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record AI analysis job successfully enqueued for an email document.
    /// </summary>
    /// <param name="documentType">Type of document: "email" or "attachment"</param>
    public void RecordAiJobEnqueued(string documentType)
    {
        _aiJobsEnqueued.Add(1,
            new KeyValuePair<string, object?>("email.document_type", documentType));
    }

    /// <summary>
    /// Record failed attempt to enqueue AI analysis job.
    /// </summary>
    /// <param name="documentType">Type of document: "email" or "attachment"</param>
    /// <param name="errorCode">Error category</param>
    public void RecordAiJobEnqueueFailure(string documentType, string errorCode)
    {
        _aiJobEnqueueFailures.Add(1,
            new KeyValuePair<string, object?>("email.document_type", documentType),
            new KeyValuePair<string, object?>("email.error_code", errorCode));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Distributed Tracing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start a new Activity for distributed tracing.
    /// </summary>
    public Activity? StartActivity(string operationName, Guid? emailId = null, string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null)
        {
            if (emailId.HasValue)
            {
                activity.SetTag("email.id", emailId.Value.ToString());
            }
            if (!string.IsNullOrEmpty(correlationId))
            {
                activity.SetTag("correlation_id", correlationId);
            }
        }
        return activity;
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}
