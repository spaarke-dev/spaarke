# ADR-001: Minimal API + BackgroundService (Concise)

> **Status**: Accepted
> **Domain**: API/BFF Architecture
> **Last Updated**: 2026-05-19

---

## Decision

Run the **BFF on a single ASP.NET Core App Service** using:
- **Minimal API** for synchronous HTTP endpoints
- **BackgroundService** workers for async jobs tied to BFF business logic (Service Bus)
- **Azure Functions PERMITTED** for narrowly-scoped out-of-band integration work (Dataverse → AI Search sync, scheduled indexers, webhook receivers, event-triggered extraction)
- **No Durable Functions** (use Service Bus + state machine instead)

**Rationale**: A single BFF runtime eliminates duplicate cross-cutting concerns (auth, retries, correlation) within the BFF, simplifies BFF debugging, and ensures predictable performance. Out-of-band integration workloads (event-driven sync, timer-driven indexing) are legitimately independent of the BFF and belong in Functions when the trigger semantics or lifecycle independence justify it.

---

## Constraints

### ✅ MUST

- **MUST** use Minimal API for all BFF HTTP endpoints (no Functions hosting BFF endpoints)
- **MUST** use BackgroundService + Service Bus for BFF-coupled async work
- **MUST** register BFF services in single `Program.cs` middleware pipeline
- **MUST** return `ProblemDetails` for all BFF API errors
- **MUST** expose `/healthz` endpoint for health checks
- **MUST** (when using Functions) deploy them via Bicep alongside the BFF, share App Insights correlation, and use Managed Identity + Key Vault

### ❌ MUST NOT

- **MUST NOT** host BFF endpoints in Azure Functions
- **MUST NOT** duplicate BFF auth, correlation, or ProblemDetails infrastructure inside a Function
- **MUST NOT** use Durable Functions for orchestrations
- **MUST NOT** let Functions grow into a shadow BFF — keep them narrowly scoped to out-of-band integration work

### ✅ Functions are appropriate for

- Dataverse → AI Search sync (event-driven + scheduled reconciliation)
- Closure-extraction and indexing pipelines triggered by events
- Webhook receivers from external systems
- Timer-driven indexers and reconciliation jobs
- Anything where the trigger semantics (event grid, blob, webhook, timer) don't fit BackgroundService ergonomically

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

### Anti-Pattern: Functions hosting BFF endpoints

```csharp
// ❌ DON'T: host BFF endpoints in a Function
[FunctionName("GetDocument")]
public async Task<IActionResult> Run([HttpTrigger] HttpRequest req) { }

// ✅ DO: BFF endpoints in Minimal API
app.MapGet("/api/documents/{id}", ...);
```

### Acceptable Pattern: Functions for out-of-band integration

```csharp
// ✅ OK: Dataverse change event → AI Search sync (out-of-band, event-driven)
[FunctionName("SyncMatterToIndex")]
public async Task Run([ServiceBusTrigger("dataverse-changes")] DataverseChangeEvent evt)
{
    // Independent of BFF request pipeline; deployed via same Bicep package;
    // shares App Insights correlation; uses Managed Identity + Key Vault.
}
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
