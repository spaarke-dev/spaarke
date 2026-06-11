using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Verifies citations against the Spaarke internal AI Search index
/// (<c>spaarke-rag-references</c>) — the curated knowledge base of authoritative legal
/// references ingested from uploaded matter documents and curated legal sources.
///
/// This is the fallback provider: it handles CaseLaw, Statute, and Regulation types.
/// Confidence is derived from the AI Search relevance score returned for the top result.
///
/// Score thresholds (keyword / BM25):
///   &gt;= 0.85 → IsVerified = true, ConfidenceScore = normalised score
///   &lt;  0.85 → IsVerified = false, ConfidenceScore = 0
///
/// ADR-010: no interface wrapper — single implementation, singleton lifetime.
/// ADR-015: citation text MUST NOT appear in log messages; log only type and outcome.
/// </summary>
public sealed class InternalIndexProvider : IVerificationProvider
{
    // R6 Wave B-G11 (2026-06-10) — Lazy<SearchClient> so DI startup doesn't crash
    // when AiSearch:ReferencesEndpoint/ApiKey aren't configured (matches the
    // BingGroundingOptions B-G8 + AgentServiceOptions B-G11 hardening pattern).
    // Config validation happens on first VerifyAsync/SearchAsync call.
    private readonly Lazy<SearchClient> _searchClientLazy;
    private SearchClient _searchClient => _searchClientLazy.Value;
    private readonly ILogger<InternalIndexProvider> _logger;

    /// <summary>Name of the semantic configuration defined in spaarke-rag-references.json.</summary>
    private const string SemanticConfigName = "rag-references-semantic-config";

    /// <summary>Score threshold: top result must meet or exceed this to count as verified.</summary>
    private const double VerifyThreshold = 0.85;

    /// <summary>Maximum results returned by <see cref="SearchAsync"/>.</summary>
    private const int SearchTop = 5;

    /// <inheritdoc/>
    public string ProviderName => "InternalIndex";

    /// <inheritdoc/>
    public IReadOnlyList<CitationType> SupportedTypes { get; } =
        [CitationType.CaseLaw, CitationType.Statute, CitationType.Regulation];

    /// <inheritdoc/>
    /// <remarks>Always returns <c>true</c> — handles all three supported types.</remarks>
    public bool CanVerify(CitationType type) => SupportedTypes.Contains(type);

