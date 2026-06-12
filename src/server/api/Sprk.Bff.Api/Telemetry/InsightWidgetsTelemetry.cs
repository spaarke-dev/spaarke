using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Metrics and tracing for the Insights Engine Widgets r1 surface (OpenTelemetry-compatible).
/// Tracks: per-invocation counts + latency distributions for the <c>InsightSummaryCard</c>
/// widget's BFF invocation path (`/api/insights/ask` → <c>IInsightsAi.AnswerQuestionAsync</c>),
/// including cache-hit/miss split and kill-switch classification per ADR-018/ADR-032.
///
/// Usage:
/// - Meter name: <c>Sprk.Bff.Api.InsightWidgets</c> for OpenTelemetry configuration (wired in
///   <c>TelemetryModule.AddTelemetryModule</c>). Resolved per project Q-U8 evidence:
///   matches all 9 existing BFF meters' <c>Sprk.Bff.Api.&lt;Feature&gt;</c> convention
///   (see <see cref="R5SummarizeTelemetry.MeterName"/>:49, <see cref="InsightsCacheMetrics"/>:33).
///   NOT <c>Spaarke.InsightWidgets</c> — that earlier spec wording was superseded.
/// - Singleton lifetime (per <c>AnalysisServicesModule</c>). Unconditional registration mirrors
///   <see cref="R5SummarizeTelemetry"/>: telemetry surface is harmless when unused (zero events
///   emitted) and sidesteps the asymmetric-registration anti-pattern (root CLAUDE.md §10 F.1).
///
/// Locked event schema (downstream contract for spec NFR-06 + task 066 KQL verification —
/// renames after this commit require coordinated dashboard updates):
///
/// | Instrument                          | Type             | Dimensions (BOUNDED)                                                 |
/// |-------------------------------------|------------------|----------------------------------------------------------------------|
/// | widget.insightcard.invoked          | Counter&lt;long&gt;    | topic, mode, outcome, cacheHit, tenant.id (optional)                 |
/// | widget.insightcard.duration         | Histogram&lt;double&gt;| topic, mode, outcome, cacheHit, tenant.id (optional)                 |
///
/// Bounded enum dimensions (cardinality-safe per ADR-014 + ADR-015):
/// - <c>topic</c> ∈ { <c>matter-health</c> } — r1 ships Matter Health single-mode; extensible as new
///   `sprk_aitopicregistry` rows are added.
/// - <c>mode</c> ∈ { <c>single</c>, <c>multi</c>, <c>cohort</c> } — r1 uses <c>single</c>; <c>multi</c> +
///   <c>cohort</c> framework-shape for r2+ multi-entity subjects.
/// - <c>outcome</c> ∈ { <c>success</c>, <c>failed</c>, <c>cache_hit</c>, <c>kill_switched</c> } — distinguishes
///   ADR-018/032 503 ProblemDetails kill-switch path from generic failure.
/// - <c>cacheHit</c> ∈ { <c>true</c>, <c>false</c> } — boolean from <c>IInsightsPlaybookExecutionCache</c>
///   hit/miss; redundant with <c>outcome == "cache_hit"</c> but kept as separate dimension for
///   dashboard convenience (per spec NFR-06 explicit list).
/// - <c>tenant.id</c> — optional low-cardinality dimension (per R5 + RagTelemetry precedent).
///
/// Out-of-enum input is rejected at the call site with <see cref="ArgumentException"/> (loud-fail
/// during development, cardinality safety in production). High-cardinality identifiers
/// (<c>subject</c> matter GUID, correlation IDs, user IDs, free-form text) are EXCLUDED from
/// metric dimensions per ADR-014 + ADR-015 cardinality discipline; the matter GUID belongs on
/// the <see cref="Activity"/> / span context only (set via <see cref="StartActivity"/>).
///
/// Application Insights / Kusto sample queries (task 066 SC-11 verification):
/// - Invocation volume by topic: <c>customMetrics | where name == "widget.insightcard.invoked" | summarize count() by customDimensions["topic"]</c>
/// - Cache-hit rate: <c>customMetrics | where name == "widget.insightcard.invoked" | summarize sum(value) by customDimensions["cacheHit"]</c>
/// - p95 latency: <c>customMetrics | where name == "widget.insightcard.duration" | summarize percentile(value, 95) by customDimensions["topic"], customDimensions["mode"]</c>
/// - Kill-switch frequency (ADR-018): <c>customMetrics | where name == "widget.insightcard.invoked" | where customDimensions["outcome"] == "kill_switched" | count</c>
/// </summary>
public sealed class InsightWidgetsTelemetry : IDisposable
{
    /// <summary>Meter name for OpenTelemetry registration. Stable downstream contract per Q-U8.</summary>
    public const string MeterName = "Sprk.Bff.Api.InsightWidgets";

