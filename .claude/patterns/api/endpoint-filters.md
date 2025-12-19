# Endpoint Filters Pattern

> **Domain**: BFF API / Authorization
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-008, ADR-003

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` | Resource-level document auth |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs` | Policy-based authorization |

---

## Pattern Structure

### Filter Implementation

```csharp
public class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly string _operation;

    public DocumentAuthorizationFilter(AuthorizationService authService, string operation)
    {
        _authorizationService = authService;
        _operation = operation;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // 1. Extract user ID from claims
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Problem(statusCode: 401, title: "Unauthorized");
        }

        // 2. Extract resource ID from route
        var resourceId = ExtractResourceId(context);
        if (string.IsNullOrEmpty(resourceId))
        {
            return Results.Problem(statusCode: 400, title: "Bad Request");
        }

        // 3. Check authorization
        var authContext = new AuthorizationContext
        {
            UserId = userId,
            ResourceId = resourceId,
            Operation = _operation,
            CorrelationId = httpContext.TraceIdentifier
        };

        var result = await _authorizationService.AuthorizeAsync(authContext);

        if (!result.IsAllowed)
        {
            return ProblemDetailsHelper.Forbidden(result.ReasonCode);
        }

        return await next(context);
    }
}
```

### Extension Method for Easy Application

```csharp
public static class DocumentAuthorizationFilterExtensions
{
    public static TBuilder AddDocumentAuthorizationFilter<TBuilder>(
        this TBuilder builder,
        string operation) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices
                .GetRequiredService<AuthorizationService>();
            var filter = new DocumentAuthorizationFilter(authService, operation);
            return await filter.InvokeAsync(context, next);
        });
    }
}
```

---

## Usage in Endpoints

```csharp
// Using extension method
group.MapGet("/{id}", GetDocument)
    .AddDocumentAuthorizationFilter("document.read");

// Using policy-based authorization
group.MapPost("/", CreateDocument)
    .RequireAuthorization("cancreatedocuments");
```

---

## Policy-Based Authorization (Alternative)

Defined in `Program.cs` (lines 101-204):

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canpreviewfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.preview")));

    options.AddPolicy("candownloadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.download")));

    options.AddPolicy("canmanagecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.manage")));
});
```

---

## When to Use Each Approach

| Approach | Use When |
|----------|----------|
| **Endpoint Filter** | Resource ID in route, custom extraction logic needed |
| **Policy + Handler** | Standard claims-based, reusable across endpoints |

---

## Key Conventions

### Resource ID Extraction Priority
```csharp
private static string? ExtractResourceId(EndpointFilterInvocationContext context)
{
    var routeValues = context.HttpContext.Request.RouteValues;
    return routeValues.TryGetValue("documentId", out var v) ? v?.ToString() :
           routeValues.TryGetValue("containerId", out v) ? v?.ToString() :
           routeValues.TryGetValue("driveId", out v) ? v?.ToString() :
           routeValues.TryGetValue("itemId", out v) ? v?.ToString() :
           null;
}
```

### Deny Code Format
Pattern: `{domain}.{area}.{action}.{reason}`
- `sdap.access.deny.team_mismatch`
- `sdap.access.deny.role_insufficient`

---

## Related Patterns

- [Endpoint Definition](endpoint-definition.md) - Where filters are applied
- [Error Handling](error-handling.md) - ProblemDetails for denials

---

**Lines**: ~125

