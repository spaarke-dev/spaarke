# TASK-3.3: Virtual Scrolling with react-window

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 3 - Advanced Features
**Estimated Time:** 4 hours
**Prerequisites:** TASK-2.3 (Grid View), TASK-3.2 (Column Renderers)
**Next Task:** TASK-3.4 (Toolbar UI)

---

## Objective

Integrate `react-window` for virtual scrolling to efficiently render large datasets (1,000+ records) in GridView and ListView. This ensures performance remains <500ms initial render and supports 10,000+ records with <100 DOM elements.

**Why This Matters:**
- **Performance:** Dataverse datasets can contain thousands of records
- **Memory Efficiency:** Only render visible rows (virtualization)
- **User Experience:** Smooth scrolling with large datasets
- **ADR Alignment:** ADR-012 shared component library supports performance-first design

---

## Critical Standards

**Must Read:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Performance requirements (virtualization >100 items)
- [DATASET-COMPONENT-PERFORMANCE.md](./DATASET-COMPONENT-PERFORMANCE.md) - Virtual scrolling patterns
- [ADR-012-SHARED-COMPONENT-LIBRARY.md](../../../docs/ADR-012-SHARED-COMPONENT-LIBRARY.md) - Shared library architecture

**Key Rules:**
1. ✅ Use `react-window` (NOT `@tanstack/react-virtual`) per SPRINT-5-IMPLEMENTATION-PLAN.md
2. ✅ Fixed row heights for optimal performance
3. ✅ Virtualization threshold: >100 records
4. ✅ Target: <100 DOM elements for large datasets
5. ✅ Fluent UI v9 only (NO v8 components)
6. ✅ All work in `src/shared/Spaarke.UI.Components/`

---

## Implementation Steps

### Step 1: Install react-window Dependencies

**Location:** `src/shared/Spaarke.UI.Components/`

```bash
cd src/shared/Spaarke.UI.Components
npm install react-window@^1.8.10
npm install --save-dev @types/react-window@^1.8.8
```

**Validation:**
```bash
cat package.json | grep -A 2 "dependencies"
cat package.json | grep -A 2 "devDependencies"
```

**Expected:**
- `react-window` in `dependencies`
- `@types/react-window` in `devDependencies`

---

### Step 2: Create Virtualization Hook

**File:** `src/shared/Spaarke.UI.Components/src/hooks/useVirtualization.ts`

```typescript
import { useMemo } from "react";

export interface VirtualizationConfig {
  enabled: boolean;
  threshold: number; // Number of records to trigger virtualization
  itemHeight: number; // Fixed height per row
  overscanCount?: number; // Extra rows to render outside viewport
}

export interface VirtualizationResult {
  shouldVirtualize: boolean;
  itemHeight: number;
  overscanCount: number;
}

const DEFAULT_CONFIG: VirtualizationConfig = {
  enabled: true,
  threshold: 100,
  itemHeight: 44, // Matches Fluent UI DataGrid row height
  overscanCount: 5
};

/**
 * Hook to determine if virtualization should be enabled based on record count
 */
export function useVirtualization(
  recordCount: number,
  config?: Partial<VirtualizationConfig>
): VirtualizationResult {
  const finalConfig = useMemo(() => ({
    ...DEFAULT_CONFIG,
    ...config
  }), [config]);

  return useMemo(() => ({
    shouldVirtualize: finalConfig.enabled && recordCount > finalConfig.threshold,
    itemHeight: finalConfig.itemHeight,
    overscanCount: finalConfig.overscanCount ?? DEFAULT_CONFIG.overscanCount!
  }), [recordCount, finalConfig]);
}
```

**Why:**
- Centralized logic for when to enable virtualization
- Configurable threshold (default: 100 records)
- Fixed item height for optimal react-window performance
- Overscan prevents blank rows during fast scrolling

---

