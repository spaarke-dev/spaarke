# AI Semantic Search Foundation - Design Document

> **Project**: ai-semantic-search-foundation-r1
> **Version**: 1.0
> **Created**: January 2026
> **Status**: Draft

---

## Executive Summary

This project establishes the **foundational API infrastructure** for AI-powered semantic search across the Spaarke document management system. The foundation provides hybrid search capabilities (vector + keyword), manual filter support, and extensibility hooks for future agentic RAG integration.

**Key Deliverable**: A reusable `SemanticSearchService` in the BFF API that powers:
- Repository-wide document search
- Scoped search (within a Matter, Project, or document subset)
- Copilot integration via AI Tool
- Graph filter functionality (Document Relationship Viewer)

---

## Problem Statement

### Current State

- Documents have vector embeddings in Azure AI Search (`documentVector3072`)
- Related document visualization uses vector similarity search
- No general-purpose semantic search capability exists
- Users cannot search documents using natural language

### Business Need

An AI-enabled Document Management System requires **semantic search** as a core capability:
- Users expect to search using natural language ("find contracts about payment terms")
- Search must understand meaning, not just keywords
- Results must be filterable by Matter, Document Type, Date, etc.
- Search must integrate with Copilot for conversational access

### Gap Analysis

| Capability | Current State | Required State |
|------------|---------------|----------------|
| Vector search | Exists (Visualization) | Expose as general API |
| Keyword search | None | Add BM25/full-text |
| Hybrid search | None | Combine vector + keyword |
| Filter support | Partial | Full filter builder |
| Query embedding | Exists (indexing) | Add query-time embedding |
| Copilot integration | None | AI Tool handler |
| Extensibility | None | Preprocessor/postprocessor hooks |

---

## Scope

### In Scope (R1)

| Feature | Description |
|---------|-------------|
| **Hybrid Search API** | Vector + keyword search with result fusion |
| **Filter Builder** | Matter, Document Type, Date Range, Document IDs |
| **Query Embedding** | Real-time query vectorization |
| **Result Scoring** | Combined relevance scoring |
| **Scoped Search** | All documents, by Matter, by document ID subset |
| **AI Tool Handler** | Copilot integration for natural language search |
| **Extensibility Hooks** | `IQueryPreprocessor`, `IResultPostprocessor` interfaces |

### Out of Scope (Future)

| Feature | Deferred To |
|---------|-------------|
| LLM-based query rewriting | Agentic RAG project |
| Cross-encoder reranking | Agentic RAG project |
| Auto-inferred filters from query | Agentic RAG project |
| Conversational search refinement | Agentic RAG project |
| Full Playbook scope integration | Agentic RAG project |

---

## Technical Architecture

