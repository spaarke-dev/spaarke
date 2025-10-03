# Task 4.4 - Phase 1: Add OBO Methods to Operation Classes

**Sprint:** 4
**Phase:** 1 of 7
**Estimated Effort:** 6 hours
**Status:** Ready to Implement (Documentation Corrected)
**Source:** Verified against [OboSpeService.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Services\OboSpeService.cs)

---

## Objective

Add user-context (OBO) methods to existing operation classes by copying exact implementations from `OboSpeService`.

**Key Principle:** Use EXACT patterns from OboSpeService - DTOs, error handling, validation, Graph API calls.

---

## Phase 1.1: ContainerOperations (1 hour) ✅ COMPLETE

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs`

**Status:** Already implemented and verified (build succeeds).

---

## Phase 1.2: DriveItemOperations (2 hours)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs`

**Add section comment at end of class (before closing brace):**

```csharp
    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
```

### Method 1: ListChildrenAsUserAsync

**Source:** OboSpeService.cs lines 185-271

```csharp
/// <summary>
/// Lists drive children as the user (OBO flow) with paging and ordering.
/// </summary>
public async Task<ListingResponse> ListChildrenAsUserAsync(
    string userToken,
    string containerId,
    ListingParameters parameters,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    using var activity = Activity.Current;
    activity?.SetTag("operation", "ListChildrenAsUser");
    activity?.SetTag("containerId", containerId);

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

        // Get the drive for the container
        var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
            .GetAsync(cancellationToken: ct);

        if (drive?.Id is null)
        {
            _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
            return new ListingResponse(new List<DriveItemDto>(), null);
        }

        // Call Graph API to list root items with OData query parameters
        var children = await graphClient.Drives[drive.Id].Items
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = "parentReference/path eq '/drive/root:'";
                requestConfiguration.QueryParameters.Top = parameters.ValidatedTop;
                requestConfiguration.QueryParameters.Skip = parameters.ValidatedSkip;

                // Apply ordering (OData $orderby)
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

**Key Points:**
- ✅ Use `.Items` collection with Filter, NOT `.Root.Children`
- ✅ Use `parameters.ValidatedTop` not `parameters.Top`
- ✅ DriveItemDto uses positional constructor
- ✅ ListingResponse(items, nextLink) - only 2 parameters
- ✅ Error handling for 404, 403, 429

---

### Method 2: DownloadFileWithRangeAsUserAsync

**Source:** OboSpeService.cs lines 530-655

```csharp
/// <summary>
/// Downloads file with range support as the user (OBO flow).
/// Supports partial content (206) and conditional requests (304).
/// </summary>
public async Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    RangeHeader? range,
    string? ifNoneMatch,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    if (!FileOperationExtensions.IsValidItemId(itemId))
    {
        _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
        return null;
    }

    using var activity = Activity.Current;
    activity?.SetTag("operation", "DownloadFileWithRangeAsUser");
    activity?.SetTag("driveId", driveId);
    activity?.SetTag("itemId", itemId);

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

        // First, get item metadata to check ETag and size
        var item = await graphClient.Drives[driveId].Items[itemId]
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
            contentStream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.Headers.Add("Range", $"bytes={actualStart}-{actualEnd}");
                }, cancellationToken: ct);

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
            contentStream = await graphClient.Drives[driveId].Items[itemId].Content
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

**Key Points:**
- ✅ Validates itemId before Graph API call
- ✅ FileContentResponse constructor: (Content, ContentLength, ContentType, ETag, RangeStart, RangeEnd, TotalSize)
- ✅ Range data stored as Start/End/Total, not header string
- ✅ Error handling for 404, 403, 416

---

### Method 3: UpdateItemAsUserAsync

**Source:** OboSpeService.cs lines 420-492

