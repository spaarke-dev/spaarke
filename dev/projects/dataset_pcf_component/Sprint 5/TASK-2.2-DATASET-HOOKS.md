# Task 2.2: Implement Dataset and Headless Mode Hooks

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 2 - Core Component Development
**Estimated Time:** 4 hours
**Prerequisites:** [TASK-2.1-CORE-COMPONENT-STRUCTURE.md](./TASK-2.1-CORE-COMPONENT-STRUCTURE.md)
**Next Task:** [TASK-2.3-GRID-VIEW-IMPLEMENTATION.md](./TASK-2.3-GRID-VIEW-IMPLEMENTATION.md)

---

## Objective

Create custom React hooks to handle two data modes:
1. **Dataset Mode** - Data provided by Power Platform dataset binding (model-driven apps)
2. **Headless Mode** - Component fetches data via Web API using FetchXML (custom pages)

**Why:** The Universal Dataset component must work in both model-driven apps (dataset binding) and custom pages (headless). Hooks encapsulate this logic and provide a consistent data interface.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - Dataset API, Web API patterns
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - React hooks patterns

**Key Rules:**
- ✅ Use React hooks (useState, useEffect, useMemo)
- ✅ Memoize expensive operations
- ✅ Handle loading/error states
- ✅ Support pagination for headless mode
- ✅ All code in shared library

---

## Step 1: Create Hook Types

**Create `src/shared/Spaarke.UI.Components/src/hooks/types.ts`:**

```typescript
/**
 * Hook return types for dataset and headless modes
 */

import { IDatasetRecord, IDatasetColumn } from "../types";

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

---

## Step 2: Create useDatasetMode Hook

**Create `src/shared/Spaarke.UI.Components/src/hooks/useDatasetMode.ts`:**

```typescript
/**
 * useDatasetMode - Extract data from PCF dataset binding
 * Used in model-driven apps where Power Platform provides the dataset
 */

import { useState, useEffect, useMemo } from "react";
import { IDatasetRecord, IDatasetColumn } from "../types";
import { IDatasetResult } from "./types";

export interface IUseDatasetModeProps {
  dataset: ComponentFramework.PropertyTypes.DataSet;
}

export function useDatasetMode(props: IUseDatasetModeProps): IDatasetResult {
  const { dataset } = props;
  const [error, setError] = useState<string | null>(null);

  // Extract columns from dataset
  const columns = useMemo((): IDatasetColumn[] => {
    if (!dataset.columns || dataset.columns.length === 0) {
      return [];
    }

    return dataset.columns.map((col) => ({
      name: col.name,
      displayName: col.displayName,
      dataType: col.dataType,
      isKey: false,
      isPrimary: col.name === dataset.columns[0]?.name,
      visualSizeFactor: col.visualSizeFactor
    }));
  }, [dataset.columns]);

  // Extract records from dataset
  const records = useMemo((): IDatasetRecord[] => {
    if (!dataset.sortedRecordIds || dataset.sortedRecordIds.length === 0) {
      return [];
    }

    const extractedRecords: IDatasetRecord[] = [];

    dataset.sortedRecordIds.forEach((recordId) => {
      const record = dataset.records[recordId];
      if (!record) return;

      const dataRecord: IDatasetRecord = {
        id: recordId,
        entityName: dataset.getTargetEntityType()
      };

      // Extract all column values
      columns.forEach((col) => {
        const formattedValue = record.getFormattedValue(col.name);
        const rawValue = record.getValue(col.name);

        dataRecord[col.name] = formattedValue || rawValue;
        dataRecord[`${col.name}_raw`] = rawValue;
      });

      extractedRecords.push(dataRecord);
    });

    return extractedRecords;
  }, [dataset.sortedRecordIds, dataset.records, columns]);

  // Pagination state
  const paging = dataset.paging;
  const hasNextPage = paging?.hasNextPage ?? false;
  const hasPreviousPage = paging?.hasPreviousPage ?? false;

  // Pagination functions
  const loadNextPage = () => {
    if (hasNextPage && paging) {
      paging.loadNextPage();
    }
  };

  const loadPreviousPage = () => {
    if (hasPreviousPage && paging) {
      paging.loadPreviousPage();
    }
  };

  const refresh = () => {
    try {
      dataset.refresh();
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to refresh dataset");
    }
  };

  // Monitor for errors
  useEffect(() => {
    if (dataset.error) {
      setError(dataset.error.message);
    } else {
      setError(null);
    }
  }, [dataset.error]);

  return {
    records,
    columns,
    loading: dataset.loading,
    error,
    totalRecordCount: paging?.totalResultCount ?? records.length,
    hasNextPage,
    hasPreviousPage,
    loadNextPage,
    loadPreviousPage,
    refresh
  };
}
```

---

## Step 3: Create useHeadlessMode Hook

**Create `src/shared/Spaarke.UI.Components/src/hooks/useHeadlessMode.ts`:**

```typescript
/**
 * useHeadlessMode - Fetch data via Web API using FetchXML
 * Used in custom pages where no dataset binding exists
 */

