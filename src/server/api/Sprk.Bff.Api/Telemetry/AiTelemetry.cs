using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics for AI operations (OpenTelemetry-compatible).
/// Tracks: summarization, RAG search, tool execution, and export operations.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Ai" for OpenTelemetry configuration
/// - Metrics prefixes: ai.summarize.*, ai.rag.*, ai.tool.*, ai.export.*
/// - Common dimensions: ai.status (success/failed), ai.error_code
///
/// Application Insights custom queries:
/// - RAG latency: customMetrics | where name == "ai.rag.duration" | summarize percentile(value, 95)
/// - Tool success rate: customMetrics | where name == "ai.tool.requests" | summarize count() by customDimensions["ai.status"]
/// - Export by format: customMetrics | where name == "ai.export.requests" | summarize count() by customDimensions["ai.format"]
/// </summary>
public class AiTelemetry : IDisposable
{
    private readonly Meter _meter;

    // Summarization metrics
    private readonly Counter<long> _summarizeRequests;
    private readonly Counter<long> _summarizeSuccesses;
    private readonly Counter<long> _summarizeFailures;
    private readonly Histogram<double> _summarizeDuration;
    private readonly Counter<long> _tokenUsage;
    private readonly Histogram<long> _fileSize;

    // RAG metrics
    private readonly Counter<long> _ragRequests;
    private readonly Histogram<double> _ragDuration;
    private readonly Histogram<double> _ragEmbeddingDuration;
    private readonly Histogram<double> _ragSearchDuration;
    private readonly Histogram<long> _ragResultCount;

    // Tool execution metrics
    private readonly Counter<long> _toolRequests;
    private readonly Histogram<double> _toolDuration;
    private readonly Counter<long> _toolTokens;

    // Export metrics
    private readonly Counter<long> _exportRequests;
    private readonly Histogram<double> _exportDuration;
    private readonly Histogram<long> _exportFileSize;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Ai";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public AiTelemetry()
    {
        // Create meter (OpenTelemetry-compatible)
        _meter = new Meter(MeterName, "1.0.0");

        // === Summarization Metrics ===
        _summarizeRequests = _meter.CreateCounter<long>(
            name: "ai.summarize.requests",
            unit: "{request}",
            description: "Total number of summarization requests");

        _summarizeSuccesses = _meter.CreateCounter<long>(
            name: "ai.summarize.successes",
            unit: "{request}",
            description: "Number of successful summarizations");

        _summarizeFailures = _meter.CreateCounter<long>(
            name: "ai.summarize.failures",
            unit: "{request}",
            description: "Number of failed summarizations");

        _summarizeDuration = _meter.CreateHistogram<double>(
            name: "ai.summarize.duration",
            unit: "ms",
            description: "Summarization operation duration in milliseconds");

        _tokenUsage = _meter.CreateCounter<long>(
            name: "ai.summarize.tokens",
            unit: "{token}",
            description: "Total tokens used for summarization");

        _fileSize = _meter.CreateHistogram<long>(
            name: "ai.summarize.file_size",
            unit: "By",
            description: "Size of files processed for summarization");

        // === RAG Metrics ===
        _ragRequests = _meter.CreateCounter<long>(
            name: "ai.rag.requests",
            unit: "{request}",
            description: "Total number of RAG search requests");

        _ragDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.duration",
            unit: "ms",
            description: "Total RAG search duration in milliseconds");

