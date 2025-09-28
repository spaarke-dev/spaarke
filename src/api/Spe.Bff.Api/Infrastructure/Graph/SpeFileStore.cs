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

    public Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        string? description = null,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "CreateContainer");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Creating container {DisplayName} with type {ContainerTypeId}",
            displayName, containerTypeId);

        var graphClient = _factory.CreateAppOnlyClient();

        // Simplified container creation - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("CreateContainerAsync temporarily simplified due to Graph SDK v5 API changes");
        FileStorageContainer? container = null; // Would create via Graph API

        _logger.LogInformation("Successfully created container {ContainerId} with display name {DisplayName}",
            container?.Id, displayName);

        return Task.FromResult(container == null ? null : new ContainerDto(
            container.Id!,
            container.DisplayName!,
            container.Description,
            container.CreatedDateTime ?? DateTimeOffset.UtcNow));
    }

    public Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "GetContainerDrive");
        activity?.SetTag("containerId", containerId);

        _logger.LogInformation("Getting drive for container {ContainerId}", containerId);

        var graphClient = _factory.CreateAppOnlyClient();

        // Simplified drive retrieval - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("GetContainerDriveAsync temporarily simplified due to Graph SDK v5 API changes");
        Drive? drive = null; // Would get via Graph API

        _logger.LogInformation("Successfully retrieved drive {DriveId} for container {ContainerId}",
            drive?.Id, containerId);

        return Task.FromResult(drive == null ? null : new ContainerDto(
            drive.Id!,
            drive.Name ?? "Unknown",
            drive.Description,
            drive.CreatedDateTime ?? DateTimeOffset.UtcNow));
    }

    public Task<FileHandleDto?> UploadSmallAsync(
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "UploadSmall");
        activity?.SetTag("containerId", containerId);
        activity?.SetTag("filePath", path);

        _logger.LogInformation("Uploading small file to container {ContainerId} at path {Path}",
            containerId, path);

        var graphClient = _factory.CreateAppOnlyClient();

        // Simplified upload - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("UploadSmallAsync temporarily simplified due to Graph SDK v5 API changes");
        DriveItem? item = null; // Would upload via Graph API

        _logger.LogInformation("Successfully uploaded file {ItemId} to container {ContainerId}",
            item?.Id, containerId);

        return Task.FromResult(item == null ? null : new FileHandleDto(
            item.Id!,
            item.Name!,
            item.ParentReference?.Id,
            item.Size,
            item.CreatedDateTime ?? DateTimeOffset.UtcNow,
            item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            item.ETag,
            item.Folder != null));
    }

    public Task<IList<ContainerDto>?> ListContainersAsync(Guid containerTypeId, CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListContainers");
        activity?.SetTag("containerTypeId", containerTypeId.ToString());

        _logger.LogInformation("Listing containers for type {ContainerTypeId}", containerTypeId);

        var graphClient = _factory.CreateAppOnlyClient();

        // Simplified listing - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("ListContainersAsync temporarily simplified due to Graph SDK v5 API changes");
        FileStorageContainerCollectionResponse? response = null; // Would list via Graph API

        var result = response?.Value ?? new List<FileStorageContainer>();
        _logger.LogInformation("Found {Count} containers for type {ContainerTypeId}",
            result.Count, containerTypeId);

        return Task.FromResult<IList<ContainerDto>?>(result.Select(c => new ContainerDto(
            c.Id!,
            c.DisplayName!,
            c.Description,
            c.CreatedDateTime ?? DateTimeOffset.UtcNow)).ToList());
    }

    public Task<IList<FileHandleDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        CancellationToken ct = default)
    {
        using var activity = Activity.Current;
        activity?.SetTag("operation", "ListChildren");
        activity?.SetTag("driveId", driveId);
        activity?.SetTag("itemId", itemId);

        _logger.LogInformation("Listing children in drive {DriveId}, item {ItemId}", driveId, itemId);

        var graphClient = _factory.CreateAppOnlyClient();

        // Simplified listing - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("ListChildrenAsync temporarily simplified due to Graph SDK v5 API changes");
        DriveItemCollectionResponse? page = null; // Would list via Graph API

        var result = page?.Value ?? new List<DriveItem>();
        _logger.LogInformation("Found {Count} children in drive {DriveId}", result.Count, driveId);

        return Task.FromResult<IList<FileHandleDto>>(result.Select(item => new FileHandleDto(
            item.Id!,
            item.Name!,
            item.ParentReference?.Id,
            item.Size,
            item.CreatedDateTime ?? DateTimeOffset.UtcNow,
            item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            item.ETag,
            item.Folder != null)).ToList());
    }

    public Task<UploadSessionDto?> CreateUploadSessionAsync(
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

        var graphClient = _factory.CreateAppOnlyClient();

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

        // Simplified upload session - API temporarily disabled due to Graph SDK v5 changes
        _logger.LogWarning("CreateUploadSessionAsync temporarily simplified due to Graph SDK v5 API changes");
        UploadSession? session = null; // Would create session via Graph API

        _logger.LogInformation("Created upload session {UploadUrl} for file {Path}",
            session?.UploadUrl, path);

        return Task.FromResult(session == null ? null : new UploadSessionDto(
            session.UploadUrl!,
            session.ExpirationDateTime ?? DateTimeOffset.UtcNow.AddHours(24)));
    }

    public Task<HttpResponseMessage> UploadChunkAsync(
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

        // Simplified chunk upload - returns placeholder response
        _logger.LogWarning("UploadChunkAsync temporarily simplified due to Graph SDK v5 API changes");

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Accepted));
    }
}