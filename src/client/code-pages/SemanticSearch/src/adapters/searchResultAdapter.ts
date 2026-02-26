/**
 * Adapter: Map search results → IDatasetRecord for UniversalDatasetGrid.
 *
 * Bridges the BFF API response types (DocumentSearchResult, RecordSearchResult)
 * to the shared library's generic IDatasetRecord shape.
 *
 * @see types/index.ts — source types
 * @see Spaarke.UI.Components/src/types/DatasetTypes.ts — target type
 */

import type { DocumentSearchResult, RecordSearchResult, SearchDomain } from "../types";

/** Shape compatible with IDatasetRecord from @spaarke/ui-components. */
export interface IDatasetRecord {
    id: string;
    entityName: string;
    [key: string]: unknown;
}

/** Map a document search result to IDatasetRecord. */
export function mapDocumentResult(result: DocumentSearchResult): IDatasetRecord {
    return {
        id: result.documentId ?? "",
        entityName: "sprk_document",
        name: result.name,
        combinedScore: result.combinedScore,
        documentType: result.documentType,
        fileType: result.fileType,
        parentEntityName: result.parentEntityName,
        parentEntityType: result.parentEntityType,
        parentEntityId: result.parentEntityId,
        updatedAt: result.updatedAt,
        createdAt: result.createdAt,
        createdBy: result.createdBy,
        summary: result.summary,
        highlights: result.highlights,
        fileUrl: result.fileUrl,
        recordUrl: result.recordUrl,
    };
}

/** Map a record search result (matter/project/invoice) to IDatasetRecord. */
export function mapRecordResult(result: RecordSearchResult): IDatasetRecord {
    return {
        id: result.recordId,
        entityName: result.recordType,
        recordName: result.recordName,
        confidenceScore: result.confidenceScore,
        recordDescription: result.recordDescription,
        organizations: result.organizations,
        people: result.people,
        keywords: result.keywords,
        matchReasons: result.matchReasons,
        createdAt: result.createdAt,
        modifiedAt: result.modifiedAt,
    };
}

/** Map an array of search results based on the active domain. */
export function mapSearchResults(
    results: (DocumentSearchResult | RecordSearchResult)[],
    domain: SearchDomain,
): IDatasetRecord[] {
    if (domain === "documents") {
        return results.map((r) => mapDocumentResult(r as DocumentSearchResult));
    }
    return results.map((r) => mapRecordResult(r as RecordSearchResult));
}

/** Map search domain to Dataverse entity logical name. */
export function domainToEntityName(domain: SearchDomain): string {
    switch (domain) {
        case "documents":
            return "sprk_document";
        case "matters":
            return "sprk_matter";
        case "projects":
            return "sprk_project";
        case "invoices":
            return "sprk_invoice";
    }
}
