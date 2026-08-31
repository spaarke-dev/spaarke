# Task 085 — Stub contributor (FR-13 / Success Criterion 11 framework proof)

## Approach

Two new files, zero changes anywhere else:

- `src/client/shared/Spaarke.UI.Components/src/components/SidePane/__stub__/StubContributor.tsx`
  — a minimal `StubContributor` component + the exported
  `STUB_SIDE_PANE_REGISTRY_ENTRY` descriptor (`{ id, icon, title, order, component }`).
- `src/client/shared/Spaarke.UI.Components/src/components/SidePane/__tests__/stubContributor.test.tsx`
  — the render proof (5 tests).

## Avoiding production pollution

`StubContributor.tsx` has **no module-load side effects** — it does NOT call
`registerSidePaneContributor()` itself; it only *exports* the component and the
descriptor object. The global `sidePaneRegistry` is a singleton Map, so any
module that imports it and calls `registerSidePaneContributor(...)` at import
time would register permanently for the life of that JS context.

Production entry points (`src/solutions/NavigatorPane/src/main.tsx` and any
future contributor bundle) never import from `SidePane/__stub__/`, so the stub
cannot reach the shipping bundle by construction — there is nothing to
tree-shake around, nothing to feature-flag off. Only
`stubContributor.test.tsx` imports the descriptor and calls
`registerSidePaneContributor(STUB_SIDE_PANE_REGISTRY_ENTRY)` in each `it()` (or
`beforeEach`-equivalent per test), with `clearSidePaneRegistry()` run in both
`beforeEach` and `afterEach` (mirroring the existing
`SprkSidePaneHost.test.tsx` convention) so the registry singleton is always
left clean — belt-and-suspenders on top of the "never imported by production"
guarantee.

Verified: running the full SidePane suite (`SprkSidePaneHost.test.tsx` +
`stubContributor.test.tsx`) together produces 16/16 passing with no
cross-file registry leakage.

## Registry entry shape used

```ts
export const STUB_SIDE_PANE_REGISTRY_ENTRY: SidePaneRegistryEntry = {
  id: 'fr13-stub-proof',
  icon: <BeakerRegular />,
  title: 'FR-13 Stub',
  order: 999,
  component: async () => ({ default: StubContributor }),
};
```

`order` is required by the `SidePaneRegistryEntry` interface (task 011) for
every contributor — including `NavigatorPane`'s own registration in
`main.tsx` — so it is not "extra registry surface" beyond the task's
`{ id, icon, title, component }` requirement; it's the one universal sort key
every entry must supply. The test file additionally asserts the descriptor's
key set is exactly `['component', 'icon', 'id', 'order', 'title']` — no
privileged/extra fields.

## FR-13 proof — host code NOT modified

`git status --porcelain` after this task's changes shows exactly:

```
?? src/client/shared/Spaarke.UI.Components/src/components/SidePane/__stub__/
?? src/client/shared/Spaarke.UI.Components/src/components/SidePane/__tests__/stubContributor.test.tsx
```

`SprkSidePaneHost.tsx` and `sidePaneRegistry.ts` are untouched — the stub
extends the framework purely by calling the existing
`registerSidePaneContributor()` export from the test. No host edit was
required at any point; the escalation trigger in the task POML did not fire.

## Test results

- `npx jest src/components/SidePane` → 2 suites, 16 tests, all passing.
- `npx tsc --noEmit` → clean.
- `npm run build` → succeeds.
- Full library `npx jest` run: 10 pre-existing failing suites unrelated to
  this task (`surfaceLaunchRegistry`, `toolbarLaunchDefaults`,
  `EntityCreationService.cascade`, `XrmDataverseClient`,
  `buildDynamicWorkspaceConfig`, `RichFilePreview`, `ConversationView.forward`,
  `TimelineComposeBox`, `recordHeader.integration`,
  `ConversationView.emailInFlow`) — none touch `SidePane/` and none are
  modified by this task; confirmed pre-existing via `git status --porcelain`
  showing no changes to those files.

## Deviations

None. No escalation was required.
