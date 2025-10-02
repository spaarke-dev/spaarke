# Task 2.1: OboSpeService Real Implementation - Replace Mock Data with Microsoft Graph SDK v5

**Priority:** HIGH (Sprint 3, Phase 2)
**Estimated Effort:** 8-10 days
**Status:** BLOCKS PRODUCTION
**Dependencies:** Task 1.1 (Authorization), Task 1.2 (Configuration)

---

## Context & Problem Statement

The `OboSpeService` currently returns **mock/sample data** for all file operations, making it unusable for production:

1. **All Graph calls are stubbed**: Lines 28-29, 144-146, 250-259, etc. return generated data instead of calling Microsoft Graph
2. **Wrong namespace**: Line 7 uses `namespace Services;` instead of `Spe.Bff.Api.Services`
3. **Mock data generators exist**: `GenerateSampleItems`, `GenerateSampleFileContent` should be removed
4. **No real file operations**: Upload, download, list, update, delete all return fake data
5. **SDK v5 migration incomplete**: Comments indicate "API temporarily disabled due to Graph SDK v5 changes"

**File Location**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs` (559 lines)

This blocks all real file management functionality and prevents end-to-end testing.

---

## Goals & Outcomes

### Primary Goals
1. Replace all mock data generation with real Microsoft Graph SDK v5 calls
2. Fix namespace from `Services` to `Spe.Bff.Api.Services`
3. Implement proper On-Behalf-Of (OBO) token flow for delegated access
4. Remove all sample data generators and placeholder logic
5. Implement full CRUD operations: List, Download, Upload (small + chunked), Update, Delete
6. Add proper error handling for Graph API failures (404, 403, 429, 5xx)

### Success Criteria
- [ ] All methods call real Microsoft Graph APIs (no mocks)
- [ ] ListChildrenAsync returns actual SharePoint Embedded files
- [ ] DownloadContentWithRangeAsync streams real file content with range support
- [ ] Upload operations (small + chunked) successfully store files in SPE
- [ ] Update/Delete operations work against real Graph resources
- [ ] Proper error handling for Graph API error codes
- [ ] Integration tests validate against test SPE container
- [ ] Namespace corrected to `Spe.Bff.Api.Services`

### Non-Goals
- Offline/cache-first mode (Sprint 4+)
- Advanced delta sync capabilities (Sprint 4+)
- Conflict resolution beyond basic last-write-wins (Sprint 4+)

---

## Architecture & Design

### Current State (Sprint 2)
```
┌─────────────────────┐
│   OboSpeService     │
└──────────┬──────────┘
           │
           v
┌──────────────────────────┐
│ IGraphClientFactory      │
│ CreateOnBehalfOfClient() │ ← Creates Graph client with user token
└──────────┬───────────────┘
           │
           v
┌──────────────────────────┐
│ Microsoft Graph SDK v5   │
│ (Calls stubbed/disabled) │ ← Returns mock data
└──────────────────────────┘
           │
           v
┌──────────────────────────┐
│ Sample Data Generators   │
│ - GenerateSampleItems    │
│ - GenerateSampleFileContent
└──────────────────────────┘
```

### Target State (Sprint 3)
```
┌─────────────────────┐
│   OboSpeService     │ ← namespace Spe.Bff.Api.Services
└──────────┬──────────┘
           │
           v
┌──────────────────────────┐
│ IGraphClientFactory      │
│ CreateOnBehalfOfClient() │ ← OBO token flow
└──────────┬───────────────┘
           │
           v
┌──────────────────────────┐
│ Microsoft Graph SDK v5   │
│ Real API calls:          │
│ - Storage.FileStorage    │
│   .Containers[id]        │
│   .Drive.Items           │
└──────────┬───────────────┘
           │
           v
┌──────────────────────────┐
│ SharePoint Embedded      │
│ - List children          │
│ - Download content       │
│ - Upload (small/chunked) │
│ - Update metadata        │
│ - Delete items           │
└──────────────────────────┘
```

---

## Relevant ADRs

### ADR-007: SPE Storage Seam Minimalism
- **Key Principle**: Minimal abstraction over Graph SDK, direct SDK calls
- **No Repository Pattern**: Direct Graph client usage in services
- **Error Mapping**: Map Graph exceptions to domain errors

### ADR-010: DI Minimalism
- **IGraphClientFactory as Seam**: Only interface for Graph client creation
- **Concrete Services**: OboSpeService registered as concrete, not interface
- **Constructor Injection**: Inject only IGraphClientFactory, IMemoryCache, ILogger

---

## Implementation Steps

### Step 1: Fix Namespace

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs`

