# Task 033 — consolidate `@spaarke/document-operations` into `@spaarke/ui-components`

> Owner decision (2026-11-13, "Approve all"): move the hook into `@spaarke/ui-components` and retire the package. Plan: `notes/package-reviews.md` §6.

## Pre-checks

- Conflict check: the only open PR touching these files is #935 (stale draft; ADR-012 and the census test), already reported.
- ADR-022 escalation trigger (hook needs a React 18+ API): **not fired**. The hook imports only `useState` and `useCallback`; a grep of the moved files for `useTransition|useDeferredValue|useId|useSyncExternalStore|useInsertionEffect` returns 0.
- PCF-bundle escalation trigger (build fails or grows > 10%): **not fired** — see "PCF bundle".

## What changed

| Area | Change |
|---|---|
| ui-components | `useDocumentActions.ts` and its test moved with `git mv` to `src/hooks/` and `src/hooks/__tests__/` (code unchanged; header comments updated, test import now `../useDocumentActions`). Exported from `src/hooks/index.ts`, so from the main barrel; **not** from `pcf-safe.ts`. ui-components already depended on `@spaarke/auth`, so no new dependency edge. |
| SemanticSearch | `App.tsx` imports `@spaarke/ui-components/hooks/useDocumentActions`, the deep-import pattern it already uses for `utils/xrmContext` etc. Its jest config maps that one file (a blanket barrel mapping would pull in ESM-only `d3-force`, per the existing comment). Dependency, tsconfig path and webpack alias for the package removed; lock regenerated, extraneous block removed by hand. |
| Compose | `ComposeWorkspace.tsx` adds `useDocumentActions` to its existing `@spaarke/ui-components` import; `ComposeToolbar.tsx` imports it from there. Dependency and tsconfig paths removed; lock cleaned. Tests: the six files that stubbed the package (`unmountFlush`, `bornInEditorSave`, `renderOnSave`, `saveErrorRouting`, `saveLifecycle`, `saveOpLogPreservation`) now carry the same stub inside their existing full-replacement `@spaarke/ui-components` mock. `openInWordError` takes the real hook from ui-components source (was the package source). `changeSummary`, `imports` and `search` replace ui-components wholesale but previously got the real hook unmocked from the package, so they now take the real hook from ui-components source — same behaviour as before. Comments in `ComposeEditor`, `activeDocShare`, `upload`, `README`, `jest.config.js` updated. |
| SpaarkeAi | Dependency, tsconfig paths, jest mappings and all three vite entries (dependency-redirect list, react `include` globs, `resolve.alias`) removed; lock cleaned. `__tests__/compose/ComposeToolbar.test.tsx` mocks `@spaarke/ui-components` (ComposeToolbar imports only the hook from it). |
| LegalWorkspace | The three vite entries added by unified-access-control-r2 for the transitive dependency removed; `@spaarke/ui-components` is already aliased to source. |
| Package | `src/client/shared/Spaarke.DocumentOperations/` deleted (its `@spaarke/auth` junction removed first so the delete could not follow it). |
| Census | ADR-012 concise row removed + census-changes line; full ADR item 3 (6 grandfathered) + census-changes row; `SharedPackageCensusTests` entry replaced by a retirement comment. |
| Other references | `ci-standalone-checks.yml` compose-client-gate no longer installs/builds the deleted package (the next run would have failed); `Build-AllClientComponents.ps1` list (12 libraries, matches disk) and the Compose dependency comment; client-lint package list; `CAPABILITY-INDEX.md` row 29 path. |

Side effect worth knowing: `deploy-spaarke-ai.yml` triggers on `src/client/shared/Spaarke.UI.Components/**` but never listed the old package, so an edit to the hook did not redeploy SpaarkeAi. It now does.

## PCF bundle (POML constraint)

ScopeConfigEditor (imports `@spaarke/ui-components` main barrel), `npm run build:prod`:
- Before: 338,693 bytes. After (ui-components `dist` rebuilt with the hook exported from `hooks/index.js`): 338,693 bytes. Delta 0 — tree-shaken (`sideEffects: false`); the string `open-links` does not occur in the bundle.
- The install for this measurement rewrote ScopeConfigEditor's lock; reverted, not committed.

## Acceptance criteria

| Criterion | Result |
|---|---|
| Applied across all 3 consumers | SpaarkeAi, SemanticSearch, Compose.Components re-pointed (plus LegalWorkspace's transitive vite entries). |
| ui-components, Compose.Components, SemanticSearch, SpaarkeAi build; tests pass | See "Verification". |
| Census tests pass | `SharedPackageCensus` 8/8. |
| Negative: no `@spaarke/document-operations` in `src/` | 0 (remaining repo hits: the two ADR retirement records, the census-test comment, `docs/archive`, other projects' historical notes). |
| Negative: no `useTransition`/`useDeferredValue` in moved files | 0. |
| Existing contract only | Hook body unchanged; same exports (`useDocumentActions`, `UseDocumentActionsOptions`, `UseDocumentActionsResult`); the test moved with it (17 tests). |

## Verification

- ui-components `npm run build` (tsc): exit 0. Moved suite 17/17; full suite 386 suites, 6371 tests passed.
- Compose.Components `npm run build` (tsc): exit 0; jest 111 suites, 1455 tests passed.
- SemanticSearch `npm install` + `npm run build` (webpack): exit 0; jest 21 suites, 531 tests passed.
- SpaarkeAi `npm run build`: exit 0; jest 137 suites, 1226 tests passed.
- LegalWorkspace `npm run build` (Vite): exit 0.
- client-lint ratchet (`check --package` ui-components, compose-components, semantic-search, spaarkeai): all ok.
- `dotnet test tests/Spaarke.ArchTests --filter SharedPackageCensus`: 8 passed.

## Found in passing (pre-existing, not caused here) → task 034 files it

SemanticSearch standalone `tsc --noEmit -p .` fails: 23 × TS6059 (the `@spaarke/auth` tsconfig path points at source outside `rootDir: ./src`) and 2 × TS2352 in `src/index.tsx:60,109` (`Theme` → `Record<string,string>` cast). Nothing runs this check (the build is webpack). None of the errors involve the hook; the removed document-operations path also pointed at out-of-root source, so this change lowers the count. Same class as the LegalWorkspace standalone-tsc finding in `notes/032-legal-workspace.md`.

## Quality gates (Step 9.5)

- code-review (inline, diff scope): no findings. Checked: every consumer resolves the hook in build, typecheck and jest (Vite/webpack aliases → ui-components source; tsconfig → `dist` types; jest mappings or full-module mocks); every full-replacement ui-components mock in a suite that renders ComposeWorkspace or ComposeToolbar supplies `useDocumentActions`; no stale lock blocks; CI gate no longer references the deleted folder; the deploy trigger covers the new location; one React copy (hook's `react` import goes through each consumer's existing dedupe/mapping).
- adr-check: ADR-012 compliant (a package failing the three tests is consolidated into the default home; census row + test entry in the same change); ADR-022 compliant (no React 18+ API; not added to `pcf-safe`; PCF bundle unchanged); ADR-028 compliant (hook still calls `authenticatedFetch` from `@spaarke/auth`, unchanged); ADR-038 (the test moved with the code, same assertions).
- Adversarial pass: not run per task; NFR-03 sets one pass over items 1–4 (task 080).
