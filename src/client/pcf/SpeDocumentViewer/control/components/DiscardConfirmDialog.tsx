/**
 * DiscardConfirmDialog Component
 *
 * Confirmation dialog before discarding checkout changes.
 * Warns user that unsaved changes will be lost.
 */

import * as React from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogContent,
    DialogActions,
    DialogBody,
    Button,
    Spinner,
    makeStyles,
    tokens
} from '@fluentui/react-components';
import { Warning24Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
    content: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM
    },
    warningRow: {
        display: 'flex',
        alignItems: 'flex-start',
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalS,
        backgroundColor: tokens.colorPaletteYellowBackground1,
        borderRadius: tokens.borderRadiusMedium
    },
    warningIcon: {
        color: tokens.colorPaletteYellowForeground2,
        flexShrink: 0,
        marginTop: '2px'
    },
    warningText: {
        color: tokens.colorNeutralForeground1,
        fontSize: tokens.fontSizeBase300
    }
});

export interface DiscardConfirmDialogProps {
    /** Whether the dialog is open */
    isOpen: boolean;
    /** Document name for display */
    documentName: string;
    /** Loading state (discard in progress) */
    isLoading?: boolean;
    /** Callback when discard is confirmed */
    onConfirm: () => void;
    /** Callback when dialog is cancelled */
    onCancel: () => void;
}

/**
 * Confirmation dialog for discarding checkout changes
 */
export const DiscardConfirmDialog: React.FC<DiscardConfirmDialogProps> = ({
    isOpen,
    documentName,
    isLoading = false,
    onConfirm,
    onCancel
}) => {
    const styles = useStyles();

    return (
        <Dialog open={isOpen} onOpenChange={(_, data) => !data.open && onCancel()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>Discard Changes?</DialogTitle>
                    <DialogContent className={styles.content}>
                        <p>
                            You are about to discard your changes to "{documentName}"
                            and release the document lock.
                        </p>

                        <div className={styles.warningRow}>
                            <Warning24Regular className={styles.warningIcon} />
                            <span className={styles.warningText}>
                                Any unsaved changes will be permanently lost.
                                This action cannot be undone.
                            </span>
                        </div>
                    </DialogContent>

                    <DialogActions>
                        <Button
                            appearance="secondary"
                            onClick={onCancel}
                            disabled={isLoading}
                        >
                            Keep Editing
                        </Button>
                        <Button
                            appearance="primary"
                            onClick={onConfirm}
                            disabled={isLoading}
                            icon={isLoading ? <Spinner size="tiny" /> : undefined}
                        >
                            {isLoading ? 'Discarding...' : 'Discard Changes'}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default DiscardConfirmDialog;
