# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-09-02 (context-handoff before /compact — Outlook add-in UX redesign: Slices 1–3 DONE on branch + multiple UI-feedback rounds; next big item is #3 the Create To Do form redesign)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | **Outlook add-in UX redesign** (UAT-driven, owner 2026-09-01/02). Iterated in the add-in's **own browser test harness**: `cd src/client/office-addins && npm run start:outlook` → `https://localhost:3000/outlook/taskpane-test.html` (mock Office + mock auth + demo data via `window.__SPAARKE_TEST_MODE__`). NOT spaarke-prototype (Xrm-based; add-in is Office.js). First run: `npm install --legacy-peer-deps` + `npx office-addin-dev-certs install`. |
| **Branch** | `work/email-communication-intelligence-r2` — **10 commits ahead of master, all committed, tree clean.** Nothing merged yet (WIP). |
| **Build gate** | Add-in: `cd src/client/office-addins && npm run build:dev` (webpack/babel — the gate). BFF: `dotnet build src/server/api/Sprk.Bff.Api/`. Both currently **0-err**. `npm run typecheck` = ~393 PRE-EXISTING errors (exactOptional / jest-dom missing) — filter to changed files. |
| **DONE** | **Slice 1** toolbar (logo removed, tabs+⋮ overflow) + "Related to" rename. **Slice 2** reconciliation-style auto-match cards + chips + green-check select + wizard footer + AI-toggles-removed. **Slice 3** BFF-backed inline "New record". Inline-create in the search row; chips = Matter/Project/Invoice; BFF quickcreate supports Invoice. **#3 Create To Do form redesign — DONE (commit d2b5dbc34):** now creates a **first-class `sprk_todo`** (NOT sprk_event) regarding the filed record, mirroring the CreateTodoWizard field set (Name/Description/Assigned-To-contact/Due/Priority/Effort); regarding shown as green record card; Cancel/Save footer with gray "Saved" (pane stays open). New BFF `POST /api/office/todo` + `OfficeService.CreateTodoAsync`. Both builds 0-err; publish 48.94 MB; no CVEs. |
| **NEXT (big)** | Owner UAT of the Create To Do form (browser harness). Then: SaveFlow footer "Saved" decision (§A.4), production save-context wiring (§C), logo (§B), deploy (§D). |

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

### B. Logo
- **Black logo asset saved**: `projects/email-communication-intelligence-r2/spaarke-black-logo.svg` (owner-provided, monochrome). Earlier color one at `spaarke-color-logo-only.svg`.
- The in-pane toolbar logo was **removed** per feedback. The black logo is (most likely) for the **app-tile** — the manifest `icons.color`/`icons.outline` point at **missing** `assets/icon-color.png`/`icon-outline.png` → **blank Apps-list tile**. Wiring = generate PNGs (color + 32px monochrome outline + ribbon icons) from the SVG via `generate-icons.mjs` (uses `sharp`) → reference in `outlook/manifest.json` + `word/word-manifest.xml`. Confirm with owner whether black logo is for app-tile or a re-added in-pane mark.

### C. Production wiring (before deploy)
- **Create To Do** real `communicationId` + regarding must come from the Save flow (demo-wired only via `initialSavedContext`).
- **Related-to** confirm writes the regarding at SAVE (existing path) — verify end-to-end once deployed.

### D. Deploy
- Version bump (Outlook `manifest.json` 1.0.20→ + Word) → GitHub Actions `deploy-office-addins.yml` (holds secrets; live SWA `b64eb1876` on `icy-desert-0bfdbb61e.6.azurestaticapps.net`) → re-register in M365 admin (version bump required).
- **BFF must also deploy** (Slice 3 added the quickcreate endpoint) via `.\scripts\Deploy-BffApi.ps1`.
- Other-entity-types: quickcreate is Matter/Project/Invoice only (Account/Contact removed from chips; the 3 supported types cover the chips).
- **Test obligation (§10)**: BFF `Services/Office` changed → the e2e spec `tests/e2e/specs/quickcreate-flow.spec.ts` covers the flow (a unit test needs OfficeService's 12-dep ctor → mock-heavy, ADR-038-discouraged). Add/confirm before the wrap-up PR.

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