    /// <summary>Static <see cref="ActivitySource"/> for distributed tracing (matches R5SummarizeTelemetry / RagTelemetry precedent).</summary>
    public static readonly ActivitySource ActivitySource = new(MeterName, "1.0.0");

    // Cardinality enforcement — invalid input throws ArgumentException at the call site so
    // unbounded dimensions cannot silently pollute the metric store. See class-level remarks.
    private static readonly HashSet<string> ValidTopics = new(StringComparer.Ordinal)
    {
        "matter-health",
        // Extensible: new entries here when additional `sprk_aitopicregistry` rows ship.
    };

    private static readonly HashSet<string> ValidModes = new(StringComparer.Ordinal)
    {
        "single",
        "multi",
        "cohort",
    };

    private static readonly HashSet<string> ValidOutcomes = new(StringComparer.Ordinal)
    {
        "success",
        "failed",
        "cache_hit",
        "kill_switched",
    };

    private readonly Meter _meter;

    private readonly Counter<long> _invocationCounter;
    private readonly Histogram<double> _durationHistogram;

    public InsightWidgetsTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _invocationCounter = _meter.CreateCounter<long>(
            name: "widget.insightcard.invoked",
            unit: "{invocation}",
            description: "Number of InsightSummaryCard widget invocations through the BFF /api/insights/ask path.");

