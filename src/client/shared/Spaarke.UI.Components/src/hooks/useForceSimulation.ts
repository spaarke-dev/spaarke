/**
 * useForceSimulation — Shared synchronous d3-force layout hook.
 *
 * Replaces async tick-by-tick simulation with synchronous pre-computation
 * via `sim.tick(N)`. Returns fully positioned nodes immediately with no
 * "isSimulating" state or layout spinner.
 *
 * Two layout modes:
 * - **hub-spoke**: First node (or node with `isSource: true`) pinned at center
 *   with `fx`/`fy`. All other nodes orbit around it.
 * - **peer-mesh**: All nodes are equal — no pinned center node.
 *
 * @example
 * ```tsx
 * const { nodes, edges, viewport } = useForceSimulation(rawNodes, rawEdges, {
 *   mode: "hub-spoke",
 *   center: { x: 0, y: 0 },
 * });
 * ```
 */

import { useMemo } from "react";
import {
  forceSimulation,
  forceLink,
  forceManyBody,
  forceCenter,
  forceCollide,
  type SimulationNodeDatum,
  type SimulationLinkDatum,
} from "d3-force";

// ─── Public interfaces ───────────────────────────────────────────────

/** Generic input node — extensible via index signature. */
export interface ForceNode {
  id: string;
  label?: string;
  isSource?: boolean;
  [key: string]: unknown;
}

/** Generic input edge — extensible via index signature. */
export interface ForceEdge {
  source: string;
  target: string;
  weight?: number;
  [key: string]: unknown;
}

/** A node after layout, guaranteed to have x/y coordinates. */
export interface PositionedNode extends ForceNode {
  x: number;
  y: number;
}

/** An edge after layout with resolved source/target node references. */
export interface PositionedEdge {
  source: string;
  target: string;
  sourceX: number;
  sourceY: number;
  targetX: number;
  targetY: number;
  weight: number;
  [key: string]: unknown;
}

/** Viewport hint computed from the bounding box of positioned nodes. */
export interface Viewport {
  x: number;
  y: number;
  zoom: number;
}

/** Complete layout result returned by the hook. */
export interface ForceLayoutResult {
  nodes: PositionedNode[];
  edges: PositionedEdge[];
  viewport: Viewport;
}

/** Configuration options for the force simulation. */
export interface ForceSimulationOptions {
  /** Number of synchronous ticks to run (default 300). */
  ticks?: number;
  /** Repulsive charge strength — negative values push nodes apart (default -800). */
  chargeStrength?: number;
  /** Multiplier for link rest-length; scaled by `1 - weight` (default 400). */
  linkDistanceMultiplier?: number;
  /** Minimum distance between node centres (default 60). */
  collisionRadius?: number;
  /** Layout mode. */
  mode: "hub-spoke" | "peer-mesh";
  /** Layout center point (default `{ x: 0, y: 0 }`). */
  center?: { x: number; y: number };
}

// ─── Internal d3 node / link wrappers ────────────────────────────────

interface SimNode extends SimulationNodeDatum {
  id: string;
  /** Index into the original `nodes` array so we can map back. */
  _srcIndex: number;
}

interface SimLink extends SimulationLinkDatum<SimNode> {
  source: string | SimNode;
  target: string | SimNode;
  weight: number;
  /** Index into the original `edges` array. */
  _srcIndex: number;
}

// ─── Defaults ────────────────────────────────────────────────────────

const DEFAULTS: Required<Omit<ForceSimulationOptions, "mode">> = {
  ticks: 300,
  chargeStrength: -800,
  linkDistanceMultiplier: 400,
  collisionRadius: 60,
  center: { x: 0, y: 0 },
};

// ─── Viewport helpers ────────────────────────────────────────────────

const VIEWPORT_PADDING = 80;

