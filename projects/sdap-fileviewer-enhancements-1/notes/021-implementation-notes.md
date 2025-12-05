# Task 021 Implementation Notes

## Date: December 4, 2025

## Summary

Updated the `/ping` endpoint to be a lightweight, unauthenticated health check suitable for warm-up agents. Also added a `/status` endpoint for more detailed service information.

## Changes Made

### File: `src/server/api/Spe.Bff.Api/Program.cs`

**Before:**
```csharp
app.MapGet("/ping", (HttpContext context) =>
{
    return TypedResults.Json(new
    {
        service = "Spe.Bff.Api",
        version = "1.0.0",
        environment = app.Environment.EnvironmentName,
        timestamp = DateTimeOffset.UtcNow
    });
});
```

**After:**
```csharp
// Lightweight ping endpoint for warm-up agents (Task 021)
app.MapGet("/ping", () => Results.Ok("pong"))
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Lightweight health check for warm-up agents.");

// Detailed status endpoint with service metadata
app.MapGet("/status", () =>
{
    return TypedResults.Json(new
    {
        service = "Spe.Bff.Api",
        version = "1.0.0",
        timestamp = DateTimeOffset.UtcNow
    });
})
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Service status with metadata (no sensitive info).");
```

## Design Decisions

1. **Simple "pong" response**: The `/ping` endpoint returns just `"pong"` as plain text for maximum simplicity and speed. No JSON overhead.

2. **Removed environment from /status**: The original endpoint exposed `environment` (Development/Production) which could be considered sensitive information that aids attackers in targeting environment-specific vulnerabilities.

3. **Separated concerns**: Split into `/ping` (ultra-lightweight) and `/status` (metadata) to support different use cases.

4. **AllowAnonymous**: Both endpoints are explicitly unauthenticated so external warm-up agents can access them without tokens.

5. **WithTags("Health")**: Grouped under "Health" tag in Swagger for easy discovery.

## Endpoints Summary

| Endpoint | Response | Auth | Purpose |
|----------|----------|------|---------|
| `/ping` | `"pong"` | None | Warm-up agents, quick health check |
| `/status` | JSON with service/version/timestamp | None | Service metadata |
| `/healthz` | ASP.NET Health Check | None | Kubernetes probes, detailed health |

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| GET /ping returns 200 OK with "pong" | ✅ |
| /ping does not require authentication | ✅ |
| Response time < 100ms | ✅ (plain text, no DI) |
| No sensitive information exposed | ✅ (removed environment) |
| Endpoint appears in Swagger under Health tag | ✅ |
| dotnet build succeeds | ✅ |

## Warm-up Agent Configuration

External services can use `/ping` for health monitoring:

### Azure App Service Health Check
Configure in Azure Portal → App Service → Monitoring → Health check:
- Path: `/ping`
- Interval: 5 minutes

### Azure Logic App Timer
Create a Logic App with:
- Trigger: Recurrence (every 5 minutes)
- Action: HTTP GET to `https://<app-url>/ping`

### UptimeRobot / Similar
- URL: `https://<app-url>/ping`
- Expected response: `pong`
- Interval: 5 minutes
