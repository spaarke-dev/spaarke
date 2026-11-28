# Task 005 notes: FR-03 cleanup PR part 1 (§10.1 items 1b, 2, 4, 5a, 6)

> 2026-11-25. Deviations from the POML, findings, and measurements. Rework line is at the end.

## What was done per item

| Item | Change |
|---|---|
| 1b | Deleted `DocumentProcessingJobHandler`, `Ai/Jobs/ProfileSummaryJobHandler`, `Ai/Jobs/EmailAnalysisJobHandler` (no producer: a src/scripts/infra/tests grep for each `JobType` literal and `JobTypeName` found only the handlers, their tests and a log-search word list in `scripts/Debug-OfficeWorkers.ps1`). Removed the three `AddScoped<IJobHandler,...>` lines, the two ArchTests DEF-JOBS rows (`AsymmetricRegistrationTests`, `GateCombinationConstructibilityTests`) and the `EmailAnalysisJobHandlerTests` class (the second class in `EmailAnalysisIntegrationTests.cs`; `EmailAnalysisIntegrationTests`, which tests `AnalyzeEmailAsync`, stays). `jobs-architecture.md` handler rows and chaining line removed. `ProfileSummaryWorker` untouched. |
| 2 | `StartupDiagnostics` expected list is now built from the handlers' own `JobTypeName` constants: `AppOnlyDocumentAnalysis` always; `RagIndexing` and `BulkRagIndexing` only when `DocumentIntelligence:Enabled` (the same gate `JobProcessingModule` registers them under). It is NOT derived from the registered handlers: that would make the check a tautology that can never warn. Before, it warned on every start about `ProcessEmailToDocument`, `BatchProcessEmails` (never handlers) and the three deleted ones, and with DocIntel off about the RAG handlers too. |
| 4 | `DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`: TL;DR table, Path C paragraph, model-① "when used", mermaid node, Part 3 hand-off line and design note, Fix Option 3 marked APPLIED (#919), Part 5 checklist line, code map, status header. Line numbers for `AppOnlyAnalysisService` dropped (they had drifted). Part 4 (the original failure analysis) is intentionally left as history. |
| 5a | `IncomingCommunicationProcessor.EnqueueDocumentAnalysisAsync` and `CommunicationService.EnqueueDocumentAnalysisAsync` now use `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId)` and `MaxAttempts = 3`; nothing else in those contracts changed. Both methods went from `private` to `internal` (InternalsVisibleTo already grants the test assembly) so the test drives the real sites with no reflection. Stale comment at `CommunicationService.cs:664` updated. |
| 6 | `ConsumerTypes`: `AgreementClassify`, `NdaReview`, `NdaStandardSummary` added and put in `All`. All three Binding rows exist in `infra/dataverse/sprk_playbookconsumer-rows.json` (:7, :694, :716), so neither escalation trigger fired and routing behaviour is unchanged (the constants only close the drift the health check reported). |

## Regression test (item 5a)

`tests/integration/regression/Intake10_1_Item5a_DocumentProfileIdempotencyKeyTests.cs`, 4 tests: each archival site queues the upload key with MaxAttempts 3; the real `AppOnlyDocumentAnalysisJobHandler` over one in-memory idempotency store profiles a document once across email-archive, inbound-mailbox and upload contracts; a different document id gets a different key. The upload-path contract is built in the test the way `DocumentOperationsEndpoints` builds it (the endpoint itself needs an HTTP host); that mirror is the one weak spot (K-class: if the endpoint changed its key the test would not notice, but it and `OfficeProfileQueue` already share `ProfileIdempotencyKey`).

Seeding proof: reverted `CommunicationService` to `DocAnalysis:{id}` / MaxAttempts 2: 2 of 4 failed (key assertion; handler profiled the document twice). Restored: 4 of 4 pass.

## Found in passing (not fixed, outside the POML file list)

- `.claude/patterns/api/background-workers.md:14` names `DocumentProcessingJobHandler` as the "Handler exemplar" (file now deleted). Sub-agents cannot write `.claude/`: main session to repoint it (e.g. to `AppOnlyDocumentAnalysisJobHandler`).
- Stale mentions of the deleted handlers in docs not on the POML list: `docs/architecture/AI-ARCHITECTURE.md:407-408`, `sdap-bff-api-patterns.md:254-255`, `sdap-component-interactions.md:155`, `sdap-document-processing-architecture.md:141,346,565,637`, `sdap-overview.md:276-281`, root `FLOW-ANALYSIS.md`. `CommunicationEnrichmentService.cs:304` comment still names the old key `DocAnalysis:{documentId}`. All doc/comment only; list for the operator or fold into task 006's doc pass.
- With `DocumentIntelligence:Enabled=false` the old list warned about RAG handlers that were never registered; the new list no longer does.

## Quality gates

code-review (standard depth, whole diff): no F-class findings. adr-check: ADR-004 (key is now the canonical one), ADR-010 (only removals), ADR-032 (removed ledger rows only for deleted classes; no gate changed), ADR-038 (ArchTests rows removed because their subject was deleted: Amendment A3 orphan case; no scenario loses coverage), ADR-039 (constants only for existing rows): compliant.

rework: none task 005

## Addendum: tests that the ConsumerTypes constants broke (fixed; outside the POML file list)

Adding the three constants to `ConsumerTypes.All` failed two existing eval guards, both reachable only by running the Eval/Catalog tests:
- `P2LoopInjectionEvalSuiteTests.FullCatalog_EveryClosedCatalogConsumerType_HasAnEvalFamily` needs a case per `All` member in `tests/integration/contract/Eval/golden-utterances.json`: added GU-147 (agreement-classify), GU-148 (nda-review), GU-149 (nda-standard-summary), in the same "coverage entry, dedicated tests live elsewhere" shape as GU-142..146 (invoice constants).
- `NdaReviewDispatchEvalTests.DispatchCases_ExpectedConsumerType_IsGroundedInTheMirror_AndNotYetACodeConstant` asserted the opposite of this task (nda-review/nda-standard-summary NOT in `All`; its own message said "flip catalogStatus to existing only if a constant is added"). Flipped as it instructs: `nda-review-eval-cases.json` catalogStatus mirrored -> existing (5 cases), the test now asserts the consumer type IS in `All` (renamed `..._AndIsACodeConstant`), doc comment updated. Grounding against the mirror file is unchanged.
Eval/Catalog/ConsumerType/Routing filter: 2630 passed, 0 failed.

## Test results

- New regression file: 4 tests, 2 fail without the fix (seeding proof), 4 pass.
- Filtered Eval|ConsumerType|RoutingConsumer|Catalog: 2630 pass.
- `tests/Spaarke.ArchTests`: 2831 pass, 0 fail (includes Adr032 ledgers and WorkloadPlacementDocDriftTests).
- First full `Sprk.Bff.Api.Tests` run while two other sessions were building: ~35 WebApplicationFactory tests timed out after 3+ minutes each (`TaskCanceledException`, "client aborted the request"); the two real failures were the eval tests above. A sample of the timeouts (ChatRefine, DriveKeyedWriteRouteRetirement) passed in isolation: environmental load, not this change. See the final figures below.

## Final figures

- Full `Sprk.Bff.Api.Tests` run, twice, on a machine at ~96% CPU from other sessions: only WebApplicationFactory tests failed, all by 1-3 minute request timeouts (about 35 different tests per run, a different set each time). 91 of them re-run in isolation (Compose, Insights, Office, Workspace, PipelineHealth, Issue1084/1086 classes): 91 pass. Filtered Communication|DiGraph|JobProcessing|Jobs|EmailAnalysis|AppOnly: 5855 pass, 0 fail (DiGraphValidation included). Eval|ConsumerType|RoutingConsumer|Catalog: 2630 pass. ArchTests: 2831 pass. Not run to a clean full-suite green because of the load; the operator should let CI confirm.
- Build: `dotnet build` of the BFF and test project: 0 errors, 0 warnings.
- Publish size (BFF hygiene section 10 item 4): `dotnet publish -c Release` (framework-dependent linux-x64) of origin/master @ d148580e15 vs this branch, each from a fresh `git archive` copy at a short path (`C:\w5m`, `C:\w5b`; no worktree created), zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included. Both 194 files. master 41.608 MB, branch 41.602 MB, delta -0.006 MB. Well under 60 MB.
- CVE: `dotnet list package --vulnerable --include-transitive` on the BFF: none; no package reference changed.
- Placement Justification (for the PR): no new surface. Deletions plus a key fix; the only additions are three `ConsumerTypes` constants for Binding rows that already exist, one test file, and three eval-coverage cases. Hot path: Communication files are hot (open PR #1494 touches `IncomingCommunicationJobHandler.cs`, not the two files edited here); `/conflict-check` is left to the PR step.
- Commit hook note: the husky pre-commit needs `prettier` on PATH; this worktree has no `node_modules`, so I committed with the main checkout's `node_modules/.bin` on PATH (hook ran, not skipped).
