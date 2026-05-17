namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Configuration options for <see cref="CapabilityRouter"/> Layers 1, 2, and 3.
///
/// Bound from the <c>Capabilities:Router</c> configuration section.
/// </summary>
public sealed class CapabilityRouterOptions
{
    /// <summary>Configuration section name used for <see cref="Microsoft.Extensions.Options.IOptions{T}"/> binding.</summary>
    public const string SectionName = "Capabilities:Router";

    /// <summary>
    /// Minimum normalised confidence score required for Layer 1 to return a
    /// <see cref="CapabilityRoutingResult.Confident"/> result without escalating to Layer 2.
    ///
    /// Default: 0.8.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// Lower confidence threshold applied when the active playbook session is set
    /// and the top-scoring capability belongs to that playbook.
    ///
    /// Default: 0.65. Must be less than <see cref="ConfidenceThreshold"/>.
    /// </summary>
    public double PlaybookBiasThreshold { get; set; } = 0.65;

    /// <summary>
    /// Maximum number of tool names included in the Layer 3 broad superset.
    /// Default: 12.
    /// </summary>
    public int MaxSupersetTools { get; set; } = 12;

    /// <summary>
    /// Optional ID of the default playbook to restrict the Layer 3 superset.
    /// When set, only capabilities with a non-null PlaybookId contribute.
    /// Null (default) = union of all enabled capabilities.
    /// </summary>
    public string? DefaultPlaybookId { get; set; }

    /// <summary>
    /// Hard-coded general-purpose tool set used as a fallback when the manifest
    /// contains no tools and no DefaultPlaybookId is configured.
    /// </summary>
    public static readonly string[] GeneralSupersetFallbackTools =
    [
        "GenerateSummary",
        "GetKnowledgeSource",
        "QueryEntities",
        "RefineText",
        "SearchDiscovery",
        "SearchDocuments",
    ];

    /// <summary>
    /// Configuration for the Layer 2 GPT-4o-mini intent classifier (AIPU2-013).
    /// When <see cref="Layer2Options.Enabled"/> is <c>false</c>, Layer 2 is skipped
    /// entirely and uncertain turns fall through directly to Layer 3.
    /// </summary>
    public Layer2Options Layer2 { get; set; } = new();
}

/// <summary>
/// Layer 2 GPT-4o-mini intent classifier options.
///
/// Nested under <c>Capabilities:Router:Layer2</c> in configuration.
/// </summary>
public sealed class Layer2Options
{
    /// <summary>
    /// Whether Layer 2 classification is active.
    /// When <c>false</c>, all uncertain turns from Layer 1 fall through to Layer 3.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum wall-clock time in milliseconds for a single Layer 2 LLM call.
    /// Default: 500.
    /// </summary>
    public int TimeoutMs { get; set; } = 500;

    /// <summary>
    /// Maximum number of capabilities included in the classification prompt.
    /// Default: 20.
    /// </summary>
    public int MaxCandidates { get; set; } = 20;
}
