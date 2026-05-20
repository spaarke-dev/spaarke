using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry metrics for SSE event payload validation (AIPU2-026).
///
/// Meter name: <c>Sprk.Bff.Api.AiSafety</c> — shared with
/// <see cref="PromptShieldTelemetry"/> and registered in
/// <see cref="Infrastructure.DI.TelemetryModule"/>.
///
/// Emitted instruments:
///   - <c>ai_sse_validation_failures_total</c>  Counter — schema violations detected,
///     broken down by SSE event type.
///
/// Labels on the counter:
///   - <c>event_type</c>: the R2 event type string (e.g. <c>workspace_widget</c>).
/// </summary>
public sealed class SseValidationTelemetry : IDisposable
{
    /// <summary>Meter name shared across all AI Safety telemetry classes.</summary>
    public const string MeterName = PromptShieldTelemetry.MeterName;

    private readonly Meter _meter;
    private readonly Counter<long> _validationFailuresTotal;

    public SseValidationTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _validationFailuresTotal = _meter.CreateCounter<long>(
            name: "ai_sse_validation_failures_total",
            unit: "{event}",
            description: "Total number of SSE tool output events that failed schema validation, by event type.");
    }

    /// <summary>
    /// Increments the validation failure counter for the given event type.
    /// </summary>
    /// <param name="eventType">
    /// The SSE event type string (e.g. <c>workspace_widget</c>).
    /// </param>
    public void RecordValidationFailure(string eventType)
    {
        _validationFailuresTotal.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType));
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
