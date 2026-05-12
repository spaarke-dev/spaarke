/**
 * OData Dataverse Client
 *
 * IDataverseClient implementation that uses direct OData/Web API fetch calls
 * with token-based authentication. Designed for Code Pages (React 18 standalone
 * pages) that do not have access to ComponentFramework.WebApi.
 *
 * Authentication: Uses ITokenProvider to obtain Dataverse access tokens.
 *
 * @version 1.0.0
 */
import type { IDataverseClient, ITokenProvider, ILogger, DataverseRecordRef } from './types';
/**
 * Configuration for ODataDataverseClient.
 */
export interface ODataDataverseClientOptions {
    /**
     * Dataverse environment base URL.
     * Example: "https://spaarkedev1.crm.dynamics.com"
     */
    dataverseUrl: string;
    /**
     * Token provider that returns a Dataverse access token.
     * The token must have scope for the Dataverse environment
     * (e.g., "https://spaarkedev1.crm.dynamics.com/.default").
     */
    getAccessToken: ITokenProvider;
    /** OData API version (default: "v9.2") */
    apiVersion?: string;
    /** Logger implementation */
    logger?: ILogger;
}
/**
 * Dataverse client backed by direct OData/Web API fetch calls.
 *
 * Usage (Code Page):
 * ```typescript
 * const client = new ODataDataverseClient({
 *     dataverseUrl: 'https://spaarkedev1.crm.dynamics.com',
 *     getAccessToken: () => msalInstance.acquireTokenSilent({ scopes: ['...'] }).then(r => r.accessToken),
 * });
 * const result = await client.createRecord('sprk_document', payload);
 * ```
 */
export declare class ODataDataverseClient implements IDataverseClient {
    private readonly baseApiUrl;
    private readonly getAccessToken;
    private readonly logger;
    constructor(options: ODataDataverseClientOptions);
    /**
     * Create a record via OData POST request.
     *
     * Uses the OData-EntityId response header to extract the created record GUID.
     */
    createRecord(entityLogicalName: string, data: Record<string, unknown>): Promise<DataverseRecordRef>;
    /**
     * Update a record via OData PATCH request.
     */
    updateRecord(entityLogicalName: string, id: string, data: Record<string, unknown>): Promise<void>;
    /**
     * Derive entity set name from logical name.
     *
     * Simple pluralization: appends 's' (works for all Spaarke custom entities
     * and standard entities like account/accounts, contact/contacts).
     */
    private getEntitySetName;
    /**
     * Extract record ID from the OData response.
     *
     * Tries:
     * 1. Response body JSON (when Prefer: return=representation is used)
     * 2. OData-EntityId response header
     */
    private extractRecordId;
    /**
     * Try to read error body for diagnostics.
     */
    private tryReadErrorBody;
}
//# sourceMappingURL=ODataDataverseClient.d.ts.map