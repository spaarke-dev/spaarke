# Task 180 — custom Create / wizard buttons follow the Create privilege

Owner round 89 item 2 (2026-10-09, binding): every custom Create / wizard ribbon button hides when the user lacks the
Create privilege on the target table, using Dataverse's standard privilege rule, no code. A read-only role then sees no
create commands.

## The rule (one per target table)

```xml
<DisplayRule Id="sprk.CreatePrivilege.<table>.DisplayRule">
  <EntityPrivilegeRule EntityName="<table>" PrivilegeType="Create" PrivilegeDepth="Basic" />
</DisplayRule>
```

- **Display rule, not enable rule.** `EntityPrivilegeRule` is a display-rule type, and the owner asked for the button to
  hide. It is added next to the command's existing rules (`FormStateRule Existing` etc.), so both must hold.
- **`PrivilegeDepth="Basic"`** = holds Create at any depth (user, BU, parent:child BU, organization).
- **`EntityName` is always explicit**: a form button on a matter that creates a project checks `sprk_project`, not the
  form's table.
- **Selection by launcher function, not command id.** `infrastructure/dataverse/ribbon/CreatePrivilegeRibbons/create-launchers.json`
  maps each JavaScript launcher to the table it creates. A command whose action calls a mapped launcher gets the rule.
  A new button on an existing launcher is found without editing the list, and `-Verify` reads every host table's
  effective ribbon for any command calling a launcher.
- Out-of-the-box New buttons are untouched (goal 4). The platform subgrid New (`Mscrm.AddNewRecordFromSubGridStandard`)
  and task 147's secure-child New (`sprk.SecureChild.<table>.New.Command`) already carry the platform's
  `Mscrm.CreateSelectedEntityPermission` (checked live on dev). They are therefore listed in `notCreate`, not changed.

## (1) Inventory

Deployed on dev = present in spaarkedev1's unmanaged `ribboncommands` (read-only Web API, 2026-10-09). Before this
task, none of them carried a Create privilege rule. The grid wizard commands had no rules at all; the matter form
wizards had only `FormStateRule Existing`.

### Deployed on dev — ruled by this task

| # | Host table / surface | Command | Launcher | Creates (rule on) | Repo sources changed |
|---|---|---|---|---|---|
| 1 | sprk_matter form | `sprk.Wizard.Matter.CreateProject.Command` | `openCreateProjectWizard` | sprk_project | SpaarkeMaster sprk_Matter, spaarke_insights sprk_Matter, MatterRibbons/customizations.xml |
| 2 | sprk_matter form | `sprk.Wizard.Matter.CreateEvent.Command` | `openCreateEventWizard` | sprk_event | same three |
| 3 | sprk_matter form | `sprk.Wizard.Matter.CreateTodo.Command` | `openCreateTodoWizard` | sprk_todo | same three |
| 4 | sprk_matter form | `sprk.Wizard.Matter.UploadDocuments.Command` | `openDocumentUploadWizard` | sprk_document | same three |
| 5 | sprk_matter form | `sprk.Wizard.Matter.PlaybookLibrary.Command` | `openPlaybookLibrary` | sprk_analysis | same three |
| 6 | sprk_matter home grid | `sprk.Matter.NewWizard.Grid.Command` (replaces the hidden OOB New) | `openCreateMatterStandalone` | sprk_matter | SpaarkeMaster sprk_Matter, spaarke_insights sprk_Matter |
| 7 | sprk_project home grid | `sprk.Project.NewWizard.Grid.Command` (replaces the hidden OOB New) | `openCreateProjectStandalone` | sprk_project | SpaarkeMaster sprk_Project |
| 8 | sprk_event home grid | `sprk.Event.NewWizard.Grid.Command` (replaces the hidden OOB New) | `openCreateEventStandalone` | sprk_event | SpaarkeMaster sprk_Event |
| 9 | sprk_workassignment home grid | `sprk.WorkAssignment.NewWizard.Grid.Command` (replaces the hidden OOB New) | `openCreateWorkAssignmentStandalone` | sprk_workassignment | SpaarkeMaster sprk_WorkAssignment, WorkAssignmentRibbons/Entities/sprk_workassignment/RibbonDiff.xml |
| 10 | sprk_document home grid | `sprk.Document.NewUpload.Grid.Command` (replaces the hidden OOB New) | `Spaarke_UploadDocumentsStandalone` | sprk_document | SpaarkeMaster sprk_Document |
| 11 | sprk_document subgrid | `Spaarke.Document.AddMultiple.Command` | `Spaarke_AddMultipleDocuments` | sprk_document | SpaarkeMaster sprk_Document, spaarke_containers sprk_Document, DocumentRibbons/Entities/sprk_Document/RibbonDiff.xml |
| 12 | sprk_analysis subgrid | `Spaarke.Analysis.NewAnalysisSubgrid.Command` (replaces the hidden OOB New) | `Spaarke_NewAnalysisFromSubgrid` | sprk_analysis | SpaarkeMaster sprk_analysis, AnalysisRibbons/Entities/sprk_analysis/RibbonDiff.xml |
| 13 | email form | `sprk.Email.ArchiveEmail.Command` ("Save to Document") | `Spaarke.Email.saveToDocument` | sprk_document | SpaarkeMaster Email, EmailRibbons/Entities/email/RibbonDiff.xml |

