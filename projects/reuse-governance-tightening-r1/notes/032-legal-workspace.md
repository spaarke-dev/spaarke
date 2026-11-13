# Task 032 — retire the `@spaarke/legal-workspace` facade

> Owner decision (2026-11-13, "Approve all"): retire the facade; SpaarkeAi depends on the LegalWorkspace solution directly. Plan: `notes/package-reviews.md` §9.

## Pre-checks

- Escalation trigger (another active project depends on the boundary): **not fired**. `spaarke-dataset-grid-framework-r2`, which created the facade (FR-10), is archived and has no worktree.
- Conflict check: the only open PR touching these files is #935 (stale draft; ADR-012 and the census test).

## What changed

| Area | Change |
|---|---|
| LegalWorkspace | `package.json` (name `legal-workspace`) gains `"types"` and `"exports": { ".": "./src/index.ts" }`. Barrel comments updated. |
| SpaarkeAi | Dependency `"@spaarke/legal-workspace": "file:../../client/shared/Spaarke.LegalWorkspace"` → `"legal-workspace": "file:../LegalWorkspace"`; `main.tsx` imports from `legal-workspace`; tsconfig path → `../LegalWorkspace/src/index.ts`; vite `resolveSharedLibDeps` list and react `include` globs drop the facade paths (the `../LegalWorkspace/src` entries stay). Lock regenerated; the leftover "extraneous" facade block removed by hand, `npm install` accepts it. |
| ai-widgets | Stray tsconfig path `@spaarke/legal-workspace` → a non-existent `index.d.ts` removed (P-11; nothing imported it). |
| Package | `src/client/shared/Spaarke.LegalWorkspace/` deleted (4 files, no tests). |
| Census | ADR-012 concise row removed + census-changes line; full ADR item 3 (7 grandfathered) + census-changes row; `SharedPackageCensusTests` entry removed. |
| Other references | `Build-AllClientComponents.ps1` exclusion notes; `code-page-deploy` skill dual-deploy section; docs `SPAARKEAI-COMPONENT-MODEL.md` (§1, §5 — also corrected: `WorkspaceLayoutWidget` does not import LegalWorkspace, `main.tsx` registers a renderer), `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md`, `LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`, `SPAARKEAI-COMPONENTIZATION-AUDIT.md`, `BUILD-A-NEW-WORKSPACE-WIDGET.md`, `docs/INDEX.md`; comments in ui-components `WorkspaceRenderer.ts` / `defaultWorkspaceRenderer.ts` and smart-todo `todo.ts`. Touched architecture docs carry a `Last Reviewed` stamp. |

## Risk check from the review ("Vite dedupe and transpile globs list both trees")

- npm does not install the link target's dependencies: after the change `npm install` reported "added 1 package, removed 1" and created no `LegalWorkspace/node_modules`, the same as the shared `file:` packages. CI (`deploy-spaarke-ai.yml`) installs only SpaarkeAi, so its install is unchanged.
- The `../LegalWorkspace/src` entries in `resolveSharedLibDeps` and the react `include` globs remain, so LegalWorkspace's bare imports still resolve to SpaarkeAi's `node_modules` and `dedupe` still forces one React / Fluent copy.

## Corrected claim (found in passing)

`code-page-deploy` said the SpaarkeAi ← LegalWorkspace dual-deploy trap was "RESOLVED" by FR-10. It was not, and is not now: SpaarkeAi bundles LegalWorkspace's source whether by alias, facade or `file:` dependency, so an edit there reaches users only when SpaarkeAi is rebuilt. On master it is covered by `deploy-spaarke-ai.yml`'s `src/solutions/LegalWorkspace/**` path trigger. The skill and the widget guide now say so. No code defect.

## Acceptance criteria

| Criterion | Result |
|---|---|
| Owner-approved action fully applied | Facade deleted; SpaarkeAi declares `legal-workspace`; LegalWorkspace exports `./src/index.ts`. |
| SpaarkeAi builds; existing tests pass | See "Verification". |
| `SharedPackageCensusTests` passes; row and entry match | 8/8 pass. |
| Negative: no `@spaarke/legal-workspace` in `src/` | Grep returns 0. |
| Existing contract only | No new tests; the import surface (`LegalWorkspaceApp`, `createLegalWorkspaceSectionRegistry`, `setLegalWorkspaceRuntimeConfig`) is unchanged and resolves to the same file. |

## Verification

- SpaarkeAi `npm run build` (html-reset check, `tsc-surface-gate`, Vite, ribbon): exit 0. SpaarkeAi `jest`: 137 suites, 1226 tests passed.
- LegalWorkspace `npm install` + `npm run build` (Vite): exit 0; its lock file unchanged.
- `dotnet test tests/Spaarke.ArchTests --filter SharedPackageCensus`: 8 passed.
- Grep `@spaarke/legal-workspace|Spaarke.LegalWorkspace` in `src/`: 0.

## Found in passing (pre-existing, not caused here) → task 034 files it

LegalWorkspace's standalone `tsc --noEmit -p .` reports 234 errors (174 in its own files). Nothing runs this check: LegalWorkspace's build is Vite-only, its standalone code page was retired (W-6), and SpaarkeAi's `tsc-surface-gate` checks only SpaarkeAi's own files. Most errors are hygiene (no jest/node types in its tsconfig, unused locals). Three deserve a look:
- `src/sections/index.ts:23` re-exports `ComposeWorkspacePlaceholder`, which `composeEditor.registration.ts` no longer exports. The barrel has no importers, so nothing breaks today; the first importer would fail to bundle.
- `src/sectionRegistry.ts:34` imports type `NarrateRequest`, which `@spaarke/ui-components` no longer exports (type-only, erased at build).
- `src/hooks/useWorkspaceLayouts.ts:62` and five `sections/*.registration.ts` deep-import `@spaarke/ai-widgets/...` paths that LegalWorkspace's tsconfig cannot resolve (Vite resolves them through SpaarkeAi's aliases).

My changes to LegalWorkspace (two `package.json` fields, comments) cannot affect these: LegalWorkspace never imports itself by package name.

## Quality gates (Step 9.5)

- code-review (inline, diff scope): no findings. Checked: the import surface is unchanged (`LegalWorkspaceApp`, `createLegalWorkspaceSectionRegistry`, `setLegalWorkspaceRuntimeConfig` from the same `src/index.ts`); the tsconfig path and the node_modules junction resolve to the same file; Vite keeps the `../LegalWorkspace/src` dependency-redirect and transpile entries; CI install unchanged (no link-target dependencies installed); docs now match the code.
- adr-check: ADR-012 compliant (a facade with one consumer that failed all three tests is retired; census row + test entry in the same change; a solution exporting its entry point is not a shared package); ADR-022 / ADR-021 / ADR-028 n/a (no runtime, UI or auth change).
- Adversarial pass: not run per task; NFR-03 sets one pass over items 1–4 (task 080).
