# Task 4.4: Remove ISpeService/IOboSpeService - Full Refactor Implementation Guide

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 12.5 hours (1.5 days)
**Status:** Ready to Implement
**Approved Approach:** Full Refactor

---

## Executive Summary

Remove `ISpeService` and `IOboSpeService` interface abstractions (ADR-007 violation) by consolidating all Graph operations into a single `SpeFileStore` facade with dual authentication modes (app-only and OBO).

**Approach:** Move existing OBO code from `OboSpeService` into modular operation classes, then expose via `SpeFileStore` facade.

---

## Implementation Phases Overview

This implementation is divided into 7 phases, each documented in a separate file for better context management:

### Phase Files

1. **[Phase 1: Add OBO Methods to Operation Classes](TASK-4.4-PHASE-1-ADD-OBO-METHODS.md)** (6 hours)
   - Add `ListContainersAsUserAsync` to ContainerOperations
   - Add 4 methods to DriveItemOperations (List, Download, Update, Delete)
   - Add 3 methods to UploadSessionManager (Upload, CreateSession, UploadChunk)
   - Create UserOperations class with 2 methods (GetUserInfo, GetCapabilities)

2. **[Phase 2: Update SpeFileStore Facade](TASK-4.4-PHASE-2-UPDATE-FACADE.md)** (1 hour)
   - Add UserOperations to constructor
   - Add 11 delegation methods for OBO operations

3. **[Phase 3: Create TokenHelper Utility](TASK-4.4-PHASE-3-TOKEN-HELPER.md)** (30 minutes)
   - Create static TokenHelper class
   - Centralize bearer token extraction logic

4. **[Phase 4: Update Endpoints](TASK-4.4-PHASE-4-UPDATE-ENDPOINTS.md)** (2 hours)
   - Update 7 endpoints in OBOEndpoints.cs
   - Update 2 endpoints in UserEndpoints.cs
   - Replace IOboSpeService with SpeFileStore

5. **[Phase 5: Delete Interface Files](TASK-4.4-PHASE-5-DELETE-FILES.md)** (30 minutes)
   - Delete ISpeService.cs, SpeService.cs
   - Delete IOboSpeService.cs, OboSpeService.cs

6. **[Phase 6: Update DI Registration](TASK-4.4-PHASE-6-UPDATE-DI.md)** (1 hour)
   - Remove interface-based registrations
   - Add UserOperations to DI container

7. **[Phase 7: Build & Test](TASK-4.4-PHASE-7-BUILD-TEST.md)** (1.5 hours)
   - Clean build verification
   - Unit and integration tests
   - Runtime verification

---

## Quick Reference: Implementation Phases (Consolidated)

### Phase 1: Add OBO Methods to Operation Classes (6 hours)

#### 1.1 ContainerOperations - Add User Context Method (1 hour)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/ContainerOperations.cs`

**Add this method after existing methods:**

```csharp
/// <summary>
/// Lists containers accessible to the user (OBO flow).
/// </summary>
/// <param name="userToken">User's bearer token for OBO flow</param>
public async Task<IList<ContainerDto>> ListContainersAsUserAsync(
    string userToken,
    Guid containerTypeId,
    CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(userToken))
    {
        throw new ArgumentException("User access token is required for OBO operations", nameof(userToken));
    }

    try
    {
        // Create Graph client with user token (OBO flow)
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        // Query containers with user's permissions
        var containers = await graphClient.Storage.FileStorage.Containers
            .GetAsync(requestConfig =>
            {
                requestConfig.QueryParameters.Filter = $"containerTypeId eq {containerTypeId}";
            }, ct);

        if (containers?.Value == null)
        {
            _logger.LogInformation("No containers found for containerTypeId {ContainerTypeId} (user context)", containerTypeId);
            return new List<ContainerDto>();
        }

        var result = containers.Value
            .Select(c => new ContainerDto
            {
                Id = c.Id ?? string.Empty,
                DisplayName = c.DisplayName ?? string.Empty,
                ContainerTypeId = c.ContainerTypeId ?? Guid.Empty,
                CreatedDateTime = c.CreatedDateTime ?? DateTimeOffset.MinValue
            })
            .ToList();

        _logger.LogInformation("Listed {Count} containers for containerTypeId {ContainerTypeId} (user context)",
            result.Count, containerTypeId);

        return result;
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to list containers for user (containerTypeId: {ContainerTypeId})", containerTypeId);
        throw;
    }
}
```

