/**
 * Unit tests for useClusterLayout hook -- d3-force cluster graph layout.
 *
 * Tests:
 * - Empty results -> empty nodes/edges
 * - Clustering by DocumentType, Organization, MatterType
 * - Cluster node data shape (clusterKey, label, recordCount, avgSimilarity)
 * - Expanded cluster adds record nodes with edges
 * - Collapsed clusters do not add record nodes
 * - Inter-cluster edges based on shared attributes
 * - Top 100 results cap (MAX_GRAPH_RESULTS)
 * - Results sorted by score before clustering
 * - Node positions are assigned by d3-force simulation
 * - Mixed document and record results
 *
 * @see useClusterLayout.ts
 */

import { renderHook } from "@testing-library/react";
import type {
    DocumentSearchResult,
    RecordSearchResult,
    GraphClusterBy,
    ClusterNodeData,
    RecordNodeData,
} from "../../types";

// We do NOT mock d3-force -- the hook runs a synchronous simulation,
// and we want to verify node positions are non-zero after simulation.

import { useClusterLayout } from "../../hooks/useClusterLayout";

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeDocResult(
    id: string,
    opts: Partial<DocumentSearchResult> = {},
): DocumentSearchResult {
    return {
        documentId: id,
        name: `Doc ${id}`,
        combinedScore: 0.85,
        documentType: "Contract",
        fileType: "pdf",
        parentEntityType: "matter",
        parentEntityId: "entity-1",
        parentEntityName: "Acme Corp",
        ...opts,
    };
}

