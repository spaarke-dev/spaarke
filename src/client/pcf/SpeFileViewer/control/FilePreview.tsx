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
    MessageBarType
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
/** Timeout duration for iframe loading (10 seconds) */
const IFRAME_LOAD_TIMEOUT_MS = 10000;

export class FilePreview extends React.Component<FilePreviewProps, FilePreviewState> {
    private bffClient: BffClient;

    /** Timeout ID for iframe load timeout */
    private iframeLoadTimeoutId: ReturnType<typeof setTimeout> | null = null;

    constructor(props: FilePreviewProps) {
        super(props);

        // Initialize state
        this.state = {
            previewUrl: null,
            isLoading: true,
            isIframeLoading: false,
            isEditLoading: false,
            isWebLoading: false,
            error: null,
            documentInfo: null
        };

        // Initialize BFF client
        this.bffClient = new BffClient(props.bffApiUrl);
    }

    /**
     * Cleanup timeout on unmount
     */
    public componentWillUnmount(): void {
        this.clearIframeLoadTimeout();
    }

    /**
     * Clear iframe load timeout
     */
    private clearIframeLoadTimeout(): void {
        if (this.iframeLoadTimeoutId) {
            clearTimeout(this.iframeLoadTimeoutId);
            this.iframeLoadTimeoutId = null;
        }
    }

    /**
     * Start iframe load timeout
     * Called after setting iframe src
     */
    private startIframeLoadTimeout(): void {
        this.clearIframeLoadTimeout();

        this.iframeLoadTimeoutId = setTimeout(() => {
            console.error('[FilePreview] Iframe load timeout - document took too long to load');
            this.setState({
                isIframeLoading: false,
                error: 'Document load timeout. Please try again.'
            });
        }, IFRAME_LOAD_TIMEOUT_MS);
    }

    /**
     * Handle iframe load success
     */
    private handleIframeLoad = (): void => {
        this.clearIframeLoadTimeout();
        console.log('[FilePreview] Iframe loaded successfully');

        this.setState({
            isIframeLoading: false
        });
    };

