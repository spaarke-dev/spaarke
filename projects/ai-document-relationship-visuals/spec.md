# AI Document Relationship Visualization — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-10
> **Source**: `projects/ai-document-relationship-visuals/design.md` (v3.0)

## Executive Summary

Enhance the **DocumentRelationshipViewer** Code Page and establish a **standardized graph visualization pattern** for the Spaarke platform. This project delivers: (1) a Related Document Count card on the Document Main Form so users see related document counts instantly on record load, (2) standardized `@xyflow/react` + synchronous `d3-force` graph rendering via a shared hook, (3) CSV export from the Grid view, and (4) Grid view enhancements including quick search.

---

## Scope

### In Scope

- **FR-1: RelatedDocumentCount PCF control** — New PCF for Document Main Form displaying count of semantically related documents with drill-through to full viewer dialog
- **FR-2: CSV/Excel export** — Export button in Grid view toolbar, downloads filtered relationship data as CSV
- **FR-3: Grid view enhancements** — Quick search filter, row selection for graph highlight
- **FR-4: Shared `useForceSimulation` hook** — Extract synchronous d3-force layout to `@spaarke/ui-components` with hub-spoke and peer-mesh modes
- **FR-5: BFF API `countOnly` parameter** — Fast path on existing visualization endpoint that skips graph topology, returns count only (~50-200ms)
- **Shared `RelationshipCountCard` component** — Callback-based Fluent v9 card in `@spaarke/ui-components`
- **Migrate Code Page graph** from async `useForceLayout` to shared sync `useForceSimulation`

### Out of Scope

- SemanticSearch Code Page migration to @xyflow (future project — this project establishes the shared hook)
- Refactoring existing DocumentRelationshipViewer PCF (duplicated codebase remains as-is)
- Inline editing in Grid view
- Multi-select export (export is filtered view only)
- Real-time updates (data fetched on load only)
- Mobile-optimized layouts (responsive but desktop-first)
- Semantic search within results (deferred to `ai-semantic-search-foundation-r1`)

### Affected Areas

- `src/client/pcf/RelatedDocumentCount/` — **NEW** PCF control
- `src/client/shared/Spaarke.UI.Components/src/components/RelationshipCountCard/` — **NEW** shared component
- `src/client/shared/Spaarke.UI.Components/src/hooks/useForceSimulation.ts` — **NEW** shared hook
- `src/client/code-pages/DocumentRelationshipViewer/src/App.tsx` — Add Export button, quick search input
- `src/client/code-pages/DocumentRelationshipViewer/src/components/DocumentGraph.tsx` — Migrate to shared `useForceSimulation`
- `src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx` — Accept search filter, expose filtered rows for export
- `src/client/code-pages/DocumentRelationshipViewer/src/services/CsvExportService.ts` — **NEW** export service
- `src/server/api/Sprk.Bff.Api/` — Add `countOnly` query parameter to visualization endpoint
- `src/solutions/` — Dataverse form customization to place RelatedDocumentCount PCF

---

## Requirements

### Functional Requirements

