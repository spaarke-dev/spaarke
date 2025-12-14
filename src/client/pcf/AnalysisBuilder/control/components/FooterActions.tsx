/**
 * FooterActions Component
 *
 * Footer action buttons for Analysis Builder.
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 *
 * Features:
 * - Execute confirmation dialog with save options
 * - Save Playbook and Save As buttons
 * - Designed for mini modal size (works in all sizes)
 */

import * as React from "react";
import {
    Button,
    Spinner,
    makeStyles,
    tokens,
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent
} from "@fluentui/react-components";
import {
    Save24Regular,
    SaveCopy24Regular,
    Play24Filled,
    Dismiss24Regular
} from "@fluentui/react-icons";
import { IFooterActionsProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        justifyContent: "flex-end",
        paddingTop: "8px",
        paddingBottom: "8px",
        paddingLeft: "8px",
        paddingRight: "8px",
        borderTopWidth: "1px",
        borderTopStyle: "solid",
        borderTopColor: tokens.colorNeutralStroke1,
        backgroundColor: tokens.colorNeutralBackground2,
        boxSizing: "border-box",
        minHeight: "48px"
    },
    buttonGroup: {
        display: "flex",
        flexDirection: "row",
        alignItems: "center",
        gap: "4px"
    },
    dialogContent: {
        paddingTop: "8px",
        paddingBottom: "16px"
    },
    dialogActions: {
        paddingTop: "8px"
    },
    dialogButtonRow: {
        display: "flex",
        gap: "8px",
        justifyContent: "flex-end",
        flexWrap: "nowrap",
        whiteSpace: "nowrap"
    }
});

export const FooterActions: React.FC<IFooterActionsProps> = ({
    onSavePlaybook,
    onSaveAs,
    onCancel,
    onExecute,
    isExecuting,
    canSave,
    canExecute
}) => {
    const styles = useStyles();
    const [showExecuteDialog, setShowExecuteDialog] = React.useState(false);

    const handleExecuteClick = (): void => {
        setShowExecuteDialog(true);
    };

    const handleSaveAndExecute = async (): Promise<void> => {
        setShowExecuteDialog(false);
        await onSavePlaybook();
        await onExecute();
    };

    const handleSaveAsAndExecute = async (): Promise<void> => {
        setShowExecuteDialog(false);
        await onSaveAs();
        await onExecute();
    };

    const handleExecuteWithoutSave = async (): Promise<void> => {
        setShowExecuteDialog(false);
        await onExecute();
    };

    return (
        <div className={styles.container}>
            <div className={styles.buttonGroup}>
                <Button
                    appearance="subtle"
                    icon={<Save24Regular />}
                    onClick={onSavePlaybook}
                    disabled={!canSave || isExecuting}
                    size="small"
                >
                    Save
                </Button>
                <Button
                    appearance="subtle"
                    icon={<SaveCopy24Regular />}
                    onClick={onSaveAs}
                    disabled={isExecuting}
                    size="small"
                >
                    Save As
                </Button>
                <Button
                    appearance="secondary"
                    onClick={onCancel}
                    disabled={isExecuting}
                    size="small"
                >
                    Cancel
                </Button>
                <Button
                    appearance="primary"
                    icon={isExecuting ? <Spinner size="tiny" /> : <Play24Filled />}
                    onClick={handleExecuteClick}
                    disabled={!canExecute || isExecuting}
                    size="small"
                >
                    {isExecuting ? "Running..." : "Execute"}
                </Button>
            </div>

            {/* Save Configuration Dialog - all buttons in one row */}
            <Dialog
                open={showExecuteDialog}
                onOpenChange={(_, data) => setShowExecuteDialog(data.open)}
            >
                <DialogSurface>
                    <DialogBody>
                        <DialogTitle
                            action={
                                <Button
                                    appearance="subtle"
                                    aria-label="close"
                                    icon={<Dismiss24Regular />}
                                    onClick={() => setShowExecuteDialog(false)}
                                />
                            }
                        >
                            Save Configuration
                        </DialogTitle>
                        <DialogContent className={styles.dialogContent}>
                            Do you want to save the Analysis configuration before executing?
                        </DialogContent>
                        <DialogActions className={styles.dialogActions}>
                            <div className={styles.dialogButtonRow}>
                                <Button
                                    appearance="primary"
                                    icon={<Save24Regular />}
                                    onClick={handleSaveAndExecute}
                                    disabled={!canSave}
                                >
                                    Save Playbook
                                </Button>
                                <Button
                                    appearance="primary"
                                    icon={<SaveCopy24Regular />}
                                    onClick={handleSaveAsAndExecute}
                                >
                                    Save As
                                </Button>
                                <Button
                                    appearance="secondary"
                                    onClick={handleExecuteWithoutSave}
                                >
                                    Do Not Save
                                </Button>
                            </div>
                        </DialogActions>
                    </DialogBody>
                </DialogSurface>
            </Dialog>
        </div>
    );
};

export default FooterActions;
