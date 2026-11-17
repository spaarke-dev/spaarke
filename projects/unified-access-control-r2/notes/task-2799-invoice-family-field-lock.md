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

### Known limits decided by the main session (review 1, 2026-11-13)

- **AppendTo is asked on every record a locked lookup names, including one it already names.** Stricter than "only a
  newly named record", fails closed, and is what Dataverse itself asked of the person's own PATCH before the split (a PATCH
  that sets a lookup asks AppendTo on its target whether or not the value changed). Not changed.
- **The probe's 404 retry timing** (`CallerRecordAccessProbe`, existing behaviour, hard to reach). Not changed.
- **Key forms (review 1 F3).** A locked key is written as the app only in a form the 045g guard reads as that column
  (`InvoiceFamilyCoreRecordGuard.ColumnOfKey`: the logical name or `{navigation}@odata.bind`). The guard compares both
  parts **without regard to case** (it lower-cases the column and matches the `@odata.bind` suffix with `OrdinalIgnoreCase`),
  so `SPRK_Invoice@odata.bind` is the guard's `sprk_invoice` and is checked, not refused; following the guard was the brief's
  rule. Dataverse's own navigation names are case-sensitive, so such a key would fail at Dataverse (after the person's
  write on a mixed write: partly saved, never success). `_sprk_invoice_value`, `sprk_invoice@foo` and any key that
  `NormalizeColumn` maps to a locked column but the guard does not are Denied, nothing written.

### K-1 — a mixed write is two requests (known limit)

A write that sets locked and other columns is two requests: the other columns as the person (their own privileges and
field security apply), then the locked columns as the app. If the second fails after the first landed, the other
columns stay saved. The second is not the request's to cancel (`CancellationToken.None` once the first landed; review 1
F1). On C3's Optimistic mode the first carries the row-version precondition; the second cannot (the first changed the
version), so the window is the same. The answer is never success: each path returns its error answer with fixed text saying the change
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

## 8. Review 1 fixes (2026-11-13)

Four findings, each fixed in its own commit on `fix/2799-invoice-family-field-lock` (local; not pushed).

| # | Finding | Fix | Tests |
|---|---|---|---|
| F1 | The application's second write used the request's token, so a client disconnect or playbook timeout after the person's write landed turned an allowed write into a partly-saved one | `UpdateExistingSplitAsync` and `PatchAsCallerAsync` send the application's write with `CancellationToken.None` once the person's write landed (precedent: the after-write steps, `FieldMappingEndpoints` `restamper.AfterWriteAsync(..., CancellationToken.None)`, `OwnedChildWrite` `AfterReparentAsync`). A locked-only write (nothing landed yet) keeps the request's token | `InvoiceFamilyLockedWriteTests`: the request is cancelled when the person's write lands; the app write still runs (helper and OBO) |
| F2 | `InvoiceFamilyFieldLockGuardTests` cleared a whole method once it called the split, so a direct caller write added beside it was unseen | New ratchet `SplitMembers_DirectCallerWrites_AreRatcheted`: each member that calls the split is pinned to its count of direct caller-identity write calls (`IsCallerIdentityWrite`, per IL call site), with a reason. C1 `ApplyMappingsToChildRecordsAsync` 1, C2 `UpdateAsync` 1, C3 `PatchAsync` 1 (each the non-family branch: `IsUpdateOnlyTarget` false, the same keys `LockedColumns` comes from); C4 `RefileAsync`, C5 `UpdateAsync`, C6 `ExecuteChatAsync` 0. More = NEW, fewer = STALE, unpinned split member = NEW, pin on a non-split member = STALE | Control `SplitMemberRatchet_MustFireAndMustNotFire`: fixture `InvoiceFamilyCallerWriteControlSplitPlusDirect` (split + a direct `PatchAsync`) fires; the split alone at its pin does not |
| F3 | The split decided "locked" with `NormalizeColumn` (any `@…`, `_x_value`); the guard decides on `ColumnOfKey` (logical name or `…@odata.bind`). A key the guard never decided on could be written as the app | `CheckPersonMayWriteAsync` reads each locked key with the guard's own `ColumnOfKey` (made `internal`); a key that normalizes to a locked column but is not the guard's column is Denied, nothing written or asked. The guard is case-insensitive, so a differently cased bind is followed as the guard reads it (§4) | `_sprk_invoice_value`, `sprk_invoice@foo`, `sprk_invoice@odata.bind@odata.bind` → Denied, nothing written or asked; OBO `_sprk_matter_value` → Denied; `sprk_invoice`, `sprk_Invoice@odata.bind`, `SPRK_Invoice@odata.bind` → checked, written by the app |
| F4 | C3's Optimistic mode adds `versionnumber` to the fields; the split put it with the person's columns, so a locked-only write sent a person write of the version alone | `UpdateExistingSplitAsync(expectedVersion)`; `DataverseUpdateHandler` passes the version it read. Person part = the version alone → no person write; one app write of the locked columns via `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync` (If-Match `W/"version"`, never creates; 404 → the guard's not-found code). Its `DBConcurrencyException` is now a concurrency conflict in `IsConcurrencyException` (retried, then the path's existing "Optimistic concurrency update failed after N attempts"). Otherwise the person's write carries the version as before, then the app's (K-1). No person write → never "partly saved" | Helper: version-only → one conditional app write on 42, no person write; mismatch → `DBConcurrencyException`, nothing written; a fault → propagates, not partly saved; mixed → person write carries `versionnumber`, then app. C3 (`InvoiceFamilyGenericUpdatePathTests.LockedWriteConcurrency.cs`): the same three through the real handler, the mismatch retried twice then refused |

