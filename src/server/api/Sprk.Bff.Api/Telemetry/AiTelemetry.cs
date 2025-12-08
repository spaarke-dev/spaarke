using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics for AI summarization operations (OpenTelemetry-compatible).
/// Tracks: request count, success/failure, duration, token usage.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Ai" for OpenTelemetry configuration
/// - Metrics: ai.summarize.requests, ai.summarize.duration, ai.summarize.tokens
/// - Dimensions: ai.status (success/failed), ai.method (streaming/batch), ai.extraction (native/docint/vision)
///
/// Application Insights custom queries:
/// - Success rate: customMetrics | where name == "ai.summarize.requests" | summarize count() by customDimensions["ai.status"]
/// - Token usage: customMetrics | where name == "ai.summarize.tokens" | summarize sum(value) by customDimensions["ai.token_type"]
/// </summary>
public class AiTelemetry : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _summarizeRequests;
    private readonly Counter<long> _summarizeSuccesses;
    private readonly Counter<long> _summarizeFailures;
    private readonly Histogram<double> _summarizeDuration;
    private readonly Counter<long> _tokenUsage;
    private readonly Histogram<long> _fileSize;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Ai";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public AiTelemetry()
    {
        // Create meter (OpenTelemetry-compatible)
        _meter = new Meter(MeterName, "1.0.0");

        // Counter: Total summarization requests
        _summarizeRequests = _meter.CreateCounter<long>(
            name: "ai.summarize.requests",
            unit: "{request}",
            description: "Total number of summarization requests");

        // Counter: Successful summarizations
        _summarizeSuccesses = _meter.CreateCounter<long>(
            name: "ai.summarize.successes",
            unit: "{request}",
            description: "Number of successful summarizations");

        // Counter: Failed summarizations
        _summarizeFailures = _meter.CreateCounter<long>(
            name: "ai.summarize.failures",
            unit: "{request}",
            description: "Number of failed summarizations");

        // Histogram: Summarization duration
        _summarizeDuration = _meter.CreateHistogram<double>(
            name: "ai.summarize.duration",
            unit: "ms",
            description: "Summarization operation duration in milliseconds");

        // Counter: Token usage (for cost tracking)
        _tokenUsage = _meter.CreateCounter<long>(
            name: "ai.summarize.tokens",
            unit: "{token}",
            description: "Total tokens used for summarization");

        // Histogram: File size processed
        _fileSize = _meter.CreateHistogram<long>(
            name: "ai.summarize.file_size",
            unit: "By",
            description: "Size of files processed for summarization");
    }

    /// <summary>
    /// Record the start of a summarization request.
    /// Returns a Stopwatch for timing.
    /// </summary>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    public Stopwatch RecordRequestStart(string method = "streaming", string? extraction = null)
    {
        var tags = new TagList
        {
            { "ai.method", method }
        };
        if (extraction != null)
        {
            tags.Add("ai.extraction", extraction);
        }

        _summarizeRequests.Add(1, tags);
        return Stopwatch.StartNew();
    }

    /// <summary>
    /// Record successful summarization completion.
    /// </summary>
    /// <param name="stopwatch">Stopwatch from RecordRequestStart</param>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    /// <param name="fileType">File extension (e.g., .pdf, .txt)</param>
    /// <param name="fileSizeBytes">Size of the file in bytes</param>
    public void RecordSuccess(
        Stopwatch stopwatch,
        string method = "streaming",
        string? extraction = null,
        string? fileType = null,
        long? fileSizeBytes = null)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "ai.method", method },
            { "ai.status", "success" }
        };
        if (extraction != null) tags.Add("ai.extraction", extraction);
        if (fileType != null) tags.Add("ai.file_type", fileType);

        _summarizeSuccesses.Add(1, tags);
        _summarizeDuration.Record(durationMs, tags);

        if (fileSizeBytes.HasValue)
        {
            _fileSize.Record(fileSizeBytes.Value, tags);
        }
    }

    /// <summary>
    /// Record failed summarization.
    /// </summary>
    /// <param name="stopwatch">Stopwatch from RecordRequestStart</param>
    /// <param name="errorCode">Error code (e.g., openai_rate_limit, extraction_failed)</param>
    /// <param name="method">Processing method: streaming, batch</param>
    /// <param name="extraction">Extraction method: native, document_intelligence, vision</param>
    public void RecordFailure(
        Stopwatch stopwatch,
        string errorCode,
        string method = "streaming",
        string? extraction = null)
    {
        stopwatch.Stop();
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        var tags = new TagList
        {
            { "ai.method", method },
            { "ai.status", "failed" },
            { "ai.error_code", errorCode }
        };
        if (extraction != null) tags.Add("ai.extraction", extraction);

        _summarizeFailures.Add(1, tags);
        _summarizeDuration.Record(durationMs, tags);
    }

    /// <summary>
    /// Record token usage for cost tracking.
    /// </summary>
    /// <param name="promptTokens">Number of tokens in the prompt</param>
    /// <param name="completionTokens">Number of tokens in the completion</param>
    /// <param name="model">Model name (e.g., gpt-4o-mini, gpt-4o)</param>
    public void RecordTokenUsage(long promptTokens, long completionTokens, string model = "gpt-4o-mini")
    {
        _tokenUsage.Add(promptTokens,
            new KeyValuePair<string, object?>("ai.token_type", "prompt"),
            new KeyValuePair<string, object?>("ai.model", model));

        _tokenUsage.Add(completionTokens,
            new KeyValuePair<string, object?>("ai.token_type", "completion"),
            new KeyValuePair<string, object?>("ai.model", model));
    }

    /// <summary>
    /// Start a new Activity for distributed tracing.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., SummarizeStream, SummarizeBatch)</param>
    /// <param name="documentId">Document ID being processed</param>
    public Activity? StartActivity(string operationName, Guid? documentId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null && documentId.HasValue)
        {
            activity.SetTag("document.id", documentId.Value.ToString());
        }
        return activity;
    }

    /// <summary>
    /// Dispose the meter when the service is disposed.
    /// </summary>
    public void Dispose()
    {
        _meter?.Dispose();
    }
}

/// <summary>
/// Extension methods for metric tag building.
/// </summary>
public static class AiTelemetryExtensions
{
    /// <summary>
    /// Convert TextExtractionMethod to telemetry-friendly string.
    /// </summary>
    public static string ToTelemetryString(this Models.Ai.TextExtractionMethod method) => method switch
    {
        Models.Ai.TextExtractionMethod.Native => "native",
        Models.Ai.TextExtractionMethod.DocumentIntelligence => "document_intelligence",
        Models.Ai.TextExtractionMethod.VisionOcr => "vision",
        Models.Ai.TextExtractionMethod.NotSupported => "not_supported",
        _ => "unknown"
    };
}
