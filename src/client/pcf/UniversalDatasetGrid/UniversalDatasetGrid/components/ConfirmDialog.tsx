/**
 * Reusable Confirmation Dialog Component
 * Uses Fluent UI v9 Dialog components
 */

import * as React from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogBody,
    DialogActions,
    DialogContent,
    Button
} from '@fluentui/react-components';

interface ConfirmDialogProps {
    /** Dialog open state */
    open: boolean;

    /** Dialog title */
    title: string;

    /** Dialog message/content */
    message: string;

    /** Confirm button label */
    confirmLabel?: string;

    /** Cancel button label */
    cancelLabel?: string;

    /** Confirm button callback */
    onConfirm: () => void;

    /** Cancel button callback */
    onCancel: () => void;
}

/**
 * Confirmation dialog with confirm/cancel actions
 *
 * Example usage:
 * ```typescript
 * <ConfirmDialog
 *   open={isOpen}
 *   title="Delete File"
 *   message="Are you sure you want to delete this file?"
 *   confirmLabel="Delete"
 *   cancelLabel="Cancel"
 *   onConfirm={handleConfirm}
 *   onCancel={handleCancel}
 * />
 * ```
 */
export const ConfirmDialog: React.FC<ConfirmDialogProps> = ({
    open,
    title,
    message,
    confirmLabel = 'Confirm',
    cancelLabel = 'Cancel',
    onConfirm,
    onCancel
}) => {
    return (
        <Dialog open={open} onOpenChange={(_, data) => !data.open && onCancel()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>{title}</DialogTitle>
                    <DialogContent>{message}</DialogContent>
                    <DialogActions>
                        <Button appearance="secondary" onClick={onCancel}>
                            {cancelLabel}
                        </Button>
                        <Button appearance="primary" onClick={onConfirm}>
                            {confirmLabel}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};
