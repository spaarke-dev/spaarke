using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for Finance Intelligence Module operations (OpenTelemetry-compatible).
/// Tracks: classification, extraction, budget signals, cache operations.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Finance" for OpenTelemetry configuration
/// - Metrics: finance.classification.*, finance.extraction.*, finance.signal.*
/// - Dimensions: finance.status (success/failed), finance.classification (InvoiceCandidate/Unknown)
///
/// Application Insights custom queries:
/// - Classification rate: customMetrics | where name startswith "finance.classification" | summarize count() by customDimensions["finance.status"]
/// - Extraction latency: customMetrics | where name == "finance.extraction.duration" | summarize avg(value), percentile(value, 95)
/// - Budget signals: customMetrics | where name startswith "finance.signal" | summarize count() by customDimensions["finance.signal_type"]
/// </summary>
public class FinanceTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Classification metrics
    private readonly Counter<long> _classificationRequests;
    private readonly Counter<long> _classificationSuccesses;
    private readonly Counter<long> _classificationFailures;
    private readonly Histogram<double> _classificationDuration;

    // Extraction metrics
    private readonly Counter<long> _extractionRequests;
    private readonly Counter<long> _extractionSuccesses;
    private readonly Counter<long> _extractionFailures;
    private readonly Histogram<double> _extractionDuration;

    // Signal metrics
    private readonly Counter<long> _signalsEmitted;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Finance";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public FinanceTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // ═══════════════════════════════════════════════════════════════════════════
        // Classification Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _classificationRequests = _meter.CreateCounter<long>(
            name: "finance.classification.requests",
            unit: "{request}",
            description: "Total number of finance document classification requests");

        _classificationSuccesses = _meter.CreateCounter<long>(
            name: "finance.classification.successes",
            unit: "{request}",
            description: "Number of successful finance document classifications");

        _classificationFailures = _meter.CreateCounter<long>(
            name: "finance.classification.failures",
            unit: "{request}",
            description: "Number of failed finance document classifications");

        _classificationDuration = _meter.CreateHistogram<double>(
            name: "finance.classification.duration",
            unit: "ms",
            description: "Finance document classification duration in milliseconds");

        // ═══════════════════════════════════════════════════════════════════════════
        // Extraction Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _extractionRequests = _meter.CreateCounter<long>(
            name: "finance.extraction.requests",
            unit: "{request}",
            description: "Total number of finance data extraction requests");

        _extractionSuccesses = _meter.CreateCounter<long>(
            name: "finance.extraction.successes",
            unit: "{request}",
            description: "Number of successful finance data extractions");

        _extractionFailures = _meter.CreateCounter<long>(
            name: "finance.extraction.failures",
            unit: "{request}",
            description: "Number of failed finance data extractions");

        _extractionDuration = _meter.CreateHistogram<double>(
            name: "finance.extraction.duration",
            unit: "ms",
            description: "Finance data extraction duration in milliseconds");

        // ═══════════════════════════════════════════════════════════════════════════
        // Signal Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _signalsEmitted = _meter.CreateCounter<long>(
            name: "finance.signal.emitted",
            unit: "{signal}",
            description: "Number of finance signals emitted (BudgetWarning, VelocitySpike, etc.)");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Classification Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record the start of a classification request.
    /// </summary>
    public Stopwatch RecordClassificationStart(string documentId)
    {
        var tags = new TagList
        {
            { "document.id", documentId }
        };

        _classificationRequests.Add(1, tags);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful classification.
    /// </summary>
    public void RecordClassificationSuccess(Stopwatch stopwatch, string documentId, string classification)
    {
        stopwatch.Stop();
        var tags = new TagList
        {
            { "document.id", documentId },
            { "finance.status", "success" },
            { "finance.classification", classification }
        };

        _classificationSuccesses.Add(1, tags);
        _classificationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    /// <summary>
    /// Record classification failure.
    /// </summary>
    public void RecordClassificationFailure(Stopwatch stopwatch, string documentId, string errorCode)
    {
        stopwatch.Stop();
        var tags = new TagList
        {
            { "document.id", documentId },
            { "finance.status", "failed" },
            { "finance.error_code", errorCode }
        };

        _classificationFailures.Add(1, tags);
        _classificationDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Extraction Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record the start of an extraction request.
    /// </summary>
    public Stopwatch RecordExtractionStart(string documentId)
    {
        var tags = new TagList
        {
            { "document.id", documentId }
        };

        _extractionRequests.Add(1, tags);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful extraction.
    /// </summary>
    public void RecordExtractionSuccess(Stopwatch stopwatch, string documentId)
    {
        stopwatch.Stop();
        var tags = new TagList
        {
            { "document.id", documentId },
            { "finance.status", "success" }
        };

        _extractionSuccesses.Add(1, tags);
        _extractionDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    /// <summary>
    /// Record extraction failure.
    /// </summary>
    public void RecordExtractionFailure(Stopwatch stopwatch, string documentId, string errorCode)
    {
        stopwatch.Stop();
        var tags = new TagList
        {
            { "document.id", documentId },
            { "finance.status", "failed" },
            { "finance.error_code", errorCode }
        };

        _extractionFailures.Add(1, tags);
        _extractionDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Signal Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record a finance signal emission (BudgetWarning, VelocitySpike, etc.).
    /// </summary>
    public void RecordSignalEmitted(string signalType, string? matterId = null)
    {
        var tags = new TagList
        {
            { "finance.signal_type", signalType }
        };

        if (!string.IsNullOrEmpty(matterId))
        {
            tags.Add("matter.id", matterId);
        }

        _signalsEmitted.Add(1, tags);
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
