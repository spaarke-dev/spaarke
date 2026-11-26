# 050b: retire the legacy extraction path (deletion)

Branch `task/ebilling-050b`, based on `86fc546a65` (tip of `origin/work/ebilling-r1`). Rigor FULL (a deletion that touches `.cs` under the BFF). Owner decisions D-71 (delete) and D-134 (confirm redirect is 050a). Spec FR-21, FR-46, D-19, D-52.

No behaviour changed: only code that nothing calls after 050a was deleted. No FinanceBaseValues, `sprk_totalamount` or core-lookup file was touched, and no caller outside Finance needed a removed member (the two escalation triggers did not fire).

## 1. Step 0: grep inventory BEFORE the change

Command (tracked files only; `docs/assessments/**` excluded as historical):

```
git grep -n -E "ExtractInvoiceFactsAsync|ExtractionResultSchema|ExtractionDeploymentName|InvoiceExtractionPlaybookId|GetPlaybookByNameAsync|InvoiceExtractionJobHandler|JobTypeInvoiceExtraction|\"InvoiceExtraction\"|FinanceJsonSchemas|IInvoiceAnalysisService|InvoiceAnalysisService|FinanceExtraction" -- src tests scripts config
```

Hits by file (before):

| File | Member |
|---|---|
| `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` (683 lines) | the handler; `JobTypeName = "InvoiceExtraction"`; `ExtractInvoiceFactsAsync` call :221; `InvoiceExtractionPlaybookId` read :331; `UpdateInvoiceExtractionStatusAsync` :562 |
| `Services/Finance/IInvoiceAnalysisService.cs`, `InvoiceAnalysisService.cs` | `ExtractInvoiceFactsAsync`; playbook `FinanceExtraction`; `ExtractionResultSchema`; `ExtractionDeploymentName` |
| `Services/Finance/Models/FinanceJsonSchemas.cs`, `ExtractionResult.cs`, `InvoiceHeader.cs`, `BillingEventLine.cs` | the schema and the three models only `ExtractionResult` used |
| `Configuration/FinanceOptions.cs` :39, :52 | `ExtractionDeploymentName`, `InvoiceExtractionPlaybookId` |
| `appsettings.template.json` :472-473 | `Finance:InvoiceExtractionPlaybookId` and its comment key |
| `Infrastructure/DI/FinanceModule.cs` | `IInvoiceAnalysisService` registration :328; `IJobHandler, InvoiceExtractionJobHandler` :461; comments :23-31, :320-324, :390-396, :424 |
| `Infrastructure/DI/AnalysisServicesModule.cs` | `IOutputOrchestratorService` real + Null registrations; comments naming the handler (:608, :671, :675, :1321, :1326) |
| `Services/Ai/PublicContracts/IInvoiceAi.cs`, `InvoiceAi.cs`, `NullInvoiceAi.cs` | `GetPlaybookByNameAsync`, `GetStructuredCompletionAsync<T>` |
| `Services/Ai/NullOutputOrchestratorService.cs`, `CommunicationClassificationAi.cs:103`, `InvoiceIndexingJobHandler.cs:339`, `InvoiceSubmissionProcessingJobHandler.cs:65` | comments only |
| `tests/Spaarke.ArchTests/Adr032/AsymmetricRegistrationTests.cs` :57-58 | 2 DEF-FIN rows |
| `tests/Spaarke.ArchTests/Adr032/GateCombinationConstructibilityTests.cs` :62, :97 | 1 DEF-FIN row and its note |
| `tests/Spaarke.ArchTests/ADR013_LinearConsumerBoundaryTests.cs` :74 | playbook-lookup baseline entry |
| `tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs` :1464 | fixed-key exemption for `InvoiceExtractionJobHandler::UpdateInvoiceExtractionStatusAsync` |
| `tests/unit/.../Services/Ai/PlaybookRunIdentityTests.cs` :274 | `SystemEntries` row for the handler (it set the `System` run marker) |
| `tests/integration/.../ChatEndpointsTests.cs:819`, `KnowledgeBaseEndpointsTests.cs:233` | comments naming `InvoiceAnalysisService` |
| `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs:80` | 050a's negative test: confirm sends no job of type `"InvoiceExtraction"` (kept; it passes) |

