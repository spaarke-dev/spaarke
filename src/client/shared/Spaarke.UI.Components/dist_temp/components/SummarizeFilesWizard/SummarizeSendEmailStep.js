/**
 * SummarizeSendEmailStep.tsx
 * Follow-on step for "Send Email" in the Summarize Files wizard.
 *
 * Adapts the CreateMatter/SendEmailStep pattern:
 *   - To (LookupField -> searchUsersAsLookup)
 *   - Subject (Input)
 *   - Body (Textarea, 15 rows, pre-filled with summary)
 *   - "Include only short summary" checkbox at top
 */
import * as React from 'react';
import { Checkbox, Field, Input, Textarea, Text, makeStyles, tokens, } from '@fluentui/react-components';
import { LookupField } from '../LookupField';
// ---------------------------------------------------------------------------
// Template builders
// ---------------------------------------------------------------------------
export function buildSummaryEmailSubject() {
    return 'Document Summary';
}
export function buildSummaryEmailBody(summary, shortSummary, useShort) {
    const summaryContent = useShort ? shortSummary : summary;
    return (`Dear Colleague,\n\n` +
        `Please find the AI-generated summary of the uploaded documents below.\n\n` +
        `Kind regards,\n` +
        `[Your Name]\n\n` +
        `────────────────────────────────────\n\n` +
        summaryContent);
}
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
    stepTitle: {
        color: tokens.colorNeutralForeground1,
    },
    stepSubtitle: {
        color: tokens.colorNeutralForeground3,
    },
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
    requiredMark: {
        color: tokens.colorPaletteRedForeground1,
    },
    infoNote: {
        color: tokens.colorNeutralForeground3,
        paddingTop: tokens.spacingVerticalXS,
    },
});
// ---------------------------------------------------------------------------
// SummarizeSendEmailStep (exported)
// ---------------------------------------------------------------------------
export const SummarizeSendEmailStep = ({ emailTo, onEmailToChange, emailSubject, onEmailSubjectChange, emailBody, onEmailBodyChange, onSearchUsers, includeShortSummary, onIncludeShortSummaryChange, }) => {
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
            React.createElement(Text, { as: "h2", size: 500, weight: "semibold", className: styles.stepTitle }, "Send Email"),
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, "Compose an email with the file summary. It will be sent via the system.")),
        React.createElement(Checkbox, { checked: includeShortSummary, onChange: (_e, data) => onIncludeShortSummaryChange(!!data.checked), label: "Include only short summary" }),
        React.createElement("div", { className: styles.form },
            React.createElement(LookupField, { label: "To", required: true, placeholder: "Search users...", value: selectedUser, onChange: handleUserSelect, onSearch: onSearchUsers, minSearchLength: 2 }),
            React.createElement(Field, { label: renderLabel('Subject', true), required: true },
                React.createElement(Input, { value: emailSubject, onChange: (e) => onEmailSubjectChange(e.target.value), placeholder: "Email subject", "aria-label": "Subject" })),
            React.createElement(Field, { label: renderLabel('Message', true), required: true },
                React.createElement(Textarea, { value: emailBody, onChange: (e) => onEmailBodyChange(e.target.value), placeholder: "Compose your message\u2026", rows: 15, resize: "vertical", "aria-label": "Message body" })),
            React.createElement(Text, { size: 100, className: styles.infoNote }, "This email will be sent via the BFF communication endpoint."))));
};
//# sourceMappingURL=SummarizeSendEmailStep.js.map