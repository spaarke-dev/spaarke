namespace Sprk.Bff.Api.Services.Ai.Insights.Sanitization;

/// <summary>
/// D-50 / D-A25 — sanitizes raw document text before it reaches any LLM step in the
/// universal ingest pipeline (D-P7). Mechanical, zero-LLM, deterministic.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase 1 scope (minimal-viable sanitizer)</b>: collapses pathological whitespace,
/// strips control characters that confuse tokenizers, caps total length to a deterministic
/// upper bound, and strips leading prompt-injection prefixes that have been observed in
/// adversarial inputs (e.g., <c>"Ignore all previous instructions..."</c>). The sanitizer
/// is intentionally conservative — substantive content (case, punctuation, accents) is
/// preserved so the downstream <c>GroundingVerifier</c> can still match verbatim quotes.
/// </para>
/// <para>
/// <b>Phase 1.5+ swap path</b>: when the LAVERN cross-tool Sanitizer primitive ships, this
/// interface is the seam — its single impl (<see cref="InsightsContentSanitizer"/>) is
/// replaced by a delegating adapter without changing any caller. The interface
/// signature deliberately matches LAVERN's expected shape (raw → sanitized + diagnostic
/// metadata) so the swap is a DI re-registration.
/// </para>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/Sanitization/</c>. Phase 1
/// consumers: <see cref="IIngestOrchestrator"/> (task 040). Phase 1.5+: extended Layers
/// (entity extraction, deal-terms extraction) as they come online.
/// </para>
/// </remarks>
public interface IInsightsContentSanitizer
{
    /// <summary>
    /// Sanitizes raw document text. Returns the sanitized text along with diagnostic
    /// counters useful for the D-P11 review surface (how often sanitization actually
    /// changes input — high rates suggest upstream extraction quality issues).
    /// </summary>
    /// <param name="rawText">Raw text extracted from a document (e.g., via TextExtractor).
    /// May be null/whitespace; sanitizer returns an empty result in that case (not an error).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sanitized text plus diagnostic metadata.</returns>
    Task<SanitizationResult> SanitizeAsync(string? rawText, CancellationToken ct);
}

/// <summary>
/// Result of sanitization. Carries the cleaned text plus diagnostic counters.
/// </summary>
/// <param name="SanitizedText">The cleaned text. Empty when input was null/whitespace.</param>
/// <param name="OriginalLength">Length of <c>rawText</c> at entry (post null-coalesce to empty).</param>
/// <param name="SanitizedLength">Length of <see cref="SanitizedText"/>.</param>
/// <param name="WasTruncated">True if the input exceeded <c>MaxLength</c> and was capped.</param>
/// <param name="HadInjectionPrefix">True if a recognized prompt-injection prefix was stripped.</param>
public sealed record SanitizationResult(
    string SanitizedText,
    int OriginalLength,
    int SanitizedLength,
    bool WasTruncated,
    bool HadInjectionPrefix);
