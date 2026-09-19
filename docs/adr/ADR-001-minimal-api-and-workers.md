# ADR-001: Minimal API as the single BFF runtime

| Field | Value |
|-------|-------|
| Status | **Accepted, as amended** |
| Date | 2025-09-27 |
| Updated | 2026-09-12 (Amendment A1 — workload placement moved to ADR-052) |
| Authors | Spaarke Engineering |

> ⚠️ **READ Amendment A1 FIRST.** [ADR-052](ADR-052-workload-placement.md) now governs **where** background,
> scheduled and event-driven work runs. A1 supersedes this ADR's Azure Functions and Durable Functions provisions;
> the original text below is kept as history inside marked regions. Everything about the BFF runtime itself —
> Minimal API, one middleware pipeline, ProblemDetails, `/healthz`, endpoint-level authorization — is unchanged
> and binding.

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-001 Concise](../../.claude/adr/ADR-001-minimal-api.md) - decision + constraints + patterns
- [ADR-052 Concise](../../.claude/adr/ADR-052-workload-placement.md) - where background, scheduled and event-driven work runs
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules for API development
- [Endpoint Definition Pattern](../../.claude/patterns/api/endpoint-definition.md) - Minimal API code examples
- [Service Registration Pattern](../../.claude/patterns/api/service-registration.md) - DI registration examples

**When to load this full ADR**: Historical context, alternatives analysis, detailed consequences

---

## Context

SDAP requires a single, predictable runtime for synchronous request/response and asynchronous jobs tied to BFF business logic. The original concern (2025-09) was that fragmenting the BFF across Azure Functions and ASP.NET Core duplicates cross-cutting concerns (auth, retries, correlation, ProblemDetails), splits identity flows, introduces cold-start variance, and divides debugging/deployment.

<!-- adr052-drift:allow reason="ADR-001 original context (2026-05-19 wording), superseded by A1 / ADR-052" -->
That concern remains valid for **the BFF runtime itself**. It does not extend to **out-of-band integration workloads** (e.g., Dataverse → AI Search sync, scheduled indexers, event-triggered extraction pipelines, third-party webhook receivers) that are genuinely independent of the BFF request pipeline. For those, Azure Functions are the right tool — they're event-driven, scale independently, Bicep-deployable per tenant, and don't share any of the cross-cutting concerns the original concern was about.
<!-- /adr052-drift:allow -->

> **Superseded by ADR-052 §2–§3 (A1):** placement is decided per workload from stated signals and costs, not by
> whether the work fits an "integration" category.

## Decision

| Rule | Description |
|------|-------------|
| **Single BFF runtime** | Run the BFF on a single ASP.NET Core App Service |
| **Minimal API** | Use Minimal API for all synchronous BFF endpoints |
| **Background work inside the BFF** *(refined by A1)* | The mechanism follows the trigger: queue/topic message → Service Bus + `IJobHandler` (ADR-004); schedule → `IScheduledJob` (ADR-036). Where the work runs at all is ADR-052's decision |

<!-- adr052-drift:allow reason="ADR-001 original Decision rows 4–5 and Functions criteria (2026-05-19), superseded by A1 / ADR-052" -->
| Rule (original — superseded) | Description |
|------|-------------|
| **Azure Functions for out-of-band integration** | **Permitted** for workloads that are independent of the BFF request pipeline and meet the criteria below |
| **No Durable Functions** | Continue to avoid Durable Functions; if multi-step orchestration is needed, use Service Bus + state machine |

### When Azure Functions ARE appropriate (original — superseded)

