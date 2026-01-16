# Design Document: AI Search & Visualization Module

> **Date**: January 8, 2026
> **Status**: Draft - Design Review
> **Author**: Spaarke AI Team
> **Project**: AI Azure Search Module

---

## 1. Executive Summary

This document outlines the design for the **AI Search & Visualization Module**, a new capability for the Spaarke Power Apps model-driven application. The module enables users to:

1. **Semantic Search**: Perform advanced AI-powered search across documents stored in SharePoint Embedded
2. **Document Relationship Visualization**: Discover and explore relationships between documents based on content similarity, keywords, and metadata
3. **"Find Related" Experience**: Access visualization directly from Document records via a ribbon button that opens an interactive graph modal
4. **Direct Navigation**: Open related Document records in Dataverse AND/OR view files directly from SharePoint Embedded

The system leverages the existing **Spaarke AI Infrastructure** (Azure AI Search, Azure OpenAI, Redis caching, and the R3 RAG architecture) while evaluating integration opportunities with the newly available **Microsoft Foundry IQ** service.

### 1.1 Core User Questions Addressed

This module answers the fundamental questions users have when working with documents:

| User Question | Module Capability |
|---------------|-------------------|
| **"What other documents are like this document?"** | Vector similarity search finds semantically similar documents regardless of naming or folder structure |
| **"What documents are related to this document?"** | Relationship visualization shows connections based on content, keywords, entities, and metadata |
| **"What other Matters or Projects are similar based on their documents?"** | Cross-entity discovery surfaces related records by analyzing document similarity across organizational boundaries |

These questions represent the core value proposition: **enabling users to discover connections between documents that would otherwise remain hidden in traditional folder-based or search-based document management.**

---

## 2. Microsoft Foundry IQ Analysis (January 2026)

### 2.1 Current Foundry IQ Capabilities

Microsoft Foundry IQ is now in **public preview** with significant enhancements since initial release:

| Feature | Description | Spaarke Relevance |
|---------|-------------|-------------------|
| **Agentic Retrieval** | Query decomposition into subqueries, parallel processing, semantic reranking | Could enhance complex "Find Related" queries |
| **Knowledge Bases** | Reusable retrieval configurations across agents/apps | Aligns with multi-tenant architecture |
| **Multi-Source Integration** | Up to 10 knowledge sources including SharePoint, Blob, OneLake | Direct SharePoint Embedded potential |
| **Reflective Search** | Iterative refinement using SLM for hard queries | Improves relevance for ambiguous searches |
| **MCP Integration** | Model Context Protocol for standardized tool calls | Future agent integration path |
| **Purview Integration** | Respects sensitivity labels from Microsoft Purview | Enterprise compliance |

**Performance Claim**: ~36% improvement in RAG answer quality compared to brute-force search.

### 2.2 Spaarke R3 vs Foundry IQ Comparison

| Aspect | Spaarke R3 Stack | Foundry IQ |
|--------|------------------|------------|
| **Backend** | Azure AI Search | Azure AI Search (same) |
| **Embeddings** | Azure OpenAI text-embedding-3-small | Azure OpenAI (same) |
| **Search Type** | Hybrid (BM25 + HNSW + Semantic) | Agentic (query decomposition + rerank) |
| **Caching** | Redis with SHA256 keys, 7-day TTL | Managed (unknown specifics) |
| **Multi-Tenant** | tenantId OData filter, 3 deployment models | Unknown isolation model |
| **SharePoint Embedded** | Custom integration via SpeFileStore | Not explicitly supported (SharePoint M365 only) |
| **Customization** | Full control | Limited to configuration |
| **Maturity** | Production (R3 complete) | Public Preview |

### 2.3 Recommendation

**Decision: Continue with Spaarke R3 architecture for this module.**

**Rationale**:
1. **SharePoint Embedded**: Foundry IQ explicitly supports "SharePoint in Microsoft M365" but not SharePoint Embedded containers. Our custom integration is required.
2. **Multi-Tenant Control**: R3's 3-tier deployment model (Shared/Dedicated/CustomerOwned) provides granular tenant isolation that Foundry IQ doesn't guarantee.
3. **Production Readiness**: R3 is battle-tested; Foundry IQ is in preview.
4. **Existing Investment**: R3 infrastructure is deployed and operational.

