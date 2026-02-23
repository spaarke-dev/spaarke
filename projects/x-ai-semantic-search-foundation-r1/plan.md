# Project Plan: AI Semantic Search Foundation

> **Last Updated**: 2026-01-20
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Establish foundational API infrastructure for AI-powered semantic search, enabling hybrid vector+keyword search across Spaarke documents with entity-scoped filtering and Copilot integration.

**Scope**:
- Hybrid Search API with RRF fusion
- Entity-agnostic scoping (Matter, Project, Invoice, Account, Contact)
- Filter Builder for documentTypes, fileTypes, tags, dateRange
- AI Tool Handler for Copilot integration
- Index schema extension for parent entity fields
- Extensibility interfaces for future agentic RAG

**Estimated Effort**: 8-12 days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: Use Minimal API patterns; no Azure Functions
- **ADR-008**: Use endpoint filters for authorization (not global middleware)
- **ADR-010**: DI minimalism - ≤15 non-framework registrations; register concretes
- **ADR-013**: Extend BFF API for AI; apply rate limiting to all AI endpoints
- **ADR-016**: Bounded concurrency for upstream AI calls; clear 429/503 responses
- **ADR-019**: Return ProblemDetails for all errors with correlation ID

**From Spec**:
- Security trimming via scope-based authorization (tenant isolation + resource authorization)
- `scope=all` NOT supported in R1 (returns 400)
- Only `combinedScore` for R1; `similarity` and `keywordScore` are null
- Embedding failure falls back to keyword-only with warning

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Use RRF for hybrid scoring | Azure AI Search default, proven approach | No custom weight tuning needed |
| Entity-agnostic scoping | Supports all entity types with documents | Single API serves Matter, Project, Invoice, etc. |
| No result caching R1 | Simpler, always fresh | May add in R2 based on load testing |
| Index schema extension | Required for entity scoping | New fields: parentEntityType, parentEntityId, parentEntityName |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-001-minimal-api.md` - Minimal API patterns
- `.claude/adr/ADR-008-endpoint-filters.md` - Authorization filters
- `.claude/adr/ADR-010-di-minimalism.md` - DI patterns
- `.claude/adr/ADR-013-ai-architecture.md` - AI architecture
- `.claude/adr/ADR-016-ai-rate-limits.md` - Rate limiting
- `.claude/adr/ADR-019-problemdetails.md` - Error handling

**Knowledge Articles**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` - AI architecture overview
- `docs/guides/RAG-ARCHITECTURE.md` - RAG patterns
- `docs/guides/RAG-CONFIGURATION.md` - RAG configuration
- `infrastructure/ai-search/spaarke-knowledge-index-v2.json` - Current index schema

**Patterns**:
- `.claude/patterns/api/endpoint-definition.md` - Endpoint patterns
- `.claude/patterns/api/endpoint-filters.md` - Auth filters
- `.claude/patterns/api/error-handling.md` - Error handling
- `.claude/patterns/api/service-registration.md` - DI registration
- `.claude/patterns/auth/uac-access-control.md` - UAC patterns

