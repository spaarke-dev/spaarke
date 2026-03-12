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
    ReactFlowProvider,
    Background,
    Controls,
    MiniMap,
    useNodesState,
    useEdgesState,
    useReactFlow,
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
    container: { position: "absolute", top: 0, left: 0, right: 0, bottom: 0 },
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

/** Auto-fit graph to canvas when node count changes */
const AutoFitOnChange: React.FC<{ nodeCount: number }> = ({ nodeCount }) => {
    const { fitView } = useReactFlow();
    useEffect(() => {
        // Small delay to let nodes render before fitting
        const timer = setTimeout(() => fitView({ padding: 0.2, maxZoom: 1.5, duration: 200 }), 100);
        return () => clearTimeout(timer);
    }, [nodeCount, fitView]);
    return null;
};

const DocumentGraphInner: React.FC<DocumentGraphProps> = ({
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
    // Source node defaults to left side of canvas (negative x offset)
    const layoutResult = useForceSimulation(forceNodes, forceEdges, {
        mode: "hub-spoke",
        chargeStrength: layoutOptions?.chargeStrength ?? -1000,
        linkDistanceMultiplier: layoutOptions?.distanceMultiplier ?? 400,
        collisionRadius: layoutOptions?.collisionRadius ?? 100,
        center: { x: layoutOptions?.centerX ?? -200, y: layoutOptions?.centerY ?? 0 },
    });

    // Map positioned output back to @xyflow/react Node format
    // Post-process: shift so source node is at left, children spread right
    const layoutNodes = useMemo<DocumentNode[]>(() => {
        const posMap = new Map(layoutResult.nodes.map((pn) => [pn.id, pn]));

        // Find the source node position
        const sourceNode = inputNodes.find((n) => n.data.isSource);
        const sourcePos = sourceNode ? posMap.get(sourceNode.id) : undefined;
        const sourceX = sourcePos?.x ?? 0;

        // Find the average x of non-source nodes
        const nonSourcePositions = inputNodes
            .filter((n) => !n.data.isSource)
            .map((n) => posMap.get(n.id)?.x ?? 0);
        const avgNonSourceX = nonSourcePositions.length > 0
            ? nonSourcePositions.reduce((a, b) => a + b, 0) / nonSourcePositions.length
            : sourceX + 200;

        // If source is not already to the left of children, flip x positions around the midpoint
        const midX = (sourceX + avgNonSourceX) / 2;
        const needsFlip = sourceX > avgNonSourceX;

        return inputNodes.map((node) => {
            const pos = posMap.get(node.id);
            let x = pos?.x ?? 0;
            if (needsFlip) {
                x = midX - (x - midX); // mirror around midpoint
            }
            return {
                ...node,
                position: { x, y: pos?.y ?? 0 },
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
                <AutoFitOnChange nodeCount={inputNodes.length} />
            </ReactFlow>
        </div>
    );
};

/** Wrap in ReactFlowProvider so useReactFlow works inside AutoFitOnChange */
export const DocumentGraph: React.FC<DocumentGraphProps> = (props) => (
    <ReactFlowProvider>
        <DocumentGraphInner {...props} />
    </ReactFlowProvider>
);

export default DocumentGraph;
