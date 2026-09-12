# Draft — ADR amendments + key directive rewrites for ADR-052 (task 102, step 1)

> **Status**: DRAFT for the Fable-tier review and owner approval. Nothing here is applied yet.
> **Companion drafts**: `ADR-052-workload-placement.full.md`, `ADR-052-workload-placement.concise.md`
> **Rule for every text below**: state the placement rule only by a one-line summary plus a link to ADR-052.

---

## 1. ADR-001 — Amendment A1 (2026-09-12)

### 1a. Full ADR (`docs/adr/ADR-001-minimal-api-and-workers.md`)

**Title** → `ADR-001: Minimal API as the single BFF runtime` (the file name is unchanged).

**Header table**: `Status` → **Accepted, as amended** · `Updated` → `2026-09-12 (Amendment A1)`.

**Banner under the header** (new):

> ⚠️ **READ [Amendment A1](#amendment-a1-2026-09-12-workload-placement-moves-to-adr-052) FIRST.**
> ADR-052 now governs **where** background, scheduled and event-driven work runs (BFF, Azure Functions,
> Container Apps Jobs). A1 supersedes this ADR's Azure Functions and Durable Functions provisions. Everything
> about the BFF runtime itself — Minimal API, one middleware pipeline, ProblemDetails, `/healthz`,
> endpoint-level authorization — is unchanged and still binding.

**New section, appended**:

```markdown
## Amendment A1 (2026-09-12): workload placement moves to ADR-052

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5; owner decision 2026-09-12).
> **Driver**: `unified-access-control-r2` task 102. **Evidence**:
> `projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`.

### Why
By 2026-09 four incompatible placement rules existed — "no Functions", "narrow out-of-band only", "timer/queue/
webhook → Functions", "scheduled work in-process, no Functions" — and each decided the host from the trigger.
The original caution guarded one thing: fragmenting the BFF runtime. That concern still holds for user-facing
endpoints. It never justified forbidding Functions for background or event work, where Microsoft's current
guidance often recommends them.

### What is superseded (→ ADR-052)
| Provision in this ADR | Now governed by |
|---|---|
| Decision rows "Azure Functions for out-of-band integration" and "No Durable Functions" | ADR-052 §2, §7 |
| "When Azure Functions ARE / are NOT appropriate" | ADR-052 §3–§4 |
| "Operational requirements when using Functions" | ADR-052 §6 |
| AI-Directed Coding Guidance bullets 3–5 (Functions / Durable) and Compliance items 3–4 | ADR-052 §5–§7, Compliance |
| Operationalization row "Design documents use 'App Service (Minimal API + Workers)' not 'Function App / Triggers'" | Design documents name the host chosen under ADR-052 |

### What is refined
"BackgroundService + Service Bus for BFF-coupled async work" now reads: **inside the BFF, the mechanism follows
the trigger** — queue-driven → ADR-004; schedule-driven → ADR-036. Where the work runs at all is ADR-052's call.

### What is unchanged
A single BFF runtime on App Service; Minimal API for every BFF endpoint; **no BFF endpoints hosted in Azure
Functions**; one middleware pipeline for cross-cutting concerns; ProblemDetails; `/healthz`; endpoint-level
resource authorization (ADR-008).
```

### 1b. Concise ADR (`.claude/adr/ADR-001-minimal-api.md`) — full replacement body

- Title → `ADR-001: Minimal API BFF Runtime (Concise)`; `Last Updated` → 2026-09-12 (Amendment A1).
- **Decision**: single ASP.NET Core App Service BFF · Minimal API for synchronous endpoints · one middleware
  pipeline · **background, scheduled and event-driven work: placement per [ADR-052](ADR-052-workload-placement.md);
  in-BFF mechanism per ADR-004 (queue) / ADR-036 (schedule)**.
- **MUST**: Minimal API for all BFF HTTP endpoints · single `Program.cs` pipeline · ProblemDetails · `/healthz`.
- **MUST NOT**: host BFF endpoints in Azure Functions · duplicate BFF auth / correlation / ProblemDetails outside the BFF.
- **Remove**: the "Azure Functions PERMITTED…" bullet, "No Durable Functions", the "Functions are appropriate
  for" list, the "(when using Functions)" MUST, and the "Acceptable Pattern: Functions for out-of-band
  integration" example. Keep the anti-pattern example "Functions hosting BFF endpoints".
- **Integration table**: add ADR-004, ADR-036, ADR-052.

### 1c. Index rows
- `.claude/adr/INDEX.md`: `| ADR-001 | Minimal API BFF runtime | BFF endpoints in Minimal API; background-work placement per ADR-052 | Accepted (amended 2026-09-12) |`
- `docs/adr/README-ADRs.md` L15: `- [ADR-001: Minimal API as the single BFF runtime (placement of background work: ADR-052)](./ADR-001-minimal-api-and-workers.md)`
- `docs/adr/README-ADRs.md` L85: "CI should fail on reintroduction of Azure Functions/WebJobs packages or attributes ([ADR-001])" →
  "CI fails on Azure Functions packages or Function-attributed methods **inside the BFF assembly** ([ADR-001], [ADR-052])".

---

## 2. ADR-036 — Amendment A1 (2026-09-12)

### 2a. Full ADR (`docs/adr/ADR-036-background-job-infrastructure.md`)

Header: add `Updated | 2026-09-12 (Amendment A1)`; `Status` → **Accepted, as amended**. Banner:

> ⚠️ **READ [Amendment A1](#amendment-a1-2026-09-12-placement-runtime-truth-and-exactly-once) FIRST.** A1
> corrects this ADR's statement of ADR-001, records how the scheduler actually runs today (in-memory,
> once per instance), and adds the exactly-once, idempotency, retry, registration and heartbeat rules.

```markdown
## Amendment A1 (2026-09-12): placement, runtime truth, and exactly-once

> **Status**: Accepted (path **B**, root CLAUDE.md §6.5; owner decision 2026-09-12). **Driver**:
> `unified-access-control-r2` tasks 102 (this text) and 103 (the lease + helper implementation).

### 1. Placement
Where scheduled work runs is decided by **ADR-052** — the BFF, a Functions timer, or a Container Apps job. This
ADR governs **how scheduled work runs inside the BFF**. The earlier cross-reference "ADR-001: in-process workers;
no Azure Functions" misstated ADR-001 (narrowed on 2026-05-19) and is withdrawn.

### 2. Runtime as built (verified in code, 2026-09-12)
- The only store is `InMemoryBackgroundJobStore`. No `DataverseBackgroundJobStore` exists, although the
  `sprk_backgroundjob*` tables are deployed. §3's "persist to Dataverse" describes the target, not today.
- `ScheduledJobHost` starts on **every App Service instance and every deployment slot**. There is no lease, and
  `HasRunForScheduledTimeAsync` is process-local — so each tick runs once **per instance**.
- Admin `disable` changes the store on the one instance that served the request and is re-seeded `Enabled:true`
  on restart. It is neither durable nor fleet-wide.

### 3. Rules added
- **MUST** (jobs that must run once per schedule): the host acquires a distributed lease keyed by
  `(jobId, scheduledFireUtc)` before dispatch; a host that does not acquire it records the tick as **skipped**,
  not failed. *Implemented by task 103. Until it merges, every job MUST tolerate duplicate ticks through
  per-unit idempotency.*
- **MUST**: deployment slots other than production do not run scheduled jobs (a slot-sticky setting the host
  honours; task 103).
- **MUST**: every `IScheduledJob` is idempotent per unit of work. The host is at-least-once.
- **MUST** (new jobs; existing jobs when next touched): throw from `ExecuteAsync` on a failure a retry could fix —
  after emitting the run's heartbeat — so `JobRetryPolicy` applies. Per-item failures are counted and the run
  continues. `Success:false` is for failures a retry cannot fix.
- **MUST**: every run emits one structured heartbeat with its counts, including a run with nothing to do, so a
  missed run is detectable while run history is process-local.
- **MUST**: register through `services.AddScheduledJob<TJob>(cron, enabled)` (task 103) — one shared bootstrap,
  no per-job bootstrap class.
- **MUST**: jobs stay host-neutral (no dependency on `ScheduledJobHost`), per ADR-052 §5.

### 4. Corrections
- `MembershipReconciliationJob` **shipped** (registered in `MembershipModule`); it is not deferred.
- Third consumer: `GrantExpiryReminderJob` (`unified-access-control-r2` task 100).
- §2's "idempotency probe via `HasRunForScheduledTimeAsync` prevents duplicate execution on restart" holds
  per instance only until the lease lands.

### 5. Still deferred
`DataverseBackgroundJobStore` (durable history, fleet-wide enable/disable) — tracked in GitHub issue #TBD.
```

### 2b. Concise ADR (`.claude/adr/ADR-036-background-job-infrastructure.md`)
- `Last Updated` → 2026-09-12 (Amendment A1). Status line: "Accepted, as amended".
- Decision: reference consumers → PlaybookSchedulerJob (shipped), MembershipReconciliationJob (**shipped**),
  GrantExpiryReminderJob (task 100).
- New short section **"Runtime as built"** (3 bullets from 2a §2).
- **MUST** list gains the seven A1 rules (one line each).
- MUST NOT: "use Hangfire, Quartz.NET or external schedulers. ADR-001 prefers in-process" → "use Hangfire,
  Quartz.NET or another in-process scheduler framework. Where scheduled work runs at all is ADR-052's call."
- Integration table: `ADR-001 | In-process workers; no Azure Functions` → `ADR-052 | Where scheduled work runs
  (BFF, Functions timer, Container Apps job); this ADR = the in-BFF mechanism`.

---

## 3. ADR-004 — Amendment A1 (2026-09-12): scope and duplicate detection

Full (`docs/adr/ADR-004-async-job-contract.md`) — appended section; concise (`.claude/adr/ADR-004-job-contract.md`)
— Decision line and a new "Scope" block:

- **Scope**: queue-driven async work consumed from Service Bus. Schedule-driven work → ADR-036. Where either runs → ADR-052.
  The concise Decision "Use one standard Job Contract for all async work" becomes "…for all **queue-driven** async work".
- **Handler contract**: the non-generic `IJobHandler` (`Services/Jobs/IJobHandler.cs`). `IJobHandler<T>` does not exist.
- **Duplicate detection**: `MessageId = IdempotencyKey` deduplicates only when the queue has duplicate detection
  enabled (Standard/Premium). As of 2026-09-12 it is **off** on the BFF queues (`service-bus.bicep`; issue #TBD).
  Receive-side idempotency (`IIdempotencyService` or a natural-key upsert) is therefore **MUST** for every handler.
- **Known non-conforming consumers** (bespoke message shapes): the three Office workers and the membership topic —
  migrate over time (issue #TBD).

---

## 4. ADR-013 — Amendment 2026-09-12 (placement pointers only)

Concise (`.claude/adr/ADR-013-ai-architecture.md`):
- L24 "Azure Functions for sync/extraction/scheduled work (already permitted by ADR-001; …)" →
  "Background, scheduled and event-driven AI work placed outside the BFF under **ADR-052** (e.g. the Insights sync pipelines)".
- L38 "MUST use Job Contract for background AI work (ADR-004)" → "MUST use the in-BFF mechanism for background
  AI work that stays in the BFF: queue-driven → ADR-004; schedule-driven → ADR-036 (e.g. `PlaybookSchedulerJob`);
  where it runs → ADR-052".
- L52 "(Functions are permitted only for out-of-band integration — see ADR-001)" → "(where non-request work runs: ADR-052)".
- Decision table L89 row "Is it event-driven (timer, queue, webhook) with no synchronous user wait? | NO | YES" →
  "Does ADR-052 place it outside the BFF (an F-signal outweighs the costs)? | NO | YES", with a note under the
  table: *a trigger type alone never decides placement — ADR-052.*

Full (`docs/adr/ADR-013-ai-architecture.md`): L53 and L232–233 → the same pointer wording; a dated amendment
line in the header.

---

## 5. Key directive rewrite — `.claude/constraints/bff-extensions.md`

### §D "New Background Work" — replaces the first bullet
- **MUST** decide the host — the BFF, Azure Functions or Container Apps Jobs — under **ADR-052**, and record it in
  the Placement Justification (§A).
- Inside the BFF, the mechanism follows the trigger: queue-driven → Service Bus + `IJobHandler` via
  `ServiceBusJobProcessor` (ADR-004); schedule-driven → `IScheduledJob` on `ScheduledJobHost` (ADR-036); startup /
  long-lived → plain `IHostedService`. **MUST NOT** add a hand-rolled timer `BackgroundService`.
- *(The remaining §D bullets — AI-coupled handlers in `Services/Ai/Jobs/`, no LLM calls outside `Services/Ai/`,
  the SPE writer-identity pattern — are unchanged.)*

### §E — replaces "MUST consider whether new AI work is event-driven … → Azure Functions per ADR-001"
- **MUST** decide where non-request AI work (sync, scheduled, webhook-triggered) runs under **ADR-052**. A trigger
  type alone never decides it.

### "Decision Criteria: Does This Belong in the BFF?" — replaces the row
| Question | Answer → BFF | Answer → Elsewhere |
|---|---|---|
| Does ADR-052 place it outside the BFF (an F-signal outweighs the costs)? | NO | YES (Functions / Container Apps Jobs per ADR-052) |

### §A.1 wording
"(Azure Functions for out-of-band work per ADR-001 …)" → "(Azure Functions or Container Apps Jobs per ADR-052; a
separate deployable per refined ADR-013 …)".
