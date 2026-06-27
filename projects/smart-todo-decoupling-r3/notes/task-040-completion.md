# Task 040 - Parent-Form Subgrids - Completion Notes

> **Project**: smart-todo-decoupling-r3
> **Task**: 040 - Add To Dos subgrid to all 11 parent-entity main forms
> **Status**: ✅ Primary deliverables complete; ribbon deployment partial (see below)
> **Date**: 2026-06-08

---

## Critical preflight finding (entity-set name)

The sprk_todo entity-set name on spaarkedev1 is `sprk_todos`, **NOT** `sprk_todoes`.

Tasks 007 and 020 may have assumed `sprk_todoes` (Dataverse auto-pluralization rule
for nouns ending in "o" sometimes produces "oes"). The actual platform value is
`sprk_todos` (verified via Dataverse describe; `Collection Name: sprk_todos`).

**Impact**: Any BFF / SmartTodo / external-spa / LegalWorkspace code that
references the OData entity-set as `sprk_todoes` will get a 404. A one-line audit
across those four surfaces is warranted. The string to search for is
`sprk_todoes` (and equivalent case-insensitive variants).

This finding does NOT affect task 040 itself - the subgrid binds by
RelationshipName (schema name), not by entity-set name.

---

## Deliverables

### 1. Subgrid on all 11 parent-form main forms - DEPLOYED + VERIFIED

Script: `scripts/Deploy-TodoSubgridsToElevenParentForms.ps1`

All 11 entity main forms now have a "To Dos" tab containing a subgrid bound to
the `sprk_todo` entity via the appropriate `sprk_regarding*` relationship.

| Entity              | Main form                   | Form ID                              | Relationship                                                 |
|---------------------|-----------------------------|--------------------------------------|--------------------------------------------------------------|
| sprk_matter         | Matter main form            | 4fa382f2-c273-f011-b4cb-6045bdd6a665 | sprk_sprk_matter_sprk_todo_RegardingMatter                   |
| sprk_project        | Project main form           | 5aa00242-5212-f111-8342-7ced8d1dc988 | sprk_sprk_project_sprk_todo_RegardingProject                 |
| sprk_event          | Event main form             | eaf22dcb-9aff-f011-8406-7c1e525abd8b | sprk_sprk_event_sprk_todo_RegardingEvent                     |
| sprk_communication  | Email main form             | e19b728c-5d0f-f111-8342-7ced8d1dc988 | sprk_sprk_communication_sprk_todo_RegardingCommunication     |
| sprk_workassignment | Work Assignment main form   | 7e578eef-761d-f111-88b3-7c1e520aa4df | sprk_sprk_workassignment_sprk_todo_RegardingWorkAssignment   |
| sprk_invoice        | Invoice main form           | 93aa1c69-0406-f111-8406-7c1e525abd8b | sprk_sprk_invoice_sprk_todo_RegardingInvoice                 |
| sprk_budget         | Budget main form            | 2da25f11-1009-f111-8407-7c1e520aa4df | sprk_sprk_budget_sprk_todo_RegardingBudget                   |
| sprk_analysis       | Analysis main form          | d408a721-77d7-f011-8406-7c1e525abd8b | sprk_sprk_analysis_sprk_todo_RegardingAnalysis               |
| sprk_organization   | Organization main form      | 07000450-aa00-f111-8406-7c1e525abd8b | sprk_sprk_organization_sprk_todo_RegardingOrganization       |
| contact (OOB)       | Contact main form           | 1fed44d1-ae68-4a41-bd2b-f13acac4acfa | sprk_contact_sprk_todo_RegardingContact                      |
| sprk_document       | Document main form          | 9088d6a4-4cf2-f011-8406-7c1e520aa4df | sprk_sprk_document_sprk_todo_RegardingDocument               |

**Subgrid configuration** (uniform across all 11):
- Tab name: `tab_todos_<entity>`; collapsed by default
- Control type: classid `{E7A81278-8635-4D9E-8D4D-59480B391C5B}` (subgrid)
- RecordsPerPage = 30; AutoExpand = Auto
- EnableQuickFind = false; EnableViewPicker = **true** (exposes All view)
- Default ViewId = `{DE2C9B75-74EB-40F4-92D8-DEABE0521E33}` (Active To Dos)

### 2. Saved queries - DEPLOYED + VERIFIED

- **Active To Dos** (pre-existing, default view since task 002):
  - savedqueryid `de2c9b75-74eb-40f4-92d8-deabe0521e33`
  - FetchXml filter: `statecode eq 0` (Active state - includes statuscode IN
    (1=Open, 659490001=In Progress) per task 009 customization, since
    statuscode 2=Completed and 659490002=Dismissed both flip statecode to 1)
  - querytype = 0 (Public); isdefault = true

- **All To Dos** (created by this task):
  - savedqueryid `9977d599-4763-f111-ab0c-000d3a4d8152`
  - FetchXml: no state filter; columns name / statuscode / sprk_duedate /
    sprk_assignedto / createdon
  - querytype = 0 (Public); isdefault = false

**Parameterization approach**: views are NOT parameterized by lookup. Instead,
subgrid binding via `RelationshipName` + `TargetEntityType` causes Dataverse to
auto-filter rows where the lookup attribute on the relationship references the
parent form's record. This is the standard 1:N subgrid pattern and produces
direct OData parity with `?$filter=_sprk_regarding<entity>_value eq <id>`.

