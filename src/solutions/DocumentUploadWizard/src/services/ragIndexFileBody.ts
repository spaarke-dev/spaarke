/**
 * The one builder of the wizard's POST /api/ai/rag/index-file body, used by the upload (Phase 4, `uploadOrchestrator.ts`) and
 * the Find Similar step (`findSimilarIndexing.ts`, GitHub #3299). Both name the same parent: a request that omits it lets the
 * server clear the parent the upload stored when the document row names no record (for example a document filed under a
 * contact).
 */
import type { ParentContext } from "@spaarke/ui-components/services/document-upload";

export interface RagIndexFileBody {
    driveId: string;
    itemId: string;
    fileName: string;
    tenantId: string;
    documentId: string | undefined;
    parentEntity: { entityType: string; entityId: string; entityName: string } | undefined;
}

export function buildRagIndexFileBody(args: {
    driveId: string;
    itemId: string;
    fileName: string;
    tenantId: string;
    documentId: string | undefined;
    parentContext: ParentContext;
}): RagIndexFileBody {
    const { parentContext } = args;
    // Map Dataverse logical name (e.g., "sprk_matter") to the short form the BFF stores
    // ("matter"). Mirrors the convention used by RagEndpoints.SendToIndex when building
    // ParentEntityContext from a Document's lookups.
    const shortEntityType = parentContext.parentEntityName?.startsWith("sprk_")
        ? parentContext.parentEntityName.substring("sprk_".length)
        : parentContext.parentEntityName ?? "";

    return {
        driveId: args.driveId,
        itemId: args.itemId,
        fileName: args.fileName,
        tenantId: args.tenantId,
        documentId: args.documentId,
        parentEntity:
            shortEntityType && parentContext.parentRecordId
                ? {
                      entityType: shortEntityType,
                      entityId: parentContext.parentRecordId,
                      entityName: parentContext.parentDisplayName ?? "",
                  }
                : undefined,
    };
}
