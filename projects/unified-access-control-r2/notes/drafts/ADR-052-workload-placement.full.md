# ADR-052: Workload placement — the BFF, Azure Functions, or Container Apps Jobs

| Field | Value |
|-------|-------|
| Status | **Proposed** — owner-approved direction 2026-09-12; Accepted when task 102 merges |
| Date | 2026-09-12 |
| Authors | Spaarke Engineering — `unified-access-control-r2` task 102 |
| Supersedes | ADR-001's Azure Functions and Durable Functions provisions (see ADR-001 Amendment A1) |
| Amends | ADR-004 (scope), ADR-013 (decision-table row), ADR-036 (placement statement + runtime rules) |
| Evidence | [`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`](../../projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md) |

> **Single source of truth.** This ADR is the ONLY place the placement rule is stated in full. Every other
> document — ADRs, constraints, patterns, skills, architecture docs, templates — states it by a one-line
> summary and a link here. `tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs` fails the build when a
> contradicting phrasing reappears.

---

## Context

Background work at Spaarke — work triggered by a queue, a schedule, or an external event rather than by a user
request — had four contradictory placement rules by 2026-09, written in four eras and never reconciled:

| Era | Rule | Where it still lived |
|---|---|---|
| 2025-09 → 2026-04 | "No Azure Functions" | The ADR-001 ArchTest message, ADR indexes, most architecture docs, pattern files, the project-setup template |
| 2026-05-19 | Functions permitted for narrowly-scoped out-of-band integration; "default to BackgroundService when close" | ADR-001, several constraints, the PR template |
| 2026-05-20 | "Event-driven (timer, queue, webhook) → Functions" and "`IJobHandler<T>`, not a free-form `IHostedService`" | `bff-extensions.md` §D/§E, ADR-013's decision table |
| 2026-06-21 | Scheduled work = in-process `IScheduledJob`; "no Azure Functions" | ADR-036, its pattern and data-model docs |

The code followed none of them consistently: zero Azure Functions, fourteen hand-rolled timer
`BackgroundService`s (five added after ADR-036 forbade new ones), three `IScheduledJob`s. An agent following the
newest ADR was flagged for violating an older constraint; every new project was seeded with "no Azure Functions".

Two facts made the question urgent rather than academic:

1. **The rule was answering the wrong question.** Each version decided *where* work runs from *what triggers
   it*. Trigger and host are independent: a queue can be consumed in-process or by a Function; a schedule can
   fire in-process or as a Function timer.
2. **The in-process scheduler runs every job once per instance.** `ScheduledJobHost` has no lease; its
   "already ran" check is process-local; staging slots run it too; the full-stack template autoscales from two
   instances. Microsoft's guidance names exactly this — a scheduler on every instance "starts multiple copies".

Microsoft's current guidance (Azure Architecture Center *Background jobs*, 2026-03-30; *Web-Queue-Worker*;
*Choose a compute service*) places background work **per workload**, weighing independent scaling, security
boundary, release cadence and failure isolation against the added hosting cost — neither "everything in the web
app" nor "everything in Functions".

The original "no Functions" caution was written at the start of the project to avoid fragmenting the BFF —
duplicated auth, correlation and error handling, and cold starts on user requests. That concern remains valid for
**user-facing API endpoints**. It was never a sound rule for background or event work, which is where Functions
are strongest.

---

## Decision

### 1. Two questions, answered separately

| Question | Governed by | Answer depends on |
|---|---|---|
| **Where does this workload run?** (the host) | **This ADR** | Coupling, lifecycle, scale signal, isolation — §3 |
| **How does it run inside the BFF?** (the mechanism) | ADR-004 (queue) · ADR-036 (schedule) | The trigger |

Inside the BFF the mechanism follows the trigger:

| Trigger | Mechanism | ADR |
|---|---|---|
| Queue / event | Service Bus → `ServiceBusJobProcessor` → `IJobHandler` | ADR-004 |
| Schedule (cron, interval, daily-at-time) | `IScheduledJob` on `ScheduledJobHost` | ADR-036 |
| Once at startup · long-lived listener | Plain `IHostedService` | — |

A **new hand-rolled timer `BackgroundService` is not permitted.** Existing ones migrate when next touched.

### 2. Principle