**Current Code (Line 7)**:
```csharp
namespace Services;
```

**Required Change**:
```csharp
namespace Spe.Bff.Api.Services;
```

---

### Step 2: Implement Real ListChildrenAsync

**Current Code (Lines 133-172)**:
```csharp
public async Task<ListingResponse> ListChildrenAsync(string userBearer, string containerId, ListingParameters parameters, CancellationToken ct)
{
    var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
    var drive = await graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
    if (drive?.Id is null) return new ListingResponse(new List<DriveItemDto>(), null);

    // For now, create sample data since Graph SDK v5 API is disabled
    var sampleItems = GenerateSampleItems(parameters.ValidatedTop + parameters.ValidatedSkip + 10);
    // ... mock pagination logic
}
```

**Required Implementation**:
```csharp
public async Task<ListingResponse> ListChildrenAsync(
    string userBearer,
    string containerId,
    ListingParameters parameters,
    CancellationToken ct)
{
    try
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // Get the drive for the container
        var drive = await graph.Storage.FileStorage.Containers[containerId].Drive
            .GetAsync(cancellationToken: ct);

        if (drive?.Id is null)
        {
            _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
            return new ListingResponse(new List<DriveItemDto>(), null);
        }

        // Build OData query for listing with pagination and ordering
        var requestConfig = new Action<Microsoft.Graph.Drives.Item.Root.Children.ChildrenRequestBuilder.ChildrenRequestBuilderGetRequestConfiguration>(config =>
        {
            config.QueryParameters.Top = parameters.ValidatedTop;
            config.QueryParameters.Skip = parameters.ValidatedSkip;

            // Apply ordering (OData $orderby)
            var orderField = parameters.ValidatedOrderBy.ToLowerInvariant() switch
            {
                "name" => "name",
                "lastmodifieddatetime" => "lastModifiedDateTime",
                "size" => "size",
                _ => "name"
            };
            var orderDirection = parameters.ValidatedOrderDir == "desc" ? " desc" : " asc";
            config.QueryParameters.Orderby = new[] { orderField + orderDirection };
        });

        // Call Graph API to list children
        var children = await graph.Drives[drive.Id].Root.Children
            .GetAsync(requestConfig, cancellationToken: ct);

        if (children?.Value == null)
        {
            return new ListingResponse(new List<DriveItemDto>(), null);
        }

        // Map Graph DriveItem to DriveItemDto
        var items = children.Value.Select(item => new DriveItemDto(
            Id: item.Id!,
            Name: item.Name!,
            Size: item.Size,
            ETag: item.ETag,
            LastModifiedDateTime: item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            ContentType: item.File?.MimeType,
            Folder: item.Folder != null ? new FolderDto(item.Folder.ChildCount) : null
        )).ToList();

        // Handle pagination (@odata.nextLink)
        string? nextLink = null;
        if (!string.IsNullOrEmpty(children.OdataNextLink))
        {
            // Extract skip token from nextLink
            var nextSkip = parameters.ValidatedSkip + parameters.ValidatedTop;
            nextLink = $"/api/obo/containers/{containerId}/children?top={parameters.ValidatedTop}&skip={nextSkip}&orderBy={parameters.ValidatedOrderBy}&orderDir={parameters.ValidatedOrderDir}";
        }

        _logger.LogInformation("Listed {Count} items for container {ContainerId}", items.Count, containerId);
        return new ListingResponse(items, nextLink);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Container or drive not found: {ContainerId}", containerId);
        return new ListingResponse(new List<DriveItemDto>(), null);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied to container {ContainerId}: {Error}", containerId, ex.Message);
        throw new UnauthorizedAccessException($"Access denied to container {containerId}", ex);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
    {
        _logger.LogWarning("Graph API throttling for container {ContainerId}, retry after {RetryAfter}s",
            containerId, ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60);
        throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to list children for container {ContainerId}", containerId);
        throw;
    }
}
```

---

### Step 3: Implement Real DownloadContentWithRangeAsync

