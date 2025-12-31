# RAG Architecture Guide

> **Version**: 1.0
> **Created**: 2025-12-29
> **Project**: AI Document Intelligence R3
> **Status**: Phase 1 Complete

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture Components](#architecture-components)
3. [Deployment Models](#deployment-models)
4. [Hybrid Search Pipeline](#hybrid-search-pipeline)
5. [Index Schema](#index-schema)
6. [Service Architecture](#service-architecture)
7. [Embedding Cache](#embedding-cache)
8. [Security and Isolation](#security-and-isolation)
9. [Performance Characteristics](#performance-characteristics)
10. [Integration Points](#integration-points)

---

## Overview

The Spaarke RAG (Retrieval-Augmented Generation) system provides knowledge retrieval capabilities for AI Document Intelligence features. It enables:

- **Hybrid Search**: Combines keyword, vector, and semantic ranking for optimal relevance
- **Multi-Tenant Isolation**: Three deployment models (Shared, Dedicated, CustomerOwned)
- **High Performance**: Redis-cached embeddings, P95 < 500ms target latency
- **Scalability**: Per-customer indexes for enterprise customers

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Search Platform | Azure AI Search | Native Azure integration, semantic ranking |
| Embedding Model | text-embedding-3-small | 1536 dims, cost-effective, high quality |
| Caching | Redis (ADR-009) | Consistent with platform caching strategy |
| Search Type | Hybrid | Best accuracy: keyword + vector + semantic |

---

## Architecture Components

```
┌─────────────────────────────────────────────────────────────────┐
│                        BFF API Layer                            │
├─────────────────────────────────────────────────────────────────┤
│  RagEndpoints.cs                                                │
│  ├── POST /api/ai/rag/search      → Hybrid search               │
│  ├── POST /api/ai/rag/index       → Index document              │
│  ├── POST /api/ai/rag/index/batch → Batch index                 │
│  ├── DELETE /api/ai/rag/{id}      → Delete document             │
│  ├── DELETE /api/ai/rag/source/{id} → Delete by source          │
│  └── POST /api/ai/rag/embedding   → Generate embedding          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Service Layer                              │
├─────────────────────────────────────────────────────────────────┤
│  IRagService (RagService.cs)                                    │
│  ├── SearchAsync()         → Hybrid search with semantic ranking│
│  ├── IndexDocumentAsync()  → Index single document chunk        │
│  ├── DeleteDocumentAsync() → Delete by document ID              │
│  └── GetEmbeddingAsync()   → Generate/cache embedding           │
│                                                                 │
│  IKnowledgeDeploymentService (KnowledgeDeploymentService.cs)    │
│  ├── GetDeploymentConfigAsync() → Get/create tenant config      │
│  ├── GetSearchClientAsync()     → Route to correct index        │
│  └── SaveDeploymentConfigAsync() → Persist config               │
│                                                                 │
│  IEmbeddingCache (EmbeddingCache.cs)                           │
│  ├── GetEmbeddingAsync()        → Retrieve cached embedding     │
│  └── SetEmbeddingAsync()        → Store embedding with TTL      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    External Services                            │
├─────────────────────────────────────────────────────────────────┤
│  Azure AI Search                Azure OpenAI                    │
│  ├── spaarke-knowledge-index    ├── text-embedding-3-small      │
│  ├── {tenant}-knowledge         └── 1536 dimensions             │
│  └── Customer indexes                                           │
│                                                                 │
│  Redis Cache                    Key Vault                       │
│  └── sdap:embedding:{hash}      └── CustomerOwned API keys      │
└─────────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | DI Lifetime |
|-----------|---------------|-------------|
| `RagEndpoints` | HTTP endpoint definitions, request validation | N/A (static) |
| `IRagService` | Search, indexing, embedding orchestration | Scoped |
| `IKnowledgeDeploymentService` | Tenant config, SearchClient routing | Singleton |
| `IEmbeddingCache` | Redis-based embedding caching | Singleton |
| `IOpenAiClient` | Azure OpenAI API calls (embeddings + chat) | Singleton |

---

## Deployment Models

The RAG system supports three deployment models to accommodate different customer requirements:

### Shared Model (Default)

```
┌─────────────────────────────────────────┐
│      spaarke-knowledge-index            │
├─────────────────────────────────────────┤
│  tenantId: "tenant-a" │ document data   │
│  tenantId: "tenant-b" │ document data   │
│  tenantId: "tenant-c" │ document data   │
└─────────────────────────────────────────┘
              │
              ▼ Filter: tenantId == "tenant-a"
┌─────────────────────────────────────────┐
│  Results for tenant-a only              │
└─────────────────────────────────────────┘
```

| Aspect | Details |
|--------|---------|
| **Index Name** | `spaarke-knowledge-index` |
| **Isolation** | Logical (tenantId filter on all queries) |
| **Cost** | Lowest (shared infrastructure) |
| **Use Case** | SMB customers, default deployment |
| **Configuration** | None required (auto-detected) |

### Dedicated Model

```
┌──────────────────────┐  ┌──────────────────────┐
│ tenant-a-knowledge   │  │ tenant-b-knowledge   │
├──────────────────────┤  ├──────────────────────┤
│  Tenant A docs only  │  │  Tenant B docs only  │
└──────────────────────┘  └──────────────────────┘
```

| Aspect | Details |
|--------|---------|
| **Index Name** | `{sanitizedTenantId}-knowledge` |
| **Isolation** | Physical (separate index per customer) |
| **Cost** | Higher (dedicated resources) |
| **Use Case** | Enterprise, compliance requirements |
| **Configuration** | Set `Model = Dedicated` in deployment config |

**Index Name Sanitization**:
- Converted to lowercase
- Non-alphanumeric characters removed (except hyphens)
- Format: `{sanitized-tenant}-knowledge`

Examples:
| Tenant ID | Sanitized Index Name |
|-----------|---------------------|
| `Tenant-ABC-123` | `tenantabc123-knowledge` |
| `ENTERPRISE_CORP` | `enterprisecorp-knowledge` |
| `acme.inc` | `acmeinc-knowledge` |

### CustomerOwned Model

```
┌──────────────────────────────────────────┐
│  Customer's Azure Subscription           │
├──────────────────────────────────────────┤
│  customer-search-instance                │
│  └── customer-knowledge-index            │
└──────────────────────────────────────────┘
              ▲
              │ API Key from Key Vault
┌──────────────────────────────────────────┐
│  Spaarke Key Vault                       │
│  └── secret: customer-api-key-secret     │
└──────────────────────────────────────────┘
```

| Aspect | Details |
|--------|---------|
| **Index Name** | Customer-provided |
| **Isolation** | Complete (customer's Azure subscription) |
| **Cost** | Customer-managed |
| **Use Case** | Data sovereignty, BYOK requirements |
| **Configuration** | `SearchEndpoint`, `IndexName`, `ApiKeySecretName` |

**Required Configuration**:

```csharp
new KnowledgeDeploymentConfig
{
    TenantId = "customer-id",
    Model = RagDeploymentModel.CustomerOwned,
    SearchEndpoint = "https://customer-search.search.windows.net",
    IndexName = "customer-knowledge-index",
    ApiKeySecretName = "customer-api-key-secret",  // Key Vault secret name
    IsActive = true
}
```

### Model Comparison

| Feature | Shared | Dedicated | CustomerOwned |
|---------|--------|-----------|---------------|
| Physical Isolation | No | Yes | Yes |
| Index Location | Spaarke | Spaarke | Customer |
| Cost to Customer | Included | Premium | Customer pays |
| Setup Complexity | None | Low | Medium |
| Data Sovereignty | No | Partial | Full |
| Compliance (SOC2, etc.) | Shared | Dedicated | Customer-managed |

---

## Hybrid Search Pipeline

The RAG system uses a three-stage hybrid search pipeline:

### Stage 1: Query Processing

```
User Query: "What are the payment terms for contracts?"
                    │
                    ▼
┌─────────────────────────────────────────┐
│  1. Check embedding cache               │
│  2. Generate embedding (if cache miss)  │
│  3. Cache embedding for future queries  │
└─────────────────────────────────────────┘
                    │
                    ▼
        Query Embedding (1536 dims)
```

### Stage 2: Hybrid Retrieval

```
┌─────────────────────────────────────────────────────────────┐
│  Azure AI Search - Hybrid Query                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────────────┐  ┌─────────────────┐                  │
│  │ Keyword Search  │  │ Vector Search   │                  │
│  │ (BM25 ranking)  │  │ (Cosine sim.)   │                  │
│  └────────┬────────┘  └────────┬────────┘                  │
│           │                    │                            │
│           └──────────┬─────────┘                            │
│                      │                                      │
│                      ▼                                      │
│           ┌─────────────────────┐                          │
│           │ RRF Score Fusion    │                          │
│           │ (Reciprocal Rank)   │                          │
│           └─────────────────────┘                          │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Stage 3: Semantic Reranking

```
┌─────────────────────────────────────────┐
│  Semantic Ranker                        │
│  (knowledge-semantic-config)            │
├─────────────────────────────────────────┤
│  - Reranks top results                  │
│  - Uses deep language understanding     │
│  - Prioritizes semantic relevance       │
└─────────────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────┐
│  Final Results                          │
│  - Scored by relevance                  │
│  - Filtered by minScore threshold       │
│  - Limited by topK parameter            │
└─────────────────────────────────────────┘
```

### Search Options

| Option | Default | Description |
|--------|---------|-------------|
| `tenantId` | Required | Tenant isolation filter |
| `topK` | 10 | Maximum results to return |
| `minScore` | 0.5 | Minimum relevance threshold |
| `documentTypes` | null | Filter by document type |
| `tags` | null | Filter by tags |

---

## Index Schema

### Field Definitions

| Field | Type | Searchable | Filterable | Purpose |
|-------|------|------------|------------|---------|
| `id` | String | No | Yes | Unique document chunk ID |
| `tenantId` | String | No | Yes | Tenant isolation |
| `deploymentId` | String | No | Yes | Deployment config reference |
| `deploymentModel` | String | No | Yes | Shared/Dedicated/CustomerOwned |
| `documentId` | String | No | Yes | Parent document reference |
| `documentName` | String | Yes | No | Human-readable name |
| `documentType` | String | Yes | Yes | Classification (contract, policy, etc.) |
| `chunkIndex` | Int32 | No | Yes | Position in document |
| `chunkCount` | Int32 | No | No | Total chunks for document |
| `content` | String | Yes | No | Actual text content |
| `contentVector` | Vector (1536) | N/A | N/A | Embedding for semantic search |
| `tags` | Collection | Yes | Yes | Custom tags for filtering |
| `metadata` | String | No | No | JSON metadata blob |
| `createdAt` | DateTimeOffset | No | Yes | Creation timestamp |
| `updatedAt` | DateTimeOffset | No | Yes | Last update timestamp |

### Vector Configuration

```json
{
  "name": "contentVector",
  "type": "Collection(Edm.Single)",
  "dimensions": 1536,
  "vectorSearchProfile": "knowledge-vector-profile"
}
```

### Vector Search Profile

| Setting | Value | Rationale |
|---------|-------|-----------|
| Algorithm | HNSW | Best balance of speed and accuracy |
| Metric | Cosine | Standard for text embeddings |
| m | 4 | Connections per node |
| efConstruction | 400 | Index build quality |
| efSearch | 500 | Query-time exploration |

### Semantic Configuration

```json
{
  "name": "knowledge-semantic-config",
  "prioritizedFields": {
    "titleField": { "fieldName": "documentName" },
    "contentFields": [{ "fieldName": "content" }],
    "keywordsFields": [{ "fieldName": "tags" }]
  }
}
```

---

## Service Architecture

### IKnowledgeDeploymentService

Manages tenant deployment configurations and SearchClient routing.

```csharp
public interface IKnowledgeDeploymentService
{
    // Get or create deployment config for tenant
    Task<KnowledgeDeploymentConfig> GetDeploymentConfigAsync(string tenantId);

    // Get SearchClient routed to correct index
    Task<SearchClient> GetSearchClientAsync(string tenantId);

    // Persist deployment configuration
    Task<KnowledgeDeploymentConfig> SaveDeploymentConfigAsync(KnowledgeDeploymentConfig config);

    // Validate CustomerOwned deployment settings
    Task<DeploymentValidationResult> ValidateCustomerOwnedDeploymentAsync(KnowledgeDeploymentConfig config);
}
```

**Key Behaviors**:
- Caches SearchClient instances per tenant
- Creates default Shared config if none exists
- Validates CustomerOwned configs before activation
- Sanitizes tenant IDs for index naming

### IRagService

Provides hybrid search and document indexing operations.

```csharp
public interface IRagService
{
    // Hybrid search with semantic ranking
    Task<RagSearchResponse> SearchAsync(string query, RagSearchOptions options, CancellationToken ct);

    // Index single document chunk
    Task<KnowledgeDocument> IndexDocumentAsync(KnowledgeDocument document, CancellationToken ct);

    // Batch index multiple chunks
    Task<IReadOnlyList<IndexResult>> IndexDocumentsBatchAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct);

    // Delete document by ID
    Task<bool> DeleteDocumentAsync(string documentId, string tenantId, CancellationToken ct);

    // Delete all chunks for source document
    Task<int> DeleteBySourceDocumentAsync(string sourceDocumentId, string tenantId, CancellationToken ct);

    // Generate embedding (with caching)
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct);
}
```

**Key Behaviors**:
- Generates embeddings via IOpenAiClient
- Checks embedding cache before API calls
- Routes to correct index via IKnowledgeDeploymentService
- Includes telemetry (latency, cache hits)

---

## Embedding Cache

### Cache Strategy

Embeddings are deterministic for the same model and input text. The cache:

1. Computes SHA256 hash of input text
2. Checks Redis for cached embedding
3. If miss, generates via Azure OpenAI
4. Stores in Redis with 7-day TTL

### Cache Key Format

```
sdap:embedding:{base64-sha256-hash}
```

### Serialization

Embeddings are stored as binary for efficiency:

```csharp
// float[] → byte[]
byte[] bytes = new byte[embedding.Length * sizeof(float)];
Buffer.BlockCopy(embedding.ToArray(), 0, bytes, 0, bytes.Length);

// byte[] → float[]
float[] result = new float[bytes.Length / sizeof(float)];
Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
```

### Cache Metrics

Uses existing `CacheMetrics` with `cacheType="embedding"`:

| Metric | Description |
|--------|-------------|
| `cache_hits_total{cacheType="embedding"}` | Cache hit count |
| `cache_misses_total{cacheType="embedding"}` | Cache miss count |
| `cache_hit_rate{cacheType="embedding"}` | Hit rate percentage |

### Error Handling

Cache failures are graceful - embedding generation continues without cache:

```csharp
try
{
    var cached = await _cache.GetEmbeddingAsync(hash, ct);
    if (cached != null) return cached;
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Cache lookup failed, generating fresh embedding");
}
// Continue with embedding generation
```

---

## Security and Isolation

### Tenant Isolation

| Model | Isolation Method | Security Level |
|-------|-----------------|----------------|
| Shared | tenantId filter on all queries | Logical |
| Dedicated | Separate index per tenant | Physical |
| CustomerOwned | Customer's Azure subscription | Complete |

### Query Isolation

All searches include tenant filter:

```csharp
var filter = $"tenantId eq '{tenantId}'";
var searchOptions = new SearchOptions
{
    Filter = filter,
    // ... other options
};
```

### CustomerOwned Security

- API keys stored in Spaarke Key Vault (not in code/config)
- Keys retrieved at runtime via managed identity
- Customer controls their own Azure AI Search instance

### API Authentication

All RAG endpoints require authentication:

```csharp
group.MapPost("/search", Search)
    .RequireAuthorization()  // JWT authentication required
    .RequireRateLimiting("ai-batch");  // Rate limiting
```

---

## Performance Characteristics

### Target Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| Search P95 Latency | < 500ms | End-to-end search time |
| Index P95 Latency | < 2000ms | Single document indexing |
| Embedding Cache Hit Rate | > 80% | For repeated queries |

### Performance Factors

| Factor | Impact | Optimization |
|--------|--------|--------------|
| Embedding Generation | 100-300ms | Redis caching |
| Vector Search | 50-100ms | HNSW algorithm |
| Semantic Ranking | 100-200ms | Limited to top results |
| Network Latency | Variable | Regional deployment |

### Scaling Considerations

| Scenario | Recommendation |
|----------|----------------|
| High query volume | Increase AI Search replicas |
| Large document corpus | Consider Dedicated model |
| Many concurrent users | Scale Redis cluster |
| Enterprise customer | Dedicated or CustomerOwned |

---

## Integration Points

### Document Ingestion Pipeline

```
Document Upload → Document Intelligence (parsing)
                          │
                          ▼
                  Text Extraction
                          │
                          ▼
                  Chunking (if needed)
                          │
                          ▼
                  POST /api/ai/rag/index
                          │
                          ▼
                  Generate Embedding
                          │
                          ▼
                  Store in AI Search
```

### Analysis Pipeline

```
User Query in Analysis Workspace
              │
              ▼
    POST /api/ai/rag/search
              │
              ▼
    Retrieve Relevant Context
              │
              ▼
    Augment LLM Prompt
              │
              ▼
    Generate Analysis Response
```

### Future Integrations (Phase 2+)

| Integration | Purpose | Phase |
|-------------|---------|-------|
| Tool Framework | RAG as tool for AI agents | Phase 2 |
| Playbook System | Pre-configured RAG queries | Phase 3 |
| Export Services | Include RAG sources in exports | Phase 4 |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | Configuration reference |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | Troubleshooting guide |
| [AI-DEPLOYMENT-GUIDE.md](AI-DEPLOYMENT-GUIDE.md) | Full deployment guide |
| [auth-AI-azure-resources.md](../architecture/auth-AI-azure-resources.md) | Azure resource reference |

---

*Document created: 2025-12-29*
*AI Document Intelligence R3 - Phase 1 Complete*
