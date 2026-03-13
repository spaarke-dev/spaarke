# Document Relationship Viewer — Developer Guide

> **Last Updated**: March 2026
> **Code Page**: `src/client/code-pages/DocumentRelationshipViewer/`
> **Dataverse Web Resource**: `sprk_documentrelationshipviewer`
> **PCF Companion**: `RelatedDocumentCount` (v1.20.2)

---

## Overview

The Document Relationship Viewer is a **React 19 Code Page** that visualizes semantically related documents in four view modes: Grid, Graph, Network, and Timeline. It is opened as a Dataverse dialog from the RelatedDocumentCount PCF card and displays documents connected by AI-detected similarity, shared matter/project, email threads, and invoice relationships.

### Architecture at a Glance

```
┌─────────────────────────────────────────────────────────────┐
│  RelatedDocumentCount PCF (React 16, field-bound)           │
│  ├── useRelatedDocumentGraphData  → single API call         │
│  ├── RelationshipCountCard        → count + mini graph      │
│  └── FindSimilarDialog            → opens Code Page iframe  │
├─────────────────────────────────────────────────────────────┤
│  DocumentRelationshipViewer Code Page (React 19, standalone)│
│  ├── App.tsx                      → orchestrator            │
│  ├── 4 view modes                 → Grid/Graph/Network/Time │
│  ├── FilePreviewDialog            → document preview        │
│  ├── ControlPanel                 → filters & settings      │
│  └── useVisualizationApi          → data fetching           │
├─────────────────────────────────────────────────────────────┤
│  BFF API  GET /api/ai/visualization/related/{documentId}    │
│  └── Azure AI Search + Dataverse metadata                   │
└─────────────────────────────────────────────────────────────┘
```

---

## Table of Contents

