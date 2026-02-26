# Universal Dataset Grid — Multi-Source Adaptation Spec

> **Status**: Draft
> **Created**: 2026-02-26
> **Scope**: Extend `UniversalDatasetGrid` shared library to support Dataverse, Azure AI, API, and combined data sources; migrate EventsPage and Semantic Search to standardized component.

---

## 1. Executive Summary

The `UniversalDatasetGrid` component in `@spaarke/ui-components` currently supports two data modes:
- **PCF Dataset mode** — binds to `ComponentFramework.PropertyTypes.DataSet`
- **Headless mode** — fetches via `ComponentFramework.WebApi` + FetchXML

Both modes are Dataverse-centric. This spec extends the component with a **third data mode** — **External Data mode** — that accepts pre-fetched data from any source (Azure AI Search, BFF APIs, external services). It also defines a **Combined mode** pattern for scenarios requiring Dataverse enrichment of Azure-sourced records.

### Goals

1. **Single grid component** for all Spaarke data surfaces (forms, code pages, custom pages)
2. **Data-source agnostic** — Dataverse, Azure AI Search, REST APIs, combined
3. **Column definitions from Dataverse** — `sprk_gridconfiguration` table drives view/column config
4. **EventsPage migration path** — concrete plan to retire 1700-line bespoke `GridSection.tsx`
5. **Semantic Search adoption** — replace custom `SearchResultsGrid.tsx` with standardized component

---

## 2. Current Architecture

### 2.1 UniversalDatasetGrid Component (Shared Library)

**Location**: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/`

```
UniversalDatasetGrid.tsx  ← Orchestrator (data mode → view routing)
├── GridView.tsx           ← Fluent UI DataGrid (standard)
├── CardView.tsx           ← Card/tile layout
├── ListView.tsx           ← Compact list
├── VirtualizedGridView.tsx ← >1000 records
└── VirtualizedListView.tsx
```

**Key Interfaces (DatasetTypes.ts)**:

```typescript
interface IDatasetRecord {
  id: string;
  entityName: string;
  [key: string]: any;
}

interface IDatasetColumn {
  name: string;
  displayName: string;
  dataType: string;       // DataverseAttributeType enum values
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
  isSecured?: boolean;
  canRead?: boolean;
  canUpdate?: boolean;
  canCreate?: boolean;
}
```

**Current Props**:

```typescript
interface IUniversalDatasetGridProps {
  config?: IDatasetConfig;
  configJson?: string;
  dataset?: ComponentFramework.PropertyTypes.DataSet;  // Mode 1: PCF
  headlessConfig?: {                                    // Mode 2: Headless
    webAPI: ComponentFramework.WebApi;
    entityName: string;
    fetchXml?: string;
    pageSize: number;
  };
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (recordId: string) => void;
  context: any;
}
```

### 2.2 ColumnRendererService

Built-in type renderers (Dataverse-centric):

| DataType | Renderer | Output |
|----------|----------|--------|
| `SingleLine.Text` | `renderText` | Plain text |
| `SingleLine.Email` | `renderEmail` | `mailto:` link |
| `SingleLine.Phone` | `renderPhone` | `tel:` link |
| `SingleLine.URL` | `renderUrl` | Clickable link |
| `Currency` | `renderMoney` | Locale currency |
| `DateAndTime.DateAndTime` | `renderDateTime` | Locale date+time |
| `DateAndTime.DateOnly` | `renderDateOnly` | Locale date |
| `TwoOptions` | `renderTwoOptions` | Check/X icon |
| `OptionSet` | `renderOptionSet` | Fluent Badge |
| `MultiSelectOptionSet` | `renderMultiSelectOptionSet` | Multiple Badges |
| `Lookup.Simple` | `renderLookup` | Link with display name |
| `Boolean` | `renderBoolean` | Check/X icon |

### 2.3 Consumers to Migrate

| Consumer | Current Pattern | Lines | Data Source |
|----------|----------------|-------|-------------|
| **Semantic Search** (`SearchResultsGrid.tsx`) | Custom Fluent DataGrid | ~365 | BFF API (Azure AI Search) |
| **EventsPage** (`GridSection.tsx`) | Raw `<table>` + Fluent styles | ~1709 | Dataverse FetchXML/OData |
| **PCF Forms** (various) | UniversalDatasetGrid PCF control | — | PCF Dataset binding |

---

## 3. Proposed Architecture

### 3.1 Three Data Modes

```
┌─────────────────────────────────────────────┐
│          UniversalDatasetGrid               │
│   (orchestrator + view routing)             │
├─────────────────────────────────────────────┤
│                                             │
│  Mode 1: PCF Dataset                        │
│    useDatasetMode(dataset)                  │
│    → IDatasetResult                         │
│                                             │
│  Mode 2: Headless (Dataverse WebAPI)        │
│    useHeadlessMode(webAPI, fetchXml)        │
│    → IDatasetResult                         │
│                                             │
│  Mode 3: External Data  ← NEW              │
│    useExternalDataMode(externalConfig)      │
│    → IDatasetResult                         │
│                                             │
├─────────────────────────────────────────────┤
│  View Routing:                              │
│    GridView | CardView | ListView           │
│    (+ VirtualizedGridView for >1000 rows)   │
└─────────────────────────────────────────────┘
```

### 3.2 New External Data Mode

**Purpose**: Accept pre-fetched records from any source — the calling component owns data fetching; the grid owns display.

```typescript
/**
 * External Data configuration — data provided by caller, not fetched by grid.
 * Supports Azure AI Search results, BFF API responses, or any JSON array.
 */
