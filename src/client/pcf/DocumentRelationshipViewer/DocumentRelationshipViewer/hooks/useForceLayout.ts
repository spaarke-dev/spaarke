/**
 * useForceLayout - Custom hook for d3-force based graph layout
 *
 * This hook calculates node positions using d3-force simulation
 * where edge length encodes similarity (shorter = more similar).
 *
 * Constraint: Edge distance = 200 * (1 - similarity)
 * Example: similarity=0.8 → distance=40, similarity=0.5 → distance=100
 */

import * as React from "react";
import {
    forceSimulation,
    forceLink,
    forceManyBody,
    forceCenter,
    forceCollide,
    Simulation,
    SimulationNodeDatum,
    SimulationLinkDatum,
} from "d3-force";
import type {
    DocumentNode,
    DocumentEdge,
    ForceLayoutOptions,
} from "../types/graph";

// Default layout options
const DEFAULT_OPTIONS: Required<ForceLayoutOptions> = {
    distanceMultiplier: 200,
    collisionRadius: 50,
    centerX: 0,
    centerY: 0,
    chargeStrength: -300,
};

/**
 * Internal node type for d3-force simulation
 */
interface ForceNode extends SimulationNodeDatum {
    id: string;
    isSource?: boolean;
}

/**
 * Internal link type for d3-force simulation
 */
interface ForceLink extends SimulationLinkDatum<ForceNode> {
    source: string | ForceNode;
    target: string | ForceNode;
    similarity: number;
}

/**
 * Hook result type
 */
export interface UseForceLayoutResult {
    /** Nodes with calculated positions */
    layoutNodes: DocumentNode[];
    /** Edges (unchanged) */
    layoutEdges: DocumentEdge[];
    /** Whether simulation is running */
    isSimulating: boolean;
    /** Manually trigger layout recalculation */
    recalculate: () => void;
}

/**
 * Calculate node positions using d3-force simulation.
 *
 * @param nodes - Input nodes from API
 * @param edges - Input edges from API
 * @param options - Layout configuration options
 * @returns Nodes with positions, edges, and simulation state
 */
export function useForceLayout(
    nodes: DocumentNode[],
    edges: DocumentEdge[],
    options?: ForceLayoutOptions
): UseForceLayoutResult {
    const opts = { ...DEFAULT_OPTIONS, ...options };
    const [layoutNodes, setLayoutNodes] = React.useState<DocumentNode[]>(nodes);
    const [isSimulating, setIsSimulating] = React.useState(false);
    const simulationRef = React.useRef<Simulation<ForceNode, ForceLink> | null>(null);

    /**
     * Run force simulation and update node positions
     */
    const runSimulation = React.useCallback(() => {
        if (nodes.length === 0) {
            setLayoutNodes([]);
            return;
        }

        setIsSimulating(true);

        // Create force nodes from input
        const forceNodes: ForceNode[] = nodes.map((node) => ({
            id: node.id,
            isSource: node.data.isSource,
            // Initialize source node at center, others randomly
            x: node.data.isSource ? opts.centerX : undefined,
            y: node.data.isSource ? opts.centerY : undefined,
            // Fix source node at center
            fx: node.data.isSource ? opts.centerX : null,
            fy: node.data.isSource ? opts.centerY : null,
        }));

        // Create force links with distance based on similarity
        // Distance = distanceMultiplier * (1 - similarity)
        // Higher similarity = shorter distance
        const forceLinks: ForceLink[] = edges.map((edge) => ({
            source: edge.source,
            target: edge.target,
            similarity: edge.data?.similarity ?? 0.5,
        }));

        // Stop existing simulation
        if (simulationRef.current) {
            simulationRef.current.stop();
        }

        // Create new simulation
        const simulation = forceSimulation<ForceNode, ForceLink>(forceNodes)
            // Link force: edge length based on similarity
            .force(
                "link",
                forceLink<ForceNode, ForceLink>(forceLinks)
                    .id((d) => d.id)
                    .distance((link) => {
                        // Edge distance = 200 * (1 - similarity)
                        // similarity 0.8 → distance 40
                        // similarity 0.5 → distance 100
                        return opts.distanceMultiplier * (1 - link.similarity);
                    })
                    .strength(0.5)
            )
            // Charge force: nodes repel each other
            .force("charge", forceManyBody<ForceNode>().strength(opts.chargeStrength))
            // Center force: pull toward center
            .force("center", forceCenter<ForceNode>(opts.centerX, opts.centerY))
            // Collision force: prevent node overlap
            .force(
                "collide",
                forceCollide<ForceNode>().radius(opts.collisionRadius).strength(0.7)
            )
            // Faster convergence
            .alphaDecay(0.05)
            .velocityDecay(0.3);

        simulationRef.current = simulation;

        // Run simulation to completion
        simulation.on("tick", () => {
            // Update node positions during simulation
            const updatedNodes = nodes.map((node) => {
                const forceNode = forceNodes.find((fn) => fn.id === node.id);
                return {
                    ...node,
                    position: {
                        x: forceNode?.x ?? 0,
                        y: forceNode?.y ?? 0,
                    },
                };
            });
            setLayoutNodes(updatedNodes);
        });

        simulation.on("end", () => {
            setIsSimulating(false);
        });

        // Let simulation run for sufficient iterations
        simulation.alpha(1).restart();
    }, [nodes, edges, opts.distanceMultiplier, opts.collisionRadius, opts.centerX, opts.centerY, opts.chargeStrength]);

    // Run simulation when nodes/edges change
    React.useEffect(() => {
        runSimulation();

        return () => {
            if (simulationRef.current) {
                simulationRef.current.stop();
            }
        };
    }, [runSimulation]);

    return {
        layoutNodes,
        layoutEdges: edges,
        isSimulating,
        recalculate: runSimulation,
    };
}

export default useForceLayout;
