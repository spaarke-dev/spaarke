/**
 * BFF API Adapter for IUploadService
 *
 * Bridges file upload operations to the Spaarke BFF API in any Spaarke
 * consumer context (Code Page SPA, PCF, Office Add-in). Authentication is
 * handled by the caller-provided `authenticatedFetch` function (from
 * `useAuth()` in `@spaarke/auth`).
 *
 * Uses XMLHttpRequest for upload progress reporting when an `onProgress`
 * callback is provided. XHR cannot use the `authenticatedFetch` wrapper,
 * so an optional `getBearerToken` callback is accepted — this is one of
 * the documented Auth v2 exception sites where the token is materialized
 * explicitly (see D-AUTH-7). Use `getAccessToken` from `useAuth()` here.
 *
 * @see IUploadService in ../../types/serviceInterfaces
 * @see ADR-007 - SpeFileStore Facade
 * @see ADR-012 - Shared Component Library
 * @see ADR-027 - Spaarke Auth Architecture (v2) — function-based contract
 *
 * @example
 * ```typescript
 * import { useAuth } from "@spaarke/auth";
 * import { createBffUploadService } from "@spaarke/ui-components";
 *
 * const { authenticatedFetch, getAccessToken } = useAuth();
 * const uploadService = createBffUploadService(
 *   authenticatedFetch,
 *   "https://spe-api-dev-67e2xz.azurewebsites.net",
 *   getAccessToken  // required only when callers may pass onProgress (XHR path)
 * );
 * const result = await uploadService.uploadFile(
 *   "sprk_matter", matterId, file,
 *   { onProgress: (loaded, total) => console.log(`${Math.round(loaded/total*100)}%`) }
 * );
 * ```
 */

import type { IUploadService, UploadOptions, UploadResult } from '../../types/serviceInterfaces';
import type { AuthenticatedFetch } from './bffDataServiceAdapter';

/**
 * Optional callback that returns a Bearer token string for XHR requests.
 *
 * When progress reporting is requested the adapter falls back to
 * XMLHttpRequest which cannot use the `authenticatedFetch` wrapper.
 * Provide this callback so the XHR can set its own Authorization header.
 *
 * Use `getAccessToken` returned from `useAuth()` in `@spaarke/auth`; this
 * is one of the audited D-AUTH-7 exception sites where the token must be
 * materialized as a string because the underlying transport (XHR) does not
 * accept a fetch wrapper.
 */
export type GetBearerToken = () => Promise<string>;

/**
 * Creates an IUploadService implementation backed by the Spaarke BFF API.
 *
 * File uploads go through the BFF because SharePoint Embedded storage
 * requires server-side Microsoft Graph API calls that cannot be made
 * from the browser.
 *
 * @param authenticatedFetch - The `authenticatedFetch` returned by `useAuth()` from `@spaarke/auth`.
 *   Used for the no-progress upload path and for `getContainerIdForEntity`.
 * @param bffBaseUrl - Base URL of the Spaarke BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net")
 * @param getBearerToken - Optional async function returning a Bearer token for XHR progress uploads.
 *   Pass `getAccessToken` from `useAuth()`. Required only when callers may supply `onProgress`.
 * @returns An IUploadService backed by the BFF API
 *
 * @example
 * ```typescript
 * // In a React component or hook:
 * import { useAuth } from "@spaarke/auth";
 * import { createBffUploadService } from "@spaarke/ui-components";
 *
 * function MatterUploadPanel({ bffBaseUrl, matterId }: { bffBaseUrl: string; matterId: string }) {
 *   const { authenticatedFetch, getAccessToken } = useAuth();
 *
 *   const uploadService = useMemo(
 *     () => createBffUploadService(authenticatedFetch, bffBaseUrl, getAccessToken),
 *     [authenticatedFetch, bffBaseUrl, getAccessToken]
 *   );
 *
 *   const handleUpload = useCallback(async (selectedFile: File) => {
 *     // Upload with progress tracking (uses XHR path internally)
 *     const result = await uploadService.uploadFile(
 *       "sprk_matter",
 *       matterId,
 *       selectedFile,
 *       {
 *         onProgress: (loaded, total) => setProgress(Math.round((loaded / total) * 100)),
 *         metadata: { category: "contract" },
 *       }
 *     );
 *
 *     // Retrieve the container ID for an entity (uses authenticatedFetch path)
 *     const containerId = await uploadService.getContainerIdForEntity("sprk_matter", matterId);
 *     console.log(`Uploaded ${result.name} into container ${containerId}`);
 *   }, [uploadService, matterId]);
 *
 *   // ...
 * }
 * ```
 */
