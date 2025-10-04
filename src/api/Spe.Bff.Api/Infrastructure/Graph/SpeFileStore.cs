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
    private readonly UserOperations _userOps;

    public SpeFileStore(
        ContainerOperations containerOps,
        DriveItemOperations driveItemOps,
        UploadSessionManager uploadManager,
        UserOperations userOps)
    {
        _containerOps = containerOps ?? throw new ArgumentNullException(nameof(containerOps));
        _driveItemOps = driveItemOps ?? throw new ArgumentNullException(nameof(driveItemOps));
        _uploadManager = uploadManager ?? throw new ArgumentNullException(nameof(uploadManager));
        _userOps = userOps ?? throw new ArgumentNullException(nameof(userOps));
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

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
    // All methods delegate to specialized operation classes.
    // These methods accept userToken and use OBO authentication flow.

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

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        string userToken,
        string containerId,
        CancellationToken ct = default)
        => _userOps.GetUserCapabilitiesAsync(userToken, containerId, ct);
}