**Current Code (Lines 441-528)**:
```csharp
public async Task<FileContentResponse?> DownloadContentWithRangeAsync(...)
{
    // ... stub implementation with GenerateSampleFileContent
    var sampleContent = GenerateSampleFileContent(itemId);
    // ... mock range handling
}
```

**Required Implementation**:
```csharp
public async Task<FileContentResponse?> DownloadContentWithRangeAsync(
    string userBearer,
    string driveId,
    string itemId,
    RangeHeader? range,
    string? ifNoneMatch,
    CancellationToken ct)
{
    try
    {
        if (!FileOperationExtensions.IsValidItemId(itemId))
        {
            _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
            return null;
        }

        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // First, get item metadata to check ETag and size
        var item = await graph.Drives[driveId].Items[itemId]
            .GetAsync(cancellationToken: ct);

        if (item == null)
        {
            _logger.LogWarning("Item not found: {ItemId}", itemId);
            return null;
        }

        // Handle If-None-Match (ETag-based caching)
        if (!string.IsNullOrEmpty(ifNoneMatch) &&
            !string.IsNullOrEmpty(item.ETag) &&
            ifNoneMatch.Trim('"') == item.ETag.Trim('"'))
        {
            _logger.LogInformation("ETag match for item {ItemId}, returning 304 Not Modified", itemId);
            return new FileContentResponse(
                Content: Stream.Null,
                ContentLength: 0,
                ContentType: item.File?.MimeType ?? "application/octet-stream",
                ETag: item.ETag
            );
        }

        var totalSize = item.Size ?? 0;
        var contentType = item.File?.MimeType ?? "application/octet-stream";

        // Download content with optional range
        Stream contentStream;
        long contentLength;
        long? rangeStart = null;
        long? rangeEnd = null;

        if (range != null && range.IsValid && totalSize > 0)
        {
            // Handle partial content (HTTP 206)
            var actualEnd = Math.Min(range.End, totalSize - 1);
            var actualStart = Math.Min(range.Start, actualEnd);

            if (actualStart >= totalSize)
            {
                // Range not satisfiable (HTTP 416)
                _logger.LogWarning("Range not satisfiable for item {ItemId}: {Start}-{End}/{TotalSize}",
                    itemId, actualStart, actualEnd, totalSize);
                return null;
            }

            // Use Graph API to download specific range
            var requestConfig = new Action<Microsoft.Graph.Drives.Item.Items.Item.Content.ContentRequestBuilder.ContentRequestBuilderGetRequestConfiguration>(config =>
            {
                config.Headers.Add("Range", $"bytes={actualStart}-{actualEnd}");
            });

            contentStream = await graph.Drives[driveId].Items[itemId].Content
                .GetAsync(requestConfig, cancellationToken: ct);

            if (contentStream == null)
            {
                _logger.LogError("Failed to download range content for item {ItemId}", itemId);
                return null;
            }

            contentLength = actualEnd - actualStart + 1;
            rangeStart = actualStart;
            rangeEnd = actualEnd;

            _logger.LogInformation("Serving range {Start}-{End} of item {ItemId} (total: {TotalSize})",
                actualStart, actualEnd, itemId, totalSize);
        }
        else
        {
            // Download full content
            contentStream = await graph.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (contentStream == null)
            {
                _logger.LogError("Failed to download content for item {ItemId}", itemId);
                return null;
            }

            contentLength = totalSize;
            _logger.LogInformation("Serving full content of item {ItemId} (size: {Size})", itemId, totalSize);
        }

        return new FileContentResponse(
            Content: contentStream,
            ContentLength: contentLength,
            ContentType: contentType,
            ETag: item.ETag,
            RangeStart: rangeStart,
            RangeEnd: rangeEnd,
            TotalSize: totalSize
        );
    }
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
    catch (ServiceException ex) when (ex.ResponseStatusCode == 416)
    {
        _logger.LogWarning("Range not satisfiable for item {ItemId}", itemId);
        return null;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to download content for item {ItemId}", itemId);
        throw;
    }
}
```

---

### Step 4: Implement Real Upload (Small Files)

**Current Code (Lines 42-52)**:
```csharp
public async Task<DriveItem?> UploadSmallAsync(...)
{
    // Simplified upload - API temporarily disabled due to Graph SDK v5 changes
    DriveItem? item = null; // Would upload via Graph API
    return item;
}
```

