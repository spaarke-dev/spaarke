namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// Detects ungrounded claims in an LLM response by comparing the response text against
/// the source documents that were retrieved for the turn (RAG passages).
///
/// Uses the Azure AI Content Safety Groundedness Detection API (2024-09-15-preview).
/// This is a POST-LLM check — it runs AFTER the response stream completes, never blocking it.
/// The result is emitted as a <c>safety_annotation</c> SSE event so the client can annotate
/// ungrounded segments in the UI without suppressing the response.
///
/// Implementation contract:
///   - MUST complete in under 200ms (P95).
///   - MUST fail-open on service unavailability (HTTP 429, 5xx, timeout): log warning, return
///     <see cref="GroundednessResult.AssumeGrounded"/>.
///   - MUST NOT log the AI response text or source document content (ADR-015: Tier 1 log =
///     identifiers + outcome only). Only segment count, score, and latency may be logged.
///   - MUST skip the API call and return <see cref="GroundednessResult.AssumeGrounded"/> when
///     <see cref="GroundednessRequest.SourceDocuments"/> is empty (nothing to check against).
/// </summary>
public interface IGroundednessCheckService
{
    /// <summary>
    /// Checks the LLM response for claims that are not supported by the source documents.
    /// </summary>
    /// <param name="request">
    /// The check request containing the AI response text and the source document passages
    /// that were used to generate it.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="GroundednessResult"/> with the groundedness verdict, any ungrounded segments,
    /// and OTEL-compatible latency measurement.
    /// </returns>
    Task<GroundednessResult> CheckAsync(GroundednessRequest request, CancellationToken ct = default);
}
