# Create-Wizard Duplication Analysis & Reconciliation Plan

> **Filed**: 2026-09-01 · **Project**: `unified-access-control-r2` · **Trigger**: owner question — *"why do we have two of the same CreateProject? … if we only need 1 then the other needs to be removed; if we do need 2 … BOTH need to have the same steps including secure projects (and in the same order)."*
>
> **Method**: full Fable-model analysis. Every claim below is either **VERIFIED** (file:line / commit SHA cited, checked from the repo ROOT `c:\code_files\spaarke-wt-unified-access-control-r2`) or explicitly marked **INFERRED** / "no evidence found". `Spaarke.UI.Components/dist/**` and `node_modules/**` were **excluded** everywhere — compiled copies there are build artifacts, not a third implementation.
>
> **Status**: ANALYSIS ONLY. Nothing was deleted, moved, or refactored. The plan in §E awaits owner approval.

---

## Executive answer (one paragraph)

There are two source copies of the Create Project wizard because the March-2026 **UI Dialog & Shell Standardization** project (`projects/x-ui-dialog-shell-standardization/`, "UDSS") specified a **move** of the wizard from LegalWorkspace into `@spaarke/ui-components` (commit `d474e46df`, 2026-03-19) but **never filed the deletion of the source folder** — unlike QuickStart, which got an explicit removal task (`025-remove-quickstart.poml`). The LegalWorkspace copy has been **dead code ever since**: nothing imports `ProjectWizardDialog.tsx` (verified two independent ways), and every live launch path — WorkspaceGrid card, Get Started card, ribbon button, SpaarkeAi widget, Assistant hand-off — opens the `sprk_createprojectwizard` Code Page, which renders the **shared** copy (`src/solutions/CreateProjectWizard/src/main.tsx:9`). So the answer to "do we need 2?" is **no — only one is live**, the parity-of-steps branch of the question dissolves for this pair, and the reconciliation is: **delete the dead LegalWorkspace wizard chain (keeping its two genuinely-live files), pin tasks 068/093 to the shared copy, and fix the one REAL live parity gap this analysis surfaced — the Secure toggle rendered inside SummarizeFilesWizard's create-project follow-on that never provisions.**

---

## (A) Why two exist — evidence, then verdict

### A.1 Git chronology (all VERIFIED via `git log --diff-filter=A` / `--follow` per file)

| Date | SHA | Event |
|---|---|---|
| 2026-03-04 | `29f547c18` | **Original** created: `src/solutions/LegalWorkspace/src/components/CreateProject/{ProjectWizardDialog,CreateProjectStep,projectService,projectFormTypes}` — *"feat(workspace): add Create New Project wizard using WizardShell"*. Wired to WorkspaceGrid's "create-new-project" card as an **inline lazy dialog**. |
| 2026-03-16 | `21567b3da` | *"feat(sdap): implement Secure Project & External Access Platform (complete)"* adds **`SecureProjectSection.tsx`** (+ provisioning) to the **LegalWorkspace** copy — Phase 6: "Secure Project toggle in Create Project wizard". |
| 2026-03-19 | `d474e46df` | **The fork point.** *"feat(ui): implement UI Dialog & Shell Standardization — all 43 tasks"*: "Extract all wizard/dialog components from Corporate Workspace into standalone Code Page web resources backed by shared library components… CreateMatter + CreateProject wizards extracted, Code Pages created… WorkspaceGrid fully restructured — all inline dialogs replaced with navigateTo." Creates `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/**` (both `CreateProjectStep.tsx` and `SecureProjectSection.tsx` added here; `git log --follow` traces the shared section back to `21567b3da` — a detected copy). **The LegalWorkspace originals were left in the tree.** |
| 2026-03-23 | `63aa48683` | Shared copy only: *"…move Secure Project to top (WCWE-013, WCWE-014)"* — the Secure section moves from the bottom of the Enter Info step (line ~500 at the fork, verified via `git show d474e46df:<path>`) to the **top** (today `CreateProjectStep.tsx:402`). The LW copy never received this. |
| 2026-03-23/24 | `72ff34234`, `844bf7e92`, `d791c759d` | Shared copy only: Dataverse lookup **side pane** (`DataverseLookupField`), route-prefix fixes. |
| 2026-07-18 | `0998a18b7` | Shared copy only: Assistant hand-off pre-seed (initialFormValues / initialFileRefs / onComplete honest-ack). |
| 2026-08-02 | `7c8b4108b` | LW copy touched **mechanically** by the modal-system P7 sweep (size scale / dedup) — not feature parity. |
| 2026-08-25 | (uac-r2 BFF task 021) | Shared copy only: provisioning semantics rewritten — *"assign the project to the canonical Secure Project business unit's owner team, then provision its own SPE container… No business unit or account is created (BFF task 021, 2026-08-25)"* (`CreateProjectWizard.tsx:683-686`). The LW copy still implements the retired BU-per-project model (*"The BU will be named 'SP-{projectName}'"*, `ProjectWizardDialog.tsx:174-176`). |

### A.2 What the UDSS project record says (VERIFIED by sub-agent read of `projects/x-ui-dialog-shell-standardization/`)

