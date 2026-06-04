namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Computes a calibrated confidence level (High / Medium / Low) for each AI response.
///
/// Scoring formula (FR-406):
///   1. groundedness_ratio  = (total_segments - ungrounded_count) / max(total_segments, 1)
///      When no groundedness result is available, groundedness_ratio = 1.0 (optimistic heuristic).
///   2. source_score        = min(SourcePassageCount / 5.0, 1.0)   (5+ passages = full score)
///   3. raw_score           = (groundedness_ratio * 0.6) + (source_score * 0.4)
///   4. Level mapping:
///        raw_score >= 0.75  → High
///        0.40 <= score < 0.75 → Medium
///        score < 0.40       → Low
///   5. Override: SourcePassageCount == 0 always → Low (regardless of other scores)
///
/// This is pure computation — no I/O, no async. Safe to call inline in the SSE emission path.
///
/// ADR-015 compliance: only counts (segment count, passage count, character count) appear in
/// the rationale string. Raw text (response body, source passages) is never held or logged.
///
/// Lifetime: Singleton — registered in <see cref="Infrastructure.DI.AiSafetyModule"/>.
/// </summary>
public sealed class ConfidenceScoringService : IConfidenceScoringService
{
    // -------------------------------------------------------------------------
    // Scoring constants (FR-406)
    // -------------------------------------------------------------------------

    /// <summary>Number of source passages that saturates the source_score component.</summary>
    private const double MaxSourcePassagesForFullScore = 5.0;

    /// <summary>Weight of the groundedness_ratio component in the composite score.</summary>
    private const double GroundednessWeight = 0.6;

    /// <summary>Weight of the source_score component in the composite score.</summary>
    private const double SourceWeight = 0.4;

    /// <summary>Minimum raw_score for the High band.</summary>
    private const double HighThreshold = 0.75;

    /// <summary>Minimum raw_score for the Medium band (below this → Low).</summary>
    private const double MediumThreshold = 0.40;

    // -------------------------------------------------------------------------
    // IConfidenceScoringService
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public ConfidenceScoringResult Score(ConfidenceScoringRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Override: no sources → always Low, skip further computation.
        if (request.SourcePassageCount == 0)
        {
            return new ConfidenceScoringResult(
                Level: ConfidenceLevel.Low,
                Score: 0f,
                Rationale: BuildRationale(
                    sourcePassageCount: 0,
                    groundednessRatio: null,
                    ungroundedCount: 0,
                    totalSegments: 0,
                    sourceScore: 0.0,
                    rawScore: 0.0,
                    overrideReason: "No source passages were retrieved; confidence is Low regardless of other factors."));
        }

        // Compute groundedness_ratio from groundedness result (if available).
        double groundednessRatio;
        int totalSegments;
        int ungroundedCount;

        if (request.GroundednessResult is not null)
        {
            ungroundedCount = request.GroundednessResult.UngroundedSegments.Count;
            totalSegments = EstimateTotalSegments(request.GroundednessResult);
            groundednessRatio = (double)(totalSegments - ungroundedCount) / Math.Max(totalSegments, 1);
        }
        else
        {
            // Fallback heuristic: no groundedness data → assume optimistic ratio,
            // but discount the source_score-only path slightly below full confidence.
            groundednessRatio = 1.0;
            totalSegments = 0;
            ungroundedCount = 0;
        }

        // source_score: capped at 1.0 once 5+ passages are present.
        var sourceScore = Math.Min(request.SourcePassageCount / MaxSourcePassagesForFullScore, 1.0);

        // Composite raw_score.
        var rawScore = (groundednessRatio * GroundednessWeight) + (sourceScore * SourceWeight);

        // Clamp to [0, 1] for safety (floating-point shouldn't breach bounds, but be explicit).
        rawScore = Math.Clamp(rawScore, 0.0, 1.0);

        var level = MapToLevel(rawScore);

        return new ConfidenceScoringResult(
            Level: level,
            Score: (float)rawScore,
            Rationale: BuildRationale(
                sourcePassageCount: request.SourcePassageCount,
                groundednessRatio: request.GroundednessResult is not null ? groundednessRatio : null,
                ungroundedCount: ungroundedCount,
                totalSegments: totalSegments,
                sourceScore: sourceScore,
                rawScore: rawScore,
                overrideReason: null));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps a raw composite score to a <see cref="ConfidenceLevel"/>.
    /// </summary>
    private static ConfidenceLevel MapToLevel(double rawScore) => rawScore switch
    {
        >= HighThreshold => ConfidenceLevel.High,
        >= MediumThreshold => ConfidenceLevel.Medium,
        _ => ConfidenceLevel.Low,
    };

    /// <summary>
    /// Estimates the total number of segments that the groundedness check evaluated.
    ///
    /// The Azure AI Content Safety API only returns <em>ungrounded</em> details; it does not
    /// tell us the total number of segments it evaluated. We estimate total_segments as:
    ///   - ungrounded_count + 1   when ungrounded segments were found but the response was
    ///     not flagged as fully ungrounded (i.e. at least one grounded segment exists), OR
    ///   - max(ungrounded_count, 1)  as the lower bound.
    ///
    /// This is a conservative approximation. A richer groundedness API response in a future
    /// API version could provide the actual total; this method is the single place to update.
    /// </summary>
    private static int EstimateTotalSegments(GroundednessResult result)
    {
        var ungrounded = result.UngroundedSegments.Count;

        if (ungrounded == 0)
        {
            // Fully grounded (or check was skipped) — treat as 1 segment grounded.
            return 1;
        }

        // At least one ungrounded segment; the response is partially ungrounded if IsGrounded
        // is still true (fail-open), or fully ungrounded if IsGrounded is false.
        // Use ungrounded + 1 to avoid a zero denominator and to credit at least one
        // grounded segment when the check did not mark the whole response as ungrounded.
        return result.IsGrounded ? ungrounded + 1 : ungrounded;
    }

    /// <summary>
    /// Builds a human-readable rationale string (ADR-015: counts only, no raw text).
    /// </summary>
    private static string BuildRationale(
        int sourcePassageCount,
        double? groundednessRatio,
        int ungroundedCount,
        int totalSegments,
        double sourceScore,
        double rawScore,
        string? overrideReason)
    {
        if (overrideReason is not null)
        {
            return overrideReason;
        }

        var groundednessDescription = groundednessRatio.HasValue
            ? $"groundedness_ratio={groundednessRatio.Value:P0} ({ungroundedCount} ungrounded of ~{totalSegments} segments)"
            : "groundedness_ratio=N/A (no groundedness check result; optimistic fallback used)";

        return
            $"source_passages={sourcePassageCount} → source_score={sourceScore:F2}; " +
            $"{groundednessDescription}; " +
            $"raw_score={rawScore:F3} (weights: groundedness×0.6 + source×0.4).";
    }
}