### Step 3: Create VirtualizedListView Component

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/VirtualizedListView.tsx`

```typescript
import * as React from "react";
import { FixedSizeList as List } from "react-window";
import {
  Text,
  makeStyles,
  tokens,
  shorthands
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types/DatasetTypes";
import { ColumnRendererService } from "../../services/ColumnRendererService";

const useStyles = makeStyles({
  listContainer: {
    width: "100%",
    height: "100%"
  },
  row: {
    display: "flex",
    alignItems: "center",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    ...shorthands.padding("8px", "16px"),
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover
    }
  },
  rowSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected
    }
  },
  primaryColumn: {
    flex: 1,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorBrandForeground1
  },
  secondaryColumn: {
    flex: 1,
    color: tokens.colorNeutralForeground2
  }
});

export interface VirtualizedListViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  itemHeight: number;
  overscanCount: number;
  onRecordClick?: (recordId: string) => void;
}

export const VirtualizedListView: React.FC<VirtualizedListViewProps> = (props) => {
  const styles = useStyles();
  const listRef = React.useRef<List>(null);

  // Get primary and first secondary column
  const primaryColumn = props.columns.find(c => c.isPrimary) ?? props.columns[0];
  const secondaryColumn = props.columns.find(c => !c.isPrimary && c.name !== primaryColumn?.name);

  const renderRow = React.useCallback(({ index, style }: { index: number; style: React.CSSProperties }) => {
    const record = props.records[index];
    const isSelected = props.selectedRecordIds.includes(record.id);

    const primaryRenderer = ColumnRendererService.getRenderer(primaryColumn);
    const primaryValue = primaryRenderer(record[primaryColumn.name], record, primaryColumn);

    let secondaryValue: React.ReactElement | string | null = null;
    if (secondaryColumn) {
      const secondaryRenderer = ColumnRendererService.getRenderer(secondaryColumn);
      secondaryValue = secondaryRenderer(record[secondaryColumn.name], record, secondaryColumn);
    }

    return (
      <div
        style={style}
        className={`${styles.row} ${isSelected ? styles.rowSelected : ""}`}
        onClick={() => props.onRecordClick?.(record.id)}
      >
        <div className={styles.primaryColumn}>{primaryValue}</div>
        {secondaryValue && (
          <div className={styles.secondaryColumn}>{secondaryValue}</div>
        )}
      </div>
    );
  }, [props.records, props.selectedRecordIds, props.onRecordClick, primaryColumn, secondaryColumn, styles]);

  return (
    <div className={styles.listContainer}>
      <List
        ref={listRef}
        height={600} // Will be calculated from parent container
        itemCount={props.records.length}
        itemSize={props.itemHeight}
        width="100%"
        overscanCount={props.overscanCount}
      >
        {renderRow}
      </List>
    </div>
  );
};
```

**Why:**
- Uses `FixedSizeList` from react-window for optimal performance
- Renders only visible rows + overscan
- Integrates with existing `ColumnRendererService`
- Respects field security (renderers handle secured fields)
- Fluent UI v9 styling with makeStyles

---

### Step 4: Update ListView to Support Virtualization

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx`

**Add imports:**
```typescript
import { useVirtualization } from "../../hooks/useVirtualization";
import { VirtualizedListView } from "./VirtualizedListView";
```

**Update interface:**
```typescript
export interface ListViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  enableVirtualization?: boolean; // NEW
  onRecordClick?: (recordId: string) => void;
}
```

