# Package Upgrade Impact Analysis - Microsoft.Graph & Polly

**Date:** 2025-10-20
**Scope:** Spe.Bff.Api package upgrades (Graph v0.x → v5.x, Polly v1.x → v8.x)
**Risk Level:** **MEDIUM** (requires code changes, but well-defined scope)

---

## Executive Summary

**Can we safely upgrade without breaking production? YES**

**Key Finding:** The package upgrades **will NOT affect** the PCF control or existing SDAP functionality because:

1. **PCF has NO direct dependency on Microsoft.Graph or Polly packages**
   - PCF uses standard `fetch()` API via `SdapApiClient.ts`
   - Communication is 100% HTTP/REST - completely package-agnostic
   - PCF only depends on API contract (URLs, request/response formats)

2. **BFF API changes are isolated to server-side implementation**
   - API contracts (endpoints, DTOs, auth) remain identical
   - URL routes unchanged
   - Request/response formats unchanged
   - HTTP status codes unchanged

3. **Changes required are minimal and well-defined**
   - Only 2 files need code changes
   - Breaking changes are syntax/namespace only (not behavioral)
   - Zero database or configuration changes needed

---

## Component Dependency Analysis

### 1. PCF Control (UniversalDatasetGrid) - ✅ ZERO IMPACT

**Current dependencies:**
```typescript
// SdapApiClient.ts
- fetch() API (browser native)
- MSAL.js for authentication
- TypeScript/React
- Fluent UI React
```

**API endpoints used by PCF:**
```typescript
PUT  /api/obo/drives/{driveId}/upload?fileName={name}
GET  /api/obo/drives/{driveId}/items/{itemId}/content
DELETE /api/obo/drives/{driveId}/items/{itemId}
```

**Impact:** **NONE**
- PCF never imports Microsoft.Graph or Polly packages
- PCF uses standard HTTP `fetch()` - completely package-agnostic
- As long as BFF API endpoints return same HTTP responses, PCF works identically
- API contract remains 100% unchanged

**Testing required:** Standard regression testing (upload, download, delete)

---

### 2. BFF API Server-Side Components - 🔧 REQUIRES CODE CHANGES

**Files requiring changes: 2 files**

#### File 1: ProblemDetailsHelper.cs (Lines 10, 69-75)

**Current code (Graph v0.x):**
```csharp
using Microsoft.Graph;

public static IResult FromGraphException(ServiceException ex)
{
    var status = ex.ResponseStatusCode;
    var code = GetErrorCode(ex);
    var graphRequestId = ex.ResponseHeaders?.GetValues("request-id")?.FirstOrDefault();
    // ...
}

private static string GetErrorCode(ServiceException ex)
{
    return ex.Message?.Contains("Authorization_RequestDenied") == true ? "Authorization_RequestDenied" :
           ex.Message?.Contains("TooManyRequests") == true ? "TooManyRequests" :
           ex.ResponseStatusCode.ToString();
}
```

**Required changes (Graph v5.x):**
```csharp
using Microsoft.Graph.Models.ODataErrors;

public static IResult FromGraphException(ODataError ex)
{
    var status = ex.ResponseStatusCode ?? 500;
    var code = GetErrorCode(ex);
    var graphRequestId = ex.ResponseHeaders?.FirstOrDefault(h =>
        h.Key == "request-id" || h.Key == "client-request-id").Value;
    // ...
}

private static string GetErrorCode(ODataError ex)
{
    var errorCode = ex.Error?.Code ?? "";
    return errorCode == "Authorization_RequestDenied" ? "Authorization_RequestDenied" :
           errorCode == "activityLimitReached" ? "TooManyRequests" :
           status.ToString();
}
```

**Breaking changes:**
- `ServiceException` → `ODataError`
- `ResponseHeaders` changed from `HttpResponseHeaders` to `Dictionary<string, string>`
- Error codes now in `ex.Error.Code` property instead of message parsing

**Impact:** Error handling and logging only - does NOT affect happy path

