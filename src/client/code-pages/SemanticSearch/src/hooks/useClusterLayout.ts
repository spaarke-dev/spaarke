/**
 * useClusterLayout — d3-force layout hook for cluster graph visualization
 *
 * Transforms search results into ReactFlow-compatible Node[] and Edge[] arrays
 * with physics-based cluster positioning via d3-force simulation.
 *
 * Algorithm:
 *   1. Take top 100 results by similarity/confidence score
 *   2. Group results by the selected cluster category
 *   3. Create ClusterNode for each group (collapsed view)
 *   4. When a cluster is expanded, add RecordNodes for that cluster's members
 *   5. Create inter-cluster edges where results share relationships
 *   6. Run d3-force simulation for node positioning
 *
 * @see useForceLayout.ts (DocumentRelationshipViewer) — base reference
 */

import { useMemo } from "react";
import {
    forceSimulation,
    forceManyBody,
    forceCenter,
    forceCollide,
    forceLink,
    type SimulationNodeDatum,
    type SimulationLinkDatum,
} from "d3-force";
import type { Node, Edge } from "@xyflow/react";
import type {
    DocumentSearchResult,
    RecordSearchResult,
    GraphClusterBy,
    ClusterNodeData,
    RecordNodeData,
    SearchDomain,
} from "../types";

// =============================================
// Constants
// =============================================

const MAX_GRAPH_RESULTS = 100;
const SIMULATION_TICKS = 300;
const CLUSTER_CHARGE_STRENGTH = -800;
const RECORD_CHARGE_STRENGTH = -200;
const CLUSTER_BASE_RADIUS = 40;

// =============================================
// Internal types
// =============================================

type SearchResult = DocumentSearchResult | RecordSearchResult;

interface ForceNode extends SimulationNodeDatum {
    id: string;
    isCluster: boolean;
    clusterId?: string;
}

interface ForceLink extends SimulationLinkDatum<ForceNode> {
    source: string;
    target: string;
}

interface ClusterGroup {
    key: string;
    label: string;
    results: SearchResult[];
    avgSimilarity: number;
}

// =============================================
// Helpers
// =============================================

/** Check if a result is a DocumentSearchResult. */
function isDocumentResult(result: SearchResult): result is DocumentSearchResult {
    return "documentId" in result;
}

/** Get the score for sorting/display. */
function getScore(result: SearchResult): number {
    return isDocumentResult(result)
        ? result.combinedScore
        : result.confidenceScore;
}

/** Get unique ID for a result. */
function getResultId(result: SearchResult): string {
    return isDocumentResult(result)
        ? result.documentId ?? ""
        : result.recordId;
}

/** Get display name for a result. */
function getResultName(result: SearchResult): string {
    return isDocumentResult(result)
        ? result.name ?? "Untitled"
        : result.recordName;
}

/** Get the search domain for a result. */
function getResultDomain(result: SearchResult): SearchDomain {
    if (isDocumentResult(result)) return "documents";
    switch (result.recordType) {
        case "sprk_matter":
            return "matters";
        case "sprk_project":
            return "projects";
        case "sprk_invoice":
            return "invoices";
        default:
            return "matters";
    }
}

/**
 * Extract the cluster key from a result based on the clustering mode.
 */
function extractClusterKey(
    result: SearchResult,
    clusterBy: GraphClusterBy
): string {
    if (isDocumentResult(result)) {
        switch (clusterBy) {
            case "DocumentType":
            case "MatterType":
                return result.documentType ?? "Uncategorized";
            case "Organization":
                return result.parentEntityName ?? "Uncategorized";
            case "PracticeArea":
            case "PersonContact":
                return "Uncategorized";
            default:
                return "Uncategorized";
        }
    } else {
        switch (clusterBy) {
            case "MatterType":
                return result.recordType === "sprk_matter"
                    ? result.recordName?.split(" ")[0] ?? "Uncategorized"
                    : "Uncategorized";
            case "Organization":
                return result.organizations?.[0] ?? "Uncategorized";
            case "PersonContact":
                return result.people?.[0] ?? "Uncategorized";
            case "DocumentType":
            case "PracticeArea":
                return "Uncategorized";
            default:
                return "Uncategorized";
        }
    }
}

/**
 * Group results into clusters.
 */
function groupByClusters(
    results: SearchResult[],
    clusterBy: GraphClusterBy
): ClusterGroup[] {
    const groups = new Map<string, SearchResult[]>();

    for (const result of results) {
        const key = extractClusterKey(result, clusterBy);
        const group = groups.get(key);
        if (group) {
            group.push(result);
        } else {
            groups.set(key, [result]);
        }
    }

    return Array.from(groups.entries()).map(([key, members]) => {
        const totalScore = members.reduce((sum, r) => sum + getScore(r), 0);
        return {
            key,
            label: key,
            results: members,
            avgSimilarity: members.length > 0 ? totalScore / members.length : 0,
        };
    });
}

// =============================================
// Hook
// =============================================

export interface UseClusterLayoutResult {
    nodes: Node[];
    edges: Edge[];
    isSimulating: boolean;
}

/**
 * Transforms search results into positioned ReactFlow nodes and edges
 * using d3-force cluster layout.
 *
 * @param results - Search results to cluster
 * @param clusterBy - Clustering category
 * @param expandedClusterId - ID of expanded cluster (null = collapsed view)
 */
