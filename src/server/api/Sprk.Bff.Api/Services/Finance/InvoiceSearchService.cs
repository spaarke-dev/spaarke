using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Semantic search service for invoices using Azure AI Search.
/// Performs hybrid search with vector embeddings and semantic reranking.
/// </summary>
/// <remarks>
/// Follows ADR-013: AI via BFF API (not separate service).
/// Index schema defined in infrastructure/ai-search/invoice-index-schema.json.
/// </remarks>
public interface IInvoiceSearchService
{
    /// <summary>
    /// Search invoices using semantic search with optional matter filtering.
    /// </summary>
    /// <param name="query">Search query text (required).</param>
    /// <param name="matterId">Optional matter ID filter.</param>
    /// <param name="top">Maximum number of results (default 10, max 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results with scores and highlights.</returns>
    Task<InvoiceSearchResponse> SearchAsync(
        string query,
        Guid? matterId = null,
        int top = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of invoice semantic search.
/// </summary>
public class InvoiceSearchService : IInvoiceSearchService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly ILogger<InvoiceSearchService> _logger;

    // Index name (MVP: single index, production: per-tenant)
    private const string IndexName = "spaarke-invoices-dev";

    // Vector field name (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector";

    // Semantic configuration name from schema
    private const string SemanticConfigName = "invoice-semantic";

    // Search fields for keyword queries
    private static readonly string[] SearchFields = ["content", "vendorName", "invoiceNumber"];

    public InvoiceSearchService(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        ILogger<InvoiceSearchService> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InvoiceSearchResponse> SearchAsync(
        string query,
        Guid? matterId = null,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query, nameof(query));

        if (top < 1 || top > 50)
        {
            throw new ArgumentException("Top must be between 1 and 50", nameof(top));
        }

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Starting invoice search: query length={QueryLength}, matterId={MatterId}, top={Top}",
            query.Length, matterId, top);

        try
        {
            // Step 1: Generate embedding for query
            ReadOnlyMemory<float> queryEmbedding;
            try
            {
                queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(
                    query,
                    model: "text-embedding-3-large",
                    dimensions: 3072,
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Generated query embedding: {Dimensions} dimensions", queryEmbedding.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding for query: {Error}", ex.Message);
                throw new InvalidOperationException("Failed to generate query embedding. Search cannot proceed.", ex);
            }

            // Step 2: Get SearchClient for the index
            var searchClient = _searchIndexClient.GetSearchClient(IndexName);

            // Step 3: Build filter expression
            var filter = BuildFilter(matterId);

            // Step 4: Build search options (hybrid search with semantic reranking)
            var searchOptions = BuildSearchOptions(queryEmbedding, filter, top);

            // Step 5: Execute search
            var response = await searchClient.SearchAsync<InvoiceSearchDocument>(
                query, searchOptions, cancellationToken);
            var searchResults = response.Value;

            // Step 6: Process results
            var results = await ProcessSearchResultsAsync(searchResults, cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Invoice search completed: {ResultCount} results in {Duration}ms",
                results.Count, stopwatch.ElapsedMilliseconds);

            return new InvoiceSearchResponse
            {
                Results = results,
                TotalCount = searchResults.TotalCount ?? results.Count,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search request failed: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Build OData filter expression for matter filtering.
    /// </summary>
    private static string? BuildFilter(Guid? matterId)
    {
        if (matterId.HasValue)
        {
            return $"matterId eq '{matterId.Value}'";
        }

        return null;
    }

    /// <summary>
    /// Build search options for hybrid search with vector + semantic reranking.
    /// </summary>
    private static Azure.Search.Documents.SearchOptions BuildSearchOptions(
        ReadOnlyMemory<float> queryEmbedding,
        string? filter,
        int top)
    {
        var searchOptions = new Azure.Search.Documents.SearchOptions
        {
            Filter = filter,
            Size = top,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Semantic
        };

        // Add vector search
        searchOptions.VectorSearch = new VectorSearchOptions
        {
            Queries =
            {
                new VectorizedQuery(queryEmbedding)
                {
                    KNearestNeighborsCount = top * 2, // Retrieve more for fusion
                    Fields = { VectorFieldName }
                }
            }
        };

        // Add keyword search fields
        foreach (var field in SearchFields)
        {
            searchOptions.SearchFields.Add(field);
        }

        // Enable semantic ranking
        searchOptions.SemanticSearch = new SemanticSearchOptions
        {
            SemanticConfigurationName = SemanticConfigName,
            QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
        };

        // Enable highlighting on content field
        searchOptions.HighlightFields.Add("content");

        return searchOptions;
    }

    /// <summary>
    /// Process search results into response format.
    /// Groups by invoiceId and keeps the highest-scoring chunk per invoice.
    /// </summary>
    private async Task<IReadOnlyList<InvoiceSearchResultItem>> ProcessSearchResultsAsync(
        SearchResults<InvoiceSearchDocument> searchResults,
        CancellationToken cancellationToken)
    {
        // Dictionary to track best result per invoice
        var invoiceResults = new Dictionary<string, InvoiceSearchResultItem>(StringComparer.OrdinalIgnoreCase);

        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document == null) continue;

            var doc = result.Document;
            var score = result.Score ?? 0;

            // Use semantic reranker score if available (0-4 range)
            if (result.SemanticSearch?.RerankerScore.HasValue == true)
            {
                score = result.SemanticSearch.RerankerScore.Value;
            }

            // Normalize score to 0-1 range
            var normalizedScore = Math.Clamp(score / 4.0, 0.0, 1.0);

            var invoiceId = doc.InvoiceId;

            // Extract highlights
            var highlights = GetHighlights(result);

            // Check if we already have a result for this invoice
            if (invoiceResults.TryGetValue(invoiceId, out var existingResult))
            {
                // Keep the higher scoring result
                if (normalizedScore > existingResult.Score)
                {
                    invoiceResults[invoiceId] = CreateSearchResultItem(doc, normalizedScore, highlights);
                }
                // Merge highlights if score is not higher
                else if (highlights != null && highlights.Count > 0 && existingResult.Highlights != null)
                {
                    var mergedHighlights = existingResult.Highlights
                        .Concat(highlights)
                        .Distinct()
                        .Take(3)
                        .ToList();
                    invoiceResults[invoiceId] = existingResult with { Highlights = mergedHighlights };
                }
            }
            else
            {
                // First result for this invoice
                invoiceResults[invoiceId] = CreateSearchResultItem(doc, normalizedScore, highlights);
            }
        }

        // Return results sorted by score descending
        return invoiceResults.Values
            .OrderByDescending(r => r.Score)
            .ToList();
    }

    /// <summary>
    /// Create a search result item from a document.
    /// </summary>
    private static InvoiceSearchResultItem CreateSearchResultItem(
        InvoiceSearchDocument doc,
        double score,
        IReadOnlyList<string>? highlights)
    {
        return new InvoiceSearchResultItem
        {
            InvoiceId = Guid.Parse(doc.InvoiceId),
            DocumentId = Guid.Parse(doc.DocumentId),
            Score = score,
            InvoiceNumber = doc.InvoiceNumber,
            VendorName = doc.VendorName,
            TotalAmount = doc.TotalAmount,
            Currency = doc.Currency,
            InvoiceDate = doc.InvoiceDate,
            Highlights = highlights
        };
    }

    /// <summary>
    /// Extract highlights from search result.
    /// </summary>
    private static IReadOnlyList<string>? GetHighlights(Azure.Search.Documents.Models.SearchResult<InvoiceSearchDocument> result)
    {
        // Try regular highlights first
        if (result.Highlights?.TryGetValue("content", out var highlights) == true)
        {
            return highlights.ToList();
        }

        // Try semantic captions if available
        if (result.SemanticSearch?.Captions?.Count > 0)
        {
            return result.SemanticSearch.Captions
                .Select(c => c.Highlights ?? c.Text)
                .Where(h => !string.IsNullOrEmpty(h))
                .ToList()!;
        }

        return null;
    }
}

/// <summary>
/// Document model matching the invoice search index schema.
/// </summary>
internal class InvoiceSearchDocument
{
    public string Id { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string InvoiceId { get; set; } = null!;
    public string DocumentId { get; set; } = null!;
    public string? MatterId { get; set; }
    public string? VendorName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTimeOffset? InvoiceDate { get; set; }
    public double? TotalAmount { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Response for invoice search operations.
/// </summary>
public record InvoiceSearchResponse
{
    /// <summary>Search results.</summary>
    public IReadOnlyList<InvoiceSearchResultItem> Results { get; init; } = [];

    /// <summary>Total count of matching results.</summary>
    public long TotalCount { get; init; }

    /// <summary>Search duration in milliseconds.</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Individual invoice search result item.
/// </summary>
public record InvoiceSearchResultItem
{
    /// <summary>Invoice ID from Dataverse.</summary>
    public Guid InvoiceId { get; init; }

    /// <summary>Document ID from Dataverse.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Relevance score (0-1, higher is better).</summary>
    public double Score { get; init; }

    /// <summary>Invoice number.</summary>
    public string? InvoiceNumber { get; init; }

    /// <summary>Vendor name.</summary>
    public string? VendorName { get; init; }

    /// <summary>Total amount.</summary>
    public double? TotalAmount { get; init; }

    /// <summary>Currency code.</summary>
    public string? Currency { get; init; }

    /// <summary>Invoice date.</summary>
    public DateTimeOffset? InvoiceDate { get; init; }

    /// <summary>Text highlights from search.</summary>
    public IReadOnlyList<string>? Highlights { get; init; }
}
