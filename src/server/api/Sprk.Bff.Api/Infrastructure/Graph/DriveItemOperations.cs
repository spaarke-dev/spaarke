using System.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Handles DriveItem operations for SharePoint Embedded files and folders.
/// Responsible for listing, downloading, deleting, and metadata retrieval.
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

    public async Task<IList<FileHandleDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListChildren");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Listing children in drive {DriveId}, item {ItemId}", driveId, itemId);

        try
        {
            var graphClient = _factory.ForApp();

            DriveItemCollectionResponse? page;

            if (string.IsNullOrEmpty(itemId))
            {
                // List root items - use Items collection directly
                page = await graphClient.Drives[driveId].Items
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = "parentReference/path eq '/drive/root:'";
                    }, cancellationToken: ct);
            }
            else
            {
                // List items in specific folder
                page = await graphClient.Drives[driveId].Items[itemId].Children
                    .GetAsync(cancellationToken: ct);
            }

            if (page?.Value == null)
            {
                _logger.LogWarning("No children found in drive {DriveId}, item {ItemId}", driveId, itemId);
                return new List<FileHandleDto>();
            }

            var result = page.Value;
            _logger.LogInformation("Found {Count} children in drive {DriveId}", result.Count, driveId);

            return result.Select(item => new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl)).ToList();
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Drive {DriveId} or item {ItemId} not found", driveId, itemId);
            return new List<FileHandleDto>();
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error listing children: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to list children: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing children: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "DownloadFile");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Downloading file {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.ForApp();

            var stream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (stream == null)
            {
                _logger.LogWarning("Failed to download file {ItemId} - stream is null", itemId);
                return null;
            }

            _logger.LogInformation("Successfully downloaded file {ItemId}", itemId);
            return stream;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error downloading file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to download file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error downloading file: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "DeleteFile");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Deleting file {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.ForApp();

            await graphClient.Drives[driveId].Items[itemId]
                .DeleteAsync(cancellationToken: ct);

            _logger.LogInformation("Successfully deleted file {ItemId}", itemId);
            return true;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return false;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error deleting file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to delete file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting file: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetFileMetadata");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Getting metadata for file {ItemId} from drive {DriveId}", itemId, driveId);

        try
        {
            var graphClient = _factory.ForApp();

            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            if (item == null)
            {
                _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved metadata for file {ItemId}", itemId);

            return new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error getting file metadata: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get file metadata: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting file metadata: {Error}", ex.Message);
            throw;
        }
    }

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================

    /// <summary>
    /// Lists drive children as the user (OBO flow) with paging and ordering.
    /// </summary>
    public async Task<ListingResponse> ListChildrenAsUserAsync(
        HttpContext ctx,
        string containerId,
        ListingParameters parameters,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListChildrenAsUser");
        activity?.SetTag("containerId", containerId);

        _logger.LogInformation("Listing children for container {ContainerId} (user context)", containerId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

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

    /// <summary>
    /// Downloads file with range support as the user (OBO flow).
    /// Supports partial content (206) and conditional requests (304).
    /// </summary>
    public async Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        RangeHeader? range,
        string? ifNoneMatch,
        CancellationToken ct = default)
    {
        if (!FileOperationExtensions.IsValidItemId(itemId))
        {
            _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
            return null;
        }

        using var activity = Activity.Current;
        activity?.SetTag("operation", "DownloadFileWithRangeAsUser");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Downloading file {DriveId}/{ItemId} (user context)", driveId, itemId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

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
            Stream? contentStream;
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
                Content: contentStream!,  // Guaranteed non-null by null checks above
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

    /// <summary>
    /// Updates drive item (rename/move) as the user (OBO flow).
    /// </summary>
    public async Task<DriveItemDto?> UpdateItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        UpdateFileRequest request,
        CancellationToken ct = default)
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

        using var activity = Activity.Current;
        activity?.SetTag("operation", "UpdateItemAsUser");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Updating item {DriveId}/{ItemId} (user context)", driveId, itemId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

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

    /// <summary>
    /// Deletes drive item as the user (OBO flow).
    /// </summary>
    public async Task<bool> DeleteItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        if (!FileOperationExtensions.IsValidItemId(itemId))
        {
            _logger.LogWarning("Invalid item ID: {ItemId}", itemId);
            return false;
        }

        using var activity = Activity.Current;
        activity?.SetTag("operation", "DeleteItemAsUser");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Deleting item {DriveId}/{ItemId} (user context)", driveId, itemId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

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

    /// <summary>
    /// Get file metadata using user OBO authentication.
    /// Used by AI services that need to access user-uploaded files.
    /// </summary>
    public async Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetFileMetadataAsUser");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Getting metadata for file {ItemId} from drive {DriveId} (OBO)",
            itemId, driveId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

            var item = await graphClient.Drives[driveId].Items[itemId]
                .GetAsync(cancellationToken: ct);

            if (item == null)
            {
                _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved metadata for file {ItemId} (OBO)", itemId);

            return new FileHandleDto(
                item.Id!,
                item.Name!,
                item.ParentReference?.Id,
                item.Size,
                item.CreatedDateTime ?? DateTimeOffset.UtcNow,
                item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
                item.ETag,
                item.Folder != null,
                item.WebUrl);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Access denied getting metadata for file {ItemId}: {Error}", itemId, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error getting file metadata: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get file metadata: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting file metadata: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Download file content using user OBO authentication.
    /// Used by AI services that need to access user-uploaded files.
    /// </summary>
    public async Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "DownloadFileAsUser");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Downloading file {ItemId} from drive {DriveId} (OBO)", itemId, driveId);

        try
        {
            var graphClient = await _factory.ForUserAsync(ctx, ct);

            var stream = await graphClient.Drives[driveId].Items[itemId].Content
                .GetAsync(cancellationToken: ct);

            if (stream == null)
            {
                _logger.LogWarning("Failed to download file {ItemId} - stream is null", itemId);
                return null;
            }

            _logger.LogInformation("Successfully downloaded file {ItemId} (OBO)", itemId);
            return stream;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("File {ItemId} not found in drive {DriveId}", itemId, driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("Access denied downloading file {ItemId}: {Error}", itemId, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error downloading file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to download file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error downloading file: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Get preview URL for a file using app-only authentication.
    /// Returns ephemeral URL that expires in ~10 minutes.
    /// Used for server-side file viewing with correlation ID tracking.
    /// </summary>
    public async Task<FilePreviewDto> GetPreviewUrlAsync(
        string driveId,
        string itemId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetPreviewUrl");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        if (!string.IsNullOrEmpty(correlationId))
        {
            activity?.SetTag("correlationId", correlationId);
        }

        _logger.LogInformation("[{CorrelationId}] Getting preview URL for {DriveId}/{ItemId} (app-only)",
            correlationId ?? "N/A", driveId, itemId);

        try
        {
            var graphClient = _factory.ForApp();

            // Call Graph API preview action with default viewer settings
            var previewRequest = new Microsoft.Graph.Drives.Item.Items.Item.Preview.PreviewPostRequestBody();

            var previewResult = await graphClient.Drives[driveId]
                .Items[itemId]
                .Preview
                .PostAsync(previewRequest, cancellationToken: ct);

            if (previewResult == null || string.IsNullOrEmpty(previewResult.GetUrl))
            {
                _logger.LogWarning("[{CorrelationId}] Preview URL not returned for {DriveId}/{ItemId}",
                    correlationId ?? "N/A", driveId, itemId);
                throw new InvalidOperationException($"Failed to get preview URL for item {itemId}");
            }

            _logger.LogInformation("[{CorrelationId}] Preview URL retrieved for {ItemId}, expires in ~10 minutes",
                correlationId ?? "N/A", itemId);

            return new FilePreviewDto(
                PreviewUrl: previewResult.GetUrl,
                PostUrl: previewResult.PostUrl,
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10), // Preview URLs typically expire in ~10 minutes
                ContentType: null // Will be enriched from Document metadata
            );
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 404)
        {
            _logger.LogWarning("[{CorrelationId}] File not found: {DriveId}/{ItemId}",
                correlationId ?? "N/A", driveId, itemId);
            throw new FileNotFoundException($"File {itemId} not found in drive {driveId}", ex);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == 403)
        {
            _logger.LogWarning("[{CorrelationId}] Access denied to file {ItemId}: {Error}",
                correlationId ?? "N/A", itemId, ex.Message);
            throw new UnauthorizedAccessException($"Access denied to file {itemId}", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Graph API error getting preview URL: {Error}",
                correlationId ?? "N/A", ex.Message);
            throw new InvalidOperationException($"Failed to get preview URL: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{CorrelationId}] Unexpected error getting preview URL for {ItemId}",
                correlationId ?? "N/A", itemId);
            throw;
        }
    }
}
