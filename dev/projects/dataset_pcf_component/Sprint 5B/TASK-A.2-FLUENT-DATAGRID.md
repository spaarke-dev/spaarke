# Task A.2: Implement Fluent UI DataGrid

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** A - Architecture Refactor
**Priority:** CRITICAL
**Estimated Effort:** 6-8 hours
**Status:** 🔴 Not Started
**Depends On:** Task A.1 (Single React Root)

---

## Objective

Replace the raw HTML `<table>` grid with Fluent UI v9's `DataGrid` component, ensuring full ADR compliance and proper dataset integration.

---

## Current Issues

**From Compliance Assessment (Section 2.2):**
> "The core grid/toolbar uses plain HTML tables and buttons. ADR requires Fluent UI v9 components (e.g., `DataGrid`, `Checkbox`, `Toolbar`, `Menu`)."

**Current Implementation Problems:**
1. Uses raw `<table>`, `<tr>`, `<td>` elements
2. Hard-coded inline styles (`borderCollapse`, `padding`, etc.)
3. Manual checkbox creation with `createElement`
4. No keyboard navigation support
5. No accessibility attributes
6. Violates ADR Fluent UI requirement

**Current Code Location:**
- `index.ts` lines 217-330: `renderMinimalGrid()` method
- Creates HTML table with manual DOM manipulation

---

## Target Implementation

**Fluent UI DataGrid Pattern:**

```typescript
<DataGrid
  items={rows}
  columns={columns}
  selectionMode="multiselect"
  sortable
  focusMode="composite"
  aria-label="Document dataset grid"
>
  {/* DataGrid handles rendering internally */}
</DataGrid>
```

**Key Benefits:**
- ✅ Built-in keyboard navigation
- ✅ ARIA attributes for accessibility
- ✅ Fluent design tokens (no hard-coded styles)
- ✅ Selection management
- ✅ Sorting support
- ✅ Responsive and theme-aware

---

## Implementation Steps

### Step 1: Install Fluent UI DataGrid (if needed)

**AI Coding Instructions:**

```bash
# Check if @fluentui/react-components includes DataGrid
# It should be included in v9.54.0

# Verify in package.json:
# "@fluentui/react-components": "^9.54.0"

# If DataGrid is not available, we may need:
# npm install @fluentui/react-table --save

# Check documentation at:
# https://react.fluentui.dev/?path=/docs/components-table-datagrid--default
```

---

### Step 2: Create DataGrid Component

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/DatasetGrid.tsx`

**AI Coding Instructions:**

```typescript
/**
 * Create new file: DatasetGrid.tsx
 *
 * This component wraps Fluent UI DataGrid and integrates with PCF dataset.
 */

import * as React from 'react';
import {
    DataGrid,
    DataGridHeader,
    DataGridHeaderCell,
    DataGridBody,
    DataGridRow,
    DataGridCell,
    TableCellLayout,
    TableColumnDefinition,
    createTableColumn,
    tokens
} from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';

interface DatasetGridProps {
    /** PCF dataset from context */
    dataset: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord & {
        columns: ComponentFramework.PropertyHelper.DataSetApi.Column[];
        sortedRecordIds: string[];
        records: {
            [id: string]: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord;
        };
        getSelectedRecordIds(): string[];
        setSelectedRecordIds(ids: string[]): void;
    };

    /** Selected record IDs */
    selectedRecordIds: string[];

    /** Selection change callback */
    onSelectionChange: (recordIds: string[]) => void;
}

/**
 * Row data interface for DataGrid.
 */
interface GridRow {
    recordId: string;
    [key: string]: string | number | boolean | Date | null;
}

/**
 * Fluent UI DataGrid component for PCF dataset.
 *
 * Displays dataset records in a fully accessible, keyboard-navigable grid
 * using Fluent UI v9 components.
 */