interface IExternalDataConfig {
  /** Pre-fetched records mapped to IDatasetRecord shape. */
  records: IDatasetRecord[];

  /** Column definitions for the current view. */
  columns: IDatasetColumn[];

  /** Whether initial data load is in progress (shows loading overlay). */
  loading: boolean;

  /** Error message to display, if any. */
  error?: string | null;

  /** Total count of matching records (may exceed records.length). */
  totalCount?: number;

  /** Whether more records are available for infinite scroll. */
  hasNextPage?: boolean;

  /** Callback to load next page. Grid calls this when scroll sentinel is reached. */
  onLoadNextPage?: () => void;

  /** Callback to refresh data. Grid calls this from toolbar refresh button. */
  onRefresh?: () => void;
}
```

**Updated Props**:

```typescript
interface IUniversalDatasetGridProps {
  config?: IDatasetConfig;
  configJson?: string;

  // Data Source (mutually exclusive — pick ONE)
  dataset?: ComponentFramework.PropertyTypes.DataSet;     // Mode 1: PCF
  headlessConfig?: IHeadlessConfig;                       // Mode 2: Dataverse WebAPI
  externalConfig?: IExternalDataConfig;                   // Mode 3: External Data ← NEW

  // Selection
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;

  // Actions
  onRecordClick: (recordId: string) => void;

  // Context (for theme detection — optional for external mode)
  context?: any;
}
```

### 3.3 `useExternalDataMode` Hook

Thin adapter that wraps caller-provided data into `IDatasetResult`:

```typescript
// hooks/useExternalDataMode.ts

interface IUseExternalDataModeProps {
  config: IExternalDataConfig;
}

function useExternalDataMode(props: IUseExternalDataModeProps): IDatasetResult {
  const { config } = props;

  return {
    records: config.records,
    columns: config.columns,
    loading: config.loading,
    error: config.error ?? null,
    totalRecordCount: config.totalCount ?? config.records.length,
    hasNextPage: config.hasNextPage ?? false,
    hasPreviousPage: false,
    loadNextPage: config.onLoadNextPage ?? (() => {}),
    loadPreviousPage: () => {},
    refresh: config.onRefresh ?? (() => {}),
  };
}
```

### 3.4 Extended ColumnRendererService

Add non-Dataverse data types for Azure AI Search and API results:

```typescript
// New data type constants (extend DataverseAttributeType enum)
enum ExtendedDataType {
  // --- Existing Dataverse types (unchanged) ---

  // --- New: AI Search / API types ---
  Percentage = "Percentage",           // 0.0–1.0 → "75%"
  ScoreBar = "ScoreBar",              // 0.0–1.0 → visual progress bar
  StringArray = "StringArray",         // string[] → comma-separated
  EntityLink = "EntityLink",           // { id, name, entityType } → navigable link
  FileType = "FileType",              // "pdf", "docx" → icon + label
  Duration = "Duration",              // ISO 8601 duration → human readable
  Json = "Json",                       // Raw JSON → formatted code block
}
```

**New Renderers**:

| DataType | Renderer | Use Case |
|----------|----------|----------|
| `Percentage` | `renderPercentage` | Similarity/confidence scores |
| `ScoreBar` | `renderScoreBar` | Visual relevance indicator bar |
| `StringArray` | `renderStringArray` | Reference numbers, tags |
| `EntityLink` | `renderEntityLink` | Parent entity, parent matter links |
| `FileType` | `renderFileType` | Icon for pdf/docx/xlsx |

### 3.5 Custom Renderer Registration

Allow consumers to register domain-specific renderers:

```typescript
// services/ColumnRendererService.tsx — add to existing class

