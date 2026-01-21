# RAG Architecture Guide

> **Version**: 1.4
> **Created**: 2025-12-29
> **Updated**: 2026-01-20
> **Project**: AI Document Intelligence R3 + RAG Pipeline R1 + Semantic Search Foundation R1
> **Status**: R3 Phases 1-5 Complete, RAG Pipeline Phase 1 Complete, Semantic Search R1 Complete

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture Components](#architecture-components)
3. [File Indexing Pipeline](#file-indexing-pipeline)
4. [Deployment Models](#deployment-models)
5. [Hybrid Search Pipeline](#hybrid-search-pipeline)
6. [Semantic Search API](#semantic-search-api) *(R1 - NEW)*
7. [Index Schema](#index-schema)
8. [Service Architecture](#service-architecture)
9. [Job Processing](#job-processing)
10. [Embedding Cache](#embedding-cache)
11. [Security and Isolation](#security-and-isolation)
12. [Performance Characteristics](#performance-characteristics)
13. [Integration Points](#integration-points)

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
| Embedding Model | text-embedding-3-large | 3072 dims, high accuracy RAG retrieval |
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
│  IFileIndexingService (FileIndexingService.cs)                  │
│  ├── IndexFileAsync()         → Index file (OBO/user context)   │
│  ├── IndexFileAppOnlyAsync()  → Index file (app-only/background)│
│  └── IndexContentAsync()      → Index pre-extracted content     │
│                                                                 │
│  IRagService (RagService.cs)                                    │
│  ├── SearchAsync()         → Hybrid search with semantic ranking│
│  ├── IndexDocumentAsync()  → Index single document chunk        │
│  ├── DeleteDocumentAsync() → Delete by document ID              │
│  └── GetEmbeddingAsync()   → Generate/cache embedding           │
│                                                                 │
│  ITextChunkingService (TextChunkingService.cs)                  │
│  └── ChunkTextAsync()      → Split text into indexed chunks     │
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
│  ├── spaarke-knowledge-index-v2 ├── text-embedding-3-large      │
│  ├── {tenant}-knowledge         └── 3072 dimensions             │
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
| `IFileIndexingService` | End-to-end file indexing orchestration | Scoped |
| `IRagService` | Search, indexing, embedding orchestration | Scoped |
| `ITextChunkingService` | Text chunking with configurable strategies | Singleton |
| `ITextExtractor` | Text extraction from documents (PDF, Office, etc.) | Singleton |
| `IKnowledgeDeploymentService` | Tenant config, SearchClient routing | Singleton |
| `IEmbeddingCache` | Redis-based embedding caching | Singleton |
| `IOpenAiClient` | Azure OpenAI API calls (embeddings + chat) | Singleton |
| `RagIndexingJobHandler` | Async job processing with idempotency | Scoped |
| `IIdempotencyService` | Duplicate detection and processing locks | Singleton |

---

## File Indexing Pipeline

The RAG indexing pipeline provides end-to-end file indexing with three entry points:

### Pipeline Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    File Indexing Service                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Entry Points:                                                   │
│  ├── IndexFileAsync()         → OBO (user context, real-time)   │
│  ├── IndexFileAppOnlyAsync()  → App-only (background jobs)      │
│  └── IndexContentAsync()      → Pre-extracted content           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Step 1: File Download (for file-based entry points)            │
├─────────────────────────────────────────────────────────────────┤
│  ISpeFileOperations                                              │
│  ├── DownloadFileAsync()        → App-only download             │
│  └── DownloadFileAsUserAsync()  → OBO download (user context)   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Step 2: Text Extraction                                         │
├─────────────────────────────────────────────────────────────────┤
│  ITextExtractor                                                  │
│  ├── ExtractAsync()  → Route to appropriate extractor           │
│  │                                                               │
│  │  ┌──────────────────────────────────────────────────────┐    │
│  │  │ Extractors by File Type:                              │    │
│  │  │ ├── PDF, DOCX, DOC → Document Intelligence           │    │
│  │  │ ├── TXT, MD, JSON  → Native text read                 │    │
│  │  │ ├── PNG, JPG       → Vision OCR                       │    │
│  │  │ └── EML, MSG       → Email parser                     │    │
│  │  └──────────────────────────────────────────────────────┘    │
│  │                                                               │
│  └── Returns: TextExtractionResult (text, method, email metadata)│
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Step 3: Text Chunking                                           │
├─────────────────────────────────────────────────────────────────┤
│  ITextChunkingService                                            │
│  ├── ChunkTextAsync()  → Split text into indexable chunks       │
│  │                                                               │
│  │  Chunking Strategy:                                          │
│  │  ├── Target chunk size: ~1000 tokens                         │
│  │  ├── Overlap: 100 tokens (context preservation)              │
│  │  ├── Boundary-aware: Sentence/paragraph splitting            │
│  │  └── Returns: List<TextChunk> with position metadata         │
│  │                                                               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Step 4: Batch Indexing                                          │
├─────────────────────────────────────────────────────────────────┤
│  IRagService.IndexDocumentsBatchAsync()                          │
│  ├── Generate embeddings for each chunk                         │
│  ├── Create KnowledgeDocument records                           │
│  └── Upload to Azure AI Search                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Result: FileIndexingResult                                      │
├─────────────────────────────────────────────────────────────────┤
│  ├── Success: bool                                               │
│  ├── ChunksIndexed: int                                          │
│  ├── SpeFileId: string                                           │
│  └── ErrorMessage: string?                                       │
└─────────────────────────────────────────────────────────────────┘
```

### Entry Point Comparison

| Entry Point | Auth Context | Use Case | Download Required |
|-------------|--------------|----------|-------------------|
| `IndexFileAsync` | OBO (user token) | Real-time indexing from UI | Yes |
| `IndexFileAppOnlyAsync` | App-only | Background job processing | Yes |
| `IndexContentAsync` | N/A | Pre-extracted text (emails, etc.) | No |

### FileIndexingResult

| Property | Type | Description |
|----------|------|-------------|
| `Success` | bool | Whether indexing completed successfully |
| `ChunksIndexed` | int | Number of chunks successfully indexed |
| `SpeFileId` | string | SharePoint Embedded file ID |
| `ErrorMessage` | string? | Error description if failed |

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
| **Index Name** | `spaarke-knowledge-index-v2` |
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
        Query Embedding (3072 dims)
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

## Semantic Search API

> **Added in**: Semantic Search Foundation R1 (2026-01-20)

The Semantic Search API provides a **general-purpose search capability** for searching documents across entity scopes (Matter, Project, Invoice, Account, Contact). It builds on the RAG hybrid search pipeline but exposes a simplified, entity-aware API.

### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Semantic Search Endpoints                     │
├─────────────────────────────────────────────────────────────────┤
│  SemanticSearchEndpoints.cs                                      │
│  ├── POST /api/ai/search        → Hybrid semantic search         │
│  └── POST /api/ai/search/count  → Document count (pagination)    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    SemanticSearchService                         │
├─────────────────────────────────────────────────────────────────┤
│  ISemanticSearchService (SemanticSearchService.cs)               │
│  ├── SearchAsync()      → Execute hybrid search                  │
│  ├── CountAsync()       → Count matching documents               │
│  ├── BuildFilters()     → Construct OData filters                │
│  └── BuildSearchQuery() → Build Azure AI Search query            │
│                                                                  │
│  Extensibility Hooks (R1: no-op implementations):               │
│  ├── IQueryPreprocessor  → Future query expansion                │
│  └── IResultPostprocessor → Future result enrichment            │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Shared Infrastructure                         │
├─────────────────────────────────────────────────────────────────┤
│  IEmbeddingService      → Generate query embeddings              │
│  IKnowledgeDeploymentService → Route to correct index           │
│  IAiSearchClientFactory → Get SearchClient for tenant           │
└─────────────────────────────────────────────────────────────────┘
```

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/ai/search` | POST | Execute hybrid semantic search |
| `/api/ai/search/count` | POST | Get count of matching documents |

### Scoping Models

Semantic Search supports two scoping models for R1:

| Scope | Description | Required Fields |
|-------|-------------|-----------------|
| `entity` | Search within a parent entity (Matter, Project, etc.) | `entityType`, `entityId` |
| `documentIds` | Search specific documents by ID list | `documentIds[]` (max 100) |

**Note**: `scope=all` is NOT supported in R1 and returns HTTP 400.

### Hybrid Search Modes

The API supports three search modes via `options.hybridMode`:

| Mode | Description | When to Use |
|------|-------------|-------------|
| `rrf` (default) | Reciprocal Rank Fusion (vector + keyword) | Best overall relevance |
| `vector` | Vector search only | When semantic similarity is priority |
| `keyword` | Keyword search only | When exact term matching is needed |

### Request Schema

```json
{
  "query": "search terms",
  "scope": "entity",
  "entityType": "matter",
  "entityId": "guid-of-matter",
  "options": {
    "hybridMode": "rrf",
    "top": 10,
    "skip": 0,
    "minRelevanceScore": 0.5,
    "documentTypes": ["contract", "invoice"],
    "fileTypes": [".pdf", ".docx"],
    "tags": ["legal"],
    "dateRange": {
      "from": "2024-01-01",
      "to": "2024-12-31"
    },
    "includeContent": true
  }
}
```

### Response Schema

```json
{
  "results": [
    {
      "documentId": "chunk-id",
      "speFileId": "spe-file-id",
      "fileName": "Contract.pdf",
      "documentType": "contract",
      "content": "Chunk content...",
      "combinedScore": 0.85,
      "parentEntityType": "matter",
      "parentEntityId": "matter-guid",
      "parentEntityName": "Smith vs. Jones",
      "highlights": ["<em>payment</em> terms..."]
    }
  ],
  "metadata": {
    "totalResults": 42,
    "returnedResults": 10,
    "searchDurationMs": 245,
    "searchMode": "rrf",
    "embeddingGenerated": true
  }
}
```

### Authorization

Semantic Search uses `SemanticSearchAuthorizationFilter` for security trimming:

1. **Entity Scope**: Validates user has access to the parent entity via Dataverse permissions
2. **DocumentIds Scope**: Validates user has access to each specified document

### AI Tool Integration

The `SemanticSearchToolHandler` integrates semantic search with the AI Tool Framework for Copilot integration:

```csharp
// Tool definition
{
  "name": "search_documents",
  "description": "Search documents using natural language",
  "parameters": {
    "query": "string - search query",
    "scope": "string - entity or documentIds",
    "entityType": "string - matter, project, etc.",
    "entityId": "string - entity GUID"
  }
}
```

### Graceful Degradation

If embedding generation fails:
1. Log warning with error details
2. Fall back to keyword-only search
3. Return results with `metadata.embeddingGenerated = false`

### Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Search P50 | < 500ms | End-to-end including embedding |
| Search P95 | < 1000ms | With cold embedding cache |
| Count P50 | < 200ms | No embedding required |

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
| `contentVector3072` | Vector (3072) | N/A | N/A | Embedding for semantic search |
| `tags` | Collection | Yes | Yes | Custom tags for filtering |
| `metadata` | String | No | No | JSON metadata blob |
| `createdAt` | DateTimeOffset | No | Yes | Creation timestamp |
| `updatedAt` | DateTimeOffset | No | Yes | Last update timestamp |
| `parentEntityType` | String | No | Yes | Parent entity type (matter, project, etc.) *(R1)* |
| `parentEntityId` | String | No | Yes | Parent entity GUID *(R1)* |
| `parentEntityName` | String | Yes | No | Parent entity display name *(R1)* |

### Vector Configuration

```json
{
  "name": "contentVector3072",
  "type": "Collection(Edm.Single)",
  "dimensions": 3072,
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

## Job Processing

The RAG pipeline supports async job processing via a single `sdap-jobs` Azure Service Bus queue. All background indexing flows through `ServiceBusJobProcessor` which routes to job handlers by type.

### Job Processing Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Entry Points                                                    │
├─────────────────────────────────────────────────────────────────┤
│  PCF FileUpload  ──► POST /api/ai/rag/index-file (direct)       │
│  Email Processing ──► sdap-jobs queue (async)                   │
│  API Endpoint    ──► POST /api/ai/rag/enqueue-indexing (async)  │
│  Bulk Admin      ──► POST /api/ai/rag/admin/bulk-index (async)  │
└─────────────────────────────────────────────────────────────────┘
                              │ (async paths)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  ServiceBusJobProcessor (sdap-jobs queue)                        │
├─────────────────────────────────────────────────────────────────┤
│  - Deserializes JobContract from Service Bus message            │
│  - Routes to handler by JobType                                  │
│  - Handles retries, dead-letter queue                           │
│  - JobType: "RagIndexing" → RagIndexingJobHandler               │
│  - JobType: "BulkRagIndexing" → BulkRagIndexingJobHandler       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  RagIndexingJobHandler                                           │
├─────────────────────────────────────────────────────────────────┤
│  Implements: IJobHandler<RagIndexingJobPayload>                  │
│  Job Type: "RagIndexing"                                         │
│                                                                  │
│  Processing Flow:                                                │
│  ├── 1. Check idempotency (already processed?)                  │
│  ├── 2. Acquire processing lock                                  │
│  ├── 3. Execute FileIndexingService.IndexFileAppOnlyAsync()     │
│  ├── 4. Mark as processed (on success)                          │
│  └── 5. Release lock (always)                                   │
└─────────────────────────────────────────────────────────────────┘
```

### Idempotency

All job processing is idempotent via `IIdempotencyService`:

| Operation | Purpose | TTL |
|-----------|---------|-----|
| `IsEventProcessedAsync` | Check if job already completed | N/A |
| `TryAcquireProcessingLockAsync` | Prevent concurrent processing | Lock duration |
| `MarkEventAsProcessedAsync` | Record successful completion | 7 days |
| `ReleaseProcessingLockAsync` | Allow retries on failure | Immediate |

### Job Contract

```csharp
public class RagIndexingJobPayload
{
    public string TenantId { get; set; }
    public string DriveId { get; set; }
    public string ItemId { get; set; }
    public string FileName { get; set; }
    public string? DocumentId { get; set; }
}
```

### Job Status Flow

```
JobStatus.Pending
    │
    ▼ (handler picks up)
JobStatus.Processing
    │
    ├── Success → JobStatus.Completed
    │              └── Marked as processed in idempotency store
    │
    ├── Transient Failure → JobStatus.Failed
    │                        └── Retry with exponential backoff
    │
    └── Permanent Failure → JobStatus.Poisoned
                             └── Moved to poison queue
```

### Error Classification

| Error Type | Example | Status | Action |
|------------|---------|--------|--------|
| Transient | HTTP timeout, service unavailable | Failed | Retry |
| Permanent | File not found, invalid payload | Poisoned | No retry |
| Lock conflict | Another instance processing | Completed | Skip (idempotent) |

### Telemetry

The `RagTelemetry` class tracks:

| Metric | Description |
|--------|-------------|
| `rag_indexing_duration_seconds` | Time to index a file |
| `rag_indexing_chunks_total` | Number of chunks indexed |
| `rag_indexing_errors_total` | Indexing failures by type |

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

### Completed Integrations (R3)

| Integration | Purpose | Phase | Status |
|-------------|---------|-------|--------|
| Analysis Orchestration | RAG context in analysis prompts | Phase 2 | ✅ Complete |
| Playbook System | Pre-configured RAG queries | Phase 2 | ✅ Complete |
| Export Services | Include sources in DOCX/PDF/Email/Teams exports | Phase 3 | ✅ Complete |
| Circuit Breaker | Polly resilience for AI Search | Phase 4 | ✅ Complete |
| Tenant Authorization | `TenantAuthorizationFilter` for isolation | Phase 5 | ✅ Complete |

### Resilience (R3 Phase 4)

The RAG system includes circuit breaker protection via `ResilientSearchClient`:

| Parameter | Value | Behavior |
|-----------|-------|----------|
| Failure ratio threshold | 50% | Circuit opens after 50% failures |
| Minimum throughput | 5 calls | Needs 5 calls before evaluating |
| Break duration | 30 seconds | Wait before retrying |
| Timeout | 30 seconds | Per-request timeout |

When the circuit is open, returns `503 Service Unavailable` with error code `ai_circuit_open`.

**Monitoring:**
- `AiTelemetry.cs` tracks circuit state changes
- Application Insights custom metrics for AI operations
- Azure Monitor alerts on circuit breaker state

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
*Updated: 2026-01-20 - Semantic Search API section added*
*AI Document Intelligence R3 - Phases 1-5 Complete*
*RAG Pipeline R1 - Phase 1 Complete*
*Semantic Search Foundation R1 - Complete (hybrid search API, entity scoping, AI Tool integration)*