---

#### 1.2 DriveItemOperations - Add User Context Methods (2 hours)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs`

**Add these methods after existing methods:**

```csharp
// =============================================================================
// USER CONTEXT METHODS (OBO Flow)
// =============================================================================

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

    try
    {
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        // Get drive for container
        var drive = await graphClient.Storage.FileStorage.Containers[containerId]
            .Drive.GetAsync(cancellationToken: ct);

        if (drive?.Id == null)
        {
            _logger.LogWarning("Drive not found for container {ContainerId} (user context)", containerId);
            return new ListingResponse
            {
                Items = new List<DriveItemDto>(),
                TotalCount = 0
            };
        }

        // List children with OData query parameters
        var children = await graphClient.Drives[drive.Id].Root.Children
            .GetAsync(requestConfig =>
            {
                requestConfig.QueryParameters.Top = parameters.Top;
                requestConfig.QueryParameters.Skip = parameters.Skip;
                requestConfig.QueryParameters.Orderby = new[] { $"{parameters.OrderBy} {parameters.OrderDir}" };
            }, ct);

        var items = children?.Value?
            .Select(item => new DriveItemDto
            {
                Id = item.Id ?? string.Empty,
                Name = item.Name ?? string.Empty,
                Size = item.Size ?? 0,
                CreatedDateTime = item.CreatedDateTime ?? DateTimeOffset.MinValue,
                LastModifiedDateTime = item.LastModifiedDateTime ?? DateTimeOffset.MinValue,
                WebUrl = item.WebUrl ?? string.Empty,
                IsFolder = item.Folder != null
            })
            .ToList() ?? new List<DriveItemDto>();

        _logger.LogInformation("Listed {Count} children for container {ContainerId} (user context)",
            items.Count, containerId);

        return new ListingResponse
        {
            Items = items,
            TotalCount = items.Count,
            Top = parameters.Top,
            Skip = parameters.Skip
        };
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to list children for user (containerId: {ContainerId})", containerId);
        throw;
    }
}

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

    try
    {
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        // Get file metadata
        var driveItem = await graphClient.Drives[driveId].Items[itemId]
            .GetAsync(cancellationToken: ct);

        if (driveItem?.File == null)
        {
            _logger.LogWarning("File not found: {DriveId}/{ItemId} (user context)", driveId, itemId);
            return null;
        }

        // Check ETag for conditional requests (304 Not Modified)
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == driveItem.ETag)
        {
            _logger.LogInformation("ETag match - returning 304 Not Modified");
            return new FileContentResponse
            {
                Content = Stream.Null,
                ContentLength = 0,
                ETag = driveItem.ETag ?? string.Empty,
                IsRangeRequest = false
            };
        }

        // Download content (with range if specified)
        Stream? contentStream;
        long contentLength;
        string? contentRangeHeader = null;
        bool isRangeRequest = false;

        if (range != null)
        {
            // Range request - download partial content
            var httpClient = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}/content");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", userToken);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(
                range.Start, range.End);

            var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                _logger.LogWarning("Range not satisfiable: {Range}", range);
                return null;
            }

            response.EnsureSuccessStatusCode();

            contentStream = await response.Content.ReadAsStreamAsync(ct);
            contentLength = response.Content.Headers.ContentLength ?? 0;
            contentRangeHeader = response.Content.Headers.ContentRange?.ToString();
            isRangeRequest = true;

            _logger.LogInformation("Downloaded partial content: {ContentRange}", contentRangeHeader);
        }
        else
        {
            // Full content download
            contentStream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (contentStream == null)
            {
                _logger.LogWarning("Failed to download content for {DriveId}/{ItemId}", driveId, itemId);
                return null;
            }

            contentLength = driveItem.Size ?? 0;
            _logger.LogInformation("Downloaded full content: {Size} bytes", contentLength);
        }

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
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to download file for user (driveId: {DriveId}, itemId: {ItemId})",
            driveId, itemId);
        throw;
    }
}

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

    try
    {
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        // Build update payload
        var driveItem = new DriveItem();

        if (!string.IsNullOrEmpty(request.Name))
        {
            driveItem.Name = request.Name;
        }

        if (!string.IsNullOrEmpty(request.ParentReferenceId))
        {
            driveItem.ParentReference = new ItemReference
            {
                Id = request.ParentReferenceId
            };
        }

        // Update item
        var updatedItem = await graphClient.Drives[driveId].Items[itemId]
            .PatchAsync(driveItem, cancellationToken: ct);

        if (updatedItem == null)
        {
            _logger.LogWarning("Failed to update item {DriveId}/{ItemId} (user context)", driveId, itemId);
            return null;
        }

        _logger.LogInformation("Updated item {ItemId}: Name={Name}, Parent={Parent}",
            itemId, request.Name, request.ParentReferenceId);

        return new DriveItemDto
        {
            Id = updatedItem.Id ?? string.Empty,
            Name = updatedItem.Name ?? string.Empty,
            Size = updatedItem.Size ?? 0,
            CreatedDateTime = updatedItem.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastModifiedDateTime = updatedItem.LastModifiedDateTime ?? DateTimeOffset.MinValue,
            WebUrl = updatedItem.WebUrl ?? string.Empty,
            IsFolder = updatedItem.Folder != null
        };
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to update item for user (driveId: {DriveId}, itemId: {ItemId})",
            driveId, itemId);
        throw;
    }
}

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

    try
    {
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        await graphClient.Drives[driveId].Items[itemId]
            .DeleteAsync(cancellationToken: ct);

        _logger.LogInformation("Deleted item {DriveId}/{ItemId} (user context)", driveId, itemId);
        return true;
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
    {
        _logger.LogWarning("Item not found for deletion: {DriveId}/{ItemId}", driveId, itemId);
        return false;
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to delete item for user (driveId: {DriveId}, itemId: {ItemId})",
            driveId, itemId);
        throw;
    }
}
```

