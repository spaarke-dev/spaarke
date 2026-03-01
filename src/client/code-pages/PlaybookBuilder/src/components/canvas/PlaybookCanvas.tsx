/**
 * PlaybookCanvas — @xyflow/react v12 canvas for the Playbook Builder
 *
 * Migrated from react-flow-renderer v10 (PCF R4) to @xyflow/react v12 (Code Page R5).
 *
 * Key v12 migration changes:
 * - ReactFlow is a named export (not default)
 * - Import '@xyflow/react/dist/style.css' instead of 'react-flow-renderer/dist/style.css'
 * - useReactFlow() hook for flow instance (replaces ref-based ReactFlowInstance)
 * - screenToFlowPosition() replaces project()
 * - Typed generics: Node<PlaybookNodeData>, Edge<ConditionEdgeData>
 * - MiniMap, Background, Controls from '@xyflow/react'
 *
 * ADR-021: All colors use Fluent design tokens (dark mode support).
 */

import React, { useCallback, useRef, type DragEvent } from "react";
import {
    ReactFlow,
    Background,
    Controls,
    MiniMap,
    useReactFlow,
    BackgroundVariant,
    type Node,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { makeStyles, tokens } from "@fluentui/react-components";
import { useCanvasStore } from "../../stores/canvasStore";
import { nodeTypes } from "../nodes";
import { edgeTypes } from "../edges";
import type { PlaybookNodeType, PlaybookNodeData } from "../../types/canvas";

const useStyles = makeStyles({
    container: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: tokens.colorNeutralBackground1,
    },
});

/**
 * Inner canvas component that uses the useReactFlow hook.
 * Must be rendered inside a ReactFlowProvider.
 */
export const PlaybookCanvasInner = React.memo(function PlaybookCanvasInner() {
    const styles = useStyles();
    const reactFlowWrapper = useRef<HTMLDivElement>(null);

    // v12: useReactFlow() hook provides screenToFlowPosition, fitView, etc.
    const { screenToFlowPosition } = useReactFlow();

    // Get state and actions from store
    const nodes = useCanvasStore((s) => s.nodes);
    const edges = useCanvasStore((s) => s.edges);
    const onNodesChange = useCanvasStore((s) => s.onNodesChange);
    const onEdgesChange = useCanvasStore((s) => s.onEdgesChange);
    const onConnect = useCanvasStore((s) => s.onConnect);
    const selectNode = useCanvasStore((s) => s.selectNode);
    const onDrop = useCanvasStore((s) => s.onDrop);

    // Handle node selection
    const onNodeClick = useCallback(
        (_event: React.MouseEvent, node: Node) => {
            selectNode(node.id);
        },
        [selectNode]
    );

    // Handle click on canvas background (deselect)
    const onPaneClick = useCallback(() => {
        selectNode(null);
    }, [selectNode]);

    // Handle drag over for drop target
    const onDragOver = useCallback((event: DragEvent<HTMLDivElement>) => {
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
    }, []);

    // Handle drop from node palette
    const handleDrop = useCallback(
        (event: DragEvent<HTMLDivElement>) => {
            event.preventDefault();

            // Get node data from drag event
            const nodeTypeData = event.dataTransfer.getData(
                "application/reactflow"
            );
            if (!nodeTypeData) return;

            try {
                const { type, label } = JSON.parse(nodeTypeData) as {
                    type: PlaybookNodeType;
                    label: string;
                };

                // v12: screenToFlowPosition() replaces the v10 project() method
                const position = screenToFlowPosition({
                    x: event.clientX,
                    y: event.clientY,
                });

                onDrop(position, type, label);
            } catch (e) {
                console.error("Failed to parse dropped node data:", e);
            }
        },
        [onDrop, screenToFlowPosition]
    );

    return (
        <div ref={reactFlowWrapper} className={styles.container}>
            <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={nodeTypes}
                edgeTypes={edgeTypes}
                onNodesChange={onNodesChange}
                onEdgesChange={onEdgesChange}
                onConnect={onConnect}
                onNodeClick={onNodeClick}
                onPaneClick={onPaneClick}
                onDragOver={onDragOver}
                onDrop={handleDrop}
                fitView
                snapToGrid
                snapGrid={[16, 16]}
                defaultEdgeOptions={{
                    type: "smoothstep",
                    animated: true,
                }}
                deleteKeyCode="Delete"
                selectionKeyCode="Shift"
                minZoom={0.1}
                maxZoom={2}
                attributionPosition="bottom-left"
            >
                <Background
                    variant={BackgroundVariant.Dots}
                    gap={16}
                    size={1}
                    color={tokens.colorNeutralStroke2}
                />
                <Controls showZoom showFitView showInteractive />
                <MiniMap
                    nodeColor={(node: Node<PlaybookNodeData>) => {
                        switch (node.data?.type) {
                            case "start":
                                return tokens.colorNeutralBackground5;
                            case "aiAnalysis":
                            case "aiCompletion":
                                return tokens.colorBrandBackground;
                            case "condition":
                                return tokens.colorPaletteYellowBackground3;
                            case "deliverOutput":
                                return tokens.colorPaletteGreenBackground3;
                            case "createTask":
                            case "sendEmail":
                                return tokens.colorPaletteBerryBackground2;
                            case "wait":
                                return tokens.colorPaletteMagentaBackground2;
                            default:
                                return tokens.colorNeutralBackground3;
                        }
                    }}
                    maskColor={tokens.colorNeutralBackgroundAlpha2}
                    style={{
                        backgroundColor: tokens.colorNeutralBackground2,
                    }}
                />
            </ReactFlow>
        </div>
    );
});

/**
 * PlaybookCanvas — Wrapper that provides ReactFlowProvider context.
 *
 * The useReactFlow() hook requires a ReactFlowProvider ancestor.
 * This component wraps PlaybookCanvasInner with the provider.
 *
 * Usage:
 * ```tsx
 * import { PlaybookCanvas } from "./components/canvas/PlaybookCanvas";
 * <PlaybookCanvas />
 * ```
 */
export { ReactFlowProvider } from "@xyflow/react";

export const PlaybookCanvas = React.memo(function PlaybookCanvas() {
    // Note: ReactFlowProvider must be provided by the parent layout component.
    // This separation allows the parent to also use useReactFlow() if needed.
    return <PlaybookCanvasInner />;
});
