# W5 open questions: the repair marker (Q1) and the live quick-create flags (Q2)

Task 050. Read-only investigation, 2026-11-02. Resolves spec "Unresolved Questions" Q1 (repair marker) and Q2 (live quick-create flags). Feeds task 051 (FR-24 table list) and task 052 (FR-25 selection query).

Deviation (coordinator-approved): the POML lists task 019 as pending. Dev already runs the W2 create core (task 017, BFF `fcf01480f4`), so Q1 is characterised now against that build. Re-run the live createdby sample after 019 closes if the owner wants it on the final build.

No write, publish or import was performed against any environment. The list of read calls is at the end.

---

## Q1. Is `createdby` != the BFF application user a safe marker for "created outside the product" (FR-25)?

### Verdict

**Conditional yes. The comparison is usable, but not as written ("!= the BFF application user", one identity). Use "`createdby` not in the set of product principals".** Escalated to the owner (see "Escalation").

What the evidence says:

1. **No product path creates a row under impersonation.** The Communication routes use `MSCRMCallerID` for reads and for a PATCH of an existing row. The entity-create sender takes no impersonation argument. `CallerObjectId` is never sent by any production call site. `createdonbehalfby` was empty on all 2,446 sampled live rows. So the spec's Q1 worry ("a product create under impersonation") does not occur by design.
2. **"OBO" in this codebase means OBO for the checks, not for the write.** The W2 core (`RecordCreationService`) runs WhoAmI and the access checks under the caller's token, then issues exactly one app-only create. So `createdby` is the BFF app user for the child-records route, AI create, email draft, Office quick-create and budget inquiry. The premise "most product creates are OBO, so `createdby` is the user" is false for the server routes.
3. **Product rows whose `createdby` is NOT the BFF app user do exist**, and they match the literal rule:
   - rows written by the Signal and Decision Record writers, which use a different application user (`mi-ontology-writer-dev`);
   - rows created in the browser as the user by the Matter and Project wizards (and LegalWorkspace Create Matter), until W3 moves them (live: matter 2026-10-22 and project 2026-10-27 were created by a person);
   - the four legacy OBO create calls in the BFF (table below, route B).
4. **"The BFF application user" is not one identity.** Dev has two BFF application users (`SDAP-BFF-SPE-API`, last create 2026-08-17; `mi-bff-api-dev`, first sampled create 2026-08-18, everything since). Prod has `spaarke-bff-api-prod`. A single configured id would classify every row written by the other identity as "native".
5. **Why the over-selection is benign:** Repair is fill-only, one conditional write, idempotent, and skips append-only and kept-creator tables (`RecordCreationService.Repair.cs:36-46`). Selecting a product row costs one read; it does not corrupt it.
6. **The under-selection is a real gap.** App-only creates that bypass the core also have `createdby` = BFF app user, so the marker can never select them (route D below). Repair covers them only if FR-21 / FR-18 / FR-19 route them through the service, or the census names them as exceptions.

### Marker to use (proposal, not implemented)

**A (recommended).** Select rows with `createdon >= max(go-live, watermark)` and `createdby NOT IN ProductPrincipals`.

