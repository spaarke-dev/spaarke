using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Interface for SPE file operations needed by AI services.
/// Extracted from SpeFileStore to enable unit testing without complex mock setup.
/// </summary>
public interface ISpeFileOperations
{
    /// <summary>
    /// Get file metadata including name and size (app-only auth).
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Get file metadata using user OBO authentication.
    /// Use this when accessing files uploaded by a user in their context.
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download file content as a stream (app-only auth).
    /// </summary>
    Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download file content using user OBO authentication.
    /// Use this when accessing files uploaded by a user in their context.
    /// </summary>
    Task<Stream?> DownloadFileAsUserAsync(
        HttpContext ctx,
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Resolve a container ID to its drive ID.
    /// Container IDs start with "b!" (base64-encoded SharePoint site ID).
    /// If the input is already a drive ID, returns it unchanged.
    /// </summary>
    /// <param name="containerOrDriveId">Container ID (b!xxx) or drive ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The drive ID for the container.</returns>
    Task<string> ResolveDriveIdAsync(string containerOrDriveId, CancellationToken ct = default);
}
