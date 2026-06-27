using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Configuration;

/// <summary>
/// Configuration for the file-aware playbook-candidate selector
/// (chat-routing-redesign-r1 FR-47 + FR-48). Bound to the
/// <c>PlaybookSelector</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-018 (Typed options) the selection thresholds used by
/// <see cref="Sprk.Bff.Api.Services.Ai.Chat.PlaybookCandidateSelector"/>
/// live in a typed options class rather than as scattered <c>const</c>
/// literals so they can be tuned per-environment without a code change
/// (e.g., relaxed thresholds in <c>bff-dev</c> for evaluation, tightened
/// thresholds in production after telemetry-driven calibration).
/// </para>
/// <para>
/// <b>FR-47 thresholds</b> (chat-routing-redesign-r1 spec §"Phase 5+7 Revised Scope"):
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="ConfidenceThreshold"/> = <c>0.85</c> — minimum
///       similarity for the top-1 candidate to be considered a
///       <b>high-confidence single</b>. When the top-1 is at or above this
///       threshold AND the top-2 is meaningfully below (see
///       <see cref="ConfidenceDeltaMargin"/>), the selector does NOT
///       request a rerank.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="SecondaryThreshold"/> = <c>0.80</c> — minimum
///       similarity for a candidate to appear in the returned top-N list
///       at all. Candidates below this threshold are pruned regardless of
///       rank.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="ConfidenceDeltaMargin"/> = <c>0.05</c> — minimum gap
///       between top-1 and top-2 for the result to be classified
///       unambiguous. When <c>top1 − top2 ≤ margin</c> the selector
///       flags the result as ambiguous and recommends a downstream LLM
///       rerank (task 111R).
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>FR-48 invariant</b>: the selector NEVER auto-executes a playbook —
/// the configured thresholds influence only WHICH candidates are surfaced
/// to the user and whether a rerank is recommended. The downstream
/// <c>playbook_options</c> SSE event (task 117a) is the only path to user
/// confirmation; the user must click to invoke. This is enforced by the
/// shape of the result (no auto-execute flag) — these options never carry
/// an "execute" toggle.
/// </para>
/// </remarks>
public class PlaybookSelectorOptions
{
    /// <summary>
    /// Configuration section name. Used by
    /// <c>configuration.GetSection(PlaybookSelectorOptions.SectionName)</c>.
    /// </summary>
    public const string SectionName = "PlaybookSelector";

    /// <summary>
    /// Similarity threshold above which a single top-1 candidate is
    /// treated as a high-confidence match (no rerank required when the
    /// gap to top-2 also exceeds <see cref="ConfidenceDeltaMargin"/>).
    /// Spec FR-47 default: <c>0.85</c>.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "ConfidenceThreshold must be between 0.0 and 1.0.")]
    public double ConfidenceThreshold { get; set; } = 0.85;

    /// <summary>
    /// Minimum similarity threshold for a candidate to be returned in the
    /// top-N list. Candidates below this threshold are pruned. Spec FR-47
    /// default: <c>0.80</c>.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "SecondaryThreshold must be between 0.0 and 1.0.")]
    public double SecondaryThreshold { get; set; } = 0.80;

    /// <summary>
    /// Minimum gap between top-1 and top-2 confidence below which the
    /// result is classified ambiguous and a downstream LLM rerank is
    /// recommended (task 111R). Spec FR-47 default: <c>0.05</c>.
    /// </summary>
    [Range(0.0, 1.0, ErrorMessage = "ConfidenceDeltaMargin must be between 0.0 and 1.0.")]
    public double ConfidenceDeltaMargin { get; set; } = 0.05;

    /// <summary>
    /// Maximum number of candidates returned in the top-N list. Spec
    /// FR-47 ("Always show top 3"): <c>3</c>. Exposed as a tunable so
    /// per-environment evaluation can experiment with N without a code
    /// change.
    /// </summary>
    [Range(1, 10, ErrorMessage = "MaxCandidates must be between 1 and 10.")]
    public int MaxCandidates { get; set; } = 3;
}
