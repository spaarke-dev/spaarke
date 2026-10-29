# Hard-deleting a core record: what would be deleted, and what could go wrong

> Read-only inventory, 2026-10-18. Source: `origin/master` at `5f1593aca`. Live data and metadata: spaarkedev1, read-only Web API GETs as Ralph Schroeder (System Administrator, so every count below is the full table, not a trimmed view). Nothing was written to Dataverse, git or Key Vault.
> The query scripts and their raw JSON output are in the session scratchpad: `C:/Users/RALPHS~1/AppData/Local/Temp/claude/c--code-files-spaarke-wt-unified-access-control-r2/993ea642-9455-4e0e-8f57-ed99084c5e37/scratchpad/` (`o2m_all.json`, `counts.json`, `multi.json`, `xmlrels.json`).

## 0. Executive summary

**The owner's proposed rule:** hard-deleting a core record (matter, project, work assignment, service request) deletes everything that belongs to it. It never deletes another core record or a reference record, it asks for confirmation first, and it is meant to be rare.

**What it would delete, on dev today.** All 62 matters, 23 projects, 22 work assignments and 3 service requests together own the following directly:

| Table | Rows |
|---|---|
| Documents | 157 |
| Communications | 130 |
| Communication threads | 116 |
| Signals | 133 |
| Events | 57 |
| To-dos | 41 |
| Memos | 21 |
| KPI assessments | 20 |
| Invoices | 8 |
| Decision Records | 6 |
| Budgets | 4 |
| Budget revisions | 3 |
| Report cards | 3 |
| Spend snapshots | 2 |
| External grants (`sprk_externalrecordaccess`) | 100 |
| Assigned-access ledger rows (`sprk_assignedaccess`) | 96 |

Grandchildren would go too: the analyses on each document (810 analyses are attached to documents), communication attachments and participants, email review logs, budget buckets, file versions and event logs.

**Today almost nothing cascades.** Every core→child relationship is RemoveLink, with two exceptions: `sprk_policy_matter` (Cascade, live only) and project→`sprk_assignedaccess` (Cascade, in the solution XML too). A core delete today therefore leaves its children in place with an empty lookup.

**Top risks**

1. **Dataverse cannot express the rule in metadata alone.** A table can be the child in only ONE relationship that cascades. Learn says: *"No new relationship can have any action set to Cascade All … if the related table in that relationship already exists as a related table in another relationship that has any action set to Cascade All … This prevents relationships that create a multi-parent relationship."* ([create-edit-entity-relationships](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-edit-entity-relationships)).
   - Most children have two to four core lookups (a document has matter, project, work assignment and service request), so Dataverse could cascade from at most ONE core type per child table.
   - `sprk_analysis` already has its one cascading parent (`sprk_document`), and `sprk_assignedaccess` already has the project. The code says so: `AssignedAccessReconciliationJob.cs:55` reads "a table may have only one cascade-delete parent, and the project holds it".
   - The rule needs a server-side delete (BFF) for everything except one core type.
2. **A cascade skips the child's own Delete check.** This was observed live earlier on 2026-10-18; Learn does not document it either way (§3e). Under Cascade, a Core User, who holds Delete on matters at Parent:Child BU depth, would delete Decision Records, external grants, ledger rows, Signals, report cards, spend data, budget revisions and email review logs. The Core User role has no Delete privilege on any of those tables.
3. **Nothing outside Dataverse reacts to a Dataverse delete.** There are no plugins, webhooks or flows. A cascaded `sprk_document` delete leaves its SharePoint Embedded file (153 core-filed documents on dev have an SPE item id) and its AI Search chunks behind. Only the BFF's own document-delete route removes the file. Containers are shared (55 matters use 2 containers), so a container must never be deleted with a record.
4. **Children with more than one parent.** A cascade from one core record would delete rows that still belong to another live core record:
   - Directly: 5 communications, 1 event, 1 Signal and 1 budget point at two different core records.
   - Through a parent: 3 communications and 2 documents are filed to a matter that differs from the matter of their thread, invoice or communication.
5. **Records with retention value would be destroyed.** These are the Decision Records (designed append-only), invoices, billing events, the eBilling submission/approval/correction tables, budget revisions, and the grant and ledger tables.
   - The ledger (`sprk_assignedaccess`) has auditing OFF, so a project delete already erases it with no audit trail today.
6. **No delete path shows what will go.** The model-driven app's standard Delete is not overridden on any core table. The AI tool `dataverse.delete_record` names `sprk_matter` and `sprk_project` in its description and deletes as the user. Neither counts related records.

**Decisions needed** (§5 has the detail):

- (D1) the mechanism: Dataverse cascade or a BFF delete operation (recommended: BFF);
- (D2) Decision Records;
- (D3) finance records;
- (D4) grants and ledger;
- (D5) the multi-parent policy;
- (D6) who may hard-delete;
- (D7) SPE files;
- (D8) communications and their attachment documents;
- (D9) the `sprk_policy_matter` drift.

---

## 1. Which tables are "core" and which are "reference"

### 1.1 Core records

There are four core tables. Code defines the set; the ontology catalog does not.

| Table | Evidence |
|---|---|
| `sprk_matter`, `sprk_project`, `sprk_workassignment`, `sprk_servicerequest` | `CoreAncestorResolver.CoreRecordEntities`, `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs:66-72`: "CORE record entities — direct grants required; these never inherit. Pinned literally by test, and MUST equal the TypeScript `CORE_RECORD_ENTITIES`." |
| The same set is used by Signals | `SignalCoreRecordAccess.cs` remarks, "Core set (K2)": a Signal's core record "must be one of `CoreAncestorResolver.CoreRecordEntities`". |
| Secure roots and external-grant roots: Project, Matter, WorkAssignment only (service request excluded) | `ExternalGrantRootType`, `Infrastructure/ExternalAccess/ExternalGrantRoot.cs:8-13`. `SecureChildLineage.Roots`, `Services/Access/SecureChildLineage.cs:45-46`, comes from that enum. `config/secure-record-owner-role.json` lists the same three with `kind: root`. `SecureChildLineage.cs:24-26`: "a service request is a root of its own that the ownership resolver does not look through". |
| Core never inherits from core | `CoreAncestorResolver.cs:31`: "Matter does NOT inherit from Project — both are CORE". Write-path invariant I-17 (`docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md:147`): a work assignment or project can be *filed* under a parent, but is still core. |

**The ontology catalog does not mark core.** No table named `sprk_corerecordtype` exists live; the request to `sprk_corerecordtypes` returned 404. `sprk_corerecordtype` is a lookup column on `sprk_signal` and `sprk_decisionrecord` to `sprk_recordtype_ref`. That table is a catalog of 16 record types: To Do, Invoice Line, Matter, Project, Analysis, Document, Organization, Person, Event, Invoice, Budget, Service Request, Account, Work Assignment and Report Card. Its custom columns are `sprk_recorddisplayname`, `sprk_recorddisplaynamefield`, `sprk_recordlogicalname`, `sprk_recordtypecode`, `sprk_recordtypename`, `sprk_regardingfield` and `sprk_regardingrecordnumberfield`, and none of them flags core. The four-table list in `CoreAncestorResolver` is therefore the only definition. `projects/spaarke-ontology-platform-r1/notes/ontology-component-model.md` §3 defines vocabulary (Signal, Work Item, Decision Record), not a core set. Its §3.1 names matter, work assignment and service request among the "subjects" a Signal points at.

### 1.2 Reference records

