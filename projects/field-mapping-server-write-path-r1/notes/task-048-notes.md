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
answers the same for any record, payload, identity and write mode. Two wordings of the one refusal (fix round 1; the code is the same): `ChangedOnlyByItsWriterMessage` is a plain sentence for every
path a person reads it on, and the chat update tool keeps the model-facing text, as the create side splits them:

> '{table}' records are changed only by Spaarke itself and can't be changed by this update; nothing was written.
> (record_update.changed_only_by_its_writer)

> (chat only, `DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage`) '{table}' records can't be changed from chat — this
> update was REJECTED and nothing was written. ... Do NOT retry this update — ... (record_update.changed_only_by_its_writer).

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
| Browser re-file core (`ChildRecordEndpoints.UpdateAsync`; four callers: the child-records, event and communication filing routes and `DecisionRouteCores.UpdateChildAsync`) | Yes (fix round 1) | Itself, after the core-pair check, before the metadata read | 403 problem, reason code `child_record.denied`, detail = the message |
| Re-file core (`OwnedChildWrite.RefileAsync`) | Yes (fix round 1) | Itself, after the core-pair check, before the invoice-family read | `RefileOutcome.Invalid` = the message |
| `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` (push child writer) | Yes (fix round 1) | Itself, first, before any child is read | Every child counted failed, one `PushFieldMappingsError` per child carrying the message; nothing written |
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

- K4 (ordering, three answers): on a Signal Update Record node the writer-only refusal follows steps that can answer first. A
  `type:"string"` mapping against a Choice column with an unmatchable value throws `FieldCoercionException`; a lookup whose target
  entity set cannot be resolved throws `InvalidOperationException`; and the table metadata read for a `type:"string"` mapping can fail
  (no row is read or written by any of them). Fix round 1 tried to move the check ahead of the coercion and metadata steps and
  could not: the later shape refusals are asked on the built payload, and on a Signal their order is pinned (the app-only
  access-column refusal first, then the core record pair: `AccessTableAppOnlyWriterTests`, `CoreRecordPairUpdateWriterTests`), so a
  table-only check ahead of the payload build would answer first for payloads those tests pin to another code. All three still
  refuse or fail with nothing written; the writer-only code is the answer only when none of them applies.
- K1 (guard reach): the guards recognise a generic update path only through `UpdateRecordFieldsAsync`,
  `UpdateExistingRecordFieldsAsync` and `IDataverseUserClient.PatchAsync` (what `CoreRecordPairUpdateCheckGuardTests` scans, and
  this guard follows). A future generic path written on `IGenericEntityService.UpdateAsync`/`BulkUpdateAsync` or
  `DataverseWebApiClient.UpdateAsync` would not be seen by either. Fix round 1 removed the exemption list: the three shared cores
  now ask the check themselves.
- K4: on a Signal or Decision Record item that names the core record pair, the chat tool's `ValidateChat` answers with the
  writer-only message while `ExecuteChatAsync` (reached only past validation, or by a direct call) answers with the core-pair code
  first, as #2568 pinned. Both refuse with nothing written.
- K3: `ChatUpdate_ATableOnlyItsWriterChanges_TheRefusalIsTheSameForAnyRecord` already passed for one table before the change (the
  test double answered existing and missing rows alike there); the must-fire tests carry the proof.
- Scope note: the work-product persister is not one of the POML's named tools; it is included because it is a configured,
  table-generic update that asks the core-pair check, and the guard requires every such path to decide. Remove it if the owner
  scopes it out.
- Out of scope: a client surface writing these tables through `Xrm.WebApi` is not a BFF path and is not covered here.


## Review note for the PR (owner decision)

Three #2568 tests asserted "another column of a Signal or Decision Record is written" (the chat tool, the Update Record core and the
output mapping). By the owner's decision of 2026-11-04 they now assert the opposite outcome, the task 048 refusal (renamed
`..._IsNotTheCorePairRefusal`). Their core-pair refusal assertions are unchanged. This goes in the PR description too.
Fix round 1 inverts a fourth, found by the second filter: `CoreRecordPairUpdateWriterTests.Push_ARuleOntoTheCorePair_IsSkipped_AndNeverWritten`
drove the push's child writer directly and expected the other rule written with only the core-pair rule skipped. The writer now
refuses a Signal or Decision Record whole (every child failed with the plain message, nothing written), so it is renamed
`Push_ARuleOntoTheCorePair_OfASignalOrDecisionRecord_IsRefusedWithTheWholeTable_AndNeverWritten`; the per-rule core-pair skip is
still in the writer but no registered table that carries the pair can reach it now.