**Update component:**
```typescript
export const ListView: React.FC<ListViewProps> = (props) => {
  const styles = useStyles();

  // Check if virtualization should be enabled
  const virtualization = useVirtualization(props.records.length, {
    enabled: props.enableVirtualization ?? true
  });

  // Filter to only readable columns
  const displayColumns = props.columns.filter(
    (col) => col.canRead !== false
  );

  // Use virtualized view for large datasets
  if (virtualization.shouldVirtualize) {
    return (
      <VirtualizedListView
        records={props.records}
        columns={displayColumns}
        selectedRecordIds={props.selectedRecordIds}
        itemHeight={virtualization.itemHeight}
        overscanCount={virtualization.overscanCount}
        onRecordClick={props.onRecordClick}
      />
    );
  }

  // Original non-virtualized implementation for small datasets
  const primaryColumn = displayColumns.find(c => c.isPrimary) ?? displayColumns[0];
  const secondaryColumn = displayColumns.find(c => !c.isPrimary && c.name !== primaryColumn?.name);

  return (
    <div className={styles.listContainer}>
      {props.records.map((record) => {
        const isSelected = props.selectedRecordIds.includes(record.id);

        const primaryRenderer = ColumnRendererService.getRenderer(primaryColumn);
        const primaryValue = primaryRenderer(record[primaryColumn.name], record, primaryColumn);

        let secondaryValue: React.ReactElement | string | null = null;
        if (secondaryColumn) {
          const secondaryRenderer = ColumnRendererService.getRenderer(secondaryColumn);
          secondaryValue = secondaryRenderer(record[secondaryColumn.name], record, secondaryColumn);
        }

        return (
          <div
            key={record.id}
            className={`${styles.row} ${isSelected ? styles.rowSelected : ""}`}
            onClick={() => props.onRecordClick?.(record.id)}
          >
            <div className={styles.primaryColumn}>{primaryValue}</div>
            {secondaryValue && (
              <div className={styles.secondaryColumn}>{secondaryValue}</div>
            )}
          </div>
        );
      })}
    </div>
  );
};
```

**Why:**
- Conditionally uses virtualization based on record count
- Falls back to original implementation for small datasets (<100 records)
- No breaking changes to existing API
- Seamless integration with column renderers

---

### Step 5: Create VirtualizedGridView Component

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/VirtualizedGridView.tsx`

```typescript
import * as React from "react";
import { FixedSizeList as List } from "react-window";
import {
  Text,
  makeStyles,
  tokens,
  shorthands
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn } from "../../types/DatasetTypes";
import { ColumnRendererService } from "../../services/ColumnRendererService";

const useStyles = makeStyles({
  gridContainer: {
    width: "100%",
    height: "100%",
    display: "flex",
    flexDirection: "column"
  },
  header: {
    display: "flex",
    ...shorthands.borderBottom("2px", "solid", tokens.colorNeutralStroke1),
    backgroundColor: tokens.colorNeutralBackground2,
    fontWeight: tokens.fontWeightSemibold,
    position: "sticky",
    top: 0,
    zIndex: 1
  },
  headerCell: {
    ...shorthands.padding("12px", "16px"),
    textAlign: "left",
    color: tokens.colorNeutralForeground1
  },
  row: {
    display: "flex",
    ...shorthands.borderBottom("1px", "solid", tokens.colorNeutralStroke2),
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover
    }
  },
  rowSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected,
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Selected
    }
  },
  cell: {
    ...shorthands.padding("12px", "16px"),
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap"
  }
});

export interface VirtualizedGridViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  itemHeight: number;
  overscanCount: number;
  onRecordClick?: (recordId: string) => void;
}

export const VirtualizedGridView: React.FC<VirtualizedGridViewProps> = (props) => {
  const styles = useStyles();
  const listRef = React.useRef<List>(null);

  // Filter to only readable columns
  const displayColumns = props.columns.filter((col) => col.canRead !== false);

  // Calculate column width (equal distribution for simplicity)
  const columnWidth = `${100 / displayColumns.length}%`;

  const renderRow = React.useCallback(({ index, style }: { index: number; style: React.CSSProperties }) => {
    const record = props.records[index];
    const isSelected = props.selectedRecordIds.includes(record.id);

    return (
      <div
        style={style}
        className={`${styles.row} ${isSelected ? styles.rowSelected : ""}`}
        onClick={() => props.onRecordClick?.(record.id)}
      >
        {displayColumns.map((col) => {
          const renderer = ColumnRendererService.getRenderer(col);
          const value = renderer(record[col.name], record, col);

          return (
            <div
              key={col.name}
              className={styles.cell}
              style={{ width: columnWidth }}
            >
              {value}
            </div>
          );
        })}
      </div>
    );
  }, [props.records, props.selectedRecordIds, props.onRecordClick, displayColumns, columnWidth, styles]);

  return (
    <div className={styles.gridContainer}>
      {/* Header row */}
      <div className={styles.header}>
        {displayColumns.map((col) => (
          <div
            key={col.name}
            className={styles.headerCell}
            style={{ width: columnWidth }}
          >
            {col.displayName}
          </div>
        ))}
      </div>

      {/* Virtualized rows */}
      <List
        ref={listRef}
        height={600} // Will be calculated from parent container
        itemCount={props.records.length}
        itemSize={props.itemHeight}
        width="100%"
        overscanCount={props.overscanCount}
      >
        {renderRow}
      </List>
    </div>
  );
};
```

**Why:**
- Simple virtualized grid using FixedSizeList
- Fixed row height for performance
- Column renderers integrated
- Field security respected
- Fluent UI v9 styling

**Note:** Fluent UI DataGrid v9 has built-in virtualization, but this provides explicit control over virtualization behavior.

---

### Step 6: Update GridView to Support Virtualization

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx`

