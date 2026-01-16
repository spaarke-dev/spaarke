using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for document download operations (OpenTelemetry-compatible).
/// Tracks: download requests, successes, failures, authorization denials.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Document" for OpenTelemetry configuration
/// - Metrics: document.download.*
/// - Dimensions: document.status (success/failed/not_found/denied), document.mime_type
///
/// Application Insights custom queries:
/// - Download success rate: customMetrics | where name startswith "document.download" | summarize count() by customDimensions["document.status"]
/// - Download latency: customMetrics | where name == "document.download.duration" | summarize avg(value), percentile(value, 95)
/// - Authorization denials: customMetrics | where name == "document.download.denied" | summarize count() by customDimensions["document.denial_reason"]
/// </summary>
public class DocumentTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Download metrics
    private readonly Counter<long> _downloadRequests;
    private readonly Counter<long> _downloadSuccesses;
    private readonly Counter<long> _downloadFailures;
    private readonly Counter<long> _downloadNotFound;
    private readonly Counter<long> _downloadDenied;
    private readonly Histogram<double> _downloadDuration;
    private readonly Histogram<long> _downloadFileSize;

    // Analysis job metrics
    private readonly Counter<long> _analysisJobSuccesses;
    private readonly Counter<long> _analysisJobFailures;
    private readonly Counter<long> _analysisJobDuplicates;
    private readonly Histogram<double> _analysisJobDuration;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Document";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public DocumentTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // ═══════════════════════════════════════════════════════════════════════════
        // Download Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _downloadRequests = _meter.CreateCounter<long>(
            name: "document.download.requests",
            unit: "{request}",
            description: "Total number of document download requests");

        _downloadSuccesses = _meter.CreateCounter<long>(
            name: "document.download.successes",
            unit: "{request}",
            description: "Number of successful document downloads");

        _downloadFailures = _meter.CreateCounter<long>(
            name: "document.download.failures",
            unit: "{request}",
            description: "Number of failed document downloads (errors)");

        _downloadNotFound = _meter.CreateCounter<long>(
            name: "document.download.not_found",
            unit: "{request}",
            description: "Number of download requests for non-existent documents");

        _downloadDenied = _meter.CreateCounter<long>(
            name: "document.download.denied",
            unit: "{request}",
            description: "Number of download requests denied by authorization");

        _downloadDuration = _meter.CreateHistogram<double>(
            name: "document.download.duration",
            unit: "ms",
            description: "Document download duration in milliseconds");

        _downloadFileSize = _meter.CreateHistogram<long>(
            name: "document.download.file_size",
            unit: "By",
            description: "Size of downloaded files in bytes");

        // ═══════════════════════════════════════════════════════════════════════════
        // Analysis Job Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _analysisJobSuccesses = _meter.CreateCounter<long>(
            name: "document.analysis_job.successes",
            unit: "{job}",
            description: "Number of successful app-only document analysis jobs");

        _analysisJobFailures = _meter.CreateCounter<long>(
            name: "document.analysis_job.failures",
            unit: "{job}",
            description: "Number of failed app-only document analysis jobs");

        _analysisJobDuplicates = _meter.CreateCounter<long>(
            name: "document.analysis_job.duplicates",
            unit: "{job}",
            description: "Number of analysis jobs skipped due to idempotency (already processed)");

        _analysisJobDuration = _meter.CreateHistogram<double>(
            name: "document.analysis_job.duration",
            unit: "ms",
            description: "App-only document analysis job duration in milliseconds");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Download Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record the start of a download request.
    /// </summary>
    public Stopwatch RecordDownloadStart(string documentId, string? userId = null)
    {
        var tags = new TagList
        {
            { "document.id", documentId }
        };

        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }

        _downloadRequests.Add(1, tags);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful document download.
    /// </summary>
    public void RecordDownloadSuccess(
        Stopwatch stopwatch,
        string documentId,
        string? userId = null,
        string? fileName = null,
        string? mimeType = null,
        long? fileSizeBytes = null)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "document.id", documentId },
            { "document.status", "success" }
        };

        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }

        if (!string.IsNullOrEmpty(mimeType))
        {
            tags.Add("document.mime_type", mimeType);
        }

        _downloadSuccesses.Add(1, tags);
        _downloadDuration.Record(durationMs, tags);

        if (fileSizeBytes.HasValue)
        {
            _downloadFileSize.Record(fileSizeBytes.Value, tags);
        }
    }

    /// <summary>
    /// Record document not found.
    /// </summary>
    public void RecordDownloadNotFound(
        Stopwatch stopwatch,
        string documentId,
        string? userId = null,
        string reason = "document_not_found")
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "document.id", documentId },
            { "document.status", "not_found" },
            { "document.not_found_reason", reason }
        };

        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }

        _downloadNotFound.Add(1, tags);
        _downloadDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record download authorization denied.
    /// </summary>
    public void RecordDownloadDenied(
        string documentId,
        string? userId = null,
        string reason = "access_denied")
    {
        var tags = new TagList
        {
            { "document.id", documentId },
            { "document.status", "denied" },
            { "document.denial_reason", reason }
        };

        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }

        _downloadDenied.Add(1, tags);
    }

    /// <summary>
    /// Record download failure (error).
    /// </summary>
    public void RecordDownloadFailure(
        Stopwatch stopwatch,
        string documentId,
        string? userId = null,
        string errorCode = "unknown_error")
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "document.id", documentId },
            { "document.status", "failed" },
            { "document.error_code", errorCode }
        };

        if (!string.IsNullOrEmpty(userId))
        {
            tags.Add("user.id", userId);
        }

        _downloadFailures.Add(1, tags);
        _downloadDuration.Record(durationMs, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Analysis Job Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record successful app-only analysis job completion.
    /// </summary>
    /// <param name="duration">Job execution duration.</param>
    /// <param name="analysisId">Optional Dataverse Analysis record ID for correlation.</param>
    public void RecordAnalysisJobSuccess(TimeSpan duration, Guid? analysisId = null)
    {
        var tags = new TagList
        {
            { "job.status", "success" }
        };

        if (analysisId.HasValue)
        {
            tags.Add("analysis.id", analysisId.Value.ToString());
        }

        _analysisJobSuccesses.Add(1, tags);
        _analysisJobDuration.Record(duration.TotalMilliseconds, tags);
    }

    /// <summary>
    /// Record app-only analysis job failure.
    /// </summary>
    /// <param name="errorCode">Error code describing the failure type.</param>
    /// <param name="analysisId">Optional Dataverse Analysis record ID for correlation (if created before failure).</param>
    public void RecordAnalysisJobFailure(string errorCode, Guid? analysisId = null)
    {
        var tags = new TagList
        {
            { "job.status", "failed" },
            { "job.error_code", errorCode }
        };

        if (analysisId.HasValue)
        {
            tags.Add("analysis.id", analysisId.Value.ToString());
        }

        _analysisJobFailures.Add(1, tags);
    }

    /// <summary>
    /// Record skipped analysis job (duplicate/already processed).
    /// </summary>
    public void RecordAnalysisJobSkippedDuplicate()
    {
        var tags = new TagList
        {
            { "job.status", "skipped" },
            { "job.skip_reason", "duplicate" }
        };

        _analysisJobDuplicates.Add(1, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Distributed Tracing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start a new Activity for distributed tracing.
    /// </summary>
    public Activity? StartActivity(string operationName, string? documentId = null, string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null)
        {
            if (!string.IsNullOrEmpty(documentId))
            {
                activity.SetTag("document.id", documentId);
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
