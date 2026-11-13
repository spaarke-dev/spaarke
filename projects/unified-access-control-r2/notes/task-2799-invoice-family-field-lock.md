# #2799 — invoice-family field lock: writer inventory and the shared helper

Base: `task/fm-045g` @ 69ba3435a6 (field-mapping-server-write-path-r1 task 045g, not yet on master). Chunks 1 and 2 of the
split agreed with the main session on 2026-11-13. Chunks 3-6 (call-site conversion, script, ArchTests, docs and gates)
are launched separately.

## 1. The locked set (main-session ruling 1, 2026-11-13)

Derived from `InvoiceFamilyCoreRecordGuard`: for every table in `CoreRecordLookups.Keys`, every column of
`GuardedLookups(table)`. In code: `InvoiceFamilyLockedWrite.LockedColumns` (never a hand-copied list).

| Table | Core lookups (`CoreRecordLookups`) | Invoice link (`GuardedLookups` extra) |
|---|---|---|
| `sprk_invoice` | `sprk_matter`, `sprk_project`, `sprk_regardingworkassignment` | `sprk_supersedesinvoice` |
| `sprk_billingevent` | `sprk_matter`, `sprk_project`, `sprk_regardingworkassignment` | `sprk_invoice` |
| `sprk_invoicesubmission` | `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_regardingworkassignment` | `sprk_invoice` |
| `sprk_invoiceapproval` | the same regarding trio | `sprk_invoice` |
| `sprk_invoicecorrection` | the same regarding trio | `sprk_invoice` |
| `sprk_externalidentifier` | the same regarding trio | `sprk_regardinginvoice` |

24 columns. **Dev metadata (spaarkedev1, read-only, 2026-11-13):** every one is a `Lookup` with `CanBeSecuredForUpdate`
and `CanBeSecuredForRead` true and `IsSecured` false. None blocks the lock. Entity sets (dev): `sprk_invoices`,
`sprk_billingevents`, `sprk_invoicesubmissions`, `sprk_invoiceapprovals`, `sprk_externalidentifiers`,
`sprk_invoicecorrections`; targets `sprk_matters`, `sprk_projects`, `sprk_workassignments`, `sprk_invoices`.

## 2. Writer inventory

Identity key: **app** = the BFF application user (ServiceClient / `IGenericEntityService` / `IFieldMappingDataverseService`
without impersonation); **impersonated** = app token + `MSCRMCallerID` (`impersonateSystemUserId`); **OBO** = the caller's
own token (`IDataverseUserClient`); **user** = a client running as the signed-in user (Xrm.WebApi / fetch to Dataverse).

### 2.1 Caller-identity writers (must become check-as-the-person, write-as-the-app: G5)

