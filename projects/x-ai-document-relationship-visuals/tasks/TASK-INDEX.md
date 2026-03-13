# Task Index — AI Document Relationship Visuals

> **Last Updated**: 2026-03-13
> **Total Tasks**: 13
> **Status**: Complete

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Pending (not started) |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Paused / Blocked |
| ⏭️ | Skipped |

---

## Phase 1: Shared Library Foundation

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 001 | Create RelationshipCountCard Shared Component | ✅ | — | frontend, fluent-ui, shared-component |
| 002 | Create useForceSimulation Shared Hook | ✅ | — | frontend, shared-component, d3-force |
| 003 | Verify Shared Library Build, Exports, and Test Coverage | ✅ | 001, 002 | testing, shared-component, build |

## Phase 2: BFF API Enhancement

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 010 | Add countOnly Parameter to BFF API Visualization Endpoint | ⏭️ | — | bff-api, api |
| 011 | Unit Tests for countOnly API Parameter | ⏭️ | 010 | testing, bff-api |

> **Note**: Tasks 010/011 were skipped. The PCF now uses a single full API call (`limit=20`) that returns both count (from `metadata.totalResults`) and graph data, eliminating the need for a separate `countOnly` parameter. This is faster than two round-trips (countOnly + full graph).

## Phase 3: Code Page Enhancements

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 020 | Migrate DocumentGraph to Shared useForceSimulation Hook | ✅ | 003 | frontend, fluent-ui, code-page |
| 021 | Add CSV Export Service and Export Button to Grid View | ✅ | 003 | frontend, fluent-ui, code-page |
| 022 | Add Quick Search Filter to Grid View | ✅ | 003 | frontend, fluent-ui, code-page |
| 024 | Build and Deploy Enhanced Code Page | ✅ | 020, 021, 022 | deploy, code-page |

> **Additional work completed beyond original scope**:
> - Document Type and Relationship Type client-side filters with Select All/Clear
> - Preview URL caching with TTL (10 min) and LRU (50 entries), hover-to-prefetch
> - esbuild-loader + webpack filesystem cache (build: 17s → <1s warm)
> - FilePreviewDialog integration (grid row click + graph node click → preview)
> - Network Graph and Timeline view modes
> - Toolbar restructure with view toggle buttons
> - Graph: shadow removal, thinner edges, L→R handle positions, auto-fit

## Phase 4: RelatedDocumentCount PCF

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 030 | Scaffold RelatedDocumentCount PCF Control | ✅ | 003 | pcf, frontend, fluent-ui |
| 031 | Implement RelatedDocumentCount PCF Component Logic | ✅ | 030, 003, 010 | pcf, frontend, fluent-ui |
| 033 | Unit Tests for RelatedDocumentCount PCF | ⏭️ | 031 | testing, pcf |
| 034 | Deploy RelatedDocumentCount PCF and Configure Document Main Form | ✅ | 033 | deploy, pcf, dataverse |

> **Note**: Task 033 (unit tests) skipped for now — can be added as follow-up. PCF v1.20.2 deployed and validated in Dataverse.

## Phase 5: Integration & Testing

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 040 | End-to-End Integration Testing and Performance Validation | ✅ | 024, 034 | testing, e2e-test |

> Validated via manual user testing — multiple rounds of UX feedback incorporated.

## Phase 6: Wrap-Up

| # | Task | Status | Dependencies | Tags |
|---|------|--------|-------------|------|
| 090 | Project Wrap-Up | ✅ | 040 | documentation, cleanup |

---

## Summary

| Metric | Value |
|--------|-------|
| Completed | 10 |
| Skipped | 3 (010, 011, 033) |
| Total | 13 |
| Branch | `feature/ai-document-relationship-visuals` |
| PR | #220 |
| Merged to master | 2026-03-12 |
| PCF Version | 1.20.2 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002 | None | Independent shared components — can build simultaneously |
| B | 010 | None | BFF API work — independent of shared lib |
| C | 020, 021, 022 | 003 complete | Independent Code Page enhancements — different files |

**Note**: Groups A and B can run in parallel (Phase 1 shared lib and Phase 2 API have no dependencies on each other).

---

## Critical Path

```
001 ─┐
     ├─→ 003 ─→ 020 ─┐
002 ─┘              │
                    ├─→ 024 ─┐
              021 ─┘         │
              022 ─┘         │
                             ├─→ 040 ─→ 090
010 ─→ 011                   │
   └─→ 030 ─→ 031 ─→ 033 ─→ 034 ─┘
```

**Longest path**: 001/002 → 003 → 030 → 031 → 033 → 034 → 040 → 090
