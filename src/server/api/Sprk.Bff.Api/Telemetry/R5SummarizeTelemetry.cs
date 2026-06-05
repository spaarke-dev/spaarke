using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for the R5 Summarize-for-Chat vertical slice (OpenTelemetry-compatible).
/// Tracks: invocation counts + token usage + latency + file_count distributions across BOTH
/// invocation paths (agent-tool dispatch AND direct endpoint), plus per-session
/// `spaarke-session-files` index document-count observations for cost + eviction tuning.
///
/// Usage:
/// - Meter name: <c>Sprk.Bff.Api.R5Summarize</c> for OpenTelemetry configuration (wired in
///   <c>TelemetryModule.AddTelemetryModule</c>).
/// - Singleton lifetime (per <c>AnalysisServicesModule</c>). Unconditionally registered
///   alongside <see cref="AiTelemetry"/>; R5 introduces NO new feature flags per CLAUDE.md §3.2.
///
/// Locked event schema (downstream contract for Phase 3 task 042 / D3-03 Kusto dashboards —
/// renames after this commit require coordinated dashboard updates):
///
/// | Instrument                          | Type             | Dimensions (BOUNDED)                                                 |
/// |-------------------------------------|------------------|----------------------------------------------------------------------|
/// | r5.summarize.invocation             | Counter&lt;long&gt;    | path, completion_status, tenant.id (optional)                        |
/// | r5.summarize.file_count             | Histogram&lt;long&gt;  | path, completion_status, tenant.id (optional)                        |
/// | r5.summarize.total_tokens           | Histogram&lt;long&gt;  | path, completion_status, tenant.id (optional)                        |
/// | r5.summarize.latency_ms             | Histogram&lt;double&gt;| path, completion_status, tenant.id (optional)                        |
/// | r5.session_files.index_size         | Histogram&lt;long&gt;  | phase, tenant.id (optional)                                          |
///
/// Bounded enum dimensions:
/// - <c>path</c> ∈ { <c>agent_tool</c>, <c>direct_endpoint</c> }
/// - <c>completion_status</c> ∈ { <c>success</c>, <c>failed</c>, <c>declined</c>, <c>cancelled</c> }
/// - <c>phase</c> ∈ { <c>post_write</c>, <c>post_evict</c>, <c>post_cleanup</c> }
///
/// Out-of-enum input is rejected at the call site with <see cref="ArgumentException"/> (loud-fail
/// during development, cardinality safety in production). High-cardinality identifiers
/// (<c>sessionId</c>, correlation IDs, user IDs, file names, prompt text, document content) are
/// EXCLUDED from metric dimensions per ADR-014 + ADR-015 cardinality discipline; correlation IDs
/// belong on the <see cref="Activity"/> / span context only.
///
/// Application Insights / Kusto sample queries:
/// - Invocation path mix: <c>customMetrics | where name == "r5.summarize.invocation" | summarize count() by customDimensions["path"]</c>
/// - Completion status breakdown: <c>customMetrics | where name == "r5.summarize.invocation" | summarize count() by customDimensions["completion_status"]</c>
/// - Per-session token-budget burn (cost dashboards, spec NFR-06): <c>customMetrics | where name == "r5.summarize.total_tokens" | summarize percentile(value, 50), percentile(value, 95)</c>
/// - Cleanup-cadence tuning input (spec NFR-02): <c>customMetrics | where name == "r5.session_files.index_size" | summarize avg(value) by customDimensions["phase"], bin(timestamp, 1h)</c>
/// </summary>
public sealed class R5SummarizeTelemetry : IDisposable
{
    /// <summary>Meter name for OpenTelemetry registration. Stable downstream contract.</summary>
    public const string MeterName = "Sprk.Bff.Api.R5Summarize";

    /// <summary>Static <see cref="ActivitySource"/> for distributed tracing (matches AiTelemetry / RagTelemetry precedent).</summary>
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    // Cardinality enforcement — invalid input throws ArgumentException at the call site so
    // unbounded dimensions cannot silently pollute the metric store. See class-level remarks.
    private static readonly HashSet<string> ValidPaths = new(StringComparer.Ordinal)
    {
        "agent_tool",
        "direct_endpoint",
    };