function makeRecordResult(
    id: string,
    type = "sprk_matter",
    opts: Partial<RecordSearchResult> = {},
): RecordSearchResult {
    return {
        recordId: id,
        recordType: type,
        recordName: `Record ${id}`,
        confidenceScore: 0.8,
        organizations: ["Acme Corp"],
        people: ["Jane Doe"],
        ...opts,
    };
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useClusterLayout", () => {
    // --- Empty results ---

    describe("empty results", () => {
        it("should return empty nodes and edges for empty results array", () => {
            const { result } = renderHook(() =>
                useClusterLayout([], "DocumentType", null),
            );

            expect(result.current.nodes).toEqual([]);
            expect(result.current.edges).toEqual([]);
            expect(result.current.isSimulating).toBe(false);
        });
    });

    // --- Cluster grouping ---

    describe("cluster grouping", () => {
        it("should group document results by DocumentType", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Agreement" }),
                makeDocResult("d3", { documentType: "Contract" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            // Should have 2 cluster nodes (Contract, Agreement)
            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(2);

            const clusterKeys = clusterNodes.map(
                (n) => (n.data as unknown as ClusterNodeData).clusterKey,
            );
            expect(clusterKeys).toContain("Contract");
            expect(clusterKeys).toContain("Agreement");
        });

        it("should group document results by Organization (parentEntityName)", () => {
            const results = [
                makeDocResult("d1", { parentEntityName: "Acme Corp" }),
                makeDocResult("d2", { parentEntityName: "Globex" }),
                makeDocResult("d3", { parentEntityName: "Acme Corp" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", null),
            );

            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(2);
        });

        it("should group record results by Organization", () => {
            const results = [
                makeRecordResult("r1", "sprk_matter", {
                    organizations: ["Acme Corp"],
                }),
                makeRecordResult("r2", "sprk_matter", {
                    organizations: ["Globex"],
                }),
                makeRecordResult("r3", "sprk_project", {
                    organizations: ["Acme Corp"],
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", null),
            );

            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(2);
        });

        it("should group record results by PersonContact", () => {
            const results = [
                makeRecordResult("r1", "sprk_matter", { people: ["Alice"] }),
                makeRecordResult("r2", "sprk_matter", { people: ["Bob"] }),
                makeRecordResult("r3", "sprk_project", { people: ["Alice"] }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "PersonContact", null),
            );

            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(2);
        });

        it("should use 'Uncategorized' for documents when clusterBy does not apply", () => {
            const results = [
                makeDocResult("d1"),
                makeDocResult("d2"),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "PracticeArea", null),
            );

            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            // All results should fall into "Uncategorized"
            expect(clusterNodes).toHaveLength(1);
            const data = clusterNodes[0].data as unknown as ClusterNodeData;
            expect(data.clusterKey).toBe("Uncategorized");
        });

        it("should use 'Uncategorized' for records when clusterBy is DocumentType", () => {
            const results = [
                makeRecordResult("r1", "sprk_matter"),
                makeRecordResult("r2", "sprk_project"),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(1);
            const data = clusterNodes[0].data as unknown as ClusterNodeData;
            expect(data.clusterKey).toBe("Uncategorized");
        });
    });

    // --- Cluster node data ---

    describe("cluster node data", () => {
        it("should include recordCount equal to number of results in cluster", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Contract" }),
                makeDocResult("d3", { documentType: "Contract" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.type === "clusterNode",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.recordCount).toBe(3);
        });

        it("should compute avgSimilarity from results in cluster", () => {
            const results = [
                makeDocResult("d1", { combinedScore: 0.9 }),
                makeDocResult("d2", { combinedScore: 0.7 }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.type === "clusterNode",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.avgSimilarity).toBeCloseTo(0.8, 1);
        });

        it("should include topResults with up to 3 names", () => {
            const results = [
                makeDocResult("d1", { name: "Alpha" }),
                makeDocResult("d2", { name: "Beta" }),
                makeDocResult("d3", { name: "Gamma" }),
                makeDocResult("d4", { name: "Delta" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.type === "clusterNode",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.topResults).toHaveLength(3);
        });

        it("should set isExpanded false when cluster is not expanded", () => {
            const results = [makeDocResult("d1")];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.type === "clusterNode",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.isExpanded).toBe(false);
        });

        it("should set category to the clusterBy parameter", () => {
            const results = [makeDocResult("d1")];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", null),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.type === "clusterNode",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.category).toBe("Organization");
        });
    });

    // --- Expanded cluster ---

    describe("expanded cluster", () => {
        it("should add record nodes when a cluster is expanded", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Contract" }),
            ];
            const expandedClusterId = "cluster-Contract";

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", expandedClusterId),
            );

            const recordNodes = result.current.nodes.filter(
                (n) => n.type === "recordNode",
            );
            expect(recordNodes).toHaveLength(2);
        });

        it("should create edges from cluster to each record node", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Contract" }),
            ];
            const expandedClusterId = "cluster-Contract";

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", expandedClusterId),
            );

            const clusterToRecordEdges = result.current.edges.filter(
                (e) => e.source === "cluster-Contract" && e.target.startsWith("record-"),
            );
            expect(clusterToRecordEdges).toHaveLength(2);
        });

        it("should populate record node data correctly for documents", () => {
            const results = [
                makeDocResult("d1", {
                    documentType: "Contract",
                    name: "Employment Contract",
                    combinedScore: 0.92,
                    parentEntityName: "Acme Corp",
                }),
            ];
            const expandedClusterId = "cluster-Contract";

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", expandedClusterId),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.recordId).toBe("d1");
            expect(data.recordName).toBe("Employment Contract");
            expect(data.similarity).toBe(0.92);
            expect(data.parentEntityName).toBe("Acme Corp");
            expect(data.domain).toBe("documents");
            expect(data.recordType).toBe("sprk_document");
        });

        it("should populate record node data correctly for records", () => {
            const results = [
                makeRecordResult("r1", "sprk_matter", {
                    recordName: "Employment Matter",
                    confidenceScore: 0.88,
                    organizations: ["Acme Corp"],
                }),
            ];
            const expandedClusterId = "cluster-Acme Corp";

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", expandedClusterId),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.recordId).toBe("r1");
            expect(data.recordName).toBe("Employment Matter");
            expect(data.similarity).toBe(0.88);
            expect(data.domain).toBe("matters");
            expect(data.recordType).toBe("sprk_matter");
        });

        it("should set isExpanded true on the expanded cluster node", () => {
            const results = [makeDocResult("d1", { documentType: "Contract" })];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", "cluster-Contract"),
            );

            const clusterNode = result.current.nodes.find(
                (n) => n.id === "cluster-Contract",
            );
            const data = clusterNode!.data as unknown as ClusterNodeData;
            expect(data.isExpanded).toBe(true);
        });

        it("should not expand non-matching clusters", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Agreement" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", "cluster-Contract"),
            );

            // Contract cluster is expanded -> 1 record node
            // Agreement cluster is NOT expanded -> 0 record nodes for it
            const recordNodes = result.current.nodes.filter(
                (n) => n.type === "recordNode",
            );
            expect(recordNodes).toHaveLength(1);
            expect(recordNodes[0].id).toBe("record-d1");
        });
    });

    // --- Inter-cluster edges ---

    describe("inter-cluster edges", () => {
        it("should create edges between clusters with shared parent entities (documents)", () => {
            const results = [
                makeDocResult("d1", {
                    documentType: "Contract",
                    parentEntityId: "shared-entity",
                }),
                makeDocResult("d2", {
                    documentType: "Agreement",
                    parentEntityId: "shared-entity",
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const interClusterEdges = result.current.edges.filter(
                (e) =>
                    e.source.startsWith("cluster-") &&
                    e.target.startsWith("cluster-"),
            );
            expect(interClusterEdges.length).toBeGreaterThanOrEqual(1);
        });

        it("should create edges between clusters with shared organizations (records)", () => {
            const results = [
                makeRecordResult("r1", "sprk_matter", {
                    people: ["Alice"],
                    organizations: ["Shared Corp"],
                }),
                makeRecordResult("r2", "sprk_project", {
                    people: ["Bob"],
                    organizations: ["Shared Corp"],
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "PersonContact", null),
            );

            const interClusterEdges = result.current.edges.filter(
                (e) =>
                    e.source.startsWith("cluster-") &&
                    e.target.startsWith("cluster-"),
            );
            expect(interClusterEdges.length).toBeGreaterThanOrEqual(1);
        });

        it("should not create inter-cluster edges when no shared attributes exist", () => {
            const results = [
                makeDocResult("d1", {
                    documentType: "Contract",
                    parentEntityId: "entity-A",
                }),
                makeDocResult("d2", {
                    documentType: "Agreement",
                    parentEntityId: "entity-B",
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            const interClusterEdges = result.current.edges.filter(
                (e) =>
                    e.source.startsWith("cluster-") &&
                    e.target.startsWith("cluster-"),
            );
            expect(interClusterEdges).toHaveLength(0);
        });
    });

    // --- MAX_GRAPH_RESULTS cap ---

    describe("MAX_GRAPH_RESULTS cap", () => {
        it("should limit to top 100 results by score", () => {
            const results = Array.from({ length: 150 }, (_, i) =>
                makeDocResult(`d${i}`, { combinedScore: 1 - i * 0.005 }),
            );

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            // All results are "Contract" type, so one cluster node
            const clusterNodes = result.current.nodes.filter(
                (n) => n.type === "clusterNode",
            );
            expect(clusterNodes).toHaveLength(1);

            const data = clusterNodes[0].data as unknown as ClusterNodeData;
            // Should be capped at 100
            expect(data.recordCount).toBe(100);
        });
    });

    // --- Node positions ---

    describe("node positions", () => {
        it("should assign non-zero positions to cluster nodes after simulation", () => {
            const results = [
                makeDocResult("d1", { documentType: "Contract" }),
                makeDocResult("d2", { documentType: "Agreement" }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            // With 2 clusters, at least one should be non-zero after d3-force
            const positions = result.current.nodes.map((n) => n.position);
            const hasNonZero = positions.some(
                (p) => p.x !== 0 || p.y !== 0,
            );
            expect(hasNonZero).toBe(true);
        });

        it("should always set isSimulating to false (synchronous simulation)", () => {
            const results = [makeDocResult("d1")];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", null),
            );

            expect(result.current.isSimulating).toBe(false);
        });
    });

    // --- Domain mapping ---

    describe("domain mapping for record results", () => {
        it("should map sprk_matter to 'matters' domain", () => {
            const results = [makeRecordResult("r1", "sprk_matter")];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", "cluster-Acme Corp"),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.domain).toBe("matters");
        });

        it("should map sprk_project to 'projects' domain", () => {
            const results = [
                makeRecordResult("r1", "sprk_project", {
                    organizations: ["Acme Corp"],
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", "cluster-Acme Corp"),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.domain).toBe("projects");
        });

        it("should map sprk_invoice to 'invoices' domain", () => {
            const results = [
                makeRecordResult("r1", "sprk_invoice", {
                    organizations: ["Acme Corp"],
                }),
            ];

            const { result } = renderHook(() =>
                useClusterLayout(results, "Organization", "cluster-Acme Corp"),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.domain).toBe("invoices");
        });

        it("should map document results to 'documents' domain", () => {
            const results = [makeDocResult("d1", { documentType: "Contract" })];

            const { result } = renderHook(() =>
                useClusterLayout(results, "DocumentType", "cluster-Contract"),
            );

            const recordNode = result.current.nodes.find(
                (n) => n.type === "recordNode",
            );
            const data = recordNode!.data as unknown as RecordNodeData;
            expect(data.domain).toBe("documents");
        });
    });

    // --- Memoization ---

    describe("memoization", () => {
        it("should return same reference when inputs do not change", () => {
            const results = [makeDocResult("d1")];
            const { result, rerender } = renderHook(
                ({ res, cb, exp }) => useClusterLayout(res, cb, exp),
                {
                    initialProps: {
                        res: results,
                        cb: "DocumentType" as GraphClusterBy,
                        exp: null as string | null,
                    },
                },
            );

            const firstRender = result.current;
            rerender({
                res: results,
                cb: "DocumentType" as GraphClusterBy,
                exp: null,
            });
            const secondRender = result.current;

            // useMemo should return same reference for same inputs
            expect(firstRender.nodes).toBe(secondRender.nodes);
            expect(firstRender.edges).toBe(secondRender.edges);
        });
    });
});
