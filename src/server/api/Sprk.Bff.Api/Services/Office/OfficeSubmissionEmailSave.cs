using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// ebilling-r1 task 095 (model §9.1 step 1): the from-email submission's in-process run of the <c>/office/save</c> pipeline. The
/// SAME <see cref="OfficeService.SaveAsync"/> code (Graph enrich, the caller's own duplicate, owner, capture and filing, job row,
/// upload, document create, finalization), with three differences the submission needs: the <c>.eml</c> is created with its own
/// non-provisional Restricted and the submission marker in the same create (<see cref="OfficeDocumentPersistence.ApplyEmailCommunicationOutcome"/> with <c>submissionNarrowing</c>);
/// an email whose communication cannot be identified is refused before the job row and the upload (never task 130's save-Restricted
/// fallback, model §9.1 F1-c); and the outcome carries the created <c>.eml</c>, its communication and its MIME parts, so the
/// caller resolves the <c>.eml</c> from this request rather than from a job result (§9.1 step 1a).
/// </summary>
/// <remarks>Internal and implemented by <see cref="OfficeService"/> only (registered by a cast of the scoped <see cref="IOfficeService"/>,
/// so it is the same instance); the from-email submission service is its only caller. It is not an HTTP surface.</remarks>
internal interface IOfficeSubmissionEmailSave
{
    /// <summary>
    /// Saves <paramref name="request"/> (an Email save; the caller has set <c>SelectedAttachmentFileNames = []</c> and
    /// <c>IdempotencyKey = null</c>) for a from-email submission. Never throws for a refusal: <see cref="SubmissionEmailSaveOutcome.Response"/>
    /// carries the Office refusal, or <see cref="SubmissionEmailSaveOutcome.Refusal"/> the submission-specific one.
    /// </summary>
    /// <param name="markedSha256">The SHA-256 (hex) of every Invoice and Supporting part, one entry per marked part. The built
    /// <c>.eml</c> must carry each one byte-for-byte; otherwise the save is refused before the upload
    /// (<see cref="SubmissionEmailSaveRefusal.AttachmentNotPreserved"/>, 095 review F1).</param>
    Task<SubmissionEmailSaveOutcome> SaveEmailForSubmissionAsync(
        SaveRequest request, IReadOnlyList<string> markedSha256, string userId, HttpContext httpContext, CancellationToken cancellationToken);
}

/// <summary>Why the submission's save refused the email before writing anything (model §9.1 F1-c).</summary>
internal enum SubmissionEmailSaveRefusal
{
    /// <summary>No communication resolves for the email after the capture and the Message-ID lookup: permanent.</summary>
    NoCommunication,

    /// <summary>The Message-ID lookup faulted: a transient Dataverse error, retryable.</summary>
    CommunicationUnavailable,

    /// <summary>
    /// The <c>.eml</c> as built does not carry every marked part byte-for-byte (095 review F1: a part MIME rewrites, or one the reader
    /// does not see as an attachment). Refused after the build and before the upload and the document create: permanent.
    /// </summary>
    AttachmentNotPreserved,
}

/// <summary>One attachment part of a built or saved <c>.eml</c>: its 0-based ordinal among the message's attachments, its file name and
/// the SHA-256 (lower-case hex) of its decoded bytes. No content and no subject.</summary>
internal sealed record EmlPart(int Ordinal, string? FileName, string Sha256);

/// <summary>
/// What the submission save did. Exactly one of: a refusal (<see cref="Refusal"/> set, nothing written), an Office answer that is not a
/// success (<see cref="SaveResponse.Success"/> false, passed through), the caller's own earlier save (<see cref="SaveResponse.Duplicate"/>,
/// nothing written: the caller resolves the earlier <c>.eml</c> from the communication), a content duplicate
/// (<see cref="ContentDuplicate"/>: no document created), or a fresh <c>.eml</c> (<see cref="EmlDocumentId"/> with its
/// <see cref="CommunicationId"/> and <see cref="EmlParts"/>).
/// </summary>
internal sealed record SubmissionEmailSaveOutcome
{
    public SaveResponse? Response { get; init; }

    public SubmissionEmailSaveRefusal? Refusal { get; init; }

    /// <summary>The <c>.eml</c> this request created (own Restricted, marked, linked to <see cref="CommunicationId"/>).</summary>
    public Guid? EmlDocumentId { get; init; }

    /// <summary>The email's communication (the capture's, else the Message-ID lookup's). Set whenever the save got past the refusal.</summary>
    public Guid? CommunicationId { get; init; }

    /// <summary>The content-dedup suppress resolved to an existing canonical document instead of creating one.</summary>
    public bool ContentDuplicate { get; init; }

    /// <summary>The attachment parts of the <c>.eml</c> as built and uploaded.</summary>
    public IReadOnlyList<EmlPart>? EmlParts { get; init; }
}

/// <summary>The per-request state <see cref="OfficeService"/>'s save threads through for a submission save (null for every other save).</summary>
internal sealed class SubmissionSaveState
{
    public SubmissionEmailSaveRefusal? Refusal { get; set; }

    public Guid? CommunicationId { get; set; }

    public Guid? EmlDocumentId { get; set; }

    public bool ContentDuplicate { get; set; }

    public IReadOnlyList<EmlPart>? EmlParts { get; set; }

    /// <summary>The marked parts' SHA-256, one per marked part, that the built <c>.eml</c> must carry (095 review F1).</summary>
    public IReadOnlyList<string> MarkedSha256 { get; init; } = Array.Empty<string>();

    /// <summary>Whether <see cref="EmlParts"/> carries every one of <see cref="MarkedSha256"/>, counted (two identical marked files need
    /// two parts).</summary>
    public bool PreservesEveryMarkedPart()
    {
        var available = (EmlParts ?? Array.Empty<EmlPart>())
            .GroupBy(p => p.Sha256, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        foreach (var required in MarkedSha256.GroupBy(s => s, StringComparer.OrdinalIgnoreCase))
        {
            if (!available.TryGetValue(required.Key, out var count) || count < required.Count())
            {
                return false;
            }
        }

        return true;
    }
}
