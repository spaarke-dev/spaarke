/**
 * ContextSwitchDialog - Non-blocking dialog for context navigation changes
 *
 * When the Dataverse side pane detects that the user navigated to a different
 * record (e.g., from a Matter to a Project), this dialog offers two options:
 *   1. Switch to the new record's context (start fresh or transfer session)
 *   2. Keep the current context (dismiss and continue)
 *
 * Uses Fluent UI v9 Dialog, Button, and Text components per ADR-021.
 * Uses design tokens for all styling — no hard-coded colors.
 *
 * @see ADR-021 - Fluent UI v9 Design System
 */

import { useCallback } from "react";
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogContent,
    DialogActions,
    Button,
    Text,
    makeStyles,
    tokens,
} from "@fluentui/react-components";
import {
    ArrowSwap20Regular,
    Dismiss20Regular,
} from "@fluentui/react-icons";
import type { DetectedContext } from "../services/contextService";
import { getEntityDisplayName } from "../services/contextService";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ContextSwitchDialogProps {
    /** Whether the dialog is currently open. */
    open: boolean;
    /** The new context that was detected (the record the user navigated to). */
    newContext: DetectedContext;
    /** The current/previous context (the record the user was working on). */
    currentContext: DetectedContext;
    /** Called when the user chooses to switch to the new context. */
    onSwitch: (newContext: DetectedContext) => void;
    /** Called when the user chooses to keep the current context. */
    onKeep: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    body: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
    },
    contextInfo: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: tokens.borderRadiusMedium,
        padding: tokens.spacingVerticalS + " " + tokens.spacingHorizontalM,
    },
    contextLabel: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
    },
    contextValue: {
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
    },
    contextId: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase100,
        fontFamily: tokens.fontFamilyMonospace,
    },
    arrow: {
        display: "flex",
        justifyContent: "center",
        color: tokens.colorNeutralForeground3,
        fontSize: "20px",
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Context switch confirmation dialog.
 *
 * Shown when the polling-based context detection notices the user has
 * navigated to a different Dataverse record while the SprkChat side pane
 * is still open.
 */
export const ContextSwitchDialog: React.FC<ContextSwitchDialogProps> = ({
    open,
    newContext,
    currentContext,
    onSwitch,
    onKeep,
}) => {
    const styles = useStyles();

    const newEntityName = getEntityDisplayName(newContext.entityType);
    const currentEntityName = getEntityDisplayName(currentContext.entityType);

    const handleSwitch = useCallback(() => {
        onSwitch(newContext);
    }, [onSwitch, newContext]);

    const handleKeep = useCallback(() => {
        onKeep();
    }, [onKeep]);

    // Truncate entity ID for display (show first 8 chars)
    const truncateId = (id: string): string => {
        if (!id) return "";
        return id.length > 8 ? `${id.substring(0, 8)}...` : id;
    };

    return (
        <Dialog open={open} modalType="alert">
            <DialogSurface aria-label="Context switch confirmation">
                <DialogBody>
                    <DialogTitle>Record Changed</DialogTitle>
                    <DialogContent>
                        <div className={styles.body}>
                            <Text>
                                You navigated to a different record. Would you like to
                                switch the chat context?
                            </Text>

                            {/* Current context */}
                            <div className={styles.contextInfo}>
                                <Text className={styles.contextLabel}>
                                    Current context
                                </Text>
                                <Text className={styles.contextValue}>
                                    {currentEntityName}
                                </Text>
                                {currentContext.entityId && (
                                    <Text className={styles.contextId}>
                                        {truncateId(currentContext.entityId)}
                                    </Text>
                                )}
                            </div>

                            {/* Arrow */}
                            <div className={styles.arrow}>
                                <ArrowSwap20Regular />
                            </div>

                            {/* New context */}
                            <div className={styles.contextInfo}>
                                <Text className={styles.contextLabel}>
                                    New record
                                </Text>
                                <Text className={styles.contextValue}>
                                    {newEntityName}
                                </Text>
                                {newContext.entityId && (
                                    <Text className={styles.contextId}>
                                        {truncateId(newContext.entityId)}
                                    </Text>
                                )}
                            </div>
                        </div>
                    </DialogContent>
                    <DialogActions>
                        <Button
                            appearance="secondary"
                            icon={<Dismiss20Regular />}
                            onClick={handleKeep}
                            data-testid="context-switch-keep"
                        >
                            Keep Current
                        </Button>
                        <Button
                            appearance="primary"
                            icon={<ArrowSwap20Regular />}
                            onClick={handleSwitch}
                            data-testid="context-switch-switch"
                        >
                            Switch to {newEntityName}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default ContextSwitchDialog;