export function useClusterLayout(
    results: SearchResult[],
    clusterBy: GraphClusterBy,
    expandedClusterId: string | null
): UseClusterLayoutResult {
    return useMemo(() => {
        if (results.length === 0) {
            return { nodes: [], edges: [], isSimulating: false };
        }

        // 1. Take top 100 results by score
        const sortedResults = [...results]
            .sort((a, b) => getScore(b) - getScore(a))
            .slice(0, MAX_GRAPH_RESULTS);

        // 2. Group into clusters
        const clusters = groupByClusters(sortedResults, clusterBy);

        if (clusters.length === 0) {
            return { nodes: [], edges: [], isSimulating: false };
        }

        // 3. Build force nodes
        const forceNodes: ForceNode[] = [];
        const rfNodes: Node[] = [];
        const rfEdges: Edge[] = [];

        // Add cluster nodes
        for (const cluster of clusters) {
            const clusterId = `cluster-${cluster.key}`;

            forceNodes.push({
                id: clusterId,
                isCluster: true,
            });

            const nodeData: ClusterNodeData = {
                clusterKey: cluster.key,
                clusterLabel: cluster.label,
                recordCount: cluster.results.length,
                avgSimilarity: cluster.avgSimilarity,
                category: clusterBy,
                topResults: cluster.results
                    .slice(0, 3)
                    .map((r) => ({ name: getResultName(r) })),
                isExpanded: expandedClusterId === clusterId,
            };

            rfNodes.push({
                id: clusterId,
                type: "clusterNode",
                position: { x: 0, y: 0 },
                data: nodeData as unknown as Record<string, unknown>,
            });

            // If this cluster is expanded, add record nodes
            if (expandedClusterId === clusterId) {
                for (const result of cluster.results) {
                    const recordId = `record-${getResultId(result)}`;

                    forceNodes.push({
                        id: recordId,
                        isCluster: false,
                        clusterId,
                    });

                    const recordData: RecordNodeData = {
                        recordId: getResultId(result),
                        recordName: getResultName(result),
                        similarity: getScore(result),
                        parentEntityName: isDocumentResult(result)
                            ? result.parentEntityName
                            : undefined,
                        domain: getResultDomain(result),
                        recordType: isDocumentResult(result)
                            ? "sprk_document"
                            : result.recordType,
                    };

                    rfNodes.push({
                        id: recordId,
                        type: "recordNode",
                        position: { x: 0, y: 0 },
                        data: recordData as unknown as Record<string, unknown>,
                    });

                    // Edge from cluster to record
                    rfEdges.push({
                        id: `edge-${clusterId}-${recordId}`,
                        source: clusterId,
                        target: recordId,
                        type: "default",
                    });
                }
            }
        }

        // 4. Create inter-cluster edges based on shared attributes
        for (let i = 0; i < clusters.length; i++) {
            for (let j = i + 1; j < clusters.length; j++) {
                const clusterA = clusters[i];
                const clusterB = clusters[j];

                // Count shared organizations/people/parentEntity between clusters
                let sharedCount = 0;
                for (const rA of clusterA.results) {
                    for (const rB of clusterB.results) {
                        if (
                            !isDocumentResult(rA) &&
                            !isDocumentResult(rB)
                        ) {
                            // Record-to-record: check shared organizations
                            const orgsA = rA.organizations ?? [];
                            const orgsB = rB.organizations ?? [];
                            for (const org of orgsA) {
                                if (orgsB.includes(org)) sharedCount++;
                            }
                        } else if (
                            isDocumentResult(rA) &&
                            isDocumentResult(rB)
                        ) {
                            // Document-to-document: check same parent entity
                            if (
                                rA.parentEntityId &&
                                rA.parentEntityId === rB.parentEntityId
                            ) {
                                sharedCount++;
                            }
                        }
                    }
                }

                if (sharedCount > 0) {
                    rfEdges.push({
                        id: `edge-cluster-${clusterA.key}-${clusterB.key}`,
                        source: `cluster-${clusterA.key}`,
                        target: `cluster-${clusterB.key}`,
                        type: "default",
                        data: { weight: sharedCount },
                        style: {
                            strokeWidth: Math.min(sharedCount, 5),
                            opacity: 0.4,
                        },
                    });
                }
            }
        }

        // 5. Build force links from edges
        const forceLinks: ForceLink[] = rfEdges.map((edge) => ({
            source: edge.source,
            target: edge.target,
        }));

        // 6. Run d3-force simulation (synchronous)
        const simulation = forceSimulation<ForceNode, ForceLink>(forceNodes)
            .force(
                "charge",
                forceManyBody<ForceNode>().strength((d) =>
                    d.isCluster
                        ? CLUSTER_CHARGE_STRENGTH
                        : RECORD_CHARGE_STRENGTH
                )
            )
            .force("center", forceCenter(0, 0))
            .force(
                "collide",
                forceCollide<ForceNode>().radius((d) => {
                    if (d.isCluster) {
                        const cluster = clusters.find(
                            (c) => `cluster-${c.key}` === d.id
                        );
                        return (
                            CLUSTER_BASE_RADIUS +
                            Math.sqrt(cluster?.results.length ?? 1) * 15
                        );
                    }
                    return 30;
                })
            )
            .force(
                "link",
                forceLink<ForceNode, ForceLink>(forceLinks)
                    .id((d) => d.id)
                    .distance(150)
                    .strength(0.3)
            )
            .stop();

        // Run simulation synchronously for instant layout
        simulation.tick(SIMULATION_TICKS);

        // 7. Apply positions from simulation to ReactFlow nodes
        const positionedNodes = rfNodes.map((node) => {
            const forceNode = forceNodes.find((fn) => fn.id === node.id);
            return {
                ...node,
                position: {
                    x: forceNode?.x ?? 0,
                    y: forceNode?.y ?? 0,
                },
            };
        });

        return {
            nodes: positionedNodes,
            edges: rfEdges,
            isSimulating: false,
        };
    }, [results, clusterBy, expandedClusterId]);
}

export default useClusterLayout;
