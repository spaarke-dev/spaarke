# Task 4.3: Enable Rate Limiting on All Endpoints

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 1 day (8 hours)
**Status:** Ready for Implementation
**Dependencies:** None

---

## Problem Statement

### Current State (SECURITY RISK)
**All 27 API endpoints are unprotected from abuse** due to disabled rate limiting. The codebase has 20+ TODO comments indicating rate limiting was deferred.

**Evidence:**
```csharp
// Program.cs line 315 - Rate limiting service added but NOT configured
builder.Services.AddRateLimiter(options => { }); // Empty configuration!

// OBOEndpoints.cs - 9 endpoints with TODO comments
// TODO: .RequireRateLimiting("graph-read")
app.MapGet("/api/obo/containers/{containerId}/items", handler);

// DocumentsEndpoints.cs - 6 endpoints unprotected
// TODO: .RequireRateLimiting("graph-write")
app.MapPost("/api/containers/{containerId}/documents", handler);

// UploadEndpoints.cs - 3 endpoints unprotected
// TODO: .RequireRateLimiting("upload-heavy")
app.MapPost("/api/upload/session", handler);
```

**Critical Impact:**
- Denial of Service (DoS) attacks possible
- Graph API quota exhaustion (Microsoft throttles at tenant level)
- Service Bus message flood
- Dataverse API throttling (429 responses affect all users)
- Production deployment vulnerable to abuse

**Affected Endpoints:** 27 total
- OBO operations: 9 endpoints (read-heavy)
- Document operations: 6 endpoints (write-heavy)
- Upload operations: 3 endpoints (bandwidth-heavy)
- Permission operations: 4 endpoints (dataverse-heavy)
- User operations: 5 endpoints (light)

### Target State (SECURE)
Enable ASP.NET Core rate limiting with multiple policies tailored to endpoint resource usage.

---

## Architecture Context

### Rate Limiting Strategies

**1. Fixed Window (Simple, Predictable)**
- Allow N requests per time window
- Window resets at fixed intervals
- Example: 100 requests per minute

**2. Sliding Window (Smooth, Prevents Bursts)**
- Allow N requests per rolling time window
- Prevents burst at window boundary
- Example: 100 requests per 60 seconds (sliding)

**3. Token Bucket (Flexible, Allows Bursts)**
- Bucket has N tokens, refills over time
- Each request consumes 1 token
- Allows bursts up to bucket size
- Example: 100 token bucket, refill 10/second

**4. Concurrency Limiter**
- Limit concurrent requests (not rate)
- Useful for long-running operations
- Example: Max 5 concurrent uploads

**Recommended Approach:** Hybrid strategy with multiple policies

---

### Rate Limiting Policies Design

#### Policy 1: `graph-read` (High Volume)
**Endpoints:** Container listing, file metadata, folder navigation
**Usage Pattern:** Frequent polling, user browsing
**Limits:**
- 100 requests per minute per user
- Sliding window
- HTTP 429 with Retry-After header

**Graph API Context:**
- Microsoft throttles at ~2000 requests/10 seconds per tenant
- Need to leave headroom for other apps in tenant

---

#### Policy 2: `graph-write` (Moderate Volume, Higher Cost)
**Endpoints:** File upload/download, move, copy, delete
**Usage Pattern:** User actions, background jobs
**Limits:**
- 50 requests per minute per user
- Token bucket with burst allowance
- HTTP 429 with Retry-After header

---

#### Policy 3: `dataverse-query` (Conservative, Avoid 429s)
**Endpoints:** Permission checks, document metadata, user access
**Usage Pattern:** High frequency, caching reduces load
**Limits:**
- 200 requests per minute per user (Dataverse is generous)
- Sliding window
- HTTP 429 with Retry-After header

---

#### Policy 4: `upload-heavy` (Concurrency + Rate)
**Endpoints:** Upload session create, chunk upload, finalize
**Usage Pattern:** Long-running, bandwidth-intensive
**Limits:**
- 5 concurrent uploads per user (concurrency limit)
- 20 upload sessions per hour per user (rate limit)
- HTTP 429 with estimated wait time

