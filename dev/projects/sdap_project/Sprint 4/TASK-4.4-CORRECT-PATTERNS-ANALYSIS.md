# Task 4.4 - Correct Implementation Patterns from OboSpeService

**Date:** October 2, 2025
**Purpose:** Document authoritative patterns from OboSpeService for correct Phase documentation
**Status:** Senior-level code review complete

---

## Executive Summary

Conducted thorough review of [OboSpeService.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs) to extract correct implementation patterns. The phase documentation I created contains **multiple critical errors** that would have caused compilation failures and incorrect behavior.

**Key Finding:** Phase documentation used "vibe coding" patterns instead of actual codebase patterns.

---

## Critical Errors Found in Phase Documentation

### ❌ Error 1: Incorrect DTO Initialization Syntax

**Phase Docs (WRONG):**
```csharp
new DriveItemDto {
    Id = item.Id ?? string.Empty,
    Name = item.Name ?? string.Empty,
    Size = item.Size ?? 0,
    CreatedDateTime = item.CreatedDateTime ?? DateTimeOffset.MinValue,
    WebUrl = item.WebUrl ?? string.Empty,
    IsFolder = item.Folder != null
}
```

**Actual Codebase (CORRECT):**
```csharp
new DriveItemDto(
    Id: item.Id!,
    Name: item.Name!,
    Size: item.Size,
    ETag: item.ETag,
    LastModifiedDateTime: item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
    ContentType: item.File?.MimeType,
    Folder: item.Folder != null ? new FolderDto(item.Folder.ChildCount) : null
)
```

