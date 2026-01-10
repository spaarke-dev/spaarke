/**
 * DocumentGraph - React Flow canvas with d3-force layout
 *
 * This component renders the document relationship graph using React Flow
 * with node positions calculated by d3-force simulation.
 *
 * Follows:
 * - ADR-006: PCF for all custom UI
 * - ADR-021: Fluent UI v9 with dark mode support
 * - ADR-022: React 16 compatible APIs (using react-flow-renderer v10)
 */

import * as React from "react";
import ReactFlow, {
    Background,
    Controls,
    MiniMap,
    useNodesState,
    useEdgesState,
    BackgroundVariant,
    Node,
    Edge,
    NodeTypes,
    EdgeTypes,
} from "react-flow-renderer";
import "react-flow-renderer/dist/style.css";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
} from "@fluentui/react-components";
import { useForceLayout } from "../hooks/useForceLayout";
import { DocumentNode as DocumentNodeComponent } from "./DocumentNode";
import { DocumentEdge as DocumentEdgeComponent } from "./DocumentEdge";
import type {
    DocumentNode,
    DocumentEdge,
    DocumentNodeData,
    ForceLayoutOptions,
} from "../types/graph";

/**
 * Props for DocumentGraph component
 */
export interface DocumentGraphProps {
    /** Nodes from API response */
    nodes: DocumentNode[];
    /** Edges from API response */
    edges: DocumentEdge[];
    /** Whether dark mode is enabled */
    isDarkMode?: boolean;
    /** Callback when node is selected */
    onNodeSelect?: (node: DocumentNode) => void;
    /** Layout options */
    layoutOptions?: ForceLayoutOptions;
    /** Container width */
    width?: number;
    /** Container height */
    height?: number;
    /** Whether to show minimap (default: false) */
    showMinimap?: boolean;
    /** Compact mode for icon-only display in fieldBound mode */
    compactMode?: boolean;
}

/**
 * Styles using Fluent UI design tokens (ADR-021 compliant)
 */
const useStyles = makeStyles({
    container: {
        width: "100%",
        height: "100%",
        minHeight: "300px",
        position: "relative",
    },
    loadingOverlay: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        backgroundColor: tokens.colorNeutralBackground1,
        opacity: 0.9,
        zIndex: 10,
    },
    emptyState: {
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        height: "100%",
        color: tokens.colorNeutralForeground3,
        gap: tokens.spacingVerticalM,
    },
});

/**
 * Node types registry for React Flow
 * Uses custom DocumentNode component from Task 012
 */
const nodeTypes: NodeTypes = {
    document: DocumentNodeComponent as unknown as NodeTypes["default"],
};

/**
 * Edge types registry for React Flow
 * Uses custom DocumentEdge component from Task 013
 */
const edgeTypes: EdgeTypes = {
    similarity: DocumentEdgeComponent as unknown as EdgeTypes["default"],
};

/**
 * DocumentGraph component - Renders document relationship graph
 */
export const DocumentGraph: React.FC<DocumentGraphProps> = ({
    nodes: inputNodes,
    edges: inputEdges,
    isDarkMode = false,
    onNodeSelect,
    layoutOptions,
    width,
    height,
    showMinimap = false,
    compactMode = false,
}) => {
    const styles = useStyles();

    // Use (0,0) as center - React Flow's fitView will handle viewport positioning
    // Using container center caused issues with large viewport widths
    const centerX = 0;
    const centerY = 0;

    // Use force layout hook to calculate positions
    const { layoutNodes, layoutEdges, isSimulating } = useForceLayout(
        inputNodes,
        inputEdges,
        {
            ...layoutOptions,
            centerX,
            centerY,
        }
    );

    // React Flow state - add compactMode to node data
    const nodesWithCompactMode = React.useMemo(() => {
        return layoutNodes.map((node) => ({
            ...node,
            data: {
                ...node.data,
                compactMode,
            },
        }));
    }, [layoutNodes, compactMode]);

    const [nodes, setNodes, onNodesChange] = useNodesState(nodesWithCompactMode);
    const [edges, setEdges, onEdgesChange] = useEdgesState(layoutEdges);

    // Update nodes when layout changes or compact mode changes
    React.useEffect(() => {
        setNodes(nodesWithCompactMode);
    }, [nodesWithCompactMode, setNodes]);

    // Update edges when layout changes
    React.useEffect(() => {
        setEdges(layoutEdges);
    }, [layoutEdges, setEdges]);

    // Handle node click
    const handleNodeClick = React.useCallback(
        (_event: React.MouseEvent, node: Node) => {
            if (onNodeSelect) {
                onNodeSelect(node as DocumentNode);
            }
        },
        [onNodeSelect]
    );

    // Empty state
    if (inputNodes.length === 0) {
        return (
            <div className={styles.container}>
                <div className={styles.emptyState}>
                    <Text size={400}>No document relationships to display</Text>
                    <Text size={200}>
                        Select a document to view its relationships
                    </Text>
                </div>
            </div>
        );
    }

    // Apply custom edge type for similarity-based styling (Task 013)
    const typedEdges = edges.map((edge) => ({
        ...edge,
        type: "similarity", // Use custom DocumentEdge component
    }));

    return (
        <div className={styles.container}>
            {isSimulating && (
                <div className={styles.loadingOverlay}>
                    <Spinner size="medium" label="Calculating layout..." />
                </div>
            )}

            <ReactFlow
                nodes={nodes}
                edges={typedEdges}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodeClick={handleNodeClick}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                fitView
                fitViewOptions={{
                    padding: 0.2,
                    maxZoom: 1.5,
                    minZoom: 0.3,
                }}
                minZoom={0.1}
                maxZoom={2}
                connectOnClick={false}
                attributionPosition="bottom-left"
                style={{ width: "100%", height: "100%" }}
            >
                <Background
                    variant={BackgroundVariant.Dots}
                    gap={20}
                    size={1}
                    color={isDarkMode ? "#444" : "#ddd"}
                />
                <Controls
                    showZoom
                    showFitView
                    showInteractive={false}
                />
                {showMinimap && (
                    <MiniMap
                        nodeColor={(node: Node<DocumentNodeData>) => {
                            return node.data?.isSource
                                ? tokens.colorBrandBackground
                                : tokens.colorNeutralBackground3;
                        }}
                        maskColor={isDarkMode ? "rgba(0,0,0,0.7)" : "rgba(255,255,255,0.7)"}
                        style={{
                            backgroundColor: isDarkMode
                                ? tokens.colorNeutralBackground2
                                : tokens.colorNeutralBackground1,
                        }}
                    />
                )}
            </ReactFlow>
        </div>
    );
};

export default DocumentGraph;
