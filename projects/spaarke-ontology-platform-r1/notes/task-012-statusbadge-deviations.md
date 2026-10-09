# Task 012 (C-4) — deviations from the POML, recorded per step 7

> StatusBadge (`@spaarke/ui-components`), FR-28 / FR-41.

## 1. Barrel export file — `components/index.ts`, not the top-level `src/index.ts`

The POML's `<outputs>` and `<relevant-files>` list
`src/client/shared/Spaarke.UI.Components/src/index.ts` as the file to modify. The actual codebase
convention (confirmed by reading both files) is:

- `src/index.ts` just does `export * from './components';` (plus icons/theme/types/utils/hooks/services) and
  is **not** where individual components are wired in.
- Every other component (`AccessGrantModal`, `TodoDetail`, `TagFilter`, `DocumentRowMenu`, ...) is exported
  from `src/components/index.ts` via its own `export * from './ComponentName';` line.

I added `export * from './StatusBadge';` to `src/components/index.ts` (inserted immediately before the
`AccessGrantModal` export block, with a comment) instead of editing the top-level `src/index.ts`, which
would have been redundant and off-convention. `<steps mode="directional">` permits this kind of adaptation
when a step is wrong for the real codebase state.

**Reduced-collision note for the orchestrator**: the task brief warned that task 011 (MetricCard
disambiguation) may also touch `src/client/shared/Spaarke.UI.Components/src/index.ts`. Because I edited
`src/components/index.ts` instead (a different file), there should be no overlap with task 011's edit to the
top-level `src/index.ts`. If task 011 *also* needs to touch `src/components/index.ts` (e.g. to add an
explicit disambiguating re-export for `MetricCard`), the one-line, clearly-commented insertion I made should
not conflict unless it lands on the exact same lines — worth a quick diff check by the main session before
merge.

## 2. Test file location — `__tests__/` subfolder, not a flat sibling file

The POML's `<outputs>` lists the test file as a flat sibling:
`.../StatusBadge/StatusBadge.test.tsx`. The package's `jest.config.js` `testMatch` is:

```js
testMatch: ['**/__tests__/**/*.test.ts', '**/__tests__/**/*.test.tsx']
```

A flat `StatusBadge/StatusBadge.test.tsx` (not inside a `__tests__` folder) would **not** be discovered by
Jest at all — it would silently run zero tests rather than failing loudly. Every existing component test in
this package (`PaneHeader`, `HeaderToolbar`, `RelationshipCountCard`, the sibling
`PinnedMemoryProvenanceBadge` in `Spaarke.AI.Widgets`, ...) lives under a `__tests__/` subfolder for exactly
this reason. I placed the test at
`src/components/StatusBadge/__tests__/StatusBadge.test.tsx` and did not create a flat copy, to match
convention and to actually get test execution.

## 3. An extra file the POML didn't list — `StatusBadge/index.ts`

Every other folder-based component in `src/components/` (`PaneHeader`, `AccessGrantModal`,
`RelationshipCountCard`, ...) has its own `index.ts` re-exporting the named symbols from the implementation
file — `export * from './StatusBadge'` in the parent barrel resolves to that folder-local `index.ts`, not
directly to `StatusBadge.tsx`. This wasn't in the POML's `<outputs>` list. Omitting it is NOT cosmetic: a
full `tsc` build (`npx tsc --noEmit`) failed with `TS2307: Cannot find module './StatusBadge'` at
`components/index.ts:293` until I added
`src/components/StatusBadge/index.ts` with:

```ts
export { StatusBadge } from './StatusBadge';
export type { StatusBadgeProps, StatusBadgeTone } from './StatusBadge';
```

After adding it, `npx tsc --noEmit` no longer reports any StatusBadge-related error (see Verification below
for the full remaining-error list, all pre-existing and unrelated).

## 4. No other deviations

Component API, tone set, fallback behavior, barrel export *intent*, and the test coverage (one test per
tone, the two fallback cases, the dark-mode smoke test, accessibility) all follow the POML as written.

## Verification performed

- `npx jest src/components/StatusBadge --no-coverage` — **9/9 passed** (tones × 4, fallback × 2,
  accessibility × 2, dark-mode smoke × 1).
- `npx eslint src/components/StatusBadge/StatusBadge.tsx src/components/StatusBadge/index.ts
  src/components/StatusBadge/__tests__/StatusBadge.test.tsx src/components/index.ts` — clean, no errors
  (only an unrelated Node ESM/CommonJS warning about the eslint config file itself).
- `npx tsc --noEmit` — no error on any StatusBadge file or on `components/index.ts`. Nine pre-existing
  errors remain, all in files this task did not touch and all caused by sibling workspace packages
  (`@spaarke/auth`, `@spaarke/sdap-client`) not having a built `dist/` in this fresh worktree
  (`AccessGrantModal/types.ts`, `FileUploadService.ts` ×5, `services/document-upload/types.ts`,
  `EntityCreationService.ts`, `useWizardPageBootstrap.ts`). Confirmed pre-existing by checking those two
  sibling packages have no `dist/` folder at all yet — this is a workspace-build-order gap, not something
  introduced by StatusBadge.
- Grep for `#[0-9a-fA-F]{3,8}|rgb(|rgba(|hsl(` over `StatusBadge.tsx` — no matches (satisfies the
  no-hex/rgb-literal acceptance criterion).
