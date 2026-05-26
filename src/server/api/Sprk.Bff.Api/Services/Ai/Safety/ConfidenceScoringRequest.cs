namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Input to <see cref="IConfidenceScoringService.Score"/>.
///
/// All inputs are cheap to collect at the call site: they are counts and the already-computed
/// <see cref="GroundednessResult"/> from the prior groundedness check. No additional I/O is
/// required to build this record.
///
/// ADR-015: this record does NOT carry raw text (response body, source passages). Only
/// computed counts and the groundedness result — which itself contains only segment counts,
/// not the segment text — flow through here.
/// </summary>
/// <param name="SourcePassageCount">
/// Number of RAG source passages that were retrieved and injected into the LLM context for
/// this turn. Zero means no sources were available; the service will override to
/// <see cref="ConfidenceLevel.Low"/> regardless of other inputs.
/// </param>
/// <param name="GroundednessResult">
/// The result of the groundedness check for this turn, produced by
/// <see cref="IGroundednessCheckService"/>. When null (e.g. groundedness check was disabled
/// or failed with an unrecoverable error), scoring falls back to the source-count heuristic only.
/// </param>
/// <param name="ResponseLength">
/// Character count of the full LLM response text. Used for context in the rationale string;
/// not used in score computation.
/// </param>
/// <param name="CitationCount">
/// Number of inline citations extracted from the LLM response by CitationExtractor. Used for
/// context in the rationale string; not used in score computation.
/// </param>
public sealed record ConfidenceScoringRequest(
    int SourcePassageCount,
    GroundednessResult? GroundednessResult,
    int ResponseLength = 0,
    int CitationCount = 0);
