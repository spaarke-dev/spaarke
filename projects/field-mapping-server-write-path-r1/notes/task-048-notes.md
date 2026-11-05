# Task 048 — Signal, Decision Record and Budget Revision rows are changed only by their writers

Owner decision 2026-11-04 (notes/decisions.md, "The generic update tools do not change Signal, Decision Record or Budget Revision
rows"; D-204 extended from create to update). Branch `task/fm-048` from `origin/work/field-mapping-server-write-path-r1` at
b9a5a9cbe0 (contains 042c's merge 79d7360fb9 and #2568 909754869f).

## Ontology's answer (2026-11-04), recorded before coding the output mapping

- **Output mapping is included.** `DataverseUpdateHandler` is the playbook output mapping; its only consumer is
  `OutputOrchestratorService`. No output-mapping configuration or seed JSON names these tables, and 0 of 93 dev playbook nodes do.
- **Writers to leave alone.** These tables are written only by: `SignalWriter` (create, lifecycle, Decision Record link);
  `DecisionRecordWriter`; the Revise Budget writer in `DecisionActionExecutors` (runs as the writer identity);
  `SignalEvaluationService`'s upsert. None of them goes through `RecordCreationService`, chat `update_record`, the Update Record
  node, output mapping or the field-mapping push.
- **Refusal scope.** Refuse update and upsert alike, before any read or write, for the caller and the system principal, with the
  same `CreatedOnlyByItsWriter` registry read as 042c.
- **Must-not-fire.** The writers' own updates still pass: a Signal close and its Decision Record link, a decision commit, Revise
  Budget.

## The one rule

`RecordTableRules.RefuseWriterOnlyTableUpdate(table)` reads `RecordTableRule.CreatedOnlyByItsWriter` (042c's flag) and returns
`RecordOwnerResolution.Refused("record_update.changed_only_by_its_writer", message)` or null. Table only: it reads nothing, so it
answers the same for any record, payload, identity and write mode. One message (`ChangedOnlyByItsWriterMessage`), modelled on the
chat create tool's message for the same tables:

> '{table}' records can't be changed by this update — it was REJECTED and nothing was written. They are changed only by Spaarke
> itself: a signal by its evaluation and its lifecycle, a decision record or a budget revision when a decision is committed. Do
> NOT retry this update — {table} is blocked on every general update unconditionally and no argument change makes it valid; work
> from the signal or its decision instead (record_update.changed_only_by_its_writer).

No existing refusal's kind, code or wording changes (NFR-07). Each path asks it after its existing shape-only refusals (the
access-column and core-record-pair checks keep answering first where they did, so their pinned order is unchanged) and before any
row is read or written.

## Path inventory

| Path | Covered | Where | Refusal shape |
|---|---|---|---|
| Chat `dataverse.update_record` (`DataverseUpdateRecordHandler`, writes via `IDataverseUserClient.PatchAsync`) | Yes | `ValidateChat` (before the confirm dialog) and `ExecuteChatAsync` (before the entity-set read) | Tool validation failure / tool error, code `record_update.changed_only_by_its_writer` |
| Update Record node (`UpdateRecordNodeExecutor` → `UpdateRecordActionCore`) | Yes | `UpdateRecordActionCore.UpdateAsync`, after the shape refusals, before any row read or write | `RecordOwnerUnresolvedException`; the node maps the code to `ValidationFailed` (as the core-pair refusal) |
| Action seam (`ActionSeam` → `UpdateRecordActionCore`, confirmed proposal as its user) | Yes | Same core, every payload source and identity | `UpdateRecordResult` failure carrying the message (ActionSeam's existing mapping) |
| Playbook output mapping (`DataverseUpdateHandler`, only consumer `OutputOrchestratorService`) | Yes (ontology: included) | `UpdateAsync`, after the shape refusals, before the invoice-family read, the row-version read and the write; app-only and as the run's user | `RecordOwnerUnresolvedException` (as the core-pair refusal) |
| Field-mapping push (`POST /api/v1/field-mappings/push`) | Yes | `ValidatePushRequest`: before the source is authorized and before any profile, row or child is read | 400 validation problem, `errors.targetEntity` = the message |
| Work-product persister (`TopicRegistryWorkProductPersister`, a configured column of the session's host record, user-OBO PATCH) | Yes | `PersistAsync`, after the registry mapping, before the host table is resolved or written | `InvalidOperationException` "registry row … targets {table}.{field}, which is not written: {message} Fix the registry row." (as its core-pair refusal) |
| Browser re-file core (`ChildRecordEndpoints.UpdateAsync`) | Excluded (reasoned) | — | Its routes pass `RefileTables` (todo, memo, invoice, report card, analysis), an event or a communication only; pinned by `WriterOnlyTablesUpdateGuardTests.TheBrowserRefileTables_IncludeNoWriterOnlyTable` |
| Re-file core (`OwnedChildWrite.RefileAsync`) | Excluded (reasoned) | — | Called by the chat tool after it refuses the three tables, and by `ChildRecordEndpoints.UpdateAsync` (fixed tables) |
| `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` | Excluded (reasoned) | — | Its one caller, `PushFieldMappingsAsync`, refuses the target in `ValidatePushRequest` first |
| Fixed-table writers (`FinanceRollupService`, `ScorecardCalculatorService`, the ontology admin services, the decision action executors, …) | Not generic | — | Listed in `CoreRecordPairUpdateCheckGuardTests.FixedPayloadWriters` with their fixed table or columns |
| The general create (`RecordCreationService`, chat create) | 042c | — | Already refuses the three tables |

Upsert: after #2568 the generic paths write update-only (`UpdateExistingRecordFieldsAsync`, `If-Match: *`); the refusal is on the
table, so it holds for any write mode.

**Escalation trigger check:** no first-party flow updates one of the three tables through a generic path. Output mapping: ontology
census (0 of 93 nodes; no seed JSON). Field-mapping push: no profile in the repo targets them (repo grep of `infra/`, `config/`,
`scripts/`); a push requires a `sprk_fieldmappingprofile` row naming the target, which this task did not query on dev (no
Dataverse access in this task). Work-product persister: no `sprk_aitopicregistry` seed in the repo names them; dev rows not
queried (same reason). The writers never reach any generic path (guard below).

## Guards and tests

- `tests/Spaarke.ArchTests/WriterOnlyTablesUpdateGuardTests.cs` (IL, per source member): every member that asks
  `RefuseFixedAfterCreateColumns` (the generic update paths, already forced by `CoreRecordPairUpdateCheckGuardTests`) also asks
  `RefuseWriterOnlyTableUpdate`, or is listed with its reason (closed list, stale-entry checks); the six update tools are pinned by
  name (non-vacuity); the refusal equals the registry flag table by table and agrees with the chat create tool's check, and its
  compiled body reads `CreatedOnlyByItsWriter` (one list). Controls: must fire (a member asking only the core-pair check; a
  hand-written table list) and must not fire (both checks, the second in an async local function; the flag read).
- `tests/Spaarke.ArchTests/WriterOnlyTablesWritersGuardTests.cs` (extended): `SignalWriter`, `DecisionRecordWriter`,
  `ReviseBudgetExecutor`, the writer identity and `DecisionCommitService` never depend on a generic update path (IL), with a
  must-fire / must-not-fire control.
- Behaviour (KEEP paths): `tests/integration/data-mutation/Signals/WriterOnlyTablesUpdateRefusalTests.cs` (node, core for every
  payload source and identity, output mapping app-only and as the run's user with both concurrency modes, any record id; must-not-fire
  on `sprk_event`); `SecureChildOwnershipAiToolTests.WriterOnlyTableUpdate.cs` (chat validate and execute, any case, any record;
  must-not-fire on `sprk_todo`); `FieldMappingPushAuthorizationContractTests.Push_ToATableOnlyItsWriterChanges_Is400_AndNothingIsAsked`;
  `WorkProductEnvelopePersistenceContractTests.PersistAsync_ARegistryRowTargetingATableOnlyItsWriterChanges_IsRefused_AndNothingIsWritten`.
- Writers' must-not-fire (existing behaviour tests, unchanged and passing): Revise Budget
  (`SecureChildOwnershipAiToolTests.DecisionReviseBudget_StillRecordsTheRevision_ThroughTheWriter_NotTheGeneralCreate`), decision
  commit (`DecisionCommitServiceTests`, `DecisionCommitFollowUpsTests`), Signal close and Decision Record link (`SignalWriterTests`,
  `SignalWriterTests.AutomatedClose`, `DecisionRecordWriterTests`, `SignalWriterSeamTests`). They write through the writer client,
  not a generic path, so the refusal cannot reach them.
- Three #2568 controls asserted "another column of a Signal or Decision Record is written" through the chat tool, the Update Record
  core and the output mapping. The owner's decision inverts that: they now assert the task 048 refusal (renamed
  `…_IsNotTheCorePairRefusal`). The core-pair refusal assertions themselves are unchanged.

Failing first: with the six path files at HEAD (registry members kept so the tests compile) 38 of the 44 new or converted
playbook/push/persister tests failed (the 6 passing are the must-not-fire controls), 8 of the 10 chat tests failed (the
must-not-fire and one "same for any record" row pass either way), and the update guard's main test failed; with the change all pass.

## Verification (code commit 80440d5238)

- `verify-suites.sh`: solution build 0 warnings / 0 errors; ArchTests 1937 / 1937 passed; filtered Sprk.Bff.Api.Tests 6802 passed,
  7 skipped, 0 failed. Extra filter for the `Signals` namespace, node, output mapping, action seam, decision commit and Signal
  writer tests (outside the script's filter): 5327 passed, 2 skipped, 0 failed.
- Publish delta (`publish-delta.ps1`, Release zip) against a fresh build of origin/master d9bef8e90f: 41,220,842 → 41,221,323 bytes,
  +481 bytes, 192 files both sides, 39.31 MB total.
- CVE: `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages for Sprk.Bff.Api.
- Placement (CLAUDE.md §10): no new endpoint, service, DI registration or package. One internal static method, one constant and one
  message added to the existing `RecordTableRules` registry; five existing update paths call it.
- code-review: 0 F-class findings; K-class findings are listed below. adr-check: 0 violations, 0 warnings (ADR-002, 010, 013, 015,
  038, 039, 041 compliant; 001, 008, 028 not applicable).

## Known limits

- K4: the Update Record core resolves table metadata (column types for a `type:"string"` mapping, a lookup target's entity set)
  before its shape refusals, as it did for the core-pair refusal; the writer-only refusal follows them, so for such a payload a
  metadata read can precede it. No row is read or written. Moving it first would change the pinned order of the access-column
  refusal on a Signal (`AccessTableAppOnlyWriterTests`).
- K1: `ApplyMappingsToChildRecordsAsync` (internal) relies on its one caller's request check; a second caller added later would
  not be caught by the guard (the listed reason names the one caller).
- K4: on a Signal or Decision Record item that names the core record pair, the chat tool's `ValidateChat` answers with the
  writer-only message while `ExecuteChatAsync` (reached only past validation, or by a direct call) answers with the core-pair code
  first, as #2568 pinned. Both refuse with nothing written.
- K3: `ChatUpdate_ATableOnlyItsWriterChanges_TheRefusalIsTheSameForAnyRecord` already passed for one table before the change (the
  test double answered existing and missing rows alike there); the must-fire tests carry the proof.
- Scope note: the work-product persister is not one of the POML's named tools; it is included because it is a configured,
  table-generic update that asks the core-pair check, and the guard requires every such path to decide. Remove it if the owner
  scopes it out.
- Out of scope: a client surface writing these tables through `Xrm.WebApi` is not a BFF path and is not covered here.

rework: none task 048