| Table(s) | Evidence |
|---|---|
| `contact`, `account`, `systemuser`, `team`, `businessunit` | `SecureChildLineage.cs:26-27`: "Lookups to identity tables (contact, account, systemuser) are relationships, not parents." |
| `sprk_organization`, `contact`, `account`, `sprk_budget`, `sprk_reportcard` are not core and not child | `CoreAncestorResolver.cs:78-82` calls them "intentionally unclassified". Budget and report card are nonetheless owned children for delete purposes: Secure Record Owner role `kind: child` and `SecureChildLineage.cs` entries. |
| Document links that are not filing (contact, organization, email, service request) | `SecureChildLineage.cs:52-55` `DocumentLinksThatAreNotFiling`. |
| Catalog, config and lookup tables (`*_ref`, `sprk_recordtype_ref`, `sprk_analysisaction`, `sprk_analysisplaybook`, `sprk_ruleset`, `sprk_billingcodeset`, `sprk_gridconfiguration`, `sprk_policyversion` and similar) | `sprk_corerecordtype` is "a catalog row" (`SecureChildLineage.cs:93`, `:144`). `sprk_policyversion` is "not filing" (`:94`). Several of these tables have **Restrict** relationships live (organization→ratecard, organization→timekeeper, billingcodeset→billingcode, aimodeldeployment/analysisaction→playbooknode). |
| `email` (system activity) | Its custom `sprk_matter` and `sprk_project` lookups are RemoveLink. Learn: *"A custom table can't be the primary table in a relationship with a related system table that cascades"*, so matter→email can never be Cascade. |

---

## 2. One-to-many relationships where a core table is the parent

**How to read these tables.** The relationships were read live from `EntityDefinitions(LogicalName=…)/OneToManyRelationships`. "XML" is `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/<file>:<line>` on master. The row count is the number of live child rows with that lookup set (`$apply=filter(_x_value ne null)/aggregate($count as n)`).

**Live-vs-XML drift.** I compared all 349 custom 1:N relationships under every `sprk_` parent. Exactly one differs: `sprk_policy_matter` is Delete=Cascade live and RemoveLink in XML (`sprk_Matter.xml:555`). A solution import from master would put it back to RemoveLink.

**Tables that exist only on dev.** Fifteen relationships exist live and are absent from master XML. They belong to tables from the unmerged ebilling-r1 project: `sprk_ratecard`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection`, `sprk_externalidentifier`, plus `sprk_billingevent.sprk_regardingworkassignment` and `sprk_invoice.sprk_regardingworkassignment`. `git grep` for these table names on master returns nothing; they appear only in ebilling-r1 commits (for example `36b36daf9c`).

**What the classes mean**

| Class | Meaning |
|---|---|
| OWNED CHILD | Delete under the rule. |
| OWNED – retention / grant decision | Belongs to the record, but has legal, financial or audit value; the owner decides. |
| CORE | Must never be deleted. |
| REFERENCE/OOB | Must never be deleted. |

### 2.1 sprk_matter: 62 live rows on dev

| Relationship | Child.lookup | Delete live | Delete XML (file:line) | Class | Rows set |
|---|---|---|---|---|---|
| `sprk_servicerequest_RegardingMatter_sprk_matter` | `sprk_servicerequest.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:647) | CORE | 1 |
| `sprk_workassignment_RegardingMatter_sprk_matter_n1` | `sprk_workassignment.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:950) | CORE | 4 |
| `sprk_matter_Email_1n` | `email.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:450) | REFERENCE/OOB | 1 |
| `sprk_policy_matter` | `sprk_policy.sprk_matter` | **Cascade (DRIFT)** | RemoveLink (sprk_Matter.xml:555) | OWNED (matter-scoped rule; owner kept Cascade 2026-10-18) | 0 |
| `sprk_communicationrule_Matter_sprk_matter` | `sprk_communicationrule.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:139) | OWNED (matter-scoped config) | 0 |
| `sprk_sprk_matter_sprk_assignedaccess_matter` | `sprk_assignedaccess.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:744) | OWNED – grant/ledger | 55 |
| `sprk_externalrecordaccess_Matter_n1` | `sprk_externalrecordaccess.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:310) | OWNED – grant/ledger | 56 |
| `sprk_decisionrecord_matter` | `sprk_decisionrecord.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:209) | OWNED – retention | 6 |
| `sprk_decisionrecord_regmatter` | `sprk_decisionrecord.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:240) | OWNED – retention | 6 |
| `sprk_Matter_Invoice_1n` | `sprk_invoice.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:485) | OWNED – retention | 8 |
| `sprk_sprk_matter_sprk_billingevent_sprk_matter` | `sprk_billingevent.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:775) | OWNED – retention | 0 |
| `sprk_budgetrevision_matter` | `sprk_budgetrevision.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:73) | OWNED – retention | 3 |
| `sprk_invoiceapproval_regmatter` | `sprk_invoiceapproval.sprk_regardingmatter` | RemoveLink | absent (live only, ebilling-r1) | OWNED – retention | 0 |
| `sprk_invoicecorrection_regmatter` | `sprk_invoicecorrection.sprk_regardingmatter` | RemoveLink | absent (live only, ebilling-r1) | OWNED – retention | 0 |
| `sprk_invoicesubmission_regmatter` | `sprk_invoicesubmission.sprk_regardingmatter` | RemoveLink | absent (live only, ebilling-r1) | OWNED – retention | 0 |
| `sprk_matter_ratecard_matter` | `sprk_ratecard.sprk_matter` | RemoveLink | absent (live only, ebilling-r1) | OWNED? – decision (rate agreement with a firm) | 0 |
| `sprk_matter_document` | `sprk_document.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:415) | OWNED CHILD | 137 |
| `sprk_sprk_matter_sprk_document_sprk_relatedmatter` | `sprk_document.sprk_relatedmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:810) | OWNED CHILD | 0 |
| `sprk_communication_RegardingMatter_n1` | `sprk_communication.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:104) | OWNED CHILD | 122 |
| `sprk_communicationthread_RegardingMatter_sprk_matter` | `sprk_communicationthread.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:174) | OWNED CHILD | 108 |
| `sprk_signal_matter` | `sprk_signal.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:682) | OWNED CHILD | 108 |
| `sprk_signal_regmatter` | `sprk_signal.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:713) | OWNED CHILD | 108 |
| `sprk_event_RegardingMatter_n1` | `sprk_event.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:275) | OWNED CHILD | 42 |
| `sprk_sprk_matter_sprk_todo_RegardingMatter` | `sprk_todo.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:915) | OWNED CHILD | 38 |
| `sprk_memo_RegardingMatter_n1` | `sprk_memo.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:520) | OWNED CHILD | 19 |
| `sprk_kpiassessment_Matter_1n` | `sprk_kpiassessment.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:345) | OWNED CHILD | 18 |
| `sprk_Matter_Budget_1n` | `sprk_budget.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:380) | OWNED CHILD | 4 |
| `sprk_reportcard_RegardingMatter_sprk_matter` | `sprk_reportcard.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:612) | OWNED CHILD | 3 |
| `sprk_sprk_matter_sprk_spendsnapshot_sprk_matter` | `sprk_spendsnapshot.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:880) | OWNED CHILD | 2 |
| `sprk_sprk_matter_sprk_spendsignal_sprk_matter` | `sprk_spendsignal.sprk_matter` | RemoveLink | RemoveLink (sprk_Matter.xml:845) | OWNED CHILD | 0 |
| `sprk_agreement_RegardingMatter_sprk_matter` | `sprk_agreement.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:3) | OWNED CHILD | 0 |
| `sprk_analysis_RegardingMatter_sprk_matter` | `sprk_analysis.sprk_regardingmatter` | RemoveLink | RemoveLink (sprk_Matter.xml:38) | OWNED CHILD | 0 |
| `sprk_externalidentifier_regmatter` | `sprk_externalidentifier.sprk_regardingmatter` | RemoveLink | absent (live only, ebilling-r1) | OWNED CHILD | 0 |

