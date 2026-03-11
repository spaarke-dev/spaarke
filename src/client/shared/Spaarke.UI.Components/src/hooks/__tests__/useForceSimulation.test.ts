/**
 * useForceSimulation Hook — Comprehensive Unit Tests
 *
 * Validates synchronous d3-force pre-computation in both hub-spoke and
 * peer-mesh modes. Target: 90%+ coverage.
 */

import { renderHook } from "@testing-library/react";
import {
    useForceSimulation,
    type ForceNode,
    type ForceEdge,
    type ForceSimulationOptions,
} from "../useForceSimulation";

// ─── Helpers ─────────────────────────────────────────────────────────

function makeNodes(count: number, sourceIndex?: number): ForceNode[] {
    return Array.from({ length: count }, (_, i) => ({
        id: `node-${i}`,
        label: `Node ${i}`,
        isSource: sourceIndex === i ? true : undefined,
    }));
}

function makeEdges(pairs: [number, number][], weight = 0.5): ForceEdge[] {
    return pairs.map(([s, t]) => ({
        source: `node-${s}`,
        target: `node-${t}`,
        weight,
    }));
}

function runHook(nodes: ForceNode[], edges: ForceEdge[], options: ForceSimulationOptions) {
    return renderHook(() => useForceSimulation(nodes, edges, options));
}

// ─── Tests ───────────────────────────────────────────────────────────

