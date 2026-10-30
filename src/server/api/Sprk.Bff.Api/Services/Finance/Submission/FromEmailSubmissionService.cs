using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>What the from-email route answers: a receipt (202), a refusal (ProblemDetails with <c>errorCode</c>), or the in-process save's own
/// Office refusal, passed through as <c>/office/save</c> renders it.</summary>
internal sealed record FromEmailSubmissionOutcome
{
    public FromEmailReceipt? Receipt { get; init; }

    public int Status { get; init; }
    public string? ErrorCode { get; init; }
    public string? Detail { get; init; }

    /// <summary>The fields the contract puts beside the code (<c>reason</c>, <c>fileName</c>, <c>mark</c>).</summary>
    public IReadOnlyDictionary<string, object?>? Extensions { get; init; }

    public SaveError? OfficeError { get; init; }

    /// <summary>
    /// A refusal answered after step A filed the communication for this request: the route adds the top-level <c>filingKept: true</c>
    /// (contract section 6, "After step A", changed 2026-10-29). An <see cref="ErrorCode"/> refusal already carries the sentence in its
    /// <see cref="Detail"/>; an <see cref="OfficeError"/> pass-through is extended by the route.
    /// </summary>
    public bool FilingKept { get; init; }

    public static FromEmailSubmissionOutcome Accepted(FromEmailReceipt receipt) => new() { Receipt = receipt, Status = StatusCodes.Status202Accepted };

    public static FromEmailSubmissionOutcome Refused(int status, string errorCode, string detail, IReadOnlyDictionary<string, object?>? extensions = null) =>
        new() { Status = status, ErrorCode = errorCode, Detail = detail, Extensions = extensions };

    public static FromEmailSubmissionOutcome Office(SaveError error) => new() { OfficeError = error };
}

/// <summary>The 202's ids (contract section 3), before the route adds the status URL.</summary>
internal sealed record FromEmailReceipt(Guid SubmissionId, Guid JobId, string TrackingNumber, Guid EmlDocumentId);

/// <summary>
/// The from-email submission (ebilling-r1 task 095; model §9.1 "Email-only save"; wire contract <c>notes/095-from-email-contract.md</c>).
/// One call: the marks and bytes rules, the firm, the replay of an earlier attempt of the same K, the in-process <c>/office/save</c> of
/// the email alone (own Restricted, marked), the <c>.eml</c> resolved from its communication, then task 094's Submit Invoice operation
/// through <see cref="ISubmitInvoiceFromEmail"/>. Every refusal before the save writes nothing.
/// </summary>
/// <remarks>
/// <para><b>Order</b> (contract section 6 "Order"): marks (400), bytes (422), the firm invoice number (400), a Correction's Write on its
/// invoice (the uniform 404; task 097b), the firm (400 / 503) and its link (403 / 503), the operation's availability (503), the replay of K
/// (section 5), the communication's filing (409), a Correction's own checks (409 correction_not_allowed; task 097b), the save, the
/// <c>.eml</c>'s resolution, the operation.</para>
/// <para><b>Logs</b> carry ids, ordinals and codes only: never a file name, a subject or a Message-ID (model §9.1 step 3).</para>
/// </remarks>
internal sealed class FromEmailSubmissionService
{
    private static readonly string[] AttemptColumns =
    [
        "sprk_processingstatus", TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.FirmColumn,
        TrackingNumberAllocator.FirmInvoiceNumberColumn, "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
        RecordCreatorPerson.Column,
        // Review round 5 (pass-2 F1-1): whether a Failed attempt's undo is unfinished.
        InvoiceSubmissionPersister.SubmissionReasonCode, InvoiceSubmissionPersister.SubmissionInvoice,
    ];

    private const string DocumentEntity = "sprk_document";
    private const string DocumentRelatedCommunication = "sprk_relatedcommunication";
    private const string DocumentIsEmailArchive = "sprk_isemailarchive";
    private const string DocumentEmailMessageId = "sprk_emailmessageid";
    private const string DocumentParentDocument = "sprk_parentdocument";

    private readonly InvoiceSubmissionService _submissions;
    private readonly ISubmitInvoiceFromEmail _operation;
    private readonly IOfficeSubmissionEmailSave _save;
    private readonly IGenericEntityService _entities;
    private readonly ICommunicationDataverseService _communications;
    private readonly ICallerSystemUserResolver _callers;
    private readonly SpeFileStore _spe;
    private readonly ILogger<FromEmailSubmissionService> _logger;

