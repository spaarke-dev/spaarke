/**
 * RelationshipViewerModal - Full-screen modal for document relationship visualization
 *
 * Provides a full-screen modal container with:
 * - Header with title and close button
 * - Left sidebar with ControlPanel for filtering
 * - Main canvas area with DocumentGraph
 * - NodeActionBar overlay when a node is selected
 *
 * Follows:
 * - ADR-021: Fluent UI v9 exclusively, design tokens for all colors
 * - ADR-022: React 16 compatible APIs
 * - FR-04: Full-screen modal experience
 * - Dialog patterns from .claude/patterns/pcf/dialog-patterns.md
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Button,
    Text,
    Divider,
} from "@fluentui/react-components";
import {
    Dismiss24Regular,
    DocumentFlowchart24Regular,
} from "@fluentui/react-icons";
import { DocumentGraph } from "./DocumentGraph";
import { ControlPanel, FilterSettings, DEFAULT_FILTER_SETTINGS } from "./ControlPanel";
import { NodeActionBar } from "./NodeActionBar";
import type { DocumentNode, DocumentEdge } from "../types/graph";

/**
 * Props for RelationshipViewerModal component
 */
export interface RelationshipViewerModalProps {
    /** Whether the modal is open */
    isOpen: boolean;
    /** Callback to close the modal */
    onClose: () => void;
    /** Source document ID */
    sourceDocumentId: string;
    /** Source document name (for header display) */
    sourceDocumentName?: string;
    /** Graph nodes data */
    nodes: DocumentNode[];
    /** Graph edges data */
    edges: DocumentEdge[];
    /** Whether dark mode is enabled */
    isDarkMode?: boolean;
    /** Callback when user clicks Expand on a node */
    onExpand?: (documentId: string) => void;
    /** Callback when filter settings change */
    onFilterChange?: (settings: FilterSettings) => void;
}

/**
 * Styles using Fluent UI v9 design tokens (ADR-021 compliant)
 * z-index 10000+ for Dataverse compatibility
 */
const useStyles = makeStyles({
    // Modal overlay - covers entire screen
    overlay: {
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: "rgba(0, 0, 0, 0.5)",
        zIndex: 10000, // High z-index for Dataverse forms
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
    },
    // Modal container - full-screen with padding
    modalContainer: {
        position: "absolute",
        top: "16px",
        left: "16px",
        right: "16px",
        bottom: "16px",
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusLarge,
        boxShadow: tokens.shadow64,
        display: "flex",
        flexDirection: "column",
        overflow: "hidden",
    },
    // Header with title and close button
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
        padding: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalL,
        paddingRight: tokens.spacingHorizontalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    headerIcon: {
        color: tokens.colorBrandForeground1,
    },
    headerTitle: {
        flex: 1,
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXXS,
    },
    headerTitleText: {
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
    },
    headerSubtitle: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    closeButton: {
        minWidth: "auto",
    },
    // Main content area with sidebar and canvas
    content: {
        flex: 1,
        display: "flex",
        overflow: "hidden",
        position: "relative",
    },
    // Left sidebar with control panel
    sidebar: {
        width: "300px",
        minWidth: "280px",
        maxWidth: "320px",
        borderRight: `1px solid ${tokens.colorNeutralStroke1}`,
        overflowY: "auto",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    sidebarContent: {
        padding: tokens.spacingVerticalS,
    },
    // Main canvas area
    canvas: {
        flex: 1,
        position: "relative",
        display: "flex",
        flexDirection: "column",
        backgroundColor: tokens.colorNeutralBackground1,
    },
    graphContainer: {
        flex: 1,
        position: "relative",
        minHeight: 0, // Important for flex sizing
    },
    // Footer with version
    footer: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2,
    },
    footerStats: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    footerVersion: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground4,
    },
});

// Control version - must match ControlManifest.Input.xml
const CONTROL_VERSION = "1.0.16";

/**
 * RelationshipViewerModal - Full-screen modal for document relationship visualization
 */
