# Background Workers Architecture

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: All BackgroundService and IHostedService implementations — lifecycle, scheduling patterns, and coordination

---

## Overview

The SDAP BFF API hosts 17 background workers that run as .NET `BackgroundService` or `IHostedService` implementations within the single API process. Per ADR-001, all background processing uses this pattern — no Azure Functions. Workers fall into four categories: Service Bus queue processors, periodic timer services, event-driven channel consumers, and startup-only services.

This architecture keeps operational complexity low (single deployment unit) while providing durable async processing, scheduled maintenance, and real-time event handling.

## Component Structure

### Service Bus Queue Processors (4)

These workers consume Azure Service Bus queues and process messages continuously.

| Component | Path | Queue | Scheduling |
|-----------|------|-------|-----------|
| ServiceBusJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | `sdap-jobs` | Continuous; configurable MaxConcurrentCalls |
| CommunicationJobProcessor | `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationJobProcessor.cs` | `sdap-communication` | Continuous; half of MaxConcurrentCalls |
| UploadFinalizationWorker | `src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs` | `office-upload-finalization` | Continuous; 5 concurrent calls |
| ProfileSummaryWorker | `src/server/api/Sprk.Bff.Api/Workers/Office/ProfileSummaryWorker.cs` | `office-profile` | Continuous; 5 concurrent calls |
| IndexingWorkerHostedService | `src/server/api/Sprk.Bff.Api/Workers/Office/IndexingWorkerHostedService.cs` | `office-indexing` | Continuous; 5 concurrent calls |

### Periodic Timer Services (7)

These workers execute on a recurring schedule using `PeriodicTimer` or delay-until-time patterns.

| Component | Path | Interval | Pattern |
|-----------|------|----------|---------|
| ScheduledRagIndexingService | `src/server/api/Sprk.Bff.Api/Services/Jobs/ScheduledRagIndexingService.cs` | Configurable (default: 60 min) | PeriodicTimer; opt-in via `ScheduledRagIndexing:Enabled` |
| GraphSubscriptionManager | `src/server/api/Sprk.Bff.Api/Services/Communication/GraphSubscriptionManager.cs` | 30 minutes | PeriodicTimer; creates/renews Graph webhook subscriptions for all receive-enabled mailboxes |
| InboundPollingBackupService | `src/server/api/Sprk.Bff.Api/Services/Communication/InboundPollingBackupService.cs` | 5 minutes | PeriodicTimer; polls mailboxes for emails missed by webhooks; multi-layer deduplication |
| DailySendCountResetService | `src/server/api/Sprk.Bff.Api/Services/Communication/DailySendCountResetService.cs` | Once daily at midnight UTC | Delay-until-midnight loop; resets `sprk_sendstoday` for all communication accounts |
| DemoExpirationService | `src/server/api/Sprk.Bff.Api/Services/Registration/DemoExpirationService.cs` | Once daily at midnight UTC | Delay-until-midnight loop; expires demo accounts (disable Entra, revoke SPE access, notify) |
| PlaybookSchedulerService | `src/server/api/Sprk.Bff.Api/Services/PlaybookSchedulerService.cs` | 1 hour | PeriodicTimer; executes notification-mode playbooks for all active users; 5 parallel users per playbook |
| TodoGenerationService | `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs` | 24 hours | PeriodicTimer; scans for deadline-approaching and budget-alert conditions, creates to-do records |
| SpeDashboardSyncService | `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/SpeDashboardSyncService.cs` | 15 minutes (default) | PeriodicTimer + on-demand Channel trigger; syncs SPE container metrics to Redis cache |

### Event-Driven Channel Consumers (2)

These workers read from in-memory `Channel<T>` instances, processing items as they arrive.

