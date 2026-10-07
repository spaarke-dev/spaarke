# ADR-001: Minimal API BFF Runtime (Concise)

> **Status**: Accepted, as amended (A1, 2026-09-12)
> **Domain**: API/BFF Architecture
> **Last Updated**: 2026-09-12 (Amendment A1 — workload placement moved to [ADR-052](ADR-052-workload-placement.md))

---

## Decision

Run the **BFF on a single ASP.NET Core App Service**:
- **Minimal API** for all synchronous HTTP endpoints
- **One middleware pipeline** for cross-cutting concerns (ProblemDetails, correlation/telemetry, security headers,
  rate limiting); resource authorization stays endpoint-level (ADR-008)
- **Background, scheduled and event-driven work**: *where* it runs → [ADR-052](ADR-052-workload-placement.md);
  *how* it runs inside the BFF → [ADR-004](ADR-004-job-contract.md) (queue) / [ADR-036](ADR-036-background-job-infrastructure.md) (schedule)

**Rationale**: A single BFF runtime avoids duplicated cross-cutting concerns (auth, retries, correlation), keeps
BFF debugging simple and latency predictable. That concern is about user-facing endpoints; it does not decide where
background work runs (Amendment A1).

---

## Constraints

### ✅ MUST

- **MUST** use Minimal API for all BFF HTTP endpoints
- **MUST** register BFF services in the single `Program.cs` middleware pipeline (via feature modules — ADR-010)
- **MUST** return `ProblemDetails` for all BFF API errors
- **MUST** expose `/healthz` endpoint for health checks

### ❌ MUST NOT

- **MUST NOT** host BFF endpoints in Azure Functions
- **MUST NOT** duplicate BFF auth, correlation, or ProblemDetails infrastructure outside the BFF
- **MUST NOT** put Azure Functions or Durable Task packages, or Function-attributed methods, inside `Sprk.Bff.Api`
  (enforced by `ADR001_MinimalApiTests`)

---

## Implementation Patterns

### Minimal API Endpoint

```csharp
// Minimal API with endpoint filter
app.MapGet("/api/documents/{id}", (string id, DocumentService svc) =>
    svc.GetDocumentAsync(id))
    .AddEndpointFilter<DocumentAuthorizationFilter>();
```

**See**: [Endpoint Definition Pattern](../patterns/api/endpoint-definition.md) for complete examples

### Background work inside the BFF

Queue/topic message → `IJobHandler` via `ServiceBusJobProcessor` (ADR-004) · schedule → `IScheduledJob` on
`ScheduledJobHost` (ADR-036). No new hand-rolled timer `BackgroundService` (ADR-052 §1).

**See**: [Background Worker Pattern](../patterns/api/background-workers.md)

### Anti-Pattern: Functions hosting BFF endpoints

```csharp
// ❌ DON'T: host BFF endpoints in a Function
[Function("GetDocument")]
public async Task<IActionResult> Run([HttpTrigger] HttpRequest req) { }

// ✅ DO: BFF endpoints in Minimal API
app.MapGet("/api/documents/{id}", ...);
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-052](ADR-052-workload-placement.md) | Where background, scheduled and event-driven work runs (supersedes this ADR's Functions provisions) |
| [ADR-004](ADR-004-job-contract.md) | Queue-driven work inside the BFF |
| [ADR-036](ADR-036-background-job-infrastructure.md) | Schedule-driven work inside the BFF |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint filters for authorization (not global middleware) |
| [ADR-010](ADR-010-di-minimalism.md) | Limit DI registrations to ≤15 non-framework services |
| [ADR-017](ADR-017-bff-resiliency.md) | Use Polly for resilience in workers and HTTP clients |
| [ADR-021](ADR-021-configuration.md) | Configuration management in single runtime |

---

## When to Reference This ADR

**Load this ADR when**:
- Creating new API endpoints
- Setting up new services in the BFF pipeline
- Reviewing whether something belongs in the BFF runtime (for background work, load **ADR-052** too)

**Related AI Context**:
- [API Constraints](../constraints/api.md) - Full MUST/MUST NOT rules
- [Endpoint Definition Pattern](../patterns/api/endpoint-definition.md) - Code examples
- [Service Registration Pattern](../patterns/api/service-registration.md) - DI examples

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-001-minimal-api-and-workers.md](../../docs/adr/ADR-001-minimal-api-and-workers.md) —
includes Amendment A1 and the superseded original text, kept as history.
