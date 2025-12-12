/**
 * FooterActions Component
 *
 * Footer action buttons for Analysis Builder.
 * Design Reference: UI Screenshots/01-ANALYSIS-BUILDER-MODAL.jpg
 */

import * as React from "react";
import {
    Button,
    Spinner,
    makeStyles,
    tokens
} from "@fluentui/react-components";
import {
    Save24Regular,
    SaveAs24Regular,
    Dismiss24Regular,
    Play24Filled
} from "@fluentui/react-icons";
import { IFooterActionsProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        padding: "16px 24px",
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2
    },
    leftGroup: {
        display: "flex",
        gap: "8px"
    },
    rightGroup: {
        display: "flex",
        gap: "8px"
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

    return (
        <div className={styles.container}>
            {/* Left side - Save actions */}
            <div className={styles.leftGroup}>
                <Button
                    appearance="subtle"
                    icon={<Save24Regular />}
                    onClick={onSavePlaybook}
                    disabled={!canSave || isExecuting}
                >
                    Save Playbook
                </Button>
                <Button
                    appearance="subtle"
                    icon={<SaveAs24Regular />}
                    onClick={onSaveAs}
                    disabled={isExecuting}
                >
                    Save As...
                </Button>
            </div>

            {/* Right side - Cancel and Execute */}
            <div className={styles.rightGroup}>
                <Button
                    appearance="secondary"
                    icon={<Dismiss24Regular />}
                    onClick={onCancel}
                    disabled={isExecuting}
                >
                    Cancel
                </Button>
                <Button
                    appearance="primary"
                    icon={isExecuting ? <Spinner size="tiny" /> : <Play24Filled />}
                    onClick={onExecute}
                    disabled={!canExecute || isExecuting}
                >
                    {isExecuting ? "Executing..." : "Execute"}
                </Button>
            </div>
        </div>
    );
};

export default FooterActions;
