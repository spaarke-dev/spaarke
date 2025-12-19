# API/BFF Constraints

> **Domain**: BFF API Development
> **Source ADRs**: ADR-001, ADR-008, ADR-010, ADR-019
> **Last Updated**: 2025-12-18

---

## When to Load This File

Load when:
- Creating new API endpoints
- Implementing BackgroundService workers
- Registering services in DI
- Implementing error handling
- Reviewing API code

---

## MUST Rules

### Architecture (ADR-001)

- ✅ **MUST** use Minimal API for all HTTP endpoints
- ✅ **MUST** use BackgroundService + Service Bus for async work
- ✅ **MUST** register all services in single `Program.cs` middleware pipeline
- ✅ **MUST** expose `/healthz` endpoint for health checks

### Authorization (ADR-008)

- ✅ **MUST** use endpoint filters for resource authorization
- ✅ **MUST** call `.RequireAuthorization()` on protected route groups
- ✅ **MUST** add explicit authorization filter for resource checks
- ✅ **MUST** keep global middleware limited to cross-cutting concerns only

### Dependency Injection (ADR-010)

- ✅ **MUST** register concretes by default (not interfaces)
- ✅ **MUST** use feature module extensions (`AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule`)
- ✅ **MUST** use Options pattern with `ValidateOnStart()`
- ✅ **MUST** keep DI registrations ≤15 non-framework lines
- ✅ **MUST** use single typed `HttpClient` per upstream service

### Error Handling (ADR-019)

- ✅ **MUST** return ProblemDetails (RFC 7807) for all HTTP failures
- ✅ **MUST** include stable `errorCode` extension (especially for AI endpoints)
- ✅ **MUST** include correlation ID in errors and logs
- ✅ **MUST** map upstream failures to consistent status codes (429, 503, 500)
- ✅ **MUST** emit terminal error event for SSE failures

---

## MUST NOT Rules

### Architecture (ADR-001)

- ❌ **MUST NOT** introduce Azure Functions projects or packages
- ❌ **MUST NOT** use Durable Functions for orchestrations
- ❌ **MUST NOT** create separate function app hosts
- ❌ **MUST NOT** use Functions bindings or triggers

### Authorization (ADR-008)

- ❌ **MUST NOT** create global middleware for resource authorization
- ❌ **MUST NOT** perform resource checks before routing completes
- ❌ **MUST NOT** create "god middlewares" that handle multiple concerns

### Dependency Injection (ADR-010)

- ❌ **MUST NOT** create interfaces without genuine seam requirement
- ❌ **MUST NOT** inject `GraphServiceClient` directly into endpoints
- ❌ **MUST NOT** register duplicate HttpClients
- ❌ **MUST NOT** inline registrations (use feature modules)

### Error Handling (ADR-019)

- ❌ **MUST NOT** leak document content, prompts, or model output in errors
- ❌ **MUST NOT** invent custom error shapes per endpoint
- ❌ **MUST NOT** silently drop SSE connections without terminal event

---

## Quick Reference Patterns

### Endpoint Definition

```csharp
var documents = app.MapGroup("/api/documents")
    .RequireAuthorization();

documents.MapGet("/{id}", GetDocument)
    .AddEndpointFilter<DocumentAuthorizationFilter>();
```

### Service Registration

```csharp
builder.Services
    .AddSpaarkeCore(builder.Configuration)
    .AddDocumentsModule()
    .AddWorkersModule();
```

### Error Response

```csharp
return ProblemDetailsHelper.AiUnavailable(
    correlationId: context.TraceIdentifier,
    errorCode: "AI_SERVICE_UNAVAILABLE");
```

---

## Pattern Files (Complete Examples)

- [Endpoint Definition](../patterns/api/endpoint-definition.md)
- [Endpoint Filters](../patterns/api/endpoint-filters.md)
- [Service Registration](../patterns/api/service-registration.md)
- [Error Handling](../patterns/api/error-handling.md)
- [Background Workers](../patterns/api/background-workers.md)

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-001](../adr/ADR-001-minimal-api.md) | Runtime architecture | Decisions about hosting, workers |
| [ADR-008](../adr/ADR-008-endpoint-filters.md) | Authorization model | Auth implementation decisions |
| [ADR-010](../adr/ADR-010-di-minimalism.md) | DI architecture | Seam justification, module structure |
| [ADR-019](../adr/ADR-019-problemdetails.md) | Error handling | Error strategy, SSE events |

---

**Lines**: ~130
**Purpose**: Single-file reference for all API development constraints
