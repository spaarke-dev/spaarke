using Microsoft.Graph.Models;

namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Service interface for SharePoint Embedded operations via Microsoft Graph.
/// Abstracts Graph SDK calls and provides business logic for SPE containers and files.
/// </summary>
public interface ISpeService
{
    /// <summary>
    /// Creates a new SPE container using app-only (MI) authentication.
    /// </summary>
    Task<FileStorageContainer?> CreateContainerAsync(Guid containerTypeId, string name, string? description = null);

    /// <summary>
    /// Gets the drive associated with an SPE container using app-only (MI) authentication.
    /// </summary>
    Task<Drive?> GetContainerDriveAsync(string containerId);

    /// <summary>
    /// Uploads a small file to a container using app-only (MI) authentication.
    /// For large files, use upload session methods.
    /// </summary>
    Task<DriveItem?> UploadSmallAsync(string containerId, string path, Stream content, CancellationToken ct = default);

    /// <summary>
    /// Lists containers by container type ID using app-only (MI) authentication.
    /// </summary>
    Task<IList<FileStorageContainer>?> ListContainersAsync(Guid containerTypeId);

    /// <summary>
    /// Lists children (files/folders) in a drive using app-only (MI) authentication.
    /// Operates on container drives, not user drives.
    /// </summary>
    Task<IList<DriveItem>> ListChildrenAsync(string driveId, string? itemId = null, CancellationToken ct = default);

    /// <summary>
    /// Creates an upload session for large file uploads using app-only (MI) authentication.
    /// </summary>
    Task<UploadSession?> CreateUploadSessionAsync(string containerId, string path, CancellationToken ct = default);

    /// <summary>
    /// Uploads a chunk to an upload session using app-only (MI) authentication.
    /// </summary>
    Task<HttpResponseMessage> UploadChunkAsync(UploadSession session, Stream file, long start, long length, CancellationToken ct = default);
}