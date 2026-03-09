# AI Playbook Node Builder R5 — Design Document

> **Project**: Rebuild the Playbook Builder as a React 19 Code Page with @xyflow/react v12+
> **Branch**: `work/ai-playbook-node-builder-r5`
> **Predecessor**: `ai-playbook-node-builder-r4` (PCF control, React 16, react-flow-renderer v10)
> **Reference Implementation**: DocumentRelationshipViewer Code Page (@xyflow/react v12.8.3, React 19)
> **Date**: 2026-02-28

---

## 1. Executive Summary

The Playbook Builder is a visual node-based workflow designer that lets users compose AI analysis pipelines by dragging, connecting, and configuring nodes on a canvas. It was originally built as a PCF control (React 16, `react-flow-renderer` v10) embedded on the `sprk_analysisplaybook` Dataverse form.

This project rebuilds it as a **standalone React 19 Code Page** using **@xyflow/react v12+**, following the same architecture established by the AnalysisWorkspace and DocumentRelationshipViewer code pages. This unlocks modern React features, the latest xyflow capabilities, and eliminates the PCF framework constraints that limit the builder's potential.

### Why Now

- **Pre-release window** — no production users to migrate
- **Mock data cleanup required anyway** — all scope selectors (skills, knowledge, tools, models) use hardcoded fake data; replacing them touches every store
- **Proven code page pattern** — AnalysisWorkspace and DocumentRelationshipViewer already establish the full architecture (auth, Dataverse access, build pipeline, deployment)
- **@xyflow/react v12 already in use** — the DocumentRelationshipViewer code page (`spaarke-wt-ai-semantic-search-ui-r3`) already uses `@xyflow/react ^12.8.3` with React 19, proving the stack works in this codebase

---

## 2. Problem Statement

### 2.1 Architectural Mismatch

The Playbook Builder is **not a form field control**. It is a full-screen visual design workspace that manages an entire aggregate (playbook + nodes + edges + scopes). But it is wrapped in a PCF `StandardControl` interface that forces:

- **React 16** (platform-provided per ADR-022) — cannot use hooks-based xyflow API, concurrent rendering, Suspense, or transitions
- **Form field binding** — declares `canvasJson`, `playbookName`, `playbookDescription` as bound properties, but they are optional fallbacks; the control works entirely via WebAPI
- **PCF lifecycle overhead** — `init()`, `updateView()`, `getOutputs()`, `destroy()` add complexity for no benefit
- **react-flow-renderer v10** — legacy package, now renamed to `@xyflow/react`; v12+ features (sub-flows, hooks API, enhanced controls, layout algorithms) require React 18+

Per ADR-006: *"field-bound → PCF; standalone dialog → Code Page."* The Playbook Builder belongs in the Code Page category.

### 2.2 Mock Data Throughout

All scope selectors use hardcoded arrays with fake IDs that will never match Dataverse records at execution time:

| Store | Mock Data | Real Table |
|-------|-----------|------------|
| `scopeStore.ts` | `'skill-1'` through `'skill-6'` | `sprk_analysisskill` |
| `scopeStore.ts` | `'knowledge-1'` through `'knowledge-5'` | `sprk_aiknowledge` |
| `scopeStore.ts` | `'tool-1'` through `'tool-5'` | `sprk_analysistool` |
| `modelStore.ts` | `'50000000-...-000001'` through `'...-05'` | `sprk_aimodeldeployment` |

Additionally, the Properties panel has **no action selector** (`actionId` exists in the data type but has no UI picker) and **no output type selector**.

### 2.3 Deployment Friction

PCF controls require a multi-step deployment: `npm run build` → `pac pcf push` or solution import → publish customizations → clear form cache. Code pages deploy as a single HTML web resource upload — faster iteration, simpler CI/CD.

---

## 3. Solution Summary

Rebuild the Playbook Builder as a Code Page with three pillars:

1. **React 19 + @xyflow/react v12+** — modern hooks-based canvas with enhanced node visualization, sub-flow support, and built-in layout algorithms
2. **Real Dataverse lookups** — replace all mock data with `fetch()` calls to the Dataverse Web API for skills, knowledge, tools, actions, and model deployments
3. **Established code page patterns** — follow the AnalysisWorkspace architecture for auth (Xrm platform + MSAL fallback), build (Webpack 5 + inline HTML), and deployment

### Separation of Concerns (Unchanged)

```
Playbook Builder (Code Page)     →  Owns Dataverse CRUD at build time
  - Creates/updates sprk_playbooknode records
  - Reads scope tables (skills, knowledge, tools, actions, models)
  - Saves canvas JSON to sprk_analysisplaybook

BFF API                           →  Only reads at execution time
  - Reads sprk_playbooknode records
  - Resolves scopes via N:N tables
  - Executes nodes via PlaybookOrchestrationService
```

---

## 4. Existing State (PCF Control — R4)

### 4.1 Technology Stack

| Aspect | Current (R4 PCF) |
|--------|------------------|
| React | 16.14.0 (platform-provided) |
| Graph Library | react-flow-renderer 10.3.17 |
| State Management | Zustand 4.5.0 |
| UI Framework | Fluent UI v9.54.0 |
| Entry Point | `ComponentFramework.StandardControl` class |
| DOM Mounting | `ReactDOM.render()` (React 16 API) |
| Build | pcf-scripts (custom PCF build) |
| Deployment | PCF solution import (ControlManifest.xml + bundle.js) |

### 4.2 Component Inventory

**Location**: `src/client/pcf/PlaybookBuilderHost/control/`

```
control/
├── index.ts                              # PCF entry point (593 lines)
├── ControlManifest.Input.xml             # PCF manifest
├── PlaybookBuilderHost.tsx               # Root React component (415 lines)
│
├── components/
│   ├── BuilderLayout.tsx                 # 3-panel layout (357 lines)
│   ├── Canvas/
│   │   └── Canvas.tsx                    # React Flow v10 wrapper (155 lines)
│   ├── Nodes/                            # 7 custom node types
│   │   ├── BaseNode.tsx
│   │   ├── AiAnalysisNode.tsx
│   │   ├── AiCompletionNode.tsx
│   │   ├── ConditionNode.tsx
│   │   ├── DeliverOutputNode.tsx
│   │   ├── CreateTaskNode.tsx
│   │   ├── SendEmailNode.tsx
│   │   └── WaitNode.tsx
│   ├── Edges/
│   │   └── ConditionEdge.tsx             # Condition branch edge
│   ├── Properties/
│   │   ├── PropertiesPanel.tsx
│   │   ├── NodePropertiesForm.tsx        # Accordion-based config
│   │   ├── ScopeSelector.tsx             # Multi-select (MOCK DATA)
│   │   ├── ModelSelector.tsx             # Model picker (MOCK DATA)
│   │   └── ConditionEditor.tsx
│   ├── AiAssistant/                      # AI chat modal (12 files)
│   │   ├── AiAssistantModal.tsx
│   │   ├── ChatHistory.tsx
│   │   ├── ChatInput.tsx
│   │   ├── CommandPalette.tsx
│   │   ├── OperationFeedback.tsx
│   │   ├── TestProgressView.tsx
│   │   ├── TestResultPreview.tsx
│   │   └── ... (5 more)
│   ├── Execution/
│   │   ├── ExecutionOverlay.tsx
│   │   └── ConfidenceBadge.tsx
│   ├── ScopeBrowser/                     # Browse capabilities modal
│   │   ├── ScopeBrowser.tsx
│   │   ├── ScopeList.tsx
│   │   └── ScopeFormDialog.tsx
│   ├── Templates/
│   │   └── TemplateLibraryDialog.tsx
│   ├── SaveAsDialog/
│   │   └── SaveAsDialog.tsx
│   └── TestModeSelector/
│       └── TestModeSelector.tsx
│
├── stores/                               # Zustand stores
│   ├── canvasStore.ts                    # Node/edge state (272 lines)
│   ├── scopeStore.ts                     # Skills/Knowledge/Tools (MOCK - 217 lines)
│   ├── modelStore.ts                     # AI models (MOCK - 144 lines)
│   ├── aiAssistantStore.ts              # AI chat state
│   ├── executionStore.ts                # Execution tracking
│   └── templateStore.ts                 # Template library
│
├── hooks/
│   ├── useExecutionStream.ts             # SSE connection
│   ├── useKeyboardShortcuts.ts           # Ctrl+Z, Ctrl+S, etc.
│   └── useResponsive.ts                  # Responsive layout
│
├── services/
│   ├── playbookNodeSync.ts              # Canvas-to-node Dataverse sync (381 lines)
│   └── AiPlaybookService.ts             # BFF API SSE streaming (470 lines)
│
└── styles.css
```

