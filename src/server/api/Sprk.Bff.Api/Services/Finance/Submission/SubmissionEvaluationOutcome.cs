using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

// ─────────────────────────────────────────────────────────────────────────────
// Contracts of the InvoiceSubmissionProcessing pipeline, steps 1-5 (ebilling-r1 task 049 part B): the submission as the job read it,
// and the outcome that persistence (part C) and the after-commit work (part D) consume. Notes: 049-submission-processing-job.md.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>How the pipeline classified a submission (D-101; notes 049 section 2).</summary>
public enum SubmissionEvaluationKind
{
    /// <summary>The decider (073) accepted. Invoice In Review with every finding; submission Accepted.</summary>
    Accepted,

    /// <summary>The decider rejected an SPA submission. Invoice created Rejected (D-101) through 014; submission Rejected with the
    /// first deciding finding's reason code.</summary>
    AutoRejected,

    /// <summary>The decider rejected an Internal submission. Invoice In Review, held; submission Rejected with
    /// <see cref="SubmissionReasonCodes.Blocked"/>.</summary>
    Blocked,

    /// <summary>A non-retryable failure (flag off, AI read off, missing tracking number or received date). No invoice; submission Failed
    /// with <see cref="SubmissionEvaluationOutcome.ReasonCode"/>. D-126: line coding that cannot run after the read is not a failure
    /// (<see cref="AcceptedUncoded"/>).</summary>
    Failed,

    /// <summary>A document-level read failure (FR-21: unreadable, page limit, no invoice). No invoice; submission Rejected with that code.</summary>
    RejectedUnreadable,

    /// <summary>
    /// D-126: the invoice was read and resolved but its lines could not be coded (no code set for the core record, or line coding switched
    /// off) and no billing rule rejected it. The invoice and its uncoded lines are created In Review with the approval portion, held for
    /// review by one pipeline finding (<see cref="SubmissionReasonCodes.CodingNoCodeSetHold"/> or
    /// <see cref="SubmissionReasonCodes.CodingUnavailableHold"/>) plus any rule findings; the submission is Accepted with that coding code
    /// as its reason (D-130 b). D-130 (1): the rules still run on the firm's billed data (<see cref="SubmissionEvaluationOutcome.Coding"/> is
    /// null, so no AI-assigned field exists and a rule reading one holds as <c>ai_assigned_input</c>). A rule that rejects makes the outcome
    /// <see cref="AutoRejected"/> or <see cref="Blocked"/> as usual, still carrying the coding hold finding.
    /// </summary>
    AcceptedUncoded,
}

/// <summary>
/// The submission as the job read it (one <c>sprk_invoicesubmission</c> read, steps 0c / 0d). The core record here is the one the
/// evaluation judged under and the one the invoice must be filed under (072 part L): part C re-reads the submission before persisting
/// and re-evaluates when it changed.
/// </summary>
public sealed record SubmissionSnapshot
{
    /// <summary>The columns <see cref="Read"/> needs; the job retrieves the submission with these.</summary>
    public static IReadOnlyList<string> Columns { get; } =
    [
        TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.SequenceColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn,
        TrackingNumberAllocator.FirmColumn, "sprk_channel", "sprk_document", "sprk_processingstatus", "sprk_invoice",
        "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment", "createdon",
        // ebilling-r1 task 097b: a Correction's target (ResubmissionOfSubmissionId).
        InvoiceSubmissionService.ResubmissionOfColumn,
    ];

    public required Guid SubmissionId { get; init; }

    /// <summary><c>sprk_vendororg</c>.</summary>
    public required Guid FirmId { get; init; }

    /// <summary>The one <c>sprk_regarding{core}</c> lookup (FR-05).</summary>
    public required InvoiceCoreRecord CoreRecord { get; init; }

    public required InvoiceSubmissionChannel Channel { get; init; }

    /// <summary>D-87 tracking number (<c>sprk_trackingnumber</c>), allocated at intake; never allocated or changed by the job.</summary>
    public required string TrackingNumber { get; init; }

    /// <summary><c>sprk_submissionsequence</c>, the <c>n</c> of the tracking number.</summary>
    public required int Sequence { get; init; }