---

#### 1.3 UploadSessionManager - Add User Context Methods (2 hours)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs`

**Add these methods after existing methods:**

```csharp
// =============================================================================
// USER CONTEXT METHODS (OBO Flow)
// =============================================================================

/// <summary>
/// Uploads a small file (< 4MB) as the user (OBO flow).
/// </summary>
public async Task<FileHandleDto?> UploadSmallAsUserAsync(
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
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        // Get drive ID for container
        var drive = await graphClient.Storage.FileStorage.Containers[containerId]
            .Drive.GetAsync(cancellationToken: ct);

        if (drive?.Id == null)
        {
            _logger.LogWarning("Drive not found for container {ContainerId} (user context)", containerId);
            return null;
        }

        // Validate content size (< 4MB for small upload)
        if (content.CanSeek && content.Length > 4 * 1024 * 1024)
        {
            _logger.LogWarning("Content too large for small upload: {Size} bytes", content.Length);
            throw new ArgumentException("Content exceeds 4MB limit. Use chunked upload.");
        }

        // Upload file
        var uploadedItem = await graphClient.Drives[drive.Id].Root
            .ItemWithPath(path)
            .Content
            .PutAsync(content, cancellationToken: ct);

        if (uploadedItem == null)
        {
            _logger.LogError("Upload failed for path {Path} in container {ContainerId} (user context)",
                path, containerId);
            return null;
        }

        _logger.LogInformation("Uploaded file {Path} in container {ContainerId} (user context, {Size} bytes)",
            path, containerId, uploadedItem.Size ?? 0);

        return new FileHandleDto
        {
            Id = uploadedItem.Id ?? string.Empty,
            Name = uploadedItem.Name ?? string.Empty,
            Size = uploadedItem.Size ?? 0,
            CreatedDateTime = uploadedItem.CreatedDateTime ?? DateTimeOffset.MinValue,
            LastModifiedDateTime = uploadedItem.LastModifiedDateTime ?? DateTimeOffset.MinValue,
            WebUrl = uploadedItem.WebUrl
        };
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
    {
        _logger.LogWarning("Access denied uploading to container {ContainerId} (user context)", containerId);
        throw new UnauthorizedAccessException($"Access denied to container {containerId}", ex);
    }
    catch (ServiceException ex) when (ex.ResponseStatusCode == 413)
    {
        _logger.LogWarning("Content too large for path {Path}", path);
        throw new ArgumentException("Content size exceeds limit. Use chunked upload.", ex);
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to upload file for user (container: {ContainerId}, path: {Path})",
            containerId, path);
        throw;
    }
}

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
        var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

        var uploadSession = await graphClient.Drives[driveId].Root
            .ItemWithPath(path)
            .CreateUploadSession
            .PostAsync(new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", conflictBehavior.ToString().ToLowerInvariant() }
                    }
                }
            }, cancellationToken: ct);

        if (uploadSession == null)
        {
            _logger.LogError("Failed to create upload session for path {Path} in drive {DriveId} (user context)",
                path, driveId);
            return null;
        }

        _logger.LogInformation("Created upload session for {Path} in drive {DriveId} (user context, expires: {Expiration})",
            path, driveId, uploadSession.ExpirationDateTime);

        return new UploadSessionResponse
        {
            UploadUrl = uploadSession.UploadUrl ?? string.Empty,
            ExpirationDateTime = uploadSession.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(1),
            NextExpectedRanges = uploadSession.NextExpectedRanges?.ToList() ?? new List<string>()
        };
    }
    catch (ServiceException ex)
    {
        _logger.LogError(ex, "Failed to create upload session for user (driveId: {DriveId}, path: {Path})",
            driveId, path);
        throw;
    }
}

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
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadSessionUrl);

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", userToken);
        request.Headers.Add("Content-Range", contentRange);
        request.Content = new ByteArrayContent(chunkData);
        request.Content.Headers.ContentLength = chunkData.Length;

        var response = await httpClient.SendAsync(request, ct);

        var statusCode = (int)response.StatusCode;
        DriveItem? completedItem = null;

        if (statusCode == 200 || statusCode == 201)
        {
            // Upload complete - parse DriveItem from response
            var json = await response.Content.ReadAsStringAsync(ct);
            completedItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(json);
        }

        _logger.LogInformation("Uploaded chunk (range: {Range}, status: {Status})",
            contentRange, statusCode);

        return new ChunkUploadResponse
        {
            StatusCode = statusCode,
            CompletedItem = completedItem
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to upload chunk for user (session: {Session})", uploadSessionUrl);
        throw;
    }
}
```

