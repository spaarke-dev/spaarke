# Spaarke.Scheduling Architecture

> **Last Updated**: 2026-06-22
> **Last Reviewed**: 2026-06-22
> **Reviewed By**: spaarke-platform-foundations-r3 Wave 16 (task 092 — architecture doc)
> **Status**: Shipped (R3 Part 2 — framework + 2 reference consumers live on spaarkedev1)
> **Purpose**: Shared cron-driven background-job framework — contract, host, run history, admin surface, lifetime pattern, and migration roadmap for the 26 remaining ad-hoc `BackgroundService` workers

---

## Overview (plain language)

`Spaarke.Scheduling` is a small shared .NET library that provides **one** uniform way to write, schedule, monitor, retry, and manually trigger a cron-driven background job inside the BFF.

Before R3 Part 2, every scheduled worker (rag indexing, daily reset, demo expiration, playbook scheduler, …) was a hand-rolled `BackgroundService` with its own `PeriodicTimer`, its own retry shape, its own logging conventions, and zero operator surface — turning a job on or off required an appsettings change + redeploy, and there was no run-history audit. The R2 UAT found 28 such `BackgroundService` implementations with no shared framework; this is what the new library replaces.

The framework consists of four pieces:

1. **`IScheduledJob` contract** — implement one interface, one method, return a result record.
2. **`ScheduledJobHost`** — the single `BackgroundService` that owns the cron loop for every registered job (one host, N jobs).
3. **`IBackgroundJobStore`** — persistence seam for definitions + run history (in-memory today on spaarkedev1; Dataverse-backed swap is a one-line change once the production rollout calls for it).
4. **`/api/admin/jobs/*` endpoints** — `SystemAdmin`-policy surface for list / status / history / trigger-now / enable / disable.

**Positioning vs the existing patterns** (see [background-workers-architecture.md](background-workers-architecture.md) for the full inventory and [jobs-architecture.md](jobs-architecture.md) for the queue-driven sibling):

| Concern | `Spaarke.Scheduling` | Service Bus jobs (`jobs-architecture.md`) | Hand-rolled timer `BackgroundService` |
|---|---|---|---|
| Trigger shape | Cron schedule (Cronos) | Queue message arrival | `PeriodicTimer` / `Task.Delay` |
| Persistence | `sprk_backgroundjob` + `sprk_backgroundjobrun` | Service Bus + Redis idempotency | None (or per-worker bespoke) |
| Admin surface | `/api/admin/jobs/*` (6 endpoints) | DLQ inspect / re-drive only | None |
| Retry | `JobRetryPolicy` (3 / 5s / 2min / exp) | Service Bus delivery count + DLQ | Per-worker bespoke |
| Migration story | Opportunistic — 26 workers remain | Stable | Target of `Spaarke.Scheduling` migration |

The two reference consumers shipping in R3 (`PlaybookSchedulerJob`, `MembershipReconciliationJob`) prove the framework end-to-end on a real Dataverse environment.

---

## Component Inventory

### Framework library — `src/server/shared/Spaarke.Scheduling/`