1. [Project Structure](#project-structure)
2. [Entry Point and Auth Bootstrap](#entry-point-and-auth-bootstrap)
3. [App Component — State and Routing](#app-component)
4. [View Modes](#view-modes)
   - [Grid View](#grid-view)
   - [Graph View](#graph-view)
   - [Network View](#network-view)
   - [Timeline View](#timeline-view)
5. [Shared Components from @spaarke/ui-components](#shared-components)
6. [FilePreviewDialog](#filepreviewdialog)
7. [Client-Side Filtering Pipeline](#client-side-filtering-pipeline)
8. [Preview URL Caching](#preview-url-caching)
9. [API Contract](#api-contract)
10. [Webpack Configuration](#webpack-configuration)
11. [Build and Deploy](#build-and-deploy)
12. [PCF Companion — RelatedDocumentCount](#pcf-companion)
13. [Troubleshooting](#troubleshooting)
14. [Applying to Semantic Search Code Page](#applying-to-semantic-search-code-page)

---

## 1. Project Structure

```
src/client/code-pages/DocumentRelationshipViewer/
├── src/
│   ├── index.tsx                          # Entry point (createRoot, auth, theme)
│   ├── App.tsx                            # Root component (toolbar, views, filters)
│   ├── components/
│   │   ├── DocumentGraph.tsx              # @xyflow/react force-directed graph
│   │   ├── DocumentNode.tsx               # Custom node renderer (cards, badges)
│   │   ├── DocumentEdge.tsx               # Custom edge renderer (colored lines)
│   │   ├── RelationshipGrid.tsx           # Fluent v9 DataGrid table
│   │   ├── RelationshipNetwork.tsx        # SVG d3-force live simulation
│   │   ├── RelationshipTimeline.tsx       # SVG date vs similarity scatter
│   │   ├── ControlPanel.tsx               # Right-side settings panel
│   │   └── FilePreviewDialog.tsx          # Document preview modal
│   ├── hooks/
│   │   └── useVisualizationApi.ts         # API data fetching hook
│   ├── services/
│   │   ├── authInit.ts                    # @spaarke/auth initialization
│   │   ├── FilePreviewServiceAdapter.ts   # Preview URL + file operations
│   │   ├── CsvExportService.ts            # CSV export for grid rows
│   │   └── VisualizationApiService.ts     # HTTP client for BFF API
│   └── types/
│       ├── api.ts                         # API response interfaces
│       └── graph.ts                       # @xyflow/react node/edge types
├── index.html                             # HTML shell (100vh, no scrollbars)
├── webpack.config.js                      # Bundles everything (React 19 included)
├── build-webresource.ps1                  # Inlines bundle.js into HTML
├── package.json                           # Dependencies and scripts
├── tsconfig.json
└── out/
    ├── bundle.js                          # Step 1 output (intermediate)
    └── sprk_documentrelationshipviewer.html  # Step 2 output (DEPLOYABLE)
```

---

## 2. Entry Point and Auth Bootstrap

**File:** `src/index.tsx`

The entry point handles three concerns: URL parameter unwrapping, authentication initialization, and theme application.

### URL Parameter Unwrapping

Dataverse wraps query params in a `?data=encodedString` envelope. The entry point unwraps this:

```typescript
// Dataverse passes: ?data=documentId%3Dabc%26theme%3Ddark
const rawData = new URLSearchParams(window.location.search).get("data") ?? "";
const params = new URLSearchParams(rawData);
// Now: params.get("documentId") === "abc", params.get("theme") === "dark"
```

Falls back to Xrm form context if no URL params provided (for testing in Dataverse forms).

### Auth Initialization

```typescript
import { initializeAuth, getAuthProvider } from "./services/authInit";

await initializeAuth(apiBaseUrl);
const authProvider = getAuthProvider();
const tenantId = tenantId || authProvider?.getTenantId?.();
```

Uses `@spaarke/auth` (MSAL-based) for token acquisition. Must complete before any API calls.

### Theme Application

```typescript
const isDark = theme === "dark" ||
  (!theme && window.matchMedia("(prefers-color-scheme: dark)").matches);

createRoot(root).render(
  <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
    <App params={params} isDark={isDark} />
  </FluentProvider>
);
```

---

## 3. App Component

**File:** `src/App.tsx`

The App component is the root orchestrator managing:

- **View mode** toggling (grid/graph/network/timeline)
- **Toolbar** (refresh, search, export, view buttons, filter toggle)
- **Filter state** via ControlPanel
- **FilePreviewDialog** shared across all views
- **Preview URL caching** with LRU eviction

### Key State

| State | Type | Default | Purpose |
|-------|------|---------|---------|
| `viewMode` | `"grid" \| "graph" \| "network" \| "timeline"` | `"grid"` | Active view |
| `showFilterPanel` | `boolean` | `true` | Settings panel visibility |
| `filters` | `FilterSettings` | See ControlPanel defaults | Threshold, depth, types |
| `searchQuery` | `string` | `""` | Client-side text search |
| `selectedNode` | `DocumentNodeData \| null` | `null` | Selected for preview |
| `previewUrlCacheRef` | `Map<string, { url, ts }>` | Empty | LRU cache |

### Toolbar Layout

```
[Refresh] | [SearchBox (expandable)] [Export (grid only)] | [Grid] [Graph] [Network] [Timeline] | [Filter/Settings]
```

### Footer

```
{N} related documents · {M} connections · {latency}ms {cache badge} · v1.0.0
```

---

## 4. View Modes

### Grid View

**File:** `src/components/RelationshipGrid.tsx`

Fluent UI v9 `DataGrid` with 7 columns:

| Column | Width | Content |
|--------|-------|---------|
| Document | 600px | Name + file icon + "Source" badge |
| Relationship | 160px | Colored badges (semantic, same_matter, etc.) |
| Similarity | 100px | Percentage with color coding |
| Type | 100px | Document type or file extension |
| Parent Entity | 180px | Parent record name |
| Modified | 130px | Last modified date |
| Preview | 60px | Eye icon button |

**Features:**
- Filters out hub nodes (matter/project/invoice/email) — shows documents only
- Case-insensitive search on document name
- Sortable, resizable columns
- Row click → FilePreviewDialog
- Row hover → prefetch preview URL
- CSV export via CsvExportService

### Graph View

**File:** `src/components/DocumentGraph.tsx`

Uses **@xyflow/react v12** with the shared `useForceSimulation` hook from `@spaarke/ui-components`.

**Key Architecture:**

```typescript
import { useForceSimulation } from "@spaarke/ui-components/dist/hooks/useForceSimulation";

// Synchronous force layout — positions resolved before first render
const { nodes: layoutNodes, edges: layoutEdges } = useForceSimulation(
  forceNodes, forceEdges, {
    mode: "hub-spoke",
    chargeStrength: -1000,
    linkDistanceMultiplier: 400,
    collisionRadius: 100,
  }
);
```

**Custom Node Types** (`DocumentNode.tsx`):
- **Source document** — Blue background, "Source" badge
- **Related document** — White card with relationship badges
- **Parent hub** (matter/project/invoice/email) — Green background, entity icon
- **Orphan file** — Dashed border, "File only" badge
- **Compact mode** — Circular icons (40x40px) for dense graphs

**Custom Edge Types** (`DocumentEdge.tsx`):
- Stroke width scales by similarity (0.75–1.5px)
- Color-coded by relationship type
- Label shows relationship + similarity percentage

**Important Configuration:**
- Auto-fits view on node count change (100ms debounce)
- Uses `ReactFlowProvider` wrapper (required for `useReactFlow()`)
- Handles positioned on Left (target) and Right (source) for L→R flow
- No shadows on cards (removed for cleaner look)

### Network View

**File:** `src/components/RelationshipNetwork.tsx`

SVG-based d3-force visualization with **live simulation** (nodes continue to move after render).

**Key Differences from Graph View:**
- Uses raw `d3-force` (not @xyflow/react)
- Live simulation with interactive dragging
- Pan and zoom via mouse events
- Node radius scales by similarity score
- No card UI — just colored circles
- Legend overlay for relationship types

**Simulation Parameters:**
- Initial warm-up: 300 ticks
- Charge strength: -600
- Link distance: 100–350 (scales by 1 - similarity)
- Center strength: 0.015

### Timeline View

**File:** `src/components/RelationshipTimeline.tsx`

SVG scatter plot with:
- **X-axis:** Document modified date
- **Y-axis:** Similarity score (0–100%)
- **"No Date" strip** for documents without timestamps
- **Zoom** on X-axis via mouse wheel, double-click to reset
- Click opens FilePreviewDialog

---

## 5. Shared Components from @spaarke/ui-components

These components are imported from the shared library and can be reused in other code pages:

### useForceSimulation Hook

```typescript
import { useForceSimulation } from "@spaarke/ui-components/dist/hooks/useForceSimulation";
```

**Purpose:** Synchronous force-directed layout calculation. Positions are resolved before the component renders (unlike d3-force live simulation).

**Parameters:**
- `nodes: ForceNode[]` — Nodes with id, optional initial position
- `edges: ForceEdge[]` — Edges with source/target ids
- `options: ForceLayoutOptions` — mode, chargeStrength, linkDistanceMultiplier, collisionRadius, ticks

**Modes:** `"hub-spoke"` (default), `"cluster"`, `"radial"`

Used by: DocumentGraph.tsx (graph view), MiniGraph.tsx (PCF preview)

### RelationshipCountCard

```typescript
import { RelationshipCountCard } from "@spaarke/ui-components/dist/components/RelationshipCountCard";
```

PCF card showing count + mini graph preview. Props: `count`, `isLoading`, `error`, `onOpen`, `onRefresh`, `lastUpdated`, `graphPreview`.

### MiniGraph

```typescript
import { MiniGraph } from "@spaarke/ui-components/dist/components/MiniGraph";
```

Compact SVG graph preview (used inside RelationshipCountCard). Uses `useForceSimulation` with 50 ticks for fast rendering.

### FindSimilarDialog

```typescript
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
```

Fluent v9 Dialog that renders an iframe to the Code Page URL. Used by the PCF to open the full viewer.

### FilePreviewDialog (shared interface)

The `IFilePreviewServices` interface is defined in the shared library. The Code Page implements it via `FilePreviewServiceAdapter.ts`.

---

## 6. FilePreviewDialog

**File:** `src/components/FilePreviewDialog.tsx`

Full-screen modal (85vw x 85vh) for document preview with toolbar actions.

### Dialog Flow

1. User clicks row (grid), node (graph), circle (network), or point (timeline)
2. App.tsx sets `selectedNode` state
3. Preview URL fetched (or served from cache)
4. Dialog opens with iframe showing document preview
5. Toolbar provides: Open File, Open Record, Email, Copy Link, Workspace toggle

### Preview URL Resolution

```typescript
// FilePreviewServiceAdapter.ts
const services = createFilePreviewServices(apiBaseUrl);
const url = await services.getDocumentPreviewUrl(documentId);
// GET /api/documents/{id}/preview-url → { previewUrl: "https://..." }
```

---

## 7. Client-Side Filtering Pipeline

Filtering happens **after** the API response, in three stages:

```
API Response (all nodes/edges)
  │
  ├── Stage 1: Relationship Type Filter
  │   └── Keep only edges matching selected relationship types
  │   └── Keep nodes that still have connections (or are source)
  │
  ├── Stage 2: Document Type + Connectivity Filter
  │   └── Keep only nodes matching selected document types
  │   └── Remove orphaned hub nodes with no remaining children
  │
  └── Stage 3: Search Query Filter (grid view only)
      └── Case-insensitive match on document name
```

**Important:** The `documentTypes` and `relationshipTypes` params are also passed to the API for server-side filtering. Client-side filtering is the fallback for immediate UI response when toggling checkboxes.

### ControlPanel Settings

| Setting | Range | Default | Sent to API? |
|---------|-------|---------|-------------|
| Similarity Threshold | 50–95% | 65% | Yes |
| Depth (Levels) | 1–3 | 1 | Yes |
| Max Nodes per Level | 10–50 | 25 | Yes |
| Document Types | Checkboxes | All selected | Yes (omitted when all) |
| Relationship Types | Checkboxes | All selected | Yes |

**Select All / Clear Links:** Each checkbox group has "Select All" and "Clear" links for quick toggling.

---

## 8. Preview URL Caching

App.tsx maintains an LRU cache for preview URLs to avoid redundant API calls:

```typescript
const previewUrlCacheRef = useRef(new Map<string, { url: string; timestamp: number }>());

// Cache config
const CACHE_TTL = 10 * 60 * 1000;  // 10 minutes
const CACHE_MAX_SIZE = 50;          // LRU eviction

// Hover prefetch (grid view)
const handleRowHover = useCallback((documentId: string) => {
  const cache = previewUrlCacheRef.current;
  if (!cache.has(documentId)) {
    services.getDocumentPreviewUrl(documentId).then(url => {
      if (cache.size >= CACHE_MAX_SIZE) {
        // Evict oldest entry
        const oldest = [...cache.entries()].sort((a, b) => a[1].timestamp - b[1].timestamp)[0];
        cache.delete(oldest[0]);
      }
      cache.set(documentId, { url, timestamp: Date.now() });
    });
  }
}, [services]);
```

---

## 9. API Contract

### Endpoint

```
GET /api/ai/visualization/related/{documentId}
  ?tenantId={guid}
  &threshold=0.65
  &limit=25
  &depth=1
  &documentTypes=legal,financial       (omitted when all types selected)
  &relationshipTypes=semantic,same_matter
  &includeKeywords=true
  &includeParentEntity=true
```

### Response: `DocumentGraphResponse`

```typescript
interface DocumentGraphResponse {
  nodes: ApiDocumentNode[];
  edges: ApiDocumentEdge[];
  metadata: GraphMetadata;
}

interface ApiDocumentNode {
  id: string;
  type: "source" | "related" | "orphan" | "matter" | "project" | "invoice" | "email";
  depth: number;
  data: ApiDocumentNodeData;
}

interface ApiDocumentNodeData {
  label: string;
  documentType: string;
  fileType?: string;
  speFileId?: string;
  isOrphanFile?: boolean;
  similarity?: number;
  relationshipType?: string;
  relationshipLabel?: string;
  relationshipTypes?: string[];
  parentEntityName?: string;
  sharedKeywords?: string[];
  createdOn?: string;
  modifiedOn?: string;
}

interface ApiDocumentEdge {
  id: string;
  source: string;
  target: string;
  data: {
    similarity: number;
    relationshipType: string;
    relationshipLabel: string;
    sharedKeywords?: string[];
  };
}

interface GraphMetadata {
  sourceDocumentId: string;
  tenantId: string;
  totalResults: number;          // Used by PCF for count display
  threshold: number;
  depth: number;
  maxDepthReached: boolean;
  nodesPerLevel: Record<number, number>;
  searchLatencyMs: number;
  cacheHit: boolean;
}
```

### Node Types

| Type | Description | Visual |
|------|-------------|--------|
| `source` | The document being analyzed | Blue card, "Source" badge |
| `related` | Semantically similar document | White card, relationship badges |
| `orphan` | File without Dataverse record | Dashed border, "File only" badge |
| `matter` | Parent matter entity | Green hub, Matter icon |
| `project` | Parent project entity | Green hub, Project icon |
| `invoice` | Parent invoice entity | Green hub, Invoice icon |
| `email` | Parent email entity | Green hub, Email icon |

### Relationship Types

| Type | Color | Label |
|------|-------|-------|
| `semantic` | Brand blue | Semantically Similar |
| `same_matter` | Green | Same Matter |
| `same_project` | Green | Same Project |
| `same_email` | Warning orange | Same Email Thread |
| `same_invoice` | Berry | Same Invoice |
| `same_container` | Neutral | Same Container |

---

## 10. Webpack Configuration

**File:** `webpack.config.js`

### Key Settings

```javascript
module.exports = {
  entry: "./src/index.tsx",
  output: { filename: "bundle.js", path: path.resolve(__dirname, "out") },
  mode: "production",

  module: {
    rules: [
      {
        test: /\.tsx?$/,
        loader: "esbuild-loader",      // 10x faster than ts-loader
        options: { target: "es2020" },
      },
      {
        test: /\.css$/,                 // Required for @xyflow/react styles
        use: ["style-loader", "css-loader"],
      },
    ],
  },

  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: [
      path.resolve(__dirname, "node_modules"),  // THIS CODE PAGE'S node_modules FIRST
      "node_modules",                            // Fallback
    ],
    alias: {
      "@spaarke/auth": path.resolve(__dirname, "../../shared/Spaarke.Auth.Client/dist"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../shared/Spaarke.UI.Components/dist"),
    },
  },

  // Force single bundle (required for build-webresource.ps1 inlining)
  optimization: { splitChunks: false },
  plugins: [new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 })],
};
```

### Critical: resolve.modules Order

The `resolve.modules` array **must list the code page's own `node_modules` first**. This prevents the dual React problem where @xyflow/react resolves to the shared library's React 18 while the code page uses React 19:

```
// WRONG — causes "Invalid hook call" errors
modules: ["node_modules"]

// CORRECT — forces single React instance
modules: [path.resolve(__dirname, "node_modules"), "node_modules"]
```

### CSS Support for @xyflow/react

@xyflow/react v12 imports CSS files internally. The `css-loader` + `style-loader` chain is required:

```javascript
{ test: /\.css$/, use: ["style-loader", "css-loader"] }
```

Without this, the build fails with "Module parse failed: Unexpected token" on `.css` imports.

---

## 11. Build and Deploy

### Two-Step Build Pipeline

**Step 1: Webpack Build**
```bash
cd src/client/code-pages/DocumentRelationshipViewer
npm run build
# Output: out/bundle.js (~1 MB)
```

**Step 2: Inline into HTML**
```bash
powershell -File build-webresource.ps1
# Output: out/sprk_documentrelationshipviewer.html (~1 MB)
```

The PowerShell script replaces `<script src="bundle.js"></script>` in `index.html` with the full inlined bundle content.

### Deploy to Dataverse

1. Open [make.powerapps.com](https://make.powerapps.com)
2. Navigate to environment → Solutions → find solution containing the web resource
3. Open `sprk_documentrelationshipviewer` web resource
4. Click **Choose File** → select `out/sprk_documentrelationshipviewer.html`
5. **Save** → **Publish**

### Quick Build Command

```bash
cd src/client/code-pages/DocumentRelationshipViewer && npm run build && powershell -File build-webresource.ps1
```

---

## 12. PCF Companion — RelatedDocumentCount

The `RelatedDocumentCount` PCF control (v1.20.2) serves as the entry point on Dataverse forms.

### Single API Call Optimization

The PCF uses a single `useRelatedDocumentGraphData` hook that extracts both the count (from `metadata.totalResults`) and graph preview data from one API call:

```typescript
// One hook replaces the old two-hook pattern (useRelatedDocumentCount + useRelatedDocumentGraphData)
const { count, nodes: graphNodes, edges: graphEdges, isLoading, error, lastUpdated, refetch } =
    useRelatedDocumentGraphData(documentId, tenantId, apiBaseUrl, isAuthReady);
```

### MiniGraph Performance

The PCF's MiniGraph uses 50 force simulation ticks (reduced from 80) for faster rendering with minimal visual difference.

### Version Bumping (5 Locations)

When updating the PCF version, all 5 files must match:

1. `RelatedDocumentCount/ControlManifest.Input.xml` — `version="X.Y.Z"`
2. `Solution/Controls/.../ControlManifest.xml` — `version="X.Y.Z"`
3. `Solution/solution.xml` — `<Version>X.Y.Z</Version>`
4. `Solution/pack.ps1` — `$version = "X.Y.Z"`
5. `RelatedDocumentCount.tsx` — `data-pcf-version="X.Y.Z"`

### Build and Pack

```bash
cd src/client/pcf/RelatedDocumentCount/RelatedDocumentCount
npm run build
cp out/controls/RelatedDocumentCount/bundle.js ../Solution/Controls/sprk_Spaarke.Pcf.RelatedDocumentCount.RelatedDocumentCount/bundle.js
cd ../Solution
powershell -File pack.ps1
# Output: bin/SpaarkeRelatedDocumentCount_vX.Y.Z.zip
```

---

## 13. Troubleshooting

### Dual React Problem ("Invalid hook call")

**Symptom:** `Uncaught Error: Invalid hook call. Hooks can only be called inside the body of a function component.`

**Cause:** Two React instances loaded — one from the code page's `node_modules` and another from a dependency (e.g., @spaarke/ui-components or @xyflow/react).

**Fix:** Ensure `resolve.modules` in `webpack.config.js` lists the code page's `node_modules` first:

```javascript
resolve: {
  modules: [
    path.resolve(__dirname, "node_modules"),  // This code page's React FIRST
    "node_modules",
  ],
}
```

**Verify:** Check bundle for duplicate React by searching `bundle.js` for `"react"` module declarations.

### Graph Not Showing (Empty Canvas)

**Possible causes:**
1. **No CSS loader** — @xyflow/react needs `css-loader` + `style-loader` in webpack
2. **Container has zero height** — Ensure parent has explicit height (use `flex: 1` or `height: 100%`)
3. **useForceSimulation returns empty** — Check that input nodes have valid IDs and edges reference existing node IDs
4. **ReactFlowProvider missing** — `useReactFlow()` only works inside `<ReactFlowProvider>`

### Filters Not Working

**Check the 3-stage pipeline:**
1. **Relationship types** — Are edges being filtered correctly? Check `edges.filter(e => selectedTypes.includes(e.data.relationshipType))`
2. **Document types** — API uses business types (legal, financial), not file extensions. If all types are selected, the param should be omitted
3. **Search query** — Only applies to grid view; check case-insensitive match

### Build Slow (>30 seconds)

**Switch to esbuild-loader** (already configured):
```javascript
{ test: /\.tsx?$/, loader: "esbuild-loader", options: { target: "es2020" } }
```

Enable webpack filesystem cache:
```javascript
cache: { type: "filesystem" }
```

### Preview URL Not Loading

1. **Auth not initialized** — Ensure `initializeAuth()` completes before API calls
2. **CORS** — Preview URLs from SharePoint may need the BFF as a proxy
3. **Cache stale** — TTL is 10 minutes; clear cache by refreshing the page
4. **404 from API** — Document may not have a preview available (non-previewable file type)

### Shared Library Build Errors

The shared library (`@spaarke/ui-components`) has pre-existing type errors in some components. To build only specific changed files:

```bash
cd src/client/shared/Spaarke.UI.Components
npx esbuild src/components/MiniGraph/MiniGraph.tsx --outfile=dist/components/MiniGraph/MiniGraph.js --format=esm --loader:.tsx=tsx --sourcemap --target=es2020
```

### Solution ZIP Missing Changes

After building the PCF, you must **copy the bundle** to the Solution directory before packing:

```bash
cp out/controls/RelatedDocumentCount/bundle.js ../Solution/Controls/sprk_Spaarke.Pcf.RelatedDocumentCount.RelatedDocumentCount/bundle.js
cd ../Solution && powershell -File pack.ps1
```

---

## 14. Applying to Semantic Search Code Page

This section documents patterns and components that can be reused when upgrading the Semantic Search HTML code page.

### Components to Reuse

| Component | Import Path | Purpose |
|-----------|------------|---------|
| `useForceSimulation` | `@spaarke/ui-components/dist/hooks/useForceSimulation` | Synchronous graph layout |
| `FilePreviewDialog` | Local implementation (see pattern) | Document preview modal |
| `FilePreviewServiceAdapter` | Local implementation (see pattern) | Auth bridge for preview APIs |

### @xyflow/react Migration Checklist

If upgrading from a legacy graph library to @xyflow/react v12:

1. **Install dependencies:**
   ```bash
   npm install @xyflow/react react react-dom
   ```

2. **Add CSS support to webpack:**
   ```javascript
   { test: /\.css$/, use: ["style-loader", "css-loader"] }
   ```

3. **Add resolve.modules** to prevent dual React:
   ```javascript
   resolve: {
     modules: [path.resolve(__dirname, "node_modules"), "node_modules"]
   }
   ```

4. **Wrap in ReactFlowProvider:**
   ```tsx
   <ReactFlowProvider>
     <MyGraphComponent />
   </ReactFlowProvider>
   ```

5. **Use shared force simulation hook** (not @xyflow/react's built-in layout):
   ```typescript
   const { nodes, edges } = useForceSimulation(forceNodes, forceEdges, options);
   ```

6. **Register custom node/edge types:**
   ```typescript
   const nodeTypes = useMemo(() => ({ document: DocumentNode }), []);
   const edgeTypes = useMemo(() => ({ relationship: DocumentEdge }), []);
   ```

### Multi-View Pattern

The App.tsx pattern for supporting multiple view modes is reusable:

```typescript
type ViewMode = "grid" | "graph" | "network" | "timeline";
const [viewMode, setViewMode] = useState<ViewMode>("grid");

// Toolbar with toggle buttons
<ToggleButton checked={viewMode === "grid"} onClick={() => setViewMode("grid")}>Grid</ToggleButton>
<ToggleButton checked={viewMode === "graph"} onClick={() => setViewMode("graph")}>Graph</ToggleButton>

// Conditional rendering
{viewMode === "grid" && <RelationshipGrid ... />}
{viewMode === "graph" && <DocumentGraph ... />}
```

### Client-Side Filter Pattern

Reusable pattern for checkbox-based filtering with Select All / Clear:

```typescript
const [selectedTypes, setSelectedTypes] = useState<Set<string>>(new Set(ALL_TYPES));

const handleToggleType = (type: string) => {
  setSelectedTypes(prev => {
    const next = new Set(prev);
    next.has(type) ? next.delete(type) : next.add(type);
    return next;
  });
};

const handleSelectAll = () => setSelectedTypes(new Set(ALL_TYPES));
const handleClear = () => setSelectedTypes(new Set());
```

### Preview URL Caching Pattern

```typescript
const CACHE_TTL = 10 * 60 * 1000;
const CACHE_MAX = 50;
const cache = useRef(new Map<string, { url: string; timestamp: number }>());

const getPreviewUrl = async (id: string): Promise<string> => {
  const entry = cache.current.get(id);
  if (entry && Date.now() - entry.timestamp < CACHE_TTL) return entry.url;

  const url = await services.getDocumentPreviewUrl(id);
  if (cache.current.size >= CACHE_MAX) {
    const oldest = [...cache.current.entries()].sort((a, b) => a[1].timestamp - b[1].timestamp)[0];
    cache.current.delete(oldest[0]);
  }
  cache.current.set(id, { url, timestamp: Date.now() });
  return url;
};
```

### Webpack Template for New Code Pages

```javascript
const path = require("path");
const webpack = require("webpack");

module.exports = {
  entry: "./src/index.tsx",
  output: { filename: "bundle.js", path: path.resolve(__dirname, "out") },
  mode: "production",
  devtool: "source-map",
  cache: { type: "filesystem" },
  module: {
    rules: [
      { test: /\.tsx?$/, loader: "esbuild-loader", options: { target: "es2020" } },
      { test: /\.css$/, use: ["style-loader", "css-loader"] },
    ],
  },
  resolve: {
    extensions: [".tsx", ".ts", ".js"],
    modules: [path.resolve(__dirname, "node_modules"), "node_modules"],
    alias: {
      "@spaarke/auth": path.resolve(__dirname, "../../shared/Spaarke.Auth.Client/dist"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../shared/Spaarke.UI.Components/dist"),
    },
  },
  optimization: { splitChunks: false },
  plugins: [new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 })],
};
```

---

## Related Documentation

- [Shared UI Components Guide](SHARED-UI-COMPONENTS-GUIDE.md)
- [PCF Deployment Guide](PCF-DEPLOYMENT-GUIDE.md)
- [Code Page Deploy Skill](../../.claude/skills/code-page-deploy/SKILL.md)
- [ADR-006: PCF for forms, Code Pages for dialogs](../../docs/adr/ADR-006-pcf-over-webresources.md)
- [ADR-021: Fluent UI v9 Design System](../../docs/adr/ADR-021-fluent-design-system.md)
- [ADR-022: PCF Platform Libraries](../../docs/adr/ADR-022-pcf-platform-libraries.md)
