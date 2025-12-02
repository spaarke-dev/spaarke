# Task 4.4 Review - Remove ISpeService/IOboSpeService Abstractions

**Date:** 2025-10-02
**Status:** Under Review - Requires Updated Implementation Plan
**Reviewer:** Claude (Senior .NET Architect)

---

## Executive Summary

Task 4.4 correctly identifies an ADR-007 violation but requires a **major revision** to the implementation plan. The current task document was written before Sprint 3 Task 3.2 refactored SpeFileStore into a modular facade pattern.

**Key Findings:**
1. ✅ **Correctly identifies problem**: ISpeService and IOboSpeService violate ADR-007
2. ⚠️ **Implementation plan outdated**: Based on pre-Sprint-3 architecture
3. ⚠️ **Scope underestimated**: Requires creating 10-11 new OBO methods in SpeFileStore
4. ✅ **No DI registration**: IOboSpeService isn't registered in DI, making this easier

---

## Current Architecture State (Post-Sprint 3)

### What EXISTS Now:

**SpeFileStore (Facade) - App-Only Methods:**
```
src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs (87 lines)
├─ Delegates to ContainerOperations (app-only MI auth)
├─ Delegates to DriveItemOperations (app-only MI auth)
└─ Delegates to UploadSessionManager (app-only MI auth)
```

**IOboSpeService (Interface) - User-Context Methods:**
```
src/api/Spe.Bff.Api/Services/IOboSpeService.cs (19 lines)
├─ 11 methods that accept userBearer token
└─ Used by OBOEndpoints (7 endpoints) and UserEndpoints (2 endpoints)
```

**OboSpeService (Implementation):**
```
src/api/Spe.Bff.Api/Services/OboSpeService.cs (~650 lines)
├─ Implements IOboSpeService
├─ Creates Graph clients with user tokens via IGraphClientFactory
└─ Handles OBO flow, range requests, upload sessions, etc.
```

### What DOES NOT EXIST:

1. ❌ No OBO methods in SpeFileStore (no `*AsUserAsync` methods)
2. ❌ No user token parameter support in SpeFileStore
3. ❌ ContainerOperations/DriveItemOperations don't support user tokens
4. ❌ No TokenHelper utility for bearer token extraction

---

## Interface Usage Analysis

### Files Using IOboSpeService:

| File | Usage Count | Methods Used |
|------|-------------|--------------|
| `OBOEndpoints.cs` | 7 | ListChildrenAsync, UploadSmallAsync, CreateUploadSessionAsync, UploadChunkAsync, UpdateItemAsync, DownloadContentWithRangeAsync, DeleteItemAsync |
| `UserEndpoints.cs` | 2 | GetUserInfoAsync, GetUserCapabilitiesAsync |

**Total:** 9 endpoints using 9 unique IOboSpeService methods

### Methods in IOboSpeService Interface:

```csharp
public interface IOboSpeService
{
    Task<IList<DriveItem>> ListChildrenAsync(string userBearer, string containerId, CancellationToken ct);
    Task<ListingResponse> ListChildrenAsync(string userBearer, string containerId, ListingParameters parameters, CancellationToken ct);
    Task<IResult?> DownloadContentAsync(string userBearer, string driveId, string itemId, CancellationToken ct);
    Task<DriveItem?> UploadSmallAsync(string userBearer, string containerId, string path, Stream content, CancellationToken ct);
    Task<UserInfoResponse?> GetUserInfoAsync(string userBearer, CancellationToken ct);
    Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(string userBearer, string containerId, CancellationToken ct);
    Task<UploadSessionResponse?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior conflictBehavior, CancellationToken ct);
    Task<ChunkUploadResponse> UploadChunkAsync(string userBearer, string uploadSessionUrl, string contentRange, byte[] chunkData, CancellationToken ct);
    Task<DriveItemDto?> UpdateItemAsync(string userBearer, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct);
    Task<bool> DeleteItemAsync(string userBearer, string driveId, string itemId, CancellationToken ct);
    Task<FileContentResponse?> DownloadContentWithRangeAsync(string userBearer, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct);
}
```

**Total: 11 methods** (including 2 overloads of ListChildrenAsync)

---

## Gap Analysis: What Needs to Be Built

### 1. ContainerOperations - Add OBO Support

**Current:** Only app-only methods
**Required:** Add user-token overloads

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs
// ADD THESE METHODS:

