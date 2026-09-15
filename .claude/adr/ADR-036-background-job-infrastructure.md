# ADR-036: Background-Job Infrastructure (Spaarke.Scheduling) (Concise)

> **Status**: Accepted, as amended (A1, 2026-09-12) — shipped in R3 (2026-06-22)
> **Domain**: BFF API / Background Workers / Shared Library
> **Last Updated**: 2026-09-14 (A1.1 — a lease store that is down means no dispatch; task 103 implemented rules 1, 2 and 6)
> **Source project**: `spaarke-platform-foundations-r3` Part 2 (closes the "28 BackgroundService implementations with no shared framework" gap surfaced during R2 UAT)
> **Cross-references**: **where** scheduled work runs → [ADR-052](ADR-052-workload-placement.md); this ADR = **how** it runs inside the BFF. Reinforces ADR-010 (DI minimalism); reuses ADR-012 (shared library convention); aligns with CLAUDE.md §10.

> ⚠️ **READ A1 FIRST** ([full ADR](../../docs/adr/ADR-036-background-job-infrastructure.md#amendment-a1-2026-09-12-placement-runtime-as-built-and-one-dispatch-per-schedule)).
> It records how the scheduler actually runs today and adds the dispatch, idempotency, retry, heartbeat and
> registration rules. MUSTs marked *target state* describe the Dataverse-backed store, which does not exist yet.

---

## Decision

A shared library `src/server/shared/Spaarke.Scheduling/` provides a uniform contract + host + admin surface for **schedule-driven** background jobs **inside the BFF**. Cron parsing via the `Cronos` NuGet package (~63 KB). Job definitions and per-run records are designed to persist in Dataverse (`sprk_backgroundjob`, `sprk_backgroundjobrun` — deployed, not yet used; *target state*). Admin operators control jobs via `/api/admin/jobs/*` endpoints behind the existing `SystemAdmin` policy.

**Reference consumers**:

1. **`PlaybookSchedulerJob` — SHIPPED in R3** (task 023). Migration of the legacy `PlaybookSchedulerService` (deleted). One job (`jobId="notification-playbook-scheduler"`) that fans out across the 7 active notification playbooks, each child with a **fresh `correlationId`** (Q1); children's correlationIds recorded in `JobRunResult.ResultJson`.
2. **`MembershipReconciliationJob` — SHIPPED** (registered in `MembershipModule`).
3. **`GrantExpiryReminderJob`** — `unified-access-control-r2` task 100 (FR-33 expiry reminders).

**Out of scope** (migration over time):
- Existing hand-rolled timer `BackgroundService`s migrate **when next touched** (ADR-052 §1; an ArchTest ratchet lets the set only shrink).
- Queue consumers (`ServiceBusJobProcessor` family) — ADR-004, not this ADR.

**Boundary vs `sprk_processingjob`** (existing Office-scoped per-document job tracker): `sprk_backgroundjob*` is a PARALLEL family for scheduled jobs across all domains. The two coexist deliberately; do NOT overload `sprk_processingjob` with non-Office JobTypes.

---

## Runtime as built (A1 §2 — verified in code 2026-09-12)

- The only store is `InMemoryBackgroundJobStore`; no `DataverseBackgroundJobStore` exists. Cron and enablement are set in code at registration.
- `ScheduledJobHost` starts on every instance, and every dispatch runs under a distributed lease (Redis, `IScheduledJobLease`) that also records the last dispatched occurrence — so each tick runs **once across instances**. Non-production slots run no ticks (`Scheduling__RunScheduledJobs=false`, slot-sticky). Task 103, 2026-09-14.
- `HasRunForScheduledTimeAsync` is **inert** for its stated purpose (memory is lost on restart; `AdvanceNextFire` already prevents in-process re-fire).
- Admin `disable` changes the store on the one instance that served the request; a restart re-seeds the registration default. Neither durable nor fleet-wide.

---

## Three Patterns

| Pattern | When to use | Behavior |
|---|---|---|
| **Job definition** | Persistent schedule for a single logical job | Registered with a `BackgroundJobDefinition`; `IBackgroundJobStore.LoadJobsAsync` returns it; host owns the cron tick |
| **Single-row fan-out** | One scheduled tick triggers N concrete sub-units (e.g., 7 playbooks) | One job + one IScheduledJob impl that loops; per-child correlationId; record children in `JobRunResult.ResultJson` (Q1) |
| **Admin trigger ("Run Now")** | Operator-initiated run outside the cron schedule | POST `/api/admin/jobs/{jobId}/trigger` → `ScheduledJobHost.TriggerNowAsync` → fresh run with `trigger=ManualAdmin` |

---

## Constraints

### ✅ MUST

- **MUST** implement `IScheduledJob` (JobId, DisplayName, Description, `ExecuteAsync(JobRunContext, CancellationToken)`) for new schedule-driven work that ADR-052 places in the BFF. No new hand-rolled timer `BackgroundService`.
- **MUST (A1-1) one dispatch per schedule** for jobs that must not run concurrently: a distributed lease that outlives the whole run including every retry, and makes a manual trigger and a scheduled tick mutually exclusive; a host without the lease records **skipped**. No lease store configured → dispatch + warn once. Store configured but unavailable → retry the acquire with the job backoff, then **do not dispatch**; record the tick **failed** and log an error (**A1.1**, owner decision 2026-09-14). As built: `ScheduledJobHost` + `RedisScheduledJobLease`; a manual trigger of a running job gets 409.
- **MUST (A1-2) slots**: non-production deployment slots do not run scheduled jobs (slot-sticky `Scheduling__RunScheduledJobs=false`, set by `scripts/Deploy-BffApi.ps1 -UseSlotDeploy`).
- **MUST (A1-3) idempotency**: an **atomic claim** per unit of work before its side effect, a **completion marker** after it; a failed unit releases its claim.
- **MUST (A1-4) retry**: throw from `ExecuteAsync` when a retry could complete work this tick would otherwise lose (no progress possible, or transient unit failures no later tick revisits); otherwise count failures and complete. New jobs; existing jobs when next touched.
- **MUST (A1-5) heartbeat**: one structured heartbeat per attempt with its counts and attempt number, including an attempt with nothing to do.
- **MUST (A1-6) registration**: `services.AddScheduledJob<TJob>(cron, enabled)` — no per-job bootstrap class; the registry and the in-memory store fill themselves from the registrations (`WorkloadPlacementGuardTests.ScheduledJobsRegisterThroughAddScheduledJobOnly`).
- **MUST (A1-7) host-neutrality**: jobs do not depend on `ScheduledJobHost`, `IBackgroundJobStore` or `ScheduledJobRegistry` (ADR-052 §5).
- **MUST** honor `CancellationToken` end-to-end; `ScheduledJobHost.StopAsync` drains in-flight jobs within 30s (NFR-07).
- **MUST** apply `JobRetryPolicy` (default: 3 attempts; no delay before attempt 1, then `BaseDelay·2^(attempt-2)` — 5s, 10s — capped at 2min). It retries only when `ExecuteAsync` throws.
- **MUST** register `ScheduledJobHost` as `Singleton` AND `AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>())` (singleton-identity forwarder so admin trigger and cron loop share `_inFlight` state).
- **MUST** gate admin endpoints with `RequireAuthorization("SystemAdmin")` (Q6 — use the existing policy at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241); do NOT create a new "PlatformAdmin" policy).
- **MUST** apply `bff-extensions.md §A` (pre-merge checklist) + `§F.1` (asymmetric-registration guard) on every BFF-touching task that adds an `IScheduledJob` consumer.
- **MUST** publish-size delta ≤ +1 MB per task; cumulative ceiling 60 MB (NFR-01).
- *Target state (A1 §2)* — seed a `sprk_backgroundjob` row per job so operators tune cron in Dataverse; record every run in `sprk_backgroundjobrun` (`correlationId`, `trigger`, `startedOn`, `completedOn`, `status`, `errorMessage`, `processedItems`, `resultJson`). Until `DataverseBackgroundJobStore` exists, run history is process-local.

### ❌ MUST NOT

- **MUST NOT** extend `sprk_processingjob` with new `JobType` values to track scheduled jobs. That entity is Office-scoped; the parallel `sprk_backgroundjob*` family handles scheduled work.
- **MUST NOT** use `Hangfire`, `Quartz.NET`, or another third-party scheduler inside the BFF. Where scheduled work runs at all is ADR-052's decision; inside the BFF, `ScheduledJobHost` is the scheduler.
- **MUST NOT** treat a scheduled job as a JPS playbook. `PlaybookSchedulerJob` runs playbooks; it is not itself a playbook.
- **MUST NOT** require operators to redeploy to disable a job — `POST /api/admin/jobs/{jobId}/disable`. *As built this is per-instance and not durable (A1 §2); the durable, fleet-wide version needs `DataverseBackgroundJobStore`.*
- **MUST NOT** swallow exceptions in `IScheduledJob.ExecuteAsync` silently. Retry (A1-4) or fail loudly with `Success=false` + `ErrorMessage`, visible via `/api/admin/jobs/{jobId}/status`.

---

## Key Types

```csharp
namespace Spaarke.Scheduling;

public interface IScheduledJob
{
    string JobId { get; }
    string DisplayName { get; }
    string Description { get; }
    Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken);
}

public record JobRunContext(
    Guid RunId,
    string CorrelationId,
    JobRunTrigger Trigger,
    IDictionary<string, object> Parameters);

public record JobRunResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration,
    string? ResultJson = null);  // For fan-out children correlationIds, etc.

public enum JobRunTrigger { Scheduled = 1, ManualAdmin = 2, OnStartup = 3 }

public interface IBackgroundJobStore
{
    Task<IReadOnlyList<BackgroundJobDefinition>> LoadJobsAsync(CancellationToken ct);
    Task<Guid> RecordRunStartAsync(string jobId, JobRunTrigger trigger, string correlationId, DateTime? scheduledFireUtc, CancellationToken ct);
    Task RecordRunCompleteAsync(Guid runId, JobRunResult result, CancellationToken ct);
    Task<IReadOnlyList<BackgroundJobRunRecord>> GetRecentRunsAsync(string jobId, int limit, CancellationToken ct);
    Task<bool> HasRunForScheduledTimeAsync(string jobId, DateTime scheduledFireUtc, CancellationToken ct);  // inert as built (A1 §2)
    Task<bool> SetEnabledAsync(string jobId, bool enabled, CancellationToken ct);
}
```

---

## Admin Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/jobs` | List registered jobs + status (last run, next scheduled) |
| GET | `/api/admin/jobs/{jobId}/status` | Detailed status + last 10 runs |
| GET | `/api/admin/jobs/{jobId}/history?limit=50` | Run history (default 50, max 500) |
| POST | `/api/admin/jobs/{jobId}/trigger` | Run NOW; returns 202 + `{runId, status, startedAt}` · 409 if the job is running · 503 if the lease store is down |
| POST | `/api/admin/jobs/{jobId}/enable` | Enable scheduled execution; 204 |
| POST | `/api/admin/jobs/{jobId}/disable` | Disable without removing; 204 |

All require `SystemAdmin` policy (Q6). As built, every one of them answers for the single instance that served the request.

---

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| Extend `sprk_processingjob` with new JobType values | Overloads entity beyond Office domain intent; couples scheduled jobs to Office; `DocumentId` lookup is meaningless |
| Treat scheduled jobs as playbooks | Wrong abstraction — junction sync isn't an AI playbook; awkward for non-playbook jobs (cache warming, reconciliation, etc.) |
| Hardcoded C# constants + `appsettings.json` (pre-R3 state) | No "Run Now", no central registry, no run-history audit |
| Migrate all 28 services in R3 | High risk; some services have subtle behaviors; migration when next touched is safer |
| `Hangfire` / `Quartz.NET` / third-party scheduler in the BFF | Adds a dependency; `ScheduledJobHost` + Cronos is sufficient inside the BFF. Work that needs a scheduler outside the BFF is placed under ADR-052 (e.g. a Functions timer) |

---

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-052](ADR-052-workload-placement.md) | Where scheduled work runs; this ADR = the in-BFF mechanism |
| [ADR-004](ADR-004-job-contract.md) | Queue-driven work (not this ADR) |
| [ADR-008](ADR-008-endpoint-filters.md) | Admin endpoints use endpoint-filter auth (NOT global middleware) |
| [ADR-009](ADR-009-redis-caching.md) | The distributed lease store (task 103); cache-warming jobs may consume the framework |
| [ADR-010](ADR-010-di-minimalism.md) | `IScheduledJob` / `IBackgroundJobStore` allowed as testing seams (≥2 implementations from day 1) |
| [ADR-012](ADR-012-shared-components.md) | Spaarke.Scheduling is a shared .NET library |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | NFR-01 publish-size enforcement |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | If any IScheduledJob is feature-gated, apply Null-Object pattern (P1/P2/P3) |
| [ADR-034](ADR-034-user-record-membership.md) | `MembershipReconciliationJob` is a reference consumer |

---

## See Also

- Full ADR: [`docs/adr/ADR-036-background-job-infrastructure.md`](../../docs/adr/ADR-036-background-job-infrastructure.md) (A1 in full)
- Spec: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 2
- Data model: [`docs/data-model/sprk_backgroundjob.md`](../../docs/data-model/sprk_backgroundjob.md), [`docs/data-model/sprk_backgroundjobrun.md`](../../docs/data-model/sprk_backgroundjobrun.md)
- Constraints: [`.claude/constraints/bff-extensions.md`](../constraints/bff-extensions.md) §§A, D, F.1
