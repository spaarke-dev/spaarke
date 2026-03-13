# Project Plan: AI Document Relationship Visuals

> **Last Updated**: 2026-03-10
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Enhance DocumentRelationshipViewer with a Related Document Count card, CSV export, grid search, and standardized graph visualization using shared `@xyflow/react` + synchronous `d3-force` infrastructure.

**Scope**:
- RelatedDocumentCount PCF + RelationshipCountCard shared component
- Shared `useForceSimulation` hook (sync d3-force, hub-spoke + peer-mesh)
- BFF API `countOnly` fast path
- Code Page enhancements (CSV export, quick search, shared hook migration)

**Estimated Effort**: ~40-60 hours across 5 phases + wrap-up

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-001**: BFF API must use Minimal API pattern; return `ProblemDetails` on errors
- **ADR-006**: PCF for form-bound controls; Code Page for standalone dialogs
- **ADR-008**: Endpoint filters for authorization, not global middleware
- **ADR-010**: DI minimalism — ≤15 non-framework registrations; register concretes
- **ADR-012**: Shared components in `@spaarke/ui-components`; callback-based props; 90%+ test coverage
- **ADR-021**: Fluent UI v9 exclusively; design tokens; dark mode required; WCAG 2.1 AA
- **ADR-022**: PCF uses React 16 platform-provided (`ReactDOM.render`); Code Page bundles React 19 (`createRoot`)

**From Spec**:
- `useForceSimulation` must use only `useMemo` (no React 18+ concurrent APIs) for PCF compatibility
- PCF must use deep imports from shared lib (`@spaarke/ui-components/dist/components/{Name}`)
- Graph must render instantly — no "Calculating layout..." spinner

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Sync d3-force pre-computation | Eliminates layout spinner; proven in SemanticSearch | Replace `useForceLayout` with `useForceSimulation` |
| `countOnly` API parameter | Separate fast count from full graph computation | New query param on existing endpoint |
| Shared hook + component extraction | Reuse across consumers; consistent behavior | New exports in `@spaarke/ui-components` |
| Hub-spoke + peer-mesh modes | DocRelViewer uses hub-spoke; future SemanticSearch uses peer-mesh | Config option on hook |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/pcf-deploy/` — Deploy RelatedDocumentCount PCF
- `.claude/skills/code-page-deploy/` — Deploy DocumentRelationshipViewer Code Page
- `.claude/skills/bff-deploy/` — Deploy BFF API after countOnly enhancement
- `.claude/skills/dataverse-deploy/` — Deploy solutions/forms
- `.claude/skills/adr-check/` — Validate ADR compliance
- `.claude/skills/code-review/` — Quality gate at step 9.5

**Knowledge Articles**:
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — Shared lib build, import patterns
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — PCF versioning (4 locations), deployment
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — BFF visualization endpoint patterns

**Patterns**:
- `.claude/patterns/pcf/control-initialization.md` — PCF lifecycle, React 16 pattern
- `.claude/patterns/pcf/theme-management.md` — Dark mode, theme tokens
- `.claude/patterns/pcf/dialog-patterns.md` — Opening Code Page dialogs from PCF
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint with MapGroup
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/testing/unit-test-structure.md` — Jest test structure

**Constraints**:
- `.claude/constraints/pcf.md` — React 16, deep imports, featureconfig.json
- `.claude/constraints/api.md` — Minimal API, endpoint filters, ProblemDetails
- `.claude/constraints/testing.md` — Coverage requirements (90%+ shared, 80%+ PCF)

**Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — PCF deployment
- `scripts/Test-SdapBffApi.ps1` — BFF API endpoint testing

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Shared Library Foundation
└─ RelationshipCountCard + useForceSimulation in @spaarke/ui-components
└─ Unit tests (90%+ coverage)

Phase 2: BFF API Enhancement
└─ countOnly parameter on visualization endpoint
└─ API tests

Phase 3: Code Page Enhancements
└─ Migrate graph to shared useForceSimulation
└─ CSV export + quick search

Phase 4: RelatedDocumentCount PCF
└─ New PCF control with count card + dialog
└─ Document Main Form configuration

Phase 5: Integration, Testing & Deployment
└─ End-to-end testing, dark mode, performance
└─ Deploy all components

Wrap-up: Project completion
└─ Code review, ADR check, documentation
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 (shared lib) BLOCKS Phase 3 (Code Page migration) and Phase 4 (PCF uses shared components)
- Phase 2 (API countOnly) BLOCKS Phase 4 (PCF count fetch)
- Phase 3 and Phase 4 can run in PARALLEL after Phase 1+2 complete

**High-Risk Items:**
- Shared hook React 16 compatibility — Mitigation: test in PCF harness early
- BFF API endpoint modification — Mitigation: `?limit=1` fallback

---

## 4. Phase Breakdown

### Phase 1: Shared Library Foundation

**Objectives:**
1. Create `RelationshipCountCard` shared component
2. Create `useForceSimulation` shared hook with sync pre-computation
3. Achieve 90%+ test coverage on both

**Deliverables:**
- [ ] `RelationshipCountCard` component (Fluent v9, callback-based props, dark mode)
- [ ] `useForceSimulation` hook (sync d3-force, hub-spoke + peer-mesh modes)
- [ ] Unit tests (90%+ coverage)
- [ ] Barrel + deep import exports verified

**Critical Tasks:**
- `useForceSimulation` — core shared hook, BLOCKS Phase 3 and Phase 4

