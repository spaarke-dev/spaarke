# Background Workers Pattern

## When
Adding async job processing via Service Bus queue.

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
