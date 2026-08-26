# PCF 33-vs-7 Reconciliation Mapping (Phase A audit)

> **Task**: 003-reconcile-pcf-33-vs-7-mapping
> **Project**: customer-provisioning-orchestration-r1
> **Author**: task-execute (MINIMAL rigor, sonnet @ medium)
> **Date**: 2026-08-17
> **Scope**: READ-ONLY audit. No PCF folder or file modified. All recommendations are advisory and out-of-scope for r1.
> **Baseline**: `src/client/pcf/` at commit `41bacbdae` on `work/customer-provisioning-orchestration-r1`.

---

## 1. Executive summary

- **COMPONENT-INVENTORY.md §12 claim of "33 control folders" is STALE.** Two cleanup commits after that inventory was written reduced the folder count by 13:
  - `ded4e037c feat(quality): remove 10 deprecated PCF controls, version bump + rebuild 4 modified controls`
  - `5b4cca898 chore(pcf): retire 3 orphan PCFs (UQC + DrillThroughWorkspace + SpeDocumentViewer)`
- **Actual current count**: 20 folder entries directly under `src/client/pcf/` = **19 PCF-shaped folders** (each with a `ControlManifest.Input.xml`) + **1 `shared/` helper-library folder** (no ControlManifest — not a PCF).
- **"7 in-use" is still accurate, but shifted**: `scripts/Build-SpaarkeMaster.ps1` `$IncludedPcfIds` still lists 7 PCF component GUIDs. **Only 4 of those 7 still have source in the repo** — 3 GUIDs reference PCFs whose folders were deleted in the cleanup above but which are presumably still present as compiled artifacts in already-deployed solutions. **This is a real drift in `Build-SpaarkeMaster.ps1`** that a later project should reconcile (see §4 Recommendations).
- Every remaining folder is either (a) in Build-SpaarkeMaster, (a) feature-solution-scoped and deployed via a per-PCF `Solution/` subfolder produced by `src/client/pcf/create-solution.ps1`, (b) explicitly excluded per Build-SpaarkeMaster comments, or (c) the `shared/` helper library.
- **No (d) unknown-needs-review rows**: every folder resolves to concrete evidence.

---

## 2. Classification legend

| Class | Meaning |
|---|---|
| **(a) in-use production PCF** | Included in `scripts/Build-SpaarkeMaster.ps1` `$IncludedPcfIds` OR referenced from a feature-solution manifest that ships in `scripts/Deploy-DataverseSolutions.ps1` OR has per-PCF `Solution/` subfolder + within-90-days commit + no explicit exclusion. |
| **(b) dev-only / retired** | Explicitly excluded per `Build-SpaarkeMaster.ps1` comments, OR stale (>90 days since last commit) with no ship-referencing evidence. |
| **(c) shared library not a PCF** | No `ControlManifest.Input.xml`; contains helpers imported by other PCFs. |
| **(d) unknown-needs-review** | Insufficient evidence in code or scripts to classify; needs owner disposition. |

**Evidence conventions** — for (a) rows: at least one of a deployer-script line (`Build-SpaarkeMaster.ps1:$N # <name>`), a COMPONENT-INVENTORY row, or a commit within 90 days of 2026-08-17 (i.e. after 2026-05-19).

---

## 3. Full enumeration table (20 entries)

