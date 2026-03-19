/**
 * Xrm Upload Service Adapter for IUploadService
 *
 * Bridges file upload operations to the Spaarke BFF API, which handles
 * SharePoint Embedded (SPE) storage. Unlike data operations that go through
 * Xrm.WebApi, file uploads are routed through the BFF because SPE storage
 * requires server-side Graph API calls.
 *
 * Uses XMLHttpRequest for upload progress reporting when an `onProgress`
 * callback is provided; falls back to fetch for simpler requests.
 *
 * @see IUploadService in ../../types/serviceInterfaces
 * @see ADR-007 - SpeFileStore Facade
 * @see ADR-012 - Shared Component Library
 *
 * @example
 * ```typescript
 * import { createXrmUploadService } from "@spaarke/ui-components";
 *
 * const uploadService = createXrmUploadService("https://spe-api-dev-67e2xz.azurewebsites.net");
 * const result = await uploadService.uploadFile(
 *   "sprk_matter", matterId, file,
 *   { onProgress: (loaded, total) => console.log(`${Math.round(loaded/total*100)}%`) }
 * );
 * ```
 */

import type {
  IUploadService,
  UploadOptions,
  UploadResult,
} from '../../types/serviceInterfaces';
import { getXrm } from '../xrmContext';

/**
 * Attempts to retrieve a Bearer token from the Xrm global context.
 *
 * In Dataverse-hosted contexts the CRM token is available via the client URL
 * cookie. We use the Xrm.Utility.getGlobalContext() to build the auth header.
 *
 * @returns A Bearer token string, or undefined when not available
 */
function getBearerToken(): string | undefined {
  try {
    const xrm = getXrm();
    if (xrm?.Utility) {
      const globalContext = xrm.Utility.getGlobalContext();
      // In Dataverse-hosted context, authentication is cookie-based for
      // same-origin BFF requests. For cross-origin BFF calls the token
      // is acquired via the global context. Return the client URL so the
      // caller can decide how to authenticate.
      return globalContext.getClientUrl();
    }
  } catch {
    // Token retrieval failed — caller should handle gracefully
  }
  return undefined;
}

/**
 * Builds standard headers for BFF API requests.
 *
 * @param clientUrl - The Dataverse client URL (used for cookie-based auth context)
 * @returns Headers object for fetch / XHR calls
 */
function buildHeaders(clientUrl?: string): Record<string, string> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
  };
  if (clientUrl) {
    headers['X-Dataverse-Url'] = clientUrl;
  }
  return headers;
}

/**
 * Creates an IUploadService implementation that delegates to the Spaarke BFF API.
 *
 * File uploads go through the BFF rather than Xrm.WebApi because SharePoint
 * Embedded storage requires server-side Microsoft Graph API calls that cannot
 * be made from the browser.
 *
 * @param bffBaseUrl - Base URL of the Spaarke BFF API (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net")
 * @returns An IUploadService backed by the BFF API
 *
 * @example
 * ```typescript
 * const uploadService = createXrmUploadService("https://spe-api-dev-67e2xz.azurewebsites.net");
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
export function createXrmUploadService(bffBaseUrl: string): IUploadService {
  const baseUrl = bffBaseUrl.replace(/\/+$/, '');

  return {
    uploadFile(
      entityName: string,
      entityId: string,
      file: File,
      options?: UploadOptions
    ): Promise<UploadResult> {
      const url = `${baseUrl}/api/documents/upload`;
      const clientUrl = getBearerToken();
      const headers = buildHeaders(clientUrl);

      const formData = new FormData();
      formData.append('file', file);
      formData.append('entityName', entityName);
      formData.append('entityId', entityId);

      if (options?.metadata) {
        formData.append('metadata', JSON.stringify(options.metadata));
      }

      // Use XMLHttpRequest when progress reporting is requested
      if (options?.onProgress) {
        return new Promise<UploadResult>((resolve, reject) => {
          const xhr = new XMLHttpRequest();
          xhr.open('POST', url);

          // Set headers (do NOT set Content-Type — FormData sets boundary automatically)
          Object.entries(headers).forEach(([key, value]) => {
            xhr.setRequestHeader(key, value);
          });
          xhr.withCredentials = true;

          xhr.upload.addEventListener('progress', (event) => {
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
              reject(
                new Error(`Upload failed with status ${xhr.status}: ${xhr.statusText}`)
              );
            }
          });

          xhr.addEventListener('error', () => {
            reject(new Error('Upload failed due to a network error'));
          });

          xhr.addEventListener('abort', () => {
            reject(new Error('Upload was aborted'));
          });

          xhr.send(formData);
        });
      }

      // Simple fetch path when no progress callback is needed
      return fetch(url, {
        method: 'POST',
        headers,
        credentials: 'include',
        body: formData,
      }).then(async (response) => {
        if (!response.ok) {
          throw new Error(
            `Upload failed with status ${response.status}: ${response.statusText}`
          );
        }
        return (await response.json()) as UploadResult;
      });
    },

    async getContainerIdForEntity(
      entityName: string,
      entityId: string
    ): Promise<string> {
      const url = `${baseUrl}/api/containers/${encodeURIComponent(entityName)}/${encodeURIComponent(entityId)}`;
      const clientUrl = getBearerToken();
      const headers = buildHeaders(clientUrl);

      const response = await fetch(url, {
        method: 'GET',
        headers,
        credentials: 'include',
      });

      if (!response.ok) {
        throw new Error(
          `Failed to retrieve container ID (${response.status}): ${response.statusText}`
        );
      }

      const data = (await response.json()) as { containerId: string };
      return data.containerId;
    },
  };
}
