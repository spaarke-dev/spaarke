# Test diet report: spaarkeai-word-add-in-r1

**Run date:** 2026-11-24 (task 090, `/test-diet`, ADR-038 §7)
**Branch:** work/spaarkeai-word-add-in-r1. Files were read from origin/master 9211656b6e, where all of this project's merged work lives.

**Scope.** The test files added or modified by this project's 48 merged PRs. Those are the PRs whose head branch is `work/spaarkeai-word-add-in-r1*`, `word-addin-*` or this project's `fix/` and `feat/invoice-*` branches, merged from 2026-09-04 on. Each PR's contribution is its own diff, `mergeCommit^1..mergeCommit`. That gives 222 test files, of which **215 still exist** (89 .NET, 126 jest). Per the touch-radius rule, every method in each of those files is classified, not only the changed ones.

**Project rules applied:**
- ADR-038 Path A (CLAUDE.md, 2026-09-09) treats `src/client/office-addins/**/__tests__/**` as KEEP-equivalent, so those tests were classified by content.
- The jest files in `src/client/shared/**` and `src/client/pcf/**` have no front-end KEEP category, so they were judged by content and never marked PATH-VIOLATION.
- `tests/unit/Sprk.Bff.Api.Tests/**` files that were already on master on 2026-10-12 are grandfathered.

**This report is read-only.** None of the `git rm`, `git mv` or method-delete suggestions below was applied. Each is the reviewer's decision (ADR-038 / FR-B09).

## Summary

| Class | .NET (89 files) | jest part 1 (63) | jest part 2 (63) | **Total** | Action |
|---|---|---|---|---|---|
| MAINTAIN | 908 | 793 | 872 | **2573** | Confirmed, no action |
| FITNESS FUNCTION (ArchTests) | 106 | 0 | 0 | **106** | Keep |
| SCAFFOLDING (delete candidate) | 9 | 18 | 3 | **30** | Reviewer: see the slices |
| AMBIGUOUS | 16 | 31 | 15 | **62** | Reviewer judgment |
| ORPHAN | 0 | 0 | 0 | **0** | — |
| DETACHED | 0 | 0 | 0 | **0** | — |
| PATH-VIOLATION | 21 | 0 | 0 | **21** | Reviewer: proposed `git mv` to `tests/unit/domain/Office/` |
| **Total methods** | **1060** | **842** | **890** | **2792** | — |

**Escalation guard (task 032, NFR-02): not triggered.** The matter-denied tests in `tests/integration/contract/Api/Ai/VisualizationRowAuthorizationContractTests.cs` and `RecordSearchRowAuthorizationContractTests.cs` drive the real endpoints and assert the response. They are **MAINTAIN**.

**Required action (stale registry entry).** `tests/.reliability-registry.json` still lists `JobStatusSseIntegrationTests.PublishStatusUpdate_LatencyUnder1Second_MeetsSpecRequirement`. That test no longer exists; commit 85420b416a deleted it in an earlier diet. The registry's `_exitRule` says a stale entry must be removed. Task 090 may change only `projects/**`, so the removal is handed to the reviewer along with the deletions below. The surviving Stopwatch test, `PublishStatusUpdate_HighVolumeUpdates_MaintainsLatency`, is not registered and is classed AMBIGUOUS (leans delete).

**Found in passing (not a classification):** `EntityPicker` (only re-exported from `components/index.ts`) and `TaskPaneHeader` (no longer rendered; only its `HostType` type is imported) are dead components. Removing each one together with its test file is cleaner than pruning their tests one by one.