export const DatasetGrid: React.FC<DatasetGridProps> = ({
    dataset,
    selectedRecordIds,
    onSelectionChange
}) => {
    // Convert dataset to rows format
    const rows = React.useMemo<GridRow[]>(() => {
        return dataset.sortedRecordIds.map(recordId => {
            const record = dataset.records[recordId];
            const row: GridRow = { recordId };

            // Add each column value
            dataset.columns.forEach(column => {
                const value = record.getFormattedValue(column.name);
                row[column.name] = value || '';
            });

            return row;
        });
    }, [dataset.sortedRecordIds, dataset.records, dataset.columns]);

    // Define columns for DataGrid
    const columns = React.useMemo<TableColumnDefinition<GridRow>[]>(() => {
        return dataset.columns.map(column =>
            createTableColumn<GridRow>({
                columnId: column.name,
                compare: (a, b) => {
                    const aVal = a[column.name]?.toString() || '';
                    const bVal = b[column.name]?.toString() || '';
                    return aVal.localeCompare(bVal);
                },
                renderHeaderCell: () => {
                    return column.displayName;
                },
                renderCell: (item) => {
                    return (
                        <TableCellLayout>
                            {item[column.name]?.toString() || ''}
                        </TableCellLayout>
                    );
                }
            })
        );
    }, [dataset.columns]);

    // Handle selection change
    const handleSelectionChange = React.useCallback(
        (event: React.MouseEvent | React.KeyboardEvent, data: { selectedItems: Set<string> }) => {
            const newSelection = Array.from(data.selectedItems);
            onSelectionChange(newSelection);
        },
        [onSelectionChange]
    );

    return (
        <div
            style={{
                height: '100%',
                overflow: 'auto',
                background: tokens.colorNeutralBackground1
            }}
        >
            <DataGrid
                items={rows}
                columns={columns}
                sortable
                selectionMode="multiselect"
                selectedItems={new Set(selectedRecordIds)}
                onSelectionChange={handleSelectionChange as any}
                getRowId={(item) => item.recordId}
                focusMode="composite"
                aria-label="Dataset grid"
                style={{ minWidth: '100%' }}
            >
                <DataGridHeader>
                    <DataGridRow>
                        {({ renderHeaderCell }) => (
                            <DataGridHeaderCell>
                                {renderHeaderCell()}
                            </DataGridHeaderCell>
                        )}
                    </DataGridRow>
                </DataGridHeader>
                <DataGridBody<GridRow>>
                    {({ item, rowId }) => (
                        <DataGridRow<GridRow> key={rowId}>
                            {({ renderCell }) => (
                                <DataGridCell>
                                    {renderCell(item)}
                                </DataGridCell>
                            )}
                        </DataGridRow>
                    )}
                </DataGridBody>
            </DataGrid>
        </div>
    );
};
```

**Note:** The exact API for Fluent UI DataGrid may vary. Check the latest documentation at https://react.fluentui.dev/ and adjust imports/props accordingly.

---

### Step 3: Integrate DataGrid into Main Component

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**AI Coding Instructions:**

```typescript
/**
 * Update UniversalDatasetGridRoot.tsx to use DatasetGrid
 *
 * REPLACE the placeholder grid div with the new DatasetGrid component
 */

import * as React from 'react';
import { IInputs } from '../generated/ManifestTypes';
import { GridConfiguration } from '../types';
import { CommandBar } from './CommandBar';
import { DatasetGrid } from './DatasetGrid'; // ADD THIS IMPORT

interface UniversalDatasetGridRootProps {
    context: ComponentFramework.Context<IInputs>;
    notifyOutputChanged: () => void;
    config: GridConfiguration;
}

export const UniversalDatasetGridRoot: React.FC<UniversalDatasetGridRootProps> = ({
    context,
    notifyOutputChanged,
    config
}) => {
    const dataset = context.parameters.dataset;

    const [selectedRecordIds, setSelectedRecordIds] = React.useState<string[]>(
        dataset.getSelectedRecordIds() || []
    );

    // Sync selection with Power Apps
    const handleSelectionChange = React.useCallback((recordIds: string[]) => {
        setSelectedRecordIds(recordIds);
        dataset.setSelectedRecordIds(recordIds);
        notifyOutputChanged();
    }, [dataset, notifyOutputChanged]);

    React.useEffect(() => {
        const contextSelection = dataset.getSelectedRecordIds() || [];
        if (JSON.stringify(contextSelection) !== JSON.stringify(selectedRecordIds)) {
            setSelectedRecordIds(contextSelection);
        }
    }, [dataset]);

    const handleCommandExecute = React.useCallback((commandId: string) => {
        console.log(`[UniversalDatasetGridRoot] Command executed: ${commandId}`);

        switch (commandId) {
            case 'addFile':
                console.log('Add File - will implement in SDAP phase');
                break;
            case 'removeFile':
                console.log('Remove File - will implement in SDAP phase');
                break;
            case 'updateFile':
                console.log('Update File - will implement in SDAP phase');
                break;
            case 'downloadFile':
                console.log('Download File - will implement in SDAP phase');
                break;
            default:
                console.warn(`Unknown command: ${commandId}`);
        }
    }, []);

    const selectedRecords = React.useMemo(() => {
        return selectedRecordIds
            .map(id => dataset.records[id])
            .filter(record => record != null);
    }, [selectedRecordIds, dataset.records]);

    return (
        <div
            style={{
                display: 'flex',
                flexDirection: 'column',
                height: '100%',
                width: '100%'
            }}
        >
            <CommandBar
                config={config}
                selectedRecordIds={selectedRecordIds}
                selectedRecords={selectedRecords}
                onCommandExecute={handleCommandExecute}
            />

            {/* REPLACE placeholder div with DatasetGrid */}
            <DatasetGrid
                dataset={dataset}
                selectedRecordIds={selectedRecordIds}
                onSelectionChange={handleSelectionChange}
            />
        </div>
    );
};
```

---

### Step 4: Remove Old Grid Implementation

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts`

