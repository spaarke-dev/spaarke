# Phase 2 - Task 5: Simplify Authorization

**Phase**: 2 (Simplify Service Layer)
**Duration**: 1-2 hours
**Risk**: Low
**Patterns**: Use AuthorizationService from Spaarke.Core directly
**Anti-Patterns**: [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)

---

## Current State (Before Starting)

**Current Authorization Problem**:
- Wrapper services: `IDataverseSecurityService`, `IUacService`
- These wrappers just call through to AuthorizationService (pass-through)
- Adds layer without adding value
- More interfaces to maintain

**Code Impact**:
- Unnecessary indirection: Endpoint → Wrapper → AuthorizationService
- Change ripple: Update authorization = update wrapper + interface + endpoint
- Testing overhead: Mock wrapper interface instead of real authorization
- Harder to understand: Why wrap what's already usable?

**Quick Verification**:
```bash
# Check if wrapper services exist
ls src/api/Spe.Bff.Api/Services/IDataverseSecurityService.cs 2>/dev/null && echo "Wrapper exists" || echo "Wrapper doesn't exist - skip this task"

# Check wrapper usage
grep -rn "IDataverseSecurityService\|IUacService" src/api/Spe.Bff.Api/Api/
```

**Note**: This task is CONDITIONAL - if wrappers don't exist, skip to Task 2.6

---

## Background: Why Authorization Wrappers Exist

**Historical Context**:
- Authorization was complex: Custom logic for permissions, roles, UAC
- Pattern: "Wrap complex logic in service"
- Created wrappers to "simplify" calling code
- Each wrapper got interface "for testability"

**How It Evolved**:
1. **V1**: Direct Dataverse queries for permissions (coupled)
2. **V2**: Created AuthorizationService in Spaarke.Core (better)
3. **V3**: Created IDataverseSecurityService wrapper "for convenience" (unnecessary)
4. **V4**: Added IUacService wrapper "for consistency" (pattern proliferation)

**Why Wrappers Seemed Necessary**:
- "Simplify complex authorization logic"
- "Provide convenient API for endpoints"
- "Abstract away Dataverse details"
- "Make authorization testable"

**What We Realized**:
- Wrappers are just pass-through (no logic added)
- AuthorizationService already has good API
- Wrappers don't simplify, they add indirection
- Can test against AuthorizationService directly

**Why Direct Usage is Correct**:
- **No unnecessary layer**: Endpoint → AuthorizationService (direct)
- **Better clarity**: See what authorization actually does
- **Easier testing**: Test real AuthorizationService behavior
- **Less maintenance**: One class instead of three (interface + wrapper + service)

**Important Exception**:
- `IAccessDataSource` is **NOT a wrapper** - it's a **required seam**
- Provides abstraction over Dataverse data queries (may have multiple implementations)
- Per ADR-003: "Keep IAccessDataSource as data access abstraction"

**Real Example**:
```csharp
// ❌ OLD: Unnecessary wrapper (pass-through, no logic)
public class DataverseSecurityService : IDataverseSecurityService
{
    private readonly AuthorizationService _authz;

    public async Task<bool> CanUploadAsync(string userId, string containerId)
    {
        // Just passes through - why does this exist?
        return await _authz.IsAuthorizedAsync(userId, "canuploadfiles", new { ContainerId = containerId });
    }
}

// Endpoint uses wrapper
private static async Task<IResult> Upload(IDataverseSecurityService security, ...)
{
    if (!await security.CanUploadAsync(userId, containerId))  // Indirection!
        return Results.Forbid();
}

// ✅ NEW: Direct usage (clear, no indirection)
private static async Task<IResult> Upload(AuthorizationService authz, ...)
{
    if (!await authz.IsAuthorizedAsync(userId, "canuploadfiles", new { ContainerId = containerId }))  // Clear!
        return Results.Forbid();
}
```

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically removing unnecessary authorization wrapper services.

