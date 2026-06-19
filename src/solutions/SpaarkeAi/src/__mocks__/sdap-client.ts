/**
 * Minimal Jest mock for the `@spaarke/sdap-client` package.
 *
 * `@spaarke/sdap-client` is NOT resolvable from the SpaarkeAi Jest workspace
 * (the package is only on the PCF / Office Add-in module-resolution paths).
 * Every test transitively importing `@spaarke/ui-components`'s
 * `EntityCreationService` (and the other CreateXxxWizard service files) fails
 * with `Cannot find module '@spaarke/sdap-client'`.
 *
 * The SpaarkeAi unit tests do NOT exercise the SDAP indexing path directly
 * — they only need the import to resolve so the barrel side-effect chain
 * (Spaarke.UI.Components/src/index → /services/index → EntityCreationService)
 * completes. This stub returns no-op shapes for the symbols imported by
 * EntityCreationService.ts (and the two wizard service files using the same
 * import surface).
 *
 * Added by R6 Wave C-G3 gap-fill (task 057 — PinToMatterButton.test.tsx and
 * the other two affordance tests previously committed in 2677f9439 without
 * test verification). Mirrors the existing `marked.ts` mock pattern.
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
