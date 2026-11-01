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

A body left with no column after the drop is not refused (NFR-07, review R2): nothing is written, `PATCH /api/v1/child-records/{table}/{id}` answers its normal 204 and the chat update returns success with a note that the search index is set by the server. A body that was empty BEFORE any drop keeps the mapper's 400 `'item' must contain at least one column` (pinned by a control test). The first version of this task answered the 400 for the index-only body; review round 1 found that was a new refusal.

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

Logs are outside the repo, in the session scratchpad folder `fm039\verify\` (`sln.log`, `arch.log`, `suites.log`) and `fm039\sln-build.log`.

- `dotnet build Spaarke.sln`: 0 errors, 0 warnings.
- `verify-suites.sh`: Spaarke.ArchTests 1884 passed, 0 failed; Sprk.Bff.Api.Tests (filtered) 6511 passed, 7 skipped (existing skips), 0 failed.
- Sprk.Bff.Api.Tests was never run unfiltered.
- Re-run after review round 1 at `40647a4c1d` (logs in `fm039\verify2\`): `dotnet build Spaarke.sln` 0 errors, 0 warnings; `verify-suites.sh` Spaarke.ArchTests 1886 passed, 1 failed on the first pass, then 1887 of 1887 (the failure was `ContactCreateOwnerPolicyGuardTests.Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime`, a wall-clock bound that overran (6 s) while another session's test host held about 16 GB of a 61 GB machine; it passes alone, with the other ArchTests of this change: 155 of 155); Sprk.Bff.Api.Tests (filtered, which already includes `ChildRecord` and `ExternalDocumentNarrowing`) 6529 passed, 7 skipped (existing), 0 failed, up from 6511 (18 new tests).

## Publish delta and CVE

`publish-delta.ps1 -MasterSha d0c1c5dc3c -BranchSha bcd40aecba`: master zip 40,985,178 bytes (192 files), branch zip 40,992,607 bytes (192 files); delta +7,429 bytes (+0.007 MB); total 39.09 MB (thresholds: +5 MB needs justification, 55 MB review, 60 MB stop: none reached). `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No new package.

