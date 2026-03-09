# Azure AI Search — Two-Index Schema Reference

> **Project**: ai-spaarke-platform-enhancements-r1
> **Created**: 2026-02-23 (AIPL-016)
> **Status**: Defined — pending provisioning (run `Provision-AiSearchIndexes.ps1`)
> **Service**: `spaarke-search-dev` (West US 2, Standard tier)
> **Endpoint**: `https://spaarke-search-dev.search.windows.net/`

---

## Overview

The AI Platform Foundation uses two separate Azure AI Search indexes optimized for different
retrieval patterns:

| Index | Chunk Size | Purpose | Populated By |
|-------|-----------|---------|--------------|
| `knowledge-index` | 512 tokens | Curated precision retrieval (RAG queries, citations) | Admin via KnowledgeBaseEndpoints |
| `discovery-index` | 1024 tokens | Broad discovery, entity extraction, similarity search | Auto via RagIndexingPipeline after analysis |

**Embedding model**: `text-embedding-3-small` — 1536 dimensions (configured in `AzureOpenAI:EmbeddingModelName`)

**Note on dimensions**: The existing `spaarke-knowledge-index-v2` uses `text-embedding-3-large` (3072 dims).
These two new indexes intentionally use `text-embedding-3-small` (1536 dims) as specified in the AI Platform
spec (FR-A06). They are separate indexes with different names and serve different workstreams (new AI platform
vs existing RAG pipeline).

---

## ADR-014 Compliance

Both indexes enforce tenant isolation per **ADR-014**:
- `tenantId`: `filterable: true`, `facetable: true`
- All queries MUST include a `$filter=tenantId eq '{id}'` predicate
- Cache keys MUST be scoped by `tenantId` when caching search results

---

## knowledge-index

**Index file**: `infrastructure/ai-search/knowledge-index.json`

### Purpose

Stores 512-token chunks from curated knowledge sources (KNW-001–010 from Workstream B).
Used by:
- `RagQueryBuilder` (AIPL-010) for precision RAG retrieval
- `KnowledgeBaseEndpoints` (AIPL-015) for admin search-quality testing
- Chat tools: `KnowledgeRetrievalTools` (AIPL-053)

### Field Schema

| Field | Type | Key | Searchable | Filterable | Facetable | Sortable | Notes |
|-------|------|-----|------------|------------|-----------|----------|-------|
| `id` | `Edm.String` | ✅ | — | — | — | — | Deterministic chunk ID (documentId + chunkIndex) |
| `documentId` | `Edm.String` | — | — | ✅ | — | — | Dataverse `sprk_document` record ID |
| `tenantId` | `Edm.String` | — | — | ✅ | ✅ | — | **ADR-014**: must be filterable + facetable |
| `content` | `Edm.String` | — | ✅ | — | — | — | Chunk text (analyzer: standard.lucene) |
| `contentVector` | `Collection(Edm.Single)` | — | ✅ (vector) | — | — | — | 1536-dim, profile: `knowledge-vector-profile` |
| `sectionTitle` | `Edm.String` | — | ✅ | ✅ | — | — | Section heading detected by Doc Intelligence |
| `documentType` | `Edm.String` | — | — | ✅ | ✅ | — | e.g., `contract`, `invoice`, `nda` |
| `pageNumber` | `Edm.Int32` | — | — | ✅ | — | ✅ | 1-based page number from source document |
| `chunkIndex` | `Edm.Int32` | — | — | ✅ | — | ✅ | Sequential chunk index within document |
| `indexedAt` | `Edm.DateTimeOffset` | — | — | ✅ | — | ✅ | UTC timestamp when chunk was indexed |

### Vector Search Configuration

```json
{
  "vectorSearch": {
    "algorithms": [
      {
        "name": "hnsw-knowledge",
        "kind": "hnsw",
        "hnswParameters": {
          "m": 4,
          "efConstruction": 400,
          "efSearch": 500,
          "metric": "cosine"
        }
      }
    ],
    "profiles": [
      {
        "name": "knowledge-vector-profile",
        "algorithm": "hnsw-knowledge"
      }
    ]
  }
}
```

### Semantic Configuration

Name: `knowledge-semantic-config` (also the default configuration)

| Role | Field |
|------|-------|
| Title | `sectionTitle` |
| Content (primary) | `content` |
| Keywords | `documentType` |

### Chunk ID Generation Pattern

```csharp
// Deterministic ID — same document + chunk always gets same ID (idempotent indexing)
var id = Convert.ToBase64String(
    SHA256.HashData(Encoding.UTF8.GetBytes($"{documentId}:{chunkIndex}")));
```

---

## discovery-index

**Index file**: `infrastructure/ai-search/discovery-index.json`

### Purpose

Stores 1024-token chunks from all analyzed documents. Auto-populated by `RagIndexingPipeline`
after each document analysis completes (via Service Bus job). The larger chunks capture more
contextual structure. The additional `entityMentions` field enables entity-based discovery.

Used by:
- `RagIndexingPipeline` (AIPL-014) for auto-indexing after analysis
- Similarity search (Phase 2 — Find Similar Documents)
- Entity-based navigation (Phase 2)

### Field Schema

All fields from `knowledge-index` plus one additional field:

| Field | Type | Key | Searchable | Filterable | Facetable | Sortable | Notes |
|-------|------|-----|------------|------------|-----------|----------|-------|
| `id` | `Edm.String` | ✅ | — | — | — | — | Same deterministic generation as knowledge-index |
| `documentId` | `Edm.String` | — | — | ✅ | — | — | Dataverse `sprk_document` record ID |
| `tenantId` | `Edm.String` | — | — | ✅ | ✅ | — | **ADR-014**: must be filterable + facetable |
| `content` | `Edm.String` | — | ✅ | — | — | — | Chunk text (analyzer: standard.lucene) |
| `contentVector` | `Collection(Edm.Single)` | — | ✅ (vector) | — | — | — | 1536-dim, profile: `discovery-vector-profile` |
| `sectionTitle` | `Edm.String` | — | ✅ | ✅ | — | — | Section heading |
| `documentType` | `Edm.String` | — | — | ✅ | ✅ | — | Document classification |
| `pageNumber` | `Edm.Int32` | — | — | ✅ | — | ✅ | Source page number |
| `chunkIndex` | `Edm.Int32` | — | — | ✅ | — | ✅ | Sequential chunk index |
| `indexedAt` | `Edm.DateTimeOffset` | — | — | ✅ | — | ✅ | Index timestamp |
| `entityMentions` | `Collection(Edm.String)` | — | ✅ | — | — | — | **discovery-specific**: entities extracted during analysis (people, orgs, dates, refs) |

### Vector Search Configuration

```json
{
  "vectorSearch": {
    "algorithms": [
      {
        "name": "hnsw-discovery",
        "kind": "hnsw",
        "hnswParameters": {
          "m": 4,
          "efConstruction": 400,
          "efSearch": 500,
          "metric": "cosine"
        }
      }
    ],
    "profiles": [
      {
        "name": "discovery-vector-profile",
        "algorithm": "hnsw-discovery"
      }
    ]
  }
}
```

### Semantic Configuration

Name: `discovery-semantic-config` (also the default configuration)

| Role | Field |
|------|-------|
| Title | `sectionTitle` |
| Content (primary) | `content` |
| Keywords (primary) | `entityMentions` |
| Keywords (secondary) | `documentType` |

---

## Provisioning

### Script

```powershell
# From repo root — requires az login with Contributor on spe-infrastructure-westus2
pwsh "projects/ai-spaarke-platform-enhancements-r1/scripts/Provision-AiSearchIndexes.ps1"

# Preview without changes
pwsh "projects/ai-spaarke-platform-enhancements-r1/scripts/Provision-AiSearchIndexes.ps1" -WhatIf
```

### Manual (az REST)

```bash
# Get admin key
az search admin-key show \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --query "primaryKey" -o tsv

# Create knowledge-index
az rest --method PUT \
  --uri "https://spaarke-search-dev.search.windows.net/indexes/knowledge-index?api-version=2024-07-01" \
  --headers "Content-Type=application/json" "api-key=<admin-key>" \
  --body @infrastructure/ai-search/knowledge-index.json

# Create discovery-index
az rest --method PUT \
  --uri "https://spaarke-search-dev.search.windows.net/indexes/discovery-index?api-version=2024-07-01" \
  --headers "Content-Type=application/json" "api-key=<admin-key>" \
  --body @infrastructure/ai-search/discovery-index.json
```

### Verification

```bash
# Verify knowledge-index
az search index show \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --name knowledge-index

# Verify discovery-index
az search index show \
  --service-name spaarke-search-dev \
  --resource-group spe-infrastructure-westus2 \
  --name discovery-index
```

---

## appsettings.template.json Configuration

The index names are wired in `AiSearch` config section (added by AIPL-003):

```json
{
  "AiSearch": {
    "Endpoint": "[your-search-endpoint]",
    "ApiKeySecretName": "AzureAISearchApiKey",
    "KnowledgeIndexName": "knowledge-index",
    "DiscoveryIndexName": "discovery-index",
    "SemanticConfigName": "semantic-config"
  },
  "AzureOpenAI": {
    "EmbeddingModelName": "text-embedding-3-small"
  }
}
```

These are consumed by `IOptions<AiSearchOptions>` (provisioned in AIPL-004).

---

## Query Patterns

### Knowledge Query (RAG precision)

```csharp
// Used by RagQueryBuilder (AIPL-010)
var searchOptions = new SearchOptions
{
    Filter = $"tenantId eq '{tenantId}'",
    Select = { "id", "documentId", "content", "sectionTitle", "pageNumber", "chunkIndex" },
    SemanticSearch = new SemanticSearchOptions
    {
        SemanticConfigurationName = "knowledge-semantic-config",
        QueryAnswer = new QueryAnswerOptions(QueryAnswerType.Extractive)
    },
    VectorSearch = new VectorSearchOptions
    {
        Queries = { new VectorizableTextQuery(queryText)
        {
            KNearestNeighborsCount = 10,
            Fields = { "contentVector" }
        }}
    },
    Size = 10
};
```

### Discovery Query (entity + broad search)

```csharp
// Used by similarity/discovery features (Phase 2)
var searchOptions = new SearchOptions
{
    Filter = $"tenantId eq '{tenantId}'",
    SearchFields = { "content", "sectionTitle", "entityMentions" },
    SemanticSearch = new SemanticSearchOptions
    {
        SemanticConfigurationName = "discovery-semantic-config"
    }
};
```

---

## Related Tasks

| Task | Dependency |
|------|-----------|
| AIPL-003 | Config wired (appsettings.template.json) — complete |
| AIPL-010 | RagQueryBuilder queries knowledge-index |
| AIPL-013 | RagIndexingPipeline indexes to both indexes |
| AIPL-014 | RagIndexingPipeline + RagIndexingJobHandler needs both indexes live |
| AIPL-015 | KnowledgeBaseEndpoints admin CRUD for knowledge-index |
| AIPL-018 | Phase 2 API deploy blocked on AIPL-016 |

---

*Created: 2026-02-23 (AIPL-016 — Provision Two-Index Azure AI Search Schema)*
