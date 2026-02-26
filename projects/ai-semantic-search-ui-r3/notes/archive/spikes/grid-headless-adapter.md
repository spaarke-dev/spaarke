# Spike: Universal DatasetGrid Headless Data Adapter

> **Task**: R3-003
> **Date**: 2026-02-24
> **Author**: Claude (investigation task)
> **Status**: Complete
> **Verdict**: **Scenario B** -- Grid inner components accept data arrays directly; adapter pattern recommended

---

## Executive Summary

The SemanticSearch code page needs to display BFF API search results in a grid. The Universal DatasetGrid component was investigated to determine if it can accept custom (non-Dataverse) data.

**Finding**: The top-level `UniversalDatasetGrid` has a `headlessConfig` prop, but it is tightly coupled to Dataverse WebAPI and FetchXML -- it cannot accept arbitrary data. However, the **inner `GridView` component** (and `VirtualizedGridView`) accept plain `IDatasetRecord[]` and `IDatasetColumn[]` arrays with **zero Dataverse dependencies**. This makes a direct adapter approach fully viable.

**Recommendation**: Use `GridView` directly in the SemanticSearch code page with a thin adapter hook (`useSearchResultsAdapter`) that maps BFF search results to `IDatasetRecord[]` / `IDatasetColumn[]`. Do NOT use `UniversalDatasetGrid` at all -- it adds unnecessary Dataverse coupling (privilege checks, entity configuration, command registry).

---

## Component Architecture Analysis

### UniversalDatasetGrid.tsx (Top-Level Orchestrator)

**File**: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`

```typescript
export interface IUniversalDatasetGridProps {
  config?: IDatasetConfig;
  configJson?: string;

  // Data Source (mutually exclusive)
  dataset?: ComponentFramework.PropertyTypes.DataSet;      // PCF mode
  headlessConfig?: {                                        // Headless mode
    webAPI: ComponentFramework.WebApi;  // <-- Dataverse-coupled
    entityName: string;                 // <-- Dataverse entity required
    fetchXml?: string;                  // <-- FetchXML required
    pageSize: number;
  };

  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (recordId: string) => void;
  context: any;
}
```

**Problem for SemanticSearch**: The `headlessConfig` requires:
- `ComponentFramework.WebApi` -- a Dataverse-specific API client
- `entityName` -- a Dataverse entity logical name
- `fetchXml` -- a FetchXML query string

None of these exist in a BFF API context. The component also loads `PrivilegeService`, `EntityConfigurationService`, and `CommandRegistry` -- all Dataverse-specific services that would fail or be meaningless with search results.

### headlessConfig Verdict: NOT usable for BFF data

The `useHeadlessMode` hook (`src/client/shared/Spaarke.UI.Components/src/hooks/useHeadlessMode.ts`) internally calls `webAPI.retrieveMultipleRecords()` with FetchXML. It constructs `IDatasetRecord[]` by iterating Dataverse entity responses and extracting attributes. There is no extension point to inject custom data.

---

### GridView.tsx (Inner Component -- THE ADAPTER TARGET)

**File**: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx`

```typescript
export interface IGridViewProps {
  records: IDatasetRecord[];           // Plain array -- no Dataverse coupling
  columns: IDatasetColumn[];           // Plain column definitions
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
  enableVirtualization: boolean;
  rowHeight: number;
  scrollBehavior: ScrollBehavior;      // "Auto" | "Infinite" | "Paged"
  loading: boolean;
  hasNextPage: boolean;
  loadNextPage: () => void;
}
```

**This is pure data-in, UI-out.** No Dataverse dependencies. It:
- Renders a Fluent UI v9 `DataGrid` with sortable, resizable columns
- Supports infinite scroll (triggers `loadNextPage` at 90% scroll depth)
- Supports paged mode with "Load More" button
- Delegates to `VirtualizedGridView` for >1000 records
- Uses `ColumnRendererService` for cell formatting (which is also data-type generic)

### IDatasetRecord and IDatasetColumn Types

**File**: `src/client/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts`

```typescript
export interface IDatasetRecord {
  id: string;              // Required: unique identifier
  entityName: string;      // Required: entity type (can be "searchresult")
  [key: string]: any;      // Dynamic: any key-value pairs for column data
}

export interface IDatasetColumn {
  name: string;            // Column key (matches record property name)
  displayName: string;     // Column header label
  dataType: string;        // "string", "number", "datetime", etc.
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
  isSecured?: boolean;
  canRead?: boolean;
  canUpdate?: boolean;
  canCreate?: boolean;
}
```