**Future Consideration**: Monitor Foundry IQ GA timeline and SharePoint Embedded support. Consider hybrid approach where Foundry IQ handles complex agentic queries while R3 handles document indexing and tenant isolation.

---

## 3. Solution Architecture

### 3.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Power Apps Model-Driven App                         │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  sprk_document Form                                                  │    │
│  │  ┌──────────────────┐    ┌────────────────────────────────────┐    │    │
│  │  │ "Find Related"   │───▶│  DocumentRelationshipViewer PCF    │    │    │
│  │  │  Ribbon Button   │    │  (Modal with React Flow Canvas)    │    │    │
│  │  └──────────────────┘    └────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        │ HTTPS (JWT Auth)
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            Spaarke BFF API                                   │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │  VisualizationEndpoints.cs                                            │   │
│  │  ├── GET  /api/ai/visualization/related/{documentId}                  │   │
│  │  ├── GET  /api/ai/visualization/cluster/{tenantId}                    │   │
│  │  └── POST /api/ai/visualization/explore                               │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                        │                                     │
│  ┌──────────────────┐    ┌─────────────────────┐    ┌──────────────────┐   │
│  │ IVisualization   │───▶│ IRagService (R3)    │───▶│ IEmbeddingCache  │   │
│  │ Service          │    │ Hybrid Search       │    │ Redis            │   │
│  └──────────────────┘    └─────────────────────┘    └──────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                        │
                    ┌───────────────────┴───────────────────┐
                    ▼                                       ▼
        ┌─────────────────────┐                 ┌─────────────────────┐
        │   Azure AI Search   │                 │    Azure OpenAI     │
        │ spaarke-knowledge-  │                 │ text-embedding-     │
        │ index (HNSW 1536)   │                 │ 3-small             │
        └─────────────────────┘                 └─────────────────────┘
```

### 3.2 Data Flow: "Find Related" Operation

```
1. User clicks "Find Related" on Document form
           │
           ▼
2. PCF Control opens modal, calls API with documentId
           │
           ▼
3. VisualizationService retrieves source document embedding
   ├── Check Redis cache (sdap:embedding:{hash})
   └── If miss: Generate via Azure OpenAI, cache result
           │
           ▼
4. Execute vector similarity search
   ├── Query: HNSW nearest neighbors (cosine similarity)
   ├── Filter: tenantId eq '{currentTenant}'
   ├── Threshold: similarity >= 0.65 (configurable)
   └── Limit: top 25 results (configurable)
           │
           ▼
5. Build graph response (Nodes + Edges)
   ├── Center node: Source document
   ├── Related nodes: Similar documents with metadata
   └── Edges: Similarity scores, shared keywords
           │
           ▼
6. PCF renders interactive graph with React Flow
```

---

## 4. Frontend Design: PCF Control Architecture

### 4.1 Control Overview

| Property | Value |
|----------|-------|
| **Control Name** | `DocumentRelationshipViewer` |
| **Type** | Virtual PCF Control (React-based) |
| **Target Entity** | `sprk_document` |
| **Trigger** | Ribbon button command |
| **UI Framework** | Fluent UI v9 + @xyflow/react |

**ADR Compliance**:
- **ADR-006**: PCF over webresources (no legacy JS)
- **ADR-021**: Fluent UI v9 exclusively, dark mode support required
- **ADR-022**: React 16 APIs only, unmanaged solutions

### 4.2 Modal Design: Full-Screen Modal (Selected)

**Decision**: Full-screen modal provides the optimal experience for document relationship visualization.

```
┌─────────────────────────────────────────────────────────────────────┐
│  Document Relationships                                    [X] Close │
├─────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐                                                │
│  │ Controls Panel  │    ┌─────────────────────────────────────┐    │
│  │ ───────────────│    │                                     │    │
│  │ Similarity: 65% │    │         React Flow Canvas           │    │
│  │ [━━━━━━━━━○───] │    │                                     │    │
│  │                 │    │      ┌───┐                          │    │
│  │ Show: 25 docs   │    │      │Doc│──────┐                   │    │
│  │ [━━━━━○───────] │    │      │ A │      │                   │    │
│  │                 │    │      └───┘      │                   │    │
│  │ Depth: 2 levels │    │         │       ▼                   │    │
│  │ [━━━━━○───────] │    │         │    ┌─────┐                │    │
│  │                 │    │         └───▶│SOURCE│◀──┐           │    │
│  │ Filter by Type: │    │              └─────┘   │           │    │
│  │ ☑ Contracts     │    │                 │      │           │    │
│  │ ☑ Invoices      │    │      ┌───┐     │   ┌───┐          │    │
│  │ ☐ Reports       │    │      │Doc│◀────┘   │Doc│          │    │
│  │                 │    │      │ B │         │ C │          │    │
│  └─────────────────┘    └───┴───┘─────────└───┘──────────────┘    │
│                                                                      │
│  Selected: Contract_2024.pdf                                         │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │ [Open Document Record]  [View File in SharePoint]  [Export]    │ │
│  └────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Features**:
- Maximum canvas area for complex graphs
- Control panel with similarity threshold, depth limiting, and document type filters
- **Action bar for selected node**: Open Dataverse record OR view file in SharePoint Embedded
- Professional data visualization experience

