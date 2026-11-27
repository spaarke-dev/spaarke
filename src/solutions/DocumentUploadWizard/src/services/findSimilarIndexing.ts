/**
 * Find Similar step: make sure the chosen document is in the search index before the viewer opens.
 *
 * POST /api/ai/rag/index-file requires `tenantId` (the BFF returns 400 without it, GitHub #3299). The body is the one the
 * upload itself sends (`buildRagIndexFileBody`): it names the document, so the server holds it to that row, and the
 * same parent, so this request cannot clear the parent the upload stored.
 */
import { buildBffApiUrl } from "@spaarke/auth";
import type { authenticatedFetch } from "@spaarke/auth";
import type { ParentContext } from "@spaarke/ui-components/services/document-upload";
import { buildRagIndexFileBody } from "./ragIndexFileBody";

export interface FindSimilarIndexTarget {
    documentId: string;
    driveId: string;
    itemId: string;
    fileName: string;
    /** The parent the upload was given (the same object `UploadOrchestratorConfig.parentContext` carries). */
    parentContext: ParentContext;
}

/** `@spaarke/auth` `authenticatedFetch`, which throws on any non-2xx response (a raw `fetch` does not, so it is not accepted). */
export type IndexFetch = typeof authenticatedFetch;

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

        const { parentContext, ...file } = target;
        await fetchFn(buildBffApiUrl(bffBaseUrl, "/ai/rag/index-file"), {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(buildRagIndexFileBody({ ...file, tenantId, parentContext })),
        });
        return true;
    } catch (err) {
        console.warn("[FindSimilar] Indexing request failed:", err);
        return false;
    }
}