**Total: 54 files, 28 React components, 6 Zustand stores, 3 hooks, 2 services**

### 4.3 PCF Coupling Points (11 Total)

| PCF API | Usage | Files | Migration |
|---------|-------|-------|-----------|
| `context.webAPI.updateRecord()` | Auto-save canvas to Dataverse | index.ts, playbookNodeSync.ts | → `fetch()` PATCH |
| `context.webAPI.retrieveRecord()` | Load canvas fallback | index.ts | → `fetch()` GET |
| `context.webAPI.createRecord()` | Create node records | playbookNodeSync.ts | → `fetch()` POST |
| `context.webAPI.deleteRecord()` | Delete orphaned nodes | playbookNodeSync.ts | → `fetch()` DELETE |
| `context.webAPI.retrieveMultipleRecords()` | Query existing nodes | playbookNodeSync.ts | → `fetch()` GET with OData |
| `context.parameters.*` | Read bound/input properties | index.ts | → URL params |
| `context.mode.trackContainerResize()` | Enable responsive sizing | index.ts | → `ResizeObserver` |
| `context.mode.allocatedWidth/Height` | Container dimensions | index.ts | → `window.innerWidth/Height` |
| `context.mode.contextInfo.entityId` | Record ID from form | index.ts | → URL param `?id=` |
| `context.notifyOutputChanged()` | Sync dirty state to form | index.ts | → Remove (auto-save only) |
| `Xrm.Navigation.openForm()` | Navigate to cloned playbook | PlaybookBuilderHost.tsx | → `window.open()` or `Xrm.Navigation` |

### 4.4 What Transfers As-Is (Zero Changes)

| Layer | Count | Notes |
|-------|-------|-------|
| React components | 28 | All framework-agnostic (Fluent UI v9 + React) |
| Zustand stores | 6 | Pure state management (no PCF dependencies) |
| Hooks | 3 | DOM/browser APIs only |
| AiPlaybookService | 1 | Uses fetch API (fully portable) |

### 4.5 Canvas JSON Format (Preserved)

```typescript
interface CanvasJson {
  nodes: PlaybookNode[];   // { id, type, position: {x,y}, data: PlaybookNodeData }
  edges: Edge[];           // { id, source, target, sourceHandle?, targetHandle?, type? }
  version: number;         // Currently 1
}
```

The canvas JSON format is stored in `sprk_canvaslayoutjson` and is compatible between react-flow-renderer v10 and @xyflow/react v12 — the node/edge schema is the same.

### 4.6 PlaybookNodeData Interface (Current)

```typescript
export interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType;   // 'aiAnalysis' | 'aiCompletion' | 'condition' | 'deliverOutput' | 'createTask' | 'sendEmail' | 'wait'
  actionId?: string;        // No UI selector (MISSING)
  outputVariable?: string;
  config?: Record<string, unknown>;
  timeoutSeconds?: number;
  retryCount?: number;
  conditionJson?: string;
  skillIds?: string[];      // MOCK IDs ('skill-1', etc.)
  knowledgeIds?: string[];  // MOCK IDs ('knowledge-1', etc.)
  toolId?: string;          // MOCK ID ('tool-1', etc.)
  modelDeploymentId?: string; // MOCK GUID ('50000000-...')
  [key: string]: unknown;
}
```

---

## 5. Future State (Code Page — R5)

### 5.1 Technology Stack

| Aspect | Future (R5 Code Page) |
|--------|----------------------|
| React | 19.0.0 (bundled) |
| Graph Library | @xyflow/react ^12.8.3 |
| State Management | Zustand 5.x |
| UI Framework | Fluent UI v9 |
| Entry Point | `createRoot()` (React 19) |
| DOM Mounting | `createRoot(document.getElementById('root')!)` |
| Build | Webpack 5 + esbuild-loader |
| Deployment | Single inline HTML web resource |
| Auth | Xrm platform strategies + MSAL ssoSilent fallback |
| Dataverse Access | Direct REST API via `fetch()` |

### 5.2 @xyflow/react v12 Features Available

Reference: DocumentRelationshipViewer code page already uses `@xyflow/react ^12.8.3`.

| Feature | v10 (current) | v12+ (future) | Benefit |
|---------|---------------|---------------|---------|
| Import style | Default export: `import ReactFlow from 'react-flow-renderer'` | Named export: `import { ReactFlow } from '@xyflow/react'` | Tree-shakeable |
| Node types | `NodeProps` | `NodeProps<Node<T>>` with generics | Type-safe node data |
| Hooks API | Limited | `useReactFlow()`, `useNodes()`, `useEdges()`, `useNodesState()`, `useEdgesState()` | Hooks-first design |
| Sub-flows | Not supported | `parentId` on nodes creates groups | Composable playbook sections |
| Layout | Manual positioning only | Built-in layout algorithms | Auto-layout on add |
| Background | Basic dots/lines | `BackgroundVariant.Dots`, `.Lines`, `.Cross` | Richer canvas chrome |
| Controls | Basic zoom/fit | Enhanced panel with custom controls | Better UX |
| Performance | Standard React rendering | Concurrent rendering compatible | Smoother canvas interactions |
| CSS | `react-flow-renderer/dist/style.css` | `@xyflow/react/dist/style.css` | Updated styles |
| Dark mode | Manual theming | Native theme support + CSS variables | Easier ADR-021 compliance |
| Edge routing | Basic smoothstep | Enhanced routing, edge labels, interaction zones | Better workflow visualization |

### 5.3 Component Architecture (Target)

**Location**: `src/client/code-pages/PlaybookBuilder/`

