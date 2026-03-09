/**
 * documentRecordService.ts
 * Creates sprk_document records in Dataverse and performs NavMap lookups.
 *
 * Two strategies for Dataverse write:
 *   1. Xrm.WebApi.createRecord (when running inside Dataverse form context)
 *   2. authenticatedFetch to BFF proxy (when Xrm is unavailable)
 *
 * NavMap lookup: GET /api/navmap/{entity}/{relationship}/lookup
 *
 * @see ADR-002 - Thin Dataverse plugins (record creation via WebApi)
 * @see ADR-008 - Endpoint filters for auth
 */

import { authenticatedFetch } from "@spaarke/auth";
import type { IDocumentFormValues, ICreateDocumentResult } from "../types";

// ---------------------------------------------------------------------------
// Xrm type shim (minimal, to avoid @types/xrm full dependency at runtime)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */
interface XrmWebApi {
    createRecord(entityLogicalName: string, data: Record<string, any>): Promise<{ id: string }>;
}

function getXrmWebApi(): XrmWebApi | null {
    try {
        const frames: Window[] = [window];
        try {
            if (window.parent && window.parent !== window) frames.push(window.parent);
        } catch { /* cross-origin */ }
        try {
            if (window.top && window.top !== window && window.top !== window.parent) frames.push(window.top!);
        } catch { /* cross-origin */ }

        for (const frame of frames) {
            try {
                const xrm = (frame as any).Xrm;
                if (xrm?.WebApi?.createRecord) {
                    return xrm.WebApi as XrmWebApi;
                }
            } catch { /* unavailable */ }
        }
    } catch { /* frame access error */ }
    return null;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// NavMap lookup
// ---------------------------------------------------------------------------

export interface INavMapLookupResult {
    /** Whether the lookup succeeded. */
    success: boolean;
    /** The resolved entity ID. */
    entityId?: string;
    /** The resolved entity name. */
    entityName?: string;
    /** Error message on failure. */
    error?: string;
}

/**
 * Perform a NavMap lookup via the BFF API.
 *
 * @param entity        Source entity logical name
 * @param relationship  Relationship name to traverse
 * @param lookupValue   Value to search for
 * @returns Lookup result
 */
export async function navMapLookup(
    entity: string,
    relationship: string,
    lookupValue: string,
): Promise<INavMapLookupResult> {
    try {
        const encodedValue = encodeURIComponent(lookupValue);
        const response = await authenticatedFetch(
            `/api/navmap/${entity}/${relationship}/lookup?value=${encodedValue}`,
        );

        if (!response.ok) {
            return {
                success: false,
                error: `NavMap lookup failed (HTTP ${response.status})`,
            };
        }

        const data = await response.json();
        return {
            success: true,
            entityId: data.id ?? data.entityId,
            entityName: data.name ?? data.entityName,
        };
    } catch (err) {
        return {
            success: false,
            error: err instanceof Error ? err.message : "NavMap lookup failed",
        };
    }
}

// ---------------------------------------------------------------------------
// Create document record
// ---------------------------------------------------------------------------

/**
 * Create a sprk_document record in Dataverse.
 *
 * Attempts Xrm.WebApi.createRecord first (same-origin, faster).
 * Falls back to authenticatedFetch to BFF if Xrm is unavailable.
 *
 * @param formValues   Document metadata from the wizard form
 * @param matterId     Optional related matter ID
 * @param driveItemIds Optional array of SPE drive item IDs from uploaded files
 * @returns Creation result
 */
export async function createDocumentRecord(
    formValues: IDocumentFormValues,
    matterId?: string,
    driveItemIds?: string[],
): Promise<ICreateDocumentResult> {
    // Build the record payload
    const record: Record<string, unknown> = {
        sprk_name: formValues.name,
        sprk_description: formValues.description || undefined,
    };

    if (formValues.documentType) {
        record.sprk_documenttype = formValues.documentType;
    }

    // Link to matter via lookup binding
    if (matterId) {
        record["sprk_MatterId@odata.bind"] = `/sprk_matters(${matterId})`;
    }

    // Store drive item IDs as JSON for linking
    if (driveItemIds && driveItemIds.length > 0) {
        record.sprk_driveitemids = JSON.stringify(driveItemIds);
    }

    // Strategy 1: Xrm.WebApi (preferred when available)
    const webApi = getXrmWebApi();
    if (webApi) {
        try {
            const result = await webApi.createRecord("sprk_document", record);
            return {
                success: true,
                documentId: result.id.replace(/[{}]/g, "").toLowerCase(),
            };
        } catch (err) {
            return {
                success: false,
                error: err instanceof Error ? err.message : "Failed to create document record via Xrm.WebApi",
            };
        }
    }

    // Strategy 2: BFF proxy
    try {
        const response = await authenticatedFetch("/api/obo/documents", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(record),
        });

        if (!response.ok) {
            const errorText = await response.text();
            return {
                success: false,
                error: `Failed to create document record (HTTP ${response.status}): ${errorText}`,
            };
        }

        const data = await response.json();
        return {
            success: true,
            documentId: data.id ?? data.documentId,
        };
    } catch (err) {
        return {
            success: false,
            error: err instanceof Error ? err.message : "Failed to create document record",
        };
    }
}