### Modern command (appaction) on dev — NOT ruled; owner decision (verifier F2, fix round 1)

| # | App / surface | Modern command | Launcher | Creates | State |
|---|---|---|---|---|---|
| 13a | Matter Management, sprk_document main grid | "New Document" — `sprk__NewDocument!97b3448447bf4b1bb0bd610c4dd96e4f!sprk_MatterManagement!sprk_document!1` (appactionid `ffbd45bf-8c9d-48db-b433-c3c09e6577de`; unmanaged, visible, `visibilitytype` None; made in the command designer, last modified 2026-10-06 by Ralph Schroeder) | `Spaarke_UploadDocumentsStandalone` (`sprk_subgrid_commands`) | sprk_document | UNRULED today. **Owner 2026-10-09: HIDE it** — `-Apply` sets `hidden = true` (step 4b), then `-Verify` passes it |

The first inventory missed it: an appaction is not ribbon XML, so `RetrieveEntityRibbon` (and everything built on it) does
not return it. The dry run with `-EnvironmentUrl` and `-Verify` now also read every unmanaged, active appaction whose
`onclickeventjavascriptfunctionname` is a launcher (live on dev: 6 unmanaged appactions, all on sprk_document in Matter
Management; this is the only one calling a launcher — the other five are the platform's own New / Add New / Add Existing,
all hidden).

**Can it get a Create-privilege rule by script? No.** The modern equivalent is a Power Fx Visible formula,
`DataSourceInfo(Documents, DataSourceInfo.CreatePermission)` (Microsoft Learn, "Use Power Fx with commands", which uses
exactly this example). The formula is authored in the command designer and compiled into the app's command component
library (a canvas `.msapp`); the appaction row only names the library and function (`visibilitytype` = 1 Formula,
`visibilityformulacomponentlibrary`, `visibilityformulafunctionname`). No supported API writes that formula or reads
its text back. dev has no command component library and no Formula-visibility appaction at all (read 2026-10-09).
`visibilitytype` 2 "Classic Rules" applies to the platform's converted commands; there is no documented way to point a
custom appaction at a RibbonDiff rule. So `-Apply` does not touch it, and nothing was deleted.

**Options (owner decision):**
1. Author the formula in the command designer (Matter Management → Documents main grid → New Document → Visibility
   "Show on condition from formula" → `DataSourceInfo(Documents, DataSourceInfo.CreatePermission)`), publish.
   `-Verify` then passes it with a "manual check" line, since the formula text cannot be read back.
2. Hide it (`hidden = true` on the row, reversible). The classic "+New Document" (`sprk.Document.NewUpload.Grid.Command`,
   row 10: same launcher, same grid) already covers it and is ruled by `-Apply`.
3. Delete it (same user-visible result as 2).
4. Leave it: a read-only user keeps seeing "New Document" on that grid, and `-Verify` keeps failing.

Recommendation: 2, or 1 if the modern button is wanted. It duplicates the classic button that `-Apply` rules.

**Owner decision (2026-10-09, relayed by the coordinator): option 2, HIDE it.**
- The appaction is listed in `create-launchers.json` `hideAppActions` (by appactionid `ffbd45bf-8c9d-48db-b433-c3c09e6577de`
  and uniquename).
- The `-Apply` hide, in order:
  1. Refuses, writing nothing, unless the live row has that uniquename, is unmanaged and calls a launcher.
  2. Sets `hidden = true` on that one row only (step 4b, after the ribbon imports).
  3. Publishes the Matter Management app (`PublishXml`, appmodules).
  4. Reads `hidden` back and throws if it is not true.
- The dry run prints the planned hide.
- `-Verify` counts a hidden create appaction as ruled.
- Nothing was changed live by this task; the hide runs at gate G180-1.

### In the repo, not deployed on dev — ruled in source so a later deploy carries it