```
src/
├── index.tsx                             # React 19 createRoot() entry point
├── App.tsx                               # Root component (auth gate, loading, layout)
│
├── components/
│   ├── BuilderLayout.tsx                 # 3-panel layout (migrated)
│   ├── canvas/
│   │   └── PlaybookCanvas.tsx            # @xyflow/react v12 wrapper (REWRITTEN)
│   ├── nodes/                            # Custom node types (MIGRATED to v12 API)
│   │   ├── BaseNode.tsx                  # Updated: NodeProps<Node<PlaybookNodeData>>
│   │   ├── AiAnalysisNode.tsx
│   │   ├── AiCompletionNode.tsx
│   │   ├── ConditionNode.tsx
│   │   ├── DeliverOutputNode.tsx
│   │   ├── CreateTaskNode.tsx
│   │   ├── SendEmailNode.tsx
│   │   └── WaitNode.tsx
│   ├── edges/
│   │   └── ConditionEdge.tsx             # Updated: EdgeProps generics
│   ├── properties/
│   │   ├── PropertiesPanel.tsx           # (migrated)
│   │   ├── NodePropertiesForm.tsx        # (migrated + action selector added)
│   │   ├── ScopeSelector.tsx             # (REWRITTEN — real Dataverse lookups)
│   │   ├── ActionSelector.tsx            # (NEW — queries sprk_analysisaction)
│   │   ├── ModelSelector.tsx             # (REWRITTEN — real Dataverse lookups)
│   │   └── ConditionEditor.tsx           # (migrated)
│   ├── ai-assistant/                     # (migrated as-is, 12 files)
│   ├── execution/                        # (migrated as-is)
│   ├── scope-browser/                    # (migrated)
│   ├── templates/                        # (migrated)
│   └── save-as-dialog/                   # (migrated)
│
├── stores/
│   ├── canvasStore.ts                    # (migrated — update to v12 types)
│   ├── scopeStore.ts                     # (REWRITTEN — Dataverse lookups)
│   ├── modelStore.ts                     # (REWRITTEN — Dataverse lookups)
│   ├── aiAssistantStore.ts              # (migrated)
│   ├── executionStore.ts                # (migrated)
│   └── templateStore.ts                 # (migrated)
│
├── hooks/
│   ├── useAuth.ts                        # (NEW — from AnalysisWorkspace pattern)
│   ├── useThemeDetection.ts              # (NEW — from AnalysisWorkspace pattern)
│   ├── usePlaybookLoader.ts              # (NEW — loads playbook from Dataverse)
│   ├── useAutoSave.ts                    # (NEW — debounced save to Dataverse)
│   ├── useExecutionStream.ts             # (migrated)
│   ├── useKeyboardShortcuts.ts           # (migrated)
│   └── useResponsive.ts                  # (migrated)
│
├── services/
│   ├── dataverseClient.ts                # (NEW — fetch()-based Dataverse CRUD)
│   ├── authService.ts                    # (NEW — from AnalysisWorkspace pattern)
│   ├── playbookNodeSync.ts              # (MIGRATED — uses dataverseClient)
│   └── aiPlaybookService.ts             # (migrated — uses authService for tokens)
│
├── config/
│   ├── msalConfig.ts                     # (NEW — from AnalysisWorkspace)
│   └── bffConfig.ts                      # (NEW — from AnalysisWorkspace)
│
├── types/
│   └── playbook.ts                       # Shared type definitions
│
├── package.json
├── webpack.config.js
├── tsconfig.json
├── build-webresource.ps1                 # Inline bundle → HTML
└── index.html                            # Template HTML
```

### 5.4 DataverseClient Service (New)

Replaces all `context.webAPI.*` calls with direct Dataverse REST API calls:

```typescript
export class DataverseClient {
  private baseUrl: string;   // e.g., 'https://spaarkedev1.crm.dynamics.com'
  private getToken: () => Promise<string>;

  async createRecord(entitySet: string, data: Record<string, unknown>): Promise<{ id: string }>;
  async updateRecord(entitySet: string, id: string, data: Record<string, unknown>): Promise<void>;
  async deleteRecord(entitySet: string, id: string): Promise<void>;
  async retrieveRecord(entitySet: string, id: string, select?: string): Promise<Record<string, unknown>>;
  async retrieveMultipleRecords(entitySet: string, options?: string): Promise<{ value: Record<string, unknown>[] }>;
}
```

This maps 1:1 to the PCF `context.webAPI` interface but uses `fetch()` with Bearer token auth. The `playbookNodeSync.ts` service already abstracts its Dataverse calls behind a `PcfWebApi` interface — the `DataverseClient` can implement this same interface.

### 5.5 Scope Stores (Rewritten)

Replace mock arrays with Dataverse queries:

```typescript
// scopeStore.ts — Future State
export const useScopeStore = create<ScopeState>((set, get) => ({
  skills: [],
  knowledge: [],
  tools: [],
  actions: [],    // NEW — was missing entirely
  isLoading: true,

  loadScopes: async (client: DataverseClient) => {
    const [skills, knowledge, tools, actions] = await Promise.all([
      client.retrieveMultipleRecords('sprk_analysisskills', '?$select=sprk_name,sprk_description,sprk_category&$filter=statecode eq 0'),
      client.retrieveMultipleRecords('sprk_aiknowledges', '?$select=sprk_name,sprk_description,sprk_type&$filter=statecode eq 0'),
      client.retrieveMultipleRecords('sprk_analysistools', '?$select=sprk_name,sprk_description,sprk_handlertype&$filter=statecode eq 0'),
      client.retrieveMultipleRecords('sprk_analysisactions', '?$select=sprk_name,sprk_description&$filter=statecode eq 0'),
    ]);
    set({ skills: mapSkills(skills.value), knowledge: mapKnowledge(knowledge.value), tools: mapTools(tools.value), actions: mapActions(actions.value), isLoading: false });
  },
}));
```

### 5.6 Authentication Flow

Follows the established AnalysisWorkspace pattern:

```
1. Xrm Platform Strategies (priority order):
   a. Xrm.Utility.getGlobalContext().getAccessToken()   (modern 2024+)
   b. __crmTokenProvider.getToken()                      (legacy global)
   c. AUTHENTICATION_TOKEN global                        (fallback)
   d. Xrm.Page.context.getAuthToken()                   (deprecated)
   e. window.__SPAARKE_BFF_TOKEN__                       (bridge token)

2. MSAL ssoSilent (fallback for embedded web resource mode):
   - Uses existing Azure AD session cookie
   - No interactive login required
   - Authority: https://login.microsoftonline.com/organizations
   - Client ID: 170c98e1-d486-4355-bcbe-170454e0207c (Spaarke DSM-SPE Dev 2)

3. Token Caching:
   - In-memory only (never localStorage/sessionStorage)
   - 5-minute expiry buffer for proactive refresh
   - 4-minute refresh interval
```

### 5.7 URL Parameter Resolution

```typescript
// Code page receives parameters via URL
// Three hosting modes supported:

// Mode 1: Opened from form via navigateTo
//   ?data=playbookId={guid}&theme=dark
const dataEnvelope = rawUrlParams.get('data');
const appParams = dataEnvelope
  ? new URLSearchParams(decodeURIComponent(dataEnvelope))
  : rawUrlParams;

// Mode 2: Direct URL (development/testing)
//   ?playbookId={guid}&theme=light

// Mode 3: Embedded on form (frame-walk fallback)
//   Reads parent Xrm.Page.data.entity.getId()
```

### 5.8 Build & Deployment Pipeline

Following the established two-step code page build:

```bash
# Step 1: Webpack bundle
cd src/client/code-pages/PlaybookBuilder
npm run build
# Output: out/bundle.js (~1.2 MB estimated)

# Step 2: Inline into single HTML
powershell -File build-webresource.ps1
# Output: out/sprk_playbookbuilder.html (deployable)

# Step 3: Deploy to Dataverse
# Upload sprk_playbookbuilder.html as Webpage (HTML) web resource
```

### 5.9 How the Code Page Opens

```typescript
// From playbook list or form, open as full-page dialog:
Xrm.Navigation.navigateTo(
  {
    pageType: 'webresource',
    webresourceName: 'sprk_playbookbuilder',
    data: `playbookId=${playbookId}`
  },
  {
    target: 2,  // Dialog
    width: { value: 95, unit: '%' },
    height: { value: 95, unit: '%' },
    position: 1  // Center
  }
);

// Or as full-page navigation:
Xrm.Navigation.navigateTo(
  {
    pageType: 'webresource',
    webresourceName: 'sprk_playbookbuilder',
    data: `playbookId=${playbookId}`
  },
  { target: 1 }  // Full page
);
```

---

## 6. Existing @xyflow/react v12 Reference Implementation

### 6.1 DocumentRelationshipViewer Code Page

**Location**: `spaarke-wt-ai-semantic-search-ui-r3/src/client/code-pages/DocumentRelationshipViewer/`

This code page already uses `@xyflow/react ^12.8.3` with React 19 and provides the proven integration pattern for the Playbook Builder migration.

**Dependencies**:
```json
{
  "@xyflow/react": "^12.8.3",
  "d3-force": "^3.0.0",
  "react": "^19.0.0",
  "react-dom": "^19.0.0"
}
```

### 6.2 Import Pattern (v12)

```typescript
// DocumentGraph.tsx — @xyflow/react v12 imports
import {
  ReactFlow,           // Named export (not default)
  Background,
  Controls,
  MiniMap,
  useNodesState,       // Hooks-based state management
  useEdgesState,
  BackgroundVariant,
  type NodeTypes,
  type EdgeTypes,
  type Node,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
```

### 6.3 Custom Node Pattern (v12)

```typescript
// DocumentNode.tsx — typed NodeProps with generics
import { Handle, Position, type NodeProps, type Node } from '@xyflow/react';

interface DocumentNodeData {
  title: string;
  similarity: number;
  // ... other fields
}

export function DocumentNode({ data }: NodeProps<Node<DocumentNodeData>>) {
  return (
    <div>
      <Handle type="target" position={Position.Left} />
      <div>{data.title}</div>
      <Handle type="source" position={Position.Right} />
    </div>
  );
}
```

### 6.4 Force-Directed Layout Hook (d3-force)

```typescript
// useForceLayout.ts — d3-force simulation for automatic layout
import { forceSimulation, forceLink, forceManyBody, forceCenter, forceCollide } from 'd3-force';

const DEFAULT_OPTIONS = {
  distanceMultiplier: 400,
  collisionRadius: 100,
  chargeStrength: -1000,
  centerX: 0,
  centerY: 0,
};
```

### 6.5 Migration Map: v10 → v12

| Area | react-flow-renderer v10 (R4) | @xyflow/react v12 (R5) |
|------|------------------------------|------------------------|
| Package | `import ReactFlow from 'react-flow-renderer'` | `import { ReactFlow } from '@xyflow/react'` |
| CSS | `react-flow-renderer/dist/style.css` | `@xyflow/react/dist/style.css` |
| State hooks | `useNodesState`, `useEdgesState` (v10 compatible) | Same hooks, enhanced types |
| Node props | `NodeProps` (untyped data) | `NodeProps<Node<PlaybookNodeData>>` (generic) |
| Edge props | `EdgeProps` | `EdgeProps<Edge<PlaybookEdgeData>>` (generic) |
| Instance | `ReactFlowInstance` | `ReactFlowInstance` (enhanced API) |
| Background | `<Background variant="dots" />` | `<Background variant={BackgroundVariant.Dots} />` |
| Node position | `reactFlowInstance.project({x, y})` | `reactFlowInstance.screenToFlowPosition({x, y})` |

### 6.6 New v12 Features to Leverage

**Sub-flows (Node Groups)**:
```typescript
// Group related nodes (e.g., "Document Analysis Phase")
const groupNode: Node = {
  id: 'phase-1',
  type: 'group',
  position: { x: 0, y: 0 },
  data: { label: 'Document Analysis Phase' },
  style: { width: 600, height: 400 },
};

// Child nodes reference parent
const childNode: Node = {
  id: 'node-1',
  parentId: 'phase-1',  // NEW in v12
  position: { x: 50, y: 50 },  // Relative to parent
  data: { ... },
};
```

**Built-in Layout Algorithms**:
```typescript
// Auto-layout nodes on canvas
import { getLayoutedElements } from '@xyflow/react';
// Or use d3-force (already proven in DocumentRelationshipViewer)
```

**Enhanced Controls Panel**:
```typescript
<ReactFlow>
  <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
  <Controls showInteractive showFitView showZoom />
  <MiniMap
    nodeColor={(node) => nodeTypeColors[node.data.type]}
    zoomable
    pannable
  />
</ReactFlow>
```

---

## 7. Canvas-to-Execution Integration (Critical Gap Analysis)

**The canvas was built as a visual POC.** It lets you draw nodes and connect them, but most node types cannot be configured for actual execution. This section maps every execution requirement to a canvas configuration element and identifies what must be built.

### 7.1 Current State: What Works vs. What Doesn't

| Node Type | Canvas Creates | Canvas Configures | Execution Result |
|-----------|---------------|-------------------|------------------|
| **AI Analysis** | Yes | Tool, Skills, Knowledge, Model, Timeout, Retry | Executes if tool selected |
| **Condition** | Yes | Operator, Left/Right expressions, Branches | Executes if condition configured |
| **AI Completion** | Yes | Model only (partial) | No executor implemented yet |
| **Deliver Output** | Yes | Nothing — no config UI | Fails: "Delivery configuration required" |
| **Send Email** | Yes | Nothing — no config UI | Fails: "At least one recipient required" |
| **Create Task** | Yes | Nothing — no config UI | Fails: "Task subject is required" |
| **Wait** | Yes | Nothing — no config UI | No executor implemented yet |

**Bottom line: Only 2 of 7 node types are usable end-to-end.** The rest create visual boxes on the canvas that produce execution errors.

### 7.2 Per-Node Execution Requirements

Each node type's executor reads `sprk_configjson` (a JSON blob) for its type-specific configuration. The canvas must provide UI to build this JSON.

#### AI Analysis Node — `AiAnalysisNodeExecutor`

```
Execution reads:
  ToolId          → Required. Determines which handler runs (e.g., EntityExtractionHandler)
  SkillIds[]      → Optional. Augments prompt with skill definitions
  KnowledgeIds[]  → Optional. Injects knowledge context
  ModelDeploymentId → Optional. Overrides default AI model
  ConfigJson      → Optional. Handler-specific settings

Canvas provides:
  ✅ Tool selector       (ScopeSelector — but MOCK DATA)
  ✅ Skills multi-select  (ScopeSelector — but MOCK DATA)
  ✅ Knowledge multi-select (ScopeSelector — but MOCK DATA)
  ✅ Model selector       (ModelSelector — but MOCK DATA)
  ✅ Timeout / Retry      (SpinButton inputs)
  ❌ Action selector      (MISSING — no UI to set actionId)
  ❌ isActive toggle      (MISSING — no way to disable a node)

Status: Structurally complete, blocked by mock data + missing action selector
```

