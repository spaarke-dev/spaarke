# Daily Briefing prototype harness (task 020 / FR-D1)

## Location

`c:/code_files/spaarke-prototype/projects/daily-briefing-r5-uat/`

This is a **separate repo** (`spaarke-prototype`, branch `feature/uat-harness-framework`) from this worktree. It
mounts the REAL `Spaarke.DailyBriefing.Components` shared library from this worktree via a Vite source alias — no
component source is copied or forked. Editing files under
`src/client/shared/Spaarke.DailyBriefing.Components/` in this worktree hot-reloads the harness's browser tab.

## Run command

```powershell
cd c:/code_files/spaarke-prototype/projects/daily-briefing-r5-uat
$env:SPAARKE_REPO_ROOT = "c:/code_files/spaarke-wt-spaarke-daily-update-service-r5"
npm install --legacy-peer-deps --no-audit --no-fund   # first time only
npm run dev
```

Opens `http://localhost:5174`. `SPAARKE_REPO_ROOT` must be set every session (the shared `_infra/vite.shared-libs.ts`
alias config defaults to a different worktree, `spaarke-wt-smart-todo-r4`) — see `prototype-harness-setup/SKILL.md`
Step 8.

Build-verify (no dev server): `SPAARKE_REPO_ROOT=... npx vite build` — confirmed green (see "Build status" below).

## What's mounted

The REAL `DailyBriefingApp` composer from
`@spaarke/daily-briefing-components/components` (aliased to
`src/client/shared/Spaarke.DailyBriefing.Components/src/components/index.ts` in this worktree) — not a
reimplementation. It renders, in order: `HighPrioritySection` → `TldrSection` → `ActivityNotesSection` (per-channel
`NarrativeBullet` rows), exactly as it does in production. `App.tsx` wraps it in a `FluentProvider` with a light/dark
`Switch` toggle (webLightTheme / webDarkTheme) — no hardcoded colors in the harness wrapper (ADR-021).

## Fixture data

`Spaarke.DailyBriefing.Components` does **not** read its briefing data via `Xrm.WebApi` — `useBriefingRender` calls
`POST /api/ai/daily-briefing/render` through `authenticatedFetch` (the BFF AI endpoint). The harness mocks that
route (not Xrm records) via `installMocks({ auth: { routes: {...} } } )` in
`projects/daily-briefing-r5-uat/src/main.tsx`.

The fixture itself lives at
`c:/code_files/spaarke-prototype/_infra/seed/factories/daily-briefing-render.ts` (`makeDailyBriefingMixedItemFixture()`),
registered in `_infra/seed/index.ts` alongside the other (Xrm-oriented) seed factories.

**Provenance / deviation from the task-016 corpus plan**: task 020's plan was to reuse the Phase-A task-016
mixed-item eval corpus verbatim. At the time this harness was scaffolded, **task 016 had not yet landed**
(`TASK-INDEX.md`: 016 status still 🔲 not-started as of 2026-07-08 — it depends on 011/013/014, which are themselves
prerequisites this harness task does not depend on). The fixture was therefore hand-authored **following the same
principle** the task-016 corpus asserts: every bullet's `narrative` / `primaryEntityName` / `primaryEntityType` /
`primaryEntityId` fields are destructured from the SAME source item — zero cross-item pairing by construction. When
task 016 lands, swap this fixture for the real corpus (update `daily-briefing-render.ts` to import/derive from it) so
the design work in task 021 continues to iterate against the same data the accuracy suite validates.

Fixture contents: 5 channels (`overdue-tasks`, `upcoming-tasks`, `documents`, `matters`, `to-dos`; 9 item rows total)
spanning 6 legal-domain matters/projects, a 4-item High-Priority section (one of each action state — Overdue,
DueToday, DueSoon, Recent — and one of each reason — HighPriority, Monitor, Both), and a grounded TL;DR whose
`summary` / `keyTakeaways` / `topAction` all trace back to facts present in the channels + High-Priority items above.

## Known build workaround (Rollup cross-directory resolution)

Importing the real `@spaarke/daily-briefing-components/components` barrel transitively pulls in TWO files that
import from the BARE `@spaarke/ui-components` specifier:

- `SubRowTodo.tsx` imports `MicrosoftToDoIcon` (icon-only; `SubRowTodo` never actually mounts in this harness's
  usage since `DailyBriefingApp` never passes the `items` prop that triggers `NarrativeBullet`'s sub-list).
- `useInlineTodoCreate.ts` imports `applyResolverFields` + `TODO_REGARDING_CATALOG` from
  `@spaarke/ui-components/services`.

The shared `_infra/vite.shared-libs.ts` alias points the bare `@spaarke/ui-components` specifier at the library's
**full** root barrel (`src/index.ts`) / **full** services barrel (`services/index.ts`), which re-export the entire
component + service surface (buttons, wizards, `renderMarkdown` → `marked`, `SprkChatBridge`, `dispatchConsumer`,
etc.) — dozens of files this harness doesn't use and hasn't installed. Rollup's build-time resolver also cannot find
`@microsoft/applicationinsights-web` (a real dependency of `AppInsightsService.ts`, itself pulled in by the services
barrel) because that file lives in the r5 worktree and Node's module-resolution walk from its own directory never
reaches across to this harness's `node_modules`.

**Fix** — three harness-LOCAL Vite alias overrides in `projects/daily-briefing-r5-uat/vite.config.ts` (spread AFTER
`...sharedLibsAlias` so they win; the SHARED `_infra/vite.shared-libs.ts` file was NOT changed for these three, so
other harnesses like `smart-todo-r4-uat` are unaffected):

1. `@spaarke/ui-components` (bare) → real `.../Spaarke.UI.Components/src/icons/index.ts` (narrower, real, dependency-free — the only export the reachable module graph needs).
2. `@spaarke/ui-components/services` → `src/stubs/uiComponentsServicesNarrow.ts`, a local re-export of ONLY `PolymorphicResolverService.ts` + `TodoRegardingUpdateBuilder.ts` (both verified dependency-free) — REAL production logic, just narrower than the full 15-service barrel.
3. `@microsoft/applicationinsights-web` → `src/stubs/applicationinsights-web-stub.ts`, a minimal typed no-op (dead code in this harness — nothing calls `AppInsightsService.initialize()` from the Daily Briefing render path; only the PCF surfaces `SemanticSearchControl`/`VisualHost` do).

**This is NOT a workaround of the task-020 "shared lib builds standalone" blocker** (that was PR #584, already
merged — confirmed at task start: `package.json` has `file:` deps, `tsconfig.json` has sibling `dist` paths). It's a
narrower, harness-specific Rollup resolution gap for optional/unused transitive surface, addressed without touching
`Spaarke.DailyBriefing.Components` production source and without broadening the shared `_infra/vite.shared-libs.ts`
alias map for other harnesses.

## Build status

`SPAARKE_REPO_ROOT=c:/code_files/spaarke-wt-spaarke-daily-update-service-r5 npx vite build` — **green**
(2194 modules transformed, single ~656 kB JS bundle, no resolution errors).

`npm run dev` starts cleanly (Vite ready in <500ms) and serves `DailyBriefingApp.tsx` + its full component tree with
HTTP 200 / no server-side transform errors.

## Verification gap — no browser automation tool in this session

This scaffolding was executed in an agent session with no browser/screenshot tool available (no Chrome integration,
no Playwright/Puppeteer). I could NOT capture the light/dark baseline screenshots this task's acceptance criteria and
outputs call for (`notes/design/baseline/`), and could not directly observe the rendered DOM for console errors.

What WAS verified:
- Production build succeeds end-to-end (all module resolution, including the deep `@spaarke/ui-components`
  transitive chain, is green).
- Dev server starts and serves every module in the `DailyBriefingApp` import chain at HTTP 200 with no
  server-side transform errors logged.
- Manual code-level trace confirms the mock wiring is correct: `fetchBriefingLive()` calls
  `POST /api/ai/daily-briefing/render`; the harness's `installMocks({ auth: { routes } } )` in `main.tsx` intercepts
  that exact path and returns the fixture; the fixture's shape matches `NarrateResponse` (`tldr` / `channelNarratives`
  / `highPriorityItems` / `generatedAtUtc`) field-for-field against `briefingService.ts`.
- `Xrm.WebApi` is installed (empty store) so `useBriefingPreferences` degrades gracefully to defaults (confirmed via
  code trace of `fetchDigestPreferences`'s empty-result path — no thrown errors).

**What's still open**: an operator (or a session with Chrome/Playwright available) should run `npm run dev`, open
`http://localhost:5174`, confirm the full briefing renders (item rows + TL;DR + High-Priority section) against the
mixed-item fixture, toggle the theme Switch to confirm light + dark both render with no console errors, and save one
screenshot of each into this `notes/design/baseline/` directory before task 020 is marked fully verified. The
`/ui-test` skill (Chrome integration) or a quick manual check both satisfy this.

## Files created / modified (spaarke-prototype repo only)

- `_infra/vite.shared-libs.ts` — added `@spaarke/daily-briefing-components` (+ `/components` subpath) alias and
  transpile-path entry (shared; safe for all harnesses).
- `_infra/seed/factories/daily-briefing-render.ts` — new fixture factory (`makeDailyBriefingMixedItemFixture`).
- `_infra/seed/index.ts` — registered the new factory's exports.
- `projects/daily-briefing-r5-uat/` — new harness (package.json, tsconfig.json, vite.config.ts, index.html,
  src/main.tsx, src/App.tsx, src/stubs/uiComponentsServicesNarrow.ts, src/stubs/applicationinsights-web-stub.ts).

No file under `src/client/shared/Spaarke.DailyBriefing.Components/` (this worktree) was modified.