class ColumnRendererService {
  // Existing static renderers...

  /** Custom renderer registry — keyed by dataType string. */
  private static customRenderers = new Map<string, ColumnRenderer>();

  /**
   * Register a custom renderer for a data type.
   * Custom renderers take priority over built-in renderers.
   */
  static registerRenderer(dataType: string, renderer: ColumnRenderer): void {
    this.customRenderers.set(dataType, renderer);
  }

  /** Unregister a custom renderer. */
  static unregisterRenderer(dataType: string): void {
    this.customRenderers.delete(dataType);
  }

  /** Updated getRenderer — checks custom registry first. */
  static getRenderer(column: IDatasetColumn): ColumnRenderer {
    // Custom renderer takes priority
    if (this.customRenderers.has(column.dataType)) {
      return this.customRenderers.get(column.dataType)!;
    }

    // Secured field check
    if (column.isSecured && column.canRead === false) {
      return this.renderSecuredField;
    }

    // Built-in Dataverse renderers (existing switch statement)
    // ...
  }
}
```

---

## 4. Data Mapping Layer

### 4.1 Mapping Search Results → IDatasetRecord

Each consumer provides a thin adapter function. These live alongside the consumer, not in the shared library.

**Semantic Search — Document Results**:

```typescript
// adapters/searchResultAdapter.ts (in SemanticSearch code page)

function mapDocumentResult(result: DocumentSearchResult): IDatasetRecord {
  return {
    id: result.documentId,
    entityName: "sprk_document",
    // Indexed fields (from Azure AI Search)
    name: result.name,
    combinedScore: result.combinedScore,
    documentType: result.documentType,
    fileType: result.fileType,
    parentEntityName: result.parentEntityName,
    updatedAt: result.updatedAt,
    // Metadata
    _sourceType: "azure-ai-search",
  };
}

function mapRecordResult(result: RecordSearchResult): IDatasetRecord {
  return {
    id: result.recordId,
    entityName: result.entityType,  // "sprk_matter", "sprk_project", "sprk_invoice"
    recordName: result.recordName,
    confidenceScore: result.confidenceScore,
    referenceNumbers: result.referenceNumbers,
    organizations: result.organizations,
    modifiedAt: result.modifiedAt,
    // Enriched fields (from Dataverse lookup, if available)
    matterType: result.matterType,
    practiceArea: result.practiceArea,
    status: result.status,
    parentMatter: result.parentMatter,
    amount: result.amount,
    vendor: result.vendor,
    _sourceType: "azure-ai-search",
  };
}
```

**EventsPage — Event Records**:

```typescript
// adapters/eventRecordAdapter.ts (in EventsPage)

function mapEventRecord(event: IEventRecord): IDatasetRecord {
  return {
    id: event.sprk_eventid,
    entityName: "sprk_event",
    ...event,  // All OData fields pass through (formatted values included)
    _sourceType: "dataverse",
  };
}
```

### 4.2 Mapping Column Definitions → IDatasetColumn

**From `sprk_gridconfiguration` JSON** (view definition):

```typescript
// services/viewDefinitionService.ts

interface IViewColumnDef {
  key: string;           // Field name
  label: string;         // Display name
  width?: number;        // Pixel width
  dataType?: string;     // DataverseAttributeType or ExtendedDataType
  sortable?: boolean;
  render?: string;       // Named renderer ("percentage", "date", "currency", etc.)
}

function mapViewColumnToDatasetColumn(col: IViewColumnDef): IDatasetColumn {
  return {
    name: col.key,
    displayName: col.label,
    dataType: col.dataType ?? resolveDataType(col.render),
    visualSizeFactor: col.width ? col.width / 100 : undefined,
    isKey: false,
    isPrimary: false,
  };
}

/** Map named renderer to DataverseAttributeType/ExtendedDataType. */
function resolveDataType(render?: string): string {
  switch (render) {
    case "percentage": return "Percentage";
    case "date": return "DateAndTime.DateOnly";
    case "dateTime": return "DateAndTime.DateAndTime";
    case "currency": return "Currency";
    case "array": return "StringArray";
    case "entityLink": return "EntityLink";
    case "fileType": return "FileType";
    default: return "SingleLine.Text";
  }
}
```

**Fallback from hardcoded config** (current `domainColumns.ts` pattern):

```typescript
function mapGridColumnDefToDatasetColumn(col: GridColumnDef): IDatasetColumn {
  return {
    name: col.key,
    displayName: col.label,
    dataType: col.render ? inferDataType(col.render) : "SingleLine.Text",
    visualSizeFactor: col.width ? col.width / 100 : undefined,
  };
}
```

### 4.3 Combined Data Pattern (Dataverse + Azure)

For scenarios where Azure AI Search returns core fields and Dataverse provides enrichment (e.g., Matter's `practiceArea`, `matterType`):

```
┌──────────────┐     ┌───────────────────┐     ┌──────────────┐
│ Azure AI     │     │  Combine/Enrich   │     │ Grid Display │
│ Search API   │────▶│  (client-side)    │────▶│ (IDatasetRecord[]) │
│ (core fields)│     │                   │     │              │
└──────────────┘     │  Join on recordId │     └──────────────┘
                     │                   │
