using System.ComponentModel;
using System.Text;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing web search capabilities for the SprkChatAgent.
///
/// Exposes a single search method:
///   - <see cref="SearchWebAsync"/> — searches the web via Azure Bing Search API and returns
///     results marked as external content for data governance (ADR-015).
///
/// TODO: PH-088-A (post-R2) — SearchWebAsync returns mock results until Bing API is provisioned.
///
/// When the Azure Bing Search API is provisioned, this class will:
///   - Accept an HttpClient pre-configured with the Bing API key (Ocp-Apim-Subscription-Key header)
///   - Call GET https://api.bing.microsoft.com/v7.0/search with query and count parameters
///   - Deserialize the WebPages response and format results
///
/// The Bing API key is sourced from Key Vault configuration and is NEVER exposed to client-side code.
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="Microsoft.Extensions.AI.AIFunction"/>
/// objects via <see cref="Microsoft.Extensions.AI.AIFunctionFactory.Create"/>.
///
/// ADR-010: 0 additional DI registrations — factory-instantiated only.
/// ADR-013: AI tools use AIFunctionFactory.Create pattern.
/// ADR-015: Results are marked as [External Source]; MUST NOT log full result bodies.
/// ADR-016: Web searches count toward AI endpoint rate limits; maxResults bounded at 10.
/// </summary>
public sealed class WebSearchTools
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new <see cref="WebSearchTools"/> instance.
    /// </summary>
    /// <param name="logger">Logger for operation metadata (ADR-015: no result content in logs).</param>
    public WebSearchTools(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// TODO: PH-088-A (post-R2) — Returns mock results until Bing API is provisioned.
    /// </summary>
    /// <param name="query">Web search query</param>
    /// <param name="maxResults">Maximum number of results to return (default: 5, cap: 10).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted string of web search results, each prefixed with [External Source].</returns>
    [Description("Search the web for information relevant to the user's query. " +
                 "Use this when the question cannot be answered from internal documents alone.")]
    public Task<string> SearchWebAsync(
        [Description("Web search query")] string query,
        [Description("Maximum number of results to return (default: 5, cap: 10)")]
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        var count = Math.Clamp(maxResults, 1, 10);

        // ADR-015: Log query length and result count only — never log the query text or result bodies
        _logger.LogInformation(
            "SearchWeb starting — queryLen={QueryLen}, maxResults={MaxResults}",
            query.Length, count);

        // TODO: PH-088-A (post-R2) — Replace mock results with Bing API call when provisioned.
        //
        // When Bing API is provisioned, replace this block with:
        //   1. HttpClient GET to https://api.bing.microsoft.com/v7.0/search?q={query}&count={count}&mkt=en-US
        //   2. Header: Ocp-Apim-Subscription-Key = {key from Key Vault}
        //   3. Deserialize BingWebSearchResponse.WebPages.Value[]
        //   4. Format each result with [External Source] prefix
        var results = GenerateMockResults(query, count);

        _logger.LogInformation(
            "SearchWeb completed — queryLen={QueryLen}, resultCount={ResultCount}",
            query.Length, results.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"Web search returned {results.Count} result(s) for query: \"{query}\"");
        sb.AppendLine();
        sb.AppendLine("**Note**: These results are from external web sources and have not been verified against internal knowledge.");
        sb.AppendLine();

        foreach (var (result, idx) in results.Select((r, i) => (r, i + 1)))
        {
            sb.AppendLine($"[{idx}] [External Source] {result.Title} - {result.Url}");
            sb.AppendLine($"    {result.Snippet}");
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString().TrimEnd());
    }

    // === Private helpers ===

    /// <summary>
    /// Generates mock search results for development and testing.
    /// Returns up to <paramref name="count"/> realistic-looking results.
    ///
    /// TODO: PH-088-A (post-R2) — Remove when real Bing API call replaces mock results.
    /// </summary>
    private static List<MockSearchResult> GenerateMockResults(string query, int count)
    {
        var allMockResults = new List<MockSearchResult>
        {
            new(
                Title: "Understanding Legal Document Analysis Best Practices",
                Url: "https://www.example.com/legal-document-analysis-guide",
                Snippet: "A comprehensive guide to modern document analysis techniques including AI-assisted review, " +
                         "key clause extraction, and automated compliance checking for legal professionals."),
            new(
                Title: "Microsoft Graph API Documentation - SharePoint Embedded",
                Url: "https://learn.microsoft.com/en-us/graph/api/resources/sharepoint-embedded",
                Snippet: "Official documentation for SharePoint Embedded (SPE) APIs via Microsoft Graph, " +
                         "covering container management, file operations, and permission models."),
            new(
                Title: "Azure AI Services - Document Intelligence Overview",
                Url: "https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/overview",
                Snippet: "Azure AI Document Intelligence uses machine learning models to automate data extraction " +
                         "from documents, forms, and invoices with high accuracy."),
            new(
                Title: "Industry Trends in Legal Technology 2026",
                Url: "https://www.example.com/legaltech-trends-2026",
                Snippet: "Emerging trends in legal technology for 2026, including AI-powered contract review, " +
                         "predictive analytics for case outcomes, and integrated collaboration platforms."),
            new(
                Title: "Data Governance Best Practices for AI Systems",
                Url: "https://www.example.com/ai-data-governance-best-practices",
                Snippet: "Guidelines for implementing responsible data governance in AI-powered systems, " +
                         "covering data minimization, audit trails, and privacy-by-design principles.")
        };

        return allMockResults.Take(count).ToList();
    }

    /// <summary>
    /// Internal record representing a single web search result.
    /// Used for both mock results and future Bing API response mapping.
    /// </summary>
    private sealed record MockSearchResult(string Title, string Url, string Snippet);
}
