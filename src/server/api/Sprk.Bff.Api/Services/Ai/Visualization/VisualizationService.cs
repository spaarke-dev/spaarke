using System.Diagnostics;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.Visualization;

/// <summary>
/// Provides document relationship visualization using Azure AI Search vector similarity.
/// Queries the documentVector field for document-level similarity search.
/// </summary>
/// <remarks>
/// Visualization pipeline:
/// 1. Get source document's documentVector from Azure AI Search
/// 2. Execute vector similarity search on documentVector field
/// 3. Retrieve document metadata from Dataverse for URLs and parent entity info
/// 4. Build graph response with nodes (documents) and edges (relationships)
///
/// Performance targets:
/// - P95 latency: less than 500ms for up to 50 nodes
/// - Search execution: ~100-300ms depending on index size
///
/// Multi-tenant isolation via tenantId filter on all queries.
/// </remarks>
public class VisualizationService : IVisualizationService
{
    private readonly IKnowledgeDeploymentService _deploymentService;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<VisualizationService> _logger;
    private readonly DataverseOptions _dataverseOptions;

    // Vector field for document-level embeddings (1536 dimensions)
    private const string DocumentVectorFieldName = "documentVector";
    private const int VectorDimensions = 1536;

    // Maximum nodes to prevent exponential growth
    private const int MaxTotalNodes = 100;

    public VisualizationService(
        IKnowledgeDeploymentService deploymentService,
        IDataverseService dataverseService,
        IOptions<DataverseOptions> dataverseOptions,
        ILogger<VisualizationService> logger)
    {
        _deploymentService = deploymentService;
        _dataverseService = dataverseService;
        _dataverseOptions = dataverseOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
        Guid documentId,
        VisualizationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.TenantId);

        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug(
            "Starting visualization search for document {DocumentId}, tenant {TenantId}, threshold={Threshold}, limit={Limit}, depth={Depth}",
            documentId, options.TenantId, options.Threshold, options.Limit, options.Depth);

