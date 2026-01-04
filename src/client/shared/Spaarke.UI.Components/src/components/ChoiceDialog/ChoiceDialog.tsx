/**
 * Choice Dialog Component
 *
 * Rich option button dialog for presenting 2-4 mutually exclusive choices.
 * Each option displays icon, title, and description for clear user understanding.
 *
 * Standards: ADR-023 Choice Dialog Pattern, ADR-021 Fluent UI v9
 */

import * as React from "react";
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button,
    Text,
    makeStyles,
    tokens
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

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

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    content: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM
    },
    optionsContainer: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        marginTop: tokens.spacingVerticalM
    },
    optionButton: {
        display: "flex",
        alignItems: "center",
        justifyContent: "flex-start",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        width: "100%",
        textAlign: "left",
        minHeight: "64px" // Accessibility: minimum touch target
    },
    optionIcon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1,
        flexShrink: 0 // Prevent icon from shrinking
    },
    optionText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
        overflow: "hidden" // Handle long text
    },
    optionTitle: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1
    },
    optionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200,
        lineHeight: tokens.lineHeightBase200
    }
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
export const ChoiceDialog: React.FC<IChoiceDialogProps> = ({
    open,
    title,
    message,
    options,
    onSelect,
    onDismiss,
    cancelText = "Cancel"
}) => {
    const styles = useStyles();

    const handleOptionClick = React.useCallback((optionId: string) => {
        onSelect(optionId);
    }, [onSelect]);

    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent className={styles.content}>
                        {typeof message === "string" ? <Text>{message}</Text> : message}

                        <div className={styles.optionsContainer}>
                            {options.map((option) => (
                                <Button
                                    key={option.id}
                                    appearance="outline"
                                    className={styles.optionButton}
                                    disabled={option.disabled}
                                    onClick={() => handleOptionClick(option.id)}
                                    aria-describedby={`option-desc-${option.id}`}
                                >
                                    <span className={styles.optionIcon}>{option.icon}</span>
                                    <div className={styles.optionText}>
                                        <span className={styles.optionTitle}>{option.title}</span>
                                        <span
                                            id={`option-desc-${option.id}`}
                                            className={styles.optionDescription}
                                        >
                                            {option.description}
                                        </span>
                                    </div>
                                </Button>
                            ))}
                        </div>
                    </DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onDismiss}>
                            {cancelText}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default ChoiceDialog;
