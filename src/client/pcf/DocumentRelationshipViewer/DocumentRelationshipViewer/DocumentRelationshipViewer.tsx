import * as React from "react";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    makeStyles,
    tokens,
    Text,
    Spinner,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import { DocumentFlowchart24Regular } from "@fluentui/react-icons";
import { IInputs } from "./generated/ManifestTypes";
import { DocumentGraph } from "./components/DocumentGraph";
import type { DocumentNode, DocumentEdge } from "./types/graph";

// Control version - must match ControlManifest.Input.xml
const CONTROL_VERSION = "1.0.0";

/**
 * Props for the DocumentRelationshipViewer component
 */
export interface IDocumentRelationshipViewerProps {
    /** PCF context for accessing parameters and platform features */
    context: ComponentFramework.Context<IInputs>;
    /** Callback to notify PCF framework of output changes */
    notifyOutputChanged: () => void;
    /** Callback when user selects a document node */
    onDocumentSelect?: (documentId: string) => void;
}

/**
 * Styles using Fluent UI design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        height: "100%",
        minHeight: "400px",
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        padding: tokens.spacingVerticalM,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    },
    graphContainer: {
        flex: 1,
        display: "flex",
        position: "relative",
        minHeight: "300px",
    },
    placeholder: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
        width: "100%",
        height: "100%",
    },
    footer: {
        padding: tokens.spacingVerticalS,
        paddingRight: tokens.spacingHorizontalM,
        textAlign: "right",
        borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    },
    versionText: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground4,
    },
    errorContainer: {
        padding: tokens.spacingVerticalL,
        width: "100%",
    },
});

/**
 * Resolve theme based on context and system preferences (ADR-021)
 */
function resolveTheme(context?: ComponentFramework.Context<IInputs>) {
    // Check PCF context for dark mode
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme ? webDarkTheme : webLightTheme;
    }

    // Fallback to system preference
    if (typeof window !== "undefined") {
        return window.matchMedia("(prefers-color-scheme: dark)").matches
            ? webDarkTheme
            : webLightTheme;
    }

    return webLightTheme;
}

/**
 * Check if dark mode is enabled
 */
function isDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    if (typeof window !== "undefined") {
        return window.matchMedia("(prefers-color-scheme: dark)").matches;
    }

    return false;
}

/**
 * Generate sample nodes and edges for testing the graph visualization.
 * This will be replaced with API data in production.
 */
function generateSampleData(sourceDocumentId: string): { nodes: DocumentNode[]; edges: DocumentEdge[] } {
    // Source node (center)
    const sourceNode: DocumentNode = {
        id: sourceDocumentId,
        type: "document",
        position: { x: 0, y: 0 },
        data: {
            documentId: sourceDocumentId,
            name: "Source Document.pdf",
            fileType: "pdf",
            size: 1024000,
            isSource: true,
        },
    };

    // Related documents with varying similarity
    const relatedDocs = [
        { id: "doc-1", name: "Related Contract.docx", fileType: "docx", similarity: 0.92 },
        { id: "doc-2", name: "Previous Agreement.pdf", fileType: "pdf", similarity: 0.85 },
        { id: "doc-3", name: "Meeting Notes.docx", fileType: "docx", similarity: 0.78 },
        { id: "doc-4", name: "Project Proposal.pptx", fileType: "pptx", similarity: 0.72 },
        { id: "doc-5", name: "Budget Report.xlsx", fileType: "xlsx", similarity: 0.65 },
        { id: "doc-6", name: "Legal Review.pdf", fileType: "pdf", similarity: 0.58 },
        { id: "doc-7", name: "Status Update.docx", fileType: "docx", similarity: 0.52 },
    ];

    const nodes: DocumentNode[] = [
        sourceNode,
        ...relatedDocs.map((doc) => ({
            id: doc.id,
            type: "document" as const,
            position: { x: 0, y: 0 }, // Will be calculated by force layout
            data: {
                documentId: doc.id,
                name: doc.name,
                fileType: doc.fileType,
                similarity: doc.similarity,
                isSource: false,
                parentEntityName: "Matter ABC-123",
            },
        })),
    ];

    // Create edges from source to all related documents
    const edges: DocumentEdge[] = relatedDocs.map((doc) => ({
        id: `edge-${sourceDocumentId}-${doc.id}`,
        source: sourceDocumentId,
        target: doc.id,
        data: {
            similarity: doc.similarity,
            sharedKeywords: ["contract", "agreement"],
        },
    }));

    return { nodes, edges };
}

