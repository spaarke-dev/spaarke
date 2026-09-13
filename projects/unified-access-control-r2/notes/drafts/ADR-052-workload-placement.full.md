# ADR-052: Workload placement — the BFF, Azure Functions, or Container Apps Jobs

| Field | Value |
|-------|-------|
| Status | **Proposed** — owner-approved direction 2026-09-12; Accepted when task 102 merges |
| Date | 2026-09-12 |
| Authors | Spaarke Engineering — `unified-access-control-r2` task 102 (draft v2, after the Fable-tier review) |
| Supersedes | ADR-001's Azure Functions and Durable Functions provisions (ADR-001 Amendment A1); ADR-004's Durable Functions prohibition (ADR-004 Amendment A1) |
| Amends | ADR-013 (placement pointers), ADR-036 (placement statement + runtime rules) |
| Evidence | [`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`](../../projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md) |

> **Single source of truth.** This ADR is the ONLY place the placement rule is stated in full. Every other
> document states it by a one-line summary and a link here. `tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs`
> fails the build when a contradicting phrasing reappears outside a marked historical region.

---

## Context

Background work — work triggered by a queue, a schedule or an external event rather than a user request — had
four contradictory placement rules by 2026-09, never reconciled:

<!-- adr052-drift:allow reason="quotes the superseded rules as history" -->
| Era | Rule |
|---|---|
| 2025-09 → 2026-04 | "No Azure Functions" |
| 2026-05-19 | Functions permitted for narrowly-scoped out-of-band integration; "default to BackgroundService when close" |
| 2026-05-20 | "Event-driven (timer, queue, webhook) → Functions"; "`IJobHandler<T>`, not a free-form `IHostedService`" |
| 2026-06-21 | Scheduled work = in-process `IScheduledJob`; ADR-001 restated as "no Azure Functions" |
<!-- /adr052-drift:allow -->

The code followed none consistently: no Azure Functions, fourteen hand-rolled timer `BackgroundService`s (five
added after ADR-036 forbade new ones), three `IScheduledJob`s. An agent following the newest ADR was flagged for
violating an older constraint, and every new project was seeded with a flat ban.

Two facts made the question urgent:

1. **Each rule decided the host from the trigger.** Trigger and host are independent: a queue can be consumed
   in-process or by a Function; a schedule can fire in-process or as a Function timer.
2. **The in-process scheduler dispatches every job once per instance.** `ScheduledJobHost` has no lease; its
   duplicate probe is process-local; staging slots run it too; the full-stack template autoscales from two
   instances. Microsoft's guidance names exactly this: a scheduler on every instance "starts multiple copies".

Microsoft's current guidance (Azure Architecture Center *Background jobs*, 2026-03-30; *Web-Queue-Worker*;
*Choose a compute service*) places background work **per workload**, weighing independent scaling, security
boundary, release cadence and failure isolation against the added hosting cost.

The original caution against Functions was written at project start to avoid fragmenting the BFF — duplicated
auth, correlation and error handling, cold starts on user requests. That concern still holds for **user-facing
API endpoints**. It was never a sound rule for background or event work.

---

## Decision

### 1. Two questions, answered separately

| Question | Governed by | Depends on |
|---|---|---|
| **Where does this workload run?** (host) | **This ADR** | Coupling, lifecycle, scale signal, isolation — §3 |
| **How does it run inside the BFF?** (mechanism) | ADR-004 (queue) · ADR-036 (schedule) | The trigger |

Inside the BFF the mechanism follows the trigger:

| Trigger | Mechanism | ADR |
|---|---|---|
| Queue or topic message | Service Bus → `ServiceBusJobProcessor` → `IJobHandler` | ADR-004 |
| Schedule (cron, interval, daily-at-time) | `IScheduledJob` on `ScheduledJobHost` | ADR-036 |
| Once at startup · a long-lived connection that is not a message consumer (e.g. a Redis pub/sub subscriber) | Plain `IHostedService` | — |

A queue or topic consumer is **never** a "long-lived listener": it is ADR-004 work. The only exceptions are the
named non-conforming consumers listed in ADR-004 Amendment A1.

