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
    TableColumnId,
    tokens,
    makeStyles,
    Button,
    Tooltip,
} from '@fluentui/react-components';
import { FilterDismiss20Regular } from '@fluentui/react-icons';
import { HyperlinkCell } from './HyperlinkCell';
import { ColumnFilter, useColumnFilters } from './ColumnFilter';
import { FilterValue } from './FilterPopup';
import {
    OptimisticRowUpdateRequest,
    OptimisticUpdateResult,
    RowFieldUpdate
} from '../types';
import { logger } from '../utils/logger';

/**
 * Power Apps Native Grid Styling
 *
 * These styles match the native Power Apps/Dataverse grid appearance exactly:
 * - Row height: 44px (matching native grid compact view)
 * - Header: Semi-bold text with subtle gray background
 * - Hover: Subtle background highlight
 * - Selection: Branded selection color
 * - Cell padding: 8px horizontal for compact appearance
 * - Font: Segoe UI, base 300 size (14px)
 *
 * All colors use Fluent UI v9 tokens for dark mode support (ADR-021).
 */
const useStyles = makeStyles({
    // Grid container with Power Apps-like appearance
    gridRoot: {
        // Override DataGrid default styles to match Power Apps
        '& [role="grid"]': {
            // Row styling
            '& [role="row"]': {
                // Default row height to match Power Apps
                minHeight: '44px',
                height: '44px',
            },
            // Header row styling
            '& [role="row"]:first-child, & thead [role="row"]': {
                backgroundColor: tokens.colorNeutralBackground2,
                borderBottomWidth: '2px',
                borderBottomStyle: 'solid',
                borderBottomColor: tokens.colorNeutralStroke1,
            },
            // Header cell styling
            '& [role="columnheader"]': {
                fontWeight: tokens.fontWeightSemibold,
                fontSize: tokens.fontSizeBase300,
                color: tokens.colorNeutralForeground1,
                paddingTop: tokens.spacingVerticalS,
                paddingBottom: tokens.spacingVerticalS,
                paddingLeft: tokens.spacingHorizontalS,
                paddingRight: tokens.spacingHorizontalS,
            },
            // Body row styling
            '& tbody [role="row"], & [role="rowgroup"] [role="row"]': {
                borderBottomWidth: '1px',
                borderBottomStyle: 'solid',
                borderBottomColor: tokens.colorNeutralStroke2,
                backgroundColor: tokens.colorNeutralBackground1,
                // Hover state - subtle highlight
                ':hover': {
                    backgroundColor: tokens.colorNeutralBackground1Hover,
                },
            },
            // Selected row styling
            '& [role="row"][aria-selected="true"]': {
                backgroundColor: tokens.colorNeutralBackground1Selected,
                ':hover': {
                    backgroundColor: tokens.colorNeutralBackground1Selected,
                },
            },
            // Cell styling
            '& [role="gridcell"]': {
                fontSize: tokens.fontSizeBase300,
                color: tokens.colorNeutralForeground1,
                paddingTop: tokens.spacingVerticalS,
                paddingBottom: tokens.spacingVerticalS,
                paddingLeft: tokens.spacingHorizontalS,
                paddingRight: tokens.spacingHorizontalS,
                // Text overflow handling
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
            },
            // Selection cell (checkbox column) - compact
            '& [role="gridcell"]:first-child': {
                paddingLeft: tokens.spacingHorizontalM,
            },
        },
    },
    // Header cell content with filter icon
    headerCellContent: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        width: '100%',
    },
    headerText: {
        flex: 1,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        fontWeight: tokens.fontWeightSemibold,
    },
    // Filter toolbar at top of grid
    filterToolbar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-end',
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke1,
        backgroundColor: tokens.colorNeutralBackground2,
        gap: tokens.spacingHorizontalS,
    },
    filterCount: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
    },
});

