# Phase 2 - Task 2: Update Endpoints to Use SpeFileStore

**Phase**: 2 (Simplify Service Layer)
**Duration**: 2-3 hours
**Risk**: Medium
**Patterns**: [endpoint-file-upload.md](../patterns/endpoint-file-upload.md), [error-handling-standard.md](../patterns/error-handling-standard.md)
**Anti-Patterns**: [anti-pattern-leaking-sdk-types.md](../patterns/anti-pattern-leaking-sdk-types.md)

---

## Current State (Before Starting)

**Current Endpoint Problem**:
- Endpoints inject interfaces: `IResourceStore`, `ISpeService`
- Endpoints pass requests through 6-layer call chain
- Endpoints may receive or return SDK types (DriveItem)
- Hard to understand flow: Follow dependency chain across multiple files

**Code Impact**:
- Method call overhead: 50-100ms per request
- Debugging difficulty: Step through 6 files to trace one operation
- Change ripple: Modify interface + 2 implementations + endpoints
- Testing complexity: Mock multiple layers

**Quick Verification**:
```bash
# Check current endpoint dependencies
grep -n "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Api/OBOEndpoints.cs

# Should see interface injection
# If you see "SpeFileStore" - task already done!
```

---

## Background: Why Endpoints Use Interfaces

**Historical Context**:
- Endpoints followed "Dependency Inversion Principle" strictly
- Pattern: "Depend on abstractions, not concretions"
- Assumption: "Interfaces make code testable"
- Each service layer got its own interface "for testability"

**How Endpoints Evolved**:
1. **Initial**: Direct Graph SDK calls in endpoints (coupled)
2. **V2**: Created IResourceStore to abstract storage (better)
3. **V3**: Added ISpeService for SPE-specific logic (over-abstracted)
4. **V4**: Added more interfaces for "future flexibility" (over-engineered)

**Why This Seemed Correct**:
- Textbook SOLID principles: "Program to interfaces"
- Test mocking: "Need interfaces to create mocks"
- Flexibility: "Might swap implementations later"
- Clean Architecture: "Ports and adapters pattern"

**What Changed Our Understanding**:
- **ADR-010 (DI Minimalism)**: Interfaces only for factories/collections
- Testing evolution: Mock at infrastructure boundaries (IGraphClientFactory), not every layer
- YAGNI realized: "Future flexibility" never used (single implementation for 2 years)
- Performance cost: Interface dispatch + call chain = measurable overhead

**Why Direct Injection is Correct**:
- **Still testable**: Mock IGraphClientFactory (infrastructure boundary)
- **Better performance**: Direct method calls, no interface dispatch
- **Easier to understand**: See concrete implementation, not abstraction
- **Faster refactoring**: Change one class, not interface + implementation
- **Type safety**: Compiler knows concrete types, better IntelliSense

**Real Example**:
```csharp
// ❌ OLD: Program to interface (over-abstraction)
private static async Task<IResult> UploadFile(IResourceStore store, ...)
{
    var result = await store.UploadAsync(...);  // What implementation? Who knows!
    return Results.Ok(result);  // What type? Unknown without checking interface!
}

// ✅ NEW: Inject concrete (clear, direct)
private static async Task<IResult> UploadFile(SpeFileStore fileStore, ...)
{
    FileUploadResult result = await fileStore.UploadFileAsync(...);  // Clear what it does!
    return Results.Ok(result);  // Clear return type! IDE shows properties!
}
```

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically updating endpoints to use the new SpeFileStore concrete class.

TASK: Update OBOEndpoints.cs, DocumentsEndpoints.cs, and UploadEndpoints.cs to inject and use SpeFileStore instead of IResourceStore/ISpeService.

CONSTRAINTS:
- Must inject SpeFileStore directly (concrete class, not interface)
- Must return DTOs from endpoints (FileUploadResult, FileDownloadResult)
- Must NOT change API contracts (URL routes, request/response shapes)
- Must preserve existing authorization and validation logic

