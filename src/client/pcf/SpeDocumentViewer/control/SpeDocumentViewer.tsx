/**
 * SpeDocumentViewer React Component
 *
 * Unified document viewer with check-out/check-in workflow.
 * Supports preview mode (embed.aspx) and edit mode (embedview).
 */

import * as React from 'react';
import { useCallback, useRef, useState, useEffect } from 'react';
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Spinner,
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
    MessageBarActions,
    Button
} from '@fluentui/react-components';
import { ArrowClockwise24Regular } from '@fluentui/react-icons';
import { DocumentViewerProps } from './types';
import { useDocumentPreview } from './hooks/useDocumentPreview';
import { useCheckoutFlow } from './hooks/useCheckoutFlow';
import { Toolbar } from './components/Toolbar';
import { CheckInDialog } from './components/CheckInDialog';
import { DiscardConfirmDialog } from './components/DiscardConfirmDialog';
import { BffClient } from './BffClient';
import './css/SpeDocumentViewer.css';

// Control version - update in all 4 locations per PCF-V9-PACKAGING.md
const CONTROL_VERSION = '1.0.15';
const BUILD_DATE = '2026-01-15';

/**
 * DocumentViewerApp - Main React component
 * (Named differently from PCF class to avoid naming conflict)
 */
export const DocumentViewerApp: React.FC<DocumentViewerProps> = ({
    documentId,
    bffApiUrl,
    accessToken,
    correlationId,
    isDarkTheme,
    enableEdit,
    enableDelete,
    enableDownload,
    showToolbar,
    onRefresh,
    onDeleted
}) => {
    const iframeRef = useRef<HTMLIFrameElement>(null);
    const bffClient = useRef(new BffClient(bffApiUrl));

    // Dialog states
    const [isCheckInDialogOpen, setIsCheckInDialogOpen] = useState(false);
    const [isDiscardDialogOpen, setIsDiscardDialogOpen] = useState(false);
    const [isDeleteLoading, setIsDeleteLoading] = useState(false);
    const [isOpenInWebLoading, setIsOpenInWebLoading] = useState(false);

    // Use the preview hook for state management
    const {
        previewUrl,
        documentInfo,
        checkoutStatus,
        isLoading,
        isIframeLoading,
        isIframeTimedOut,
        error: previewError,
        refresh,
        onIframeLoad,
        onIframeError,
        resetIframeState
    } = useDocumentPreview(documentId, bffApiUrl, accessToken, correlationId);

    // Office file extensions that support Office Online viewing
    // (matches SpeFileViewer's comprehensive list)
    const OFFICE_EXTENSIONS = React.useMemo(() => [
        // Word
        '.docx', '.doc', '.docm', '.dot', '.dotx', '.dotm',
        // Excel
        '.xlsx', '.xls', '.xlsm', '.xlsb', '.xlt', '.xltx', '.xltm',
        // PowerPoint
        '.pptx', '.ppt', '.pptm', '.pot', '.potx', '.potm', '.pps', '.ppsx', '.ppsm'
    ], []);

    // Determine if file supports "Open in Web" (Office Online)
    const supportsOpenInWeb = React.useMemo(() => {
        const ext = documentInfo?.fileExtension?.toLowerCase();
        if (!ext) return false;
        return OFFICE_EXTENSIONS.includes(ext);
    }, [documentInfo?.fileExtension, OFFICE_EXTENSIONS]);

    // Use the checkout flow hook
    const {
        viewMode,
        editUrl,
        isCheckoutLoading,
        isCheckInLoading,
        isDiscardLoading,
        error: checkoutError,
        lockError,
        checkout,
        checkIn,
        discard,
        clearError,
        resetToPreview,
        updateCheckoutStatus
    } = useCheckoutFlow({
        documentId,
        bffApiUrl,
        accessToken,
        correlationId,
        onCheckoutSuccess: () => {
            console.log('[SpeDocumentViewer] Checkout succeeded');
        },
        onCheckInSuccess: () => {
            console.log('[SpeDocumentViewer] Check-in succeeded, refreshing preview');
            refresh();
        },
        onDiscardSuccess: () => {
            console.log('[SpeDocumentViewer] Discard succeeded, refreshing preview');
            refresh();
        }
    });

    // Sync checkout status from preview hook to checkout flow hook
    useEffect(() => {
        updateCheckoutStatus(checkoutStatus);
    }, [checkoutStatus, updateCheckoutStatus]);

    // Combined error from either hook
    const error = previewError || checkoutError;

    /**
     * Handle refresh button click
     */
    const handleRefresh = useCallback(() => {
        clearError();
        refresh();
        onRefresh?.();
    }, [refresh, onRefresh, clearError]);

    /**
     * Handle retry after timeout
     */
    const handleRetry = useCallback(() => {
        if (iframeRef.current && previewUrl) {
            // Force iframe reload by resetting src
            resetIframeState();
            iframeRef.current.src = previewUrl;
        } else {
            refresh();
        }
    }, [previewUrl, resetIframeState, refresh]);

    /**
     * Handle iframe load event
     */
    const handleIframeLoad = useCallback(() => {
        onIframeLoad();
    }, [onIframeLoad]);

    /**
     * Handle iframe error event
     */
    const handleIframeError = useCallback(() => {
        onIframeError();
    }, [onIframeError]);

    /**
     * Handle edit button click - initiate checkout flow
     */
    const handleEdit = useCallback(async () => {
        if (!documentId) return;
        await checkout();
    }, [documentId, checkout]);

    /**
     * Handle check-in button click - show dialog
     */
    const handleCheckInClick = useCallback(() => {
        setIsCheckInDialogOpen(true);
    }, []);

    /**
     * Handle check-in confirm from dialog
     */
    const handleCheckInConfirm = useCallback(async (comment: string) => {
        const success = await checkIn(comment);
        if (success) {
            setIsCheckInDialogOpen(false);
        }
    }, [checkIn]);

    /**
     * Handle check-in dialog cancel
     */
    const handleCheckInCancel = useCallback(() => {
        setIsCheckInDialogOpen(false);
    }, []);

    /**
     * Handle discard button click - show confirmation dialog
     */
    const handleDiscardClick = useCallback(() => {
        setIsDiscardDialogOpen(true);
    }, []);

    /**
     * Handle discard confirm from dialog
     */
    const handleDiscardConfirm = useCallback(async () => {
        const success = await discard();
        if (success) {
            setIsDiscardDialogOpen(false);
        }
    }, [discard]);

    /**
     * Handle discard dialog cancel
     */
    const handleDiscardCancel = useCallback(() => {
        setIsDiscardDialogOpen(false);
    }, []);

    /**
     * Handle open in desktop button click
     */
    const handleOpenDesktop = useCallback(async () => {
        if (!documentId) return;

        console.log('[SpeDocumentViewer] Open in desktop clicked');

        try {
            const response = await bffClient.current.getOpenLinks(documentId, accessToken, correlationId);

            if (response.desktopUrl) {
                // Open desktop app using protocol URL
                window.location.href = response.desktopUrl;
            } else {
                console.warn('[SpeDocumentViewer] No desktop URL available');
            }
        } catch (err) {
            console.error('[SpeDocumentViewer] Get open links failed:', err);
        }
    }, [documentId, accessToken, correlationId]);

    /**
     * Handle open in web button click - opens Office Online in new tab
     */
    const handleOpenInWeb = useCallback(async () => {
        if (!documentId) return;

        setIsOpenInWebLoading(true);
        console.log('[SpeDocumentViewer] Open in web clicked');

        try {
            const response = await bffClient.current.getOpenLinks(documentId, accessToken, correlationId);

            if (response.webUrl) {
                // Open Office Online in new tab with security attributes
                window.open(response.webUrl, '_blank', 'noopener,noreferrer');
            } else {
                console.warn('[SpeDocumentViewer] No web URL available');
            }
        } catch (err) {
            console.error('[SpeDocumentViewer] Get open links failed:', err);
        } finally {
            setIsOpenInWebLoading(false);
        }
    }, [documentId, accessToken, correlationId]);

    /**
     * Handle download button click
     * Uses the BFF download endpoint which uses app-only auth.
     * This works for all documents including those uploaded by email-to-document.
     */
    const handleDownload = useCallback(async () => {
        if (!documentId) return;

        console.log('[SpeDocumentViewer] Download button clicked');

        try {
            // Download through BFF proxy (app-only auth on server)
            // This works for documents that users don't have direct SPE permissions for
            await bffClient.current.downloadDocument(
                documentId,
                accessToken,
                correlationId,
                documentInfo?.name
            );
        } catch (err) {
            console.error('[SpeDocumentViewer] Download failed:', err);
        }
    }, [documentId, accessToken, correlationId, documentInfo?.name]);

    /**
     * Handle delete button click (after confirmation)
     */
    const handleDelete = useCallback(async () => {
        if (!documentId) return;

        setIsDeleteLoading(true);
        console.log('[SpeDocumentViewer] Delete confirmed');

        try {
            await bffClient.current.deleteDocument(documentId, accessToken, correlationId);
            console.log('[SpeDocumentViewer] Document deleted successfully');

            // Notify parent
            onDeleted?.();

        } catch (err) {
            console.error('[SpeDocumentViewer] Delete failed:', err);
        } finally {
            setIsDeleteLoading(false);
        }
    }, [documentId, accessToken, correlationId, onDeleted]);

    // Theme
    const theme = isDarkTheme ? webDarkTheme : webLightTheme;

    // Style for FluentProvider to ensure proper height inheritance
    const fluentProviderStyle: React.CSSProperties = {
        height: '100%',
        display: 'flex',
        flexDirection: 'column'
    };

    // Render loading state (initial API call)
    if (isLoading) {
        return (
            <FluentProvider theme={theme} style={fluentProviderStyle}>
                <div className="spe-document-viewer-container" data-theme={isDarkTheme ? 'dark' : 'light'}>
                    <div className="spe-document-viewer-loading">
                        <Spinner size="large" label="Loading document..." />
                    </div>
                </div>
            </FluentProvider>
        );
    }

    // Render error state (but not iframe timeout - that has special handling)
    if (error && !isIframeTimedOut) {
        return (
            <FluentProvider theme={theme} style={fluentProviderStyle}>
                <div className="spe-document-viewer-container" data-theme={isDarkTheme ? 'dark' : 'light'}>
                    <div className="spe-document-viewer-error">
                        <MessageBar intent="error">
                            <MessageBarBody>
                                <MessageBarTitle>Error</MessageBarTitle>
                                {error}
                            </MessageBarBody>
                            <MessageBarActions>
                                <Button
                                    appearance="transparent"
                                    icon={<ArrowClockwise24Regular />}
                                    onClick={handleRefresh}
                                >
                                    Retry
                                </Button>
                            </MessageBarActions>
                        </MessageBar>
                    </div>
                </div>
            </FluentProvider>
        );
    }

    // Render no document ID state
    if (!documentId) {
        return (
            <FluentProvider theme={theme} style={fluentProviderStyle}>
                <div className="spe-document-viewer-container" data-theme={isDarkTheme ? 'dark' : 'light'}>
                    <div className="spe-document-viewer-error">
                        <MessageBar intent="warning">
                            <MessageBarBody>
                                <MessageBarTitle>No Document</MessageBarTitle>
                                No document ID provided. Save the form to view the document.
                            </MessageBarBody>
                        </MessageBar>
                    </div>
                </div>
            </FluentProvider>
        );
    }

    // Determine which URL to show in iframe based on view mode
    const iframeSrc = viewMode === 'edit' ? editUrl : previewUrl;

    return (
        <FluentProvider theme={theme} style={fluentProviderStyle}>
            <div className="spe-document-viewer-container" data-theme={isDarkTheme ? 'dark' : 'light'}>
                {/* Toolbar - only shown when showToolbar is true */}
                {showToolbar && (
                    <Toolbar
                        documentInfo={documentInfo}
                        checkoutStatus={checkoutStatus}
                        viewMode={viewMode}
                        isDarkTheme={isDarkTheme}
                        enableEdit={enableEdit}
                        enableDownload={enableDownload}
                        enableDelete={enableDelete}
                        isLoading={isIframeLoading}
                        isEditLoading={isCheckoutLoading}
                        isCheckInLoading={isCheckInLoading}
                        isDeleteLoading={isDeleteLoading}
                        isOpenInWebLoading={isOpenInWebLoading}
                        supportsOpenInWeb={supportsOpenInWeb}
                        onRefresh={handleRefresh}
                        onEdit={handleEdit}
                        onDownload={handleDownload}
                        onOpenInWeb={handleOpenInWeb}
                        onDelete={handleDelete}
                        onOpenDesktop={handleOpenDesktop}
                        onCheckIn={handleCheckInClick}
                        onDiscard={handleDiscardClick}
                    />
                )}

                {/* Document lock error banner */}
                {lockError && (
                    <div className="spe-document-viewer-banner">
                        <MessageBar intent="warning" style={{ margin: '8px' }}>
                            <MessageBarBody>
                                <MessageBarTitle>Document Locked</MessageBarTitle>
                                This document is currently being edited by {lockError.lockedByName}.
                                You can view but not edit until they release it.
                            </MessageBarBody>
                            <MessageBarActions>
                                <Button
                                    appearance="transparent"
                                    icon={<ArrowClockwise24Regular />}
                                    onClick={handleRefresh}
                                >
                                    Refresh
                                </Button>
                            </MessageBarActions>
                        </MessageBar>
                    </div>
                )}

                {/* Preview/Edit area */}
                <div className="spe-document-viewer-preview">
                    {/* Iframe timeout message with retry */}
                    {isIframeTimedOut && (
                        <div className="spe-document-viewer-iframe-loading">
                            <MessageBar intent="warning" style={{ maxWidth: '400px' }}>
                                <MessageBarBody>
                                    <MessageBarTitle>Preview Timed Out</MessageBarTitle>
                                    The document preview took too long to load. This may be due to a slow connection or a large file.
                                </MessageBarBody>
                                <MessageBarActions>
                                    <Button
                                        appearance="transparent"
                                        icon={<ArrowClockwise24Regular />}
                                        onClick={handleRetry}
                                    >
                                        Retry
                                    </Button>
                                </MessageBarActions>
                            </MessageBar>
                        </div>
                    )}

                    {/* Loading overlay */}
                    {isIframeLoading && !isIframeTimedOut && (
                        <div className="spe-document-viewer-iframe-loading">
                            <Spinner size="medium" label={viewMode === 'edit' ? 'Loading editor...' : 'Loading preview...'} />
                        </div>
                    )}

                    {/* Document iframe (preview or edit URL) */}
                    {iframeSrc && (
                        <iframe
                            ref={iframeRef}
                            src={iframeSrc}
                            className="spe-document-viewer-iframe"
                            style={{ visibility: isIframeLoading || isIframeTimedOut ? 'hidden' : 'visible' }}
                            onLoad={handleIframeLoad}
                            onError={handleIframeError}
                            title={documentInfo?.name || 'Document'}
                            sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
                            allow="autoplay"
                        />
                    )}
                </div>

                {/* Version footer - REQUIRED per PCF CLAUDE.md */}
                <div className="spe-document-viewer-footer">
                    <span className="spe-document-viewer-version">
                        v{CONTROL_VERSION} - Built {BUILD_DATE}
                    </span>
                </div>
            </div>

            {/* Check-In Dialog */}
            <CheckInDialog
                isOpen={isCheckInDialogOpen}
                documentName={documentInfo?.name || 'Document'}
                isLoading={isCheckInLoading}
                onConfirm={handleCheckInConfirm}
                onCancel={handleCheckInCancel}
            />

            {/* Discard Confirmation Dialog */}
            <DiscardConfirmDialog
                isOpen={isDiscardDialogOpen}
                documentName={documentInfo?.name || 'Document'}
                isLoading={isDiscardLoading}
                onConfirm={handleDiscardConfirm}
                onCancel={handleDiscardCancel}
            />
        </FluentProvider>
    );
};

export default DocumentViewerApp;
