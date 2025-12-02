# Task 1.1: Authorization Implementation - Enable Real Access Control

**Priority:** CRITICAL (Sprint 3, Phase 1)
**Estimated Effort:** 5-8 days
**Status:** 🔴 BLOCKS PRODUCTION
**Dependencies:** Dataverse Web API must be functional

---

## Context & Problem Statement

The authorization system is currently **completely disabled**, creating a critical security vulnerability:

1. **All policies use placeholder logic**: `RequireAssertion(_ => true)` in [Program.cs:27-29](../../../src/api/Spe.Bff.Api/Program.cs#L27-L29)
2. **DataverseAccessDataSource returns AccessLevel.None**: [DataverseAccessDataSource.cs:23](../../../src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs#L23) - stub implementation
3. **AuthorizationService never sees real data**: The rule chain processes empty/none access levels
4. **Users are effectively all-powerful**: Any authenticated user can access any resource

This violates fundamental security principles and blocks production deployment.

---

## Goals & Outcomes

### Primary Goals
1. Implement real Dataverse queries in `DataverseAccessDataSource`
2. Replace placeholder authorization policies with real access checks
3. Ensure `AuthorizationService` evaluates actual user permissions
4. Add comprehensive integration tests for authorization flow

### Success Criteria
- [ ] `DataverseAccessDataSource` queries Dataverse for real user access data
- [ ] Authorization policies enforce actual access control rules
- [ ] Integration tests validate policy enforcement (including denials)
- [ ] All existing endpoints respect authorization (no bypasses)
- [ ] Performance: Authorization checks complete in < 200ms (with caching)
- [ ] Audit logging captures all authorization decisions

### Non-Goals
- Custom tenant-specific policies (Sprint 4+)
- Role-based access control beyond basic team membership (Sprint 4+)
- Link token authorization (Sprint 4+)

---

## Architecture & Design

### Current State (Sprint 2)
```
┌─────────────┐
│  Endpoint   │
└──────┬──────┘
       │
       v
┌──────────────────────┐
│ [Authorize("policy")]│ ← RequireAssertion(_ => true)
└──────┬───────────────┘
       │
       v
┌──────────────────────────┐
│ AuthorizationService     │
└──────┬───────────────────┘
       │
       v
┌──────────────────────────┐
│ DataverseAccessDataSource│ ← Returns AccessLevel.None (stub)
└──────────────────────────┘
       │
       v
┌──────────────────────────┐
│ IAuthorizationRule chain │ ← Never denies (no real data)
│ - ExplicitDenyRule       │
│ - ExplicitGrantRule      │
│ - TeamMembershipRule     │
└──────────────────────────┘
```

### Target State (Sprint 3)
```
┌─────────────┐
│  Endpoint   │
└──────┬──────┘
       │
       v
┌──────────────────────┐
│ [Authorize("policy")]│ ← Real assertion: check AuthorizationService
└──────┬───────────────┘
       │
       v
┌──────────────────────────┐
│ AuthorizationService     │ ← Evaluates rules with real data
└──────┬───────────────────┘
       │
       v
┌──────────────────────────┐
│ DataverseAccessDataSource│ ← Queries Dataverse Web API
│ - GetUserAccessAsync     │ ← Returns real AccessSnapshot
└──────┬───────────────────┘
       │
       v
┌──────────────────────────┐
│ Dataverse Web API        │
│ - Query user permissions │
│ - Query team memberships │
│ - Query roles            │
└──────────────────────────┘
       │
       v
┌──────────────────────────┐
│ IAuthorizationRule chain │ ← Evaluates with real AccessSnapshot
│ - ExplicitDenyRule       │ ← Can deny based on Dataverse data
│ - ExplicitGrantRule      │ ← Grants based on permissions
│ - TeamMembershipRule     │ ← Checks team membership
└──────────────────────────┘
```

---

## Relevant ADRs

### ADR-003: Lean Authorization Seams
- **Key Principle**: One UAC seam (`IAccessDataSource`), concrete `AuthorizationService`
- **Rule Design**: Small, ordered `IAuthorizationRule` policies
- **Caching Strategy**: Per-request snapshots via `RequestCache`
- **Deny Reasons**: Stable, machine-readable codes (e.g., `sdap.access.deny.team_mismatch`)

### ADR-010: DI Minimalism
- **Keep Seams**: `IAccessDataSource` and `IEnumerable<IAuthorizationRule>`
- **Register Concretes**: `AuthorizationService` registered as concrete, not interface
- **Feature Modules**: Use `AddSpaarkeCore()` for authorization DI

---

## Implementation Steps

### Step 1: Implement Real Dataverse Queries

**File:** `src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`

**Current Code:**
```csharp
public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
{
    // Placeholder implementation
    var snapshot = new AccessSnapshot
    {
        UserId = userId,
        ResourceId = resourceId,
        AccessLevel = AccessLevel.None,  // ❌ Always returns None
        TeamMemberships = Array.Empty<string>(),
        Roles = Array.Empty<string>()
    };
    return Task.FromResult(snapshot);
}
```

**Required Changes:**

```csharp
public async Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
{
    _logger.LogInformation("Fetching access data for user {UserId} on resource {ResourceId}", userId, resourceId);

    try
    {
        // Query Dataverse for user permissions on the resource
        // Use DataverseWebApiService or inject IDataverseService

        // 1. Query explicit permissions (sdap_documentpermission table)
        var permissions = await QueryUserPermissionsAsync(userId, resourceId, ct);

        // 2. Query team memberships
        var teams = await QueryUserTeamMembershipsAsync(userId, ct);

        // 3. Query roles (if applicable)
        var roles = await QueryUserRolesAsync(userId, ct);

        // 4. Determine access level based on permissions
        var accessLevel = DetermineAccessLevel(permissions, teams, resourceId);

        var snapshot = new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = accessLevel,
            TeamMemberships = teams.ToArray(),
            Roles = roles.ToArray()
        };

        _logger.LogDebug("Access snapshot retrieved: {AccessLevel} for user {UserId}", snapshot.AccessLevel, userId);
        return snapshot;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to fetch access data for user {UserId} on resource {ResourceId}", userId, resourceId);

        // Fail-safe: Return None on errors (fail-closed security)
        return new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = AccessLevel.None,
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        };
    }
}

private async Task<List<PermissionRecord>> QueryUserPermissionsAsync(string userId, string resourceId, CancellationToken ct)
{
    // TODO: Implement Dataverse Web API query
    // FetchXML or OData query to sdap_documentpermission table
    // Filter by userid and resourceid
    throw new NotImplementedException("Query Dataverse for permissions");
}

private async Task<List<string>> QueryUserTeamMembershipsAsync(string userId, CancellationToken ct)
{
    // TODO: Implement Dataverse Web API query
    // Query teammembership table for user's teams
    throw new NotImplementedException("Query Dataverse for team memberships");
}

private async Task<List<string>> QueryUserRolesAsync(string userId, CancellationToken ct)
{
    // TODO: Implement Dataverse Web API query
    // Query security roles for user
    throw new NotImplementedException("Query Dataverse for roles");
}

private AccessLevel DetermineAccessLevel(List<PermissionRecord> permissions, List<string> teams, string resourceId)
{
    // Business logic to determine access level
    // Consider direct permissions and team-based permissions

    if (permissions.Any(p => p.AccessLevel == AccessLevel.FullControl))
        return AccessLevel.FullControl;

    if (permissions.Any(p => p.AccessLevel == AccessLevel.Write))
        return AccessLevel.Write;

    if (permissions.Any(p => p.AccessLevel == AccessLevel.Read))
        return AccessLevel.Read;

    return AccessLevel.None;
}

// Helper class for permission records
private record PermissionRecord(string UserId, string ResourceId, AccessLevel AccessLevel);
```

**AI Coding Prompt:**
```
Implement real Dataverse queries in DataverseAccessDataSource.GetUserAccessAsync:

Context:
- File: src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs
- Current implementation returns stub data (AccessLevel.None)
- Need to query Dataverse Web API for real user permissions

Requirements:
1. Inject IDataverseService (already registered in DI)
2. Query sdap_documentpermission table for user's explicit permissions on the resource
3. Query teammembership and team tables for user's team memberships
4. Query security roles if needed
5. Implement fail-closed security: return AccessLevel.None on errors
6. Use structured logging with LogInformation/LogError
7. Handle exceptions gracefully
8. Return AccessSnapshot with real data

Code Quality:
- Senior C# developer standards
- Use async/await properly
- No catch-all exception handlers without logging
- Use ArgumentNullException.ThrowIfNull for parameters
- Follow ADR-010 DI minimalism (inject only what's needed)

Testing:
- Should be unit testable by mocking IDataverseService
- Add XML doc comments for public methods
```

---

### Step 2: Replace Placeholder Authorization Policies

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Current Code:**
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canmanagecontainers", p => p.RequireAssertion(_ => true)); // TODO
    options.AddPolicy("canwritefiles", p => p.RequireAssertion(_ => true)); // TODO
});
```

**Required Changes:**
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canmanagecontainers", p =>
        p.RequireAssertion(context =>
        {
            // Get AuthorizationService from DI
            var authService = context.Resource as HttpContext;
            if (authService == null) return false;

            var userId = context.User.FindFirst("oid")?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return false;

            // Extract resourceId from route/query
            var resourceId = authService.Request.RouteValues["containerId"]?.ToString();
            if (string.IsNullOrEmpty(resourceId)) return false;

            // Call AuthorizationService (injected)
            var authSvc = authService.RequestServices.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>();
            var result = authSvc.CheckAccessAsync(userId, resourceId, AccessLevel.FullControl, CancellationToken.None).GetAwaiter().GetResult();

            return result.IsAllowed;
        }));

    options.AddPolicy("canwritefiles", p =>
        p.RequireAssertion(context =>
        {
            var authService = context.Resource as HttpContext;
            if (authService == null) return false;

            var userId = context.User.FindFirst("oid")?.Value
                      ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return false;

            var resourceId = authService.Request.RouteValues["containerId"]?.ToString()
                          ?? authService.Request.RouteValues["driveId"]?.ToString();
            if (string.IsNullOrEmpty(resourceId)) return false;

            var authSvc = authService.RequestServices.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>();
            var result = authSvc.CheckAccessAsync(userId, resourceId, AccessLevel.Write, CancellationToken.None).GetAwaiter().GetResult();

            return result.IsAllowed;
        }));
});
```

