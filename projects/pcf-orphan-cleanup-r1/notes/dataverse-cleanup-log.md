# Dataverse cleanup log

> **Project**: pcf-orphan-cleanup-r1 · **Spec**: FR-07 · **Procedure**: [§3 Part C](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#3-part-c--dataverse-cleanup-single-session-gated)
> Entries per environment. Times are UTC.

---

## spaarkedev1 — Task 003 — 2026-10-14 (session ran 2026-10-15 02:31–02:35 UTC)

- **Environment**: `https://spaarkedev1.crm.dynamics.com` (PAC profile [3], `ralph.schroeder@spaarke.com`; Web API via `az account get-access-token`). No other environment was touched.
- **Owner approval**: 2026-10-14, "yes clean up the PCFs".
- **Guardrails held**: no Publish All Customizations; no SpaarkeMaster import; the only import was the scoped resurrection-drill import (`scripts/Import-SolutionScoped.ps1`, which published 2 web resources of `SpaarkeEventCalendarFilter` and nothing else); no Key Vault, app-setting or BFF change.
- **Pre-flight**: the June 2026 4-check was stale and was redone in full against the live environment and `origin/master` `2e4772606`. Results and every hold are in [`preflight-deviations.md`](preflight-deviations.md) DEV-002 to DEV-007.

### Fresh backups (taken before any deletion)

Folder: [`backups-2026-10-14/`](../backups-2026-10-14/). 24 ZIPs, 23.2 MB. Each has an unmanaged and a managed export. `solution.xml` `<Managed>` was checked on every file, and each target's `ControlManifest.xml` + `bundle.js` or `.msapp` was confirmed inside the ZIP it relies on.

| Backup (`baseline-{name}-{unmanaged,managed}-pre-cleanup-2026-10-14.zip`) | Unmanaged bytes | Covers | In git |
|---|---:|---|:---:|
| `PcfOrphanBackupAnalysisBuilder` (temp solution: AB PCF + canvas `40af8`) | 161,614 | AnalysisBuilder PCF + canvas. **Closes the June "no backup" gap** | yes |
| `PcfOrphanBackupCanvasPages` (temp solution: canvas `e48a5` + `786f4`) | 97,028 | the two other canvas pages | yes |
| `DueDatesWidget` | 15,600 | DueDatesWidget | yes |
| `SpaarkeEventCalendarFilter` | 12,648 | EventCalendarFilter | yes |
| `SpaarkeLegalWorkspace` | 752,944 | LegalWorkspace PCF | yes |
| `SpaarkeSpeDocumentViewer`, `EventAutoAssociateSolution`, `EventFormControllerSolution`, `FieldMappingAdminSolution`, `RegardingLinkSolution` | 5 KB – 254 KB | held controls (reference only) | yes |
| `SpaarkeCore` (v1.1.0.0) | 5,676,556 | aggregator that held the 3 canvas pages | **no** (local only, see note) |
| `SPRKDOCINTELLIGENCE` (v1.0.0.0) | 4,917,595 | aggregator that held the 3 canvas pages | **no** (local only, see note) |
| `AnalysisWorkspaceSolution` | n/a | **export fails**: "No WebResource for CustomControlResourceId {f3757ca8-4924-4d66-9c91-84723b03d7f3}". The June backup `backups-2026-06-22/baseline-AnalysisWorkspaceSolution-…` remains the only copy (DEV-005) | — |

Note: the June convention committed every backup ZIP (about 5 MB). The two aggregator exports total about 21 MB with both flavours, and recovery does not need them, because the targeted temp-solution backups hold the exact components removed from those aggregators. They stay in the worktree folder `backups-2026-10-14/` and are not committed (`*.zip` is gitignored; the committed ZIPs are force-added).

The two temp solutions existed only for the backup. They became empty as the deletions ran and were deleted at 02:34:41 and 02:34:44 UTC (re-query: 0 rows). Unmanaged-solution deletion removes no components.

### Method per deletion (procedure §3.2, Web API)

1. Immediate pre-delete gate: `RetrieveDependenciesForDelete(ObjectId, ComponentType)`. **Any** dependency stops the run (script exit 2), so each hosting canvas page was deleted before its PCF.
2. `RemoveSolutionComponent` from every unmanaged named solution (Default and Active are virtual).
3. `DELETE customcontrols({id})` or `DELETE canvasapps({id})`.
4. Verify: GET by id returns 404; a name query returns 0 rows; `solutioncomponent` rows for the object id = 0.

Deviations from §3.2: step 3 (unpublish canvas) was skipped because a custom page cannot be unpublished through the API and deletion removes it outright. The procedure orders deletions UQC first, but UQC was already absent (DEV-006), so the order was ECF (with the drill), then AB canvas → AB PCF → DDW → LW → DTW canvas → EP canvas.

### Entries (FR-07)

- 2026-10-15 02:32 — `sprk_Spaarke.Controls.EventCalendarFilter` v1.0.5
  - §1.2 results: refs = none. Live: no dependency; no form, view, sitemap or web-resource content match. Master: no reference (only a doc comment in `Spaarke.Events.Components` CalendarSection.tsx and VisualHost Storybook stories that import a folder already deleted, tracked in #1468).
  - Hosting canvas: none
  - Solutions touched: SpaarkeEventCalendarFilter (component removed; the solution remains)
  - Deletion confirmed: PCF GUID `0ceec858-928e-439b-8c8b-a9b2013c7c29` deleted 02:32:04; GET 404, name rows 0, solutioncomponent rows 0
  - Backup: `backups-2026-10-14/baseline-SpaarkeEventCalendarFilter-{unmanaged,managed}-pre-cleanup-2026-10-14.zip`
  - **Resurrection drill (NFR-07): PASS.** See the section below. The resurrected copy (GUID `ed6fef95-8118-4853-a207-4728a0650df2`) was re-deleted at 02:33:02 and verified gone (404 / 0 / 0).
  - Smoke test: pending owner (checklist item 1)

- 2026-10-15 02:33 — canvas `sprk_analysisbuilder_40af8` ("Analysis Builder")
  - §1.2 results: refs = none. No dependency. No master reference; no live code navigates to any custom page by name (`pageType: "custom"` appears only in a comment in `sprk_analysis_commands.js:564`).
  - Hosting canvas: this is the hosting canvas, for AnalysisBuilder PCF
  - Solutions touched: SPRKDOCINTELLIGENCE, SpaarkeCore, PcfOrphanBackupAnalysisBuilder (temp)
  - Deletion confirmed: canvas GUID `f077022c-96c4-48ba-9bdd-b6af4b035a1e` deleted 02:33:18; 404 / 0 / 0
  - Backup: `baseline-PcfOrphanBackupAnalysisBuilder-*` (`CanvasApps/sprk_analysisbuilder_40af8_DocumentUri.msapp`), plus SpaarkeCore and SPRKDOCINTELLIGENCE local exports
  - Smoke test: pending owner (checklist item 5)

- 2026-10-15 02:33 — `sprk_Spaarke.Controls.AnalysisBuilder` v2.9.2
  - §1.2 results: refs = only the hosting canvas `f077022c-…` before step 1 above; none at the gate. No master reference (`sprk_analysisbuilder` in SpaarkeMaster is the separate code-page web resource, which still exists).
  - Hosting canvas: `sprk_analysisbuilder_40af8` (deleted first)
  - Solutions touched: PcfOrphanBackupAnalysisBuilder (temp; otherwise only Default)
  - Deletion confirmed: PCF GUID `dfc82d5b-9842-4639-b388-f57a06e57f2d` deleted 02:33:30; 404 / 0 / 0
  - Backup: `baseline-PcfOrphanBackupAnalysisBuilder-*` (`Controls/sprk_Spaarke.Controls.AnalysisBuilder/bundle.js` 195,062 B)
  - Smoke test: pending owner (checklist item 5)

- 2026-10-15 02:33 — `sprk_Spaarke.Controls.DueDatesWidget` v1.0.8
  - §1.2 results: refs = none. Master: only synthetic sample data in `tests/scripts/SpaarkePackageScope.Tests.ps1`, which is not a live reference.
  - Hosting canvas: none
  - Solutions touched: DueDatesWidget
  - Deletion confirmed: PCF GUID `bac653dd-ea07-4401-93ee-6d5654d11d0f` deleted 02:33:45; 404 / 0 / 0
  - Backup: `baseline-DueDatesWidget-*`
  - Smoke test: pending owner (checklist item 1)

- 2026-10-15 02:33 — `sprk_Spaarke.LegalWorkspace` v1.0.1
  - §1.2 results: refs = none. Master: `src/solutions/SpaarkeAi/package.json:49` names the shared library folder `client/shared/Spaarke.LegalWorkspace`, not this PCF.
  - Hosting canvas: none
  - Solutions touched: SpaarkeLegalWorkspace
  - Deletion confirmed: PCF GUID `6aa7c56d-e47c-4220-9d49-548629546906` deleted 02:33:49; 404 / 0 / 0
  - Backup: `baseline-SpaarkeLegalWorkspace-*`
  - Smoke test: pending owner (checklist item 4)

- 2026-10-15 02:34 — canvas `sprk_visualizationdrillthroughworkspace_e48a5` ("Visualization Drill Through Documents")
  - §1.2 results: refs = none. No dependency. No master reference. No `sprk_chartdefinition.sprk_drillthroughtarget` names it (the 11 targets in use are listed under "Data check").
  - Hosting canvas: this is the canvas (its DrillThroughWorkspace PCF was never deployed)
  - Solutions touched: PcfOrphanBackupCanvasPages (temp), SPRKDOCINTELLIGENCE, SpaarkeCore
  - Deletion confirmed: canvas GUID `cb8dcd7e-517c-4e0e-aaac-90d978d65522` deleted 02:34:02; 404 / 0 / 0
  - Backup: `baseline-PcfOrphanBackupCanvasPages-*`
  - Smoke test: pending owner (checklist item 2)

- 2026-10-15 02:34 — canvas `sprk_eventspage_786f4` ("Events Page")
  - §1.2 results: refs = none. No dependency. No master reference (the live Events page is web resource `sprk_eventspage.html`).
  - Hosting canvas: this is the canvas, the predecessor of the EventsPage code page
  - Solutions touched: PcfOrphanBackupCanvasPages (temp), SPRKDOCINTELLIGENCE, SpaarkeCore
  - Deletion confirmed: canvas GUID `1118454c-4886-4788-9028-3c645dfef97f` deleted 02:34:07; 404 / 0 / 0
  - Backup: `baseline-PcfOrphanBackupCanvasPages-*`
  - Smoke test: pending owner (checklist items 1 and 2)

- 2026-10-15 20:29 — `sprk_Spaarke.Controls.AnalysisWorkspace` v1.3.5 (owner approved 2026-10-15; was held under DEV-005)
  - §1.2 results: `RetrieveDependenciesForDelete` = 0; `dependency` table empty; no form, view, sitemap or app placement; hosting canvas already gone; the ribbon (`sprk_analysis_commands.js`) opens `sprk_spaarkeai`; no master code reference (docs only: `package-scope.json` exclusion, components inventory).
  - Already broken live: 2 of its 3 resources (`bundle.js`, `css/AnalysisWorkspace.css`) pointed at deleted web resources, so the control could not render.
  - Solutions touched: AnalysisWorkspaceSolution, PowerAppsToolsTemp_sprk (RemoveSolutionComponent), then DELETE
  - Deletion confirmed: GUID `286d7717-5ec3-47eb-87ce-c022cec09f2d`; 404 / 0 by name / 0 solutioncomponents / 0 resources; the shared `fluent_9_4_0` web resource is intact; `sprk_` customcontrols 27 → 26
  - Backup: a fresh export is impossible (`ExportSolution` fails on the dangling resource). Use `backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip` (same v1.3.5, sha256 `749a1c128d06…`), plus the row metadata in `backups-2026-10-14/metadata-AnalysisWorkspace-customcontrol-2026-10-15.json`
  - Restore: `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ZipPath projects/pcf-orphan-cleanup-r1/backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip -SolutionUniqueName AnalysisWorkspaceSolution`
  - Left behind: `AnalysisWorkspaceSolution` is now empty (unmanaged). The old `sprk_analysisworkspace` code-page web resource belongs to `ai-advanced-capabilities-analysis-hub-r1` task 071.

- 2026-10-15 20:45 — `sprk_Spaarke.Controls.FieldMappingAdmin` v1.1.0 (owner approved retirement 2026-10-15; was held under DEV-004)
  - §1.2 results: `RetrieveDependenciesForDelete` = 0. No form, view, dashboard, sitemap or web-resource placement among 1,456 forms, 4,304 views and 235 `sprk_*` web resources. Profiles and rules are authored on native forms (6 profiles, 25 rules, untouched after deletion).
  - Solutions touched: FieldMappingAdminSolution (RemoveSolutionComponent), then DELETE
  - Deletion confirmed: GUID `d8aac645-3e55-4300-8510-4c4504c46d5e`; 404 / 0 by name / 0 solutioncomponents / 0 resources; shared `fluent_9_4_0` intact; `sprk_` customcontrols 26 → 25
  - Backup: fresh export `backups-2026-10-14/baseline-FieldMappingAdminSolution-unmanaged-pre-cleanup-2026-10-15.zip` (contains the control's bundle, CSS and manifest) plus `metadata-FieldMappingAdmin-customcontrol-2026-10-15.json`
  - Repo: removed from `infrastructure/dataverse/solutions/FieldMappingAdminSolution` (root component and `Controls/`), so a redeploy of that solution does not bring it back
  - Restore: `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ZipPath projects/pcf-orphan-cleanup-r1/backups-2026-10-14/baseline-FieldMappingAdminSolution-unmanaged-pre-cleanup-2026-10-15.zip -SolutionUniqueName FieldMappingAdminSolution`

### Resurrection drill (NFR-07): PASS

| Step | UTC | Result |
|---|---|---|
| Delete ECF `0ceec858-…` | 02:32:04 | verified gone (404 / 0 / 0) |
| `pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ZipPath …/baseline-SpaarkeEventCalendarFilter-unmanaged-pre-cleanup-2026-10-14.zip -SolutionUniqueName SpaarkeEventCalendarFilter` | 02:32:13 → 02:32:42 | exit 0; "Scoped publish … 2 web resources … No tenant-wide publish was run." |
| Re-query | 02:32 | `sprk_Spaarke.Controls.EventCalendarFilter` v1.0.5 is back with **new GUID `ed6fef95-8118-4853-a207-4728a0650df2`** (createdon 02:32:27), in SpaarkeEventCalendarFilter. This confirms §3.5.2: name-stable, GUID-changing |
| Re-delete | 02:33:02 | verified gone (404 / 0 / 0) |

The POML asked for UQC, and the June plan suggested EventAutoAssociate. UQC was already absent and EventAutoAssociate is held, so ECF, the smallest cleared control, was used.

### Not deleted

| Component | Why | Ref |
|---|---|---|
| `sprk_Spaarke.SpeDocumentViewer` v1.0.29 | Live on the sprk_Document main form; source restored to master by #1453 | DEV-002 |
| `sprk_Spaarke.Controls.EventAutoAssociate` | Live on the sprk_event quick-create form; SpaarkeMaster root component | DEV-003 |
| `sprk_Spaarke.Controls.EventFormController` | Live on 2 sprk_event forms; SpaarkeMaster root component | DEV-003 |
| `sprk_Spaarke.Controls.RegardingLink` | Live in 6 sprk_event views; SpaarkeMaster root component | DEV-003 |
| `sprk_Spaarke.Controls.FieldMappingAdmin` | Master's `FieldMappingAdminSolution` mirror says "retained to avoid regression" | DEV-004 |
| `sprk_Spaarke.Controls.AnalysisWorkspace` | DEV-001 owner hold. Its reason no longer applies (ribbon now opens `sprk_spaarkeai`; canvas host already gone), but deleting waits for owner confirmation | DEV-005 |
| `sprk_Spaarke.Controls.PlaybookBuilderHost` + `sprk_playbookbuilder_eb5ad` | Out of scope (D-03) | — |

Already absent before this session (DEV-006): UQC PCF + `UniversalQuickCreate` solution, canvas `sprk_documentuploaddialog_e52db`, canvas `sprk_analysisworkspace_8bc0b`.

### Post-session verification

- SC-02 query (`startswith(name,'sprk_Spaarke') or startswith(name,'sprk_Sprk')`, componentstate 0): **31 → 27** (DEV-007: 13 was the stale June target).
- All 4 deleted customcontrol names and all 3 deleted canvas names: 0 rows.
- Held controls are unchanged (version and `modifiedon` identical to the pre-session read). PlaybookBuilderHost canvas is still present.
- No leftover web resources are named after the deleted controls. (`sprk_analysisbuilder` is the live code page.)
- **Data check**: the 11 `sprk_chartdefinition.sprk_drillthroughtarget` values in use name no deleted page. One record, "Due Date Count Card" `4929d1ed-…`, has the pre-existing broken value `sprk_eventspage` with no `.html`. It is filed as **#1895** and was not caused by this session.

### Smoke tests (procedure §3.2 step 8): complete (owner, 2026-10-15) — no cleanup regressions

Browser checks need the owner. The checklist is in [`../current-task.md`](../current-task.md#owner-smoke-test-checklist). Each failure below was investigated read-only against spaarkedev1 and master `09dbed8f3`. None was caused by a deletion in this log, so nothing was restored.

| Item | Result | Cause |
|---|---|---|
| 1. Event calendar filter | PASS | — |
| 2a. Calendar drill-through | PASS (dialog filtered to the matter) | — |
| 2a. Calendar day counts | FAIL, pre-existing, not cleanup | VisualHost `CalendarVisual.tsx` never applies the matter context, and chart `154bd4a4-…` uses a FetchXML with the deprecated `sprk_eventstatus` filter and a 5-day/top-10 window (returns 0 rows live). Filed **#1950** (related #1709). |
| 2b. VisualHost drill-through | FAIL, not cleanup; cause not reproduced | VisualHost has used web-resource drill-through since b7da8e4e83 (2026-02-09). No chart definition names a deleted page. The Matter-form charts all target `.html` pages that filter. Filed **#1956** (needs the card and the console lines). |
| 3. Document upload, email send | PASS | — |
| 4. Console renders | PASS | — |
| 4. "Matters workspace" missing | FAIL, pre-existing (since 2026-08-28), not cleanup | Saved layouts owned by the BFF app user have been hidden by #832's owner filter since then. Filed **#1952**. The owner meant a *system* Matters workspace: none had ever existed (skipped in #531 because a user-authored "Matters" layout was then visible to everyone; that record is gone). Created on dev1 as system layout `779bcf42-…` and in `scripts/system-layouts.json` (PR #1955). |
| 5. Analysis entry points | PASS (opens; owner will revisit the Analysis UX later) | — |
| 6. Held controls | PASS | — |
| 7. Console errors | PASS (none) | — |

### Issues filed or updated this session

- #1895: Due Date Count Card drill-through target lacks `.html` (pre-existing, user-visible in dev).
- #1896: dead web resource `sprk_OpenDocumentQuickCreate` still ships in SpaarkeMaster and points at the deleted UQC page.
- #1468: progress comment (what was deleted, what is held, AnalysisWorkspaceSolution export failure).
- #1950: VisualHost calendar ignores matter context; its chart query returns no rows (smoke item 2a, pre-existing).
- #1951: PROJECT BUDGET chart drill-through targets the non-existent `sprk_invoicepage.html` (found during smoke triage, pre-existing).
- #1956: VisualHost drill-through on a Matter opens an unfiltered list (smoke item 2b, cause not reproduced, needs info).
- #1952: saved workspace layouts created before #832 are invisible to their users (smoke item 4, pre-existing since 2026-08-28).
