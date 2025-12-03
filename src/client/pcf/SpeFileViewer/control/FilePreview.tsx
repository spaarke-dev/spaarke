/**
 * FilePreview React Component
 *
 * Displays SharePoint file preview in an iframe using BFF-provided preview URL
 */

import * as React from 'react';
import {
    Spinner,
    SpinnerSize,
    MessageBar,
    MessageBarType,
    Dialog,
    DialogType,
    DialogFooter,
    PrimaryButton
} from '@fluentui/react';
import { FilePreviewProps, FilePreviewState } from './types';
import { BffClient } from './BffClient';

/**
 * FilePreview component
 *
 * States:
 * - Loading: Fetching preview URL from BFF
 * - Preview: Displaying SharePoint preview iframe
 * - Error: Displaying error message
 */
export class FilePreview extends React.Component<FilePreviewProps, FilePreviewState> {
    private bffClient: BffClient;

    constructor(props: FilePreviewProps) {
        super(props);

        // Initialize state
        this.state = {
            previewUrl: null,
            officeUrl: null,
            isLoading: true,
            error: null,
            documentInfo: null,
            mode: 'preview',
            showReadOnlyDialog: false
        };

        // Initialize BFF client
        this.bffClient = new BffClient(props.bffApiUrl);
    }

    /**
     * Load preview URL when component mounts
     */
    public async componentDidMount(): Promise<void> {
        await this.loadPreview();
    }

    /**
     * Reload preview when documentId changes
     */
    public async componentDidUpdate(prevProps: FilePreviewProps): Promise<void> {
        if (prevProps.documentId !== this.props.documentId) {
            console.log('[FilePreview] Document ID changed, reloading preview...');
            await this.loadPreview();
        }
    }

    /**
     * Load preview URL from BFF
     */
    private async loadPreview(): Promise<void> {
        const { documentId, accessToken, correlationId } = this.props;

        // Validate documentId
        if (!documentId || documentId.trim() === '') {
            this.setState({
                isLoading: false,
                error: 'No document selected',
                previewUrl: null,
                documentInfo: null
            });
            return;
        }

        // Reset state to loading
        this.setState({
            isLoading: true,
            error: null,
            previewUrl: null,
            documentInfo: null
        });

        console.log(`[FilePreview] Loading preview for document: ${documentId}`);
        console.log(`[FilePreview] Correlation ID: ${correlationId}`);

        try {
            // Call BFF API
            const response = await this.bffClient.getPreviewUrl(
                documentId,
                accessToken,
                correlationId
            );

            // Update state with preview URL
            this.setState({
                previewUrl: response.previewUrl,
                documentInfo: {
                    name: response.documentInfo.name,
                    fileExtension: response.documentInfo.fileExtension,
                    size: response.documentInfo.size
                },
                isLoading: false,
                error: null
            });

            console.log(`[FilePreview] Preview loaded: ${response.documentInfo.name}`);

        } catch (error) {
            // Handle error
            const errorMessage = error instanceof Error ? error.message : String(error);
            console.error('[FilePreview] Failed to load preview:', errorMessage);

            this.setState({
                isLoading: false,
                error: errorMessage,
                previewUrl: null,
                documentInfo: null
            });
        }
    }

    /**
     * Retry loading preview (for error recovery)
     */
    private handleRetry = async (): Promise<void> => {
        await this.loadPreview();
    };

    /**
     * Check if file extension is a Microsoft Office type
     *
     * Office files can be opened in Office Online editor mode.
     * Other file types (PDF, images, etc.) only support preview.
     *
     * @param extension File extension (e.g., "docx", "xlsx", "pdf")
     * @returns true if Office file, false otherwise
     */
    private isOfficeFile(extension?: string): boolean {
        if (!extension) {
            return false;
        }

        const officeExtensions = [
            // Word
            'docx', 'doc', 'docm', 'dot', 'dotx', 'dotm',
            // Excel
            'xlsx', 'xls', 'xlsm', 'xlsb', 'xlt', 'xltx', 'xltm',
            // PowerPoint
            'pptx', 'ppt', 'pptm', 'pot', 'potx', 'potm', 'pps', 'ppsx', 'ppsm'
        ];

        return officeExtensions.includes(extension.toLowerCase());
    }

    /**
     * Open file in Office Online editor mode
     *
     * Workflow:
     * 1. Call BFF API to get Office URL
     * 2. Switch iframe to editor mode
     * 3. Show read-only dialog if user lacks edit permissions
     *
     * @throws Error if API call fails (handled by setState)
     */
    private handleOpenEditor = async (): Promise<void> => {
        const { documentId, accessToken, correlationId } = this.props;

        console.log(`[FilePreview] Opening editor for document: ${documentId}`);

        // Set loading state
        this.setState({
            isLoading: true,
            error: null
        });

        try {
            // Call BFF API to get Office URL
            const response = await this.bffClient.getOfficeUrl(
                documentId,
                accessToken,
                correlationId
            );

            // Update state to editor mode
            this.setState({
                officeUrl: response.officeUrl,
                mode: 'editor',
                isLoading: false,
                // Show dialog if user has read-only access
                showReadOnlyDialog: !response.permissions.canEdit
            });

            console.log(
                `[FilePreview] Editor opened | CanEdit: ${response.permissions.canEdit} | Role: ${response.permissions.role}`
            );

            // Log permission details for debugging
            if (!response.permissions.canEdit) {
                console.warn(
                    `[FilePreview] User has read-only access. Office Online will load in read-only mode.`
                );
            }

        } catch (error) {
            // Handle API errors
            const errorMessage = error instanceof Error ? error.message : String(error);
            console.error('[FilePreview] Failed to open editor:', errorMessage);

            this.setState({
                isLoading: false,
                error: errorMessage,
                mode: 'preview' // Stay in preview mode on error
            });
        }
    };

