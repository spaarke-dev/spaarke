namespace Sprk.Bff.Api.Services.Ai.Visualization;

/// <summary>
/// Provides document relationship visualization capabilities using Azure AI Search vector similarity.
/// Enables users to discover semantically related documents and visualize document networks.
/// </summary>
/// <remarks>
/// Visualization pipeline:
/// 1. Retrieve source document embedding from Azure AI Search
/// 2. Execute vector similarity search to find related documents
/// 3. Build graph structure with nodes (documents) and edges (relationships)
/// 4. Return graph data for React Flow visualization
///
/// Integrates with IRagService for vector operations and embedding cache.
/// Supports multi-tenant isolation via tenantId filtering.
/// </remarks>
public interface IVisualizationService
{
    /// <summary>
    /// Find documents related to a specific document using vector similarity search.
    /// </summary>
    /// <param name="documentId">The source document ID (sprk_document GUID).</param>
    /// <param name="options">Visualization options including threshold, limit, and filters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Graph response with nodes, edges, and metadata for visualization.</returns>
    Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
        Guid documentId,
        VisualizationOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Options for document relationship visualization queries.
/// </summary>
public record VisualizationOptions
{
    /// <summary>
    /// Tenant identifier for multi-tenant routing.
    /// Required for all visualization operations.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Minimum similarity score threshold (0.0-1.0).
    /// Documents below this score are filtered out.
    /// Default: 0.65
    /// </summary>
    public float Threshold { get; init; } = 0.65f;

    /// <summary>
    /// Maximum number of related documents per level.
    /// Default: 25, Max: 50
    /// </summary>
    public int Limit { get; init; } = 25;

    /// <summary>
    /// Relationship depth (1-3 levels).
    /// 1 = source + direct relations
    /// 2 = includes second-level connections
    /// 3 = maximum depth
    /// Default: 1
    /// </summary>
    public int Depth { get; init; } = 1;

    /// <summary>
    /// Whether to include shared keywords in edge data.
    /// Shared keywords are extracted from sprk_extractpeople, sprk_extractorganization fields.
    /// Default: true
    /// </summary>
    public bool IncludeKeywords { get; init; } = true;

    /// <summary>
    /// Optional filter by document types.
    /// e.g., ["Contract", "Invoice", "Agreement"]
    /// Null means all document types.
    /// </summary>
    public IList<string>? DocumentTypes { get; init; }

    /// <summary>
    /// Whether to include parent entity (Matter/Project) information in node data.
    /// Enables cross-entity discovery.
    /// Default: true
    /// </summary>
    public bool IncludeParentEntity { get; init; } = true;
}

/// <summary>
/// Response containing graph data for document relationship visualization.
/// </summary>
public record DocumentGraphResponse
{
    /// <summary>
    /// Document nodes in the graph.
    /// First node is always the source document (depth=0).
    /// </summary>
    public IReadOnlyList<DocumentNode> Nodes { get; init; } = [];

    /// <summary>
    /// Relationship edges connecting document nodes.
    /// Each edge represents a similarity relationship between two documents.
    /// </summary>
    public IReadOnlyList<DocumentEdge> Edges { get; init; } = [];

    /// <summary>
    /// Metadata about the graph query and results.
    /// </summary>
    public GraphMetadata Metadata { get; init; } = new();
}

/// <summary>
/// A document node in the relationship graph.
/// </summary>
public record DocumentNode
{
    /// <summary>
    /// Document GUID (sprk_document ID).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Node type: "source" for the queried document, "related" for similar documents.
    /// </summary>
    public string Type { get; init; } = "related";

    /// <summary>
    /// Depth level in the graph.
    /// 0 = source document
    /// 1 = directly related
    /// 2+ = second-level and beyond
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Document data for display and navigation.
    /// </summary>
    public DocumentNodeData Data { get; init; } = new();

    /// <summary>
    /// Optional initial position for the node.
    /// Used when layout positions are pre-calculated.
    /// </summary>
    public NodePosition? Position { get; init; }
}

/// <summary>
/// Data associated with a document node.
/// </summary>
public record DocumentNodeData
{
    /// <summary>
    /// Document display name.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Document type (e.g., Contract, Invoice, Agreement).
    /// </summary>
    public string DocumentType { get; init; } = string.Empty;

