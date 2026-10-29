namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095: THE seam between the from-email route and task 094's Submit Invoice operation ("submit this source to the
/// Submit Invoice operation"). 095 resolves everything the route owns (the marks, the bytes, the firm, the replay, the in-process
/// <c>.eml</c> save and its resolution from the communication) and hands the operation one <see cref="FromEmailSubmitInput"/>; 094
/// implements it (model §9.1 steps 1 to 6): <see cref="SubmitInvoiceFromEmailOperation"/>, which replaced 095's not-yet-available
/// stand-in (task 094). A seam that reports <see cref="IsAvailable"/> false still makes the route answer 503
/// <see cref="InvoiceSubmissionErrors.Failed"/> before any write.
/// </summary>
/// <remarks>
/// <para><b>Why no adapter onto today's intake.</b> <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/> would point the
/// submission at the <c>.eml</c> as if it were the invoice PDF (the job would extract the <c>.eml</c> and re-file it under the invoice,
/// <see cref="InvoiceDocumentRefile"/>), and derives its ids from the document, not from K and a generation;
/// <see cref="InvoiceSubmissionService.SubmitAsync"/> creates a second, invoice-PDF Document with random ids. Neither can carry a
/// source <c>.eml</c> with marked parts without changing what it does today (notes/095-submit-invoice-from-email.md, "Seam").</para>
/// <para><b>What 094 must do</b> (the contract this interface pins; task 092's file-store intake):</para>
/// <list type="number">
/// <item>Read each marked part out of the saved <c>.eml</c> with <see cref="EmlMarkedPartReader"/>: its ordinal, name and SHA-256
/// must match (uac-r2 F2-c; the read's <c>versionnumber</c> is the source version).</item>
/// <item>Stage each part in the Finance file store under its <see cref="FromEmailMarkedPart.StoredFileId"/>. The Invoice part goes
/// under <c>StoredFileIds.ForUpload(SubmissionId)</c>, exactly as 092's upload does, with purpose Invoice PDF. Supporting parts are
/// Invoice supporting files. All are Firm-visible = No, with the future invoice (<c>InvoiceIdFor(SubmissionId)</c>) as parent. The
/// source <c>.eml</c> and its communication are recorded as the Stored File's <c>sprk_sourcedocument</c> and
/// <c>sprk_sourcecommunication</c> (model §5 table).</item>
/// <item>Create the submission row with exactly <see cref="FromEmailSubmitInput.SubmissionId"/> through
/// <see cref="ITrackingNumberAllocator.CreateSubmissionAsync"/>. Its <see cref="TrackingNumberOutcome.AlreadyCreated"/> is the race
/// guard two concurrent retries rely on (EB-D-160). The row has Channel Internal (OP-6) and the caller as creator person (OP-5 reads
/// it). <b>Its <c>sprk_document</c> stays EMPTY</b>: 092's <c>SubmissionSnapshot.Read</c> fails any document that is not a 050a
/// confirm with <c>document_unavailable</c>.</item>
/// <item>Queue the job <see cref="FromEmailSubmitInput.JobId"/>. It reads the staged bytes through 092's
/// <c>StoredFileService.OpenStagedForProcessingAsync</c>.</item>
/// <item>Run step A (filing an unfiled communication) and steps 1 to 6.</item>
/// <item>Answer <see cref="ReadAttemptSourceAsync"/> from what it recorded.</item>
/// </list>
/// </remarks>
internal interface ISubmitInvoiceFromEmail
{
    /// <summary>True for task 094's operation; false makes the route refuse before any write.</summary>
    bool IsAvailable { get; }

    /// <summary>Submits the resolved source. Never throws for a refusal.</summary>
    Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct);

    /// <summary>Re-queues the job of a submission that is still Received (a replay, contract section 5). False when the queue refused it.</summary>
    Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct);

    /// <summary>
    /// What an earlier attempt recorded about its source, for the replay (contract section 5): the source <c>.eml</c> (the 202's
    /// <c>emlDocumentId</c>) and the Invoice part's SHA-256 (the "other details differ" check). Null when nothing is recorded (the replay
    /// then answers 503 failed). A read fault throws. The submission row names no document (092), so this cannot come from it.
    /// </summary>
    Task<FromEmailAttemptSource?> ReadAttemptSourceAsync(Guid submissionId, CancellationToken ct);
}

/// <summary>An earlier attempt's source, as the Submit Invoice operation recorded it (<see cref="ISubmitInvoiceFromEmail.ReadAttemptSourceAsync"/>).</summary>
/// <param name="SourceEmlDocumentId">The <c>.eml</c> Document the attempt's files were copied from.</param>
/// <param name="InvoiceSha256">The SHA-256 (lower-case hex) of its Invoice part.</param>
internal sealed record FromEmailAttemptSource(Guid SourceEmlDocumentId, string InvoiceSha256);