---

#### 1.4 Create UserOperations Class (1 hour)

**NEW FILE:** `src/api/Spe.Bff.Api/Infrastructure/Graph/UserOperations.cs`

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
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<UserOperations> _logger;

    public UserOperations(IGraphClientFactory graphClientFactory, ILogger<UserOperations> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
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
            var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

            var user = await graphClient.Me.GetAsync(cancellationToken: ct);

            if (user == null)
            {
                _logger.LogWarning("Failed to retrieve user info");
                return null;
            }

            _logger.LogInformation("Retrieved user info for {UserPrincipalName}", user.UserPrincipalName);

            return new UserInfoResponse
            {
                Id = user.Id ?? string.Empty,
                DisplayName = user.DisplayName ?? string.Empty,
                UserPrincipalName = user.UserPrincipalName ?? string.Empty,
                Mail = user.Mail ?? string.Empty,
                GivenName = user.GivenName ?? string.Empty,
                Surname = user.Surname ?? string.Empty
            };
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Failed to get user info");
            throw;
        }
    }

    /// <summary>
    /// Gets user capabilities for a specific container.
    /// Returns what operations the user can perform based on their permissions.
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
            var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

            // Get container permissions for user
            var permissions = await graphClient.Storage.FileStorage.Containers[containerId]
                .Permissions
                .GetAsync(cancellationToken: ct);

            // Determine capabilities based on permission roles
            bool canRead = false;
            bool canWrite = false;
            bool canDelete = false;
            bool canShare = false;

            if (permissions?.Value != null)
            {
                foreach (var permission in permissions.Value)
                {
                    var roles = permission.Roles ?? new List<string>();

                    if (roles.Contains("owner", StringComparer.OrdinalIgnoreCase))
                    {
                        canRead = canWrite = canDelete = canShare = true;
                        break;
                    }

                    if (roles.Contains("member", StringComparer.OrdinalIgnoreCase) ||
                        roles.Contains("write", StringComparer.OrdinalIgnoreCase))
                    {
                        canRead = canWrite = canDelete = true;
                    }

                    if (roles.Contains("reader", StringComparer.OrdinalIgnoreCase) ||
                        roles.Contains("read", StringComparer.OrdinalIgnoreCase))
                    {
                        canRead = true;
                    }
                }
            }

            _logger.LogInformation("Retrieved user capabilities for container {ContainerId}: Read={Read}, Write={Write}, Delete={Delete}, Share={Share}",
                containerId, canRead, canWrite, canDelete, canShare);

            return new UserCapabilitiesResponse
            {
                ContainerId = containerId,
                CanRead = canRead,
                CanWrite = canWrite,
                CanDelete = canDelete,
                CanShare = canShare
            };
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Failed to get user capabilities for container {ContainerId}", containerId);
            throw;
        }
    }
}
```

---

### Phase 2: Update SpeFileStore Facade (1 hour)

**File:** `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**Update constructor to inject UserOperations:**

