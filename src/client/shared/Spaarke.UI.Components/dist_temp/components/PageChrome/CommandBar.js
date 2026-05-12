/**
 * CommandBar Component
 *
 * OOB-style command bar for Custom Pages.
 * Matches Power Apps entity homepage ribbon styling.
 *
 * Features:
 * - Standard commands: New, Delete, Refresh
 * - Custom commands via props
 * - Selection-aware delete button
 * - Optional search box
 * - Dark mode support
 * - Keyboard shortcuts
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */
import * as React from 'react';
import { Toolbar, ToolbarButton, ToolbarDivider, ToolbarGroup, Tooltip, Input, Badge, makeStyles, tokens, mergeClasses, } from '@fluentui/react-components';
import { Add20Regular, Delete20Regular, ArrowClockwise20Regular, Search20Regular, } from '@fluentui/react-icons';
import { useKeyboardShortcuts } from '../../hooks/useKeyboardShortcuts';
const useStyles = makeStyles({
    toolbar: {
        backgroundColor: tokens.colorNeutralBackground1,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        minHeight: '44px',
        display: 'flex',
        alignItems: 'center',
        flexWrap: 'nowrap',
    },
    toolbarCompact: {
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        minHeight: '36px',
    },
    leftGroup: {
        display: 'flex',
        alignItems: 'center',
        flexGrow: 1,
    },
    rightGroup: {
        display: 'flex',
        alignItems: 'center',
        marginLeft: 'auto',
    },
    searchBox: {
        width: '200px',
        marginLeft: tokens.spacingHorizontalM,
    },
    deleteBadge: {
        marginLeft: tokens.spacingHorizontalXS,
    },
    buttonLabel: {
        marginLeft: tokens.spacingHorizontalXS,
    },
});
/**
 * CommandBar - OOB-style command bar for Power Apps Custom Pages
 */
export const CommandBar = ({ entityLogicalName, selectedIds = [], commands = [], onNew, onDelete, onRefresh, onSearch, showNew = true, showDelete = true, showRefresh = true, showSearch = false, searchPlaceholder = 'Search...', canCreate = true, canDelete = true, compact = false, className, }) => {
    const styles = useStyles();
    const [searchText, setSearchText] = React.useState('');
    const hasSelection = selectedIds.length > 0;
    const selectionCount = selectedIds.length;
    // Keyboard shortcuts
    const shortcuts = React.useMemo(() => [
        {
            key: 'ctrl+n',
            handler: () => {
                if (showNew && canCreate && onNew) {
                    onNew();
                }
            },
            description: 'Create new record',
        },
        {
            key: 'delete',
            handler: () => {
                if (showDelete && canDelete && hasSelection && onDelete) {
                    onDelete(selectedIds);
                }
            },
            description: 'Delete selected records',
        },
        {
            key: 'f5',
            handler: (e) => {
                e.preventDefault();
                if (showRefresh && onRefresh) {
                    onRefresh();
                }
            },
            description: 'Refresh data',
        },
    ], [showNew, showDelete, showRefresh, canCreate, canDelete, hasSelection, selectedIds, onNew, onDelete, onRefresh]);
    useKeyboardShortcuts(shortcuts);
    // Handle search
    const handleSearchChange = React.useCallback((e) => {
        const value = e.target.value;
        setSearchText(value);
    }, []);
    const handleSearchKeyDown = React.useCallback((e) => {
        if (e.key === 'Enter' && onSearch) {
            onSearch(searchText);
        }
    }, [searchText, onSearch]);
    // Handle delete click
    const handleDeleteClick = React.useCallback(() => {
        if (onDelete && hasSelection) {
            onDelete(selectedIds);
        }
    }, [onDelete, hasSelection, selectedIds]);
    return (React.createElement(Toolbar, { "aria-label": `${entityLogicalName} command bar`, className: mergeClasses(styles.toolbar, compact && styles.toolbarCompact, className) },
        React.createElement(ToolbarGroup, { className: styles.leftGroup },
            showNew && (React.createElement(Tooltip, { content: React.createElement(React.Fragment, null,
                    "New ",
                    entityLogicalName,
                    React.createElement("span", { style: { marginLeft: '8px', opacity: 0.7 } }, "Ctrl+N")), relationship: "description" },
                React.createElement(ToolbarButton, { icon: React.createElement(Add20Regular, null), disabled: !canCreate, onClick: onNew, "aria-label": `Create new ${entityLogicalName}`, "aria-keyshortcuts": "Control+N" },
                    React.createElement("span", { className: styles.buttonLabel }, "New")))),
            showDelete && (React.createElement(React.Fragment, null,
                showNew && React.createElement(ToolbarDivider, null),
                React.createElement(Tooltip, { content: hasSelection
                        ? `Delete ${selectionCount} selected ${selectionCount === 1 ? 'record' : 'records'}`
                        : 'Select records to delete', relationship: "description" },
                    React.createElement(ToolbarButton, { icon: React.createElement(Delete20Regular, null), disabled: !canDelete || !hasSelection, onClick: handleDeleteClick, "aria-label": `Delete selected ${entityLogicalName} records`, "aria-keyshortcuts": "Delete" },
                        React.createElement("span", { className: styles.buttonLabel }, "Delete"),
                        hasSelection && (React.createElement(Badge, { appearance: "filled", color: "danger", size: "small", className: styles.deleteBadge }, selectionCount)))))),
            showRefresh && (React.createElement(React.Fragment, null,
                (showNew || showDelete) && React.createElement(ToolbarDivider, null),
                React.createElement(Tooltip, { content: React.createElement(React.Fragment, null,
                        "Refresh data",
                        React.createElement("span", { style: { marginLeft: '8px', opacity: 0.7 } }, "F5")), relationship: "description" },
                    React.createElement(ToolbarButton, { icon: React.createElement(ArrowClockwise20Regular, null), onClick: onRefresh, "aria-label": "Refresh data", "aria-keyshortcuts": "F5" }, !compact && React.createElement("span", { className: styles.buttonLabel }, "Refresh"))))),
            commands.length > 0 && (showNew || showDelete || showRefresh) && React.createElement(ToolbarDivider, null),
            commands.map(command => (React.createElement(React.Fragment, { key: command.key },
                React.createElement(Tooltip, { content: command.description || command.label, relationship: "description" },
                    React.createElement(ToolbarButton, { icon: command.icon, disabled: command.disabled, onClick: command.onClick, "aria-label": command.label }, !compact && React.createElement("span", { className: styles.buttonLabel }, command.label))),
                command.dividerAfter && React.createElement(ToolbarDivider, null))))),
        showSearch && (React.createElement(ToolbarGroup, { className: styles.rightGroup },
            React.createElement(Input, { className: styles.searchBox, contentBefore: React.createElement(Search20Regular, null), placeholder: searchPlaceholder, value: searchText, onChange: handleSearchChange, onKeyDown: handleSearchKeyDown, size: compact ? 'small' : 'medium', "aria-label": "Search records" })))));
};
// Default export for convenience
export default CommandBar;
//# sourceMappingURL=CommandBar.js.map