Note: the 108 Signals with `sprk_matter` set and the 108 with `sprk_regardingmatter` set are the same rows; on all but one of them both lookups name the same matter.

**System (OOB) 1:N on sprk_matter** (platform-managed; listed for completeness):

- Delete=Cascade: `bulkdeletefailure`, `duplicaterecord` (×2), `mailboxtrackingfolder`, `principalobjectattributeaccess`, `queueitem`, `sharepointdocument`, `sharepointdocumentlocation`, `syncerror`, `team` (`regardingobjectid`), `userentityinstancedata`.
- NoCascade: `asyncoperation`, `deleteditemreference`, `processsession`.
- Live rows pointing at core records: teams 0, SharePoint document locations 0, POA 0, sync errors 0, annotations 0, queue items 0.
- Matter, project, work assignment and service request have **no** activity (`activitypointer`), note (`annotation`) or connection relationships. They are not activity- or notes-enabled. So no OOB activity is deleted with them.

### 2.2 sprk_project: 23 live rows on dev

| Relationship | Child.lookup | Delete live | Delete XML | Class | Rows set |
|---|---|---|---|---|---|
| `sprk_servicerequest_RegardingProject_sprk_project` | `sprk_servicerequest.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:458) | CORE | 0 |
| `sprk_workassignment_RegardingProject_sprk_project_n1` | `sprk_workassignment.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:800) | CORE | 5 |
| `sprk_Project_Email_1n` | `email.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:388) | REFERENCE/OOB | 0 |
| `sprk_sprk_project_sprk_assignedaccess_project` | `sprk_assignedaccess.sprk_project` | **Cascade** | Cascade (sprk_Project.xml:524) | OWNED – grant/ledger | 17 |
| `sprk_externalrecordaccess_Project_sprk_project_n1` | `sprk_externalrecordaccess.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:248) | OWNED – grant/ledger | 20 |
| `sprk_decisionrecord_project_sprk_project` | `sprk_decisionrecord.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:143) | OWNED – retention | 0 |
| `sprk_decisionrecord_regproject` | `sprk_decisionrecord.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:178) | OWNED – retention | 0 |
| `sprk_sprk_project_sprk_invoice_sprk_project` | `sprk_invoice.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:660) | OWNED – retention | 0 |
| `sprk_sprk_project_sprk_billingevent_sprk_project` | `sprk_billingevent.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:555) | OWNED – retention | 0 |
| `sprk_invoiceapproval_regproject` / `sprk_invoicecorrection_regproject` / `sprk_invoicesubmission_regproject` | `…sprk_regardingproject` | RemoveLink | absent (ebilling-r1) | OWNED – retention | 0 / 0 / 0 |
| `sprk_project_ratecard_project` | `sprk_ratecard.sprk_project` | RemoveLink | absent (ebilling-r1) | OWNED? – decision | 0 |
| `sprk_Project_Document_1n` | `sprk_document.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:353) | OWNED CHILD | 16 |
| `sprk_sprk_project_sprk_document_sprk_relatedproject` | `sprk_document.sprk_relatedproject` | RemoveLink | RemoveLink (sprk_Project.xml:625) | OWNED CHILD | 0 |
| `sprk_event_RegardingProject_n1` | `sprk_event.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:213) | OWNED CHILD | 10 |
| `sprk_communication_RegardingProject_n1` | `sprk_communication.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:73) | OWNED CHILD | 8 |
| `sprk_signal_regproject` | `sprk_signal.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:493) | OWNED CHILD | 7 |
| `sprk_communicationthread_RegardingProject_sprk_project` | `sprk_communicationthread.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:108) | OWNED CHILD | 3 |
| `sprk_sprk_project_sprk_todo_RegardingProject` | `sprk_todo.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:765) | OWNED CHILD | 3 |
| `sprk_kpiassessment_Project_1n` | `sprk_kpiassessment.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:283) | OWNED CHILD | 2 |
| `sprk_memo_RegardingProject_n1` | `sprk_memo.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:318) | OWNED CHILD | 2 |
| `sprk_sprk_project_sprk_budget_sprk_project` | `sprk_budget.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:590) | OWNED CHILD | 1 |
| `sprk_reportcard_RegardingProject_sprk_project` | `sprk_reportcard.sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:423) | OWNED CHILD | 0 |
| `sprk_sprk_project_sprk_spendsnapshot_sprk_project` / `…spendsignal…` | `sprk_spendsnapshot.sprk_project` / `sprk_spendsignal.sprk_project` | RemoveLink | RemoveLink (sprk_Project.xml:730 / :695) | OWNED CHILD | 0 / 0 |
| `sprk_agreement_RegardingProject_sprk_project` / `sprk_analysis_RegardingProject_sprk_project` | `…sprk_regardingproject` | RemoveLink | RemoveLink (sprk_Project.xml:3 / :38) | OWNED CHILD | 0 / 0 |
| `sprk_externalidentifier_regproject` | `sprk_externalidentifier.sprk_regardingproject` | RemoveLink | absent (ebilling-r1) | OWNED CHILD | 0 |

The system (OOB) relationships are the same set as on the matter; no live rows.

### 2.3 sprk_workassignment: 22 live rows on dev

| Relationship | Child.lookup | Delete live | Delete XML | Class | Rows set |
|---|---|---|---|---|---|
| `sprk_servicerequest_RegardingWorkAssignment_sprk_workassignment` | `sprk_servicerequest.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:318) | CORE | 0 |
| `sprk_sprk_workassignment_sprk_assignedaccess_workassignment` | `sprk_assignedaccess.sprk_workassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:384) | OWNED – grant/ledger | 24 |
| `sprk_externalrecordaccess_WorkAssignment_sprk_workassignment_n1` | `sprk_externalrecordaccess.sprk_workassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:248) | OWNED – grant/ledger | 24 |
| `sprk_decisionrecord_workassignment_sprk_workassignment` / `sprk_decisionrecord_regworkasg` | `sprk_decisionrecord.sprk_workassignment` / `.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:143 / :108) | OWNED – retention | 0 / 0 |
| `…sprk_billingevent_sprk_regardingworkassignment`, `…sprk_invoice_sprk_regardingworkassignment`, `sprk_invoiceapproval/correction/submission_regworkassignment` | `…sprk_regardingworkassignment` | RemoveLink | absent (ebilling-r1) | OWNED – retention | all 0 |
| `sprk_workassignment_ratecard_workassignment` | `sprk_ratecard.sprk_workassignment` | RemoveLink | absent (ebilling-r1) | OWNED? – decision | 0 |
| `sprk_signal_regworkasg` | `sprk_signal.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:353) | OWNED CHILD | 19 |
| `sprk_event_RegardingWorkAssignment_n1` | `sprk_event.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:213) | OWNED CHILD | 6 |
| `sprk_communication_RegardingWorkAssignment_n1` | `sprk_communication.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:38) | OWNED CHILD | 1 |
| `sprk_communicationthread_RegardingWorkAssignment_sprk_workassignment` | `sprk_communicationthread.sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:73) | OWNED CHILD | 1 |
| `sprk_WorkAssignment_Document_1n` | `sprk_document.sprk_workassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:450) | OWNED CHILD | 1 |
| `sprk_document_RelatedWorkAssignment_sprk_workassignment` | `sprk_document.sprk_relatedworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:178) | OWNED CHILD | 0 |
| `sprk_memo_RegardingWorkAssignment_n1` / `sprk_sprk_workassignment_sprk_todo_RegardingWorkAssignment` / `sprk_analysis_RegardingWorkAssignment_sprk_workassignment` | `…sprk_regardingworkassignment` | RemoveLink | RemoveLink (sprk_WorkAssignment.xml:283 / :415 / :3) | OWNED CHILD | 0 / 0 / 0 |
| `sprk_externalidentifier_regworkassignment` | `sprk_externalidentifier.sprk_regardingworkassignment` | RemoveLink | absent (ebilling-r1) | OWNED CHILD | 0 |