**Inputs**: Existing `useForceLayout` (async pattern), SemanticSearch `sim.tick(300)` pattern
**Outputs**: New exports in `@spaarke/ui-components`

### Phase 2: BFF API Enhancement

**Objectives:**
1. Add `countOnly` query parameter to visualization endpoint
2. Skip graph topology computation when `countOnly=true`

**Deliverables:**
- [ ] `countOnly` parameter handler on existing endpoint
- [ ] API returns metadata only (empty nodes/edges) for count requests
- [ ] Response time < 200ms for count-only requests
- [ ] Unit tests for new parameter handling

**Inputs**: Existing visualization endpoint handler
**Outputs**: Enhanced API endpoint

### Phase 3: Code Page Enhancements

**Objectives:**
1. Migrate `DocumentGraph` to shared `useForceSimulation` (eliminate spinner)
2. Add CSV export service and Export button
3. Add quick search filter in toolbar

**Deliverables:**
- [ ] `DocumentGraph.tsx` uses `useForceSimulation` from shared lib
- [ ] `CsvExportService.ts` (Blob + anchor download, UTF-8 BOM)
- [ ] Export button in Grid view toolbar
- [ ] Quick search input filters grid rows
- [ ] All existing functionality regression-tested

**Inputs**: Phase 1 complete (shared hook available)
**Outputs**: Enhanced Code Page

### Phase 4: RelatedDocumentCount PCF

**Objectives:**
1. Create new PCF control for Document Main Form
2. Display count, loading, error states
3. Open FindSimilarDialog on click

**Deliverables:**
- [ ] `RelatedDocumentCount` PCF scaffolded and implemented
- [ ] `useRelatedDocumentCount` API hook (calls countOnly endpoint)
- [ ] Dark mode support
- [ ] Document Main Form configured with PCF field

**Inputs**: Phase 1 (RelationshipCountCard), Phase 2 (countOnly API)
**Outputs**: Deployed PCF on Document form

### Phase 5: Integration, Testing & Deployment

**Objectives:**
1. End-to-end testing of full flow (form → count → dialog → graph)
2. Dark mode verification across all surfaces
3. Performance validation
4. Deploy all components

**Deliverables:**
- [ ] Integration test: count card → dialog → graph flow
- [ ] Dark mode passes on all surfaces
- [ ] Performance: count < 200ms, layout < 50ms, grid 100 docs < 1s
- [ ] All components deployed to dev environment

**Inputs**: All phases complete
**Outputs**: Deployed and verified system

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| BFF API (Azure App Service) | Production | Low | Well-established deployment path |
| Dataverse environment | Available | Low | Dev environment accessible |
| MSAL authentication | Production | Low | `@spaarke/auth` stable |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| DocumentRelationshipViewer Code Page | `src/client/code-pages/DocumentRelationshipViewer/` | Deployed |
| FindSimilarDialog | `@spaarke/ui-components` | Available |
| BFF visualization endpoint | `Sprk.Bff.Api` | Deployed |
| `@spaarke/auth` | npm package | Available |

---

## 6. Testing Strategy

**Unit Tests** (90%+ coverage for shared, 80%+ for PCF):
- `RelationshipCountCard` — render states (loading, count, error, zero), dark mode, click handler
- `useForceSimulation` — hub-spoke positions, peer-mesh positions, edge cases (0 nodes, 1 node)
- `CsvExportService` — CSV generation, UTF-8 BOM, column mapping
- `useRelatedDocumentCount` — API call, error handling, loading state

**Integration Tests**:
- BFF API `countOnly` parameter — returns metadata only
- Code Page graph migration — renders without spinner

**Manual E2E Tests**:
- Open Document record → count card loads within 200ms
- Click count card → dialog opens with graph
- Graph renders instantly (no spinner)
- Export CSV from Grid view → opens in Excel
- Dark mode on all surfaces

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] `useForceSimulation` returns positioned nodes synchronously (no `isSimulating` state)
- [ ] `RelationshipCountCard` renders all states (loading, count, error, zero)
- [ ] Both components build in shared library; barrel + deep imports work

**Phase 2:**
- [ ] `?countOnly=true` returns `metadata` with empty `nodes[]`/`edges[]`
- [ ] Response time < 200ms for count-only requests

**Phase 3:**
- [ ] Graph renders without "Calculating layout..." spinner
- [ ] CSV export includes all 6 specified columns
- [ ] Quick search filters grid in real-time

**Phase 4:**
- [ ] Count card visible on Document main form
- [ ] Click opens FindSimilarDialog with correct document context

### Business Acceptance

- [ ] Users see related document count immediately on form load
- [ ] Users can export relationship data for compliance reporting
- [ ] Consistent graph visualization experience (no layout spinner)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | `countOnly` requires BFF API changes | Med | Med | Fall back to `?limit=1` (still returns totalResults) |
| R2 | Sync pre-computation slow for 100+ nodes | Low | Low | Cap at 300 ticks; ~100ms acceptable |
| R3 | `useForceSimulation` React 16 incompatibility | Low | High | Uses only `useMemo`; test in PCF harness early |
| R4 | Shared lib build breaks from new components | Low | Med | Test barrel + deep import paths before merge |
| R5 | CSV blocked by browser popup blocker | Low | Low | Use blob/anchor pattern (not window.open) |

---

## 9. Next Steps

1. **Generate task files** from this plan
2. **Create feature branch** for isolation
3. **Begin Phase 1** — shared library foundation

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