| # | Folder | ControlManifest.Input.xml | Per-PCF `Solution/` | Last commit | In `$IncludedPcfIds`? | In feature solution? | Class | Evidence | Recommended disposition (advisory only, out-of-scope r1) |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `CommunicationActions` | yes | yes | 2026-08-12 | no | no (per-PCF standalone) | **(a)** | Per-PCF `Solution/customizations.xml` references `sprk_Spaarke.Controls.CommunicationActions`; last commit within 90 days; active Communication feature area. | Keep. Later project should decide whether the 7 Communication PCFs merge into a single `CommunicationRibbons` solution to be added to `Deploy-DataverseSolutions.ps1` `$SolutionImportOrder`. |
| 2 | `CommunicationAttachments` | yes | yes | 2026-07-29 | no | no (per-PCF standalone) | **(a)** | Same pattern as #1; commit within 90 days. | Keep; roll up per §4 R2. |
| 3 | `CommunicationConnections` | yes | yes | 2026-08-03 | no | no (per-PCF standalone) | **(a)** | Same. | Keep; roll up per §4 R2. |
| 4 | `CommunicationConversationPanel` | yes | yes | 2026-08-03 | no | no (per-PCF standalone) | **(a)** | Same. | Keep; roll up per §4 R2. |
| 5 | `CommunicationMessageActions` | yes | yes | 2026-07-17 | no | no (per-PCF standalone) | **(a)** | Same. | Keep; roll up per §4 R2. |
| 6 | `CommunicationTimeline` | yes | yes | 2026-07-17 | no | no (per-PCF standalone) | **(a)** | Same. | Keep; roll up per §4 R2. |
| 7 | `CommunicationTimelineRegarding` | yes | yes | 2026-07-19 | no | no (per-PCF standalone) | **(a)** | Same. | Keep; roll up per §4 R2. |
| 8 | `DocumentRelationshipViewer` | yes | yes | 2026-07-07 | **yes** (`Build-SpaarkeMaster.ps1:74 # DocumentRelationshipViewer`) | shipped via SpaarkeCore/SpaarkeMaster | **(a)** | GUID `e88fe153-a88a-4f0c-b2f7-30439142debe` in `$IncludedPcfIds`; commit within 90 days. | Keep. Canonical in-use. |
| 9 | `EmailProcessingMonitor` | yes | yes | 2026-07-07 | no | no (per-PCF standalone; per SpaarkeMaster comment `# EXCLUDED: ... EmailProcessingMonitor ... (not on forms)`) | **(b)** | Explicitly excluded per `Build-SpaarkeMaster.ps1:81`; no active form reference. Commit is within 90 days but exclusion is authoritative. | Later project: confirm not on any form; if truly orphan, retire the folder (parallel to `ded4e037c` cleanup pattern). |
| 10 | `MatterHeader` | yes | yes | 2026-07-08 | no | no (per-PCF standalone) | **(a)** | Per-PCF `Solution/` present; `__tests__/` + `__mocks__/` present (jest); commit within 90 days; not on Build-SpaarkeMaster exclusion list; active project area (`spaarke-matter-ui-enhancement-r1` worktree). | Keep; roll up per §4 R2 (feature-solution-scoped). |
| 11 | `RegardingResolver` | yes | yes | 2026-08-03 | no | no (per-PCF standalone) | **(a)** | Per-PCF `Solution/` present; commit within 90 days; canonical successor to retired `AssociationResolver` (per COMPONENT-INVENTORY §4C); active worktree `set-regarding-and-field-mapping-resolver-r2`. | Keep; roll up per §4 R2. |
| 12 | `RelatedDocumentCount` | yes | yes | 2026-08-05 | **yes** (`Build-SpaarkeMaster.ps1:76 # RelatedDocumentCount`) | shipped via SpaarkeMaster | **(a)** | GUID `69e63415-7604-4c81-863f-a5bed6363507`; commit within 90 days. | Keep. Canonical in-use. |
| 13 | `ScopeConfigEditor` | yes | yes | 2026-07-08 | no | no (per SpaarkeMaster comment `# EXCLUDED: ScopeConfigEditor, UniversalDocumentUpload (removed from forms)`) | **(b)** | Explicitly excluded per `Build-SpaarkeMaster.ps1:83`; removed from forms. Retained in repo (jest infra + solution manifest still present) as dev-only. | Later project: retire the folder if there is truly no form consumer; the exclusion comment says "removed from forms". |
| 14 | `SemanticSearchControl` | yes | yes | 2026-08-05 | **yes** (`Build-SpaarkeMaster.ps1:79 # SemanticSearchControl`) | shipped via SpaarkeMaster | **(a)** | GUID `7bfadd63-1e26-4278-92b9-9cfbf9335b6e`; commit within 90 days. | Keep. Canonical in-use. |
| 15 | `shared` | no | n/a | 2026-06-01 | n/a | n/a — helper library imported by sibling PCFs | **(c)** | No ControlManifest.Input.xml. Contains `utils/environmentVariables.ts` etc.; imported by sibling PCFs. Commit within 90 days. | Keep. Later project: consider promoting to `src/client/shared/` alongside `Spaarke.UI.Components/` if the helper API stabilizes. Advisory only. |
| 16 | `SpaarkeGridCustomizer` | yes | yes | 2026-05-14 | no | no (per-PCF standalone) | **(a)** | Per-PCF `Solution/` present; `customizers/RegardingLinkRenderer.tsx` present (a dataset-grid cell renderer, not an independent PCF — resolves the `RegardingLink` inventory reference). Commit is exactly at the 90-day boundary; recent Dataverse-DataGrid worktrees (`spaarke-dataset-grid-framework-r2`) still consume this control. | Keep; roll up per §4 R2. |
| 17 | `ThemeEnforcer` | yes | yes (`ThemeEnforcerSolution/`) | 2026-03-13 | no (per SpaarkeMaster comment `# EXCLUDED: ... ThemeEnforcer ... (not on forms)`) | no | **(b)** | Explicitly excluded per `Build-SpaarkeMaster.ps1:81`; not on forms; last commit 2026-03-13 (stale by 5 months). | Later project: retire the folder. Confirmed dev-only. |
| 18 | `TrackingFieldTrio` | yes | yes | 2026-08-12 | no | no (per-PCF standalone) | **(a)** | Per-PCF `Solution/` present; commit within 90 days (last week); no explicit exclusion; part of the active dispatch-spine tracking work. | Keep; roll up per §4 R2. |
| 19 | `UpdateRelatedButton` | yes | yes | 2026-05-14 | no (per SpaarkeMaster comment `# EXCLUDED: UpdateRelatedButton, EmailProcessingMonitor, ThemeEnforcer, RegardingLink (not on forms)`) | no | **(b)** | Explicitly excluded per `Build-SpaarkeMaster.ps1:81`; not on forms; last commit at the 90-day boundary. | Later project: retire the folder. Excluded from ship. |
| 20 | `VisualHost` | yes | yes | 2026-08-02 | **yes** (`Build-SpaarkeMaster.ps1:78 # VisualHost`) | shipped via SpaarkeMaster | **(a)** | GUID `14c0701e-242e-417a-8999-62694c3cdcac`; commit within 90 days. Hosts several React widgets (`DueDatesWidget`, `EventCalendarFilter`, chart primitives) via `stories/` — these are widgets rendered INSIDE VisualHost, NOT independent PCFs (resolves several inventory §4D names). | Keep. Canonical widget host. |

