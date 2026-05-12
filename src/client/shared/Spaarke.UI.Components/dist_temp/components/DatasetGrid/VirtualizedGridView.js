import * as React from 'react';
import { FixedSizeList as List } from 'react-window';
import { makeStyles, tokens, shorthands } from '@fluentui/react-components';
import { ColumnRendererService } from '../../services/ColumnRendererService';
const useStyles = makeStyles({
    gridContainer: {
        width: '100%',
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
    },
    header: {
        display: 'flex',
        ...shorthands.borderBottom('2px', 'solid', tokens.colorNeutralStroke1),
        backgroundColor: tokens.colorNeutralBackground2,
        fontWeight: tokens.fontWeightSemibold,
        position: 'sticky',
        top: 0,
        zIndex: 1,
    },
    headerCell: {
        ...shorthands.padding('12px', '16px'),
        textAlign: 'left',
        color: tokens.colorNeutralForeground1,
    },
    row: {
        display: 'flex',
        ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
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
    cell: {
        ...shorthands.padding('12px', '16px'),
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
    },
});
export const VirtualizedGridView = props => {
    const styles = useStyles();
    const listRef = React.useRef(null);
    // Filter to only readable columns
    const displayColumns = props.columns.filter(col => col.canRead !== false);
    // Calculate column width (equal distribution for simplicity)
    const columnWidth = `${100 / displayColumns.length}%`;
    const renderRow = React.useCallback(({ index, style }) => {
        const record = props.records[index];
        const isSelected = props.selectedRecordIds.includes(record.id);
        return (React.createElement("div", { style: style, className: `${styles.row} ${isSelected ? styles.rowSelected : ''}`, onClick: () => props.onRecordClick?.(record.id) }, displayColumns.map(col => {
            const renderer = ColumnRendererService.getRenderer(col);
            const value = renderer(record[col.name], record, col);
            return (React.createElement("div", { key: col.name, className: styles.cell, style: { width: columnWidth } }, value));
        })));
    }, [props.records, props.selectedRecordIds, props.onRecordClick, displayColumns, columnWidth, styles]);
    return (React.createElement("div", { className: styles.gridContainer },
        React.createElement("div", { className: styles.header }, displayColumns.map(col => (React.createElement("div", { key: col.name, className: styles.headerCell, style: { width: columnWidth } }, col.displayName)))),
        React.createElement(List, { ref: listRef, height: 600, itemCount: props.records.length, itemSize: props.itemHeight, width: "100%", overscanCount: props.overscanCount }, renderRow)));
};
//# sourceMappingURL=VirtualizedGridView.js.map