        _durationHistogram = _meter.CreateHistogram<double>(
            name: "widget.insightcard.duration",
            unit: "ms",
            description: "End-to-end duration of an InsightSummaryCard widget invocation in milliseconds (includes cache lookup + playbook execution when not cached).");
    }

    /// <summary>
    /// Record a completed InsightSummaryCard widget invocation. Called from the BFF
    /// invocation path (task 051 wiring) after the playbook execution + cache decision
    /// completes. Both counter and duration histogram are incremented on a single call
    /// so dashboards see a coherent event stream.
    /// </summary>
    /// <param name="topic">
    /// Topic registry code from <c>sprk_aitopicregistry.sprk_topiccode</c>. MUST be one of the
    /// bounded values in <see cref="ValidTopics"/> (r1: <c>matter-health</c>).
    /// Throws <see cref="ArgumentException"/> on out-of-enum input (cardinality safety).
    /// </param>
    /// <param name="mode">
    /// Subject scope. MUST be one of: <c>single</c> | <c>multi</c> | <c>cohort</c>.
    /// r1 uses <c>single</c>; <c>multi</c>/<c>cohort</c> reserved for r2+ multi-entity subjects.
    /// Throws <see cref="ArgumentException"/> on out-of-enum input (cardinality safety).
    /// </param>
    /// <param name="outcome">
    /// Final invocation outcome. MUST be one of: <c>success</c> | <c>failed</c> | <c>cache_hit</c> | <c>kill_switched</c>.
    /// <c>kill_switched</c> classifies the ADR-018/032 503 ProblemDetails path distinctly from
    /// generic <c>failed</c>. Throws <see cref="ArgumentException"/> on out-of-enum input.
    /// </param>
    /// <param name="cacheHit">
    /// Whether the invocation was served from <c>IInsightsPlaybookExecutionCache</c>. Boolean is
    /// cardinality-safe. Redundant with <paramref name="outcome"/> == <c>cache_hit</c> but kept as
    /// separate dimension per spec NFR-06 explicit tag list.
    /// </param>
    /// <param name="durationMs">End-to-end duration in milliseconds (recorded on the duration histogram).</param>
    /// <param name="tenantId">
    /// Optional tenant identifier — low-cardinality dimension per <c>R5SummarizeTelemetry</c> +
    /// <c>RagTelemetry</c> precedent (ADR-014). Pass <c>null</c> to omit the dimension. Never pass
    /// matter GUIDs, user IDs, correlation IDs, or any free-form text — those are high-cardinality
    /// and prohibited by ADR-015. The matter GUID (subject) belongs on the <see cref="Activity"/>
    /// span context only — set it via <see cref="StartActivity"/>.
    /// </param>
    /// <exception cref="ArgumentException">If <paramref name="topic"/>, <paramref name="mode"/>, or <paramref name="outcome"/> is not in the allowed enum.</exception>
    public void RecordInvocation(
        string topic,
        string mode,
        string outcome,
        bool cacheHit,
        double durationMs,
        string? tenantId = null)
    {
        if (!ValidTopics.Contains(topic))
        {
            throw new ArgumentException(
                $"Invalid InsightWidgets topic '{topic}'. Must be one of: {string.Join(", ", ValidTopics)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store. " +
                "Add new topic codes to ValidTopics when shipping new sprk_aitopicregistry rows.",
                nameof(topic));
        }

        if (!ValidModes.Contains(mode))
        {
            throw new ArgumentException(
                $"Invalid InsightWidgets mode '{mode}'. Must be one of: {string.Join(", ", ValidModes)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store.",
                nameof(mode));
        }

        if (!ValidOutcomes.Contains(outcome))
        {
            throw new ArgumentException(
                $"Invalid InsightWidgets outcome '{outcome}'. Must be one of: {string.Join(", ", ValidOutcomes)}. " +
                "This guard prevents high-cardinality dimensions from polluting the metric store.",
                nameof(outcome));
        }

        var tags = new TagList
        {
            { "topic", topic },
            { "mode", mode },
            { "outcome", outcome },
            { "cacheHit", cacheHit ? "true" : "false" },
        };

        if (!string.IsNullOrEmpty(tenantId))
        {
            tags.Add("tenant.id", tenantId);
        }

        _invocationCounter.Add(1, tags);
        _durationHistogram.Record(durationMs, tags);
    }

    /// <summary>
    /// Start a new <see cref="Activity"/> for distributed tracing of an InsightSummaryCard
    /// invocation. Mirrors the <see cref="R5SummarizeTelemetry.StartActivity"/> pattern.
    /// </summary>
    /// <param name="operationName">Logical operation name (e.g., <c>InsightSummaryCard.Invoke</c>).</param>
    /// <param name="tenantId">Optional tenant id to set as an Activity tag (low-cardinality dimension per ADR-014).</param>
    /// <param name="subject">
    /// Optional subject identifier (e.g., <c>matter:GUID</c>) set as an Activity tag. The subject is
    /// high-cardinality and belongs on the span context only — NOT on metric dimensions. This is
    /// the explicit ADR-014/015 cardinality discipline.
    /// </param>
    /// <param name="correlationId">
    /// Optional correlation id set as an Activity tag. Correlation IDs belong on the span context, NOT
    /// on metric dimensions.
    /// </param>
    public Activity? StartActivity(
        string operationName,
        string? tenantId = null,
        string? subject = null,
        string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity(operationName, ActivityKind.Internal);
        if (activity != null)
        {
            if (!string.IsNullOrEmpty(tenantId))
            {
                activity.SetTag("tenant.id", tenantId);
            }

            if (!string.IsNullOrEmpty(subject))
            {
                activity.SetTag("subject", subject);
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