**IMPORTANT: Senior Developer Note**
The above approach with `GetAwaiter().GetResult()` is a **code smell** but necessary due to `RequireAssertion` being synchronous. A better approach is to use `IAuthorizationHandler`:

**Better Implementation (Create Custom Authorization Handler):**

**New File:** `src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessRequirement.cs`
```csharp
using Microsoft.AspNetCore.Authorization;

namespace Spe.Bff.Api.Infrastructure.Authorization;

public class ResourceAccessRequirement : IAuthorizationRequirement
{
    public AccessLevel RequiredLevel { get; }

    public ResourceAccessRequirement(AccessLevel requiredLevel)
    {
        RequiredLevel = requiredLevel;
    }
}
```

**New File:** `src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs`
```csharp
using Microsoft.AspNetCore.Authorization;
using Spaarke.Core.Auth;
using System.Security.Claims;

namespace Spe.Bff.Api.Infrastructure.Authorization;

public class ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
{
    private readonly Spaarke.Core.Auth.AuthorizationService _authService;
    private readonly ILogger<ResourceAccessHandler> _logger;

    public ResourceAccessHandler(
        Spaarke.Core.Auth.AuthorizationService authService,
        ILogger<ResourceAccessHandler> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceAccessRequirement requirement)
    {
        // Extract user ID from claims
        var userId = context.User.FindFirst("oid")?.Value
                  ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Authorization failed: No user ID found in claims");
            context.Fail();
            return;
        }

        // Extract resource ID from HTTP context
        if (context.Resource is not HttpContext httpContext)
        {
            _logger.LogWarning("Authorization failed: Context.Resource is not HttpContext");
            context.Fail();
            return;
        }

        var resourceId = httpContext.Request.RouteValues["containerId"]?.ToString()
                      ?? httpContext.Request.RouteValues["driveId"]?.ToString()
                      ?? httpContext.Request.RouteValues["documentId"]?.ToString();

        if (string.IsNullOrEmpty(resourceId))
        {
            _logger.LogWarning("Authorization failed: No resource ID found in route");
            context.Fail();
            return;
        }

        // Check access
        var result = await _authService.CheckAccessAsync(
            userId,
            resourceId,
            requirement.RequiredLevel,
            httpContext.RequestAborted);

        if (result.IsAllowed)
        {
            _logger.LogDebug("Authorization succeeded for user {UserId} on resource {ResourceId} with level {Level}",
                userId, resourceId, requirement.RequiredLevel);
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Authorization denied for user {UserId} on resource {ResourceId}: {Reason}",
                userId, resourceId, result.DenyReason);
            context.Fail();
        }
    }
}
```

