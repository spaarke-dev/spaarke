/**
 * AssignWorkStep.tsx
 * Follow-on step: "Assign Work" -- assign internal resources and law firm.
 *
 * Sections:
 *   - Internal Resources: Assigned Attorney (contact), Assigned Paralegal (contact)
 *   - Assigned Law Firm: Law Firm (organization), Law Firm Attorney (contact filtered by firm)
 *   - Notify assigned resources checkbox
 *
 * Dependencies are injected via props -- no solution-specific imports.
 */
import * as React from 'react';
import { Text, Checkbox, makeStyles, tokens, } from '@fluentui/react-components';
import { LookupField } from '../LookupField/LookupField';
import { searchContactsAsLookup, searchOrganizationsAsLookup, } from './workAssignmentService';
import { WorkAssignmentService } from './workAssignmentService';
import { EMPTY_ASSIGN_WORK_STATE } from './formTypes';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalL,
    },
    headerText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXS,
        marginBottom: tokens.spacingVerticalM,
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
    sectionLabel: {
        color: tokens.colorNeutralForeground2,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        paddingBottom: tokens.spacingVerticalXS,
    },
    row: {
        display: 'grid',
        gridTemplateColumns: '1fr 1fr',
        gap: tokens.spacingHorizontalM,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const AssignWorkStep = ({ dataService, authenticatedFetch, bffBaseUrl, containerId, onFormValues, initialValues, }) => {
    const styles = useStyles();
    const [formValues, setFormValues] = React.useState(initialValues ?? EMPTY_ASSIGN_WORK_STATE);
    const serviceRef = React.useRef(null);
    if (!serviceRef.current) {
        serviceRef.current = new WorkAssignmentService(dataService, authenticatedFetch, bffBaseUrl, containerId);
    }
    React.useEffect(() => {
        onFormValues(formValues);
    }, [formValues, onFormValues]);
    // -- Internal Resources ----------------------------------------------------
    const handleAttorneyChange = React.useCallback((item) => {
        setFormValues((prev) => ({
            ...prev,
            assignedAttorneyId: item?.id ?? '',
            assignedAttorneyName: item?.name ?? '',
        }));
    }, []);
    const handleSearchAttorneys = React.useCallback((query) => searchContactsAsLookup(dataService, query), [dataService]);
    const handleParalegalChange = React.useCallback((item) => {
        setFormValues((prev) => ({
            ...prev,
            assignedParalegalId: item?.id ?? '',
            assignedParalegalName: item?.name ?? '',
        }));
    }, []);
    const handleSearchParalegals = React.useCallback((query) => searchContactsAsLookup(dataService, query), [dataService]);
    // -- Law Firm --------------------------------------------------------------
    const handleLawFirmChange = React.useCallback((item) => {
        setFormValues((prev) => ({
            ...prev,
            assignedLawFirmId: item?.id ?? '',
            assignedLawFirmName: item?.name ?? '',
            // Clear attorney when firm changes
            assignedLawFirmAttorneyId: '',
            assignedLawFirmAttorneyName: '',
        }));
    }, []);
    const handleSearchLawFirms = React.useCallback((query) => searchOrganizationsAsLookup(dataService, query), [dataService]);
    const handleLawFirmAttorneyChange = React.useCallback((item) => {
        setFormValues((prev) => ({
            ...prev,
            assignedLawFirmAttorneyId: item?.id ?? '',
            assignedLawFirmAttorneyName: item?.name ?? '',
        }));
    }, []);
    const handleSearchLawFirmAttorneys = React.useCallback((query) => {
        if (!formValues.assignedLawFirmId)
            return Promise.resolve([]);
        return serviceRef.current.searchContactsByOrganization(formValues.assignedLawFirmId, query);
    }, [formValues.assignedLawFirmId]);
    // -- Notify ----------------------------------------------------------------
    const handleNotifyChange = React.useCallback((_e, data) => {
        setFormValues((prev) => ({ ...prev, notifyResources: data.checked === true }));
    }, []);
    // -- Render ----------------------------------------------------------------
    const attorneyValue = formValues.assignedAttorneyId
        ? { id: formValues.assignedAttorneyId, name: formValues.assignedAttorneyName }
        : null;
    const paralegalValue = formValues.assignedParalegalId
        ? { id: formValues.assignedParalegalId, name: formValues.assignedParalegalName }
        : null;
    const lawFirmValue = formValues.assignedLawFirmId
        ? { id: formValues.assignedLawFirmId, name: formValues.assignedLawFirmName }
        : null;
    const lawFirmAttorneyValue = formValues.assignedLawFirmAttorneyId
        ? { id: formValues.assignedLawFirmAttorneyId, name: formValues.assignedLawFirmAttorneyName }
        : null;
    return (React.createElement("div", { className: styles.form },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Assign Work"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Assign internal resources and law firm to this work assignment.")),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 300, weight: "semibold", className: styles.sectionLabel }, "Internal Resources"),
            React.createElement("div", { className: styles.row },
                React.createElement(LookupField, { label: "Assigned Attorney", value: attorneyValue, onChange: handleAttorneyChange, onSearch: handleSearchAttorneys, placeholder: "Search contacts..." }),
                React.createElement(LookupField, { label: "Assigned Paralegal", value: paralegalValue, onChange: handleParalegalChange, onSearch: handleSearchParalegals, placeholder: "Search contacts..." }))),
        React.createElement("div", { className: styles.section },
            React.createElement(Text, { size: 300, weight: "semibold", className: styles.sectionLabel }, "Assigned Law Firm"),
            React.createElement(LookupField, { label: "Law Firm", value: lawFirmValue, onChange: handleLawFirmChange, onSearch: handleSearchLawFirms, placeholder: "Search organizations..." }),
            React.createElement(LookupField, { label: "Law Firm Attorney", value: lawFirmAttorneyValue, onChange: handleLawFirmAttorneyChange, onSearch: handleSearchLawFirmAttorneys, placeholder: formValues.assignedLawFirmId ? 'Search contacts at firm...' : 'Select a law firm first' })),
        React.createElement(Checkbox, { checked: formValues.notifyResources, onChange: handleNotifyChange, label: "Notify assigned resources" })));
};
//# sourceMappingURL=AssignWorkStep.js.map