/**
 * Source Document Viewer Component
 *
 * Displays the original document in the center panel of Analysis Workspace.
 * Uses BFF API to get preview URL and displays in an iframe.
 *
 * Task 055: Integrate SpeFileViewer for Source Preview
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
    MessageBar,
    MessageBarBody,
    Button,
    Tooltip
} from "@fluentui/react-components";
import {
    DocumentRegular,
    ArrowClockwiseRegular,
    OpenRegular,
    FullScreenMaximize24Regular
} from "@fluentui/react-icons";
import { logInfo, logError } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface ISourceDocumentViewerProps {
    /** Document ID from Dataverse */
    documentId: string;
    /** SharePoint container ID */
    containerId: string;
    /** SharePoint file ID */
    fileId: string;
    /** BFF API base URL */
    apiBaseUrl: string;
    /** Function to get access token for API calls */
    getAccessToken?: () => Promise<string>;
    /** Callback when fullscreen is requested */
    onFullscreen?: () => void;
}

interface IDocumentInfo {
    name: string;
    fileExtension?: string;
    size?: number;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        width: "100%",
        overflow: "hidden"
    },
    loadingContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        gap: tokens.spacingVerticalM
    },
    errorContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        padding: tokens.spacingHorizontalL,
        gap: tokens.spacingVerticalM
    },
    emptyContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorNeutralForeground3,
        gap: tokens.spacingVerticalM
    },
    emptyIcon: {
        fontSize: "48px"
    },
    toolbar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2
    },
    toolbarInfo: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flex: 1,
        overflow: "hidden"
    },
    toolbarFileName: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap" as const
    },
    toolbarActions: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS
    },
    iframeContainer: {
        flex: 1,
        position: "relative" as const,
        overflow: "hidden"
    },
    iframe: {
        width: "100%",
        height: "100%",
        border: "none"
    },
    iframeLoading: {
        position: "absolute" as const,
        top: "50%",
        left: "50%",
        transform: "translate(-50%, -50%)"
    }
});

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

