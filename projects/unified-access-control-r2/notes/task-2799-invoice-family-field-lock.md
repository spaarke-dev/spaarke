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
- `UpdateExistingSplitAsync(records, table, recordId, fields, impersonateSystemUserId, notFoundAsReadFailed, rightsOf, ct)`: the
  drop-in for the guard's `UpdateExistingAsync` on C1-C3. No locked column, or an app-only run → exactly the old single
  write. Otherwise: check; `Unavailable` → `ReadFailedCode`, `Denied` → `LockedWriteDeniedCode`, nothing written; allowed →
  the other columns as the person (their own field security and privileges still apply), then the locked columns as the app.
- `SplitBody(table, jsonBody)`: for C4-C6, the locked fields (for the app write) and the rest of the JSON body (the
  person's own PATCH).

**Right on the row:** Append, not AppendTo — setting a lookup costs Append on the row that holds it and AppendTo on the
record it names (Dataverse's rule; the precedent is `ProjectMatterFiling` step 4: Write|Append on the project, AppendTo on
the matter). The brief's "AppendTo on the row" is read as this.

**Known limit:** see K-1 in §4 (a mixed write is two requests).

## 4. The wiring (chunk 3)

Every caller-identity writer in §2.1 now checks AS THE PERSON and writes the locked columns AS THE APP. A write that sets
no locked column, and a run with no person (C2/C3 app-only), keeps its single write and identity, unchanged.

| # | Where | How | Refusals |
|---|---|---|---|
| C1 | `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` (`Write()`) | `UpdateExistingSplitAsync`, `RightsOfPrincipal(probe, callerSystemUserId)` (the probe the push already resolves) | per-row "Not written: …" with the guard's code-only text; partly saved → `PartlySavedMessage` |
| C2 | `UpdateRecordActionCore.Patch()` | same; the probe from a scope when the run has a person | `RecordOwnerUnresolvedException` (the seam's typed failure), like the guard's other codes |
| C3 | `DataverseUpdateHandler.PatchAsync` | same; an optional `CallerRecordAccessProbe` ctor parameter | `RecordOwnerUnresolvedException`, like the guard's other codes |
| C4 | `OwnedChildWrite.RefileAsync` (the write callback) | `PatchAsCallerAsync`, `RightsOfCaller` (RetrievePrincipalAccess under the caller's token) | `RefileOutcome.LockedWrite` → the route / tool maps it (C5 / C6 below) |
| C5 | `ChildRecordEndpoints.UpdateAsync` | `PatchAsCallerAsync`; the app writer from the request services | denied → 403 `DeniedCode`; unavailable → 500 `invoice_family_read_failed` (the path's existing unavailable answer); partly saved → 500 `invoice_family_locked_write_partly_saved` |
| C6 | `DataverseUpdateRecordHandler` (ordinary and re-file) | `PatchAsCallerAsync`; the app writer from a scope | denied → `AccessDenied`; unavailable → `invoice_family_read_failed`; partly saved → `ServiceError` with `PartlySavedMessage` |
| C7 | `CallerWriteAuthorization.NoFieldSecuredColumnAsync` | the locked columns (`LockedColumnsOf`) are not asked as field-secured, like the #1616 pair; (1) and (2) still ask Create/Append and AppendTo AS THE CALLER, and the create stays one app-only write | unchanged (a secured column that is not locked is still refused) |

An unknown record answers exactly as one the person may not use (NFR-01): Dataverse's 403 and 404 are both `Denied`.
On the OBO paths a failed WhoAmI keeps the path's existing client-failure answer.

### K-1 — a mixed write is two requests (known limit)

A write that sets locked and other columns is two requests: the other columns as the person (their own privileges and
field security apply), then the locked columns as the app. If the second fails after the first landed, the other
columns stay saved. The answer is never success: each path returns its error answer with fixed text saying the change
was only partly saved (`invoice_family_locked_write_partly_saved`; no exception text), and a Warning is logged with the
table, the record id and the TraceId. Covered by `InvoiceFamilyLockedWriteTests` (helper), `InvoiceFamilyGenericUpdatePathTests`
(C1-C3) and `SecureChildOwnershipAiToolTests` (C4-C6). Same shape as `ProjectMatterFiling`'s `OtherColumnsSaved`.

## 5. The lock script and the client-write detector (chunk 4)

**Target, not a new engine.** `scripts/Set-DocumentPointerFieldSecurity.ps1` gains `-Target InvoiceFamilyLinks`;
`scripts/Set-InvoiceFamilyFieldSecurity.ps1` is its named entry point (dry run default, `-Apply`, `-Verify`; default solution
SpaarkeMaster). Reason: that engine is already "the ONE mechanism" for task 133's two profiles and carries the
named-entry-point precedent (Narrowing, AccessMarker, ReportCatalog). It already handles multi-table `Pairs`, the p1-p7
preconditions, secure-then-grant-at-once and the revert. A separate script would fork its helpers.

**The one list.** `scripts/common/InvoiceFamilyLockedColumns.psd1` (6 tables, entity sets, 24 columns). The engine target,
the detector and the ArchTest read it; the ArchTest pins it both ways to `InvoiceFamilyLockedWrite.LockedColumns` and
`EntitySets` (§6).

**Preconditions for this target.**
- p1/p2/p3/p6/p7 unchanged: profiles exist and are in the solution; Readers on every business unit's default team
  (`-Verify` fails otherwise); Writers on the BFF application users only; family tables are root components; columns exist.
- **p4a (new, fail closed):** reads each family table's ManyToOne navigation names (FAIL if none can be read), then scans
  every DEPLOYED web resource (types 1 and 3) with `scripts/common/Find-ClientInvoiceFamilyWrite.ps1`. Each hit is a FAIL
  naming the web resource, table and shape. Dry run reports; `-Apply` refuses with nothing written.
- **p4b (new):** `-BffWritesInvoiceFamilyLinksAsApp`, the operator's confirmation that the BFF with chunks 1-3 is deployed;
  required with `-Apply` (same shape as the AccessMarker target's confirmation switch).
- **p5 is informational for this target.** The configuration-table check exists for the pointer targets, where config rows
  name client-written columns. Here config rows (field mappings) drive BFF split writers, which write as the application,
  so there is nothing to refuse. Flagged for confirmation.

**Detector** (`scripts/common/Find-ClientInvoiceFamilyWrite.ps1`, next to `Find-ClientPointerWrite.ps1`). Table-scoped: a
hit needs a family table AND a locked column of THAT table. Shapes: (a) `createRecord`/`updateRecord` with a literal family
table name or a variable bound to one, payload taken from the call's top-level arguments; (b) `fetch` / XHR with
`POST|PATCH|PUT` and a family entity-set path segment; (c) `$batch` request lines. Payload rules: a key equal to a locked
column (with or without `@odata.bind`, any case) is a hit; a non-literal payload, a spread or a computed key is a hit
(fail closed). `@odata.bind` VALUES that merely point at a family row (an event's `sprk_Invoice@odata.bind`) are not
writes of the family table. Known limits (file header): entity names chosen at run time from data, object-key bindings
built elsewhere, and form-script `setValue` on family forms (not table-attributable from the script text). Once locked,
the secured column refuses all three at run time; the detector is pre-lock evidence, not the enforcement.

**Dev, read-only (2026-11-13, spaarkedev1; operator az CLI identity; nothing written).**
- Dry run: exit 0, "DRY RUN complete — nothing was written". p1 OK (both profiles, in SpaarkeMaster); p6 OK (all 6
  tables root components, behavior 0); p2 OK (Readers on all 6 business units' default teams); p3 OK (both BFF
  application users are Writers); p5 INFO; p7 OK (24 of 24 columns exist); navigation names equal the column names on
  all 6 tables; p4b INFO. **p4a FAIL x3**, as expected: `sprk_creatematterwizard` (updateRecord sets
  `sprk_Matter@odata.bind` on sprk_invoice), `sprk_spaarkeai` (same shape; the deployed copy has one such write, the
  committed bundle two), `sprk_createinvoicewizard` (createRecord on sprk_invoice with a payload variable, fail closed).
  Would secure 24 columns and add 48 field permissions.
- `-Verify`: exit 1, 75 FAIL = the 3 p4a findings + 72 MISSING (24 columns not secured, 48 grants absent). p1, p2, p3,
  p6, p7 OK. Expected: nothing is locked until the owner-approved `-Apply`, after the three web resources are rebuilt
  from 045g source and deployed.

**Tests.** `tests/scripts/Set-InvoiceFamilyFieldSecurity.Tests.ps1` (Pester, mocked Dataverse, the harness of
`Set-AccessMarkerFieldSecurity.Tests.ps1`): 8/8 pass. The list is 24 columns on 6 tables; dry run reports a stale bundle
and writes nothing; `-Apply` refuses with nothing written (must fire); refuses without the confirmation switch; `-Apply`
with only an sprk_event write secures and grants all 24 and publishes the 6 tables (must not fire); `-Verify` fails
unlocked, passes locked, fails when a default team lacks Readers. Regression: `Set-AccessMarkerFieldSecurity.Tests.ps1`
19/19.

## 6. The ArchTests (chunk 5)

`tests/Spaarke.ArchTests/InvoiceFamilyFieldLockGuardTests.cs`: three guards, each with must-fire and must-not-fire controls.

1. **One list, both ways.** The psd1's tables, columns and entity sets equal `InvoiceFamilyLockedWrite.LockedColumns` /
   `EntitySets` (24 columns, 6 tables asserted); the engine, entry point and detector read the psd1 (no inline copy).
2. **No client writes.** Scans `src/client`, `src/solutions` and the committed bundles under
   `src/dataverse/solutions/**/WebResources/**` through the PowerShell detector (one implementation; the test runs it
   through pwsh, as the pointer parity test already does). **Ratchet baseline** (hits per file; more hits = NEW, fewer =
   STALE, both fail), reason "stale committed bundle; removed when rebuilt from 045g source":
   `SpaarkeMaster/WebResources/sprk_creatematterwizard` 1, `sprk_spaarkeai` 2, `sprk_createinvoicewizard` 1. Source
   (`src/client`, `src/solutions`) has zero hits. Detector controls: 19 write cases fire (incl. the seeded
   `updateRecord("sprk_invoice", id, {"sprk_Matter@odata.bind": ...})`); 15 read or other-table cases do not (incl. the
   same column on sprk_event).
3. **No caller-identity bypass.** Reuses `CoreRecordPairUpdateCheckGuardTests`' IL scan and per-source-member
   classification (used, not modified). A source member that calls a caller-identity write
   (`IDataverseUserClient.PatchAsync`, `IFieldMappingDataverseService.UpdateRecordFieldsAsync` /
   `UpdateExistingRecordFieldsAsync`, `InvoiceFamilyCoreRecordGuard.UpdateExistingAsync`) must also go through
   `InvoiceFamilyLockedWrite` (`PatchAsCallerAsync` / `UpdateExistingSplitAsync`) or sit on a closed list of members that
   cannot carry a locked column (20 entries, each with its reason). Non-vacuity: the C1-C6 members from §4 are found.
   Fixtures: patch bypass, impersonated bypass and guard bypass fire; through-the-split and another PATCH API do not.

**Perturbations** (each on a committed tree, compiling, reverted with `git checkout`; working tree clean afterwards):

| # | Perturbation | Failed |
|---|---|---|
| P1a | drop `sprk_supersedesinvoice` from the psd1 | 3 (set equality, list control, detector control) |
| P1b | add `sprk_name` to sprk_invoice in the psd1 | 3 (same three) |
| P2a | seeded `updateRecord("sprk_invoice", id, {"sprk_Matter@odata.bind": ...})` in `TodoRegardingUpdateBuilder.ts` | 1 (ratchet NEW) |
| P2b | the same write on `sprk_event` | 0 (must not fire) |
| P2c | committed `sprk_createinvoicewizard`: `createRecord("sprk_invoice", c)` to `"sprk_event"` | 1 (ratchet STALE) |
| P3 | `OwnedChildWrite`: new member calling `user.PatchAsync` on `sprk_invoices` | 1 here (+1 in the CoreRecordPair guard) |
| P3b | new member calling `InvoiceFamilyCoreRecordGuard.UpdateExistingAsync` with the person | 1 |

P2a/P2b are TypeScript edits, not type-checked; syntactically valid.

**Gates.** `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. New ArchTests 8/8. Full
`tests/Spaarke.ArchTests`: 2552 passed, 0 failed, 0 skipped (HEAD 7853e50750). Pester 8/8 (new), 19/19 (AccessMarker).

## 7. Docs (chunk 6)

- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: new invariant registry row I-24 (the 24 link columns are written
  only by the BFF application user; check as the person, write as the app). Owner `InvoiceFamilyLockedWrite`; enforced by
  field security, `InvoiceFamilyFieldLockGuardTests` and the client detector; K-1 noted.
- `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`: new "Invoice-family links: field security" step after the message-access
  columns step (the guide's closest field-security step; it had no invoice-family section). Order: BFF, web resources (p4a),
  dry run, `-BffWritesInvoiceFamilyLinksAsApp -Apply` (owner go), `-Verify`.
- `tests/integration/auth/UnifiedAccessControl/ChildRecordContainerResolutionTests.cs` (comment only, ~line 2004): the
  work-assignment/invoice filing-loop row pair is now described as one the model-driven app could store before the lock.
  No test code changed.
