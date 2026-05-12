/**
 * ListView - Compact list layout for simple records
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { makeStyles, tokens, Text, Button, Spinner, Checkbox, mergeClasses } from '@fluentui/react-components';
import { ChevronRightRegular } from '@fluentui/react-icons';
import { useVirtualization } from '../../hooks/useVirtualization';
import { VirtualizedListView } from './VirtualizedListView';
const useStyles = makeStyles({
    root: {
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        position: 'relative',
    },
    scrollContainer: {
        flex: 1,
        overflow: 'auto',
    },
    listContainer: {
        display: 'flex',
        flexDirection: 'column',
    },
    listItem: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        cursor: 'pointer',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    listItemSelected: {
        backgroundColor: tokens.colorNeutralBackground1Selected,
    },
    listItemContent: {
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
        minWidth: 0,
    },
    primaryText: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
    },
    secondaryText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
    },
    metadataText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        marginLeft: 'auto',
        flexShrink: 0,
    },
    chevron: {
        color: tokens.colorNeutralForeground3,
        flexShrink: 0,
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
        width: 'calc(100% - 32px)',
        marginLeft: tokens.spacingHorizontalM,
        marginRight: tokens.spacingHorizontalM,
    },
    emptyState: {
        padding: tokens.spacingVerticalXXL,
        textAlign: 'center',
        color: tokens.colorNeutralForeground3,
    },
});
export const ListView = props => {
    const styles = useStyles();
    const scrollContainerRef = React.useRef(null);
    // Check if virtualization should be enabled
    const virtualization = useVirtualization(props.records.length, {
        enabled: props.enableVirtualization ?? true,
    });
    // Filter to only readable columns
    const readableColumns = React.useMemo(() => {
        return props.columns.filter(col => col.canRead !== false);
    }, [props.columns]);
    // Use virtualized view for large datasets
    if (virtualization.shouldVirtualize) {
        return (React.createElement(VirtualizedListView, { records: props.records, columns: readableColumns, selectedRecordIds: props.selectedRecordIds, itemHeight: virtualization.itemHeight, overscanCount: virtualization.overscanCount, onRecordClick: recordId => {
                const record = props.records.find(r => r.id === recordId);
                if (record)
                    props.onRecordClick(record);
            } }));
    }
    const isInfiniteScroll = React.useMemo(() => {
        if (props.scrollBehavior === 'Infinite')
            return true;
        if (props.scrollBehavior === 'Paged')
            return false;
        return props.records.length > 100;
    }, [props.scrollBehavior, props.records.length]);
    const handleScroll = React.useCallback((e) => {
        if (!isInfiniteScroll || !props.hasNextPage || props.loading) {
            return;
        }
        const container = e.currentTarget;
        const scrollPercentage = (container.scrollTop + container.clientHeight) / container.scrollHeight;
        if (scrollPercentage > 0.9) {
            props.loadNextPage();
        }
    }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);
    const handleItemSelect = React.useCallback((recordId, checked) => {
        if (checked) {
            props.onSelectionChange([...props.selectedRecordIds, recordId]);
        }
        else {
            props.onSelectionChange(props.selectedRecordIds.filter(id => id !== recordId));
        }
    }, [props]);
    const handleItemClick = React.useCallback((record, e) => {
        if (e.target.closest('input[type="checkbox"]')) {
            return;
        }
        props.onRecordClick(record);
    }, [props]);
    const displayColumns = React.useMemo(() => {
        return readableColumns.slice(0, 3);
    }, [readableColumns]);
    if (props.records.length === 0 && !props.loading) {
        return (React.createElement("div", { className: styles.emptyState },
            React.createElement("p", null, "No records to display")));
    }
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.scrollContainer, ref: scrollContainerRef, onScroll: handleScroll },
            React.createElement("div", { className: styles.listContainer }, props.records.map(record => {
                const isSelected = props.selectedRecordIds.includes(record.id);
                const primaryCol = displayColumns[0];
                const secondaryCol = displayColumns[1];
                const metadataCol = displayColumns[2];
                const primaryValue = primaryCol ? String(record[primaryCol.name] || '') : record.id;
                const secondaryValue = secondaryCol ? String(record[secondaryCol.name] || '') : '';
                const metadataValue = metadataCol ? String(record[metadataCol.name] || '') : '';
                return (React.createElement("div", { key: record.id, className: mergeClasses(styles.listItem, isSelected && styles.listItemSelected), onClick: e => handleItemClick(record, e) },
                    React.createElement(Checkbox, { checked: isSelected, onChange: (_e, data) => handleItemSelect(record.id, !!data.checked) }),
                    React.createElement("div", { className: styles.listItemContent },
                        React.createElement(Text, { className: styles.primaryText }, primaryValue),
                        secondaryValue && React.createElement(Text, { className: styles.secondaryText }, secondaryValue)),
                    metadataValue && React.createElement(Text, { className: styles.metadataText }, metadataValue),
                    React.createElement(ChevronRightRegular, { className: styles.chevron })));
            }))),
        isInfiniteScroll && props.loading && (React.createElement("div", { className: styles.loadingOverlay },
            React.createElement(Spinner, { size: "small", label: "Loading more records..." }))),
        !isInfiniteScroll && props.hasNextPage && !props.loading && (React.createElement(Button, { appearance: "subtle", className: styles.loadMoreButton, onClick: props.loadNextPage },
            "Load More (",
            props.records.length,
            " records loaded)")),
        !isInfiniteScroll && props.loading && (React.createElement("div", { className: styles.loadingOverlay },
            React.createElement(Spinner, { size: "small", label: "Loading..." })))));
};
//# sourceMappingURL=ListView.js.map