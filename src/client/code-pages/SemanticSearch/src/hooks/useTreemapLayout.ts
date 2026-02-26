/**
 * useTreemapLayout — Treemap tile layout hook
 *
 * Computes pixel-positioned treemap tiles from grouped search results
 * using d3-hierarchy's treemap with squarified tiling. Each result
 * becomes a leaf tile sized by its relevance score, nested under a
 * group header determined by the selected groupBy category.
 *
 * All computation runs synchronously inside useMemo.
 */

import { useMemo } from "react";
import {
    hierarchy,
    treemap,
    treemapSquarify,
    type HierarchyRectangularNode,
} from "d3-hierarchy";
import type { VisualizationColorBy } from "../types";
import {
    type SearchResult,
    getScore,
    getResultId,
    getResultName,
    groupResults,
} from "../utils/groupResults";

// =============================================
// Public types
// =============================================

/** A single result tile positioned within the treemap. */
export interface TreemapTile {
    /** Unique result ID (documentId or recordId). */
    id: string;
    /** Display name of the result. */
    name: string;
    /** Relevance score (0-1). */
    score: number;
    /** Category group key this tile belongs to. */
    group: string;
    /** Left edge in pixels. */
    x0: number;
    /** Top edge in pixels. */
    y0: number;
    /** Right edge in pixels. */
    x1: number;
    /** Bottom edge in pixels. */
    y1: number;
    /** Tile width in pixels (x1 - x0). */
    width: number;
    /** Tile height in pixels (y1 - y0). */
    height: number;
    /** Original search result for drill-down. */
    result: SearchResult;
}

/** Summary information for a treemap category group. */
export interface TreemapGroup {
    /** Category key. */
    key: string;
    /** Display label (same as key). */
    label: string;
    /** Number of results in this group. */
    count: number;
    /** Sum of all result scores in the group. */
    totalScore: number;
}

// =============================================
// Internal types for d3-hierarchy data shape
// =============================================

interface TreemapLeafDatum {
    name: string;
    value: number;
    result: SearchResult;
    children?: undefined;
}

interface TreemapBranchDatum {
    name: string;
    children: TreemapLeafDatum[];
    value?: undefined;
    result?: undefined;
}

interface TreemapRootDatum {
    name: string;
    children: TreemapBranchDatum[];
    value?: undefined;
    result?: undefined;
}

type TreemapDatum = TreemapRootDatum | TreemapBranchDatum | TreemapLeafDatum;

// =============================================
// Constants
// =============================================

const PADDING_TOP = 20;
const PADDING_INNER = 2;
const PADDING_OUTER = 4;

// =============================================
// Hook
// =============================================

/**
 * Compute treemap tile positions from grouped search results.
 *
 * @param results  - All available search results (documents + records)
 * @param groupBy  - Category field for grouping tiles
 * @param width    - Available container width in pixels
 * @param height   - Available container height in pixels
 * @returns Positioned tiles and group summaries.
 */
export function useTreemapLayout(
    results: SearchResult[],
    groupBy: VisualizationColorBy,
    width: number,
    height: number,
): { tiles: TreemapTile[]; groups: TreemapGroup[] } {
    return useMemo(() => {
        // Guard: invalid dimensions or empty results
        if (width <= 0 || height <= 0 || results.length === 0) {
            return { tiles: [], groups: [] };
        }

        // 1. Group results by the selected category
        const resultGroups = groupResults(results, groupBy);

        if (resultGroups.length === 0) {
            return { tiles: [], groups: [] };
        }

        // 2. Build d3-hierarchy data tree
        const rootData: TreemapRootDatum = {
            name: "root",
            children: resultGroups.map((group) => ({
                name: group.key,
                children: group.results.map((result) => ({
                    name: getResultName(result),
                    value: Math.max(getScore(result) * 100, 1),
                    result,
                })),
            })),
        };

        // 3. Create hierarchy and compute sums
        const root = hierarchy<TreemapDatum>(rootData)
            .sum((d) => (d as TreemapLeafDatum).value ?? 0);

        // 4. Apply treemap layout
        const treemapLayout = treemap<TreemapDatum>()
            .size([width, height])
            .paddingTop(PADDING_TOP)
            .paddingInner(PADDING_INNER)
            .paddingOuter(PADDING_OUTER)
            .tile(treemapSquarify);

        treemapLayout(root);

        // 5. Extract leaf nodes as TreemapTile[]
        const tiles: TreemapTile[] = [];

        for (const leaf of root.leaves()) {
            const leafData = leaf.data as TreemapLeafDatum;

            // Skip non-leaf data (branches/root should not appear as leaves,
            // but guard defensively)
            if (!leafData.result) continue;

            const rectNode = leaf as HierarchyRectangularNode<TreemapDatum>;

            // Determine parent group key
            const parentGroup = leaf.parent?.data as TreemapBranchDatum | undefined;
            const groupKey = parentGroup?.name ?? "Uncategorized";

            tiles.push({
                id: getResultId(leafData.result),
                name: leafData.name,
                score: getScore(leafData.result),
                group: groupKey,
                x0: rectNode.x0,
                y0: rectNode.y0,
                x1: rectNode.x1,
                y1: rectNode.y1,
                width: rectNode.x1 - rectNode.x0,
                height: rectNode.y1 - rectNode.y0,
                result: leafData.result,
            });
        }

        // 6. Build group summaries
        const groups: TreemapGroup[] = resultGroups.map((group) => ({
            key: group.key,
            label: group.label,
            count: group.results.length,
            totalScore: group.totalScore,
        }));

        return { tiles, groups };
    }, [results, groupBy, width, height]);
}

export default useTreemapLayout;
