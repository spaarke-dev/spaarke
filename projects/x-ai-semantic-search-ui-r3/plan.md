# Project Plan: AI Semantic Search UI R3

> **Last Updated**: 2026-02-24
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Build a full-page Semantic Search code page for system-wide, multi-entity AI-powered search, complementing the existing matter-bound PCF control with a standalone experience accessible from the Dataverse sitemap.

**Scope**:
- React 19 Code Page with graph clustering + Universal DatasetGrid views
- New BFF API endpoint for entity records search
- Enhanced document search with `scope=all` support
- Saved search favorites via `sprk_gridconfiguration`
- DocumentRelationshipViewer grid migration to Universal DatasetGrid
- Sitemap entry and command bar navigation

**Estimated Effort**: ~120 hours across 8 phases

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006/026**: Code Page = single self-contained HTML web resource (webpack + build-webresource.ps1)
- **ADR-021/022**: React 19 bundled with `createRoot()`; Fluent UI v9 exclusively; dark mode required
- **ADR-001/008**: Minimal API endpoints with endpoint filters for auth
- **ADR-013**: Extend BFF for AI features; no direct Azure AI calls from frontend
- **ADR-012**: Import `UniversalDatasetGrid`, `ViewSelector` from `@spaarke/ui-components`
- **ADR-009/014**: Redis-first caching with tenant-scoped, versioned keys

**From Spec**:
- Single-domain search (one tab active at a time)
- `sprk_gridconfiguration` for saved search storage
- Graph drill-down is inline sub-layout (not navigation)
- Up to 100 nodes in graph view, 1000 results in grid via infinite scroll

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| webpack (not Vite) for build | Matches existing DocumentRelationshipViewer | Reuse webpack.config.js pattern |
| React 19 bundled | ADR-021/022 Code Page requirement | No platform-library declarations |
| Single-domain search | Owner decision — simpler UX | One API call per search, no result merging |
| `sprk_gridconfiguration` | Owner decision — more control | Use existing ViewService + ViewSelector |
| Inline graph drill-down | Matches DocRelViewer pattern | Expand cluster within canvas, not navigate |
| Include DocRelViewer grid migration | Owner decision — visual consistency | Additional 3 tasks in Phase 7 |

### Discovered Resources

**Applicable Skills**:
- `.claude/skills/code-page-deploy/` — Two-step build pipeline (webpack + inline HTML)
- `.claude/skills/bff-deploy/` — Deploy BFF API to Azure App Service
- `.claude/skills/adr-aware/` — Auto-load ADR constraints (always-apply)
- `.claude/skills/dataverse-deploy/` — Deploy web resource to Dataverse

**Knowledge Articles**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — AI architecture, AiSearchService, embeddings
- `docs/architecture/universal-dataset-grid-architecture.md` — Grid component architecture
- `src/solutions/SpaarkeCore/entities/sprk_gridconfiguration/entity-schema.md` — Saved search entity schema
- `.claude/patterns/api/endpoint-definition.md` — Minimal API endpoint pattern
- `.claude/patterns/api/endpoint-filters.md` — Authorization filter pattern
- `.claude/patterns/auth/msal-client.md` — MSAL singleton pattern
- `.claude/patterns/webresource/full-page-custom-page.md` — Code Page template
- `.claude/patterns/pcf/theme-management.md` — Theme detection (4-level priority)

**Canonical Code Examples**:
- `src/client/code-pages/DocumentRelationshipViewer/` — Primary reference (React 19, @xyflow/react, MSAL, webpack)
- `src/client/pcf/SemanticSearchControl/` — Filter UX, search hooks, API services (adapt R16→R19)
- `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/` — UniversalDatasetGrid, ViewSelector
- `src/server/api/Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` — Existing search endpoints
- `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs` — Search service pipeline

**Available Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — PCF/web resource deployment
- `scripts/Test-SdapBffApi.ps1` — API endpoint validation

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Foundation & Investigation (Tasks 001-004)
└─ Project scaffold, spike investigations for unknowns

Phase 2: BFF API Backend (Tasks 010-017)
└─ Enhance document search, create records search endpoint

Phase 3: Code Page Core (Tasks 020-029)
└─ Entry point, auth, API services, filters, domain tabs, search hooks