TASK: Remove IDataverseSecurityService and IUacService wrappers, update endpoints to use AuthorizationService from Spaarke.Core directly.

CONSTRAINTS:
- Must preserve IAccessDataSource (required seam per ADR-003)
- Must preserve authorization logic and checks
- Must NOT change authorization behavior (same permissions)
- Must use AuthorizationService from Spaarke.Core (if available)

VERIFICATION BEFORE STARTING:
1. Verify Phase 2 Task 4 complete (tests updated)
2. Verify wrapper services exist (IDataverseSecurityService, IUacService)
3. Verify Spaarke.Core has AuthorizationService available
4. If any verification fails, STOP and assess approach

FOCUS: Stay focused on simplifying authorization only. Do NOT delete obsolete files (that's Task 2.6).
```

---

## Goal

Remove unnecessary authorization wrapper services (IDataverseSecurityService, IUacService) and use **AuthorizationService** from Spaarke.Core directly in endpoints.

**Problem**:
- Wrapper services add unnecessary layer
- IDataverseSecurityService wraps AuthorizationService
- IUacService wraps UAC checks
- Violates ADR-010 (no unnecessary abstractions)

**Target**:
- Endpoints inject AuthorizationService directly
- Remove wrapper interfaces and implementations
- Preserve IAccessDataSource (required seam for Dataverse queries)

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 2 Task 4 complete
- [ ] Check tests updated
grep "Mock<IGraphClientFactory>" tests/Spe.Bff.Api.Tests/

# 2. Find wrapper services
- [ ] Locate IDataverseSecurityService
grep -r "IDataverseSecurityService" src/api/Spe.Bff.Api/

- [ ] Locate IUacService
grep -r "IUacService" src/api/Spe.Bff.Api/

# 3. Verify Spaarke.Core AuthorizationService
- [ ] Check if AuthorizationService exists in shared project
ls src/shared/Spaarke.Core/Authorization/AuthorizationService.cs
# OR
grep -r "class AuthorizationService" src/shared/

# 4. Document current authorization usage
- [ ] Count endpoints using wrapper services: _____
```

**If wrapper services don't exist**: Skip this task, proceed to Task 2.6

**If AuthorizationService doesn't exist in Spaarke.Core**: Use existing authorization approach, focus on removing wrappers only

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Api/OBOEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs (or Program.cs)
```

**Files to potentially remove** (in Task 2.6):
```bash
- src/api/Spe.Bff.Api/Services/IDataverseSecurityService.cs
- src/api/Spe.Bff.Api/Services/DataverseSecurityService.cs
- src/api/Spe.Bff.Api/Services/IUacService.cs
- src/api/Spe.Bff.Api/Services/UacService.cs
```

---

## Implementation

### Option A: Use Spaarke.Core AuthorizationService (Preferred)

**Scenario**: AuthorizationService exists in Spaarke.Core

#### Step 1: Update Endpoint to Use AuthorizationService

**File**: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

```csharp
using Spaarke.Core.Authorization; // From Spaarke.Core
using Spe.Bff.Api.Storage;

namespace Spe.Bff.Api.Api;

public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/obo/upload", UploadFile)
            .RequireAuthorization()
            .DisableAntiforgery();

        return app;
    }

    // ✅ NEW: Inject AuthorizationService directly
    private static async Task<IResult> UploadFile(
        SpeFileStore fileStore,
        AuthorizationService authz,  // ✅ Direct from Spaarke.Core
        HttpRequest request,
        HttpContext httpContext,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();
        var fileName = request.Query["fileName"].ToString();

        if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(fileName))
            return Results.BadRequest(new { Error = "containerId and fileName are required" });

        try
        {
            // ✅ NEW: Use AuthorizationService directly
            var userId = httpContext.User.FindFirst("oid")?.Value
                ?? throw new UnauthorizedAccessException("User ID not found in token");

            var isAuthorized = await authz.IsAuthorizedAsync(
                userId,
                "canuploadfiles",
                new { ContainerId = containerId });

            if (!isAuthorized)
            {
                logger.LogWarning(
                    "User {UserId} unauthorized to upload to container {ContainerId}",
                    userId, containerId);
                return Results.Forbid();
            }

            // Upload file
            var userToken = ExtractBearerToken(request);
            var result = await fileStore.UploadFileAsync(
                containerId,
                fileName,
                request.Body,
                userToken,
                cancellationToken);

            return Results.Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized upload attempt");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error uploading file");
            return Results.Problem(
                title: "Upload failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string ExtractBearerToken(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            throw new UnauthorizedAccessException("Missing or invalid Authorization header");

        return authHeader["Bearer ".Length..];
    }
}
```

#### Step 2: Update DI Registration

**File**: `src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs` (or SpaarkeCore.Extensions.cs)

```csharp
using Spaarke.Core.Authorization;
using Spaarke.Dataverse;

