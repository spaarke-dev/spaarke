# G — Add Visual Host "Upcoming To Dos" to the Matter main form

> **Task**: R4-081 (smart-todo-r4)
> **Date**: 2026-06-10
> **Workstream**: G (Visual Host on parent forms)
> **Status**: ready for live-deploy in spaarkedev1
> **Predecessors**:
> - R4-080 — 4 chart-def JSONs + deploy script (`Create-UpcomingTodosChartDefinitions.ps1`)
> - R4-034 — `useLaunchContext` extended with `openTodos` discriminator + `parseDataParams()` envelope consumption
> - R4-051 — RegardingResolver PCF bound to To Do form + pre-save handler (CREATE-mode bridge in place)

This document is the maker checklist for mounting the `Spaarke.Visuals.VisualHost` virtual PCF on the `sprk_matter` main form and binding it to the `Upcoming To Dos — Matter` chart definition created in R4-080. The form-designer work itself is a live action performed in spaarkedev1 — this checklist captures the exact steps so a maker (Ralph or successor) can execute it without ambiguity.

**R4 source code does NOT change in this task.** The deliverables are (1) this instructions doc and (2) form-designer changes captured in solution XML at deploy time (R4-092).

The companion tasks 082 / 083 / 084 mount the same control on Project / Invoice / WorkAssignment forms — those tasks reuse this checklist with per-form value substitutions called out in §10.

---

## Summary of deliverables in this task

| Artifact | Path | Status |
|---|---|---|
| Matter chart def record `Upcoming To Dos — Matter` | (Dataverse `sprk_chartdefinition` row, deployed via R4-080 PS script) | Live record once script runs in spaarkedev1 |
| Form-mount checklist (this doc) | `projects/smart-todo-r4/notes/g-matter-form-visualhost-instructions.md` | NEW (this task) |
| `sprk_matter` main form change | Live form-designer mod on `sprk_matter` main form | Live action — user executes per §1–§7 |
| Solution XML capture | (deferred to R4-092 export step) | Deferred |

---

## Why a separate "Upcoming To Dos" section (one-paragraph context)

