# Spike 050: RelationshipGrid Migration to Universal DatasetGrid Pattern

> **Task**: 050 (Research / Analysis)
> **Date**: 2026-02-25
> **Author**: Claude (investigation task)
> **Status**: Complete
> **Verdict**: Migration is feasible but must follow the **Scenario B** pattern from spike 003 -- use `GridView` directly, NOT `UniversalDatasetGrid`

---

## Executive Summary

The DocumentRelationshipViewer's `RelationshipGrid` is a self-contained Fluent UI v9 DataGrid component (320 lines) that displays document relationship data in tabular form. It uses the same low-level Fluent UI DataGrid primitives as the shared library's `GridView`, but with completely custom column definitions and domain-specific rendering (file-type icons, similarity scores, relationship badges, action buttons).

**Migration assessment**: The `RelationshipGrid` can be migrated to the `GridView` pattern from `@spaarke/ui-components`, but several domain-specific features require custom renderers that go beyond `ColumnRendererService`'s built-in Dataverse types. The recommended approach is to map `DocumentNode[]` to `IDatasetRecord[]` via an adapter hook, and use `GridView` with custom column renderers for the 3 specialized columns (document name with icon, relationship badges, similarity score).

**Estimated effort**: Medium (2-3 task files). The migration gains visual consistency and reuses infinite scroll / virtualization for free, but requires building a custom adapter and custom column renderers.

---

## 1. Current RelationshipGrid Implementation

### File Location

`src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx` (320 lines)

### Props

```typescript
export interface RelationshipGridProps {
    nodes: DocumentNode[];  // All nodes from the visualization API (including source)
    isDarkMode?: boolean;   // Currently accepted but unused in rendering
}
```

### Data Source and Transformation

Data flows as follows:

1. **API call**: `useVisualizationApi` hook fetches from `GET /api/ai/visualization/related/{documentId}` via `VisualizationApiService`
2. **API response mapping**: `VisualizationApiService.mapToGraphData()` transforms `ApiDocumentNode[]` into `@xyflow/react` `DocumentNode[]` objects
3. **Grid filtering**: `RelationshipGrid` filters out hub nodes (matter, project, invoice, email) from the node array, keeping only document nodes
4. **Grid row mapping**: Each `DocumentNode` is mapped to a `GridRow` (`{ id: string; data: DocumentNodeData }`)

```typescript
// Current filtering and row mapping (lines 139-146)
const rows = useMemo((): GridRow[] => {
    return nodes
        .filter((n) => {
            const nodeType = n.data.nodeType;
            return nodeType !== "matter" && nodeType !== "project"
                && nodeType !== "invoice" && nodeType !== "email";
        })
        .map((n) => ({ id: n.id, data: n.data }));
}, [nodes]);
```

### DocumentNodeData Fields Used by Grid

From `src/client/code-pages/DocumentRelationshipViewer/src/types/graph.ts`:

| Field | Type | Used In Column |
|-------|------|----------------|
| `name` | `string` | Document (primary) |
| `fileType` | `string` | Document (icon selection) |
| `isSource` | `boolean` | Document (source badge), Relationship, Similarity |
| `isOrphanFile` | `boolean` | Relationship |
| `relationshipLabel` | `string` | Relationship |
| `relationshipTypes` | `Array<{ type, label, similarity }>` | Relationship (multiple badges) |
| `similarity` | `number` | Similarity (percentage) |
| `documentType` | `string` | Type |
| `parentEntityName` | `string` | Parent Entity |
| `modifiedOn` | `string` | Modified (date) |
| `documentId` | `string` | Actions (open record) |
| `recordUrl` | `string` | Actions (open record) |
| `fileUrl` | `string` | Actions (view file) |

### Column Definitions (7 Columns)

