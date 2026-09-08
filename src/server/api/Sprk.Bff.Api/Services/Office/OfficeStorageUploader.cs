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
    /// <remarks>
    /// Uploads FLAT into the container root. The dormant <c>folderPath</c> parameter was deleted along
    /// with <c>SaveRequest.FolderPath</c>: it was client-supplied, no client ever sent it (zero hits for
    /// <c>folderPath</c> under <c>src/client/**</c>), and in SPE any folder segment in an upload path is
    /// created implicitly by Graph — so the only thing the plumbing could do was mint folders nobody
    /// asked for. Reinstating a caller-chosen folder would also reinstate that side effect.
    /// </remarks>
    public async Task<(bool Success, string? DriveId, string? ItemId, string? WebUrl, string? Error)> UploadToSpeAsync(
        string containerId,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Uploading to SPE container {ContainerId}, file {FileName} (flat container root)",
            containerId,
            fileName);

        try
        {
            // Resolve container to drive ID
            var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, cancellationToken);

            // Upload using SpeFileStore (ADR-007) — the file name IS the path; no folder segments.
            //
            // SANITIZED 2026-08-29 at THIS layer as well as at the caller. OfficeService already sanitizes
            // all three of its branches, but the ROOT CAUSE of the mystery folders was precisely one of
            // those branches forgetting to: the email branch sanitized and the document branch did not, for
            // as long as the feature existed. This uploader is where the value stops being "a name" and
            // becomes "a path", so it is the last place that can still be honest about it. The double call
            // is idempotent (sanitizing a sanitized name is a no-op).
            var uploadPath = SpeUploadPath.SanitizeFileName(fileName);
            var result = await _speFileStore.UploadSmallAsync(driveId, uploadPath, content, cancellationToken);

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
