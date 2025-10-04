# Task 2.3: Implement GridView with Infinite Scroll

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 2 - Core Component Development
**Estimated Time:** 6 hours (was 5h, +1h for infinite scroll)
**Prerequisites:** [TASK-2.2-DATASET-HOOKS.md](./TASK-2.2-DATASET-HOOKS.md)
**Next Task:** [TASK-2.4-CARD-LIST-VIEWS.md](./TASK-2.4-CARD-LIST-VIEWS.md)

---

## Objective

Implement a fully functional GridView component using Fluent UI v9 DataGrid with:
- Sortable, resizable columns
- Row selection (single/multiple)
- Infinite scroll with context-aware defaults
- Type-based cell rendering
- Performance optimized for 10K+ records

**Why:** GridView is the primary display mode for datasets. Infinite scroll provides seamless UX for large datasets while maintaining optional pagination for controlled navigation.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - DataGrid patterns, virtualization
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - Dataset best practices

**Key Rules:**
- ✅ Use Fluent UI v9 DataGrid (NOT v8)
- ✅ Virtualization for >100 rows
- ✅ Infinite scroll auto-load at 90% threshold
- ✅ All styling via Griffel makeStyles
- ✅ No hard-coded colors/spacing

---

## Step 1: Add ScrollBehavior to Types

**Update `src/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts`:**

Add scroll behavior enum and update config:

```typescript
/**
 * Core types for Universal Dataset component
 */

export type ViewMode = "Grid" | "Card" | "List";
export type ThemeMode = "Auto" | "Spaarke" | "Host";
export type SelectionMode = "None" | "Single" | "Multiple";
export type ScrollBehavior = "Auto" | "Infinite" | "Paged";

export interface IDatasetRecord {
  id: string;
  entityName: string;
  [key: string]: any;
}

export interface IDatasetColumn {
  name: string;
  displayName: string;
  dataType: string;
  isKey?: boolean;
  isPrimary?: boolean;
  visualSizeFactor?: number;
}

export interface IDatasetConfig {
  viewMode: ViewMode;
  enableVirtualization: boolean;
  rowHeight: number;
  selectionMode: SelectionMode;
  showToolbar: boolean;
  enabledCommands: string[];
  theme: ThemeMode;
  scrollBehavior: ScrollBehavior;
}

export interface ICommandContext {
  selectedRecords: IDatasetRecord[];
  entityName: string;
  webAPI: ComponentFramework.WebApi;
  navigation: ComponentFramework.Navigation;
  refresh?: () => void;
  parentRecord?: ComponentFramework.EntityReference;
  emitLastAction?: (action: string) => void;
}
```

---

## Step 2: Implement Full GridView Component

**Replace `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx`:**

