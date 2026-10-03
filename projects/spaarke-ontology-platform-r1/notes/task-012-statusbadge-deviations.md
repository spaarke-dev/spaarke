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

## 3. No other deviations

Component API, tone set, fallback behavior, barrel export *intent*, and the test coverage (one test per
tone, the two fallback cases, the dark-mode smoke test, accessibility) all follow the POML as written.