const IFRAME_LOAD_TIMEOUT_MS = 15000;

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const SourceDocumentViewer: React.FC<ISourceDocumentViewerProps> = ({
    documentId,
    containerId,
    fileId,
    apiBaseUrl,
    getAccessToken,
    onFullscreen
}) => {
    const styles = useStyles();

    // State
    const [isLoading, setIsLoading] = React.useState(false);
    const [isIframeLoading, setIsIframeLoading] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);
    const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
    const [documentInfo, setDocumentInfo] = React.useState<IDocumentInfo | null>(null);

    // Refs
    const timeoutRef = React.useRef<NodeJS.Timeout | null>(null);

    // Cleanup timeout on unmount
    React.useEffect(() => {
        return () => {
            if (timeoutRef.current) {
                clearTimeout(timeoutRef.current);
            }
        };
    }, []);

    // Load preview when document changes
    React.useEffect(() => {
        if (documentId && containerId && fileId) {
            loadPreview();
        } else {
            setPreviewUrl(null);
            setDocumentInfo(null);
            setError(null);
        }
    }, [documentId, containerId, fileId]);

    /**
     * Load preview URL from BFF API
     */
    const loadPreview = async () => {
        if (!documentId) {
            setError("No document ID provided");
            return;
        }

        setIsLoading(true);
        setError(null);
        setPreviewUrl(null);

        logInfo("SourceDocumentViewer", `Loading preview for document: ${documentId}`);

        try {
            // Get access token if auth function provided
            let authHeaders: Record<string, string> = {};
            if (getAccessToken) {
                try {
                    const token = await getAccessToken();
                    authHeaders = { "Authorization": `Bearer ${token}` };
                    logInfo("SourceDocumentViewer", "Auth token acquired for preview");
                } catch (authErr) {
                    logError("SourceDocumentViewer", "Failed to acquire auth token", authErr);
                    throw new Error("Authentication failed. Please refresh and try again.");
                }
            }

            // Call BFF API to get preview URL
            const response = await fetch(`${apiBaseUrl}/documents/${documentId}/preview-url`, {
                method: "GET",
                headers: {
                    "Content-Type": "application/json",
                    "Accept": "application/json",
                    ...authHeaders
                }
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.detail || `HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json();

            setPreviewUrl(data.previewUrl);
            setDocumentInfo({
                name: data.documentInfo?.name || "Document",
                fileExtension: data.documentInfo?.fileExtension,
                size: data.documentInfo?.size
            });
            setIsIframeLoading(true);

            // Start timeout for iframe loading
            timeoutRef.current = setTimeout(() => {
                logError("SourceDocumentViewer", "Iframe load timeout");
                setIsIframeLoading(false);
                setError("Document preview timed out. Please try again.");
            }, IFRAME_LOAD_TIMEOUT_MS);

            logInfo("SourceDocumentViewer", `Preview URL received for: ${data.documentInfo?.name}`);

        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : String(err);
            logError("SourceDocumentViewer", "Failed to load preview", err);
            setError(errorMessage);
        } finally {
            setIsLoading(false);
        }
    };

    /**
     * Handle iframe load complete
     */
    const handleIframeLoad = () => {
        if (timeoutRef.current) {
            clearTimeout(timeoutRef.current);
            timeoutRef.current = null;
        }
        setIsIframeLoading(false);
        logInfo("SourceDocumentViewer", "Iframe loaded successfully");
    };

    /**
     * Handle iframe error
     */
    const handleIframeError = () => {
        if (timeoutRef.current) {
            clearTimeout(timeoutRef.current);
            timeoutRef.current = null;
        }
        setIsIframeLoading(false);
        setError("Failed to load document preview");
        logError("SourceDocumentViewer", "Iframe failed to load");
    };

    /**
     * Handle refresh button
     */
    const handleRefresh = () => {
        loadPreview();
    };

    /**
     * Handle open in new tab
     */
    const handleOpenInNewTab = () => {
        if (previewUrl) {
            window.open(previewUrl, "_blank", "noopener,noreferrer");
        }
    };

    /**
     * Format file size
     */
    const formatFileSize = (bytes?: number): string => {
        if (!bytes) return "";
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    // Empty state - no document selected
    if (!documentId || !containerId || !fileId) {
        return (
            <div className={styles.emptyContainer}>
                <DocumentRegular className={styles.emptyIcon} />
                <Text weight="semibold">No document selected</Text>
                <Text size={200}>
                    Select a document to preview the original source
                </Text>
            </div>
        );
    }

    // Loading state
    if (isLoading) {
        return (
            <div className={styles.loadingContainer}>
                <Spinner size="large" label="Loading document preview..." />
            </div>
        );
    }

    // Error state
    if (error) {
        return (
            <div className={styles.errorContainer}>
                <MessageBar intent="error">
                    <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
                <Button appearance="outline" onClick={handleRefresh}>
                    Retry
                </Button>
            </div>
        );
    }

    // Preview state
    return (
        <div className={styles.container}>
            {/* Toolbar */}
            {previewUrl && documentInfo && (
                <div className={styles.toolbar}>
                    <div className={styles.toolbarInfo}>
                        <DocumentRegular />
                        <Text className={styles.toolbarFileName} title={documentInfo.name}>
                            {documentInfo.name}
                        </Text>
                        {documentInfo.size && (
                            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
                                ({formatFileSize(documentInfo.size)})
                            </Text>
                        )}
                    </div>
                    <div className={styles.toolbarActions}>
                        <Tooltip content="Refresh" relationship="label">
                            <Button
                                icon={<ArrowClockwiseRegular />}
                                appearance="subtle"
                                size="small"
                                onClick={handleRefresh}
                            />
                        </Tooltip>
                        <Tooltip content="Open in new tab" relationship="label">
                            <Button
                                icon={<OpenRegular />}
                                appearance="subtle"
                                size="small"
                                onClick={handleOpenInNewTab}
                            />
                        </Tooltip>
                        {onFullscreen && (
                            <Tooltip content="Fullscreen" relationship="label">
                                <Button
                                    icon={<FullScreenMaximize24Regular />}
                                    appearance="subtle"
                                    size="small"
                                    onClick={onFullscreen}
                                />
                            </Tooltip>
                        )}
                    </div>
                </div>
            )}

            {/* Iframe */}
            <div className={styles.iframeContainer}>
                {isIframeLoading && (
                    <div className={styles.iframeLoading}>
                        <Spinner size="medium" label="Rendering document..." />
                    </div>
                )}
                {previewUrl && (
                    <iframe
                        className={styles.iframe}
                        style={{ visibility: isIframeLoading ? "hidden" : "visible" }}
                        src={previewUrl}
                        title="Document Preview"
                        sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
                        onLoad={handleIframeLoad}
                        onError={handleIframeError}
                    />
                )}
            </div>
        </div>
    );
};

export default SourceDocumentViewer;