System (OOB): the same set minus `team`, `queueitem` and the two SharePoint relationships; no live rows.

### 2.4 sprk_servicerequest: 3 live rows on dev

| Relationship | Child.lookup | Delete live | Delete XML | Class | Rows set |
|---|---|---|---|---|---|
| `sprk_decisionrecord_regsvcreq` | `sprk_decisionrecord.sprk_regardingservicerequest` | RemoveLink | RemoveLink (sprk_ServiceRequest.xml:108) | OWNED – retention | 0 |
| `sprk_communication_RegardingServiceRequest_sprk_servicerequest` | `sprk_communication.sprk_regardingservicerequest` | RemoveLink | RemoveLink (sprk_ServiceRequest.xml:38) | OWNED CHILD | 4 |
| `sprk_communicationthread_RegardingServiceRequest_sprk_servicerequest` | `sprk_communicationthread.sprk_regardingservicerequest` | RemoveLink | RemoveLink (sprk_ServiceRequest.xml:73) | OWNED CHILD | 4 |
| `sprk_document_RelatedServiceRequest_sprk_servicerequest` | `sprk_document.sprk_relatedservicerequest` | RemoveLink | RemoveLink (sprk_ServiceRequest.xml:143) | OWNED CHILD | 3 |
| `sprk_analysis_…` / `sprk_event_…` / `sprk_memo_…` / `sprk_signal_regsvcreq` / `sprk_todo_…` (`…sprk_regardingservicerequest`) | | RemoveLink | RemoveLink (sprk_ServiceRequest.xml:3 / :178 / :213 / :248 / :279) | OWNED CHILD | all 0 |

A service request has no lookups to the grant, ledger or finance tables.

**An existing precedent:** the external delete-own route for a service request refuses with 409 `has_related_records` when any related row exists. It does this because all its relationships are RemoveLink and a delete would orphan staff records (`Api/ExternalAccess/ExternalProjectDataEndpoints.cs:494`; the gate is documented at `Infrastructure/ExternalAccess/ExternalDataService.cs` ≈`:2165-2185`).

### 2.5 N:N relationships (a delete only disassociates them)

| Relationship | Intersect table | Live links | XML |
|---|---|---|---|
| `sprk_Project_Matter_nn` (matter ↔ project, CORE↔CORE) | `sprk_project_sprk_matter` | 3 | sprk_Matter.xml:586 |
| `sprk_Matter_Contact_Contact_nn` | `sprk_matter_contact` | 0 | Contact.xml:2335 |
| `sprk_Matter_sprk_Organization_Organization_nn` | `sprk_matter_organization` | 1 | sprk_Organization.xml:423 |
| `sprk_precedent_matter` | `sprk_precedent_matter` | 0 | sprk_Precedent.xml:3 |
| `sprk_Project_Contact_Contact_nn` | `sprk_project_contact` | 4 | Contact.xml:2676 |
| `sprk_Project_sprk_Organization_Organization_nn` | `sprk_project_organization` | 0 | sprk_Organization.xml:589 |

Work assignment and service request have no N:N relationships. Deleting a core record removes its intersect rows; the contact, organization or other core record on the far side survives.

### 2.6 Rows that refer to a core record by text id, not a lookup (no cascade can reach them)

| Table | What it holds on dev |
|---|---|
| `sprk_navitem.sprk_targetid` | 42 matter, 9 project, 6 work assignment and 2 service request nav items |
| `sprk_notificationoutbox.sprk_regardingrecordid` | 229 rows with type `sprk_matter`, plus 2 with type "Matter" |
| `sprk_uploadcontext.sprk_parentrecordid` | 1 matter |
| `sprk_userentityassociation` | Membership junction, 0 rows. `MembershipReconciliationJob.cs:38-42` scans it nightly for orphans. |
| Others | `sprk_affinity.sprk_targetid` (15 rows, type not checked) and `sprk_noaccessentry` (1 row) |
| On the children themselves | `sprk_signal.sprk_corerecordid` and `sprk_decisionrecord.sprk_corerecordid` keep the core id as text. A Decision Record therefore still names its matter after a RemoveLink. |

---

## 3. Data-loss hazards

### (a) Children with more than one parent

**Method.** For every child table, I read every row's core lookups and counted the rows whose set lookups name two or more DIFFERENT core records (`multi.py`).

| Child table | Core lookups on the table | Rows with any set | ≥2 lookups set | ≥2 **different** core records |
|---|---|---|---|---|
| `sprk_communication` | regarding matter / project / work assignment / service request | 130 | 5 | **5** (matter + project) |
| `sprk_event` | regarding ×4 | 57 | 1 | **1** (matter + project) |
| `sprk_signal` | `sprk_matter` + regarding ×4 | 133 | 108 | **1** (matter + project). The other 107 repeat the same matter. |
| `sprk_budget` | matter, project | 4 | 1 | **1** (matter + project) |
| `sprk_decisionrecord` | matter / project / work assignment (typed) + regarding ×4 | 6 | 6 | 0 (typed and regarding agree) |
| `sprk_document` | matter / related matter / project / related project / work assignment / related work assignment / related service request | 157 | 0 | 0 |
| `sprk_communicationthread`, `sprk_todo`, `sprk_memo`, `sprk_invoice`, `sprk_kpiassessment`, `sprk_spendsnapshot`, `sprk_reportcard`, `sprk_externalrecordaccess` (100), `sprk_assignedaccess` (96), `sprk_workassignment`, `sprk_servicerequest`, `email` | 2–4 each | various | 0 | 0 |
| ebilling-r1 tables, `sprk_agreement`, `sprk_analysis`, `sprk_billingevent`, `sprk_spendsignal`, `sprk_ratecard` | 2–3 each | 0 | – | – |

**Through a parent** (`multi2.py`). I compared each row's own core lookups with the core lookups of its non-core parents, one hop up:

| Rows | What they are |
|---|---|
| `sprk_communication` ×2 | Own matter b68299c6, but their thread is filed to matter 1e992cc9 |
| `sprk_communication` ×1 | Own matter ec408889, but `regardinginvoice` is an invoice on matter 2444af6d |
| `sprk_document` ×2 | Own matter (86335ce3 / 39cde3e3), but `sprk_relatedcommunication` is a communication on another matter (e9638630 / ec408889) |
| `sprk_document` ×5 | No core of their own; their related communication is filed to matter + project |
| `sprk_communicationattachment` ×8, `sprk_communicationparticipant` ×9, `sprk_emailreviewlog` ×3 | Inherit a two-core communication |
| `sprk_budgetrevision` ×1, `sprk_budgetbucket` ×1 | Inherit the two-core budget |

**Why it matters**

- Deleting matter A by cascade deletes a communication that is also filed to project B. Project B silently loses it.
- If a thread cascades to its communications, deleting matter 1e992cc9's thread deletes two communications filed to matter b68299c6.
- The platform rule in §3f makes the Dataverse-cascade version of this worse. Only one core type per child could cascade, so a row filed to (matter, project) would be deleted by a matter delete but only unlinked by a project delete.

### (b) The full delete tree