---

#### Policy 5: `job-submission` (Conservative, Prevent Queue Flood)
**Endpoints:** Document event submission, batch operations
**Usage Pattern:** Background processing, async
**Limits:**
- 10 job submissions per minute per user
- Fixed window
- HTTP 429 with queue depth info

---

#### Policy 6: `anonymous` (Public Endpoints)
**Endpoints:** Health check, OpenAPI docs
**Usage Pattern:** Monitoring, automation
**Limits:**
- 60 requests per minute per IP
- Fixed window

---

## Solution Design

### Step 1: Update Program.cs - Configure Rate Limiter

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Current State (line 315):**
```csharp
builder.Services.AddRateLimiter(options => { }); // Empty!
```

**Replacement:**
```csharp
// ============================================================================
// RATE LIMITING - Protect against abuse and resource exhaustion
// ============================================================================
builder.Services.AddRateLimiter(options =>
{
    // Global settings
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Allow health checks through without rate limiting
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            return RateLimitPartition.GetNoLimiter<string>("healthz");
        }

        // Partition by user ID (authenticated) or IP (anonymous)
        var partitionKey = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown"
            : context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 1000, // Global fallback limit
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0 // No queueing, reject immediately
        });
    });

    // Rejection behavior
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString("0");
        context.HttpContext.Response.Headers.ContentType = "application/problem+json";

        var problemDetails = new
        {
            type = "https://httpstatuses.com/429",
            title = "Too Many Requests",
            status = 429,
            detail = $"Rate limit exceeded. Please retry after {retryAfter} seconds.",
            instance = context.HttpContext.Request.Path.ToString()
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Log rate limit violations
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Rate limit exceeded for {User} on {Path}. Retry after {RetryAfter}s",
            context.HttpContext.User.Identity?.Name ?? "anonymous",
            context.HttpContext.Request.Path,
            retryAfter);
    };

    // Policy: graph-read (High volume read operations)
    options.AddPolicy("graph-read", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 100,
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy: graph-write (Moderate volume write operations)
    options.AddPolicy("graph-write", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 50,
            TokensPerPeriod = 50,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy: dataverse-query (Dataverse API calls)
    options.AddPolicy("dataverse-query", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 200,
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy: upload-heavy (Concurrent + rate limited)
    options.AddPolicy("upload-heavy", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return RateLimitPartition.GetConcurrencyLimiter(userId, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 5, // Max 5 concurrent uploads per user
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10 // Queue up to 10 additional requests
        });
    });

    // Policy: job-submission (Background job processing)
    options.AddPolicy("job-submission", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy: anonymous (Public endpoints by IP)
    options.AddPolicy("anonymous", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 60,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});
```

**Required Using Statements:**
```csharp
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
```

---

### Step 2: Add Rate Limiting Middleware

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Location:** After `app.UseAuthorization()` (around line 248)

**Current:**
```csharp
app.UseAuthentication();
app.UseAuthorization();

// Health check
app.MapHealthChecks("/healthz").AllowAnonymous();
```

**Update To:**
```csharp
app.UseAuthentication();
app.UseAuthorization();

// Rate limiting (after authentication so user ID is available)
app.UseRateLimiter();

// Health check
app.MapHealthChecks("/healthz").AllowAnonymous();
```

---

### Step 3: Apply Rate Limiting to OBO Endpoints

**File:** `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

**Find and Replace Pattern:**
```csharp
// BEFORE (9 occurrences)
.RequireAuthorization("canreadcontainers");
// TODO: .RequireRateLimiting("graph-read")