    /// <summary>
    /// Similarity score to the source document (0.0-1.0).
    /// Null for the source document itself.
    /// </summary>
    public double? Similarity { get; init; }

    /// <summary>
    /// Keywords extracted by AI analysis.
    /// From sprk_extractpeople, sprk_extractorganization, etc.
    /// </summary>
    public IReadOnlyList<string> ExtractedKeywords { get; init; } = [];

    /// <summary>
    /// Document creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedOn { get; init; }

    /// <summary>
    /// Document last modified timestamp.
    /// </summary>
    public DateTimeOffset ModifiedOn { get; init; }

    /// <summary>
    /// Dataverse record URL for "Open Document Record" action.
    /// Format: https://{org}.crm.dynamics.com/main.aspx?...
    /// </summary>
    public string RecordUrl { get; init; } = string.Empty;

    /// <summary>
    /// SharePoint Embedded file URL for "View File in SharePoint" action.
    /// </summary>
    public string FileUrl { get; init; } = string.Empty;

    /// <summary>
    /// Optional inline preview URL for the file.
    /// </summary>
    public string? FilePreviewUrl { get; init; }

    /// <summary>
    /// Parent entity type if document is associated with a Matter/Project.
    /// e.g., "sprk_matter", "sprk_project"
    /// </summary>
    public string? ParentEntityType { get; init; }

    /// <summary>
    /// Parent entity GUID if document is associated with a Matter/Project.
    /// </summary>
    public string? ParentEntityId { get; init; }

    /// <summary>
    /// Parent entity display name.
    /// </summary>
    public string? ParentEntityName { get; init; }
}

/// <summary>
/// Position coordinates for a node.
/// </summary>
public record NodePosition
{
    /// <summary>
    /// X coordinate.
    /// </summary>
    public double X { get; init; }

    /// <summary>
    /// Y coordinate.
    /// </summary>
    public double Y { get; init; }
}

/// <summary>
/// A relationship edge connecting two document nodes.
/// </summary>
public record DocumentEdge
{
    /// <summary>
    /// Unique edge identifier.
    /// Format: {sourceId}-{targetId}
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Source node ID (document GUID).
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Target node ID (document GUID).
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Edge data including similarity and shared keywords.
    /// </summary>
    public DocumentEdgeData Data { get; init; } = new();
}

/// <summary>
/// Data associated with a relationship edge.
/// </summary>
public record DocumentEdgeData
{
    /// <summary>
    /// Cosine similarity score between the connected documents (0.0-1.0).
    /// Higher values indicate stronger semantic similarity.
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// Keywords shared between the connected documents.
    /// Useful for explaining why documents are related.
    /// </summary>
    public IReadOnlyList<string> SharedKeywords { get; init; } = [];

    /// <summary>
    /// Type of relationship.
    /// "semantic" = based on vector similarity
    /// "keyword" = based on shared extracted keywords
    /// "metadata" = based on shared metadata (e.g., same parent entity)
    /// </summary>
    public string RelationshipType { get; init; } = "semantic";
}

/// <summary>
/// Metadata about the graph query and results.
/// </summary>
public record GraphMetadata
{
    /// <summary>
    /// The source document ID that was queried.
    /// </summary>
    public string SourceDocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Tenant ID for the query.
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// Total number of related documents found (before limit applied).
    /// </summary>
    public int TotalResults { get; init; }

    /// <summary>
    /// Similarity threshold used for filtering.
    /// </summary>
    public float Threshold { get; init; }

    /// <summary>
    /// Requested depth level.
    /// </summary>
    public int Depth { get; init; }

    /// <summary>
    /// Actual maximum depth reached in results.
    /// May be less than requested if no documents found at deeper levels.
    /// </summary>
    public int MaxDepthReached { get; init; }

    /// <summary>
    /// Count of nodes at each depth level.
    /// Index 0 = depth 0 (source), Index 1 = depth 1 (direct relations), etc.
    /// </summary>
    public IReadOnlyList<int> NodesPerLevel { get; init; } = [];

    /// <summary>
    /// Time taken for the search operation in milliseconds.
    /// </summary>
    public long SearchLatencyMs { get; init; }

    /// <summary>
    /// Whether the source document embedding was retrieved from cache.
    /// </summary>
    public bool CacheHit { get; init; }
}
