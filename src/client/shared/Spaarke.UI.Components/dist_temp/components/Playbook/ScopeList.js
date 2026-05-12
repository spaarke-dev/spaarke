/**
 * ScopeList Component
 *
 * Generic checkbox/radio list for selecting scope items (actions, skills,
 * knowledge, tools). Supports multi-select (Checkbox) and single-select
 * (RadioGroup) modes, as well as a read-only locked state.
 *
 * Ported from src/client/pcf/AnalysisBuilder/control/components/ScopeList.tsx
 * and adapted for React 18 / Code Page usage with external selectedIds state.
 */
import React from 'react';
import { Checkbox, Radio, RadioGroup, Text, Spinner, makeStyles, tokens, mergeClasses, } from '@fluentui/react-components';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        gap: '8px',
    },
    item: {
        display: 'flex',
        alignItems: 'flex-start',
        gap: '12px',
        paddingTop: '12px',
        paddingBottom: '12px',
        paddingLeft: '12px',
        paddingRight: '12px',
        borderRadius: tokens.borderRadiusMedium,
        backgroundColor: tokens.colorNeutralBackground1,
        cursor: 'pointer',
    },
    itemSelected: {
        backgroundColor: tokens.colorBrandBackground2,
    },
    itemReadOnly: {
        cursor: 'default',
        opacity: '0.7',
    },
    selector: {
        flexShrink: 0,
        marginTop: '2px',
    },
    content: {
        flex: 1,
        minWidth: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
    },
    name: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    description: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground2,
        lineHeight: '1.4',
    },
    loading: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '48px',
        gap: '16px',
    },
    empty: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '48px',
        color: tokens.colorNeutralForeground3,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export function ScopeList({ items, selectedIds, onSelectionChange, isLoading, multiSelect = true, emptyMessage = 'No items available', readOnly = false, }) {
    const styles = useStyles();
    // ------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------
    const handleCheckboxChange = (itemId, checked) => {
        if (readOnly)
            return;
        let newSelected;
        if (checked) {
            newSelected = [...selectedIds, itemId];
        }
        else {
            newSelected = selectedIds.filter(id => id !== itemId);
        }
        onSelectionChange(newSelected);
    };
    const handleRadioChange = (_event, data) => {
        if (readOnly)
            return;
        onSelectionChange([data.value]);
    };
    // ------------------------------------------------------------------
    // Loading state
    // ------------------------------------------------------------------
    if (isLoading) {
        return (React.createElement("div", { className: styles.loading },
            React.createElement(Spinner, { size: "medium", label: "Loading items..." })));
    }
    // ------------------------------------------------------------------
    // Empty state
    // ------------------------------------------------------------------
    if (items.length === 0) {
        return (React.createElement("div", { className: styles.empty },
            React.createElement(Text, null, emptyMessage)));
    }
    // ------------------------------------------------------------------
    // Single-select — RadioGroup
    // ------------------------------------------------------------------
    if (!multiSelect) {
        const selectedValue = selectedIds.length > 0 ? selectedIds[0] : '';
        return (React.createElement(RadioGroup, { value: selectedValue, onChange: handleRadioChange, disabled: readOnly, className: styles.container }, items.map(item => {
            const isSelected = selectedIds.includes(item.id);
            return (React.createElement("div", { key: item.id, className: mergeClasses(styles.item, isSelected && styles.itemSelected, readOnly && styles.itemReadOnly) },
                React.createElement(Radio, { value: item.id, disabled: readOnly, className: styles.selector }),
                React.createElement("div", { className: styles.content },
                    React.createElement(Text, { className: styles.name }, item.name),
                    item.description && React.createElement(Text, { className: styles.description }, item.description))));
        })));
    }
    // ------------------------------------------------------------------
    // Multi-select — Checkboxes
    // ------------------------------------------------------------------
    return (React.createElement("div", { className: styles.container }, items.map(item => {
        const isSelected = selectedIds.includes(item.id);
        return (React.createElement("div", { key: item.id, className: mergeClasses(styles.item, isSelected && styles.itemSelected, readOnly && styles.itemReadOnly) },
            React.createElement(Checkbox, { checked: isSelected, disabled: readOnly, onChange: (_e, data) => handleCheckboxChange(item.id, !!data.checked), className: styles.selector }),
            React.createElement("div", { className: styles.content },
                React.createElement(Text, { className: styles.name }, item.name),
                item.description && React.createElement(Text, { className: styles.description }, item.description))));
    })));
}
export default ScopeList;
//# sourceMappingURL=ScopeList.js.map