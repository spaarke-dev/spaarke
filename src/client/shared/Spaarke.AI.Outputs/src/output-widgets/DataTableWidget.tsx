/**
 * DataTableWidget
 *
 * Renders a sortable, filterable table of structured data rows using Fluent
 * v9 DataGrid with client-side sorting and a Search Input filter at the top.
 *
 * Sorting is managed with React useState and the DataGrid's built-in
 * sortState/onSortChange props. Filtering is a simple substring match
 * across all column values.
 *
 * NOT PCF-safe — requires React 19 and Fluent UI v9.
 *
 * Data shape injected via the AI streaming response (already parsed by the
 * calling code page). No direct API calls inside this widget.
 *
 * @see ADR-021 — Fluent UI v9 design system (no hard-coded colors)
 * @see ADR-012 — Shared component library
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Input,
  Spinner,
  DataGrid,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  TableColumnId,
  TableRowId,
  createTableColumn,
  SortDirection,
} from '@fluentui/react-components';
import { SearchRegular } from '@fluentui/react-icons';
import type { OutputWidgetProps } from '../types';

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

export interface DataTableColumn {
  /** Unique key matching the property name in each row record. */
  key: string;
  /** Human-readable column header label. */
  label: string;
  /** When true this column header is clickable for sort. Defaults to false. */
  sortable?: boolean;
}

export type DataTableRowValue = string | number | boolean;

export interface DataTableData {
  /** Column definitions for the table. */
  columns: DataTableColumn[];
  /** Data rows — each row is a Record keyed by column key. */
  rows: Array<Record<string, DataTableRowValue>>;
}

export type DataTableWidgetProps = OutputWidgetProps<DataTableData>;

// ---------------------------------------------------------------------------
// Internal — row items with stable IDs for DataGrid
// ---------------------------------------------------------------------------

/** Internal row wrapper that adds a stable numeric ID so getRowId works. */
interface IndexedRow {
  /** The original row data record. */
  data: Record<string, DataTableRowValue>;
  /** Stable zero-based index from the source data.rows array. */
  originalIndex: number;
}

// ---------------------------------------------------------------------------
// Internal sort state (mirrors Fluent v9 SortState shape)
// ---------------------------------------------------------------------------

