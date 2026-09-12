# ADR-052: Workload Placement — BFF, Azure Functions, Container Apps Jobs (Concise)

> **Status**: Proposed (Accepted when `unified-access-control-r2` task 102 merges) — owner-approved direction 2026-09-12
> **Domain**: Background, scheduled and event-driven work; hosting
> **Last Updated**: 2026-09-12
> **Supersedes**: ADR-001's Functions / Durable provisions · ADR-004's Durable prohibition · **Amends**: ADR-013, ADR-036

> **This ADR is the only place the placement rule is stated in full.** Other documents link here.
> `WorkloadPlacementDocDriftTests` fails the build if a contradicting phrasing reappears.

---

## Decision

| Question | Governed by |
|---|---|
| **Where does it run?** BFF · Azure Functions · Container Apps Jobs | **This ADR** |
| **How does it run inside the BFF?** | Queue/topic message → `IJobHandler` (**ADR-004**) · Schedule → `IScheduledJob` (**ADR-036**) · Startup, or a long-lived non-message connection → plain `IHostedService` |

A queue or topic consumer is never a "long-lived listener" — it is ADR-004 work.

**Principle: no default host, no prohibition.** Use Azure Functions where they streamline the system or improve
performance or flexibility; not where they only add moving parts. **Tie-breaker: fewer moving parts.** Record the
choice in the Placement Justification.

## Signals

| Favour Functions | Favour the BFF |
|---|---|
| **F1** Event intake that should not depend on BFF availability | **B1** Needs the caller's identity (OBO, user-written SPE files) |
| **F2** Independent scaling — bursty or heavy work | **B2** Uses a large share of BFF domain code |
| **F3** One dispatch per schedule without building coordination | **B3** Low volume; same scale signal, identity, release cadence |
| **F4** Isolation — failure, security, release cadence | **B4** Tied to a live request (streaming, chat, sub-500 ms) |
| **F5** Multi-step durable orchestration | |

Move out of the BFF when an F-signal is material **and** outweighs the costs: SPE access keyed to the BFF app
registration is not inherited, BFF code must be extracted to shared libraries, a deployable per stamp (app +
host storage + provisioning + CI/CD), and Flex cost/limits.

---

## Constraints

### ✅ MUST

- **MUST** record the host decision in the Placement Justification (`bff-extensions.md` §A), citing signals and costs
- **MUST** give scheduled work that must not run concurrently **one dispatch per schedule across instances** — a Functions timer, or an `IScheduledJob` under `ScheduledJobHost`'s distributed lease (ADR-036 A1). Never a per-instance timer. Execution stays at-least-once under retry
- **MUST** make every background handler idempotent per unit of work
- **MUST** keep `IScheduledJob` implementations free of `ScheduledJobHost` / `IBackgroundJobStore` / `ScheduledJobRegistry` dependencies
- **MUST** place a Functions project under `src/server/functions/<Name>/` so the server-source ArchTests cover it

### ❌ MUST NOT

- **MUST NOT** host user-facing BFF API endpoints in Azure Functions
- **MUST NOT** run work needing the caller's identity outside the BFF request holding it
- **MUST NOT** add new Azure WebJobs
- **MUST NOT** add a new hand-rolled timer `BackgroundService` (existing ones migrate when next touched — any PR changing their behaviour or timer loop)

### When a Function is chosen

| Area | Rule |
|---|---|
| Hosting | .NET isolated worker, **Flex Consumption** (Premium only for a named Flex limit, with owner approval) |
| Tenancy | **Model 2**: one app per customer stamp · **Model 1**: a shared multi-tenant app is acceptable, carrying invariants I1–I5 · **Fleet-scoped**: platform subscription, platform identity |
| Identity | **Reuse the stamp's managed identity**, app-only (`DefaultAzureCredential` + `AZURE_CLIENT_ID`). **MUST NOT** act as the BFF app registration (no confidential client, no OBO, no user tokens) or call BFF endpoints. No secrets — including identity-based host storage. Dedicated identity only for isolation, with owner approval |
| Inbound HTTP | Webhook-shaped only, with sender validation (Graph `clientState`, Event Grid validation, or an Entra app role) |
| Deploy / config | Bicep in the stamp's provisioning; same repo and CI/CD; Key Vault references |
| Observability | Shared App Insights; the supported isolated-worker telemetry integration; W3C trace context |
| Events | Service Bus buffer; atomic per-unit idempotency; `MaxDeliveryCount` → monitored DLQ; lock renewal; sessions for ordering; ≥1 always-ready instance for latency-sensitive intake |
| Code | Shared logic in `src/server/shared/*`; **MUST NOT** reference `Sprk.Bff.Api` or copy BFF code |

### Orchestration, other hosts, governance

- **Durable Task** (Durable Functions or the Durable Task SDK in a dedicated worker) on **Durable Task Scheduler** is permitted for multi-step, long-running, human-gated or fan-out orchestration — **in its own host, never inside `Sprk.Bff.Api`**. Model 2: a task hub per stamp; Model 1: a shared hub with `tenantId` in every input. Hand-rolled Service Bus + state machine needs a written reason.
- **Container Apps Jobs** for heavy or long-running work needing a custom runtime; same guardrails.
- **Owner approval (🔔)** for the first Function per tenancy model, any dedicated identity, any Premium plan, any Durable Task Scheduler resource. The first Function's one-time setup (provisioning handlers, CI, deploy procedure, pinned Core Tools) is its own task. Moving a BFF workload to a Function runs both paths behind an ADR-032 kill switch until the BFF path is retired.

---

## Compliance

| Check | Where |
|---|---|
| No Functions / Durable Task packages or Function-attributed methods in the BFF assembly | `ADR001_MinimalApiTests` |
| No contradicting placement phrasing outside marked historical regions | `WorkloadPlacementDocDriftTests` |
| No new hand-rolled timer `BackgroundService` (ratchet) · `IScheduledJob` host-neutrality · Functions projects under `src/server/functions/`, not referencing `Sprk.Bff.Api` | ArchTests added by task 102 |
| Host choice recorded with signals + costs | Placement Justification; `code-review` / `adr-check` |

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-001](ADR-001-minimal-api.md) | BFF runtime (Minimal API, one pipeline); its Functions provisions are superseded here |
| [ADR-004](ADR-004-job-contract.md) | Mechanism for queue-driven work in the BFF; its Durable prohibition is superseded here |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Mechanism for scheduled work in the BFF; the distributed lease |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Secret-free identity (A4) |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | Kill switch when moving a workload out of the BFF |
| [ADR-038](ADR-038-testing-strategy.md) | Test KEEP paths for Functions projects |
| [ADR-013](ADR-013-ai-architecture.md) | AI placement of non-request work defers to this ADR |

**Full ADR**: [docs/adr/ADR-052-workload-placement.md](../../docs/adr/ADR-052-workload-placement.md) · **Evidence**:
`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`
