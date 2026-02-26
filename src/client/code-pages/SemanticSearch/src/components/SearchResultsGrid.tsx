/**
 * SearchResultsGrid — Grid view for search results using Fluent UI v9 DataGrid
 *
 * Uses Fluent UI DataGrid directly (not UniversalDatasetGrid from shared library)
 * because the shared library GridView requires IDatasetRecord/IDatasetColumn from
 * a separate package. Per spike task 003 findings, the inner GridView accepts
 * plain data arrays; we replicate that pattern here with Fluent UI primitives.
 *
 * Features:
 *   - Domain-specific column configurations (passed as props)
 *   - Sortable columns (asc/desc)
 *   - Multi-row selection with checkboxes
 *   - Infinite scroll via IntersectionObserver
 *   - Loading overlay for initial load and load-more
 *   - 44px row height (standard Spaarke grid row)
 *
 * @see notes/spikes/grid-headless-adapter.md — spike findings
 * @see GridView.tsx (shared library) — reference pattern
 */

import React, { useMemo, useCallback, useRef, useEffect, useState } from "react";
import {
    DataGrid,
    DataGridHeader,
    DataGridRow,
    DataGridHeaderCell,
    DataGridBody,
    DataGridCell,
    createTableColumn,
    Spinner,
    Text,
    Checkbox,
    makeStyles,
    tokens,
    type TableColumnDefinition,
    type DataGridProps,
    type TableRowId,
} from "@fluentui/react-components";
import type {
    SearchDomain,
    GridColumnDef,
    DocumentSearchResult,
    RecordSearchResult,
} from "../types";

// =============================================
// Props
// =============================================

export interface SearchResultsGridProps {
    /** Search results to display. */
    results: (DocumentSearchResult | RecordSearchResult)[];
    /** Total number of matching results (for status display). */
    totalCount: number;
    /** Whether initial search is in progress. */
    isLoading: boolean;
    /** Whether additional page is loading. */
    isLoadingMore: boolean;
    /** Whether more results are available. */
    hasMore: boolean;
    /** Active search domain tab. */
    activeDomain: SearchDomain;
    /** Column definitions for the active domain. */
    columns: GridColumnDef[];
    /** Callback to load next page of results. */
    onLoadMore: () => void;
    /** Callback when row selection changes. */
    onSelectionChange: (selectedIds: string[]) => void;
    /** Callback when a column sort is requested. */
    onSort: (columnKey: string, direction: "asc" | "desc") => void;
}

// =============================================
// Helpers
// =============================================

/** Extract unique ID from a search result. */
function getResultId(result: DocumentSearchResult | RecordSearchResult): string {
    if ("documentId" in result && result.documentId) {
        return result.documentId;
    }
    if ("recordId" in result) {
        return result.recordId;
    }
    return "";
}

/** Extract cell value from a result by column key. */
function getCellValue(
    result: DocumentSearchResult | RecordSearchResult,
    key: string
): unknown {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (result as Record<string, any>)[key];
}

/** Format a cell value for display. */
function formatCellValue(value: unknown): string {
    if (value == null) return "";
    if (typeof value === "number") return value.toFixed(2);
    if (typeof value === "string") return value;
    if (Array.isArray(value)) return value.join(", ");
    return String(value);
}

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        width: "100%",
        height: "100%",
        position: "relative",
        overflow: "hidden",
    },
    gridContainer: {
        flex: 1,
        overflow: "auto",
        position: "relative",
    },
    loadingOverlay: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        backgroundColor: tokens.colorNeutralBackground1,
        opacity: 0.85,
        zIndex: 10,
    },
    loadMoreContainer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: tokens.spacingVerticalM,
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        gap: tokens.spacingVerticalS,
        color: tokens.colorNeutralForeground3,
        padding: tokens.spacingVerticalXXL,
    },
    sentinel: {
        height: "1px",
        width: "100%",
    },
    cell: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
    },
});

// =============================================
// Component
// =============================================