**Why Full-Screen**:
- Document networks can be complex with 25+ nodes
- Users need space to explore relationships visually
- Control panel and action bar require dedicated real estate

### 4.3 Layout Algorithm: d3-force (Selected)

React Flow (@xyflow/react) does not include built-in layouting algorithms. After evaluating options, **d3-force** is selected for the document relationship visualization.

**Decision**: Use **d3-force** for force-directed graph layout.

| Criteria | d3-force Fit |
|----------|--------------|
| **Similarity visualization** | Edge length encodes similarity (shorter = more similar) |
| **Natural clustering** | Similar documents naturally group together |
| **Interactive exploration** | Users can drag nodes to rearrange |
| **Real-time updates** | Layout adjusts dynamically as filters change |
| **Performance** | Acceptable for target node count (≤50 per level) |

**Alternatives Considered**:

| Library | Why Not Selected |
|---------|------------------|
| dagre | Better for hierarchical relationships; less organic for similarity networks |
| elkjs | Larger bundle size; overkill for our use case |
| d3-hierarchy | Designed for parent-child trees; not peer relationships |

**Implementation Approach**:
```typescript
// Layout configuration for document similarity graph
const forceLayoutConfig = {
  forceLink: {
    distance: (edge) => 200 * (1 - edge.similarity), // Closer = more similar
    strength: 0.5
  },
  forceManyBody: {
    strength: -300 // Repulsion between nodes
  },
  forceCenter: {
    x: canvasWidth / 2,
    y: canvasHeight / 2
  },
  forceCollide: {
    radius: 60 // Prevent node overlap
  }
};
```

### 4.4 Node and Edge Design

#### Node Types

| Type | Visual | Data |
|------|--------|------|
| **Source** | Large, highlighted, center position | Current document |
| **Related** | Standard size, color by similarity | Similar documents |
| **Cluster** | Group indicator | Documents sharing topic/keywords |

#### Node Component (Fluent UI v9)

```tsx
// Must support dark mode per ADR-021
const DocumentNode: React.FC<NodeProps<DocumentNodeData>> = ({ data }) => {
  const styles = useStyles(); // Fluent UI v9 makeStyles

  return (
    <Card className={styles.nodeCard} appearance={data.isSource ? "filled" : "outline"}>
      <CardHeader
        image={<DocumentIcon />}
        header={<Text weight="semibold">{data.label}</Text>}
        description={<Caption1>{data.documentType}</Caption1>}
      />
      {!data.isSource && (
        <Badge appearance="tint" color={getSimilarityColor(data.similarity)}>
          {Math.round(data.similarity * 100)}% match
        </Badge>
      )}
    </Card>
  );
};
```

#### Edge Styling

| Similarity | Edge Style | Color (Light/Dark) |
|------------|------------|-------------------|
| 90-100% | Thick solid | Green / Teal |
| 75-89% | Medium solid | Blue / Cyan |
| 65-74% | Thin dashed | Gray / Slate |

### 4.5 Graph Data Model

