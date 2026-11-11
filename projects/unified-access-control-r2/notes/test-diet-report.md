# Test diet report — unified-access-control-r2

**Run date**: 2026-11-11 · **Branch**: `task/uac-090-wrapup` (cut from `work/unified-access-control-r2` at `631a25787c`, merged with origin/master) · **Task**: 090 wrap-up
**Scope**: every `tests/**/*.cs` file added or modified (`--diff-filter=AM`) by a commit whose message matches `uac|unified-access`, from the project's first commit `be6d54309f` (2026-08-19) to HEAD, that still exists. Most of this project merged through task PRs straight to master, so the branch range alone would miss them. The message filter also catches a few other projects' commits that cite uac-r2 (for example ontology re-checks); they are in scope because they were made for this project's reviews.
**Method**: touch-radius expansion (every method in a touched file). Heuristic 0 (ArchTests = fitness function) and heuristic 1 (KEEP path; files on master at `e91218e1f` on 2026-10-12 are grandfathered) applied per file; heuristics 1, 3, 4 and 10 (B1, B3/B4, B8) applied mechanically by a source scan; heuristic 2 (B13 names) checked mechanically. **Heuristics 5–9 and 11–14 (mirror, pass-through, coverage filler, language feature, snapshot, generator, setup ratio, orphan, detached) were NOT applied method by method** — 15,266 methods in 935 files is beyond a wrap-up pass. Anything those heuristics would find is therefore neither confirmed nor ruled out; see "Not done".

## Summary

| Class | Files | Test methods | Action |
|---|---|---|---|
| FITNESS FUNCTION (`tests/Spaarke.ArchTests/**`) | 89 | 776 | KEEP (heuristic 0) |
| MAINTAIN (KEEP path, or grandfathered pre-2026-10-12 file; no mechanical ban hit) | 679 | 10974 | confirmed at the mechanical level |
| SCAFFOLDING, high confidence (DELETE) | 0 | 0 | **none** — no file or method met a ban without a behavioural reason to keep it |
| AMBIGUOUS (a B1/B3/B8 pattern present in the file) | 65 | 1485 | owner/reviewer judgment, listed below |
| PATH-VIOLATION (added after 2026-10-12 outside a KEEP path) | 102 | 2031 | `git mv` decision for the owner, listed below |
| ORPHAN / DETACHED | not assessed | — | see "Not done" |
| **Total touched** | **935** | **15266** | — |

## Delete commands