    /// <summary>
    /// Initialises the provider from pre-built <see cref="SearchClient"/>.
    /// Use the factory constructor (accepting <see cref="IConfiguration"/>) in production.
    /// This overload exists for unit-test injection.
    /// </summary>
    public InternalIndexProvider(SearchClient searchClient, ILogger<InternalIndexProvider> logger)
    {
        if (searchClient is null) throw new ArgumentNullException(nameof(searchClient));
        _searchClientLazy = new Lazy<SearchClient>(() => searchClient, isThreadSafe: true);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Production constructor: builds the <see cref="SearchClient"/> from configuration keys
    /// <c>AiSearch:ReferencesEndpoint</c>, <c>AiSearch:ReferencesApiKey</c>, and
    /// <c>AiSearch:ReferencesIndexName</c> (default: <c>spaarke-rag-references</c>).
    ///
    /// R6 Wave B-G11 (2026-06-10) — SearchClient creation is deferred to first use via
    /// <see cref="Lazy{T}"/> so DI startup doesn't crash when these config keys are
    /// missing (matches the BingGroundingOptions B-G8 + AgentServiceOptions B-G11 pattern).
    /// </summary>
    public InternalIndexProvider(IConfiguration configuration, ILogger<InternalIndexProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _searchClientLazy = new Lazy<SearchClient>(() =>
        {
            var endpoint = configuration["AiSearch:ReferencesEndpoint"]
                ?? throw new InvalidOperationException("AiSearch:ReferencesEndpoint is not configured.");
            var apiKey = configuration["AiSearch:ReferencesApiKey"]
                ?? throw new InvalidOperationException("AiSearch:ReferencesApiKey is not configured.");
            var indexName = configuration["AiSearch:ReferencesIndexName"]
                ?? "spaarke-rag-references";

            return new SearchClient(
                new Uri(endpoint),
                indexName,
                new AzureKeyCredential(apiKey));
        }, isThreadSafe: true);
    }

    // =========================================================================
    // IVerificationProvider
    // =========================================================================

    /// <inheritdoc/>
    /// <remarks>
    /// Queries <c>spaarke-rag-references</c> using <see cref="Citation.NormalizedKey"/>
    /// as search text with semantic ranking enabled.  The top result's score determines
    /// whether the citation is considered verified.
    ///
    /// <see cref="RequestFailedException"/> is caught and returned as unverified
    /// (never propagated), consistent with the interface contract.
    /// </remarks>
    public async Task<CitationVerificationResult> VerifyAsync(Citation citation, CancellationToken ct)
    {
        try
        {
            var opts = new SearchOptions
            {
                Size = 3,
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = SemanticConfigName,
                    QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                },
            };
            opts.Select.Add("id");
            opts.Select.Add("content");
            opts.Select.Add("knowledgeSourceName");
            opts.Select.Add("domain");

            var response = await _searchClient
                .SearchAsync<SearchDocument>(citation.NormalizedKey, opts, ct)
                .ConfigureAwait(false);

            SearchResult<SearchDocument>? topResult = null;
            double topScore = 0;

            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                // Use semantic reranker score when available; fall back to BM25 score.
                var score = result.SemanticSearch?.RerankerScore ?? result.Score ?? 0;
                if (topResult is null || score > topScore)
                {
                    topResult = result;
                    topScore = score;
                }
            }

            if (topResult is null || topScore < VerifyThreshold)
            {
                _logger.LogDebug(
                    "InternalIndex: {CitationType} — no result above threshold ({Threshold}). " +
                    "topScore={TopScore:F3}",
                    citation.CitationType, VerifyThreshold, topScore);

                return Unverified(citation);
            }

            var content = topResult.Document.TryGetValue("content", out var c) ? c as string : null;
            var knowledgeSource = topResult.Document.TryGetValue("knowledgeSourceName", out var ks) ? ks as string : null;
            var domain = topResult.Document.TryGetValue("domain", out var d) ? d as string : null;
            var docId = topResult.Document.TryGetValue("id", out var id) ? id as string : null;

            // Build a best-effort source URL from the document ID (no dedicated URL field in index).
            var sourceUrl = BuildSourceUrl(docId, domain);

            // Normalise confidence: semantic reranker scores are [0, 4]; BM25 is unbounded.
            // We clamp to [0, 1] regardless of the scoring regime.
            var confidence = (float)Math.Min(topScore / 4.0, 1.0);

            _logger.LogDebug(
                "InternalIndex: {CitationType} verified. topScore={TopScore:F3}, confidence={Confidence:F3}",
                citation.CitationType, topScore, confidence);

            return new CitationVerificationResult(
                Citation: citation,
                IsVerified: true,
                ConfidenceScore: confidence,
                SourceUrl: sourceUrl,
                VerifiedText: Truncate(content, 500),
                VerificationProvider: ProviderName,
                LatencyMs: 0.0);   // latency tracked by CitationVerificationService
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "InternalIndex: search request failed for {CitationType}. " +
                "Status={Status}. Returning unverified.",
                citation.CitationType, ex.Status);

