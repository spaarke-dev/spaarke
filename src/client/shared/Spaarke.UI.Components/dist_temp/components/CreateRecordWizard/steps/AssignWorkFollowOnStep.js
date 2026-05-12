/**
 * AssignWorkFollowOnStep.tsx
 * Follow-on step for creating a Work Assignment linked to the parent entity.
 *
 * Replaces the old "Assign Resources" step. Collects all fields needed to
 * create a sprk_workassignment Dataverse record, linked to the parent matter
 * or project via N:1 relationship.
 *
 * Fields:
 *   - Name (required, free text)
 *   - Description (optional, multi-line)
 *   - Matter Type (optional, lookup — auto-filled from parent matter)
 *   - Practice Area (optional, lookup — auto-filled from parent record)
 *   - Priority (option set: Low / Normal / High / Critical; defaults to Normal)
 *   - Response Due Date (optional, date picker)
 *   - Assigned Attorney (optional, contact lookup)
 *   - Assigned Paralegal (optional, contact lookup)
 *   - Assigned Outside Counsel (optional, organization lookup)
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors
 *   - makeStyles with semantic tokens throughout
 *   - ADR-021: dark mode support via colorNeutral/colorBrand tokens
 *   - ADR-012: shared library component, no solution-specific imports
 */
import * as React from 'react';
import { Text, Input, Textarea, Select, Field, makeStyles, tokens, } from '@fluentui/react-components';
import { LookupField } from '../../LookupField/LookupField';
// ---------------------------------------------------------------------------
// Priority option set values (sprk_priority on sprk_workassignment)
// ---------------------------------------------------------------------------
export const WORK_ASSIGNMENT_PRIORITY = {
    Low: 100000000,
    Normal: 100000001,
    High: 100000002,
    Critical: 100000003,
};
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
    twoColumn: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: tokens.spacingHorizontalM,
    },
    fullWidth: {
        width: '100%',
    },
});
// ---------------------------------------------------------------------------
// AssignWorkStep (exported)
// ---------------------------------------------------------------------------
export const AssignWorkFollowOnStep = ({ nameValue, onNameChange, descriptionValue, onDescriptionChange, matterTypeValue, onMatterTypeChange, onSearchMatterTypes, practiceAreaValue, onPracticeAreaChange, onSearchPracticeAreas, priorityValue, onPriorityChange, responseDueDateValue, onResponseDueDateChange, attorneyValue, onAttorneyChange, onSearchAttorneys, paralegalValue, onParalegalChange, onSearchParalegals, outsideCounselValue, onOutsideCounselChange, onSearchOutsideCounsel, }) => {
    const styles = useStyles();
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Assign Work"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Create a work assignment linked to this record. All fields except Name are optional.")),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Details"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement(Field, { label: "Name", required: true },
                    React.createElement(Input, { value: nameValue, onChange: (_e, data) => onNameChange(data.value), placeholder: "Enter work assignment name...", className: styles.fullWidth })),
                React.createElement(Field, { label: "Description" },
                    React.createElement(Textarea, { value: descriptionValue, onChange: (_e, data) => onDescriptionChange(data.value), placeholder: "Describe the work to be done...", rows: 3, className: styles.fullWidth })))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Classification"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement(LookupField, { label: "Matter Type", placeholder: "Search matter types...", value: matterTypeValue, onChange: onMatterTypeChange, onSearch: onSearchMatterTypes, minSearchLength: 1 }),
                React.createElement(LookupField, { label: "Practice Area", placeholder: "Search practice areas...", value: practiceAreaValue, onChange: onPracticeAreaChange, onSearch: onSearchPracticeAreas, minSearchLength: 1 }))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Scheduling"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement("div", { className: styles.twoColumn },
                    React.createElement(Field, { label: "Priority" },
                        React.createElement(Select, { value: String(priorityValue), onChange: (_e, data) => onPriorityChange(Number(data.value)), className: styles.fullWidth },
                            React.createElement("option", { value: String(WORK_ASSIGNMENT_PRIORITY.Low) }, "Low"),
                            React.createElement("option", { value: String(WORK_ASSIGNMENT_PRIORITY.Normal) }, "Normal"),
                            React.createElement("option", { value: String(WORK_ASSIGNMENT_PRIORITY.High) }, "High"),
                            React.createElement("option", { value: String(WORK_ASSIGNMENT_PRIORITY.Critical) }, "Critical"))),
                    React.createElement(Field, { label: "Response Due Date" },
                        React.createElement(Input, { type: "date", value: responseDueDateValue, onChange: (_e, data) => onResponseDueDateChange(data.value), className: styles.fullWidth }))))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 400, weight: "semibold", className: styles.sectionTitle }, "Resources"),
            React.createElement("div", { className: styles.sectionFields },
                React.createElement(LookupField, { label: "Assigned Attorney", placeholder: "Search contacts...", value: attorneyValue, onChange: onAttorneyChange, onSearch: onSearchAttorneys, minSearchLength: 2 }),
                React.createElement(LookupField, { label: "Assigned Paralegal", placeholder: "Search contacts...", value: paralegalValue, onChange: onParalegalChange, onSearch: onSearchParalegals, minSearchLength: 2 }),
                React.createElement(LookupField, { label: "Assigned Outside Counsel", placeholder: "Search organizations...", value: outsideCounselValue, onChange: onOutsideCounselChange, onSearch: onSearchOutsideCounsel, minSearchLength: 2 })))));
};
//# sourceMappingURL=AssignWorkFollowOnStep.js.map