VERIFICATION BEFORE STARTING:
1. Verify Phase 2 Task 1 complete (SpeFileStore.cs exists)
2. Verify DTOs exist (FileUploadResult.cs, FileDownloadResult.cs, FileMetadata.cs)
3. Verify current endpoints use IResourceStore or ISpeService
4. If any verification fails, STOP and complete Phase 2 Task 1 first

FOCUS: Stay focused on updating endpoint injection and method calls only. Do NOT update DI registrations (that's Task 2.3) or write tests (that's Task 2.4).
```

---

## Goal

Update all SPE-related endpoints to inject **SpeFileStore** directly and return DTOs instead of Graph SDK types.

**Problem**:
- Endpoints currently inject interfaces (IResourceStore, ISpeService)
- Endpoints may return Graph SDK types (DriveItem)
- 6-layer call chain

**Target**:
- Endpoints inject SpeFileStore (concrete class)
- Endpoints return DTOs (FileUploadResult, FileDownloadResult)
- 3-layer call chain (Endpoint → SpeFileStore → Graph)

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 2 Task 1 complete
- [ ] Check SpeFileStore exists
ls src/api/Spe.Bff.Api/Storage/SpeFileStore.cs

- [ ] Check DTOs exist
ls src/api/Spe.Bff.Api/Models/FileUploadResult.cs
ls src/api/Spe.Bff.Api/Models/FileDownloadResult.cs
ls src/api/Spe.Bff.Api/Models/FileMetadata.cs

# 2. Identify endpoints to update
- [ ] Find files using IResourceStore or ISpeService
grep -r "IResourceStore\|ISpeService" src/api/Spe.Bff.Api/Api/

# 3. Document current endpoint count
- [ ] Count endpoints to update: _____
```

