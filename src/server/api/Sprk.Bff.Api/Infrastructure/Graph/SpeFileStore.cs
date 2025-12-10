using Microsoft.Graph.Models;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Facade for SharePoint Embedded file operations.
/// Delegates to specialized operation classes for better maintainability.
/// Refactored from 604-line god class to cohesive modules (Task 3.2).
/// </summary>
public class SpeFileStore : ISpeFileOperations
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

    public Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.GetFileMetadataAsUserAsync(ctx, driveId, itemId, ct);

    public Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileAsUserAsync(ctx, driveId, itemId, ct);

    public Task<FilePreviewDto> GetPreviewUrlAsync(
        string driveId,
        string itemId,
        string? correlationId = null,
        CancellationToken ct = default)
        => _driveItemOps.GetPreviewUrlAsync(driveId, itemId, correlationId, ct);

    /// <summary>
    /// Resolve a container ID to its drive ID.
    /// Drive IDs start with "b!" (base64-encoded SharePoint site reference).
    /// Container IDs are GUIDs like "a1234567-89ab-cdef-0123-456789abcdef".
    /// If the input is already a drive ID, returns it unchanged.
    /// </summary>
    public async Task<string> ResolveDriveIdAsync(string containerOrDriveId, CancellationToken ct = default)
    {
        // Drive IDs from SharePoint typically start with "b!" (base64-encoded site reference)
        // If it already starts with "b!", it's a drive ID - return as-is
        if (containerOrDriveId.StartsWith("b!", StringComparison.OrdinalIgnoreCase))
        {
            return containerOrDriveId;
        }

        // Otherwise, it might be a container ID (GUID format) - try to resolve it
        var containerDrive = await _containerOps.GetContainerDriveAsync(containerOrDriveId, ct);
        if (containerDrive == null)
        {
            throw new InvalidOperationException($"Could not resolve container {containerOrDriveId} to drive ID");
        }

        return containerDrive.Id;
    }

    // =============================================================================
    // USER CONTEXT METHODS (OBO Flow)
    // =============================================================================
    // All methods delegate to specialized operation classes.
    // These methods accept userToken and use OBO authentication flow.

    // Container Operations (user context)
    public Task<IList<ContainerDto>> ListContainersAsUserAsync(
        HttpContext ctx,
        Guid containerTypeId,
        CancellationToken ct = default)
        => _containerOps.ListContainersAsUserAsync(ctx, containerTypeId, ct);

    // Drive Item Operations (user context)
    public Task<ListingResponse> ListChildrenAsUserAsync(
        HttpContext ctx,
        string containerId,
        ListingParameters parameters,
        CancellationToken ct = default)
        => _driveItemOps.ListChildrenAsUserAsync(ctx, containerId, parameters, ct);

    public Task<FileContentResponse?> DownloadFileWithRangeAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        RangeHeader? range,
        string? ifNoneMatch,
        CancellationToken ct = default)
        => _driveItemOps.DownloadFileWithRangeAsUserAsync(ctx, driveId, itemId, range, ifNoneMatch, ct);

    public Task<DriveItemDto?> UpdateItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        UpdateFileRequest request,
        CancellationToken ct = default)
        => _driveItemOps.UpdateItemAsUserAsync(ctx, driveId, itemId, request, ct);

    public Task<bool> DeleteItemAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default)
        => _driveItemOps.DeleteItemAsUserAsync(ctx, driveId, itemId, ct);

    // Upload Operations (user context)
    public Task<FileHandleDto?> UploadSmallAsUserAsync(
        HttpContext ctx,
        string containerId,
        string path,
        Stream content,
        CancellationToken ct = default)
        => _uploadManager.UploadSmallAsUserAsync(ctx, containerId, path, content, ct);

    public Task<UploadSessionResponse?> CreateUploadSessionAsUserAsync(
        HttpContext ctx,
        string driveId,
        string path,
        ConflictBehavior conflictBehavior,
        CancellationToken ct = default)
        => _uploadManager.CreateUploadSessionAsUserAsync(ctx, driveId, path, conflictBehavior, ct);

    public Task<ChunkUploadResponse> UploadChunkAsUserAsync(
        string userToken,
        string uploadSessionUrl,
        string contentRange,
        byte[] chunkData,
        CancellationToken ct = default)
        => _uploadManager.UploadChunkAsUserAsync(userToken, uploadSessionUrl, contentRange, chunkData, ct);

    // User Operations
    public Task<UserInfoResponse?> GetUserInfoAsync(
        HttpContext ctx,
        CancellationToken ct = default)
        => _userOps.GetUserInfoAsync(ctx, ct);

    public Task<UserCapabilitiesResponse> GetUserCapabilitiesAsync(
        HttpContext ctx,
        string containerId,
        CancellationToken ct = default)
        => _userOps.GetUserCapabilitiesAsync(ctx, containerId, ct);
}
