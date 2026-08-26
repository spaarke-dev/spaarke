# `src/solutions/` reconciliation — audit note

> **Date**: 2026-08-17
> **Author**: Task 008 (customer-provisioning-orchestration-r1, Phase A)
> **Scope**: Read-only audit per spec §11.1a + FR-09 + Q2 owner clarification
> **Feeds**: Wave C4 H6 handler (Package Deployer for the authoritative 8 solutions)
> **Status**: Advisory. This note enumerates and classifies; remediation of classes (c) / (d) is OUT OF SCOPE for r1 (recommendations only).

---

## 1. Executive summary

- `src/solutions/` has **36 top-level folders** as of `HEAD` on branch `work/customer-provisioning-orchestration-r1` (commit at task kickoff = `b6022edd7`).
- **8 folders are the authoritative Dataverse-managed solutions** shipped by H6 (Package Deployer) per `scripts/Deploy-DataverseSolutions.ps1 $SolutionImportOrder`. This is the corrected count per spec §11.1a / Q2 (design v3.1's "~10" is retired).
- **28 remaining folders** ("non-deployer-listed") classify as: **14 code pages via `Deploy-Release.ps1` Phase 4** (class a), **0 feature-solution-scoped subfolders** (class b — the 8 solution folders have no top-level companions), **3 dev-only/retired** (class c), and **11 unknown-needs-review** (class d — active code pages not wired into `Deploy-Release.ps1` Phase 4 today).
- **Reconciliation math**: 36 total − 8 authoritative = 28 non-listed. Matches spec §11.1a's "~28" characterization exactly.
- **One drift noted** (not a defect in r1 scope): `scripts/Deploy-DataverseSolutions.ps1` header comment still says "all 10 Spaarke managed solutions" (lines 6 and 36) while `$SolutionImportOrder` contains exactly 8 entries. See §5 recommendation R1.

---

## 2. Authoritative 8 solutions (H6 Package Deployer input)

Verbatim from `scripts/Deploy-DataverseSolutions.ps1` lines 124–138 (`$SolutionImportOrder` — the single source of truth):

| # | Folder | `SolutionName` (in Dataverse) | Display name | Tier |
|---|---|---|---|---|
| 1 | `SpaarkeCore` | `SpaarkeCore` | Spaarke Core | 1 (base entities/option-sets/roles) |
| 2 | `webresources` | `SpaarkeWebResources` | Spaarke Web Resources | 2 (JS files for forms/ribbons) |
| 3 | `CalendarSidePane` | `CalendarSidePane` | Calendar Side Pane | 3 (feature — independent) |
| 4 | `DocumentUploadWizard` | `DocumentUploadWizard` | Document Upload Wizard | 3 |
| 5 | `EventCommands` | `EventRibbons` | Event Ribbon Commands | 3 |
| 6 | `EventDetailSidePane` | `EventDetailSidePane` | Event Detail Side Pane | 3 |
| 7 | `EventsPage` | `EventsPage` | Events Page | 3 |
| 8 | `LegalWorkspace` | `LegalWorkspace` | Legal Workspace | 3 |

**Notes**:
- Tier-1 → Tier-2 → Tier-3 is a strict dependency order (SpaarkeCore first, then webresources, then the six independent Tier-3 features).
- `EventsPage` and `DocumentUploadWizard` folders are dual-purpose: they are **both** an authoritative managed solution (Tier 3) **and** contain a `dist/` build referenced by `Deploy-Release.ps1` Phase 4 as a code page (`sprk_eventspage` via `Deploy-EventsPage.ps1`; `sprk_documentuploadwizard` via `Deploy-WizardCodePages.ps1`). This is intentional — the managed solution ships the ribbon/entity metadata, and the Phase-4 web resource upload ships the compiled HTML/JS payload.
- Full FR-09 acceptance requires all 8 imported at correct versions.

---

## 3. Code pages deployed via `Deploy-Release.ps1` Phase 4

Phase 4 (Deploy-Release.ps1 lines 482–514) invokes `scripts/Deploy-AllWebResources.ps1` (5 active components; 2 retired), which in turn deploys the following folders under `src/solutions/`:

### 3a. Per-component wiring from `Deploy-AllWebResources.ps1` (lines 87–118)

| Component | Sub-script | Solution folders consumed |
|---|---|---|
| `SpeAdminApp` | `Deploy-SpeAdminApp.ps1` | `src\solutions\SpeAdminApp` (line 26 of that script) |
| `WizardCodePages` | `Deploy-WizardCodePages.ps1` | 12 folders (see §3b table below) |
| `EventsPage` | `Deploy-EventsPage.ps1` | `src\solutions\EventsPage\dist\index.html` (line 20 of that script) — **already in authoritative-8** |
| `PCFWebResources` | `Deploy-PCFWebResources.ps1` | PCF bundle output (not from `src/solutions/`) |
| `RibbonIcons` | `Deploy-RibbonIcons.ps1` | 3 SVG icons (not from `src/solutions/`) |
| `CorporateWorkspace` | RETIRED 2026-05-26 | `LegalWorkspace/dist/corporateworkspace.html` (commented out; keep-out guard in script) |
| `ExternalWorkspaceSpa` | RETIRED 2026-07-20 | Removed; now Azure Static Web Apps |

### 3b. `Deploy-WizardCodePages.ps1` inventory (lines 43–61)

| # | Web-resource name | Folder consumed | Type |
|---|---|---|---|
| 1 | `sprk_creatematterwizard` | `src\solutions\CreateMatterWizard\dist\index.html` | HTML |
| 2 | `sprk_createprojectwizard` | `src\solutions\CreateProjectWizard\dist\index.html` | HTML |
| 3 | `sprk_createeventwizard` | `src\solutions\CreateEventWizard\dist\index.html` | HTML |
| 4 | `sprk_createinvoicewizard` | `src\solutions\CreateInvoiceWizard\dist\index.html` | HTML |
| 5 | `sprk_createreportcardwizard` | `src\solutions\CreateReportCardWizard\dist\index.html` | HTML |
| 6 | `sprk_createtodowizard` | `src\solutions\CreateTodoWizard\dist\index.html` | HTML |
| 7 | `sprk_createworkassignmentwizard` | `src\solutions\CreateWorkAssignmentWizard\dist\index.html` | HTML |
| 8 | `sprk_summarizefileswizard` | `src\solutions\SummarizeFilesWizard\dist\index.html` | HTML |
| 9 | `sprk_findsimilar` | `src\solutions\FindSimilarCodePage\dist\index.html` | HTML |
| 10 | `sprk_playbooklibrary` | `src\solutions\PlaybookLibrary\dist\index.html` | HTML |
| 11 | `sprk_wizard_commands` | `src\client\webresources\js\sprk_wizard_commands.js` | JS (not a `src/solutions/` folder — informational) |
| 12 | `sprk_documentuploadwizard` | `src\solutions\DocumentUploadWizard\dist\index.html` | HTML (**folder also in authoritative-8**) |
| 13 | `sprk_alldocuments` | `src\solutions\AllDocuments\dist\alldocuments.html` | HTML |
| 14 | `sprk_workspacelayoutwizard` | `src\solutions\WorkspaceLayoutWizard\dist\sprk_workspacelayoutwizard.html` | HTML |

### 3c. Standalone code-page scripts (NOT wired into `Deploy-AllWebResources.ps1`)

| Script | Folder consumed | Note |
|---|---|---|
| `scripts/Deploy-ReportingCodePage.ps1` | `src\solutions\Reporting` | Standalone; **NOT included in `Deploy-Release.ps1` Phase 4 today** — see §5 recommendation R3 |

---

## 4. Classification table — all 36 folders under `src/solutions/`

Columns:
- **Class**: `(A8)` = authoritative-8; `(a)` = code page via `Deploy-Release.ps1` Phase 4; `(b)` = feature-solution-scoped (companion inside an authoritative-8 folder — **none found**); `(c)` = dev-only/retired; `(d)` = unknown-needs-review.
- **Evidence**: canonical citation for the classification.
- **Last commit**: `git log -1 --format=%cs` per folder as of `HEAD`.

| # | Folder | Class | Evidence | Last commit |
|---|---|---|---|---|
| 1 | `AllDocuments` | (a) | `Deploy-WizardCodePages.ps1:57` → `sprk_alldocuments` | 2026-08-07 |
| 2 | `CalendarSidePane` | (A8) | `Deploy-DataverseSolutions.ps1:132` (`$SolutionImportOrder` Tier 3) | 2026-06-29 |
| 3 | `CommunicationReconciliation` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Very recent (2026-08-14) → likely active dev. See R2. | 2026-08-14 |
| 4 | `CopilotAgent` | (c) | No `package.json`; no build output; not referenced anywhere in `scripts/`. Last touch 2026-05-26 (~3 months stale). Likely retired. See R4. | 2026-05-26 |
| 5 | `CreateEventWizard` | (a) | `Deploy-WizardCodePages.ps1:45` → `sprk_createeventwizard` | 2026-07-20 |
| 6 | `CreateInvoiceWizard` | (a) | `Deploy-WizardCodePages.ps1:46` → `sprk_createinvoicewizard` | 2026-07-10 |
| 7 | `CreateMatterWizard` | (a) | `Deploy-WizardCodePages.ps1:43` → `sprk_creatematterwizard` | 2026-07-18 |
| 8 | `CreateProjectWizard` | (a) | `Deploy-WizardCodePages.ps1:44` → `sprk_createprojectwizard` | 2026-07-18 |
| 9 | `CreateReportCardWizard` | (a) | `Deploy-WizardCodePages.ps1:47` → `sprk_createreportcardwizard` | 2026-07-10 |
| 10 | `CreateTodoWizard` | (a) | `Deploy-WizardCodePages.ps1:48` → `sprk_createtodowizard` | 2026-06-18 |
| 11 | `CreateWorkAssignmentWizard` | (a) | `Deploy-WizardCodePages.ps1:49` → `sprk_createworkassignmentwizard` | 2026-07-20 |
| 12 | `DailyBriefing` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Very recent (2026-08-14). Likely a new active code page missing Phase-4 wiring. See R2. | 2026-08-14 |
| 13 | `DemoRegistration` | (c) | No `package.json`; not referenced in `scripts/`. Last touch 2026-04-03 (oldest folder). Likely retired. See R4. | 2026-04-03 |
| 14 | `DocumentUploadWizard` | (A8) + (a) dual | `Deploy-DataverseSolutions.ps1:133` (`$SolutionImportOrder` Tier 3) **AND** `Deploy-WizardCodePages.ps1:56` (code page `sprk_documentuploadwizard`). Dual by design. | 2026-08-02 |
| 15 | `EmailPage` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Recent (2026-08-14). See R2. | 2026-08-14 |
| 16 | `EventCommands` | (A8) | `Deploy-DataverseSolutions.ps1:134` (`$SolutionImportOrder` Tier 3, `SolutionName=EventRibbons`) | 2026-02-06 |
| 17 | `EventDetailSidePane` | (A8) | `Deploy-DataverseSolutions.ps1:135` (`$SolutionImportOrder` Tier 3) | 2026-08-02 |
| 18 | `EventsPage` | (A8) + (a) dual | `Deploy-DataverseSolutions.ps1:136` (`$SolutionImportOrder` Tier 3) **AND** `Deploy-EventsPage.ps1:20` (code page HTML upload). Dual by design. | 2026-07-07 |
| 19 | `FindSimilarCodePage` | (a) | `Deploy-WizardCodePages.ps1:51` → `sprk_findsimilar` | 2026-08-02 |
| 20 | `LegalWorkspace` | (A8) | `Deploy-DataverseSolutions.ps1:137` (`$SolutionImportOrder` Tier 3). Note: `sprk_corporateworkspace` web-resource retired 2026-05-26 (see `Deploy-WizardCodePages.ps1:60` commented block). | 2026-08-13 |
| 21 | `NavigatorPane` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Recent (2026-08-14). See R2. | 2026-08-14 |
| 22 | `Notepad` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Last touch 2026-07-05. See R2. | 2026-07-05 |
| 23 | `PlaybookLibrary` | (a) | `Deploy-WizardCodePages.ps1:52` → `sprk_playbooklibrary` | 2026-07-29 |
| 24 | `Reporting` | (a) [drift] | `Deploy-ReportingCodePage.ps1` (standalone) — builds `src/solutions/Reporting` and uploads `sprk_reporting`. **NOT wired into `Deploy-AllWebResources.ps1`**, so a normal `Deploy-Release.ps1` Phase-4 run does NOT deploy Reporting today. See R3. | 2026-06-29 |
| 25 | `SmartTodo` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). See R2. | 2026-08-02 |
| 26 | `spaarke_insights` | (c) | No `package.json`; not referenced in `scripts/`. Naming convention (lower_snake) diverges from all other folders (Pascal). Last touch 2026-05-28. Likely legacy/retired. See R4. | 2026-05-28 |
| 27 | `SpaarkeAi` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script directly (grep clean). Recent (2026-08-14). Note: root `CLAUDE.md` §17 documents SpaarkeAi as a first-class code-page surface with a componentization audit — this row is a Phase-4 wiring gap, not a project-level unknown. See R2. | 2026-08-14 |
| 28 | `SpaarkeCore` | (A8) | `Deploy-DataverseSolutions.ps1:126` (`$SolutionImportOrder` Tier 1) | 2026-08-13 |
| 29 | `SpeAdminApp` | (a) | `Deploy-SpeAdminApp.ps1:26` → `sprk_speadmin`; wired into `Deploy-AllWebResources.ps1:87–92` | 2026-06-29 |
| 30 | `sprk_communicationconversationpage` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). Recent (2026-08-03). See R2. | 2026-08-03 |
| 31 | `sprk_communicationspage` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). See R2. | 2026-07-21 |
| 32 | `sprk_invoicespage` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). See R2. | 2026-06-11 |
| 33 | `sprk_kpiassessmentspage` | (d) | Not in `$SolutionImportOrder`; not referenced by any Phase-4 deploy script (grep clean). See R2. | 2026-06-11 |
| 34 | `SummarizeFilesWizard` | (a) | `Deploy-WizardCodePages.ps1:50` → `sprk_summarizefileswizard` | 2026-07-20 |
| 35 | `webresources` | (A8) | `Deploy-DataverseSolutions.ps1:129` (`$SolutionImportOrder` Tier 2, `SolutionName=SpaarkeWebResources`) | 2026-08-14 |
| 36 | `WorkspaceLayoutWizard` | (a) | `Deploy-WizardCodePages.ps1:61` → `sprk_workspacelayoutwizard` | 2026-07-03 |