```csharp
public class SpeFileStore
{
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadManager;
    private readonly UserOperations _userOps;  // ADD THIS

    public SpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadManager,
        UserOperations userOps)  // ADD THIS PARAMETER
    {
        _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
        _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
        _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
        _userOps = userOps ?? throw new ArgumentNullException(nameof(userOps));  // ADD THIS
    }

    // ... existing app-only methods ...

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow) - ADD THESE AT THE END
    // =============================================================================

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
    public Task<FileHandleDto?> UploadSmallAsUserAsync(
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

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsUserAsync(
        string userToken,
        string containerId,
        CancellationToken ct = default)
        => _userOps.GetUserCapabilitiesAsync(userToken, containerId, ct);
}
```

---

### Phase 3: Create TokenHelper Utility (30 minutes)

**NEW FILE:** `src/api/Spe.Bff.Api/Infrastructure/Auth/TokenHelper.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Auth;

/// <summary>
/// Helper for extracting bearer tokens from HttpContext.
/// Consolidates token extraction logic used across OBO endpoints.
/// </summary>
public static class TokenHelper
{
    /// <summary>
    /// Extracts bearer token from Authorization header.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Thrown if token missing or malformed</exception>
    public static string ExtractBearerToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(authHeader))
        {
            throw new UnauthorizedAccessException("Missing Authorization header");
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Invalid Authorization header format. Expected 'Bearer {token}'");
        }

        return authHeader["Bearer ".Length..].Trim();
    }
}
```

---

### Phase 4: Update Endpoints (2 hours)

#### 4.1 Update OBOEndpoints.cs

