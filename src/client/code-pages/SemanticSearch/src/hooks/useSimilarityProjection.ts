/**
 * useSimilarityProjection — Network graph data computation hook
 *
 * Computes node data and pairwise similarity edges from search results
 * for network graph visualization. The actual d3-force simulation is
 * managed by the component (SearchResultsMap) for interactive dragging.
 *
 * Returns raw node data and edges — no positions are computed here.
 */

import { useMemo } from "react";
import type { VisualizationColorBy } from "../types";
import {
    type SearchResult,
    isDocumentResult,
    getScore,
    getResultId,
    getResultName,
    extractClusterKey,
    filterAndSortResults,
} from "../utils/groupResults";

// =============================================
// Public types
// =============================================

/** Node data for the network graph (positions managed by component simulation). */
export interface NetworkNodeData {
    /** Unique result ID (documentId or recordId). */
    id: string;
    /** Dot radius: 8-24 px scaled by score. */
    radius: number;
    /** Category key derived from extractClusterKey. */
    category: string;
    /** Display name of the result. */
    name: string;
    /** Relevance score (0-1). */
    score: number;
    /** Original search result for drill-down. */
    result: SearchResult;
}

/** A similarity link between two nodes. */
export interface SimilarityEdge {
    sourceId: string;
    targetId: string;
    /** Similarity value (0-~1.1). Higher = more related. */
    similarity: number;
}

// =============================================
// Constants
// =============================================

const MAX_RESULTS = 100;
const MIN_LINK_SIMILARITY = 0.35;

/** Generic fallback category values that should NOT count as meaningful matches. */
const GENERIC_CATEGORIES = new Set([
    "Uncategorized", "Document", "Other", "Unassigned", "Unknown", "Record", "Matter",
]);

// =============================================
// Helpers
// =============================================

/**
 * Check whether two string arrays share at least one element.
 */
function hasOverlap(a: string[] | undefined, b: string[] | undefined): boolean {
    if (!a || !b || a.length === 0 || b.length === 0) return false;
    for (const item of a) {
        if (b.includes(item)) return true;
    }
    return false;
}

/**
 * Compute pairwise metadata similarity between two results.
 * Returns a value in [0, ~1.1] — higher means more related.
 */
function computePairSimilarity(
    a: SearchResult,
    b: SearchResult,
    colorBy: VisualizationColorBy,
): number {
    let similarity = 0;

    // Same category: +0.4 (only for meaningful/specific categories, not generic fallbacks)
    const catA = extractClusterKey(a, colorBy);
    const catB = extractClusterKey(b, colorBy);
    if (catA === catB && !GENERIC_CATEGORIES.has(catA)) {
        similarity += 0.4;
    }

    // Domain-specific attributes
    const aIsDoc = isDocumentResult(a);
    const bIsDoc = isDocumentResult(b);

    if (aIsDoc && bIsDoc) {
        if (a.parentEntityName && a.parentEntityName === b.parentEntityName) {
            similarity += 0.3;
        }
        if (a.fileType && a.fileType === b.fileType) {
            similarity += 0.1;
        }
    } else if (!aIsDoc && !bIsDoc) {
        if (hasOverlap(a.organizations, b.organizations)) {
            similarity += 0.3;
        }
        if (hasOverlap(a.people, b.people)) {
            similarity += 0.1;
        }
        if (hasOverlap(a.keywords, b.keywords)) {
            similarity += 0.2;
        }
    }

    // Score proximity: closer scores → small bonus
    similarity += (1 - Math.abs(getScore(a) - getScore(b))) * 0.1;

    return similarity;
}

// =============================================
// Hook
// =============================================

/**
 * Compute node data and pairwise similarity edges from search results.
 *
 * @param results       - All available search results (documents + records)
 * @param colorBy       - Category field for clustering / coloring
 * @param minSimilarity - Minimum relevance threshold (0-100 scale)
 * @returns Node data, similarity edges, and readiness flag.
 */
export function useSimilarityProjection(
    results: SearchResult[],
    colorBy: VisualizationColorBy,
    minSimilarity: number,
): { nodes: NetworkNodeData[]; edges: SimilarityEdge[]; isReady: boolean } {
    return useMemo(() => {
        if (results.length === 0) return { nodes: [], edges: [], isReady: true };

        // 1. Filter by minSimilarity, take top 100 by score
        const filtered = filterAndSortResults(results, minSimilarity, MAX_RESULTS);

        if (filtered.length === 0) return { nodes: [], edges: [], isReady: true };

        // 2. Build node data with category assignment
        const nodes: NetworkNodeData[] = filtered.map((result) => {
            const score = getScore(result);
            return {
                id: getResultId(result),
                radius: 8 + score * 16, // 8-24 px
                category: extractClusterKey(result, colorBy),
                name: getResultName(result),
                score,
                result,
            };
        });

        // 3. Build pairwise edges where similarity > threshold
        const edges: SimilarityEdge[] = [];
        for (let i = 0; i < nodes.length; i++) {
            for (let j = i + 1; j < nodes.length; j++) {
                const sim = computePairSimilarity(
                    nodes[i].result,
                    nodes[j].result,
                    colorBy,
                );
                if (sim > MIN_LINK_SIMILARITY) {
                    edges.push({
                        sourceId: nodes[i].id,
                        targetId: nodes[j].id,
                        similarity: sim,
                    });
                }
            }
        }

        return { nodes, edges, isReady: true };
    }, [results, colorBy, minSimilarity]);
}

export default useSimilarityProjection;