import { useState, useEffect, useCallback, useMemo } from "react";
import { IDatasetRecord, IDatasetColumn } from "../types";
import { IDatasetResult } from "./types";

export interface IUseHeadlessModeProps {
  webAPI: ComponentFramework.WebApi;
  entityName: string;
  fetchXml?: string;
  pageSize: number;
  autoLoad?: boolean;
}

interface IPagingInfo {
  pageNumber: number;
  pagingCookie?: string;
}

export function useHeadlessMode(props: IUseHeadlessModeProps): IDatasetResult {
  const { webAPI, entityName, fetchXml, pageSize, autoLoad = true } = props;

  const [records, setRecords] = useState<IDatasetRecord[]>([]);
  const [columns, setColumns] = useState<IDatasetColumn[]>([]);
  const [loading, setLoading] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);
  const [pagingInfo, setPagingInfo] = useState<IPagingInfo>({ pageNumber: 1 });
  const [totalRecordCount, setTotalRecordCount] = useState<number>(0);
  const [hasMore, setHasMore] = useState<boolean>(false);

  // Build FetchXML query with paging
  const buildFetchXml = useCallback((page: number, cookie?: string): string => {
    if (fetchXml) {
      // User-provided FetchXML - inject paging attributes
      const fetchDoc = new DOMParser().parseFromString(fetchXml, "text/xml");
      const fetchNode = fetchDoc.querySelector("fetch");

      if (fetchNode) {
        fetchNode.setAttribute("page", page.toString());
        fetchNode.setAttribute("count", pageSize.toString());
        if (cookie) {
          fetchNode.setAttribute("paging-cookie", cookie);
        }
      }

      return new XMLSerializer().serializeToString(fetchDoc);
    } else {
      // Default FetchXML - retrieve all columns
      return `
        <fetch page="${page}" count="${pageSize}" ${cookie ? `paging-cookie="${cookie}"` : ""}>
          <entity name="${entityName}">
            <all-attributes />
          </entity>
        </fetch>
      `.trim();
    }
  }, [fetchXml, entityName, pageSize]);

  // Fetch data from Web API
  const fetchData = useCallback(async (page: number, cookie?: string) => {
    setLoading(true);
    setError(null);

    try {
      const query = buildFetchXml(page, cookie);

      // Execute FetchXML query
      const response = await webAPI.retrieveMultipleRecords(
        entityName,
        `?fetchXml=${encodeURIComponent(query)}`
      );

      // Extract records
      const fetchedRecords: IDatasetRecord[] = response.entities.map((entity: any) => {
        const record: IDatasetRecord = {
          id: entity[`${entityName}id`] || entity.id,
          entityName
        };

        // Copy all entity attributes
        Object.keys(entity).forEach((key) => {
          if (key !== `${entityName}id` && !key.startsWith("@")) {
            record[key] = entity[key];
          }
        });

        return record;
      });

      setRecords(fetchedRecords);

      // Extract columns from first record (if not already set)
      if (columns.length === 0 && fetchedRecords.length > 0) {
        const firstRecord = fetchedRecords[0];
        const extractedColumns: IDatasetColumn[] = Object.keys(firstRecord)
          .filter((key) => key !== "id" && key !== "entityName")
          .map((key) => ({
            name: key,
            displayName: key.replace(/_/g, " ").replace(/\b\w/g, (l) => l.toUpperCase()),
            dataType: typeof firstRecord[key] === "number" ? "number" : "string",
            isKey: key === `${entityName}id`,
            isPrimary: false
          }));

        setColumns(extractedColumns);
      }

      // Pagination info
      setHasMore(response.entities.length === pageSize);
      setTotalRecordCount(response.entities.length); // Note: FetchXML doesn't return total count

      // Extract paging cookie from response
      const nextCookie = (response as any)["@Microsoft.Dynamics.CRM.fetchxmlpagingcookie"];
      setPagingInfo({ pageNumber: page, pagingCookie: nextCookie });

      setLoading(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to fetch data");
      setRecords([]);
      setLoading(false);
    }
  }, [buildFetchXml, entityName, webAPI, columns.length]);

  // Load next page
  const loadNextPage = useCallback(() => {
    if (hasMore && !loading) {
      fetchData(pagingInfo.pageNumber + 1, pagingInfo.pagingCookie);
    }
  }, [hasMore, loading, pagingInfo, fetchData]);

  // Load previous page
  const loadPreviousPage = useCallback(() => {
    if (pagingInfo.pageNumber > 1 && !loading) {
      fetchData(pagingInfo.pageNumber - 1);
    }
  }, [pagingInfo.pageNumber, loading, fetchData]);

  // Refresh data
  const refresh = useCallback(() => {
    fetchData(1); // Reset to page 1
  }, [fetchData]);

  // Auto-load on mount
  useEffect(() => {
    if (autoLoad) {
      fetchData(1);
    }
  }, [autoLoad, fetchData]);

  return {
    records,
    columns,
    loading,
    error,
    totalRecordCount,
    hasNextPage: hasMore,
    hasPreviousPage: pagingInfo.pageNumber > 1,
    loadNextPage,
    loadPreviousPage,
    refresh
  };
}
```

---

## Step 4: Export Hooks

**Create `src/shared/Spaarke.UI.Components/src/hooks/index.ts`:**

```typescript
export * from "./types";
export * from "./useDatasetMode";
export * from "./useHeadlessMode";
```

**Update `src/shared/Spaarke.UI.Components/src/index.ts`:**

```typescript
/**
 * Spaarke UI Components - Shared component library
 * Standards: ADR-012, KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

// Theme
export * from "./theme";

// Types
export * from "./types";

// Utils
export * from "./utils";

// Hooks
export * from "./hooks";

// Components
export * from "./components";
```

---

## Step 5: Update UniversalDatasetGrid to Use Hooks

**Edit `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`:**

Replace the component with:

```typescript
/**
 * UniversalDatasetGrid - Main component for dataset display
 * Routes to GridView, CardView, or ListView based on configuration
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md, ADR-012
 */