┌──────────────┐     │                   │
│ Dataverse    │────▶│                   │
│ WebAPI       │     └───────────────────┘
│ (enrichment) │
└──────────────┘
```

**Implementation — `useEnrichedResults` hook**:

```typescript
// hooks/useEnrichedResults.ts (in consuming code page, not shared library)

interface IEnrichmentConfig {
  /** Records from primary source (e.g., Azure AI Search). */
  primaryRecords: IDatasetRecord[];

  /** Entity name for Dataverse enrichment query. */
  enrichEntityName: string;

  /** Fields to fetch from Dataverse for enrichment. */
  enrichFields: string[];

  /** Field on primary record that maps to Dataverse record ID. */
  primaryIdField: string;

  /** Whether enrichment is enabled (skip for domains with no enriched fields). */
  enabled: boolean;
}

function useEnrichedResults(config: IEnrichmentConfig): {
  records: IDatasetRecord[];
  isEnriching: boolean;
} {
  // 1. Collect record IDs from primary results
  // 2. Batch-fetch from Dataverse WebAPI:
  //    GET /api/data/v9.2/{entitySet}?$select={enrichFields}&$filter=({idField} eq '{id1}' or ...)
  // 3. Merge enrichment fields into primary records
  // 4. Return merged records

  // Implementation handles:
  //   - Batching (max 50 IDs per request to avoid URL length limits)
  //   - Caching (don't re-fetch already enriched records)
  //   - Graceful degradation (show primary data immediately, enrich in background)
  //   - Missing records (some IDs may not exist in Dataverse)
}
```

**Usage in Semantic Search App.tsx**:

```typescript
// Map search results to IDatasetRecord
const primaryRecords = useMemo(
  () => activeResults.map(r => isDocDomain ? mapDocumentResult(r) : mapRecordResult(r)),
  [activeResults, isDocDomain]
);

// Enrich with Dataverse fields (matters: matterType, practiceArea; projects: status, parentMatter)
const { records: enrichedRecords, isEnriching } = useEnrichedResults({
  primaryRecords,
  enrichEntityName: domainToEntity(activeDomain),
  enrichFields: getEnrichFieldsForDomain(activeDomain),
  primaryIdField: "id",
  enabled: activeDomain !== "documents", // Documents don't need enrichment
});

// Pass to grid
<UniversalDatasetGrid
  externalConfig={{
    records: enrichedRecords,
    columns: activeColumns,   // from sprk_gridconfiguration or fallback
    loading: isSearching,
    hasNextPage: hasMore,
    onLoadNextPage: handleLoadMore,
    onRefresh: handleRefresh,
  }}
  config={{ viewMode: "Grid", selectionMode: "Multiple", ... }}
  selectedRecordIds={selectedIds}
  onSelectionChange={handleSelectionChange}
  onRecordClick={handleResultClick}