```typescript
interface DocumentGraphResponse {
  nodes: DocumentNode[];
  edges: DocumentEdge[];
  metadata: GraphMetadata;
}

interface DocumentNode {
  id: string;                    // Document GUID
  type: "source" | "related";
  depth: number;                 // 0 = source, 1 = direct relation, 2 = second-level, etc.
  data: {
    label: string;               // Document name
    documentType: string;        // Contract, Invoice, etc.
    similarity?: number;         // 0-1 score (null for source)
    extractedKeywords: string[]; // From AI extraction
    createdOn: string;           // ISO date
    modifiedOn: string;
    // Navigation URLs (both required for user actions)
    recordUrl: string;           // Dataverse record URL for "Open Document Record"
    fileUrl: string;             // SPE file URL for "View File in SharePoint"
    filePreviewUrl?: string;     // SPE inline preview URL (if available)
    // Parent entity reference (for Matter/Project similarity)
    parentEntityType?: string;   // "sprk_matter" | "sprk_project" | null
    parentEntityId?: string;     // GUID of parent Matter/Project
    parentEntityName?: string;   // Display name of parent
  };
  position?: { x: number; y: number }; // Optional initial position
}

interface DocumentEdge {
  id: string;
  source: string;               // Source node ID
  target: string;               // Target node ID
  data: {
    similarity: number;         // Cosine similarity score
    sharedKeywords: string[];   // Common extracted keywords
    relationshipType: "semantic" | "keyword" | "metadata";
  };
}

interface GraphMetadata {
  sourceDocumentId: string;
  tenantId: string;
  totalResults: number;
  threshold: number;
  depth: number;                 // Requested depth level
  maxDepthReached: number;       // Actual max depth in results
  nodesPerLevel: number[];       // Count of nodes at each level [1, 18, 45]
  searchLatencyMs: number;
  cacheHit: boolean;
}
```

---

## 5. Backend API Design

### 5.1 New Endpoints

#### GET /api/ai/visualization/related/{documentId}

Find documents related to a specific document.

**Request**:
```http
GET /api/ai/visualization/related/3fa85f64-5717-4562-b3fc-2c963f66afa6
Authorization: Bearer {jwt}
X-Tenant-Id: {tenantId}
```

**Query Parameters**:
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `threshold` | float | 0.65 | Minimum similarity score (0-1) |
| `limit` | int | 25 | Maximum related documents per level |
| `depth` | int | 1 | Relationship depth (1-3 levels) |
| `includeKeywords` | bool | true | Include shared keywords in edges |
| `documentTypes` | string[] | null | Filter by document type |
| `includeParentEntity` | bool | true | Include parent Matter/Project info |

**Response**:
```json
{
  "nodes": [...],
  "edges": [...],
  "metadata": {
    "sourceDocumentId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "tenantId": "customer-123",
    "totalResults": 18,
    "threshold": 0.65,
    "searchLatencyMs": 245,
    "cacheHit": true
  }
}
```

#### GET /api/ai/visualization/cluster/{tenantId}

Get document clusters for a tenant (future enhancement).

#### POST /api/ai/visualization/explore

Explore relationships from multiple seed documents (future enhancement).

### 5.2 Service Architecture

```csharp
public interface IVisualizationService
{
    Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
        Guid documentId,
        VisualizationOptions options,
        CancellationToken ct);

    Task<DocumentClusterResponse> GetDocumentClustersAsync(
        string tenantId,
        ClusterOptions options,
        CancellationToken ct);
}

public class VisualizationService : IVisualizationService
{
    private readonly IRagService _ragService;
    private readonly IEmbeddingCache _embeddingCache;
    private readonly IDataverseClient _dataverseClient;
    private readonly ILogger<VisualizationService> _logger;

    public async Task<DocumentGraphResponse> GetRelatedDocumentsAsync(
        Guid documentId,
        VisualizationOptions options,
        CancellationToken ct)
    {
        // 1. Get source document embedding
        var sourceDoc = await _dataverseClient.GetDocumentAsync(documentId, ct);
        var embedding = await GetOrCreateEmbeddingAsync(sourceDoc, ct);

        // 2. Vector search for similar documents
        var searchResults = await _ragService.SearchAsync(
            embedding,
            new RagSearchOptions
            {
                TenantId = options.TenantId,
                TopK = options.Limit,
                MinScore = options.Threshold,
                DocumentTypes = options.DocumentTypes,
                ExcludeIds = new[] { documentId.ToString() }
            },
            ct);

        // 3. Build graph response
        return BuildGraphResponse(sourceDoc, searchResults, options);
    }
}
```