    private static readonly HashSet<string> ValidCompletionStatuses = new(StringComparer.Ordinal)
    {
        "success",
        "failed",
        "declined",
        "cancelled",
    };

    private static readonly HashSet<string> ValidPhases = new(StringComparer.Ordinal)
    {
        "post_write",
        "post_evict",
        "post_cleanup",
    };

    private readonly Meter _meter;

    // Single canonical invocation event — BOTH agent-tool path AND direct-endpoint path increment this.
    private readonly Counter<long> _invocationCounter;

    // Distributional metrics keyed on the same (path, completion_status, tenant.id) tag set as the invocation counter.
    private readonly Histogram<long> _fileCountHistogram;
    private readonly Histogram<long> _totalTokensHistogram;
    private readonly Histogram<double> _latencyMsHistogram;

    // Session-files index document-count observation (cleanup-cadence + cost-tuning input per spec NFR-02 / NFR-06).
    private readonly Histogram<long> _sessionFilesIndexSizeHistogram;

    public R5SummarizeTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _invocationCounter = _meter.CreateCounter<long>(
            name: "r5.summarize.invocation",
            unit: "{invocation}",
            description: "Number of R5 Summarize-for-Chat invocations (both agent-tool and direct-endpoint paths converge here).");

        _fileCountHistogram = _meter.CreateHistogram<long>(
            name: "r5.summarize.file_count",
            unit: "{file}",
            description: "Number of files included in a single R5 Summarize invocation.");

        _totalTokensHistogram = _meter.CreateHistogram<long>(
            name: "r5.summarize.total_tokens",
            unit: "{token}",
            description: "Total tokens (prompt + completion) consumed by an R5 Summarize invocation. Cost dashboards derive cost-per-session from this distribution per spec NFR-06.");

        _latencyMsHistogram = _meter.CreateHistogram<double>(
            name: "r5.summarize.latency_ms",
            unit: "ms",
            description: "End-to-end latency of an R5 Summarize invocation in milliseconds.");