**No new hand-rolled timer `BackgroundService`** (a `BackgroundService` driving its own `PeriodicTimer`,
`Task.Delay` loop or wait-until loop). Existing ones migrate **when next touched** — meaning any PR that changes
the service's behaviour or its timer loop; log-only or comment-only edits do not count. An ArchTest ratchet holds
the existing set and lets it only shrink.

### 2. Principle

**No default host and no prohibition.** Azure Functions are the right solution when they streamline the system or
improve performance or flexibility, and the wrong one when they only add moving parts (owner, 2026-09-12). When
neither host is clearly stronger, **choose the option with fewer moving parts.** The choice and its reasoning are
recorded in the Placement Justification (`.claude/constraints/bff-extensions.md` §A; root CLAUDE.md §10).

### 3. Placement signals

**Favour Azure Functions** (or Container Apps Jobs — §8):

| # | Signal | Why |
|---|---|---|
| F1 | Event intake that should not depend on the BFF's availability — external webhooks, Event Grid, blob events, change feeds | Events keep flowing and retrying through BFF deploys, restarts and outages |
| F2 | Independent scaling — bursty or heavy work | Scales on queue depth rather than API load, and cannot starve the API |
| F3 | One dispatch per schedule without building coordination | A Functions timer trigger is a singleton across instances by design (storage lease) |
| F4 | Isolation — failure, security boundary, or release cadence | A bad parse or runaway job cannot take the API down |
| F5 | Multi-step durable orchestration — waits, human gates, fan-out/fan-in | Durable Task (§7): checkpoint and replay instead of a hand-rolled state machine |

**Favour the BFF:**

| # | Signal | Why |
|---|---|---|
| B1 | Needs the calling user's identity — OBO tokens, files the user wrote to SharePoint Embedded | Must run inside the user's request (`bff-extensions.md` Pattern 4) |
| B2 | Uses a large share of BFF domain code | A Function cannot reference `Sprk.Bff.Api`; the code must first move to shared libraries |
| B3 | Low volume, with the BFF's scale signal, identity and release cadence | Colocation is cheaper; Microsoft's partitioning guidance allows it |
| B4 | Tied to a live request — streaming, chat, sub-500 ms responses | Latency coupling |

A workload moves out of the BFF when at least one F-signal is material **and** outweighs the costs in §4.

### 4. The Spaarke cost of a Function — weigh it explicitly

1. **Identity and access.** Reusing the stamp's managed identity (§6) gives a Function everything the BFF's own
   app-only background work can do — its Dataverse application user, SharePoint Embedded app-only access, and its
   Key Vault, AI Search and Service Bus assignments — because in Azure the BFF's app-only calls already run as
   that managed identity (`GraphClientFactory`, `Graph:ManagedIdentity:Enabled=true`). No new grants, nothing to
   provision. What a Function does not get is the user-delegated (OBO) path, which it must never use (§5). Only a
   **dedicated** identity (§6) needs grants of its own.
2. **Code sharing.** BFF-coupled work needs its domain logic extracted into `src/server/shared/*` first.
3. **A deployable per stamp.** A Function app, its host storage account (`AzureWebJobsStorage`, which also holds
   the timer lease), provisioning steps (Bicep, app settings, acceptance), a CI/CD path, a deploy procedure and a
   test harness. The first Function carries this as a one-time cost (§10).
4. **Money and platform limits.** Flex Consumption with one always-ready instance: roughly $15–25 a month per
   stamp (knowledge-base estimate, 2026-05). Flex is Linux-only, has no deployment slots, runs timers in UTC only,
   and is not in every region.

### 5. Rules

**MUST**
- **MUST** record the host decision in the Placement Justification, citing the §3 signals and §4 costs.
- **MUST** give scheduled work that must not run concurrently on several instances **one dispatch per schedule
  across instances** — a Functions timer trigger, or an `IScheduledJob` under `ScheduledJobHost`'s distributed
  lease (ADR-036 A1). Never a per-instance timer. (Execution remains at-least-once under retry.)
- **MUST** make every background handler idempotent per unit of work, whatever its host.
- **MUST** keep `IScheduledJob` implementations host-neutral: no dependency on `ScheduledJobHost`,
  `IBackgroundJobStore` or `ScheduledJobRegistry`. (While jobs live in `Sprk.Bff.Api`, moving one to a Function
  still requires extracting its dependencies — host-neutrality removes the host coupling, not that work.)