```
CORE (matter | project | work assignment | service request)
├─ sprk_document (sprk_matter / sprk_project / sprk_workassignment / sprk_related*)
│   ├─ sprk_analysis.sprk_documentid ............ ALREADY Cascade live (sprk_Document.xml:178); 810 rows on dev
│   │   ├─ sprk_analysisoutput / analysischatmessage / analysisemailmetadata / analysisworkingversion ... Cascade
│   │   ├─ annotation, appointment, email, task, letter, fax, phonecall, chat, socialactivity,
│   │   │  recurringappointmentmaster, adx_portalcomment, adx_inviteredemption (regardingobjectid) ... Cascade
│   │   │  (sprk_analysis is activity-enabled: deleting an analysis deletes activities regarding it; 0 on dev)
│   │   └─ sprk_analysisaction / analysisknowledge / analysisskill / analysistool .sprk_analysisid ... RemoveLink  ← CATALOG, never cascade
│   ├─ sprk_fileversion.sprk_document ............ RemoveLink today (7 rows). Owned. The comment
│   │   "cascade should handle FileVersions" at DocumentCheckoutService.cs:862 is FALSE live.
│   ├─ sprk_emailartifact / sprk_attachmentartifact .sprk_document ... RemoveLink (0 rows)
│   ├─ sprk_document.sprk_parentdocument (77 rows) / sprk_canonicaldocument ... RemoveLink: SELF-REFERENCE; never cascade (a child
│   │   document may be filed elsewhere)
│   ├─ sprk_communicationattachment.sprk_document ... RemoveLink (attachment belongs to the communication)
│   ├─ sprk_ratecard.sourcedocument, sprk_ruleset.sourcedocument, sprk_billingexport.csv/jsondocument,
│   │   sprk_processingjob.sprk_document, sprk_invoicesubmission.document/reportdocument ... RemoveLink ← CONFIG/FINANCE, never cascade
│   └─ team.regardingobjectid ... Cascade (OOB, 0 rows)
├─ sprk_communication (regarding*)
│   ├─ sprk_communicationattachment (104), sprk_communicationparticipant (254), sprk_emailreviewlog (18) ... RemoveLink; owned
│   ├─ sprk_document.sprk_relatedcommunication (76; 57 with NO core of their own) ... RemoveLink; would orphan with their SPE files
│   ├─ sprk_event / sprk_todo / sprk_memo / sprk_signal / sprk_decisionrecord / sprk_analysis .sprk_regardingcommunication ... RemoveLink
│   └─ sprk_workassignment.regardingcommunication, sprk_servicerequest.regardingcommunication ... RemoveLink ← CORE, never cascade
├─ sprk_communicationthread (regarding*)
│   └─ sprk_communication.sprk_communicationthread (257) ... RemoveLink. Cascading would hit 2 communications filed to another matter.
├─ sprk_event (regarding*)
│   ├─ sprk_eventlog.sprk_event (1) ... RemoveLink; owned
│   ├─ annotation / sharepointdocument(location) / team ... Cascade (OOB)
│   ├─ sprk_event.sprk_regardingevent ... SELF-REFERENCE (0 rows)
│   └─ sprk_workassignment.sprk_regardingevent ... RemoveLink ← CORE
├─ sprk_todo, sprk_memo (regarding*) — leaves, except sprk_servicerequest.sprk_regardingtodo ← CORE
├─ sprk_invoice (sprk_matter / sprk_project / regardingworkassignment)
│   ├─ sprk_billingevent.sprk_invoice (1), sprk_invoicesubmission / sprk_invoiceapproval .sprk_invoice ... RemoveLink
│   ├─ sprk_invoicecorrection.sprk_invoice ... ALREADY Cascade live (ebilling-r1)
│   ├─ sprk_invoice.sprk_supersedesinvoice ... SELF-REFERENCE
│   ├─ contact.sprk_invoice, sprk_organization.sprk_invoice ... RemoveLink ← REFERENCE, never cascade (0 rows)
│   └─ sprk_workassignment.regardinginvoice (1 row!), sprk_servicerequest.regardinginvoice ... RemoveLink ← CORE
├─ sprk_budget ── sprk_budgetbucket (ALREADY Cascade), sprk_budgetrevision.sprk_budget (RemoveLink)
├─ sprk_reportcard ── sprk_kpiassessment.sprk_reportcard (1), sprk_memo.sprk_reportcard
├─ sprk_spendsnapshot ── sprk_spendsignal.sprk_snapshot (0)
├─ sprk_signal — leaf (sprk_decisionrecord is its parent, not its child)
├─ sprk_decisionrecord ── sprk_signal.sprk_decisionrecord (4), sprk_invoiceapproval.sprk_decisionrecord
├─ sprk_policy (matter only; Cascade live) ── sprk_policyversion, sprk_signal.sprk_policy ... RemoveLink. The owner's
│   2026-10-18 live check B: an admin delete removed only the rule; versions and Signals survived with nulled lookups.
├─ sprk_ratecard ── sprk_ratecardrate (ALREADY Cascade)
├─ sprk_externalrecordaccess ── sprk_assignedaccess.sprk_externalrecordaccess (94 rows) ... RemoveLink
├─ sprk_assignedaccess, sprk_kpiassessment, sprk_spendsignal, sprk_agreement, sprk_externalidentifier,
│   sprk_communicationrule, sprk_budgetrevision — leaves
└─ CORE / REFERENCE reached directly: sprk_workassignment, sprk_servicerequest, email ... must stay RemoveLink
```

**Paths that reach a core or reference table**

| Kind | Paths |
|---|---|
| Core | `sprk_event`→`sprk_workassignment`; `sprk_communication`→`sprk_workassignment` / `sprk_servicerequest`; `sprk_todo`→`sprk_servicerequest`; `sprk_invoice`→`sprk_workassignment` (1 live row) / `sprk_servicerequest` |
| Reference | `sprk_invoice`→`contact`, `sprk_invoice`→`sprk_organization`, `sprk_analysis`→`sprk_analysisaction` (catalog), `sprk_document`→`sprk_ruleset` / `sprk_ratecard` |

**Self-references (loops)**

- `sprk_document.sprk_parentdocument` (77 rows) and `.sprk_canonicaldocument`
- `sprk_event.sprk_regardingevent`
- `sprk_invoice.sprk_supersedesinvoice`
- `sprk_invoicesubmission.sprk_resubmissionof`

**None of these may ever cascade.** All of them are RemoveLink today.

### (c) Data outside Dataverse

**Dataverse-side reaction to a delete: none.** There is no plugin, step, webhook, service endpoint or flow in `src/dataverse` or `scripts`. The only hits are the audit-listing script `scripts/Audit-DataverseComponents.ps1:115-117`, and `src/dataverse/solutions/spaarke_core/Workflows/.gitkeep` is empty. This matches ADR-002. **A cascade-deleted row therefore gets no clean-up of any kind.**