### 5.3 Endpoint Filter for Authorization

Per ADR-008, use endpoint filters (not global middleware):

```csharp
public class VisualizationAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var documentId = context.GetArgument<Guid>(0);
        var user = context.HttpContext.User;

        // Verify user has access to the source document
        var hasAccess = await _authService.CanAccessDocumentAsync(
            user, documentId, context.HttpContext.RequestAborted);

        if (!hasAccess)
            return Results.Forbid();

        return await next(context);
    }
}
```

---

## 6. Data Source & Indexing

### 6.1 Index Schema Requirements

The existing `spaarke-knowledge-index` provides the necessary fields:

| Field | Purpose for Visualization |
|-------|--------------------------|
| `id` | Unique chunk identifier |
| `documentId` | Parent document reference (for grouping chunks) |
| `tenantId` | Multi-tenant isolation |
| `documentName` | Node labels |
| `documentType` | Filtering, node styling |
| `contentVector` | Similarity calculation (1536 dims) |
| `tags` | Shared keyword detection |
| `metadata` | Additional display data |

### 6.2 Embedding Strategy for Documents

**Challenge**: Documents are chunked for RAG; we need document-level similarity.

**Options**:

| Strategy | Description | Pros | Cons |
|----------|-------------|------|------|
| **A. First Chunk** | Use embedding of first chunk | Simple, fast | May miss document essence |
| **B. Average Pooling** | Average all chunk embeddings | Captures full document | Requires aggregation query |
| **C. Summary Embedding** | Embed AI-generated summary | Best semantic representation | Requires summary generation |
| **D. Dedicated Field** | Store document-level embedding | Fast retrieval | Additional storage, sync needed |

**Recommendation**: **Option D (Dedicated Field)** for optimal performance:
1. When document is indexed, also generate and store a `documentVector` field
2. Query `documentVector` for visualization (not chunk-level `contentVector`)
3. Sync on document update

**Index Enhancement**:
```json
{
  "name": "documentVector",
  "type": "Collection(Edm.Single)",
  "dimensions": 1536,
  "vectorSearchProfile": "knowledge-vector-profile",
  "searchable": false
}
```

### 6.3 Aggregation for Existing Data

For documents already indexed (without `documentVector`), use aggregation:

```csharp
// Fallback: compute document embedding from chunks
var chunks = await _searchClient.SearchAsync<KnowledgeDocument>(
    "*",
    new SearchOptions
    {
        Filter = $"documentId eq '{documentId}'",
        Select = { "contentVector" }
    },
    ct);

// Average pooling
var documentEmbedding = AverageVectors(chunks.Select(c => c.ContentVector));
```

---

## 7. Security & Access Control

### 7.1 Authorization Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Authorization Flow                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. JWT Token Validation (BFF API)                                          │
│     └── Verify token, extract user claims                                   │
│                                                                              │
│  2. Tenant Isolation (IRagService)                                          │
│     └── Filter: tenantId eq '{userTenant}'                                  │
│                                                                              │
│  3. Document Access Check (VisualizationAuthorizationFilter)                │
│     └── Verify user can access source document via Dataverse security       │
│                                                                              │
│  4. Result Filtering (VisualizationService)                                 │
│     └── Only return documents user has permission to view                   │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 7.2 Security Considerations

| Concern | Mitigation |
|---------|------------|
| **Cross-Tenant Data Leakage** | Mandatory tenantId filter on all queries |
| **Unauthorized Document Access** | Verify Dataverse permissions for each result |
| **Embedding Exposure** | Never return raw embeddings to client |
| **Rate Limiting** | Apply `ai-standard` rate limit policy |

### 7.3 Multi-Tenant Deployment Models

The visualization service inherits R3's deployment model support:

| Model | Visualization Behavior |
|-------|----------------------|
| **Shared** | Filter by tenantId within `spaarke-knowledge-index` |
| **Dedicated** | Query tenant-specific index |
| **CustomerOwned** | Query customer's Azure AI Search instance |

---

## 8. Performance Considerations

### 8.1 Target Metrics

| Metric | Target | Rationale |
|--------|--------|-----------|
| **API Latency (P95)** | < 500ms | Interactive UX requirement |
| **Graph Render Time** | < 200ms | Smooth visualization |
| **Max Nodes (Total)** | 100 | Performance and usability |
| **Max Nodes per Level** | 25-50 | Prevents exponential growth |

