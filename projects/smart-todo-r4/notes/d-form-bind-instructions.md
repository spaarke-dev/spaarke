# D — Bind RegardingResolver PCF to the To Do main form

> **Task**: R4-051 (smart-todo-r4)
> **Date**: 2026-06-10
> **Workstream**: D (regarding resolver)
> **Status**: ready for live-deploy in spaarkedev1
> **Predecessor**: R4-050 (PCF implementation — `Spaarke.Controls.RegardingResolver` v1.0.0 shipped)

This document is the maker checklist for binding the `Spaarke.Controls.RegardingResolver` virtual PCF (built in task R4-050) to the OOB MDA To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`, registering the companion pre-save JS Web Resource, and verifying smoke + NFR-04 in spaarkedev1.

The form-designer work itself is a live action the user performs in spaarkedev1 — this checklist captures the exact steps. R4 source code does NOT change after this task lands (the JS Web Resource is new but the PCF is unchanged).

---

## Summary of deliverables in this task

| Artifact | Path | Status |
|---|---|---|
| RegardingResolver PCF | `src/client/pcf/RegardingResolver/` | Shipped in R4-050 (v1.0.0) |
| Pre-save JS Web Resource | `src/client/webresources/js/sprk_todo_regarding_presave.js` | NEW (this task) |
| Form-binding checklist (this doc) | `projects/smart-todo-r4/notes/d-form-bind-instructions.md` | NEW (this task) |
| Solution wrapper | (deferred to R4-092 "deploy" task) | Deferred |

---

## Why a pre-save handler is needed (one-paragraph context)

The PCF writes all 5 polymorphic-regarding fields via `Xrm.WebApi.updateRecord` through the shared `applyResolverFields` (ADR-024, FR-21). On an **existing** record this works cleanly — the PCF has a host GUID and persists immediately. On a **new** record (CREATE form), the PCF cannot call `updateRecord` because the row doesn't exist yet; it computes the payload but defers persistence. The bound `regardingRecordType` lookup output IS propagated to the form natively via `notifyOutputChanged()`. The other 4 fields (`sprk_regarding<X>` lookup, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`) need a bridge so they ride the form's INSERT transaction. The pre-save handler mirrors the `AssociationResolver` precedent of calling `Xrm.Page.getAttribute(fieldName).setValue(value)` to stage values into the form's pending-attribute buffer.

Per the new contract, the RegardingResolver PCF SHOULD surface a pending payload on `window.__sprk_regarding_pending__` so the handler can pick it up. If the seam is empty (older PCF bundle, or no selection made), the handler is a no-op — the form save proceeds normally.

> **Carry-forward to a follow-up task** (NOT this one): a small enhancement to `RegardingResolver/RegardingResolverApp.tsx` SHOULD populate `window.__sprk_regarding_pending__` from `handleSelectRecord` on new records (when `getHostRecordId()` returns undefined). This is out of scope for R4-051 per the brief ("DO NOT modify the RegardingResolver PCF itself"). On UPDATE records, no change is needed — `applyResolverFields` already persists directly. See "Open notes for follow-up" at the bottom of this doc.

---

## 1. Verify (or add) the hidden `sprk_regardingrecordtype` field

The PCF is bound to the host entity's `sprk_regardingrecordtype` lookup field (a lookup to `sprk_recordtype_ref`). This field MUST exist on the To Do main form before the PCF can be added.

**Steps**:

