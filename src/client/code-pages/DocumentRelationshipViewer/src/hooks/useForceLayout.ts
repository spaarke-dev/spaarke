/**
 * useForceLayout — Code Page version
 * Identical d3-force layout logic; framework-agnostic.
 */

import { useState, useRef, useCallback, useEffect } from "react";
import {
    forceSimulation,
    forceLink,
    forceManyBody,
    forceCenter,
    forceCollide,
    type Simulation,
    type SimulationNodeDatum,
    type SimulationLinkDatum,
} from "d3-force";
import type { DocumentNode, DocumentEdge, ForceLayoutOptions } from "../types/graph";

const DEFAULT_OPTIONS: Required<ForceLayoutOptions> = {
    distanceMultiplier: 400,
    collisionRadius: 100,
    centerX: 0,
    centerY: 0,
    chargeStrength: -1000,
};

interface ForceNode extends SimulationNodeDatum {
    id: string;
    isSource?: boolean;
}

interface ForceLink extends SimulationLinkDatum<ForceNode> {
    source: string | ForceNode;
    target: string | ForceNode;
    similarity: number;
}

export interface UseForceLayoutResult {
    layoutNodes: DocumentNode[];
    layoutEdges: DocumentEdge[];
    isSimulating: boolean;
    recalculate: () => void;
}

export function useForceLayout(
    nodes: DocumentNode[],
    edges: DocumentEdge[],
    options?: ForceLayoutOptions
): UseForceLayoutResult {
    const opts = { ...DEFAULT_OPTIONS, ...options };
    const [layoutNodes, setLayoutNodes] = useState<DocumentNode[]>(nodes);
    const [isSimulating, setIsSimulating] = useState(false);
    const simulationRef = useRef<Simulation<ForceNode, ForceLink> | null>(null);

    const runSimulation = useCallback(() => {
        if (nodes.length === 0) {
            setLayoutNodes([]);
            return;
        }

        setIsSimulating(true);

        const nonSourceNodes = nodes.filter((n) => !n.data.isSource);
        const numNonSource = nonSourceNodes.length;

        const forceNodes: ForceNode[] = nodes.map((node) => {
            if (node.data.isSource) {
                return { id: node.id, isSource: true, x: opts.centerX, y: opts.centerY, fx: opts.centerX, fy: opts.centerY };
            }
            const nodeIndex = nonSourceNodes.findIndex((n) => n.id === node.id);
            const angle = (2 * Math.PI * nodeIndex) / numNonSource - Math.PI / 2;
            const radius = 150;
            return {
                id: node.id,
                isSource: false,
                x: opts.centerX + radius * Math.cos(angle),
                y: opts.centerY + radius * Math.sin(angle),
                fx: null,
                fy: null,
            };
        });

        const forceLinks: ForceLink[] = edges.map((edge) => ({
            source: edge.source,
            target: edge.target,
            similarity: edge.data?.similarity ?? 0.5,
        }));

        if (simulationRef.current) simulationRef.current.stop();

        const simulation = forceSimulation<ForceNode, ForceLink>(forceNodes)
            .force("link", forceLink<ForceNode, ForceLink>(forceLinks)
                .id((d) => d.id)
                .distance((link) => opts.distanceMultiplier * (1 - link.similarity))
                .strength(0.5))
            .force("charge", forceManyBody<ForceNode>().strength(opts.chargeStrength))
            .force("center", forceCenter<ForceNode>(opts.centerX, opts.centerY))
            .force("collide", forceCollide<ForceNode>().radius(opts.collisionRadius).strength(0.7))
            .alphaDecay(0.05)
            .velocityDecay(0.3);

        simulationRef.current = simulation;

        simulation.on("tick", () => {
            const updatedNodes = nodes.map((node) => {
                const forceNode = forceNodes.find((fn) => fn.id === node.id);
                return { ...node, position: { x: forceNode?.x ?? 0, y: forceNode?.y ?? 0 } };
            });
            setLayoutNodes(updatedNodes);
        });

        simulation.on("end", () => setIsSimulating(false));
        simulation.alpha(1).restart();
    }, [nodes, edges, opts.distanceMultiplier, opts.collisionRadius, opts.centerX, opts.centerY, opts.chargeStrength]);

    useEffect(() => {
        runSimulation();
        return () => { if (simulationRef.current) simulationRef.current.stop(); };
    }, [runSimulation]);

    return { layoutNodes, layoutEdges: edges, isSimulating, recalculate: runSimulation };
}

export default useForceLayout;
