# Task 041 — the search index follows the content type (FR-40, #2502): notes

> Rigor FULL. Model: opus @ high, from the POML. Step mode: prescriptive. Started 2026-11-03.
> Branch: `task/fm-041`, cut from the work-branch tip 9935ef2dcf (master merged in; no open PR touches 041's files).

## Step 1 — read-only dev count (AC1)

Run 2026-11-03 ~15:40Z with the Dataverse MCP `read_query` only, against `spaarkedev1`. **No Dataverse write was made, and no chunk or column was cleaned** (owner 2026-11-02: leave old chunks).

### Catalog (`sprk_aisearchindex`)

`SELECT sprk_aisearchindexid, sprk_searchindexname, sprk_isdefault, statecode FROM sprk_aisearchindex`

| Id | Name | Default | State |
|---|---|---|---|
| `fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6` | spaarke-files-index | **Yes** | Active |
| `ffcc183b-8b71-f111-ab0d-7ced8ddc4cc6` | spaarke-discovery-index | No | Active |
| `df04…`, `e104…`, `e304…`, `929a…`, `949a…`, `979a…` (6 rows) | spaarke-records-index | No | Active |
| `c104e55f-…` | spaarke-knowledge-index-v2 | No | Inactive |
| `dd04e55f-…` | spaarke-file-index | No | Inactive |

### (a) `sprk_document.sprk_searchindexname`

`SELECT sprk_searchindexname, COUNT(sprk_documentid) AS n FROM sprk_document GROUP BY sprk_searchindexname`

| Value | Rows |
|---|---|
| (null) | 64 |
| spaarke-files-index | 490 |
| **spaarke-knowledge-index-v2** | **76** |
| **spaarke-file-index** | **1** |

The total is 631. **77 stamps name an index other than files or discovery.** Escalation trigger 1 says to record these and continue, and the owner said to leave them. These are historical stamps for retired indexes, and 041 changes none of them.

### (b) `_sprk_ai_search_index_value` on the six search-indexed tables

`SELECT sprk_ai_search_index, COUNT(<id>) AS n FROM <table> GROUP BY sprk_ai_search_index`

| Table | null | = files-index row `fdcc183b` | Another catalog row |
|---|---|---|---|
| sprk_matter | 65 | 0 | **0** |
| sprk_project | 29 | 1 | **0** |
| sprk_workassignment | 31 | 1 | **0** |
| sprk_event | 93 | 2 | **0** |
| sprk_invoice | 12 | 0 | **0** |
| sprk_document | 631 | 0 | **0** |

### (c) Business units with an index

`SELECT businessunitid, name, sprk_searchindexname, sprk_ai_search_index FROM businessunit`

| BU | `sprk_searchindexname` | `sprk_ai_search_index` |
|---|---|---|
| Spaarke Business Unit 1 (`cb15f587-…`) | spaarke-files-index | `fdcc183b` (files) |
| Spaarke Demo (`9271b764-…`) | spaarke-files-index | null |
| Spaarke (root, `06fbf21c-…`) | spaarke-files-index | null |
| Secure Record, Spaarke Dev 1, Spaarke Test 1 | null | null |

**No business unit names an index other than `spaarke-files-index`.** Escalation trigger 1's STOP condition does not fire, and the "no behaviour change" premise of design §14.6 holds on dev.

## Steps 2–5 — implementation (implementer, branch `task/fm-041`)

### Failing-first (step 2)

The must-fire 1–7 tests and the AC10 test were written first and run against the unchanged src (filtered: `RagSendToIndex|RagIndexingJobHandlerIndexRouting|BulkRagIndexingJobHandlerReplaceStaleChunks|PostUploadIndexingEnqueuer|SearchIndexDefault|RecordRepairMode`): **51 failed, 102 passed, 153 total**.

| Failed before the change | Count | Must-fire |
|---|---|---|
| `RagSendToIndexIndexNameContractTests.IndexFile_ABodySearchIndexName_IsIgnored_TheWriteAndTheStampAreTheDefault` (allow-listed, not allow-listed) | 2 | MF1, MF2 |
| `RagSendToIndexIndexNameContractTests.SendToIndex_WhenTheDocumentParentOrBusinessUnitNamesAnotherIndex_TheWriteAndTheStampAreTheDefault` | 1 | MF3 |
| `RagIndexingJobHandlerIndexRoutingTests.ProcessAsync_WhenThePayloadDocumentParentOrUnitNamesAnotherIndex_WritesAndStampsTheFilesIndex` (null → Poisoned, the strict resolver threw on `ResolveAsync`; `"spaarke-invoices-index"` → request named it) | 2 | MF4 |
| `BulkRagIndexingJobHandlerReplaceStaleChunksTests.ProcessAsync_WhenTheDocumentParentOrUnitNamesAnotherIndex_WritesTheFilesIndexOnce` (request named `spaarke-knowledge-index-v2`) | 1 | MF4 |
| `SearchIndexDefaultTests`: `ACreateWhoseOwningUnitHasAnIndex_WritesNeitherColumn_TracesStep8NoOp_AndReadsNoBusinessUnit` ×6 and 30 other rewritten create cases (MF5), plus 4 drop-log cases from the AC9-exception list (applied value / level) | 40 | MF5 (+AC13) |
| `RecordRepairModeTests` `AUserOwnedRowReOwnedInThisPatch_…`, `ARowMissingTheIndexColumns_…` ×2, `AMappingRuleTargetingTheSearchIndex_…` | 4 | MF6 |
| `PostUploadIndexingEnqueuerTests.EnqueueIfApplicableAsync_SharedIndexNameDiffersFromTheKnowledgeIndex_StampsTheKnowledgeIndex` (stamped `spaarke-knowledge-index-v2`) | 1 | MF7 |

As must-not-fire requires, these passed before the change: AC10 (`Search_ARequestNamedIndexOutsideTheAllowList_Is400IndexNotAllowed_ByTheRealAllowListCheck`), index-file with no body index (MNF1), and the drop-half tests that do not depend on the applied value. The full log is in the session scratchpad (`failing-first.txt`).

The MF7 test's arrangement changed after the src change. The enqueuer no longer takes `IOptions<AnalysisOptions>`, so `SharedIndexName` cannot reach it. The test now builds the enqueuer over a resolver whose default is `spaarke-files-index` and asserts that stamp. The "SharedIndexName differs" input is enforced by type: the constructor parameter is gone (AC7).

### Changed files

**src (§14.4 rows 1–9)**
- `Services/Ai/SearchIndexNameResolver.cs`: `ResolveAsync`, the FetchXml chain, `IGenericEntityService` and the logger are removed. What remains is `GetDefaultIndexName()`, the constants `LookupColumn` and `LegacyTextColumn`, and a constructor over `IOptions<AiSearchOptions>`.
- `Api/Ai/RagEndpoints.cs`:
  - send-to-index: no resolver call, no `SearchIndexName` on the request, and the stamp is `GetDefaultIndexName()`;
  - index-file: one comment only;
  - search route: untouched.
- `Services/Ai/IFileIndexingService.cs`: `SearchIndexName` removed from `FileIndexRequest` and `ContentIndexRequest`.
- `Services/Ai/FileIndexingService.cs`: passes `searchIndexName: null`, the tenant default, to the RAG write and to the stale-chunk trim.
- `Services/Jobs/Handlers/RagIndexingJobHandler.cs`, `Services/Ai/Jobs/BulkRagIndexingJobHandler.cs` and `Workers/Office/IndexingWorkerHostedService.cs`:
  - no resolver chain and no INDEX_NOT_ALLOWED retry;
  - `RagIndexingJobPayload.SearchIndexName` is removed;
  - the bulk handler and the worker drop the resolver dependency.
- `Services/Ai/PostUploadIndexingEnqueuer.cs` and `IPostUploadIndexingEnqueuer.cs`: the constructor takes `ISearchIndexNameResolver` in place of `IOptions<AnalysisOptions>` (ADR-010, no new registration), and `PostUploadIndexingRequest.SearchIndexName` is removed.
- Eight producers drop `SearchIndexName: null`:
  - `UploadFinalizationWorker`
  - `AnalysisEndpoints`
  - `ExternalProjectDataEndpoints`
  - `ComposeService`
  - `AnalysisResultPersistence`
  - `IncomingCommunicationProcessor`
  - `CommunicationEnrichmentService`
  - `PublicContracts/RelocatedFileIndexing`
- `Services/RecordCreation/RecordCreationService.cs`:
  - step 8 is a recorded NoOp, "the index follows the content type (#2502); nothing written";
  - removed: `SearchIndexDefaultAsync`, `BusinessUnitSearchDefaultsAsync`, the BU-defaults warning, and the decision's `AppliedSearchIndex` and `DefaultWarnings`;
  - step 12 logs the drop with `applied: (null, null)`.
- `Services/RecordCreation/RecordCreationService.Repair.cs`: `RepairSearchIndexAsync`, `RecordRepairPart.SearchIndex` and the reason codes `SearchIndexOwnerUnsettled`, `SearchIndexUnavailable` and `SearchIndexNoDefault` are deleted. The protected-columns guard (Repair's mapping never writes either column) is kept.
- `Services/RecordCreation/RecordTableRules.cs`: both `client_search_index_dropped` calls use `LogInformation`; the docs are updated.
- Comments only: `Infrastructure/DI/AnalysisServicesModule.cs`, `Api/DataverseDocumentsEndpoints.cs`.

**tests**: see "Test scope" below.

**scripts / docs**
- `scripts/backfill-multi-container-multi-index/Audit-MultiContainerMultiIndex-Drift.ps1`: index half removed (AC14).
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`: I-5 rewritten.
- `docs/architecture/AI-SEARCH-INDEX-CATALOG.md`: §4 routing subsection added (AC18).

### AC status

| AC | Status | Evidence |
|---|---|---|
| AC2 | Met | MF1, MF2 and MNF1: the `IndexFile_*` theory in `RagSendToIndexIndexNameContractTests`. With an allow-listed, a non-allow-listed or no body index, the request reaching the pipeline names no index, the stamp is `KnowledgeIndexName`, and the response is 200, never 400. |
| AC3 | Write: met for send-to-index, the RAG handler and the bulk handler by test (MF3, MF4). Stamp: met for send-to-index and the RAG handler by test, and for the bulk handler by test since review round 1 (F1). **Office worker: stamped; by type and code only, no harness** (known limit, not Met-by-test) | Corrected in review round 1: before it, the bulk handler and the Office worker wrote `KnowledgeIndexName` but never stamped the document. Both now stamp `GetDefaultIndexName()` on success, as `RagIndexingJobHandler` does; a failed stamp is logged and fails nothing. The Office worker request has no index field. All INDEX_NOT_ALLOWED retry branches are deleted (grep `INDEX_NOT_ALLOWED` in the four files: none). |
| AC4 | Met | The four DTOs have no index field. `grep -n ResolveAsync SearchIndexNameResolver.cs` finds nothing. `GetDefaultIndexName` and its two tests remain. |
| AC5 | Met | MF5 ×6 tables (`SearchIndexDefaultTests`): neither column, a step-8 NoOp trace and `BusinessUnitReads` empty. The pipeline seam traces are now NoOp, and step numbers are unchanged. |
| AC6 | Met | MF6 (`RecordRepairModeTests`: re-owned row, the missing-columns theory, the mapping rule). The repair method and its three reason codes are deleted. |
| AC7 | Met | MF7 (rewritten in review round 1 F2 over a real `SearchIndexNameResolver`), plus the constructor swap. |
| AC8 | Met | `AnExplicitNullBind_IsDropped_AndNoBindKeyIsSent_NotEvenAsNull`, `AssertNoIndexColumn` (key absent, never null) and the Repair `AssertNoIndexKey`. |
| AC9 | Met except for the listed exceptions | See "AC9 exceptions". `SearchIndexGenericUpdatePathTests`, `SecureChildOwnershipAiToolTests.SearchIndexUpdatePaths`, `SearchIndexDropGuardTests`, the index-file authorization contract tests, `SearchIndexNameEndpointContractTests`, the `RagEndpointsTests` search-selection tests and the session-files, discovery and invoice routing tests needed no edit. |
| AC10 | Met | New test `RagSendToIndexIndexNameContractTests.Search_ARequestNamedIndexOutsideTheAllowList_Is400IndexNotAllowed_ByTheRealAllowListCheck`. It runs through the real `KnowledgeDeploymentService` allow-list (`AiSearch:AllowedIndexes` configured in the fixture; no mocked throw) and asserts 400 with `extensions.code == "INDEX_NOT_ALLOWED"`. |
| AC11 | Met | See "AC11 trim grep". |
| AC12 | Met | See "Build, tests, publish, CVE". |
| AC13 | Met | `grep client_search_index_dropped src` finds two log sites, both in `RecordTableRules.LogSearchIndexDropped`, both `LogInformation` (RecordCreationService only calls it). `SearchIndexDefaultTests.ADroppedIndexThatDiffers_…` asserts `LogLevel.Information`. Task 035 returns it to Warning. |
| AC14 | Met | The audit script's index half is removed and the container half kept. The runbook reference for task 062 and the backfill inventory are below. |
| AC16 | 042b has not merged | Neither `HEAD` nor `origin/master` contains it; TASK-INDEX shows 042b 🔲. **042b carries the change**: its §15.5 step-8 bullet, and its "event index empty plus `search_index_unset_for_owner_bu`" assertion, become "step 8 is a NoOp; no index column". |
| AC18 | Met | I-5 reads "**no server writer; client values dropped; the stamp is the pipeline's**". `AI-SEARCH-INDEX-CATALOG.md` §4 gains "Write-time routing: the index follows the content type", with rules 1–4 of design §14.2 and the search-time allow-list note. |
| AC19 | Met; no correction needed | The listed lines in 029, 030, 036, 052 and 053 say "no index column written by the server; the document stamp names the files index after indexing", which matches the final behaviour. **Outside AC19's list and stale:** 052 `:49` ("Repair mode is the owner of mapping, owner, search index and stamp") and `:95`/`:97` (Repair "repairs … search index"). Repair no longer writes an index, so the 052 executor should read those lines as "mapping, owner and stamp". They are not edited here. |

### Test scope (must-fire 1–7, must-not-fire 1–6, AC10; anything beyond, with a one-line reason)

New:
- `tests/unit/domain/Ai/RagIndexingJobHandlerIndexRoutingTests.cs` (MF4)
- in `tests/integration/contract/Api/Ai/RagSendToIndexIndexNameContractTests.cs`:
  - the MF1/MF2/MNF1 theory
  - AC10
  - the bulk MF4 in `BulkRagIndexingJobHandlerReplaceStaleChunksTests.cs`

Rewritten:
- MF3, which inverts `:105`.
- MF5: `SearchIndexDefaultTests` `:115`, `:125`, `:135`, `:154`, `:197`, `:268`.
- MF6: `RecordRepairModeTests` `:229`, `:264`.
- MF7: `PostUploadIndexingEnqueuerTests`.

Beyond the list, each with its reason:
- `RecordRepairModeTests.AMappingRuleTargetingTheSearchIndex_IsNeverApplied_AndRepairWritesNoIndex` (was `:913`): it asserted a fill from the owning unit, so its subject changed. It now pins AC6's "Repair's mapping still cannot write the columns".
- `VersionSaveAiRefreshSeamTests` (§14.7 lists it as compile-only): it also pinned the routed index (`RoutedIndex`). It now asserts that the default index holds the chunks and that the routed index is empty. These are routing-pinning assertions, so they follow AC3.
- `OfficeQuickCreateContractTests.Post_Matter_WithMatterType_Returns201_SetsTypeOwnerAndBusinessUnitDefaults_…` → `…SetsTypeAndOwner_NoSearchIndex_…`, and `OfficeQuickCreateProjectContractTests.Post_Project_Returns201_OwnedByCallersTeam_WithBusinessUnitDefaults_…` → `…NoSearchIndex_…`: these are the §14.7 compile-only "Office quick-create … where they assert index columns". The index asserts are inverted to "absent", and the tests are renamed because the old names lied.
- `SecureRootCreateRouteTests.OfficeSecureOwner`:
  - `…AndIndexedByItsOwnersUnit` → `…AndGetsNoSearchIndex`
  - `…NotSecure_AndIndexedByThatUnit` → `…NotSecure_AndGetsNoSearchIndex`
  - These are §14.7 "secure-root seam tests where they assert index columns". `TeamReads` is now empty, and neither key is in the body. The cross-reference comment in `CrossSurfaceOwnerTests.cs:71` follows the rename.
- `RecordCreationServiceSecureRootSeamTests.CreateAsync_AProjectUnderASecureMatter_IsCreatedIntoIsolation_WithTheSecureBusinessUnitsSearchDefaults` → `…WithNoSearchIndex`: §14.7 secure-root seam. No unit is read, and neither key is in the body.
- `RecordCreationServicePipelineSeamTests` `:145`, `:257`: the step-8 trace `Ran` → `NoOp` (AC5).
- Compile-only:
  - `PostUploadIndexingEnqueuerReplaceStaleChunksTests` (constructor, request)
  - `PostUploadIndexingEnqueuerTests` (constructor, request; the `capturedRequest.SearchIndexName` assert is gone with the field)
  - `BulkRagIndexingJobHandlerReplaceStaleChunksTests` (resolver removed)
  - `SearchIndexNameResolverTests` (constructor)
  - `RelocatedFileIndexingTests` (constructor)

### Deletions (ADR-038 A3: subject gone)

| Test | §14.7 | A3 evidence |
|---|---|---|
| `SearchIndexNameResolverTests` ×9 `ResolveAsync_*` (`:89-326`), with their helpers | listed | `ResolveAsync` is deleted (AC4). The two `GetDefaultIndexName_*` tests stay. |
| `RagIndexingJobHandlerTests` `:490`, `:522`, `:558` (region "SearchIndexName Threading") | listed | `RagIndexingJobPayload.SearchIndexName` and `FileIndexRequest.SearchIndexName` are deleted, so there is nothing to thread. Routing is covered by MF4. |
| `FileIndexingServiceTests` `:459`, `:506` (thread-through) | listed | `SearchIndexName` is deleted from both request types. `IndexFileAppOnlyAsync_WithoutSearchIndexName_PassesNullToRagService` stays and pins the null. |
| `SearchIndexDefaultTests.AnEventUnderASecureMatter_WhoseUnitHasNoIndex_GetsNone_…_AndLogsIt` (`:280`) | listed | Step 8 fills nothing and logs no `search_index_unset_for_owner_bu`. The no-column half is folded into `AnEventUnderASecureMatter_WhoseUnitHasAnIndex_GetsNoIndexColumn_AndNoUnitIsRead`, which also asserts the log is absent. |
| `RecordRepairModeTests.AnOwnerKeptInItsUnitByTheBusinessUnitSkip_GetsNoSearchIndex` (`:210`) | listed | Repair writes no index. The owner half is covered by `AnOwnerInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2`. |
| `RecordRepairModeTests` `:166`, `:364` | listed, **trimmed, not deleted** | Deviation: each also asserts a live owner subject (`AnUnresolvableOwner_IsLeftUnchanged_WithATypedRefusal`, `AUserOwnedSecureFlaggedRoot_KeepsItsOwner`), so A3 does not allow deleting them. Only their SearchIndex-part assertions are removed, and the tests are renamed to the owner truth. |

### AC9 exceptions (approved by the coordinator; for UAC-r2's AC17 check)

Rules applied:
- only applied-value, Warning→Information and BU-bind fragments are edited;
- every other drop assertion is byte-for-byte;
- a test whose name would lie is renamed, never deleted;
- AC13: task 035 returns the level to Warning.

| File : test | Old fragment | New fragment | Reason |
|---|---|---|---|
| `SearchIndexDefaultTests.cs : ADroppedIndexThatDiffers_IsLogged_WithIds_Origin_AndTheTable_AndNoNamesOrEmail` | `l.Level == LogLevel.Warning` | `l.Level == LogLevel.Information` (because "Information until W3 lands…; task 035 returns it to Warning") | AC13 |
| same | `.And.Contain($"appliedIndexId={ChildIndex}")` | `.And.Contain("appliedIndexId=(null)")` | AC5 (nothing applied) |
| same | `.And.Contain("appliedIndexName=" + ChildIndexName)` | `.And.Contain("appliedIndexName=(null)")` | AC5 |
| `SearchIndexDefaultTests.cs : ADroppedNameThatIsNotAnIndexName_NeverReachesTheLog_OnlyItsLength` | `line.Should().Contain("appliedIndexName=" + ChildIndexName, "the catalog's own name …")` | `line.Should().Contain("appliedIndexName=(null)", "nothing is applied (task 041), so no catalog name is logged")` | AC5 |
| `SearchIndexDefaultTests.cs : ADroppedIndexThatEqualsTheApplied_IsNotLogged_NameComparedTrimmedAndIgnoringCase` → **renamed** `ADroppedIndexThatEqualsTheOwningUnitsIndex_IsLogged_BecauseNothingIsApplied` | `_world.Log.Messages.Should().NotContain(m => m.Contains("client_search_index_dropped"), …)` | `…Where(m => m.Contains("client_search_index_dropped")).Should().HaveCount(2, "one line per column: nothing is applied, …")` | AC5: the unit's own index no longer equals the applied (none) |
| `SearchIndexDefaultTests.cs : Issue2399_AWorkAssignmentBindingTheUsersUnitIndexTheCallerCannotAppendTo_IsNotRefused_AndAsksNoAppendToOnTheCatalog` | `Text(fields, LookupBind).Should().Be($"/sprk_aisearchindexes({ChildIndex:D})", …)`; `Text(fields, NameColumn).Should().Be(ChildIndexName)` | `AssertNoIndexColumn(fields)` | AC5 (BU bind) |
| `SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs : WorkAssignmentCreate_ABodyBindingASearchIndexTheCallerCannotAppendTo_IsCreated_AskingNoAppendToOnTheCatalog` | `….Should().ContainSingle().Which.Should().Be("sprk_AI_Search_Index@odata.bind", …)`; `ServerValue(… bind).Should().Be(UnitIndexRow …)`; `ServerValue(… "sprk_searchindexname").Should().Be(UnitIndexName, …)` | `….Should().BeEmpty("the wizard's bind is dropped and the server writes none (task 041)")`; `fields.Should().NotContainKey("sprk_searchindexname", …)` | AC5 (BU bind) |
| `… : WorkAssignmentCreate_ABodyWithBothSpellingsOfTheSearchIndexBind_IsCreated_WithTheOwningUnitsIndexOnly` → **renamed** `…_IsCreated_WithNoIndex` | `….Should().ContainSingle()`; `ServerValue(… bind).Should().Be(UnitIndexRow …)` | `….Should().BeEmpty()` | AC5 (BU bind) |
| `… : WorkAssignmentCreate_ABodyWithAMalformedSearchIndexBind_IsCreated_NotRefusedAs400` | `ServerValue(… bind).Should().Be(UnitIndexRow …)` | `fields.Keys.Where(… "sprk_ai_search_index" …).Should().BeEmpty()` | AC5 (BU bind) |
| `… : ChatCreate_AClientIndexThatDiffersFromTheApplied_LogsOneDrop_WithTheRecordId_TheAppliedIndex_AndOriginCaller` → **renamed** (review round 1 #3) `…_LogsOneDrop_WithTheRecordId_AndOriginCaller` | `ServerValue(created.Fields, bind).Should().Be(UnitIndexRow …)`; `.And.Contain($"appliedIndexId={UnitIndexRow}")` | `created.Fields.Keys.Where(…).Should().BeEmpty()`; `.And.Contain("appliedIndexId=(null)")` | AC5. `line.Should().NotContain("unchanged", "a create has an applied index; …")` still passes: the create logs `(null)`, the update logs `unchanged`. **Review round 1 #3 (coordinator lifted byte-for-byte for this one line):** the assertion is unchanged; its because-text `"a create has an applied index; 'unchanged' is the update's"` became ``"a create logs `(null)` as the applied value; 'unchanged' is the update's wording"``. |
| `… : ChatCreate_AClientIndexThatEqualsTheApplied_LogsNoDrop` → **renamed** `ChatCreate_AClientIndexThatEqualsTheOwningUnitsIndex_IsLogged_BecauseNothingIsApplied` | `ServerValue(fields, bind).Should().Be(UnitIndexRow …)`; `dropLines.Should().BeEmpty("FR-38 logs a dropped index only when it differs from the applied one")` | `fields.Keys.Where(…).Should().BeEmpty()`; `dropLines.Should().HaveCount(2, "one line per column: nothing is applied, …")` | AC5 |
| `… : ChatCreate_AMalformedClientIndex_StillSucceeds_AndIsLoggedOnceFromTheService` → **renamed** `…_StillSucceeds_AndLogsNoDrop_BecauseBothAreNone` | `ServerValue(created.Fields, bind).Should().Be(UnitIndexRow …)`; `dropLines.Should().ContainSingle().Which.Should().Contain($"record={created.Id}").And.Contain($"appliedIndexId={UnitIndexRow}")` | `created.Fields.Keys.Where(…).Should().BeEmpty()`; **added** `created.Fields.Should().NotContainKey("sprk_searchindexname", "the malformed value never reaches the row")`; `dropLines.Should().BeEmpty("FR-38: equal means both none; a client value that parses to no id equals the empty applied value")` | AC5 (nothing applied). Cross-reference **019 P2-5**: it said this case "is moot once 041 makes step 8 a no-op". It is not moot: a malformed client index now never logs, because the applied value is always none. It stays **K4** (diagnostics only): the column is dropped either way, and a client index that parses to a real id still logs. The coordinator updates `task-019-notes` P2-5. |
| `RelocatedFileIndexingTests.cs : Issue1510_TheRelocatedFile_IsIndexedByTheJobHandler_UnderTheDocumentsParent` | `names.Verify(n => n.ResolveAsync(Document.ToString("D"), "matter", matter.ToString(), It.IsAny<CancellationToken>()), Times.Once, "the index routing sees the recovered parent too");` | (removed) | Subject gone: AC2/AC3, §14.4 row 7. #1510 parent recovery is still asserted via `ParentEntity == matter`. Constructor lines are compile-only. The test is not renamed. |
| `OfficeQuickCreateProjectContractTests.cs : Post_Project_WhenBusinessUnitDefaultsCannotBeRead_StillCreates_AndWarns` → **rewritten as** `Post_Project_NeverReadsTheBusinessUnitDefaults_SoAFailingTeamReadNeitherFailsNorWarns` | `body!.Warnings.Should().ContainSingle(w => w.Contains("Business-unit search defaults could not be read"))` plus the "this project falls back" noun pin | `body!.Warnings.Should().BeNull(…)`; `factory.Entities.Verify(e => e.RetrieveAsync("team", OwnerTeam.Id, …), Times.Never)` | The subject was deleted with step 8's BU read (AC2/AC3). The arrangement still makes that team read throw. The test now proves the create never makes the team or BU read: the old failing read becomes a `Times.Never` trip-wire, and no warning is produced. `created.Entity!.Contains("sprk_searchindexname").Should().BeFalse()` is unchanged. |

ResolveAsync grep (coordinator condition). Across `tests/`, the only `Verify`/`Setup` of `ISearchIndexNameResolver.ResolveAsync` that assert arguments or counts were:
- the `RelocatedFileIndexingTests` line above;
- the `RagIndexingJobHandlerTests` region, which was deleted.

The other hits were compile-only mock setups (BulkRag, VersionSave and the deleted `SearchIndexNameResolverTests`). No other AC9 hit was found. The two must-not-fire lines (`RelocatedFileIndexingTests.cs:71`, `:137`) are unchanged.

Not edited, still green, now vacuous: `OfficeQuickCreateSourceFieldSecurityTests` asserts that `Warnings` does not contain "Business-unit search defaults could not be read" (`:87`, `:100`, `:127`). That warning no longer exists in src, so those three lines can never fail. They are left for `/test-diet`.

### Prescribed steps not done

- **§14.4 row 10: move `LookupColumn` / `LegacyTextColumn` off `SearchIndexNameResolver`.** Skipped, with the coordinator's agreement.
  - Why: AC9 pins `Spaarke.ArchTests/SearchIndexDropGuardTests.cs` at `:190`, `:198`, `:205` and `:207`, which reference `SearchIndexNameResolver.LookupColumn`, so moving the constants would mean editing an AC9 test.
  - Coupling: the CRUD→AI reference (`RecordTableRules` / Repair → `SearchIndexNameResolver` constants) is unchanged from master, so no new coupling is added.
  - The class still compiles with only `GetDefaultIndexName()` and the two constants.
  - Its DI registration (`AddScoped<ISearchIndexNameResolver, SearchIndexNameResolver>`) is still used by `RagEndpoints` (index-file, send-to-index), `RagIndexingJobHandler`, `PostUploadIndexingEnqueuer`, and (since review round 1) `BulkRagIndexingJobHandler` and `IndexingWorkerHostedService`, so it is not dead.
  - The dead constructor dependencies (`IGenericEntityService`, logger) were removed with `ResolveAsync`.
  - The bulk handler and the Office worker take it again since review round 1 (F1), for the stamp only (`GetDefaultIndexName()`).
  - ArchTests pass (see below), so no layering rule broke.

### AC14 — scripts

**Audit script.** `Audit-MultiContainerMultiIndex-Drift.ps1` is now container-only:
- removed: the BU `sprk_searchindexname` read, the record and document index columns, the container→index map (replaced by a known-container list), the index-mapping anomaly, and the `currentIndex`/`derivedIndex` report columns;
- kept: the parent container vs child mode, the BU-container override/drift classification, the unknown-container anomaly, and the read-only GET contract.

It parses (`Parser.ParseFile`: 0 errors) and has no PATCH/POST/PUT/DELETE call.

**Runbook reference for task 062.** `docs/guides/MULTI-CONTAINER-MULTI-INDEX-OPERATOR-RUNBOOK.md` still describes setting BU `sprk_searchindexname` and an index drift audit at `:7`, `:59-131`, `:183-206`, `:227`, `:252`, `:302-313` and `:334`.

**Backfill inventory (not changed).** Neither backfill script was run.

| Script | Writes | Value written | Can it differ from the written index? |
|---|---|---|---|
| `Backfill-MultiContainerMultiIndex-Documents.ps1` | `sprk_document.sprk_searchindexname`, PATCH, only when empty (`:198`, `:278-280`) | `$ContainerIndexMap[$graphDrive]` (`:93-95`); both known containers map to the literal `spaarke-files-index` | **Yes, in an environment whose `AiSearch:KnowledgeIndexName` is not `spaarke-files-index`** (`appsettings.template.json:332` takes `#{SHARED_KNOWLEDGE_INDEX_NAME}#`). The stamp would then name an index the file was not written to, and rule 3 of §14.2 requires the stamp to be true. On dev the two match. |
| `Backfill-MultiContainerMultiIndex-ParentRecords.ps1` | `sprk_containerid` and `sprk_searchindexname` on the five parent tables, only when empty (`:482`, `:550-556`, `:584`) | the same literal map (`:157-159`) | Not a stamp. It writes a record column that §14.2 rule 4 says has **no server writer**, so it contradicts FR-40 wherever it runs. |

**To file per `/project-defer-issue-tracking`.** I did not file it: a sub-agent does not open GitHub issues, so main files it. Proposed text: "Retire or rewrite the two Backfill-MultiContainerMultiIndex-*.ps1 scripts for FR-40: the document script's hard-coded `spaarke-files-index` can stamp an index other than `KnowledgeIndexName`; the parent script writes record index columns that no longer have a writer".

### AC11 — trim grep

`git diff HEAD -U0 -- src | grep -E '^[+-].*(TrimToReadable|IRetrievalAccessTrim|RetrievalAccessTrim|accessTrim)'` finds nothing.

In the guarded set, the diff touches four files:
- `RagEndpoints.cs`: send-to-index routing lines and one comment in index-file; the search route and its trim are untouched;
- `ExternalProjectDataEndpoints.cs`, `CommunicationEnrichmentService.cs` and `IncomingCommunicationProcessor.cs`: one `SearchIndexName: null` line each.

`SemanticSearchEndpoints`, `ReadableRagSearch`, `AnalysisRagProcessor`, `DocumentSearchHandler` and `PlaybookChatContextProvider` are not in the diff. No added line contains `sprk_isdefault`.

### Build, tests, publish, CVE (AC12)

- **BFF build**: `dotnet build src/server/api/Sprk.Bff.Api/` gives 0 warnings, 0 errors. The test project builds with 0 errors.
- **Filtered suites**: one `dotnet test --filter` over the AC12 terms plus `SecureRootCreateRoute` (the Office secure-owner tests §14.7 names). Sprk.Bff.Api.Tests was never run unfiltered. Result: **1695 passed, 0 failed, 3 skipped, 1698 total.** The 3 skips are the pre-existing `OfficeEndpointsContractTests.Post_OfficeQuickCreate_*` (019 P1-5, K4). Per term (a test name can match several terms, so the rows overlap):

| Filter term | Passed | Failed | Skipped |
|---|---|---|---|
| RecordCreation* | 502 | 0 | 0 |
| SearchIndex* | 151 | 0 | 0 |
| RecordRepairMode* | 65 | 0 | 0 |
| RagIndexingJobHandler* | 27 | 0 | 0 |
| BulkRagIndexingJobHandler* | 5 | 0 | 0 |
| FileIndexingService* | 17 | 0 | 0 |
| PostUploadIndexingEnqueuer* | 32 | 0 | 0 |
| RagSendToIndex* | 9 | 0 | 0 |
| RelocatedFileIndexing* | 11 | 0 | 0 |
| VersionSaveAiRefresh* | 9 | 0 | 0 |
| QuickCreate* (Office quick-create contract) | 298 | 0 | 3 |
| CrossSurfaceOwner* | 12 | 0 | 0 |
| SecureChildOwnership* | 547 | 0 | 0 |
| SecureRootCreateRoute* | 31 | 0 | 0 |

- **ArchTests**: `dotnet test tests/Spaarke.ArchTests/` gives **1911 passed, 0 failed** (including `SearchIndexDropGuardTests`, unedited).
- **Publish-size delta** (§10). Both trees were published `-c Release` with `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`, then zipped with PowerShell `Compress-Archive`.
  - Baseline: a fresh `origin/master` 8438fc93f5 in a detached worktree at `C:\wt041m`, published to `C:\wt041m-pub`. Master is 4 commits ahead of the branch's merge-base 21730d5d65, none of them in `src/server`, so it is a like-for-like baseline.
  - Branch: published to `C:\wt041b-pub`.

| Build | Files | Unzipped bytes | Zip bytes |
|---|---|---|---|
| origin/master 8438fc93f5 | 192 | 136,967,320 | 41,216,763 (39.31 MB) |
| task/fm-041 | 192 | 136,946,228 | 41,210,702 (39.30 MB) |
| **Delta** | 0 | −21,092 | **−6,061 bytes** |

  Well under the +5 MB, 55 MB and 60 MB thresholds.
- **CVE**: `dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive` reports "no vulnerable packages given the current sources". No package was added or changed.

### Anything else noticed (not changed)

- `PostUploadIndexingEnqueuerReplaceStaleChunksTests` and `PostUploadIndexingEnqueuerTests` still `using Sprk.Bff.Api.Configuration` (needed for `PostUploadIndexingOptions`/`ServiceBusOptions`); no change.

## Review round 1 (Opus adversarial review: 0 High, no authorization bypass, no data-loss path)

| # | Class | Finding | Change | Evidence |
|---|---|---|---|---|
| 1 | F4 | The bulk handler and the Office worker wrote `KnowledgeIndexName` but never stamped the document (AC3 "write AND stamp"; catalog rule 3). | `BulkRagIndexingJobHandler` and `IndexingWorkerHostedService` stamp `sprk_searchindexname = GetDefaultIndexName()` (with `SearchIndexCompletedOn`, `SearchIndexed`, `SearchIndexedOn`) after a successful index, a copy of `RagIndexingJobHandler`'s stamp block and its failure handling: a failed stamp is logged at Warning and never fails or retries the index. Skipped files (E-Billing Output Files) are not stamped. New ctor deps are the two `RagIndexingJobHandler` already uses (`IDocumentDataverseService`, `ISearchIndexNameResolver`), both already registered; no new DI registration, package or file. | Bulk: new `BulkRagIndexingJobHandlerReplaceStaleChunksTests.ProcessAsync_ForceReindexOfADocumentStampedWithAnotherIndex_RestampsTheDefaultIndex` (document stamped `spaarke-knowledge-index-v2`; after ForceReindex the one `UpdateDocumentAsync` for that document carries the default). Failing-first: with the stamp call removed from the handler it **failed** (UpdateDocumentAsync 0 times); restored, it passes. Also new `ProcessAsync_WhenTheStampFails_TheJobStillCompletesWithTheDocumentProcessed` (a guard: Completed, Processed=1, one index call; it passes before and after). Office worker: stamped; by type and code only, no harness (known limit). |
| 2 | F3 | MF7's name claimed "SharedIndexName differs" with no such input, and it asserted a mocked `GetDefaultIndexName`. | Renamed `EnqueueIfApplicableAsync_StampsTheConfiguredKnowledgeIndexName`. It builds the real `SearchIndexNameResolver` over `AiSearchOptions { KnowledgeIndexName = "k-041" }` and asserts the stamp is `k-041`. `AiSearchOptions` has no `SharedIndexName`, and the enqueuer no longer takes `AnalysisOptions`, so a different SharedIndexName cannot be an input (said in the test's summary). | Test-only change; passes. It also passes on c58ea58faf (no src change behind it): it strengthens the proof, it does not catch a new defect. |
| 3 | K4 | `SecureChildOwnershipAiToolTests.SearchIndexCreateRoute.cs:190,201`: the name says "TheAppliedIndex" and the because-text says a create has an applied index. | Test renamed `ChatCreate_AClientIndexThatDiffersFromTheApplied_LogsOneDrop_WithTheRecordId_AndOriginCaller`; because-text reworded. Assertion unchanged. Listed under AC9 exceptions. | Passes in the filtered run. |
| 4 | K4 | `RecordCreationService.cs` `OwnerRepair.BusinessUnit` was dead. | Removed the member and its 4 constructor arguments. Consequent dead code removed with it: `OwnerKept`'s `rowUnit` parameter (it only fed that argument; 4 callers) and `OwnerRefused`'s `unit` local. The `TeamBusinessUnitAsync` / `teamUnit` read is kept: the trigger-2 check uses it, as does `rowUnit`. | Behaviour unchanged; RecordRepairMode and RecordCreation suites pass. |
| 5 | K4 | Stale text. | `RecordTableRules.cs` (`sprk_document` rule comment): the Office Matter/Project create no longer fills an index; a document's `sprk_searchindexname` is the pipeline's stamp. `AI-SEARCH-INDEX-CATALOG.md` rule 2: `FileIndexingService` takes no index argument and passes none to `RagService`; `RagService.IndexDocumentsBatchAsync` is the seam that still takes an optional index (`null` = tenant default). | Text only. |

**Round 1 verification.** BFF build: 0 warnings, 0 errors. Filtered suites (the AC12 terms plus `SecureRootCreateRoute`, `IndexingWorker`, `BulkRag`, `PostUploadIndexingEnqueuer`; Sprk.Bff.Api.Tests never unfiltered): **1697 passed, 0 failed, 3 skipped, 1700 total** (the same 3 pre-existing `OfficeEndpointsContractTests.Post_OfficeQuickCreate_*` skips; +2 = the two new bulk tests). ArchTests: **1911 passed, 0 failed**. No file or package added, so the publish delta was not re-measured.

Left alone as instructed: the three vacuous Office assertions (`/test-diet`), §14.4 row 10, the P2-5 logging, and `DeliverToIndexNodeExecutor` (the coordinator files it).

**Release note.** A document whose stamp names a non-default index is re-indexed into the knowledge index and re-stamped. Chunks in the old index are left in place (owner 2026-11-02) and are no longer reachable through relocation.