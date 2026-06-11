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

    /// <summary>
    /// R6 task 042 (FR-30): Dataverse <c>sprk_analysisplaybook</c> ID of the playbook
    /// associated with the SINGLE confident capability, when one can be unambiguously
    /// identified.
    ///
    /// Populated by Layers 1 and 2 when the resolved <see cref="SelectedCapabilities"/>
    /// contains exactly one entry AND that entry's manifest <c>PlaybookId</c> is non-null.
    /// Left <c>null</c> when:
    /// <list type="bullet">
    ///   <item>Multiple capabilities tied at the top score (Layer 1) — the playbook is
    ///         ambiguous; consumers fall through to the conversational default.</item>
    ///   <item>The winning capability has no <c>PlaybookId</c> (e.g., a global
    ///         capability that does not bind to a playbook).</item>
    ///   <item>Uncertain / Layer 3 fallback results.</item>
    /// </list>
    ///
    /// Consumed by <c>SprkChatAgentFactory.CreateAgentAsync</c> to resolve the playbook's
    /// terminal node <c>destination</c> (per <see cref="Models.Ai.NodeRoutingConfig"/>)
    /// and emit ONE rendering per intent (FR-30 dedup). ADR-015 compliant — a
    /// deterministic identifier; never user message content.
    /// </summary>
    public Guid? SelectedPlaybookId { get; init; }

    /// <summary>Creates a high-confidence routing result for the given capabilities.</summary>
    /// <param name="selectedCapabilities">Selected capability names (non-empty).</param>
    /// <param name="confidence">Normalised confidence in [0, 1].</param>
    /// <param name="layer">Originating routing layer (1 or 2).</param>
    /// <param name="latencyMs">Wall-clock latency in milliseconds.</param>
    /// <param name="selectedPlaybookId">
    /// R6 task 042 (FR-30): Optional Dataverse playbook ID associated with the SINGLE
    /// confident capability. Pass <c>null</c> when ambiguous (multiple capabilities tied)
    /// or when the capability has no playbook binding. See
    /// <see cref="SelectedPlaybookId"/> for the full contract.
    /// </param>
    public static CapabilityRoutingResult Confident(
        string[] selectedCapabilities,
        double confidence,
        int layer,
        long latencyMs,
        Guid? selectedPlaybookId = null)
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
            SelectedPlaybookId = selectedPlaybookId,
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
