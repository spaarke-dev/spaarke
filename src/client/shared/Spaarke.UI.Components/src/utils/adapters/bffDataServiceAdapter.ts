/**
 * BFF API Adapter for IDataService
 *
 * Bridges the Spaarke BFF API to the platform-agnostic IDataService interface,
 * enabling shared components to perform Dataverse CRUD operations in any
 * context (Code Page SPA, PCF, Office Add-in) by routing data operations
 * through the BFF API via an authenticated fetch function.
 *
 * Auth model (Spaarke Auth v2 — see ADR-027):
 *   The caller obtains `authenticatedFetch` from the `useAuth()` hook in
 *   `@spaarke/auth`. The adapter never sees raw access tokens — token
 *   acquisition, refresh, and `Authorization` header construction happen
 *   inside `authenticatedFetch`. This is the only canonical way to call
 *   the BFF from a Spaarke consumer; do NOT pass an `accessToken` string
 *   or build your own `Authorization: Bearer ...` headers.
 *
 * @see IDataService in ../../types/serviceInterfaces
 * @see ADR-012 - Shared Component Library
 * @see ADR-027 - Spaarke Auth Architecture (v2) — function-based contract
 *
 * @example
 * ```typescript
 * import { useAuth } from "@spaarke/auth";
 * import { createBffDataService } from "@spaarke/ui-components";
 *
 * const { authenticatedFetch } = useAuth();
 * const dataService = createBffDataService(
 *   authenticatedFetch,
 *   "https://spe-api-dev-67e2xz.azurewebsites.net"
 * );
 * const id = await dataService.createRecord("sprk_matter", { sprk_name: "Acme v. Beta" });
 * const record = await dataService.retrieveRecord("sprk_matter", id);
 * ```
 */

import type { IDataService } from '../../types/serviceInterfaces';

/**
 * Authenticated fetch function type.
 *
 * Structurally identical to `AuthenticatedFetchFn` exported by `@spaarke/auth`
 * (kept as a local type alias so this package has zero runtime dependency on
 * the auth library). The expected caller is the `authenticatedFetch` returned
 * by `useAuth()` — it acquires a token, attaches `Authorization: Bearer <jwt>`,
 * and handles silent refresh on 401.
 */
export type AuthenticatedFetch = (url: string, init?: RequestInit) => Promise<Response>;

/**
 * Creates an IDataService implementation backed by the Spaarke BFF API.
 *
 * Each CRUD operation maps to a RESTful BFF endpoint under `/api/dataverse/`.
 * The `authenticatedFetch` parameter handles token acquisition and attachment,
 * keeping this adapter free of auth concerns.
 *
 * @param authenticatedFetch - The `authenticatedFetch` returned by `useAuth()` from `@spaarke/auth`.
 *   Must NOT be a hand-rolled fetch wrapper that materializes a Bearer token —
 *   the canonical wrapper handles refresh and CAE claims-challenge replay.
 * @param bffBaseUrl - Base URL of the Spaarke BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net")
 * @returns An IDataService backed by the BFF API
 *
 * @example
 * ```typescript
 * // In a React component or hook:
 * import { useAuth } from "@spaarke/auth";
 * import { createBffDataService } from "@spaarke/ui-components";
 *
 * function MatterListPanel({ bffBaseUrl }: { bffBaseUrl: string }) {
 *   const { authenticatedFetch } = useAuth();
 *
 *   const dataService = useMemo(
 *     () => createBffDataService(authenticatedFetch, bffBaseUrl),
 *     [authenticatedFetch, bffBaseUrl]
 *   );
 *
 *   // Retrieve active matters — no token handling required at the call site
 *   const loadMatters = useCallback(async () => {
 *     const matters = await dataService.retrieveMultipleRecords(
 *       "sprk_matter",
 *       "?$select=sprk_name&$filter=statecode eq 0&$top=50"
 *     );
 *     console.log(`Found ${matters.entities.length} active matters`);
 *   }, [dataService]);
 *
 *   // ...
 * }
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