1. In spaarkedev1, open the To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` in the modern form designer (`make.powerapps.com` → Tables → To Do → Forms → Information).
2. In the **Table columns** panel, search for `sprk_regardingrecordtype`.
3. If the column is **NOT** on the form yet:
    - Drag `Regarding Record Type` (the `sprk_regardingrecordtype` lookup) onto the form in a hidden tab/section, OR add it visibly first then hide it after binding.
    - Open the field properties → **Display** → uncheck **Display label on the form**.
    - Open the field properties → **Display** → check **Hide**.
4. Verify the field type is `Lookup` and the referenced entity is `sprk_recordtype_ref`. If not, escalate — the resolver pattern requires this exact target.
5. Confirm the field appears in the form tree as a hidden lookup before proceeding.

---

## 2. Bind the RegardingResolver PCF to `sprk_regardingrecordtype`

1. Select the hidden `sprk_regardingrecordtype` field in the form tree.
2. In the right-hand panel, click **Components** → **+ Component**.
3. Search for `Regarding Resolver` (display name) or `Spaarke.Controls.RegardingResolver` (namespace). It should appear under **Code components** if the `RegardingResolver` managed solution from task R4-050 is imported in spaarkedev1.
4. Click **Add**. The control properties panel opens.
5. Set the input properties:

| Property | Value | Notes |
|---|---|---|
| `regardingRecordType` (bound) | (auto — bound to `sprk_regardingrecordtype`) | The discriminator lookup. |
| `entity` (input) | `sprk_todo` | FR-22 reusability lever. Per-form configuration only. |
| `regardingTargets` (input) | (leave empty for default 11-entity catalog) | OR comma-separated subset, e.g. `sprk_matter,sprk_project,sprk_event,sprk_workassignment,sprk_invoice`. |
| `readOnly` (input) | (leave unchecked) | Defaults to inheriting `mode.isControlDisabled` per FR-24. |

6. Check **Web** under **Show component on**.
7. Click **Done**.
8. (Optional cosmetics) — unhide the lookup field so the PCF renders visibly: in the field properties, uncheck **Hide**. The PCF replaces the default lookup widget. The label is the field's display name; rename to `Regarding` if needed.
9. Position the field on a visible main-form section (e.g., under the existing Subject / Status fields).

---

## 3. Upload the pre-save JS Web Resource

The web resource file is `src/client/webresources/js/sprk_todo_regarding_presave.js`.

**Steps**:

1. In `make.powerapps.com`, open the target solution (the same solution that will hold the form changes — see §6 for solution composition).
2. **+ New** → **Web resource** → **JavaScript (JS)**.
3. Fill in:
    - **Display name**: `SmartTodo — Regarding Pre-Save`
    - **Name**: `sprk_/scripts/smarttodo_regarding_presave.js` (publisher prefix `sprk_` + path convention used by other Spaarke scripts e.g. `sprk_/scripts/kpiassessment_quickcreate.js`)
    - **Description**: `Pre-save bridge for the RegardingResolver PCF — stages all 5 resolver fields onto new-record CREATE transactions. See projects/smart-todo-r4/notes/d-form-bind-instructions.md.`
    - **File**: upload `src/client/webresources/js/sprk_todo_regarding_presave.js`.
4. Save and Publish the web resource.

---

## 4. Register the OnLoad handler on the To Do main form

The JS Web Resource registers its own `OnSave` handler programmatically via `addOnSave` during `OnLoad` — only the OnLoad entry point needs to be wired in the form designer.

**Steps**:

1. Re-open the To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` in the form designer.
2. Open **Form properties** (right-hand panel — gear icon).
3. **Events** tab → **Form Libraries** → **+ Add library** → select the web resource `sprk_/scripts/smarttodo_regarding_presave.js` (display name: `SmartTodo — Regarding Pre-Save`).
4. **Events** tab → **Event Handlers** → choose **OnLoad** → **+ Event Handler**.
5. Configure the handler:
    - **Library**: `sprk_/scripts/smarttodo_regarding_presave.js`
    - **Function**: `Spaarke.SmartTodo.RegardingPreSave.onLoad`
    - **Enabled**: checked
    - **Pass execution context as first parameter**: **checked** (mandatory — the handler dereferences `executionContext.getFormContext()`)
6. Click **Done**.

> **DO NOT** also wire an OnSave handler manually in the form designer. The handler self-registers via `addOnSave` to keep the registration centralized.

---

## 5. Publish + smoke test

1. Click **Save** then **Publish** on the form.
2. Click **Publish all customizations** at the solution level (top of solution view).
3. Open the To Do main form in a browser (model-driven app → To Do entity → New Record).
4. **Smoke A — selection + save persists 5 fields**:
    - In the resolver, pick `sprk_matter` from the dropdown → click **Select Record**.
    - Pick a real Matter from the lookup dialog.
    - The PCF shows "Associated with {matter name}".
    - Click **Save** on the form ribbon.
    - Reload the record.
    - Open the form's advanced find (or `Xrm.WebApi.retrieveRecord` from the console) and confirm ALL 5 fields are set:
        - `sprk_regardingrecordtype` (lookup to sprk_recordtype_ref) — populated
        - `sprk_regardingmatter` (lookup to sprk_matter) — populated
        - `sprk_regardingrecordid` (string) — populated
        - `sprk_regardingrecordname` (string) — populated
        - `sprk_regardingrecordurl` (URL) — populated
    - Console check (run in browser dev tools while on the saved record):
        ```javascript
        Xrm.WebApi.retrieveRecord("sprk_todo", Xrm.Page.data.entity.getId().replace(/[{}]/g, ""),
          "?$select=sprk_regardingrecordid,sprk_regardingrecordname,sprk_regardingrecordurl,_sprk_regardingmatter_value,_sprk_regardingrecordtype_value"
        ).then(r => console.table(r));
        ```