### System Context

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              CONSUMERS                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────────┐ │
│  │ Semantic Search │  │ Document        │  │ Copilot                     │ │
│  │ PCF (Toolbar)   │  │ Relationship    │  │ (AI Chat)                   │ │
│  │                 │  │ Viewer          │  │                             │ │
│  └────────┬────────┘  └────────┬────────┘  └──────────────┬──────────────┘ │
│           │                    │                          │                 │
│           └────────────────────┼──────────────────────────┘                 │
│                                ▼                                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                         BFF API LAYER                                        │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                   SemanticSearchEndpoints                             │   │
│  │  POST /api/ai/search/semantic                                        │   │
│  │  POST /api/ai/search/semantic/count                                  │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                │                                            │
│                                ▼                                            │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                   SemanticSearchService                               │   │
│  │  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────┐  │   │
│  │  │ Query          │  │ Hybrid Search  │  │ Result                 │  │   │
│  │  │ Preprocessor   │──▶│ Executor       │──▶│ Postprocessor          │  │   │
│  │  │ (extensible)   │  │                │  │ (extensible)           │  │   │
│  │  └────────────────┘  └────────────────┘  └────────────────────────┘  │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
│                                │                                            │
│           ┌────────────────────┼────────────────────┐                       │
│           ▼                    ▼                    ▼                       │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ IEmbedding      │  │ Azure AI        │  │ IDataverse      │             │
│  │ Service         │  │ Search Client   │  │ Service         │             │
│  │ (query→vector)  │  │ (hybrid search) │  │ (metadata)      │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Hybrid Search Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        HYBRID SEARCH EXECUTION                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. PREPROCESS QUERY                                                        │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  User Query: "contracts about payment terms from last year"       │    │
│     │                           │                                       │    │
│     │                           ▼                                       │    │
│     │  [IQueryPreprocessor] ← EXTENSIBILITY HOOK (future: LLM rewrite) │    │
│     │                           │                                       │    │
│     │                           ▼                                       │    │
│     │  Processed Query + Extracted Filters                              │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  2. EMBED QUERY                                                             │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  IEmbeddingService.EmbedAsync(query)                              │    │
│     │  → Azure OpenAI text-embedding-3-large                            │    │
│     │  → 3072-dimension vector                                          │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  3. BUILD FILTERS                                                           │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  SearchFilterBuilder.Build(filters)                               │    │
│     │  → $filter: documentType eq 'Contract' and createdOn ge 2024-01  │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  4. EXECUTE HYBRID SEARCH                                                   │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  Azure AI Search with:                                            │    │
│     │  - Vector query (k-NN on documentVector3072)                      │    │
│     │  - Text query (BM25 on content, title, keywords)                  │    │
│     │  - Filters applied                                                │    │
│     │  - Reciprocal Rank Fusion (RRF) for score combination             │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  5. POSTPROCESS RESULTS                                                     │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  [IResultPostprocessor] ← EXTENSIBILITY HOOK (future: reranking) │    │
│     │                           │                                       │    │
│     │                           ▼                                       │    │
│     │  Enrich with Dataverse metadata (URLs, Matter names)              │    │
│     │  Format highlights, compute final scores                          │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## API Design

### Endpoint: Semantic Search

```
POST /api/ai/search/semantic
Authorization: Bearer {token}
Content-Type: application/json

{
  "query": "contracts about payment terms and conditions",
  "scope": "all" | "matter" | "documentIds",
  "scopeId": "matter-guid-here",           // Required if scope = "matter"
  "documentIds": ["guid1", "guid2"],        // Required if scope = "documentIds"
  "filters": {
    "documentTypes": ["Contract", "Agreement"],
    "matterTypes": ["Litigation", "Corporate"],
    "fileTypes": ["pdf", "docx"],
    "dateRange": {
      "field": "createdOn" | "modifiedOn",
      "from": "2024-01-01",
      "to": "2024-12-31"
    }
  },
  "options": {
    "limit": 50,
    "offset": 0,
    "includeHighlights": true,
    "includeExplanation": false,           // Future: relevance explanation
    "hybridMode": "rrf" | "vectorOnly" | "keywordOnly"
  }
}
```

### Response

```json
{
  "results": [
    {
      "documentId": "abc-123",
      "speFileId": "spe-456",
      "name": "Master Service Agreement - Acme Corp.pdf",
      "documentType": "Contract",
      "fileType": "pdf",
      "similarity": 0.87,
      "keywordScore": 0.72,
      "combinedScore": 0.82,
      "highlights": [
        "...payment terms shall be net 30 days from invoice date...",
        "...conditions of payment include..."
      ],
      "matterId": "matter-789",
      "matterName": "Acme Corp Litigation",
      "fileUrl": "https://...",
      "recordUrl": "https://...",
      "createdOn": "2024-06-15T10:30:00Z",
      "modifiedOn": "2024-08-20T14:45:00Z"
    }
  ],
  "metadata": {
    "totalResults": 127,
    "returnedResults": 50,
    "searchDuration": 245,
    "queryVector": null,
    "appliedFilters": {
      "documentTypes": ["Contract", "Agreement"],
      "dateRange": { "from": "2024-01-01", "to": "2024-12-31" }
    }
  }
}
```