| Store | What happens on a cascade delete today | Evidence |
|---|---|---|
| **SPE files** | **Left behind.** Only the BFF route `DELETE /api/documents/{id}` deletes the file (`DocumentCheckoutService.DeleteAsync`, file delete `:838` or verified-first `:929`, then the row `:862`). The second route, `DELETE /api/v1/documents/{id}`, deletes the row only (`DataverseDocumentsEndpoints.cs:306`, `:337`). A Dataverse cascade calls neither. | 573 of 588 documents have `sprk_graphitemid`; 153 of them are filed to a core record. 57 more are tied only to a communication. |
| **SPE containers** | **Must never be deleted with a record.** Containers are shared. | 55 matters with `sprk_containerid` use **2** distinct containers; documents use 2. Container deletes exist only in SPE-admin and provisioning code (`Api/SpeAdmin/RecycleBinEndpoints.cs:76`, `ContainerOperations.cs:224`, `GraphContainerProvisioner.cs:355`). |
| **SPE just-in-time writer grants** on a secure record's own container | **Likely orphaned (unverified live).** The removal pass lists only *live* secure records (`SpeContainerMembershipSync.cs:301`, `sprk_issecure eq true`), so a deleted secure record's container is no longer visited. | `SpeContainerMembershipSync.cs:26-31` |
| **Azure AI Search chunks** | **Left behind.** No reconciler compares the index with `sprk_document`. The only deletes are an explicit admin route (`RagService.cs:724`, `DELETE /api/ai/rag/{documentId}` at `RagEndpoints.cs:87`) and the re-index tail trim (`FileIndexingService.cs:402`). The read paths drop a result whose document the caller cannot read (`RagEndpoints.cs:940-954`, `VisualizationEndpoints.cs:426-433`, fail closed), so a deleted document's chunks should not be served. That is unverified for every consumer; the chunks still cost storage. | |
| **Membership junction** (`sprk_userentityassociation`) | Cleaned within 24 h by the nightly orphan scan. | `MembershipReconciliationJob.cs:38-42`; the document route says so at `DataverseDocumentsEndpoints.cs:345-352` |
| **External grants** | With RemoveLink, a grant whose record is gone stays ACTIVE with empty lookups. The scheduled rule R4 deactivates it. With Cascade, the row would be deleted instead and R4 would never see it. | `ExternalAccessReconciliationJob.cs:48-56`. Rootless ledger rows are marked Revoked/RootDeleted by `AssignedAccessReconciliationJob.cs:54-66`. |
| **Redis / caches** | Access caches are evicted on share writes and membership events, not on a Dataverse delete. A deleted record's cached rights simply stop mattering (no record). | `MembershipCacheInvalidator.cs:430` |
| **Service Bus jobs** | Jobs queued for a since-deleted record skip it (for example `SpendSnapshotGenerationJobHandler.cs:96-111`, `SecureChildReconciler.cs:534`, `:612`). | Not every handler was read. |
| **Graph / Entra groups** | No code found that creates Entra groups or Teams per core record. On dev, `team.regardingobjectid` has 0 rows pointing at a core record. | |

### (d) Records with legal, financial or audit retention value (owner must decide each)