    public FromEmailSubmissionService(
        InvoiceSubmissionService submissions,
        ISubmitInvoiceFromEmail operation,
        IOfficeSubmissionEmailSave save,
        IGenericEntityService entities,
        ICommunicationDataverseService communications,
        ICallerSystemUserResolver callers,
        SpeFileStore spe,
        ILogger<FromEmailSubmissionService> logger)
    {
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        _save = save ?? throw new ArgumentNullException(nameof(save));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _communications = communications ?? throw new ArgumentNullException(nameof(communications));
        _callers = callers ?? throw new ArgumentNullException(nameof(callers));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Runs the from-email submission for the caller of <paramref name="http"/>. Never throws for a refusal.</summary>
    public async Task<FromEmailSubmissionOutcome> SubmitAsync(
        FromEmailSubmissionInput input, string officeUserId, HttpContext http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(http);

        // ── 1. Marks (400) and bytes (422): nothing is read or written for a request that breaks them ──
        var (marksRefusal, saved) = FromEmailMarkRules.Check(input.Email.Attachments ?? new List<AttachmentReference>(), input.Marks);
        if (marksRefusal is not null)
        {
            var extensions = new Dictionary<string, object?>();
            if (marksRefusal.ErrorCode == FromEmailSubmissionErrors.MarksInvalid)
            {
                extensions["reason"] = marksRefusal.Reason;
            }

            if (marksRefusal.FileName is not null)
            {
                extensions["fileName"] = marksRefusal.FileName;
            }

            if (marksRefusal.Mark is not null)
            {
                extensions["mark"] = marksRefusal.Mark;
            }

            return FromEmailSubmissionOutcome.Refused(marksRefusal.Status, marksRefusal.ErrorCode, marksRefusal.Detail, extensions);
        }

        // ── 2. The firm invoice number (D-91) ──
        var number = input.FirmInvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvoiceNumberRequired,
                "The firm invoice number is required.");
        }

        // ── 3. Correction (task 097b; OP-7 until then): the caller's own Write on the invoice it replaces, before anything else is read
        //       about it. Without it, or for an invoice that does not exist, the uniform not-found answer (the route returns it as is) ──
        Entity? correctedInvoice = null;
        if (input.Mode == FromEmailModes.Correction)
        {
            var correction = await InvoiceSubmissionEndpoints.PrepareCorrectionAsync(
                input.InvoiceId ?? throw new ArgumentException("A Correction names its invoice (the body check).", nameof(input)), http, ct);
            if (correction.Refusal is { } correctionRefused)
            {
                throw new FromEmailRouteAnswerException(correctionRefused);
            }

            correctedInvoice = correction.Invoice;
        }

        // ── 4. The firm: active (400 / 503), then linked to the record (D-106: 403 / 503) ──
        if (await CheckFirmAsync(input, ct) is { } firmRefusal)
        {
            return firmRefusal;
        }

        // ── 5. The Submit Invoice operation (task 094) must be there before anything is written ──
        if (!_operation.IsAvailable)
        {
            _logger.LogWarning("From-email submission refused before any write: the Submit Invoice operation is not available yet (task 094)");
            return NotQueued();
        }

        var caller = await _callers.ResolveAsync(http.User, ct);
        if (!caller.IsResolved || !Guid.TryParse(caller.SystemUserId, out var callerId) || callerId == Guid.Empty)
        {
            // Step 0 already required a systemuser; reaching here without one is a transient read, never a guess.
            return Unavailable("Your user record could not be read right now. Nothing was saved; try again shortly.");
        }

        var invoiceSha = saved[0].Sha256;
        var k = FromEmailSubmissionKey.Of(input);

        // ── 6. The replay of K (contract section 5, EB-D-160, EB-D-165). K has no firm, number or caller: whoever submits this email,
        //       record and marking meets the same attempts, so one email never becomes a second invoice while one is live ──
        var latest = await FindLatestAttemptAsync(k, ct);
        if (latest.Refusal is { } readRefusal)
        {
            return readRefusal;
        }

        if (latest.Row is { } earlier)
        {
            if (await AnswerExistingAsync(earlier, latest.Generation, k, callerId, invoiceSha, input, http, ct) is { } answered)
            {
                return answered;
            }
        }

