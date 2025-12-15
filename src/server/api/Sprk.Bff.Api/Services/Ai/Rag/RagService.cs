using System.Diagnostics;
using System.Text;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Rag;

/// <summary>
/// RAG service implementation using Azure AI Search for hybrid search.
/// Supports keyword + vector search with semantic reranking.
/// Follows ADR-013 (AI Architecture).
/// </summary>
public class RagService : IRagService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly SearchClient _searchClient;
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<RagService> _logger;

    // Vector field configuration (must match index schema)
    private const string VectorFieldName = "contentVector";
    private const int VectorDimensions = 1536;
    private const string SemanticConfigName = "default";

    // Token estimation: ~4 chars per token for English text
    private const double CharsPerToken = 4.0;

    public RagService(
        IOpenAiClient openAiClient,
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<RagService> logger)
    {
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create search client
        if (string.IsNullOrEmpty(_options.AiSearchEndpoint) || string.IsNullOrEmpty(_options.AiSearchKey))
        {
            throw new InvalidOperationException(
                "Azure AI Search endpoint and key must be configured for RAG service. " +
                "Set DocumentIntelligence:AiSearchEndpoint and DocumentIntelligence:AiSearchKey.");
        }

        var endpoint = new Uri(_options.AiSearchEndpoint);
        var credential = new AzureKeyCredential(_options.AiSearchKey);
        _searchClient = new SearchClient(endpoint, _options.KnowledgeIndexName, credential);

        _logger.LogInformation(
            "RagService initialized with index {IndexName} at {Endpoint}",
            _options.KnowledgeIndexName, _options.AiSearchEndpoint);
    }

    /// <inheritdoc />
    public async Task<RagSearchResult> SearchAsync(
        RagSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrEmpty(request.Query);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "RAG search starting: Query={Query}, Mode={Mode}, CustomerId={CustomerId}, Top={Top}",
            request.Query.Length > 50 ? request.Query[..50] + "..." : request.Query,
            request.Mode,
            request.CustomerId,
            request.Top);

        try
        {
            // Generate embedding for vector search (if needed)
            float[]? queryVector = null;
            int? embeddingTokens = null;

            if (request.Mode is SearchMode.Vector or SearchMode.Hybrid)
            {
                queryVector = await GenerateEmbeddingAsync(request.Query, cancellationToken);
                embeddingTokens = EstimateTokens(request.Query);
            }

            // Build search options
            var searchOptions = BuildSearchOptions(request, queryVector);

            // Execute search
            var searchResults = await _searchClient.SearchAsync<KnowledgeChunkDocument>(
                request.Mode == SearchMode.Vector ? null : request.Query,
                searchOptions,
                cancellationToken);

            // Process results
            var hits = new List<RagSearchHit>();
            long totalCount = 0;

            await foreach (var result in searchResults.Value.GetResultsAsync())
            {
                totalCount++;

                if (result.Document == null) continue;

                // Apply minimum score filter
                var score = result.Score ?? 0;
                if (score < request.MinScore) continue;

                hits.Add(MapToHit(result));
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "RAG search completed: {HitCount} hits (total {TotalCount}) in {DurationMs}ms",
                hits.Count, totalCount, stopwatch.ElapsedMilliseconds);

            return RagSearchResult.Ok(
                hits.ToArray(),
                (int)totalCount,
                stopwatch.ElapsedMilliseconds,
                embeddingTokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "RAG search failed after {DurationMs}ms: {Error}",
                stopwatch.ElapsedMilliseconds, ex.Message);

            return RagSearchResult.Fail(ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc />
    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        return await _openAiClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<GroundedContext> GetGroundedContextAsync(
        string query,
        Guid customerId,
        Guid[]? knowledgeSourceIds = null,
        int maxChunks = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Getting grounded context: Query={Query}, CustomerId={CustomerId}, MaxChunks={MaxChunks}",
            query.Length > 50 ? query[..50] + "..." : query,
            customerId,
            maxChunks);

        try
        {
            // Search for relevant chunks
            var searchResult = await SearchAsync(new RagSearchRequest
            {
                Query = query,
                CustomerId = customerId,
                KnowledgeSourceIds = knowledgeSourceIds,
                Top = maxChunks,
                MinScore = 0.6, // Higher threshold for grounding
                Mode = SearchMode.Hybrid,
                UseSemanticReranking = true,
                IncludePublic = true
            }, cancellationToken);

            stopwatch.Stop();

            if (!searchResult.Success || searchResult.Results.Length == 0)
            {
                _logger.LogDebug(
                    "No relevant context found for grounding in {DurationMs}ms",
                    stopwatch.ElapsedMilliseconds);

                return GroundedContext.Empty(stopwatch.ElapsedMilliseconds);
            }

            // Format context for prompt injection
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("### Relevant Context from Knowledge Base ###");
            contextBuilder.AppendLine();

            var sources = new Dictionary<string, ContextSource>();

            foreach (var hit in searchResult.Results)
            {
                // Add chunk to context
                contextBuilder.AppendLine($"[Source: {hit.DocumentTitle ?? hit.DocumentFileName ?? "Unknown"}]");
                contextBuilder.AppendLine(hit.Content);
                contextBuilder.AppendLine();

                // Track sources for attribution
                var sourceKey = hit.DocumentId ?? hit.Id;
                if (!sources.TryGetValue(sourceKey, out var source))
                {
                    source = new ContextSource
                    {
                        DocumentId = hit.DocumentId,
                        DocumentTitle = hit.DocumentTitle,
                        DocumentFileName = hit.DocumentFileName,
                        KnowledgeSourceId = hit.KnowledgeSourceId,
                        KnowledgeType = hit.KnowledgeType,
                        ChunkIndices = [hit.ChunkIndex]
                    };
                    sources[sourceKey] = source;
                }
                else
                {
                    // Add chunk index to existing source
                    sources[sourceKey] = source with
                    {
                        ChunkIndices = source.ChunkIndices.Append(hit.ChunkIndex).ToArray()
                    };
                }
            }

            var contextText = contextBuilder.ToString();
            var estimatedTokens = EstimateTokens(contextText);

            _logger.LogInformation(
                "Grounded context retrieved: {ChunkCount} chunks, {SourceCount} sources, ~{Tokens} tokens in {DurationMs}ms",
                searchResult.Results.Length,
                sources.Count,
                estimatedTokens,
                stopwatch.ElapsedMilliseconds);

            return new GroundedContext
            {
                ContextText = contextText,
                Sources = sources.Values.ToArray(),
                ChunkCount = searchResult.Results.Length,
                EstimatedTokens = estimatedTokens,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.LogError(ex,
                "Failed to get grounded context after {DurationMs}ms: {Error}",
                stopwatch.ElapsedMilliseconds, ex.Message);

            return GroundedContext.Empty(stopwatch.ElapsedMilliseconds);
        }
    }

    private SearchOptions BuildSearchOptions(RagSearchRequest request, float[]? queryVector)
    {
        var options = new SearchOptions
        {
            Size = request.Top,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = request.UseSemanticReranking
                ? new SemanticSearchOptions
                {
                    SemanticConfigurationName = SemanticConfigName,
                    QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                    QueryAnswer = new QueryAnswer(QueryAnswerType.None)
                }
                : null
        };

        // Add fields to retrieve
        options.Select.Add("id");
        options.Select.Add("knowledgeSourceId");
        options.Select.Add("documentId");
        options.Select.Add("documentTitle");
        options.Select.Add("documentFileName");
        options.Select.Add("chunkIndex");
        options.Select.Add("content");
        options.Select.Add("knowledgeType");
        options.Select.Add("category");
        options.Select.Add("tags");
        options.Select.Add("customerId");

        // Build filter for tenant isolation
        var filters = new List<string>();

        // Customer filter (tenant isolation)
        // Include customer's content OR public content (null customerId = Spaarke shared)
        if (request.IncludePublic)
        {
            filters.Add($"(customerId eq '{request.CustomerId}' or customerId eq null)");
        }
        else
        {
            filters.Add($"customerId eq '{request.CustomerId}'");
        }

        // Knowledge source filter
        if (request.KnowledgeSourceIds?.Length > 0)
        {
            var sourceFilters = request.KnowledgeSourceIds
                .Select(id => $"knowledgeSourceId eq '{id}'");
            filters.Add($"({string.Join(" or ", sourceFilters)})");
        }

        // Knowledge type filter
        if (!string.IsNullOrEmpty(request.KnowledgeType))
        {
            filters.Add($"knowledgeType eq '{request.KnowledgeType}'");
        }

        // Category filter
        if (!string.IsNullOrEmpty(request.Category))
        {
            filters.Add($"category eq '{request.Category}'");
        }

        // Tag filter (any match)
        if (request.Tags?.Length > 0)
        {
            var tagFilters = request.Tags.Select(t => $"tags/any(tag: tag eq '{t}')");
            filters.Add($"({string.Join(" or ", tagFilters)})");
        }

        if (filters.Count > 0)
        {
            options.Filter = string.Join(" and ", filters);
        }

        // Add vector search (if applicable)
        if (queryVector != null && request.Mode is SearchMode.Vector or SearchMode.Hybrid)
        {
            var vectorQuery = new VectorizedQuery(queryVector)
            {
                KNearestNeighborsCount = request.Top,
                Fields = { VectorFieldName }
            };
            options.VectorSearch = new VectorSearchOptions
            {
                Queries = { vectorQuery }
            };
        }

        _logger.LogDebug(
            "Search options built: Filter={Filter}, Mode={Mode}, Semantic={Semantic}",
            options.Filter,
            request.Mode,
            request.UseSemanticReranking);

        return options;
    }

    private static RagSearchHit MapToHit(SearchResult<KnowledgeChunkDocument> result)
    {
        var doc = result.Document;
        return new RagSearchHit
        {
            Id = doc.Id,
            KnowledgeSourceId = doc.KnowledgeSourceId,
            DocumentId = doc.DocumentId,
            DocumentTitle = doc.DocumentTitle,
            DocumentFileName = doc.DocumentFileName,
            ChunkIndex = doc.ChunkIndex,
            Content = doc.Content ?? string.Empty,
            Score = result.Score ?? 0,
            RerankerScore = result.SemanticSearch?.RerankerScore,
            KnowledgeType = doc.KnowledgeType,
            Category = doc.Category,
            Tags = doc.Tags,
            IsPublic = doc.CustomerId == null
        };
    }

    private static int EstimateTokens(string text)
    {
        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }
}

/// <summary>
/// Document model for Azure AI Search knowledge index.
/// Must match the index schema in spaarke-knowledge-shared-index.json.
/// </summary>
internal class KnowledgeChunkDocument
{
    public string Id { get; set; } = string.Empty;
    public Guid KnowledgeSourceId { get; set; }
    public string? DocumentId { get; set; }
    public string? DocumentTitle { get; set; }
    public string? DocumentFileName { get; set; }
    public int ChunkIndex { get; set; }
    public string? Content { get; set; }
    public string? KnowledgeType { get; set; }
    public string? Category { get; set; }
    public string[]? Tags { get; set; }
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