**No default host and no prohibition.** A workload goes where it fits best: Azure Functions are the right
solution when they streamline the system or improve performance or flexibility, and the wrong one when they only
add moving parts (owner, 2026-09-12). When neither host is clearly stronger, **choose the option with fewer
moving parts.** The choice and its reasoning are recorded in the Placement Justification
(`.claude/constraints/bff-extensions.md`; root CLAUDE.md §10).

### 3. Placement signals

**Signals that favour Azure Functions** (or Container Apps Jobs — §8):

| # | Signal | Why |
|---|---|---|
| F1 | Event intake that should not depend on the BFF's availability — external webhooks, Event Grid, blob events, change feeds | Events keep flowing and retrying through BFF deploys, restarts and outages |
| F2 | Independent scaling — bursty or heavy work | Scales on queue depth rather than API load, and cannot starve the API |
| F3 | Must run exactly once per schedule | A Functions timer trigger is a singleton by design (storage lease) |
| F4 | Isolation — failure, security boundary, or release cadence | A bad parse or runaway job cannot take the API down |
| F5 | Multi-step durable orchestration (waits, human gates, fan-out/fan-in) | Durable Task (§7) — checkpoint and replay instead of a hand-rolled state machine |

**Signals that favour the BFF:**

| # | Signal | Why |
|---|---|---|
| B1 | Needs the calling user's identity — OBO tokens, files the user wrote to SharePoint Embedded | Must run inside the user's request scope (`bff-extensions.md` Pattern 4); a Function holds no user token |
| B2 | Uses a large share of BFF domain code | A Function cannot reference the web assembly; the code would first have to move to shared libraries |
| B3 | Low volume, with the BFF's scale signal, identity and release cadence | Colocation is cheaper and Microsoft's partitioning guidance allows it |
| B4 | Tied to a live request — streaming, chat, sub-500 ms responses | Latency coupling |

A workload moves to Functions when at least one F-signal is material **and** outweighs the Spaarke costs in §4.

### 4. The Spaarke cost of a Function — weigh it explicitly

1. **Identity.** A new identity needs a Dataverse application user in every environment, a grant on each
   customer's SharePoint Embedded container-type **registration** (grants are keyed by app id and set per
   consuming tenant), and Key Vault / AI Search role assignments. Avoided by default — §6.
2. **Code sharing.** BFF-coupled work needs its domain logic extracted into `src/server/shared/*` first.
   Independent work (sync, projection) has little to share.
3. **A deployable per stamp.** The first Function adds provisioning steps (Bicep, app settings, acceptance),
   a CI/CD path, a deploy procedure and a test harness. One-time, then marginal.
4. **Money and platform limits.** Flex Consumption with one always-ready instance costs roughly $15–25 a month
   per stamp. Flex is Linux-only, has no deployment slots, runs timers in UTC only, and is not in every region.

### 5. Rules

**MUST**
- **MUST** record the host decision in the Placement Justification, citing the §3 signals and the §4 costs.
- **MUST** implement scheduled work that must run once per schedule either as a Functions timer trigger or as an
  `IScheduledJob` under `ScheduledJobHost`'s distributed lease (ADR-036 Amendment A1) — never as a per-instance timer.
- **MUST** make every background handler idempotent per unit of work, whatever its host. Every host here
  delivers at least once.
- **MUST** keep `IScheduledJob` implementations free of any host dependency, so a job can move between
  `ScheduledJobHost` and a Functions timer through a thin adapter. The adapter is built with the first
  Function-hosted job, not before.

**MUST NOT**
- **MUST NOT** host user-facing BFF API endpoints in Azure Functions.
- **MUST NOT** run work that needs the calling user's identity outside the BFF request that holds it.
- **MUST NOT** add new Azure WebJobs.
- **MUST NOT** add a new hand-rolled timer `BackgroundService` (see §1).

### 6. Guardrails when a Function is chosen

| Area | Rule |
|---|---|
| Hosting | .NET isolated worker on **Flex Consumption**. Premium only for a named Flex limitation. |
| Tenancy | Follows the stamp's model. **Model 2**: a Function app per customer stamp. **Model 1**: a shared multi-tenant Function app is acceptable, carrying the same tenant-isolation invariants as the shared BFF (`tenantId` on every query and partition key — deployment guide §8, I1–I5). |
| Identity | **Reuse the stamp's BFF app identity by default** — the same user-assigned managed identity and federated credential, hence the same app id, Dataverse application user and SharePoint Embedded grants. A dedicated identity only when isolation is the reason for the Function; provisioning then adds the Dataverse user, the container-type registration grant and the role assignments. Managed identity only — no secrets (ADR-028 A4). |
| Deployment | Bicep inside the stamp's provisioning (Model 1 / Model 2 stacks), same repo, same CI/CD. |
| Configuration | Key Vault references. |
| Observability | The stamp's shared App Insights; OpenTelemetry with W3C trace context so a trace spans BFF → Service Bus → Function. |
| Events | Service Bus buffers event sources whose delivery must survive a Function outage. Handlers idempotent; dead-letter queue monitored and alerted. |
| Code | Shared logic lives in `src/server/shared/*`. A Functions project **MUST NOT** reference `Sprk.Bff.Api` and **MUST NOT** copy BFF code. |