/>
```

**Enrichment field map by domain**:

| Domain | Entity | Enriched Fields (from Dataverse) |
|--------|--------|----------------------------------|
| Documents | `sprk_document` | None (all fields indexed) |
| Matters | `sprk_matter` | `sprk_mattertype`, `sprk_practicearea` |
| Projects | `sprk_project` | `sprk_status`, `_sprk_parentmatter_value` |
| Invoices | `sprk_invoice` | `sprk_amount`, `sprk_vendor`, `_sprk_parentmatter_value`, `sprk_invoicedate` |

---

## 5. View Definitions in `sprk_gridconfiguration` (Option A)

### 5.1 Schema (No Changes Required)

Uses existing `sprk_gridconfiguration` fields:

| Field | Usage for View Definitions |
|-------|---------------------------|
| `sprk_name` | View display name (e.g., "Document Details", "Matter Summary") |
| `sprk_entitylogicalname` | Domain scoping (e.g., `"semantic_search_documents"`, `"semantic_search_matters"`) |
| `sprk_viewtype` | Value `2` (CustomFetchXML) — reused with JSON discriminator |
| `sprk_configjson` | Full view definition JSON (see schema below) |
| `sprk_isdefault` | `true` for the default view per domain |
| `sprk_sortorder` | Display order in view selector dropdown |
| `sprk_iconname` | Optional icon for the view selector |
| `sprk_description` | Admin description |

### 5.2 `sprk_configjson` Schema for View Definitions

```json
{
  "_type": "semantic-search-view",
  "_version": 1,
  "domain": "documents",
  "columns": [
    {
      "key": "name",
      "label": "Document",
      "width": 400,
      "dataType": "SingleLine.Text",
      "sortable": true,
      "isPrimary": true
    },
    {
      "key": "combinedScore",
      "label": "Similarity",
      "width": 100,
      "dataType": "Percentage",
      "sortable": true
    },
    {
      "key": "documentType",
      "label": "Type",
      "width": 120,
      "dataType": "SingleLine.Text",
      "sortable": true
    },
    {
      "key": "fileType",
      "label": "File Type",
      "width": 90,
      "dataType": "FileType",
      "sortable": true
    },
    {
      "key": "parentEntityName",
      "label": "Parent Entity",
      "width": 180,
      "dataType": "EntityLink",
      "sortable": true
    },
    {
      "key": "updatedAt",
      "label": "Modified",
      "width": 120,
      "dataType": "DateAndTime.DateOnly",
      "sortable": true
    }
  ],
  "defaultSort": {
    "column": "combinedScore",
    "direction": "desc"
  }
}
```

### 5.3 Discriminator Convention

| `_type` Value | Purpose | `sprk_entitylogicalname` |
|---------------|---------|--------------------------|
| `"semantic-search-view"` | View/column definition for grid | `"semantic_search_{domain}"` |
| `"semantic-search"` | Saved search (query + filters + view state) | `"semantic_search"` |
| `"grid-config"` | Standard UniversalDatasetGrid config (existing) | Entity logical name |

### 5.4 Default View Records (Seed Data)

Pre-populate 4 records in `sprk_gridconfiguration` — one per search domain:

| `sprk_name` | `sprk_entitylogicalname` | `sprk_isdefault` | Domain |
|-------------|--------------------------|-------------------|--------|
| Document Search Results | `semantic_search_documents` | `true` | documents |
| Matter Search Results | `semantic_search_matters` | `true` | matters |
| Project Search Results | `semantic_search_projects` | `true` | projects |
| Invoice Search Results | `semantic_search_invoices` | `true` | invoices |

### 5.5 View Fetching Hook

```typescript
// hooks/useSearchViewDefinitions.ts

interface SearchViewDefinition {
  id: string;
  name: string;
  domain: SearchDomain;
  columns: IViewColumnDef[];
  defaultSort?: { column: string; direction: "asc" | "desc" };
  isDefault: boolean;
  sortOrder: number;
}

function useSearchViewDefinitions(domain: SearchDomain): {
  views: SearchViewDefinition[];
  activeView: SearchViewDefinition | null;
  setActiveView: (viewId: string) => void;
  isLoading: boolean;
  error: string | null;
} {
  // 1. Query sprk_gridconfiguration:
  //    $filter=sprk_entitylogicalname eq 'semantic_search_{domain}'
  //            and statecode eq 0
  //    $orderby=sprk_sortorder asc
  //
  // 2. Parse sprk_configjson, validate _type === "semantic-search-view"
  //
  // 3. Map to SearchViewDefinition[]
  //
  // 4. Set activeView to isDefault=true record (or first)
  //
  // 5. Cache with 5-minute TTL (same pattern as ConfigurationService)
}
```

### 5.6 Fallback Strategy

```
1. Try: Fetch views from sprk_gridconfiguration for active domain
2. If found: Use column definitions from configjson
3. If not found (or fetch fails): Fall back to hardcoded domainColumns.ts
4. If domainColumns.ts has no entry: Use record keys as auto-columns (existing headless behavior)
```

This ensures the grid always renders, even before Dataverse view records are seeded.

---

## 6. EventsPage Migration Plan

### 6.1 Current State

The EventsPage (`src/solutions/EventsPage/`) is a Dataverse Custom Page with a 1709-line `GridSection.tsx` that:
- Uses raw HTML `<table>` with Fluent styling
- Fetches data via FetchXML from Dataverse saved views (`savedquery`)
- Parses `layoutXml` for dynamic columns
- Has custom cell renderers for event name, regarding links, status badges, priority colors
- Has `ColumnHeaderMenu` for per-column filtering
- Has client-side sorting

### 6.2 Migration Strategy

**Phase 1: Adopt UniversalDatasetGrid with Headless Mode**

The EventsPage already fetches from Dataverse, so it maps cleanly to headless mode:

```typescript
// Before (1709 lines of custom grid code)
<GridSection
  viewId={selectedViewId}
  calendarFilter={calendarFilter}
  ...
