# Task 3.2: SpeFileStore Refactoring - Split God Class into Cohesive Modules

**Priority:** MEDIUM (Sprint 3, Phase 3)
**Estimated Effort:** 5-6 days
**Status:** IMPROVES MAINTAINABILITY
**Dependencies:** Task 2.1 (OboSpeService - understanding Graph API patterns)

---

## Context & Problem Statement

The `SpeFileStore` class is a **604-line god class** that mixes multiple concerns and violates Single Responsibility Principle:

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\SpeFileStore.cs` (604 lines)

**Problems**:
1. **Multiple Responsibilities**: Container management, file operations, upload sessions, permissions
2. **Hard to Test**: Monolithic class requires mocking entire Graph SDK
3. **Hard to Extend**: Adding new operations requires modifying large file
4. **Duplicated Patterns**: Same error handling repeated in every method
5. **Mixed Abstractions**: App-only and OBO operations in same class

**Method Breakdown**:
- **Container Operations** (Lines 20-129): CreateContainer, GetContainerDrive
- **Upload Operations** (Lines 131-364): UploadSmall, CreateUploadSession, UploadChunk
- **DriveItem Operations** (Lines 366-604): GetItem, UpdateItem, DeleteItem, ListChildren, Download, Permissions

---

## Goals & Outcomes

### Primary Goals
1. Split `SpeFileStore` into focused, cohesive classes:
   - `ContainerOperations`: Container creation, drive retrieval
   - `DriveItemOperations`: List, get, update, delete file metadata
   - `UploadSessionManager`: Upload session creation and chunked upload
2. Extract common patterns into shared error handling and retry logic
3. Maintain API compatibility (existing code should work without changes)
4. Follow ADR-007 (SPE Storage Seam Minimalism) - minimal abstraction over Graph SDK
5. Improve testability by reducing class size and dependencies

### Success Criteria
- [ ] `SpeFileStore` acts as facade, delegates to specialized classes
- [ ] Each class < 300 lines
- [ ] Single Responsibility Principle followed
- [ ] Existing code continues to work (API compatibility)
- [ ] Unit tests easier to write (smaller scope per class)
- [ ] Common error handling extracted to shared utility
- [ ] Code review: improved readability and maintainability

### Non-Goals
- Changing public API surface (maintain compatibility)
- Performance optimization (Sprint 4+)
- Adding new features (Sprint 4+)
- Repository pattern (ADR-007: avoid over-abstraction)

---

## Architecture & Design

### Current State (Sprint 2) - God Class
```
┌──────────────────────────┐
│      SpeFileStore        │  604 lines
│  (All Responsibilities)  │
│                          │
│ - CreateContainer        │  Container ops
│ - GetContainerDrive      │
│ - UploadSmall            │  Upload ops
│ - CreateUploadSession    │
│ - UploadChunk            │
│ - GetItem                │  DriveItem ops
│ - ListChildren           │
│ - DownloadContent        │
│ - UpdateItem             │
│ - DeleteItem             │
│ - SetPermissions         │  Permission ops
│                          │
│ All use IGraphClient     │
│ Repeated error handling  │
└──────────────────────────┘
```

### Target State (Sprint 3) - Cohesive Modules
```
┌──────────────────────────┐
│      SpeFileStore        │  ~100 lines (Facade)
│  (Coordinates modules)   │
└───────┬──────────────────┘
        │
        ├─────────────────────────────────┐
        │                                 │
        v                                 v
┌──────────────────────┐      ┌──────────────────────┐
│ ContainerOperations  │      │ DriveItemOperations  │
│  ~150 lines          │      │  ~250 lines          │
│                      │      │                      │
│ - CreateContainer    │      │ - ListChildren       │
│ - GetContainer       │      │ - GetItem            │
│ - GetContainerDrive  │      │ - UpdateItem         │
│ - ListContainers     │      │ - DeleteItem         │
└──────────────────────┘      │ - DownloadContent    │
                              └──────────────────────┘
        │
        v
┌──────────────────────┐
│ UploadSessionManager │
│  ~200 lines          │
│                      │
│ - UploadSmall        │
│ - CreateUploadSession│
│ - UploadChunk        │
│ - ResumeUpload       │
└──────────────────────┘

        │
        v
