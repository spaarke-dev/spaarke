# RESUME AFTER COMPACTION — ai-spaarke-ai-workspace-UI-r1

> **Date saved**: 2026-06-10 (full session refresh — supersedes all prior versions)
> **Active branch**: `feature/ai-spaarke-ai-workspace-UI-r1`
> **Active PR**: #372
> **HEAD**: `275add458` (pushed)
> **Worktree dir**: `c:/code_files/spaarke` (the main repo path)
> **Uncommitted**: 3 `package-lock.json` files (CalendarSidePane, EventDetailSidePane, TodoDetailSidePane) from npm installs — safe to discard or commit-as-noise
> **Deployment target**: `https://spaarkedev1.crm.dynamics.com`

---

## CRITICAL CONTEXT — READ FIRST

### 1. This branch's work was OVERWRITTEN in spaarkedev1 today
A parallel Claude session (likely on `work/spaarke-ai-platform-unification-r6`)
deployed an old version of `sprk_spaarkeai` after this branch's deploys,
silently reverting all iter-2 work and the box-sizing chain from operator view.
**Restoration was performed** at the end of this session:

- Rebuilt SpaarkeAi from HEAD `275add458` (verified bundle has all markers: `matters-list`, `BriefcaseSearchRegular`, `filterChipButton`, `WidgetErrorBoundary`, `1cdd19d2-3964`, `gridTableOverride`).
- Redeployed via `scripts\Deploy-SpaarkeAi.ps1` (3704 KB).
- User asked to verify via hard-refresh in Incognito — **pending verification at compaction time**.

### 2. ALL parallel branches are the SAME user (you) running parallel Claude sessions across worktrees
This changes the coordination model. There's no "other developer" to slack —
all overwrites are session-to-session collisions on a shared spaarkedev1.

### 3. The fix proposed at compaction time was a "deploy ledger" pattern
Not yet implemented. Documented below in §7 so it survives compaction.

---

## What this branch shipped (all committed + pushed at HEAD 275add458)

### Phase A — Static hardening (commits `fe4d37389`, `2aeb2d98`)
- `tsconfig.base.json` at repo root — shared TS strict settings
- 3 surface tsconfigs (SpaarkeAi, DailyBriefing, WLW) extend the base
- `scripts/tsc-surface-gate.mjs` — surface-scoped tsc gate (filters to `src/**` errors; shared-lib errors non-fatal)
- Wired into SpaarkeAi + DailyBriefing `npm run build` (WLW deferred — 24 pre-existing owned errors)
- `pdfjs-dist` + `mammoth` added to SpaarkeAi package.json (dynamic imports via SprkChat hook)

### Phase B — Runtime resilience (commit `1240fd65`)
- `AppErrorBoundary` in `@spaarke/ui-components/src/components/AppErrorBoundary/` — self-contained FluentProvider, surface name, onError, fallback props
- All 3 Code Page entry points wrapped (SpaarkeAi, DailyBriefing, WLW)
- `safeRegister` helper in `@spaarke/ui-components/src/utils/safeRegister.ts`
- 26/28 registration calls wrapped (`register-workspace-widgets.ts` 21 calls + `register-context-widgets.ts` 5 calls)

### Phase D — Per-widget boundary + telemetry (commit `ff66d005`)
- `WidgetErrorBoundary` in `@spaarke/ui-components/src/components/WidgetErrorBoundary/`
- `reportClientError` helper in `@spaarke/ui-components/src/services/reportClientError.ts`
- `AppInsightsService.trackException` method added
- AppErrorBoundary + safeRegister wired through reportClientError
- `<Widget />` mount in WorkspaceTabManagerComponent wrapped in WidgetErrorBoundary
- SpaarkeAi + DailyBriefing bootstrap AppInsightsService from `VITE_APP_INSIGHTS_KEY` env var
- vite-env.d.ts files declare the env var

### Iteration 2 slices (3 commits)
- **Slice 1** (`17e1ca67`): DataGrid `ResizeObserver` responsive column sizing in `DataGrid.tsx`
- **Slice 2** (`a00a8b03`): Matters widget chain
   - `LegalWorkspace/src/sections/matters.registration.ts` (new)
   - `sectionRegistry.ts` + `sections/index.ts` + `sectionMetadataCatalog.ts` updated (`BriefcaseSearchRegular` icon)
   - `register-workspace-widgets.ts` registers `matters-list` direct widget via `safeRegisterWidget`
