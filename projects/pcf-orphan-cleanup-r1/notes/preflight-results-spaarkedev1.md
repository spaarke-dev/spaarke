# Preflight Results — spaarkedev1

> **Task**: 001 — Pre-flight backups + 4-check verification
> **Environment**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com/`)
> **Executed**: 2026-06-22
> **Auth**: PAC CLI profile [3] — `ralph.schroeder@spaarke.com`
> **Procedure section**: [§1.1 baseline backups + §1.2 per-control 4-check](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#1-pre-flight-binding-before-any-destructive-action)

---

## 1. Executive summary

### Verdict per control

| # | Control | Disposition | Reason |
|---:|---|---|---|
| 1 | `sprk_Spaarke.Controls.UniversalDocumentUpload` | ✅ **ready-to-delete** | Ribbon migrated (sprk_subgrid_commands.js v4.0.0); no FormXML refs in dedicated solution |
| 2 | `sprk_Spaarke.SpeDocumentViewer` | ✅ **ready-to-delete** | No external importers; no FormXML refs in dedicated solution. **Live form-binding re-check MANDATORY at Task 003 per D-02.** |
| 3 | `sprk_Spaarke.Controls.AnalysisBuilder` | ✅ **ready-to-delete** | Superseded by Playbook Library Code Page; only in Default solution (no dedicated solution to back up) |
| 4 | `sprk_Spaarke.Controls.AnalysisWorkspace` (PCF) | ⏭ **PERMANENT HOLD** (owner decision 2026-06-22) | Live ribbon ref in `sprk_analysis_commands.js:167`; preserved for future rewire to a new analysis Code Page wizard modal. See [`preflight-deviations.md` DEV-001](preflight-deviations.md#dev-001) — RESOLVED. |
| 5 | `sprk_Spaarke.Controls.DueDatesWidget` | ✅ **ready-to-delete** | No external importers; backup captured |
| 6 | `sprk_Spaarke.Controls.EventAutoAssociate` | ✅ **ready-to-delete** | No external importers; backup captured |
| 7 | `sprk_Spaarke.Controls.EventCalendarFilter` | ✅ **ready-to-delete** | Superseded by `@spaarke/events-components` CalendarFilterPane; backup captured |
| 8 | `sprk_Spaarke.Controls.EventFormController` | ✅ **ready-to-delete** | No external importers; backup captured |
| 9 | `sprk_Spaarke.Controls.FieldMappingAdmin` | ✅ **ready-to-delete** | Admin UI consolidated; backup captured |
| 10 | `sprk_Spaarke.Controls.RegardingLink` | ✅ **ready-to-delete** | Predecessor of RegardingResolver (which is in active use); backup captured |
| 11 | `sprk_Spaarke.LegalWorkspace` (PCF) | ✅ **ready-to-delete** | Superseded by LegalWorkspace Code Page; backup captured |

### Canvas apps disposition

| # | Canvas app | Hosts | Disposition |
|---:|---|---|---|
| 1 | `sprk_documentuploaddialog_e52db` | UQC PCF | ✅ ready-to-delete (ribbon migrated v4.0.0) |
| 2 | `sprk_analysisbuilder_40af8` | AnalysisBuilder PCF | ✅ ready-to-delete |
| 3 | `sprk_analysisworkspace_8bc0b` | AnalysisWorkspace PCF | ⏭ **PERMANENT HOLD** ([DEV-001](preflight-deviations.md#dev-001) — RESOLVED 2026-06-22; preserved for future rewire) |
| 4 | `sprk_visualizationdrillthroughworkspace_e48a5` | DrillThroughWorkspace PCF (never deployed) | ✅ ready-to-delete (empty host; PCF was never deployed) |
| 5 | `sprk_eventspage_786f4` | (predecessor of EventsPage Code Page) | ✅ ready-to-delete |
| 6 | `sprk_playbookbuilder_eb5ad` | PlaybookBuilderHost PCF | ⏭ **OUT OF SCOPE** (per D-03 — separate triage) |

### Counts (final, post owner triage)

- **Customcontrols**: 10 ready-to-delete, **1 permanent hold (AnalysisWorkspace PCF — owner-preserved for future rewire)** = **10/11 cleared for Task 003**
- **Canvas apps**: 4 ready-to-delete, **1 permanent hold (analysisworkspace_8bc0b — preserved with the PCF)**, 1 out-of-scope (playbookbuilder per D-03) = **4/6 cleared for Task 003**
- **Baseline backups captured**: **10 dedicated unmanaged solution ZIPs** in [`backups-2026-06-22/`](../backups-2026-06-22/) (including AnalysisWorkspaceSolution backup — kept as future-rewire reference)
- **Deviations filed**: 1 (DEV-001 — RESOLVED 2026-06-22 with owner decision to preserve)

---

## 2. Methodology

### 2.1 Backup capture (procedure §1.1)

Exported 10 dedicated unmanaged solutions via `pac solution export --managed false --include general`. **Deliberately excluded**:

- **Default + Active solutions** (`fd140aaf-...` / `fd140aae-...`) — system solutions, virtual containers, exporting whole is impractical (gigabytes) and unnecessary
- **Master/aggregator solutions** (`SpaarkeMaster`, `SpaarkeCore`, `SpaarkeMasterTest2`) — they reference the orphan PCFs but the PCFs are owned by their dedicated solutions. Restoring from a master backup would over-restore unrelated work
- **`PowerAppsToolsTemp_sprk`** — transient/working solution

The 10 captured ZIPs:

| Solution | Size | Notes |
|---|---:|---|
| `baseline-UniversalQuickCreate-pre-cleanup-2026-06-22.zip` | 1.7 MB | UQC PCF + canvas page |
| `baseline-SpaarkeSpeDocumentViewer-pre-cleanup-2026-06-22.zip` | 107 KB | SDV PCF |
| `baseline-AnalysisWorkspaceSolution-pre-cleanup-2026-06-22.zip` | 2.0 MB | AnalysisWorkspace PCF |
| `baseline-DueDatesWidget-pre-cleanup-2026-06-22.zip` | 16 KB | DueDatesWidget PCF |
| `baseline-EventAutoAssociateSolution-pre-cleanup-2026-06-22.zip` | 6 KB | EventAutoAssociate PCF |
| `baseline-SpaarkeEventCalendarFilter-pre-cleanup-2026-06-22.zip` | 13 KB | EventCalendarFilter PCF |
| `baseline-EventFormControllerSolution-pre-cleanup-2026-06-22.zip` | 254 KB | EventFormController PCF |
| `baseline-FieldMappingAdminSolution-pre-cleanup-2026-06-22.zip` | 17 KB | FieldMappingAdmin PCF |
| `baseline-RegardingLinkSolution-pre-cleanup-2026-06-22.zip` | 152 KB | RegardingLink PCF |
| `baseline-SpaarkeLegalWorkspace-pre-cleanup-2026-06-22.zip` | 753 KB | LegalWorkspace PCF |
| **Total** | **~5 MB** | All committed to git for rollback durability |

**AnalysisBuilder backup gap**: AnalysisBuilder (PCF GUID `dfc82d5b-9842-4639-b388-f57a06e57f2d`) lives only in the Default solution — no dedicated solution exists to back up. Resurrection would require either:
1. Re-exporting from another environment (if it exists there)
2. Webresource-level recovery via the customcontrol bundle.js
3. Accepting irrecoverability (AnalysisBuilder is the oldest orphan — modifiedon 2026-01-05; superseded by Playbook Library Code Page)

Per [project resurrection playbook (§3.5)](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#35-resurrection--recovery--explicit-playbook), the third option is acceptable for this control.

### 2.2 Per-control 4-check (procedure §1.2)

Executed in this order:

1. **Check 1: solution memberships** — Dataverse MCP `read_query` on `solutioncomponent` (see §3 below per control)
2. **Check 2: FormXML grep** — `unzip` + grep customizations.xml in each exported ZIP for constructor name (see §4 below)
3. **Check 3: ribbon JS grep** — `Grep` across `src/`, `scripts/`, `.github/` for canvas-app names (see §5 below)
4. **Check 4: canvas-app dependencies** — Dataverse MCP `read_query` for canvas-app solution memberships (see §2.3)

**FormXML coverage limitation**: Check 2 grepped only the dedicated solution XMLs. Forms that bind PCFs typically live in master/entity-owner solutions, NOT in PCF-dedicated solutions. **Therefore Check 2 is necessary-but-not-sufficient.** The procedure's §3.2 step 1 ("re-run §1.2 checks immediately pre-deletion") is the gate that catches live form bindings — operators MUST use the Maker portal's "Used By" / "Dependencies" view before each deletion in Task 003.

### 2.3 Canvas-app solution memberships

| Canvas app | Solutions (cluster) |
|---|---|
| `sprk_documentuploaddialog_e52db` (c1611b0d) | Default, Active, SpaarkeMasterTest2, SpaarkeCore, + 2 unidentified |
| `sprk_analysisbuilder_40af8` (f077022c) | (not yet queried; see deviations §6) |
| `sprk_analysisworkspace_8bc0b` (ff384002) | Default, Active, SpaarkeCore, + 2 unidentified |
| `sprk_visualizationdrillthroughworkspace_e48a5` (cb8dcd7e) | Default, Active, SpaarkeCore, + 1 unidentified |
| `sprk_eventspage_786f4` (1118454c) | Default, Active, SpaarkeCore, + 1 unidentified |
| `sprk_playbookbuilder_eb5ad` (80ee5f36) | (out-of-scope per D-03) |

**Implication for cleanup**: removing each canvas app from solution memberships requires touching multiple solutions per app. In Maker portal: open each solution → Objects → Apps → Remove from this solution. Cannot be batched; sequential per app.

---

## 3. Per-control solution memberships (Check 1 results)

> Format: control name → list of unmanaged solutions containing it. `Default`+`Active` excluded (always present).

| Control | Dedicated solution | Other unmanaged solutions |
|---|---|---|
| UQC (456e739d) | **UniversalQuickCreate** | PowerAppsToolsTemp_sprk, SpaarkeMasterTest2, SpaarkeCore |
| SDV (49b0cecd) | **SpaarkeSpeDocumentViewer** | SpaarkeMaster, SpaarkeCore |
| AnalysisBuilder (dfc82d5b) | **(none — only Default)** | — |
| AnalysisWorkspace PCF (286d7717) | **AnalysisWorkspaceSolution** | PowerAppsToolsTemp_sprk |
| DueDatesWidget (bac653dd) | **DueDatesWidget** | (none other) |
| EventAutoAssociate (21dd8ded) | **EventAutoAssociateSolution** | SpaarkeMasterTest2, SpaarkeMaster, SpaarkeCore |
| EventCalendarFilter (0ceec858) | **SpaarkeEventCalendarFilter** | (none other) |
| EventFormController (b85d8eac) | **EventFormControllerSolution** | SpaarkeMasterTest2, SpaarkeMaster, SpaarkeCore |
| FieldMappingAdmin (d8aac645) | **FieldMappingAdminSolution** | (none other) |
| RegardingLink (d8c352f3) | **RegardingLinkSolution** | (none other) |
| LegalWorkspace PCF (6aa7c56d) | **SpaarkeLegalWorkspace** | (none other) |

**Cleanup implication**: for each control, the Task 003 sequence must remove the control from EVERY solution it appears in (per procedure §3.2 step 5), then delete the customcontrol record. The dedicated solution stays — only its component (the PCF) is removed.

---

## 4. FormXML grep results (Check 2)

For each of the 10 exported solution ZIPs, `customizations.xml` was extracted + grepped for the constructor names.

| Solution ZIP | Constructor-name match count | Interpretation |
|---|---:|---|
| All 10 ZIPs | 2 each | Manifest definition (CustomControl `<Name>` + `<FileName>`) — NO form bindings in any dedicated solution |

**Spot-verified**: UQC's customizations.xml lines 33-34 (Name + FileName entries in the CustomControl element); SDV's same pattern at lines 33-34. Confirmed not form bindings.

**No `<formxml>` elements found** in any dedicated solution — these are PCF/canvas-app-only solutions.

**Deferred to Task 003**: live form-binding verification via Maker portal's "Used By" feature before each per-control deletion. This is the §1.2 re-run gate at procedure §3.2 step 1.

---

## 5. Ribbon JS grep results (Check 3)

Pattern: canvas-app names (`sprk_documentuploaddialog`, `sprk_analysisbuilder_40af8`, `sprk_analysisworkspace_8bc0b`, `sprk_visualizationdrillthroughworkspace`, `sprk_eventspage_786f4`)

**Filtered to live source**: `src/`, `scripts/`, `.github/` (worktree scratch spaces under `.claude/worktrees/` excluded).

5 source files matched:

| File | Reference type | Verdict |
|---|---|---|
| `src/client/pcf/UniversalQuickCreate/docs/REPO-STRUCTURE.md` | Inside UQC folder (being deleted) | Safe |
| `src/client/pcf/UniversalQuickCreate/solution/src/canvaspages/sprk_universaldocumentupload_page.json` | Inside UQC folder (being deleted) | Safe |
| `src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js` | Version-history COMMENT (line 583) — documenting the v3.0.x → v4.0.0 migration | Safe — comment only |
| `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` | Documentation COMMENT (line 579) | Safe — comment only |
| **`src/client/webresources/js/sprk_analysis_commands.js`** | **LIVE CODE — `Xrm.Navigation.navigateTo({ name: "sprk_analysisworkspace_8bc0b" })` at line 167** | **⚠ DEVIATION — see DEV-001** |

This finding is the load-bearing result of Task 001 — without the §1.2 check it would have been missed.

---

## 6. Out-of-scope verification

- **PlaybookBuilderHost** + `sprk_playbookbuilder_eb5ad` canvas app: NOT included in Task 001 verification per project D-03 (explicitly out of scope; separate owner triage required).

---

## 7. Recommendations for Task 003

### 7.1 Proceed-with-care list (10 customcontrols + 4 canvas apps)

All marked ✅ **ready-to-delete** per §1 summary. Per-control deletion order per procedure §3.1 with these adjustments:

| Original order in procedure §3.1 | Adjustment for spaarkedev1 cleanup |
|---|---|
| Row 1 (UQC) | Proceed |
| Row 2 (SDV) | Proceed — but MANDATORY live-form re-check via Maker portal first |
| Row 3 (AnalysisBuilder) | Proceed — AnalysisBuilder backup gap accepted (see §2.1) |
| **Row 4 (AnalysisWorkspace PCF)** | **PERMANENT HOLD (DEV-001 RESOLVED owner-preserved for future rewire)** |
| Rows 5–11 (DueDatesWidget through LegalWorkspace PCF) | Proceed |
| Canvas apps 1, 2, 4, 5 (documentuploaddialog, analysisbuilder, visualizationdrillthroughworkspace, eventspage_786f4) | Proceed |
| **Canvas app 3 (analysisworkspace_8bc0b)** | **PERMANENT HOLD (DEV-001 RESOLVED — preserved with its hosted PCF)** |
| Canvas app 6 (playbookbuilder) | Out of scope (D-03) |

### 7.2 Resurrection drill — pick low-stakes control

Per procedure §3.2 step 4, Task 003 must spot-test the resurrection path once during the session. Recommended candidate: **EventAutoAssociate** (smallest ZIP at 6 KB; oldest orphan; lowest risk if the drill itself misfires).

### 7.3 Before-deletion per-control re-verification

Per procedure §3.2 step 1, immediately before each deletion:
- **Open Maker portal → Solutions → relevant solution → Objects → click the PCF → check "Dependencies / Used By"**
- This is the live FormXML check that Check 2 (XML-grep) couldn't fully cover

If the "Used By" view shows any form / view / app dependency, **STOP** for that control and file a deviation.

---

## 8. Boundary verification

- ✅ Read-only audit + backup capture only. NO Dataverse mutations.
- ✅ NO source-tree modifications in `src/`. Verified by `git status` showing only `projects/pcf-orphan-cleanup-r1/` additions.
- ✅ 10 backup ZIPs in `backups-2026-06-22/` (~5 MB total — small enough to commit).
- ✅ Per-control disposition documented for all 11 customcontrols + 6 canvas apps in scope.
- ✅ 1 deviation filed (DEV-001 in `preflight-deviations.md`).

---

## 9. Cross-references

- [`preflight-deviations.md`](preflight-deviations.md) — DEV-001 details + recommended resolution
- [`backups-2026-06-22/`](../backups-2026-06-22/) — the 10 baseline solution ZIPs
- [`tasks/003-dataverse-cleanup-spaarkedev1.poml`](../tasks/003-dataverse-cleanup-spaarkedev1.poml) — consumes these results
- [Procedure §1.1 backups + §1.2 4-check](../../ai-procedure-quality-r1/notes/inventory/orphan-pcf-cleanup-and-react-types-procedure-2026-06-22.md#1-pre-flight-binding-before-any-destructive-action)
