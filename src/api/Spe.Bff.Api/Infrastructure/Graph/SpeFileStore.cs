using Microsoft.Graph.Models;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Facade for SharePoint Embedded file operations.
/// Delegates to specialized operation classes for better maintainability.
/// Refactored from 604-line god class to cohesive modules (Task 3.2).
/// </summary>
public class SpeFileStore
{
    private readonly ContainerOperations _containerOps;
    private readonly DriveItemOperations _driveItemOps;
    private readonly UploadSessionManager _uploadManager;

    public SpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadManager)
    {
        _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
        _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
        _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
    }

    // Container Operations - delegate to ContainerOperations
    public Task<ContainerDto?> CreateContainerAsync(
        Guid containerTypeId,
        string displayName,
        string? description = null,
        CancellationToken ct = default)
        => _containerOps.CreateContainerAsync(containerTypeId, displayName, description, ct);

    public Task<ContainerDto?> GetContainerDriveAsync(string containerId, CancellationToken ct = default)
        => _containerOps.GetContainerDriveAsync(containerId, ct);

    public Task<IList<ContainerDto>?> ListContainersAsync(Guid containerTypeId, CancellationToken ct = default)
        => _containerOps.ListContainersAsync(containerTypeId, ct);

    // Upload Operations - delegate to UploadSessionManager
    public Task<FileHandleDto?> UploadSmallAsync(
        string driveId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.UploadSmallAsync(driveId, path, content, ct);

    public Task<UploadSessionDto?> CreateUploadSessionAsync(
        string containerId,
        string path,
        CancellationToken ct = default)
        => _uploadManager.CreateUploadSessionAsync(containerId, path, ct);

    public Task<HttpResponseMessage> UploadChunkAsync(
        UploadSessionDto session,
        Stream file,
        long start,
        long length,
        CancellationToken ct = default)
        => _uploadManager.UploadChunkAsync(session, file, start, length, ct);

    // Drive Item Operations - delegate to DriveItemOperations
    public Task<IList<FileHandleDto>> ListChildrenAsync(
        string driveId,
        string? itemId = null,
        CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsync(driveId, itemId, ct);

    public Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileAsync(driveId, itemId, ct);

    public Task<bool> DeleteFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DeleteFileAsync(driveId, itemId, ct);

    public Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.GetFileMetadataAsync(driveId, itemId, ct);
}