### 4a. Tally

| Class | Count | Folders |
|---|---|---|
| (A8) authoritative-8 | 8 | SpaarkeCore, webresources, CalendarSidePane, DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage, LegalWorkspace |
| (a) code page via `Deploy-Release.ps1` Phase 4 | 14 | AllDocuments, CreateEventWizard, CreateInvoiceWizard, CreateMatterWizard, CreateProjectWizard, CreateReportCardWizard, CreateTodoWizard, CreateWorkAssignmentWizard, FindSimilarCodePage, PlaybookLibrary, Reporting (standalone script — drift, see R3), SpeAdminApp, SummarizeFilesWizard, WorkspaceLayoutWizard |
| (b) feature-solution-scoped subfolder | 0 | None — none of the authoritative-8 folders has top-level companions under `src/solutions/`. (Companion assets, when present, live inside the solution folder itself, e.g. `EventsPage/dist/index.html`.) |
| (c) dev-only / retired | 3 | CopilotAgent, DemoRegistration, spaarke_insights |
| (d) unknown / needs review (active code pages missing Phase-4 wiring) | 11 | CommunicationReconciliation, DailyBriefing, EmailPage, NavigatorPane, Notepad, SmartTodo, SpaarkeAi, sprk_communicationconversationpage, sprk_communicationspage, sprk_invoicespage, sprk_kpiassessmentspage |
| **Total non-listed** | **28** | (14a + 0b + 3c + 11d) |
| **Grand total** | **36** | (28 non-listed + 8 authoritative) |

