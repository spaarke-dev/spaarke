# ADR-036: Background-Job Infrastructure (Spaarke.Scheduling) (Concise)

> **Status**: Accepted
> **Domain**: BFF API / Background Workers / Shared Library
> **Last Updated**: 2026-06-21
> **Source project**: `spaarke-platform-foundations-r3` Part 2 (closes the "28 BackgroundService implementations with no shared framework" gap surfaced during R2 UAT)
> **Cross-references**: extends ADR-001 (in-process workers); reinforces ADR-010 (DI minimalism); reuses ADR-012 (shared library convention); aligns with CLAUDE.md §10.

---

## Decision

A new shared library `src/server/shared/Spaarke.Scheduling/` provides a uniform contract + host + admin surface for **schedule-driven** background jobs across Spaarke. Cron parsing via the `Cronos` NuGet package (~63 KB). Job definitions persist in a new Dataverse entity `sprk_backgroundjob`; per-run instances in `sprk_backgroundjobrun`. Admin operators control jobs via `/api/admin/jobs/*` endpoints behind the existing `SystemAdmin` policy.

**TWO reference consumers ship in R3** to prove the framework end-to-end:

1. `PlaybookSchedulerJob` — migration of the legacy `PlaybookSchedulerService` (deleted). Single `sprk_backgroundjob` row (`jobId="notification-playbook-scheduler"`) that fans out across the 7 active notification playbooks. Each child playbook receives a **fresh `correlationId`** per Q1 owner clarification; the parent run records all children correlationIds in `sprk_backgroundjobrun.sprk_resultjson` for trace correlation.
2. `MembershipReconciliationJob` — nightly reconciliation of the Phase 2 `sprk_userentityassociation` junction table against source-of-truth lookups (real reconciliation logic per Phase 2 owner decision; NOT a no-op).

**Out of scope for R3** (opportunistic migration over time):
- The other 26 `BackgroundService` implementations in BFF — they keep their bespoke patterns until touched.
- Queue-consumer services (`ServiceBusJobProcessor` family) — different shape (event-driven, not schedule-driven).

**Boundary vs `sprk_processingjob`** (existing Office-scoped per-document job tracker): `sprk_backgroundjob*` is a PARALLEL family for scheduled jobs across all domains. The two coexist deliberately; do NOT overload `sprk_processingjob` with non-Office JobTypes.

---

## Three Patterns

| Pattern | When to use | Behavior |
|---|---|---|
| **Job definition** | Persistent schedule for a single logical job | Insert one `sprk_backgroundjob` row; `IBackgroundJobStore.LoadJobsAsync` returns it; host owns the cron tick |
| **Single-row fan-out** | One scheduled tick triggers N concrete sub-units (e.g., 7 playbooks) | One job row + one IScheduledJob impl that loops; per-child correlationId; record children in `JobRunResult.ResultJson` (Q1) |
| **Admin trigger ("Run Now")** | Operator-initiated run outside the cron schedule | POST `/api/admin/jobs/{jobId}/trigger` → `ScheduledJobHost.TriggerNowAsync` → fresh run row with `trigger=ManualAdmin` |

---

## Constraints

### ✅ MUST

- **MUST** implement `IScheduledJob` (JobId, DisplayName, Description, `ExecuteAsync(JobRunContext, CancellationToken)`) for any new schedule-driven background work. Do not introduce a new `BackgroundService` for cron-style work without explicit ADR amendment.
- **MUST** seed a `sprk_backgroundjob` row at host startup (idempotent) for every registered `IScheduledJob`. Operators tune cron via Dataverse, not appsettings.
- **MUST** record every run in `sprk_backgroundjobrun` with `correlationId` (NFR-08), `trigger` (Scheduled / ManualAdmin / OnStartup), `startedOn`, `completedOn`, `status` (Running / Success / Failed / Cancelled), `errorMessage` on failure, `processedItems` when meaningful, `resultJson` for handler-specific output.
- **MUST** honor `CancellationToken` end-to-end; `ScheduledJobHost.StopAsync` drains in-flight jobs within 30s (NFR-07).
- **MUST** apply `JobRetryPolicy` (default: 3 attempts, 5s base, 2min cap, exponential 2^(attempt-1)). Failures after exhaustion record `status=Failed` with the last `errorMessage`.
- **MUST** use idempotency via `IBackgroundJobStore.HasRunForScheduledTimeAsync(jobId, scheduledFireUtc)` to prevent duplicate execution on host restart mid-tick.
- **MUST** register `ScheduledJobHost` as `Singleton` AND `AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>())` (singleton-identity forwarder so admin trigger and cron loop share `_inFlight` state).
- **MUST** gate admin endpoints with `RequireAuthorization("SystemAdmin")` (per Q6 owner clarification — use existing policy at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241); do NOT create a new "PlatformAdmin" policy).
- **MUST** apply `bff-extensions.md §A` (pre-merge checklist) + `§F.1` (asymmetric-registration anti-pattern guard) on every BFF-touching task that adds an `IScheduledJob` consumer.
- **MUST** publish-size delta ≤ +1 MB per task; cumulative ceiling 60 MB (NFR-01).