#### Condition Node — `ConditionNodeExecutor`

```
Execution reads ConfigJson:
  {
    "condition": {
      "operator": "eq|ne|gt|lt|contains|exists|and|or|not",
      "left": "{{nodeName.output.fieldName}}",
      "right": "value or {{variable}}"
    },
    "trueBranch": "branchNodeOutputVar",
    "falseBranch": "branchNodeOutputVar"
  }

Canvas provides:
  ✅ ConditionEditor with operator dropdown
  ✅ Left/right expression fields (support {{}} templates)
  ✅ Edge routing for true/false branches

Status: Complete — fully configurable
```

#### Deliver Output Node — `DeliverOutputNodeExecutor`

```
Execution reads ConfigJson:
  {
    "deliveryType": "markdown|html|text|json",          ← REQUIRED
    "template": "## Report\n{{extract.output.summary}}", ← REQUIRED for non-JSON
    "outputFormat": {
      "includeMetadata": true,
      "includeSourceCitations": false,
      "maxLength": 10000
    }
  }

Canvas provides:
  ❌ No delivery type selector
  ❌ No template editor (Handlebars syntax)
  ❌ No output format options
  ❌ No variable reference helper (list available {{variables}})

Status: COMPLETELY MISSING — node is a visual placeholder only
```

**What must be built:**
- Delivery type dropdown (markdown, html, text, json)
- Handlebars template editor with syntax highlighting
- Variable reference panel showing available `{{nodeName.output.*}}` from upstream nodes
- Output format options (metadata, citations, max length)

#### Send Email Node — `SendEmailNodeExecutor`

```
Execution reads ConfigJson:
  {
    "to": ["user@example.com", "{{node1.output.recipientEmail}}"],
    "cc": ["cc@example.com"],
    "subject": "Analysis: {{extract.output.documentName}}",
    "body": "{{deliverOutput.output.content}}",
    "isHtml": true
  }

Canvas provides:
  ❌ No recipient fields (To, CC)
  ❌ No subject field
  ❌ No body editor
  ❌ No HTML toggle
  ❌ No template variable support

Status: COMPLETELY MISSING — node is a visual placeholder only
```

**What must be built:**
- Recipients input (To/CC with tag-style multi-entry)
- Subject line input with `{{variable}}` autocomplete
- Body text editor with `{{variable}}` support
- HTML/plain text toggle

#### Create Task Node — `CreateTaskNodeExecutor`

```
Execution reads ConfigJson:
  {
    "subject": "Review {{extract.output.documentName}}",
    "description": "{{deliverOutput.output.content}}",
    "regardingObjectId": "{{recordId}}",
    "regardingObjectType": "sprk_document",
    "ownerId": "{{assigneeId}}",
    "dueDate": "{{dueDate}}"
  }

Canvas provides:
  ❌ No subject field
  ❌ No description field
  ❌ No regarding object picker
  ❌ No owner/assignee picker
  ❌ No due date field

Status: COMPLETELY MISSING — node is a visual placeholder only
```

**What must be built:**
- Subject input with `{{variable}}` support
- Description textarea with `{{variable}}` support
- Regarding object type selector + record lookup
- Owner/assignee user lookup
- Due date picker (relative or absolute)

#### Wait Node — No Executor Yet

```
Execution reads ConfigJson:
  {
    "waitType": "duration|until|condition",
    "duration": { "hours": 24 },
    "untilDateTime": "2026-03-01T00:00:00Z",
    "resumeCondition": { ... }
  }

Canvas provides:
  ❌ No wait type selector
  ❌ No duration input
  ❌ No date/time picker

Status: No executor AND no config UI — fully placeholder
```

#### AI Completion Node — No Executor Yet

```
Execution reads ConfigJson:
  {
    "systemPrompt": "You are a legal analyst...",
    "userPromptTemplate": "Analyze: {{document.text}}",
    "temperature": 0.7,
    "maxTokens": 4000
  }

Canvas provides:
  ❌ No system prompt editor
  ❌ No user prompt template editor
  ❌ No temperature/token controls

Status: No executor AND minimal config UI — mostly placeholder
```

### 7.3 Cross-Cutting Gaps (All Node Types)

| Gap | Impact | Current State |
|-----|--------|---------------|
| **Action selector** | Cannot associate node with execution action | No UI — `actionId` field exists in data type but no picker |
| **isActive toggle** | Cannot disable nodes without deleting | No UI — field exists in Dataverse but not exposed |
| **ConfigJson editor** | Node-specific config never gets written | No typed forms; only generic `config` field in PlaybookNodeData |
| **Variable reference** | Users can't see available `{{variables}}` | No UI showing upstream node outputs |
| **Validation feedback** | Users don't know if config is complete | No per-node validation indicators |
| **Mock scope data** | Selected IDs won't match Dataverse at execution | All scope stores use hardcoded arrays |

### 7.4 The ConfigJson Problem

The execution pipeline reads `sprk_configjson` for all node-specific configuration. Currently:

1. **Canvas stores generic data** in `PlaybookNodeData` (label, timeout, retry, skillIds, etc.)
2. **`playbookNodeSync.ts` strips known fields** and puts the rest into `sprk_configjson` as a blob
3. **No node type has a form that writes type-specific config** — the config JSON field is empty for Deliver Output, Send Email, Create Task, Wait
4. **Executors validate ConfigJson and fail** when it's missing or incomplete

The fix: each node type needs a **typed configuration form** that writes its specific config into the canvas store, which then flows through sync into `sprk_configjson`.

### 7.5 Template Variable System

Multiple node types support Handlebars-style `{{variable}}` references in their config:

```
{{nodeName.output.fieldName}}   — Reference a specific output field from an upstream node
{{nodeName.text}}               — Reference the raw text output from an upstream node
{{document.text}}               — Reference the source document text
{{recordId}}                    — Reference the triggering record ID
```

The canvas needs a **variable reference panel** that:
1. Lists all upstream nodes (from edge connections)
2. Shows their output variable names
3. Provides insert-into-field functionality
4. Validates that referenced variables exist

### 7.6 PlaybookNodeData Interface (Required Expansion)

```typescript
// Current — minimal, insufficient
export interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType;
  actionId?: string;
  outputVariable?: string;
  config?: Record<string, unknown>;    // ← Generic, never populated by UI
  skillIds?: string[];
  knowledgeIds?: string[];
  toolId?: string;
  modelDeploymentId?: string;
  timeoutSeconds?: number;
  retryCount?: number;
  conditionJson?: string;
}

// Required — typed config per node type
export interface PlaybookNodeData {
  label: string;
  type: PlaybookNodeType;
  actionId?: string;
  outputVariable?: string;
  isActive?: boolean;                   // NEW — enable/disable toggle
  skillIds?: string[];
  knowledgeIds?: string[];
  toolId?: string;
  modelDeploymentId?: string;
  timeoutSeconds?: number;
  retryCount?: number;

  // Type-specific config (maps to sprk_configjson)
  conditionJson?: string;               // Condition nodes

  // Deliver Output config
  deliveryType?: 'markdown' | 'html' | 'text' | 'json';
  template?: string;                    // Handlebars template
  includeMetadata?: boolean;
  includeSourceCitations?: boolean;
  maxOutputLength?: number;

  // Send Email config
  emailTo?: string[];                   // Recipients
  emailCc?: string[];                   // CC recipients
  emailSubject?: string;                // Subject (template supported)
  emailBody?: string;                   // Body (template supported)
  emailIsHtml?: boolean;

  // Create Task config
  taskSubject?: string;                 // Subject (template supported)
  taskDescription?: string;             // Description (template supported)
  taskRegardingType?: string;           // Entity type
  taskRegardingId?: string;             // Record ID (template supported)
  taskOwnerId?: string;                 // Assignee
  taskDueDate?: string;                 // Due date

  // AI Completion config
  systemPrompt?: string;
  userPromptTemplate?: string;
  temperature?: number;
  maxTokens?: number;

  // Wait config
  waitType?: 'duration' | 'until' | 'condition';
  waitDurationHours?: number;
  waitUntilDateTime?: string;
}
```