| Table | Why it matters | Live today | Who can delete it directly |
|---|---|---|---|
| `sprk_decisionrecord` | "An append-only business record of decisions made"; it becomes the Matter Report Card (`ontology-component-model.md` §4.13). The writer is append-only except the live stamp columns (`DecisionRecordWriter.cs:388-396`). It keeps `sprk_corerecordid` as text, so RemoveLink preserves the link. | RemoveLink; 6 rows; audit ON | Admin and Service Deleter only (no Spaarke role) |
| `sprk_invoice`, `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection` | Financial records. The last three come from ebilling-r1 (unmerged). | RemoveLink (correction cascades from invoice) | Invoice: Core User P:C. Billing event: admin only. Submission/approval/correction: Core User P:C. |
| `sprk_budgetrevision` | "The revision a decision records" (`SecureChildLineage.cs:78-79`, D-33/D-38) | RemoveLink; 3 rows | Core User: none |
| `sprk_externalrecordaccess` | Who outside the firm had access to what | RemoveLink + R4; 100 rows; audit ON | Admin only |
| `sprk_assignedaccess` | The assigned-access ledger ("rows are never deleted or deactivated by the BFF", `AssignedAccessReconciliationJob.cs:61`) | **Project→Cascade already; matter and work assignment RemoveLink. Audit OFF.** 96 rows | Admin only |
| `sprk_signal` | Evidence behind decisions (subject, rule, version) | RemoveLink | Ontology Administrator and admin |
| `email` (system) | One email may be filed to several records | RemoveLink; cannot cascade (custom→system) | n/a |
| Dataverse audit log | Org auditing ON; `auditretentionperiodv2` is **null** (not set; #2027 is open). Core tables audit ON; `sprk_assignedaccess` OFF. Learn: audit rows survive the record delete and are removed only by retention or an explicit `DeleteRecordChangeHistory` ([retrieve-audit-data](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/auditing/retrieve-audit-data), [delete-audit-data](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/auditing/delete-audit-data)). Whether each cascade-deleted child gets its own audit row is undocumented. | | |

**Restore.** Live `recyclebinconfig` rows exist for `sprk_matter`, `sprk_project` and `sprk_document` (`cleanupintervalindays` 30) and for `sprk_workassignment`, `sprk_decisionrecord` and `organization` (-1), all `isreadyforrecyclebin = true`. **What these values mean was not verified.** Learn: cascade-deleted rows can be restored only after their parent is restored, and logic you run yourself is not undone ([restore-deleted-table-records](https://learn.microsoft.com/en-us/power-platform/admin/restore-deleted-table-records)). A restore would not bring back an SPE file or an index chunk.

### (e) Secure records: cascade skips the child's own Delete check

**Observed live, 2026-10-18** (`current-task.md`, "Ontology PR #2075"): "a Core User's matter delete is NOT refused — Dataverse cascaded without checking prvDeletesprk_Policy (audited under the user)".

Learn neither confirms nor denies this for synchronous cascades. The only text found is for async cascades: *"Security exceptions occur when the user who executed the cascading operation has insufficient privileges to make a change to one or more records"* ([async-cascading](https://learn.microsoft.com/en-us/power-platform/admin/async-cascading)). That suggests large (async, ≥5,000-row) cascades may behave differently: they could fail part-way instead. This is unverified.

**Who can delete a core record today** (root-BU roles, live):

| Table | Roles with Delete |
|---|---|
| `prvDeletesprk_Matter`, `prvDeletesprk_Project`, `prvDeletesprk_WorkAssignment`, `prvDeletesprk_ServiceRequest` | Spaarke Core User (Parent:Child BU), System Administrator, System Customizer, Service Deleter |

**Full Access sharees.** UAC's Full Access share level includes `DeleteAccess` (`Services/Access/RecordShareLevels.cs:86`), so a Full Access sharee of a secure matter or project can delete it.

**Children a Core User cannot delete directly but would delete through a cascade:**

| Core User Delete privilege on the child | Tables |
|---|---|
| **None** | `sprk_decisionrecord`, `sprk_signal` (Ontology Admin only), `sprk_externalrecordaccess`, `sprk_assignedaccess`, `sprk_billingevent`, `sprk_policy`, `sprk_budgetrevision`, `sprk_budgetbucket`, `sprk_reportcard`, `sprk_spendsnapshot`, `sprk_spendsignal`, `sprk_emailreviewlog`, `sprk_fileversion`, `sprk_eventlog`, `sprk_analysisoutput`, `sprk_ratecard`, `sprk_communicationrule` |
| **User depth only** (their own rows) | `sprk_communicationthread`, `sprk_communicationattachment`, `sprk_communicationparticipant` |

**Secure-team-owned children.** These are owned by "Secure Record Owners" in BU "Secure Record" (D-11). The secure-child share synchronizer gives a sharee the INTERSECTION of the share sets of all the row's secure roots (`SecureChildLineage.cs:19-23`), which may be narrower than the sharee's right on the root. Under Cascade, a Full Access sharee of the root deletes every such child anyway.

On dev today: 3 secure projects (owned by the team), 0 secure matters or work assignments, and 2 team-owned documents. All other child tables have 0 team-owned rows.

**This contradicts "security fails closed"** in the write-path rules (CLAUDE.md §17, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md`): what the caller may delete would be decided by the parent alone.

### (f) Dataverse platform limits (cited)

1. **One cascading parent per child table.** *"No new relationship can have any action set to **Cascade All**, **Cascade Active**, or **Cascade User-Owned** if the related table in that relationship already exists as a related table in another relationship that has any action set to [those]. This prevents relationships that create a multi-parent relationship."* — [create-edit-entity-relationships](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/create-edit-entity-relationships), "Limitations on behaviors you can set".
   - Live, every child table has at most one Delete=Cascade parent today:
     - `sprk_analysis` ← `sprk_document`
     - `sprk_policy` ← `sprk_matter`
     - `sprk_assignedaccess` ← `sprk_project`
     - `sprk_invoicecorrection` ← `sprk_invoice`
     - `sprk_budgetbucket` ← `sprk_budget`
     - `sprk_analysisoutput` ← `sprk_analysis`
   - Merge=Cascade on contact/account lookups coexists with these. For example, `sprk_analysis` has both `contact` Merge=Cascade and `sprk_document` Delete=Cascade. So Merge evidently does not count; this is inferred from live metadata, not stated by Learn.
   - The team already hit this limit (`AssignedAccessReconciliationJob.cs:55`, `ExternalAccessReconciliationJob.cs:52`).
   - **Consequence:** for each child table, at most ONE of matter / project / work assignment / service request can be the cascading parent. `sprk_analysis` can have none, because the document holds its one slot.
2. **No cascade from a custom table to a system table** (same page): matter→`email` cannot cascade.
3. **Delete options** are only Cascade All, Remove Link and Restrict (same page). Restrict *"Prevent[s] the primary table row from being deleted when related table rows exist."*
4. **Large cascades** ([async-cascading](https://learn.microsoft.com/en-us/power-platform/admin/async-cascading)):
   - *"By default, cascading operations are performed as a synchronous transaction."*
   - Large synchronous ones *"can cause performance issues … when long-running transactions fail due to server timeouts"*, and they lock every record involved.
   - Async threshold for Delete: **5,000 records**. Async mode gives *"eventual consistency"*, retries, then stops as Failed in System Jobs, and *"It isn't possible to cancel an asynchronous cascading job."*
   - Whether async mode is on for spaarkedev1 was not checked.
   - Dev volumes are far below 5,000; production matters with years of email could approach it.
5. **No documented maximum depth or loop rule.** The one guard: actions *"don't execute"* when *"attempting to perform an action on a parent row that is already running a cascading action"* (create-edit-entity-relationships).
6. **No plugins** (ADR-002): there is no server hook on Delete, so all clean-up must be done before or instead of the platform delete.

### (g) Where hard deletes start today, and where a confirmation could go

| Entry point | What it deletes | Confirmation today |
|---|---|---|
| **MDA standard Delete** (form and grid) on matter, project, work assignment, service request | The row, plus whatever Dataverse cascades | The platform's generic dialog. No `RibbonDiff.xml` hides or overrides `Mscrm.DeletePrimaryRecord` or the grid/subgrid Delete for any core table (searched every `RibbonDiff.xml`). The Core User role has Delete at P:C depth. |
| **AI tool `dataverse.delete_record`** (`Services/Ai/Handlers/DataverseDeleteRecordHandler.cs:65`, delete at `:162`) | Any table, as the user (OBO). The description names `sprk_matter`, `sprk_project` and `sprk_document`. | `hasUserApproved` flag only. No related-record count, no SPE or index clean-up. |
| BFF `DELETE /api/documents/{id}` (`DocumentOperationsEndpoints.cs:80`) | Document + SPE file (best effort) | Called from the document form button `sprk_DocumentOperations.js:1080` (confirm at `:1091`) |
| BFF `DELETE /api/v1/documents/{id}` (`DataverseDocumentsEndpoints.cs:306`) | Document row only | – |
| External SPA delete-own (`ExternalProjectDataEndpoints.cs:464`, `:474`, `:482`, `:494`) | Own document (file first), to-do, event, service request | Service request: 409 if anything is related |
| `SecureRootInheritance.cs:1200` | A just-created root whose isolation set-up failed (create rollback) | n/a (server) |
| Create-wizard rollback (`EntityCreationService.ts:838`, `DocumentRecordService.ts:103`) | `sprk_document` only. No core-record rollback found. | n/a |
| Grid `delete-selected` (`DataGrid/commandBar/defaults.ts:176-186`) | Any entity configured with it. Live: only the two `sprk_event` grid configurations. | Fluent dialog in `CommandBar` |
| `InvoiceReviewService.cs:1127`, `AnalysisEndpoints.cs:1264` | Undo of a failed create (invoice, analysis) | n/a |
| Scripts | No script deletes core records. `scripts/Repair-ComposeIdentityKey.ps1:213` deletes duplicate `sprk_document` rows. | – |

**Where a "this will also delete N related records" confirmation could live**

- **MDA command bar.** A RibbonDiff `CustomAction` replacing Delete on the four core forms and grids, calling a JS web resource. The existing patterns to copy:
  - `sprk_DocumentOperations.js:1080-1112`: confirm, then call a BFF delete;
  - `sprk_access_ribbon.js:434-470`: `confirmationFor`, `confirmFirst` around `Xrm.Navigation.openConfirmDialog`.
  - No Power Fx command components exist in the solution today.
- **A BFF preview route** returning the counts and the multi-parent rows, and a delete route that does the work. This mirrors the service-request 409 `has_related_records` precedent. The same route would serve the MDA button, the Console and the AI tool.
- **The AI tool** should be pointed at that route for core tables, or refuse them.

---

## 4. Proposed change table

### 4.0 The mechanism comes first (owner decision D1)

| | Option A: Dataverse Delete=Cascade | Option B: a BFF "delete core record" operation (recommended) |
|---|---|---|
| Reaches all four core types | **No.** One cascading parent per child (§3f.1); `sprk_analysis` can have none. | Yes |
| Multi-parent rows | Deleted silently | Can skip, re-home or refuse, per the D5 rule |
| Child privileges | Bypassed (§3e) | Each child delete runs as the caller, or the operation refuses |
| SPE files / index chunks | Left behind | Deleted through `DocumentCheckoutService.DeleteAsync` (verified-file-first policy) and `IRagService` |
| Confirmation with counts | Needs a separate count query that can drift from what Dataverse deletes | The same code produces the preview and the delete |
| Large volumes | Synchronous up to 5,000 rows, then async that cannot be cancelled | Batched, resumable (ADR-004 job) |
| Fits ADR-002 / write-path ("one server-side owner per invariant") | Partly | Yes |

Under Option B, the relationship metadata should stay **RemoveLink**, so a delete that bypasses the BFF (MDA, Web API) orphans rather than destroys. The core-record Delete privilege would then be narrowed to the BFF's path (decision D6). Every table below is written for Option B. The "If Option A" column says what Dataverse could do.

### 4.1 Straightforward: owned, delete under the rule

| Child table (relationships from §2) | Today | Proposed | If Option A | Extra work |
|---|---|---|---|---|
| `sprk_todo` (regarding ×4) | RemoveLink | Delete | Cascade from ONE core type only | Multi-parent check |
| `sprk_memo` (regarding ×4) | RemoveLink | Delete | Same | Multi-parent check |
| `sprk_event` (regarding ×4) + `sprk_eventlog` | RemoveLink | Delete event and its event logs | Same; `eventlog` would need its own cascade from event | 1 multi-parent row on dev |
| `sprk_kpiassessment` (matter, project) | RemoveLink | Delete | One core type | – |
| `sprk_reportcard` (regarding matter, project) | RemoveLink | Delete. Flag: §4.13 calls the report card "the asset". Confirm with the owner if in doubt. | One core type | – |
| `sprk_spendsnapshot`, `sprk_spendsignal` (matter, project) | RemoveLink | Delete (derived data) | One core type each | – |
| `sprk_budget` + `sprk_budgetbucket` | RemoveLink (bucket Cascade from budget) | Delete | One core type | 1 two-core budget on dev |
| `sprk_agreement`, `sprk_analysis` (regarding), `sprk_externalidentifier` | RemoveLink | Delete | Agreement and external identifier: one core type each. **Analysis: impossible** (document holds the slot). | Analysis has activity cascades of its own |
| `sprk_communicationrule` (matter) | RemoveLink | Delete (matter-scoped config, the same reasoning as policy) | Possible (one parent) | – |
| `sprk_policy` (matter) | **Cascade live / RemoveLink XML** | Keep Cascade (owner decision 2026-10-18) | Already | **Fix the XML** at `sprk_Matter.xml:555`, or an import reverts it (D9) |
| `sprk_signal` (matter + regarding ×4) | RemoveLink | Delete | One core type | 1 two-core Signal; Signals that a Decision Record points at → see D2 |
| `sprk_document` (7 core lookups) + `sprk_fileversion`, `sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_analysis` (already Cascade) | RemoveLink | Delete the document **through `DocumentCheckoutService.DeleteAsync`** (file first), and its file versions | One core type; the SPE file is left behind | **SPE file delete; search-index chunk delete; never delete the container**; the `parentdocument` self-reference stays RemoveLink |

### 4.2 Needs an owner decision

| Child table | Today | Options | Recommendation | Extra work |
|---|---|---|---|---|
| `sprk_decisionrecord` (typed ×3 + regarding ×4) | RemoveLink | Keep / Delete / Restrict (block the core delete while one exists) | **Keep (RemoveLink).** It is the append-only record; `sprk_corerecordid` keeps the core id as text. | None |
| `sprk_invoice` + `sprk_billingevent`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_invoicecorrection` | RemoveLink (correction Cascade from invoice) | Keep / Delete / Restrict | Keep or Restrict. Financial retention; ebilling-r1 owns these tables, so its owner must agree. | The ebilling tables are not on master yet |
| `sprk_budgetrevision` (matter, budget) | RemoveLink | Keep / Delete | Keep: it is a decision's record | – |
| `sprk_externalrecordaccess` (matter / project / work assignment) | RemoveLink + R4 deactivation | Keep (R4) / Delete | **Keep.** It is the history of external access, and R4 already ends it. | None. Under Option A, R4 would become dead for that type. |
| `sprk_assignedaccess` (matter / work assignment RemoveLink; **project Cascade**) | Mixed | Make all RemoveLink / make all Delete | Make **all RemoveLink** (change project→assignedaccess). Ledger rows are "never deleted"; audit is OFF on this table. | A metadata change (project→assignedaccess Delete to RemoveLink) |
| `sprk_ratecard` + `sprk_ratecardrate` (matter / project / work assignment; firm Restrict) | RemoveLink | Delete if matter-specific / Keep | Ask ebilling-r1 | – |
| `sprk_communication` + attachments, participants, review logs | RemoveLink | Delete / Keep | Delete, but **only when no other core claims it** (5 two-core rows; 3 more through a thread or invoice) | Multi-parent rule; SPE files of the 57 communication-only documents |
| `sprk_communicationthread` | RemoveLink | Delete thread; what about its communications? | Delete the thread, and only the communications whose own core is the deleted record. Re-home or unlink the others. | 2 communications on dev sit in another matter's thread |
| Documents tied only to a communication (`sprk_relatedcommunication`, 57) | RemoveLink | Delete with the communication / keep | Delete with the communication (they are its attachments) | SPE file + index |
| Multi-parent rows in general (D5) | – | (i) delete anyway; (ii) unlink from the deleted core and keep (re-home to the surviving core); (iii) refuse the delete and list them | **(ii)**, shown in the confirmation | – |

### 4.3 Must never cascade (keep RemoveLink, or Restrict)

| Relationship(s) | Why |
|---|---|
| `sprk_workassignment.sprk_regardingmatter` / `sprk_regardingproject` (matter 4, project 5); `sprk_servicerequest.sprk_regardingmatter` / `…project` / `…workassignment` (1) | CORE→CORE |
| `sprk_project_sprk_matter` (N:N, 3 links) | CORE↔CORE (disassociate only) |
| `email.sprk_matter` / `sprk_project` | System table; Dataverse forbids the cascade; an email can be filed in several places |
| Contact / organization / precedent N:N | REFERENCE |
| Grandchild paths into CORE: `sprk_workassignment.sprk_regardingevent` / `regardingcommunication` / `regardinginvoice` (1 row); `sprk_servicerequest.sprk_regardingcommunication` / `regardingtodo` / `regardinginvoice` | They would delete a core record transitively |
| Grandchild paths into REFERENCE or CATALOG: `contact.sprk_invoice`, `sprk_organization.sprk_invoice`, `sprk_analysisaction` / `analysisknowledge` / `analysisskill` / `analysistool .sprk_analysisid`, `sprk_ruleset` / `sprk_ratecard .sprk_sourcedocument`, `sprk_billingexport`, `sprk_processingjob`, `sprk_policyversion.sprk_policy` | Reference, config or finance |
| Self-references: `sprk_document.sprk_parentdocument` (77) / `.sprk_canonicaldocument`, `sprk_event.sprk_regardingevent`, `sprk_invoice.sprk_supersedesinvoice`, `sprk_invoicesubmission.sprk_resubmissionof` | Loops; a related row may belong to another core |
| SPE containers | Shared (55 matters → 2 containers) |

---

## 5. Decisions for the owner

| # | Decision | Recommendation |
|---|---|---|
| D1 | Mechanism: Dataverse cascade (Option A) or a BFF delete-core-record operation (Option B) | **B.** A cannot cover more than one core type per child, bypasses child privileges, and leaves SPE files and index chunks behind. |
| D2 | Decision Records on a deleted core | Keep (RemoveLink; the text core id survives) |
| D3 | Invoices, billing events, eBilling submissions/approvals/corrections, budget revisions | Keep or Restrict; agree with ebilling-r1 |
| D4 | External grants and the assigned-access ledger | Keep both (R4 ends grants; the ledger gets Revoked/RootDeleted). Change project→`sprk_assignedaccess` from Cascade to RemoveLink so all three roots behave the same. |
| D5 | A child filed to another live core record | Unlink from the deleted core and keep; list these rows in the confirmation |
| D6 | Who may hard-delete a core record | Today every Core User can (P:C), from the MDA and through the AI tool. Recommend restricting to the BFF route plus a named role, and pointing the AI tool at the route or refusing core tables. |
| D7 | SPE files of deleted documents | Delete the file with the row (verified-file-first); never the container |
| D8 | Communications, threads, and documents that exist only as a communication's attachments | Delete with the communication unless another core claims them |
| D9 | `sprk_policy_matter` is Cascade live, RemoveLink in master XML | Commit the XML change (owner kept the cascade), or the next import reverts it |

## 6. Not verified, and limits of this inventory

- Whether a **synchronous** cascade checks the caller's Delete privilege on each child: observed live (bypassed) but not documented by Learn. An async (≥5,000) cascade may behave differently.
- Whether async cascading is enabled on spaarkedev1, and what the `recyclebinconfig` values mean exactly.
- Whether a cascade-deleted child gets its own Delete audit row.
- The orphaned SPE just-in-time grants after a secure record delete: read from code, not tested.
- Every AI Search consumer dropping chunks of a deleted document: two read paths were read, not all.
- Bodies of some SPE-admin Graph delete calls, the wizard adapters' rollback callers beyond `EntityCreationService`, and every job handler's delete handling were not read.
- Row counts are dev data only. Production volumes (email, documents) decide whether the 5,000-row async threshold matters.