/>

// After (~50 lines)
<UniversalDatasetGrid
  headlessConfig={{
    webAPI: Xrm.WebApi,
    entityName: "sprk_event",
    fetchXml: enhancedFetchXml,  // With date filter and context filter injected
    pageSize: 50,
  }}
  config={{
    viewMode: "Grid",
    selectionMode: "Multiple",
    showToolbar: true,
    enabledCommands: ["open", "refresh", "complete", "close", "cancel"],
    scrollBehavior: "Infinite",
    rowHeight: 44,
    enableVirtualization: true,
    theme: "Auto",
  }}
  selectedRecordIds={selectedEventIds}
  onSelectionChange={setSelectedEventIds}
  onRecordClick={handleEventClick}
  context={context}
/>
```

**Phase 2: Register Custom Renderers for Events**

Events need custom cell rendering that doesn't exist in the built-in renderer set:

```typescript
// Register event-specific renderers at EventsPage init
ColumnRendererService.registerRenderer("EventName", (value, record) => (
  <Link onClick={() => openEventSidePane(record.id)}>
    {value || "—"}
  </Link>
));

ColumnRendererService.registerRenderer("EventStatus", (value, record) => (
  <Badge
    appearance={getStatusAppearance(value)}
    color={getStatusColor(value)}
  >
    {record["sprk_eventstatus@OData.Community.Display.V1.FormattedValue"] || value}
  </Badge>
));

ColumnRendererService.registerRenderer("EventPriority", (value, record) => {
  const color = value === 1 ? "danger" : value === 3 ? "subtle" : "informative";
  return (
    <Badge appearance="outline" color={color}>
      {record["sprk_priority@OData.Community.Display.V1.FormattedValue"] || value}
    </Badge>
  );
});
```

**Phase 3: Move Column Filtering to Shared Library**

Extract `ColumnHeaderMenu` from EventsPage into `@spaarke/ui-components`:

```
src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/
  ├── ColumnHeaderMenu.tsx   ← Extracted from EventsPage
  ├── GridView.tsx           ← Updated to support column filtering
  └── ...
```

**Phase 4: View-Driven Columns via sprk_gridconfiguration**

EventsPage currently reads `savedquery.layoutXml`. Migrate to read from `sprk_gridconfiguration` instead, using the same pattern as Semantic Search:

```typescript
// EventsPage stores its view configs in sprk_gridconfiguration too
{
  "_type": "events-page-view",
  "_version": 1,
  "domain": "sprk_event",
  "columns": [
    { "key": "sprk_eventname", "label": "Event Name", "width": 200, "dataType": "EventName" },
    { "key": "sprk_duedate", "label": "Due Date", "width": 150, "dataType": "DateAndTime.DateOnly" },
    { "key": "sprk_eventstatus", "label": "Status", "width": 100, "dataType": "EventStatus" },
    ...
  ]
}
```

Or, keep supporting `savedquery.layoutXml` via an adapter that maps `LayoutColumn[]` → `IDatasetColumn[]`:

```typescript
function mapLayoutColumnToDatasetColumn(lc: LayoutColumn): IDatasetColumn {
  return {
    name: lc.name,
    displayName: lc.label,
    dataType: inferDataTypeFromField(lc.name, lc.isLookup),
    visualSizeFactor: lc.width / 100,
  };
}
```

### 6.3 Migration Effort Estimate

| Phase | Scope | Shared Library Changes | EventsPage Changes |
|-------|-------|----------------------|-------------------|
| 1 | Adopt headless mode | None (already supported) | Replace `<table>` with `<UniversalDatasetGrid>` |
| 2 | Custom renderers | Add `registerRenderer()` API | Register event renderers at init |
| 3 | Column filtering | Extract ColumnHeaderMenu | Remove bespoke filtering code |
| 4 | View-driven columns | None (or add layoutXml adapter) | Switch column source |

### 6.4 Risk Mitigation

- **Feature parity**: EventsPage has bespoke features (calendar filter injection, regarding navigation, status bulk actions). These remain in EventsPage — only the grid rendering migrates.
- **Regression**: Keep `GridSection.tsx` during transition. Feature-flag the new grid (`useUniversalGrid` config). Remove old code after validation.
- **Performance**: EventsPage loads 500+ events. UniversalDatasetGrid already virtualizes at >1000 records. No regression expected.

---

## 7. Semantic Search Migration Plan

### 7.1 Current State

`SearchResultsGrid.tsx` (365 lines) uses Fluent UI DataGrid directly with:
- Domain-specific columns from `domainColumns.ts`
- Custom renderers (percentage, date, currency, array)
- Multi-row selection, infinite scroll, loading overlay
- No column filtering or column customization

### 7.2 Migration Steps

**Step 1: Add adapter mapping**

Create `adapters/searchResultAdapter.ts` with `mapDocumentResult()` and `mapRecordResult()` functions (Section 4.1).

**Step 2: Add view definition hook**

Create `hooks/useSearchViewDefinitions.ts` to fetch column configs from `sprk_gridconfiguration` with fallback to `domainColumns.ts`.

**Step 3: Register search-specific renderers**

```typescript
// At SemanticSearch app init
ColumnRendererService.registerRenderer("Percentage", (value) => {
  const score = typeof value === "number" ? value : 0;
  return `${Math.round(score * 100)}%`;
});

