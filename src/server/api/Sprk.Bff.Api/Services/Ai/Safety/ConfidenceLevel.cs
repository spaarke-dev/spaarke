namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Calibrated confidence level for an AI response, derived from the number of supporting
/// source passages and the groundedness ratio of the response segments.
///
/// Used as part of the <c>safety_annotation</c> SSE event payload so the UI can display
/// a confidence indicator alongside the AI response (FR-406).
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>
    /// The response is well-supported: raw_score >= 0.75 (5+ passages with high groundedness
    /// ratio, or equivalent combination).
    /// </summary>
    High,

    /// <summary>
    /// The response is partially supported: raw_score in [0.40, 0.75).
    /// </summary>
    Medium,

    /// <summary>
    /// The response has limited or no source support: raw_score &lt; 0.40, or
    /// SourcePassageCount == 0 (override regardless of other scores).
    /// </summary>
    Low,
}