        _sessionFilesIndexSizeHistogram = _meter.CreateHistogram<long>(
            name: "r5.session_files.index_size",
            unit: "{document}",
            description: "Per-tenant `spaarke-session-files` index document count observed at a specific phase (post_write, post_evict, post_cleanup). Cleanup-cadence tuning input per spec NFR-02.");
    }

    /// <summary>
    /// Record a completed R5 Summarize invocation. Called from BOTH the agent-tool path
    /// (<c>InvokeSummarizePlaybookTool</c> on <c>SprkChatAgent</c>) AND the direct-endpoint path
    /// (<c>POST /api/ai/chat/sessions/{id}/summarize</c>). Both paths converge on the same
    /// counter so dashboards see a single coherent event stream.
    /// </summary>
    /// <param name="path">
    /// Invocation path. MUST be one of: <c>agent_tool</c> | <c>direct_endpoint</c>.
    /// Throws <see cref="ArgumentException"/> on out-of-enum input (cardinality safety).
    /// </param>
    /// <param name="completionStatus">
    /// Final completion status. MUST be one of: <c>success</c> | <c>failed</c> | <c>declined</c> | <c>cancelled</c>.
    /// Throws <see cref="ArgumentException"/> on out-of-enum input (cardinality safety).
    /// </param>
    /// <param name="fileCount">Number of files included in this invocation (recorded on the file_count histogram).</param>
    /// <param name="totalTokens">Total tokens consumed (prompt + completion). Recorded on the total_tokens histogram for cost dashboards.</param>
    /// <param name="latencyMs">End-to-end latency in milliseconds (recorded on the latency_ms histogram).</param>
    /// <param name="tenantId">
    /// Optional tenant identifier — low-cardinality dimension per <c>RagTelemetry.RecordRagSearchSuccess</c> precedent
    /// (ADR-014). Pass <c>null</c> to omit the dimension. Never pass session IDs, user IDs, correlation IDs, file
    /// names, or any free-form text — those are high-cardinality and prohibited by ADR-015 + cardinality discipline.
    /// </param>
    /// <exception cref="ArgumentException">If <paramref name="path"/> or <paramref name="completionStatus"/> is not in the allowed enum.</exception>
    public void RecordSummarizeInvocation(
        string path,
        string completionStatus,
        long fileCount,
        long totalTokens,
        double latencyMs,
        string? tenantId = null)
    {
        if (!ValidPaths.Contains(path))
        {
            throw new ArgumentException(
                $"Invalid R5 Summarize invocation path '{path}'. Must be one of: {string.Join(", ", ValidPaths)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store.",
                nameof(path));
        }

        if (!ValidCompletionStatuses.Contains(completionStatus))
        {
            throw new ArgumentException(
                $"Invalid R5 Summarize completion_status '{completionStatus}'. Must be one of: {string.Join(", ", ValidCompletionStatuses)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store.",
                nameof(completionStatus));
        }

        var tags = new TagList
        {
            { "path", path },
            { "completion_status", completionStatus },
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _invocationCounter.Add(1, tags);
        _fileCountHistogram.Record(fileCount, tags);
        _totalTokensHistogram.Record(totalTokens, tags);
        _latencyMsHistogram.Record(latencyMs, tags);
    }

    /// <summary>
    /// Record an observation of the per-tenant <c>spaarke-session-files</c> index document count
    /// at a specific lifecycle phase. Consumed by:
    /// - Task 003 RAG indexing pipeline at <c>phase = "post_write"</c> after successful indexing.
    /// - Task 007 session-files cleanup <c>IHostedService</c> at <c>phase = "post_evict"</c> after eviction
    ///   and <c>phase = "post_cleanup"</c> after a full sweep.
    /// Drives cleanup-cadence tuning per spec NFR-02 (default 6h sweep; tune to 1h if storage grows).
    /// </summary>
    /// <param name="phase">
    /// Lifecycle phase. MUST be one of: <c>post_write</c> | <c>post_evict</c> | <c>post_cleanup</c>.
    /// Throws <see cref="ArgumentException"/> on out-of-enum input (cardinality safety).
    /// </param>
    /// <param name="documentCount">Observed document count in the index for this tenant.</param>
    /// <param name="tenantId">
    /// Optional tenant identifier — low-cardinality dimension per <c>RagTelemetry.RecordRagSearchSuccess</c> precedent
    /// (ADR-014). Pass <c>null</c> to omit the dimension.
    /// </param>
    /// <exception cref="ArgumentException">If <paramref name="phase"/> is not in the allowed enum.</exception>
    public void RecordSessionFilesIndexSize(
        string phase,
        long documentCount,
        string? tenantId = null)
    {
        if (!ValidPhases.Contains(phase))
        {
            throw new ArgumentException(
                $"Invalid R5 session-files phase '{phase}'. Must be one of: {string.Join(", ", ValidPhases)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store.",
                nameof(phase));
        }

        var tags = new TagList
        {
            { "phase", phase },
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _sessionFilesIndexSizeHistogram.Record(documentCount, tags);
    }

    /// <summary>
    /// Start a new <see cref="Activity"/> for distributed tracing of an R5 Summarize operation.
    /// Mirrors the <c>AiTelemetry.StartActivity</c> / <c>RagTelemetry.StartActivity</c> pattern.
    /// </summary>
    /// <param name="operationName">Logical operation name (e.g., <c>SummarizeForChat</c>).</param>
    /// <param name="tenantId">Optional tenant id to set as an Activity tag (low-cardinality dimension per ADR-014).</param>
    /// <param name="correlationId">
    /// Optional correlation id set as an Activity tag. Correlation IDs belong on the span context, NOT
    /// on metric dimensions — that's the explicit cardinality discipline.
    /// </param>
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

    /// <summary>Dispose the meter when the service is disposed.</summary>
    public void Dispose()
    {
        _meter.Dispose();
    }
}
