/**
 * DocumentEdge — Custom @xyflow/react v12 edge component
 *
 * Key v12 migration change:
 * - getBezierPath returns [path, labelX, labelY, offsetX, offsetY]
 * - getEdgeCenter is removed; use labelX/labelY from getBezierPath
 */

import React from "react";
import { type EdgeProps, getBezierPath } from "@xyflow/react";
import { tokens } from "@fluentui/react-components";
import type { DocumentEdge as TDocumentEdge } from "../types/graph";

interface EdgeStyle {
    strokeWidth: number;
    stroke: string;
    strokeDasharray?: string;
    opacity: number;
}

/** Color-code edge stroke by relationship type */
const getRelationshipStroke = (type?: string): string => {
    switch (type) {
        case "semantic": return tokens.colorBrandStroke1;
        case "same_matter": return tokens.colorPaletteGreenBorder2;
        case "same_project": return tokens.colorPaletteGreenBorder1;
        case "same_email": case "same_thread": return tokens.colorStatusWarningBorder1;
        case "same_invoice": return tokens.colorPaletteBerryBorder2;
        default: return tokens.colorNeutralStroke1;
    }
};

const getEdgeStyle = (similarity: number, relationshipType?: string): EdgeStyle => {
    const stroke = getRelationshipStroke(relationshipType);
    if (similarity >= 0.9) return { strokeWidth: 4, stroke, opacity: 0.9 };
    if (similarity >= 0.75) return { strokeWidth: 3, stroke, opacity: 0.8 };
    if (similarity >= 0.65) return { strokeWidth: 2, stroke, opacity: 0.7 };
    return { strokeWidth: 1.5, stroke, strokeDasharray: "5,5", opacity: 0.5 };
};

/** Color-code edge label by relationship type */
const getLabelColors = (type?: string): { bg: string; fg: string } => {
    switch (type) {
        case "semantic": return { bg: tokens.colorBrandBackground2, fg: tokens.colorBrandForeground1 };
        case "same_matter": case "same_project": return { bg: tokens.colorPaletteGreenBackground2, fg: tokens.colorPaletteGreenForeground1 };
        case "same_email": case "same_thread": return { bg: tokens.colorStatusWarningBackground1, fg: tokens.colorStatusWarningForeground1 };
        case "same_invoice": return { bg: tokens.colorPaletteBerryBackground2, fg: tokens.colorPaletteBerryForeground2 };
        default: return { bg: tokens.colorNeutralBackground3, fg: tokens.colorNeutralForeground3 };
    }
};

const getLabelStyle = (type?: string): React.CSSProperties => {
    const { bg, fg } = getLabelColors(type);
    return {
        fontSize: "9px", fontWeight: 600, padding: "2px 6px", borderRadius: "4px",
        pointerEvents: "all", whiteSpace: "nowrap",
        backgroundColor: bg, color: fg,
    };
};

/**
 * DocumentEdge component for @xyflow/react v12
 *
 * EdgeProps<TDocumentEdge> gives data: TDocumentEdge['data'] = DocumentEdgeData
 * getBezierPath v12 returns [path, labelX, labelY, offsetX, offsetY]
 */
export const DocumentEdge: React.FC<EdgeProps<TDocumentEdge>> = ({
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
    const relationshipType = data?.relationshipType;
    const edgeStyle = getEdgeStyle(similarity, relationshipType);

    // v12: getBezierPath returns [path, labelX, labelY, offsetX, offsetY]
    const [edgePath, labelX, labelY] = getBezierPath({
        sourceX, sourceY, sourcePosition,
        targetX, targetY, targetPosition,
    });

    const similarityPercent = Math.round(similarity * 100);

    return (
        <>
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
            <foreignObject width={120} height={24} x={labelX - 60} y={labelY - 12} requiredExtensions="http://www.w3.org/1999/xhtml">
                <div style={{ display: "flex", justifyContent: "center", alignItems: "center", width: "100%", height: "100%" }}>
                    <div style={getLabelStyle(relationshipType)} className="nodrag nopan">
                        {data?.relationshipLabel ? `${data.relationshipLabel} ${similarityPercent}%` : `${similarityPercent}%`}
                    </div>
                </div>
            </foreignObject>
        </>
    );
};

export default DocumentEdge;
