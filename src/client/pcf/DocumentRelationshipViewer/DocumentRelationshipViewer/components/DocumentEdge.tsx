/**
 * DocumentEdge - Custom React Flow edge for document relationships
 *
 * Edge styling encodes relationship strength:
 * - 90-100% similarity: thick green (strong relationship)
 * - 75-89% similarity: medium blue (moderate relationship)
 * - 65-74% similarity: thin yellow (weak relationship)
 * - <65% similarity: thin gray dashed (very weak relationship)
 *
 * Follows:
 * - ADR-021: Fluent UI v9 color tokens where applicable
 * - ADR-022: React 16 compatible APIs
 */

import * as React from "react";
import {
    EdgeProps,
    getBezierPath,
    getEdgeCenter,
} from "react-flow-renderer";
import { tokens } from "@fluentui/react-components";
import type { DocumentEdgeData } from "../types/graph";

/**
 * Edge style configuration based on similarity score
 */
interface EdgeStyle {
    strokeWidth: number;
    stroke: string;
    strokeDasharray?: string;
    opacity: number;
}

/**
 * Get edge style based on similarity score
 *
 * - 90-100%: thick (4px), green, solid, high opacity
 * - 75-89%: medium (3px), blue, solid, medium opacity
 * - 65-74%: thin (2px), yellow/warning, solid, medium opacity
 * - <65%: thin (1.5px), gray, dashed, low opacity
 */
const getEdgeStyle = (similarity: number): EdgeStyle => {
    if (similarity >= 0.9) {
        return {
            strokeWidth: 4,
            stroke: tokens.colorStatusSuccessBorder1,
            opacity: 0.9,
        };
    }

    if (similarity >= 0.75) {
        return {
            strokeWidth: 3,
            stroke: tokens.colorBrandStroke1,
            opacity: 0.8,
        };
    }

    if (similarity >= 0.65) {
        return {
            strokeWidth: 2,
            stroke: tokens.colorStatusWarningBorder1,
            opacity: 0.7,
        };
    }

    // Low similarity: thin dashed gray
    return {
        strokeWidth: 1.5,
        stroke: tokens.colorNeutralStroke1,
        strokeDasharray: "5,5",
        opacity: 0.5,
    };
};

/**
 * Get label background color based on similarity
 * Uses semantic status tokens (ADR-021 compliant)
 */
const getLabelStyle = (
    similarity: number
): React.CSSProperties => {
    const baseStyle: React.CSSProperties = {
        fontSize: "10px",
        fontWeight: 500,
        padding: "2px 6px",
        borderRadius: "4px",
        pointerEvents: "all",
    };

    if (similarity >= 0.9) {
        return {
            ...baseStyle,
            backgroundColor: tokens.colorStatusSuccessBackground1,
            color: tokens.colorStatusSuccessForeground1,
        };
    }

    if (similarity >= 0.75) {
        return {
            ...baseStyle,
            backgroundColor: tokens.colorBrandBackground2,
            color: tokens.colorBrandForeground1,
        };
    }

    if (similarity >= 0.65) {
        return {
            ...baseStyle,
            backgroundColor: tokens.colorStatusWarningBackground1,
            color: tokens.colorStatusWarningForeground1,
        };
    }

    return {
        ...baseStyle,
        backgroundColor: tokens.colorNeutralBackground3,
        color: tokens.colorNeutralForeground3,
    };
};

/**
 * DocumentEdge component for React Flow (v10 compatible)
 */
export const DocumentEdge: React.FC<EdgeProps<DocumentEdgeData>> = ({
    id,
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
    data,
    style,
    markerEnd,
    selected,
}) => {
    const similarity = data?.similarity ?? 0.5;
    const edgeStyle = getEdgeStyle(similarity);

    // Calculate the bezier path
    const edgePath = getBezierPath({
        sourceX,
        sourceY,
        sourcePosition,
        targetX,
        targetY,
        targetPosition,
    });

    // Get edge center for label positioning
    const [centerX, centerY] = getEdgeCenter({
        sourceX,
        sourceY,
        targetX,
        targetY,
    });

    // Format similarity as percentage
    const similarityPercent = Math.round(similarity * 100);

    return (
        <>
            {/* Main edge path */}
            <path
                id={id}
                className="react-flow__edge-path"
                d={edgePath}
                markerEnd={markerEnd}
                style={{
                    ...style,
                    strokeWidth: edgeStyle.strokeWidth,
                    stroke: edgeStyle.stroke,
                    strokeDasharray: edgeStyle.strokeDasharray,
                    opacity: selected ? 1 : edgeStyle.opacity,
                    fill: "none",
                }}
            />

            {/* Edge label showing similarity percentage */}
            <foreignObject
                width={50}
                height={24}
                x={centerX - 25}
                y={centerY - 12}
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
                        style={getLabelStyle(similarity)}
                        className="nodrag nopan"
                    >
                        {similarityPercent}%
                    </div>
                </div>
            </foreignObject>
        </>
    );
};

/**
 * Animated version of DocumentEdge for highlighting paths
 */
export const AnimatedDocumentEdge: React.FC<EdgeProps<DocumentEdgeData>> = (
    props
) => {
    const similarity = props.data?.similarity ?? 0.5;
    const edgeStyle = getEdgeStyle(similarity);

    const [edgePath] = getBezierPath({
        sourceX: props.sourceX,
        sourceY: props.sourceY,
        sourcePosition: props.sourcePosition,
        targetX: props.targetX,
        targetY: props.targetY,
        targetPosition: props.targetPosition,
    });

    return (
        <>
            {/* Background path (static) */}
            <path
                id={props.id}
                style={{
                    strokeWidth: edgeStyle.strokeWidth,
                    stroke: edgeStyle.stroke,
                    opacity: edgeStyle.opacity * 0.3,
                }}
                className="react-flow__edge-path"
                d={edgePath}
            />
            {/* Animated overlay */}
            <path
                style={{
                    strokeWidth: edgeStyle.strokeWidth,
                    stroke: edgeStyle.stroke,
                    strokeDasharray: "10,10",
                    opacity: edgeStyle.opacity,
                    animation: "dashdraw 0.5s linear infinite",
                }}
                className="react-flow__edge-path"
                d={edgePath}
            />
        </>
    );
};

export default DocumentEdge;