### 8.2 Depth Limiting Strategy

**Critical for Performance**: Without depth limiting, graph size grows exponentially.

| Depth | Nodes (25/level) | Nodes (50/level) | Recommended Use |
|-------|------------------|------------------|-----------------|
| 1 | 1 + 25 = 26 | 1 + 50 = 51 | Default for initial load |
| 2 | 1 + 25 + 625 = 651 | 1 + 50 + 2500 = 2551 | On-demand expansion |
| 3 | Potentially 15,000+ | Potentially 125,000+ | NOT recommended |

**Implementation**:
1. **Default**: Depth = 1, Limit = 25 (26 total nodes maximum)
2. **User Expansion**: Click "Expand" on a node to load its Level 2 connections
3. **Hard Cap**: Maximum 100 visible nodes regardless of settings
4. **Server-side Pruning**: If depth > 1 requested, return most similar nodes per level

### 8.3 Optimization Strategies

| Strategy | Implementation |
|----------|---------------|
| **Depth Limiting** | Default depth = 1; expand on demand |
| **Embedding Cache** | Redis cache with 7-day TTL (existing) |
| **Document Vector Cache** | Cache document-level embeddings |
| **Lazy Loading** | Initial load at depth 1; expand nodes on demand |
| **Progressive Rendering** | Render Level 0-1 immediately; Level 2+ on expand |
| **Result Prefetch** | Prefetch Level 2 for high-similarity nodes |

### 8.4 Scaling Considerations

| Scenario | Recommendation |
|----------|----------------|
| **High query volume** | Increase AI Search replicas |
| **Large document corpus (>100k)** | Consider index partitioning |
| **Complex graphs (>50 nodes)** | Use depth limiting, server-side clustering |
| **Slow graph rendering** | Reduce max nodes per level, use virtualization |

---

## 9. User Experience Design

### 9.1 Interaction Patterns

| Interaction | Behavior |
|-------------|----------|
| **Hover on Node** | Show tooltip with document name, type, similarity score, and parent Matter/Project |
| **Click on Node** | Select node, show action bar with navigation options |
| **Double-Click on Node** | Quick action: Open Document record in Dataverse (new tab) |
| **Drag Node** | Reposition (force layout adjusts) |
| **Mouse Wheel** | Zoom in/out |
| **Pan (Drag Canvas)** | Navigate large graphs |
| **Click on Edge** | Show relationship details (similarity score, shared keywords) |
| **Expand Node** | Load next level of related documents (depth + 1) |

### 9.2 Node Action Bar (When Selected)

When a user clicks on a node, an action bar appears with navigation options:

```
┌────────────────────────────────────────────────────────────────────────┐
│  Selected: NDA_Acme_Corp_2024.pdf (Contract, 87% match)                │
│  Parent: Acme Corporation Matter                                        │
├────────────────────────────────────────────────────────────────────────┤
│  [📄 Open Document Record]  [📁 View File in SharePoint]  [🔍 Expand]  │
└────────────────────────────────────────────────────────────────────────┘
```

| Action | Description | Target |
|--------|-------------|--------|
| **Open Document Record** | Opens the `sprk_document` record in Dataverse | New browser tab |
| **View File in SharePoint** | Opens the file in SharePoint Embedded viewer/editor | New browser tab or inline preview |
| **Expand** | Loads related documents for this node (next depth level) | Updates graph in-place |

**Implementation Notes**:
- `recordUrl` → Dataverse form URL: `https://{org}.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=sprk_document&id={guid}`
- `fileUrl` → SPE file URL via `SpeFileStore.GetFileUrlAsync()` (respects user permissions)

### 9.3 Control Panel Features

| Feature | Description | Default |
|---------|-------------|---------|
| **Similarity Threshold** | Slider (50-95%) to filter edges by minimum similarity | 65% |
| **Depth Limit** | Slider (1-3 levels) to control relationship depth | 2 levels |
| **Max Nodes per Level** | Slider (10-50) to limit nodes at each depth | 25 |
| **Document Type Filter** | Checkboxes to show/hide by document type | All selected |
| **Search/Filter** | Text search within visible nodes | - |
| **Export** | PNG image, JSON data, CSV | - |