// AFTER
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");
```

**Specific Endpoints to Update:**

**Line ~25-35 (GET containers):**
```csharp
app.MapGet("/api/obo/containers", async (/*...*/) =>
{
    // ...
})
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");
```

**Line ~50-60 (GET container items):**
```csharp
app.MapGet("/api/obo/containers/{containerId}/items", async (/*...*/) =>
{
    // ...
})
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");
```

**Continue for all 9 OBO endpoints...**

**Summary of OBO Endpoint Rate Limits:**
- List containers → `graph-read`
- Get container items → `graph-read`
- Get item metadata → `graph-read`
- Download file → `graph-read`
- Create container → `graph-write`
- Upload file → `upload-heavy`
- Delete file → `graph-write`
- Move file → `graph-write`
- Copy file → `graph-write`

---

### Step 4: Apply Rate Limiting to Document Endpoints

**File:** `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs`

**Apply to 6 endpoints:**

```csharp
// GET /api/containers/{containerId}/documents (read)
.RequireAuthorization("canreaddocuments")
.RequireRateLimiting("graph-read");

// POST /api/containers/{containerId}/documents (create)
.RequireAuthorization("cancreatedocuments")
.RequireRateLimiting("graph-write");

// GET /api/containers/{containerId}/documents/{documentId} (read single)
.RequireAuthorization("canreaddocuments")
.RequireRateLimiting("graph-read");

// PUT /api/containers/{containerId}/documents/{documentId} (update)
.RequireAuthorization("canupdatedocuments")
.RequireRateLimiting("graph-write");

// DELETE /api/containers/{containerId}/documents/{documentId} (delete)
.RequireAuthorization("candeletedocuments")
.RequireRateLimiting("graph-write");

// POST /api/containers/{containerId}/documents/{documentId}/download (download)
.RequireAuthorization("canreaddocuments")
.RequireRateLimiting("graph-read");
```

---

### Step 5: Apply Rate Limiting to Upload Endpoints

**File:** `src/api/Spe.Bff.Api/Api/UploadEndpoints.cs`

**Apply to 3 endpoints:**

```csharp
// POST /api/upload/session (create upload session)
.RequireAuthorization("canuploadfiles")
.RequireRateLimiting("upload-heavy");

// PUT /api/upload/session/{sessionId}/chunk (upload chunk)
.RequireAuthorization("canuploadfiles")
.RequireRateLimiting("upload-heavy");

// POST /api/upload/session/{sessionId}/finalize (finalize upload)
.RequireAuthorization("canuploadfiles")
.RequireRateLimiting("upload-heavy");
```

---

### Step 6: Apply Rate Limiting to Permission Endpoints

**File:** `src/api/Spe.Bff.Api/Api/PermissionsEndpoints.cs`

**Apply to 4 endpoints:**

```csharp
// GET /api/permissions/documents/{documentId} (single check)
.RequireAuthorization()
.RequireRateLimiting("dataverse-query");

// POST /api/permissions/batch (batch check)
.RequireAuthorization()
.RequireRateLimiting("dataverse-query");

// POST /api/permissions/grant (grant access)
.RequireAuthorization("canmanagepermissions")
.RequireRateLimiting("dataverse-query");

// POST /api/permissions/revoke (revoke access)
.RequireAuthorization("canmanagepermissions")
.RequireRateLimiting("dataverse-query");
```

---

### Step 7: Apply Rate Limiting to User Endpoints

**File:** `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

**Apply to 5 endpoints:**

```csharp
// GET /api/user/profile (light read)
.RequireAuthorization()
.RequireRateLimiting("graph-read");

// GET /api/user/containers (list user containers)
.RequireAuthorization("canreadcontainers")
.RequireRateLimiting("graph-read");

// GET /api/user/recent-documents (query)
.RequireAuthorization()
.RequireRateLimiting("dataverse-query");

// POST /api/user/preferences (update)
.RequireAuthorization()
.RequireRateLimiting("graph-write");

// GET /api/user/activity (query)
.RequireAuthorization()
.RequireRateLimiting("dataverse-query");
```

---

### Step 8: Add Rate Limiting Configuration (Optional Enhancement)