        try
        {
            // Step 1: Get SearchClient for tenant
            var searchClient = await _deploymentService.GetSearchClientAsync(options.TenantId, cancellationToken);

            // Step 2: Get source document with its documentVector
            var sourceDocument = await GetSourceDocumentAsync(
                searchClient, documentId.ToString(), options.TenantId, cancellationToken);

            if (sourceDocument == null)
            {
                _logger.LogWarning("Source document {DocumentId} not found in index", documentId);
                return CreateEmptyResponse(documentId.ToString(), options);
            }

            // Step 3: Search for related documents using documentVector
            var relatedDocuments = await SearchRelatedDocumentsAsync(
                searchClient, sourceDocument.DocumentVector, documentId.ToString(), options, cancellationToken);

            // Step 4: Get Dataverse metadata for URLs and parent entity info
            var documentMetadata = await GetDocumentMetadataAsync(
                sourceDocument, relatedDocuments, cancellationToken);

            // Step 5: Build graph response
            var response = BuildGraphResponse(
                sourceDocument, relatedDocuments, documentMetadata, options, stopwatch.ElapsedMilliseconds);

            stopwatch.Stop();

            _logger.LogInformation(
                "Visualization search completed for document {DocumentId}: {NodeCount} nodes, {EdgeCount} edges in {ElapsedMs}ms",
                documentId, response.Nodes.Count, response.Edges.Count, stopwatch.ElapsedMilliseconds);

            return response;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Azure AI Search error during visualization for document {DocumentId}", documentId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during visualization for document {DocumentId}", documentId);
            throw;
        }
    }

    #region Private Methods

    /// <summary>
    /// Get source document with its documentVector from Azure AI Search.
    /// </summary>
    private async Task<VisualizationDocument?> GetSourceDocumentAsync(
        SearchClient searchClient,
        string documentId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // Query for a single chunk of the source document to get its documentVector
        var searchOptions = new SearchOptions
        {
            Size = 1,
            Filter = $"documentId eq '{EscapeFilterValue(documentId)}' and tenantId eq '{EscapeFilterValue(tenantId)}'",
            Select = { "id", "documentId", "documentName", "documentType", "tenantId",
                       "createdAt", "updatedAt", "documentVector", "metadata", "tags" }
        };

        var response = await searchClient.SearchAsync<VisualizationDocument>("*", searchOptions, cancellationToken);
        var results = response.Value;

        await foreach (var result in results.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document != null)
            {
                return result.Document;
            }
        }

        return null;
    }

    /// <summary>
    /// Search for documents with similar documentVector.
    /// </summary>
    private async Task<List<(VisualizationDocument Document, double Score)>> SearchRelatedDocumentsAsync(
        SearchClient searchClient,
        ReadOnlyMemory<float> sourceVector,
        string sourceDocumentId,
        VisualizationOptions options,
        CancellationToken cancellationToken)
    {
        if (sourceVector.Length == 0)
        {
            _logger.LogWarning("Source document {DocumentId} has no documentVector", sourceDocumentId);
            return [];
        }

        // Build search options with vector search on documentVector
        var searchOptions = new SearchOptions
        {
            Size = options.Limit * 2, // Retrieve more to account for duplicates (same doc, different chunks)
            IncludeTotalCount = true,
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(sourceVector)
                    {
                        KNearestNeighborsCount = options.Limit * 2,
                        Fields = { DocumentVectorFieldName }
                    }
                }
            },
            Select = { "id", "documentId", "documentName", "documentType", "tenantId",
                       "createdAt", "updatedAt", "documentVector", "metadata", "tags" }
        };

        // Build filter: tenant isolation + exclude source document + optional document types
        var filters = new List<string>
        {
            $"tenantId eq '{EscapeFilterValue(options.TenantId)}'",
            $"documentId ne '{EscapeFilterValue(sourceDocumentId)}'"
        };

        if (options.DocumentTypes?.Count > 0)
        {
            var typeFilters = options.DocumentTypes
                .Select(t => $"documentType eq '{EscapeFilterValue(t)}'");
            filters.Add($"({string.Join(" or ", typeFilters)})");
        }

        searchOptions.Filter = string.Join(" and ", filters);

        // Execute search
        var response = await searchClient.SearchAsync<VisualizationDocument>("*", searchOptions, cancellationToken);
        var results = response.Value;

        // Process results, deduplicating by documentId
        var seenDocuments = new HashSet<string>();
        var relatedDocuments = new List<(VisualizationDocument Document, double Score)>();

        await foreach (var result in results.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document == null) continue;

            var score = result.Score ?? 0;

            // Apply threshold filter
            if (score < options.Threshold) continue;

            // Deduplicate by documentId (multiple chunks from same document)
            if (!seenDocuments.Add(result.Document.DocumentId))
            {
                continue;
            }

            relatedDocuments.Add((result.Document, score));

            // Respect limit
            if (relatedDocuments.Count >= options.Limit)
            {
                break;
            }
        }

        return relatedDocuments;
    }

    /// <summary>
    /// Get document metadata from Dataverse for URLs and parent entity info.
    /// </summary>
    private async Task<Dictionary<string, DocumentMetadata>> GetDocumentMetadataAsync(
        VisualizationDocument sourceDocument,
        List<(VisualizationDocument Document, double Score)> relatedDocuments,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, DocumentMetadata>();

        // Collect all document IDs
        var documentIds = new List<string> { sourceDocument.DocumentId };
        documentIds.AddRange(relatedDocuments.Select(r => r.Document.DocumentId));

        // Fetch metadata for each document
        foreach (var docId in documentIds.Distinct())
        {
            try
            {
                var entity = await _dataverseService.GetDocumentAsync(docId, cancellationToken);
                if (entity != null)
                {
                    metadata[docId] = new DocumentMetadata
                    {
                        RecordUrl = BuildRecordUrl(docId),
                        FileUrl = BuildFileUrl(entity),
                        FilePreviewUrl = BuildPreviewUrl(entity),
                        ExtractedKeywords = ParseKeywords(entity.Keywords),
                        CreatedOn = entity.CreatedOn,
                        ModifiedOn = entity.ModifiedOn,
                        // TODO: Add parent entity lookup when available
                        ParentEntityType = null,
                        ParentEntityId = null,
                        ParentEntityName = null
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get metadata for document {DocumentId}", docId);
                // Continue without metadata for this document
                metadata[docId] = new DocumentMetadata
                {
                    RecordUrl = BuildRecordUrl(docId),
                    FileUrl = string.Empty,
                    ExtractedKeywords = []
                };
            }
        }

        return metadata;
    }

    /// <summary>
    /// Build the graph response from search results.
    /// </summary>
    private DocumentGraphResponse BuildGraphResponse(
        VisualizationDocument sourceDocument,
        List<(VisualizationDocument Document, double Score)> relatedDocuments,
        Dictionary<string, DocumentMetadata> metadata,
        VisualizationOptions options,
        long searchLatencyMs)
    {
        var nodes = new List<DocumentNode>();
        var edges = new List<DocumentEdge>();

        // Add source node
        var sourceMetadata = metadata.GetValueOrDefault(sourceDocument.DocumentId) ?? new DocumentMetadata();
        nodes.Add(CreateNode(sourceDocument, sourceMetadata, isSource: true, depth: 0, similarity: null));

        // Add related nodes and edges
        foreach (var (document, score) in relatedDocuments)
        {
            var docMetadata = metadata.GetValueOrDefault(document.DocumentId) ?? new DocumentMetadata();
            nodes.Add(CreateNode(document, docMetadata, isSource: false, depth: 1, similarity: score));

            // Create edge from source to related document
            var sharedKeywords = options.IncludeKeywords
                ? FindSharedKeywords(sourceMetadata.ExtractedKeywords, docMetadata.ExtractedKeywords)
                : [];

            edges.Add(new DocumentEdge
            {
                Id = $"{sourceDocument.DocumentId}-{document.DocumentId}",
                Source = sourceDocument.DocumentId,
                Target = document.DocumentId,
                Data = new DocumentEdgeData
                {
                    Similarity = score,
                    SharedKeywords = sharedKeywords,
                    RelationshipType = "semantic"
                }
            });
        }

        // Calculate nodes per level
        var nodesPerLevel = new List<int> { 1 }; // Level 0 has 1 node (source)
        if (relatedDocuments.Count > 0)
        {
            nodesPerLevel.Add(relatedDocuments.Count); // Level 1
        }

        return new DocumentGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                SourceDocumentId = sourceDocument.DocumentId,
                TenantId = options.TenantId,
                TotalResults = relatedDocuments.Count,
                Threshold = options.Threshold,
                Depth = options.Depth,
                MaxDepthReached = relatedDocuments.Count > 0 ? 1 : 0,
                NodesPerLevel = nodesPerLevel,
                SearchLatencyMs = searchLatencyMs,
                CacheHit = false // TODO: Track cache hits when implemented
            }
        };
    }

    private static DocumentNode CreateNode(
        VisualizationDocument document,
        DocumentMetadata metadata,
        bool isSource,
        int depth,
        double? similarity)
    {
        return new DocumentNode
        {
            Id = document.DocumentId,
            Type = isSource ? "source" : "related",
            Depth = depth,
            Data = new DocumentNodeData
            {
                Label = document.DocumentName,
                DocumentType = document.DocumentType ?? "Unknown",
                Similarity = similarity,
                ExtractedKeywords = metadata.ExtractedKeywords,
                CreatedOn = document.CreatedAt,
                ModifiedOn = document.UpdatedAt,
                RecordUrl = metadata.RecordUrl,
                FileUrl = metadata.FileUrl,
                FilePreviewUrl = metadata.FilePreviewUrl,
                ParentEntityType = metadata.ParentEntityType,
                ParentEntityId = metadata.ParentEntityId,
                ParentEntityName = metadata.ParentEntityName
            }
        };
    }

    private static DocumentGraphResponse CreateEmptyResponse(string documentId, VisualizationOptions options)
    {
        return new DocumentGraphResponse
        {
            Nodes = [],
            Edges = [],
            Metadata = new GraphMetadata
            {
                SourceDocumentId = documentId,
                TenantId = options.TenantId,
                TotalResults = 0,
                Threshold = options.Threshold,
                Depth = options.Depth,
                MaxDepthReached = 0,
                NodesPerLevel = [],
                SearchLatencyMs = 0,
                CacheHit = false
            }
        };
    }

    private static IReadOnlyList<string> FindSharedKeywords(
        IReadOnlyList<string> keywords1,
        IReadOnlyList<string> keywords2)
    {
        if (keywords1.Count == 0 || keywords2.Count == 0)
        {
            return [];
        }

        var set1 = new HashSet<string>(keywords1, StringComparer.OrdinalIgnoreCase);
        return keywords2.Where(k => set1.Contains(k)).ToList();
    }

    private static IReadOnlyList<string> ParseKeywords(string? keywords)
    {
        if (string.IsNullOrWhiteSpace(keywords))
        {
            return [];
        }

        return keywords
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private string BuildRecordUrl(string documentId)
    {
        // Format: https://{org}.crm.dynamics.com/main.aspx?etn=sprk_document&id={guid}&pagetype=entityrecord
        var orgUrl = _dataverseOptions.EnvironmentUrl.TrimEnd('/');
        return $"{orgUrl}/main.aspx?etn=sprk_document&id={documentId}&pagetype=entityrecord";
    }

    private string BuildFileUrl(DocumentEntity entity)
    {
        if (string.IsNullOrEmpty(entity.GraphDriveId) || string.IsNullOrEmpty(entity.GraphItemId))
        {
            return string.Empty;
        }

        // Format: SharePoint URL via Graph
        return $"https://graph.microsoft.com/v1.0/drives/{entity.GraphDriveId}/items/{entity.GraphItemId}";
    }

    private static string? BuildPreviewUrl(DocumentEntity entity)
    {
        if (string.IsNullOrEmpty(entity.GraphDriveId) || string.IsNullOrEmpty(entity.GraphItemId))
        {
            return null;
        }

        // Format: SharePoint preview URL via Graph
        return $"https://graph.microsoft.com/v1.0/drives/{entity.GraphDriveId}/items/{entity.GraphItemId}/preview";
    }

    private static string EscapeFilterValue(string value)
    {
        // Escape single quotes for OData filter expressions
        return value.Replace("'", "''");
    }

    #endregion
}

/// <summary>
/// Document model for visualization queries (includes documentVector field).
/// </summary>
internal class VisualizationDocument
{
    public string Id { get; set; } = string.Empty;
    public string DocumentId { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string? DocumentType { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ReadOnlyMemory<float> DocumentVector { get; set; }
    public string? Metadata { get; set; }
    public IList<string>? Tags { get; set; }
}

/// <summary>
/// Document metadata from Dataverse for building node data.
/// </summary>
internal class DocumentMetadata
{
    public string RecordUrl { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public string? FilePreviewUrl { get; set; }
    public IReadOnlyList<string> ExtractedKeywords { get; set; } = [];
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
    public string? ParentEntityType { get; set; }
    public string? ParentEntityId { get; set; }
    public string? ParentEntityName { get; set; }
}
