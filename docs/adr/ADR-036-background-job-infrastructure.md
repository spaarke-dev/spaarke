# ADR-036: Background-Job Infrastructure (Spaarke.Scheduling)

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-06-21 |
| Authors | Spaarke Engineering, R3 project |
| Source project | `spaarke-platform-foundations-r3` Part 2 |
| Supersedes | n/a |
| Cross-references | extends ADR-001 (in-process workers); reinforces ADR-010 (DI minimalism); reuses ADR-012 (shared library); aligns with CLAUDE.md §10 (BFF hygiene). |

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
- Wraps each invocation in `JobRetryPolicy` (default 3 attempts, 5s base, 2min cap, exponential 2^(attempt-1))
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
- Related ADRs: ADR-001 (in-process), ADR-008 (endpoint filters), ADR-009 (Redis), ADR-010 (DI minimalism), ADR-012 (shared library), ADR-029 (BFF publish hygiene), ADR-032 (Null-Object Kill-Switch — applies if any IScheduledJob is feature-gated), ADR-034 (User-record membership — provides `MembershipReconciliationJob` as second reference consumer).
