/**
 * DocumentGraph — @xyflow/react v12 canvas with d3-force layout
 *
 * Migrated from react-flow-renderer v10 PCF to @xyflow/react v12 Code Page.
 * Key changes:
 * - ReactFlow is now a named export (not default)
 * - CSS import path updated
 * - NodeTypes/EdgeTypes type parameters updated for v12
 */

import React, { useMemo, useEffect, useCallback } from "react";
import {
    ReactFlow,
    Background,
    Controls,
    MiniMap,
    useNodesState,
    useEdgesState,
    BackgroundVariant,
    type NodeTypes,
    type EdgeTypes,
    type Node,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { makeStyles, tokens, Spinner, Text } from "@fluentui/react-components";
import { useForceLayout } from "../hooks/useForceLayout";
import { DocumentNode as DocumentNodeComponent } from "./DocumentNode";
import { DocumentEdge as DocumentEdgeComponent } from "./DocumentEdge";
import type { DocumentNode, DocumentEdge, DocumentNodeData, ForceLayoutOptions } from "../types/graph";

export interface DocumentGraphProps {
    nodes: DocumentNode[];
    edges: DocumentEdge[];
    isDarkMode?: boolean;
    onNodeSelect?: (node: DocumentNode) => void;
    layoutOptions?: ForceLayoutOptions;
    showMinimap?: boolean;
    compactMode?: boolean;
}

const useStyles = makeStyles({
    container: { width: "100%", height: "100%", minHeight: "300px", position: "relative" },
    loadingOverlay: {
        position: "absolute", top: 0, left: 0, right: 0, bottom: 0,
        display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center",
        backgroundColor: tokens.colorNeutralBackground1, opacity: 0.9, zIndex: 10,
    },
    emptyState: {
        display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center",
        height: "100%", color: tokens.colorNeutralForeground3, gap: tokens.spacingVerticalM,
    },
});

const nodeTypes: NodeTypes = {
    document: DocumentNodeComponent as NodeTypes[string],
};

const edgeTypes: EdgeTypes = {
    similarity: DocumentEdgeComponent as EdgeTypes[string],
};

export const DocumentGraph: React.FC<DocumentGraphProps> = ({
    nodes: inputNodes,
    edges: inputEdges,
    isDarkMode = false,
    onNodeSelect,
    layoutOptions,
    showMinimap = true,
    compactMode = false,
}) => {
    const styles = useStyles();

    const { layoutNodes, layoutEdges, isSimulating } = useForceLayout(
        inputNodes, inputEdges,
        { ...layoutOptions, centerX: 0, centerY: 0 }
    );

    const nodesWithCompactMode = useMemo(
        () => layoutNodes.map((node) => ({ ...node, data: { ...node.data, compactMode } })),
        [layoutNodes, compactMode]
    );

    const [nodes, setNodes, onNodesChange] = useNodesState(nodesWithCompactMode);
    const [edges, setEdges, onEdgesChange] = useEdgesState(layoutEdges);

    useEffect(() => { setNodes(nodesWithCompactMode); }, [nodesWithCompactMode, setNodes]);
    useEffect(() => { setEdges(layoutEdges); }, [layoutEdges, setEdges]);

    const handleNodeClick = useCallback(
        (_event: React.MouseEvent, node: Node) => {
            if (onNodeSelect) onNodeSelect(node as DocumentNode);
        },
        [onNodeSelect]
    );

    if (inputNodes.length === 0) {
        return (
            <div className={styles.container}>
                <div className={styles.emptyState}>
                    <Text size={400}>No document relationships to display</Text>
                    <Text size={200}>Select a document with AI embeddings to view relationships</Text>
                </div>
            </div>
        );
    }

    const typedEdges = edges.map((edge) => ({ ...edge, type: "similarity" }));

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
                fitViewOptions={{ padding: 0.2, maxZoom: 1.5, minZoom: 0.3 }}
                minZoom={0.1}
                maxZoom={2}
                connectOnClick={false}
                attributionPosition="bottom-left"
                style={{ width: "100%", height: "100%" }}
            >
                <Background variant={BackgroundVariant.Dots} gap={20} size={1} color={isDarkMode ? tokens.colorNeutralStroke2 : tokens.colorNeutralStroke3} />
                <Controls showZoom showFitView showInteractive={false} />
                {showMinimap && (
                    <MiniMap
                        nodeColor={(node: Node<DocumentNodeData>) =>
                            node.data?.isSource ? tokens.colorBrandBackground : tokens.colorNeutralBackground3
                        }
                        maskColor={isDarkMode ? tokens.colorNeutralBackgroundAlpha2 : tokens.colorNeutralBackgroundAlpha}
                        style={{ backgroundColor: isDarkMode ? tokens.colorNeutralBackground2 : tokens.colorNeutralBackground1 }}
                    />
                )}
            </ReactFlow>
        </div>
    );
};

export default DocumentGraph;
