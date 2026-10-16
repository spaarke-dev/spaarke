using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Null-Object implementation of <see cref="ISemanticSearchService"/> registered when the compound AI kill-switch is OFF
/// (ADR-032, P3 Fail-Fast).
/// </summary>
/// <remarks>
/// <para>
/// The real service is registered only inside the DocumentIntelligence + Analysis gate (<c>AddSemanticSearch</c>), while eBilling's
/// related-work route (ebilling-r1 task 107, FR-34) is mapped unconditionally behind its own flag, so with AI off the route's service
/// would not be constructible. An empty result would read as "no related work product" instead of "search is switched off", so every
/// member throws <see cref="FeatureDisabledException"/>; the consumer reports the section as unavailable.
/// </para>
/// </remarks>
public sealed class NullSemanticSearchService : ISemanticSearchService
{
    private const string ErrorCode = "ai.semantic-search.disabled";
    private const string DetailMessage =
        "Semantic search requires Analysis:Enabled=true and DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullSemanticSearchService> _logger;

    public NullSemanticSearchService(ILogger<NullSemanticSearchService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(SearchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request, string tenantId, CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(CountAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method) =>
        _logger.LogWarning("{Method} called on NullSemanticSearchService: semantic search is disabled ({ErrorCode})", method, ErrorCode);
}