        _ragEmbeddingDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.embedding_duration",
            unit: "ms",
            description: "Embedding generation duration in milliseconds");

        _ragSearchDuration = _meter.CreateHistogram<double>(
            name: "ai.rag.search_duration",
            unit: "ms",
            description: "Azure AI Search query duration in milliseconds");

        _ragResultCount = _meter.CreateHistogram<long>(
            name: "ai.rag.result_count",
            unit: "{result}",
            description: "Number of results returned from RAG search");

        // === Tool Execution Metrics ===
        _toolRequests = _meter.CreateCounter<long>(
            name: "ai.tool.requests",
            unit: "{request}",
            description: "Total number of tool executions");

        _toolDuration = _meter.CreateHistogram<double>(
            name: "ai.tool.duration",
            unit: "ms",
            description: "Tool execution duration in milliseconds");

        _toolTokens = _meter.CreateCounter<long>(
            name: "ai.tool.tokens",
            unit: "{token}",
            description: "Total tokens used by tools");

        // === Export Metrics ===
        _exportRequests = _meter.CreateCounter<long>(
            name: "ai.export.requests",
            unit: "{request}",
            description: "Total number of export requests");

        _exportDuration = _meter.CreateHistogram<double>(
            name: "ai.export.duration",
            unit: "ms",
            description: "Export operation duration in milliseconds");

        _exportFileSize = _meter.CreateHistogram<long>(
            name: "ai.export.file_size",
            unit: "By",
            description: "Size of exported files in bytes");
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

    #region RAG Metrics

    /// <summary>
    /// Record a RAG search operation.
    /// </summary>
    /// <param name="totalDurationMs">Total search duration in milliseconds</param>
    /// <param name="embeddingDurationMs">Embedding generation duration in milliseconds</param>
    /// <param name="searchDurationMs">Azure AI Search query duration in milliseconds</param>
    /// <param name="resultCount">Number of results returned</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="embeddingCacheHit">Whether embedding was retrieved from cache</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordRagSearch(
        double totalDurationMs,
        double embeddingDurationMs,
        double searchDurationMs,
        int resultCount,
        bool success,
        bool embeddingCacheHit = false,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.status", success ? "success" : "failed" },
            { "ai.cache_hit", embeddingCacheHit.ToString().ToLowerInvariant() }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _ragRequests.Add(1, tags);
        _ragDuration.Record(totalDurationMs, tags);
        _ragEmbeddingDuration.Record(embeddingDurationMs, tags);
        _ragSearchDuration.Record(searchDurationMs, tags);
        _ragResultCount.Record(resultCount, tags);
    }

    #endregion

    #region Tool Metrics

    /// <summary>
    /// Record a tool execution.
    /// </summary>
    /// <param name="toolId">Tool identifier (e.g., EntityExtractor, ClauseAnalyzer)</param>
    /// <param name="durationMs">Execution duration in milliseconds</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="inputTokens">Number of input tokens used</param>
    /// <param name="outputTokens">Number of output tokens generated</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordToolExecution(
        string toolId,
        double durationMs,
        bool success,
        int inputTokens = 0,
        int outputTokens = 0,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.tool_id", toolId },
            { "ai.status", success ? "success" : "failed" }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _toolRequests.Add(1, tags);
        _toolDuration.Record(durationMs, tags);

        if (inputTokens > 0 || outputTokens > 0)
        {
            _toolTokens.Add(inputTokens,
                new KeyValuePair<string, object?>("ai.tool_id", toolId),
                new KeyValuePair<string, object?>("ai.token_type", "input"));
            _toolTokens.Add(outputTokens,
                new KeyValuePair<string, object?>("ai.tool_id", toolId),
                new KeyValuePair<string, object?>("ai.token_type", "output"));
        }
    }

    #endregion

    #region Export Metrics

    /// <summary>
    /// Record an export operation.
    /// </summary>
    /// <param name="format">Export format (docx, pdf, email)</param>
    /// <param name="durationMs">Export duration in milliseconds</param>
    /// <param name="success">Whether the operation succeeded</param>
    /// <param name="fileSizeBytes">Size of exported file in bytes (null for action-based exports)</param>
    /// <param name="errorCode">Error code if failed</param>
    public void RecordExport(
        string format,
        double durationMs,
        bool success,
        long? fileSizeBytes = null,
        string? errorCode = null)
    {
        var tags = new TagList
        {
            { "ai.format", format.ToLowerInvariant() },
            { "ai.status", success ? "success" : "failed" }
        };
        if (!success && errorCode != null)
        {
            tags.Add("ai.error_code", errorCode);
        }

        _exportRequests.Add(1, tags);
        _exportDuration.Record(durationMs, tags);

        if (fileSizeBytes.HasValue && fileSizeBytes.Value > 0)
        {
            _exportFileSize.Record(fileSizeBytes.Value, tags);
        }
    }

    #endregion

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
