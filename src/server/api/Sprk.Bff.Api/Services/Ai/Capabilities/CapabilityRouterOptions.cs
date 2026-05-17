namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Configuration options for <see cref="CapabilityRouter"/> Layer 1.
///
/// Bound from the <c>Capabilities:Router</c> configuration section:
/// <code>
/// {
///   "Capabilities": {
///     "Router": {
///       "ConfidenceThreshold": 0.8,
///       "PlaybookBiasThreshold": 0.65
///     }
///   }
/// }
/// </code>
/// </summary>
public sealed class CapabilityRouterOptions
{
    /// <summary>Configuration section name used for <see cref="Microsoft.Extensions.Options.IOptions{T}"/> binding.</summary>
    public const string SectionName = "Capabilities:Router";

    /// <summary>
    /// Minimum normalised confidence score required for Layer 1 to return a
    /// <see cref="CapabilityRoutingResult.Confident"/> result without escalating to Layer 2.
    ///
    /// Formula: topScore / (topScore + secondScore + epsilon).
    /// Values close to 1.0 demand a single dominant capability; values close to 0.5 allow ties.
    /// Default: 0.8 (handles the majority of unambiguous turns).
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// Lower confidence threshold applied when the active playbook session is set
    /// and the top-scoring capability belongs to that playbook.
    ///
    /// Rationale: within a known playbook context, keyword ambiguity is less likely
    /// because only the playbook's capabilities are in scope. A lower threshold avoids
    /// unnecessary LLM calls for single-playbook sessions.
    ///
    /// Default: 0.65. Must be less than <see cref="ConfidenceThreshold"/>.
    /// </summary>
    public double PlaybookBiasThreshold { get; set; } = 0.65;
}
