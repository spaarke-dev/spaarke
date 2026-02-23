# AI Semantic Search Foundation - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-20
> **Revised**: 2026-01-20
> **Source**: design.md

## Executive Summary

This project establishes the **foundational API infrastructure** for AI-powered semantic search across the Spaarke document management system. It delivers a reusable `SemanticSearchService` in the BFF API that provides hybrid search (vector + keyword), entity-scoped filtering, and extensibility hooks for future agentic RAG integration.

## Scope

### In Scope

- **Hybrid Search API**: Vector + keyword search with RRF (Reciprocal Rank Fusion) result fusion
- **Entity-Agnostic Scoping**: Search within any parent entity (Matter, Project, Invoice, Account, Contact)
- **Filter Builder**: Document Type, File Type, Date Range, Tags
- **Query Embedding**: Real-time query vectorization via Azure OpenAI
- **Result Scoring**: Combined relevance scoring via RRF (single `combinedScore`)
- **AI Tool Handler**: Copilot integration via `search_documents` tool
- **Extensibility Hooks**: `IQueryPreprocessor`, `IResultPostprocessor` interfaces for future agentic RAG
- **Index Schema Extension**: Add `parentEntityType` and `parentEntityId` fields

### Out of Scope

- LLM-based query rewriting (deferred to Agentic RAG project)
- Cross-encoder reranking (deferred to Agentic RAG project)
- Auto-inferred filters from query text (deferred to Agentic RAG project)
- Conversational search refinement (deferred to Agentic RAG project)
- Full Playbook scope integration (deferred to Agentic RAG project)
- Search UI/PCF control (separate project: `ai-semantic-search-ui-r2`)
- `scope=all` (deferred until scalable security trimming implemented)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/Ai/` - New search endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/` - New SemanticSearchService
- `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/` - AI Tool handler for Copilot
- `infrastructure/ai-search/` - Index schema extension
- Existing: `IEmbeddingService`, `IAiSearchClientFactory`, `IDataverseService` (reuse)

---

## Security Trimming

> **P0 Requirement**: Tenant isolation is necessary but NOT sufficient. Every search must enforce resource-level authorization.

### R1 Security Model

For R1, security is enforced through **caller-provided authorized scopes**:

| Scope | Authorization Model | Implementation |
|-------|---------------------|----------------|
| `entity` | Caller authorizes `entityId` via UAC before calling search | Endpoint filter validates entity access, then filters by `parentEntityId` |
| `documentIds` | Caller provides pre-authorized document ID list | Endpoint filter validates each document (or batch via parent entity) |
| `all` | **NOT SUPPORTED IN R1** | Returns `400 NotSupported` |

### Authorization Flow

```
1. Request arrives at /api/ai/search/semantic
2. Endpoint filter extracts scope parameters
3. IF scope=entity:
   - Validate user has access to entityType+entityId via UAC
   - Add parentEntityType + parentEntityId to search filter
4. IF scope=documentIds:
   - Validate user has access to each documentId (or their parent entities)
   - Add documentId filter to search
5. IF scope=all:
   - Return 400 NotSupported (R1 constraint)
6. Add tenantId filter (always required)
7. Execute search with combined filters
```

### Filter Composition

Every search request MUST include:
```
$filter = tenantId eq '{userTenantId}'
          AND {scope-specific-filter}
          AND {user-provided-filters}
```

### Future: `scope=all` (R2+)

When `scope=all` is implemented, it will require one of:
- **Index-level ACL strategy**: Add `authorizedUsers` or `authorizedGroups` field to index
- **Bounded access set**: Cache user's accessible entity IDs, filter by those

---

## Index Contract: spaarke-knowledge-index-v2

> **P0 Requirement**: Explicit schema definition prevents implementation drift and filter bugs.

### Current Schema

