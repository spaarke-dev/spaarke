/**
 * Choice Dialog Component
 *
 * Rich option button dialog for presenting 2-4 mutually exclusive choices.
 * Each option displays icon, title, and description for clear user understanding.
 *
 * Standards: ADR-023 Choice Dialog Pattern, ADR-021 Fluent UI v9
 */
import * as React from 'react';
/**
 * Represents a single choice option in the dialog.
 */
export interface IChoiceDialogOption {
    /** Unique identifier for this option */
    id: string;
    /** Icon component (24px recommended) */
    icon: React.ReactNode;
    /** Short, action-oriented title */
    title: string;
    /** Explanation of what happens when chosen */
    description: string;
    /** Optional: disable this option */
    disabled?: boolean;
}
/**
 * Props for the ChoiceDialog component.
 */
export interface IChoiceDialogProps {
    /** Whether the dialog is open */
    open: boolean;
    /** Dialog title */
    title: string;
    /** Contextual message explaining the situation */
    message: string | React.ReactNode;
    /** Array of 2-4 choice options */
    options: IChoiceDialogOption[];
    /** Callback when user selects an option */
    onSelect: (optionId: string) => void;
    /** Callback when dialog is dismissed */
    onDismiss: () => void;
    /** Optional: text for cancel button (default: "Cancel") */
    cancelText?: string;
}
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
export declare const ChoiceDialog: React.FC<IChoiceDialogProps>;
export default ChoiceDialog;
//# sourceMappingURL=ChoiceDialog.d.ts.map