function computeViewport(
  positioned: PositionedNode[],
  cx: number,
  cy: number,
): Viewport {
  if (positioned.length === 0) {
    return { x: cx, y: cy, zoom: 1 };
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

  const spanX = maxX - minX + VIEWPORT_PADDING * 2;
  const spanY = maxY - minY + VIEWPORT_PADDING * 2;
  const span = Math.max(spanX, spanY, 1);

  // A zoom of 1 means the layout fits roughly in a 1000x1000 viewport.
  const zoom = Math.min(1, 1000 / span);

  return {
    x: (minX + maxX) / 2,
    y: (minY + maxY) / 2,
    zoom,
  };
}

// ─── Hook ────────────────────────────────────────────────────────────

/**
 * Synchronously computes a d3-force layout and returns positioned nodes and edges.
 *
 * @param nodes  - Array of input nodes.
 * @param edges  - Array of input edges (source/target by node id).
 * @param options - Simulation configuration.
 * @returns Positioned nodes, edges with coordinates, and a viewport hint.
 */
export function useForceSimulation(
  nodes: ForceNode[],
  edges: ForceEdge[],
  options: ForceSimulationOptions,
): ForceLayoutResult {
  const {
    ticks = DEFAULTS.ticks,
    chargeStrength = DEFAULTS.chargeStrength,
    linkDistanceMultiplier = DEFAULTS.linkDistanceMultiplier,
    collisionRadius = DEFAULTS.collisionRadius,
    center: centerOpt,
    mode,
  } = options;

  const cx = centerOpt?.x ?? DEFAULTS.center.x;
  const cy = centerOpt?.y ?? DEFAULTS.center.y;

  return useMemo(() => {
    // ── Empty input ──
    if (nodes.length === 0) {
      return { nodes: [], edges: [], viewport: { x: cx, y: cy, zoom: 1 } };
    }

    // ── Single node — place at center ──
    if (nodes.length === 1) {
      const positioned: PositionedNode[] = [{ ...nodes[0], x: cx, y: cy }];
      return {
        nodes: positioned,
        edges: [],
        viewport: { x: cx, y: cy, zoom: 1 },
      };
    }

    // ── Determine hub node for hub-spoke mode ──
    const isHubSpoke = mode === "hub-spoke";
    let hubIndex = 0;
    if (isHubSpoke) {
      const sourceIdx = nodes.findIndex((n) => n.isSource === true);
      hubIndex = sourceIdx >= 0 ? sourceIdx : 0;
    }

    // ── Build simulation nodes ──
    const nonHubCount = isHubSpoke ? nodes.length - 1 : nodes.length;
    let nonHubCounter = 0;

    const simNodes: SimNode[] = nodes.map((node, i) => {
      if (isHubSpoke && i === hubIndex) {
        // Pin hub at center
        return { id: node.id, _srcIndex: i, x: cx, y: cy, fx: cx, fy: cy };
      }
      // Spread non-hub nodes in a circle for stable initial positions
      const angle =
        (2 * Math.PI * nonHubCounter) / Math.max(nonHubCount, 1) - Math.PI / 2;
      nonHubCounter++;
      const radius = 150;
      return {
        id: node.id,
        _srcIndex: i,
        x: cx + radius * Math.cos(angle),
        y: cy + radius * Math.sin(angle),
      };
    });

    // ── Build simulation links (only for edges whose nodes exist) ──
    const nodeIdSet = new Set(nodes.map((n) => n.id));
    const simLinks: SimLink[] = edges
      .map((edge, i) => ({
        source: edge.source,
        target: edge.target,
        weight: edge.weight ?? 0.5,
        _srcIndex: i,
      }))
      .filter(
        (link) =>
          nodeIdSet.has(link.source as string) &&
          nodeIdSet.has(link.target as string),
      );

    // ── Create and run simulation synchronously ──
    const simulation = forceSimulation<SimNode>(simNodes)
      .force(
        "link",
        forceLink<SimNode, SimLink>(simLinks)
          .id((d) => d.id)
          .distance((link) => linkDistanceMultiplier * (1 - link.weight))
          .strength(0.5),
      )
      .force("charge", forceManyBody<SimNode>().strength(chargeStrength))
      .force("center", forceCenter<SimNode>(cx, cy))
      .force(
        "collide",
        forceCollide<SimNode>().radius(collisionRadius).strength(0.7),
      )
      .alphaDecay(0.05)
      .velocityDecay(0.3)
      .stop();

    // Synchronous pre-computation — no async ticks, no spinner
    simulation.tick(ticks);

    // ── Map back to positioned nodes ──
    const nodeMap = new Map<string, SimNode>();
    for (const sn of simNodes) {
      nodeMap.set(sn.id, sn);
    }

    const positionedNodes: PositionedNode[] = simNodes.map((sn) => ({
      ...nodes[sn._srcIndex],
      x: sn.x ?? cx,
      y: sn.y ?? cy,
    }));

    // ── Map back to positioned edges ──
    const positionedEdges: PositionedEdge[] = simLinks.map((sl) => {
      const srcNode =
        typeof sl.source === "object" ? sl.source : nodeMap.get(sl.source);
      const tgtNode =
        typeof sl.target === "object" ? sl.target : nodeMap.get(sl.target);
      const originalEdge = edges[sl._srcIndex];
      // Copy extensible properties, overwriting source/target with string ids
      const { source: _s, target: _t, weight: _w, ...rest } = originalEdge;
      return {
        ...rest,
        source:
          nodes[srcNode?._srcIndex ?? 0]?.id ?? (originalEdge.source as string),
        target:
          nodes[tgtNode?._srcIndex ?? 0]?.id ?? (originalEdge.target as string),
        sourceX: srcNode?.x ?? cx,
        sourceY: srcNode?.y ?? cy,
        targetX: tgtNode?.x ?? cx,
        targetY: tgtNode?.y ?? cy,
        weight: sl.weight,
      };
    });

    const viewport = computeViewport(positionedNodes, cx, cy);

    return { nodes: positionedNodes, edges: positionedEdges, viewport };
  }, [
    nodes,
    edges,
    mode,
    ticks,
    chargeStrength,
    linkDistanceMultiplier,
    collisionRadius,
    cx,
    cy,
  ]);
}

export default useForceSimulation;
