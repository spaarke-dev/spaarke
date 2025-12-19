# Error Handling Pattern

> **Domain**: BFF API / Error Responses
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-019

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Errors/ProblemDetailsHelper.cs` | Helper methods |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Exceptions/SdapProblemException.cs` | Custom exception |
| `src/server/api/Sprk.Bff.Api/Program.cs` (lines 789-851) | Global exception handler |

---

## Pattern Structure

### ProblemDetails Helper

```csharp
public static class ProblemDetailsHelper
{
    // Validation errors
    public static IResult ValidationProblem(Dictionary<string, string[]> errors)
        => Results.ValidationProblem(errors);

    public static IResult ValidationError(string detail)
        => Results.Problem(title: "Validation Error", statusCode: 400, detail: detail);

    // Authorization errors
    public static IResult Forbidden(string reasonCode)
        => Results.Problem(
            title: "Forbidden",
            statusCode: 403,
            detail: "Access denied",
            extensions: new Dictionary<string, object?> { ["reasonCode"] = reasonCode });

    // Graph API errors
    public static IResult FromGraphException(ODataError ex)
    {
        var status = ex.ResponseStatusCode > 0 ? ex.ResponseStatusCode : 500;
        return Results.Problem(
            title: status == 403 ? "forbidden" : "error",
            detail: ex.Error?.Message ?? ex.Message,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["graphErrorCode"] = ex.Error?.Code,
                ["graphRequestId"] = ExtractRequestId(ex)
            });
    }

    // AI service errors
    public static IResult AiUnavailable(string reason, string? correlationId = null);
    public static IResult AiRateLimited(int? retryAfterSeconds = null);
}
```

### Custom Exception Type

```csharp
public sealed class SdapProblemException : Exception
{
    public string Code { get; }        // stable error code
    public string Title { get; }       // short description
    public string? Detail { get; }     // detailed message
    public int StatusCode { get; }     // HTTP status
    public Dictionary<string, object>? Extensions { get; }

    public SdapProblemException(
        string code,
        string title,
        string? detail = null,
        int statusCode = 400,
        Dictionary<string, object>? extensions = null) { }
}
```

---

## Usage in Endpoints

```csharp
app.MapGet("/api/containers", async (...) =>
{
    try
    {
        // Validation
        if (!containerTypeId.HasValue)
        {
            return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
            {
                ["containerTypeId"] = ["containerTypeId is required"]
            });
        }

        var result = await speFileStore.ListContainersAsync(containerTypeId.Value);
        return TypedResults.Ok(result);
    }
    catch (ODataError ex)
    {
        logger.LogError(ex, "Failed to list containers");
        return ProblemDetailsHelper.FromGraphException(ex);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error listing containers");
        return TypedResults.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "An unexpected error occurred",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
    }
});
```

---

## Global Exception Handler

```csharp
// Program.cs (lines 789-851)
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = ctx.TraceIdentifier;

        (int status, string code, string title, string detail) = exception switch
        {
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail ?? sp.Message),
            MsalServiceException => (401, "obo_failed", "OBO Token Acquisition Failed", "..."),
            ODataError gs => ((int?)gs.ResponseStatusCode ?? 500, "graph_error", "Graph API Error", "..."),
            _ => (500, "server_error", "Internal Server Error", "...")
        };

        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://spaarke.com/errors/{code}",
            title,
            detail,
            status,
            extensions = new { code, correlationId = traceId }
        });
    });
});
```

---

## Error Code Conventions

| Domain | Code Pattern | Example |
|--------|--------------|---------|
| Validation | `invalid_{field}` | `invalid_container_type_id` |
| Authorization | `sdap.access.deny.{reason}` | `sdap.access.deny.team_mismatch` |
| Graph | `graph_error` | `graph_error` |
| OBO | `obo_failed` | `obo_failed` |
| AI | `ai_{type}` | `ai_unavailable`, `ai_rate_limited` |
| Server | `server_error` | `server_error` |

---

## RFC 7807 Response Format

```json
{
  "type": "https://spaarke.com/errors/invalid_id",
  "title": "Validation Error",
  "detail": "The provided ID is not a valid GUID",
  "status": 400,
  "correlationId": "abc123",
  "code": "invalid_id"
}
```

---

## Related Patterns

- [Endpoint Definition](endpoint-definition.md) - Where errors are returned
- [Endpoint Filters](endpoint-filters.md) - Authorization errors

---

**Lines**: ~145

