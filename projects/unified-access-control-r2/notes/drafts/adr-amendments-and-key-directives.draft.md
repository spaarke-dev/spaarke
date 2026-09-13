# Draft v2 — ADR amendments + key directive rewrites for ADR-052 (task 102)

> **Status**: DRAFT v2 — revised after the Fable-tier adversarial review (2026-09-12). Awaiting owner approval.
> Nothing here is applied yet.
> **Companion drafts**: `ADR-052-workload-placement.full.md`, `ADR-052-workload-placement.concise.md`,
> `drift-guard-design.draft.md`.
> **Rule for every text below**: state the placement rule by a one-line summary plus a link to ADR-052. Where an
> amendment QUOTES a superseded rule, wrap the quote in `<!-- adr052-drift:allow reason="…" -->` markers.

---

## 1. ADR-001 — Amendment A1 (2026-09-12)

### 1a. Full ADR (`docs/adr/ADR-001-minimal-api-and-workers.md`)

- **Title** → `ADR-001: Minimal API as the single BFF runtime` (file name unchanged).
- **Header**: `Status` → **Accepted, as amended** · `Updated` → `2026-09-12 (Amendment A1)`.
- **Banner** (new, under the header):
  > ⚠️ **READ Amendment A1 FIRST.** ADR-052 now governs **where** background, scheduled and event-driven work
  > runs. A1 supersedes this ADR's Azure Functions and Durable Functions provisions; the original text below is
  > kept as history inside marked regions. Everything about the BFF runtime itself — Minimal API, one
  > middleware pipeline, ProblemDetails, `/healthz`, endpoint-level authorization — is unchanged and binding.
- **The superseded original sections** (Decision rows 4–5, "When Azure Functions ARE / are NOT appropriate",
  "Operational requirements when using Functions", AI-Directed Coding Guidance bullets 3–5, Compliance items 2–4,
  and the Operationalization "Docs" row) are **kept verbatim but wrapped** in
  `<!-- adr052-drift:allow reason="ADR-001 original text, superseded by A1 / ADR-052" -->` markers, each with a
  one-line "Superseded by ADR-052 §N" note.
- **New section, appended:**

```markdown
## Amendment A1 (2026-09-12): workload placement moves to ADR-052

> **Status**: Accepted (resolution path **B — amendment**, root CLAUDE.md §6.5; owner decision 2026-09-12).
> **Driver**: `unified-access-control-r2` task 102.
> **Evidence**: `projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`.

### Why
By 2026-09 four incompatible placement rules existed, and each decided the host from the trigger. This ADR's
original caution guarded one thing — fragmenting the BFF runtime. That still holds for user-facing endpoints. It
never justified forbidding Functions for background or event work.

### What is superseded (→ ADR-052)
| Provision | Now governed by |
|---|---|
| Azure Functions permissions and criteria, operational requirements | ADR-052 §2–§6 |
| "No Durable Functions" | ADR-052 §7 — Durable Task permitted in its own host, never inside the BFF |
| Design documents naming only "App Service (Minimal API + Workers)" | Design documents name the host chosen under ADR-052 |

### What is refined
"BackgroundService + Service Bus for BFF-coupled async work" now reads: inside the BFF, the mechanism follows the
trigger — queue/topic → ADR-004; schedule → ADR-036. Where the work runs at all is ADR-052's decision.

### What is unchanged
A single BFF runtime on App Service; Minimal API for every BFF endpoint; **no BFF endpoints in Azure Functions**;
one middleware pipeline; ProblemDetails; `/healthz`; endpoint-level resource authorization (ADR-008). The ArchTest
`ADR001_MinimalApiTests` still bans Functions **and Durable Task** packages, and Function-attributed methods,
**inside the BFF assembly**.
```

### 1b. Concise ADR (`.claude/adr/ADR-001-minimal-api.md`) — replacement body

- Title → `ADR-001: Minimal API BFF Runtime (Concise)`; `Last Updated` → 2026-09-12 (Amendment A1).
- **Decision**: single ASP.NET Core App Service BFF · Minimal API for synchronous endpoints · one middleware
  pipeline · **background, scheduled and event-driven work: where it runs → [ADR-052](ADR-052-workload-placement.md);
  how it runs inside the BFF → ADR-004 (queue) / ADR-036 (schedule)**.
- **MUST**: Minimal API for all BFF HTTP endpoints · single `Program.cs` pipeline · ProblemDetails · `/healthz`.
- **MUST NOT**: host BFF endpoints in Azure Functions · duplicate BFF auth / correlation / ProblemDetails
  infrastructure outside the BFF · put Functions or Durable Task packages inside `Sprk.Bff.Api`.