### Endpoint: Count Only

```
POST /api/ai/search/semantic/count
```

Same request body, returns only:

```json
{
  "count": 127,
  "appliedFilters": { ... }
}
```

---

## Component Design

### SemanticSearchService

```csharp
public class SemanticSearchService : ISemanticSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IAiSearchClientFactory _searchClientFactory;
    private readonly IDataverseService _dataverseService;
    private readonly IEnumerable<IQueryPreprocessor> _preprocessors;
    private readonly IEnumerable<IResultPostprocessor> _postprocessors;

    public async Task<SemanticSearchResponse> SearchAsync(
        SemanticSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Preprocess query (extensibility hook)
        var processedQuery = await PreprocessQueryAsync(request, cancellationToken);

        // 2. Embed query
        var queryVector = await _embeddingService.EmbedAsync(
            processedQuery.Query, cancellationToken);

        // 3. Build filters
        var filter = SearchFilterBuilder.Build(request.Filters, request.Scope, request.ScopeId);

        // 4. Execute hybrid search
        var searchResults = await ExecuteHybridSearchAsync(
            queryVector, processedQuery.Query, filter, request.Options, cancellationToken);

        // 5. Postprocess results (extensibility hook)
        var enrichedResults = await PostprocessResultsAsync(
            searchResults, request, cancellationToken);

        return enrichedResults;
    }
}
```

### Extensibility Interfaces

```csharp
/// <summary>
/// Hook for query preprocessing (future: LLM-based rewriting, entity extraction)
/// </summary>
public interface IQueryPreprocessor
{
    int Order { get; }
    Task<ProcessedQuery> ProcessAsync(
        SemanticSearchRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Hook for result postprocessing (future: reranking, explanations)
/// </summary>
public interface IResultPostprocessor
{
    int Order { get; }
    Task<IReadOnlyList<SearchResult>> ProcessAsync(
        IReadOnlyList<SearchResult> results,
        SemanticSearchRequest request,
        CancellationToken cancellationToken);
}
```

### SearchFilterBuilder

```csharp
public static class SearchFilterBuilder
{
    public static string Build(
        SearchFilters? filters,
        SearchScope scope,
        string? scopeId,
        IReadOnlyList<string>? documentIds = null)
    {
        var conditions = new List<string>();

        // Scope filters
        if (scope == SearchScope.Matter && !string.IsNullOrEmpty(scopeId))
            conditions.Add($"matterId eq '{scopeId}'");

        if (scope == SearchScope.DocumentIds && documentIds?.Count > 0)
            conditions.Add($"search.in(documentId, '{string.Join(",", documentIds)}')");

        // Content filters
        if (filters?.DocumentTypes?.Count > 0)
            conditions.Add($"search.in(documentType, '{string.Join(",", filters.DocumentTypes)}')");

        if (filters?.FileTypes?.Count > 0)
            conditions.Add($"search.in(fileType, '{string.Join(",", filters.FileTypes)}')");

        // Date range filters
        if (filters?.DateRange != null)
        {
            var field = filters.DateRange.Field ?? "createdOn";
            if (!string.IsNullOrEmpty(filters.DateRange.From))
                conditions.Add($"{field} ge {filters.DateRange.From}");
            if (!string.IsNullOrEmpty(filters.DateRange.To))
                conditions.Add($"{field} le {filters.DateRange.To}");
        }

        return conditions.Count > 0 ? string.Join(" and ", conditions) : null;
    }
}
```

### AI Tool Handler (Copilot Integration)

```csharp
public class SemanticSearchToolHandler : IAiToolHandler
{
    public string ToolName => "search_documents";

    public ToolDefinition GetDefinition() => new()
    {
        Name = ToolName,
        Description = "Search for documents using natural language. " +
                      "Use this when the user asks to find, search, or locate documents.",
        Parameters = new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string", description = "Natural language search query" },
                documentTypes = new { type = "array", items = new { type = "string" } },
                matterId = new { type = "string", description = "Optional Matter GUID to scope search" },
                limit = new { type = "integer", default = 10 }
            },
            required = new[] { "query" }
        }
    };

    public async Task<ToolResult> ExecuteAsync(
        JsonElement parameters,
        AiToolContext context,
        CancellationToken cancellationToken)
    {
        var request = MapToSearchRequest(parameters, context);
        var results = await _searchService.SearchAsync(request, cancellationToken);
        return FormatToolResponse(results);
    }
}
```

