/**
 * SendEmailStep.tsx
 * Generic email composition step for use in any wizard or multi-step form.
 *
 * Accepts configurable title, subtitle, default subject/body, and an optional
 * regarding entity reference. All domain-specific values are passed via props
 * rather than hardcoded.
 *
 * Layout:
 *   +----------------------------------------------------------------------+
 *   |  {title}                                                              |
 *   |  {subtitle}                                                           |
 *   |                                                                       |
 *   |  {headerContent}  (optional slot for extra controls above the form)   |
 *   |                                                                       |
 *   |  To *      [Search users...                             ]             |
 *   |                                                                       |
 *   |  Subject * [Pre-filled subject                          ]             |
 *   |                                                                       |
 *   |  Message * [Pre-filled body                             ]             |
 *   |                                                                       |
 *   |  {infoNote}                                                           |
 *   +----------------------------------------------------------------------+
 *
 * Constraints:
 *   - Fluent v9: Input, Textarea, Field, Text
 *   - makeStyles with semantic tokens -- ZERO hardcoded colors
 */
import * as React from 'react';
import { Field, Input, Textarea, Text, makeStyles, tokens } from '@fluentui/react-components';
import { LookupField } from './LookupField';
import { extractEmailFromUserName } from './emailHelpers';
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
// SendEmailStep (exported)
// ---------------------------------------------------------------------------
export const SendEmailStep = ({ title, subtitle, emailTo, onEmailToChange, emailSubject, onEmailSubjectChange, emailBody, onEmailBodyChange, onSearchUsers, headerContent, infoNote = 'This email will be saved as a draft activity.', messageRows = 15, }) => {
    const styles = useStyles();
    // Track the selected user lookup item for the LookupField display
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
            React.createElement(Text, { size: 200, className: styles.stepSubtitle }, subtitle)),
        headerContent,
        React.createElement("div", { className: styles.form },
            React.createElement(LookupField, { label: "To", required: true, placeholder: "Search users...", value: selectedUser, onChange: handleUserSelect, onSearch: onSearchUsers, minSearchLength: 2 }),
            React.createElement(Field, { label: renderLabel('Subject', true), required: true },
                React.createElement(Input, { value: emailSubject, onChange: e => onEmailSubjectChange(e.target.value), placeholder: "Email subject", "aria-label": "Subject" })),
            React.createElement(Field, { label: renderLabel('Message', true), required: true },
                React.createElement(Textarea, { value: emailBody, onChange: e => onEmailBodyChange(e.target.value), placeholder: "Compose your message\u2026", rows: messageRows, resize: "vertical", "aria-label": "Message body" })),
            React.createElement(Text, { size: 100, className: styles.infoNote }, infoNote))));
};
SendEmailStep.displayName = 'SendEmailStep';
//# sourceMappingURL=SendEmailStep.js.map