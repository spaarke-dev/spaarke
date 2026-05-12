/**
 * CreateEventStep.tsx
 * Entity-specific form for "Create New Event" wizard.
 *
 * Fields:
 *   - Event Name (required, Input)
 *   - Event Type (LookupField -> sprk_eventtype_ref)
 *   - Due Date (Input type="date")
 *   - Priority (Dropdown: Low/Normal/High/Urgent)
 *   - Description (Textarea)
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *
 * @see IDataService — high-level data access abstraction
 */
import * as React from 'react';
import { Text, Input, Textarea, Dropdown, Option, Field, makeStyles, tokens, } from '@fluentui/react-components';
import { LookupField } from '../LookupField/LookupField';
import { EventService } from './eventService';
import { EMPTY_EVENT_FORM } from './formTypes';
// ---------------------------------------------------------------------------
// Priority options
// ---------------------------------------------------------------------------
const PRIORITY_OPTIONS = [
    { key: 100000000, text: 'Low' },
    { key: 100000001, text: 'Normal' },
    { key: 100000002, text: 'High' },
    { key: 100000003, text: 'Urgent' },
];
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    stepTitle: {
        color: tokens.colorNeutralForeground1,
        marginBottom: tokens.spacingVerticalXS,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
        marginBottom: tokens.spacingVerticalM,
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
export const CreateEventStep = ({ dataService, onValidChange, onFormValues, initialFormValues, }) => {
    const styles = useStyles();
    const [formValues, setFormValues] = React.useState(initialFormValues ?? EMPTY_EVENT_FORM);
    const serviceRef = React.useRef(null);
    if (!serviceRef.current) {
        serviceRef.current = new EventService(dataService);
    }
    // Report validity whenever form changes
    React.useEffect(() => {
        const isValid = formValues.eventName.trim().length > 0;
        onValidChange(isValid);
        onFormValues(formValues);
    }, [formValues, onValidChange, onFormValues]);
    // -- Field handlers --------------------------------------------------------
    const handleNameChange = React.useCallback((e) => {
        setFormValues((prev) => ({ ...prev, eventName: e.target.value }));
    }, []);
    const handleEventTypeChange = React.useCallback((item) => {
        setFormValues((prev) => ({
            ...prev,
            eventTypeId: item?.id ?? '',
            eventTypeName: item?.name ?? '',
        }));
    }, []);
    const handleSearchEventTypes = React.useCallback((query) => serviceRef.current.searchEventTypes(query), []);
    const handleDueDateChange = React.useCallback((e) => {
        setFormValues((prev) => ({ ...prev, dueDate: e.target.value }));
    }, []);
    const handlePriorityChange = React.useCallback((_e, data) => {
        const val = parseInt(data.optionValue ?? '100000001', 10);
        setFormValues((prev) => ({ ...prev, priority: val }));
    }, []);
    const handleDescriptionChange = React.useCallback((e) => {
        setFormValues((prev) => ({ ...prev, description: e.target.value }));
    }, []);
    // -- Render ----------------------------------------------------------------
    const eventTypeValue = formValues.eventTypeId
        ? { id: formValues.eventTypeId, name: formValues.eventTypeName }
        : null;
    const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === formValues.priority)?.text ?? 'Normal';
    return (React.createElement("div", { className: styles.form },
        React.createElement("div", null,
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Event Details"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Enter the details for the new event.")),
        React.createElement(Field, { label: "Event Name", required: true },
            React.createElement(Input, { value: formValues.eventName, onChange: handleNameChange, placeholder: "Enter event name", autoComplete: "off" })),
        React.createElement(LookupField, { label: "Event Type", value: eventTypeValue, onChange: handleEventTypeChange, onSearch: handleSearchEventTypes, placeholder: "Search event types..." }),
        React.createElement("div", { className: styles.row },
            React.createElement(Field, { label: "Due Date" },
                React.createElement(Input, { type: "date", value: formValues.dueDate, onChange: handleDueDateChange })),
            React.createElement(Field, { label: "Priority" },
                React.createElement(Dropdown, { value: selectedPriorityText, selectedOptions: [String(formValues.priority)], onOptionSelect: handlePriorityChange }, PRIORITY_OPTIONS.map((opt) => (React.createElement(Option, { key: opt.key, value: String(opt.key) }, opt.text)))))),
        React.createElement(Field, { label: "Description" },
            React.createElement(Textarea, { value: formValues.description, onChange: handleDescriptionChange, placeholder: "Describe the event...", rows: 4, resize: "vertical" }))));
};
//# sourceMappingURL=CreateEventStep.js.map