Phase 4: Grid & Graph Views (Tasks 030-038)
└─ Universal DatasetGrid integration, @xyflow/react clustering, view toggle

Phase 5: Interactive Features (Tasks 040-047)
└─ Command bar, saved searches, entity dialogs, URL params

Phase 6: DocumentRelationshipViewer Migration (Tasks 050-052)
└─ Migrate RelationshipGrid.tsx to Universal DatasetGrid

Phase 7: Testing & Quality (Tasks 060-067)
└─ Unit tests, integration tests, dark mode, accessibility, bundle optimization

Phase 8: Deployment & Wrap-up (Tasks 070-080)
└─ Build, deploy, sitemap, end-to-end validation, project wrap-up
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 (investigations) → INFORMS Phase 2-4 implementation
- Task 002 (records index investigation) → BLOCKS Task 012-013 (records search endpoint)
- Task 003 (grid headless adapter) → BLOCKS Task 030 (grid integration)
- Phase 2 (BFF API) → BLOCKS Phase 3 (code page API services need working endpoints)
- Phase 3 (core) → BLOCKS Phase 4-5 (views and features need core components)

**High-Risk Items:**
- Records index field coverage unknown → Mitigation: Investigation task 002
- Universal DatasetGrid headless data support → Mitigation: Spike task 003
- Bundle size (React 19 + @xyflow/react + d3-force) → Mitigation: Task 067 optimization

---

## 4. Phase Breakdown

### Phase 1: Foundation & Investigation (Tasks 001-004)

**Objectives:**
1. Scaffold the code page project structure
2. Investigate unknowns that affect implementation approach

**Deliverables:**
- [ ] Code page project structure (package.json, webpack, tsconfig, index.html, build script)
- [ ] Records index field coverage report
- [ ] Universal DatasetGrid headless adapter feasibility report
- [ ] sprk_gridconfiguration schema compatibility report

**Critical Tasks:**
- Task 001 (scaffold) — MUST BE FIRST, creates the project structure
- Tasks 002-004 (investigations) — CAN RUN IN PARALLEL, inform subsequent phases

**Inputs**: DocumentRelationshipViewer structure, spec.md
**Outputs**: Working project scaffold, investigation reports in `notes/spikes/`

### Phase 2: BFF API Backend (Tasks 010-017)

**Objectives:**
1. Enable `scope=all` for system-wide document search
2. Create entity records search endpoint
3. Add rate limiting, caching, error handling

**Deliverables:**
- [ ] Enhanced `POST /api/ai/search` with `scope=all` and `entityTypes` filter
- [ ] New `POST /api/ai/search/records` endpoint
- [ ] Unit tests (80%+ coverage)
- [ ] Integration tests for search endpoints
- [ ] BFF API deployed to dev

**Critical Tasks:**
- Task 010-011 (enhance existing endpoint) — PARALLEL, independent changes
- Task 012-013 (new records endpoint) — SEQUENTIAL, models then service
- Task 015-016 (tests) — AFTER implementation
- Task 017 (deploy) — LAST in phase

**Inputs**: Existing SemanticSearchEndpoints.cs, SemanticSearchService.cs, search models
**Outputs**: Working API endpoints, test coverage

### Phase 3: Code Page Core (Tasks 020-029)

**Objectives:**
1. Create working code page with auth, theme, and search functionality
2. Implement filter pane with domain-adaptive filters
3. Implement domain tabs and search state management

**Deliverables:**
- [ ] Code page entry point (index.tsx, App.tsx, FluentProvider)
- [ ] MSAL authentication service
- [ ] API service clients (document search, records search)
- [ ] TypeScript type definitions
- [ ] Search filter pane with all filter components
- [ ] Domain tabs component
- [ ] Search hooks (useSemanticSearch, useRecordSearch)

**Critical Tasks:**
- Task 020-023 (entry point, auth, services, types) — FOUNDATIONAL, mostly parallel
- Task 024-027 (filter components, domain tabs) — AFTER types defined
- Task 028-029 (search hooks) — AFTER services and types

**Inputs**: DocumentRelationshipViewer entry point, PCF filter components, search models
**Outputs**: Working code page with search + filters (no grid/graph yet)

