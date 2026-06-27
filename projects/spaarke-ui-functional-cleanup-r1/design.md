# Spaarke UI Functional Cleanup — R1

> **Status**: Discovery (2026-06-11). Tracks the deferred-but-substantive items surfaced during `ai-spaarke-ai-workspace-UI-r1` operator testing that fell outside that PR's scope.
> **Branch**: TBD (likely create a worktree when work starts).
> **Predecessor**: `projects/ai-spaarke-ai-workspace-UI-r1/notes/followup-backlog.md` §1–§5. This project formalizes those follow-ups.
> **Purpose**: Close the highest-value usability + functional gaps the operator hit during 2026-06-10 testing. Four items, three HIGH and one MEDIUM.

---

## 1. Scope

### In scope (4 items)

1. **Modal-preview record-open standard** for ALL dataset grids (HIGH)
2. **Documents widget "New" → Upload Wizard** (depends on #1's custom-handler contract)
3. **Find Similar Documents — 403 + iframe login prompt** (HIGH — split operator-side permission grant from code-side iframe auth audit)
4. **Summarize Files Wizard — Email step 400 + Project create missing fields** (HIGH — two distinct bug investigations)

### Out of scope

- The full DataverseEntityViewWidget framework migration (that's done in `ai-spaarke-ai-workspace-UI-r1` Batch 2 — deployed as registrations; this project consumes the framework).
- BFF endpoint redesign for Find Similar (already exists; this project audits the auth flow only).
- New analytics, new dashboards, new playbook authoring — confine to the four items above.

---

## 2. Item 1 — Modal-preview record-open standard for dataset grids

### Operator quote (paraphrased, from `followup-backlog.md` §1)

> *"For dataset grids (whether code page or widget embedded) we are adopting new standard UI/UX for opening records to use modal (entity main view — unless a specific exception like Documents that use the preview modal or To Do that uses the Kanban code page); should have the browse records feature."*

### Current state

Row-click on a `DataGrid` cell calls `Xrm.Navigation.openForm(...)` which navigates the user to a new tab (the entity's main form in MDA).

### Target state

Click-to-open opens an in-page modal hosting the entity main view, with header carrying:
- **Browse records** — `<` / `>` chevrons to walk the result set without closing the modal (matches the existing Document Preview modal in SemanticSearchControl PCF).
- **Toolbar dropdown** — Refresh / Save / Delete / common actions.

### Intentional exceptions

- **Documents grid** — open the existing document-preview modal (from SemanticSearchControl PCF), not the entity main form.
- **To Do grid** — open in SmartTodo Kanban Code Page, not a modal.

### Technical approach

- **Design new `rowOpen` action type** `'modal'` in `RowOpenConfig` (per `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts`). Either:
  - Calls `Xrm.Navigation.navigateTo(...)` with `target: 2` (Dataverse modal)
  - OR mounts an in-React `Dialog` hosting a form iframe (full control over chevrons + toolbar)
- **Implement `EntityRecordModal`** in `@spaarke/ui-components` — record-browse adapter reads the current `DataGrid`'s record list and supports `<` / `>` navigation.
- **Per-surface opt-in** via the `sprk_gridconfiguration.rowOpen` field. Default `'main-form'` (current behavior) for backward compat; opt-in to `'modal'` per grid record.
- **Documents-specific exception path** — route Documents grids to the existing preview-modal (refactor the SemanticSearchControl PCF preview component out to `@spaarke/ui-components`, or wire the existing component directly).
- **Roll-out across grids**: Projects, Matters, Invoices, WorkAssignments, AllDocuments, Reporting.

### Estimated scope

- Design: 1–2 days
- Implementation (`EntityRecordModal` + `rowOpen: modal` wiring): 3–5 days
- Refit + per-surface config: 1 day
- Total: ~7–10 days as its own focused work-stream.

---

## 3. Item 2 — Documents widget "New" → Upload Wizard

### Operator quote (from `followup-backlog.md` §2)

> *"The 'New' should open the UploadDocument wizard (currently going to the OOB new main form)."*

### Current state

Clicking "New" on the Documents widget calls the OOB Dataverse `newRecord` command which opens the standard `sprk_document` create form.

### Target state

"New" launches `sprk_documentuploadwizard` Code Page (the existing wizard) with the parent entity context (Matter, Project, etc.) passed through.

### Three coordinated changes required

1. **Custom command handler registration** in `SpaarkeAi/src/main.tsx` (or `LegalWorkspaceApp`'s mount path) — call `registerCommandHandler('openDocumentUploadWizard', ctx => {...})` from `@spaarke/ui-components`. Handler accepts `parentContext: { entityName, recordId }` and dispatches `Xrm.Navigation.navigateTo({pageType: 'webresource', webresourceName: 'sprk_documentuploadwizard', data})`.

2. **DataGrid framework — parent context exposure** to custom handlers. The `CommandBarConfig.primary[].customHandlerId` route is documented but the handler's invocation context surface needs verification — does it receive `parentContext` from the host shell? If not, that's a small framework adjustment (3-5 lines + types).

3. **`sprk_gridconfiguration` JSON patch** on the Documents row (in spaarkedev1 the row id is `1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` per `ai-spaarke-ai-workspace-UI-r1` resume doc). Replace the default `newRecord` command:

```json
{
  "commandBar": {
    "showDefaultCommands": { "newRecord": false },
    "primary": [
      {
        "id": "upload",
        "label": "Upload",
        "icon": "ArrowUpload24Regular",
        "action": "custom",
        "customHandlerId": "openDocumentUploadWizard"
      }
    ]
  }
}
```

### Dependency on Item 1

This item exercises the same `customHandlerId` + `parentContext` plumbing as Item 1's modal-open standard. **Recommend doing Item 1 first** so the contract is settled before Item 2 lands.

### Estimated scope

- Handler registration + framework parent-context verification: 0.5 day
- Dataverse config row update + smoke test: 0.5 day
- Total: ~1 day (after Item 1's contract is finalized).

---

## 4. Item 3 — Find Similar Documents — 403 + iframe login prompt

### Operator report (from `followup-backlog.md` §3)

- Console: `GET /api/ai/visualization/related/...` returns **403 Forbidden** with body `"Insufficient permissions (Read access required)"`.
- Login prompt suggesting iframe MSAL bootstrap is not sharing cache.

### Split into 2 sub-items

#### 4.1 — 403 root cause (Dataverse permission, not code bug)

`src/server/api/Sprk.Bff.Api/Api/Filters/VisualizationAuthorizationFilter.cs` extracts `oid` from claims and calls `IAiAuthorizationService.AuthorizeAsync(documentId)` which checks Dataverse `RetrievePrincipalAccess` for **read access on the source document**.

The calling user lacks that permission for the specific document being queried (`63406620-1232-4fcb-b77d-b736eab9bd85`). **Operator action**:
- Grant the test user Read on `sprk_document` for the records being queried, OR
- Assign the correct security role that grants Read on `sprk_document` org-wide for the test environment.

**No code change required for 4.1.**

#### 4.2 — iframe MSAL audit (code-side)

Console log: `[SpaarkeAuth] Token acquired via in-memory-cache(browser-msal)` BEFORE the call — auth IS being acquired in the OUTER shell. The "login prompt" the operator mentioned is likely a separate iframe MSAL flow from `sprk_documentrelationshipviewer` (the Find Similar Code Page wraps in an iframe per the console URLs).

Investigate whether the `@spaarke/auth` cache is being shared across the iframe boundary or whether the iframe is doing its own MSAL bootstrap. Reference contract: [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) INV-1..INV-8 — specifically the cross-iframe cache sharing invariant.

If iframe MSAL bootstrap is independent, fix per ADR-028 — either:
- Pass the parent's token via `postMessage` and skip bootstrap entirely.
- Configure MSAL with `cacheLocation: 'localStorage'` so both contexts read the same shared cache.

Likely a 1-line config fix in `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` after comparing to ADR-028's contract.

### Estimated scope

- 4.1: 15-30 min operator-side (Dataverse permission grant)
- 4.2: 0.5-1 day investigation + fix + verify (Spaarke Auth iframe audit)

---

## 5. Item 5 — Summarize Files Wizard bugs

### Operator report (from `followup-backlog.md` §5)

> *"For the Summarize Files wizard, the summarize step worked; selected added steps email and create project; for email sending, got '400 error'; for related Project was created but no 'Project Number' and no Documents / Files."*

### 5.1 — Email step 400

`POST /api/communications/send` (or equivalent) returning 400.

**First step**: operator captures the 400 response body — should carry a `ProblemDetails` with the validation error name + offending field.

**Then investigate**:
- BFF endpoint inspection — `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` (or wherever the send route lives) for required-field validation.
- Likely cause: the wizard's email payload is missing a required field (subject, body, recipients, related-entity refs). Audit `src/solutions/SummarizeFilesWizard/` step components to find where the payload is constructed.

### 5.2 — Create Project missing fields

Two sub-symptoms:

**Missing Project Number**:
- Likely cause A: the `sprk_projectnumber` autonumber attribute isn't configured on `sprk_project` in the target env. **Operator action**: verify autonumber config.
- Likely cause B: the wizard's create-project payload doesn't include the trigger that fires autonumber population. Some Dataverse autonumber fields require a non-empty placeholder string at create-time. Audit the wizard's `ICreateProjectPayload` construction.

**No Documents/Files linked**:
- The wizard SHOULD be associating the newly-created project with the documents the user summarized.
- Association payload (likely a `sprk_project_document` N:N or a `regarding` lookup on `sprk_document`) is either missing or failing silently. Audit the wizard's post-create step for the `Associate` API call.

### Estimated scope

- 5.1: 2-3 hours (depending on 400 response body content)
- 5.2: 3-4 hours (two sub-bugs to root-cause independently)
- Total: ~1 day

---

## 6. Sequencing

| Order | Item | Why this order |
|---|---|---|
| **1st** | **Item 3.1** (Find Similar 403) | Operator-side; unblocks user testing immediately while code work proceeds. |
| **2nd** | **Item 1** (Modal-preview standard) | Defines the `customHandlerId` + `parentContext` contract that Item 2 reuses. |
| **3rd** | **Item 2** (Documents "New" → wizard) | Consumes Item 1's contract; small (~1 day) once Item 1 lands. |
| **In parallel** (independent) | **Item 3.2** (iframe MSAL audit) | Auth investigation; isolated from grid framework work. |
| **In parallel** (independent) | **Item 5** (Summarize Files bugs) | Wizard-specific bug fixes; isolated from grid framework work. |

Items 1+2 must be sequential. Items 3.2 and 5 can be done by separate workstreams in parallel with 1+2.

---

## 7. Out of scope (explicit non-goals)

- **CalendarSidePane orphan** — already fixed in `ai-spaarke-ai-workspace-UI-r1` followups PR #376.
- **CreateTodoWizard Quick Start card** — already shipped in PR #376.
- **Box-sizing reset CI gate** — already shipped in PR #376 (workflow `.github/workflows/css-reset-gate.yml`).
- **17-host box-sizing reset backfill** — DONE in `ai-spaarke-ai-workspace-UI-r1` Wave 1+2+3.

---

## 8. Acceptance criteria

The project is complete when:

- ✅ Item 1: `'modal'` rowOpen action implemented in DataGrid framework, `EntityRecordModal` shipped in `@spaarke/ui-components`, at least 3 grids (Matters, Projects, AllDocuments) opt-in via `sprk_gridconfiguration` and operator-verified.
- ✅ Item 2: Documents widget "New" launches `sprk_documentuploadwizard` with `parentContext` populated.
- ✅ Item 3.1: Operator-confirmed: 403 no longer surfaces in console for the test user.
- ✅ Item 3.2: Iframe Code Pages share the parent shell's MSAL cache; no second login prompt for the same user in a session.
- ✅ Item 5.1: Email step succeeds end-to-end; 400 root cause documented + fixed.
- ✅ Item 5.2: Create Project produces a record with valid Project Number AND linked source documents.

---

## 9. Related projects + references

- **Predecessor**: `projects/ai-spaarke-ai-workspace-UI-r1/notes/followup-backlog.md` §1, §2, §3, §5.
- **DataGrid Framework**: [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md).
- **DataGrid Framework config guide**: [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md).
- **Auth contract**: ADR-028 + [`.claude/patterns/auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md).
- **SemanticSearchControl preview modal** (reference impl for Item 1's Documents exception): `src/client/pcf/SemanticSearchControl/`.

---

## 10. Open questions (to resolve before implementation)

1. **Item 1**: Should `EntityRecordModal` use Dataverse's native modal (`navigateTo`, target: 2) or an in-React iframe-hosted form? Native is more consistent with MDA conventions; in-React gives full control over chevrons + toolbar.
2. **Item 1**: For the Documents exception, should the preview modal be a NEW component in `@spaarke/ui-components` or should `SemanticSearchControl`'s implementation be hoisted as-is?
3. **Item 5.1**: Is the email validation discrepancy on the BFF side (stricter than the wizard's contract) or on the wizard side (missing required field)? Need the 400 response body to answer.

---

## 11. Next actions

- [ ] Operator: capture the 400 response body for Item 5.1 (browser DevTools Network tab).
- [ ] Operator: grant Read on `sprk_document` to the test user for Item 3.1.
- [ ] Owner TBD: triage Item 1's design (native modal vs in-React) — ~30 min decision.
- [ ] Owner TBD: schedule work-stream split between Items 1+2 (sequential) and 3.2 + 5 (parallel).
