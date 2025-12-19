# ADR-001: Standardize on Minimal API + BackgroundService; do not use Azure Functions

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
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

SDAP requires a single, predictable runtime for synchronous request/response and asynchronous jobs. Mixing Azure Functions with ASP.NET Core introduces duplicated cross-cutting concerns (auth, retries, correlation, ProblemDetails), inconsistent identity flows, cold-start variance, and split debugging/deployment pipelines.

## Decision

| Rule | Description |
|------|-------------|
| **Single runtime** | Run SDAP on a single ASP.NET Core App Service |
| **Minimal API** | Use Minimal API for all synchronous endpoints |
| **BackgroundService** | Use BackgroundService workers subscribed to Azure Service Bus for async jobs |
| **No Functions** | Do not use Azure Functions or Durable Functions |

## Consequences

**Positive:**
- One middleware pipeline for authentication, authorization, correlation, retry policies, and ProblemDetails
- Simplified local development and deployment, easier debugging and observability
- Predictable performance and instance warmup behavior

**Negative:**
- Loss of Functions bindings ergonomics; explicit Service Bus SDK usage and schedulers required
- Responsibility to implement durable, idempotent job handling rests with us

## Alternatives Considered

| Alternative | Rejection Reason |
|-------------|------------------|
| Azure Functions (HTTP/Service Bus triggers) | Duplicate cross-cutting concerns, operational complexity |
| Durable Functions | Cognitive load, lock-in, host fragmentation |

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
- New asynchronous capabilities: enqueue ADR-004 `JobContract` jobs and process them in `BackgroundService` workers.
- Do not introduce Azure Functions/Durable Functions packages, attributes, or separate hosts.

## Exceptions

| Exception | Requirement |
|-----------|-------------|
| Extreme timer density | ADR addendum + runtime review |
| Multi-day orchestrations | Cannot be modeled as queued steps; ADR addendum required |

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
- [ ] No Azure Functions projects in solution
- [ ] All async work uses BackgroundService + Service Bus
- [ ] Endpoints defined in Minimal API style
- [ ] Single middleware pipeline for cross-cutting concerns (no duplicate auth/retry stacks across separate hosts)
- [ ] Resource authorization implemented at the endpoint (ADR-008)