**Add imports:**
```typescript
import { useVirtualization } from "../../hooks/useVirtualization";
import { VirtualizedGridView } from "./VirtualizedGridView";
```

**Update interface:**
```typescript
export interface GridViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  enableVirtualization?: boolean; // NEW
  onRecordClick?: (recordId: string) => void;
  onSelectionChange?: (selectedIds: string[]) => void;
}
```

**Add virtualization check at top of component:**
```typescript
export const GridView: React.FC<GridViewProps> = (props) => {
  const styles = useStyles();

  // Check if virtualization should be enabled
  const virtualization = useVirtualization(props.records.length, {
    enabled: props.enableVirtualization ?? true
  });

  // Filter to only readable columns
  const displayColumns = props.columns.filter(
    (col) => col.canRead !== false
  );

  // Use custom virtualized grid for very large datasets (>1000 records)
  // For 100-1000 records, rely on Fluent DataGrid's built-in virtualization
  if (virtualization.shouldVirtualize && props.records.length > 1000) {
    return (
      <VirtualizedGridView
        records={props.records}
        columns={displayColumns}
        selectedRecordIds={props.selectedRecordIds}
        itemHeight={virtualization.itemHeight}
        overscanCount={virtualization.overscanCount}
        onRecordClick={props.onRecordClick}
      />
    );
  }

  // Rest of existing GridView implementation using Fluent DataGrid
  // ...
```

**Why:**
- Uses custom virtualized grid for 1000+ records
- Leverages Fluent DataGrid's built-in virtualization for 100-1000 records
- Falls back to standard rendering for <100 records
- Progressive enhancement approach

---

### Step 7: Export New Virtualization Components

**File:** `src/shared/Spaarke.UI.Components/src/hooks/index.ts`

```typescript
export * from "./useDatasetMode";
export * from "./useVirtualization"; // NEW
```

**File:** `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/index.ts`

```typescript
export * from "./GridView";
export * from "./CardView";
export * from "./ListView";
export * from "./VirtualizedGridView"; // NEW
export * from "./VirtualizedListView"; // NEW
```

---

### Step 8: Update Main Component to Pass Virtualization Config

**File:** `src/shared/Spaarke.UI.Components/src/components/UniversalDatasetGrid.tsx`

**Update props interface:**
```typescript
export interface UniversalDatasetGridProps {
  // ... existing props
  enableVirtualization?: boolean;
}
```

**Pass to views:**
```typescript
{currentView === "grid" && (
  <GridView
    records={records}
    columns={columns}
    selectedRecordIds={selectedRecordIds}
    enableVirtualization={props.enableVirtualization ?? true} // NEW
    onRecordClick={handleRecordClick}
    onSelectionChange={handleSelectionChange}
  />
)}

{currentView === "list" && (
  <ListView
    records={records}
    columns={columns}
    selectedRecordIds={selectedRecordIds}
    enableVirtualization={props.enableVirtualization ?? true} // NEW
    onRecordClick={handleRecordClick}
  />
)}
```

---

### Step 9: Build and Verify

```bash
cd src/shared/Spaarke.UI.Components
npm run build
```

**Validation:**
- ✅ Build succeeds with 0 TypeScript errors
- ✅ All virtualization components compile
- ✅ Hooks export correctly

---