ColumnRendererService.registerRenderer("StringArray", (value) => {
  if (Array.isArray(value)) return value.join(", ");
  return typeof value === "string" ? value : "";
});

ColumnRendererService.registerRenderer("FileType", (value) => {
  // Could show file type icon in future
  return typeof value === "string" ? value.toUpperCase() : "";
});
```

**Step 4: Replace SearchResultsGrid with UniversalDatasetGrid**

In `App.tsx`, replace:

```tsx
// Before
<SearchResultsGrid
  results={activeResults}
  totalCount={totalCount}
  isLoading={isSearching}
  isLoadingMore={isLoadingMore}
  hasMore={hasMore}
  activeDomain={activeDomain}
  columns={domainColumns}
  onLoadMore={handleLoadMore}
  onSelectionChange={handleSelectionChange}
  onSort={handleSort}
/>

// After
<UniversalDatasetGrid
  externalConfig={{
    records: enrichedRecords,
    columns: viewColumns,
    loading: isSearching,
    hasNextPage: hasMore,
    onLoadNextPage: handleLoadMore,
    onRefresh: () => executeSearch(query, filters, activeDomain),
    totalCount,
  }}
  config={{
    viewMode: "Grid",
    selectionMode: "Multiple",
    showToolbar: false,
    enabledCommands: [],
    scrollBehavior: "Infinite",
    rowHeight: 44,
    enableVirtualization: true,
    theme: "Auto",
  }}
  selectedRecordIds={selectedIds}
  onSelectionChange={handleSelectionChange}
  onRecordClick={handleResultClick}
/>
```

**Step 5: Add ViewSelector dropdown** (optional enhancement)

If multiple view definitions exist per domain, add a view selector:

```tsx
<ViewSelector
  views={viewDefinitions}
  activeViewId={activeViewId}
  onViewChange={setActiveViewId}
