# Background Jobs Admin Guide (Spaarke.Scheduling)

> **Status**: Shipped in R3 (2026-06-22)
> **Audience**: Spaarke operators, system administrators
> **Last Updated**: 2026-06-22
> **Related**:
> - Architecture: [`docs/architecture/background-workers-architecture.md`](../architecture/background-workers-architecture.md)
> - ADR (concise): [`.claude/adr/ADR-036-background-job-infrastructure.md`](../../.claude/adr/ADR-036-background-job-infrastructure.md)
> - ADR (full): [`docs/adr/ADR-036-background-job-infrastructure.md`](../adr/ADR-036-background-job-infrastructure.md)
> - Data model: [`docs/data-model/sprk_backgroundjob.md`](../data-model/sprk_backgroundjob.md), [`docs/data-model/sprk_backgroundjobrun.md`](../data-model/sprk_backgroundjobrun.md)
> - Forward link: future project `scheduled-jobs-migration` (Wave 28 — opportunistic migration of remaining 26 `BackgroundService` implementations)

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

Spaarke's BFF API runs **28 different scheduled background tasks** (notification dispatch, reconciliation passes, cache warming, polling, etc.). Before R3 each of those had its own bespoke `BackgroundService` implementation with a private `PeriodicTimer`, a private `IOptions<XOptions>` for "enabled" and "interval," and no shared run history. Operators had **no admin visibility** ("did the nightly job actually run?"), **no "Run Now" button** ("the data is stale; I need it refreshed before the hour is up"), and **no central registry** ("which scheduled jobs exist? what are they doing? are they healthy?").

The **Spaarke.Scheduling framework** — shipped in R3 Part 2 — replaces that fragmentation with one place to see and control every scheduled job:

- A shared library `Spaarke.Scheduling` defines a uniform `IScheduledJob` contract and a `ScheduledJobHost` that owns the cron loop for all of them.
- Job definitions live in the new Dataverse entity **`sprk_backgroundjob`** (catalog of "what jobs exist"), tunable by operators without code deploys.
- Each run is recorded in **`sprk_backgroundjobrun`** (per-run history with status, duration, error message, correlation id).
- Admin endpoints **`/api/admin/jobs/*`** let operators list, inspect, trigger, enable, and disable jobs with HTTP calls — gated by the existing `SystemAdmin` policy.

This guide covers what an operator needs to do day-to-day. **You don't need to know any C#** to use the framework.

> **Boundary vs `sprk_processingjob`**: The existing `sprk_processingjob` entity is scoped to Office document operations (per-file save/email/share jobs with a `DocumentId` lookup). It is **not** replaced. `sprk_backgroundjob*` is a parallel family for *scheduled* jobs across all domains. Both coexist.

---

## Current Job Inventory

Two reference consumers ship in R3 and are seeded automatically at BFF startup:

| Job ID | Display Name | Default Cron | Default Enabled | Purpose |
|---|---|---|---|---|
| `notification-playbook-scheduler` | Notification Playbook Scheduler | `0 * * * *` (every hour at minute 0) | Yes | Fans out all 7 active notification-mode playbooks for every active user. Each child playbook gets a fresh correlation id; children are recorded in the parent run's `ResultJson` for tracing. Replaces the legacy `PlaybookSchedulerService`. |
| `membership-reconciliation` | Membership Junction Reconciliation | `0 2 * * *` (daily at 02:00 UTC) | Yes | Reconciles the `sprk_userentityassociation` junction table against source-of-truth identity Lookups on configured entities (`sprk_matter`, `sprk_document`, `sprk_event`, `sprk_task`, `sprk_opportunity`). Load-bearing for the 8 Q4 `sprk_assigned*` Lookups on `sprk_matter` because those fields are edited exclusively via maker portal / Power Automate / plugins (not through BFF endpoints), so real-time membership events do not cover them. |

