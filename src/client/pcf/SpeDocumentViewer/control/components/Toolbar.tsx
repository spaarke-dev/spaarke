/**
 * Toolbar Component
 *
 * Fluent v9 toolbar for SpeDocumentViewer with contextual buttons.
 * Shows different buttons based on view mode and checkout status.
 *
 * Preview Mode buttons: Refresh, Edit, Download, Delete, Expand
 * Edit Mode buttons: Open Desktop, Check In, Discard
 */

import * as React from 'react';
import {
    makeStyles,
    shorthands,
    tokens,
    Button,
    Tooltip,
    Text,
    Spinner,
    Dialog,
    DialogSurface,
    DialogTitle,
    DialogContent,
    DialogActions,
    DialogTrigger,
    DialogBody
} from '@fluentui/react-components';
import {
    ArrowClockwise24Regular,
    Edit24Regular,
    ArrowDownload24Regular,
    Delete24Regular,
    FullScreenMaximize24Regular,
    Checkmark24Regular,
    Dismiss24Regular,
    Desktop24Regular,
    Globe24Regular
} from '@fluentui/react-icons';
import { CheckoutStatus, DocumentInfo, ViewMode } from '../types';
import { CheckoutStatusBadge } from './CheckoutStatusBadge';

const useStyles = makeStyles({
    toolbar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        ...shorthands.padding('8px', '12px'),
        backgroundColor: tokens.colorNeutralBackground2,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke1,
        minHeight: '48px',
        flexShrink: 0
    },
    leftSection: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap('8px'),
        ...shorthands.overflow('hidden'),
        flex: 1,
        minWidth: 0
    },
    centerSection: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0
    },
    rightSection: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap('4px'),
        flexShrink: 0
    },
    docName: {
        fontWeight: tokens.fontWeightSemibold,
        fontSize: '14px',
        color: tokens.colorNeutralForeground1,
        ...shorthands.overflow('hidden'),
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap'
    },
    docSize: {
        color: tokens.colorNeutralForeground3,
        fontSize: '12px',
        flexShrink: 0
    },
    divider: {
        width: '1px',
        height: '24px',
        backgroundColor: tokens.colorNeutralStroke2,
        marginLeft: '8px',
        marginRight: '8px'
    }
});

export interface ToolbarProps {
    /** Document metadata */
    documentInfo: DocumentInfo | null;
    /** Current checkout status */
    checkoutStatus: CheckoutStatus | null;
    /** Current view mode */
    viewMode: ViewMode;
    /** Dark theme mode */
    isDarkTheme?: boolean;

    // Feature flags
    /** Enable edit/checkout button */
    enableEdit?: boolean;
    /** Enable download button */
    enableDownload?: boolean;
    /** Enable delete button */
    enableDelete?: boolean;

    // Loading states
    /** Any operation is loading */
    isLoading?: boolean;
    /** Edit/checkout operation loading */
    isEditLoading?: boolean;
    /** Check-in operation loading */
    isCheckInLoading?: boolean;
    /** Delete operation loading */
    isDeleteLoading?: boolean;
    /** Open in web operation loading */
    isOpenInWebLoading?: boolean;

    // Feature: Open in Web
    /** Whether file type supports Office Online (hide for .eml, .pdf, etc.) */
    supportsOpenInWeb?: boolean;

    // Callbacks - Preview Mode
    /** Refresh button clicked */
    onRefresh?: () => void;
    /** Edit/checkout button clicked */
    onEdit?: () => void;
    /** Download button clicked */
    onDownload?: () => void;
    /** Open in web button clicked */
    onOpenInWeb?: () => void;
    /** Delete button clicked (after confirmation) */
    onDelete?: () => void;
    /** Expand/fullscreen button clicked */
    onFullscreen?: () => void;

    // Callbacks - Edit Mode
    /** Open in desktop button clicked */
    onOpenDesktop?: () => void;
    /** Check-in button clicked */
    onCheckIn?: () => void;
    /** Discard changes button clicked */
    onDiscard?: () => void;
}

