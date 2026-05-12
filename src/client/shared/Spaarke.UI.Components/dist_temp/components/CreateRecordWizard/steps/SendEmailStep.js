/**
 * SendEmailStep.tsx
 * Follow-on step for composing an email to client.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library.
 * Entity-specific form values are no longer referenced — email pre-fill
 * is handled by the CreateRecordWizard via config callbacks.
 *
 * @see CreateRecordWizard — pre-fills emailSubject/emailBody from config
 */
import * as React from 'react';
import { Field, Input, Textarea, Text, makeStyles, tokens } from '@fluentui/react-components';
import { LookupField } from '../../LookupField/LookupField';
// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
function extractEmailFromUserName(name) {
    const match = name.match(/\(([^)]+@[^)]+)\)/);
    return match ? match[1] : '';
}
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
    stepTitle: { color: tokens.colorNeutralForeground1 },
    stepSubtitle: { color: tokens.colorNeutralForeground3 },
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    labelRow: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    requiredMark: { color: tokens.colorPaletteRedForeground1 },
    infoNote: {
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalXS,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const SendEmailStep = ({ title = 'Send Notification Email', emailTo: _emailTo, onEmailToChange, emailCc, onEmailCcChange, emailSubject, onEmailSubjectChange, emailBody, onEmailBodyChange, onSearchUsers, }) => {
    const styles = useStyles();
    const [selectedUser, setSelectedUser] = React.useState(null);
    const handleUserSelect = React.useCallback((item) => {
        setSelectedUser(item);
        if (item) {
            const email = extractEmailFromUserName(item.name);
            onEmailToChange(email || item.name);
        }
        else {
            onEmailToChange('');
        }
    }, [onEmailToChange]);
    const renderLabel = (text, required) => (React.createElement("span", { className: styles.labelRow },
        text,
        required && (React.createElement("span", { "aria-hidden": "true", className: styles.requiredMark }, ' *'))));
    return (React.createElement("div", { className: styles.root },
        React.createElement("div", { className: styles.headerText },
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, title),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Compose an introductory email. It will be created as an email activity in Dataverse, linked to this record.")),
        React.createElement("div", { className: styles.form },
            React.createElement(LookupField, { label: "To", required: true, placeholder: "Search users...", value: selectedUser, onChange: handleUserSelect, onSearch: onSearchUsers, minSearchLength: 2 }),
            onEmailCcChange && (React.createElement(Field, { label: "CC" },
                React.createElement(Input, { value: emailCc ?? '', onChange: e => onEmailCcChange(e.target.value), placeholder: "CC email addresses (separate with ;)", "aria-label": "CC" }))),
            React.createElement(Field, { label: renderLabel('Subject', true), required: true },
                React.createElement(Input, { value: emailSubject, onChange: e => onEmailSubjectChange(e.target.value), placeholder: "Email subject", "aria-label": "Subject" })),
            React.createElement(Field, { label: renderLabel('Message', true), required: true },
                React.createElement(Textarea, { value: emailBody, onChange: e => onEmailBodyChange(e.target.value), placeholder: "Compose your message\u2026", rows: 15, resize: "vertical", "aria-label": "Message body" })),
            React.createElement(Text, { size: 100, className: styles.infoNote }, "This email will be saved as a draft activity on the record. You can review and send it from there."))));
};
//# sourceMappingURL=SendEmailStep.js.map