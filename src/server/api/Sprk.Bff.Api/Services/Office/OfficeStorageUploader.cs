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
    /// Called only after <see cref="OfficeDocumentPersistence.IsUploadUnreferencedAsync"/> has proven that no
    /// <c>sprk_document</c> points at the item (task 046): under the path-keyed <c>Replace</c> upload, the item THIS
    /// request uploaded can be an existing document's own file, and this method cannot tell the difference.
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

    /// <summary>
    /// Outcome of <see cref="WriteNewVersionAsync"/>. <see cref="ErrorCode"/> is an Office error code
    /// (<c>OFFICE_0xx</c>) chosen HERE, where the typed SPE exception is still in hand, so no caller has to
    /// re-derive what went wrong from a message string.
    /// </summary>
    public sealed record VersionWriteResult(
        bool Success,
        string? ItemId,
        string? ItemName,
        string? WebUrl,
        string? ErrorCode,
        string? Error);

    /// <summary>
    /// FR-11 version save (spaarkeai-word-add-in-r1 task 023): writes <paramref name="content"/> as a NEW
    /// VERSION of the EXISTING drive item <paramref name="itemId"/>. The item id is unchanged and the previous
    /// content stays in the item's version history.
    /// </summary>
    /// <remarks>
    /// <para><b>Why item-keyed, and why this call.</b> <see cref="UploadToSpeAsync"/> is PATH-keyed: if the
    /// document had been renamed, or the user typed a different name, a path PUT would mint a SECOND item —
    /// the rename-approximation of versioning that task 023 forbids. <see cref="SpeFileStore.ReplaceFileContentAsUserAsync(HttpContext, string, string, Stream, CancellationToken)"/>
    /// PUTs to <c>/drives/{driveId}/items/{itemId}/content</c>, which SharePoint commits as a new version of
    /// that same item. It is the call Compose's save-back already uses for "update in place, never mint a
    /// duplicate". ADR-007: it is the facade's; no Graph type crosses into this class.</para>
    /// <para><b>Why the caller's identity (OBO).</b> The add-in user is, by construction, editing this
    /// item — Word reached it in SPE as that user — so the delegated identity holds write on it, and SPE then
    /// enforces that write itself, beneath the Dataverse <c>write</c> gate on the route. An app-only write
    /// would bypass SPE's ACL and rest the whole decision on the filter.</para>
    /// <para>Never throws for an SPE refusal: each typed refusal becomes a <see cref="VersionWriteResult"/> with
    /// a distinct code. A cancellation still propagates.</para>
    /// </remarks>
    public async Task<VersionWriteResult> WriteNewVersionAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        Stream content,
        CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _speFileStore.ReplaceFileContentAsUserAsync(
                httpContext, driveId, itemId, content, cancellationToken);

            if (saved is null)
            {
                // The facade maps a Graph 404 to null: the row's pointers name an item SPE no longer has.
                // Never recover by uploading a fresh item — that is a second document, not a version.
                _logger.LogWarning(
                    "Version write refused: drive item {ItemId} on drive {DriveId} was not found in SPE.",
                    itemId, driveId);
                return new VersionWriteResult(false, null, null, null, "OFFICE_017",
                    "The document's file was not found in storage.");
            }

            if (!string.Equals(saved.Id, itemId, StringComparison.Ordinal))
            {
                // A PUT to /items/{id}/content cannot answer with a different item. If it ever does, the
                // one-item invariant is already broken somewhere below the facade: report it, never paper over it.
                _logger.LogError(
                    "Version write returned drive item {ReturnedItemId}, not the targeted {ItemId} (drive {DriveId}).",
                    saved.Id, itemId, driveId);
                return new VersionWriteResult(false, saved.Id, saved.Name, saved.WebUrl, "OFFICE_012",
                    "Storage answered the version write with a different item.");
            }

            _logger.LogInformation(
                "New SPE version written: DriveId={DriveId}, ItemId={ItemId}, Size={Size}",
                driveId, itemId, saved.Size);

            return new VersionWriteResult(true, saved.Id, saved.Name, saved.WebUrl, null, null);
        }
        catch (DocumentLockedByWordException ex)
        {
            _logger.LogWarning(ex,
                "Version write refused: drive item {ItemId} on drive {DriveId} is locked.", itemId, driveId);
            return new VersionWriteResult(false, null, null, null, "OFFICE_019",
                "The document is locked for editing, so a new version could not be written.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "Version write refused: the caller may not write drive item {ItemId} on drive {DriveId}.",
                itemId, driveId);
            return new VersionWriteResult(false, null, null, null, "OFFICE_009",
                "You do not have permission to write this document's file.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Version write failed for drive item {ItemId} on drive {DriveId}.", itemId, driveId);
            return new VersionWriteResult(false, null, null, null, "OFFICE_012", ex.Message);
        }
    }

    /// <summary>
    /// Task 047 (spaarkeai-word-add-in-r1): does drive item <paramref name="itemId"/> CURRENTLY hold exactly
    /// <paramref name="expected"/>? <c>true</c> or <c>false</c> when its content was read and compared; <c>null</c>
    /// when it could not be read (item missing, Graph failure). An unknown is never "the same".
    /// </summary>
    /// <remarks>
    /// <para>Reads the item's current content through the facade (ADR-007; app-only, the identity the version path's
    /// content-identity read already uses) and compares it byte for byte. It stops at the first difference and never
    /// reads more than <c>expected.Length + 1</c> bytes. The content is compared, never returned.</para>
    /// <para>Why bytes, not SPE's <c>quickXorHash</c>: comparing hashes would need the request's bytes hashed with
    /// QuickXorHash here, and a local reimplementation that disagreed with SPE would fail silently. A byte compare
    /// cannot disagree with what SPE holds.</para>
    /// </remarks>
    public async Task<bool?> ItemHoldsContentAsync(
        string driveId,
        string itemId,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var current = await _speFileStore.DownloadFileAsync(driveId, itemId, cancellationToken);
            if (current is null)
            {
                return null;
            }

            return await ContentEqualsAsync(current, expected, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not read drive item {ItemId} on drive {DriveId} to compare its current content.", itemId, driveId);
            return null;
        }
    }

    private static async Task<bool> ContentEqualsAsync(Stream stream, ReadOnlyMemory<byte> expected, CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Min(81920, expected.Length + 1)];
        var offset = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return offset == expected.Length;
            }

            if (offset + read > expected.Length
                || !buffer.AsSpan(0, read).SequenceEqual(expected.Span.Slice(offset, read)))
            {
                return false;
            }

            offset += read;
        }
    }
}
