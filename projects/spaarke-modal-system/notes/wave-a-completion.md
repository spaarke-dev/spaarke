# Wave A (Group A / P0) — Completion Note

> **Date**: 2026-08-01
> **Tasks**: 001 (size scale tokens), 002 (scaled Fluent theme), 003 (window-controls glyph)
> **Executed**: main session (consolidated — see rationale below)

## Outcome

All three P0 foundation tasks complete. Shared-lib build **green** (tsc exit 0), **20/20** tests
pass across the three suites, eslint clean.

| Task | Output | Verify |
|------|--------|--------|
| 001 | `SprkModal/sizes.ts` + `__tests__/sizes.test.ts` | 7 sizes, exact caps (md 1040/720, lg 1280/880); `getSurfaceStyle` math + `widthLabel` |
| 002 | `SprkModal/scaledTheme.ts` + `__tests__/scaledTheme.test.ts` | px-token multiply, colors untouched, `scale===1` identity, no base mutation |
| 003 | `ModalWindowControls.tsx` (icon swap) | `ArrowMaximize/Minimize` → `FullScreenMaximize/Minimize`; API + tests unchanged/green |

## Execution decision

Consolidated the three parallel-safe tasks into ONE main-session execution rather than three
sub-agents: total surface is ~5 small files (below task-execute Step 8.0's parallelization
threshold), they share one npm build target (one consolidated build beats three racing `dist`
writes), and full context on all three (POMLs + prototype sources + shipped files) was already loaded.

## Deviations from the POML text (all minor, all documented per each task's step "document any deviation")

1. **`getSurfaceStyle`/`widthLabel` default `uiScale = 1`** (task 001). The prototype required
   `uiScale`; POML step 2 asked for `getSurfaceStyle(size, uiScale=1)`. Added the default to both
   for caller convenience; a test asserts `getSurfaceStyle('md') === getSurfaceStyle('md', 1)`.

2. **Width form is pre-multiplied px, not `calc(var(--sprk-ui-scale))`** (task 001). Task 001's
   prose showed `min(calc(px * var(--sprk-ui-scale,1)), vw)`, but the authoritative spec **FR-02**
   says `min(cap·uiScale px, N·vw)` — the pre-multiplied form the prototype emits (e.g. `min(1560px,
   92vw)` for md@1.5). Followed spec FR-02 + the prototype: `uiScale` is a numeric arg the host
   threads to BOTH `getSurfaceStyle` and `scaleTheme` (one scale source, JS-resolved), consistent
   with the scaled-theme mechanism. No CSS `zoom`, no CSS var. **Not an escalation** — the load-bearing
   SIZE_SPEC caps (md/lg) match spec §6.2 exactly.

3. **`wizard = 60%×70%` is the OOB size, not the SprkModal `wizard` size.** Task 010's prose
   conflates them; spec **FR-11** shows `60%×70%` is the OOB `navigateTo` dimension (a different
   mechanism), while the SprkModal `wizard` size is `62vw × min(74vh, 760px)` per the prototype.
   No conflict for task 001; flag for task 010 (standards doc) to keep the two layers distinct.

4. **`ModalWindowControls` tests unchanged** (task 003). The existing suite asserts the behavioral
   contract by **aria-label** (`Maximize dialog`/`Restore dialog size`), which the glyph swap does
   NOT change, and there is no snapshot file. So the tests remain valid and green as-is; the icon
   identity is verified by the JSX + the prototype reference + a green build (the `FullScreen*` icons
   resolve in `@fluentui/react-icons ^2.0.320`). No test edit was needed.

## Environment setup performed (fresh worktree)

This worktree had no installed `node_modules`. Ran `npm install --legacy-peer-deps --no-audit
--no-fund` in `Spaarke.UI.Components`, and built the two `file:` sibling packages (`Spaarke.SdapClient`,
`Spaarke.Auth`) whose missing `dist/` was the *only* source of the two pre-existing `tsc` errors
(`@spaarke/sdap-client`, `@spaarke/auth` in `EntityCreationService.ts` / `useWizardPageBootstrap.ts`
— neither touched by this work). After building the siblings the full shared-lib build is green.