┌──────────────────────┐
│ GraphErrorHandler    │  Shared utility
│  ~50 lines           │
│                      │
│ - HandleServiceEx    │
│ - LogAndThrow        │
└──────────────────────┘
```

---

## Relevant ADRs

### ADR-007: SPE Storage Seam Minimalism
- **Minimal Abstraction**: Direct Graph SDK calls, no repository pattern
- **Thin Wrappers**: Classes wrap Graph operations with domain errors
- **No ORM-like**: Not mimicking Entity Framework or similar

### ADR-010: DI Minimalism
- **IGraphClientFactory**: Only seam for Graph client creation
- **Concrete Classes**: Register concrete classes, not interfaces (unless needed)
- **Constructor Injection**: Inject only IGraphClientFactory, ILogger

---

## Implementation Steps

### Step 1: Create ContainerOperations

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\ContainerOperations.cs`

```csharp
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles SharePoint Embedded container operations.
/// </summary>
public class ContainerOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<ContainerOperations> _logger;

    public ContainerOperations(IGraphClientFactory factory, ILogger<ContainerOperations> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        string? description = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Creating SPE container {DisplayName} with type {ContainerTypeId}",
            displayName, containerTypeId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var container = new FileStorageContainer
            {
                DisplayName = displayName,
                Description = description,
                ContainerTypeId = containerTypeId
            };

            var createdContainer = await graphClient.Storage.FileStorage.Containers
                .PostAsync(container, cancellationToken: ct);

            if (createdContainer == null)
            {
                _logger.LogError("Failed to create container - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Successfully created SPE container {ContainerId}", createdContainer.Id);

            return new ContainerDto(
                createdContainer.Id!,
                createdContainer.DisplayName!,
                createdContainer.Description,
                createdContainer.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to create container {displayName}");
        }
    }

    public async Task<ContainerDto?> GetContainerAsync(string containerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting container {ContainerId}", containerId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var container = await graphClient.Storage.FileStorage.Containers[containerId]
                .GetAsync(cancellationToken: ct);

            if (container == null)
            {
                _logger.LogWarning("Container not found: {ContainerId}", containerId);
                return null;
            }

            return new ContainerDto(
                container.Id!,
                container.DisplayName!,
                container.Description,
                container.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to get container {containerId}");
        }
    }

    public async Task<string?> GetContainerDriveIdAsync(string containerId, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting drive ID for container {ContainerId}", containerId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive?.Id is null)
            {
                _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
                return null;
            }

            _logger.LogInformation("Drive {DriveId} found for container {ContainerId}", drive.Id, containerId);
            return drive.Id;
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to get drive for container {containerId}");
        }
    }
}
```

---

### Step 2: Create DriveItemOperations

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\DriveItemOperations.cs`

```csharp
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles DriveItem operations (list, get, update, delete, download).
/// </summary>
public class DriveItemOperations
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<DriveItemOperations> _logger;

    public DriveItemOperations(IGraphClientFactory factory, ILogger<DriveItemOperations> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<DriveItemDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        int top = 100,
        int skip = 0,
        CancellationToken ct = default)
    {
        var path = itemId ?? "root";
        _logger.LogInformation("Listing children in drive {DriveId}, item {ItemId}", driveId, path);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var requestConfig = new Action<Microsoft.Graph.Drives.Item.Items.Item.Children.ChildrenRequestBuilder.ChildrenRequestBuilderGetRequestConfiguration>(config =>
            {
                config.QueryParameters.Top = top;
                config.QueryParameters.Skip = skip;
            });

            DriveItemCollectionResponse? children;
            if (itemId == null)
            {
                children = await graphClient.Drives[driveId].Root.Children
                    .GetAsync(requestConfig, cancellationToken: ct);
            }
            else
            {
                children = await graphClient.Drives[driveId].Items[itemId].Children
                    .GetAsync(requestConfig, cancellationToken: ct);
            }

            if (children?.Value == null)
            {
                return new List<DriveItemDto>();
            }

            return children.Value.Select(item => new DriveItemDto(
                Id: item.Id!,
                Name: item.Name!,
                Size: item.Size,
                ETag: item.ETag,
                LastModifiedDateTime: item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                ContentType: item.File?.MimeType,
                Folder: item.Folder != null ? new FolderDto(item.Folder.ChildCount) : null
            )).ToList();
        }
        catch (ServiceException ex)
        {
            GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to list children in drive {driveId}");
            return new List<DriveItemDto>();
        }
    }

    public async Task<DriveItem?> GetItemAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting item {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();
            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            return item;
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to get item {itemId}");
        }
    }

    public async Task<DriveItem?> UpdateItemAsync(
        string driveId,
        string itemId,
        DriveItem updates,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Updating item {ItemId} in drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();
            var updatedItem = await graphClient.Drives[driveId].Items[itemId]
                .PatchAsync(updates, cancellationToken: ct);

            _logger.LogInformation("Successfully updated item {ItemId}", itemId);
            return updatedItem;
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to update item {itemId}");
        }
    }

    public async Task<bool> DeleteItemAsync(string driveId, string itemId, CancellationToken ct = default)
    {
        _logger.LogInformation("Deleting item {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();
            await graphClient.Drives[driveId].Items[itemId]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation("Successfully deleted item {ItemId}", itemId);
            return true;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("Item {ItemId} not found (may already be deleted)", itemId);
            return false;
        }
        catch (ServiceException ex)
        {
            GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to delete item {itemId}");
            return false;
        }
    }

    public async Task<Stream?> DownloadContentAsync(
        string driveId,
        string itemId,
        long? rangeStart = null,
        long? rangeEnd = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading content for item {ItemId}", itemId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            if (rangeStart.HasValue && rangeEnd.HasValue)
            {
                // Range download
                var requestConfig = new Action<Microsoft.Graph.Drives.Item.Items.Item.Content.ContentRequestBuilder.ContentRequestBuilderGetRequestConfiguration>(config =>
                {
                    config.Headers.Add("Range", $"bytes={rangeStart}-{rangeEnd}");
                });

                return await graphClient.Drives[driveId].Items[itemId].Content
                    .GetAsync(requestConfig, cancellationToken: ct);
            }
            else
            {
                // Full download
                return await graphClient.Drives[driveId].Items[itemId].Content
                    .GetAsync(cancellationToken: ct);
            }
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to download content for item {itemId}");
        }
    }
}
```

---

### Step 3: Create UploadSessionManager

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\UploadSessionManager.cs`