    /// <summary>The firm invoice number as typed at upload (D-91).</summary>
    public required string FirmInvoiceNumber { get; init; }

    /// <summary>
    /// <c>sprk_document</c>: the Submit Invoice SOURCE document (ebilling-r1 task 094; the confirm and from-document routes), whose bytes the
    /// job copies into the invoice's Stored File (9.1 step 2). <c>null</c> for an upload (ebilling-r1 task 092): its PDF is staged in the
    /// Finance file store under <see cref="Files.StoredFileIds.ForUpload"/> of the submission.
    /// </summary>
    public Guid? DocumentId { get; init; }

    /// <summary>
    /// Where the invoice's file comes from (task 094; 092 adversarial review item 1): THE one discriminator every channel-specific step
    /// reads (the job's byte read and staging). Decided once, here, from the stored row; never inferred from a null document elsewhere.
    /// </summary>
    public required SubmissionFileSource FileSource { get; init; }

    /// <summary>True when the PDF is the upload staged in the Finance file store (no document; task 092).</summary>
    public bool IsUpload => FileSource == SubmissionFileSource.Upload;

    /// <summary>
    /// ebilling-r1 task 097b (EB-D-179 item 1): <c>sprk_resubmissionof</c>, the earlier submission a Correction replaces; null for a New
    /// submission. The replaced invoice is resolved from it by <see cref="ReadCorrectedInvoiceAsync"/>, never assumed.
    /// </summary>
    public Guid? ResubmissionOfSubmissionId { get; init; }

    /// <summary><c>createdon</c> in UTC.</summary>
    public required DateTime CreatedOnUtc { get; init; }

    /// <summary>The date rules are judged on (072 <c>ReceivedOn</c>): <see cref="CreatedOnUtc"/>'s UTC calendar day.</summary>
    public DateOnly ReceivedOn => DateOnly.FromDateTime(CreatedOnUtc);

    /// <summary>
    /// Reads a submission row. Fails closed: a missing tracking number, <c>createdon</c>, firm, core record (or more than one), typed
    /// firm invoice number or supported channel gives a <see cref="SubmissionEvaluationKind.Failed"/> reason code, never a default.
    /// </summary>
    /// <remarks>ebilling-r1 task 092: no document is an upload (its PDF is in the file store). Task 094 (a deliberate change of 092's rule,
    /// recorded in notes/094-submit-invoice.md): an INTERNAL submission that names a document is a Submit Invoice source submission,
    /// whatever its id (the confirm's derived id, the from-document route's, or 095's from-email id), and its bytes are copied, never read
    /// in place. A document on an SPA submission is the retired intake shape (an SPE upload with a document per submission) and is refused
    /// with <see cref="SubmissionReasonCodes.DocumentUnavailable"/> (dev data is recreated, not migrated: O-13).</remarks>
    public static (SubmissionSnapshot? Snapshot, string? FailureReasonCode) Read(Entity row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // The core-record rule (exactly one of the three lookups, FR-05) is 044's, read through its own mapping.
        var view = InvoiceSubmissionService.ToView(row);
        var sequence = row.GetAttributeValue<int?>(TrackingNumberAllocator.SequenceColumn);
        if (sequence is not > 0 || string.IsNullOrWhiteSpace(view.TrackingNumber))
        {
            return (null, SubmissionReasonCodes.TrackingMissing);
        }

        // 0001-01-01 is no received date either: no rule version is in force on it (072 part W).
        if (view.CreatedOn is not { } createdOn || createdOn == default)
        {
            return (null, SubmissionReasonCodes.ReceivedOnMissing);
        }

        if (view.FirmId is not { } firmId || firmId == Guid.Empty)
        {
            return (null, SubmissionReasonCodes.FirmMissing);
        }

        if (view.CoreRecord is not { } core)
        {
            return (null, SubmissionReasonCodes.CoreRecordMissing);
        }

        if (string.IsNullOrWhiteSpace(view.FirmInvoiceNumber))
        {
            return (null, SubmissionReasonCodes.FirmInvoiceNumberMissing);
        }

        var channel = row.GetAttributeValue<OptionSetValue>("sprk_channel")?.Value;
        if (channel is not ((int)InvoiceSubmissionChannel.Spa or (int)InvoiceSubmissionChannel.Internal))
        {
            return (null, SubmissionReasonCodes.ChannelUnsupported);
        }

        var documentId = view.DocumentId is { } linkedDocument && linkedDocument != Guid.Empty ? linkedDocument : (Guid?)null;
        if (documentId is not null && channel != (int)InvoiceSubmissionChannel.Internal)
        {
            return (null, SubmissionReasonCodes.DocumentUnavailable);
        }

        // Fix round F1-1 (security): the document is a source only for the submission the intake created FOR it - the id derived from that
        // document (ExistingDocumentSubmissionIdFor; the confirm and the from-document route both use it, after checking the caller's Read on
        // the document). Any other submission naming a document (a sprk_document written onto an upload submission afterwards, or a derived
        // submission re-pointed at another document) would have the job copy and extract a document no one checked: it fails closed.
        if (documentId is { } source && !InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(source, row.Id))
        {
            return (null, SubmissionReasonCodes.DocumentUnavailable);
        }

        return (new SubmissionSnapshot
        {
            SubmissionId = row.Id,
            FirmId = firmId,
            CoreRecord = core,
            Channel = (InvoiceSubmissionChannel)channel.Value,
            TrackingNumber = view.TrackingNumber!,
            Sequence = sequence.Value,
            FirmInvoiceNumber = view.FirmInvoiceNumber!,
            DocumentId = documentId,
            FileSource = documentId is null ? SubmissionFileSource.Upload : SubmissionFileSource.SourceDocument,
            ResubmissionOfSubmissionId = view.ResubmissionOfId,
            CreatedOnUtc = createdOn.Kind == DateTimeKind.Local ? createdOn.ToUniversalTime() : DateTime.SpecifyKind(createdOn, DateTimeKind.Utc),
        }, null);
    }

