# Task 2.4: Implement CardView and ListView with Infinite Scroll

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 2 - Core Component Development
**Estimated Time:** 4 hours
**Prerequisites:** [TASK-2.3-GRID-VIEW-IMPLEMENTATION.md](./TASK-2.3-GRID-VIEW-IMPLEMENTATION.md)
**Next Task:** [TASK-3.1-COMMAND-SYSTEM.md](./TASK-3.1-COMMAND-SYSTEM.md)

---

## Objective

Implement CardView (tile layout) and ListView (compact list) components using Fluent UI v9 with the same infinite scroll capabilities as GridView.

**Why:**
- **CardView**: Better for visual content (documents with thumbnails, products, contacts with photos)
- **ListView**: Better for mobile, simple records, and vertical scrolling
- Both provide alternative UX patterns for different use cases

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Card patterns, responsive design
- Same infinite scroll logic as TASK-2.3

**Key Rules:**
- ✅ Use Fluent UI v9 Card component (NOT deprecated Power Apps Cards)
- ✅ Responsive grid layout (auto-fills available space)
- ✅ Same scroll behavior as GridView (Auto/Infinite/Paged)
- ✅ All styling via Griffel makeStyles
- ✅ Support selection in both views

---

## Step 1: Implement CardView Component

**Replace `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/CardView.tsx`:**