- **Remove**: the Functions-permission bullet and list, "No Durable Functions", the "(when using Functions)"
  MUST, and the "Acceptable Pattern: Functions for out-of-band integration" example. Keep the anti-pattern example.
- **Integration table**: add ADR-004, ADR-036, ADR-052.

### 1c. Index rows
- `.claude/adr/INDEX.md`: `| ADR-001 | Minimal API BFF runtime | BFF endpoints in Minimal API; background-work placement → ADR-052 | Accepted (amended 2026-09-12) |`
  and a new row `| ADR-052 | Workload placement | Host per workload on merit (BFF / Functions / Container Apps Jobs); fewer moving parts wins ties | Proposed → Accepted on merge |`
- `docs/adr/README-ADRs.md` L15 → `ADR-001: Minimal API as the single BFF runtime (placement of background work: ADR-052)`; L85 →
  "CI fails on Azure Functions / Durable Task packages or Function-attributed methods **inside the BFF assembly** ([ADR-001], [ADR-052])"; add an ADR-052 entry.
- `docs/adr/ADR-VALIDATION-PROCESS.md` L19 → "ADR-001: no Functions / Durable Task packages or Function-attributed methods inside the BFF assembly".

---

## 2. ADR-036 — Amendment A1 (2026-09-12)

### 2a. Full ADR (`docs/adr/ADR-036-background-job-infrastructure.md`)

Header: `Updated | 2026-09-12 (Amendment A1)`; `Status` → **Accepted, as amended**. Banner: *READ A1 FIRST — it
corrects this ADR's statement of ADR-001, records how the scheduler actually runs today, and adds the dispatch,
idempotency, retry, heartbeat and registration rules.* The existing "persist to Dataverse / operators tune cron
via Dataverse" MUSTs are annotated *"target state — see A1 §2"*.

```markdown
## Amendment A1 (2026-09-12): placement, runtime as built, and one dispatch per schedule

> **Status**: Accepted (path **B**, root CLAUDE.md §6.5; owner decision 2026-09-12). **Driver**:
> `unified-access-control-r2` task 102 (this text); task 103 implements the lease, slot guard and helper.

### 1. Placement
Where scheduled work runs is decided by **ADR-052** — the BFF, a Functions timer, or a Container Apps job. This
ADR governs **how scheduled work runs inside the BFF**. The earlier cross-reference
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
   unavailable**, behaviour follows the owner decision recorded in task 103 (A1.1) — this amendment does not
   prescribe it. Execution remains at-least-once under retry.
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
   bootstrap class.
7. **Host-neutrality (MUST).** Jobs do not depend on `ScheduledJobHost`, `IBackgroundJobStore` or
   `ScheduledJobRegistry` (ADR-052 §5).

### 4. Corrections
- `MembershipReconciliationJob` **shipped** (registered in `MembershipModule`); it is not deferred.
- Third consumer: `GrantExpiryReminderJob` (`unified-access-control-r2` task 100).

### 5. Until task 103 merges
The three shipped jobs still dispatch once per instance and rely on per-unit idempotency (owner 2026-09-12: the
work is in dev only, so no formal exception is recorded).

### 6. Still deferred
`DataverseBackgroundJobStore` — durable history, fleet-wide enable/disable (GitHub issue #TBD).
```

### 2b. Concise ADR (`.claude/adr/ADR-036-background-job-infrastructure.md`)
- `Last Updated` → 2026-09-12 (Amendment A1); status "Accepted, as amended".
- Reference consumers: PlaybookSchedulerJob, MembershipReconciliationJob (**shipped**), GrantExpiryReminderJob.
- New **"Runtime as built"** block (4 bullets from 2a §2).
- The **MUST** list gains rules 1–7 (one line each). The "seed a `sprk_backgroundjob` row" and "operators tune cron
  via Dataverse" MUSTs are marked *target state (A1 §2)*.
- MUST NOT: replace "ADR-001 prefers in-process" with "Where scheduled work runs is ADR-052's decision."
- Integration table: the ADR-001 row → `ADR-052 | Where scheduled work runs; this ADR = the in-BFF mechanism`.
- The source comment at `src/server/shared/Spaarke.Scheduling/ScheduledJobHost.cs:28` is aligned the same way.

---

## 3. ADR-004 — Amendment A1 (2026-09-12)

