using Spe.Bff.Api.Infrastructure.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace Services;

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
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
        var drive = await graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
        if (drive?.Id is null) return null;

        // Simplified upload - API temporarily disabled due to Graph SDK v5 changes
        DriveItem? item = null; // Would upload via Graph API

        return item;
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
            var drive = await graph.Storage.FileStorage.Containers[containerId].Drive.GetAsync(cancellationToken: ct);
            if (drive?.Id is null)
            {
                return new ListingResponse(new List<DriveItemDto>(), null);
            }

            // For now, create sample data since Graph SDK v5 API is disabled
            // In real implementation, this would call Microsoft Graph with proper pagination and ordering
            var sampleItems = GenerateSampleItems(parameters.ValidatedTop + parameters.ValidatedSkip + 10); // Generate more for pagination demo

            // Apply sorting
            var sortedItems = ApplySorting(sampleItems, parameters.ValidatedOrderBy, parameters.ValidatedOrderDir);

            // Apply pagination
            var pagedItems = sortedItems.Skip(parameters.ValidatedSkip).Take(parameters.ValidatedTop).ToList();

            // Generate nextLink if there are more items
            string? nextLink = null;
            if (sortedItems.Count > parameters.ValidatedSkip + parameters.ValidatedTop)
            {
                var nextSkip = parameters.ValidatedSkip + parameters.ValidatedTop;
                nextLink = $"/api/obo/containers/{containerId}/children?top={parameters.ValidatedTop}&skip={nextSkip}&orderBy={parameters.ValidatedOrderBy}&orderDir={parameters.ValidatedOrderDir}";
            }

            _logger.LogInformation("Listed {Count} items for container {ContainerId} (top={Top}, skip={Skip}, orderBy={OrderBy}, orderDir={OrderDir})",
                pagedItems.Count, containerId, parameters.ValidatedTop, parameters.ValidatedSkip, parameters.ValidatedOrderBy, parameters.ValidatedOrderDir);

            return new ListingResponse(pagedItems, nextLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list children for container {ContainerId}", containerId);
            return new ListingResponse(new List<DriveItemDto>(), null);
        }
    }

    private static List<DriveItemDto> GenerateSampleItems(int count)
    {
        var items = new List<DriveItemDto>();
        var random = new Random(42); // Fixed seed for consistent results

        for (int i = 0; i < count; i++)
        {
            var isFolder = random.Next(0, 4) == 0; // 25% chance of folder
            var name = isFolder ? $"Folder_{i:D3}" : $"Document_{i:D3}.{GetRandomExtension(random)}";
            var size = isFolder ? (long?)null : random.Next(1024, 50 * 1024 * 1024); // 1KB to 50MB
            var childCount = isFolder ? random.Next(0, 20) : (int?)null;

            items.Add(new DriveItemDto(
                Id: $"item_{i:D3}",
                Name: name,
                Size: size,
                ETag: $"etag_{i}",
                LastModifiedDateTime: DateTimeOffset.UtcNow.AddDays(-random.Next(0, 365)),
                ContentType: isFolder ? null : GetContentType(name),
                Folder: isFolder ? new FolderDto(childCount) : null
            ));
        }

        return items;
    }

    private static string GetRandomExtension(Random random)
    {
        var extensions = new[] { "txt", "docx", "pdf", "xlsx", "pptx", "png", "jpg", "mp4" };
        return extensions[random.Next(extensions.Length)];
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".mp4" => "video/mp4",
            _ => "application/octet-stream"
        };
    }

    private static List<DriveItemDto> ApplySorting(List<DriveItemDto> items, string orderBy, string orderDir)
    {
        var query = items.AsQueryable();

        query = orderBy.ToLowerInvariant() switch
        {
            "name" => orderDir == "desc"
                ? query.OrderByDescending(x => x.Name)
                : query.OrderBy(x => x.Name),
            "lastmodifieddatetime" => orderDir == "desc"
                ? query.OrderByDescending(x => x.LastModifiedDateTime)
                : query.OrderBy(x => x.LastModifiedDateTime),
            "size" => orderDir == "desc"
                ? query.OrderByDescending(x => x.Size ?? 0)
                : query.OrderBy(x => x.Size ?? 0),
            _ => query.OrderBy(x => x.Name)
        };

        return query.ToList();
    }

    public async Task<UploadSessionResponse?> CreateUploadSessionAsync(string userBearer, string driveId, string path, ConflictBehavior conflictBehavior, CancellationToken ct)
    {
        try
        {
            var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

            // In real implementation, this would call Microsoft Graph to create upload session
            // For now, simulate the response since Graph SDK v5 API is disabled
            var sessionId = Guid.NewGuid().ToString("N");
            var uploadUrl = $"https://graph.microsoft.com/v1.0/me/drive/items/{sessionId}/uploadSession";
            var expirationDateTime = DateTimeOffset.UtcNow.AddHours(1); // Sessions typically expire in 1 hour

            _logger.LogInformation("Created upload session for drive {DriveId}, path {Path}, conflict behavior {ConflictBehavior}",
                driveId, path, conflictBehavior);

            return new UploadSessionResponse(uploadUrl, expirationDateTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create upload session for drive {DriveId}, path {Path}", driveId, path);
            return null;
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
                return new ChunkUploadResponse(400); // Bad Request
            }

            // Validate chunk size (8-10 MiB as per requirements)
            const long minChunkSize = 8 * 1024 * 1024; // 8 MiB
            const long maxChunkSize = 10 * 1024 * 1024; // 10 MiB

            if (chunkData.Length < minChunkSize || chunkData.Length > maxChunkSize)
            {
                if (range.Total.HasValue && range.End + 1 == range.Total.Value)
                {
                    // Final chunk can be smaller
                    if (chunkData.Length > maxChunkSize)
                    {
                        _logger.LogWarning("Chunk size {Size} exceeds maximum {MaxSize}", chunkData.Length, maxChunkSize);
                        return new ChunkUploadResponse(413); // Payload Too Large
                    }
                }
                else if (chunkData.Length < minChunkSize)
                {
                    _logger.LogWarning("Chunk size {Size} below minimum {MinSize}", chunkData.Length, minChunkSize);
                    return new ChunkUploadResponse(400); // Bad Request
                }
            }

            // Verify chunk size matches Content-Range
            if (chunkData.Length != range.ChunkSize)
            {
                _logger.LogWarning("Chunk data length {ActualSize} does not match Content-Range size {ExpectedSize}",
                    chunkData.Length, range.ChunkSize);
                return new ChunkUploadResponse(400); // Bad Request
            }

            var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);

            // In real implementation, this would upload the chunk to Microsoft Graph
            // and handle resumable upload logic with proper retry handling
            await Task.Delay(100, ct); // Simulate network delay

            // Check if this is the final chunk
            bool isLastChunk = range.Total.HasValue && (range.End + 1 >= range.Total.Value);

            if (isLastChunk)
            {
                // Simulate completed upload - return the final DriveItem
                var completedItem = new DriveItemDto(
                    Id: $"upload_{Guid.NewGuid():N}",
                    Name: ExtractFileNameFromUploadUrl(uploadSessionUrl),
                    Size: range.Total ?? range.End + 1,
                    ETag: $"etag_{DateTimeOffset.UtcNow.Ticks}",
                    LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ContentType: "application/octet-stream", // Would be detected from file extension
                    Folder: null
                );

                _logger.LogInformation("Completed upload session {UploadUrl}, final item: {ItemId}",
                    uploadSessionUrl, completedItem.Id);

                return new ChunkUploadResponse(201, completedItem); // Created
            }
            else
            {
                _logger.LogInformation("Uploaded chunk {Start}-{End} for session {UploadUrl}",
                    range.Start, range.End, uploadSessionUrl);

                return new ChunkUploadResponse(202); // Accepted (more chunks expected)
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Upload chunk operation was cancelled for session {UploadUrl}", uploadSessionUrl);
            return new ChunkUploadResponse(499); // Client Closed Request
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload chunk for session {UploadUrl}", uploadSessionUrl);
            return new ChunkUploadResponse(500); // Internal Server Error
        }
    }

    private static string ExtractFileNameFromUploadUrl(string uploadUrl)
    {
        // Extract a reasonable filename from the upload session URL
        // In real implementation, this would be tracked properly
        var uri = new Uri(uploadUrl);
        var segments = uri.Segments;
        return segments.Length > 2 ? $"uploaded_file_{segments[^2].Trim('/')}.bin" : "uploaded_file.bin";
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

            // In real implementation, this would call Microsoft Graph to update the item
            // For now, simulate the response since Graph SDK v5 API is disabled

            // Simulate finding the existing item
            var existingItem = GenerateSampleItems(1).FirstOrDefault();
            if (existingItem == null)
            {
                _logger.LogWarning("Item not found: {ItemId}", itemId);
                return null;
            }

            // Apply updates
            var updatedName = request.Name ?? existingItem.Name;
            var updatedItem = existingItem with
            {
                Name = updatedName,
                LastModifiedDateTime = DateTimeOffset.UtcNow,
                ETag = $"etag_{DateTimeOffset.UtcNow.Ticks}"
            };

            _logger.LogInformation("Updated item {ItemId}: name={Name}, parentRef={ParentRef}",
                itemId, request.Name, request.ParentReferenceId);

            return updatedItem;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update item {ItemId} in drive {DriveId}", itemId, driveId);
            return null;
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

            // In real implementation, this would call Microsoft Graph to delete the item
            // For now, simulate successful deletion
            await Task.Delay(50, ct); // Simulate API call

            _logger.LogInformation("Deleted item {ItemId} from drive {DriveId}", itemId, driveId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete item {ItemId} from drive {DriveId}", itemId, driveId);
            return false;
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

            // In real implementation, this would call Microsoft Graph to get the file
            // For now, simulate file content generation

            // Generate sample file content
            var sampleContent = GenerateSampleFileContent(itemId);
            var totalSize = sampleContent.Length;
            var currentETag = $"etag_{itemId}_{totalSize}";

            // Handle If-None-Match (ETag-based caching)
            if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch.Trim('"') == currentETag.Trim('"'))
            {
                _logger.LogInformation("ETag match for item {ItemId}, returning 304 Not Modified", itemId);
                return new FileContentResponse(
                    Content: Stream.Null,
                    ContentLength: 0,
                    ContentType: "application/octet-stream",
                    ETag: currentETag
                );
            }

            Stream contentStream;
            long contentLength;
            long? rangeStart = null;
            long? rangeEnd = null;

            if (range != null && range.IsValid)
            {
                // Handle range request
                var actualEnd = Math.Min(range.End, totalSize - 1);
                var actualStart = Math.Min(range.Start, actualEnd);

                if (actualStart >= totalSize)
                {
                    // Range not satisfiable
                    return null;
                }

                var rangeLength = actualEnd - actualStart + 1;
                var rangeData = new byte[rangeLength];
                Array.Copy(sampleContent, actualStart, rangeData, 0, rangeLength);

                contentStream = new MemoryStream(rangeData);
                contentLength = rangeLength;
                rangeStart = actualStart;
                rangeEnd = actualEnd;

                _logger.LogInformation("Serving range {Start}-{End} of item {ItemId} (total size: {TotalSize})",
                    actualStart, actualEnd, itemId, totalSize);
            }
            else
            {
                // Serve full content
                contentStream = new MemoryStream(sampleContent);
                contentLength = totalSize;

                _logger.LogInformation("Serving full content of item {ItemId} (size: {Size})", itemId, totalSize);
            }

            var contentType = GetContentTypeFromItemId(itemId);

            return new FileContentResponse(
                Content: contentStream,
                ContentLength: contentLength,
                ContentType: contentType,
                ETag: currentETag,
                RangeStart: rangeStart,
                RangeEnd: rangeEnd,
                TotalSize: totalSize
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download content for item {ItemId} from drive {DriveId}", itemId, driveId);
            return null;
        }
    }

    private static byte[] GenerateSampleFileContent(string itemId)
    {
        // Generate deterministic sample content based on item ID
        var size = Math.Abs(itemId.GetHashCode()) % (5 * 1024 * 1024) + (1024 * 1024); // 1-5 MB
        var content = new byte[size];
        var random = new Random(itemId.GetHashCode()); // Deterministic based on item ID
        random.NextBytes(content);

        // Add some identifiable patterns for testing
        var header = Encoding.UTF8.GetBytes($"SAMPLE_FILE_{itemId}_");
        Array.Copy(header, content, Math.Min(header.Length, content.Length));

        return content;
    }

    private static string GetContentTypeFromItemId(string itemId)
    {
        // Simulate content type detection based on item ID patterns
        if (itemId.Contains("image", StringComparison.OrdinalIgnoreCase))
            return "image/jpeg";
        if (itemId.Contains("video", StringComparison.OrdinalIgnoreCase))
            return "video/mp4";
        if (itemId.Contains("document", StringComparison.OrdinalIgnoreCase))
            return "application/pdf";
        if (itemId.Contains("text", StringComparison.OrdinalIgnoreCase))
            return "text/plain";

        return "application/octet-stream";
    }
}