**AI Coding Instructions:**

```typescript
/**
 * Delete the following methods from index.ts:
 *
 * - renderMinimalGrid() (lines 217-330)
 * - createToolbar() (if exists)
 * - createButton() (if exists)
 * - createCheckbox() (if exists)
 * - toggleSelection() (if exists)
 *
 * These are replaced by React components.
 */

// The index.ts should now only contain:
// - constructor()
// - init()
// - updateView()
// - destroy()
// - getOutputs()
// - renderReactTree() (private helper)
```

---

## Testing Checklist

After implementing this task:

### Build & Deploy
- [ ] Build succeeds: `npm run build`
- [ ] No TypeScript errors
- [ ] ESLint passes
- [ ] Bundle size: Check it's still under 5 MB
- [ ] Deploy: `pac pcf push --publisher-prefix sprk`

### Functional Testing
- [ ] Grid displays all dataset columns
- [ ] Grid displays all dataset rows
- [ ] Column headers show correct names
- [ ] Cell values display correctly
- [ ] Row selection works (click checkbox)
- [ ] Multi-select works (Ctrl+Click, Shift+Click)
- [ ] Selected count updates in command bar
- [ ] Command buttons enable/disable based on selection
- [ ] Selection persists when switching records in Power Apps

### Accessibility Testing
- [ ] Tab navigation works through grid
- [ ] Arrow keys navigate cells
- [ ] Space bar toggles row selection
- [ ] Screen reader announces grid structure
- [ ] High contrast mode works

### Visual Testing
- [ ] Grid uses Fluent design tokens (no custom colors)
- [ ] Grid matches Power Apps theme
- [ ] Columns are properly sized
- [ ] Scrolling works for large datasets
- [ ] No layout shifts or flicker

---

## Validation Criteria

### Success Criteria:
1. ✅ No raw `<table>` elements in code
2. ✅ All grid UI uses Fluent UI DataGrid component
3. ✅ No inline styles (uses Fluent tokens only)
4. ✅ Dataset columns render dynamically from metadata
5. ✅ Selection state syncs with Power Apps
6. ✅ Keyboard navigation works
7. ✅ ARIA attributes present (check DevTools)

### Anti-Patterns to Avoid:
- ❌ Manual `<table>`, `<tr>`, `<td>` elements
- ❌ `createElement()` for grid cells
- ❌ Inline CSS styles
- ❌ Hard-coded column definitions
- ❌ Direct DOM manipulation for selection

---

## Troubleshooting

### Issue: DataGrid not found in @fluentui/react-components

**Solution:**
Check if you need separate package:
```bash
npm install @fluentui/react-table --save
```

Then import from:
```typescript
import { DataGrid, ... } from '@fluentui/react-table';
```

### Issue: Selection not working

**Cause:** PCF dataset selection API mismatch

**Solution:**
Ensure you're calling:
```typescript
dataset.setSelectedRecordIds(recordIds);
notifyOutputChanged();
```

### Issue: Columns not rendering

**Cause:** Dataset columns might not be loaded yet

**Solution:**
Add loading state:
```typescript
if (!dataset.columns || dataset.columns.length === 0) {
    return <div>Loading columns...</div>;
}
```

---

## References

- **Compliance Assessment:** Section 2.2 "Fluent UI v9 ADR Violations"
- **Compliance Assessment:** Section 4.2 "Fluent UI Toolbar & Grid"
- **Fluent UI DataGrid Docs:** https://react.fluentui.dev/?path=/docs/components-table-datagrid--default
- **PCF Dataset API:** https://learn.microsoft.com/power-apps/developer/component-framework/reference/dataset

---

## Completion Criteria

Task A.2 is complete when:
1. DataGrid component implemented and working
2. All old HTML table code removed
3. Selection works bidirectionally with Power Apps
4. All validation criteria met
5. No console errors or warnings
6. Ready to proceed to Task A.3 (Fluent Toolbar)

---

_Task Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
_Requires: Task A.1 completion_