| # | Host / surface | Command | Creates (rule on) | Source |
|---|---|---|---|---|
| 14–23 | analysis, budget, communication, contact, document, event, invoice, organization, project, work assignment forms | `sprk.Wizard.<Host>.CreateTodo.Command` | sprk_todo | `<Host>Ribbons/createtodo-button.xml` (generated by `scripts/Generate-CreateTodoRibbonXmlForTenEntities.ps1`, generator updated) |
| 24–25 | sprk_project form | `sprk.Wizard.Project.UploadDocuments.Command`, `...PlaybookLibrary.Command` | sprk_document, sprk_analysis | ProjectRibbons/customizations.xml |
| 26–27 | sprk_event form | `sprk.Wizard.Event.UploadDocuments.Command`, `...PlaybookLibrary.Command` | sprk_document, sprk_analysis | EventRibbons/customizations.xml |
| 28–29 | sprk_analysisplaybook grid / form | `Spaarke.Playbook.NewFromList.Command`, `Spaarke.Playbook.NewFromForm.Command` | sprk_analysisplaybook | src/client/webresources/ribbon/sprk_analysisplaybook_ribbon.xml |
| 30 | sprk_event form | `Spaarke.Event.AddMemo.Command` | sprk_memo | src/solutions/EventCommands (EventRibbonDiffXml.xml, solution export/customizations.xml) |
| — | (no command calls it yet) | `Spaarke_NewAnalysis` (`sprk_analysis_commands.js`) added to `create-launchers.json` (verifier K3) | sprk_analysis | — |
| 31 | sprk_kpiassessment subgrid on matter | `sprk.matter.subgrid.kpi.AddKpiButton.Command` ("+ Add KPI") | sprk_kpiassessment | src/solutions/SpaarkeCore/entities/sprk_matter/RibbonDiff/add-kpi-ribbon.xml |

### Reviewed, not create commands (unchanged)

| Command | Why |
|---|---|
| `sprk.Wizard.*.SummarizeFiles.Command` | Summarizes uploaded files (`/api/workspace/files/summarize`). It creates nothing itself. Its optional follow-on cards (create project, work on analysis) are separate creates, refused server-side without the privilege. |
| `sprk.Wizard.*.FindSimilar.Command` | Search only. |
| `sprk.SecureChild.<table>.New.Command` (9 tables), `Mscrm.AddNewRecordFromSubGridStandard` overrides | Already `Mscrm.CreateSelectedEntityPermission` (task 147 kept the platform rule). |
| `Sprk.Registration.Approve/Reject.*` | Admin action on a registration request, not a create button. Server-gated. |
| `sprk.Navigator.*.Open`, `sprk.Global.*`, `sprk.matter/project.fieldmapping.push`, `sprk.Document.*` (check-in, delete, …), `Spaarke.ChatContextMap.RefreshCache`, theme menus | Not creates. |
| Access ribbon (`sprk.Access.*`, Share commands) | Not touched (tasks 175/179 own it). |

### Secondary-table gaps (a wizard ruled on its primary table only)

- Create Work Assignment also creates a `sprk_event` (`workAssignmentService`).
- Create Event and Create To Do create `sprk_document` rows for attached files (`createDocumentRecords`).
- The create wizards' follow-on cards (e.g. a Summarize Files "create project" card, the matter wizard's follow-ons) create other tables.

A user with Create on the primary table but not on the secondary one still sees the button. That secondary create
fails with the platform privilege error, or the BFF's server-side check (`prvCreate*` in
`QuickCreateSourceAccessFilter` / `TodoSourceAccessFilter` / `EventEndpoints` / `AnalysisAuthorizationFilter`). No
data is written wrongly. Hiding the button on every secondary table would also hide it from users who can do the
primary job, so this is deliberate.

## Repo-vs-dev drift (read-only comparison, 2026-10-09)

1. **`src/dataverse/solutions/SpaarkeMaster/` matches dev** for every deployed create command (same ids, actions,
   rules; exported 2026-10-08). It is the source that mirrors dev.
2. **`infrastructure/dataverse/ribbon/` is partly stale or never deployed:**
   - The 10 `createtodo-button.xml` snippets (Create To Do on analysis, budget, communication, contact, document,
     event, invoice, organization, project, work assignment) are **not on dev**. Dev has Create To Do on the matter
     form only.
   - `ProjectRibbons/customizations.xml` and `EventRibbons/customizations.xml` (Upload Documents, Summarize Files, Find
     Similar, Playbook Library on the project and event forms) are **not on dev**.
   - `MatterRibbons/customizations.xml` matches dev's seven matter form wizard buttons, except the library name: the
     repo has `$webresource:sprk_wizard_commands.js`, dev has `$webresource:sprk_wizard_commands`.
   - The grid "New … wizard" commands for matter, project, event and document are **on dev and in SpaarkeMaster**,
     but not in `infrastructure/dataverse/ribbon/` (only the work assignment one is, in `WorkAssignmentRibbons/`).
   - `README.md` there lists only the form wizard buttons and says their only rules are `FormStateRule Existing`.
     Updated in this task.
