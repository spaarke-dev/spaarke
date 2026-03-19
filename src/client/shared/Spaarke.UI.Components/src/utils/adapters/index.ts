/**
 * Adapter Barrel Exports
 *
 * Factory functions that create platform-agnostic service implementations
 * backed by either the Xrm runtime APIs (Dataverse-hosted context) or
 * the BFF API (Power Pages SPA context).
 *
 * @example Xrm adapters (Code Pages, PCF controls):
 * ```typescript
 * import {
 *   createXrmDataService,
 *   createXrmUploadService,
 *   createXrmNavigationService,
 * } from "@spaarke/ui-components";
 *
 * const dataService = createXrmDataService();
 * const uploadService = createXrmUploadService("https://spe-api-dev-67e2xz.azurewebsites.net");
 * const navService = createXrmNavigationService();
 * ```
 *
 * @example BFF adapters (Power Pages SPA):
 * ```typescript
 * import {
 *   createBffDataService,
 *   createBffUploadService,
 *   createBffNavigationService,
 * } from "@spaarke/ui-components";
 *
 * const dataService = createBffDataService(authenticatedFetch, "https://spe-api-dev-67e2xz.azurewebsites.net");
 * const uploadService = createBffUploadService(authenticatedFetch, "https://spe-api-dev-67e2xz.azurewebsites.net");
 * const navService = createBffNavigationService((path) => router.push(path));
 * ```
 */

// Xrm adapters (Dataverse-hosted context)
export { createXrmDataService } from './xrmDataServiceAdapter';
export { createXrmUploadService } from './xrmUploadServiceAdapter';
export { createXrmNavigationService } from './xrmNavigationServiceAdapter';

// BFF API adapters (Power Pages SPA context)
export { createBffDataService } from './bffDataServiceAdapter';
export type { AuthenticatedFetch } from './bffDataServiceAdapter';
export { createBffUploadService } from './bffUploadServiceAdapter';
export type { GetBearerToken } from './bffUploadServiceAdapter';
export { createBffNavigationService } from './bffNavigationServiceAdapter';
export type {
  NavigateFunction,
  DialogRenderer,
  DialogCloser,
} from './bffNavigationServiceAdapter';
