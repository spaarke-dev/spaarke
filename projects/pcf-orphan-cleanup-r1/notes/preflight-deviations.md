# Preflight Deviations — spaarkedev1

> **Task**: 001 — Pre-flight verification
> **Filed**: 2026-06-22
> **Purpose**: Document any pre-flight check that surfaced an unexpected reference, blocking the corresponding control / canvas app from the ready-to-delete list. Per [procedure §1.2](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#12-per-control-still-referenced-check): "Unexpected matches: STOP and reconcile."

---

## DEV-001 — AnalysisWorkspace PCF + Custom Page have a live ribbon dependency

**Discovered**: 2026-06-22 during procedure §1.2 Check 3 (ribbon-JS grep)

**Affected scope**:
- Customcontrol: `sprk_Spaarke.Controls.AnalysisWorkspace` (GUID `286d7717-5ec3-47eb-87ce-c022cec09f2d`) — v1.3.5
- Canvas app: `sprk_analysisworkspace_8bc0b` (GUID `ff384002-a0ea-4253-a2a0-88ae3c49108b`) — the Custom Page hosting the PCF
- Dedicated solution: `AnalysisWorkspaceSolution` (already backed up at [`backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip`](../backups-2026-06-22/baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip))

### Evidence

[`src/client/webresources/js/sprk_analysis_commands.js`](../../../src/client/webresources/js/sprk_analysis_commands.js) defines a ribbon command function `Spaarke_OpenAnalysisWorkspace(analysisId, documentId)` (line 145) that opens the AnalysisWorkspace Custom Page:

```javascript
// Lines 165-169
const pageInput = {
    pageType: "custom",
    name: "sprk_analysisworkspace_8bc0b",
    recordId: dataPayload  // Pass data via recordId (dialog workaround)
};
```

This is a **live runtime call** — if the canvas app is deleted, this ribbon command breaks. Since the canvas app hosts the PCF, deleting the PCF (which forces the canvas app's redeploy) has the same effect.

### Why the inventory missed this

The 2026-06-22 inventory ([pcf-deployment-inventory-2026-06-22.md §3 row 2](../../ai-procedure-quality-r1/notes/inventory/pcf-deployment-inventory-2026-06-22.md)) classified AnalysisWorkspace PCF as "Superseded by AnalysisWorkspace Code Page (`src/client/code-pages/AnalysisWorkspace/`)" based on the Code Page's existence. The assumption was that the migration to the Code Page had retired the canvas-page path. **The migration is incomplete** — `sprk_analysis_commands.js` still has the legacy navigation function and may still be wired to a ribbon button.

Companion file [`src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js`](../../../src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js) documents at line 578-580:

```
The existing Spaarke_OpenAnalysisWorkspace() function in sprk_analysis_commands.js
navigates to a Custom Page (pageType: "custom", name: "sprk_analysisworkspace_8bc0b").

With the Form OnLoad approach, the subgrid ribbon command for "Open Workspace"
(Spaarke.Analysis.OpenWorkspace.Command) is no longer needed — users click the...
```

The launcher's design comment implies the form-onload approach **superseded** the ribbon command — but the ribbon JS still exists and is still deployed (it's under `src/client/webresources/` which deploys as a `sprk_*.js` webresource). Whether the ribbon button that calls `Spaarke_OpenAnalysisWorkspace()` is still wired is the open question.

### Recommended resolution (before Task 003 acts on this row)

Three steps to verify before deciding:

1. **Maker portal check** — Open Spaarke model-driven app → find any subgrid that historically had "Open Workspace" ribbon button → inspect ribbon command. If `Spaarke_OpenAnalysisWorkspace` is the action, the path is live.
2. **Web resource staleness** — Check if `sprk_analysis_commands` web resource is up-to-date with the source file (or if a newer version has been deployed that removed `Spaarke_OpenAnalysisWorkspace`).
3. **Form-onload completeness** — Confirm `sprk_AnalysisWorkspaceLauncher.js`'s form-onload approach is wired to ALL forms that previously used the ribbon path. If yes, the ribbon function is dead code and the path is safe to break.

### Three possible outcomes from the resolution

| Outcome | Action |
|---|---|
| Ribbon button is still wired + active | **Do not delete** AnalysisWorkspace PCF or canvas app. File as a "blocked — needs migration completion" item. Migration to form-onload must finish first. |
| Ribbon button is unwired OR `sprk_analysis_commands.js` is updated/retired | Reclassify as `ready-to-delete`; resume per procedure §3.2 sequence. Update this deviation to `RESOLVED`. |
| Ambiguous / contested | Owner decision required. Default to "do not delete" until resolved. |

### Scope impact

- 10 of 11 customcontrols remain `ready-to-delete` for Task 003.
- 4 of 6 canvas apps remain `ready-to-delete`.
- AnalysisWorkspace PCF + canvas app held back from Task 003 scope pending resolution.
- AnalysisWorkspace's dedicated solution `AnalysisWorkspaceSolution` IS still backed up (in case future cleanup needs it).

### Status

`RESOLVED — 2026-06-22 — outcome 1 (PERMANENT HOLD)`. **Re-checked 2026-10-14: the hold reason no longer applies. See [DEV-005](#dev-005--dev-001-analysisworkspace-hold-reason-no-longer-applies-hold-reported). Still held pending owner confirmation.**

### Owner decision (2026-06-22)

**"Leave the command bar ribbon button (even though broken) because we may rewire it to a new analysis code page wizard modal."**

Decision implications:

- **`sprk_Spaarke.Controls.AnalysisWorkspace` PCF (`286d7717-…`) — DO NOT DELETE.** Stays deployed in spaarkedev1 + spaarkedev2 indefinitely.
- **`sprk_analysisworkspace_8bc0b` canvas page (`ff384002-…`) — DO NOT DELETE.** Stays deployed indefinitely. The PCF lives inside it; both must remain together.
- **`Spaarke_OpenAnalysisWorkspace()` ribbon function — LEAVE IN PLACE.** May be broken in steady-state today (form-onload migration incomplete) but is preserved for a planned future rewire to a new analysis Code Page wizard modal.
- **`AnalysisWorkspaceSolution` baseline backup — KEEP** in `backups-2026-06-22/` for future reference / rewire-planning context.

### Future-work flag (for the planned rewire project)

When the rewire happens:
- The new code-page wizard modal should be wired via `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_...' })` (NOT `pageType: 'custom'`) — mirroring the DocumentUploadWizard pattern that drove UQC's retirement (sprk_subgrid_commands.js v4.0.0).
- After the rewire ships, `sprk_analysisworkspace_8bc0b` canvas page + AnalysisWorkspace PCF + `Spaarke_OpenAnalysisWorkspace()` ribbon function become genuinely orphan and can be retired in a follow-on cleanup pass.
- The original 11-control / 6-canvas Task 003 scope can be revisited at that point.

---

## (No further deviations as of 2026-06-22)

---

# 2026-10-14 re-check (Task 003)

> The owner directed that the June 4-check be redone against the live environment and current master before any deletion. Method: `RetrieveDependenciesForDelete` per component; `solutioncomponent` membership; a live content grep of every `systemform.formxml` (including dashboards), `savedquery.layoutxml/layoutjson`, `sitemap.sitemapxml` and every `sprk_*` JS/HTML web resource body (82 scanned); and `git grep` of master (`src/`, `infrastructure/`, `scripts/`, `.github/`, `tests/`) at `origin/master` 2e4772606.
> Must-fire controls: the form grep returns the two `sprk_event` forms for `Controls.EventFormController`; the view grep returns six `sprk_event` views for `Controls.RegardingLink`. Both are known bindings, so the grep finds real references.

## DEV-002 — SpeDocumentViewer is live again (HOLD)

- Source was deleted by PR #412 (2026-06-23) and **restored to master** by #1453 (2598b2fb95, 2026-10-08, B2B guest sign-in fix). Further master commits on 2026-10-08/09 changed it.
- Live: v1.0.29, modified 2026-10-10. `RetrieveDependenciesForDelete` returns systemform `9088d6a4-4cf2-f011-8406-7c1e520aa4df` (the sprk_Document main form). Master FormXml for that form binds `sprk_Spaarke.SpeDocumentViewer` for all three form factors.
- Member of SpaarkeMaster (root component in master `Solution.xml:483`), SpaarkeCore, SpaarkeMasterTest2, SpaarkeSpeDocumentViewer, PowerAppsToolsTemp_sprk.
- The June "confirmed unused" premise is wrong today. **Do not delete.** Under NFR-04, a parallel reintroduction takes precedence, so FR-01's SDV source deletion is also void. FR-02 row 2 is removed from scope.

## DEV-003 — EventAutoAssociate, EventFormController, RegardingLink are bound in SpaarkeMaster (HOLD)

| Control | Live dependency | Master reference |
|---|---|---|
| EventAutoAssociate | systemform `642e1a65-4201-f111-8407-7ced8d1dc988` (sprk_event quick create) | SpaarkeMaster root component `Solution.xml:474`; quickCreate FormXml binds it |
| EventFormController | systemforms `90d2eff7-…` + `835b8ee8-…` (sprk_event "Event modal form", "Event Assign Work main form") | `Solution.xml:475`; both main FormXml files bind it |
| RegardingLink | 6 sprk_event views (My Events Open, All Tasks, All Tasks Open, Matter All Tasks Open 7 Days, My Tasks Open, All Tasks Open 7 Days). `RetrieveDependenciesForDelete` does **not** report these. Only the content grep finds them | `Solution.xml:476`; 6 SavedQueries bind it |

SpaarkeMaster 1.2.x was re-authored and exported from dev on 2026-10-08 (65eb1cb2b2) with these controls as root components. Deleting them would break live forms and views, and the next SpaarkeMaster import would bring them back. **Do not delete.** They are not orphans.

## DEV-004 — FieldMappingAdmin is deliberately retained on master (HOLD)

- No live dependency, but master carries `infrastructure/dataverse/solutions/FieldMappingAdminSolution/` (added 2026-07-02, bcb4db16ab, set-regarding-and-field-mapping-resolver-r1) with the control's bundle and a deploy script. Its README says: "PCF Control: `Spaarke.Controls.FieldMappingAdmin` (unrelated, pre-existing; retained to avoid regression)".
- A redeploy of that solution would recreate the control. **Do not delete** until that project's owner agrees and the mirror drops the control.

## DEV-005 — DEV-001 (AnalysisWorkspace) hold reason no longer applies (HOLD, reported)

- The DEV-001 ribbon function `Spaarke_OpenAnalysisWorkspace()` (`src/client/webresources/js/sprk_analysis_commands.js:152`) **has already been rewired**. It now navigates `pageType: "webresource"`, `webresourceName: "sprk_spaarkeai"`. No master file references `sprk_analysisworkspace_8bc0b`.
- The hosting canvas page `sprk_analysisworkspace_8bc0b` (`ff384002-…`) **no longer exists** in spaarkedev1 (GET returns 404). Someone outside this project removed it between 2026-06-22 and 2026-10-14.
- The PCF `286d7717-…` has no live dependency and no master reference. Members: AnalysisWorkspaceSolution, PowerAppsToolsTemp_sprk, Default.
- `AnalysisWorkspaceSolution` **cannot be exported** ("No WebResource for CustomControlResourceId {f3757ca8-4924-4d66-9c91-84723b03d7f3}"), so the PCF record is already broken in dev.
- Corroborating note: `projects/spaarkeai-word-add-in-r1/notes/124-spedocumentviewer-and-orphans.md` row "AnalysisWorkspace" says "the owner overrode DEV-001 on 2026-08-20 ('only launch is SpaarkeAI now')" and recommends removal (#1468). That is another session's record, not an instruction to this task.
- The PCF is now a genuine orphan. DEV-001 was an owner decision, so **it is not deleted in this session.** Owner decision needed: reclassify as ready-to-delete for a follow-up pass (and for task 006)?

## DEV-006 — components already absent before this session

| Component | June GUID | State 2026-10-14 |
|---|---|---|
| `sprk_Spaarke.Controls.UniversalDocumentUpload` (UQC) | `456e739d-…` | absent. The `UniversalQuickCreate` solution is absent too |
| `sprk_documentuploaddialog_e52db` (UQC canvas) | `c1611b0d-…` | absent |
| `sprk_analysisworkspace_8bc0b` | `ff384002-…` | absent (see DEV-005) |

These were removed outside this project. No audit records exist for canvas-app deletes. Consequence: the POML's UQC-based resurrection drill (step 4) cannot run. It runs on EventCalendarFilter instead. Stale leftover: web resource `sprk_OpenDocumentQuickCreate` (master `src/client/webresources/js/sprk_OpenDocumentQuickCreate.js:11` and the SpaarkeMaster mirror) still navigates to the deleted `sprk_documentuploaddialog_e52db`. No ribbon, form or other component depends on it (`RetrieveDependenciesForDelete` is empty, and no master ribbon XML calls it), so no live path breaks. It is dead code, filed as an issue.

## DEV-007 — the SC-02 count target (13) is stale

The June figure was 24 Spaarke customcontrols, and 24 − 11 = 13. On 2026-10-14 there are **31** (`startswith(name,'sprk_Spaarke') or startswith(name,'sprk_Sprk')`, componentstate 0). The extra controls are new Communication*, AssociationResolver, RecordHeader and similar. With 4 deletions the expected post-session count is **27**. SC-02 should be read as "31 − deleted".
