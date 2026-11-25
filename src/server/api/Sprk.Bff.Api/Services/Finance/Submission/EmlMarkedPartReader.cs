using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>How reading a from-email submission's marked parts ended.</summary>
internal enum EmlMarkedPartReadOutcome
{
    /// <summary>Every marked part was read and matched its mark.</summary>
    Read,

    /// <summary>The <c>.eml</c> row is gone or holds no file: its save did not finish. The submission ends Failed
    /// <see cref="FromEmailSubmissionErrors.EmailSaveFailed"/>.</summary>
    EmailSaveFailed,

    /// <summary>A part is missing, or its ordinal, name or SHA-256 differs from the mark, or the file is not a MIME message. The submission
    /// ends Failed <see cref="SubmissionReasonCodes.DocumentUnavailable"/>; the <c>.eml</c> is left as it is.</summary>
    DocumentUnavailable,

    /// <summary>
    /// DEF-052 (only when the caller passed <c>maxPartBytes</c>): a marked part, at its mark's ordinal and name, is over the per-file limit.
    /// Permanent (a retry reads the same file); the route answers it as an over-limit upload (413 <see cref="InvoiceSubmissionErrors.TooLarge"/>).
    /// </summary>
    TooLarge,

    /// <summary>Dataverse or SPE faulted: retry the delivery (nothing was decided).</summary>
    Transient,
}

/// <summary>A marked part's bytes, as read from the saved <c>.eml</c>.</summary>
internal sealed record EmlMarkedPartContent(FromEmailMarkedPart Part, byte[] Content);

/// <summary>The read: the outcome, and on <see cref="EmlMarkedPartReadOutcome.Read"/> the parts and the <c>.eml</c> row's version.</summary>
internal sealed record EmlMarkedPartRead(
    EmlMarkedPartReadOutcome Outcome,
    IReadOnlyList<EmlMarkedPartContent> Parts,
    long? RowVersion)
{
    public static EmlMarkedPartRead Of(EmlMarkedPartReadOutcome outcome) => new(outcome, Array.Empty<EmlMarkedPartContent>(), null);
}

/// <summary>
/// ebilling-r1 task 095 (model §9.1 step 3, uac-r2 F2-c): the submission job's byte read of a from-email source. ONE retrieve of the
/// <c>.eml</c> row gives its drive, item and <c>versionnumber</c> (step 4's <c>IfRowVersionMatches</c> pins that version); the file is read
/// from SPE app-only, never from a mailbox; each marked part must be at its ordinal with its name and SHA-256, or the submission ends
/// Failed. Logs carry ids and ordinals only, never a file name or a subject.
/// </summary>
/// <remarks>Built for task 094, which calls it at staging: it reads each marked part out of the saved <c>.eml</c> and stages the bytes in
/// the Finance file store under the part's Stored File id. Since task 092 the job reads the staged bytes
/// (<c>StoredFileService.OpenStagedForProcessingAsync</c>), not a Document, so this read is the one place the bytes leave the
/// <c>.eml</c>. Where 094 runs it (intake or job) decides whether its outcomes are a refusal or a Failed submission; 095 does not wire it.</remarks>
internal sealed class EmlMarkedPartReader
{
    private const string DocumentEntity = "sprk_document";
    private static readonly string[] RowColumns = ["sprk_graphdriveid", "sprk_graphitemid", "versionnumber"];

    private readonly IGenericEntityService _entities;
    private readonly SpeFileStore _spe;
    private readonly ILogger<EmlMarkedPartReader> _logger;

    public EmlMarkedPartReader(IGenericEntityService entities, SpeFileStore spe, ILogger<EmlMarkedPartReader> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the <paramref name="marks"/> of submission <paramref name="submissionId"/> out of <c>.eml</c> <paramref name="emlDocumentId"/>.
    /// DEF-052: <paramref name="maxPartBytes"/> (<c>null</c>, the default, is no limit) is the per-file limit each part is decoded under (a
    /// part over it is never held); a marked part over it, at its mark's ordinal and name, is <see cref="EmlMarkedPartReadOutcome.TooLarge"/>.
    /// An unmarked part over it is skipped.
    /// </summary>
    public async Task<EmlMarkedPartRead> ReadAsync(
        Guid submissionId, Guid emlDocumentId, IReadOnlyList<FromEmailMarkedPart> marks, CancellationToken ct, long? maxPartBytes = null)
    {
        ArgumentNullException.ThrowIfNull(marks);

        Microsoft.Xrm.Sdk.Entity? row;
        try
        {
            row = await _entities.RetrieveAsync(DocumentEntity, emlDocumentId, RowColumns, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            row = null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Submission {SubmissionId}: .eml {DocumentId} could not be read; the delivery is retried", submissionId, emlDocumentId);
            return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.Transient);
        }

        var driveId = row?.GetAttributeValue<string>("sprk_graphdriveid");
        var itemId = row?.GetAttributeValue<string>("sprk_graphitemid");
        if (row is null || string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
        {
            _logger.LogWarning("Submission {SubmissionId}: .eml {DocumentId} is gone or holds no file; its save did not finish", submissionId, emlDocumentId);
            return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.EmailSaveFailed);
        }

        var version = row.GetAttributeValue<long?>("versionnumber");

        IReadOnlyList<(EmlPart Part, byte[] Content, bool Oversize)> parts;
        try
        {
            await using var stream = await _spe.DownloadFileAsync(driveId, itemId, ct);
            if (stream is null)
            {
                _logger.LogWarning("Submission {SubmissionId}: the file of .eml {DocumentId} is not in storage", submissionId, emlDocumentId);
                return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.EmailSaveFailed);
            }

            parts = EmlMimeParts.ReadWithContent(stream, ct, maxPartBytes);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Submission {SubmissionId}: the file of .eml {DocumentId} is not a MIME message", submissionId, emlDocumentId);
            return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.DocumentUnavailable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Submission {SubmissionId}: .eml {DocumentId} could not be read from storage; the delivery is retried",
                submissionId, emlDocumentId);
            return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.Transient);
        }

        var read = new List<EmlMarkedPartContent>(marks.Count);
        foreach (var mark in marks)
        {
            var found = mark.Ordinal >= 0 && mark.Ordinal < parts.Count ? parts[mark.Ordinal] : default;
            if (found.Oversize && string.Equals(found.Part.FileName, mark.FileName, StringComparison.Ordinal))
            {
                // Its bytes were never held, so its hash is unknown; the name is what ties it to the mark.
                _logger.LogWarning("Submission {SubmissionId}: part {Ordinal} of .eml {DocumentId} is over the per-file size limit",
                    submissionId, mark.Ordinal, emlDocumentId);
                return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.TooLarge);
            }

            if (found.Part is null || found.Oversize
                || !string.Equals(found.Part.FileName, mark.FileName, StringComparison.Ordinal)
                || !string.Equals(found.Part.Sha256, mark.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Submission {SubmissionId}: part {Ordinal} of .eml {DocumentId} does not match its mark ({Reason})",
                    submissionId, mark.Ordinal, emlDocumentId,
                    found.Part is null ? "missing" : found.Part.Sha256 != mark.Sha256 ? "content" : "name");
                return EmlMarkedPartRead.Of(EmlMarkedPartReadOutcome.DocumentUnavailable);
            }

            read.Add(new EmlMarkedPartContent(mark, found.Content));
        }

        return new EmlMarkedPartRead(EmlMarkedPartReadOutcome.Read, read, version);
    }

    private static bool IsNotFound(Exception ex)
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