### 7. Orchestration

**Durable Task** — Durable Functions, or the Durable Task SDK on App Service — backed by **Durable Task
Scheduler**, is permitted for multi-step, long-running, human-gated or fan-out orchestration. Hand-rolled
orchestration (Service Bus plus a state machine) remains permitted but needs a written reason in the Placement
Justification. This replaces ADR-001's blanket "no Durable Functions", which current Microsoft guidance no longer
supports.

### 8. Container Apps Jobs

For heavy or long-running work that needs a custom runtime, OS tooling or execution beyond Functions limits —
including scheduled jobs. Identity, tenancy, deployment and observability rules as in §6.

### 9. Worked examples

| Workload | Signals | Placement |
|---|---|---|
| A daily job that reads BFF-owned records and notifies users through BFF services | B2, B3; must run once | BFF: `IScheduledJob` under the lease |
| A Dataverse change feed projected into AI Search, independent of any user request | F1, F2 | Azure Functions (Service Bus trigger) |
| CPU-heavy document processing on files the managed identity wrote | F2, F4 | Azure Functions, or the BFF while volume is low (tie-breaker) |
| A multi-day provisioning run with manual gates | F5 | Durable Task |
| Chat streaming | B1, B4 | BFF |

The current inventory of workloads and their assessed placement lives in the evidence note, not here, so this
ADR does not go stale when the inventory changes.

---

## Consequences

**Positive**
- One citable rule for every project, provisioning handler and future Functions project.
- The trigger no longer decides the host; each workload's host follows from stated signals and costs.
- Exactly-once scheduling becomes a rule with a named mechanism, closing the duplicate-execution defect.
- Durable orchestration becomes available where it fits, in line with current Microsoft guidance.

**Negative**
- The first Function carries a one-time cost (provisioning, CI/CD, deploy procedure), which a project must budget.
- Placement needs judgment. Mitigated by the signal table, the tie-breaker and the review checklist.
- Stale phrasings exist across ~50 documents. Mitigated by one alignment pass plus the drift guard.

---

## Alternatives considered

| Alternative | Rejected because |
|---|---|
| Keep ADR-001's "narrowly-scoped out-of-band" wording | It still forbids Functions where Microsoft recommends them (F2–F5) and leaves the trigger-versus-host confusion in place |
| Amend ADR-001 only | ADR-001 is about the BFF runtime; provisioning, identity and future Functions projects need one citable placement ADR (owner, 2026-09-12) |
| All background work in Functions | Adds a deployable per stamp and extracts BFF code for workloads with no F-signal (B2, B3) — complexity without benefit |
| All background work in-process, with a lease | Ignores F1/F2/F4 workloads, where isolation and independent scaling are the point |
| Container Apps for everything | Heavier operations than Functions for the event-driven majority |

---

## Compliance

| Mechanism | Checks |
|---|---|
| `tests/Spaarke.ArchTests/ADR001_MinimalApiTests.cs` | No Functions packages or Function-attributed methods inside the BFF assembly |
| `tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs` | Contradicting placement phrasings do not reappear in directives or docs |
| Placement Justification (`bff-extensions.md` §A) | Host choice recorded, with signals and costs |
| `code-review` and `adr-check` skills | Route the host to this ADR, schedule-driven work to ADR-036, and queue-driven work to ADR-004 |

**Review checklist**
- [ ] Host chosen with §3 signals and §4 costs recorded; tie-breaker applied where the choice was close
- [ ] Nothing that needs the caller's identity runs outside the BFF request
- [ ] Scheduled work that must run once uses a Functions timer or the scheduler lease
- [ ] Handlers idempotent; dead-letter queue monitored
- [ ] For a Function: §6 guardrails satisfied (Flex, tenancy model, identity reuse, Bicep, Key Vault, OpenTelemetry, shared code only)
