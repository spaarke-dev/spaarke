/**
 * SearchResultsMap — Interactive network graph showing document similarity
 *
 * Documents are represented as draggable circles (nodes) connected by lines
 * (edges) whose thickness and opacity indicate relative similarity. Similarity
 * percentages are displayed on each connection line.
 *
 * Features:
 *   - Live d3-force simulation with interactive dragging
 *   - Similarity percentages displayed on connection lines
 *   - Node radius scaled by relevance score (8-24px)
 *   - Pan (click-drag background) and zoom (mouse wheel) navigation
 *   - Hover tooltip showing name, score, category, summary, connections
 *   - Click to open document preview dialog
 *   - Color legend in upper-left corner
 *
 * @see useSimilarityProjection — data computation hook (nodes + edges)
 * @see colorScale — getCategoryColor, buildColorLegend
 * @see ADR-021 — Fluent UI v9 design system
 */

import React, { useState, useCallback, useMemo, useRef, useEffect } from "react";
import {
    makeStyles,
    tokens,
    Spinner,
    Text,
    mergeClasses,
} from "@fluentui/react-components";
import {
    forceSimulation,
    forceManyBody,
    forceCollide,
    forceLink,
    forceX,
    forceY,
    type SimulationNodeDatum,
    type SimulationLinkDatum,
} from "d3-force";
import type { DocumentSearchResult, RecordSearchResult, SearchDomain, VisualizationColorBy } from "../types";
import { useSimilarityProjection } from "../hooks/useSimilarityProjection";
import { getCategoryColor, buildColorLegend } from "../utils/colorScale";
import { getResultId, getResultDomain, isDocumentResult } from "../utils/groupResults";

// =============================================
// Simulation types
// =============================================

interface SimNode extends SimulationNodeDatum {
    id: string;
    radius: number;
    category: string;
    name: string;
    score: number;
    result: DocumentSearchResult | RecordSearchResult;
}

interface SimLink extends SimulationLinkDatum<SimNode> {
    similarity: number;
}

// =============================================
// Props
// =============================================

export interface SearchResultsMapProps {
    /** Search results to project onto the network graph. */
    results: (DocumentSearchResult | RecordSearchResult)[];
    /** Category field for color-coding points. */
    colorBy: VisualizationColorBy;
    /** Minimum similarity threshold (0-100 scale). */
    minSimilarity: number;
    /** Whether results are still loading. */
    isLoading: boolean;
    /** Active search domain tab. */
    activeDomain: SearchDomain;
    /** Callback when a result point is clicked. Optionally includes click screen coordinates. */
    onResultClick: (resultId: string, domain: SearchDomain, clickPosition?: { x: number; y: number }) => void;
}

// =============================================
// Constants
// =============================================

const VIEWPORT_PADDING = 50;
const MIN_SCALE = 0.2;
const MAX_SCALE = 5;
const ZOOM_FACTOR = 0.001;
const CLICK_THRESHOLD = 5; // px movement to distinguish click from drag

// Force simulation parameters — tuned for spread-out, readable layout
const INITIAL_TICKS = 300;
const CHARGE_STRENGTH = -600;
const LINK_DISTANCE_MAX = 350;
const LINK_DISTANCE_MIN = 100;
const CENTER_STRENGTH = 0.015;
const LINK_STRENGTH_FACTOR = 0.08;

// =============================================
// Styles
// =============================================