- **MUST** place a Functions project under `src/server/functions/<Name>/`, so every server-source ArchTest
  (credential guards, tenant-isolation invariants) covers it.

**MUST NOT**
- **MUST NOT** host user-facing BFF API endpoints in Azure Functions.
- **MUST NOT** run work that needs the calling user's identity outside the BFF request that holds it.
- **MUST NOT** add new Azure WebJobs.
- **MUST NOT** add a new hand-rolled timer `BackgroundService` (§1).

### 6. Guardrails when a Function is chosen

| Area | Rule |
|---|---|
| Hosting | .NET isolated worker on **Flex Consumption**. Premium only for a named Flex limitation (owner approval, §10). |
| Tenancy — Model 2 | A Function app per customer stamp. |
| Tenancy — Model 1 | A shared multi-tenant Function app is acceptable. It carries the same tenant-isolation invariants as the shared BFF — `tenantId` on every AI Search query and Cosmos partition key (deployment guide §8, I1–I5) — enforced because it lives under `src/server/` (§5). |
| Tenancy — fleet-scoped | Platform-level work that is not per customer (e.g. the provisioning control plane) runs in the platform subscription under the platform's identity, never a customer stamp's. |
| Identity | **Reuse the stamp's user-assigned managed identity by default** — the identity the BFF's own app-only work already runs as, so no additional grants are needed; a dedicated identity only when isolation is the reason for the Function (owner approval, §10). The Function authenticates **app-only as that managed identity** (`DefaultAzureCredential` pinned by `AZURE_CLIENT_ID`). It **MUST NOT** use the identity's federated credential to act as the BFF app registration — no confidential client for the BFF app, no OBO, no accepting or exchanging user tokens — and **MUST NOT** call BFF endpoints. No secrets (ADR-028 A4), including the host storage account: identity-based `AzureWebJobsStorage`, no shared keys. |
| Inbound HTTP | Webhook-shaped triggers only, and each MUST validate its sender (Graph `clientState`, Event Grid subscription validation, or an Entra app role). A Function never serves an interactive API. |
| Deployment | Bicep inside the stamp's provisioning (Model 1 / Model 2 stacks); same repo, same CI/CD. |
| Configuration | Key Vault references. |
| Observability | The stamp's shared App Insights, using the supported App Insights / OpenTelemetry integration for the .NET isolated worker, with W3C trace context so a trace spans BFF → Service Bus → Function. |
| Events | Service Bus buffers event sources whose delivery must survive a Function outage. Handlers idempotent (atomic per-unit claim — ADR-004 A1); `MaxDeliveryCount` sends poison messages to the dead-letter queue, which is monitored and alerted; lock renewal configured for long handlers; sessions when ordering matters. Latency-sensitive intake (e.g. Graph notifications, which expect a fast acknowledgement) keeps **≥1 always-ready instance**. |
| Code | Shared logic lives in `src/server/shared/*`. A Functions project **MUST NOT** reference `Sprk.Bff.Api` and **MUST NOT** copy BFF code. |

### 7. Orchestration

**Durable Task** — Durable Functions, or the Durable Task SDK in a dedicated worker — backed by **Durable Task
Scheduler**, is permitted for multi-step, long-running, human-gated or fan-out orchestration. It runs in its **own
host, never inside `Sprk.Bff.Api`**; the ADR-001 ArchTest keeps the Durable Task namespaces banned in the BFF
assembly. Durable Task Scheduler follows the tenancy rules: a task hub per stamp for Model 2; a shared hub for
Model 1 with `tenantId` in every orchestration input; no document content in orchestration payloads (ADR-004).
Hand-rolled orchestration (Service Bus plus a state machine) remains permitted but needs a written reason in the
Placement Justification.

### 8. Container Apps Jobs

For heavy or long-running work that needs a custom runtime, OS tooling or execution beyond Functions limits —
including scheduled jobs. Identity, tenancy, deployment and observability rules as in §6.

### 9. Worked examples

