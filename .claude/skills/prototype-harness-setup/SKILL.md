---
description: Scaffold a new spaarke-prototype harness for visual iteration on a production component (widget, Code Page, PCF, or shared lib) with HMR + mocks
tags: [prototype, harness, ui, hmr, ux-iteration, framework-consumer]
techStack: [vite, react, fluent-v9, typescript, powershell]
appliesTo: ["set up prototype harness", "create UAT harness", "stand up local dev for widget", "iterate on UI visually without deploy"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-18
---

# prototype-harness-setup

> **Category**: UI / Prototyping
> **Last Reviewed**: 2026-06-18
> **Exemplar rationale**: Harness directory names are user-named per-project (`<project>-uat`); no stable canonical artifact. The 11-step workflow is the contract.

---

## Purpose

Scaffold a new **production component harness** (Mode 2) in `c:/code_files/spaarke-prototype/` that mounts production source from a Spaarke worktree against mocked Xrm + auth + seeded Dataverse data. Eliminates the master-deploy round-trip for visual iteration.

**Reduces a 5-minute, 7-step manual ceremony** (copy template → rename → edit package.json → set env var → npm install → edit App.tsx → edit main.tsx) **to ~30 seconds of skill invocation**.

**Companion skills**:
- `prototype-harness-extend` — add a new entity factory + preset to existing `_infra/seed/`
- `prototype-experiment-init` — scaffold a **Mode 1** standalone UX experiment (no production source yet)

**Reference**: [PROTOTYPE-UI-SYSTEM-GUIDE.md](../../../c:/code_files/spaarke-prototype/docs/PROTOTYPE-UI-SYSTEM-GUIDE.md) in the prototype repo.

---

## Applies When

- User says "set up prototype harness for X" / "create UAT harness" / "stand up local dev for the X widget"
- User asks how to iterate visually on a production component without deploying
- A worktree exists with production component source the user wants to iterate on
- **Trigger phrases**: `/prototype-harness-setup`, "prototype harness", "UAT harness for X", "iterate on widget visually", "stand up local dev for X"

**Do NOT use** when:
- The component does not exist yet in production code (use `prototype-experiment-init` instead — that's Mode 1 greenfield)
- The user wants to test PCF lifecycle (init/updateView/destroy) — that needs `pcf-start` from `pcf-scripts` OR master-deploy

---

## Sandbox Safety — Read This Once

**Seed data does NOT affect production:**
- Lives in `c:/code_files/spaarke-prototype/_infra/seed/` — production code never imports it
- Stored in-memory only — never written to a real Dataverse environment
- The Vite alias is **one-way** (prototype reads production source; production never imports from prototype)
- Mock `Xrm.WebApi` only exists when `installMocks()` runs in the harness `main.tsx` — production deploys never call this

The harness is a sandbox. Edit production source in the worktree as usual; the harness re-renders via HMR.

---

## Prerequisites

| Check | Command |
|---|---|
| `c:/code_files/spaarke-prototype` repo exists | `Test-Path c:/code_files/spaarke-prototype/.git` |
| On framework branch | `cd c:/code_files/spaarke-prototype && git branch --show-current` → expect `feature/uat-harness-framework` (or `main` once merged) |
| Target worktree exists | `Test-Path <worktree-path>` |
| Node + npm available | `node --version && npm --version` |

If `spaarke-prototype` is missing: `git clone https://github.com/spaarke-dev/spaarke-prototype.git c:/code_files/spaarke-prototype`. If on the wrong branch: `git checkout feature/uat-harness-framework`.

---

## Workflow

### Step 1: Gather Inputs

Auto-detect what you can; prompt only when ambiguous.

**Project name** (required):
```
ASK: "Project name? (kebab-case, e.g., 'smart-todo-r4', 'matter-form'). Resulting harness dir = projects/<name>-uat/"
```

**Worktree path** (auto-detect):
- IF `pwd` matches `c:/code_files/spaarke-wt-*` → use it (offer to confirm)
- ELSE: `git worktree list` from main repo → if exactly one matches `*-{project-name}`, use it
- ELSE: prompt with default `c:/code_files/spaarke-wt-<project-name>`

**Components to mount** (detect by listing the worktree's shared + solutions dirs):
```
Run:
  ls <worktree>/src/client/shared/Spaarke.*.Components/src/ 2>$null
  ls <worktree>/src/solutions/*/src/SmartTodoApp.tsx, <worktree>/src/solutions/*/src/*App.tsx 2>$null
  ls <worktree>/src/client/pcf/*/control/index.ts 2>$null

PRESENT detected components as a checklist. Examples:
  [x] @spaarke/smart-todo-components (widget)
  [x] @spaarke/smart-todo-app (Code Page at src/solutions/SmartTodo/src/SmartTodoApp.tsx)
  [ ] @spaarke/ui-components (probably not needed standalone)
  [ ] AnalysisWorkspace PCF (at src/client/pcf/AnalysisWorkspace/)

ASK: "Which to mount? (comma-separated, or 'all detected')"

IF multiple full-height surfaces (widget + code page) → recommend tab-switch layout
IF single component → recommend single-mount layout
```

**Interactive features to wire** (grep the chosen components):
```
For EACH chosen component, grep for:
  - CreateTodoWizard / CreateRecordWizard → wizard launcher
  - SmartTodoModal / RecordNavigationModalShell → hybrid modal
  - Popover / Menu / Dialog imports → popovers
  - @hello-pangea/dnd → drag-drop (no setup needed beyond dedup)

ASK only if found:
  "Detected CreateTodoWizard usage. Wire '+ New' to open the wizard against mocked data? [Y/n]"
```

**Entity / seed preset**:
```
DETECT entity from component's webApi calls:
  grep -E "retrieveMultipleRecords\(['\"](sprk_\w+)" <chosen-components>

IF entity factory exists at _infra/seed/factories/{entity}.ts:
  → "Use existing seed preset 'X' (N records)? [Y/n]"
IF entity factory missing:
  → "Entity sprk_X not seeded. Run prototype-harness-extend after this skill finishes? [Y/n]"
  → For this skill: scaffold an empty preset with TODO comment
```

---

### Step 2: Confirm Plan

**OUTPUT a single confirmation block** showing all gathered inputs:

```
PLAN
────
Project:           {project}
Worktree:          {worktree-path}
Harness dir:       c:/code_files/spaarke-prototype/projects/{project}-uat/
Mount components:  @spaarke/X, @spaarke/Y
Layout:            {single | tab-switch}
Interactive UI:    {wizards/modals/etc to wire | none}
Seed preset:       {existing 'X' | new 'Y' scaffold}
SPAARKE_REPO_ROOT: {worktree-path}

Proceed? [Y/n]
```

If `n` → stop and offer to refine.

---

### Step 3: Copy Template + Rename

```powershell
$dest = "c:/code_files/spaarke-prototype/projects/{project}-uat"
if (Test-Path $dest) {
  # Don't overwrite existing harness — ask
  ASK: "Harness dir exists. Overwrite / reuse / abort? [overwrite/reuse/abort]"
}
Copy-Item -Recurse `
  c:/code_files/spaarke-prototype/projects/_templates/prod-component-harness `
  $dest
```

Update `$dest/package.json` `name` field to `"{project}-uat"`.

---

### Step 4: Add Vite Aliases (if needed)

```powershell
# Check if any chosen component path already in _infra/vite.shared-libs.ts
grep "{component-path}" c:/code_files/spaarke-prototype/_infra/vite.shared-libs.ts

# IF missing → add an alias entry to sharedLibsAlias{}
#   "@spaarke/{logical-name}": path.join(SPAARKE_ROOT, "{component-source-path}")
# AND add to transpileSharedLibPaths[] if .tsx files need transpilation
```

Common cases:
- Shared peer package (`src/client/shared/Spaarke.X.Components/`) → `@spaarke/x-components`
- Code Page (`src/solutions/X/src/XApp.tsx`) → `@spaarke/x-app` (logical alias; not a real package)
- PCF inner React component → `@spaarke/x-pcf` aliasing to the React component file (NOT `index.ts` which is the PCF wrapper)

---

### Step 5: Generate `src/main.tsx`

Standard pattern (verbose logs ON by default for first-run discoverability):

```tsx
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

import { installMocks } from "../../../_infra/mocks";
import { createSeed } from "../../../_infra/seed";
import { App } from "./App";

installMocks({
  seed: createSeed("{preset-name}"),
  verbose: true,
  broadcast: true,
});

const root = ReactDOM.createRoot(document.getElementById("root")!);
root.render(
  <React.StrictMode>
    <FluentProvider theme={webLightTheme}>
      <App />
    </FluentProvider>
  </React.StrictMode>,
);
```

If the chosen preset doesn't exist yet: leave a `// TODO: scaffold preset via prototype-harness-extend` comment and use `seed: { records: {} }`.

---

### Step 6: Generate `src/App.tsx`

Three template variants based on what was detected:

**Variant A — Single component (widget or code page only)**:

```tsx
import * as React from "react";
import { makeStyles, tokens, Title3, Caption1 } from "@fluentui/react-components";
import { {ComponentName} } from "@spaarke/{package}";

const useStyles = makeStyles({
  page: { minHeight: "100vh", padding: tokens.spacingHorizontalL },
  widgetFrame: {
    maxWidth: "1200px", margin: "0 auto",
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    padding: tokens.spacingHorizontalM,
  },
});

/* eslint-disable @typescript-eslint/no-explicit-any */
function getMockedWebApi(): any {
  const xrm = (window as any).Xrm;
  if (!xrm?.WebApi) throw new Error("installMocks() did not run before render");
  return xrm.WebApi;
}
/* eslint-enable @typescript-eslint/no-explicit-any */

export function App(): React.ReactElement {
  const s = useStyles();
  const webApi = React.useMemo(() => getMockedWebApi(), []);
  return (
    <div className={s.page}>
      <Title3>{Project} — Harness</Title3>
      <Caption1 block>Open DevTools → Console for Xrm/auth logs</Caption1>
      <section className={s.widgetFrame}>
        <{ComponentName} webApi={webApi} {...sensibleDefaults} />
      </section>
    </div>
  );
}
```

**Variant B — Two components (widget + code page) → tab-switch**:

Reference: `c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat/src/App.tsx` is the canonical example. Adapt that pattern — `TabList` + `Tab` with `selectedValue` state, conditional render of each surface.

**Variant C — PCF control**:

Add `import { makePcfContext, makeDatasetParameter } from "../../../_infra/mocks";` and build a context object before render:

```tsx
const ctx = makePcfContext({
  parameters: { /* per-control input props */ },
});

<{PcfReactComponent} context={ctx} />
```

**Wire interactive features** (only when user opted in):
- CreateTodoWizard: import from `@spaarke/ui-components`, add `wizardOpen` state, `setWizardOpen(true)` on the component's add callback, mount conditionally at App level
- Refetch on wizard close: capture refetchRef from the widget's `onRefetchReady` prop, call it in wizard `onClose`

---

### Step 7: Configure `vite.config.ts`

The template's vite.config.ts already imports `sharedLibsAlias`, `sharedLibsDedup`, `transpileSharedLibPaths`. No edits needed UNLESS you added a brand-new shared lib that needs a non-standard transpile pattern.

---

### Step 8: Set `SPAARKE_REPO_ROOT` Env Var

Print BOTH PowerShell and bash instructions:

```
PowerShell (one-time per session):
  $env:SPAARKE_REPO_ROOT = "{worktree-path}"

PowerShell persistent (recommended — saves it to user env):
  [System.Environment]::SetEnvironmentVariable("SPAARKE_REPO_ROOT", "{worktree-path}", "User")

bash:
  export SPAARKE_REPO_ROOT="{worktree-path}"
```

Skip if `{worktree-path}` matches the framework default (`c:/code_files/spaarke-wt-smart-todo-r4`).

---

### Step 9: Install + Build Verify

```powershell
cd c:/code_files/spaarke-prototype/projects/{project}-uat
npm install
npx vite build
```

If build fails:
- `Cannot find module '@spaarke/X'` → alias missing in `_infra/vite.shared-libs.ts` (Step 4)
- `installMocks is not defined` → wrong relative path in `src/main.tsx`
- `Two copies of React` → missing dedup entry in `sharedLibsDedup`

---

### Step 10: First Run

```powershell
npm run dev
```

Browser opens at `localhost:5173`. Verify:
- Component renders without console errors
- Seeded data visible (if a real preset was chosen)
- DevTools console shows `[xrm-mock]`, `[auth-mock]` logs (verbose mode)

---

### Step 11: Report + Next Steps

```
✅ Harness ready at: c:/code_files/spaarke-prototype/projects/{project}-uat/

To iterate:
  cd c:/code_files/spaarke-prototype/projects/{project}-uat
  npm run dev   # → http://localhost:5173

Edit production source in: {worktree-path}/src/...
  → HMR reloads the harness within ~200ms on save

To extend seed data with a new entity:
  Invoke: prototype-harness-extend
  (Or edit _infra/seed/factories/{entity}.ts manually)

Master-deploy is still required for final UAT:
  - Cross-tab BroadcastChannel
  - Real OBO auth refresh
  - MDA chrome sizing inside the actual Custom Page iframe
  - Drag-drop persistence across browser refresh
```

---

## Anti-Patterns (do NOT do)

| ❌ Don't | Why |
|---|---|
| Copy production component source into the harness | The Vite alias views it; duplication = drift |
| Put project-specific mocks inside `projects/{name}-uat/src/` | They belong in `_infra/seed/factories/` so other harnesses reuse them |
| Add seed data to production code paths to make tests "easier" | Seed data is sandbox-only; production code never knows about it |
| `git commit` production component code from the prototype repo | Production code lives in the worktree; commit there |
| Skip master-deploy because "the harness looks fine" | Harness can't test Xrm-real behaviours; deploy is the final gate |
| Wire `bffBaseUrl` to a real BFF URL for wizard testing | Use empty string + mocked `authenticatedFetch` (which returns predictable 200s) |

---

## Failure Modes & Recovery

| Symptom | Cause | Fix |
|---|---|---|
| `Cannot find module '@spaarke/X'` on startup | Alias missing or path typo in `_infra/vite.shared-libs.ts` | Add/fix the alias entry; verify the file exists at the resolved path |
| Component imports `Xrm` and crashes on render | `installMocks()` called AFTER `ReactDOM.render` | Move the `installMocks()` call to the top of `src/main.tsx`, before mount |
| HMR doesn't fire when editing worktree files | `transpileSharedLibPaths` doesn't cover the edited dir | Add the dir to the array in `_infra/vite.shared-libs.ts` |
| Two copies of React error | Missing dedup | Add the package name to `sharedLibsDedup[]` |
| Empty list everywhere despite data being seeded | OData `$filter` syntax not supported by mock | Look for `[xrm-mock] unsupported filter:` warnings in console; either simplify the query OR extend the parser in `_infra/mocks/xrm.ts` |
| Wizard opens but Create button errors | Mock `authenticatedFetch` returns 200-but-empty; wizard expected a response payload | Either accept the error for visual demo OR override the route in `installMocks({ auth: { routes: {...} } })` |
| Skill ran but user can't see seed data | They haven't run `npm run dev` yet, OR their preset returned empty records | Run dev server; check `_infra/seed/presets/<name>.ts` returns non-empty `records.{entity}` |

---

## Related Skills

| Skill | Relationship |
|---|---|
| `prototype-harness-extend` | Add new entities/factories to the seed (run AFTER this skill if you need a new entity) |
| `prototype-experiment-init` | For Mode 1 greenfield UX experiments (no production source yet) |
| `worktree-setup` | Creates the production worktree this skill aliases into |
| `task-execute` | Production code edits happen here while the harness reloads |

---

## Reference

- Harness framework branch: `feature/uat-harness-framework` in `c:/code_files/spaarke-prototype`
- Framework design doc: `c:/code_files/spaarke-prototype/projects/_framework-setup-2026-06/README.md`
- Template source: `c:/code_files/spaarke-prototype/projects/_templates/prod-component-harness/`
- Shared infra: `c:/code_files/spaarke-prototype/_infra/` (mocks + seed + vite alias)
- Canonical consumer (reference impl): `c:/code_files/spaarke-prototype/projects/smart-todo-r4-uat/`
- Developer guide: `c:/code_files/spaarke-prototype/docs/PROTOTYPE-UI-SYSTEM-GUIDE.md`