**File:** `src/api/Spe.Bff.Api/Configuration/RateLimitingOptions.cs` (create new)

```csharp
namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for rate limiting policies.
/// </summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Whether rate limiting is enabled. If false, all requests are allowed.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Configuration for graph-read policy (high volume read operations).
    /// </summary>
    public RateLimitPolicy GraphRead { get; init; } = new()
    {
        PermitLimit = 100,
        WindowMinutes = 1
    };

    /// <summary>
    /// Configuration for graph-write policy (moderate volume write operations).
    /// </summary>
    public RateLimitPolicy GraphWrite { get; init; } = new()
    {
        PermitLimit = 50,
        WindowMinutes = 1
    };

    /// <summary>
    /// Configuration for dataverse-query policy.
    /// </summary>
    public RateLimitPolicy DataverseQuery { get; init; } = new()
    {
        PermitLimit = 200,
        WindowMinutes = 1
    };

    /// <summary>
    /// Configuration for upload-heavy policy (concurrency limit).
    /// </summary>
    public RateLimitPolicy UploadHeavy { get; init; } = new()
    {
        PermitLimit = 5, // Concurrent uploads
        WindowMinutes = 0 // N/A for concurrency limiter
    };
}

public sealed class RateLimitPolicy
{
    public int PermitLimit { get; init; }
    public int WindowMinutes { get; init; }
}
```

**Then read from appsettings.json:**
```json
{
  "RateLimiting": {
    "Enabled": true,
    "GraphRead": {
      "PermitLimit": 100,
      "WindowMinutes": 1
    },
    "GraphWrite": {
      "PermitLimit": 50,
      "WindowMinutes": 1
    },
    "DataverseQuery": {
      "PermitLimit": 200,
      "WindowMinutes": 1
    },
    "UploadHeavy": {
      "PermitLimit": 5,
      "WindowMinutes": 0
    }
  }
}
```

---

## Testing Strategy

### Unit Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/RateLimitingTests.cs` (create new)

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExceedRateLimit_Returns429()
    {
        // Arrange
        var client = _factory.CreateClient();
        // Assume test auth returns same user for all requests

        // Act - Hammer endpoint beyond rate limit
        var tasks = Enumerable.Range(0, 150) // Exceed 100 req/min limit
            .Select(_ => client.GetAsync("/api/user/containers"));

        var responses = await Task.WhenAll(tasks);

        // Assert - At least some requests should be rate limited
        var rateLimitedResponses = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(rateLimitedResponses > 0, "Expected some requests to be rate limited");

        // Verify Retry-After header
        var firstRateLimited = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(firstRateLimited.Headers.Contains("Retry-After"), "Expected Retry-After header");
    }

    [Fact]
    public async Task HealthCheck_NotRateLimited()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Hammer health check endpoint
        var tasks = Enumerable.Range(0, 200)
            .Select(_ => client.GetAsync("/healthz"));

        var responses = await Task.WhenAll(tasks);

        // Assert - All should succeed (health check exempt from rate limiting)
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task RateLimitResponse_ContainsProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - Exceed rate limit
        var tasks = Enumerable.Range(0, 150)
            .Select(_ => client.GetAsync("/api/user/containers"));
        var responses = await Task.WhenAll(tasks);

        var rateLimited = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        var content = await rateLimited.Content.ReadAsStringAsync();

        // Assert - Should be ProblemDetails JSON
        Assert.Contains("\"status\": 429", content);
        Assert.Contains("\"title\": \"Too Many Requests\"", content);
        Assert.Contains("Rate limit exceeded", content);
    }
}
```

---

### Integration Tests

**File:** `tests/integration/Spe.Integration.Tests/RateLimitingIntegrationTests.cs`

```csharp
using System.Net;
using Xunit;

namespace Spe.Integration.Tests;

