# ADR-001: Minimal API + BackgroundService as the BFF runtime; Azure Functions permitted for narrowly-scoped out-of-band work

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2026-05-19 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-001 Concise](../../.claude/adr/ADR-001-minimal-api.md) - ~145 lines, decision + constraints + patterns
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules for API development
- [Endpoint Definition Pattern](../../.claude/patterns/api/endpoint-definition.md) - Minimal API code examples
- [Service Registration Pattern](../../.claude/patterns/api/service-registration.md) - DI registration examples

**When to load this full ADR**: Historical context, alternatives analysis, detailed consequences

---

## Context

SDAP requires a single, predictable runtime for synchronous request/response and asynchronous jobs tied to BFF business logic. The original concern (2025-09) was that fragmenting the BFF across Azure Functions and ASP.NET Core duplicates cross-cutting concerns (auth, retries, correlation, ProblemDetails), splits identity flows, introduces cold-start variance, and divides debugging/deployment.

That concern remains valid for **the BFF runtime itself**. It does not extend to **out-of-band integration workloads** (e.g., Dataverse → AI Search sync, scheduled indexers, event-triggered extraction pipelines, third-party webhook receivers) that are genuinely independent of the BFF request pipeline. For those, Azure Functions are the right tool — they're event-driven, scale independently, Bicep-deployable per tenant, and don't share any of the cross-cutting concerns the original concern was about.

## Decision

| Rule | Description |
|------|-------------|
| **Single BFF runtime** | Run the BFF on a single ASP.NET Core App Service |
| **Minimal API** | Use Minimal API for all synchronous BFF endpoints |
| **BackgroundService for BFF-coupled async** | Use BackgroundService workers subscribed to Azure Service Bus for async work tied to BFF business logic |
| **Azure Functions for out-of-band integration** | **Permitted** for workloads that are independent of the BFF request pipeline and meet the criteria below |
| **No Durable Functions** | Continue to avoid Durable Functions; if multi-step orchestration is needed, use Service Bus + state machine |

### When Azure Functions ARE appropriate

- Workload is genuinely independent of the BFF request pipeline (no shared auth context, no shared correlation flow)
- Trigger semantics that BackgroundService can't elegantly express (timer-driven indexing, webhook receivers, event grid / blob triggers, Dataverse change-feed sync)
- Lifecycle is independent of the BFF (sync can run while BFF is down for maintenance; failure of one doesn't cascade to the other)
- Per-tenant deployable as part of the standard Bicep package

### When Azure Functions are NOT appropriate

- Re-implementing endpoints that belong in the BFF
- Duplicating BFF auth, correlation, or ProblemDetails infrastructure
- Splitting a single coherent BFF concern across hosts to "feel modular"
- Anywhere a BackgroundService worker over Service Bus is a clean fit

### Operational requirements when using Functions

- Must be deployable via Bicep alongside the rest of the stack (single per-tenant deployment unit)
- Must publish to Application Insights with correlation IDs that join the BFF's traces
- Must use the same identity/secret-management story (Managed Identity, Key Vault)
- Must be versioned in the same repo and follow the same CI/CD pipeline
- Code must be reviewable as a peer of BFF code, not a parallel runtime with its own conventions

## Consequences

**Positive:**
- One middleware pipeline for BFF cross-cutting concerns (auth, correlation, retry, ProblemDetails) — preserved
- Simpler debugging and observability for the BFF — preserved
- **New**: Event-driven and timer-driven integration work has a natural home (Functions) without contorting BackgroundService for triggers it wasn't designed for
- **New**: Multi-tenant sync and extraction workloads can scale independently of the BFF

**Negative:**
- Two deployable runtimes (BFF + Functions) when out-of-band integration is in use
- Discipline required to keep Functions narrowly scoped and not let them grow into a shadow BFF
- Slightly more complex Bicep + CI/CD pipeline

## Alternatives Considered

| Alternative | Rejection Reason |
|-------------|------------------|
| Azure Functions for BFF endpoints | Original concern stands: duplicates cross-cutting concerns, splits the BFF runtime |
| Durable Functions for orchestration | Cognitive load, lock-in, host fragmentation; Service Bus + state machine remains cleaner |
| BackgroundService for all triggers (including webhooks, event grid, timers) | BackgroundService isn't ergonomic for event-grid or webhook ingress; coupling them to the BFF lifecycle creates fragility |

## Operationalization

| Component | Implementation |
|-----------|----------------|
| Entry point | `Program.cs` registers Minimal API, ServiceBusClient, HttpClient policies, ProblemDetails, BackgroundService workers |
| Workers | `Azure.Messaging.ServiceBus` ServiceBusProcessor with Polly retries and idempotent handlers |
| Health | `/healthz` probe exposed |
| Logging | Application Insights + structured `ILogger` logging (no PII) |
| Docs | Design documents use "App Service (Minimal API + Workers)" not "Function App / Triggers" |

**Clarification:** “Single middleware pipeline” refers to shared cross-cutting concerns (exception handling/`ProblemDetails`, correlation/telemetry, security headers, rate limiting). Resource authorization remains **endpoint-level** (endpoint filters/policies) per ADR-008.

## AI-Directed Coding Guidance

- New synchronous capabilities: add Minimal API endpoints (route groups), return `ProblemDetails` on errors.
- New asynchronous capabilities tied to BFF business logic: enqueue ADR-004 `JobContract` jobs and process them in `BackgroundService` workers.
- New out-of-band integration capabilities (sync, extraction, webhook ingress, timer-driven indexing): Azure Functions are permitted when they meet the criteria in the Decision section. Default to BackgroundService when the choice is genuinely close — only reach for Functions when the trigger or lifecycle independence clearly justifies it.
- Do not introduce Durable Functions packages or attributes — use Service Bus + state machine for multi-step orchestration.
- Do not introduce Functions where the work clearly belongs in the BFF.

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
- [ ] BFF endpoints defined in Minimal API style (no Azure Functions hosting BFF endpoints)
- [ ] BFF-coupled async work uses BackgroundService + Service Bus
- [ ] If Azure Functions are introduced: they are out-of-band integration work (sync, extraction, webhook ingress, timer indexing), Bicep-deployable, share App Insights correlation, and use Managed Identity + Key Vault
- [ ] No Durable Functions packages in solution
- [ ] Single middleware pipeline for BFF cross-cutting concerns (no duplicate auth/retry stacks within the BFF)
- [ ] Resource authorization implemented at the endpoint (ADR-008)
