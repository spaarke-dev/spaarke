/**
 * MiniGraph — A compact SVG node-link diagram for previewing document relationships.
 *
 * Pure SVG rendering — no @xyflow/react dependency. Uses the shared
 * useForceSimulation hook for layout computation.
 *
 * Non-interactive preview: clicking anywhere opens the full viewer.
 *
 * @see ADR-021 - Fluent UI v9 design tokens for theming
 * @see ADR-022 - React 16 compatible (useMemo only)
 */

import * as React from "react";
import { tokens } from "@fluentui/react-components";
import { useForceSimulation } from "../../hooks/useForceSimulation";
import type { ForceNode, ForceEdge } from "../../hooks/useForceSimulation";
import type { MiniGraphNode, MiniGraphEdge } from "../../types/MiniGraphTypes";
import { getRelationshipStroke, getRelationshipNodeFill } from "../../utils/relationshipColors";

// ─── Props ───────────────────────────────────────────────────────────

export interface IMiniGraphProps {
    /** Nodes to display in the preview. */
    nodes: MiniGraphNode[];
    /** Edges connecting nodes. */
    edges: MiniGraphEdge[];
    /** SVG width in pixels. */
    width?: number;
    /** SVG height in pixels. */
    height?: number;
    /** Click handler — typically opens the full viewer. */
    onClick?: () => void;
}

// ─── Constants ───────────────────────────────────────────────────────

const DEFAULT_WIDTH = 260;
const DEFAULT_HEIGHT = 140;
const SOURCE_RADIUS = 8;
const RELATED_RADIUS = 5;
const HUB_RADIUS = 6;
const VIEWBOX_PADDING = 30;

/** Hub node types that get a slightly larger radius. */
const HUB_TYPES = new Set(["matter", "project", "invoice", "email"]);

// ─── Helpers ─────────────────────────────────────────────────────────

/**
 * Determine the primary relationship type for a node based on its incoming edges.
 * Used to color the node by its strongest relationship.
 */
function getPrimaryRelationshipType(
    nodeId: string,
    edges: MiniGraphEdge[]
): string | undefined {
    let best: MiniGraphEdge | undefined;
    for (const e of edges) {
        if (e.target === nodeId || e.source === nodeId) {
            if (!best || (e.similarity ?? 0) > (best.similarity ?? 0)) {
                best = e;
            }
        }
    }
    return best?.relationshipType;
}

// ─── Component ───────────────────────────────────────────────────────