| Component | Path | Purpose |
|---|---|---|
| `IScheduledJob` | [`IScheduledJob.cs:11-27`](../../src/server/shared/Spaarke.Scheduling/IScheduledJob.cs#L11) | Contract every job implements: `JobId`, `DisplayName`, `Description`, `ExecuteAsync(JobRunContext, CancellationToken)`. Permitted as an interface under ADR-010 as a testing/swap seam. |
| `JobRunContext` | [`JobRunContext.cs:11-15`](../../src/server/shared/Spaarke.Scheduling/JobRunContext.cs#L11) | Per-run input record — `RunId`, `CorrelationId` (NFR-08), `Trigger`, `Parameters` (sourced from `sprk_backgroundjob.sprk_configjson` or admin-trigger request body). |
| `JobRunResult` | [`JobRunResult.cs:29-34`](../../src/server/shared/Spaarke.Scheduling/JobRunResult.cs#L29) | Per-run outcome — `Success`, `ErrorMessage?`, `ProcessedItems?`, `Duration`, `ResultJson?` (R3 task 023 / FR-2.8 — opaque per-handler JSON for admin UI). |
| `JobRunTrigger` (enum) | [`JobRunTrigger.cs:8-18`](../../src/server/shared/Spaarke.Scheduling/JobRunTrigger.cs#L8) | `Scheduled = 1`, `ManualAdmin = 2`, `OnStartup = 3`. |
| `ScheduledJobHost` | [`ScheduledJobHost.cs:47-917`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs#L47) | The single `BackgroundService` that owns the cron loop. One scheduling loop computes the next fire across all enabled jobs via Cronos and sleeps until the earliest (one timer, not N — see "Design choices" in the class XML doc). Hourly definition refresh (FR-2.3). 30-second graceful drain (NFR-07). Per-tick idempotency probe via `HasRunForScheduledTimeAsync` (FR-2.3). `TriggerNowAsync` ([`L200-283`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs#L200)) backs the admin "Run Now" endpoint with fire-and-track semantics. `RefreshDefinitionsAsync` ([`L573`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs#L573)) is public so admin enable/disable forces immediate re-evaluation without waiting for the hourly refresh. |
| `ScheduledJobHostOptions` | [`ScheduledJobHostOptions.cs:12-31`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobHostOptions.cs#L12) | POCO knobs: `RefreshInterval` (default 1h), `ShutdownDrainTimeout` (default 30s), `MaxLoopSleep`, `RetryPolicy`. No `IOptions` wrapper (ADR-010). |
| `ScheduledJobRegistry` | [`ScheduledJobRegistry.cs:20-49`](../../src/server/shared/Spaarke.Scheduling/ScheduledJobRegistry.cs#L20) | Singleton in-memory `ConcurrentDictionary<JobId, IScheduledJob>`. O(1) lookup; duplicate-id register throws. Populated at startup by feature-module bootstrap hosted services. |
| `IBackgroundJobStore` | [`IBackgroundJobStore.cs:21-150`](../../src/server/shared/Spaarke.Scheduling/IBackgroundJobStore.cs#L21) | Persistence seam — `LoadJobsAsync`, `RecordRunStartAsync`, `RecordRunCompleteAsync`, `HasRunForScheduledTimeAsync` (idempotency probe), `GetRecentRunsAsync`, `SetEnabledAsync`. ADR-010 justified by ≥2 implementations from day one. |
| `BackgroundJobDefinition` (record) | [`IBackgroundJobStore.cs:203-209`](../../src/server/shared/Spaarke.Scheduling/IBackgroundJobStore.cs#L203) | Immutable view of one `sprk_backgroundjob` row — `JobId`, `DisplayName`, `Description`, `Enabled`, `CronSchedule`, `ConfigJson?`. |
| `BackgroundJobRunRecord` (record) | [`IBackgroundJobStore.cs:180-191`](../../src/server/shared/Spaarke.Scheduling/IBackgroundJobStore.cs#L180) | Stable projection of one `sprk_backgroundjobrun` row surfaced to admin tooling. Includes `ResultJson` (R3 task 023). |
| `InMemoryBackgroundJobStore` | [`InMemoryBackgroundJobStore.cs:23-100+`](../../src/server/shared/Spaarke.Scheduling/InMemoryBackgroundJobStore.cs#L23) | Current backing store on spaarkedev1. Two `ConcurrentDictionary` instances — `_jobs` for definitions, `_runs` for history. Seeded at startup by feature-module bootstrap hosted services via `AddOrReplaceJob`. Run history is process-local (lost on App Service restart) — acceptable for the R3 P3 admin-surface validation goal; the Dataverse-backed swap (tasks 015/016 entities now deployed) is a one-line change in `SchedulingModule`. |
| `JobRetryPolicy` | [`JobRetryPolicy.cs:29-70`](../../src/server/shared/Spaarke.Scheduling/JobRetryPolicy.cs#L29) | POCO: `MaxAttempts = 3`, `BaseDelay = 5s`, `MaxDelay = 2min`. Formula: `BaseDelay × 2^(attemptNumber-2)`, capped at `MaxDelay`. No jitter (in-process single-caller-per-tick — deterministic delays are easier to reason about for tests + ops than the HTTP-jitter case Polly addresses). |
| `JobNotFoundException` | [`JobNotFoundException.cs`](../../src/server/shared/Spaarke.Scheduling/JobNotFoundException.cs) | Thrown by `TriggerNowAsync` when the JobId is unknown to the registry; mapped to 404 ProblemDetails by the admin endpoint. |

### BFF wiring — `src/server/api/Sprk.Bff.Api/`

| Component | Path | Purpose |
|---|---|---|
| `SchedulingModule.AddSchedulingModule` | [`Infrastructure/DI/SchedulingModule.cs:75-131`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/SchedulingModule.cs#L75) | DI registration for the framework primitives + `PlaybookSchedulerJob`. **Unconditional** per bff-extensions.md §F.1 asymmetric-registration rule (admin endpoints map unconditionally; their dependencies must too). Registers `ScheduledJobHost` as `Singleton` + forwards to `AddHostedService` so admin trigger and cron loop share the same `_inFlight` state. |
| `SchedulingModule.SchedulingBootstrapHostedService` | [`Infrastructure/DI/SchedulingModule.cs:157-213`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/SchedulingModule.cs#L157) | One-shot startup hosted service that (1) registers `PlaybookSchedulerJob` with `ScheduledJobRegistry` and (2) seeds the `notification-playbook-scheduler` definition row with cron `0 * * * *`. Inserted at index 0 of the hosted-services list so it runs BEFORE `ScheduledJobHost`'s first tick. Idempotent on host restart. |
| `MembershipModule.MembershipReconciliationBootstrapHostedService` | [`Infrastructure/DI/MembershipModule.cs:313-369`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/MembershipModule.cs#L313) | One-shot startup hosted service for `MembershipReconciliationJob`. Mirrors the scheduling-module bootstrap pattern; seeds `membership-reconciliation` with cron `0 2 * * *` (daily 02:00 UTC). Honors `MembershipReconciliationOptions.Enabled` (true by default). Independent of Service Bus topic deploy. |
| `JobsEndpoints.MapAdminJobsEndpoints` | [`Api/Admin/JobsEndpoints.cs:49-153`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L49) | Maps the 6 admin endpoints under `/api/admin/jobs` with `RequireAuthorization("SystemAdmin")`. Endpoint mapping is unconditional. |

### Reference consumers (shipping in R3)

| Consumer | Path | Job ID | Seed cron | Purpose |
|---|---|---|---|---|
| `PlaybookSchedulerJob` | [`Services/Ai/PlaybookSchedulerJob.cs:67-662`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs#L67) | `notification-playbook-scheduler` ([`L74`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs#L74)) | `0 * * * *` (hourly) | R3 task 023 single-row fan-out across 7 active notification playbooks; fresh per-child correlationId (Q1 owner clarification) recorded in `JobRunResult.ResultJson` so operators can join parent ↔ children. Migrates the legacy `PlaybookSchedulerService` (deleted). Preserves the legacy 1h tick cadence (NFR-04). |
| `MembershipReconciliationJob` | [`Services/Ai/Membership/MembershipReconciliationJob.cs:146-…`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipReconciliationJob.cs#L146) | `membership-reconciliation` ([`L154`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipReconciliationJob.cs#L154)) | `0 2 * * *` (daily 02:00 UTC, configurable via `MembershipReconciliationOptions.CronSchedule`) | R3 task 085 nightly source-of-truth reconciliation of `sprk_userentityassociation` against the 8 `sprk_assigned*` Lookups on `sprk_matter` + `sprk_task` + `sprk_opportunity`. Independent of task 071's Service Bus topic deploy — dispatches directly to task 084's `IMembershipJunctionUpdater`. Enabled by default. |

---

## Dataverse Schema

Two new entities form the persistence layer for the framework. Both deployed to **spaarkedev1** and idempotent re-creation scripts live under `scripts/`.

| Entity | Purpose | Data model doc |
|---|---|---|
| `sprk_backgroundjob` | Catalog of scheduled-job definitions — one row per registered `IScheduledJob`. Operators tune cron and enable/disable via this row, not appsettings. Denormalizes most-recent-run status/timestamps for fast list views. | [`docs/data-model/sprk_backgroundjob.md`](../data-model/sprk_backgroundjob.md) |
| `sprk_backgroundjobrun` | Per-run history (1:N with `sprk_backgroundjob`). Each `IScheduledJob.ExecuteAsync` invocation writes one row on start, updated on completion with `status`, `duration`, `processedItems`, optional `resultJson`, or `errorMessage`. Backs idempotency probe + audit + `/api/admin/jobs/{jobId}/history`. | [`docs/data-model/sprk_backgroundjobrun.md`](../data-model/sprk_backgroundjobrun.md) |

On spaarkedev1 today the framework runs against `InMemoryBackgroundJobStore` (run history is process-local); the Dataverse-backed implementation backed by these two entities is a one-line swap in `SchedulingModule` and is the planned production rollout step.

### Why not extend `sprk_processingjob`?

`sprk_processingjob` is **Office-scoped** (per-document, with a `DocumentId` lookup that is meaningless for non-Office jobs and with `JobType` values like `DocumentSave`, `ProfileSummary`, `Indexing`). Overloading it for scheduled cross-domain jobs (cache warmers, reconciliations, etc.) was explicitly rejected (see ADR-036 "Alternatives Considered"). The two families coexist deliberately:

| Family | Scope | Trigger | Schema |
|---|---|---|---|
| `sprk_processingjob` | Office documents — per-document job tracking | Endpoint / Service Bus | DocumentId-coupled |
| `sprk_backgroundjob*` | Any domain — scheduled jobs | Cron / admin manual trigger | JobId-keyed |

**Binding rule** (per ADR-036 MUST NOT): do NOT introduce new `JobType` values on `sprk_processingjob` for scheduled jobs.

---

## Admin Endpoints

All endpoints under `/api/admin/jobs` are guarded by `RequireAuthorization("SystemAdmin")` (the existing policy at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) per Q6 owner clarification — NOT a new `PlatformAdmin` policy). All map unconditionally per bff-extensions.md §F.1.

| Method | Path | Handler | Purpose |
|---|---|---|---|
| GET | `/api/admin/jobs` | [`ListJobsAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L172) | List every registered job + status summary (last run + computed next cron occurrence). 200 + empty list when registry is empty. |
| GET | `/api/admin/jobs/{jobId}/status` | [`GetJobStatusAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L213) | Per-job detail + last 10 runs. AC-2.7: most-recent failure surfaces in `RecentRuns[0].ErrorMessage`. 404 when jobId is not registered. |
| GET | `/api/admin/jobs/{jobId}/history?limit=N` | [`GetJobHistoryAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L365) | Recent runs newest-first. Default limit 50, hard cap 500. 404 when jobId is not registered. |
| POST | `/api/admin/jobs/{jobId}/trigger` | [`TriggerJobAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L295) | Manual out-of-band dispatch via `ScheduledJobHost.TriggerNowAsync`. Returns 202 Accepted + `{runId, status, startedAt}` + `Location` header. Idempotency NOT applied (admin chose to retrigger). |
| POST | `/api/admin/jobs/{jobId}/enable` | [`EnableJobAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L415) | Flips `Enabled = true` + force-refresh host. 204 No Content. 404 when no definition row exists. |
| POST | `/api/admin/jobs/{jobId}/disable` | [`DisableJobAsync`](../../src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs#L430) | Flips `Enabled = false` + force-refresh host. Disabled jobs remain visible in list/status/history but the host skips dispatch. 204 No Content. |

Enable/Disable both pair the store mutation with an immediate `ScheduledJobHost.RefreshDefinitionsAsync` so the new state takes effect on the next scheduling-loop tick rather than waiting for the hourly refresh. Refresh failure is logged at Warning and does NOT roll back the durable store mutation — the hourly refresh is the backstop.

---

## Lifetime + DI Pattern: Singleton-with-Scoped

`IScheduledJob` implementations are registered as **Singleton** (because `ScheduledJobRegistry` is singleton-scoped and the host invokes them across many ticks). But almost every realistic job needs **Scoped** services (DbContext, `IGenericEntityService`, OBO-flavored clients, request-scoped telemetry).

The pattern: inject `IServiceScopeFactory` into the singleton job, and call `CreateScope()` **per `ExecuteAsync` invocation**. Dispose at end of execution. This matches the long-standing pattern used by `AgentServiceNodeExecutor` in the AI subsystem.

**Worked examples** (cite verbatim):

- `PlaybookSchedulerJob` — [`L139`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs#L139) opens a per-execution scope and resolves `IGenericEntityService` from it. Per-user inner work additionally creates a nested `using var userScope = _scopeFactory.CreateScope();` at [`L426`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs#L426) so each parallel user gets isolated `IPlaybookOrchestrationService` state.
- `MembershipReconciliationJob` — same lifetime pattern (Singleton + `IServiceScopeFactory.CreateScope()` per `ExecuteAsync`). The job resolves `IMembershipJunctionUpdater` + `IMembershipFieldDiscoveryService` + `IGenericEntityService` from the fresh scope; the handler is Scoped per `MembershipModule` and the discovery + entity services are Singleton, but the scope is cheap and gives correct disposal for any Scoped collaborators accumulated over time. Documented at [`MembershipReconciliationJob.cs:62-71`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipReconciliationJob.cs#L62).

**Why not register the job itself as Scoped?** Because `ScheduledJobRegistry` is singleton-scoped — a Scoped registration would be captured at startup and pinned to the root provider, defeating the point. The Singleton-with-Scoped-Inner pattern preserves the framework's "one instance per registered job" contract while keeping each execution's dependencies fresh and disposed correctly.

---

## Job Lifecycle

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            HOST STARTUP                                     │
└─────────────────────────────────────────────────────────────────────────────┘
   │
   ├── SchedulingBootstrapHostedService (index 0)
   │      ├── ScheduledJobRegistry.Register(PlaybookSchedulerJob)
   │      └── InMemoryBackgroundJobStore.AddOrReplaceJob("notification-playbook-scheduler", cron="0 * * * *")
   │
   ├── MembershipReconciliationBootstrapHostedService
   │      ├── ScheduledJobRegistry.Register(MembershipReconciliationJob)
   │      └── InMemoryBackgroundJobStore.AddOrReplaceJob("membership-reconciliation", cron="0 2 * * *")
   │
   └── ScheduledJobHost (BackgroundService.StartAsync)
          └── ExecuteAsync ──► RefreshDefinitionsAsync (initial load)

┌─────────────────────────────────────────────────────────────────────────────┐
│                         SCHEDULING LOOP (ExecuteAsync)                      │
└─────────────────────────────────────────────────────────────────────────────┘
   │
   ┌───────────────────► TickAsync (one iteration)
   │                       │
   │                       ├── (every RefreshInterval = 1h) RefreshDefinitionsAsync
   │                       │      ├── store.LoadJobsAsync()
   │                       │      ├── for each definition: registry.Resolve + ParseCron
   │                       │      └── replace _state atomically (snapshot-safe with concurrent ticks)
   │                       │
   │                       ├── for each enabled job whose NextFireUtc ≤ now:
   │                       │      └── DispatchAndAdvance ──► Task.Run(RunJobAsync)
   │                       │                                    │
   │                       │                                    ├── HasRunForScheduledTimeAsync (idempotency probe)
   │                       │                                    │      └── if duplicate → skip (no run row written)
   │                       │                                    │
   │                       │                                    ├── RecordRunStartAsync
   │                       │                                    │      └── (jobId, trigger=Scheduled, correlationId, scheduledFireUtc)
   │                       │                                    │
   │                       │                                    ├── ExecuteWithRetryAsync (JobRetryPolicy)
   │                       │                                    │      ├── attempt 1: handler.ExecuteAsync(JobRunContext, ct)
   │                       │                                    │      ├── on exception → ComputeDelay(2) = 5s wait
   │                       │                                    │      ├── attempt 2: handler.ExecuteAsync(JobRunContext, ct)
   │                       │                                    │      ├── on exception → ComputeDelay(3) = 10s wait
   │                       │                                    │      └── attempt 3 final → JobRunResult.Failure(lastException.Message)
   │                       │                                    │
   │                       │                                    └── RecordRunCompleteAsync(runId, JobRunResult)
   │                       │
   │                       └── sleep until earliest future fire (capped at MaxLoopSleep)
   │
   └── (loop until stoppingToken)

┌─────────────────────────────────────────────────────────────────────────────┐
│                       ADMIN MANUAL TRIGGER (out-of-band)                    │
└─────────────────────────────────────────────────────────────────────────────┘
   POST /api/admin/jobs/{jobId}/trigger
   │
   └── ScheduledJobHost.TriggerNowAsync
         ├── registry.Resolve(jobId) (404 on miss via JobNotFoundException)
         ├── RecordRunStartAsync (trigger=ManualAdmin, scheduledFireUtc=null)
         ├── Task.Run(RunManualTriggerAsync) ──► tracked in _inFlight
         │      ├── ExecuteHandlerWithRetryAsync (same JobRetryPolicy)
         │      └── RecordRunCompleteAsync
         └── return 202 + {runId, "Running", startedAt}    (BEFORE handler completes)

┌─────────────────────────────────────────────────────────────────────────────┐
│                            HOST SHUTDOWN (StopAsync)                        │
└─────────────────────────────────────────────────────────────────────────────┘
   │
   ├── base.StopAsync ──► cancels stoppingToken (in-flight jobs observe cancellation)
   ├── snapshot _inFlight values
   └── Task.WhenAll(snapshot).WaitAsync(ShutdownDrainTimeout = 30s)
         ├── drained cleanly → "all in-flight jobs drained"
         └── timed out → log Warning + count still-running (NFR-07 ceiling reached)
```

Key invariants enforced by the host:

- **Idempotency** — `HasRunForScheduledTimeAsync(jobId, scheduledFireUtc)` is the probe; a host restart mid-tick does NOT re-execute a scheduled run that already wrote a `sprk_backgroundjobrun` row for the same `(jobId, scheduledFireUtc)` pair. Manual triggers do NOT participate (admin chose to retrigger).
- **Correlation (NFR-08)** — every scheduled dispatch + every manual trigger generates a fresh `Guid.NewGuid().ToString("N")` correlation id. Fan-out children (e.g., 7 playbooks under one parent) each receive a fresh child correlationId recorded in the parent's `JobRunResult.ResultJson`.
- **Cancellation (NFR-07)** — every async hop observes `CancellationToken`; `StopAsync` drains in-flight jobs within 30s.
- **Single host identity** — `ScheduledJobHost` is registered as `Singleton` AND forwarded to `AddHostedService` (`SchedulingModule.cs:96,102`). Admin trigger + cron loop MUST share the same `_inFlight` dictionary, otherwise the drain logic would miss runs.

---

## Migration Story — the other 26 BackgroundService implementations

R3 ships the framework + 2 reference consumers. The remaining 26 ad-hoc `BackgroundService` implementations in BFF (rag indexing, daily reset, demo expiration, graph subscription manager, inbound polling backup, etc. — see [`background-workers-architecture.md`](background-workers-architecture.md) for the full inventory) are migrated **opportunistically**: when a task touches the worker for any reason, that's the moment to migrate it to `IScheduledJob`.

**Why opportunistic, not all-at-once**:

- Some workers have subtle behaviors (initial delays, channel triggers, opt-in flags) that need per-worker analysis.
- A flag-day migration is high-risk; an opportunistic migration is naturally batched + reviewed.
- Workers with non-cron triggers (channel consumers, queue processors, one-time migrations) are EXPLICITLY out of scope — see "Out of Scope" below.

A follow-up project `scheduled-jobs-migration` will be scoped in Wave 28 (this repo): its `design.md` will inventory the 26 workers, classify each (cron-driven → migrate; channel-driven → skip; one-shot migration → skip), define the migration recipe (preserve cadence + per-worker tests), and slot the migrations across waves. Until that project lands, the canonical pattern for any new cron-driven work is:

1. Implement `IScheduledJob`.
2. Add a `*BootstrapHostedService` to your feature module's DI module that registers the handler in `ScheduledJobRegistry` + seeds the `sprk_backgroundjob` row (see `SchedulingModule.SchedulingBootstrapHostedService` and `MembershipModule.MembershipReconciliationBootstrapHostedService` as worked examples).
3. Do NOT create a new `BackgroundService` for cron-style work — ADR-036 MUST.

---

## Out of Scope

The following deliberately do NOT migrate to `Spaarke.Scheduling`:

| Family | Why | Stays as |
|---|---|---|
| Service Bus queue consumers (`ServiceBusJobProcessor`, `CommunicationJobProcessor`, `UploadFinalizationWorker`, `ProfileSummaryWorker`, `IndexingWorkerHostedService`) | **Event-driven**, not schedule-driven — different shape. See [`jobs-architecture.md`](jobs-architecture.md). | Existing `BackgroundService` per [`background-workers-architecture.md`](background-workers-architecture.md) §1 |
| Channel consumers (`BulkOperationService`, `PlaybookIndexingBackgroundService`) | In-memory event loop; no schedule. | Existing `BackgroundService` per [`background-workers-architecture.md`](background-workers-architecture.md) §3 |
| One-time migration services (`DocumentVectorBackfillService`, `EmbeddingMigrationService`) | Opt-in run-once-and-exit pattern; cron is the wrong abstraction. | Existing `BackgroundService` per [`background-workers-architecture.md`](background-workers-architecture.md) §4 |
| Startup validation (`StartupValidationService`) | Not periodic — runs once at host start. | Existing `IHostedService` per [`background-workers-architecture.md`](background-workers-architecture.md) §5 |

External schedulers (Hangfire, Quartz.NET, Azure Functions, Logic Apps) are NOT considered — ADR-001 prefers in-process workers; the volume + cadence requirements are met comfortably by a single in-process `BackgroundService`. See ADR-036 "Alternatives Considered" for the full rejection rationale.

---

## Deployment + Operations

**Current state (2026-06-22)**:

- **Framework library** — `Spaarke.Scheduling` ships in every BFF deployment.
- **Entities** — `sprk_backgroundjob` + `sprk_backgroundjobrun` deployed to **spaarkedev1**; idempotent re-creation scripts at `scripts/Create-BackgroundJobEntity.ps1` and `scripts/Create-BackgroundJobRunEntity.ps1`.
- **Backing store** — `InMemoryBackgroundJobStore` is wired on spaarkedev1. Run history is process-local (lost on App Service restart). The Dataverse-backed swap is a one-line change in `SchedulingModule.AddSchedulingModule`.
- **Seeded jobs** — `notification-playbook-scheduler` (cron `0 * * * *`, enabled) and `membership-reconciliation` (cron `0 2 * * *`, enabled by default; honors `Membership:Reconciliation:Enabled` appsettings override).
- **Admin endpoints** — `/api/admin/jobs/*` (all 6) live and unconditional.

**Operator runbook**:

- Trigger a job manually: `POST /api/admin/jobs/{jobId}/trigger` (returns 202 + `runId`; poll `GET /api/admin/jobs/{jobId}/status` for outcome).
- Disable a job without redeploy: `POST /api/admin/jobs/{jobId}/disable` → takes effect on next scheduling-loop tick (forced via `RefreshDefinitionsAsync`).
- Inspect last 50 runs: `GET /api/admin/jobs/{jobId}/history?limit=50` (hard cap 500).
- Re-enable: `POST /api/admin/jobs/{jobId}/enable`.

**Failure observability**: any failed run records the last attempt's exception message in `sprk_backgroundjobrun.sprk_errormessage` and surfaces it through `GET /api/admin/jobs/{jobId}/status` → `RecentRuns[0].ErrorMessage` (AC-2.7). The host's `ExecuteWithRetryAsync` logs every attempt at `Warning` + the final failure at `Error` — App Insights queries grouped by `JobId` + `CorrelationId` work without changes.

---

## Related

- [ADR-036](../../.claude/adr/ADR-036-background-job-infrastructure.md) — Background-job infrastructure (binding decision record, concise) + [full ADR](../adr/ADR-036-background-job-infrastructure.md)
- [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) — Minimal API + in-process workers (no Azure Functions)
- [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint-filter authorization (admin endpoints follow this pattern)
- [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism (justifies `IScheduledJob` + `IBackgroundJobStore` interfaces with ≥2 implementations from day one)
- [ADR-012](../../.claude/adr/ADR-012-shared-components.md) — Shared library convention (`Spaarke.Scheduling` follows this)
- [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — Publish-size discipline (Cronos ~63 KB; per-task ≤ +1 MB; cumulative ≤ 60 MB)
- [ADR-032](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) — Null-Object kill-switch pattern (applies to any feature-gated `IScheduledJob`)
- [ADR-034](../../.claude/adr/ADR-034-user-record-membership.md) — User-record membership (drives `MembershipReconciliationJob`'s reconciliation requirement)
- [background-workers-architecture.md](background-workers-architecture.md) — The broader inventory of all 17 `BackgroundService`/`IHostedService` implementations (Spaarke.Scheduling is the framework that this page's §3 sibling pattern is migrating toward)
- [jobs-architecture.md](jobs-architecture.md) — Service Bus queue-driven sibling family (different trigger shape; same family of "async work")
- [`docs/data-model/sprk_backgroundjob.md`](../data-model/sprk_backgroundjob.md) + [`sprk_backgroundjobrun.md`](../data-model/sprk_backgroundjobrun.md) — Entity schemas + Dataverse-backed store contract
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§A (pre-merge checklist), F.1 (asymmetric-registration anti-pattern guard) — binding for every BFF task that adds an `IScheduledJob` consumer