**Perturbations** (each on a committed tree, compiling, reverted with `git checkout`; working tree clean afterwards):

| # | Perturbation | Failed |
|---|---|---|
| P-F1 | `InvoiceFamilyLockedWrite.cs` back to 9bf53be69f (the request's token on the app write) | 2 (helper + OBO cancellation tests) |
| P-F2 | `DataverseUpdateHandler.PatchAsync`: a second impersonated `UpdateExistingRecordFieldsAsync` in the family branch, beside the split | 1 (the new ratchet: NEW, 2 vs pinned 1). The pre-existing whole-method guard and `CoreRecordPairUpdateCheckGuardTests` stayed green, which is the gap F2 closes |
| P-F3 | `CheckPersonMayWriteAsync` back to `NormalizeColumn` | 4 (3 key-form theory cases + the OBO case) |
| P-F4a | the handler no longer passes `expectedVersion` | 2 (C3 version-only and mismatch) |
| P-F4b | the helper's version-only branch disabled | 5 (3 helper + 2 C3) |
| P-F4c | `IsConcurrencyException` without `DBConcurrencyException` | 1 (C3 mismatch: no retry, raw exception) |

**Gates** (HEAD cbd496b2d4). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. Full `tests/Spaarke.ArchTests`: 2554 passed, 0 failed, 0 skipped (was 2552; +2 = the F2 ratchet and its control). `Sprk.Bff.Api.Tests` filtered to the helper, C1-C6 paths, field mapping, the update handler, Update Record, child records, the chat update tool, the 045g guard and every `*Concurrency*` class: 2381 passed, 0 failed, 0 skipped. Helper `InvoiceFamilyLockedWriteTests` 42 → 53.

## 9. Review 2 fixes (2026-11-14)

Five findings, each fixed in its own commit on `fix/2799-invoice-family-field-lock` (local; not pushed; rebased on master
cfa97d718b).

| # | Finding | Fix | Tests |
|---|---|---|---|
| R2-1 | The locked-column check asked `RetrievePrincipalAccess` (app-only naming the person on C1-C3; under the person's token on C4-C6), which answers from roles, teams and shares, not from the user's own state. With only locked columns written nothing went out as the person, so a disabled user, or one whose access mode does not write, could get an app write their own write would have been refused | `CheckPersonMayWriteAsync` takes a `PersonStateQuestion`, asked ONCE per write after the key forms are read and before any rights question. `PersonStateOf(appOnly, systemUserId)` reads `systemusers(id)?$select=isdisabled,accessmode` app-only through the existing `IFieldMappingDataverseService.RetrieveRecordFieldsAsync` (no new reader; no impersonation, so a disabled user cannot fault it). Allowed only when `isdisabled` is `false` and `accessmode` is 0 (Read-Write) or 4 (Non-interactive). No row (the read's 404 answers no columns), an unreadable value or `Guid.Empty` → Denied (the one answer); a fault → Unavailable (`invoice_family_read_failed` / `CallerPatchKind.Unavailable`); only the caller's own cancellation is rethrown. C1-C3 pass the split's `records`; C4-C6 the `appOnly` writer with the WhoAmI id. Unchanged: a write with no locked column and a run with no person ask nothing | Helper (`InvoiceFamilyLockedWriteTests`, 53 → 73): disabled → Denied, nothing written or asked; accessmode 1/2/3/5 → Denied; 0 and 4 → Allowed, state read once for two locked columns; no row → Denied, byte-identical (code and message) to an unknown record; unreadable values → Denied; read fault → Unavailable, fault text not logged; not asked without a locked column or on an app run; OBO: the same five states, missing row ≡ unknown record, fault → Unavailable. C1-C3 (`InvoiceFamilyGenericUpdatePathTests.LockedWrite.cs`, every path): disabled / Read / Administrative → denied, nothing written or asked; Non-interactive → allowed, state read once; no row ≡ unknown core record; read fault → read-failed. C4-C6 (`SecureChildOwnershipAiToolTests.InvoiceFamilyLockedWrite.cs`): disabled / Read / Administrative → the path's denial (403 `child_record.denied` / `AccessDenied`) on route and tool, ordinary and re-file; Non-interactive allowed; no row ≡ the check's own denial, byte-identical; fault → 500 `invoice_family_read_failed` |
| R2-2 | `-Target InvoiceFamilyLinks` (p4a) reported a locked lookup whose navigation name differs from its column as INFO, but the BFF reads a locked bind only as `{column}@odata.bind` (`ColumnOfKey`), so such an environment would break the BFF's writes once locked | That case is now a FAIL (a case-only difference still passes: the guard reads binds without regard to case). Dry run and `-Verify` report it; `-Apply` refuses with nothing written. Only this target has the check; others unchanged | Pester (`Set-InvoiceFamilyFieldSecurity.Tests.ps1`): `-Apply` refused, nothing written (must fire); `-Verify` fails (must fire); a case-only difference on all 24 columns passes `-Verify` (must not fire) |
| R2-3 | The shared engine printed every enabled System Administrator user and team by name on every run (every target) | One INFO line of counts (enabled users, of them application users, teams); names only with the new `-ListSystemAdministrators` switch (engine; forwarded by `Set-InvoiceFamilyFieldSecurity.ps1`). Other entry points and existing switches unchanged (they do not forward the new switch; the engine takes it directly) | Pester: default output names no holder and carries the counts (a disabled holder is not counted); the switch lists the enabled holders. Regression `Set-AccessMarkerFieldSecurity.Tests.ps1` 19/19 |
| R2-4 | `IsCallerIdentityWrite` did not count `IDataverseUserClient.PostAsync` (OBO create, `$batch`) | `PostAsync` is a caller-identity write. Six existing callers on the closed list, each with its reason: `DataverseCreateRecordHandler.ExecuteChatAsync` (its run-as-user POST is reached only after every family table is refused: `IsInvoiceCreate`, `ServerWrittenInvoiceFamilyTables`, `IsCreatedOnlyByItsWriter` for `sprk_billingevent`), `DataverseSearchDataHandler.ExecuteChatAsync` (search query, a read), `EmailDraftToolHandler.ExecuteDraftAsync` (`sprk_communication`), `ReportingEndpoints.CreateReport` (`sprk_reports`), `OntologyAdminAuthoringService.PublishVersionAsync` (`sprk_policyversions`), `OntologyAdminClassificationService.CreateAsync` (`sprk_triagecategories`). No split member's ratchet count changed | Control `CallerWriteDetector_MustFireAndMustNotFire`: fixture `InvoiceFamilyCallerWriteControlPostBypass` (an OBO `PostAsync` creating a `sprk_billingevents` row with `sprk_Invoice@odata.bind`, and a `$batch` `PostAsync`) fires; the through-the-split and other-API fixtures still do not |
| R2-5 | A denial from the locked-column check surfaced on C1-C3 as "the invoice-family core-record rule refused this write (invoice_family_locked_write_denied)" | `InvoiceFamilyLockedWrite.LockedWriteDeniedMessage`: "You do not have permission to change this record's invoice links (the matter, project, work assignment or invoice it is filed under); nothing was written (invoice_family_locked_write_denied)." Same text for every denial (unknown id, missing right, disabled user; NFR-01). `RefusalMessage(code)` serves C1 (`"Not written: " + …`) and C2/C3 (`RefusalException`); unavailable keeps `invoice_family_read_failed`'s code-only text; partly saved unchanged | Helper: the denied text pinned, code kept, no "core-record rule"; the read-failed refusal still the guard's code-only text. C1-C3: the denied outcome carries the new text; the unanswered outcome the read-failed text; `Locked_AnUnknownCoreRecord_…` (unchanged) still pins unknown ≡ denied including the message |

**Perturbations** (each on a committed tree, compiling, reverted with `git checkout`; working tree clean afterwards):

| # | Perturbation | Failed |
|---|---|---|
| P-R2-1 | `CheckPersonMayWriteAsync`: the state answer ignored (`if (state == (LockedWriteCheck)99)`) | 34 (every must-fire state test in the helper, C1-C3 and C4-C6; the Allowed and not-asked tests stayed green) |
| P-R2-2 | the nav-name `Report 'FAIL'` back to `'INFO'` | 2 (Pester: `-Apply` refused; `-Verify` fails) |
| P-R2-3 | the name listing unconditional (`if ($true)`) | 1 (Pester: default output names no holder) |
| P-R2-4 | `IsCallerIdentityWrite` without `PostAsync` | 2 (the control, and the guard: the six closed-list entries reported STALE) |
| P-R2-5 | `RefusalMessage`: denied back to the guard's code-only text | 4 (helper text pin + the C1-C3 denied-text theory, 3 paths) |

**Gates** (HEAD c03cc41ec1). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings. Full `tests/Spaarke.ArchTests`: 2554 passed, 0 failed, 0 skipped (count unchanged: R2-4 extended the existing control). `Sprk.Bff.Api.Tests` filtered to the helper, C1-C6 paths, field mapping, the update handler, Update Record, child records, the chat update tool, the action seam, the 045g guard and every `*Concurrency*` class: 3317 passed, 0 failed, 0 skipped. `InvoiceFamilyLockedWriteTests` 73, `InvoiceFamilyGenericUpdatePathTests` 616, `SecureChildOwnershipAiToolTests` 801. Pester 5.7.1: `Set-InvoiceFamilyFieldSecurity.Tests.ps1` 13/13 (was 8; +3 R2-2, +2 R2-3), `Set-AccessMarkerFieldSecurity.Tests.ps1` 19/19.

**Not changed (scope).** C7 (creates, `CallerWriteAuthorization`) asks its questions under the caller's OBO token and was not in R2-1's brief; it reads no `isdisabled` / `accessmode` either (flagged for the main session). The other entry points (`Set-AccessMarkerFieldSecurity.ps1`, `Set-ReportCatalogFieldSecurity.ps1`, `Set-DocumentNarrowingFieldSecurity.ps1`) do not forward `-ListSystemAdministrators`.