- **Slice 3** (`472bb36d`): Calendar filter-chip rewrite
   - `CalendarWorkspaceWidget.tsx` filter row → Popover-anchored chip toolbar
   - Event Type / Status / Date Field chips use RadioGroup popovers
   - Date Range chip with From/To inputs + Quick Select presets
   - Calendar day-click + chip edits share `pending.fromDate/toDate` state

### Round 2-11 DataGrid layout fixes (10 commits — the 11-round saga)
**Final pattern that works:**
1. `box-sizing: border-box` reset in host `index.html` (round 11 — primary fix)
2. `min-width: 0` on every flex/grid ancestor from workspace row → SectionPanel.card → DataverseEntityViewWidget root → DataGrid root → innerCard → gridScroll
3. ResizeObserver pixel cap on DataverseEntityViewWidget root
4. Griffel `!important` override of FluentDataGrid's hardcoded `min-width: fit-content` (`gridTableOverride` class)
5. 2-pass column-math redistribution + per-cell `visibleColumns.length × 24px` padding reserve
6. Width-bucket key on FluentDataGrid for re-mount on big resizes

Each surface that hosts DataGrid MUST have item 1. Without it cells render `+24px` wider than declared.

### Documents config row swap (commit `389e63f8` + `e2ac8d0a`)
- Created new `sprk_gridconfiguration` row `1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` ("Active Documents (Workspace)", `sprk_document`, points at OOB "Active Documents" savedquery `82d75343-…`)
- Updated `ENTITY_VIEW_CONFIG_IDS.documents` in `register-workspace-widgets.ts` to the new GUID
- ALSO updated `documents.registration.ts` in LegalWorkspace (the LW-side shim) to the new GUID — this was the second-pass fix; the LW shim had its own hardcoded constant separate from the direct-widget registration

### Box-sizing audit cleanup (commits `b547ae03`, `69193486`, `275add45`)

**Audit results** (from `notes/box-sizing-reset-audit.md`):
17 of 23 Code Pages were MISSING the canonical reset.

**Wave 1 — DataGrid hosts (deployed)**:
- AllDocuments (1185 KB) + Reporting (1777 KB)

**Wave 2 pilot (deployed, user-verified clean)**:
- CreateTodoWizard (1498 KB)
- Preemptive: DailyBriefing + WorkspaceLayoutWizard (reset needed before CI gate wired)

**Wave 3 (12 surfaces — code committed; 11 of 12 deployed)**:
| Surface | Build | Deploy |
|---|---|---|
| CreateEventWizard | ✅ | ✅ via WizardCodePages |
| CreateMatterWizard | ✅ | ✅ |
| CreateProjectWizard | ✅ | ✅ |
| CreateWorkAssignmentWizard | ✅ | ✅ |
| SummarizeFilesWizard | ✅ | ✅ |
| FindSimilarCodePage | ✅ | ✅ |
| PlaybookLibrary | ✅ | ✅ |
| SmartTodo | ✅ | ✅ via Deploy-SmartTodo |
| SpeAdminApp | ✅ | ✅ via Deploy-SpeAdminApp |
| EventDetailSidePane | ✅ | ✅ |
| TodoDetailSidePane | ✅ | ✅ inline deploy |
| **CalendarSidePane** | ❌ | ❌ pre-existing build orphan |

**CalendarSidePane orphan**: commit `dfabe436a` deleted its `src/components/` as part of the R4 hoist to `@spaarke/events-components`, but `App.tsx` line 23 still imports `{ CalendarSection, type CalendarFilterOutput } from "./components"`. **Fix**: change to `from "@spaarke/events-components"`. Reset is already in CalendarSidePane/index.html waiting for the build to be repaired. Tracked in `notes/followup-backlog.md` §7.

### CI gate (commit `b547ae03`)
- `scripts/check-html-css-reset.mjs` — regex-validates `*, *::before, *::after { box-sizing: border-box }` in a Code Page's index.html
- Wired into SpaarkeAi, DailyBriefing, WLW `package.json` build scripts
- Clear failure message + remediation tip
- Other surfaces opt-in by adding the gate call to their `build` script