export const MiniGraph: React.FC<IMiniGraphProps> = ({
    nodes,
    edges,
    width,
    height,
    onClick,
}) => {
    // Use a ref to measure the container and fill available space
    const containerRef = React.useRef<HTMLDivElement>(null);
    const [measuredSize, setMeasuredSize] = React.useState<{ w: number; h: number } | null>(null);

    React.useEffect(() => {
        if (!containerRef.current) return;
        const el = containerRef.current;
        const observe = () => {
            const rect = el.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) {
                setMeasuredSize({ w: Math.floor(rect.width), h: Math.floor(rect.height) });
            }
        };
        observe();
        const ro = new ResizeObserver(observe);
        ro.observe(el);
        return () => ro.disconnect();
    }, []);

    // Use explicit props if provided, otherwise measured container size, fallback to defaults
    const effectiveWidth = width ?? measuredSize?.w ?? DEFAULT_WIDTH;
    const effectiveHeight = height ?? measuredSize?.h ?? DEFAULT_HEIGHT;
    // Map to ForceNode / ForceEdge for the simulation hook
    const forceNodes: ForceNode[] = React.useMemo(
        () =>
            nodes.map((n) => ({
                id: n.id,
                label: n.label,
                isSource: n.type === "source",
                type: n.type,
                similarity: n.similarity,
            })),
        [nodes]
    );

    const forceEdges: ForceEdge[] = React.useMemo(
        () =>
            edges.map((e) => ({
                source: e.source,
                target: e.target,
                weight: e.similarity ?? 0.5,
                relationshipType: e.relationshipType,
            })),
        [edges]
    );

    // Run synchronous d3-force simulation with compact settings
    const { nodes: positioned, edges: positionedEdges } =
        useForceSimulation(forceNodes, forceEdges, {
            mode: "hub-spoke",
            ticks: 50,
            chargeStrength: -200,
            linkDistanceMultiplier: 100,
            collisionRadius: 15,
        });

    // Normalize positions into pixel space so radii are visually consistent
    const normalized = React.useMemo(() => {
        if (positioned.length === 0) {
            return { nodes: [], edges: [], scale: 1 };
        }

        let minX = Infinity;
        let maxX = -Infinity;
        let minY = Infinity;
        let maxY = -Infinity;

        for (const n of positioned) {
            if (n.x < minX) minX = n.x;
            if (n.x > maxX) maxX = n.x;
            if (n.y < minY) minY = n.y;
            if (n.y > maxY) maxY = n.y;
        }

        const rawSpanX = maxX - minX || 1;
        const rawSpanY = maxY - minY || 1;
        const pad = VIEWBOX_PADDING;
        const usableW = effectiveWidth - pad * 2;
        const usableH = effectiveHeight - pad * 2;
        const scale = Math.min(usableW / rawSpanX, usableH / rawSpanY);
        const midX = (minX + maxX) / 2;
        const midY = (minY + maxY) / 2;

        const mappedNodes = positioned.map((n) => ({
            ...n,
            px: (n.x - midX) * scale + effectiveWidth / 2,
            py: (n.y - midY) * scale + effectiveHeight / 2,
        }));

        const nodePos = new Map<string, { px: number; py: number }>();
        for (const mn of mappedNodes) {
            nodePos.set(mn.id, { px: mn.px, py: mn.py });
        }

        const mappedEdges = positionedEdges.map((e) => {
            const src = nodePos.get(e.source);
            const tgt = nodePos.get(e.target);
            return {
                ...e,
                px1: src?.px ?? 0,
                py1: src?.py ?? 0,
                px2: tgt?.px ?? 0,
                py2: tgt?.py ?? 0,
            };
        });

        return { nodes: mappedNodes, edges: mappedEdges, scale };
    }, [positioned, positionedEdges, effectiveWidth, effectiveHeight]);

    // Don't render if no nodes
    if (nodes.length === 0) {
        return (
            <div
                ref={containerRef}
                style={{ width: "100%", height: "100%", minHeight: "80px" }}
            />
        );
    }

    return (
        <div
            ref={containerRef}
            style={{ width: "100%", height: "100%", minHeight: "80px" }}
        >
        <svg
            width="100%"
            height="100%"
            viewBox={`0 0 ${effectiveWidth} ${effectiveHeight}`}
            preserveAspectRatio="xMidYMid meet"
            onClick={onClick}
            style={{
                cursor: onClick ? "pointer" : "default",
                display: "block",
            }}
            role="img"
            aria-label={`Relationship graph preview with ${nodes.length} documents`}
        >
            {/* Edges — render first so nodes draw on top */}
            {normalized.edges.map((e, i) => {
                const relType = (e as unknown as { relationshipType?: string })
                    .relationshipType;
                const similarity = e.weight ?? 0.5;
                return (
                    <line
                        key={`e-${i}`}
                        x1={e.px1}
                        y1={e.py1}
                        x2={e.px2}
                        y2={e.py2}
                        stroke={getRelationshipStroke(relType)}
                        strokeWidth={1.5}
                        opacity={0.3 + similarity * 0.4}
                    />
                );
            })}

            {/* Nodes — radii now in pixel space */}
            {normalized.nodes.map((n) => {
                const nodeType = (n as unknown as Record<string, unknown>).type as string | undefined;
                const isSource = n.isSource === true || nodeType === "source";
                const isHub = HUB_TYPES.has(nodeType ?? "");
                const r = isSource
                    ? SOURCE_RADIUS
                    : isHub
                      ? HUB_RADIUS
                      : RELATED_RADIUS;

                let fill: string;
                if (isSource) {
                    fill = tokens.colorBrandBackground;
                } else {
                    const relType = getPrimaryRelationshipType(n.id, edges);
                    fill = getRelationshipNodeFill(relType);
                }

                return (
                    <circle
                        key={n.id}
                        cx={n.px}
                        cy={n.py}
                        r={r}
                        fill={fill}
                        stroke={
                            isSource
                                ? tokens.colorBrandStroke1
                                : tokens.colorNeutralStroke2
                        }
                        strokeWidth={isSource ? 2 : 1}
                    />
                );
            })}
        </svg>
        </div>
    );
};

export default MiniGraph;