5. **Smoke B — update an existing record**:
    - Open a saved To Do (regarding empty).
    - Pick a Project. Confirm the PCF persists immediately (no form save needed — `applyResolverFields` writes via `updateRecord`).
    - Reload. Confirm all 5 fields persisted.
6. **Smoke C — clear selection**:
    - Open a saved To Do (regarding set).
    - Click **Clear** in the PCF.
    - Reload. Confirm all 5 fields are null (FR-13 mutual-exclusivity verifies via the clear-all-15-fields path inside `clearRegarding`).
7. **Smoke D — switch regarding entity type**:
    - Open a saved To Do regarding a Matter.
    - In the PCF, change Record Type to `sprk_project`, pick a Project.
    - Reload. Confirm `sprk_regardingmatter` is now null and `sprk_regardingproject` is set (mutual-exclusivity).

---

## 6. NFR-04 verification — hybrid modal iframe propagation

The smart-todo-r4 hybrid modal (`<RecordNavigationModalShell>` + iframe-embedded OOB MDA form, task R4-040) renders the OOB To Do main form inside an iframe. NFR-04 requires that form-designer changes (this task) propagate WITHOUT any R4 source change.

**Verification steps** (live):

1. Hard-refresh the SmartTodo Code Page in the same model-driven app.
2. Click a To Do card → the hybrid modal opens with the OOB form embedded in an iframe.
3. **Expected**:
    - The RegardingResolver PCF renders inside the modal iframe (same UX as the main form).
    - Selecting a record and saving inside the iframe persists all 5 fields.
    - No R4 source code was changed to enable this — the iframe pulls the live form definition from Dataverse, which includes the freshly-published PCF binding + OnLoad handler.
4. **PASS criterion**: Resolver works inside the modal iframe with no R4 React/TypeScript source change required.

This validates the hybrid modal architectural choice (OD-1, design.md).

---

## 7. Read-only mode verification (FR-24)

1. Sign in as a user with a read-only role on `sprk_todo` (or open a record in a state where the form is `mode.isControlDisabled === true`).
2. Open the To Do record.
3. **Expected**: the PCF renders the read-only variant (clickable link to the regarding parent record + label "Regarding:"); no Dropdown, no Select Record button, no Clear button.

---

## 8. Solution composition + deploy order

R4-051 does NOT generate solution wrappers — solution authoring is deferred to **R4-092** (deploy task). The user manually authors / updates the following solutions in spaarkedev1 for this task's smoke test:

| Solution | Contains | Imported in order |
|---|---|---|
| `RegardingResolver` (managed) | `Spaarke.Controls.RegardingResolver` PCF | 1st — must be present before the form references it. |
| `SmartTodoWebResources` (managed or unmanaged) | `sprk_/scripts/smarttodo_regarding_presave.js` web resource | 2nd — must be present before the form references it as a library. |
| `SmartTodoFormConfig` (managed or unmanaged) | The To Do main form with PCF binding + OnLoad library reference | 3rd — pulls the PCF + web resource references. |

> **Why this order?**
> Dataverse rejects solution imports that reference missing components. The PCF binding and web-resource library reference both fail validation if their target components aren't yet in the target environment.
>
> **Recommended**: For routine env-to-env promotion, package all three into a single Spaarke `SmartTodo` master solution (with PCF + web resource + form-config inside) imported as a single transaction. The 3-solution split above is only relevant when the PCF and web resource are owned by separate teams.

---

## 9. Rollback procedure

If the form binding causes issues (PCF fails to render, save errors, etc.) in spaarkedev1:

1. **Quick fix (5 min)**: open the form designer, navigate to the `sprk_regardingrecordtype` field's component panel, remove the `Spaarke.Controls.RegardingResolver` component (revert to the default lookup widget). Remove the OnLoad event handler. Save + publish.
2. **Full rollback (form)**: import the previous version of the `SmartTodoFormConfig` (or equivalent) solution to restore the prior form definition.
3. **Pre-save script issue isolation**: the handler is designed to be a no-op on errors (every callback wraps `try/catch` and logs only — it NEVER blocks the form save). If the script itself throws on load, only the OnLoad handler-registration fails; the form save still works (the PCF still propagates `sprk_regardingrecordtype` natively, but the 4 companion fields will be empty on CREATE — exactly the pre-handler behavior).
4. **PCF binary issue**: the PCF v1.0.0 is solution-portable; rollback by re-importing the previous version's solution. If no previous version exists (R4-050 was the first ship), document the issue and re-publish a fix.

---

## Open notes for follow-up tasks

### For R4-052 (read-only mode)

- The PCF's `resolveReadOnly()` in `RegardingResolverHost.tsx` already handles `context.mode.isControlDisabled` AND the explicit `readOnly` manifest input. Task 052 should:
    - Verify the PCF renders the read-only variant (no Dropdown / Select Record / Clear) when the user's security role lacks Write on `sprk_regardingrecordtype`.
    - Verify the OOB form-level `mode.isControlDisabled` (e.g., on a deactivated record) propagates to the PCF.
- No R4-051 deliverable affects read-only behavior — the JS Web Resource is a no-op on read-only forms (no save can occur).

### For R4-081 through R4-084 (Visual Host on parent forms — Matter / Project / Invoice / WorkAssignment)

- Those tasks add the SmartTodo Visual Host to PARENT entity forms (not the To Do main form). The Visual Host displays related To Do records; it does NOT use the RegardingResolver.
- The Visual Host opens the SmartTodo Code Page modal (FR-34) with filter context — drill-through to a specific To Do uses the hybrid modal from R4-040, which embeds the OOB form. The form contract from R4-051 (PCF binding + OnLoad handler) flows transparently through that modal per NFR-04 §6 above.
- **Implication for 081-084**: no additional form-script work is needed; the modal iframe inherits the To Do form's PCF + handler bindings automatically. The Visual Host on parent forms doesn't need the pre-save handler because it doesn't host the To Do form directly.

### For the RegardingResolver PCF (out-of-band enhancement, NOT R4)

A small follow-up should populate `window.__sprk_regarding_pending__` from the PCF after a successful `applyRegardingSelection` when `getHostRecordId()` returns undefined (CREATE mode). Sketch:

```typescript
// In RegardingResolverApp.tsx handleSelectRecord, after applyRegardingSelection returns success:
if (!writeCtx.hostRecordId) {
  // CREATE mode — the payload was computed but not persisted by webApi.updateRecord.
  // Surface it for the form's OnSave handler to stage via getAttribute().setValue().
  (window as any).__sprk_regarding_pending__ = {
    hostEntity,
    entityType: selection.entityType,
    entitySet: result.catalogEntry?.entitySet,
    lookupAttribute: result.catalogEntry?.lookupAttribute,
    recordId: selection.recordId,
    recordName: selection.recordName,
    recordUrl: /* derived via buildRecordUrl */,
  };
}
```

Without this enhancement, the pre-save handler is a defensive no-op on new records — meaning the 4 companion fields are still empty after the first save, and a subsequent edit (UPDATE mode) is required for them to populate. This is acceptable for spaarkedev1 smoke test if the user re-saves once.

### Deploy notes (PR description content for NFR-09)

This task's deployed surfaces:

- **NEW** — JS Web Resource `sprk_/scripts/smarttodo_regarding_presave.js` (in a `SmartTodoWebResources` solution or the broader SmartTodo deployment solution)
- **MODIFIED** — To Do main form `eca59df4-1364-f111-ab0c-7ced8ddc4cc6` (PCF binding + OnLoad library reference)
- **UNCHANGED** — RegardingResolver PCF v1.0.0 (shipped in R4-050)
- **UNCHANGED** — `PolymorphicResolverService.applyResolverFields` (the canonical write path per ADR-024)
- **UNCHANGED** — R4 TypeScript / React source (NFR-04 invariant)

Solution import order (per §8): RegardingResolver PCF → SmartTodoWebResources → SmartTodoFormConfig.

After deploy: hard-refresh the model-driven app + the SmartTodo Code Page; run smokes A/B/C/D from §5 + the NFR-04 check from §6.

---

*End of D form-bind checklist. Task R4-051 complete when this checklist has been executed in spaarkedev1 and all four smokes pass.*
