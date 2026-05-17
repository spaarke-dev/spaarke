namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Immutable result produced by <see cref="ICapabilityRouter"/> after each routing pass.
///
/// A result describes the outcome of one routing layer:
///   - Layer 1 (keyword classifier): synchronous, no LLM, &lt;50ms NFR.
///   - Layer 2 (LLM intent): asynchronous, single LLM call.
///   - Layer 3 (fallback): returns a safe default when Layers 1 and 2 are inconclusive.
///
/// Use the static factory methods to construct instances:
///   - <see cref="Confident"/>: Layer resolved with confidence above threshold.
///   - <see cref="Uncertain"/>: Layer could not reach threshold — caller should try Layer 2.
///   - <see cref="Fallback"/>: Used by Layer 3 to signal a safe default was selected.
///
/// ADR-015: this record MUST NOT carry user message content or LLM response text.
///          Only capability names, confidence scores, layer numbers, and latency are stored.
/// </summary>
public sealed record CapabilityRoutingResult
{
    // ── Core properties ───────────────────────────────────────────────────────

    /// <summary>
    /// Whether the routing layer produced a high-confidence decision.
    /// When <c>true</c>, <see cref="SelectedCapabilities"/> is non-empty and callers
    /// MUST NOT escalate to a deeper routing layer.
    /// When <c>false</c>, <see cref="SelectedCapabilities"/> is empty and the caller
    /// SHOULD escalate to the next layer.
    /// </summary>
    public bool IsConfident { get; init; }

    /// <summary>
    /// Names of capabilities selected by this routing layer.
    /// Empty when <see cref="IsConfident"/> is <c>false</c>.
    /// Each name corresponds to a <see cref="CapabilityManifestEntry.CapabilityName"/>.
    /// </summary>
    public string[] SelectedCapabilities { get; init; } = [];

    /// <summary>
    /// Normalised confidence score in the range [0.0, 1.0].
    /// Computed as: topScore / (topScore + secondScore + epsilon).
    /// Values above the configured threshold (default 0.8) result in <see cref="IsConfident"/> = true.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Routing layer that produced this result: 1, 2, or 3.
    /// Layer 0 is never returned (reserved to indicate "not yet routed").
    /// </summary>
    public int Layer { get; init; }

    /// <summary>
    /// Wall-clock duration of the routing operation in milliseconds.
    /// Layer 1 must be under 50ms (NFR-03). Layers 2 and 3 may be higher.
    /// </summary>
    public long LatencyMs { get; init; }

    // ── Static factory methods ────────────────────────────────────────────────

    /// <summary>
    /// Creates a high-confidence routing result for the given capabilities.
    /// Used by any layer when the confidence score exceeds the configured threshold.
    /// </summary>
    /// <param name="selectedCapabilities">Non-empty array of selected capability names.</param>
    /// <param name="confidence">Normalised confidence score (must be &gt; threshold).</param>
    /// <param name="layer">Routing layer that produced this result (1, 2, or 3).</param>
    /// <param name="latencyMs">Elapsed wall-clock time for the routing operation.</param>
    public static CapabilityRoutingResult Confident(
        string[] selectedCapabilities,
        double confidence,
        int layer,
        long latencyMs)
    {
        ArgumentNullException.ThrowIfNull(selectedCapabilities);
        if (selectedCapabilities.Length == 0)
            throw new ArgumentException("selectedCapabilities must not be empty for a Confident result.", nameof(selectedCapabilities));

        return new CapabilityRoutingResult
        {
            IsConfident = true,
            SelectedCapabilities = selectedCapabilities,
            Confidence = confidence,
            Layer = layer,
            LatencyMs = latencyMs
        };
    }

    /// <summary>
    /// Creates an uncertain routing result signalling fall-through to the next layer.
    /// Used by Layer 1 when the top keyword score is below the confidence threshold.
    /// </summary>
    /// <param name="confidence">Best confidence score achieved (below threshold).</param>
    /// <param name="layer">Routing layer that produced this result.</param>
    /// <param name="latencyMs">Elapsed wall-clock time for the routing operation.</param>
    public static CapabilityRoutingResult Uncertain(double confidence, int layer, long latencyMs) =>
        new()
        {
            IsConfident = false,
            SelectedCapabilities = [],
            Confidence = confidence,
            Layer = layer,
            LatencyMs = latencyMs
        };

    /// <summary>
    /// Creates a fallback routing result used by Layer 3 when no layer reached confidence.
    /// The fallback capability (e.g. a default general-purpose capability) is still returned
    /// so that the pipeline never stalls.
    /// </summary>
    /// <param name="fallbackCapabilityNames">Fallback capability names (may be empty for a no-op).</param>
    /// <param name="latencyMs">Elapsed wall-clock time for the routing operation.</param>
    public static CapabilityRoutingResult Fallback(string[] fallbackCapabilityNames, long latencyMs) =>
        new()
        {
            IsConfident = false,
            SelectedCapabilities = fallbackCapabilityNames,
            Confidence = 0.0,
            Layer = 3,
            LatencyMs = latencyMs
        };
}