| Field | Type | Searchable | Filterable | Sortable | Notes |
|-------|------|------------|------------|----------|-------|
| `id` | Edm.String | - | - | - | Key field (chunk ID) |
| `tenantId` | Edm.String | - | ✅ | - | Tenant isolation |
| `deploymentId` | Edm.String | - | ✅ | - | Deployment model |
| `documentId` | Edm.String | - | ✅ | - | Spaarke Document ID |
| `speFileId` | Edm.String | - | ✅ | - | SPE file ID |
| `documentName` | Edm.String | ✅ | - | ✅ | Document display name |
| `fileName` | Edm.String | ✅ | - | ✅ | Original file name |
| `documentType` | Edm.String | - | ✅ | - | Document type (Contract, Invoice, etc.) |
| `fileType` | Edm.String | - | ✅ | - | File extension (pdf, docx, etc.) |
| `content` | Edm.String | ✅ | - | - | Chunk text content |
| `contentVector3072` | Collection(Edm.Single) | ✅ | - | - | Chunk embedding (3072 dims) |
| `documentVector3072` | Collection(Edm.Single) | ✅ | - | - | Document embedding (3072 dims) |
| `tags` | Collection(Edm.String) | ✅ | ✅ | - | Document tags |
| `createdAt` | Edm.DateTimeOffset | - | ✅ | ✅ | Creation timestamp |
| `updatedAt` | Edm.DateTimeOffset | - | ✅ | ✅ | Last update timestamp |
| `chunkIndex` | Edm.Int32 | - | - | ✅ | Chunk sequence number |
| `chunkCount` | Edm.Int32 | - | - | - | Total chunks in document |
| `knowledgeSourceId` | Edm.String | - | ✅ | - | AI knowledge grouping (not business entity) |
| `knowledgeSourceName` | Edm.String | ✅ | - | ✅ | Knowledge source display name |
| `metadata` | Edm.String | - | - | - | JSON metadata blob |

### Required Schema Extensions (R1 Prerequisite)

| Field | Type | Searchable | Filterable | Purpose |
|-------|------|------------|------------|---------|
| `parentEntityType` | Edm.String | - | ✅ | Entity type: "matter", "project", "invoice", "account", "contact" |
| `parentEntityId` | Edm.String | - | ✅ | Parent entity GUID |
| `parentEntityName` | Edm.String | ✅ | - | Parent entity display name (for results enrichment) |

### Filter Syntax Notes

- **Date filters**: Use ISO 8601 format: `createdAt ge 2024-01-01T00:00:00Z`
- **String equality**: `documentType eq 'Contract'`
- **Collection contains**: `tags/any(t: t eq 'important')`
- **Multiple values**: `search.in(documentType, 'Contract,Agreement', ',')`

---

## API Design

### Endpoint: Semantic Search

```
POST /api/ai/search/semantic
Authorization: Bearer {token}
Content-Type: application/json
```

#### Request Schema

```json
{
  "query": "contracts about payment terms",
  "scope": "entity" | "documentIds",
  "entityType": "matter" | "project" | "invoice" | "account" | "contact",
  "entityId": "guid-here",
  "documentIds": ["guid1", "guid2"],
  "filters": {
    "documentTypes": ["Contract", "Agreement"],
    "fileTypes": ["pdf", "docx"],
    "tags": ["important", "reviewed"],
    "dateRange": {
      "field": "createdAt" | "updatedAt",
      "from": "2024-01-01T00:00:00Z",
      "to": "2024-12-31T23:59:59Z"
    }
  },
  "options": {
    "limit": 20,
    "offset": 0,
    "includeHighlights": true,
    "hybridMode": "rrf" | "vectorOnly" | "keywordOnly"
  }
}
```

#### Request Validation Rules

| Field | Constraint | Error |
|-------|------------|-------|
| `query` | Max 1000 characters | 400 `QUERY_TOO_LONG` |
| `query` | Required unless `hybridMode=keywordOnly` with filters | 400 `QUERY_REQUIRED` |
| `scope` | Required, must be "entity" or "documentIds" | 400 `INVALID_SCOPE` |
| `scope=all` | Not supported in R1 | 400 `SCOPE_NOT_SUPPORTED` |
| `entityType` | Required when `scope=entity` | 400 `ENTITY_TYPE_REQUIRED` |
| `entityId` | Required when `scope=entity` | 400 `ENTITY_ID_REQUIRED` |
| `documentIds` | Required when `scope=documentIds`, max 100 items | 400 `DOCUMENT_IDS_REQUIRED` |
| `options.limit` | 1-50, default 20 | 400 `INVALID_LIMIT` |
| `options.offset` | 0-1000, default 0 | 400 `INVALID_OFFSET` |

#### Empty Query Behavior

| `query` | `hybridMode` | Behavior |
|---------|--------------|----------|
| Empty/null | `rrf` or `vectorOnly` | 400 `QUERY_REQUIRED` |
| Empty/null | `keywordOnly` | Valid - returns filtered results without text scoring |
| Non-empty | Any | Normal search |

#### Response Schema

