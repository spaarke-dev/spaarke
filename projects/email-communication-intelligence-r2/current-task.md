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
| **DONE** | Slices 1–3 (toolbar, reconciliation "Related to" cards, inline New-record). **#3 Create To Do → first-class `sprk_todo`** (d2b5dbc34) + UAT round (f8364819e: responsive form, plain no-icon Save on both To Do + SaveFlow, schema fix). **§C save→To-Do regarding wiring** (9e62afcac: SaveFlow `onSaved` → App seeds savedContext live after a real save). **§10 contract tests** for `POST /api/office/todo` (e59c7a4b0: 2 run, 1 skip). **§B app-tile icons** from Spaarke logo (92dfacfe3: generated icon-color/outline + refreshed set → fixes blank tile). **§D version bumps** (c9d89ca2f: Outlook 1.0.21, Word 1.0.5). All builds 0-err. |
| **DEPLOYED 2026-09-03** | **BFF** → `spaarke-bff-dev` (Deploy-BffApi.ps1; hash-verified; `/api/office/todo`→401 live). **Add-in** → SWA `spaarke-office-addins` (`icy-desert-0bfdbb61e.6.azurestaticapps.net`, RG `spe-infrastructure-westus2`) via `gh workflow run deploy-office-addins.yml --ref work/email-communication-intelligence-r2` (run 33706384108, success). Verified live: outlook manifest **1.0.21**, `assets/icon-color.png`+`icon-outline.png` 200. |
| **UAT ROUND 2026-09-03** | **M365 admin center registration**: the unified `manifest.json` (manifestVersion `devPreview`) is REJECTED by Integrated apps (URL upload expects XML; devPreview not accepted). Solution: **restored the OfficeApp/MailApp XML manifest** at `outlook/outlook-manifest.xml` (recovered from d47fa31f1^) + wired into the build → served at `https://icy-desert-…/outlook/outlook-manifest.xml`. **Word already has XML** (`word/manifest.xml`). Admin center accepts the XML by **file upload** (URL upload failed — content-type quirk). Naming: **Outlook="Spaarke Outlook"**, **Word="Spaarke Word"**. Logo enlarged (icons regenerated at 4% padding; IconUrl→icon-64, HighRes→icon-128). Versions: Outlook **1.0.22**, Word **1.0.6**. Re-upload files: `C:\tmp\spaarke-outlook-manifest.xml`, `C:\tmp\spaarke-word-manifest.xml`. **Word .docx bug FIXED** (cf2f46527): `WordHostAdapter` now captures the real .docx via `getFileAsync(FileType.Compressed)` (was flat OOXML XML from `getOoxml()` → no preview/open/AI); `useSaveFlow` guarantees `.docx` extension. **SPE content-type sanity check: BFF correct** — Graph infers MIME from the name extension (`ItemWithPath(path).Content.PutAsync`), so the `.docx` extension fix is sufficient; no BFF change. |
| **NEXT** | Reload the Word taskpane (close/reopen) → re-test Save: doc should preview + open in Word + mount to Compose + AI profile the real content. Re-upload the 1.0.22/1.0.6 XML manifest files in admin center for the name+logo. Then Outlook Create To Do live UAT (§1.7 real-Dataverse smoke — first exercise of the `sprk_todo` write path). Open decision: SaveFlow footer gray-"Saved" style (§A.4). |

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
