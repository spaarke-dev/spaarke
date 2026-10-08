# RAG Architecture Guide

> **Version**: 1.5
> **Updated**: 2026-03-05
> **Project**: AI Document Intelligence R3 + RAG Pipeline R1 + Semantic Search Foundation R1 + AI Resource Activation R3
> **Status**: R3 Phases 1-5 Complete, RAG Pipeline Phase 1 Complete, Semantic Search R1 Complete, Resource Activation R3 Code Complete

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
- **Per-Customer Isolation**: a dedicated AI Search service per customer (each stamp's own), in both Model 1 and Model 2; the setting `Analysis:DefaultRagModel` stays at its code default `Shared`, which reads the stamp's own `AiSearch:KnowledgeIndexName` (`spaarke-files-index`, created by H2b); `Dedicated` reads `{tenantId}-knowledge`, which nothing creates — never set it (corrected 2026-10-08, T235; #1432) — see [Deployment Models](#deployment-models).
- **High Performance**: Redis-cached embeddings, P95 < 500ms target latency
- **Scalability**: Per-customer indexes for every customer

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
│  ├── POST /api/ai/rag/search      → Hybrid search (rows trimmed │
│  │                                 to documents caller can Read)│
│  ├── POST /api/ai/rag/index       → Index chunk (SystemAdmin)   │
│  ├── DELETE /api/ai/rag/{id}      → Delete chunk (SystemAdmin)  │
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
│                                                                 │
│  (golden reference WRITES: operator scripts only —                 │
│   scripts/ai-search/Add-ReferenceToIndex.ps1 / Index-AllReferences │
│   .ps1; ReferenceIndexingService was removed by uac-r2 task 163)   │
│                                                                    │
│  ReferenceRetrievalService (ReferenceRetrievalService.cs)          │
│  └── SearchReferencesAsync()  → Hybrid search against references   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    External Services                            │
├─────────────────────────────────────────────────────────────────┤
│  Azure AI Search                Azure OpenAI                    │
│  ├── spaarke-knowledge-index-v2 ├── text-embedding-3-large      │
│  ├── spaarke-rag-references    └── 3072 dimensions              │
│  ├── {tenant}-knowledge                                         │
│  └── Customer indexes                                           │
│                                                                 │
│  Redis Cache                                                    │
│  └── sdap:embedding:{hash}                                      │
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
| `ReferenceRetrievalService` | Query golden reference index (L1 knowledge) | Singleton |

The golden reference index is WRITTEN only by the operator scripts `scripts/ai-search/Add-ReferenceToIndex.ps1` /
`Index-AllReferences.ps1`. The BFF writer (`ReferenceIndexingService`, with `ISchemaMapper` /
`KnowledgeDocumentSchemaMapper`) and its `/api/admin/knowledge/*` routes were removed by unified-access-control-r2
task 163 (owner round 10 item 1: no caller, in no published API description).

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

> **Corrected 2026-10-08 (T235).** The 2026-09-28 (D-12) rewrite of this section said "`Shared` is RETIRED —
> never provision it; `Dedicated` is the default". The *architecture* it described is right — every customer has
> its own AI Search service — but it mapped that onto the wrong setting value. In code
> (`KnowledgeDeploymentService.CreateDefaultConfig`) **`Shared` = "the configured `AiSearch:KnowledgeIndexName`"**,
> which on a stamp is that stamp's own index; **`Dedicated` = "`{tenantId}-knowledge`"**, an index nothing creates.
> What D-12 retired is one AI Search service holding several customers — not the `Shared` setting value.

The `RagDeploymentModel` enum carries two values:

| Value | Status | When |
|---|---|---|
| **`Shared`** (code default) | ✅ **use — leave the setting unset** | Every stamp, Model 1 and Model 2: reads `AiSearch:KnowledgeIndexName` in the stamp's own AI Search service |
| `Dedicated` | 🔴 **never set** | Reads `{tenantId}-knowledge`, which no provisioning step creates (#1432) |

### Per-customer AI Search service (every stamp)

Every customer gets their own AI Search service and index, in their own Azure subscription — Spaarke's
Azure tenant under Model 1, the customer's own tenant under Model 2.

```
  Customer A subscription        Customer B subscription
┌──────────────────────┐  ┌──────────────────────┐
│ customer-a-knowledge │  │ customer-b-knowledge │
├──────────────────────┤  ├──────────────────────┤
│  Customer A docs only│  │  Customer B docs only│
└──────────────────────┘  └──────────────────────┘
```

| Aspect | Details |
|--------|---------|
| **Index Name** | `AiSearch:KnowledgeIndexName` — `spaarke-files-index` by default, created by H2b with the stamp's other canonical indexes |
| **Isolation** | Physical — a separate AI Search **service** per customer, not merely a separate index |
| **Cost** | A real per-customer AI Search floor. Pay it; this is the highest-value segregation case. |
| **Use Case** | **All customers**, both models — the default |
| **Configuration** | None — `Analysis:DefaultRagModel` stays at its default `Shared` |

⚠️ **Naming note.** Index names on a stamp are the same in every stamp; they do not distinguish customers and
need not — **the AI Search service itself is per-customer**, and the resource boundary, not the name, is what
isolates. Do not reintroduce a design in which two customers' documents can land in one service.

### 🔴 Retired: one AI Search service shared across customers

Before D-12 the shared Model 1 tier placed **every customer's document text and embeddings in one AI Search
index** (`spaarke-knowledge-index-v2` on Spaarke's service) and isolated them with a per-query `tenantId eq '…'`
filter. (The `Shared` setting value pointed at that index then; today it points at the stamp's own index.)

It is retired for one decisive reason: **under Model 1 every customer presents the same `tenantId`**
(Spaarke's), so the filter separates **Entra tenants** only — never **customers** — while every test and
health signal keyed on it reports success. A filter must also be written correctly in every query, forever,
by everyone; a resource boundary cannot be forgotten. For a product holding privileged legal material, that
difference is the whole argument.

**Never put two customers' documents in one AI Search service, and never rely on a `tenantId` filter for
customer isolation.**

**Index Name Sanitization** (the unused `Dedicated` value only):
- Converted to lowercase
- Non-alphanumeric characters removed (except hyphens)
- Format: `{sanitized-tenant}-knowledge`

Examples:
| Tenant ID | Sanitized Index Name |
|-----------|---------------------|
| `Tenant-ABC-123` | `tenantabc123-knowledge` |
| `ENTERPRISE_CORP` | `enterprisecorp-knowledge` |
| `acme.inc` | `acmeinc-knowledge` |

### Removed: the CustomerOwned model

The CustomerOwned model (an index in another subscription reached with an API key) was removed by customer-provisioning-orchestration-r1 task 230b (2026-10-06): a customer that brings its own Azure subscription/tenant gets a dedicated Model 2 stamp (D-12), whose BFF uses its own AI Search with its managed identity — no key (owner D13). `Analysis:DefaultRagModel` accepts `Shared` or `Dedicated`; any other value fails at startup.

### Model Comparison

| Feature | Per-customer service (every stamp; setting default `Shared`) | ~~One service for all customers~~ (retired, D-12) |
|---------|---------------------|----------------------|
| Physical Isolation | Yes | ~~No~~ |
| Index Location | The customer's own subscription (Spaarke's Azure tenant under Model 1, the customer's under Model 2) | ~~One Spaarke index for everyone~~ |
| Cost to Customer | Directly attributable — their own subscription | ~~Included~~ |
| Setup Complexity | Low | ~~None~~ |
| Data Sovereignty | Partial (full under Model 2) | ~~No~~ |
| Compliance (SOC2, etc.) | Dedicated | ~~Shared~~ |

---

## Golden Reference Index (R3)

The `spaarke-rag-references` index stores curated domain knowledge separate from customer documents.

### Purpose

| Index | Content | Scale | Access |
|-------|---------|-------|--------|
| `{customer}-knowledge` | That customer's documents | 100K+ chunks | Dedicated AI Search service per customer |
| `spaarke-rag-references` | Curated domain knowledge (KNW-001–010) | ~100 chunks | Spaarke-curated reference content — carries no customer data |

Separating references from customer documents ensures:
- **Guaranteed retrieval**: Small index = high recall for domain terms
- **No noise**: Customer documents can't dilute reference quality
- **Fast queries**: ~100 chunks vs 100K+ in customer index

### Schema

Key fields beyond standard `KnowledgeDocument`:

| Field | Purpose |
|-------|---------|
| `knowledgeSourceId` | Links chunk to Dataverse `sprk_analysisknowledge` record |
| `knowledgeSourceName` | Human-readable source name (e.g., "Contract Clause Library") |
| `documentType` | Domain tag (e.g., "contract-law", "financial-analysis") |

### Indexing the reference index
 reference index is populated by the operator scripts `scripts/ai-search/Add-ReferenceToIndex.ps1` and `scripts/ai-search/Index-AllReferences.ps1` (see the `add-reference-to-index` skill). The BFF admin routes that used to do this (`/api/admin/knowledge/index-references`, `POST`/`DELETE /api/admin/knowledge/index-reference/{id}`) were **deleted** by unified-access-control-r2 task 163: they had no caller and let any signed-in user re-embed, overwrite or wipe the shared grounding index.

### Knowledge-Augmented Execution

When a playbook executes an AI node, `AiAnalysisNodeExecutor` retrieves knowledge in three tiers:

```
Prompt Assembly Order:
  1. Skill instructions (JPS or flat text)
  2. L1: Golden reference knowledge (spaarke-rag-references)
  3. L2: Similar customer documents (spaarke-knowledge-index-v2) [optional]
  4. L3: Business entity context (spaarke-records-index) [optional]
  5. Document content
```

Controlled per-action via `KnowledgeRetrievalConfig` in ConfigJson:
```json
{
  "knowledgeRetrieval": {
    "mode": "auto",
    "topK": 5,
    "includeDocumentContext": false,
    "includeEntityContext": false
  }
}
```

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
| `knowledgeSourceIds` | null | Include chunks from these sources (OR logic) |
| `excludeKnowledgeSourceIds` | null | Exclude chunks from these sources (NOT logic) |
| `tags` | null | Include chunks with any of these tags (OR logic) |
| `requiredTags` | null | Require ALL of these tags (AND logic) |
| `excludeTags` | null | Exclude chunks with any of these tags (NOT logic) |
| `parentEntityType` | null | Filter by parent entity type (e.g., "matter", "project") |
| `parentEntityId` | null | Filter by parent entity ID — both type and ID must be set |

#### OData Filter Generation

The `BuildSearchOptions` method in `RagService` constructs OData filters with boolean logic:

- **Include sources (≤10 items)**: OR chain — `(knowledgeSourceId eq 'a' or knowledgeSourceId eq 'b')`
- **Include sources (>10 items)**: `search.in(knowledgeSourceId, 'a,b,...', ',')` — avoids Azure AI Search clause limits
- **Exclude sources**: `not search.in(knowledgeSourceId, 'x,y', ',')`
- **Required tags (AND)**: Individual `tags/any(t: t eq 'tag')` per tag, joined with `and`
- **Include tags (OR)**: `tags/any(t: search.in(t, 'a,b', ','))`
- **Exclude tags**: `not tags/any(t: search.in(t, 'a,b', ','))`
- **Entity scope**: `parentEntityType eq 'matter' and parentEntityId eq 'guid'` — partial scope (only one set) is ignored

All filters are combined with `and` semantics. The 10-item threshold for `search.in()` balances log readability (OR chains are easier to debug) with performance (search.in avoids clause limits).

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
| `deploymentModel` | String | No | Yes | Shared/Dedicated |
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
}
```

**Key Behaviors**:
- Caches SearchClient instances per tenant
- Creates a default config if none exists — the code default `Shared` reads `AiSearch:KnowledgeIndexName`
  (the stamp's own index), which is correct on every stamp. Do not set `Dedicated` (`{tenantId}-knowledge`
  exists nowhere — #1432).
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
- Constructs OData filters with boolean logic from `RagSearchOptions` extended properties (see [Search Options](#search-options))
- Entity scoping via `ParentEntityType`/`ParentEntityId` for workspace-level isolation

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
│  Implements: IJobHandler (non-generic)                           │
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

### Customer Isolation

| Model | Isolation Method | Security Level |
|-------|-----------------|----------------|
| **Per-customer service** (every stamp; setting default `Shared`) | A separate AI Search **service** per customer, in that customer's own subscription | Physical — a resource boundary |
| ~~One service for all customers~~ (retired, D-12) | ~~`tenantId` filter on all queries~~ | 🔴 **None between customers** — see below |

### Query filter — what it does and does not enforce

All searches still include the tenant filter, and the mechanism is unchanged:

```csharp
var filter = $"tenantId eq '{tenantId}'";
var searchOptions = new SearchOptions
{
    Filter = filter,
    // ... other options
};
```

⚠️ **This filter separates Entra tenants, not customers.** Under Model 1 every customer presents
**Spaarke's** tenant GUID, so the filter is a no-op between customers — and it reports success while doing
nothing. It is retained as belt-and-braces (it is correct and cheap, and it is load-bearing under Model 2
where tenants really do differ), but **the isolation that matters comes from the dedicated AI Search
service**, not from this predicate. Never cite a `tenantId` filter as evidence of *customer* isolation.

### Search authentication

The BFF reaches its stamp's own AI Search service with its managed identity — no API key. The former
key-based `CustomerOwned` path was removed (task 230b, 2026-10-06); see [Deployment Models](#deployment-models).

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
| Large document corpus | Scale that customer's own AI Search SKU — every customer is already on `Dedicated` |
| Many concurrent users | Scale that customer's Redis (dedicated, Standard tier) |
| Customer requires data sovereignty / BYOK | Model 2 — a dedicated stamp in the customer's own subscription/tenant (`Dedicated`) |

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
Playbook Execution (AiAnalysisNodeExecutor)
              │
              ├── L1: ReferenceRetrievalService.SearchReferencesAsync()
              │       └── spaarke-rag-references (golden domain knowledge)
              │
              ├── L2: IRagService.SearchAsync() [optional]
              │       └── spaarke-knowledge-index-v2 (similar customer docs)
              │
              ├── L3: IRecordSearchService [optional]
              │       └── spaarke-records-index (business entity metadata)
              │
              ├── Merge L1 + L2 + L3 → KnowledgeContext
              │
              ├── Build prompt: Skills + Knowledge + Document
              │
              └── IOpenAiClient → Azure OpenAI → Analysis Response
```

### Completed Integrations (R3)

| Integration | Purpose | Phase | Status |
|-------------|---------|-------|--------|
| Analysis Orchestration | RAG context in analysis prompts | Phase 2 | ✅ Complete |
| Playbook System | Pre-configured RAG queries | Phase 2 | ✅ Complete |
| Export Services | Include sources in DOCX/PDF/Email/Teams exports | Phase 3 | ✅ Complete |
| Circuit Breaker | Polly resilience for AI Search | Phase 4 | ✅ Complete |
| Tenant Authorization | `TenantAuthorizationFilter` for isolation | Phase 5 | ✅ Complete |
| Knowledge-Augmented Execution | L1/L2/L3 tiered knowledge retrieval in playbook nodes | R3 | ✅ Complete |
| Reference Index Admin | Admin endpoints for golden reference indexing | R3 | ✅ Complete |
| Result Caching | Redis caching for reference retrieval results | R3 | ✅ Complete |

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
*Updated: 2026-03-05 - Knowledge-Augmented Execution, Golden Reference Index, Model Selection (AI Resource Activation R3)*
