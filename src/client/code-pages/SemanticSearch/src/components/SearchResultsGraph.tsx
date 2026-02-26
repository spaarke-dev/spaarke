/**
 * SearchResultsGraph — @xyflow/react v12 canvas for cluster visualization
 *
 * Renders search results as a force-directed graph with cluster nodes and
 * record nodes. Follows the same pattern as DocumentGraph.tsx from the
 * DocumentRelationshipViewer code page.
 *
 * Features:
 *   - ReactFlow canvas with custom nodeTypes (clusterNode, recordNode)
 *   - Background (dots), Controls, MiniMap sub-components
 *   - Empty state when no results
 *   - Truncation warning when results > 100 (graph limited to top 100)
 *   - Loading overlay with Spinner
 *
 * @see DocumentGraph.tsx (DocumentRelationshipViewer) — primary reference
 */

import React, { useEffect, useCallback, useRef } from "react";
import {
    ReactFlow,
    Background,
    Controls,
    MiniMap,
    useNodesState,
    useEdgesState,
    BackgroundVariant,
    type NodeTypes,
    type Node,
    type Edge,
    type ReactFlowInstance,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
    MessageBar,
    MessageBarBody,
} from "@fluentui/react-components";
import { ClusterNode as ClusterNodeComponent } from "./ClusterNode";
import { RecordNode as RecordNodeComponent } from "./RecordNode";
import type { GraphClusterBy } from "../types";

// =============================================
// Props
// =============================================

export interface SearchResultsGraphProps {
    /** Graph nodes (from useClusterLayout hook). */
    nodes: Node[];
    /** Graph edges (from useClusterLayout hook). */
    edges: Edge[];
    /** Callback when a node is clicked. */
    onNodeClick: (nodeId: string, nodeType: "cluster" | "record") => void;
    /** Current clustering category. */
    clusterBy: GraphClusterBy;
    /** Whether graph layout is being calculated. */
    isLoading: boolean;
    /** Total result count (to show warning when > 100). */
    resultCount: number;
    /** Currently expanded cluster ID (null = all collapsed). */
    expandedClusterId: string | null;
}

// =============================================
// Node/edge type registrations — OUTSIDE component to avoid re-registration
// =============================================

const nodeTypes: NodeTypes = {
    clusterNode: ClusterNodeComponent as NodeTypes[string],
    recordNode: RecordNodeComponent as NodeTypes[string],
};

// =============================================
// Styles
// =============================================

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
    warningBar: {
        position: "absolute",
        top: tokens.spacingVerticalS,
        left: tokens.spacingHorizontalM,
        right: tokens.spacingHorizontalM,
        zIndex: 5,
    },
});

// =============================================
// Component
// =============================================

export const SearchResultsGraph: React.FC<SearchResultsGraphProps> = ({
    nodes: inputNodes,
    edges: inputEdges,
    onNodeClick,
    clusterBy,
    isLoading,
    resultCount,
    expandedClusterId,
}) => {
    const styles = useStyles();
    const rfInstanceRef = useRef<ReactFlowInstance | null>(null);

    const [nodes, setNodes, onNodesChange] = useNodesState(inputNodes);
    const [edges, setEdges, onEdgesChange] = useEdgesState(inputEdges);

    // Sync props to internal state when they change
    useEffect(() => {
        setNodes(inputNodes);
    }, [inputNodes, setNodes]);

    useEffect(() => {
        setEdges(inputEdges);
    }, [inputEdges, setEdges]);

    // fitView after expand/collapse changes
    useEffect(() => {
        const timer = setTimeout(() => {
            rfInstanceRef.current?.fitView({
                padding: 0.2,
                duration: 300,
            });
        }, 50);
        return () => clearTimeout(timer);
    }, [expandedClusterId]);

    // Store ReactFlow instance on init
    const handleInit = useCallback((instance: ReactFlowInstance) => {
        rfInstanceRef.current = instance;
    }, []);

    // Node click handler — determine type from node.type field
    const handleNodeClick = useCallback(
        (_event: React.MouseEvent, node: Node) => {
            const nodeType: "cluster" | "record" =
                node.type === "recordNode" ? "record" : "cluster";
            onNodeClick(node.id, nodeType);
        },
        [onNodeClick]
    );

    // Empty state
    if (inputNodes.length === 0 && !isLoading) {
        return (
            <div className={styles.container}>
                <div className={styles.emptyState}>
                    <Text size={400} weight="semibold">
                        No results to visualize
                    </Text>
                    <Text size={200}>
                        Run a search to see results clustered by {clusterBy}
                    </Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container}>
            {/* Loading overlay */}
            {isLoading && (
                <div className={styles.loadingOverlay}>
                    <Spinner size="medium" label="Calculating layout..." />
                </div>
            )}

            {/* Truncation warning when results exceed graph limit */}
            {resultCount > 100 && (
                <div className={styles.warningBar}>
                    <MessageBar intent="warning">
                        <MessageBarBody>
                            Showing top 100 of {resultCount} results in graph view.
                            Switch to grid view to see all results.
                        </MessageBarBody>
                    </MessageBar>
                </div>
            )}

            <ReactFlow
                nodes={nodes}
                edges={edges}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onNodeClick={handleNodeClick}
                onInit={handleInit}
                nodeTypes={nodeTypes}
                fitView
                fitViewOptions={{ padding: 0.2, maxZoom: 1.5, minZoom: 0.3 }}
                minZoom={0.1}
                maxZoom={2}
                connectOnClick={false}
                attributionPosition="bottom-left"
                style={{ width: "100%", height: "100%" }}
                aria-label="Search results graph"
            >
                <Background
                    variant={BackgroundVariant.Dots}
                    gap={20}
                    size={1}
                />
                <Controls showZoom showFitView showInteractive={false} />
                <MiniMap
                    style={{
                        backgroundColor: tokens.colorNeutralBackground1,
                    }}
                />
            </ReactFlow>
        </div>
    );
};

export default SearchResultsGraph;