**Tests this project deleted** (already gone, listed for the record): `matterTypeLookupService.cache.test.ts` (#1301), `shareLinkService.test.ts` (#1292), `tests/e2e/specs/outlook-addins/share-flow.spec.ts` (#1052), `OfficeMatterTypeMappingTests.cs` (#1301), `OfficeJobStatusServiceTests.cs` (#1085). Each went with the production code or route it covered.

## Count delta

- Methods in scope: 2792 (touch radius; the project wrote most of the office-addins and Office-contract tests).
- MAINTAIN + FITNESS: 2679.
- If the reviewer confirms every SCAFFOLDING delete: 2792 − 30 = **2762**. The AMBIGUOUS group can move that by up to 62 more.

## Detail by slice

The three sections below are the classifiers' full output: every non-MAINTAIN method with its ban, reason and suggested action, and a per-file line.

Build-vs-maintain criteria per ADR-038 §7: the 17-ban classifier B1–B17 (Beck, "delete the scaffolding"; Feathers on characterization tests; Google test sizes).

---

## Slice A: .NET

## Test-diet slice A: .NET tests (spaarkeai-word-add-in-r1, task 090)

Files covered: 89 (read from origin/master). Methods classified: 1060.
Counts: MAINTAIN 908, FITNESS 106, SCAFFOLDING 9, AMBIGUOUS 16, ORPHAN 0, DETACHED 0, PATH-VIOLATION 21, ESCALATE 0, STALE-REGISTRY 1.

Method of work: every method was enumerated with its assertion, mock and Verify counts, and every name was read against heuristics 2-14. Method bodies were read for every method that tripped a heuristic (name shape, assertion count of 0 or 1, Verify-only, mock-heavy, constant or default pinning, serializer or enum round-trip, reflection or source scan). Methods that were not flagged were classified from name plus counts only, which is the main limit on this report. Nothing in the repo was deleted, moved or edited.

Grandfather check (`git cat-file -e e91218e1f:<path>`): 20 files under `tests/unit/Sprk.Bff.Api.Tests/**` are grandfathered; 2 are not (OfficeEmailSentDateEnrichmentTests and OfficeEntitySearchWorkAssignmentTests, PATH-VIOLATION below). `RecordOwnershipResolverDouble.cs` and `MinimalDocx.cs` hold no tests.

Escalation guard (NFR-02 negative authorization): checked `VisualizationRowAuthorizationContractTests.cs` (11 methods, including `Related_WhenCallerIsDeniedReadOnAMatter_ServesNoneOfThatMattersDocuments`, `Related_WhenCallerIsDeniedReadOnEveryNeighbour_ServesNoRowsAndAZeroCount`, `Related_WithWriteButNotRead_ServesNothing`) and `RecordSearchRowAuthorizationContractTests.cs` (`ARecordTheCallerCannotRead_IsNotReturned_AndIsNotCounted`). All drive production endpoints through the test host with real response assertions: MAINTAIN. Nothing of that kind classifies as SCAFFOLDING or DETACHED, so there are no ESCALATE items. Also MAINTAIN: `OfficeEntitySearchAuthorizationContractTests` (`SearchEntities_WhenCallerIsDeniedReadOnAMatter_ServesNeitherThatMatterNorItsCount`) and `OfficeTodoSourceAuthorizationContractTests`.

Reliability registry (`tests/.reliability-registry.json`, `_lastUpdated` 2026-10-12): the only entry that touches these files is `Sprk.Bff.Api.Tests.Integration.JobStatusSseIntegrationTests.PublishStatusUpdate_LatencyUnder1Second_MeetsSpecRequirement`. That method no longer exists; it was deleted in commit 85420b416a (test-diet 082b). The same file now holds `PublishStatusUpdate_HighVolumeUpdates_MaintainsLatency`, which is wall-clock dependent (Stopwatch, p95 under 200 ms) and is NOT in the registry. Report: STALE-REGISTRY (the entry names a deleted test). Action for the owner: remove the entry; the surviving timing test is handled in the AMBIGUOUS table below.

### Non-MAINTAIN methods

| file | method | class | ban(s) | reason | suggested action |
|---|---|---|---|---|---|
| tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs | JobType_ReturnsCorrectValue | SCAFFOLDING | B6, B16 | asserts a constant equals its literal | delete method JobType_ReturnsCorrectValue |
| same | JobTypeName_Constant_HasExpectedValue | SCAFFOLDING | B6, B16 | same constant, same literal | delete method JobTypeName_Constant_HasExpectedValue |
| same | ProcessAsync_NewJob_ChecksIdempotency | SCAFFOLDING | B7, B9 | only a Verify of one idempotency call; outcome tests cover the path | delete method ProcessAsync_NewJob_ChecksIdempotency |
| same | ProcessAsync_NewJob_AcquiresProcessingLock | SCAFFOLDING | B7, B9 | only a Verify of one lock call | delete method ProcessAsync_NewJob_AcquiresProcessingLock |
| same | ProcessAsync_NewJob_CallsAnalysisService | SCAFFOLDING | B7, B9 | only a Verify of one delegated call | delete method ProcessAsync_NewJob_CallsAnalysisService |
| same | AppOnlyDocumentAnalysisPayload_CanBeCreated | SCAFFOLDING | B16 | auto-property round-trip on a DTO | delete method AppOnlyDocumentAnalysisPayload_CanBeCreated |
| same | AppOnlyDocumentAnalysisPayload_SerializesCorrectly | SCAFFOLDING | B12 | default System.Text.Json round-trip of a DTO | delete method AppOnlyDocumentAnalysisPayload_SerializesCorrectly |
| same | ProcessAsync_NewJob_Success_MarksAsProcessed | AMBIGUOUS | B7 | Verify-only, but the only check that success is recorded | reviewer judgment; keep unless an outcome test also asserts it |
| same | ProcessAsync_NewJob_Success_ReleasesLock | AMBIGUOUS | B7 | Verify-only; releasing the lock is real behaviour | reviewer judgment |
| same | ProcessAsync_WithCustomPlaybook_PassesPlaybookToService | AMBIGUOUS | B9 | Verify-only on argument passing, but the argument is the behaviour | reviewer judgment |
| same | ProcessAsync_DuplicateJob_AlreadyProcessed_DoesNotCallAnalysisService | AMBIGUOUS | B7 | Verify Times.Never; a meaningful negative | reviewer judgment; lean keep |
| same | ProcessAsync_AnalysisFails_StillReleasesLock | AMBIGUOUS | B7 | Verify-only; failure-path cleanup is real behaviour | reviewer judgment; lean keep |
| tests/unit/Sprk.Bff.Api.Tests/Services/Office/JobStatusServiceTests.cs | JobStatusUpdate_HasDefaultTimestamp | SCAFFOLDING | B16 | tests a DTO default initializer | delete method JobStatusUpdate_HasDefaultTimestamp |
| same | JobStatusUpdateType_HasAllExpectedValues | SCAFFOLDING | B11, B16 | assigns an enum to a DTO and reads it back | delete method JobStatusUpdateType_HasAllExpectedValues |
| tests/unit/Sprk.Bff.Api.Tests/Integration/JobStatusSseIntegrationTests.cs | PublishStatusUpdate_HighVolumeUpdates_MaintainsLatency | AMBIGUOUS | B7, Stopwatch ban | measures wall-clock latency of a mocked Redis publish; unregistered timing test | reviewer judgment; lean delete method. The file also overlaps JobStatusServiceTests on publish, health and fail-open paths |
| tests/unit/Sprk.Bff.Api.Tests/Api/ConfigEndpointsClientConfigTests.cs | WireShape_IsAdditive_CamelCaseField | AMBIGUOUS | B12 | asserts the framework's camelCase output, but also pins the existing contract fields | reviewer judgment |
| tests/unit/Sprk.Bff.Api.Tests/Filters/Office/RateLimitFilterTests.cs | Filter_RateLimitExceeded_LogsWarning | AMBIGUOUS | B9 | Verify of one logger call; also depends on DateTime.UtcNow segment arithmetic | reviewer judgment |
| tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentIdentityByIdResolutionTests.cs | Retrieve_IsByIdOnSprkDocument_AndAsksForTheNamesAndAllFourDirectSlots | AMBIGUOUS | B9, B17 | single Verify on the Dataverse retrieve (column projection) | reviewer judgment |
| tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentUrlIdentityResolutionTests.cs | AlternateKey_ReceivesTheRawSpeItemId_Uncanonicalized | AMBIGUOUS | B9 | single Verify on argument passing; pins a past defect | reviewer judgment; lean keep |
| tests/unit/domain/Dataverse/RecordOwnershipResolverTests.cs | RecordOwnershipParent_NormalisesItsTable_SoTwoSpellingsOfOneRowAreEqual | AMBIGUOUS | B11 | record equality plus `with`, but the trim and lowercase normaliser is hand-written | reviewer judgment; lean keep |
| tests/integration/auth/UnifiedAccessControl/OfficeSaveContainerProvenanceTests.cs | SaveRequest_ExposesNoPropertyThatNamesAStorageContainer | AMBIGUOUS | none (public reflection) | structural invariant over a DTO's public properties, fitness-function shaped, inside an auth path; guards a security rule | reviewer judgment; consider git mv to tests/Spaarke.ArchTests/ |
| tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs | NoBffSourceFileResolvesTenantFromTheRequest | AMBIGUOUS | none (source scan) | regex scan of BFF source; a fitness function placed in the tenant path | reviewer judgment; consider git mv to tests/Spaarke.ArchTests/ |
| tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs | CreatePrivilegeFor_IsTheLiveDataversePrivilegeName | AMBIGUOUS | B6 | asserts a production function returns a literal privilege name; the literal pins the live Dataverse name | reviewer judgment; lean keep (the 403 tests cover behaviour) |
| tests/integration/contract/Api/Office/OfficeTodoSourceAuthorizationContractTests.cs | CreateTodoPrivilege_IsTheLiveDataversePrivilegeName | AMBIGUOUS | B6, B16 | single-line assertion of a constant against a literal | reviewer judgment; lean keep |
| tests/integration/data-mutation/OfficeVersionSave/OfficeEmailDocumentCommunicationLinkTests.cs | EveryFilingResult_MapsToItsWireOutcome_AndNotApplicableToNone | AMBIGUOUS | B6, B14 | enum-to-enum mapping theory | reviewer judgment |
| tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEmailSentDateEnrichmentTests.cs | all 8 methods | PATH-VIOLATION | B-path | file added after 2026-10-12 outside a KEEP path; content is behavioural (Graph sent-date enrichment, fail-soft, cancellation) | git mv to tests/unit/domain/Office/OfficeEmailSentDateEnrichmentTests.cs (confirm the domain test project can reference the BFF types used) |
| tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEntitySearchWorkAssignmentTests.cs | all 13 methods | PATH-VIOLATION | B-path | file added after 2026-10-12 outside a KEEP path; content is behavioural; `EntityType_SerializesAsTheFriendlyName_TheClientMatchesOn` is B12-flavoured and could be dropped on the move | git mv to tests/unit/domain/Office/OfficeEntitySearchWorkAssignmentTests.cs |

### Observations that are not classifications

- Heuristic 2 (naming) tripped on nothing. Names are sentence-shaped scenario-and-outcome in every file.
- Zero-assertion counts in `DocumentEmailIdentityContractTests`, `DocumentIdentityContractTests` and the `OfficeQuickCreate*ContractTests` files come from assertion helpers (`ShouldBeTheSameAnswerWithoutACard`, `AssertFoldedAsync`); each was read and asserts a response.
- `JobStatusSseIntegrationTests` (grandfathered, 29 methods) overlaps `JobStatusServiceTests` (15) on publish, health and fail-open paths. Mocking the Redis boundary is acceptable, so these stay MAINTAIN; a reviewer may want to merge them.
- `RouteAuthorizationGuardTests.Ledger.cs` is 3,961 lines of ledger data and holds no methods.
- The front-end KEEP-category note does not apply to this slice (.NET only).

### Per file

file | methods | maintain count | note
tests/Spaarke.ArchTests/ADR010_DITests.cs | 5 | 0 | FITNESS FUNCTION (ArchTests): 5 FITNESS
tests/Spaarke.ArchTests/ActivityCurrentDisposalGuardTests.cs | 3 | 0 | FITNESS FUNCTION (ArchTests): 3 FITNESS
tests/Spaarke.ArchTests/PcfManifestKeyAttributeGuardTests.cs | 3 | 0 | FITNESS FUNCTION (ArchTests): 3 FITNESS
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs | 0 | 0 | ledger data partial of the route-authorization fitness function; no test methods
tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs | 85 | 0 | FITNESS FUNCTION (ArchTests): 85 FITNESS
tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs | 10 | 0 | FITNESS FUNCTION (ArchTests): 10 FITNESS
tests/integration/Shared/Office/MinimalDocx.cs | 0 | 0 | no test methods (helper)
tests/integration/auth/Office/CallerRecordAccessProbeRecordsTests.cs | 7 | 7 | all MAINTAIN
tests/integration/auth/UnifiedAccessControl/OfficeSaveContainerProvenanceTests.cs | 3 | 2 | 1 AMBIGUOUS (reflection over public DTO surface, structural)
tests/integration/contract/Api/Ai/RagSendToIndexIndexNameContractTests.cs | 5 | 5 | all MAINTAIN
tests/integration/contract/Api/Ai/RagSendToIndexReplaceStaleChunksContractTests.cs | 2 | 2 | all MAINTAIN
tests/integration/contract/Api/Ai/RecordSearchRowAuthorizationContractTests.cs | 1 | 1 | all MAINTAIN
tests/integration/contract/Api/Ai/SendToIndexAuthorizationContractTests.cs | 8 | 8 | all MAINTAIN
tests/integration/contract/Api/Ai/VisualizationBudgetWarningContractTests.cs | 3 | 3 | all MAINTAIN
tests/integration/contract/Api/Ai/VisualizationRowAuthorizationContractTests.cs | 11 | 11 | all MAINTAIN
tests/integration/contract/Api/Documents/DocumentCreateOwnershipContractTests.cs | 7 | 7 | all MAINTAIN
tests/integration/contract/Api/Documents/DocumentEmailIdentityContractTests.cs | 27 | 27 | all MAINTAIN
tests/integration/contract/Api/Documents/DocumentIdentityByIdContractTests.cs | 8 | 8 | all MAINTAIN
tests/integration/contract/Api/Documents/DocumentIdentityContractTests.cs | 10 | 10 | all MAINTAIN
tests/integration/contract/Api/Documents/DocumentProfileContractTests.cs | 5 | 5 | all MAINTAIN
tests/integration/contract/Api/Documents/RelatedRecordCardContractTests.cs | 4 | 4 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeEndpointAuthorizationContractTests.cs | 6 | 6 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs | 41 | 41 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeEntitySearchAuthorizationContractTests.cs | 11 | 11 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeFilingAccessParityContractTests.cs | 8 | 8 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeGenerateProfileContractTests.cs | 9 | 9 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeMatterTypeLookupContractTests.cs | 5 | 5 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs | 33 | 32 | 1 AMBIGUOUS (privilege-name literal pin)
tests/integration/contract/Api/Office/OfficeQuickCreateFieldsContractTests.cs | 14 | 14 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeQuickCreateProjectContractTests.cs | 16 | 16 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeSaveDocumentStampContractTests.cs | 8 | 8 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeSaveNoTargetContainerContractTests.cs | 16 | 16 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeSaveUnexpectedErrorContractTests.cs | 1 | 1 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeTodoRegardingContractTests.cs | 14 | 14 | all MAINTAIN
tests/integration/contract/Api/Office/OfficeTodoSourceAuthorizationContractTests.cs | 13 | 12 | 1 AMBIGUOUS (privilege-name literal pin)
tests/integration/data-mutation/OfficeVersionSave/OfficeCreateCollisionTests.cs | 9 | 9 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeEmailDocumentCommunicationLinkTests.cs | 29 | 28 | 1 AMBIGUOUS (enum-to-enum mapping theory)
tests/integration/data-mutation/OfficeVersionSave/OfficeEmailSaveNamingTests.cs | 6 | 6 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeEmailSentDateTests.cs | 5 | 5 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeImmutableSaveFileSafetyTests.cs | 4 | 4 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeSaveAsNewDocumentLinkGraduateTests.cs | 3 | 3 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeSaveSpineIdempotencyTests.cs | 6 | 6 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeTypedNameCollisionTests.cs | 5 | 5 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeVersionSaveAiRefreshPayloadTests.cs | 4 | 4 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeVersionSaveOneRowTests.cs | 4 | 4 | all MAINTAIN
tests/integration/data-mutation/OfficeVersionSave/OfficeVersionSaveRevertTests.cs | 9 | 9 | all MAINTAIN
tests/integration/data-mutation/RecordOwnership/OfficeRecordOwnershipTests.cs | 13 | 13 | all MAINTAIN
tests/integration/data-mutation/SpeUploadPaths/SpeFlatUploadPathTests.cs | 11 | 11 | all MAINTAIN
tests/integration/regression/Issue1038_OfficeSaveSecureRecordContainerTests.cs | 1 | 1 | all MAINTAIN
tests/integration/regression/Issue1075_QuickSaveLogicalNameTests.cs | 2 | 2 | all MAINTAIN
tests/integration/regression/Issue1084_OfficeJobRecordDurabilityTests.cs | 7 | 7 | all MAINTAIN
tests/integration/regression/Issue1086_OfficeBackgroundWorkRestartTests.cs | 12 | 12 | all MAINTAIN
tests/integration/regression/Issue975_ChatAttachmentValidationProblemJsonTests.cs | 1 | 1 | all MAINTAIN
tests/integration/regression/Issue975_OfficeJobStreamProblemJsonTests.cs | 3 | 3 | all MAINTAIN
tests/integration/regression/Issue975_ProblemJsonContentTypeTests.cs | 2 | 2 | all MAINTAIN
tests/integration/seam/Office/VersionSaveAiRefreshSeamTests.cs | 5 | 5 | all MAINTAIN
tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs | 8 | 7 | 1 AMBIGUOUS (source-scan fitness function in a tenant path)
tests/unit/Sprk.Bff.Api.Tests/Api/ConfigEndpointsClientConfigTests.cs | 4 | 3 | 1 AMBIGUOUS (serializer default shape, B12); grandfathered
tests/unit/Sprk.Bff.Api.Tests/Filters/DocumentUrlIdentityFilterTests.cs | 5 | 5 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Filters/IdempotencyFilterReplayGateTests.cs | 5 | 5 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Filters/Office/RateLimitFilterTests.cs | 9 | 8 | 1 AMBIGUOUS (logger Verify only); grandfathered
tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Graph/SharedItemResolutionTests.cs | 11 | 11 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Graph/SharingUrlTokenTests.cs | 7 | 7 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Integration/JobStatusSseIntegrationTests.cs | 29 | 28 | 1 AMBIGUOUS (Stopwatch over mocks); grandfathered; STALE-REGISTRY entry points here
tests/unit/Sprk.Bff.Api.Tests/Services/Communication/OutboundAttachmentArchiveTests.cs | 20 | 20 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentEmailIdentityResolutionTests.cs | 23 | 23 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentIdentityByIdResolutionTests.cs | 8 | 7 | 1 AMBIGUOUS (single Verify on projection columns); grandfathered
tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentUrlIdentityResolutionDisplayFieldTests.cs | 8 | 8 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentUrlIdentityResolutionTests.cs | 18 | 17 | 1 AMBIGUOUS (single Verify on argument passing); grandfathered
tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/AppOnlyDocumentAnalysisJobHandlerTests.cs | 22 | 10 | 7 SCAFFOLDING, 5 AMBIGUOUS; grandfathered
tests/unit/Sprk.Bff.Api.Tests/Services/Office/JobStatusServiceTests.cs | 15 | 13 | 2 SCAFFOLDING (DTO default and enum round-trip); grandfathered
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeDocumentPersistenceDedupTests.cs | 5 | 5 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeDocumentPersistenceEditableDedupTests.cs | 11 | 11 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEmailClientContentTests.cs | 4 | 4 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEmailSentDateEnrichmentTests.cs | 8 | 0 | PATH-VIOLATION (added after 2026-10-12 under tests/unit/Sprk.Bff.Api.Tests); content is behavioural
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEntitySearchMappingTests.cs | 6 | 6 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeEntitySearchWorkAssignmentTests.cs | 13 | 0 | PATH-VIOLATION (added after 2026-10-12); content is behavioural
tests/unit/Sprk.Bff.Api.Tests/Services/Office/OfficeReferenceListMappingTests.cs | 10 | 10 | all MAINTAIN
tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordOwnershipResolverDouble.cs | 0 | 0 | no test methods (helper)
tests/unit/domain/Ai/BulkRagIndexingJobHandlerReplaceStaleChunksTests.cs | 6 | 6 | all MAINTAIN
tests/unit/domain/Ai/DeliverToIndexNodeExecutorReplaceStaleChunksTests.cs | 2 | 2 | all MAINTAIN
tests/unit/domain/Ai/PostUploadIndexingEnqueuerReplaceStaleChunksTests.cs | 4 | 4 | all MAINTAIN
tests/unit/domain/Ai/RagServiceChunkTrimTests.cs | 8 | 8 | all MAINTAIN
tests/unit/domain/Dataverse/DocumentEntityMappingTests.cs | 6 | 6 | all MAINTAIN
tests/unit/domain/Dataverse/RecordOwnershipResolverTests.cs | 199 | 198 | 1 AMBIGUOUS (record normalisation, B11 flavour)
tests/unit/domain/Office/OfficeDocumentStampTests.cs | 22 | 22 | all MAINTAIN
tests/unit/domain/Office/OfficeJobEffectiveStateTests.cs | 10 | 10 | all MAINTAIN
tests/unit/domain/Office/UploadFinalizationOwnerTeamTests.cs | 10 | 10 | all MAINTAIN
tests/unit/domain/Office/UploadSessionManagerActivityLifetimeTests.cs | 1 | 1 | all MAINTAIN

---

## Slice B: jest, part 1

## Test-diet slice B / js1 - 63 jest files (office-addins), read from origin/master

Files covered: 63 (list_js1.txt). Methods: 842 (it.each counted once).

MAINTAIN 793 | FITNESS 0 | SCAFFOLDING 18 | AMBIGUOUS 31 | ORPHAN 0 | DETACHED 0 | PATH-VIOLATION 0 | ESCALATE 0 | STALE-REGISTRY 0

Path rule: office-addins/**/__tests__ is KEEP-equivalent (project decision 2026-09-09, Path A); classified by content only. The reliability registry is .NET-only, so no STALE-REGISTRY check applies. The NFR-02 escalation guard does not apply (no .NET authorization test in this slice).

Method: all test names read for all 63 files; bodies read for every test flagged here plus a zero-assertion scan of every it-block (only the files listed below had assertion-free bodies, plus helper-assert cases that were checked and kept). Remaining tests are not individually body-read and are MAINTAIN on name/scenario/outcome shape and the scan.

### Non-MAINTAIN methods

file | method | class | ban(s) | reason | suggested action
---|---|---|---|---|---
src/client/office-addins/shared/services/__tests__/ApiClient.test.ts | configure > should configure with baseUrl and bffApiClientId | SCAFFOLDING | B10 | ends in expect(true).toBe(true); asserts nothing about configure | delete method
src/client/office-addins/shared/services/__tests__/ApiClient.test.ts | ApiClientError > should create error with ApiError details | AMBIGUOUS | B16 | constructor property round-trip, but message-from-detail rule is real | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | getHostType > should return "outlook" | SCAFFOLDING | B6,B16 | one-line assertion of a constant return | delete method
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | getItemType > should return "email" | SCAFFOLDING | B6,B16 | one-line assertion of a constant return | delete method
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getItemId > should return the item ID | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getSubject > should return the subject | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getSenderEmail > should return sender email | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getInternetMessageId > should return the internet message ID | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getConversationId > should return the conversation ID | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | read: getImportance > should return normal importance | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | compose: getItemId > should return item ID asynchronously | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | compose: getSubject > should return subject asynchronously | AMBIGUOUS | B6,B9 | pass-through of one mocked Office item field (host-boundary mock is legitimate; assertion is 1:1 with the getter) | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | getHostType > should return "word" | SCAFFOLDING | B6,B16 | one-line assertion of a constant return | delete method
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | getItemType > should return "document" | SCAFFOLDING | B6,B16 | one-line assertion of a constant return | delete method
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | getAttachments > should return empty array (not applicable for Word) | AMBIGUOUS | B6 | constant stub, but records the not-applicable contract | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | getSenderEmail > should return empty string (not applicable for Word) | AMBIGUOUS | B6 | constant stub, but records the not-applicable contract | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | getRecipients > should return empty array (not applicable for Word) | AMBIGUOUS | B6 | constant stub, but records the not-applicable contract | reviewer judgment
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | isInitialized > should return false before initialization | AMBIGUOUS | B16 | default-state getter; the paired "true after initialize" test carries the behaviour | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | search behavior > shows empty state when no results | SCAFFOLDING | B10 | body ends with a comment; no assertion at all | delete method
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | accessibility > filter chips indicate checked state | SCAFFOLDING | B10 | no assertion; comment only | delete method
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | initial render > renders with default placeholder | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | initial render > renders with custom placeholder | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | initial render > renders with label | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | accessibility > has accessible name | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | accessibility > uses label as accessible name | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | accessibility > filter chips have checkbox role | AMBIGUOUS | B16,B10 | prop echoed to DOM / element-exists check with no logic (checkbox test only asserts length > 0) | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Duplicate Detection > handles duplicate response correctly | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Error Handling > displays error message on API failure | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Success State > renders success UI after completion | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Success State > handles view document action | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Success State > handles copy link action | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Success State > handles save another action | SCAFFOLDING | B10,B13 | empty body (comment "would require simulating the full save flow"); name claims a behaviour nothing checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Quick Create > calls onQuickCreate callback when triggered | SCAFFOLDING | B10,B13 | only asserts the form renders; never triggers or checks the callback | delete method
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Initial Render > renders save button | AMBIGUOUS | B10 | single element-exists check | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | Accessibility > has proper ARIA labels | AMBIGUOUS | B10 | same form-role assertion as "renders save form with entity picker" | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx | shows Outlook icon for outlook host | SCAFFOLDING | B10,B13 | asserts the title text, not an icon; name claims more than the test checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx | shows Word icon for word host | SCAFFOLDING | B10,B13 | asserts the title text, not an icon; name claims more than the test checks | delete method
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx | renders with default title | AMBIGUOUS | B16 | default echo to text | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx | renders with custom title | AMBIGUOUS | B16 | prop echo to text | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneShell.test.tsx | accepts a title prop without breaking rendering (prop is currently unused) | SCAFFOLDING | B10 | pins a dead prop: asserts the text is NOT shown and children render; the test comment itself says deleting would be honest | delete method (and drop the dead title prop)
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneShell.test.tsx | renders footer with version | AMBIGUOUS | B16 | prop echo to text | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneShell.test.tsx | renders footer with app name | AMBIGUOUS | B16 | prop echo to text | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneNavigation.test.tsx | returns correct default tab for each host type | SCAFFOLDING | B6,B16 | two assertions that a function returns the constant "save" | delete method
src/client/office-addins/shared/taskpane/components/__tests__/LinkedTodosBanner.test.tsx | renders count for very large numbers | AMBIGUOUS | B16 | uses 42, not a large number; same path as the plural test | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/LinkedTodosBanner.test.tsx | accepts a className prop applied LAST so host overrides win | AMBIGUOUS | B16 | prop passthrough, but states a Spaarke convention | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/FindResultsList.test.tsx | never has a 360px maxHeight anywhere in the component source (regression guard) | AMBIGUOUS | fitness-style | source-text scan guard in a behavioural file; a real regression guard but not behaviour | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/SaveModeSection.test.tsx | renders under the dark theme (ADR-021) | AMBIGUOUS | B10 | smoke check: one button exists under the dark theme | reviewer judgment
src/client/office-addins/shared/taskpane/components/views/__tests__/EmailView.test.tsx | resolves the wrapper to the shared library source - not a local copy | AMBIGUOUS | B3 | module-resolution wiring check; protects the shared-lib reuse rule but not behaviour | reviewer judgment
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.invoiceTaskTypes.test.tsx | ALL_ENTITY_TYPES (the generic picker default) does not include WorkAssignment | AMBIGUOUS | B16 | constant-content assertion; a deliberate product decision | reviewer judgment

### Per file

file | methods | maintain count | note
---|---|---|---
src/client/office-addins/outlook/commands/__tests__/commands.emailContent.test.ts | 3 | 3 | behavioural; no findings
src/client/office-addins/outlook/commands/__tests__/commands.test.ts | 16 | 16 | behavioural; no findings
src/client/office-addins/packaging/__tests__/mergeUnifiedManifest.test.ts | 13 | 13 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/HostAdapterFactory.test.ts | 12 | 12 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.attachmentKinds.test.ts | 3 | 3 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.composeEmail.test.ts | 6 | 6 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.readDocumentStamp.test.ts | 3 | 3 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/OutlookAdapter.test.ts | 37 | 27 | 10 flagged (see table)
src/client/office-addins/shared/adapters/__tests__/WordAdapter.composeEmail.test.ts | 2 | 2 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/WordAdapter.documentChangeDetection.test.ts | 7 | 7 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/WordAdapter.readDocumentStamp.test.ts | 18 | 18 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/WordAdapter.test.ts | 43 | 37 | 6 flagged (see table)
src/client/office-addins/shared/adapters/__tests__/WordAdapter.writeDocumentStamp.test.ts | 10 | 10 | behavioural; no findings
src/client/office-addins/shared/adapters/__tests__/capabilities.test.ts | 11 | 11 | capability matrix tests kept (NFR-10 gating contract); flag-by-flag but consumed by gating logic
src/client/office-addins/shared/adapters/__tests__/documentStampContract.test.ts | 2 | 2 | cross-language contract (client constants vs server C# source); kept as MAINTAIN
src/client/office-addins/shared/services/__tests__/ApiClient.test.ts | 20 | 18 | 2 flagged (see table)
src/client/office-addins/shared/taskpane/__tests__/App.emailIdentity.test.tsx | 9 | 9 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/AddFirmPanel.test.tsx | 24 | 24 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/AttachmentSelector.saveLimit.test.tsx | 2 | 2 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/CreateRecordForm.test.tsx | 12 | 12 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/DocumentProfileSection.resize.test.tsx | 6 | 6 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/DocumentProfileSection.test.tsx | 18 | 18 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/EntityPicker.test.tsx | 32 | 24 | EntityPicker is exported only from components/index.ts; no live view renders it (SaveFlow uses RelatedToPicker). Dead component: consider removing component + test together
src/client/office-addins/shared/taskpane/components/__tests__/FindResultsList.openAffordance.test.tsx | 5 | 5 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/FindResultsList.test.tsx | 32 | 31 | 1 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/FindSplitPane.test.tsx | 7 | 7 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/LinkedTodosBanner.test.tsx | 21 | 19 | 2 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/RelatedRecordCard.test.tsx | 11 | 11 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/RelatedToPicker.errorSurfacing.test.tsx | 8 | 8 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/RelatedToPicker.filingAccess.test.tsx | 8 | 8 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/RelatedToPicker.layout.test.tsx | 14 | 14 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/RelatedToPicker.matterType.test.tsx | 11 | 11 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.alreadySaved.test.tsx | 10 | 10 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.buttonFeedback.test.tsx | 5 | 5 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.captureAtSave.test.tsx | 7 | 7 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.copyRecordLink.test.tsx | 2 | 2 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.documentName.test.tsx | 15 | 15 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.emailAlreadySaved.test.tsx | 6 | 6 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.emailFilingNotice.test.tsx | 5 | 5 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.filingAccess.test.tsx | 3 | 3 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.invoiceTaskTypes.test.tsx | 5 | 4 | 1 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.matterTypeQuickCreate.test.tsx | 3 | 3 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.openRecord.test.tsx | 8 | 8 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.quickCreateErrorSurfacing.test.tsx | 4 | 4 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.round5.test.tsx | 5 | 5 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.savedState.test.tsx | 25 | 25 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.test.tsx | 23 | 14 | oldest SaveFlow file; 7 empty/placeholder tests; real behaviour lives in the SaveFlow.* split files
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlow.versionMode.test.tsx | 8 | 8 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveFlowCollision.test.tsx | 14 | 14 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/SaveModeSection.test.tsx | 11 | 10 | 1 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneExpand.test.tsx | 4 | 4 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneHeader.test.tsx | 9 | 5 | TaskPaneHeader is no longer rendered (replaced by TaskPaneToolbar, task 015); only its HostType type is still imported. Consider removing component + test together
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneNavigation.test.tsx | 19 | 18 | 1 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneShell.test.tsx | 10 | 7 | 3 flagged (see table)
src/client/office-addins/shared/taskpane/components/__tests__/TaskPaneToolbar.fit.test.tsx | 9 | 9 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/CreateTodoView.test.tsx | 9 | 9 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/EmailView.test.tsx | 13 | 12 | 1 flagged (see table)
src/client/office-addins/shared/taskpane/components/views/__tests__/FindView.test.tsx | 41 | 41 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/InvoiceView.addFirm.test.tsx | 36 | 36 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/InvoiceView.correctionNote.test.tsx | 13 | 13 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/InvoiceView.test.tsx | 86 | 86 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.captureAtSave.test.tsx | 5 | 5 | behavioural; no findings
src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.documentChangeDetection.test.tsx | 3 | 3 | behavioural; no findings

---

## Slice C: jest, part 2

## Test diet slice C (jest part 2): diet-C-js2

Files covered: 63 (list_js2.txt). Methods classified: 890 (it/test blocks; each it.each counted as one block).

Counts: MAINTAIN 872, FITNESS 0, SCAFFOLDING 3, AMBIGUOUS 15, ORPHAN 0, DETACHED 0, PATH-VIOLATION 0, ESCALATE 0, STALE-REGISTRY 0.

Method (not file) basis: all production imports of every file resolve on origin/master (no ORPHAN). Jest files have no .NET reliability-registry entries. No Task 032 NFR-02 authorization test is in this slice. Path rule: no front-end KEEP category (ADR-038 Path B amendment suggested) (applies to src/client/shared/** and src/client/pcf/** files; office-addins/**/__tests__ is KEEP-equivalent by project decision 2026-09-09).

### Non-MAINTAIN methods

| file | method | class | ban(s) | reason | suggested action |
|---|---|---|---|---|---|
| src/client/office-addins/shared/taskpane/components/views/__tests__/ShareView.test.tsx | renders search section | AMBIGUOUS | B10 | presence-only render check of static text/placeholder; no behaviour | reviewer: delete method if the interaction tests below are deemed sufficient |
| src/client/office-addins/shared/taskpane/components/views/__tests__/ShareView.test.tsx | renders search button | AMBIGUOUS | B10 | presence-only render check of a button | reviewer: delete method if redundant with "disables search button when search query is empty" |
| src/client/office-addins/shared/taskpane/components/views/__tests__/ShareView.test.tsx | displays error message when error prop is set | AMBIGUOUS | B9 | prop echoed to text; thin, but it is the only error-display check | reviewer judgment |
| src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | forwards both when supplied (savedState / onSavedStateChange) | AMBIGUOUS | B7, B9 | mocked SaveFlow; asserts identity of two forwarded props only | reviewer judgment |
| src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | omits both entirely when not supplied (savedState / onSavedStateChange) | AMBIGUOUS | B7, B9 | mocked SaveFlow; asserts key absence on pass-through | reviewer judgment |
| src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | forwards (onComplete through its stamp-writing wrapper) onSaved, onQuickCreate, ... | AMBIGUOUS | B7, B9 | mostly prop identity on a mocked child; the onComplete-wrapper assertion is behavioural | reviewer judgment; keep at least the onComplete assertion |
| src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | omits those optional props entirely (not just undefined-valued) when not supplied | AMBIGUOUS | B7, B9 | mocked SaveFlow; key-absence assertions | reviewer judgment |
| src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | uses the provided getAccessToken | AMBIGUOUS | B7, B9 | asserts toBe on a forwarded function; pure pass-through | reviewer judgment |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useAnnounce.test.ts | live regions are visually hidden | AMBIGUOUS | B12 | asserts literal inline-style values (1px, absolute); the screen-reader-only contract is real but the check is style-literal | reviewer judgment |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useEntitySearch.test.ts | returns initial state with empty query | AMBIGUOUS | B16 | default-value round-trip of a hook; no scenario | reviewer judgment |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useEntitySearch.test.ts | ALL_ENTITY_TYPES contains all entity types | SCAFFOLDING | B6, B16 | asserts a constant equals its own literal list (mirror) | delete method ALL_ENTITY_TYPES contains all entity types |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.test.ts | starts with idle flow state | AMBIGUOUS | B16 | initial-state defaults; includes isValid=true design note | reviewer judgment |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.test.ts | has empty attachment selection | AMBIGUOUS | B16 | single default-value assertion | reviewer judgment |
| src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.test.ts | defaults to include body | AMBIGUOUS | B16 | single default-value assertion | reviewer judgment |
| src/client/office-addins/shared/taskpane/services/__tests__/openRecordLauncher.test.ts | with the real (default) click — invokes HTMLAnchorElement.click() without throwing | SCAFFOLDING | B10 | only assertion is not.toThrow(); the test comment says it just proves the default path is wired up | delete method with the real (default) click ... |
| src/client/office-addins/shared/taskpane/utils/__tests__/invoiceSubmissionMessages.firmAssign.test.ts | every sentence the table holds is a plain string (no function, so no input but the code) | SCAFFOLDING | B11 | typeof === string over a table the type system already constrains | delete method every sentence the table holds is a plain string |
| src/client/office-addins/word/commands/__tests__/commands.test.ts | completes immediately (showTaskPane) | AMBIGUOUS | B6 | 1:1 mirror of a one-line command; but a ribbon command that never completes hangs the host | reviewer judgment |
| src/client/shared/Spaarke.Auth/tests/InMemoryCache.test.ts | exposes a composite name including the inner strategy | AMBIGUOUS | B16 | asserts a name getter returns a literal; diagnostic label only | reviewer judgment |

### Per file

file | methods | maintain count | note
src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.emailContentCapture.test.tsx | 5 | 5 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.identityStamp.test.tsx | 3 | 3 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/components/views/__tests__/SaveView.test.tsx | 20 | 15 | KEEP-equivalent (Path A); judged by content; 5 flagged above
src/client/office-addins/shared/taskpane/components/views/__tests__/ShareView.test.tsx | 20 | 17 | KEEP-equivalent (Path A); judged by content; 3 flagged above
src/client/office-addins/shared/taskpane/hooks/__tests__/useAnnounce.test.ts | 19 | 18 | KEEP-equivalent (Path A); judged by content; 1 flagged above
src/client/office-addins/shared/taskpane/hooks/__tests__/useEntitySearch.test.ts | 21 | 19 | KEEP-equivalent (Path A); judged by content; 2 flagged above
src/client/office-addins/shared/taskpane/hooks/__tests__/useFindRecordMatches.test.tsx | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useLazyResults.test.tsx | 8 | 8 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useRelatedRecord.test.ts | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.emailContent.test.tsx | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.emailFiling.test.ts | 4 | 4 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.emailName.test.ts | 6 | 6 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.terminalOutcome.test.ts | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.test.ts | 28 | 25 | KEEP-equivalent (Path A); judged by content; 3 flagged above
src/client/office-addins/shared/taskpane/hooks/__tests__/useSaveFlow.versionSave.test.ts | 15 | 15 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/communicationSuggestionsService.test.ts | 17 | 17 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentFilingService.test.ts | 8 | 8 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentIdentityService.emailIdentity.test.ts | 13 | 13 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentIdentityService.stampById.test.ts | 5 | 5 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentIdentityService.stampPrecedence.test.ts | 11 | 11 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentIdentityService.stampWrite.test.ts | 5 | 5 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/documentIdentityService.test.ts | 27 | 27 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/emailContentCapture.test.ts | 20 | 20 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/invoiceSubmissionSeam.correctionNote.test.ts | 9 | 9 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/invoiceSubmissionSeam.test.ts | 24 | 24 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/invoiceSubmissionService.addFirm.test.ts | 18 | 18 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/invoiceSubmissionService.correctionNote.test.ts | 4 | 4 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/invoiceSubmissionService.test.ts | 69 | 69 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/openRecordLauncher.test.ts | 20 | 19 | KEEP-equivalent (Path A); judged by content; 1 flagged above
src/client/office-addins/shared/taskpane/services/__tests__/openRecordLauncher.webOpen.test.ts | 9 | 9 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/paneEmailService.test.ts | 16 | 16 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/quickSaveHelpers.test.ts | 40 | 40 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/referenceListService.cache.test.ts | 14 | 14 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/sendEmailService.test.ts | 17 | 17 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/taskPaneExpand.test.ts | 15 | 15 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/services/__tests__/taskPaneWidthService.test.ts | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/cleanGuid.test.ts | 7 | 7 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/copyText.test.ts | 4 | 4 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/documentFileName.test.ts | 2 | 2 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/errorMessages.collision.test.ts | 11 | 11 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/errorMessages.fetchFailure.test.ts | 6 | 6 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/invoiceSubmissionMessages.correctionNote.test.ts | 3 | 3 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/shared/taskpane/utils/__tests__/invoiceSubmissionMessages.firmAssign.test.ts | 11 | 10 | KEEP-equivalent (Path A); judged by content; 1 flagged above
src/client/office-addins/shared/taskpane/utils/__tests__/invoiceSubmissionMessages.test.ts | 35 | 35 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/word/commands/__tests__/commands.test.ts | 35 | 34 | KEEP-equivalent (Path A); judged by content; 1 flagged above
src/client/office-addins/word/commands/__tests__/notify.test.ts | 15 | 15 | KEEP-equivalent (Path A); judged by content
src/client/office-addins/word/commands/__tests__/quickSaveDialog.test.ts | 24 | 24 | KEEP-equivalent (Path A); judged by content
src/client/pcf/SpeDocumentViewer/control/__tests__/BffClient.test.ts | 14 | 14 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/pcf/SpeDocumentViewer/control/__tests__/authInit.test.ts | 16 | 16 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/BrowserMsalStrategy.test.ts | 17 | 17 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/InMemoryCache.test.ts | 16 | 15 | no front-end KEEP category (ADR-038 Path B amendment suggested); 1 flagged above
src/client/shared/Spaarke.Auth/tests/authenticatedFetch.test.ts | 4 | 4 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/bffClientConfig.test.ts | 8 | 8 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/config.test.ts | 24 | 24 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/dataverseOrigin.test.ts | 3 | 3 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/initAuth.tenantDiscovery.test.ts | 19 | 19 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.Auth/tests/tenant.test.ts | 16 | 16 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/AttachmentList.test.tsx | 13 | 13 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/documentRecordLink.test.ts | 9 | 9 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/linkToRecordSend.test.tsx | 2 | 2 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/shareLinks.test.ts | 7 | 7 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/__tests__/wrappers.test.tsx | 17 | 17 | no front-end KEEP category (ADR-038 Path B amendment suggested)
src/client/shared/Spaarke.UI.Components/src/services/__tests__/AppInsightsService.test.ts | 7 | 7 | no front-end KEEP category (ADR-038 Path B amendment suggested)
