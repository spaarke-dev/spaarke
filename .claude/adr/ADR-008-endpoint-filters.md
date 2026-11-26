# ADR-008: Endpoint Filters for Authorization (Concise)

> **Status**: Accepted
> **Domain**: API Authorization
> **Last Updated**: 2026-11-25 (Amendment A1)

---

## Decision

Use **endpoint filters** for resource-based authorization, not global middleware.

**Rationale**: Global middleware runs before routing, lacks access to route values (e.g., `documentId`) and request body. Endpoint filters have full context and sit where authorization logic belongs—next to the endpoint.

---

## Constraints

### ✅ MUST

- **MUST** use endpoint filters for resource authorization of every resource the request names (Amendment A1: a resource only the service's own lookup identifies is checked in the service, under A1's seven conditions)
- **MUST** call `.RequireAuthorization()` on protected route groups
- **MUST** add explicit authorization filter for resource checks
- **MUST** keep global middleware limited to cross-cutting concerns only

### ❌ MUST NOT

- **MUST NOT** create global middleware for resource authorization
- **MUST NOT** perform resource checks before routing completes
- **MUST NOT** create "god middlewares" that handle multiple concerns

---

## Implementation Patterns

### Endpoint Filter

```csharp
// Protected route group
var documents = app.MapGroup("/api/documents")
    .RequireAuthorization();  // Base auth check

// Resource authorization via filter
documents.MapGet("/{id}", GetDocument)
    .AddEndpointFilter<DocumentAuthorizationFilter>();

documents.MapPut("/{id}", UpdateDocument)
    .AddEndpointFilter<DocumentAuthorizationFilter>(Operation.Write);
```

**See**: [Endpoint Filters Pattern](../patterns/api/endpoint-filters.md)

### Authorization Filter Implementation

```csharp
public class DocumentAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var documentId = context.GetArgument<string>(0);
        var authorized = await _authService.AuthorizeAsync(documentId);

        return authorized ? await next(context) : Results.Forbid();
    }
}
```

**See**: [Endpoint Filter Pattern](../patterns/api/endpoint-filters.md)

### Anti-Pattern: Global Middleware

```csharp
// ❌ DON'T: Global resource authorization
app.UseMiddleware<DocumentSecurityMiddleware>();
// Problem: Runs before routing, no documentId available

// ✅ DO: Endpoint filter (see above)
```

---

### Amendment A1 (2026-11-25): a resource the service finds
When the request does not name the resource and only a lookup inside the service identifies it (for example an
idempotency match or a message-id match), check it in the service, where it is found. Do not repeat the lookup in
a filter: two lookups can disagree. Conditions: run as the caller; run before any content read or write (a job
row counts as a write); check the exact id the action then uses; fail closed; use the shared check
(`AuthorizeDocumentsAsync` for documents, `CallerRecordAccessProbe` for other records with a system-user caller,
the external evaluator for a contact); test the deny and fault paths; name it in the route's ledger entry.
Full text: [ADR-008 A1](../../docs/adr/ADR-008-authorization-endpoint-filters.md#amendment-a1-2026-11-25-a-resource-the-service-finds-is-checked-where-it-is-found).

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Single middleware pipeline for cross-cutting only |
| [ADR-003](ADR-003-authorization-seams.md) | Authorization service and rules pattern |
| [ADR-019](ADR-019-problemdetails.md) | Return ProblemDetails on 401/403 |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Named API key schemes (`BuilderAdminApiKey`, `RagApiKey`), HMAC webhook validation, and audit-logging middleware are all realized as endpoint filters under this ADR |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-008-authorization-endpoint-filters.md](../../docs/adr/ADR-008-authorization-endpoint-filters.md)

For detailed context including:
- Authorization patterns for different scenarios
- Middleware pipeline ordering
- Bulk/list endpoint authorization strategies
- Success metrics

---

**Lines**: ~85
**Pattern Files**: Authorization patterns in `patterns/api/endpoint-filters.md` and `patterns/auth/*.md`
**Optimized for**: Quick reference during endpoint creation and security reviews