**Required Implementation**:
```csharp
public async Task<DriveItem?> UploadSmallAsync(
    string userBearer,
    string containerId,
    string path,
    Stream content,
    CancellationToken ct)
{
    try
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // Get drive ID
        var drive = await graph.Storage.FileStorage.Containers[containerId].Drive
            .GetAsync(cancellationToken: ct);

        if (drive?.Id is null)
        {
            _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
            return null;
        }

        // Validate content size (small upload < 4MB)
        if (content.CanSeek && content.Length > 4 * 1024 * 1024)
        {
            _logger.LogWarning("Content too large for small upload: {Size} bytes (max 4MB)", content.Length);
            throw new ArgumentException("Content size exceeds 4MB limit for small uploads. Use chunked upload instead.");
        }

        // Upload file using PUT to drive item path
        var uploadedItem = await graph.Drives[drive.Id].Root
            .ItemWithPath(path)
            .Content
            .PutAsync(content, cancellationToken: ct);

        if (uploadedItem == null)
        {
            _logger.LogError("Upload failed for path {Path} in container {ContainerId}", path, containerId);
            return null;
        }

        _logger.LogInformation("Successfully uploaded file to {Path} in container {ContainerId}, item ID: {ItemId}",
            path, containerId, uploadedItem.Id);

        return uploadedItem;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied uploading to container {ContainerId}: {Error}", containerId, ex.Message);
        throw new UnauthorizedAccessException($"Access denied to container {containerId}", ex);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 413)
    {
        _logger.LogWarning("Content too large for path {Path}", path);
        throw new ArgumentException("Content size exceeds limit. Use chunked upload for large files.", ex);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 429)
    {
        _logger.LogWarning("Graph API throttling, retry after {RetryAfter}s",
            ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60);
        throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload to path {Path} in container {ContainerId}", path, containerId);
        throw;
    }
}
```

---

### Step 5: Implement Real Chunked Upload

**Current Code (Lines 244-266, 268-354)**:
Mock implementation with simulated upload sessions.

**Required Implementation**:

```csharp
public async Task<UploadSessionResponse?> CreateUploadSessionAsync(
    string userBearer,
    string driveId,
    string path,
    ConflictBehavior conflictBehavior,
    CancellationToken ct)
{
    try
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // Create upload session request
        var uploadSessionRequest = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
        {
            Item = new DriveItemUploadableProperties
            {
                AdditionalData = new Dictionary<string, object>
                {
                    ["@microsoft.graph.conflictBehavior"] = conflictBehavior.ToString().ToLowerInvariant()
                }
            }
        };

        // Create upload session via Graph API
        var session = await graph.Drives[driveId].Root
            .ItemWithPath(path)
            .CreateUploadSession
            .PostAsync(uploadSessionRequest, cancellationToken: ct);

        if (session == null || string.IsNullOrEmpty(session.UploadUrl))
        {
            _logger.LogError("Failed to create upload session for path {Path}", path);
            return null;
        }

        _logger.LogInformation("Created upload session for path {Path}, expires at {ExpirationDateTime}",
            path, session.ExpirationDateTime);

        return new UploadSessionResponse(
            session.UploadUrl,
            session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(1)
        );
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied creating upload session: {Error}", ex.Message);
        throw new UnauthorizedAccessException("Access denied", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to create upload session for path {Path}", path);
        throw;
    }
}

public async Task<ChunkUploadResponse> UploadChunkAsync(
    string userBearer,
    string uploadSessionUrl,
    string contentRange,
    byte[] chunkData,
    CancellationToken ct)
{
    try
    {
        var range = ContentRangeHeader.Parse(contentRange);
        if (range == null || !range.IsValid)
        {
            _logger.LogWarning("Invalid Content-Range header: {ContentRange}", contentRange);
            return new ChunkUploadResponse(400);
        }

        // Validate chunk size (8-10 MiB as per Graph API requirements)
        const long minChunkSize = 8 * 1024 * 1024;
        const long maxChunkSize = 10 * 1024 * 1024;

        if (chunkData.Length < minChunkSize && (!range.Total.HasValue || range.End + 1 < range.Total.Value))
        {
            _logger.LogWarning("Chunk size {Size} below minimum {MinSize} (not final chunk)", chunkData.Length, minChunkSize);
            return new ChunkUploadResponse(400);
        }

        if (chunkData.Length > maxChunkSize)
        {
            _logger.LogWarning("Chunk size {Size} exceeds maximum {MaxSize}", chunkData.Length, maxChunkSize);
            return new ChunkUploadResponse(413);
        }

        if (chunkData.Length != range.ChunkSize)
        {
            _logger.LogWarning("Chunk data length {ActualSize} does not match Content-Range size {ExpectedSize}",
                chunkData.Length, range.ChunkSize);
            return new ChunkUploadResponse(400);
        }

        // Upload chunk to Graph API using raw HTTP (SDK doesn't expose chunked upload directly)
        using var httpClient = new HttpClient();
        using var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Content-Range", contentRange);
        content.Headers.ContentLength = chunkData.Length;

        var response = await httpClient.PutAsync(uploadSessionUrl, content, ct);

        // Handle response based on status code
        if (response.StatusCode == System.Net.HttpStatusCode.Accepted) // 202 - more chunks expected
        {
            _logger.LogInformation("Uploaded chunk {Start}-{End} for session {UploadUrl}",
                range.Start, range.End, uploadSessionUrl);
            return new ChunkUploadResponse(202);
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.Created ||
                 response.StatusCode == System.Net.HttpStatusCode.OK) // 201/200 - upload complete
        {
            var responseContent = await response.Content.ReadAsStringAsync(ct);
            var driveItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(responseContent);

            if (driveItem == null)
            {
                _logger.LogError("Failed to deserialize completed upload response");
                return new ChunkUploadResponse(500);
            }

            var completedItemDto = new DriveItemDto(
                Id: driveItem.Id!,
                Name: driveItem.Name!,
                Size: driveItem.Size,
                ETag: driveItem.ETag,
                LastModifiedDateTime: driveItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                ContentType: driveItem.File?.MimeType,
                Folder: null
            );

            _logger.LogInformation("Completed upload session {UploadUrl}, item ID: {ItemId}",
                uploadSessionUrl, completedItemDto.Id);

            return new ChunkUploadResponse(201, completedItemDto);
        }
        else
        {
            _logger.LogWarning("Unexpected response from chunked upload: {StatusCode}", response.StatusCode);
            return new ChunkUploadResponse((int)response.StatusCode);
        }
    }
    catch (OperationCanceledException)
    {
        _logger.LogWarning("Upload chunk operation was cancelled");
        return new ChunkUploadResponse(499);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload chunk for session {UploadUrl}", uploadSessionUrl);
        return new ChunkUploadResponse(500);
    }
}
```

---

### Step 6: Implement Real Update/Delete Operations

**UpdateItemAsync** (Lines 365-413):
```csharp
public async Task<DriveItemDto?> UpdateItemAsync(
    string userBearer,
    string driveId,
    string itemId,
    UpdateFileRequest request,
    CancellationToken ct)
{
    try
    {
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

        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // Build update request
        var driveItemUpdate = new DriveItem();

        if (!string.IsNullOrEmpty(request.Name))
        {
            driveItemUpdate.Name = request.Name;
        }

        if (!string.IsNullOrEmpty(request.ParentReferenceId))
        {
            driveItemUpdate.ParentReference = new ItemReference
            {
                Id = request.ParentReferenceId
            };
        }

        // Execute update via Graph API
        var updatedItem = await graph.Drives[driveId].Items[itemId]
            .PatchAsync(driveItemUpdate, cancellationToken: ct);

        if (updatedItem == null)
        {
            _logger.LogWarning("Item not found or update failed: {ItemId}", itemId);
            return null;
        }

        _logger.LogInformation("Updated item {ItemId}: name={Name}", itemId, updatedItem.Name);

        return new DriveItemDto(
            Id: updatedItem.Id!,
            Name: updatedItem.Name!,
            Size: updatedItem.Size,
            ETag: updatedItem.ETag,
            LastModifiedDateTime: updatedItem.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            ContentType: updatedItem.File?.MimeType,
            Folder: updatedItem.Folder != null ? new FolderDto(updatedItem.Folder.ChildCount) : null
        );
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Item not found: {ItemId}", itemId);
        return null;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied updating item {ItemId}: {Error}", itemId, ex.Message);
        throw new UnauthorizedAccessException($"Access denied to item {itemId}", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to update item {ItemId}", itemId);
        throw;
    }
}
```

