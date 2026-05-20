using System.Diagnostics;

namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

// ── Output model ──────────────────────────────────────────────────────────────

/// <summary>
/// Annotation produced by <see cref="CitationSafetyCheck.CheckResponseAsync"/> for a single
/// LLM response. Carried as the data payload in the <c>safety_annotation</c> SSE event
/// (event type: <c>citation_verification</c>).
///
/// SSE event schema:
/// <code>
/// {
///   "type": "citation_verification",
///   "citations": [
///     {
///       "raw":        string,
///       "type":       string,
///       "normalized": string,
///       "isVerified": bool,
///       "confidence": float,
///       "sourceUrl":  string | null
///     }
///   ]
/// }
/// </code>
///
/// When <see cref="HasCitations"/> is <c>false</c> the caller MUST still emit the event
/// with an empty <see cref="Citations"/> array — silent omission is not permitted.
/// </summary>
/// <param name="Citations">
/// Ordered list of citation annotation entries extracted from the LLM response.
/// Empty when no citations were detected; never null.
/// </param>
public sealed record CitationSafetyAnnotation(
    IReadOnlyList<CitationAnnotationEntry> Citations)
{
    /// <summary><c>true</c> when at least one citation was extracted from the response.</summary>
    public bool HasCitations => Citations.Count > 0;

    /// <summary>SSE event type string placed in the "type" field of the safety_annotation event.</summary>
    public const string SseEventType = "citation_verification";
}

/// <summary>
/// A single citation entry within a <see cref="CitationSafetyAnnotation"/>.
/// Serialised to camelCase JSON by the SSE pipeline.
/// </summary>
/// <param name="Raw">Verbatim citation text as extracted from the LLM response. ADR-015: NOT logged.</param>
/// <param name="Type">Citation type string (CaseLaw, Statute, Patent, SecFiling, Regulation, Unknown).</param>
/// <param name="Normalized">Canonical deduplication key (e.g. "542 U.S. 296", "35 U.S.C. § 101").</param>
/// <param name="IsVerified"><c>true</c> when a provider confirmed the citation.</param>
/// <param name="Confidence">Provider confidence score in [0.0, 1.0]; 0 when unverified or error.</param>
/// <param name="SourceUrl">Canonical provider URL, or <c>null</c> when unavailable.</param>
public sealed record CitationAnnotationEntry(
    string Raw,
    string Type,
    string Normalized,
    bool IsVerified,
    float Confidence,
    string? SourceUrl);

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Post-LLM safety check that extracts and verifies legal citations in an AI response,
/// then returns a <see cref="CitationSafetyAnnotation"/> ready for emission as a
/// <c>safety_annotation</c> SSE event.
///
/// Design decisions:
///   - POST-LLM, non-blocking: runs after the response stream completes and the result is
///     emitted as a <c>citation_verification</c> typed safety_annotation SSE event.
///     The user sees the AI response immediately; citations are annotated after.
///   - Fail-open: any exception returns an annotation with an empty citations list and a
///     warning log. The check MUST NOT suppress a valid AI response.
///   - Always emits: even when no citations are found, returns an annotation with an empty
///     array so the pipeline can emit the event without a silent gap.
///   - Parallel-safe: this service and <see cref="IGroundednessCheckService"/> emit different
///     SSE event types and may run concurrently without deadlock or ordering conflicts.
///   - ADR-015: citation text MUST NOT appear in log messages. Only counts and types are logged.
///
/// Lifetime: Scoped — one instance per HTTP request, consistent with IGroundednessCheckService.
/// Registered in <see cref="Infrastructure.DI.AiSafetyModule"/>.
/// </summary>
public sealed class CitationSafetyCheck
{
    private readonly ICitationVerificationService _verificationService;
    private readonly ILogger<CitationSafetyCheck> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CitationSafetyCheck"/>.
    /// </summary>
    /// <param name="verificationService">
    /// Stateless citation extraction and verification service (singleton lifetime).
    /// </param>
    /// <param name="logger">
    /// Logger. ADR-015: log only counts, types, and latency — never citation text.
    /// </param>
    public CitationSafetyCheck(
        ICitationVerificationService verificationService,
        ILogger<CitationSafetyCheck> logger)
    {
        _verificationService = verificationService ?? throw new ArgumentNullException(nameof(verificationService));
        _logger              = logger               ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts and verifies all legal citations in <paramref name="aiResponse"/> and
    /// returns a <see cref="CitationSafetyAnnotation"/> for the SSE pipeline to emit.
    ///
    /// This method is called automatically after every LLM response by the AI safety
    /// pipeline (task 065). It MUST NOT throw — any internal failure returns an empty
    /// annotation with a warning log (fail-open contract).
    ///
    /// Concurrency: safe to run in parallel with <see cref="IGroundednessCheckService.CheckAsync"/>
    /// because both services are stateless and emit distinct SSE event types.
    /// </summary>
    /// <param name="aiResponse">
    /// The full LLM response text to scan. ADR-015: this value is NEVER logged.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="CitationSafetyAnnotation"/> containing the verification results.
    /// When no citations are found the annotation has an empty <see cref="CitationSafetyAnnotation.Citations"/>
    /// list — the caller MUST still emit the SSE event (no silent omission).
    /// </returns>
    public async Task<CitationSafetyAnnotation> CheckResponseAsync(string aiResponse, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
        {
            _logger.LogDebug("CitationSafetyCheck: empty response — skipping extraction.");
            return new CitationSafetyAnnotation([]);
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var report = await _verificationService.VerifyAllAsync(aiResponse, ct).ConfigureAwait(false);
            sw.Stop();

            // ADR-015: log counts and latency only — never citation text.
            _logger.LogInformation(
                "CitationSafetyCheck: responseLen={ResponseLen}, total={Total}, " +
                "verified={Verified}, unverified={Unverified}, errors={Errors}, latencyMs={LatencyMs:F1}",
                aiResponse.Length,
                report.TotalCitations,
                report.Verified.Count,
                report.Unverified.Count,
                report.Errors.Count,
                sw.Elapsed.TotalMilliseconds);

            var entries = report.All
                .Select(r => new CitationAnnotationEntry(
                    Raw:        r.Citation.RawText,
                    Type:       r.Citation.CitationType.ToString(),
                    Normalized: r.Citation.NormalizedKey,
                    IsVerified: r.IsVerified,
                    Confidence: r.ConfidenceScore,
                    SourceUrl:  r.SourceUrl))
                .ToList()
                .AsReadOnly();

            return new CitationSafetyAnnotation(entries);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogInformation(
                "CitationSafetyCheck: cancelled after {LatencyMs:F1}ms. Returning empty annotation.",
                sw.Elapsed.TotalMilliseconds);
            return new CitationSafetyAnnotation([]);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Fail-open: log warning, return empty annotation — MUST NOT suppress the AI response.
            _logger.LogWarning(ex,
                "CitationSafetyCheck: failed unexpectedly after {LatencyMs:F1}ms. " +
                "Returning empty annotation (fail-open).",
                sw.Elapsed.TotalMilliseconds);
            return new CitationSafetyAnnotation([]);
        }
    }
}