```json
{
  "results": [
    {
      "documentId": "abc-123",
      "speFileId": "spe-456",
      "name": "Master Service Agreement - Acme Corp.pdf",
      "documentType": "Contract",
      "fileType": "pdf",
      "combinedScore": 0.82,
      "similarity": null,
      "keywordScore": null,
      "highlights": [
        "...payment terms shall be net 30 days from invoice date...",
        "...conditions of payment include..."
      ],
      "parentEntityType": "matter",
      "parentEntityId": "matter-789",
      "parentEntityName": "Acme Corp Litigation",
      "fileUrl": "https://...",
      "recordUrl": "https://...",
      "createdAt": "2024-06-15T10:30:00Z",
      "updatedAt": "2024-08-20T14:45:00Z"
    }
  ],
  "metadata": {
    "totalResults": 127,
    "returnedResults": 20,
    "searchDurationMs": 245,
    "appliedFilters": {
      "documentTypes": ["Contract", "Agreement"],
      "dateRange": { "from": "2024-01-01T00:00:00Z", "to": "2024-12-31T23:59:59Z" }
    },
    "warnings": [
      {
        "code": "EMBEDDING_UNAVAILABLE",
        "message": "Vector search unavailable, results based on keyword matching only",
        "details": null
      }
    ]
  }
}
```

#### Scoring Fields (R1)

| Field | R1 Behavior | Future (R2+) |
|-------|-------------|--------------|
| `combinedScore` | Azure AI Search fused score (0.0-1.0) | Same |
| `similarity` | `null` | Separate vector query score |
| `keywordScore` | `null` | Separate BM25 score |

### Endpoint: Count

```
POST /api/ai/search/semantic/count
```

Same request body. Returns:

```json
{
  "count": 127,
  "appliedFilters": { ... },
  "warnings": []
}
```

**Optimization**: When `query` is empty and `hybridMode=keywordOnly`, skip embedding generation entirely.

---

## Search Execution Contract

> Defines exact query strategy per `hybridMode` to prevent implementation drift.

### Mode: `rrf` (Default)

```
1. Embed query → 3072-dim vector
2. Execute Azure AI Search hybrid query:
   - VectorQuery: documentVector3072, k=limit*3 (over-retrieve for fusion)
   - TextQuery: query text against content, documentName, fileName
   - QueryType: semantic (if configured) or simple
   - Filter: combined security + user filters
3. Azure AI Search applies RRF internally
4. Return top `limit` results
```

### Mode: `vectorOnly`

```
1. Embed query → 3072-dim vector
2. Execute Azure AI Search vector-only query:
   - VectorQuery: documentVector3072, k=limit
   - NO text query
   - Filter: combined security + user filters
3. Return results ranked by vector similarity
```

### Mode: `keywordOnly`

```
1. NO embedding call (skip entirely)
2. Execute Azure AI Search text-only query:
   - TextQuery: query text (or "*" if empty) against content, documentName, fileName
   - QueryType: simple
   - Filter: combined security + user filters
3. Return results ranked by BM25
```

---

## Enrichment Strategy

> Prevents N+1 Dataverse calls by batching metadata lookups.

### Enrichment Fields

| Field | Source | Strategy |
|-------|--------|----------|
| `parentEntityName` | Index (if `parentEntityName` field added) | Preferred - no extra call |
| `parentEntityName` | Dataverse lookup | Fallback - batched |
| `fileUrl` | Computed from `speFileId` | No external call |
| `recordUrl` | Computed from `documentId` | No external call |

### Batch Lookup Pattern

```csharp
// Collect unique entity IDs from results
var entityIds = results
    .Select(r => (r.ParentEntityType, r.ParentEntityId))
    .Distinct()
    .ToList();

// Single batched Dataverse query (max 50 per batch)
var entityNames = await _dataverseService.GetEntityNamesAsync(entityIds);

// Enrich results
foreach (var result in results)
    result.ParentEntityName = entityNames[(result.ParentEntityType, result.ParentEntityId)];
```

### Caching (Allowed)

Entity names may be cached with short TTL (5 minutes) since they change infrequently. This does NOT contradict "no search result caching" - we cache reference data, not search results.

---

## Observability

> Defines safe logging fields (no PII, no content).

### Request Logging

| Field | Log | Notes |
|-------|-----|-------|
| `correlationId` | ✅ | From request headers |
| `tenantId` | ✅ | Or tenant hash |
| `scope` | ✅ | "entity" or "documentIds" |
| `entityType` | ✅ | When scope=entity |
| `entityId` | ✅ | GUID only |
| `documentIds.Count` | ✅ | Count only, not IDs |
| `query` | ❌ | May contain sensitive info |
| `filters` | ✅ | Filter structure (not values for free-text) |
| `hybridMode` | ✅ | rrf/vectorOnly/keywordOnly |