| # | Column ID | Header | Sortable | Rendering Complexity | Notes |
|---|-----------|--------|----------|---------------------|-------|
| 1 | `name` | "Document" | Yes (localeCompare) | **HIGH** | File-type icon (12 cases) + text + optional "Source" badge |
| 2 | `relationship` | "Relationship" | Yes (localeCompare) | **HIGH** | 4-way conditional: Source badge, "File only" badge, multi-badge array, or single badge |
| 3 | `similarity` | "Similarity" | Yes (numeric desc) | **MEDIUM** | Percentage with 4-tier color coding (high/med/low/none) |
| 4 | `type` | "Type" | Yes (localeCompare) | **LOW** | Plain text (documentType or fileType fallback) |
| 5 | `parent` | "Parent Entity" | Yes (localeCompare) | **LOW** | Plain text with dash fallback |
| 6 | `modified` | "Modified" | Yes (localeCompare) | **LOW** | Date formatting (year, month short, day) |
| 7 | `actions` | "" (no header) | No | **MEDIUM** | Two icon buttons: "Open record" and "View in SharePoint" |

### Sorting

Client-side sorting via Fluent UI DataGrid's built-in `sortable` prop. Each column provides a `compare` function:
- String columns: `localeCompare()`
- Similarity: numeric comparison (descending by default)
- No server-side sort integration

### Selection

**None currently implemented.** The DataGrid uses `focusMode="composite"` but no selection mode or selection callbacks. No checkbox column.

### Row Actions

Two action buttons per row rendered inline in the last column:
1. **Open record** (`Open20Regular` icon) -- opens Dataverse entity record via `window.open()` to entity form URL
2. **View file** (`Globe20Regular` icon) -- opens SharePoint file URL via `window.open()`

Buttons are disabled when URLs are not available (orphan files, missing recordUrl/fileUrl).

### Styling

All styles use Fluent v9 `makeStyles` with design tokens:
- `tokens.colorNeutralBackground1` for container
- `tokens.colorBrandForeground1` for file icons and medium similarity
- `tokens.colorStatusSuccessForeground1` for high similarity
- `tokens.colorStatusWarningForeground1` for low similarity
- `tokens.colorNeutralForeground3` for no-similarity and empty states
- Custom styles for name cell layout (icon + text + badge in flex row)

### Empty State

Custom empty state with centered text: "No documents to display" / "Load a document with AI embeddings to see related documents".

---

## 2. Shared Library GridView Capabilities

### GridView Props (from `IGridViewProps`)

```typescript
export interface IGridViewProps {
  records: IDatasetRecord[];        // Plain data array
  columns: IDatasetColumn[];        // Column definitions
  selectedRecordIds: string[];      // Selection state
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
  enableVirtualization: boolean;    // Auto-switch to VirtualizedGridView at >1000 records
  rowHeight: number;                // Default 44px
  scrollBehavior: ScrollBehavior;   // "Auto" | "Infinite" | "Paged"
  loading: boolean;
  hasNextPage: boolean;
  loadNextPage: () => void;
}
```

### Features Available for Free

| Feature | GridView Support | RelationshipGrid Current | Benefit |
|---------|-----------------|-------------------------|---------|
| Sortable columns | Yes (built-in) | Yes (built-in) | Same |
| Resizable columns | Yes (`resizableColumns` prop) | No | **Gain** |
| Selection (multi) | Yes (`selectionMode`) | No | **Gain** |
| Infinite scroll | Yes (`scrollBehavior: "Infinite"`) | No (all rows rendered at once) | **Gain** |
| Virtualization | Yes (>1000 records auto-switch) | No | **Gain** |
| Loading state | Yes (`loading` prop + spinner) | No | **Gain** |
| Empty state | Yes (built-in) | Yes (custom) | Same (but less customizable) |
| Row click | Yes (`onRecordClick`) | No (action buttons instead) | Available |

### ColumnRendererService Capabilities

The shared `ColumnRendererService` provides renderers for Dataverse data types:

| Renderer | Applicable to RelationshipGrid? |
|----------|-------------------------------|
| `renderText` | Yes -- Type, Parent Entity columns |
| `renderDateOnly` / `renderDateTime` | Yes -- Modified column |
| `renderUrl` | Partial -- could use for fileUrl |
| `renderNumber` | Partial -- similarity is a decimal 0-1, needs percentage formatting |
| `renderOptionSet` (Badge) | Partial -- relationship type badges have custom colors |

**NOT covered by ColumnRendererService**:
- File-type icon selection (12-case switch on extension)
- Multi-badge array for relationship types with color mapping
- Similarity percentage with 4-tier conditional coloring
- Source badge indicator
- Dual-action button cell

---

