/**
 * SearchResultsGrid — Grid view for search results using Fluent UI v9 DataGrid
 *
 * Accepts standardized IDatasetColumn[] and IDatasetRecord[] types for display,
 * using dataType-based cell rendering (matching ColumnRendererService patterns
 * from @spaarke/ui-components shared library).
 *
 * Features:
 *   - dataType-driven cell rendering (Percentage, DateOnly, Currency, etc.)
 *   - Sortable columns (asc/desc)
 *   - Multi-row selection with checkboxes
 *   - Infinite scroll via IntersectionObserver
 *   - Loading overlay for initial load and load-more
 *   - 44px row height (standard Spaarke grid row)
 *
 * @see hooks/useSearchViewDefinitions.ts — column definitions (Dataverse or fallback)
 * @see adapters/searchResultAdapter.ts — IDatasetRecord mapping
 * @see ColumnRendererService.tsx (shared library) — reference renderer patterns
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
    Button,
    Menu,
    MenuTrigger,
    MenuPopover,
    MenuList,
    MenuItemCheckbox,
    makeStyles,
    tokens,
    type TableColumnDefinition,
    type DataGridProps,
    type TableRowId,
    type TableColumnSizingOptions,
} from "@fluentui/react-components";
import { ColumnTriple20Regular } from "@fluentui/react-icons";
import type { IDatasetColumn } from "../hooks/useSearchViewDefinitions";
import type { IDatasetRecord } from "../adapters/searchResultAdapter";
import type { SearchDomain } from "../types";

// =============================================
// Props
// =============================================

export interface SearchResultsGridProps {
    /** Mapped records to display (IDatasetRecord shape). */
    records: IDatasetRecord[];
    /** Total number of matching results (for status display). */
    totalCount: number;
    /** Whether initial search is in progress. */
    isLoading: boolean;
    /** Whether additional page is loading. */
    isLoadingMore: boolean;
    /** Whether more results are available. */
    hasMore: boolean;
    /** Active search domain tab (used for empty-state messaging). */
    activeDomain: SearchDomain;
    /** Column definitions from useSearchViewDefinitions (IDatasetColumn shape). */
    columns: IDatasetColumn[];
    /** Callback to load next page of results. */
    onLoadMore: () => void;
    /** Callback when row selection changes. */
    onSelectionChange: (selectedIds: string[]) => void;
    /** Callback when a column sort is requested. */
    onSort: (columnKey: string, direction: "asc" | "desc") => void;
}

// =============================================
// DataType-based cell rendering
// =============================================

/** Render a cell value based on the column's dataType. Matches ColumnRendererService patterns. */
function renderByDataType(value: unknown, dataType: string): string {
    if (value == null || value === "") return "";

    switch (dataType) {
        case "Percentage": {
            const num = typeof value === "number" ? value : Number(value);
            if (isNaN(num)) return String(value);
            return `${Math.round(num * 100)}%`;
        }
        case "DateAndTime.DateOnly": {
            if (typeof value !== "string" && typeof value !== "number") return String(value);
            try {
                return new Date(value as string | number).toLocaleDateString(undefined, {
                    year: "numeric",
                    month: "short",
                    day: "numeric",
                });
            } catch {
                return String(value);
            }
        }
        case "Currency": {
            const num = typeof value === "number" ? value : Number(value);
            if (isNaN(num)) return String(value);
            return new Intl.NumberFormat(undefined, {
                style: "currency",
                currency: "USD",
                minimumFractionDigits: 2,
            }).format(num);
        }
        case "StringArray": {
            if (Array.isArray(value)) return value.join(", ");
            return typeof value === "string" ? value : String(value);
        }
        case "FileType": {
            return typeof value === "string" ? value.toUpperCase() : String(value);
        }
        case "EntityLink": {
            if (typeof value === "object" && value !== null && "name" in value) {
                return String((value as { name: string }).name);
            }
            return String(value);
        }
        default: {
            // SingleLine.Text and all other types
            if (typeof value === "number") return value.toLocaleString();
            if (Array.isArray(value)) return value.join(", ");
            return String(value);
        }
    }
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
    gridToolbar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-end",
        paddingRight: tokens.spacingHorizontalM,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        backgroundColor: tokens.colorNeutralBackground2,
        minHeight: "32px",
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
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
    },
    headerCell: {
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        fontWeight: tokens.fontWeightSemibold,
    },
});

// =============================================
// Component
// =============================================

