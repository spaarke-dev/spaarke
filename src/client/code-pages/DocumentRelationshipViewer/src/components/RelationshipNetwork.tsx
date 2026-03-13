/**
 * RelationshipNetwork — SVG network graph showing document similarity
 *
 * Adapted from SemanticSearch's SearchResultsMap for the DocumentRelationshipViewer.
 * Documents are rendered as draggable circles connected by similarity edges.
 * Similar documents cluster together via d3-force simulation.
 *
 * Features:
 *   - Live d3-force simulation with interactive dragging
 *   - Similarity percentages on connection lines
 *   - Node radius scaled by similarity (source node is largest)
 *   - Pan (click-drag background) and zoom (mouse wheel)
 *   - Hover tooltip with name, similarity, type, relationship
 *   - Click to open FilePreviewDialog
 *   - Color-coded by relationship type
 */

import React, {
  useState,
  useCallback,
  useMemo,
  useRef,
  useEffect,
} from "react";
import {
  makeStyles,
  tokens,
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
import type {
  DocumentNode,
  DocumentEdge,
  DocumentNodeData,
} from "../types/graph";

// =============================================
// Simulation types
// =============================================

interface SimNode extends SimulationNodeDatum {
  id: string;
  radius: number;
  color: string;
  borderColor: string;
  name: string;
  similarity: number;
  isSource: boolean;
  data: DocumentNodeData;
}

interface SimLink extends SimulationLinkDatum<SimNode> {
  similarity: number;
}

// =============================================
// Props
// =============================================

export interface RelationshipNetworkProps {
  nodes: DocumentNode[];
  edges: DocumentEdge[];
  isDarkMode?: boolean;
  onNodeClick?: (documentId: string, documentName: string) => void;
}

// =============================================
// Constants
// =============================================

const VIEWPORT_PADDING = 50;
const MIN_SCALE = 0.2;
const MAX_SCALE = 5;
const ZOOM_FACTOR = 0.001;
const CLICK_THRESHOLD = 5;

const INITIAL_TICKS = 300;
const CHARGE_STRENGTH = -600;
const LINK_DISTANCE_MAX = 350;
const LINK_DISTANCE_MIN = 100;
const CENTER_STRENGTH = 0.015;
const LINK_STRENGTH_FACTOR = 0.08;

// Relationship type → color mapping
const RELATIONSHIP_COLORS: Record<string, { bg: string; border: string }> = {
  semantic: {
    bg: tokens.colorBrandBackground2,
    border: tokens.colorBrandStroke1,
  },
  same_matter: {
    bg: tokens.colorPaletteGreenBackground2,
    border: tokens.colorPaletteGreenBorder2,
  },
  same_project: {
    bg: tokens.colorPaletteGreenBackground2,
    border: tokens.colorPaletteGreenBorder2,
  },
  same_email: {
    bg: tokens.colorPaletteYellowBackground2,
    border: tokens.colorPaletteYellowBorder2,
  },
  same_thread: {
    bg: tokens.colorPaletteYellowBackground2,
    border: tokens.colorPaletteYellowBorder2,
  },
  same_invoice: {
    bg: tokens.colorPaletteRedBackground2,
    border: tokens.colorPaletteRedBorder2,
  },
};

const DEFAULT_COLOR = {
  bg: tokens.colorNeutralBackground3,
  border: tokens.colorNeutralStroke1,
};

function getNodeColor(data: DocumentNodeData): { bg: string; border: string } {
  if (data.isSource)
    return {
      bg: tokens.colorBrandBackground,
      border: tokens.colorBrandStroke1,
    };
  const relType = data.relationshipTypes?.[0]?.type ?? data.relationshipType;
  if (relType && RELATIONSHIP_COLORS[relType])
    return RELATIONSHIP_COLORS[relType];
  return DEFAULT_COLOR;
}

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
  },
  legendItem: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  legendLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    whiteSpace: "nowrap",
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
});

// =============================================
// Component
// =============================================

