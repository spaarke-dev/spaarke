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
export declare function createBffDataService(authenticatedFetch: AuthenticatedFetch, bffBaseUrl: string): IDataService;
//# sourceMappingURL=bffDataServiceAdapter.d.ts.map