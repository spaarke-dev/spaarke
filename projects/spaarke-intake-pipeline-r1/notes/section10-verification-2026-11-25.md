# §10 cleanup findings: re-verification (2026-11-25)

> Read-only re-check of `design.md` §10 against master merged at `4af7aaecb5` (Sonnet code-mapper, `git grep` over src/infra/scripts/tests/docs/projects). Paths relative to `src/server/api/Sprk.Bff.Api/` unless stated. Not covered: Power Automate flow JSON (none in repo), live Dataverse/prod config.

| # | Claim | Verdict |
|---|---|---|
| 1 | Four handlers with no producer | CONFIRMED (all four) |
| 2 | StartupDiagnostics lists ghost handlers | CONFIRMED (warning only) |
| 3 | `DeadLetterQueueService` has no caller | CONFIRMED |
| 4 | Profile doc says node playbook, code runs a direct Action | CONFIRMED |
| 5 | Email bypasses the ingestor; profile keys differ | CONFIRMED; double profile possible across paths (PARTLY: reachability in practice unchecked) |
| 6 | `agreement-classify`, `nda-review` not in `ConsumerTypes.All` | CONFIRMED |
| 7 | Reply sends no In-Reply-To / References | CONFIRMED |
| 8 | `sprk_backgroundjobrun` unused | PARTLY: table unused; in-memory `BackgroundJobRunRecord` is live; ADR-036 text stale |

## 1. Handlers with no producer
| Handler | File | DI | Tests | LOC |
|---|---|---|---|---|
| AttachmentClassification | `Services/Jobs/Handlers/AttachmentClassificationJobHandler.cs:43` | `Infrastructure/DI/FinanceModule.cs:432` | `tests/integration/seam/Finance/AttachmentClassificationJobHandlerTests.cs`; ArchTests Adr032 | 668 |
| ProfileSummary | `Services/Ai/Jobs/ProfileSummaryJobHandler.cs:46` | `JobProcessingModule.cs:62` | ArchTests Adr032 only | 417 |
| EmailAnalysis | `Services/Ai/Jobs/EmailAnalysisJobHandler.cs:35` | `JobProcessingModule.cs:46` | `tests/unit/.../EmailAnalysisIntegrationTests.cs:700` | 247 |
| DocumentProcessing | `Services/Jobs/Handlers/DocumentProcessingJobHandler.cs:24` ("Sample job handler", `:8`) | `JobProcessingModule.cs:32` | none | 99 |

- AttachmentClassification is eBilling's: ISS-004 "never enqueued" (`projects/ebilling-r1/notes/defer-issues.md:939-951`), issue #1920; `docs/architecture/finance-intelligence-architecture.md:39` still claims a trigger from `EmailToDocumentJobHandler`; bound to `invoice-classify` in `infra/dataverse/sprk_playbookconsumer-rows.json:575`. Natural producer site: `Services/Communication/IncomingCommunicationProcessor.cs` near `:1653` (where `DocAnalysis` is enqueued).
- EmailAnalysis came from `projects/x-email-to-document-automation-r2` task 032.

## 2. StartupDiagnostics
`Infrastructure/DI/StartupDiagnostics.cs:84` expects `{AppOnlyDocumentAnalysis, DocumentProcessing, ProcessEmailToDocument, ProfileSummary, RagIndexing, BatchProcessEmails, BulkRagIndexing, EmailAnalysis}`. `ProcessEmailToDocument`/`BatchProcessEmails` are not handlers (only `Models/Email/BatchProcessModels.cs` request/response types). Mismatch → `LogWarning` (`:85-91`) on every start; no failure.

## 3. DeadLetterQueueService
`Services/Jobs/DeadLetterQueueService.cs` (488 LOC): `GetSummaryAsync`, `ListMessagesAsync`, `GetMessageAsync`, `RedriveMessagesAsync`. Registered `Infrastructure/DI/WorkersModule.cs:37`. No caller in src or tests. Models in `DlqModels.cs`.

## 4. Doc drift
`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` says node playbook ("Path C") at `:21, :59, :118, :129, :177, :180, :257, :265, :280`; `:91` already says direct-Action spine. Code: `Services/Ai/AppOnlyAnalysisService.cs:~627-645` (#919 convergence) runs `ProfileViaLinearActionAsync`; node path only when the AI seams are null.

## 5. Email ingestion and profile keys
- Only `ICommunicationChannelIngestor` is `MessagingIngestor` (`Infrastructure/DI/CommunicationModule.cs:439`); `:436` comment: email stays on `IncomingCommunicationProcessor` by design (`CommunicationChannelDispatcher.cs:54`).
- Keys: `DocAnalysis:{id}` at `IncomingCommunicationProcessor.cs:1656` and `CommunicationService.cs:4554` (MaxAttempts 2); `analysis-{id}-documentprofile[-version-{id:N}]` from `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey` (`:42-45`), used by `DocumentOperationsEndpoints.cs:641`, `ExternalProjectDataEndpoints.cs:2239`, `OfficeProfileQueue.cs:82`, `UploadFinalizationWorker.cs:917,1359` (MaxAttempts 3). Broker `MaxDeliveryCount = 5` (`JobContract.cs:65`).
- Handler dedups on the given key only (`AppOnlyDocumentAnalysisJobHandler.cs:87-104`) → the two formats never dedup against each other.
- Smallest fix: both `DocAnalysis:` literals → `ProfileIdempotencyKey(documentId)`, MaxAttempts 3.

## 6. ConsumerTypes
No constant/`All` entry for `agreement-classify` or `nda-review` in `Services/Ai/PublicContracts/ConsumerTypes.cs` (`All` at `:416`; rule at `:360-375`: a row without a constant is Unhealthy). `RoutingConsumerTypeHealthCheck.cs:251-262, :501, :585-588` computes rows-without-constants as drift (reported on `/healthz/catalog`). Seeds: `infra/dataverse/sprk_playbookconsumer-rows.json:7-15` and `:694-703` (`nda-review` → action `agreement-review`, asymmetry intentional per `$comment-agreements-r1-002`). `nda-standard-summary` (`:716`) may be the same; unchecked.

## 7. Reply threading
`Services/Communication/Channels/EmailChannelSender.cs:205-231` builds a new `Message`; only header is the server-send marker (`:229-232`). No `createReply`. `InReplyToMessageId` is stored (`CommunicationService.cs:1208, :3241`) and used for thread joining, never sent. Graph accepts only `x-`-prefixed custom `internetMessageHeaders`, so the fix is `createReply`/`createReplyAll` on the parent message.

## 8. sprk_backgroundjobrun
`docs/data-model/sprk_backgroundjobrun.md:8` "deployed but unused" (store is `InMemoryBackgroundJobStore`); entity XML `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_backgroundjobrun/Entity.xml`. `docs/adr/ADR-036-background-job-infrastructure.md:104` still says a row is persisted per invocation. Office-scoped note: `scripts/Create-BackgroundJobEntity.ps1:233`, `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_backgroundjob/Entity.xml:13`, `docs/data-model/sprk_backgroundjob.md:33-35`.