    /**
     * Return to preview mode from editor mode
     *
     * Resets state to show preview iframe and hides read-only dialog.
     */
    private handleBackToPreview = (): void => {
        console.log('[FilePreview] Returning to preview mode');

        this.setState({
            mode: 'preview',
            showReadOnlyDialog: false
        });
    };

    /**
     * Dismiss the read-only permission dialog
     *
     * User can dismiss the dialog and continue using editor
     * in read-only mode (Office Online enforces this).
     */
    private dismissReadOnlyDialog = (): void => {
        console.log('[FilePreview] Dismissing read-only dialog');

        this.setState({
            showReadOnlyDialog: false
        });
    };

    /**
     * Render component
     */
    public render(): React.ReactNode {
        const {
            isLoading,
            error,
            previewUrl,
            officeUrl,
            documentInfo,
            mode,
            showReadOnlyDialog
        } = this.state;

        return (
            <div className="spe-file-viewer">
                {/* Loading State */}
                {isLoading && (
                    <div className="spe-file-viewer__loading">
                        <Spinner
                            size={SpinnerSize.large}
                            label={mode === 'editor' ? 'Loading editor...' : 'Loading preview...'}
                            ariaLive="assertive"
                        />
                    </div>
                )}

                {/* Error State */}
                {!isLoading && error && (
                    <div className="spe-file-viewer__error">
                        <MessageBar
                            messageBarType={MessageBarType.error}
                            isMultiline={true}
                        >
                            <strong>
                                Unable to load file {mode === 'editor' ? 'editor' : 'preview'}
                            </strong>
                            <p>{error}</p>
                        </MessageBar>
                        <button
                            className="spe-file-viewer__retry-button"
                            onClick={this.handleRetry}
                        >
                            Retry
                        </button>
                    </div>
                )}

                {/* Preview/Editor State */}
                {!isLoading && !error && (previewUrl || officeUrl) && (
                    <div className="spe-file-viewer__preview">
                        {/* Action Buttons Header */}
                        <div className="spe-file-viewer__actions">
                            {/* Open in Editor Button (Preview Mode + Office Files Only) */}
                            {mode === 'preview' && this.isOfficeFile(documentInfo?.fileExtension) && (
                                <button
                                    className="spe-file-viewer__action-button spe-file-viewer__action-button--primary"
                                    onClick={this.handleOpenEditor}
                                    aria-label="Open in Office Online Editor"
                                    title="Edit this document in Office Online"
                                >
                                    Open in Editor
                                </button>
                            )}

                            {/* Back to Preview Button (Editor Mode Only) */}
                            {mode === 'editor' && (
                                <button
                                    className="spe-file-viewer__action-button spe-file-viewer__action-button--secondary"
                                    onClick={this.handleBackToPreview}
                                    aria-label="Return to preview mode"
                                    title="Return to read-only preview"
                                >
                                    ← Back to Preview
                                </button>
                            )}
                        </div>

                        {/* Iframe - Dynamic src based on mode */}
                        <iframe
                            className="spe-file-viewer__iframe"
                            src={mode === 'editor' ? officeUrl! : previewUrl!}
                            title={mode === 'editor' ? 'Office Editor' : 'File Preview'}
                            sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
                            allow="autoplay"
                        />
                    </div>
                )}

                {/* Empty State */}
                {!isLoading && !error && !previewUrl && !officeUrl && (
                    <div className="spe-file-viewer__empty">
                        <MessageBar messageBarType={MessageBarType.info}>
                            No document selected
                        </MessageBar>
                    </div>
                )}

                {/* Read-Only Permission Dialog */}
                {showReadOnlyDialog && (
                    <Dialog
                        hidden={!showReadOnlyDialog}
                        onDismiss={this.dismissReadOnlyDialog}
                        dialogContentProps={{
                            type: DialogType.normal,
                            title: 'File Opened in Read-Only Mode',
                            subText: 'You have view-only access to this file. To make changes, contact the file owner to request edit permissions.'
                        }}
                        modalProps={{
                            isBlocking: false,
                            styles: { main: { maxWidth: 450 } }
                        }}
                    >
                        <DialogFooter>
                            <PrimaryButton
                                onClick={this.dismissReadOnlyDialog}
                                text="OK"
                            />
                        </DialogFooter>
                    </Dialog>
                )}
            </div>
        );
    }

    /**
     * Format file size for display
     */
    private formatFileSize(bytes: number): string {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
        return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
    }
}