### Shared-lib hygiene (commit `b547ae03`)
Cleared 32 pre-existing deferred tsc errors:
- **12 TS2307 wrong import paths** — `@spaarke/{ai-outputs|ui-components}/src/X` → `/X` (path map already expands `/src/`)
  - Fixed in `register-workspace-widgets.ts`, `CreateMatterWizardWidget.tsx`, `DocumentUploadWizardWidget.tsx`
- **20 TS2503 `ComponentFramework` namespace** — added `@types/powerapps-component-framework@^1.3.4` to SpaarkeAi devDeps so SpaarkeAi's `typeRoots: ['./node_modules/@types']` picks it up
- **Re-enabled** `noUnusedLocals: true` + `noUnusedParameters: true` at `tsconfig.base.json`
- Cleaned 3 newly-surfaced surface-owned errors:
  - SpaarkeAi: 2 dead `capturedHighlightCalls` / `highlightCalls` vars in ContextPaneController.test.tsx
  - DailyBriefing: 1 dead `_handleNavigate` useCallback in App.tsx

Post-cleanup state:
- SpaarkeAi: 0 owned / 71 shared-lib errors (deferred for next-pass cleanup)
- DailyBriefing: 0 owned / 65 shared-lib errors
- WLW: 27 owned errors (no gate; pre-existing real type bugs)

### Documentation (commit `976871e3`)
- `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §9.1 NEW — embedded workspace-widget hosts (5 contract rules + diagnostic script + reference impls)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` §7.1 NEW — sizing & layout chain (4-step contract + boilerplate + anti-patterns)
- `.claude/patterns/ui/embedded-widget-sizing.md` NEW — 25-line pointer file; indexed in parent INDEX.md
- `projects/ai-spaarke-ai-workspace-UI-r1/notes/box-sizing-reset-audit.md` — the 17-host audit results + recommended follow-up

---

## Followup backlog (NOT in this PR — see `notes/followup-backlog.md`)

These came out of operator testing feedback today and exceed this PR's scope:

1. **Modal-preview record-open standard for ALL dataset grids** (HIGH — recommended new project `dataset-grid-modal-preview-r1`)
   - Today row-click → `Xrm.Navigation.openForm` opens a new tab
   - Wanted: in-page modal showing entity main view, with browse-records chevrons + toolbar dropdown (matches the Document Preview pattern from SemanticSearchControl PCF)
   - Exceptions: Documents use existing preview modal; To Do uses Kanban Code Page
2. **Documents widget "New" → UploadDocumentWizard** (MEDIUM)
   - Today: opens OOB new form
   - Fix needs: custom command handler registered in SpaarkeAi/main.tsx + DataGrid framework's custom-handler context surface verification + sprk_gridconfiguration JSON patch
3. **Find Similar Documents 403 + iframe login prompt** (HIGH operational)
   - 403 is Dataverse RetrievePrincipalAccess on the source document — operator permission grant
   - Login prompt suggests DocumentRelationshipViewer iframe MSAL bootstrap not sharing cache
4. **CreateTodoWizard placement / Quick Start card** (MEDIUM)
   - Ribbon DisplayRule is `<FormStateRule State="Existing" />` — intentional; only shows on saved Matters
   - Wanted: also add to Quick Start widget in SpaarkeAi (action card in `getStarted.registration.ts`)
5. **Summarize Files bugs** (HIGH)
   - Email step → 400 (payload validation failure)
   - Project step → created but missing Project Number + no linked Documents
6. **CalendarSidePane build orphan** (LOW)
   - One-line fix; documented above
7. **Cross-cutting CI gate adoption** — extend `check-html-css-reset.mjs` to a top-level CI workflow

---

## Cross-branch deploy coordination state

