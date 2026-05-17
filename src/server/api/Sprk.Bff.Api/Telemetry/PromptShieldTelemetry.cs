using System.Diagnostics.Metrics;
using Sprk.Bff.Api.Services.Ai.Safety;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry metrics for the Prompt Shield safety service (AIPU2-020).
///
/// Meter name: <c>Sprk.Bff.Api.AiSafety</c> — registered in <see cref="Infrastructure.DI.TelemetryModule"/>.
///
/// Emitted instruments:
///   - <c>ai_safety_prompt_shield_blocked_total</c>  Counter  — blocked requests by reason
///   - <c>ai_safety_prompt_shield_latency_ms</c>    Histogram — P50/P95/P99 latency per scan
///
/// Labels on blocked counter:
///   - <c>reason</c>: <c>user_injection</c> | <c>document_injection</c>
///
/// Labels on latency histogram:
///   - <c>outcome</c>: <c>blocked</c> | <c>safe</c> | <c>fail_open</c>
///   - <c>fail_open_cause</c>: <c>timeout</c> | <c>error</c> | <c>-</c> (when not fail-open)
/// </summary>
public sealed class PromptShieldTelemetry : IDisposable
{
    public const string MeterName = "Sprk.Bff.Api.AiSafety";

    private readonly Meter _meter;
    private readonly Counter<long> _blockedTotal;
    private readonly Histogram<double> _latencyMs;

    public PromptShieldTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _blockedTotal = _meter.CreateCounter<long>(
            name: "ai_safety_prompt_shield_blocked_total",
            unit: "{request}",
            description: "Total number of requests blocked by Prompt Shield, by attack type.");

        _latencyMs = _meter.CreateHistogram<double>(
            name: "ai_safety_prompt_shield_latency_ms",
            unit: "ms",
            description: "Wall-clock latency of each Prompt Shield API scan.");
    }

    /// <summary>
    /// Records the outcome of a completed (non-fail-open) scan.
    /// </summary>
    public void RecordScan(PromptShieldResult result, double latencyMs)
    {
        if (result.IsBlocked)
        {
            var reason = result.BlockReason switch
            {
                PromptShieldBlockReason.UserInjection => "user_injection",
                PromptShieldBlockReason.DocumentInjection => "document_injection",
                _ => "unknown"
            };

            _blockedTotal.Add(1,
                new KeyValuePair<string, object?>("reason", reason));

            _latencyMs.Record(latencyMs,
                new KeyValuePair<string, object?>("outcome", "blocked"),
                new KeyValuePair<string, object?>("fail_open_cause", "-"));
        }
        else
        {
            _latencyMs.Record(latencyMs,
                new KeyValuePair<string, object?>("outcome", "safe"),
                new KeyValuePair<string, object?>("fail_open_cause", "-"));
        }
    }

    /// <summary>
    /// Records a fail-open outcome (service unavailable — request allowed through with warning).
    /// </summary>
    /// <param name="cause"><c>timeout</c> or <c>error</c></param>
    /// <param name="latencyMs">Elapsed time before the failure was detected.</param>
    public void RecordFailOpen(string cause, double latencyMs)
    {
        _latencyMs.Record(latencyMs,
            new KeyValuePair<string, object?>("outcome", "fail_open"),
            new KeyValuePair<string, object?>("fail_open_cause", cause));
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