3. **Not on dev at all**: `sprk_analysisplaybook_ribbon.xml` (New Playbook), `src/solutions/EventCommands` (Add Memo
   and the other event commands), and `add-kpi-ribbon.xml` (+ Add KPI).
4. **The 10 `createtodo-button.xml` snippets were not well-formed XML.** Their comment contained
   `--publish-changes`, and a double hyphen is not allowed in an XML comment. No tool could merge them. The generator
   comment is reworded and the files are regenerated.

## (3) Deploy commands, in order (main-session gate G180-1; nothing was run live)

Prerequisites: none (no web resource or BFF change; the rule is declarative). Do not run it concurrently with another
ribbon import into matter / project / work assignment / event / document / analysis / email (tasks 175/179's
`Set-AccessRibbon.ps1`, task 147's `Deploy-SecureChildNewCommands.ps1`). Run before or after them, not during.

Stale exports (verifier K1/K2, fixed): every importer of these tables now refuses an export that lacks anything live or holds an older version of it (ids AND normalised content of each command, rule, custom / hide action and label; content compare added in the re-check of e090df15b).
- `Set-CreatePrivilegeRibbon.ps1 -Apply` and `Deploy-SecureChildNewCommands.ps1` (with `-ExportDir`) both run
  `infrastructure/dataverse/ribbon/Test-RibbonExportCurrent.ps1`. It checks every unmanaged ribbon command, rule,
  custom action, hide action and label the environment holds for each exported table, and it runs before anything is
  written.
- `Deploy-SecureChildNewCommands.ps1` still takes a caller-supplied export, but one taken before this task's import
  can no longer strip the `sprk.CreatePrivilege.*` rules.
- `Set-AccessRibbon.ps1` exports fresh itself.
- The modern "New Document" (13a) is hidden by step 3 (`-Apply`, owner decision 2026-10-09). The dry run (step 2)
  shows the planned hide.

```powershell
cd infrastructure/dataverse/ribbon/CreatePrivilegeRibbons
pwsh ./Set-CreatePrivilegeRibbon.ps1                                                          # 1. dry run (sources)
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com     # 2. + live read, read-only
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply      # 3. LIVE (exports
#    SpaarkeAccessRibbons, SpaarkeSecureChildRibbons, AnalysisRibbons, EmailRibbons; refuses before any import if an
#    export would delete a live command or a create-command table is uncovered; merges; imports with publish; verifies)
pwsh ./Set-CreatePrivilegeRibbon.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify `
    -BeforeList <WorkDir printed by step 3>/before.json                                       # 4. read-only -Verify
```

The `-Verify` is per ribbon (per host table): each of the 15 host tables in `create-launchers.json` is checked. Every
create command must carry its rule, and the rule must be exactly the canonical one. Every before-command must still be
present.

Live gate G180-2 (after step 4, main session): a read-only test role (Create removed on matter, project, work
assignment, event, to do, document, analysis) sees no custom create command on the Matter, Project and Work Assignment
forms and home grids. Summarize Files and Find Similar still show. A Spaarke Basic User still sees them all.

## Coordinator answers to the open questions (2026-10-09)

1. Summarize Files stays unruled: it creates nothing itself.
2. sprk_analysis is the right table for Playbook Library.
3. No CI check for `create-launchers.json`: the dry run is the guard.
4. The never-deployed repo buttons stay as they are, filed as #1521 for the owning projects. These are the 10 Create To
   Do snippets, the Project/Event form wizard buttons, New Playbook, Add Memo and + Add KPI.

## Dev evidence (read-only, 2026-10-09)

- Unmanaged create commands on dev: rows 1–13 above. Their definitions came from `ribboncommands`, their locations
  from `ribbondiffs` (the four `HomepageGrid.<table>.NewRecord.Hide` entries confirm the grid wizards replace the
  platform New), and their rules from `ribbonrules`.
- Solutions holding the tables (`solutioncomponents`, type 1):
  - SpaarkeAccessRibbons: matter, project, work assignment.
  - SpaarkeSecureChildRibbons: event, document, communication, invoice, report card, budget, KPI assessment, to do,
    billing event.
  - AnalysisRibbons: analysis (full), plus document and analysis playbook (shell).
  - EmailRibbons: email (shell).
  - The single-table solutions (MatterRibbons, ProjectRibbons, ...) hold their table as a shell.