---

## Playbooks Integration Strategy

### Current Playbooks Components to Leverage

| Component | Location | Use in Semantic Search |
|-----------|----------|------------------------|
| **IEmbeddingService** | Existing | Query embedding (reuse directly) |
| **IAiSearchClientFactory** | Existing | Search client creation (reuse directly) |
| **Knowledge Scope** | Playbooks | Future: domain terminology, synonyms |
| **Skills Scope** | Playbooks | Future: intent classification |
| **Tools Scope** | Playbooks | Future: multi-index search orchestration |
| **Actions Scope** | Playbooks | Future: multi-step query refinement |

### Extensibility for Future Playbooks Integration

The `IQueryPreprocessor` and `IResultPostprocessor` interfaces are designed to allow future integration with Playbooks scopes:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     FUTURE: AGENTIC RAG INTEGRATION                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  PlaybookQueryPreprocessor : IQueryPreprocessor                      │    │
│  │                                                                      │    │
│  │  Uses:                                                               │    │
│  │  - Knowledge scope → expand query with synonyms/terminology          │    │
│  │  - Skills scope → classify intent, extract entities                  │    │
│  │  - Actions scope → orchestrate multi-step query refinement           │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  PlaybookResultPostprocessor : IResultPostprocessor                  │    │
│  │                                                                      │    │
│  │  Uses:                                                               │    │
│  │  - Tools scope → cross-encoder reranking                             │    │
│  │  - Skills scope → generate relevance explanations                    │    │
│  │  - Outcomes scope → format results for specific consumers            │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Recommended: Knowledge Scope for Search

Create a **search-specific knowledge file** in Playbooks:

```yaml
# knowledge/search-terminology.yaml
domain: legal-documents
synonyms:
  - term: "MSA"
    expansions: ["Master Service Agreement", "service agreement", "master agreement"]
  - term: "NDA"
    expansions: ["Non-Disclosure Agreement", "confidentiality agreement"]
  - term: "payment terms"
    expansions: ["payment schedule", "payment conditions", "net 30", "net 60"]

entities:
  - type: "matter"
    patterns: ["matter", "case", "project"]
  - type: "document_type"
    patterns: ["contract", "agreement", "invoice", "letter"]
```

This knowledge can be loaded by a future `PlaybookQueryPreprocessor` to expand queries with domain-specific synonyms.

---

## Data Flow Diagrams

### Repository-Wide Search

```
User → PCF Control → POST /api/ai/search/semantic
                            │
                            ▼
                    SemanticSearchService
                            │
                            ├── Embed query (Azure OpenAI)
                            ├── Build filters (none for "all" scope)
                            ├── Hybrid search (AI Search)
                            └── Enrich results (Dataverse metadata)
                            │
                            ▼
                    Response with ranked results
```

### Graph Filter Search (Document Relationship Viewer)

```
User enters search in Viewer → POST /api/ai/search/semantic
                               {
                                 scope: "documentIds",
                                 documentIds: [related doc IDs from graph]
                               }
                                      │
                                      ▼
                              SemanticSearchService
                                      │
                                      ├── Embed query
                                      ├── Build filter with documentIds
                                      ├── Search ONLY within those docs
                                      └── Return ranked subset
```

### Copilot Integration

```
User: "Find contracts about payment terms"
            │
            ▼
    AI Tool Orchestrator
            │
            ├── Tool: search_documents
            │         │
            │         ▼
            │   SemanticSearchToolHandler
            │         │
            │         ▼
            │   SemanticSearchService.SearchAsync()
            │         │
            │         ▼
            │   Return results to orchestrator
            │
            ▼
    Format response for chat
```

