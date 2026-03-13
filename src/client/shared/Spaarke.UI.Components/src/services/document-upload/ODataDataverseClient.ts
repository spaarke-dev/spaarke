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

import type {
  IDataverseClient,
  ITokenProvider,
  ILogger,
  DataverseRecordRef,
} from "./types";
import { consoleLogger } from "./types";

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
export class ODataDataverseClient implements IDataverseClient {
  private readonly baseApiUrl: string;
  private readonly getAccessToken: ITokenProvider;
  private readonly logger: ILogger;

  constructor(options: ODataDataverseClientOptions) {
    const dataverseUrl = options.dataverseUrl.endsWith("/")
      ? options.dataverseUrl.slice(0, -1)
      : options.dataverseUrl;
    const apiVersion = options.apiVersion ?? "v9.2";

    this.baseApiUrl = `${dataverseUrl}/api/data/${apiVersion}`;
    this.getAccessToken = options.getAccessToken;
    this.logger = options.logger ?? consoleLogger;

    this.logger.info("ODataDataverseClient", "Initialized", {
      baseApiUrl: this.baseApiUrl,
    });
  }

  /**
   * Create a record via OData POST request.
   *
   * Uses the OData-EntityId response header to extract the created record GUID.
   */
  async createRecord(
    entityLogicalName: string,
    data: Record<string, unknown>,
  ): Promise<DataverseRecordRef> {
    const entitySetName = this.getEntitySetName(entityLogicalName);
    const url = `${this.baseApiUrl}/${entitySetName}`;

    this.logger.info(
      "ODataDataverseClient",
      `Creating ${entityLogicalName} record`,
      { url },
    );

    const token = await this.getAccessToken();

    const response = await fetch(url, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
        Accept: "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
        Prefer: "return=representation",
      },
      body: JSON.stringify(data),
    });

    if (!response.ok) {
      const errorBody = await this.tryReadErrorBody(response);
      throw new Error(
        `Failed to create ${entityLogicalName} record: HTTP ${response.status} ${response.statusText}. ${errorBody}`,
      );
    }

    // Extract record ID from response
    const id = await this.extractRecordId(response, entityLogicalName);

    this.logger.info(
      "ODataDataverseClient",
      `Created ${entityLogicalName} record: ${id}`,
    );

    return { id };
  }

  /**
   * Update a record via OData PATCH request.
   */
  async updateRecord(
    entityLogicalName: string,
    id: string,
    data: Record<string, unknown>,
  ): Promise<void> {
    const entitySetName = this.getEntitySetName(entityLogicalName);
    const sanitizedId = id.replace(/[{}]/g, "").toLowerCase();
    const url = `${this.baseApiUrl}/${entitySetName}(${sanitizedId})`;

    this.logger.info(
      "ODataDataverseClient",
      `Updating ${entityLogicalName} record: ${sanitizedId}`,
      { url },
    );

    const token = await this.getAccessToken();

    const response = await fetch(url, {
      method: "PATCH",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
        Accept: "application/json",
        "OData-MaxVersion": "4.0",
        "OData-Version": "4.0",
      },
      body: JSON.stringify(data),
    });

    if (!response.ok) {
      const errorBody = await this.tryReadErrorBody(response);
      throw new Error(
        `Failed to update ${entityLogicalName} record ${sanitizedId}: HTTP ${response.status} ${response.statusText}. ${errorBody}`,
      );
    }

    this.logger.info(
      "ODataDataverseClient",
      `Updated ${entityLogicalName} record: ${sanitizedId}`,
    );
  }

  // -----------------------------------------------------------------------
  // Private helpers
  // -----------------------------------------------------------------------

  /**
   * Derive entity set name from logical name.
   *
   * Simple pluralization: appends 's' (works for all Spaarke custom entities
   * and standard entities like account/accounts, contact/contacts).
   */
  private getEntitySetName(entityLogicalName: string): string {
    // Standard Dataverse convention: entity set = logical name + 's'
    return `${entityLogicalName}s`;
  }

  /**
   * Extract record ID from the OData response.
   *
   * Tries:
   * 1. Response body JSON (when Prefer: return=representation is used)
   * 2. OData-EntityId response header
   */
  private async extractRecordId(
    response: Response,
    entityLogicalName: string,
  ): Promise<string> {
    // Try response body first (Prefer: return=representation)
    try {
      const body = await response.json();
      const primaryKey = `${entityLogicalName}id`;
      if (body[primaryKey]) {
        return body[primaryKey];
      }
      // Some responses use generic 'id' field
      if (body.id) {
        return body.id;
      }
    } catch {
      // Fall through to header-based extraction
    }

    // Try OData-EntityId header
    const entityIdHeader = response.headers.get("OData-EntityId");
    if (entityIdHeader) {
      const match = entityIdHeader.match(/\(([0-9a-f-]+)\)/i);
      if (match) {
        return match[1];
      }
    }

    throw new Error(
      `Could not extract record ID from ${entityLogicalName} create response`,
    );
  }

  /**
   * Try to read error body for diagnostics.
   */
  private async tryReadErrorBody(response: Response): Promise<string> {
    try {
      const errorData = await response.json();
      return errorData?.error?.message || JSON.stringify(errorData);
    } catch {
      try {
        return await response.text();
      } catch {
        return "";
      }
    }
  }
}
