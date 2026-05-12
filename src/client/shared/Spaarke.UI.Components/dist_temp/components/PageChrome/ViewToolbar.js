/**
 * ViewToolbar Component
 *
 * Horizontal toolbar row below the CommandBar containing the ViewSelector
 * and optional Edit filters / Edit columns buttons.
 * Styled to match OOB Power Apps view toolbar.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */
import * as React from 'react';
import { ToolbarButton, ToolbarDivider, Tooltip, Text, makeStyles, tokens, mergeClasses, } from '@fluentui/react-components';
import { Filter20Regular, TableSettings20Regular, ChevronDown20Regular } from '@fluentui/react-icons';
const useStyles = makeStyles({
    toolbar: {
        backgroundColor: tokens.colorNeutralBackground1,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        minHeight: '36px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
    },
    toolbarCompact: {
        minHeight: '32px',
        paddingTop: '2px',
        paddingBottom: '2px',
    },
    leftSection: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalM,
        flexGrow: 1,
    },
    rightSection: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
    viewNameButton: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXS,
        cursor: 'pointer',
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: 'transparent',
        border: '0',
        fontFamily: 'inherit',
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        '&:hover': {
            backgroundColor: tokens.colorNeutralBackground1Hover,
        },
        '&:active': {
            backgroundColor: tokens.colorNeutralBackground1Pressed,
        },
    },
    viewName: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
    },
    recordCount: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginLeft: tokens.spacingHorizontalS,
    },
    chevron: {
        color: tokens.colorNeutralForeground3,
    },
    actionButton: {
        color: tokens.colorNeutralForeground2,
    },
    divider: {
        height: '20px',
    },
});
/**
 * ViewToolbar - Horizontal bar below CommandBar with view selector and actions
 */
export const ViewToolbar = ({ children, viewName, recordCount, showEditFilters = false, showEditColumns = false, onEditFilters, onEditColumns, onViewClick, compact = false, className, }) => {
    const styles = useStyles();
    const hasRightSection = showEditFilters || showEditColumns;
    return (React.createElement("div", { className: mergeClasses(styles.toolbar, compact && styles.toolbarCompact, className), role: "toolbar", "aria-label": "View toolbar" },
        React.createElement("div", { className: styles.leftSection }, children ? (
        // Render children (ViewSelector component)
        React.createElement(React.Fragment, null,
            children,
            recordCount !== undefined && (React.createElement(Text, { className: styles.recordCount },
                "(",
                recordCount.toLocaleString(),
                " ",
                recordCount === 1 ? 'record' : 'records',
                ")")))) : viewName ? (
        // Render simple view name button
        React.createElement("button", { className: styles.viewNameButton, onClick: onViewClick, "aria-label": "Change view", "aria-haspopup": "listbox" },
            React.createElement("span", { className: styles.viewName }, viewName),
            React.createElement(ChevronDown20Regular, { className: styles.chevron }),
            recordCount !== undefined && React.createElement(Text, { className: styles.recordCount },
                "(",
                recordCount.toLocaleString(),
                ")"))) : null),
        hasRightSection && (React.createElement("div", { className: styles.rightSection },
            showEditFilters && (React.createElement(Tooltip, { content: "Edit filters", relationship: "label" },
                React.createElement(ToolbarButton, { icon: React.createElement(Filter20Regular, null), onClick: onEditFilters, className: styles.actionButton, "aria-label": "Edit filters" }, !compact ? 'Edit filters' : undefined))),
            showEditFilters && showEditColumns && React.createElement(ToolbarDivider, { className: styles.divider }),
            showEditColumns && (React.createElement(Tooltip, { content: "Edit columns", relationship: "label" },
                React.createElement(ToolbarButton, { icon: React.createElement(TableSettings20Regular, null), onClick: onEditColumns, className: styles.actionButton, "aria-label": "Edit columns" }, !compact ? 'Edit columns' : undefined)))))));
};
// Default export for convenience
export default ViewToolbar;
//# sourceMappingURL=ViewToolbar.js.map