| Workload | Signals | Placement |
|---|---|---|
| A daily job reading BFF-owned records and notifying users through BFF services | B2, B3; one dispatch per schedule | BFF: `IScheduledJob` under the lease |
| A Dataverse change feed projected into AI Search, independent of any user request | F1, F2 | Azure Functions (Service Bus trigger) |
| CPU-heavy processing of files the managed identity wrote | F2, F4 | Azure Functions — or the BFF while volume is low (tie-breaker) |
| A multi-day provisioning run with manual gates | F5; fleet-scoped | Durable Task in the platform subscription |
| Chat streaming | B1, B4 | BFF |

The inventory of current workloads and their assessed placement lives in the evidence note, not here, so this ADR
does not go stale as the inventory changes.

### 10. Governance and operations

- **Owner approval (🔔, root CLAUDE.md §6)** is required for: the first Function under each tenancy model; any
  dedicated identity; any Premium plan; any Durable Task Scheduler resource.
- **The first Function's one-time setup is its own task**: provisioning handlers (Bicep, app settings,
  acceptance), a CI workflow, a deploy procedure, and a pinned Azure Functions Core Tools version.
- **Moving an existing BFF workload to a Function**: run both behind an ADR-032 kill switch on the BFF side, drain
  the dead-letter queue, then remove the BFF path.
- **Tests** for Functions projects follow ADR-038's KEEP paths; no new test category.

---

## Consequences

**Positive**
- One citable rule for every project, provisioning handler and future Functions project.
- The trigger no longer decides the host; each workload's host follows from stated signals and costs.
- One-dispatch-per-schedule becomes a rule with a named mechanism, closing the duplicate-dispatch defect.
- Durable orchestration becomes available where it fits.

**Negative**
- The first Function carries a one-time cost that a project must budget (§10).
- Placement needs judgment. Mitigated by the signal table, the tie-breaker, owner approval for the first Function,
  and the review checklist.
- Stale phrasings exist across ~70 documents. Mitigated by one alignment pass plus the drift guard.

---

## Alternatives considered

| Alternative | Rejected because |
|---|---|
| Keep ADR-001's narrowed wording | Still forbids Functions where Microsoft recommends them (F2–F5); leaves trigger-versus-host confusion |
| Amend ADR-001 only | ADR-001 is about the BFF runtime; provisioning, identity and future Functions projects need one citable placement ADR (owner) |
| All background work in Functions | A deployable per stamp and code extraction for workloads with no F-signal — complexity without benefit |
| All background work in-process with a lease | Ignores workloads where isolation or independent scaling is the point |
| Reuse the BFF app registration (not just the managed identity) in Functions | Gives a Function the BFF's delegated power, including OBO — a shadow BFF by credential |
| Container Apps for everything | Heavier operations than Functions for the event-driven majority |

---

## Compliance

| Mechanism | Checks |
|---|---|
| `ADR001_MinimalApiTests` | No Functions or Durable Task packages, and no Function-attributed methods, inside the BFF assembly |
| `WorkloadPlacementDocDriftTests` | Contradicting placement phrasings do not reappear outside marked historical regions |
| Timer-service ratchet (ArchTest) | No new hand-rolled timer `BackgroundService`; the allow-listed existing set may only shrink |
| Host-neutrality (ArchTest) | `IScheduledJob` implementations do not depend on `ScheduledJobHost`, `IBackgroundJobStore` or `ScheduledJobRegistry` |
| Functions-project guard (ArchTest) | A project referencing the Functions worker SDK lives under `src/server/functions/` and does not reference `Sprk.Bff.Api` |
| Credential guards (existing ArchTests) | Cover Functions projects automatically, because they scan `src/server/**` |
| Placement Justification + `code-review` / `adr-check` | Host recorded with signals and costs; routing to ADR-052 / ADR-036 / ADR-004 |

**Review checklist**
- [ ] Host chosen with §3 signals and §4 costs recorded; tie-breaker applied where close
- [ ] Nothing that needs the caller's identity runs outside the BFF request
- [ ] Scheduled work that must not run concurrently gets one dispatch per schedule (timer trigger or lease)
- [ ] Handlers idempotent with an atomic per-unit claim; dead-letter queue monitored
- [ ] For a Function: §6 guardrails met (Flex, tenancy row, managed identity app-only, no secrets, sender validation, Bicep, Key Vault, telemetry, shared code only) and §10 approval obtained where required