**Why This Matters:**
- DTOs are C# 9 records with positional parameters, not classes with property initializers
- Missing required parameters cause compilation errors
- Wrong properties (CreatedDateTime, WebUrl, IsFolder don't exist in DriveItemDto)

---

### ❌ Error 2: Incorrect ListingResponse Structure

**Phase Docs (WRONG):**
```csharp
return new ListingResponse
{
    Items = items,
    TotalCount = items.Count,
    Top = parameters.Top,
    Skip = parameters.Skip
};
```

**Actual Codebase (CORRECT):**
```csharp
return new ListingResponse(items, nextLink);
```

**Why This Matters:**
- ListingResponse is a record with constructor: `ListingResponse(IList<DriveItemDto> Items, string? NextLink)`
- There is NO TotalCount, Top, or Skip property
- Phase docs invented properties that don't exist

---

### ❌ Error 3: Incorrect FileContentResponse Structure

**Phase Docs (WRONG):**
```csharp
return new FileContentResponse
{
    Content = contentStream,
    ContentType = driveItem.File.MimeType ?? "application/octet-stream",
    ContentLength = contentLength,
    ETag = driveItem.ETag ?? string.Empty,
    FileName = driveItem.Name ?? "download",
    IsRangeRequest = isRangeRequest,
    ContentRangeHeader = contentRangeHeader
};
```

**Actual Codebase (CORRECT):**
```csharp
return new FileContentResponse(
    Content: contentStream,
    ContentLength: contentLength,
    ContentType: contentType,
    ETag: item.ETag,
    RangeStart: rangeStart,
    RangeEnd: rangeEnd,
    TotalSize: totalSize
);
```

**Why This Matters:**
- Constructor signature: `FileContentResponse(Stream Content, long ContentLength, string ContentType, string? ETag, long? RangeStart, long? RangeEnd, long? TotalSize)`
- There is NO FileName property
- IsRangeRequest and ContentRangeHeader are computed properties, not constructor parameters
- Range data stored as Start/End/Total, not as header string

---

### ❌ Error 4: Incorrect Graph API Call Pattern

**Phase Docs (WRONG):**
```csharp
var children = await graphClient.Drives[drive.Id].Root.Children
    .GetAsync(requestConfig =>
    {
        requestConfig.QueryParameters.Top = parameters.Top;
        requestConfig.QueryParameters.Skip = parameters.Skip;
        requestConfig.QueryParameters.Orderby = new[] { $"{parameters.OrderBy} {parameters.OrderDir}" };
    }, ct);
```

**Actual Codebase (CORRECT):**
```csharp
var children = await graph.Drives[drive.Id].Items
    .GetAsync(requestConfiguration =>
    {
        requestConfiguration.QueryParameters.Filter = "parentReference/path eq '/drive/root:'";
        requestConfiguration.QueryParameters.Top = parameters.ValidatedTop;
        requestConfiguration.QueryParameters.Skip = parameters.ValidatedSkip;

        var orderField = parameters.ValidatedOrderBy.ToLowerInvariant() switch
        {
            "name" => "name",
            "lastmodifieddatetime" => "lastModifiedDateTime",
            "size" => "size",
            _ => "name"
        };
        var orderDirection = parameters.ValidatedOrderDir == "desc" ? " desc" : " asc";
        requestConfiguration.QueryParameters.Orderby = new[] { orderField + orderDirection };
    }, cancellationToken: ct);
```

**Why This Matters:**
- Use `.Items` collection, NOT `.Root.Children` (Root.Children doesn't exist in this context)
- Must add Filter query parameter to get root items
- Use `parameters.ValidatedTop` not `parameters.Top` (validates bounds)
- OrderBy needs field mapping (name, lastmodifieddatetime, size)
- Order direction must be separate (" desc" or " asc" suffix)

---

### ❌ Error 5: Missing Validation Logic

**Phase Docs:** No validation
**Actual Codebase (CORRECT):**
```csharp
// UpdateItemAsync
if (!FileOperationExtensions.IsValidItemId(itemId))
{
    _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
    return null;
}

if (!string.IsNullOrEmpty(request.Name) && !FileOperationExtensions.IsValidFileName(request.Name))
{
    _logger.LogWarning("Invalid file name: {Name}", request.Name);
    return null;
}

// UploadSmallAsync
if (content.CanSeek && content.Length > 4 * 1024 * 1024)
{
    _logger.LogWarning("Content too large for small upload: {Size} bytes (max 4MB)", content.Length);
    throw new ArgumentException("Content size exceeds 4MB limit for small uploads. Use chunked upload instead.");
}
```

**Why This Matters:**
- Production code validates all inputs before Graph API calls
- Missing validation = security vulnerability
- Phase docs completely omitted this critical pattern

---

### ❌ Error 6: Incomplete Error Handling

**Phase Docs:** Only catches generic ServiceException
**Actual Codebase (CORRECT):**
```csharp
catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
{
    _logger.LogWarning("Item not found: {ItemId}", itemId);
    return null;
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
{
    _logger.LogWarning("Access denied to item {ItemId}: {Error}", itemId, ex.Message);
    throw new UnauthorizedAccessException($"Access denied to item {itemId}", ex);
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
{
    _logger.LogWarning("Graph API throttling, retry after {RetryAfter}s",
        ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60);
    throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Failed to delete item {ItemId}", itemId);
    throw;
}
```

**Why This Matters:**
- Production code handles 404, 403, 413, 416, 429 specifically
- Different exceptions for different HTTP status codes
- Phase docs had generic catch-all only

---

## Correct Patterns Extracted

### Pattern 1: DriveItemDto Constructor

**Signature:**
```csharp
public record DriveItemDto(
    string Id,
    string Name,
    long? Size,
    string? ETag,
    DateTimeOffset? LastModifiedDateTime,
    string? ContentType,
    FolderDto? Folder
);
```

**Usage:**
```csharp
var dto = new DriveItemDto(
    Id: item.Id!,
    Name: item.Name!,
    Size: item.Size,
    ETag: item.ETag,
    LastModifiedDateTime: item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
    ContentType: item.File?.MimeType,
    Folder: item.Folder != null ? new FolderDto(item.Folder.ChildCount) : null
);
```

---

### Pattern 2: ListingResponse Constructor

**Signature:**
```csharp
public record ListingResponse(
    IList<DriveItemDto> Items,
    string? NextLink
);
```

**Usage:**
```csharp
// Empty response
return new ListingResponse(new List<DriveItemDto>(), null);

// With items and pagination
string? nextLink = null;
if (!string.IsNullOrEmpty(children.OdataNextLink))
{
    var nextSkip = parameters.ValidatedSkip + parameters.ValidatedTop;
    nextLink = $"/api/obo/containers/{containerId}/children?top={parameters.ValidatedTop}&skip={nextSkip}";
}
return new ListingResponse(items, nextLink);
```

---

### Pattern 3: FileContentResponse Constructor

**Signature:**
```csharp
public record FileContentResponse(
    Stream Content,
    long ContentLength,
    string ContentType,
    string? ETag,
    long? RangeStart = null,
    long? RangeEnd = null,
    long? TotalSize = null
);
```

**Usage (Full Content):**
```csharp
return new FileContentResponse(
    Content: contentStream,
    ContentLength: totalSize,
    ContentType: contentType,
    ETag: item.ETag
);
```

**Usage (Partial Content/Range):**
```csharp
return new FileContentResponse(
    Content: contentStream,
    ContentLength: contentLength,
    ContentType: contentType,
    ETag: item.ETag,
    RangeStart: rangeStart,
    RangeEnd: rangeEnd,
    TotalSize: totalSize
);
```

**Usage (304 Not Modified):**
```csharp
return new FileContentResponse(
    Content: Stream.Null,
    ContentLength: 0,
    ContentType: item.File?.MimeType ?? "application/octet-stream",
    ETag: item.ETag
);
```

---

### Pattern 4: UploadSessionResponse Constructor

**Signature:**
```csharp
public record UploadSessionResponse(
    string UploadUrl,
    DateTimeOffset ExpirationDateTime
);
```

**Usage:**
```csharp
return new UploadSessionResponse(
    session.UploadUrl,
    session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(1)
);
```

---

### Pattern 5: ChunkUploadResponse Constructor

**Signature:**
```csharp
public record ChunkUploadResponse(
    int StatusCode,
    DriveItemDto? CompletedItem = null
);
```

**Usage:**
```csharp
// More chunks expected
return new ChunkUploadResponse(202);

// Upload complete with item
return new ChunkUploadResponse(201, completedItemDto);

// Error
return new ChunkUploadResponse(400);
```

---

### Pattern 6: UserInfoResponse Constructor

**Signature:**
```csharp
public record UserInfoResponse(
    string DisplayName,
    string UserPrincipalName,
    string Oid
);
```

**Usage:**
```csharp
return new UserInfoResponse(
    DisplayName: user.DisplayName ?? "Unknown User",
    UserPrincipalName: user.UserPrincipalName ?? "unknown@domain.com",
    Oid: user.Id
);
```

---

### Pattern 7: UserCapabilitiesResponse Constructor

**Signature:**
```csharp
public record UserCapabilitiesResponse(
    bool Read,
    bool Write,
    bool Delete,
    bool CreateFolder
);
```

**Usage:**
```csharp
return new UserCapabilitiesResponse(
    Read: hasAccess,
    Write: hasAccess,
    Delete: hasAccess,
    CreateFolder: hasAccess
);
```

---

## Correct Error Handling Pattern

```csharp
try
{
    var graph = await _factory.CreateOnBehalfOfClientAsync(userToken);

    // Operation logic
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
{
    _logger.LogWarning("Resource not found: {Resource}", resourceId);
    return null; // or appropriate response
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
{
    _logger.LogWarning("Access denied to {Resource}: {Error}", resourceId, ex.Message);
    throw new UnauthorizedAccessException($"Access denied to {resourceId}", ex);
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 413)
{
    _logger.LogWarning("Content too large");
    throw new ArgumentException("Content size exceeds limit", ex);
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 416)
{
    _logger.LogWarning("Range not satisfiable");
    return null;
}
catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
{
    _logger.LogWarning("Graph API throttling, retry after {RetryAfter}s",
        ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60);
    throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Operation failed for {Resource}", resourceId);
    throw;
}
```

---

## Correct Validation Pattern

```csharp
// Validate item ID
if (!FileOperationExtensions.IsValidItemId(itemId))
{
    _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
    return null; // or throw ArgumentException
}

// Validate file name
if (!string.IsNullOrEmpty(fileName) && !FileOperationExtensions.IsValidFileName(fileName))
{
    _logger.LogWarning("Invalid file name: {Name}", fileName);
    return null; // or throw ArgumentException
}

// Validate content size for small upload
if (content.CanSeek && content.Length > 4 * 1024 * 1024)
{
    _logger.LogWarning("Content too large for small upload: {Size} bytes (max 4MB)", content.Length);
    throw new ArgumentException("Content size exceeds 4MB limit. Use chunked upload.");
}
```

---

## Method Signatures to Implement

### ContainerOperations

```csharp
public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
    string userToken,
    Guid containerTypeId,
    CancellationToken ct = default)
```

### DriveItemOperations

```csharp
public async Task<ListingResponse> ListChildrenAsUserAsync(
    string userToken,
    string containerId,
    ListingParameters parameters,
    CancellationToken ct = default)

public async Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    RangeHeader? range,
    string? ifNoneMatch,
    CancellationToken ct = default)

public async Task<DriveItemDto?> UpdateItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    UpdateFileRequest request,
    CancellationToken ct = default)

public async Task<bool> DeleteItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    CancellationToken ct = default)
```

### UploadSessionManager

```csharp
public async Task<DriveItem?> UploadSmallAsUserAsync(
    string userToken,
    string containerId,
    string path,
    Stream content,
    CancellationToken ct = default)

public async Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
    string userToken,
    string driveId,
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

### UserOperations (NEW)

```csharp
public async Task<UserInfoResponse?> GetUserInfoAsync(
    string userToken,
    CancellationToken ct = default)

public async Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
    string userToken,
    string containerId,
    CancellationToken ct = default)
```

---

## Impact Assessment

### Documentation Files Requiring Updates

1. ✅ **TASK-4.4-PHASE-1-ADD-OBO-METHODS.md** - Multiple errors (DTOs, Graph API calls, validation)
2. ✅ **TASK-4.4-PHASE-2-UPDATE-FACADE.md** - Return types may be incorrect
3. ⚠️ **TASK-4.4-PHASE-3-TOKEN-HELPER.md** - Likely correct (simple utility)
4. ✅ **TASK-4.4-PHASE-4-UPDATE-ENDPOINTS.md** - DTO usage errors
5. ⚠️ **TASK-4.4-PHASE-5-DELETE-FILES.md** - Likely correct (just file deletion)
6. ⚠️ **TASK-4.4-PHASE-6-UPDATE-DI.md** - Likely correct (DI registration)
7. ⚠️ **TASK-4.4-PHASE-7-BUILD-TEST.md** - Likely correct (verification steps)

**High Priority:** Phases 1, 2, 4
**Medium Priority:** Phases 3, 5, 6, 7 (verify but likely ok)

---

## Recommendation

**Action Required:**
1. Revert DriveItemOperations.cs changes (currently has 24 compile errors)
2. Update Phase 1, 2, and 4 documentation with correct patterns from this document
3. Verify Phases 3, 5, 6, 7 for consistency
4. Resume implementation with corrected documentation

**Time Estimate:**
- Update Phase 1: 20 minutes
- Update Phase 2: 10 minutes
- Update Phase 4: 15 minutes
- Verify Phases 3, 5-7: 10 minutes
- **Total: 55 minutes**

**Confidence After Fix:** ✅ **HIGH** - Implementation will work on first try with correct documentation.

---

**Status:** Ready to update phase documentation with authoritative patterns from OboSpeService.