### Phase 4: Grid & Graph Views (Tasks 030-038)

**Objectives:**
1. Implement grid view with Universal DatasetGrid and domain-specific columns
2. Implement graph view with metadata-driven clustering
3. Add view toggle and filter options fetching

**Deliverables:**
- [ ] Grid view with domain-specific columns (Documents, Matters, Projects, Invoices)
- [ ] Graph view with cluster nodes, record nodes, d3-force layout
- [ ] Graph drill-down (expand/collapse clusters)
- [ ] View toggle (graph ↔ grid) toolbar
- [ ] Filter options fetching from Dataverse

**Critical Tasks:**
- Task 030-031 (grid) and Task 032-036 (graph) — PARALLEL groups
- Task 037 (view toggle) — AFTER both grid and graph
- Task 038 (filter options) — INDEPENDENT, can parallel with views

**Inputs**: Universal DatasetGrid, @xyflow/react, d3-force, spec column definitions
**Outputs**: Working grid + graph views with domain-specific rendering

### Phase 5: Interactive Features (Tasks 040-047)

**Objectives:**
1. Implement selection-aware command bar
2. Implement saved search favorites
3. Implement entity record dialogs and document actions
4. Add URL parameter support and status bar

**Deliverables:**
- [ ] Command bar with entity-type-aware actions
- [ ] Saved search selector (ViewSelector pattern)
- [ ] Saved search CRUD hooks
- [ ] Entity record dialog (multi-entity Xrm.Navigation)
- [ ] Document actions (open, delete, email, reindex)
- [ ] URL parameter parsing and deep-linking
- [ ] Status bar (results count, search time, version)

**Critical Tasks:**
- Tasks 040-046 — MOSTLY PARALLEL (independent features)
- Task 047 (status bar) — INDEPENDENT

**Inputs**: Command bar spec, saved search schema, navigation patterns
**Outputs**: Full-featured interactive code page

### Phase 6: DocumentRelationshipViewer Migration (Tasks 050-052)

**Objectives:**
1. Migrate DocumentRelationshipViewer's RelationshipGrid to Universal DatasetGrid
2. Verify visual consistency with the new SemanticSearch grid

**Deliverables:**
- [ ] Updated RelationshipGrid.tsx using Universal DatasetGrid
- [ ] DocRelViewer grid tests passing
- [ ] Visual consistency verification

**Critical Tasks:**
- Task 050 (analysis) → Task 051 (migration) → Task 052 (testing) — SEQUENTIAL

**Inputs**: Existing RelationshipGrid.tsx, Universal DatasetGrid API
**Outputs**: Migrated grid component, passing tests

### Phase 7: Testing & Quality (Tasks 060-067)

**Objectives:**
1. Achieve comprehensive test coverage for hooks, services, components
2. Validate dark mode, accessibility, and bundle size

**Deliverables:**
- [ ] Unit tests for hooks, services, components
- [ ] Integration tests for end-to-end search flow
- [ ] Dark mode visual validation
- [ ] Accessibility validation
- [ ] Bundle size optimization (tree-shaking, lazy loading)

**Critical Tasks:**
- Tasks 060-063 (tests) — PARALLEL groups
- Tasks 064-065 (dark mode, a11y) — PARALLEL
- Task 066-067 (bundle optimization) — AFTER initial build working

**Inputs**: All implemented components, build output
**Outputs**: Test suite, optimization report, passing CI

### Phase 8: Deployment & Wrap-up (Tasks 070-080)

**Objectives:**
1. Deploy code page and BFF API to dev
2. Create sitemap entry and command bar button
3. End-to-end validation in Dataverse
4. Code review and project wrap-up

**Deliverables:**
- [ ] Code page deployed as `sprk_semanticsearch.html` web resource
- [ ] BFF API deployed with new endpoints
- [ ] Sitemap entry under AI/Search area group
- [ ] Command bar button for Semantic Search
- [ ] End-to-end validation report
- [ ] Code review + ADR check passing
- [ ] Project wrap-up (lessons learned, README status update)

**Critical Tasks:**
- Task 070-072 (deploy) — SEQUENTIAL (build → deploy code page → deploy API)
- Task 073 (e2e validation) — AFTER deployments
- Task 074 (code review) — AFTER validation
- Task 080 (wrap-up) — LAST

