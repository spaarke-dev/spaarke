# ADR-052: Workload Placement — BFF, Azure Functions, Container Apps Jobs (Concise)

> **Status**: Accepted (2026-09-13) — owner-approved direction 2026-09-12 (D1–D7) and identity wording 2026-09-13; lands with `unified-access-control-r2` (PR #950)
> **Domain**: Background, scheduled and event-driven work; hosting
> **Last Updated**: 2026-09-13
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

Move out of the BFF when an F-signal is material **and** outweighs the costs. Reusing the managed identity needs no
new grants for managed-identity app-only access (it is not OBO, and not the CIAM Graph or Power BI app
registrations). The costs are: BFF code extracted to shared libraries; a deployable per stamp (app + host storage +
provisioning + CI/CD); Flex cost and limits.

---

## Constraints

### ✅ MUST

- **MUST** record the host decision in the Placement Justification (`bff-extensions.md` §A), citing signals and costs
- **MUST** give scheduled work that must not run concurrently **one dispatch per schedule across instances** — a Functions timer, or an `IScheduledJob` under `ScheduledJobHost`'s distributed lease (ADR-036 A1). Never a per-instance timer. Execution stays at-least-once under retry
- **MUST** make every background handler idempotent per unit of work
- **MUST** keep `IScheduledJob` implementations free of `ScheduledJobHost` / `IBackgroundJobStore` / `ScheduledJobRegistry` dependencies
- **MUST** place a Functions project under `src/server/functions/<Name>/` so the `src/server/**` ArchTests cover it (credential guards and I2/I3 as of 2026-09-13; the first Function's setup widens I4–I6)

### ❌ MUST NOT

- **MUST NOT** host user-facing BFF API endpoints in Azure Functions
- **MUST NOT** run work needing the caller's identity outside the BFF request holding it
- **MUST NOT** add new Azure WebJobs
- **MUST NOT** add a new hand-rolled timer `BackgroundService` (existing ones migrate when next touched — any PR changing their behaviour or timer loop)

### When a Function is chosen

| Area | Rule |
|---|---|
| Hosting | .NET isolated worker, **Flex Consumption** (Premium only for a named Flex limit, with owner approval) |
| Tenancy | **Model 2**: one app per customer stamp · **Model 1**: a shared multi-tenant app is acceptable, carrying invariants I2–I5 (deployment guide §8) · **Fleet-scoped**: platform subscription, platform identity |
| Identity | **Reuse the stamp's user-assigned managed identity**, app-only (`DefaultAzureCredential` pinned to its client ID) — ADR-028 A4's **app-only** row: the BFF's managed-identity access, no new grants, no user sign-in. **MUST NOT** use A4's **confidential-client** row (no MSAL confidential client, no client-assertion or certificate credential, no OBO, no user tokens), **MUST NOT** impersonate a Dataverse caller (ADR-028 A5 headers — acting as a user outside a request), and **MUST NOT** call BFF endpoints. No secrets — including identity-based host storage. Dedicated identity only for isolation or for access bound to another app registration (e.g. CIAM Graph, Power BI), with owner approval |
| Inbound HTTP | Webhook-shaped only, with sender validation (Graph `clientState`, Event Grid validation, or an Entra app role) |
| Deploy / config | Bicep in the stamp's provisioning; same repo and CI/CD; Key Vault references |
| Observability | Shared App Insights; the supported isolated-worker telemetry integration; W3C trace context |
| Events | Service Bus buffer; atomic per-unit idempotency; `MaxDeliveryCount` → monitored DLQ; lock renewal; sessions for ordering; ≥1 always-ready instance for latency-sensitive intake |
| Code | Shared logic in `src/server/shared/*`; **MUST NOT** reference `Sprk.Bff.Api` or copy BFF code |

### Orchestration, other hosts, governance

- **Durable Task** (Durable Functions or the Durable Task SDK in a dedicated worker) on **Durable Task Scheduler** is permitted for multi-step, long-running, human-gated or fan-out orchestration — **in its own host, never inside `Sprk.Bff.Api`**. Model 2: a task hub per stamp; Model 1: a shared hub with `tenantId` in every input. No document content in orchestration payloads (ADR-004). Hand-rolled Service Bus + state machine needs a written reason.
- **Container Apps Jobs** for heavy or long-running work needing a custom runtime; same guardrails.
- **Owner approval (🔔)** for the first Function per tenancy model, any dedicated identity, any Premium plan, any Durable Task Scheduler resource.
- **The first Function's one-time setup is its own task**: provisioning handlers, CI, deploy procedure, pinned Core Tools, widening the I4–I6 tenant ArchTests to `src/server/functions/**`, and aligning `infra/insights/modules/function-app.bicep` (#985).
- **Moving a BFF workload to a Function**: run both paths behind an ADR-032 kill switch, drain the dead-letter queue, then retire the BFF path.

---

## Compliance

| Check | Where |
|---|---|
| No Functions / Durable Task packages or Function-attributed types, methods or parameters in the BFF assembly | `ADR001_MinimalApiTests` |
| No contradicting placement phrasing outside marked historical regions | `WorkloadPlacementDocDriftTests` |
| No new hand-rolled timer `BackgroundService` (ratchet) · `IScheduledJob` host-neutrality · Functions projects under `src/server/functions/`, not referencing `Sprk.Bff.Api`, no confidential-client / assertion / OBO / impersonation types | `WorkloadPlacementGuardTests` |
| Host choice recorded with signals + costs | Placement Justification; `code-review` / `adr-check` |

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-001](ADR-001-minimal-api.md) | BFF runtime (Minimal API, one pipeline); its Functions provisions are superseded here |
| [ADR-004](ADR-004-job-contract.md) | Mechanism for queue-driven work in the BFF; its Durable prohibition is superseded here |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Mechanism for scheduled work in the BFF; the distributed lease |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | A4: the app-only row a Function uses; the confidential-client row it must not. A5: the impersonation it must not use |
| [ADR-032](ADR-032-bff-nullobject-kill-switch.md) | Kill switch when moving a workload out of the BFF |
| [ADR-038](../../docs/adr/ADR-038-testing-strategy.md) | Test KEEP paths for Functions projects |
| [ADR-013](ADR-013-ai-architecture.md) | AI placement of non-request work defers to this ADR |

## When to Reference This ADR

**Load when**: adding background, scheduled, queue- or event-driven work; proposing an Azure Function, Container
Apps job or Durable Task orchestration; writing a Placement Justification; reviewing where work runs.

**Full ADR**: [docs/adr/ADR-052-workload-placement.md](../../docs/adr/ADR-052-workload-placement.md) · **Evidence**:
`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`
