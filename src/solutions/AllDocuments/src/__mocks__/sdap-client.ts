/**
 * Minimal Jest mock for the `@spaarke/sdap-client` package.
 *
 * `@spaarke/sdap-client` ships from `dist/` as ESM which ts-jest's CommonJS
 * transform cannot consume. Every test transitively importing
 * `@spaarke/ui-components`'s `EntityCreationService` (via the barrel) fails
 * with `SyntaxError: Unexpected token 'export'` at dist/index.js parse time.
 *
 * The wizard unit tests do NOT exercise the SDAP indexing path directly —
 * they only need the import to resolve so the barrel side-effect chain
 * (Spaarke.UI.Components/src/index → /services/index → EntityCreationService)
 * completes. This stub returns no-op shapes for the symbols imported by
 * EntityCreationService.ts.
 *
 * Mirrors SpaarkeAi/src/__mocks__/sdap-client.ts — DEF-001 wiring for
 * spaarke-dataset-grid-framework-r2.
 *
 * @see jest.config.ts moduleNameMapper — wires this file to `@spaarke/sdap-client`
 */

// SdapApiClient — `EntityCreationService` instantiates this with a base URL.
// The unit tests don't exercise the resulting client, so a no-op stub suffices.
export class SdapApiClient {
  constructor(_opts?: unknown) {
    /* no-op */
  }

  indexFile(_req: unknown): Promise<unknown> {
    return Promise.resolve({});
  }
}

// Type-only exports — ts-jest erases these at transform time.
export type IndexFileRequest = Record<string, unknown>;
export type IndexFileResult = Record<string, unknown>;