**Inputs**: Built artifacts, deployed environments
**Outputs**: Production-ready feature, project archive

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Search (spaarke-search-dev) | GA | Low | Existing service, proven |
| Azure OpenAI (embeddings) | GA | Low | Existing service |
| Dataverse (spaarkedev1) | Available | Low | Dev environment ready |
| BFF API (spe-api-dev-67e2xz) | Available | Low | Running in dev |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| SemanticSearchControl PCF (R2) | `src/client/pcf/SemanticSearchControl/` | Completed (v1.0.46) |
| DocumentRelationshipViewer | `src/client/code-pages/DocumentRelationshipViewer/` | Completed (v1.0.3) |
| Universal DatasetGrid | `src/client/shared/Spaarke.UI.Components/` | Available (v2.0.7) |
| ViewSelector/ViewService | `src/client/shared/Spaarke.UI.Components/` | Available |
| SemanticSearchService | `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/` | Available |
| knowledge-index | Azure AI Search | Available |
| spaarke-records-index | Azure AI Search | Available (coverage TBD) |
| sprk_gridconfiguration | Dataverse entity | Available |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- Search hooks (useSemanticSearch, useRecordSearch, useClusterLayout)
- API services (SemanticSearchApiService, RecordSearchApiService)
- Filter components (FilterDropdown, DateRangeFilter, SearchDomainTabs)
- BFF API services (SemanticSearchService, RecordSearchService)
- BFF API endpoints (search, count, records)

**Integration Tests**:
- End-to-end document search flow (query → API → results)
- End-to-end records search flow (query → API → results)
- Saved search save/load cycle
- Command bar actions (delete, email, reindex)

**Visual/Manual Tests**:
- Dark mode rendering across all components
- Graph clustering with various category selections
- Grid column switching across domain tabs
- Bundle size verification (<3s load time)
- Keyboard navigation and ARIA compliance

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1-2 (Backend Ready):**
- [ ] `POST /api/ai/search` returns results with `scope=all`
- [ ] `POST /api/ai/search/records` returns Matters, Projects, Invoices
- [ ] All API tests pass, 80%+ coverage

**Phase 3-5 (Code Page Complete):**
- [ ] Code page loads in <3s as single HTML web resource
- [ ] All 4 domain tabs work with domain-specific columns and filters
- [ ] Graph clustering renders with 5 category options
- [ ] Saved searches persist and restore correctly
- [ ] Command bar actions work per entity type
- [ ] Dark mode renders correctly

**Phase 6-8 (Shipped):**
- [ ] DocRelViewer grid migrated and working
- [ ] End-to-end validation passes in Dataverse
- [ ] Code review + ADR check clean
- [ ] Bundle size optimized

### Business Acceptance

- [ ] Legal professionals can search system-wide from a single page
- [ ] Cross-entity search provides meaningful results
- [ ] Saved searches improve workflow efficiency
- [ ] Graph visualization reveals patterns in search results

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|-------------|--------|------------|
| R1 | Records index not fully populated | Medium | High | Spike task 002 before implementation |
| R2 | Universal DatasetGrid doesn't support headless data | Medium | Medium | Spike task 003; fallback to custom Fluent DataGrid |
| R3 | Bundle size exceeds 3s load target | Medium | Medium | Tree-shake, lazy-load graph, code-split (task 067) |
| R4 | sprk_gridconfiguration schema insufficient | Low | Medium | Spike task 004; extend schema if needed |
| R5 | MSAL token scope doesn't cover cross-entity | Low | Medium | Verify scope in task 021 |
| R6 | Graph performance with 100+ nodes | Medium | Low | Limit to top 100 results in graph view |
| R7 | scope=all exposes data across tenant boundaries | Low | High | Verify tenant isolation in search service |

---

## 9. Next Steps

1. **Run** `/project-pipeline` Step 3 to generate task files
2. **Execute** Task 001 (project scaffold)
3. **Execute** Tasks 002-004 (investigations, parallel)
4. **Begin** Phase 2 (BFF API backend)

---

**Status**: Ready for Tasks
**Next Action**: Generate task files from this plan

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
