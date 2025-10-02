using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;
using System.Diagnostics;

namespace Spe.Bff.Api.Infrastructure.Graph;

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
            var graphClient = _factory.CreateAppOnlyClient();

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
                item.Folder != null)).ToList();
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
            var graphClient = _factory.CreateAppOnlyClient();

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
            var graphClient = _factory.CreateAppOnlyClient();

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
            var graphClient = _factory.CreateAppOnlyClient();

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
                item.Folder != null);
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
}