import * as React from "react";
import { FluentProvider, makeStyles, tokens } from "@fluentui/react-components";
import { detectTheme } from "../../utils/themeDetection";
import { IDatasetConfig } from "../../types";
import { useDatasetMode } from "../../hooks/useDatasetMode";
import { useHeadlessMode } from "../../hooks/useHeadlessMode";
import { GridView } from "./GridView";
import { CardView } from "./CardView";
import { ListView } from "./ListView";

export interface IUniversalDatasetGridProps {
  // Configuration
  config: IDatasetConfig;

  // Data Source (mutually exclusive)
  dataset?: ComponentFramework.PropertyTypes.DataSet;
  headlessConfig?: {
    webAPI: ComponentFramework.WebApi;
    entityName: string;
    fetchXml?: string;
    pageSize: number;
  };

  // Selection
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;

  // Actions
  onRecordClick: (recordId: string) => void;

  // Context (for theme detection)
  context: any; // ComponentFramework.Context<IInputs>
}

const useStyles = makeStyles({
  root: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    fontFamily: tokens.fontFamilyBase
  },
  content: {
    flex: 1,
    overflow: "auto"
  },
  loading: {
    padding: tokens.spacingVerticalXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground2
  },
  error: {
    padding: tokens.spacingVerticalXL,
    textAlign: "center",
    color: tokens.colorPaletteRedForeground1
  }
});