```typescript
/**
 * GridView - Table layout using Fluent UI DataGrid
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  DataGrid,
  DataGridHeader,
  DataGridRow,
  DataGridHeaderCell,
  DataGridBody,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  makeStyles,
  tokens,
  Button,
  Spinner
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from "../../types";

export interface IGridViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
  enableVirtualization: boolean;
  rowHeight: number;
  scrollBehavior: ScrollBehavior;
  loading: boolean;
  hasNextPage: boolean;
  loadNextPage: () => void;
}

const useStyles = makeStyles({
  root: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column",
    position: "relative"
  },
  gridContainer: {
    flex: 1,
    overflow: "auto",
    position: "relative"
  },
  loadingOverlay: {
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: "1px",
    borderTopStyle: "solid",
    borderTopColor: tokens.colorNeutralStroke1
  },
  loadMoreButton: {
    margin: tokens.spacingVerticalM,
    width: "100%"
  },
  emptyState: {
    padding: tokens.spacingVerticalXXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground3
  }
});

export const GridView: React.FC<IGridViewProps> = (props) => {
  const styles = useStyles();
  const gridContainerRef = React.useRef<HTMLDivElement>(null);

  // Determine if infinite scroll should be active
  const isInfiniteScroll = React.useMemo(() => {
    if (props.scrollBehavior === "Infinite") return true;
    if (props.scrollBehavior === "Paged") return false;
    // Auto mode: infinite for >100 records, paged otherwise
    return props.records.length > 100;
  }, [props.scrollBehavior, props.records.length]);

  // Handle scroll for infinite scroll
  const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
    if (!isInfiniteScroll || !props.hasNextPage || props.loading) {
      return;
    }

    const container = e.currentTarget;
    const { scrollTop, scrollHeight, clientHeight } = container;

    // Calculate scroll percentage
    const scrollPercentage = (scrollTop + clientHeight) / scrollHeight;

    // Load more when 90% scrolled
    if (scrollPercentage > 0.9) {
      props.loadNextPage();
    }
  }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);

  // Convert IDatasetColumn to Fluent DataGrid columns
  const gridColumns = React.useMemo((): TableColumnDefinition<IDatasetRecord>[] => {
    return props.columns.map((col) =>
      createTableColumn<IDatasetRecord>({
        columnId: col.name,
        compare: (a, b) => {
          const aVal = String(a[col.name] ?? "");
          const bVal = String(b[col.name] ?? "");
          return aVal.localeCompare(bVal);
        },
        renderHeaderCell: () => col.displayName,
        renderCell: (item) => {
          const value = item[col.name];
          return <span>{value != null ? String(value) : ""}</span>;
        }
      })
    );
  }, [props.columns]);

  // Handle row selection
  const handleSelectionChange = React.useCallback((e: any, data: any) => {
    const selectedItems = data.selectedItems as Set<string>;
    props.onSelectionChange(Array.from(selectedItems));
  }, [props.onSelectionChange]);

  // Handle row click
  const handleRowClick = React.useCallback((record: IDatasetRecord) => {
    props.onRecordClick(record);
  }, [props.onRecordClick]);

  // Empty state
  if (props.records.length === 0 && !props.loading) {
    return (
      <div className={styles.emptyState}>
        <p>No records to display</p>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <div
        className={styles.gridContainer}
        ref={gridContainerRef}
        onScroll={handleScroll}
      >
        <DataGrid
          items={props.records}
          columns={gridColumns}
          sortable
          resizableColumns
          selectionMode={props.selectedRecordIds.length > 0 ? "multiselect" : undefined}
          selectedItems={new Set(props.selectedRecordIds)}
          onSelectionChange={handleSelectionChange}
          focusMode="composite"
        >
          <DataGridHeader>
            <DataGridRow>
              {({ renderHeaderCell }) => (
                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
              )}
            </DataGridRow>
          </DataGridHeader>
          <DataGridBody<IDatasetRecord>>
            {({ item, rowId }) => (
              <DataGridRow<IDatasetRecord>
                key={rowId}
                onClick={() => handleRowClick(item)}
                style={{ cursor: "pointer" }}
              >
                {({ renderCell }) => (
                  <DataGridCell>{renderCell(item)}</DataGridCell>
                )}
              </DataGridRow>
            )}
          </DataGridBody>
        </DataGrid>
      </div>

      {/* Loading indicator for infinite scroll */}
      {isInfiniteScroll && props.loading && (
        <div className={styles.loadingOverlay}>
          <Spinner size="small" label="Loading more records..." />
        </div>
      )}

      {/* Load More button for paged mode */}
      {!isInfiniteScroll && props.hasNextPage && !props.loading && (
        <Button
          appearance="subtle"
          className={styles.loadMoreButton}
          onClick={props.loadNextPage}
        >
          Load More ({props.records.length} records loaded)
        </Button>
      )}

      {/* Loading indicator for paged mode */}
      {!isInfiniteScroll && props.loading && (
        <div className={styles.loadingOverlay}>
          <Spinner size="small" label="Loading..." />
        </div>
      )}
    </div>
  );
};
```

---

## Step 3: Update UniversalDatasetGrid to Pass Scroll Props