1. **FR-1.1**: New `RelatedDocumentCount` PCF control displays count of semantically related documents on the Document main form — Acceptance: Card renders on Document form with numeric count
2. **FR-1.2**: Count fetches on form load using BFF API `?countOnly=true` (fast path, ~50-200ms) — Acceptance: Network tab shows `countOnly=true` request completing < 200ms
3. **FR-1.3**: Clicking the card opens `FindSimilarDialog` with the full DocumentRelationshipViewer Code Page — Acceptance: Dialog opens at 85vw x 85vh with graph view
4. **FR-1.4**: Card shows loading spinner while count is being fetched — Acceptance: Spinner visible during API call
5. **FR-1.5**: Card shows error state if API call fails — Acceptance: Error message displayed, card doesn't crash
6. **FR-1.6**: Card renders correctly in dark mode — Acceptance: Visual inspection in dark theme
7. **FR-1.7**: Card shows "Last updated" timestamp — Acceptance: Timestamp visible below count (Should Have)
8. **FR-1.8**: Count auto-refreshes if form context changes (user navigates to different record) — Acceptance: Count updates on record navigation (Should Have)
9. **FR-2.1**: Export button in Grid view toolbar downloads CSV of visible documents — Acceptance: CSV file downloads on click
10. **FR-2.2**: CSV includes: Document Name, Type, Similarity %, Relationship Type, Parent Entity, Modified Date — Acceptance: All columns present in downloaded file
11. **FR-2.3**: Export respects active filters (threshold, document types) — Acceptance: Filtered view matches exported data
12. **FR-2.4**: CSV supports 500+ rows without browser issues — Acceptance: Export completes < 2s for 500 rows (Should Have)
13. **FR-2.5**: Export filename includes source document name and date — Acceptance: Filename pattern `related-documents-{name}-{date}.csv` (Should Have)
14. **FR-3.1**: Quick search/filter text input to filter grid by document name — Acceptance: Grid filters as user types (Should Have)
15. **FR-3.2**: Row click/selection highlights corresponding node in Graph view — Acceptance: Click row, switch to Graph, node is highlighted (Should Have)
16. **FR-3.3**: Column resize and reorder — Acceptance: Columns draggable (Could Have)
17. **FR-4.1**: Extract `useForceSimulation` hook into `@spaarke/ui-components` — Acceptance: Hook importable from shared library
18. **FR-4.2**: Hook uses synchronous pre-computation (no tick-by-tick state updates) — Acceptance: No `isSimulating` state, no layout spinner
19. **FR-4.3**: Support hub-spoke mode (source node pinned at center) for relationship graphs — Acceptance: Source node at center with `fx`/`fy` pinning
20. **FR-4.4**: Support peer-mesh mode (no central node) for future SemanticSearch — Acceptance: All nodes equal, no pinning (Should Have)
21. **FR-4.5**: Configurable force parameters (charge, distance, collision) with sensible defaults — Acceptance: Options interface with defaults
22. **FR-4.6**: Include shared viewport fitting utility — Acceptance: Bounding box → scale/translate computation (Should Have)
23. **FR-5.1**: Add `countOnly` query parameter to `GET /api/ai/visualization/related/{documentId}` — Acceptance: Parameter accepted, returns metadata only
24. **FR-5.2**: When `countOnly=true`, skip graph topology computation, return only `metadata` with empty `nodes[]` and `edges[]` — Acceptance: Response has empty arrays, populated metadata
25. **FR-5.3**: Count-only response time target: < 200ms — Acceptance: Network tab timing (Should Have)

### Non-Functional Requirements

- **NFR-1**: All components must support dark mode (Fluent UI v9 design tokens) — applies to all UI surfaces
- **NFR-2**: Code Page uses React 19 (bundled); PCF uses React 16 (platform-provided) — per ADR-022
- **NFR-3**: `RelationshipCountCard` follows callback-based prop pattern (no service imports) — per ADR-012
- **NFR-4**: New shared components added to `@spaarke/ui-components` with barrel + deep import support — per ADR-012
- **NFR-5**: `useForceSimulation` must work in both React 16 (PCF) and React 19 (Code Page) contexts — uses `useMemo` only
- **NFR-6**: Graph renders instantly on open — no "Calculating layout..." spinner (sync pre-computation)
- **NFR-7**: Grid view must handle 100+ documents without performance degradation
- **NFR-8**: CSV export must support 500+ rows
- **NFR-9**: Shared components require 90%+ test coverage — per ADR-012
- **NFR-10**: BFF API errors must return `ProblemDetails` format — per ADR-001

---

