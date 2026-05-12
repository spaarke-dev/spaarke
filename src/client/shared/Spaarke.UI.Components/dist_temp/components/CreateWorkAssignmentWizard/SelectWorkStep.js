/**
 * SelectWorkStep.tsx
 * Step 1: "Work to Assign" -- select the entity record this work relates to.
 *
 * Uses DataverseLookupField for record selection (same pattern as
 * AssociateToStep, CreateMatter, CreateProject).
 *
 * UAT pattern:
 *   - Record Type dropdown + DataverseLookupField (Xrm.Utility.lookupObjects)
 *   - Selected record display via DataverseLookupField chip
 *   - Step is marked isSkippable: true so the footer Skip button appears
 *   - Next is only enabled when a record is selected
 */
import * as React from 'react';
import { Text, Dropdown, Option, makeStyles, tokens, } from '@fluentui/react-components';
import { DataverseLookupField } from '../LookupField/DataverseLookupField';
// ---------------------------------------------------------------------------
// Record Type options
// ---------------------------------------------------------------------------
const RECORD_TYPE_OPTIONS = [
    { key: 'matter', text: 'Matter', entityLogicalName: 'sprk_matter' },
    { key: 'project', text: 'Project', entityLogicalName: 'sprk_project' },
    { key: 'invoice', text: 'Invoice', entityLogicalName: 'sprk_invoice' },
    { key: 'event', text: 'Event', entityLogicalName: 'sprk_event' },
];
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
    formRow: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    dropdownWrapper: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        maxWidth: '300px',
    },
    fieldLabel: {
        color: tokens.colorNeutralForeground2,
    },
    skipHint: {
        color: tokens.colorNeutralForeground3,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const SelectWorkStep = ({ onValidChange, onFormValues, initialValues, navigationService, }) => {
    const styles = useStyles();
    const [recordType, setRecordType] = React.useState(initialValues?.recordType ?? '');
    const [recordValue, setRecordValue] = React.useState(initialValues?.recordId ? { id: initialValues.recordId, name: initialValues.recordName ?? '' } : null);
    // Report validity + values — Next is enabled only when a record is selected
    React.useEffect(() => {
        const isValid = recordValue !== null;
        onValidChange(isValid);
        onFormValues({
            recordType,
            recordId: recordValue?.id ?? '',
            recordName: recordValue?.name ?? '',
        });
    }, [recordType, recordValue, onValidChange, onFormValues]);
    const handleRecordTypeChange = React.useCallback((_e, data) => {
        const val = (data.optionValue ?? '');
        setRecordType(val);
        // Clear previous selection when entity type changes
        if (recordValue) {
            setRecordValue(null);
        }
    }, [recordValue]);
    const handleRecordChange = React.useCallback((item) => {
        setRecordValue(item);
    }, []);
    const selectedOption = RECORD_TYPE_OPTIONS.find((o) => o.key === recordType);
    const selectedTypeText = selectedOption?.text ?? '';
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Work to Assign"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Select the subject matter that is to be assigned for work responsibility.")),
        React.createElement("div", { className: styles.formRow },
            React.createElement("div", { className: styles.dropdownWrapper },
                React.createElement(Text, { size: 200, weight: "semibold", className: styles.fieldLabel }, "Record Type"),
                React.createElement(Dropdown, { value: selectedTypeText, selectedOptions: recordType ? [recordType] : [], onOptionSelect: handleRecordTypeChange, placeholder: "Select record type..." }, RECORD_TYPE_OPTIONS.map((opt) => (React.createElement(Option, { key: opt.key, value: opt.key }, opt.text))))),
            recordType && selectedOption && (React.createElement(DataverseLookupField, { label: selectedTypeText, entityType: selectedOption.entityLogicalName, value: recordValue, onChange: handleRecordChange, navigationService: navigationService, placeholder: `Search ${selectedTypeText.toLowerCase()}s...` }))),
        React.createElement(Text, { size: 200, className: styles.skipHint }, "You can always link to a record later. Use the Skip button to create the work assignment without a parent record.")));
};
//# sourceMappingURL=SelectWorkStep.js.map