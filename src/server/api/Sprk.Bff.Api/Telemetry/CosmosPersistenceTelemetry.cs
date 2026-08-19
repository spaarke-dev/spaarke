using System.Diagnostics.Metrics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// OpenTelemetry counter for Cosmos DB persistence WRITE FAILURES across the AI-persistence layer
/// (sessions, memory-items, memory/pins, audit, prompts, feedback).
///
/// <para><b>Why this exists</b> (spaarkeai-compose-r7 R-5, 2026-08-18): Cosmos write failures on the
/// warm tier are deliberately swallowed at Warning so they never break the streaming SSE response
/// (ADR-015 D-06). That is correct for a SINGLE transient failure — but it also meant a TOTAL,
/// persistent write outage (the <c>ttl: null</c> → HTTP 400 regression that froze History + memory
/// for 11 days) was invisible: no request failed, and the only signal was a Warning log nobody read.
/// This counter turns each swallowed failure into an ALERTABLE metric so a silent total outage
/// cannot recur unnoticed. Alert suggestion: <c>cosmos.write_failures</c> &gt; 0 sustained.</para>
///
/// <para>Wired into OpenTelemetry in <see cref="Infrastructure.DI.TelemetryModule"/> via
/// <c>metrics.AddMeter(<see cref="MeterName"/>)</c>, following the <c>Sprk.Bff.Api.&lt;Feature&gt;</c>
/// meter convention. Static (like <c>PinnedMemoryEndpoints</c>' meter) so it is callable from the
/// deep swallow points without threading a DI singleton through the persistence services.</para>
///
/// <para>App Insights / Kusto:
/// <c>customMetrics | where name == "cosmos.write_failures" | summarize sum(value) by bin(timestamp, 5m), customDimensions["container"]</c></para>
/// </summary>
public static class CosmosPersistenceTelemetry
{
    /// <summary>Meter name for OpenTelemetry registration. Stable downstream contract.</summary>
    public const string MeterName = "Sprk.Bff.Api.CosmosPersistence";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> WriteFailures = Meter.CreateCounter<long>(
        name: "cosmos.write_failures",
        unit: "{failure}",
        description: "Count of swallowed Cosmos DB write failures on the AI-persistence layer, tagged by container. A sustained nonzero value indicates a persistent write outage (writes silently not landing).");

    /// <summary>
    /// Record one swallowed Cosmos write failure. Called from the catch blocks that log-and-swallow a
    /// Cosmos upsert exception so the failure surfaces as an alertable metric even though the request
    /// itself continues.
    /// </summary>
    /// <param name="container">
    /// The Cosmos container the failed write targeted — a BOUNDED, low-cardinality dimension
    /// (e.g. <c>sessions</c>, <c>memory-items</c>, <c>memory</c>). Never pass ids or free-form text.
    /// </param>
    public static void RecordWriteFailure(string container)
    {
        WriteFailures.Add(1, new KeyValuePair<string, object?>("container", container));
    }
}