**Update Program.cs:**
```csharp
// Register authorization handler
builder.Services.AddSingleton<IAuthorizationHandler, ResourceAccessHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("canmanagecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement(AccessLevel.FullControl)));

    options.AddPolicy("canwritefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement(AccessLevel.Write)));

    options.AddPolicy("canreadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement(AccessLevel.Read)));
});
```

**AI Coding Prompt:**
```
Replace placeholder authorization policies with real access control:

Context:
- File: src/api/Spe.Bff.Api/Program.cs
- Current policies use RequireAssertion(_ => true) which allows all access
- Need to integrate with Spaarke.Core.Auth.AuthorizationService

Requirements:
1. Create ResourceAccessRequirement implementing IAuthorizationRequirement
2. Create ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
3. Handler should extract userId from claims (oid or NameIdentifier)
4. Handler should extract resourceId from route values (containerId/driveId/documentId)
5. Call AuthorizationService.CheckAccessAsync with required access level
6. Log authorization decisions (success at Debug, failures at Warning)
7. Update Program.cs to register handler and policies

Code Quality:
- Senior C# developer standards
- Async all the way (no GetAwaiter().GetResult())
- Comprehensive error handling with logging
- Use ArgumentNullException.ThrowIfNull
- Follow ADR-003 authorization patterns

Files to Create:
- src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessRequirement.cs
- src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs

Files to Modify:
- src/api/Spe.Bff.Api/Program.cs
```

---

### Step 3: Add Integration Tests

**New File:** `tests/Spe.Bff.Api.Tests/Integration/AuthorizationIntegrationTests.cs`

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace Spe.Bff.Api.Tests.Integration;

public class AuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetContainer_WithoutAuthorization_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/containers/test-container-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContainer_WithInsufficientPermissions_Returns403()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock IAccessDataSource to return AccessLevel.None
                services.AddScoped<IAccessDataSource, MockAccessDataSourceNoAccess>();
            });
        }).CreateClient();

        var token = GenerateMockJwt("user-123");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers/test-container-id");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetContainer_WithSufficientPermissions_Returns200()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock IAccessDataSource to return AccessLevel.Read
                services.AddScoped<IAccessDataSource, MockAccessDataSourceReadAccess>();
            });
        }).CreateClient();

        var token = GenerateMockJwt("user-123");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/containers/test-container-id");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData(AccessLevel.None, HttpStatusCode.Forbidden)]
    [InlineData(AccessLevel.Read, HttpStatusCode.Forbidden)]
    [InlineData(AccessLevel.Write, HttpStatusCode.OK)]
    [InlineData(AccessLevel.FullControl, HttpStatusCode.OK)]
    public async Task WriteOperation_RespectsAccessLevels(AccessLevel userLevel, HttpStatusCode expectedStatus)
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IAccessDataSource>(sp => new MockAccessDataSource(userLevel));
            });
        }).CreateClient();

        var token = GenerateMockJwt("user-123");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.PostAsync("/api/containers/test-container-id/files",
            new StringContent("{\"name\":\"test.txt\"}", System.Text.Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private string GenerateMockJwt(string userId)
    {
        // TODO: Generate a valid mock JWT with oid claim
        // Use System.IdentityModel.Tokens.Jwt or test helpers
        throw new NotImplementedException();
    }
}

// Mock implementations
internal class MockAccessDataSourceNoAccess : IAccessDataSource
{
    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = AccessLevel.None,
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        });
    }
}

