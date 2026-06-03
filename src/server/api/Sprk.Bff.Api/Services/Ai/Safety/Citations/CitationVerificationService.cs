using System.Diagnostics;

namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Orchestrates citation extraction and parallel verification across all registered
/// <see cref="IVerificationProvider"/> implementations.
///
/// Routing algorithm:
///   1. Call <see cref="CitationExtractor.ExtractCitations"/> on the input text.
///   2. For each extracted <see cref="Citation"/>, find the first registered provider
///      whose <see cref="IVerificationProvider.CanVerify"/> returns <c>true</c>.
///   3. If no provider matches → <see cref="CitationVerificationResult.NoProvider"/>.
///   4. If a provider is found → call <see cref="IVerificationProvider.VerifyAsync"/> in parallel.
///   5. If the provider throws → <see cref="CitationVerificationResult.FromError"/>; the
///      remaining citations continue to be processed.
///   6. Aggregate all results into a <see cref="CitationVerificationReport"/>.
///
/// Lifetime: Singleton — stateless; all injected providers are also singletons.
///
/// ADR-010: <see cref="ICitationVerificationService"/> interface registered for testability.
/// ADR-015: citation text MUST NOT appear in log messages.
/// </summary>
public sealed class CitationVerificationService : ICitationVerificationService
{
    private readonly IReadOnlyList<IVerificationProvider> _providers;
    private readonly ILogger<CitationVerificationService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="CitationVerificationService"/>.
    /// </summary>
    /// <param name="providers">
    /// All registered <see cref="IVerificationProvider"/> implementations, injected by DI
    /// via <c>IEnumerable&lt;IVerificationProvider&gt;</c>. May be empty (no providers registered).
    /// </param>
    /// <param name="logger">Logger. ADR-015: log only type/provider/outcome — not citation text.</param>
    public CitationVerificationService(
        IEnumerable<IVerificationProvider> providers,
        ILogger<CitationVerificationService> logger)
    {
        _providers = providers.ToList().AsReadOnly();
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Citation> Extract(string text) =>
        CitationExtractor.ExtractCitations(text);

    /// <inheritdoc/>
    public async Task<CitationVerificationReport> VerifyAllAsync(string text, CancellationToken ct = default)
    {
        var citations = CitationExtractor.ExtractCitations(text);

        if (citations.Count == 0)
        {
            _logger.LogDebug("CitationVerification: no citations found in text ({CharCount} chars).",
                text.Length);
            return new CitationVerificationReport([], [], []);
        }

        _logger.LogInformation(
            "CitationVerification: verifying {Count} citation(s) using {ProviderCount} provider(s).",
            citations.Count, _providers.Count);

        // Dispatch all citations in parallel. Each citation resolves its own provider.
        var tasks = citations.Select(c => VerifySingleAsync(c, ct));
        var allResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        var verified = new List<CitationVerificationResult>();
        var unverified = new List<CitationVerificationResult>();
        var errors = new List<CitationVerificationResult>();

        foreach (var result in allResults)
        {
            if (result.VerificationProvider == "error")
                errors.Add(result);
            else if (result.IsVerified)
                verified.Add(result);
            else
                unverified.Add(result);
        }

        _logger.LogInformation(
            "CitationVerification complete: verified={Verified}, unverified={Unverified}, errors={Errors}.",
            verified.Count, unverified.Count, errors.Count);

        return new CitationVerificationReport(
            verified.AsReadOnly(),
            unverified.AsReadOnly(),
            errors.AsReadOnly());
    }

    // =========================================================================
    // Private: single citation dispatch
    // =========================================================================

    private async Task<CitationVerificationResult> VerifySingleAsync(Citation citation, CancellationToken ct)
    {
        // Find the first provider that supports this citation type.
        var provider = _providers.FirstOrDefault(p => p.CanVerify(citation.CitationType));

        if (provider is null)
        {
            _logger.LogDebug(
                "CitationVerification: no provider for {CitationType}. Returning unverified.",
                citation.CitationType);
            return CitationVerificationResult.NoProvider(citation);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await provider.VerifyAsync(citation, ct).ConfigureAwait(false);
            sw.Stop();

            // ADR-015: log type + provider + outcome only, never citation text.
            _logger.LogDebug(
                "CitationVerification: {Provider} verified {CitationType} → isVerified={IsVerified}, " +
                "confidence={Confidence:F2}, latencyMs={LatencyMs:F1}",
                provider.ProviderName, citation.CitationType,
                result.IsVerified, result.ConfidenceScore, sw.Elapsed.TotalMilliseconds);

            return result;
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(
                "CitationVerification: {Provider} cancelled for {CitationType} after {LatencyMs:F1}ms.",
                provider.ProviderName, citation.CitationType, sw.Elapsed.TotalMilliseconds);
            throw; // Re-throw cancellations — callers (Task.WhenAll) handle them correctly.
        }
        catch (Exception ex)
        {
            sw.Stop();
            var latencyMs = sw.Elapsed.TotalMilliseconds;

            _logger.LogWarning(ex,
                "CitationVerification: {Provider} threw for {CitationType} after {LatencyMs:F1}ms. " +
                "Returning error result.",
                provider.ProviderName, citation.CitationType, latencyMs);

            return CitationVerificationResult.FromError(
                citation,
                provider.ProviderName,
                ex.Message,
                latencyMs);
        }
    }
}