[Collection("Integration")]
public class RateLimitingIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public RateLimitingIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GraphReadEndpoint_RespectsSlidingWindow()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act - Make 100 requests (at limit)
        var responses1 = await Task.WhenAll(
            Enumerable.Range(0, 100).Select(_ => client.GetAsync("/api/user/containers")));

        // Wait 10 seconds (1/6 of window should have passed, ~17 permits available)
        await Task.Delay(TimeSpan.FromSeconds(10));

        // Make 20 more requests
        var responses2 = await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => client.GetAsync("/api/user/containers")));

        // Assert - First 100 should succeed, next 20 should partially succeed
        Assert.All(responses1, r => Assert.NotEqual(HttpStatusCode.TooManyRequests, r.StatusCode));

        var successCount = responses2.Count(r => r.StatusCode != HttpStatusCode.TooManyRequests);
        Assert.InRange(successCount, 15, 20); // Some should succeed due to sliding window
    }

    [Fact]
    public async Task UploadEndpoint_LimitsConcurrency()
    {
        // Arrange
        var client = _fixture.CreateAuthenticatedClient();

        // Act - Start 10 concurrent upload sessions (limit is 5)
        var tasks = Enumerable.Range(0, 10)
            .Select(async i =>
            {
                var response = await client.PostAsync("/api/upload/session",
                    JsonContent.Create(new { fileName = $"test{i}.pdf", fileSize = 1024 }));
                return response;
            })
            .ToList();

        var responses = await Task.WhenAll(tasks);

        // Assert - Some should be rate limited due to concurrency limit
        var rateLimited = responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(rateLimited > 0, "Expected some concurrent requests to be limited");
        Assert.True(rateLimited <= 5, "Expected at most 5 concurrent requests allowed");
    }
}
```

---

### Manual Testing with Apache Bench

**Test graph-read endpoint:**
```bash
# Install Apache Bench (ab)
# Windows: Download from https://www.apachelounge.com/download/

# Test rate limiting (150 requests, 10 concurrent)
ab -n 150 -c 10 -H "Authorization: Bearer YOUR_TOKEN" \
   https://localhost:5001/api/user/containers

# Expected output:
# Requests per second: ~100-120 (some rejected)
# Non-2xx responses: ~50 (429 Too Many Requests)
```

**Test upload-heavy concurrency:**
```bash
# 20 concurrent upload sessions (limit is 5)
ab -n 20 -c 20 -H "Authorization: Bearer YOUR_TOKEN" \
   -p upload.json -T application/json \
   https://localhost:5001/api/upload/session

