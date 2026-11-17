# ISS-028 (#2879) — correction rows are written only by their writer

Decision: EB-D-248. Task: `tasks/iss028-invoicecorrection-writer-only.poml`.

## The rule

`RecordTableRules` now sets `CreatedOnlyByItsWriter = true` on the `sprk_invoicecorrection` entry, the same flag the Signal and Decision Record entries carry.
No new flag, no new guard type, and nothing else on the entry changed (kind, invoice-family pre-write rule and mapping-protected core lookups are as before;
no `FixedAfterCreateColumns` or `WriterOnlyColumns` were added).

A correction row, and the reviewer's note it carries (`sprk_note`, `InvoiceCorrectionNote.CorrectionRowColumn`), is created and changed only by the two
services that own it: `InvoiceCorrectionService` (a reviewer's correct or confirm) and `InvoiceLineDecisionService` (the "Accepted in bulk" and
"Unreviewed" rows of a line decision). Both send their rows through `IGenericEntityService.ExecuteTransactionAsync` (an `UpsertRequest` per row with a
deterministic id). That call asks none of the writer-only refusals, so neither writer is affected.

## What the flag does on each path

| Path | Effect on `sprk_invoicecorrection` |
|---|---|
| General create (`RecordCreationService`, route and mapped items, caller and system principal) | Refused at the start of the pipeline as `record_creation.table_unsupported` (400), before any read (`GenericCreateRefused` is implied by the flag). |
| Chat create tool (`DataverseCreateRecordHandler`, validate and execute) | Already refused (the invoice-family block, asked first). It keeps those words; `IsCreatedOnlyByItsWriter` now also answers true for the table. |
| Chat update tool, Update Record node and action seam, playbook output mapping (`DataverseUpdateHandler`), work-product persister, field-mapping push, child-records update and re-file core | Refused whole by `RecordTableRules.RefuseWriterOnlyTableUpdate` (`record_update.changed_only_by_its_writer`), before any read or write. |
| Chat delete tool (`DataverseDeleteRecordHandler`) | Refused by `RefuseWriterOnlyTableDelete` (`record_delete.changed_only_by_its_writer`). Delete reads this flag alone. |
| Record repair pass (`RecordRepairPassJob.ExcludedTables`) | The table joins the tables the pass never sweeps (it excludes every writer-only table). The pass never wrote a correction row before this change: repair refuses every invoice-family write, failing closed (`RecordCreationService.Repair.cs:225` and `:936-948`, `InvoiceFamilyGuardUnavailable`, TODO #1911). Secure-family ownership of a correction row is still covered by `SecureChildLineage.cs:306` plus the `SecureChildReconciler`, pinned by `SecureChildOrdinaryParentMoveTests.cs:241-255`. |

## Every reference to the table

| Reference | Affected? |
|---|---|
| `RecordTableRules.cs` (the entry) | Changed: the flag, with a comment. |
| `InvoiceCorrectionService.cs` (`BuildRow`, `ExecuteTransactionAsync`) | No. Writes through `IGenericEntityService.ExecuteTransactionAsync`; no generic path. |
| `InvoiceLineDecisionService.cs` (`CorrectionRow`, `CorrectionEntity`, `ExecuteTransactionAsync`) | No. Same call. |
| `InvoiceCorrectionNote.cs` | No. Names the note column; no write. |
| `InvoiceCorrectionReevaluation.cs`, `InvoiceFindingsDiff.cs`, `InvoiceSubmissionRerun.cs`, `InvoiceLineAddService.cs` | No. Read-only (`QueryExpression`) or call the services above. |
| `DataverseCreateRecordHandler.cs` (`ServerWrittenInvoiceFamilyTables`) | Overlap only. The invoice-family block is asked before the writer-only block, so the chat words are unchanged; `IsCreatedOnlyByItsWriter` now also answers true. |
| `InvoiceFamilyCoreRecordGuard.cs`, `InvoiceFamilyLockedWrite.cs` | No change. The guard still lists the table (defence in depth), but no generic path reaches it for this table any more, because the writer-only refusal is asked first. |
| `OwnedChildWrite.cs` (`RegardingPairTables`; the re-file core) | The pair list is a column map, not a write path: unchanged. The re-file core already asks `RefuseWriterOnlyTableUpdate`, so it now refuses the table. |
| `SecureChildLineage.cs`, `RecordFamilyEdges.cs`, `RecordFamilyReader.cs`, `ParentLineage.cs` | No. Lineage and read registries. The secure-child reconciler re-owns rows with its own app-only writes after a re-file; it does not ask the refusals (the same as for Signals and Decision Records). |
| `RecordCreatorPerson.cs`, `ExternalTeamShareSynchronizer.cs` (never-shared list), `ExternalInvoiceVisibility.cs` | No. Registries and read filters. |
| `RecordRepairPassJob.cs` | Yes, by design: the table is no longer swept (see the table above). |
| Client code, playbooks, config files and `scripts/ebilling/*.ps1` | No client code or playbook references the table, and no generic client caller exists. Some provisioning config files and `scripts/ebilling/*.ps1` name it; none of them writes rows, so none is affected. |

No legitimate writer goes through a generic path the flag refuses, so nothing was widened.

## The tests

New: `tests/integration/data-mutation/Ebilling/InvoiceCorrectionWriterOnlyTests.cs`.
- Must fire: the rule is flagged and the generic create, update and delete answers carry the writer-only codes (trimmed and any case).
- Must not fire: the rest of the entry is as before and the sibling family tables stay open; `InvoiceCorrectionService` still writes the row and its note in one
  transaction for all four shapes (line and header, correct and confirm) with the refusal in force; `InvoiceLineDecisionService` still writes its bulk rows.

The table was added to the existing writer-only theory lists, so every generic path is proved to refuse it: the general create (`RecordCreationServicePipelineSeamTests.WriterOnlyTables`, all
three ways in), the Update Record node and core and the output mapping (`WriterOnlyTablesUpdateRefusalTests`), the chat update (`SecureChildOwnershipAiToolTests.WriterOnlyTableUpdate`,
which also covers the re-file, push child writer and decision-executor update cores), the chat delete (`DataverseDeleteRecordHandlerTests`), the field-mapping push
(`FieldMappingPushAuthorizationContractTests`) and the work-product persister (`WorkProductEnvelopePersistenceContractTests`).
`WriterOnlyTablesWritersGuardTests` now also names `InvoiceCorrectionService` and `InvoiceLineDecisionService` as writers, so an IL scan and a source scan prove they never
depend on the general create or a generic update path.

Tests that pinned the old, writable rule and were updated:
- `RecordTableRulesTests`, `WriterOnlyTablesUpdateGuardTests`, `RecordTableRulesRegistryGuardTests`: the flagged set is the three tables plus the correction.
- `RecordRepairPassJobTests`: the correction is in the excluded tables.
- `ChatServerWrittenTableBlockTests.CreateRecord_AServerWrittenInvoiceFamilyTable_KeepsItsOwnRefusal`: it asserted `IsCreatedOnlyByItsWriter` false for every family table; now true for the correction only, and the message assertion (the invoice-family words) is unchanged.
- `InvoiceFamilyGenericUpdatePathTests` (all partials), `SecureChildOwnershipAiToolTests.InvoiceFamily*` and `RecordCreationServiceInvoiceChildRuleSeamTests`: the correction rows were removed from the generic-path family-table lists (these prove the invoice-family
  guard on paths that now refuse the correction first); the guard's own unit tests still cover the table. One test (derived pair on a core move without a pair) was retargeted from the correction to the approval.

## Known limits

- Update and delete refusals read the one flag, so there is no way to open one of the three paths for the table alone.
- A new direct writer of correction rows must send them through `IGenericEntityService.ExecuteTransactionAsync` (or another typed path) and be added to `WriterOnlyTablesWritersGuardTests`.
- The guard's IL scan does not follow a dependency reached only through another type the writer calls (the same limit as for the other writers).
- Existing rows are unchanged; only future generic writes are refused.
- Once #1911 lands and repair can write invoice-family rows, a correction row created outside the product stays excluded from the repair pass (it is never swept).

## Review round 1

| Finding | Change |
|---|---|
| Repair-pass rationale (notes) | Rewritten: repair already refused every invoice-family write (fail closed, `RecordCreationService.Repair.cs:225`, `:936-948`, TODO #1911), so it never wrote a correction row; secure-family ownership stays covered by `SecureChildLineage.cs:306` and the `SecureChildReconciler` (`SecureChildOrdinaryParentMoveTests.cs:241-255`). New known limit added for the post-#1911 case. |
| Config claim (notes) | Corrected: the config files and `scripts/ebilling/*.ps1` do reference the table (for provisioning); none writes rows. No role or privilege detail given. |
| Guard messages | `WriterOnlyTablesWritersGuardTests`: failure messages, the first display name, and the class summary and remarks use generic "writer-only table" wording and name the two correction writers. |
| Re-file core in the guard's list | `Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite` added to `GeneralUpdate`. No writer in `Services/Signals` or `Services/Finance` references it, so nothing legitimate is flagged. |
| Pair-agreement theory data | `GenericPathFamilyTables` in `InvoiceFamilyGenericUpdatePathTests.PairAgreement.cs` is a hard-coded array of the three tables, not derived from `RefuseWriterOnlyTableUpdate`. |
| Test names | The six tests named "ApprovalOrCorrection" now name the approval only (seam tests, two `InvoiceFamilyGenericUpdatePathTests.FixedAfterCreate` tests, the chat-update `InvoiceFamilyFixedAfterCreate` test). Tests in `InvoiceFamilyCoreRecordGuardTests` and the derive tests still cover both tables and keep their names. |

## Part 2: native write hardening (EB-D-249)

Correction rows: native write hardening. The role source now agrees with part 1: correction rows are written only by their writers.

### Files changed

- One role file under `src/dataverse/solutions/SpaarkeMaster/Roles/` (entries for `sprk_invoicecorrection` only; no other table touched).
- `tests/Spaarke.ArchTests/FeatureRolePrivilegeGuardTests.cs`: the new guard section (from line 1508).
- `config/feature-role-privileges.json`: carries the change as `removals` entries under gap `ISS028` (see "Follow-up" below).
- `config/secure-record-owner-role.json` and `config/external-collaborator-role.json`: checked, unchanged.

### Identity of the writers (verified before the edit)

Both writers send correction rows through `IGenericEntityService.ExecuteTransactionAsync`
(`InvoiceCorrectionService.cs:938`, `:960`, row built at `:1436`; `InvoiceLineDecisionService.cs:438`, `:790`, row built at `:1294`).
`IGenericEntityService` is the singleton `DataverseServiceClientImpl` (`Infrastructure/DI/GraphModule.cs:145`), whose `ServiceClient`
is built with the BFF's own application credential (`DataverseServiceClientImpl.cs:99-171`); `ExecuteTransactionAsync`
(`DataverseServiceClientImpl.cs:3207-3214`) executes on that client, and the class sets no caller id. No write of a correction row runs
as the calling user; the generic create, update and delete paths refuse the table (part 1).

### Guard

`FeatureRolePrivilegeGuardTests`, section "Correction rows: native write hardening". The writer-only tables are read from the registry
(`RecordTableRules.All` where `CreatedOnlyByItsWriter`), which the ArchTests project already references, so a table added to the
registry is covered without editing the test. The guard checks every person role file in every `Roles` folder under
`src/dataverse/solutions` (`PersonRoles`, the same set the existing must-not-hold rules read; the repo has no packed or bundled copy of
the roles) and every ruled change in `config/feature-role-privileges.json`. The writer's own role (`ExpectedAppUsersRoles`) is exempt.
Findings outside a ratchet baseline fail; a baseline entry no longer found, or found at another depth, fails (the ratchet matches
role, privilege and depth). The baseline holds four entries for other writer-only tables that predate this rule, all
owner-ruled, ontology D-160/D-162, reaffirmed 2026-11-05 (designated cleanup role); the Budget Revision entries also cite D-204.

Controls:
- `WriterOnlyNativeWriteControls`: must-fire for each forbidden verb in any casing on a synthetic role and for a synthetic ruled change;
  must-not-fire for the permitted verbs, for another table and for the writer's role; ratchet controls (listed, unlisted, stale).
- `WriterOnlyNativeWriteFiresOnSeededRoleFiles`: on a copy of the role folder, a synthetic role document granting a forbidden verb fails,
  the writer's role holding the same does not, and dropping a baseline entry's privilege makes it stale.
- `WriterOnlyTablesHaveNoNativeWritesOutsideTheBaseline`: the repo as it is.

### Tests

- Before (the role file at `3d1efbcd30`): the real-repo test and the seeded test fail (5 findings, all on `sprk_invoicecorrection`).
- After: `Spaarke.ArchTests` filter `FeatureRolePrivilege|WriterOnly|Role`: 365 passed, 0 failed.
  `Sprk.Bff.Api.Tests` filter `InvoiceCorrection|InvoiceLineDecision|WriterOnly`: 682 passed, 0 failed.

### Rollout

Dev gets the change by a direct scoped edit after merge (EB-D-250). Other environments get it through the SpaarkeMaster import.

### Follow-up (same task)

- `config/feature-role-privileges.json`: the correction-row write entries are in the `removals` block under gap `ISS028`, so
  `scripts/Set-FeatureRolePrivileges.ps1 -Remove -Gap ISS028` applies exactly this change (dry run first, then `-Apply`, then
  `-Remove -Verify -Gap ISS028`); `-Restore -Gap ISS028` is its rollback.
- `scripts/ebilling/Deploy-EbillingRolePrivileges.ps1` (D-90 mirror) no longer lists `sprk_invoicecorrection` as a working table, so a
  re-run cannot put the entries back. Pinned by `tests/scripts/Deploy-EbillingRolePrivileges.Tests.ps1` (Pester 5.7.1).
- The new guard reads the config's `changes` only; a `removals` entry is must-not-fire (`WriterOnlyNativeWriteIgnoresConfigRemovals`).
  `RemovalsAreAbsentFromTheRoleFilesAndForbidden` counts 86 removals and takes the writer-only guard as the coverage of the ISS028 ones.
- `NewestExpectationsFileCarriesTheRuledSet` leaves gap `ISS028` out (`PendingNextReleaseRemovalGaps`); `PendingRemovalGapsAreARatchet`
  fails once the newest expectations file carries an entry of a pending gap, so the gap then leaves the list.
- Next release's role-privilege expectations must list the five removed entries (managed stage-and-upgrade): the correction-row write entries.
  `PendingNextReleaseRemovalGaps` records the expectations version gap `ISS028` was raised against (1.3.1.0); any newer expectations
  file that does not carry the gap's entries fails the guard.
- Intended, owner confirmation pending: after this change an ordinary user's native delete of an invoice that has correction rows (and
  of a core record or invoice line that correction rows reference) is refused; invoices end by status, not by delete. To be tried
  during the dev apply.

## Dev apply (EB-D-250), 2026-11-17

- Before notice to uac-r2, with the dry-run plan, the live cascades and the rollback; uac-r2 confirmed that they match its review.
- Live cascades onto `sprk_invoicecorrection`:
  - invoice: Delete=Cascade;
  - invoice line, matter, project, work assignment and record type: Delete=RemoveLink;
  - Assign=NoCascade on all of them.
- `scripts/Set-FeatureRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Remove -Gap ISS028 -Apply` removed 5 entries and read each back absent on the root role and every business-unit copy. `-Verify` passed.
- Read-back on all 6 Spaarke Core User roles: correction rows hold Read, Append and AppendTo at Deep, and nothing else.
- After notice sent to uac-r2, which re-probes.
- Rollback: `-Restore -Gap ISS028 -Apply`.
- Other environments get the change with the SpaarkeMaster import (1.3.2.0 expectations at the master merge).
