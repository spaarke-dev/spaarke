// ebilling-r1 task 118b-1: the ONE read of a Submit Invoice source document's bytes (procedure-and-file-model 9.1 step 2), moved out of
// InvoiceSubmissionProcessingJobHandler.LoadSourceDocumentAsync so the job (the invoice copy) and the from-document intake (the supporting
// copies, 118b-2) read a document through the same rule.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing - InvoiceSubmissionProcessingJobHandler.LoadSourceDocumentAsync read the row, refused an email archive or a row with no
//       file pointer, verified the pointer's container and downloaded the bytes, inline. EmlMarkedPartReader reads a .eml (no container
//       check, MIME parts), which is not a document's file.
//   (2) Extension - it IS that code, moved (a static method, no DI registration, no new service): the job keeps its staging, its logs and
//       its outcome mapping; only the read moved. Extending EmlMarkedPartReader would mix the .eml's MIME rule into a document read.
//   (3) Cost of doing nothing - the intake would need a second copy of the container-verified read to stage supporting documents (118b
//       escalation, option C rejected), and the two copies could drift on the one check that keeps the app from copying a file the row
//       does not own.

using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>How the read of a source document's file ended (<see cref="SourceDocumentFileRead.ReadAsync"/>).</summary>
internal enum SourceDocumentFileOutcome
{
    /// <summary>The bytes were read; <see cref="SourceDocumentFile.Content"/> and <see cref="SourceDocumentFile.Version"/> are set.</summary>
    Read,

    /// <summary>The <c>sprk_document</c> row no longer exists.</summary>
    Gone,

    /// <summary>The row has no file pointer (drive or item), or it is an email archive (<c>sprk_isemailarchive</c>): not a Submit Invoice source.</summary>
    NoFile,

    /// <summary>The pointer's container could not be verified as the row's own (<see cref="RecordContainerResolver.IsDocumentPointerContainerAllowedAsync(Guid, string?, string?, CancellationToken)"/>); nothing was downloaded.</summary>
    StorageUnverified,

    /// <summary>SharePoint Embedded returned no stream (it may answer later).</summary>
    NotDownloaded,

    /// <summary>
    /// Only when the caller passed <c>maxBytes</c> (task 118b review, F1-b): the row's <c>sprk_filesize</c> is over it (nothing was
    /// downloaded), or the stream delivered more than it (the copy stopped there). The job passes none and never sees this.
    /// </summary>
    TooLarge,
}

/// <summary>
/// The answer of <see cref="SourceDocumentFileRead.ReadAsync"/>. On <see cref="SourceDocumentFileOutcome.Read"/>: the bytes, the row's
/// <c>versionnumber</c> read in the same retrieve, its <c>sprk_mimetype</c> as stored (null when empty) and its communication
/// (<c>sprk_relatedcommunication</c>, null when none). On any other outcome only <see cref="Outcome"/> is meaningful.
/// <para>Task 118b-2: <see cref="FileName"/>, the row's <c>sprk_filename</c> (null when empty), is read only for its extension (a supporting
/// copy's name is the tracking number and its part number, never the source's name: uac K1).</para>
/// </summary>
internal sealed record SourceDocumentFile(
    SourceDocumentFileOutcome Outcome,
    byte[]? Content = null,
    long Version = 0,
    string? MimeType = null,
    Guid? CommunicationId = null,
    string? FileName = null);

/// <summary>
/// ebilling-r1 tasks 094 / 118b-1 (procedure-and-file-model 9.1 step 2): a source document's file, read as the application. In order, as
/// the job has read it since 094: ONE retrieve of the row (<see cref="Columns"/>: the file pointer, the version, the type, the
/// communication, the email-archive flag); a row with no pointer, or an email archive, is <see cref="SourceDocumentFileOutcome.NoFile"/>;
/// the version (<see cref="InvoiceSubmissionPersister.VersionOf"/>, which throws when the row carries none); the pointer's container
/// verified FIRST (the download follows the pointer as the application; fail closed); then the bytes.
/// </summary>
/// <remarks>A fault of the retrieve (other than not-found), of the container check or of the download propagates: the caller decides
/// whether it is transient. Nothing is logged here; the callers log with their own ids (ADR-015: ids and codes only).</remarks>
internal static class SourceDocumentFileRead
{
    /// <summary>The source document as step 2 reads it: the file pointer, the version and the type, in ONE retrieve.</summary>
    internal static readonly string[] Columns =
    [
        "sprk_graphdriveid", "sprk_graphitemid", "sprk_mimetype", InvoiceReviewService.VersionNumber,
        "sprk_relatedcommunication", "sprk_isemailarchive",
        // Task 118b-2: the extension of a supporting copy (the name itself is never copied).
        "sprk_filename",
        // Task 118b review (F1-b): the size the row records (Whole Number, may be empty), judged before a download when a limit is given.
        FileSizeColumn,
    ];