## 3. Migration Plan

### Approach: Adapter Hook + GridView + Custom Column Renderers

#### Step 1: Create `useRelationshipGridAdapter` Hook

Maps `DocumentNode[]` to `IDatasetRecord[]` + `IDatasetColumn[]`:

```typescript
function mapNodeToRecord(node: DocumentNode): IDatasetRecord {
    return {
        id: node.id,
        entityName: "document_relationship",
        name: node.data.name,
        fileType: node.data.fileType ?? "file",
        isSource: node.data.isSource ?? false,
        isOrphanFile: node.data.isOrphanFile ?? false,
        relationshipLabel: node.data.relationshipLabel ?? "",
        relationshipTypes: JSON.stringify(node.data.relationshipTypes ?? []),
        similarity: node.data.similarity ?? 0,
        documentType: node.data.documentType ?? node.data.fileType?.toUpperCase() ?? "",
        parentEntityName: node.data.parentEntityName ?? "",
        modifiedOn: node.data.modifiedOn ?? "",
        documentId: node.data.documentId ?? "",
        recordUrl: node.data.recordUrl ?? "",
        fileUrl: node.data.fileUrl ?? "",
    };
}

const RELATIONSHIP_COLUMNS: IDatasetColumn[] = [
    { name: "name",             displayName: "Document",       dataType: "string",   isPrimary: true },
    { name: "relationshipLabel", displayName: "Relationship",  dataType: "string"    },
    { name: "similarity",       displayName: "Similarity",     dataType: "number"    },
    { name: "documentType",     displayName: "Type",           dataType: "string"    },
    { name: "parentEntityName", displayName: "Parent Entity",  dataType: "string"    },
    { name: "modifiedOn",       displayName: "Modified",       dataType: "datetime"  },
];
```

**Key issue**: `IDatasetRecord` uses `[key: string]: any`, so complex types like `relationshipTypes` array must be serialized or stored alongside primitive values. The adapter would store both the primitive value (for sorting/fallback) and the original complex data (for custom rendering).

#### Step 2: Custom Column Renderers

Three columns need custom renderers that go beyond `ColumnRendererService`:

**Document column** (name + icon + source badge):
- Create a custom `renderDocumentName` function
- Reuse existing `getFileIcon()` helper
- Render: `[icon] [name text] [optional "Source" badge]`

**Relationship column** (multi-badge):
- Create a custom `renderRelationshipBadges` function
- Parse `relationshipTypes` from the record
- Render conditional badge array with color mapping

**Similarity column** (percentage with color tiers):
- Create a custom `renderSimilarityScore` function
- Apply 4-tier color classes based on threshold

**Actions column**:
- This column does not map to `IDatasetColumn` at all -- it's a UI-only column
- Options: (a) Handle via `onRecordClick` prop, or (b) Add a custom last column outside the adapter

#### Step 3: Replace RelationshipGrid Component

Replace the current `RelationshipGrid` with:

```tsx
<GridView
    records={adaptedRecords}
    columns={adaptedColumns}
    selectedRecordIds={selectedIds}
    onSelectionChange={setSelectedIds}
    onRecordClick={handleRecordClick}
    enableVirtualization={false}     // Relationship data is typically <100 nodes
    rowHeight={44}
    scrollBehavior="Auto"
    loading={false}                  // Loading handled at App level
    hasNextPage={false}              // No pagination for relationship data
    loadNextPage={() => {}}
/>
```

---

## 4. What Changes vs. What Stays

### Changes

| Aspect | Current | After Migration |
|--------|---------|-----------------|
| DataGrid component | Direct Fluent UI `DataGrid` | Shared `GridView` from `@spaarke/ui-components` |
| Column definitions | Inline `createTableColumn<GridRow>()` | `IDatasetColumn[]` + custom renderers |
| Data type | `GridRow { id, data: DocumentNodeData }` | `IDatasetRecord` (flat, dynamic keys) |
| Row selection | None | Multi-select available (via `GridView` props) |
| Column resizing | Not available | Available (GridView enables `resizableColumns`) |
| Actions column | Inline buttons in last column | Either `onRecordClick` handler or kept as custom column |
| Empty state | Custom centered message | GridView's built-in empty state (less customizable) |
| Sorting | Custom `compare` functions per column | Same mechanism (GridView also uses `createTableColumn` with `compare`) |

