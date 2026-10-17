using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The one server-side invoice intake (ebilling-r1 FR-17, Processing Pipeline step 0, ADR-002): validates the request, stores the PDF in the core
/// record's container, creates the <c>sprk_document</c> (type Invoice) and the <c>sprk_invoicesubmission</c> (Received, with its tracking number),
/// and enqueues <see cref="JobType"/>. Both entry points call it: the external SPA route and the internal route (D-24: the same checks).
/// </summary>
/// <remarks>
/// <para><b>Authorization is the routes' job, not this service's.</b> Each route's endpoint filter decides the caller, the firm and the core record
/// before this runs (ADR-008); the request carries the validated firm and core record. This service validates the payload and writes.</para>
/// <para><b>Order (copied from the external upload, <c>ExternalProjectDataEndpoints.UploadDocumentForParent</c>):</b> every check and read first
/// (number, file, firm, core-record fields, owner, container), then the bytes, then the rows, then the job. A failure after the upload is
/// compensated in reverse order with its own token, so it leaves no Received submission behind (acceptance criterion 7). A compensation that
/// itself fails is logged and never masks the original failure.</para>
/// <para><b>The writes ignore the caller's token.</b> From the upload to the enqueue every call runs under <see cref="CancellationToken.None"/>:
/// Graph, Dataverse and Service Bus finish a request they received even when the caller abandons it, and the abandoned call's reply (the id
/// compensation needs) is lost. A client that disconnects mid-write therefore gets a complete submission it never hears about, not orphans.
/// A caller already gone before the first write writes nothing.</para>
/// <para><b>Only creator of submissions.</b> The row is created by <see cref="ITrackingNumberAllocator"/> with its number already set (D-87); the
/// submission id is minted here once per request and is also the job's idempotency key. This service never creates <c>sprk_invoice</c> (task 049
/// does).</para>
/// <para><b>Logging (ADR-015).</b> Ids and outcomes only; the firm invoice number, the tracking number and the file name are business data and are
/// not logged above Debug.</para>
/// </remarks>
public class InvoiceSubmissionService
{
    /// <summary>The ADR-004 job type of the processing job (handled by task 049).</summary>
    public const string JobType = "InvoiceSubmissionProcessing";

    /// <summary><c>sprk_document.sprk_documenttype</c> option "Invoice" (SpaarkeMaster sprk_Document/Entity.xml).</summary>
    internal const int InvoiceDocumentType = 100000001;

    internal const string PdfContentType = "application/pdf";
    internal const int MaxPoNumberLength = 100;
    internal const int MaxListSize = 50;

    /// <summary>Values per <c>In</c> condition in the list read (the RateCardImportService bound).</summary>
    internal const int MaxInValues = 500;

    internal const string SubmissionEntity = TrackingNumberAllocator.SubmissionEntity;
    internal const string OrganizationEntity = "sprk_organization";
    internal const string DocumentEntity = "sprk_document";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private static readonly string[] ViewColumns =
    {
        TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn, TrackingNumberAllocator.FirmColumn,
        "sprk_ponumber", "sprk_processingstatus", "sprk_channel", "sprk_receivedon", "sprk_outcomereasoncode", "sprk_document",
        "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment", "createdon",
    };

    private readonly ITrackingNumberAllocator _allocator;
    private readonly IGenericEntityService _entities;
    private readonly IDocumentDataverseService _documents;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ExternalDataService _externalData;
    private readonly SpeFileStore _spe;
    private readonly RecordContainerResolver _containers;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly JobSubmissionService _jobs;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<InvoiceSubmissionService> _logger;