**DeleteItemAsync** (Lines 415-439):
```csharp
public async Task<bool> DeleteItemAsync(
    string userBearer,
    string driveId,
    string itemId,
    CancellationToken ct)
{
    try
    {
        if (!FileOperationExtensions.IsValidItemId(itemId))
        {
            _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
            return false;
        }

        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

        // Delete item via Graph API
        await graph.Drives[driveId].Items[itemId]
            .DeleteAsync(cancellationToken: ct);

        _logger.LogInformation("Deleted item {ItemId} from drive {DriveId}", itemId, driveId);
        return true;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Item not found (may already be deleted): {ItemId}", itemId);
        return false;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied deleting item {ItemId}: {Error}", itemId, ex.Message);
        throw new UnauthorizedAccessException($"Access denied to item {itemId}", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to delete item {ItemId}", itemId);
        throw;
    }
}
```

---

### Step 7: Remove All Mock Data Generators

**Lines to Delete**:
- Lines 174-242: `GenerateSampleItems`, `GetRandomExtension`, `GetContentType`, `ApplySorting`
- Lines 530-558: `GenerateSampleFileContent`, `GetContentTypeFromItemId`
- Line 387: Remove call to `GenerateSampleItems(1).FirstOrDefault()`

**Files to Update**:
- `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs`

---

## AI Coding Prompts

### Prompt 1: Fix Namespace and Implement ListChildrenAsync
```
Replace mock ListChildrenAsync with real Microsoft Graph SDK v5 calls:

Context:
- File: C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs
- Current namespace (line 7): "namespace Services;" - MUST change to "namespace Spe.Bff.Api.Services;"
- Lines 133-172: Mock implementation using GenerateSampleItems
- Need to call real Graph API: graph.Drives[driveId].Root.Children.GetAsync()

Requirements:
1. Fix namespace to Spe.Bff.Api.Services
2. Remove GenerateSampleItems call and all mock logic
3. Call graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync() to get drive ID
4. Call graph.Drives[driveId].Root.Children.GetAsync() with OData parameters
5. Support pagination via OData $top, $skip, $orderby
6. Map Graph SDK DriveItem to DriveItemDto
7. Handle nextLink for pagination
8. Error handling: 404 (not found), 403 (forbidden), 429 (throttling)

Code Quality:
- Senior C# developer standards
- Comprehensive logging (LogInformation for success, LogWarning for 404/403)
- Proper exception handling with specific catch blocks for ServiceException
- Use structured logging with properties

Testing:
- Should work with real SPE container
- Validate pagination with different top/skip values
- Test ordering by name, lastModifiedDateTime, size
```

### Prompt 2: Implement Real Download with Range Support
```
Replace mock DownloadContentWithRangeAsync with real Graph API:

Context:
- File: C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs
- Lines 441-528: Mock implementation with GenerateSampleFileContent
- Need HTTP 206 Partial Content support for range requests

Requirements:
1. Get item metadata first: graph.Drives[driveId].Items[itemId].GetAsync()
2. Check ETag for If-None-Match (return 304 if match)
3. Handle range requests with "Range: bytes=start-end" header
4. Download full or partial content: graph.Drives[driveId].Items[itemId].Content.GetAsync()
5. Return FileContentResponse with proper Content-Length, Content-Range headers
6. Error handling: 404, 403, 416 (Range Not Satisfiable), 429

Code Quality:
- Stream content efficiently (no buffering entire file in memory)
- Dispose streams properly
- Handle edge cases (empty files, invalid ranges)
- Log download metrics (size, duration)
```

### Prompt 3: Implement Upload (Small and Chunked)
```
Implement real file upload for both small (<4MB) and large (chunked) files:

Context:
- File: C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs
- Lines 42-52: Mock UploadSmallAsync
- Lines 244-354: Mock chunked upload

Requirements for Small Upload:
1. Validate content size < 4MB
2. Use graph.Drives[driveId].Root.ItemWithPath(path).Content.PutAsync(stream)
3. Return DriveItem on success
4. Error handling: 403, 413 (Payload Too Large), 429

Requirements for Chunked Upload:
1. CreateUploadSessionAsync: Create upload session with ConflictBehavior
2. UploadChunkAsync: Upload chunks (8-10 MiB) to session URL
3. Use raw HttpClient for chunk uploads (SDK doesn't expose this)
4. Handle 202 (Accepted), 201/200 (Created/Complete)
5. Validate Content-Range header format
6. Return DriveItemDto on final chunk completion

Code Quality:
- Validate chunk sizes per Graph API requirements
- Track upload progress for large files
- Handle network failures with retry logic (caller responsibility)
- Comprehensive error handling
```

