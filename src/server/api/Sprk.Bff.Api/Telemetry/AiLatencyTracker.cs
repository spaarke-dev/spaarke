using System.Diagnostics;

namespace Sprk.Bff.Api.Telemetry;

/// <summary>
/// Scoped per-request stopwatch wrapper that records AI chat streaming latency metrics
/// via <see cref="AiLatencyTelemetry"/> (AIPU2-066).
///
/// Lifecycle (one instance per HTTP request — registered as <c>AddScoped</c>):
///   1. <see cref="StartRequest"/>      — called when the SSE stream begins (captures t0).
///   2. <see cref="RecordFirstToken"/>  — called when the first SSE token byte is written.
///   3. <see cref="RecordToken"/>       — called for each subsequent token (TBT sampling).
///   4. <see cref="CompleteRequest"/>   — called when the SSE done event is emitted;
///                                        flushes all histograms to <see cref="AiLatencyTelemetry"/>.
///
/// Usage in <c>ChatEndpoints</c> (streaming path):
/// <code>
/// tracker.StartRequest();
/// await foreach (var chunk in streamingResult)
/// {
///     if (!firstToken) { tracker.RecordFirstToken(); firstToken = true; }
///     else             { tracker.RecordToken(); }
///     await WriteChunkAsync(chunk);
/// }
/// tracker.CompleteRequest(promptTokens, completionTokens);
/// </code>
/// </summary>
public sealed class AiLatencyTracker
{
    private readonly AiLatencyTelemetry _telemetry;

    // Per-request state
    private readonly Stopwatch _requestClock = new();
    private readonly Stopwatch _tokenClock = new();

    private string _model = "unknown";
    private int _routingLayer = 1;

    private double _ttftMs;
    private bool _firstTokenRecorded;

    public AiLatencyTracker(AiLatencyTelemetry telemetry)
    {
        _telemetry = telemetry;
    }

    /// <summary>
    /// Marks the start of the streaming request.
    /// Must be called before <see cref="RecordFirstToken"/>.
    /// </summary>
    /// <param name="model">Model deployment name selected by the router.</param>
    /// <param name="routingLayer">Routing tier that produced the model selection (1, 2, or 3).</param>
    public void StartRequest(string model = "unknown", int routingLayer = 1)
    {
        _model = model;
        _routingLayer = routingLayer;
        _firstTokenRecorded = false;
        _ttftMs = 0;
        _requestClock.Restart();
        _tokenClock.Restart();
    }

    /// <summary>
    /// Records the Time to First Token.
    /// Call exactly once when the first SSE token byte is written to the response stream.
    /// Resets the inter-token clock for subsequent <see cref="RecordToken"/> calls.
    /// </summary>
    public void RecordFirstToken()
    {
        _ttftMs = _requestClock.Elapsed.TotalMilliseconds;
        _firstTokenRecorded = true;
        _tokenClock.Restart();
    }

    /// <summary>
    /// Records the Time Between Tokens for a single subsequent token.
    /// Call after <see cref="RecordFirstToken"/> for each additional token written to the stream.
    /// Resets the inter-token clock so the next call measures the gap from this token.
    /// </summary>
    public void RecordToken()
    {
        if (!_firstTokenRecorded)
        {
            // Guard: if caller missed RecordFirstToken, treat this as the first token.
            RecordFirstToken();
            return;
        }

        var tbtMs = _tokenClock.Elapsed.TotalMilliseconds;
        _tokenClock.Restart();
        _telemetry.RecordTbt(tbtMs, _model, _routingLayer);
    }

    /// <summary>
    /// Flushes all accumulated measurements to <see cref="AiLatencyTelemetry"/> when the
    /// SSE done event is emitted.  Safe to call even if <see cref="StartRequest"/> was never
    /// called (no-ops in that case to avoid division-by-zero or negative elapsed values).
    /// </summary>
    /// <param name="promptTokens">Tokens consumed by the prompt for this turn.</param>
    /// <param name="completionTokens">Tokens generated in the completion.</param>
    public void CompleteRequest(long promptTokens, long completionTokens)
    {
        if (!_requestClock.IsRunning && _requestClock.ElapsedTicks == 0)
        {
            // StartRequest was never called — nothing to record.
            return;
        }

        _requestClock.Stop();
        var ttltMs = _requestClock.Elapsed.TotalMilliseconds;

        // TTFT — may be zero if no tokens were emitted (error path).
        if (_firstTokenRecorded)
        {
            _telemetry.RecordTtft(_ttftMs, _model, _routingLayer);
        }

        // TTLT — always record to capture full request duration.
        _telemetry.RecordTtlt(ttltMs, _model, _routingLayer);

        // Token counts.
        _telemetry.RecordTokenCounts(promptTokens, completionTokens, _model, _routingLayer);
    }
}
