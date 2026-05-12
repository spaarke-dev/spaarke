using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Services.Ai.SemanticSearch;

/// <summary>
/// Implements hybrid semantic search using Azure AI Search with vector and keyword search.
/// Supports RRF (Reciprocal Rank Fusion), vector-only, and keyword-only search modes.
/// </summary>
/// <remarks>
/// <para>
/// Search pipeline:
/// <list type="number">
/// <item>Query preprocessing (IQueryPreprocessor - no-op for R1)</item>
/// <item>Generate query embedding via IOpenAiClient (with caching)</item>
/// <item>Build OData filter via SearchFilterBuilder</item>
/// <item>Execute search in Azure AI Search (hybrid/vector/keyword modes)</item>
/// <item>Result post-processing (IResultPostprocessor - no-op for R1)</item>
/// </list>
/// </para>
/// <para>
/// On embedding failure, falls back to keyword-only search with EMBEDDING_FALLBACK warning.
/// </para>
/// </remarks>
public sealed class SemanticSearchService : ISemanticSearchService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IQueryPreprocessor _queryPreprocessor;
    private readonly IResultPostprocessor _resultPostprocessor;
    private readonly IDocumentDataverseService _documentService;
    private readonly ILogger<SemanticSearchService> _logger;

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "knowledge-semantic-config";

    // Vector field name (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector3072";

    // Search fields for keyword queries
    // Note: parentEntityname (lowercase 'n') exists in index but not added to search fields yet
    private static readonly string[] SearchFields = ["content", "fileName", "knowledgeSourceName"];

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticSearchService"/> class.
    /// </summary>
    public SemanticSearchService(
        IKnowledgeDeploymentService deploymentService,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IQueryPreprocessor queryPreprocessor,
        IResultPostprocessor resultPostprocessor,
        IDocumentDataverseService documentService,
        ILogger<SemanticSearchService> logger)
    {
        _deploymentService = deploymentService;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _queryPreprocessor = queryPreprocessor;
        _resultPostprocessor = resultPostprocessor;
        _documentService = documentService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        var totalStopwatch = Stopwatch.StartNew();
        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();
        var warnings = new List<SearchWarning>();

        var hybridMode = request.Options?.HybridMode ?? HybridSearchMode.Rrf;
        var limit = request.Options?.Limit ?? 20;
        var offset = request.Options?.Offset ?? 0;
        var includeHighlights = request.Options?.IncludeHighlights ?? true;
        var minScore = request.Options?.MinScore ?? 0.0;

        // Associated-only mode: bypass Azure AI Search and query Dataverse directly so
        // just-uploaded documents appear immediately (no indexing lag).
        if (request.AssociatedOnly)
        {
            return await SearchAssociatedOnlyAsync(request, limit, offset, totalStopwatch, cancellationToken);
        }

        _logger.LogDebug(
            "Starting semantic search for tenant {TenantId}, mode={Mode}, query length={QueryLength}",
            tenantId, hybridMode, request.Query?.Length ?? 0);

        try
        {
            // Step 1: Query preprocessing (no-op for R1)
            var preprocessResult = await _queryPreprocessor.ProcessAsync(request, tenantId, cancellationToken);
            var processedRequest = preprocessResult.ProcessedRequest;

            // Step 2: Generate embedding for vector modes
            ReadOnlyMemory<float> queryEmbedding = default;
            var embeddingFailed = false;

            if (hybridMode != HybridSearchMode.KeywordOnly && !string.IsNullOrWhiteSpace(processedRequest.Query))
            {
                embeddingStopwatch.Start();
                try
                {
                    queryEmbedding = await GetEmbeddingWithCacheAsync(processedRequest.Query, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Embedding generation failed, falling back to keyword search");
                    embeddingFailed = true;
                    warnings.Add(new SearchWarning
                    {
                        Code = SearchWarningCode.EmbeddingFallback,
                        Message = "Embedding generation failed. Results from keyword search only."
                    });
                    hybridMode = HybridSearchMode.KeywordOnly;
                }
                embeddingStopwatch.Stop();
            }

            // Step 3: Get SearchClient for tenant
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Step 4: Build filter using SearchFilterBuilder
            var filter = SearchFilterBuilder.BuildFilter(
                tenantId,
                processedRequest.Scope,
                processedRequest.EntityType,
                processedRequest.EntityId,
                processedRequest.DocumentIds,
                processedRequest.Filters);

            // Step 5: Build search options
            searchStopwatch.Start();
            var searchOptions = BuildSearchOptions(
                hybridMode,
                queryEmbedding,
                filter,
                limit,
                offset,
                includeHighlights);

            // Step 6: Execute search
            var searchText = hybridMode != HybridSearchMode.VectorOnly && !string.IsNullOrWhiteSpace(processedRequest.Query)
                ? processedRequest.Query
                : "*";

            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                searchText, searchOptions, cancellationToken);
            var searchResults = response.Value;
            searchStopwatch.Stop();

            // Step 7: Process results
            var results = await ProcessSearchResultsAsync(
                searchResults,
                includeHighlights,
                cancellationToken);

            // The raw count from Azure AI Search is the number of CHUNKS matching the
            // filter (RAG-indexed docs are split into multiple chunks). After
            // ProcessSearchResultsAsync dedupes by documentId, the meaningful count is
            // the number of UNIQUE documents — that's what the UI should display.
            // (Previously: var totalResults = searchResults.TotalCount ?? results.Count
            //  which reported chunk count and caused "12 found / 2 shown" mismatches.)
            var totalResults = results.Count;

            // Step 7b: Enrich results with Dataverse metadata (createdBy, summary, tldr)
            results = await EnrichResultsWithDataverseMetadataAsync(results, cancellationToken);

            // Step 7c: Apply server-side score threshold so the total count reflects
            // what the UI will actually show. Before this, the count included low-relevance
            // hits the user couldn't see, causing the "68 found / 49 shown" mismatch.
            //
            // IMPORTANT: only apply threshold when there's an actual search query. When the
            // query is empty (Refresh button / initial load), Azure AI Search returns docs
            // by filter only and the scores are RRF/BM25 values in the 0.01-0.05 range
            // (no semantic intent to rank against). Applying a 0.5 threshold to those
            // wipes EVERYTHING, leaving the user with an empty grid for what should be a
            // "show me everything in scope" query.
            var hasQuery = !string.IsNullOrWhiteSpace(processedRequest.Query);
            if (minScore > 0.0 && hasQuery)
            {
                var beforeCount = results.Count;
                results = results.Where(r => r.CombinedScore >= minScore).ToList();
                totalResults = results.Count;
                _logger.LogDebug(
                    "Applied MinScore={MinScore} filter: {Before} → {After} results",
                    minScore, beforeCount, results.Count);
            }
            else if (minScore > 0.0 && !hasQuery)
            {
                _logger.LogDebug(
                    "Skipping MinScore={MinScore} filter — empty query (no semantic relevance to threshold)",
                    minScore);
            }

            // Step 8: Build applied filters summary
            var appliedFilters = BuildAppliedFilters(processedRequest, filter);

            // Step 9: Build response
            var searchResponse = new SemanticSearchResponse
            {
                Results = results,
                Metadata = new SearchMetadata
                {
                    TotalResults = totalResults,
                    ReturnedResults = results.Count,
                    SearchDurationMs = searchStopwatch.ElapsedMilliseconds,
                    EmbeddingDurationMs = embeddingStopwatch.ElapsedMilliseconds,
                    ExecutedMode = embeddingFailed ? HybridSearchMode.KeywordOnly : hybridMode,
                    AppliedFilters = appliedFilters,
                    Warnings = warnings.Count > 0 ? warnings : null
                }
            };

            // Step 10: Result post-processing (no-op for R1)
            var postprocessResult = await _resultPostprocessor.ProcessAsync(
                searchResponse, processedRequest, tenantId, cancellationToken);

            totalStopwatch.Stop();

            _logger.LogInformation(
                "Semantic search completed for tenant {TenantId}: {ResultCount}/{TotalCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms, mode={Mode})",
                tenantId, results.Count, totalResults, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds, hybridMode);

            return postprocessResult.ProcessedResponse;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during semantic search for tenant {TenantId}", tenantId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SemanticSearchCountResponse> CountAsync(
        SemanticSearchRequest request,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId, nameof(tenantId));

        _logger.LogDebug("Starting semantic search count for tenant {TenantId}", tenantId);

        try
        {
            // Get SearchClient for tenant
            var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

            // Build filter using SearchFilterBuilder
            var filter = SearchFilterBuilder.BuildFilter(
                tenantId,
                request.Scope,
                request.EntityType,
                request.EntityId,
                request.DocumentIds,
                request.Filters);

            // Build count-only search options
            var searchOptions = new Azure.Search.Documents.SearchOptions
            {
                Filter = filter,
                Size = 0, // We only want count
                IncludeTotalCount = true
            };

            // Execute search to get count
            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                "*", searchOptions, cancellationToken);
            var searchResults = response.Value;

            var count = searchResults.TotalCount ?? 0;

            _logger.LogInformation(
                "Semantic search count completed for tenant {TenantId}: {Count} documents",
                tenantId, count);

            return new SemanticSearchCountResponse
            {
                Count = count,
                AppliedFilters = BuildAppliedFilters(request, filter),
                Warnings = null
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during count for tenant {TenantId}", tenantId);
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Gets embedding with caching support.
    /// </summary>
    private async Task<ReadOnlyMemory<float>> GetEmbeddingWithCacheAsync(
        string text,
        CancellationToken cancellationToken)
    {
        // Try cache first
        var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(text, cancellationToken);
        if (cachedEmbedding.HasValue)
        {
            return cachedEmbedding.Value;
        }

        // Cache miss - generate and cache
        var embedding = await _openAiClient.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        await _embeddingCache.SetEmbeddingForContentAsync(text, embedding, cancellationToken);

        return embedding;
    }

    /// <summary>
    /// Builds Azure AI Search options based on hybrid mode and parameters.
    /// </summary>
    private static Azure.Search.Documents.SearchOptions BuildSearchOptions(
        string hybridMode,
        ReadOnlyMemory<float> queryEmbedding,
        string filter,
        int limit,
        int offset,
        bool includeHighlights)
    {
        var searchOptions = new Azure.Search.Documents.SearchOptions
        {
            Filter = filter,
            Size = limit,
            Skip = offset,
            IncludeTotalCount = true
        };

        // Configure based on mode
        var useVector = hybridMode != HybridSearchMode.KeywordOnly && queryEmbedding.Length > 0;
        var useKeyword = hybridMode != HybridSearchMode.VectorOnly;

        // Add vector search if needed
        if (useVector)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = limit * 2, // Retrieve more for RRF fusion
                        Fields = { VectorFieldName }
                    }
                }
            };
        }

        // Add keyword search fields
        if (useKeyword)
        {
            foreach (var field in SearchFields)
            {
                searchOptions.SearchFields.Add(field);
            }
        }

        // Enable semantic ranking for hybrid and keyword modes
        if (useKeyword)
        {
            searchOptions.QueryType = SearchQueryType.Semantic;
            searchOptions.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfigurationName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive)
            };
        }

        // Enable highlighting
        if (includeHighlights)
        {
            searchOptions.HighlightFields.Add("content");
        }

        return searchOptions;
    }

    /// <summary>
    /// Processes search results into response format.
    /// Deduplicates by documentId, keeping only the highest-scoring chunk per document.
    /// Normalizes semantic reranker scores from 0-4 range to 0-1 range.
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> ProcessSearchResultsAsync(
        SearchResults<KnowledgeDocument> searchResults,
        bool includeHighlights,
        CancellationToken cancellationToken)
    {
        // Dictionary to track best result per document (by documentId)
        var documentResults = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);

        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document == null) continue;

            var doc = result.Document;
            var rawScore = result.Score ?? 0;

            // Use semantic reranker score if available (0-4 range)
            if (result.SemanticSearch?.RerankerScore.HasValue == true)
            {
                rawScore = result.SemanticSearch.RerankerScore.Value;
            }

            // Normalize score to 0-1 range
            // Semantic reranker scores are typically 0-4, so divide by 4
            // Clamp to ensure we stay within 0-1 bounds
            var normalizedScore = Math.Clamp(rawScore / 4.0, 0.0, 1.0);

            var documentId = doc.DocumentId ?? doc.SpeFileId ?? Guid.NewGuid().ToString();

            // Check if we already have a result for this document
            if (documentResults.TryGetValue(documentId, out var existingResult))
            {
                // Keep the higher scoring result
                if (normalizedScore > existingResult.CombinedScore)
                {
                    documentResults[documentId] = CreateSearchResult(doc, normalizedScore, includeHighlights ? GetHighlights(result) : null);
                }
                // Optionally merge highlights from multiple chunks
                else if (includeHighlights && existingResult.Highlights != null)
                {
                    var newHighlights = GetHighlights(result);
                    if (newHighlights != null && newHighlights.Count > 0)
                    {
                        // Keep existing result but add unique highlights (up to 3 total)
                        var mergedHighlights = existingResult.Highlights
                            .Concat(newHighlights)
                            .Distinct()
                            .Take(3)
                            .ToList();
                        documentResults[documentId] = existingResult with { Highlights = mergedHighlights };
                    }
                }
            }
            else
            {
                // First result for this document
                documentResults[documentId] = CreateSearchResult(doc, normalizedScore, includeHighlights ? GetHighlights(result) : null);
            }
        }

        // Return results sorted by score descending
        return documentResults.Values
            .OrderByDescending(r => r.CombinedScore)
            .ToList();
    }

    /// <summary>
    /// Creates a SearchResult from a KnowledgeDocument.
    /// </summary>
    private static SearchResult CreateSearchResult(
        KnowledgeDocument doc,
        double normalizedScore,
        IReadOnlyList<string>? highlights)
    {
        return new SearchResult
        {
            DocumentId = doc.DocumentId,
            SpeFileId = doc.SpeFileId,
            Name = doc.FileName,
            DocumentType = doc.DocumentType,
            FileType = doc.FileType,
            CombinedScore = normalizedScore,
            Similarity = null, // Reserved for R2
            KeywordScore = null, // Reserved for R2
            Highlights = highlights,
            ParentEntityType = doc.ParentEntityType,
            ParentEntityId = doc.ParentEntityId,
            ParentEntityName = doc.ParentEntityName,
            FileUrl = null, // To be populated by endpoint if needed
            RecordUrl = null, // To be populated by endpoint if needed
            CreatedAt = doc.CreatedAt,
            UpdatedAt = doc.UpdatedAt
        };
    }

    /// <summary>
    /// Extracts highlights from search result.
    /// </summary>
    private static IReadOnlyList<string>? GetHighlights(Azure.Search.Documents.Models.SearchResult<KnowledgeDocument> result)
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

    /// <summary>
    /// Enriches search results with Dataverse metadata (createdBy, summary, tldr).
    /// Performs a post-search lookup for each result with a documentId.
    /// </summary>
    private async Task<IReadOnlyList<SearchResult>> EnrichResultsWithDataverseMetadataAsync(
        IReadOnlyList<SearchResult> results,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0) return results;

        var enriched = new List<SearchResult>(results.Count);

        foreach (var result in results)
        {
            if (string.IsNullOrEmpty(result.DocumentId))
            {
                enriched.Add(result);
                continue;
            }

            try
            {
                var doc = await _documentService.GetDocumentAsync(result.DocumentId, cancellationToken);
                if (doc != null)
                {
                    enriched.Add(result with
                    {
                        CreatedBy = doc.CreatedBy,
                        Summary = doc.Summary,
                        Tldr = doc.Tldr,
                    });
                }
                else
                {
                    enriched.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enrich result {DocumentId} with Dataverse metadata", result.DocumentId);
                enriched.Add(result);
            }
        }

        return enriched;
    }

    /// <summary>
    /// Builds applied filters summary for response.
    /// </summary>
    private static AppliedFilters BuildAppliedFilters(SemanticSearchRequest request, string filterExpression)
    {
        return new AppliedFilters
        {
            Scope = request.Scope,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            DocumentIdCount = request.DocumentIds?.Count,
            EntityTypes = request.Filters?.EntityTypes,
            DocumentTypes = request.Filters?.DocumentTypes,
            FileTypes = request.Filters?.FileTypes,
            Tags = request.Filters?.Tags,
            DateRange = request.Filters?.DateRange is not null
            ? new AppliedDateRange { From = request.Filters.DateRange.From, To = request.Filters.DateRange.To }
            : null
        };
    }

    #endregion

    #region Associated-Only path (Dataverse-direct, no AI Search)

    /// <summary>
    /// Queries Dataverse directly for documents associated with the parent record via
    /// the relevant lookup (<c>_sprk_matter_value</c> / <c>_sprk_project_value</c> /
    /// <c>_sprk_invoice_value</c>) and returns them as <see cref="SearchResult"/>s.
    /// </summary>
    /// <remarks>
    /// This path bypasses Azure AI Search so just-uploaded documents are visible
    /// immediately. The optional <c>Query</c> is applied as a case-insensitive substring
    /// match against the document name. Results carry <c>CombinedScore=0.0</c> because
    /// there is no semantic relevance to rank by — sort order is recency DESC then name.
    /// </remarks>
    private async Task<SemanticSearchResponse> SearchAssociatedOnlyAsync(
        SemanticSearchRequest request,
        int limit,
        int offset,
        Stopwatch totalStopwatch,
        CancellationToken cancellationToken)
    {
        var queryStopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.EntityId))
        {
            throw new ArgumentException(
                "associatedOnly=true requires scope=entity with valid entityType and entityId.",
                nameof(request));
        }

        if (!Guid.TryParse(request.EntityId, out var parentGuid))
        {
            throw new ArgumentException($"entityId '{request.EntityId}' is not a valid GUID.", nameof(request));
        }

        // Dispatch on parent entity type — these are the lookup fields the upload wizard sets.
        IEnumerable<DocumentEntity> documents = request.EntityType.ToLowerInvariant() switch
        {
            "matter"  => await _documentService.GetDocumentsByMatterAsync(parentGuid, null, cancellationToken),
            "project" => await _documentService.GetDocumentsByProjectAsync(parentGuid, null, cancellationToken),
            "invoice" => await _documentService.GetDocumentsByInvoiceAsync(parentGuid, null, cancellationToken),
            _ => throw new ArgumentException(
                $"associatedOnly is not supported for entityType '{request.EntityType}' " +
                "(supported: matter, project, invoice).",
                nameof(request))
        };

        // Optional client-supplied filters: substring name match, file-type filter, date range.
        var queryText = request.Query?.Trim();
        if (!string.IsNullOrEmpty(queryText))
        {
            documents = documents.Where(d =>
                (d.Name?.Contains(queryText, StringComparison.OrdinalIgnoreCase) ?? false)
                || (d.FileName?.Contains(queryText, StringComparison.OrdinalIgnoreCase) ?? false)
                || (d.DocumentType?.Contains(queryText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (request.Filters?.FileTypes is { Count: > 0 } fileTypes)
        {
            var fileTypeSet = new HashSet<string>(fileTypes, StringComparer.OrdinalIgnoreCase);
            documents = documents.Where(d => d.FileName is not null
                && fileTypeSet.Contains(System.IO.Path.GetExtension(d.FileName).TrimStart('.')));
        }

        if (request.Filters?.DocumentTypes is { Count: > 0 } docTypes)
        {
            var docTypeSet = new HashSet<string>(docTypes, StringComparer.OrdinalIgnoreCase);
            documents = documents.Where(d => d.DocumentType is not null && docTypeSet.Contains(d.DocumentType));
        }

        if (request.Filters?.DateRange is { } dateRange)
        {
            if (dateRange.From is { } from)  documents = documents.Where(d => d.CreatedOn >= from.UtcDateTime);
            if (dateRange.To   is { } to)    documents = documents.Where(d => d.CreatedOn <= to.UtcDateTime);
        }

        // Sort: most recent first, then name ascending for stable ordering.
        var sortedAll = documents
            .OrderByDescending(d => d.CreatedOn)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pageResults = sortedAll
            .Skip(offset)
            .Take(limit)
            .Select(d => MapDocumentEntityToSearchResult(d, request.EntityType!))
            .ToList();

        queryStopwatch.Stop();
        totalStopwatch.Stop();

        _logger.LogInformation(
            "Associated-only search for {EntityType} {EntityId}: {Total} matched, {Returned} returned in {Ms}ms",
            request.EntityType, request.EntityId, sortedAll.Count, pageResults.Count, totalStopwatch.ElapsedMilliseconds);

        return new SemanticSearchResponse
        {
            Results = pageResults,
            Metadata = new SearchMetadata
            {
                TotalResults = sortedAll.Count,
                ReturnedResults = pageResults.Count,
                SearchDurationMs = queryStopwatch.ElapsedMilliseconds,
                EmbeddingDurationMs = 0,
                ExecutedMode = "associatedOnly",
                AppliedFilters = new AppliedFilters
                {
                    Scope = request.Scope,
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    DocumentTypes = request.Filters?.DocumentTypes,
                    FileTypes = request.Filters?.FileTypes,
                    DateRange = request.Filters?.DateRange is not null
                        ? new AppliedDateRange { From = request.Filters.DateRange.From, To = request.Filters.DateRange.To }
                        : null
                },
                Warnings = null
            }
        };
    }

    /// <summary>
    /// Maps a Dataverse <see cref="DocumentEntity"/> to a <see cref="SearchResult"/>
    /// shaped like an AI Search response, so the client can treat both paths uniformly.
    /// </summary>
    private static SearchResult MapDocumentEntityToSearchResult(DocumentEntity doc, string parentEntityType)
    {
        // Parent lookup — the entity type dictates which property holds the FK.
        var (parentId, parentName) = parentEntityType.ToLowerInvariant() switch
        {
            "matter"  => (doc.MatterId,  doc.MatterName),
            "project" => (doc.ProjectId, doc.ProjectName),
            "invoice" => (doc.InvoiceId, doc.InvoiceName),
            _ => (null, null)
        };

        var fileExt = doc.FileName is not null
            ? System.IO.Path.GetExtension(doc.FileName).TrimStart('.').ToLowerInvariant()
            : null;

        return new SearchResult
        {
            DocumentId = doc.Id,
            SpeFileId = doc.GraphItemId,
            Name = doc.Name,
            DocumentType = doc.DocumentType,
            FileType = string.IsNullOrEmpty(fileExt) ? null : fileExt,
            // No semantic score on the Dataverse-direct path — the client should hide
            // any "relevance" UI when ExecutedMode == "associatedOnly".
            CombinedScore = 0.0,
            ParentEntityType = parentEntityType,
            ParentEntityId = parentId,
            ParentEntityName = parentName,
            CreatedAt = doc.CreatedOn,
            UpdatedAt = doc.ModifiedOn,
            CreatedBy = doc.CreatedBy,
            Summary = doc.Summary,
            Tldr = doc.Tldr
        };
    }

    #endregion
}
