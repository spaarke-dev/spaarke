# Scheduled-Jobs Pattern (Spaarke.Scheduling Framework)

> **Last Reviewed**: 2026-06-22
> **Reviewed By**: spaarke-platform-foundations-r3 Wave 28
> **Status**: Verified
> **Source**: ADR-036 · R3 spec Part 2

## When
Adding any **schedule-driven** background work (cron, interval, daily-at-time). Use `IScheduledJob` + `Spaarke.Scheduling` framework. For **queue-driven** workers (Service Bus / event-triggered), see [`background-workers.md`](background-workers.md) instead.

## Read These Files
1. `src/server/shared/Spaarke.Scheduling/IScheduledJob.cs` — contract (JobId, DisplayName, Description, ExecuteAsync)
2. `src/server/shared/Spaarke.Scheduling/JobRunContext.cs` + `JobRunResult.cs` — input/output records
3. `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs` — BackgroundService that ticks; you do NOT write a host
4. `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` — worked example: single-row fan-out across N child runs with fresh correlationIds (Q1)
5. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipReconciliationJob.cs` — worked example: discovery-driven Dataverse scan + dispatch via shared handler
6. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SchedulingModule.cs` — registration pattern + bootstrap seed
7. `.claude/adr/ADR-036-background-job-infrastructure.md` — binding rules
8. `.claude/patterns/ai/node-executor-authoring.md` — companion pattern (Singleton+Scoped DI applies)

## Constraints
- **ADR-036**: Implement `IScheduledJob`; do NOT create a new `BackgroundService` for cron-style work
- **ADR-010**: Job class is Singleton; use `IServiceScopeFactory.CreateScope()` per ExecuteAsync if you depend on Scoped services (Dataverse client, OBO token cache, MembershipResolver)
- **ADR-001**: In-process BackgroundService; no Azure Functions
- **NFR-07**: Honor `CancellationToken` end-to-end; StopAsync drains within 30s
- **NFR-08**: Every run records `correlationId`; for fan-out, every child gets a fresh per-child correlationId (Q1)
- **bff-extensions.md §A**: Pre-merge checklist applies (publish-size + CVE + tests)
- **MUST NOT**: extend `sprk_processingjob` for scheduled work; that entity is Office-scoped

## Key Rules

### Contract
- Implement `IScheduledJob` (the framework owns the host, retry, idempotency, persistence)
- `JobId` is the unique key — also used in `/api/admin/jobs/{jobId}/*` endpoints
- Return `JobRunResult` with `Success`, `ErrorMessage`, `ProcessedItems`, `Duration`, optional `ResultJson` (e.g., per-child correlationIds for fan-out)

### DI Lifetime (Singleton + Scoped)
- Register your `IScheduledJob` impl as Singleton in `SchedulingModule.cs`
- Inject `IServiceScopeFactory`; per ExecuteAsync invocation: `using var scope = _scopeFactory.CreateScope();` then resolve Scoped dependencies from `scope.ServiceProvider`
- Mirror `PlaybookSchedulerJob` + `MembershipReconciliationJob` exactly — do NOT invent a new lifetime pattern

### Seed at startup
- Add seed in `SchedulingBootstrapHostedService` (or a sibling) — set `JobId`, `CronSchedule`, `Enabled=true|false`, optional `ConfigJson`
- Seed is idempotent (`AddOrReplaceJob`) — survives restart without duplicating rows
- Cron schedules use Cronos syntax (5-field standard; `0 2 * * *` = daily at 02:00 UTC)

### Reuse over duplicate
- Need to write to Dataverse junction? Reuse `IMembershipJunctionUpdater` (R3 task 084)
- Need to publish events? Reuse `IMembershipEventPublisher` (R3 task 081)
- Need to invalidate cache? Reuse `IMembershipCacheInvalidator` (R3 task 086)
- Pattern: a recon job is a Dataverse-only path; an event-driven flow is a publish-then-consume path; both share the same junction writer

### Tests (mandatory)
- `Validate_*` tests for argument guards
- `ExecuteAsync_HappyPath_*` for primary code path
- `ExecuteAsync_CancellationRequested_*` for NFR-07
- `ExecuteAsync_PerRowError_LogsAndContinues_*` (don't fail whole job on one bad row)
- `ExecuteAsync_Result_ContainsExpectedBreakdown` (verify ResultJson shape for admin visibility)

### Admin surface (free, by registering)
- `GET /api/admin/jobs/{jobId}/status` — operators see last run + history
- `POST /api/admin/jobs/{jobId}/trigger` — operators run NOW without redeploy
- `POST /api/admin/jobs/{jobId}/enable|disable` — operators pause without redeploy
- All gated by existing `SystemAdmin` policy (Q6 — do NOT create new policy)

## When NOT to Use This Pattern
- **Queue-triggered** processing (Service Bus message handler): use [`background-workers.md`](background-workers.md) → `IJobHandler` + `ServiceBusJobProcessor`
- **One-shot startup work**: just `IHostedService` directly (no scheduling needed)
- **Long-lived workers** (SignalR connection holders, etc.): `IHostedService` directly; not migration candidates

## Companion Pattern Docs
- [`background-workers.md`](background-workers.md) — queue-driven sibling
- [`../ai/node-executor-authoring.md`](../ai/node-executor-authoring.md) — Singleton+Scoped DI for AI node executors (same pattern)
- [`error-handling.md`](error-handling.md) — JobRunResult.ErrorMessage shape
- [`resilience.md`](resilience.md) — JobRetryPolicy defaults (3 attempts, 5s base, 2min cap)
