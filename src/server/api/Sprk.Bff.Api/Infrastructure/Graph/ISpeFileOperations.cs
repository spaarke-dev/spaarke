using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Interface for SPE file operations needed by AI services.
/// Extracted from SpeFileStore to enable unit testing without complex mock setup.
/// </summary>
public interface ISpeFileOperations
{
    /// <summary>
    /// Get file metadata including name and size.
    /// </summary>
    Task<FileHandleDto?> GetFileMetadataAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);

    /// <summary>
    /// Download file content as a stream.
    /// </summary>
    Task<Stream?> DownloadFileAsync(
        string driveId,
        string itemId,
        CancellationToken ct = default);
}