| Component | Path | Trigger |
|-----------|------|---------|
| BulkOperationService | `src/server/api/Sprk.Bff.Api/Services/SpeAdmin/BulkOperationService.cs` | Channel enqueue from bulk operation endpoints; processes delete/permission operations with per-item progress |
| PlaybookIndexingBackgroundService | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingBackgroundService.cs` | Channel enqueue from trigger endpoint; indexes playbook embeddings; exposes static Instance accessor (ADR-010) |

### One-Time Migration Services (2)

These workers run once at startup (if enabled) and then exit.

| Component | Path | Purpose |
|-----------|------|---------|
| DocumentVectorBackfillService | `src/server/api/Sprk.Bff.Api/Services/Jobs/DocumentVectorBackfillService.cs` | Backfills `documentVector` by averaging chunk `contentVector` values; opt-in via `DocumentVectorBackfill:Enabled` |
| EmbeddingMigrationService | `src/server/api/Sprk.Bff.Api/Services/Jobs/EmbeddingMigrationService.cs` | Migrates embeddings from 1536 to 3072 dimensions (text-embedding-3-large); opt-in via `EmbeddingMigration:Enabled`; supports resume via `ResumeFromDocumentId` |

### Startup Validation (1)

| Component | Path | Purpose |
|-----------|------|---------|
| StartupValidationService | `src/server/api/Sprk.Bff.Api/Infrastructure/Startup/StartupValidationService.cs` | IHostedService (not BackgroundService); validates Graph, Dataverse, ServiceBus, Redis configuration at startup; fails fast on missing config |

## Data Flow

### Lifecycle Management

1. **Startup**: All workers register via `AddHostedService<T>()` in their respective DI modules. The .NET host starts all IHostedService implementations in registration order.
2. **Startup delay**: Most workers include a 10-30 second initial delay (`Task.Delay`) to allow dependencies (Dataverse, Graph, Service Bus) to warm up before the first execution.
3. **Execution**: Each BackgroundService runs `ExecuteAsync()` until `stoppingToken` is cancelled. Workers catch `OperationCanceledException` for graceful shutdown.
4. **Error resilience**: All periodic services catch exceptions per-cycle and continue to the next interval. A single failure never crashes the background service loop.
5. **Shutdown**: The host calls `StopAsync()` on each worker. Service Bus processors explicitly stop and dispose their `ServiceBusProcessor` instances.

### Scheduling Patterns

**PeriodicTimer** (preferred for fixed intervals):
- Used by: ScheduledRagIndexingService, GraphSubscriptionManager, InboundPollingBackupService, PlaybookSchedulerService, TodoGenerationService, SpeDashboardSyncService
- Pattern: `using var timer = new PeriodicTimer(interval); while (await timer.WaitForNextTickAsync(ct)) { ... }`

**Delay-until-time** (for wall-clock scheduling):
- Used by: DailySendCountResetService, DemoExpirationService
- Pattern: Calculate delay from now to next midnight UTC, `await Task.Delay(delay, ct)`, then execute

**Channel consumer** (for event-driven processing):
- Used by: BulkOperationService, PlaybookIndexingBackgroundService
- Pattern: `await foreach (var item in channel.Reader.ReadAllAsync(ct)) { ... }`

**Run-once** (for migrations):
- Used by: DocumentVectorBackfillService, EmbeddingMigrationService
- Pattern: Check `Enabled` flag, execute migration, then exit `ExecuteAsync()`

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Azure Service Bus | ServiceBusClient, ServiceBusProcessor | Queue processors depend on Service Bus connectivity |
| Depends on | Redis | IDistributedCache | Idempotency, batch status, dashboard metrics caching |
| Depends on | Dataverse | IOrganizationServiceAsync, IGenericEntityService | Record queries and updates for scheduled tasks |
| Depends on | Microsoft Graph | GraphServiceClient, IGraphClientFactory | Subscription management, mailbox polling, user management |
| Depends on | Azure OpenAI | IOpenAiClient | Embedding generation in migration services |
| Depends on | Azure AI Search | SearchIndexClient | Index operations in migration and indexing services |
| Consumed by | API endpoints | BulkOperationService.EnqueueAsync() | Endpoints submit bulk ops to channel for background processing |
| Consumed by | API endpoints | SpeDashboardSyncService refresh channel | On-demand dashboard refresh trigger |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| All background work in-process | BackgroundService in BFF API | Single deployment unit; no Azure Functions overhead | ADR-001 |
| Opt-in migration services | Disabled by default via config flag | Prevents migration services from running in all environments | — |
| Queue isolation by domain | Separate queues for shared jobs, communication, and Office pipeline | DI failure isolation; independent scaling | ADR-004 |
| PeriodicTimer over Timer/CronJob | .NET 6+ PeriodicTimer | No timer drift, cancellation-aware, simpler than cron | ADR-001 |
| Channel for in-process events | System.Threading.Channels | Back-pressure-free FIFO with minimal allocation; no external dependency | ADR-010 |
| Static accessor for PlaybookIndexingService | `PlaybookIndexingBackgroundService.Instance` | Avoids adding DI registration; trigger endpoint accesses via static | ADR-010 |

## Constraints

- **MUST**: Use BackgroundService pattern for all background processing — no Azure Functions (ADR-001)
- **MUST**: Register all hosted services via `AddHostedService<T>()` in the appropriate DI module
- **MUST**: Include startup delay (10-30s) in services that depend on external systems (Dataverse, Graph)
- **MUST**: Catch exceptions per-cycle in periodic services — never let a single failure crash the loop
- **MUST**: Handle `OperationCanceledException` gracefully for clean shutdown
- **MUST NOT**: Block `ExecuteAsync` with synchronous operations — all work must be async
- **MUST NOT**: Enable migration services (DocumentVectorBackfill, EmbeddingMigration) in production without explicit configuration review

## Known Pitfalls

- **All workers share one process**: A memory leak or CPU spike in any worker affects all others. Monitor per-worker telemetry via structured logging tags.
- **Startup ordering**: Workers start in DI registration order, not dependency order. Workers that depend on other workers being ready must use startup delays or feature flags.
- **App Service auto-restart**: Azure App Service may recycle the process, stopping all workers. Long-running migrations (EmbeddingMigration) support `ResumeFromDocumentId` for this reason.
- **Channel backpressure**: BulkOperationService uses an unbounded channel. Extremely large bulk operations could consume significant memory. The UI should limit batch sizes.
- **Daily services at midnight**: DailySendCountResetService and DemoExpirationService both fire at midnight UTC. If the app restarts just before midnight, the delay calculation ensures they still fire at the next midnight rather than immediately.
- **Graph subscription expiry**: Graph mail subscriptions expire after 3 days. If GraphSubscriptionManager is down for more than 3 days, subscriptions expire silently. InboundPollingBackupService provides fallback coverage.

## Related

- [jobs-architecture.md](jobs-architecture.md) — Job contract, handlers, idempotency, dead-letter management
- [communication-service-architecture.md](communication-service-architecture.md) — Email pipeline including GraphSubscriptionManager and InboundPollingBackupService
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API + BackgroundService (no Azure Functions)
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism
