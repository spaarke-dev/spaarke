# Task 4.4 - Phase 4: Update Endpoints

**Sprint:** 4
**Phase:** 4 of 7
**Estimated Effort:** 2 hours
**Dependencies:** Phases 1, 2, 3 complete
**Status:** Blocked

---

## Objective

Replace `IOboSpeService` with `SpeFileStore` in all OBO endpoints. Use `TokenHelper` instead of inline `GetBearer()` methods.

---

## Pattern to Follow

**BEFORE:**
```csharp
app.MapGet("/api/obo/...", async (
    [FromServices] IOboSpeService oboSvc,  // ❌ Interface
    HttpContext ctx) =>
{
    var bearer = GetBearer(ctx);  // ❌ Inline helper
    var result = await oboSvc.SomeMethod(bearer, ...);
    return TypedResults.Ok(result);
});
```

**AFTER:**
```csharp
app.MapGet("/api/obo/...", async (
    [FromServices] SpeFileStore speFileStore,  // ✅ Concrete facade
    HttpContext ctx) =>
{
    try
    {
        var userToken = TokenHelper.ExtractBearerToken(ctx);  // ✅ Utility
        var result = await speFileStore.SomeMethodAsUserAsync(userToken, ...);
        return TypedResults.Ok(result);
    }
    catch (UnauthorizedAccessException)  // ✅ Handle token errors
    {
        return TypedResults.Unauthorized();
    }
});
```

---

## Phase 4.1: Update OBOEndpoints.cs

**File:** `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

### Step 1: Update using statements

```csharp
using Spe.Bff.Api.Infrastructure.Graph;  // Keep
using Spe.Bff.Api.Infrastructure.Auth;   // ADD THIS
```

### Step 2: Replace IOboSpeService → SpeFileStore (7 endpoints)

For each endpoint, make these changes:
1. Change `[FromServices] IOboSpeService oboSvc` → `[FromServices] SpeFileStore speFileStore`
2. Wrap endpoint logic in `try/catch (UnauthorizedAccessException)`
3. Change `GetBearer(ctx)` → `TokenHelper.ExtractBearerToken(ctx)`
4. Change method calls to `speFileStore.*AsUserAsync(userToken, ...)`

**Endpoints to update:**
1. `GET /api/obo/containers/{id}/children` → `.ListChildrenAsUserAsync(...)`
2. `PUT /api/obo/containers/{id}/files/{*path}` → `.UploadSmallAsUserAsync(...)`
3. `POST /api/obo/drives/{driveId}/upload-session` → `.CreateUploadSessionAsUserAsync(...)`
4. `PUT /api/obo/upload-session/chunk` → `.UploadChunkAsUserAsync(...)`
5. `PATCH /api/obo/drives/{driveId}/items/{itemId}` → `.UpdateItemAsUserAsync(...)`
6. `GET /api/obo/drives/{driveId}/items/{itemId}/content` → `.DownloadFileWithRangeAsUserAsync(...)`
7. `DELETE /api/obo/drives/{driveId}/items/{itemId}` → `.DeleteItemAsUserAsync(...)`

### Step 3: Delete GetBearer() helper method

Remove the private `GetBearer()` method at end of file - no longer needed.

**See:** Full refactored code in [TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md](TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md) Phase 4.1

---

## Phase 4.2: Update UserEndpoints.cs

**File:** `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

### Step 1: Update using statements

```csharp
using Spe.Bff.Api.Infrastructure.Graph;  // CHANGED (remove Services)
using Spe.Bff.Api.Infrastructure.Auth;   // ADD THIS
```

### Step 2: Replace IOboSpeService → SpeFileStore (2 endpoints)

**Endpoint 1:** `GET /api/me`
- Change parameter: `IOboSpeService oboSvc` → `SpeFileStore speFileStore`
- Add try/catch for `UnauthorizedAccessException`
- Change method call: `.GetUserInfoAsync(userToken, ct)`

**Endpoint 2:** `GET /api/me/capabilities`
- Change parameter: `IOboSpeService oboSvc` → `SpeFileStore speFileStore`
- Add try/catch for `UnauthorizedAccessException`
- Change method call: `.GetUserCapabilitiesAsync(userToken, containerId, ct)`

### Step 3: Delete GetBearer() helper method

Remove the private `GetBearer()` method - no longer needed.

**See:** Full refactored code in [TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md](TASK-4.4-FULL-REFACTOR-IMPLEMENTATION.md) Phase 4.2

---

## Acceptance Criteria

- [ ] OBOEndpoints.cs updated (7 endpoints)
- [ ] UserEndpoints.cs updated (2 endpoints)
- [ ] All `IOboSpeService` references removed
- [ ] All `GetBearer()` methods removed
- [ ] All endpoints use `TokenHelper.ExtractBearerToken(ctx)`
- [ ] All endpoints handle `UnauthorizedAccessException`
- [ ] Build succeeds with 0 errors

---

## Next Phase

**Phase 5:** Delete interface and implementation files
