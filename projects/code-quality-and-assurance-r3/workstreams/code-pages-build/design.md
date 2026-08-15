# Code Page Solutions (35) + Build/Config Sprawl — Cleanup & Remediation Design

> **Surface**: Code Page solutions (`src/solutions/*`, 30 npm/Vite roots + 5 non-package dirs = the 35-solution surface) **+ build/config sprawl** (per-solution package/lock/vite configs, CI coverage, install reproducibility)
> **Status**: DRAFT — for owner review before `/design-to-spec` (a **surface workstream** of `code-quality-and-assurance-r3`, not a standalone project)
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: **11-dimension fan-out + Fable adversarial verification** (r3 spec NFR-05). Every finding below survived the mandatory adversarial-verification pass; refuted claims are listed record-only in §4 and MUST NOT drive remediation.
> **Read-only statement**: This assessment modified **no code**. Its sole outputs are this design and the SCORECARD row inputs (§8). Remediation is operator-gated and task-created separately.
> **Program**: Executes in the r3 worktree on `work/code-quality-and-assurance-r3` as small PRs (`/conflict-check` each). Model: [`workstreams/bff-api/design.md`](../bff-api/design.md).

---

## 0. Summary & verdict

The Code Page surface is **functionally live and defensively coded at its seams, but structurally unmanaged**. The good news is real: every page authenticates via `@spaarke/auth` (ADR-028), OData injection boundaries are consistently escaped, no page calls LLM internals directly (ADR-013 honored), the 7 `Create*Wizard` pages that consume the shared wizard library do so correctly as thin shells, and **no shipped broken data path was found anywhere on the surface**. That is why the two gating dimensions grade B+ (D2) and A- (D3).

Everything around that spine has sprawled. The surface carries **27 God components over the 800-LOC line** (worst: `ConversationPane.tsx` at 3,444 LOC), **four orphaned diverged wizard forks** plus a live 1,800-LOC SmartToDo fork inside LegalWorkspace, **HIGH CVEs open in all 30 solutions** including runtime-shipped `pdfjs-dist` (arbitrary-JS-on-malicious-PDF, reachable from user-attached chat PDFs), **zero PR-blocking build/typecheck/lint gates for all 30 solutions**, installs that bypass all 30 committed lockfiles, **489 unstructured console calls** (including full record payloads logged verbatim on a live save path), effectively **1-of-35 live telemetry**, three test files that can never execute, and reference docs that describe a deleted code page as "canonical" and mandate the wrong build tool.

**Composed surface grade: C** (mean 1.98 of 11 equal-weight dimensions → C; gating cap `min(C, D2=B+, D3=A-)` = **C**, cap not binding).

| Dimension | First-pass | **Final (re-adjudicated)** | Movement / note |
|---|---|---|---|
| D1 Architecture & boundaries | C+ | **C+** | All 5 findings confirmed; God-component sprawl vs intact ADR-013 spine |
| D2 Correctness & reliability (gating) | B+ | **B+** | Own findings weakened to INFO+LOW (dead hook); live cross-filed D4-02 poller bug holds it at B+ |
| D3 Security (gating) | B+ | **A-** | **Moved UP** — D3-02 raw-Bearer claim REFUTED (sanctioned D-AUTH-7 exception); one LOW survivor |
| D4 Performance & scalability | C+ | **C+** | All 4 confirmed (count-by-fetch, infinite poll, no bundle budget, unbounded read) |
| D5 DRY / dead code | D+ | **D+** | D5-03 downgraded HIGH→LOW (137-LOC shims), but 4 HIGH forks remain |
| D6 Consistency & conventions | C+ | **C+** | All 8 confirmed; D6-02 strengthened (parity claim false for 2 of 3 named siblings) |
| D7 Testability & test quality | D+ | **D+** | Confirmed + strengthened (SpeAdminApp/Reporting have no runner at all; D7-03 blocker now stale) |
| D8 Dependency & supply-chain | D | **D** | D8-01 strengthened (pdfjs-dist runtime-reachable on attacker-suppliable PDFs) — F-borderline; remediate first |
| D9 Observability | D+ | **D+** | Strengthened: SpaarkeAi telemetry never wired in prod → live telemetry ≈ 1/35 |
| D10 ALM / build hygiene | D+ | **D+** | Strengthened: effectively 30/30 solutions ungated (client CI job is continue-on-error) |
| D11 Knowledge/doc accuracy | C- | **C-** | All 5 confirmed (deleted page documented as canonical; webpack-vs-Vite mandate) |

**Surface grade: C** · gating cap **not** applied (mean already below both gates).

Highest-leverage first moves (tranche A, §6): bump `pdfjs-dist` + `npm audit fix` (D8-01), stop logging save payloads (D9-01), fix the infinite poller (D4-02), fix the count-by-fetch (D4-01), delete the four verified-orphan LegalWorkspace wizard clusters (~1,660 LOC, D5-01/02/03/04), and correct the five reference-doc drifts (D11).

---

## 1. Problem statement

The 30 Vite/React code-page solutions grew one project at a time, each cloning a sibling's scaffold (package.json, vite config, authInit, bootstrap) and drifting independently. Nothing gates them: blocking CI builds only the BFF, the client-quality CI job is `continue-on-error`, Prettier explicitly ignores `src/solutions/`, `npm ci` is broken on ~14/16 so every install floats past the 30 committed lockfiles, and no workspace/hoisting layer exists across 80 npm roots. Inside the pages, the same clone-and-drift dynamic produced 27 God components, four orphaned wizard forks, a live SmartToDo fork, three package-naming conventions, and 489 ad-hoc console calls standing in for observability. The reference docs for the surface predate the Vite generation entirely.

