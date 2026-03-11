# AI Document Relationship Visuals

> **Last Updated**: 2026-03-10
>
> **Status**: In Progress

## Overview

Enhance the DocumentRelationshipViewer Code Page with a Related Document Count card on the Document Main Form, CSV export, grid enhancements, and a standardized graph visualization pattern using `@xyflow/react` + synchronous `d3-force` pre-computation. This project also extracts shared graph infrastructure (`useForceSimulation` hook, `RelationshipCountCard` component) into `@spaarke/ui-components`.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation phases and WBS |
| [Task Index](./tasks/TASK-INDEX.md) | Task status and dependencies |
| [Specification](./spec.md) | AI-optimized requirements |
| [Design Document](./design.md) | Original design (v3.0) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Owner** | Product Team |

## Problem Statement

Users opening a Document record have no indication of how many semantically related documents exist — they must open the full viewer just to check. The DocumentRelationshipViewer graph shows a "Calculating layout..." spinner for 500-1000ms on every load due to async tick-by-tick d3-force simulation. There is no export capability for compliance reporting, and the codebase has three separate graph visualization implementations with different rendering approaches, creating inconsistent UX.

## Solution Summary

Deliver a `RelatedDocumentCount` PCF control that shows document count on form load (~50-200ms via BFF API fast path), with drill-through to the full viewer dialog. Standardize on `@xyflow/react` + synchronous d3-force pre-computation for instant graph rendering. Extract a shared `useForceSimulation` hook and `RelationshipCountCard` component into `@spaarke/ui-components`. Add CSV export and quick search to the Code Page Grid view.

## Graduation Criteria

The project is considered **complete** when:

- [ ] RelatedDocumentCount PCF renders count on Document main form within 200ms of form load
- [ ] Clicking count card opens FindSimilarDialog with full DocumentRelationshipViewer
- [ ] Graph renders instantly on dialog open — no "Calculating layout..." spinner
- [ ] CSV export downloads filtered relationship data from Grid view
- [ ] Quick search filters grid rows by document name
- [ ] `useForceSimulation` hook builds cleanly in `@spaarke/ui-components` (hub-spoke + peer-mesh)
- [ ] `RelationshipCountCard` builds cleanly in `@spaarke/ui-components`
- [ ] Shared components have 90%+ test coverage
- [ ] All components render correctly in dark mode
- [ ] All existing functionality (Graph, Grid, Filters) continues working

## Scope

### In Scope

- RelatedDocumentCount PCF control for Document Main Form
- RelationshipCountCard shared component in `@spaarke/ui-components`
- `useForceSimulation` shared hook (sync d3-force, hub-spoke + peer-mesh)
- BFF API `countOnly` query parameter on visualization endpoint
- CSV export from Grid view
- Quick search filter in Grid view toolbar
- Migrate Code Page graph to shared `useForceSimulation`

### Out of Scope

- SemanticSearch Code Page migration to @xyflow (future project)
- Refactoring existing DocumentRelationshipViewer PCF (duplicated codebase remains as-is)
- Inline editing in Grid view
- Multi-select export
- Real-time updates
- Mobile-optimized layouts

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Standardize on `@xyflow/react` + sync d3-force | Rich React nodes, built-in interactions, instant layout | — |
| Sync pre-computation over async tick-by-tick | Eliminates layout spinner; proven pattern in SemanticSearch | — |
| Shared hook in `@spaarke/ui-components` | Reuse across Code Pages; React 16 compatible for PCF | ADR-012 |
| `countOnly` BFF API fast path | Separate fast count (~50-200ms) from full graph (~300-800ms) | ADR-001 |
| PCF for count card, Code Page for viewer | Field-bound count on form; standalone dialog for full viewer | ADR-006 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `countOnly` API requires BFF changes | Med | Med | Fall back to `?limit=1` initially |
| Sync pre-computation slow for 100+ nodes | Low | Low | Cap at 300 ticks; ~100ms acceptable |
| `useForceSimulation` React 16 compatibility | High | Low | Uses only `useMemo` — no React 18+ APIs |
| Shared lib build breaks from new components | Med | Low | Test barrel + deep import paths before merge |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| DocumentRelationshipViewer Code Page | Internal | Deployed | Existing — will be modified |
| FindSimilarDialog in `@spaarke/ui-components` | Internal | Available | Existing shared component |
| BFF API visualization endpoint | Internal | Deployed | Needs `countOnly` enhancement |
| `@spaarke/auth` | Internal | Available | MSAL authentication |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