**Update `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx`:**

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
    overflow: "hidden"
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
  const result = isHeadlessMode ? headlessResult : datasetResult;
  const { records, columns, loading, error, hasNextPage, loadNextPage } = result;

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
  }, [props]);

  return (
    <FluentProvider theme={theme}>
      <div className={styles.root}>
        {/* Toolbar will be added in Phase 3 */}

        <div className={styles.content}>
          {error ? (
            <div className={styles.error}>Error: {error}</div>
          ) : (
            <ViewComponent
              records={records}
              columns={columns}
              selectedRecordIds={props.selectedRecordIds}
              onSelectionChange={props.onSelectionChange}
              onRecordClick={handleRecordClick}
              enableVirtualization={props.config.enableVirtualization}
              rowHeight={props.config.rowHeight}
              scrollBehavior={props.config.scrollBehavior}
              loading={loading}
              hasNextPage={hasNextPage}
              loadNextPage={loadNextPage}
            />
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
```

---

## Step 4: Update PCF Control to Include ScrollBehavior

**Update `power-platform/pcf/UniversalDataset/index.ts`:**

Add scrollBehavior to config (around line 85):

```typescript
// Build configuration from manifest properties
const config: IDatasetConfig = {
  viewMode: context.parameters.viewMode?.raw || "Grid",
  enableVirtualization: context.parameters.enableVirtualization?.raw ?? true,
  rowHeight: context.parameters.rowHeight?.raw || 48,
  selectionMode: context.parameters.selectionMode?.raw || "Multiple",
  showToolbar: context.parameters.showToolbar?.raw ?? true,
  enabledCommands: (context.parameters.enabledCommands?.raw || "open,create,delete,refresh").split(","),
  theme: context.parameters.theme?.raw || "Auto",
  scrollBehavior: "Auto" // Will add manifest property in TASK-1.4
};
```

---

## Step 5: Build and Test

```bash
# Build shared library
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
npm run build

# Expected output: Successfully compiled
```

---

## Validation Checklist

```bash
# 1. Verify GridView updated
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
cat src/components/DatasetGrid/GridView.tsx | grep "handleScroll"
# Should show scroll handler

# 2. Verify ScrollBehavior type added
cat src/types/DatasetTypes.ts | grep "ScrollBehavior"
# Should show type definition

# 3. Verify build succeeds
npm run build
# Should succeed with 0 errors

# 4. Verify DataGrid imports
cat dist/components/DatasetGrid/GridView.d.ts | grep "IGridViewProps"
# Should show scrollBehavior property
```

---

## Success Criteria

- ✅ GridView uses Fluent UI v9 DataGrid component
- ✅ Columns are sortable and resizable
- ✅ Row selection works (single/multiple)
- ✅ Infinite scroll auto-loads at 90% threshold
- ✅ Paged mode shows "Load More" button
- ✅ Auto mode intelligently switches (<100 = paged, >100 = infinite)
- ✅ Loading indicators display correctly
- ✅ Empty state shows when no records
- ✅ All styling uses Griffel and tokens
- ✅ No hard-coded colors or spacing

---

## Deliverables

**Files Updated:**
1. `src/types/DatasetTypes.ts` - Added ScrollBehavior type
2. `src/components/DatasetGrid/GridView.tsx` - Full implementation with infinite scroll
3. `src/components/DatasetGrid/UniversalDatasetGrid.tsx` - Pass scroll props to views
4. `power-platform/pcf/UniversalDataset/index.ts` - Include scrollBehavior in config

**Build Output:**
- Updated `dist/` with new GridView implementation

---

## Scroll Behavior Logic

### Auto Mode (Default):
```
Records ≤ 100: Paged (Load More button)
Records > 100: Infinite (Auto-load at 90%)
```

### Infinite Mode:
- Always auto-load when scrolled to 90%
- Spinner shown at bottom while loading
- Seamless experience

### Paged Mode:
- Always show "Load More" button
- Manual control over data loading
- Predictable navigation

---

## Performance Considerations

**Infinite Scroll:**
- ⚡ Scroll listener debounced via useCallback
- 🎯 Loads only when necessary (90% threshold)
- 💾 Previous records stay in memory (no unload)
- 🔄 Loading state prevents duplicate requests

**DataGrid Virtualization:**
- Fluent UI DataGrid has built-in virtualization
- Only renders visible rows in DOM
- Handles 10K+ records efficiently

---

## Common Issues & Solutions

**Issue:** Infinite scroll triggers multiple times
**Solution:** Loading state check prevents duplicate calls

**Issue:** Scroll position resets after load
**Solution:** React maintains scroll position automatically

**Issue:** "Load More" button not showing
**Solution:** Check hasNextPage is true and scrollBehavior is "Paged"

**Issue:** DataGrid not sortable
**Solution:** Ensure `sortable` prop is true on DataGrid

---

## Next Steps

After completing this task:
1. Proceed to [TASK-2.4-CARD-LIST-VIEWS.md](./TASK-2.4-CARD-LIST-VIEWS.md)
2. Will implement CardView and ListView with same infinite scroll logic

---

**Task Status:** Ready for Execution
**Estimated Time:** 6 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
