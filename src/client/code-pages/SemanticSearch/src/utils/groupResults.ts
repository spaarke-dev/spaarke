/**
 * groupResults — Shared helpers for categorizing and accessing search results
 *
 * Extracted from useClusterLayout.ts for reuse across Map, Treemap, and
 * Timeline visualization hooks.
 */

import type {
    DocumentSearchResult,
    RecordSearchResult,
    GraphClusterBy,
    SearchDomain,
} from "../types";

// =============================================
// Type alias
// =============================================

export type SearchResult = DocumentSearchResult | RecordSearchResult;

// =============================================
// Type guards & accessors
// =============================================

/** Check if a result is a DocumentSearchResult. */
export function isDocumentResult(
    result: SearchResult
): result is DocumentSearchResult {
    return "documentId" in result;
}

/** Get the relevance score for a result. */
export function getScore(result: SearchResult): number {
    return isDocumentResult(result)
        ? result.combinedScore
        : result.confidenceScore;
}

/** Get the unique ID for a result. */
export function getResultId(result: SearchResult): string {
    return isDocumentResult(result)
        ? result.documentId ?? ""
        : result.recordId;
}

/** Get the display name for a result. */
export function getResultName(result: SearchResult): string {
    return isDocumentResult(result)
        ? result.name ?? "Untitled"
        : result.recordName;
}

/** Get the search domain for a result. */
export function getResultDomain(result: SearchResult): SearchDomain {
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

/** Get the date value from a result based on a field name. */
export function getResultDate(
    result: SearchResult,
    field: "createdAt" | "updatedAt" | "modifiedAt"
): string | undefined {
    if (isDocumentResult(result)) {
        if (field === "createdAt") return result.createdAt;
        if (field === "updatedAt" || field === "modifiedAt")
            return result.updatedAt;
    } else {
        if (field === "createdAt") return result.createdAt;
        if (field === "modifiedAt" || field === "updatedAt")
            return result.modifiedAt;
    }
    return undefined;
}

// =============================================
// Clustering
// =============================================

/**
 * Extract the cluster/category key from a result based on a grouping mode.
 */
export function extractClusterKey(
    result: SearchResult,
    clusterBy: GraphClusterBy
): string {
    if (isDocumentResult(result)) {
        switch (clusterBy) {
            case "DocumentType":
                // Fall through: documentType → fileType (pdf/docx) → parentEntityType
                return result.documentType || result.fileType || result.parentEntityType || "Document";
            case "MatterType":
                return result.parentEntityType || result.documentType || "Other";
            case "Organization":
                return result.parentEntityName || "Unassigned";
            case "PracticeArea":
                return result.fileType || result.documentType || "Other";
            case "PersonContact":
                return result.createdBy || "Unknown";
            default:
                return result.documentType || result.fileType || "Document";
        }
    } else {
        switch (clusterBy) {
            case "MatterType":
                return result.recordType === "sprk_matter"
                    ? result.recordName?.split(" ")[0] ?? "Matter"
                    : result.recordType?.replace("sprk_", "") ?? "Record";
            case "Organization":
                return result.organizations?.[0] ?? "Unassigned";
            case "PersonContact":
                return result.people?.[0] ?? "Unknown";
            case "DocumentType":
                return result.recordType?.replace("sprk_", "") ?? "Record";
            case "PracticeArea":
                return result.keywords?.[0] ?? "Other";
            default:
                return result.recordType?.replace("sprk_", "") ?? "Record";
        }
    }
}

/** A group of results sharing a category key. */
export interface ResultGroup {
    key: string;
    label: string;
    results: SearchResult[];
    avgScore: number;
    totalScore: number;
}

/**
 * Group results into categories by the selected clustering mode.
 */
export function groupResults(
    results: SearchResult[],
    groupBy: GraphClusterBy
): ResultGroup[] {
    const groups = new Map<string, SearchResult[]>();

    for (const result of results) {
        const key = extractClusterKey(result, groupBy);
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
            avgScore: members.length > 0 ? totalScore / members.length : 0,
            totalScore,
        };
    });
}

/**
 * Sort and limit results by score, optionally filtering by minimum similarity.
 */
export function filterAndSortResults(
    results: SearchResult[],
    minSimilarity: number = 0,
    maxResults: number = 100
): SearchResult[] {
    const filtered =
        minSimilarity > 0
            ? results.filter((r) => getScore(r) * 100 >= minSimilarity)
            : results;

    return [...filtered]
        .sort((a, b) => getScore(b) - getScore(a))
        .slice(0, maxResults);
}