Matches spec §11.1a's "~28 non-listed" characterization exactly.

---

## 5. Recommendations (advisory — remediation OUT OF SCOPE for r1)

None of these are r1 blockers. They exist to hand a clean baseline to Wave C4 (H6 handler) and to log latent drift for the appropriate future project.

- **R1 — Correct the stale doc-comment in `Deploy-DataverseSolutions.ps1`** (LOW). Lines 6 and 36 of the `.SYNOPSIS`/`.DESCRIPTION` block still say "all 10 Spaarke managed solutions" while `$SolutionImportOrder` (line 124) contains exactly 8. The **code is the source of truth** (per root CLAUDE.md §2); this note treats the 8 as authoritative. Owner should fix the header comment in a trivial doc PR; no code change needed. **Not r1 scope** — this is a comment cleanup, not a deployer defect.

- **R2 — Reconcile the 11 class-(d) code pages against Phase-4 wiring** (MEDIUM, MULTIPLE OWNERS). Each of the 11 unknown/needs-review folders is a code page that has recent activity but is NOT deployed by `Deploy-Release.ps1` Phase 4 today. Per-folder outcomes are one of: (i) add a wiring entry to `Deploy-WizardCodePages.ps1` (or a new sub-script) + register the web resource under the correct managed solution, or (ii) mark the folder retired if it's been superseded (e.g. by a component library or a widget). This work belongs to the owner of each surface — not r1. Preserving the enumeration in this note is enough to prevent silent divergence during Wave C4.

