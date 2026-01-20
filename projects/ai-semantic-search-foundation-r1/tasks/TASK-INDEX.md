# AI Semantic Search Foundation - Task Index

> **Auto-updated by task-execute skill**
> **Project**: ai-semantic-search-foundation-r1
> **Last Updated**: 2026-01-20

---

## Summary

| Metric | Value |
|--------|-------|
| Total Tasks | 18 |
| Completed | 10 |
| In Progress | 0 |
| Pending | 8 |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ⏭️ | Deferred |

---

## Task List

### Phase 1: Index Schema & Infrastructure

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 001 | Extend Azure AI Search index schema with parent entity fields | ✅ | none | FULL |
| 002 | Update KnowledgeDocument model with parent entity fields | ✅ | 001 | FULL |
| 003 | Update FileIndexingService to populate parent entity fields | ✅ | 002 | FULL |
| 004 | Verify index configuration supports hybrid search | ✅ | 001 | STANDARD |

### Phase 2: Core Search Service

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 010 | Create SemanticSearch request/response DTOs | ✅ | 003 | FULL |
| 011 | Create SearchFilterBuilder for OData filter construction | ✅ | 010 | FULL |
| 012 | Create ISemanticSearchService interface | ✅ | 010 | FULL |
| 013 | Implement SemanticSearchService with hybrid search | ✅ | 011, 012 | FULL |
| 014 | Implement no-op preprocessor and postprocessor for R1 | ✅ | 012 | STANDARD |
| 015 | Register SemanticSearch services in DI container | ✅ | 013, 014 | STANDARD |

### Phase 3: API Endpoints & Authorization

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 020 | Create SemanticSearchEndpoints with search and count methods | 🔲 | 015 | FULL |
| 021 | Create SemanticSearchAuthorizationFilter endpoint filter | 🔲 | 020 | FULL |
| 022 | Implement request validation with stable error codes | 🔲 | 020, 021 | FULL |

### Phase 4: AI Tool Integration

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 030 | Create SemanticSearchToolHandler for Copilot integration | 🔲 | 022 | FULL |
| 031 | Test Copilot tool integration manually | 🔲 | 030 | MINIMAL |

### Phase 5: Testing & Validation

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 040 | Unit tests for SearchFilterBuilder | 🔲 | 011 | STANDARD |
| 041 | Unit tests for SemanticSearchService | 🔲 | 013 | STANDARD |
| 042 | Unit tests for request validation filter | 🔲 | 022 | STANDARD |
| 043 | Integration tests for semantic search flow | 🔲 | 022 | STANDARD |
| 044 | Integration tests for authorization filter | 🔲 | 021 | STANDARD |
| 045 | Performance validation (latency targets) | 🔲 | 043, 044 | STANDARD |

### Phase 6: Project Wrap-up

| ID | Title | Status | Dependencies | Rigor |
|----|-------|--------|--------------|-------|
| 090 | Project wrap-up | 🔲 | all | FULL |

---

## Critical Path

```
Phase 1: Index Schema
001 → 002 → 003 → 004
         ↓
Phase 2: Core Search Service
010 → 011 ─┬→ 013 → 015 → Phase 2 Complete
012 ───────┘
014 ───────→ 015
         ↓
Phase 3: API Endpoints
020 → 021 → 022
         ↓
Phase 4: AI Tool
030 → 031
         ↓
Phase 5: Testing
040, 041, 042 (can parallel after their deps)
043 → 044 → 045
         ↓
Phase 6: Wrap-up
090
```

---

## Rigor Level Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| FULL | 10 | 001-003, 010-013, 020-022, 030, 090 |
| STANDARD | 7 | 004, 014-015, 040-045 |
| MINIMAL | 1 | 031 |

---

## Notes

- **Phase 1 is prerequisite**: Index schema must be extended before search service can be built
- **Phase 2-3 are sequential**: Service depends on DTOs, endpoints depend on service
- **Phase 4 depends on Phase 3**: Tool handler needs working endpoints
- **Phase 5 tests can partially parallel**: Unit tests can run as soon as their target is complete
- **Task 090 is mandatory final task**: Runs quality gates and cleanup

---

## Key Constraints (from spec.md)

- **R1 Scope Restriction**: `scope=all` returns 400 (not supported)
- **Entity-Agnostic**: Supports Matter, Project, Invoice, Account, Contact
- **Embedding Fallback**: On failure, fall back to keyword-only with warning
- **Scoring**: Only `combinedScore` populated; `similarity`, `keywordScore` are null
- **Performance**: p50 < 500ms, p95 < 1000ms

---

*Updated by task-execute skill as tasks progress*
