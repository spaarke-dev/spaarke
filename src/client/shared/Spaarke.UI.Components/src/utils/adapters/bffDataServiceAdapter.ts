/**
 * BFF API Adapter for IDataService
 *
 * Bridges the Spaarke BFF API to the platform-agnostic IDataService interface,
 * enabling shared components to perform Dataverse CRUD operations in a Power
 * Pages SPA context where no Xrm object is available. All data operations are
 * routed through the BFF API via an authenticated fetch function.
 *
 * @see IDataService in ../../types/serviceInterfaces
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createBffDataService } from "@spaarke/ui-components";
 *
 * const dataService = createBffDataService(authenticatedFetch, "https://spe-api-dev-67e2xz.azurewebsites.net");
 * const id = await dataService.createRecord("sprk_matter", { sprk_name: "Acme v. Beta" });
 * const record = await dataService.retrieveRecord("sprk_matter", id);
 * ```
 */

import type { IDataService } from '../../types/serviceInterfaces';

/**
 * Authenticated fetch function type.
 *
 * In a Power Pages SPA context, authentication is handled externally
 * (e.g. via MSAL). The caller provides a fetch wrapper that automatically
 * attaches the Bearer token to outgoing requests.
 */
export type AuthenticatedFetch = (url: string, init?: RequestInit) => Promise<Response>;

/**
 * Creates an IDataService implementation backed by the Spaarke BFF API.
 *
 * Each CRUD operation maps to a RESTful BFF endpoint under `/api/dataverse/`.
 * The `authenticatedFetch` parameter handles token acquisition and attachment,
 * keeping this adapter free of auth concerns.
 *
 * @param authenticatedFetch - A fetch wrapper that attaches auth credentials
 * @param bffBaseUrl - Base URL of the Spaarke BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net")
 * @returns An IDataService backed by the BFF API
 *
 * @example
 * ```typescript
 * // With MSAL-based authenticated fetch
 * const authFetch: AuthenticatedFetch = async (url, init) => {
 *   const token = await msalInstance.acquireTokenSilent({ scopes: ["api://..."] });
 *   return fetch(url, {
 *     ...init,
 *     headers: { ...init?.headers, Authorization: `Bearer ${token.accessToken}` },
 *   });
 * };
 *
 * const dataService = createBffDataService(authFetch, "https://spe-api-dev-67e2xz.azurewebsites.net");
 *
 * // Retrieve active matters
 * const matters = await dataService.retrieveMultipleRecords(
 *   "sprk_matter",
 *   "?$select=sprk_name&$filter=statecode eq 0&$top=50"
 * );
 * console.log(`Found ${matters.entities.length} active matters`);
 * ```
 */
export function createBffDataService(
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string
): IDataService {
  const baseUrl = bffBaseUrl.replace(/\/+$/, '');

  /**
   * Builds the full BFF API URL for a Dataverse entity endpoint.
   *
   * @param entityName - Logical name of the entity
   * @param id - Optional record GUID
   * @param options - Optional OData query string (may or may not start with "?")
   */
  function buildUrl(entityName: string, id?: string, options?: string): string {
    let url = `${baseUrl}/api/dataverse/${encodeURIComponent(entityName)}`;
    if (id) {
      url += `/${encodeURIComponent(id)}`;
    }
    if (options) {
      // Ensure query string starts with "?"
      url += options.startsWith('?') ? options : `?${options}`;
    }
    return url;
  }

  /**
   * Checks a fetch response and throws a descriptive error on failure.
   */
  async function ensureOk(response: Response, operation: string): Promise<void> {
    if (!response.ok) {
      let detail = response.statusText;
      try {
        const body = await response.text();
        if (body) {
          detail = body;
        }
      } catch {
        // Ignore parse errors — use statusText
      }
      throw new Error(
        `BFF ${operation} failed with status ${response.status}: ${detail}`
      );
    }
  }

  return {
    async createRecord(
      entityName: string,
      data: Record<string, unknown>
    ): Promise<string> {
      const url = buildUrl(entityName);
      const response = await authenticatedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      await ensureOk(response, 'createRecord');

      const result = (await response.json()) as Record<string, unknown>;
      // BFF returns the created record ID — support common response shapes
      const id = result['id'] ?? result['Id'] ?? result['entityId'];
      if (typeof id !== 'string') {
        throw new Error(
          'BFF createRecord response did not contain a recognisable record ID'
        );
      }
      return id;
    },

    async retrieveRecord(
      entityName: string,
      id: string,
      options?: string
    ): Promise<Record<string, unknown>> {
      const url = buildUrl(entityName, id, options);
      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      await ensureOk(response, 'retrieveRecord');

      return (await response.json()) as Record<string, unknown>;
    },

    async retrieveMultipleRecords(
      entityName: string,
      options?: string
    ): Promise<{ entities: Record<string, unknown>[] }> {
      const url = buildUrl(entityName, undefined, options);
      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });
      await ensureOk(response, 'retrieveMultipleRecords');

      const result = (await response.json()) as Record<string, unknown>;
      // Support both { entities: [...] } and { value: [...] } response shapes
      const entities = (result['entities'] ?? result['value'] ?? []) as Record<
        string,
        unknown
      >[];
      return { entities };
    },

    async updateRecord(
      entityName: string,
      id: string,
      data: Record<string, unknown>
    ): Promise<void> {
      const url = buildUrl(entityName, id);
      const response = await authenticatedFetch(url, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data),
      });
      await ensureOk(response, 'updateRecord');
    },

    async deleteRecord(entityName: string, id: string): Promise<void> {
      const url = buildUrl(entityName, id);
      const response = await authenticatedFetch(url, {
        method: 'DELETE',
      });
      await ensureOk(response, 'deleteRecord');
    },
  };
}
