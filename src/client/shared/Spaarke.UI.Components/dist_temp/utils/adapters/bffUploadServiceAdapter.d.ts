/**
 * BFF API Adapter for IUploadService
 *
 * Bridges file upload operations to the Spaarke BFF API in a Power Pages SPA
 * context where no Xrm object is available. Authentication is handled by the
 * caller-provided `authenticatedFetch` function.
 *
 * Uses XMLHttpRequest for upload progress reporting when an `onProgress`
 * callback is provided, with a token-extraction hook so the XHR request
 * carries the same credentials as the authenticated fetch.
 *
 * @see IUploadService in ../../types/serviceInterfaces
 * @see ADR-007 - SpeFileStore Facade
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createBffUploadService } from "@spaarke/ui-components";
 *
 * const uploadService = createBffUploadService(authenticatedFetch, "https://spe-api-dev-67e2xz.azurewebsites.net");
 * const result = await uploadService.uploadFile(
 *   "sprk_matter", matterId, file,
 *   { onProgress: (loaded, total) => console.log(`${Math.round(loaded/total*100)}%`) }
 * );
 * ```
 */
import type { IUploadService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetch } from './bffDataServiceAdapter';
/**
 * Optional callback that returns a Bearer token string for XHR requests.
 *
 * When progress reporting is requested the adapter falls back to
 * XMLHttpRequest which cannot use the `authenticatedFetch` wrapper.
 * Provide this callback so the XHR can set its own Authorization header.
 */
export type GetBearerToken = () => Promise<string>;
/**
 * Creates an IUploadService implementation backed by the Spaarke BFF API.
 *
 * File uploads go through the BFF because SharePoint Embedded storage
 * requires server-side Microsoft Graph API calls that cannot be made
 * from the browser.
 *
 * @param authenticatedFetch - A fetch wrapper that attaches auth credentials
 * @param bffBaseUrl - Base URL of the Spaarke BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net")
 * @param getBearerToken - Optional async function returning a Bearer token for XHR progress uploads
 * @returns An IUploadService backed by the BFF API
 *
 * @example
 * ```typescript
 * // With MSAL-based authenticated fetch and token getter
 * const authFetch: AuthenticatedFetch = async (url, init) => {
 *   const token = await msalInstance.acquireTokenSilent({ scopes: ["api://..."] });
 *   return fetch(url, {
 *     ...init,
 *     headers: { ...init?.headers, Authorization: `Bearer ${token.accessToken}` },
 *   });
 * };
 *
 * const getToken = async () => {
 *   const result = await msalInstance.acquireTokenSilent({ scopes: ["api://..."] });
 *   return result.accessToken;
 * };
 *
 * const uploadService = createBffUploadService(authFetch, "https://spe-api-dev-67e2xz.azurewebsites.net", getToken);
 *
 * // Upload with progress tracking
 * const result = await uploadService.uploadFile(
 *   "sprk_matter",
 *   "00000000-0000-0000-0000-000000000001",
 *   selectedFile,
 *   {
 *     onProgress: (loaded, total) => setProgress(Math.round((loaded / total) * 100)),
 *     metadata: { category: "contract" },
 *   }
 * );
 *
 * // Retrieve the container ID for an entity
 * const containerId = await uploadService.getContainerIdForEntity("sprk_matter", matterId);
 * ```
 */
export declare function createBffUploadService(authenticatedFetch: AuthenticatedFetch, bffBaseUrl: string, getBearerToken?: GetBearerToken): IUploadService;
//# sourceMappingURL=bffUploadServiceAdapter.d.ts.map