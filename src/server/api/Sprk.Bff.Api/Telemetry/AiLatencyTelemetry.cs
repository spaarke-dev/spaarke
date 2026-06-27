using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry metrics for AI chat latency instrumentation (AIPU2-066).
///
/// Meter name: <c>Sprk.Bff.Api.AiLatency</c> — registered in <see cref="Infrastructure.DI.TelemetryModule"/>.
///
/// Emitted instruments:
///   - <c>ai_chat_ttft_ms</c>              Histogram — Time to First Token (driven by prompt size)
///   - <c>ai_chat_tbt_ms</c>               Histogram — Time Between Tokens (generation throughput)
///   - <c>ai_chat_ttlt_ms</c>              Histogram — Time to Last Token (full response time)
///   - <c>ai_chat_prompt_tokens</c>         Histogram — Processed prompt tokens per call
///   - <c>ai_chat_generated_tokens_total</c> Counter  — Total completion tokens accumulated
///   - <c>ai_routing_total_latency_ms</c>   Histogram — End-to-end routing decision time
///
/// Labels on all instruments:
///   - <c>model</c>:         e.g. <c>gpt-4o</c>, <c>gpt-4o-mini</c>
///   - <c>routing_layer</c>: <c>1</c> | <c>2</c> | <c>3</c>
///
/// Alert thresholds (checked by callers after recording):
///   - TTFT   &gt; 1500 ms  → <see cref="IsTtftExceeded"/>
///   - Prompt &gt; 9000 tokens → <see cref="IsPromptBudgetExceeded"/>
///
/// Application Insights KQL queries:
///   TTFT P95:        customMetrics | where name == "ai_chat_ttft_ms"  | summarize percentile(value, 95) by bin(timestamp, 5m)
///   Prompt budget:   customMetrics | where name == "ai_chat_prompt_tokens" | summarize percentile(value, 95) by bin(timestamp, 5m)
///   Route tier dist: customMetrics | where name == "ai_routing_total_latency_ms" | summarize count() by tostring(customDimensions["routing_layer"])
/// </summary>
public sealed class AiLatencyTelemetry : IDisposable
{
    public const string MeterName = "Sprk.Bff.Api.AiLatency";

    // Alert thresholds
    private const double TtftAlertThresholdMs = 1500.0;
    private const int PromptBudgetAlertThreshold = 9000;

    private readonly Meter _meter;

    // ── Streaming latency instruments ────────────────────────────────────────
    private readonly Histogram<double> _ttftMs;
    private readonly Histogram<double> _tbtMs;
    private readonly Histogram<double> _ttltMs;

    // ── Token count instruments ───────────────────────────────────────────────
    private readonly Histogram<long> _promptTokens;
    private readonly Counter<long> _generatedTokensTotal;

    // ── Routing latency ───────────────────────────────────────────────────────
    private readonly Histogram<double> _routingTotalLatencyMs;

    public AiLatencyTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _ttftMs = _meter.CreateHistogram<double>(
            name: "ai_chat_ttft_ms",
            unit: "ms",
            description: "Time to First Token — elapsed milliseconds from request start until the first SSE token byte is written.");

        _tbtMs = _meter.CreateHistogram<double>(
            name: "ai_chat_tbt_ms",
            unit: "ms",
            description: "Time Between Tokens — inter-token gap in milliseconds (generation throughput proxy).");

        _ttltMs = _meter.CreateHistogram<double>(
            name: "ai_chat_ttlt_ms",
            unit: "ms",
            description: "Time to Last Token — total elapsed milliseconds from request start until the SSE done event is emitted.");

        _promptTokens = _meter.CreateHistogram<long>(
            name: "ai_chat_prompt_tokens",
            unit: "{token}",
            description: "Prompt tokens consumed per chat turn (drives model tier routing decision).");

        _generatedTokensTotal = _meter.CreateCounter<long>(
            name: "ai_chat_generated_tokens_total",
            unit: "{token}",
            description: "Cumulative completion tokens generated across all chat turns.");