## Fix round 1 (review: no F; K items fixed in scope)

1. **Exemptions relied on callers (K1).** `ChildRecordEndpoints.UpdateAsync`, `OwnedChildWrite.RefileAsync` and
   `FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync` now call `RecordTableRules.RefuseWriterOnlyTableUpdate` themselves, before
   any read or write (after the core-pair check, so that order is unchanged), each in its own refusal shape (table above).
   `WriterOnlyTablesUpdateGuardTests.FixedTableUpdatePaths` and its stale-entry checks are gone, and the three methods are pinned by
   name with the other update paths. `ChildRecordEndpoints.UpdateAsync` has four callers (the three routes and
   `DecisionRouteCores.UpdateChildAsync`); the core now decides for all of them. `RefileAsync` was in the same list and is covered
   the same way, since dropping the list needs it. The test `TheBrowserRefileTables_IncludeNoWriterOnlyTable` is removed: it only
   existed to excuse the exemption.
   - Must fire: `ChildRecordUpdateCore_…IsRefused_BeforeAnyReadOrWrite` (3 tables), `DecisionRouteUpdate_…IsRefused…` (3),
     `RefileCore_…IsRefused…` (3), `PushChildWriter_…FailsEveryChild…` (3).
   - Must not fire: `DecisionRouteUpdate_ATodo_IsStillWritten`, `ChildRecordUpdateCore_AnOrdinaryTable_IsStillWritten`, and the
     existing decision `sprk_todo` update tests (`DecisionActionExecutorsTests`).
2. **Wording.** The model-facing text ("REJECTED ... Do NOT retry ...") now exists only as
   `DataverseUpdateRecordHandler.ChangedOnlyByItsWriterChatMessage`, used by the chat tool's validate and execute. Every other path
   answers `RecordTableRules.ChangedOnlyByItsWriterMessage`: "'{table}' records are changed only by Spaarke itself and can't be
   changed by this update; nothing was written. (record_update.changed_only_by_its_writer)". Code unchanged. The guard test pins the
   plain text (no "Do NOT") and the chat text (has it); the chat tests assert the chat message, the others the plain one.
3. **Node ordering.** Not movable without changing pinned Signal orders; the known-limit text now names all three answers (see K4).
4. **Notes.** K1 guard reach and the #2568 inversion are recorded above.
5. **Found in passing (filed here, not fixed).** `src/client/pcf/UpdateRelatedButton/UpdateRelatedButtonApp.tsx:174` shows
   `detail || title`, so every push 400 (`Results.ValidationProblem`, which carries `errors`, no `detail`) shows "One or more
   validation errors occurred." The 400 for a writer-only target (`errors.targetEntity`) is therefore not readable in that button.

rework: none task 048

### Fix round 1 verification (code commit cfb7e0cfab)

- `verify-suites.sh`: solution build 0 warnings / 0 errors; ArchTests 1936 / 1936 passed (one fewer: the refile-tables test is gone);
  filtered Sprk.Bff.Api.Tests 6813 passed, 7 skipped, 0 failed.
- Second filter (Signals, Update Record node and core, `DataverseUpdate`, `ActionSeam`, decision commit and actions, Signal and
  Decision Record writers, `WriterOnly`, work-product envelope, field-mapping push): 4415 passed, 2 skipped, 0 failed. It caught the
  fourth inverted #2568 test (see above), which the script's filter does not reach.
- Failing first: with `ChildRecordEndpoints`, `OwnedChildWrite` and `FieldMappingEndpoints` at their previous content, 18 tests fail
  (12 new `048 fix 1` must-fire tests and the 6 push-pair tests); the must-not-fire tests pass either way.
- Publish delta (Release zip) against fresh origin/master d9bef8e90f: 41,108,226 -> 41,109,003 bytes, +777 bytes, 191 files both
  sides, 39.2 MB. CVE: `dotnet list package --vulnerable --include-transitive` reports none for Sprk.Bff.Api.

