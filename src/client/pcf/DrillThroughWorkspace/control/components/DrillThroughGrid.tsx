/**
 * DrillThroughGrid Component
 *
 * Purpose-built grid for chart visualization drill-through.
 * Displays dataset records filtered by chart interactions.
 *
 * Features:
 * - Fluent UI v9 DataGrid
 * - FilterStateContext integration for drill interactions
 * - Row highlighting based on active filter
 * - Multi-select with selection sync
 * - Column sorting
 */

import * as React from "react";
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
  TableColumnId,
  tokens,
  Spinner,
  Text,
  makeStyles,
  shorthands,
} from "@fluentui/react-components";
import { useFilterState } from "../hooks/useFilterState";

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    height: "100%",
    width: "100%",
    overflow: "auto",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  loadingContainer: {
    height: "100%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalM),
    backgroundColor: tokens.colorNeutralBackground1,
  },
  emptyContainer: {
    height: "100%",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    flexDirection: "column",
    ...shorthands.gap(tokens.spacingVerticalS),
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground3,
  },
  filterBadge: {
    display: "flex",
    alignItems: "center",
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalXS, tokens.spacingHorizontalS),
    backgroundColor: tokens.colorBrandBackground2,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    marginBottom: tokens.spacingVerticalS,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IDrillThroughGridProps {
  /** PCF dataset from context */
  dataset: ComponentFramework.PropertyTypes.DataSet;

  /** Callback when record selection changes */
  onSelectionChange?: (recordIds: string[]) => void;

  /** Show loading state */
  isLoading?: boolean;
}

/**
 * Row data interface for DataGrid
 */
interface GridRow {
  recordId: string;
  [key: string]: string | number | boolean | Date | null;
}

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

/**
 * DrillThroughGrid - Fluent UI DataGrid for drill-through visualization.
 *
 * Integrates with FilterStateContext to:
 * - Display filtered dataset records
 * - Show active filter indicator
 * - Highlight rows matching filter criteria
 */
export const DrillThroughGrid: React.FC<IDrillThroughGridProps> = ({
  dataset,
  onSelectionChange,
  isLoading: externalLoading,
}) => {
  const styles = useStyles();
  const { isFiltered, activeFilter } = useFilterState();

  // Track selected record IDs
  const [selectedRecordIds, setSelectedRecordIds] = React.useState<string[]>(
    () => dataset.getSelectedRecordIds() || []
  );

  // Convert dataset to rows format
  const rows = React.useMemo<GridRow[]>(() => {
    if (!dataset.sortedRecordIds || !dataset.records) {
      return [];
    }

    return dataset.sortedRecordIds.map((recordId: string) => {
      const record = dataset.records[recordId];
      const row: GridRow = { recordId };

      // Add each column value
      if (dataset.columns) {
        dataset.columns.forEach(
          (column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
            const value = record.getFormattedValue(column.name);
            row[column.name] = value || "";
          }
        );
      }

      return row;
    });
  }, [dataset.sortedRecordIds, dataset.records, dataset.columns]);

  // Define columns for DataGrid
  const columns = React.useMemo<TableColumnDefinition<GridRow>[]>(() => {
    if (!dataset.columns || dataset.columns.length === 0) {
      return [];
    }

    return dataset.columns.map(
      (column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
        const isFilterColumn = activeFilter?.field === column.name;

        return createTableColumn<GridRow>({
          columnId: column.name as TableColumnId,
          compare: (a: GridRow, b: GridRow) => {
            const aVal = a[column.name]?.toString() || "";
            const bVal = b[column.name]?.toString() || "";
            return aVal.localeCompare(bVal);
          },
          renderHeaderCell: () => {
            // Highlight the column header if it's the filter column
            return (
              <span
                style={{
                  fontWeight: isFilterColumn ? 600 : 400,
                  color: isFilterColumn
                    ? tokens.colorBrandForeground1
                    : undefined,
                }}
              >
                {column.displayName}
                {isFilterColumn && " ●"}
              </span>
            );
          },
          renderCell: (item: GridRow) => {
            return (
              <TableCellLayout>
                {item[column.name]?.toString() || ""}
              </TableCellLayout>
            );
          },
        });
      }
    );
  }, [dataset.columns, activeFilter?.field]);

  // Handle selection change
  const handleSelectionChange = React.useCallback(
    (
      _e: React.MouseEvent | React.KeyboardEvent,
      data: { selectedItems: Set<unknown> }
    ) => {
      const newSelection = Array.from(data.selectedItems) as string[];
      setSelectedRecordIds(newSelection);

      // Sync with PCF dataset
      dataset.setSelectedRecordIds(newSelection);

      // Notify parent
      onSelectionChange?.(newSelection);
    },
    [dataset, onSelectionChange]
  );

  // Sync selection when dataset changes externally
  React.useEffect(() => {
    const contextSelection = dataset.getSelectedRecordIds() || [];
    if (JSON.stringify(contextSelection) !== JSON.stringify(selectedRecordIds)) {
      setSelectedRecordIds(contextSelection);
    }
  }, [dataset, selectedRecordIds]);

  // Loading state
  const isLoading = externalLoading || dataset.loading;

  if (isLoading) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="medium" label="Loading data..." />
      </div>
    );
  }

  // Empty state - no columns yet
  if (!dataset.columns || dataset.columns.length === 0) {
    return (
      <div className={styles.loadingContainer}>
        <Spinner size="small" label="Loading columns..." />
      </div>
    );
  }

  // Empty state - no records
  if (rows.length === 0) {
    return (
      <div className={styles.emptyContainer}>
        <Text size={400}>No records found</Text>
        {isFiltered && (
          <Text size={300}>
            Try adjusting your filter or click on a different chart segment
          </Text>
        )}
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {/* Active filter indicator */}
      {isFiltered && activeFilter && (
        <div className={styles.filterBadge}>
          <Text size={200} weight="medium">
            Filtered by: {activeFilter.field}
          </Text>
          {activeFilter.label && (
            <Text size={200}>= {activeFilter.label}</Text>
          )}
        </div>
      )}

      {/* DataGrid */}
      <DataGrid
        items={rows}
        columns={columns}
        sortable
        selectionMode="multiselect"
        selectedItems={new Set(selectedRecordIds)}
        onSelectionChange={handleSelectionChange}
        getRowId={(item) => item.recordId}
        focusMode="composite"
        aria-label="Drill-through data grid"
        size="medium"
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => (
              <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
            )}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<GridRow>>
          {({ item, rowId }) => (
            <DataGridRow<GridRow> key={rowId}>
              {({ renderCell }) => (
                <DataGridCell>{renderCell(item)}</DataGridCell>
              )}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    </div>
  );
};

export default DrillThroughGrid;
