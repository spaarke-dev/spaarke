using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry metrics for the cross-matter conversation safety feature (AIPU2-028, FR-408).
///
/// Meter name: <c>Sprk.Bff.Api.AiSafety</c> — shared with
/// <see cref="PromptShieldTelemetry"/> and registered in
/// <see cref="Infrastructure.DI.TelemetryModule"/>.
///
/// Emitted instruments:
///   - <c>ai_safety_cross_matter_pivot_total</c>         Counter — matter pivots detected
///   - <c>ai_safety_cross_matter_content_stripped_total</c> Counter — retrieval messages stripped
///
/// ADR-015: counters carry only identifiers (previous/new matter IDs are NOT labels to avoid
/// PII leakage into metric label cardinality).  Labels are outcome-only.
/// </summary>
public sealed class CrossMatterSafetyTelemetry : IDisposable
{
    /// <summary>Shared safety meter name (also used by <see cref="PromptShieldTelemetry"/>).</summary>
    public const string MeterName = PromptShieldTelemetry.MeterName;

    private readonly Meter _meter;
    private readonly Counter<long> _pivotTotal;
    private readonly Counter<long> _contentStrippedTotal;

    public CrossMatterSafetyTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _pivotTotal = _meter.CreateCounter<long>(
            name: "ai_safety_cross_matter_pivot_total",
            unit: "{event}",
            description: "Total number of matter context pivots detected within a conversation session.");

        _contentStrippedTotal = _meter.CreateCounter<long>(
            name: "ai_safety_cross_matter_content_stripped_total",
            unit: "{message}",
            description: "Total number of retrieval tool_result messages stripped on a matter pivot.");
    }

    /// <summary>
    /// Records a detected matter pivot.
    /// </summary>
    /// <param name="historyWasModified">
    /// <c>true</c> when at least one retrieval message was stripped; <c>false</c> when the
    /// history contained no retrieval messages (pivot detected but nothing to strip).
    /// </param>
    public void RecordPivot(bool historyWasModified)
    {
        var outcome = historyWasModified ? "content_stripped" : "no_content";
        _pivotTotal.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>
    /// Records the count of retrieval messages stripped during a pivot sanitization.
    /// </summary>
    /// <param name="count">Number of messages replaced with the privacy placeholder.</param>
    public void RecordContentStripped(int count)
    {
        if (count > 0)
        {
            _contentStrippedTotal.Add(count);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _meter.Dispose();
}
