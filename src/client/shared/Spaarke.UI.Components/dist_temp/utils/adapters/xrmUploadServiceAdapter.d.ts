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
import type { IUploadService } from '../../types/serviceInterfaces';
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
export declare function createXrmUploadService(bffBaseUrl: string): IUploadService;
//# sourceMappingURL=xrmUploadServiceAdapter.d.ts.map