**File:** `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

**Replace all `IOboSpeService` with `SpeFileStore` and use `TokenHelper`:**

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Infrastructure.Auth;  // ADD THIS
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Api;

public static class OBOEndpoints
{
    public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
    {
        // GET: list children (as user) with paging, ordering, and metadata
        app.MapGet("/api/obo/containers/{id}/children", async (
            string id,
            int? top,
            int? skip,
            string? orderBy,
            string? orderDir,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED FROM IOboSpeService
            CancellationToken ct) =>
        {
            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                var parameters = new Spe.Bff.Api.Models.ListingParameters(
                    Top: top ?? 50,
                    Skip: skip ?? 0,
                    OrderBy: orderBy ?? "name",
                    OrderDir: orderDir ?? "asc"
                );

                var result = await speFileStore.ListChildrenAsUserAsync(userToken, id, parameters, ct);  // CHANGED
                return TypedResults.Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-read");

        // PUT: small upload (as user)
        app.MapPut("/api/obo/containers/{id}/files/{*path}", async (
            string id, string path, HttpRequest req, HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            var (ok, err) = ValidatePathForOBO(path);
            if (!ok) return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["path"] = new[] { err! } });

            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                using var ms = new MemoryStream();
                await req.Body.CopyToAsync(ms, ct);
                ms.Position = 0;

                var item = await speFileStore.UploadSmallAsUserAsync(userToken, id, path, ms, ct);  // CHANGED
                return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");

        // POST: create upload session (as user)
        app.MapPost("/api/obo/drives/{driveId}/upload-session", async (
            string driveId,
            string path,
            string? conflictBehavior,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ProblemDetailsHelper.ValidationError("path query parameter is required");
            }

            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED
                var behavior = Spe.Bff.Api.Models.ConflictBehaviorExtensions.ParseConflictBehavior(conflictBehavior);
                var session = await speFileStore.CreateUploadSessionAsUserAsync(userToken, driveId, path, behavior, ct);  // CHANGED

                return session == null
                    ? TypedResults.Problem(statusCode: 500, title: "Failed to create upload session")
                    : TypedResults.Ok(session);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");

        // PUT: upload chunk (as user)
        app.MapPut("/api/obo/upload-session/chunk", async (
            HttpRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                // Get required headers
                var uploadSessionUrl = request.Headers["Upload-Session-Url"].FirstOrDefault();
                var contentRange = request.Headers["Content-Range"].FirstOrDefault();

                if (string.IsNullOrWhiteSpace(uploadSessionUrl))
                {
                    return ProblemDetailsHelper.ValidationError("Upload-Session-Url header is required");
                }

                if (string.IsNullOrWhiteSpace(contentRange))
                {
                    return ProblemDetailsHelper.ValidationError("Content-Range header is required");
                }

                // Read chunk data from request body
                using var ms = new MemoryStream();
                await request.Body.CopyToAsync(ms, ct);
                var chunkData = ms.ToArray();

                if (chunkData.Length == 0)
                {
                    return ProblemDetailsHelper.ValidationError("Request body cannot be empty");
                }

                var result = await speFileStore.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);  // CHANGED

                return result.StatusCode switch
                {
                    200 => TypedResults.Ok(result.CompletedItem), // Upload complete
                    201 => TypedResults.Created("", result.CompletedItem), // Upload complete
                    202 => TypedResults.Accepted("", result.CompletedItem), // More chunks expected
                    400 => TypedResults.BadRequest("Invalid chunk or Content-Range"),
                    413 => TypedResults.Problem(statusCode: 413, title: "Chunk too large"),
                    499 => TypedResults.Problem(statusCode: 499, title: "Client closed request"),
                    _ => TypedResults.Problem(statusCode: 500, title: "Upload failed")
                };
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Upload chunk failed");
            }
        }).RequireRateLimiting("graph-write");

        // PATCH: update item (rename/move)
        app.MapPatch("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            UpdateFileRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return ProblemDetailsHelper.ValidationError("itemId is required");
                }

                if (request == null || (string.IsNullOrEmpty(request.Name) && string.IsNullOrEmpty(request.ParentReferenceId)))
                {
                    return ProblemDetailsHelper.ValidationError("At least one of 'name' or 'parentReferenceId' must be provided");
                }

                var updatedItem = await speFileStore.UpdateItemAsUserAsync(userToken, driveId, itemId, request, ct);  // CHANGED

                return updatedItem == null
                    ? TypedResults.NotFound()
                    : TypedResults.Ok(updatedItem);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
        }).RequireRateLimiting("graph-write");

        // GET: download content with range support (enhanced)
        app.MapGet("/api/obo/drives/{driveId}/items/{itemId}/content", async (
            string driveId,
            string itemId,
            HttpRequest request,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return ProblemDetailsHelper.ValidationError("itemId is required");
                }

                // Parse Range header
                var rangeHeader = request.Headers["Range"].FirstOrDefault();
                var range = Spe.Bff.Api.Models.RangeHeader.Parse(rangeHeader);

                // Parse If-None-Match header (for ETag-based caching)
                var ifNoneMatch = request.Headers["If-None-Match"].FirstOrDefault();

                var fileContent = await speFileStore.DownloadFileWithRangeAsUserAsync(userToken, driveId, itemId, range, ifNoneMatch, ct);  // CHANGED

                if (fileContent == null)
                {
                    return range != null
                        ? TypedResults.Problem(statusCode: 416, title: "Range Not Satisfiable") // 416
                        : TypedResults.NotFound();
                }

                // Handle ETag match (304 Not Modified)
                if (fileContent.ContentLength == 0 && fileContent.Content == Stream.Null)
                {
                    return TypedResults.StatusCode(304); // Not Modified
                }

                var response = fileContent.IsRangeRequest
                    ? TypedResults.Stream(fileContent.Content, fileContent.ContentType, enableRangeProcessing: true)
                    : TypedResults.Stream(fileContent.Content, fileContent.ContentType);

                // Set headers
                ctx.Response.Headers.ETag = $"\"{fileContent.ETag}\"";
                ctx.Response.Headers.AcceptRanges = "bytes";

                if (fileContent.IsRangeRequest)
                {
                    ctx.Response.StatusCode = 206; // Partial Content
                    ctx.Response.Headers.ContentRange = fileContent.ContentRangeHeader;
                }

                return response;
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Download failed");
            }
        }).RequireRateLimiting("graph-read");

        // DELETE: delete item (as user)
        app.MapDelete("/api/obo/drives/{driveId}/items/{itemId}", async (
            string driveId,
            string itemId,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED
            CancellationToken ct) =>
        {
            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    return ProblemDetailsHelper.ValidationError("itemId is required");
                }

                var deleted = await speFileStore.DeleteItemAsUserAsync(userToken, driveId, itemId, ct);  // CHANGED

                return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Unauthorized();
            }
            catch (ServiceException ex)
            {
                return ProblemDetailsHelper.FromGraphException(ex);
            }
            catch (Exception)
            {
                return TypedResults.Problem(statusCode: 500, title: "Delete failed");
            }
        }).RequireRateLimiting("graph-write");

        return app;
    }

    // Keep helper method (no changes)
    private static (bool ok, string? error) ValidatePathForOBO(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (false, "path is required");
        if (path.EndsWith("/", StringComparison.Ordinal)) return (false, "path must not end with '/'");
        if (path.Contains("..")) return (false, "path must not contain '..'");
        foreach (var ch in path) if (char.IsControl(ch)) return (false, "path contains control characters");
        if (path.Length > 1024) return (false, "path too long");
        return (true, null);
    }
}
```

