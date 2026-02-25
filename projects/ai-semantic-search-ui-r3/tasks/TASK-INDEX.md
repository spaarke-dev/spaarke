# Task Index — AI Semantic Search UI R3

> **Last Updated**: 2026-02-24
> **Total Tasks**: 54
> **Status Legend**: 🔲 Pending | 🔄 In Progress | ✅ Complete | 🚫 Blocked

---

## Phase 1: Foundation & Investigation

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 001 | Scaffold SemanticSearch Code Page Project | 🔲 | 3h | — | — |
| 002 | Investigate spaarke-records-index Field Coverage | 🔲 | 2h | — | phase1-spikes |
| 003 | Investigate Universal DatasetGrid Headless Adapter | 🔲 | 2h | — | phase1-spikes |
| 004 | Investigate sprk_gridconfiguration Schema | 🔲 | 1h | — | phase1-spikes |

## Phase 2: BFF API Backend

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 010 | Enable scope=all in POST /api/ai/search | 🔲 | 3h | — | phase2-enhance |
| 011 | Add entityTypes Filter to POST /api/ai/search | 🔲 | 2h | — | phase2-enhance |
| 012 | Create Records Search Models | 🔲 | 1.5h | — | — |
| 013 | Create RecordSearchService | 🔲 | 4h | 012 | — |
| 014 | Create POST /api/ai/search/records Endpoint | 🔲 | 2h | 012, 013 | — |
| 015 | Unit Tests — Enhanced Search + Records Search | 🔲 | 4h | 010-014 | phase2-tests |
| 016 | Integration Tests — Search Endpoints | 🔲 | 3h | 010, 011, 014 | phase2-tests |
| 017 | Deploy BFF API to Dev | 🔲 | 1h | 015, 016 | — |

## Phase 3: Code Page Core

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 020 | Code Page Entry Point (index.tsx, App.tsx, Theme) | 🔲 | 3h | 001 | — |
| 021 | MSAL Authentication Service | 🔲 | 2h | 020 | — |
| 022 | API Service Clients (Search + Records) | 🔲 | 3h | 021 | — |
| 023 | TypeScript Type Definitions | 🔲 | 2h | 001 | — |
| 024 | Search Filter Pane | 🔲 | 3h | 020, 023 | phase3-filters |
| 025 | FilterDropdown Component | 🔲 | 2h | 023 | phase3-filters |
| 026 | DateRangeFilter Component | 🔲 | 2h | 023 | phase3-filters |
| 027 | Search Domain Tabs | 🔲 | 2h | 020, 023 | — |
| 028 | useSemanticSearch Hook | 🔲 | 3h | 022, 023 | phase3-hooks |
| 029 | useRecordSearch Hook | 🔲 | 2h | 022, 023 | phase3-hooks |

## Phase 4: Grid & Graph Views

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 030 | SearchResultsGrid (Universal DatasetGrid) | 🔲 | 4h | 003, 023 | phase4-grid |
| 031 | Domain-Specific Grid Columns | 🔲 | 3h | 030, 002 | phase4-grid |
| 032 | SearchResultsGraph (@xyflow/react Canvas) | 🔲 | 3h | 020, 023 | phase4-graph |
| 033 | ClusterNode Component | 🔲 | 3h | 032, 023 | phase4-graph |
| 034 | RecordNode Component | 🔲 | 2h | 032, 023 | phase4-graph |
| 035 | useClusterLayout Hook (d3-force) | 🔲 | 5h | 023 | phase4-graph |
| 036 | Graph Drill-Down (Expand/Collapse) | 🔲 | 4h | 032-035 | — |
| 037 | View Toggle Toolbar (Grid/Graph) | 🔲 | 2h | 030, 032 | — |
| 038 | useFilterOptions Hook (Dataverse Metadata) | 🔲 | 2h | 023 | — |

## Phase 5: Interactive Features

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 040 | Search Command Bar (Selection-Aware) | 🔲 | 3h | 020, 023 | phase5-features |
| 041 | Saved Search Selector (ViewSelector Pattern) | 🔲 | 4h | 004, 023 | phase5-features |
| 042 | useSavedSearches Hook (CRUD) | 🔲 | 3h | 004, 023 | phase5-features |
| 043 | Entity Record Dialog (Multi-Entity) | 🔲 | 1.5h | 020 | phase5-features |
| 044 | useDocumentActions Hook | 🔲 | 3h | 022, 023 | phase5-features |
| 045 | URL Parameter Support | 🔲 | 2h | 020, 027, 041 | — |
| 046 | Status Bar | 🔲 | 1h | 020 | phase5-features |
| 047 | Wire Up Full Search Flow | 🔲 | 4h | 024-046 | — |