After review round 1: `publish-delta.ps1 -MasterSha d0c1c5dc3c -BranchSha 40647a4c1d`: master zip 40,985,159 bytes (192 files), branch zip 40,993,275 bytes (192 files); delta +8,116 bytes (+0.008 MB); total 39.09 MB (no threshold reached). `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages.

### Placement Justification (CLAUDE.md section 10 and 11)

Existing: the work lands in `RecordCreationService` step 8 and the existing `RecordTableRules` registry, and in the existing update paths that already own the write. Extension: nothing new is needed, because the registry's `SearchIndexed` flag already says which tables own an index and one `internal static` helper block on it serves create and every update path; a separate abstraction would only be a second place that says which tables are indexed. Cost of doing nothing: a client-supplied index lets the AppendTo check refuse a legitimate create (#2399) and lets any client choose which index a record lands in. Placement: code stays in the BFF inside the existing service; no new endpoint, no DI registration, no interface, no package, no background work, no new file under `src/`.

## Review round 1 (Opus adversarial review of `09210a41c4`; no blocking defect)

Each item below has a test that failed before the fix and passes after (or, for a test-only item, a must-fire proof). Logs are outside the repo, in `fm039\` (`r2-before.log`, `r2-after.log`, `r3-before.log`, `r3-after.log`, `r4-after.log`, `r4-mustfire.log`, `r5-after.log`, `r5-mustfire.log`, `r7-before.log`, `r7-after.log`).

| Item | Finding | Fix | Commit | Before | After |
|---|---|---|---|---|---|
| R1 | A dropped index name could carry free text (an email, a long sentence) into the log | One shape check, `RecordTableRules.LoggableIndexName` (`^[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?$`, 100 ms timeout); anything else logs `<not an index name> (length N)`. Every log site goes through `LogSearchIndexDropped`, so it is the one place | `c306ddce43` | 2 failed | 0 failed (612 in the `SearchIndex` + `SecureChildOwnershipAiToolTests` filter) |
| R2 | An update naming only an index column was refused 400 `child_record.invalid_payload`, a refusal that did not exist before 039 (NFR-07) | When the drop empties the body, skip the write. Child-records PATCH answers 204; the chat update returns success with "the search index is set by the server, so it was left as it is". `PUT /documents` and the push already no-op. A body empty BEFORE any drop keeps the mapper's 400 (control test x4) | `509e67c005` | 2 failed of 6 | 0 failed of 6 (979 in the wider filter) |
| R3 | Repair's mapping could copy a parent's index onto a search-indexed row and report it Present | `RecordCreationService.Repair.cs`: both index columns join Repair's protected set when `rule.SearchIndexed`. Not put in `ProtectedFromMapping`, because create step 3 also reads it: the create-side drop at step 3.5 logs `origin=mapping`, and moving the exclusion would silence that log and add a skip warning. Repair stays fill-only; its own search-index step writes the owning unit's index | `745186b5d4` | 1 failed | 102 passed in the Repair filters |
| R4 | The real wizard route had no test of its own: reverting the Web API body drop (`RecordCreationService.cs:412-414`) broke nothing | `SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs`: `POST /api/v1/child-records/sprk_workassignment` over the real service with a unit that holds an index; both bind casings, both at once, a malformed bind, a catalog row the caller cannot AppendTo; asserts 201, no catalog read, exactly one server bind key holding the owning unit's index | `0ecd0bb982` | n/a (test only) | 5 pass; with the body drop reverted 3 fail (both spellings, malformed x2) |
| R5 | The must-not-fire test missed the lookup itself | `ATableNotMarkedSearchIndexed_KeepsTheCallersSearchIndexLookup_AndItsAppendToCheck`: on `sprk_todo`, a bind of `sprk_ai_search_index` with `NoAppendToIn` is refused as before, and written when allowed | `155da7b5e8` | n/a (test only) | 3 pass; with the drop made unconditional 2 fail |
| R6 | No guard against a new writer of a client-shaped body that forgets the drop | `tests/Spaarke.ArchTests/SearchIndexDropGuardTests.cs`: a source scan of `src/server/**` for the mapper entry points and the field updaters; each file must show `OwnsSearchIndex` / `WithoutSearchIndex` / `RemoveSearchIndex` / `rule.SearchIndexed` or be listed with a one-line reason (12 listed, no violation baseline; a stale entry fails). Must-fire and must-not-fire controls | `3c4577c050` | n/a | 3 pass; with the drop renamed out of `DataverseCreateRecordHandler` the guard fails |
| R7 | The chat create mapped the model's item before the service dropped the index, so a malformed index lookup failed the whole create (`ValidationFailed`) while the chat update dropped it | `DataverseCreateRecordHandler.cs`: when `OwnsSearchIndex`, the item's index columns are dropped before `MapAsync`, with the same helper and log | `7544f27dd5` | 1 failed of 3 | 0 failed |
| R9 | Docs said the I-5 row was the matter/project/document fill-only default; a comment at `RecordTableRules.cs` named a deleted Office method | I-5 row rewritten for FR-38 (six tables, both columns server-owned, step 8 the only writer, client value dropped on create and on BFF updates, native form edits out of scope); the comment now names step 8 | `40647a4c1d` | docs | docs |

Finding 10 (the rag index-file name names the index) is filed by the coordinator. Finding 8 (log timing and labels) is a known limit that the coordinator records; as the code stands:

- **K1 timing.** On a create, the `client_search_index_dropped` line is written at step 12, after the row exists. A create refused or faulted between the drop (steps 2 and 3.5) and the create logs the refusal on its own path but not the drop.
- **K2 labels.** `origin` says which strip caught the value (`caller`, `mapping`, `default`), not who wrote it: a value in a caller's already-mapped item that survives to the post-mapping strip is labelled `mapping`.
- **K3 chat create.** R7's early drop logs with `record` unset and no applied value (the row does not exist yet and the service finds nothing left to drop), so a chat create never logs the applied index beside the dropped one. A chat create whose item held ONLY an index column is left with no column and gets the mapper's empty-item answer; before, Dataverse would have refused the same create for its missing required columns (a primary name), so nothing is created either way, but the refusal text differs.
- **K4 update paths.** An update logs `unchanged` for the applied index, because the row's index is not touched; it is not read to compare.

## Out of scope (found, not changed)

- The indexing-pipeline stamps (`RagEndpoints`, `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer`) write `sprk_searchindexname` on a document after indexing. They are server writers and not client-shaped, so they were left alone. If FR-38 should also own "where it was indexed", that is a separate decision.
- `PUT /api/v1/documents/{id}` cannot carry the lookup (the typed request has no such field), so only the name is dropped there.
- Repair stays fill-only by design (it repairs rows created before FR-38); it now covers the three new tables through the same registry flag.
