# RAG Architecture Guide

> **Version**: 1.5
> **Created**: 2025-12-29
> **Updated**: 2026-01-23
> **Project**: AI Document Intelligence R3 + RAG Pipeline R1 + Semantic Search UI R2
> **Status**: R3 Phases 1-5 Complete, RAG Pipeline Phase 1 Complete, Semantic Search R1 Complete, Semantic Search UI R2 In Progress

---

## Table of Contents

1. [Overview](#overview)
2. [Architecture Components](#architecture-components)
3. [File Indexing Pipeline](#file-indexing-pipeline)
4. [Deployment Models](#deployment-models)
5. [Hybrid Search Pipeline](#hybrid-search-pipeline)
6. [Semantic Search API](#semantic-search-api) *(R1)*
7. [Semantic Search UI](#semantic-search-ui) *(R2 - NEW)*
8. [Index Schema](#index-schema)
9. [Service Architecture](#service-architecture)
10. [Job Processing](#job-processing)
11. [Embedding Cache](#embedding-cache)
12. [Security and Isolation](#security-and-isolation)
13. [Performance Characteristics](#performance-characteristics)
14. [Integration Points](#integration-points)

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
│  ├── POST /api/ai/rag/search        → Hybrid search             │
│  ├── POST /api/ai/rag/index         → Index document            │
│  ├── POST /api/ai/rag/index/batch   → Batch index               │
│  ├── DELETE /api/ai/rag/{id}        → Delete document           │
│  ├── DELETE /api/ai/rag/source/{id} → Delete by source          │
│  ├── POST /api/ai/rag/embedding     → Generate embedding        │
│  ├── POST /api/ai/rag/index-file    → Index file (OBO)          │
│  ├── POST /api/ai/rag/send-to-index → Ribbon button indexing    │
│  ├── POST /api/ai/rag/enqueue-indexing → Queue for async index  │
│  │                                                               │
│  Admin Endpoints (SystemAdmin required):                         │
│  ├── POST /api/ai/rag/admin/bulk-index → Submit bulk job        │
│  └── GET /api/ai/rag/admin/bulk-index/{jobId}/status → Job status│
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
| `RagIndexingJobHandler` | Async single-file job processing with idempotency | Scoped |
| `BulkRagIndexingJobHandler` | Async bulk document indexing with progress tracking | Scoped |
| `IIdempotencyService` | Duplicate detection and processing locks | Singleton |
| `IDataverseService` | Document and entity lookup from Dataverse | Scoped |

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

## Semantic Search UI

> **Added in**: Semantic Search UI R2 (2026-01-23)

The Semantic Search UI provides **user-facing components** for searching and indexing documents within Dataverse model-driven apps. It consists of a PCF control for search display and Dataverse ribbon buttons for indexing operations.

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Dataverse UI Layer                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  SemanticSearchControl (PCF v1.0.15)                           │
│  ├── React + Fluent UI v9                                       │
│  ├── Hybrid search UI with filters                              │
│  ├── Result cards with similarity scoring                       │
│  └── MSAL authentication for BFF API                            │
│                                                                 │
│  "Send to Index" Ribbon Buttons                                 │
│  ├── Grid (multi-select)                                        │
│  ├── Form (single document)                                     │
│  └── Subgrid (multi-select)                                     │
│                                                                 │
│  sprk_DocumentOperations.js (v1.26.0)                          │
│  └── sendToIndex() → POST /api/ai/rag/send-to-index            │
│  └── 10-record limit per selection                             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BFF API Layer                            │
├─────────────────────────────────────────────────────────────────┤
│  POST /api/ai/rag/send-to-index                                 │
│  ├── 1. Fetch document details from Dataverse                   │
│  ├── 2. Extract parent entity context (Matter/Project/Invoice)  │
│  ├── 3. Index file via OBO authentication                       │
│  └── 4. Update Dataverse tracking fields                        │
└─────────────────────────────────────────────────────────────────┘
```

### PCF Control: SemanticSearchControl

The `SemanticSearchControl` is a PowerApps Component Framework (PCF) control that provides semantic document search within Dataverse forms and dashboards.

**Location**: `src/client/pcf/SemanticSearchControl/`

**Current Version**: 1.0.15

#### Components

| Component | File | Description |
|-----------|------|-------------|
| `SemanticSearchControl` | `SemanticSearchControl.tsx` | Main PCF entry point |
| `SearchInput` | `components/SearchInput.tsx` | Query input with debouncing |
| `FilterPanel` | `components/FilterPanel.tsx` | File type, date range, entity filters |
| `FilterDropdown` | `components/FilterDropdown.tsx` | Individual filter dropdown |
| `DateRangeFilter` | `components/DateRangeFilter.tsx` | Date range picker |
| `ResultsList` | `components/ResultsList.tsx` | Scrollable results container |
| `ResultCard` | `components/ResultCard.tsx` | Individual search result display |
| `SimilarityBadge` | `components/SimilarityBadge.tsx` | Relevance score indicator |
| `HighlightedSnippet` | `components/HighlightedSnippet.tsx` | Content with query highlights |
| `EmptyState` | `components/EmptyState.tsx` | No results message |
| `ErrorState` | `components/ErrorState.tsx` | Error display |
| `LoadingState` | `components/LoadingState.tsx` | Loading spinner |

#### Hooks

| Hook | File | Description |
|------|------|-------------|
| `useSemanticSearch` | `hooks/useSemanticSearch.ts` | Search state management, API calls |
| `useFilters` | `hooks/useFilters.ts` | Filter state management |
| `useFilterOptions` | `hooks/useFilterOptions.ts` | Dynamic filter option loading |
| `useInfiniteScroll` | `hooks/useInfiniteScroll.ts` | Pagination with infinite scroll |

#### Services

| Service | File | Description |
|---------|------|-------------|
| `SemanticSearchApiService` | `services/SemanticSearchApiService.ts` | BFF API client |
| `DataverseMetadataService` | `services/DataverseMetadataService.ts` | Entity metadata retrieval |
| `NavigationService` | `services/NavigationService.ts` | Document/entity navigation |
| `ThemeService` | `services/ThemeService.ts` | Dark mode support (ADR-021) |
| `MsalAuthProvider` | `services/auth/MsalAuthProvider.ts` | MSAL token acquisition |

#### Control Manifest Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `entityType` | string | Yes | Parent entity logical name (matter, project, etc.) |
| `entityId` | string | Yes | Parent entity GUID (bound to record) |
| `bffApiUrl` | string | Yes | BFF API base URL |
| `tenantId` | string | Yes | Dataverse tenant ID |

### Send to Index Ribbon Buttons

Ribbon buttons enable users to manually index documents for semantic search directly from the Dataverse UI.

**Solution**: `DocumentRibbons` (v1.4.0.0)
**Location**: `infrastructure/dataverse/ribbon/DocumentRibbons/`

#### Button Configurations

| Button | Location | Selection | Description |
|--------|----------|-----------|-------------|
| Grid Button | Document grid command bar | Multi-select (max 10) | Index selected documents |
| Form Button | Document form command bar | Single | Index current document |
| Subgrid Button | Related documents subgrid | Multi-select (max 10) | Index from subgrid |

> **⚠️ 10-Record Limit**: Manual "Send to Index" is limited to 10 documents per operation to ensure responsive UI and prevent timeout issues. For bulk indexing needs, use the Admin Bulk Index endpoints.

#### JavaScript Web Resource

**File**: `sprk_DocumentOperations.js` (v1.26.0)
**Location**: `infrastructure/dataverse/webresources/sprk_DocumentOperations.js`

**Key Functions**:

```javascript
// Entry point for ribbon button
Sprk.Document.sendToIndex(primaryControl, selectedRecordIds?)

// Calls BFF API
POST /api/ai/rag/send-to-index
{
  "documentIds": ["guid1", "guid2", ...],
  "tenantId": "{organizationId}"
}
```

#### Dataverse Fields Updated

When a document is successfully indexed, these fields are updated:

| Field | Type | Value |
|-------|------|-------|
| `sprk_searchindexed` | Boolean | `true` |
| `sprk_searchindexname` | String | Index name (e.g., `spaarke-knowledge-index-v2`) |
| `sprk_searchindexedon` | DateTime | UTC timestamp of indexing |

### Send to Index Endpoint

The `/api/ai/rag/send-to-index` endpoint orchestrates document indexing from Dataverse.

**Request**:
```json
{
  "documentIds": ["guid1", "guid2"],
  "tenantId": "organization-guid"
}
```

**Response**:
```json
{
  "totalRequested": 2,
  "successCount": 2,
  "failedCount": 0,
  "results": [
    {
      "documentId": "guid1",
      "success": true,
      "chunksIndexed": 5,
      "indexName": "spaarke-knowledge-index-v2",
      "parentEntityType": "matter",
      "parentEntityId": "matter-guid"
    },
    {
      "documentId": "guid2",
      "success": true,
      "chunksIndexed": 3,
      "indexName": "spaarke-knowledge-index-v2",
      "parentEntityType": "project",
      "parentEntityId": "project-guid"
    }
  ]
}
```

**Processing Flow**:

1. **Fetch Document**: Retrieve document record from Dataverse including lookups
2. **Validate File**: Ensure document has `GraphDriveId` and `GraphItemId`
3. **Extract Parent Entity**: Determine Matter, Project, or Invoice association
4. **Index File**: Call `IFileIndexingService.IndexFileAsync()` with OBO auth
5. **Update Dataverse**: Set `sprk_searchindexed`, `sprk_searchindexname`, `sprk_searchindexedon`
6. **Return Results**: Per-document success/failure with chunk counts

### Authorization

| Component | Authorization Method |
|-----------|---------------------|
| SemanticSearchControl | MSAL OBO token → BFF API |
| Ribbon Button | Dataverse session → BFF API (OBO) |
| Send to Index Endpoint | JWT + TenantAuthorizationFilter |

### Automatic Re-indexing on Check-in

> **Added in**: Semantic Search UI R2 (v1.26.0)

Documents are **automatically re-indexed** when users check them in after editing. This ensures the search index stays current without requiring manual "Send to Index" actions.

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Document Check-in Flow                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  User edits document in Office Online                            │
│        │                                                         │
│        ▼                                                         │
│  POST /api/documents/{id}/checkin                                │
│        │                                                         │
│        ├─► 1. Release SharePoint lock (Graph API)               │
│        │                                                         │
│        ├─► 2. Create new version                                 │
│        │                                                         │
│        └─► 3. Enqueue re-index job (fire-and-forget)            │
│                    │                                             │
│                    ▼                                             │
│            JobSubmissionService                                  │
│                    │                                             │
│                    ▼                                             │
│            sdap-jobs queue (RagIndexing job)                     │
│                    │                                             │
│                    ▼                                             │
│            RagIndexingJobHandler                                 │
│            └── IndexFileAppOnlyAsync()                           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### Configuration

Automatic re-indexing is controlled via `ReindexingOptions` in `appsettings.json`:

```json
{
  "Reindexing": {
    "Enabled": true,
    "TenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    "TriggerOnCheckin": true
  }
}
```

| Setting | Default | Description |
|---------|---------|-------------|
| `Enabled` | `true` | Master switch for all automatic re-indexing |
| `TenantId` | Required | **Azure AD tenant ID** for index routing. Must match PCF control and web resource tenant IDs |
| `TriggerOnCheckin` | `true` | Whether check-in triggers re-indexing |

> **⚠️ Important**: The `TenantId` must be the **Azure AD tenant ID** (e.g., `a221a95e-6abc-4434-aecc-e48338a1b2f2`), NOT the Dataverse organization ID. This ensures consistent index access across all components.

#### Job Payload

The check-in endpoint enqueues a `RagIndexingJobPayload`:

```csharp
new RagIndexingJobPayload
{
    TenantId = options.TenantId,        // Azure AD tenant ID
    DriveId = fileInfo.DriveId,          // SharePoint Embedded drive
    ItemId = fileInfo.ItemId,            // SharePoint Embedded item
    FileName = fileInfo.FileName,        // For logging
    DocumentId = documentId.ToString(),  // Dataverse document GUID
    Source = "CheckinTrigger",           // Job source tracking
    EnqueuedAt = DateTimeOffset.UtcNow   // Timestamp
}
```

#### Behavior

| Scenario | Behavior |
|----------|----------|
| Re-indexing enabled | Job enqueued after successful check-in, response includes `aiAnalysisTriggered: true` |
| Re-indexing disabled | No job enqueued, response includes `aiAnalysisTriggered: false` |
| TenantId not configured | Warning logged, no job enqueued |
| Job enqueueing fails | Warning logged, check-in still succeeds (fire-and-forget) |

#### Check-in Response

The check-in response now includes re-indexing status:

```json
{
  "success": true,
  "documentId": "guid",
  "newVersionNumber": "2.0",
  "checkedInAt": "2026-01-23T12:00:00Z",
  "aiAnalysisTriggered": true,
  "correlationId": "..."
}
```

---

## Index Schema

### Field Definitions

| Field | Type | Searchable | Filterable | Purpose |
|-------|------|------------|------------|---------|
| `id` | String | No | Yes | Unique document chunk ID |
| `tenantId` | String | No | Yes | Tenant isolation |
| `deploymentId` | String | No | Yes | Deployment config reference |
| `deploymentModel` | String | No | Yes | Shared/Dedicated/CustomerOwned |
| `knowledgeSourceId` | String | No | Yes | Source knowledge record ID *(R2)* |
| `knowledgeSourceName` | String | Yes | No | Knowledge source display name *(R2)* |
| `documentId` | String | No | Yes | Parent document reference (sprk_document) |
| `speFileId` | String | No | Yes | SharePoint Embedded file ID *(R2)* |
| `fileName` | String | Yes | No | File display name |
| `fileType` | String | No | Yes | File extension (pdf, docx, msg, etc.) *(R2)* |
| `documentType` | String | Yes | Yes | Classification (contract, policy, etc.) - deprecated, use fileType |
| `chunkIndex` | Int32 | No | Yes | Position in document |
| `chunkCount` | Int32 | No | No | Total chunks for document |
| `content` | String | Yes | No | Actual text content |
| `contentVector3072` | Vector (3072) | N/A | N/A | Chunk embedding for semantic search |
| `documentVector3072` | Vector (3072) | N/A | N/A | Document-level embedding for similarity |
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
│  Ribbon Button   ──► POST /api/ai/rag/send-to-index (direct OBO)│
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
       ┌──────────────────────┼──────────────────────┐
       ▼                                             ▼
┌──────────────────────────────────┐  ┌──────────────────────────────────┐
│  RagIndexingJobHandler           │  │  BulkRagIndexingJobHandler       │
├──────────────────────────────────┤  ├──────────────────────────────────┤
│  Job Type: "RagIndexing"         │  │  Job Type: "BulkRagIndexing"     │
│                                  │  │                                  │
│  Processing Flow:                │  │  Processing Flow:                │
│  1. Check idempotency            │  │  1. Query documents (Dataverse)  │
│  2. Acquire processing lock      │  │  2. For each document:           │
│  3. IndexFileAppOnlyAsync()      │  │     a. Check idempotency         │
│  4. Mark as processed            │  │     b. IndexFileAppOnlyAsync()   │
│  5. Release lock                 │  │     c. Update progress           │
│                                  │  │  3. Update job status            │
│  Use: Single file async index    │  │                                  │
└──────────────────────────────────┘  │  Use: Admin bulk operations      │
                                      │  Progress: BatchJobStatusStore   │
                                      └──────────────────────────────────┘
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
*Updated: 2026-01-23 - Automatic re-indexing on check-in, 10-record limit for Send to Index*
*AI Document Intelligence R3 - Phases 1-5 Complete*
*RAG Pipeline R1 - Phase 1 Complete*
*Semantic Search Foundation R1 - Complete (hybrid search API, entity scoping, AI Tool integration)*
*Semantic Search UI R2 - In Progress (PCF v1.0.15, Send to Index v1.26.0, automatic re-indexing on check-in)*
