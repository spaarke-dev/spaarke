/**
 * ConditionEdge — Custom edges for condition node true/false branches.
 *
 * Migrated from react-flow-renderer v10 to @xyflow/react v12.
 *
 * Key v12 migration changes:
 * - EdgeProps<Edge<ConditionEdgeData>> typed generics
 * - getBezierPath from '@xyflow/react' (getSmoothStepPath also available)
 * - EdgeText replaced with foreignObject for edge labels
 * - BaseEdge available for simpler edge rendering
 * - Fluent design tokens for colors (ADR-021)
 */

import React from "react";
import {
    BaseEdge,
    getBezierPath,
    type EdgeProps,
    type Edge,
} from "@xyflow/react";
import { tokens } from "@fluentui/react-components";
import type { ConditionEdgeData } from "../../types/canvas";

/**
 * Custom edge for true branch connections (green).
 * Uses Fluent design tokens: colorPaletteGreenForeground1 for stroke.
 */
export const TrueBranchEdge: React.FC<EdgeProps<Edge<ConditionEdgeData>>> = ({
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    style,
    markerEnd,
}) => {
    const [edgePath, labelX, labelY] = getBezierPath({
        sourceX,
        sourceY,
        targetX,
        targetY,
        sourcePosition,
        targetPosition,
    });

    return (
        <>
            <BaseEdge
                id={id}
                path={edgePath}
                markerEnd={markerEnd}
                style={{
                    ...style,
                    stroke: tokens.colorPaletteGreenForeground1,
                    strokeWidth: 2,
                }}
            />
            <foreignObject
                width={60}
                height={24}
                x={labelX - 30}
                y={labelY - 12}
                requiredExtensions="http://www.w3.org/1999/xhtml"
            >
                <div
                    style={{
                        display: "flex",
                        justifyContent: "center",
                        alignItems: "center",
                        width: "100%",
                        height: "100%",
                    }}
                >
                    <div
                        className="nodrag nopan"
                        style={{
                            backgroundColor: tokens.colorPaletteGreenBackground2,
                            color: tokens.colorPaletteGreenForeground1,
                            fontWeight: 600,
                            fontSize: tokens.fontSizeBase100,
                            padding: "2px 6px",
                            borderRadius: tokens.borderRadiusSmall,
                            whiteSpace: "nowrap",
                        }}
                    >
                        True
                    </div>
                </div>
            </foreignObject>
        </>
    );
};

/**
 * Custom edge for false branch connections (red).
 * Uses Fluent design tokens: colorPaletteRedForeground1 for stroke.
 */
export const FalseBranchEdge: React.FC<EdgeProps<Edge<ConditionEdgeData>>> = ({
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    style,
    markerEnd,
}) => {
    const [edgePath, labelX, labelY] = getBezierPath({
        sourceX,
        sourceY,
        targetX,
        targetY,
        sourcePosition,
        targetPosition,
    });

    return (
        <>
            <BaseEdge
                id={id}
                path={edgePath}
                markerEnd={markerEnd}
                style={{
                    ...style,
                    stroke: tokens.colorPaletteRedForeground1,
                    strokeWidth: 2,
                }}
            />
            <foreignObject
                width={60}
                height={24}
                x={labelX - 30}
                y={labelY - 12}
                requiredExtensions="http://www.w3.org/1999/xhtml"
            >
                <div
                    style={{
                        display: "flex",
                        justifyContent: "center",
                        alignItems: "center",
                        width: "100%",
                        height: "100%",
                    }}
                >
                    <div
                        className="nodrag nopan"
                        style={{
                            backgroundColor: tokens.colorPaletteRedBackground2,
                            color: tokens.colorPaletteRedForeground1,
                            fontWeight: 600,
                            fontSize: tokens.fontSizeBase100,
                            padding: "2px 6px",
                            borderRadius: tokens.borderRadiusSmall,
                            whiteSpace: "nowrap",
                        }}
                    >
                        False
                    </div>
                </div>
            </foreignObject>
        </>
    );
};
