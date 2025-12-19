# ADR-008: Endpoint Filters for Authorization (Concise)

> **Status**: Accepted
> **Domain**: API Authorization
> **Last Updated**: 2025-12-18

---

## Decision

Use **endpoint filters** for resource-based authorization, not global middleware.

**Rationale**: Global middleware runs before routing, lacks access to route values (e.g., `documentId`) and request body. Endpoint filters have full context and sit where authorization logic belongs—next to the endpoint.

---

## Constraints

### ✅ MUST

- **MUST** use endpoint filters for resource authorization
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

**See**: [Authorization Service Pattern](../patterns/auth/authorization-service.md)

### Anti-Pattern: Global Middleware

```csharp
// ❌ DON'T: Global resource authorization
app.UseMiddleware<DocumentSecurityMiddleware>();
// Problem: Runs before routing, no documentId available

// ✅ DO: Endpoint filter (see above)
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Single middleware pipeline for cross-cutting only |
| [ADR-003](ADR-003-authorization-seams.md) | Authorization service and rules pattern |
| [ADR-019](ADR-019-problemdetails.md) | Return ProblemDetails on 401/403 |

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
