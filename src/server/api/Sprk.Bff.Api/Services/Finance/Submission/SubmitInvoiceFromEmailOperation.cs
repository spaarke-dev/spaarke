// ebilling-r1 task 094: the Submit Invoice operation for a from-email source (procedure-and-file-model 9.1; the seam task 095 pinned in
// ISubmitInvoiceFromEmail; wire contract notes/095-from-email-contract.md).
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing - InvoiceSubmissionService runs the intake for an upload (SubmitAsync) and for a source document (SubmitExistingDocumentAsync);
//       EmlMarkedPartReader reads the marked parts out of a saved .eml; StoredFileService stages bytes; the allocator creates the row; the
//       job, the persister and the commit gate commit and check a staged source. Nothing stages a .eml's marked parts with their source.
//   (2) Extension - it is the implementation 095's seam was built for (it replaces SubmitInvoiceFromEmailNotYetAvailable, so it adds no
//       interface): SubmitAsync would treat the .eml as an upload with no source and SubmitExistingDocumentAsync would copy the .eml itself
//       as the invoice PDF (ISubmitInvoiceFromEmail remarks). It reuses the intake's checks (DecideForFromEmailAsync), its row shape and job.
//   (3) Cost of doing nothing - the from-email route answers 503 ebilling.submission.failed to every request (the stand-in), so an invoice
//       received by email cannot be submitted from Outlook at all (word-add-in-r1).
//
// Placement (bff-extensions.md): Services/Finance/Submission beside the intake it composes; in-request work (no new background work, no
// package, no endpoint: the route is 095's). DI: one scoped registration in FinanceModule replacing the stand-in.