**Risk:** LOW - Error responses will still be HTTP 403/401/500 with ProblemDetails format

---

#### File 2: GraphHttpMessageHandler.cs (Lines 3-6, 20, 41, 116-133)

**Current code (Polly v1.x):**
```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;

private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;

private IAsyncPolicy<HttpResponseMessage> BuildResiliencePolicy()
{
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(/* ... */);

    var circuitBreakerPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(/* ... */);

    var timeoutPolicy = Policy
        .TimeoutAsync<HttpResponseMessage>(
            timeout: TimeSpan.FromSeconds(_options.TimeoutSeconds),
            timeoutStrategy: TimeoutStrategy.Pessimistic,
            onTimeoutAsync: async (context, timespan, task) => { /* ... */ });

    return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy);
}
```

**Required changes (Polly v8.x):**
```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

private ResiliencePipeline<HttpResponseMessage> BuildResiliencePipeline()
{
    var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError ||
                                   r.StatusCode == HttpStatusCode.RequestTimeout ||
                                   r.StatusCode == HttpStatusCode.TooManyRequests),
            MaxRetryAttempts = _options.RetryCount,
            Delay = TimeSpan.FromSeconds(_options.RetryBackoffSeconds),
            BackoffType = DelayBackoffType.Exponential,
            OnRetry = args => { /* logging */ }
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
        {
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode >= HttpStatusCode.InternalServerError),
            FailureRatio = 0.5,
            MinimumThroughput = _options.CircuitBreakerFailureThreshold,
            BreakDuration = TimeSpan.FromSeconds(_options.CircuitBreakerBreakDurationSeconds),
            OnOpened = args => { /* logging */ },
            OnClosed = args => { /* logging */ },
            OnHalfOpened = args => { /* logging */ }
        })
        .AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
            OnTimeout = args => { /* logging */ }
        })
        .Build();

    return pipeline;
}

protected override async Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request,
    CancellationToken cancellationToken)
{
    return await _resiliencePipeline.ExecuteAsync(async ct =>
    {
        return await base.SendAsync(request, ct);
    }, cancellationToken);
}
```

**Breaking changes:**
- `IAsyncPolicy<T>` → `ResiliencePipeline<T>`
- `Policy.WrapAsync()` → `ResiliencePipelineBuilder`
- `Polly.Timeout` namespace removed (integrated into core)
- Different configuration syntax

**Impact:** Resilience behavior only - retry/circuit breaker/timeout logic remains functionally identical

**Risk:** LOW - Behavior is equivalent, just different API syntax

---

### 3. Files with NO Changes Required - ✅ COMPATIBLE

**Infrastructure/Graph/*.cs files (9 files):**
- ContainerOperations.cs
- DriveItemOperations.cs
- SpeFileStore.cs
- UploadSessionManager.cs
- UserOperations.cs
- GraphClientFactory.cs
- IGraphClientFactory.cs

**Current code:**
```csharp
using Microsoft.Graph.Models;  // ← This namespace is NEW in v5.x

var container = new FileStorageContainer { /* ... */ };
var driveItem = await client.Drives[driveId].Items[itemId].GetAsync();
```

**Impact:** **NONE - Already compatible with v5.x!**

**Why?**
- Code already uses `Microsoft.Graph.Models` namespace (v5+ structure)
- Code was written targeting Graph SDK v5+
- The COMPILE errors occur only because v0.x is being restored locally
- When v5.x is restored, these files will compile without changes

**Evidence:** The October 6 build succeeded with these files unchanged, proving they're v5+ compatible

---

### 4. API Endpoints - ✅ ZERO CHANGES REQUIRED

**All endpoint handlers (OBOEndpoints.cs, UploadEndpoints.cs, DocumentsEndpoints.cs):**

**Current code:**
```csharp
app.MapPut("/api/obo/drives/{driveId}/upload", async (
    string driveId,
    [FromQuery] string fileName,
    HttpRequest request,
    IGraphClientFactory graphFactory,
    /* ... */) =>
{
    var client = graphFactory.CreateForUser(accessToken);
    var result = await client.Drives[driveId].Items.Root
        .ItemWithPath(fileName)
        .Content
        .PutAsync(fileStream);
    return TypedResults.Ok(result);
});
```

**Impact:** **NONE**
- Minimal API route definitions unchanged
- HTTP method, URL patterns unchanged
- Request/response DTOs unchanged
- Authentication flow unchanged

**Why?** Endpoint code uses `IGraphClientFactory` abstraction - implementation details are hidden

---

## API Contract Stability - ✅ 100% PRESERVED

**Critical for PCF compatibility:**

### HTTP Endpoints (Unchanged)
```
✅ PUT  /api/obo/drives/{driveId}/upload?fileName={name}
✅ GET  /api/obo/drives/{driveId}/items/{itemId}/content
✅ DELETE /api/obo/drives/{driveId}/items/{itemId}
✅ GET  /api/health
✅ POST /api/documents (future - Phase 7)
```

### Request Formats (Unchanged)
```typescript
// Upload
PUT /api/obo/drives/{driveId}/upload?fileName={name}
Headers: Authorization: Bearer {token}
Body: <binary file content>

