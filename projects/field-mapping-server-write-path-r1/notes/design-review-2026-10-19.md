# Design review 2026-10-19: evidence base

> **What this is**: the verified facts behind the 2026-10-19 rewrite of [`design.md`](../design.md). Checked against
> `origin/master` @ `49279def1` (2026-10-18) by read-only code investigation. File references are relative to
> `src/server/api/Sprk.Bff.Api/` unless they start with `src/`, `tests/`, `docs/` or `projects/`.
> **Use**: `spec.md` cites this file for current-state facts instead of re-deriving them. Line numbers drift; re-grep
> before you rely on one.

---

## 1. What changed since the 2026-09-29 design

| Event | Date | Effect on this project |
|---|---|---|
| word-add-in-r1 PR #960 merged; project now 136/136 tasks done | 2026-09-30 | `RecordCreationService`, `CreateTimeFieldMapping`, `RecordOwnershipResolver`, `QuickCreateSourceAccessFilter` are on master. The §9 gate is satisfied. |
| word-add-in-r1 task 080: every Office create is team-owned; backfill applied | 2026-09-30 / 10-02 | I-6 done for Office. |
| UAC-r2 task 146: I-6 applied to every other app-only BFF writer; `RecordOwnerAssignmentCensusTests` | 2026-10-02 | I-6 done for BFF writers. #1034 is still open but mostly resolved. |
| UAC-r2 task 147 (#1312): `POST /api/v1/child-records/{table}` + `OwnedChildWrite` | 2026-10-06 | **The BFF create pipeline the design planned to build exists.** 6 of 8 wizards create through it. It applies no field mapping. |
| ontology-r1 task 048 (D-66): `OwnedChildWrite` moved to `Services/Dataverse/`; chat create tool and `DecisionRouteCores` use it | 2026-10-09 | The core is shared by AI chat and decision follow-ons. |
| ADR-002 amended (word task 087, path B): WP-1 allows a platform-native declarative owner (autonumber, I-11); WP-7/WP-8 added | 2026-10-03 | Rules are now WP-1…WP-8. The registry runs I-1…I-18. |
| UAC-r2 task 156: `CoreAncestorRestamper`, `CoreAncestorStampReconciliationJob` (`IScheduledJob`, 5 min) | 2026-10 | A precedent for W5's repair pass. |
| `1473b34c6` (#2094): re-file writers (incl. field-mapping push) call `SecureChildReconciler.AfterRefileAsync` | 2026-10-18 | Push is a re-file writer; the engine swap must keep this. |
| FieldMappingAdmin PCF retired (`81fe87fb9`) | 2026-10-15 | Profiles and rules are authored on native forms only. |

## 2. The three engines

| Engine | Location | Facts |
|---|---|---|
| TS client | `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts` (633 lines) | 9 call sites in 8 wizards: matterService:322, projectService:378, eventService:368, todoService:266, invoiceService:254, workAssignmentService:472, reportCardService:241, CreateAnalysisWizardWidget:906 and :965. **Every call is inside the create/finish function. No preview exists anywhere.** Mapping runs AFTER the lookup block, so **mapping overwrites user-entered values**. |
| Push | `Api/FieldMappings/FieldMappingEndpoints.cs` `ApplyMappingRule` :1207 (file is 1350 lines) | Ignores MappingType/DefaultValue/Expression; treats every rule as Copy. ≤500 children (:432). Re-file writer: `ReparentAsync` + `AfterRefileAsync` (:1079-1164). |
| C# create-time | `Services/Office/CreateTimeFieldMapping.cs` (363 lines, `internal static`) | Copy/Default/Concat/Template; protected-attribute guard. Only caller: `RecordCreationService` (Office Matter/Project). **No unit tests**; exercised only by the Office quick-create contract tests. |

**Semantic differences** (TS vs C#; push does Copy only):

| Aspect | TS | C# |
|---|---|---|
| Null scalar source | explicit `null` is copied | skipped |
| Option set into Text | raw integer | formatted label |
| Date / Boolean into Text | raw | ISO-8601 / Yes-No |
| Default | string literal as written | converted to the target CLR type |
| Concat/Template values | `String(value)`; lookups unhandled | `ToText` (labels, ISO dates, lookup name) |
| Protected targets | none | skipped with a warning |
| Lookup write | `{nav}@odata.bind` via nav-prop discovery | `EntityReference` |

No TS↔C# parity test exists.

## 3. Configuration model

- **Tables**: `sprk_fieldmappingprofile` (source/target record type lookups, `sprk_capabilitymode` in SpaarkeMaster vs
  `sprk_compatibilitymode` in the older FieldMappingAdminSolution export, `sprk_defaultvalue`) and
  `sprk_fieldmappingrule` (source/target field + type 0-6, `sprk_mapping_type` 0 Copy / 1 Default / 2 Concat /
  3 Template, `sprk_defaultvalue`, `sprk_expression`, `sprk_executionorder`, `sprk_isactive`, `sprk_compatibilitymode`,
  `sprk_isrequired`, `sprk_iscascadingsource`, `sprk_mappingdirection`, **`sprk_syncmode` 0 One-time / 1 Manual
  Refresh**).
- **Read path**: `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:2315-2385`. **Unread rule columns**:
  `sprk_syncmode` and `sprk_mappingdirection`. **Read but unused by either engine**: `IsRequired`,
  `CompatibilityMode`, `IsCascadingSource`.
- **Authoring**: native model-driven forms + a rule subgrid (`FieldMappingAdminSolution` form XML).
- **Seeded profiles** (`DataverseWebApiFieldMappingSeeder.cs:131-180`): Matter→Event (8 rules), Matter→Invoice (4),
  Matter→Report Card (8). All Copy, lookup→lookup "attorney matrix" (attorneys, paralegals, law firms,
  internal/external). A live Matter→Work Assignment profile is deliberately unseeded. **#1318**: the seeder filter uses
  `sprk_fieldmappingprofile eq` (400); it must use `_sprk_fieldmappingprofile_value`.
- **OOB relationship mapping**: `src/dataverse/solutions/SpaarkeMaster/Other/EntityMaps.xml` has ONE map,
  `businessunit` → `sprk_matter` `sprk_ai_search_index`. It is a form-prefill relic of I-5.
- **Dead client code**: `FieldMappingHandler.ts`, the legacy AssociationResolver integration. It reads `sprk_syncmode`
  from the profile (where it doesn't exist) and has no consumer.

**Precedence bug today**: the Report Card wizard lets the user pick Law Firm 1 / Assigned External / Assigned Internal
(`CreateReportCardStep.tsx:168-186`), the exact columns the Matter→Report Card profile maps. The client applies the
mapping after the payload is built, so a matter value silently replaces the user's pick.

## 4. The two create cores

| Step | `RecordCreationService` (Office) | `OwnedChildWrite` (+ `ChildRecordEndpoints`) |
|---|---|---|
| Callers | `OfficeService.cs:2009` only | child-records route (11 tables), `DataverseCreateRecordHandler` (chat), `EmailDraftToolHandler`, `BudgetInquiryExecutor`, `DecisionRouteCores` |
| Tables | Matter, Project | todo, event, memo, invoice, reportcard, analysis, document, budget, kpiassessment, billingevent, workassignment (root) |
| Caller Create privilege | route filter | core, as caller, from metadata |
| Append / AppendTo on lookups | **no** (#1614: secure parents only) | yes, every lookup + regarding pair |
| Field-secured columns | not checked | refused |
| Source Read | yes (`QuickCreateSourceAccessFilter`) | n/a |
| Field mapping / search-index default | yes / yes (caller BU) | **no / no** |
| Owner | `ResolveOwningTeamAsync`: **caller's BU** | `ResolveOwnerAsync(ForParents)`: **parent's BU first** |
| Creator person | yes | yes |
| Autonumber retry + read-back | yes | no |
| I-1 stamp, I-16, sharee mirror | n/a (roots) | in the ROUTE after create; **the chat tool skips them** |
| Input | typed `RecordCreationRequest` | raw Web API JSON / chat item → `MappedItem` |
| Shape | sealed scoped class, 9 deps, 1264 lines | `internal static`, 1692 lines (create + re-file + pair rules + authz helpers) |

**Owner inconsistency**: a Project filed under an ordinary Matter is owned by the caller's BU team via Office, but by
the Matter's BU team via chat.

## 5. Create-surface inventory (product code)

Writers: **Xrm** = `Xrm.WebApi` as user · **C→BFF** = client → child-records route (`OwnedChildWrite`) · **BFF-own**
= bespoke server create · **RCS** = `RecordCreationService` · **OCW** = `OwnedChildWrite` in-process.

| Table | Surface | Writer | Mapping today |
|---|---|---|---|
| sprk_matter | Create Matter wizard (`matterService.ts:337`; host `solutions/CreateMatterWizard/src/main.tsx:52` uses `createXrmDataService()`) | **Xrm, user-owned** | client |
| sprk_matter | Office quick-create | RCS | server |
| sprk_matter | AI chat `dataverse.create_record` | OCW | none |
| sprk_matter | `solutions/LegalWorkspace/.../CreateMatter/matterService.ts:222` | Xrm | **dead code**, no importer of `MatterService` |
| sprk_project | Create Project wizard (`projectService.ts:393`) | **Xrm, user-owned** (secure option → provisioning reassigns) | client |
| sprk_project | Office quick-create / AI chat | RCS / OCW | server / none |
| sprk_workassignment | wizard / decision assign-work / chat | C→BFF / OCW | client / none |
| sprk_event | wizard (also work-assignment follow-on) | C→BFF | client |
| sprk_event | `POST /api/v1/events` (`EventEndpoints.cs:695`) | BFF-own | none |
| sprk_event | decision add-event / external portal | OCW / BFF-own (external) | none |
| sprk_todo | wizard, SmartTodo, Daily Briefing inline, EventDetailSidePane, Analysis follow-on, LegalWorkspace lib | C→BFF | client (wizard only) |
| sprk_todo | `/api/office/todo` (`OfficeService.cs:2289`) | BFF-own | none |
| sprk_todo | `TodoGenerationService.cs:988`, `TaskActionCore.cs:315`, `InquiryReplyTodoCreator.cs:160`, decision add-todo | BFF-own (system) / OCW | none |
| sprk_invoice | wizard | C→BFF | client |
| sprk_invoice | Office quick-create (`OfficeService.cs:1966`), `InvoiceReviewService.cs:688-720` | BFF-own | none |
| sprk_reportcard | wizard | C→BFF | client |
| sprk_analysis | wizard widget (`:925`) | C→BFF | client |
| sprk_analysis | `POST /api/ai/analysis/create` (`AnalysisEndpoints.cs:166`), `AnalysisResultPersistence`, `AppOnlyAnalysisService` | BFF-own | none |
| sprk_communication | `CommunicationService` send/post; inbound `MessagingIngestor`, `IncomingCommunicationProcessor`, `EmailUploadCaptureService`; AI `email.draft` | BFF-own / OCW | none |
| sprk_communication (Analysis wizard) | `CreateAnalysisWizardWidget.tsx:965`: **not a create**; maps analysis→communication into an email DRAFT (subject/body), sent via `/api/communications/send` | n/a | client (overwrites user-typed subject/body) |
| sprk_document | Document Upload wizard, wizard file uploads | C→BFF | none |
| sprk_document | `POST /api/v1/documents` (Summarize Files), Office save (`OfficeDocumentPersistence.cs:283`, `UploadFinalizationWorker`), email attachments, communication archive | BFF-own | none |
| sprk_memo | Notepad, EventDetailSidePane (`useRelatedRecord.ts:147` → BFF) | C→BFF | none |
| sprk_memo | Event ribbon "Add Memo" (`sprk_event_ribbon_commands.js:552-566`) | **native quick-create form** | none |
| sprk_budget / kpiassessment / billingevent | secure-record ribbon (`sprk_secure_child_ribbon.js`) | C→BFF | none |
| sprk_kpiassessment | "Add KPI" (`sprk_kpi_ribbon_actions.js:106-122`) on non-secure host | **native quick-create form** | none |
| sprk_servicerequest | external intake, budget inquiry | BFF-own (external) / OCW | none |
| sprk_signal / sprk_decisionrecord | `SignalWriter`, `DecisionRecordWriter` | BFF-own | none |
| any | external portal (`ExternalDataService`) | BFF-own (external) | none, **out of scope** (external-access-r3) |

**Wizards** (10): Matter, Project, Event, To Do, Invoice, Work Assignment, Report Card, Analysis, Document Upload,
Summarize Files. Launch commands: `sprk_wizard_commands.js:196-289`; secure-child "New" overrides:
`sprk_secure_child_ribbon.js:63-83`.

**Native create surfaces**: the solution XML has `IsQuickCreateEnabled=1` for `sprk_Project`
(`SpaarkeMaster/.../sprk_Project/Entity.xml:3347`), `sprk_InvoiceLineItem`, and `sprk_Matter`
(`spaarke_insights`). `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:120` says quick create is off live. **Verify live.**

**`sprk_createdbyperson` is NOT an outside-the-product marker.** It is empty on wizard Matter/Project, inbound
communications, job-created rows, external-contact rows (they use `sprk_createdbycontact`) and service requests (the
column doesn't exist). Candidate marker for W5: platform `createdby` ≠ the BFF application user (every BFF create is
app-only). Verify that no product path creates with impersonation (`MSCRMCallerID` is used on the Communication
apply/undo routes).

**`bffDataServiceAdapter.ts`**: zero production importers of `createBffDataService`; its `/api/dataverse/{entity}`
write routes don't exist (`RecordEndpoints.cs` is gone).

## 6. Ownership and access control

- **Standard**: D-11 (`projects/unified-access-control-r2/notes/owner-decision-brief-2026-09-18.md:618`) and I-6
  (`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:136`): BU default Owner team (`isdefault=true AND
  teamtype=0`), record-first, else acting user's BU, else refuse; secure-if-any → named Secure Record Owners team.
  **No per-table "who owns" document exists**; `RecordOwnerAssignmentCensusTests` covers server child-table writers
  only.
- **Roles** (`src/dataverse/solutions/SpaarkeMaster/Roles/`):
  - **Spaarke Basic User**: Read + AppendTo **Deep** on matter, project, document, todo, event, invoice, memo,
    reportcard and workassignment (no Create); all privileges Deep on analysis; **User (Basic) depth** on
    communication.
  - **Spaarke Core User**: Create/Read/Write/Append/AppendTo/Assign/Delete **Deep** on the main tables (reportcard
    Read only).
  - **AI Analysis User**: User depth on the analysis tables.
  - `SecureBuRoleDepthAssertionTests` is the NFR-05 standing assertion.
- **Topology** (dev): root "Spaarke" holds the BFF app user and most users; child BUs "Spaarke Business Unit 1" and
  "Spaarke Demo"; a Secure Record BU with no users. App-owned root rows are unreachable from child BUs below Global
  (word task 080 §1).
- **Visibility of a user-owned row**: Dataverse places it in the owner's BU. Readers reach it per role depth: User =
  owner (+ shares), BU = that BU, Deep = that BU + ancestors, Org = all. So with Deep Read, a Matter assigned to user
  U is visible to everyone in U's BU and above, not only to U. The harm is the BU MOVE (siblings lose it, families
  split, secure isolation can be broken) and User-depth tables (owner-only).
- **Gaps**: wizard Matter/Project rows are user-owned, with no assessment and no reassign (the backfill script
  touches app-owned rows only). OOB **Assign** (Core User holds `prvAssign` Deep on matter) changes `ownerid` and
  moves the BU, bypassing the standard. UAC-r2 task 146 note: "widening user roles with prvAssign is forbidden".
- **Accepted exceptions** (user/creator-owned): `appnotification` (`NotificationActionCore.cs:192`,
  `NotificationService.cs:88`); `sprk_notificationoutbox` (`OutboxService.cs:135`, **no access assessment found**);
  Direct thread (`DirectThreadAccessService.cs:94`, census waiver); unfiled communications `KeepCreator` (owner round
  10 item 8, 2026-10-03); `DecisionRecordWriter.cs:1451` communication-owner case (**not traced**).
- **AppendTo does not imply Read**: they are separate `RetrievePrincipalAccess` flags. View-only POA shares grant
  Read without AppendTo by design.

## 7. `/push` use case

- **Purpose**: the "Update related" ribbon button on the Matter and Project forms (`sprk_fieldmapping_push.js`,
  wired in `SpaarkeMaster/.../sprk_Matter/RibbonDiff.xml` and `sprk_Project/RibbonDiff.xml`) re-copies parent values
  (today: the attorney matrix) onto existing children. The `UpdateRelatedButton` PCF has no form binding found;
  `sprk_updaterelated_commands.js` (Event ribbon) has a hard-coded dev URL and no deployment found.
- **Open issues**: #2098 (≈17 Dataverse calls/child; a 500-child push can exceed 230 s); #1437 (raw HttpClient text in
  errors, ADR-019); #2056 (no invoice-family guard on push); #1617 (a regarding-pair mapping skips the caller AppendTo
  check); #1614 (Office Project AppendTo for secure parents only).

## 8. Records created outside the product

| Invariant | Repaired today? |
|---|---|
| I-1 ancestor stamp | yes: `CoreAncestorStampReconciliationJob` (`IScheduledJob`, 5 min) |
| I-2 secure isolation | under secure parents only: `SecureChildReconciliationJob` |
| I-3 mapping, I-5 search index, I-6 owner (non-secure) | **no** |

- **No Dataverse change-signal consumer exists.** `RecordSyncJob` is a disabled timer `BackgroundService` (ratchet-listed
  under ADR-052).
- **Callers must keep the Create privilege**: the unified service checks the caller's Create privilege, so native
  creates cannot be blocked by role and a repair pass is required.
