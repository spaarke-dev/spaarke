/**
 * @spaarke/legal-workspace
 *
 * Shared package boundary for the LegalWorkspace section registry.
 *
 * R2 FR-10 (spaarke-dataset-grid-framework-r2, 2026-07-02):
 *  - Eliminates the SpaarkeAi ← LegalWorkspace source-alias trap by inserting
 *    a proper package boundary between the two solutions.
 *  - Strategy: RE-EXPORT (see notes/fr10-migration-strategy.md).
 *
 * R2 task 020 — initial scaffold. No exports yet.
 * R2 task 021 — populates this file with re-exports of files that stay under
 *   src/solutions/LegalWorkspace/src/ (sectionRegistry.ts + 6 section
 *   registration modules + LegalWorkspaceApp).
 * R2 task 022 — re-points SpaarkeAi's `@spaarke/legal-workspace` alias from
 *   `../LegalWorkspace/src` to `../../client/shared/Spaarke.LegalWorkspace/src`.
 *
 * Architecture:
 *  - Fluent UI v9 only (ADR-021).
 *  - Auth via `@spaarke/auth` (ADR-028).
 *  - React 16-19 peer-compatible via peerDependencies (ADR-022) — matches
 *    Spaarke.DailyBriefing.Components structural template.
 *  - SSOT for LegalWorkspace-scoped surfaces (ADR-012) — distinct from
 *    `@spaarke/ui-components` (cross-domain shared UI).
 */

// -----------------------------------------------------------------------------
// R2 task 021 (FR-10) — RE-EXPORT population
// -----------------------------------------------------------------------------
//
// Strategy: RE-EXPORT (see projects/spaarke-dataset-grid-framework-r2/notes/
// fr10-migration-strategy.md). Physical files stay at
//   src/solutions/LegalWorkspace/src/
// and this package provides a facade so consumers (SpaarkeAi in task 022) can
// import via `@spaarke/legal-workspace` without knowing about the underlying
// solution path.
//
// The re-export target is the LegalWorkspace public barrel
// (`src/solutions/LegalWorkspace/src/index.ts`), which already curates the
// public surface consumers need:
//   - `LegalWorkspaceApp` (+ type `ILegalWorkspaceAppProps`)
//   - `LegalWorkspaceRenderer` (WorkspaceRenderer-typed binding)
//   - `setLegalWorkspaceRuntimeConfig`
//   - Section-registry factories: `SECTION_REGISTRY`,
//     `createLegalWorkspaceSectionRegistry`, `getSectionById`,
//     `getSectionsByCategory`
//   - Type `LegalWorkspaceSectionRegistryOptions`
//   - Type `SectionRegistration` (re-export from `@spaarke/ui-components`)
//
// Re-exporting the barrel guarantees byte-for-byte parity with the alias
// SpaarkeAi consumes today (`@spaarke/legal-workspace` → LegalWorkspace/src).
// Task 022 flips the alias path; consumer import sites do not change.
//
// -----------------------------------------------------------------------------
// Standalone-build behavior (2026-07-02, task 021) — matches DailyBriefing precedent
// -----------------------------------------------------------------------------
//
// This package's `npm run build` (`tsc --noEmit`) DOES NOT succeed standalone
// once the re-export below is in place. Reason: `export * from '...'` causes
// tsc to follow the LegalWorkspace source tree, which imports
// `@spaarke/ui-components`, `@spaarke/auth`, `@spaarke/ai-widgets`, etc. Those
// packages are declared as **peerDependencies** (not regular deps) so they are
// NOT installed under this package's `node_modules/`, and the peer deps'
// tsconfig `paths` (which resolve `@spaarke/*` aliases in the consumer
// solutions) are not in scope here. tsc reports TS2307 + duplicate-type errors
// from the LegalWorkspace tree's own `node_modules/@fluentui/react-icons`.
//
// This is the SAME accepted pattern as `Spaarke.DailyBriefing.Components`
// (source-only lib with `@spaarke/*` peerDependencies + `tsc --noEmit`) —
// documented in `scripts/Build-AllClientComponents.ps1` (line 8) as
// "intentionally excluded" from the orchestrator's `$SharedLibs` list. Task
// 024 excludes `Spaarke.LegalWorkspace` from that list on the same basis.
//
// Where type-checking IS performed:
//   - SpaarkeAi's `tsc` pass (after task 022 flips the alias) — full type-check
//     of the re-exported surface via SpaarkeAi's `tsconfig.json` paths.
//   - Standalone LegalWorkspace's `tsc` pass (unchanged) — LegalWorkspace
//     continues to consume its own `sectionRegistry.ts` via local relative
//     import; the re-export here is purely a package facade for outsiders.
//
// Runtime resolution:
//   - Vite (SpaarkeAi) resolves this package's `main` field (`./src/index.ts`)
//     via the alias task 022 configures, then follows the `export *` to the
//     LegalWorkspace source. LegalWorkspace's own `@spaarke/*` alias paths are
//     applied by Vite's resolver because SpaarkeAi's vite.config.ts already
//     declares them. Zero behavioral change vs the pre-task-022 source alias.

export * from '../../../../solutions/LegalWorkspace/src/index.ts';