```csharp
/// <summary>
/// Updates drive item (rename/move) as the user (OBO flow).
/// </summary>
public async Task<DriveItemDto?> UpdateItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    UpdateFileRequest request,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

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

    using var activity = Activity.Current;
    activity?.SetTag("operation", "UpdateItemAsUser");
    activity?.SetTag("driveId", driveId);
    activity?.SetTag("itemId", itemId);

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

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
        var updatedItem = await graphClient.Drives[driveId].Items[itemId]
            .PatchAsync(driveItemUpdate, cancellationToken: ct);

        if (updatedItem == null)
        {
            _logger.LogWarning("Item not found or update failed: {ItemId}", itemId);
            return null;
        }

        _logger.LogInformation("Updated item {ItemId}: name={Name}, parentRef={ParentRef}",
            itemId, updatedItem.Name, request.ParentReferenceId);

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

**Key Points:**
- ✅ Validates itemId and fileName before Graph API call
- ✅ Uses DriveItemDto positional constructor
- ✅ Error handling for 404, 403

---

### Method 4: DeleteItemAsUserAsync

**Source:** OboSpeService.cs lines 494-528

```csharp
/// <summary>
/// Deletes drive item as the user (OBO flow).
/// </summary>
public async Task<bool> DeleteItemAsUserAsync(
    string userToken,
    string driveId,
    string itemId,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    if (!FileOperationExtensions.IsValidItemId(itemId))
    {
        _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
        return false;
    }

    using var activity = Activity.Current;
    activity?.SetTag("operation", "DeleteItemAsUser");
    activity?.SetTag("driveId", driveId);
    activity?.SetTag("itemId", itemId);

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

        // Delete item via Graph API
        await graphClient.Drives[driveId].Items[itemId]
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

**Key Points:**
- ✅ Validates itemId before Graph API call
- ✅ Returns bool for success/not-found
- ✅ Error handling for 404, 403

---

## Phase 1.3: UploadSessionManager (2 hours)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

**Add section comment at end of class (before closing brace):**

```csharp
    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
```

### Method 1: UploadSmallAsUserAsync

**Source:** OboSpeService.cs lines 43-104

```csharp
/// <summary>
/// Uploads a small file (< 4MB) as the user (OBO flow).
/// </summary>
public async Task<DriveItem?> UploadSmallAsUserAsync(
    string userToken,
    string containerId,
    string path,
    Stream content,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

        // Get drive ID
        var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
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
        var uploadedItem = await graphClient.Drives[drive.Id].Root
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

**Key Points:**
- ✅ Validates content size (< 4MB)
- ✅ Returns DriveItem (Graph SDK type, not DTO)
- ✅ Error handling for 403, 413, 429

---

### Method 2: CreateUploadSessionAsUserAsync

**Source:** OboSpeService.cs lines 273-321

```csharp
/// <summary>
/// Creates an upload session for large files as the user (OBO flow).
/// </summary>
public async Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
    string userToken,
    string driveId,
    string path,
    ConflictBehavior conflictBehavior,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

    try
    {
        var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

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
        var session = await graphClient.Drives[driveId].Root
            .ItemWithPath(path)
            .CreateUploadSession
            .PostAsync(uploadSessionRequest, cancellationToken: ct);

        if (session == null || string.IsNullOrEmpty(session.UploadUrl))
        {
            _logger.LogError("Failed to create upload session for path {Path}", path);
            return null;
        }

        _logger.LogInformation("Created upload session for drive {DriveId}, path {Path}, conflict behavior {ConflictBehavior}, expires at {ExpirationDateTime}",
            driveId, path, conflictBehavior, session.ExpirationDateTime);

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
        _logger.LogError(ex, "Failed to create upload session for drive {DriveId}, path {Path}", driveId, path);
        throw;
    }
}
```

**Key Points:**
- ✅ UploadSessionResponse constructor: (UploadUrl, ExpirationDateTime)
- ✅ Uses ConflictBehavior enum
- ✅ Error handling for 403

---

### Method 3: UploadChunkAsUserAsync

**Source:** OboSpeService.cs lines 323-418

```csharp
/// <summary>
/// Uploads a chunk to an upload session as the user (OBO flow).
/// </summary>
public async Task<ChunkUploadResponse> UploadChunkAsUserAsync(
    string userToken,
    string uploadSessionUrl,
    string contentRange,
    byte[] chunkData,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
        throw new ArgumentException("User access token required", nameof(userToken));

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
            var driveItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(responseContent, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

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

**Key Points:**
- ✅ Validates chunk size (8-10 MB)
- ✅ Uses ContentRangeHeader.Parse
- ✅ ChunkUploadResponse constructor: (StatusCode, CompletedItem?)
- ✅ Returns DriveItemDto on completion (201)

---

## Phase 1.4: Create UserOperations Class (1 hour)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs` (NEW)

**Source:** OboSpeService.cs lines 106-183

```csharp
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// User-specific Graph operations (user info, capabilities).
/// All operations use OBO (On-Behalf-Of) flow.
/// </summary>
public class UserOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<UserOperations> _logger;

    public UserOperations(IGraphClientFactory factory, ILogger<UserOperations> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets current user information via Microsoft Graph /me endpoint.
    /// </summary>
    public async Task<UserInfoResponse?> GetUserInfoAsync(
        string userToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);
            var user = await graphClient.Me.GetAsync(cancellationToken: ct);

            if (user == null || string.IsNullOrEmpty(user.Id))
                return null;

            return new UserInfoResponse(
                DisplayName: user.DisplayName ?? "Unknown User",
                UserPrincipalName: user.UserPrincipalName ?? "unknown@domain.com",
                Oid: user.Id
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info");
            return null;
        }
    }

    /// <summary>
    /// Gets user capabilities for a specific container.
    /// Returns what operations the user can perform based on their permissions.
    /// Note: Simplified implementation - checks drive access only.
    /// </summary>
    public async Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        string userToken,
        string containerId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userToken))
            throw new ArgumentException("User access token required", nameof(userToken));

        try
        {
            var graphClient = await _factory.CreateOnBehalfOfClientAsync(userToken);

            // Try to access the container to determine capabilities
            var hasAccess = false;
            try
            {
                var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
                hasAccess = drive?.Id != null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User denied access to container {ContainerId}", containerId);
                hasAccess = false;
            }

            var capabilities = new UserCapabilitiesResponse(
                Read: hasAccess,
                Write: hasAccess,
                Delete: hasAccess,
                CreateFolder: hasAccess
            );

            _logger.LogInformation("Retrieved capabilities for container {ContainerId}: {Capabilities}",
                containerId, capabilities);

            return capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine capabilities for container {ContainerId}", containerId);
            return new UserCapabilitiesResponse(false, false, false, false);
        }
    }
}
```

**Key Points:**
- ✅ UserInfoResponse constructor: (DisplayName, UserPrincipalName, Oid)
- ✅ UserCapabilitiesResponse constructor: (Read, Write, Delete, CreateFolder)
- ✅ Simplified capabilities (checks drive access only)
- ✅ Note: OboSpeService has caching - we're omitting that for simplicity (can add later)

---

## Verification After Phase 1

```bash
# Build should succeed
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: 0 errors

# Verify methods added
grep -n "AsUserAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs
grep -n "AsUserAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs
grep -n "class UserOperations" src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs
```

---

## Acceptance Criteria

- [ ] Phase 1.1: ✅ COMPLETE (ContainerOperations)
- [ ] Phase 1.2: DriveItemOperations has 4 OBO methods (List, Download, Update, Delete)
- [ ] Phase 1.3: UploadSessionManager has 3 OBO methods (Upload, CreateSession, UploadChunk)
- [ ] Phase 1.4: UserOperations.cs created with 2 methods (GetUserInfo, GetCapabilities)
- [ ] Build succeeds with 0 errors
- [ ] All methods use exact patterns from OboSpeService
- [ ] All DTOs use positional constructors
- [ ] All methods include validation and comprehensive error handling

---

## Next Phase

**Phase 2:** Update SpeFileStore facade to expose OBO methods via delegation
