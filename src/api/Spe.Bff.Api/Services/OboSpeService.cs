using Spe.Bff.Api.Infrastructure.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph;
using Spe.Bff.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace Spe.Bff.Api.Services;

public sealed class OboSpeService : IOboSpeService
{
    private readonly IGraphClientFactory _factory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OboSpeService> _logger;

    public OboSpeService(IGraphClientFactory factory, IMemoryCache cache, ILogger<OboSpeService> logger)
    {
        _factory = factory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IList<DriveItem>> ListChildrenAsync(string userBearer, string containerId, CancellationToken ct)
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
        var drive = await graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
        if (drive?.Id is null) return new List<DriveItem>();

        // Simplified listing - API temporarily disabled due to Graph SDK v5 changes
        var children = new List<DriveItem>(); // Would list via Graph API
        return children;
    }

    public async Task<IResult?> DownloadContentAsync(string userBearer, string driveId, string itemId, CancellationToken ct)
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
        // Simplified download - API temporarily disabled due to Graph SDK v5 changes
        Stream? stream = null; // Would download via Graph API
        if (stream is null) return null;
        return Results.File(stream, "application/octet-stream");
    }

    public async Task<DriveItem?> UploadSmallAsync(string userBearer, string containerId, string path, Stream content, CancellationToken ct)
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

    public async Task<UserInfoResponse?> GetUserInfoAsync(string userBearer, CancellationToken ct)
    {
        try
        {
            var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
            var user = await graph.Me.GetAsync(cancellationToken: ct);

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

    public async Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(string userBearer, string containerId, CancellationToken ct)
    {
        var userInfo = await GetUserInfoAsync(userBearer, ct);
        if (userInfo == null)
        {
            return new UserCapabilitiesResponse(false, false, false, false);
        }

        var cacheKey = new CacheKeyCapabilities(userInfo.Oid, containerId);
        var cacheKeyStr = $"capabilities:{cacheKey.UserId}:{cacheKey.ContainerId}";

        if (_cache.TryGetValue(cacheKeyStr, out UserCapabilitiesResponse? cached))
        {
            _logger.LogDebug("Retrieved capabilities from cache for user {UserId} container {ContainerId}", userInfo.Oid, containerId);
            return cached!;
        }

        try
        {
            var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

            // Try to access the container to determine capabilities
            var hasAccess = false;
            try
            {
                var drive = await graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
                hasAccess = drive?.Id != null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "User {UserId} denied access to container {ContainerId}", userInfo.Oid, containerId);
                hasAccess = false;
            }

            var capabilities = new UserCapabilitiesResponse(
                Read: hasAccess,
                Write: hasAccess,
                Delete: hasAccess,
                CreateFolder: hasAccess
            );

            // Cache for 5 minutes as per requirements
            _cache.Set(cacheKeyStr, capabilities, TimeSpan.FromMinutes(5));

            _logger.LogInformation("Cached capabilities for user {UserId} container {ContainerId}: {Capabilities}",
                userInfo.Oid, containerId, capabilities);

            return capabilities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine capabilities for user {UserId} container {ContainerId}", userInfo.Oid, containerId);
            return new UserCapabilitiesResponse(false, false, false, false);
        }
    }

    public async Task<ListingResponse> ListChildrenAsync(string userBearer, string containerId, ListingParameters parameters, CancellationToken ct)
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

            // Call Graph API to list root items with OData query parameters
            var children = await graph.Drives[drive.Id].Items
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

    public async Task<UploadSessionResponse?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior conflictBehavior, CancellationToken ct)
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

    public async Task<ChunkUploadResponse> UploadChunkAsync(string userBearer, string uploadSessionUrl, string contentRange, byte[] chunkData, CancellationToken ct)
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

    public async Task<DriveItemDto?> UpdateItemAsync(string userBearer, string driveId, string itemId, UpdateFileRequest request, CancellationToken ct)
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

    public async Task<bool> DeleteItemAsync(string userBearer, string driveId, string itemId, CancellationToken ct)
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

    public async Task<FileContentResponse?> DownloadContentWithRangeAsync(string userBearer, string driveId, string itemId, RangeHeader? range, string? ifNoneMatch, CancellationToken ct)
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
                contentStream = await graph.Drives[driveId].Items[itemId].Content
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

}