| # | Writer | Where | Op | Identity | Tables reached |
|---|---|---|---|---|---|
| C1 | Field-mapping push | `Api/FieldMappings/FieldMappingEndpoints.cs:1583` → `InvoiceFamilyCoreRecordGuard.UpdateExistingAsync` (`InvoiceFamilyCoreRecordGuard.cs:1072-1092`) | update / clear | impersonated (`callerSystemUserId`) | every family table a rule targets (the push does not read `MappingProtectedColumns`, `ParentLineage.cs:1234-1235`, so a rule can carry a locked column) |
| C2 | Playbook Update Record node, and `ActionSeam.UpdateRecordAsync` (Job B apply / undo, `CommunicationProposalApplyService.cs:300/521`, `CommunicationCreateTaskApplyService.cs:383/534/622`) | `Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs:314` → guard `UpdateExistingAsync` | update / clear | impersonated when the run has a person (`input.ImpersonateSystemUserId`), app otherwise | every family table |
| C3 | Output orchestrator | `Services/Dataverse/DataverseUpdateHandler.cs:316` → guard `UpdateExistingAsync` (non-family tables: `:330`) | update / clear | impersonated when the run has a person, app otherwise | every family table |
| C4 | Child-record re-file core | `Services/Dataverse/OwnedChildWrite.cs:2471` (`RefileAsync`'s PATCH) | update / clear | OBO | `sprk_invoice` via `PATCH /api/v1/child-records/sprk_invoice/{id}` (`ChildRecordEndpoints.RefileTables`, `ChildRecordEndpoints.cs:117-120`); every family table via the chat update tool (`DataverseUpdateRecordHandler.cs:518`) |
| C5 | Child-record ordinary update | `Api/ChildRecordEndpoints.cs:840` (the `NotARefile` PATCH after `RefileAsync`) | update / clear | OBO | `sprk_invoice` (e.g. a write of only `sprk_supersedesinvoice`, which is not an ownership-parent change) |
| C6 | Chat update tool, ordinary update | `Services/Ai/Handlers/DataverseUpdateRecordHandler.cs:410` (the `NotARefile` PATCH) | update / clear | OBO | every family table |

**RefileAsync (main-session ruling 2):** NOT protected by `RecordTableRules.RefuseFixedAfterCreateColumns`. That rule
(`RecordTableRules.cs:556-570`) refuses only a table's `FixedAfterCreateColumns`, which are set for the Signal, Decision
Record and Budget Revision tables only (`RecordTableRules.cs:497-515`); no family table has any. So a re-file of an invoice
(C4) and the ordinary updates C5/C6 write locked columns as the caller today. They are converted like the others (chunk 3),
and no "refusal pin" test applies.

**Creates (C7, a lock-induced break, not a G5 conversion):** `RecordCreationService.CreateAsync`
(`Services/RecordCreation/RecordCreationService.cs:144`, step 11 at `:218-261`) already writes ONE app-only create after
checking as the caller (G5 for creates). But its caller check (3), `CallerWriteAuthorization.NoFieldSecuredColumnAsync`
(`CallerWriteAuthorization.cs:129-157`), REFUSES any column marked `IsSecured`, except the #1616 BFF-managed pair
(`OwnedChildWrite.BffManagedPairColumnsOf`). Once the lock is applied, every generic create of a family row that names
its core record or invoice would be refused ("field-secured and cannot be set"). Reached by: `POST /api/v1/child-records/{table}`
(`ChildRecordEndpoints.cs:274/311`), the chat create tool, and every other `RecordCreationService` caller, for
`sprk_invoice`, `sprk_invoicesubmission`, `sprk_invoiceapproval`, `sprk_externalidentifier`, `sprk_invoicecorrection`
(`sprk_billingevent` is `GenericCreateOnlyRefused`, `RecordTableRules.cs:386`). Check (2) of the same method already asks
AppendTo AS THE CALLER on every lookup the row sets, and check (1) the table's Create and Append privileges, so the
exemption #1616 made for the project pair applies in the same way: chunk 3 must exempt the locked columns from check (3)
(through `InvoiceFamilyLockedWrite.LockedColumnsOf`), with a test, before `-Apply`.

### 2.2 App-only writers (unchanged; covered by the Writers profile)

- Invoice, line, approval and submission rows at submission: `Services/Finance/Submission/InvoiceSubmissionPersister.cs:461/1699/1937/1968`,
  `InvoiceSubmissionService.cs:1593`, `InvoiceSubmissionRerun.cs:566/596/633`, `TrackingNumberAllocator.cs:604/792`.
- Add line, adjustments, line decisions, corrections, assign-firm, lifecycle: `InvoiceLineAddService.cs:1179`,
  `InvoiceAdjustmentService.cs:657`, `InvoiceLineDecisionService.cs:1268`, `InvoiceCorrectionService.cs:1424`,
  `InvoiceCorrectionReevaluation.cs:396/591/816/908/988`, `InvoiceAssignFirmService.cs:1066/1088/1106`,
  `InvoiceLifecycleService.cs:447`, `EligibleApproverResolver.cs:235`. All use the app's `IGenericEntityService` /
  ServiceClient; the finance services that hold an `IDataverseUserClient` (`InvoiceReviewReadService`, `SubmissionOverrideService`,
  `DuplicateDetailGate`) use it for reads only.
- `RecordCreationService` step 11 (the create itself), `RecordCreationService.Repair.cs:246`, `CoreAncestorRestamper.cs:781`
  (stamp columns, app-only).
- `ProjectMatterFiling` writes `sprk_project`'s pair (not a family table).

### 2.3 Clients, scripts, seed data, forms

- **Client source** (`src/client`, `src/solutions`): no `Xrm.WebApi` / fetch write of a family table. Invoice writes go through
  the BFF (`bffChildWriteAdapter.ts`, `OWNERSHIP_CHILD_TABLES`). The Create Matter wizard's invoice association was removed
  by 045g (`CreateMatterWizard.tsx:95-160`: "an invoice included, writes nothing"); the Create Invoice wizard's direct
  `createRecord('sprk_invoice')` is no longer in source.
- **Deployed web resources are older than source** (dev, read-only check 2026-11-13): `sprk_creatematterwizard`
  (modified 2026-11-01) and `sprk_spaarkeai` (2026-11-13) still contain `updateRecord("sprk_invoice", …, {"sprk_Matter@odata.bind": …})`;
  `sprk_createinvoicewizard` (2026-10-11) still contains `createRecord("sprk_invoice", …)`. The committed bundles under
  `src/dataverse/solutions/SpaarkeMaster/WebResources/` contain the same calls. They disappear once 045g's source is built
  and deployed. Consequences: (a) chunk 4's client-write precondition (p4a sibling) must scan the DEPLOYED web resources and
  will fail on dev until they are redeployed — that is the intended stop; (b) chunk 5's client ArchTest scans the committed
  bundles too, so they must be rebuilt (or ratchet-listed) in that chunk.
- **Detector scope (chunks 4-5):** the locked column NAMES (`sprk_matter`, `sprk_project`, `sprk_invoice`,
  `sprk_regardingmatter`, …) are also ordinary writable columns of many other tables (`sprk_event`, `sprk_document`,
  `sprk_todo`, …). A column-name-only detector like `Find-ClientPointerWrite.ps1` would fire on every one of them. The
  invoice-family detector has to be table-scoped (a write whose target table is a family table), which is a design point
  for chunk 4.
- **Scripts:** none writes a locked column. `scripts/ebilling/Backfill-EbillingSubmissionSourceEmail.ps1:166` patches
  `sprk_invoicesubmissions` with `sprk_SourceEmailDocument@odata.bind` only; `Repair-IntermediateFiledWorkAssignments.ps1`
  writes `sprk_workassignment`; `Backfill-RecordOwnership.ps1` writes `ownerid`; `Migrate-SearchIndexLookup.ps1` and the
  multi-container backfill write their own columns.
- **Seed data:** none (`scripts/seed-data/**` names no family table; `playbooks.json` targets `sprk_event`).
- **Forms:** `sprk_invoice` and `sprk_billingevent` main and quick-create forms show the lookups as editable controls
  (issue plan comment). Once locked they become read-only for users without the Writers profile, which is the point.
  No form script sets a locked column (no `getAttribute(<locked>).setValue` in any client or web-resource source).

## 3. The shared helper (chunk 2)

`src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyLockedWrite.cs` — static, no DI (ADR-010).

- `LockedColumns` / `LockedColumnsOf(table)`: the set in §1, from the guard.
- `CheckPersonMayWriteAsync(table, recordId, locked, rightsOf, ct)`: AS THE PERSON, through a rights question
  (`RightsOfPrincipal(probe, systemUserId)` wraps `CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync`;
  an OBO path may pass its own). Write on the row (plus Append when a lookup is set to a record), then AppendTo on every
  record a locked lookup now names. Any unanswered question → `Unavailable`; a refusal, a missing row or target, or an
  unreadable value → `Denied` (one answer: an unknown id and an unappendable one are alike). Nothing is written by it.
- `UpdateExistingAsync(records, table, recordId, fields, impersonateSystemUserId, notFoundAsReadFailed, rightsOf, ct)`: the
  drop-in for the guard's `UpdateExistingAsync` on C1-C3. No locked column, or an app-only run → exactly the old single
  write. Otherwise: check; `Unavailable` → `ReadFailedCode`, `Denied` → `LockedWriteDeniedCode`, nothing written; allowed →
  the other columns as the person (their own field security and privileges still apply), then the locked columns as the app.
- `SplitBody(table, jsonBody)`: for C4-C6, the locked fields (for the app write) and the rest of the JSON body (the
  person's own PATCH).

**Right on the row:** Append, not AppendTo — setting a lookup costs Append on the row that holds it and AppendTo on the
record it names (Dataverse's rule; the precedent is `ProjectMatterFiling` step 4: Write|Append on the project, AppendTo on
the matter). The brief's "AppendTo on the row" is read as this.

**Known limit (same as `ProjectMatterFiling`):** a mixed write is two requests. If the app write fails after the person's
write of the other columns succeeded, those columns stay written; the answer says nothing locked was written.