export const SearchResultsGrid: React.FC<SearchResultsGridProps> = ({
    records,
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

    // --- Column visibility state ---
    // All columns visible by default; key = column name
    const [hiddenColumns, setHiddenColumns] = useState<Set<string>>(new Set());

    // Suppress unused-var warning — totalCount reserved for future status display
    void totalCount;

    // Reset hidden columns when domain changes (columns change)
    useEffect(() => {
        setHiddenColumns(new Set());
    }, [activeDomain]);

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

    // --- Visible columns (filtered by user selection) ---
    const visibleColumns = useMemo(
        () => columns.filter((col) => !hiddenColumns.has(col.name)),
        [columns, hiddenColumns],
    );

    // --- Map IDatasetColumn to Fluent TableColumnDefinition ---
    type GridItem = IDatasetRecord & { _rowId: number };

    const tableColumns: TableColumnDefinition<GridItem>[] = useMemo(
        () =>
            visibleColumns.map((col) =>
                createTableColumn<GridItem>({
                    columnId: col.name,
                    compare: (a, b) => {
                        const aVal = a[col.name];
                        const bVal = b[col.name];
                        if (typeof aVal === "number" && typeof bVal === "number") {
                            return aVal - bVal;
                        }
                        return String(aVal ?? "").localeCompare(String(bVal ?? ""));
                    },
                    renderHeaderCell: () => col.displayName,
                    renderCell: (item) => {
                        const value = item[col.name];
                        return renderByDataType(value, col.dataType);
                    },
                })
            ),
        [visibleColumns],
    );

    // --- Column sizing options (from visualSizeFactor → pixel widths) ---
    const columnSizingOptions: TableColumnSizingOptions = useMemo(() => {
        const options: TableColumnSizingOptions = {};
        for (const col of visibleColumns) {
            const defaultWidth = col.visualSizeFactor
                ? Math.round(col.visualSizeFactor * 100)
                : 150;
            options[col.name] = {
                defaultWidth,
                minWidth: Math.max(80, Math.round(defaultWidth * 0.5)),
                idealWidth: defaultWidth,
            };
        }
        return options;
    }, [visibleColumns]);

    // --- Column picker: checked state for MenuItemCheckbox ---
    const columnCheckedValues = useMemo(() => {
        const visible = columns
            .filter((col) => !hiddenColumns.has(col.name))
            .map((col) => col.name);
        return { columns: visible };
    }, [columns, hiddenColumns]);

    const handleCheckedValueChange = useCallback(
        (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
            if (data.name !== "columns") return;
            // checkedItems = names of columns that are now checked (visible)
            const visibleSet = new Set(data.checkedItems);
            const newHidden = new Set<string>();
            for (const col of columns) {
                if (!visibleSet.has(col.name)) {
                    newHidden.add(col.name);
                }
            }
            setHiddenColumns(newHidden);
        },
        [columns],
    );

    // --- Selection handler ---
    const handleSelectionChange: DataGridProps["onSelectionChange"] = useCallback(
        (_event: unknown, data: { selectedItems: Set<TableRowId> }) => {
            setSelectedRows(data.selectedItems);
            const ids = Array.from(data.selectedItems)
                .map((rowId) => {
                    const record = records[rowId as number];
                    return record?.id ?? "";
                })
                .filter(Boolean);
            onSelectionChange(ids);
        },
        [records, onSelectionChange],
    );

    // --- Sort handler ---
    const handleSortChange = useCallback(
        (_event: React.MouseEvent, data: { sortColumn?: string | number; sortDirection: "ascending" | "descending" }) => {
            const colKey = String(data.sortColumn ?? "");
            const direction = data.sortDirection === "ascending" ? "asc" as const : "desc" as const;
            setSortState({ columnKey: colKey, direction });
            onSort(colKey, direction);
        },
        [onSort],
    );

    // --- Items with row IDs ---
    const items: GridItem[] = useMemo(
        () => records.map((r, i) => ({ ...r, _rowId: i })),
        [records],
    );

    // --- Empty state ---
    if (!isLoading && records.length === 0) {
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

            {/* Column picker toolbar */}
            <div className={styles.gridToolbar}>
                <Menu
                    checkedValues={columnCheckedValues}
                    onCheckedValueChange={handleCheckedValueChange}
                >
                    <MenuTrigger disableButtonEnhancement>
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<ColumnTriple20Regular />}
                            aria-label="Choose columns"
                        >
                            Columns
                        </Button>
                    </MenuTrigger>
                    <MenuPopover>
                        <MenuList>
                            {columns.map((col) => (
                                <MenuItemCheckbox
                                    key={col.name}
                                    name="columns"
                                    value={col.name}
                                >
                                    {col.displayName}
                                </MenuItemCheckbox>
                            ))}
                        </MenuList>
                    </MenuPopover>
                </Menu>
            </div>

            <div className={styles.gridContainer} ref={gridContainerRef}>
                <DataGrid
                    items={items}
                    columns={tableColumns}
                    selectionMode="multiselect"
                    selectedItems={selectedRows}
                    onSelectionChange={handleSelectionChange}
                    sortable
                    onSortChange={handleSortChange}
                    resizableColumns
                    columnSizingOptions={columnSizingOptions}
                    getRowId={(item: GridItem) => item._rowId}
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
                                <DataGridHeaderCell className={styles.headerCell}>
                                    {renderHeaderCell()}
                                </DataGridHeaderCell>
                            )}
                        </DataGridRow>
                    </DataGridHeader>
                    <DataGridBody<GridItem>>
                        {({ item, rowId }) => (
                            <DataGridRow<GridItem>
                                key={rowId}
                                selectionCell={{
                                    checkboxIndicator: { "aria-label": "Select row" },
                                }}
                                style={{ height: "44px" }}
                            >
                                {({ renderCell }) => (
                                    <DataGridCell className={styles.cell}>
                                        {renderCell(item)}
                                    </DataGridCell>
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
