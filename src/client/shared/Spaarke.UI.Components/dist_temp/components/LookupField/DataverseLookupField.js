/**
 * DataverseLookupField.tsx
 * Lookup field that opens the standard Dataverse lookup side pane
 * via INavigationService.openLookup() (Xrm.Utility.lookupObjects).
 *
 * When a value is selected it renders as a dismissible chip (same visual as
 * the inline LookupField). When no value is selected a "Select" button is shown.
 *
 * Falls back to the inline LookupField when no navigationService is provided
 * or when the caller passes an explicit onSearch function and the lookup returns
 * an empty result (graceful no-op in non-Dataverse contexts such as the BFF SPA).
 *
 * Usage:
 * ```tsx
 * <DataverseLookupField
 *   label="Matter Type"
 *   required
 *   entityType="sprk_mattertype_ref"
 *   value={matterTypeValue}
 *   onChange={handleMatterTypeChange}
 *   navigationService={navigationService}
 *   // Fallback: used when navigationService is absent or returns empty
 *   onSearch={handleSearchMatterTypes}
 *   isAiPrefilled={isAiField('matterTypeId')}
 * />
 * ```
 *
 * Constraints:
 *   - Fluent v9 only: Button, Text, Field, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colours
 *   - Supports light, dark, and high-contrast modes
 *
 * @see INavigationService.openLookup
 * @see ADR-012 — Shared Component Library
 * @see ADR-021 — Fluent v9 Design System
 */
import * as React from 'react';
import { Button, Field, Spinner, Text, makeStyles, tokens, } from '@fluentui/react-components';
import { DismissRegular, SearchRegular } from '@fluentui/react-icons';
import { LookupField } from './LookupField';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    wrapper: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
    },
    // ── Label row ─────────────────────────────────────────────────────────────
    labelRow: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    requiredMark: {
        color: tokens.colorPaletteRedForeground1,
    },
    // ── Empty state: search-input–styled button ─────────────────────────────
    selectRow: {
        display: 'flex',
        alignItems: 'center',
        width: '100%',
    },
    selectButton: {
        width: '100%',
        justifyContent: 'flex-start',
        fontWeight: tokens.fontWeightRegular,
        color: tokens.colorNeutralForeground4,
        borderTopColor: tokens.colorNeutralStroke1,
        borderRightColor: tokens.colorNeutralStroke1,
        borderBottomColor: tokens.colorNeutralStroke1,
        borderLeftColor: tokens.colorNeutralStroke1,
        minHeight: '32px',
    },
    // ── Selected chip ─────────────────────────────────────────────────────────
    selectedChip: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalXXS,
        paddingBottom: tokens.spacingVerticalXXS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalXXS,
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: tokens.colorBrandBackground2,
        borderTopWidth: '1px',
        borderRightWidth: '1px',
        borderBottomWidth: '1px',
        borderLeftWidth: '1px',
        borderTopStyle: 'solid',
        borderRightStyle: 'solid',
        borderBottomStyle: 'solid',
        borderLeftStyle: 'solid',
        borderTopColor: tokens.colorBrandStroke2,
        borderRightColor: tokens.colorBrandStroke2,
        borderBottomColor: tokens.colorBrandStroke2,
        borderLeftColor: tokens.colorBrandStroke2,
        alignSelf: 'flex-start',
        marginTop: tokens.spacingVerticalXXS,
    },
    selectedChipName: {
        color: tokens.colorBrandForeground2,
    },
});
// ---------------------------------------------------------------------------
// DataverseLookupField
// ---------------------------------------------------------------------------
/**
 * Renders a label with an optional required mark and optional extra content.
 */
function LabelContent({ label, required, labelExtra, styles, }) {
    return (React.createElement("span", { className: styles.labelRow },
        label,
        required && (React.createElement("span", { "aria-hidden": "true", className: styles.requiredMark }, ' *')),
        labelExtra));
}
export const DataverseLookupField = ({ label, required, entityType, value, onChange, navigationService, onSearch, placeholder, labelExtra, minSearchLength = 1, }) => {
    const styles = useStyles();
    const [isOpening, setIsOpening] = React.useState(false);
    // ── Open Dataverse lookup side pane ──────────────────────────────────────
    const handleOpenLookup = React.useCallback(async () => {
        if (!navigationService)
            return;
        setIsOpening(true);
        try {
            const results = await navigationService.openLookup({ entityType });
            if (results.length > 0) {
                // Use first result (allowMultiSelect defaults to false)
                onChange({ id: results[0].id, name: results[0].name });
            }
            // If results is empty the user cancelled — preserve existing value
        }
        finally {
            setIsOpening(false);
        }
    }, [navigationService, entityType, onChange]);
    // ── Clear selection ───────────────────────────────────────────────────────
    const handleClear = React.useCallback(() => {
        onChange(null);
    }, [onChange]);
    // ── Fallback: no navigation service (or not a Dataverse context) ──────────
    if (!navigationService) {
        if (onSearch) {
            return (React.createElement(LookupField, { label: label, required: required, placeholder: placeholder ?? `Search ${label.toLowerCase()}...`, value: value, onChange: onChange, onSearch: onSearch, labelExtra: labelExtra, minSearchLength: minSearchLength }));
        }
        // No navigationService and no onSearch — render read-only chip or empty
        return (React.createElement("div", { className: styles.wrapper },
            React.createElement(Field, { label: React.createElement(LabelContent, { label: label, required: required, labelExtra: labelExtra, styles: styles }), required: required }, value ? (React.createElement("div", { className: styles.selectedChip },
                React.createElement(Text, { size: 200, weight: "semibold", className: styles.selectedChipName }, value.name))) : (React.createElement(Text, { size: 200, style: { color: tokens.colorNeutralForeground3 } }, "No selection")))));
    }
    // ── Dataverse side-pane lookup mode ─────────────────────────────────────
    return (React.createElement("div", { className: styles.wrapper },
        React.createElement(Field, { label: React.createElement(LabelContent, { label: label, required: required, labelExtra: labelExtra, styles: styles }), required: required }, value ? (
        // Selected: show chip with dismiss button
        React.createElement("div", { className: styles.selectedChip },
            React.createElement(Text, { size: 200, weight: "semibold", className: styles.selectedChipName }, value.name),
            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(DismissRegular, { fontSize: 14 }), onClick: handleClear, "aria-label": `Clear ${label}` }))) : (
        // Empty: show "Select" button that opens the Dataverse lookup pane
        React.createElement("div", { className: styles.selectRow },
            React.createElement(Button, { className: styles.selectButton, appearance: "outline", size: "medium", icon: isOpening ? React.createElement(Spinner, { size: "extra-tiny" }) : React.createElement(SearchRegular, null), onClick: handleOpenLookup, disabled: isOpening, "aria-label": `Select ${label}` }, isOpening ? 'Opening\u2026' : placeholder ?? `Search ${label.toLowerCase()}...`))))));
};
DataverseLookupField.displayName = 'DataverseLookupField';
//# sourceMappingURL=DataverseLookupField.js.map