### Response Logging

| Field | Log | Notes |
|-------|-----|-------|
| `totalResults` | ✅ | |
| `returnedResults` | ✅ | |
| `searchDurationMs` | ✅ | |
| `embedDurationMs` | ✅ | Time for embedding call |
| `enrichDurationMs` | ✅ | Time for Dataverse enrichment |
| `fallbackOccurred` | ✅ | True if embedding failed |
| `warnings` | ✅ | Warning codes only |

### Metrics to Emit

```
semantic_search_requests_total{scope, entityType, hybridMode}
semantic_search_duration_ms{quantile="p50|p95|p99"}
semantic_search_embed_duration_ms{quantile="p50|p95"}
semantic_search_fallback_total{reason}
semantic_search_errors_total{errorCode}
```

---

## Requirements

### Functional Requirements

1. **FR-01**: Hybrid Search Endpoint - `POST /api/ai/search/semantic` returns ranked results using RRF fusion
   - Acceptance: Returns results with `combinedScore`; `similarity` and `keywordScore` are null for R1

2. **FR-02**: Count Endpoint - `POST /api/ai/search/semantic/count` returns total matching documents
   - Acceptance: Skips embedding when query is empty with `keywordOnly` mode

3. **FR-03**: Entity Scope - Support `scope=entity` with `entityType` and `entityId`
   - Acceptance: Filters by `parentEntityType` and `parentEntityId` in index

4. **FR-04**: DocumentIds Scope - Support `scope=documentIds` with list of document GUIDs
   - Acceptance: Filters by `documentId` in index; max 100 documents

5. **FR-05**: Content Filtering - Support filters for documentTypes, fileTypes, tags, dateRange
   - Acceptance: Filters correctly apply to search results

6. **FR-06**: AI Tool Integration - `search_documents` tool available in Copilot via `IAiToolHandler`
   - Acceptance: Copilot can invoke search via natural language; results formatted for chat

7. **FR-07**: Extensibility Interfaces - `IQueryPreprocessor` and `IResultPostprocessor` interfaces
   - Acceptance: Interfaces defined and invoked in pipeline; no-op implementations for R1

8. **FR-08**: Embedding Fallback - Fall back to keyword-only search when embedding fails
   - Acceptance: Returns results with warning `EMBEDDING_UNAVAILABLE`; no hard error

9. **FR-09**: Request Validation - Validate all request parameters with clear error codes
   - Acceptance: Invalid requests return 400 with stable `errorCode`

10. **FR-10**: Index Schema Extension - Add `parentEntityType`, `parentEntityId`, `parentEntityName` fields
    - Acceptance: Fields exist in index; existing documents re-indexed with parent entity data

### Non-Functional Requirements

- **NFR-01**: Search latency p50 < 500ms, p95 < 1000ms
- **NFR-02**: Support 50 concurrent searches
- **NFR-03**: Bearer token authentication on all endpoints
- **NFR-04**: Security trimming via scope-based authorization (see Security Trimming section)
- **NFR-05**: No PII in logs (see Observability section)
- **NFR-06**: Rate limiting applied to search endpoints

---

## Technical Constraints

### Applicable ADRs

| ADR | Constraint Summary |
|-----|-------------------|
| **ADR-001** | Use Minimal API patterns; no Azure Functions |
| **ADR-008** | Use endpoint filters for authorization (not global middleware) |
| **ADR-010** | DI minimalism - ≤15 non-framework registrations; register concretes |
| **ADR-013** | Extend BFF API for AI; apply rate limiting to all AI endpoints |
| **ADR-016** | Bounded concurrency for upstream AI calls; clear 429/503 responses |
| **ADR-019** | Return ProblemDetails for all errors with correlation ID |

### MUST Rules

- ✅ MUST use Minimal API for all HTTP endpoints
- ✅ MUST use endpoint filters for resource authorization
- ✅ MUST return ProblemDetails for all API errors with correlation ID and stable `errorCode`
- ✅ MUST apply rate limiting to AI endpoints
- ✅ MUST bound concurrency for upstream AI service calls (embedding, search)
- ✅ MUST include `tenantId` filter in every search query
- ✅ MUST validate scope authorization before executing search
- ✅ MUST batch Dataverse enrichment calls (not N+1)
- ✅ MUST NOT support `scope=all` in R1
- ✅ MUST NOT leak document content in error messages or logs
- ✅ MUST NOT call Azure AI services directly from PCF