- **R3 — Wire `Deploy-ReportingCodePage.ps1` into `Deploy-AllWebResources.ps1`** (LOW-MEDIUM). Standalone `Deploy-ReportingCodePage.ps1` exists and works, but it is not called by the Phase-4 orchestrator, so a normal `Deploy-Release.ps1` run does NOT deploy the Reporting code page. Either add a component entry to `Deploy-AllWebResources.ps1` (5 → 6 active components) OR document the intentional exclusion. **Not r1 scope** — Reporting owner decision. Track alongside R2 in the appropriate follow-on project.

- **R4 — Decide on retirement / deletion of the 3 class-(c) folders** (LOW). `CopilotAgent` (2026-05-26), `DemoRegistration` (2026-04-03), and `spaarke_insights` (2026-05-28) show no `package.json`, no build output, and no references from any `scripts/` file. Retention cost is trivial; recommend a repo-hygiene sweep (e.g. by `/repo-cleanup` or the appropriate steward) to either restore under Phase-4 wiring (unlikely) or archive/delete. **Not r1 scope**.

- **R5 — Feed this note directly into Wave C4 H6 handler design** (BINDING for r1). H6 (Package Deployer for the 8 solutions) MUST source its input list from `$SolutionImportOrder` — never from a folder-enumeration of `src/solutions/`. That is the whole point of Q2 / §11.1a: enumeration produces 36 folders; only 8 are authoritative managed solutions. This note is the H6 baseline artifact.