            return Unverified(citation);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Free-text search against the references index, returning up to
    /// <see cref="SearchTop"/> candidate <see cref="Citation"/> objects.
    /// Returns an empty list on search failure (never propagates exceptions).
    /// </remarks>
    public async Task<IReadOnlyList<Citation>> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            var opts = new SearchOptions
            {
                Size = SearchTop,
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = SemanticConfigName,
                },
            };
            opts.Select.Add("id");
            opts.Select.Add("content");
            opts.Select.Add("domain");

            var response = await _searchClient
                .SearchAsync<SearchDocument>(query, opts, ct)
                .ConfigureAwait(false);

            var results = new List<Citation>();

            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                var content = result.Document.TryGetValue("content", out var c) ? c as string : null;
                var domain = result.Document.TryGetValue("domain", out var d) ? d as string : null;
                var docId = result.Document.TryGetValue("id", out var id) ? id as string : null;

                // Derive a normalised key and citation type from whatever the index returns.
                var (citationType, normalizedKey) = InferCitationMeta(domain, content, docId);

                results.Add(new Citation(
                    RawText: content is not null ? Truncate(content, 200)! : query,
                    CitationType: citationType,
                    NormalizedKey: normalizedKey));
            }

            return results.AsReadOnly();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "InternalIndex: SearchAsync failed for query. Status={Status}. " +
                "Returning empty list.",
                ex.Status);

            return [];
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Retrieves the full document text from the index by performing an exact-match
    /// lookup using <see cref="Citation.NormalizedKey"/> against the <c>content</c>
    /// field.  Returns <c>null</c> when not found or on error.
    /// </remarks>
    public async Task<string?> GetFullTextAsync(Citation citation, CancellationToken ct)
    {
        try
        {
            // Wrap the normalised key in quotes for a phrase match — closest to exact lookup
            // available without a dedicated citationKey filterable field.
            var opts = new SearchOptions
            {
                Size = 1,
            };
            opts.Select.Add("id");
            opts.Select.Add("content");

            var phrase = $"\"{citation.NormalizedKey}\"";
            var response = await _searchClient
                .SearchAsync<SearchDocument>(phrase, opts, ct)
                .ConfigureAwait(false);

            await foreach (var result in response.Value.GetResultsAsync().WithCancellation(ct))
            {
                if (result.Document.TryGetValue("content", out var c) && c is string text && !string.IsNullOrEmpty(text))
                    return text;
            }

            return null;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "InternalIndex: GetFullTextAsync failed for {CitationType}. " +
                "Status={Status}. Returning null.",
                citation.CitationType, ex.Status);

            return null;
        }
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private static CitationVerificationResult Unverified(Citation citation) =>
        new(
            Citation: citation,
            IsVerified: false,
            ConfidenceScore: 0f,
            SourceUrl: null,
            VerifiedText: null,
            VerificationProvider: "InternalIndex",
            LatencyMs: 0.0);

    /// <summary>
    /// Constructs a best-effort source URL from available index fields.
    /// The <c>spaarke-rag-references</c> index does not contain a dedicated URL field,
    /// so we synthesise a reference path using the document ID and domain.
    /// </summary>
    private static string? BuildSourceUrl(string? docId, string? domain)
    {
        if (string.IsNullOrEmpty(docId))
            return null;

        return string.IsNullOrEmpty(domain)
            ? $"spaarke://references/{docId}"
            : $"spaarke://references/{domain}/{docId}";
    }

    /// <summary>
    /// Infers <see cref="CitationType"/> and a normalised key from available index fields.
    /// Used by <see cref="SearchAsync"/> to construct <see cref="Citation"/> objects from
    /// raw index documents that lack explicit citation metadata.
    /// </summary>
    private static (CitationType type, string key) InferCitationMeta(
        string? domain, string? content, string? docId)
    {
        // Use domain tag as a coarse type hint when present.
        var type = domain?.ToLowerInvariant() switch
        {
            "caselaw" or "case-law" => CitationType.CaseLaw,
            "statute" or "statutes" => CitationType.Statute,
            "regulation" or "regulations" => CitationType.Regulation,
            _ => CitationType.Unknown,
        };

        // Prefer a key derived from content (first 120 chars), fall back to doc ID.
        var key = !string.IsNullOrEmpty(content)
            ? Truncate(content, 120)!
            : (docId ?? "unknown");

        return (type, key);
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