export const SearchResultsGrid: React.FC<SearchResultsGridProps> = ({
    results,
    totalCount,
    isLoading,
    isLoadingMore,
    hasMore,
    activeDomain,
    columns,
    onLoadMore,
    onSelectionChange,
    onSort,
}) => {
    const styles = useStyles();
    const sentinelRef = useRef<HTMLDivElement>(null);
    const gridContainerRef = useRef<HTMLDivElement>(null);
    const [selectedRows, setSelectedRows] = useState<Set<TableRowId>>(new Set());
    const [sortState, setSortState] = useState<{
        columnKey: string;
        direction: "asc" | "desc";
    } | null>(null);

    // --- Infinite scroll via IntersectionObserver ---
    useEffect(() => {
        const sentinel = sentinelRef.current;
        if (!sentinel) return;

        const observer = new IntersectionObserver(
            (entries) => {
                const entry = entries[0];
                if (entry.isIntersecting && hasMore && !isLoadingMore && !isLoading) {
                    onLoadMore();
                }
            },
            {
                root: gridContainerRef.current,
                rootMargin: "200px",
                threshold: 0,
            }
        );

        observer.observe(sentinel);
        return () => observer.disconnect();
    }, [hasMore, isLoadingMore, isLoading, onLoadMore]);

    // --- Map GridColumnDef to Fluent TableColumnDefinition ---
    const tableColumns: TableColumnDefinition<DocumentSearchResult | RecordSearchResult>[] =
        useMemo(
            () =>
                columns.map((col) =>
                    createTableColumn<DocumentSearchResult | RecordSearchResult>({
                        columnId: col.key,
                        compare: col.sortable
                            ? (a, b) => {
                                  const aVal = getCellValue(a, col.key);
                                  const bVal = getCellValue(b, col.key);
                                  if (typeof aVal === "number" && typeof bVal === "number") {
                                      return aVal - bVal;
                                  }
                                  return String(aVal ?? "").localeCompare(String(bVal ?? ""));
                              }
                            : undefined,
                        renderHeaderCell: () => col.label,
                        renderCell: (item) => {
                            const value = getCellValue(item, col.key);
                            if (col.render) {
                                return col.render(value, item as unknown as Record<string, unknown>);
                            }
                            return formatCellValue(value);
                        },
                    })
                ),
            [columns]
        );

    // --- Selection handler ---
    const handleSelectionChange: DataGridProps["onSelectionChange"] = useCallback(
        (_event: unknown, data: { selectedItems: Set<TableRowId> }) => {
            setSelectedRows(data.selectedItems);
            const ids = Array.from(data.selectedItems).map((rowId) => {
                const result = results[rowId as number];
                return result ? getResultId(result) : "";
            }).filter(Boolean);
            onSelectionChange(ids);
        },
        [results, onSelectionChange]
    );

    // --- Sort handler ---
    const handleSortChange = useCallback(
        (_event: React.MouseEvent, data: { sortColumn?: string | number; sortDirection: "ascending" | "descending" }) => {
            const colKey = String(data.sortColumn ?? "");
            const direction = data.sortDirection === "ascending" ? "asc" as const : "desc" as const;
            setSortState({ columnKey: colKey, direction });
            onSort(colKey, direction);
        },
        [onSort]
    );

    // --- Items with row IDs ---
    const items = useMemo(
        () => results.map((r, i) => ({ ...r, _rowId: i })),
        [results]
    );

    // --- Empty state ---
    if (!isLoading && results.length === 0) {
        return (
            <div className={styles.root}>
                <div className={styles.emptyState}>
                    <Text size={400} weight="semibold">
                        No results found
                    </Text>
                    <Text size={200}>
                        Try adjusting your search query or filters for{" "}
                        {activeDomain}
                    </Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.root}>
            {/* Loading overlay for initial search */}
            {isLoading && (
                <div className={styles.loadingOverlay}>
                    <Spinner size="medium" label="Searching..." />
                </div>
            )}

            <div className={styles.gridContainer} ref={gridContainerRef}>
                <DataGrid
                    items={items}
                    columns={tableColumns}
                    selectionMode="multiselect"
                    selectedItems={selectedRows}
                    onSelectionChange={handleSelectionChange}
                    sortable
                    onSortChange={handleSortChange}
                    getRowId={(item: (DocumentSearchResult | RecordSearchResult) & { _rowId: number }) => item._rowId}
                    style={{ minWidth: "100%" }}
                    aria-label="Search results"
                    {...(sortState
                        ? {
                              defaultSortState: {
                                  sortColumn: sortState.columnKey,
                                  sortDirection:
                                      sortState.direction === "asc"
                                          ? "ascending"
                                          : "descending",
                              },
                          }
                        : {})}
                >
                    <DataGridHeader>
                        <DataGridRow
                            selectionCell={{
                                checkboxIndicator: { "aria-label": "Select all rows" },
                            }}
                        >
                            {({ renderHeaderCell }) => (
                                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                            )}
                        </DataGridRow>
                    </DataGridHeader>
                    <DataGridBody<(DocumentSearchResult | RecordSearchResult) & { _rowId: number }>>
                        {({ item, rowId }) => (
                            <DataGridRow<(DocumentSearchResult | RecordSearchResult) & { _rowId: number }>
                                key={rowId}
                                selectionCell={{
                                    checkboxIndicator: { "aria-label": "Select row" },
                                }}
                                style={{ height: "44px" }}
                            >
                                {({ renderCell }) => (
                                    <DataGridCell className={styles.cell}>{renderCell(item)}</DataGridCell>
                                )}
                            </DataGridRow>
                        )}
                    </DataGridBody>
                </DataGrid>

                {/* Load-more spinner */}
                {isLoadingMore && (
                    <div className={styles.loadMoreContainer}>
                        <Spinner size="small" label="Loading more results..." />
                    </div>
                )}

                {/* Infinite scroll sentinel */}
                <div ref={sentinelRef} className={styles.sentinel} />
            </div>
        </div>
    );
};

export default SearchResultsGrid;