### 3. Ribbon "Create To Do" command - PARTIAL

**Matter (sprk_matter): DEPLOYED + VERIFIED** via UDSS-041
(`infrastructure/dataverse/ribbon/MatterRibbons/customizations.xml`,
`sprk.Wizard.Matter.CreateTodo.*`). Handler:
`Spaarke.Commands.Wizards.openCreateTodoWizard` in `sprk_wizard_commands.js`,
which already reads entityType + entityId from primaryControl and passes them
in the wizard URL per the createtodo-launch-contract.

**Other 10 entities: SOURCE DRAFTED, DEPLOYMENT DEFERRED**

The script `scripts/Generate-CreateTodoRibbonXmlForTenEntities.ps1` produces
per-entity ribbon-diff XML fragments at:

- `infrastructure/dataverse/ribbon/ProjectRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/EventRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/CommunicationRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/WorkAssignmentRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/InvoiceRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/BudgetRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/AnalysisRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/OrganizationRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/ContactRibbons/createtodo-button.xml`
- `infrastructure/dataverse/ribbon/DocumentRibbons/createtodo-button.xml`

Each fragment is a self-contained `<ImportExportXml>` ready to merge into a
dedicated ribbon solution's `customizations.xml`. Deployment follows the
ribbon-edit skill workflow:
1. Create unmanaged dedicated solution per entity (`<DisplayName>Ribbons`)
2. Add only the entity's metadata
3. Export, merge the fragment into customizations.xml, re-pack, import,
   publish.

**Why deferred**: per `.claude/skills/ribbon-edit/SKILL.md`, ribbon edits are
solution-import-based and require dedicated per-entity solutions to be
established in the maker portal before fragments can be merged. Programmatic
ribbon writes via direct Web API PATCH are not a supported pattern.

**FR-17 acceptance impact**: the "Create To Do" command on the subgrid command
bar (or form command bar) is one of three FR-17 criteria. The 10-entity
follow-up is a clean, mechanical, source-controlled hand-off rather than
silent ribbon manipulation. Until deployed, users on non-Matter forms must
use the subgrid's built-in "+ New" (creates an empty `sprk_todo` linked via
the relationship) - functional but does NOT invoke the wizard with pre-fill.

---

## Smoke test results

Read-through verification (no UI access) - see `verify-040.ps1` output captured
in the task execution log:

- ✅ 'All To Dos' saved query created (querytype=0)
- ✅ 'Active To Dos' saved query pre-existing (statecode=0 filter)
- ✅ Matter form patched: `tab_todos_sprk_matter` present, relationship binding
  correct, ViewId = Active To Dos GUID, view picker enabled
- ✅ Contact form patched (OOB entity, no `sprk_` prefix): subgrid present,
  uses `sprk_contact_sprk_todo_RegardingContact` relationship
- ✅ All 11 forms confirmed to have `tab_todos_<entity>` after patch

---

## Per-entity quirks

- **contact** (OOB): relationship schema name lacks the `sprk_sprk_` prefix
  (just `sprk_contact_sprk_todo_RegardingContact`). Subgrid + ribbon both
  reference 'contact' as lowercase logical name, 'Contact' as display name.
- **sprk_communication**: primary "main form" is named **Email main form**, not
  "Communication main form" (legacy naming). Patched correctly.
- **sprk_event**: has 9 active forms (Action / Task / Communication / Approval /
  Meeting side-pane forms, plus modal + Assign Work). Patched the canonical
  "Event main form" only. The other 8 do NOT have the subgrid - intentional,
  they are narrow-purpose side panes.
- **sprk_document**: subgrid added to Document main form. Note that the
  `sprk_document` entity also surfaces in the All Documents code page;
  the subgrid here is for the entity main-form page specifically.

---

## Blockers / open considerations

- **Entity-set name `sprk_todos` vs assumed `sprk_todoes`** - one-line audit
  across BFF + SmartTodo + external-spa + LegalWorkspace recommended (see top).
- **Ribbon deployment for the 10 non-Matter entities** is a manual follow-up
  per ribbon-edit skill prerequisites. The source XML is committed; the
  per-entity dedicated solution setup is the gating step.
- The Active To Dos view filters by `statecode=0` rather than explicit
  `statuscode IN (1, 659490001)`. These are functionally equivalent per the
  task-009 statuscode map but the spec calls out the explicit pair - if a
  future statuscode is added with `statecode=0` it would be included by the
  current filter. Trade-off documented; no change needed for current
  acceptance criteria.

---

## Files produced / modified

- `scripts/Deploy-TodoSubgridsToElevenParentForms.ps1` (fixed bugs:
  systemform `$orderby=createdon` invalid; added preferred form names for
  all 10 non-Matter entities)
- `scripts/Generate-CreateTodoRibbonXmlForTenEntities.ps1` (new)
- `infrastructure/dataverse/ribbon/<Entity>Ribbons/createtodo-button.xml`
  (10 files; 4 with new parent folders: WorkAssignment, Invoice, Budget,
  Organization, Contact - per the OOB Contact entity)
- `projects/smart-todo-decoupling-r3/notes/task-040-completion.md` (this file)

Modified entities in spaarkedev1: 11 main forms (formxml) +
1 new savedquery + publish for 11 entities.
