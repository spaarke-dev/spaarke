/**
 * DocumentPreviewDialog — Positioned preview panel for document search results
 *
 * Opens when a user clicks a document data point in Map, Treemap, or Timeline views.
 * Appears to the right of the clicked node (or centered if no anchor position).
 * Panel dimensions match PCF SemanticSearchControl preview: 880px × 85vh.
 *
 * Shows an embedded SharePoint file preview with a toolbar of actions:
 *   - Open File: opens the document in a new browser tab
 *   - Open Record: opens the Dataverse entity form in a dialog
 *   - Find Similar: triggers a similarity search (callback to parent)
 *
 * Preview URL is fetched from BFF API: GET /api/documents/{documentId}/preview-url
 *
 * @see ADR-021 for Fluent UI v9 design system requirements
 * @see FileAccessEndpoints.cs — preview-url endpoint
 */

import { useState, useEffect, useCallback, useMemo } from "react";
import {
    makeStyles,
    tokens,
    Button,
    Spinner,
    Text,
    Toolbar,
    ToolbarButton,
    ToolbarDivider,
} from "@fluentui/react-components";
import {
    Dismiss24Regular,
    Open20Regular,
    DocumentRegular,
    SearchRegular,
} from "@fluentui/react-icons";
import type { DocumentSearchResult } from "../types";
import { BFF_API_BASE_URL, buildAuthHeaders } from "../services/apiBase";
import { openEntityRecord } from "./EntityRecordDialog";

// =============================================
// Props
// =============================================

export interface DocumentPreviewDialogProps {
    /** Whether the panel is open. */
    open: boolean;
    /** The document result to preview (null when closed). */
    result: DocumentSearchResult | null;
    /** Screen coordinates of the click that opened the panel (for positioning). */
    anchorPosition?: { x: number; y: number } | null;
    /** Called when the panel should close. */
    onClose: () => void;
    /** Called when "Find Similar" is clicked. */
    onFindSimilar?: (documentId: string) => void;
}

// =============================================
// Panel dimensions — match PCF SemanticSearchControl preview
// =============================================

const PANEL_WIDTH = 880;
const PANEL_HEIGHT_VH = 85;
const ANCHOR_OFFSET = 16;
const VIEWPORT_MARGIN = 8;

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    backdrop: {
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.3)",
        zIndex: 1000,
    },
    panel: {
        position: "fixed",
        width: `${PANEL_WIDTH}px`,
        height: `${PANEL_HEIGHT_VH}vh`,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusXLarge,
        boxShadow: tokens.shadow64,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
        zIndex: 1001,
    },
    titleBar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalS,
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        flexShrink: 0,
    },
    titleText: {
        overflow: "hidden",
        textOverflow: "ellipsis",
        whiteSpace: "nowrap",
        flex: 1,
        marginRight: tokens.spacingHorizontalS,
    },
    toolbar: {
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
        flexShrink: 0,
    },
    previewContainer: {
        flex: 1,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        overflow: "hidden",
    },
    iframe: {
        width: "100%",
        height: "100%",
        border: "none",
    },
    errorContainer: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        gap: tokens.spacingVerticalM,
    },
});

// =============================================
// Preview URL fetcher
// =============================================

interface PreviewUrlResponse {
    previewUrl: string;
    documentInfo: {
        name: string;
        mimeType?: string;
        fileExtension?: string;
    };
}

async function fetchPreviewUrl(documentId: string): Promise<PreviewUrlResponse> {
    const headers = await buildAuthHeaders();
    const response = await fetch(
        `${BFF_API_BASE_URL}/api/documents/${documentId}/preview-url`,
        { headers },
    );

    if (!response.ok) {
        throw new Error(`Failed to load preview: ${response.status}`);
    }

    return response.json() as Promise<PreviewUrlResponse>;
}

// =============================================
// Positioning helper
// =============================================

function computePanelPosition(
    anchorPosition: { x: number; y: number } | null | undefined,
): { left: number; top: number } {
    const vpWidth = window.innerWidth;
    const vpHeight = window.innerHeight;
    const panelH = vpHeight * (PANEL_HEIGHT_VH / 100);

    // No anchor — center on screen
    if (!anchorPosition) {
        return {
            left: Math.max(0, (vpWidth - PANEL_WIDTH) / 2),
            top: Math.max(0, (vpHeight - panelH) / 2),
        };
    }

    const { x, y } = anchorPosition;

    // Try right of click
    let left = x + ANCHOR_OFFSET;
    if (left + PANEL_WIDTH > vpWidth - VIEWPORT_MARGIN) {
        // Flip to left of click
        left = x - ANCHOR_OFFSET - PANEL_WIDTH;
    }
    // Clamp horizontally
    left = Math.max(VIEWPORT_MARGIN, Math.min(left, vpWidth - PANEL_WIDTH - VIEWPORT_MARGIN));

    // Vertically center on click point, clamped to viewport
    let top = y - panelH / 2;
    top = Math.max(VIEWPORT_MARGIN, Math.min(top, vpHeight - panelH - VIEWPORT_MARGIN));

    return { left, top };
}

