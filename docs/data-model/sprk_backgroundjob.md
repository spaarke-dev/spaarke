# sprk_backgroundjob — Background Job Definition

> **Project**: spaarke-platform-foundations-r3 (Part 2 — Background-job infrastructure)
> **Task**: R3-015 (FR-2.4)
> **Created**: 2026-06-21
> **Status**: Deployed to spaarkedev1
> **Schema script**: [`scripts/Create-BackgroundJobEntity.ps1`](../../scripts/Create-BackgroundJobEntity.ps1) (idempotent)

---

## Purpose

`sprk_backgroundjob` holds the **catalog of scheduled-job definitions** for the new
`Spaarke.Scheduling` background-job framework introduced in R3 Part 2.

`ScheduledJobHost` (a hosted `BackgroundService` inside the BFF) reads this table on
startup and refreshes hourly. For each enabled row it:

1. Resolves the `sprk_handlertype` (fully-qualified C# class name) to an `IScheduledJob`
   instance via DI.
2. Parses `sprk_cronschedule` (Cronos NuGet).
3. Manages a per-job `PeriodicTimer` that invokes `IScheduledJob.ExecuteAsync(...)` on
   the cron cadence.
4. Records each invocation in the sibling [`sprk_backgroundjobrun`](#related-entities)
   table (1:N) with status, duration, and error/result detail.
5. Denormalizes the most recent run's status/timestamps back onto the parent
   `sprk_backgroundjob` row so list views can show "last run" without joining.

Administrators interact with jobs through the `/api/admin/jobs/*` endpoints
(R3 tasks 020/021/022): list, status, history, enable, disable, trigger-now.

### Why not extend `sprk_processingjob`?

There is an EXISTING `sprk_processingjob` entity in the org. It is scoped to **Office
document operations** (`DocumentSave`, `EmailSave`, `ShareLinks`, `QuickCreate`,
`ProfileSummary`, `Indexing`, `DeepAnalysis`) and tracks individual job RUN instances
with stages, progress, idempotency keys, and a `DocumentId` lookup. R3's design
[explicitly rejects](../../projects/spaarke-platform-foundations-r3/design.md) overloading
that entity:

| Aspect | `sprk_processingjob` (existing) | `sprk_backgroundjob` (new) |
|---|---|---|
| Scope | Office document operations | Scheduled-job framework (junction recon, playbook scheduler, cache warming, etc.) |
| Granularity | One row per **run instance** | One row per **job definition** (run history lives in `sprk_backgroundjobrun`) |
| Office coupling | Has `DocumentId` lookup (meaningless for non-Office jobs) | None |
| Trigger model | Event-driven (file save, email send) | Cron + manual admin trigger |
| Status fields | Stage/progress/idempotency | Last-run denormalized fields |

The two entities **coexist**; clients consume the one that matches the operation domain.
Naming-collision check is binding per spec §FR-2.4 and design "Alternatives Considered" §Part 2.

---

## Columns

Standard audit fields (`createdon`, `createdby`, `modifiedon`, `modifiedby`, `ownerid`,
`statecode`, `statuscode`, `versionnumber`) are auto-added by Dataverse and are not
listed below.

| # | Column | Type | Required | Max length | Default | Purpose |
|---|---|---|---|---|---|---|
| 1 | `sprk_jobid` | Text | **Required** | 100 | — | Stable string identifier (e.g., `"membership-reconciliation"`, `"notification-playbook-scheduler"`). **Unique key** — addresses jobs without GUIDs. |
| 2 | `sprk_displayname` | Text | **Required** | 200 | — | Human-readable job name. **Primary name field** for the entity. |
| 3 | `sprk_description` | Multiline Text | Optional | 2000 | — | What the job does. Surfaced in admin endpoints + maker portal. |
| 4 | `sprk_handlertype` | Text | Optional | 500 | — | Fully-qualified C# class name (e.g., `Spaarke.Scheduling.MembershipReconciliationJob`). Resolved by `ScheduledJobHost` at startup via reflection + DI. |
| 5 | `sprk_enabled` | Boolean (Yes/No) | Optional | — | `Yes` | Master enable/disable. When `No`, `ScheduledJobHost` does not start a timer for this job. Toggleable at runtime via `POST /api/admin/jobs/{jobId}/enable\|disable`. |
| 6 | `sprk_cronschedule` | Text | Optional | 100 | — | Standard 5-field cron expression (e.g., `"0 2 * * *"` for nightly 02:00). Parsed by the Cronos NuGet. Empty/null = manual-trigger only. |
| 7 | `sprk_configjson` | Multiline Text | Optional | 100 000 | — | Handler-specific configuration JSON. Schema owned by the handler. E.g., batch size, entity scope, lookback window. |
| 8 | `sprk_lastrunstartedon` | DateTime (UserLocal) | Optional | — | — | Denormalized from the most recent `sprk_backgroundjobrun.sprk_startedon`. |
| 9 | `sprk_lastruncompletedon` | DateTime (UserLocal) | Optional | — | — | Denormalized from the most recent `sprk_backgroundjobrun.sprk_completedon`. |
| 10 | `sprk_lastrunstatus` | OptionSet (local) | Optional | — | — | Status of the most recent run. See [OptionSet values](#optionset-sprk_lastrunstatus) below. |
| 11 | `sprk_lastrunerror` | Multiline Text | Optional | 2000 | — | Truncated error message from the most recent failed run (full detail in `sprk_backgroundjobrun.sprk_errormessage`). Cleared on next successful run. |

**Count: 11 columns** (matches spec FR-2.4 exactly).

### OptionSet: `sprk_lastrunstatus`

Local option set (scoped to this attribute) — values match the
[`sprk_backgroundjobrun.sprk_status`](#related-entities) values for consistency.

| Value | Label | Meaning |
|---|---|---|
| 1 | Success | Run completed without error. |
| 2 | Failed | Run threw an unhandled exception or the handler returned a failure result. |
| 3 | Running | A run is currently in progress (set on start, replaced on completion). |
| 4 | Cancelled | Run was cancelled (host shutdown, admin cancel, `CancellationToken` fired). |

---

## Alternate Key (Unique Constraint)

| Key SchemaName | Attributes | Purpose |
|---|---|---|
| `sprk_jobid_key` | `sprk_jobid` | Enforces uniqueness of the stable job identifier and enables `RetrieveByAlternateKeyAsync("sprk_backgroundjob", { sprk_jobid })` lookups (per [`docs/data-model/schema-additions-alternate-keys.md`](schema-additions-alternate-keys.md) canonical pattern). |

The framework addresses jobs by `sprk_jobid` everywhere (admin endpoints, host startup,
diagnostic logs); the GUID primary key is for Dataverse internal use only and is never
embedded in C# constants or appsettings.

---

## Related Entities

| Relation | Other entity | Cardinality | Lookup field (on child) | Purpose |
|---|---|---|---|---|
| Parent → runs | `sprk_backgroundjobrun` | 1 : N | `sprk_backgroundjob` (lookup) | Each job has zero or more run-history rows. Created by R3 task 016. |

There are **no** other relationships in R3 scope. (No regarding/parent/customer/owner
references beyond the standard `ownerid` system field.)

---

## Solution Membership + Deployment

- **Solution**: To be added to the active unmanaged Spaarke solution (per ADR-027).
  The schema script creates the entity at the org root; solution placement is performed
  via the maker portal or `pac solution add-solution-component` as part of the broader
  R3 deployment.
- **Deployment**: Idempotent. Re-running [`Create-BackgroundJobEntity.ps1`](../../scripts/Create-BackgroundJobEntity.ps1)
  on an environment where the entity already exists adds only missing attributes /
  missing alternate key and is safe.
- **Dry-run**: Invoke with `-DryRun` to preview without modifying Dataverse.
- **Different env**: Invoke with `-EnvironmentUrl "https://<env>.crm.dynamics.com"`.

---

## Service Usage Map

Once R3 Phase 2 + Phase 3 land, the following services consume this entity:

| Caller | Operation | File |
|---|---|---|
| `ScheduledJobHost` | Read all enabled rows on startup + hourly refresh; update `sprk_lastrun*` denormalized fields after each run | `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs` (R3 task 013) |
| Admin endpoints | List, status, history, enable, disable, trigger | `src/server/api/Sprk.Bff.Api/Api/Admin/JobsEndpoints.cs` (R3 tasks 020/021/022) |
| `PlaybookSchedulerService` migration | Owns the single row with `sprk_jobid = "notification-playbook-scheduler"` | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerService.cs` (R3 task 023) |
| `MembershipReconciliationJob` | Owns the row with `sprk_jobid = "membership-reconciliation"` | `src/server/api/Sprk.Bff.Api/Services/Membership/MembershipReconciliationJob.cs` (R3 task 085) |

---

## ADR Compliance

| ADR | Compliance |
|---|---|
| **ADR-001** (BFF Minimal API + BackgroundService) | `Spaarke.Scheduling` library is in-process; no Azure Functions or external schedulers. |
| **ADR-002** (Late-bound entities) | All Dataverse access is late-bound; no early-bound code generation. |
| **ADR-027** (Unmanaged solution; `sprk_` prefix) | Entity uses `sprk_` prefix; will be added to the active unmanaged solution. |
| **ADR-029** (BFF publish hygiene) | This task is a Dataverse-only schema change — 0 MB BFF publish-size delta. |
| **ADR-036** (NEW — Background-job infrastructure) | This entity is the catalog table the ADR describes. ADR-036 authored in R3 task 017. |

---

## References

- **Spec**: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) §FR-2.4
- **Design**: [`projects/spaarke-platform-foundations-r3/design.md`](../../projects/spaarke-platform-foundations-r3/design.md) §"Dataverse entities (NEW)" + §"Alternatives Considered" (Part 2)
- **Task**: [`projects/spaarke-platform-foundations-r3/tasks/015-create-sprk-backgroundjob-entity.poml`](../../projects/spaarke-platform-foundations-r3/tasks/015-create-sprk-backgroundjob-entity.poml)
- **Sibling entity**: [`docs/data-model/sprk_backgroundjobrun.md`](sprk_backgroundjobrun.md) (created by R3 task 016)
- **Alternate-key pattern**: [`docs/data-model/schema-additions-alternate-keys.md`](schema-additions-alternate-keys.md)
- **Pattern source for script**: [`scripts/Create-AiPersonaEntity.ps1`](../../scripts/Create-AiPersonaEntity.ps1) (R6 canonical exemplar)
