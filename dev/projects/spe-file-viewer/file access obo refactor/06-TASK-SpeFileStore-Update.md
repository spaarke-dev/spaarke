# Task 06: Update SpeFileStore Methods (Optional)

**Task ID**: `06-SpeFileStore-Update`
**Estimated Time**: 30 minutes
**Status**: Not Started (Optional)
**Dependencies**: 03-IGraphClientFactory, 04-GraphClientFactory-Implementation

---

## 📋 Prompt

**OPTIONAL TASK**: Update `SpeFileStore.cs` to accept `HttpContext` and use OBO authentication for file operations. This is NOT required for FileAccessEndpoints (Task 05 calls Graph API directly), but would future-proof SpeFileStore for other callers.

---

## ⚠️ Important Note

**This task is OPTIONAL for the file access refactor**. The FileAccessEndpoints (Task 05) no longer use SpeFileStore - they call Graph API directly using `IGraphClientFactory.ForUserAsync`.

**Consider skipping this task if**:
- You want to minimize scope and ship quickly
- SpeFileStore is only used by legacy endpoints
- You prefer to refactor SpeFileStore later when needed

**Complete this task if**:
- You want to refactor SpeFileStore for future OBO usage
- Other endpoints besides FileAccessEndpoints use SpeFileStore
- You want consistency across all file operations

---

## ✅ Todos

- [ ] Decide if this task is needed (check SpeFileStore usage)
- [ ] Open `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`
- [ ] Update method signatures to accept `HttpContext` parameter
- [ ] Replace `_graphClientFactory.CreateAppOnlyClient()` with `ForUserAsync(ctx)`
- [ ] Update all callers (find with `Grep` or IDE search)
- [ ] Build and test

---

## 📚 Required Knowledge

### Current SpeFileStore Implementation
SpeFileStore currently uses **app-only authentication**:

```csharp
public class SpeFileStore
{
    private readonly IGraphClientFactory _graphClientFactory;

    public async Task<DriveItem?> GetFileAsync(string containerId, string path, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.CreateAppOnlyClient();  // App-only
        // ... Graph API calls
    }
}
```

### Proposed Changes
Accept `HttpContext` and use OBO:

```csharp
public async Task<DriveItem?> GetFileAsync(
    HttpContext ctx,  // NEW parameter
    string containerId,
    string path,
    CancellationToken ct)
{
    var graphClient = await _graphClientFactory.ForUserAsync(ctx, ct);  // OBO
    // ... Graph API calls
}
```

---

## 📂 Related Files

**File to Modify**:
- [src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs](../../../src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs)

**Files That May Call SpeFileStore** (check with Grep):
- `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs` (already uses OBO manually)
- Legacy endpoints (if any)

---

## 🎯 Implementation Steps

### 1. Find SpeFileStore Usage

```bash
cd c:/code_files/spaarke
grep -r "SpeFileStore" src/api/Spe.Bff.Api --include="*.cs"
```

**If no results besides SpeFileStore.cs itself**: Skip this task (no callers).

**If OBOEndpoints.cs is the only caller**: OBOEndpoints already extracts tokens manually, so this task is low priority.

### 2. Update Method Signatures (Example)

**Before**:
```csharp
public async Task<DriveItem?> GetFileAsync(string containerId, string path, CancellationToken ct)
{
    var graphClient = _graphClientFactory.CreateAppOnlyClient();
    // ...
}
```

**After**:
```csharp
public async Task<DriveItem?> GetFileAsync(
    HttpContext ctx,
    string containerId,
    string path,
    CancellationToken ct)
{
    var graphClient = await _graphClientFactory.ForUserAsync(ctx, ct);
    // ...
}
```

### 3. Update All Methods

Apply the same pattern to:
- `GetFileAsync`
- `CreateFolderAsync`
- `UploadSmallAsUserAsync` (may already accept user token)
- `UploadLargeAsUserAsync` (may already accept user token)
- Any other methods using `CreateAppOnlyClient()`

### 4. Update Callers

Example caller update (if OBOEndpoints.cs uses SpeFileStore):

**Before**:
```csharp
var item = await speFileStore.GetFileAsync(containerId, path, ct);
```

**After**:
```csharp
var item = await speFileStore.GetFileAsync(ctx, containerId, path, ct);
```

---

## ✅ Acceptance Criteria

### Build Success
- [ ] Project builds without errors
- [ ] All callers updated

### Code Quality
- [ ] All methods use `ForUserAsync` (not `CreateAppOnlyClient`)
- [ ] HttpContext parameter added consistently
- [ ] XML documentation updated

### Testing
1. Build: `dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`
2. If OBOEndpoints.cs uses SpeFileStore, test an OBO endpoint
3. Verify file operations still work

---

## 📝 Notes

### Why This Task Is Optional

FileAccessEndpoints (Task 05) **do not use SpeFileStore**. They call Graph API directly:

```csharp
// FileAccessEndpoints.cs (Task 05)
var graphClient = await graphFactory.ForUserAsync(context, ct);
var previewResponse = await graphClient.Drives[driveId].Items[itemId].Preview.PostAsync(...);
```

SpeFileStore is only needed if:
1. Other endpoints use it
2. You want to centralize file operations

### Alternative: Keep SpeFileStore App-Only

If SpeFileStore is only used for **admin operations** (container creation, background jobs), it's correct to keep it using app-only authentication. In that case:

1. Skip this task
2. Rename methods to clarify intent: `GetFileAsAppAsync`
3. Create separate methods for user operations: `GetFileAsUserAsync`

---

## 🔗 Decision Point

**Before starting this task**, run:

```bash
cd c:/code_files/spaarke
grep -r "SpeFileStore" src/api/Spe.Bff.Api/Api --include="*.cs"
```

**If no results**: Skip this task entirely.

**If results exist**: Review each caller and decide if OBO is needed.

---

**Previous Task**: [05-TASK-FileAccessEndpoints-Refactor.md](./05-TASK-FileAccessEndpoints-Refactor.md)
**Next Task**: [07-TASK-DocumentStorageResolver-Validation.md](./07-TASK-DocumentStorageResolver-Validation.md)
