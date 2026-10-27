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
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The one server-side invoice intake (ebilling-r1 FR-17, Processing Pipeline step 0, ADR-002): validates the request, stores the PDF in the core
/// record's container, creates the <c>sprk_document</c> (type Invoice) and the <c>sprk_invoicesubmission</c> (Received, with its tracking number),
/// and enqueues <see cref="JobType"/>. Both entry points call it: the external SPA route and the internal route (D-24: the same checks).
/// </summary>
/// <remarks>
/// <para><b>Authorization of the CALLER is the routes' job.</b> Each route's endpoint filter decides the caller, the firm and the core record
/// before this runs (ADR-008); the request carries the validated firm and core record. This service validates the payload and writes.</para>
/// <para><b>The firm-to-record link is THIS service's (ebilling-r1 D-106, D-110; ADR-002 one owner per invariant).</b> A submission is filed
/// only under a firm that is one of the core record's assigned law firms (<c>sprk_assignedlawfirm1/2</c>): 403 <see cref="InvoiceSubmissionErrors.FirmNotLinked"/>
/// otherwise, 503 <see cref="InvoiceSubmissionErrors.FirmUnverifiable"/> when the links cannot be read. The check runs here, in the one
/// decision point both channels call, so an internal user who names a firm the record is not linked to is refused exactly as an external
/// contact is; the external filter keeps an early copy of the same check (<see cref="CheckFirmLinkAsync"/>) only to refuse before the upload
/// is read. It reuses <see cref="ExternalParticipationService.GetReferencedOrganizationIdsAsync"/> (the access model's own record-to-organization
/// read): that class lives in the external-access infrastructure namespace this service already depends on (<see cref="ExternalDataService"/>,
/// <see cref="CallerPrincipal"/>), and the read is app-only and channel-neutral, so no new seam or interface is needed (ADR-010).</para>
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
/// <para><b>The existing-document entry (ebilling-r1 050a, D-134).</b> The review-confirm route calls <see cref="SubmitExistingDocumentAsync"/>:
/// the same field, firm, core-record, firm-link and owner checks (<see cref="DecideAsync"/>), no file checks, no upload and no document create;
/// the submission points at the confirmed document, its id is derived from the document (idempotent by document), and the job re-files the
/// document under the invoice it creates (<see cref="InvoiceDocumentRefile"/>).</para>
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
        // ebilling-r1 task 098: the resulting invoice, so the external routes can apply (a') to the submission (a derived row).
        InvoiceColumn,
    };

    /// <summary>The resulting invoice of an accepted submission (<c>sprk_invoicesubmission.sprk_invoice</c>).</summary>
    internal const string InvoiceColumn = "sprk_invoice";

    private readonly ITrackingNumberAllocator _allocator;
    private readonly IGenericEntityService _entities;
    private readonly IDocumentDataverseService _documents;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ExternalDataService _externalData;
    private readonly ExternalParticipationService _participations;
    private readonly SpeFileStore _spe;
    private readonly RecordContainerResolver _containers;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ExistingDocumentIsolationCheck _isolation;
    private readonly JobSubmissionService _jobs;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<InvoiceSubmissionService> _logger;

    public InvoiceSubmissionService(
        ITrackingNumberAllocator allocator,
        IGenericEntityService entities,
        IDocumentDataverseService documents,
        ICommunicationDataverseService recordTypes,
        ExternalDataService externalData,
        ExternalParticipationService participations,
        SpeFileStore spe,
        RecordContainerResolver containers,
        IRecordOwnershipResolver ownership,
        ExistingDocumentIsolationCheck isolation,
        JobSubmissionService jobs,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionService> logger)
    {
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _externalData = externalData ?? throw new ArgumentNullException(nameof(externalData));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _spe = spe ?? throw new ArgumentNullException(nameof(spe));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _isolation = isolation ?? throw new ArgumentNullException(nameof(isolation));
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
        if (spaCaller?.SystemUserId is not null)
        {
            // Defence in depth for the external route's filter (ExternalInvoiceSubmissionAuthorizationFilter, same code and status): a workforce
            // systemuser submits through the internal route, and no external contact rule applies to it. Refused before any read or write
            // (fail closed), so a route that ever skipped the filter still cannot file under a contact's firm membership.
            _logger.LogWarning("Invoice submission refused: systemuser {SystemUserId} called the external entry point", spaCaller.SystemUserId);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.UseInternalRoute,
                "This is the external invoice submission. As an internal user, submit invoices through the internal route instead.");
        }

        if (request.Channel == InvoiceSubmissionChannel.Spa && (spaCaller is null || spaCaller.ContactId == Guid.Empty))
        {
            // The SPA route's filter guarantees a contact; reaching here without one is a caller bug, refused before any write.
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmForbidden,
                "Your identity could not be tied to a firm, so the invoice cannot be submitted.");
        }

        // ── 2. Reads that decide whether anything may be written ──
        var decision = await DecideAsync(request, spaCaller, ct);
        if (decision.Refusal is { } refused)
        {
            return refused;
        }

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

        return await WriteAsync(request, number, file, decision.Row!, decision.ResolverFields!, decision.Owner!, container.ContainerId!, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Submit an existing document (ebilling-r1 task 050a, D-134: the review-confirm route)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The submission id of the confirm of <paramref name="documentId"/>: deterministic, so the document has exactly one confirm submission.
    /// It is the dedupe key of <see cref="SubmitExistingDocumentAsync"/> (D-134 "idempotent by document") and the marker by which the job knows
    /// the submission's document was not created by intake and must be re-filed under the invoice (<see cref="InvoiceDocumentRefile"/>).
    /// </summary>
    public static Guid ExistingDocumentSubmissionIdFor(Guid documentId) => InvoiceSubmissionPersister.IdOf($"ebilling-confirm-submission-{documentId:D}");

    /// <summary>The processing job id of that submission: deterministic too, so a retry answers with the same job id it was first given.</summary>
    public static Guid ExistingDocumentJobIdFor(Guid documentId) => InvoiceSubmissionPersister.IdOf($"ebilling-confirm-job-{documentId:D}");

    /// <summary>
    /// The document's core-record lookups (050a review #3): the confirm's core record must be the one the document is filed under, if any. The
    /// kinds match <see cref="InvoiceCoreRecordKind"/>.
    /// </summary>
    /// <remarks>Both families (D-140): the unprefixed lookups and their <c>sprk_related*</c> twins are both live filing links on a document
    /// (<c>DocumentLinkFields.All</c>; <c>SecureChildLineage</c> reads both as parents, and <c>DocumentLinkFieldMap.SupersededBy</c> only
    /// RECORDS the mapping for a future migration), so a document filed through either one is filed.
    /// <para>A named projection of the one vocabulary, <see cref="DocumentLinkFields.All"/> (DocumentLinkVocabularyGuardTests): every link whose
    /// target is a matter, project or work assignment. Excluded, on purpose: the invoice links (a document re-filed under an invoice is what
    /// this confirm produces, and the persister checks <c>sprk_invoice</c> itself); the communication link (followed as the chain's next hop,
    /// <see cref="DocumentRelatedCommunication"/>); and every other target (agreement, contact, event, organization, service request, to-do,
    /// vendor, the OOB email activity), none of which is an invoice core record.</para></remarks>
    private static readonly (string Column, InvoiceCoreRecordKind Kind)[] DocumentCoreColumns =
    [
        .. DocumentLinkFields.All
            .Where(f => CoreRecordKindOfTable(f.TargetEntityLogicalName) is not null)
            .Select(f => (f.LogicalName, CoreRecordKindOfTable(f.TargetEntityLogicalName)!.Value)),
    ];

    private static InvoiceCoreRecordKind? CoreRecordKindOfTable(string table) => table switch
    {
        "sprk_matter" => InvoiceCoreRecordKind.Matter,
        "sprk_project" => InvoiceCoreRecordKind.Project,
        "sprk_workassignment" => InvoiceCoreRecordKind.WorkAssignment,
        _ => null,
    };

    /// <summary>D-140: the communication's core-record lookups (the regarding columns <c>CommunicationService</c> files an email under).</summary>
    private static readonly (string Column, InvoiceCoreRecordKind Kind)[] CommunicationCoreColumns =
    [
        ("sprk_regardingmatter", InvoiceCoreRecordKind.Matter),
        ("sprk_regardingproject", InvoiceCoreRecordKind.Project),
        ("sprk_regardingworkassignment", InvoiceCoreRecordKind.WorkAssignment),
    ];

    private const string CommunicationEntity = "sprk_communication";
    private const string ThreadEntity = "sprk_communicationthread";

    /// <summary>
    /// 050a review round 4 (uac-r2 F1): a communication's link to its thread, from the lineage map (<c>SecureChildLineage</c>, the column
    /// <see cref="RecordThreadFiling"/> and <c>SecureChildReconciler.ContextFor</c> follow). The thread carries the same three regarding
    /// columns as the communication (<see cref="CommunicationCoreColumns"/>; the map's thread entry), so they are read from it unchanged.
    /// </summary>
    private static readonly string CommunicationThread =
        Sprk.Bff.Api.Services.Access.SecureChildLineage.Children[CommunicationEntity].Lookups.Single(l => l.Value == ThreadEntity).Key;

    /// <summary>D-140: the communication hop's columns: its regarding core columns and its thread (round 4).</summary>
    private static readonly string[] CommunicationHopColumns = [.. CommunicationCoreColumns.Select(c => c.Column), CommunicationThread];

    /// <summary>
    /// D-140: the thread row's columns <see cref="RecordThreadFiling.ParentsOf"/> needs here: its type (<c>sprk_threadtype</c>, which decides
    /// whether it files its messages at all) and the three core regarding columns it carries (the only parents compared as core records).
    /// </summary>
    private static readonly string[] ThreadHopColumns =
        [RecordThreadFiling.ThreadTypeColumn, .. CommunicationCoreColumns.Select(c => c.Column)];

    /// <summary>D-140: a document's link to the communication it came from (every email archive / attachment writer stamps it).</summary>
    private static readonly string DocumentRelatedCommunication =
        DocumentLinkFields.All.Single(f => f.TargetEntityLogicalName == "sprk_communication").LogicalName;

    /// <summary>D-140: a document's link to the document it was extracted from (an attachment's parent).</summary>
    private const string DocumentParentDocument = "sprk_parentdocument";

    /// <summary>
    /// D-140: how many documents the filing chain walks, the confirmed one included (it, then at most three parents). A chain that is still
    /// unresolved past it, or that loops, is refused (fail closed): the record the document is filed under could not be established.
    /// </summary>
    internal const int MaxFilingChainDocuments = 4;

    private static readonly string[] FilingChainColumns =
        [.. DocumentCoreColumns.Select(c => c.Column), DocumentRelatedCommunication, DocumentParentDocument];

    private static readonly string[] ExistingDocumentColumns =
        ["sprk_invoice", "sprk_graphdriveid", "sprk_graphitemid", .. FilingChainColumns];

    /// <summary>
    /// The existing-document entry (D-134): a reviewer confirms that a document already stored (an email attachment) is an invoice. It runs the
    /// checks <see cref="SubmitAsync"/> runs on the request's fields, the firm, the core record, the firm link (D-106 / D-110) and the owner, and
    /// SKIPS the file checks, the upload and the document create: the submission points at <see cref="InvoiceSubmissionRequest.ExistingDocumentId"/>,
    /// so no second copy of the PDF is made. Never throws for a refusal.
    /// </summary>
    /// <remarks>
    /// <para><b>Idempotent by document.</b> The submission id is <see cref="ExistingDocumentSubmissionIdFor"/> the document. A retry for the same
    /// firm, record and firm invoice number gets the earlier submission back (no second submission, so no second invoice); one that names
    /// another firm, record or number is 409 <see cref="InvoiceSubmissionErrors.DocumentAlreadySubmitted"/>. Two confirms racing cannot both
    /// create: the second create of the same id is the allocator's <see cref="TrackingNumberOutcome.AlreadyCreated"/>.</para>
    /// <para><b>Nothing is deleted.</b> The row is never compensated by a delete (it may be a concurrent confirm's). Instead the job is
    /// (re)submitted whenever the submission is still Received: after the create, after <see cref="TrackingNumberOutcome.AlreadyCreated"/>,
    /// and on a retry that finds it Received. The job handler runs one delivery per submission under its lock and skips a submission that is
    /// already terminal (FR-20), so a second message for it is harmless; a lost reply or a failed enqueue is completed by the retry.</para>
    /// <para><b>The document</b> must exist (404 <see cref="InvoiceSubmissionErrors.DocumentNotFound"/>), hold a stored file (422
    /// <see cref="InvoiceSubmissionErrors.DocumentHasNoFile"/>) and not already be linked to an invoice (409
    /// <see cref="InvoiceSubmissionErrors.DocumentAlreadyInvoiced"/>), and not be filed under another core record (409
    /// <see cref="InvoiceSubmissionErrors.DocumentCoreRecordMismatch"/>; an unfiled document is allowed, owner 2026-10-20). The caller's rights
    /// on it are the route's (ADR-008).</para>
    /// </remarks>
    public virtual async Task<InvoiceSubmissionOutcome> SubmitExistingDocumentAsync(InvoiceSubmissionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExistingDocumentId is not { } documentId || documentId == Guid.Empty)
        {
            throw new ArgumentException("The existing-document entry needs ExistingDocumentId.", nameof(request));
        }

        if (request.Channel != InvoiceSubmissionChannel.Internal || request.File is not null)
        {
            throw new ArgumentException("The existing-document entry is internal and carries no file.", nameof(request));
        }

        // ── 1. The request's own fields: D-91 first, so a confirm without a number is 400 before anything is read ──
        if (ValidateFields(request) is { } invalid)
        {
            return invalid;
        }

        var number = request.FirmInvoiceNumber!.Trim();
        var submissionId = ExistingDocumentSubmissionIdFor(documentId);
        var jobId = ExistingDocumentJobIdFor(documentId);

        // ── 2. The earlier submission for this document, if any: the retry's answer ──
        InvoiceSubmissionView? earlier;
        try
        {
            earlier = await GetAsync(submissionId, ct);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionOutcome.Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title);
        }

        if (earlier is not null)
        {
            return await AnswerWithEarlierAsync(request, number, earlier, documentId, jobId, ct);
        }

        // ── 3. The document: present, holding a file, not already an invoice's, not filed under another core record ──
        if (await CheckExistingDocumentAsync(documentId, request.CoreRecord, ct) is { } documentRefusal)
        {
            return documentRefusal;
        }

        // ── 4. The same checks as an upload (firm, record, firm link, owner) ──
        var decision = await DecideAsync(request, spaCaller: null, ct);
        if (decision.Refusal is { } refused)
        {
            return refused;
        }

        // ── 5. The submission, then its job ──
        var row = decision.Row!;
        foreach (var (column, value) in decision.ResolverFields!.ToFields())
        {
            row[column] = value;
        }

        row["sprk_name"] = number;
        row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received);
        row["sprk_channel"] = new OptionSetValue((int)InvoiceSubmissionChannel.Internal);
        row["sprk_document"] = new EntityReference(DocumentEntity, documentId);
        row["sprk_receivedon"] = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.PoNumber))
        {
            row["sprk_ponumber"] = request.PoNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            row["sprk_correlationid"] = request.CorrelationId.Length > 100 ? request.CorrelationId[..100] : request.CorrelationId;
        }

        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } guardRefusal)
        {
            throw new InvalidOperationException($"The submission payload failed the invoice-family guard ({guardRefusal.RefusalCode}).");
        }

        // A caller already gone writes nothing; from here on no write observes the caller's token (as SubmitAsync).
        ct.ThrowIfCancellationRequested();
        var write = CancellationToken.None;

        TrackingNumberResult allocation;
        try
        {
            allocation = await _allocator.CreateSubmissionAsync(submissionId, request.FirmId, number, row.Attributes.ToDictionary(), write);
        }
        catch (InvalidOperationException ex)
        {
            // The allocator's reconcile: the id exists for another firm or firm invoice number (a concurrent confirm that named them). Not
            // ours, so not touched.
            _logger.LogWarning(ex, "Invoice submission {SubmissionId}: the confirm of document {DocumentId} met a concurrent confirm with other details",
                submissionId, documentId);
            return DocumentAlreadySubmitted();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId}: the confirm of document {DocumentId} could not be created", submissionId, documentId);
            return NotQueued();
        }

        if (!allocation.Succeeded)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: no tracking number could be allocated ({Reason})", submissionId, allocation.Reason);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
                "A tracking number could not be assigned right now, so nothing was saved. Try again shortly.");
        }

        if (allocation.Outcome == TrackingNumberOutcome.AlreadyCreated)
        {
            // 050a review #5: a concurrent confirm (or a lost reply) created the row first. The allocator's reconcile compared the firm and the
            // number only; the row is answered exactly as a retry is (firm, number AND core record, and its terminal state), never taken as ours.
            InvoiceSubmissionView? concurrent;
            try
            {
                concurrent = await GetAsync(submissionId, write);
            }
            catch (SdapProblemException ex)
            {
                return InvoiceSubmissionOutcome.Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title);
            }

            if (concurrent is null)
            {
                // Created, then gone before it could be read (a delete outside this path): nothing to answer with; a retry decides again.
                _logger.LogWarning("Invoice submission {SubmissionId}: already created for document {DocumentId} but no longer readable", submissionId, documentId);
                return NotQueued();
            }

            return await AnswerWithEarlierAsync(request, number, concurrent, documentId, jobId, write);
        }

        if (!await TrySubmitJobAsync(jobId, submissionId, request.CorrelationId))
        {
            return NotQueued();
        }

        _logger.LogInformation(
            "Invoice submission {SubmissionId} received for existing document {DocumentId} ({Outcome}) for {Entity} {RecordId}; job {JobId} enqueued",
            submissionId, documentId, allocation.Outcome, request.CoreRecord.EntityLogicalName, request.CoreRecord.RecordId, jobId);
        return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(submissionId, jobId, allocation.TrackingNumber!, documentId));
    }

    /// <summary>
    /// A retry (or a concurrent create, review #5): the document's confirm submission exists. The same firm, record and firm invoice number get
    /// it back (re-queued while it is still Received); anything else is 409 and names nothing about it. One that already ended Failed or
    /// Rejected is 409 <see cref="InvoiceSubmissionErrors.SubmissionAlreadyEnded"/> naming that status, and the caller does not re-mark the
    /// document (review #4; owner 2026-10-20, D-139: a new attempt after a terminal status is a later feature).
    /// </summary>
    private async Task<InvoiceSubmissionOutcome> AnswerWithEarlierAsync(
        InvoiceSubmissionRequest request, string number, InvoiceSubmissionView earlier, Guid documentId, Guid jobId, CancellationToken ct)
    {
        var same = earlier.FirmId == request.FirmId
            && earlier.CoreRecord == request.CoreRecord
            && earlier.DocumentId == documentId
            && string.Equals(earlier.FirmInvoiceNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(earlier.TrackingNumber);
        if (!same)
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: document {DocumentId} was already confirmed with other details; refused (409)",
                earlier.SubmissionId, documentId);
            return DocumentAlreadySubmitted();
        }

        if (earlier.Status is nameof(InvoiceSubmissionStatus.Failed) or nameof(InvoiceSubmissionStatus.Rejected))
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: the confirm of document {DocumentId} already ended {Status}; refused (409)",
                earlier.SubmissionId, documentId, earlier.Status);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.SubmissionAlreadyEnded,
                $"The document was already submitted as an invoice and that submission ended {earlier.Status}. Nothing was saved; " +
                "a new attempt for the same document is not available yet.");
        }

        if (earlier.Status == nameof(InvoiceSubmissionStatus.Received))
        {
            ct.ThrowIfCancellationRequested();
            if (!await TrySubmitJobAsync(jobId, earlier.SubmissionId, request.CorrelationId))
            {
                return NotQueued();
            }
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: the confirm of document {DocumentId} was retried; answering with it ({Status})",
            earlier.SubmissionId, documentId, earlier.Status);
        return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(earlier.SubmissionId, jobId, earlier.TrackingNumber!, documentId));
    }

    /// <summary>
    /// Null when the document can be submitted; otherwise the refusal. A read fault is 503 (never "not found"). A document filed under a core
    /// record other than <paramref name="core"/> is 409 <see cref="InvoiceSubmissionErrors.DocumentCoreRecordMismatch"/> (review #3), where
    /// "filed under" is the EFFECTIVE core record through the filing chain (<see cref="ResolveFilingAsync"/>, D-140: an email attachment
    /// follows its communication's matter); an UNFILED document (nothing in the chain names a core record) is allowed (owner 2026-10-20, D-139).
    /// </summary>
    private async Task<InvoiceSubmissionOutcome?> CheckExistingDocumentAsync(Guid documentId, InvoiceCoreRecord core, CancellationToken ct)
    {
        Entity? document;
        try
        {
            document = await _entities.RetrieveAsync(DocumentEntity, documentId, ExistingDocumentColumns, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            document = null;
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Invoice submission refused: document {DocumentId} could not be read", documentId);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                "The document could not be read right now. Try again shortly.");
        }

        if (document is null)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status404NotFound, InvoiceSubmissionErrors.DocumentNotFound,
                "The document no longer exists. Nothing was saved.");
        }

        if (document.GetAttributeValue<EntityReference>("sprk_invoice") is { Id: var linked } && linked != Guid.Empty)
        {
            // The invoice is never named: the caller was authorized against the document, not against that invoice.
            _logger.LogInformation("Invoice submission refused: document {DocumentId} is already linked to invoice {InvoiceId}", documentId, linked);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadyInvoiced,
                "The document is already linked to an invoice. Nothing was saved.");
        }

        if (string.IsNullOrWhiteSpace(document.GetAttributeValue<string>("sprk_graphdriveid"))
            || string.IsNullOrWhiteSpace(document.GetAttributeValue<string>("sprk_graphitemid")))
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.DocumentHasNoFile,
                "The document has no stored file, so it cannot be submitted as an invoice.");
        }

        // D-140 (owner 2026-10-20, "follow the email's matter"): the document's EFFECTIVE core record, through its filing chain.
        IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)>? filed;
        try
        {
            filed = await ResolveFilingAsync(documentId, document, ct);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Invoice submission refused: the filing chain of document {DocumentId} could not be read", documentId);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                "The record the document is filed under could not be read right now. Nothing was saved; try again shortly.");
        }

        if (filed is null)
        {
            _logger.LogWarning(
                "Invoice submission refused: the filing chain of document {DocumentId} is longer than {Max} documents or loops; its record is unknown",
                documentId, MaxFilingChainDocuments);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
                "The record the document is filed under could not be established, so it cannot be submitted for this record. Nothing was saved.");
        }

        foreach (var (column, kind, filedUnder) in filed)
        {
            if (kind != core.Kind || filedUnder != core.RecordId)
            {
                // The other record is never named: the caller was authorized against the confirm's record, not that one.
                _logger.LogInformation(
                    "Invoice submission refused: document {DocumentId} is filed under {Column} {FiledUnder}, not {Entity} {RecordId}",
                    documentId, column, filedUnder, core.EntityLogicalName, core.RecordId);
                return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
                    "The document is filed under a different record than the one named, so it cannot be submitted for this record. Nothing was saved.");
            }
        }

        // 050a review round 3 (uac-r2 F1-a): "unfiled" by the D-140 columns is not "not isolated". The document's isolation decides too,
        // through every ownership parent it has (a Secure record's thread, an event, an invoice, ...): a secure root other than this
        // record, or isolation this check cannot place, is refused; a fault is 503. The job decides it again at commit (F1-b).
        return IsolationRefusal(await _isolation.CheckAsync(documentId, core, ct));
    }

    /// <summary>
    /// 050a review round 3: the confirm's answer to an <see cref="ExistingDocumentIsolationCheck"/> verdict (null: allowed). The other record
    /// is never named: the caller was authorized against the confirm's record, not that one.
    /// </summary>
    private static InvoiceSubmissionOutcome? IsolationRefusal(ExistingDocumentIsolation isolation) => isolation.Verdict switch
    {
        ExistingDocumentIsolationVerdict.Allowed => null,
        ExistingDocumentIsolationVerdict.Unavailable => InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable,
            InvoiceSubmissionErrors.CoreRecordUnavailable,
            "Who may see the document could not be checked right now. Nothing was saved; try again shortly."),
        ExistingDocumentIsolationVerdict.OtherSecureRoot => InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict,
            InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
            "The document belongs to a different, restricted record than the one named, so it cannot be submitted for this record. Nothing was saved."),
        _ => InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
            "The document is restricted to a record that could not be established, so it cannot be submitted for this record. Nothing was saved."),
    };

    /// <summary>
    /// D-140: the core records <paramref name="document"/> is EFFECTIVELY filed under, each with the column that names it. In order, the first
    /// that names any core record decides: (1) the document's own core lookups (both families, <see cref="DocumentCoreColumns"/>); (2) else its
    /// communication's (<c>sprk_relatedcommunication</c>) regarding matter / project / work assignment, together with its thread's
    /// (<c>sprk_communicationthread</c>, round 4: the union, with the parents <see cref="RecordThreadFiling.ParentsOf"/> returns for it): an email attachment is
    /// created with only that link, and its isolation follows the communication; (3) else its parent document (<c>sprk_parentdocument</c>),
    /// the same way.
    /// Empty: nothing in the chain is filed (a truly unfiled document, allowed, D-139); a row of the chain that no longer exists files nothing.
    /// Null: the chain is longer than <see cref="MaxFilingChainDocuments"/> documents or loops, so the record is unknown (the caller refuses).
    /// A read fault propagates.
    /// </summary>
    private async Task<IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)>?> ResolveFilingAsync(
        Guid documentId, Entity document, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { documentId };
        var current = document;
        for (var documents = 1; ; documents++)
        {
            var filed = CoreRecordsOf(current, DocumentCoreColumns, DocumentEntity);
            if (filed.Count == 0
                && current.GetAttributeValue<EntityReference>(DocumentRelatedCommunication) is { Id: var communicationId } && communicationId != Guid.Empty
                && await ReadChainRowAsync(CommunicationEntity, communicationId, CommunicationHopColumns, ct) is { } communication)
            {
                filed = CoreRecordsOf(communication, CommunicationCoreColumns, CommunicationEntity);

                // 050a review round 4 (uac-r2 F1): the communication's thread files it too, by the ONE shared rule
                // RecordThreadFiling.ParentsOf (#2143 round 7d), so the filing check and the ownership resolver cannot disagree:
                // the communication's parents are the UNION of its own regarding columns and the parents its thread files its
                // messages under (never "the thread only when the communication names none"). A RECORD thread (and a thread whose
                // type is not set or not known: fail closed) contributes every parent it carries, so a communication regarding M on
                // S's thread is filed under both and is refused for M and for S alike (an invoice from it may only be filed where
                // every parent agrees). A DIRECT thread (1:1, the per-user master) files its messages under nothing whatever
                // lookups it carries, so it contributes nothing. Only a parent that is a core record (matter, project, work
                // assignment) is compared here; any other ownership parent is the isolation check's. A thread that is gone files
                // nothing; a read fault propagates (503).
                if (communication.GetAttributeValue<EntityReference>(CommunicationThread) is { Id: var threadId } && threadId != Guid.Empty
                    && await ReadChainRowAsync(ThreadEntity, threadId, ThreadHopColumns, ct) is { } thread)
                {
                    foreach (var threadParent in RecordThreadFiling.ParentsOf(thread))
                    {
                        if (CoreRecordKindOfTable(threadParent.EntityLogicalName) is { } kind)
                        {
                            filed.Add(($"{ThreadEntity}.{CommunicationCoreColumns.First(c => c.Kind == kind).Column}", kind, threadParent.RecordId));
                        }
                    }
                }
            }

            if (filed.Count > 0)
            {
                return filed;
            }

            if (current.GetAttributeValue<EntityReference>(DocumentParentDocument) is not { Id: var parentId } || parentId == Guid.Empty)
            {
                return filed; // the end of the chain, nothing filed: unfiled
            }

            if (documents >= MaxFilingChainDocuments || !visited.Add(parentId))
            {
                return null;
            }

            if (await ReadChainRowAsync(DocumentEntity, parentId, FilingChainColumns, ct) is not { } parent)
            {
                return filed; // the parent is gone: nothing files the document through it
            }

            current = parent;
        }
    }

    private static List<(string Column, InvoiceCoreRecordKind Kind, Guid Id)> CoreRecordsOf(
        Entity row, (string Column, InvoiceCoreRecordKind Kind)[] columns, string table) =>
        columns
            .Select(c => (Column: $"{table}.{c.Column}", c.Kind, Id: row.GetAttributeValue<EntityReference>(c.Column)?.Id ?? Guid.Empty))
            .Where(c => c.Id != Guid.Empty)
            .ToList();

    /// <summary>A row of the filing chain, or null when it no longer exists. A read fault propagates.</summary>
    private async Task<Entity?> ReadChainRowAsync(string table, Guid id, string[] columns, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(table, id, columns, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <summary>Submits the job (ADR-004, the submission id as idempotency key); false when the queue refused it (logged).</summary>
    private async Task<bool> TrySubmitJobAsync(Guid jobId, Guid submissionId, string? correlationId)
    {
        try
        {
            await _jobs.SubmitJobAsync(BuildJob(jobId, submissionId, correlationId), CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId}: its job {JobId} could not be queued; a retry of the confirm queues it", submissionId, jobId);
            return false;
        }
    }

    private static InvoiceSubmissionOutcome DocumentAlreadySubmitted() =>
        InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentAlreadySubmitted,
            "The document was already confirmed as an invoice with a different firm, record or invoice number. Nothing was saved.");

    /// <summary>
    /// The confirm's submission may have been saved but its job was not queued. Not "nothing saved": a retry of the same confirm finds the
    /// submission and queues it (see <see cref="SubmitExistingDocumentAsync"/>).
    /// </summary>
    private static InvoiceSubmissionOutcome NotQueued() =>
        InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.Failed,
            "The invoice could not be queued for processing right now. Retry the confirmation; it completes the same submission.");

    /// <summary>What <see cref="DecideAsync"/> found: a refusal, or the submission row (core lookup and owner set), its resolver fields and owner.</summary>
    private sealed record IntakeDecision(
        InvoiceSubmissionOutcome? Refusal, Entity? Row = null, InvoiceFamilyResolverFields? ResolverFields = null, RecordOwnerResolution? Owner = null);

    /// <summary>
    /// The checks every entry runs before it writes (D-24: the same checks on every channel; D-134: the existing-document entry too), in this
    /// order: the firm is active, the core record's fields derive (013), the firm is one of the record's assigned law firms (D-106 / D-110), and
    /// an owner team resolves. Reads only; nothing is written.
    /// </summary>
    private async Task<IntakeDecision> DecideAsync(InvoiceSubmissionRequest request, CallerPrincipal? spaCaller, CancellationToken ct)
    {
        var core = request.CoreRecord;
        switch (await IsActiveFirmAsync(request.FirmId, ct))
        {
            case null:
                return new(InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.FirmUnverifiable,
                    "The firm could not be checked right now. Try again shortly."));
            case false:
                return new(InvoiceSubmissionOutcome.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid,
                    "The firm was not found or is not active."));
        }

        var row = new Entity(SubmissionEntity);
        row[core.SubmissionColumn] = new EntityReference(core.EntityLogicalName, core.RecordId);
        var decided = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            InvoiceFamilyCoreRecordGuard.Check(row), _entities, _recordTypes.QueryRecordTypeRefAsync, ct);
        if (decided.IsRefused || decided.ResolverFields is null)
        {
            _logger.LogWarning("Invoice submission refused: the core record's fields could not be derived ({Code})", decided.RefusalCode);
            return new(decided.RefusalCode == InvoiceFamilyCoreRecordGuard.ReadFailedCode
                ? InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                    "The record could not be read right now. Try again shortly.")
                : InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.CoreRecordUnavailable,
                    "The record cannot take an invoice submission (it is missing or incomplete)."));
        }

        // D-106 / D-110: the firm must be one of the core record's assigned law firms, on EVERY channel (the internal route names its firm in
        // the query and is held to the same rule as an external contact). After the record's own fields (a missing record is 422 above) and
        // before the owner is resolved and long before any write; the same codes whichever route called. The order matters: the link reader
        // cannot tell a record that is not returned (deleted, not found) from a fault, so both come back unreadable, and asking it first would
        // answer a deleted record 503 "try again shortly". The detail names no firm and no record.
        switch (await CheckFirmLinkAsync(core, request.FirmId, ct))
        {
            case InvoiceFirmLink.Linked:
                break;
            case InvoiceFirmLink.NotLinked:
                _logger.LogInformation(
                    "Invoice submission refused: firm {FirmId} is not linked to {Entity} {RecordId} ({Channel})",
                    request.FirmId, core.EntityLogicalName, core.RecordId, request.Channel);
                return new(InvoiceSubmissionOutcome.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.FirmNotLinked,
                    "The firm you chose is not linked to this record, so an invoice cannot be submitted for it."));
            default:
                return new(InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.FirmUnverifiable,
                    "The firm could not be checked right now. Try again shortly."));
        }

        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new(core.EntityLogicalName, core.RecordId) }) with
            {
                RequestedBy = RecordRequester.Of(systemUserId: spaCaller?.SystemUserId, objectId: request.CallerObjectId),
            },
            ct);
        if (!owner.IsOwned)
        {
            return new(InvoiceSubmissionOutcome.OwnerRefused(owner));
        }

        // The owner team and the creator person go on the row here, in the member that builds it (RecordOwnerAssignmentCensusTests).
        owner.ApplyTo(row);
        return new(null, row, decided.ResolverFields, owner);
    }

    /// <summary>The container of the core record (D-44: secure when secure; fail closed). Virtual so a test can stand in for the resolver.</summary>
    internal virtual Task<ContainerDecision> ResolveContainerAsync(InvoiceCoreRecord core, CancellationToken ct) =>
        _containers.ResolveForRecordAsync(core.EntityLogicalName, core.RecordId, ct);

    private InvoiceSubmissionOutcome? Validate(InvoiceSubmissionRequest request)
    {
        if (ValidateFields(request) is { } invalid)
        {
            return invalid;
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

    /// <summary>The checks of the request's own fields every entry runs first: the number (D-91), its length, the PO, the firm and record ids.</summary>
    private static InvoiceSubmissionOutcome? ValidateFields(InvoiceSubmissionRequest request)
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
    /// Whether <paramref name="firmId"/> is one of the core record's assigned law firms (<c>sprk_assignedlawfirm1/2</c>), read through the access
    /// model's own record-to-organization read (<see cref="ExternalParticipationService.GetReferencedOrganizationIdsAsync"/>, one batched query
    /// that selects BOTH lookups). The ONE definition of "linked" for the submit gate (this service, and the external filter's early refusal);
    /// the picker route applies <see cref="LinkOf"/> to its batched answer, so the list it offers is exactly what a submit accepts. A fault, a
    /// missing row or an incomplete answer is <see cref="InvoiceFirmLink.Unreadable"/>, never "linked" and never a final "not linked". The reader
    /// cannot tell a record that is not returned (deleted) from a fault, so a caller that must tell them apart reads the record first: <see cref="SubmitAsync"/>
    /// derives the record's fields (a missing record is its 422) before it asks for the link.
    /// </summary>
    public async Task<InvoiceFirmLink> CheckFirmLinkAsync(InvoiceCoreRecord core, Guid firmId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(core);
        try
        {
            var references = await _participations.GetReferencedOrganizationIdsAsync(core.EntityLogicalName, new[] { core.RecordId }, ct);
            return LinkOf(references is not null && references.TryGetValue(core.RecordId, out var record) ? record : null, firmId);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "The assigned firms of {Entity} {RecordId} could not be read; failing closed", core.EntityLogicalName, core.RecordId);
            return InvoiceFirmLink.Unreadable;
        }
    }

    /// <summary>
    /// The pure rule behind <see cref="CheckFirmLinkAsync"/>: an answer that is missing, flagged unreadable or empty-handed (a defaulted
    /// struct) is <see cref="InvoiceFirmLink.Unreadable"/>; an empty firm id never matches; a firm links only when it is among the record's
    /// organizations. An empty lookup slot is simply absent from <see cref="ReferencedOrganizations.OrganizationIds"/>, so a null never
    /// counts as a match.
    /// </summary>
    internal static InvoiceFirmLink LinkOf(ReferencedOrganizations? read, Guid firmId)
    {
        if (read is not { Unreadable: false, OrganizationIds: { } organizations })
        {
            return InvoiceFirmLink.Unreadable;
        }

        return firmId != Guid.Empty && organizations.Contains(firmId) ? InvoiceFirmLink.Linked : InvoiceFirmLink.NotLinked;
    }

    /// <summary>
    /// The id and display name of each ACTIVE firm among <paramref name="firmIds"/>, in the order given (ebilling-r1 task 051: the firm step's
    /// list on <c>GET /api/v1/external/me</c>). A missing or inactive firm is left out (a submission for it is refused anyway, see
    /// <see cref="IsActiveFirmAsync"/>); a firm with no name is listed under its id. Returns <see langword="null"/> when any read faulted (a
    /// fault is never "no firm"). Reads only the ids it is given, so the caller's own firms are the only ones that can come back.
    /// </summary>
    public async Task<IReadOnlyList<(Guid FirmId, string Name)>?> ReadActiveFirmNamesAsync(IReadOnlyCollection<Guid> firmIds, CancellationToken ct)
    {
        var wanted = firmIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return Array.Empty<(Guid FirmId, string Name)>();
        }

        // ONE query for all the caller's firms (they are few; the bound keeps the IN list well inside Dataverse's limits). The organization's
        // primary name is sprk_organizationname: sprk_name does not exist on that table, and selecting it is a 400 (RegardingNameFields).
        var nameColumn = RegardingNameFields.PrimaryNameField(OrganizationEntity)
            ?? throw new InvalidOperationException("No primary name column is known for sprk_organization.");
        var found = new Dictionary<Guid, string>();
        try
        {
            foreach (var chunk in wanted.Chunk(MaxInValues))
            {
                var query = new QueryExpression(OrganizationEntity) { ColumnSet = new ColumnSet("statecode", nameColumn) };
                query.Criteria.AddCondition("sprk_organizationid", ConditionOperator.In, chunk.Cast<object>().ToArray());
                query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
                foreach (var firm in (await _entities.RetrieveMultipleAsync(query, ct)).Entities)
                {
                    var name = firm.GetAttributeValue<string>(nameColumn);
                    found[firm.Id] = string.IsNullOrWhiteSpace(name) ? firm.Id.ToString() : name;
                }
            }
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Firm list unavailable: the firms could not be read");
            return null;
        }

        // In the order given; a missing or inactive firm (not returned) is not offered.
        return wanted.Where(found.ContainsKey).Select(id => (id, found[id])).ToList();
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
            InvoiceId = row.GetAttributeValue<EntityReference>(InvoiceColumn) is { Id: var invoiceId } && invoiceId != Guid.Empty
                ? invoiceId
                : null,
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
