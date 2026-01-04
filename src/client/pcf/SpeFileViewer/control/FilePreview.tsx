/**
 * FilePreview React Component
 *
 * Displays SharePoint file preview in an iframe using BFF-provided preview URL.
 * Uses Fluent UI v9 for Power Apps model-driven app style consistency.
 * Supports dark mode via global theme settings (command bar menu).
 *
 * Note: Per-control theme toggle removed in favor of global theme menu.
 * See: projects/mda-darkmode-theme/spec.md
 */

import * as React from 'react';
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Button,
    Spinner,
    MessageBar,
    MessageBarBody,
    MessageBarTitle,
    tokens
} from '@fluentui/react-components';
import {
    EditRegular,
    GlobeRegular,
    ArrowClockwiseRegular,
    LockClosedRegular
} from '@fluentui/react-icons';
import { FilePreviewProps, FilePreviewState } from './types';
import { BffClient } from './BffClient';

/** Timeout duration for iframe loading (10 seconds) */
const IFRAME_LOAD_TIMEOUT_MS = 10000;

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
            documentInfo: null,
            checkoutStatus: undefined
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
            documentInfo: null,
            checkoutStatus: undefined
        });

        console.log(`[FilePreview] Loading view URL for document: ${documentId}`);

        try {
            // Call BFF API - use view-url endpoint for real-time viewing (no cache)
            const response = await this.bffClient.getViewUrl(
                documentId,
                accessToken,
                correlationId
            );

            // Update state with view URL and checkout status
            this.setState({
                previewUrl: response.previewUrl,
                documentInfo: {
                    name: response.documentInfo.name,
                    fileExtension: response.documentInfo.fileExtension,
                    size: response.documentInfo.size
                },
                checkoutStatus: response.checkoutStatus,
                isLoading: false,
                isIframeLoading: true,
                error: null
            });

            // Start timeout for iframe loading
            this.startIframeLoadTimeout();

            console.log(`[FilePreview] View URL received: ${response.documentInfo.name}`);
            if (response.checkoutStatus?.isCheckedOut) {
                console.log(`[FilePreview] Document checked out by: ${response.checkoutStatus.checkedOutBy?.name}`);
            }

        } catch (error) {
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
     * Handle refresh button click
     */
    private handleRefresh = async (): Promise<void> => {
        console.log('[FilePreview] Refresh clicked');

        // Call parent refresh callback if provided
        if (this.props.onRefresh) {
            this.props.onRefresh();
        }

        // Reload the preview
        await this.loadPreview();
    };

    /**
     * Retry loading preview (for error recovery)
     */
    private handleRetry = async (): Promise<void> => {
        await this.loadPreview();
    };

    /**
     * Check if file extension is a Microsoft Office type
     */
    private isOfficeFile(extension?: string): boolean {
        if (!extension) {
            return false;
        }

        const officeExtensions = [
            'docx', 'doc', 'docm', 'dot', 'dotx', 'dotm',
            'xlsx', 'xls', 'xlsm', 'xlsb', 'xlt', 'xltx', 'xltm',
            'pptx', 'ppt', 'pptm', 'pot', 'potx', 'potm', 'pps', 'ppsx', 'ppsm'
        ];

        return officeExtensions.includes(extension.toLowerCase());
    }

    /**
     * Handle "Open in Desktop" button click
     */
    private handleEditInDesktop = async (): Promise<void> => {
        const { documentId, accessToken, correlationId } = this.props;
        const { documentInfo } = this.state;

        console.log(`[FilePreview] Open in Desktop clicked for: ${documentInfo?.name}`);

        this.setState({ isEditLoading: true });

        try {
            const response = await this.bffClient.getOpenLinks(
                documentId,
                accessToken,
                correlationId
            );

            if (!response.desktopUrl) {
                this.setState({
                    isEditLoading: false,
                    error: 'This file type cannot be opened in a desktop application.'
                });
                return;
            }

            console.log('[FilePreview] Launching desktop application...');
            window.location.href = response.desktopUrl;

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
     * Handle "Open in Web" button click
     */
    private handleOpenInWeb = async (): Promise<void> => {
        const { documentId, accessToken, correlationId } = this.props;
        const { documentInfo } = this.state;

        console.log(`[FilePreview] Open in Web clicked for: ${documentInfo?.name}`);

        this.setState({ isWebLoading: true });

        try {
            const response = await this.bffClient.getOpenLinks(
                documentId,
                accessToken,
                correlationId
            );

            if (!response.webUrl) {
                this.setState({
                    isWebLoading: false,
                    error: 'Unable to get web URL for this document.'
                });
                return;
            }

            console.log('[FilePreview] Opening in Office Online...');
            window.open(response.webUrl, '_blank', 'noopener,noreferrer');

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
            documentInfo,
            checkoutStatus
        } = this.state;

        // Use isDarkTheme from props (controlled by global theme menu)
        const { isDarkTheme } = this.props;

        // Select theme based on dark mode setting
        const theme = isDarkTheme ? webDarkTheme : webLightTheme;

        // Show loading if API is loading OR iframe is loading
        const showSpinner = isLoading || isIframeLoading;

        // Any button loading state
        const anyButtonLoading = isEditLoading || isWebLoading;

        // Checkout status display
        const isCheckedOut = checkoutStatus?.isCheckedOut ?? false;
        const checkedOutByName = checkoutStatus?.checkedOutBy?.name ?? 'Unknown';
        const isCheckedOutByCurrentUser = checkoutStatus?.isCurrentUser ?? false;

        return (
            <FluentProvider theme={theme} style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
                <div className={`spe-file-viewer ${isDarkTheme ? 'spe-file-viewer--dark' : ''}`}>
                    {/* Loading State */}
                    {showSpinner && (
                        <div className="spe-file-viewer__loading">
                            <Spinner
                                size="large"
                                label={isLoading ? 'Loading preview...' : 'Rendering document...'}
                            />
                        </div>
                    )}

                    {/* Error State */}
                    {!showSpinner && error && (
                        <div className="spe-file-viewer__error">
                            <MessageBar intent="error">
                                <MessageBarBody>
                                    <MessageBarTitle>Unable to load file preview</MessageBarTitle>
                                    {error}
                                </MessageBarBody>
                            </MessageBar>
                            <Button
                                appearance="outline"
                                onClick={this.handleRetry}
                                style={{ marginTop: tokens.spacingVerticalM }}
                            >
                                Retry
                            </Button>
                        </div>
                    )}

                    {/* Preview State */}
                    {!isLoading && !error && previewUrl && (
                        <div className="spe-file-viewer__preview">
                            {/* Action Buttons Header */}
                            {!isIframeLoading && documentInfo && (
                                <div className="spe-file-viewer__actions">
                                    {/* Refresh Button - using native title for PCF compatibility */}
                                    <Button
                                        appearance="outline"
                                        icon={<ArrowClockwiseRegular />}
                                        onClick={this.handleRefresh}
                                        disabled={anyButtonLoading}
                                        aria-label="Refresh preview"
                                        title="Refresh preview"
                                        data-testid="refresh-btn"
                                    >
                                        Refresh
                                    </Button>

                                    {/* Checkout Status Badge - using native title for PCF compatibility */}
                                    {isCheckedOut ? (
                                        <div
                                            style={{
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: tokens.spacingHorizontalXS,
                                                padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
                                                backgroundColor: isCheckedOutByCurrentUser
                                                    ? tokens.colorPaletteGreenBackground2
                                                    : tokens.colorPaletteYellowBackground2,
                                                borderRadius: tokens.borderRadiusMedium,
                                                fontSize: tokens.fontSizeBase200,
                                                fontWeight: tokens.fontWeightSemibold
                                            }}
                                            title={isCheckedOutByCurrentUser
                                                ? 'You have this document checked out'
                                                : `Checked out by ${checkedOutByName}`}
                                        >
                                            <LockClosedRegular />
                                            <span>
                                                {isCheckedOutByCurrentUser
                                                    ? 'Checked out by you'
                                                    : `Checked out by ${checkedOutByName}`}
                                            </span>
                                        </div>
                                    ) : (
                                        <div
                                            style={{
                                                display: 'flex',
                                                alignItems: 'center',
                                                gap: tokens.spacingHorizontalXS,
                                                padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
                                                backgroundColor: tokens.colorNeutralBackground3,
                                                borderRadius: tokens.borderRadiusMedium,
                                                fontSize: tokens.fontSizeBase200,
                                                color: tokens.colorNeutralForeground2
                                            }}
                                            title="Document is checked in and available for checkout"
                                        >
                                            <span>Checked In</span>
                                        </div>
                                    )}

                                    {/* Spacer */}
                                    <div style={{ flex: 1 }} />

                                    {/* Open in Desktop Button - Only for Office files */}
                                    {this.isOfficeFile(documentInfo.fileExtension) && (
                                        <Button
                                            appearance="outline"
                                            icon={isEditLoading ? <Spinner size="tiny" /> : <EditRegular />}
                                            onClick={this.handleEditInDesktop}
                                            disabled={anyButtonLoading}
                                            aria-label={isEditLoading ? 'Opening...' : 'Open in Desktop'}
                                            title="Open in Word, Excel, or PowerPoint"
                                            data-testid="edit-in-desktop-btn"
                                        >
                                            {isEditLoading ? 'Opening...' : 'Open in Desktop'}
                                        </Button>
                                    )}

                                    {/* Open in Web Button */}
                                    {this.isOfficeFile(documentInfo.fileExtension) && (
                                        <Button
                                            appearance="outline"
                                            icon={isWebLoading ? <Spinner size="tiny" /> : <GlobeRegular />}
                                            onClick={this.handleOpenInWeb}
                                            disabled={anyButtonLoading}
                                            aria-label={isWebLoading ? 'Opening...' : 'Open in Web'}
                                            title="Open in Office Online (browser)"
                                            data-testid="open-in-web-btn"
                                        >
                                            {isWebLoading ? 'Opening...' : 'Open in Web'}
                                        </Button>
                                    )}
                                </div>
                            )}

                            {/* Preview Iframe */}
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
                            <MessageBar intent="info">
                                <MessageBarBody>No document selected</MessageBarBody>
                            </MessageBar>
                        </div>
                    )}

                    {/* Version Footer */}
                    <div
                        style={{
                            position: 'absolute',
                            bottom: tokens.spacingVerticalXS,
                            right: tokens.spacingHorizontalS,
                            fontSize: tokens.fontSizeBase100,
                            color: tokens.colorNeutralForeground4,
                            opacity: 0.7
                        }}
                    >
                        v2.0.1 - Built 2026-01-03
                    </div>
                </div>
            </FluentProvider>
        );
    }
}