# Expected: ~15 rejections (429)
```

---

## Validation & Verification

### Success Criteria

✅ **Build & Compilation:**
- [ ] `dotnet build Spaarke.sln` completes with 0 errors
- [ ] No TODO comments for rate limiting remain

✅ **Unit Tests:**
- [ ] Rate limiting tests pass
- [ ] Health check remains exempt from rate limiting

✅ **Integration Tests:**
- [ ] Sliding window rate limiter works correctly
- [ ] Concurrency limiter enforces max concurrent requests

✅ **Functional Tests:**
- [ ] Exceed rate limit → 429 response with Retry-After header
- [ ] Retry-After duration is accurate
- [ ] ProblemDetails JSON returned
- [ ] Logs show rate limit violations

✅ **Production Readiness:**
- [ ] All 27 endpoints have rate limiting applied
- [ ] Application Insights tracks 429 responses
- [ ] Dashboard shows rate limit metrics by policy

---

## Monitoring & Observability

### Application Insights Queries

**Rate Limit Violations by User:**
```kusto
requests
| where resultCode == 429
| extend userId = tostring(customDimensions.userId)
| summarize violations = count() by userId, bin(timestamp, 1h)
| order by violations desc
```

**Rate Limit Violations by Endpoint:**
```kusto
requests
| where resultCode == 429
| summarize violations = count() by name, bin(timestamp, 1h)
| order by violations desc
```

**Top Rate Limited IPs (Anonymous):**
```kusto
requests
| where resultCode == 429
| where isempty(customDimensions.userId)
| summarize violations = count() by client_IP
| order by violations desc
| take 20
```

---

### Alerts

**Alert 1: Excessive Rate Limiting**
```
Metric: requests (resultCode == 429)
Threshold: > 1000 in 5 minutes
Action: Email ops team
```

**Alert 2: Potential DoS Attack**
```
Metric: requests (resultCode == 429) from single IP
Threshold: > 500 in 1 minute
Action: Trigger IP ban, email security team
```

---

## Rollback Plan

**If Rate Limiting Breaks Legitimate Usage:**

1. **Temporary Workaround - Increase Limits:**
   ```csharp
   // Program.cs - Increase permit limits 10x
   PermitLimit = 1000, // Was 100
   ```

2. **Or Disable Rate Limiting Entirely:**
   ```csharp
   // Program.cs - Comment out rate limiter middleware
   // app.UseRateLimiter();
   ```

3. **Restart application**

4. **Monitor:** Watch for increased Graph API / Dataverse throttling

---

## Known Issues & Limitations

### Issue #1: Distributed Rate Limiting
**Current Implementation:** Per-instance rate limiting (not shared across pods)

**Impact:** In multi-instance deployment, each instance tracks limits independently
- 3 instances × 100 req/min = 300 req/min effective limit per user

**Future Solution:** Use Redis-backed distributed rate limiter
```csharp
// TODO (Sprint 5): Implement distributed rate limiting with Redis
options.AddPolicy("graph-read", context =>
{
    return RateLimitPartition.GetRedisSlidingWindowLimiter(userId, redis);
});
```

---

### Issue #2: Rate Limiting vs Circuit Breaker
**Difference:**
- Rate limiting: Protect **your** API from abuse
- Circuit breaker: Protect **downstream** APIs from overload

**Both are needed:**
- Rate limiting on endpoints (this task)
- Circuit breaker in GraphHttpMessageHandler (already exists)

---

## References

### Documentation
- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [Microsoft Graph Throttling](https://learn.microsoft.com/en-us/graph/throttling)
- [Dataverse API Limits](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/api-limits)

### Related ADRs
- None directly, but aligns with production readiness goals

### Related Files
- `src/api/Spe.Bff.Api/Program.cs` (lines 315, endpoint definitions)
- All endpoint files: `Api/OBOEndpoints.cs`, `Api/DocumentsEndpoints.cs`, etc.

---

## AI Implementation Prompt

**Copy this prompt to your AI coding assistant:**

```
Enable rate limiting on all 27 API endpoints to prevent abuse and resource exhaustion.

CONTEXT:
- Rate limiter service added but not configured (Program.cs line 315)
- 20+ TODO comments indicate deferred implementation
- All endpoints vulnerable to DoS attacks

TASKS:
1. Update src/api/Spe.Bff.Api/Program.cs line 315:
   - Configure rate limiter with 6 policies (graph-read, graph-write, dataverse-query, upload-heavy, job-submission, anonymous)
   - Add rejection handler with 429 response and ProblemDetails
   - Use sliding window for reads, token bucket for writes, concurrency for uploads
2. Update src/api/Spe.Bff.Api/Program.cs pipeline:
   - Add app.UseRateLimiter() after UseAuthorization()
3. Update all endpoint files (OBOEndpoints.cs, DocumentsEndpoints.cs, etc.):
   - Remove TODO comments
   - Add .RequireRateLimiting("policy-name") to each endpoint
   - Match policy to endpoint resource usage pattern
4. Add required using statements (System.Threading.RateLimiting)
5. Run dotnet build and verify no errors

VALIDATION:
- Build succeeds with 0 errors
- No TODO comments remain for rate limiting
- Logs show rate limiter configured with 6 policies

Reference the code examples in Steps 1-7 of this task document for specific implementations.
```

---

**Task Owner:** [Assign to developer]
**Reviewer:** [Assign to senior developer/architect]
**Created:** 2025-10-02
**Updated:** 2025-10-02
