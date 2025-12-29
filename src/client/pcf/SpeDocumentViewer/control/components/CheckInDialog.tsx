/**
 * CheckInDialog Component
 *
 * Modal dialog for document check-in with optional comment input.
 * Comment field is encouraged but optional.
 */

import * as React from 'react';
import { useState, useRef, useEffect } from 'react';
import {
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogContent,
    DialogActions,
    DialogBody,
    Button,
    Textarea,
    Label,
    makeStyles,
    tokens,
    Spinner
} from '@fluentui/react-components';

const useStyles = makeStyles({
    content: {
        display: 'flex',
        flexDirection: 'column',
        gap: tokens.spacingVerticalM
    },
    commentField: {
        minHeight: '80px'
    },
    hint: {
        color: tokens.colorNeutralForeground3,
        fontSize: tokens.fontSizeBase200
    }
});

export interface CheckInDialogProps {
    /** Whether the dialog is open */
    isOpen: boolean;
    /** Document name for display */
    documentName: string;
    /** Loading state (check-in in progress) */
    isLoading?: boolean;
    /** Callback when check-in is confirmed */
    onConfirm: (comment: string) => void;
    /** Callback when dialog is cancelled */
    onCancel: () => void;
}

/**
 * Dialog for confirming document check-in with optional comment
 */
export const CheckInDialog: React.FC<CheckInDialogProps> = ({
    isOpen,
    documentName,
    isLoading = false,
    onConfirm,
    onCancel
}) => {
    const styles = useStyles();
    const [comment, setComment] = useState('');
    const textareaRef = useRef<HTMLTextAreaElement>(null);

    // Auto-focus comment field when dialog opens
    useEffect(() => {
        if (isOpen && textareaRef.current) {
            // Small delay to ensure dialog is fully rendered
            const timer = setTimeout(() => {
                textareaRef.current?.focus();
            }, 100);
            return () => clearTimeout(timer);
        }
    }, [isOpen]);

    // Reset comment when dialog opens
    useEffect(() => {
        if (isOpen) {
            setComment('');
        }
    }, [isOpen]);

    const handleConfirm = () => {
        onConfirm(comment.trim());
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        // Ctrl+Enter to submit
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            handleConfirm();
        }
    };

    return (
        <Dialog open={isOpen} onOpenChange={(_, data) => !data.open && onCancel()}>
            <DialogSurface>
                <DialogBody>
                    <DialogTitle>Check In Document</DialogTitle>
                    <DialogContent className={styles.content}>
                        <p>
                            You are about to check in "{documentName}".
                            This will save your changes and create a new version.
                        </p>

                        <div>
                            <Label htmlFor="checkin-comment">
                                Version comment (optional)
                            </Label>
                            <Textarea
                                ref={textareaRef}
                                id="checkin-comment"
                                className={styles.commentField}
                                placeholder="Describe the changes you made..."
                                value={comment}
                                onChange={(_, data) => setComment(data.value)}
                                onKeyDown={handleKeyDown}
                                disabled={isLoading}
                                resize="vertical"
                            />
                            <span className={styles.hint}>
                                Press Ctrl+Enter to submit
                            </span>
                        </div>
                    </DialogContent>

                    <DialogActions>
                        <Button
                            appearance="secondary"
                            onClick={onCancel}
                            disabled={isLoading}
                        >
                            Cancel
                        </Button>
                        <Button
                            appearance="primary"
                            onClick={handleConfirm}
                            disabled={isLoading}
                            icon={isLoading ? <Spinner size="tiny" /> : undefined}
                        >
                            {isLoading ? 'Checking in...' : 'Check In'}
                        </Button>
                    </DialogActions>
                </DialogBody>
            </DialogSurface>
        </Dialog>
    );
};

export default CheckInDialog;