export const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps> = (props) => {
  const styles = useStyles();

  // Determine data mode
  const isHeadlessMode = !!props.headlessConfig;

  // Use appropriate hook based on mode
  const datasetResult = useDatasetMode({
    dataset: props.dataset || ({} as any)
  });

  const headlessResult = useHeadlessMode({
    webAPI: props.headlessConfig?.webAPI || ({} as any),
    entityName: props.headlessConfig?.entityName || "",
    fetchXml: props.headlessConfig?.fetchXml,
    pageSize: props.headlessConfig?.pageSize || 25,
    autoLoad: isHeadlessMode
  });

  // Select result based on mode
  const { records, columns, loading, error } = isHeadlessMode ? headlessResult : datasetResult;

  // Detect theme from context
  const theme = React.useMemo(
    () => detectTheme(props.context, props.config.theme),
    [props.context, props.config.theme]
  );

  // Select view component based on config
  const ViewComponent = React.useMemo(() => {
    switch (props.config.viewMode) {
      case "Card":
        return CardView;
      case "List":
        return ListView;
      case "Grid":
      default:
        return GridView;
    }
  }, [props.config.viewMode]);

  // Handle record click
  const handleRecordClick = React.useCallback((record: any) => {
    props.onRecordClick(record.id);
  }, [props.onRecordClick]);

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Toolbar will be added in Phase 3 */}

        <div className={styles.content}>
          {error ? (
            <div className={styles.error}>Error: {error}</div>
          ) : loading ? (
            <div className={styles.loading}>Loading...</div>
          ) : (
            <ViewComponent
              records={records}
              columns={columns}
              selectedRecordIds={props.selectedRecordIds}
              onSelectionChange={props.onSelectionChange}
              onRecordClick={handleRecordClick}
              enableVirtualization={props.config.enableVirtualization}
              rowHeight={props.config.rowHeight}
            />
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
```

---

## Step 6: Build Shared Library

```bash
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
npm run build
```

**Expected output:** Successfully compiled

---

## Step 7: Update PCF Control to Support Both Modes

**Edit `power-platform/pcf/UniversalDataset/index.ts`:**

```typescript
public updateView(context: ComponentFramework.Context<IInputs>): void {
  this.context = context;

  // Build configuration from manifest properties
  const config: IDatasetConfig = {
    viewMode: context.parameters.viewMode?.raw || "Grid",
    enableVirtualization: context.parameters.enableVirtualization?.raw ?? true,
    rowHeight: context.parameters.rowHeight?.raw || 48,
    selectionMode: context.parameters.selectionMode?.raw || "Multiple",
    showToolbar: context.parameters.showToolbar?.raw ?? true,
    enabledCommands: (context.parameters.enabledCommands?.raw || "open,create,delete,refresh").split(","),
    theme: context.parameters.theme?.raw || "Auto"
  };

  // Determine mode
  const isHeadlessMode = context.parameters.headlessMode?.raw ?? false;

  // Render React component with appropriate props
  const element = React.createElement(UniversalDatasetGrid, {
    config,
    dataset: isHeadlessMode ? undefined : context.parameters.datasetGrid,
    headlessConfig: isHeadlessMode ? {
      webAPI: context.webAPI,
      entityName: context.parameters.headlessEntityName?.raw || "",
      fetchXml: context.parameters.headlessFetchXml?.raw,
      pageSize: context.parameters.headlessPageSize?.raw || 25
    } : undefined,
    selectedRecordIds: this.selectedRecords,
    onSelectionChange: (ids) => {
      this.selectedRecords = ids;
      this.notifyOutputChanged();
    },
    onRecordClick: (recordId) => {
      if (!isHeadlessMode) {
        context.parameters.datasetGrid.openDatasetItem(recordId);
      }
    },
    context: context
  });

  ReactDOM.render(element, this.container);
}
```

---

## Validation Checklist

```bash
# 1. Verify hooks created
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
ls src/hooks/
# Should show: index.ts, types.ts, useDatasetMode.ts, useHeadlessMode.ts

# 2. Verify shared library builds
npm run build
# Should succeed with 0 errors

# 3. Verify exports
cat dist/hooks/index.d.ts
# Should export useDatasetMode, useHeadlessMode, IDatasetResult

# 4. Verify UniversalDatasetGrid updated
cat src/components/DatasetGrid/UniversalDatasetGrid.tsx | grep "useDatasetMode"
# Should show import and usage
```

---

## Success Criteria

- ✅ useDatasetMode hook extracts records/columns from PCF dataset
- ✅ useHeadlessMode hook fetches data via Web API with FetchXML
- ✅ Both hooks return IDatasetResult interface
- ✅ Pagination supported (loadNextPage, loadPreviousPage)
- ✅ Error handling implemented
- ✅ Loading states managed
- ✅ UniversalDatasetGrid switches modes dynamically
- ✅ Shared library builds successfully

---

## Deliverables

1. `src/hooks/types.ts` - IDatasetResult interface
2. `src/hooks/useDatasetMode.ts` - Dataset binding hook
3. `src/hooks/useHeadlessMode.ts` - Web API fetching hook
4. `src/hooks/index.ts` - Hook exports
5. Updated `src/index.ts` - Export hooks
6. Updated `UniversalDatasetGrid.tsx` - Use hooks for data
7. Updated PCF `index.ts` - Support both modes

---

## Common Issues & Solutions

**Issue:** "Cannot read property 'sortedRecordIds' of undefined"
**Solution:** Check dataset is passed and initialized. Add null checks in hook.

**Issue:** FetchXML query returns no results
**Solution:** Verify entity name is correct logical name (e.g., "sprk_document" not "Document")

**Issue:** Paging cookie errors
**Solution:** Ensure paging-cookie XML attribute is properly encoded

**Issue:** Hook causes infinite re-renders
**Solution:** Wrap callbacks in useCallback, deps in useMemo

---

## Next Steps

After completing this task:
1. Proceed to [TASK-2.3-GRID-VIEW-IMPLEMENTATION.md](./TASK-2.3-GRID-VIEW-IMPLEMENTATION.md)
2. Will implement full GridView with Fluent UI DataGrid

---

**Task Status:** Ready for Execution
**Estimated Time:** 4 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
