/**
 * DataverseClient -- fetch()-based Dataverse Web API CRUD service
 *
 * Replaces PCF context.webAPI for the Code Page. All calls use
 * Bearer token from AuthService + standard OData Web API.
 *
 * Supports:
 *   - CRUD: createRecord, retrieveRecord, updateRecord, deleteRecord
 *   - Query: retrieveMultipleRecords (with OData $filter, $select, $expand, $orderby)
 *   - N:N: associate, disassociate
 *
 * @see spec.md - PCF Coupling Points to Replace (11 Total)
 */

import { getAccessToken, getClientUrl } from "./authService";

const LOG_PREFIX = "[PlaybookBuilder:DataverseClient]";

export interface DataverseRecord {
    [key: string]: unknown;
}

export interface RetrieveMultipleResult {
    entities: DataverseRecord[];
    nextLink?: string;
}

function getBaseUrl(): string {
    const clientUrl = getClientUrl();
    if (clientUrl) return `${clientUrl}/api/data/v9.2`;
    // Fallback: assume same origin (web resource on Dataverse form)
    return `${window.location.origin}/api/data/v9.2`;
}

async function fetchWithAuth(url: string, options: RequestInit = {}): Promise<Response> {
    const token = await getAccessToken();
    const response = await fetch(url, {
        ...options,
        headers: {
            "Authorization": `Bearer ${token}`,
            "Accept": "application/json",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
            "Content-Type": "application/json; charset=utf-8",
            ...options.headers,
        },
    });

    if (!response.ok) {
        const errorBody = await response.text().catch(() => "");
        console.error(`${LOG_PREFIX} ${options.method || "GET"} ${url} failed: ${response.status}`, errorBody);
        throw new Error(`Dataverse API ${response.status}: ${response.statusText}`);
    }

    return response;
}

/**
 * Create a new record in a Dataverse table.
 * @returns The ID of the created record (GUID without braces).
 */
export async function createRecord(entitySetName: string, data: DataverseRecord): Promise<string> {
    const url = `${getBaseUrl()}/${entitySetName}`;
    const response = await fetchWithAuth(url, {
        method: "POST",
        body: JSON.stringify(data),
        headers: { "Prefer": "return=representation" },
    });

    // Extract ID from OData-EntityId header or response body
    const entityIdHeader = response.headers.get("OData-EntityId");
    if (entityIdHeader) {
        const match = entityIdHeader.match(/\(([0-9a-f-]+)\)/i);
        if (match) return match[1];
    }

    const body = await response.json();
    const idKey = Object.keys(body).find(k => k.endsWith("id") && typeof body[k] === "string");
    if (idKey) return (body[idKey] as string).replace(/[{}]/g, "");

    throw new Error("Could not extract record ID from create response");
}

/**
 * Retrieve a single record by ID.
 */
export async function retrieveRecord(
    entitySetName: string,
    id: string,
    options?: string
): Promise<DataverseRecord> {
    const cleanId = id.replace(/[{}]/g, "");
    let url = `${getBaseUrl()}/${entitySetName}(${cleanId})`;
    if (options) url += `?${options}`;

    const response = await fetchWithAuth(url);
    return response.json();
}

/**
 * Update an existing record by ID.
 */
export async function updateRecord(
    entitySetName: string,
    id: string,
    data: DataverseRecord
): Promise<void> {
    const cleanId = id.replace(/[{}]/g, "");
    const url = `${getBaseUrl()}/${entitySetName}(${cleanId})`;
    await fetchWithAuth(url, {
        method: "PATCH",
        body: JSON.stringify(data),
    });
}

/**
 * Delete a record by ID.
 */
export async function deleteRecord(entitySetName: string, id: string): Promise<void> {
    const cleanId = id.replace(/[{}]/g, "");
    const url = `${getBaseUrl()}/${entitySetName}(${cleanId})`;
    await fetchWithAuth(url, { method: "DELETE" });
}

/**
 * Retrieve multiple records with OData query options.
 * @param queryOptions - OData query string (e.g., "$select=name&$filter=status eq 1&$orderby=name")
 */
export async function retrieveMultipleRecords(
    entitySetName: string,
    queryOptions?: string
): Promise<RetrieveMultipleResult> {
    let url = `${getBaseUrl()}/${entitySetName}`;
    if (queryOptions) url += `?${queryOptions}`;

    const response = await fetchWithAuth(url);
    const body = await response.json();

    return {
        entities: body.value ?? [],
        nextLink: body["@odata.nextLink"],
    };
}

/**
 * Associate two records via N:N relationship.
 * POST /{entitySetName}({id})/{navigationProperty}/$ref
 */
export async function associate(
    entitySetName: string,
    id: string,
    navigationProperty: string,
    relatedEntitySetName: string,
    relatedId: string
): Promise<void> {
    const cleanId = id.replace(/[{}]/g, "");
    const cleanRelatedId = relatedId.replace(/[{}]/g, "");
    const url = `${getBaseUrl()}/${entitySetName}(${cleanId})/${navigationProperty}/$ref`;
    await fetchWithAuth(url, {
        method: "POST",
        body: JSON.stringify({
            "@odata.id": `${getBaseUrl()}/${relatedEntitySetName}(${cleanRelatedId})`,
        }),
    });
}

/**
 * Disassociate two records from an N:N relationship.
 * DELETE /{entitySetName}({id})/{navigationProperty}({relatedId})/$ref
 */
export async function disassociate(
    entitySetName: string,
    id: string,
    navigationProperty: string,
    relatedId: string
): Promise<void> {
    const cleanId = id.replace(/[{}]/g, "");
    const cleanRelatedId = relatedId.replace(/[{}]/g, "");
    const url = `${getBaseUrl()}/${entitySetName}(${cleanId})/${navigationProperty}(${cleanRelatedId})/$ref`;
    await fetchWithAuth(url, { method: "DELETE" });
}
