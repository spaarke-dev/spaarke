# Current Task State — Smart To Do R5

> **Last Updated**: 2026-08-17 (deploy pass complete; INBOUND fix in progress)
> **Recovery**: Read "Quick Recovery" first. Branch `work/smart-todo-r5`, all deploy-pass work committed+pushed through **`226ee6154`** (46 behind origin/master — not yet synced).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Core project 20/28 + modal spine done. UAT-fix + deploy loop with operator. **All 3 pending deploys are now DONE.** Then a NEW blocker surfaced: the **INBOUND** event-sourced-todo bug (operator wants it resolved before wrap-up). |
| **Status** | ✅ **Deploy pass COMPLETE** (2026-08-17): all 4 UAT items + date-search LIVE on spaarkedev1. Operator re-testing. Now implementing the **INBOUND** fix (BFF). |
| **Next Action** | Implement the **INBOUND fix — Option A gated** (see "INBOUND" below). It's a BFF change (Path-A exception to BFF=N, operator-authorized). Branch is **46 behind origin/master** — consider Update-Only sync before/with the BFF work (TodoGenerationService.cs identical to master; DataverseServiceClientImpl.cs +43 on master, stub NOT yet throwing). |

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
