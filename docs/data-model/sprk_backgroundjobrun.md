# sprk_backgroundjobrun — Background Job Run

> **Project**: spaarke-platform-foundations-r3 (Part 2 — Background-job infrastructure)
> **Task**: R3-016 (FR-2.5)
> **Created**: 2026-06-21
> **Status**: Deployed to spaarkedev1
> **Schema script**: [`scripts/Create-BackgroundJobRunEntity.ps1`](../../scripts/Create-BackgroundJobRunEntity.ps1) (idempotent)

---

## Purpose

`sprk_backgroundjobrun` records **per-run history** for the `Spaarke.Scheduling`
background-job framework introduced in R3 Part 2. Each invocation of an `IScheduledJob`
implementation by `ScheduledJobHost` writes one row here on start, then updates that row
on completion with status, duration, processed-item count, optional result JSON, or error
detail.

The framework uses this table for three concrete purposes:

1. **Audit trail** — Operators can query "what runs occurred in the last 24h, and what
   were their outcomes?" without needing App Insights queries. Surfaced by R3 task 022's
   `GET /api/admin/jobs/{jobId}/history` endpoint.
2. **Idempotency probe (R3 task 014)** — Before starting a scheduled tick,
   `ScheduledJobHost` queries this table for an existing row with the same
   `sprk_backgroundjob` parent + `sprk_scheduledfireon` value. If found, the tick is
   skipped (host already ran for this fire time). This prevents duplicate runs after host
   restarts and is more reliable than time-window arithmetic.
3. **Denormalization source** — On run completion, the framework updates the parent
   `sprk_backgroundjob`'s `sprk_lastrun*` fields with values from the most recent row
   here, so list views show last-run information without joining.

The sibling parent entity is [`sprk_backgroundjob`](sprk_backgroundjob.md) (created by
R3 task 015 — the catalog of job definitions).

### Why not extend `sprk_processingjob`?

