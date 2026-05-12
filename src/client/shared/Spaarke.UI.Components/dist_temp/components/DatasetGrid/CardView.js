/**
 * CardView - Tile/Card layout for visual content
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { Card, CardHeader, makeStyles, tokens, Text, Button, Spinner, Checkbox } from '@fluentui/react-components';
import { ColumnRendererService } from '../../services/ColumnRendererService';
const useStyles = makeStyles({
    root: {
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
    },
    scrollContainer: {
        flex: 1,
        overflow: 'auto',
        padding: tokens.spacingVerticalM,
    },
    cardGrid: {
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))',
        gap: tokens.spacingHorizontalM,
    },
    card: {
        cursor: 'pointer',
        height: '240px',
    },
    cardHeader: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    cardContent: {
        padding: tokens.spacingVerticalM,
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
    },
    fieldRow: {
        display: 'flex',
        justifyContent: 'space-between',
    },
    fieldLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
    },
    fieldValue: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
    },
    loadingOverlay: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: tokens.spacingVerticalL,
    },
    loadMoreButton: {
        margin: tokens.spacingVerticalM,
    },
    emptyState: {
        padding: tokens.spacingVerticalXXL,
        textAlign: 'center',
        color: tokens.colorNeutralForeground3,
    },
});
export const CardView = props => {
    const styles = useStyles();
    const isInfiniteScroll = React.useMemo(() => {
        if (props.scrollBehavior === 'Infinite')
            return true;
        if (props.scrollBehavior === 'Paged')
            return false;
        return props.records.length > 100;
    }, [props.scrollBehavior, props.records.length]);
    const handleScroll = React.useCallback((e) => {
        if (!isInfiniteScroll || !props.hasNextPage || props.loading)
            return;
        const container = e.currentTarget;
        const scrollPercentage = (container.scrollTop + container.clientHeight) / container.scrollHeight;
        if (scrollPercentage > 0.9)
            props.loadNextPage();
    }, [isInfiniteScroll, props.hasNextPage, props.loading, props.loadNextPage]);
    const handleCardSelect = React.useCallback((recordId, checked) => {
        if (checked) {
            props.onSelectionChange([...props.selectedRecordIds, recordId]);
        }
        else {
            props.onSelectionChange(props.selectedRecordIds.filter(id => id !== recordId));
        }
    }, [props]);
    const handleCardClick = React.useCallback((record, e) => {
        if (e.target.closest('input[type="checkbox"]'))
            return;
        props.onRecordClick(record);
    }, [props]);
    // Filter readable columns and take first 3 for card display
    const displayColumns = React.useMemo(() => props.columns.filter(col => col.canRead !== false).slice(0, 3), [props.columns]);
    if (props.records.length === 0 && !props.loading) {
        return (React.createElement("div", { className: styles.emptyState },
            React.createElement("p", null, "No records to display")));
    }
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.scrollContainer, onScroll: handleScroll },
            React.createElement("div", { className: styles.cardGrid }, props.records.map(record => {
                const isSelected = props.selectedRecordIds.includes(record.id);
                const primaryField = displayColumns[0];
                const primaryValue = primaryField ? String(record[primaryField.name] || '') : record.id;
                return (React.createElement(Card, { key: record.id, className: styles.card, onClick: e => handleCardClick(record, e) },
                    React.createElement(CardHeader, { header: React.createElement("div", { className: styles.cardHeader },
                            React.createElement(Checkbox, { checked: isSelected, onChange: (_e, data) => handleCardSelect(record.id, !!data.checked) }),
                            React.createElement(Text, { weight: "semibold", truncate: true }, primaryValue)) }),
                    React.createElement("div", { className: styles.cardContent }, displayColumns.slice(1).map(col => {
                        const renderer = ColumnRendererService.getRenderer(col);
                        const renderedValue = renderer(record[col.name], record, col);
                        return (React.createElement("div", { key: col.name, className: styles.fieldRow },
                            React.createElement(Text, { className: styles.fieldLabel },
                                col.displayName,
                                ":"),
                            React.createElement("div", { className: styles.fieldValue }, renderedValue)));
                    }))));
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
//# sourceMappingURL=CardView.js.map