namespace Spe.Bff.Api.Extensions;

public static class SpaarkeCoreExtensions
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // ✅ Authorization (from Spaarke.Core)
        services.AddSingleton<AuthorizationService>();

        // ✅ Access data source (required seam per ADR-003)
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        // ❌ REMOVE: Wrapper services (if they exist)
        // services.AddScoped<IDataverseSecurityService, DataverseSecurityService>();
        // services.AddScoped<IUacService, UacService>();

        return services;
    }
}
```

### Option B: No Spaarke.Core AuthorizationService (Alternative)

**Scenario**: AuthorizationService doesn't exist, use ASP.NET Core authorization directly

```csharp
using Microsoft.AspNetCore.Authorization;

namespace Spe.Bff.Api.Api;

public static class OBOEndpoints
{
    // Use ASP.NET Core IAuthorizationService
    private static async Task<IResult> UploadFile(
        SpeFileStore fileStore,
        IAuthorizationService authzService,  // ASP.NET Core built-in
        HttpContext httpContext,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();

        // Check authorization using policy
        var authResult = await authzService.AuthorizeAsync(
            httpContext.User,
            new { ContainerId = containerId },
            "CanUploadFiles");

        if (!authResult.Succeeded)
        {
            logger.LogWarning("User unauthorized to upload to container {ContainerId}", containerId);
            return Results.Forbid();
        }

        // ... rest of upload logic
    }
}
```

**DI Registration** (Option B):
```csharp
// ASP.NET Core authorization (built-in)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CanUploadFiles", policy =>
        policy.Requirements.Add(new UploadPermissionRequirement()));
});

builder.Services.AddSingleton<IAuthorizationHandler, UploadPermissionHandler>();
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Authorization Check
```bash
# Verify AuthorizationService is used
grep -r "AuthorizationService" src/api/Spe.Bff.Api/Api/
# Expected: Should find usage in endpoints

# Verify wrapper services removed from DI
grep -r "IDataverseSecurityService\|IUacService" src/api/Spe.Bff.Api/Extensions/
# Expected: No results (or only in comments)

# Verify IAccessDataSource preserved (required seam)
grep -r "IAccessDataSource" src/api/Spe.Bff.Api/Extensions/
# Expected: Should still be registered ✅
```