---

## 6. Acceptance-criteria checklist

Per task 008 POML `<acceptance-criteria>`:

- [x] The authoritative-8 section (§2) enumerates all 8 solutions per `$SolutionImportOrder` verbatim.
- [x] The classification table (§4) contains every folder under `src/solutions/` exactly once with a class (A8/a/b/c/d).
- [x] Every class-(a) row cites the specific `Deploy-Release.ps1` Phase-4 line (via `Deploy-AllWebResources.ps1` → sub-script) that maps folder → web resource.
- [x] Class-(b) is enumerated (as empty, with justification) — no authoritative-8 folder has a top-level companion under `src/solutions/`.
- [x] Note does NOT use the retired "~10 solutions" count from design v3.1. (It appears exactly once, in §5 R1, as the stale doc-comment being flagged.)
- [x] Read-only audit: `git diff` shows only this new notes file. No code, script, or solution folder touched.

---

## 7. Method & sources

- **Solution enumeration**: `ls src/solutions/` on branch `work/customer-provisioning-orchestration-r1` at commit `b6022edd7` (task 001 landed).
- **Authoritative-8 source**: `scripts/Deploy-DataverseSolutions.ps1` lines 124–138 (`$SolutionImportOrder`).
- **Phase-4 wiring source**: `scripts/Deploy-Release.ps1` lines 482–514 → `scripts/Deploy-AllWebResources.ps1` lines 66–118 → per-component sub-scripts (`Deploy-SpeAdminApp.ps1`, `Deploy-WizardCodePages.ps1` lines 43–61, `Deploy-EventsPage.ps1`, `Deploy-PCFWebResources.ps1`, `Deploy-RibbonIcons.ps1`, standalone `Deploy-ReportingCodePage.ps1`).
- **Spec anchor**: `projects/customer-provisioning-orchestration-r1/spec.md` §11.1a solutions reconciliation + FR-09 + Q2 owner clarification (v3.3 line 44).
- **Per-folder metadata**: presence of `package.json` and last-commit date via `git log -1 --format=%cs -- src/solutions/{folder}`.
- **"Not referenced" claims for classes (c)/(d)**: verified by `grep` across `scripts/**` for each folder name; no matches means no deploy-script wiring.