Per FR-31 and FR-36, Smart To Do R4 surfaces a curated card list of upcoming + pinned `sprk_todo` records on the 4 parent entity forms (Matter / Project / Invoice / WorkAssignment). The card list is rendered by the existing virtual PCF `Spaarke.Visuals.VisualHost` (v1.4.16), the same PCF used for every other VH-driven card in the Spaarke UX. The chart def is the env-portable contract layer: chart-def text fields (`sprk_contextfieldname`, `sprk_drillthroughtarget`, `sprk_fetchxmlquery`, `sprk_visualtype`) carry all the business logic; the form-designer step just binds one VH instance per form. Drill-through opens the SmartTodo Code Page modal (FR-34) pre-filtered to the current Matter via the `useLaunchContext` translator (extended in R4-034 to consume VisualHost's auto-injected `entityName` / `filterField` / `filterValue` / `mode=dialog` keys).

A dedicated section makes the card visually distinct from the rest of the Matter form (Related, Documents, etc.) and lets the form designer position it where users will see it on cold-open of the record.

---

## 0. Hard prerequisites (verify before opening form designer)

Execute these BEFORE step 1. If any fail, the form binding will not work.

| Prereq | Verification | Remedy |
|---|---|---|
| **Matter chart def deployed** — record `Upcoming To Dos — Matter` exists in spaarkedev1 with all 5 contract fields | Run query: `pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -DryRun` then live-deploy if not present (see `projects/smart-todo-r4/notes/g-chart-def-deploy-notes.md` §"Live deploy command") | Run the live `Create-UpcomingTodosChartDefinitions.ps1` per R4-080 deploy notes |
| **`Spaarke.Visuals.VisualHost` PCF v1.4.16+ deployed** | Confirm via `make.powerapps.com` → Solutions → search for "Visual Host" or for the publisher solution that ships it; OR run `pac solution list \| grep -i visualhost` | Re-import the solution shipping `Spaarke.Visuals.VisualHost` |
| **SmartTodo Code Page web resource `sprk_smarttodo.html`** registered and published | Solution explorer → Web resources → search `sprk_smarttodo` | Re-import / re-deploy the SmartTodo Code Page solution. **CRITICAL**: must be registered WITH the `.html` suffix per R4-003 spike §5.1 — VisualHost's drill-through branch detection keys off `.toLowerCase().endsWith('.html')` |
| **`useLaunchContext` hook supports `openTodos` discriminator** — already shipped via R4-034 | Code path: `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` (471 LOC, 22 tests) — on master via PR #377 squash `eed39e40a` | No remedy needed — already live |
| **Old `UPCOMING TASKS` chart visual instance to be replaced** (chart def `154bd4a4-f359-f111-a825-3833c5d9bcab`) | Open Matter main form in form designer → check whether existing Visual Host control mounted | Note the existing instance's position so the new instance can replace it (see §3) |

---

## 1. Open the `sprk_matter` main form in the form designer

1. Sign in to `https://make.powerapps.com` as a maker with customize-system permission in the spaarkedev1 environment.
2. Top-right environment switcher → confirm **spaarkedev1**.
3. Left nav → **Tables** → search **Matter** → click `sprk_matter`.
4. Tab strip → **Forms**.
5. Click the main form named **Information** (or whichever main form is configured as primary; verify the form **Type** column shows **Main**).
6. The modern form designer opens.

> **Reference**: the same form was the host for the legacy `UPCOMING TASKS` visual (chart def `154bd4a4-...`). If that visual is still mounted, plan to remove it as the final step in §6 (NOT before — verify the new card renders first).

---

## 2. Add an "Upcoming To Dos" section

The R4 spec (FR-36) calls for a NEW dedicated section labeled "Upcoming To Dos". The section MUST be a single-column container so the VisualHost card can fill its width gracefully (the card adapts to container width).

**Steps**:

1. In the form designer left tree, navigate to the tab that should host the new section. Recommended: the main / general tab where Related / Activities and the existing Visual Host cards live. If unsure, ask the form owner. The default Matter form has a "Summary" / "General" tab at the leftmost position — use that.
2. Right-click the target tab → **Add section** → choose **1-column section**.
3. The new section appears at the bottom of the tab. Drag it to the desired position. Recommended position: above any "Related" tab content and below the basic Matter detail fields (Name, Status, Owner). If a `UPCOMING TASKS` visual already exists in this tab, place the new section ABOVE it (the legacy section will be removed in §6).
4. In the right-hand properties panel, set:
   - **Label**: `Upcoming To Dos`
   - **Name**: `tab_general_section_upcomingtodos` (or any consistent name — the maker tooling auto-prefixes per solution publisher; verify Name uses snake_case and is unique on the form)
   - **Hide label on form**: **unchecked** (the section header is the visible card title since `showTitle` on the VH instance can stay false)
   - **Visibility**: **Default visible**
5. Click **Done**.

---

## 3. Add the `Spaarke.Visuals.VisualHost` virtual PCF to the section

VisualHost is bound to a column. Per the established Spaarke pattern (see existing VH instances on Matter form for KPI / Document counts / etc.), the binding column must be a field that exists on the host entity and is NOT used for any other purpose by the form. A common convention is to bind to a hidden text column the entity already exposes (e.g., `sprk_visualhostbindingfield` if it exists) — but for R4 we can bind to the entity's **primary name field** (`sprk_name`) and rely on the `chartDefinitionId` static input to drive the actual chart. (See §3.1 for rationale.)

### 3.1 Why bind to the primary name field

VisualHost's `chartDefinition` property is a `Lookup.Simple` to `sprk_chartdefinition`, but per the v1.1.0+ manifest, a static `chartDefinitionId` input (SingleLine.Text) takes precedence when the lookup is empty. R4 G uses the **static `chartDefinitionId` input** path because:

- The Matter form doesn't have a dedicated `sprk_chartdefinition` lookup field (and adding one just for VH binding would clutter the schema).
- Multiple VH cards per form is supported via the static-ID pattern (per VH v1.1.0 release notes captured in `ControlManifest.Input.xml` line 21–26 description).
- The static-ID approach is what the existing `UPCOMING TASKS` instance uses on Matter today.

**Implication**: VH must still be added to a column (PCF rule). Any hidden text column on `sprk_matter` works. The primary name field is universally present and the binding does NOT cause VH to read the column's value (the lookup branch wins precedence-wise, and we use the static-ID input).

If a dedicated hidden text column for VH bindings exists on `sprk_matter` (e.g., one previously added by KPI cards), prefer that — it makes intent clear at form-designer reading time. Open the form tree and scan for an existing column with VH already bound to it; if present, mirror its binding pattern.

### 3.2 Mount the VH control

1. In the form tree, locate the binding column on the form (e.g., the primary name field — `sprk_name` — or the hidden VH-binding column if one exists). If the primary name field is the chosen binding target, you may need to drag it onto the form temporarily so it can be selected — drag it into a hidden tab/section, then continue.
2. Click the field to select it.
3. Right-hand panel → **Components** tab → **+ Component**.
4. Search for `Visual Host`. The control should appear under **Code components** as `Spaarke.Visuals.VisualHost` (v1.4.16+).
5. Click **Add**. The control properties panel opens.
6. Set the input properties:

| Property | Value | Notes |
|---|---|---|
| `chartDefinition` (bound lookup) | (leave empty) | We use the static-ID input instead. |
| `chartDefinitionId` (input) | **GUID of the `Upcoming To Dos — Matter` chart def record** | Retrieve via the deploy-script output OR via a one-time query (see §3.3). Per NFR-03, this value is env-specific BUT the chart def name (`Upcoming To Dos — Matter`) is portable; the GUID changes per env. Capture and treat as env config. |
| `contextFieldName` (input) | **`sprk_matterid`** | VisualHost auto-injects the current record's primary ID into the drill-through `filterValue` and uses this field to know WHICH parent ID to substitute. Per VisualHost manifest line 29–34, this is the field name on the **VIEWING entity** that holds the parent record ID — for the Matter form, that's `sprk_matterid` (Matter's primary key). When VH passes `filterValue=<current-matter-id>`, the SmartTodo Code Page filters on the chart def's `sprk_contextfieldname=sprk_regardingmatter`. |
| `fetchXmlOverride` (input) | (leave empty) | The chart def's `sprk_fetchxmlquery` is the live query. |
| `height` (input) | `300` | Recommended height in pixels; tune for usability. The Due Date Card List visual type (100000009) self-sizes its cards within the height envelope. |
| `width` (input) | (leave empty) | Auto-fill section width. |
| `justification` (input) | `left` | Standard alignment. |
| `columnPosition` (input) | (leave empty) | Not in a multi-column layout. |
| `columns` (input) | (leave empty) | Auto. |
| `valueFormatOverride` (input) | (leave empty) | Use chart def default. |
| `showTitle` (input) | **`false`** | The form section label "Upcoming To Dos" already provides the title. |
| `titleFontSize` (input) | (leave empty) | Irrelevant when `showTitle=false`. |
| `showVersion` (input) | **`true`** | Standard — small version badge in lower-left aids deployment verification per `CLAUDE.md` Version Footer Requirement. |
| `showToolbar` (input) | **`true`** | Provides the expand-to-modal button — REQUIRED for drill-through per FR-34. |
| `enableDrillThrough` (input) | **`true`** | REQUIRED — gates the toolbar-expand click handler that calls `Xrm.Navigation.navigateTo`. |
| `appInsightsKey` (input) | (leave empty — or set per env config) | Optional telemetry. |

7. Under **Show component on**: check **Web**. (Phone/Tablet are out of scope for R4.)
8. Click **Done**.
9. If the binding column was the primary name field and you moved it into a hidden section in step 3.2.1, leave it there — DO NOT hide the section/tab containing the VH control, but DO hide the original field-label if it overlaps visually. The VH control replaces the column's default widget render with the chart card.

### 3.3 Get the `Upcoming To Dos — Matter` chart def GUID

Run from the repo root (PowerShell 7):

```powershell
$env = "https://spaarkedev1.crm.dynamics.com"
$token = az account get-access-token --resource $env --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; Accept = "application/json" }
$uri = "$env/api/data/v9.2/sprk_chartdefinitions?`$select=sprk_chartdefinitionid,sprk_name&`$filter=$([uri]::EscapeDataString("sprk_name eq 'Upcoming To Dos — Matter'"))"
(Invoke-RestMethod -Uri $uri -Headers $headers).value | Format-Table sprk_name, sprk_chartdefinitionid
```

Copy the GUID (without braces) and paste into the `chartDefinitionId` input in §3.2 step 6.

> Per NFR-03, **do not hardcode this GUID into R4 source code**. The form designer is allowed to hold the env-specific GUID because it lives in solution XML and is updated per-env at solution import time (or via a publisher-side post-import hook). For env-to-env promotion, the receiving env's deploy procedure must update the chart-def reference in the imported form XML (or re-mount the VH instance with the new env's GUID).

---

## 4. Publish form + chart def + (if needed) web resource

1. Click **Save** at the top of the form designer.
2. Click **Publish**.
3. Click **Back** to return to the form list.
4. Top of the solution view (or the Tables > Matter view) → click **Publish all customizations**. This publishes the form change AND ensures any cached chart def metadata in the runtime is refreshed.

> If the `sprk_smarttodo.html` web resource was newly published as part of the R4-040 SmartTodo Code Page deployment, also confirm it's published (Solutions → SmartTodo Code Page → Publish all customizations).

---

## 5. Smoke test in spaarkedev1

> **Environment**: spaarkedev1. **Browser**: Edge or Chrome, signed in as a user with at least Read access to `sprk_matter` and `sprk_todo`, and access to at least 1 published Matter record that has 1+ regarding-Matter `sprk_todo` records.

### Smoke A — section + card render

1. Open the spaarkedev1 model-driven app (whichever app surfaces Matter; typically `Spaarke`).
2. Navigate to **Matter** → open a Matter record that has at least 1 `sprk_todo` regarding it.
3. **Expected**:
   - The "Upcoming To Dos" section is visible in the tab where it was added (typically the main / general tab).
   - Under the section header, the VisualHost card renders a list of due-date cards for matching `sprk_todo` records (state=Active, statuscode in {Open, In Progress}, and either duedate within next 5 days OR `sprk_todopinned=true`) per the chart def's FetchXML + the VH-injected `sprk_regardingmatter eq <current-matter-id>` filter.
   - If the Matter has 0 matching `sprk_todo` records, the VH shows an empty-state placeholder (depending on VH version, may be "No items" or a blank well — not an error).
4. **PASS criterion**: section visible + card renders OR empty state shown gracefully.

### Smoke B — drill-through opens SmartTodo Code Page modal pre-filtered

1. With the same Matter open, click the **expand** icon (or whichever toolbar button on the VH instance triggers drill-through). The icon may appear as a maximize/full-screen button in the card's top-right corner.
2. **Expected**:
   - A modal dialog overlay opens (centered, ~90% × 85% of viewport per VH's hardcoded `width: 90%, height: 85%` per `VisualHostRoot.tsx handleExpandClick`).
   - Inside the modal, the SmartTodo Code Page (`sprk_smarttodo.html`) loads.
   - The SmartTodo Kanban is **pre-filtered** to only the current Matter's `sprk_todo` records (via `useLaunchContext` decoding VH's `data` envelope: `entityName=sprk_todo&filterField=sprk_regardingmatter&filterValue=<current-matter-id>&mode=dialog`).
   - The Code Page header may show a small "Filtered: <Matter name>" indicator (per R4-034's `openTodos` discriminator surface) OR the filter is implicit in the data shown.
3. **Console verification** (open DevTools BEFORE clicking expand):
   - Click expand → in the dropdown next to "Console", switch to the iframe (the entry containing `sprk_smarttodo`).
   - Run:
     ```javascript
     console.log("location.search =", window.location.search);
     ```
   - **Expected**: `?data=entityName%3Dsprk_todo%26filterField%3Dsprk_regardingmatter%26filterValue%3D<guid>%26mode%3Ddialog`
4. **PASS criterion**: Modal opens AND Kanban displays only the current Matter's `sprk_todo` records.

### Smoke C — drill-through opens modal NOT entity list view

1. Re-do Smoke B and confirm the modal is the SmartTodo Code Page (a custom React layout with Kanban columns) — **NOT** the OOB Dataverse entity grid view for `sprk_todo`.
2. **PASS criterion**: VisualHost rendered the web-resource modal, not the entity-list modal (this confirms the chart def's `sprk_drillthroughtarget = "sprk_smarttodo.html"` was honored).

### Smoke D — modal close returns control to Matter form

1. Inside the modal, click the close button (X) or press ESC.
2. **Expected**: Modal closes, Matter form remains on screen, no unsaved-changes prompt (because the modal is a read-only-by-default surface — though card actions inside the Kanban may have modified records).
3. **PASS criterion**: Clean close without errors.

### Smoke E — new Matter (CREATE form) graceful behavior

1. Click **+ New Matter** in the app.
2. **Expected**:
   - The form opens in CREATE mode (record not yet saved).
   - The "Upcoming To Dos" section is visible BUT the VH card may show an empty state OR a "Save the record first" message OR may simply render nothing (VH's behavior on `entityFormContext.entityId === undefined` is to render no data — confirmed by reading VH source).
   - No JavaScript errors in console.
3. Fill in the Matter name + Status; save. Open the saved record.
4. **Expected**: Card now renders normally (CREATE-mode behavior of VH on save is to re-fetch chart data with the new record's ID).
5. **PASS criterion**: No JS errors on CREATE; card recovers gracefully on save.

---

## 6. Remove the legacy `UPCOMING TASKS` chart visual instance (if present)

**Only after Smokes A–D PASS**. The legacy `UPCOMING TASKS` visual driven by chart def `154bd4a4-f359-f111-a825-3833c5d9bcab` is superseded by the new "Upcoming To Dos" card.

**Steps**:

1. Re-open the `sprk_matter` main form in the form designer.
2. Locate the legacy visual:
   - Form tree → search for an existing VisualHost control instance with `chartDefinitionId = 154bd4a4-f359-f111-a825-3833c5d9bcab` (or its section label `UPCOMING TASKS` — case may vary).
   - If unsure, click each existing VH instance in turn and check its `chartDefinitionId` input value.
3. Click the legacy VH instance → right-click → **Remove** (or the component-section's trash icon).
4. If the legacy instance had its own section, also remove the empty section (right-click section header → **Remove**).
5. Save + Publish form.
6. Re-run Smoke A to confirm only the new "Upcoming To Dos" card renders.

> If the legacy `UPCOMING TASKS` visual is NOT present (e.g., it was removed in a prior cleanup or this Matter form is a fresh import), skip this section.

---

## 7. Capture solution XML (for source-of-truth + deploy)

The form change is now live in spaarkedev1. Per the R4-092 deploy plan, the form change must be captured in solution XML so it can be promoted to other envs (and so R4-098's wrap-up PR can include the solution XML diff for review).

**Recommended approach** (managed/unmanaged solution patterns):

1. In `make.powerapps.com` → **Solutions** → open the solution that owns the `sprk_matter` form. For Spaarke, this is typically the `Spaarke.Matter` solution (or `Sprk.Matter.Forms`, depending on env-specific publisher conventions).
2. Verify the recently-modified form appears as a **dirty** entry (form metadata version incremented).
3. Click **Export solution** → **Unmanaged** (so the form XML diff is readable in the repo).
4. After export completes, download the `.zip`.
5. From the repo root, extract the solution to the SmartTodo solution dir using PAC:

   ```powershell
   # Verify the target solution dir exists
   ls src/solutions/Spaarke.Matter/
   # Or if a different solution name:
   ls src/solutions/

   # Extract (overwriting prior version)
   pac solution unpack `
     --zipfile <downloaded-solution>.zip `
     --folder src/solutions/Spaarke.Matter `
     --processCanvasApps `
     --allowDelete
   ```

6. `git diff src/solutions/Spaarke.Matter/` should show the form-XML diff including:
   - New section node with the `Upcoming To Dos` label
   - New `customcontrol` element with namespace `Spaarke.Visuals.VisualHost`
   - The `chartDefinitionId` input value (will hold a GUID specific to spaarkedev1)
   - The `contextFieldName` input value `sprk_matterid`
7. **Stage + commit** only the relevant form XML files (NOT the entire solution if other unrelated dirty entries are present). Example:

   ```powershell
   git add src/solutions/Spaarke.Matter/SolutionPackage/src/Entities/sprk_matter/FormXml/main/Information.xml
   # Verify
   git status
   ```

8. Commit message convention (do NOT commit during this task — main session handles commits):
   ```
   chore(R4-081): Capture sprk_matter main form change — Visual Host "Upcoming To Dos" mounted
   ```

> **GUID env-specificity note** (per NFR-03): the `chartDefinitionId` GUID in the form XML is spaarkedev1's. When promoting to other envs, either (a) run a substitution step in the env-specific deploy pipeline to replace the GUID, or (b) re-mount the VH instance per-env. The deploy procedure for tasks 082 / 083 / 084 (parallel for Project / Invoice / WorkAssignment forms) must follow this same convention.

---

## 8. NFR-09 deploy-trail content (for R4-092 / final PR)

When R4-092 runs, the PR description must identify the deployed form change. The required content (paste into the PR description's "Deployed form changes" section):

```
R4-081 deployed surfaces:
- MODIFIED — sprk_matter main form "Information"
  - NEW SECTION: "Upcoming To Dos" (tab_general_section_upcomingtodos)
  - NEW VH INSTANCE: Spaarke.Visuals.VisualHost bound to <binding-column>
    * chartDefinitionId = <env-specific GUID for "Upcoming To Dos — Matter" chart def>
    * contextFieldName = sprk_matterid
    * showToolbar = true, enableDrillThrough = true, showVersion = true
  - REMOVED: legacy UPCOMING TASKS VH instance (chart def 154bd4a4-...) — if present prior to R4
- UNCHANGED: Spaarke.Visuals.VisualHost PCF v1.4.16 (no source change)
- UNCHANGED: Chart def "Upcoming To Dos — Matter" (deployed via R4-080 PS script)
- UNCHANGED: SmartTodo Code Page (sprk_smarttodo.html) (deployed via R4-040)
- UNCHANGED: useLaunchContext hook (R4-034 extension)
```

---

## 9. Rollback procedure

If the form change causes issues (VH fails to render, drill-through error, etc.) in spaarkedev1:

1. **Quick fix (5 min)**: open the form designer, locate the VH control on the binding column, click → **Remove**. Restore the original column's default widget. If a new section was added, remove it. Save + Publish.
2. **Full rollback (form)**: re-import the previous version of the `Spaarke.Matter` (or equivalent) solution to restore the prior form definition. The legacy `UPCOMING TASKS` visual (if it was removed in §6) returns automatically.
3. **Chart def issue isolation**: if the VH renders but cards are wrong/missing, suspect the chart def — verify the `sprk_fetchxmlquery` content + the `sprk_contextfieldname` + `sprk_drillthroughtarget` values per `g-chart-def-deploy-notes.md` §"Verification". The chart def can be edited live in `make.powerapps.com` → Tables → Chart Definition → row → edit text fields → save (no form re-publish needed).
4. **Drill-through issue isolation**:
   - If drill-through opens the modal but Kanban shows ALL todos (no filter): suspect `useLaunchContext` regression. Run R4-034's 22 executable-spec tests (`src/solutions/SmartTodo/src/hooks/__tests__/useLaunchContext.test.ts`) against current source.
   - If drill-through opens the OOB entity list view instead of the Code Page modal: suspect chart def `sprk_drillthroughtarget` is not exactly `sprk_smarttodo.html` (must include `.html`). Verify per R4-080 deploy notes §"Schema gotcha".
   - If drill-through fails entirely (no modal opens): check browser console — likely the `sprk_smarttodo` web resource is not registered with the `.html` suffix in this env. Re-deploy SmartTodo Code Page.

---

## 10. Per-form value substitutions for 082 / 083 / 084

Tasks 082, 083, 084 follow the EXACT same checklist with these substitutions:

| Task | Form | Chart def name | `contextFieldName` (VH input) | Owning solution (assume current) |
|---|---|---|---|---|
| **R4-081** (THIS) | `sprk_matter` main form | `Upcoming To Dos — Matter` | `sprk_matterid` | `Spaarke.Matter` |
| **R4-082** | `sprk_project` main form | `Upcoming To Dos — Project` | `sprk_projectid` | `Spaarke.Project` (verify name) |
| **R4-083** | `sprk_invoice` main form | `Upcoming To Dos — Invoice` | `sprk_invoiceid` | `Spaarke.Invoice` (verify name) |
| **R4-084** | `sprk_workassignment` main form | `Upcoming To Dos — Work Assignment` | `sprk_workassignmentid` | `Spaarke.WorkAssignment` (verify name) |

**Per-form chart def GUID**: each task pulls a different GUID from the chart def query in §3.3 (substitute the `sprk_name` filter).

**Per-form smoke test target**: each form requires its own test record with at least 1 `sprk_todo` regarding-row of the respective parent type. Verify the resolver-set fields persisted from R4-051 work (i.e., `sprk_regardingproject` / `sprk_regardinginvoice` / `sprk_regardingworkassignment` on the test `sprk_todo` records).

---

## Open notes for follow-up tasks

### For R4-082 / R4-083 / R4-084 (next wave — DO NOT do in R4-081)

- Reuse this checklist verbatim with the substitutions table in §10.
- The smoke test set (A–E) applies identically per form.
- The deploy-trail content for §8 must be reproduced per form.
- If 082/083/084 are run in parallel by separate agents, EACH must capture solution XML to a DIFFERENT solution dir (per the table in §10). No file conflict expected.

### For R4-092 (deploy task)

- Verify the chart-def deploy script ran for all 4 parent types (only Matter is in R4-081's scope).
- Per the env-specific GUID note in §3.3 / §7: confirm the import procedure to other envs substitutes the GUID OR re-mounts the VH instance per-env.
- Verify `Spaarke.Visuals.VisualHost` PCF v1.4.16+ is in the target env BEFORE importing the form change.

### For R4-093 (UI test suite — NFR validation)

- NFR-05 (modal nav latency < 500ms): measure the VH expand-click-to-modal-paint time on Matter form. Acceptance: < 500ms cold, < 200ms warm.
- NFR-07 (a11y): verify the VH expand button is keyboard-accessible (Tab into card, Enter to expand) AND the modal traps focus on open.
- NFR-08 (orientation switch): irrelevant to R4-081 (this is the Kanban orientation toggle; tested per R4-070).

---

*End of G form-mount checklist (Matter). Task R4-081 complete when this checklist has been executed in spaarkedev1 and all five smokes (A–E) pass; the solution XML for the form change is captured to the repo via §7.*