```typescript
/**
 * CardView - Tile/Card layout for visual content
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  Card,
  CardHeader,
  CardPreview,
  makeStyles,
  tokens,
  Text,
  Badge,
  Button,
  Spinner,
  Checkbox
} from "@fluentui/react-components";
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from "../../types";

export interface ICardViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
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
  scrollContainer: {
    flex: 1,
    overflow: "auto",
    padding: tokens.spacingVerticalM
  },
  cardGrid: {
    display: "grid",
    gridTemplateColumns: "repeat(auto-fill, minmax(280px, 1fr))",
    gap: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalL
  },
  card: {
    cursor: "pointer",
    height: "240px",
    ":hover": {
      boxShadow: tokens.shadow16
    }
  },
  cardSelected: {
    borderColor: tokens.colorBrandForeground1,
    borderWidth: "2px"
  },
  cardHeader: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS
  },
  cardContent: {
    padding: tokens.spacingVerticalM,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalS
  },
  fieldRow: {
    display: "flex",
    justifyContent: "space-between",
    alignItems: "center"
  },
  fieldLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200
  },
  fieldValue: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold
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
    width: "calc(100% - 32px)",
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM
  },
  emptyState: {
    padding: tokens.spacingVerticalXXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground3
  }
});

export const CardView: React.FC<ICardViewProps> = (props) => {
  const styles = useStyles();
  const scrollContainerRef = React.useRef<HTMLDivElement>(null);

  // Determine if infinite scroll should be active
  const isInfiniteScroll = React.useMemo(() => {
    if (props.scrollBehavior === "Infinite") return true;
    if (props.scrollBehavior === "Paged") return false;
    return props.records.length > 100;
  }, [props.scrollBehavior, props.records.length]);

  // Handle scroll for infinite scroll
  const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
    if (!isInfiniteScroll || !props.hasNextPage || props.loading) {
      return;
    }

    const container = e.currentTarget;
    const { scrollTop, scrollHeight, clientHeight } = container;
    const scrollPercentage = (scrollTop + clientHeight) / scrollHeight;

    if (scrollPercentage > 0.9) {
      props.loadNextPage();
    }
  }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);

  // Handle card selection
  const handleCardSelect = React.useCallback((recordId: string, checked: boolean) => {
    if (checked) {
      props.onSelectionChange([...props.selectedRecordIds, recordId]);
    } else {
      props.onSelectionChange(props.selectedRecordIds.filter(id => id !== recordId));
    }
  }, [props]);

  // Handle card click
  const handleCardClick = React.useCallback((record: IDatasetRecord, e: React.MouseEvent) => {
    // Don't trigger if clicking checkbox
    if ((e.target as HTMLElement).closest('input[type="checkbox"]')) {
      return;
    }
    props.onRecordClick(record);
  }, [props]);

  // Get primary column (first 2-3 columns to display)
  const displayColumns = React.useMemo(() => {
    return props.columns.slice(0, 3);
  }, [props.columns]);

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
        className={styles.scrollContainer}
        ref={scrollContainerRef}
        onScroll={handleScroll}
      >
        <div className={styles.cardGrid}>
          {props.records.map((record) => {
            const isSelected = props.selectedRecordIds.includes(record.id);
            const primaryField = displayColumns[0];
            const primaryValue = primaryField ? String(record[primaryField.name] || "") : record.id;

            return (
              <Card
                key={record.id}
                className={`${styles.card} ${isSelected ? styles.cardSelected : ""}`}
                onClick={(e) => handleCardClick(record, e)}
              >
                <CardHeader
                  header={
                    <div className={styles.cardHeader}>
                      <Checkbox
                        checked={isSelected}
                        onChange={(_e, data) => handleCardSelect(record.id, !!data.checked)}
                      />
                      <Text weight="semibold" truncate>
                        {primaryValue}
                      </Text>
                    </div>
                  }
                />
                <div className={styles.cardContent}>
                  {displayColumns.slice(1).map((col) => (
                    <div key={col.name} className={styles.fieldRow}>
                      <Text className={styles.fieldLabel}>{col.displayName}:</Text>
                      <Text className={styles.fieldValue} truncate>
                        {String(record[col.name] || "-")}
                      </Text>
                    </div>
                  ))}
                </div>
              </Card>
            );
          })}
        </div>
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

## Step 2: Implement ListView Component

**Replace `src/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx`:**

```typescript
/**
 * ListView - Compact list layout for simple records
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Spinner,
  Checkbox,
  mergeClasses
} from "@fluentui/react-components";
import { ChevronRightRegular } from "@fluentui/react-icons";
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from "../../types";

export interface IListViewProps {
  records: IDatasetRecord[];
  columns: IDatasetColumn[];
  selectedRecordIds: string[];
  onSelectionChange: (selectedIds: string[]) => void;
  onRecordClick: (record: IDatasetRecord) => void;
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
  scrollContainer: {
    flex: 1,
    overflow: "auto"
  },
  listContainer: {
    display: "flex",
    flexDirection: "column"
  },
  listItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: "1px",
    borderBottomStyle: "solid",
    borderBottomColor: tokens.colorNeutralStroke2,
    cursor: "pointer",
    ":hover": {
      backgroundColor: tokens.colorNeutralBackground1Hover
    }
  },
  listItemSelected: {
    backgroundColor: tokens.colorNeutralBackground1Selected
  },
  listItemContent: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXXS,
    minWidth: 0
  },
  primaryText: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap"
  },
  secondaryText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap"
  },
  metadataText: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    marginLeft: "auto",
    flexShrink: 0
  },
  chevron: {
    color: tokens.colorNeutralForeground3,
    flexShrink: 0
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
    width: "calc(100% - 32px)",
    marginLeft: tokens.spacingHorizontalM,
    marginRight: tokens.spacingHorizontalM
  },
  emptyState: {
    padding: tokens.spacingVerticalXXL,
    textAlign: "center",
    color: tokens.colorNeutralForeground3
  }
});

export const ListView: React.FC<IListViewProps> = (props) => {
  const styles = useStyles();
  const scrollContainerRef = React.useRef<HTMLDivElement>(null);

  // Determine if infinite scroll should be active
  const isInfiniteScroll = React.useMemo(() => {
    if (props.scrollBehavior === "Infinite") return true;
    if (props.scrollBehavior === "Paged") return false;
    return props.records.length > 100;
  }, [props.scrollBehavior, props.records.length]);

  // Handle scroll for infinite scroll
  const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
    if (!isInfiniteScroll || !props.hasNextPage || props.loading) {
      return;
    }

    const container = e.currentTarget;
    const { scrollTop, scrollHeight, clientHeight } = container;
    const scrollPercentage = (scrollTop + clientHeight) / scrollHeight;

    if (scrollPercentage > 0.9) {
      props.loadNextPage();
    }
  }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);

  // Handle item selection
  const handleItemSelect = React.useCallback((recordId: string, checked: boolean) => {
    if (checked) {
      props.onSelectionChange([...props.selectedRecordIds, recordId]);
    } else {
      props.onSelectionChange(props.selectedRecordIds.filter(id => id !== recordId));
    }
  }, [props]);

  // Handle item click
  const handleItemClick = React.useCallback((record: IDatasetRecord, e: React.MouseEvent) => {
    // Don't trigger if clicking checkbox
    if ((e.target as HTMLElement).closest('input[type="checkbox"]')) {
      return;
    }
    props.onRecordClick(record);
  }, [props]);

  // Get columns to display (primary, secondary, metadata)
  const [primaryCol, secondaryCol, metadataCol] = React.useMemo(() => {
    return props.columns.slice(0, 3);
  }, [props.columns]);

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
        className={styles.scrollContainer}
        ref={scrollContainerRef}
        onScroll={handleScroll}
      >
        <div className={styles.listContainer}>
          {props.records.map((record) => {
            const isSelected = props.selectedRecordIds.includes(record.id);
            const primaryValue = primaryCol ? String(record[primaryCol.name] || "") : record.id;
            const secondaryValue = secondaryCol ? String(record[secondaryCol.name] || "") : "";
            const metadataValue = metadataCol ? String(record[metadataCol.name] || "") : "";

            return (
              <div
                key={record.id}
                className={mergeClasses(
                  styles.listItem,
                  isSelected && styles.listItemSelected
                )}
                onClick={(e) => handleItemClick(record, e)}
              >
                <Checkbox
                  checked={isSelected}
                  onChange={(_e, data) => handleItemSelect(record.id, !!data.checked)}
                />
                <div className={styles.listItemContent}>
                  <Text className={styles.primaryText}>{primaryValue}</Text>
                  {secondaryValue && (
                    <Text className={styles.secondaryText}>{secondaryValue}</Text>
                  )}
                </div>
                {metadataValue && (
                  <Text className={styles.metadataText}>{metadataValue}</Text>
                )}
                <ChevronRightRegular className={styles.chevron} />
              </div>
            );
          })}
        </div>
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

## Step 3: Build and Test

```bash
# Build shared library
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
npm run build

# Expected output: Successfully compiled
```

---

## Validation Checklist

```bash
# 1. Verify CardView updated
cd /c/code_files/spaarke/src/shared/Spaarke.UI.Components
cat src/components/DatasetGrid/CardView.tsx | grep "Card"
# Should show Card imports

# 2. Verify ListView updated
cat src/components/DatasetGrid/ListView.tsx | grep "ChevronRightRegular"
# Should show icon import

# 3. Verify build succeeds
npm run build
# Should succeed with 0 errors

# 4. Check compiled output
ls -lh dist/components/DatasetGrid/
# Should show CardView.js and ListView.js
```

---

## Success Criteria

**CardView:**
- ✅ Uses Fluent UI v9 Card component
- ✅ Responsive grid layout (auto-fills space)
- ✅ Shows 2-3 primary fields per card
- ✅ Checkbox selection
- ✅ Hover effects
- ✅ Selected state visual indicator
- ✅ Infinite scroll at 90%
- ✅ Empty state handling

**ListView:**
- ✅ Compact vertical list layout
- ✅ Primary text (bold) + secondary text
- ✅ Metadata on right (date/status)
- ✅ Chevron icon indicates clickable
- ✅ Checkbox selection
- ✅ Hover background
- ✅ Selected state background
- ✅ Infinite scroll at 90%
- ✅ Empty state handling

**Both Views:**
- ✅ Same scroll behavior as GridView (Auto/Infinite/Paged)
- ✅ All styling via Griffel and tokens
- ✅ No hard-coded colors or spacing
- ✅ Loading indicators (spinner + button)

---

## Deliverables

**Files Updated:**
1. `src/components/DatasetGrid/CardView.tsx` - Full card/tile implementation
2. `src/components/DatasetGrid/ListView.tsx` - Full compact list implementation

**Build Output:**
- Updated `dist/components/DatasetGrid/CardView.js`
- Updated `dist/components/DatasetGrid/ListView.js`

---

## Visual Layout Examples

### CardView Layout:
```
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ ☑ Document 1 │  │ ☐ Document 2 │  │ ☐ Document 3 │
├──────────────┤  ├──────────────┤  ├──────────────┤
│ Status:      │  │ Status:      │  │ Status:      │
│ Active       │  │ Pending      │  │ Complete     │
│              │  │              │  │              │
│ Date:        │  │ Date:        │  │ Date:        │
│ 2025-01-15   │  │ 2025-01-14   │  │ 2025-01-13   │
└──────────────┘  └──────────────┘  └──────────────┘

[Responsive: 1-4 columns depending on screen width]
```

### ListView Layout:
```
┌────────────────────────────────────────────────────┐
│ ☑  Document 1                    Jan 15, 2025  →  │
│    Status: Active                                  │
├────────────────────────────────────────────────────┤
│ ☐  Document 2                    Jan 14, 2025  →  │
│    Status: Pending                                 │
├────────────────────────────────────────────────────┤
│ ☐  Document 3                    Jan 13, 2025  →  │
│    Status: Complete                                │
└────────────────────────────────────────────────────┘

[Fixed single column, optimized for vertical scrolling]
```

---

## Common Issues & Solutions

**Issue:** Cards not wrapping on small screens
**Solution:** CSS grid with `auto-fill` handles this automatically

**Issue:** List items too tall on mobile
**Solution:** Padding uses tokens which are responsive

**Issue:** Checkbox clicks trigger card/item click
**Solution:** Check `e.target.closest('input[type="checkbox"]')` to prevent

**Issue:** Selected state not visible
**Solution:** Use border color + background color for clear indication

---

## Performance Considerations

**CardView:**
- Grid layout is GPU-accelerated
- Only renders visible cards (browser handles this)
- For 1000 cards: ~5-10MB memory

**ListView:**
- Minimal DOM (single div per row)
- Very fast rendering (simple flexbox)
- For 1000 rows: ~2-3MB memory

**Infinite Scroll:**
- Same 90% threshold as GridView
- Same loading state checks
- Cumulative loading (records stay in DOM)

---

## Next Steps

After completing this task:
1. **Phase 2 COMPLETE!** 🎉
2. Proceed to [TASK-3.1-COMMAND-SYSTEM.md](./TASK-3.1-COMMAND-SYSTEM.md) (Phase 3)
3. Will implement command toolbar and custom actions

---

**Task Status:** Ready for Execution
**Estimated Time:** 4 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