## Technical Constraints

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| **ADR-001** | Minimal API + BackgroundService | BFF API `countOnly` enhancement must use Minimal API pattern, return `ProblemDetails` on errors |
| **ADR-006** | Anti-legacy-JS: PCF for form controls, Code Pages for dialogs | Code Page for full viewer dialog; PCF for count card on form |
| **ADR-008** | Endpoint Filters for Authorization | BFF API endpoint must use `.RequireAuthorization()` + endpoint filters, not global middleware |
| **ADR-010** | DI Minimalism | Any new service must be registered as concrete in feature module; ≤15 non-framework DI lines |
| **ADR-012** | Shared Component Library | `RelationshipCountCard` + `useForceSimulation` in `@spaarke/ui-components`; callback-based props; 90%+ test coverage |
| **ADR-021** | Fluent UI v9 Design System | All UI uses Fluent v9 components and design tokens; dark mode required; WCAG 2.1 AA |
| **ADR-022** | PCF Platform Libraries | PCF: React 16 platform-provided, `ReactDOM.render()`; Code Page: React 19 bundled, `createRoot()` |

### MUST Rules

- ✅ MUST use `@fluentui/react-components` (Fluent v9) exclusively — no v8, no custom CSS
- ✅ MUST use design tokens for all colors/spacing — no hard-coded hex/rgb values
- ✅ MUST support dark mode and high-contrast themes on all UI surfaces
- ✅ MUST use `ReactControl` pattern with `ReactDOM.render()` for PCF — no `createRoot()`
- ✅ MUST use `createRoot()` for Code Page entry point (React 19, bundled)
- ✅ MUST declare `platform-library` in PCF `ControlManifest.Input.xml`
- ✅ MUST use deep imports (`@spaarke/ui-components/dist/components/{Name}`) in PCF consumers
- ✅ MUST use callback-based props in shared components — zero service dependencies
- ✅ MUST use only `useMemo` (no `useTransition`, `useDeferredValue`) in shared hooks for React 16 compatibility
- ✅ MUST use Minimal API pattern (`.MapGet()`) for BFF endpoint
- ✅ MUST use endpoint filters for resource authorization — no global middleware
- ✅ MUST return `ProblemDetails` for all API errors
- ✅ MUST achieve 90%+ test coverage on shared components (`RelationshipCountCard`, `useForceSimulation`)
- ✅ MUST keep PCF bundle under 5MB

### MUST NOT Rules

- ❌ MUST NOT use Fluent UI v8 components or mix versions
- ❌ MUST NOT use React 18+ APIs in PCF controls (`createRoot`, concurrent features)
- ❌ MUST NOT bundle React/ReactDOM into PCF artifacts
- ❌ MUST NOT import services directly in shared components
- ❌ MUST NOT use global middleware for authorization (ADR-008)
- ❌ MUST NOT inject `GraphServiceClient` directly — use facade (ADR-010)
- ❌ MUST NOT create legacy JavaScript webresources (ADR-006)

### Existing Patterns to Follow

- See `src/client/pcf/SemanticSearchControl/` for PCF `ReactControl` + shared component pattern
- See `src/client/shared/Spaarke.UI.Components/src/components/FindSimilarDialog/` for callback-based shared component pattern
- See `src/client/code-pages/SemanticSearch/src/components/SearchResultsMap.tsx` for synchronous d3-force pre-computation pattern (`sim.tick(300)`)
- See `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` for current async layout (to be replaced)
- See `.claude/patterns/` for detailed code patterns

---

## Architecture

### Component Architecture