- Workload is genuinely independent of the BFF request pipeline (no shared auth context, no shared correlation flow)
- Trigger semantics that BackgroundService can't elegantly express (timer-driven indexing, webhook receivers, event grid / blob triggers, Dataverse change-feed sync)
- Lifecycle is independent of the BFF (sync can run while BFF is down for maintenance; failure of one doesn't cascade to the other)
- Per-tenant deployable as part of the standard Bicep package

### When Azure Functions are NOT appropriate (original — superseded)

- Re-implementing endpoints that belong in the BFF
- Duplicating BFF auth, correlation, or ProblemDetails infrastructure
- Splitting a single coherent BFF concern across hosts to "feel modular"
- Anywhere a BackgroundService worker over Service Bus is a clean fit

### Operational requirements when using Functions (original — superseded)

- Must be deployable via Bicep alongside the rest of the stack (single per-tenant deployment unit)
- Must publish to Application Insights with correlation IDs that join the BFF's traces
- Must use the same identity/secret-management story (Managed Identity, Key Vault)
- Must be versioned in the same repo and follow the same CI/CD pipeline
- Code must be reviewable as a peer of BFF code, not a parallel runtime with its own conventions
<!-- /adr052-drift:allow -->

> **Superseded by ADR-052:** placement criteria §2–§5; guardrails for a chosen Function §6; Durable Task, permitted
> in its own host and never inside the BFF, §7.

## Consequences

<!-- adr052-drift:allow reason="ADR-001 original consequences (2026-05-19), superseded by A1 / ADR-052" -->
**Positive:**
- One middleware pipeline for BFF cross-cutting concerns (auth, correlation, retry, ProblemDetails) — preserved
- Simpler debugging and observability for the BFF — preserved
- **New**: Event-driven and timer-driven integration work has a natural home (Functions) without contorting BackgroundService for triggers it wasn't designed for
- **New**: Multi-tenant sync and extraction workloads can scale independently of the BFF

**Negative:**
- Two deployable runtimes (BFF + Functions) when out-of-band integration is in use
- Discipline required to keep Functions narrowly scoped and not let them grow into a shadow BFF
- Slightly more complex Bicep + CI/CD pipeline
<!-- /adr052-drift:allow -->

> The consequences of placement decisions are now recorded in ADR-052. The first two positive bullets — one
> pipeline, simpler BFF debugging — remain this ADR's.

## Alternatives Considered

<!-- adr052-drift:allow reason="ADR-001 original alternatives (2026-05-19); the Durable row is superseded by ADR-052 §7" -->
| Alternative | Rejection Reason |
|-------------|------------------|
| Azure Functions for BFF endpoints | Original concern stands: duplicates cross-cutting concerns, splits the BFF runtime |
| Durable Functions for orchestration | Cognitive load, lock-in, host fragmentation; Service Bus + state machine remains cleaner |
| BackgroundService for all triggers (including webhooks, event grid, timers) | BackgroundService isn't ergonomic for event-grid or webhook ingress; coupling them to the BFF lifecycle creates fragility |
<!-- /adr052-drift:allow -->

> The first row still holds (no BFF endpoints in Azure Functions). The Durable row is superseded by ADR-052 §7.

## Operationalization

| Component | Implementation |
|-----------|----------------|
| Entry point | `Program.cs` registers Minimal API, ServiceBusClient, HttpClient policies, ProblemDetails, BackgroundService workers |
| Workers | `Azure.Messaging.ServiceBus` ServiceBusProcessor with Polly retries and idempotent handlers |
| Health | `/healthz` probe exposed |
| Logging | Application Insights + structured `ILogger` logging (no PII) |
| Docs | Design documents name the host chosen under ADR-052 for each background workload *(A1; the original row required "App Service (Minimal API + Workers)" wording)* |

**Clarification:** “Single middleware pipeline” refers to shared cross-cutting concerns (exception handling/`ProblemDetails`, correlation/telemetry, security headers, rate limiting). Resource authorization remains **endpoint-level** (endpoint filters/policies) per ADR-008.

## AI-Directed Coding Guidance

- New synchronous capabilities: add Minimal API endpoints (route groups), return `ProblemDetails` on errors.
- New asynchronous capabilities *(refined by A1)*: decide where they run under ADR-052. Inside the BFF, queue-driven work enqueues an ADR-004 `JobContract` and is handled by an `IJobHandler`; schedule-driven work is an ADR-036 `IScheduledJob`.

<!-- adr052-drift:allow reason="ADR-001 original AI guidance bullets 3–5 (2026-05-19), superseded by A1 / ADR-052" -->
- New out-of-band integration capabilities (sync, extraction, webhook ingress, timer-driven indexing): Azure Functions are permitted when they meet the criteria in the Decision section. Default to BackgroundService when the choice is genuinely close — only reach for Functions when the trigger or lifecycle independence clearly justifies it.
- Do not introduce Durable Functions packages or attributes — use Service Bus + state machine for multi-step orchestration.
- Do not introduce Functions where the work clearly belongs in the BFF.
<!-- /adr052-drift:allow -->

> **Superseded by ADR-052:** signals and costs (§3–§4) with the fewer-moving-parts tie-breaker (§2); Durable Task
> in its own host (§7). The one rule that survives unchanged: nothing inside `Sprk.Bff.Api` uses Functions or
> Durable Task packages.

## Success Metrics

| Metric | Target |
|--------|--------|
| MTTR | Reduced |
| API surface | Single OpenAPI spec |
| Latency p95/p99 | Stable |
| Cold-start | Predictable |
| Duplicate retry stacks | Zero |

## Compliance

**Code review checklist:**
- [ ] BFF endpoints defined in Minimal API style (BFF endpoints are never hosted in Azure Functions)
- [ ] Background work: host recorded under ADR-052; inside the BFF, queue-driven → ADR-004, schedule-driven → ADR-036 *(refined by A1)*
- [ ] `Sprk.Bff.Api` references neither Azure Functions nor Durable Task packages and has no Function-attributed methods (`ADR001_MinimalApiTests`)
- [ ] Single middleware pipeline for BFF cross-cutting concerns (no duplicate auth/retry stacks within the BFF)
- [ ] Resource authorization implemented at the endpoint (ADR-008)

<!-- adr052-drift:allow reason="ADR-001 original compliance items 3–4 (2026-05-19), superseded by A1 / ADR-052" -->
- ~~If Azure Functions are introduced: they are out-of-band integration work (sync, extraction, webhook ingress, timer indexing), Bicep-deployable, share App Insights correlation, and use Managed Identity + Key Vault~~
- ~~No Durable Functions packages in solution~~
<!-- /adr052-drift:allow -->

> Superseded by the ADR-052 review checklist.

---

## Amendment A1 (2026-09-12): workload placement moves to ADR-052

> **Status**: Accepted (resolution path **B — amendment**, root CLAUDE.md §6.5; owner decision 2026-09-12).
> **Driver**: `unified-access-control-r2` task 102.
> **Evidence**: [`projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md`](../../projects/unified-access-control-r2/notes/decisions/workload-placement-policy-evaluation.md).

### Why

By 2026-09 four incompatible placement rules existed, and each decided the host from the trigger. This ADR's
original caution guarded one thing — fragmenting the BFF runtime. That still holds for user-facing endpoints. It
never justified forbidding Functions for background or event work.

### What is superseded (→ ADR-052)

<!-- adr052-drift:allow reason="A1's superseded-provisions table quotes the withdrawn rules" -->
| Provision | Now governed by |
|---|---|
| Azure Functions permissions and criteria, operational requirements | ADR-052 §2–§6 |
| "No Durable Functions" | ADR-052 §7 — Durable Task permitted in its own host, never inside the BFF |
| Design documents naming only "App Service (Minimal API + Workers)" | Design documents name the host chosen under ADR-052 |
<!-- /adr052-drift:allow -->

### What is refined

"BackgroundService + Service Bus for BFF-coupled async work" now reads: inside the BFF, the mechanism follows the
trigger — queue/topic → ADR-004; schedule → ADR-036. Where the work runs at all is ADR-052's decision.

### What is unchanged

A single BFF runtime on App Service; Minimal API for every BFF endpoint; **no BFF endpoints in Azure Functions**;
one middleware pipeline; ProblemDetails; `/healthz`; endpoint-level resource authorization (ADR-008). The ArchTest
`ADR001_MinimalApiTests` still bans Functions **and Durable Task** packages, and Function-attributed methods,
**inside the BFF assembly**.