None. **No deletion was executed** and none is recommended at high confidence. The only wiring-shaped hits (B3 DI resolution plus B8 reflection) are the two `Infrastructure/DI/*RegistrationTests.cs` files, and both resolve the real container with scope validation on, which catches a captive scoped dependency at startup, and both shipped with fixes (`RefileWriterReconcilerRegistrationTests` with #2094; `GateDecisionRecordSharingRegistrationTests` with ontology 042b r2). That is a behavioural reason to keep them, so they are AMBIGUOUS, not scaffolding. The 30-file stop rule in the 090 brief therefore did not apply.

## Ambiguous — reviewer judgment

B1 = `Mock<HttpMessageHandler>` present (often a legitimate Graph/HTTP fake in an integration test); B3 = asserts a DI resolution; B8 = `BindingFlags.NonPublic` reflection. A file-level hit, not a per-method verdict.

| File | Methods | Added by project | Flags |
|---|---|---|---|
| `tests/integration/auth/ExternalAccess/ExternalMultiRootSecureRootsTests.cs` | 14 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/auth/Ontology/OntologyAdminAuthoringEndpointTests.cs` | 67 | modified | B3:DI-resolution-assert |
| `tests/integration/auth/Ontology/OntologyAdminAuthoringSecureHoldTests.cs` | 15 | yes | B3:DI-resolution-assert |
| `tests/integration/auth/UnifiedAccessControl/ExistingCiamAccountInviteTests.cs` | 67 | yes | B8:BindingFlags.NonPublic |
| `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` | 50 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/auth/UnifiedAccessControl/ExternalModuleSavedQueryScopeTests.cs` | 16 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/auth/UnifiedAccessControl/GrantSendsInviteTests.cs` | 20 | yes | B8:BindingFlags.NonPublic |
| `tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactBindingDecisionTests.cs` | 107 | yes | B8:BindingFlags.NonPublic |
| `tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderOwnerTeamTests.cs` | 32 | modified | B8:BindingFlags.NonPublic |
| `tests/integration/contract/Api/Ai/ChatAgentRouteProofTests.cs` | 3 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Ai/ChatContextAuthorizationContractTests.cs` | 43 | yes | B8:BindingFlags.NonPublic |
| `tests/integration/contract/Api/Ai/ComposeCreateOnSaveEndpointContractTests.cs` | 14 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Ai/ComposeDispatchEndpointContractTests.cs` | 6 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Ai/ComposeWordShuttlePollEndpointContractTests.cs` | 9 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Compose/ComposeActiveDocumentContractTests.cs` | 12 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Compose/ComposeBodySessionRouteDenyContractTests.cs` | 8 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` | 8 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Compose/ComposeRouteSurfaceContractTests.cs` | 2 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Compose/ComposeSessionAndContainerAuthorizationContractTests.cs` | 14 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` | 28 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/ExternalAccess/ExternalModuleDataContractTests.cs` | 16 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/contract/Api/Office/OfficeQuickCreateRefusalMapTests.cs` | 13 | modified | B8:BindingFlags.NonPublic |
| `tests/integration/contract/Api/Reporting/ReportingCatalogBindingContractTests.cs` | 36 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/data-mutation/Communication/BudgetInquiryEmailFilingTests.cs` | 28 | modified | B8:BindingFlags.NonPublic |
| `tests/integration/data-mutation/DocumentContainer/DocumentContainerRelocatorTests.cs` | 99 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/data-mutation/DocumentContainer/DocumentVersionListPagingTests.cs` | 4 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/data-mutation/DocumentContainer/ProcessingLockRenewalTests.cs` | 5 | yes | B1:Mock<HttpMessageHandler> |
| `tests/integration/data-mutation/ExternalAccess/SecureChildOrdinaryParentMoveTests.cs` | 77 | yes | B8:BindingFlags.NonPublic |
| `tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.cs` | 68 | modified | B8:BindingFlags.NonPublic |
| `tests/integration/regression/Compose/Def14_ComposeSaveLockedDocumentTests.cs` | 8 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Ai/ComposeFidelitySeamTests.cs` | 2 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeCleanApplySeamTests.cs` | 3 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeOriginRoutingSeamTests.cs` | 10 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposePdfIntakeRoundTripSeamTests.cs` | 5 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeReferenceMapSessionLedgerSeamTests.cs` | 3 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeRefreshProfileSeamTests.cs` | 4 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeTransientKeyDedupSeamTests.cs` | 3 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ComposeUploadProjectionSeamTests.cs` | 2 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/ConcurrencySaveSeamTests.cs` | 18 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/seam/Compose/SpeVersionHistoryOboSeamTests.cs` | 8 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/Spe.Integration.Tests/SemanticSearch/SemanticSearchAuthorizationTests.cs` | 34 | modified | B1:Mock<HttpMessageHandler> |
| `tests/integration/Sprk.Provisioning.ControlPlane.LoadTests/LoadTestFixtures.cs` | 0 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/domain/Signals/SignalEvaluationTriggerHandlerTests.cs` | 37 | modified | B8:BindingFlags.NonPublic |
| `tests/unit/domain/Signals/SignalWriterTests.cs` | 101 | modified | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests.cs` | 34 | modified | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/RagEndpointsTests.cs` | 3 | modified | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/GateDecisionRecordSharingRegistrationTests.cs` | 3 | modified | B8:BindingFlags.NonPublic;B3:DI-resolution-assert |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/RefileWriterReconcilerRegistrationTests.cs` | 4 | yes | B8:BindingFlags.NonPublic;B3:DI-resolution-assert |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/DataverseContactIdentityStoreTests.cs` | 106 | yes | B8:BindingFlags.NonPublic;B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/NoAccessEnforcementStoreTests.cs` | 12 | yes | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/NoAccessListReaderTests.cs` | 30 | yes | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/SecureShareNoAccessGuardTests.cs` | 17 | yes | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AppOnlyAnalysisServiceResolveTests.cs` | 6 | modified | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverTeamOwnedOwnerTests.cs` | 15 | yes | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCollectorTests.cs` | 45 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/OutgoingFiledEmailArchiveTests.cs` | 23 | yes | B8:BindingFlags.NonPublic |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/AnchoredAnnotationPersistenceTests.cs` | 7 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceApplyTemplateTests.cs` | 8 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceCreateOnSaveTests.cs` | 16 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceImportedRenderSaveTests.cs` | 15 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceLoadImportedCommentsTests.cs` | 4 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceLoadImportedRevisionsTests.cs` | 3 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeServiceUploadFidelityTests.cs` | 4 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/CrossVersionSessionPersistenceTests.cs` | 9 | modified | B1:Mock<HttpMessageHandler> |
| `tests/unit/Sprk.Bff.Api.Tests/Services/InlineNotificationIntegrationPointsTests.cs` | 2 | modified | B8:BindingFlags.NonPublic |

## Path violations — `git mv` candidates (owner decision)

Files first added after 2026-10-12 outside the KEEP folders (ADR-038 §2 and the 2026-10-12 grandfather rule). The canonical target depends on what each file proves (authorization → `tests/integration/auth/`, writes → `tests/integration/data-mutation/`, wire shape → `tests/integration/contract/`, pure rules → `tests/unit/domain/`), which is a per-file reading this pass did not make. `TestInfrastructure/` and `Shared/` entries are helpers, not test classes; they move with the tests that use them or stay. **Nothing was moved.**

| File | Methods | Added by a project commit |
|---|---|---|
| `tests/integration/Shared/Dataverse/MappingSourceCallerReadDouble.cs` | 0 | yes |
| `tests/integration/Shared/Dataverse/RecordTypeCatalogSetup.cs` | 0 | yes |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/ChildRecords/BusinessOwnedAppendToLiveTests.cs` | 1 | no (added by another project, modified here) |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Signals/SignalReadRouteLiveTests.cs` | 0 | yes |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/Signals/SignalReadRouteTests.cs` | 105 | yes |
| `tests/integration/Sprk.Bff.Api.IntegrationTests/WorkAssignments/WorkAssignmentCreateLiveTests.cs` | 1 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Events/EventOwnershipContextTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs` | 27 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalLiveParentDocumentTests.cs` | 12 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalLiveParentReadTests.cs` | 25 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalLiveParentWalkCostLogTests.cs` | 3 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalUserContextFirmsTests.cs` | 24 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/WorkforceCeilingEndpointFilterTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/WorkforceCeilingEntitlementsTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/WorkforceCeilingRouteFamilyTests.cs` | 8 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/WorkforceCeilingRouteTests.cs` | 24 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Filters/FinanceStepZeroTests.cs` | 7 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/RateCardImportEndpointsTests.cs` | 19 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/RuleSetEndpointsTests.cs` | 49 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/StoredFileEndpointsTests.cs` | 20 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Memory/MemoryAccessDisabledUserTests.cs` | 3 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDocumentNarrowingTests.cs` | 65 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalLiveParentRuleTests.cs` | 15 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/SpeContainerMembershipServiceUnresolvableContainerTests.cs` | 6 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Http/NoTransientRetryOptionTests.cs` | 8 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Access/SecureChildReconcilerGateDecisionRecordTests.cs` | 12 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Access/SecureOwnerTeamNameMatchTests.cs` | 14 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AiDocumentNarrowingTests.cs` | 14 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Context/CallerSystemUserResolverTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/DocumentProfileOutputMapperRefusalTests.cs` | 13 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DocumentNarrowingReads.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/TaskActionCoreOwnershipOrderTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/UpdateRecordNodeExecutorRunIdentityTests.cs` | 14 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputMappingRunIdentityTests.cs` | 16 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookRunIdentityTests.cs` | 13 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ArchivedEmlNarrowing.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationByRegardingFiledMessagesTests.cs` | 15 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceArchiveAttachmentItemTests.cs` | 25 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceServerSendTests.cs` | 24 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadFilingSweepJobTests.cs` | 22 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/EmailArchiveCommunicationLinkSweepJobTests.cs` | 28 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/RuleGatedAssessedConsumerDecisionRecordTests.cs` | 11 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/SecureCopyOwnershipTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/ServerSendMarkerTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardLineSplitTests.cs` | 21 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardTests.cs` | 44 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardWorkAssignmentCycleTests.cs` | 8 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/OwnershipColumnKeyingParityTests.cs` | 8 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/TeamPrivilegeCheckTests.cs` | 6 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/DocumentCheckoutDisabledUserTests.cs` | 1 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/ExternalAccess/IdentityLinkReconciliationRegistryTargetsTests.cs` | 6 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileReconcileJobTests.cs` | 35 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileServiceOwnershipTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileServiceTests.cs` | 57 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceBaseValuesTests.cs` | 17 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FinanceSummaryRecentInvoicesTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceReviewReadServiceTests.cs` | 34 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Procedures/ProcedureServiceTests.cs` | 64 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/RuleSetServiceOwnerTests.cs` | 10 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/RuleSetServiceTests.cs` | 100 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/FromEmailSubmissionRulesTests.cs` | 16 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/FromEmailSubmissionServiceTests.cs` | 62 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/FromEmailWorld.cs` | 0 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionAfterStepsTests.cs` | 98 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionExistingDocumentTests.cs` | 72 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionFirmLinkWireTests.cs` | 12 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionPersisterTests.cs` | 127 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionProcessorTests.cs` | 38 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/InvoiceSubmissionServiceTests.cs` | 46 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/StepAFilingCoversTheHopTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Submission/SubmitInvoiceFromEmailOperationTests.cs` | 28 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Identity/SystemUserIdentityResolverDisabledFilterTests.cs` | 2 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/Handlers/InvoiceSubmissionProcessingFlowTests.cs` | 12 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandlerTests.cs` | 85 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationJobRetryTests.cs` | 13 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationReadTests.cs` | 5 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoSpeGrantLifecycleTests.cs` | 14 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/Billing/BillingFactLoaderTests.cs` | 34 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/BudgetInquiryComposerAndLinkTests.cs` | 41 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/BudgetInquiryExecutorTests.cs` | 24 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.CreatorGuard.cs` | 7 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.cs` | 76 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.GateDecision.cs` | 38 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.GateIdempotency.cs` | 10 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/GateDecisionRecordJobTests.cs` | 21 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/GateDecisionRecordSharingDirectTests.cs` | 31 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/GateDecisionRecordSharingTests.cs` | 29 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/AllVisibleDocumentNarrowing.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/CallerRecordAccessProbeDouble.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/DirectMessageOwnerTeamDouble.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/EntitySetNameFixtures.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/FixedKeyTrackingTokenSigner.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/HiddenDocumentCases.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/IdentityModelLoggerIsolation.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/IdentityModelLoggerIsolationTests.cs` | 1 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordingUserEmailRig.cs` | 0 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordTypeCatalogStub.cs` | 0 | no (added by another project, modified here) |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/SecureChildReconcilerFixtures.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/SpeContainerGraphStub.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/TeamPrivilegeCheckerDouble.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/TestInternalUserDirectory.cs` | 0 | yes |
| `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/UserFlagDirectoryDouble.cs` | 0 | yes |

## Naming notes (B13), no action

These KEEP-path files contain methods named as a behaviour sentence without the `{Method}_{Scenario}_{Expected}` underscores (for example `TheCiamPlaneNeverCreatesAContact`). B13 targets names with no scenario (`Test1`, `Foo_Works`); these state one, so they stay MAINTAIN.

- `tests/integration/auth/Finance/ApExportAuthorizationTests.cs`: ReadAloneIsNotTheDecideRight
- `tests/integration/auth/SpeAdmin/SpeAdminContainerItemRouteGateTests.cs`: TheRouteTableCoversEveryRouteInTheEndpointFile
- `tests/integration/auth/UnifiedAccessControl/AuthorizationGateTimingTests.cs`: TheScheduleRule
- `tests/integration/auth/UnifiedAccessControl/ExternalAccessReconciliationTests.cs`: TheScansPageWithTheCookieTheyAreGiven
- `tests/integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs`: TheCiamPlaneNeverCreatesAContact, TheBindRefusesAContactBoundOnTheWorkforcePlane, TheBindRefusesAContactASystemUserLinksTo, OnlyActiveContactsResolve
- `tests/integration/auth/UnifiedAccessControl/IdentityBinding/WorkforceMembershipTests.cs`: MembershipIsNeverInferredFromTheEmailDomainOrAnExtUpn
- `tests/integration/auth/UnifiedAccessControl/RestrictedExternalShareRemoverTests.cs`: TheLeaseIsReleasedAfterThePass
- `tests/integration/auth/UnifiedAccessControl/SpePermissionMatcherTests.cs`: MatchesThePermissionWhoseUpnIsTheContactsEmail, DoesNotMatchADifferentUser, ReturnsNullForANullList, SkipsPermissionsThatCarryNoUserIdentity, SkipsTheManagedPermissionAndFindsTheContactsOwn, MatchesByTheBoundObjectId, MatchesByObjectIdOnTheLegacyGrantedToShape, MatchesAB2BGuestByTheirEmailWhenTheUpnIsTheExtForm, FallsBackToTheEmailWhenTheObjectIdMatchesNothing, TrimsBothTheKeysEmailAndThePermissionsAddress
- `tests/integration/auth/UnifiedAccessControl/StampedOutsideAccessTaxonomyGuardTests.cs`: NothingIsFiledUnderAnExemptedTable
- `tests/integration/contract/Api/CatalogHealthMemoContractTests.cs`: TheWindowIsThirtySeconds
- `tests/integration/contract/Api/Workspace/WorkspaceAiSummaryAuthorizationContractTests.cs`: FailsClosed
- `tests/integration/data-mutation/DocumentContainer/RelocationTerminalAndHomeRuleTests.cs`: TheBudgets
- `tests/integration/data-mutation/ExternalAccess/SecureChildTransitionTests.cs`: TheMirrorIsNeverRemovedFromAChildStillIsolated
- `tests/integration/data-mutation/ExternalAccess/SecureParentReadVetoTests.cs`: TheClimbGoesThroughANonSecureProject
- `tests/integration/data-mutation/ExternalAccess/SecureRootFamilyOwnerTests.cs`: TheCapIsRespected, TheEmergencyStopWins
- `tests/integration/data-mutation/FinanceInvoiceReview/ConditionalLinkWriteTests.cs`: SendsTheVersionAsAWeakETag
- `tests/integration/data-mutation/RecordOwnership/RecordRepairColumnMetadataTests.cs`: MappedTextIsWrittenOnlyWhenItFitsTheColumn, AMappedLookupIsWrittenOnlyIntoALookupColumn
- `tests/integration/tenant/Ai/TenantSelectionByRequestTests.cs`: NoBffSourceFileResolvesTenantFromTheRequest
- `tests/unit/domain/Access/SecureChildLineageTests.cs`: TheLineageMapCoversExactlyTheCodifiedChildTables, EveryLookupTargetsARootOrAMirroredChild, EveryReparentableChildOfTheResolverIsMirrored

## Reliability registry

Touched files named in `tests/.reliability-registry.json` (no entry was found stale by this pass; the diff-level timing check was not run):
- `tests/integration/Spe.Integration.Tests/SystemIntegrationTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests.cs`

## Not done (stated, not hidden)

- Per-method heuristics 5–9 and 11–14 (B6, B9, B10, B11/B14/B16, B12, B17, B15, ORPHAN, DETACHED) over 15,266 methods. A follow-up diet pass can take them one KEEP folder at a time.
- Per-file canonical targets for the path violations.
- Client (Jest/Vitest) tests: the skill's scope is `tests/**/*.cs`.

## Count delta

- Test files touched (still present): 935; of these first added by a project commit: 518
- Test methods in those files: 15266
- Deleted by this pass: 0. Net post-diet count: unchanged.

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (17-ban classifier B1–B17) and Amendments A1 (ArchTests are a KEEP path) and A3 (orphan/detached evidence).