// Download
GET /api/obo/drives/{driveId}/items/{itemId}/content
Headers: Authorization: Bearer {token}
Response: <binary file content>

// Delete
DELETE /api/obo/drives/{driveId}/items/{itemId}
Headers: Authorization: Bearer {token}
Response: 204 No Content
```

### Response Formats (Unchanged)
```typescript
// Success (Upload)
200 OK
{
  "id": "01ABCDEF...",
  "name": "document.pdf",
  "size": 1024000,
  "webUrl": "https://...",
  // ... (DriveItem schema)
}

// Error (All endpoints)
400/401/403/404/500
{
  "title": "error",
  "status": 400,
  "detail": "Error message",
  "extensions": {
    "graphErrorCode": "...",
    "graphRequestId": "..."
  }
}
```

**Conclusion:** PCF will see IDENTICAL HTTP responses before and after upgrade

---

## Risk Assessment

### Overall Risk: **MEDIUM**

**Risk breakdown:**

| Component | Risk Level | Impact | Mitigation |
|-----------|-----------|--------|------------|
| PCF Control | **NONE** | Zero changes required | Standard regression testing |
| API Contracts | **NONE** | Contracts unchanged | API integration tests |
| Error Handling | **LOW** | Format preserved | Test error scenarios |
| Resilience (Polly) | **LOW** | Behavior equivalent | Load testing |
| Graph Operations | **NONE** | Already v5+ compatible | Test file upload/download |
| Authentication | **NONE** | MSAL unchanged | Test OBO flow |

### Critical Success Factors

**Must verify after upgrade:**
1. ✅ BFF API compiles without errors
2. ✅ Health endpoint returns 200 OK
3. ✅ PCF file upload succeeds
4. ✅ PCF file download succeeds
5. ✅ PCF file delete succeeds
6. ✅ Error responses preserve ProblemDetails format
7. ✅ Retry/circuit breaker behavior functions correctly

---

## Testing Strategy

### Phase 1: Local Build Verification (15 minutes)
```bash
# 1. Clear NuGet cache
dotnet nuget locals all --clear

# 2. Update csproj with version constraints
# (Add explicit versions to Spe.Bff.Api.csproj)

# 3. Restore packages
cd src/api/Spe.Bff.Api
dotnet restore

# 4. Verify correct versions
dotnet list package | grep "Microsoft.Graph\|Polly"
# Expected:
#   Microsoft.Graph                 5.56.0
#   Polly                           8.4.1

# 5. Build
dotnet build
# Expected: Build succeeded. 0 Error(s)
```

**Success criteria:** Clean build (0 errors, 0 warnings)

---

### Phase 2: Code Migration (30-45 minutes)

**Step 1: Fix ProblemDetailsHelper.cs**
```csharp
// Change namespace
- using Microsoft.Graph;
+ using Microsoft.Graph.Models.ODataErrors;