    public InvoiceSubmissionService(
        ITrackingNumberAllocator allocator,
        IGenericEntityService entities,
        IDocumentDataverseService documents,
        ICommunicationDataverseService recordTypes,
        ExternalDataService externalData,
        SpeFileStore spe,
        RecordContainerResolver containers,
        IRecordOwnershipResolver ownership,
        JobSubmissionService jobs,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionService> logger)
    {
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _externalData = externalData ?? throw new ArgumentNullException(nameof(externalData));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The configured file limit in bytes (<see cref="EbillingOptions.MaxSubmissionFileSizeMb"/>).</summary>
    public long MaxFileBytes => (long)_ebilling.CurrentValue.MaxSubmissionFileSizeMb * 1024 * 1024;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Submit
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Validates and receives one submission. Never throws for a refusal; a caller's cancellation is rethrown after compensation.</summary>
    public virtual async Task<InvoiceSubmissionOutcome> SubmitAsync(InvoiceSubmissionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── 1. The payload: nothing is read or written for a request that is not valid ──
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var number = request.FirmInvoiceNumber!.Trim();
        var file = request.File!;
        var core = request.CoreRecord;
        var spaCaller = request.Channel == InvoiceSubmissionChannel.Spa ? request.ExternalCaller : null;
        if (request.Channel == InvoiceSubmissionChannel.Spa && (spaCaller is null || spaCaller.ContactId == Guid.Empty))
        {
            // The SPA route's filter guarantees a contact; reaching here without one is a caller bug, refused before any write.
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmForbidden,
                "Your identity could not be tied to a firm, so the invoice cannot be submitted.");
        }

        // ── 2. Reads that decide whether anything may be written ──
        switch (await IsActiveFirmAsync(request.FirmId, ct))
        {
            case null:
                return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.FirmUnverifiable,
                    "The firm could not be checked right now. Try again shortly.");
            case false:
                return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid,
                    "The firm was not found or is not active.");
        }

        var row = new Entity(SubmissionEntity);
        row[core.SubmissionColumn] = new EntityReference(core.EntityLogicalName, core.RecordId);
        var decided = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            InvoiceFamilyCoreRecordGuard.Check(row), _entities, _recordTypes.QueryRecordTypeRefAsync, ct);
        if (decided.IsRefused || decided.ResolverFields is null)
        {
            _logger.LogWarning("Invoice submission refused: the core record's fields could not be derived ({Code})", decided.RefusalCode);
            return decided.RefusalCode == InvoiceFamilyCoreRecordGuard.ReadFailedCode
                ? InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                    "The record could not be read right now. Try again shortly.")
                : InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.CoreRecordUnavailable,
                    "The record cannot take an invoice submission (it is missing or incomplete).");
        }

        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new(core.EntityLogicalName, core.RecordId) }) with
            {
                RequestedBy = RecordRequester.Of(systemUserId: spaCaller?.SystemUserId, objectId: request.CallerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return InvoiceSubmissionOutcome.OwnerRefused(owner);
        }

        // The owner team and the creator person go on the row here, in the member that builds it (RecordOwnerAssignmentCensusTests).
        owner.ApplyTo(row);

        ContainerDecision container;
        try
        {
            container = await ResolveContainerAsync(core, ct);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionOutcome.Refused(ex.StatusCode, InvoiceSubmissionErrors.StorageNotConfigured, ex.Detail ?? ex.Title);
        }

        if (container.Outcome is ContainerDecisionOutcome.Unresolved or ContainerDecisionOutcome.FailClosed
            || string.IsNullOrWhiteSpace(container.ContainerId))
        {
            // Never substitute another container: for a secure record that substitution is the leak (D-44, ADR-007).
            _logger.LogWarning("Invoice submission refused: no usable container for {Entity} {RecordId} ({Outcome})",
                core.EntityLogicalName, core.RecordId, container.Outcome);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.StorageNotConfigured,
                "This record has no storage container configured, so the invoice cannot be stored.");
        }

        return await WriteAsync(request, number, file, row, decided.ResolverFields, owner, container.ContainerId!, ct);
    }

    /// <summary>The container of the core record (D-44: secure when secure; fail closed). Virtual so a test can stand in for the resolver.</summary>
    internal virtual Task<ContainerDecision> ResolveContainerAsync(InvoiceCoreRecord core, CancellationToken ct) =>
        _containers.ResolveForRecordAsync(core.EntityLogicalName, core.RecordId, ct);

    private InvoiceSubmissionOutcome? Validate(InvoiceSubmissionRequest request)
    {
        // D-91 first: the number is required, so a request without one never costs an upload.
        var number = request.FirmInvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvoiceNumberRequired,
                "The firm invoice number is required.");
        }

        if (number.Length > TrackingNumberAllocator.MaxFirmInvoiceNumberLength)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvoiceNumberTooLong,
                $"The firm invoice number may be at most {TrackingNumberAllocator.MaxFirmInvoiceNumberLength} characters.");
        }

        if (request.PoNumber is { } po && po.Trim().Length > MaxPoNumberLength)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.PoNumberTooLong,
                $"The PO number may be at most {MaxPoNumberLength} characters.");
        }

        if (request.FirmId == Guid.Empty)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "A firm is required.");
        }

        if (request.CoreRecord.RecordId == Guid.Empty)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.CoreRecordUnavailable,
                "A record is required.");
        }

        if (request.File is not { Content.Length: > 0 } file)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FileRequired,
                "A non-empty PDF file is required.");
        }

        if (file.Content.LongLength > MaxFileBytes)
        {
            return TooLarge();
        }

        if (!IsPdf(file))
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.NotPdf,
                "Only PDF invoices are accepted.");
        }

        return null;
    }

    /// <summary>413 <see cref="InvoiceSubmissionErrors.TooLarge"/> with the configured limit.</summary>
    public InvoiceSubmissionOutcome TooLarge() =>
        InvoiceSubmissionOutcome.Refused(StatusCodes.Status413PayloadTooLarge, InvoiceSubmissionErrors.TooLarge,
            $"The invoice file may be at most {_ebilling.CurrentValue.MaxSubmissionFileSizeMb} MB.");

    /// <summary>Both the declared content type and the bytes: the part says <c>application/pdf</c> and the file starts with <c>%PDF-</c>.</summary>
    internal static bool IsPdf(InvoiceSubmissionFile file)
    {
        var declared = file.ContentType?.Split(';', 2)[0].Trim();
        return string.Equals(declared, PdfContentType, StringComparison.OrdinalIgnoreCase)
            && file.Content.AsSpan().StartsWith(PdfSignature);
    }

    /// <summary>True for an active firm, false for a missing or inactive one, null when it could not be read (a fault is never "no firm").</summary>
    private async Task<bool?> IsActiveFirmAsync(Guid firmId, CancellationToken ct)
    {
        try
        {
            var firm = await _entities.RetrieveAsync(OrganizationEntity, firmId, new[] { "statecode" }, ct);
            return firm is not null && firm.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Invoice submission refused: firm {FirmId} could not be read", firmId);
            return null;
        }
    }

    /// <summary>
    /// The id and display name of each ACTIVE firm among <paramref name="firmIds"/>, in the order given (ebilling-r1 task 051: the firm step's
    /// list on <c>GET /api/v1/external/me</c>). A missing or inactive firm is left out (a submission for it is refused anyway, see
    /// <see cref="IsActiveFirmAsync"/>); a firm with no name is listed under its id. Returns <see langword="null"/> when any read faulted (a
    /// fault is never "no firm"). Reads only the ids it is given, so the caller's own firms are the only ones that can come back.
    /// </summary>
    public async Task<IReadOnlyList<(Guid FirmId, string Name)>?> ReadActiveFirmNamesAsync(IReadOnlyCollection<Guid> firmIds, CancellationToken ct)
    {
        var firms = new List<(Guid FirmId, string Name)>(firmIds.Count);
        foreach (var firmId in firmIds)
        {
            try
            {
                var firm = await _entities.RetrieveAsync(OrganizationEntity, firmId, new[] { "statecode", "sprk_name" }, ct);
                if (firm is not null && firm.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0)
                {
                    var name = firm.GetAttributeValue<string>("sprk_name");
                    firms.Add((firmId, string.IsNullOrWhiteSpace(name) ? firmId.ToString() : name));
                }
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                // Not a firm any more: not offered.
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, ct))
            {
                _logger.LogWarning(ex, "Firm list unavailable: firm {FirmId} could not be read", firmId);
                return null;
            }
        }

        return firms;
    }

    // ── Writes, in order, each compensated on a later failure ──

    private async Task<InvoiceSubmissionOutcome> WriteAsync(
        InvoiceSubmissionRequest request, string number, InvoiceSubmissionFile file, Entity row,
        InvoiceFamilyResolverFields resolverFields, RecordOwnerResolution owner, string driveId, CancellationToken ct)
    {
        var core = request.CoreRecord;
        var submissionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        // 1. The bytes go under a flat name unique to this submission (two firms' "invoice.pdf" never collide; Fail never overwrites). The
        //    name carries the submission id, so whatever sits at it can only be this request's own upload.
        var uploadPath = SpeUploadPath.SanitizeFileName(UploadName(file.FileName, submissionId));
        string? uploadAttemptedAt = null;
        string? uploadedItemId = null;
        Guid? documentId = null;
        // True from the moment the allocator is asked: a create whose reply is lost (a timeout) may have committed the row, so compensation
        // deletes it by its id whenever it MAY exist (a delete of an absent row is ignored).
        var submissionMayExist = false;

        // A caller already gone writes nothing; from here on no write observes the caller's token (see the remarks).
        ct.ThrowIfCancellationRequested();
        var write = CancellationToken.None;

        try
        {
            FileHandleDto? uploaded;
            await using (var content = new MemoryStream(file.Content, writable: false))
            {
                uploadAttemptedAt = uploadPath;
                uploaded = await _spe.UploadSmallAsync(driveId, uploadPath, content, ConflictBehavior.Fail, write);
            }

            if (uploaded is null)
            {
                throw new InvalidOperationException("The invoice upload returned no file handle.");
            }

            uploadedItemId = uploaded.Id;

            // 2. The document row, linked to the core record, then typed Invoice. Internal: the id is chosen here and sent with the create,
            //    so a create whose reply is lost is still deleted by it. SPA: the external create mints the id; a lost reply is found by
            //    the uploaded item instead (CompensateAsync). Either way a failed follow-up update is compensated too.
            documentId = request.Channel == InvoiceSubmissionChannel.Spa ? null : Guid.NewGuid();
            documentId = await CreateDocumentRowAsync(request, documentId, uploaded, driveId, file, owner, write);
            await CompleteDocumentRowAsync(request, documentId.Value, uploaded, driveId, file, write);

            // 3. The submission, created with its tracking number (D-87).
            foreach (var (column, value) in resolverFields.ToFields())
            {
                row[column] = value;
            }

            row["sprk_name"] = number;
            row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received);
            row["sprk_channel"] = new OptionSetValue((int)request.Channel);
            row["sprk_document"] = new EntityReference(DocumentEntity, documentId.Value);
            row["sprk_receivedon"] = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.PoNumber))
            {
                row["sprk_ponumber"] = request.PoNumber.Trim();
            }

            if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            {
                row["sprk_correlationid"] = request.CorrelationId.Length > 100 ? request.CorrelationId[..100] : request.CorrelationId;
            }

            if (request.Channel == InvoiceSubmissionChannel.Spa && request.ExternalCaller is { ContactId: var contactId } && contactId != Guid.Empty)
            {
                row["sprk_submitter"] = new EntityReference("contact", contactId);
            }

            // The FR-05 guard on the payload exactly as written (one core lookup), the last check before the create.
            if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
            {
                throw new InvalidOperationException($"The submission payload failed the invoice-family guard ({refused.RefusalCode}).");
            }

            submissionMayExist = true;
            var allocation = await _allocator.CreateSubmissionAsync(submissionId, request.FirmId, number, row.Attributes.ToDictionary(), write);
            if (!allocation.Succeeded)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: no tracking number could be allocated ({Reason})", submissionId, allocation.Reason);
                // A refusal creates nothing (TrackingNumberAllocator: the bound was reached without a create).
                await CompensateAsync(submissionId, submissionMayExist: false, documentId, driveId, uploadedItemId, uploadAttemptedAt);
                return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
                    "A tracking number could not be assigned right now, so nothing was saved. Try again shortly.");
            }

            // 4. The job (ADR-004): payload { submissionId }, idempotency key = the submission id.
            await _jobs.SubmitJobAsync(BuildJob(jobId, submissionId, request.CorrelationId), write);

            _logger.LogInformation(
                "Invoice submission {SubmissionId} received through {Channel} for {Entity} {RecordId}; job {JobId} enqueued",
                submissionId, request.Channel, core.EntityLogicalName, core.RecordId, jobId);
            _logger.LogDebug("Invoice submission {SubmissionId} tracking number {TrackingNumber}", submissionId, allocation.TrackingNumber);

            return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(submissionId, jobId, allocation.TrackingNumber!, documentId.Value));
        }
        catch (SpaarkeStorageException ex) when (ex.StatusCode == StatusCodes.Status409Conflict && uploadedItemId is null)
        {
            // The name carries this submission's id, so the file holding it is this request's own: the PUT committed, its reply was lost
            // and the Graph handler's retry was answered 409. Remove it (found by its path) and refuse; nothing else was written yet.
            _logger.LogWarning("Invoice submission {SubmissionId}: the upload was answered 409 (a retried PUT); removing the file it left", submissionId);
            await CompensateAsync(submissionId, submissionMayExist: false, documentId: null, driveId, itemId: null, uploadAttemptedAt);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.Failed,
                "The invoice could not be stored. Nothing was saved; try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId} failed; removing what it wrote", submissionId);
            await CompensateAsync(submissionId, submissionMayExist, documentId, driveId, uploadedItemId, uploadAttemptedAt);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status500InternalServerError, InvoiceSubmissionErrors.Failed,
                "The invoice could not be submitted and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// Creates the document row. SPA: the external surface's create (<see cref="ExternalDataService.CreateDocumentAsync"/>), which binds the core
    /// record, the owner team and the FR-25 creator stamp of the resolved principal. Internal: the shared document seam with the creator person
    /// and the id this service chose (<see cref="CreateDocumentRequest.Id"/>).
    /// </summary>
    private async Task<Guid> CreateDocumentRowAsync(
        InvoiceSubmissionRequest request, Guid? presetId, FileHandleDto uploaded, string driveId, InvoiceSubmissionFile file,
        RecordOwnerResolution owner, CancellationToken ct)
    {
        var core = request.CoreRecord;
        if (request.Channel == InvoiceSubmissionChannel.Spa)
        {
            var created = await _externalData.CreateDocumentAsync(
                core.DocumentParentKind,
                core.RecordId,
                new ExternalUploadedFilePointers(uploaded.DriveId ?? driveId, uploaded.Id, uploaded.Name, uploaded.Size ?? file.Content.LongLength, uploaded.WebUrl),
                owner.OwningTeamId!.Value,
                request.ExternalCaller!,
                ct);
            return Guid.Parse(created.SprkDocumentid);
        }

        return Guid.Parse(await _documents.CreateDocumentAsync(new CreateDocumentRequest
        {
            Id = presetId,
            Name = uploaded.Name,
            ContainerId = driveId,
            OwningTeamId = owner.OwningTeamId,
            CreatedByPersonId = owner.CreatedByPerson,
        }, ct));
    }

    /// <summary>
    /// Types the document Invoice with the PDF MIME type. The internal row also gets its file pointers and its core-record lookup here (the SPA
    /// create already set both).
    /// </summary>
    private Task CompleteDocumentRowAsync(
        InvoiceSubmissionRequest request, Guid documentId, FileHandleDto uploaded, string driveId, InvoiceSubmissionFile file, CancellationToken ct)
    {
        var core = request.CoreRecord;
        var update = request.Channel == InvoiceSubmissionChannel.Spa
            ? new UpdateDocumentRequest
            {
                DocumentType = InvoiceDocumentType,
                MimeType = PdfContentType,
            }
            : new UpdateDocumentRequest
            {
                GraphDriveId = uploaded.DriveId ?? driveId,
                GraphItemId = uploaded.Id,
                FileName = uploaded.Name,
                FileSize = uploaded.Size ?? file.Content.LongLength,
                MimeType = PdfContentType,
                HasFile = true,
                FilePath = uploaded.WebUrl,
                DocumentType = InvoiceDocumentType,
                MatterLookup = core.Kind == InvoiceCoreRecordKind.Matter ? core.RecordId : null,
                ProjectLookup = core.Kind == InvoiceCoreRecordKind.Project ? core.RecordId : null,
                WorkAssignmentLookup = core.Kind == InvoiceCoreRecordKind.WorkAssignment ? core.RecordId : null,
            };
        return _documents.UpdateDocumentAsync(documentId.ToString(), update, ct);
    }

    internal static JobContract BuildJob(Guid jobId, Guid submissionId, string? correlationId) => new()
    {
        JobId = jobId,
        JobType = JobType,
        SubjectId = submissionId.ToString(),
        CorrelationId = correlationId ?? string.Empty,
        IdempotencyKey = submissionId.ToString(),
        Payload = JsonSerializer.SerializeToDocument(new { submissionId }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    /// <summary><c>{client name without extension}-{submission id}.pdf</c>, so each submission's file name is its own.</summary>
    internal static string UploadName(string? clientFileName, Guid submissionId)
    {
        var stem = Path.GetFileNameWithoutExtension(clientFileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "invoice";
        }

        if (stem.Length > 100)
        {
            stem = stem[..100];
        }

        return $"{stem}-{submissionId:N}.pdf";
    }

    /// <summary>Best effort, reverse order, own token: a failed compensation is logged and never masks the original failure.</summary>
    /// <param name="uploadPath">The path an upload was attempted at (null when none was). With no <paramref name="itemId"/> the upload's reply
    /// never arrived, so the item is looked up by this path: the name carries the submission id, so anything there is this request's own.</param>
    private async Task CompensateAsync(
        Guid submissionId, bool submissionMayExist, Guid? documentId, string driveId, string? itemId, string? uploadPath)
    {
        if (itemId is null && uploadPath is not null)
        {
            try
            {
                itemId = (await _spe.GetDriveItemByPathAsync(driveId, uploadPath, CancellationToken.None))?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not look up the uploaded file by its path", submissionId);
            }
        }

        if (submissionMayExist)
        {
            try
            {
                await _entities.DeleteAsync(SubmissionEntity, submissionId, CancellationToken.None);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                // The lost-reply case went the other way: the create never committed. Nothing to remove.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not delete the submission row; it holds no job", submissionId);
            }
        }

        if (documentId is { } doc)
        {
            await DeleteDocumentAsync(submissionId, doc);
        }
        else if (itemId is not null)
        {
            // No id came back from the document create (a lost reply on the SPA path, which mints the id itself): the row, if it was
            // committed, carries the uploaded item in sprk_graphitemid.
            try
            {
                var query = new QueryExpression(DocumentEntity) { ColumnSet = new ColumnSet(false), TopCount = 5 };
                query.Criteria.AddCondition("sprk_graphitemid", ConditionOperator.Equal, itemId);
                foreach (var row in (await _entities.RetrieveMultipleAsync(query, CancellationToken.None)).Entities)
                {
                    await DeleteDocumentAsync(submissionId, row.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not look up a document for the uploaded file", submissionId);
            }
        }

        if (itemId is not null)
        {
            try
            {
                await _spe.DeleteFileAsync(driveId, itemId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not delete the uploaded file {ItemId}", submissionId, itemId);
            }
        }
    }

    private async Task DeleteDocumentAsync(Guid submissionId, Guid documentId)
    {
        try
        {
            await _entities.DeleteAsync(DocumentEntity, documentId, CancellationToken.None);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // A create whose reply was lost went the other way (the preset id was never committed). Nothing to remove.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not delete document {DocumentId}", submissionId, documentId);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Read (app-only; the routes scope before they return anything)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One submission, read app-only; null when it does not exist. The caller's route decides whether the caller may see it. A read fault
    /// throws <see cref="ReadUnavailable"/> (503, retryable), never a 500 and never "not found".
    /// </summary>
    public virtual async Task<InvoiceSubmissionView?> GetAsync(Guid submissionId, CancellationToken ct)
    {
        if (submissionId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var row = await _entities.RetrieveAsync(SubmissionEntity, submissionId, ViewColumns, ct);
            return row is null ? null : ToView(row);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Invoice submission {SubmissionId} could not be read", submissionId);
            throw ReadUnavailable();
        }
    }

    /// <summary>The 503 a read fault becomes (<see cref="InvoiceSubmissionErrors.CoreRecordUnavailable"/>); the routes render it.</summary>
    internal static SdapProblemException ReadUnavailable() => new(
        InvoiceSubmissionErrors.CoreRecordUnavailable, "Invoice submissions unavailable",
        "Invoice submissions could not be read right now. Try again shortly.", StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// The newest submissions of <paramref name="firmIds"/> filed against any of the given core records, newest first, at most
    /// <see cref="MaxListSize"/>. Read app-only; the scope IS the query (a record outside the sets is never read). A read fault throws
    /// <see cref="ReadUnavailable"/>.
    /// </summary>
    public virtual async Task<IReadOnlyList<InvoiceSubmissionView>> ListAsync(
        IReadOnlyCollection<Guid> firmIds,
        IReadOnlyCollection<Guid> matterIds,
        IReadOnlyCollection<Guid> projectIds,
        IReadOnlyCollection<Guid> workAssignmentIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(firmIds);
        if (firmIds.Count == 0)
        {
            return Array.Empty<InvoiceSubmissionView>();
        }

        var found = new Dictionary<Guid, InvoiceSubmissionView>();
        foreach (var (column, ids) in new[]
                 {
                     ("sprk_regardingmatter", matterIds), ("sprk_regardingproject", projectIds), ("sprk_regardingworkassignment", workAssignmentIds),
                 })
        {
            foreach (var chunk in ids.Where(id => id != Guid.Empty).Distinct().Chunk(MaxInValues))
            {
                var query = new QueryExpression(SubmissionEntity) { ColumnSet = new ColumnSet(ViewColumns), TopCount = MaxListSize };
                query.Criteria.AddCondition(TrackingNumberAllocator.FirmColumn, ConditionOperator.In, firmIds.Cast<object>().ToArray());
                query.Criteria.AddCondition(column, ConditionOperator.In, chunk.Cast<object>().ToArray());
                query.AddOrder("createdon", OrderType.Descending);

                EntityCollection page;
                try
                {
                    page = await _entities.RetrieveMultipleAsync(query, ct);
                }
                catch (Exception ex) when (!IsCallerCancellation(ex, ct))
                {
                    _logger.LogWarning(ex, "Invoice submissions could not be listed");
                    throw ReadUnavailable();
                }

                foreach (var row in page.Entities)
                {
                    found[row.Id] = ToView(row);
                }
            }
        }

        return found.Values
            .OrderByDescending(v => v.CreatedOn ?? DateTime.MinValue)
            .ThenBy(v => v.SubmissionId)
            .Take(MaxListSize)
            .ToList();
    }

    internal static InvoiceSubmissionView ToView(Entity row)
    {
        InvoiceCoreRecord? core = null;
        foreach (var (column, kind) in new[]
                 {
                     ("sprk_regardingmatter", InvoiceCoreRecordKind.Matter), ("sprk_regardingproject", InvoiceCoreRecordKind.Project),
                     ("sprk_regardingworkassignment", InvoiceCoreRecordKind.WorkAssignment),
                 })
        {
            if (row.GetAttributeValue<EntityReference>(column) is { Id: var id } && id != Guid.Empty)
            {
                // More than one would violate FR-05; such a row regards nothing here, so no route can scope it to a caller.
                core = core is null ? new InvoiceCoreRecord(kind, id) : null;
                if (core is null)
                {
                    break;
                }
            }
        }

        var status = row.GetAttributeValue<OptionSetValue>("sprk_processingstatus")?.Value;
        var channel = row.GetAttributeValue<OptionSetValue>("sprk_channel")?.Value;
        return new InvoiceSubmissionView
        {
            SubmissionId = row.Id,
            TrackingNumber = row.GetAttributeValue<string>(TrackingNumberAllocator.TrackingNumberColumn),
            FirmInvoiceNumber = row.GetAttributeValue<string>(TrackingNumberAllocator.FirmInvoiceNumberColumn),
            PoNumber = row.GetAttributeValue<string>("sprk_ponumber"),
            Status = status is { } s && Enum.IsDefined(typeof(InvoiceSubmissionStatus), s) ? ((InvoiceSubmissionStatus)s).ToString() : null,
            Channel = channel switch
            {
                100000000 => "SPA",
                100000001 => "Internal",
                100000002 => "Mailbox",
                100000003 => "Outlook",
                100000004 => "API",
                _ => null,
            },
            ReceivedOn = row.GetAttributeValue<DateTime?>("sprk_receivedon"),
            OutcomeReasonCode = row.GetAttributeValue<string>("sprk_outcomereasoncode"),
            DocumentId = row.GetAttributeValue<EntityReference>("sprk_document")?.Id,
            FirmId = row.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn)?.Id,
            CoreRecord = core,
            CreatedOn = row.GetAttributeValue<DateTime?>("createdon"),
        };
    }

    private static bool IsCallerCancellation(Exception ex, CancellationToken ct) => ex is OperationCanceledException && ct.IsCancellationRequested;

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
