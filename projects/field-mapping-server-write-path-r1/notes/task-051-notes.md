# Task 051 notes - native UI redirect (FR-24)

Branch `task/fm-051` (first round) and `task/fm-051-r1` (fix round, below). Code and script only: nothing was imported, uploaded, published or written to any environment. The deploy is the main session's, on the owner's go (section 7).

**Wording.** What this task does is steer users to the product's create surface: it hides the platform command and offers the product's own. Hiding a command is a UX steer, not enforcement. The server write path (one owner per invariant) and the repair pass are what enforce; a user who reaches the platform create by another route (an API call, a flow, the modern command bar) is handled there, not here.

## 0. Fix round r1 (adversarial review, 2026-11-26) and owner decisions

| # | Defect | Fix | Test (fails before, passes after) |
|---|---|---|---|
| 1 (F1/F4) | The subgrid "+ New" redirect never ran: the `nativeNewAllowed` / `newChildAvailable` rules of seven tables passed only `PrimaryControl`, so the script got `table === undefined` and never consulted `NATIVE_NEW_REDIRECTED`. | `Merge-SecureChildRibbon.ps1` re-run on `sprk_todo`, `sprk_event`, `sprk_document`, `sprk_communication`, `sprk_kpiassessment`, `sprk_budget`, `sprk_reportcard` (the template carries `<StringParameter Value="<t>"/>`). Section 3 item 7 earlier called the merge a no-op: it was not, the repo copies had lost the parameter. Dev's live rules still carry it (the dev `-Verify` run read all eight rules as passing the table), so importing the pre-fix repo copies would have REMOVED it on dev. | ArchTest `EverySecureChildRule_InTheSpaarkeMasterRibbons_PassesItsOwnTableToTheScript` (+ must-fire / must-not-fire control `TheTableCheck_Fires_...`); Pester `every SpaarkeMaster secure-child rule passes its own table`. |
| 2 (F2) | `-Verify` could not see defect 1. | `Test-NativeCreateSurfaces.ps1` reads the `NativeNewAllowed` rule definition out of the effective ribbon and fails a table (named) whose rule passes no table or is missing (config `subgridRule`). The published ribbon DOES show the parameter, so no detection limit applies; the limit that remains is stated in the help (it checks the parameter, not what the script does with it; Jest runs the script). Repo-side checks: the ArchTest above and the Pester cases. | Pester: `fails and names the table when its subgrid New rule passes no table`, `... is missing or was not observed`, `does not look at the subgrid rule of a table the script does not serve` (must not fire). |
| 3 (F2/F4) | The deploy plan did not land Add Memo or Add KPI. See section 3a: neither command exists on dev (not in any entity's effective ribbon), the repo named web resources that do not exist under those names, and the project command exists nowhere. | Add Memo authored into `SpaarkeMaster/Entities/sprk_Event/RibbonDiff.xml`, "+ Add KPI" into `.../sprk_KPIAssessment/RibbonDiff.xml`, both loading `bff_auth`, `assignedaccess_postsave`, `secure_child_ribbon` in order then the code under its dev name (`sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js`). `-Verify` now requires both buttons (`alsoPresent`). Section 7 rewritten. | ArchTest `AddMemo_AndAddKpi_..._LoadTheirHelpersInOrder_UnderTheWebResourceNamesThatExist` (+ `TheWebResourceNameCheck_Fires_...` control); Pester `fails and names the table when Add Memo or + Add KPI is not on its ribbon`. |
| 4 (F2) | `add-kpi-ribbon.xml` comment said the native-New rule closes the button; with the table parameter it would close "+ Add KPI" for good. | The rule is dropped from the Add KPI command and from the fragment; the comment says why; `Merge-SecureChildRibbon.ps1` no longer re-adds it (its guarded-commands list is gone); README updated. | ArchTest `SpaarkesOwnAddKpi_CreatesThroughTheBff_AndCarriesNoNativeNewRule` (replaces the test that pinned the rule). |
| 5 (F1) | Add Memo: a failed event save was silent and the memo never opened. | `Spaarke.Event.AddMemo` opens the memo whatever the save result; on a failed save it shows (the platform alert dialog the other messages use) "The memo was created, but the event could not be saved." Both copies byte-identical. | node `ribbonAddMemo.test.mjs`: `... when the event save fails still opens the memo and tells the user ...`, plus `... succeeds shows no save-failure message` (must not fire). |
| 6 (K1) | `Merge-NativeCreateRibbon.ps1` wrote `-Out` before its checks. | Writes a temp file beside `-Out`, runs the checks, then moves it into place; a failed check leaves `-Out` unchanged; the temp file never lingers. Idempotency test still green. | Pester `writes -Out only after its checks, through a temporary file that does not linger (K1)` (an order check on the script text plus no `*.tmp` left: no check can be made to fail from a seed file, so the order is what is pinned). |
| 7 | Wording. | Notes, script help and comments say "steer to the product's create surface"; hiding is not enforcement. | - |

**Fail-before / pass-after, measured 2026-11-26.** "Before" = the 20 non-test files put back to their state at `02e2ff0833` with the new tests in place; "after" = the fix commit.

| Suite | Before (fix reverted) | After |
|---|---|---|
| ArchTests `SecureChildNewCommandAgreementTests` (filtered, built without `--no-build`) | 3 failed, 13 passed (the table-parameter check, the web-resource-order check, the Add KPI no-native-rule check) | 16 passed |
| Pester 5.7.1 `Test-NativeCreateSurfaces.Tests.ps1` | 11 failed, 17 passed | 28 passed |
| node `EventCommands` | 1 failed (save fails: memo must still open), 26 passed | 27 passed |
| Jest `projectMatterSubgridCommands` (report card launcher) | 4 failed, 15 passed | 19 passed |
| Jest `secureChildRibbonScript` | not affected by the change | 71 passed |
| Full `dotnet test tests/Spaarke.ArchTests` | not run | 2849 passed, 0 failed |

The Pester K1 case pins the write order from the script text (no check can be forced to fail from a seed file), not a behavioural failure of a check; the old script fails it because it wrote `-Out` first.

**Owner decisions, 2026-11-26** (received during this round; recorded, implemented below):

- **OQ-1 report card.** A report card is created only in the context of its parent matter. Its form "New" opens the Report Card wizard (`sprk_createreportcardwizard`, task 027) filed under the matter (or project) the form names; the subgrid "+ New" already opens it (`Spaarke.SecureChild.Ribbon.newChild` via `NATIVE_NEW_REDIRECTED` + `WIZARDS`, working now that the table parameter is passed); the HOME grid has no New. New launcher `Spaarke.Commands.Wizards.openCreateReportCardFromForm` (`sprk_wizard_commands.js`, both copies identical; registered in `create-launchers.json` as `sprk_reportcard`). A form that names no parent opens nothing and says so (there is no unfiled report card). `sprk_reportcard` is now `redirected` in the config, `gridButton: none`.
- **OQ-1 budget and KPI assessment.** Created only in the context of the parent matter: the platform HOME-grid New is hidden (no replacement). The form New stays native; the subgrid New is the SecureChild BFF create-then-open (it creates the row through `POST /api/v1/child-records/{table}`, so mapping and owner are server-side, and opens it), unchanged from before 051. The owner will have wizards built as separate work; no placeholder page is built here. `-Verify` expects `Mscrm.HomepageGrid.<t>.NewRecord` absent (config `homeGridNew: hidden`) and still only reports their form New. The subgrid "+ Add KPI" creates through the BFF; left as fixed.
- **OQ-1 memo and analysis.** New stays native (no change).
- **Unchanged:** OQ-2 (invoice family, ontology tables, `sprk_servicerequest`: New native, Assign hidden) and OQ-3 (`sprk_procedureversion` stays in the list: 23 tables).

**Home-grid New on the seven redirected tables, after this task.** In every case the platform home-grid New is hidden (`Mscrm.HomepageGrid.<t>.NewRecord`) and the home grid's one New is the product's launcher button, which `-Verify` now requires present (config `gridButtonId`):

| Table | Home-grid button | Launcher |
|---|---|---|
| sprk_matter | `sprk.Matter.NewWizard.Grid.Button` | `openCreateMatterStandalone` |
| sprk_project | `sprk.Project.NewWizard.Grid.Button` | `openCreateProjectStandalone` |
| sprk_workassignment | `sprk.WorkAssignment.NewWizard.Grid.Button` | `openCreateWorkAssignmentStandalone` |
| sprk_event | `sprk.Event.NewWizard.Grid.Button` | `openCreateEventStandalone` |
| sprk_document | `sprk.Document.NewUpload.Grid.Button` | `Spaarke_UploadDocumentsStandalone` |
| sprk_todo | `sprk.NativeCreate.sprk_todo.Grid.New` | `openCreateTodoStandalone` |
| sprk_communication | `sprk.NativeCreate.sprk_communication.Grid.New` | `openNewEmailStandalone` |

The five existing buttons were already in the repo ribbons with the hide; re-running `Merge-NativeCreateRibbon.ps1` on all 23 left every other file byte-identical (idempotent). On dev today matter, project, work assignment, event and document already hide the platform home-grid New and show their wizard button; to-do and email still show the platform home-grid New until the import. Pester `every SpaarkeMaster ribbon carries what the config says` pins all of this from the repo.

Open owner question that remains (not decided here): whether the home-grid New on matter, project, work assignment, event and document, and the subgrid "+ New", should also be closed beyond what is described here.

## 1. What was built

| Piece | Where |
|---|---|
| One list of what the native UI does per table | `infrastructure/dataverse/ribbon/NativeCreateRibbons/native-create-surfaces.json` |
| Idempotent RibbonDiff merge (Assign x3 hide, platform New hide, launcher button, home-grid hide) | `.../NativeCreateRibbons/Merge-NativeCreateRibbon.ps1` |
| 23 SpaarkeMaster RibbonDiffs re-authored with it | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_*/RibbonDiff.xml` |
| `sprk_project` quick create 1 to 0 | `.../Entities/sprk_Project/Entity.xml` |
| Shared BFF create `Spaarke.SecureChild.Ribbon.createChild`; redirected tables no longer fall through to the platform New | `sprk_secure_child_ribbon.js` (v1.1.0) and its SpaarkeMaster copy |
| Add Memo and Add KPI create through the BFF, never a quick create form | `sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js` (+ copies); commands authored into the SpaarkeMaster `sprk_Event` / `sprk_KPIAssessment` RibbonDiffs |
| Standalone launchers for To Do and New Email; Report Card form launcher | `sprk_wizard_commands.js` (+ SpaarkeMaster copy); registered in `create-launchers.json` |
| `-Verify` | `scripts/Test-NativeCreateSurfaces.ps1` |
| Tests | Jest `secureChildRibbonScript.test.ts`, `projectMatterSubgridCommands.test.ts` (report card launcher); node `ribbonAddMemo.test.mjs`; Pester `tests/scripts/Test-NativeCreateSurfaces.Tests.ps1`; ArchTest `SecureChildNewCommandAgreementTests` |

## 2. Per-table matrix (repo and dev, measured 2026-11-26 before the change)

"Assign hidden today" is no on every table: no Assign hide existed anywhere. After the merge every row below has all three Assign buttons hidden.

| Table | User-creatable | Create surface | Grid New today | Form New today | Quick create (dev) | After this task |
|---|---|---|---|---|---|---|
| sprk_matter | yes | create matter wizard | hidden + wizard | native | off | form New hidden + "+New Matter" |
| sprk_project | yes | create project wizard | hidden + wizard | native | **on** | quick create off; form New hidden + "+New Project" |
| sprk_workassignment | yes | work assignment wizard | hidden + wizard | native | off | form New hidden + "+New Work Assignment" |
| sprk_event | yes | event wizard | hidden + wizard | native | off | form New hidden + "+New Event"; subgrid via SecureChild; Add Memo on the form |
| sprk_document | yes | upload wizard | hidden + wizard | native | off | form New hidden + "+New Document"; subgrid via SecureChild |
| sprk_todo | yes | to-do wizard | native | native | off | grid and form New hidden + "+New To Do" (launcher added) |
| sprk_communication | yes | email composer | native | native | off | grid and form New hidden + "+New Email" (launcher added) |
| sprk_invoice | system-created | invoice wizard from a core record's subgrid | hidden | hidden | off | unchanged New (already hidden outright); Assign hidden |
| sprk_billingevent | system-created | none (extraction) | hidden | hidden | off | as invoice |
| sprk_reportcard | yes, in its matter's context | Report Card wizard | native | native | off | home-grid New hidden (no replacement); form New hidden + "+New Report Card" (wizard under the form's matter); subgrid "+ New" opens the wizard |
| sprk_budget, sprk_kpiassessment | yes, in the matter's context | none yet (separate work) | native | native | off | home-grid New hidden; form New native (reported); subgrid New = BFF create-then-open (as before); "+ Add KPI" on the KPI subgrid (BFF) |
| sprk_memo, sprk_analysis | yes | Add Memo (BFF) / analysis wizard | native | native | off | New kept (owner OQ-1); Assign hidden |
| invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection, procedureversion, signal, decisionrecord, budgetrevision, servicerequest | no | server only | native | native | off | New kept (OQ-2); Assign hidden |

Live proof of the negative case (read-only GETs, dev, 2026-11-26, before any deploy, re-run in the fix round with the stricter `-Verify`): exit 1 naming all 23 tables. Every table shows Assign; `sprk_project` shows quick create on; the seven redirected tables show the platform form New and no replacement button (to-do and email also the platform home-grid New); report card, budget and KPI assessment show the platform home-grid New; the invoice tables show Assign only; the other kept tables show the platform New, which is reported and not a failure. No table is reported for the subgrid rule: dev's rules pass the table.

## 3. Decisions

1. **New redirect is hide + replace, ribbon only.** Platform New and Save & New are hidden on form and grid; a Spaarke "+New" button carrying `sprk.CreatePrivilege.<t>.DisplayRule` (task 180) calls the table's standalone launcher. The create then happens on the server (wizard to BFF). It steers users; it is not a security boundary (ADR-002): the BFF owns every invariant.
2. **Assign: hide the command only.** `prvAssign` is not touched on any role (owner Q12). The check proves the button is gone, not that nobody can assign.
3. **JS redirect for subgrids, not a second ribbon.** `nativeNewAllowed` returns false and `newChildAvailable` true for the redirected set (`NATIVE_NEW_REDIRECTED`) before the secure flag is read, so a non-secure host no longer lands on the platform New. This works only when the rule passes the table (defect 1 above). `NATIVE_NEW_CLOSED` (invoice only, pinned by an ArchTest) is unchanged.
4. **One create helper.** `createChild` is the single BFF POST (`/api/v1/child-records/{table}`); `createThenOpen`, Add Memo and Add KPI use it. No new auth path (`bff_auth.js` + `assignedaccess_postsave.js`). Refusal alerts with the server's text and "Nothing was created."; there is no quick create or `Xrm.WebApi.createRecord` fallback.
5. **`sprk_memo` joins `CREATE_THEN_OPEN`** (it is on the create-privilege tables and stamped child tables, so the agreement ArchTest holds).
6. **`-Verify` fails closed.** A table whose read fails is UNKNOWN and fails; a table missing from a capture fails; a subgrid rule not observed fails.
7. **The SecureChild subgrid merge WAS needed** (corrects the first round): the repo copies of seven tables had lost the table parameter. Re-running it re-orders some nodes (a larger diff than the one line per rule that matters).

### 3a. Add Memo and Add KPI on dev (read-only, 2026-11-26)

- `RetrieveEntityRibbon` (All) for `sprk_event`, `sprk_matter`, `sprk_project`, `sprk_kpiassessment`: **none of `Spaarke.Event.AddMemo.*`, `sprk.matter.subgrid.kpi.AddKpiButton.*`, `sprk.project.subgrid.kpi.AddKpiButton.*` exists, and no `Spaarke.Event.*` command at all** (no Complete, Cancel, Reschedule, Reassign). The `EventRibbons` (1.0.0.2) and `KPIRibbons` (1.0.0.1) solutions are on dev, but a ribbon is one blob per entity in the unmanaged layer and a later import replaced it. No `appaction` row names KPI or Memo for these tables.
- Web resources on dev: `sprk_event_ribbon_commands.js` (id `021a013e-9702-f111-8407-7c1e520aa4df`, 2026-10-12, 40893 bytes, sha256 `305847f9caaf...`, OLD code), `sprk_kpi_ribbon_actions.js` (id `803a95f8-8208-f111-8407-7c1e520aa4df`, 6602 bytes, `b4476fc07559...`, OLD quick-create code), `sprk_/scripts/secure_child_ribbon.js` (id `68437c5f-4ac1-f111-a05c-0022482913fc`, 2026-11-22, 22507 bytes, `9d9209b575a7...`, pre-051), `sprk_/scripts/bff_auth.js`, `sprk_/scripts/assignedaccess_postsave.js`, `sprk_wizard_commands` (id `e03039e0-d523-f111-88b5-7c1e520aa4df`, 2026-11-25, 17099 bytes, `22671fa9b6b6...`). **There is no `sprk_/scripts/kpi_ribbon_actions.js`**, which the SpaarkeCore fragment loaded, and the Event commands named `sprk_event_ribbon_commands` (no `.js`): the buttons would have loaded nothing.
- The project command `sprk.project.subgrid.kpi.AddKpiButton.Command` exists nowhere (repo or dev): only JS comments name it. The KPI button's Location is the `sprk_kpiassessment` subgrid, so ONE button serves a matter, a project and a report card form, and the script is host-agnostic (`hostOf`). So there is one command (`sprk.matter.subgrid.kpi.AddKpiButton.Command`, id kept) and `Spaarke.KpiRibbon.openProjectQuickCreate` stays in the script unused. If the owner wants a project-only button, it needs a host enable rule (new code): not done.
- Both created through the BFF: **"+ Add KPI" and the subgrid's "New KPI Assessment" now do the same thing** (two buttons on one subgrid). Owner may want to drop one; not decided here.
- The repo has three sources that look deployable and are not to be imported on dev: `src/solutions/EventCommands/solution export` (EventRibbons: a whole `sprk_event` RibbonDiff that would replace what the SpaarkeMaster import lands), the SpaarkeCore `add-kpi-ribbon.xml` fragment (kept as the authored source, merged into SpaarkeMaster), and `EventRibbonDiffXml.xml` (Ribbon Workbench source; its other commands still name the library without `.js`).

## 4. Deviations from the brief

- The Add Memo / Add KPI ribbon buttons are not deployed on dev (section 3a). The import deploys them: a behaviour addition, not only a redirect.
- `sprk_procedureversion` is in the list (registered by ebilling-r1 DEF-061 after the task 050 decision) so the guard equals `RecordTableRules.All`: 23 tables, not 22.
- Pester files in this repo target Pester 3.4; the new file uses the dash assertions (Pester 5). The user-profile Pester 6.2.0 fails to run any test file in this shell (`Export-ModuleMember` outside a module); the run used `-RequiredVersion 5.7.1`.
- r1 changed an UNIFIED-ACCESS-CONTROL-r2 test, `SecureChildNewCommandAgreementTests` (it pinned the native-New rule on "+ Add KPI"), because the contract it pinned no longer holds (section 0, defect 4).

## 5. Detection method and its limits (`-Verify`)

Quick create: `EntityDefinitions` read. Assign, New, the launcher buttons, Add Memo / Add KPI and the subgrid rule: the effective ribbon from `RetrieveEntityRibbon(All)`, searched for button ids (a hidden button is absent) and for the `NativeNewAllowed` rule definition's `StringParameter`. Limits: a modern command (appaction) naming Assign or New is not seen; hiding a command steers and does not stop an API or flow create or assign; the parameter check proves the rule hands the script its table, not what the script then does (Jest runs the script); one environment, one moment; a failed read is UNKNOWN and fails. Exit 0 only if all 23 pass; 1 names the failing tables; 2 usage or auth.

## 6. UI test plan (after the deploy, dev, a user with Create on the tables)

1. Matter, project, work assignment, event, document grids and forms: no Assign; "+New X" present on forms; the HOME-grid New is the wizard / upload button, not the platform form.
2. To Do and Email grids: "+New To Do" opens the to-do wizard; "+New Email" opens the composer; no platform New or Save & New.
3. A project subgrid and a matter subgrid: "+ New" opens the create surface and the created row appears in the subgrid. A matter's Report Cards subgrid "+ New" opens the Report Card wizard filed under the matter.
4. Report card form: "+New Report Card" opens the wizard under the form's matter; the report cards home grid has no New. Budget and KPI assessment home grids have no New; their form New opens the native form; their subgrid "+ New" creates the row through the BFF and opens it.
5. Event form, "Add Memo": a memo is created, filed to the event, and opens; no quick create flyout. Make the event save fail (for example a required field cleared by a business rule): the memo still opens and the message "The memo was created, but the event could not be saved." shows.
6. Matter and project forms, KPI subgrid, "+ Add KPI": a KPI assessment is created and opens; it is NOT disabled.
7. Project: the Communication Connections "new" control shows no quick create (see section 9).
8. An invoice: no Assign, no New anywhere. A service request / signal grid: Assign hidden, New unchanged. Memo and analysis grids: New unchanged.
9. A user without Create on `sprk_todo`: no "+New To Do".
10. `pwsh scripts/Test-NativeCreateSurfaces.ps1 -Verify -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` exits 0.

## 7. Deploy steps (main session, owner's go; in order)

The repo ships ONE import that lands everything: SpaarkeMaster (its WebResources and the entity RibbonDiffs). Do not import `EventCommands/solution export`, the SpaarkeCore fragment or any single-entity ribbon export over it: a ribbon is one blob per entity and the last import wins.

0. **Capture for rollback (read-only, before anything).**
   - `pac solution export` of SpaarkeMaster (unmanaged) into a dated folder. It holds the dev `RibbonDiff.xml` of every table below.
   - The effective ribbon of `sprk_event`, `sprk_kpiassessment`, `sprk_reportcard`, `sprk_budget`, `sprk_todo`, `sprk_communication` (`RetrieveEntityRibbon`, save each `RibbonXml.xml`).
   - The four web resources' CONTENT, by id (GET `webresourceset(<id>)?$select=content`), with their sha256 (section 3a lists id and hash): `sprk_event_ribbon_commands.js`, `sprk_kpi_ribbon_actions.js`, `sprk_/scripts/secure_child_ribbon.js`, `sprk_wizard_commands`.
   - `GET EntityDefinitions(LogicalName='sprk_project')?$select=IsQuickCreateEnabled`.
   - Drift check: every `sprk.*` / `Spaarke.*` id in the captured effective ribbons that is not in the repo `RibbonDiff.xml` of that table is LOST by the import. Stop and tell the owner if there is one.
1. **Web resources** (the ribbon calls them). Either let step 2 land them, or upload first (`code-page-deploy` upload path) and publish; the dev names are exact, do not rename and do not create `sprk_/scripts/kpi_ribbon_actions.js`:
   - `src/client/webresources/js/sprk_secure_child_ribbon.js` -> `sprk_/scripts/secure_child_ribbon.js`
   - `src/client/webresources/js/sprk_wizard_commands.js` -> `sprk_wizard_commands`
   - `src/solutions/EventCommands/sprk_event_ribbon_commands.js` -> `sprk_event_ribbon_commands.js`
   - `src/solutions/webresources/sprk_kpi_ribbon_actions.js` -> `sprk_kpi_ribbon_actions.js`
   (The SpaarkeMaster `WebResources` copies are byte-identical: `cmp` them.)
2. **Ribbons and the project flag:** import SpaarkeMaster (unmanaged, publish all). It lands the 23 RibbonDiffs (Assign hides, platform New hides, launcher buttons), the home-grid hides for report card, budget and KPI assessment, the table parameter on the eight secure-child rules, **Add Memo** on `sprk_event` and **+ Add KPI** on `sprk_kpiassessment`, and the `sprk_project` quick-create flag. If dev drifted from the repo (step 0), merge into a FRESH export instead with `Merge-NativeCreateRibbon.ps1` and `Merge-SecureChildRibbon.ps1` per table, then re-add the Add Memo and Add KPI nodes from the repo copies.
3. **Create-privilege rules:** `infrastructure/dataverse/ribbon/CreatePrivilegeRibbons/Set-CreatePrivilegeRibbon.ps1 -Verify` (task 180) must still pass with the new launchers (To Do, New Email, Report Card form) and the Add Memo / Add KPI commands.
4. **Verify:** `Test-NativeCreateSurfaces.ps1 -Verify -EnvironmentUrl ...` exits 0 (it now also requires the launcher buttons, the home-grid hides, `Spaarke.Event.AddMemo.Button` on `sprk_event`, `sprk.matter.subgrid.kpi.AddKpiButton` on `sprk_kpiassessment` and the table parameter on the subgrid rules). Compare the four web resources on dev with the repo files by sha256. Then the UI test plan.
5. **Rollback, per artifact:**
   - Web resources: re-upload the captured content to the ids in section 3a; publish.
   - Ribbons (all tables incl. Add Memo / Add KPI): re-import the step 0 SpaarkeMaster export, or restore the per-table `RibbonDiff.xml` from it; removing Add Memo or Add KPI alone means restoring `sprk_event` / `sprk_kpiassessment` `RibbonDiff.xml`.
   - `sprk_project` quick create: set `IsQuickCreateEnabled` back via `PUT EntityDefinitions` with `MSCRM.MergeLabels: true`, then `PublishXml`, only if the owner wants it back.

## 8. Owner questions

- **OQ-1: decided 2026-11-26** (section 0): memo and analysis New stay native; report card form and subgrid New open the Report Card wizard under the matter, home-grid New hidden; budget and KPI assessment: home-grid New hidden, form New native, subgrid New the BFF create-then-open (unchanged; the re-check of 2026-11-26 found the earlier "subgrid native" wording wrong), until their wizards exist.
- **OQ-2** (invoice-family system tables, ontology tables, `sprk_servicerequest`): confirm the New stays native (the task 050 decision), with Assign hidden. Open.
- **OQ-3**: confirm `sprk_procedureversion` is out of the redirect (system-created by `ProcedureService`). Open.
- **OQ-4 (open)**: should the home-grid New on matter, project, work assignment, event and document (today the wizard button, platform New hidden), and the subgrid "+ New", also be closed beyond this?
- **OQ-5 (open)**: "+ Add KPI" and "New KPI Assessment" now do the same on one subgrid: keep both?

## 9. Out-of-scope defects (found, not fixed)

1. **CommunicationConnections PCF `handleCreateType` / `useQuickCreateForm`** (`src/client/pcf/`): with quick create off the call rejects and the handler only logs, so the "new" button does nothing. True for matter today and for project after this change. The fix is to route it to the server create like Add Memo. Filing is the main session's decision.
2. **The other EventRibbons buttons** (Complete, Cancel, Reschedule, Reassign, Complete Selected, the homepage-grid set) are not on dev (overwritten by a later ribbon import) and their commands in `EventRibbonDiffXml.xml` and the SpaarkeMaster-less sources still name `sprk_event_ribbon_commands` without `.js`. Not restored here.
3. `src/solutions/EventCommands/README.md` tells the operator to name the web resource without `.js`; dev's has it.
4. A report card form's "+New Report Card" cannot work from a report card whose matter / project lookups are empty (it says so).

## 10. Scoped re-check of the fix round (main session, 2026-11-26)

A Sonnet re-check of `02e2ff0833..5f9546d8ba` confirmed every fix and scope item listed below. It also confirmed that the SpaarkeMaster web-resource copies are byte-identical to their sources.
- items 1–7;
- the report-card launcher (it passes entityType, entityId, recordName and bffBaseUrl);
- the home-grid hides (correct OOB ids);
- the To Do and Email grid launchers;
- the Add Memo alert path (a failed create returns before it, so it is not swallowed).

UAC-r2 had no access objection.

**F1, closed as a docs fix.** The budget and KPI assessment subgrid "+ New" is not native. It is the SecureChild BFF create-then-open: `NATIVE_NEW_REDIRECTED` in `sprk_secure_child_ribbon.js`, deliberate since before 051, and what dev does today. Three places wrongly said "native": the notes (§0, §2, §6 item 4, §8), the config reasons and `homeGridNew`. They are corrected. This route keeps mapping and owner on the server, in line with the owner's answer that mapping always applies. The owner is told; nothing changes on dev for the subgrid.

**Known limits.**
- **K1.** If `openMemo` rejects after the save-failure alert, it only logs. The memo exists; the user opens it from the subgrid.
- **K2.** The report-card launcher needs `sprk_regardingmatter` or `sprk_regardingproject` on the form. All four main forms have them.
- **K3.** The duplicate-id sweep over all 23 RibbonDiffs did not finish in the re-check shell. The changed tables were read by eye, with no duplicates found, and the import fails on a duplicate id.
- **K4.** `-Verify` proves the rule passes the table, not what the script does with it. Jest covers the script.

## 11. Dev deploy (main session, 2026-11-26, owner go "yes can import 051")

- **Method.** This is §7's "merge into a fresh export" route, not a repo SpaarkeMaster import. The repo copy carries every form, view and web resource, and other projects deploy pieces of those straight to dev. The steps:
  1. `pac solution export` of dev SpaarkeMaster (unmanaged) to `C:\tmp\fm051-rollback\SpaarkeMaster_dev_pre051.zip`.
  2. Replace only the 23 tables' `RibbonDiffXml` with the repo copies (work branch 3e0268349a).
  3. Replace the four web resources' content.
  4. Set `sprk_project` IsQuickCreateEnabled 1 → 0.
  5. Import with Publish All.
  - A node-by-node comparison before import showed no other entity part, file or non-entity part changed.
- **Drift check.** All 1,054 Spaarke ribbon id occurrences on dev (23 tables) exist in the repo copies, so nothing was lost.
- **Pre-import web resources.**
  - `secure_child_ribbon.js`, `sprk_wizard_commands` and `sprk_kpi_ribbon_actions.js` on dev equalled the pre-051 repo, ignoring line endings.
  - `sprk_event_ribbon_commands.js` on dev was older than the repo: it predated task 066's status-column change. The repo version is the intended one.
- **Timing.** Import 16:48Z–17:53Z (async op ea539c7a…, 5 min 18 s), then Publish All.
- **Verify.**
  - `Test-NativeCreateSurfaces.ps1 -Verify`: PASS, 23 tables, exit 0.
  - `Set-CreatePrivilegeRibbon.ps1 -Verify -EnvironmentUrl …`: PASSED.
  - The four web resources on dev equal the repo by sha256 (modified 17:51Z).
- **Rollback.** Everything is in `C:\tmp\fm051-rollback`:
  - the full pre-import export;
  - each table's effective ribbon before the import (`<table>.ribbon`);
  - the four web resources' content by id (`wr_*`, hashes in `capture.log`);
  - the `sprk_project` quick-create value (True).
- **UI checks (§6).** Pending; the ones that call the BFF wait for the d307d67e36 BFF deploy to go live.