internal class MockAccessDataSourceReadAccess : IAccessDataSource
{
    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = AccessLevel.Read,
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        });
    }
}

internal class MockAccessDataSource : IAccessDataSource
{
    private readonly AccessLevel _level;

    public MockAccessDataSource(AccessLevel level) => _level = level;

    public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, CancellationToken ct = default)
    {
        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessLevel = _level,
            TeamMemberships = Array.Empty<string>(),
            Roles = Array.Empty<string>()
        });
    }
}
```

**AI Coding Prompt:**
```
Create integration tests for authorization flow:

Context:
- Test authorization policies enforce access control
- Use WebApplicationFactory for integration tests
- Mock IAccessDataSource to control access levels

Requirements:
1. Test unauthorized requests (401)
2. Test insufficient permissions (403)
3. Test successful access with proper permissions (200)
4. Test different access levels (None, Read, Write, FullControl)
5. Use Theory tests for parameterized testing
6. Mock JWT token generation for authenticated requests

Code Quality:
- Senior C# developer standards
- Use xUnit best practices
- Arrange-Act-Assert pattern
- Clear test names that describe behavior
- Mock only at seam boundaries (IAccessDataSource)

Files to Create:
- tests/Spe.Bff.Api.Tests/Integration/AuthorizationIntegrationTests.cs