public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
    string userToken,
    Guid containerTypeId,
    CancellationToken ct = default)
{
    var graphClient = _graphClientFactory.CreateClientForUser(userToken);
    // ... implementation
}
```

### 2. DriveItemOperations - Add OBO Support

**Current:** Only app-only methods
**Required:** Add user-token overloads for all CRUD operations

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs
// ADD THESE METHODS:

public async Task<IList<FileHandleDto>> ListChildrenAsUserAsync(
    string userToken,
    string driveId,
    string? itemId,
    CancellationToken ct = default)

public async Task<Stream?> DownloadFileAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    CancellationToken ct = default)

public async Task<bool> DeleteFileAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    CancellationToken ct = default)

public async Task<FileHandleDto?> UpdateItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    UpdateFileRequest request,
    CancellationToken ct = default)

// NEW: Range download support
public async Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    RangeHeader? range,
    string? ifNoneMatch,
    CancellationToken ct = default)
```

### 3. UploadSessionManager - Add OBO Support

**Current:** Only app-only methods
**Required:** Add user-token overloads for upload sessions

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs
// ADD THESE METHODS:

public async Task<FileHandleDto?> UploadSmallAsUserAsync(
    string userToken,
    string driveId,
    string path,
    Stream content,
    CancellationToken ct = default)

public async Task<UploadSessionDto?> CreateUploadSessionAsUserAsync(
    string userToken,
    string containerId,
    string path,
    ConflictBehavior conflictBehavior,
    CancellationToken ct = default)

public async Task<ChunkUploadResponse> UploadChunkAsUserAsync(
    string userToken,
    string uploadSessionUrl,
    string contentRange,
    byte[] chunkData,
    CancellationToken ct = default)
```

### 4. SpeFileStore - Expose OBO Methods

**Current:** Only delegates to app-only methods
**Required:** Add delegation methods for all OBO operations

```csharp
// src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs
// ADD THESE DELEGATION METHODS:

// Container operations (user context)
public Task<IList<ContainerDto>> ListContainersAsUserAsync(
    string userToken, Guid containerTypeId, CancellationToken ct = default)
    => _containerOps.ListContainersAsUserAsync(userToken, containerTypeId, ct);

// Drive item operations (user context)
public Task<IList<FileHandleDto>> ListChildrenAsUserAsync(
    string userToken, string driveId, string? itemId, CancellationToken ct = default)
    => _driveItemOps.ListChildrenAsUserAsync(userToken, driveId, itemId, ct);

public Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
    string userToken, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct = default)
    => _driveItemOps.DownloadFileWithRangeAsUserAsync(userToken, driveId, itemId, range, ifNoneMatch, ct);

public Task<bool> DeleteFileAsUserAsync(
    string userToken, string driveId, string itemId, CancellationToken ct = default)
    => _driveItemOps.DeleteFileAsUserAsync(userToken, driveId, itemId, ct);

public Task<FileHandleDto?> UpdateItemAsUserAsync(
    string userToken, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct = default)
    => _driveItemOps.UpdateItemAsUserAsync(userToken, driveId, itemId, request, ct);

// Upload operations (user context)
public Task<FileHandleDto?> UploadSmallAsUserAsync(
    string userToken, string driveId, string path, Stream content, CancellationToken ct = default)
    => _uploadManager.UploadSmallAsUserAsync(userToken, driveId, path, content, ct);

public Task<UploadSessionDto?> CreateUploadSessionAsUserAsync(
    string userToken, string containerId, string path, ConflictBehavior conflictBehavior, CancellationToken ct = default)
    => _uploadManager.CreateUploadSessionAsUserAsync(userToken, containerId, path, conflictBehavior, ct);

public Task<ChunkUploadResponse> UploadChunkAsUserAsync(
    string userToken, string uploadSessionUrl, string contentRange, byte[] chunkData, CancellationToken ct = default)
    => _uploadManager.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);
```

### 5. User Info Methods - NEW Operations

**These methods don't fit into Container/DriveItem/Upload categories:**

Option A: Create new `UserOperations` class (cleaner, follows pattern)
Option B: Add directly to SpeFileStore (simpler, fewer files)

**Recommendation: Option A (matches Sprint 3 modular architecture)**

```csharp
// NEW FILE: src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs

public class UserOperations
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<UserOperations> _logger;

    public async Task<UserInfoResponse?> GetUserInfoAsync(
        string userToken, CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.CreateClientForUser(userToken);
        var me = await graphClient.Me.GetAsync(ct);
        // ... map to UserInfoResponse
    }

    public async Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        string userToken, string containerId, CancellationToken ct = default)
    {
        var graphClient = _graphClientFactory.CreateClientForUser(userToken);
        // ... query user permissions on container
    }
}
```

### 6. TokenHelper Utility - ALREADY EXISTS INLINE

**Current:** Every endpoint has inline `GetBearer()` method
**Recommendation:** Extract to shared utility (as Task 4.4 proposes)

```csharp
// NEW FILE: src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs

public static class TokenHelper
{
    public static string ExtractBearerToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
            throw new UnauthorizedAccessException("Missing Authorization header");

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Invalid Authorization header format");

        return authHeader["Bearer ".Length..].Trim();
    }
}
```

---

## Revised Implementation Steps

### Phase 1: Add OBO Support to Operation Classes (4-6 hours)

1. **ContainerOperations** - Add `ListContainersAsUserAsync`
2. **DriveItemOperations** - Add 5 methods (`List`, `Download`, `DownloadWithRange`, `Update`, `Delete`)
3. **UploadSessionManager** - Add 3 methods (`UploadSmall`, `CreateSession`, `UploadChunk`)
4. **UserOperations (NEW)** - Add 2 methods (`GetUserInfo`, `GetUserCapabilities`)

### Phase 2: Update SpeFileStore Facade (1 hour)

1. Inject `UserOperations` into constructor
2. Add 11 delegation methods (all `*AsUserAsync` variants)

### Phase 3: Create TokenHelper Utility (30 minutes)

1. Create `TokenHelper.cs` with `ExtractBearerToken` method
2. Remove inline `GetBearer()` methods from all endpoints

### Phase 4: Update Endpoints to Use SpeFileStore (2-3 hours)

1. **OBOEndpoints** - Replace `IOboSpeService` with `SpeFileStore` (7 endpoints)
2. **UserEndpoints** - Replace `IOboSpeService` with `SpeFileStore` (2 endpoints)
3. Use `TokenHelper.ExtractBearerToken()` instead of inline methods

### Phase 5: Delete Interface Files (30 minutes)

1. Delete `ISpeService.cs`
2. Delete `IOboSpeService.cs`
3. Delete `SpeService.cs`
4. Delete `OboSpeService.cs`
5. Update `MockOboSpeService` in tests (if needed)

### Phase 6: Build & Test (1-2 hours)

1. Run `dotnet build` - verify no compilation errors
2. Run unit tests - update any broken tests
3. Manual smoke test - verify OBO endpoints work

**Total Estimated Effort:** 10-14 hours (1.5-2 days)

---

## Key Differences from Original Task Document

| Aspect | Original Task | Current Reality |
|--------|---------------|-----------------|
| SpeFileStore architecture | Monolithic class | Modular facade (Sprint 3) |
| Where to add OBO methods | Directly in SpeFileStore | In ContainerOperations, DriveItemOperations, UploadSessionManager |
| UserInfo methods | Add to SpeFileStore | Create new UserOperations class |
| DI registration | Need to unregister IOboSpeService | Already not registered |
| Test mocks | MockOboSpeService needs updating | Still true, check if used |

---

## Risks & Mitigation

### Risk 1: Breaking Changes to OBO Functionality
**Mitigation:**
- Copy implementations from OboSpeService directly (don't rewrite)
- Test each endpoint after migration

### Risk 2: DTO Model Mismatches
**Issue:** OboSpeService returns custom DTOs, SpeFileStore returns different DTOs
**Mitigation:**
- Map DTOs in operation classes to match expected endpoint responses
- Consider: Should we standardize on one DTO model?

### Risk 3: Missing Range Request Support
**Issue:** SpeFileStore's `DownloadFileAsync` doesn't support range requests
**Mitigation:**
- Add `DownloadFileWithRangeAsync` method to DriveItemOperations
- Keep full implementation from OboSpeService (complex logic for 206 responses)

---

## Recommendation

**APPROVE Task 4.4 with REVISED implementation plan.**

**Changes Required:**
1. Update task document to reflect Sprint 3 modular architecture
2. Add Phase 1 (update operation classes) before updating SpeFileStore
3. Create new `UserOperations` class for user info methods
4. Increase effort estimate from 2 days to 1.5-2 days
5. Add integration testing phase

**Priority:** Still P0 Blocker (ADR-007 compliance critical)

**Dependencies:** None (Sprint 3 Task 3.2 already complete)

---

## Next Steps

1. ✅ Review complete - document created
2. ⏭️ Get user approval for revised implementation plan
3. ⏭️ Create updated TASK-4.4-IMPLEMENTATION-PLAN.md
4. ⏭️ Begin Phase 1 implementation

---

**Reviewed By:** Claude (AI Senior .NET Architect)
**Review Date:** 2025-10-02
**Status:** Ready for implementation with revised plan