### Existing Components to Reuse

| Component | Location | Status |
|-----------|----------|--------|
| `IEmbeddingService` | `Sprk.Bff.Api.Services.Ai` | Exists - reuse for query embedding |
| `IAiSearchClientFactory` | `Sprk.Bff.Api.Services.Ai` | Exists - reuse for search client |
| `IDataverseService` | `Sprk.Bff.Api.Services` | Exists - reuse for metadata enrichment |
| Azure AI Search index | `spaarke-knowledge-index-v2` | Exists - requires schema extension |
| Azure OpenAI | `text-embedding-3-large` | Exists - 3072-dimension embeddings |

### New Components to Create

| Component | Responsibility |
|-----------|---------------|
| `SemanticSearchService` | Core search orchestration |
| `SemanticSearchEndpoints` | API endpoints |
| `SearchFilterBuilder` | OData filter string construction |
| `SemanticSearchToolHandler` | IAiToolHandler for Copilot |
| `IQueryPreprocessor` | Extensibility interface |
| `IResultPostprocessor` | Extensibility interface |
| `SemanticSearchAuthorizationFilter` | Endpoint filter for scope authorization |

---

## Success Criteria

1. [ ] `POST /api/ai/search/semantic` returns relevant results - Verify: Integration test with sample queries
2. [ ] Hybrid search uses RRF fusion correctly - Verify: Compare results across modes
3. [ ] Entity scope filters by `parentEntityType` + `parentEntityId` - Verify: Unit tests
4. [ ] DocumentIds scope filters correctly - Verify: Unit tests
5. [ ] `search_documents` AI Tool works in Copilot - Verify: Manual test in Copilot UI
6. [ ] Embedding failure falls back to keyword-only with warning - Verify: Integration test with mocked failure
7. [ ] Search latency < 1s p95 - Verify: Load test with 50 concurrent requests
8. [ ] Security trimming enforced (no unauthorized document access) - Verify: Security test cases
9. [ ] Index schema extended with parent entity fields - Verify: Index definition includes new fields
10. [ ] Request validation returns clear error codes - Verify: Unit tests for each validation rule

---

## Dependencies

### Prerequisites (Must Complete First)

1. **Index Schema Extension**: Add `parentEntityType`, `parentEntityId`, `parentEntityName` to `spaarke-knowledge-index-v2`
2. **Indexing Pipeline Update**: Ensure new documents include parent entity fields when indexed

> **Note**: Existing indexed documents will NOT be backfilled (dev decision). Entity-scoped search only returns documents indexed after schema extension.

### External Dependencies

- Azure OpenAI API availability (embedding generation)
- Azure AI Search API availability (hybrid search execution)
- Dataverse API availability (metadata enrichment, authorization checks)

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Entity scoping | Matter-specific or entity-agnostic? | Entity-agnostic (Matter, Project, Invoice, Account, Contact) | API uses `entityType` + `entityId`; index needs `parentEntityType` + `parentEntityId` |
| Hybrid weight tuning | Use RRF defaults or explicit weights? | Use RRF defaults | No custom weight configuration |
| Index consistency | Wait for indexing or accept eventual consistency? | Accept eventual consistency | No freshness options |
| Result caching | Cache repeated queries? | No caching for R1 | Always fresh results |
| Scoring fields | Populate separate vector/keyword scores? | R1: null, only `combinedScore` | Simpler implementation |
| Embedding failure | Error, fallback, or retry? | Fallback to keyword-only with warning | Graceful degradation |
| `scope=all` | Support in R1? | No - return 400 NotSupported | Security-first approach |
| Index migration | How to populate parent entity fields for existing docs? | New docs only; old docs excluded from entity-scoped search | Acceptable for dev; production migration TBD |

## Assumptions

- **Entity name caching**: Short TTL (5 min) caching of entity names is acceptable for enrichment
- **Rate limiting policy**: Using existing AI rate limiting policies; specific limits TBD
- **Highlights format**: Using Azure AI Search highlight format directly

## Unresolved Questions

*None - all questions resolved.*

---

*AI-optimized specification. Original design: design.md*
*Revised based on technical review feedback: security trimming, index contract, scoring clarification, validation rules, execution contract, enrichment strategy, observability.*