The `IDatasetRecord` interface is intentionally open-ended with `[key: string]: any`. The only hard requirements are `id` and `entityName`. This makes it trivial to map BFF search results.

### IDatasetResult (Hook Return Type)

**File**: `src/client/shared/Spaarke.UI.Components/src/hooks/types.ts`

```typescript
export interface IDatasetResult {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  loading: boolean;
  error: string | null;
  totalRecordCount: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  loadNextPage: () => void;
  loadPreviousPage: () => void;
  refresh: () => void;
}
```

This is the contract that both `useDatasetMode` and `useHeadlessMode` return. A custom `useSearchResultsAdapter` hook should also return this interface for consistency.

---

### VirtualizedGridView.tsx (Large Dataset Support)

**File**: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/VirtualizedGridView.tsx`

```typescript
export interface VirtualizedGridViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  itemHeight: number;
  overscanCount: number;
  onRecordClick?: (recordId: string) => void;
}
```

Uses `react-window` `FixedSizeList` for windowed rendering. Kicks in when `GridView` detects >1000 records with virtualization enabled. Also fully Dataverse-agnostic.

---

## Infinite Scroll / Load-More Capability Assessment

**Fully supported.** The `GridView` component natively handles both scroll modes:

| Mode | Trigger | Behavior |
|------|---------|----------|
| **Infinite** (`scrollBehavior: "Infinite"`) | User scrolls past 90% of container | Calls `loadNextPage()` |
| **Paged** (`scrollBehavior: "Paged"`) | User clicks "Load More" button | Calls `loadNextPage()` |
| **Auto** (`scrollBehavior: "Auto"`) | >100 records: infinite; otherwise: paged | Automatic selection |

The adapter hook just needs to:
1. Maintain a growing `records` array (append new pages)
2. Track `hasNextPage` from the BFF API response's `hasMore` / `continuationToken` field
3. Implement `loadNextPage` to call the BFF API with the next page/offset

**Key detail**: `GridView` replaces records on re-render (it reads `props.records` directly). For infinite scroll, the adapter must **accumulate** records across pages, not replace them.

---

## ViewSelector Compatibility Assessment

**File**: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx`

The `ViewSelector` component is **NOT compatible** with BFF search results. It:
- Requires `xrm: XrmContext` -- a Dataverse API context
- Calls `ViewService` which queries `savedquery` and `sprk_gridconfiguration` Dataverse entities
- All view definitions are `IViewDefinition` with `fetchXml` and `layoutXml` fields

**Recommendation for SemanticSearch**: Do NOT use `ViewSelector`. Instead, build a custom search-specific view switcher if needed (e.g., "All Results", "Documents Only", "Emails Only") that maps to BFF API filter parameters, not FetchXML views.

**ViewService independence from FetchXML?** No. `ViewService` (`src/client/shared/Spaarke.UI.Components/src/services/ViewService.ts`) is entirely Dataverse-bound. Every method queries Dataverse entities (`savedquery`, `userquery`, `sprk_gridconfiguration`). The `IViewDefinition` type requires `fetchXml` and `layoutXml`. There is no abstraction point for non-Dataverse views.

---

## Concrete Adapter Pattern

### Recommended Hook: `useSearchResultsAdapter`

