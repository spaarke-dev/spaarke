# Task Index: AI Search & Visualization Module

> **Project**: ai-azure-search-module
> **Last Updated**: 2026-01-08
> **Total Tasks**: 28

---

## Quick Status

| Phase | Status | Progress |
|-------|--------|----------|
| Phase 1: Core Infrastructure | Not Started | 0/8 |
| Phase 2: PCF Control Development | Not Started | 0/10 |
| Phase 3: Integration & Ribbon | Not Started | 0/5 |
| Phase 4: Polish & Documentation | Not Started | 0/4 |
| Wrap-up | Not Started | 0/1 |

---

## All Tasks

| ID | Title | Phase | Status | Dependencies | Rigor |
|----|-------|-------|--------|--------------|-------|
| 001 | Update Azure AI Search index schema | 1 | 🔲 | none | FULL |
| 002 | Create IVisualizationService interface | 1 | 🔲 | none | FULL |
| 003 | Implement VisualizationService | 1 | 🔲 | 001, 002 | FULL |
| 004 | Create VisualizationAuthorizationFilter | 1 | 🔲 | none | FULL |
| 005 | Create VisualizationEndpoints | 1 | 🔲 | 003, 004 | FULL |
| 006 | Backfill existing documents | 1 | 🔲 | 001 | STANDARD |
| 007 | Unit tests for VisualizationService | 1 | 🔲 | 003 | STANDARD |
| 008 | Deploy Phase 1 API | 1 | 🔲 | 005, 007 | STANDARD |
| 010 | Scaffold DocumentRelationshipViewer PCF | 2 | 🔲 | none | FULL |
| 011 | Integrate React Flow with d3-force | 2 | 🔲 | 010 | FULL |
| 012 | Implement DocumentNode component | 2 | 🔲 | 011 | FULL |
| 013 | Implement DocumentEdge component | 2 | 🔲 | 011 | FULL |
| 014 | Implement control panel | 2 | 🔲 | 012, 013 | FULL |
| 015 | Implement node action bar | 2 | 🔲 | 012 | FULL |
| 016 | Implement full-screen modal | 2 | 🔲 | 014, 015 | FULL |
| 017 | Component tests for PCF control | 2 | 🔲 | 016 | STANDARD |
| 018 | Integration tests with Azure AI Search | 2 | 🔲 | 005, 016 | STANDARD |
| 019 | Deploy Phase 2 PCF | 2 | 🔲 | 017, 018 | STANDARD |
| 020 | Register PCF on sprk_document form | 3 | 🔲 | 019 | FULL |
| 021 | Create ribbon button command | 3 | 🔲 | 020 | FULL |
| 022 | Implement modal dialog launcher | 3 | 🔲 | 021 | FULL |
| 023 | End-to-end testing in Dataverse | 3 | 🔲 | 022 | STANDARD |
| 024 | Deploy Phase 3 ribbon | 3 | 🔲 | 023 | STANDARD |
| 030 | Implement export functionality | 4 | 🔲 | 024 | FULL |
| 031 | Performance optimization | 4 | 🔲 | 024 | STANDARD |
| 032 | Accessibility audit and fixes | 4 | 🔲 | 030, 031 | STANDARD |
| 033 | Create user documentation | 4 | 🔲 | 032 | MINIMAL |
| 090 | Project wrap-up | 5 | 🔲 | 033 | FULL |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ⏸️ | Blocked |
| ✅ | Completed |
| ⏭️ | Deferred |

---

## Critical Path

```
001 (index schema) ──┬──→ 003 (service) ──→ 005 (endpoint) ──→ 008 (deploy API)
                     │
002 (interface) ─────┘
                     │
004 (auth filter) ───┘

010 (PCF scaffold) ──→ 011 (React Flow) ──→ 012/013 (nodes/edges) ──→ 014-016 (UI) ──→ 019 (deploy PCF)

019 (PCF deployed) ──→ 020-024 (ribbon integration)

024 (ribbon deployed) ──→ 030-033 (polish) ──→ 090 (wrap-up)
```

---

## Phase Details

### Phase 1: Core Infrastructure (Days 1-5)
**Goal**: Working API endpoint with document-level embeddings

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 001 - Update Azure AI Search index schema | 2h | none |
| 002 - Create IVisualizationService interface | 1h | none |
| 003 - Implement VisualizationService | 4h | 001, 002 |
| 004 - Create VisualizationAuthorizationFilter | 2h | none |
| 005 - Create VisualizationEndpoints | 3h | 003, 004 |
| 006 - Backfill existing documents | 3h | 001 |
| 007 - Unit tests for VisualizationService | 3h | 003 |
| 008 - Deploy Phase 1 API | 2h | 005, 007 |

### Phase 2: PCF Control Development (Days 6-10)
**Goal**: Working visualization control with all UI components

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 010 - Scaffold DocumentRelationshipViewer PCF | 2h | none |
| 011 - Integrate React Flow with d3-force | 4h | 010 |
| 012 - Implement DocumentNode component | 3h | 011 |
| 013 - Implement DocumentEdge component | 2h | 011 |
| 014 - Implement control panel | 3h | 012, 013 |
| 015 - Implement node action bar | 2h | 012 |
| 016 - Implement full-screen modal | 3h | 014, 015 |
| 017 - Component tests for PCF control | 3h | 016 |
| 018 - Integration tests with Azure AI Search | 3h | 005, 016 |
| 019 - Deploy Phase 2 PCF | 2h | 017, 018 |

### Phase 3: Integration & Ribbon (Days 11-13)
**Goal**: Ribbon button opens modal with full functionality

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 020 - Register PCF on sprk_document form | 2h | 019 |
| 021 - Create ribbon button command | 2h | 020 |
| 022 - Implement modal dialog launcher | 3h | 021 |
| 023 - End-to-end testing in Dataverse | 3h | 022 |
| 024 - Deploy Phase 3 ribbon | 2h | 023 |

### Phase 4: Polish & Documentation (Days 14-15)
**Goal**: Production-ready feature with documentation

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 030 - Implement export functionality | 4h | 024 |
| 031 - Performance optimization | 3h | 024 |
| 032 - Accessibility audit and fixes | 3h | 030, 031 |
| 033 - Create user documentation | 2h | 032 |

### Wrap-up
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 090 - Project wrap-up | 2h | 033 |

---

## Execution Notes

**Parallel Execution Opportunities:**
- Tasks 001, 002, 004 can run in parallel (no dependencies)
- Tasks 012, 013 can run in parallel (both depend on 011)
- Tasks 030, 031 can run in parallel (both depend on 024)

**Deployment Tasks:**
- 008: Deploy BFF API to Azure (after Phase 1)
- 019: Deploy PCF to Dataverse (after Phase 2)
- 024: Deploy ribbon customization (after Phase 3)

---

*Updated automatically by task-execute skill*