export const RelationshipViewerModal: React.FC<RelationshipViewerModalProps> = ({
    isOpen,
    onClose,
    sourceDocumentId,
    sourceDocumentName,
    nodes,
    edges,
    isDarkMode = false,
    onExpand,
    onFilterChange,
}) => {
    const styles = useStyles();

    // Filter settings state
    const [filterSettings, setFilterSettings] = React.useState<FilterSettings>(
        DEFAULT_FILTER_SETTINGS
    );

    // Selected node state
    const [selectedNode, setSelectedNode] = React.useState<DocumentNode | null>(null);

    // Container ref for dimensions
    const graphContainerRef = React.useRef<HTMLDivElement>(null);
    const [dimensions, setDimensions] = React.useState({ width: 800, height: 600 });

    // Update dimensions on resize and when modal opens
    React.useEffect(() => {
        if (!isOpen) return;

        const updateDimensions = () => {
            if (graphContainerRef.current) {
                setDimensions({
                    width: graphContainerRef.current.clientWidth,
                    height: graphContainerRef.current.clientHeight,
                });
            }
        };

        // Initial update after a small delay to ensure DOM is ready
        const timeoutId = setTimeout(updateDimensions, 100);

        window.addEventListener("resize", updateDimensions);
        return () => {
            clearTimeout(timeoutId);
            window.removeEventListener("resize", updateDimensions);
        };
    }, [isOpen]);

    // Handle Escape key to close modal
    React.useEffect(() => {
        if (!isOpen) return;

        const handleKeyDown = (event: KeyboardEvent) => {
            if (event.key === "Escape") {
                onClose();
            }
        };

        document.addEventListener("keydown", handleKeyDown);
        return () => document.removeEventListener("keydown", handleKeyDown);
    }, [isOpen, onClose]);

    // Prevent body scroll when modal is open
    React.useEffect(() => {
        if (isOpen) {
            const originalOverflow = document.body.style.overflow;
            document.body.style.overflow = "hidden";
            return () => {
                document.body.style.overflow = originalOverflow;
            };
        }
    }, [isOpen]);

    // Handle filter settings change
    const handleFilterChange = React.useCallback(
        (newSettings: FilterSettings) => {
            setFilterSettings(newSettings);
            if (onFilterChange) {
                onFilterChange(newSettings);
            }
        },
        [onFilterChange]
    );

    // Handle node selection
    const handleNodeSelect = React.useCallback((node: DocumentNode) => {
        setSelectedNode(node);
    }, []);

    // Handle action bar close
    const handleActionBarClose = React.useCallback(() => {
        setSelectedNode(null);
    }, []);

    // Handle expand action
    const handleExpand = React.useCallback(
        (documentId: string) => {
            if (onExpand) {
                onExpand(documentId);
            }
        },
        [onExpand]
    );

    // Close modal when clicking overlay (outside modal)
    const handleOverlayClick = React.useCallback(
        (event: React.MouseEvent<HTMLDivElement>) => {
            if (event.target === event.currentTarget) {
                onClose();
            }
        },
        [onClose]
    );

    // Don't render if modal is not open
    if (!isOpen) {
        return null;
    }

    // Count visible nodes (for stats display)
    const visibleNodesCount = nodes.length;
    const visibleEdgesCount = edges.length;

    return (
        <div
            className={styles.overlay}
            onClick={handleOverlayClick}
            role="dialog"
            aria-modal="true"
            aria-labelledby="modal-title"
        >
            <div className={styles.modalContainer}>
                {/* Header */}
                <div className={styles.header}>
                    <DocumentFlowchart24Regular className={styles.headerIcon} />
                    <div className={styles.headerTitle}>
                        <Text id="modal-title" className={styles.headerTitleText} size={400}>
                            Document Relationships
                        </Text>
                        {sourceDocumentName && (
                            <Text className={styles.headerSubtitle}>
                                Source: {sourceDocumentName}
                            </Text>
                        )}
                    </div>
                    <Button
                        className={styles.closeButton}
                        appearance="subtle"
                        icon={<Dismiss24Regular />}
                        onClick={onClose}
                        aria-label="Close modal"
                        title="Close (Esc)"
                    />
                </div>

                {/* Main Content */}
                <div className={styles.content}>
                    {/* Left Sidebar - Control Panel */}
                    <div className={styles.sidebar}>
                        <div className={styles.sidebarContent}>
                            <ControlPanel
                                settings={filterSettings}
                                onSettingsChange={handleFilterChange}
                            />
                        </div>
                    </div>

                    {/* Main Canvas Area */}
                    <div className={styles.canvas}>
                        <div
                            ref={graphContainerRef}
                            className={styles.graphContainer}
                        >
                            <DocumentGraph
                                nodes={nodes}
                                edges={edges}
                                isDarkMode={isDarkMode}
                                onNodeSelect={handleNodeSelect}
                                width={dimensions.width}
                                height={dimensions.height}
                            />

                            {/* Node Action Bar - appears when node is selected */}
                            {selectedNode && (
                                <NodeActionBar
                                    nodeData={selectedNode.data}
                                    onClose={handleActionBarClose}
                                    onExpand={handleExpand}
                                    canExpand={!selectedNode.data.isSource}
                                />
                            )}
                        </div>
                    </div>
                </div>

                {/* Footer */}
                <div className={styles.footer}>
                    <Text className={styles.footerStats}>
                        {visibleNodesCount} documents &middot; {visibleEdgesCount} relationships
                    </Text>
                    <Text className={styles.footerVersion}>
                        v{CONTROL_VERSION}
                    </Text>
                </div>
            </div>
        </div>
    );
};

export default RelationshipViewerModal;
