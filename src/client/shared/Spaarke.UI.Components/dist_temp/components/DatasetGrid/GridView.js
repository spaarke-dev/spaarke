/**
 * GridView - Table layout using Fluent UI DataGrid with infinite scroll
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { DataGrid, DataGridHeader, DataGridRow, DataGridHeaderCell, DataGridBody, DataGridCell, createTableColumn, makeStyles, tokens, Button, Spinner, } from '@fluentui/react-components';
import { ColumnRendererService } from '../../services/ColumnRendererService';
import { useVirtualization } from '../../hooks/useVirtualization';
import { VirtualizedGridView } from './VirtualizedGridView';
const useStyles = makeStyles({
    root: {
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        position: 'relative',
    },
    gridContainer: {
        flex: 1,
        overflow: 'auto',
        position: 'relative',
    },
    loadingOverlay: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: tokens.spacingVerticalL,
        backgroundColor: tokens.colorNeutralBackground1,
        borderTopWidth: '1px',
        borderTopStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke1,
    },
    loadMoreButton: {
        margin: tokens.spacingVerticalM,
        width: '100%',
    },
    emptyState: {
        padding: tokens.spacingVerticalXXL,
        textAlign: 'center',
        color: tokens.colorNeutralForeground3,
    },
});
export const GridView = props => {
    const styles = useStyles();
    const gridContainerRef = React.useRef(null);
    // Check if virtualization should be enabled
    const virtualization = useVirtualization(props.records.length, {
        enabled: props.enableVirtualization,
    });
    // Filter to only readable columns
    const readableColumns = React.useMemo(() => {
        return props.columns.filter(col => col.canRead !== false);
    }, [props.columns]);
    // Use custom virtualized grid for very large datasets (>1000 records)
    // For 100-1000 records, rely on Fluent DataGrid's built-in virtualization
    if (virtualization.shouldVirtualize && props.records.length > 1000) {
        return (React.createElement(VirtualizedGridView, { records: props.records, columns: readableColumns, selectedRecordIds: props.selectedRecordIds, itemHeight: virtualization.itemHeight, overscanCount: virtualization.overscanCount, onRecordClick: recordId => {
                const record = props.records.find(r => r.id === recordId);
                if (record)
                    props.onRecordClick(record);
            } }));
    }
    // Determine if infinite scroll should be active
    const isInfiniteScroll = React.useMemo(() => {
        if (props.scrollBehavior === 'Infinite')
            return true;
        if (props.scrollBehavior === 'Paged')
            return false;
        // Auto mode: infinite for >100 records, paged otherwise
        return props.records.length > 100;
    }, [props.scrollBehavior, props.records.length]);
    // Handle scroll for infinite scroll
    const handleScroll = React.useCallback((e) => {
        if (!isInfiniteScroll || !props.hasNextPage || props.loading) {
            return;
        }
        const container = e.currentTarget;
        const { scrollTop, scrollHeight, clientHeight } = container;
        // Calculate scroll percentage
        const scrollPercentage = (scrollTop + clientHeight) / scrollHeight;
        // Load more when 90% scrolled
        if (scrollPercentage > 0.9) {
            props.loadNextPage();
        }
    }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);
    // Convert IDatasetColumn to Fluent DataGrid columns with field security
    const gridColumns = React.useMemo(() => {
        return readableColumns.map(col => createTableColumn({
            columnId: col.name,
            compare: (a, b) => {
                const aVal = String(a[col.name] ?? '');
                const bVal = String(b[col.name] ?? '');
                return aVal.localeCompare(bVal);
            },
            renderHeaderCell: () => col.displayName,
            renderCell: item => {
                const renderer = ColumnRendererService.getRenderer(col);
                return renderer(item[col.name], item, col);
            },
        }));
    }, [readableColumns]);
    // Handle row selection
    const handleSelectionChange = React.useCallback((_e, data) => {
        const selectedItems = data.selectedItems;
        props.onSelectionChange(Array.from(selectedItems));
    }, [props]);
    // Handle row click
    const handleRowClick = React.useCallback((record) => {
        props.onRecordClick(record);
    }, [props]);
    // Empty state
    if (props.records.length === 0 && !props.loading) {
        return (React.createElement("div", { className: styles.emptyState },
            React.createElement("p", null, "No records to display")));
    }
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.gridContainer, ref: gridContainerRef, onScroll: handleScroll },
            React.createElement(DataGrid, { items: props.records, columns: gridColumns, sortable: true, resizableColumns: true, selectionMode: props.selectedRecordIds.length > 0 ? 'multiselect' : undefined, selectedItems: new Set(props.selectedRecordIds), onSelectionChange: handleSelectionChange, focusMode: "composite" },
                React.createElement(DataGridHeader, null,
                    React.createElement(DataGridRow, null, ({ renderHeaderCell }) => React.createElement(DataGridHeaderCell, null, renderHeaderCell()))),
                React.createElement(DataGridBody, null, ({ item, rowId }) => (React.createElement(DataGridRow, { key: rowId, onClick: () => handleRowClick(item), style: { cursor: 'pointer' } }, ({ renderCell }) => React.createElement(DataGridCell, null, renderCell(item))))))),
        isInfiniteScroll && props.loading && (React.createElement("div", { className: styles.loadingOverlay },
            React.createElement(Spinner, { size: "small", label: "Loading more records..." }))),
        !isInfiniteScroll && props.hasNextPage && !props.loading && (React.createElement(Button, { appearance: "subtle", className: styles.loadMoreButton, onClick: props.loadNextPage },
            "Load More (",
            props.records.length,
            " records loaded)")),
        !isInfiniteScroll && props.loading && (React.createElement("div", { className: styles.loadingOverlay },
            React.createElement(Spinner, { size: "small", label: "Loading..." })))));
};
//# sourceMappingURL=GridView.js.map