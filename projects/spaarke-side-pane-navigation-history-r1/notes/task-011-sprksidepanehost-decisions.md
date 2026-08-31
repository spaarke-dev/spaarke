# Task 011 — SprkSidePaneHost + sidePaneRegistry — decisions & deviations

> FULL rigor, sonnet @ high effort. Completed 2026-08-13.

## Reuse-vs-fork decision (escalation trigger did NOT fire)

`DataGridSidePaneOrchestrator.ts` (despite its name) was already pane-agnostic —
`SidePaneSpec` has zero DataGrid coupling. It was **generalized in place**, not
forked and not extracted into a parallel core:

- `SidePaneSpec.webResourceName` changed from required to **optional**. When
  omitted, `registerPane()` skips the `pane.navigate(...)` call — this is the
  self-hosted case (`SprkSidePaneHost` IS the pane's content; there is nothing
  to navigate to), as opposed to EventsPage's existing use (open ANOTHER
  webresource INTO a pane it manages).
- `SidePaneSpec.alwaysRender?: boolean` added — new passthrough to
  `createPane`, needed for the host's MUST rule (`canClose:false` +
  `alwaysRender:true`, NFR-05). Existing callers never set it —
  `createPane({ ..., alwaysRender: undefined })` is behavior-neutral.
- `getXrm` import swapped from the untyped `services/xrmGlobal.ts` walker to
  the widened `utils/xrmContext.ts` walker (task 010) — same runtime
  semantics (window→parent→top, requires `WebApi`), gains type coverage on
  `xrm.App.sidePanes.*`. This satisfies the task's "wired to the widened
  xrmContext typings" instruction without duplicating the lifecycle logic.

Both new fields are additive/optional — **zero behavior change** for the sole
existing consumer, `src/solutions/EventsPage/src/App.tsx` (always passes
`webResourceName`, never sets `alwaysRender`). Proven via the DataGrid Jest
suite (64/64 green, unchanged) — no dedicated orchestrator unit test exists in
the repo (confirmed via grep before the change); EventsPage itself has no
Jest suite exercising the orchestrator either, so the regression evidence is
the full DataGrid test pass + a manual read of `App.tsx`'s call site.

## Registry design

`sidePaneRegistry.ts` follows `surfaceLaunchRegistry.ts`'s "resolve to
`undefined` on unknown id, never throw" contract (NOT
`WorkspaceWidgetRegistry`'s "always fall back to a placeholder component" —
that fallback exists because a workspace TAB must always render *something*
once open; a side-pane host, by contrast, decides its own benign empty state
for an unknown id). Lazy factory + resolved-component cache is lifted from
`WorkspaceWidgetRegistry`.

## SprkSidePaneHost design notes

- Composition: outer row flex — content area (`SidePaneShell` wrapped, with
  `PaneHeader` — the existing shared pane-header primitive, reused rather than
  inventing a new title bar) + a right-edge vertical icon rail (40px, one
  button per registry entry, sorted by `order`).
- This host is its OWN root `FluentProvider` (like `DataGridPageShell`), not a
  nested one — it runs inside its own pane webresource iframe (a separate
  document from the MDA shell).
- `--sprk-ui-scale`: in addition to feeding `scaleTheme(theme, uiScale)` to
  the root provider, the host also sets the CSS variable on
  `document.documentElement` (this pane's own document) — mirroring the
  `SpaarkeAi App.tsx` / `LegalWorkspace LegalWorkspaceApp.tsx` app-shell
  precedent exactly, so any `SprkModal` a future contributor renders picks up
  the scale correctly.
- ADR-021 portal re-wrap: the rail icons' `Tooltip` content is explicitly
  re-wrapped in its own `FluentProvider` (Option A from the
  `fluent-v9-portal-gotcha.md` pattern doc) — matching the
  `ColumnHeaderMenu`/`ViewSelector`/`CommandBar` precedent for portal-bearing
  surfaces, rather than relying solely on the root provider's
  `applyStylesToPortals` default (which the pattern doc explicitly flags as
  insufficient for iframe/host-bridge scenarios).
- Registry snapshot is read fresh on every render (`listSidePaneRegistryEntries()`,
  no `useMemo([])`) rather than cached at mount — contributors may register
  after this host's module evaluates, and this keeps the host correct without
  a second registry-subscription mechanism.

## Known non-issues (documented, not fixed — out of task 011 scope)

- One pre-existing ESLint warning (`Unused eslint-disable directive` on
  `DataGridSidePaneOrchestrator.ts`'s catch-block `no-console` disable)
  predates this task — confirmed via diff that the catch block was untouched.
- The full `npx jest` run (not scoped to SidePane/DataGrid) has 10 pre-existing
  failing suites unrelated to this task (`surfaceLaunchRegistry.test.ts`,
  `ConversationView.*`, `XrmDataverseClient.test.ts`,
  `buildDynamicWorkspaceConfig.test.ts`, etc.) — none touch `SidePane/`,
  `DataGrid/sidePane/`, or `xrmContext.ts`. Left as-is; not introduced by this
  task.
