/**
 * Minimal Jest mock for the `@spaarke/sdap-client` package.
 *
 * `@spaarke/sdap-client` is NOT resolvable from this package's Jest workspace
 * (the package is only on the PCF / Office Add-in module-resolution paths).
 * Every test transitively importing `@spaarke/ui-components`'s
 * `EntityCreationService` (via the widget registries' `@spaarke/ui-components`
 * barrel import) fails with `Cannot find module '@spaarke/sdap-client'`.
 *
 * These unit tests do NOT exercise the SDAP indexing path directly — they
 * only need the import to resolve so the barrel side-effect chain
 * (Spaarke.UI.Components/src/index → /services/index → EntityCreationService)
 * completes. This stub returns no-op shapes for the symbols imported by
 * EntityCreationService.ts.
 *
 * Mirrors src/solutions/SpaarkeAi/src/__mocks__/sdap-client.ts verbatim (R6
 * Wave C-G3 origin). Added by ai-architecture-redesign-r2 task 021
 * (test-repair).
 *
 * @see jest.config.ts moduleNameMapper — wires this file to `@spaarke/sdap-client`
 */

// SdapApiClient — `EntityCreationService` instantiates this with a base URL.
// The unit tests don't exercise the resulting client, so a no-op stub suffices.
export class SdapApiClient {
  constructor(_opts?: unknown) {
    /* no-op */
  }

  // Type-erasure stub — production code calls `indexFile(...)`. Returns a
  // resolved Promise so any unawaited test-side reference doesn't reject.
  indexFile(_req: unknown): Promise<unknown> {
    return Promise.resolve({});
  }
}

// Type-only exports — `import type { IndexFileRequest, IndexFileResult }` —
// ts-jest erases these at transform time, so empty object types are
// sufficient.
export type IndexFileRequest = Record<string, unknown>;
export type IndexFileResult = Record<string, unknown>;
