using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;
using System.Diagnostics;

namespace Spe.Bff.Api.Infrastructure.Graph;

public class SpeFileStore
{
    private readonly IGraphClientFactory _factory;
    private readonly ILogger<SpeFileStore> _logger;

    public SpeFileStore(IGraphClientFactory factory, ILogger<SpeFileStore> logger)
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
        using var activity = Activity.Current;
        activity?.SetTag("operation", "CreateContainer");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

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

            _logger.LogInformation("Successfully created SPE container {ContainerId} with display name {DisplayName}",
                createdContainer.Id, displayName);

            return new ContainerDto(
                createdContainer.Id!,
                createdContainer.DisplayName!,
                createdContainer.Description,
                createdContainer.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error creating container: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to create SharePoint Embedded container: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating container: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetContainerDrive");
        activity?.SetTag("containerId", containerId);

        _logger.LogInformation("Getting drive for container {ContainerId}", containerId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive == null)
            {
                _logger.LogWarning("Drive not found for container {ContainerId}", containerId);
                return null;
            }

            _logger.LogInformation("Successfully retrieved drive {DriveId} for container {ContainerId}",
                drive.Id, containerId);

            return new ContainerDto(
                drive.Id!,
                drive.Name ?? "Unknown",
                drive.Description,
                drive.CreatedDateTime ?? DateTimeOffset.UtcNow);
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container {ContainerId} not found", containerId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error getting container drive: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to get drive for container: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting container drive: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "UploadSmall");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Uploading small file to drive {DriveId} at path {Path}",
            driveId, path);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            // Upload the file using PUT to drive item content endpoint
            var item = await graphClient.Drives[driveId].Root
                .ItemWithPath(path)
                .Content
                .PutAsync(content, cancellationToken: ct);

            if (item == null)
            {
                _logger.LogError("Failed to upload file - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Successfully uploaded file {ItemId} to drive {DriveId}",
                item.Id, driveId);

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
            _logger.LogWarning("Drive {DriveId} not found", driveId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error uploading file: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to upload file: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error uploading file: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<IList<ContainerDto>?> ListContainersAsync(Guid containerTypeId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainers");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            // Get containers filtered by containerTypeId
            var response = await graphClient.Storage.FileStorage.Containers
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Filter = $"containerTypeId eq {containerTypeId}";
                }, cancellationToken: ct);

            if (response?.Value == null)
            {
                _logger.LogWarning("No containers found for type {ContainerTypeId}", containerTypeId);
                return new List<ContainerDto>();
            }

            var result = response.Value;
            _logger.LogInformation("Found {Count} containers for type {ContainerTypeId}",
                result.Count, containerTypeId);

            return result.Select(c => new ContainerDto(
                c.Id!,
                c.DisplayName!,
                c.Description,
                c.CreatedDateTime ?? DateTimeOffset.UtcNow)).ToList();
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error listing containers: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to list containers: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing containers: {Error}", ex.Message);
            throw;
        }
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

    public async Task<UploadSessionDto?> CreateUploadSessionAsync(
        string containerId,
        string path,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "CreateUploadSession");
        activity?.SetTag("containerId", containerId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Creating upload session for container {ContainerId} at path {Path}",
            containerId, path);

        try
        {
            var graphClient = _factory.CreateAppOnlyClient();

            // First, get the drive for this container
            var drive = await graphClient.Storage.FileStorage.Containers[containerId].Drive
                .GetAsync(cancellationToken: ct);

            if (drive?.Id == null)
            {
                _logger.LogError("Failed to get drive for container {ContainerId}", containerId);
                return null;
            }

            var createUploadSessionPostRequestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
            {
                Item = new DriveItemUploadableProperties
                {
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "@microsoft.graph.conflictBehavior", "rename" }
                    }
                }
            };

            var session = await graphClient.Drives[drive.Id].Root
                .ItemWithPath(path)
                .CreateUploadSession
                .PostAsync(createUploadSessionPostRequestBody, cancellationToken: ct);

            if (session == null)
            {
                _logger.LogError("Failed to create upload session - Graph API returned null");
                return null;
            }

            _logger.LogInformation("Created upload session {UploadUrl} for file {Path}",
                session.UploadUrl, path);

            return new UploadSessionDto(
                session.UploadUrl!,
                session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(24));
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Container {ContainerId} not found", containerId);
            return null;
        }
        catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Graph API throttling encountered, retry with backoff: {Error}", ex.Message);
            throw new InvalidOperationException("Service temporarily unavailable due to rate limiting", ex);
        }
        catch (ServiceException ex)
        {
            _logger.LogError(ex, "Graph API error creating upload session: {Error}", ex.Message);
            throw new InvalidOperationException($"Failed to create upload session: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating upload session: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<HttpResponseMessage> UploadChunkAsync(
        UploadSessionDto session,
        Stream file,
        long start,
        long length,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "UploadChunk");
        activity?.SetTag("start", start);
        activity?.SetTag("length", length);

        _logger.LogInformation("Uploading chunk from {Start} to {End}", start, start + length - 1);

        try
        {
            using var httpClient = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Put, session.UploadUrl);

            // Read chunk data
            var buffer = new byte[length];
            var bytesRead = await file.ReadAsync(buffer, 0, (int)length, ct);

            if (bytesRead != length)
            {
                _logger.LogWarning("Read {BytesRead} bytes but expected {Length}", bytesRead, length);
            }

            request.Content = new ByteArrayContent(buffer, 0, bytesRead);
            request.Content.Headers.ContentLength = bytesRead;
            request.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, start + bytesRead - 1);

            var response = await httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogWarning("Chunk upload returned status {StatusCode}", response.StatusCode);
            }
            else
            {
                _logger.LogInformation("Successfully uploaded chunk from {Start} to {End}", start, start + bytesRead - 1);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading chunk: {Error}", ex.Message);
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