using System.Diagnostics;
using System.Text.Json.Serialization;
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

    // Vector field for document-level embeddings (3072 dimensions, text-embedding-3-large)
    private const string DocumentVectorFieldName = "documentVector3072";
    private const int VectorDimensions = 3072;

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
            // Step 1: Get source document from Dataverse (always required for relationship lookups)
            _logger.LogDebug("[VIZ-DEBUG] Step 1: Fetching source document {DocumentId} from Dataverse", documentId);
            var sourceDataverseDoc = await _dataverseService.GetDocumentAsync(documentId.ToString(), cancellationToken);

            if (sourceDataverseDoc == null)
            {
                _logger.LogWarning("[VISUALIZATION] Source document {DocumentId} NOT FOUND in Dataverse. Ensure the document exists and the app has read permissions.", documentId);
                // Return empty response with diagnostic info in metadata
                var emptyResponse = CreateEmptyResponse(documentId.ToString(), options);
                emptyResponse.Metadata.DiagnosticMessage = $"Source document {documentId} not found in Dataverse";
                return emptyResponse;
            }

            _logger.LogDebug("[VIZ-DEBUG] Source document {DocumentId} FOUND: Name={Name}, FileName={FileName}",
                documentId, sourceDataverseDoc.Name, sourceDataverseDoc.FileName ?? "(null)");

            // Step 2: Get SearchClient and try to get source document from AI Search (optional for semantic search)
            _logger.LogDebug("[VIZ-DEBUG] Step 2: Getting SearchClient for tenant {TenantId}", options.TenantId);
            var searchClient = await _deploymentService.GetSearchClientAsync(options.TenantId, cancellationToken);
            _logger.LogDebug("[VIZ-DEBUG] Step 2: SearchClient obtained, index={IndexName}", searchClient.IndexName);

            var sourceDocument = await GetSourceDocumentAsync(
                searchClient, documentId.ToString(), options.TenantId, cancellationToken);

            // If not in AI Search, create a VisualizationDocument from Dataverse data
            var effectiveSourceDocument = sourceDocument ?? ConvertToVisualizationDocument(sourceDataverseDoc);

            if (sourceDocument == null)
            {
                _logger.LogDebug("[VIZ-DEBUG] Step 2: Source document {DocumentId} NOT FOUND in AI Search index (tenant={TenantId}). Semantic search will be SKIPPED.", documentId, options.TenantId);
            }
            else
            {
                _logger.LogDebug("[VIZ-DEBUG] Step 2: Source document FOUND in AI Search: Id={Id}, DocumentId={DocId}, FileName={FileName}, VectorLength={VectorLength}",
                    sourceDocument.Id, sourceDocument.DocumentId ?? "(null)", sourceDocument.FileName ?? "(null)", sourceDocument.DocumentVector3072.Length);
            }

            // Step 3: Query hardcoded relationships from Dataverse (always works)
            var hardcodedRelationships = await GetHardcodedRelationshipsAsync(
                documentId, sourceDataverseDoc, options, cancellationToken);

            // Step 4: Query semantic relationships from AI Search (only if document is indexed and has vector)
            _logger.LogDebug("[VIZ-DEBUG] Step 4: Semantic search check - sourceDocument={HasSource}, ShouldIncludeSemantic={ShouldInclude}",
                sourceDocument != null, ShouldIncludeRelationshipType(options, RelationshipTypes.Semantic));

            var semanticRelationships = new List<(VisualizationDocument Document, double Score, string RelationType)>();
            if (sourceDocument != null && ShouldIncludeRelationshipType(options, RelationshipTypes.Semantic))
            {
                var sourceVector = sourceDocument.GetBestVector();
                _logger.LogDebug("[VIZ-DEBUG] Step 4: Source vector length={VectorLength} (need >0 to search)", sourceVector.Length);

                if (sourceVector.Length > 0)
                {
                    _logger.LogDebug("[VIZ-DEBUG] Step 4: Executing semantic search with threshold={Threshold}, limit={Limit}", options.Threshold, options.Limit);
                    var semanticDocs = await SearchRelatedDocumentsAsync(
                        searchClient, sourceVector, documentId.ToString(), options, cancellationToken);

                    _logger.LogDebug("[VIZ-DEBUG] Step 4: Semantic search returned {Count} documents", semanticDocs.Count);

                    semanticRelationships = semanticDocs
                        .Select(r => (r.Document, r.Score, RelationshipTypes.Semantic))
                        .ToList();
                }
                else
                {
                    _logger.LogDebug("[VIZ-DEBUG] Step 4: Source document has NO VECTOR - semantic search SKIPPED. DocumentId={DocumentId}", documentId);
                }
            }
            else if (sourceDocument == null)
            {
                _logger.LogDebug("[VIZ-DEBUG] Step 4: Semantic search SKIPPED because source document not in AI Search index");
            }

            // Step 5: Merge and deduplicate relationships (hardcoded takes priority)
            var allRelationships = MergeRelationships(hardcodedRelationships, semanticRelationships, documentId.ToString());

            // Step 6: Get Dataverse metadata for URLs and parent entity info
            var documentMetadata = await GetDocumentMetadataAsync(
                effectiveSourceDocument, allRelationships.Select(r => (r.Document, r.Score)).ToList(), cancellationToken);

            // Step 7: Build graph response with parent hub topology
            var response = BuildGraphResponseWithHubTopology(
                effectiveSourceDocument, sourceDataverseDoc, allRelationships, documentMetadata, options, stopwatch.ElapsedMilliseconds);

            stopwatch.Stop();

            _logger.LogInformation(
                "Visualization search completed for document {DocumentId}: {NodeCount} nodes, {EdgeCount} edges ({HardcodedCount} hardcoded, {SemanticCount} semantic) in {ElapsedMs}ms",
                documentId, response.Nodes.Count, response.Edges.Count,
                hardcodedRelationships.Count, semanticRelationships.Count, stopwatch.ElapsedMilliseconds);

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

    #region Relationship Type Methods

    /// <summary>
    /// Check if a relationship type should be included based on filter options.
    /// </summary>
    private static bool ShouldIncludeRelationshipType(VisualizationOptions options, string relationshipType)
    {
        // If no filter specified, include all types
        if (options.RelationshipTypeFilter == null || options.RelationshipTypeFilter.Count == 0)
        {
            return true;
        }

        return options.RelationshipTypeFilter.Contains(relationshipType, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Get hardcoded relationships (same_email, same_matter, etc.) from Dataverse.
    /// </summary>
    private async Task<List<(VisualizationDocument Document, double Score, string RelationType)>> GetHardcodedRelationshipsAsync(
        Guid sourceDocumentId,
        DocumentEntity? sourceDoc,
        VisualizationOptions options,
        CancellationToken cancellationToken)
    {
        var results = new List<(VisualizationDocument Document, double Score, string RelationType)>();

        // same_email relationship (parent-child via ParentDocument lookup)
        if (ShouldIncludeRelationshipType(options, RelationshipTypes.SameEmail))
        {
            var emailSiblings = await GetEmailSiblingsAsync(sourceDocumentId, sourceDoc, cancellationToken);
            results.AddRange(emailSiblings.Select(doc => (doc, 1.0, RelationshipTypes.SameEmail)));
        }

        // same_matter relationship (documents linked to same Matter)
        _logger.LogDebug("[VIZ-DEBUG] same_matter check: MatterId={MatterId}, ShouldInclude={ShouldInclude}",
            sourceDoc?.MatterId ?? "(null)", ShouldIncludeRelationshipType(options, RelationshipTypes.SameMatter));

        if (ShouldIncludeRelationshipType(options, RelationshipTypes.SameMatter) &&
            sourceDoc?.MatterId != null &&
            Guid.TryParse(sourceDoc.MatterId, out var matterId))
        {
            _logger.LogDebug("[VIZ-DEBUG] Querying same_matter documents for MatterId={MatterId}", matterId);
            var matterDocs = await GetDocumentsByMatterAsync(matterId, sourceDocumentId, cancellationToken);
            _logger.LogDebug("[VIZ-DEBUG] same_matter query returned {Count} documents", matterDocs.Count);
            results.AddRange(matterDocs.Select(doc => (doc, 1.0, RelationshipTypes.SameMatter)));
        }

        // same_project relationship (documents linked to same Project)
        if (ShouldIncludeRelationshipType(options, RelationshipTypes.SameProject) &&
            sourceDoc?.ProjectId != null &&
            Guid.TryParse(sourceDoc.ProjectId, out var projectId))
        {
            var projectDocs = await GetDocumentsByProjectAsync(projectId, sourceDocumentId, cancellationToken);
            results.AddRange(projectDocs.Select(doc => (doc, 1.0, RelationshipTypes.SameProject)));
        }

        // same_invoice relationship (documents linked to same Invoice)
        if (ShouldIncludeRelationshipType(options, RelationshipTypes.SameInvoice) &&
            sourceDoc?.InvoiceId != null &&
            Guid.TryParse(sourceDoc.InvoiceId, out var invoiceId))
        {
            var invoiceDocs = await GetDocumentsByInvoiceAsync(invoiceId, sourceDocumentId, cancellationToken);
            results.AddRange(invoiceDocs.Select(doc => (doc, 1.0, RelationshipTypes.SameInvoice)));
        }

        // same_thread relationship (emails in same thread via ConversationIndex prefix)
        if (ShouldIncludeRelationshipType(options, RelationshipTypes.SameThread) &&
            !string.IsNullOrEmpty(sourceDoc?.EmailConversationIndex))
        {
            var threadDocs = await GetDocumentsByThreadAsync(sourceDoc.EmailConversationIndex, sourceDocumentId, cancellationToken);
            results.AddRange(threadDocs.Select(doc => (doc, 1.0, RelationshipTypes.SameThread)));
        }

        return results;
    }

    /// <summary>
    /// Get documents associated with the same Matter.
    /// </summary>
    private async Task<List<VisualizationDocument>> GetDocumentsByMatterAsync(
        Guid matterId,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var docs = await _dataverseService.GetDocumentsByMatterAsync(matterId, sourceDocumentId, cancellationToken);
            var results = docs.Select(ConvertToVisualizationDocument).ToList();
            _logger.LogDebug("Found {Count} documents for Matter {MatterId}", results.Count, matterId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting documents for Matter {MatterId}", matterId);
            return [];
        }
    }

    /// <summary>
    /// Get documents associated with the same Project.
    /// </summary>
    private async Task<List<VisualizationDocument>> GetDocumentsByProjectAsync(
        Guid projectId,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var docs = await _dataverseService.GetDocumentsByProjectAsync(projectId, sourceDocumentId, cancellationToken);
            var results = docs.Select(ConvertToVisualizationDocument).ToList();
            _logger.LogDebug("Found {Count} documents for Project {ProjectId}", results.Count, projectId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting documents for Project {ProjectId}", projectId);
            return [];
        }
    }

    /// <summary>
    /// Get documents associated with the same Invoice.
    /// </summary>
    private async Task<List<VisualizationDocument>> GetDocumentsByInvoiceAsync(
        Guid invoiceId,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var docs = await _dataverseService.GetDocumentsByInvoiceAsync(invoiceId, sourceDocumentId, cancellationToken);
            var results = docs.Select(ConvertToVisualizationDocument).ToList();
            _logger.LogDebug("Found {Count} documents for Invoice {InvoiceId}", results.Count, invoiceId);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting documents for Invoice {InvoiceId}", invoiceId);
            return [];
        }
    }

    /// <summary>
    /// Get documents in the same email thread (ConversationIndex prefix match).
    /// First 44 chars of ConversationIndex identify the thread root.
    /// </summary>
    private async Task<List<VisualizationDocument>> GetDocumentsByThreadAsync(
        string conversationIndex,
        Guid sourceDocumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            // ConversationIndex: first 44 chars = thread root identifier
            // Subsequent 10-char blocks = reply timestamps
            var threadPrefix = conversationIndex.Length > 44
                ? conversationIndex[..44]
                : conversationIndex;

            var docs = await _dataverseService.GetDocumentsByConversationIndexAsync(threadPrefix, sourceDocumentId, cancellationToken);
            var results = docs.Select(ConvertToVisualizationDocument).ToList();
            _logger.LogDebug("Found {Count} documents in email thread (prefix: {Prefix})", results.Count, threadPrefix);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting documents for email thread");
            return [];
        }
    }

    /// <summary>
    /// Get documents from the same email (parent + siblings).
    /// If source is an email archive (.eml), returns its attachments (children).
    /// If source is an attachment, returns the parent email and sibling attachments.
    /// </summary>
    private async Task<List<VisualizationDocument>> GetEmailSiblingsAsync(
        Guid sourceDocumentId,
        DocumentEntity? sourceDoc,
        CancellationToken cancellationToken)
    {
        var siblings = new List<VisualizationDocument>();

        if (sourceDoc == null)
        {
            _logger.LogDebug("Source document {DocumentId} not found in Dataverse, skipping email siblings", sourceDocumentId);
            return siblings;
        }

        // Log source document email-related fields for debugging
        _logger.LogDebug(
            "[VIZ-DEBUG] Email sibling check for {DocumentId}: IsEmailArchive={IsEmailArchive}, ParentDocumentId={ParentDocumentId}, FileName={FileName}",
            sourceDocumentId, sourceDoc.IsEmailArchive, sourceDoc.ParentDocumentId ?? "(null)", sourceDoc.FileName ?? "(null)");

        try
        {
            // Case 1: Source is an email archive (.eml) - get its children (attachments)
            if (sourceDoc.IsEmailArchive == true)
            {
                _logger.LogDebug("Source document {DocumentId} is email archive, querying children", sourceDocumentId);
                var children = await _dataverseService.GetDocumentsByParentAsync(sourceDocumentId, cancellationToken);

                foreach (var child in children)
                {
                    siblings.Add(ConvertToVisualizationDocument(child));
                }

                _logger.LogDebug("Found {Count} child documents for email {DocumentId}", siblings.Count, sourceDocumentId);
            }
            // Case 2: Source has a parent document (it's an attachment) - get parent and siblings
            else if (!string.IsNullOrEmpty(sourceDoc.ParentDocumentId) &&
                     Guid.TryParse(sourceDoc.ParentDocumentId, out var parentId))
            {
                _logger.LogDebug("Source document {DocumentId} has parent {ParentId}, querying siblings", sourceDocumentId, parentId);

                // Get parent document
                var parent = await _dataverseService.GetDocumentAsync(parentId.ToString(), cancellationToken);
                if (parent != null)
                {
                    siblings.Add(ConvertToVisualizationDocument(parent));
                }

                // Get sibling attachments (children of the same parent)
                var siblingDocs = await _dataverseService.GetDocumentsByParentAsync(parentId, cancellationToken);
                foreach (var sibling in siblingDocs)
                {
                    // Exclude the source document itself
                    if (sibling.Id != sourceDocumentId.ToString())
                    {
                        siblings.Add(ConvertToVisualizationDocument(sibling));
                    }
                }

                _logger.LogDebug("Found {Count} sibling documents for attachment {DocumentId}", siblings.Count, sourceDocumentId);
            }
            else
            {
                _logger.LogDebug(
                    "[VIZ-DEBUG] Source document {DocumentId} is not an email archive and has no parent - IsEmailArchive={IsEmailArchive}, ParentDocumentId={ParentDocumentId}",
                    sourceDocumentId, sourceDoc.IsEmailArchive, sourceDoc.ParentDocumentId ?? "(null)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting email siblings for document {DocumentId}", sourceDocumentId);
            // Don't fail the whole operation, just return empty siblings
        }

        return siblings;
    }

    /// <summary>
    /// Convert a DocumentEntity to a VisualizationDocument for graph building.
    /// </summary>
    private static VisualizationDocument ConvertToVisualizationDocument(DocumentEntity entity)
    {
        return new VisualizationDocument
        {
            Id = entity.Id,
            DocumentId = entity.Id,
            SpeFileId = entity.GraphItemId, // Use GraphItemId as SPE file identifier
            FileName = entity.FileName ?? entity.Name,
            FileType = GetFileExtensionFromFileName(entity.FileName),
            DocumentType = entity.DocumentType,
            TenantId = entity.ContainerId ?? string.Empty, // Container acts as tenant context
            CreatedAt = entity.CreatedOn,
            UpdatedAt = entity.ModifiedOn
        };
    }

    /// <summary>
    /// Extract file extension from filename.
    /// </summary>
    private static string? GetFileExtensionFromFileName(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return null;
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) ? null : ext.TrimStart('.').ToLowerInvariant();
    }

    /// <summary>
    /// Merge hardcoded and semantic relationships, deduplicating by document ID.
    /// Hardcoded relationships take priority (lower priority number wins).
    /// </summary>
    private List<(VisualizationDocument Document, double Score, string RelationType)> MergeRelationships(
        List<(VisualizationDocument Document, double Score, string RelationType)> hardcoded,
        List<(VisualizationDocument Document, double Score, string RelationType)> semantic,
        string sourceDocumentId)
    {
        var result = new Dictionary<string, (VisualizationDocument Document, double Score, string RelationType)>();

        // Add hardcoded relationships first (they have priority)
        foreach (var (doc, score, relType) in hardcoded)
        {
            var uniqueId = doc.GetUniqueId();
            if (uniqueId == sourceDocumentId) continue; // Exclude source

            if (!result.ContainsKey(uniqueId))
            {
                result[uniqueId] = (doc, score, relType);
            }
            else
            {
                // Keep the higher priority relationship type
                var existing = result[uniqueId];
                if (RelationshipTypes.GetPriority(relType) < RelationshipTypes.GetPriority(existing.RelationType))
                {
                    result[uniqueId] = (doc, score, relType);
                }
            }
        }

        // Add semantic relationships (only if not already present)
        foreach (var (doc, score, relType) in semantic)
        {
            var uniqueId = doc.GetUniqueId();
            if (uniqueId == sourceDocumentId) continue; // Exclude source

            if (!result.ContainsKey(uniqueId))
            {
                result[uniqueId] = (doc, score, relType);
            }
            // If already present (from hardcoded), keep the hardcoded one (higher priority)
        }

        return result.Values.ToList();
    }

    /// <summary>
    /// Build graph response with parent hub topology.
    /// Creates parent entity nodes (Matter, Project, Invoice, Email) as hubs
    /// and connects documents to their respective parent hubs.
    /// </summary>
    private DocumentGraphResponse BuildGraphResponseWithHubTopology(
        VisualizationDocument sourceDocument,
        DocumentEntity sourceDataverseDoc,
        List<(VisualizationDocument Document, double Score, string RelationType)> relatedDocuments,
        Dictionary<string, DocumentMetadata> metadata,
        VisualizationOptions options,
        long searchLatencyMs)
    {
        var nodes = new List<DocumentNode>();
        var edges = new List<DocumentEdge>();
        var parentHubNodes = new Dictionary<string, DocumentNode>(); // Track created parent hubs

        var sourceId = sourceDocument.GetUniqueId();

        // Add source node
        var sourceMetadata = metadata.GetValueOrDefault(sourceId) ?? new DocumentMetadata();
        nodes.Add(CreateNode(sourceDocument, sourceMetadata, isSource: true, depth: 0, similarity: null));

        // Group related documents by relationship type to create parent hubs
        var documentsByRelType = relatedDocuments
            .GroupBy(r => r.RelationType)
            .ToDictionary(g => g.Key, g => g.ToList());

        // For each relationship type that has documents, create parent hub node and edges
        foreach (var (relType, docs) in documentsByRelType)
        {
            // Create parent hub node for non-semantic relationships
            if (relType != RelationshipTypes.Semantic)
            {
                var parentHub = CreateParentHubNode(relType, sourceDataverseDoc);
                if (parentHub != null && !parentHubNodes.ContainsKey(parentHub.Id))
                {
                    parentHubNodes[parentHub.Id] = parentHub;
                    nodes.Add(parentHub);

                    // Connect source document to its parent hub
                    edges.Add(new DocumentEdge
                    {
                        Id = $"{sourceId}-{parentHub.Id}",
                        Source = sourceId,
                        Target = parentHub.Id,
                        Data = new DocumentEdgeData
                        {
                            Similarity = 1.0,
                            SharedKeywords = [],
                            RelationshipType = relType,
                            RelationshipLabel = RelationshipTypes.GetLabel(relType)
                        }
                    });
                }

                // Add each related document and connect to parent hub
                foreach (var (document, score, _) in docs)
                {
                    var targetId = document.GetUniqueId();
                    var docMetadata = metadata.GetValueOrDefault(targetId) ?? new DocumentMetadata();

                    // Add document node if not already added
                    if (!nodes.Any(n => n.Id == targetId))
                    {
                        nodes.Add(CreateNode(document, docMetadata, isSource: false, depth: 1, similarity: score));
                    }

                    // Connect document to parent hub (not to source)
                    if (parentHub != null)
                    {
                        var edgeId = $"{targetId}-{parentHub.Id}";
                        if (!edges.Any(e => e.Id == edgeId))
                        {
                            edges.Add(new DocumentEdge
                            {
                                Id = edgeId,
                                Source = targetId,
                                Target = parentHub.Id,
                                Data = new DocumentEdgeData
                                {
                                    Similarity = score,
                                    SharedKeywords = [],
                                    RelationshipType = relType,
                                    RelationshipLabel = RelationshipTypes.GetLabel(relType)
                                }
                            });
                        }
                    }
                }
            }
            else
            {
                // Semantic relationships: connect directly from source to related docs (no hub)
                foreach (var (document, score, _) in docs)
                {
                    var targetId = document.GetUniqueId();
                    var docMetadata = metadata.GetValueOrDefault(targetId) ?? new DocumentMetadata();

                    // Add document node if not already added
                    if (!nodes.Any(n => n.Id == targetId))
                    {
                        nodes.Add(CreateNode(document, docMetadata, isSource: false, depth: 1, similarity: score));
                    }

                    // Connect source directly to semantic matches
                    var edgeId = $"{sourceId}-{targetId}";
                    if (!edges.Any(e => e.Id == edgeId))
                    {
                        var sharedKeywords = options.IncludeKeywords
                            ? FindSharedKeywords(sourceMetadata.ExtractedKeywords, docMetadata.ExtractedKeywords)
                            : [];

                        edges.Add(new DocumentEdge
                        {
                            Id = edgeId,
                            Source = sourceId,
                            Target = targetId,
                            Data = new DocumentEdgeData
                            {
                                Similarity = score,
                                SharedKeywords = sharedKeywords,
                                RelationshipType = RelationshipTypes.Semantic,
                                RelationshipLabel = RelationshipTypes.GetLabel(RelationshipTypes.Semantic)
                            }
                        });
                    }
                }
            }
        }

        // Calculate nodes per level: source (0), parent hubs (1), related docs (2)
        var nodesPerLevel = new List<int> { 1 }; // Level 0: source
        if (parentHubNodes.Count > 0 || relatedDocuments.Any(r => r.RelationType == RelationshipTypes.Semantic))
        {
            nodesPerLevel.Add(parentHubNodes.Count); // Level 1: parent hubs
            nodesPerLevel.Add(nodes.Count - 1 - parentHubNodes.Count); // Level 2: documents
        }

        return new DocumentGraphResponse
        {
            Nodes = nodes,
            Edges = edges,
            Metadata = new GraphMetadata
            {
                SourceDocumentId = sourceId,
                TenantId = options.TenantId,
                TotalResults = relatedDocuments.Count,
                Threshold = options.Threshold,
                Depth = options.Depth,
                MaxDepthReached = parentHubNodes.Count > 0 ? 2 : (relatedDocuments.Count > 0 ? 1 : 0),
                NodesPerLevel = nodesPerLevel,
                SearchLatencyMs = searchLatencyMs,
                CacheHit = false
            }
        };
    }

    /// <summary>
    /// Create a parent hub node based on relationship type.
    /// </summary>
    private DocumentNode? CreateParentHubNode(string relationshipType, DocumentEntity sourceDoc)
    {
        return relationshipType switch
        {
            RelationshipTypes.SameMatter when !string.IsNullOrEmpty(sourceDoc.MatterId) => new DocumentNode
            {
                Id = $"matter-{sourceDoc.MatterId}",
                Type = NodeTypes.Matter,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = sourceDoc.MatterName ?? "Matter",
                    DocumentType = "Matter",
                    RecordUrl = BuildParentRecordUrl("sprk_matter", sourceDoc.MatterId),
                    ExtractedKeywords = []
                }
            },
            RelationshipTypes.SameProject when !string.IsNullOrEmpty(sourceDoc.ProjectId) => new DocumentNode
            {
                Id = $"project-{sourceDoc.ProjectId}",
                Type = NodeTypes.Project,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = sourceDoc.ProjectName ?? "Project",
                    DocumentType = "Project",
                    RecordUrl = BuildParentRecordUrl("sprk_project", sourceDoc.ProjectId),
                    ExtractedKeywords = []
                }
            },
            RelationshipTypes.SameInvoice when !string.IsNullOrEmpty(sourceDoc.InvoiceId) => new DocumentNode
            {
                Id = $"invoice-{sourceDoc.InvoiceId}",
                Type = NodeTypes.Invoice,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = sourceDoc.InvoiceName ?? "Invoice",
                    DocumentType = "Invoice",
                    RecordUrl = BuildParentRecordUrl("sprk_invoice", sourceDoc.InvoiceId),
                    ExtractedKeywords = []
                }
            },
            RelationshipTypes.SameEmail when !string.IsNullOrEmpty(sourceDoc.ParentDocumentId) => new DocumentNode
            {
                Id = $"email-{sourceDoc.ParentDocumentId}",
                Type = NodeTypes.Email,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = sourceDoc.EmailSubject ?? "Email",
                    DocumentType = "Email",
                    RecordUrl = BuildRecordUrl(sourceDoc.ParentDocumentId),
                    ExtractedKeywords = []
                }
            },
            RelationshipTypes.SameEmail when sourceDoc.IsEmailArchive == true => new DocumentNode
            {
                // Source IS the email - use its info as the hub
                Id = $"email-{sourceDoc.Id}",
                Type = NodeTypes.Email,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = sourceDoc.EmailSubject ?? sourceDoc.Name ?? "Email",
                    DocumentType = "Email",
                    RecordUrl = BuildRecordUrl(sourceDoc.Id),
                    ExtractedKeywords = []
                }
            },
            RelationshipTypes.SameThread when !string.IsNullOrEmpty(sourceDoc.EmailConversationIndex) => new DocumentNode
            {
                // Email thread - use conversation index prefix as ID
                Id = $"thread-{sourceDoc.EmailConversationIndex[..Math.Min(44, sourceDoc.EmailConversationIndex.Length)]}",
                Type = NodeTypes.Email,
                Depth = 1,
                Data = new DocumentNodeData
                {
                    Label = "Email Thread",
                    DocumentType = "Email Thread",
                    ExtractedKeywords = []
                }
            },
            _ => null
        };
    }

    /// <summary>
    /// Build Dataverse record URL for parent entities.
    /// </summary>
    private string BuildParentRecordUrl(string entityLogicalName, string entityId)
    {
        var orgUrl = _dataverseOptions.EnvironmentUrl.TrimEnd('/');
        return $"{orgUrl}/main.aspx?etn={entityLogicalName}&id={entityId}&pagetype=entityrecord";
    }

    #endregion

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
        var filter = $"documentId eq '{EscapeFilterValue(documentId)}' and tenantId eq '{EscapeFilterValue(tenantId)}'";
        _logger.LogDebug("[VIZ-DEBUG] GetSourceDocumentAsync: Querying AI Search with filter: {Filter}", filter);

        var searchOptions = new SearchOptions
        {
            Size = 1,
            Filter = filter,
            Select = { "id", "documentId", "speFileId", "fileName", "fileType",
                       "documentType", "tenantId", "createdAt", "updatedAt",
                       "documentVector3072", "metadata", "tags" }
        };

        var response = await searchClient.SearchAsync<VisualizationDocument>("*", searchOptions, cancellationToken);
        var results = response.Value;

        _logger.LogDebug("[VIZ-DEBUG] GetSourceDocumentAsync: Search returned TotalCount={TotalCount}", results.TotalCount);

        await foreach (var result in results.GetResultsAsync().WithCancellation(cancellationToken))
        {
            if (result.Document != null)
            {
                _logger.LogDebug("[VIZ-DEBUG] GetSourceDocumentAsync: Found document Id={Id}, DocumentId={DocId}, VectorLength={VectorLength}",
                    result.Document.Id, result.Document.DocumentId ?? "(null)", result.Document.DocumentVector3072.Length);
                return result.Document;
            }
        }

        _logger.LogDebug("[VIZ-DEBUG] GetSourceDocumentAsync: No document found matching filter");
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
            _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Source document {DocumentId} has no documentVector", sourceDocumentId);
            return [];
        }

        _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Starting vector search with sourceVector.Length={VectorLength}", sourceVector.Length);

        // Build search options with vector search
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
            Select = { "id", "documentId", "speFileId", "fileName", "fileType",
                       "documentType", "tenantId", "createdAt", "updatedAt",
                       "documentVector3072", "metadata", "tags" }
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
        _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Filter={Filter}, VectorField={VectorField}, KNN={KNN}",
            searchOptions.Filter, DocumentVectorFieldName, options.Limit * 2);

        // Execute search
        var response = await searchClient.SearchAsync<VisualizationDocument>("*", searchOptions, cancellationToken);
        var results = response.Value;

        _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Search returned TotalCount={TotalCount}", results.TotalCount);

        // Process results, deduplicating by unique ID (documentId or speFileId for orphan files)
        var seenDocuments = new HashSet<string>();
        var relatedDocuments = new List<(VisualizationDocument Document, double Score)>();
        var rawResultCount = 0;
        var belowThresholdCount = 0;
        var duplicateCount = 0;

        await foreach (var result in results.GetResultsAsync().WithCancellation(cancellationToken))
        {
            rawResultCount++;
            if (result.Document == null) continue;

            var score = result.Score ?? 0;

            // Log first few results for debugging
            if (rawResultCount <= 5)
            {
                _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Result[{Index}] DocId={DocId}, FileName={FileName}, Score={Score}, VectorLen={VectorLen}",
                    rawResultCount, result.Document.DocumentId ?? "(null)", result.Document.FileName ?? "(null)", score, result.Document.DocumentVector3072.Length);
            }

            // Apply threshold filter
            if (score < options.Threshold)
            {
                belowThresholdCount++;
                continue;
            }

            // Deduplicate by unique ID (multiple chunks from same document)
            // Uses documentId if available, otherwise speFileId for orphan files
            var uniqueId = result.Document.GetUniqueId();
            if (!seenDocuments.Add(uniqueId))
            {
                duplicateCount++;
                continue;
            }

            relatedDocuments.Add((result.Document, score));

            // Respect limit
            if (relatedDocuments.Count >= options.Limit)
            {
                break;
            }
        }

        _logger.LogDebug("[VIZ-DEBUG] SearchRelatedDocumentsAsync: Processed {RawCount} results - {BelowThreshold} below threshold ({Threshold}), {Duplicates} duplicates, {Final} final results",
            rawResultCount, belowThresholdCount, options.Threshold, duplicateCount, relatedDocuments.Count);

        return relatedDocuments;
    }

    /// <summary>
    /// Get document metadata from Dataverse for URLs and parent entity info.
    /// For orphan files (no documentId), generates metadata from SPE file info.
    /// </summary>
    private async Task<Dictionary<string, DocumentMetadata>> GetDocumentMetadataAsync(
        VisualizationDocument sourceDocument,
        List<(VisualizationDocument Document, double Score)> relatedDocuments,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, DocumentMetadata>();

        // Collect all documents (source + related)
        var allDocuments = new List<VisualizationDocument> { sourceDocument };
        allDocuments.AddRange(relatedDocuments.Select(r => r.Document));

        // Note: Individual Dataverse fetches are acceptable here due to the limit cap
        // (default 25, max 100 via MaxTotalNodes). Each call is ~20-50ms, so worst case
        // is ~1-2s total which is acceptable for this visualization UX.
        // For bulk scenarios (>100 docs), consider implementing a batch fetch method.
        foreach (var doc in allDocuments)
        {
            var uniqueId = doc.GetUniqueId();

            // Skip if already processed
            if (metadata.ContainsKey(uniqueId)) continue;

            // Handle orphan files (no documentId)
            if (string.IsNullOrEmpty(doc.DocumentId))
            {
                _logger.LogDebug("Processing orphan file {SpeFileId}", doc.SpeFileId);

                metadata[uniqueId] = new DocumentMetadata
                {
                    IsOrphanFile = true,
                    SpeFileId = doc.SpeFileId,
                    RecordUrl = string.Empty, // No Dataverse record for orphan files
                    FileUrl = BuildSpeFileUrl(doc.SpeFileId),
                    FilePreviewUrl = null,
                    ExtractedKeywords = [],
                    ParentEntityType = null,
                    ParentEntityId = null,
                    ParentEntityName = null
                };
                continue;
            }

            // Fetch metadata from Dataverse for documents with documentId
            try
            {
                var entity = await _dataverseService.GetDocumentAsync(doc.DocumentId, cancellationToken);
                if (entity != null)
                {
                    metadata[uniqueId] = new DocumentMetadata
                    {
                        IsOrphanFile = false,
                        SpeFileId = doc.SpeFileId,
                        RecordUrl = BuildRecordUrl(doc.DocumentId),
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
                _logger.LogWarning(ex, "Failed to get metadata for document {DocumentId}", doc.DocumentId);
                // Continue without metadata for this document
                metadata[uniqueId] = new DocumentMetadata
                {
                    IsOrphanFile = false,
                    SpeFileId = doc.SpeFileId,
                    RecordUrl = BuildRecordUrl(doc.DocumentId),
                    FileUrl = string.Empty,
                    ExtractedKeywords = []
                };
            }
        }

        return metadata;
    }

    /// <summary>
    /// Build the graph response from search results.
    /// Supports both regular documents and orphan files (using GetUniqueId() for consistent identification).
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

        // Get unique IDs for all documents (handles both regular docs and orphan files)
        var sourceId = sourceDocument.GetUniqueId();

        // Add source node
        var sourceMetadata = metadata.GetValueOrDefault(sourceId) ?? new DocumentMetadata();
        nodes.Add(CreateNode(sourceDocument, sourceMetadata, isSource: true, depth: 0, similarity: null));

        // Add related nodes and edges
        foreach (var (document, score) in relatedDocuments)
        {
            var targetId = document.GetUniqueId();
            var docMetadata = metadata.GetValueOrDefault(targetId) ?? new DocumentMetadata();
            nodes.Add(CreateNode(document, docMetadata, isSource: false, depth: 1, similarity: score));

            // Create edge from source to related document
            var sharedKeywords = options.IncludeKeywords
                ? FindSharedKeywords(sourceMetadata.ExtractedKeywords, docMetadata.ExtractedKeywords)
                : [];

            edges.Add(new DocumentEdge
            {
                Id = $"{sourceId}-{targetId}",
                Source = sourceId,
                Target = targetId,
                Data = new DocumentEdgeData
                {
                    Similarity = score,
                    SharedKeywords = sharedKeywords,
                    RelationshipType = RelationshipTypes.Semantic,
                    RelationshipLabel = RelationshipTypes.GetLabel(RelationshipTypes.Semantic)
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
                SourceDocumentId = sourceId,
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
        // Determine the document type for display
        // For orphan files, use "File" as the type
        var displayType = metadata.IsOrphanFile
            ? GetFileTypeDisplay(document.FileType)
            : document.DocumentType ?? "Unknown";

        // For orphan files, the node type is "orphan" instead of "related"
        var nodeType = isSource ? "source" : (metadata.IsOrphanFile ? "orphan" : "related");

        return new DocumentNode
        {
            Id = document.GetUniqueId(),
            Type = nodeType,
            Depth = depth,
            Data = new DocumentNodeData
            {
                Label = document.GetDisplayName(),
                DocumentType = displayType,
                FileType = document.FileType,
                SpeFileId = document.SpeFileId,
                IsOrphanFile = metadata.IsOrphanFile,
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

    /// <summary>
    /// Get a display-friendly type string based on file extension.
    /// </summary>
    private static string GetFileTypeDisplay(string? fileType)
    {
        return fileType?.ToLowerInvariant() switch
        {
            "pdf" => "PDF Document",
            "docx" or "doc" => "Word Document",
            "xlsx" or "xls" => "Excel Spreadsheet",
            "pptx" or "ppt" => "PowerPoint Presentation",
            "msg" => "Email",
            "eml" => "Email",
            "txt" => "Text File",
            "csv" => "CSV File",
            _ => "File"
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

    /// <summary>
    /// Build file URL for orphan files using SPE file ID.
    /// These files have no Dataverse record, so we use the SPE file ID directly.
    /// </summary>
    private static string BuildSpeFileUrl(string? speFileId)
    {
        if (string.IsNullOrEmpty(speFileId))
        {
            return string.Empty;
        }

        // For orphan files, return a placeholder URL that the PCF can handle
        // The actual URL would need to be resolved via the SPE API
        return $"spe://{speFileId}";
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
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Link to sprk_document. Nullable for orphan files (files with no Dataverse record).
    /// </summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    /// <summary>
    /// SharePoint Embedded file ID. Always populated (primary identifier for files).
    /// </summary>
    [JsonPropertyName("speFileId")]
    public string? SpeFileId { get; set; }

    /// <summary>
    /// File display name.
    /// </summary>
    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    /// <summary>
    /// File extension/type: pdf, docx, msg, xlsx, etc.
    /// </summary>
    [JsonPropertyName("fileType")]
    public string? FileType { get; set; }

    [JsonPropertyName("documentType")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Document-level vector (3072 dimensions, text-embedding-3-large).
    /// </summary>
    [JsonPropertyName("documentVector3072")]
    public ReadOnlyMemory<float> DocumentVector3072 { get; set; }

    [JsonPropertyName("metadata")]
    public string? Metadata { get; set; }

    [JsonPropertyName("tags")]
    public IList<string>? Tags { get; set; }

    /// <summary>
    /// Gets the document vector for similarity search.
    /// </summary>
    public ReadOnlyMemory<float> GetBestVector() => DocumentVector3072;

    /// <summary>
    /// Gets the display name for the document.
    /// </summary>
    public string GetDisplayName() => FileName ?? "Unknown";

    /// <summary>
    /// Gets the unique identifier for this document (prefers documentId, falls back to speFileId).
    /// </summary>
    public string GetUniqueId()
    {
        return !string.IsNullOrEmpty(DocumentId) ? DocumentId :
               !string.IsNullOrEmpty(SpeFileId) ? SpeFileId : Id;
    }
}

/// <summary>
/// Document metadata from Dataverse for building node data.
/// For orphan files (no Dataverse record), contains SPE file info only.
/// </summary>
internal class DocumentMetadata
{
    /// <summary>
    /// True if this file has no associated Dataverse record (orphan file).
    /// </summary>
    public bool IsOrphanFile { get; set; }

    /// <summary>
    /// SharePoint Embedded file ID (always populated).
    /// </summary>
    public string? SpeFileId { get; set; }

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
