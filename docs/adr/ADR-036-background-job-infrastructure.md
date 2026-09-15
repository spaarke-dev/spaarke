# ADR-036: Background-Job Infrastructure (Spaarke.Scheduling)

| Field | Value |
|-------|-------|
| Status | **Accepted, as amended** |
| Date | 2026-06-21 |
| Updated | 2026-09-12 (Amendment A1) |
| Authors | Spaarke Engineering, R3 project |
| Source project | `spaarke-platform-foundations-r3` Part 2 |
| Supersedes | n/a |
| Cross-references | **Where** scheduled work runs → [ADR-052](ADR-052-workload-placement.md); this ADR governs **how** it runs inside the BFF (A1 §1 withdraws the original <!-- adr052-drift:allow reason="quotes the withdrawn cross-reference" -->"extends ADR-001 (in-process workers)"<!-- /adr052-drift:allow --> reference). Reinforces ADR-010 (DI minimalism); reuses ADR-012 (shared library); aligns with CLAUDE.md §10 (BFF hygiene). |

> ⚠️ **READ [Amendment A1](#amendment-a1-2026-09-12-placement-runtime-as-built-and-one-dispatch-per-schedule) FIRST.**
> It corrects this ADR's statement of ADR-001, records how the scheduler actually runs today, and adds the dispatch,
> idempotency, retry, heartbeat and registration rules. Statements below about Dataverse-persisted job definitions,
> run history and operator-tuned cron describe the **target state** (A1 §2) — the only store today is in-memory.

---

## Context

The BFF (`src/server/api/Sprk.Bff.Api/`) currently has **28 `BackgroundService` implementations** with NO shared framework. R2 UAT (2026-06-19/20) surfaced operational pain points that affect every scheduled background process in Spaarke:

| Category | Count | Pattern | Examples |
|---|---|---|---|
| Queue consumers (Service Bus driven) | 5 | `ServiceBusJobProcessor` consumes queue, dispatches to handlers | `ServiceBusJobProcessor`, `CommunicationJobProcessor`, `IndexingWorkerHostedService`, `UploadFinalizationWorker`, `ProfileSummaryWorker` |
| Periodic timers (scheduled) | 7 | `PeriodicTimer + IOptions<XOptions>` per service; no shared base | `ScheduledRagIndexingService`, `PlaybookSchedulerService`, `DemoExpirationService`, `DailySendCountResetService`, `InboundPollingBackupService`, `ManifestRefreshService`, `GraphSubscriptionManager` |
| One-shot startup | 5 | `IHostedService` runs once during host start | `StartupValidationService`, `CapabilityManifestInitializer`, `EmbeddingMigrationService`, `DocumentVectorBackfillService`, `PlaybookIndexingBackgroundService` |
| Domain workers | 11 | Mixed responsibilities | `TodoGenerationService`, `SpeDashboardSyncService`, `BulkOperationService`, `SessionFilesCleanupJob`, `RecordSyncJob`, etc. |

**Confirmed gaps**:
- ❌ No central `IScheduledJobRegistry`
- ❌ No admin endpoint to list / trigger / inspect any scheduled job
- ❌ Each service hardcodes enabled flag + interval in its own Options class
- ❌ No shared run-history audit
- ✅ Only `PlaybookSchedulerService` had any Dataverse-driven schedule (in `sprk_analysisplaybook.sprk_configjson`)

**Concrete operational consequences** (raised during R2 UAT):
- Operators waited up to 1 hour to test a playbook config change (no "Run Now" admin trigger).
- No central registry meant troubleshooting "is the job firing?" required reading source code per service.
- Migration debt accumulated silently — new services kept inventing the same `PeriodicTimer + IOptions` pattern.

**The existing `sprk_processingjob` entity** is scoped to Office document operations (JobTypes: `DocumentSave`, `EmailSave`, `ShareLinks`, `QuickCreate`, `ProfileSummary`, `Indexing`, `DeepAnalysis`). Tracks individual job RUN instances with stages/progress/idempotency/correlation. **NOT a fit for general scheduled-job tracking** — overloading it would couple all scheduled jobs to the Office domain.

---

## Decision

Introduce a small shared library `Spaarke.Scheduling` + two new Dataverse entities + a thin admin endpoint surface. Migrate the existing `PlaybookSchedulerService` as the first reference consumer; ship `MembershipReconciliationJob` (Phase 2 of Part 1) as the second. Leave the other 26 services unchanged for opportunistic migration over time.

### 1. New shared library: `Spaarke.Scheduling`

Lives at `src/server/shared/Spaarke.Scheduling/`. Depends only on `Spaarke.Core` + `Cronos` NuGet (~63 KB, MIT-licensed). Used by BFF and any future service.

**Contract types** (public surface):

```csharp
public interface IScheduledJob
{
    string JobId { get; }                   // unique key, e.g., "membership-reconciliation"
    string DisplayName { get; }
    string Description { get; }
    Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken ct);
}

public record JobRunContext(
    Guid RunId,
    string CorrelationId,
    JobRunTrigger Trigger,                  // Scheduled | ManualAdmin | OnStartup
    IDictionary<string, object> Parameters);

public record JobRunResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration,
    string? ResultJson = null);             // Handler-specific output (e.g., fan-out children correlationIds — see Q1 below)

public enum JobRunTrigger { Scheduled = 1, ManualAdmin = 2, OnStartup = 3 }
```

**Persistence abstraction** `IBackgroundJobStore`:

```csharp
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

R3 ships `InMemoryBackgroundJobStore` (default registration). A `DataverseBackgroundJobStore` will replace it in a follow-up task when the first cron job needs durable run history; until then, the in-memory store is sufficient for the two reference consumers.

### 2. Host: `ScheduledJobHost : BackgroundService`

- Reads `sprk_backgroundjob` rows on startup + refreshes hourly (or on-demand via `RefreshDefinitionsAsync` after admin enable/disable)
- For each enabled job, parses cron via Cronos → computes next-fire → dispatches `IScheduledJob.ExecuteAsync` at the right time
- Wraps each invocation in `JobRetryPolicy` (default 3 attempts; no delay before attempt 1, then `BaseDelay·2^(attempt-2)` — 5s, 10s — capped at 2min; corrected 2026-09-13 against `JobRetryPolicy.cs`)
- Persists `sprk_backgroundjobrun` row per invocation; idempotency probe via `HasRunForScheduledTimeAsync` prevents duplicate execution on restart mid-tick
- Honors `CancellationToken` end-to-end; `StopAsync` drains in-flight jobs within 30s (NFR-07)

**Registration** (canonical):

```csharp
// SchedulingModule.cs
services.AddSingleton<ScheduledJobHost>();
services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>());  // forwarder so trigger + cron share state
services.AddSingleton<ScheduledJobRegistry>();
services.AddSingleton<IBackgroundJobStore, InMemoryBackgroundJobStore>();
```

### 3. Dataverse entities

**`sprk_backgroundjob`** (job definitions):

| Field | Type | Purpose |
|---|---|---|
| `sprk_jobid` | Text (unique key) | E.g., "membership-reconciliation" |
| `sprk_displayname` | Text | Human-readable |
| `sprk_description` | Multiline | What the job does |
| `sprk_handlertype` | Text | Fully-qualified C# class name (informational) |
| `sprk_enabled` | Boolean | Master enable/disable |
| `sprk_cronschedule` | Text | Standard cron (e.g., "0 2 * * *") |
| `sprk_configjson` | Multiline | Handler-specific config |
| `sprk_lastrunstartedon` | DateTime | Latest run snapshot |
| `sprk_lastruncompletedon` | DateTime | Latest run snapshot |
| `sprk_lastrunstatus` | OptionSet (Success / Failed / Running / Cancelled) | Latest run snapshot |
| `sprk_lastrunerror` | Multiline | Latest error |

**`sprk_backgroundjobrun`** (per-run instances; lookup to `sprk_backgroundjob`):

| Field | Type | Purpose |
|---|---|---|
| `sprk_backgroundjob` | Lookup → sprk_backgroundjob | Parent definition |
| `sprk_runid` | Text (unique) | Unique per run (GUID-shaped; Dataverse rejects custom Uniqueidentifier creation per data-model note) |
| `sprk_trigger` | OptionSet (Scheduled / ManualAdmin / OnStartup) | |
| `sprk_correlationid` | Text | For distributed tracing (NFR-08) |
| `sprk_startedon` | DateTime | |
| `sprk_completedon` | DateTime | |
| `sprk_status` | OptionSet (Running / Success / Failed / Cancelled) | |
| `sprk_errormessage` | Multiline | |
| `sprk_processeditems` | Whole number | Optional metric |
| `sprk_resultjson` | Multiline | Handler-specific output (children correlationIds, etc.) |
| `sprk_scheduledfireon` | DateTime | Backs `HasRunForScheduledTimeAsync` idempotency probe |

### 4. Admin endpoints (`/api/admin/jobs/*`)

All gated by `RequireAuthorization("SystemAdmin")` — **Q6 owner clarification: use the EXISTING `SystemAdmin` policy** at [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241) (checks `Admin`/`SystemAdmin` role or `scope` claim containing "admin"). Do NOT create a new "PlatformAdmin" policy — the spec.md originally proposed that name but the audit during R3 confirmed `SystemAdmin` already exists and is used by `RagEndpoints.cs:157`.

| Method | Path | Purpose | Code |
|---|---|---|---|
| GET | `/api/admin/jobs` | List all registered jobs + status | `JobStatusSummary[]` |
| GET | `/api/admin/jobs/{jobId}/status` | Detailed status + last 10 runs | `JobStatusDetail` |
| GET | `/api/admin/jobs/{jobId}/history?limit=50` | Run history (default 50, max 500) | `JobRunDetail[]` |
| POST | `/api/admin/jobs/{jobId}/trigger` | Run NOW (Trigger=ManualAdmin); fire-and-track | 202 + `TriggerResponse` |
| POST | `/api/admin/jobs/{jobId}/enable` | Enable scheduled execution | 204 |
| POST | `/api/admin/jobs/{jobId}/disable` | Disable without removing | 204 |

### 5. Two reference consumers (R3)

1. **`MembershipReconciliationJob`** (NEW for Part 1 Phase 2): nightly reconciliation of `sprk_userentityassociation` junction rows vs source-of-truth lookups. Owner promoted Phase 2 from "design-only" to firm in-scope 2026-06-20, so this job ships with REAL recon logic (not a no-op marker).
2. **`PlaybookSchedulerJob`** (REFACTOR of `PlaybookSchedulerService`): per D2 owner decision (2026-06-20), the scheduler becomes a SINGLE `sprk_backgroundjob` row (`jobId="notification-playbook-scheduler"`) that internally fans out across the 7 active notification playbooks. Existing cadence preserved 1:1 (every hour at minute 0). Per Q1 owner clarification, each child playbook gets a FRESH correlationId; the parent run records all children correlationIds in `JobRunResult.ResultJson` for trace correlation. Per-playbook "Run Now" deferred to a follow-up if operators ask for finer granularity.

### 6. NOT in R3 (deferred)

- Migration of the other 26 BackgroundService implementations — they work today; touching them is risk. Opportunistic migration over time as those services get touched.
- The 5 queue-consumer services (`ServiceBusJobProcessor` family) — they have a different shape (event-driven, not schedule-driven). Different abstraction; out of scope.
- `DataverseBackgroundJobStore` (replaces in-memory store with durable Dataverse-backed persistence) — ships when the first cron job needs durable history beyond a process lifetime.

---

## Consequences

### Positive

- **Operators get "Run Now" + history + enable/disable** via a single admin surface for all migrated jobs — closes a longstanding R2-UAT gap.
- **New scheduled work has a canonical pattern** (`IScheduledJob` + Dataverse row) instead of inventing `PeriodicTimer + IOptions` for the Nth time.
- **Run history is auditable + queryable** via Dataverse (after `DataverseBackgroundJobStore` swap) — operators can answer "did the X job run at 02:00?" without reading logs.
- **`PlaybookSchedulerJob` fan-out preserves per-playbook trace correlation** via children correlationIds in `ResultJson` (Q1) — operators can join parent ↔ children via the parent's `sprk_resultjson`.
- **Cronos cron parsing** is a tiny battle-tested dependency (~63 KB, MIT, used by Hangfire upstream) — full standard cron + 6-field seconds-mode for sub-second test cadences.

### Negative

- **Two parallel job-tracking entities** (`sprk_processingjob` for Office, `sprk_backgroundjob*` for scheduled). Mitigation: clear naming convention; data-model docs disambiguate; design documents the rationale explicitly.
- **Opportunistic migration** leaves a long tail of unmigrated services. Mitigation: ADR + framework readily available; tech debt is visible (each unmigrated service has its own `PeriodicTimer` pattern, easy to grep + migrate).
- **In-memory store is a stepping stone** that introduces transient state loss on process restart. Mitigation: documented explicitly; first cron job needing durable history triggers the Dataverse-backed swap.

### Neutral

- Cronos NuGet adds ~63 KB to BFF publish — within the +5 MB single-task escalation threshold and well under the 60 MB cumulative ceiling (NFR-01).
- `IScheduledJob` interface is allowed under ADR-010 as a testing seam (≥2 production impls from day 1: `MembershipReconciliationJob` + `PlaybookSchedulerJob`).

---

## Acceptance criteria (R3)

See spec.md AC-2.1 through AC-2.ADR. Highlights:

- ✅ AC-2.1: Spaarke.Scheduling library compiles + has unit tests for ScheduledJobHost, cron parsing, run-history recording (Spaarke.Scheduling.Tests: 57 tests as of R3 task 022 wrap).
- ✅ AC-2.2: `sprk_backgroundjob` + `sprk_backgroundjobrun` entities deployed to spaarkedev1.
- ✅ AC-2.3: `MembershipReconciliationJob` registered + visible + triggerable via `/api/admin/jobs/membership-reconciliation/trigger` (ships in task 085).
- ✅ AC-2.4: `PlaybookSchedulerJob` migrated; all 7 notification playbooks fan out; existing cadence preserved (verified by 32 PlaybookSchedulerJobTests).
- ✅ AC-2.5: Admin endpoints behind `RequireAuthorization("SystemAdmin")`; non-admin tokens get 403 (verified by AdminJobsTestFixture's `X-Test-Role:user` path).
- ✅ AC-2.6: Run rows recorded with `correlationId`, `trigger`, `status`, `duration` (NFR-08).
- ✅ AC-2.7: Failed jobs surface in `GET /status` with `lastError`.
- ✅ AC-2.ADR: this document.

---

## Open questions resolved (during R3 design)

1. **D1 (resolved 2026-06-20)**: Should `MembershipReconciliationJob` ship as a no-op marker in R3 (Phase 2 implementation deferred to R4)?
   - **Answer**: Owner promoted Phase 2 to firm in-scope 2026-06-20 → recon job ships with REAL logic, not no-op marker.
2. **D2 (resolved 2026-06-20)**: When migrating `PlaybookSchedulerService`, should each of the 7 playbooks be its own `sprk_backgroundjob` row, or one row that fans out?
   - **Answer**: Single row + fan-out (preserves 1:1 cadence with current behavior; simpler operator UX). Per-playbook "Run Now" deferred to a follow-up if needed.
3. **D4 (resolved during design)**: Cron parsing library choice — Cronos vs hand-rolled.
   - **Answer**: Cronos (mature, ~50KB-63KB, MIT, full cron + 6-field seconds-mode, used by Hangfire).
4. **Q1 (resolved 2026-06-20)**: Should fan-out playbooks share the parent's correlationId or get fresh ones?
   - **Answer**: Fresh per child. Parent records children correlationIds in `JobRunResult.ResultJson` so operators can join.
5. **Q6 (resolved 2026-06-20)**: Admin endpoints use a new "PlatformAdmin" policy?
   - **Answer**: NO — use existing `SystemAdmin` policy at `AuthorizationModule.cs:241`. Already used by `RagEndpoints.cs:157`. The "PlatformAdmin" name was a misnomer in spec.md round 1.

---

## See Also

- Concise (AI-context-loaded) version: [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
- Spec: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 2 + FR-2.1 through FR-2.8 + AC-2.* + Q1/Q6 owner clarifications
- Design: [`projects/spaarke-platform-foundations-r3/design.md`](../../projects/spaarke-platform-foundations-r3/design.md) Part 2
- Data model: [`docs/data-model/sprk_backgroundjob.md`](../data-model/sprk_backgroundjob.md), [`docs/data-model/sprk_backgroundjobrun.md`](../data-model/sprk_backgroundjobrun.md)
- Code: `src/server/shared/Spaarke.Scheduling/`, `src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs`, `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs`
- Constraints: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§A, F.1
- Related ADRs: ADR-052 (where scheduled work runs — A1), ADR-004 (queue-driven work), ADR-008 (endpoint filters), ADR-009 (Redis), ADR-010 (DI minimalism), ADR-012 (shared library), ADR-029 (BFF publish hygiene), ADR-032 (Null-Object Kill-Switch — applies if any IScheduledJob is feature-gated), ADR-034 (User-record membership — provides `MembershipReconciliationJob` as second reference consumer).

---

## Amendment A1 (2026-09-12): placement, runtime as built, and one dispatch per schedule

> **Status**: Accepted (path **B**, root CLAUDE.md §6.5; owner decision 2026-09-12). **Driver**:
> `unified-access-control-r2` task 102 (this text); task 103 implements the lease, slot guard and helper.
> **Evidence**: [`workload-placement-policy-evaluation.md`](../../projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md).

### 1. Placement

Where scheduled work runs is decided by **[ADR-052](ADR-052-workload-placement.md)** — the BFF, a Functions timer,
or a Container Apps job. This ADR governs **how scheduled work runs inside the BFF**. The earlier cross-reference
<!-- adr052-drift:allow reason="quotes the withdrawn cross-reference" -->"ADR-001: in-process workers; no Azure Functions"<!-- /adr052-drift:allow -->
misstated ADR-001 and is withdrawn.

### 2. Runtime as built (verified in code, 2026-09-12)

- The only store is `InMemoryBackgroundJobStore`; no `DataverseBackgroundJobStore` exists, although the
  `sprk_backgroundjob*` tables are deployed. Durable run history, and cron tuned in Dataverse, are the **target**
  state. Until that store exists, cron and enablement are set in code at registration.
- `ScheduledJobHost` starts on **every App Service instance and every deployment slot**, with no lease — each tick
  is dispatched once **per instance**.
- `HasRunForScheduledTimeAsync` is **inert** for its stated purpose: memory is lost on restart, and within a
  process `AdvanceNextFire` already prevents a re-fire.
- Admin `disable` changes the store on the one instance that served the request; a restart re-seeds the job to its
  registration default. It is neither durable nor fleet-wide.

### 3. Rules added

1. **One dispatch per schedule (MUST, for jobs that must not run concurrently).** Before dispatch, the host takes
   a distributed lease that (a) outlives the whole run **including every retry attempt and backoff**, and (b)
   makes a manual trigger and a scheduled tick of the same job mutually exclusive. A host that does not obtain the
   lease records the tick as **skipped**, not failed. When **no lease store is configured** (single-instance
   development), the host dispatches and logs a warning once. When a lease store **is configured but
   unavailable**, the host retries the acquire with the job backoff, then does **not** dispatch and records the
   tick as failed (A1.1, owner decision 2026-09-14). Execution remains at-least-once under retry.
2. **Slots (MUST).** Deployment slots other than production do not run scheduled jobs (a slot-sticky setting the
   host honours).
3. **Idempotency (MUST).** Each unit of work takes an **atomic claim** before its side effect and writes a
   **completion marker** after it; a failed unit releases its claim. A retry never re-applies a unit already
   marked complete.
4. **Retry (MUST for new jobs; existing jobs when next touched, as ADR-052 §1 defines it).** Throw from
   `ExecuteAsync` when a retry could complete work this tick would otherwise lose: the run cannot make progress
   (its query or a shared dependency is unreachable), or units failed transiently and no later tick will revisit
   them. Otherwise count failures and complete; the next tick retries. `Success:false` without throwing means a
   retry cannot help.
5. **Heartbeat (MUST).** Every attempt emits one structured heartbeat carrying its counts and attempt number,
   including an attempt with nothing to do, so a missed run is detectable while run history is process-local.
6. **Registration (MUST).** `services.AddScheduledJob<TJob>(cron, enabled)` — one shared bootstrap, no per-job
   bootstrap class. (The helper is introduced by task 103.)
7. **Host-neutrality (MUST).** Jobs do not depend on `ScheduledJobHost`, `IBackgroundJobStore` or
   `ScheduledJobRegistry` (ADR-052 §5).

### 4. Corrections

- `MembershipReconciliationJob` **shipped** (registered in `MembershipModule`); it is not deferred.
- Third consumer: `GrantExpiryReminderJob` (`unified-access-control-r2` task 100).
- §6 above calls the other services' migration "opportunistic". ADR-052 §1 now makes it **when next touched**, held
  by an ArchTest ratchet.

### 5. Implemented — `unified-access-control-r2` task 103 (2026-09-14)

§2's "no lease — each tick dispatched once per instance" is no longer true:

- **Rule 1.** `IScheduledJobLease` (Spaarke.Scheduling):
  - **The two implementations.** In the BFF, `RedisScheduledJobLease` runs over the existing Redis connection. It
    takes the lease with `SET NX PX` (`LockTakeAsync`), and only the holder can extend or release it.
    `ProcessLocalScheduledJobLease` applies when Redis is off, which is only in Development and Testing: deployed
    environments cannot start without Redis.
  - **Renewal.** The lease is renewed every third of `ScheduledJobHostOptions.LeaseDuration` (2 minutes) for the
    whole run, so the duration bounds only how long a dead holder can block the job.
  - **Occurrence marker.** A lease alone lets a short run finish and release before a slower instance wakes for the
    same occurrence. So the lease also records the last dispatched occurrence and refuses any occurrence earlier
    than or equal to it — the same device the Functions timer trigger uses (its schedule status).
  - **Lost lease.** A run whose lease another holder takes is cancelled.
  - **Manual trigger.** Triggering a job that is already running gets 409.
- **Rule 2.** `ScheduledJobHostOptions.RunScheduledJobs` reads `Scheduling:RunScheduledJobs`.
  `scripts/Deploy-BffApi.ps1 -UseSlotDeploy` sets `Scheduling__RunScheduledJobs=false` on the slot as a slot
  setting, so it never swaps into production.
- **Rule 6.** Jobs register with `AddScheduledJob<TJob>(cron, enabled)`, and `ScheduledJobRegistry` and
  `InMemoryBackgroundJobStore` read the registrations in their constructors. The three per-job bootstrap hosted
  services are deleted, and `WorkloadPlacementGuardTests.ScheduledJobsRegisterThroughAddScheduledJobOnly` keeps them
  from coming back.

### 6. Still deferred

`DataverseBackgroundJobStore` — durable history, fleet-wide enable/disable (#983). Migrating the 14 hand-rolled timer
services is tracked in #976.

### A1.1 (2026-09-14): a lease store that is configured but unavailable

**Owner decision** (the task 103 escalation). When the lease store is configured but cannot be reached, the host:

1. retries the acquire with the job retry policy's backoff (by default no delay, then 5 s, then 10 s);
2. then does **not** dispatch the tick;
3. records the tick as **failed** and logs an error.

The tick is recorded as failed rather than skipped because every instance loses the store at the same time, so nobody
runs the tick; "skipped" would make a fleet-wide miss look healthy. A manual trigger fails at once with 503: the admin
is waiting on the request and can retry.

**Rejected alternatives:**
- **Dispatch anyway.** Every instance runs the tick. Per-unit idempotency also lives in Redis and fails open (#984),
  so notifications would duplicate.
- **Record the tick as skipped.** It hides a fleet-wide miss.

**Cost accepted:** a tick missed during an outage. A job that must not lose one catches up on its next run:
`PlaybookSchedulerJob` already does (`sprk_lastrundate`), and `GrantExpiryReminderJob` will send the most urgent
unsent threshold (owner decision, same date; task 100).
