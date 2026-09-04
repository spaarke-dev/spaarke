# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-09-03 (matching-approaches analysis DONE + owner-reviewed; arch-doc refreshed; **go-forward plan authored at `notes/email-matching-and-triage-go-forward-plan.md`** — that plan is now the tracker for P1/P2/G1–G5).
> **Recovery**: Read "Quick Recovery" first, then the **go-forward plan** (`notes/email-matching-and-triage-go-forward-plan.md`), then "FRESH-SESSION PRIORITIES" below.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | `work/email-communication-intelligence-r2`. **Add-in stream MERGED to master** via **PR #934** (squash `f5fee2141`; master tip `399651fff`). Tree clean. Branch not yet deleted. Main repo `C:/code_files/spaarke` local master is behind (has unrelated dirty WIP — left untouched; ff it when convenient). |
| **Add-in stream — ✅ DONE + MERGED** | Outlook/Word add-in: **Create To Do → first-class `sprk_todo`** (new BFF `POST /api/office/todo` + `OfficeService.CreateTodoAsync`); §C Save→ToDo regarding wiring; §10 contract tests; **Word real `.docx` save** (`WordHostAdapter.getFileAsync(Compressed)` + `.docx` ext); **web auth fix** (`OfficeNaaStrategy`: desktop=silent NAA, Office-web=standard `https` popup via `auth-callback.html`; portable `brk-multihub://${hostname}`); XML manifest for M365 admin center (`outlook/outlook-manifest.xml`); naming "Spaarke Outlook"/"Spaarke Word"; **white-on-black icons**. All deployed to dev + UAT-confirmed. Full detail: `git log` on the branch / PR #934. |
| **Build gate** | BFF: `dotnet build src/server/api/Sprk.Bff.Api/` (0-err). Add-in: `cd src/client/office-addins && npm run build:dev`. `npm run typecheck` = ~397 PRE-EXISTING errors (exactOptional) — filter to changed files. |
| **NEXT** | **P1 — seed the triage routing catalog to `spaarkedev1`** (verify dev empty first, then seed Actions→consumers). Tracked in `notes/email-matching-and-triage-go-forward-plan.md`. The matching-approaches analysis is DONE + owner-reviewed (arch-doc refreshed 6→13 rungs; note extracted + deleted). |

---

## 🎯 FRESH-SESSION PRIORITIES (owner-set 2026-09-03)

### 0. ✅ DONE — matching-approaches analysis + owner review (2026-09-03)
Analyzed against a full codebase inventory: the 5-tier "matching ladder" is **already built** as the 13-rung Association Engine. Owner reviewed the feedback and directed: refresh the canonical arch doc (✅ `communication-intelligence-architecture.md` §3–§5, 6→13 rungs), extract the 2 worthwhile ideas (eval harness + party graph) into a plan, delete the note. **All done** — see `notes/email-matching-and-triage-go-forward-plan.md` (the tracker for P1/P2/G1–G5). Plan items **G1 done**, **G2** (delete categorization dead-seam) pending, **G3/G4** queued, **G5** parked (ADR-013 Path-B).

### 1. 🔴 Triage fix (HIGH) — the whole Pillar-E intelligence layer is "built but dark"
- **Symptom**: on REAL email captures, every triage field is null — `sprk_triagepriority` / `sprk_triagecategory` / `sprk_triagesummary` / `sprk_riconfidence` / `sprk_reviewoutcome`. Also Job-B (propose field-updates) + Job-C (propose create-task) produce nothing. Only the 14 seeded rows have triage (seed script wrote fields directly).
- **Root cause (already found)**: `notes/DEFECT-triage-not-populating-root-cause.md`. The AI **routing catalog** rows were **never seeded to `spaarkedev1`**: `sprk_playbookconsumer` (email-triage / email-propose / email-create-task) + their `sprk_analysisaction` rows don't exist. `CommunicationTriageAi.TriageAsync` → `ActionResolver.ResolveAsync("email-triage")` throws "no Action routed" → caught → returns null → NFR-04 best-effort swallow → triage never persists. (Rung-5 classify works because it calls Azure OpenAI directly, no routing table — that's why `ai-classify:` provenance IS present but triage is NOT.)
- **Fix (data-only, reversible, operator-gated)**: seed via `scripts/dataverse/Seed-PlaybookConsumers.ps1` — **Actions FIRST**, then consumers (mirror `infra/dataverse/sprk_playbookconsumer-rows.json` lines 454-513 carries the 3 email rows). `sprk_systemprompt` = the classic-JPS root of the `.action.json`. **Verify first**: query `sprk_playbookconsumer WHERE sprk_consumertype IN (email-triage,email-propose,email-create-task)` in dev → expect `[]` today. After seed, re-capture a real email → triage fields populate → the reconcile grid columns + Fields/Tasks proposal tabs light up.
- **Why it matters**: the reconciliation UX is **built + hosted** (see COORD-058 below) but displays nothing until this data exists.

