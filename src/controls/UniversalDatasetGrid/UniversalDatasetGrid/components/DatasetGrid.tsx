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
    DataGridProps,
    TableColumnId
} from '@fluentui/react-table';
import { tokens } from '@fluentui/react-components';

interface DatasetGridProps {
    /** PCF dataset from context */
    dataset: ComponentFramework.PropertyTypes.DataSet;

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
        if (!dataset.sortedRecordIds || !dataset.records) {
            return [];
        }

        return dataset.sortedRecordIds.map((recordId: string) => {
            const record = dataset.records[recordId];
            const row: GridRow = { recordId };

            // Add each column value
            if (dataset.columns) {
                dataset.columns.forEach((column: ComponentFramework.PropertyHelper.DataSetApi.Column) => {
                    const value = record.getFormattedValue(column.name);
                    row[column.name] = value || '';
                });
            }

            return row;
        });
    }, [dataset.sortedRecordIds, dataset.records, dataset.columns]);

    // Define columns for DataGrid
    const columns = React.useMemo<TableColumnDefinition<GridRow>[]>(() => {
        if (!dataset.columns || dataset.columns.length === 0) {
            return [];
        }

        return dataset.columns.map((column: ComponentFramework.PropertyHelper.DataSetApi.Column) =>
            createTableColumn<GridRow>({
                columnId: column.name as TableColumnId,
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
        (e: React.MouseEvent | React.KeyboardEvent, data: { selectedItems: Set<unknown> }) => {
            const newSelection = Array.from(data.selectedItems) as string[];
            onSelectionChange(newSelection);
        },
        [onSelectionChange]
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
                onSelectionChange={handleSelectionChange}
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
