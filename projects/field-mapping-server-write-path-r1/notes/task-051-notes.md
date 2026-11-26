# Task 051 notes - native UI redirect (FR-24)

Branch `task/fm-051`. Code and script only: nothing was imported, uploaded, published or written to any environment. The deploy is the main session's, on the owner's go (steps below).

## 1. What was built

| Piece | Where |
|---|---|
| One list of what the native UI does per table | `infrastructure/dataverse/ribbon/NativeCreateRibbons/native-create-surfaces.json` |
| Idempotent RibbonDiff merge (Assign x3 hide, platform New hide, redirect button) | `.../NativeCreateRibbons/Merge-NativeCreateRibbon.ps1` |
| 23 SpaarkeMaster RibbonDiffs re-authored with it | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_*/RibbonDiff.xml` |
| `sprk_project` quick create 1 to 0 | `.../Entities/sprk_Project/Entity.xml` |
| Shared BFF create `Spaarke.SecureChild.Ribbon.createChild`; redirected tables no longer fall through to the platform New | `sprk_secure_child_ribbon.js` (v1.1.0) and its SpaarkeMaster copy |
| Add Memo and Add KPI create through the BFF, never a quick create form | `sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js` (+ copies); their ribbon commands now load `bff_auth.js`, `assignedaccess_postsave.js`, `secure_child_ribbon.js` |
| Standalone launchers for To Do and New Email | `sprk_wizard_commands.js` (+ SpaarkeMaster copy); registered in `create-launchers.json` |
| `-Verify` | `scripts/Test-NativeCreateSurfaces.ps1` |
| Tests | Jest `secureChildRibbonScript.test.ts` (71), node `ribbonAddMemo.test.mjs` (4 new), Pester `tests/scripts/Test-NativeCreateSurfaces.Tests.ps1` (15) |

## 2. Per-table matrix (repo and dev, measured 2026-11-26 before the change)

"Assign hidden today" is no on every table: no Assign hide existed anywhere. After the merge every row below has all three Assign buttons hidden.

| Table | User-creatable | Create surface | Grid New today | Form New today | Quick create (dev) | After this task |
|---|---|---|---|---|---|---|
| sprk_matter | yes | create matter wizard | hidden + wizard | native | off | form New hidden + "+New Matter" |
| sprk_project | yes | create project wizard | hidden + wizard | native | **on** | quick create off; form New hidden + "+New Project" |
| sprk_workassignment | yes | work assignment wizard | hidden + wizard | native | off | form New hidden + "+New Work Assignment" |
| sprk_event | yes | event wizard | hidden + wizard | native | off | form New hidden + "+New Event"; subgrid via SecureChild |
| sprk_document | yes | upload wizard | hidden + wizard | native | off | form New hidden + "+New Document"; subgrid via SecureChild |
| sprk_todo | yes | to-do wizard | native | native | off | grid and form New hidden + "+New To Do" (launcher added) |
| sprk_communication | yes | email composer | native | native | off | grid and form New hidden + "+New Email" (launcher added) |
| sprk_invoice | system-created | invoice wizard from a core record's subgrid | hidden | hidden | off | unchanged New (already hidden outright); Assign hidden |
| sprk_billingevent | system-created | none (extraction) | hidden | hidden | off | as invoice |
| sprk_reportcard, budget, kpiassessment, memo, analysis | yes | no standalone wizard | native | native | off | New kept (OQ-1); Assign hidden; subgrid New redirected where SecureChild already carried it |
| invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection, procedureversion, signal, decisionrecord, budgetrevision, servicerequest | no | server only | native | native | off | New kept (OQ-2); Assign hidden |

Live proof of the negative case (read-only GETs, dev, 2026-11-26, before any deploy): `-Verify` exits 1 and names all 23 tables. Every table shows Assign; `sprk_project` shows quick create on; the seven redirected tables show the platform form New (and, for to-do and email, the grid New) and no replacement button; the invoice tables show Assign only (their New is already hidden); the 14 kept tables show the platform New, which is reported and not a failure.

## 3. Decisions

1. **New redirect is hide + replace, ribbon only.** Platform New and Save & New are hidden on form and grid; a Spaarke "+New" button carrying `sprk.CreatePrivilege.<t>.DisplayRule` (task 180) calls the table's standalone launcher. The create then happens on the server (wizard to BFF). It is UX, not a security boundary (ADR-002): the BFF owns every invariant.
2. **Assign: hide the command only.** `prvAssign` is not touched on any role (owner Q12). The check proves the button is gone, not that nobody can assign.
3. **JS redirect for subgrids, not a second ribbon.** `nativeNewAllowed` returns false and `newChildAvailable` true for the redirected set (`NATIVE_NEW_REDIRECTED`) before the secure flag is read, so a non-secure host no longer lands on the platform New. `NATIVE_NEW_CLOSED` (invoice only, pinned by an ArchTest) is unchanged.
4. **One create helper.** `createChild` is the single BFF POST (`/api/v1/child-records/{table}`); `createThenOpen`, Add Memo and Add KPI use it. No new auth path (`bff_auth.js` + `assignedaccess_postsave.js`). Refusal alerts with the server's text and "Nothing was created."; there is no quick create or `Xrm.WebApi.createRecord` fallback.
5. **`sprk_memo` joins `CREATE_THEN_OPEN`** (it is on the create-privilege tables and stamped child tables, so the agreement ArchTest holds).
6. **`-Verify` fails closed.** A table whose read fails is UNKNOWN and fails; a table missing from a capture fails.
7. **The SecureChild subgrid merge was not re-run.** The repo copies already carry the table parameter on both rules; `Merge-SecureChildRibbon.ps1` is a no-op there and only re-orders nodes.

## 4. Deviations from the brief

- The Add Memo / Add KPI ribbon buttons exist in the repo but are not deployed on dev (their web resources are). The import below deploys them; that is a behaviour addition, not only a redirect.
- `sprk_procedureversion` is in the list (registered by ebilling-r1 DEF-061 after the task 050 decision) so the guard equals `RecordTableRules.All`: 23 tables, not 22.
- Pester files in this repo target Pester 3.4; the new file uses the dash assertions (Pester 5). The user-profile Pester 6.2.0 fails to run any test file in this shell (`Export-ModuleMember` outside a module, also for existing files); the run used `-RequiredVersion 5.7.1`.

## 5. Detection method and its limits (`-Verify`)

Quick create: `EntityDefinitions` read. Assign and New: the effective ribbon from `RetrieveEntityRibbon(All)`, searched for button ids (a hidden button is absent). Limits: a modern command (appaction) naming Assign is not seen; hiding the command does not stop an API or flow assign; one environment, one moment; a failed read is UNKNOWN and fails. Exit 0 only if all 23 pass; 1 names the failing tables; 2 usage or auth.

## 6. UI test plan (after the deploy, dev, a user with Create on the tables)

1. Matter, project, work assignment, event, document grids and forms: no Assign; "+New X" present on forms; grid New opens the wizard.
2. To Do and Email grids: "+New To Do" opens the to-do wizard; "+New Email" opens the composer; no platform New or Save & New.
3. A project subgrid and a matter subgrid: New opens the create surface and the created row appears in the subgrid.
4. Event form, "Add Memo": a memo is created, filed to the event, and opens; no quick create flyout.
5. Matter form, KPI subgrid, "Add KPI": a KPI assessment is created and opens.
6. Project: the Communication Connections "new" control shows no quick create (see section 9).
7. An invoice: no Assign, no New anywhere. A service request / signal grid: Assign hidden, New unchanged.
8. A user without Create on `sprk_todo`: no "+New To Do".
9. `pwsh scripts/Test-NativeCreateSurfaces.ps1 -Verify -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` exits 0.

## 7. Deploy steps (main session, owner's go; in order)

0. **Capture the current dev copies.** `pac solution export` of SpaarkeMaster (unmanaged) to a dated folder; for the 23 tables copy each `RibbonDiff.xml`; export `sprk_project` quick-create state (`EntityDefinitions(LogicalName='sprk_project')?$select=IsQuickCreateEnabled`). Download the current `sprk_secure_child_ribbon.js` ("sprk_/scripts/secure_child_ribbon.js"), `sprk_wizard_commands`, `sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js` web resource contents.
1. **Web resources first** (the ribbon calls them): upload the four JS files from the SpaarkeMaster `WebResources` copies (`ribbon-edit` / `code-page-deploy` upload path); publish.
2. **Ribbons:** import SpaarkeMaster (unmanaged, publish all) so the 23 RibbonDiffs and the `sprk_project` quick-create flag land; or, per table, merge into a FRESH export with `Merge-NativeCreateRibbon.ps1` then import (the safer route if dev has drifted from the repo copies; compare the fresh export with section 2's "today" first).
3. **Create-privilege rules:** `infrastructure/dataverse/ribbon/CreatePrivilegeRibbons/Set-CreatePrivilegeRibbon.ps1 -Verify` (task 180) must still pass with the two new launchers.
4. **Verify:** `Test-NativeCreateSurfaces.ps1 -Verify -EnvironmentUrl ...`; then the UI test plan.
5. **Rollback:** re-upload the captured web resources; re-import the step 0 solution export (or the per-table `RibbonDiff.xml`); set `sprk_project` `IsQuickCreateEnabled` back via `PUT EntityDefinitions` with `MSCRM.MergeLabels: true` then `PublishXml` only if the owner wants quick create back.

## 8. Owner questions

- **OQ-1** (report card, budget, KPI assessment, memo, analysis: user-creatable, no standalone wizard): hide the platform New on grid and form, or route it to the parent wizard? Today the platform New stays and `-Verify` reports it.
- **OQ-2** (invoice-family system tables, ontology tables, `sprk_servicerequest`): confirm the New stays native (the task 050 decision), with Assign hidden.
- **OQ-3**: confirm `sprk_procedureversion` is out of the redirect (system-created by `ProcedureService`).

## 9. Out-of-scope defects (found, not fixed)

1. **CommunicationConnections PCF `handleCreateType` / `useQuickCreateForm`** (`src/client/pcf/`): with quick create off the call rejects and the handler only logs, so the "new" button does nothing. True for matter today and for project after this change. Scenario: on a project form, click New in the connections control; nothing opens and no message shows. Filing as an issue is the main session's decision; the fix is to route it to the server create like Add Memo.
