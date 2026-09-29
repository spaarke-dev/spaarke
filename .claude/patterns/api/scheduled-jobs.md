# Scheduled-Jobs Pattern (Spaarke.Scheduling Framework)

> **Last Reviewed**: 2026-09-14
> **Reviewed By**: unified-access-control-r2 task 103 (lease, slot guard, `AddScheduledJob<TJob>` shipped)
> **Status**: Verified
> **Source**: ADR-036 (+ Amendment A1, A1.1) · ADR-052 · R3 spec Part 2

## When
Adding **schedule-driven** background work (cron, interval, daily-at-time) that [ADR-052](../../adr/ADR-052-workload-placement.md) places **in the BFF**. Use `IScheduledJob` + `Spaarke.Scheduling`. For **queue-driven** workers (Service Bus / event-triggered), see [`background-workers.md`](background-workers.md) instead. If ADR-052 places the work elsewhere (e.g. a Functions timer), this pattern does not apply.

## Read These Files
1. `src/server/shared/Spaarke.Scheduling/IScheduledJob.cs` — contract (JobId, DisplayName, Description, ExecuteAsync)
2. `src/server/shared/Spaarke.Scheduling/JobRunContext.cs` + `JobRunResult.cs` — input/output records
3. `src/server/shared/Spaarke.Scheduling/ScheduledJobRegistration.cs` — `AddScheduledJob<TJob>(cron, enabled)`, the only way a job reaches the scheduler
4. `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs` — the BackgroundService that ticks, takes the lease and retries; you do NOT write a host
5. `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` — worked example: single-row fan-out across N child runs with fresh correlationIds (Q1)
6. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipReconciliationJob.cs` — worked example: discovery-driven Dataverse scan + dispatch via shared handler
7. `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SchedulingModule.cs` — host, lease (`RedisScheduledJobLease`) and slot guard wiring; `MembershipModule.cs` — a job registered from config
8. `.claude/adr/ADR-036-background-job-infrastructure.md` — binding rules, **Amendment A1 first**
9. `.claude/patterns/ai/node-executor-authoring.md` — companion pattern (Singleton+Scoped DI applies)

## Constraints
- **ADR-036**: Implement `IScheduledJob`; do NOT add a hand-rolled timer `BackgroundService` for cron-style work (ADR-052 §1)
- **ADR-036 A1**: one dispatch per schedule is the **host's** job — its lease; you write nothing for it · an **atomic claim** per unit of work before its side effect + a completion marker after · **throw** from `ExecuteAsync` when a retry could complete work this tick would otherwise lose, otherwise count failures and complete · one structured heartbeat log per attempt (including "nothing to do") · register via `AddScheduledJob<TJob>` · no dependency on `ScheduledJobHost` / `IBackgroundJobStore` / `ScheduledJobRegistry` / `IScheduledJobLease`
- **ADR-036 A1.1**: if Redis is down at tick time the tick is NOT dispatched (recorded failed). A job that must not lose a tick catches up on its next one — design for that
- **ADR-052**: where scheduled work runs is decided per workload; this pattern is the in-BFF mechanism
- **ADR-010**: Job class is Singleton; use `IServiceScopeFactory.CreateScope()` per ExecuteAsync if you depend on Scoped services (Dataverse client, OBO token cache, MembershipResolver)
- **NFR-07**: Honor `CancellationToken` end-to-end; StopAsync drains within 30s. The token is also cancelled when the run can no longer hold its lease — another holder took it, Redis was unreachable for a full lease duration, or the run passed `ScheduledJobHostOptions.MaxRunDuration` (2 h; a job that legitimately runs longer needs a larger value)
- **NFR-08**: Every run records `correlationId`; for fan-out, every child gets a fresh per-child correlationId (Q1)
- **bff-extensions.md §A**: Pre-merge checklist applies (publish-size + CVE + tests)
- **MUST NOT**: extend `sprk_processingjob` for scheduled work; that entity is Office-scoped

## Key Rules

### Contract
- Implement `IScheduledJob`. The framework owns the host, the cron tick, the lease and `JobRetryPolicy` (which retries only when `ExecuteAsync` throws). **You own per-unit idempotency** — the lease makes a tick run once, but a retry re-runs units, and the store's `HasRunForScheduledTimeAsync` is process-local
- `JobId` is the unique key — also used in `/api/admin/jobs/{jobId}/*` endpoints and in the lease's Redis key
- Return `JobRunResult` with `Success`, `ErrorMessage`, `ProcessedItems`, `Duration`, optional `ResultJson` (e.g., per-child correlationIds for fan-out). Never set `Skipped` — the host sets it

### DI Lifetime (Singleton + Scoped)
- `AddScheduledJob<TJob>` registers your job as a Singleton
- Inject `IServiceScopeFactory`; per ExecuteAsync invocation: `using var scope = _scopeFactory.CreateScope();` then resolve Scoped dependencies from `scope.ServiceProvider`
- Mirror `PlaybookSchedulerJob` + `MembershipReconciliationJob` exactly — do NOT invent a new lifetime pattern

### Registration
- `services.AddScheduledJob<TJob>(cron, enabled)` from the module that owns the job — any module, any order. That is all: the registry and the in-memory store fill themselves from it. No bootstrap hosted service (an ArchTest fails on one)
- An unparseable cron fails the app at startup
- Definitions are re-seeded on every start — there are no Dataverse rows yet (target state)
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
- A claim test: a unit whose side effect failed is applied exactly once across a re-run
- The lease, slot guard and registration are covered once, in `tests/unit/Spaarke.Scheduling.Tests` — do not re-test them per job

### Admin surface (free, by registering)
- `GET /api/admin/jobs/{jobId}/status` — last run + history (a tick another instance ran shows here as `Skipped`)
- `POST /api/admin/jobs/{jobId}/trigger` — run NOW without redeploy; 409 if the job is already running, 503 if the lease store is down
- `POST /api/admin/jobs/{jobId}/enable|disable` — pause without redeploy
- All gated by existing `SystemAdmin` policy (Q6 — do NOT create new policy)
- ⚠️ As built, each answers for the **one instance** that served the request, and enable/disable does not survive a restart (ADR-036 A1 §2)

## When NOT to Use This Pattern
- Work that ADR-052 places outside the BFF (Functions timer, Container Apps job)
- **Queue-triggered** processing (Service Bus message handler): use [`background-workers.md`](background-workers.md) → `IJobHandler` + `ServiceBusJobProcessor`
- **One-shot startup work**: just `IHostedService` directly (no scheduling needed)
- **Long-lived non-message connections** (e.g. a Redis pub/sub subscriber): `IHostedService` directly. A queue or topic consumer is never this — it is ADR-004 work

## Companion Pattern Docs
- [`background-workers.md`](background-workers.md) — queue-driven sibling
- [`../ai/node-executor-authoring.md`](../ai/node-executor-authoring.md) — Singleton+Scoped DI for AI node executors (same pattern)
- [`error-handling.md`](error-handling.md) — JobRunResult.ErrorMessage shape
- [`resilience.md`](resilience.md) — JobRetryPolicy defaults (3 attempts, 5s base, 2min cap)
