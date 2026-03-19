using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing web search capabilities for the SprkChatAgent.
///
/// Exposes a single search method:
///   - <see cref="SearchWebAsync"/> — searches the web via Azure Bing Web Search v7 API and returns
///     results marked as external content for data governance (ADR-015).
///
/// When the Bing API key is configured (<c>BingSearch:ApiKey</c>), calls the real Bing Web Search API.
/// When not configured, falls back to mock results with a Warning log (graceful degradation).
///
/// Scope-guided search (FR-10): If <c>scopeSearchGuidance</c> is provided (from the active scope's
/// <c>sprk_searchGuidance</c> field), it is prepended to the search query to guide Bing toward
/// more relevant results for the domain (e.g., legal research prioritizing Westlaw, LexisNexis).
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
///
/// ADR-010: 0 additional DI registrations — factory-instantiated only.
/// ADR-013: AI tools use AIFunctionFactory.Create pattern.
/// ADR-015: Results are marked as [External Source]; MUST NOT log full result bodies or query text above Debug.
/// ADR-016: Web searches bounded by SemaphoreSlim(2) to limit concurrent Bing API calls.
/// </summary>
public sealed class WebSearchTools
{
    /// <summary>
    /// Bounds concurrent Bing API calls to max 2 per ADR-016.
    /// Static to apply across all WebSearchTools instances (all agent sessions).
    /// </summary>
    private static readonly SemaphoreSlim s_bingConcurrencyGate = new(2, 2);

    /// <summary>
    /// Timeout for acquiring the concurrency semaphore before falling back to mock results.
    /// </summary>
    private static readonly TimeSpan s_semaphoreTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Timeout for individual Bing API HTTP requests.
    /// </summary>
    private static readonly TimeSpan s_httpTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CitationContext? _citationContext;
    private readonly string? _apiKey;
    private readonly string _endpoint;
    private readonly int _configMaxResults;
    private readonly string? _scopeSearchGuidance;

    /// <summary>
    /// Named HttpClient registration name for Bing Web Search API.
    /// </summary>
    public const string HttpClientName = "BingWebSearch";

    /// <summary>
    /// Creates a new <see cref="WebSearchTools"/> instance.
    /// </summary>
    /// <param name="logger">Logger for operation metadata (ADR-015: no result content in logs above Debug).</param>
    /// <param name="httpClientFactory">Factory for creating HttpClient instances for Bing API calls.</param>
    /// <param name="citationContext">Shared citation context for registering web search result citations.</param>
    /// <param name="apiKey">Bing Web Search API key. Null/empty triggers graceful mock fallback.</param>
    /// <param name="endpoint">Bing Web Search API endpoint (defaults to v7 endpoint if null).</param>
    /// <param name="maxResults">Maximum results to return from config (default 10, capped at 10).</param>
    /// <param name="scopeSearchGuidance">
    /// Optional search guidance from the active scope's <c>sprk_searchGuidance</c> field.
    /// When non-empty, prepended to search queries to guide Bing toward domain-relevant results (FR-10).
    /// Populated by R2-020 (AnalysisChatContextResolver). Null when not available.
    /// </param>
    public WebSearchTools(
        ILogger logger,
        IHttpClientFactory httpClientFactory,
        CitationContext? citationContext,
        string? apiKey,
        string? endpoint = null,
        int maxResults = 10,
        string? scopeSearchGuidance = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _citationContext = citationContext;
        _apiKey = apiKey;
        _endpoint = string.IsNullOrWhiteSpace(endpoint)
            ? "https://api.bing.microsoft.com/v7.0/search"
            : endpoint;
        _configMaxResults = Math.Clamp(maxResults, 1, 10);
        _scopeSearchGuidance = scopeSearchGuidance;
    }

    /// <summary>
    /// Searches the web for information relevant to the user's query.
    /// Use this when the user asks a question that cannot be answered from internal documents
    /// alone — for example: recent news, industry regulations, public company information,
    /// or general knowledge topics.
    ///
    /// Results are marked as [External Source] to indicate they originate from the public web
    /// and have not been verified against internal knowledge sources (ADR-015).
    ///
    /// When the Bing API key is not configured, returns mock results (graceful degradation).
    /// When the Bing concurrency limit is reached (ADR-016), returns mock results with a note.
    /// </summary>
    /// <param name="query">Web search query</param>
    /// <param name="maxResults">Maximum number of results to return (default: 5, cap: 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of web search results, each prefixed with [External Source].</returns>
    [Description("Search the web for information relevant to the user's query. " +
                 "Use this when the question cannot be answered from internal documents alone.")]
    public async Task<string> SearchWebAsync(
        [Description("Web search query")] string query,
        [Description("Maximum number of results to return (default: 5, cap: 10)")]
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        var count = Math.Clamp(maxResults, 1, _configMaxResults);

        // ADR-015: Log query length and result count only — never log the query text or result bodies
        // at Info level or above.
        _logger.LogInformation(
            "SearchWeb starting — queryLen={QueryLen}, maxResults={MaxResults}",
            query.Length, count);

        // Check if Bing API key is configured — if not, fall back to mock results.
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Bing API key not configured — using mock results for web search");
            return FormatResults(GenerateMockResults(query, count), query);
        }