See [`sprk_backgroundjob.md` — "Why not extend sprk_processingjob"](sprk_backgroundjob.md#why-not-extend-sprk_processingjob).
The same rationale applies: `sprk_processingjob` is scoped to Office document operations
and tracks runs with Office-coupled fields (`DocumentId`, stage progress, etc.).
`sprk_backgroundjobrun` is the parallel run-history table for the general scheduled-job
framework. Both coexist.

---

## Columns

Standard audit fields (`createdon`, `createdby`, `modifiedon`, `modifiedby`, `ownerid`,
`statecode`, `statuscode`, `versionnumber`) are auto-added by Dataverse and are not
listed below.

| # | Column | Type | Required | Max length | Purpose |
|---|---|---|---|---|---|
| 1 | `sprk_name` | Text (auto-number) | **Required** | 200 | **Primary name field**. Auto-generated format: `RUN-{SEQNUM:8}-{DATETIMEUTC:yyyyMMddHHmmss}`. Synthesized at row creation so views and lookups always show a meaningful label without the framework setting it explicitly. |
| 2 | `sprk_backgroundjob` | Lookup → `sprk_backgroundjob` | **Required** | — | Parent job definition. `Delete=Restrict` (parent cannot be deleted while runs exist — preserves audit history). |
| 3 | `sprk_runid` | Text | **Required** | 50 | GUID string (36 chars) uniquely identifying this run. Set by `ScheduledJobHost` at start. Stored as Text because the Dataverse metadata Web API does not support creating custom `Uniqueidentifier` attributes — only the entity's auto-generated primary id. Stable identifier independent of Dataverse's GUID. |
| 4 | `sprk_trigger` | OptionSet (local) | **Required** | — | How the run was started. See [OptionSet values](#optionset-sprk_trigger) below. |
| 5 | `sprk_correlationid` | Text | Optional | 100 | Distributed-trace correlation id (per spec NFR-08). Per Q1 owner decision (2026-06-20): the `PlaybookSchedulerService` scheduler run records its own correlation id here; each fanned-out child playbook gets a **fresh** correlation id (recorded in `sprk_resultjson`). |
| 6 | `sprk_startedon` | DateTime (UserLocal) | **Required** | — | UTC timestamp when the run started. Written by `ScheduledJobHost` before invoking `IScheduledJob.ExecuteAsync(...)`. |
| 7 | `sprk_completedon` | DateTime (UserLocal) | Optional | — | UTC timestamp when the run finished. Null while `sprk_status = Running`. Set on completion (success, failure, or cancellation). |
| 8 | `sprk_status` | OptionSet (local) | **Required** | — | Run lifecycle status. See [OptionSet values](#optionset-sprk_status) below. |
| 9 | `sprk_errormessage` | Multiline Text | Optional | 4 000 | Error detail when `sprk_status = Failed`. Truncated copy is also pushed to `sprk_backgroundjob.sprk_lastrunerror` for parent-row visibility. |
| 10 | `sprk_processeditems` | Whole Number | Optional | — | Handler-specific count metric (e.g., reconciled membership rows, processed playbooks). Optional — handlers that don't process discrete items leave it null. |
| 11 | `sprk_resultjson` | Multiline Text | Optional | 100 000 | Serialized `JobRunResult.Details` payload. Schema is owned by the handler. E.g., `PlaybookSchedulerService` writes child-playbook correlation ids here so operators can join parent ↔ children. |
| 12 | `sprk_scheduledfireon` | DateTime (UserLocal) | Optional | — | Cron-computed scheduled fire timestamp. Used by `ScheduledJobHost` idempotency probe (R3 task 014): before a tick runs, host queries for an existing row with the same parent + this value. Null for `ManualAdmin` and `OnStartup` triggers (those don't correspond to a scheduled tick). |

**Count: 12 columns** (1 primary name + 1 lookup + 10 spec FR-2.5 columns + 1 idempotency probe added per task 014 dependency).

> **Note on the FR-2.5 column count.** Spec FR-2.5 lists **10** functional columns:
> the lookup, runid, trigger, correlationid, startedon, completedon, status,
> errormessage, processeditems, resultjson. The primary `sprk_name` column is implicit
> (every Dataverse entity has one). `sprk_scheduledfireon` is the additional column
> introduced by R3 task 014 to back the idempotency probe — documented in this entity
> rather than a separate table because it is a per-run attribute.

### OptionSet: `sprk_trigger`

Local option set: `sprk_backgroundjobrun_trigger` (3 values). Matches the
`JobRunTrigger` enum in `Spaarke.Scheduling` (R3 task 013).

| Value | Label | Meaning |
|---|---|---|
| 1 | Scheduled | Run was started by the cron timer. `sprk_scheduledfireon` is set. |
| 2 | ManualAdmin | Run was triggered by an admin via `POST /api/admin/jobs/{jobId}/trigger` (R3 task 021). |
| 3 | OnStartup | Run was started on `ScheduledJobHost` startup (jobs configured to run-once at boot). |

### OptionSet: `sprk_status`

Local option set: `sprk_backgroundjobrun_status` (4 values). **Aligned with**
[`sprk_backgroundjob.sprk_lastrunstatus`](sprk_backgroundjob.md#optionset-sprk_lastrunstatus)
— same numeric values, same labels — so the denormalization step does not need a mapping
table. (Note: status value 1 differs in semantic order between the two tables — here
1=Running because a run begins in Running state; in the parent's `sprk_lastrunstatus`
1=Success because the parent denormalizes the OUTCOME of completed runs. Numeric values
are intentionally aligned across the two tables despite the slight semantic difference.)

| Value | Label | Meaning |
|---|---|---|
| 1 | Running | Run is currently in progress. Set on start; replaced on completion. |
| 2 | Success | Run completed without error. |
| 3 | Failed | Run threw an unhandled exception or the handler returned a failure result. `sprk_errormessage` is set. |
| 4 | Cancelled | Run was cancelled (host shutdown, admin cancel, `CancellationToken` fired). |

---

## Relationships

| Relation | Other entity | Cardinality | Lookup field (on this entity) | Cascade behavior | Purpose |
|---|---|---|---|---|---|
| Parent definition | `sprk_backgroundjob` | N : 1 | `sprk_backgroundjob` (Required) | **Delete=Restrict**, all others NoCascade | Each run row points to exactly one parent job definition. Restrict prevents accidental loss of run history — operators must explicitly delete child runs (or use a bulk-delete job) before deleting the parent. |

**Relationship SchemaName**: `sprk_backgroundjob_backgroundjobrun_backgroundjob` (1:N from
parent's perspective).

---

## Solution Membership + Deployment

- **Solution**: To be added to the active unmanaged Spaarke solution (per ADR-027) alongside
  the parent `sprk_backgroundjob`. The schema script creates the entity at the org root;
  solution placement is performed via the maker portal or
  `pac solution add-solution-component` as part of the broader R3 deployment.
- **Deployment**: Idempotent. Re-running [`Create-BackgroundJobRunEntity.ps1`](../../scripts/Create-BackgroundJobRunEntity.ps1)
  on an environment where the entity already exists adds only missing attributes /
  missing OptionSets / missing lookup and is safe.
- **Parent-exists guard**: The script first checks for `sprk_backgroundjob` (with a 60s
  poll). If the parent is not yet present, the script creates the entity + columns +
  option sets but **defers** the lookup creation with a clear operator-actionable message.
  Re-run after the parent is in place to finish wiring.
- **Different env**: Invoke with `-EnvironmentDomain "<env>.crm.dynamics.com"`.

---

## Service Usage Map

Once R3 Phase 2 + Phase 3 land, the following services consume this entity:

| Caller | Operation | File |
|---|---|---|
| `ScheduledJobHost` | INSERT on run start (`Status=Running`, `StartedOn=now`, `Trigger`, `ScheduledFireOn` for cron triggers); UPDATE on completion (`Status`, `CompletedOn`, `ProcessedItems`, `ResultJson`, `ErrorMessage`); UPDATE parent's `sprk_lastrun*` after completion | `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs` (R3 tasks 013 + 014) |
| Idempotency probe | Query for parent + `ScheduledFireOn` match before starting a tick (R3 task 014) | Same file |
| Admin history endpoint | `GET /api/admin/jobs/{jobId}/history` queries last N rows for a parent job | `src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs` (R3 task 022) |

---

## ADR Compliance

| ADR | Compliance |
|---|---|
| **ADR-001** (BFF Minimal API + BackgroundService) | `ScheduledJobHost` writing these rows is in-process; no Azure Functions. |
| **ADR-002** (Late-bound entities) | All Dataverse access is late-bound; no early-bound code generation. |
| **ADR-027** (Unmanaged solution; `sprk_` prefix) | Entity uses `sprk_` prefix; will be added to the active unmanaged solution. |
| **ADR-029** (BFF publish hygiene) | This task is a Dataverse-only schema change — 0 MB BFF publish-size delta. |
| **ADR-036** (NEW — Background-job infrastructure) | This entity is the run-history table the ADR describes. ADR-036 authored in R3 task 017. |

---

## References

- **Spec**: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) §FR-2.5, NFR-08, AC-2.2, AC-2.6
- **Design**: [`projects/spaarke-platform-foundations-r3/design.md`](../../projects/spaarke-platform-foundations-r3/design.md) §"Dataverse entities (NEW)" → `sprk_backgroundjobrun`
- **Task**: [`projects/spaarke-platform-foundations-r3/tasks/016-create-sprk-backgroundjobrun-entity.poml`](../../projects/spaarke-platform-foundations-r3/tasks/016-create-sprk-backgroundjobrun-entity.poml)
- **Parent entity**: [`docs/data-model/sprk_backgroundjob.md`](sprk_backgroundjob.md)
- **Idempotency probe rationale**: [`projects/spaarke-platform-foundations-r3/tasks/014-scheduled-job-retry-idempotency.poml`](../../projects/spaarke-platform-foundations-r3/tasks/014-scheduled-job-retry-idempotency.poml)
- **Pattern source for script**: [`scripts/Create-BackgroundJobEntity.ps1`](../../scripts/Create-BackgroundJobEntity.ps1) (R3 task 015 sibling)