- `ProductPrincipals` = the shared BFF identity (resolved at runtime by `IGenericEntityService.GetCurrentIdentityIdAsync`, WhoAmI, cached: `DataverseServiceClientImpl.cs:2715`; and by the Web API service's credential, which is the same application) plus the ontology writer (`OntologyWriterDataverseClient.GetWriterUserIdAsync`, `OntologyWriterDataverseClient.cs:224`) plus an explicit configured list for retired identities (for example `SDAP-BFF-SPE-API`).
- People are never in the set. A person-created row (native, import, or a pre-W3 browser wizard) is "origin unknown, repair it". That is correct and cheap.
- Tables whose writer is excluded from mapping (`sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision`) should not be in the repair table list at all (`RecordTableRules.cs:392-415`; `sprk_decisionrecord` is already append-only in Repair, `RecordCreationService.Repair.cs:44-46`).

**B (not recommended now).** A server-written origin column on every target table, set by the core at step 11. Independent of identity, but adds a Dataverse column to about 19 tables (CLAUDE.md section 11, cost-of-doing-nothing is weak because A works), needs every creator to set it, and a row copied or imported with the value is forged. Keep as the fallback if maintaining the principal set proves brittle.

**C (rejected).** `createdonbehalfby`. It is empty on every native row and every product row (0 of 2,446 sampled), so it cannot separate them. It would only mark the impersonated-upsert corner (below).

### Principal per route (`createdby`)

Dev BFF `fcf01480f4`. "Live" is the newest sampled rows per table (top 500 by `createdon`, 19 tables, read-only GET).

| # | Route | Principal that writes | `createdby` | Evidence |
|---|---|---|---|---|
| A | **W2 core** (`RecordCreationService.CreateAsync`, step 11). Callers: child-records route `Api/ChildRecordEndpoints.cs:230`; AI create (Owned path) `Services/Ai/Handlers/DataverseCreateRecordHandler.cs:435`; email draft (communication) `Services/Ai/Handlers/EmailDraftToolHandler.cs:457`; Office quick-create `Services/Office/OfficeService.cs:2441`; budget inquiry `Services/Signals/Actions/BudgetInquiryExecutor.cs:281` | Caller token for WhoAmI and checks only (`Services/RecordCreation/RecordCreationService.cs:322-348`); the write is app-only | **BFF app user** | `RecordCreationService.cs:208-220` ("ONE app-only create": `_appOnly.UpdateRecordFieldsAsync(table, id, fields, ct)`, no impersonation argument); `Spaarke.Dataverse/DataverseWebApiService.cs:2098-2121` (impersonation parameter defaults to null). Live: the sampled project, work assignment, to do, event, document and communication rows created since 2026-10-28 are all `mi-bff-api-dev` |
| B | **Legacy OBO create** (`IDataverseUserClient.PostAsync`): AI create fall-through for tables not on the Owned path `DataverseCreateRecordHandler.cs:454`; email draft for non-communication tables `EmailDraftToolHandler.cs:478`; reporting catalog `Api/Reporting/ReportingEndpoints.cs:450`; ontology admin `Services/Ontology/OntologyAdminAuthoringService.cs:1017,1188` and `OntologyAdminClassificationService.cs:190` | The calling user, MSAL on-behalf-of token | **the user** (not the app, not impersonated) | `Infrastructure/Dataverse/DataverseUserClient.cs:121-126,198` (`AcquireTokenOnBehalfOf`); `IDataverseUserClient.cs` remarks. Which registry tables can reach B on the AI routes is not enumerated here (the kept-creator ownership exceptions); the reporting and ontology-admin targets are not mapping targets |
| C | **Browser as the user** (`Xrm.WebApi.createRecord`): Create Matter wizard `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts:338`; Create Project wizard `.../CreateProjectWizard/projectService.ts:368`; LegalWorkspace Create Matter `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:222`. Other wizard tables route through the BFF adapter (`BFF_CHILD_CREATE_TABLES`) to route A | The user | **the user** | Live: sprk_matter 2026-10-22 and sprk_project 2026-10-27 created by a person. Ends when W3 (FR-15/16, tasks 070-072) ships and the census guard forbids client creates |
| D | **App-only, outside the core**: `IGenericEntityService.CreateAsync` (`DataverseServiceClientImpl.cs:2595`): document `:322`, analysis `:542`, communication race-proof `:3666-3672`; `DataverseWebApiService.CreateEventAsync` `:587` (`SendPostAsJsonAsync` `:249-253`); Office to do `Services/Office/OfficeService.cs:3064`; playbook create-task `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:349`; Communication create-task `Services/Communication/CommunicationCreateTaskApplyService.cs:346`; ingestion `Services/Communication/IncomingCommunicationProcessor.cs:1135,1170,1337`; **ebilling persister** `Services/Finance/Submission/InvoiceSubmissionPersister.cs:590,593,624` (one transaction: `DataverseServiceClientImpl.cs:3026`) and `Services/Finance/InvoiceLineDecisionService.cs:391,707` | The BFF's shared application identity | **BFF app user** | `DataverseServiceClientImpl.cs:300-303` ("createdby is the application user here"); `CommunicationCreateTaskApplyService.cs:29-45,343-346` ("only createdby is the app user"). Never selected by the marker; their invariants are the W3 / FR-21 job |
| E | **Signal and Decision Record writers** (`SignalWriter.cs:653`, `DecisionRecordWriter.cs:2163`, via `OntologyWriterDataverseClient.CreateAsync` `OntologyWriterDataverseClient.cs:241-242`) | A dedicated managed identity, not the shared BFF application user | **`mi-ontology-writer-dev`** | `OntologyWriterDataverseClient.cs:1-60` (class remarks, F7/F13/F14: never routed through the shared identity); `Infrastructure/DI/SignalsModule.cs:42-53`. Live: 280 of 280 signals, 51 of 51 decision records, 2 of 3 budget revisions |
| F | **Impersonated requests** (`MSCRMCallerID`) | The BFF app, acting as a user | n/a, none of them creates | See "Impersonation inventory" below |

Live creator counts (top 500 per table, 2,446 rows, 0 with `createdonbehalfby`):

| Table | BFF app (`mi-bff-api-dev` + `SDAP-BFF-SPE-API`) | Person | Ontology writer |
|---|---|---|---|
| sprk_document | 413 | 87 | 0 |
| sprk_communication | 321 | 27 | 0 |
| sprk_analysis | 492 | 8 | 0 |
| sprk_notificationoutbox | 401 | 2 | 0 |
| sprk_todo | 37 | 40 | 0 |
| sprk_event | 19 | 73 | 0 |
| sprk_workassignment | 10 | 22 | 0 |
| sprk_matter | 4 | 61 | 0 |
| sprk_project | 2 | 27 | 0 |
| sprk_invoice | 1 | 10 | 0 |
| sprk_budget | 1 | 4 | 0 |
| sprk_invoicesubmission | 1 | 0 | 0 |
| sprk_kpiassessment, sprk_memo, sprk_reportcard, sprk_billingevent | 0 | 24, 21, 3, 1 | 0 |
| sprk_signal | 0 | 0 | 280 |
| sprk_decisionrecord | 0 | 0 | 51 |
| sprk_budgetrevision | 0 | 1 | 2 |

The "Person" column mixes browser-wizard, native and test rows; after the W2 / W3 deploys the newest rows per table are the BFF app. The two BFF application users: `SDAP-BFF-SPE-API` `bb5a90e5-4ca8-f011-bbd3-7c1e5215b8b5` (last sampled create 2026-08-17T18:19Z) and `mi-bff-api-dev` `8793f4b0-01db-f011-8406-7c1e520aa4df` (first sampled 2026-08-18T02:13Z, current). Writer: `mi-ontology-writer-dev` `3121bf1b-9fbf-f111-aaaf-0022482913fc`.

### Impersonation inventory (does anything set `MSCRMCallerID` or `CallerObjectId`?)

- **The one place the header is set** is `Spaarke.Dataverse/DataverseImpersonation.cs:64-73` (`ApplyAsSystemUser`, `MSCRMCallerID`), called only from `DataverseWebApiService.CreateAuthenticatedRequestAsync` (`:214-233`) when `impersonateSystemUserId` is supplied.
- **`CallerObjectId` is never sent.** `ApplyAsEntraUser` (`DataverseImpersonation.cs:90`) has no call site in `src/` outside tests.
- **Senders that pass an impersonation id:** GET (`SendGetAsync` `:240-242`; `RetrieveMultipleRowsAsync` `:1235-1251`; `QueryEvents` `:517-524`; principal-access reads `:1659`, `:1719`), PATCH (`SendPatchAsJsonAsync` `:265-267`; `UpdateRecordFieldsAsync` `:2098-2121`; `UpdateExistingRecordFieldsAsync` `:2161-2181`, with `If-Match: *`), and the share `RevokeAccess` action for the owner's own share (`:1443`, `:1492-1496`).
- **The entity-create sender has no impersonation parameter:** `SendPostAsJsonAsync` (`:249-253`) calls `CreateAuthenticatedRequestAsync(HttpMethod.Post, url, ct)`. The ServiceClient path (`CreateAsync`) has no impersonation at all.
- **Communication routes:** `CommunicationEndpoints.cs:204-313` (reads, via `IImpersonatedCommunicationQuery`, `CommunicationModule.cs:297`) and `:386-488` (apply / undo: a PATCH of an existing row under the confirming user). The Job C create goes through `IActionSeam.CreateTaskAsync` app-only, then a caller-impersonated PATCH (`CommunicationCreateTaskApplyService.cs:343-346,389`).
- **Corner, not a designed create path:** `UpdateRecordFieldsAsync` has no `If-Match`, so it is an upsert. With an impersonation id (`Services/Dataverse/DataverseUpdateHandler.cs:253` for a person-started playbook update-record; `Api/FieldMappings/FieldMappingEndpoints.cs:1189-1190` for /push) a PATCH to an id that does not exist would create a row with `createdby` = that person and `createdonbehalfby` = the BFF app. /push takes the id from a read of existing children; update-record guards existence only for the invoice family. Under marker A such a row is selected (the person is not in the set), which is the benign case. No action beyond awareness.

### Residual risk

- The principal set must be kept current when an identity changes (dev changed on 2026-08-17/18). Resolve the BFF identity at runtime, never from a constant, and list retired identities in configuration. Pre-go-live rows are excluded by the go-live floor anyway.
- Route D rows are invisible to the marker. FR-21's census must show each as routed to the service or a named exception.
- Only dev was read (see Q2 "Limits"). The principal names above are dev's; prod's are `spaarke-bff-api-prod` and the prod writer identity and were not read.

### Escalation (CLAUDE.md section 6)

POML trigger fired: product rows (writer MI rows, pre-W3 browser wizard rows, legacy OBO rows) match a literal "`createdby` != BFF app user" selection. Owner decisions requested:

1. Adopt marker A (principal set) in place of the single-identity rule in spec FR-25 and design section 4.4 B.
2. Confirm `sprk_signal`, `sprk_decisionrecord`, `sprk_budgetrevision` stay out of the repair table list.
3. FR-21 says the Signal and Decision Record writers create "through the system principal" of the service. The service's system principal writes under the shared BFF identity (`RecordCreationService.cs:220`), but these writers were deliberately given a dedicated managed identity (`OntologyWriterDataverseClient.cs` remarks; `SignalsModule.cs:42-53`). Confirm whether FR-21 keeps the dedicated identity (rows stay `createdby` = writer; marker A still holds) or moves them to the shared one (their identity isolation is lost). This belongs in tasks 060 / 063 as well.

---

## Q2. Live quick-create flags (FR-24)

### Method and limits

Dataverse Web API `EntityDefinitions` (`IsQuickCreateEnabled`) and `systemforms` (type 7) on `https://spaarkedev1.crm.dynamics.com`, read-only. The solution-XML column is `src/dataverse/solutions/SpaarkeMaster/Entities/<table>/Entity.xml`. **Only dev was readable here; no other environment was read.** The POML trigger "flag differs between environments" is therefore untested, not cleared. The scripted `-Verify` (task 051) must report per environment.

### Findings

- **Only `sprk_project` has quick create on, live and in the solution XML.** Every other Spaarke table in the registry is off live and in XML.
- **Matter is off** (XML `0`, live false), contrary to the design note (`design.md:127`, spec Q2) that says the XML enables it.
- **"Invoice Line Item" is `sprk_billingevent`** (display name "Invoice Line"; `InvoiceTotalsService.cs:46`). It is off live and `0` in XML. There is no `sprk_invoicelineitem` table on dev or in the solution (`design.md:762` keeps it out until ebilling deploys it, #2348).
- **No architecture doc says the flag is off per table.** `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:40` only lists quick-create as a non-product write class. The "off" claim is the comment in `scripts/Deploy-SecureChildNewCommands.ps1:32` ("IsQuickCreateEnabled = false on every child table (live)"), which covers child tables, not the roots (Project is a root).
- **`sprk_project` has the flag on but no quick-create form** (no type 7 `systemform` for `sprk_project` on dev). What the platform shows in that state was not exercised.
- **Active quick-create forms exist for five tables whose flag is off**: sprk_matter, sprk_document, sprk_event, sprk_invoice, sprk_kpiassessment (plus `sprk_aitopicregistry`, an unrelated table). They are dormant while the flag is off and would return if it is flipped on.
- **Event "Add Memo" opens `Xrm.Navigation.openForm({ entityName: "sprk_memo", useQuickCreateForm: true, ... })`** (`src/solutions/EventCommands/sprk_event_ribbon_commands.js:552-566`). `sprk_memo` is off live and has no quick-create form on dev, so what Add Memo does today is a question for task 051 (live UI check); it was not exercised here (read-only).
- **Platform tables**: `task` is ON (platform default); `account`, `contact`, `email` are off. `task` is in the registry only as a chat target and is excluded from mapping (`RecordTableRules.cs`, `ChatPlatformTableIsNotMapped`).

### Per-table table

Live = dev `IsQuickCreateEnabled`. QC form = active type 7 form on dev. XML = SpaarkeMaster `Entity.xml`. Doc claim = what the repo's docs say.

| Table | Live | Active QC form (dev) | Solution XML | Doc claim |
|---|---|---|---|---|
| sprk_matter | false | yes ("Matter quick create") | 0 | design.md:127 / spec: XML says on (wrong: XML is 0) |
| **sprk_project** | **true** | **none** | **1** | design.md:127 / spec: XML says on (right); no doc says off |
| sprk_workassignment | false | none | 0 | child-table off, Deploy-SecureChildNewCommands.ps1:32 |
| sprk_todo | false | none | 0 | same |
| sprk_event | false | yes | 0 | same |
| sprk_memo | false | none | 0 | same (Add Memo uses `useQuickCreateForm`, see above) |
| sprk_invoice | false | yes | 0 | same |
| sprk_reportcard | false | none | 0 | same |
| sprk_analysis | false | none | 0 | same |
| sprk_document | false | yes | 0 | same |
| sprk_budget | false | none | 0 | same |
| sprk_kpiassessment | false | yes | 0 | same |
| sprk_billingevent ("Invoice Line Item") | false | none | 0 | design.md:127 / spec: XML says on (wrong: XML is 0) |
| sprk_invoicesubmission | false | none | 0 | same (child) |
| sprk_invoiceapproval | false | none | 0 | same |
| sprk_externalidentifier | false | none | 0 | same |
| sprk_invoicecorrection | false | none | 0 | same |
| sprk_communication | false | none | 0 | same |
| sprk_servicerequest | false | none | 0 | out of scope (external-access-r3) |
| sprk_signal | false | none | 0 | ontology writer table, never user-created |
| sprk_decisionrecord | false | none | 0 | same |
| sprk_budgetrevision | false | none | 0 | same |
| task (platform) | **true** | n/a | n/a (not in SpaarkeMaster) | not a Spaarke table; not a mapping target |
| account / contact / email (platform) | false | account, contact have the platform QC form | n/a | not mapping targets |

### FR-24 table list (for task 051)

- **Verify off (`-Verify`, per environment): the 22 `sprk_` tables in `RecordTableRules.All`** (matter, project, workassignment, todo, event, memo, invoice, reportcard, analysis, document, budget, kpiassessment, billingevent, invoicesubmission, invoiceapproval, externalidentifier, invoicecorrection, communication, servicerequest, signal, decisionrecord, budgetrevision). Ribbon "New" redirect applies to the user-creatable tables only (not the invoice-family system tables, the three ontology tables, or service request, which stays with external-access-r3).
- **Change required on dev: `sprk_project` only** (flag true to false, in SpaarkeMaster `Entity.xml` `IsQuickCreateEnabled` 1 to 0 and live). Nothing else needs flipping.
- **Report only, not changed:** `task` (platform, on), and the dormant QC forms on matter, document, event, invoice, kpiassessment.
- Task 051 also owns the Add Memo / Add KPI replacement and a live UI check of what Project "+ New" does with the flag on and no form.

---

## Read calls made (all read-only; no MCP write tool, no POST/PATCH/DELETE)

Auth: `az account show`; `az account get-access-token --resource https://spaarkedev1.crm.dynamics.com`. Every Dataverse call below is an HTTP GET.

1. `systemusers?$filter=applicationid ne null` (application users)
2. `EntityDefinitions?$select=LogicalName,ObjectTypeCode,IsQuickCreateEnabled,OwnershipType,IsCustomEntity&$filter=IsCustomEntity eq true` (a first attempt with `startswith` was rejected by the server, 0x8006088a)
3. `EntityDefinitions` for `account`, `contact`, `email`, `task` (`IsQuickCreateEnabled`)
4. `EntityDefinitions(LogicalName='sprk_billingevent')` (`DisplayName`, `IsQuickCreateEnabled`)
5. `systemforms?$filter=type eq 7`; `systemforms?$filter=objecttypecode eq 'sprk_project'`
6. 19 entity-set samples, `$select=createdon,_createdby_value,_createdonbehalfby_value&$orderby=createdon desc&$top=500`: sprk_matters, sprk_projects, sprk_todos, sprk_events, sprk_documents, sprk_communications, sprk_signals, sprk_decisionrecords, sprk_billingevents, sprk_invoices, sprk_workassignments, sprk_analysises, sprk_memos, sprk_budgets, sprk_kpiassessments, sprk_reportcards, sprk_invoicesubmissions, sprk_notificationoutboxes, sprk_budgetrevisions
7. `sprk_fieldmappingprofiles` (three GETs: one rejected for an unknown property, one `$top=1`, one `$select` of source/target record type and state; 7 profiles on dev: Matter to Event, Invoice, Project, Report Card, Work Assignment; Project to Event)
8. `EntityDefinitions(LogicalName='sprk_fieldmappingprofile')/Attributes` (returned an error, unused)

Repo: read-only grep and file reads under `src/`, `scripts/`, `docs/`, `projects/`. No source file changed.