interface ControlledSortState {
  sortColumn: TableColumnId | undefined;
  sortDirection: SortDirection;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    height: '100%',
    overflow: 'hidden',
  },
  searchBar: {
    display: 'flex',
    alignItems: 'center',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    flexShrink: 0,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  searchInput: {
    width: '100%',
    maxWidth: '320px',
  },
  tableContainer: {
    overflowY: 'auto',
    flexGrow: 1,
    padding: `0 ${tokens.spacingHorizontalL} ${tokens.spacingVerticalM}`,
  },
  noRows: {
    color: tokens.colorNeutralForeground3,
    padding: `${tokens.spacingVerticalM} 0`,
  },
  errorText: {
    color: tokens.colorStatusDangerForeground1,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
  },
  cellText: {
    color: tokens.colorNeutralForeground1,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Convert any cell value to a display string. */
function cellToString(value: DataTableRowValue | undefined): string {
  if (value === undefined || value === null) return '';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  return String(value);
}

/** Compare two raw cell values for sort (numeric if both are numbers, else string). */
function compareCellValues(a: DataTableRowValue | undefined, b: DataTableRowValue | undefined): number {
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  const sa = cellToString(a);
  const sb = cellToString(b);
  const na = Number(sa);
  const nb = Number(sb);
  if (!Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
  return sa.localeCompare(sb);
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * DataTableWidget renders a Fluent v9 DataGrid with client-side column
 * sorting (clicking sortable column headers toggles asc/desc/off) and text
 * filtering via a Search Input at the top. All data lives in props — no API
 * calls inside this component.
 */
export default function DataTableWidget({
  data,
  isLoading,
  error,
  className,
}: DataTableWidgetProps): React.ReactElement {
  const styles = useStyles();
  const [filterText, setFilterText] = React.useState('');
  const [sortState, setSortState] = React.useState<ControlledSortState>({
    sortColumn: undefined,
    sortDirection: 'ascending',
  });

  // ---- Wrap source rows with stable indices --------------------------------

  const indexedRows = React.useMemo<IndexedRow[]>(
    () =>
      (data?.rows ?? []).map((row, idx) => ({
        data: row,
        originalIndex: idx,
      })),
    [data]
  );

  // ---- Filtered items ------------------------------------------------------

  const filteredItems = React.useMemo<IndexedRow[]>(() => {
    const query = filterText.trim().toLowerCase();
    if (!query) return indexedRows;
    return indexedRows.filter(item =>
      (data?.columns ?? []).some(col => cellToString(item.data[col.key]).toLowerCase().includes(query))
    );
  }, [indexedRows, filterText, data]);

  // ---- Sorted items --------------------------------------------------------

  const sortedItems = React.useMemo<IndexedRow[]>(() => {
    const { sortColumn, sortDirection } = sortState;
    if (!sortColumn) return filteredItems;
    const key = String(sortColumn);
    const multiplier = sortDirection === 'ascending' ? 1 : -1;
    return [...filteredItems].sort((a, b) => multiplier * compareCellValues(a.data[key], b.data[key]));
  }, [filteredItems, sortState]);

  // ---- Fluent v9 column definitions ----------------------------------------

  const columns = React.useMemo<TableColumnDefinition<IndexedRow>[]>(
    () =>
      (data?.columns ?? []).map(col =>
        createTableColumn<IndexedRow>({
          columnId: col.key,
          compare: col.sortable ? (a, b) => compareCellValues(a.data[col.key], b.data[col.key]) : undefined,
          renderHeaderCell: () => col.label,
          renderCell: item => cellToString(item.data[col.key]),
        })
      ),
    [data]
  );

  // ---- Row ID — use stable originalIndex ----------------------------------

  const getRowId = React.useCallback((item: IndexedRow): TableRowId => item.originalIndex, []);

  // ---- Render --------------------------------------------------------------

  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Spinner size="medium" label="Loading table..." />
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <Text className={styles.errorText}>{error}</Text>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      {/* Search filter */}
      <div className={styles.searchBar}>
        <Input
          className={styles.searchInput}
          contentBefore={<SearchRegular />}
          placeholder="Filter rows..."
          value={filterText}
          onChange={(_e, d) => setFilterText(d.value)}
          size="small"
          appearance="outline"
        />
      </div>

      {/* Table */}
      <div className={styles.tableContainer}>
        {sortedItems.length === 0 ? (
          <Text className={styles.noRows}>No matching rows.</Text>
        ) : (
          <DataGrid
            items={sortedItems}
            columns={columns}
            sortable
            sortState={sortState}
            onSortChange={(_e, next) => setSortState(next)}
            getRowId={getRowId}
          >
            <DataGridHeader>
              <DataGridRow>
                {({ renderHeaderCell, columnId }) => {
                  const col = data.columns.find(c => c.key === columnId);
                  const isSortable = col?.sortable ?? false;
                  const currentDir = sortState.sortColumn === columnId ? sortState.sortDirection : undefined;
                  return (
                    <DataGridHeaderCell key={String(columnId)} sortDirection={isSortable ? currentDir : undefined}>
                      {renderHeaderCell()}
                    </DataGridHeaderCell>
                  );
                }}
              </DataGridRow>
            </DataGridHeader>
            <DataGridBody<IndexedRow>>
              {row => (
                <DataGridRow key={row.rowId}>
                  {({ renderCell }) => (
                    <DataGridCell>
                      <Text size={200} className={styles.cellText}>
                        {renderCell(row.item)}
                      </Text>
                    </DataGridCell>
                  )}
                </DataGridRow>
              )}
            </DataGridBody>
          </DataGrid>
        )}
      </div>
    </div>
  );
}