```typescript
// src/client/code-pages/SemanticSearch/src/hooks/useSearchResultsAdapter.ts

import { useState, useCallback, useRef } from "react";
import type { IDatasetRecord, IDatasetColumn } from "@spaarke/ui-components";
import type { IDatasetResult } from "@spaarke/ui-components/hooks/types";

/** BFF search result item (from /api/ai/semantic-search) */
interface ISearchResultItem {
  id: string;
  title: string;
  snippet: string;
  score: number;
  source: string;           // "email" | "document" | "note"
  entityType?: string;
  entityId?: string;
  createdOn?: string;
  modifiedOn?: string;
  metadata?: Record<string, unknown>;
}

/** BFF search response */
interface ISearchResponse {
  results: ISearchResultItem[];
  totalCount: number;
  hasMore: boolean;
  continuationToken?: string;
}

/** Column definitions for search results grid */
const SEARCH_RESULT_COLUMNS: IDatasetColumn[] = [
  { name: "title",      displayName: "Title",       dataType: "string",   isPrimary: true },
  { name: "snippet",    displayName: "Summary",     dataType: "string"    },
  { name: "score",      displayName: "Relevance",   dataType: "number"    },
  { name: "source",     displayName: "Source",       dataType: "string"    },
  { name: "modifiedOn", displayName: "Modified",     dataType: "datetime"  },
];

/** Map a single BFF result to IDatasetRecord */
function mapToDatasetRecord(item: ISearchResultItem): IDatasetRecord {
  return {
    id: item.id,
    entityName: "searchresult",   // Virtual entity name
    title: item.title,
    snippet: item.snippet,
    score: item.score,
    source: item.source,
    entityType: item.entityType ?? "",
    entityId: item.entityId ?? "",
    createdOn: item.createdOn ?? "",
    modifiedOn: item.modifiedOn ?? "",
  };
}

/**
 * Adapter hook that maps BFF semantic search results to IDatasetResult
 * for consumption by GridView.
 */
export function useSearchResultsAdapter(
  searchFn: (query: string, continuationToken?: string) => Promise<ISearchResponse>
): IDatasetResult & { search: (query: string) => void } {

  const [records, setRecords] = useState<IDatasetRecord[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasNextPage, setHasNextPage] = useState(false);
  const continuationTokenRef = useRef<string | undefined>();
  const currentQueryRef = useRef<string>("");
  const [totalRecordCount, setTotalRecordCount] = useState(0);

  /** Execute a new search (resets results) */
  const search = useCallback(async (query: string) => {
    currentQueryRef.current = query;
    continuationTokenRef.current = undefined;
    setLoading(true);
    setError(null);

    try {
      const response = await searchFn(query);
      setRecords(response.results.map(mapToDatasetRecord));
      setTotalRecordCount(response.totalCount);
      setHasNextPage(response.hasMore);
      continuationTokenRef.current = response.continuationToken;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Search failed");
      setRecords([]);
    } finally {
      setLoading(false);
    }
  }, [searchFn]);

  /** Load next page (appends to existing results) */
  const loadNextPage = useCallback(async () => {
    if (!hasNextPage || loading) return;
    setLoading(true);

    try {
      const response = await searchFn(
        currentQueryRef.current,
        continuationTokenRef.current
      );
      // APPEND, not replace -- critical for infinite scroll
      setRecords(prev => [...prev, ...response.results.map(mapToDatasetRecord)]);
      setHasNextPage(response.hasMore);
      continuationTokenRef.current = response.continuationToken;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load more results");
    } finally {
      setLoading(false);
    }
  }, [hasNextPage, loading, searchFn]);

  /** Refresh (re-execute current query) */
  const refresh = useCallback(() => {
    if (currentQueryRef.current) {
      search(currentQueryRef.current);
    }
  }, [search]);

  return {
    records,
    columns: SEARCH_RESULT_COLUMNS,
    loading,
    error,
    totalRecordCount,
    hasNextPage,
    hasPreviousPage: false,   // Search results don't page backward
    loadNextPage,
    loadPreviousPage: () => {},  // No-op
    refresh,
    search,
  };
}
```

### Recommended Usage in SemanticSearch Component

```tsx
// src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx

import * as React from "react";
import { GridView } from "@spaarke/ui-components";
import { useSearchResultsAdapter } from "../hooks/useSearchResultsAdapter";
import { searchApi } from "../services/searchApi";

export const SearchResultsGrid: React.FC<{ query: string }> = ({ query }) => {
  const adapter = useSearchResultsAdapter(searchApi.semanticSearch);
  const [selectedIds, setSelectedIds] = React.useState<string[]>([]);

  // Trigger search when query changes
  React.useEffect(() => {
    if (query) {
      adapter.search(query);
    }
  }, [query]);

  return (
    <GridView
      records={adapter.records}
      columns={adapter.columns}
      selectedRecordIds={selectedIds}
      onSelectionChange={setSelectedIds}
      onRecordClick={(record) => {
        // Open the source document/email/note
        console.log("Open record:", record.entityType, record.entityId);
      }}
      enableVirtualization={true}
      rowHeight={44}
      scrollBehavior="Infinite"
      loading={adapter.loading}
      hasNextPage={adapter.hasNextPage}
      loadNextPage={adapter.loadNextPage}
    />
  );
};
```

---

## Scenario Classification