/>
```

**Step 6: Delete retired files**

- `src/components/SearchResultsGrid.tsx` — replaced
- `src/config/domainColumns.ts` — becomes fallback only (keep for now)

### 7.3 Enrichment Integration

For Matters/Projects/Invoices, add the combined data pattern:

```tsx
const { records: enrichedRecords, isEnriching } = useEnrichedResults({
  primaryRecords: mappedResults,
  enrichEntityName: domainToEntity(activeDomain),
  enrichFields: ENRICH_FIELDS[activeDomain],
  primaryIdField: "id",
  enabled: activeDomain !== "documents",
});
```

Grid shows primary data immediately. Enriched columns populate as Dataverse responses arrive (progressive loading UX — skeleton cells → real data).

---

## 8. Shared Library Changes Summary

### 8.1 New Files

| File | Location | Purpose |
|------|----------|---------|
| `useExternalDataMode.ts` | `hooks/` | External data mode hook |
| `ExtendedDataTypes.ts` | `types/` | Non-Dataverse data type constants |

### 8.2 Modified Files

| File | Change |
|------|--------|
| `DatasetTypes.ts` | Add `IExternalDataConfig` interface |
| `UniversalDatasetGrid.tsx` | Add `externalConfig` prop, mode 3 routing |
| `ColumnRendererService.tsx` | Add `registerRenderer()` / `unregisterRenderer()`, new built-in renderers |
| `ColumnRendererTypes.ts` | Extend `DataverseAttributeType` with `ExtendedDataType` values |

### 8.3 Extracted from EventsPage (Future)

| Component | Source | Destination |
|-----------|--------|-------------|
| `ColumnHeaderMenu` | EventsPage `GridSection.tsx` | Shared library `DatasetGrid/ColumnHeaderMenu.tsx` |
| `parseLayoutXml` | EventsPage `FetchXmlService.ts` | Shared library `services/LayoutXmlParser.ts` |

### 8.4 No Breaking Changes

All changes are **additive**:
- New optional `externalConfig` prop — existing consumers unaffected
- New renderer registration API — existing static renderers unchanged
- New data types — existing `DataverseAttributeType` enum values preserved

---

## 9. Implementation Phases

### Phase 1: Core External Data Mode (Shared Library)
- Add `IExternalDataConfig` interface to `DatasetTypes.ts`
- Create `useExternalDataMode.ts` hook
- Update `UniversalDatasetGrid.tsx` to support mode 3
- Add `registerRenderer()` API to `ColumnRendererService`
- Add `Percentage`, `StringArray`, `FileType` built-in renderers
- Unit tests for new hook and renderers

### Phase 2: Semantic Search Adoption
- Create `searchResultAdapter.ts` mapping functions
- Create `useSearchViewDefinitions.ts` hook (with `sprk_gridconfiguration` fetch)
- Create `useEnrichedResults.ts` hook for combined data
- Register search-specific renderers
- Replace `SearchResultsGrid` with `UniversalDatasetGrid` in `App.tsx`
- Keep `domainColumns.ts` as fallback
- Seed 4 default view records in `sprk_gridconfiguration`

### Phase 3: EventsPage Migration
- Replace `<table>` in `GridSection.tsx` with `<UniversalDatasetGrid headlessConfig={...}>`
- Register event-specific custom renderers (EventName, EventStatus, EventPriority)
- Extract `ColumnHeaderMenu` to shared library
- Feature-flag new grid during validation
- Remove old `GridSection.tsx` after confirmed parity

### Phase 4: Platform Standardization
- All new grids use `UniversalDatasetGrid` exclusively
- Document grid usage patterns in developer guide
- Add `ColumnHeaderMenu` integration to `GridView.tsx`
- Add `layoutXml` parser adapter for legacy Dataverse view support

---

## 10. Decision Log

| Decision | Rationale | Alternative Considered |
|----------|-----------|----------------------|
| External mode takes pre-fetched data | Grid shouldn't know about API/search internals; separation of concerns | Grid fetches from BFF API directly (rejected: tight coupling) |
| Custom renderer registration | Consumers need domain-specific cell rendering without forking shared code | Pass render functions as props (rejected: doesn't scale across view modes) |
| `sprk_gridconfiguration` for view definitions | Reuses existing table, no schema changes, admin-editable | Dedicated `sprk_searchviewdefinition` table (deferred: unnecessary complexity) |
| Fallback to hardcoded columns | Ensures grid always renders even before Dataverse seed data | Require Dataverse records (rejected: fragile, blocks development) |
| Combined data via client-side enrichment | Simple, no BFF changes needed, progressive UX | BFF API joins Dataverse + Azure (deferred: higher complexity) |
| EventsPage migrates in phases | Reduces risk, maintains feature parity during transition | Big-bang rewrite (rejected: too risky for production page) |

---

## Appendix A: Type Reference

### IExternalDataConfig (New)
```typescript
interface IExternalDataConfig {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  loading: boolean;
  error?: string | null;
  totalCount?: number;
  hasNextPage?: boolean;
  onLoadNextPage?: () => void;
  onRefresh?: () => void;
}
```

### IDatasetRecord (Unchanged)
```typescript
interface IDatasetRecord {
  id: string;
  entityName: string;
  [key: string]: any;
}
```

### IDatasetColumn (Unchanged)
```typescript
interface IDatasetColumn {
  name: string;
  displayName: string;
  dataType: string;
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
  isSecured?: boolean;
  canRead?: boolean;
  canUpdate?: boolean;
  canCreate?: boolean;
}
```

### IViewColumnDef (New — stored in sprk_gridconfiguration configjson)
```typescript
interface IViewColumnDef {
  key: string;
  label: string;
  width?: number;
  dataType?: string;
  sortable?: boolean;
  render?: string;
  isPrimary?: boolean;
}
```

### SearchViewDefinition (New — parsed from sprk_gridconfiguration)
```typescript
interface SearchViewDefinition {
  id: string;
  name: string;
  domain: SearchDomain;
  columns: IViewColumnDef[];
  defaultSort?: { column: string; direction: "asc" | "desc" };
  isDefault: boolean;
  sortOrder: number;
}
```