## Fix round 2 (chat delete honours D-204)

Owner decision 2026-11-05 (UAC-r2 and ontology agree): the general delete path honours D-204 for `sprk_signal`,
`sprk_decisionrecord` and `sprk_budgetrevision`, the same registry flag (`RecordTableRule.CreatedOnlyByItsWriter`) the create and
update refusals read. Ontology confirms none of their code removes these rows.

- **Helper.** `RecordTableRules.RefuseWriterOnlyTableDelete(table)` (table only, trimmed and case-insensitive, reads the flag) returns
  `RecordOwnerResolution.Refused("record_delete.changed_only_by_its_writer", plain message)` or null. Plain message:
  "'{table}' records are changed only by Spaarke itself and can't be removed by this operation; nothing was removed.
  (record_delete.changed_only_by_its_writer)".
- **Chat delete tool** (`DataverseDeleteRecordHandler`): asks it in `ValidateChat` (so the gate shows the refusal and never a confirm
  dialog) and in `ExecuteChatAsync` right after the arguments are parsed, before the approval check and before any Dataverse call,
  for any record id, with or without `hasUserApproved`. The tool has one identity (the caller's own client), so there is no separate
  system-principal branch. Its model-facing wording is `DataverseDeleteRecordHandler.ChangedOnlyByItsWriterChatMessage` (same code),
  as the update tool's.
- **Inventory of every other path that deletes a Dataverse row** (scan of `IDataverseUserClient.DeleteAsync`,
  `IGenericEntityService.DeleteAsync` and `DataverseWebApiClient.DeleteAsync`; pinned by `WriterOnlyTablesDeleteGuardTests`):
  - No playbook node executor, no action-seam step and no output-mapping handler deletes a row (searched `Services/Ai/Nodes`, the
    action core, `DataverseUpdateHandler`; the node executor types have no delete kind).
  - The only generic delete whose table a caller chooses is the chat tool above.
  - Reasoned exclusions (15 members, each with its reason in `FixedTableDeletes`): compensations and routes whose table is a constant in
    code (analysis promote, report create and delete, the two SPE admin routes, workspace layout, stored files, the membership
    junction, the finance rate-card, rule-set and invoice-submission rollbacks and the submission commit gate), and the
    isolated-create completion, which removes only the work assignment or project it was handed after creating it (its create path
    already refuses the three tables). The external-access delete-own route is a fixed enum (document, to-do, event, service
    request).
  - The guard is a closed list with stale-entry checks; a new member that deletes through the three APIs fails it until it asks the
    check or is listed with its reason. Limit: a delete on an API outside those three (the Dataverse SDK client directly, a raw HTTP
    call) would not be seen.
- **Tests.** Must fire (`DataverseDeleteRecordHandlerTests`, `048 fix 2`): each of the three tables, upper case, approved and not,
  refused on validate and on execute with no Dataverse call. Must not fire: an ordinary table still validates, and the existing
  `ExecuteChatAsync_ValidDelete_DeletesViaResolvedEntitySet` still deletes through the user's client. Guard
  (`WriterOnlyTablesDeleteGuardTests`): the delete refusal equals the flag table by table and agrees with the update and create
  refusals, its body reads the flag, both chat members ask it, and the controls (must fire: a delete through the entity service and
  through the Web API client without the check; must not fire: a member that asks it, or is listed).

### Fix round 2 verification (code commit 611484f57c)

- `verify-suites.sh`: solution build 0 warnings / 0 errors; ArchTests 1939 / 1939 passed (+3: the delete guard); filtered
  Sprk.Bff.Api.Tests 6813 passed, 7 skipped, 0 failed (it includes `DataverseDeleteRecordHandlerTests`, 28 tests).
- Second filter (as round 1, plus `DataverseDelete`): 4443 passed, 2 skipped, 0 failed.
- Failing first: with the two checks in `DataverseDeleteRecordHandler` neutralised (the helper kept), 12 of the 28 delete tool tests fail
  (the 6 validate and 6 execute must-fire rows); the must-not-fire tests pass either way.
- Publish delta (Release zip) against fresh origin/master d9bef8e90f: +1,553 bytes, 39.21 MB total. CVE: none for Sprk.Bff.Api.