### Stays the Same

| Aspect | Why |
|--------|-----|
| File-type icon logic | Custom business logic, no shared equivalent |
| Relationship badge coloring | Domain-specific color mapping |
| Similarity tier coloring | Domain-specific thresholds |
| Hub-node filtering | Pre-processing before grid display |
| `formatDate` helper | Shared `ColumnRendererService.renderDateOnly` can replace |
| `getRelationshipBadgeColor` | Domain-specific, stays as helper |

---

## 5. Risk Areas and Potential Issues

| Risk | Severity | Likelihood | Description | Mitigation |
|------|----------|------------|-------------|------------|
| **Custom renderers don't fit ColumnRendererService** | Medium | High | 3 of 7 columns need fully custom renderers that bypass `ColumnRendererService` | Create domain-specific renderer functions; `GridView` does not require use of `ColumnRendererService` (it's optional) |
| **Actions column has no `IDatasetColumn` equivalent** | Medium | High | The actions column is not data-bound; it's a UI control column | Option A: Move to `onRecordClick` with a context menu. Option B: Keep as a custom column appended outside the adapter. Option C: Add a "virtual" column in the adapter |
| **`IDatasetRecord` flattens complex types** | Low | Medium | `relationshipTypes` is an array of objects; `IDatasetRecord` uses `any` values | Store serialized JSON and parse in custom renderer; or store original array since `[key: string]: any` allows it |
| **Empty state customization** | Low | Low | GridView's built-in empty state shows "No records to display" (generic); current shows domain-specific message | Override by conditionally rendering custom empty state before GridView |
| **`isDarkMode` prop becomes unused** | Low | Low | Currently accepted but not used in rendering (tokens handle theming automatically) | Remove prop after migration |
| **Bundle size increase** | Low | Low | Importing `GridView` from shared library adds code | GridView is already a dependency via `@spaarke/ui-components`; no new packages needed |
| **Sorting behavior parity** | Low | Low | Current `compare` functions must be preserved in `createTableColumn` within GridView's column mapping | GridView uses the same Fluent `createTableColumn` API; exact same pattern |
| **SearchResultsGrid already chose direct Fluent DataGrid** | Informational | N/A | The SemanticSearch code page (task 030) chose to use Fluent DataGrid directly per spike 003 findings, NOT GridView | The RelationshipGrid migration could follow either pattern. Using `GridView` gains more shared features; using direct Fluent DataGrid matches the sibling component |

---

## 6. Complexity Estimate

| Work Item | Effort | Notes |
|-----------|--------|-------|
| Create `useRelationshipGridAdapter` hook | Small (1-2 hours) | Map DocumentNode[] to IDatasetRecord[] with flat structure |
| Create 3 custom column renderers | Medium (2-3 hours) | Document name, relationship badges, similarity score |
| Replace RelationshipGrid with GridView | Small (1 hour) | Swap component, wire props |
| Handle actions column | Small-Medium (1-2 hours) | Decide pattern: onRecordClick vs. custom column |
| Preserve empty state | Trivial | Conditional rendering before GridView |
| Verify sort behavior | Small (30 min) | Test all 6 sortable columns |
| **Total estimated effort** | **Medium (1-1.5 task files)** | Straightforward but requires custom renderers |

### Task File Recommendation

**One task file** if done standalone (migration only).
**Two task files** if combined with:
- Extracting shared file-type icon utilities to `@spaarke/ui-components`
- Adding row selection functionality
- Aligning with SemanticSearch's `SearchResultsGrid` pattern

---

## 7. Alternative: Direct Fluent DataGrid (Skip GridView)

The SemanticSearch code page's `SearchResultsGrid` (task 030) chose to use Fluent UI DataGrid directly, bypassing the shared `GridView`. This was a deliberate decision per spike 003 findings:

> "Uses Fluent UI DataGrid directly (not UniversalDatasetGrid from shared library) because the shared library GridView requires IDatasetRecord/IDatasetColumn from a separate package."

### Comparison of Approaches

