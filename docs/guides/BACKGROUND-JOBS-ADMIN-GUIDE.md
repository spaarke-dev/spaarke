# Background Jobs Admin Guide (Spaarke.Scheduling)

> **Status**: Shipped in R3 (2026-06-22); corrected 2026-09-13 to match the runtime as built ([ADR-036 Amendment A1 §2](../adr/ADR-036-background-job-infrastructure.md))
> **Audience**: Spaarke operators, system administrators
> **Last Updated**: 2026-09-13
> **Related**:
> - Architecture: [`docs/architecture/background-workers-architecture.md`](../architecture/background-workers-architecture.md), [`docs/architecture/spaarke-scheduling-architecture.md`](../architecture/spaarke-scheduling-architecture.md)
> - ADR (concise): [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
> - ADR (full): [`docs/adr/ADR-036-background-job-infrastructure.md`](../adr/ADR-036-background-job-infrastructure.md)
> - Placement (where scheduled work runs at all): [`docs/adr/ADR-052-workload-placement.md`](../adr/ADR-052-workload-placement.md)
> - Data model (target store): [`docs/data-model/sprk_backgroundjob.md`](../data-model/sprk_backgroundjob.md), [`docs/data-model/sprk_backgroundjobrun.md`](../data-model/sprk_backgroundjobrun.md)
> - Forward link: future project `scheduled-jobs-migration` (Wave 28 — migration of the remaining timer `BackgroundService` implementations)

---

> ⚠️ **Runtime as built — read this first** ([ADR-036 A1 §2](../adr/ADR-036-background-job-infrastructure.md), verified in code 2026-09-12)
>
> - **The only store is `InMemoryBackgroundJobStore`** — per-process memory. No Dataverse-backed store exists yet; the `sprk_backgroundjob` / `sprk_backgroundjobrun` tables are deployed but **unused** (the Dataverse store is deferred — ADR-036 A1 §6).
> - **Run history is per instance and lost on every restart** (deploy, recycle, scale-in). `/status` and `/history` show only the runs recorded by the instance that served your request.
> - **Cron and enablement are set in code at registration**, not in Dataverse. Nothing an operator edits in Dataverse affects the scheduler today.
> - **`disable` / `enable` change only the instance that served the request**, and a restart re-seeds the job to its registration default. They are neither durable nor fleet-wide.
> - **`HasRunForScheduledTimeAsync` is process-local** — it cannot see a run from before a restart or on another instance, so it does not prevent duplicates.
> - **Every App Service instance runs the scheduler, but each tick runs once.**
>   - Every dispatch takes a distributed lease in Redis, which also records the last occurrence dispatched; other instances record the tick as `Skipped`.
>   - Non-production deployment slots run no ticks (`Scheduling__RunScheduledJobs=false`, slot-sticky).
>   - If Redis is down at tick time, the tick is **not** run and is recorded `Failed` (ADR-036 A1.1).
>
>   (`unified-access-control-r2` task 103, 2026-09-14.)
>
> The sections below describe both the behaviour you get today and, where they differ, the target state.

---

## Table of Contents

- [What the Framework Does](#what-the-framework-does)
- [Current Job Inventory](#current-job-inventory)
- [Admin Endpoints](#admin-endpoints)
- [Common Operator Scenarios](#common-operator-scenarios)
- [Configuration](#configuration)
- [Retry and Idempotency Behavior](#retry-and-idempotency-behavior)
- [Troubleshooting](#troubleshooting)
- [Deployment Status](#deployment-status-current)
- [Future Roadmap](#future-roadmap)

---

## What the Framework Does

Spaarke's BFF API runs a couple of dozen background services — as of 2026-09-13, 23 `BackgroundService` subclasses (14 of them hand-rolled timers, migrating per ADR-052 §1, #976) plus 3 `IScheduledJob`s on this framework (notification dispatch, reconciliation passes, polling, etc.). Before R3 each scheduled one had its own bespoke `BackgroundService` implementation with a private `PeriodicTimer`, a private `IOptions<XOptions>` for "enabled" and "interval," and no shared run history. Operators had **no admin visibility** ("did the nightly job actually run?"), **no "Run Now" button** ("the data is stale; I need it refreshed before the hour is up"), and **no central registry** ("which scheduled jobs exist? what are they doing? are they healthy?").

The **Spaarke.Scheduling framework** — shipped in R3 Part 2 — replaces that fragmentation with one place to see and control every scheduled job:

- A shared library `Spaarke.Scheduling` defines a uniform `IScheduledJob` contract and a `ScheduledJobHost` that owns the cron loop for all of them.
- Job definitions are seeded **in code at registration** into the job store. The Dataverse entity **`sprk_backgroundjob`** (catalog of "what jobs exist", tunable by operators without code deploys) is the **target** store — deployed but unused today.
- Each run is recorded in the store's run history (status, duration, error message, correlation id) — **in process memory today**. The Dataverse entity **`sprk_backgroundjobrun`** is the target durable history.
- Admin endpoints **`/api/admin/jobs/*`** let operators list, inspect, trigger, enable, and disable jobs with HTTP calls — gated by the existing `SystemAdmin` policy. Each call acts on the instance that serves it (see the callout above).

This guide covers what an operator needs to do day-to-day. **You don't need to know any C#** to use the framework.

> **Boundary vs `sprk_processingjob`**: The existing `sprk_processingjob` entity is scoped to Office document operations (per-file save/email/share jobs with a `DocumentId` lookup). It is **not** replaced. `sprk_backgroundjob*` is a parallel family for *scheduled* jobs across all domains. Both coexist.

---

## Current Job Inventory

Three jobs ship and are seeded automatically at BFF startup:

| Job ID | Display Name | Default Cron | Default Enabled | Purpose |
|---|---|---|---|---|
| `notification-playbook-scheduler` | Notification Playbook Scheduler | `0 * * * *` (every hour at minute 0) | Yes | Fans out all 7 active notification-mode playbooks for every active user. Each child playbook gets a fresh correlation id; children are recorded in the parent run's `ResultJson` for tracing. Replaces the legacy `PlaybookSchedulerService`. |
| `membership-reconciliation` | Membership Junction Reconciliation | `0 2 * * *` (daily at 02:00 UTC) | Yes (`Membership:Reconciliation:Enabled`) | Reconciles the `sprk_userentityassociation` junction table against source-of-truth identity Lookups on configured entities (`sprk_matter`, `sprk_document`, `sprk_event`, `sprk_task`, `sprk_opportunity`). Load-bearing for the 8 Q4 `sprk_assigned*` Lookups on `sprk_matter` because those fields are edited exclusively via maker portal / Power Automate / plugins (not through BFF endpoints), so real-time membership events do not cover them. |
| `external-grant-expiry-reminders` | External Grant Expiry Reminders | `0 6 * * *` (daily at 06:00 UTC) | Yes | Reminds the internal user who granted an external share (else the record's owner, else its creator) that the grant is about to expire, through the existing `NotificationService`. Added by `unified-access-control-r2` task 100. |

All definitions are seeded on every BFF startup (idempotent — a restart re-runs the same seed without harm). **The seed is also the only source of cron and enablement**: a restart restores the values set in code, which is why an admin `disable` does not survive one. Changing a cron schedule or default today means a code change and a BFF redeploy (for the membership recon, its `appsettings` section — see [Configuration](#configuration)).

---

## Admin Endpoints

All endpoints live under `/api/admin/jobs` and require the `SystemAdmin` policy (the same policy used by `RagEndpoints`'s bulk-indexing admin group — there is **no separate `PlatformAdmin` policy**).

**Every endpoint acts on the single App Service instance that serves the request.** With more than one instance (or a running deployment slot), two consecutive calls can reach different instances and return different run histories.

The base URL on dev is `https://spe-api-dev-67e2xz.azurewebsites.net`. Substitute your environment's BFF host name.

### `GET /api/admin/jobs` — List all registered jobs

Enumerates every `IScheduledJob` currently registered with the host. Joins each row with the most-recent run summary (from this instance's in-memory history) and the next computed cron occurrence.

```bash
curl -s \
  -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs
```

**Expected response (HTTP 200)**:

```json
[
  {
    "jobId": "membership-reconciliation",
    "displayName": "Membership Junction Reconciliation",
    "description": "Nightly reconciliation of sprk_userentityassociation junction rows ...",
    "enabled": true,
    "cronSchedule": "0 2 * * *",
    "lastRunStartedOn": "2026-06-22T02:00:00.000+00:00",
    "lastRunCompletedOn": "2026-06-22T02:00:13.421+00:00",
    "lastRunStatus": "Succeeded",
    "nextScheduledOn": "2026-06-23T02:00:00.000+00:00"
  },
  {
    "jobId": "notification-playbook-scheduler",
    "displayName": "Notification Playbook Scheduler",
    "description": "Periodically executes notification-mode playbooks ...",
    "enabled": true,
    "cronSchedule": "0 * * * *",
    "lastRunStartedOn": "2026-06-22T18:00:00.000+00:00",
    "lastRunCompletedOn": "2026-06-22T18:00:02.108+00:00",
    "lastRunStatus": "Succeeded",
    "nextScheduledOn": "2026-06-22T19:00:00.000+00:00"
  }
]
```

Results are sorted alphabetically by `jobId` for predictability. An empty list (HTTP 200, body `[]`) means no jobs are registered — that is a valid steady state, not an error. A `lastRunStatus` of `null` shortly after a deploy is normal: the instance has no history yet.

| Field | Meaning |
|---|---|
| `jobId` | Stable job id (also the `sprk_jobid` key of the target Dataverse store) |
| `displayName` | Human-readable name |
| `enabled` | Whether this instance's scheduling loop will fire this job on its next cron tick |
| `cronSchedule` | Standard 5-field cron expression (e.g., `0 2 * * *`) — parsed by [Cronos](https://github.com/HangfireIO/Cronos) |
| `lastRunStatus` | `"Succeeded"`, `"Failed"`, `"InProgress"`, or `null` if this instance has not run it since it started |
| `nextScheduledOn` | Next cron occurrence; `null` if `enabled` is false or the cron expression is unparseable |

### `GET /api/admin/jobs/{jobId}/status` — Detail for one job

Returns the same summary fields **plus the last 10 run records** held by this instance (most-recent-first). The most-recent failure surfaces in `recentRuns[0].errorMessage` when `lastRunStatus = "Failed"`.

```bash
curl -s \
  -H "Authorization: Bearer {token}" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/status"
```

**Expected response (HTTP 200)**:

```json
{
  "jobId": "notification-playbook-scheduler",
  "displayName": "Notification Playbook Scheduler",
  "description": "Periodically executes notification-mode playbooks ...",
  "enabled": true,
  "cronSchedule": "0 * * * *",
  "lastRunStartedOn": "2026-06-22T18:00:00.000+00:00",
  "lastRunCompletedOn": "2026-06-22T18:00:02.108+00:00",
  "lastRunStatus": "Succeeded",
  "nextScheduledOn": "2026-06-22T19:00:00.000+00:00",
  "recentRuns": [
    {
      "runId": "5e3a7b4c-1d8f-4a2b-9c0d-...",
      "trigger": "Scheduled",
      "correlationId": "9f2a1c4b5d6e7f80...",
      "startedOn": "2026-06-22T18:00:00.000+00:00",
      "completedOn": "2026-06-22T18:00:02.108+00:00",
      "status": "Succeeded",
      "errorMessage": null,
      "processedItems": 7,
      "durationMs": 2108
    }
  ]
}
```

Run records do **not** include the job's `ResultJson` (per-child or per-entity breakdown): the store keeps it, but `JobRunDetail` does not expose it. Use the run's `correlationId` to find the detail in the BFF logs.

**HTTP 404** is returned when `jobId` is not registered (e.g., typo in the URL).

### `GET /api/admin/jobs/{jobId}/history?limit=N` — Run history

Returns this instance's most-recent run records for a job, ordered newest-first. Use this for "what happened in the last 50 runs?" queries — bearing in mind that history starts at the instance's last restart.

```bash
curl -s \
  -H "Authorization: Bearer {token}" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/membership-reconciliation/history?limit=20"
```

| Parameter | Default | Maximum | Notes |
|---|---|---|---|
| `limit` | 50 | 500 | Values `<= 0` are treated as the default; values `> 500` are clamped to 500. |

**Expected response (HTTP 200)** — an array of `JobRunDetail` objects (same shape as the `recentRuns[]` array above). **HTTP 404** when `jobId` is not registered.

### `POST /api/admin/jobs/{jobId}/trigger` — Run NOW (out-of-band)

Dispatches the job immediately on the instance that serves the request, with `Trigger = ManualAdmin` and a fresh correlation id. The endpoint **returns 202 Accepted immediately** with the new run id — it does **not** wait for the job to complete. Admin clients poll `GET /api/admin/jobs/{jobId}/status` to see when the run finishes (and may need to retry the poll if it lands on another instance).

```bash
curl -s -X POST \
  -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/membership-reconciliation/trigger
```

**Expected response (HTTP 202 Accepted)**:

```json
{
  "runId": "8b21f4a3-...",
  "status": "Running",
  "startedAt": "2026-06-22T18:42:11.215+00:00"
}
```

The response also includes a `Location: /api/admin/jobs/membership-reconciliation/runs/{runId}` header.

**Key facts about manual triggers**:

- **Always returns immediately**. Jobs run for arbitrary durations (membership recon = minutes; an index rebuild could be hours) — blocking the HTTP request would time out and tie up a request thread.
- **One run of a job at a time.** A trigger takes the job's lease.
  - **HTTP 409:** the job is already running — a scheduled tick or another trigger — and no second run starts.
  - **HTTP 503:** Redis, the lease store, is down.
  - Once a run completes you can trigger again: two triggers one after another give two runs.
- **Admin client cancellation does NOT interrupt the run**. Once dispatch is complete, the in-flight job stops only on host shutdown (it is cancelled, then drained for up to 30 seconds), or when its lease can no longer be held: another holder took it, Redis was unreachable for a full lease duration, or the run passed `MaxRunDuration` (2 hours).
- **HTTP 404** if `jobId` is not registered.

### `POST /api/admin/jobs/{jobId}/enable` — Resume scheduled execution

Sets the job's `Enabled` flag in the **in-memory store of the instance that served the request** and triggers an immediate refresh of that instance's scheduling host, so the change takes effect on its **next scheduling-loop tick**. Other instances and slots are unaffected, and a restart restores the registration default. (Target state: flips `sprk_backgroundjob.sprk_enabled`, durable and fleet-wide.)

```bash
curl -s -X POST \
  -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/enable
```

**Expected response (HTTP 204 No Content)**. No body.

### `POST /api/admin/jobs/{jobId}/disable` — Pause without removing

Mirror of `/enable`, with the same single-instance, non-durable scope. Disabled jobs remain visible in `GET /api/admin/jobs` but that instance's scheduling loop skips them on cron ticks. **Do not rely on it to stop a job fleet-wide** — see [Scenario 2](#scenario-2-a-buggy-job-needs-to-be-paused-pending-fix).

```bash
curl -s -X POST \
  -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/disable
```

**Expected response (HTTP 204 No Content)**.

### Response Codes (all endpoints)

| Code | Meaning |
|---|---|
| 200 | Read operation succeeded |
| 202 | Manual trigger accepted; poll `/status` for outcome |
| 204 | Enable/disable succeeded (on the serving instance) |
| 401 | Missing or invalid bearer token |
| 403 | Token does not have the `SystemAdmin` policy |
| 404 | `jobId` is not registered (or — for enable/disable — has no definition in the store) |
| 500 | Server-side error (read `/healthz` and the BFF App Service logs) |

---

## Common Operator Scenarios

### Scenario 1: "Last night's daily briefings didn't send"

The notification playbook scheduler is hourly, but each individual playbook respects its own schedule (on its `sprk_analysisplaybook` row; typically daily at 06:00 UTC for the morning-briefing playbook). If users report a missing briefing, walk through this:

1. **List all jobs** and check the playbook scheduler's last run:
   ```bash
   curl -s -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs
   ```
   Look for `notification-playbook-scheduler.lastRunStatus`. If `"Succeeded"`, the scheduler ran but individual playbooks may have been skipped (not due) or failed for specific users. If `null`, the instance you reached has restarted since the run — history is not durable.

2. **Pull recent history**:
   ```bash
   curl -s -H "Authorization: Bearer {token}" \
     "https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/history?limit=10"
   ```
   For each run, look at `errorMessage` (top-level failure). The per-child-playbook breakdown (`correlationId`, `failureCount` per playbook) is in the run's `ResultJson`, which the admin endpoints do not return — search the BFF logs by the run's `correlationId`.

3. **Decide**: was it a transient failure (Dataverse hiccup, Graph throttling) or a logic error?

4. **For transient failures**, trigger manually:
   ```bash
   curl -s -X POST -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/trigger
   ```
   Poll status until `lastRunStatus = "Succeeded"`.

5. **For logic errors**, stop the job (see Scenario 2) and notify the development team.

### Scenario 2: "A buggy job needs to be paused pending fix"

You spotted a regression — the recon job is creating bad junction rows, or the playbook scheduler is sending duplicate emails.

**Today, `disable` is not a reliable stop.** It pauses the job only on the instance that served the request; every other instance and any running deployment slot keeps firing it, and the next restart (including the redeploy that ships the fix) re-enables it from its registration default. Use it as an immediate, partial brake, then stop the job durably:

```bash
# Partial brake — pauses the job on the serving instance only
curl -s -X POST -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/{jobId}/disable

# Verify on that instance
curl -s -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/{jobId}/status
# Expect "enabled": false, "nextScheduledOn": null
```

**Durable stop**: change the job's enablement where it is registered and redeploy — for `membership-reconciliation`, set `Membership:Reconciliation:Enabled=false` in App Service configuration (a configuration change restarts the app, and every instance re-seeds with the new value); for the other jobs, the registration default is in code, so the developer ships the change. On a single-instance environment the `disable` call is effective until the next restart.

After the fix ships, the redeploy re-seeds the job to its registration default (enabled) — no `enable` call is needed unless you disabled it on an instance that has not restarted.

### Scenario 3: "Membership data is stale; force a recon"

A user reports their access permissions are wrong (e.g., they should see a matter as assigned counsel because of a maker-portal edit, but the `sprk_userentityassociation` junction hasn't caught up yet). The nightly 02:00 UTC recon will fix it, but you can force it now:

1. **Trigger manually**:
   ```bash
   curl -s -X POST -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/membership-reconciliation/trigger
   # Response includes runId; capture it.
   ```

2. **Monitor**:
   ```bash
   curl -s -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/membership-reconciliation/status
   ```
   Watch `recentRuns[0]` — `status` flips from `"InProgress"` to `"Succeeded"`. `processedItems` shows the count of junction rows touched (added + removed + verified). If the run does not appear, the poll reached a different instance from the trigger — retry.

3. **Per-entity breakdown** — the run's `ResultJson` holds one object per entity type (`entityType`, `discoveredFields`, `parentRowsScanned`, `verified`, `removed`, `errors`, `durationMs`), but the admin endpoints do not return it and it is not written to Dataverse today. Search the BFF App Service logs by the run's `correlationId`. If any entity type reports errors, the same logs carry the detail.

### Scenario 4: "Onboarding a new scheduled job"

For operators, onboarding a new job is **mostly a no-op**. The developer:

1. Decides where the work runs under [ADR-052](../adr/ADR-052-workload-placement.md); for work that stays in the BFF:
2. Implements `IScheduledJob` (interface with `JobId`, `DisplayName`, `Description`, `ExecuteAsync(JobRunContext, CancellationToken)`).
3. Registers it with one line in its feature module: `services.AddScheduledJob<TJob>(cron, enabled)` (ADR-036 A1 rule 6). There is no bootstrap hosted service.
4. Deploys the BFF.

On host startup the job registry and the job store are built from those registrations: the handler is registered and its definition seeded with the given cron. The job immediately appears in `GET /api/admin/jobs`. Operators only need to act if they want to trigger it manually; changing its cron or default enablement is a code change today.

---

## Configuration

The framework's configuration surface lives in **code at registration** today, plus host-level tuning in `ScheduledJobHostOptions`. Per-job tuning in Dataverse is the target state.

### Job-level configuration (target: Dataverse — `sprk_backgroundjob` row)

**Not active today.** Until the Dataverse-backed store exists, nothing reads these fields; edits to a `sprk_backgroundjob` row have no effect. Cron and enablement come from the job's registration in code. When the store ships, these fields become the operator's tuning surface, taking effect on the next scheduling-loop tick:

| Field | Logical name | Type | Purpose |
|---|---|---|---|
| Cron schedule | `sprk_cronschedule` | Text | Standard 5-field cron expression. Examples below. Empty or null = manual-trigger only (no scheduled ticks). |
| Enabled | `sprk_enabled` | Yes/No | Master enable/disable. Will be toggleable fleet-wide via `POST /api/admin/jobs/{jobId}/enable\|disable`. |
| Config JSON | `sprk_configjson` | Multiline text | Handler-specific configuration JSON. Each `IScheduledJob` implementation owns its schema. For example, `notification-playbook-scheduler` reads no fields from here (its config lives on each `sprk_analysisplaybook` row); `membership-reconciliation` reads no fields from here either (its config lives in `appsettings.json` under `Membership:Reconciliation` — see below). |
| Display name | `sprk_displayname` | Text | Updates the value returned in admin endpoints. Cosmetic. |
| Description | `sprk_description` | Multiline text | Updates the value returned in admin endpoints. Cosmetic. |

**Cron quick reference** (5-field minute-precision is the production default):

| Expression | Schedule |
|---|---|
| `0 * * * *` | Every hour at minute 0 (the playbook scheduler's default) |
| `0 2 * * *` | Daily at 02:00 UTC (the recon's default) |
| `*/15 * * * *` | Every 15 minutes |
| `0 8 * * 1-5` | Weekdays at 08:00 UTC |
| `0 0 1 * *` | First of every month at midnight UTC |

All cron expressions are evaluated in **UTC**. Cronos's [cron reference](https://github.com/HangfireIO/Cronos) is the canonical source for syntax details. If the expression is unparseable, the host logs an error and returns `nextScheduledOn = null` in the admin listing — the job never fires until the registration is fixed.

**6-field syntax** (with seconds field) is supported for internal high-frequency jobs (tests, watchdogs); production jobs are expected to stick to 5-field minute-precision.

### Host-level configuration (`ScheduledJobHostOptions`)

These settings control the framework's overall behavior; they apply to all jobs and rarely need to be changed:

| Setting | Default | Purpose |
|---|---|---|
| `ScheduledJobHostOptions.RefreshInterval` | `1 hour` | How often the host re-reads its job store to pick up new / disabled / cron-changed definitions without a restart. Admin enable/disable triggers an immediate refresh on the serving instance in addition to the periodic one. |
| `ScheduledJobHostOptions.ShutdownDrainTimeout` | `30 seconds` (NFR-07) | How long `StopAsync` waits for in-flight jobs to observe cancellation and complete before the host force-exits. |
| `ScheduledJobHostOptions.MaxLoopSleep` | `1 hour` | Maximum time the scheduling loop sleeps between checks. Defends against pathological cron expressions whose next-fire is far in the future. |
| `ScheduledJobHostOptions.RetryPolicy.MaxAttempts` | `3` | Total attempts including the first call (so: first call + 2 retries). |
| `ScheduledJobHostOptions.RetryPolicy.BaseDelay` | `5 seconds` | Delay before the second attempt. Doubles on each subsequent attempt. |
| `ScheduledJobHostOptions.RetryPolicy.MaxDelay` | `2 minutes` | Upper bound on any single retry delay. |

These are POCO defaults baked into `ScheduledJobHostOptions`. They are not bound from `appsettings.json` by default — overriding them today requires a code change to the DI registration (`PostConfigure<ScheduledJobHostOptions>`). The defaults match the R3 spec and are conservative for current production volumes.

**Membership reconciliation** has its own `appsettings.json` section that controls which entity types the recon scans, its cron and whether it is enabled (read at startup, when the job is seeded):

```json
{
  "Membership": {
    "Reconciliation": {
      "EntityTypes": [ "sprk_matter", "sprk_document", "sprk_event", "sprk_task", "sprk_opportunity" ],
      "CronSchedule": "0 2 * * *",
      "Enabled": true,
      "FetchPageSize": 500,
      "OrphanFetchPageSize": 500
    }
  }
}
```

To narrow the recon to a single entity type during testing (faster runs), shrink `EntityTypes`. The discovery service auto-detects which Lookup fields on each entity to reconcile — operators do not list individual field names.

---

## Retry and Idempotency Behavior

### Retry policy

When an `IScheduledJob.ExecuteAsync` invocation throws, the host applies a per-job retry-with-exponential-backoff:

- **Default**: 3 attempts total (1 initial + 2 retries).
- **Delay schedule**: 5 seconds before attempt 2; 10 seconds before attempt 3 — formula `BaseDelay * 2^(attempt-2)`, capped at `MaxDelay = 2 minutes`.
- **No jitter** — each host dispatches a job once per tick, so deterministic delays are easier to reason about than randomized ones. (Note that "once per tick" is per instance today — see below.)

After 3 failed attempts, the run is recorded as `status = "Failed"` and the final exception's message is written to the run record, where `/status` surfaces it as `recentRuns[0].errorMessage`. (Target state: also written to `sprk_backgroundjobrun.sprk_errormessage` and denormalized to `sprk_backgroundjob.sprk_lastrunerror`.) The job's next scheduled tick proceeds normally — retries are bounded to the current tick; the cron cadence is the macro-level retry. ADR-036 A1 §3 sets when a job should throw (so the host retries) versus record failures and complete.

### Duplicate runs and idempotency

**Each scheduled tick runs once across the fleet** (ADR-036 A1 rule 1, `unified-access-control-r2` task 103):

1. **The lease.** Before dispatching a tick, the host takes the job's lease in Redis — atomically, with `SET NX PX` — and records the occurrence it is dispatching. Another instance waking for the same tick finds the lease held, or the occurrence already dispatched, and records the tick as **`Skipped`**, not failed.
2. **Its lifetime.** The lease is renewed for the whole run, retries included, and released when the run ends. If an instance dies mid-run, its lease expires within 2 minutes.
3. **Slots.** Non-production deployment slots run no ticks: `Scheduling__RunScheduledJobs=false` is a slot setting, so it never swaps into production.
4. **If Redis is down at tick time**, the host retries for about 15 seconds. It then does **not** run the tick, and records it as `Failed` with "Scheduler lease store unavailable" (owner decision, ADR-036 A1.1). A job that must not lose a tick catches up on its next one.
5. **The old probe.** `IBackgroundJobStore.HasRunForScheduledTimeAsync` is still process-local and inert (ADR-036 A1 §2); the lease is what prevents duplicates.

Retries within a run re-run units of work, so each job stays **idempotent per unit of work** (ADR-036 A1 §3.3).

Manual triggers (`ManualAdmin`) take the same lease, so they never overlap a scheduled tick or each other; you get 409 while the job runs. They carry no `scheduledFireUtc` and are not de-duplicated per tick: two triggers one after another give two runs.

### Cancellation and shutdown drain

Every `IScheduledJob` implementation must honor the `CancellationToken` passed to `ExecuteAsync`. When the BFF receives a shutdown signal:

1. `ScheduledJobHost.StopAsync` cancels the host's stopping token.
2. The token propagates to all in-flight `ExecuteAsync` invocations.
3. The host waits up to **30 seconds** (`ShutdownDrainTimeout`, per NFR-07) for in-flight runs to observe cancellation and return.
4. If the 30-second window expires with jobs still running, the host logs a warning ("NFR-07 ceiling reached — N job(s) still running") and exits anyway.

In-flight runs that are cancelled return `JobRunResult.Success = false`, `ErrorMessage = "Cancelled by host shutdown (NFR-07)"`. The host writes the completion record using `CancellationToken.None` so the record is written even on shutdown — though with the in-memory store it is lost with the process.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| Job is not running on its schedule | `enabled` is false on the instance you checked, OR the cron expression is unparseable, OR the BFF host is not running | Check `GET /api/admin/jobs/{jobId}/status`. If `enabled = false`, call `POST .../enable` (serving instance only) or restart to restore the registration default. If `nextScheduledOn = null` despite `enabled = true`, the cron expression is invalid — it is set in code, so file a bug (check BFF App Service logs for the `CronFormatException` warning). If the host itself is down, check `GET /healthz`. |
| `nextScheduledOn` is `null` for an enabled job | Cron expression is unparseable | The cron is set at registration in code (or `Membership:Reconciliation:CronSchedule` for the recon). Test the expression with [crontab.guru](https://crontab.guru/) and fix the registration. |
| Job always fails | Read `recentRuns[0].errorMessage` from `/status` | If the message references Dataverse timeout or throttling, check the BFF's connection health and Service Bus throttle counters. If it references a code path / null reference / parse error, file a bug with the correlation id from the run record. |
| HTTP 403 on `/api/admin/jobs/*` | Token does not have the `SystemAdmin` policy | The endpoint uses the same policy as `RagEndpoints`'s bulk-indexing admin group. Verify the user's role assignment includes the `SystemAdmin` claim. |
| HTTP 404 on enable / disable | `jobId` is registered as a handler but has no definition in the store | This should not happen for a job registered with `AddScheduledJob`: its definition is seeded when the store is built. A handler registered by hand has no definition, and the ArchTest `ScheduledJobsRegisterThroughAddScheduledJobOnly` forbids that in the BFF. File a bug. |
| Manual trigger returns HTTP 409 | The job is already running — a scheduled tick or another trigger | Wait for the run to finish (`/status`), then trigger again. |
| Manual trigger returns 202 but `/status` never updates | The run is still in progress, or the poll reached a different instance | Jobs run on a background task; poll `/status` every few seconds. For long-running jobs (recon, large fan-out), expect minutes. The run is recorded only on the instance that served the trigger. |
| History is empty or shorter than expected | The instance restarted (deploy, recycle, scale-in) or the request reached a different instance | Expected today: run history is in-memory and per instance. Use App Insights / BFF logs by `JobId` + `CorrelationId` for durable history. |
| A tick shows `Skipped` on this instance | Another instance ran it, or another run of the job held the lease | **Expected** — one instance runs each tick (ADR-036 A1 rule 1). Look for the `Succeeded` run on another instance, or in the logs by `JobId`. |
| A tick shows `Failed` with "Scheduler lease store unavailable" | Redis was unreachable at tick time | **By design, the tick was not run** (ADR-036 A1.1). Check Redis health; the next tick runs normally. Trigger the job manually if the missed tick matters. |
| Two runs of one scheduled tick | Should not happen (ADR-036 A1 rule 1) | Check that the instances share one Redis (`Redis__Enabled=true`); without Redis the host logs "no distributed lease store is configured" and every instance runs every tick. Otherwise file a bug. |
| A disabled job ran anyway | `disable` reached one instance; another instance, or a restarted instance, ran it | Expected today — see [Scenario 2](#scenario-2-a-buggy-job-needs-to-be-paused-pending-fix) for a durable stop. |
| Host shutdown takes longer than 30s | A job is not honoring the `CancellationToken` | Read the BFF logs for "NFR-07 ceiling reached — N job(s) still running" warnings. Identify the slow job by the in-flight count + correlation id. The job's `IScheduledJob.ExecuteAsync` implementation needs to check `CancellationToken.IsCancellationRequested` at every await boundary. |
| Notification playbooks dispatched for "skipped" playbooks | A playbook's individual schedule said it wasn't due | Children with `status: "Skipped"` in the parent run's `ResultJson` are intentional — the scheduler ran the hourly tick but the individual playbook's `frequency = "daily"` and `lastRun` was less than 24 hours ago. `ResultJson` is not returned by the admin endpoints; check the BFF logs by correlation id. |

### Verifying the framework is healthy

A 30-second smoke test:

```bash
# 1. Health check
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"

# 2. List jobs — should return the three seeded jobs
curl -s -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs | jq '.[] | .jobId'
# Expected:
#   "external-grant-expiry-reminders"
#   "membership-reconciliation"
#   "notification-playbook-scheduler"

# 3. Trigger and watch — confirm the round-trip works
RUN=$(curl -s -X POST -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/trigger \
  | jq -r '.runId')
echo "Run id: $RUN"

sleep 5
curl -s -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/status \
  | jq '.recentRuns[0]'
# Expected: status flips from "InProgress" to "Succeeded" within a few seconds
# (on a multi-instance plan, retry if the poll reaches a different instance)
```

---

## Deployment Status (current)

| Item | Dev (spaarkedev1) | UAT | Prod |
|---|---|---|---|
| Dataverse entities (`sprk_backgroundjob`, `sprk_backgroundjobrun`) | Deployed, **unused** | Pending Dataverse schema deploy | Pending Dataverse schema deploy |
| `Spaarke.Scheduling` library + `ScheduledJobHost` hosted service | Live on BFF (every instance; one run per tick via the Redis lease; slots guarded) | Pending BFF deploy | Pending BFF deploy |
| Admin endpoints (`/api/admin/jobs/*`) | Live, `SystemAdmin`-gated, per instance | Pending BFF deploy | Pending BFF deploy |
| Seeded jobs (`notification-playbook-scheduler`, `membership-reconciliation`, `external-grant-expiry-reminders`) | Seeded at host startup, all enabled | Pending BFF deploy | Pending BFF deploy |
| Run-history backing store | In-memory (process-local; lost on App Service restart) | In-memory | In-memory |

**About the in-memory store**: `InMemoryBackgroundJobStore` is the only `IBackgroundJobStore` implementation. The framework records every run, but the history is process-local — an App Service recycle wipes it, and each instance holds its own. **No Dataverse-backed store exists yet** (deferred — ADR-036 A1 §6, GitHub issue filed by `unified-access-control-r2` task 102); building it is real work (durable history, fleet-wide enable/disable), not a one-line swap.

**UAT / Prod onboarding** requires:

1. *(Optional today — the tables are unused until the Dataverse store ships.)* Run the idempotent Dataverse schema scripts ([`Create-BackgroundJobEntity.ps1`](../../scripts/Create-BackgroundJobEntity.ps1) + [`Create-BackgroundJobRunEntity.ps1`](../../scripts/Create-BackgroundJobRunEntity.ps1)) against the target environment and add both entities to the active unmanaged Spaarke solution (per ADR-027).
2. Deploy the BFF (no `appsettings.json` changes required — defaults are spec-correct).
3. Run the [verifying the framework is healthy](#verifying-the-framework-is-healthy) smoke test.
4. Deploy through a staging slot only with `scripts/Deploy-BffApi.ps1 -UseSlotDeploy`, the `deploy-bff-api.yml` workflow, or the L2 control plane's H9 provisioning deploy; all three set the slot guard (`Scheduling__RunScheduledJobs=false`, slot-sticky) before deploying, and H9 deploys nothing if it cannot. A slot deployed any other way runs scheduled jobs against production data. To set the guard by hand: `az webapp config appsettings set -g <rg> -n <app> --slot staging --slot-settings Scheduling__RunScheduledJobs=false`.

---

## Future Roadmap

- **Dataverse-backed job store** — durable run history and fleet-wide enable/disable, making the `sprk_backgroundjob*` tables live (ADR-036 A1 §6).
- **Migration of the remaining timer `BackgroundService` implementations** — **when next touched** ([ADR-052](../adr/ADR-052-workload-placement.md) §1), held by an ArchTest ratchet; tracked under the future project **`scheduled-jobs-migration`**. Queue-consumer services (`ServiceBusJobProcessor` family) are out of scope — they are ADR-004 work, not schedule-driven.
- **Cron-expression validator helper in the admin endpoints** — pre-save feedback for operators once cron is tunable in Dataverse.
- **Slack / Teams notification on job failure** — future hook into the run-completion path so a failed run pings a configured channel.
- **Per-playbook "Run Now"** — today `POST .../notification-playbook-scheduler/trigger` runs the whole scheduler (all 7 playbooks for all users). A follow-up will optionally accept a `playbookId` in the request body to fan out only one playbook.

---

*Admin guide for the `Spaarke.Scheduling` background-job framework. See also: [Architecture](../architecture/background-workers-architecture.md) | [ADR-036 concise](../../.claude/adr/ADR-036-background-job-infrastructure.md) | [ADR-036 full](../adr/ADR-036-background-job-infrastructure.md) | [ADR-052](../adr/ADR-052-workload-placement.md) | [`sprk_backgroundjob`](../data-model/sprk_backgroundjob.md) | [`sprk_backgroundjobrun`](../data-model/sprk_backgroundjobrun.md)*
