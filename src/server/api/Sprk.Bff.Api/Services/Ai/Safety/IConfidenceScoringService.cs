namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Computes a calibrated confidence level (High / Medium / Low) for each AI response based on
/// the number of supporting RAG source passages and the groundedness ratio returned by the
/// groundedness check.
///
/// Implementation contract:
///   - MUST be synchronous (no I/O, pure computation). Callers rely on this being cheap enough
///     to call inline in the SSE event-emission path.
///   - MUST NOT log raw AI response text or source passage content (ADR-015).
///   - MUST return <see cref="ConfidenceLevel.Low"/> when
///     <see cref="ConfidenceScoringRequest.SourcePassageCount"/> is zero, regardless of
///     groundedness inputs.
///   - MUST populate <see cref="ConfidenceScoringResult.Rationale"/> with a non-empty string
///     that describes the inputs (counts only, not text) that drove the decision.
///
/// Lifetime: Singleton — stateless pure computation, safe for concurrent use.
/// </summary>
public interface IConfidenceScoringService
{
    /// <summary>
    /// Computes the confidence level for the AI response described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">
    /// Inputs: source passage count, groundedness result (optional), and optional metadata.
    /// </param>
    /// <returns>
    /// A <see cref="ConfidenceScoringResult"/> with the level, raw score, and rationale string.
    /// </returns>
    ConfidenceScoringResult Score(ConfidenceScoringRequest request);
}
