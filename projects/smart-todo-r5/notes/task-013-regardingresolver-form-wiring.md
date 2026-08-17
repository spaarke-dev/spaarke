# Task 013 — RegardingResolver wiring on the `sprk_todo` main form (FR-04)

> **Date**: 2026-08-16 · opus/xhigh · FULL rigor · prescriptive steps.
> **Status**: **COMPLETE (2026-08-16)** — all 4 acceptance criteria met. Criteria 1–3 were already present from R4; **criterion 4 (presave OnLoad handler) was registered + published via task 014's solution-import deploy** (Option B, per operator). See `task-014-deploy-and-smoke.md` for the deploy mechanism + live verification. Negatives hold: no AssociationResolver introduced; no PCF source modified.

## RESOLUTION (2026-08-16)
`Spaarke.SmartTodo.RegardingPreSave.onLoad` (lib `sprk_todo_regarding_presave`) is now registered as an OnLoad handler on the live To Do main form (`passExecutionContext=true`), verified via Web API GET. It self-wires the OnSave bridge via `addOnSave`. NOTE: direct Web API PATCH of `systemform` is a silent no-op in spaarkedev1 — registration went through a `pac` solution export/edit/import roundtrip (documented in the 014 note; relevant to the Phase-4 form tasks 030/031/032).

## Target form (live)
- **Name**: `To Do main form`
- **formid**: `eca59df4-1364-f111-ab0c-7ced8ddc4cc6`
- **objecttypecode**: 10946 (`sprk_todo`)
- Confirmed via `systemform` query (2026-08-16).

## Step 1 — schema confirmation (no gap → escalation NOT triggered)
`describe tables/sprk_todo` confirms **every** required attribute exists on the entity:
- **5 denormalized fields**: `sprk_regardingrecordtype` (lookup→`sprk_recordtype_ref`), `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber`, `sprk_regardingrecordurl`.
- **12 write-catalog lookups** (per `TodoRegardingUpdateBuilder.ts` `TODO_REGARDING_CATALOG`): matter, project, event, communication, workassignment, invoice, budget, analysis, organization, contact, document, reportcard.
- `sprk_regardingservicerequest` also exists on the entity but is **not** a write target (not in the 12-entry catalog) — correctly excluded.

## Step 2 — live form inventory
Read the full `formxml`. Findings:

| Acceptance criterion | Result |
|---|---|
| **1.** RegardingResolver bound to `sprk_regardingrecordtype`, `entity`="sprk_todo" | ✅ **MET** — `customControl name="sprk_Spaarke.Controls.RegardingResolver"` present for all 3 formFactors (0/1/2) on the cell `uniqueid {ead3c715-10ec-44cf-895a-1ecbf90a09f2}` in section `General_section_relatedrecord`. Params: `regardingRecordType=sprk_regardingrecordtype`, `entity` static="sprk_todo", `regardingRecordNumberField=sprk_regardingrecordnumber`, `regardingRecordNameField=sprk_regardingrecordname`, `title="RELATED RECORD"`. |
| **2.** 5 denormalized fields present (visible or hidden) | ✅ **MET** — all 5 on the form; the resolver text/URL trio is present as hidden (`visible="false"`) cells in `General_section_relatedrecord`; `sprk_regardingrecordid` present in `general_relatedrecord`. |
| **3.** 12 catalog lookups present | ✅ **MET** — all 12 present as `visible="false"` cells in the `general_relatedrecord` section. |
| **4.** `sprk_todo_regarding_presave.js` registered as OnSave handler | ❌ **GAP** — the form's `<events>` block registers only ONE handler: `Spaarke.SmartTodo.RegardingRecordNumberHyperlink.onLoad` (library `sprk_regardingrecordnumber_hyperlink`). No presave library, no entry point for `Spaarke.SmartTodo.RegardingPreSave`. |

**Webresource deployment confirmed**: `sprk_todo_regarding_presave` (webresourceid `2ae21d81-f987-4d98-b1ff-3ba6694e3b21`, type 3 / JScript) IS deployed in the environment — so the handler can be registered; the library exists.

## The correct registration (per the webresource's own contract, lines 55–61)
Register an **OnLoad** handler (NOT OnSave directly — the script wires OnSave itself via `addOnSave`):
- **Event**: OnLoad (form: To Do main form)
- **Library**: `sprk_todo_regarding_presave`
- **Function**: `Spaarke.SmartTodo.RegardingPreSave.onLoad`
- **Pass execution context as first parameter**: **Yes** (required — the function calls `executionContext.getFormContext()`)
- Do **not** also wire OnSave in the designer.

## Why I did not raw-edit the formxml via MCP
1. **No publish path** — the Dataverse MCP surface exposes `update_record` but no `PublishXml`/publish-customizations. A `systemform` update would be saved-but-unpublished → not live, not verifiable.
2. **`active="false"` on the existing onload event** — injecting into that block risks the handler silently never firing; no way to visually verify on a production main form.
3. Production main form; the maker-portal Events UI produces correct XML and auto-publishes.

## Remaining step (one of):
- **(A) Maker portal (2 min, recommended)**: make.powerapps.com → sprk_todo → Forms → *To Do main form* → Events → Form Libraries: add `sprk_todo_regarding_presave` → Event Handlers: Event=OnLoad, Library=`sprk_todo_regarding_presave`, Function=`Spaarke.SmartTodo.RegardingPreSave.onLoad`, ✅ *Pass execution context as first parameter* → Save & Publish. Then task 013 criterion 4 is met.
- **(B) Fold into task 014** (`Deploy schema+form; real-DV resolver smoke`) — 014's deploy flow (pac solution import `--publish-changes` / maker) can register + publish the handler alongside the real-DV smoke that actually validates the CREATE-mode bridge.

## Note on when the presave matters
The presave bridge is only exercised on **CREATE via the OOB form** (formType===1). Today `+ New Task` opens `CreateTodoWizard`; FR-10 (task 030) swaps it to the OOB main form — at which point the presave becomes load-bearing. 013 gates 030, so the registration must land before Phase 4. UPDATE-mode regarding edits already work via the PCF's `webApi.updateRecord` path (no presave needed).

## Original `<events>`/`<formLibraries>` (rollback reference — pre-change state)
```xml
<events><event name="onload" application="false" active="false"><Handlers><Handler functionName="Spaarke.SmartTodo.RegardingRecordNumberHyperlink.onLoad" libraryName="sprk_regardingrecordnumber_hyperlink" handlerUniqueId="{2fb8a5c1-e246-4bc9-ae19-b427899df1f4}" enabled="true" parameters="" passExecutionContext="true" /></Handlers></event></events>
<formLibraries><Library name="sprk_regardingrecordnumber_hyperlink" libraryUniqueId="{3185a16b-199d-45e1-a3f7-be4377c0fa3b}" /></formLibraries>
```