/**
 * Format file size for display
 */
function formatFileSize(bytes: number | undefined | null): string {
    if (bytes === undefined || bytes === null || bytes === 0) return '';

    const units = ['B', 'KB', 'MB', 'GB'];
    const k = 1024;
    const i = Math.floor(Math.log(bytes) / Math.log(k));

    return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${units[i]}`;
}

export const Toolbar: React.FC<ToolbarProps> = ({
    documentInfo,
    checkoutStatus,
    viewMode,
    isDarkTheme = false,
    enableEdit = true,
    enableDownload = true,
    enableDelete = false,
    isLoading = false,
    isEditLoading = false,
    isCheckInLoading = false,
    isDeleteLoading = false,
    isOpenInWebLoading = false,
    supportsOpenInWeb = false,
    onRefresh,
    onEdit,
    onDownload,
    onOpenInWeb,
    onDelete,
    onFullscreen,
    onOpenDesktop,
    onCheckIn,
    onDiscard
}) => {
    const styles = useStyles();

    const documentName = documentInfo?.name || 'Document';
    const documentSize = formatFileSize(documentInfo?.size);

    // Determine if edit button should be disabled
    // Disabled if: loading, OR document is checked out by another user
    const isCheckedOutByOther = checkoutStatus?.isCheckedOut && !checkoutStatus.isCurrentUser;
    const editButtonDisabled = isLoading || isEditLoading || isCheckedOutByOther;

    // Edit button tooltip
    const editButtonTooltip = isCheckedOutByOther
        ? `Checked out by ${checkoutStatus?.checkedOutBy?.name || 'another user'}`
        : 'Edit document (check out for editing)';

    // Delete button disabled when document is checked out (by anyone)
    const deleteButtonDisabled = isLoading || isDeleteLoading || checkoutStatus?.isCheckedOut;

    // Delete button tooltip
    const deleteButtonTooltip = checkoutStatus?.isCheckedOut
        ? 'Cannot delete while document is checked out'
        : 'Delete document and file';

    // Check-in button disabled when loading
    const checkInButtonDisabled = isLoading || isCheckInLoading;

    return (
        <div className={styles.toolbar}>
            {/* Left Section: Document Info */}
            <div className={styles.leftSection}>
                <Text className={styles.docName} title={documentName}>
                    {documentName}
                </Text>
                {documentSize && (
                    <Text className={styles.docSize}>
                        ({documentSize})
                    </Text>
                )}
            </div>

            {/* Center Section: Checkout Status */}
            <div className={styles.centerSection}>
                <CheckoutStatusBadge
                    checkoutStatus={checkoutStatus}
                    isDarkTheme={isDarkTheme}
                />
            </div>

            {/* Right Section: Action Buttons */}
            <div className={styles.rightSection}>
                {viewMode === ViewMode.Preview && (
                    <>
                        {/* Refresh Button - HIDDEN: Moved to ribbon button (2026-01-15)
                        <Tooltip content="Refresh preview" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<ArrowClockwise24Regular />}
                                onClick={onRefresh}
                                disabled={isLoading}
                                aria-label="Refresh"
                            />
                        </Tooltip>
                        */}

                        {/* Edit Button - Visible when enableEdit=true */}
                        {enableEdit && (
                            <Tooltip content={editButtonTooltip} relationship="label">
                                <Button
                                    appearance="subtle"
                                    size="small"
                                    icon={isEditLoading ? <Spinner size="tiny" /> : <Edit24Regular />}
                                    onClick={onEdit}
                                    disabled={editButtonDisabled}
                                    aria-label="Edit"
                                />
                            </Tooltip>
                        )}

                        {/* Download Button - Visible when enableDownload=true */}
                        {enableDownload && (
                            <Tooltip content="Download document" relationship="label">
                                <Button
                                    appearance="subtle"
                                    size="small"
                                    icon={<ArrowDownload24Regular />}
                                    onClick={onDownload}
                                    disabled={isLoading}
                                    aria-label="Download"
                                />
                            </Tooltip>
                        )}

                        {/* Open in Web Button - HIDDEN: Moved to ribbon button (2026-01-15)
                        {supportsOpenInWeb && (
                            <Tooltip content="Open in Office Online" relationship="label">
                                <Button
                                    appearance="subtle"
                                    size="small"
                                    icon={isOpenInWebLoading ? <Spinner size="tiny" /> : <Globe24Regular />}
                                    onClick={onOpenInWeb}
                                    disabled={isLoading || isOpenInWebLoading}
                                    aria-label="Open in Web"
                                />
                            </Tooltip>
                        )}
                        */}

                        {/* Delete Button - Visible when enableDelete=true */}
                        {enableDelete && (
                            <Dialog>
                                <DialogTrigger disableButtonEnhancement>
                                    <Tooltip content={deleteButtonTooltip} relationship="label">
                                        <Button
                                            appearance="subtle"
                                            size="small"
                                            icon={isDeleteLoading ? <Spinner size="tiny" /> : <Delete24Regular />}
                                            disabled={deleteButtonDisabled}
                                            aria-label="Delete"
                                        />
                                    </Tooltip>
                                </DialogTrigger>
                                <DialogSurface>
                                    <DialogBody>
                                        <DialogTitle>Delete Document?</DialogTitle>
                                        <DialogContent>
                                            <Text>
                                                This will permanently delete "{documentName}" and the associated file.
                                                This action cannot be undone.
                                            </Text>
                                        </DialogContent>
                                        <DialogActions>
                                            <DialogTrigger disableButtonEnhancement>
                                                <Button appearance="secondary">Cancel</Button>
                                            </DialogTrigger>
                                            <Button
                                                appearance="primary"
                                                onClick={onDelete}
                                            >
                                                Delete
                                            </Button>
                                        </DialogActions>
                                    </DialogBody>
                                </DialogSurface>
                            </Dialog>
                        )}

                        {/* Divider before Expand button */}
                        {onFullscreen && <div className={styles.divider} />}

                        {/* Expand Button - Visible when onFullscreen callback provided */}
                        {onFullscreen && (
                            <Tooltip content="Open in larger view" relationship="label">
                                <Button
                                    appearance="subtle"
                                    size="small"
                                    icon={<FullScreenMaximize24Regular />}
                                    onClick={onFullscreen}
                                    disabled={isLoading}
                                    aria-label="Expand"
                                />
                            </Tooltip>
                        )}
                    </>
                )}

                {viewMode === ViewMode.Edit && (
                    <>
                        {/* Open in Desktop Button - HIDDEN: Moved to ribbon button (2026-01-15)
                        <Tooltip content="Open in desktop application" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Desktop24Regular />}
                                onClick={onOpenDesktop}
                                disabled={isLoading}
                                aria-label="Open in Desktop"
                            />
                        </Tooltip>

                        <div className={styles.divider} />
                        */}

                        {/* Check In Button - Primary action */}
                        <Tooltip content="Save changes and release lock" relationship="label">
                            <Button
                                appearance="primary"
                                size="small"
                                icon={isCheckInLoading ? <Spinner size="tiny" /> : <Checkmark24Regular />}
                                onClick={onCheckIn}
                                disabled={checkInButtonDisabled}
                            >
                                Check In
                            </Button>
                        </Tooltip>

                        {/* Discard Button */}
                        <Tooltip content="Discard changes and release lock" relationship="label">
                            <Button
                                appearance="subtle"
                                size="small"
                                icon={<Dismiss24Regular />}
                                onClick={onDiscard}
                                disabled={isLoading}
                            >
                                Discard
                            </Button>
                        </Tooltip>
                    </>
                )}

                {viewMode === ViewMode.Processing && (
                    <Spinner size="small" label="Processing..." />
                )}
            </div>
        </div>
    );
};

export default Toolbar;