### ❌ MUST NOT

- **MUST NOT** extend `sprk_processingjob` with new `JobType` values to track scheduled jobs. That entity is Office-scoped (DocumentId lookup is meaningless for non-Office); the parallel `sprk_backgroundjob*` family handles scheduled work.
- **MUST NOT** use `Hangfire`, `Quartz.NET`, or external schedulers. ADR-001 prefers in-process; current pattern works at expected volume.
- **MUST NOT** treat a scheduled job as a JPS playbook (the inverse of the actual migration path). `PlaybookSchedulerJob` runs playbooks; it is not itself a playbook.
- **MUST NOT** require operators to redeploy to disable a job. `POST /api/admin/jobs/{jobId}/disable` flips `sprk_backgroundjob.sprk_enabled` and triggers immediate `ScheduledJobHost.RefreshDefinitionsAsync` (no waiting for hourly refresh).
- **MUST NOT** swallow exceptions in `IScheduledJob.ExecuteAsync` silently. Either retry (within policy) or fail loudly with `status=Failed` + `errorMessage` surfaced via `/api/admin/jobs/{jobId}/status`.

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
    Task<bool> HasRunForScheduledTimeAsync(string jobId, DateTime scheduledFireUtc, CancellationToken ct);
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
| POST | `/api/admin/jobs/{jobId}/trigger` | Run NOW; returns 202 + `{runId, status, startedAt}` |
| POST | `/api/admin/jobs/{jobId}/enable` | Enable scheduled execution; 204 |
| POST | `/api/admin/jobs/{jobId}/disable` | Disable without removing; 204 |

All require `SystemAdmin` policy (Q6).

---

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| Extend `sprk_processingjob` with new JobType values | Overloads entity beyond Office domain intent; couples scheduled jobs to Office; `DocumentId` lookup is meaningless |
| Treat scheduled jobs as playbooks | Wrong abstraction — junction sync isn't an AI playbook; awkward for non-playbook jobs (cache warming, reconciliation, etc.) |
| Hardcoded C# constants + `appsettings.json` (current state) | What we're upgrading from; no maker control, no "Run Now", no central registry, no run-history audit |
| Migrate all 28 services in R3 | High risk; some services have subtle behaviors; opportunistic migration is safer — only the 2 reference consumers ship in R3 |
| `Hangfire` / `Quartz.NET` / external scheduler | Adds dependency; existing `BackgroundService + PeriodicTimer` pattern works fine at our scale; ADR-001 prefers in-process |

---

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-001](ADR-001-minimal-api.md) | In-process workers; no Azure Functions |
| [ADR-008](ADR-008-endpoint-filters.md) | Admin endpoints use endpoint-filter auth (NOT global middleware) |
| [ADR-009](ADR-009-redis-caching.md) | Future: cache-warming jobs may consume the framework |
| [ADR-010](ADR-010-di-minimalism.md) | `IScheduledJob` / `IBackgroundJobStore` allowed as testing seams (≥2 implementations from day 1) |
| [ADR-012](ADR-012-shared-components.md) | Spaarke.Scheduling is a new shared .NET library |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | NFR-01 publish-size enforcement |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | If any IScheduledJob is feature-gated, apply Null-Object pattern (P1/P2/P3) |
| [ADR-034](ADR-034-user-record-membership.md) | Phase 2 `MembershipReconciliationJob` is the second reference consumer |

---

## See Also

- Full ADR: [`docs/adr/ADR-036-background-job-infrastructure.md`](../../docs/adr/ADR-036-background-job-infrastructure.md)
- Spec: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 2
- Data model: [`docs/data-model/sprk_backgroundjob.md`](../../docs/data-model/sprk_backgroundjob.md), [`docs/data-model/sprk_backgroundjobrun.md`](../../docs/data-model/sprk_backgroundjobrun.md)
- Constraints: [`.claude/constraints/bff-extensions.md`](../constraints/bff-extensions.md) §§A, F.1
