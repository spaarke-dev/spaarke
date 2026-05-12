/**
 * CreateTodoStep.tsx
 * Entity-specific form for "Create New To Do" wizard.
 *
 * Simpler form than Event: Title, Due Date, Priority, Description.
 *
 * Accepts IDataService (not IWebApi) for shared library portability.
 */
import * as React from 'react';
import { Text, Input, Textarea, Dropdown, Option, Field, makeStyles, tokens, } from '@fluentui/react-components';
import { EMPTY_TODO_FORM } from './formTypes';
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
export const CreateTodoStep = ({ dataService: _dataService, onValidChange, onFormValues, initialFormValues, }) => {
    const styles = useStyles();
    const [formValues, setFormValues] = React.useState(initialFormValues ?? EMPTY_TODO_FORM);
    React.useEffect(() => {
        const isValid = formValues.title.trim().length > 0;
        onValidChange(isValid);
        onFormValues(formValues);
    }, [formValues, onValidChange, onFormValues]);
    const handleTitleChange = React.useCallback((e) => {
        setFormValues((prev) => ({ ...prev, title: e.target.value }));
    }, []);
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
    const selectedPriorityText = PRIORITY_OPTIONS.find((o) => o.key === formValues.priority)?.text ?? 'Normal';
    return (React.createElement("div", { className: styles.form },
        React.createElement("div", null,
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "To Do Details"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Enter the details for the new to do item.")),
        React.createElement(Field, { label: "Title", required: true },
            React.createElement(Input, { value: formValues.title, onChange: handleTitleChange, placeholder: "What needs to be done?", autoComplete: "off" })),
        React.createElement("div", { className: styles.row },
            React.createElement(Field, { label: "Due Date" },
                React.createElement(Input, { type: "date", value: formValues.dueDate, onChange: handleDueDateChange })),
            React.createElement(Field, { label: "Priority" },
                React.createElement(Dropdown, { value: selectedPriorityText, selectedOptions: [String(formValues.priority)], onOptionSelect: handlePriorityChange }, PRIORITY_OPTIONS.map((opt) => (React.createElement(Option, { key: opt.key, value: String(opt.key) }, opt.text)))))),
        React.createElement(Field, { label: "Description" },
            React.createElement(Textarea, { value: formValues.description, onChange: handleDescriptionChange, placeholder: "Add notes or details...", rows: 3, resize: "vertical" }))));
};
//# sourceMappingURL=CreateTodoStep.js.map