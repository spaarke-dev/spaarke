# Background Workers Pattern (Queue-Driven)

> **Last Reviewed**: 2026-06-22
> **Reviewed By**: spaarke-platform-foundations-r3 Wave 28 (refreshed to distinguish queue-vs-schedule shape)
> **Status**: Verified

## When
Adding async job processing via **Service Bus queue** (event-triggered work — incoming message kicks off a handler). For **schedule-driven** work (cron, interval, daily-at-time), use [`scheduled-jobs.md`](scheduled-jobs.md) instead (`IScheduledJob` + `Spaarke.Scheduling` framework, R3).

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` — Job envelope schema (JobId, JobType, IdempotencyKey, Payload)
2. `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` — BackgroundService that routes jobs to handlers
3. `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` — Submit jobs to Service Bus queue
4. `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentProcessingJobHandler.cs` — Handler exemplar
5. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkersModule.cs` — Worker DI registration

## Constraints
- **ADR-001**: Use BackgroundService, not Azure Functions
- **ADR-004**: Idempotent job processing — check idempotency key before work
- **ADR-017**: Retry with exponential backoff; dead-letter after max attempts

## Key Rules
- Implement `IJobHandler` interface (JobType string + ProcessAsync returning JobOutcome)
- Idempotency key patterns: `doc-{id}-v{version}`, `analysis-{id}-{type}`, `email-{messageId}`
- Register handlers as Scoped in WorkersModule