const useStyles = makeStyles({
    container: {
        position: "relative",
        flex: 1,
        overflow: "hidden",
        width: "100%",
        height: "100%",
    },
    svg: {
        width: "100%",
        height: "100%",
        cursor: "grab",
    },
    svgPanning: {
        cursor: "grabbing",
    },
    centerMessage: {
        position: "absolute",
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        gap: tokens.spacingVerticalM,
        color: tokens.colorNeutralForeground3,
    },
    legend: {
        position: "absolute",
        top: tokens.spacingVerticalS,
        left: tokens.spacingHorizontalS,
        display: "flex",
        flexDirection: "column",
        gap: "6px",
        paddingTop: tokens.spacingVerticalS,
        paddingBottom: tokens.spacingVerticalS,
        paddingLeft: tokens.spacingHorizontalM,
        paddingRight: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow4,
        maxHeight: "280px",
        overflowY: "auto",
    },
    legendItem: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
    },
    legendLabel: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground2,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
        maxWidth: "200px",
    },
    tooltip: {
        position: "absolute",
        pointerEvents: "none",
        paddingTop: tokens.spacingVerticalXS,
        paddingBottom: tokens.spacingVerticalXS,
        paddingLeft: tokens.spacingHorizontalS,
        paddingRight: tokens.spacingHorizontalS,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        boxShadow: tokens.shadow8,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
        zIndex: 20,
        maxWidth: "300px",
    },
    tooltipName: {
        fontSize: tokens.fontSizeBase200,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "nowrap",
        overflow: "hidden",
        textOverflow: "ellipsis",
    },
    tooltipDetail: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground3,
    },
    tooltipSummary: {
        fontSize: tokens.fontSizeBase100,
        color: tokens.colorNeutralForeground2,
        marginTop: "4px",
        display: "-webkit-box",
        WebkitLineClamp: 3,
        WebkitBoxOrient: "vertical" as const,
        overflow: "hidden",
    },
});

// =============================================
// Component
// =============================================

