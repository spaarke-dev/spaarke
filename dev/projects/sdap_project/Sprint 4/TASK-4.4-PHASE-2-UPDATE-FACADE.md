# Task 4.4 - Phase 2: Update SpeFileStore Facade

**Sprint:** 4
**Phase:** 2 of 7
**Estimated Effort:** 1 hour
**Dependencies:** Phase 1 complete
**Status:** Ready to Implement (Documentation Corrected)
**Source:** Verified against Phase 1 method signatures

---

## Objective

Update `SpeFileStore` to inject `UserOperations` and expose all OBO methods via delegation pattern.

**Architecture:** SpeFileStore acts as single facade for all Graph operations (app-only + OBO), delegating to specialized operation classes.

---

## Step 1: Update Constructor and Fields

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Current constructor:**
```csharp
private readonly ContainerOperations _containerOps;
private readonly DriveItemOperations _driveItemOps;
private readonly UploadSessionManager _uploadManager;

public SpeFileStore(
    ContainerOperations containerOps,
    DriveItemOperations driveItemOps,
    UploadSessionManager uploadManager)
{
    _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
    _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
    _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
}
```

**Updated constructor (add UserOperations):**
```csharp
private readonly ContainerOperations _containerOps;
private readonly DriveItemOperations _driveItemOps;
private readonly UploadSessionManager _uploadManager;
private readonly UserOperations _userOps;  // ADD field

public SpeFileStore(
    ContainerOperations containerOps,
    DriveItemOperations driveItemOps,
    UploadSessionManager uploadManager,
    UserOperations userOps)  // ADD parameter
{
    _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
    _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
    _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
    _userOps = userOps ?? throw new ArgumentNullException(nameof(userOps));  // ADD initialization
}
```

---

## Step 2: Add OBO Delegation Methods

**Add at end of class (before closing brace):**

```csharp
    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
    // All methods delegate to specialized operation classes.
    // These methods accept userToken and use OBO authentication flow.

    // Container Operations (user context)
    public Task<IList<ContainerDto>> ListContainersAsUserAsync(
        string userToken,
        Guid containerTypeId,
        CancellationToken ct = default)
        => _containerOps.ListContainersAsUserAsync(userToken, containerTypeId, ct);

    // Drive Item Operations (user context)
    public Task<ListingResponse> ListChildrenAsUserAsync(
        string userToken,
        string containerId,
        ListingParameters parameters,
        CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsUserAsync(userToken, containerId, parameters, ct);

    public Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
        string userToken,
        string driveId,
        string itemId,
        RangeHeader? range,
        string? ifNoneMatch,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileWithRangeAsUserAsync(userToken, driveId, itemId, range, ifNoneMatch, ct);

    public Task<DriveItemDto?> UpdateItemAsUserAsync(
        string userToken,
        string driveId,
        string itemId,
        UpdateFileRequest request,
        CancellationToken ct = default)
        => _driveItemOps.UpdateItemAsUserAsync(userToken, driveId, itemId, request, ct);

    public Task<bool> DeleteItemAsUserAsync(
        string userToken,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DeleteItemAsUserAsync(userToken, driveId, itemId, ct);

    // Upload Operations (user context)
    public Task<DriveItem?> UploadSmallAsUserAsync(
        string userToken,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.UploadSmallAsUserAsync(userToken, containerId, path, content, ct);

    public Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
        string userToken,
        string driveId,
        string path,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
        => _uploadManager.CreateUploadSessionAsUserAsync(userToken, driveId, path, conflictBehavior, ct);

    public Task<ChunkUploadResponse> UploadChunkAsUserAsync(
        string userToken,
        string uploadSessionUrl,
        string contentRange,
        byte[] chunkData,
        CancellationToken ct = default)
        => _uploadManager.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);

    // User Operations
    public Task<UserInfoResponse?> GetUserInfoAsync(
        string userToken,
        CancellationToken ct = default)
        => _userOps.GetUserInfoAsync(userToken, ct);

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        string userToken,
        string containerId,
        CancellationToken ct = default)
        => _userOps.GetUserCapabilitiesAsync(userToken, containerId, ct);
```

**Key Points:**
- ✅ All methods are one-line delegations using `=>`
- ✅ No business logic in facade (pure delegation)
- ✅ Method signatures match Phase 1 exactly
- ✅ `UploadSmallAsUserAsync` returns `Task<DriveItem?>` (Graph SDK type)
- ✅ `GetUserCapabilitiesAsync` (not "AsUserAsync" - all user ops are OBO by default)

---

## Complete Method Summary

**Total OBO Methods Added:** 11

1. `ListContainersAsUserAsync` - List containers user has access to
2. `ListChildrenAsUserAsync` - List files in container with pagination
3. `DownloadFileWithRangeAsUserAsync` - Download file with range/cache support
4. `UpdateItemAsUserAsync` - Rename/move file
5. `DeleteItemAsUserAsync` - Delete file
6. `UploadSmallAsUserAsync` - Upload file < 4MB
7. `CreateUploadSessionAsUserAsync` - Create chunked upload session
8. `UploadChunkAsUserAsync` - Upload chunk to session
9. `GetUserInfoAsync` - Get current user info (/me)
10. `GetUserCapabilitiesAsync` - Get user permissions for container

---

## Verification

```bash
# Build should succeed
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: 0 errors

# Verify methods added
grep -n "AsUserAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs
# Expected: 11 method definitions

# Verify UserOperations in constructor
grep -n "UserOperations" src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs
# Expected: Field declaration, constructor parameter, constructor initialization
```

---

## Acceptance Criteria

- [ ] Constructor updated with `UserOperations userOps` parameter
- [ ] Private field `_userOps` added and initialized
- [ ] 11 OBO delegation methods added:
  - [ ] ListContainersAsUserAsync
  - [ ] ListChildrenAsUserAsync
  - [ ] DownloadFileWithRangeAsUserAsync
  - [ ] UpdateItemAsUserAsync
  - [ ] DeleteItemAsUserAsync
  - [ ] UploadSmallAsUserAsync
  - [ ] CreateUploadSessionAsUserAsync
  - [ ] UploadChunkAsUserAsync
  - [ ] GetUserInfoAsync
  - [ ] GetUserCapabilitiesAsync
- [ ] Build succeeds with 0 errors
- [ ] All methods follow pattern: one-line delegation with `=>`
- [ ] Return types match Phase 1 method signatures exactly

---

## Architecture Notes

**Before Task 4.4:**
```
Admin Endpoints → SpeFileStore → Operation Classes (app-only)
User Endpoints  → IOboSpeService → OboSpeService (OBO)
```

**After Phase 2:**
```
Admin Endpoints → SpeFileStore → Operation Classes (app-only methods)
User Endpoints  → SpeFileStore → Operation Classes (OBO methods)
                      ↓
              Single Facade, Dual Auth Modes
```

**ADR-007 Compliance:**
- ✅ Single focused facade (SpeFileStore)
- ✅ No interface abstractions
- ✅ Delegates to specialized operation classes
- ✅ Clear separation between app-only and user-context methods

---

## Next Phase

**Phase 3:** Create TokenHelper utility for bearer token extraction (can run in parallel)
