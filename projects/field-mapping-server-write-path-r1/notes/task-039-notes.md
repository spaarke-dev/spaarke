# Task 039 notes: server-owned search index (FR-38, #2399)

Branch `task/fm-039`, started from `f3040d9667` (master `d0c1c5dc3c` is the publish-delta baseline). Rigor FULL.

## What changed

| Part | Where | Behaviour now |
|---|---|---|
| A | `RecordTableRules.cs` | `SearchIndexed = true` on `sprk_workassignment`, `sprk_event`, `sprk_invoice` (beside matter, project, document). Repair's `RepairSearchIndexAsync`, `RecordTableRulesTests`, the invoice-family drift test and the arch guards still hold (see "Changed existing assertions"). |
| B | `RecordCreationService.cs` step 2 and after step 3½, helpers on `RecordTableRules` | On a search-indexed table, BOTH columns are dropped from the payload whatever their origin: caller item or WebApi body (before the mapper, so a malformed bind cannot refuse), field-mapping value (`mappedValues`), adapter post-mapping default (`defaultColumns`). Every spelling: `sprk_AI_Search_Index@odata.bind`, `sprk_ai_search_index@odata.bind` (case-insensitive), a `MappedItem.Lookups` entry, a null bind, and the legacy text column `sprk_searchindexname`. Mapped-value and default warnings that name either column are removed. It is a drop, never a refusal: step 2's blocked-column refusal does not fire. K3 is kept (one canonical key, `PutServerValue`). |
| C | `RecordCreationService.SearchIndexDefaultAsync` | Step 8 is the only writer. It reads the business unit of the FINAL owning team (`TeamBusinessUnitAsync(owningTeam)`, the Secure team included) and writes that unit's two values or nothing. The fill-only checks are gone. A unit without an index sends no column and logs `search_index_unset_for_owner_bu` (Information; table, owning team, unit, which column is unset). Never the caller's unit, never a walk to the root, never a fallback. The read-failure path (warning plus `Failed` state) is unchanged. |
| D | `RecordTableRules.LogSearchIndexDropped` | `client_search_index_dropped` (Warning, shape of 018's `default_value_dropped`) when the dropped value differs from the applied one (equal = both none, same id, or same name trimmed and ordinal-ignore-case). Fields: table, record id (logged at step 12 on create, once the id is known), column, origin (`caller`/`mapping`/`default`), dropped and applied index id; the name column carries dropped and applied index NAME instead. On an update the applied value is `unchanged`. No person name, no email, no record name. |
| E | one shared helper block on `RecordTableRules` (`OwnsSearchIndex`, `SearchIndexColumnOf`, `WithoutSearchIndex` x2, `RemoveSearchIndex`, `DroppedSearchIndexValue`, `LogSearchIndexDropped`) | Every update path that can write either column on the six tables drops it and logs it (inventory below). No new file under `src/`, no DI registration, no interface (ADR-010). |

## Update-path inventory (E)

Searched: `DataverseUpdateRecordHandler`, `ChildRecordEndpoints` (incl. `PATCH /api/v1/child-records/sprk_project/{id}`), `FieldMappingEndpoints` push, `EventEndpoints`, `CommunicationEndpoints`, then every `UpdateRecordFieldsAsync` / `UpdateExistingRecordFieldsAsync` / `PatchAsync` / `UpdateAsync` caller, and every `src/` mention of `sprk_ai_search_index` / `sprk_searchindexname`.

| # | Path | Tables (of the six) | Verdict | Evidence |
|---|---|---|---|---|
| 1 | `PATCH /api/v1/child-records/{table}/{id}` (`ChildRecordEndpoints.UpdateAsync`); also `DecisionRouteCores.UpdateChildAsync`, which calls it | `sprk_invoice` (RefileTables also lists todo, memo, reportcard, analysis: not indexed) | dropped here | `ChildRecordEndpoints.cs:608-619` |
| 2 | `PATCH /api/v1/child-records/sprk_project/{id}` (`FileProjectAsync`) | `sprk_project` | cannot write the column: the body may name only `sprk_regardingrecordid` and `sprk_regardingrecordtype@odata.bind` | `ChildRecordEndpoints.cs:422` (`ProjectFilingShapeProblem`), `:470` |
| 3 | `PATCH /api/v1/events/{id}/filing`, `PATCH /api/communications/{id}/filing` (`filingOnly: true`) | `sprk_event` | already protected: filing columns only | `ChildRecordEndpoints.cs:136` (`FilingShapeProblem`), `:583`; `EventEndpoints.cs:269,284`; `CommunicationEndpoints.cs:84` |
| 4 | `PATCH /api/v1/events/{id}/due-assignee` | `sprk_event` | cannot write the column: fixed body | `Api/Events/EventDueAssigneeWrite.cs` |
| 5 | Chat `dataverse.update_record` (`DataverseUpdateRecordHandler`) | all six | dropped here | `DataverseUpdateRecordHandler.cs:230-241` |
| 6 | `PUT /api/v1/documents/{id}` (typed `UpdateDocumentRequest.SearchIndexName`) | `sprk_document` | dropped here | `DataverseDocumentsEndpoints.cs:151-165` |
| 7 | Playbook output mapping (`DataverseUpdateHandler.UpdateAsync`, via `OutputOrchestratorService`) | all six | dropped here (on a copy of the dictionary) | `DataverseUpdateHandler.cs:73-82` |
| 8 | Update Record node and ActionSeam / Job B apply (`UpdateRecordActionCore`) | all six | dropped here (origin `caller` for a confirmed proposal, else `mapping`) | `UpdateRecordActionCore.cs:177-190` |
| 9 | Field-mapping push (`FieldMappingEndpoints.ApplyMappingsToChildRecordsAsync`) | all six | dropped here: a rule aimed at either column is Skipped ("The search index is set by the server and cannot be copied."), plus a safety net on the final payload | `FieldMappingEndpoints.cs:1075-1100`, `:1103-1113` |
| 10 | Decision executors (`DecisionActionExecutors`: respond / extend / decline PATCHes on a work assignment) | `sprk_workassignment` | cannot write the column: fixed columns | `Services/Signals/Actions/DecisionActionExecutors.cs` (work-assignment PATCH builders) |
| 11 | `WorkProductRecordPersister` | registry-declared host column | cannot write an index: envelope JSON into an admin-registered column | `WorkProductRecordPersister.cs` |
| 12 | Server index stamps after indexing (`RagEndpoints`, `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer`) | `sprk_document` | not client-shaped and not through the service: they record WHERE a file was indexed, after the fact. Left as they are (out of scope, listed in "Out of scope") | typed `UpdateDocumentRequest`/entity writes |
| 13 | Any other server writer that sets an index through `RecordCreationService` | none | none found, so no escalation was needed | grep of `sprk_ai_search_index` and `sprk_searchindexname` in `src/`: the resolver, `RecordCreation`, the indexing stamps and the typed document requests only |

A body left with no column after the drop is answered as any empty update is (the mapper's 400 `'item' must contain at least one column`; the chat tool reports it): nothing is written, and the index is not.

## Changed existing assertions (old to new)

| File | Test | Old | New |
|---|---|---|---|
| `tests/integration/data-mutation/RecordOwnership/SearchIndexDefaultTests.cs` | `AnExplicitSearchIndex_IsKept_OnEverySearchIndexedTable` (x3) | a caller's explicit index wins (task 015 fill-only) | replaced by `AClientsSearchIndex_IsDropped_AndTheOwningUnitsIsWritten_OnEverySearchIndexedTable` (x6 tables) |
| same | `AnExplicitName_IsKept_AndOnlyTheUnsetLookupIsFilled` (x3) | name kept, lookup filled | replaced by `AnExplicitName_IsDropped_AndTheOwningUnitsNameAndIndexAreWritten` (x3) |
| same | `ATableNotMarkedSearchIndexed_ReceivesNoSearchIndexValue` | used `sprk_event` as the not-indexed table | uses `sprk_todo` (carries a lookup); also pins the caller's AppendTo check and the lookup being written |
| `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` | `SearchIndexed_IsMatterProjectAndDocumentOnly` | three tables | renamed `SearchIndexed_IsTheSixIndexedTablesOnly`, six tables |
| `tests/integration/seam/RecordCreation/RecordCreationServicePipelineSeamTests.cs` | `CreateAsync_AnEventFiledUnderAnOrdinaryMatter_RunsTheStepsInOrder_AndCreatesExactlyOnce`, `CreateAsync_AsTheSystem_SkipsTheCallerSteps_...` | step 8 `NoOp` | step 8 `Ran` (an event is search-indexed now) |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.cs` (fixture) | 11 tests (`..._ADefaultOnlyProfile_ReadsNoSource_...`, `..._WhenTheCallerReadOfTheSourceFails_...` x5, `..._WhenMappingFaultsAfterARuleWasSkipped...`, `..._WhenTheMappingConfigurationCannotBeRead_...`, `..._AMissingSourceAndOneTheCallerCannotRead_...`, `..._AHalfNamedMappingSource_...`, `..._AMappedLookupTheCallerCannotAppendTo_...`, `..._WithNoMappingAsked_...`) | step 8's business-unit reads went through the strict source reader and showed up as `source-read`, or failed with a read-failure warning | fixture answers the `team` and `businessunit` reads (a unit without an index) unjournaled; every assertion on those tests is unchanged |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.WorkAssignmentCreate.cs` | `WorkAssignmentCreate_TheWizardsPayloadUnderAnOrdinaryMatter_IsCreatedByTheApp_OwnedByTheMattersTeam_ForTheCaller` | the wizard's `sprk_searchindexname` is written as given | the column is absent (dropped; the owning unit has no index in this world) |
| `tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.cs` | `ATeamOwnedRow_KeepsItsOwner_EvenOneTheRuleWouldNotGive`, `ATemplateThatYieldsABlankString_...`, `AConcatThatYieldsOnlyASpace_...`, `AMappingFault_MapsNothing_AndTheOwnerIsStillRepaired` (x2) | an event row carried no index, so Repair now (correctly) filled it and these found an extra write | the helper `EventUnderTheMatter` takes `indexed: true` and these tests pass it, so they stay about owner and mapping; Repair's own index tests (`TheSearchIndexDefault_IsFilledOnlyWhereUnset`, ...) are untouched |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs` (scripted caller) | n/a (harness) | no `sprk_ai_search_index` lookup on the six tables, no `sprk_aisearchindexes` set | declared (live navigation property `sprk_AI_Search_Index`), as the live schema has it |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/EntitySetNameFixtures.cs` | n/a (harness) | no pinned set for `sprk_aisearchindex` | `sprk_aisearchindexes` |

No KEEP-path test was deleted without a replacement.

## New tests (all KEEP paths; none new under `tests/unit/Sprk.Bff.Api.Tests/**`)

- `SearchIndexDefaultTests.cs`: #2399 repro (a work assignment from a general-unit caller binding the user's unit index, a catalog row the caller cannot AppendTo: not refused, no AppendTo asked on `sprk_aisearchindexes`, the row gets the final owning unit's index); the six-table drop (both spellings, and the unit-has-no-index form that the overwrite cannot hide); name-only; Secure team's unit and a Secure unit with no index (`search_index_unset_for_owner_bu`, ids only); `client_search_index_dropped` fires when values differ, is absent when equal (trim, ignore case) and when both are none, carries no name or email; not-search-indexed table keeps the caller's AppendTo check and writes the lookup and the column; K3 test kept (`AnExplicitNullBind_IsUnset_AndIsFilledUnderTheCanonicalKeyOnly`).
- `RecordCreationServiceMappingSeamTests.Task039.cs`: a mapping rule aimed at either column (explicit `mappingSource`) is dropped with no warning and no AppendTo asked, logged `origin=mapping`; a post-mapping default (`origin=default`); a caller's name on an event with no mapping.
- One test group per update path: `SecureChildOwnershipAiToolTests.SearchIndexUpdatePaths.cs` (child-records PATCH on an invoice in both bind spellings, only-index body, not-indexed table; chat update on workassignment/event/invoice/matter, only-index, not-indexed), `SearchIndexGenericUpdatePathTests.cs` (output orchestrator, Update Record node incl. confirmed proposal, field-mapping push, each with its not-indexed control), `DocumentPutSearchIndexTests.cs` (the real mapped `PUT /api/v1/documents/{id}`).
- Log of `PUT /documents` is covered by the helper tests on the other paths; the route's logger is the host's and is not captured there.

## Must-fire (tests fail with the drop reverted, pass with it restored)

The drop was disabled with a temporary switch (not committed): B off (create-side strips in `RecordCreationService`) and E off (`OwnsSearchIndex` false). Run over the new/changed suites (`SearchIndex`, `RecordCreationServiceMappingSeam`, `SecureChildOwnershipAiToolTests`, `DocumentPutSearchIndex`).

| Mode | Failed | Passed | Notes |
|---|---|---|---|
| B reverted | 16 | 719 | the #2399 repro, name-only, never-passed-on x6, logs, null bind and blank name, Secure-unit no-index, the three mapping-seam origins, the wizard parity test |
| E reverted | 20 | 709 | push x2, Update Record node x4, output orchestrator x5, chat x5, child-records PATCH x3, document PUT x1 |
| restored | 0 | 735 | |

## Verification

VERIFY_PLACEHOLDER

## Publish delta and CVE

PUBLISH_PLACEHOLDER

### Placement Justification (CLAUDE.md section 10 and 11)

Existing: the work lands in `RecordCreationService` step 8 and the existing `RecordTableRules` registry, and in the existing update paths that already own the write. Extension: nothing new is needed, because the registry's `SearchIndexed` flag already says which tables own an index and one `internal static` helper block on it serves create and every update path; a separate abstraction would only be a second place that says which tables are indexed. Cost of doing nothing: a client-supplied index lets the AppendTo check refuse a legitimate create (#2399) and lets any client choose which index a record lands in. Placement: code stays in the BFF inside the existing service; no new endpoint, no DI registration, no interface, no package, no background work, no new file under `src/`.

## Out of scope (found, not changed)

- The indexing-pipeline stamps (`RagEndpoints`, `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer`) write `sprk_searchindexname` on a document after indexing. They are server writers and not client-shaped, so they were left alone. If FR-38 should also own "where it was indexed", that is a separate decision.
- `PUT /api/v1/documents/{id}` cannot carry the lookup (the typed request has no such field), so only the name is dropped there.
- Repair stays fill-only by design (it repairs rows created before FR-38); it now covers the three new tables through the same registry flag.
