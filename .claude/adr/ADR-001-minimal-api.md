# ADR-001: Minimal API + BackgroundService (Concise)

> **Status**: Accepted
> **Domain**: API/BFF Architecture
> **Last Updated**: 2025-12-18

---

## Decision

Run SDAP on a **single ASP.NET Core App Service** using:
- **Minimal API** for synchronous HTTP endpoints
- **BackgroundService** workers for async jobs (Service Bus)
- **No Azure Functions** or Durable Functions

**Rationale**: Single runtime eliminates duplicate cross-cutting concerns (auth, retries, correlation), simplifies debugging, and ensures predictable performance.

---

## Constraints

### ✅ MUST

- **MUST** use Minimal API for all HTTP endpoints
- **MUST** use BackgroundService + Service Bus for async work
- **MUST** register all services in single `Program.cs` middleware pipeline
- **MUST** return `ProblemDetails` for all API errors
- **MUST** expose `/healthz` endpoint for health checks

### ❌ MUST NOT

- **MUST NOT** introduce Azure Functions projects or packages
- **MUST NOT** use Durable Functions for orchestrations
- **MUST NOT** create separate function app hosts
- **MUST NOT** use Functions bindings or triggers

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

### BackgroundService Worker

```csharp
// Service Bus processor in BackgroundService
public class DocumentWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _processor.StartProcessingAsync(ct);
    }
}
```

**See**: [Background Worker Pattern](../patterns/api/background-workers.md) for complete examples

### Anti-Pattern: Azure Functions

```csharp
// ❌ DON'T: Create Azure Functions
[FunctionName("Process")]
public async Task Run([ServiceBusTrigger("queue")] string msg) { }

// ✅ DO: Use BackgroundService (see pattern file)
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint filters for authorization (not global middleware) |
| [ADR-010](ADR-010-di-minimalism.md) | Limit DI registrations to ≤15 non-framework services |
| [ADR-017](ADR-017-bff-resiliency.md) | Use Polly for resilience in workers and HTTP clients |
| [ADR-021](ADR-021-configuration.md) | Configuration management in single runtime |

---

## When to Reference This ADR

**Load this ADR when**:
- Creating new API endpoints
- Implementing async background jobs
- Setting up new services or workers
- Reviewing architecture for Functions usage

**Related AI Context**:
- [API Constraints](../constraints/api.md) - Full MUST/MUST NOT rules
- [Endpoint Definition Pattern](../patterns/api/endpoint-definition.md) - Code examples
- [Service Registration Pattern](../patterns/api/service-registration.md) - DI examples

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-001-minimal-api-and-workers.md](../../docs/adr/ADR-001-minimal-api-and-workers.md)

For detailed context including:
- Historical alternatives considered
- Detailed consequences analysis
- Success metrics and compliance checklist
- Exception scenarios requiring addendum

---

**Lines**: 118 (target: 100-150)
**Pattern Files**: Code examples maintained in `patterns/api/*.md` (single source of truth)
**Optimized for**: Quick reference during API/worker development