| Scenario | Description | Verdict |
|----------|-------------|---------|
| **A) headlessConfig IS supported** | `headlessConfig` exists but requires Dataverse WebAPI + FetchXML | Not viable for BFF data |
| **B) Grid accepts data array directly** | `GridView` accepts plain `IDatasetRecord[]` + `IDatasetColumn[]` | **SELECTED -- fully viable** |
| **C) Grid requires Dataverse WebAPI** | Only `UniversalDatasetGrid` requires it; inner components do not | Partial -- top-level only |

**Final verdict: Scenario B.** The `GridView` (and `VirtualizedGridView`) components are fully decoupled from Dataverse and accept plain data arrays. The `UniversalDatasetGrid` orchestrator adds Dataverse coupling that is unnecessary and harmful for BFF search results.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| `IDatasetRecord` shape changes in shared lib | Low | Medium | Pin to known interface; add adapter tests |
| `GridView` props change (breaking) | Low | Medium | Import from barrel export; integration test |
| `ColumnRendererService` requires Dataverse metadata | Low | Low | It operates on `dataType` string, not Dataverse metadata |
| Missing sort support for BFF results | Medium | Low | `GridView` has client-side sort via Fluent `DataGrid sortable` prop |
| `VirtualizedGridView` not tested with non-Dataverse data | Low | Low | It only uses `IDatasetRecord` -- fully generic |

---

## Recommendation for Task 030 Implementer

1. **Use `GridView` directly** -- do NOT use `UniversalDatasetGrid`. Import from `@spaarke/ui-components`.

2. **Create `useSearchResultsAdapter` hook** in the SemanticSearch code page project (not in the shared library). This keeps search-specific logic isolated.

3. **Define `SEARCH_RESULT_COLUMNS`** as a constant. Map BFF response fields to `IDatasetColumn[]` entries. Use `dataType: "string"` for most fields, `dataType: "number"` for relevance score, `dataType: "datetime"` for dates.

4. **Implement infinite scroll** by accumulating records across pages in the adapter. The `GridView` natively supports `scrollBehavior: "Infinite"` with `loadNextPage` callback.

5. **Skip ViewSelector entirely** -- it is Dataverse-bound. If search result filtering is needed (by source type, date range, etc.), build custom filter UI above the grid.

6. **Skip CommandToolbar** -- the standard Dataverse commands (New, Delete, Refresh) don't apply to search results. Build a custom action bar if needed (e.g., "Open in Dataverse", "Copy Link", "Add to Matter").

7. **Set `entityName: "searchresult"`** on all `IDatasetRecord` objects. This is a virtual entity name used only for type identification within the grid.

8. **Consider custom `ColumnRendererService` overrides** for:
   - Relevance score: render as a visual bar or percentage badge
   - Source type: render as an icon (email, document, note)
   - Snippet: render with search term highlighting (bold matching terms)

---

## Files Examined

| File | Path | Key Finding |
|------|------|-------------|
| UniversalDatasetGrid.tsx | `src/client/shared/.../components/DatasetGrid/UniversalDatasetGrid.tsx` | `headlessConfig` exists but Dataverse-coupled |
| GridView.tsx | `src/client/shared/.../components/DatasetGrid/GridView.tsx` | Accepts plain `IDatasetRecord[]` -- **the adapter target** |
| VirtualizedGridView.tsx | `src/client/shared/.../components/DatasetGrid/VirtualizedGridView.tsx` | Also plain data; kicks in at >1000 records |
| useHeadlessMode.ts | `src/client/shared/.../hooks/useHeadlessMode.ts` | Uses `webAPI.retrieveMultipleRecords()` -- Dataverse-only |
| useDatasetMode.ts | `src/client/shared/.../hooks/useDatasetMode.ts` | Uses `ComponentFramework.PropertyTypes.DataSet` -- PCF-only |
| DatasetTypes.ts | `src/client/shared/.../types/DatasetTypes.ts` | `IDatasetRecord` has open `[key: string]: any` -- flexible |
| hooks/types.ts | `src/client/shared/.../hooks/types.ts` | `IDatasetResult` interface -- adapter should match this |
| ViewService.ts | `src/client/shared/.../services/ViewService.ts` | Entirely Dataverse-bound; not usable for BFF |
| ViewSelector.tsx | `src/client/shared/.../components/DatasetGrid/ViewSelector.tsx` | Requires `xrm: XrmContext`; not usable for BFF |
| Architecture doc | `docs/architecture/universal-dataset-grid-architecture.md` | Confirms two-mode design; Custom Page example uses `GridView` directly |