```
BFF API
  GET /api/ai/visualization/related/{documentId}
  → Full response: nodes[], edges[], metadata (300-800ms)
  → Count only (?countOnly=true): metadata only (50-200ms)
      │                                    │
      │ Full graph data                    │ Count only
      ▼                                    ▼
Code Page (React 19)                 RelatedDocumentCount PCF (React 16)
DocumentRelationshipViewer/          └── hosts RelationshipCountCard
├── App.tsx (toolbar, views)             └── opens FindSimilarDialog → Code Page
├── DocumentGraph.tsx (@xyflow)
│   └── useForceSimulation (SHARED)
├── RelationshipGrid.tsx
├── CsvExportService.ts (NEW)
└── ControlPanel.tsx

@spaarke/ui-components (Shared)
├── RelationshipCountCard (NEW) — count + onOpen callback
├── useForceSimulation (NEW) — sync d3-force, hub-spoke/peer-mesh
└── FindSimilarDialog (existing) — iframe dialog shell
```

### Data Flow: Document Form → Count Card → Full Viewer

1. User opens Document record
2. `RelatedDocumentCount` PCF initializes, calls API with `?countOnly=true`
3. API returns `metadata.totalResults` in ~50-200ms
4. `RelationshipCountCard` renders: "RELATED DOCUMENTS [10]"
5. User clicks card → `FindSimilarDialog` opens (85vw x 85vh iframe)
6. Code Page loads, authenticates via `@spaarke/auth`
7. Full API call returns nodes + edges (~300-800ms)
8. `useForceSimulation` runs 300 ticks synchronously (~20-50ms)
9. `@xyflow/react` renders fully-positioned graph instantly (no spinner)

### New Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `RelatedDocumentCount` PCF | `src/client/pcf/RelatedDocumentCount/` | Form-bound PCF hosting count card + dialog |
| `RelationshipCountCard` | `@spaarke/ui-components` | Shared display component (count, loading, error, onOpen) |
| `useForceSimulation` hook | `@spaarke/ui-components` | Shared sync d3-force layout (hub-spoke + peer-mesh) |
| `CsvExportService.ts` | Code Page `/services/` | Blob + anchor CSV download |

### Modified Components

| Component | Changes |
|-----------|---------|
| `App.tsx` (Code Page) | Add Export button (Grid view only), quick search input in toolbar |
| `DocumentGraph.tsx` (Code Page) | Replace `useForceLayout` with shared `useForceSimulation` |
| `RelationshipGrid.tsx` (Code Page) | Accept search filter prop, `onRowSelect` callback, expose filtered rows |
| BFF API endpoint | Add `countOnly` query parameter to existing visualization route |

---

## Key Technical Decisions

### Synchronous Pre-Computation

Replace the current async tick-by-tick d3-force simulation with synchronous pre-computation:

```typescript
// BEFORE (current — causes spinner)
simulation.on("tick", () => { setLayoutNodes(updated); }); // 30+ React renders

// AFTER (shared hook — instant)
const sim = forceSimulation(nodes).force(...).stop();
sim.tick(300); // ~20-50ms for 25 nodes, zero React renders
return positionedNodes; // Single render with final positions
```

### PCF Control Properties

```xml
<property name="documentId" of-type="SingleLine.Text" usage="bound" required="true" />
<property name="tenantId" of-type="SingleLine.Text" usage="input" />
<property name="apiBaseUrl" of-type="SingleLine.Text" usage="input" />
<property name="cardTitle" of-type="SingleLine.Text" usage="input" default-value="RELATED DOCUMENTS" />
```

### Shared Hook Interface

```typescript
export interface ForceSimulationOptions {
    ticks?: number;              // default: 300
    chargeStrength?: number;     // default: -800
    linkDistanceMultiplier?: number; // default: 400
    collisionRadius?: number;    // default: 60
    mode?: "hub-spoke" | "peer-mesh"; // default: "hub-spoke"
    center?: { x: number; y: number }; // default: { x: 0, y: 0 }
}
```

### API Response Schema

```typescript
interface DocumentGraphResponse {
    nodes: ApiDocumentNode[];      // Empty if countOnly=true
    edges: ApiDocumentEdge[];      // Empty if countOnly=true
    metadata: {
        sourceDocumentId: string;
        tenantId: string;
        totalResults: number;      // Used by count card
        threshold: number;
        searchLatencyMs: number;
        cacheHit: boolean;
    };
}
```