`JobTypeInvoiceExtraction` had 0 hits before (as the brief said). The string `"InvoiceExtraction"` appears only as `InvoiceExtractionJobHandler.JobTypeName` and in 050a's negative test. Nothing enqueued it: 050a removed the only producer (`InvoiceReviewService`).

Test files constructing `new InvoiceAi(IPlaybookService, ...)`: 9 (listed in section 4).

## 2. After-grep: each acceptance criterion with its evidence

| Criterion | Evidence (after) |
|---|---|
| No `ExtractInvoiceFactsAsync`, `ExtractionResultSchema`, `ExtractionDeploymentName`, `InvoiceExtractionPlaybookId` in `src/` and `tests/` | `grep -rn -E "ExtractInvoiceFactsAsync\|ExtractionResultSchema\|ExtractionDeploymentName\|InvoiceExtractionPlaybookId\|InvoiceAnalysisService" src tests scripts infra infrastructure config` returns nothing. (`FinanceJsonSchemas` survives only in two `infra/dataverse/**` description strings that say the Action "replaces the C# FinanceJsonSchemas"; they are history, left.) |
| No `GetPlaybookByNameAsync`; no `GetStructuredCompletionAsync` on `IInvoiceAi` / `InvoiceAi` / `NullInvoiceAi` | `grep -rn GetPlaybookByNameAsync src tests` returns nothing; `grep GetStructuredCompletionAsync` over the three files returns nothing. (`IOpenAiClient`, `OpenAiClient` and `ICommunicationClassificationAi` keep their own `GetStructuredCompletionAsync`; untouched.) |
| `IInvoiceAi` keeps `GenerateEmbeddingAsync` and every Action-backed method; `NullInvoiceAi` covers each | `IInvoiceAi` members after: `GenerateEmbeddingAsync`, `ClassifyAsync`, `CodeLinesAsync`, `MatchCodeSetAsync`, `DraftRulesAsync`, `DraftFirmNoticeAsync`, `ReadInvoiceAsync` (7). `NullInvoiceAi` has the same 7 public methods, each throwing `FeatureDisabledException`; it compiles against the interface, so a missing one would not build. `InvoiceAiContractTests.NullInvoiceAi_CapabilityMethod_ThrowsFeatureDisabledWithInvoiceErrorCode` passes. |
| Nothing enqueues `InvoiceExtraction`; no handler registered for it | `grep -rn "\"InvoiceExtraction\"\|JobTypeInvoiceExtraction\|InvoiceExtractionJobHandler" src tests scripts infra .github` finds only comments (FinanceModule:436, IDataverseUpdateHandler.cs, InvoiceIndexingJobHandler:339, InvoiceSubmissionProcessingJobHandler:65) and 050a's negative test `InvoiceReviewWritePathTests.cs:80`, which passes. |
| Negative: no path except 049 creates `sprk_invoice` | See section 6. |
| The three ArchTest baselines hold no entry for a deleted type; ArchTests pass | Section 5 lists the entries removed; ArchTests filters pass (section 8). |
| Deleted tests listed with the removed member each covered | Section 4. |
| Publish-size delta vs a fresh master build; no new HIGH CVE | Section 7. |
| `dotnet build` exits 0; tests pass | Section 8. The whole `Sprk.Bff.Api.Tests` and the whole ArchTests were NOT run (brief); filtered runs only. |

## 3. Deleted and changed code

Deleted (git status `D`):

- `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` (also held `InvoiceExtractionPayload`, `InvoiceRecord`, `DocumentRecord`; none used elsewhere)
- `Services/Finance/IInvoiceAnalysisService.cs`, `Services/Finance/InvoiceAnalysisService.cs`
- `Services/Finance/Models/FinanceJsonSchemas.cs`, `ExtractionResult.cs`, `InvoiceHeader.cs`, `BillingEventLine.cs` (the last three had no reader once `ExtractionResult` went; POML named only `ExtractionResult`, the other two were its sole children, so they went with it)
- `Services/Ai/OutputOrchestratorService.cs`, `NullOutputOrchestratorService.cs`, `IOutputOrchestratorService.cs` (the last also held `PlaybookExecutionContext`, `OutputMappingResult`, `EntityUpdateResult`; no other user, see section 5.2)

Changed:

- `IInvoiceAi.cs`, `InvoiceAi.cs`, `NullInvoiceAi.cs`: the two legacy members removed; `InvoiceAi` no longer takes `IPlaybookService` (constructor is now `IOpenAiClient, IActionResolver, IActionRunner, ContentUnderstandingClient, ILogger`); `OpenAI.Chat` / `Models.Ai` usings dropped; the class docs rewritten.
- `FinanceOptions.cs`: two properties removed. `appsettings.template.json`: the `InvoiceExtractionPlaybookId` key and its comment key removed.
- `FinanceModule.cs`: the `IInvoiceAnalysisService` block and the `IJobHandler, InvoiceExtractionJobHandler` registration removed; comments corrected; the `SpendSnapshotGeneration` "no producer" NOTE (which sat on the handler's registration) kept as a free-standing note. `AnalysisServicesModule.cs`: both `IOutputOrchestratorService` registrations removed; comments corrected.
- `IDataverseUpdateHandler.cs`: gained the `ConcurrencyMode` enum, which lived in the deleted `OutputOrchestratorService.cs`, now in `Sprk.Bff.Api.Services.Dataverse`. Seven fully qualified `Sprk.Bff.Api.Services.Ai.ConcurrencyMode` references in the `InvoiceFamilyGenericUpdatePathTests.*` partials and `WriterOnlyTablesWritersGuardTests` were repointed (a namespace-only change; they are other projects' tests, edited mechanically).
- Comments only: `CommunicationClassificationAi.cs:103`, `InvoiceIndexingJobHandler.cs:339`, `InvoiceSubmissionProcessingJobHandler.cs:65`, `InvoiceFamilyCoreRecordGuard.cs:7`, `IFieldMappingDataverseService.cs:58`, `RecalculateWriteNoCreateTests.cs:24,124` (050a notes section 10 list, those that described a path that no longer exists), `ChatEndpointsTests`, `KnowledgeBaseEndpointsTests`, `InvoiceReadSeamTests`, `InvoiceAiActionSeamTests`.
- `infrastructure/dataverse/views/invoice-review-queue.md`: the "If Confirmed" flow described creating `sprk_invoice` and enqueuing the handler; now describes the submission.

050a section 10 stale comments NOT changed because they are still accurate or do not describe the retired path: `OwnedChildWrite.cs:299`, `InvoiceTotalsService.cs:32,40` ("the InvoiceReviewService retry pattern" still exists), `SubmissionRejectionRule.cs:156`, `TaskActionCore.cs:115`, `RecordOwnerAssignmentCensusTests.cs:1690` (already says 050a removed it).

## 4. Deleted or edited tests and the member each covered

Tests of removed members:

| Test | Covered | Action |
|---|---|---|
| `OutputMappingRunIdentityTests`: `APersonsRun_MapsItsOutputsAsThatPerson`, `ASystemRun_MapsItsOutputsAppOnly`, `APersonsRun_WhoseUserIsUnknown_WritesNothing_AndNeverAppOnly` (2 cases), `AMappingContextThatSaysNothing_IsAPersonsRun_AndWritesNothingWithoutAUser`, `APersonsUpdate_ThatDataverseRefuses_IsAFailedUpdateWithTheCode`, `ASystemUpdate_ThatDataverseRefuses_IsAFailedUpdate_WithoutThePersonsCode`, `APersonsRun_ItsOutputMappingOntoAnAccessTableOrColumn_FailsThatUpdate_AndNothingIsWritten` (4 cases) | `OutputOrchestratorService.ApplyOutputMappingAsync` (the per-update run identity, #2257) | deleted with the service |
| `AnalysisServicesModuleGatingTests.OutputOrchestrator_WhenCompoundGateOff_ApplyOutputMapping_FailsFastWithStable503ErrorCode` (3 cases) | `NullOutputOrchestratorService` / `IOutputOrchestratorService` registration | deleted |
| `AnalysisServicesModuleGatingTests.FinanceModule_NoLongerRegisters_PlaybookLookup_Or_OutputOrchestrator` | the `IOutputOrchestratorService` half asserted it is not resolvable from FinanceModule alone | the `IOutputOrchestratorService` assertion removed; test renamed `..._PlaybookLookup`; the lookup assertion stays |
| `PlaybookRunIdentityTests` `SystemEntries` row for the handler | the handler set the `System` run marker (one call site) | row removed; the source guard (`OnlyTheListedEntries_MarkARunSystem_...`) now expects the 3 remaining entries and passes |

Kept (the handler and its guards stay): the `TheHandler_*` / `TheHandlersReFile_*` tests of `DataverseUpdateHandler` in `OutputMappingRunIdentityTests` (writes AS the person, row-version read AS the person, deny-lists, access sets, re-file), plus the access-set theory for the handler. The class summary says what went.

Constructor-only edits (no test lost): 9 test sites that built `new InvoiceAi(new Mock<IPlaybookService>(Strict).Object, ...)` lost the first argument: `ContentUnderstandingLiveTests` (2), `AttachmentClassificationJobHandlerTests`, `BillingRuleDraftSeamTests`, `CodeSetMatchingLiveTests`, `CodeSetMatchingSeamTests`, `FirmNoticeActionSeamTests`, `InvoiceAiActionSeamTests` (2), `LineCodingSeamTests`, `InvoiceReadSeamTests`. Their anti-stub assertion that a capability method never touches the playbook service now holds by construction (the facade has no playbook dependency); the strict `IOpenAiClient` mock remains.

No test existed for `InvoiceExtractionJobHandler`, `InvoiceAnalysisService`, `FinanceJsonSchemas` or the two removed `IInvoiceAi` members (grep of `tests/` before: none), so there was nothing further to delete. Per ADR-038 a deletion adds no test; the proof is the ratchets (section 5), the compile (any remaining caller would not build), and 050a's negative test.

## 5. Ratchet entries removed (none added)

| Ratchet | Entry removed |
|---|---|
| `AsymmetricRegistrationTests.KnownDefects` | `("InvoiceExtractionJobHandler", "IInvoiceAnalysisService", "DEF-FIN")` and `("InvoiceExtractionJobHandler", "TextExtractorService", "DEF-FIN")` |
| `GateCombinationConstructibilityTests.KnownDefects` | `Defect("DEF-FIN", "InvoiceExtractionJobHandler", "IInvoiceAnalysisService", ...)`; the `DEF-FIN` note text updated |
| `ADR013_LinearConsumerBoundaryTests.PlaybookLookupBaselineConsumerTypes` | `Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceExtractionJobHandler` |
| `ExternalDocumentNarrowingGuardTests` fixed-key exemptions (the "EveryGenericUpdateWriter_AsksTheAppOnlyRefusal" guard) | `InvoiceExtractionJobHandler::UpdateInvoiceExtractionStatusAsync` (the POML note's #2237/#2239 exemption, removed because it names a member that no longer exists) |
| `PlaybookRunIdentityTests.SystemEntries` | `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` |

### 5.1 How this relates to #2242, #2259, #2263 (already closed; not closed again)

- **#2242** (`UpdateInvoiceExtractionStatusAsync` wrote `sprk_extractionstatus` through `UpdateDocumentFieldsAsync(invoiceId)`, which targets `sprk_document`): the member is deleted with the handler. The fixed-key exemption in `ExternalDocumentNarrowingGuardTests` is removed.
- **#2259** (the seeded 'Finance Invoice Processing' output mapping never wrote; `ResolveComplexValue` threw): `OutputOrchestratorService` is deleted, so `ResolveComplexValue` is gone; the mapping can never start writing.
- **#2263** (`DataverseUpdateHandler` Optimistic mode puts `versionnumber` in the PATCH body, not `If-Match`): see 5.2; the handler class survives, its only production caller is gone.
- uac-r2 PR #2258 (merged) added `OutputMappingRunIdentityTests`; per the POML note, 050b (second to merge) deletes the orchestrator half. Done.

### 5.2 OutputOrchestratorService and DataverseUpdateHandler outcome

- After the handler went, `IOutputOrchestratorService` had **no resolver and no caller** in `src/` (grep: only its two DI registrations, its Null peer and two test files). It was removed along with the Null peer, the interface, `PlaybookExecutionContext`, `OutputMappingResult`, `EntityUpdateResult`, both registrations, and the tests in section 4. `EveryGenericUpdateWriter_AsksTheAppOnlyRefusal` and the other guards that list `DataverseUpdateHandler` as a writer needed no change: the guards name the handler class, which stays.
- **`DataverseUpdateHandler` stays**, as the brief said, but the brief's reason needs a correction: `FieldMappingEndpoints.cs:1690` and `UpdateRecordActionCore.cs:345` use only its **static** helpers (`RecordNotFoundPhrase`, `CarriesLookupBind`, `RecordNotFoundRefusal`), not the injected instance. After 050b **no production code injects `IDataverseUpdateHandler`**; the registration in `FinanceModule` is resolved only by tests (`WriterOnlyTablesWritersGuardTests`, `RefileWriterReconcilerRegistrationTests`, `OutputMappingRunIdentityTests`) and named in about 12 ArchTest writer guards. So the instance service, and its `Optimistic` concurrency mode (#2263's defect), have no production caller left. I did not remove them: that edits `DataverseUpdateHandler.cs` (a writer in many other projects' guards: `CoreRecordPairUpdateCheckGuardTests`, `GenericUpdatePathsPreparePairGuardTests`, `InvoiceFamilyFieldLockGuardTests`, `WriterOnlyTables*`, `RecordOwnerAssignmentCensusTests`, `RefileWriterUnfileRuleGuardTests`, `SearchIndexDropGuardTests`, `UpdateRecordFieldsCallerGuardTests`, `InvoiceMoneyColumnsAppOnlyWriteGuardTests`, ...). **Recorded as an orphan for the owner / uac-r2:** remove `DataverseUpdateHandler`'s instance API (keeping the static helpers, or moving them), its registration, `ConcurrencyMode` and those guard rows in one reviewed change.

## 6. Negative grep: `sprk_invoice` creation

- `InvoiceLifecycleService.BuildInvoiceCreateRequest` (the create builder, 014) has exactly one caller: `Services/Finance/Submission/InvoiceSubmissionPersister.cs:679` (049). `grep -rn "BuildInvoiceCreateRequest(" src/server`: the interface, the implementation and that one call.
- No `new Entity("sprk_invoice"...)` / `CreateAsync("sprk_invoice"...)` remains in `Services/Finance` outside the lifecycle service's row builder (`InvoiceLifecycleService.cs:460`, used by the above).
- Other `sprk_invoice` create surfaces pre-date this task. The generic `ChildRecordEndpoints` invoice create was closed by 096b (`ChildRecordEndpoints.cs:71-76`); the chat `create_record` tool refuses invoices (`DataverseCreateRecordHandler.cs:340`, `:444`). The one left is Office QuickCreate (`OfficeService.cs` ~2202-2260, `QuickCreateEntityType.Invoice`), which EB-D-201 assigns to task 096 (still open, gated on one add-in release). So the negative criterion holds for every path 050b owns; the QuickCreate path is a known limit owned by 096, not an owner question.
- `InvoiceExtractionJobHandler` never created `sprk_invoice` (it updated one); confirm creation went in 050a.

## 7. Publish size and CVE

Same command, Release, framework default of the project (`linux-x64` output), on three trees; sizes are the `-o` folder total (191 files each):

| Tree | Bytes | MB (MiB) |
|---|---|---|
| origin/master `fe150ac35a` (fresh build, scratch worktree) | 141,116,421 | 134.58 |
| `86fc546a65` (this branch's base, i.e. before 050b) | 141,063,761 | 134.53 |
| `task/ebilling-050b` code commit `43b8cc23f6` | 140,993,165 | 134.46 |

Delta of this branch vs fresh master: **-123,256 bytes (-0.12 MB)**; delta of 050b alone (vs its base): **-70,596 bytes (-0.07 MB)**. Negative, as expected; far below the +5 MB per-task bar. The absolute figure is 134 MB because the publish folder is the self-contained `linux-x64` output; the "55 MB / 60 MB" thresholds in root CLAUDE.md section 10 were written against a different measure, so I report the delta only and flag the absolute for whoever owns the threshold.

`dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: "no vulnerable packages given the current sources". No new HIGH CVE. No package was added or removed.

Scratch worktrees (`C:\tmp\050b\mastersrc`, `basesrc`) removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.

## 8. Build and test commands (all exit 0)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet build tests/unit/Sprk.Bff.Api.Tests/` (compiles the `tests/integration/{seam,contract,data-mutation,regression,tenant}` and `tests/unit/domain` sources): 0 errors. `dotnet build tests/Spaarke.ArchTests/`: 0 errors. (A first test-project build failed on the `ConcurrencyMode` namespace and the removed constructor argument; fixed above.)
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Finance|~InvoiceAi|~OutputMapping|~AnalysisServicesModule|~PlaybookRunIdentity|~InvoiceIndexing|~InvoiceSubmission|~InvoiceReview"`: Passed 10091, Failed 0, Skipped 4 (live-environment tests).
- `... --filter "~ContentUnderstanding|~InvoiceFamily|~RecordOwnership|~GenericUpdate|~PublicContracts|~RecalculateWriteNoCreate|~DataverseUpdateHandler|~RefileWriter|~ChatDocument"`: Passed 3549, Failed 0, Skipped 3 (live).
- `... --filter "~Drift|~Options|~Infrastructure.DI|~JobHandler|~ServiceBusJob|~Startup|~Registration"`: Passed 1530, Failed 0, Skipped 1.
- `dotnet test tests/Spaarke.ArchTests/ --filter "~Adr032|~ADR013|~ExternalDocumentNarrowing|~GenericUpdate|~UpdateRecordFields|~WriterOnlyTables|~CoreRecordPairUpdateCheck|~InvoiceFamily|~InvoiceMoney|~RecordOwnerAssignmentCensus|~SearchIndexDrop|~RefileWriter|~BillingExportClaim|~ADR010|~ADR032"`: Passed 194, Failed 0.
- `... --filter "~Finance|~Invoice|~Registration|~Options|~ADR0|~ADR1|~Handler|~Playbook"`: Passed 273, Failed 0.
- Not run: the whole `Sprk.Bff.Api.Tests` and the whole ArchTests (brief). A before/after "fails before" proof is not applicable to a deletion; see section 4.

## 9. Dataverse rows left for owner action (NOT touched)

- The `sprk_analysisplaybook` rows **`FinanceExtraction`** (by-name playbook, code `PB-013`; DEV `sprk_playbookid` `1e657651-9308-f111-8407-7c1e520aa4df`, "Finance Invoice Processing") and its seeded output mapping (`scripts/seed-data/playbooks.json` still describes "OutputMapping processed by OutputOrchestratorService"). No code reads them now. Deactivating them needs explicit owner OK (POML constraint "live data").
- The legacy `sprk_invoice.sprk_extractedjson` column (20,000 chars; `docs/data-model/json-field-schemas.md` now says no writer remains) and `sprk_extractionstatus`: no code writes them.
- Environment App Service settings `Finance__InvoiceExtractionPlaybookId` / `Finance__ExtractionDeploymentName`, if any stamp has them: they bind to nothing now (same as the retired `EbillingOutputContainerId`, 092). Provisioning scripts do not set them (grep of `scripts`, `infra`, `.github`: none).
- An `InvoiceExtraction` message already on a Service Bus queue when this deploys has no handler and dead-letters `NoHandler` (`ServiceBusJobProcessor.cs:204`). 050a stopped producing them; the deploy order is 050a then 050b, so the window is only messages enqueued before 050a reached an environment.

## 10. Orphans and things noticed (not fixed)

1. **`DataverseUpdateHandler` instance service** and its `Optimistic` mode have no production injector now (section 5.2).
2. ~~`FinanceTelemetry` extraction metrics~~: removed in review fix round 1 (section 13).
3. `FinancialCalculationToolHandler` is still registered twice (concrete + `IAiToolHandler` forwarding); the concrete registration was "needed by InvoiceExtractionJobHandler (direct injection)". The forwarding resolves the concrete type, so both stay; only the comment changed.
4. `InvoiceExtractionToolHandler` (`Services/Ai/Handlers`, out of scope per POML) is untouched. Docs had pointed to a wrong path (`Services/Finance/Tools/InvoiceExtractionToolHandler.cs`); corrected in `finance-intelligence-architecture.md`.
5. QuickCreate Invoice entity type (section 6): owned by task 096 (EB-D-201).
6. The `docs/assessments/**` files that mention the handler are historical and untouched.

## 11. Deviations from the POML steps

- Step 4 names "three ArchTest baselines"; five lists named the handler (the two others are the `ExternalDocumentNarrowingGuardTests` exemption the POML note orders removed, and `PlaybookRunIdentityTests.SystemEntries`). All five shrank.
- Step 3 deleted `InvoiceHeader` and `BillingEventLine` as well as `ExtractionResult` (dead once it went).
- Step 5 "full test": filtered runs only, as the brief instructs (section 8).
- Step 6 (TASK-INDEX.md) NOT done: the main session owns it (brief).
- `IOutputOrchestratorService` was removed (the POML's preferred branch); `ConcurrencyMode` had to move out of it (not mentioned in the POML).
- Docs updated beyond the two named: `jobs-architecture.md`, `sdap-overview.md`, `sdap-workspace-integration-patterns.md`, `finance-intelligence-user-guide.md`, `json-field-schemas.md`, `schema-additions-alternate-keys.md`, `schema-corrections.md`, `SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` row T-05, `infrastructure/dataverse/views/invoice-review-queue.md`. `docs/assessments/**` left.
- No second adversarial-verifier pass was run by this agent; code-review and adr-check are for the main session (the deletion tag requires them).

## 12. ADR / reuse check (adr-check, self-run)

- ADR-013 / ADR-043: the change removes the only non-Action AI members from `IInvoiceAi` (the by-name playbook lookup and C# schema); the playbook-lookup baseline shrank.
- ADR-032: `NullInvoiceAi` still has a peer for every `IInvoiceAi` method; the Null peer of the deleted `IOutputOrchestratorService` went with it (no unconditional consumer remains); the DEF-FIN rows for the deleted handler went with it.
- ADR-002 / D-19: no write path added or moved. CLAUDE.md section 11: no new surface (a deletion); the one new declaration is the moved `ConcurrencyMode` enum (an existing type, relocated).
- BFF hygiene (section 10): publish size down, no new package, no new endpoint or DI registration; two registrations removed.

## 13. Review fix round 1 (Opus adversarial review, 2026-11-22)

One adversarial pass (Opus) over `86fc546a65..ef113f6c60`. No F1/F3 finding; no broken contract. Fixed:

| # | Finding | Fix |
|---|---|---|
| 1 | `FinanceTelemetry` extraction instruments and `RecordExtraction*` methods had no caller after the handler went; the class doc still pointed operators at `finance.extraction.duration`. No dashboard or alert in `infra/`, `infrastructure/`, `deploy/`, `scripts/` or `.github/` uses the names. | Removed the four instruments, the three methods and the KQL hint; the class doc says `finance.extraction.*` is retired. |
| 2 | ISS-002 (#1918) and ISS-003 (#1919) describe the legacy extraction path and were still Open. | Both set to Resolved in `defer-issues.md`; the completion commit closes #1918 and #1919. |
| 3 | `finance-intelligence-user-guide.md` named a job-replay admin operation that does not exist. | Says there is no replay operation; a failed submission ends Failed and is submitted again (EB-D-165). |
| 4 | `InvoiceReviewService.InvExtractionStatus` (`sprk_extractionstatus`) had no reader or writer, under a comment saying the persister writes it (050a leftover). | Constant deleted. |
| 5 | `scripts/seed-data/playbooks.json` still said `OutputOrchestratorService` applies the PB-013 output mapping. | Both notes reworded as retired (string fields only; the JSON still parses). |
| 7 | Section 6 was stale (096b closed the child-record create, the chat tool refuses invoices, EB-D-201 answered the QuickCreate question). | Section 6 rewritten. |
| 8 | Nits: unused `using Sprk.Bff.Api.Services.Ai;` in `IDataverseUpdateHandler.cs`; leading blank line in `IInvoiceAi.cs`. | Removed. |

Known limits, not fixed here:
- **6** — #2263 (`DataverseUpdateHandler` Optimistic mode puts `versionnumber` in the PATCH body) was auto-closed by docs commit b32faae4, not by a fix. After 050b no production code injects `IDataverseUpdateHandler`, so the defect is off every real path, but nothing tracked it: #2263 is reopened with that context (owner of the handler code: uac-r2 guards name the class).
- **9** — `InvoiceAiContractTests` does not check that the Null peer throws for `GenerateEmbeddingAsync` and `MatchCodeSetAsync` (pre-existing; the compiler still forces both members to exist). K3.