**Reusable Code**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` - Existing search implementation
- `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` - Search interface pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` - Indexing patterns
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs` - Tool registration

**Scripts**:
- `scripts/Test-SdapBffApi.ps1` - API testing

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Index Schema & Infrastructure (2 days)
└─ Extend index schema with parent entity fields
└─ Update indexing pipeline
└─ Verify index configuration

Phase 2: Core Search Service (3-4 days)
└─ SemanticSearchService implementation
└─ SearchFilterBuilder implementation
└─ Hybrid search execution (RRF, vectorOnly, keywordOnly)
└─ Embedding fallback handling

Phase 3: API Endpoints & Authorization (2 days)
└─ SemanticSearchEndpoints (search + count)
└─ SemanticSearchAuthorizationFilter
└─ Request validation
└─ ProblemDetails error handling

Phase 4: AI Tool Integration (1-2 days)
└─ SemanticSearchToolHandler (IAiToolHandler)
└─ Tool registration
└─ Copilot integration testing

Phase 5: Testing & Validation (2 days)
└─ Unit tests for all components
└─ Integration tests
└─ Performance validation
└─ Security testing
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (needs index schema)
- Phase 3 BLOCKED BY Phase 2 (needs service)
- Phase 4 BLOCKED BY Phase 3 (needs endpoints working)

**High-Risk Items:**
- Index schema migration - Mitigation: Dev uses new docs only
- Embedding service availability - Mitigation: Fallback to keyword-only

---

## 4. Phase Breakdown

### Phase 1: Index Schema & Infrastructure

**Objectives:**
1. Extend Azure AI Search index with parent entity fields
2. Update document indexing pipeline to populate new fields
3. Verify index configuration supports hybrid search

**Deliverables:**
- [ ] Index schema definition with `parentEntityType`, `parentEntityId`, `parentEntityName`
- [ ] Updated `KnowledgeDocument.cs` model with new fields
- [ ] Updated indexing service to populate parent entity fields
- [ ] Verification script/test for index schema

**Critical Tasks:**
- Index schema extension MUST BE FIRST - all other work depends on it

**Inputs**:
- `infrastructure/ai-search/spaarke-knowledge-index-v2.json`
- `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs`

**Outputs**:
- Updated index definition
- Updated model classes
- Updated indexing code

---

### Phase 2: Core Search Service

**Objectives:**
1. Implement `SemanticSearchService` with hybrid search
2. Implement `SearchFilterBuilder` for OData filter construction
3. Support all three hybrid modes (rrf, vectorOnly, keywordOnly)
4. Handle embedding failures with graceful fallback

**Deliverables:**
- [ ] `ISemanticSearchService` interface
- [ ] `SemanticSearchService` implementation
- [ ] `SearchFilterBuilder` static class
- [ ] Request/response DTOs
- [ ] `IQueryPreprocessor` interface (no-op for R1)
- [ ] `IResultPostprocessor` interface (no-op for R1)
- [ ] Result enrichment (batch Dataverse lookup)

**Critical Tasks:**
- `SearchFilterBuilder` required before service can query
- Embedding integration required for vector search

**Inputs**:
- `IEmbeddingService` (existing)
- `IAiSearchClientFactory` (existing)
- `IDataverseService` (existing)

**Outputs**:
- Search service ready for endpoint wiring

---

### Phase 3: API Endpoints & Authorization

**Objectives:**
1. Create Minimal API endpoints for search and count
2. Implement authorization filter for scope validation
3. Add request validation with clear error codes
4. Return ProblemDetails for all errors

**Deliverables:**
- [ ] `SemanticSearchEndpoints.cs` with MapGroup registration
- [ ] `POST /api/ai/search/semantic` endpoint
- [ ] `POST /api/ai/search/semantic/count` endpoint
- [ ] `SemanticSearchAuthorizationFilter` (endpoint filter)
- [ ] Request validation (query length, scope, limits)
- [ ] Response with `warnings[]` array

**Critical Tasks:**
- Authorization filter MUST validate scope before search executes

**Inputs**:
- `SemanticSearchService` from Phase 2
- UAC authorization patterns

**Outputs**:
- Working API endpoints
- Authorization enforced

---

### Phase 4: AI Tool Integration

**Objectives:**
1. Create `SemanticSearchToolHandler` implementing `IAiToolHandler`
2. Register tool with tool handler registry
3. Test Copilot integration

**Deliverables:**
- [ ] `SemanticSearchToolHandler.cs`
- [ ] Tool definition (name, description, parameters)
- [ ] Tool registration in DI
- [ ] Copilot integration verification

**Inputs**:
- `IAiToolHandler` interface
- `ToolHandlerRegistry` pattern
- Working search endpoints

**Outputs**:
- `search_documents` tool available in Copilot

---

### Phase 5: Testing & Validation

**Objectives:**
1. Comprehensive unit test coverage
2. Integration tests for end-to-end flows
3. Performance validation (latency targets)
4. Security testing (authorization, tenant isolation)

**Deliverables:**
- [ ] Unit tests for `SemanticSearchService`
- [ ] Unit tests for `SearchFilterBuilder`
- [ ] Unit tests for request validation
- [ ] Integration tests for search flow
- [ ] Integration tests for authorization
- [ ] Performance test (50 concurrent, <1s p95)
- [ ] Security test cases

**Inputs**:
- All components from Phases 1-4

**Outputs**:
- All tests passing
- Performance verified
- Security verified

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Search | GA | Low | Existing infrastructure |
| Azure OpenAI | GA | Medium | Rate limits - bounded concurrency, fallback |
| Dataverse API | GA | Low | Existing integration |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `IEmbeddingService` | `src/server/api/.../Services/Ai/` | Production |
| `IAiSearchClientFactory` | `src/server/api/.../Services/Ai/` | Production |
| `IDataverseService` | `src/server/api/.../Services/` | Production |
| `IAiToolHandler` | `src/server/api/.../Services/Ai/` | Production |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- `SemanticSearchService` - all search modes, fallback handling
- `SearchFilterBuilder` - all filter combinations
- Request validation - all error codes
- Authorization filter - scope validation

**Integration Tests**:
- End-to-end search with real Azure AI Search
- Authorization enforcement
- Embedding fallback behavior

**E2E Tests**:
- Copilot tool invocation
- Complete search flow from API to results

**Performance Tests**:
- 50 concurrent searches
- p50 < 500ms, p95 < 1000ms

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] Index schema includes `parentEntityType`, `parentEntityId`, `parentEntityName`
- [ ] New documents indexed with parent entity data

**Phase 2:**
- [ ] Hybrid search returns ranked results
- [ ] Embedding failure returns keyword-only results with warning

**Phase 3:**
- [ ] API returns 400 for invalid requests with stable error codes
- [ ] Authorization filter validates scope before search

**Phase 4:**
- [ ] `search_documents` tool callable from Copilot

**Phase 5:**
- [ ] All tests pass
- [ ] p95 latency < 1000ms

### Business Acceptance

- [ ] Users can search documents using natural language within an entity scope
- [ ] Copilot can answer "find documents about X" queries
- [ ] No unauthorized document access possible

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Index schema migration complexity | Low | Medium | Dev: new docs only |
| R2 | Azure OpenAI rate limits | Medium | Medium | Bounded concurrency, fallback |
| R3 | Entity authorization edge cases | Medium | High | Thorough security testing |
| R4 | Performance under load | Low | Medium | Load testing, caching consideration for R2 |

---

## 9. Next Steps

1. **Review this plan.md** for completeness
2. **Run** `/task-create ai-semantic-search-foundation-r1` to generate task files
3. **Begin** Phase 1: Index Schema & Infrastructure

---

**Status**: Ready for Tasks
**Next Action**: Generate task files via project-pipeline

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
