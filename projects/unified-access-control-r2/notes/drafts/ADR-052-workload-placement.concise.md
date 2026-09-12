# ADR-052: Workload Placement — BFF, Azure Functions, Container Apps Jobs (Concise)

> **Status**: Proposed (Accepted when `unified-access-control-r2` task 102 merges) — owner-approved direction 2026-09-12
> **Domain**: Background, scheduled and event-driven work; hosting
> **Last Updated**: 2026-09-12
> **Supersedes**: ADR-001's Azure Functions / Durable Functions provisions · **Amends**: ADR-004, ADR-013, ADR-036

> **This ADR is the only place the placement rule is stated in full.** Other documents link here.
> `WorkloadPlacementDocDriftTests` fails the build if a contradicting phrasing reappears.

---

## Decision

**Two questions, never merged:**

| Question | Governed by |
|---|---|
| **Where does it run?** BFF · Azure Functions · Container Apps Jobs | **This ADR** |
| **How does it run inside the BFF?** | Queue → `IJobHandler` (**ADR-004**) · Schedule → `IScheduledJob` (**ADR-036**) · startup / long-lived → plain `IHostedService` |

**Principle: no default host, no prohibition.** Use Azure Functions where they streamline the system or improve
performance or flexibility; not where they only add moving parts. **Tie-breaker: fewer moving parts.** Record
the choice in the Placement Justification.

## Signals

| Favour Functions | Favour the BFF |
|---|---|
| **F1** Event intake that should not depend on BFF availability (webhooks, Event Grid, blob, change feed) | **B1** Needs the caller's identity (OBO, user-written SPE files) |
| **F2** Independent scaling — bursty or heavy work | **B2** Uses a large share of BFF domain code |
| **F3** Must run exactly once per schedule | **B3** Low volume; same scale signal, identity, release cadence |
| **F4** Isolation — failure, security, release cadence | **B4** Tied to a live request (streaming, chat, sub-500 ms) |
| **F5** Multi-step durable orchestration | |

Move to Functions when an F-signal is material **and** outweighs the Spaarke costs: identity grants per customer
(avoided by reusing the BFF identity), code extraction to shared libraries, a deployable per stamp, and Flex
cost/limits (~$15–25/mo per stamp; Linux only; no slots; UTC timers).

---

## Constraints

### ✅ MUST

- **MUST** record the host decision in the Placement Justification (`bff-extensions.md` §A), citing signals and costs
- **MUST** implement run-once scheduled work as a Functions timer OR an `IScheduledJob` under `ScheduledJobHost`'s distributed lease (ADR-036 A1) — never a per-instance timer
- **MUST** make every background handler idempotent per unit of work (every host is at-least-once)
- **MUST** keep `IScheduledJob` implementations host-neutral (no dependency on `ScheduledJobHost`)

### ❌ MUST NOT

- **MUST NOT** host user-facing BFF API endpoints in Azure Functions
- **MUST NOT** run work needing the caller's identity outside the BFF request holding it
- **MUST NOT** add new Azure WebJobs
- **MUST NOT** add a new hand-rolled timer `BackgroundService`

### When a Function is chosen

| Area | Rule |
|---|---|
| Hosting | .NET isolated worker, **Flex Consumption** (Premium only for a named Flex limit) |
| Tenancy | **Model 2**: one Function app per customer stamp. **Model 1**: a shared multi-tenant app is acceptable, with the BFF's tenant-isolation invariants (I1–I5) |
| Identity | **Reuse the stamp's BFF app identity by default** (same UAMI + federated credential → same app id and grants). Dedicated identity only when isolation is the reason. Managed identity only (ADR-028 A4) |
| Deploy / config | Bicep in the stamp's provisioning; same repo and CI/CD; Key Vault references |
| Observability | Shared App Insights; OpenTelemetry / W3C trace context |
| Events | Service Bus buffer where delivery must survive an outage; idempotent handlers; monitored DLQ |
| Code | Shared logic in `src/server/shared/*`; a Functions project **MUST NOT** reference `Sprk.Bff.Api` or copy BFF code |

### Orchestration and other hosts

- **Durable Task** (Durable Functions or the Durable Task SDK on App Service, on **Durable Task Scheduler**) is permitted for multi-step, long-running, human-gated or fan-out orchestration. Hand-rolled Service Bus + state machine needs a written reason. *(Replaces ADR-001's "No Durable Functions".)*
- **Container Apps Jobs** for heavy or long-running work needing a custom runtime; same guardrails.

---

## Compliance

| Check | Where |
|---|---|
| No Functions packages or Function-attributed methods in the BFF assembly | `tests/Spaarke.ArchTests/ADR001_MinimalApiTests.cs` |
| No contradicting placement phrasing in directives/docs | `tests/Spaarke.ArchTests/WorkloadPlacementDocDriftTests.cs` |
| Host choice recorded with signals + costs | Placement Justification; `code-review` / `adr-check` |

---

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-001](ADR-001-minimal-api.md) | BFF runtime (Minimal API, one pipeline). Its Functions provisions are superseded here |
| [ADR-004](ADR-004-job-contract.md) | Mechanism for queue-driven work inside the BFF |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Mechanism for scheduled work inside the BFF; the distributed lease |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Secret-free identity (A4) for any Function |
| [ADR-013](ADR-013-ai-architecture.md) | AI placement criteria defer to this ADR for background/event work |

**Full ADR**: [docs/adr/ADR-052-workload-placement.md](../../docs/adr/ADR-052-workload-placement.md) · **Evidence**:
`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`
