using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Services.Ai.EventRules;

/// <summary>
/// Metrics for the Event entry path (NFR-09: the per-user daily budget is
/// "enforced AND telemetered"; ADR-016 cost visibility). OpenTelemetry-compatible;
/// pattern mirrors <see cref="Sprk.Bff.Api.Telemetry.R5SummarizeTelemetry"/>.
///
/// Locked instrument schema:
///
/// | Instrument                    | Type          | Dimensions (BOUNDED)      |
/// |-------------------------------|---------------|---------------------------|
/// | eventpath.execution           | Counter&lt;long&gt; | event, ucid, outcome      |
/// | eventpath.bound_denial        | Counter&lt;long&gt; | event, reason             |
///
/// Bounded dimensions: <c>outcome</c> ∈ { success, failed, gated } (gated = M4
/// confirmation suspended the member chain); <c>reason</c> = the closed
/// <see cref="EventNoticeReasons"/> vocabulary. <c>ucid</c> is bounded by the
/// closed catalog (§3 UC vocabulary). High-cardinality identifiers (user oid,
/// session id, file ids) are EXCLUDED per ADR-014/ADR-015 cardinality discipline
/// — the per-user enforcement decision is logged (identifiers only) not tagged.
/// </summary>
public sealed class EventRulesTelemetry : IDisposable
{
    /// <summary>Meter name for OpenTelemetry registration. Stable downstream contract.</summary>
    public const string MeterName = "Sprk.Bff.Api.EventRules";

    private static readonly HashSet<string> ValidOutcomes = new(StringComparer.Ordinal)
    {
        "success",
        "failed",
        "gated",
    };

    private static readonly HashSet<string> ValidReasons = new(StringComparer.Ordinal)
    {
        EventNoticeReasons.Superseded,
        EventNoticeReasons.OptedOut,
        EventNoticeReasons.DailyCap,
        EventNoticeReasons.NoAttachments,
        EventNoticeReasons.NoRule,
    };

    private readonly Meter _meter;
    private readonly Counter<long> _executionCounter;
    private readonly Counter<long> _boundDenialCounter;

    public EventRulesTelemetry()
    {
        _meter = new Meter(MeterName, "1.0.0");
        _executionCounter = _meter.CreateCounter<long>(
            name: "eventpath.execution",
            unit: "{execution}",
            description: "Event-path capability-member executions (the unit the NFR-09 daily budget counts).");
        _boundDenialCounter = _meter.CreateCounter<long>(
            name: "eventpath.bound_denial",
            unit: "{denial}",
            description: "Event-rule firings denied/deferred by a FR-P1-03 bound or precondition.");
    }

    /// <summary>Records one member execution. <paramref name="outcome"/> ∈ {success, failed, gated}.</summary>
    public void RecordExecution(string eventName, string? ucid, string outcome)
    {
        if (!ValidOutcomes.Contains(outcome))
        {
            throw new ArgumentException(
                $"Invalid outcome '{outcome}' — must be one of: {string.Join(", ", ValidOutcomes)}.",
                nameof(outcome));
        }

        _executionCounter.Add(1,
            new KeyValuePair<string, object?>("event", eventName),
            new KeyValuePair<string, object?>("ucid", ucid ?? "unknown"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>Records a bound/precondition denial. <paramref name="reason"/> = <see cref="EventNoticeReasons"/> value.</summary>
    public void RecordBoundDenial(string eventName, string reason)
    {
        if (!ValidReasons.Contains(reason))
        {
            throw new ArgumentException(
                $"Invalid reason '{reason}' — must be one of: {string.Join(", ", ValidReasons)}.",
                nameof(reason));
        }

        _boundDenialCounter.Add(1,
            new KeyValuePair<string, object?>("event", eventName),
            new KeyValuePair<string, object?>("reason", reason));
    }

    public void Dispose() => _meter.Dispose();
}
