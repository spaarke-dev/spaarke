/**
 * SendEmailDialog.tsx
 * Reusable email composition dialog with user lookup, subject, and body fields.
 *
 * Fully callback-based — consumer provides search and send implementations.
 * No service dependencies; works in both PCF controls and Code Page solutions.
 *
 * Layout:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  Email Document                                              [X]    │
 *   │                                                                     │
 *   │  To *      [Search users...                             ]           │
 *   │                                                                     │
 *   │  Subject * [Document: Contract Agreement                ]           │
 *   │                                                                     │
 *   │  Message * [Dear Colleague,                              ]          │
 *   │            [Please find the following document...]                  │
 *   │                                                                     │
 *   │                                     [Cancel]  [Send]               │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Constraints:
 *   - Fluent v9: Dialog, Input, Textarea, Field, Button, Spinner
 *   - makeStyles with semantic tokens — ZERO hardcoded colors
 */
import * as React from 'react';
import { Dialog, DialogSurface, DialogTitle, DialogBody, DialogContent, DialogActions, Field, Input, Textarea, Button, Spinner, Text, makeStyles, tokens, } from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { LookupField } from '../LookupField/LookupField';
// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    surface: {
        maxWidth: '520px',
        width: '90vw',
    },
    form: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
        paddingTop: tokens.spacingVerticalS,
    },
    labelRow: {
        display: 'inline-flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalXXS,
    },
    requiredMark: {
        color: tokens.colorPaletteRedForeground1,
    },
    errorText: {
        color: tokens.colorPaletteRedForeground1,
        paddingTop: tokens.spacingVerticalXS,
    },
    spinnerOverlay: {
        display: 'flex',
        alignItems: 'center',
        gap: tokens.spacingHorizontalS,
    },
});
// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
export const SendEmailDialog = ({ open, onClose, defaultSubject, defaultBody, onSearchUsers, onSend, }) => {
    const styles = useStyles();
    // Form state
    const [selectedUser, setSelectedUser] = React.useState(null);
    const [subject, setSubject] = React.useState('');
    const [body, setBody] = React.useState('');
    const [sending, setSending] = React.useState(false);
    const [error, setError] = React.useState(null);
    // Reset form when dialog opens with new defaults
    React.useEffect(() => {
        if (open) {
            setSelectedUser(null);
            setSubject(defaultSubject ?? '');
            setBody(defaultBody ?? '');
            setSending(false);
            setError(null);
        }
    }, [open, defaultSubject, defaultBody]);
    // ── Handlers ──────────────────────────────────────────────────────────
    const handleSend = React.useCallback(async () => {
        if (!selectedUser)
            return;
        if (!subject.trim())
            return;
        setSending(true);
        setError(null);
        try {
            await onSend({ to: selectedUser, subject: subject.trim(), body });
            onClose();
        }
        catch (err) {
            console.error('[SendEmailDialog] Send failed:', err);
            setError(err instanceof Error ? err.message : 'Failed to send email. Please try again.');
        }
        finally {
            setSending(false);
        }
    }, [selectedUser, subject, body, onSend, onClose]);
    const canSend = !!selectedUser && !!subject.trim() && !sending;
    const renderLabel = (text, required) => (React.createElement("span", { className: styles.labelRow },
        text,
        required && (React.createElement("span", { "aria-hidden": "true", className: styles.requiredMark }, ' *'))));
    return (React.createElement(Dialog, { open: open, onOpenChange: (_, data) => {
            if (!data.open)
                onClose();
        } },
        React.createElement(DialogSurface, { className: styles.surface },
            React.createElement(DialogTitle, { action: React.createElement(Button, { appearance: "subtle", icon: React.createElement(Dismiss24Regular, null), "aria-label": "Close", onClick: onClose }) }, "Email Document"),
            React.createElement(DialogBody, null,
                React.createElement(DialogContent, null,
                    React.createElement("div", { className: styles.form },
                        React.createElement(LookupField, { label: "To", required: true, placeholder: "Search users...", value: selectedUser, onChange: setSelectedUser, onSearch: onSearchUsers, minSearchLength: 2 }),
                        React.createElement(Field, { label: renderLabel('Subject', true), required: true },
                            React.createElement(Input, { value: subject, onChange: e => setSubject(e.target.value), placeholder: "Email subject", "aria-label": "Subject", disabled: sending })),
                        React.createElement(Field, { label: renderLabel('Message') },
                            React.createElement(Textarea, { value: body, onChange: e => setBody(e.target.value), placeholder: "Compose your message...", rows: 10, resize: "vertical", "aria-label": "Message body", disabled: sending })),
                        error && (React.createElement(Text, { size: 200, className: styles.errorText }, error))))),
            React.createElement(DialogActions, null,
                React.createElement(Button, { appearance: "secondary", onClick: onClose, disabled: sending }, "Cancel"),
                React.createElement(Button, { appearance: "primary", onClick: handleSend, disabled: !canSend }, sending ? (React.createElement("span", { className: styles.spinnerOverlay },
                    React.createElement(Spinner, { size: "tiny" }),
                    "Sending...")) : ('Send'))))));
};
SendEmailDialog.displayName = 'SendEmailDialog';
//# sourceMappingURL=SendEmailDialog.js.map