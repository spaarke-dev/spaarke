/**
 * Ambient shim for `@spaarke/sdap-client` (DI-028-02).
 *
 * external-spa consumes `@spaarke/ui-components` from source (via the vite alias).
 * One reachable shared module — `services/EntityCreationService.ts`, pulled into
 * the type graph as a TYPE-only dependency through
 * `PlaybookLibraryShell → analysisService` — statically imports
 * `@spaarke/sdap-client`. That package is a `file:` dependency of the shared
 * library and is intentionally NOT installed in external-spa's node_modules, so
 * tsc cannot resolve it while type-checking the shared source.
 *
 * At runtime this code path is tree-shaken out of the IIFE bundle (only the
 * `AuthenticatedFetchFn` type is consumed), so no real implementation is bundled.
 * This loose ambient declaration exists solely to satisfy the type-checker.
 */
declare module '@spaarke/sdap-client' {
  export type IndexFileRequest = any;
  export type IndexFileResult = any;
  export class SdapApiClient {
    constructor(options?: any);
    [key: string]: any;
  }
}