        // Apply scope-guided search: prepend guidance to the query if available (FR-10).
        var effectiveQuery = ApplyScopeGuidance(query);

        // ADR-015: Log the effective query only at Debug level (may contain sensitive case terms).
        _logger.LogDebug(
            "SearchWeb effective query (with scope guidance): {EffectiveQuery}",
            effectiveQuery);

        // ADR-016: Bound concurrent Bing API calls to max 2 via SemaphoreSlim.
        if (!await s_bingConcurrencyGate.WaitAsync(s_semaphoreTimeout, cancellationToken))
        {
            _logger.LogWarning(
                "Bing API concurrency limit reached (max 2 concurrent calls) — falling back to mock results");
            return FormatResults(GenerateMockResults(query, count), query,
                degradationNote: "Web search is temporarily limited. Results shown are from a fallback source.");
        }

        try
        {
            var results = await CallBingApiAsync(effectiveQuery, count, cancellationToken);

            _logger.LogInformation(
                "SearchWeb completed — queryLen={QueryLen}, resultCount={ResultCount}",
                query.Length, results.Count);

            // Register citations in the shared CitationContext for SSE delivery.
            RegisterCitations(results);

            return FormatResults(results, query);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Bing API request timed out after {TimeoutSeconds}s — returning empty results",
                s_httpTimeout.TotalSeconds);
            return FormatResults([], query,
                degradationNote: "Web search timed out. Please try again or refine your query.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Bing API request failed with HTTP error — returning empty results");
            return FormatResults([], query,
                degradationNote: "Web search is temporarily unavailable. Please try again later.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Bing API response — returning empty results");
            return FormatResults([], query,
                degradationNote: "Web search returned an unexpected response. Please try again.");
        }
        finally
        {
            s_bingConcurrencyGate.Release();
        }
    }

    // === Bing API Integration ===

    /// <summary>
    /// Calls the Bing Web Search v7 API and returns parsed search results.
    /// </summary>
    private async Task<List<WebSearchResult>> CallBingApiAsync(
        string query, int count, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = s_httpTimeout;

        var requestUri = $"{_endpoint}?q={Uri.EscapeDataString(query)}&count={count}&mkt=en-US";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Bing API returned non-success status {StatusCode}",
                (int)response.StatusCode);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var bingResponse = JsonSerializer.Deserialize<BingSearchResponse>(json);

        if (bingResponse?.WebPages?.Value is not { Count: > 0 })
        {
            _logger.LogInformation("Bing API returned no web page results");
            return [];
        }

        return bingResponse.WebPages.Value
            .Take(count)
            .Select((r, i) => new WebSearchResult(
                Title: r.Name ?? "Untitled",
                Url: r.Url ?? "",
                Snippet: TruncateSnippet(r.Snippet ?? "", 500),
                Position: i + 1))
            .ToList();
    }

    // === Scope-Guided Search (FR-10) ===

    /// <summary>
    /// Applies scope search guidance to the query by prepending guidance terms.
    /// If guidance contains specific domain/site references, they are prepended as free-text qualifiers
    /// to help Bing prioritize authoritative sources for the domain.
    /// </summary>
    private string ApplyScopeGuidance(string query)
    {
        if (string.IsNullOrWhiteSpace(_scopeSearchGuidance))
            return query;

        // Prepend scope guidance as a free-text qualifier.
        // Bing handles natural language well — prepending "Westlaw LexisNexis" or
        // "authoritative legal sources" naturally biases results toward those sources.
        return $"{_scopeSearchGuidance} {query}";
    }

    // === Citation Registration ===

    /// <summary>
    /// Registers each web search result as a citation in the shared <see cref="CitationContext"/>.
    /// Position-based confidence scoring: 1st result = 0.95, linear decay to 10th = 0.50.
    /// All web results use SourceType = "[External Source]" per ADR-015.
    /// </summary>
    private void RegisterCitations(List<WebSearchResult> results)
    {
        if (_citationContext is null || results.Count == 0)
            return;

        foreach (var result in results)
        {
            // Confidence: position 1 → 0.95, position N → max(0.50, 0.95 - (N-1)*0.05)
            var confidence = Math.Max(0.50, 0.95 - (result.Position - 1) * 0.05);

            // Register using CitationContext.AddCitation with web-specific fields.
            // ChunkId uses URL as the unique identifier for web results (no chunk index).
            // SourceType "web" signals the frontend to render globe icon + [External Source] badge (ADR-015).
            // SourceName carries the clean title (no [External Source] prefix — the badge is rendered by the frontend).
            _citationContext.AddCitation(
                chunkId: result.Url,
                sourceName: result.Title,
                pageNumber: null,
                excerpt: TruncateSnippet(result.Snippet, CitationContext.MaxExcerptLength),
                sourceType: "web",
                url: result.Url,
                snippet: TruncateSnippet(result.Snippet, CitationContext.MaxExcerptLength));
        }
    }

    // === Result Formatting ===

    /// <summary>
    /// Formats search results into an AI-readable text block with inline [N] markers.
    /// </summary>
    private static string FormatResults(
        List<WebSearchResult> results, string query, string? degradationNote = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Web search returned {results.Count} result(s).");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(degradationNote))
        {
            sb.AppendLine($"**Note**: {degradationNote}");
            sb.AppendLine();
        }

        sb.AppendLine("**Note**: These results are from external web sources and have not been verified against internal knowledge.");
        sb.AppendLine();

        foreach (var result in results)
        {
            sb.AppendLine($"[{result.Position}] [External Source] {result.Title} - {result.Url}");
            sb.AppendLine($"    {result.Snippet}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    // === Mock Fallback ===

    /// <summary>
    /// Generates mock search results for development and testing.
    /// Used when Bing API key is not configured or when concurrency limit is reached.
    /// </summary>
    private static List<WebSearchResult> GenerateMockResults(string query, int count)
    {
        var allMockResults = new List<WebSearchResult>
        {
            new(
                Title: "Understanding Legal Document Analysis Best Practices",
                Url: "https://www.example.com/legal-document-analysis-guide",
                Snippet: "A comprehensive guide to modern document analysis techniques including AI-assisted review, " +
                         "key clause extraction, and automated compliance checking for legal professionals.",
                Position: 1),
            new(
                Title: "Microsoft Graph API Documentation - SharePoint Embedded",
                Url: "https://learn.microsoft.com/en-us/graph/api/resources/sharepoint-embedded",
                Snippet: "Official documentation for SharePoint Embedded (SPE) APIs via Microsoft Graph, " +
                         "covering container management, file operations, and permission models.",
                Position: 2),
            new(
                Title: "Azure AI Services - Document Intelligence Overview",
                Url: "https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview",
                Snippet: "Azure AI Document Intelligence uses machine learning models to automate data extraction " +
                         "from documents, forms, and invoices with high accuracy.",
                Position: 3),
            new(
                Title: "Industry Trends in Legal Technology 2026",
                Url: "https://www.example.com/legaltech-trends-2026",
                Snippet: "Emerging trends in legal technology for 2026, including AI-powered contract review, " +
                         "predictive analytics for case outcomes, and integrated collaboration platforms.",
                Position: 4),
            new(
                Title: "Data Governance Best Practices for AI Systems",
                Url: "https://www.example.com/ai-data-governance-best-practices",
                Snippet: "Guidelines for implementing responsible data governance in AI-powered systems, " +
                         "covering data minimization, audit trails, and privacy-by-design principles.",
                Position: 5)
        };

        return allMockResults.Take(count).ToList();
    }

    // === Helper Methods ===

    /// <summary>
    /// Truncates a snippet to the specified maximum length, appending "..." if truncated.
    /// </summary>
    private static string TruncateSnippet(string snippet, int maxLength) =>
        snippet.Length > maxLength ? snippet[..maxLength] + "..." : snippet;

    // === Internal Types ===

    /// <summary>
    /// Internal record representing a single web search result.
    /// Used for both mock results and Bing API response mapping.
    /// </summary>
    private sealed record WebSearchResult(string Title, string Url, string Snippet, int Position);

    /// <summary>
    /// Bing Web Search v7 API response model (partial — only fields we need).
    /// </summary>
    private sealed class BingSearchResponse
    {
        [JsonPropertyName("webPages")]
        public BingWebPages? WebPages { get; set; }
    }

    /// <summary>
    /// Web pages section of the Bing response.
    /// </summary>
    private sealed class BingWebPages
    {
        [JsonPropertyName("value")]
        public List<BingWebPage>? Value { get; set; }
    }

    /// <summary>
    /// Individual web page result from Bing.
    /// </summary>
    private sealed class BingWebPage
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("snippet")]
        public string? Snippet { get; set; }
    }
}
