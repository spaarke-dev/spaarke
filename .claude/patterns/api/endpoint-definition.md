# Endpoint Definition Pattern

> **Domain**: BFF API / Minimal API
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-001, ADR-008

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | AI streaming endpoints |
| `src/server/api/Sprk.Bff.Api/Api/Documents/DocumentsEndpoints.cs` | Container CRUD |
| `src/server/api/Sprk.Bff.Api/Api/Documents/UploadEndpoints.cs` | File uploads |
| `src/server/api/Sprk.Bff.Api/Api/Documents/FileAccessEndpoints.cs` | Preview/download |

---

## Pattern Structure

```csharp
public static class {Feature}Endpoints
{
    public static IEndpointRouteBuilder Map{Feature}Endpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/{feature}")
            .RequireAuthorization()
            .WithTags("{Feature}");

        group.MapPost("/action", HandlerMethod)
            .AddEndpointFilter<AuthorizationFilter>()
            .RequireRateLimiting("policy-name")
            .WithName("ActionName")
            .WithSummary("Brief description")
            .Produces<ResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        return app;
    }

    private static async Task<IResult> HandlerMethod(
        RequestDto request,
        SpeFileStore speFileStore,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // Implementation
    }
}
```

---

## Key Conventions

### Extension Method Pattern
- One static class per feature area
- Extension method returns `IEndpointRouteBuilder` for chaining
- Called from `Program.cs`: `app.Map{Feature}Endpoints();`

### MapGroup for Shared Configuration
```csharp
var group = app.MapGroup("/api/containers")
    .RequireAuthorization()           // All endpoints require auth
    .WithTags("Containers");          // OpenAPI grouping
```

### Handler Method Signature
```csharp
private static async Task<IResult> GetContainer(
    Guid id,                              // Route parameter
    [FromQuery] bool? includeDeleted,     // Query parameter
    SpeFileStore speFileStore,            // DI injection
    ILogger<Program> logger,              // Logging
    HttpContext context,                  // For TraceIdentifier
    CancellationToken ct)                 // Cancellation
```

### Response Types
```csharp
return TypedResults.Ok(result);                    // 200
return TypedResults.Created($"/api/x/{id}", dto);  // 201
return TypedResults.NoContent();                   // 204
return TypedResults.NotFound();                    // 404
return ProblemDetailsHelper.ValidationError(msg); // 400
```

---

## Registration in Program.cs

```csharp
// src/server/api/Sprk.Bff.Api/Program.cs (lines 898-941)

app.MapUserEndpoints();
app.MapPermissionsEndpoints();
app.MapNavMapEndpoints();
app.MapDataverseDocumentsEndpoints();
app.MapFileAccessEndpoints();
app.MapDocumentsEndpoints();
app.MapUploadEndpoints();
app.MapOBOEndpoints();

// Conditional registration
if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled"))
{
    app.MapDocumentIntelligenceEndpoints();
}
```

---

## Fluent Configuration Reference

| Method | Purpose |
|--------|---------|
| `.RequireAuthorization()` | Require authenticated user |
| `.RequireAuthorization("policy")` | Require specific policy |
| `.RequireRateLimiting("policy")` | Apply rate limit policy |
| `.AddEndpointFilter<T>()` | Add authorization filter |
| `.WithName("name")` | Operation ID for OpenAPI |
| `.WithTags("tag")` | OpenAPI grouping |
| `.WithSummary("text")` | Short description |
| `.Produces<T>(status)` | Document success response |
| `.ProducesProblem(status)` | Document error response |

---

## Related Patterns

- [Endpoint Filters](endpoint-filters.md) - Authorization filter implementation
- [Error Handling](error-handling.md) - ProblemDetails responses
- [Service Registration](service-registration.md) - DI configuration

---

**Lines**: ~115