    /// <summary>
    /// ebilling-r1 task 097b: the invoice this submission corrects, resolved from <see cref="ResubmissionOfSubmissionId"/>: that submission's
    /// <c>sprk_invoice</c>, accepted only when it is <see cref="InvoiceSubmissionPersister.InvoiceIdFor"/> of that submission (the invoice
    /// the job created from it; the intake guard records nothing else). For task 097c's persister, which closes the old invoice.
    /// </summary>
    /// <returns><see cref="CorrectedInvoiceRead.None"/> for a New submission; <see cref="CorrectedInvoiceRead.Resolved"/> with both ids; or
    /// <see cref="CorrectedInvoiceRead.Unresolved"/> (fail closed) when the earlier submission is gone, names no invoice or names another one.
    /// A read fault propagates (the caller's retry).</returns>
    public Task<CorrectedInvoiceRead> ReadCorrectedInvoiceAsync(IGenericEntityService entities, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        return ResubmissionOfSubmissionId is { } earlier
            ? ReadCorrectedInvoiceAsync(entities, earlier, ct)
            : Task.FromResult(CorrectedInvoiceRead.None);
    }

    /// <summary>
    /// The same resolution from the stored <c>sprk_resubmissionof</c> alone (<paramref name="earlier"/>), for a reader that has the submission
    /// row but no snapshot: task 097c's undo (<see cref="SubmissionCommitGate.CompensateAsync"/>), which may run on a later delivery.
    /// </summary>
    internal static async Task<CorrectedInvoiceRead> ReadCorrectedInvoiceAsync(IGenericEntityService entities, Guid earlier, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        Entity row;
        try
        {
            row = await entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, earlier, [InvoiceSubmissionService.InvoiceColumn], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            return CorrectedInvoiceRead.Unresolved(earlier);
        }

        var invoice = row?.GetAttributeValue<EntityReference>(InvoiceSubmissionService.InvoiceColumn)?.Id;
        return invoice is { } id && id == InvoiceSubmissionPersister.InvoiceIdFor(earlier)
            ? CorrectedInvoiceRead.Resolved(earlier, id)
            : CorrectedInvoiceRead.Unresolved(earlier);
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

/// <summary>
/// ebilling-r1 task 097b: what <see cref="SubmissionSnapshot.ReadCorrectedInvoiceAsync"/> found. <see cref="IsCorrection"/> false: a New
/// submission. Otherwise <see cref="CorrectedInvoiceId"/> is set only when the target resolved (fail closed when it did not).
/// </summary>
public sealed record CorrectedInvoiceRead(bool IsCorrection, Guid? ResubmissionOfSubmissionId, Guid? CorrectedInvoiceId)
{
    public static CorrectedInvoiceRead None { get; } = new(false, null, null);

    public static CorrectedInvoiceRead Resolved(Guid resubmissionOf, Guid invoiceId) => new(true, resubmissionOf, invoiceId);

    public static CorrectedInvoiceRead Unresolved(Guid resubmissionOf) => new(true, resubmissionOf, null);
}

/// <summary>
/// ebilling-r1 task 094 (092 adversarial review item 1): where a submission's invoice file comes from, decided once by
/// <see cref="SubmissionSnapshot.Read"/>. Task 095 adds its from-email kind here (the .eml's marked parts, staged under
/// <see cref="Files.StoredFileIds.ForAttachment"/>); the job switches on it and refuses (permanently) a kind it does not handle.
/// The post-commit gate (<see cref="SubmissionCommitGate"/>) needs no kind: it finds the invoice's files by their parent and reads each
/// row's own source.
/// </summary>
public enum SubmissionFileSource
{
    /// <summary>The PDF uploaded through a submission route, staged at intake under <see cref="Files.StoredFileIds.ForUpload"/> (task 092).</summary>
    Upload,

    /// <summary>A Submit Invoice source document whose bytes the job copies (9.1 step 2) under <see cref="Files.StoredFileIds.ForSourceVersion"/>.</summary>
    SourceDocument,
}

/// <summary>D-91: how the firm invoice number read from the PDF compares with the typed one.</summary>
public enum InvoiceNumberMatch
{
    /// <summary>Equal once case, whitespace and punctuation are ignored.</summary>
    Match,

    /// <summary>Different. A hold finding (<see cref="SubmissionReasonCodes.InvoiceNumberMismatch"/>), never a rejection.</summary>
    Mismatch,

    /// <summary>The PDF shows no invoice number. Nothing to compare (a missing value is RequiredData's, 071), so no D-91 finding.</summary>
    NotRead,
}

/// <summary>D-91 comparison result: both values as typed and as read, and the read value's analyzer confidence.</summary>
public sealed record InvoiceNumberCheck(InvoiceNumberMatch Result, string Typed, string? Read, decimal? ReadConfidence)
{
    /// <summary>
    /// Compares <paramref name="typed"/> with <paramref name="read"/>: letters and digits only, upper-cased with the invariant culture.
    /// A value with no letter or digit is compared trimmed and case-insensitively instead, so it never equals everything.
    /// </summary>
    public static InvoiceNumberCheck Compare(string typed, ExtractedInvoiceField<string?> read)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(read);
        if (string.IsNullOrWhiteSpace(read.Value))
        {
            return new(InvoiceNumberMatch.NotRead, typed, read.Value, read.Confidence);
        }

        var a = Normalise(typed);
        var b = Normalise(read.Value);
        var equal = a.Length > 0 && b.Length > 0
            ? string.Equals(a, b, StringComparison.Ordinal)
            : string.Equals(typed.Trim(), read.Value.Trim(), StringComparison.OrdinalIgnoreCase);
        return new(equal ? InvoiceNumberMatch.Match : InvoiceNumberMatch.Mismatch, typed, read.Value, read.Confidence);
    }

    internal static string Normalise(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}

/// <summary>
/// A finding the pipeline itself raises (not a billing rule's): it holds the invoice for review and never rejects. Part D writes it as
/// a Signal under the D-123 system billing policy once ontology-r1 agrees that design; until then it is stored in the findings JSON.
/// </summary>
/// <param name="ReasonCode">A <see cref="SubmissionReasonCodes"/> pipeline finding code.</param>
/// <param name="Values">The values it cites, by name (for D-91: <c>typed</c> and <c>read</c>).</param>
public sealed record SubmissionPipelineFinding(string ReasonCode, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// What steps 1-5 produced for one submission (task 049 part B). Part C persists it all-or-nothing; part D writes the Signals,
/// the Decision Record and the terminal status from it. Members after the failure point are null.
/// </summary>
public sealed record SubmissionEvaluationOutcome
{
    public required SubmissionEvaluationKind Kind { get; init; }

    public required Guid SubmissionId { get; init; }

    /// <summary>
    /// The terminal reason code: the failure code (<see cref="SubmissionEvaluationKind.Failed"/>), the read code
    /// (<see cref="SubmissionEvaluationKind.RejectedUnreadable"/>), the first deciding finding's reason code
    /// (<see cref="SubmissionEvaluationKind.AutoRejected"/>), <see cref="SubmissionReasonCodes.Blocked"/>
    /// (<see cref="SubmissionEvaluationKind.Blocked"/>), the coding code (<see cref="SubmissionEvaluationKind.AcceptedUncoded"/>:
    /// <see cref="SubmissionReasonCodes.CodingNoCodeSetHold"/> or <see cref="SubmissionReasonCodes.CodingUnavailableHold"/>, D-130 b),
    /// or null (<see cref="SubmissionEvaluationKind.Accepted"/>).
    /// </summary>
    public string? ReasonCode { get; init; }

    /// <summary>The submission read the pipeline ran on; null when the row failed <see cref="SubmissionSnapshot.Read"/> or the flag is off.</summary>
    public SubmissionSnapshot? Submission { get; init; }

    /// <summary>The date the rules were judged on (<see cref="SubmissionSnapshot.ReceivedOn"/>).</summary>
    public DateOnly? ReceivedOn => Submission?.ReceivedOn;

    /// <summary>The core record the evaluation used, which the invoice must be filed under (072 part L).</summary>
    public InvoiceCoreRecord? CoreRecord => Submission?.CoreRecord;

    /// <summary>Step 1: the invoice as read (043). Line <c>i</c> here is line index <c>i</c> everywhere below.</summary>
    public ExtractedInvoice? Read { get; init; }

    /// <summary>Step 1c: the D-91 comparison.</summary>
    public InvoiceNumberCheck? InvoiceNumber { get; init; }

    /// <summary>Pipeline findings (D-91 mismatch; D-126 lines not coded). Each holds; none rejects.</summary>
    public IReadOnlyList<SubmissionPipelineFinding> PipelineFindings { get; init; } = [];

    /// <summary>Step 2: arithmetic, timekeeper and rate resolution and combined confidence per field (046).</summary>
    public InvoiceResolutionResult? Resolution { get; init; }

    /// <summary>Step 3: codes, observations and the code set per line (047; each line's <c>CodeSetSource</c> / <c>CodeSetNeedsReview</c>).
    /// <c>LineKey</c> is the line index in invariant text. Null when the submission is uncoded (D-126): an
    /// <see cref="SubmissionEvaluationKind.AcceptedUncoded"/> outcome, or an AutoRejected / Blocked one carrying a coding hold finding.</summary>
    public InvoiceCodingResult? Coding { get; init; }

    /// <summary>Step 4 input: exactly what was judged (for the findings JSON and part C's re-evaluation check).</summary>
    public BillingEvaluationRequest? EvaluationRequest { get; init; }

    /// <summary>Step 4: the billing findings (072).</summary>
    public BillingEvaluationResult? Evaluation { get; init; }

    /// <summary>Step 5: the decision (073); <see cref="SubmissionDecision.DecidingFindings"/> are the findings that rejected.</summary>
    public SubmissionDecision? Decision { get; init; }

    /// <summary>
    /// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 2): the copies the job staged from the submission's source before the commit
    /// (one for a Submit Invoice source document; task 095: one per marked part of the .eml). The persister writes one Stored File row per
    /// copy and guards each distinct source at the version it was staged from. Empty for an upload (its file is the intake's stage). Set by
    /// the job after the evaluation (the processor never stages).
    /// </summary>
    public IReadOnlyList<StagedSourceCopy> StagedFiles { get; init; } = [];

    internal static SubmissionEvaluationOutcome Failed(Guid submissionId, string reasonCode, SubmissionSnapshot? submission = null,
        ExtractedInvoice? read = null) =>
        new() { Kind = SubmissionEvaluationKind.Failed, SubmissionId = submissionId, ReasonCode = reasonCode, Submission = submission, Read = read };
}
