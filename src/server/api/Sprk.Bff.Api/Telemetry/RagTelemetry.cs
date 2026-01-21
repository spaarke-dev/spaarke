using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for RAG (Retrieval-Augmented Generation) operations (OpenTelemetry-compatible).
/// Tracks: indexing jobs, search operations, embedding generation.
///
/// Usage:
/// - Meter name: "Sprk.Bff.Api.Rag" for OpenTelemetry configuration
/// - Metrics: rag.indexing.*, rag.search.*
/// - Dimensions: job.status, search.status, error_code
///
/// Application Insights custom queries:
/// - Indexing success rate: customMetrics | where name startswith "rag.indexing" | summarize count() by customDimensions["job.status"]
/// - Search latency: customMetrics | where name == "rag.search.duration" | summarize avg(value), percentile(value, 95)
/// - Chunks indexed: customMetrics | where name == "rag.indexing.chunks" | summarize sum(value)
/// </summary>
public class RagTelemetry : IDisposable
{
    private readonly Meter _meter;

    // RAG Indexing Job Metrics
    private readonly Counter<long> _indexingJobSuccesses;
    private readonly Counter<long> _indexingJobFailures;
    private readonly Counter<long> _indexingJobDuplicates;
    private readonly Histogram<double> _indexingJobDuration;
    private readonly Counter<long> _chunksIndexed;

    // RAG Search Metrics
    private readonly Counter<long> _searchSuccesses;
    private readonly Counter<long> _searchFailures;
    private readonly Histogram<double> _searchDuration;
    private readonly Histogram<int> _searchResultCount;

    // Meter name for OpenTelemetry
    private const string MeterName = "Sprk.Bff.Api.Rag";

    // Static ActivitySource for distributed tracing
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    public RagTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        // ═══════════════════════════════════════════════════════════════════════════
        // RAG Indexing Job Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _indexingJobSuccesses = _meter.CreateCounter<long>(
            name: "rag.indexing.job.successes",
            unit: "{job}",
            description: "Number of successful RAG indexing jobs");

        _indexingJobFailures = _meter.CreateCounter<long>(
            name: "rag.indexing.job.failures",
            unit: "{job}",
            description: "Number of failed RAG indexing jobs");

        _indexingJobDuplicates = _meter.CreateCounter<long>(
            name: "rag.indexing.job.duplicates",
            unit: "{job}",
            description: "Number of indexing jobs skipped due to idempotency (already processed)");

        _indexingJobDuration = _meter.CreateHistogram<double>(
            name: "rag.indexing.job.duration",
            unit: "ms",
            description: "RAG indexing job duration in milliseconds");

        _chunksIndexed = _meter.CreateCounter<long>(
            name: "rag.indexing.chunks",
            unit: "{chunk}",
            description: "Total number of document chunks indexed");

        // ═══════════════════════════════════════════════════════════════════════════
        // RAG Search Metrics
        // ═══════════════════════════════════════════════════════════════════════════
        _searchSuccesses = _meter.CreateCounter<long>(
            name: "rag.search.successes",
            unit: "{search}",
            description: "Number of successful RAG searches");

        _searchFailures = _meter.CreateCounter<long>(
            name: "rag.search.failures",
            unit: "{search}",
            description: "Number of failed RAG searches");

        _searchDuration = _meter.CreateHistogram<double>(
            name: "rag.search.duration",
            unit: "ms",
            description: "RAG search duration in milliseconds");

        _searchResultCount = _meter.CreateHistogram<int>(
            name: "rag.search.result_count",
            unit: "{result}",
            description: "Number of results returned per search");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RAG Indexing Job Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record successful RAG indexing job completion.
    /// </summary>
    /// <param name="duration">Job execution duration.</param>
    /// <param name="chunksIndexed">Number of chunks successfully indexed.</param>
    public void RecordRagIndexingJobSuccess(TimeSpan duration, int chunksIndexed = 0)
    {
        var tags = new TagList
        {
            { "job.status", "success" }
        };

        _indexingJobSuccesses.Add(1, tags);
        _indexingJobDuration.Record(duration.TotalMilliseconds, tags);

        if (chunksIndexed > 0)
        {
            _chunksIndexed.Add(chunksIndexed, tags);
        }
    }

    /// <summary>
    /// Record RAG indexing job failure.
    /// </summary>
    /// <param name="errorCode">Error code describing the failure type.</param>
    public void RecordRagIndexingJobFailure(string errorCode)
    {
        var tags = new TagList
        {
            { "job.status", "failed" },
            { "job.error_code", errorCode }
        };

        _indexingJobFailures.Add(1, tags);
    }

    /// <summary>
    /// Record skipped indexing job (duplicate/already processed).
    /// </summary>
    public void RecordRagIndexingJobSkippedDuplicate()
    {
        var tags = new TagList
        {
            { "job.status", "skipped" },
            { "job.skip_reason", "duplicate" }
        };

        _indexingJobDuplicates.Add(1, tags);
    }

    /// <summary>
    /// Record bulk RAG indexing job completion.
    /// </summary>
    /// <param name="processedCount">Number of documents successfully indexed.</param>
    /// <param name="errorCount">Number of documents that failed to index.</param>
    /// <param name="skippedCount">Number of documents skipped (already indexed).</param>
    /// <param name="duration">Total job execution duration.</param>
    public void RecordBulkRagIndexingCompleted(int processedCount, int errorCount, int skippedCount, TimeSpan duration)
    {
        var tags = new TagList
        {
            { "job.type", "bulk" },
            { "job.status", errorCount > 0 && processedCount == 0 ? "failed" : errorCount > 0 ? "partial" : "success" }
        };

        _indexingJobSuccesses.Add(processedCount, tags);
        _indexingJobFailures.Add(errorCount, tags);
        _indexingJobDuplicates.Add(skippedCount, tags);
        _indexingJobDuration.Record(duration.TotalMilliseconds, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RAG Search Methods
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Record successful RAG search.
    /// </summary>
    /// <param name="duration">Search execution duration.</param>
    /// <param name="resultCount">Number of results returned.</param>
    /// <param name="tenantId">Tenant ID for filtering metrics (logged as dimension, not content per ADR-015).</param>
    public void RecordRagSearchSuccess(TimeSpan duration, int resultCount, string? tenantId = null)
    {
        var tags = new TagList
        {
            { "search.status", "success" }
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _searchSuccesses.Add(1, tags);
        _searchDuration.Record(duration.TotalMilliseconds, tags);
        _searchResultCount.Record(resultCount, tags);
    }

    /// <summary>
    /// Record RAG search failure.
    /// </summary>
    /// <param name="errorCode">Error code describing the failure type.</param>
    /// <param name="tenantId">Tenant ID for filtering metrics (logged as dimension, not content per ADR-015).</param>
    public void RecordRagSearchFailure(string errorCode, string? tenantId = null)
    {
        var tags = new TagList
        {
            { "search.status", "failed" },
            { "search.error_code", errorCode }
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _searchFailures.Add(1, tags);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Distributed Tracing
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start a new Activity for distributed tracing.
    /// </summary>
    public Activity? StartActivity(string operationName, string? tenantId = null, string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null)
        {
            if (!string.IsNullOrEmpty(tenantId))
            {
                activity.SetTag("tenant.id", tenantId);
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