        var generation = latest.Row is null ? 1 : latest.Generation + 1;
        if (generation > FromEmailSubmissionKey.MaxGeneration)
        {
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.SubmissionAlreadyEnded,
                "This email was already submitted as an invoice too many times. Nothing was saved.");
        }

        // ── 7. The communication, if the email was already captured, must not be filed under another core record (409) ──
        Guid? knownCommunication;
        try
        {
            knownCommunication = (await _communications.GetCommunicationByInternetMessageIdAsync(input.MessageId, ct))?.Id;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission refused before any write: the email's communication could not be read");
            return Unavailable("The email's record could not be read right now. Nothing was saved; try again shortly.");
        }

        if (knownCommunication is { } existing && existing != Guid.Empty
            && await CheckCommunicationFilingAsync(existing, input.CoreRecord, ct) is { } filingRefusal)
        {
            return filingRefusal;
        }

        // ── 7b. Task 097b: a Correction's own checks (the invoice's firm, core record, status, submission and firm invoice number; 409
        //        correction_not_allowed with its reason), read-only, before the save. Only a NEW attempt reaches here: an existing attempt of
        //        K (which names the invoice) was answered by the replay above, so a retry is never refused for what its own commit did. ──
        Guid? resubmissionOf = null;
        if (correctedInvoice is not null)
        {
            var (correctionRefusal, target) = await _submissions.CheckCorrectionAsync(input.CoreRecord, input.FirmId, number, correctedInvoice, ct);
            if (correctionRefusal is not null)
            {
                return FromEmailSubmissionOutcome.Refused(correctionRefusal.StatusCode, correctionRefusal.ErrorCode!,
                    correctionRefusal.Detail ?? "The invoice was not submitted.", correctionRefusal.Extensions);
            }

            resubmissionOf = target;
        }

        // ── 8. The email-only save (model §9.1 step 1): the /office/save pipeline in-process, no attachment Documents ──
        var saveRequest = new SaveRequest
        {
            ContentType = SaveContentType.Email,
            TargetEntity = new SaveEntityReference { EntityType = input.SaveEntityType, EntityId = input.CoreRecord.RecordId },
            Email = input.Email with { SelectedAttachmentFileNames = new List<string>() },
            IdempotencyKey = null,
        };

        var saveOutcome = await _save.SaveEmailForSubmissionAsync(saveRequest, saved.Select(m => m.Sha256).ToList(), officeUserId, http, ct);
        var resolved = await ResolveEmlAsync(saveOutcome, input, callerId, saved, ct);
        if (resolved.Refusal is { } emlRefusal)
        {
            return emlRefusal;
        }

        // ── 9. The Submit Invoice operation (task 094) with the .eml as its source ──
        var submissionId = FromEmailSubmissionKey.SubmissionIdFor(k, generation);
        var jobId = FromEmailSubmissionKey.JobIdFor(k, generation);
        var result = await _operation.SubmitAsync(new FromEmailSubmitInput
        {
            SourceEmlDocumentId = resolved.EmlDocumentId,
            CommunicationId = resolved.CommunicationId,
            CoreRecord = input.CoreRecord,
            FirmId = input.FirmId,
            FirmInvoiceNumber = number,
            PoNumber = string.IsNullOrWhiteSpace(input.PoNumber) ? null : input.PoNumber.Trim(),
            // Task 092: each part is staged in the file store under its deterministic Stored File id; the row names no document.
            MarkedParts = resolved.Parts.Select(p => p with { StoredFileId = FromEmailSubmissionKey.StoredFileIdFor(submissionId, p) }).ToList(),
            SubmissionId = submissionId,
            JobId = jobId,
            Generation = generation,
            Mode = input.Mode,
            InvoiceId = input.InvoiceId,
            ResubmissionOf = resubmissionOf,
            CallerSystemUserId = callerId,
            CallerObjectId = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null,
            CorrelationId = http.TraceIdentifier,
            Http = http,
        }, ct);

        switch (result.Outcome)
        {
            case FromEmailSubmitOutcome.Received when !string.IsNullOrEmpty(result.TrackingNumber):
                _logger.LogInformation(
                    "From-email submission {SubmissionId} (attempt {Generation}) received for {Entity} {RecordId} from .eml {EmlDocumentId}; job {JobId}",
                    submissionId, generation, input.CoreRecord.EntityLogicalName, input.CoreRecord.RecordId, resolved.EmlDocumentId, jobId);
                return FromEmailSubmissionOutcome.Accepted(new FromEmailReceipt(submissionId, jobId, result.TrackingNumber, resolved.EmlDocumentId));

            case FromEmailSubmitOutcome.AlreadyCreated:
                // EB-D-160 race: a concurrent retry of the same K created this attempt first. Answer it exactly as a replay.
                _logger.LogInformation("From-email submission {SubmissionId} (attempt {Generation}) was created concurrently; answering it as a replay",
                    submissionId, generation);
                var concurrent = await ReadAttemptAsync(submissionId, ct);
                if (concurrent.Refusal is { } concurrentFault)
                {
                    return AfterFiling(concurrentFault, result.Filed);
                }

                // The concurrency guard of K (the deterministic id and the allocator's AlreadyCreated): the creator, the status and the
                // details are re-checked on the row that won, so of two callers racing on one Failed attempt exactly one creates the new
                // generation and the other gets 409 (EB-D-165), and a same-caller retry with other details gets its 409 too. Step A may have
                // filed for this request before it lost the race: a refusal then says the filing is kept (review round 5).
                return AfterFiling(
                    concurrent.Row is { } row
                    && await AnswerExistingAsync(row, generation, k, callerId, invoiceSha, input, http, ct) is { } replayed
                        ? replayed
                        : NotQueued(),
                    result.Filed);

            case FromEmailSubmitOutcome.Refused when result.ErrorCode is not null:
                // The operation put the sentence in the detail of a refusal after its step A; the route adds filingKept.
                return FromEmailSubmissionOutcome.Refused(result.Status, result.ErrorCode, result.Detail ?? "The invoice was not submitted.")
                    with { FilingKept = result.Filed };

            default:
                return NotQueued();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // The firm
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<FromEmailSubmissionOutcome?> CheckFirmAsync(FromEmailSubmissionInput input, CancellationToken ct)
    {
        if (input.FirmId == Guid.Empty)
        {
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "A firm is required.");
        }

        switch (await _submissions.IsActiveFirmForFromEmailAsync(input.FirmId, ct))
        {
            case null:
                return FirmUnverifiable();
            case false:
                return FromEmailSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid,
                    "The firm was not found or is not active.");
        }

        return await _submissions.CheckFirmLinkAsync(input.CoreRecord, input.FirmId, ct) switch
        {
            InvoiceFirmLink.Linked => null,
            InvoiceFirmLink.NotLinked => FromEmailSubmissionOutcome.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmNotLinked,
                "The firm you chose is not linked to this record, so an invoice cannot be submitted for it."),
            _ => FirmUnverifiable(),
        };
    }

    /// <summary>A firm or firm-link read fault. Contract section 6 lists one code for a transient Dataverse fault on this route,
    /// <c>record_unavailable</c>; the internal intake's <c>firm_unverifiable</c> is not in the add-in's closed set, so it is not used here.</summary>
    private static FromEmailSubmissionOutcome FirmUnverifiable() =>
        FromEmailSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
            "The firm could not be checked right now. Try again shortly.");

    // ═══════════════════════════════════════════════════════════════════════════
    // The replay of K (contract section 5)
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record Attempt(Entity? Row, int Generation, FromEmailSubmissionOutcome? Refusal);

    /// <summary>
    /// The latest attempt of K: walks the generations by their deterministic ids while each one ended Failed (EB-D-160). <c>Row</c> null:
    /// K was never submitted. Otherwise the latest row and its generation; a later generation that does not exist yet is the next attempt.
    /// </summary>
    private async Task<Attempt> FindLatestAttemptAsync(string k, CancellationToken ct)
    {
        Entity? latest = null;
        var latestGeneration = 0;
        for (var generation = 1; generation <= FromEmailSubmissionKey.MaxGeneration; generation++)
        {
            var read = await ReadAttemptAsync(FromEmailSubmissionKey.SubmissionIdFor(k, generation), ct);
            if (read.Refusal is not null)
            {
                return read with { Generation = generation };
            }

            if (read.Row is null)
            {
                break;
            }

            latest = read.Row;
            latestGeneration = generation;
            if (StatusOf(latest) != InvoiceSubmissionStatus.Failed)
            {
                break;
            }
        }

        return new Attempt(latest, latestGeneration, null);
    }

    private async Task<Attempt> ReadAttemptAsync(Guid submissionId, CancellationToken ct)
    {
        try
        {
            return new Attempt(await _entities.RetrieveAsync(InvoiceSubmissionService.SubmissionEntity, submissionId, AttemptColumns, ct), 0, null);
        }
        catch (Exception ex) when (InvoiceSubmissionServiceNotFound(ex))
        {
            return new Attempt(null, 0, null);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission refused before any write: attempt {SubmissionId} could not be read", submissionId);
            return new Attempt(null, 0, Unavailable("Invoice submissions could not be read right now. Nothing was saved; try again shortly."));
        }
    }

    /// <summary>
    /// The answer to an existing attempt of K, or null when a NEW attempt follows: the latest one ended Failed, whoever made it (EB-D-160;
    /// EB-D-165 for another caller's). The new attempt is this caller's and runs the whole fresh path (the firm and its link above, the
    /// filing, this caller's own save with its narrowing in the one create); nothing of the failed attempt's rows is reused (the earlier
    /// <c>.eml</c> candidates are this caller's own only). Otherwise: another caller's live attempt is 409 document_already_submitted with
    /// its own text (OP-5, 095 review F1-2); Rejected is 409 submission_already_ended (D-139); another firm or number, or another Invoice
    /// part, is 409 document_already_submitted (contract section 5); else the 202 replay, re-queueing a Received one.
    /// </summary>
    private async Task<FromEmailSubmissionOutcome?> AnswerExistingAsync(
        Entity row, int generation, string k, Guid callerId, string invoiceSha, FromEmailSubmissionInput input, HttpContext http, CancellationToken ct)
    {
        var status = StatusOf(row);
        if (status == InvoiceSubmissionStatus.Failed)
        {
            // Review round 5 (pass-2 F1-1): no new attempt while the failed one's undo is unfinished (its invoice still linked).
            if (SubmissionCommitGate.UndoUnfinished(
                    status, row.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode),
                    row.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.SubmissionInvoice)?.Id))
            {
                _logger.LogInformation("From-email submission {SubmissionId} (attempt {Generation}) ended Failed and its undo is unfinished; refused (503)",
                    row.Id, generation);
                return FromEmailSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.EarlierAttemptBeingRemoved,
                    InvoiceSubmissionService.EarlierAttemptBeingRemovedDetail);
            }

            return null; // EB-D-160 / EB-D-165: a new attempt (generation + 1), created by this caller
        }

        var creator = row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id;
        if (creator != callerId)
        {
            // OP-5: K has no caller, so this is the same submission from someone else; it never becomes a second invoice for one email.
            _logger.LogInformation("From-email submission {SubmissionId}: K was already submitted by another person; refused (409)", row.Id);
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted,
                "This email was already submitted as an invoice for this record by someone else. Nothing was saved.");
        }

        if (status == InvoiceSubmissionStatus.Rejected)
        {
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.SubmissionAlreadyEnded,
                "This email was already submitted as an invoice and that submission was rejected. Nothing was saved.");
        }

        // Contract section 5 (095 review F2): the same email, record and marking with another firm or firm invoice number is the same
        // submission with other details, never a second invoice.
        if (!FromEmailSubmissionKey.SameFirmDetails(
                row.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn)?.Id,
                row.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn), input))
        {
            _logger.LogInformation("From-email submission {SubmissionId}: the firm or firm invoice number differs from the live attempt; refused (409)", row.Id);
            return DocumentAlreadySubmitted();
        }

        // Task 092: the submission row names no document; the attempt's source (.eml and Invoice part) is what the operation recorded.
        FromEmailAttemptSource? source;
        try
        {
            source = await _operation.ReadAttemptSourceAsync(row.Id, ct);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission {SubmissionId}: its source could not be read", row.Id);
            return Unavailable("Invoice submissions could not be read right now. Nothing was saved; try again shortly.");
        }

        if (source is not null && !string.Equals(source.InvoiceSha256, invoiceSha, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("From-email submission {SubmissionId}: the Invoice part differs from the earlier attempt; refused (409)", row.Id);
            return DocumentAlreadySubmitted();
        }

        var tracking = row.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn);
        if (string.IsNullOrEmpty(tracking) || source is not { SourceEmlDocumentId: var emlId } || emlId == Guid.Empty || status is null)
        {
            _logger.LogWarning("From-email submission {SubmissionId}: the earlier attempt has no tracking number, source or status", row.Id);
            return NotQueued();
        }

        var jobId = FromEmailSubmissionKey.JobIdFor(k, generation);
        if (status == InvoiceSubmissionStatus.Received
            && !await _operation.RequeueAsync(row.Id, jobId, http.TraceIdentifier, CancellationToken.None))
        {
            return NotQueued();
        }

        _logger.LogInformation("From-email submission {SubmissionId} (attempt {Generation}) replayed ({Status})", row.Id, generation, status);
        return FromEmailSubmissionOutcome.Accepted(new FromEmailReceipt(row.Id, jobId, tracking, emlId));
    }

    private static InvoiceSubmissionStatus? StatusOf(Entity row) =>
        row.GetAttributeValue<OptionSetValue>("sprk_processingstatus")?.Value is { } value && Enum.IsDefined(typeof(InvoiceSubmissionStatus), value)
            ? (InvoiceSubmissionStatus)value
            : null;

    // ═══════════════════════════════════════════════════════════════════════════
    // The communication and the .eml (model §9.1 step 1a)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<FromEmailSubmissionOutcome?> CheckCommunicationFilingAsync(Guid communicationId, InvoiceCoreRecord core, CancellationToken ct)
    {
        IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)> filed;
        try
        {
            filed = await _submissions.ResolveCommunicationFilingAsync(communicationId, ct);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission refused: the filing of communication {CommunicationId} could not be read", communicationId);
            return Unavailable("The record the email is filed under could not be read right now. Nothing was saved; try again shortly.");
        }

        if (filed.Any(f => f.Kind != core.Kind || f.Id != core.RecordId))
        {
            // The other record is never named: the caller was authorized against this record, not that one.
            _logger.LogInformation("From-email submission refused: communication {CommunicationId} is filed under another core record", communicationId);
            return FromEmailSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
                "The email is filed under a different record than the one named, so it cannot be submitted for this record. Nothing was saved.");
        }

        return null;
    }

    private sealed record ResolvedEml(Guid EmlDocumentId, Guid CommunicationId, IReadOnlyList<FromEmailMarkedPart> Parts, FromEmailSubmissionOutcome? Refusal)
    {
        public static ResolvedEml Refused(FromEmailSubmissionOutcome refusal) => new(Guid.Empty, Guid.Empty, Array.Empty<FromEmailMarkedPart>(), refusal);
    }

    /// <summary>
    /// The <c>.eml</c> this submission names, from the save's outcome: the fresh one this request created, or, on the caller's own earlier save
    /// (the save's duplicate), the caller's <c>.eml</c> of the email's communication. Never from a returned job result.
    /// </summary>
    private async Task<ResolvedEml> ResolveEmlAsync(
        SubmissionEmailSaveOutcome save, FromEmailSubmissionInput input, Guid callerId, IReadOnlyList<FromEmailResolvedMark> saved, CancellationToken ct)
    {
        switch (save.Refusal)
        {
            case SubmissionEmailSaveRefusal.NoCommunication:
                return ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.NoCommunication));
            case SubmissionEmailSaveRefusal.AttachmentNotPreserved:
                return ResolvedEml.Refused(FromEmailSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity,
                    FromEmailSubmissionErrors.AttachmentNotPreserved,
                    "A marked file could not be kept unchanged in the saved email, so nothing was saved. Save the invoice PDF and submit it through the invoice upload instead."));
            case SubmissionEmailSaveRefusal.CommunicationUnavailable:
                return ResolvedEml.Refused(Unavailable("The email's record could not be read right now. Nothing was saved; try again shortly."));
        }

        if (save.Response is not { Success: true } response)
        {
            return ResolvedEml.Refused(FromEmailSubmissionOutcome.Office(save.Response?.Error ?? new SaveError
            {
                Code = Sprk.Bff.Api.Api.Office.Errors.OfficeErrorCodes.InternalError,
                Message = "The email could not be saved. Try again.",
                Retryable = true,
            }));
        }

        if (save.ContentDuplicate)
        {
            // The content-dedup suppress resolved the .eml's bytes to another document: which one is this email's is not certain.
            _logger.LogWarning("From-email submission refused: the .eml resolved to an existing document by its content");
            return ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EmlAmbiguous));
        }

        if (!response.Duplicate)
        {
            if (save.EmlDocumentId is not { } fresh || save.CommunicationId is not { } communication || save.EmlParts is null)
            {
                _logger.LogError("From-email submission: the save succeeded without naming its .eml, its communication or its parts");
                return ResolvedEml.Refused(NotQueued());
            }

            var parts = MatchParts(saved, save.EmlParts);
            if (parts is null)
            {
                // The .eml was built from the same entries the marks were checked against, so this is a defect, not a user error.
                _logger.LogError("From-email submission: .eml {EmlDocumentId} does not carry every marked part", fresh);
                return ResolvedEml.Refused(NotQueued());
            }

            return new ResolvedEml(fresh, communication, parts, null);
        }

        return await ResolveEarlierEmlAsync(input, callerId, saved, ct);
    }

    /// <summary>
    /// Step 1a's duplicate path: the caller's earlier save of this email to this record. Candidates are the Documents with the email's
    /// communication AND <c>sprk_isemailarchive</c>, filed under the target with no parent, created by the caller only; several are narrowed
    /// by the marked parts' SHA-256. None, or still several, is the permanent <c>eml_ambiguous</c>; an earlier <c>.eml</c> that is not
    /// linked (or still carries task 130's provisional Restricted) is <c>earlier_save_unlinked</c>.
    /// </summary>
    private async Task<ResolvedEml> ResolveEarlierEmlAsync(
        FromEmailSubmissionInput input, Guid callerId, IReadOnlyList<FromEmailResolvedMark> saved, CancellationToken ct)
    {
        Guid communicationId;
        try
        {
            var communication = await _communications.GetCommunicationByInternetMessageIdAsync(input.MessageId, ct);
            if (communication is null || communication.Id == Guid.Empty)
            {
                return await HasUnlinkedEarlierSaveAsync(input, callerId, ct) switch
                {
                    true => ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EarlierSaveUnlinked)),
                    false => ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.NoCommunication)),
                    null => ResolvedEml.Refused(Unavailable("The earlier save of this email could not be read right now. Try again shortly.")),
                };
            }

            communicationId = communication.Id;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission: the email's communication could not be read on the duplicate path");
            return ResolvedEml.Refused(Unavailable("The email's record could not be read right now. Try again shortly."));
        }

        if (await CheckCommunicationFilingAsync(communicationId, input.CoreRecord, ct) is { } filingRefusal)
        {
            return ResolvedEml.Refused(filingRefusal);
        }

        EntityCollection candidates;
        try
        {
            var query = CallerEmlQuery(input.CoreRecord, callerId);
            query.Criteria.AddCondition(DocumentRelatedCommunication, ConditionOperator.Equal, communicationId);
            candidates = await _entities.RetrieveMultipleAsync(query, ct);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission: the earlier .eml candidates of communication {CommunicationId} could not be read", communicationId);
            return ResolvedEml.Refused(Unavailable("The earlier save of this email could not be read right now. Try again shortly."));
        }

        if (candidates.Entities.Count == 0)
        {
            return await HasUnlinkedEarlierSaveAsync(input, callerId, ct) switch
            {
                true => ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EarlierSaveUnlinked)),
                false => ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EmlAmbiguous)),
                null => ResolvedEml.Refused(Unavailable("The earlier save of this email could not be read right now. Try again shortly.")),
            };
        }

        var matches = new List<(Guid Id, IReadOnlyList<FromEmailMarkedPart> Parts)>();
        foreach (var candidate in candidates.Entities)
        {
            // Task 130's save-time Restricted that is still provisional: the earlier save could not identify its email.
            if (AccessInheritance.Parse(candidate.GetAttributeValue<string>(AccessInheritance.Column)).Value is { OwnProvisional: true })
            {
                _logger.LogInformation("From-email submission refused: earlier .eml {DocumentId} still carries its provisional Restricted", candidate.Id);
                return ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EarlierSaveUnlinked));
            }

            var driveId = candidate.GetAttributeValue<string>("sprk_graphdriveid");
            var itemId = candidate.GetAttributeValue<string>("sprk_graphitemid");
            if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
            {
                continue; // no file yet: cannot carry the marked parts
            }

            IReadOnlyList<EmlPart> parts;
            try
            {
                await using var stream = await _spe.DownloadFileAsync(driveId, itemId, ct);
                if (stream is null)
                {
                    continue;
                }

                parts = EmlMimeParts.Read(stream, ct);
            }
            catch (FormatException)
            {
                continue; // not a MIME message: not this email's .eml
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                _logger.LogWarning(ex, "From-email submission: earlier .eml {DocumentId} could not be read from storage", candidate.Id);
                return ResolvedEml.Refused(Unavailable("The earlier save of this email could not be read right now. Try again shortly."));
            }

            if (MatchParts(saved, parts) is { } matched)
            {
                matches.Add((candidate.Id, matched));
            }
        }

        if (matches.Count != 1)
        {
            _logger.LogInformation("From-email submission refused: {Count} of the caller's earlier .eml candidates carry the marked parts", matches.Count);
            return ResolvedEml.Refused(Unidentifiable(FromEmailUnidentifiableReasons.EmlAmbiguous));
        }

        return new ResolvedEml(matches[0].Id, communicationId, matches[0].Parts, null);
    }

    /// <summary>Whether the caller has an earlier, UNLINKED <c>.eml</c> of this email under the target (null: the read faulted).</summary>
    private async Task<bool?> HasUnlinkedEarlierSaveAsync(FromEmailSubmissionInput input, Guid callerId, CancellationToken ct)
    {
        try
        {
            var query = CallerEmlQuery(input.CoreRecord, callerId);
            query.TopCount = 1;
            query.Criteria.AddCondition(DocumentEmailMessageId, ConditionOperator.Equal, input.MessageId);
            query.Criteria.AddCondition(DocumentRelatedCommunication, ConditionOperator.Null);
            return (await _entities.RetrieveMultipleAsync(query, ct)).Entities.Count > 0;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "From-email submission: the caller's unlinked earlier .eml could not be looked up");
            return null;
        }
    }

    /// <summary>The caller's active email archives filed directly under <paramref name="core"/>, with no parent document.</summary>
    private static QueryExpression CallerEmlQuery(InvoiceCoreRecord core, Guid callerId)
    {
        var query = new QueryExpression(DocumentEntity)
        {
            ColumnSet = new ColumnSet("sprk_graphdriveid", "sprk_graphitemid", AccessInheritance.Column),
            TopCount = 20,
        };
        query.Criteria.AddCondition(DocumentIsEmailArchive, ConditionOperator.Equal, true);
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition(RecordCreatorPerson.Column, ConditionOperator.Equal, callerId);
        query.Criteria.AddCondition(core.EntityLogicalName, ConditionOperator.Equal, core.RecordId);
        query.Criteria.AddCondition(DocumentParentDocument, ConditionOperator.Null);
        return query;
    }

    /// <summary>
    /// Each Invoice and Supporting mark matched to an attachment part of the <c>.eml</c> by its bytes' SHA-256 (the lowest unused ordinal);
    /// null when any marked part is not in the <c>.eml</c>. The part's own stored name is what the submission records.
    /// </summary>
    internal static IReadOnlyList<FromEmailMarkedPart>? MatchParts(IReadOnlyList<FromEmailResolvedMark> saved, IReadOnlyList<EmlPart> parts)
    {
        var used = new HashSet<int>();
        var matched = new List<FromEmailMarkedPart>(saved.Count);
        foreach (var mark in saved)
        {
            var part = parts
                .Where(p => !used.Contains(p.Ordinal) && string.Equals(p.Sha256, mark.Sha256, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Ordinal)
                .FirstOrDefault();
            if (part is null)
            {
                return null;
            }

            used.Add(part.Ordinal);
            matched.Add(new FromEmailMarkedPart(part.Ordinal, part.FileName ?? mark.Entry.FileName, part.Sha256, mark.Mark.Mark!));
        }

        return matched;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Refusals
    // ═══════════════════════════════════════════════════════════════════════════

    private static FromEmailSubmissionOutcome Unidentifiable(string reason) =>
        FromEmailSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, FromEmailSubmissionErrors.EmailUnidentifiable,
            "This email could not be identified, so nothing was saved. Save the invoice PDF and submit it through the invoice upload instead.",
            new Dictionary<string, object?> { ["reason"] = reason });

    private static FromEmailSubmissionOutcome Unavailable(string detail) =>
        FromEmailSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable, detail);

    private static FromEmailSubmissionOutcome DocumentAlreadySubmitted() =>
        FromEmailSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted,
            "This email was already submitted as an invoice with other details. Nothing was saved.");

    private static FromEmailSubmissionOutcome NotQueued() =>
        FromEmailSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed,
            "The invoice could not be submitted right now. Try again shortly.");

    /// <summary>
    /// Review round 5: a refusal answered after step A filed the communication for this request keeps the filing and says so (contract
    /// section 6, "After step A"), as <see cref="InvoiceSubmissionService.AfterFiling"/> does for the source-document route, and the route adds
    /// <c>filingKept: true</c> (changed 2026-10-29). Every non-receipt refusal gets it, whatever its code field: an <c>errorCode</c> refusal
    /// gets the sentence here; an Office pass-through (no <c>errorCode</c>, a body this service does not build) is flagged and the route extends
    /// its detail. A receipt or a request that filed nothing is unchanged.
    /// </summary>
    internal static FromEmailSubmissionOutcome AfterFiling(FromEmailSubmissionOutcome outcome, bool filed) =>
        !filed || outcome.Receipt is not null
            ? outcome
            : outcome.ErrorCode is null
                ? outcome with { FilingKept = true }
                : outcome with { Detail = InvoiceSubmissionService.AfterFilingDetail(outcome.Detail), FilingKept = true };

    private static bool IsCallerCancellation(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;

    private static bool InvoiceSubmissionServiceNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }
}