Dependencies:
- Microsoft.AspNetCore.Mvc.Testing
- xUnit
```

---

### Step 4: Add Audit Logging

**File:** `src/shared/Spaarke.Core/Auth/AuthorizationService.cs` (modify existing)

Add audit logging to `CheckAccessAsync`:

```csharp
public async Task<AuthorizationResult> CheckAccessAsync(
    string userId,
    string resourceId,
    AccessLevel requiredLevel,
    CancellationToken ct = default)
{
    using var activity = Activity.Current?.StartActivity("AuthorizationCheck");
    activity?.SetTag("userId", userId);
    activity?.SetTag("resourceId", resourceId);
    activity?.SetTag("requiredLevel", requiredLevel);

    var stopwatch = Stopwatch.StartNew();

    try
    {
        // Existing authorization logic...
        var result = await PerformAuthorizationCheck(userId, resourceId, requiredLevel, ct);

        stopwatch.Stop();
        activity?.SetTag("result", result.IsAllowed);
        activity?.SetTag("durationMs", stopwatch.ElapsedMilliseconds);

        // Audit log
        if (result.IsAllowed)
        {
            _logger.LogInformation(
                "Authorization GRANTED: User {UserId} granted {AccessLevel} on {ResourceId} ({DurationMs}ms)",
                userId, requiredLevel, resourceId, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogWarning(
                "Authorization DENIED: User {UserId} denied {AccessLevel} on {ResourceId} - Reason: {Reason} ({DurationMs}ms)",
                userId, requiredLevel, resourceId, result.DenyReason, stopwatch.ElapsedMilliseconds);
        }

        return result;
    }
    catch (Exception ex)
    {
        stopwatch.Stop();
        _logger.LogError(ex,
            "Authorization ERROR: Failed to check access for user {UserId} on {ResourceId} ({DurationMs}ms)",
            userId, resourceId, stopwatch.ElapsedMilliseconds);

        // Fail-closed: Deny on errors
        return AuthorizationResult.Deny("sdap.access.error.system_failure");
    }
}
```

---

## Testing Strategy

### Unit Tests
1. **DataverseAccessDataSource**:
   - Mock `IDataverseService`
   - Test various permission scenarios
   - Test error handling (fail-closed)
   - Test team membership logic

2. **Authorization Rules**:
   - Test each `IAuthorizationRule` in isolation
   - Test rule ordering
   - Test deny reasons are correct

3. **ResourceAccessHandler**:
   - Mock `AuthorizationService`
   - Test claim extraction
   - Test route value extraction
   - Test authorization success/failure paths

### Integration Tests
1. **End-to-End Authorization**:
   - Test with real endpoints
   - Mock only `IAccessDataSource` (seam boundary)
   - Test 401, 403, 200 responses
   - Test different access levels

2. **Performance Tests**:
   - Measure authorization check latency
   - Verify caching works (< 200ms with cache)
   - Test under load

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] `DataverseAccessDataSource` queries Dataverse (no stubs)
- [ ] All authorization policies removed `RequireAssertion(_ => true)`
- [ ] `ResourceAccessHandler` is registered and working
- [ ] Integration tests pass (401, 403, 200 scenarios)
- [ ] Unit tests for all authorization components
- [ ] Audit logging captures all authorization decisions
- [ ] Performance: < 200ms authorization checks (with caching)
- [ ] No `TODO` comments remain in authorization code
- [ ] Code review by senior developer
- [ ] Manual testing with real Dataverse data

---

## Rollout & Risk Mitigation

### Feature Flag Strategy
1. Add feature flag: `Authorization:Enabled` (default: `false`)
2. Keep placeholder policies as fallback
3. Gradual rollout:
   - Week 1: Enable for dev environment only
   - Week 2: Enable for staging with monitoring
   - Week 3: Enable for production after validation

### Monitoring & Alerts
1. **Metrics to Track**:
   - Authorization check duration (P50, P95, P99)
   - Authorization denial rate (by endpoint)
   - Authorization errors (should be near zero)
   - Cache hit rate for `RequestCache`

2. **Alerts**:
   - Alert if authorization check P95 > 500ms
   - Alert if error rate > 1%
   - Alert if denial rate spikes unexpectedly

### Rollback Plan
If issues arise:
1. Set `Authorization:Enabled = false`
2. Revert to placeholder policies temporarily
3. Investigate and fix issues
4. Re-enable after validation

---

## Knowledge & References

### Dataverse Table Schema
Expected tables for authorization:
- `sdap_documentpermission`: Explicit user/resource permissions
- `teammembership`: User-to-team mappings
- `team`: Team definitions
- `systemuser`: User information
- `role`: Security roles

### Sample Dataverse Query (FetchXML)
```xml
<fetch>
  <entity name="sdap_documentpermission">
    <attribute name="sdap_accesslevel" />
    <filter>
      <condition attribute="sdap_userid" operator="eq" value="{userId}" />
      <condition attribute="sdap_resourceid" operator="eq" value="{resourceId}" />
    </filter>
  </entity>
</fetch>
```

### Sample Dataverse Query (OData)
```
GET /api/data/v9.2/sdap_documentpermissions?$filter=sdap_userid eq '{userId}' and sdap_resourceid eq '{resourceId}'&$select=sdap_accesslevel
```

### AccessLevel Enum
```csharp
public enum AccessLevel
{
    None = 0,
    Read = 1,
    Write = 2,
    FullControl = 3
}
```

---

## Senior Developer Notes

### Code Quality Standards
1. **No blocking calls**: Never use `.Result` or `GetAwaiter().GetResult()` in async paths
2. **Fail-closed security**: Always deny access on errors/exceptions
3. **Comprehensive logging**: Log all authorization decisions for audit trail
4. **Performance**: Cache per-request to avoid chatty Dataverse queries
5. **Testability**: Mock only at seam boundaries (`IAccessDataSource`)

### Common Pitfalls to Avoid
1. ❌ Don't use `RequireAssertion` with async code (leads to deadlocks)
2. ❌ Don't return `AccessLevel.None` silently - log why
3. ❌ Don't cache authorization results beyond request scope
4. ❌ Don't expose Dataverse query errors to clients (fail-closed)
5. ❌ Don't skip audit logging for "allow" decisions

### Performance Optimization
1. Use `RequestCache` for per-request memoization
2. Batch Dataverse queries where possible
3. Consider adding distributed cache (Redis) for user permissions (5-min TTL)
4. Measure and optimize query performance

---

## Completion Criteria

Task is complete when:
1. All code changes implemented and reviewed
2. All tests passing (unit + integration)
3. Performance validated (< 200ms P95)
4. Code review approved by senior developer
5. Manual testing with real Dataverse data successful
6. Documentation updated
7. Feature flag configured for gradual rollout

**Estimated Completion: 5-8 days** (can be split between 2 developers)
