namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095: THE seam between the from-email route and task 094's Submit Invoice operation ("submit this source to the
/// Submit Invoice operation"). 095 resolves everything the route owns (the marks, the bytes, the firm, the replay, the in-process
/// <c>.eml</c> save and its resolution from the communication) and hands the operation one <see cref="FromEmailSubmitInput"/>; 094
/// implements it (model §9.1 steps 1 to 6). Until 094 merges, <see cref="SubmitInvoiceFromEmailNotYetAvailable"/> stands in and the
/// route answers 503 <see cref="InvoiceSubmissionErrors.Failed"/> before any write.
/// </summary>
/// <remarks>
/// <para><b>Why no adapter onto today's intake.</b> <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/> would point the
/// submission at the <c>.eml</c> as if it were the invoice PDF (the job would extract the <c>.eml</c> and re-file it under the invoice,
/// <see cref="InvoiceDocumentRefile"/>), and derives its ids from the document, not from K and a generation;
/// <see cref="InvoiceSubmissionService.SubmitAsync"/> creates a second, invoice-PDF Document with random ids. Neither can carry a
/// source <c>.eml</c> with marked parts without changing what it does today (notes/095-submit-invoice-from-email.md, "Seam").</para>
/// <para><b>What 094 must do</b> (the contract this interface pins): create the submission row with exactly
/// <see cref="FromEmailSubmitInput.SubmissionId"/> through <see cref="ITrackingNumberAllocator.CreateSubmissionAsync"/> (its
/// <see cref="TrackingNumberOutcome.AlreadyCreated"/> is the race guard two concurrent retries rely on, EB-D-160), with
/// <c>sprk_document</c> = the <c>.eml</c>, Channel Internal (OP-6) and the caller as creator person (OP-5 reads it); queue the job
/// <see cref="FromEmailSubmitInput.JobId"/> only after the <c>.eml</c>'s finalization; read the marked parts in the job with
/// <see cref="EmlMarkedPartReader"/>; and run step A (filing an unfiled communication) and steps 1 to 6.</para>
/// </remarks>
internal interface ISubmitInvoiceFromEmail
{
    /// <summary>False until task 094's operation is wired: the route then refuses before any write.</summary>
    bool IsAvailable { get; }

    /// <summary>Submits the resolved source. Never throws for a refusal.</summary>
    Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct);

    /// <summary>Re-queues the job of a submission that is still Received (a replay, contract section 5). False when the queue refused it.</summary>
    Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct);

    /// <summary>
    /// The SHA-256 (lower-case hex) of the Invoice part a submission recorded, for the replay's "other details differ" check (contract
    /// section 5); null when it is not recorded. A read fault throws.
    /// </summary>
    Task<string?> ReadInvoicePartSha256Async(Guid submissionId, CancellationToken ct);
}

/// <summary>A marked attachment as the submission names it: matched against the <c>.eml</c>'s MIME parts (model §9.1 step 2).</summary>
/// <param name="Ordinal">The part's 0-based ordinal among the <c>.eml</c>'s attachment parts (<c>EmlMimeParts</c>).</param>
/// <param name="FileName">The part's file name as stored in the <c>.eml</c>.</param>
/// <param name="Sha256">The SHA-256 (lower-case hex) of the part's decoded bytes.</param>
/// <param name="Mark"><see cref="FromEmailMarks.Invoice"/> or <see cref="FromEmailMarks.Supporting"/> (Don't save is never passed).</param>
internal sealed record FromEmailMarkedPart(int Ordinal, string FileName, string Sha256, string Mark);

/// <summary>Everything the Submit Invoice operation needs from the from-email route (task 095).</summary>
internal sealed record FromEmailSubmitInput
{
    public required Guid EmlDocumentId { get; init; }
    public required Guid CommunicationId { get; init; }
    public required InvoiceCoreRecord CoreRecord { get; init; }
    public required Guid FirmId { get; init; }

    /// <summary>Trimmed (D-91).</summary>
    public required string FirmInvoiceNumber { get; init; }

    public string? PoNumber { get; init; }

    /// <summary>The Invoice part first, then the Supporting parts in attachment order.</summary>
    public required IReadOnlyList<FromEmailMarkedPart> MarkedParts { get; init; }

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

    public static FromEmailSubmitResult Received(string trackingNumber) =>
        new() { Outcome = FromEmailSubmitOutcome.Received, TrackingNumber = trackingNumber };

    public static FromEmailSubmitResult AlreadyCreated() => new() { Outcome = FromEmailSubmitOutcome.AlreadyCreated };

    public static FromEmailSubmitResult Refused(int status, string errorCode, string detail) =>
        new() { Outcome = FromEmailSubmitOutcome.Refused, Status = status, ErrorCode = errorCode, Detail = detail };
}

/// <summary>Thrown by <see cref="SubmitInvoiceFromEmailNotYetAvailable"/> if it is ever called past its <see cref="ISubmitInvoiceFromEmail.IsAvailable"/> gate.</summary>
internal sealed class SubmitInvoiceFromEmailNotYetAvailableException()
    : InvalidOperationException("The Submit Invoice operation for a from-email source is task 094's and is not available yet.");

/// <summary>
/// The stand-in until task 094 (ebilling-r1, EB-D-158 early start): not available, so the route refuses 503
/// <see cref="InvoiceSubmissionErrors.Failed"/> before any write. Every member past the gate throws, as defence in depth.
/// </summary>
internal sealed class SubmitInvoiceFromEmailNotYetAvailable : ISubmitInvoiceFromEmail
{
    public bool IsAvailable => false;

    public Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct) =>
        throw new SubmitInvoiceFromEmailNotYetAvailableException();

    public Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct) =>
        throw new SubmitInvoiceFromEmailNotYetAvailableException();

    public Task<string?> ReadInvoicePartSha256Async(Guid submissionId, CancellationToken ct) =>
        throw new SubmitInvoiceFromEmailNotYetAvailableException();
}
