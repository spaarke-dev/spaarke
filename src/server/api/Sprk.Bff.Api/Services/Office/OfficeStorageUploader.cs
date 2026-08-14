using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Handles file upload to SharePoint Embedded (SPE) via the SpeFileStore facade.
/// Extracted from OfficeService to enforce single responsibility.
/// </summary>
/// <remarks>
/// Per ADR-007, all SPE operations go through SpeFileStore — no direct Graph SDK usage.
/// This service contains NO direct Microsoft.Graph references (only SpeFileStore).
/// </remarks>
public class OfficeStorageUploader
{
    private readonly SpeFileStore _speFileStore;
    private readonly ILogger<OfficeStorageUploader> _logger;

    public OfficeStorageUploader(
        SpeFileStore speFileStore,
        ILogger<OfficeStorageUploader> logger)
    {
        _speFileStore = speFileStore;
        _logger = logger;
    }

    /// <summary>
    /// Uploads content to SPE and returns the DriveId, ItemId, WebUrl, and any error.
    /// </summary>
    public async Task<(bool Success, string? DriveId, string? ItemId, string? WebUrl, string? Error)> UploadToSpeAsync(
        string containerId,
        string? folderPath,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Uploading to SPE container {ContainerId}, path {FolderPath}/{FileName}",
            containerId,
            folderPath ?? "root",
            fileName);

        try
        {
            // Resolve container to drive ID
            var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, cancellationToken);

            // Build the full path
            var path = string.IsNullOrEmpty(folderPath)
                ? fileName
                : $"{folderPath.TrimEnd('/')}/{fileName}";

            // Upload using SpeFileStore (ADR-007)
            var result = await _speFileStore.UploadSmallAsync(driveId, path, content, cancellationToken);

            if (result != null)
            {
                _logger.LogInformation(
                    "File uploaded to SPE: DriveId={DriveId}, ItemId={ItemId}",
                    driveId,
                    result.Id);

                return (true, driveId, result.Id, result.WebUrl, null);
            }

            return (false, null, null, null, "Upload returned null result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SPE upload failed for {FileName}", fileName);
            return (false, null, null, null, ex.Message);
        }
    }

    /// <summary>
    /// FR-C3 (email-communication-intelligence-r2, R-3): deletes a just-uploaded SPE drive item that turned out
    /// to be a byte-identical content DUPLICATE — the office save path suppressed the second document AND skips
    /// finalization, so this transient blob is now truly unreferenced (gate-after-write cleanup). Best-effort /
    /// non-fatal: a failed cleanup logs and returns false; it NEVER fails the save (the dedup already succeeded).
    /// Only ever called with the drive item THIS request just uploaded — never the canonical's own item.
    /// </summary>
    public async Task<bool> DeleteFromSpeAsync(string driveId, string itemId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _speFileStore.DeleteFileAsync(driveId, itemId, cancellationToken);
            _logger.LogInformation(
                "Deleted transient duplicate SPE blob DriveId={DriveId}, ItemId={ItemId} (deleted={Deleted})",
                driveId, itemId, deleted);
            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Transient duplicate SPE blob cleanup failed (non-fatal) for DriveId={DriveId}, ItemId={ItemId}",
                driveId, itemId);
            return false;
        }
    }
}
