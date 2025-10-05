# Phase C: Performance & Dataset Optimization

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** C - Performance & Dataset Optimization
**Priority:** MEDIUM
**Estimated Effort:** 1-2 days
**Status:** 🔴 Not Started
**Depends On:** Phase A completion

---

## Objective

Optimize the control for large datasets using PCF paging APIs, virtualization, and efficient state management to ensure smooth performance with 100+ records.

---

## Current Issues

**From Compliance Assessment (Section 2.1):**
> "There is no use of dataset paging, virtualization, or PCF dataset APIs beyond raw iteration. Large data sets will render slowly and degrade host app responsiveness."

**Problems:**
1. Renders ALL records at once (no paging)
2. No virtualization for long lists
3. Aggressive `notifyOutputChanged()` calls
4. Re-renders entire grid on selection changes
5. No loading states or performance optimization

---

## Tasks

### Task C.1: Implement Dataset Paging

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/DatasetGrid.tsx`

**Objective:** Use PCF paging API for large datasets

**AI Coding Instructions:**

```typescript
/**
 * Implement PCF dataset paging
 *
 * PCF provides built-in paging support via context.parameters.dataset.paging
 */

interface DatasetGridProps {
    dataset: ComponentFramework.PropertyHelper.DataSetApi.EntityRecord & {
        // ... existing props ...
        paging: {
            hasNextPage: boolean;
            hasPreviousPage: boolean;
            loadNextPage(): void;
            loadPreviousPage(): void;
            reset(): void;
            setPageSize(pageSize: number): void;
            totalResultCount: number;
            pageSize: number;
            firstPageNumber: number;
        };
    };
    selectedRecordIds: string[];
    onSelectionChange: (recordIds: string[]) => void;
}

export const DatasetGrid: React.FC<DatasetGridProps> = ({
    dataset,
    selectedRecordIds,
    onSelectionChange
}) => {
    const paging = dataset.paging;

    // Set initial page size (e.g., 50 records per page)
    React.useEffect(() => {
        if (paging.pageSize !== 50) {
            paging.setPageSize(50);
        }
    }, [paging]);

    // Check if there are more records to load
    const hasMoreRecords = paging.hasNextPage;
    const totalCount = paging.totalResultCount;
    const currentCount = dataset.sortedRecordIds.length;

    // Convert dataset to rows
    const rows = React.useMemo<GridRow[]>(() => {
        return dataset.sortedRecordIds.map(recordId => {
            const record = dataset.records[recordId];
            const row: GridRow = { recordId };

            dataset.columns.forEach(column => {
                const value = record.getFormattedValue(column.name);
                row[column.name] = value || '';
            });

            return row;
        });
    }, [dataset.sortedRecordIds, dataset.records, dataset.columns]);

    // Load more records
    const handleLoadMore = React.useCallback(() => {
        console.log('[DatasetGrid] Loading next page');
        paging.loadNextPage();
    }, [paging]);

    return (
        <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
            {/* Grid */}
            <div style={{ flex: 1, overflow: 'auto' }}>
                <DataGrid
                    items={rows}
                    columns={columns}
                    // ... other props ...
                >
                    {/* DataGrid content */}
                </DataGrid>
            </div>

            {/* Paging Controls */}
            {hasMoreRecords && (
                <div
                    style={{
                        padding: tokens.spacingVerticalM,
                        textAlign: 'center',
                        borderTop: `1px solid ${tokens.colorNeutralStroke1}`
                    }}
                >
                    <Button
                        appearance="primary"
                        onClick={handleLoadMore}
                    >
                        Load More ({currentCount} of {totalCount} records)
                    </Button>
                </div>
            )}
        </div>
    );
};
```

---

### Task C.2: Optimize State Management

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/components/UniversalDatasetGridRoot.tsx`

**Objective:** Reduce unnecessary renders and `notifyOutputChanged()` calls

**AI Coding Instructions:**