### 7.7 New Properties Forms Required

| Node Type | New Component | Sections |
|-----------|--------------|----------|
| **Deliver Output** | `DeliverOutputForm.tsx` | Delivery type dropdown, Template editor (Handlebars with variable picker), Output format options |
| **Send Email** | `SendEmailForm.tsx` | Recipients (To/CC), Subject, Body editor, HTML toggle |
| **Create Task** | `CreateTaskForm.tsx` | Subject, Description, Regarding object, Owner lookup, Due date |
| **AI Completion** | `AiCompletionForm.tsx` | System prompt editor, User prompt template, Temperature slider, Max tokens |
| **Wait** | `WaitForm.tsx` | Wait type selector, Duration input, DateTime picker |
| **Variable Reference** | `VariableReferencePanel.tsx` | Lists upstream node outputs, click-to-insert |
| **Node Validation** | `NodeValidationBadge.tsx` | Shows config completeness per node |

### 7.8 Updated playbookNodeSync Mapping

`playbookNodeSync.ts` must map all new fields into `sprk_configjson`:

```typescript
function buildConfigJson(data: PlaybookNodeData): string {
  const config: Record<string, unknown> = { __canvasNodeId: data.id };

  switch (data.type) {
    case 'deliverOutput':
      config.deliveryType = data.deliveryType;
      config.template = data.template;
      config.outputFormat = {
        includeMetadata: data.includeMetadata,
        includeSourceCitations: data.includeSourceCitations,
        maxLength: data.maxOutputLength,
      };
      break;
    case 'sendEmail':
      config.to = data.emailTo;
      config.cc = data.emailCc;
      config.subject = data.emailSubject;
      config.body = data.emailBody;
      config.isHtml = data.emailIsHtml;
      break;
    case 'createTask':
      config.subject = data.taskSubject;
      config.description = data.taskDescription;
      config.regardingObjectId = data.taskRegardingId;
      config.regardingObjectType = data.taskRegardingType;
      config.ownerId = data.taskOwnerId;
      config.dueDate = data.taskDueDate;
      break;
    // ... etc
  }

  return JSON.stringify(config);
}
```

---

## 8. Data Architecture

### 8.1 Dataverse Entity Model (Unchanged)

```
sprk_analysisplaybook (Playbook Record)
├── sprk_name                    # Playbook name
├── sprk_description             # Description
├── sprk_canvaslayoutjson        # Canvas JSON (nodes, edges, viewport)
└── sprk_playbooknode (1:N)      # Executable node records
    ├── sprk_name                # Node display name
    ├── sprk_executionorder      # Topological sort order
    ├── sprk_outputvariable      # Output variable name
    ├── sprk_configjson          # Node config (contains __canvasNodeId)
    ├── sprk_position_x/y        # Canvas position
    ├── sprk_isactive            # Active flag
    ├── sprk_timeoutseconds      # Execution timeout
    ├── sprk_retrycount          # Retry count
    ├── sprk_conditionjson       # Condition expression (condition nodes)
    ├── sprk_dependsonjson       # Upstream node GUIDs (execution graph)
    ├── sprk_actionid            # Lookup → sprk_analysisaction
    ├── sprk_toolid              # Lookup → sprk_analysistool
    └── sprk_modeldeploymentid   # Lookup → sprk_aimodeldeployment
```

### 8.2 Scope Tables (Read by Code Page)

| Table | Fields to Query | Used For |
|-------|-----------------|----------|
| `sprk_analysisskills` | `sprk_name`, `sprk_description`, `sprk_category` | Skills multi-select checkboxes |
| `sprk_aiknowledges` | `sprk_name`, `sprk_description`, `sprk_type` | Knowledge multi-select checkboxes |
| `sprk_analysistools` | `sprk_name`, `sprk_description`, `sprk_handlertype` | Tool single-select dropdown |
| `sprk_analysisactions` | `sprk_name`, `sprk_description` | Action selector (NEW) |
| `sprk_aimodeldeployments` | `sprk_name`, `sprk_provider`, `sprk_capability`, `sprk_modelid`, `sprk_contextwindow`, `sprk_isactive` | Model deployment picker |

### 8.3 N:N Relationship Tables (Node Scope Resolution)

These tables link nodes to their scope items. They are written by the code page during canvas-to-node sync and read by the BFF API at execution time:

| Relationship Table | Links |
|-------------------|-------|
| `sprk_playbooknode_analysisskill` | Node ↔ Skills (many-to-many) |
| `sprk_playbooknode_aiknowledge` | Node ↔ Knowledge (many-to-many) |

Tool and Action are stored as direct lookups on the node record (single-select).

### 8.4 Canvas-to-Node Sync Flow (Preserved)

```
User edits canvas (drag, connect, configure)
    ↓
Zustand canvasStore marks dirty
    ↓
Debounced auto-save (500ms)
    ↓
Save canvas JSON to sprk_canvaslayoutjson (PATCH)
    ↓
Sync canvas nodes to sprk_playbooknode records:
  1. Query existing nodes for this playbook
  2. Compute execution order (Kahn's topological sort)
  3. Create new / update existing / delete orphaned records
  4. Update dependsOn GUIDs (execution graph)
```

---

## 9. New Components Detail

> **Note**: In addition to the components below, Section 7.7 defines **7 new node configuration forms** (DeliverOutputForm, SendEmailForm, CreateTaskForm, AiCompletionForm, WaitForm, VariableReferencePanel, NodeValidationBadge) that close the canvas-to-execution gap.

### 9.1 ActionSelector Component (New)

The Properties panel currently has no way to select an action. This component adds an action dropdown:

```typescript
// ActionSelector.tsx — queries sprk_analysisaction records
interface ActionSelectorProps {
  nodeId: string;
  selectedActionId?: string;
}

// Single-select dropdown populated from Dataverse
// Only shown for node types that support actions (aiAnalysis, aiCompletion)
// Saves actionId to canvas store → synced to sprk_playbooknode.sprk_actionid
```

### 9.2 DataverseClient Service (New)

Thin wrapper around `fetch()` matching the Dataverse Web API v9.2 contract:

```typescript
class DataverseClient {
  constructor(private baseUrl: string, private getToken: () => Promise<string>) {}

  // Standard CRUD — maps to same interface as PCF context.webAPI
  createRecord(entitySet, data)            // POST /api/data/v9.2/{entitySet}
  updateRecord(entitySet, id, data)        // PATCH /api/data/v9.2/{entitySet}({id})
  deleteRecord(entitySet, id)              // DELETE /api/data/v9.2/{entitySet}({id})
  retrieveRecord(entitySet, id, select?)   // GET /api/data/v9.2/{entitySet}({id})?$select=...
  retrieveMultipleRecords(entitySet, opts) // GET /api/data/v9.2/{entitySet}?$filter=...&$select=...

  // Batch operations (for N:N relationship management)
  executeBatch(changesets)                 // POST /api/data/v9.2/$batch

  // Headers applied automatically:
  //   Authorization: Bearer {token}
  //   OData-MaxVersion: 4.0
  //   OData-Version: 4.0
  //   Content-Type: application/json
  //   Prefer: return=representation (for creates)
}
```

### 9.3 AuthService (New — From AnalysisWorkspace)

Copied from the established AnalysisWorkspace pattern. Multi-strategy token acquisition with in-memory caching and proactive refresh.

### 9.4 usePlaybookLoader Hook (New)

```typescript
function usePlaybookLoader(playbookId: string, client: DataverseClient) {
  // 1. Load playbook record (name, description, canvasJson)
  // 2. Load scope data in parallel (skills, knowledge, tools, actions, models)
  // 3. Initialize canvasStore with loaded canvas JSON
  // 4. Initialize scopeStore with real Dataverse records
  // Returns: { isLoading, error, playbook }
}
```

### 9.5 useAutoSave Hook (New)

```typescript
function useAutoSave(playbookId: string, client: DataverseClient) {
  // Watches canvasStore.isDirty
  // Debounces 500ms
  // PATCH sprk_canvaslayoutjson with current canvas JSON
  // Calls playbookNodeSync after successful save
  // Returns: { isSaving, lastSaved, error }
}
```

### 9.6 PlaybookCanvas Component (Rewritten)

```typescript
// Upgraded from React Flow v10 → @xyflow/react v12
import { ReactFlow, Background, Controls, MiniMap, BackgroundVariant, useReactFlow } from '@xyflow/react';
import '@xyflow/react/dist/style.css';

function PlaybookCanvas() {
  const [nodes, setNodes, onNodesChange] = useNodesState(initialNodes);
  const [edges, setEdges, onEdgesChange] = useEdgesState(initialEdges);
  const { screenToFlowPosition } = useReactFlow();  // v12 API

  return (
    <ReactFlow
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      edgeTypes={edgeTypes}
      onNodesChange={onNodesChange}
      onEdgesChange={onEdgesChange}
      onConnect={onConnect}
      onDrop={handleDrop}
      fitView
      snapToGrid
      snapGrid={[16, 16]}
      defaultEdgeOptions={{ type: 'smoothstep', animated: true }}
      deleteKeyCode="Delete"
      selectionKeyCode="Shift"
    >
      <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
      <Controls showInteractive />
      <MiniMap
        nodeColor={(node) => nodeTypeColors[node.data?.type]}
        zoomable
        pannable
      />
    </ReactFlow>
  );
}
```

---

## 10. Architecture Decisions

### AD-01: Code Page Over PCF

**Decision**: Rebuild as a Code Page, not a PCF control.

**Rationale**:
- The Playbook Builder is a standalone workspace, not a form field editor
- ADR-006 states: "field-bound → PCF; standalone dialog → Code Page"
- React 19 unlocks @xyflow/react v12+ (hooks API, sub-flows, enhanced controls)
- 70% of existing code is framework-agnostic and transfers directly
- Code page deployment is simpler (single HTML upload vs. solution import)
- Consistent with AnalysisWorkspace and DocumentRelationshipViewer architecture

**Alternatives Considered**:
- Keep as PCF: Locked to React 16, cannot upgrade xyflow, mock data still needs replacing
- PCF with React 18 bundle: Violates ADR-022 (platform-library constraint), increases bundle size

### AD-02: Direct Dataverse REST API (Not BFF API)

**Decision**: Use `fetch()` to the Dataverse Web API for all CRUD during playbook building.

**Rationale**:
- Separation of concerns: Playbook Builder owns Dataverse CRUD at build time; BFF API only reads at execution time
- Code pages running inside Dataverse have same-origin access to the Web API
- Eliminates an unnecessary network hop through the BFF
- Consistent with how the PCF version worked (direct webAPI calls)
- Auth token available from Xrm platform or MSAL

**What goes through BFF API**:
- AI Assistant chat (SSE streaming to `/api/ai/playbook-builder/process`)
- Playbook execution (handled by AnalysisWorkspace, not this builder)

### AD-03: Preserve Canvas JSON Format

**Decision**: Keep the existing `CanvasJson` format (nodes/edges/version) unchanged.

**Rationale**:
- Existing playbooks in Dataverse use this format
- @xyflow/react v12 uses the same node/edge schema as v10
- No data migration required
- Canvas-to-node sync logic remains compatible

### AD-04: Auth Pattern from AnalysisWorkspace

**Decision**: Copy the multi-strategy auth service from AnalysisWorkspace.

**Rationale**:
- Proven in production (AnalysisWorkspace already deployed)
- Handles all hosting scenarios (navigateTo, embedded, direct URL)
- In-memory token caching (secure)
- Proactive refresh prevents token expiration during long design sessions

### AD-05: Migrate Zustand Stores, Don't Rewrite State Management

**Decision**: Keep Zustand as state management, migrate stores with minimal changes.

**Rationale**:
- All 6 stores are framework-agnostic (zero PCF dependencies)
- Zustand works identically with React 19
- Store APIs remain the same — only the scope/model stores need data source changes
- Avoids risk of rewriting working state logic

---

## 11. Migration Strategy

### Phase 1: Scaffold & Core Infrastructure (Week 1)

1. Create code page project structure following AnalysisWorkspace pattern
2. Set up Webpack 5 + esbuild-loader + build-webresource.ps1
3. Implement DataverseClient service
4. Copy authService + msalConfig + bffConfig from AnalysisWorkspace
5. Create usePlaybookLoader and useAutoSave hooks
6. Create index.tsx entry point with createRoot + FluentProvider + theme detection

### Phase 2: Canvas Migration (Week 2)

1. Install @xyflow/react ^12.8.3 and d3-force ^3.0.0
2. Rewrite Canvas.tsx → PlaybookCanvas.tsx using v12 API
3. Migrate all 7 node components to v12 `NodeProps<Node<PlaybookNodeData>>` generics
4. Migrate ConditionEdge to v12 EdgeProps
5. Migrate canvasStore to use v12 types (useNodesState, useEdgesState)
6. Wire drag-and-drop with `screenToFlowPosition()` (replaces `project()`)

### Phase 3: Scope Resolution — Real Dataverse Lookups (Week 3)

1. Rewrite scopeStore — real Dataverse queries for skills, knowledge, tools, actions
2. Rewrite modelStore — real Dataverse queries for model deployments
3. Add ActionSelector component (queries `sprk_analysisaction`)
4. Add isActive toggle to all node types
5. Migrate ScopeSelector, ModelSelector to use real data
6. Update playbookNodeSync to use DataverseClient + map all config fields

### Phase 4: Node Configuration Forms — Execution Integration (Week 3-4)

**This is the critical phase that makes the canvas actually useful for building playbooks.**