## Phase 6: DocumentRelationshipViewer Migration

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 050 | Analyze RelationshipGrid | 🔲 | 1.5h | — | — |
| 051 | Migrate RelationshipGrid to Universal DatasetGrid | 🔲 | 4h | 050 | — |
| 052 | Test DocRelViewer Grid Migration | 🔲 | 2h | 051 | — |

## Phase 7: Testing & Quality

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 060 | Unit Tests — Search Hooks | 🔲 | 5h | 028, 029, 035, 042, 044, 038 | phase7-unit |
| 061 | Unit Tests — API Services | 🔲 | 3h | 021, 022 | phase7-unit |
| 062 | Unit Tests — UI Components | 🔲 | 5h | 025-027, 033, 034, 040, 041 | phase7-unit |
| 063 | Integration Tests — Search Flow | 🔲 | 4h | 047 | phase7-integration |
| 064 | Dark Mode Validation | 🔲 | 2h | 047 | phase7-quality |
| 065 | Accessibility Validation | 🔲 | 2h | 047 | phase7-quality |
| 066 | Bundle Size Analysis | 🔲 | 1.5h | 047 | — |
| 067 | Bundle Size Optimization | 🔲 | 3h | 066 | — |

## Phase 8: Deployment & Wrap-up

| # | Task | Status | Est. | Dependencies | Parallel Group |
|---|------|--------|------|-------------|----------------|
| 070 | Build and Deploy Code Page | 🔲 | 1.5h | 067 | — |
| 071 | Sitemap Entry and Command Bar Button | 🔲 | 2h | 070 | — |
| 072 | Final BFF API Deployment | 🔲 | 1h | 070 | — |
| 073 | End-to-End Validation in Dataverse | 🔲 | 3h | 070, 071, 072 | — |
| 074 | Code Review and ADR Check | 🔲 | 2h | 073 | — |
| 080 | Project Wrap-Up | 🔲 | 1h | 074 | — |

---

## Parallel Execution Groups

Tasks in the same parallel group can run simultaneously when their dependencies are met.

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| phase1-spikes | 002, 003, 004 | 001 complete (or none) | Independent investigation tasks |
| phase2-enhance | 010, 011 | — | Independent endpoint enhancements (watch shared files) |
| phase2-tests | 015, 016 | 010-014 complete | Unit and integration tests |
| phase3-filters | 024, 025, 026 | 023 complete | Independent filter components |
| phase3-hooks | 028, 029 | 022, 023 complete | Independent search hooks |
| phase4-grid | 030, 031 | 003 spike + 023 complete | Grid view components |
| phase4-graph | 032, 033, 034, 035 | 020, 023 complete | Graph view components |
| phase5-features | 040, 041, 042, 043, 044, 046 | 020, 023 complete | Independent interactive features |
| phase7-unit | 060, 061, 062 | Respective components complete | Independent test suites |
| phase7-quality | 064, 065 | 047 complete | Independent quality checks |

---

## Critical Path

```
001 (scaffold) → 020 (entry point) → 021 (auth) → 022 (API services) → 028/029 (hooks)
                                                                            ↓
                                                         030/032 (grid/graph) → 047 (wire-up)
                                                                                    ↓
                                                                         066/067 (optimize) → 070 (deploy)
                                                                                                   ↓
                                                                                        073 (e2e) → 074 (review) → 080 (wrap-up)
```

**Longest path**: 001 → 020 → 021 → 022 → 028 → 047 → 066 → 067 → 070 → 073 → 074 → 080 (12 tasks)

---

## Effort Summary

| Phase | Tasks | Estimated Hours |
|-------|-------|----------------|
| Phase 1: Foundation & Investigation | 4 | 8h |
| Phase 2: BFF API Backend | 8 | 20.5h |
| Phase 3: Code Page Core | 10 | 24h |
| Phase 4: Grid & Graph Views | 9 | 28h |
| Phase 5: Interactive Features | 8 | 21.5h |
| Phase 6: DocRelViewer Migration | 3 | 7.5h |
| Phase 7: Testing & Quality | 8 | 25.5h |
| Phase 8: Deployment & Wrap-up | 6 | 10.5h |
| **Total** | **56** | **~145.5h** |

---

## High-Risk Tasks

| Task | Risk | Mitigation |
|------|------|------------|
| 002 | Records index may lack required fields | Spike before implementation; adjust grid columns |
| 003 | Universal DatasetGrid may not support headless data | Spike before implementation; fallback to custom grid |
| 035 | d3-force clustering performance with 100+ nodes | Limit to top 100 results in graph view |
| 067 | Bundle size may exceed 3s load target | Tree-shake, lazy-load graph, code-split |

---

*Task index for AI Semantic Search UI R3. Updated by task-execute skill during execution.*
