# CLAUDE.md — spaarke-side-pane-navigation-history-r1 (Project Context)

> Loads when working this project. Complements root `CLAUDE.md` (repo-wide rules win).
> **Status**: INITIALIZED 2026-08-13 (INITIALIZE-ONLY — execution owner-gated).

## 🚨 MANDATORY: Task Execution Protocol

When executing any task in this project, invoke the **`task-execute`** skill with the task POML — do NOT read POML files and implement manually (root CLAUDE.md §4). `task-execute` loads knowledge/ADRs, tracks `current-task.md`, checkpoints every 3 steps, and runs the Step 9.5 quality gates (`code-review` + `adr-check`) at the declared rigor level.

Trigger phrases → `task-execute`: "work on task X", "continue"/"next task" (read `tasks/TASK-INDEX.md`, first 🔲), "resume task X", "pick up where we left off" (load `current-task.md`).

## What this project is

A reusable **side-pane framework** (`SprkSidePaneHost` + `sidePaneRegistry`) for the MDA shell + **Navigation History ("Navigator")** as its first contributor (Recent/Pinned/Views + search), backed by a new per-user `sprk_navitem` entity. Capture is **zero-form-handler, zero-plugin** (persistent app-level pane polling `getPageContext()`). **No BFF code.** See [`spec.md`](spec.md), [`plan.md`](plan.md).

## Non-negotiable constraints (MUST / MUST NOT)

- ✅ MUST access all data host-context via **`Xrm.WebApi`** under the signed-in user. ❌ MUST NOT add code to `Sprk.Bff.Api`.
- ❌ MUST NOT add per-form OnLoad/OnSave web resources or Dataverse plugins. The only global JS is the single app-startup bootstrap (Task 001/086).
- ❌ MUST NOT use the `audit` entity.
- ❌ MUST NOT write `sprk_monitor` from the pin/star gesture — personal pins are `sprk_navitem` only (per-user). `sprk_monitor` stays the shared record-level flag.
- ✅ MUST security-trim cached labels at render time (never show a name for a record the user lost access to).
- ✅ MUST create the app pane via `Xrm.App.sidePanes.createPane` with `canClose:false` + `alwaysRender:true`.
- ✅ MUST follow ADR-021: Fluent v9 tokens only, portal `FluentProvider` re-wrap, code-page theme detection, `--sprk-ui-scale` via `scaledTheme`/`useUiScale`.
- ✅ MUST keep `@spaarke/ui-components` code React-16/17-safe (ADR-022): no `createRoot`, use `JSXElement` not `JSX.Element`.

## Reuse first (§11) — do NOT rebuild

- `SprkSidePaneHost` = **wrap `SidePaneShell`** + **generalize `DataGridSidePaneOrchestrator`** (`components/DataGrid/sidePane/`). Do not reinvent createPane/close-on-navigate.
- Registry modeled on `WorkspaceWidgetRegistry` + `surfaceLaunchRegistry`.
- Capture re-adopts recovered `notes/retired-sidepane-code/contextService.ts` (2s poll + sessionStorage).
- `sprk_navitem` CRUD mirrors `Notepad/src/hooks/useSprkMemoRepository.ts`; side-pane CRUD mirrors `EventDetailSidePane/src/services/eventService.ts`.
- Views tab reuses `Spaarke.UI.Components/src/services/ViewService.ts`.
- Code page mirrors `CalendarSidePane`/`EventDetailSidePane` (Vite singlefile, `webresource` pane).
- Entity schema mirrors `SpaarkeCore/entities/sprk_todo/` (override to **UserOwned**).

## Skills used (in order)

`dataverse-create-schema` → `dataverse-deploy` → `code-page-deploy` → `fluent-v9-component` → `ui-test` → `adr-check` + `code-review` → `test-diet`.

## Build / deploy notes

- Code page: **Vite** `npm run build` (NOT `build:prod` — that's PCF). **Cache-clear before every build** (`rm -rf dist/ node_modules/.vite/ .vite/`) and **recompile `@spaarke/ui-components` first**, else the bundle ships stale `dist/`. Verify a known string in the built HTML.
- Node installs: `npm install --legacy-peer-deps --no-audit --no-fund` (avoid `npm ci` for Vite solutions — root §12).
- Entity: Web API + PowerShell (NOT PAC), into `SpaarkeCore` (unmanaged), UserOwned, global optionsets first, then publish.

## Hot-path & coordination

- Hot-path declaration: **BFF=N, SpaarkeAi=N, CI=N, Skill-directives=N, root-CLAUDE=N** — no §10 obligations.
- Touches `@spaarke/ui-components` (`SidePane/`, `xrmContext.ts`, new host/registry). No active worktree touches the `SidePane/` subfolder; still run `/conflict-check` before any `@spaarke/ui-components` PR.

## ADR tensions (accepted, per §6.5)

- ADR-006 → **C** (comply): code-page-as-`webresource`-pane, not a form handler.
- ADR-022 → **C**: React-16/17-safe shared-lib code; code page is React 19.
- Superseded side-pane platform → **A** (owner-approved 2026-08-12): reviving Path B bootstrap; product-driven retirement, not technical; Task 001 re-validates with Path A fallback.

## First action

**Task 001 is a spike gate** (Opus/xhigh). Do NOT build framework code until it confirms the Path B bootstrap on current UCI. On failure → escalate + Path A fallback (root §6/§6.5).
