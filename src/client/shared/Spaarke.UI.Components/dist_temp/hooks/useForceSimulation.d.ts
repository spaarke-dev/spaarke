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
    mode: 'hub-spoke' | 'peer-mesh';
    /** Layout center point (default `{ x: 0, y: 0 }`). */
    center?: {
        x: number;
        y: number;
    };
}
/**
 * Synchronously computes a d3-force layout and returns positioned nodes and edges.
 *
 * @param nodes  - Array of input nodes.
 * @param edges  - Array of input edges (source/target by node id).
 * @param options - Simulation configuration.
 * @returns Positioned nodes, edges with coordinates, and a viewport hint.
 */
export declare function useForceSimulation(nodes: ForceNode[], edges: ForceEdge[], options: ForceSimulationOptions): ForceLayoutResult;
export default useForceSimulation;
//# sourceMappingURL=useForceSimulation.d.ts.map