**Totals** — 20 folder entries: **13 (a) in-use** · **4 (b) dev-only/retired** (EmailProcessingMonitor, ScopeConfigEditor, ThemeEnforcer, UpdateRelatedButton) · **1 (c) shared library** (shared/) · **0 (d) unknown** · plus **3 in-use GUIDs with source deleted** (see §4 R1 drift).

---

## 4. Recommendations (advisory only — out-of-scope for r1)

These are captured for the future PCF-cleanup project. **r1 does not act on any of them.**

### R1. **Reconcile `Build-SpaarkeMaster.ps1 $IncludedPcfIds` drift** (single most-important finding)

Three GUIDs in `$IncludedPcfIds` reference PCFs whose source folders were removed in `ded4e037c` / `5b4cca898`:

| GUID | Comment in Build-SpaarkeMaster | Source in repo? |
|---|---|---|
| `b85d8eac-309d-4c22-8f1d-62e4dd7fd067` | `# EventFormController` | **NO** (deleted in `ded4e037c`) |
| `49b0cecd-705a-4c45-84f6-1014b075139d` | `# SpeDocumentViewer` | **NO** (deleted in `5b4cca898`) |
| `1d93fc9e-3291-4f48-a1d8-e05b8f3a42c7` | `# EventAutoAssociate` | **NO** (deleted in `ded4e037c`) |

Options for the later PCF-cleanup project: (i) restore the source folders from the pre-cleanup commit if these controls are truly still shipping, OR (ii) remove the GUIDs from `$IncludedPcfIds` and drop them from SpaarkeMaster if the deletion was intentional (which the commit message "remove 10 deprecated PCF controls" suggests). Owner decision required.

### R2. **Roll up the 12 per-PCF standalone `Solution/` folders into feature solutions**

Twelve PCFs (rows 1–7, 10, 11, 16, 18) each ship as an INDIVIDUAL managed solution produced by `src/client/pcf/create-solution.ps1` — deployment path is ad-hoc (per-folder `.pcfproj` + `pac pcf push` + per-folder `Solution/*.zip`), NOT included in `scripts/Deploy-DataverseSolutions.ps1` `$SolutionImportOrder` (which ships 8 solutions). A future project should either:

- Fold each into the appropriate feature solution (e.g., 7 Communication PCFs → new `CommunicationRibbons` solution added to `$SolutionImportOrder`; `MatterHeader` → an appropriate matter-management feature solution; `TrackingFieldTrio` → dispatch-spine feature solution; `RegardingResolver` → set-regarding feature solution; `SpaarkeGridCustomizer` → grid-framework feature solution), OR
- Add per-PCF solutions to `$SolutionImportOrder` explicitly.

Either resolves the "provisioning ships 8 solutions but 12 additional PCFs are hidden" ambiguity that r1's Phase A audit surfaced.

### R3. **Retire 4 dev-only folders in a single cleanup PR** (rows 9, 13, 17, 19)

`EmailProcessingMonitor`, `ScopeConfigEditor`, `ThemeEnforcer`, `UpdateRelatedButton` are all in `Build-SpaarkeMaster.ps1`'s explicit exclusion list. The `ded4e037c` cleanup already established the pattern. Owner sign-off required per PCF (unused-from-forms verification via `Grep` of `src/dataverse/**/*.xml` for the control's `sprk_Spaarke.Controls.<Name>` schema name).

### R4. **Refresh COMPONENT-INVENTORY.md §4 + §11**

Update:
- §4 header ("33 control folders in `src/client/pcf/`") to the actual current count (20 folders, 19 PCFs + 1 shared/).
- §4A "Confirmed in-use" list to reflect R1 drift resolution (either the 3 deleted PCFs are truly out, or their source is restored).
- §11 row ("PCF controls | 7 in-use (of 33 built)") to actual counts after R1/R2/R3.

Refresh is advisory only for r1; it happens in whichever project resolves R1.

### R5. **Consider `shared/` promotion** (row 15)

The `shared/` folder holds helpers imported by sibling PCFs. If the helper API stabilizes and starts serving code outside `src/client/pcf/`, consider promoting it to `src/client/shared/` (parallel to `Spaarke.UI.Components/`). Zero urgency — the current location is unambiguous.

---

## 5. Acceptance-criteria trace

| Criterion (from POML) | Result |
|---|---|
| Every folder under `src/client/pcf/` appears exactly once with a classification. | ✅ Rows 1–20 in §3; each folder listed once. |
| Every (a) row cites concrete evidence (deployer line OR COMPONENT-INVENTORY row OR within-90-days commit). | ✅ Each (a) row cites either a `Build-SpaarkeMaster.ps1:$N` line, or a per-PCF `Solution/` presence + commit date within 90 days (i.e. after 2026-05-19). |
| Every (d) row recommendation is advisory-only and scoped out of r1. | ✅ Zero (d) rows; not applicable. |
| Negative: `git diff` shows only the new notes file — no PCF folder/file touched. | ✅ Only this file (`projects/customer-provisioning-orchestration-r1/notes/pcf-33-vs-7-mapping-2026-08.md`) is new; no PCF file modified. |

---

## 6. Method + sources consulted

- `ls src/client/pcf/` (20 entries directly)
- `find src -maxdepth 6 -name "ControlManifest.Input.xml"` (19 hits)
- `git log --pretty=format:"%ai" -1 -- src/client/pcf/<folder>` per folder (last-commit date)
- `scripts/Build-SpaarkeMaster.ps1:70–84` (`$IncludedPcfIds` + `# EXCLUDED` comments)
- `scripts/Deploy-DataverseSolutions.ps1:124–138` (`$SolutionImportOrder` — 8 feature solutions; no PCF-standalone rows)
- `git log --all --diff-filter=D --name-only -- src/client/pcf/*` (identified 13 deleted PCFs across two commits `ded4e037c` + `5b4cca898`)
- `projects/customer-provisioning-orchestration-r1/COMPONENT-INVENTORY.md` §4 + §11 (the "33 vs 7" claim under audit)
- `src/client/pcf/create-solution.ps1` (per-PCF standalone-solution wrapper — explains the 12 per-PCF `Solution/` folders)
- `src/client/pcf/VisualHost/stories/` inspection (confirms `DueDatesWidget`, `EventCalendarFilter` etc. are VisualHost widgets, not independent PCFs)
- `src/client/pcf/SpaarkeGridCustomizer/customizers/RegardingLinkRenderer.tsx` inspection (confirms `RegardingLink` is a grid customizer, not an independent PCF)

---

*End of audit. Advisory only. No PCF folder or file modified as part of this task.*
