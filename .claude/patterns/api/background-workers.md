# Background Workers Pattern (Queue-Driven)

> **Last Reviewed**: 2026-11-25
> **Reviewed By**: spaarke-intake-pipeline-r1 task 005 (exemplar repointed after `DocumentProcessingJobHandler` was deleted; key rule updated)
> **Status**: Verified

## When
Adding async job processing via **Service Bus queue** (event-triggered work — incoming message kicks off a handler). For **schedule-driven** work (cron, interval, daily-at-time), use [`scheduled-jobs.md`](scheduled-jobs.md) instead (`IScheduledJob` + `Spaarke.Scheduling` framework, R3).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` — Job envelope schema (JobId, JobType, IdempotencyKey, Payload)
2. `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` — BackgroundService that routes jobs to handlers
3. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` — Submit jobs to Service Bus queue
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs` — Handler exemplar (idempotency key built by a static helper, `ProfileIdempotencyKey`, that every enqueue site calls)
5. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkersModule.cs` — Worker DI registration

## Constraints
- **ADR-052**: where the work runs (BFF, Azure Functions, Container Apps Jobs) is decided per workload; this pattern is the in-BFF mechanism for queue-driven work
- **ADR-004**: Idempotent job processing — an **atomic** per-message claim before the side effect and a completion marker after (check-then-set is not enough — ADR-004 A1 §4). `MessageId` de-duplicates only where the queue has duplicate detection enabled; it is off on the BFF queues (A1 §5)
- **ADR-017**: Retry with exponential backoff; dead-letter after max attempts

## Key Rules
- Implement `IJobHandler` interface (JobType string + ProcessAsync returning JobOutcome)
- Idempotency key: one key per unit of work, built in one place and reused by every enqueue site (e.g. `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey` → `analysis-{documentId}-documentprofile`; `Communication:{messageId}:Process`). Two sites building different keys for the same work run it twice
- Register handlers as Scoped in WorkersModule
