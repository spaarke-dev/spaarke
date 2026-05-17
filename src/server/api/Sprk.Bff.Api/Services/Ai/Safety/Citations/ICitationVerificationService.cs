namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Extracts legal citations from LLM-generated text and verifies them against
/// registered <see cref="IVerificationProvider"/> implementations.
///
/// This is the public facade consumed by AI pipeline components (e.g. the post-LLM
/// safety annotation stage). Inject this interface rather than the concrete
/// <see cref="CitationVerificationService"/> to keep callers testable.
///
/// Lifetime: Singleton — the service is stateless; providers are also singletons.
/// </summary>
public interface ICitationVerificationService
{
    /// <summary>
    /// Extracts all legal citations from the given text without verifying them.
    /// Useful for previewing citations before committing to provider calls.
    /// </summary>
    /// <param name="text">LLM-generated text to scan for citations.</param>
    /// <returns>
    /// Ordered list of citations found in the text; empty when none are detected.
    /// </returns>
    IReadOnlyList<Citation> Extract(string text);

    /// <summary>
    /// Extracts citations from <paramref name="text"/> and verifies each one against
    /// the first registered <see cref="IVerificationProvider"/> that supports its type.
    ///
    /// Routing rules:
    ///   - Citations with a supporting provider → <see cref="IVerificationProvider.VerifyAsync"/> called.
    ///   - Citations with no supporting provider → <see cref="CitationVerificationResult.NoProvider"/>.
    ///   - Provider exceptions (per-citation) → <see cref="CitationVerificationResult.FromError"/>;
    ///     remaining citations continue to be processed.
    ///
    /// All provider calls for a given text run in parallel (one Task per citation).
    /// </summary>
    /// <param name="text">LLM-generated text to scan and verify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="CitationVerificationReport"/> grouping results into verified,
    /// unverified, and partial lists.
    /// </returns>
    Task<CitationVerificationReport> VerifyAllAsync(string text, CancellationToken ct = default);
}