```csharp
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Manages file upload operations including small uploads and chunked upload sessions.
/// </summary>
public class UploadSessionManager
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<UploadSessionManager> _logger;
    private const long MaxSmallUploadSize = 4 * 1024 * 1024; // 4 MB
    private const long MinChunkSize = 8 * 1024 * 1024; // 8 MB
    private const long MaxChunkSize = 10 * 1024 * 1024; // 10 MB

    public UploadSessionManager(IGraphClientFactory factory, ILogger<UploadSessionManager> logger)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DriveItem?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        if (content.CanSeek && content.Length > MaxSmallUploadSize)
        {
            throw new ArgumentException(
                $"Content size {content.Length} exceeds maximum for small upload ({MaxSmallUploadSize} bytes). Use chunked upload.");
        }

        _logger.LogInformation("Uploading small file to drive {DriveId}, path {Path}", driveId, path);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var uploadedItem = await graphClient.Drives[driveId].Root
                .ItemWithPath(path)
                .Content
                .PutAsync(content, cancellationToken: ct);

            if (uploadedItem == null)
            {
                _logger.LogError("Upload failed for path {Path}", path);
                return null;
            }

            _logger.LogInformation("Successfully uploaded file to {Path}, item ID: {ItemId}", path, uploadedItem.Id);
            return uploadedItem;
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to upload small file to {path}");
        }
    }

    public async Task<UploadSession?> CreateUploadSessionAsync(
        string driveId,
        string path,
        ConflictBehavior conflictBehavior = ConflictBehavior.Replace,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Creating upload session for path {Path}", path);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

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

            var session = await graphClient.Drives[driveId].Root
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

            return session;
        }
        catch (ServiceException ex)
        {
            return GraphErrorHandler.HandleServiceException(ex, _logger,
                $"Failed to create upload session for {path}");
        }
    }

    public async Task<DriveItem?> UploadChunkAsync(
        string uploadUrl,
        byte[] chunkData,
        long rangeStart,
        long rangeEnd,
        long totalSize,
        CancellationToken ct = default)
    {
        ValidateChunkSize(chunkData.Length, rangeStart, rangeEnd, totalSize);

        _logger.LogInformation("Uploading chunk {Start}-{End}/{Total} to session",
            rangeStart, rangeEnd, totalSize);

        try
        {
            using var httpClient = new HttpClient();
            using var content = new ByteArrayContent(chunkData);

            content.Headers.Add("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{totalSize}");
            content.Headers.ContentLength = chunkData.Length;

            var response = await httpClient.PutAsync(uploadUrl, content, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted) // 202
            {
                _logger.LogInformation("Chunk {Start}-{End} accepted, more chunks expected", rangeStart, rangeEnd);
                return null; // More chunks needed
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.Created ||
                     response.StatusCode == System.Net.HttpStatusCode.OK) // 201/200
            {
                var responseContent = await response.Content.ReadAsStringAsync(ct);
                var driveItem = System.Text.Json.JsonSerializer.Deserialize<DriveItem>(responseContent);

                _logger.LogInformation("Upload completed, item ID: {ItemId}", driveItem?.Id);
                return driveItem;
            }
            else
            {
                throw new InvalidOperationException($"Unexpected response: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload chunk {Start}-{End}", rangeStart, rangeEnd);
            throw;
        }
    }

    private void ValidateChunkSize(int chunkSize, long rangeStart, long rangeEnd, long totalSize)
    {
        var expectedSize = rangeEnd - rangeStart + 1;
        if (chunkSize != expectedSize)
        {
            throw new ArgumentException(
                $"Chunk size {chunkSize} does not match range {rangeStart}-{rangeEnd} (expected {expectedSize})");
        }

        bool isLastChunk = rangeEnd + 1 >= totalSize;
        if (!isLastChunk && chunkSize < MinChunkSize)
        {
            throw new ArgumentException(
                $"Chunk size {chunkSize} below minimum {MinChunkSize} (not final chunk)");
        }

        if (chunkSize > MaxChunkSize)
        {
            throw new ArgumentException(
                $"Chunk size {chunkSize} exceeds maximum {MaxChunkSize}");
        }
    }
}
```