using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The Submit Invoice operation for a from-email source (<see cref="ISubmitInvoiceFromEmail"/>; model 9.1 steps 1 to 4 at intake; the job
/// and the commit gate run steps 3 to 6). In order, nothing written before the first stage:
/// <list type="number">
/// <item>Step 1, read-only: the caller's own Read on the <c>.eml</c> and on its communication, and the <c>.eml</c> must be that
/// communication's (<see cref="InvoiceSubmissionEndpoints.PrepareSourceAsync"/>, the confirm and from-document routes' step 1).</item>
/// <item>The intake's checks (<see cref="InvoiceSubmissionService.DecideForFromEmailAsync"/>): the number, the firm active and linked
/// (D-106), the core record's fields and the owner, with the caller's own systemuser as the creator person on EVERY generation (OP-5,
/// EB-D-165: whoever made an earlier attempt).</item>
/// <item>Step A, after those refusals (on the fresh path and on the duplicate path alike): an unfiled communication is filed under the
/// record as the caller; a filed one is left as it is.</item>
/// <item>The marked parts, read out of the saved <c>.eml</c> (<see cref="EmlMarkedPartReader"/>: ordinal, name and SHA-256 must match;
/// its <c>versionnumber</c> is the source version), each staged under its Stored File id with its source recorded (the <c>.eml</c>, its
/// communication, the part's identity, the version, the SHA-256; the Invoice part also lists the Supporting parts). Firm-visible No.</item>
/// <item>The submission, created with exactly <see cref="FromEmailSubmitInput.SubmissionId"/> by the allocator (Channel Internal, NO
/// <c>sprk_document</c>); its <see cref="TrackingNumberOutcome.AlreadyCreated"/> is the race answer.</item>
/// <item>The job <see cref="FromEmailSubmitInput.JobId"/>. The persister builds every part's row in the commit (the only writer of an
/// Invoice PDF row), runs step 3 on the <c>.eml</c> and guards it at the version that check read.</item>
/// </list>
/// <para><b>No compensation.</b> The ids are deterministic (K and the generation), so two concurrent requests of one attempt stage the same
/// blobs and race for the same row: a loser that deleted "its" blobs or row would delete the winner's. A stage left by a refused request is
/// reused by the retry (same bytes) or found by the file store's detector; a row whose job could not be queued is re-queued by the retry's
/// replay (contract section 5).</para>
/// <para><b>Logs</b> carry ids, ordinals and codes only: never a file name, a subject or a Message-ID.</para>
/// </summary>
internal class SubmitInvoiceFromEmailOperation : ISubmitInvoiceFromEmail
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private readonly InvoiceSubmissionService _submissions;
    private readonly ITrackingNumberAllocator _allocator;
    private readonly StoredFileService _files;
    private readonly JobSubmissionService _jobs;
    private readonly EmlMarkedPartReader _reader;
    private readonly ILogger<SubmitInvoiceFromEmailOperation> _logger;

    public SubmitInvoiceFromEmailOperation(
        InvoiceSubmissionService submissions,
        ITrackingNumberAllocator allocator,
        StoredFileService files,
        JobSubmissionService jobs,
        EmlMarkedPartReader reader,
        ILogger<SubmitInvoiceFromEmailOperation> logger)
    {
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<FromEmailSubmitResult> SubmitAsync(FromEmailSubmitInput input, CancellationToken ct)
    {
        Validate(input);
        var submissionId = input.SubmissionId;

        // ── 1. Step 1, read-only: the caller's Read on the .eml and its communication ──
        var prepared = await PrepareSourceAsync(input, ct);
        if (prepared.Refusal is { } stepOne)
        {
            return Answer(stepOne, filed: false);
        }

        // ── 2. The intake's checks, the caller as creator person (every generation) ──
        var request = new InvoiceSubmissionRequest
        {
            FirmId = input.FirmId,
            CoreRecord = input.CoreRecord,
            Channel = InvoiceSubmissionChannel.Internal,
            FirmInvoiceNumber = input.FirmInvoiceNumber,
            PoNumber = input.PoNumber,
            CallerObjectId = input.CallerObjectId,
            CorrelationId = input.CorrelationId,
        };
        var (refusal, row, resolverFields) = await _submissions.DecideForFromEmailAsync(request, input.CallerSystemUserId, ct);
        if (refusal is not null)
        {
            return Answer(refusal, filed: false, input);
        }

        // ── 3. Step A, after the read-only refusals ──
        var filed = false;
        if (prepared.FileSource is { } fileSource)
        {
            try
            {
                filed = await fileSource(ct);
            }
            catch (InvoiceSubmissionEndpoints.SourceFilingRefusedException ex)
            {
                return Answer(ex.Answer, filed: false);
            }
        }

        // ── 4. The marked parts, out of the saved .eml ──
        var read = await _reader.ReadAsync(submissionId, input.SourceEmlDocumentId, input.MarkedParts, ct);
        switch (read.Outcome)
        {
            case EmlMarkedPartReadOutcome.Read when read.RowVersion is not null:
                break;
            case EmlMarkedPartReadOutcome.Transient:
                return Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                    "The saved email could not be read right now. Nothing was submitted; try again shortly.", filed);
            default:
                _logger.LogWarning("From-email submission {SubmissionId}: the marked parts of .eml {EmlDocumentId} could not be read ({Outcome})",
                    submissionId, input.SourceEmlDocumentId, read.Outcome);
                return Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed,
                    "The invoice could not be submitted right now. Nothing was submitted; try again shortly.", filed);
        }

        // From here on no write observes the caller's token (InvoiceSubmissionService's rule): a write that started finishes.
        ct.ThrowIfCancellationRequested();
        var write = CancellationToken.None;
        var version = read.RowVersion!.Value;

        // ── 5. Stage every part with its source; the Invoice part lists the Supporting parts ──
        var supporting = input.MarkedParts.Where(p => p.Mark == FromEmailMarks.Supporting).Select(p => p.Ordinal).ToList();
        foreach (var part in read.Parts)
        {
            var (spec, manifest) = SpecOf(input, part.Part, version, supporting);
            StageResult stage;
            try
            {
                await using var content = new MemoryStream(part.Content, writable: false);
                stage = await _files.StageAsync(spec, content, manifest, write);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "From-email submission {SubmissionId}: part {Ordinal} could not be staged", submissionId, part.Part.Ordinal);
                return NotQueued(filed);
            }

            if (!stage.IsStaged)
            {
                _logger.LogWarning("From-email submission {SubmissionId}: the file store refused part {Ordinal} ({Code})",
                    submissionId, part.Part.Ordinal, stage.RefusalCode);
                return stage.RefusalCode switch
                {
                    StoredFileRefusal.NotConfigured => Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.StorageNotConfigured,
                        "Invoice file storage is not configured, so the invoice cannot be stored. Nothing was submitted.", filed),
                    // Other bytes are staged under this attempt's id: the same email, record and marking was submitted with another
                    // Invoice or Supporting file (contract section 5, "other details differ").
                    StoredFileRefusal.ContentConflict => Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted,
                        "This email was already submitted as an invoice with other details. Nothing was saved.", filed),
                    _ => NotQueued(filed),
                };
            }
        }

        // ── 6. The submission: exactly this id, Channel Internal, no document (092) ──
        foreach (var (column, value) in resolverFields!.ToFields())
        {
            row![column] = value;
        }

        row!["sprk_name"] = input.FirmInvoiceNumber;
        row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received);
        row["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal);
        row["sprk_receivedon"] = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(input.PoNumber))
        {
            row["sprk_ponumber"] = input.PoNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(input.CorrelationId))
        {
            row["sprk_correlationid"] = input.CorrelationId.Length > 100 ? input.CorrelationId[..100] : input.CorrelationId;
        }

        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } guard)
        {
            throw new InvalidOperationException($"The submission payload failed the invoice-family guard ({guard.RefusalCode}).");
        }

        TrackingNumberResult allocation;
        try
        {
            allocation = await _allocator.CreateSubmissionAsync(submissionId, input.FirmId, input.FirmInvoiceNumber, row.Attributes.ToDictionary(), write);
        }
        catch (Exception ex)
        {
            // The create may have committed (a lost reply): the retry's replay finds the row and queues its job.
            _logger.LogError(ex, "From-email submission {SubmissionId}: the create faulted; a retry completes it", submissionId);
            return NotQueued(filed);
        }

        switch (allocation.Outcome)
        {
            case TrackingNumberOutcome.AlreadyCreated:
                _logger.LogInformation("From-email submission {SubmissionId} already existed (a concurrent request of the same attempt)", submissionId);
                return FromEmailSubmitResult.AlreadyCreated();
            case TrackingNumberOutcome.Refused:
                _logger.LogWarning("From-email submission {SubmissionId}: no tracking number could be allocated ({Reason})", submissionId, allocation.Reason);
                return Refused(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
                    "A tracking number could not be assigned right now, so nothing was submitted. Try again shortly.", filed);
        }

        // ── 7. The job ──
        try
        {
            await _jobs.SubmitJobAsync(InvoiceSubmissionService.BuildJob(input.JobId, submissionId, input.CorrelationId), write);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "From-email submission {SubmissionId}: its job {JobId} could not be queued; a retry queues it", submissionId, input.JobId);
            return NotQueued(filed);
        }

        _logger.LogInformation(
            "From-email submission {SubmissionId} (attempt {Generation}) created from .eml {EmlDocumentId} with {PartCount} parts; job {JobId} queued",
            submissionId, input.Generation, input.SourceEmlDocumentId, read.Parts.Count, input.JobId);
        return FromEmailSubmitResult.Received(allocation.TrackingNumber!);
    }

    /// <inheritdoc />
    public async Task<bool> RequeueAsync(Guid submissionId, Guid jobId, string? correlationId, CancellationToken ct)
    {
        try
        {
            await _jobs.SubmitJobAsync(InvoiceSubmissionService.BuildJob(jobId, submissionId, correlationId), CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "From-email submission {SubmissionId}: its job {JobId} could not be re-queued", submissionId, jobId);
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>From the Invoice part's stage (its blob stays after the commit: the row names the same blob): the <c>.eml</c> it was read out
    /// of and its SHA-256. An upload's stage records no source, so it answers null.</remarks>
    public async Task<FromEmailAttemptSource?> ReadAttemptSourceAsync(Guid submissionId, CancellationToken ct)
    {
        var staged = await _files.ReadStagedSourceAsync(
            StoredFilePurpose.InvoicePdf, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId)),
            StoredFileIds.ForUpload(submissionId), ct);
        return staged is { SourceDocumentId: { } eml } ? new FromEmailAttemptSource(eml, staged.Blob.Sha256) : null;
    }

    /// <summary>
    /// Step 1 (<see cref="InvoiceSubmissionEndpoints.PrepareSourceAsync"/>) as the request's caller: its refusal, or step A (null when the
    /// communication needs no filing check). A seam for the tests, which drive the intake without an HTTP pipeline.
    /// </summary>
    internal virtual async Task<(IResult? Refusal, Func<CancellationToken, Task<bool>>? FileSource)> PrepareSourceAsync(
        FromEmailSubmitInput input, CancellationToken ct)
    {
        var prepared = await InvoiceSubmissionEndpoints.PrepareSourceAsync(
            input.SourceEmlDocumentId, input.CommunicationId, input.CoreRecord,
            input.Http ?? throw new ArgumentException("The from-email operation runs as the request's caller.", nameof(input)),
            documentReadChecked: false, ct);
        return (prepared.Refusal, prepared.Hook);
    }

    /// <summary>The spec (and, for the Invoice part, the manifest) of one marked part.</summary>
    internal static (StoredFileRowSpec Spec, StagedSourceManifest? Manifest) SpecOf(
        FromEmailSubmitInput input, FromEmailMarkedPart part, long version, IReadOnlyList<int> supportingOrdinals)
    {
        var identity = InvoiceSubmissionPersister.EmlPartIdentity(part.Ordinal);
        var extension = Path.GetExtension(part.FileName);
        var safeExtension = !string.IsNullOrEmpty(extension) && StoredFileService.IsExtension(extension) ? extension.ToLowerInvariant() : null;
        if (part.Mark == FromEmailMarks.Invoice)
        {
            var spec = InvoiceSubmissionService.UploadFileSpec(input.SubmissionId, part.FileName, InvoiceSubmissionChannel.Internal) with
            {
                FirmVisible = false,
                SourceDocumentId = input.SourceEmlDocumentId,
                SourceCommunicationId = input.CommunicationId,
                SourceAttachmentIdentity = identity,
                SourceVersion = version,
                SourceSha256 = part.Sha256.ToLowerInvariant(),
            };
            return (spec, new StagedSourceManifest(supportingOrdinals, Extension: null));
        }

        return (new StoredFileRowSpec
        {
            FileId = part.StoredFileId,
            Purpose = StoredFilePurpose.InvoiceSupportingFile,
            Parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(input.SubmissionId)),
            Name = identity + (safeExtension ?? string.Empty),
            ContentType = ContentTypes.TryGetContentType(part.FileName, out var type) ? type : "application/octet-stream",
            FirmVisible = false,
            SourceDocumentId = input.SourceEmlDocumentId,
            SourceCommunicationId = input.CommunicationId,
            SourceAttachmentIdentity = identity,
            SourceVersion = version,
            SourceSha256 = part.Sha256.ToLowerInvariant(),
            SubmissionId = input.SubmissionId,
        }, new StagedSourceManifest([], safeExtension));
    }

    /// <summary>A wiring defect (never a user error): 095 resolves every one of these before it calls.</summary>
    private static void Validate(FromEmailSubmitInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.SubmissionId == Guid.Empty || input.JobId == Guid.Empty || input.SourceEmlDocumentId == Guid.Empty
            || input.CommunicationId == Guid.Empty || input.CallerSystemUserId == Guid.Empty || input.Generation < 1
            || string.IsNullOrWhiteSpace(input.FirmInvoiceNumber) || input.Mode != FromEmailModes.New
            || input.MarkedParts.Count(p => p.Mark == FromEmailMarks.Invoice) != 1
            || input.MarkedParts.Any(p => p.Mark is not (FromEmailMarks.Invoice or FromEmailMarks.Supporting)
                || p.StoredFileId != FromEmailSubmissionKey.StoredFileIdFor(input.SubmissionId, p))
            || input.MarkedParts.Select(p => p.Ordinal).Distinct().Count() != input.MarkedParts.Count)
        {
            throw new ArgumentException("The from-email submission input is incomplete or inconsistent.", nameof(input));
        }
    }

    // ── Answers ──

    private static FromEmailSubmitResult Refused(int status, string code, string detail, bool filed) =>
        FromEmailSubmitResult.Refused(status, code, filed ? AfterFiling(detail) : detail);

    private static FromEmailSubmitResult NotQueued(bool filed) =>
        Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed, "The invoice could not be submitted right now. Try again shortly.", filed);

    private static string AfterFiling(string detail) =>
        InvoiceSubmissionService.AfterFiling(InvoiceSubmissionOutcome.Refused(0, string.Empty, detail), filed: true).Detail ?? detail;

    /// <summary>The intake's refusal; an owner refusal is the route's own ProblemDetails (contract section 6: RecordOwnerRefused reasonCode).</summary>
    private static FromEmailSubmitResult Answer(InvoiceSubmissionOutcome outcome, bool filed, FromEmailSubmitInput input)
    {
        if (outcome.Kind == InvoiceSubmissionOutcomeKind.OwnerRefused)
        {
            throw new FromEmailRouteAnswerException(ProblemDetailsHelper.RecordOwnerRefused(
                outcome.OwnerRefusal!, "invoice submission", input.Http?.TraceIdentifier ?? input.CorrelationId ?? string.Empty));
        }

        return Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail ?? "The invoice was not submitted.", filed);
    }

    /// <summary>
    /// A step-1 or step-A answer: a submission refusal (it carries <c>errorCode</c>) becomes this operation's refusal; any other answer (the
    /// communication re-file route's own) is returned by the route as it is.
    /// </summary>
    private static FromEmailSubmitResult Answer(IResult answer, bool filed)
    {
        if (answer is ProblemHttpResult { ProblemDetails: var problem }
            && problem.Extensions.TryGetValue("errorCode", out var code) && code is string errorCode && problem.Status is { } status)
        {
            return Refused(status, errorCode, problem.Detail ?? "The invoice was not submitted.", filed);
        }

        throw new FromEmailRouteAnswerException(answer);
    }
}

/// <summary>
/// ebilling-r1 task 094: an answer of the from-email operation that is another route's own (the communication re-file's, step A; the owner
/// refusal's ProblemDetails). The from-email route returns <see cref="Answer"/> as it is; nothing of the submission was written.
/// </summary>
internal sealed class FromEmailRouteAnswerException(IResult answer) : Exception("The from-email submission was answered by another route's rule.")
{
    internal IResult Answer { get; } = answer;
}
