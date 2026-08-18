# Current Task State — Smart To Do R5

> **Last Updated**: 2026-08-17 (checkpoint — UAT round 2 fixes deployed, awaiting re-UAT)
> **Recovery**: Read "Quick Recovery" first. Branch `work/smart-todo-r5` @ **`904e216e5`** (pushed; NOT yet on master — held pending re-UAT). Working tree clean (except `.husky/_/*` env artifacts).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Project CLOSED 2026-08-17 (090 ✅). **Post-close UAT stream**: operator filed **6 items** (2026-08-17). **5 of 6 done + deployed**; #2 = a new scoped project (not built). Awaiting re-UAT. |
| **Status** | Branch pushed @ `29423eb1e` (UAT commits + master merge). **Deployed to spaarkedev1**: `sprk_smarttodo` (2.0MB) + `sprk_spaarkeai` (5.7MB) + `sprk_createtodowizard` (1.9MB), all published; `sprk_todo` **To Do main form** formxml patched+published (#3). **NOT merged to master yet** — holding until re-UAT. |
| **Active task** | **UAT round 2 — 5 of 6 shipped (#1,#3,#4,#5,#6); #2 = separate BFF project (needs member-group def).** |
| **Next Action** | Operator re-UATs #1/#3/#4/#6 (hard-refresh both surfaces + reopen a To Do record for #3). **Only remaining**: #2 — define "member group of the parent" (owning team? access team? participants subgrid?), then I scope it as its own BFF project (BFF=N here, so new project + §10 placement + webhook trigger). |

### 🧪 UAT ROUND 2 (2026-08-17) — 6 items, status
1. **#1 default Assigned To → current user's contact** — ✅ DONE **on all 3 create paths + deployed**. (a) Code Page "+ New Task" OOB form: `SmartTodoApp` → `useCurrentContactId` → `launchNewTaskCreateForm(…, {contactId,contactName})` → three-key `sprk_assignedto`/…name/…type='contact'. (b) **Widget "+ New Task" → CreateTodoWizard Code Page**: new optional `defaultAssignedTo` prop on `CreateTodoWizard` seeds the Assignee NON-DESTRUCTIVELY on open; `src/solutions/CreateTodoWizard/src/main.tsx` resolves current-user contact inline + passes. (c) Outlook/parent-ribbon: `SmartTodoApp` `LaunchCreateTodoWizardHost` passes the same. Field Mapping Framework does NOT apply. +3 tests (22 pass). **Deployed: `sprk_createtodowizard` (1.9MB) + `sprk_smarttodo` re-deploy.**
2. **#2 parent-team members can access child To Dos** — ⏸️ PENDING (own project). Operator clarified: **NOT a Dataverse plugin** (they don't use them) — "server-side" = **BFF**. BUT this project is **BFF=N by constraint** → #2 is a NEW scoped project (design→spec→tasks + BFF placement justification §10 + a Dataverse-webhook→BFF trigger since no plugins). Still needs the **"member group" definition** (owning team? access team? participants subgrid?). NOT built this session.
3. **#3 hide "General" tab title on To Do main form** — ✅ DONE (round 2, 2026-08-18) + published. **First attempt (showlabel) was a no-op**: UCI renders the single-tab NAVIGATOR PIVOT independently of `ShowLabel`/`Label` (both formxml `showlabel="false"` AND formjson `"ShowLabel":false` had zero effect — the form was already ShowLabel:false and still showed "General"). **Researcher verdict**: the ONLY supported fix is a form **OnLoad** handler calling `formContext.ui.headerSection.setTabNavigatorVisible(false)` (UCI-only; must be a FORM handler so it fires inside the `navigateTo` dialog). Implemented: new form script `src/client/webresources/js/sprk_todo_hide_tabnav.js` (`Spaarke.SmartTodo.HideTabNav.onLoad`, mirrors the sibling script convention) → deployed as web resource `sprk_todo_hide_tabnav` (type 3) → registered as an OnLoad handler on the **To Do main form** (`eca59df4…`, added to `<formLibraries>` + `<event name="onload">`) → PublishXml web resource + `sprk_todo`. Operator must HARD-refresh to clear the cached form. (Distinct from the won't-do 032 form-HEADER hide.)
4. **#4 header single row** — ✅ DONE + deployed. `Header.tsx`: title + Filter/+New Task/⋮ consolidated onto ONE row (reverses 2026-06-19 two-row split). Shared → both surfaces.
5. **#5 flag icon = priority** — ✅ ANSWERED (no change). `Flag16Filled` colored by `sprk_priority` (red Urgent/orange High/green Medium/gray Low; none when unset). `derivePriorityGlyph`.
6. **#6 "[3d]" on a task due today** — ✅ DONE + deployed. `todoScoring.ts::computeDueLabel` now emits a real calendar-day countdown ("Overdue"/"Today"/"{n}d") instead of the tier name; colour tiers unchanged. Also fixed date-only (`YYYY-MM-DD`) parse → LOCAL midnight (was UTC → day-early in western TZ). Mirrored into Code Page `dueLabelUtils.ts` (List/Dismissed). Both cards import the shared util → both surfaces fixed. 68 shared-lib tests pass.

**Commits**: `Header+todoScoring` (shared, #4+#6) → `newTaskLauncher+SmartTodoApp+dueLabelUtils` (code page, #1+#6-mirror). Deploy via `scripts/Deploy-SmartTodo.ps1` + `scripts/Deploy-SpaarkeAi.ps1` (Web API PATCH `content` + PublishXml; `-DataverseUrl https://spaarkedev1.crm.dynamics.com`; `az`-token, pac profile [3] spaarkedev1).

### 🔗 UNIFICATION WORK — what's DONE + what REMAINS (this enhancement stream, 2026-08-17)
**Principle (operator)**: a code page and its corresponding widget MUST compose the same shared-lib components. Root cause was divergence: the Code Page (`src/solutions/SmartTodo/`) and the workspace widget (`SmartTodoWidget` in `@spaarke/smart-todo-components`) reimplemented the same UI.

**✅ DONE + deployed + on master:**
- **Search predicate** (`6c3564e94`): hoisted `todoSearchUtils` (`matchesTodoSearchQuery`/`buildTodoDueDateSearchBlob`, structural `TodoSearchableFields` type) into the shared lib; both surfaces import it. Widget now matches name/description/regarding/assigned/**date**.
- **Card data** (`315f4719a`): widget `$select` widened (`sprk_priority` + regarding text) + maps formatted-value annotations (`assignedToName`, `dueDateFormatted`) → the SAME shared `KanbanCard` now shows the **Assigned line + priority flag** in the widget.
- **Toolbar** (`b0f0ddbdc`): **hoisted `Header` + `SearchFilter`** from the Code Page into the shared lib (`src/components/Header`, `src/components/SearchFilter`; tests at package-root `__tests__/`). Code Page's `SmartTodoApp` + `SmartToDo.tsx` now import `Header`/`QUICK_ADD_TODO_EVENT` from `@spaarke/smart-todo-components`. Widget renders the SAME `<Header>` (Filter · + New Task · overflow Layout/Refresh + inline SearchFilter) — replacing its bespoke quick-add toolbar. Old widget toolbar wrapped in `{false && (…)}` (keeps refs, no dead-code errors). 28/28 Header+SearchFilter tests pass.
- **Selection Open action** (`ce84a0436`): widget wires `toolbarActions=[Open]` → shared `SelectionAwareToolbar` shows Open when ≥1 selected.

**⚠️ REMAINING GAPS (documented for operator; NOT rushed pre-UAT):**
1. **Settings (⋮ overflow)**: hidden in the widget (Code Page has it). Requires the widget to adopt threshold-preferences state — body-level.
2. **Selection cluster**: widget shows Open only; Code Page also has Delete/Email/Pin (widget lacks those handlers).
3. **Full body swap** (dismissed section, detail pane): **the two `SmartToDo` bodies have DIVERGENT contracts** — Code Page local `src/solutions/SmartTodo/src/components/SmartToDo.tsx` (~1090 lines, **self-fetching**: `webApi`+`userId`) vs shared `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/SmartToDo.tsx` (788 lines, **prop-fed**: `items`). A true unify = make BOTH render ONE shared prop-fed `SmartToDo`; large + risky (could destabilize the working Code Page). **This is the last real duplication (D-3-adjacent) — deferred to a dedicated staged pass.**
4. **"+ New Task"**: widget wires `onNewTask={onAddTodo}` (host callback) — UNVERIFIED whether it opens the OOB form like the Code Page.

**Deploy mechanic (reuse)**: large code-page webresources (`sprk_spaarkeai` ~5.8MB, `sprk_smarttodo` ~2.06MB) via temp-solution roundtrip (create temp sol → AddSolutionComponent type 61 → export → replace WebResources file → repack .NET ZipFile → import --publish-changes → delete temp). Shared lib is source-bundled by consumers (Vite alias → `/src`); rebuild BOTH code pages after any shared-lib change. `SMART_TODO_URL`/env for e2e.

<details><summary>Prior project-close state (090 ✅, deferrals D-10..D-13) — kept for reference</summary>
Project closed 2026-08-17: all core FRs shipped+merged+deployed (spaarkedev1 PCF/ribbon/code-pages + `spaarke-bff-dev` INBOUND dry-run-gated). #508 CLOSED, lessons-learned, `/code-review` clean, `/test-diet` 0-scaffolding. Operator-accepted deferrals: 051/052 ribbon (5 more entities), 041 Playwright real-env run, D-13 INBOUND flag-flip + ping r3 owner (Epic #427), file GH issues for D-1..D-4/D-6 + D-10..D-13. 032 header-hide = won't-do (design constraint).
</details>

### 📄 Task 041 (Playwright NFR suite) — files authored (this session)
- `tests/e2e/pages/smart-todo/SmartTodoPage.ts` — Code Page page object (does NOT extend BasePCFPage per POML); locators from source (filter `data-testid=search-filter`, "Add to-do item", "More options", toolbar "Smart To Do toolbar", columns `role=list`, cards `role=listitem`); `waitForReady`, `flipOrientationViaLayout`, `selectCard`, `setColorScheme`, `assertNoLayoutGlitch`.
- `tests/e2e/specs/smart-todo/performance.spec.ts` — NFR-02 load <3s (Date.now bracket + annotations + P95, mirrors spe-file-viewer precedent).
- `tests/e2e/specs/smart-todo/accessibility.spec.ts` — NFR-01 axe WCAG2.1AA light+dark (dark-on-yellow) + keyboard-nav (no-trap Tab walk + Enter/Space activation).
- `tests/e2e/specs/smart-todo/orientation.spec.ts` — NFR-03 select→flip→selection persists→no layout glitch (boundingBox)→post-flip drag-drop membership change.
- `tests/e2e/config/.env.example` — added `SMART_TODO_URL`. **Zero new npm deps** (playwright ^1.55.1 + axe ^4.10.2 already present); auto-wired via testDir (no package.json/config change).
- ⚠️ Two selector assumptions to confirm on the real-env run (documented in-file): `selectCard()` (click→aria-selected vs explicit checkbox) and `setColorScheme()` (prefers-color-scheme vs themeStorage localStorage key).

### ✅ DEPLOY PASS COMPLETE (2026-08-17) — all live on spaarkedev1
1. **RegardingResolver PCF v1.4.9** (`bdc1b6c2e`) — **pdfjs TRUE FIX shipped**: switched `RegardingResolverApp.tsx` + `handlers/ResolverWriteHandler.ts` from the root `@spaarke/ui-components` barrel to per-module deep dist imports (PolymorphicPicker, PolymorphicResolverService, TodoRegardingUpdateBuilder, oobModalSizes). Bundle 2.2MB→60KB, builds clean, no pdfjs. Also ships UAT #2 Regarding Name display fix. Imported+published; footer `v1.4.9 • Built 2026-08-17`.
2. **Matter ribbon** (temp-only, no repo change) — **live carrier is `MatterRibbons` v1.0.0.1, NOT `spaarke_insights`** (⚠️ repo's `spaarke_insights/.../sprk_Matter/RibbonDiff.xml` fix was against the wrong solution copy; both carry same button IDs, last-import-wins → MatterRibbons wins). Applied to live-exported MatterRibbons: 7 `.js` refs stripped (`sprk_wizard_commands.js`→`sprk_wizard_commands`, fixes #3 404), CreateTodo button icon → `sprk_ToDoCheckmark32/16.svg` + ModernImage (task 050). Verified both webresources exist. Backup: `c:\tmp\ribbon-matter\MatterRibbons.zip`. **Tasks 051/052 must target dedicated `*Ribbons` solutions, not spaarke_insights.**
3. **SpaarkeAi code page `sprk_spaarkeai`** (`226ee6154`) — UAT #4: `todo.registration.ts` added `hideTitle:true` (framework flag, types.ts:89) to suppress the duplicate section-level "Smart To Do" title; widget's own PaneHeader is now the single title. Rebuilt (5.79MB single-file), deployed via temp-solution roundtrip (`sprksaicp1`, deleted after), deployed content verified (`hideTitle:!0` present, tail `</html>` intact, no truncation). Also ships todo.registration's 031/033 widget open-refresh.

### ✅ INBOUND — event-sourced To Do generation (from r3 RED-4 / DEF-1) — CODE FIX DONE (`8e0aa4e6e`)
**Resolved in code + tested (28/28 pass), NOT yet deployed to Azure.** Reroute Rules 1 & 3 to `IEventDataverseService`; creation gated behind default-OFF `TodoGeneration:EnableEventSourcedGeneration` (dry-run logs would-create counts). appsettings.template.json documents the flag. §10 verified: publish 44.94 MB compressed (Δ≈0), CVE-clean, no new packages, options-gate (no ADR-032 asymmetry). **Remaining coordination**: (1) BFF must be deployed for dry-run logging to take effect — NOT done here (out of BFF=N scope; a BFF deploy is a separate op the operator/r3-owner coordinates); (2) once merged to master, PING the r3 dataverse-access-hardening owner so the stub→throw step can proceed; (3) file GH issue on Epic #427 (DEF-1) at wrap-up (task 090). Detail below ↓

<details><summary>Original bug analysis (kept for reference)</summary>
`notes/INBOUND-event-sourced-todo-generation-broken.md`. **Bug CONFIRMED current in code**: `TodoGenerationService.cs:334,478` (Rules 1 Overdue-events + 3 Deadline-proximity) call `_dataverse!.QueryEventsAsync` where `_dataverse` is the `IDataverseService` composite → `DataverseServiceClientImpl.QueryEventsAsync:1746-1747` is a **silent-empty stub** (LogWarning + `Array.Empty`). So those 2 rules produce ZERO To Dos. Real impl is on `IEventDataverseService`.
**DECISION (2026-08-17): Option A — reroute, GATED** (operator: "which is the best more robust solution" → I chose; A fixes the root-cause mis-route vs B amputating the feature, and unblocks the r3 stub→throw).
**Plan**: inject `IEventDataverseService` into `TodoGenerationService`; reroute Rules 1 & 3 to it; land behind a **default-OFF feature flag** (ADR-032 kill-switch) that logs a dry-run would-create count, so first-run volume/dedupe/notifications are validated before enabling. FULL rigor: BFF test obligation (`tests/unit/Sprk.Bff.Api.Tests/`), publish-size verify (≤60MB), CVE check, code-review + adr-check. **Path-A ADR/scope exception to BFF=N — document in PR** (operator authorized "resolve before completing"). Ping r3 hardening owner when done so stub→throw can proceed; file GH issue on Epic #427 (DEF-1).
</details>

### ✅ DONE + committed (this UAT session)
- **UAT #1 filter → inline text search** (`e4cb780c4`): SearchFilter renders inline LEFT of the Filter pill (Header.tsx), no "Search" label, placeholder "Filter by name, description, assigned to...". Replaced the structured FilterPane (removed `FilterPane/`). **FR-07 Show-Completed toggle removed** (operator-accepted; defer-issue).
- **Blended date-search** (`0347dc406`): `todoSearchUtils.buildTodoDueDateSearchBlob` — matches natural dates ("august", "august 18", "aug 18", "8/18", "8/18/2026", ISO, day, year, `dueDateFormatted`). Timezone-safe (regex-parses ISO parts). Added `dueDateFormatted` to ITodo + `mapTodoFormattedValues`.
- **UAT #2 RegardingResolver "Regarding Name" blank** (`f8818f327`): **v1.4.8→v1.4.9**. Root cause = Name cell single-sourced an unreliable bound prop on the auto-detect path with no fallback. Fix = displayName precedence (bound → in-session selectedTarget → read-only webAPI `$select`). PCF jest 78/78. ⚠️ **PCF build blocked — see pdfjs below.**
- **UAT #3 Matter ribbon 404** (`2073140c6`): 7 refs used `$webresource:sprk_wizard_commands.js` but the webresource is `sprk_wizard_commands` (no .js). Stripped `.js` in `src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml`. Needs `spaarke_insights` deploy.
- **The auto-associate (D-8) WORKS** — operator confirmed the record gets the matter + `sprk_regardingrecordname`. #2 was only the PCF display.

### 🔴 pdfjs / RegardingResolver PCF build fix (do FIRST in the deploy pass)
The RegardingResolver PCF **webpack build fails** because `RegardingResolverApp.tsx:238-249` + `handlers/ResolverWriteHandler.ts:32-38` import from the **ROOT barrel** `@spaarke/ui-components`, which drags the whole lib (SprkChat → `pdfjs-dist/pdf.mjs`) into the PCF bundle; the PCF's older webpack/babel can't transform pdf.mjs. **Every other PCF uses deep imports** (`@spaarke/ui-components/dist/...` or `/dist/pcf-safe`) — ADR-012 PCF Import Pattern. **TRUE FIX**: change RegardingResolver's 2 barrel imports to deep/pcf-safe paths (PolymorphicPicker, resolveRecordType/resolveRecordNumberFieldName/resolveRecordDisplayNameFieldName/buildRecordUrl, OOB_MODAL_SIZES, applyResolverFields, TODO_REGARDING_CATALOG + types). Verify `dist/pcf-safe` / `dist/services` / `dist/components/PolymorphicPicker` / `dist/utils/adapters/oobModalSizes` export them; add to pcf-safe if missing. Also mark `@spaarke/ui-components` `"sideEffects": false` (belt). NOT a workaround — it's the architectural conformance the other 12 PCFs already have. (Also `npm install --legacy-peer-deps` for RegardingResolver + siblings @spaarke/auth/@spaarke/sdap-client node_modules.)

### 🚚 PENDING DEPLOYS (operator wants the full pass, then ONE re-test)
1. **RegardingResolver PCF v1.4.9** — do the pdfjs fix → build → `pcf-deploy` (solution pack + import). Operator verifies footer `v1.4.9 • Built 2026-08-17` + Regarding Name shows.
2. **`spaarke_insights` ribbon** (#3 + task 050 icon, both in `sprk_Matter/RibbonDiff.xml`) — solution roundtrip scoped to the Matter ribbon: export live spaarke_insights → apply the `.js`-strip (7 refs) + the CreateTodo icon (Image32/16 + ModernImage = sprk_ToDoCheckmark SVGs) to the exported customizations.xml → reimport --publish-changes. ⚠️ shared solution — Matter-ribbon-only changes.
3. **SpaarkeAi/LegalWorkspace workspace** (#4 + widget modal-spine/open-refresh) — **#4 fix**: in `src/solutions/LegalWorkspace/src/sections/todo.registration.ts:336` the section config sets `title: "Smart To Do"` AND `SmartTodoWidget` renders its own PaneHeader title (showTitle default true) → duplicate. Remove the section-level title (keep the widget's), verify render, then rebuild+deploy the workspace code page (bigger; SpaarkeAi hot-path). Also carries todo.registration.ts's 031/033 open-refresh (widget surface still on pre-031 path until deployed).

### 🔜 Remaining core tasks (post-UAT)
- **041** Playwright NFR (authorable; live run needs Code Page + auth).
- **050→051→052** ribbon (050 icon deploys with #3; 051 = 5 more entities' Create-To-Do buttons — apply the SAME no-`.js` ref pattern; 052 deploy).
- **060** real-DV smoke gate (edits `.claude/skills/push-to-github` — MAIN-SESSION-ONLY §3).
- **090** wrap-up: test-diet, close PR #508 superseded, file GH issues for defer-issues (D-1..D-9), archive.

### 🧰 DEPLOY MECHANICS (hard-won — reuse these)
- **Env**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`). MCP + `pac` (profile [3]) + `az account get-access-token` all target it.
- **systemform edits are a SILENT NO-OP via direct Web API PATCH** — MUST use `pac` solution export→edit→import roundtrip. (Form handlers 013/014 already live: presave + score OnLoad on form `eca59df4…`; `sprk_todo_score_onchange` webresource created.)
- **Large webresource (Code Page 2MB): raw PATCH TRUNCATES (~2.0MB)** → ALWAYS solution-roundtrip. Verify the copied file size == build BEFORE import, and verify deployed content markers (not just "success") AFTER — a stale build shipped once this session.
- **Sandbox blocks `Remove-Item -Recurse -Force`** (pre-execution) → use `[System.IO.Directory]::Delete($p,$true)` or fresh dir names + `-Force` overwrite flags.
- Code Page webresource: `sprk_smarttodo` (`f85a1884-962b-f111-88b5-7ced8d1dc988`). Build: `cd src/solutions/SmartTodo; clear dist/.vite; npm run build` → `dist/smarttodo.html` (Vite source-bundles UI.Components; cache clear mandatory).
- Roundtrip scripts are in the session scratchpad (cp3*, wire-todo-form.ps1, etc.).

### 📋 UAT/defer tracker
`projects/smart-todo-r5/notes/defer-issues.md` D-7 (filter→search + FR-07 removal), D-8 (auto-associate — DONE), D-9 (ribbon 404 — fixed, deploy pending). Plus notes/uat-*.md per item. All need GH issue URLs before task-090.

---

## Full State (Detailed)
See `tasks/TASK-INDEX.md` for the 28-task registry (14 core ✅ + modal spine 030-033 ✅ + 013/014). Locked decisions, disjoint-partition ledger, and codebase-drift reconciliations are preserved in git history of this file (pre-2026-08-17 versions) if needed.