### Step 10: Update Type Exports

**File:** `src/shared/Spaarke.UI.Components/src/types/index.ts`

```typescript
export * from "./DatasetTypes";
export * from "./CommandTypes";
export * from "./ColumnRendererTypes";
export { PrivilegeService } from "../services/PrivilegeService";
export { FieldSecurityService } from "../services/FieldSecurityService";
export { ColumnRendererService } from "../services/ColumnRendererService";
export { useDatasetMode } from "../hooks/useDatasetMode";
export { useVirtualization } from "../hooks/useVirtualization"; // NEW
```

---

## Validation Checklist

Run these commands to verify completion:

```bash
# 1. Verify react-window installed
cd src/shared/Spaarke.UI.Components
npm list react-window @types/react-window

# 2. Verify files exist
ls src/hooks/useVirtualization.ts
ls src/components/DatasetGrid/VirtualizedGridView.tsx
ls src/components/DatasetGrid/VirtualizedListView.tsx

# 3. Build succeeds
npm run build

# 4. Verify exports
grep -r "useVirtualization" src/types/index.ts
grep -r "VirtualizedGridView" src/components/DatasetGrid/index.ts
grep -r "VirtualizedListView" src/components/DatasetGrid/index.ts
```

---

## Success Criteria

- ✅ react-window installed (v1.8.10)
- ✅ `useVirtualization` hook implemented
- ✅ `VirtualizedGridView` component created
- ✅ `VirtualizedListView` component created
- ✅ ListView conditionally uses virtualization (>100 records)
- ✅ GridView conditionally uses virtualization (>1000 records)
- ✅ All exports updated
- ✅ Build succeeds with 0 errors
- ✅ Fixed row heights for optimal performance
- ✅ Overscan prevents blank rows during scroll
- ✅ Field security integration maintained
- ✅ Fluent UI v9 styling throughout

---

## Performance Targets

| Metric | Target | Validation |
|--------|--------|------------|
| Initial render | <500ms | Test with 1000 records |
| Scroll performance | 60 FPS | Visual inspection |
| DOM elements | <100 | DevTools inspection with 10K records |
| Memory usage | Stable | No memory leaks during scroll |

---

## Testing Plan

1. **Small dataset (<100 records):** Standard rendering (no virtualization)
2. **Medium dataset (100-1000 records):** Fluent DataGrid built-in virtualization
3. **Large dataset (1000+ records):** Custom react-window virtualization
4. **Field security:** Secured fields show `***` in virtualized views
5. **Selection:** Click handlers work in virtualized rows
6. **Column renderers:** All types render correctly in virtualized views

---

## Common Issues

### Issue: Blank rows during fast scrolling
**Solution:** Increase `overscanCount` in useVirtualization hook (default: 5)

### Issue: Row height inconsistencies
**Solution:** Ensure all rows have fixed height (default: 44px matches Fluent UI)

### Issue: Horizontal scrolling not working
**Solution:** Set explicit width on VirtualizedGridView columns

### Issue: Selection state lost
**Solution:** Ensure `selectedRecordIds` prop updates correctly in parent

---

## Deliverables

- ✅ `src/hooks/useVirtualization.ts` - Virtualization logic
- ✅ `src/components/DatasetGrid/VirtualizedGridView.tsx` - Virtual grid
- ✅ `src/components/DatasetGrid/VirtualizedListView.tsx` - Virtual list
- ✅ Updated `GridView.tsx` with virtualization support
- ✅ Updated `ListView.tsx` with virtualization support
- ✅ Updated exports in `index.ts` files
- ✅ Build output with 0 errors

---

## Next Steps

1. ✅ Mark TASK-3.3 complete
2. ➡️ Proceed to [TASK-3.4-TOOLBAR-UI.md](./TASK-3.4-TOOLBAR-UI.md)
3. Test virtualization with large datasets
4. Verify performance metrics
5. Add E2E tests for virtualization in Phase 4

---

**Estimated Time:** 4 hours
**Actual Time:** _(Fill in upon completion)_
**Completion Date:** _(Fill in upon completion)_
**Status:** 📝 Ready for execution
