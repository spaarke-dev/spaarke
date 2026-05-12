/**
 * AssignResourcesStep.tsx
 * Follow-on step for "Assign Resources" in the Create New Matter wizard.
 *
 * Uses LookupField for each assignment. Values are lifted to WizardDialog
 * form state (AI pre-fill populates these from CreateRecordStep).
 *
 * Constraints:
 *   - Fluent v9: Text, Checkbox -- ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */
import * as React from 'react';
import { Text, Checkbox, makeStyles, tokens, } from '@fluentui/react-components';
import { LookupField } from './LookupField';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    headerText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
    section: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    sectionTitle: {
        color: tokens.colorNeutralForeground1,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        paddingBottom: tokens.spacingVerticalXS,
    },
    sectionFields: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalS,
    },
    notifySection: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
    },
    notifyHint: {
        color: tokens.colorNeutralForeground4,
        paddingLeft: '28px', // align with checkbox label
    },
});
// ---------------------------------------------------------------------------
// AssignResourcesStep (exported)
// ---------------------------------------------------------------------------
export const AssignResourcesStep = ({ attorneyValue, onAttorneyChange, onSearchAttorneys, isAttorneyAiPrefilled, paralegalValue, onParalegalChange, onSearchParalegals, isParalegalAiPrefilled, outsideCounselValue, onOutsideCounselChange, onSearchOutsideCounsel, isOutsideCounselAiPrefilled, notifyResources, onNotifyChange, }) => {
    const styles = useStyles();
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Assign Resources"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Search and assign internal and external resources to this matter. All fields are optional.")),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Internal Resources"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement(LookupField, { label: "Assigned Attorney", placeholder: "Search contacts...", value: attorneyValue, onChange: onAttorneyChange, onSearch: onSearchAttorneys, isAiPrefilled: isAttorneyAiPrefilled, minSearchLength: 2 }),
                React.createElement(LookupField, { label: "Assigned Paralegal", placeholder: "Search contacts...", value: paralegalValue, onChange: onParalegalChange, onSearch: onSearchParalegals, isAiPrefilled: isParalegalAiPrefilled, minSearchLength: 2 }))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "External Resources"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement(LookupField, { label: "Assigned Outside Counsel", placeholder: "Search organizations...", value: outsideCounselValue, onChange: onOutsideCounselChange, onSearch: onSearchOutsideCounsel, isAiPrefilled: isOutsideCounselAiPrefilled, minSearchLength: 2 }))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Notifications"),
            React.createElement("div", { className: styles.notifySection },
                React.createElement(Checkbox, { checked: notifyResources, onChange: (_e, data) => onNotifyChange(!!data.checked), label: "Notify assigned resources" }),
                React.createElement(Text, { size: 100, className: styles.notifyHint }, "Notifications will be sent when this feature is available.")))));
};
//# sourceMappingURL=AssignResourcesStep.js.map