// =============================================
// Component
// =============================================

export const DocumentPreviewDialog: React.FC<DocumentPreviewDialogProps> = ({
    open,
    result,
    anchorPosition,
    onClose,
    onFindSimilar,
}) => {
    const styles = useStyles();
    const [previewUrl, setPreviewUrl] = useState<string | null>(null);
    const [isLoadingPreview, setIsLoadingPreview] = useState(false);
    const [previewError, setPreviewError] = useState<string | null>(null);

    const documentId = result?.documentId ?? null;
    const documentName = result?.name ?? "Document";

    // --- Panel position ---
    const panelPosition = useMemo(
        () => computePanelPosition(anchorPosition),
        [anchorPosition],
    );

    // --- Fetch preview URL when panel opens ---
    useEffect(() => {
        if (!open || !documentId) {
            setPreviewUrl(null);
            setPreviewError(null);
            return;
        }

        let cancelled = false;
        setIsLoadingPreview(true);
        setPreviewError(null);

        fetchPreviewUrl(documentId)
            .then((data) => {
                if (!cancelled) {
                    setPreviewUrl(data.previewUrl);
                }
            })
            .catch((err) => {
                if (!cancelled) {
                    setPreviewError(
                        err instanceof Error ? err.message : "Failed to load preview",
                    );
                }
            })
            .finally(() => {
                if (!cancelled) {
                    setIsLoadingPreview(false);
                }
            });

        return () => {
            cancelled = true;
        };
    }, [open, documentId]);

    // --- Escape key to close ---
    useEffect(() => {
        if (!open) return;
        const handleKeyDown = (e: KeyboardEvent) => {
            if (e.key === "Escape") onClose();
        };
        document.addEventListener("keydown", handleKeyDown);
        return () => document.removeEventListener("keydown", handleKeyDown);
    }, [open, onClose]);

    // --- Action handlers ---

    const handleOpenFile = useCallback(() => {
        if (documentId) {
            if (result?.fileUrl) {
                window.open(result.fileUrl, "_blank");
            } else if (previewUrl) {
                window.open(previewUrl, "_blank");
            }
        }
    }, [documentId, result?.fileUrl, previewUrl]);

    const handleOpenRecord = useCallback(() => {
        if (documentId) {
            openEntityRecord(documentId, "documents");
        }
    }, [documentId]);

    const handleFindSimilar = useCallback(() => {
        if (documentId && onFindSimilar) {
            onFindSimilar(documentId);
        }
    }, [documentId, onFindSimilar]);

    if (!open) return null;

    return (
        <>
            {/* Backdrop — click to close */}
            <div className={styles.backdrop} onClick={onClose} />

            {/* Preview panel */}
            <div
                className={styles.panel}
                style={{ left: panelPosition.left, top: panelPosition.top }}
                role="dialog"
                aria-label={`Preview: ${documentName}`}
            >
                {/* Title bar */}
                <div className={styles.titleBar}>
                    <Text weight="semibold" size={400} className={styles.titleText}>
                        {documentName}
                    </Text>
                    <Button
                        appearance="subtle"
                        icon={<Dismiss24Regular />}
                        onClick={onClose}
                        aria-label="Close"
                    />
                </div>

                {/* Toolbar: Open File | Open Record | Find Similar */}
                <Toolbar className={styles.toolbar} size="small">
                    <ToolbarButton
                        icon={<Open20Regular />}
                        onClick={handleOpenFile}
                        disabled={!documentId}
                    >
                        Open File
                    </ToolbarButton>
                    <ToolbarButton
                        icon={<DocumentRegular />}
                        onClick={handleOpenRecord}
                        disabled={!documentId}
                    >
                        Open Record
                    </ToolbarButton>
                    <ToolbarDivider />
                    <ToolbarButton
                        icon={<SearchRegular />}
                        onClick={handleFindSimilar}
                        disabled={!documentId || !onFindSimilar}
                    >
                        Find Similar
                    </ToolbarButton>
                </Toolbar>

                {/* Preview area */}
                <div className={styles.previewContainer}>
                    {isLoadingPreview && <Spinner label="Loading preview..." />}

                    {previewError && (
                        <div className={styles.errorContainer}>
                            <Text weight="semibold">Preview unavailable</Text>
                            <Text size={200}>{previewError}</Text>
                        </div>
                    )}

                    {previewUrl && !isLoadingPreview && !previewError && (
                        <iframe
                            className={styles.iframe}
                            src={previewUrl}
                            title={`Preview: ${documentName}`}
                            sandbox="allow-scripts allow-same-origin allow-forms allow-popups"
                        />
                    )}
                </div>
            </div>
        </>
    );
};

export default DocumentPreviewDialog;
