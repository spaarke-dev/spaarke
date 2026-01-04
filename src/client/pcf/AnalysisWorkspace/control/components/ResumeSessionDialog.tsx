/**
 * Resume Session Dialog Component
 *
 * Displays when opening an existing analysis that has chat history.
 * Asks user whether to resume the previous session or start fresh.
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
import { HistoryRegular, DocumentAddRegular } from "@fluentui/react-icons";
import { logInfo } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IResumeSessionDialogProps {
    /** Whether the dialog is open */
    open: boolean;
    /** Number of chat messages in history */
    chatMessageCount: number;
    /** Callback when user chooses to resume with history */
    onResumeWithHistory: () => void;
    /** Callback when user chooses to start fresh */
    onStartFresh: () => void;
    /** Callback when dialog is dismissed */
    onDismiss: () => void;
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
        textAlign: "left"
    },
    optionIcon: {
        fontSize: "24px",
        color: tokens.colorBrandForeground1
    },
    optionText: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS
    },
    optionTitle: {
        fontWeight: tokens.fontWeightSemibold
    },
    optionDescription: {
        color: tokens.colorNeutralForeground2,
        fontSize: tokens.fontSizeBase200
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ResumeSessionDialog: React.FC<IResumeSessionDialogProps> = ({
    open,
    chatMessageCount,
    onResumeWithHistory,
    onStartFresh,
    onDismiss
}) => {
    const styles = useStyles();

    const handleResumeWithHistory = React.useCallback(() => {
        logInfo("ResumeSessionDialog", "User chose to resume with history");
        onResumeWithHistory();
    }, [onResumeWithHistory]);

    const handleStartFresh = React.useCallback(() => {
        logInfo("ResumeSessionDialog", "User chose to start fresh");
        onStartFresh();
    }, [onStartFresh]);

    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onDismiss()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>Resume Previous Session?</DialogTitle>
                    <DialogContent className={styles.content}>
                        <Text>
                            This analysis has an existing conversation with{" "}
                            <strong>{chatMessageCount} message{chatMessageCount !== 1 ? "s" : ""}</strong>.
                        </Text>
                        <Text>
                            Would you like to continue from where you left off, or start a fresh conversation?
                        </Text>

                        <div className={styles.optionsContainer}>
                            <Button
                                appearance="outline"
                                className={styles.optionButton}
                                onClick={handleResumeWithHistory}
                            >
                                <HistoryRegular className={styles.optionIcon} />
                                <div className={styles.optionText}>
                                    <span className={styles.optionTitle}>Resume Session</span>
                                    <span className={styles.optionDescription}>
                                        Continue with your previous conversation history
                                    </span>
                                </div>
                            </Button>

                            <Button
                                appearance="outline"
                                className={styles.optionButton}
                                onClick={handleStartFresh}
                            >
                                <DocumentAddRegular className={styles.optionIcon} />
                                <div className={styles.optionText}>
                                    <span className={styles.optionTitle}>Start Fresh</span>
                                    <span className={styles.optionDescription}>
                                        Begin a new conversation (previous history will be cleared)
                                    </span>
                                </div>
                            </Button>
                        </div>
                    </DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onDismiss}>
                            Cancel
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default ResumeSessionDialog;