#### 4.2 Update UserEndpoints.cs

**File:** `src/api/Spe.Bff.Api/Api/UserEndpoints.cs`

**Replace all `IOboSpeService` with `SpeFileStore` and use `TokenHelper`:**

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Spe.Bff.Api.Infrastructure.Errors;
using Spe.Bff.Api.Infrastructure.Graph;  // CHANGED
using Spe.Bff.Api.Infrastructure.Auth;  // ADD THIS

namespace Spe.Bff.Api.Api;

/// <summary>
/// User identity and capabilities endpoints following ADR-008.
/// Groups all user-related operations with consistent error handling.
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/me - Get current user info
        app.MapGet("/api/me", GetCurrentUserAsync)
            .RequireRateLimiting("graph-read");

        // GET /api/me/capabilities?containerId={containerId} - Get user capabilities for container
        app.MapGet("/api/me/capabilities", async (
            string? containerId,
            HttpContext ctx,
            [FromServices] SpeFileStore speFileStore,  // CHANGED FROM IOboSpeService
            [FromServices] ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var traceId = ctx.TraceIdentifier;

            if (string.IsNullOrWhiteSpace(containerId))
            {
                return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["containerId"] = ["containerId query parameter is required"]
                });
            }

            try
            {
                var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

                logger.LogInformation("Getting user capabilities for container {ContainerId}", containerId);
                var capabilities = await speFileStore.GetUserCapabilitiesAsUserAsync(userToken, containerId, ct);  // CHANGED
                return TypedResults.Ok(capabilities);
            }
            catch (UnauthorizedAccessException)
            {
                return TypedResults.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Bearer token is required",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve user capabilities");
                return TypedResults.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred while retrieving user capabilities",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }
        })
        .RequireRateLimiting("graph-read");

        return app;
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext ctx,
        SpeFileStore speFileStore,  // CHANGED FROM IOboSpeService
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var traceId = ctx.TraceIdentifier;

        try
        {
            var userToken = TokenHelper.ExtractBearerToken(ctx);  // CHANGED

            logger.LogInformation("Getting user information");
            var userInfo = await speFileStore.GetUserInfoAsync(userToken, ct);  // CHANGED

            if (userInfo == null)
            {
                return TypedResults.Problem(
                    statusCode: 401,
                    title: "Unauthorized",
                    detail: "Invalid or expired token",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            return TypedResults.Ok(userInfo);
        }
        catch (UnauthorizedAccessException)
        {
            return TypedResults.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Bearer token is required",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve user information");
            return TypedResults.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving user information",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    // REMOVE: GetBearer() helper method - now using TokenHelper.ExtractBearerToken()
}
```

---

### Phase 5: Delete Interface Files (30 minutes)

**Delete these files:**
1. `src/api/Spe.Bff.Api/Infrastructure/Graph/ISpeService.cs`
2. `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeService.cs`
3. `src/api/Spe.Bff.Api/Services/IOboSpeService.cs`
4. `src/api/Spe.Bff.Api/Services/OboSpeService.cs`

**Update test mocks (if needed):**
Check `tests/unit/Spe.Bff.Api.Tests/Mocks/MockOboSpeService.cs` - if it's used, replace with mock of `SpeFileStore` instead.

---

### Phase 6: Update DI Registration (30 minutes)

**File:** `src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs`

```csharp
using Spaarke.Core.Auth;
using Spe.Bff.Api.Api.Filters;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE specialized operation classes (Task 3.2)
        services.AddScoped<ContainerOperations>();
        services.AddScoped<DriveItemOperations>();
        services.AddScoped<UploadSessionManager>();
        services.AddScoped<UserOperations>();  // ADD THIS LINE

        // SPE file store facade (delegates to specialized classes)
        services.AddScoped<SpeFileStore>();

        // Document authorization filters
        services.AddScoped<DocumentAuthorizationFilter>(provider =>
            new DocumentAuthorizationFilter(
                provider.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>(),
                "read"));

        return services;
    }
}
```

---

### Phase 7: Build & Test (2 hours)

#### 7.1 Build Verification

```bash
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