---

## Dependencies

### Existing Components (Reuse)

| Component | Package/Location | Status |
|-----------|------------------|--------|
| `IEmbeddingService` | `Sprk.Bff.Api.Services.Ai` | Exists |
| `IAiSearchClientFactory` | `Sprk.Bff.Api.Services.Ai` | Exists |
| `IDataverseService` | `Sprk.Bff.Api.Services` | Exists |
| Azure AI Search index | `documents-v2` | Exists |
| Azure OpenAI embeddings | `text-embedding-3-large` | Exists |

### New Components (Create)

| Component | Responsibility |
|-----------|---------------|
| `SemanticSearchService` | Core search orchestration |
| `SemanticSearchEndpoints` | API endpoints |
| `SearchFilterBuilder` | Filter construction |
| `SemanticSearchToolHandler` | Copilot AI Tool |
| `IQueryPreprocessor` | Extensibility interface |
| `IResultPostprocessor` | Extensibility interface |

---

## Non-Functional Requirements

### Performance

| Metric | Target | Rationale |
|--------|--------|-----------|
| Search latency (p50) | < 500ms | User expectation for search |
| Search latency (p95) | < 1000ms | Account for embedding + search |
| Embedding latency | < 200ms | Azure OpenAI SLA |
| Concurrent searches | 50 | Expected peak load |

### Scalability

- Search scales with Azure AI Search tier
- Embedding scales with Azure OpenAI quota
- No local caching of embeddings (stateless)

### Security

- All endpoints require Bearer token authentication
- Tenant isolation via filter builder
- No PII in logs (document IDs only)

---

## Testing Strategy

| Test Type | Scope | Tools |
|-----------|-------|-------|
| Unit tests | Service logic, filter builder | xUnit, Moq |
| Integration tests | End-to-end search flow | TestContainers, real AI Search |
| Performance tests | Latency under load | k6, Application Insights |

---

## Success Criteria

- [ ] `POST /api/ai/search/semantic` returns relevant results
- [ ] Hybrid search outperforms pure vector on test queries
- [ ] Filters correctly scope results (Matter, Doc Type, Date)
- [ ] `search_documents` AI Tool works in Copilot
- [ ] Extensibility hooks allow future preprocessor registration
- [ ] Document Relationship Viewer can use API for graph filtering
- [ ] Search latency < 1s p95

---

## Future Enhancements (Agentic RAG Project)

| Enhancement | Mechanism | Playbooks Scope |
|-------------|-----------|-----------------|
| Query rewriting | `PlaybookQueryPreprocessor` | Actions + Skills |
| Entity extraction | `PlaybookQueryPreprocessor` | Knowledge + Skills |
| Auto-filter inference | `PlaybookQueryPreprocessor` | Skills |
| Cross-encoder reranking | `PlaybookResultPostprocessor` | Tools |
| Relevance explanations | `PlaybookResultPostprocessor` | Outcomes |
| Conversational refinement | Multi-turn orchestration | Actions |

---

## ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-001 | Minimal API | Endpoint filters, no controllers |
| ADR-013 | AI Tool Framework | `SemanticSearchToolHandler` implements `IAiToolHandler` |
| ADR-010 | DI Minimalism | Single `SemanticSearchService`, interfaces for extension |

---

## Open Questions

1. **Hybrid weight tuning**: What ratio of vector vs keyword scoring works best for legal documents?
2. **Index updates**: Should search wait for latest indexing or accept eventual consistency?
3. **Result caching**: Should we cache search results for repeated queries?

---

## Appendix: Related Projects

| Project | Relationship |
|---------|--------------|
| `ai-semantic-search-ui-r2` | Consumes this API, provides PCF control |
| `ai-document-relationship-visuals` | Uses `/search/semantic` for graph filtering |
| Future: Agentic RAG | Extends via `IQueryPreprocessor`, `IResultPostprocessor` |