---

## Success Criteria

1. [ ] Document main form shows related document count within 200ms of form load — Verify: Open Document record, observe card timing in DevTools
2. [ ] Clicking count card opens FindSimilarDialog with full viewer — Verify: Click card, dialog opens with graph
3. [ ] Graph renders instantly on dialog open — no layout spinner — Verify: No "Calculating layout..." message
4. [ ] CSV export downloads file with all filtered documents from Grid view — Verify: Export, open in Excel
5. [ ] Export respects active filters — Verify: Apply filters, export, verify subset
6. [ ] Quick search filters grid rows by document name — Verify: Type text, rows filter
7. [ ] `useForceSimulation` hook added to `@spaarke/ui-components` and builds cleanly — Verify: `npm run build`
8. [ ] `RelationshipCountCard` added to `@spaarke/ui-components` and builds cleanly — Verify: `npm run build`
9. [ ] All existing functionality (Graph, Grid, Filters) continues working — Verify: Regression test
10. [ ] All components render correctly in dark mode — Verify: Toggle dark mode
11. [ ] Shared components have 90%+ test coverage — Verify: `npm test --coverage`

### Performance Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Count card display (form load) | < 200ms API response | Browser DevTools network tab |
| Graph layout computation (25 nodes) | < 50ms | `performance.now()` around `sim.tick(300)` |
| Graph first paint after data fetch | < 100ms | Browser DevTools performance |
| CSV export (500 docs) | < 2000ms | Manual timing |
| Grid view render (100 docs) | < 1000ms | Browser DevTools performance |

---

## Dependencies

### Prerequisites

- Existing Code Page (`src/client/code-pages/DocumentRelationshipViewer/`) — deployed and working
- Existing `FindSimilarDialog` in `@spaarke/ui-components` — available
- Existing BFF API visualization endpoint — deployed (needs `countOnly` enhancement)
- `@spaarke/auth` — MSAL authentication package

### External Dependencies

- BFF API: `GET /api/ai/visualization/related/{documentId}` (existing + `countOnly` param)
- MSAL Authentication via `@spaarke/auth`
- Dataverse Web Resource: `sprk_documentrelationshipviewer` HTML web resource
- Document Main Form: Form customization to place `RelatedDocumentCount` PCF

### Technical Dependencies

| Dependency | Version | Used By |
|------------|---------|---------|
| react | 19.x (bundled) | Code Page |
| react | 16.14.0 (platform) | PCF Controls |
| @fluentui/react-components | 9.54+ | Code Page |
| @fluentui/react-components | 9.46.2 | PCF Controls |
| @xyflow/react | 12.x | Code Page graph view |
| d3-force | 3.x | Shared `useForceSimulation` hook |
| @spaarke/ui-components | 2.x | FindSimilarDialog, RelationshipCountCard, useForceSimulation |
| @spaarke/auth | latest | MSAL authentication |

---

## Graph Visualization Audit (Current State)

Three separate implementations currently exist — this project standardizes the approach:

| Component | Rendering | Layout | React | Node Visual | Data Source |
|-----------|-----------|--------|-------|-------------|-------------|
| **DocumentRelationshipViewer Code Page** | `@xyflow/react` v12 | d3-force async tick-by-tick | 19 | Rich cards | Server (BFF API) |
| **DocumentRelationshipViewer PCF** | `react-flow-renderer` v10 | d3-force async tick-by-tick | 16 | Rich cards (duplicated) | Server (BFF API) |
| **SemanticSearch Code Page** | Raw SVG | d3-force sync 300-tick | 19 | Simple colored circles | Client (pairwise metadata) |
| **PlaybookBuilder Code Page** | `@xyflow/react` v12 | None (static DAG) | 19 | Custom step nodes | Static definition |