**Expected:** 0 errors, same warnings as before

#### 7.2 Run Unit Tests

```bash
dotnet test tests/unit/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj
```

**Fix any broken tests** (update mocks if needed)

#### 7.3 Manual Endpoint Testing

Test each OBO endpoint:
1. `GET /api/me` - User info
2. `GET /api/me/capabilities?containerId=abc` - User capabilities
3. `GET /api/obo/containers/abc/children` - List files
4. `PUT /api/obo/containers/abc/files/test.txt` - Upload file
5. `GET /api/obo/drives/xyz/items/123/content` - Download file
6. `PATCH /api/obo/drives/xyz/items/123` - Rename file
7. `DELETE /api/obo/drives/xyz/items/123` - Delete file

---

## Acceptance Criteria

- [ ] ✅ Build succeeds with 0 errors
- [ ] ✅ No `ISpeService` or `IOboSpeService` interfaces exist
- [ ] ✅ All 9 OBO endpoints use `SpeFileStore` instead of `IOboSpeService`
- [ ] ✅ `UserOperations` class created and registered in DI
- [ ] ✅ `TokenHelper` utility created and used in all OBO endpoints
- [ ] ✅ Unit tests pass
- [ ] ✅ Manual testing of OBO endpoints successful
- [ ] ✅ ADR-007 compliant (single facade for all Graph operations)

---

## Rollback Plan

If issues arise:
1. Revert changes to endpoints (`OBOEndpoints.cs`, `UserEndpoints.cs`)
2. Restore deleted files (`IOboSpeService.cs`, `OboSpeService.cs`)
3. Remove new files (`UserOperations.cs`, `TokenHelper.cs`)
4. Revert `SpeFileStore.cs` and `DocumentsModule.cs` changes
5. Run build verification

---

## Success Metrics

1. **ADR-007 Compliance:** ✅ No interface abstractions
2. **Single Facade:** ✅ All Graph operations via `SpeFileStore`
3. **Modular Design:** ✅ OBO methods in operation classes
4. **Code Reuse:** ✅ App-only and OBO methods in same classes
5. **Maintainability:** ✅ Clear separation of concerns

---

**Estimated Total Time:** 12.5 hours (1.5 days)
**Status:** Ready to implement
**Next Step:** Begin Phase 1.1 (ContainerOperations)