export const SearchResultsMap: React.FC<SearchResultsMapProps> = ({
    results,
    colorBy,
    minSimilarity,
    isLoading,
    activeDomain,
    onResultClick,
}) => {
    const styles = useStyles();

    // Data hook — computes node data and pairwise similarity edges
    const { nodes: nodeData, edges: edgeData } = useSimilarityProjection(
        results, colorBy, minSimilarity,
    );

    // ─── Container sizing via ResizeObserver ───
    const containerRef = useRef<HTMLDivElement | null>(null);
    const [dimensions, setDimensions] = useState({ width: 0, height: 0 });

    const containerCallbackRef = useCallback((node: HTMLDivElement | null) => {
        if (containerRef.current) {
            const obs = (containerRef.current as unknown as { __ro?: ResizeObserver }).__ro;
            obs?.disconnect();
            containerRef.current = null;
        }
        if (node) {
            containerRef.current = node;
            const rect = node.getBoundingClientRect();
            setDimensions({ width: rect.width, height: rect.height });
            const observer = new ResizeObserver((entries) => {
                for (const entry of entries) {
                    setDimensions({
                        width: entry.contentRect.width,
                        height: entry.contentRect.height,
                    });
                }
            });
            observer.observe(node);
            (node as unknown as { __ro?: ResizeObserver }).__ro = observer;
        }
    }, []);

    useEffect(() => {
        return () => {
            const node = containerRef.current;
            if (node) {
                (node as unknown as { __ro?: ResizeObserver }).__ro?.disconnect();
            }
        };
    }, []);

    // ─── SVG ref for coordinate conversion ───
    const svgRef = useRef<SVGSVGElement | null>(null);

    // ─── Simulation refs ───
    const simulationRef = useRef<ReturnType<typeof forceSimulation<SimNode>> | null>(null);
    const simNodesRef = useRef<SimNode[]>([]);
    const simLinksRef = useRef<SimLink[]>([]);
    const simNodeMapRef = useRef<Map<string, SimNode>>(new Map());
    const [, setTickCount] = useState(0);

    // ─── View transform (pan/zoom) ───
    const [viewTransform, setViewTransform] = useState({ x: 0, y: 0, scale: 1 });
    const viewTransformRef = useRef(viewTransform);
    viewTransformRef.current = viewTransform;

    // Keep dimensions in a ref for the simulation effect
    const dimRef = useRef(dimensions);
    dimRef.current = dimensions;

    // Stable ref for onResultClick
    const onResultClickRef = useRef(onResultClick);
    onResultClickRef.current = onResultClick;

    // ─── Create/recreate simulation when data changes ───
    useEffect(() => {
        simulationRef.current?.stop();

        if (nodeData.length === 0) {
            simNodesRef.current = [];
            simLinksRef.current = [];
            simNodeMapRef.current = new Map();
            return;
        }

        // Create simulation nodes with random initial spread
        const simNodes: SimNode[] = nodeData.map((n) => ({
            id: n.id,
            radius: n.radius,
            category: n.category,
            name: n.name,
            score: n.score,
            result: n.result,
            x: (Math.random() - 0.5) * 400,
            y: (Math.random() - 0.5) * 400,
        }));

        // Create simulation links (d3 resolves source/target to node refs on tick)
        const simLinks: SimLink[] = edgeData.map((e) => ({
            source: e.sourceId as unknown as SimNode,
            target: e.targetId as unknown as SimNode,
            similarity: e.similarity,
        }));

        // Build lookup map
        const nodeMap = new Map<string, SimNode>();
        for (const n of simNodes) nodeMap.set(n.id, n);

        simNodesRef.current = simNodes;
        simLinksRef.current = simLinks;
        simNodeMapRef.current = nodeMap;

        // Create simulation with strong repulsion for good spread
        const sim = forceSimulation<SimNode>(simNodes)
            .force("charge", forceManyBody<SimNode>().strength(CHARGE_STRENGTH))
            .force("x", forceX<SimNode>(0).strength(CENTER_STRENGTH))
            .force("y", forceY<SimNode>(0).strength(CENTER_STRENGTH))
            .force(
                "collide",
                forceCollide<SimNode>().radius((d) => d.radius + 4),
            )
            .force(
                "link",
                forceLink<SimNode, SimLink>(simLinks)
                    .id((d) => d.id)
                    .distance((d) => LINK_DISTANCE_MAX * (1 - d.similarity) + LINK_DISTANCE_MIN)
                    .strength((d) => d.similarity * LINK_STRENGTH_FACTOR),
            )
            .stop();

        // Run synchronous initial layout for immediate positions
        sim.tick(INITIAL_TICKS);

        // Auto-fit viewport to initial positions
        const { width, height } = dimRef.current;
        if (width > 0 && height > 0) {
            let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
            for (const n of simNodes) {
                const nx = n.x ?? 0, ny = n.y ?? 0;
                if (nx - n.radius < minX) minX = nx - n.radius;
                if (nx + n.radius > maxX) maxX = nx + n.radius;
                if (ny - n.radius < minY) minY = ny - n.radius;
                if (ny + n.radius > maxY) maxY = ny + n.radius;
            }
            minX -= VIEWPORT_PADDING;
            maxX += VIEWPORT_PADDING;
            minY -= VIEWPORT_PADDING;
            maxY += VIEWPORT_PADDING;
            const w = maxX - minX, h = maxY - minY;
            const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            const fitScale = Math.min(width / w, height / h) * 0.9;
            const cs = Math.max(MIN_SCALE, Math.min(MAX_SCALE, fitScale));
            setViewTransform({
                x: width / 2 - cx * cs,
                y: height / 2 - cy * cs,
                scale: cs,
            });
        }

        // Trigger initial render with computed positions
        setTickCount((t) => t + 1);

        // Start interactive mode at very low alpha (barely moving, ready for drag)
        sim.alpha(0.005)
            .alphaDecay(0.02)
            .on("tick", () => setTickCount((t) => t + 1))
            .restart();

        simulationRef.current = sim;
        return () => { sim.stop(); };
    }, [nodeData, edgeData]);

    // Re-fit on container resize
    useEffect(() => {
        const nodes = simNodesRef.current;
        if (nodes.length === 0 || dimensions.width === 0 || dimensions.height === 0) return;

        let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
        for (const n of nodes) {
            const nx = n.x ?? 0, ny = n.y ?? 0;
            if (nx - n.radius < minX) minX = nx - n.radius;
            if (nx + n.radius > maxX) maxX = nx + n.radius;
            if (ny - n.radius < minY) minY = ny - n.radius;
            if (ny + n.radius > maxY) maxY = ny + n.radius;
        }
        minX -= VIEWPORT_PADDING;
        maxX += VIEWPORT_PADDING;
        minY -= VIEWPORT_PADDING;
        maxY += VIEWPORT_PADDING;
        const w = maxX - minX, h = maxY - minY;
        const cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        const fitScale = Math.min(dimensions.width / w, dimensions.height / h) * 0.9;
        const cs = Math.max(MIN_SCALE, Math.min(MAX_SCALE, fitScale));
        setViewTransform({
            x: dimensions.width / 2 - cx * cs,
            y: dimensions.height / 2 - cy * cs,
            scale: cs,
        });
    }, [dimensions.width, dimensions.height]);

    // ─── Interaction state ───
    const [isPanning, setIsPanning] = useState(false);
    const panStart = useRef<{ x: number; y: number; tx: number; ty: number } | null>(null);
    const draggedNodeId = useRef<string | null>(null);

    // Hover state
    const [hoveredId, setHoveredId] = useState<string | null>(null);
    const [tooltipPos, setTooltipPos] = useState({ x: 0, y: 0 });

    // ─── Node drag handlers (via document-level listeners for reliability) ───
    const handleNodePointerDown = useCallback((e: React.PointerEvent, nodeId: string) => {
        e.stopPropagation();
        e.preventDefault();
        draggedNodeId.current = nodeId;

        const startX = e.clientX;
        const startY = e.clientY;

        // Set fx/fy to pin node at current cursor position
        const svg = svgRef.current;
        if (svg) {
            const rect = svg.getBoundingClientRect();
            const vt = viewTransformRef.current;
            const node = simNodeMapRef.current.get(nodeId);
            if (node) {
                node.fx = (e.clientX - rect.left - vt.x) / vt.scale;
                node.fy = (e.clientY - rect.top - vt.y) / vt.scale;
            }
        }

        // Reheat simulation for smooth dragging
        simulationRef.current?.alphaTarget(0.3).restart();

        const onMove = (ev: PointerEvent) => {
            const svgEl = svgRef.current;
            if (!svgEl || !draggedNodeId.current) return;
            const rect = svgEl.getBoundingClientRect();
            const vt = viewTransformRef.current;
            const node = simNodeMapRef.current.get(draggedNodeId.current);
            if (node) {
                node.fx = (ev.clientX - rect.left - vt.x) / vt.scale;
                node.fy = (ev.clientY - rect.top - vt.y) / vt.scale;
            }
        };

        const onUp = (ev: PointerEvent) => {
            // Release node
            const node = draggedNodeId.current
                ? simNodeMapRef.current.get(draggedNodeId.current)
                : null;
            if (node) {
                node.fx = null;
                node.fy = null;
            }

            // Check if this was a click (minimal movement) vs drag
            const dx = ev.clientX - startX;
            const dy = ev.clientY - startY;
            const wasClick = Math.abs(dx) < CLICK_THRESHOLD && Math.abs(dy) < CLICK_THRESHOLD;

            if (wasClick && node) {
                onResultClickRef.current(
                    getResultId(node.result),
                    getResultDomain(node.result),
                    { x: ev.clientX, y: ev.clientY },
                );
            }

            draggedNodeId.current = null;
            simulationRef.current?.alphaTarget(0);
            document.removeEventListener("pointermove", onMove);
            document.removeEventListener("pointerup", onUp);
        };

        document.addEventListener("pointermove", onMove);
        document.addEventListener("pointerup", onUp);
    }, []);

    // ─── Canvas pan handlers (on SVG background) ───
    const handleSvgMouseDown = useCallback(
        (e: React.MouseEvent<SVGSVGElement>) => {
            if (e.button !== 0) return;
            if (draggedNodeId.current) return;
            setIsPanning(true);
            panStart.current = {
                x: e.clientX,
                y: e.clientY,
                tx: viewTransform.x,
                ty: viewTransform.y,
            };
        },
        [viewTransform.x, viewTransform.y],
    );

    const handleSvgMouseMove = useCallback(
        (e: React.MouseEvent<SVGSVGElement>) => {
            // Update tooltip position
            const rect = e.currentTarget.getBoundingClientRect();
            setTooltipPos({
                x: e.clientX - rect.left + 12,
                y: e.clientY - rect.top + 12,
            });

            // Handle panning
            if (!isPanning || !panStart.current) return;
            setViewTransform((prev) => ({
                ...prev,
                x: panStart.current!.tx + (e.clientX - panStart.current!.x),
                y: panStart.current!.ty + (e.clientY - panStart.current!.y),
            }));
        },
        [isPanning],
    );

    const handleSvgMouseUp = useCallback(() => {
        setIsPanning(false);
        panStart.current = null;
    }, []);

    const handleSvgMouseLeave = useCallback(() => {
        setIsPanning(false);
        panStart.current = null;
        setHoveredId(null);
    }, []);

    // ─── Zoom toward cursor ───
    const handleWheel = useCallback((e: React.WheelEvent<SVGSVGElement>) => {
        e.preventDefault();
        const rect = e.currentTarget.getBoundingClientRect();
        const cursorX = e.clientX - rect.left;
        const cursorY = e.clientY - rect.top;
        setViewTransform((prev) => {
            const delta = -e.deltaY * ZOOM_FACTOR;
            const newScale = Math.max(MIN_SCALE, Math.min(MAX_SCALE, prev.scale * (1 + delta)));
            const ratio = newScale / prev.scale;
            return {
                x: cursorX - (cursorX - prev.x) * ratio,
                y: cursorY - (cursorY - prev.y) * ratio,
                scale: newScale,
            };
        });
    }, []);

    // ─── Color legend ───
    const legendEntries = useMemo(() => {
        const categories = nodeData.map((n) => n.category);
        return buildColorLegend(categories);
    }, [nodeData]);

    // ─── Hovered node data for tooltip ───
    const hoveredNode = useMemo(() => {
        if (!hoveredId) return null;
        return simNodeMapRef.current.get(hoveredId) ?? null;
    }, [hoveredId]);

    const hoveredSummary = useMemo(() => {
        if (!hoveredNode) return null;
        const r = hoveredNode.result;
        if (isDocumentResult(r)) {
            return r.tldr || r.summary || null;
        }
        return "recordDescription" in r
            ? (r as unknown as { recordDescription?: string }).recordDescription ?? null
            : null;
    }, [hoveredNode]);

    const hoveredConnectionCount = useMemo(() => {
        if (!hoveredId) return 0;
        return edgeData.filter(
            (e) => e.sourceId === hoveredId || e.targetId === hoveredId,
        ).length;
    }, [hoveredId, edgeData]);

    // ─── Read current node/link positions from simulation refs for rendering ───
    const simNodes = simNodesRef.current;
    const simLinks = simLinksRef.current;

    // ─── Loading state ───
    if (isLoading) {
        return (
            <div className={styles.container} ref={containerCallbackRef}>
                <div className={styles.centerMessage}>
                    <Spinner size="medium" label="Computing similarity layout..." />
                </div>
            </div>
        );
    }

    // ─── Empty state ───
    if (nodeData.length === 0) {
        return (
            <div className={styles.container} ref={containerCallbackRef}>
                <div className={styles.centerMessage}>
                    <Text size={400} weight="semibold">
                        No results to visualize
                    </Text>
                    <Text size={200}>
                        Run a search to see the document network graph
                    </Text>
                </div>
            </div>
        );
    }

    return (
        <div className={styles.container} ref={containerCallbackRef}>
            {/* SVG network graph */}
            <svg
                ref={svgRef}
                className={mergeClasses(
                    styles.svg,
                    isPanning ? styles.svgPanning : undefined,
                )}
                onWheel={handleWheel}
                onMouseDown={handleSvgMouseDown}
                onMouseMove={handleSvgMouseMove}
                onMouseUp={handleSvgMouseUp}
                onMouseLeave={handleSvgMouseLeave}
                aria-label={`Network graph showing ${simNodes.length} results with ${simLinks.length} similarity connections`}
            >
                <g
                    style={{
                        transform: `translate(${viewTransform.x}px, ${viewTransform.y}px) scale(${viewTransform.scale})`,
                        transformOrigin: "0 0",
                    }}
                >
                    {/* Similarity edges + labels — render behind nodes */}
                    {simLinks.map((link, i) => {
                        const src = link.source as SimNode;
                        const tgt = link.target as SimNode;
                        if (typeof src === "string" || typeof tgt === "string") return null;
                        const sx = src.x ?? 0, sy = src.y ?? 0;
                        const tx = tgt.x ?? 0, ty = tgt.y ?? 0;

                        // Edge visual properties
                        const strokeWidth = 0.5 + link.similarity * 2.5;
                        const baseOpacity = 0.08 + link.similarity * 0.27;
                        const isConnected = hoveredId === src.id || hoveredId === tgt.id;

                        // Edge midpoint for label
                        const midX = (sx + tx) / 2;
                        const midY = (sy + ty) / 2;

                        return (
                            <g key={`edge-${i}`}>
                                <line
                                    x1={sx}
                                    y1={sy}
                                    x2={tx}
                                    y2={ty}
                                    stroke={tokens.colorNeutralForeground3}
                                    strokeWidth={isConnected ? strokeWidth * 1.5 : strokeWidth}
                                    style={{
                                        opacity: hoveredId
                                            ? isConnected
                                                ? Math.min(baseOpacity * 3, 0.7)
                                                : baseOpacity * 0.3
                                            : baseOpacity,
                                        transition: "opacity 0.15s ease",
                                    }}
                                />
                                {/* Similarity percentage label */}
                                <text
                                    x={midX}
                                    y={midY}
                                    dy="-4"
                                    textAnchor="middle"
                                    fontSize="9"
                                    fill={tokens.colorNeutralForeground3}
                                    style={{
                                        opacity: hoveredId
                                            ? isConnected
                                                ? 0.9
                                                : 0.0
                                            : 0.15,
                                        transition: "opacity 0.15s ease",
                                        pointerEvents: "none",
                                        userSelect: "none",
                                    }}
                                >
                                    {Math.round(link.similarity * 100)}%
                                </text>
                            </g>
                        );
                    })}

                    {/* Result nodes — draggable */}
                    {simNodes.map((node) => {
                        const colors = getCategoryColor(node.category);
                        const isHovered = hoveredId === node.id;

                        return (
                            <circle
                                key={node.id}
                                cx={node.x ?? 0}
                                cy={node.y ?? 0}
                                r={isHovered ? node.radius * 1.2 : node.radius}
                                fill={colors.background}
                                stroke={colors.foreground}
                                strokeWidth={isHovered ? 2.5 : 1}
                                style={{
                                    cursor: draggedNodeId.current === node.id ? "grabbing" : "grab",
                                    opacity: hoveredId && !isHovered ? 0.5 : 1,
                                    transition: "r 0.15s ease, opacity 0.15s ease",
                                }}
                                onMouseEnter={() => setHoveredId(node.id)}
                                onMouseLeave={() => setHoveredId(null)}
                                onPointerDown={(e) => handleNodePointerDown(e, node.id)}
                            />
                        );
                    })}
                </g>
            </svg>

            {/* Color legend — upper-left */}
            {legendEntries.length > 0 && (
                <div className={styles.legend}>
                    {legendEntries.map((entry) => (
                        <div key={entry.key} className={styles.legendItem}>
                            <svg width="16" height="16">
                                <circle
                                    cx={8}
                                    cy={8}
                                    r={7}
                                    fill={entry.background}
                                    stroke={entry.foreground}
                                    strokeWidth={1}
                                />
                            </svg>
                            <span className={styles.legendLabel}>{entry.key}</span>
                        </div>
                    ))}
                </div>
            )}

            {/* Hover tooltip — hidden during drag */}
            {hoveredNode && !draggedNodeId.current && (
                <div
                    className={styles.tooltip}
                    style={{ left: tooltipPos.x, top: tooltipPos.y }}
                >
                    <div className={styles.tooltipName}>{hoveredNode.name}</div>
                    <div className={styles.tooltipDetail}>
                        Score: {Math.round(hoveredNode.score * 100)}%
                    </div>
                    <div className={styles.tooltipDetail}>
                        {hoveredNode.category}
                    </div>
                    {hoveredConnectionCount > 0 && (
                        <div className={styles.tooltipDetail}>
                            {hoveredConnectionCount} connection
                            {hoveredConnectionCount !== 1 ? "s" : ""}
                        </div>
                    )}
                    {hoveredSummary && (
                        <div className={styles.tooltipSummary}>
                            {hoveredSummary}
                        </div>
                    )}
                </div>
            )}
        </div>
    );
};

export default SearchResultsMap;
