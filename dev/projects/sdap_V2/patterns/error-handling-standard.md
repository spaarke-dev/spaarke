# Pattern: Standard Error Handling

**Use For**: Handling Graph API errors in endpoints
**Task**: Consistent error responses across all endpoints
**Time**: 5 minutes

---

## Quick Copy-Paste: Full Error Handler

```csharp
try
{
    var result = await service.PerformOperationAsync(resourceId);
    return Results.Ok(result);
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    logger.LogWarning("Resource not found: {ResourceId}, Message: {Message}",
        resourceId, ex.Message);

    return Results.Problem(
        title: "Resource Not Found",
        detail: $"The requested resource '{resourceId}' was not found",
        statusCode: 404,
        instance: $"/api/resources/{resourceId}");
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
{
    logger.LogWarning("Access denied to resource: {ResourceId}, Message: {Message}",
        resourceId, ex.Message);

    return Results.Problem(
        title: "Access Denied",
        detail: "You do not have permission to access this resource",
        statusCode: 403);
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
{
    var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);

    logger.LogWarning("Throttled by Graph API, retry after {RetryAfter}s",
        retryAfter.TotalSeconds);

    return Results.StatusCode(429);
    // Note: Set Retry-After header in middleware
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
    logger.LogWarning("Conflict detected: {Message}", ex.Message);

    return Results.Problem(
        title: "Conflict",
        detail: "The operation conflicts with the current state of the resource",
        statusCode: 409);
}
catch (OperationCanceledException)
{
    logger.LogInformation("Operation cancelled by client");
    return Results.StatusCode(499); // Client Closed Request
}
catch (Exception ex)
{
    logger.LogError(ex, "Unexpected error in operation for resource {ResourceId}", resourceId);

    return Results.Problem(
        title: "Internal Server Error",
        detail: "An unexpected error occurred. Please try again later.",
        statusCode: 500);
}
```

---

## ServiceException Status Code Map

| Status Code | When? | Response | Log Level |
|-------------|-------|----------|-----------|
| **404** Not Found | Container/file doesn't exist | `Results.Problem(404)` | Warning |
| **403** Forbidden | No permission to container | `Results.Problem(403)` | Warning |
| **429** Too Many Requests | Graph API throttling | `Results.StatusCode(429)` | Warning |
| **409** Conflict | Version mismatch, duplicate | `Results.Problem(409)` | Warning |
| **401** Unauthorized | Invalid/expired token | `Results.Unauthorized()` | Warning |
| **400** Bad Request | Validation failure | `Results.BadRequest()` | Warning |
| **500** Internal Server Error | Unexpected exception | `Results.Problem(500)` | Error |
| **499** Client Closed | User cancelled request | `Results.StatusCode(499)` | Info |

---

## Quick Copy-Paste: Minimal Error Handler

For simple endpoints where you only care about common errors:

```csharp
try
{
    var result = await service.PerformOperationAsync(resourceId);
    return Results.Ok(result);
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    logger.LogWarning("Resource not found: {ResourceId}", resourceId);
    return Results.NotFound(new { error = "Resource not found" });
}
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
{
    logger.LogWarning("Access denied: {ResourceId}", resourceId);
    return Results.Problem("Access denied", statusCode: 403);
}
catch (Exception ex)
{
    logger.LogError(ex, "Operation failed");
    return Results.Problem("Operation failed", statusCode: 500);
}
```

---

## Logging Best Practices

### Log Level Guidelines

```csharp
// Debug - Detailed flow (only in dev)
logger.LogDebug("Starting operation for {ResourceId}", resourceId);

// Information - Normal operations
logger.LogInformation("File uploaded: {ItemId}", result.ItemId);

// Warning - Expected errors (user/client issue)
logger.LogWarning("Resource not found: {ResourceId}", resourceId);
logger.LogWarning("Access denied: {UserId} to {ResourceId}", userId, resourceId);

// Error - Unexpected errors (system/code issue)
logger.LogError(ex, "Operation failed for {ResourceId}", resourceId);
logger.LogError(ex, "Failed to connect to Dataverse");
```

### Structured Logging

**✅ DO** - Use structured parameters:
```csharp
logger.LogInformation("User {UserId} uploaded {FileName} to {ContainerId}",
    userId, fileName, containerId);
```

**❌ DON'T** - Use string interpolation:
```csharp
logger.LogInformation($"User {userId} uploaded {fileName} to {containerId}");
```

---

## Custom Exception Types (Optional)

```csharp
namespace Spe.Bff.Api.Exceptions;

public class SdapException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public SdapException(string message, string errorCode, int statusCode = 500)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

public class ResourceNotFoundException : SdapException
{
    public ResourceNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} '{resourceId}' not found", "RESOURCE_NOT_FOUND", 404)
    {
        Data["ResourceType"] = resourceType;
        Data["ResourceId"] = resourceId;
    }
}
```

---

## Checklist

- [ ] All ServiceException status codes handled
- [ ] OperationCanceledException caught (499 response)
- [ ] Generic Exception catch-all at end
- [ ] Appropriate log levels (Warning for expected, Error for unexpected)
- [ ] Structured logging with parameters
- [ ] Problem Details format for errors
- [ ] No sensitive data in error messages

---

## Common Mistakes

**❌ Wrong Order**:
```csharp
catch (Exception ex) // This catches everything!
{
    return Results.Problem();
}
catch (ServiceException ex) // Never reached
{
    return Results.NotFound();
}
```

**✅ Correct Order**:
```csharp
catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    return Results.NotFound();
}
catch (Exception ex) // Catch-all at end
{
    return Results.Problem();
}
```

---

## Related Files

- Apply to: All endpoint handlers in `src/api/Spe.Bff.Api/Api/`
- Uses: `Microsoft.Graph.ServiceException`
- Returns: `IResult` with appropriate status codes