| Aspect | GridView Migration | Keep Direct Fluent DataGrid |
|--------|-------------------|----------------------------|
| Code reuse | Higher (shared library) | Lower (duplicate grid patterns) |
| Customization | Constrained by IDatasetColumn | Full control over column rendering |
| Bundle impact | None (already a dependency) | None |
| Consistency with SearchResultsGrid | Different pattern | Same pattern |
| Gained features | Resizable columns, virtualization, selection | Only what we explicitly build |
| Implementation effort | Medium (adapter + renderers) | Low (already working) |
| Maintenance | Shared library upgrades apply | Must maintain independently |

### Recommendation

**If visual consistency with SearchResultsGrid is the priority**: Keep the direct Fluent DataGrid pattern. Both code pages use the same low-level DataGrid and can share helper functions (icon mapping, badge coloring) without going through the shared library abstraction.

**If maximizing shared library adoption per ADR-012 is the priority**: Migrate to GridView. This aligns with the project's CLAUDE.md which states `ADR-012: Import UniversalDatasetGrid, ViewSelector from @spaarke/ui-components`.

**Balanced recommendation**: Migrate to `GridView` for the RelationshipGrid because:
1. ADR-012 compliance is a stated project constraint
2. The relationship data is small (typically <100 nodes), so GridView's pagination/virtualization adds safety without cost
3. The SemanticSearch `SearchResultsGrid` may also be migrated later for consistency

---

## 8. Files Examined

| File | Absolute Path | Key Finding |
|------|---------------|-------------|
| RelationshipGrid.tsx | `src/client/code-pages/DocumentRelationshipViewer/src/components/RelationshipGrid.tsx` | 320 lines; 7 columns with 3 complex renderers; no selection; client-side sort |
| App.tsx | `src/client/code-pages/DocumentRelationshipViewer/src/App.tsx` | Manages view toggle (graph/grid); passes `nodes` directly to RelationshipGrid |
| graph.ts (types) | `src/client/code-pages/DocumentRelationshipViewer/src/types/graph.ts` | `DocumentNodeData` with 18 fields; extends `Record<string, unknown>` |
| api.ts (types) | `src/client/code-pages/DocumentRelationshipViewer/src/types/api.ts` | API response types; `DocumentGraphResponse`, `ApiDocumentNode` |
| useVisualizationApi.ts | `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useVisualizationApi.ts` | Data-fetching hook; returns `{ nodes, edges, metadata }` |
| VisualizationApiService.ts | `src/client/code-pages/DocumentRelationshipViewer/src/services/VisualizationApiService.ts` | Maps API response to `@xyflow/react` node/edge format; builds relationship map |
| UniversalDatasetGrid.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx` | Top-level orchestrator; Dataverse-coupled; NOT the migration target |
| GridView.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx` | Inner component; accepts `IDatasetRecord[]`; **THE migration target** |
| VirtualizedGridView.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/VirtualizedGridView.tsx` | Large-dataset support via react-window; auto-selected by GridView |
| CardView.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/CardView.tsx` | Alternative view mode; not applicable to relationship data |
| ListView.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx` | Alternative view mode; not applicable to relationship data |
| ViewSelector.tsx | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx` | Dataverse-bound view picker; NOT usable for relationship data |
| DatasetTypes.ts | `src/client/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts` | `IDatasetRecord` (open shape), `IDatasetColumn`, `IDatasetConfig` |
| ColumnRendererService.tsx | `src/client/shared/Spaarke.UI.Components/src/services/ColumnRendererService.tsx` | Type-based cell renderers for Dataverse types; covers 2 of 7 columns (text, date) |
| useHeadlessMode.ts | `src/client/shared/Spaarke.UI.Components/src/hooks/useHeadlessMode.ts` | Dataverse WebAPI + FetchXML; NOT usable for BFF data |
| useDatasetMode.ts | `src/client/shared/Spaarke.UI.Components/src/hooks/useDatasetMode.ts` | PCF dataset extraction; NOT usable outside PCF |
| SearchResultsGrid.tsx | `src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx` | Sibling code page's grid; uses direct Fluent DataGrid (not GridView) |
| grid-headless-adapter.md | `projects/ai-semantic-search-ui-r3/notes/spikes/grid-headless-adapter.md` | Prior spike confirming Scenario B: GridView inner components are Dataverse-agnostic |