// Update method signature and implementation
- public static IResult FromGraphException(ServiceException ex)
+ public static IResult FromGraphException(ODataError ex)
{
-   var status = ex.ResponseStatusCode;
+   var status = ex.ResponseStatusCode ?? 500;

-   var code = GetErrorCode(ex);
+   var code = ex.Error?.Code ?? status.ToString();

-   var graphRequestId = ex.ResponseHeaders?.GetValues("request-id")?.FirstOrDefault();
+   var graphRequestId = ex.ResponseHeaders?
        .FirstOrDefault(h => h.Key == "request-id" || h.Key == "client-request-id").Value;
}

- private static string GetErrorCode(ServiceException ex)
+ private static string GetErrorCode(ODataError ex)
{
-   return ex.Message?.Contains("Authorization_RequestDenied") == true ? "Authorization_RequestDenied" :
-          ex.Message?.Contains("TooManyRequests") == true ? "TooManyRequests" :
-          ex.ResponseStatusCode.ToString();
+   var errorCode = ex.Error?.Code ?? "";
+   return errorCode == "Authorization_RequestDenied" ? "Authorization_RequestDenied" :
+          errorCode == "activityLimitReached" ? "TooManyRequests" :
+          (ex.ResponseStatusCode ?? 500).ToString();
}
```

**Step 2: Fix GraphHttpMessageHandler.cs**

Option A: **Minimal Changes (Polly.Extensions.Http still works in v8)**
```csharp
// Keep most existing code, just remove Polly.Timeout namespace
- using Polly.Timeout;

// Timeout policy now uses core Polly
var timeoutPolicy = Policy
    .TimeoutAsync<HttpResponseMessage>(
        timeout: TimeSpan.FromSeconds(_options.TimeoutSeconds),
        onTimeoutAsync: async (context, timespan, abandoned) =>
        {
            // ... logging
        });
```

Option B: **Full Migration to v8 API** (see detailed code above)

**Recommendation:** Start with Option A (minimal changes) for faster deployment

---

### Phase 3: Local Testing (20 minutes)

**Test 1: Health Check**
```bash
dotnet run --project src/api/Spe.Bff.Api
curl http://localhost:5000/api/health
# Expected: 200 OK
```

**Test 2: Authentication Flow**
```bash
# Test token acquisition
curl -X POST http://localhost:5000/api/auth/token \
  -H "Authorization: Bearer {test-token}"
# Expected: 200 OK with access token
```

**Test 3: Graph API Error Handling**
```bash
# Trigger 403 error (no permissions)
curl -X PUT http://localhost:5000/api/obo/drives/invalid/upload \
  -H "Authorization: Bearer {invalid-token}"
# Expected: 403 with ProblemDetails JSON
```

**Test 4: Resilience (Retry/Timeout)**
```bash
# Monitor logs for retry behavior
# Expected: Retry attempts logged with exponential backoff
```

---

### Phase 4: Integration Testing with PCF (30 minutes)

**Test 5: PCF File Upload**
1. Deploy BFF API to Azure (or test locally)
2. Open UniversalDatasetGrid PCF in Dataverse
3. Click "Upload" button
4. Select file and upload
5. **Expected:** File uploads successfully, DriveItem returned

**Test 6: PCF File Download**
1. Click "Download" on existing file
2. **Expected:** File downloads successfully

**Test 7: PCF File Delete**
1. Click "Delete" on file
2. **Expected:** File deleted, 204 No Content

**Test 8: PCF Error Handling**
1. Try uploading to invalid container
2. **Expected:** User-friendly error message displayed

---

### Phase 5: Production Smoke Test (15 minutes)

**After deployment to Azure:**
```bash
# 1. Health check
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/health
# Expected: 200 OK

# 2. Monitor Application Insights for errors
az monitor app-insights query \
  --app spe-appinsights-dev \
  --analytics-query "traces | where severityLevel > 2 | take 10"
# Expected: No new errors