    /**
     * Handle iframe load error
     */
    private handleIframeError = (): void => {
        this.clearIframeLoadTimeout();
        console.error('[FilePreview] Iframe failed to load');

        this.setState({
            isIframeLoading: false,
            error: 'Failed to load document preview. Please try again.'
        });
    };

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
        this.clearIframeLoadTimeout();
        this.setState({
            isLoading: true,
            isIframeLoading: false,
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

            // Update state with preview URL - keep loading until iframe loads
            this.setState({
                previewUrl: response.previewUrl,
                documentInfo: {
                    name: response.documentInfo.name,
                    fileExtension: response.documentInfo.fileExtension,
                    size: response.documentInfo.size
                },
                isLoading: false,
                isIframeLoading: true, // Wait for iframe to load
                error: null
            });

            // Start timeout for iframe loading
            this.startIframeLoadTimeout();

            console.log(`[FilePreview] Preview URL received: ${response.documentInfo.name}, waiting for iframe...`);

        } catch (error) {
            // Handle error
            const errorMessage = error instanceof Error ? error.message : String(error);
            console.error('[FilePreview] Failed to load preview:', errorMessage);

            this.setState({
                isLoading: false,
                isIframeLoading: false,
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
     * Office files can be opened in desktop Office applications.
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
     * Handle "Edit in Desktop" button click
     *
     * Opens the document in the native Office desktop application (Word, Excel, PowerPoint).
     * Calls BFF /open-links endpoint and launches desktop app via protocol URL.
     */
    private handleEditInDesktop = async (): Promise<void> => {
        const { documentId, accessToken, correlationId } = this.props;
        const { documentInfo } = this.state;

        console.log(`[FilePreview] Edit in Desktop clicked for document: ${documentId}`);
        console.log(`[FilePreview] File: ${documentInfo?.name} (${documentInfo?.fileExtension})`);

        // Set loading state
        this.setState({ isEditLoading: true });

        try {
            // Call BFF API to get open links
            const response = await this.bffClient.getOpenLinks(
                documentId,
                accessToken,
                correlationId
            );

            console.log(`[FilePreview] Open links received for: ${response.fileName}`);

            // Check if desktop URL is available
            if (!response.desktopUrl) {
                console.warn('[FilePreview] No desktop URL available for this file type');
                this.setState({
                    isEditLoading: false,
                    error: 'This file type cannot be opened in a desktop application.'
                });
                return;
            }

            // Launch desktop application using protocol URL
            // Protocol URLs like ms-word:ofe|u|{url} trigger the native Office app
            console.log('[FilePreview] Launching desktop application...');
            window.location.href = response.desktopUrl;

            // Clear loading state after a short delay
            // The page may navigate away, but if it doesn't (e.g., app not installed),
            // we should reset the loading state
            setTimeout(() => {
                this.setState({ isEditLoading: false });
            }, 1000);

        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : String(error);
            console.error('[FilePreview] Failed to get open links:', errorMessage);

            this.setState({
                isEditLoading: false,
                error: `Failed to open in desktop: ${errorMessage}`
            });
        }
    };

    /**
     * Render pencil/edit icon SVG
     */
    private renderEditIcon(): React.ReactNode {
        return (
            <svg
                className="spe-file-viewer__edit-icon"
                viewBox="0 0 16 16"
                width="16"
                height="16"
                fill="currentColor"
                aria-hidden="true"
            >
                <path d="M12.1 3.9l.8.8-7.8 7.8H4.3v-.8l7.8-7.8zm1.1-1.1l.5.5c.3.3.3.8 0 1.1l-8.5 8.5c-.1.1-.3.2-.5.2H3v-1.7c0-.2.1-.4.2-.5l8.5-8.5c.3-.3.8-.3 1.1 0l.4.4z" />
            </svg>
        );
    }

    /**
     * Render globe/web icon SVG
     */
    private renderWebIcon(): React.ReactNode {
        return (
            <svg
                className="spe-file-viewer__web-icon"
                viewBox="0 0 16 16"
                width="16"
                height="16"
                fill="currentColor"
                aria-hidden="true"
            >
                <path d="M8 1a7 7 0 1 0 7 7 7 7 0 0 0-7-7zm5.9 6.5h-2.6a10.3 10.3 0 0 0-.8-3.8 6 6 0 0 1 3.4 3.8zM8 2a5.5 5.5 0 0 1 1.3 4.5H6.7A5.5 5.5 0 0 1 8 2zm-2.5 1.7a10.3 10.3 0 0 0-.8 3.8H2.1a6 6 0 0 1 3.4-3.8zM2.1 8.5h2.6a10.3 10.3 0 0 0 .8 3.8 6 6 0 0 1-3.4-3.8zM8 14a5.5 5.5 0 0 1-1.3-4.5h2.6A5.5 5.5 0 0 1 8 14zm2.5-1.7a10.3 10.3 0 0 0 .8-3.8h2.6a6 6 0 0 1-3.4 3.8z" />
            </svg>
        );
    }

    /**
     * Handle "Open in Web" button click
     *
     * Opens the document in Office Online in a new browser tab.
     * Provides an alternative for users without desktop Office apps.
     */
    private handleOpenInWeb = async (): Promise<void> => {
        const { documentId, accessToken, correlationId } = this.props;
        const { documentInfo } = this.state;

        console.log(`[FilePreview] Open in Web clicked for document: ${documentId}`);
        console.log(`[FilePreview] File: ${documentInfo?.name} (${documentInfo?.fileExtension})`);

        // Set loading state
        this.setState({ isWebLoading: true });

        try {
            // Call BFF API to get open links
            const response = await this.bffClient.getOpenLinks(
                documentId,
                accessToken,
                correlationId
            );

            console.log(`[FilePreview] Open links received for: ${response.fileName}`);

            // Check if web URL is available
            if (!response.webUrl) {
                console.warn('[FilePreview] No web URL available for this file');
                this.setState({
                    isWebLoading: false,
                    error: 'Unable to get web URL for this document.'
                });
                return;
            }

            // Open in new tab
            console.log('[FilePreview] Opening in Office Online...');
            window.open(response.webUrl, '_blank', 'noopener,noreferrer');

            // Clear loading state
            this.setState({ isWebLoading: false });

        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : String(error);
            console.error('[FilePreview] Failed to get open links:', errorMessage);

            this.setState({
                isWebLoading: false,
                error: `Failed to open in web: ${errorMessage}`
            });
        }
    };

    /**
     * Render component
     */
    public render(): React.ReactNode {
        const {
            isLoading,
            isIframeLoading,
            isEditLoading,
            isWebLoading,
            error,
            previewUrl,
            documentInfo
        } = this.state;

        // Show loading if API is loading OR iframe is loading
        const showSpinner = isLoading || isIframeLoading;

        return (
            <div className="spe-file-viewer">
                {/* Loading State - API fetch or iframe loading */}
                {showSpinner && (
                    <div className="spe-file-viewer__loading">
                        <Spinner
                            size={SpinnerSize.large}
                            label={isLoading ? 'Loading preview...' : 'Rendering document...'}
                            ariaLive="assertive"
                        />
                    </div>
                )}

                {/* Error State */}
                {!showSpinner && error && (
                    <div className="spe-file-viewer__error">
                        <MessageBar
                            messageBarType={MessageBarType.error}
                            isMultiline={true}
                        >
                            <strong>Unable to load file preview</strong>
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

                {/* Preview State - Show when URL available (even if iframe still loading) */}
                {!isLoading && !error && previewUrl && (
                    <div className="spe-file-viewer__preview">
                        {/* Action Buttons Header - Only show when iframe is loaded */}
                        {!isIframeLoading && documentInfo && (
                            <div className="spe-file-viewer__actions">
                                {/* Open in Desktop Button - Only for Office files */}
                                {this.isOfficeFile(documentInfo.fileExtension) && (
                                    <button
                                        className={`spe-file-viewer__action-button spe-file-viewer__action-button--primary spe-file-viewer__edit-btn${isEditLoading ? ' spe-file-viewer__action-button--loading' : ''}`}
                                        onClick={this.handleEditInDesktop}
                                        disabled={isEditLoading || isWebLoading}
                                        aria-label={isEditLoading ? 'Opening in desktop application...' : 'Open in desktop application'}
                                        title={isEditLoading ? 'Opening...' : 'Open in Word, Excel, or PowerPoint'}
                                        data-testid="edit-in-desktop-btn"
                                    >
                                        {isEditLoading ? (
                                            <span className="spe-file-viewer__button-spinner" aria-hidden="true"></span>
                                        ) : (
                                            this.renderEditIcon()
                                        )}
                                        <span>{isEditLoading ? 'Opening...' : 'Open in Desktop'}</span>
                                    </button>
                                )}
                                {/* Open in Web Button - For users without desktop Office */}
                                {this.isOfficeFile(documentInfo.fileExtension) && (
                                    <button
                                        className={`spe-file-viewer__action-button spe-file-viewer__action-button--secondary spe-file-viewer__web-btn${isWebLoading ? ' spe-file-viewer__action-button--loading' : ''}`}
                                        onClick={this.handleOpenInWeb}
                                        disabled={isEditLoading || isWebLoading}
                                        aria-label={isWebLoading ? 'Opening in browser...' : 'Open in browser'}
                                        title={isWebLoading ? 'Opening...' : 'Open in Office Online (browser)'}
                                        data-testid="open-in-web-btn"
                                    >
                                        {isWebLoading ? (
                                            <span className="spe-file-viewer__button-spinner" aria-hidden="true"></span>
                                        ) : (
                                            this.renderWebIcon()
                                        )}
                                        <span>{isWebLoading ? 'Opening...' : 'Open in Web'}</span>
                                    </button>
                                )}
                            </div>
                        )}

                        {/* Preview Iframe */}
                        {/* Hidden while loading, visible when loaded */}
                        <iframe
                            className="spe-file-viewer__iframe"
                            style={{ visibility: isIframeLoading ? 'hidden' : 'visible' }}
                            src={previewUrl}
                            title="File Preview"
                            sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
                            allow="autoplay"
                            onLoad={this.handleIframeLoad}
                            onError={this.handleIframeError}
                        />
                    </div>
                )}

                {/* Empty State */}
                {!showSpinner && !error && !previewUrl && (
                    <div className="spe-file-viewer__empty">
                        <MessageBar messageBarType={MessageBarType.info}>
                            No document selected
                        </MessageBar>
                    </div>
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