---

### Step 4: Create GraphErrorHandler (Shared Utility)

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphErrorHandler.cs`

```csharp
using Microsoft.Graph;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Centralized error handling for Microsoft Graph API exceptions.
/// </summary>
public static class GraphErrorHandler
{
    public static T? HandleServiceException<T>(
        ServiceException ex,
        ILogger logger,
        string operationContext) where T : class
    {
        switch (ex.ResponseStatusCode)
        {
            case 404:
                logger.LogWarning("{Context}: Resource not found - {Error}", operationContext, ex.Message);
                return null;

            case 403:
                logger.LogWarning("{Context}: Access denied - {Error}", operationContext, ex.Message);
                throw new UnauthorizedAccessException($"{operationContext}: Access denied", ex);

            case 429:
                var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta?.TotalSeconds ?? 60;
                logger.LogWarning("{Context}: Throttling, retry after {RetryAfter}s", operationContext, retryAfter);
                throw new InvalidOperationException($"{operationContext}: Service temporarily unavailable due to rate limiting", ex);

            case 416:
                logger.LogWarning("{Context}: Range not satisfiable - {Error}", operationContext, ex.Message);
                return null;

            case 413:
                logger.LogWarning("{Context}: Payload too large - {Error}", operationContext, ex.Message);
                throw new ArgumentException($"{operationContext}: Content too large", ex);

            default:
                logger.LogError(ex, "{Context}: Graph API error {StatusCode} - {Error}",
                    operationContext, ex.ResponseStatusCode, ex.Message);
                throw new InvalidOperationException($"{operationContext}: {ex.Message}", ex);
        }
    }
}
```

---

### Step 5: Refactor SpeFileStore as Facade

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\SpeFileStore.cs`

**Replace entire content with**:
```csharp
namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Facade for SharePoint Embedded operations.
/// Delegates to specialized operation classes for container, drive item, and upload management.
/// </summary>
public class SpeFileStore
{
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadMgr;

    public SpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadMgr)
    {
        _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
        _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
        _uploadMgr = uploadMgr ?? throw new ArgumentNullException(nameof(uploadMgr));
    }

    // Container Operations
    public Task<ContainerDto?> CreateContainerAsync(Guid containerTypeId, string displayName, string? description = null, CancellationToken ct = default)
        => _containerOps.CreateContainerAsync(containerTypeId, displayName, description, ct);

    public Task<ContainerDto?> GetContainerAsync(string containerId, CancellationToken ct = default)
        => _containerOps.GetContainerAsync(containerId, ct);

    public Task<string?> GetContainerDriveIdAsync(string containerId, CancellationToken ct = default)
        => _containerOps.GetContainerDriveIdAsync(containerId, ct);

    // DriveItem Operations
    public Task<List<DriveItemDto>> ListChildrenAsync(string driveId, string? itemId = null, int top = 100, int skip = 0, CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsync(driveId, itemId, top, skip, ct);

    public Task<DriveItem?> GetItemAsync(string driveId, string itemId, CancellationToken ct = default)
        => _driveItemOps.GetItemAsync(driveId, itemId, ct);

    public Task<DriveItem?> UpdateItemAsync(string driveId, string itemId, DriveItem updates, CancellationToken ct = default)
        => _driveItemOps.UpdateItemAsync(driveId, itemId, updates, ct);

    public Task<bool> DeleteItemAsync(string driveId, string itemId, CancellationToken ct = default)
        => _driveItemOps.DeleteItemAsync(driveId, itemId, ct);

    public Task<Stream?> DownloadContentAsync(string driveId, string itemId, long? rangeStart = null, long? rangeEnd = null, CancellationToken ct = default)
        => _driveItemOps.DownloadContentAsync(driveId, itemId, rangeStart, rangeEnd, ct);

    // Upload Operations
    public Task<DriveItem?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct = default)
        => _uploadMgr.UploadSmallAsync(driveId, path, content, ct);

    public Task<UploadSession?> CreateUploadSessionAsync(string driveId, string path, ConflictBehavior conflictBehavior = ConflictBehavior.Replace, CancellationToken ct = default)
        => _uploadMgr.CreateUploadSessionAsync(driveId, path, conflictBehavior, ct);

    public Task<DriveItem?> UploadChunkAsync(string uploadUrl, byte[] chunkData, long rangeStart, long rangeEnd, long totalSize, CancellationToken ct = default)
        => _uploadMgr.UploadChunkAsync(uploadUrl, chunkData, rangeStart, rangeEnd, totalSize, ct);
}
```

