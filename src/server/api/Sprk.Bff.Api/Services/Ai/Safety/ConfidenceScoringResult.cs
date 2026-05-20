namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Outcome of a confidence scoring computation for a single AI response.
///
/// Included in the <c>safety_annotation</c> SSE event payload as the <c>confidence</c>
/// sub-object so the UI can display a confidence indicator alongside the AI response (FR-406).
/// </summary>
/// <param name="Level">
/// Categorical confidence level: High, Medium, or Low.
/// Directly drives the UI confidence badge colour.
/// </param>
/// <param name="Score">
/// Raw composite score in [0.0, 1.0].
/// Computed as <c>(groundedness_ratio * 0.6) + (source_score * 0.4)</c>.
/// Useful for logging and telemetry; the UI renders <see cref="Level"/>, not the raw score.
/// </param>
/// <param name="Rationale">
/// Non-empty, human-readable explanation of the inputs that drove the level assignment.
/// Included in the SSE event so the client can surface it in a tooltip or audit log.
/// ADR-015: must not contain raw AI response text or source passage content — counts only.
/// </param>
public sealed record ConfidenceScoringResult(
    ConfidenceLevel Level,
    float Score,
    string Rationale);
