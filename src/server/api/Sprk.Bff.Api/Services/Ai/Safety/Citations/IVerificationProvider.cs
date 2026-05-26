namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Abstraction for a citation verification provider (e.g. CourtListener, USPTO, EDGAR, eCFR).
///
/// Design principles:
///   - One provider per citation authority (one per external API).
///   - Provider selection is driven by <see cref="SupportedTypes"/>; the first registered
///     provider that returns <c>true</c> for a given <see cref="CitationType"/> handles it.
///   - All methods must fail gracefully — exceptions propagate to
///     <see cref="CitationVerificationService"/>, which catches them per-citation and returns
///     <see cref="CitationVerificationResult.FromError"/> without aborting the batch.
///   - ADR-015: providers MUST NOT log raw citation text; log only type, provider name,
///     and outcome/latency.
///
/// Registration: add implementations to the DI container via
/// <c>services.AddSingleton&lt;IVerificationProvider, MyProvider&gt;()</c> in
/// <see cref="Infrastructure.DI.AiSafetyModule"/>. The
/// <see cref="CitationVerificationService"/> receives all registered providers via
/// <c>IEnumerable&lt;IVerificationProvider&gt;</c> constructor injection.
/// </summary>
public interface IVerificationProvider
{
    /// <summary>
    /// Short identifier for this provider (e.g. "CourtListener", "USPTO", "EDGAR", "eCFR").
    /// Appears as <see cref="CitationVerificationResult.VerificationProvider"/> in results.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// The citation types this provider can verify.
    /// <see cref="CitationVerificationService"/> calls <see cref="CanVerify"/> to select
    /// the first compatible provider for each extracted citation.
    /// </summary>
    IReadOnlyList<CitationType> SupportedTypes { get; }

    /// <summary>
    /// Returns <c>true</c> if this provider supports the given citation type.
    /// </summary>
    /// <remarks>
    /// Default implementation checks <see cref="SupportedTypes"/>. Override only when
    /// type-based routing is insufficient (e.g. sub-type disambiguation).
    /// </remarks>
    bool CanVerify(CitationType type) => SupportedTypes.Contains(type);

    /// <summary>
    /// Verifies that a citation exists and is accurate according to this provider's authority.
    /// </summary>
    /// <param name="citation">The citation to verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="CitationVerificationResult"/> with verification outcome, confidence,
    /// source URL, and optional excerpt. Must not throw — exceptions are caught by the caller.
    /// </returns>
    Task<CitationVerificationResult> VerifyAsync(Citation citation, CancellationToken ct);

    /// <summary>
    /// Searches the provider's corpus for citations matching the given free-text query.
    /// Used for discovery workflows (e.g. "find cases about § 101 patent eligibility").
    /// </summary>
    /// <param name="query">Free-text search query.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of candidate citations; empty list when no results found.</returns>
    Task<IReadOnlyList<Citation>> SearchAsync(string query, CancellationToken ct);

    /// <summary>
    /// Retrieves the full text of a citation from the provider, if available.
    /// Used to surface the authoritative source text alongside a verified citation.
    /// </summary>
    /// <param name="citation">The citation whose full text is requested.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Full text string, or <c>null</c> if not available from this provider.</returns>
    Task<string?> GetFullTextAsync(Citation citation, CancellationToken ct);
}