**Standardized pattern** (established by this project):
- `@xyflow/react` v12 for rendering
- `d3-force` v3 with synchronous pre-computation
- Shared `useForceSimulation` hook in `@spaarke/ui-components`

**SemanticSearch migration** to this pattern is a future project.

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| `countOnly` API param requires BFF changes | Medium | Blocks count card | Fall back to `?limit=1` initially (still returns `metadata.totalResults`) |
| Sync pre-computation slow for 100+ nodes | Low | Delayed first paint | Cap at 300 ticks; ~100ms for 100 nodes is acceptable |
| `useForceSimulation` React 16 compatibility | Low | PCF build fails | Hook uses only `useMemo` — no React 18+ APIs |
| CSV blocked by browser popup blocker | Low | Export fails | Use blob/anchor pattern (not window.open) |
| Shared lib build breaks from new components | Low | Consumer builds fail | Test barrel + deep import paths before merge |

---

## Owner Clarifications

*Captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Standardization | Should DocumentRelationshipViewer and SemanticSearch use the same graph approach? | Yes — standardize on @xyflow + sync d3-force | Shared `useForceSimulation` hook; SemanticSearch migrates in future project |
| Count card | Should Document form show related document count? | Yes — required deliverable (FR-1) | New PCF control + BFF API `countOnly` fast path |
| Sync pre-computation | Does sync layout allow fast card population? | Yes — count card uses separate `countOnly` API (~50-200ms); sync layout is for viewer only (~20-50ms) | Two separate concerns: count API vs layout computation |

## Assumptions

*Proceeding with these assumptions (not explicitly specified in design):*

- **React version**: Code Page uses React 19 (per current `package.json`), not React 18 as stated in some design sections
- **Zero-state card**: When a document has 0 related documents, the card shows "0" and remains clickable (opens empty viewer)
- **Test coverage**: Shared components (`RelationshipCountCard`, `useForceSimulation`) will have 90%+ unit test coverage per ADR-012
- **PCF `ReactControl`**: `RelatedDocumentCount` uses `ReactControl<IInputs, IOutputs>` pattern (returns `React.ReactElement` from `updateView`)
- **Export format**: CSV only (not XLSX) — Excel opens CSV files natively with UTF-8 BOM

## Unresolved Questions

- [ ] **Exact BFF API endpoint handler location**: Which file currently handles `GET /api/ai/visualization/related/{documentId}`? — Blocks: FR-5 implementation (task will discover this)
- [ ] **Document Main Form section placement**: Which form section should host the RelatedDocumentCount PCF? — Blocks: Dataverse form customization (can be decided during implementation)

---

## Existing File Inventory

### Code Page (will be modified)

| File | Purpose |
|------|---------|
| `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` | Entry point — `createRoot()`, URL params, FluentProvider |
| `src/client/code-pages/DocumentRelationshipViewer/src/App.tsx` | Main component — toolbar, view toggle, filter panel |
| `src/client/code-pages/DocumentRelationshipViewer/src/components/DocumentGraph.tsx` | @xyflow/react graph (migrate to shared hook) |
| `src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx` | Fluent v9 DataGrid (add search, export support) |
| `src/client/code-pages/DocumentRelationshipViewer/src/components/ControlPanel.tsx` | Filter controls |
| `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` | Force layout (to be replaced) |

### Shared Library (will receive new components)

| Component | Status |
|-----------|--------|
| `FindSimilarDialog` | Existing — consumed by new PCF |
| `RelationshipCountCard` | **NEW** — count card shared component |
| `useForceSimulation` | **NEW** — sync d3-force shared hook |

### Existing PCF (NOT modified)

| File | Purpose |
|------|---------|
| `src/client/pcf/DocumentRelationshipViewer/` | Independent graph PCF — remains as-is |

---

*AI-optimized specification. Original design: `projects/ai-document-relationship-visuals/design.md` (v3.0)*