### 2. 🟡 Email semantic AI search (feature the owner wants)
- Captured `.eml` archives are `sprk_searchindexed=false` → never AI-indexed (regular docs = `true`). Pillar-B UAT finding **#5**. Owner: **the `.eml` files SHOULD be sent to our AI search index.**
- **Open question the owner raised (investigate)**: *are emails searched/analyzed differently because they're emails?* (chunking, metadata, sender/thread fields, `.eml` MIME parsing vs a plain doc). Root-cause why the downstream analysis/indexing job skips/fails email archives (worker DOES queue AI+RAG when `triggerAiProcessing`+`ragIndex` both set — failure is downstream).
- **Scope guard (owner steer)**: this is about **real-time-captured** emails being searchable — NOT bulk-reviewing historical/existing mailboxes (that's a SEPARATE future onboarding project).

### 3. 🔵 Minor / decisions (carry-overs)
- Re-verify **#4** (selected attachments → child Documents) end-to-end on the deployed dev (fixed + merged; needs fresh confirm).
- **§A.4** SaveFlow footer gray-"Saved" style — open product decision (currently keeps the richer "Document Saved" card).
- **Ribbon command icons** — manifest references `save-*/share-*/grant-*.png` that don't exist → broken glyphs. Cosmetic; regenerate via `generate-icons.mjs` pattern.
- **Per-env Entra provisioning doc** — fold into `docs/guides/auth-deployment-setup.md` + customer deployment guide: each customer env registers `brk-multihub://<host>` **and** `https://<host>/auth-callback.html` as **SPA** redirect URIs (Pillar-B #1 + the web-auth fix both need this).

### Status of the "deferred" items (resolved this analysis, 2026-09-03)
- **COORD-058 (Pillar-E reconcile host) — ✅ effectively DONE**: R2 self-hosted the reconcile UX — `src/solutions/CommunicationReconciliation/` code page + `Spaarke.AI.Widgets/.../ReconciliationWorkspaceWidget.tsx` + `LegalWorkspace/.../reconciliation.registration.ts` (tasks 061–063). r5 (closed) NOT needed. The note was written before self-hosting.
- **COORD-054 / COORD-064 — no action**: 054 explicitly "no code change required" (future convergence only if a 3rd consumer appears); 064 additive + already shipped.
- **`.eml` archive lookup DEFECT** — ✅ RESOLVED (`0026af5e1`, `sprk_communication`→`sprk_relatedcommunication`).
- Pillar-B **#2/#3/#6/#7** — ✅ fixed + on master (#934). #2 (real Dataverse search) confirmed by Word UAT.

---

## Outstanding work (prioritized)

### A. #3 — Create To Do form redesign — ✅ DONE (commit d2b5dbc34, 2026-09-02)
**Decision locked (AskUserQuestion):** creates a **first-class `sprk_todo`**, not `sprk_event` (the requested Priority/Effort/Contact-assignee fields only exist on `sprk_todo`; owner confirmed "we are not using the sprk-event type 'to do' anymore"). Regarding = the record the email was filed to.

**Built:** `CreateTodoView.tsx` full form (Name/Description/Assigned-To Contact type-ahead/Due/Priority/Effort dropdowns), green record card for the regarding, Cancel(reset)/Save footer with gray "Saved" (don't-close). `todoChoices.ts` add-in-local mirror of the priority/effort choice→score tables (§11 sanctioned dup of `todoScoreMappings.ts`). BFF: `CreateTodoRequest`/`Response`, `OfficeService.CreateTodoAsync` (app-only create via `IGenericEntityService`, ownerid=caller, regarding via `sprk_regardingmatter/project/invoice` + resolver fields id/name + best-effort `sprk_recordtype_ref`, assignee→contact), `POST /api/office/todo`. Contact lookup reuses `/api/office/search/entities?type=Contact`.

**Residual / open:**
- **§A.4 SaveFlow "Saved" footer** — owner asked the Save form footer to also show gray "Saved". DEFERRED: SaveFlow already has a richer async success flow (job-status → "Document Saved" card, pane stays open) — didn't force the gray-button pattern there to avoid regressing View-Document/Save-Another affordances. **Confirm with owner** whether they want the exact gray-button style on the Save footer too.
- **§10 test obligation** — `Services/Office` changed; add/confirm a test for `POST /api/office/todo` before the wrap-up PR (OfficeService's 12-dep ctor is mock-heavy per ADR-038 → prefer the e2e spec path).
- `sprk_assignedto` targets **contact** (live), NOT systemuser as the stale `entity-schema.md` row 40 says — the wizard's `todoService` + the owner both confirm contact.

**Original owner UI feedback 2026-09-02 (for reference):**
1. **Replace "Linked to" with the record card** — reuse the Save screen's selected-card look (green check style) for the regarding record.
2. **Full form** matching the existing **`CreateTodoWizard` "To Do Details" step**: **Name/Title, Description, Assigned To (lookup to Contact), Due Date, Priority (choice), Effort (choice)**, Notes. Owner: *reuse the CreateTodoWizard form if possible, else recreate to match*.
   - The wizard form lives in **`@spaarke/ui-components`** (`src/solutions/CreateTodoWizard/src/main.tsx` is just a thin Xrm host). ⚠️ Like the reconciliation components, it is likely **Xrm-host-bound** (Assigned-To lookup, choice option-sets) → the add-in (no Xrm) probably **recreates the layout** rather than reusing directly. Investigate first (mirror the RelatedToPicker verdict).
   - **Assigned To** in the add-in needs a **Contact search via BFF** (no Xrm lookup); **Priority/Effort** are `sprk_event` option-sets — render as `<Select>` with the option values.
   - **BFF gap**: the create-task endpoint `POST /api/communications/{communicationId}/create-task` (`CreateAdHocTaskRequest` in `CommunicationCreateTaskApplyService.cs`) has Subject, Description, DueDate, **AssignedTo**, Status — but **NOT Priority/Effort**. Adding those = a BFF change (extend the request + the impersonated PATCH field set). Confirm the `sprk_event` field names for priority/effort before wiring.
3. **Cancel + Save at bottom; on Save DO NOT close the pane** — change the Save button to a **gray "Saved"** indicator. **Same behavior for the main Save-to-Spaarke form** (SaveFlow footer). (Cross-cutting: both `CreateTodoView` and `SaveFlow` footers.)

Current `CreateTodoView.tsx` has only Title + Due date + a warning-style "Linked to". Files: `src/client/office-addins/shared/taskpane/components/views/CreateTodoView.tsx`.

### B. Logo — ✅ DONE (92dfacfe3)
- Source `shared/assets/spaarke-logo.svg` (owner's black mark). `generate-icons.mjs` rewritten to render from it into `shared/assets` (the CopyWebpackPlugin source) → produced the missing `icon-color.png` (128) + `icon-outline.png` (32) that the Outlook unified-manifest `icons.color`/`icons.outline` reference (was blank tile), plus refreshed `icon-16/32/64/80/128`. Verified they copy to `dist/assets` on build. `sharp` is a manual dev dep (`npm install --no-save sharp`), not in package.json.
- **Not done (pre-existing, out of §B scope):** ribbon command-button icons `save-*/share-*/grant-*.png` referenced by the manifest don't exist in `shared/assets` → those buttons show broken/blank glyphs. Separate cleanup.

### C. Production save-context wiring — ✅ DONE (9e62afcac)
- SaveFlow fires `onSaved(selectedEntity)` once on save complete/duplicate; App's `handleSaved` seeds `savedContext` from it → the Create To Do tab goes live (regarding = the filed record) after a real save. `SavedTodoContext.communicationId` made optional (the To Do regarding is the record, not the communication). Demo detection tolerant of the optional id. **Verify end-to-end once deployed** (a real save → switch to Create To Do → confirm the regarding card shows the saved record).

### D. Deploy — version bumps DONE (c9d89ca2f); EXECUTION is operator-gated (live Azure/M365/CI — NOT agent-run)
Versions bumped: Outlook `manifest.json` + `index.tsx` → **1.0.21**; Word → **1.0.5**. Operator steps, in order:
1. **BFF** (adds `POST /api/office/todo`): `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` → verify `curl https://spaarke-bff-dev.azurewebsites.net/api/office/todo` returns **401** (route found, auth required) NOT 404.
2. **Add-in**: push branch → GitHub Actions `deploy-office-addins.yml` (holds secrets; live SWA `b64eb1876` on `icy-desert-0bfdbb61e.6.azurestaticapps.net`). Confirm run green + `gh run list --workflow=deploy-office-addins.yml`.
3. **M365 re-register** at the bumped versions (M365 admin / sideload) — required because the manifest version changed.
- **§10 test obligation — ✅ DONE (e59c7a4b0):** `tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs` — `Post_OfficeCreateTodo_WithMissingName_Returns400` + `_WhenUnauthenticated_Returns401` (both run) + a Skip'd 201 happy-path.

---

## Add-in architecture (key files + facts)

**Reconciliation reuse verdict**: the add-in can't reuse the Xrm-bound reconciliation/wizard UI components; it reuses the **host-agnostic candidate logic** (`@spaarke/communication-components/logic/connections/provenance` → `derivePrimaryReview`) + BFF endpoints, and renders **its own Fluent v9 cards**.

**Client (`src/client/office-addins/`):**
- `shared/taskpane/components/RelatedToPicker.tsx` — auto-match cards + chips (Matter/Project/Invoice, left of "Related to" label) + inline-create-in-search-row + green-check select. `onCreateRecord(type,name)` → BFF quickcreate → auto-select.
- `shared/taskpane/components/SaveFlow.tsx` — uses RelatedToPicker; `relatedSearch` (GET `/api/office/search/entities`), `createRelatedRecord` (POST `/api/office/quickcreate/{type}`); AI-processing UI removed (defaults stay on); wizard footer (Cancel/Save — **needs "Saved" state per §A.3**). DEMO_RELATED_CANDIDATES + isBrowserTestMode mocks.
- `shared/taskpane/services/communicationSuggestionsService.ts` — `fetchRelatedCandidates()` (candidates w/ confidence) + `RelatedCandidate`.
- `shared/taskpane/components/TaskPaneToolbar.tsx` (single toolbar) + `TaskPaneShell.tsx` + `TaskPaneNavigation.tsx` (`getAvailableTabs`).
- `shared/taskpane/components/views/CreateTodoView.tsx` — inline Create To Do (**needs the §A form redesign**). `App.tsx` renders it under the `createTodo` tab; `outlook/taskpane/index.tsx` mocks auth + demo saved-context in test mode; `taskpane-test.html` sets the test flag.
- `SpaarkeLogo.tsx` — was swapped to the color brand SVG then removed from the toolbar (component still exists, unused).

**BFF (Slice 3):**
- `Services/Office/OfficeService.cs` `QuickCreateAsync` — creates `sprk_matter`/`sprk_project`/`sprk_invoice` (name field per type; matter/project also description) via `IGenericEntityService.CreateAsync` with `ownerid` = caller (resolved in the endpoint). Optional `IGenericEntityService` ctor dep (registered singleton → IDataverseService).
- `Api/Office/OfficeEndpoints.cs` — `MapQuickCreateEndpoints` uncommented → `POST /api/office/quickcreate/{entityType}` routable; handler resolves caller systemuserid via `ICallerSystemUserResolver` (best-effort) + passes it.
- `Services/Office/IOfficeService.cs` — signature gained `string? ownerSystemUserId`.

---

## Prior completed work (merged to master, this project)
- **#919 document-profile AI bug** FIXED (#923, deployed). **Document Upload wizard "Send Email"** rebuilt (#925/#927/#929/#930). **Task 044 add-in deploy** (live SWA current). **R-1/R-2/R-3 remediation** shipped.

## Merge/deploy reference
- Master PROTECTED (ruleset `21824191`, required check literal `Router`). `gh pr create` + `gh pr merge {n} --auto --merge`. `git fetch origin && git merge origin/master` before pushing.
- Add-in deploy: `deploy-office-addins.yml`. BFF deploy: `scripts/Deploy-BffApi.ps1`. M365 re-register needs a manifest version bump.