/**
 * DocumentRelationshipViewer - Interactive graph visualization of related documents.
 *
 * This control displays a force-directed graph showing document relationships
 * based on vector similarity from Azure AI Search.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 APIs with platform libraries
 */
export const DocumentRelationshipViewer: React.FC<IDocumentRelationshipViewerProps> = ({
    context,
    notifyOutputChanged,
    onDocumentSelect,
}) => {
    const styles = useStyles();
    const theme = resolveTheme(context);
    const darkMode = isDarkMode(context);

    // Get input parameters
    const documentId = context.parameters.documentId?.raw ?? "";
    const apiBaseUrl = context.parameters.apiBaseUrl?.raw ?? "https://spe-api-dev-67e2xz.azurewebsites.net";

    // State for loading and error
    const [isLoading, setIsLoading] = React.useState(false);
    const [error, setError] = React.useState<string | null>(null);
    const [selectedNodeId, setSelectedNodeId] = React.useState<string | null>(null);

    // Graph data state
    const [graphData, setGraphData] = React.useState<{ nodes: DocumentNode[]; edges: DocumentEdge[] }>({
        nodes: [],
        edges: [],
    });

    // Get container dimensions for layout
    const containerRef = React.useRef<HTMLDivElement>(null);
    const [dimensions, setDimensions] = React.useState({ width: 800, height: 600 });

    // Update dimensions on resize
    React.useEffect(() => {
        const updateDimensions = () => {
            if (containerRef.current) {
                setDimensions({
                    width: containerRef.current.clientWidth,
                    height: containerRef.current.clientHeight,
                });
            }
        };

        updateDimensions();
        window.addEventListener("resize", updateDimensions);
        return () => window.removeEventListener("resize", updateDimensions);
    }, []);

    // Load graph data when documentId changes
    React.useEffect(() => {
        if (!documentId || documentId.trim() === "") {
            setGraphData({ nodes: [], edges: [] });
            return;
        }

        // For now, use sample data
        // TODO: Replace with API call in production
        const sampleData = generateSampleData(documentId);
        setGraphData(sampleData);
    }, [documentId, apiBaseUrl]);

    // Handle node selection
    const handleNodeSelect = React.useCallback(
        (node: DocumentNode) => {
            setSelectedNodeId(node.id);
            if (onDocumentSelect) {
                onDocumentSelect(node.data.documentId);
            }
            notifyOutputChanged();
        },
        [onDocumentSelect, notifyOutputChanged]
    );

    // Check if we should show the placeholder
    const showPlaceholder = !documentId || documentId.trim() === "";

    return (
        <FluentProvider theme={theme}>
            <div className={styles.container}>
                {/* Header */}
                <div className={styles.header}>
                    <DocumentFlowchart24Regular />
                    <Text weight="semibold" size={400}>
                        Document Relationships
                    </Text>
                    {selectedNodeId && (
                        <Text size={200} style={{ marginLeft: "auto", color: tokens.colorNeutralForeground3 }}>
                            Selected: {selectedNodeId}
                        </Text>
                    )}
                </div>

                {/* Graph Container */}
                <div className={styles.graphContainer} ref={containerRef}>
                    {isLoading ? (
                        <div className={styles.placeholder}>
                            <Spinner size="large" label="Loading relationships..." />
                        </div>
                    ) : error ? (
                        <div className={styles.errorContainer}>
                            <MessageBar intent="error">
                                <MessageBarBody>{error}</MessageBarBody>
                            </MessageBar>
                        </div>
                    ) : showPlaceholder ? (
                        <div className={styles.placeholder}>
                            <DocumentFlowchart24Regular style={{ fontSize: 48 }} />
                            <Text>
                                Select a document to view its relationships
                            </Text>
                            <Text size={200}>
                                Graph visualization will appear here
                            </Text>
                        </div>
                    ) : (
                        <DocumentGraph
                            nodes={graphData.nodes}
                            edges={graphData.edges}
                            isDarkMode={darkMode}
                            onNodeSelect={handleNodeSelect}
                            width={dimensions.width}
                            height={dimensions.height}
                        />
                    )}
                </div>

                {/* Footer with version (ADR-021, PCF-V9-PACKAGING.md requirement) */}
                <div className={styles.footer}>
                    <span className={styles.versionText}>
                        v{CONTROL_VERSION}
                    </span>
                </div>
            </div>
        </FluentProvider>
    );
};
