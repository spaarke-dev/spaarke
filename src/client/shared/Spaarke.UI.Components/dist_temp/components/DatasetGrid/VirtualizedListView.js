import * as React from 'react';
import { FixedSizeList as List } from 'react-window';
import { makeStyles, tokens, shorthands } from '@fluentui/react-components';
import { ColumnRendererService } from '../../services/ColumnRendererService';
const useStyles = makeStyles({
    listContainer: {
        width: '100%',
        height: '100%',
    },
    row: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
        ...shorthands.padding('8px', '16px'),
        cursor: 'pointer',
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
    },
    rowSelected: {
        backgroundColor: tokens.colorNeutralBackground1Selected,
        ':hover': {
            backgroundColor: tokens.colorNeutralBackground1Selected,
        },
    },
    primaryColumn: {
        flex: 1,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorBrandForeground1,
    },
    secondaryColumn: {
        flex: 1,
        color: tokens.colorNeutralForeground2,
    },
});
export const VirtualizedListView = props => {
    const styles = useStyles();
    const listRef = React.useRef(null);
    // Get primary and first secondary column
    const primaryColumn = props.columns.find(c => c.isPrimary) ?? props.columns[0];
    const secondaryColumn = props.columns.find(c => !c.isPrimary && c.name !== primaryColumn?.name);
    const renderRow = React.useCallback(({ index, style }) => {
        const record = props.records[index];
        const isSelected = props.selectedRecordIds.includes(record.id);
        const primaryRenderer = ColumnRendererService.getRenderer(primaryColumn);
        const primaryValue = primaryRenderer(record[primaryColumn.name], record, primaryColumn);
        let secondaryValue = null;
        if (secondaryColumn) {
            const secondaryRenderer = ColumnRendererService.getRenderer(secondaryColumn);
            secondaryValue = secondaryRenderer(record[secondaryColumn.name], record, secondaryColumn);
        }
        return (React.createElement("div", { style: style, className: `${styles.row} ${isSelected ? styles.rowSelected : ''}`, onClick: () => props.onRecordClick?.(record.id) },
            React.createElement("div", { className: styles.primaryColumn }, primaryValue),
            secondaryValue && React.createElement("div", { className: styles.secondaryColumn }, secondaryValue)));
    }, [props.records, props.selectedRecordIds, props.onRecordClick, primaryColumn, secondaryColumn, styles]);
    return (React.createElement("div", { className: styles.listContainer },
        React.createElement(List, { ref: listRef, height: 600, itemCount: props.records.length, itemSize: props.itemHeight, width: "100%", overscanCount: props.overscanCount }, renderRow)));
};
//# sourceMappingURL=VirtualizedListView.js.map