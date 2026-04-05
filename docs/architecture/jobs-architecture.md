# Jobs Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Azure Service Bus job processing system — contract, routing, handlers, idempotency, and dead-letter management

---

## Overview

The SDAP jobs subsystem provides asynchronous background processing for document analysis, email handling, RAG indexing, and financial intelligence. All jobs flow through Azure Service Bus using a uniform `JobContract` message schema defined by ADR-004. Two dedicated queue processors — `ServiceBusJobProcessor` (shared) and `CommunicationJobProcessor` (email-only) — route messages to the appropriate `IJobHandler` implementation based on the `JobType` discriminator.

The system was designed to replace in-process background queues with a durable, retryable message bus. Domain isolation between the shared queue (`sdap-jobs`) and the communication queue (`sdap-communication`) prevents cross-domain DI failures from blocking email processing.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| ServiceBusJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | BackgroundService that consumes the shared `sdap-jobs` queue and routes to IJobHandler by JobType |
| CommunicationJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationJobProcessor.cs` | BackgroundService that consumes the dedicated `sdap-communication` queue, routes only to IncomingCommunicationJobHandler |
| IJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/IJobHandler.cs` | Interface: `string JobType` + `Task<JobOutcome> ProcessAsync(JobContract, CancellationToken)` |
| JobContract | `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` | Uniform message schema: JobId, JobType, SubjectId, CorrelationId, IdempotencyKey, Attempt, MaxAttempts, Payload (JsonDocument), CreatedAt |
| JobOutcome | `src/server/api/Sprk.Bff.Api/Services/Jobs/JobOutcome.cs` | Processing result: Completed, Failed (retryable), or Poisoned (dead-letter) |
| JobSubmissionService | `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` | Enqueues jobs to Service Bus; maps IdempotencyKey to MessageId for duplicate detection; supports both shared and communication queues |
| IdempotencyService | `src/server/api/Sprk.Bff.Api/Services/Jobs/IdempotencyService.cs` | Redis-based deduplication: check-before-process, processing locks (5min TTL), mark-as-processed (24h TTL) |
| BatchJobStatusStore | `src/server/api/Sprk.Bff.Api/Services/Jobs/BatchJobStatusStore.cs` | Redis-based batch job progress tracking with state machine (Pending → InProgress → Completed/PartiallyCompleted/Failed) |
| DeadLetterQueueService | `src/server/api/Sprk.Bff.Api/Services/Jobs/DeadLetterQueueService.cs` | DLQ inspection (summary, list, get) and re-drive with attempt count reset |
| JobProcessingModule | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs` | DI registration for handlers, Service Bus client, and hosted services |

## Job Handlers (13 handlers)

| Handler | JobType Constant | Domain | Key Behavior |
|---------|-----------------|--------|-------------|
| DocumentProcessingJobHandler | `DocumentProcessing` | Documents | Sample/scaffold handler for generic document operations |
| AppOnlyDocumentAnalysisJobHandler | `AppOnlyDocumentAnalysis` | AI | Runs Document Profile analysis via AppOnlyAnalysisService; idempotency key `analysis-{docId}-documentprofile` |
| EmailAnalysisJobHandler | `EmailAnalysis` | AI/Email | Combined email + attachment AI analysis; idempotency key `emailanalysis-{emailId}` |
| RagIndexingJobHandler | `RagIndexing` | AI/Search | Single-document RAG indexing via FileIndexingService; updates Dataverse tracking fields |
| BulkRagIndexingJobHandler | `BulkRagIndexing` | AI/Search | Batch RAG indexing with bounded concurrency (SemaphoreSlim), Dataverse document query, BatchJobStatusStore progress tracking |
| ProfileSummaryJobHandler | `ProfileSummary` | AI/Office | Office add-in post-save profile extraction; chains to RagIndexing job |
| AttachmentClassificationJobHandler | `AttachmentClassification` | Finance | AI classification of email attachments as invoice candidates; multi-signal entity matching (reference, vendor, parent, keyword) |
| InvoiceExtractionJobHandler | `InvoiceExtraction` | Finance | AI fact extraction via OutputOrchestrator; chains to InvoiceIndexing job |
| InvoiceIndexingJobHandler | `InvoiceIndexing` | Finance/Search | Indexes invoice into Azure AI Search with embeddings (text-embedding-3-large, 3072 dims) |
| SpendSnapshotGenerationJobHandler | `SpendSnapshotGeneration` | Finance | Aggregates BillingEvents into snapshots, evaluates threshold signals, runs finance rollup |
| IncomingCommunicationJobHandler | `IncomingCommunication` | Communication | Processes webhook notifications; delegates to IncomingCommunicationProcessor for message fetch, record creation, .eml archival |
| UploadFinalizationWorker | (Office pipeline) | Office | Moves temp files to SPE, creates Dataverse records, queues profile/indexing stages |
| ProfileSummaryWorker | (Office pipeline) | Office | AI profile generation for Office-uploaded documents |

## Data Flow

### Standard Job Lifecycle

1. **Submission**: API endpoint or background service creates a `JobContract` and calls `JobSubmissionService.SubmitJobAsync()` (or `SubmitCommunicationJobAsync()` for email jobs)
2. **Enqueue**: JobSubmissionService serializes the contract to JSON, sets `MessageId` from IdempotencyKey (SHA-256 hashed if >128 chars), and sends to the appropriate Service Bus queue
3. **Receive**: ServiceBusJobProcessor (or CommunicationJobProcessor) receives the message with PeekLock, creates a DI scope, deserializes `JobContract`
4. **Route**: Processor resolves all `IJobHandler` registrations and finds the handler where `handler.JobType == job.JobType`
5. **Process**: Handler calls `IdempotencyService.IsEventProcessedAsync()` and `TryAcquireProcessingLockAsync()` before executing business logic
6. **Outcome**: Handler returns `JobOutcome` — Completed (complete message), Failed (abandon for redelivery), or Poisoned (dead-letter)
7. **Dead-letter**: Messages are dead-lettered when: poisoned by handler, `job.IsAtMaxAttempts`, `DeliveryCount >= 5`, or 3 unhandled exceptions

### Job Chaining Pattern

Several handlers chain to downstream jobs upon completion:

- `ProfileSummaryJobHandler` → queues `RagIndexing` job
- `InvoiceExtractionJobHandler` → queues `InvoiceIndexing` job
- `UploadFinalizationWorker` → queues profile or indexing stage

### Queue Isolation

- **`sdap-jobs`**: Shared queue for all non-communication handlers. ServiceBusJobProcessor enumerates all IJobHandler registrations to find the match.
- **`sdap-communication`**: Dedicated queue for IncomingCommunication jobs only. CommunicationJobProcessor resolves IncomingCommunicationJobHandler directly (no enumeration), preventing cross-domain DI failures.

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Azure Service Bus | ServiceBusClient | Connection string from `ConnectionStrings:ServiceBus` |
| Depends on | Redis (IDistributedCache) | IdempotencyService, BatchJobStatusStore | Deduplication keys, processing locks, batch status |
| Depends on | Dataverse | IDocumentDataverseService, IGenericEntityService | Record CRUD for documents, invoices, matters |
| Depends on | Azure AI Search | SearchIndexClient, SearchClient | Invoice and document indexing |
| Depends on | Azure OpenAI | IOpenAiClient | Embedding generation, AI analysis |
| Consumed by | API endpoints | JobSubmissionService | Endpoints enqueue jobs for async processing |
| Consumed by | ScheduledRagIndexingService | JobSubmissionService | Timer-based bulk indexing submission |
| Consumed by | DeadLetterQueueService | Admin endpoints | DLQ inspection and re-drive |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Message bus over in-process queue | Azure Service Bus with PeekLock | Durability, retries, dead-letter, multi-instance scaling | ADR-001, ADR-004 |
| Uniform job contract | Single JobContract class for all job types | Consistent routing, idempotency, retry semantics | ADR-004 |
| Domain queue isolation | Separate communication queue | Prevents finance/AI DI failures from blocking email processing | — |
| Handler registration | Scoped IJobHandler via DI | Each handler gets fresh DI scope per message; safe for transient dependencies | ADR-010 |
| Idempotency via Redis | Distributed cache with TTL-based keys | Fail-open on cache miss; processing locks prevent concurrent execution | ADR-004 |
| Re-drive resets attempts | Attempt counter set to 0 on re-drive | Allows full retry budget after manual DLQ intervention | — |

## Constraints

- **MUST**: Use `JobContract` for all async job submissions — no ad-hoc message formats
- **MUST**: Every handler must be idempotent — calling `ProcessAsync` multiple times with the same IdempotencyKey must be safe
- **MUST**: Return `JobOutcome.Poisoned` for permanent failures (not-found, invalid payload) — do not allow infinite retries
- **MUST**: Register handlers as `IJobHandler` in `JobProcessingModule` — the processor discovers handlers via `GetServices<IJobHandler>()`
- **MUST NOT**: Make Graph/HTTP calls from Dataverse plugins — use job handlers for async processing (ADR-002)
- **MUST NOT**: Use Azure Functions for background processing (ADR-001)
- **MUST**: Use `JobSubmissionService.SubmitCommunicationJobAsync()` for email jobs — not the shared queue

## Known Pitfalls

- **Handler DI resolution failure**: If any single IJobHandler implementation has an unresolvable dependency, `GetServices<IJobHandler>()` throws for ALL handlers on the shared queue. This is why communication jobs use a separate queue with direct resolution.
- **IdempotencyKey length**: Service Bus MessageId has a 128-character limit. JobSubmissionService auto-hashes keys longer than 128 chars via SHA-256, but callers should keep keys short when possible.
- **MaxAutoLockRenewalDuration**: Set to 10 minutes on the shared processor. Long-running handlers (BulkRagIndexing can process hundreds of documents) must complete within this window or the lock will be lost and the message redelivered.
- **Batch status TTL**: BatchJobStatusStore records expire after 7 days. Status polling endpoints will return null for expired jobs.
- **Dead-letter re-drive**: Re-driven messages get a new MessageId (Guid), so Service Bus duplicate detection will not reject them even if the original IdempotencyKey was used.

## Related

- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API + BackgroundService (no Azure Functions)
- [ADR-004](../../.claude/adr/ADR-004-job-contract.md) — Async job contract and uniform processing
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism
- [communication-service-architecture.md](communication-service-architecture.md) — Email processing pipeline
- [finance-intelligence-architecture.md](finance-intelligence-architecture.md) — Invoice classification and extraction pipeline