**Master HEAD**: `a2ac6a849` (= `work/smart-todo-r4` just merged via PR #374 at 14:24 today)

### Active branches (8 worktrees, all you in parallel Claude sessions)

| Branch | Worktree path | Ahead | Behind | State |
|---|---|---|---|---|
| `feature/ai-spaarke-ai-workspace-UI-r1` | `c:/code_files/spaarke` | 33 | 6 | THIS branch — pushed, 3 uncommitted noise files |
| `work/spaarke-ai-platform-unification-r6` | `c:/code_files/spaarke-wt-spaarke-ai-platform-unification-r6` | 42 | 2 | **Very active** — heavy SpaarkeAi UI work (Pillar 6c events, Pillar 9 visibility, widget schema-aware dispatch). LIKELY OVERWROTE SPAARKEAI TODAY. |
| `work/spaarke-multi-container-multi-index-r1` | `c:/code_files/spaarke-wt-spaarke-multi-container-multi-index-r1` | 49 | 5 | **Very active** — BFF resolver + PCF + Code Page deploys. TASK-INDEX mentions deploys done today, UAT pending. |
| `work/smart-todo-r3-wrap-up` | `c:/code_files/spaarke-wt-smart-todo-decoupling-r3` | 5 | 0 | Active, docs only + R4 design — up-to-date with master |
| `work/email-communication-solution-r3` | `c:/code_files/spaarke-wt-email-communication-solution-r3` | 2 | 11 | Slow — planning artifacts only |
| `work/ai-spaarke-action-engine-r1` | `c:/code_files/spaarke-wt-ai-spaarke-action-engine-r1` | 1 | 363 | Dormant scaffold — last touched 2026-05-30 |

### Deploy collision diagnosis

The SpaarkeAi overwrite today (12:51 deploy → user saw old behavior) was almost
certainly caused by a deploy from `work/spaarke-ai-platform-unification-r6` after
my 12:51 deploy. R6's name ("ai platform unification") + recent commits on
SpaarkeAi UI (Pillar 9 visibility, widget dispatch) point directly there.

**Database confirms**: `modifiedon` of sprk_spaarkeai showed 12:51 (my deploy),
but the bundle content the user saw was OLD. Either someone deployed AFTER me
without updating the timestamp (unlikely), OR the user was hitting cache, OR
the 12:51 deploy itself overwrote a brief r6 deploy I didn't see.

**Current state (end of session)**: I redeployed SpaarkeAi from this branch's
HEAD `275add45` — verified markers present. User asked to hard-refresh in
Incognito to confirm. Confirmation pending at compaction time.

### Proposed deploy ledger pattern (NOT YET IMPLEMENTED)

To prevent silent overwrites between your parallel sessions:

**Location**: `~/.spaarke-deploy-ledger.json` (outside repo so all worktrees share it)

**Shape**:
```json
{
  "spaarkedev1": {
    "sprk_spaarkeai": {
      "branch": "ai-spaarke-ai-workspace-UI-r1",
      "commit": "275add458",
      "deployed_at": "2026-06-10T14:32:00Z"
    },
    "sprk_reporting": { ... }
  }
}
```

**Helper**: `scripts/deploy-ledger.mjs` exposes `recordDeploy(env, webResource, branch, commit)` and `peekLastDeploy(env, webResource)`.

**Patch each deploy script** to:
1. Read the ledger before deploying
2. If last deploy of THIS web resource was from a different branch, print a clear warning ("⚠ sprk_spaarkeai was last deployed from r6/97f7e6bcb at 14:42 — you're overwriting with ai-spaarke-ai-workspace-UI-r1/275add45. Continue? y/N")
3. On confirmation, write to ledger, then deploy
4. Patch list: `Deploy-SpaarkeAi.ps1`, `Deploy-DailyBriefing.ps1`, `Deploy-WizardCodePages.ps1`, `Deploy-ReportingCodePage.ps1`, `Deploy-SmartTodo.ps1`, `Deploy-SpeAdminApp.ps1`, `Deploy-EventDetailSidePane.ps1`, `Deploy-CalendarSidePane.ps1` (~8 scripts)

**Estimated work**: 60-90 min to implement + test.

### Master deploy strategy (PROPOSED)

**Wave A — Ready to merge** (low risk, low conflict):
- `feature/ai-spaarke-ai-workspace-UI-r1` (this)
- `work/smart-todo-r3-wrap-up` (docs only, 0 behind)

Action: rebase each on master, merge PRs, deploy from master.

**Wave B — Negotiate before merging** (high in-flight, possible SpaarkeAi conflict):
- `work/spaarke-ai-platform-unification-r6` (42 ahead)
- `work/spaarke-multi-container-multi-index-r1` (49 ahead)

Action: read each branch's commit log + impact-on-SpaarkeAi diff vs this branch.
Decide merge order to minimize merge conflicts on `SpaarkeAi/`, `LegalWorkspace/`,
`@spaarke/ui-components/`, `@spaarke/ai-widgets/`.

**Wave C — Triage** (slow / dormant):
- `work/email-communication-solution-r3` — confirm still active; rebase or park
- `work/ai-spaarke-action-engine-r1` — 363 behind, near-scaffold; likely close + archive

---

## Pending verification at compaction time

1. **SpaarkeAi redeploy** — user hard-refreshes in Incognito; confirms Matters widget mountable, Calendar chip toolbar back, DataGrid columns fit container
2. **Cross-branch conflict assessment** — has `r6` touched the same SpaarkeAi files this branch did? Quick test: `git log origin/master..work/spaarke-ai-platform-unification-r6 --name-only -- src/solutions/SpaarkeAi/ src/client/shared/Spaarke.AI.Widgets/ src/client/shared/Spaarke.UI.Components/ src/solutions/LegalWorkspace/` from the r6 worktree

---

## How to resume after compaction

### Action 0 — Re-orient
1. Read this file end-to-end.
2. `git log --oneline -3` in `c:/code_files/spaarke` to confirm HEAD is still `275add45`.
3. Confirm with user: did the SpaarkeAi redeploy from end-of-session restore visible behavior?

### Action 1 — Cross-branch conflict assessment (15-20 min)
For each of the two heavy active branches (r6 + multi-container), run:
```
git log origin/master..<branch> --name-only -- src/solutions/SpaarkeAi/ src/client/shared/ src/solutions/LegalWorkspace/
```
Catalog overlap with files this branch modified. If r6 also touched
`SpaarkeAi/main.tsx` / `LegalWorkspace/src/sectionRegistry.ts` / `register-workspace-widgets.ts` etc., that's a merge conflict to plan for.

### Action 2 — Decide on deploy ledger implementation
If user wants the ledger pattern, implement it per §7 above. ~60-90 min.

### Action 3 — Wave A master deploy
- Coordinate with user: which branch first (smart-todo-r3-wrap-up is small/clean; this branch is bigger; either order works)
- Rebase, merge PR, deploy from master
- Verify spaarkedev1 looks right

### Action 4 — Followup item triage
Items 1–6 in `followup-backlog.md` need owners + projects. User decides which (if any) to start in-session vs. defer to new projects.

---

## File index (everything in this PR that matters)

### New files
- `scripts/check-html-css-reset.mjs`
- `scripts/tsc-surface-gate.mjs`
- `tsconfig.base.json`
- `src/client/shared/Spaarke.UI.Components/src/components/AppErrorBoundary/AppErrorBoundary.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/WidgetErrorBoundary/WidgetErrorBoundary.tsx`
- `src/client/shared/Spaarke.UI.Components/src/services/reportClientError.ts`
- `src/client/shared/Spaarke.UI.Components/src/utils/safeRegister.ts`
- `src/solutions/LegalWorkspace/src/sections/matters.registration.ts`
- `src/solutions/DailyBriefing/src/vite-env.d.ts`
- `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §9.1 (added)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` §7.1 (added)
- `.claude/patterns/ui/embedded-widget-sizing.md`
- `projects/ai-spaarke-ai-workspace-UI-r1/notes/box-sizing-reset-audit.md`
- `projects/ai-spaarke-ai-workspace-UI-r1/notes/followup-backlog.md`
- This file

### Modified files (key entries — full list via `git log --name-only`)
- `src/solutions/SpaarkeAi/index.html` (round 11 box-sizing reset)
- `src/solutions/SpaarkeAi/src/main.tsx` (AppErrorBoundary + AppInsights bootstrap)
- `src/solutions/SpaarkeAi/src/vite-env.d.ts` (VITE_APP_INSIGHTS_KEY)
- `src/solutions/SpaarkeAi/package.json` (devDeps: powerapps-component-framework types; scripts: typecheck, check:html-reset, build chain)
- `src/solutions/SpaarkeAi/tsconfig.json` (extends base)
- `src/solutions/DailyBriefing/index.html`, `src/main.tsx`, `package.json`, `tsconfig.json`, `src/App.tsx` (dead code removed)
- `src/solutions/WorkspaceLayoutWizard/index.html`, `src/main.tsx`, `package.json`, `tsconfig.json`
- All 12 Wave 3 surfaces' `index.html` (box-sizing reset added)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (rounds 6-11 layout chain + 2-pass column math + width-bucket key + gridTableOverride + responsive useLayoutEffect)
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/SectionPanel.tsx` (round 7 — min-width: 0 on card)
- `src/client/shared/Spaarke.UI.Components/src/services/AppInsightsService.ts` (trackException)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DataverseEntityViewWidget.tsx` (round 9 pixel-cap wrapper + min-width: 0 + width: 100%)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` (matters-list registration, `/src/` removed from import paths, safeRegisterWidget wrapper)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateMatterWizardWidget.tsx` (`/src/` removed)
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/DocumentUploadWizardWidget.tsx` (`/src/` removed)
- `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts` (safeRegisterContext wrapper)
- `src/solutions/LegalWorkspace/src/sections/documents.registration.ts` (DOCUMENTS_CONFIG_ID → `1cdd19d2-3964-…`)
- `src/solutions/LegalWorkspace/src/sectionRegistry.ts` (mattersRegistration added)
- `src/solutions/LegalWorkspace/src/sections/index.ts` (export mattersRegistration)
- `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` (slice 3 chip toolbar)
- `tsconfig.base.json` (noUnusedLocals re-enabled)

### Deploy scripts touched
- `scripts/Deploy-SpaarkeAi.ps1` (no changes; canonical)
- All wizard deploys went through `scripts/Deploy-WizardCodePages.ps1`
- Dedicated scripts used: `Deploy-SmartTodo.ps1`, `Deploy-SpeAdminApp.ps1`, `Deploy-EventDetailSidePane.ps1`, `Deploy-ReportingCodePage.ps1`
- Inline deploys (no script): AllDocuments, TodoDetailSidePane, WLW

---

## Important Dataverse state in spaarkedev1

### Web resources currently deployed (as of end of session)

| Web resource | Branch | Bundle KB | Deployed at | Status |
|---|---|---|---|---|
| `sprk_spaarkeai` | this | 3704 | end of session redeploy | ✅ restored from overwrite |
| `sprk_alldocuments` | this | 1185 | 13:39 | ✅ |
| `sprk_reporting` | this | 1777 | 10:36 | ✅ |
| `sprk_dailyupdate` | this | 1570 | 2026-06-09 12:12 | ⚠️ DailyBriefing not redeployed since Wave 3 reset added |
| `sprk_workspacelayoutwizard` | this | 1769 | 13:39 | ✅ |
| `sprk_createtodowizard` | this | 1498 | 13:39 | ✅ |
| `sprk_creatematterwizard` etc. (6 wizards) | this | various | 13:39 | ✅ via WizardCodePages |
| `sprk_playbooklibrary` | this | 1495 | 13:39 | ✅ |
| `sprk_findsimilar` | this | 1374 | 13:39 | ✅ |
| `sprk_summarizefileswizard` | this | 1525 | 13:39 | ✅ |
| `sprk_documentuploadwizard` | this | 1075 | 13:39 | ✅ |
| `sprk_smarttodo` | this | 814 | 13:43 | ✅ |
| `sprk_speadmin` | this | 1803 | 13:52 | ✅ |
| `sprk_eventdetailsidepane` | this | 1555 | 13:54 | ✅ |
| `sprk_tododetailsidepane` | this | 1103 | 13:56 | ✅ |
| `sprk_calendarsidepane` | n/a | n/a | (skipped — build orphan) | ❌ |

### Dataverse data changes (still present)

- `sprk_gridconfiguration` row `1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` ("Active Documents (Workspace)") — created during round 3 fixes for the Documents widget; points at OOB "Active Documents" savedquery `82d75343-…`
- Existing row `d99a4352-…` ("Semantic Search Documents View") UNCHANGED — left for SemanticSearchControl PCF
- Existing row `113ad380-9e63-…` ("Active Matters (Workspace)") for Matters — created earlier in iter-2 slice 2 work

---

## Active task list at compaction

```
[completed] SpaarkeAi redeployed from current HEAD (3704 KB; markers verified)
[in_progress] USER: hard-refresh in Incognito; confirm Matters widget + chip toolbar + DataGrid layout are back
[pending] Plan: master deploy workflow for all active project branches
```

The "Plan: master deploy workflow" task is what the user wanted addressed when
they invoked the resume-doc save. The proposed plan + ledger pattern are in §7
above; not yet implemented.

---

*End of resume doc. After /compact, the next session re-loads this file as its
first action and continues from §"How to resume after compaction".*
