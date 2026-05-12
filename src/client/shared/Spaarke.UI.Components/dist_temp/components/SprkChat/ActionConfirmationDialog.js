/**
 * ActionConfirmationDialog - HITL confirmation dialog for playbook actions
 *
 * Renders a Fluent v9 Dialog when a playbook action has requiresConfirmation=true.
 * Shows the proposed action summary, extracted parameters, and Confirm/Cancel buttons.
 *
 * On Confirm: dispatches the action to the BFF for execution.
 * On Cancel: clears the pending action state without side effects.
 *
 * @see spec-FR-07 — HITL confirmation dialog
 * @see ADR-021 — Fluent v9 Dialog component (not window.confirm)
 * @see ADR-022 — React 16 APIs only
 */
import * as React from 'react';
import { makeStyles, shorthands, tokens, Button, Text, Divider, Spinner, } from '@fluentui/react-components';
import { CheckmarkRegular, DismissRegular, ShieldCheckmarkRegular, } from '@fluentui/react-icons';
// ─────────────────────────────────────────────────────────────────────────────
// Styles (Fluent v9 makeStyles — ADR-021)
// ─────────────────────────────────────────────────────────────────────────────
const useStyles = makeStyles({
    overlay: {
        position: 'absolute',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: tokens.colorBackgroundOverlay,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
    },
    dialog: {
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderRadius(tokens.borderRadiusXLarge),
        boxShadow: tokens.shadow16,
        ...shorthands.padding(tokens.spacingVerticalXL, tokens.spacingHorizontalXL),
        maxWidth: '480px',
        width: '90%',
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.gap(tokens.spacingVerticalM),
    },
    header: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap(tokens.spacingHorizontalS),
    },
    headerIcon: {
        color: tokens.colorBrandForeground1,
        fontSize: '24px',
    },
    title: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: tokens.fontSizeBase400,
        color: tokens.colorNeutralForeground1,
    },
    summary: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase300,
        lineHeight: tokens.lineHeightBase300,
    },
    parametersSection: {
        backgroundColor: tokens.colorNeutralBackground2,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.gap(tokens.spacingVerticalXS),
    },
    parameterRow: {
        display: 'flex',
        ...shorthands.gap(tokens.spacingHorizontalS),
        fontSize: tokens.fontSizeBase200,
    },
    parameterLabel: {
        color: tokens.colorNeutralForeground3,
        fontWeight: tokens.fontWeightSemibold,
        minWidth: '100px',
        flexShrink: 0,
    },
    parameterValue: {
        color: tokens.colorNeutralForeground1,
        wordBreak: 'break-word',
    },
    actions: {
        display: 'flex',
        justifyContent: 'flex-end',
        ...shorthands.gap(tokens.spacingHorizontalS),
        ...shorthands.padding(tokens.spacingVerticalXS, '0px'),
    },
});
// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────
/**
 * ActionConfirmationDialog renders an inline card-style dialog (positioned absolutely
 * within the SprkChat container) for HITL action confirmation.
 *
 * Uses Fluent v9 design tokens for colors, spacing, and typography.
 * Does NOT use window.confirm() or custom modal (per ADR-021 constraint).
 *
 * @example
 * ```tsx
 * <ActionConfirmationDialog
 *   pendingAction={pendingAction}
 *   onConfirm={handleConfirm}
 *   onCancel={handleCancel}
 *   isConfirming={isConfirming}
 * />
 * ```
 */
export const ActionConfirmationDialog = ({ pendingAction, onConfirm, onCancel, isConfirming = false, }) => {
    const styles = useStyles();
    // Handle keyboard: Escape to cancel
    const handleKeyDown = React.useCallback((e) => {
        if (e.key === 'Escape' && !isConfirming) {
            onCancel();
        }
    }, [onCancel, isConfirming]);
    // Handle confirm click
    const handleConfirm = React.useCallback(() => {
        if (pendingAction && !isConfirming) {
            onConfirm(pendingAction);
        }
    }, [pendingAction, onConfirm, isConfirming]);
    // Don't render when there's no pending action
    if (!pendingAction) {
        return null;
    }
    const paramEntries = Object.entries(pendingAction.parameters);
    return (React.createElement("div", { className: styles.overlay, role: "dialog", "aria-modal": "true", "aria-label": `Confirm action: ${pendingAction.actionName}`, "data-testid": "action-confirmation-dialog", onKeyDown: handleKeyDown },
        React.createElement("div", { className: styles.dialog },
            React.createElement("div", { className: styles.header },
                React.createElement("span", { className: styles.headerIcon }, React.createElement(ShieldCheckmarkRegular)),
                React.createElement(Text, { className: styles.title },
                    "Confirm Action: ",
                    pendingAction.actionName)),
            React.createElement(Text, { className: styles.summary }, pendingAction.summary),
            paramEntries.length > 0 && (React.createElement(React.Fragment, null,
                React.createElement(Divider, null),
                React.createElement("div", { className: styles.parametersSection, "data-testid": "action-parameters" }, paramEntries.map(([key, value]) => (React.createElement("div", { key: key, className: styles.parameterRow },
                    React.createElement("span", { className: styles.parameterLabel },
                        key,
                        ":"),
                    React.createElement("span", { className: styles.parameterValue }, value))))))),
            React.createElement(Divider, null),
            React.createElement("div", { className: styles.actions },
                React.createElement(Button, { appearance: "secondary", icon: React.createElement(DismissRegular), onClick: onCancel, disabled: isConfirming, "data-testid": "action-cancel-button" }, "Cancel"),
                React.createElement(Button, { appearance: "primary", icon: isConfirming ? React.createElement(Spinner, { size: 'tiny' }) : React.createElement(CheckmarkRegular), onClick: handleConfirm, disabled: isConfirming, "data-testid": "action-confirm-button" }, isConfirming ? 'Confirming...' : 'Confirm')))));
};
export default ActionConfirmationDialog;
//# sourceMappingURL=ActionConfirmationDialog.js.map