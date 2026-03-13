# AI Document Relationship Visualization - Design Document

> **Project**: AI Document Relationship Visuals
> **Version**: 3.0
> **Date**: March 10, 2026
> **Author**: Product Team
> **Status**: Draft - Pending Review

---

## Executive Summary

This project enhances the **DocumentRelationshipViewer** Code Page and establishes a **standardized graph visualization pattern** for the Spaarke platform. The core deliverables are:

1. **Related Document Count on Document Main Form** — When a user opens a Document record, a card immediately shows how many semantically related documents exist (e.g., "10 related documents"). Clicking the card opens the full relationship viewer dialog.
2. **Standardized graph visualization** — Adopt `@xyflow/react` + synchronous `d3-force` pre-computation as the platform standard for all document relationship graphs. Extract a shared `useForceSimulation` hook into `@spaarke/ui-components`.
3. **CSV/Excel Export** — Export filtered relationship data from the Grid view.
4. **Grid view enhancements** — Quick search filter, improved column interactions.

### Technical Standardization Decision

The codebase currently has **three separate graph visualization implementations** using different rendering approaches (see [Graph Visualization Audit](#graph-visualization-audit-current-state)). This project standardizes on:

- **Rendering**: `@xyflow/react` v12 — rich React component nodes, built-in pan/zoom/minimap/controls, accessibility
- **Layout**: `d3-force` v3 with **synchronous pre-computation** (compute all positions before first paint — no layout spinner)
- **Shared hook**: `useForceSimulation` in `@spaarke/ui-components` — reusable across all graph consumers

This pattern will be adopted by the SemanticSearch Code Page in a future project, replacing its current raw SVG approach. The fundamental user need — "show me how documents relate to each other" — is the same regardless of whether relationships come from the RAG pipeline or semantic search.

### What Already Exists (Implemented)

- **Code Page** (`src/client/code-pages/DocumentRelationshipViewer/`) — React 18 standalone web resource with:
  - Interactive force-directed graph view (`@xyflow/react` v12)
  - Grid/table view (`RelationshipGrid.tsx` with Fluent v9 DataGrid)
  - Graph/Grid view toggle in toolbar
  - Filter panel with similarity threshold slider, depth limit, document type filters
  - Node action bar (open record, view file, expand)
  - MSAL authentication via `@spaarke/auth`
  - Dark mode support (URL param or OS preference)
- **PCF Control** (`src/client/pcf/DocumentRelationshipViewer/`) — Independent field-bound PCF (React 16) with its own graph visualization, filter menu, and full-screen modal (`RelationshipViewerModal.tsx`). This is a separate codebase from the Code Page — components (`DocumentGraph`, `DocumentNode`, `ControlPanel`, etc.) are duplicated, not shared. Graph-only (no Grid view).
- **FindSimilarDialog** (`@spaarke/ui-components`) — Shared component that opens the Code Page in a near-fullscreen iframe dialog (85vw x 85vh)

---

## Problem Statement

### Current State

The **DocumentRelationshipViewer** Code Page (v1.0.0) provides:
- Interactive `@xyflow/react` v12 force-directed graph visualization
- Grid view with sortable Fluent v9 DataGrid (columns: Document, Relationship, Similarity, Type, Parent Entity, Modified)
- Filter panel: similarity threshold slider, depth limit, max nodes per level, document type checkboxes
- Node selection with action bar (open record, view file)
- Support for parent hub nodes (Matter, Project, Invoice, Email)

The **PCF Control** (`src/client/pcf/DocumentRelationshipViewer/`) is a **separate, independent implementation** with:
- Its own duplicated components: `DocumentGraph`, `DocumentNode`, `DocumentEdge`, `ControlPanel`, `NodeActionBar`
- Its own duplicated hooks: `useVisualizationApi`, `useForceLayout`
- Its own duplicated services: `VisualizationApiService`, auth services
- `RelationshipViewerModal.tsx` — a full-screen modal with sidebar + graph (not using FindSimilarDialog)
- Graph-only view (no Grid view, no view toggle, no export)
- React 16 APIs (platform-provided), field-bound with `selectedDocumentId` output
- Relationship type filter via Fluent v9 Menu dropdown

The **SemanticSearch Code Page** (`src/client/code-pages/SemanticSearch/`) uses a completely different graph approach:
- Raw SVG rendering with `d3-force` simulation directly (no @xyflow)
- Simple colored circles for nodes (no rich document cards)
- Client-side pairwise similarity computation (`useSimilarityProjection.ts`)
- Manual pan/zoom/drag event handling

The **FindSimilarDialog** shared component provides:
- iframe-based dialog shell consumed by PCF controls and Code Pages
- Opens the Code Page web resource as a dialog via `Xrm.Navigation.navigateTo`

### Pain Points

| Issue | User Impact |
|-------|-------------|
| **No related document count on Document form** | Users must open the full viewer just to know if related documents exist |
| **No export capability** | Users cannot extract relationship data to Excel for reporting |
| **Inconsistent graph UX** | SemanticSearch and DocumentRelationshipViewer show document relationships using completely different visual approaches — confusing for users |
| **Code duplication** | PCF and Code Page duplicate all graph components — bug fixes require changes in two places |
| **Async layout causes spinner** | Current `useForceLayout` runs tick-by-tick, showing "Calculating layout..." spinner for 500-1000ms on every load |
| **No shared graph infrastructure** | Each consumer implements its own d3-force simulation, viewport fitting, and interaction handlers |

### User Feedback

- When opening a Document record, users want to **immediately see** how many related documents exist without opening the full viewer
- Users want to export document relationship lists to Excel for compliance reporting
- Users expect the same rich document information experience across all graph visualizations (relationship viewer and semantic search)

---

## Proposed Solution

### Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                   DOCUMENT RELATIONSHIP VISUALIZATION                              │
├──────────────────────────────────────────────────────────────────────────────────┤
│                                                                                  │
│  ┌────────────────────────────────────────────────────────────────────────────┐  │
│  │                    BFF API                                                  │  │
│  │  GET /api/ai/visualization/related/{documentId}                             │  │
│  │  → Full: nodes[], edges[], metadata { totalResults, latencyMs }             │  │
│  │  → Count only (?countOnly=true): metadata only (fast path)                  │  │
│  │  Query params: tenantId, threshold, limit, depth, documentTypes, countOnly  │  │
│  └────────────────────────────────────────────────────────────────────────────┘  │
│                         ▲                         ▲                              │
│                         │ Full graph              │ Count only                   │
│                         │ (~300-800ms)            │ (~50-200ms)                  │
│                         │                         │                              │
│  ┌──────────────────────┼─────────────────────────┼───────────────────────────┐  │
│  │                      │                         │                           │  │
│  │  Code Page (React 18, bundled)                 │                           │  │
│  │  src/client/code-pages/DocumentRelationshipViewer/                         │  │
│  │  ┌──────────────────────────────────────────────────────────────────┐      │  │
│  │  │ App.tsx — Toolbar (Graph|Grid toggle, Filters, Search, Export)   │      │  │
│  │  │ ├── DocumentGraph.tsx — @xyflow/react v12 rendering              │      │  │
│  │  │ │   └── useForceSimulation (SHARED) — sync d3-force pre-compute  │      │  │
│  │  │ ├── RelationshipGrid.tsx — Fluent v9 DataGrid (sortable)         │      │  │
│  │  │ ├── ControlPanel.tsx — Similarity, depth, type filters           │      │  │
│  │  │ └── CsvExportService.ts — Blob + anchor download                 │      │  │
│  │  └──────────────────────────────────────────────────────────────────┘      │  │
│  │                                                                            │  │
│  │  Opened via Xrm.Navigation.navigateTo:                                    │  │
│  │    { pageType: "webresource", webresourceName: "sprk_...", data: "..." }   │  │
│  │    { target: 2, width: 85%, height: 85% }                                 │  │
│  └────────────────────────────────────────────────────────────────────────────┘  │
│                         ▲                         │                              │
│                         │ iframe                  │                              │
│  ┌──────────────────────┼─────────────────────────┼───────────────────────────┐  │
│  │  @spaarke/ui-components (Shared Library)       │                           │  │
│  │                                                │                           │  │
│  │  ┌──────────────────────────────┐              │                           │  │
│  │  │ useForceSimulation (NEW)     │ ◄── Shared by all graph consumers        │  │
│  │  │ • Sync d3-force pre-compute  │     (Code Page, future SemanticSearch)   │  │
│  │  │ • Hub-spoke & peer-mesh modes│                                          │  │
│  │  │ • Configurable force params  │                                          │  │
│  │  └──────────────────────────────┘                                          │  │
│  │                                                                            │  │
│  │  ┌──────────────────────────────┐  ┌──────────────────────────────┐        │  │
│  │  │ FindSimilarDialog (existing) │  │ RelationshipCountCard (NEW)  │        │  │
│  │  │ • iframe dialog shell        │  │ • Count display              │        │  │
│  │  │ • 85vw x 85vh               │  │ • Click → onOpen callback    │        │  │
│  │  │ • Consumer builds URL        │  │ • Callback-based props       │        │  │
│  │  └──────────────────────────────┘  │ • ~5KB (no API deps)         │        │  │
│  │                                     └──────────────────────────────┘        │  │
│  └────────────────────────────────────────────────────────────────────────────┘  │
│                         ▲                         ▲                              │
│                         │ consumes                │ consumes                     │
│  ┌──────────────────────┼─────────────────────────┼───────────────────────────┐  │
│  │  Document Main Form — PCF Controls (React 16)  │                           │  │
│  │                                                 │                           │  │
│  │  ┌───────────────────────────────────────────┐  │                           │  │
│  │  │ RelatedDocumentCount PCF (NEW)            │──┘ calls API ?countOnly=true │  │
│  │  │ • Hosts RelationshipCountCard             │                              │  │
│  │  │ • Fetches count on form load              │                              │  │
│  │  │ • Opens FindSimilarDialog on click        │                              │  │
│  │  │ • Field-bound (sprk_documentid)           │                              │  │
│  │  └───────────────────────────────────────────┘                              │  │
│  │                                                                              │  │
│  │  ┌───────────────────────────────────────────┐                              │  │
│  │  │ DocumentRelationshipViewer PCF (existing)  │                              │  │
│  │  │ • Embedded graph on forms (independent)    │                              │  │
│  │  │ • Graph-only, duplicated codebase          │                              │  │
│  │  │ • (No changes in this project)             │                              │  │
│  │  └───────────────────────────────────────────┘                              │  │
│  └──────────────────────────────────────────────────────────────────────────────┘  │
│                                                                                    │
└────────────────────────────────────────────────────────────────────────────────────┘
```

### Standardization Decision: `@xyflow/react` + Synchronous `d3-force`

**Decision**: All document relationship graph visualizations in Spaarke will use `@xyflow/react` for rendering with `d3-force` synchronous pre-computation for layout.

**Why `@xyflow/react`**: Documents need rich node representations — file type icons, document names, similarity badges, relationship type labels, action buttons. Each @xyflow node is a full React component, so Fluent v9 `<Button>`, `<Badge>`, `<Text>`, `<Tooltip>` work naturally inside nodes. Raw SVG requires manual `<text>`, `<rect>`, `<foreignObject>` positioning — fragile, loses Fluent v9 design token compliance, and requires reimplementing all interaction (pan, zoom, minimap, keyboard nav, ARIA, touch) that @xyflow provides built-in.

**Why synchronous pre-computation**: The current `useForceLayout` runs the d3-force simulation tick-by-tick with React state updates, causing a "Calculating layout..." spinner for 500-1000ms. Synchronous pre-computation runs all simulation ticks before the first render:

```typescript
// Synchronous pre-computation — no spinner, instant graph
function computeLayout(nodes, edges, options): PositionedNode[] {
    const sim = forceSimulation(forceNodes)
        .force("link", forceLink(forceLinks)...)
        .force("charge", forceManyBody()...)
        .force("collide", forceCollide()...)
        .stop();

    sim.tick(300);  // ~20-50ms for 25 nodes, synchronous

    return nodes.map(n => ({
        ...n,
        position: { x: forceNodes[n.id].x, y: forceNodes[n.id].y }
    }));
}
```

The user sees a fully-positioned graph on first paint. This approach is proven in the SemanticSearch Code Page (`sim.tick(300)` before render).

**Impact on SemanticSearch**: The SemanticSearch Code Page currently uses raw SVG with simple colored circles. It will migrate to @xyflow with rich document card nodes in a **future project**, using the shared `useForceSimulation` hook established here. This ensures consistent document visualization UX across the platform.

### Key Architectural Observation: Code Duplication

The PCF control and Code Page currently **duplicate** nearly all components (`DocumentGraph`, `DocumentNode`, `DocumentEdge`, `ControlPanel`, `NodeActionBar`, `useVisualizationApi`, `useForceLayout`, `VisualizationApiService`, auth services). This means:
- Bug fixes must be applied in two places
- The PCF is missing features the Code Page has (Grid view, view toggle, export)
- The PCF has its own `RelationshipViewerModal.tsx` while the Code Page uses `FindSimilarDialog` from shared lib

This project does **not** refactor the existing PCF control, but the new `RelatedDocumentCount` PCF is built cleanly on shared components from the start.

---

## Requirements

### Functional Requirements

#### FR-1: Related Document Count on Document Main Form

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-1.1 | New `RelatedDocumentCount` PCF control displays count of semantically related documents on the Document main form | Must Have |
| FR-1.2 | Count fetches on form load using BFF API `?countOnly=true` (fast path, ~50-200ms) | Must Have |
| FR-1.3 | Clicking the card opens `FindSimilarDialog` with the full DocumentRelationshipViewer Code Page | Must Have |
| FR-1.4 | Card shows loading spinner while count is being fetched | Must Have |
| FR-1.5 | Card shows error state if API call fails | Must Have |
| FR-1.6 | Card renders correctly in dark mode | Must Have |
| FR-1.7 | Card shows "Last updated" timestamp | Should Have |
| FR-1.8 | Count auto-refreshes if form context changes (e.g., user navigates to different record) | Should Have |

#### FR-2: CSV/Excel Export (Code Page Enhancement)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-2.1 | Export button in Grid view toolbar downloads CSV of visible documents | Must Have |
| FR-2.2 | CSV includes: Document Name, Type, Similarity %, Relationship Type, Parent Entity, Modified Date | Must Have |
| FR-2.3 | Export respects active filters (threshold, document types) | Must Have |
| FR-2.4 | CSV supports 500+ rows without browser issues | Should Have |
| FR-2.5 | Export filename includes source document name and date | Should Have |

#### FR-3: Grid View Enhancements (Code Page)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-3.1 | Quick search/filter text input to filter by document name | Should Have |
| FR-3.2 | Row click/selection highlights corresponding node if user switches to Graph view | Should Have |
| FR-3.3 | Column resize and reorder | Could Have |

#### FR-4: Shared Force Layout Hook (`@spaarke/ui-components`)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-4.1 | Extract `useForceSimulation` hook into `@spaarke/ui-components` | Must Have |
| FR-4.2 | Hook uses synchronous pre-computation (no tick-by-tick state updates) | Must Have |
| FR-4.3 | Support hub-spoke mode (source node pinned at center) for relationship graphs | Must Have |
| FR-4.4 | Support peer-mesh mode (no central node) for future SemanticSearch adoption | Should Have |
| FR-4.5 | Configurable force parameters (charge strength, link distance, collision radius) with sensible defaults | Must Have |
| FR-4.6 | Include shared viewport fitting utility (bounding box → scale/translate) | Should Have |

#### FR-5: BFF API Enhancement — Count-Only Fast Path

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-5.1 | Add `countOnly` query parameter to `GET /api/ai/visualization/related/{documentId}` | Must Have |
| FR-5.2 | When `countOnly=true`, skip graph topology computation, return only `metadata` with empty `nodes[]` and `edges[]` | Must Have |
| FR-5.3 | Count-only response time target: < 200ms | Should Have |

### Non-Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| NFR-1 | All components must support dark mode (Fluent UI v9 design tokens) | Must Have |
| NFR-2 | Code Page uses React 18 (bundled); PCF uses React 16 (platform-provided) | Must Have |
| NFR-3 | RelationshipCountCard follows callback-based prop pattern (no service imports) | Must Have |
| NFR-4 | New shared components added to `@spaarke/ui-components` with barrel + deep import support | Must Have |
| NFR-5 | `useForceSimulation` must work in both React 16 (PCF) and React 18 (Code Page) contexts | Must Have |
| NFR-6 | Graph renders instantly on open — no "Calculating layout..." spinner (sync pre-computation) | Must Have |
| NFR-7 | Grid view must handle 100+ documents without performance degradation | Should Have |
| NFR-8 | CSV export must support 500+ rows | Should Have |

---

## Technical Approach

### Component Design

#### RelatedDocumentCount PCF Control (New)

A lightweight PCF control for the Document main form. It hosts the shared `RelationshipCountCard` component and manages the API call + dialog lifecycle.

**Location:** `src/client/pcf/RelatedDocumentCount/`

```
RelatedDocumentCount/
├── RelatedDocumentCount.pcfproj
├── RelatedDocumentCount/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                         # PCF entry point (ReactControl)
│   ├── RelatedDocumentCount.tsx          # Main component
│   ├── hooks/
│   │   └── useRelatedDocumentCount.ts   # API call for count (countOnly=true)
│   └── types/
│       └── index.ts
├── package.json
├── tsconfig.json
└── featureconfig.json
```

**Control Properties:**

```xml
<property name="documentId" of-type="SingleLine.Text" usage="bound" required="true" />
<property name="tenantId" of-type="SingleLine.Text" usage="input" />
<property name="apiBaseUrl" of-type="SingleLine.Text" usage="input" />
<property name="cardTitle" of-type="SingleLine.Text" usage="input" default-value="RELATED DOCUMENTS" />
```

**Component wiring:**

```typescript
// RelatedDocumentCount.tsx — PCF component
import { RelationshipCountCard } from "@spaarke/ui-components/dist/components/RelationshipCountCard";
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";

const RelatedDocumentCount: React.FC<Props> = ({ documentId, tenantId, apiBaseUrl }) => {
    const { count, isLoading, error } = useRelatedDocumentCount(documentId, tenantId, apiBaseUrl);
    const [dialogOpen, setDialogOpen] = React.useState(false);

    const viewerUrl = `${webResourceBaseUrl}/sprk_documentrelationshipviewer?` +
        `documentId=${documentId}&tenantId=${tenantId}&theme=${isDark ? "dark" : "light"}`;

    return (
        <>
            <RelationshipCountCard
                count={count}
                isLoading={isLoading}
                error={error}
                onOpen={() => setDialogOpen(true)}
            />
            <FindSimilarDialog
                open={dialogOpen}
                onClose={() => setDialogOpen(false)}
                url={viewerUrl}
            />
        </>
    );
};
```

#### RelationshipCountCard (New Shared Component)

**Location:** `src/client/shared/Spaarke.UI.Components/src/components/RelationshipCountCard/`

**Follows shared library design principles:**
- Zero service dependencies — callback-based props only
- Context-agnostic — no PCF APIs, no Xrm globals
- Fluent v9 only — `makeStyles`, `tokens`, no custom CSS

```typescript
export interface IRelationshipCountCardProps {
    /** Card title (default: "RELATED DOCUMENTS") */
    title?: string;
    /** Number of related documents to display */
    count: number;
    /** Whether data is currently loading */
    isLoading?: boolean;
    /** Error message to display instead of count */
    error?: string | null;
    /** Called when user clicks the card (consumer handles navigation) */
    onOpen: () => void;
    /** Optional "last updated" timestamp */
    lastUpdated?: Date;
}
```

#### useForceSimulation (New Shared Hook)

**Location:** `src/client/shared/Spaarke.UI.Components/src/hooks/useForceSimulation.ts`

```typescript
export interface ForceSimulationOptions {
    /** Number of synchronous ticks to pre-compute (default: 300) */
    ticks?: number;
    /** Charge strength — negative = repulsion (default: -800) */
    chargeStrength?: number;
    /** Link distance multiplier (default: 400) */
    linkDistanceMultiplier?: number;
    /** Collision radius (default: 60) */
    collisionRadius?: number;
    /** Mode: "hub-spoke" pins source at center; "peer-mesh" has no central node */
    mode?: "hub-spoke" | "peer-mesh";
    /** Center position for hub node (default: { x: 0, y: 0 }) */
    center?: { x: number; y: number };
}

export interface ForceNode {
    id: string;
    isSource?: boolean;
    // ... consumer extends with own data
}

export interface ForceEdge {
    source: string;
    target: string;
    similarity: number;
}

export interface ForceSimulationResult<N extends ForceNode> {
    /** Nodes with computed x/y positions */
    positionedNodes: Array<N & { x: number; y: number }>;
    /** Viewport fitting transform */
    fitView: { x: number; y: number; scale: number };
}

/**
 * Synchronous force-directed layout computation.
 * Runs all ticks before returning — no async state updates, no spinner.
 */
export function useForceSimulation<N extends ForceNode>(
    nodes: N[],
    edges: ForceEdge[],
    options?: ForceSimulationOptions
): ForceSimulationResult<N>;
```

**Key design decisions:**
- Returns positioned nodes in a single synchronous pass — no `isSimulating` state
- Includes viewport fitting computation (bounding box → scale/translate)
- `hub-spoke` mode pins the source node at center with `fx`/`fy` (for DocumentRelationshipViewer)
- `peer-mesh` mode has no pinned nodes (for future SemanticSearch)
- Works with React 16 and React 18 (uses `useMemo`, no version-specific APIs)

#### Code Page Enhancements

**New Components/Services:**

| Component | Purpose | Key Implementation Details |
|-----------|---------|---------------------------|
| `CsvExportService.ts` | Export grid data to CSV | Blob + anchor download, UTF-8 BOM for Excel |
| Export button in toolbar | Trigger CSV download | Fluent v9 ToolbarButton, visible in Grid view only |
| Quick search input | Filter by document name | Text input in toolbar, filters grid rows client-side |

**Modified Components:**

| Component | Changes |
|-----------|---------|
| `App.tsx` | Add Export button to toolbar (Grid view only), add quick search input |
| `RelationshipGrid.tsx` | Accept `onRowSelect` callback, accept search filter prop, expose filtered rows for export |
| `DocumentGraph.tsx` | Migrate from async `useForceLayout` to sync `useForceSimulation` from shared lib |

### Data Flow

#### Document Form → Count Card → Full Viewer

```
User opens Document record (e.g., Contract_v2.pdf)
    │
    ▼
Document Main Form loads
    │
    ├─► RelatedDocumentCount PCF initializes
    │   │
    │   ├─► Calls: GET /api/ai/visualization/related/{docId}?countOnly=true&tenantId=...
    │   │   Response (~50-200ms): { nodes: [], edges: [], metadata: { totalResults: 10, ... } }
    │   │
    │   ▼
    │   RelationshipCountCard renders: "RELATED DOCUMENTS [10] [↗]"
    │
    │   ... User clicks card ...
    │   │
    │   ▼
    │   FindSimilarDialog opens (85vw x 85vh iframe)
    │   │
    │   ├─► Code Page loads: sprk_documentrelationshipviewer?documentId=...&tenantId=...
    │   │
    │   ├─► Authenticates via @spaarke/auth
    │   │
    │   ├─► Calls: GET /api/ai/visualization/related/{docId}?threshold=0.65&limit=25
    │   │   Response (~300-800ms): { nodes: [...], edges: [...], metadata: {...} }
    │   │
    │   ├─► useForceSimulation runs 300 ticks synchronously (~20-50ms)
    │   │
    │   ▼
    │   @xyflow/react renders fully-positioned graph INSTANTLY (no spinner)
    │   User sees: Graph view with toolbar (Graph|Grid toggle, Filters, Search, Export)
```

#### CSV Export Data Flow

```
User is in Grid view, clicks "Export CSV" button
    │
    ▼
App.tsx calls CsvExportService.exportToCsv(filteredNodes)
    │
    ├─► Converts nodes to CSV rows (respecting active filters)
    │     Columns: Document Name, Type, Similarity %, Relationship, Parent, Modified
    │
    ├─► Creates Blob with UTF-8 BOM (Excel compatibility)
    │
    └─► Triggers browser download via anchor element
          Filename: "related-documents-{sourceDocName}-{date}.csv"
```

---

## User Interface Design

### Document Main Form — Related Document Count

```
┌─────────────────────────────────────────────────────────────────────┐
│ Document: Contract_v2.pdf                                            │
│ ═══════════════════════════════════════════════════════════════════  │
│                                                                      │
│  General │ Timeline │ Related │                                      │
│  ────────────────────────────                                        │
│                                                                      │
│  ┌──────────────────────────────────────────┐                       │
│  │ Document Details                          │                       │
│  │ ...                                       │                       │
│  └──────────────────────────────────────────┘                       │
│                                                                      │
│  ┌──────────────────────────────────────────┐                       │
│  │ ┌──────────────────────────────────────┐ │                       │
│  │ │ [📊] RELATED DOCUMENTS          [↗]  │ │  ← RelatedDocumentCount PCF
│  │ │                                      │ │
│  │ │               10                     │ │  ← Count from API
│  │ │                                      │ │
│  │ │      Last updated: just now          │ │
│  │ └──────────────────────────────────────┘ │                       │
│  └──────────────────────────────────────────┘                       │
│                                                                      │
│  ┌──────────────────────────────────────────┐                       │
│  │ AI Summary                                │                       │
│  │ ...                                       │                       │
│  └──────────────────────────────────────────┘                       │
└─────────────────────────────────────────────────────────────────────┘
         │
         │ User clicks card
         ▼
┌─────────────────────────────────────────────────────────────────────┐
│ FindSimilarDialog (85vw x 85vh)                                   X │
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │ DocumentRelationshipViewer Code Page                            │ │
│ │                                                                 │ │
│ │ [Graph] [Grid]  |  [Filter] [🔍 Search...]  [📥 Export]       │ │
│ │ ─────────────────────────────────────────────────────────────── │ │
│ │                                                                 │ │
│ │     ┌───────┐                                                   │ │
│ │     │Source │──── 92% ────┌──────────┐                         │ │
│ │     │ Doc   │             │Contract_2│                         │ │
│ │     └───────┘──── 78% ────└──────────┘                         │ │
│ │         │                                                       │ │
│ │         └──── 71% ────┌──────────┐                             │ │
│ │                        │Email_Thrd│                             │ │
│ │                        └──────────┘                             │ │
│ │                                                                 │ │
│ │ 12 related documents · 18 edges · 142ms · v1.1.0              │ │
│ └─────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### Grid View with Export (Enhanced)

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Toolbar                                                                   │
│  [Graph] [Grid*]   |  [Filter]  [🔍 Search documents...]  [📥 Export]   │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│ ┌──────────────────┬──────────────┬───────────┬──────────┬──────────┐   │
│ │ Document       ▼ │ Relationship │ Similarity│ Type     │ Modified │   │
│ ├──────────────────┼──────────────┼───────────┼──────────┼──────────┤   │
│ │ 📄 Contract_v2   │ Same matter  │ 92%       │ Contract │ Jan 15   │   │
│ │ 📄 Invoice_Q4    │ Same matter  │ 78%       │ Invoice  │ Jan 12   │   │
│ │ 📧 Email_Thread  │ Same thread  │ 71%       │ Email    │ Jan 10   │   │
│ │ 📄 Report_2024   │ Semantic     │ 65%       │ Report   │ Jan 08   │   │
│ └──────────────────┴──────────────┴───────────┴──────────┴──────────┘   │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│ Footer: 12 related documents · 18 edges · 142ms · v1.1.0                │
└──────────────────────────────────────────────────────────────────────────┘
```

### CSV Export Format

```csv
Document Name,Document Type,Similarity %,Relationship Type,Parent Entity,Modified Date,Document ID
"Contract_v2.pdf","Contract",92,"Same matter","Smith v. Jones","2026-01-15","abc123..."
"Invoice_Q4.pdf","Invoice",78,"Same matter","Smith v. Jones","2026-01-12","def456..."
"Email_Thread.msg","Email",71,"Same thread","","2026-01-10","ghi789..."
```

---

## Graph Visualization Audit (Current State)

Three separate implementations currently use graph/network visualization:

| Component | Graph Rendering | Force Layout | React | Node Visual | Edge Source |
|-----------|----------------|-------------|-------|-------------|-------------|
| **DocumentRelationshipViewer Code Page** | `@xyflow/react` v12 | `d3-force` v3 (async tick-by-tick) | 19 | Rich cards (icons, badges, actions) | Server-computed (BFF API) |
| **DocumentRelationshipViewer PCF** | `react-flow-renderer` v10 | `d3-force` v3 (async tick-by-tick) | 16 | Rich cards (duplicated) | Server-computed (BFF API) |
| **SemanticSearch Code Page** | Raw SVG | `d3-force` v3 (sync 300-tick pre-compute) | 19 | Simple colored circles | Client-computed (pairwise metadata) |
| **PlaybookBuilder Code Page** | `@xyflow/react` v12 | None (static DAG) | 19 | Custom step nodes | Static definition |

**What this project standardizes:**
- `@xyflow/react` v12 for rendering (rich React component nodes, built-in interactions)
- `d3-force` v3 with synchronous pre-computation for layout (instant graph, no spinner)
- Shared `useForceSimulation` hook in `@spaarke/ui-components`

**Migration path for SemanticSearch** (future project):
1. Replace raw SVG with `@xyflow/react` — render document cards instead of circles
2. Replace `useSimilarityProjection` + inline `forceSimulation` with shared `useForceSimulation` hook
3. Keep client-side similarity computation (different data source, but same rendering)

---

## Applicable ADRs and Constraints

### Architecture Decision Records

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-006 | Anti-legacy-JS: PCF for form controls, Code Pages for dialogs | Code Page for full viewer dialog; PCF for count card on form |
| ADR-012 | Shared Component Library | RelationshipCountCard + useForceSimulation in `@spaarke/ui-components` |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9 components and design tokens; dark mode required |
| ADR-022 | PCF Platform Libraries | PCF: React 16 platform-provided; Code Page: React 18 bundled |

### Key Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| Code Page uses React 18 `createRoot()` | ADR-006, ADR-022 | Code Page bundles its own React 18; opened as webresource dialog |
| PCF uses React 16 platform-provided | ADR-022 | RelatedDocumentCount PCF uses deep imports from shared lib |
| Shared components: callback-based props | ADR-012 | RelationshipCountCard accepts count/onOpen/error — no service deps |
| Shared hooks must be React 16 compatible | ADR-022 | `useForceSimulation` uses `useMemo` only — no React 18-specific APIs |
| Fluent UI v9 only | ADR-021 | No Fluent v8 components; use design tokens for colors |
| Dark mode required | ADR-021 | All styling via tokens; Code Page supports `theme=dark` URL param |
| PCF deep imports from shared lib | ADR-012 | Avoid barrel import (Lexical/jsx-runtime issue); use `dist/components/{Name}` |

---

## Dependencies

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

### External Dependencies

| Dependency | Description |
|------------|-------------|
| BFF API | `GET /api/ai/visualization/related/{documentId}` — existing + new `countOnly` param |
| MSAL Authentication | Token acquisition via `@spaarke/auth` |
| Dataverse Web Resource | `sprk_documentrelationshipviewer` HTML web resource |
| Document Main Form | Form customization to place `RelatedDocumentCount` PCF in a section |

### Existing Assets (Already Implemented)

| Asset | Location | Status |
|-------|----------|--------|
| Code Page (Graph + Grid + Filters) | `src/client/code-pages/DocumentRelationshipViewer/` | Deployed |
| PCF Control (embedded graph) | `src/client/pcf/DocumentRelationshipViewer/` | Deployed (not modified) |
| FindSimilarDialog shared component | `@spaarke/ui-components` | Available |
| BFF API visualization endpoint | `Sprk.Bff.Api` | Deployed (needs `countOnly` param) |

---

## Success Criteria

### Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|--------------|
| AC-1 | Document main form shows related document count within 200ms of form load | Manual test: open Document record, observe card |
| AC-2 | Clicking count card opens FindSimilarDialog with full viewer | Manual test: click card, verify dialog opens |
| AC-3 | Graph renders instantly on dialog open — no "Calculating layout..." spinner | Manual test: open viewer, observe instant graph |
| AC-4 | CSV export downloads file with all filtered documents from Grid view | Manual test: export, open in Excel |
| AC-5 | Export respects active filters (threshold, document types) | Manual test: apply filters, export, verify subset |
| AC-6 | Quick search filters grid rows by document name | Manual test: type text, verify filtering |
| AC-7 | `useForceSimulation` hook added to `@spaarke/ui-components` and builds cleanly | `npm run build` in shared library |
| AC-8 | `RelationshipCountCard` added to `@spaarke/ui-components` and builds cleanly | `npm run build` in shared library |
| AC-9 | All existing functionality (Graph, Grid, Filters) continues working | Regression test |
| AC-10 | All components render correctly in dark mode | Manual test: toggle dark mode |

### Performance Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Count card display (form load) | < 200ms from API (countOnly) | Browser DevTools network tab |
| Graph layout computation (25 nodes, sync) | < 50ms | `performance.now()` around `sim.tick(300)` |
| Graph first paint after data fetch | < 100ms (sync layout + @xyflow render) | Browser DevTools |
| CSV export (500 docs) | < 2000ms | Manual timing |
| Grid view render (100 docs) | < 1000ms | Browser DevTools |

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| `countOnly` API param requires BFF changes | Medium | Blocks count card | Can fall back to `?limit=1` initially (still returns `metadata.totalResults`) |
| Sync pre-computation slow for very large graphs (100+ nodes) | Low | Delayed first paint | Cap at 300 ticks; for 100 nodes ~100ms is acceptable |
| `useForceSimulation` React 16 compatibility | Low | PCF build fails | Hook uses only `useMemo` — no React 18 APIs |
| CSV export blocked by browser popup blocker | Low | Export fails | Use standard blob/anchor pattern (not window.open) |
| Shared lib build breaks from new components | Low | Consumer builds fail | Test barrel export + deep import paths before merge |

---

## Out of Scope

The following items are explicitly out of scope for this project:

- **SemanticSearch migration to @xyflow** — Future project; this project establishes the shared hook and pattern
- **Refactoring existing DocumentRelationshipViewer PCF** — The duplicated PCF remains as-is; no changes
- **Inline editing** in Grid view
- **Multi-select export** (export only filtered view, not selected rows)
- **Real-time updates** (data fetched on load only)
- **Mobile-optimized layouts** (responsive but desktop-first)
- **Semantic search within results** (deferred to ai-semantic-search-foundation-r1)

---

## Future Enhancements

### SemanticSearch Migration to @xyflow (Future Project)

The SemanticSearch Code Page (`src/client/code-pages/SemanticSearch/`) currently renders document similarity as simple colored circles in raw SVG. A future project will migrate it to use:
- `@xyflow/react` v12 for rendering — rich document card nodes matching the DocumentRelationshipViewer
- Shared `useForceSimulation` hook (established by this project) in `peer-mesh` mode
- Consistent document visualization UX across the platform

**Key difference preserved**: SemanticSearch computes similarity client-side from search result metadata (`useSimilarityProjection`), while DocumentRelationshipViewer uses server-computed edges from the RAG pipeline. The rendering and layout are standardized; the data source remains different.

### Semantic Search Within Relationships

**Project**: `ai-semantic-search-foundation-r1`

Once deployed, the DocumentRelationshipViewer can add a search box that performs semantic search **within** the related documents subset via `POST /api/ai/search/semantic` with `scope: "documentIds"`.

---

## Related Projects

| Project | Relationship | Status |
|---------|--------------|--------|
| SemanticSearch @xyflow migration | Will adopt `useForceSimulation` hook and @xyflow pattern from this project | Future |
| `ai-semantic-search-foundation-r1` | Provides API for future semantic search in graph | Planned |
| `ai-semantic-search-ui-r2` | Standalone search control (separate from this project) | Planned |

---

## Glossary

| Term | Definition |
|------|------------|
| **Code Page** | React 18 standalone HTML web resource opened via `navigateTo({ pageType: "webresource" })` |
| **PCF** | Power Apps Component Framework — field-bound React 16 control on Dataverse forms |
| **FindSimilarDialog** | Shared component in `@spaarke/ui-components` — iframe dialog shell for opening Code Page |
| **RelationshipCountCard** | Shared component — displays count with drill-through callback, zero service deps |
| **useForceSimulation** | Shared hook — synchronous d3-force layout computation, hub-spoke and peer-mesh modes |
| **RAG** | Retrieval-Augmented Generation — AI pipeline that discovers document relationships |
| **BFF API** | Backend-for-Frontend API serving visualization data |
| **Design Token** | Fluent UI v9 semantic color/spacing variable (e.g., `tokens.colorBrandBackground`) |
| **Hub-spoke** | Graph layout where one source node is pinned at center, related nodes orbit around it |
| **Peer-mesh** | Graph layout where all nodes are equal peers, no central node (used by SemanticSearch) |
| **Sync pre-computation** | Running all d3-force simulation ticks before first render (vs. async tick-by-tick) |

---

## Appendix

### A. API Contract Reference

**Endpoint**: `GET /api/ai/visualization/related/{documentId}`

**Query Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `tenantId` | string | Required. Tenant identifier for API routing |
| `threshold` | number | Optional. Minimum similarity score (0.0-1.0) |
| `limit` | number | Optional. Maximum results to return |
| `depth` | number | Optional. Graph traversal depth limit |
| `documentTypes` | string[] | Optional. Filter by document types |
| `countOnly` | boolean | **NEW**. If true, skip graph topology computation, return only metadata |

**Response Schema**:
```typescript
interface DocumentGraphResponse {
    nodes: ApiDocumentNode[];      // Empty array if countOnly=true
    edges: ApiDocumentEdge[];      // Empty array if countOnly=true
    metadata: {
        sourceDocumentId: string;
        tenantId: string;
        totalResults: number;      // ← Used by count card
        threshold: number;
        searchLatencyMs: number;
        cacheHit: boolean;
    };
}
```

### B. Current File Inventory

**Code Page** (`src/client/code-pages/DocumentRelationshipViewer/src/`):
| File | Purpose |
|------|---------|
| `index.tsx` | Entry point — `createRoot()`, URL param parsing, FluentProvider |
| `App.tsx` | Main component — toolbar, view toggle, filter panel, content area |
| `components/DocumentGraph.tsx` | @xyflow/react v12 graph visualization |
| `components/RelationshipGrid.tsx` | Fluent v9 DataGrid with sortable columns |
| `components/ControlPanel.tsx` | Filter controls (similarity, depth, types) |
| `components/DocumentNode.tsx` | Custom graph node component |
| `components/DocumentEdge.tsx` | Custom graph edge component |
| `components/NodeActionBar.tsx` | Selected node actions (open record, view file) |
| `hooks/useVisualizationApi.ts` | API data fetching hook |
| `hooks/useForceLayout.ts` | Force-directed layout hook (to be replaced by shared useForceSimulation) |
| `services/authInit.ts` | @spaarke/auth initialization |
| `services/VisualizationApiService.ts` | API client |
| `types/api.ts` | API response types |
| `types/graph.ts` | Graph node/edge types |
| `types/auth.ts` | Auth types |

**Existing PCF Control** (`src/client/pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/`) — **Independent implementation, not modified by this project**:
| File | Purpose | Duplicated? |
|------|---------|-------------|
| `index.ts` | PCF entry point — `ReactControl<IInputs, IOutputs>` | PCF-specific |
| `DocumentRelationshipViewer.tsx` | Main component — header, graph, filter dropdown, footer (v1.0.31) | PCF-specific |
| `components/DocumentGraph.tsx` | Graph visualization | Yes — duplicated from Code Page |
| `components/DocumentNode.tsx` | Custom graph node component | Yes |
| `components/DocumentEdge.tsx` | Custom graph edge component | Yes |
| `components/ControlPanel.tsx` | Filter controls (similarity, depth, types) | Yes |
| `components/NodeActionBar.tsx` | Selected node actions | Yes |
| `components/RelationshipViewerModal.tsx` | Full-screen modal with sidebar (PCF-only, not used by Code Page) | PCF-specific |
| `hooks/useVisualizationApi.ts` | API data fetching hook | Yes |
| `hooks/useForceLayout.ts` | Force-directed layout hook | Yes |
| `services/VisualizationApiService.ts` | API client | Yes |
| `services/auth/MsalAuthProvider.ts` | MSAL auth provider | Yes |
| `authInit.ts` | @spaarke/auth initialization for PCF context | PCF-specific |
| `types/api.ts`, `graph.ts`, `auth.ts`, `index.ts` | Type definitions | Yes |

**SemanticSearch Code Page** (`src/client/code-pages/SemanticSearch/src/`) — **Not modified by this project, future migration target**:
| File | Purpose | Future Change |
|------|---------|---------------|
| `components/SearchResultsMap.tsx` | Raw SVG graph rendering + d3-force simulation | Replace with @xyflow + useForceSimulation |
| `hooks/useSimilarityProjection.ts` | Client-side pairwise similarity computation | Keep (different data source) |

**Shared Library** (`src/client/shared/Spaarke.UI.Components/`):
| Component | Purpose | Status |
|-----------|---------|--------|
| `FindSimilarDialog` | iframe dialog shell for Code Page viewer | Existing |
| `RelationshipCountCard` | Count display with drill-through callback | **NEW** |
| `useForceSimulation` | Shared sync d3-force layout hook | **NEW** |

### C. Related Documentation

- [Shared UI Components Guide](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md)
- [PCF Deployment Guide](../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)
- [Spaarke AI Architecture](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [RAG Architecture](../../docs/guides/RAG-ARCHITECTURE.md)

---

*Document Version History*

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-20 | Product Team | Initial draft (PCF-based architecture) |
| 2.0 | 2026-03-10 | Product Team | Updated to reflect current Code Page + PCF + shared lib architecture |
| 3.0 | 2026-03-10 | Product Team | Standardization decision (@xyflow + sync d3-force); added RelatedDocumentCount PCF for Document form; added shared useForceSimulation hook; added BFF countOnly API; scoped SemanticSearch migration as future project |