export const RelationshipNetwork: React.FC<RelationshipNetworkProps> = ({
  nodes: inputNodes,
  edges: inputEdges,
  onNodeClick,
}) => {
  const styles = useStyles();

  // ─── Container sizing via ResizeObserver ───
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [dimensions, setDimensions] = useState({ width: 0, height: 0 });

  const containerCallbackRef = useCallback((node: HTMLDivElement | null) => {
    if (containerRef.current) {
      const obs = (containerRef.current as unknown as { __ro?: ResizeObserver })
        .__ro;
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
      if (node)
        (node as unknown as { __ro?: ResizeObserver }).__ro?.disconnect();
    };
  }, []);

  // ─── SVG ref ───
  const svgRef = useRef<SVGSVGElement | null>(null);

  // ─── Simulation refs ───
  const simulationRef = useRef<ReturnType<
    typeof forceSimulation<SimNode>
  > | null>(null);
  const simNodesRef = useRef<SimNode[]>([]);
  const simLinksRef = useRef<SimLink[]>([]);
  const simNodeMapRef = useRef<Map<string, SimNode>>(new Map());
  const [, setTickCount] = useState(0);

  // ─── View transform ───
  const [viewTransform, setViewTransform] = useState({ x: 0, y: 0, scale: 1 });
  const viewTransformRef = useRef(viewTransform);
  viewTransformRef.current = viewTransform;

  const dimRef = useRef(dimensions);
  dimRef.current = dimensions;

  const onNodeClickRef = useRef(onNodeClick);
  onNodeClickRef.current = onNodeClick;

  // ─── Build simulation data from DocumentNodes ───
  const { simNodeData, simEdgeData } = useMemo(() => {
    // Filter out hub nodes (matter/project/invoice/email)
    const docNodes = inputNodes.filter((n) => {
      const t = n.data.nodeType;
      return (
        t !== "matter" && t !== "project" && t !== "invoice" && t !== "email"
      );
    });

    const nodeData = docNodes.map((n) => {
      const sim = n.data.similarity ?? 0;
      const colors = getNodeColor(n.data);
      return {
        id: n.id,
        radius: n.data.isSource ? 24 : 8 + sim * 16,
        color: colors.bg,
        borderColor: colors.border,
        name: n.data.name,
        similarity: sim,
        isSource: n.data.isSource ?? false,
        data: n.data,
      };
    });

    const nodeIdSet = new Set(docNodes.map((n) => n.id));
    const edgeData = inputEdges
      .filter((e) => nodeIdSet.has(e.source) && nodeIdSet.has(e.target))
      .map((e) => ({
        source: e.source,
        target: e.target,
        similarity: e.data?.similarity ?? 0.5,
      }));

    return { simNodeData: nodeData, simEdgeData: edgeData };
  }, [inputNodes, inputEdges]);

  // ─── Create/recreate simulation ───
  useEffect(() => {
    simulationRef.current?.stop();

    if (simNodeData.length === 0) {
      simNodesRef.current = [];
      simLinksRef.current = [];
      simNodeMapRef.current = new Map();
      return;
    }

    const simNodes: SimNode[] = simNodeData.map((n) => ({
      ...n,
      x: (Math.random() - 0.5) * 400,
      y: (Math.random() - 0.5) * 400,
    }));

    const simLinks: SimLink[] = simEdgeData.map((e) => ({
      source: e.source as unknown as SimNode,
      target: e.target as unknown as SimNode,
      similarity: e.similarity,
    }));

    const nodeMap = new Map<string, SimNode>();
    for (const n of simNodes) nodeMap.set(n.id, n);

    simNodesRef.current = simNodes;
    simLinksRef.current = simLinks;
    simNodeMapRef.current = nodeMap;

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
          .distance(
            (d) => LINK_DISTANCE_MAX * (1 - d.similarity) + LINK_DISTANCE_MIN,
          )
          .strength((d) => d.similarity * LINK_STRENGTH_FACTOR),
      )
      .stop();

    sim.tick(INITIAL_TICKS);

    // Auto-fit
    const { width, height } = dimRef.current;
    if (width > 0 && height > 0) {
      let minX = Infinity,
        maxX = -Infinity,
        minY = Infinity,
        maxY = -Infinity;
      for (const n of simNodes) {
        const nx = n.x ?? 0,
          ny = n.y ?? 0;
        if (nx - n.radius < minX) minX = nx - n.radius;
        if (nx + n.radius > maxX) maxX = nx + n.radius;
        if (ny - n.radius < minY) minY = ny - n.radius;
        if (ny + n.radius > maxY) maxY = ny + n.radius;
      }
      minX -= VIEWPORT_PADDING;
      maxX += VIEWPORT_PADDING;
      minY -= VIEWPORT_PADDING;
      maxY += VIEWPORT_PADDING;
      const w = maxX - minX,
        h = maxY - minY;
      const cx = (minX + maxX) / 2,
        cy = (minY + maxY) / 2;
      const fitScale = Math.min(width / w, height / h) * 0.9;
      const cs = Math.max(MIN_SCALE, Math.min(MAX_SCALE, fitScale));
      setViewTransform({
        x: width / 2 - cx * cs,
        y: height / 2 - cy * cs,
        scale: cs,
      });
    }

    setTickCount((t) => t + 1);

    sim
      .alpha(0.005)
      .alphaDecay(0.02)
      .on("tick", () => setTickCount((t) => t + 1))
      .restart();
    simulationRef.current = sim;
    return () => {
      sim.stop();
    };
  }, [simNodeData, simEdgeData]);

  // Re-fit on resize
  useEffect(() => {
    const nodes = simNodesRef.current;
    if (nodes.length === 0 || dimensions.width === 0 || dimensions.height === 0)
      return;
    let minX = Infinity,
      maxX = -Infinity,
      minY = Infinity,
      maxY = -Infinity;
    for (const n of nodes) {
      const nx = n.x ?? 0,
        ny = n.y ?? 0;
      if (nx - n.radius < minX) minX = nx - n.radius;
      if (nx + n.radius > maxX) maxX = nx + n.radius;
      if (ny - n.radius < minY) minY = ny - n.radius;
      if (ny + n.radius > maxY) maxY = ny + n.radius;
    }
    minX -= VIEWPORT_PADDING;
    maxX += VIEWPORT_PADDING;
    minY -= VIEWPORT_PADDING;
    maxY += VIEWPORT_PADDING;
    const w = maxX - minX,
      h = maxY - minY;
    const cx = (minX + maxX) / 2,
      cy = (minY + maxY) / 2;
    const fitScale =
      Math.min(dimensions.width / w, dimensions.height / h) * 0.9;
    const cs = Math.max(MIN_SCALE, Math.min(MAX_SCALE, fitScale));
    setViewTransform({
      x: dimensions.width / 2 - cx * cs,
      y: dimensions.height / 2 - cy * cs,
      scale: cs,
    });
  }, [dimensions.width, dimensions.height]);

  // ─── Interaction state ───
  const [isPanning, setIsPanning] = useState(false);
  const panStart = useRef<{
    x: number;
    y: number;
    tx: number;
    ty: number;
  } | null>(null);
  const draggedNodeId = useRef<string | null>(null);
  const [hoveredId, setHoveredId] = useState<string | null>(null);
  const [tooltipPos, setTooltipPos] = useState({ x: 0, y: 0 });

  // ─── Node drag ───
  const handleNodePointerDown = useCallback(
    (e: React.PointerEvent, nodeId: string) => {
      e.stopPropagation();
      e.preventDefault();
      draggedNodeId.current = nodeId;

      const startX = e.clientX;
      const startY = e.clientY;

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
        const node = draggedNodeId.current
          ? simNodeMapRef.current.get(draggedNodeId.current)
          : null;
        if (node) {
          node.fx = null;
          node.fy = null;
        }

        const dx = ev.clientX - startX;
        const dy = ev.clientY - startY;
        if (
          Math.abs(dx) < CLICK_THRESHOLD &&
          Math.abs(dy) < CLICK_THRESHOLD &&
          node
        ) {
          if (node.data.documentId) {
            onNodeClickRef.current?.(node.data.documentId, node.name);
          }
        }

        draggedNodeId.current = null;
        simulationRef.current?.alphaTarget(0);
        document.removeEventListener("pointermove", onMove);
        document.removeEventListener("pointerup", onUp);
      };

      document.addEventListener("pointermove", onMove);
      document.addEventListener("pointerup", onUp);
    },
    [],
  );

  // ─── Pan ───
  const handleSvgMouseDown = useCallback(
    (e: React.MouseEvent<SVGSVGElement>) => {
      if (e.button !== 0 || draggedNodeId.current) return;
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
      const rect = e.currentTarget.getBoundingClientRect();
      setTooltipPos({
        x: e.clientX - rect.left + 12,
        y: e.clientY - rect.top + 12,
      });
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

  // ─── Zoom ───
  const handleWheel = useCallback((e: React.WheelEvent<SVGSVGElement>) => {
    e.preventDefault();
    const rect = e.currentTarget.getBoundingClientRect();
    const cursorX = e.clientX - rect.left;
    const cursorY = e.clientY - rect.top;
    setViewTransform((prev) => {
      const delta = -e.deltaY * ZOOM_FACTOR;
      const newScale = Math.max(
        MIN_SCALE,
        Math.min(MAX_SCALE, prev.scale * (1 + delta)),
      );
      const ratio = newScale / prev.scale;
      return {
        x: cursorX - (cursorX - prev.x) * ratio,
        y: cursorY - (cursorY - prev.y) * ratio,
        scale: newScale,
      };
    });
  }, []);

  // ─── Hovered node ───
  const hoveredNode = useMemo(() => {
    if (!hoveredId) return null;
    return simNodeMapRef.current.get(hoveredId) ?? null;
  }, [hoveredId]);

  const hoveredConnectionCount = useMemo(() => {
    if (!hoveredId) return 0;
    return simEdgeData.filter(
      (e) => e.source === hoveredId || e.target === hoveredId,
    ).length;
  }, [hoveredId, simEdgeData]);

  // ─── Legend entries ───
  const legendEntries = useMemo(() => {
    const entries: Array<{ key: string; bg: string; border: string }> = [
      {
        key: "Source",
        bg: tokens.colorBrandBackground,
        border: tokens.colorBrandStroke1,
      },
      {
        key: "Semantic",
        bg: tokens.colorBrandBackground2,
        border: tokens.colorBrandStroke1,
      },
      {
        key: "Same Matter",
        bg: tokens.colorPaletteGreenBackground2,
        border: tokens.colorPaletteGreenBorder2,
      },
      {
        key: "Same Email",
        bg: tokens.colorPaletteYellowBackground2,
        border: tokens.colorPaletteYellowBorder2,
      },
    ];
    return entries;
  }, []);

  const simNodes = simNodesRef.current;
  const simLinks = simLinksRef.current;

  if (simNodeData.length === 0) {
    return (
      <div className={styles.container} ref={containerCallbackRef}>
        <div className={styles.centerMessage}>
          <Text size={400} weight="semibold">
            No documents to visualize
          </Text>
          <Text size={200}>
            Load a document with AI embeddings to see the network graph
          </Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.container} ref={containerCallbackRef}>
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
        aria-label={`Network graph showing ${simNodes.length} documents with ${simLinks.length} similarity connections`}
      >
        <g
          style={{
            transform: `translate(${viewTransform.x}px, ${viewTransform.y}px) scale(${viewTransform.scale})`,
            transformOrigin: "0 0",
          }}
        >
          {/* Edges */}
          {simLinks.map((link, i) => {
            const src = link.source as SimNode;
            const tgt = link.target as SimNode;
            if (typeof src === "string" || typeof tgt === "string") return null;
            const sx = src.x ?? 0,
              sy = src.y ?? 0;
            const tx = tgt.x ?? 0,
              ty = tgt.y ?? 0;
            const strokeWidth = 0.5 + link.similarity * 2.5;
            const baseOpacity = 0.08 + link.similarity * 0.27;
            const isConnected = hoveredId === src.id || hoveredId === tgt.id;
            const midX = (sx + tx) / 2,
              midY = (sy + ty) / 2;

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
                <text
                  x={midX}
                  y={midY}
                  dy="-4"
                  textAnchor="middle"
                  fontSize="9"
                  fill={tokens.colorNeutralForeground3}
                  style={{
                    opacity: hoveredId ? (isConnected ? 0.9 : 0.0) : 0.15,
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

          {/* Nodes */}
          {simNodes.map((node) => {
            const isHovered = hoveredId === node.id;
            return (
              <circle
                key={node.id}
                cx={node.x ?? 0}
                cy={node.y ?? 0}
                r={isHovered ? node.radius * 1.2 : node.radius}
                fill={node.color}
                stroke={node.borderColor}
                strokeWidth={isHovered ? 2.5 : 1}
                style={{
                  cursor:
                    draggedNodeId.current === node.id ? "grabbing" : "grab",
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

      {/* Legend */}
      <div className={styles.legend}>
        {legendEntries.map((entry) => (
          <div key={entry.key} className={styles.legendItem}>
            <svg width="16" height="16">
              <circle
                cx={8}
                cy={8}
                r={7}
                fill={entry.bg}
                stroke={entry.border}
                strokeWidth={1}
              />
            </svg>
            <span className={styles.legendLabel}>{entry.key}</span>
          </div>
        ))}
      </div>

      {/* Tooltip */}
      {hoveredNode && !draggedNodeId.current && (
        <div
          className={styles.tooltip}
          style={{ left: tooltipPos.x, top: tooltipPos.y }}
        >
          <div className={styles.tooltipName}>{hoveredNode.name}</div>
          {!hoveredNode.isSource && (
            <div className={styles.tooltipDetail}>
              Similarity: {Math.round(hoveredNode.similarity * 100)}%
            </div>
          )}
          {hoveredNode.data.documentType && (
            <div className={styles.tooltipDetail}>
              Type: {hoveredNode.data.documentType}
            </div>
          )}
          {hoveredNode.data.relationshipLabel && (
            <div className={styles.tooltipDetail}>
              Relationship: {hoveredNode.data.relationshipLabel}
            </div>
          )}
          {hoveredConnectionCount > 0 && (
            <div className={styles.tooltipDetail}>
              {hoveredConnectionCount} connection
              {hoveredConnectionCount !== 1 ? "s" : ""}
            </div>
          )}
          {hoveredNode.isSource && (
            <div
              className={styles.tooltipDetail}
              style={{ fontStyle: "italic" }}
            >
              Source document
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default RelationshipNetwork;
