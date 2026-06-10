# Follow-up Backlog — ai-spaarke-ai-workspace-UI-r1

> **Created**: 2026-06-10 (operator testing feedback after Wave 1+2 box-sizing pilots)
> **Owner**: TBD — items below are scope-creep candidates from this project's
> testing feedback that need their own home (separate project, smart-todo-r4,
> permission/ops work, etc.).
> **Status**: For triage. None are blocking this PR; this PR ships the
> box-sizing audit cleanup + iter-2 work it was originally scoped for.

The 11-round DataGrid layout saga and the box-sizing audit revealed bigger
UX/architecture work that the operator wants standardized. Capturing here so
the items aren't lost.

---

## 1. Modal-preview record-open standard for ALL dataset grids — HIGH

**Operator quote (paraphrased)**: *"For dataset grids (whether code page or
widget embedded) we are adopting new standard UI/UX for opening records to
use modal (entity main view — unless a specific exception like Documents that
use the preview modal or To Do that uses the Kanban code page); should have
the browse records feature."*

### What this means concretely

- Today: clicking a row's primary-name cell in a DataGrid calls
  `Xrm.Navigation.openForm(...)` (or equivalent) which **navigates the user
  to a new tab** (the entity's main form in MDA).
- Desired: clicking a row should open the record in an in-page MODAL
  showing the entity main view, with a header carrying:
  - **Browse records** — `<` / `>` chevrons to walk the result set without
    closing the modal (matches the screenshot the operator shared from the
    Document Preview / Semantic Search PCF).
  - **Toolbar dropdown** — common actions (Refresh, Save, Delete, …).
- Exceptions are intentional:
  - **Documents**: use the EXISTING document-preview modal (from
    SemanticSearchControl PCF) instead of the main form.
  - **To Do**: open in the SmartTodo Kanban Code Page instead of a modal.

### What's in the codebase today

- DataGrid framework's `rowOpen` config supports a few row-open strategies
  (`'main-form'` opens via `Xrm.Navigation.openForm`, custom handler IDs for
  bespoke flows). See
  [`src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts`](../../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts)
  `RowOpenConfig`.
- The Document Preview modal (SemanticSearchControl PCF reference) exists at
  [`src/client/pcf/SemanticSearchControl/`](../../../src/client/pcf/SemanticSearchControl/)
  — has the browse-records chevron behavior.

### What needs to happen

- Design: a new `rowOpen` action type — say `'modal'` — that mounts an
  in-page modal hosting the entity main form via `Xrm.Navigation.navigateTo`
  with `target: 2`, OR an in-React Dialog hosting a form iframe.
- Implementation: shared `EntityRecordModal` component in
  `@spaarke/ui-components` with a record-browse adapter that reads the
  current DataGrid's record list and supports `<` / `>` navigation.
- Per-surface opt-in via the `sprk_gridconfiguration` `rowOpen` field.
- Documents-specific exception path: route Documents grids to the existing
  preview-modal (refactor the SemanticSearchControl PCF preview component out
  to a shared lib, or wire the existing component directly).
- Apply across all grids: Projects/Matters/Invoices/WorkAssignments
  workspace widgets + AllDocuments standalone + Reporting.

### Estimated scope

- Design: 1-2 days.
- Implementation: 3-5 days.
- Refit across surfaces: 1 day.

This is its own project. Recommended title:
`dataset-grid-modal-preview-r1`.

---

## 2. Documents widget "New" → Upload wizard (not OOB main form) — MEDIUM

**Operator quote**: *"the 'New' should open the UploadDocument wizard
(currently going to the OOB new main form)."*

### Why this didn't ship in this PR

Initially classified as a quick fix. On inspection, it requires THREE
coordinated changes:

1. **Custom command handler registration** in SpaarkeAi/main.tsx (or
   LegalWorkspaceApp's mount path) — calls
   `registerCommandHandler('openDocumentUploadWizard', ...)` from
   `@spaarke/ui-components`. The handler needs access to the parent entity
   context (Matter, Project, …) to pass through to the wizard via the data
   envelope.

2. **DataGrid framework — parent context exposure to handlers**. The
   `CommandBarConfig.primary[].customHandlerId` route is documented but the
   handler's invocation context surface needs verification — does it receive
   `parentContext` from the host? If not, that's a small framework
   adjustment.

3. **sprk_gridconfiguration JSON patch** on the Documents row
   (`1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` in spaarkedev1):
   ```json
   {
     "source": { ... },
     "display": { ... },
     "commandBar": {
       "showDefaultCommands": { "newRecord": false },
       "primary": [{
         "id": "upload",
         "label": "Upload",
         "icon": "ArrowUpload24Regular",
         "action": "custom",
         "customHandlerId": "openDocumentUploadWizard"
       }]
     },
     "_version": "1.0"
   }
   ```

### Recommended owner

Part of `dataset-grid-modal-preview-r1` since both relate to the Documents
widget standardization, OR a small targeted PR after design clarifies the
custom-handler/parent-context contract.

---

## 3. Find Similar Documents — auth + 403 — HIGH (operational)

**Operator console**: `GET /api/ai/visualization/related/...` → 403 Forbidden
with body "Insufficient permissions (Read access required)".

### Root cause

[`src/server/api/Sprk.Bff.Api/Api/Filters/VisualizationAuthorizationFilter.cs`](../../../src/server/api/Sprk.Bff.Api/Api/Filters/VisualizationAuthorizationFilter.cs)
extracts `oid` from claims and calls
`IAiAuthorizationService.AuthorizeAsync(documentId)` which checks Dataverse
`RetrievePrincipalAccess` for **read access on the source document**. The
calling user (you, in the test) lacks that permission for the specific
document being queried (`63406620-1232-4fcb-b77d-b736eab9bd85`).

### Not a code bug

This is a Dataverse-side permission setup issue. The Dataverse application
user / your test user needs:
- Read access on `sprk_document` for the records being queried, OR
- The correct security role that grants Read on `sprk_document` org-wide
  for the test environment.

### Secondary observation — login prompt

The console log shows `[SpaarkeAuth] Token acquired via
in-memory-cache(browser-msal)` BEFORE the call — auth IS being acquired. The
"login prompt" the operator mentioned is likely a separate iframe MSAL flow
from `sprk_documentrelationshipviewer` (the Find Similar Code Page wraps in
an iframe per the console URLs). Worth investigating whether the
`@spaarke/auth` cache is being shared across the iframe boundary or whether
the iframe is doing its own MSAL bootstrap. If the latter and it can hit a
silent-auth path consistently, the prompt should be a one-time-per-session
event at worst.

### Recommended owner

- Permission grant: operator (Dataverse admin) — should be straightforward.
- Iframe auth investigation: probably a small bug in
  DocumentRelationshipViewer's bootstrap; could be a one-line fix after
  reading
  [`src/client/code-pages/DocumentRelationshipViewer/src/index.tsx`](../../../src/client/code-pages/DocumentRelationshipViewer/src/index.tsx)
  + comparing to ADR-028's auth contract.

---

## 4. CreateTodoWizard — placement / discoverability — MEDIUM

**Operator question**: *"this doesn't launch from command bar — where do I
launch this? Also the wizard should be added to the Quick Start spaarke.ai
context widget area."*

### Today's wiring

The wizard IS wired to a ribbon button on **sprk_matter forms**
([`src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml`](../../../src/solutions/spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml))
calling `Spaarke.Commands.Wizards.openCreateTodoWizard`. The DisplayRule is
`<FormStateRule State="Existing" />` — button hidden until the Matter is
saved (intentional). The ribbon may or may not be published in the operator's
environment.

### What's actually being asked

The "Quick Start spaarke.ai context widget area" — i.e., add CreateTodo as a
launchable action on the SpaarkeAi home pane's Quick Start widget (the
`get-started.registration.ts` content pane in LegalWorkspace). Today the
Quick Start widget shows entity-specific action cards (CreateMatter,
CreateProject etc.). Adding CreateTodo there is straightforward:

1. Locate the action-cards array in
   [`src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts`](../../../src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts).
2. Add a new card invoking `openCreateTodoWizard` (the same dispatcher used
   from the ribbon webresource).
3. Add a card icon, title, description.
4. Verify ribbon webresource is published in spaarkedev1.

Estimated 30-45 min including a SpaarkeAi rebuild + deploy.

### Recommended owner

- Quick Start card addition: this project (small fast-follow PR) or
  smart-todo-r4 if it makes sense to bundle with other SmartTodo work.
- Ribbon publish verification: operator.

---

## 5. Summarize Files Wizard — Email 400 + Project missing fields — HIGH (bugs)

**Operator report**: *"for the Summarize Files wizard, the summarize step
worked; selected added steps email and create project; for email sending,
got '400 error'; for related Project was created but no 'Project Number' and
no Documents / Files."*

### Two distinct bugs

#### 5.1 Email step → 400

`POST /api/communications/send` (or equivalent) returning 400. Need:
- Operator to capture the response body (400 should carry a ProblemDetails
  with the validation error).
- BFF endpoint inspection — see
  [`src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs`](../../../src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs)
  (or wherever the send route lives) for required fields.

Likely cause: the wizard's email payload is missing a required field
(subject, body, recipients, related-entity refs). The wizard's email step
construction in
[`src/solutions/SummarizeFilesWizard/`](../../../src/solutions/SummarizeFilesWizard/)
needs auditing.

#### 5.2 Create Project → missing Project Number + no linked Documents/Files

Two sub-symptoms:
- Project Number missing — likely an autonumber attribute not configured on
  `sprk_project` in the target env, OR the wizard's create-project payload
  isn't including the trigger for autonumber population.
- No Documents/Files linked — the wizard should be associating the
  newly-created project with the documents the user summarized. The
  association payload (likely a `sprk_document_project` N:N or a regarding
  lookup) is missing or failing silently.

### Recommended owner

- Both bugs need investigation in `src/solutions/SummarizeFilesWizard/` and
  the BFF endpoint(s). Each is probably <2 hours but they're independent.
- Likely the AI Document Intelligence project or its own small bugfix PR.

---

## 6. 17-host box-sizing reset backfill — DONE (Wave 1+2 deployed; Wave 3
batched into this PR)

Audit from prior commit `976871e3` flagged 17 surfaces missing the reset.
This PR's Wave 1 (AllDocuments + Reporting), Wave 2 pilot
(CreateTodoWizard), and Wave 3 batch (12 more) closes that audit. The new
`scripts/check-html-css-reset.mjs` build-gate prevents future regressions on
surfaces that opt into it (currently SpaarkeAi + DailyBriefing + WLW;
others opt in via their package.json's `build` script).

No follow-up needed beyond redeploying Wave 3 surfaces via their existing
deploy scripts when convenient.

---

## Cross-cutting follow-up — CI gate adoption

The new `scripts/check-html-css-reset.mjs` is currently invoked from 3
surface `package.json` build scripts. For maximum prevention:

- **Option A** — wire it into a top-level CI job in
  [`.github/workflows/`](../../../.github/workflows/) that runs the check
  against every surface's `index.html` on every PR. Catches the omission
  even when a surface adds itself without opting into the per-package gate.
- **Option B** — extend each surface's `package.json` `build` script as we
  touch them. Slow burn, but doesn't add CI complexity.

Either is fine. Operator preference TBD.