---

### Step 6: Update DI Registration

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs`

```csharp
// Register specialized operation classes
builder.Services.AddScoped<ContainerOperations>();
builder.Services.AddScoped<DriveItemOperations>();
builder.Services.AddScoped<UploadSessionManager>();

// Register facade (maintains API compatibility)
builder.Services.AddScoped<SpeFileStore>();
```

---

## AI Coding Prompts

### Prompt 1: Create Specialized Operation Classes
```
Refactor SpeFileStore into focused operation classes:

Context:
- Current file: C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\SpeFileStore.cs (604 lines)
- God class mixing container, drive item, and upload operations
- Need to split into ContainerOperations, DriveItemOperations, UploadSessionManager

Requirements:
1. Create ContainerOperations: CreateContainer, GetContainer, GetContainerDriveId
2. Create DriveItemOperations: List, Get, Update, Delete, Download
3. Create UploadSessionManager: UploadSmall, CreateUploadSession, UploadChunk
4. Each class should inject IGraphClientFactory and ILogger<T>
5. Extract common error handling to GraphErrorHandler static class
6. Each class < 300 lines
7. Maintain same API signatures

Code Quality:
- Senior C# developer standards
- Single Responsibility Principle
- DRY (Don't Repeat Yourself) for error handling
- Comprehensive logging
- Follow ADR-007 (minimal abstraction)

Files to Create:
- ContainerOperations.cs
- DriveItemOperations.cs
- UploadSessionManager.cs
- GraphErrorHandler.cs (static utility)
```

### Prompt 2: Refactor SpeFileStore as Facade
```
Convert SpeFileStore to facade pattern delegating to operation classes:

Context:
- Current 604-line god class
- Need to maintain API compatibility (existing code must work)
- Delegate all operations to specialized classes

Requirements:
1. Inject ContainerOperations, DriveItemOperations, UploadSessionManager
2. All public methods delegate to appropriate operation class
3. Remove all implementation code (keep only delegation)
4. File should be ~100 lines
5. Maintain exact same public API surface

Code Quality:
- Clean delegation (no logic in facade)
- ArgumentNullException.ThrowIfNull for dependencies
- XML doc comments explaining facade pattern

Files to Modify:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\SpeFileStore.cs
```

---

## Testing Strategy

### Unit Tests (Per Class)
1. **ContainerOperations**: Mock IGraphClientFactory, test each method
2. **DriveItemOperations**: Test list, get, update, delete with mocked Graph client
3. **UploadSessionManager**: Test upload validations, chunk size checks
4. **GraphErrorHandler**: Test error code mapping (404, 403, 429, etc.)

### Integration Tests
1. **SpeFileStore Facade**: Verify delegation works end-to-end
2. **Real Graph API**: Test against test SPE container

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] SpeFileStore refactored to facade pattern (~100 lines)
- [ ] ContainerOperations created (<200 lines)
- [ ] DriveItemOperations created (<300 lines)
- [ ] UploadSessionManager created (<250 lines)
- [ ] GraphErrorHandler created (static utility)
- [ ] All classes registered in DI
- [ ] Existing code continues to work (API compatibility)
- [ ] Unit tests updated for new classes
- [ ] Code review: improved readability and maintainability

---

## Completion Criteria

Task is complete when:
1. SpeFileStore acts as facade
2. All operations split into specialized classes
3. Each class follows Single Responsibility Principle
4. Error handling centralized
5. Tests pass
6. Code review approved

**Estimated Completion: 5-6 days**
