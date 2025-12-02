# Task 02: Add Global Exception Handler

**Task ID**: `02-GlobalExceptionHandler`
**Estimated Time**: 20 minutes
**Status**: Not Started
**Dependencies**: 01-TASK-SdapProblemException

---

## 📋 Prompt

Add a global exception handler to `Program.cs` that converts exceptions into RFC 7807 Problem Details JSON responses with correlation IDs. This provides consistent error handling across all endpoints.

---

## ✅ Todos

- [ ] Open `src/api/Spe.Bff.Api/Program.cs`
- [ ] Add `using Spe.Bff.Api.Infrastructure.Exceptions;`
- [ ] Add `using Microsoft.Identity.Client;`
- [ ] Add global exception handler after `app.UseHttpsRedirection();`
- [ ] Map SdapProblemException → structured response
- [ ] Map MsalServiceException → 401 Unauthorized
- [ ] Map ServiceException (Graph API) → appropriate status code
- [ ] Include correlation ID from `HttpContext.TraceIdentifier`
- [ ] Build and verify no compilation errors

---

## 📚 Required Knowledge

### ASP.NET Core Exception Handling Middleware
The `UseExceptionHandler` middleware catches unhandled exceptions and allows custom responses:

```csharp
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        // Handle exception and write response
    });
});
```

### Exception Mapping Strategy
1. **SdapProblemException** → Use exception properties (Code, Title, Detail, StatusCode)
2. **MsalServiceException** → 401 Unauthorized (OBO token acquisition failed)
3. **ServiceException** (Graph API) → Use ResponseStatusCode from Graph SDK
4. **All Others** → 500 Internal Server Error (unexpected errors)

### Correlation ID
Use `HttpContext.TraceIdentifier` as correlation ID for tracing errors across logs.

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Program.cs](../../../src/api/Spe.Bff.Api/Program.cs)

**Dependencies**:
- `SdapProblemException` (created in Task 01)
- `Microsoft.Identity.Client.MsalServiceException` (from MSAL.NET)
- `Microsoft.Graph.Models.ODataErrors.ServiceException` (from Graph SDK)

---

## 🎯 Implementation

### Location in Program.cs

Add the exception handler **after** `app.UseHttpsRedirection();` and **before** endpoint mappings:

```csharp
app.UseHttpsRedirection();

// ============================================================================
// GLOBAL EXCEPTION HANDLER - RFC 7807 Problem Details
// ============================================================================
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = ctx.TraceIdentifier;

        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        // Map exception to Problem Details
        var (status, code, title, detail) = exception switch
        {
            // SDAP validation/business logic errors
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail),

            // MSAL OBO token acquisition failures
            MsalServiceException ms => (
                401,
                "obo_failed",
                "OBO Token Acquisition Failed",
                $"Failed to exchange user token for Graph API token: {ms.Message}"
            ),

            // Graph API errors
            Microsoft.Graph.Models.ODataErrors.ServiceException gs when gs.ResponseStatusCode > 0 => (
                gs.ResponseStatusCode,
                "graph_error",
                "Graph API Error",
                gs.Message
            ),

            // Unexpected errors
            _ => (
                500,
                "server_error",
                "Internal Server Error",
                "An unexpected error occurred. Please check correlation ID in logs."
            )
        };

        // Log the error with correlation ID
        logger.LogError(exception,
            "Request failed with {StatusCode} {Code}: {Detail} | CorrelationId: {CorrelationId}",
            status, code, detail, traceId);

        // Return RFC 7807 Problem Details response
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://spaarke.com/errors/{code}",
            title,
            detail,
            status,
            extensions = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = traceId
            }
        });
    });
});

app.UseAuthorization();

// Endpoint mappings...
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] No compiler warnings about missing usings

### Code Quality
- [ ] Handler is placed after `UseHttpsRedirection()`
- [ ] Handler is placed before `UseAuthorization()`
- [ ] All exception types are properly mapped
- [ ] Correlation ID is included in response
- [ ] Error is logged with correlation ID

### Testing
- [ ] Build solution: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
- [ ] Run locally and trigger an error (e.g., invalid document ID)
- [ ] Verify response has `application/problem+json` content type
- [ ] Verify response includes `correlationId` in extensions

### Example Response
```json
{
  "type": "https://spaarke.com/errors/invalid_id",
  "title": "Invalid Document ID",
  "detail": "Document ID 'abc' is not a valid GUID format",
  "status": 400,
  "extensions": {
    "code": "invalid_id",
    "correlationId": "0HN7GKQJ5K3QR:00000001"
  }
}
```

---

## 📝 Notes

- The `type` field uses a custom URL format (not required to be a real URL per RFC 7807)
- The `extensions` dictionary allows adding custom fields like `correlationId` and `code`
- MSAL errors (OBO failures) return 401 to indicate authentication issues
- Graph API errors preserve the original status code (403 Forbidden, 404 Not Found, etc.)
- Unexpected errors (500) intentionally hide details from the user but log full exception

---

## 🚨 Important

After adding this handler, **remove any try-catch blocks** in endpoints that return generic error messages. Let exceptions bubble up to the global handler for consistent error responses.

---

**Previous Task**: [01-TASK-SdapProblemException.md](./01-TASK-SdapProblemException.md)
**Next Task**: [03-TASK-IGraphClientFactory.md](./03-TASK-IGraphClientFactory.md)