### Prompt 4: Implement Update and Delete Operations
```
Implement real UpdateItemAsync and DeleteItemAsync:

Context:
- File: C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs
- Lines 365-413: Mock UpdateItemAsync
- Lines 415-439: Mock DeleteItemAsync

Requirements for Update:
1. Validate item ID and file name
2. Build DriveItem with Name and/or ParentReference
3. Call graph.Drives[driveId].Items[itemId].PatchAsync()
4. Return updated DriveItemDto
5. Error handling: 404, 403, 429

Requirements for Delete:
1. Validate item ID
2. Call graph.Drives[driveId].Items[itemId].DeleteAsync()
3. Return true on success, false on 404
4. Error handling: 404 (may already be deleted), 403, 429

Code Quality:
- Idempotent delete (don't fail if already deleted)
- Log all operations with item IDs
- Proper exception mapping
```

### Prompt 5: Remove All Mock Data Generators
```
Remove all sample data generation code:

Context:
- File: C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs
- Lines 174-242: GenerateSampleItems, GetRandomExtension, GetContentType, ApplySorting
- Lines 530-558: GenerateSampleFileContent, GetContentTypeFromItemId

Requirements:
1. Delete GenerateSampleItems method and all related helpers
2. Delete GenerateSampleFileContent method
3. Remove any calls to these methods
4. Ensure no mock logic remains
5. Verify all methods call real Graph API

Validation:
- Search codebase for "sample" or "mock" - should find none in OboSpeService
- All methods should have real Graph SDK calls
- No fake data generation remains
```

---

## Testing Strategy

### Unit Tests (Mock Graph Client)
1. **ListChildrenAsync**:
   - Test successful listing with pagination
   - Test empty results
   - Test ordering (name, date, size)
   - Test error cases (404, 403, 429)

2. **DownloadContentWithRangeAsync**:
   - Test full content download
   - Test partial content (range requests)
   - Test ETag caching (304 Not Modified)
   - Test invalid ranges (416)

3. **Upload Operations**:
   - Test small file upload success
   - Test large file chunked upload
   - Test upload session creation
   - Test chunk validation

4. **Update/Delete**:
   - Test successful update (name, parent)
   - Test successful delete
   - Test idempotent delete
   - Test error cases

### Integration Tests (Real SPE Container)
1. **End-to-End File Operations**:
   - Create test container
   - Upload test file (small + large)
   - List files and verify
   - Download file and compare content
   - Update file name
   - Delete file
   - Cleanup

2. **Error Handling**:
   - Test unauthorized access (403)
   - Test not found (404)
   - Test throttling behavior (429)

3. **Performance Tests**:
   - Upload 100MB file (chunked)
   - Download large file with ranges
   - List 1000+ items with pagination

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] Namespace fixed to `Spe.Bff.Api.Services`
- [ ] ListChildrenAsync calls real Graph API with pagination
- [ ] DownloadContentWithRangeAsync streams real files with range support
- [ ] UploadSmallAsync uploads files < 4MB to SPE
- [ ] Chunked upload (CreateUploadSessionAsync + UploadChunkAsync) works for large files
- [ ] UpdateItemAsync renames/moves items
- [ ] DeleteItemAsync deletes items
- [ ] All mock generators removed (GenerateSampleItems, GenerateSampleFileContent)
- [ ] Error handling for 404, 403, 429, 416, 413
- [ ] Integration tests pass against real SPE container
- [ ] No TODO comments remain
- [ ] Code review by senior developer

---

## Completion Criteria

Task is complete when:
1. All 7 methods implemented with real Graph SDK v5 calls
2. Namespace corrected
3. All mock data generators removed
4. Integration tests pass with real SPE container
5. Error handling validated for all HTTP status codes
6. Performance validated (large file upload/download)
7. Code review approved

**Estimated Completion: 8-10 days** (can be split: 1 dev for read ops, 1 dev for write ops)
