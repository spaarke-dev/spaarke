# ADR-019: ProblemDetails & Error Handling (Concise)

> **Status**: Proposed
> **Domain**: API Error Handling
> **Last Updated**: 2025-12-18

---

## Decision

Use **RFC 7807 ProblemDetails** for all API errors. Include stable error codes and correlation IDs. For SSE, emit terminal error events.

**Rationale**: Consistent error shapes reduce client complexity, improve debuggability, and prevent information leakage.

---

## Constraints

### ✅ MUST

- **MUST** return ProblemDetails for all HTTP failures
- **MUST** include stable `errorCode` extension (especially for AI endpoints)
- **MUST** include correlation ID in errors and logs
- **MUST** map upstream failures to consistent status codes (429, 503, 500)
- **MUST** emit terminal error event for SSE failures

### ❌ MUST NOT

- **MUST NOT** leak document content, prompts, or model output in errors
- **MUST NOT** invent custom error shapes per endpoint
- **MUST NOT** silently drop SSE connections without terminal event

---

## Implementation Patterns

### ProblemDetails Response

```csharp
// Use helper for consistency
return ProblemDetailsHelper.AiUnavailable(
    correlationId: context.TraceIdentifier,
    errorCode: "AI_SERVICE_UNAVAILABLE");

// Or manual construction
return Results.Problem(
    statusCode: 503,
    title: "Service Unavailable",
    extensions: new Dictionary<string, object?>
    {
        ["errorCode"] = "AI_SERVICE_UNAVAILABLE",
        ["correlationId"] = context.TraceIdentifier
    });
```

**See**: [Error Handling Pattern](../patterns/api/error-handling.md)

### SSE Terminal Error

```csharp
// Emit error event before closing
await writer.WriteAsync($"event: error\n");
await writer.WriteAsync($"data: {JsonSerializer.Serialize(new {
    type = "error",
    done = true,
    errorCode = "AI_RATE_LIMITED",
    message = "Rate limit exceeded",
    correlationId = traceId
})}\n\n");
```

**See**: [SSE Error Pattern](../patterns/api/sse-errors.md)

### Status Code Mapping

| Upstream Failure | HTTP Status | Error Code |
|------------------|-------------|------------|
| Rate limit (429) | 429 | `UPSTREAM_RATE_LIMITED` |
| Service down | 503 | `UPSTREAM_UNAVAILABLE` |
| Timeout | 504 | `UPSTREAM_TIMEOUT` |
| Unknown | 500 | `INTERNAL_ERROR` |

---

## Anti-Patterns

```csharp
// ❌ DON'T: Custom error shape
return Results.Json(new { error = "Something went wrong" });

// ❌ DON'T: Leak content in errors
return Results.Problem(detail: $"Failed processing: {documentContent}");

// ❌ DON'T: Missing correlation
return Results.Problem(title: "Error");

// ✅ DO: ProblemDetails with correlation
return ProblemDetailsHelper.InternalError(context.TraceIdentifier);
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Single exception handling middleware |
| [ADR-013](ADR-013-ai-architecture.md) | AI endpoint error codes |
| [ADR-015](ADR-015-ai-governance.md) | No content in error messages |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-019-api-errors-and-problemdetails.md](../../docs/adr/ADR-019-api-errors-and-problemdetails.md)

For detailed context including:
- SSE error event structure
- Failure mode analysis
- Compliance checklist

---

**Lines**: ~100
**Pattern Files**: Error patterns in `patterns/api/error-handling.md`