Both definitions are seeded on every BFF startup (idempotent — a host restart re-runs the same seed without harm). Operators can tune the cron schedule, the enabled flag, or the per-job `ConfigJson` via Dataverse without redeploying the BFF.

---

## Admin Endpoints

All endpoints live under `/api/admin/jobs` and require the `SystemAdmin` policy (the same policy used by `RagEndpoints`'s bulk-indexing admin group — there is **no separate `PlatformAdmin` policy**).

The base URL on dev is `https://spe-api-dev-67e2xz.azurewebsites.net`. Substitute your environment's BFF host name.

### `GET /api/admin/jobs` — List all registered jobs

Enumerates every `IScheduledJob` currently registered with the host. Joins each row with the most-recent run summary and the next computed cron occurrence.

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

Results are sorted alphabetically by `jobId` for predictability. An empty list (HTTP 200, body `[]`) means no jobs are registered — that is a valid steady state, not an error.

| Field | Meaning |
|---|---|
| `jobId` | Stable id matching the Dataverse `sprk_backgroundjob.sprk_jobid` row |
| `displayName` | Human-readable name |
| `enabled` | Whether the scheduling loop will fire this job on its next cron tick |
| `cronSchedule` | Standard 5-field cron expression (e.g., `0 2 * * *`) — parsed by [Cronos](https://github.com/HangfireIO/Cronos) |
| `lastRunStatus` | `"Succeeded"`, `"Failed"`, `"InProgress"`, or `null` if never executed |
| `nextScheduledOn` | Next cron occurrence; `null` if `enabled` is false or the cron expression is unparseable |

### `GET /api/admin/jobs/{jobId}/status` — Detail for one job

Returns the same summary fields **plus the last 10 run records** (most-recent-first). The most-recent failure surfaces in `recentRuns[0].errorMessage` when `lastRunStatus = "Failed"`.

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

**HTTP 404** is returned when `jobId` is not registered (e.g., typo in the URL).

### `GET /api/admin/jobs/{jobId}/history?limit=N` — Full run history

Returns the most-recent run records for a job, ordered newest-first. Use this for "what happened in the last 50 runs?" queries.

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

Dispatches the job immediately with `Trigger = ManualAdmin` and a fresh correlation id. The endpoint **returns 202 Accepted immediately** with the new run id — it does **not** wait for the job to complete. Admin clients poll `GET /api/admin/jobs/{jobId}/status` to see when the run finishes.

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
- **No idempotency dedupe**. If you double-click the trigger button, two run records are written and the handler runs twice. This is the operator's explicit choice. Scheduled cron ticks DO dedupe (see [Retry and Idempotency Behavior](#retry-and-idempotency-behavior)).
- **Admin client cancellation does NOT interrupt the run**. Once dispatch is complete, only host shutdown can stop the in-flight job (and even then it has a 30-second drain window).
- **HTTP 404** if `jobId` is not registered.

### `POST /api/admin/jobs/{jobId}/enable` — Resume scheduled execution

Flips `sprk_backgroundjob.sprk_enabled = true` and triggers an immediate refresh of the scheduling host so the change takes effect on the **next scheduling-loop tick** (not the hourly refresh).

```bash
curl -s -X POST \
  -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/enable
```

**Expected response (HTTP 204 No Content)**. No body.

### `POST /api/admin/jobs/{jobId}/disable` — Pause without removing

Mirror of `/enable`. Disabled jobs remain visible in `GET /api/admin/jobs` (the admin surface) but the scheduling loop skips them on cron ticks. Use this to pause a job pending a fix rather than redeploying.

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
| 204 | Enable/disable succeeded |
| 401 | Missing or invalid bearer token |
| 403 | Token does not have the `SystemAdmin` policy |
| 404 | `jobId` is not registered (or — for enable/disable — has no definition row in the store) |
| 500 | Server-side error (read `/healthz` and the BFF App Service logs) |

---

## Common Operator Scenarios

### Scenario 1: "Last night's daily briefings didn't send"

The notification playbook scheduler is hourly, but each individual playbook respects its own schedule inside `sprk_configjson` (typically daily at 06:00 UTC for the morning-briefing playbook). If users report a missing briefing, walk through this:

1. **List all jobs** and check the playbook scheduler's last run:
   ```bash
   curl -s -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs
   ```
   Look for `notification-playbook-scheduler.lastRunStatus`. If `"Succeeded"`, the scheduler ran but individual playbooks may have been skipped (not due) or failed for specific users.

2. **Pull recent history**:
   ```bash
   curl -s -H "Authorization: Bearer {token}" \
     "https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/history?limit=10"
   ```
   For each run, look at `errorMessage` (top-level failure) and — when you fetch `/status` — open the parent run record in Dataverse to inspect `sprk_resultjson` for per-child-playbook breakdown including the `correlationId` and `failureCount` for each playbook.

3. **Decide**: was it a transient failure (Dataverse hiccup, Graph throttling) or a logic error?

4. **For transient failures**, trigger manually:
   ```bash
   curl -s -X POST -H "Authorization: Bearer {token}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/notification-playbook-scheduler/trigger
   ```
   Poll status until `lastRunStatus = "Succeeded"`.

5. **For logic errors**, disable the job pending a fix (see Scenario 2) and notify the development team.

### Scenario 2: "A buggy job needs to be paused pending fix"

You spotted a regression — the recon job is creating bad junction rows, or the playbook scheduler is sending duplicate emails. Pause the job immediately, no redeploy required:

```bash
# Pause now — takes effect on the next scheduling-loop tick (within seconds)
curl -s -X POST -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/{jobId}/disable

# Verify it's paused
curl -s -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/{jobId}/status
# Expect "enabled": false, "nextScheduledOn": null
```

After the developer ships a fix and the BFF redeploys, re-enable:

```bash
curl -s -X POST -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs/{jobId}/enable
```

The job resumes on its next cron tick — no manual trigger needed unless you want immediate execution.

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
   Watch `recentRuns[0]` — `status` flips from `"InProgress"` to `"Succeeded"`. `processedItems` shows the count of junction rows touched (added + removed + verified).

3. **Per-entity breakdown** — open the most recent `sprk_backgroundjobrun` record in Dataverse and read `sprk_resultjson`. You'll see one object per entity type with `entityType`, `discoveredFields`, `parentRowsScanned`, `verified`, `removed`, `errors`, and `durationMs`. If any entity type's `errors > 0`, check the BFF App Service logs for the correlation id (filterable by `correlationId={...}`).

### Scenario 4: "Onboarding a new scheduled job"

For operators, onboarding a new job is **mostly a no-op**. The developer:

1. Implements `IScheduledJob` (interface with `JobId`, `DisplayName`, `Description`, `ExecuteAsync(JobRunContext, CancellationToken)`).
2. Registers the singleton in DI plus a startup hosted service that calls `ScheduledJobRegistry.Register(handler)` and `IBackgroundJobStore.AddOrReplaceJob(definition)` — the same idempotent pattern that ships `notification-playbook-scheduler` and `membership-reconciliation`.
3. Deploys the BFF.

On host startup the seed registers the handler and seeds the `sprk_backgroundjob` row with the default cron. The job immediately appears in `GET /api/admin/jobs`. Operators only need to act if they want to change the cron schedule, disable the job, or trigger it manually.

---

## Configuration

The framework's configuration surface lives in **two places**: per-job tuning in Dataverse, and host-level tuning in `appsettings.json`.

### Job-level configuration (Dataverse — `sprk_backgroundjob` row)

Edit these fields directly in the model-driven Dataverse maker portal (or via the BFF admin endpoints for `enabled`). Changes take effect on the next scheduling-loop tick (no redeploy):

| Field | Logical name | Type | Purpose |
|---|---|---|---|
| Cron schedule | `sprk_cronschedule` | Text | Standard 5-field cron expression. Examples below. Empty or null = manual-trigger only (no scheduled ticks). |
| Enabled | `sprk_enabled` | Yes/No | Master enable/disable. Toggleable via `POST /api/admin/jobs/{jobId}/enable\|disable` (instant takes effect on next tick). |
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

All cron expressions are evaluated in **UTC**. Cronos's [cron reference](https://github.com/HangfireIO/Cronos) is the canonical source for syntax details. If the expression is unparseable, the host logs an error, leaves `sprk_lastrunstatus` untouched, and returns `nextScheduledOn = null` in the admin listing — the operator can spot the broken row and fix it.

**6-field syntax** (with seconds field) is supported for internal high-frequency jobs (tests, watchdogs); production jobs are expected to stick to 5-field minute-precision.

### Host-level configuration (BFF `appsettings.json` / App Service settings)

These settings control the framework's overall behavior; they apply to all jobs and rarely need to be changed:

| Setting | Default | Purpose |
|---|---|---|
| `ScheduledJobHostOptions.RefreshInterval` | `1 hour` | How often the host re-reads `sprk_backgroundjob` rows to pick up new / disabled / cron-changed jobs without a restart. Admin enable/disable triggers an immediate refresh in addition to the periodic one. |
| `ScheduledJobHostOptions.ShutdownDrainTimeout` | `30 seconds` (NFR-07) | How long `StopAsync` waits for in-flight jobs to observe cancellation and complete before the host force-exits. |
| `ScheduledJobHostOptions.MaxLoopSleep` | `1 hour` | Maximum time the scheduling loop sleeps between checks. Defends against pathological cron expressions whose next-fire is far in the future. |
| `ScheduledJobHostOptions.RetryPolicy.MaxAttempts` | `3` | Total attempts including the first call (so: first call + 2 retries). |
| `ScheduledJobHostOptions.RetryPolicy.BaseDelay` | `5 seconds` | Delay before the second attempt. Doubles on each subsequent attempt. |
| `ScheduledJobHostOptions.RetryPolicy.MaxDelay` | `2 minutes` | Upper bound on any single retry delay. |

These are POCO defaults baked into `ScheduledJobHostOptions`. They are not bound from `appsettings.json` by default — overriding them today requires a code change to the DI registration (`PostConfigure<ScheduledJobHostOptions>`). The defaults match the R3 spec and are conservative for current production volumes.

**Membership reconciliation** has its own `appsettings.json` section that controls which entity types the recon scans:

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
- **Delay schedule**: 5 seconds before attempt 2; 10 seconds before attempt 3 — formula `BaseDelay * 2^(attempt-1)`, capped at `MaxDelay = 2 minutes`.
- **No jitter** — the in-process scheduler has exactly one caller per job per tick, so deterministic delays are easier to reason about than randomized ones.

After 3 failed attempts, the run is recorded as `status = "Failed"`, the final exception's message is written to `sprk_backgroundjobrun.sprk_errormessage`, and the message is denormalized to `sprk_backgroundjob.sprk_lastrunerror` for parent-row visibility. The job's next scheduled tick proceeds normally — retries are bounded to the current tick; the cron cadence is the macro-level retry.

### Idempotency on host restart

When the BFF restarts in the middle of a scheduled tick (App Service recycle, deploy, crash), the framework prevents a duplicate run of the same scheduled occurrence:

1. Before dispatching a tick, the host calls `IBackgroundJobStore.HasRunForScheduledTimeAsync(jobId, scheduledFireUtc)`.
2. If a row already exists for the `(jobId, scheduledFireUtc)` pair (regardless of status — `Running`, `Succeeded`, `Failed`, `Cancelled`), the host **skips dispatch** and logs the dedupe.
3. If no row exists, the host records a fresh run-start row and proceeds.

The `scheduledFireUtc` value is the cron-derived scheduled fire time (matched at second precision or better), persisted on the `sprk_backgroundjobrun.sprk_scheduledfireon` column. It is `null` for `ManualAdmin` and `OnStartup` triggers — those do **not** participate in tick-level idempotency, which is intentional (an admin who clicks "Run Now" twice in 5 minutes expects two runs).

### Cancellation and shutdown drain

Every `IScheduledJob` implementation must honor the `CancellationToken` passed to `ExecuteAsync`. When the BFF receives a shutdown signal:

1. `ScheduledJobHost.StopAsync` cancels the host's stopping token.
2. The token propagates to all in-flight `ExecuteAsync` invocations.
3. The host waits up to **30 seconds** (`ShutdownDrainTimeout`, per NFR-07) for in-flight runs to observe cancellation and return.
4. If the 30-second window expires with jobs still running, the host logs a warning ("NFR-07 ceiling reached — N job(s) still running") and exits anyway.

In-flight runs that are cancelled return `JobRunResult.Success = false`, `ErrorMessage = "Cancelled by host shutdown (NFR-07)"`. The host writes the completion record using `CancellationToken.None` so the row persists even on shutdown.

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| Job is not running on its schedule | `enabled` is false, OR the cron expression is unparseable, OR the BFF host is not running | Check `GET /api/admin/jobs/{jobId}/status`. If `enabled = false`, call `POST .../enable`. If `nextScheduledOn = null` despite `enabled = true`, the cron expression is invalid — fix it on the Dataverse row (check BFF App Service logs for the `CronFormatException` warning). If the host itself is down, check `GET /healthz`. |
| `nextScheduledOn` is `null` for an enabled job | Cron expression is unparseable | Open the `sprk_backgroundjob` row in Dataverse and verify `sprk_cronschedule`. Test with [crontab.guru](https://crontab.guru/) for the human-readable interpretation. |
| Job always fails | Read `recentRuns[0].errorMessage` from `/status` | If the message references Dataverse timeout or throttling, check the BFF's connection health and Service Bus throttle counters. If it references a code path / null reference / parse error, file a bug with the correlation id from the run record. |
| HTTP 403 on `/api/admin/jobs/*` | Token does not have the `SystemAdmin` policy | The endpoint uses the same policy as `RagEndpoints`'s bulk-indexing admin group. Verify the user's role assignment includes the `SystemAdmin` claim. |
| HTTP 404 on enable / disable | `jobId` is registered as a handler but has no `sprk_backgroundjob` row | This is rare — typically means the seed hosted service hasn't run yet (race on startup) or the developer registered the handler without seeding. Check BFF startup logs for "Seeded BackgroundJobDefinition" messages. |
| Manual trigger returns 202 but `/status` never updates | The run is still in progress | Jobs run on a background task; poll `/status` every few seconds. For long-running jobs (recon, large fan-out), expect minutes. The `recentRuns[0].status` flips from `"InProgress"` to `"Succeeded"` / `"Failed"` when complete. |
| Duplicate runs after a deploy | Should not happen due to `HasRunForScheduledTimeAsync` idempotency check | If you do see two `sprk_backgroundjobrun` rows with the same `sprk_scheduledfireon` for the same `sprk_backgroundjob`, file a bug — the idempotency probe is not working as expected. Inspect App Service logs for "idempotency dedupe" messages on the affected tick. |
| Host shutdown takes longer than 30s | A job is not honoring the `CancellationToken` | Read the BFF logs for "NFR-07 ceiling reached — N job(s) still running" warnings. Identify the slow job by the in-flight count + correlation id. The job's `IScheduledJob.ExecuteAsync` implementation needs to check `CancellationToken.IsCancellationRequested` at every await boundary. |
| Notification playbooks dispatched for "skipped" playbooks | A playbook's individual schedule (in `sprk_configjson.schedule`) said it wasn't due | Read the parent run's `sprk_resultjson` — children with `status: "Skipped"` are intentional. The scheduler ran the hourly tick but the individual playbook's `frequency = "daily"` and `lastRun` was less than 24 hours ago. |

### Verifying the framework is healthy

A 30-second smoke test:

```bash
# 1. Health check
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"

# 2. List jobs — should return both seeded jobs
curl -s -H "Authorization: Bearer {token}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/admin/jobs | jq '.[] | .jobId'
# Expected:
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
```

---

## Deployment Status (current)

| Item | Dev (spaarkedev1) | UAT | Prod |
|---|---|---|---|
| Dataverse entities (`sprk_backgroundjob`, `sprk_backgroundjobrun`) | Deployed | Pending Dataverse schema deploy | Pending Dataverse schema deploy |
| `Spaarke.Scheduling` library + `ScheduledJobHost` hosted service | Live on BFF | Pending BFF deploy | Pending BFF deploy |
| Admin endpoints (`/api/admin/jobs/*`) | Live, `SystemAdmin`-gated | Pending BFF deploy | Pending BFF deploy |
| Seeded jobs (`notification-playbook-scheduler`, `membership-reconciliation`) | Both seeded at host startup, both enabled | Pending BFF deploy | Pending BFF deploy |
| Run-history backing store | In-memory (process-local; lost on App Service restart) | In-memory | In-memory |

**About the in-memory store**: the early-wave R3 ships with an in-memory `IBackgroundJobStore` implementation. The framework still records every run, but the history is process-local — an App Service recycle wipes it. The next wave swaps in a Dataverse-backed store that writes to `sprk_backgroundjob` / `sprk_backgroundjobrun`, at which point history becomes durable. The swap is a single-line DI change; no operator action is needed beyond redeploying the BFF after the swap lands.

**UAT / Prod onboarding** requires:

1. Run the idempotent Dataverse schema scripts ([`Create-BackgroundJobEntity.ps1`](../../scripts/Create-BackgroundJobEntity.ps1) + [`Create-BackgroundJobRunEntity.ps1`](../../scripts/Create-BackgroundJobRunEntity.ps1)) against the target environment.
2. Add both entities to the active unmanaged Spaarke solution (per ADR-027).
3. Deploy the BFF (no `appsettings.json` changes required — defaults are spec-correct).
4. Run the [verifying the framework is healthy](#verifying-the-framework-is-healthy) smoke test.

---

## Future Roadmap

- **Dataverse-backed run-history store** — replaces the in-memory store so run records survive App Service restarts.
- **Opportunistic migration of the remaining 26 `BackgroundService` implementations** — tracked under the future project **`scheduled-jobs-migration`** (Wave 28 will scaffold). The existing services keep their bespoke patterns until touched; no big-bang migration. Queue-consumer services (`ServiceBusJobProcessor` family) are intentionally out of scope — they are event-driven, not schedule-driven, and have a different shape.
- **Cron-expression validator helper in the admin endpoints** — today, an invalid cron expression is logged and `nextScheduledOn` returns `null`; future work will add a pre-save validator endpoint so operators get immediate feedback when editing `sprk_cronschedule` in Dataverse.
- **Slack / Teams notification on job failure** — future hook into the run-completion path so a failed run pings a configured channel.
- **Per-playbook "Run Now"** — today `POST .../notification-playbook-scheduler/trigger` runs the whole scheduler (all 7 playbooks for all users). A follow-up will optionally accept a `playbookId` in the request body to fan out only one playbook.

---

*Admin guide for the `Spaarke.Scheduling` background-job framework. See also: [Architecture](../architecture/background-workers-architecture.md) | [ADR-036 concise](../../.claude/adr/ADR-036-background-job-infrastructure.md) | [ADR-036 full](../adr/ADR-036-background-job-infrastructure.md) | [`sprk_backgroundjob`](../data-model/sprk_backgroundjob.md) | [`sprk_backgroundjobrun`](../data-model/sprk_backgroundjobrun.md)*