- `design.md:189` — "**Move** wizard components from `src/solutions/LegalWorkspace/src/components/` to `@spaarke/ui-components`"; row `:194` lists `CreateProject | LegalWorkspace/components/CreateProject/ | 7 files`.
- `spec.md:90` — "`src/solutions/LegalWorkspace/src/components/` — **remove wizard components**, simplify WorkspaceGrid".
- `tasks/007-extract-create-project-wizard.poml:28` step 3 — "**Move** and refactor files into the new directory".
- **But** task 007's acceptance criteria (`:34-39`) check only that files exist at the NEW path, compile, and use `IDataService` — **no criterion asserts the source folder was removed**. Task `009-update-workspace-phase1.poml:25` removes only the *imports* ("Remove LazyWizardDialog and LazyProjectWizardDialog imports"). By contrast the same project filed an explicit deletion task for QuickStart (`025-remove-quickstart.poml`: "QuickStart/ folder completely removed"). **There is no equivalent deletion task for CreateProject/, CreateMatter/, or CreateEvent/**, and no note recording a deliberate keep or a deferral.
- One deliberate exception IS recorded: `design.md:275` — "**CloseProjectDialog — simple confirmation, remains inline**" (i.e., stays in LegalWorkspace). That file is genuinely live today (§B.3).

### A.3 What the architecture record says (VERIFIED by sub-agent read; my spot-checks concur)

| Document | Finding |
|---|---|
| `docs/architecture/LEGALWORKSPACE-RETIREMENT.md` (OC-R4-05) | **Does not answer this.** It is a *deploy* retirement of the standalone `sprk_corporateworkspace` web resource, "NOT the LegalWorkspace components" (line 5). The blanket row `:31` marks `src/solutions/LegalWorkspace/src/**` "Retained (library only)". Its §3 "Preserved" list names `LegalWorkspaceApp` + section components — **never any wizard**. Its MUST-NOT-delete (`:111`) is narrowly scoped: authors must not delete LW components *"on the grounds of 'R3 FR-25 is superseded'"*. It rebuts a retirement-based deletion rationale; it does **not** assert the wizard copies are live. Its consumer audit (§4) never enumerates CreateProject. |
| `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (OC-R4-06) | **Not this case.** OC-R4-06 is the intentional retention of two *mount wrappers* (Dashboard `LegalWorkspaceApp` vs Direct `WorkspaceWidgetRegistry`) — "do not propose unifying them" (`:35`, glossary `:412`). Wizards appear only as *examples mounted by* the Direct wrapper (`:128`). There is precedent for deliberate duplication in this codebase, and **this is not it**. |
| `docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md` | The §8 remediation backlog (7 items, lines 264-278) contains **no item** about the LW wizard copies. **Not a filed known issue.** |
| `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` | Treats Create Project as Archetype 4/5 — a Code-Page-backed launcher whose "actual feature lives in a separate Code Page web resource" (§6.1:459). Its §8:885 anti-pattern "**Never two divergent copies**" is scoped to *sections* (LW vs SpaarkeAi), but is the closest rule in spirit. |
| `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` | Lists `CreateProjectWizard` **only** under `@spaarke/ui-components` (`:63`); the `@spaarke/legal-workspace` inventory (`:169-176`) exports exactly two symbols, no wizards. The component model already treats the shared copy as the only one. |
| The only acknowledgment anywhere | `projects/_backlog/needs-a-project.md:665,677` (unstarted seed item about follow-on actions): LW wizard copies "**may already be deprecated; verify before touching**" … task list would include "retirement of duplicated code in legacy LegalWorkspace copies (or confirmation they're already dead code)". Explicitly unresolved. |
| **Contradicting drift** (docs that still cite the LW copy as canonical) | `docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md:353-356` (inventory lists `components/CreateProject/ProjectWizardDialog.tsx`; header says "Status: Current", v2.0.1, 2026-04-05) and `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md:139-145` (uses **LW** `CreateProject/provisioningService.ts` as its worked BFF example). Both post-date the hoist and are stale. |

### A.4 Verdict

**(iii) Unmanaged drift — a fork nobody closed.** Specifically: an **extraction that was specified as a move but executed as a copy**, because the deletion step was never made a task or an acceptance criterion. It is *not* deliberate-and-documented (no doc records a keep; the component model and widget guides already pretend the LW copy doesn't exist), and it is *not* a live two-implementation situation (§B proves the LW wizard chain is unmounted). The evidence that settles it: UDSS `spec.md:90` says "remove wizard components" + no removal task exists + zero importers of `ProjectWizardDialog` today + every launcher resolves to the Code Page wrapping the shared copy.

One trap to respect: root `CLAUDE.md` §11 lists *"Delete LegalWorkspace `CreateRecordStep.tsx` as dead code per OC-R4-05"* as a known **anti-pattern** — because OC-R4-05 (deploy retirement) is the **wrong rationale** for deletion. The correct rationale, established here, is **verified dead code left behind by an incomplete UDSS move** — a different argument, now backed by per-file importer evidence (§B.3). Any deletion PR must cite *this* analysis, not the retirement doc.

---

## (B) The complete map

### B.1 The wizard census (VERIFIED; all paths relative to repo root; `dist/` excluded)

The claim "7 Create wizards wired to the field-mapping framework" is **exactly correct**: 7 entity services import + call `applyFieldMappings` — `eventService.ts:18/:400`, `matterService.ts:33/:308`, `projectService.ts:28/:380`, `todoService.ts:34/:214`, `workAssignmentService.ts:34/:498`, `invoiceService.ts:37/:282`, `reportCardService.ts:48/:238`. `CreateRecordWizard` is the generic engine (no service, by design). Step assembly is config-driven in `CreateRecordWizard.tsx:779-814`: `[associate-to?] → [add-files?] → info → next-steps (+ dynamic follow-ons) → success`.

| # | Wizard | Entity | Live implementation | Steps in order (as mounted) | Secure step? | Code Page | Mounted / launched from |
|---|---|---|---|---|---|---|---|
| 1 | **CreateProjectWizard** | `sprk_project` | `Spaarke.UI.Components/src/components/CreateProjectWizard/` | Associate To (Account/Matter, `CreateProjectWizard.tsx:465-475`) → Add file(s) → **Enter Info** (Secure section at TOP of step, `CreateProjectStep.tsx:402`) → Next Steps → Success. Secure provisioning runs invisibly inside `onFinish` (`CreateProjectWizard.tsx:688-705`), post-create. | **YES** (§ within Enter Info, not a standalone step) | `src/solutions/CreateProjectWizard/src/main.tsx:9` → `sprk_createprojectwizard` | WorkspaceGrid `:315/:327`; getStarted card `:65`; ribbon `sprk_wizard_commands.js:212,298` via `spaarke_insights/…/sprk_Matter/RibbonDiff.xml:140`; AI widget `CreateProjectWizardWidget.tsx:71,159`; Assistant `surfaceLaunchRegistry.ts:96`; `wizardLaunchers.ts:231` (launcher itself currently has no production caller) |
| 1b | **CreateProjectWizard (DEAD twin)** | `sprk_project` | `src/solutions/LegalWorkspace/src/components/CreateProject/` | (identical shape at its 2026-03-19 freeze: Add file(s) → Enter Info with Secure section at BOTTOM `CreateProjectStep.tsx:495` → Next Steps → Success; **no Associate To step** — `ProjectWizardDialog.tsx` config has no `associateToStep`) | YES (bottom of Enter Info) | — | **NOBODY.** Zero importers of `ProjectWizardDialog.tsx` (verified: name-grep + path-fragment grep + `React.lazy` grep across `src/`). |
| 2 | **CreateMatterWizard** | `sprk_matter` | shared lib `CreateMatterWizard/` | Associate To (Project/Account/Invoice `:382-393`) → Add file(s) → Enter Info (`:395-412`) → Next Steps → Success | No | `CreateMatterWizard/src/main.tsx:9` → `sprk_creatematterwizard` | WorkspaceGrid `:284/:296`; getStarted `:63`; ribbon `:206,294` + RibbonDiff `:82`; `CreateMatterWizardWidget` (`register-workspace-widgets.ts:478,497`); Assistant registry `:87` |
| 2b | CreateMatter (DEAD twin, top dialog) | — | `LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx` | — | No | — | Imported only by `CreateMatter/index.ts:10-11`; **that barrel has no consumer** (every external hit is a deep import of leaf files — see §B.3) |
| 3 | **CreateEventWizard** | `sprk_event` | shared lib `CreateEventWizard/` | Associate To (gated `:90-105/:334-339`) → Add file(s) → Event Details (`:341-353`) → Next Steps → Success | No | `CreateEventWizard/src/main.tsx:5` → `sprk_createeventwizard` | WorkspaceGrid `:381/:384`; latestUpdates card `:38`; ribbon `:218,302` + RibbonDiff `:127`; Assistant registry `:101` (as `create-task`) |
| 3b | CreateEvent (DEAD twin) | — | `LegalWorkspace/src/components/CreateEvent/EventWizardDialog.tsx` | — | No | — | **NOBODY** (only self-references `:2,41,48,51,145,200`; no `index.ts` in the folder) |
| 4 | **CreateTodoWizard** | `sprk_todo` | shared lib `CreateTodoWizard/TodoWizardDialog.tsx` | Associate To (gated `:192`) → Add file(s) → To Do Details (`:200-202`) → Next Steps → Success | No | `CreateTodoWizard/src/main.tsx:8` → `sprk_createtodowizard` | WorkspaceGrid `:397/:400`; todo section `:234`; ribbon `:224` + RibbonDiff `:153` |
| 5 | **CreateWorkAssignmentWizard** | `sprk_workassignment` (+ follow-on `sprk_event` `:667`) | shared lib — drives `WizardShell` DIRECTLY (`WorkAssignmentWizardDialog.tsx:5,633`), not `CreateRecordWizard` | Work to Assign (`:446-447`) → Add Files (`:459-460`) → Enter Info (`:466-467`) → Next Steps (`:500-501`) → dynamic assign-work/send-email/create-event → Success | No | `CreateWorkAssignmentWizard/src/main.tsx:8` → `sprk_createworkassignmentwizard` | WorkspaceGrid `:413/:416`; getStarted `:67`; ribbon `:306`; Assistant registry `:121` |
| 6 | **CreateInvoiceWizard** | `sprk_invoice` | shared lib `CreateInvoiceWizard/` | Associate To (`:456`) → Add file(s) → Invoice Details (`:458-460`) → Next Steps (custom cards `:342,:365,:380`) → Success | No | `CreateInvoiceWizard/src/main.tsx:5` → `sprk_createinvoicewizard` | **VisualHost "+" button only** (`VisualHostRoot.tsx:81-85`). No ribbon command exists. |
| 7 | **CreateReportCardWizard** | `sprk_reportcard` | shared lib `CreateReportCardWizard/` | Associate To (Matter+Project `:453`) → *(NO files step — `hideFilesStep:true` `:456`)* → Report Card Details (`:458-460`) → Next Steps (custom cards) → Success | No | `CreateReportCardWizard/src/main.tsx:5` → `sprk_createreportcardwizard` | **VisualHost "+" button only**. No ribbon command. |
| — | SummarizeFilesWizard (creates a project as a follow-on) | none primary; `sprk_project` via follow-on | shared lib `SummarizeFilesWizard/` (drives `WizardShell` directly `:678`) | Upload files (`:577-578`) → Run analysis (`:615-616`) → Next Steps (`:639-640`) → dynamic **create-project step** (`:408/:415` → `SummarizeCreateProjectStep.tsx:9,43` = the SHARED `CreateProjectStep` verbatim, Secure toggle included) → Success | **Transitively YES — see §C.4 parity bug** | `SummarizeFilesWizard/src/main.tsx:25` → `sprk_summarizefileswizard` | WorkspaceGrid `:346/:351`; getStarted `:71`; ConversationPane `:28,1360`; ribbon `:264` + RibbonDiff `:192`; Assistant registry `:129` |
| — | DocumentUploadWizard | `sprk_document` | **solution-local** `src/solutions/DocumentUploadWizard/src/DocumentUploadWizardDialog.tsx` (never hoisted to shared lib) | associate-to (`:433-434`) → add-files (`:451-452`) → processing (`:468-469`) → next-steps (`:504-505`) → send/finish | No | `main.tsx:33` → `sprk_documentuploadwizard` | WorkspaceGrid `:573`; ribbon `:230/:258` + RibbonDiff `:205`; subgrid commands |
| — | FindSimilar (search, not a create) | none | shared lib `FindSimilar/` (LW `FindSimilar/FindSimilarDialog.tsx:11` is a thin **adapter over the shared one** — not a fork) | Upload file(s) (`:259-260`) → Results (`:297-298`) | No | `FindSimilarCodePage/src/main.tsx` → `sprk_findsimilar` | WorkspaceGrid `:364/:368`; getStarted `:73`; ribbon `:270`; `FindSimilarWizardWidget` |

**Is the duplication ONLY CreateProject? NO — but liveness inverts per file.** LegalWorkspace retains **four** local wizard folders (`CreateProject/` 9 files, `CreateMatter/` 20 files, `CreateEvent/` 4 files, `FindSimilar/` 5 files). The three top-level LW wizard dialogs (Project / Matter / Event) are all **dead**; LW FindSimilar is a live-pattern *adapter*, not a duplicate, but is itself unconsumed. And two smaller duplications run the OTHER way (§B.3: `CloseProjectDialog` + `closureService` live in LW, shared twins unconsumed; `ProvisioningProgressStep` duplicated with **zero** mounts on either side).

### B.2 Where the LIVE CreateProject wizard actually renders (the mount chain, end-to-end VERIFIED)

```
Launchers (all → the same Code Page, never a component import):
  WorkspaceGrid.tsx:315 "Create New Project — opens Code Page dialog via navigateTo (UDSS-009)"
  getStarted.registration.ts:65 · sprk_wizard_commands.js:212,298 (ribbon, via sprk_Matter RibbonDiff.xml:140)
  CreateProjectWizardWidget.tsx:71,159 (SpaarkeAi workspace tab) · surfaceLaunchRegistry.ts:96 (Assistant)
        │  Xrm.Navigation.navigateTo({ pageType:"webresource", webresourceName:"sprk_createprojectwizard" })
        ▼
  src/solutions/CreateProjectWizard/src/main.tsx:9
        import { CreateProjectWizard } from "@spaarke/ui-components/components/CreateProjectWizard"
        ▼
  shared CreateProjectWizard.tsx:859 → CreateRecordWizard → CreateProjectStep.tsx:402 → SecureProjectSection (shared)