# 3. Monitor App Service logs
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
# Expected: No errors, normal request flow
```

---

## Rollback Plan

**If issues discovered after deployment:**

### Option 1: Instant Rollback (Azure App Service Slots)
```bash
# Swap back to previous slot
az webapp deployment slot swap \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --slot staging \
  --target-slot production
```
**Downtime:** ~30 seconds

---

### Option 2: Redeploy Previous Build
```bash
# Use October 6 artifact from /publish folder
cd /c/code_files/spaarke
tar -czf deployment.tar.gz publish/
az webapp deploy \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --src-path deployment.tar.gz
```
**Downtime:** ~2-3 minutes

---

### Option 3: Git Revert + Redeploy
```bash
# Revert commits
git revert HEAD~3..HEAD  # Revert last 3 commits
git push origin master

# Trigger CI/CD pipeline
# (Or manual deployment)
```
**Downtime:** ~5-10 minutes (depends on CI/CD)

---

## Timeline

**Total estimated time: 2-2.5 hours**

| Phase | Duration | Dependencies |
|-------|----------|--------------|
| 1. Package version constraints | 15 min | None |
| 2. Code migration (ProblemDetails + Polly) | 30-45 min | Phase 1 complete |
| 3. Local build & unit tests | 20 min | Phase 2 complete |
| 4. PCF integration testing (local/dev) | 30 min | Phase 3 complete |
| 5. Deploy to production | 15 min | Phase 4 complete |
| 6. Production smoke test | 15 min | Phase 5 complete |

**Buffer:** +30 min for unexpected issues

**Total:** 2-2.5 hours

---

## Deployment Strategy

### Recommended Approach: Blue-Green Deployment

**Step 1: Deploy to staging slot**
```bash
# Deploy to staging
az webapp deployment slot create \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --slot staging

# Deploy new build to staging
az webapp deploy \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --slot staging \
  --src-path deployment.tar.gz
```

**Step 2: Test staging**
```bash
# Test staging endpoint
curl https://spe-api-dev-67e2xz-staging.azurewebsites.net/api/health
# Expected: 200 OK

# Run PCF tests against staging
# Update PCF baseUrl to staging endpoint
# Test upload/download/delete
```

**Step 3: Swap to production (if tests pass)**
```bash
# Swap staging → production
az webapp deployment slot swap \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --slot staging \
  --target-slot production
```

**Benefits:**
- Zero-downtime deployment
- Instant rollback (swap back)
- Test in production-like environment

---

## Conclusion

### Can we safely upgrade? **YES**

**Reasons:**
1. ✅ PCF has ZERO dependency on upgraded packages (uses HTTP only)
2. ✅ API contracts remain 100% unchanged
3. ✅ Code changes are minimal (2 files) and well-defined
4. ✅ Breaking changes are syntax-only, not behavioral
5. ✅ Rollback is instant (deployment slot swap)
6. ✅ Testing strategy is comprehensive

### Recommended Action Plan

**Proceed with upgrade using blue-green deployment strategy:**
1. Add package version constraints to csproj (15 min)
2. Migrate ProblemDetailsHelper.cs and GraphHttpMessageHandler.cs (45 min)
3. Test locally with PCF (30 min)
4. Deploy to staging slot (15 min)
5. Test staging environment (30 min)
6. Swap to production (1 min)
7. Monitor for 24 hours

**Risk mitigation:**
- Use staging slot for testing
- Keep production slot as instant rollback
- Monitor Application Insights for errors
- Have October 6 build ready as fallback

### Phase 7 Implication

**After upgrade completes:**
- ✅ Phase 7 NavMapEndpoints will build successfully
- ✅ Can proceed with Task 7.3 (NavMapClient TypeScript)
- ✅ Can complete Phase 7 end-to-end testing

**Recommendation:** Complete package upgrades BEFORE proceeding with Phase 7 Tasks 7.3-7.6

---

**Created:** 2025-10-20
**Status:** Analysis Complete - Ready for Implementation
**Priority:** HIGH (blocks Phase 7, but production stable)
**Approval Required:** YES (deployment to production)