**Depth Limiting Explained**:
- **Level 0**: Source document (always 1 node)
- **Level 1**: Documents directly related to source (default: up to 25)
- **Level 2**: Documents related to Level 1 documents (default: up to 25 per Level 1 node)
- **Level 3**: Maximum depth for performance (optional, disabled by default)

Depth limiting is critical for performance - without it, the graph could grow exponentially.

### 9.4 Accessibility Requirements (ADR-021)

| Requirement | Implementation |
|-------------|---------------|
| **Keyboard Navigation** | Tab between nodes, Enter to select |
| **Screen Reader** | ARIA labels for nodes and edges |
| **High Contrast** | Support Windows High Contrast mode |
| **Dark Mode** | Full support via Fluent UI v9 tokens |

---

## 10. Implementation Phases

### Phase 1: Core Infrastructure
- [ ] Create `IVisualizationService` interface and implementation
- [ ] Implement `GET /api/ai/visualization/related/{documentId}` endpoint
- [ ] Add document-level embedding support to indexing pipeline
- [ ] Create endpoint authorization filter
- [ ] Unit tests with mocked dependencies

### Phase 2: PCF Control Development
- [ ] Scaffold `DocumentRelationshipViewer` PCF control
- [ ] Integrate @xyflow/react with d3-force layout
- [ ] Implement Fluent UI v9 node components (light/dark mode)
- [ ] Create control panel with filters and settings
- [ ] Component tests with React Testing Library

### Phase 3: Integration & Ribbon Button
- [ ] Register PCF control on `sprk_document` entity
- [ ] Create ribbon button command with JavaScript handler
- [ ] Implement modal dialog launcher
- [ ] End-to-end testing in Dataverse environment

### Phase 4: Polish & Advanced Features
- [ ] Add clustering visualization
- [ ] Implement export functionality (PNG, JSON, CSV)
- [ ] Performance optimization (lazy loading, prefetch)
- [ ] Accessibility audit and fixes
- [ ] User documentation

---

## 11. Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| **Performance with large graphs** | Medium | High | Implement pagination, clustering, lazy loading |
| **React Flow bundle size** | Low | Medium | Tree-shaking, dynamic imports |
| **Embedding drift** | Low | Medium | Re-index on model version changes |
| **User confusion** | Medium | Medium | Clear onboarding, tooltips, documentation |
| **Cross-browser issues** | Low | Low | Test matrix, Fluent UI v9 handles most |

---

## 12. Design Decisions (Confirmed)

All design decisions have been reviewed and confirmed.

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| 1 | **Modal Design** | Full-screen modal | Maximum canvas area for complex graphs |
| 2 | **Layout Algorithm** | d3-force | Natural clustering, similarity-based edge length |
| 3 | **Depth Limiting** | Default depth = 1, max = 3 | Critical for performance (prevents exponential growth) |
| 4 | **Node Actions** | Both Record + File | Users need to open Dataverse record AND/OR view SPE file |
| 5 | **Embedding Strategy** | Dedicated `documentVector` field | Optimal performance; aggregation fallback for existing data |
| 6 | **Node Limit per Level** | 25 default, slider (10-50) | Reasonable default with user adjustment capability |
| 7 | **Similarity Threshold** | 65% default, adjustable | May need tuning based on real data; user can adjust |
| 8 | **Keyword Extraction** | Use existing extract fields | Leverage `sprk_extractpeople`, `sprk_extractorganization` for `sharedKeywords`; extensible for future entity types |
| 9 | **Cross-Entity Discovery** | Parent entity info in node data | Show Matter/Project context directly on nodes |
| 10 | **File Preview Method** | New browser tab | **IMPORTANT**: Reuse existing SPE viewer PCF/components - DO NOT rebuild |

### 12.1 Component Reuse Requirements

**Critical**: This module must reuse existing Spaarke components wherever possible:

| Capability | Existing Component | Reuse Strategy |
|------------|-------------------|----------------|
| **SPE File Viewer** | Existing PCF control | Open via `fileUrl` in new tab |
| **Dataverse Navigation** | Standard Xrm.Navigation | Use `Xrm.Navigation.openForm()` |
| **Document Record** | Existing form | Navigate to existing `sprk_document` form |
| **Authentication** | Existing BFF auth | JWT tokens via existing auth flow |
| **Error Handling** | Standard patterns | Use existing error handling utilities |

