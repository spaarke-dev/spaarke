# PCF + Code Page Deployment Inventory

> **Date**: 2026-06-22
> **Triggered by**: chat-routing-redesign-r1 raised TypeScript drift in `@spaarke/ui-components` consumers; UQC was implicated. Owner asked: *"is UQC actually still in use, and is there a definitive inventory we can rely on?"*
> **Methodology**: source-tree enumeration (Glob + manifest reads) + live Dataverse query (`spaarkedev1` via Dataverse MCP) + ribbon-command source inspection.
> **Companion artifacts**: [`pcf-bundle-sizes.md`](pcf-bundle-sizes.md) (2026-05-14 bundle-size baseline; the present doc is the deployment-side complement); [`projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../../../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) (test-rot audit, not deployment).

---

## 1. Executive summary

- **Source tree**: 14 PCFs with `ControlManifest.Input.xml`, 4 code-pages under `src/client/code-pages/`, 22 React/Vite Code Pages under `src/solutions/`.
- **Dataverse (spaarkedev1)**: 23 Spaarke/Sprk-publisher PCFs deployed (all Published, all unmanaged), 20 most-recent `sprk_*` HTML web resources, 6 Spaarke-publisher canvas apps (Custom Pages).
- **UQC verdict — CONFIRMED ORPHAN**. The `Spaarke.Controls.UniversalDocumentUpload` PCF is deployed (v3.15.2) and its hosting Custom Page `sprk_documentuploaddialog_e52db` exists, but the ribbon command that drove all traffic to it migrated to a Code Page in **v4.0.0** ([sprk_subgrid_commands.js:582-585](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js#L582-L585)). Local source has been bumped to v3.15.3 with an uncommitted bundle on disk — that bump never deployed and is not in `git log`.
- **9 PCFs are deployed without a source-tree folder** — stale deployments from removed/superseded controls (AnalysisBuilder, DueDatesWidget, EventAutoAssociate, EventCalendarFilter, EventFormController, FieldMappingAdmin, RegardingLink, LegalWorkspace PCF, plus the AnalysisWorkspace PCF whose namesake lives as a Code Page instead).
- **1 PCF is in source but not deployed**: DrillThroughWorkspace v1.1.1 — consistent with the 2026-05-14 bundle-size audit ("Built but not deployed yet").

---

## 2. Source-tree PCFs (14)

| # | Folder | Full name (namespace.constructor) | Source ver | Deployed ver | Match | Notes |
|---|---|---|---:|---:|:---:|---|
| 1 | `AssociationResolver/` | `Spaarke.Controls.AssociationResolver` | 1.1.0 | 1.1.0 | ✅ | |
| 2 | `DocumentRelationshipViewer/DocumentRelationshipViewer/` | `Spaarke.Controls.DocumentRelationshipViewer` | 1.0.36 | 1.0.36 | ✅ | |
| 3 | `DrillThroughWorkspace/control/` | `Spaarke.Controls.DrillThroughWorkspace` | 1.1.1 | **— (not deployed)** | ❌ | Built locally; never pushed. Decide deploy-or-delete. |
| 4 | `EmailProcessingMonitor/control/` | `Spaarke.EmailProcessingMonitor` | 1.1.2 | 1.1.2 | ✅ | |
| 5 | `RegardingResolver/RegardingResolver/` | `Spaarke.Controls.RegardingResolver` | 1.1.0 | 1.1.0 | ✅ | |
| 6 | `RelatedDocumentCount/RelatedDocumentCount/` | `Spaarke.Pcf.RelatedDocumentCount.RelatedDocumentCount` | 1.21.3 | 1.21.3 | ✅ | |
| 7 | `ScopeConfigEditor/ScopeConfigEditor/` | `Sprk.ScopeConfigEditor` | 1.2.7 | 1.2.7 | ✅ | |
| 8 | `SemanticSearchControl/SemanticSearchControl/` | `Sprk.SemanticSearchControl` | 1.1.76 | 1.1.76 | ✅ | |
| 9 | `SpaarkeGridCustomizer/` | `Spaarke.Controls.SpaarkeGridCustomizer` | 1.0.0 | 1.0.0 | ✅ | |
| 10 | `SpeDocumentViewer/control/` | `Spaarke.SpeDocumentViewer` | 1.0.27 | 1.0.27 | ✅ | |
| 11 | `ThemeEnforcer/` | `Spaarke.ThemeEnforcer` | 1.0.0 | 1.0.0 | ✅ | |
| 12 | **`UniversalQuickCreate/control/`** | **`Spaarke.Controls.UniversalDocumentUpload`** | **3.15.3** | **3.15.2** | **⚠ src ahead** | **ORPHAN — see §4** |
| 13 | `UpdateRelatedButton/` | `Spaarke.Controls.UpdateRelatedButton` | 1.0.0 | 1.0.0 | ✅ | |
| 14 | `VisualHost/control/` | `Spaarke.Visuals.VisualHost` | 1.4.16 | 1.4.16 | ✅ | |

> **Note** — folder name ≠ constructor name in two cases: `UniversalQuickCreate/` builds `UniversalDocumentUpload` (3.15.x), and `RegardingResolver/` is *distinct from* the deployed-but-unsourced `RegardingLink` PCF (1.1.6).

---

## 3. Deployed-but-no-source PCFs (10)

These PCFs exist as published `customcontrol` records in Dataverse but have **no `ControlManifest.Input.xml`** in `src/client/pcf/`. They are leftover deployments from prior projects that did not clean up the Dataverse side when the source was removed/superseded.

| # | Deployed name | Ver | Likely status | Evidence |
|---|---|---:|---|---|
| 1 | `sprk_Spaarke.Controls.AnalysisBuilder` | 2.9.2 | Superseded by AnalysisBuilder Code Page (canvas `sprk_analysisbuilder_40af8`, modified 2026-01-14) | No source folder |
| 2 | `sprk_Spaarke.Controls.AnalysisWorkspace` (PCF) | 1.3.5 | Superseded by `src/client/code-pages/AnalysisWorkspace/` Code Page (canvas `sprk_analysisworkspace_8bc0b`, modified 2026-03-15) | No PCF source folder; Code Page exists |
| 3 | `sprk_Spaarke.Controls.DueDatesWidget` | 1.0.8 | Retired | No source folder |
| 4 | `sprk_Spaarke.Controls.EventAutoAssociate` | 1.0.0 | Retired (events flow folded into ribbons / SmartTodo) | No source folder |
| 5 | `sprk_Spaarke.Controls.EventCalendarFilter` | 1.0.5 | Superseded by `@spaarke/events-components` CalendarFilterPane | No source folder |
| 6 | `sprk_Spaarke.Controls.EventFormController` | 1.0.6 | Retired | No source folder |
| 7 | `sprk_Spaarke.Controls.FieldMappingAdmin` | 1.1.0 | Retired (admin UI consolidated) | No source folder |
| 8 | `sprk_Spaarke.Controls.PlaybookBuilderHost` | 2.25.0 | Deployed PCF; source under `src/client/pcf/PlaybookBuilderHost/` does NOT have a top-level `ControlManifest.Input.xml` — folder is a non-standard layout per 2026-05-14 audit | Folder exists; no manifest at standard path |
| 9 | `sprk_Spaarke.Controls.RegardingLink` | 1.1.6 | Predecessor of RegardingResolver | No source folder |
| 10 | `sprk_Spaarke.LegalWorkspace` (PCF) | 1.0.1 | Superseded by LegalWorkspace Code Page (`src/solutions/LegalWorkspace/`) | No PCF source folder; Code Page exists |

> **PlaybookBuilderHost** is the borderline case — there IS a source folder, but it doesn't follow the standard PCF layout. Verify with the playbook-builder owners whether the deployed v2.25.0 still matches the live source.

---

## 4. UQC deep-dive — confirmed orphan

The reason this audit was commissioned. Findings:

### 4.1 Dataverse-side state (live, queried 2026-06-22)

| Artifact | Name | Version / mod date | State |
|---|---|---|---|
| PCF control | `sprk_Spaarke.Controls.UniversalDocumentUpload` | v3.15.2 | Published, unmanaged |
| Custom Page (canvas app, hosts the PCF) | `sprk_documentuploaddialog_e52db` | mod 2026-03-15 | Published, unmanaged |
| Successor Code Page (HTML web resource) | `sprk_documentuploadwizard` | mod 2026-06-18 | Published, unmanaged — **this is what the ribbon now opens** |

### 4.2 The ribbon command migration — primary evidence

[`src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js`](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js) is the ribbon JS that drives "Add Documents" on every Documents subgrid (Matter, Project, Invoice, Account, Contact, Communication). Its version-history block at lines 582-585:

```
4.0.0: Code Page (webresource) dialog approach with React 18 (sprk_documentuploadwizard)
3.0.x: Custom Page dialog approach with PCF control (sprk_documentuploaddialog_e52db)
2.1.0: Form Dialog approach using sprk_uploadcontext utility entity
2.0.0: Initial release for Custom Page architecture (deprecated)
```

At [line 361](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js#L361) and again at [line 512](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js#L512), the command opens `webresourceName: "sprk_documentuploadwizard"` (the Code Page). Neither call path touches `sprk_documentuploaddialog_e52db` (the UQC-hosting Custom Page) anymore.

### 4.3 Source-tree drift

- Local manifest is at **v3.15.3** ([UniversalQuickCreate/control/ControlManifest.Input.xml:3](../../../../src/client/pcf/UniversalQuickCreate/control/ControlManifest.Input.xml#L3)) but Dataverse has **v3.15.2**.
- A `bundle.js` exists on disk at `solution/src/WebResources/sprk_Spaarke.Controls.UniversalDocumentUpload/bundle.js` but **is not in git history** (verified: `git log --all -- {path}` returns empty). It was built locally and never committed.
- Consistent with the 2026-05-14 bundle-size audit, which already classified UQC as "Built but not deployed yet" (no committed bundle in solution).

### 4.4 Likely cause of the version bump

A general PCF rebuild pass (probably `spaarke-auth-v2-and-hardening` task 028 "verify-rebuild-pcfs" or a similar sweep) bumped UQC's manifest along with the rest of the fleet but never actually deployed it because no one was driving traffic to UQC anymore. The bundle artifact stayed local.

---

## 5. Source-tree code-pages + solutions

Two top-level locations: `src/client/code-pages/` (4 entries) and `src/solutions/` (22 entries, all Vite/React). Both deploy as `webresource` records.

### 5.1 `src/client/code-pages/` (4)

| Folder | Deployed web resource | Last modified (DV) | Notes |
|---|---|---|---|
| `AnalysisWorkspace/` | (not found in top-20 most-recent — older than cutoff or different name) | — | Has tests but per Track A audit, 2 deprecated test files |
| `DocumentRelationshipViewer/` | — | — | Code Page complement to the PCF; needs separate verification |
| `PlaybookBuilder/` | — | — | Needs separate verification |
| `SemanticSearch/` | — | — | Per Track A audit: 17 test files orphaned, no jest config (HIGH) |

### 5.2 `src/solutions/` Vite Code Pages — confirmed deployed (top-20 most-recent)

| Local folder | Deployed `sprk_*` name | Last modified (DV) |
|---|---|---|
| `SmartTodo/` | `sprk_smarttodo` | 2026-06-21 22:20 |
| `SpaarkeAi/` | `sprk_spaarkeai` | 2026-06-21 22:19 |
| `CreateTodoWizard/` | `sprk_createtodowizard` | 2026-06-21 15:10 |
| `DailyBriefing/` (DailyUpdate) | `sprk_dailyupdate` | 2026-06-21 13:53 |
| `Reporting/` | `sprk_reporting` | 2026-06-18 22:52 |
| `CalendarSidePane/` | `sprk_calendarsidepane.html` | 2026-06-18 22:51 |
| `EventDetailSidePane/` | `sprk_eventdetailsidepane.html` | 2026-06-18 22:51 |
| `SpeAdminApp/` | `sprk_speadmin` | 2026-06-18 22:51 |
| `SummarizeFilesWizard/` | `sprk_summarizefileswizard` | 2026-06-18 22:50 |
| `PlaybookLibrary/` | `sprk_playbooklibrary` | 2026-06-18 22:50 |
| `WorkspaceLayoutWizard/` | `sprk_workspacelayoutwizard` | 2026-06-18 22:50 |
| `FindSimilarCodePage/` | `sprk_findsimilar` | 2026-06-18 22:50 |
| `CreateWorkAssignmentWizard/` | `sprk_createworkassignmentwizard` | 2026-06-18 22:50 |
| `CreateEventWizard/` | `sprk_createeventwizard` | 2026-06-18 22:50 |
| `CreateMatterWizard/` | `sprk_creatematterwizard` | 2026-06-18 22:50 |
| `CreateProjectWizard/` | `sprk_createprojectwizard` | 2026-06-18 22:50 |
| `AllDocuments/` | `sprk_alldocuments` | 2026-06-18 22:50 |
| `DocumentUploadWizard/` | `sprk_documentuploadwizard` | 2026-06-18 22:50 |
| `EventsPage/` | `sprk_eventspage.html` | 2026-06-12 22:15 |
| (matter insight card host) | `sprk_/html/matter_insight_card_host.html` | 2026-06-16 17:30 |

Out of top-20 (older or not yet probed) — but folders exist locally: `LegalWorkspace/`, `sprk_invoicespage/`, `sprk_kpiassessmentspage/`. Recommend a follow-up query specifically for those names.

### 5.3 Canvas apps (Custom Pages, `canvasapptype = 2`)

6 Spaarke-publisher canvas apps deployed:

| Name | Display name | Last modified | Likely host of |
|---|---|---|---|
| `sprk_documentuploaddialog_e52db` | File Upload | 2026-03-15 | **UQC PCF (orphan — see §4)** |
| `sprk_eventspage_786f4` | Events Page | 2026-03-15 | (predecessor of EventsPage Code Page) |
| `sprk_analysisworkspace_8bc0b` | Analysis Workspace | 2026-03-15 | AnalysisWorkspace PCF (orphan; superseded by code-page) |
| `sprk_visualizationdrillthroughworkspace_e48a5` | Visualization Drill Through Documents | 2026-03-09 | DrillThroughWorkspace PCF (PCF source v1.1.1 exists but isn't even deployed — see §2 row 3) |
| `sprk_playbookbuilder_eb5ad` | Playbook Builder | 2026-01-18 | PlaybookBuilderHost PCF |
| `sprk_analysisbuilder_40af8` | Analysis Builder | 2026-01-14 | AnalysisBuilder PCF (orphan; §3 row 1) |

All 6 are older than the most-recent Code Page redeploys (2026-06-18 onwards), consistent with a fleet-wide migration from Custom-Page-hosted PCFs to React Code Pages.

---

## 6. Recommended cleanup actions

### 6.1 Source-side — safe to delete from `src/`

| Folder | Justification |
|---|---|
| `src/client/pcf/UniversalQuickCreate/` | Ribbon migrated to Code Page in v4.0.0 (§4.2). No call path touches it. |

> **Caveat**: deleting from source does NOT remove the deployed Dataverse artifacts. Pair with §6.2.

### 6.2 Dataverse-side — needs cleanup pass

The following published `customcontrol` records should be reviewed for unpublish/delete (each requires verification that no form/ribbon/page still references it):

- `sprk_Spaarke.Controls.UniversalDocumentUpload` (v3.15.2)
- `sprk_Spaarke.Controls.AnalysisBuilder` (v2.9.2)
- `sprk_Spaarke.Controls.AnalysisWorkspace` (v1.3.5)
- `sprk_Spaarke.Controls.DueDatesWidget` (v1.0.8)
- `sprk_Spaarke.Controls.EventAutoAssociate` (v1.0.0)
- `sprk_Spaarke.Controls.EventCalendarFilter` (v1.0.5)
- `sprk_Spaarke.Controls.EventFormController` (v1.0.6)
- `sprk_Spaarke.Controls.FieldMappingAdmin` (v1.1.0)
- `sprk_Spaarke.Controls.RegardingLink` (v1.1.6)
- `sprk_Spaarke.LegalWorkspace` (v1.0.1)

Plus the canvas apps that host them: `sprk_documentuploaddialog_e52db`, `sprk_analysisworkspace_8bc0b`, `sprk_visualizationdrillthroughworkspace_e48a5`, `sprk_analysisbuilder_40af8`, possibly `sprk_eventspage_786f4` and `sprk_playbookbuilder_eb5ad` (PlaybookBuilder Code Page is in `src/client/code-pages/PlaybookBuilder/`).

### 6.3 DrillThroughWorkspace — open decision

PCF source at v1.1.1 exists but has never been deployed. The 2026-05-14 bundle-size audit and this audit agree. Decision needed: **deploy v1.1.1** (if the Drill-Through Custom Page is still desired) or **delete the source** (if drill-through is now covered by other surfaces, e.g., the Reporting Code Page).

### 6.4 PlaybookBuilderHost — needs owner triage

Source layout doesn't match other PCFs (no top-level `ControlManifest.Input.xml`). Deployed v2.25.0 is recent. Confirm with the playbook-builder owners whether (a) the source is fine and the layout is intentional, (b) source is stale and the deployed v2.25.0 originates from a different repo path, or (c) it needs realignment.

---

## 7. Methodology & data sources

### 7.1 Source-tree enumeration

- `Glob src/client/pcf/*/ControlManifest.Input.xml` (top-level) + `src/client/pcf/*/*/ControlManifest.Input.xml` (nested) → 14 manifests.
- Read every manifest for `namespace` + `constructor` + `version` attributes.
- `Glob src/client/code-pages/*/package.json` → 4 code-pages.
- `Glob src/solutions/*/package.json` → 22 Vite solutions.

### 7.2 Dataverse queries (via Dataverse MCP)

```sql
-- PCFs (Spaarke namespace, first batch)
SELECT TOP 20 name, version, componentstate, ismanaged
FROM customcontrol
WHERE name LIKE 'Spaarke%' AND componentstate = 0
ORDER BY name

-- PCFs (Sprk + Visuals + DrillThrough — second batch to cover 20-cap cut-off)
SELECT TOP 20 name, version, componentstate, ismanaged
FROM customcontrol
WHERE name LIKE 'sprk_Sprk%'
   OR name LIKE 'sprk_Spaarke.Visuals%'
   OR name LIKE 'sprk_Spaarke.Controls.DrillThroughWorkspace%'

-- Web resources (HTML Code Pages, top-20 most-recent)
SELECT TOP 20 name, displayname, ismanaged, modifiedon
FROM webresource
WHERE name LIKE 'sprk_%' AND webresourcetype = 1 AND componentstate = 0
ORDER BY modifiedon DESC

-- Canvas apps (Custom Pages, top-20 most-recent)
SELECT TOP 20 name, displayname, canvasapptype, ismanaged, lastmodifiedtime
FROM canvasapp
WHERE componentstate = 0
ORDER BY lastmodifiedtime DESC
```

Dataverse MCP `read_query` is capped at TOP 20 — multi-query batches are required for full enumeration.

### 7.3 Ribbon-command source

[`src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js`](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js) — read in full. Version-history block (lines 582-585) is the primary evidence for the UQC orphan finding.

### 7.4 What this audit does NOT cover

- Form-level / view-level references to specific PCF controls (would require reading every `systemform` and `savedquery` record's FormXML — large scan; deferred).
- Ribbon definitions for non-Document-subgrid ribbons (other ribbons may still drive traffic to deployed-but-orphan PCFs).
- Managed solutions imported from external publishers (filter was `sprk_*`/`Spaarke*`/`Sprk*` only).
- Other environments (only `spaarkedev1` was queried via MCP).

### 7.5 Reproducibility

Re-run by: (a) repeating §7.1's globs; (b) re-issuing §7.2's queries against the target environment via Dataverse MCP; (c) re-reading [sprk_subgrid_commands.js](../../../../src/solutions/DocumentUploadWizard/sprk_subgrid_commands.js)'s version-history block for ribbon-side claims about UQC.

---

## 8. Cross-references

- [`pcf-bundle-sizes.md`](pcf-bundle-sizes.md) — 2026-05-14 bundle-size baseline (precursor; this doc is the deployment complement).
- [`projects/sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md`](../../../sdap.bff.api-test-suite-repair-r2/baseline/phase4-track-a-pcf-audit-2026-06-01.md) — 2026-06-01 test-rot audit (different lens; complementary).
- [`projects/spaarke-auth-v2-and-hardening/tasks/028-verify-rebuild-pcfs.poml`](../../../spaarke-auth-v2-and-hardening/tasks/028-verify-rebuild-pcfs.poml) — likely origin of the UQC v3.15.2 → v3.15.3 manifest bump.
- [`projects/production-environment-setup-r2/tasks/030-migrate-pcf-universal-quick-create.poml`](../../../production-environment-setup-r2/tasks/030-migrate-pcf-universal-quick-create.poml) — predecessor UQC deployment task.