    /// <summary>The row's recorded file size in bytes (<c>sprk_filesize</c>, a Whole Number; empty on rows written before it was kept).</summary>
    private const string FileSizeColumn = "sprk_filesize";

    /// <summary>
    /// Reads the file of <paramref name="documentId"/> (see the type's summary for the order). <paramref name="maxBytes"/> (task 118b review,
    /// F1-b; <c>null</c>, the default, is no limit and exactly the read the job has always made): when set, a row whose recorded
    /// <c>sprk_filesize</c> is over it is <see cref="SourceDocumentFileOutcome.TooLarge"/> before anything is downloaded, and a download
    /// that delivers more than it is <see cref="SourceDocumentFileOutcome.TooLarge"/> too (the copy stops as soon as the limit is passed, so
    /// a row with no recorded size, or a wrong one, cannot fill memory). A file of exactly <paramref name="maxBytes"/> is read.
    /// </summary>
    internal static async Task<SourceDocumentFile> ReadAsync(
        IGenericEntityService entities,
        ISpeFileOperations spe,
        RecordContainerResolver containerResolver,
        Guid documentId,
        CancellationToken ct,
        long? maxBytes = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(spe);
        ArgumentNullException.ThrowIfNull(containerResolver);

        Entity document;
        try
        {
            document = await entities.RetrieveAsync(InvoiceSubmissionPersister.DocumentEntity, documentId, Columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            return new SourceDocumentFile(SourceDocumentFileOutcome.Gone);
        }

        var driveId = document.GetAttributeValue<string>("sprk_graphdriveid");
        var itemId = document.GetAttributeValue<string>("sprk_graphitemid");
        if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId) || document.GetAttributeValue<bool?>("sprk_isemailarchive") == true)
        {
            return new SourceDocumentFile(SourceDocumentFileOutcome.NoFile);
        }

        var version = InvoiceSubmissionPersister.VersionOf(document);
        if (!await containerResolver.IsDocumentPointerContainerAllowedAsync(documentId, driveId, itemId, ct).ConfigureAwait(false))
        {
            return new SourceDocumentFile(SourceDocumentFileOutcome.StorageUnverified);
        }

        // The size the row records is judged after the container check (a pointer that is not the row's own is the stronger refusal) and
        // before the download. Empty or not positive is "not recorded": the stream is then the judge.
        if (maxBytes is { } recordedLimit && RecordedSizeOf(document) is { } recorded && recorded > recordedLimit)
        {
            return new SourceDocumentFile(SourceDocumentFileOutcome.TooLarge);
        }

        var stream = await spe.DownloadFileAsync(driveId, itemId, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return new SourceDocumentFile(SourceDocumentFileOutcome.NotDownloaded);
        }

        byte[] bytes;
        using (stream)
        {
            using var buffer = new MemoryStream();
            if (maxBytes is { } streamLimit)
            {
                if (!await CopyUpToAsync(stream, buffer, streamLimit, ct).ConfigureAwait(false))
                {
                    return new SourceDocumentFile(SourceDocumentFileOutcome.TooLarge);
                }
            }
            else
            {
                await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            }

            bytes = buffer.ToArray();
        }

        var mimeType = document.GetAttributeValue<string>("sprk_mimetype");
        var fileName = document.GetAttributeValue<string>("sprk_filename");
        return new SourceDocumentFile(
            SourceDocumentFileOutcome.Read,
            bytes,
            version,
            string.IsNullOrWhiteSpace(mimeType) ? null : mimeType,
            document.GetAttributeValue<EntityReference>("sprk_relatedcommunication")?.Id is { } communication && communication != Guid.Empty
                ? communication
                : null,
            string.IsNullOrWhiteSpace(fileName) ? null : fileName);
    }

    /// <summary>The <c>sprk_filesize</c> the row records, whatever integer width the retrieve returned it in; null when empty, not a number or not positive.</summary>
    private static long? RecordedSizeOf(Entity document) => document.Contains(FileSizeColumn)
        ? document[FileSizeColumn] switch
        {
            int whole when whole > 0 => whole,
            long wide when wide > 0 => wide,
            _ => null,
        }
        : null;

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/> and stops at the first byte past <paramref name="limit"/>: false
    /// then (the destination holds at most <paramref name="limit"/> bytes), true when the whole stream, up to and including exactly
    /// <paramref name="limit"/> bytes, was copied.
    /// </summary>
    private static async Task<bool> CopyUpToAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false)) > 0)
            {
                copied += read;
                if (copied > limit)
                {
                    return false;
                }

                await destination.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            return true;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private static bool IsRecordNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }
}