```typescript
/**
 * Optimize state management with React best practices
 */

import * as React from 'react';
import { IInputs } from '../generated/ManifestTypes';
import { GridConfiguration } from '../types';
import { CommandBar } from './CommandBar';
import { DatasetGrid } from './DatasetGrid';

export const UniversalDatasetGridRoot: React.FC<UniversalDatasetGridRootProps> = ({
    context,
    notifyOutputChanged,
    config
}) => {
    const dataset = context.parameters.dataset;

    // Debounce notifyOutputChanged to avoid excessive calls
    const debouncedNotify = React.useMemo(
        () => debounce(notifyOutputChanged, 300),
        [notifyOutputChanged]
    );

    const [selectedRecordIds, setSelectedRecordIds] = React.useState<string[]>(
        dataset.getSelectedRecordIds() || []
    );

    // Optimize selection change handler with useCallback
    const handleSelectionChange = React.useCallback((recordIds: string[]) => {
        // Only update if selection actually changed
        setSelectedRecordIds(prevIds => {
            if (JSON.stringify(prevIds) === JSON.stringify(recordIds)) {
                return prevIds; // No change - prevent re-render
            }
            return recordIds;
        });

        dataset.setSelectedRecordIds(recordIds);
        debouncedNotify(); // Debounced notification
    }, [dataset, debouncedNotify]);

    // Memoize selected records to prevent unnecessary recalculation
    const selectedRecords = React.useMemo(() => {
        return selectedRecordIds
            .map(id => dataset.records[id])
            .filter(record => record != null);
    }, [selectedRecordIds, dataset.records]);

    // Memoize command handler
    const handleCommandExecute = React.useCallback((commandId: string) => {
        console.log(`[UniversalDatasetGridRoot] Command executed: ${commandId}`);
        // ... command handling ...
    }, []);

    // ... rest of component ...
};

/**
 * Simple debounce utility
 */
function debounce<T extends (...args: any[]) => any>(
    func: T,
    wait: number
): (...args: Parameters<T>) => void {
    let timeout: NodeJS.Timeout | null = null;

    return (...args: Parameters<T>) => {
        if (timeout) {
            clearTimeout(timeout);
        }
        timeout = setTimeout(() => {
            func(...args);
        }, wait);
    };
}
```

---

### Task C.3: Add Virtualization (Optional)

**Objective:** For datasets with 100+ records, only render visible rows

**AI Coding Instructions:**

```typescript
/**
 * Add virtualization using react-window or Fluent UI's built-in virtualization
 *
 * NOTE: This is OPTIONAL - only implement if performance testing shows
 * it's needed for large datasets.
 *
 * Fluent UI DataGrid may have built-in virtualization - check documentation.
 * If not, consider react-window or react-virtualized.
 */

// Example with react-window (if needed):
// npm install react-window --save
// npm install @types/react-window --save-dev

import { FixedSizeList as List } from 'react-window';

// Wrap DataGrid in virtual scroller
<List
  height={600}
  itemCount={rows.length}
  itemSize={35}
  width={'100%'}
>
  {({ index, style }) => (
    <div style={style}>
      {/* Render row at index */}
    </div>
  )}
</List>
```

**Decision Point:** Implement virtualization only if:
- Dataset has > 200 records
- Performance testing shows slow rendering
- Fluent DataGrid doesn't have built-in virtualization

---

## Testing Checklist

### Performance Testing
- [ ] Load dataset with 100 records - no lag
- [ ] Load dataset with 500 records - acceptable performance
- [ ] Load dataset with 1000+ records - paging works correctly
- [ ] Selection changes feel responsive
- [ ] No janky scrolling or UI freezes

### Paging Testing
- [ ] First page loads correctly
- [ ] "Load More" button appears when hasNextPage = true
- [ ] "Load More" loads next page correctly
- [ ] Total count displayed accurately
- [ ] No duplicate records after paging

### State Management Testing
- [ ] Selection changes don't cause full re-render
- [ ] notifyOutputChanged not called excessively
- [ ] No memory leaks (check DevTools Memory profiler)
- [ ] React DevTools shows minimal re-renders

### Edge Cases
- [ ] Empty dataset (0 records)
- [ ] Single record
- [ ] Exactly one page of records
- [ ] Very large record counts (10,000+)

---

## Performance Benchmarks

**Target Metrics:**
- ⚡ Initial render: < 1 second for 100 records
- ⚡ Selection change: < 100ms response time
- ⚡ Scroll performance: 60 FPS
- ⚡ Memory usage: < 50MB increase for 1000 records
- ⚡ Paging: < 500ms to load next page

**Measurement:**
Use browser DevTools Performance profiler to measure:
```javascript
console.time('GridRender');
// Render code
console.timeEnd('GridRender');
```

---

## Validation Criteria

### Success Criteria:
1. ✅ Dataset paging implemented and working
2. ✅ notifyOutputChanged debounced (< 5 calls/second)
3. ✅ Selection state optimized (no unnecessary re-renders)
4. ✅ Performance benchmarks met
5. ✅ Large datasets (1000+ records) work smoothly

### Code Quality:
- ✅ Use React.useMemo for expensive computations
- ✅ Use React.useCallback for event handlers
- ✅ Debounce PCF notifications
- ✅ No console errors or warnings

---

## References

- **Compliance Assessment:** Section 2.1 "Dataset operations"
- **Compliance Assessment:** Section 3 "Recommended Remediation Plan" - Step 4
- **PCF Paging API:** https://learn.microsoft.com/power-apps/developer/component-framework/reference/paging
- **React Performance:** https://react.dev/learn/render-and-commit
- **React Profiler:** https://react.dev/reference/react/Profiler

---

## Completion Criteria

Phase C is complete when:
1. All tasks completed (C.1, C.2, C.3 if needed)
2. Performance benchmarks met
3. Paging works correctly
4. All validation criteria met
5. No performance regressions

---

_Document Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
_Requires: Phase A completion_
