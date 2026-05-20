namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Immutable result produced by <see cref="ICapabilityRouter"/> after each routing pass.
///
/// ADR-015: this record MUST NOT carry user message content or LLM response text.
/// </summary>
public sealed record CapabilityRoutingResult
{
    /// <summary>Whether the routing layer produced a high-confidence decision.</summary>
    public bool IsConfident { get; init; }

    /// <summary>Names of capabilities selected by this routing layer.</summary>
    public string[] SelectedCapabilities { get; init; } = [];

    /// <summary>Normalised confidence score in the range [0.0, 1.0].</summary>
    public double Confidence { get; init; }

    /// <summary>Routing layer that produced this result: 1, 2, or 3.</summary>
    public int Layer { get; init; }

    /// <summary>Wall-clock duration of the routing operation in milliseconds.</summary>
    public long LatencyMs { get; init; }

    /// <summary>
    /// Resolved tool names to activate for this routing result.
    ///
    /// Populated by Layer 3 with the broad superset tool list (union of all enabled
    /// capabilities' tools, deduplicated and capped at
    /// <see cref="CapabilityRouterOptions.MaxSupersetTools"/>).
    ///
    /// Layers 1 and 2 leave this empty.
    /// </summary>
    public string[] SelectedToolNames { get; init; } = [];

    /// <summary>Creates a high-confidence routing result for the given capabilities.</summary>
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
            LatencyMs = latencyMs,
        };
    }

    /// <summary>Creates an uncertain routing result signalling fall-through to the next layer.</summary>
    public static CapabilityRoutingResult Uncertain(double confidence, int layer, long latencyMs) =>
        new()
        {
            IsConfident = false,
            SelectedCapabilities = [],
            Confidence = confidence,
            Layer = layer,
            LatencyMs = latencyMs,
        };

    /// <summary>
    /// Creates a fallback routing result used by Layer 3 when no layer reached confidence.
    /// <paramref name="selectedToolNames"/> carries the broad superset for the orchestrator.
    /// </summary>
    public static CapabilityRoutingResult Fallback(
        string[] fallbackCapabilityNames,
        string[] selectedToolNames,
        long latencyMs) =>
        new()
        {
            IsConfident = false,
            SelectedCapabilities = fallbackCapabilityNames,
            SelectedToolNames = selectedToolNames,
            Confidence = 0.0,
            Layer = 3,
            LatencyMs = latencyMs,
        };
}
