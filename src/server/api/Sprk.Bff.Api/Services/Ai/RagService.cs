using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Implements hybrid RAG search combining keyword search, vector search, and semantic ranking.
/// Integrates with IKnowledgeDeploymentService for multi-tenant index routing.
/// </summary>
/// <remarks>
/// Hybrid search pipeline:
/// 1. Generate embedding for query (with optional caching)
/// 2. Build hybrid query combining:
///    - Full-text keyword search on content field
///    - Vector search on contentVector field (cosine similarity)
/// 3. Apply semantic ranking for result re-ordering
/// 4. Filter by tenant and other criteria
/// 5. Return ranked results with telemetry
///
/// Performance targets:
/// - P95 latency: <500ms
/// - Embedding generation: ~50ms (cached), ~150ms (uncached)
/// - Search execution: ~100-300ms depending on index size
/// </remarks>
public class RagService : IRagService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IOpenAiClient _openAiClient;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IResilientSearchClient? _resilientClient;
    private readonly AnalysisOptions _analysisOptions;
    private readonly ILogger<RagService> _logger;
    private readonly AiTelemetry? _telemetry;

    // Semantic configuration name from the index definition
    private const string SemanticConfigurationName = "knowledge-semantic-config";

    // Vector field name and dimensions (3072-dim text-embedding-3-large)
    private const string VectorFieldName = "contentVector3072";
    private const int VectorDimensions = 3072;

    // Search field for keyword queries
    private static readonly string[] SearchFields = ["content", "fileName", "knowledgeSourceName"];

    // Supported file extensions for type extraction
    private static readonly HashSet<string> SupportedFileTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "docx", "doc", "xlsx", "xls", "pptx", "ppt", "msg", "eml", "txt", "html", "htm", "rtf", "csv"
    };

    public RagService(
        IKnowledgeDeploymentService deploymentService,
        IOpenAiClient openAiClient,
        IEmbeddingCache embeddingCache,
        IOptions<AnalysisOptions> analysisOptions,
        ILogger<RagService> logger,
        IResilientSearchClient? resilientClient = null,
        AiTelemetry? telemetry = null)
    {
        _deploymentService = deploymentService;
        _openAiClient = openAiClient;
        _embeddingCache = embeddingCache;
        _resilientClient = resilientClient;
        _analysisOptions = analysisOptions.Value;
        _logger = logger;
        _telemetry = telemetry;
    }

    /// <inheritdoc />
    public async Task<RagSearchResponse> SearchAsync(
        string query,
        RagSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TenantId);

        var totalStopwatch = Stopwatch.StartNew();
        var embeddingStopwatch = new Stopwatch();
        var searchStopwatch = new Stopwatch();
        var embeddingCacheHit = false;

        _logger.LogDebug(
            "Starting RAG search for tenant {TenantId}, query length={QueryLength}, TopK={TopK}",
            options.TenantId, query.Length, options.TopK);

        try
        {
            // Step 1: Get embedding for query (with caching)
            ReadOnlyMemory<float> queryEmbedding = default;
            if (options.UseVectorSearch)
            {
                embeddingStopwatch.Start();

                // Try cache first
                var cachedEmbedding = await _embeddingCache.GetEmbeddingForContentAsync(query, cancellationToken);
                if (cachedEmbedding.HasValue)
                {
                    queryEmbedding = cachedEmbedding.Value;
                    embeddingCacheHit = true;
                }
                else
                {
                    // Cache miss - generate and cache
                    queryEmbedding = await _openAiClient.GenerateEmbeddingAsync(query, cancellationToken: cancellationToken);
                    await _embeddingCache.SetEmbeddingForContentAsync(query, queryEmbedding, cancellationToken);
                }

                embeddingStopwatch.Stop();

                _logger.LogDebug("Query embedding in {ElapsedMs}ms, cacheHit={CacheHit}",
                    embeddingStopwatch.ElapsedMilliseconds, embeddingCacheHit);
            }

            // Step 2: Get SearchClient for tenant's deployment
            searchStopwatch.Start();
            var searchClient = options.DeploymentId.HasValue
                ? await _deploymentService.GetSearchClientByDeploymentAsync(options.DeploymentId.Value, cancellationToken)
                : await _deploymentService.GetSearchClientAsync(options.TenantId, cancellationToken);

            // Step 3: Build hybrid search options
            var searchOptions = BuildSearchOptions(options, queryEmbedding);

            // Step 4: Execute search (with optional resilience)
            var searchText = options.UseKeywordSearch ? query : "*";
            SearchResults<KnowledgeDocument> searchResults;

            if (_resilientClient != null)
            {
                searchResults = await _resilientClient.SearchAsync<KnowledgeDocument>(
                    searchClient, searchText, searchOptions, cancellationToken);
            }
            else
            {
                var response = await searchClient.SearchAsync<KnowledgeDocument>(
                    searchText, searchOptions, cancellationToken);
                searchResults = response.Value;
            }
            searchStopwatch.Stop();

            // Step 5: Process results
            var results = new List<RagSearchResult>();
            long totalCount = 0;

            await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
            {
                if (result.Document == null) continue;

                // Apply minimum score filter
                var score = result.Score ?? 0;
                var semanticScore = result.SemanticSearch?.RerankerScore;

                // Use semantic score if available, otherwise use search score
                var effectiveScore = semanticScore ?? score;
                if (effectiveScore < options.MinScore) continue;

                results.Add(new RagSearchResult
                {
                    Id = result.Document.Id,
                    DocumentId = result.Document.DocumentId,
                    DocumentName = result.Document.FileName,
                    Content = result.Document.Content,
                    KnowledgeSourceName = result.Document.KnowledgeSourceName,
                    Score = effectiveScore,
                    SemanticScore = semanticScore,
                    ChunkIndex = result.Document.ChunkIndex,
                    ChunkCount = result.Document.ChunkCount,
                    Highlights = GetHighlights(result),
                    Metadata = result.Document.Metadata,
                    Tags = result.Document.Tags
                });
            }

            // Get total count from response
            totalCount = searchResults.TotalCount ?? results.Count;

            totalStopwatch.Stop();

            _logger.LogInformation(
                "RAG search completed for tenant {TenantId}: {ResultCount} results in {TotalMs}ms " +
                "(embedding={EmbeddingMs}ms, search={SearchMs}ms)",
                options.TenantId, results.Count, totalStopwatch.ElapsedMilliseconds,
                embeddingStopwatch.ElapsedMilliseconds, searchStopwatch.ElapsedMilliseconds);

            // Record telemetry
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: results.Count,
                success: true,
                embeddingCacheHit: embeddingCacheHit);

            return new RagSearchResponse
            {
                Query = query,
                Results = results,
                TotalCount = totalCount,
                SearchDurationMs = searchStopwatch.ElapsedMilliseconds,
                EmbeddingDurationMs = embeddingStopwatch.ElapsedMilliseconds,
                EmbeddingCacheHit = embeddingCacheHit
            };
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "search_failed");
            throw;
        }
        catch (OpenAiCircuitBrokenException)
        {
            _logger.LogWarning("OpenAI circuit breaker open during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: 0,
                resultCount: 0,
                success: false,
                errorCode: "openai_circuit_breaker_open");
            throw;
        }
        catch (AiSearchCircuitBrokenException)
        {
            _logger.LogWarning("Azure AI Search circuit breaker open during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "search_circuit_breaker_open");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during RAG search for tenant {TenantId}", options.TenantId);
            _telemetry?.RecordRagSearch(
                totalDurationMs: totalStopwatch.Elapsed.TotalMilliseconds,
                embeddingDurationMs: embeddingStopwatch.Elapsed.TotalMilliseconds,
                searchDurationMs: searchStopwatch.Elapsed.TotalMilliseconds,
                resultCount: 0,
                success: false,
                errorCode: "unexpected_error");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgeDocument> IndexDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(document.TenantId);

        // Validate speFileId is populated (required for all documents including orphans)
        ArgumentException.ThrowIfNullOrEmpty(document.SpeFileId, nameof(document.SpeFileId));

        _logger.LogDebug("Indexing document {DocumentId} speFileId {SpeFileId} chunk {ChunkIndex} for tenant {TenantId}",
            document.DocumentId ?? "(orphan)", document.SpeFileId, document.ChunkIndex, document.TenantId);

        // Ensure file metadata is populated
        PopulateFileMetadata(document);

        // Generate embedding if not provided
        if (document.ContentVector.Length == 0 && !string.IsNullOrEmpty(document.Content))
        {
            var embedding = await _openAiClient.GenerateEmbeddingAsync(document.Content, cancellationToken: cancellationToken);
            document.ContentVector = embedding;
        }

        // For single-chunk documents, documentVector equals contentVector.
        // For multi-chunk documents indexed individually, use IndexDocumentsBatchAsync to compute documentVector
        // by averaging all chunk contentVectors, or run DocumentVectorBackfillService afterward.
        if (document.DocumentVector.Length == 0 && document.ContentVector.Length > 0 && document.ChunkCount == 1)
        {
            document.DocumentVector = document.ContentVector;
            _logger.LogDebug("Set documentVector = contentVector for single-chunk document speFileId {SpeFileId}", document.SpeFileId);
        }

        // Ensure timestamps are set
        var now = DateTimeOffset.UtcNow;
        if (document.CreatedAt == default)
        {
            document.CreatedAt = now;
        }
        document.UpdatedAt = now;

        // Get SearchClient and index document (with optional resilience)
        var searchClient = await _deploymentService.GetSearchClientAsync(document.TenantId, cancellationToken);
        IndexDocumentsResult indexResult;

        if (_resilientClient != null)
        {
            indexResult = await _resilientClient.MergeOrUploadDocumentsAsync(
                searchClient, new[] { document }, cancellationToken);
        }
        else
        {
            var response = await searchClient.MergeOrUploadDocumentsAsync(
                new[] { document }, cancellationToken: cancellationToken);
            indexResult = response.Value;
        }

        var result = indexResult.Results.FirstOrDefault();
        if (result?.Succeeded != true)
        {
            throw new InvalidOperationException($"Failed to index document: {result?.ErrorMessage}");
        }

        _logger.LogInformation("Indexed document {DocumentId} chunk {ChunkIndex} for tenant {TenantId}",
            document.DocumentId, document.ChunkIndex, document.TenantId);

        return document;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(
        IEnumerable<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var documentList = documents.ToList();
        if (documentList.Count == 0) return [];

        var tenantId = documentList[0].TenantId;
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        // Validate all documents have speFileId (required)
        foreach (var doc in documentList)
        {
            ArgumentException.ThrowIfNullOrEmpty(doc.SpeFileId, nameof(doc.SpeFileId));
        }

        _logger.LogDebug("Batch indexing {Count} documents for tenant {TenantId}", documentList.Count, tenantId);

        // Populate file metadata for all documents
        foreach (var doc in documentList)
        {
            PopulateFileMetadata(doc);
        }

        // Generate embeddings for documents without vectors
        var documentsNeedingEmbeddings = documentList
            .Where(d => d.ContentVector.Length == 0 && !string.IsNullOrEmpty(d.Content))
            .ToList();

        if (documentsNeedingEmbeddings.Count > 0)
        {
            var texts = documentsNeedingEmbeddings.Select(d => d.Content).ToList();
            var embeddings = await _openAiClient.GenerateEmbeddingsAsync(texts, cancellationToken: cancellationToken);

            for (int i = 0; i < documentsNeedingEmbeddings.Count; i++)
            {
                documentsNeedingEmbeddings[i].ContentVector = embeddings[i];
            }
        }

        // Compute documentVector for each unique file by averaging chunk contentVectors.
        // For documents with DocumentId, group by DocumentId; for orphan files, group by SpeFileId.
        // This enables document similarity visualization for the AI Azure Search Module.
        var documentGroups = documentList
            .GroupBy(d => d.DocumentId ?? d.SpeFileId)
            .Where(g => !string.IsNullOrEmpty(g.Key));

        foreach (var group in documentGroups)
        {
            var chunkVectors = group
                .Select(d => d.ContentVector)
                .Where(v => v.Length > 0)
                .ToList();

            if (chunkVectors.Count > 0)
            {
                var documentVector = ComputeDocumentVector(chunkVectors);
                if (documentVector.Length > 0)
                {
                    foreach (var doc in group)
                    {
                        doc.DocumentVector = documentVector;
                    }

                    var firstDoc = group.First();
                    _logger.LogDebug(
                        "Computed documentVector for {Identifier} from {ChunkCount} chunks (isOrphan={IsOrphan})",
                        group.Key, chunkVectors.Count, firstDoc.DocumentId == null);
                }
            }
        }

        // Set timestamps
        var now = DateTimeOffset.UtcNow;
        foreach (var doc in documentList)
        {
            if (doc.CreatedAt == default)
            {
                doc.CreatedAt = now;
            }
            doc.UpdatedAt = now;
        }

        // Index in batches (Azure AI Search limit is 1000 per batch)
        var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);
        var results = new List<IndexResult>();

        const int batchSize = 1000;
        for (int i = 0; i < documentList.Count; i += batchSize)
        {
            var batch = documentList.Skip(i).Take(batchSize).ToList();
            IndexDocumentsResult indexResult;

            if (_resilientClient != null)
            {
                indexResult = await _resilientClient.MergeOrUploadDocumentsAsync(
                    searchClient, batch, cancellationToken);
            }
            else
            {
                var response = await searchClient.MergeOrUploadDocumentsAsync(
                    batch, cancellationToken: cancellationToken);
                indexResult = response.Value;
            }

            foreach (var result in indexResult.Results)
            {
                results.Add(result.Succeeded
                    ? IndexResult.Success(result.Key)
                    : IndexResult.Failure(result.Key, result.ErrorMessage ?? "Unknown error"));
            }
        }

        var successCount = results.Count(r => r.Succeeded);
        _logger.LogInformation("Batch indexed {SuccessCount}/{TotalCount} documents for tenant {TenantId}",
            successCount, documentList.Count, tenantId);

        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentAsync(
        string documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _logger.LogDebug("Deleting document {DocumentId} for tenant {TenantId}", documentId, tenantId);

        var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);
        IndexDocumentsResult deleteResult;

        if (_resilientClient != null)
        {
            deleteResult = await _resilientClient.DeleteDocumentsAsync(
                searchClient, "id", new[] { documentId }, cancellationToken);
        }
        else
        {
            var response = await searchClient.DeleteDocumentsAsync(
                "id", new[] { documentId }, cancellationToken: cancellationToken);
            deleteResult = response.Value;
        }

        var result = deleteResult.Results.FirstOrDefault();
        var succeeded = result?.Succeeded ?? false;

        if (succeeded)
        {
            _logger.LogInformation("Deleted document {DocumentId} for tenant {TenantId}", documentId, tenantId);
        }
        else
        {
            _logger.LogWarning("Failed to delete document {DocumentId} for tenant {TenantId}: {Error}",
                documentId, tenantId, result?.ErrorMessage);
        }

        return succeeded;
    }

    /// <inheritdoc />
    public async Task<int> DeleteBySourceDocumentAsync(
        string sourceDocumentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDocumentId);
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _logger.LogDebug("Deleting all chunks for source document {SourceDocumentId} for tenant {TenantId}",
            sourceDocumentId, tenantId);

        var searchClient = await _deploymentService.GetSearchClientAsync(tenantId, cancellationToken);

        // First, find all chunk IDs for this source document
        var searchOptions = new SearchOptions
        {
            Filter = $"documentId eq '{EscapeFilterValue(sourceDocumentId)}' and tenantId eq '{EscapeFilterValue(tenantId)}'",
            Size = 1000,
            Select = { "id" }
        };

        SearchResults<KnowledgeDocument> searchResults;
        if (_resilientClient != null)
        {
            searchResults = await _resilientClient.SearchAsync<KnowledgeDocument>(
                searchClient, "*", searchOptions, cancellationToken);
        }
        else
        {
            var response = await searchClient.SearchAsync<KnowledgeDocument>(
                "*", searchOptions, cancellationToken);
            searchResults = response.Value;
        }

        var idsToDelete = new List<string>();
        await foreach (var result in searchResults.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document?.Id != null)
            {
                idsToDelete.Add(result.Document.Id);
            }
        }

        if (idsToDelete.Count == 0)
        {
            _logger.LogDebug("No chunks found for source document {SourceDocumentId}", sourceDocumentId);
            return 0;
        }

        // Delete all found chunks (with optional resilience)
        IndexDocumentsResult deleteResult;
        if (_resilientClient != null)
        {
            deleteResult = await _resilientClient.DeleteDocumentsAsync(
                searchClient, "id", idsToDelete, cancellationToken);
        }
        else
        {
            var deleteResponse = await searchClient.DeleteDocumentsAsync(
                "id", idsToDelete, cancellationToken: cancellationToken);
            deleteResult = deleteResponse.Value;
        }
        var deletedCount = deleteResult.Results.Count(r => r.Succeeded);

        _logger.LogInformation("Deleted {DeletedCount}/{TotalCount} chunks for source document {SourceDocumentId}",
            deletedCount, idsToDelete.Count, sourceDocumentId);

        return deletedCount;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

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

    #region Private Methods

    private SearchOptions BuildSearchOptions(RagSearchOptions options, ReadOnlyMemory<float> queryEmbedding)
    {
        var searchOptions = new SearchOptions
        {
            Size = options.TopK,
            IncludeTotalCount = true,
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = SemanticConfigurationName,
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.None)
            }
        };

        // Add vector search if enabled
        if (options.UseVectorSearch && queryEmbedding.Length > 0)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryEmbedding)
                    {
                        KNearestNeighborsCount = options.TopK * 2, // Retrieve more for hybrid fusion
                        Fields = { VectorFieldName }
                    }
                }
            };
        }

        // Build filter expression
        var filters = new List<string>();

        // Always filter by tenant for security
        filters.Add($"tenantId eq '{EscapeFilterValue(options.TenantId)}'");

        // Optional filters
        if (!string.IsNullOrEmpty(options.KnowledgeSourceId))
        {
            filters.Add($"knowledgeSourceId eq '{EscapeFilterValue(options.KnowledgeSourceId)}'");
        }

        if (!string.IsNullOrEmpty(options.DocumentType))
        {
            filters.Add($"documentType eq '{EscapeFilterValue(options.DocumentType)}'");
        }

        if (options.Tags?.Count > 0)
        {
            var tagFilters = options.Tags.Select(t => $"tags/any(tag: tag eq '{EscapeFilterValue(t)}')");
            filters.Add($"({string.Join(" or ", tagFilters)})");
        }

        searchOptions.Filter = string.Join(" and ", filters);

        // Set search fields for keyword search
        if (options.UseKeywordSearch)
        {
            foreach (var field in SearchFields)
            {
                searchOptions.SearchFields.Add(field);
            }
        }

        // Enable highlighting
        searchOptions.HighlightFields.Add("content");

        return searchOptions;
    }

    private static IReadOnlyList<string>? GetHighlights(SearchResult<KnowledgeDocument> result)
    {
        if (result.Highlights == null || !result.Highlights.TryGetValue("content", out var highlights))
        {
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

        return highlights.ToList();
    }

    private static string EscapeFilterValue(string value)
    {
        // Escape single quotes for OData filter expressions
        return value.Replace("'", "''");
    }

    /// <summary>
    /// Populates file metadata fields (fileType) from fileName if not already set.
    /// </summary>
    /// <param name="document">The document to populate metadata for.</param>
    private static void PopulateFileMetadata(KnowledgeDocument document)
    {
        // Extract FileType from FileName if not already set
        if (string.IsNullOrEmpty(document.FileType))
        {
            document.FileType = ExtractFileType(document.FileName);
        }
    }

    /// <summary>
    /// Extracts the file type (extension without dot) from a file name.
    /// Returns "unknown" if the extension cannot be determined or is not supported.
    /// </summary>
    /// <param name="fileName">The file name to extract the type from.</param>
    /// <returns>Lowercase file extension (e.g., "pdf", "docx") or "unknown".</returns>
    private static string ExtractFileType(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return "unknown";
        }

        var lastDotIndex = fileName.LastIndexOf('.');
        if (lastDotIndex < 0 || lastDotIndex == fileName.Length - 1)
        {
            return "unknown";
        }

        var extension = fileName[(lastDotIndex + 1)..].ToLowerInvariant();
        return SupportedFileTypes.Contains(extension) ? extension : "unknown";
    }

    /// <summary>
    /// Compute document-level embedding by averaging chunk contentVectors with L2 normalization.
    /// This enables document similarity visualization per the AI Azure Search Module design.
    /// </summary>
    /// <param name="chunkVectors">Content vectors from all chunks of a document.</param>
    /// <returns>Normalized averaged document vector, or empty if no valid vectors.</returns>
    private static ReadOnlyMemory<float> ComputeDocumentVector(IReadOnlyList<ReadOnlyMemory<float>> chunkVectors)
    {
        if (chunkVectors.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        // Filter valid vectors (non-empty with correct dimensions)
        var validVectors = chunkVectors.Where(v => v.Length == VectorDimensions).ToList();
        if (validVectors.Count == 0)
        {
            return ReadOnlyMemory<float>.Empty;
        }

        // Single vector - return as-is (already normalized by OpenAI)
        if (validVectors.Count == 1)
        {
            return validVectors[0];
        }

        // Compute average of all chunk vectors
        var result = new float[VectorDimensions];
        foreach (var vector in validVectors)
        {
            var span = vector.Span;
            for (int i = 0; i < VectorDimensions; i++)
            {
                result[i] += span[i];
            }
        }

        var count = validVectors.Count;
        for (int i = 0; i < VectorDimensions; i++)
        {
            result[i] /= count;
        }

        // L2 normalization for cosine similarity
        var magnitude = 0f;
        for (int i = 0; i < VectorDimensions; i++)
        {
            magnitude += result[i] * result[i];
        }
        magnitude = MathF.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < VectorDimensions; i++)
            {
                result[i] /= magnitude;
            }
        }

        return result;
    }

    #endregion
}
