namespace Sprk.Bff.Api.Services.Ai.Safety;

/// <summary>
/// A specific text segment in the LLM response that is not supported by the source documents.
/// </summary>
/// <param name="Text">
/// The verbatim text of the ungrounded segment as returned by the API.
/// ADR-015: This field MUST NOT appear in application logs.
/// </param>
/// <param name="Reason">
/// Optional human-readable explanation from the API of why the segment is considered ungrounded.
/// Null when the API does not provide a reason (e.g. when <c>reasoning=false</c> is set in the
/// request, which is the default to reduce latency).
/// </param>
public sealed record UngroundedSegment(string Text, string? Reason = null);

/// <summary>
/// Outcome of a Groundedness Detection check.
/// </summary>
/// <param name="IsGrounded">
/// True when the LLM response is fully grounded in the source documents, when the source
/// document list was empty (check skipped), or when the service was unavailable (fail-open).
/// False when at least one ungrounded segment was detected.
/// </param>
/// <param name="UngroundedSegments">
/// The text segments identified as not supported by the source documents.
/// Empty when <see cref="IsGrounded"/> is true or when the check was skipped / failed-open.
/// ADR-015: segment text MUST NOT be logged.
/// </param>
/// <param name="LatencyMs">
/// Wall-clock time (milliseconds) spent calling the Content Safety API.
/// Zero when the check was skipped (empty source documents).
/// Populated regardless of whether segments were found, to drive OTEL histograms.
/// </param>
public sealed record GroundednessResult(
    bool IsGrounded,
    IReadOnlyList<UngroundedSegment> UngroundedSegments,
    double LatencyMs)
{
    /// <summary>
    /// Convenience factory: fully grounded (no ungrounded segments detected).
    /// </summary>
    public static GroundednessResult Grounded(double latencyMs) =>
        new(true, [], latencyMs);

    /// <summary>
    /// Convenience factory: fail-open result used when the service is unavailable or the
    /// source document list is empty. Callers MUST log a warning before returning this when
    /// it is due to a service failure (not an empty-sources skip).
    /// </summary>
    public static GroundednessResult AssumeGrounded(double latencyMs = 0) =>
        new(true, [], latencyMs);

    /// <summary>
    /// Convenience factory: one or more ungrounded segments were detected.
    /// </summary>
    public static GroundednessResult Ungrounded(
        IReadOnlyList<UngroundedSegment> segments,
        double latencyMs) =>
        new(false, segments, latencyMs);
}