Full (`docs/adr/ADR-004-async-job-contract.md`): header `Updated`, banner, appended section. Concise
(`.claude/adr/ADR-004-job-contract.md`): Decision line, a **Scope** block, and the MUST/MUST NOT edits below.

- **Scope.** Queue- and topic-driven async work consumed from Service Bus. Schedule-driven work → ADR-036. Where
  either runs → ADR-052. The Decision "one standard Job Contract for all async work" becomes "…for all
  **queue-driven** async work".
- **Durable.** The prohibition <!-- adr052-drift:allow reason="quotes the withdrawn rule" -->"MUST NOT use
  Durable Functions for orchestration"<!-- /adr052-drift:allow --> is **withdrawn**: orchestration hosting is
  ADR-052 §7. It is replaced by "Multi-step orchestration follows ADR-052 §7; job payloads still carry no document
  content."
- **Handler contract.** The non-generic `IJobHandler` (`Services/Jobs/IJobHandler.cs`).
- **Receive-side idempotency (MUST).** An **atomic** per-message marker — SET NX, or a conditional upsert on a
  natural key. `IIdempotencyService` as built is check-then-set and fails open; it is not sufficient on its own
  until fixed (GitHub issue #TBD).
- **Duplicate detection.** `MessageId = IdempotencyKey` deduplicates only when the queue has duplicate detection
  enabled, which Service Bus fixes **at queue creation** (Standard/Premium). It is off on the BFF queues
  (`service-bus.bicep`); turning it on means recreating the queues (GitHub issue #TBD).
- **Known non-conforming consumers** (bespoke message shapes): the three Office workers and the membership topic —
  the only permitted exceptions to "a consumer is ADR-004 work", until migrated (GitHub issue #TBD).

---

## 4. ADR-013 — Amendment 2026-09-12 (placement pointers only)

Concise (`.claude/adr/ADR-013-ai-architecture.md`):
- L24 → "Background, scheduled and event-driven AI work placed outside the BFF under **ADR-052** (e.g. the Insights sync pipelines)".
- L38 → "MUST use the in-BFF mechanism for background AI work that stays in the BFF: queue-driven → ADR-004; schedule-driven → ADR-036 (e.g. `PlaybookSchedulerJob`); where it runs → ADR-052".
- L52 → "(where non-request work runs: ADR-052)".
- Decision-table row (L89) → "Does ADR-052 place it outside the BFF (an F-signal outweighs the costs)? | NO | YES", with a note: *a trigger type alone never decides placement — ADR-052.*

Full (`docs/adr/ADR-013-ai-architecture.md`): L53, L232–233 → the same pointer wording; L407's
`IJobHandler<AiIndexingJob>` sample → `IJobHandler`; a dated amendment line in the header.

---

## 5. Other ADRs touched (pointer edits)

| ADR | Line | Change |
|---|---|---|
| ADR-002 (full L80, concise L60) | "External services \| BackgroundService / Azure Functions" | "External services \| a host per ADR-052" |
| ADR-032 (full L242) | Functions mention | Point to ADR-052 §10 (kill switch when moving a workload out of the BFF) |

---

## 6. Key directive rewrite — `.claude/constraints/bff-extensions.md`

### §A.1
"(Azure Functions for out-of-band work per ADR-001 …)" → "(Azure Functions or Container Apps Jobs per ADR-052; a
separate deployable per refined ADR-013 …)".

### §D "New Background Work" — replaces the first bullet
- **MUST** decide the host — the BFF, Azure Functions or Container Apps Jobs — under **ADR-052**, and record it in
  the Placement Justification (§A).
- Inside the BFF, the mechanism follows the trigger: queue/topic message → Service Bus + `IJobHandler` via
  `ServiceBusJobProcessor` (ADR-004); schedule → `IScheduledJob` on `ScheduledJobHost` (ADR-036); startup, or a
  long-lived connection that is not a message consumer → plain `IHostedService`. A queue or topic consumer is
  never a "long-lived listener". **MUST NOT** add a hand-rolled timer `BackgroundService`.
- *(The remaining §D bullets are unchanged.)*

### §E — replaces the "event-driven … → Azure Functions per ADR-001" bullet
- **MUST** decide where non-request AI work (sync, scheduled, webhook-triggered) runs under **ADR-052**. A trigger
  type alone never decides it.

### "Decision Criteria: Does This Belong in the BFF?" — replaces the row
| Question | Answer → BFF | Answer → Elsewhere |
|---|---|---|
| Does ADR-052 place it outside the BFF (an F-signal outweighs the costs)? | NO | YES (Functions / Container Apps Jobs per ADR-052) |