/// <summary>A marked attachment as the submission names it: matched against the <c>.eml</c>'s MIME parts (model §9.1 step 2).</summary>
/// <param name="Ordinal">The part's 0-based ordinal among the <c>.eml</c>'s attachment parts (<c>EmlMimeParts</c>).</param>
/// <param name="FileName">The part's file name as stored in the <c>.eml</c>.</param>
/// <param name="Sha256">The SHA-256 (lower-case hex) of the part's decoded bytes.</param>
/// <param name="Mark"><see cref="FromEmailMarks.Invoice"/> or <see cref="FromEmailMarks.Supporting"/> (Don't save is never passed).</param>
/// <param name="StoredFileId">The Stored File the part is staged under, deterministic from the submission id
/// (<see cref="FromEmailSubmissionKey.StoredFileIdFor"/>). Empty until the submission id is known.</param>
internal sealed record FromEmailMarkedPart(int Ordinal, string FileName, string Sha256, string Mark, Guid StoredFileId = default);

/// <summary>Everything the Submit Invoice operation needs from the from-email route (task 095).</summary>
internal sealed record FromEmailSubmitInput
{
    /// <summary>
    /// The source <c>.eml</c> Document. It is recorded as the Stored Files' <c>sprk_sourcedocument</c> and is the copy rule's and the step 4
    /// guard's source (model §9.1). It is NEVER the submission's <c>sprk_document</c> (092).
    /// </summary>
    public required Guid SourceEmlDocumentId { get; init; }

    /// <summary>The email's communication (the Stored Files' <c>sprk_sourcecommunication</c>).</summary>
    public required Guid CommunicationId { get; init; }
    public required InvoiceCoreRecord CoreRecord { get; init; }
    public required Guid FirmId { get; init; }

    /// <summary>Trimmed (D-91).</summary>
    public required string FirmInvoiceNumber { get; init; }

    public string? PoNumber { get; init; }

    /// <summary>The Invoice part first, then the Supporting parts in attachment order; each with its Stored File id.</summary>
    public required IReadOnlyList<FromEmailMarkedPart> MarkedParts { get; init; }

    /// <summary>The Invoice part's Stored File: <c>StoredFileIds.ForUpload(SubmissionId)</c>, as 092's upload stages its PDF.</summary>
    public Guid InvoiceStoredFileId => MarkedParts.Single(p => p.Mark == FromEmailMarks.Invoice).StoredFileId;

    /// <summary>Deterministic from K and <see cref="Generation"/> (<see cref="FromEmailSubmissionKey"/>).</summary>
    public required Guid SubmissionId { get; init; }

    public required Guid JobId { get; init; }

    /// <summary>1 for the first attempt; n+1 after attempt n ended Failed (EB-D-160).</summary>
    public required int Generation { get; init; }

    /// <summary><see cref="FromEmailModes.New"/> or <see cref="FromEmailModes.Correction"/> (097).</summary>
    public required string Mode { get; init; }

    /// <summary>The invoice a Correction replaces; null for New.</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>The caller's systemuser (the submission's creator person, OP-5).</summary>
    public required Guid CallerSystemUserId { get; init; }

    public Guid? CallerObjectId { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>
    /// Task 094: the request the operation acts for. Step 1 checks the caller's own Read on the <c>.eml</c> and its communication, and
    /// step A files an unfiled communication as the caller, both through this request (its token and its scoped services).
    /// </summary>
    public HttpContext? Http { get; init; }
}

internal enum FromEmailSubmitOutcome
{
    /// <summary>The submission row was created and its job queued (or will be, after the <c>.eml</c>'s finalization).</summary>
    Received,

    /// <summary>The row with this id already existed (a concurrent retry or a lost reply): the caller answers it as a replay.</summary>
    AlreadyCreated,

    /// <summary>The operation refused; nothing of the submission was written.</summary>
    Refused,
}

/// <summary>The Submit Invoice operation's answer.</summary>
internal sealed record FromEmailSubmitResult
{
    public required FromEmailSubmitOutcome Outcome { get; init; }

    /// <summary>The tracking number (Received).</summary>
    public string? TrackingNumber { get; init; }

    /// <summary>The refusal (Refused): status, code and the client-facing detail.</summary>
    public int Status { get; init; }

    public string? ErrorCode { get; init; }

    public string? Detail { get; init; }

    /// <summary>
    /// <see cref="FromEmailSubmitOutcome.AlreadyCreated"/>: step A filed the communication for THIS request before the create lost the race
    /// (review round 5), so a refusal the route answers it with says the filing is kept (contract section 6, "After step A").
    /// </summary>
    public bool Filed { get; init; }

    public static FromEmailSubmitResult Received(string trackingNumber) =>
        new() { Outcome = FromEmailSubmitOutcome.Received, TrackingNumber = trackingNumber };

    public static FromEmailSubmitResult AlreadyCreated(bool filed = false) => new() { Outcome = FromEmailSubmitOutcome.AlreadyCreated, Filed = filed };

    public static FromEmailSubmitResult Refused(int status, string errorCode, string detail) =>
        new() { Outcome = FromEmailSubmitOutcome.Refused, Status = status, ErrorCode = errorCode, Detail = detail };
}
