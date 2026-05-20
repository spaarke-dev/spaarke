using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry metrics for the Groundedness Check safety service (AIPU2-021).
///
/// Meter name: <c>Sprk.Bff.Api.AiSafety</c> — shared with <see cref="PromptShieldTelemetry"/>
/// and registered in <see cref="Infrastructure.DI.TelemetryModule"/>.
///
/// Emitted instruments:
///   - <c>ai_safety_groundedness_ungrounded_total</c>  Counter  — LLM responses with at least
///     one ungrounded segment detected.
///   - <c>ai_safety_groundedness_latency_ms</c>       Histogram — P50/P95/P99 latency per call.
///
/// Labels on latency histogram:
///   - <c>outcome</c>: <c>grounded</c> | <c>ungrounded</c> | <c>skipped</c> | <c>fail_open</c>
/// </summary>
public sealed class GroundednessCheckTelemetry : IDisposable
{
    /// <summary>Meter name shared across all AI Safety telemetry classes.</summary>
    public const string MeterName = PromptShieldTelemetry.MeterName;

    private readonly Meter _meter;
    private readonly Counter<long> _ungroundedTotal;
    private readonly Histogram<double> _latencyMs;

    public GroundednessCheckTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _ungroundedTotal = _meter.CreateCounter<long>(
            name: "ai_safety_groundedness_ungrounded_total",
            unit: "{response}",
            description: "Total number of LLM responses where at least one ungrounded segment was detected.");

        _latencyMs = _meter.CreateHistogram<double>(
            name: "ai_safety_groundedness_latency_ms",
            unit: "ms",
            description: "Wall-clock latency of each Azure AI Content Safety Groundedness Detection API call.");
    }

    /// <summary>
    /// Records the outcome of a completed groundedness check.
    /// </summary>
    /// <param name="isGrounded">Whether the response was grounded.</param>
    /// <param name="ungroundedSegmentCount">
    /// Number of ungrounded segments detected. Zero when grounded or skipped.
    /// ADR-015: MUST NOT be used to log segment text.
    /// </param>
    /// <param name="latencyMs">Round-trip latency in milliseconds.</param>
    /// <param name="outcome">
    /// <c>grounded</c>, <c>ungrounded</c>, <c>skipped</c>, or <c>fail_open</c>.
    /// </param>
    public void RecordCheck(bool isGrounded, int ungroundedSegmentCount, double latencyMs, string outcome)
    {
        if (!isGrounded && ungroundedSegmentCount > 0)
        {
            _ungroundedTotal.Add(1);
        }

        _latencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>Outcome label for a fully grounded response.</summary>
    public const string OutcomeGrounded = "grounded";

    /// <summary>Outcome label for a response with one or more ungrounded segments.</summary>
    public const string OutcomeUngrounded = "ungrounded";

    /// <summary>Outcome label when the check is skipped (empty source documents).</summary>
    public const string OutcomeSkipped = "skipped";

    /// <summary>Outcome label when the service is unavailable and fail-open is applied.</summary>
    public const string OutcomeFailOpen = "fail_open";

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
