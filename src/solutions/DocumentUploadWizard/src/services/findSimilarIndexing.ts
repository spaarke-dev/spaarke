/**
 * Find Similar step: make sure the chosen document is in the search index before the viewer opens.
 *
 * POST /api/ai/rag/index-file requires `tenantId` (the BFF returns 400 without it, GitHub #3299). The request also
 * names the document, so the server holds the body to that row and indexes it under the row's parent record — a
 * request without a document id leaves the parent alone (#3223) and would index the file with none.
 */
import { buildBffApiUrl } from "@spaarke/auth";

export interface FindSimilarIndexTarget {
    documentId: string;
    driveId: string;
    itemId: string;
    fileName: string;
}

/** The fetch the step uses: `@spaarke/auth` `authenticatedFetch`, which throws on any non-2xx response. */
export type IndexFetch = (url: string, init?: RequestInit) => Promise<unknown>;

/** The request body for one document. */
export function buildFindSimilarIndexBody(target: FindSimilarIndexTarget, tenantId: string): Record<string, string> {
    return {
        documentId: target.documentId,
        driveId: target.driveId,
        itemId: target.itemId,
        fileName: target.fileName,
        tenantId,
    };
}

/**
 * Sends the index request. Returns `true` when the server accepted it and `false` when it could not be sent or was
 * refused, so the step can tell the user rather than open the viewer as if the document were indexed.
 */
export async function requestFindSimilarIndexing(
    fetchFn: IndexFetch,
    bffBaseUrl: string,
    target: FindSimilarIndexTarget,
    resolveTenantId: () => Promise<string | undefined | null>,
): Promise<boolean> {
    try {
        const tenantId = await resolveTenantId();
        if (!tenantId) {
            console.warn("[FindSimilar] No tenant id; the document was not sent for indexing.");
            return false;
        }

        await fetchFn(buildBffApiUrl(bffBaseUrl, "/ai/rag/index-file"), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(buildFindSimilarIndexBody(target, tenantId)),
        });
        return true;
    } catch (err) {
        console.warn("[FindSimilar] Indexing request failed:", err);
        return false;
    }
}