        _routingTotalLatencyMs = _meter.CreateHistogram<double>(
            name: "ai_routing_total_latency_ms",
            unit: "ms",
            description: "End-to-end routing decision latency in milliseconds.");
    }

    // ── Recording helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Records the Time to First Token for a completed streaming turn.
    /// </summary>
    /// <param name="ttftMs">Elapsed milliseconds from request receipt to first SSE token byte.</param>
    /// <param name="model">Model deployment name (e.g. <c>gpt-4o</c>, <c>gpt-4o-mini</c>).</param>
    /// <param name="routingLayer">Routing tier that selected the model: <c>1</c>, <c>2</c>, or <c>3</c>.</param>
    public void RecordTtft(double ttftMs, string model, int routingLayer)
    {
        _ttftMs.Record(ttftMs, BuildTags(model, routingLayer));
    }

    /// <summary>
    /// Records the inter-token gap (Time Between Tokens) for a single token in a streaming turn.
    /// Call once per token after the first token has been emitted.
    /// </summary>
    /// <param name="tbtMs">Milliseconds elapsed since the previous token was written.</param>
    /// <param name="model">Model deployment name.</param>
    /// <param name="routingLayer">Routing tier: <c>1</c>, <c>2</c>, or <c>3</c>.</param>
    public void RecordTbt(double tbtMs, string model, int routingLayer)
    {
        _tbtMs.Record(tbtMs, BuildTags(model, routingLayer));
    }

    /// <summary>
    /// Records the Time to Last Token (full response latency) for a completed streaming turn.
    /// </summary>
    /// <param name="ttltMs">Total elapsed milliseconds from request receipt to the SSE done event.</param>
    /// <param name="model">Model deployment name.</param>
    /// <param name="routingLayer">Routing tier: <c>1</c>, <c>2</c>, or <c>3</c>.</param>
    public void RecordTtlt(double ttltMs, string model, int routingLayer)
    {
        _ttltMs.Record(ttltMs, BuildTags(model, routingLayer));
    }

    /// <summary>
    /// Records prompt token count for a single chat turn.
    /// Also increments <c>ai_chat_generated_tokens_total</c> by <paramref name="completionTokens"/>.
    /// </summary>
    /// <param name="promptTokens">Tokens consumed by the prompt (system + history + user message).</param>
    /// <param name="completionTokens">Tokens generated in the completion.</param>
    /// <param name="model">Model deployment name.</param>
    /// <param name="routingLayer">Routing tier: <c>1</c>, <c>2</c>, or <c>3</c>.</param>
    public void RecordTokenCounts(long promptTokens, long completionTokens, string model, int routingLayer)
    {
        var tags = BuildTags(model, routingLayer);
        _promptTokens.Record(promptTokens, tags);
        _generatedTokensTotal.Add(completionTokens, tags);
    }

    /// <summary>
    /// Records the end-to-end routing decision latency.
    /// </summary>
    /// <param name="latencyMs">Total routing wall-clock time in milliseconds.</param>
    /// <param name="model">Model deployment name selected by the router.</param>
    /// <param name="routingLayer">Layer that produced the final decision: <c>1</c>, <c>2</c>, or <c>3</c>.</param>
    public void RecordRoutingLatency(double latencyMs, string model, int routingLayer)
    {
        _routingTotalLatencyMs.Record(latencyMs, BuildTags(model, routingLayer));
    }

    // ── Alert threshold helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the prompt token count exceeds the alert threshold (9000).
    /// Callers should log a warning and surface the value to monitoring when this returns true.
    /// </summary>
    public static bool IsPromptBudgetExceeded(int tokenCount) =>
        tokenCount > PromptBudgetAlertThreshold;

    /// <summary>
    /// Returns <see langword="true"/> when the TTFT value exceeds the alert threshold (1500 ms).
    /// Callers should log a warning when this returns true.
    /// </summary>
    public static bool IsTtftExceeded(double ms) =>
        ms > TtftAlertThresholdMs;

    // ── Internals ─────────────────────────────────────────────────────────────

    private static TagList BuildTags(string model, int routingLayer) =>
        new()
        {
            { "model", model },
            { "routing_layer", routingLayer.ToString() }
        };

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