**If any verification fails**: STOP and complete Phase 2 Task 1 first.

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Api/OBOEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs
- [ ] src/api/Spe.Bff.Api/Api/UploadEndpoints.cs
```

---

## Implementation

### Step 1: Update OBOEndpoints.cs

**File**: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

#### Before (OLD - with interfaces):
```csharp
public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/obo/upload", UploadFile)
            .RequireAuthorization()
            .DisableAntiforgery();

        app.MapGet("/api/obo/download/{id}", DownloadFile)
            .RequireAuthorization();

        return app;
    }

    // ❌ OLD: Injects interface, may return DriveItem
    private static async Task<IResult> UploadFile(
        IResourceStore resourceStore,  // ❌ Interface
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();
        var fileName = request.Query["fileName"].ToString();

        if (string.IsNullOrEmpty(containerId) || string.IsNullOrEmpty(fileName))
            return Results.BadRequest("containerId and fileName are required");

        var token = ExtractBearerToken(request);
        var result = await resourceStore.UploadAsync(containerId, fileName, request.Body, token);

        return Results.Ok(result); // May be DriveItem
    }

    // ❌ OLD: Similar issues
    private static async Task<IResult> DownloadFile(
        IResourceStore resourceStore,  // ❌ Interface
        string id,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();
        var token = ExtractBearerToken(request);

        var result = await resourceStore.DownloadAsync(containerId, id, token);
        return Results.File(result.Content, result.ContentType, result.FileName);
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

#### After (NEW - with SpeFileStore):
```csharp
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Api;

public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/obo/upload", UploadFile)
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithName("UploadFileOBO")
            .WithTags("OBO Operations");

        app.MapGet("/api/obo/download/{id}", DownloadFile)
            .RequireAuthorization()
            .WithName("DownloadFileOBO")
            .WithTags("OBO Operations");

        return app;
    }

    // ✅ NEW: Injects SpeFileStore (concrete), returns FileUploadResult DTO
    private static async Task<IResult> UploadFile(
        SpeFileStore fileStore,  // ✅ Concrete class
        HttpRequest request,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        // Validate query parameters
        var containerId = request.Query["containerId"].ToString();
        var fileName = request.Query["fileName"].ToString();

        if (string.IsNullOrEmpty(containerId))
            return Results.BadRequest(new { Error = "containerId is required" });

        if (string.IsNullOrEmpty(fileName))
            return Results.BadRequest(new { Error = "fileName is required" });

        if (request.Body == null || request.ContentLength == 0)
            return Results.BadRequest(new { Error = "Request body cannot be empty" });

        try
        {
            // Extract user token
            var userToken = ExtractBearerToken(request);

            // Upload via SpeFileStore
            var result = await fileStore.UploadFileAsync(
                containerId,
                fileName,
                request.Body,
                userToken,
                cancellationToken);

            // ✅ Returns FileUploadResult DTO
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid upload request");
            return Results.BadRequest(new { Error = ex.Message });
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

    // ✅ NEW: Uses SpeFileStore, returns FileDownloadResult DTO
    private static async Task<IResult> DownloadFile(
        SpeFileStore fileStore,  // ✅ Concrete class
        string id,
        HttpRequest request,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        // Validate parameters
        var containerId = request.Query["containerId"].ToString();

        if (string.IsNullOrEmpty(containerId))
            return Results.BadRequest(new { Error = "containerId is required" });

        if (string.IsNullOrEmpty(id))
            return Results.BadRequest(new { Error = "id is required" });

        try
        {
            // Extract user token
            var userToken = ExtractBearerToken(request);

            // Download via SpeFileStore
            var result = await fileStore.DownloadFileAsync(
                containerId,
                id,
                userToken,
                cancellationToken);

            // ✅ Returns FileDownloadResult DTO as file stream
            return Results.File(
                result.Content,
                result.ContentType,
                result.FileName,
                enableRangeProcessing: true);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Invalid download request");
            return Results.BadRequest(new { Error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized download attempt");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error downloading file {ItemId}", id);
            return Results.Problem(
                title: "Download failed",
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

### Step 2: Update DocumentsEndpoints.cs

**File**: `src/api/Spe.Bff.Api/Api/DocumentsEndpoints.cs`

```csharp
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Api;

public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documents/{id}/metadata", GetMetadata)
            .RequireAuthorization()
            .WithName("GetDocumentMetadata")
            .WithTags("Documents");

        app.MapDelete("/api/documents/{id}", DeleteDocument)
            .RequireAuthorization()
            .WithName("DeleteDocument")
            .WithTags("Documents");

        app.MapGet("/api/documents", ListDocuments)
            .RequireAuthorization()
            .WithName("ListDocuments")
            .WithTags("Documents");

        return app;
    }

    // ✅ NEW: Uses SpeFileStore, returns FileMetadata DTO
    private static async Task<IResult> GetMetadata(
        SpeFileStore fileStore,
        string id,
        HttpRequest request,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();

        if (string.IsNullOrEmpty(containerId))
            return Results.BadRequest(new { Error = "containerId is required" });

        if (string.IsNullOrEmpty(id))
            return Results.BadRequest(new { Error = "id is required" });

        try
        {
            var userToken = ExtractBearerToken(request);

            var metadata = await fileStore.GetFileMetadataAsync(
                containerId,
                id,
                userToken,
                cancellationToken);

            return Results.Ok(metadata);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized metadata request");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting metadata for {ItemId}", id);
            return Results.Problem(
                title: "Failed to get metadata",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ✅ NEW: Uses SpeFileStore
    private static async Task<IResult> DeleteDocument(
        SpeFileStore fileStore,
        string id,
        HttpRequest request,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();

        if (string.IsNullOrEmpty(containerId))
            return Results.BadRequest(new { Error = "containerId is required" });

        if (string.IsNullOrEmpty(id))
            return Results.BadRequest(new { Error = "id is required" });

        try
        {
            var userToken = ExtractBearerToken(request);

            await fileStore.DeleteFileAsync(
                containerId,
                id,
                userToken,
                cancellationToken);

            return Results.NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized delete request");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting file {ItemId}", id);
            return Results.Problem(
                title: "Delete failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    // ✅ NEW: Uses SpeFileStore, returns IEnumerable<FileMetadata>
    private static async Task<IResult> ListDocuments(
        SpeFileStore fileStore,
        HttpRequest request,
        ILogger<SpeFileStore> logger,
        CancellationToken cancellationToken)
    {
        var containerId = request.Query["containerId"].ToString();
        var folderPath = request.Query["folderPath"].ToString();

        if (string.IsNullOrEmpty(containerId))
            return Results.BadRequest(new { Error = "containerId is required" });

        try
        {
            var userToken = ExtractBearerToken(request);

            var files = await fileStore.ListFilesAsync(
                containerId,
                folderPath,
                userToken,
                cancellationToken);

            return Results.Ok(files);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Unauthorized list request");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing files in container {ContainerId}", containerId);
            return Results.Problem(
                title: "List failed",
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

### Step 3: Update UploadEndpoints.cs (if exists)

**File**: `src/api/Spe.Bff.Api/Api/UploadEndpoints.cs`

**Note**: If this file uses UploadSessionManager instead of SpeFileStore, update injection only:

```csharp
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure;

namespace Spe.Bff.Api.Api;

public static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/upload-sessions", CreateUploadSession)
            .RequireAuthorization()
            .WithName("CreateUploadSession")
            .WithTags("Upload Sessions");

        app.MapPost("/api/upload-sessions/{sessionId}/chunks", UploadChunk)
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithName("UploadChunk")
            .WithTags("Upload Sessions");

        return app;
    }

    // UploadSessionManager handles large file uploads
    // SpeFileStore handles simple file uploads
    // Keep UploadSessionManager for chunked uploads
    private static async Task<IResult> CreateUploadSession(
        UploadSessionManager sessionManager,  // Keep as-is
        HttpRequest request,
        ILogger<UploadSessionManager> logger,
        CancellationToken cancellationToken)
    {
        // Implementation remains the same
        // UploadSessionManager already uses IGraphClientFactory internally
        // No changes needed for this endpoint
    }
}
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Code Review Checklist
```bash
# Verify endpoints inject SpeFileStore (not interfaces)
- [ ] OBOEndpoints uses SpeFileStore
- [ ] DocumentsEndpoints uses SpeFileStore
- [ ] UploadEndpoints uses UploadSessionManager (keep as-is)

# Verify no interface references remain
grep -r "IResourceStore\|ISpeService\|IOboSpeService" src/api/Spe.Bff.Api/Api/
# Expected: No results

# Verify DTOs in return types
grep -E "FileUploadResult|FileDownloadResult|FileMetadata" src/api/Spe.Bff.Api/Api/*.cs
# Expected: Should see DTO types

# Verify no SDK types in endpoint signatures
grep -E "DriveItem|Entity" src/api/Spe.Bff.Api/Api/*.cs
# Expected: No results in public endpoints
```

### Manual Testing (if API is running)
```bash
# Test upload
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test.txt" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: text/plain" \
  -d "test content"

# Expected: 200 OK with FileUploadResult JSON

# Test download
curl -X GET "https://localhost:5001/api/obo/download/itemId?containerId=xxx" \
  -H "Authorization: Bearer $TOKEN"

# Expected: 200 OK with file stream

# Test metadata
curl -X GET "https://localhost:5001/api/documents/itemId/metadata?containerId=xxx" \
  -H "Authorization: Bearer $TOKEN"

# Expected: 200 OK with FileMetadata JSON
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 2 Task 1 complete (SpeFileStore exists)
- [ ] **Pre-flight**: Verified DTOs exist (FileUploadResult, FileDownloadResult, FileMetadata)
- [ ] Updated `OBOEndpoints.cs` to inject SpeFileStore
- [ ] Updated `DocumentsEndpoints.cs` to inject SpeFileStore
- [ ] Updated `UploadEndpoints.cs` (if needed)
- [ ] All endpoints inject concrete SpeFileStore (not interfaces)
- [ ] All endpoints return DTOs (not SDK types)
- [ ] Added proper error handling (try-catch blocks)
- [ ] Added logging for errors and warnings
- [ ] Added parameter validation
- [ ] API contracts unchanged (routes, request/response shapes)
- [ ] Build succeeds: `dotnet build`
- [ ] No references to IResourceStore, ISpeService, IOboSpeService

---

## Expected Results

**Before**:
- ❌ Endpoints inject interfaces (IResourceStore, ISpeService)
- ❌ May return SDK types (DriveItem)
- ❌ 6-layer call chain

**After**:
- ✅ Endpoints inject SpeFileStore (concrete class)
- ✅ Return DTOs (FileUploadResult, FileDownloadResult, FileMetadata)
- ✅ 3-layer call chain (Endpoint → SpeFileStore → Graph)

---

## Anti-Pattern Verification

### ✅ Avoided: Leaking SDK Types
```bash
# Verify no DriveItem in endpoint return types
grep -E "Task<.*DriveItem.*>" src/api/Spe.Bff.Api/Api/*.cs
# Expected: No results ✅
```

**Why**: ADR-007 - Never expose Graph SDK types in API contracts

### ✅ Avoided: Interface Proliferation
```bash
# Verify no interface injection
grep -E "IResourceStore|ISpeService" src/api/Spe.Bff.Api/Api/*.cs
# Expected: No results ✅
```

**Why**: ADR-010 - Register concrete classes, avoid unnecessary interfaces

---

## Troubleshooting

### Issue: Build fails with "SpeFileStore not found"

**Cause**: DI not yet configured

**Fix**: This is expected! DI registration happens in Task 2.3. For now, endpoints won't resolve at runtime but should compile.

### Issue: Endpoints return 500 when tested

**Cause**: SpeFileStore not registered in DI container

**Fix**: Complete Task 2.3 (Update DI Registrations) before testing endpoints.

### Issue: Type mismatch on Results.Ok(result)

**Cause**: Endpoint expecting different return type

**Fix**: Ensure endpoint returns `IResult` and uses DTOs:
```csharp
private static async Task<IResult> UploadFile(...)
{
    FileUploadResult result = await fileStore.UploadFileAsync(...);
    return Results.Ok(result); // ✅ Returns DTO as JSON
}
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ All endpoints updated to inject SpeFileStore
- [ ] ✅ All endpoints return DTOs (not SDK types)
- [ ] ✅ API contracts unchanged (routes, shapes)
- [ ] ✅ Task stayed focused (did NOT update DI - that's Task 2.3)
- [ ] ✅ Build succeeds

**If any item unchecked**: Review and fix before proceeding to Task 2.3

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Api/
git commit -m "refactor(endpoints): inject SpeFileStore concrete class per ADR-010

- Update OBOEndpoints to use SpeFileStore (not IResourceStore)
- Update DocumentsEndpoints to use SpeFileStore (not ISpeService)
- Update UploadEndpoints (keep UploadSessionManager for chunked uploads)
- All endpoints now return DTOs (FileUploadResult, FileDownloadResult, FileMetadata)
- Add proper error handling and logging
- Add parameter validation

Reduces call chain: 6 layers → 3 layers (Endpoint → SpeFileStore → Graph)
ADR Compliance: ADR-007 (DTOs only), ADR-010 (Concrete classes)
Anti-Patterns Avoided: Interface proliferation, SDK type leakage
Task: Phase 2, Task 2"
```

---

## Next Task

⚠️ **IMPORTANT**: Endpoints are updated but SpeFileStore is NOT registered in DI yet!

➡️ [Phase 2 - Task 3: Update DI Registrations](phase-2-task-3-update-di.md)

**What's next**: Register SpeFileStore as concrete class in DI container

---

## Related Resources

- **Patterns**:
  - [endpoint-file-upload.md](../patterns/endpoint-file-upload.md)
  - [error-handling-standard.md](../patterns/error-handling-standard.md)
- **Anti-Patterns**:
  - [anti-pattern-leaking-sdk-types.md](../patterns/anti-pattern-leaking-sdk-types.md)
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#solution-1-simplified-service-layer)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-007, ADR-010