### Manual Testing (if API is running)
```bash
# Test authorized request
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test.txt" \
  -H "Authorization: Bearer $VALID_TOKEN" \
  -H "Content-Type: text/plain" \
  -d "test content"
# Expected: 200 OK (if user has permission)

# Test unauthorized request
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test.txt" \
  -H "Authorization: Bearer $INVALID_TOKEN" \
  -H "Content-Type: text/plain" \
  -d "test content"
# Expected: 403 Forbidden (if user lacks permission)
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 2 Task 4 complete (tests updated)
- [ ] **Pre-flight**: Verified wrapper services exist (or skip if not present)
- [ ] **Pre-flight**: Verified authorization approach available
- [ ] Updated endpoints to use AuthorizationService directly
- [ ] Removed IDataverseSecurityService from DI (if exists)
- [ ] Removed IUacService from DI (if exists)
- [ ] Preserved IAccessDataSource (required seam)
- [ ] Updated endpoint authorization checks
- [ ] Authorization behavior unchanged (same permissions)
- [ ] Build succeeds: `dotnet build`
- [ ] Authorization tests pass (if they exist)

---

## Expected Results

**Before**:
- ❌ Endpoints inject IDataverseSecurityService (wrapper)
- ❌ Endpoints inject IUacService (wrapper)
- ❌ Unnecessary layer between endpoint and authorization

**After**:
- ✅ Endpoints inject AuthorizationService directly
- ✅ Wrapper services removed from DI
- ✅ IAccessDataSource preserved (required seam)
- ✅ Same authorization behavior, fewer layers

---

## Anti-Pattern Verification

### ✅ Avoided: Interface Proliferation
```bash
# Verify wrapper interfaces removed from DI
grep -E "IDataverseSecurityService|IUacService" src/api/Spe.Bff.Api/Extensions/
# Expected: No results ✅
```

**Why**: ADR-010 - Avoid unnecessary wrapper services

### ✅ Preserved: Required Seam
```bash
# Verify IAccessDataSource still registered (required)
grep "IAccessDataSource" src/api/Spe.Bff.Api/Extensions/
# Expected: Should find registration ✅
```

**Why**: ADR-003 - IAccessDataSource is required seam for Dataverse queries

---

## Troubleshooting

### Issue: AuthorizationService not found in Spaarke.Core

**Cause**: Shared authorization service doesn't exist

**Fix**: Use Option B (ASP.NET Core IAuthorizationService) or skip simplification

### Issue: Authorization checks fail after update

**Cause**: Different method signature or authorization logic

**Fix**: Verify authorization check logic matches:
```csharp
// Before (wrapper)
var canUpload = await _securityService.CanUploadAsync(userId, containerId);

// After (direct)
var canUpload = await _authz.IsAuthorizedAsync(userId, "canuploadfiles", new { ContainerId = containerId });
```

### Issue: "IAccessDataSource not resolved"

**Cause**: Accidentally removed required seam

**Fix**: Ensure IAccessDataSource is still registered:
```csharp
services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ Endpoints use AuthorizationService directly (no wrappers)
- [ ] ✅ Wrapper services removed from DI
- [ ] ✅ IAccessDataSource preserved (required seam)
- [ ] ✅ Authorization behavior unchanged
- [ ] ✅ Build succeeds
- [ ] ✅ Task stayed focused (did NOT delete files - that's Task 2.6)

**If any item unchecked**: Review and fix before proceeding to Task 2.6

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Api/
git add src/api/Spe.Bff.Api/Extensions/

git commit -m "refactor(auth): remove authorization wrapper services per ADR-010

- Update endpoints to inject AuthorizationService directly (from Spaarke.Core)
- Remove IDataverseSecurityService wrapper from DI
- Remove IUacService wrapper from DI
- Preserve IAccessDataSource (required seam per ADR-003)
- Authorization behavior unchanged (same permissions)

Simplification: 2 wrapper services removed, 1 direct service used
ADR Compliance: ADR-010 (No unnecessary abstractions), ADR-003 (Preserve seams)
Anti-Patterns Avoided: Interface proliferation, pass-through wrappers
Task: Phase 2, Task 5"
```

---

## Next Task

➡️ [Phase 2 - Task 6: Cleanup - Delete Obsolete Files](phase-2-task-6-cleanup.md)

**What's next**: Delete 8 obsolete service files after validation passes

---

## Related Resources

- **Patterns**: Use framework services directly (AuthorizationService, IAuthorizationService)
- **Anti-Patterns**:
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#authorization-simplification)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-003, ADR-010