**Evidence base**: 11-dimension read-only fan-out + Fable adversarial verification. The verification pass refuted 1 claim (D3-02 — record-only, §4), downgraded 2 severities (D2-01→INFO unreachable; D5-03→LOW), corrected 2 remediation traps that would have broken the live SpaarkeAi build (D5-05/D1-05: LegalWorkspace `src` is deliberately retained and consumed by SpaarkeAi's vite build — see §3.5 KEEPs), and strengthened several findings (D7-02/05, D9-03, D10-02, D7-03).

---

## 2. Goals

- **G1** — Close the supply-chain exposure: `pdfjs-dist ≥6.2.108` in the SprkChat chain + the two shipping pages; `npm audit fix` the build-time HIGHs across all 30 solutions; add an audit gate (D8-01).
- **G2** — Fix the live behavior bugs: infinite post-completion poller (D4-02), count-by-fetch with capped counts (D4-01), unbounded active-todos read (D4-04).
- **G3** — Stop the observability leaks: remove payload/PII console dumps (D9-01/05), kill the hardcoded App Insights fallback key (D9-04); then introduce one shared logger + telemetry boundary (D9-02/03/08).
- **G4** — Delete the verified-orphan LegalWorkspace wizard clusters (~1,660 LOC across CreateEvent/CreateProject/CreateMatter/FindSimilar) with the verification pass's survivor corrections honored (D5-01..04).
- **G5** — Unify the fork: SmartToDo into one shared implementation consumed by both SmartTodo and LegalWorkspace (D5-05); consolidate the 8 drifted `authInit.ts` copies (D5-07) and the 4 hand-inlined wizard bootstraps (D6-01).
- **G6** — Put the surface under CI: PR-blocking path-filtered `tsc --noEmit` + `vite build` per changed solution (D10-02), promote lint/format to blocking (D10-03), fix lockfiles so `npm ci` works (D8-03/D10-04), then decide the workspace/monorepo question (D10-06).
- **G7** — Make the tests real: wire runners where files exist but can't run (D7-01), author mutation-path tests for SpeAdminApp (D7-02), un-skip the now-unblocked placeholders (D7-03/04), fix stale headers (D7-06).
- **G8** — Shrink the God components: budget + decompose, prioritizing the 6 files >1,000 LOC and the three 2,000+ monsters (D1-01..04).
- **G9** — Fix the reference docs: AnalysisWorkspace, webpack-vs-Vite, counts, React version, CommunicationPage, stale anchors (D11-01..05).
- **G10** — Normalize conventions: package names, folder names, log tags, import paths, titles, quote rule (D6-02..08).

### Non-goals

- **NG1** — Deleting or relocating `src/solutions/LegalWorkspace/src`. The retirement is **deploy-only**; the tree is deliberately retained as the library the live SpaarkeAi build consumes (facade re-export + vite include). Only the *standalone entry* (App.tsx/main.tsx/build target) is a candidate, and only after the Dataverse pre-check (§5).
- **NG2** — Merging the intentional Calendar variants, the two live `.eml` builders (BFF), or any deliberately-divergent shared-lib pattern.
- **NG3** — A big-bang monorepo migration. Workspace adoption (D10-06/D8-06/D10-08) is a tranche-B decision with its own task and rollback plan, not a side effect of tranche A.
- **NG4** — Editing `.github/workflows` unilaterally. `ci-cd-unit-test-remediation-r1` owns workflow edits — CI-gate tasks (G6) are coordinated with that owner.
- **NG5** — Rewriting shared client libs (`@spaarke/auth`, `Spaarke.UI.Components`) beyond the minimal seams named here (correlation header, `useWizardPageBootstrap` extension, pdfjs bump). Those libs are the shared-client-libs workstream's surface.

---

## 3. Current-state inventory (verified findings)

Every row survived Fable adversarial verification. Effort: S (≤½ day) / M (≤3 days) / L (>3 days). Risk = chance of breaking live behavior or colliding with active worktrees (8 of 17 active worktrees touch SpaarkeAi — see `projects/INDEX.md`).

### 3.1 D1 — Architecture & boundaries (final: **C+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D1-01 | HIGH | `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx:1` — 3,444 LOC, 134 hook tokens (81 strict invocations), constants+helpers+mega-component in one file; live (imported by ThreePaneShell.tsx, ~30 co-located tests) | 3,444 | L | **high** | Extract pure helpers/constants (e.g. `COMPOSE_EDIT_CONFIRMATION` @205, `WIZARD_AUTO_RUN_BRIDGE_FAILURE_MESSAGE` @292) and compose/wizard-routing sub-flows into modules + child components; target <~400 LOC. **Tranche B** (SpaarkeAi contention) |
| D1-02 | HIGH | `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx:192` — 2,631 LOC; exported pure derivations (`tabAnchorKeyForContext` @192, `deriveComposeInstanceKey` @245, `deriveActiveItemHandle` @293) co-hosted with mega-component; live via ThreePaneShell.tsx:75 | 2,631 | L | **high** | Move the exported derive*/anchor functions into a workspace-model unit beside `WorkspaceTabManager.ts` (860 LOC); split the component. **Tranche B** |
| D1-03 | MED | `src/solutions/WorkspaceLayoutWizard/src/steps/ArrangeStep.tsx:1539` — 2,071 LOC; ~1,275 lines of `buildInitialAssignments` algorithm (@264) precede the view (@1539); live via App.tsx:37/:855 | 2,071 | M | med | Extract the assignment/placement algorithm into a pure exported module with unit tests; leave ArrangeStep.tsx as the view. Pairs with D7-03 (same testability seam) |
| D1-04 | MED | 27 non-test components >800 LOC across 6 solutions (census reproduced exactly); anchor `SpaarkeAi/.../HistoryOverlay.tsx` 1,483; also ManageWorkspacesPane 1,398, ContainerTypeConfig 1,300, ContainersPage 1,079, SmartToDo 1,059, FileBrowserPage 1,046, EnvironmentConfig 1,022, ContextPaneController 962, NextStepsStep 955, ThreePaneShell 954, EventDetailSidePane/App 813 … | ~12,000 | L | med | Adopt a component-size budget for code pages; decompose per-solution, prioritizing the 6 files >1,000 LOC. **Tranche B**, incremental |
| D1-05 | MED | `src/solutions/LegalWorkspace/src/components/CreateEvent/EventWizardDialog.tsx:15` +12 more — 13 deep relative `../../../../../client/shared/Spaarke.UI.Components/src/...` imports, all in LegalWorkspace; every other solution uses the `@spaarke/ui-components` alias | 178 files (residual tree) | S | low | Route the 13 imports through the `@spaarke/ui-components` alias. ⚠ Do **NOT** delete the tree (NG1 — verification: SpaarkeAi's build consumes it; facade at `Spaarke.LegalWorkspace/src/index.ts:88`, vite include `SpaarkeAi/vite.config.ts:64-66,150-157`) |

### 3.2 D2 — Correctness & reliability (gating; final: **B+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D2-01 | INFO | `src/solutions/SmartTodo/src/hooks/useTodoScoring.ts:241` — mock-fallback setTimeout + BFF fetch with no unmount cleanup (no useEffect exists in the file); double-open orphans a stale timer. **Downgraded to INFO by verification: the hook has ZERO call sites** (type-only imports; LazyTodoAISummaryDialog exported but never rendered; LegalWorkspace twin already deleted as unused) | 12 | S | low | Prefer **deleting the dead hook** (as LegalWorkspace did per `todoScoringTypes.ts:6`); if wired instead, add the teardown useEffect + clear pending timer at fetch start. Fold into D5 dead-code sweep |
| D2-02 | LOW | `useTodoScoring.ts:280` — `error` state only ever set to null; BFF failures silently substituted with `mockScoringResult` (`isMockData:true`); the dialog's error/retry branch (`TodoAISummaryDialog.tsx:438-455`) is permanently unreachable | 15 | S | low | Same disposition as D2-01: delete with the hook, or surface real errors + honor retry, or remove the dead `error`/`retry` contract members if NFR-06 mock-masking is intended terminal behavior |

**D2 grade note**: the surface's one *live* correctness bug is D4-02 (filed under D4, cross-flagged D2) — the stale-closure poller that re-fires `onComplete` every 2 s post-completion. That live-but-non-blocking defect is what holds D2 at B+ rather than A-. No shipped broken data path exists.

### 3.3 D3 — Security (gating; final: **A-**, moved up)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D3-01 | LOW | Four bootstrap catch-handlers assign interpolated `err.message` into `element.innerHTML` (DOM-XSS sink; inline event handlers execute): `SpaarkeAi/src/main.tsx:717`, `sprk_communicationconversationpage/src/main.tsx:84`, `LegalWorkspace/src/main.tsx:113`, `Reporting/src/main.tsx:85`. Verified the only production `innerHTML =` sites in src/solutions; exploitability low (config-derived privileged input, bootstrap-failure-only render) | 24 | S | low | Static markup + `textContent`/`createTextNode` for the message portion at all four sites. **Tranche A** |

**D3 grade note**: moved **up** from first-pass B+ because the second D3 claim (raw-Bearer literal) was **refuted** — see §4. Positives verified: `@spaarke/auth` on every data path (ADR-028), OData single-quote escaping + `encodeURIComponent` at every user-input filter site sampled, no secrets in tracked config, no tokens in web storage. The committed App Insights key (D9-04) is an observability-config defect, not an auth secret; the pdfjs-dist CVE is owned by D8.

### 3.4 D4 — Performance & scalability (final: **C+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D4-01 | HIGH | `src/solutions/LegalWorkspace/src/hooks/useQuickSummaryCounts.ts:50` — 6 cards × ($top=500 main + $top=100 badge) = up to **3,600 rows fetched per mount** to read `.entities.length`; counts silently capped at min(actual, $top). **Live on the SpaarkeAi workspace home** (liveness chain re-traced end-to-end through the vite include) | 45 | M | med | Replace per-card row fetches with scalar counts (FetchXML aggregate `count`, or OData `$count=true&$top=0`) — accurate + ~zero transfer. **Tranche A** (single-hook change) |
| D4-02 | HIGH | `src/solutions/SpeAdminApp/src/components/bulk/BulkOperationProgress.tsx:191` — setInterval guard closes over `status` from mount (always null) → **polls the status endpoint forever after completion** and re-fires `onComplete` every 2 s; parent (`ContainerResultsGrid.tsx:602-608`) keeps it mounted and re-runs `handleBulkComplete` each tick. Docstring claims the opposite (cross-dim D11/D2) | 12 | S | low | Track finished state in a ref (or `clearInterval` inside `pollOnce` when `result.isFinished`); fix the docstring. **Tranche A** — the surface's one live correctness bug |
| D4-03 | MED | `src/solutions/SpaarkeAi/vite.config.ts:279` (pattern ×30) — every page is `viteSingleFile()` + `assetsInlineLimit:1e8` + `manualChunks:undefined`; **no size budget or gate anywhere** (size-limit/bundlewatch/chunkSizeWarningLimit = 0 hits; nightly job measures 1/30, advisory-only; deploy workflow logs size but enforces nothing). Single-file output itself is an ADR-026 KEEP | 0 | M | low | Per-solution byte-size budget on the dist HTML (CI assertion or size-limit step) with baselines + drift thresholds. Coordinate with NG4 owner for the workflow wiring |
| D4-04 | LOW | `src/solutions/SmartTodo/src/services/queryHelpers.ts:584` — primary active-todos query has no `$top` and `retrieveMultipleRecords` passes no maxPageSize (`DataverseService.ts:447`), unlike the sibling builders (dismissed top:20, matters/projects top:5); live via `useTodoItems.ts:167,222` | 6 | S | low | Add an explicit `top` to `buildTodoItemsQuery` consistent with the sibling builders. **Tranche A** |

### 3.5 D5 — DRY / dead code (final: **D+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D5-01 | HIGH | `LegalWorkspace/src/components/CreateEvent/` (EventWizardDialog 200, CreateEventStep 222, eventService 167, formTypes 34 = 623 LOC) — zero inbound refs (repo-wide re-grep), not in barrel, shell opens `sprk_createeventwizard` via navigateTo; diverged fork of shared CreateEventWizard (386/433 lines differ) | 623 | S | low | **Delete the directory.** Dataverse dispatches by web-resource name, not component — no live-config exposure. **Tranche A** |
| D5-02 | HIGH | `LegalWorkspace/src/components/CreateProject/` wizard-entry files (ProjectWizardDialog, CreateProjectStep, ProvisioningProgressStep, projectService, provisioningService, SecureProjectSection) — zero inbound; shell uses `sprk_createprojectwizard` navigateTo (WorkspaceGrid.tsx:322-327). **Retain** `CloseProjectDialog.tsx` + `closureService.ts` (live: WorkspaceGrid.tsx:55-56/:1034; verified self-contained) | ~700 | S | low | Delete the six wizard-entry files; keep the retained pair. Sequence note: `CreateProjectStep.tsx:47-48,52` is the only consumer of CreateMatter's `LookupField`/`AiFieldTag` — delete before/with D5-04. **Tranche A** |
| D5-03 | LOW | `LegalWorkspace/src/components/FindSimilar/` — zero inbound; **verification correction: these are 137 LOC of thin shims/re-exports over `@spaarke/ui-components` FindSimilar** (not a diverged local impl; severity downgraded from HIGH). Live path uses shared `FindSimilarViewerDialog` (DocumentCard.tsx:31/:329) + navigateTo (WorkspaceGrid.tsx:364/:656) | 137 | S | low | Delete the directory. **Tranche A** |
| D5-04 | HIGH | `LegalWorkspace/src/components/CreateMatter/` — wizard-entry files zero-inbound (WizardDialog, CreateRecordStep, AssignCounselStep, AssignResourcesStep, DraftSummaryStep, WizardStepper, RecipientField, NextStepsStep); `matterService.ts` is a diverged fork (666 vs 781 LOC shared, 427 diff lines). **Live survivors (retain/re-home)**: `matterService.searchUsersAsLookup` (FilePreviewDialog.tsx:18), `FileUploadZone`/`UploadedFileList` (Playbook/DocumentUploadStep.tsx:21-22), **and `wizardTypes.ts`** (DocumentUploadStep.tsx:26 — verification correction: NOT zero-inbound). `AiFieldTag`/`LookupField` are transitively dead (only consumer is D5-02's dead CreateProjectStep) | ~900 | M | med | Delete the orphaned wizard-entry files; re-home the four live survivors (searchUsersAsLookup, FileUploadZone, UploadedFileList, wizardTypes) toward the shared lib so the diverged matterService fork is eliminated. **Tranche A** (after D5-02) |
| D5-05 | HIGH | SmartToDo forked across solutions: `SmartTodo/src/components/SmartToDo.tsx` 1,059 vs `LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` 805 (520 diff lines); sibling dup set (useTodoItems 258 vs 233 / 91 diff, TodoAISummaryDialog, PriorityScoreCard, ThresholdSettings, useUserPreferences…). **Both live** — LegalWorkspace's copy renders on the retained embedded surface (workspaceConfig.tsx:328) + SmartToDoDialog (WorkspaceGrid.tsx:66-67) | ~1,800 | L | **high** | Extract SmartToDo + hooks into a shared package (`Spaarke.SmartTodo.Components` exists as a candidate home) and consume from both. Fold D2-01/D2-02 dead-hook deletion into the extraction. **Tranche B** (both copies live, active-worktree contention) |
| D5-06 | MED | `LegalWorkspace/src/App.tsx:1` (+ main.tsx/index.html/.env.production/build target) — the deploy-retired **standalone** `sprk_corporateworkspace` shell, retained building-but-undeployed by design (LEGALWORKSPACE-RETIREMENT.md, OC-R4-05); residual deploy surface (cross-dim D10). **requiresDataverseCheck** | ~200 | M | med | Gate on retirement-doc follow-up #3 (web-resource deletion) + the §5 live dependency check; then remove the standalone entry + vite build target, keeping only the LegalWorkspaceApp library export. **Tranche B** |
| D5-07 | LOW | `services/authInit.ts` copied in 8 solutions (CommunicationReconciliation, DailyBriefing, EmailPage, LegalWorkspace, Reporting, SpaarkeAi, SpeAdminApp, sprk_communicationconversationpage) with drift (65 vs 76 LOC, 61 diff lines); canonical home exists (`useWizardPageBootstrap`) | ~560 | M | med | Factor a shared code-page auth bootstrap (extend `useWizardPageBootstrap` or new `useCodePageAuth` in `@spaarke/ui-components`); reduce each copy to app-specific config. Copies are load-bearing (e.g. closureService.ts:16) — consolidate, don't delete. **Tranche B**; pairs with D6-01 |

**Explicit KEEPs (verification-mandated — do NOT "fix"):**
- **`src/solutions/LegalWorkspace/src` stays.** The retirement is deploy-only ("This is a deploy retirement, not a code deletion"); `Spaarke.LegalWorkspace/src/index.ts:88` facade re-exports it and the live SpaarkeAi build includes it (`SpaarkeAi/vite.config.ts:64-66,150-157`; tsconfig alias). Deleting it breaks the live SpaarkeAi code page. Only the D5-01..04 *orphan clusters within it* (per-file verified) and, post-pre-check, the D5-06 standalone entry are removable.
- **`CloseProjectDialog.tsx` + `closureService.ts`** — live and self-contained (D5-02 verification).
- **`wizardTypes.ts`** in CreateMatter — live via DocumentUploadStep.tsx:26 (D5-04 verification correction).
- **The 3 hook-based Create*Wizard main.tsx** (Event/Invoice/ReportCard) are the reference pattern (D6-01) — migrate the other 4 *to* them.
- **Single-file vite output** (viteSingleFile + inline) is an ADR-026 KEEP (D4-03) — the gap is the missing budget, not the pattern.

### 3.6 D6 — Consistency & conventions (final: **C+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D6-01 | HIGH | Create*Wizard family split: 3 consume `useWizardPageBootstrap` (~50-line main.tsx: CreateEvent/Invoice/ReportCard @ src/main.tsx:8) vs 4 hand-inline the identical ~100-line bootstrap (CreateProjectWizard main.tsx:14-106, CreateMatterWizard :14-117, CreateTodoWizard, CreateWorkAssignmentWizard). Hook lacks `resolveUserBuDefaults` (verified), so migration requires a hook extension | ~400 | M | med | Extend `useWizardPageBootstrap` (expose resolveUserBuDefaults / initialFileRefs wiring), migrate the 4 inline siblings; all 7 share one structure. **Tranche B** (shared-lib seam) |
| D6-02 | MED | `CreateMatterWizard/src/main.tsx:107-108` — comment claims wiring "Matches CreateProjectWizard / CreateEventWizard / CreateWorkAssignmentWizard … exactly"; false for **two** of three named siblings (CreateEventWizard is hook-based; CreateWorkAssignmentWizard lacks resolveUserBuDefaults) | 2 | S | low | Correct the comment to name only CreateProjectWizard (or delete it; moot after D6-01). **Tranche A** |
| D6-03 | MED | sprk_ page family log-tag drift: `[CommunicationConversationPage]` (PascalCase, main.tsx:68) vs `[sprk_communicationspage]`/`[sprk_invoicespage]`/`[sprk_kpiassessmentspage]` (raw schema names) | 4 | S | low | Pick one tag convention for the family; apply at the 4 sites. **Tranche A** |
| D6-04 | MED | `package.json` name field uses 3 conventions across 30 solutions: bare kebab (majority), 6× `sprk-` prefix, 1× npm-scoped `@spaarke/document-upload-wizard`; plus FindSimilarCodePage folder vs `find-similar-dialog` name | 30 | S | low | Adopt one convention (bare kebab matching folder recommended); align the 7 outliers. **Tranche A** |
| D6-05 | LOW | 4 `sprk_` snake_case solution folders among ~26 PascalCase peers; not schema-forced (PascalCase folders also deploy as `sprk_` web resources per code-page-deploy SKILL.md:185) | 0 | M | med | Rename the 4 folders to PascalCase, keeping web-resource output names unchanged; **must update deploy-script folder registries** (scripts/Deploy-*.ps1). **Tranche B** (touches deploy tooling) |
| D6-06 | LOW | Theme utils imported via barrel (`@spaarke/ui-components`) in 3 pages vs deep subpath (`.../utils/themeStorage`) in 3 wizards — same two symbols, two styles | 6 | S | low | Standardize on the subpath import at the 3 barrel sites. **Tranche A** |
| D6-07 | LOW | Create*Wizard `<title>` drift: 4 say "Create New X", 3 say "Create X" | 7 | S | low | Standardize (recommend "Create {Entity}"). **Tranche A** |
| D6-08 | LOW | Quote-style drift across entry files; **root cause verified: `.prettierignore` excludes all of `src/solutions/` (over-broad "Dataverse solution XML" ignore) and lint-staged ESLint covers only src/client/pcf** — no formatter reaches these files | 3 | M | med | Un-ignore `src/solutions/**/*.{ts,tsx}` in `.prettierignore` **after** deciding the quote rule (root `singleQuote:true` would flip the double-quote majority) — one intentional format commit. **Tranche B** (wide mechanical diff; quiet window) |

### 3.7 D7 — Testability & test quality (final: **D+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D7-01 | HIGH | LegalWorkspace ships 3 test files that can never execute — no test script, no jest/vitest devDeps, no config, no root runner picks them up; each self-documents "does not yet have a test runner configured" (`FeedTodoSyncContext.test.tsx:10`, `BrowsePlaybooksCard.test.tsx:25` — while claiming ADR-038 KEEP/MAINTAIN, `widthPreferenceGuard.test.ts:18`). False coverage signal | 3 files | S | low | Wire a runner into the package (test script + devDeps + config), or relocate the contract tests to a package with a runner (FeedTodoSyncContext's own header suggests `@spaarke/ui-components`). **Tranche A** |
| D7-02 | HIGH | SpeAdminApp: 41 non-test logic files, **0 tests, and (verification correction) NO runner at all** (no test script, no jest config) — while `speApiClient.ts` issues live POST(:79/:93/:142)/PUT(:106)/PATCH(:120)/DELETE(:133) admin mutations consumed by 24 files incl. the destructive RecycleBinPage | 0 | M | med | Wire a runner **and** author behavior tests for the mutation methods against a fetch double at the module boundary; prioritize DELETE/restore. **Tranche A/B boundary** (new tests are additive/low-contention) |
| D7-03 | MED | `WorkspaceLayoutWizard/src/__tests__/rowHeight.test.tsx:268` — `describe.skip` wrapping 3 assertion-free placeholder its (+ `void within;` lint-silencer). **Verification: the stated blocker is stale — `buildSectionsJson` IS now exported (App.tsx:369)**, so the placeholders can be wired immediately; the JSON they'd verify is the live LayoutJson persisted to Dataverse | 22 | S | low | Replace the 3 placeholders with real assertions on the emitted JSON (80vh emits rowHeight; empty map omits; 640px round-trips) and un-skip. **Tranche A** |
| D7-04 | MED | `SmartTodo/.../ToolbarActions.test.ts:281` — `it.skip` on the only assertion of the live mailto-compose behavior (ToolbarActions.ts:294/:297 assigns `window.location.href`); jsdom-v22 rationale defers to an injectable seam | 30 | S | low | Add the injectable navigation seam (inject setHref/navigate into createToolbarActions); un-skip. **Tranche A** |
| D7-05 | LOW | Reporting: 24 logic files, 0 tests, **and (verification correction) no runner** — same class as SpeAdminApp/LegalWorkspace, not "runner-present" as first claimed. Breadth context: 27 of 35 solutions have zero test files | 0 | M | med | Wire a runner + author behavior tests for data-shaping/aggregation branches (calculation paths first, echoing the BFF invoice-totals regression class). **Tranche B** |
| D7-06 | LOW | `rowHeight.test.tsx:23-25` (line corrected by verification; not 36-40) — header claims no jest config/test script/devDeps; all three now false (jest.config.ts wired by DEF-001; `"test":"jest"`; full devDeps). Doc-drift (cross-dim D11) | 5 | S | low | Update/remove the stale note (and the lines 261-265 unexported-function claim) when D7-03 lands. **Tranche A** |

### 3.8 D8 — Dependency & supply-chain hygiene (final: **D**; F-borderline — remediate first)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D8-01 | HIGH | HIGH CVEs open in **all 30 solutions** (offline `npm audit --package-lock-only`; counts reproduced: SpaarkeAi 6, Notepad/SmartTodo/LegalWorkspace 5, DailyBriefing 4…; 0 critical). Runtime-shipped: **pdfjs-dist 5.7.284** (advisory: arbitrary JS execution on malicious PDF; vulnerable <6.2.108) in prod deps of `SpaarkeAi/package.json:77` + `DailyBriefing/package.json:40`, **dynamic-imported at runtime** by SprkChat `useChatFileAttachment.ts:336` to parse user-attached PDFs. This matches the rubric's F example ("HIGH CVE in a deployed dependency") — held at D-band only because exploitation is user-mediated; treat as the surface's #1 remediation | 0 | M | med | Bump pdfjs-dist ≥6.2.108 in the `Spaarke.UI.Components` chain + both shipping pages (verify the CDN worker pin at `useChatFileAttachment.ts:357` follows); `npm audit fix` per solution for the build-time HIGHs (brace-expansion/js-yaml/nanoid/postcss/vite); add `--audit-level=high` CI gate (coordinate NG4). **Tranche A, first** |
| D8-02 | HIGH | `@azure/msal-browser` spans 3 majors across pages hitting the same BFF: 3.30.0 (SpeAdminApp:14) / 4.28.2 (LegalWorkspace, Reporting, SpaarkeAi) / 4.30.0 (7 wizards + 3 more) / 5.6.2 (4 pages) / 5.12.0 (SmartTodo). No catalog pins it (no workspaces; verified) | 0 | M | med | Standardize on a single 5.x pin sourced from one shared version set (see D10-06); retire the 3.x/4.x holdouts. Auth-adjacent → verify login/token flows per migrated page. **Tranche B** |
| D8-03 | HIGH | Stale lockfiles: `npm ci` broken on ~14/16 (CLAUDE.md:328); **all three automated paths** run `npm install --legacy-peer-deps` (sdap-ci.yml~339, ci-tier2-advisory.yml~165, deploy-spaarke-ai.yml~93 — workflow comments corroborate), so the 30 committed lockfiles gate nothing and installs float. Also filed as D10-01 (dup) | 0 | M | med | Fix the peer conflicts (D10-04 is the root cause), regenerate all 30 lockfiles so `npm ci` succeeds, switch CI/deploy to `npm ci` (coordinate NG4). **Tranche A** for lockfiles+peerDeps; workflow switch with the CI owner |
| D8-04 | MED | Notepad alone pins React ^18.3.0 (package.json:29-30, @types :23) vs 29 pages + shared lib (authored/tested on 19.2.6) on ^19. Peer range permits 18, but the lib runs untested on 18 | 0 | S | med | Bump Notepad to React 19 (react/react-dom/@types); smoke the page. **Tranche A** |
| D8-05 | MED | Secondary drift, no catalog: @hello-pangea/dnd ^17 (SmartTodo:34) vs ^18.0.1 (3 pages); lexical ^0.41 (3) vs ^0.42 (3) incl. mirrored @lexical/* subpackages; @fluentui/react-icons ^2.0.0→^2.0.326; react-components ^9.46.2 vs ^9.54.0 | 0 | M | low | Single source-of-truth version set for @fluentui/*, lexical, dnd; align all pages. Executes naturally with D10-06 workspace adoption. **Tranche B** |
| D8-06 | LOW | 30 isolated node_modules trees, no hoisting; the 7 Create*Wizards' dependency blocks are byte-identical except name/description (diff-verified) — same transitive graph downloaded + locked 7× | 0 | L | med | npm workspaces at src/solutions (or shared toolchain preset) to hoist common devDeps. Subsumed by D10-06. **Tranche B** |

### 3.9 D9 — Observability (final: **D+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D9-01 | HIGH | `EventDetailSidePane/src/services/eventService.ts:378,:385` — full scalar + lookup save payloads (user-entered sprk_event field data) `JSON.stringify`'d to console, ungated, on the live save flow (App.tsx:491/:705); no build-time console stripping anywhere in src/solutions (verified) | 6 | S | low | Remove the payload dumps (or dev-flag-gate); log field names/counts only. **Tranche A** |
| D9-02 | HIGH | 489 ad-hoc `console.*` calls across 147 files; no shared logger, level control, or serialization contract — only informal `[Solution]` prefixes (anchor `Reporting/src/services/reportingApi.ts:99…`). Only logger-shaped thing is a local DI seam in DocumentUploadWizard | 489 | L | med | Introduce a shared structured logger (level, context, correlation id) in the shared client lib; migrate incrementally; add eslint `no-console` with a logger allowlist once D10-03 gives lint teeth. **Tranche B** |
| D9-03 | HIGH | Telemetry source-integrated in 2/35 (LegalWorkspace, SpaarkeAi) — and **verification: SpaarkeAi's `errorTelemetry` is never wired in production** (setAppInsightsInstance called only in tests) → live telemetry ≈ **1/35**. 3 more pages declare the dep with zero source integration. Critical paths (auth bootstrap, BFF fetch, wizard submit) console-only in ~33 solutions (e.g. CreateMatterWizard/main.tsx:43, DocumentUploadWizard/main.tsx:96) | 0 | L | med | Shared telemetry helper (single init + typed events, per D9-08) routed through critical-path failures across all pages; wire SpaarkeAi's existing wrapper in prod. **Tranche B** |
| D9-04 | MED | `LegalWorkspace/src/services/telemetry.ts:43-48` — hardcoded App Insights connection string (key 09a9beed-…) as **runtime fallback** whenever env-var resolution fails; reached from main.tsx:29 bootstrap; the `!connectionString` guard is dead (fallback always truthy) | 6 | S | low | Remove the fallback; disable telemetry when the env-var key is absent. **Tranche A** |
| D9-05 | MED | 135 `console.log/info/debug` calls across 51 files ship at production log level (App.tsx:203 access-state effect; eventService step traces :380/:388/:403; SmartTodo xrmProvider.ts:109 logs a system-user GUID); no strip config exists | 135 | M | low | Strip or dev-flag the info/debug traces; keep actionable warn/error routed through the D9-02 logger. **Tranche B** (rides the logger migration) |
| D9-06 | LOW | No correlation-ID propagation on client→BFF calls outside LegalWorkspace's `enableCorsCorrelation`; **verified into the shared lib**: `Spaarke.Auth/src/authenticatedFetch.ts:35-42` sets only the Bearer header | 0 | M | med | Add traceparent/x-correlation-id in the shared `authenticatedFetch` + log alongside failures. Shared-lib seam → coordinate with shared-client-libs workstream. **Tranche B** |
| D9-07 | LOW | Telemetry boundary disclaims PII scrubbing and forwards properties verbatim (`errorTelemetry.ts:16-17,:159`; LegalWorkspace telemetry.ts:82-94 likewise — and that path IS live) | 4 | S | low | Property-scrub/allowlist step in the (consolidated) telemetry wrapper before trackEvent/trackException. Rides D9-08. **Tranche B** |
| D9-08 | LOW | Two divergent App Insights wrappers (LegalWorkspace self-initializing full API vs SpaarkeAi externally-initialized error-only; the latter cites the former as "sibling pattern") — duplication (cross-dim D5/D6) with divergent naming/init/scrubbing semantics | 40 | M | med | Consolidate into one shared telemetry module in the shared client lib (single init + typed contract); D9-03/07 build on it. **Tranche B** |

### 3.10 D10 — ALM / build hygiene (final: **D+**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D10-01 | (dup) | = D8-03 (stale lockfiles / floating installs). Counted once | — | — | — | See D8-03 |
| D10-02 | HIGH | **Effectively 30/30 solutions have no PR-blocking build/typecheck gate** (verification strengthened from 29/30): ci-tier1-blocking.yml builds BFF only (:220); the only PR workflow touching src/solutions is the css-reset marker check; sdap-ci's client-quality job (5 shared libs' tsc) is `continue-on-error: true` (:300); SpaarkeAi's vite build runs post-merge in the deploy workflow; CreateMatterWizard is nightly-advisory only | 0 | M | med | Path-filtered blocking CI matrix: `tsc --noEmit` + `vite build` per changed src/solutions root. **Coordinate with ci-cd-unit-test-remediation-r1 (NG4 — owns workflows)**. **Tranche A priority, B-gated by coordination** |
| D10-03 | MED | Lint/format never blocking anywhere for the client surface: ci-tier2-advisory is "NEVER blocking" (:6) with continue-on-error (:99,117,141,169) and no `--max-warnings 0` (:183); sdap-ci's parallel job also continue-on-error (186-warning baseline documented); **and ESLint covers only src/client/pcf — src/solutions is never linted at all** | 0 | M | med | Promote eslint (`--max-warnings 0` after baseline burn-down) + prettier `--check` to blocking tier-1 for changed client paths; extend ESLint coverage to src/solutions. With NG4 owner. **Tranche B** |
| D10-04 | MED | peerDependencies anti-pattern in **26 of 30** leaf apps (private:true) declaring react `^16.14 ∥ ^17 ∥ ^18 ∥ ^19` (e.g. CreateMatterWizard:28-31); verification corrected the mechanism — historical ERESOLVE came from react ^19 vs older fluentui `<19` peer ceilings + stale locks, not a fluentui `>=18` floor. The anti-pattern + remediation stand | 0 | S | low | Remove the peerDependencies block from all 26 leaf apps; keep pinned react in devDeps. Unblocks D8-03 lockfile regeneration. **Tranche A** |
| D10-05 | MED | Two ~15.9 MB BFF deployment tarballs tracked in git (`Sprk.Bff.Api/deployment.tar.gz` 15,879,487 B + `spe-bff-api-deployment.tar.gz` 15,946,946 B ≈ 31.8 MB); `.gitignore` covers `*.zip` but has **no tar pattern** | 0 | S | low | **Already owned by the BFF workstream (task 027 / bff-api design §3.6 + DC-1)** — `git rm --cached` both + add `*.tar.gz`. Recorded here for the census; do not double-task |
| D10-06 | MED | No workspace orchestration: root package.json has no `workspaces`; no pnpm/nx/turbo/lerna anywhere (verified); 80 tracked package.json roots (30 under src/solutions) each resolving independently — the structural cause of D8-02/04/05/06 + D10-07 drift and the lockfile churn | 0 | L | **high** | Adopt npm workspaces (or preset) with one root lockfile + shared react/fluentui/msal versions. Big structural change — own task, rollback plan, quiet window. **Tranche B** |
| D10-07 | LOW | Version drift census (re-tallied): fluentui react-components ^9.46.2 ×24 / ^9.54.0 ×6; vite ^5.0.0 ×28 / ^5.4.0 ×2; react ^19 ×29 / ^18.3 ×1 | 0 | M | low | Centralize shared dep versions; align. Subsumed by D10-06 (+D8-05). **Tranche B** |
| D10-08 | LOW | 7 Create*Wizard build roots, each an independent npm root + vite build with an identical `"build": "vite build"` — 7× the build/config surface for near-identical thin wrappers (code-dup side is D5/D6-01) | 0 | L | med | Collapse onto one shared wizard build entry (or parameterized solution) after D6-01 unifies the bootstraps. **Tranche B** |

### 3.11 D11 — Knowledge/doc accuracy (final: **C-**)

| ID | Sev | Where | LOC | Effort | Risk | Remediation |
|---|---|---|---:|---|---|---|
| D11-01 | HIGH | `docs/architecture/client-resources-inventory.md:195` (+ :41/:103/:136) — deleted `code-pages/AnalysisWorkspace` documented as "active"/"canonical" in the self-declared single source of truth; dir absent from git and disk; `code-pages-architecture.md:23` repeats it | 0 | S | low | Remove/mark-retired AnalysisWorkspace in both docs; correct the src/client/code-pages count 5→4. Doc-only. **Tranche A** |
| D11-02 | MED | `docs/architecture/code-pages-architecture.md:133` mandates "Webpack (not Vite)"; :91 claims "All four Code Pages use webpack 5" — while all 30 src/solutions pages are Vite (0 webpack configs), CommunicationPage is Vite, and only 3 webpack pages remain | 0 | S | low | Add a src/solutions Vite section (or sibling doc); scope the webpack statement to the 3 remaining webpack pages. **Tranche A** |
| D11-03 | MED | `client-resources-inventory.md:42/:144` — counts 23 src/solutions pages (30 exist; ≥7 missing from §4 tables) and labels them "React 18 SPAs" (29/30 are ^19; the 1 holdout is Notepad) | 0 | S | low | Refresh §4 to 30 roots; React 19 with the Notepad-18 note; reconcile with the 35-solution surface framing. **Tranche A** |
| D11-04 | LOW | Live Vite `CommunicationPage` (src/client/code-pages) absent from both docs' inventories while deleted AnalysisWorkspace occupies its slot | 0 | S | low | Add CommunicationPage to both inventories, noting Vite. **Tranche A** |
| D11-05 | LOW | `LEGALWORKSPACE-RETIREMENT.md:77` §4 cites WorkspaceGrid.tsx:432 as the live corporateworkspace navigateTo self-reference — now stale (line holds handleOpenCloseProjectDialog; only comments remain; §8 item 2 records the SmartToDoDialog resolution) | 0 | S | low | Update/drop the stale :432 anchor, pointing to §8's recorded resolution. **Tranche A** |

---

## 4. Refuted by verification (record-only — do NOT act on)

Per r3 NFR-05 input discipline, the following first-pass claim was **refuted** by the Fable adversarial pass. It is recorded so future assessment passes don't re-claim it. It generates **no** grade input and **no** remediation item.

| ID | Claim | Why refuted |
|---|---|---|
| D3-02 | "Raw `Authorization: Bearer ${...}` literal at `SpaarkeAi/src/services/userProfileService.ts:372` deviates from the no-raw-Bearer auth convention" | The site falls under the **explicitly enumerated D-AUTH-7 exception** (ADR-028:33 + `.claude/constraints/auth.md:78`): Dataverse Web API **direct** calls are exempt because the BFF-scoped `@spaarke/auth` wrapper would attach a wrong-audience token to the Dataverse host — the suggested "fix" is precisely what ADR-028 prohibits for this call class. The file header (:15-29) documents the decision, mirroring the sanctioned PlaybookBuilder `dataverseClient.ts` sibling. Additionally, the sole production call site (`useMyAssistant.ts:107`) passes no deps, so `deps.getToken` is undefined and the Bearer branch never executes in production (same-origin cookie auth). Residual cosmetic nit only: the site lacks the literal `// Auth v2 (D-AUTH-7):` justification tag — a comment-tag nicety, not a violation. |

**Verification-pass corrections that DID survive as findings but with mandatory remediation constraints** (restated for emphasis; details in §3.5 KEEPs): D5-01's original "delete the retired LegalWorkspace tree" framing is FALSE — the tree is live library input to SpaarkeAi's build; only the alias-routing fallback remediation is valid for D1-05, and only the per-file-verified orphan clusters (D5-01..04) are deletable.

---

## 5. Data-driven-dispatch pre-check list (NFR-08)

Dataverse `sprk_*` configuration is not grep-provable from the repo. Findings flagged `requiresDataverseCheck` MUST run their live check before remediation executes. Only the **spaarke-dev** environment is live (demo/prod decommissioned — project CLAUDE.md 2026-08-14).

| Finding | Exact pre-check remediation must run FIRST |
|---|---|
| **D5-06** (remove the retired standalone LegalWorkspace entry + build target) | 1) Confirm retirement-doc follow-up #3 (deletion of the `sprk_corporateworkspace` web resource) has **landed in spaarkedev1** — the deployed copy persists there per LEGALWORKSPACE-RETIREMENT.md §8.1. 2) Run the live dependency check the doc itself says is not auditable from the local checkout: `RetrieveDependenciesForDelete` (or Solution → web resource → Show Dependencies) on `sprk_corporateworkspace`, verifying **zero** Default-Solution consumers (forms, ribbon commands, sitemap subareas, other web resources). 3) Grep the exported live sitemap/form XML for `sprk_corporateworkspace` as a belt-and-suspenders. Only then remove App.tsx/main.tsx/index.html/.env.production + the vite build target. If any dependency remains → STOP, file the blocker on the retirement doc's follow-up list. |

No other verified finding requires a Dataverse pre-check: D5-01/02/03/04 deletions were explicitly verified safe because the shell dispatches wizards **by web-resource name** via `Xrm.Navigation.navigateTo` (`sprk_createeventwizard`, `sprk_createprojectwizard`, `sprk_creatematterwizard`), not by importing the local components — deleting the orphan clusters changes only the LegalWorkspace bundle. D6-05's folder renames need **local** deploy-script registry updates (scripts/Deploy-*.ps1), not a Dataverse check, because web-resource output names stay unchanged.

---

## 6. Proposed workstreams → phases (A/B tranche split per r3 NFR-04)

**Tranche A** = low-contention bugs + hygiene, safe to execute now as small PRs off the r3 branch (`/conflict-check` each). **Tranche B** = wide or contested edits (SpaarkeAi God-component splits — 8/17 active worktrees touch SpaarkeAi; shared-lib seams; workflow edits owned by ci-cd-unit-test-remediation-r1; monorepo structure; surface-wide format flips) — reserved for a coordinated quiet window.

### Tranche A — Phase 1: Security & live-bug fixes (do first)
- 1a. **D8-01** — pdfjs-dist ≥6.2.108 (shared SprkChat chain + SpaarkeAi + DailyBriefing; verify the version-pinned CDN worker follows) + per-solution `npm audit fix` for build-time HIGHs.
- 1b. **D4-02** — fix the BulkOperationProgress stale-closure poller (+docstring).
- 1c. **D9-01 / D9-04** — remove save-payload console dumps; remove the hardcoded App Insights fallback key.
- 1d. **D3-01** — textContent at the 4 innerHTML bootstrap sinks.
- 1e. **D4-01 / D4-04** — scalar counts for Quick Summary; `$top` on the active-todos query.

### Tranche A — Phase 2: Dead code & duplication deletions (LegalWorkspace orphans)
- 2a. **D5-02** then **D5-04** (ordering per the LookupField/AiFieldTag transitive-death chain), retaining CloseProjectDialog/closureService and re-homing the 4 verified survivors incl. `wizardTypes.ts`.
- 2b. **D5-01** — delete CreateEvent cluster. **D5-03** — delete FindSimilar shims.
- 2c. **D2-01/D2-02** — delete the dead `useTodoScoring` hook (+ its dead dialog error-branch contract) as part of the sweep.
- Gate: per-solution `vite build` green + grep-zero dangling imports.

### Tranche A — Phase 3: Install reproducibility + small consistency/doc fixes
- 3a. **D10-04** — strip peerDependencies from the 26 leaf apps; **D8-03** — regenerate all 30 lockfiles until `npm ci` passes per solution. (Workflow switch to `npm ci` lands in Phase 6 with the CI owner.)
- 3b. **D8-04** — Notepad → React 19.
- 3c. **D6-02/03/04/06/07** — comment fix, log tags, package names, import paths, titles.
- 3d. **D11-01..05** — the five reference-doc corrections.
- 3e. **D7-06** — stale test-header fix (with 3f).
- 3f. **D7-01 / D7-03 / D7-04** — wire the LegalWorkspace runner (or relocate the 3 files); wire the now-unblocked rowHeight assertions; add the mailto seam + un-skip. **D7-02** runner + first SpeAdminApp mutation tests (additive, low contention).

### Tranche B — Phase 4: Shared seams & consolidation (quiet window / coordinated)
- 4a. **D6-01** — extend `useWizardPageBootstrap`; migrate the 4 inline wizards. Then **D5-07** — consolidate the 8 authInit copies onto the same seam.
- 4b. **D5-05** — extract SmartToDo into the shared package; both consumers migrate.
- 4c. **D9-08 → D9-03 → D9-07** — one shared telemetry module; wire critical paths across pages; add the scrub step. **D9-06** — correlation header in shared `authenticatedFetch` (coordinate shared-client-libs workstream).
- 4d. **D9-02 → D9-05** — shared structured logger; migrate the 489 console sites incrementally; strip info/debug residue.
- 4e. **D8-02** — msal-browser single 5.x pin (auth-adjacent; page-by-page verification).

### Tranche B — Phase 5: Structure & gates (quiet window / owner-coordinated)
- 5a. **D10-02** — PR-blocking path-filtered tsc+vite matrix (with ci-cd-unit-test-remediation-r1). **D10-03** — blocking lint/format + extend ESLint to src/solutions. **D4-03** — bundle-size budgets. **D8-01's** `--audit-level=high` gate.
- 5b. **D10-06** — workspace adoption decision (own task; subsumes D8-05/06, D10-07/08 alignment).
- 5c. **D6-08** — un-ignore src/solutions in Prettier after the quote-rule decision; one intentional format commit.
- 5d. **D6-05** — rename the 4 sprk_ folders + update deploy-script registries.
- 5e. **D1-01/02/03/04** — God-component decomposition, prioritized: ArrangeStep algorithm extraction first (pairs with D7-03's test seam, lowest contention), then the 6 >1,000-LOC files, then ConversationPane/WorkspacePane (highest contention — schedule against the SpaarkeAi worktree map). **D1-05** alias-routing rides any LegalWorkspace-touching PR.
- 5f. **D5-06** — retired standalone entry removal, **strictly after the §5 Dataverse pre-check**.
- 5g. **D7-05** — Reporting runner + behavior tests.

### Phase 6 — Wrap-up
- `090-wrapup` task → `/test-diet` gate (new tests added in Phases 3f/5g are MAINTAIN-class candidates), doc-drift re-audit of the §3.11 files, SCORECARD reconciliation.

**Not tasked here**: D10-05 (tarballs — owned by bff-api workstream task 027).

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| SpaarkeAi contention (8/17 active worktrees) breaks a God-component split mid-flight | All D1 SpaarkeAi decomposition in Tranche B quiet window; `/conflict-check` before every PR; ArrangeStep (WorkspaceLayoutWizard) first |
| Deleting LegalWorkspace files breaks the live SpaarkeAi bundle | NG1 KEEP honored; only per-file-verified orphans deleted; per-solution `vite build` + SpaarkeAi build green as the phase gate |
| pdfjs-dist 5→6 major bump changes SprkChat PDF parsing behavior | Targeted attachment-parse smoke (text-extraction happy path + worker load) before/after; the CDN worker pin must track the installed version |
| msal-browser standardization alters a page's auth flow | Page-by-page migration with login/token smoke; 4.x→5.x holdouts last; shared-catalog pin so drift can't recur |
| Lockfile regeneration changes resolved transitive versions silently | Regenerate per solution in isolated PRs; diff resolved top-level versions; `npm ci` + `vite build` green per solution |
| Workflow edits collide with the CI owner | NG4: all `.github/workflows` changes proposed to `ci-cd-unit-test-remediation-r1`, not committed unilaterally |
| Prettier un-ignore floods blame history | Single dedicated format commit; `.git-blame-ignore-revs` entry; quiet window |
| D5-06 removal orphans a live Dataverse consumer | §5 pre-check is a hard gate (`RetrieveDependenciesForDelete` + sitemap/form grep in spaarkedev1) |

---

## 8. SCORECARD row inputs (feeds `notes/SCORECARD.md` — appended by the invoking task, not this design)

**Surface**: Code Page solutions (35) + build/config sprawl · **Assessed**: 2026-08-14 · **Method**: quality-assessment workflow (11-dimension fan-out + Fable adversarial verification)

**Dimension letters**: D1 **C+** · D2 **B+** · D3 **A-** · D4 **C+** · D5 **D+** · D6 **C+** · D7 **D+** · D8 **D** · D9 **D+** · D10 **D+** · D11 **C-**

**Composition (§4.2)**: equal-weight mean = (2.3+3.3+3.7+2.3+1.3+2.3+1.3+1.0+1.3+1.3+1.7)/11 = **1.98 → C**. Gating cap `min(C, D2=B+, D3=A-)` = **C** (cap **not** binding — the mean already sits below both gating grades). **Surface grade: C.**

**Evidence bullets** (one per dimension):

- **D1 C+** — 27 non-test components >800 LOC across 6 solutions (census reproduced exactly); worst: ConversationPane.tsx 3,444 LOC/81 hook calls, WorkspacePane.tsx 2,631, ArrangeStep.tsx 2,071; plus 13 deep relative imports bypassing the `@spaarke/ui-components` alias (D1-05). ADR-013 facade intact — no page calls LLM internals.
- **D2 B+** — gating; own findings collapsed on verification to INFO+LOW (both in the zero-call-site `useTodoScoring` hook); the surface's one live correctness bug is the cross-filed D4-02 stale-closure poller (onComplete re-fires every 2 s post-completion, parent keeps it mounted). No shipped broken data path found.
- **D3 A-** — moved up from first-pass B+: the raw-Bearer claim (D3-02) was refuted as a sanctioned D-AUTH-7 Dataverse-direct exception; sole survivor is one LOW finding (4 bootstrap innerHTML sinks fed by config-derived error text). `@spaarke/auth` on every data path; OData injection boundaries escaped throughout.
- **D4 C+** — Quick Summary fetches up to 3,600 rows/mount to read `.entities.length` with counts silently capped at $top (live on the SpaarkeAi home); BulkOperationProgress polls forever post-completion; no bundle-size budget/gate on any of the 30 inline-everything single-file pages.
- **D5 D+** — dead code at scale + diverged forks: four zero-inbound LegalWorkspace wizard clusters (~2,360 LOC incl. the 427-diff-line matterService fork; FindSimilar downgraded to 137-LOC shims) and a live 1,800-LOC SmartToDo fork (520 diff lines) duplicated across two solutions.
- **D6 C+** — Create*Wizard family split 3-hook/4-hand-inlined on its core bootstrap (~100 dup LOC each); demonstrably false "matches … exactly" comment (false for 2 of 3 named siblings); 3 package-name conventions; log-tag/quote/title drift; Prettier explicitly ignores src/solutions.
- **D7 D+** — three LegalWorkspace test files can never execute (no runner, self-documented); SpeAdminApp (41 logic files, live POST/PUT/PATCH/DELETE admin client incl. destructive recycle-bin) has zero tests and no runner; the rowHeight skip-block's stated blocker is stale (buildSectionsJson now exported).
- **D8 D** — HIGH CVEs open in all 30 solutions incl. runtime-shipped pdfjs-dist 5.7.284 (arbitrary-JS-on-malicious-PDF), dynamic-imported by SprkChat on user-attached PDFs (F-borderline → tranche-A #1); msal-browser spans 3 majors; `npm ci` broken ~14/16 so all installs float past the 30 lockfiles.
- **D9 D+** — full record save payloads JSON-dumped to console on a live save path; 489 unstructured console.* calls in 147 files with no shared logger; live telemetry effectively 1/35 (SpaarkeAi's wrapper never wired in prod); hardcoded App Insights key committed as runtime fallback.
- **D10 D+** — effectively 30/30 solutions have no PR-blocking build/typecheck gate (tier-1 builds BFF only; the client CI job is continue-on-error); lint/format advisory-only and src/solutions never linted; no workspace orchestration across 80 npm roots; 2×15.9 MB tarballs tracked (`*.tar.gz` gitignore gap — BFF-owned fix).
- **D11 C-** — the self-declared "single source of truth" inventory documents the deleted AnalysisWorkspace as active/canonical, counts 23 pages where 30 exist, and labels the surface React 18 (29/30 are 19); the architecture doc mandates webpack while all 30 solutions build with Vite.

---

*This design is the Fable-verified assessment output for surface #6 (Code Pages + build/config sprawl) of code-quality-and-assurance-r3. Remediation is operator-gated: `/design-to-spec` → task creation happens only on owner approval, per the program's assessment-first decision (2026-08-06).*
