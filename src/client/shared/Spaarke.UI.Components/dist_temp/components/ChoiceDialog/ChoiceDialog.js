/**
 * Choice Dialog Component
 *
 * Rich option button dialog for presenting 2-4 mutually exclusive choices.
 * Each option displays icon, title, and description for clear user understanding.
 *
 * Standards: ADR-023 Choice Dialog Pattern, ADR-021 Fluent UI v9
 */
import * as React from 'react';
import { Dialog, DialogSurface, DialogTitle, DialogBody, DialogActions, DialogContent, Button, Text, makeStyles, tokens, } from '@fluentui/react-components';
// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    content: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM,
    },
    optionsContainer: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalS,
        marginTop: tokens.spacingVerticalM,
    },
    optionButton: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'flex-start',
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        width: '100%',
        textAlign: 'left',
        minHeight: '64px', // Accessibility: minimum touch target
    },
    optionIcon: {
        fontSize: '24px',
        color: tokens.colorBrandForeground1,
        flexShrink: 0, // Prevent icon from shrinking
    },
    optionText: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalXXS,
        overflow: 'hidden', // Handle long text
    },
    optionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    optionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200,
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────
/**
 * ChoiceDialog - Rich option button dialog for 2-4 choices.
 *
 * @example
 * ```tsx
 * const options: IChoiceDialogOption[] = [
 *     { id: "resume", icon: <HistoryRegular />, title: "Resume", description: "Continue where you left off" },
 *     { id: "fresh", icon: <DocumentAddRegular />, title: "Start Fresh", description: "Begin new session" }
 * ];
 *
 * <ChoiceDialog
 *     open={open}
 *     title="Resume Session?"
 *     message="This analysis has existing history."
 *     options={options}
 *     onSelect={(id) => handleSelection(id)}
 *     onDismiss={() => setOpen(false)}
 * />
 * ```
 */
export const ChoiceDialog = ({ open, title, message, options, onSelect, onDismiss, cancelText = 'Cancel', }) => {
    const styles = useStyles();
    const handleOptionClick = React.useCallback((optionId) => {
        onSelect(optionId);
    }, [onSelect]);
    return (React.createElement(Dialog, { open: open, onOpenChange: (_, data) => !data.open && onDismiss() },
        React.createElement(DialogSurface, null,
            React.createElement(DialogBody, null,
                React.createElement(DialogTitle, null, title),
                React.createElement(DialogContent, { className: styles.content },
                    typeof message === 'string' ? React.createElement(Text, null, message) : message,
                    React.createElement("div", { className: styles.optionsContainer }, options.map(option => (React.createElement(Button, { key: option.id, appearance: "outline", className: styles.optionButton, disabled: option.disabled, onClick: () => handleOptionClick(option.id), "aria-describedby": `option-desc-${option.id}` },
                        React.createElement("span", { className: styles.optionIcon }, option.icon),
                        React.createElement("div", { className: styles.optionText },
                            React.createElement("span", { className: styles.optionTitle }, option.title),
                            React.createElement("span", { id: `option-desc-${option.id}`, className: styles.optionDescription }, option.description))))))),
                React.createElement(DialogActions, null,
                    React.createElement(Button, { appearance: "secondary", onClick: onDismiss }, cancelText))))));
};
export default ChoiceDialog;
//# sourceMappingURL=ChoiceDialog.js.map