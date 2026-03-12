/**
 * DocumentGraph — @xyflow/react v12 canvas with synchronous d3-force layout
 *
 * Migrated from react-flow-renderer v10 PCF to @xyflow/react v12 Code Page.
 * Uses shared `useForceSimulation` hook for synchronous layout computation —
 * positions are fully resolved before first render (no spinner).
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
import { makeStyles, tokens, Text } from "@fluentui/react-components";
import { useForceSimulation, type ForceNode, type ForceEdge } from "@spaarke/ui-components";
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

    // Convert @xyflow/react nodes to ForceNode inputs
    const forceNodes = useMemo<ForceNode[]>(
        () => inputNodes.map((n) => ({
            id: n.id,
            label: n.data.name,
            isSource: n.data.isSource,
        })),
        [inputNodes]
    );

    // Convert @xyflow/react edges to ForceEdge inputs (similarity → weight)
    const forceEdges = useMemo<ForceEdge[]>(
        () => inputEdges.map((e) => ({
            source: e.source,
            target: e.target,
            weight: e.data?.similarity ?? 0.5,
        })),
        [inputEdges]
    );

    // Run synchronous force simulation — positions resolved before first render
    const layoutResult = useForceSimulation(forceNodes, forceEdges, {
        mode: "hub-spoke",
        chargeStrength: layoutOptions?.chargeStrength ?? -1000,
        linkDistanceMultiplier: layoutOptions?.distanceMultiplier ?? 400,
        collisionRadius: layoutOptions?.collisionRadius ?? 100,
        center: { x: layoutOptions?.centerX ?? 0, y: layoutOptions?.centerY ?? 0 },
    });

    // Map positioned output back to @xyflow/react Node format
    const layoutNodes = useMemo<DocumentNode[]>(() => {
        const posMap = new Map(layoutResult.nodes.map((pn) => [pn.id, pn]));
        return inputNodes.map((node) => {
            const pos = posMap.get(node.id);
            return {
                ...node,
                position: { x: pos?.x ?? 0, y: pos?.y ?? 0 },
                data: { ...node.data, compactMode },
            };
        });
    }, [inputNodes, layoutResult.nodes, compactMode]);

    const [nodes, setNodes, onNodesChange] = useNodesState(layoutNodes);
    const [edges, setEdges, onEdgesChange] = useEdgesState(inputEdges);

    useEffect(() => { setNodes(layoutNodes); }, [layoutNodes, setNodes]);
    useEffect(() => { setEdges(inputEdges); }, [inputEdges, setEdges]);

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