interface DatasetGridProps {
    /** PCF dataset from context */
    dataset: ComponentFramework.PropertyTypes.DataSet;

    /** Selected record IDs */
    selectedRecordIds: string[];

    /** Selection change callback */
    onSelectionChange: (recordIds: string[]) => void;

    /**
     * Column name to render as hyperlink that opens the side pane.
     * Default: 'sprk_eventname'
     */
    hyperlinkColumn?: string;

    /**
     * Column name containing the event type lookup ID.
     * Used when opening the side pane.
     * Default: 'sprk_eventtype'
     */
    eventTypeColumn?: string;

    /**
     * Callback when side pane is opened for a record.
     * Useful for syncing selection or other UI updates.
     */
    onSidePaneOpened?: (recordId: string) => void;

    /**
     * Enable checkbox selection column (Task 014)
     * When true, shows a checkbox column as the first column for bulk selection.
     * Header checkbox enables select all/deselect all.
     * @default true
     */
    enableCheckboxSelection?: boolean;

    /**
     * Callback when a row is clicked (not checkbox). (Task 012)
     * Emits the due date of the clicked row for calendar highlighting.
     * @param date - ISO date string (YYYY-MM-DD) or null if no date
     */
    onRowClick?: (date: string | null) => void;

    /**
     * Column name for the due date field. (Task 012)
     * Used to extract the date value when a row is clicked.
     * @default 'sprk_duedate'
     */
    dueDateColumn?: string;

    /**
     * Register callback for optimistic row updates (Task 015).
     * Called during component initialization to provide the update function.
     * @param updateFn - Function to call when a row should be updated
     */
    onRegisterOptimisticUpdate?: (
        updateFn: (request: OptimisticRowUpdateRequest) => OptimisticUpdateResult
    ) => void;

    /**
     * Enable column filters (Task 016).
     * When true, shows filter icons in column headers that open filter popups.
     * Multiple column filters combine with AND logic.
     * @default true
     */
    enableColumnFilters?: boolean;

    /**
     * Callback when column filters change (Task 016).
     * Receives the current filter state as a map of column name to filter value.
     */
    onFiltersChange?: (filters: Map<string, FilterValue>) => void;
}

/**
 * Row data interface for DataGrid.
 * Includes both formatted values and raw lookup IDs for special columns.
 */
interface GridRow {
    recordId: string;
    /** Raw lookup IDs for lookup columns (keyed by column name + '_id') */
    _lookupIds: Record<string, string | undefined>;
    /** Raw date values for date columns (keyed by column name) - Task 012 */
    _rawDates: Record<string, Date | null>;
    [key: string]: string | number | boolean | Date | null | Record<string, string | undefined> | Record<string, Date | null>;
}

/**
 * Fluent UI DataGrid component for PCF dataset.
 *
 * Displays dataset records in a fully accessible, keyboard-navigable grid
 * using Fluent UI v9 components.
 */
/** Default column names for Event records */
const DEFAULT_HYPERLINK_COLUMN = 'sprk_eventname';
const DEFAULT_EVENT_TYPE_COLUMN = 'sprk_eventtype';

/** Default column name for due date (Task 012) */
const DEFAULT_DUE_DATE_COLUMN = 'sprk_duedate';

/**
 * Type for optimistic field overrides.
 * Map of recordId -> Map of fieldName -> formattedValue
 */
type OptimisticOverrides = Map<string, Map<string, string>>;