export function createBffUploadService(
  authenticatedFetch: AuthenticatedFetch,
  bffBaseUrl: string,
  getBearerToken?: GetBearerToken
): IUploadService {
  const baseUrl = bffBaseUrl.replace(/\/+$/, '');

  return {
    uploadFile(entityName: string, entityId: string, file: File, options?: UploadOptions): Promise<UploadResult> {
      // ── Route corrected 2026-09-01 (unified-access-control-r2) ────────────────────────────────
      // This posted a multipart FormData to `${baseUrl}/api/documents/upload`, WHICH THE SERVER
      // DOES NOT SERVE — there is no such route, and the only `/upload` MapPost belongs to the
      // Compose group under a different prefix. The external-spa Document Upload page
      // (`external-spa/src/App.tsx:146` → `DocumentUploadPage.tsx:187`) is routed and shipped, so
      // every upload an external user attempted 404'd. Verified against
      // `src/server/api/Sprk.Bff.Api/Api/**` — no matching MapPost at any group prefix.
      //
      // Corrected to the record-keyed OBO route, which `OBOEndpoints.cs:34` marks as the TARGET:
      //     PUT /api/obo/records/{entityLogicalName}/{recordId}/files/{*path}
      // That route (`OBOEndpoints.cs:145`) derives the container FROM THE RECORD via
      // `RecordContainerResolver.ResolveForRecordAsync` and fails closed for a secure record with
      // no container of its own. This adapter's signature was ALREADY `(entityName, entityId, …)` —
      // the record-keyed shape — it was simply aimed at a URL that never existed.
      //
      // Do NOT "fix" this by pointing at `PUT /api/drives/{driveId}/upload` instead: that route
      // takes a CALLER-SUPPLIED drive id, which is the client-named-container defect this whole
      // project exists to remove.
      const path = encodeURIComponent(file.name);
      const url = `${baseUrl}/api/obo/records/${encodeURIComponent(entityName)}/${encodeURIComponent(entityId)}/files/${path}`;

      // The record-keyed route takes the RAW BYTES as the body — it has no multipart form and no
      // metadata field. `entityName`/`entityId` now travel in the URL rather than as form fields.
      if (options?.metadata) {
        // Refuse LOUDLY rather than dropping it. Silently discarding caller data is the exact
        // failure class this fix exists to correct, and no current caller passes metadata — so if
        // one starts, it should fail here and force a deliberate decision about where it goes.
        return Promise.reject(
          new Error(
            'bffUploadServiceAdapter: `metadata` is not supported by the record-keyed upload route ' +
              '(PUT /api/obo/records/{entity}/{id}/files/{path} accepts raw bytes only). It was ' +
              'previously sent as a multipart field to a route that did not exist, so it was never ' +
              'actually persisted. Add a server-side contract for it before passing it.'
          )
        );
      }

      // Use XMLHttpRequest when progress reporting is requested
      if (options?.onProgress) {
        return new Promise<UploadResult>((resolve, reject) => {
          const xhr = new XMLHttpRequest();
          xhr.open('PUT', url);

          // Attach auth token if a getter was provided
          const tokenPromise = getBearerToken ? getBearerToken() : Promise.resolve(undefined);

          tokenPromise
            .then(token => {
              if (token) {
                xhr.setRequestHeader('Authorization', `Bearer ${token}`);
              }
              xhr.setRequestHeader('Accept', 'application/json');

              xhr.upload.addEventListener('progress', event => {
                if (event.lengthComputable && options.onProgress) {
                  options.onProgress(event.loaded, event.total);
                }
              });

              xhr.addEventListener('load', () => {
                if (xhr.status >= 200 && xhr.status < 300) {
                  try {
                    const result = JSON.parse(xhr.responseText) as UploadResult;
                    resolve(result);
                  } catch {
                    reject(new Error('Failed to parse upload response'));
                  }
                } else {
                  reject(new Error(`Upload failed with status ${xhr.status}: ${xhr.statusText}`));
                }
              });

              xhr.addEventListener('error', () => {
                reject(new Error('Upload failed due to a network error'));
              });

              xhr.addEventListener('abort', () => {
                reject(new Error('Upload was aborted'));
              });

              // Raw bytes, not FormData — see the route note above.
              if (file.type) {
                xhr.setRequestHeader('Content-Type', file.type);
              }
              xhr.send(file);
            })
            .catch(err => {
              reject(
                new Error(
                  `Failed to acquire auth token for upload: ${err instanceof Error ? err.message : String(err)}`
                )
              );
            });
        });
      }

      // Simple authenticated fetch path when no progress callback is needed
      return authenticatedFetch(url, {
        method: 'PUT',
        headers: file.type ? { 'Content-Type': file.type } : undefined,
        body: file,
      }).then(async response => {
        if (!response.ok) {
          throw new Error(`Upload failed with status ${response.status}: ${response.statusText}`);
        }
        return (await response.json()) as UploadResult;
      });
    },

    async getContainerIdForEntity(entityName: string, entityId: string): Promise<string> {
      const url = `${baseUrl}/api/containers/${encodeURIComponent(entityName)}/${encodeURIComponent(entityId)}`;

      const response = await authenticatedFetch(url, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to retrieve container ID (${response.status}): ${response.statusText}`);
      }

      const data = (await response.json()) as { containerId: string };
      return data.containerId;
    },
  };
}
