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
import type { IPendingAction } from './types';
export interface IActionConfirmationDialogProps {
    /** The pending action to confirm. When null/undefined, the dialog is hidden. */
    pendingAction: IPendingAction | null;
    /** Callback fired when the user confirms the action. */
    onConfirm: (action: IPendingAction) => void;
    /** Callback fired when the user cancels (dismisses without executing). */
    onCancel: () => void;
    /** Whether the confirmation action is currently being dispatched. */
    isConfirming?: boolean;
}
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
export declare const ActionConfirmationDialog: React.FC<IActionConfirmationDialogProps>;
export default ActionConfirmationDialog;
//# sourceMappingURL=ActionConfirmationDialog.d.ts.map