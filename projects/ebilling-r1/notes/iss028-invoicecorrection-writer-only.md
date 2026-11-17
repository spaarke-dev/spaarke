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