1. Build `DeliverOutputForm.tsx` — delivery type, Handlebars template editor, output format
2. Build `SendEmailForm.tsx` — recipients, subject, body with template variable support
3. Build `CreateTaskForm.tsx` — subject, description, regarding object, owner, due date
4. Build `AiCompletionForm.tsx` — system prompt, user prompt template, temperature, max tokens
5. Build `WaitForm.tsx` — wait type, duration, datetime picker
6. Build `VariableReferencePanel.tsx` — lists upstream node outputs, click-to-insert `{{var}}`
7. Build `NodeValidationBadge.tsx` — shows config completeness per node (red/yellow/green)
8. Expand `PlaybookNodeData` interface with all type-specific fields
9. Update `playbookNodeSync.ts` `buildConfigJson()` to map all fields into `sprk_configjson`
10. Wire `NodePropertiesForm.tsx` to render the correct type-specific form per node type

### Phase 5: AI Assistant & Templates (Week 4-5)

1. Migrate AiAssistantModal and all 12 sub-components (no changes needed)
2. Migrate aiAssistantStore (update token acquisition)
3. Migrate templateStore (switch to DataverseClient)
4. Migrate ExecutionOverlay + ConfidenceBadge

### Phase 6: Integration & Polish (Week 5-6)

1. Wire BuilderLayout with all panels
2. Keyboard shortcuts integration
3. Auto-save + node sync end-to-end testing
4. Per-node validation: warn user when config is incomplete
5. Template variable resolution: verify `{{var}}` references resolve against upstream outputs
6. Theme / dark mode verification (ADR-021)
7. Build, deploy as web resource, test in Dataverse

### Phase 7: Execution Verification & Cleanup (Week 6-7)

1. **End-to-end execution test**: Build playbook in canvas → save → execute from AnalysisWorkspace → verify all nodes execute
2. Verify `sprk_configjson` contains correct typed config for each node type
3. Verify Deliver Output renders formatted markdown (not raw JSON)
4. Verify condition branching routes correctly
5. Remove PCF PlaybookBuilderHost control from solution
6. Update form scripts to open code page instead of PCF
7. Add navigation from playbook form to code page

---

## 12. Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| @xyflow/react v12 API differences | Medium | Low | DocumentRelationshipViewer proves the stack; v10→v12 migration guide exists |
| Dataverse REST API auth in code page | Medium | Low | AnalysisWorkspace pattern proven; same-origin access simplifies |
| Canvas JSON backward compatibility | High | Very Low | v12 uses identical node/edge schema; test with existing playbooks |
| Bundle size increase (React 19 bundled) | Low | Medium | Webpack tree-shaking + Terser; AnalysisWorkspace bundle is ~1 MB |
| Token expiration during long sessions | Medium | Medium | 4-minute proactive refresh (from AnalysisWorkspace pattern) |
| Missing Dataverse entity fields | Medium | Medium | Verify real table schemas before implementation |
| N:N relationship write from code page | Medium | Medium | Test `$batch` or associate/disassociate endpoints |

---

## 13. Verification Plan

### 13.1 Migration Verification

- [ ] All 7 node types render correctly on @xyflow/react v12 canvas
- [ ] Drag-and-drop from palette creates nodes at correct position
- [ ] Edge connections work (including condition branch routing)
- [ ] Existing playbook canvas JSON loads without errors
- [ ] Snap-to-grid, zoom, pan, fit view all functional
- [ ] MiniMap shows correct node colors by type
- [ ] Keyboard shortcuts work (Ctrl+Z, Ctrl+S, Delete)
- [ ] Dark mode renders correctly (ADR-021)

### 13.2 Dataverse Integration

- [ ] Skills load from `sprk_analysisskills` (not mock data)
- [ ] Knowledge loads from `sprk_aiknowledges` (not mock data)
- [ ] Tools load from `sprk_analysistools` (not mock data)
- [ ] Actions load from `sprk_analysisactions` (NEW)
- [ ] Model deployments load from `sprk_aimodeldeployments` (not mock data)
- [ ] Auto-save writes canvas JSON to `sprk_canvaslayoutjson`
- [ ] Node sync creates/updates/deletes `sprk_playbooknode` records
- [ ] Selected scope IDs are real Dataverse GUIDs (not 'skill-1', etc.)

### 13.3 Execution Integration (Per-Node ConfigJson)

- [ ] **AI Analysis node**: ConfigJson contains `modelDeploymentId`, `systemPrompt`, scope IDs; executor receives complete config
- [ ] **AI Completion node**: ConfigJson contains `systemPrompt`, `userPromptTemplate`, `temperature`, `maxTokens`; executor receives complete config
- [ ] **Deliver Output node**: ConfigJson contains `deliveryType`, `template` (Handlebars), output format options; executor renders formatted output (not raw JSON)
- [ ] **Send Email node**: ConfigJson contains `to`, `cc`, `subject`, `body`, `isHtml`; executor sends email with resolved template variables
- [ ] **Create Task node**: ConfigJson contains `subject`, `description`, `regardingObjectId`, `ownerId`, `dueDate`; executor creates Dataverse task record
- [ ] **Wait node**: ConfigJson contains `waitType`, `duration` or `untilDateTime`; executor pauses correctly
- [ ] **Condition node**: `conditionJson` evaluates correctly against upstream node outputs; branching routes to correct edge
- [ ] **Template variables**: `{{nodeName.output.fieldName}}` references resolve to actual upstream output values
- [ ] **Node validation**: Incomplete node config shows warning badge; prevents save with missing required fields

### 13.4 End-to-End

- [ ] Open code page from playbook form → canvas loads
- [ ] Design playbook → auto-save → close → reopen → canvas intact
- [ ] Node records exist in Dataverse after save
- [ ] Execute playbook from AnalysisWorkspace → node-based path activates
- [ ] All 7 node types execute end-to-end without ConfigJson errors
- [ ] Deliver Output produces formatted markdown/HTML (not raw JSON)
- [ ] AI Assistant chat works (SSE streaming to BFF API)
- [ ] Template library loads and applies templates

### 13.5 Graduation from R4 PCF

- [ ] Zero mock/hardcoded data in any store
- [ ] PCF control removed from solution XML
- [ ] Form updated to open code page instead of PCF
- [ ] No `react-flow-renderer` references remain
- [ ] All scope selectors query real Dataverse tables

---

## 14. Dependencies

### Internal

- AnalysisWorkspace code page (auth, build patterns)
- DocumentRelationshipViewer code page (@xyflow/react v12 patterns)
- BFF API (AI Assistant streaming endpoint)
- Dataverse dev environment with populated scope tables

### External

- @xyflow/react ^12.8.3
- d3-force ^3.0.0 (optional — for auto-layout)
- React 19.0.0
- Fluent UI v9
- Zustand 5.x
- @azure/msal-browser ^4.x
- Webpack 5

---

## 15. Out of Scope

- New node types beyond the existing 7 (adding new node types is out of scope; **building configuration forms for all 7 existing types IS in scope** — see Section 7)
- Playbook execution engine changes (BFF API / orchestration service / executors unchanged — they already read ConfigJson, the gap is that the canvas never writes it)
- AnalysisWorkspace changes (it already supports node-based execution)
- New Dataverse entities or schema changes (all tables exist; this project populates them correctly)
- Office add-in integration
- Staging/production deployment

---

*Generated: 2026-02-28 | Branch: work/ai-playbook-node-builder-r5*