export const DatasetGrid: React.FC<DatasetGridProps> = ({
    dataset,
    selectedRecordIds,
    onSelectionChange,
    hyperlinkColumn = DEFAULT_HYPERLINK_COLUMN,
    eventTypeColumn = DEFAULT_EVENT_TYPE_COLUMN,
    onSidePaneOpened,
    enableCheckboxSelection = true,
    onRowClick,
    dueDateColumn = DEFAULT_DUE_DATE_COLUMN,
    onRegisterOptimisticUpdate,
    enableColumnFilters = true,
    onFiltersChange,
}) => {
    const styles = useStyles();

    // Column filters state and handlers (Task 016)
    const {
        filters,
        setFilter,
        clearAllFilters,
        hasActiveFilters,
        activeFilterCount,
    } = useColumnFilters(dataset);

    // Notify parent when filters change (Task 016)
    React.useEffect(() => {
        if (onFiltersChange) {
            onFiltersChange(filters);
        }
    }, [filters, onFiltersChange]);

    // State for optimistic field overrides (Task 015)
    // Stores pending updates that haven't been confirmed by the server yet
    const [optimisticOverrides, setOptimisticOverrides] = React.useState<OptimisticOverrides>(
        () => new Map()
    );

    /**
     * Apply optimistic update to a single row (Task 015).
     * Updates local state immediately without waiting for server confirmation.
     * Returns rollback function for error recovery.
     */
    const handleOptimisticUpdate = React.useCallback(
        (request: OptimisticRowUpdateRequest): OptimisticUpdateResult => {
            logger.info('DatasetGrid', `Optimistic update for record ${request.recordId}`, {
                fieldCount: request.updates.length,
                fields: request.updates.map(u => u.fieldName)
            });

            // Validate request
            if (!request.recordId || !request.updates || request.updates.length === 0) {
                return {
                    success: false,
                    error: 'Invalid update request: recordId and updates are required',
                    rollback: () => {}
                };
            }

            // Check if the record exists in the current dataset
            const recordExists = dataset.sortedRecordIds?.includes(request.recordId);
            if (!recordExists) {
                return {
                    success: false,
                    error: `Record ${request.recordId} not found in current dataset`,
                    rollback: () => {}
                };
            }

            // Capture previous values for rollback
            const previousValues: RowFieldUpdate[] = [];
            const existingOverrides = optimisticOverrides.get(request.recordId);

            // Get current displayed values (either from overrides or from dataset)
            for (const update of request.updates) {
                let currentValue = '';

                // Check if there's already an override
                if (existingOverrides?.has(update.fieldName)) {
                    currentValue = existingOverrides.get(update.fieldName) || '';
                } else {
                    // Get from dataset
                    const record = dataset.records[request.recordId];
                    if (record) {
                        currentValue = record.getFormattedValue(update.fieldName) || '';
                    }
                }

                previousValues.push({
                    fieldName: update.fieldName,
                    formattedValue: currentValue
                });
            }

            // Apply the optimistic update
            setOptimisticOverrides(prev => {
                const newOverrides = new Map(prev);
                let recordOverrides = newOverrides.get(request.recordId);

                if (!recordOverrides) {
                    recordOverrides = new Map();
                    newOverrides.set(request.recordId, recordOverrides);
                }

                for (const update of request.updates) {
                    recordOverrides.set(update.fieldName, update.formattedValue);
                }

                return newOverrides;
            });

            // Create rollback function
            const rollback = () => {
                logger.info('DatasetGrid', `Rolling back optimistic update for record ${request.recordId}`);

                setOptimisticOverrides(prev => {
                    const newOverrides = new Map(prev);
                    const recordOverrides = newOverrides.get(request.recordId);

                    if (recordOverrides) {
                        // Restore previous values
                        for (const prevValue of previousValues) {
                            if (prevValue.formattedValue === '') {
                                // Remove the override entirely if the original was empty
                                recordOverrides.delete(prevValue.fieldName);
                            } else {
                                recordOverrides.set(prevValue.fieldName, prevValue.formattedValue);
                            }
                        }

                        // Clean up empty record entry
                        if (recordOverrides.size === 0) {
                            newOverrides.delete(request.recordId);
                        }
                    }

                    return newOverrides;
                });
            };

            return {
                success: true,
                rollback
            };
        },
        [dataset.sortedRecordIds, dataset.records, optimisticOverrides]
    );

    // Register the optimistic update handler with parent component (Task 015)
    React.useEffect(() => {
        if (onRegisterOptimisticUpdate) {
            onRegisterOptimisticUpdate(handleOptimisticUpdate);
        }
    }, [onRegisterOptimisticUpdate, handleOptimisticUpdate]);

    // Clear optimistic overrides when dataset refreshes (records change significantly)
    // This handles the case where the server data is reloaded
    const prevRecordIdsRef = React.useRef<string[]>([]);
    React.useEffect(() => {
        const currentIds = dataset.sortedRecordIds || [];
        const prevIds = prevRecordIdsRef.current;

        // Detect if the dataset was refreshed (significant change in record IDs)
        const recordsChanged = currentIds.length !== prevIds.length ||
            !currentIds.every((id, index) => id === prevIds[index]);

        if (recordsChanged && optimisticOverrides.size > 0) {
            logger.debug('DatasetGrid', 'Dataset refreshed - clearing optimistic overrides');
            setOptimisticOverrides(new Map());
        }

        prevRecordIdsRef.current = currentIds;
    }, [dataset.sortedRecordIds, optimisticOverrides.size]);

    // Convert dataset to rows format with optimistic overrides applied (Task 015)
    const rows = React.useMemo<GridRow[]>(() => {
        if (!dataset.sortedRecordIds || !dataset.records) {
            return [];
        }

        return dataset.sortedRecordIds.map((recordId: string) => {
            const record = dataset.records[recordId];
            const row: GridRow = { recordId, _lookupIds: {}, _rawDates: {} };

            // Get any optimistic overrides for this record
            const recordOverrides = optimisticOverrides.get(recordId);

            // Add each column value
            if (dataset.columns) {
                dataset.columns.forEach((column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
                    // Check for optimistic override first (Task 015)
                    if (recordOverrides?.has(column.name)) {
                        row[column.name] = recordOverrides.get(column.name) || '';
                    } else {
                        const value = record.getFormattedValue(column.name);
                        row[column.name] = value || '';
                    }

                    // For lookup columns, also capture the raw ID
                    // The dataset API provides getValue() which returns EntityReference for lookups
                    if (column.dataType === 'Lookup.Simple' || column.dataType === 'Lookup.Customer' || column.dataType === 'Lookup.Owner') {
                        try {
                            const rawValue = record.getValue(column.name);
                            if (rawValue && typeof rawValue === 'object' && 'id' in rawValue) {
                                const entityRef = rawValue as { id: { guid: string } | string };
                                // EntityReference has id property that can be string or object with guid
                                const lookupId = typeof entityRef.id === 'string'
                                    ? entityRef.id
                                    : (entityRef.id as { guid: string })?.guid;
                                row._lookupIds[column.name] = lookupId;
                            }
                        } catch {
                            // getValue may not be available for all columns, ignore errors
                        }
                    }

                    // For date columns, capture the raw Date value for row click handling (Task 012)
                    if (column.dataType === 'DateAndTime.DateOnly' || column.dataType === 'DateAndTime.DateAndTime') {
                        try {
                            const rawValue = record.getValue(column.name);
                            if (rawValue instanceof Date) {
                                row._rawDates[column.name] = rawValue;
                            } else {
                                row._rawDates[column.name] = null;
                            }
                        } catch {
                            row._rawDates[column.name] = null;
                        }
                    }
                });
            }

            return row;
        });
    }, [dataset.sortedRecordIds, dataset.records, dataset.columns, optimisticOverrides]);

    /**
     * Render header cell with optional filter icon (Task 016).
     * Filter icon appears next to column name and opens filter popup on click.
     */
    const renderHeaderWithFilter = React.useCallback(
        (column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
            if (!enableColumnFilters) {
                return column.displayName;
            }

            const filterValue = filters.get(column.name) || null;

            return (
                <div className={styles.headerCellContent}>
                    <span className={styles.headerText}>{column.displayName}</span>
                    <ColumnFilter
                        column={column}
                        filterValue={filterValue}
                        onFilterChange={setFilter}
                        dataset={dataset}
                    />
                </div>
            );
        },
        [enableColumnFilters, filters, setFilter, dataset, styles.headerCellContent, styles.headerText]
    );

    // Define columns for DataGrid
    const columns = React.useMemo<TableColumnDefinition<GridRow>[]>(() => {
        if (!dataset.columns || dataset.columns.length === 0) {
            return [];
        }

        return dataset.columns.map((column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
            // Special renderer for SharePoint URL column (sprk_filepath)
            if (column.name === 'sprk_filepath') {
                return createTableColumn<GridRow>({
                    columnId: column.name as TableColumnId,
                    compare: (a: GridRow, b: GridRow) => {
                        const aVal = a[column.name]?.toString() || '';
                        const bVal = b[column.name]?.toString() || '';
                        return aVal.localeCompare(bVal);
                    },
                    renderHeaderCell: () => renderHeaderWithFilter(column),
                    renderCell: (item: GridRow) => {
                        const url = item[column.name]?.toString();

                        // If no URL, show empty state
                        if (!url) {
                            return <TableCellLayout>-</TableCellLayout>;
                        }

                        // Render as clickable link
                        return (
                            <TableCellLayout>
                                <a
                                    href={url}
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    style={{
                                        color: tokens.colorBrandForeground1,
                                        textDecoration: 'none'
                                    }}
                                    onClick={(e) => e.stopPropagation()} // Prevent row selection
                                >
                                    Open in SharePoint
                                </a>
                            </TableCellLayout>
                        );
                    }
                });
            }

            // Special renderer for hyperlink column (opens side pane)
            // Per Task 013: Event Name column opens EventDetailSidePane
            if (column.name === hyperlinkColumn) {
                return createTableColumn<GridRow>({
                    columnId: column.name as TableColumnId,
                    compare: (a: GridRow, b: GridRow) => {
                        const aVal = a[column.name]?.toString() || '';
                        const bVal = b[column.name]?.toString() || '';
                        return aVal.localeCompare(bVal);
                    },
                    renderHeaderCell: () => renderHeaderWithFilter(column),
                    renderCell: (item: GridRow) => {
                        const displayText = item[column.name]?.toString() || '';
                        const eventTypeId = item._lookupIds[eventTypeColumn];

                        return (
                            <TableCellLayout>
                                <HyperlinkCell
                                    displayText={displayText}
                                    recordId={item.recordId}
                                    eventType={eventTypeId}
                                    onSidePaneOpened={onSidePaneOpened}
                                />
                            </TableCellLayout>
                        );
                    }
                });
            }

            // Default renderer for all other columns
            return createTableColumn<GridRow>({
                columnId: column.name as TableColumnId,
                compare: (a: GridRow, b: GridRow) => {
                    const aVal = a[column.name]?.toString() || '';
                    const bVal = b[column.name]?.toString() || '';
                    return aVal.localeCompare(bVal);
                },
                renderHeaderCell: () => renderHeaderWithFilter(column),
                renderCell: (item: GridRow) => {
                    return (
                        <TableCellLayout>
                            {item[column.name]?.toString() || ''}
                        </TableCellLayout>
                    );
                }
            });
        });
    }, [dataset.columns, hyperlinkColumn, eventTypeColumn, onSidePaneOpened, renderHeaderWithFilter]);


    // Handle selection change
    const handleSelectionChange = React.useCallback(
        (e: React.MouseEvent | React.KeyboardEvent, data: { selectedItems: Set<unknown> }) => {
            const newSelection = Array.from(data.selectedItems) as string[];
            onSelectionChange(newSelection);
        },
        [onSelectionChange]
    );

    /**
     * Handle row click for bi-directional calendar sync (Task 012).
     * Extracts the due date from the clicked row and emits it as ISO string.
     * Only fires on row click, not checkbox click.
     */
    const handleRowClick = React.useCallback(
        (item: GridRow, e: React.MouseEvent) => {
            // Check if the click was on a checkbox cell by checking the target
            const target = e.target as HTMLElement;
            const isCheckboxClick =
                target.closest('[role="checkbox"]') !== null ||
                target.tagName === 'INPUT' ||
                target.closest('.fui-DataGridSelectionCell') !== null;

            // Skip if this was a checkbox click
            if (isCheckboxClick) {
                return;
            }

            // Skip if no callback provided
            if (!onRowClick) {
                return;
            }

            // Extract the raw date value from the row
            const rawDate = item._rawDates[dueDateColumn];

            if (rawDate instanceof Date && !isNaN(rawDate.getTime())) {
                // Format as ISO date string (YYYY-MM-DD)
                const isoDate = rawDate.toISOString().split('T')[0];
                onRowClick(isoDate);
            } else {
                // No valid date, emit null
                onRowClick(null);
            }
        },
        [onRowClick, dueDateColumn]
    );

    // Show loading state if columns not ready
    if (!dataset.columns || dataset.columns.length === 0) {
        return (
            <div
                style={{
                    height: '100%',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    background: tokens.colorNeutralBackground1
                }}
            >
                <p>Loading columns...</p>
            </div>
        );
    }

    return (
        <div
            style={{
                height: '100%',
                width: '100%',
                display: 'flex',
                flexDirection: 'column',
                background: tokens.colorNeutralBackground1
            }}
        >
            {/* Active filters toolbar (Task 016) */}
            {enableColumnFilters && hasActiveFilters && (
                <div className={styles.filterToolbar}>
                    <span className={styles.filterCount}>
                        {activeFilterCount} filter{activeFilterCount !== 1 ? 's' : ''} active
                    </span>
                    <Tooltip content="Clear all filters" relationship="label">
                        <Button
                            appearance="subtle"
                            size="small"
                            icon={<FilterDismiss20Regular />}
                            onClick={clearAllFilters}
                            aria-label="Clear all filters"
                        >
                            Clear all
                        </Button>
                    </Tooltip>
                </div>
            )}

            {/* Grid container with scrolling - Power Apps native styling */}
            <div className={styles.gridRoot} style={{ flex: 1, overflow: 'auto' }}>
                <DataGrid
                    items={rows}
                    columns={columns}
                    sortable
                    selectionMode={enableCheckboxSelection ? "multiselect" : "single"}
                    selectedItems={new Set(selectedRecordIds)}
                    onSelectionChange={handleSelectionChange}
                    getRowId={(item) => item.recordId}
                    focusMode="composite"
                    aria-label="Dataset grid"
                    size="small"
                >
                    <DataGridHeader>
                        <DataGridRow
                            selectionCell={
                                enableCheckboxSelection ? {
                                    checkboxIndicator: { "aria-label": "Select all rows" }
                                } : undefined
                            }
                        >
                            {({ renderHeaderCell }) => (
                                <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>
                            )}
                        </DataGridRow>
                    </DataGridHeader>
                    <DataGridBody<GridRow>>
                        {({ item, rowId }) => (
                            <DataGridRow<GridRow>
                                key={rowId}
                                onClick={(e: React.MouseEvent) => handleRowClick(item, e)}
                                style={{ cursor: onRowClick ? 'pointer' : undefined }}
                                selectionCell={
                                    enableCheckboxSelection ? {
                                        checkboxIndicator: { "aria-label": "Select row" }
                                    } : undefined
                                }
                            >
                                {({ renderCell }) => (
                                    <DataGridCell>{renderCell(item)}</DataGridCell>
                                )}
                            </DataGridRow>
                        )}
                    </DataGridBody>
                </DataGrid>
            </div>
        </div>
    );
};