**Do NOT rebuild**:
- SharePoint Embedded file viewer
- Document record forms
- Authentication/authorization flows

---

## 13. Design Completeness Checklist

| Area | Status | Notes |
|------|--------|-------|
| **Core User Questions** | ✅ Defined | Section 1.1 - three key questions addressed |
| **Architecture** | ✅ Complete | PCF → BFF API → R3 → Azure AI Search |
| **Microsoft Foundry IQ** | ✅ Evaluated | Decision: Stay with R3 (SharePoint Embedded not supported) |
| **Modal Design** | ✅ Confirmed | Full-screen modal with action bar |
| **Layout Algorithm** | ✅ Confirmed | d3-force with similarity-based edge length |
| **Depth Limiting** | ✅ Confirmed | Default 1, max 3, with node expansion |
| **Node Actions** | ✅ Confirmed | Open Record + View File in SPE (reuse existing) |
| **Embedding Strategy** | ✅ Confirmed | Dedicated `documentVector` field with fallback |
| **Keyword Extraction** | ✅ Confirmed | Use existing extract fields, extensible |
| **Cross-Entity Discovery** | ✅ Confirmed | Parent entity info in node data |
| **Component Reuse** | ✅ Confirmed | Reuse existing SPE viewer, navigation, auth |
| **API Design** | ✅ Complete | GET /api/ai/visualization/related/{id} with depth param |
| **Data Model** | ✅ Complete | DocumentNode with depth, parentEntity, fileUrl |
| **Security** | ✅ Covered | 4-layer auth model, tenant isolation |
| **Performance** | ✅ Covered | Depth limiting, caching, lazy loading |
| **Accessibility** | ✅ Covered | ADR-021 compliance, keyboard nav, dark mode |
| **Implementation Phases** | ✅ Defined | 4 phases with clear deliverables |
| **ADR Compliance** | ✅ Verified | ADR-006, 008, 009, 021, 022 referenced |

### Implementation Details (Deferred to Spec/Tasks)

These items will be addressed during implementation using standard Spaarke patterns:

| Item | Approach |
|------|----------|
| **Error Handling UX** | Use standard error handling patterns (toast notifications, inline errors) |
| **Loading States** | Standard Fluent UI v9 spinners and skeletons |
| **Telemetry** | Defer to Phase 4 - nice-to-have |
| **Offline/Disconnected** | Show standard error message |
| **Mobile/Tablet** | Not in scope - model-driven apps are primarily desktop |

---

## 14. References

### Spaarke Documentation
- [RAG Architecture Guide](../../docs/guides/RAG-ARCHITECTURE.md)
- [RAG Configuration Reference](../../docs/guides/RAG-CONFIGURATION.md)
- [AI Azure Resources](../../docs/architecture/auth-AI-azure-resources.md)
- [Microsoft IQ Adoption Analysis](../../docs/architecture/Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md)
- [AI Foundry Infrastructure](../../infrastructure/ai-foundry/README.md)

### Microsoft Documentation
- [Foundry IQ Overview](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/foundry-iq-unlocking-ubiquitous-knowledge-for-agents/4470812)
- [Foundry IQ Agentic Retrieval](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/foundry-iq-boost-response-relevance-by-36-with-agentic-retrieval/4470720)
- [Azure AI Search Vector Search](https://learn.microsoft.com/en-us/azure/search/vector-search-overview)
- [Azure AI Search Semantic Ranking](https://learn.microsoft.com/en-us/azure/search/semantic-search-overview)
- [Multi-Vector and Semantic Ranking Enhancements](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/introducing-multi-vector-and-scoring-profile-integration-with-semantic-ranking-i/4418313)

### React Flow / XyFlow
- [React Flow Documentation](https://reactflow.dev/)
- [Force Layout Example](https://reactflow.dev/examples/layout/force-layout)
- [Auto Layout Guide](https://reactflow.dev/learn/layouting/layouting)

### ADRs
- ADR-006: PCF over webresources
- ADR-008: Endpoint filters for authorization
- ADR-009: Redis-first caching
- ADR-021: Fluent UI v9 Design System
- ADR-022: PCF Platform Libraries

---

*Document created: January 8, 2026*
*Status: **APPROVED** - Ready for spec.md generation*