```

The LW chain (`WorkspaceGrid` used to render `LazyProjectWizardDialog` pre-UDSS; those imports were removed by UDSS task 009) terminates at nothing: `ProjectWizardDialog` ← nobody; `CreateProjectStep` (LW) ← only `ProjectWizardDialog.tsx:29`; `SecureProjectSection` (LW) ← only LW `CreateProjectStep.tsx:49`.

**Residual caveat (INFERRED, low risk)**: a *deployed* pre-March-2026 `sprk_corporateworkspace` artifact in some environment could theoretically still contain the old inline dialog. The retirement doc says the standalone is no longer deployed; repo-side, the code is unreachable from any current entry point. If the owner wants certainty, check the deployed web-resource inventory in each environment — a repo change cannot settle that.

### B.3 LegalWorkspace per-file liveness (the deletion boundary — VERIFIED twice: census agent + my independent greps)

| LW file | Verdict | Evidence |
|---|---|---|
| `CreateProject/ProjectWizardDialog.tsx` | **DEAD** | zero importers (name + path-fragment + lazy-import greps) |
| `CreateProject/CreateProjectStep.tsx`, `SecureProjectSection.tsx`, `projectService.ts`, `projectFormTypes.ts`, `provisioningService.ts`, `ProvisioningProgressStep.tsx` | **DEAD** (transitively or zero-importer) | only reachable via `ProjectWizardDialog`; `ProvisioningProgressStep` has zero importers outright |
| `CreateProject/CloseProjectDialog.tsx` | **LIVE** | `WorkspaceGrid.tsx:55-56` `React.lazy(() => import("../CreateProject/CloseProjectDialog"))`, rendered `:1034`; also exposed via `window.__SPAARKE_OPEN_CLOSE_PROJECT__` (`:450`) |
| `CreateProject/closureService.ts` | **LIVE** | imported by LW `CloseProjectDialog.tsx:85` (the LOCAL service, not the shared one) |
| `CreateMatter/WizardDialog.tsx`, `CreateRecordStep.tsx`, `NextStepsStep.tsx`, `DraftSummaryStep.tsx`, `AssignCounselStep.tsx`, `AssignResourcesStep.tsx`, `RecipientField.tsx`, `WizardStepper.tsx`, `formTypes.ts`, `index.ts` | **DEAD** | `WizardDialog` reachable only via the unconsumed `CreateMatter/index.ts:10-11` barrel |
| `CreateMatter/matterService.ts` | **LIVE** | `FilePreview/FilePreviewDialog.tsx:18` (`searchUsersAsLookup`); also the dead dialogs import it |
| `CreateMatter/FileUploadZone.tsx`, `UploadedFileList.tsx`, `wizardTypes.ts` | **LIVE** | `Playbook/DocumentUploadStep.tsx:21,22,26` |
| `CreateMatter/LookupField.tsx`, `AiFieldTag.tsx` | **transitively DEAD** | only importers are LW `CreateProjectStep.tsx:47-48` (dead) + dead CreateMatter steps — re-verify at deletion time |
| `CreateEvent/*` (4 files) | **DEAD** | `EventWizardDialog` zero importers; the other 3 only reachable through it |
| `FindSimilar/*` (5 files) | **DEAD (unconsumed adapter)** | thin adapter over shared (`FindSimilarDialog.tsx:11`); no consumer of the folder/barrel |

Note: the three dead LW dialogs import `CreateRecordWizard` via a five-`../` relative path into shared-lib **source** (`ProjectWizardDialog.tsx:25`, `CreateMatter/WizardDialog.tsx:22`, `EventWizardDialog.tsx:15`), bypassing the `@spaarke/ui-components` package boundary — one more sign they froze before the packaging settled.

### B.4 The second duplication pair, running the OTHER way (VERIFIED)

- **`CloseProjectDialog` + `closureService`**: LIVE copy = LegalWorkspace (`WorkspaceGrid.tsx:55/1034`, local `closureService.ts:85`). The **shared** `CreateProjectWizard/CloseProjectDialog.tsx` (barrel `index.ts:49`) has **no consumer found**. The LW file self-documents the arrangement: *"This copy is kept in lockstep with the shared-lib `CreateProjectWizard/CloseProjectDialog.tsx` copy so the two don't drift (spaarke-modal-system CLAUDE.md Key Facts)"* (`CloseProjectDialog.tsx:44-46`). So this pair is **deliberate-but-manual lockstep** — a maintenance tax with a known failure mode, and directly relevant because **task 068 step 4 verifies "CloseProjectDialog copy … (:400)"** without saying which copy: *the live one is LW's*.
- **`ProvisioningProgressStep`**: duplicated in both trees; **zero mounts in either** (shared is barrel-exported but never rendered — `CreateProjectWizard.tsx` imports only `provisionSecureProject` and runs provisioning silently inside `onFinish`; LW's has zero importers). Two copies, no consumers.

---

## (C) Are the two actually the same? The behavioural diff

### C.1 `SecureProjectSection.tsx` ×2 — **functionally identical**

`git diff --no-index -w` (whitespace-insensitive) between the two files yields **only Prettier line-wrapping** (import list wrapped, long strings wrapped, arrow-param parens). Byte-for-byte semantics are equal: same `ISecureProjectSectionProps { isSecure, onSecureChange }`, same Switch/labels, same three `PROVISIONING_ITEMS` (Dedicated Business Unit / SharePoint Embedded Container / External Access Portal "Power Pages"), same permanence `MessageBar`. Line refs differ (shared: Power Pages sentence `:162`, permanence `:244-247` — the exact refs task 068 cites; LW: `:172`, `:259-263`). **Both copies carry the copy defects 068 exists to fix** (stale Power Pages claim; stale permanence claim; stale BU-provisioning claim vs the 2026-08-25 share-only contract).

### C.2 `CreateProjectStep.tsx` ×2 — same form, THREE real divergences (shared strictly ahead)

| Aspect | Shared (`CreateProjectWizard/CreateProjectStep.tsx`) | LW (`CreateProject/CreateProjectStep.tsx`) |
|---|---|---|
| **Position of Secure section within the step** | **TOP** — rendered at `:402`, after the header, BEFORE the form grid (moved by `63aa48683`, WCWE-013/014, 2026-03-23) | **BOTTOM** — rendered at `:495`, after the form grid (the original 2026-03-16 placement) |
| Lookup fields | `DataverseLookupField` with `entityType` + optional side-pane `openLookup` via `navigationService` (`:420-442`) | inline `LookupField` from `../CreateMatter` (`:426-444`) |
| Props contract | context-agnostic per ADR-012: `dataService: IDataService`, `authenticatedFetch?`, `bffBaseUrl?`, `navigationService?` (`:62-93`) | host-coupled: `webApi: IWebApi` + module-level `authenticatedFetch` / `getBffBaseUrl()` imports (`:53-54,:69`) |
| AI pre-fill skip rule | `skipIfInitialized: !!hasInitialValues && uploadedFiles.length === 0` — re-profiles when a hand-off carries a file (W-3 parity, 2026-07-18, `:306`) | `skipIfInitialized: !!hasInitialValues` (`:294`) |
| Identical | validation (projectName required), `/api/workspace/projects/pre-fill`, field extractor + lookup resolvers, grid layout, all field copy | same |

### C.3 Wizard/dialog level — the fork gap is large (shared strictly ahead; LW frozen at 2026-03 semantics)

Shared `CreateProjectWizard.tsx` has, and LW `ProjectWizardDialog.tsx` lacks:
- **Associate To step** (Account/Matter, `:465-475`) + association wiring at finish (N:N matter `$ref` / account bind, `:663-681`);
- **Field Mapping Framework** invocation (uac-r2 task 020 / FR-12: `ProjectService(dataService, authFetch, bffBaseUrl)` + `context.association`, `:560-561`);
- **BU cascade defaults** (`resolveUserBuDefaults`, FR-WIZ-02/G2, `:538-553`);
- Follow-on **Work Assignment via `WorkAssignmentService`** (ADR-024 polymorphic, `:575-623`) and **Event via `EventService`** (`:626-661`) — LW instead stamps `assignedAttorney/Paralegal/OutsideCounsel` follow-on values onto the project record itself (`ProjectWizardDialog.tsx:148-156`), a behaviour the shared copy deliberately replaced;
- **RAG indexing** of uploaded files (`indexUploadedFiles`, `:739-752`);
- **Assistant hand-off** (pre-seed + file leg + `onComplete` honest-ack, `:353-419,:794`);
- **The current provisioning contract** — service-account/share-only, "No business unit or account is created (BFF task 021, 2026-08-25)" (`:683-705`) vs LW's retired `SP-{projectName}` BU model (`:168-184`).

**Nothing functional is in LW-only that the owner would want kept** (VERIFIED by reading both files end-to-end): the LW-only behaviour (stamping assigned resources directly on the project) was superseded, not lost. Verdict: **a fork that drifted strictly one way** — every post-2026-03-19 feature landed only in the shared copy; the LW copy received only mechanical sweeps (`7c8b4108b` modal sizes).

⚠️ One stale-copy nit in the LIVE copy, in scope for 068: the shared success screen still says *"with its Business Unit, document container, and external access account provisioned"* (`CreateProjectWizard.tsx:817`) — contradicting the 2026-08-25 no-BU provisioning it sits next to.

### C.4 The parity gap that actually matters on LIVE paths (new finding)

The Secure toggle leaks into a second live surface **through reuse, with broken behaviour behind it**: `SummarizeFilesWizard`'s create-project follow-on renders the shared `CreateProjectStep` verbatim (`SummarizeCreateProjectStep.tsx:9,43` — no suppression flag exists), so the **Secure Project toggle appears inside Summarize Files** — but that wizard's finish path constructs `new ProjectService(dataService)` with **no `authFetch`/`bffBaseUrl`** (`SummarizeFilesDialog.tsx:516-517`) and **never calls `provisionSecureProject`**. A user can toggle Secure there and get a project that is flagged (or not even flagged — the form's `isSecure` is collected but the finish path does not provision) with **no secure infrastructure and no error**. Under this project's fail-closed philosophy that is a silent-fail on the secure-designation surface. (Same construction also silently no-ops field mapping there.) This is the ONE place where the owner's "same steps including secure" demand binds two LIVE surfaces — and the right fix is behavioural parity (wire provisioning + authFetch through) or explicit suppression of the toggle in that flow, decided by the owner in 093 (§E recommends wiring it).

---

## (D) Recommendation

**CONSOLIDATE. The shared-lib copy survives; the LegalWorkspace wizard chain is deleted (minus its two live files).** Keeping both is not defensible under root CLAUDE.md §11: there is no host constraint requiring two — the only LW host (WorkspaceGrid, embedded in SpaarkeAi) already launches the Code Page that renders the shared copy, and has since UDSS-009 (2026-03-19). "Keep both + enforce parity" would mean maintaining step-order parity, copy parity, provisioning-contract parity, and field-mapping parity in a component that **nothing renders** — pure tax, zero behaviour. The parity branch of the owner's question applies only to (a) the SummarizeFiles leak (§C.4 — real, fix behaviour) and (b) the CloseProjectDialog lockstep pair (§B.4 — collapse to one copy instead of maintaining lockstep by comment).

**What survives**: `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/**` — it is the only mounted implementation, the only one wired to field mapping, BU cascade, Assistant hand-off, and the current (2026-08-25) provisioning contract, and it is where pending tasks 061/068/093 already point.

**What is deleted** (after owner approval; exact boundary in §E step 3): the dead LW files listed in §B.3 — `CreateProject/` minus `CloseProjectDialog.tsx` + `closureService.ts`; the dead `CreateMatter/` dialog chain minus `matterService.ts`, `FileUploadZone.tsx`, `UploadedFileList.tsx`, `wizardTypes.ts`; `CreateEvent/` entirely; `FindSimilar/` entirely (unconsumed adapter). Each file re-verified for zero importers at execution time.

**What breaks**: nothing at runtime (nothing imports the deleted files — that is the definition of the boundary). The risks are:

| Risk | Mitigation |
|---|---|
| A hidden importer (string-built dynamic import, tooling config) | Deletion task runs LW `tsc`/`vite build` + repo-wide grep per deleted basename as acceptance criteria; both greps in this analysis found none |
| The CLAUDE.md §11 anti-pattern trap (":Delete LW CreateRecordStep.tsx as dead code per OC-R4-05" is a listed wrong move) | The deletion PR cites **this analysis** (incomplete UDSS move + per-file importer evidence), NOT the retirement doc; and adds one clarifying line to `LEGALWORKSPACE-RETIREMENT.md` §3 recording that the wizard-dialog chain was removed as UDSS leftovers, distinct from the retained dashboard-engine components |
| A stale deployed `sprk_corporateworkspace` artifact predating UDSS | Environment check (deployed web-resource inventory), outside repo scope; retirement doc says standalone no longer deploys |
| Merge friction with in-flight uac-r2 tasks (068 pending targets `CreateProjectWizard/**`) | Deletion touches only `src/solutions/LegalWorkspace/**` — file-disjoint from 068/061/063 |
| Losing history | Git preserves it; the UDSS project folder + this note record the provenance |

**If the owner instead insists on KEEP BOTH**: the concrete parity bill would be — move LW's Secure section to the top of the step (mirror `63aa48683`), add the Associate To step, port field mapping/BU cascade/follow-on services/hand-off, replace the retired BU-per-project provisioning with the 2026-08-25 contract, and re-apply 068's copy fixes to the second copy forever after. That is ~the whole §C.3 list, recurring on every future change, for a component with no mount. Not recommended; no evidence any consumer needs it.

---

## (E) The reconciliation plan (ordered)

> Numbering note: TASK-INDEX says free numbers start at **093**, with 093/094/095 reserved by the 2026-08-31 076-decomposition. The new deletion task should take the next free number (096 at time of writing — `ls tasks/` before assigning, per the TASK-INDEX rule).

**Step 0 — Owner decision gate (this document).** Approve: (a) consolidate on the shared copy, (b) delete the §B.3 DEAD set, (c) the §C.4 SummarizeFiles fix direction (wire provisioning vs suppress toggle — recommendation: wire), (d) CloseProjectDialog single-copy direction (recommendation: WorkspaceGrid imports the shared copy; delete the LW twin after prop/service parity check). Removing live-adjacent UI is hard to reverse operationally — nothing proceeds without this gate.

**Step 1 — Amend task 068 (small edit, before it executes).** 068 already targets only shared paths (`tasks/068-…poml:27-33`) — correct, keep. Add: (i) an explicit note that `src/solutions/LegalWorkspace/src/components/CreateProject/SecureProjectSection.tsx` is a DEAD twin per this analysis and is out of scope (deleted by the new task) — so nobody "helpfully" mirrors the rework into it; (ii) extend the copy-fix scope to the stale success-screen strings at `CreateProjectWizard.tsx:817` and the secure success body (`:809`), which still claim BU+account provisioning; (iii) in step 4 (CloseProjectDialog copy verify), name **which copy is live** — `LegalWorkspace/src/components/CreateProject/CloseProjectDialog.tsx` (WorkspaceGrid `:55/:1034`) — until Step 4 below collapses the pair, else the verify lands on the unmounted shared file; (iv) 068's acceptance grep "zero occurrences of Power Pages in CreateProjectWizard/**" stays valid, but add a repo-wide grep excluding `dist/` that passes once the deletion task lands (the LW twin carries the same strings at `:172/:259-263`).

**Step 2 — File the deletion task (new number, e.g. 096: "Remove dead LegalWorkspace wizard twins (UDSS move completion)").** Rigor FULL (deletion), `parallel-safe: true` vs 068/061/063 (disjoint paths), no dependency on 061/068/093 — can run any time after owner approval; **preferably BEFORE 068 and 093 execute**, so every subsequent wizard edit has exactly one possible target. Scope (each file re-verified zero-importer at execution):
- DELETE `LegalWorkspace/src/components/CreateProject/`: `ProjectWizardDialog.tsx`, `CreateProjectStep.tsx`, `SecureProjectSection.tsx`, `ProvisioningProgressStep.tsx`, `provisioningService.ts`, `projectService.ts`, `projectFormTypes.ts` · KEEP `CloseProjectDialog.tsx`, `closureService.ts` (live — WorkspaceGrid `:55/:1034`, `:85`)
- DELETE `LegalWorkspace/src/components/CreateEvent/` (all 4 files) and `LegalWorkspace/src/components/FindSimilar/` (all 5)
- DELETE from `LegalWorkspace/src/components/CreateMatter/`: `WizardDialog.tsx`, `CreateRecordStep.tsx`, `NextStepsStep.tsx`, `DraftSummaryStep.tsx`, `AssignCounselStep.tsx`, `AssignResourcesStep.tsx`, `RecipientField.tsx`, `WizardStepper.tsx`, `formTypes.ts`, `index.ts`, and (if still zero-importer after the above) `LookupField.tsx`, `AiFieldTag.tsx` · KEEP `matterService.ts`, `FileUploadZone.tsx`, `UploadedFileList.tsx`, `wizardTypes.ts` (live per §B.3)
- Also DELETE the **shared** `CreateProjectWizard/ProvisioningProgressStep.tsx` + its barrel export (`index.ts:43-48`) — zero mounts in either tree (§B.4) — unless 068/093 decide to resurrect a visible provisioning step (check with their author first; if resurrection is plausible, keep and note it)
- Acceptance: LW build green (`vite build` + `tsc`); SpaarkeAi build green; repo-wide grep per deleted basename = 0 (excluding `dist/`, `projects/`, `docs/`); one-line addition to `LEGALWORKSPACE-RETIREMENT.md` §3 recording the removal rationale (UDSS leftovers, per this analysis — explicitly NOT "per OC-R4-05")

**Step 3 — Amend/author 093 with the resolved answer.** The 2026-09-01 correction in `notes/plan-upload-path-decomposition-2026-08-31.md` §3 left "two copies" as an open question — this document closes it; 093's POML should cite this note and state: the SHARED components are the only edit target for all 7 wizards. 093's scope addition from §C.4: when moving IsSecure collection before the Info step, also fix the SummarizeFiles create-project follow-on — either plumb `authFetch`/`bffBaseUrl` + `provisionSecureProject` through `SummarizeFilesDialog.tsx:516` so a Secure toggle there behaves identically, or pass a new `hideSecureSection` prop; owner picks (recommendation: plumb it — same component, same behaviour, no new surface). Reminder from the same note: provisioning still runs **after** record creation (task 008: provisioning's final act is an `UpdateAsync` on the project) — 093 moves *collection*, not *action*.

**Step 4 — Collapse the CloseProjectDialog lockstep pair (small follow-up task or fold into 096).** Diff LW `CloseProjectDialog/closureService` against the shared twins; if the shared pair is current (the LW header `:44-46` claims lockstep), repoint `WorkspaceGrid.tsx:55-56` to `@spaarke/ui-components` and delete the LW pair; if they have drifted, reconcile first. This ends the "kept in lockstep by comment" arrangement. (Not urgent; do after 068 so 068's copy verdict lands once, in whichever file survives.)

**Step 5 — Doc-drift fixes (main session or MINIMAL task).** Update `docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md:353-356` (inventory still lists LW `ProjectWizardDialog.tsx` as current) and `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md:139-145` (worked example cites the LW `provisioningService.ts` — repoint to the shared `CreateProjectWizard/provisioningService.ts`). Optionally file the systemic launcher-duplication observation (§B census: 5 of 7 `wizardLaunchers.ts` exports have no production caller; WorkspaceGrid/ribbon/widgets each hand-roll `navigateTo`) into `projects/_backlog/needs-a-project.md` item 1, which already exists for the follow-on-actions consolidation.

**Own task vs fold into 093?** — **Own task (096), not folded.** Reasons: (1) 093 is already large (reorder across 7 wizards + Secure UI move + upload-path integration) and is **blocked on 076**, while the deletion is blocked only on owner approval — folding would needlessly serialize dead-code removal behind the container-contract cutover; (2) the deletion is pure `src/solutions/LegalWorkspace/**`, file-disjoint from 093's shared-lib edits, so parallel-safe; (3) landing 096 FIRST makes 068 and 093 unambiguous ("the wrong copy" ceases to exist), which is the cheapest possible guard against the exact wrong-component-edit failure mode the owner is worried about. Sequence: **096 → 068 (after 061) → 093 (after 076), with Step 4/5 trailing.**

---

## Appendix — evidence index (for re-verification)

- Fork commits: `29f547c18` (2026-03-04) · `21567b3da` (2026-03-16) · `d474e46df` (2026-03-19, fork) · `63aa48683` (2026-03-23, Secure→top, shared only) · `0998a18b7` (2026-07-18) · `7c8b4108b` (2026-08-02, LW mechanical)
- Live mount chain: `src/solutions/CreateProjectWizard/src/main.tsx:9` · `WorkspaceGrid.tsx:312-327` · `getStarted.registration.ts:65` · `sprk_wizard_commands.js:212,298` · `spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml:140` · `Spaarke.AI.Widgets/.../CreateProjectWizardWidget.tsx:71,159` · `SpaarkeAi surfaceLaunchRegistry.ts:96`
- Dead-chain proof: zero grep hits for `ProjectWizardDialog` imports outside its own file; LW `CreateProjectStep` imported only at `ProjectWizardDialog.tsx:29`; LW `SecureProjectSection` imported only at LW `CreateProjectStep.tsx:49`
- UDSS record: `projects/x-ui-dialog-shell-standardization/{design.md:189,194,275,315; spec.md:90; plan.md:108; tasks/007:28,34-39; tasks/009:25; tasks/025}`
- Architecture silence: `LEGALWORKSPACE-RETIREMENT.md:5,23,31,60-68,111` · `SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md:35,128,412` · `SPAARKEAI-COMPONENTIZATION-AUDIT.md:264-278` · `SPAARKEAI-COMPONENT-MODEL.md:63,169-176` · `BUILD-A-NEW-WORKSPACE-WIDGET.md:70-71,452,459,885` · `projects/_backlog/needs-a-project.md:582,665-677`
- SummarizeFiles secure leak: `SummarizeFilesWizard/SummarizeCreateProjectStep.tsx:9,43` · `SummarizeFilesDialog.tsx:408,415,516-517`
- CloseProjectDialog lockstep: LW `CloseProjectDialog.tsx:44-46,85` · shared barrel `CreateProjectWizard/index.ts:49` · `WorkspaceGrid.tsx:55-56,434,450,1034`
- Task interplay: `tasks/068-create-project-wizard-secure-step.poml:21-33,63` · `notes/plan-upload-path-decomposition-2026-08-31.md:48-85,147-162` · `tasks/TASK-INDEX.md:17,41-53,621,663`