describe("useForceSimulation", () => {
    // ── Empty input ──────────────────────────────────────────────────

    describe("zero nodes", () => {
        it("returns empty arrays and default viewport", () => {
            const { result } = runHook([], [], { mode: "hub-spoke" });

            expect(result.current.nodes).toEqual([]);
            expect(result.current.edges).toEqual([]);
            expect(result.current.viewport).toEqual({ x: 0, y: 0, zoom: 1 });
        });

        it("respects custom center for empty input", () => {
            const { result } = runHook([], [], {
                mode: "peer-mesh",
                center: { x: 100, y: 200 },
            });

            expect(result.current.viewport).toEqual({ x: 100, y: 200, zoom: 1 });
        });
    });

    // ── Single node ──────────────────────────────────────────────────

    describe("single node", () => {
        it("positions the node at center in hub-spoke mode", () => {
            const nodes = makeNodes(1);
            const { result } = runHook(nodes, [], { mode: "hub-spoke" });

            expect(result.current.nodes).toHaveLength(1);
            expect(result.current.nodes[0].x).toBe(0);
            expect(result.current.nodes[0].y).toBe(0);
            expect(result.current.nodes[0].id).toBe("node-0");
        });

        it("positions the node at custom center in peer-mesh mode", () => {
            const nodes = makeNodes(1);
            const { result } = runHook(nodes, [], {
                mode: "peer-mesh",
                center: { x: 50, y: -50 },
            });

            expect(result.current.nodes[0].x).toBe(50);
            expect(result.current.nodes[0].y).toBe(-50);
        });

        it("returns empty edges for single node", () => {
            const nodes = makeNodes(1);
            const { result } = runHook(nodes, [], { mode: "hub-spoke" });

            expect(result.current.edges).toEqual([]);
        });

        it("returns zoom 1 for single node", () => {
            const nodes = makeNodes(1);
            const { result } = runHook(nodes, [], { mode: "hub-spoke" });

            expect(result.current.viewport.zoom).toBe(1);
        });
    });

    // ── Hub-spoke mode ───────────────────────────────────────────────

    describe("hub-spoke mode", () => {
        it("pins the first node (source) at center", () => {
            const nodes = makeNodes(5, 0);
            const edges = makeEdges([
                [0, 1],
                [0, 2],
                [0, 3],
                [0, 4],
            ]);
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            const hub = result.current.nodes.find((n) => n.id === "node-0")!;
            expect(hub.x).toBe(0);
            expect(hub.y).toBe(0);
        });

        it("pins explicitly marked isSource node at center", () => {
            const nodes: ForceNode[] = [
                { id: "a", label: "A" },
                { id: "b", label: "B", isSource: true },
                { id: "c", label: "C" },
            ];
            const edges: ForceEdge[] = [
                { source: "b", target: "a", weight: 0.7 },
                { source: "b", target: "c", weight: 0.5 },
            ];
            const { result } = runHook(nodes, edges, {
                mode: "hub-spoke",
                center: { x: 100, y: 100 },
            });

            const hub = result.current.nodes.find((n) => n.id === "b")!;
            expect(hub.x).toBe(100);
            expect(hub.y).toBe(100);
        });

        it("positions non-hub nodes away from center", () => {
            const nodes = makeNodes(4, 0);
            const edges = makeEdges([
                [0, 1],
                [0, 2],
                [0, 3],
            ]);
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            for (let i = 1; i < 4; i++) {
                const n = result.current.nodes.find((nd) => nd.id === `node-${i}`)!;
                expect(typeof n.x).toBe("number");
                expect(typeof n.y).toBe("number");
                // Non-hub nodes should not be exactly at the center
                const distFromCenter = Math.sqrt(n.x * n.x + n.y * n.y);
                expect(distFromCenter).toBeGreaterThan(1);
            }
        });

        it("uses first node as hub when no isSource is set", () => {
            const nodes: ForceNode[] = [
                { id: "x" },
                { id: "y" },
                { id: "z" },
            ];
            const edges: ForceEdge[] = [
                { source: "x", target: "y" },
                { source: "x", target: "z" },
            ];
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            const hub = result.current.nodes.find((n) => n.id === "x")!;
            expect(hub.x).toBe(0);
            expect(hub.y).toBe(0);
        });

        it("returns positioned edges with coordinates", () => {
            const nodes = makeNodes(3, 0);
            const edges = makeEdges([
                [0, 1],
                [0, 2],
            ]);
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            expect(result.current.edges).toHaveLength(2);
            for (const e of result.current.edges) {
                expect(typeof e.sourceX).toBe("number");
                expect(typeof e.sourceY).toBe("number");
                expect(typeof e.targetX).toBe("number");
                expect(typeof e.targetY).toBe("number");
                expect(typeof e.weight).toBe("number");
            }
        });
    });

    // ── Peer-mesh mode ───────────────────────────────────────────────

    describe("peer-mesh mode", () => {
        it("does not pin any node at center", () => {
            const nodes = makeNodes(4);
            const edges = makeEdges([
                [0, 1],
                [1, 2],
                [2, 3],
                [3, 0],
            ]);
            const { result } = runHook(nodes, edges, { mode: "peer-mesh" });

            // In peer-mesh, no node should be fixed — all are free to move.
            // We just verify all nodes have numeric coordinates.
            for (const n of result.current.nodes) {
                expect(typeof n.x).toBe("number");
                expect(typeof n.y).toBe("number");
                expect(isFinite(n.x)).toBe(true);
                expect(isFinite(n.y)).toBe(true);
            }
        });

        it("spreads nodes with no edges apart via charge repulsion", () => {
            const nodes = makeNodes(3);
            const { result } = runHook(nodes, [], { mode: "peer-mesh" });

            // With only charge force, nodes should be spread apart
            const positions = result.current.nodes.map((n) => ({ x: n.x, y: n.y }));
            // At least two nodes should not be at the exact same position
            const uniquePositions = new Set(positions.map((p) => `${Math.round(p.x)},${Math.round(p.y)}`));
            expect(uniquePositions.size).toBeGreaterThan(1);
        });
    });

    // ── Nodes without edges ──────────────────────────────────────────

    describe("nodes without edges", () => {
        it("positions all nodes with coordinates even without edges", () => {
            const nodes = makeNodes(5);
            const { result } = runHook(nodes, [], { mode: "hub-spoke" });

            expect(result.current.nodes).toHaveLength(5);
            for (const n of result.current.nodes) {
                expect(typeof n.x).toBe("number");
                expect(typeof n.y).toBe("number");
            }
            expect(result.current.edges).toEqual([]);
        });

        it("handles peer-mesh with no edges", () => {
            const nodes = makeNodes(3);
            const { result } = runHook(nodes, [], { mode: "peer-mesh" });

            expect(result.current.nodes).toHaveLength(3);
            expect(result.current.edges).toEqual([]);
        });
    });

    // ── Custom options ───────────────────────────────────────────────

    describe("custom options", () => {
        it("respects custom tick count", () => {
            const nodes = makeNodes(3, 0);
            const edges = makeEdges([[0, 1], [0, 2]]);

            // Very few ticks — should still produce positioned nodes
            const { result } = runHook(nodes, edges, { mode: "hub-spoke", ticks: 5 });
            expect(result.current.nodes).toHaveLength(3);
            for (const n of result.current.nodes) {
                expect(typeof n.x).toBe("number");
            }
        });

        it("respects custom charge strength", () => {
            const nodes = makeNodes(3, 0);
            const edges = makeEdges([[0, 1], [0, 2]]);

            const { result: weak } = runHook(nodes, edges, {
                mode: "hub-spoke",
                chargeStrength: -50,
            });
            const { result: strong } = runHook(nodes, edges, {
                mode: "hub-spoke",
                chargeStrength: -5000,
            });

            // With stronger charge, nodes should be further apart on average
            const avgDistWeak = weak.current.nodes
                .filter((n) => n.id !== "node-0")
                .reduce((sum, n) => sum + Math.sqrt(n.x * n.x + n.y * n.y), 0);
            const avgDistStrong = strong.current.nodes
                .filter((n) => n.id !== "node-0")
                .reduce((sum, n) => sum + Math.sqrt(n.x * n.x + n.y * n.y), 0);

            expect(avgDistStrong).toBeGreaterThan(avgDistWeak);
        });

        it("respects custom center", () => {
            const nodes = makeNodes(3, 0);
            const edges = makeEdges([[0, 1], [0, 2]]);
            const center = { x: 500, y: 300 };

            const { result } = runHook(nodes, edges, { mode: "hub-spoke", center });

            const hub = result.current.nodes.find((n) => n.id === "node-0")!;
            expect(hub.x).toBe(500);
            expect(hub.y).toBe(300);
        });
    });

    // ── Viewport computation ─────────────────────────────────────────

    describe("viewport", () => {
        it("returns a viewport with x, y, and zoom", () => {
            const nodes = makeNodes(5, 0);
            const edges = makeEdges([
                [0, 1],
                [0, 2],
                [0, 3],
                [0, 4],
            ]);
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            expect(typeof result.current.viewport.x).toBe("number");
            expect(typeof result.current.viewport.y).toBe("number");
            expect(typeof result.current.viewport.zoom).toBe("number");
            expect(result.current.viewport.zoom).toBeGreaterThan(0);
            expect(result.current.viewport.zoom).toBeLessThanOrEqual(1);
        });
    });

    // ── Edge cases ───────────────────────────────────────────────────

    describe("edge cases", () => {
        it("filters out edges referencing non-existent nodes", () => {
            const nodes = makeNodes(2, 0);
            const edges: ForceEdge[] = [
                { source: "node-0", target: "node-1", weight: 0.8 },
                { source: "node-0", target: "non-existent", weight: 0.5 },
            ];
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            // Only the valid edge should appear
            expect(result.current.edges).toHaveLength(1);
            expect(result.current.edges[0].source).toBe("node-0");
            expect(result.current.edges[0].target).toBe("node-1");
        });

        it("handles edges with no weight (defaults to 0.5)", () => {
            const nodes = makeNodes(2, 0);
            const edges: ForceEdge[] = [{ source: "node-0", target: "node-1" }];
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            expect(result.current.edges).toHaveLength(1);
            expect(result.current.edges[0].weight).toBe(0.5);
        });

        it("preserves extensible properties on nodes", () => {
            const nodes: ForceNode[] = [
                { id: "a", label: "A", customProp: "hello" },
                { id: "b", label: "B", customProp: "world" },
            ];
            const { result } = runHook(nodes, [], { mode: "peer-mesh" });

            expect(result.current.nodes[0].customProp).toBe("hello");
            expect(result.current.nodes[1].customProp).toBe("world");
        });

        it("preserves extensible properties on edges", () => {
            const nodes: ForceNode[] = [
                { id: "a", isSource: true },
                { id: "b" },
            ];
            const edges: ForceEdge[] = [
                { source: "a", target: "b", weight: 0.9, customEdgeProp: 42 },
            ];
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            expect(result.current.edges[0].customEdgeProp).toBe(42);
        });

        it("handles two nodes with no edges", () => {
            const nodes = makeNodes(2);
            const { result } = runHook(nodes, [], { mode: "peer-mesh" });

            expect(result.current.nodes).toHaveLength(2);
            expect(result.current.edges).toEqual([]);
            // Both should have valid positions
            for (const n of result.current.nodes) {
                expect(isFinite(n.x)).toBe(true);
                expect(isFinite(n.y)).toBe(true);
            }
        });

        it("handles large graph (20 nodes, 30 edges)", () => {
            const nodes = makeNodes(20, 0);
            const edgePairs: [number, number][] = [];
            for (let i = 1; i < 20; i++) {
                edgePairs.push([0, i]);
            }
            // Add some cross-links
            for (let i = 1; i < 12; i++) {
                edgePairs.push([i, i + 1]);
            }
            const edges = makeEdges(edgePairs, 0.6);

            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            expect(result.current.nodes).toHaveLength(20);
            expect(result.current.edges).toHaveLength(edgePairs.length);
            // Hub pinned at center
            const hub = result.current.nodes.find((n) => n.id === "node-0")!;
            expect(hub.x).toBe(0);
            expect(hub.y).toBe(0);
        });
    });

    // ── Synchronous behavior ─────────────────────────────────────────

    describe("synchronous behavior", () => {
        it("returns result immediately (no isSimulating state)", () => {
            const nodes = makeNodes(5, 0);
            const edges = makeEdges([
                [0, 1],
                [0, 2],
                [0, 3],
                [0, 4],
            ]);
            const { result } = runHook(nodes, edges, { mode: "hub-spoke" });

            // Result is available synchronously — no "isSimulating" property
            expect(result.current).toHaveProperty("nodes");
            expect(result.current).toHaveProperty("edges");
            expect(result.current).toHaveProperty("viewport");
            expect(result.current).not.toHaveProperty("isSimulating");
        });
    });

    // ── Memoization ──────────────────────────────────────────────────

    describe("memoization", () => {
        it("returns the same reference for identical inputs", () => {
            const nodes = makeNodes(3, 0);
            const edges = makeEdges([[0, 1], [0, 2]]);
            const options: ForceSimulationOptions = { mode: "hub-spoke" };

            const { result, rerender } = renderHook(() =>
                useForceSimulation(nodes, edges, options)
            );

            const firstResult = result.current;
            rerender();
            const secondResult = result.current;

            // Same input references => same memoized output reference
            expect(firstResult).toBe(secondResult);
        });
    });
});
