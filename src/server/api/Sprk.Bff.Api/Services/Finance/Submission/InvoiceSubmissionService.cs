using System.Globalization;
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
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The one server-side invoice intake (ebilling-r1 FR-17, Processing Pipeline step 0, ADR-002): validates the request, stages the PDF in the
/// Finance file store (task 092: <see cref="StoredFileService.StageAsync"/> under <see cref="StoredFileIds.ForUpload"/> of the submission, parent
/// the invoice the job will create), creates the <c>sprk_invoicesubmission</c> (Received, with its tracking number) and enqueues
/// <see cref="JobType"/>. Both entry points call it: the external SPA route and the internal route (D-24: the same checks).
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
/// <para><b>Order:</b> every check and read first (number, file, firm, core-record fields, owner), then the bytes, then the submission row, then
/// the job. A failure after the stage is compensated in reverse order with its own token (the submission row, then the staged blob through
/// <see cref="StoredFileService.DeleteStagedAsync"/>), so it leaves no Received submission behind (acceptance criterion 7). A compensation that
/// itself fails is logged and never masks the original failure.</para>
/// <para><b>The file store, not SharePoint Embedded (ebilling-r1 task 092; procedure-and-file-model 9.1, D-44 second half).</b> No
/// <c>sprk_document</c> is created and the submission names none: the bytes are staged as a blob only. The job (049) reads the staged blob
/// (<see cref="StoredFileService.OpenStagedForProcessingAsync"/>), writes the <c>sprk_storedfile</c> row inside its commit transaction (ADR-002:
/// the row's one writer is that transaction) and flips it to Servable before its after-commit steps. The invoice PDF therefore no longer
/// appears among the core record's Documents; the review reads it by its Stored File id.</para>
/// <para><b>The writes ignore the caller's token.</b> From the stage to the enqueue every call runs under <see cref="CancellationToken.None"/>:
/// Blob storage, Dataverse and Service Bus finish a request they received even when the caller abandons it, and the abandoned call's reply is
/// lost. A client that disconnects mid-write therefore gets a complete submission it never hears about, not orphans.
/// A caller already gone before the first write writes nothing.</para>
/// <para><b>Only creator of submissions.</b> The row is created by <see cref="ITrackingNumberAllocator"/> with its number already set (D-87); the
/// submission id is minted here once per request and is also the job's idempotency key. This service never creates <c>sprk_invoice</c> (task 049
/// does).</para>
/// <para><b>Submit Invoice from a source document (ebilling-r1 050a, D-134; task 094, procedure-and-file-model 9.1).</b> The review-confirm
/// route and the from-document route call <see cref="SubmitExistingDocumentAsync"/>: the same field, firm, core-record, firm-link and owner
/// checks (<see cref="DecideAsync"/>), no file checks, no upload and no document create; the submission names the SOURCE document, its id
/// is derived from the document (idempotent by document), and the job COPIES the document's bytes into the invoice's Stored File (9.1
/// steps 2-6). The source document never moves: nothing here or in the job writes its owner, its shares or its <c>sprk_invoice</c> link
/// (the 050a re-file is retired).</para>
/// <para><b>Logging (ADR-015).</b> Ids and outcomes only; the firm invoice number, the tracking number and the file name are business data and are
/// not logged above Debug.</para>
/// </remarks>
public class InvoiceSubmissionService
{
    /// <summary>The ADR-004 job type of the processing job (handled by task 049).</summary>
    public const string JobType = "InvoiceSubmissionProcessing";

    internal const string PdfContentType = "application/pdf";
    internal const int MaxPoNumberLength = 100;
    internal const int MaxListSize = 50;

    /// <summary>Values per <c>In</c> condition in the list read (the RateCardImportService bound).</summary>
    internal const int MaxInValues = 500;

    internal const string SubmissionEntity = TrackingNumberAllocator.SubmissionEntity;
    internal const string OrganizationEntity = "sprk_organization";
    internal const string DocumentEntity = "sprk_document";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    /// <summary>Review round 5 (pass-2 F1-1): the detail of <see cref="InvoiceSubmissionErrors.EarlierAttemptBeingRemoved"/> on both routes.</summary>
    internal const string EarlierAttemptBeingRemovedDetail =
        "An earlier attempt to submit this invoice failed and is still being removed. Nothing was saved; try again shortly.";

    private static readonly string[] ViewColumns =
    {
        TrackingNumberAllocator.TrackingNumberColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn, TrackingNumberAllocator.FirmColumn,
        "sprk_ponumber", "sprk_processingstatus", "sprk_channel", "sprk_receivedon", "sprk_outcomereasoncode", "sprk_document",
        "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment", "createdon",
        // ebilling-r1 task 098: the resulting invoice, so the external routes can apply (a') to the submission (a derived row).
        InvoiceColumn,
        // ebilling-r1 task 097b: a Correction's target, so a from-document retry is matched on it (never serialized).
        ResubmissionOfColumn,
    };

    /// <summary>The resulting invoice of an accepted submission (<c>sprk_invoicesubmission.sprk_invoice</c>).</summary>
    internal const string InvoiceColumn = "sprk_invoice";

    /// <summary>
    /// ebilling-r1 task 097b (EB-D-179 item 1): <c>sprk_invoicesubmission.sprk_resubmissionof</c>, the earlier submission a Correction
    /// replaces (task 020 created the lookup; this task is its first writer). The replaced invoice is that submission's <see cref="InvoiceColumn"/>,
    /// which must be <see cref="InvoiceSubmissionPersister.InvoiceIdFor"/> of it (<see cref="SubmissionSnapshot.ReadCorrectedInvoiceAsync"/>).
    /// It is written only at create, after <see cref="CheckCorrectionAsync"/> found the earlier submission under the SAME core record, so the
    /// peer edge <c>SecureChildLineage</c> lists for it (sprk_invoicesubmission is not an ownership parent) reaches no other root.
    /// </summary>
    internal const string ResubmissionOfColumn = "sprk_resubmissionof";

    /// <summary>
    /// ebilling-r1 task 097b: what the Correction guard reads of the old invoice (app-only, after the caller's own Write passed): its firm,
    /// its core lookups (<see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>) and its status pair.
    /// </summary>
    internal static readonly string[] CorrectionInvoiceColumns =
    [
        InvoiceSubmissionPersister.InvoiceVendorOrg, "statecode", "statuscode",
        .. InvoiceFamilyCoreRecordGuard.CoreRecordLookups[FinanceBaseValues.InvoiceEntity].Select(l => l.Column),
    ];

    private static readonly string[] CorrectionSubmissionColumns =
    [
        InvoiceColumn, TrackingNumberAllocator.FirmColumn, TrackingNumberAllocator.FirmInvoiceNumberColumn,
        "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment",
        // Task 097 second review (F1): whether that submission has finished (CorrectionTargetSubmissionFinished).
        InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode,
    ];

    /// <summary>
    /// Task 097 second review (F1, F2): what the commit re-reads of the replaced invoice's own submission (the one <see cref="ResubmissionOfColumn"/>
    /// names): whether it has finished, and its firm invoice number.
    /// </summary>
    internal static readonly string[] CorrectionTargetSubmissionColumns =
    [
        InvoiceSubmissionPersister.SubmissionStatus, InvoiceSubmissionPersister.SubmissionReasonCode, TrackingNumberAllocator.FirmInvoiceNumberColumn,
    ];

    /// <summary>
    /// Task 097 second review (F1): has the replaced invoice's own <paramref name="submission"/> finished, so that a correction may supersede its
    /// invoice? Only Accepted (by processing, by an override or uncoded) or Rejected and held Blocked (D-101: the invoice In Review awaiting
    /// Override or Return; <see cref="FinanceBaseValues.HeldSubmissionConditions"/>). Received, Processing (a Blocked one included: its
    /// after-steps are still running), Failed (its undo may be unfinished), Rejected with any other code, and an unknown or unread status are not.
    /// </summary>
    internal static bool CorrectionTargetSubmissionFinished(Entity submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var status = submission.GetAttributeValue<OptionSetValue>(InvoiceSubmissionPersister.SubmissionStatus)?.Value;
        return status == (int)InvoiceSubmissionStatus.Accepted
            || (status == (int)InvoiceSubmissionStatus.Rejected
                && string.Equals(submission.GetAttributeValue<string>(InvoiceSubmissionPersister.SubmissionReasonCode), SubmissionReasonCodes.Blocked, StringComparison.Ordinal));
    }

    /// <summary>
    /// Task 097b / second review (F2), EB-D-179 item 3, D-37: the same firm invoice number, trimmed and ignoring case, as the tracking sequence
    /// keys it. A missing number on either side is not the same.
    /// </summary>
    internal static bool SameFirmInvoiceNumber(string? earlier, string? submitted) =>
        earlier is not null && submitted is not null && string.Equals(earlier.Trim(), submitted.Trim(), StringComparison.OrdinalIgnoreCase);

    private readonly ITrackingNumberAllocator _allocator;
    private readonly IGenericEntityService _entities;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly ExternalParticipationService _participations;
    private readonly StoredFileService _files;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly ExistingDocumentIsolationCheck _isolation;
    private readonly JobSubmissionService _jobs;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<InvoiceSubmissionService> _logger;

    public InvoiceSubmissionService(
        ITrackingNumberAllocator allocator,
        IGenericEntityService entities,
        ICommunicationDataverseService recordTypes,
        ExternalParticipationService participations,
        StoredFileService files,
        IRecordOwnershipResolver ownership,
        ExistingDocumentIsolationCheck isolation,
        JobSubmissionService jobs,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionService> logger)
    {
        _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _participations = participations ?? throw new ArgumentNullException(nameof(participations));
        _files = files ?? throw new ArgumentNullException(nameof(files));
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

        return await WriteAsync(request, number, file, decision.Row!, decision.ResolverFields!, ct);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Submit an existing document (ebilling-r1 task 050a, D-134: the review-confirm route)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The submission id of the confirm of <paramref name="documentId"/>: deterministic, so the document has exactly one confirm submission.
    /// It is the dedupe key of <see cref="SubmitExistingDocumentAsync"/> (D-134 "idempotent by document"; kept by task 094).
    /// </summary>
    public static Guid ExistingDocumentSubmissionIdFor(Guid documentId) => InvoiceSubmissionPersister.IdOf($"ebilling-confirm-submission-{documentId:D}");

    /// <summary>The processing job id of that submission: deterministic too, so a retry answers with the same job id it was first given.</summary>
    public static Guid ExistingDocumentJobIdFor(Guid documentId) => InvoiceSubmissionPersister.IdOf($"ebilling-confirm-job-{documentId:D}");

    /// <summary>
    /// EB-D-167 (owner 2026-10-28; closes ISS-012): the most attempts one document's confirm can make (the first and every new attempt
    /// after a Failed one), the from-email route's bound (<see cref="FromEmailSubmissionKey.MaxGeneration"/>).
    /// </summary>
    internal const int MaxExistingDocumentGeneration = FromEmailSubmissionKey.MaxGeneration;

    /// <summary>
    /// EB-D-167: the submission id of the <paramref name="generation"/>-th attempt to confirm <paramref name="documentId"/>. Generation 1 is
    /// <see cref="ExistingDocumentSubmissionIdFor(Guid)"/> unchanged (every existing row keeps its id); a later one is derived from the
    /// document and the generation, so one attempt per generation exists and two concurrent retries meet on the same id.
    /// </summary>
    public static Guid ExistingDocumentSubmissionIdFor(Guid documentId, int generation) => generation <= 1
        ? ExistingDocumentSubmissionIdFor(documentId)
        : InvoiceSubmissionPersister.IdOf(string.Create(CultureInfo.InvariantCulture, $"ebilling-confirm-submission-{documentId:D}-attempt-{generation}"));

    /// <summary>EB-D-167: the processing job id of that attempt (generation 1 unchanged).</summary>
    public static Guid ExistingDocumentJobIdFor(Guid documentId, int generation) => generation <= 1
        ? ExistingDocumentJobIdFor(documentId)
        : InvoiceSubmissionPersister.IdOf(string.Create(CultureInfo.InvariantCulture, $"ebilling-confirm-job-{documentId:D}-attempt-{generation}"));

    /// <summary>
    /// EB-D-167 (fix round F1-1 kept): true when <paramref name="submissionId"/> is one of the ids the intake derives for
    /// <paramref name="documentId"/>'s attempts. A document is a source only for a submission created FOR it.
    /// </summary>
    internal static bool IsExistingDocumentSubmissionIdOf(Guid documentId, Guid submissionId)
    {
        for (var generation = 1; generation <= MaxExistingDocumentGeneration; generation++)
        {
            if (ExistingDocumentSubmissionIdFor(documentId, generation) == submissionId)
            {
                return true;
            }
        }

        return false;
    }

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

    /// <summary>D-140: the columns a document of the filing chain is read with (task 094: shared by step 1, step 3 and step 5).</summary>
    internal static readonly string[] FilingChainColumns =
        [.. DocumentCoreColumns.Select(c => c.Column), DocumentRelatedCommunication, DocumentParentDocument];

    /// <summary>Task 094: the source document's own Restricted state (9.1 step 3: read, never a refusal ground, never stored).</summary>
    private const string DocumentAccessPermission = InheritedAccessPermission.Column;

    /// <summary>Task 094: the source's communication flags 9.1 step 3 reads (internal-only; the thread's privacy).</summary>
    private const string CommunicationInternalOnly = Sprk.Bff.Api.Services.Communication.CommunicationInternalOnlyFloor.InternalOnlyField;

    private const string ThreadPrivacy = Sprk.Bff.Api.Services.Communication.CommunicationInternalOnlyFloor.ThreadPrivacyField;

    private static readonly string[] ExistingDocumentColumns =
        ["sprk_invoice", "sprk_graphdriveid", "sprk_graphitemid", InvoiceReviewService.DocInvoiceReviewStatus, .. FilingChainColumns];

    /// <summary>Task 094: the source document as steps 3 and 5 read it: the filing chain and its own Restricted state.</summary>
    private static readonly string[] SourceCheckColumns = [.. FilingChainColumns, DocumentAccessPermission];

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

        // ── 2. The latest attempt for this document, if any: the retry's answer. EB-D-167 (owner 2026-10-28): the attempts are walked by
        //       their derived ids while each one ended Failed; after a Failed one this request is a NEW attempt (the next generation,
        //       created by this caller, through every check below), whoever made the failed one. Rejected keeps D-139 (409). ──
        InvoiceSubmissionView? earlier = null;
        var earlierGeneration = 0;
        try
        {
            for (var g = 1; g <= MaxExistingDocumentGeneration; g++)
            {
                if (await GetAsync(ExistingDocumentSubmissionIdFor(documentId, g), ct) is not { } attempt)
                {
                    break;
                }

                (earlier, earlierGeneration) = (attempt, g);
                if (attempt.Status != nameof(InvoiceSubmissionStatus.Failed))
                {
                    break;
                }
            }
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionOutcome.Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title);
        }

        if (earlier is not null && earlier.Status != nameof(InvoiceSubmissionStatus.Failed))
        {
            return await AnswerWithEarlierAsync(
                request, number, earlier, documentId, ExistingDocumentJobIdFor(documentId, earlierGeneration), request.FileSourceBeforeWrite, ct);
        }

        // Review round 5 (pass-2 F1-1): no new attempt while the latest Failed one's undo is unfinished (its invoice still linked).
        if (earlier is not null
            && SubmissionCommitGate.UndoUnfinished(InvoiceSubmissionStatus.Failed, earlier.OutcomeReasonCode, earlier.InvoiceId))
        {
            _logger.LogInformation("Invoice submission {EarlierId} of document {DocumentId} ended Failed and its undo is unfinished; refused (503)",
                earlier.SubmissionId, documentId);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.EarlierAttemptBeingRemoved,
                EarlierAttemptBeingRemovedDetail);
        }

        var generation = earlierGeneration + 1;
        if (generation > MaxExistingDocumentGeneration)
        {
            _logger.LogInformation("Invoice submission: document {DocumentId} reached {Max} attempts; refused (409)", documentId, MaxExistingDocumentGeneration);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.SubmissionAlreadyEnded,
                "The document was already submitted as an invoice too many times. Nothing was saved.");
        }

        var submissionId = ExistingDocumentSubmissionIdFor(documentId, generation);
        var jobId = ExistingDocumentJobIdFor(documentId, generation);
        if (earlier is not null)
        {
            _logger.LogInformation(
                "Invoice submission {EarlierId} of document {DocumentId} ended Failed; this request is attempt {Generation} ({SubmissionId})",
                earlier.SubmissionId, documentId, generation, submissionId);
        }

        // ── 3. The document: present, holding a file, not already an invoice's, not rejected, linking the route's communication, not filed
        //       under another core record (D-140) and not isolated elsewhere ──
        if (await CheckExistingDocumentAsync(documentId, request.CoreRecord, request.SourceCommunicationId, ct) is { } documentRefusal)
        {
            return documentRefusal;
        }

        // ── 4. The same checks as an upload (firm, record, firm link, owner) ──
        var decision = await DecideAsync(request, spaCaller: null, ct);
        if (decision.Refusal is { } refused)
        {
            return refused;
        }

        // ── 4a. Task 097b: a Correction's own checks (the invoice's firm, core record, status, submission and number), read-only, before
        //        step A and the create. Only a NEW attempt reaches here: a retry was answered by its earlier submission above. ──
        Guid? resubmissionOf = null;
        if (request.CorrectedInvoice is { } correctedInvoice)
        {
            var (correctionRefusal, target) = await CheckCorrectionAsync(request.CoreRecord, request.FirmId, number, correctedInvoice, ct);
            if (correctionRefusal is not null)
            {
                return correctionRefusal;
            }

            resubmissionOf = target;
        }

        // ── 4b. Step A (fix round F1-b): the route's filing of an unfiled source communication, as the caller - only now, after every
        //        read-only refusal above, so a refused submission never leaves a filing behind. Its refusal throws (the route answers). ──
        var filed = request.FileSourceBeforeWrite is { } fileSource && await fileSource(ct);

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
        if (resubmissionOf is { } earlierSubmission)
        {
            row[ResubmissionOfColumn] = new EntityReference(SubmissionEntity, earlierSubmission);
        }

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
            return AfterFiling(DocumentAlreadySubmitted(), filed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId}: the confirm of document {DocumentId} could not be created", submissionId, documentId);
            return AfterFiling(NotQueued(), filed);
        }

        if (!allocation.Succeeded)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: no tracking number could be allocated ({Reason})", submissionId, allocation.Reason);
            return AfterFiling(InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
                "A tracking number could not be assigned right now, so nothing was saved. Try again shortly."), filed);
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
                return AfterFiling(InvoiceSubmissionOutcome.Refused(ex.StatusCode, ex.Code, ex.Detail ?? ex.Title), filed);
            }

            if (concurrent is null)
            {
                // Created, then gone before it could be read (a delete outside this path): nothing to answer with; a retry decides again.
                _logger.LogWarning("Invoice submission {SubmissionId}: already created for document {DocumentId} but no longer readable", submissionId, documentId);
                return AfterFiling(NotQueued(), filed);
            }

            // Step A already ran above for this request: the concurrent row's answer never runs it again.
            return AfterFiling(await AnswerWithEarlierAsync(request, number, concurrent, documentId, jobId, fileSource: null, write), filed);
        }

        if (!await TrySubmitJobAsync(jobId, submissionId, request.CorrelationId))
        {
            return AfterFiling(NotQueued(), filed);
        }

        _logger.LogInformation(
            "Invoice submission {SubmissionId} received for existing document {DocumentId} ({Outcome}) for {Entity} {RecordId}; job {JobId} enqueued",
            submissionId, documentId, allocation.Outcome, request.CoreRecord.EntityLogicalName, request.CoreRecord.RecordId, jobId);
        return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(submissionId, jobId, allocation.TrackingNumber!, documentId));
    }

    /// <summary>
    /// A retry (or a concurrent create, review #5): the document's confirm submission exists. The same firm, record and firm invoice number get
    /// it back (re-queued while it is still Received); anything else is 409 and names nothing about it. One that already ended Failed or
    /// Rejected is 409 <see cref="InvoiceSubmissionErrors.SubmissionAlreadyEnded"/>, and the caller does not re-mark the document (review #4;
    /// D-139). A new attempt after Failed is EB-D-167 (<see cref="SubmitExistingDocumentAsync"/> step 2), not an answer from here.
    /// </summary>
    /// <remarks>Fix round F1-b: <paramref name="fileSource"/> (step A, the retry's own request) runs only once the answer is known to be the
    /// earlier submission itself (the same details, not ended), never before a refusal.</remarks>
    private async Task<InvoiceSubmissionOutcome> AnswerWithEarlierAsync(
        InvoiceSubmissionRequest request, string number, InvoiceSubmissionView earlier, Guid documentId, Guid jobId,
        Func<CancellationToken, Task<bool>>? fileSource, CancellationToken ct)
    {
        // Task 094: an undone submission (9.1 step 5) has its firm invoice number cleared, so for a terminal one an empty number matches.
        var ended = earlier.Status is nameof(InvoiceSubmissionStatus.Failed) or nameof(InvoiceSubmissionStatus.Rejected);
        var same = earlier.FirmId == request.FirmId
            && earlier.CoreRecord == request.CoreRecord
            && earlier.DocumentId == documentId
            && (string.Equals(earlier.FirmInvoiceNumber?.Trim(), number, StringComparison.OrdinalIgnoreCase)
                || (ended && string.IsNullOrWhiteSpace(earlier.FirmInvoiceNumber)))
            && SameCorrectionTarget(earlier.ResubmissionOfId, request.CorrectedInvoice?.Id)
            && !string.IsNullOrEmpty(earlier.TrackingNumber);
        if (!same)
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: document {DocumentId} was already confirmed with other details; refused (409)",
                earlier.SubmissionId, documentId);
            return DocumentAlreadySubmitted();
        }

        if (earlier.Status is nameof(InvoiceSubmissionStatus.Failed) or nameof(InvoiceSubmissionStatus.Rejected))
        {
            // D-139 after Rejected. Failed reaches here only as the answer to a concurrent create that has already failed (step 2 starts a
            // new attempt after a Failed one, EB-D-167): submitting again starts the next attempt.
            _logger.LogInformation("Invoice submission {SubmissionId}: the confirm of document {DocumentId} already ended {Status}; refused (409)",
                earlier.SubmissionId, documentId, earlier.Status);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.SubmissionAlreadyEnded,
                earlier.Status == nameof(InvoiceSubmissionStatus.Rejected)
                    ? "The document was already submitted as an invoice and that submission was rejected. Nothing was saved."
                    : "The document's latest submission as an invoice failed. Nothing was saved; submit it again to start a new attempt.");
        }

        var filed = fileSource is not null && await fileSource(ct);
        if (earlier.Status == nameof(InvoiceSubmissionStatus.Received))
        {
            ct.ThrowIfCancellationRequested();
            if (!await TrySubmitJobAsync(jobId, earlier.SubmissionId, request.CorrelationId))
            {
                return AfterFiling(NotQueued(), filed);
            }
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: the confirm of document {DocumentId} was retried; answering with it ({Status})",
            earlier.SubmissionId, documentId, earlier.Status);
        return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(earlier.SubmissionId, jobId, earlier.TrackingNumber!, documentId));
    }

    /// <summary>
    /// Task 097b: whether an earlier submission's Correction target (<see cref="InvoiceSubmissionView.ResubmissionOfId"/>, the replaced
    /// submission) is the one this request names (its old invoice, <paramref name="requestInvoiceId"/>): both none, or the same invoice by
    /// <see cref="InvoiceSubmissionPersister.InvoiceIdFor"/> (the guard records only a submission whose id derives its invoice).
    /// </summary>
    internal static bool SameCorrectionTarget(Guid? earlierResubmissionOf, Guid? requestInvoiceId) => earlierResubmissionOf is { } earlier
        ? requestInvoiceId is { } invoice && InvoiceSubmissionPersister.InvoiceIdFor(earlier) == invoice
        : requestInvoiceId is null;

    /// <summary>
    /// Null when the document can be submitted; otherwise the refusal. A read fault is 503 (never "not found"). A document filed under a core
    /// record other than <paramref name="core"/> is 409 <see cref="InvoiceSubmissionErrors.DocumentCoreRecordMismatch"/> (review #3), where
    /// "filed under" is the EFFECTIVE core record through the filing chain (<see cref="ResolveFilingAsync"/>, D-140: an email attachment
    /// follows its communication's matter); an UNFILED document (nothing in the chain names a core record) is allowed (owner 2026-10-20, D-139).
    /// </summary>
    private async Task<InvoiceSubmissionOutcome?> CheckExistingDocumentAsync(
        Guid documentId, InvoiceCoreRecord core, Guid? sourceCommunicationId, CancellationToken ct)
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

        // Task 094: a document rejected as not an invoice is refused up front on every route (the confirm route also refuses it itself);
        // the job refuses a reject that lands later (InvoiceSubmissionPersister.DocumentRejectedCode).
        if (document.GetAttributeValue<OptionSetValue>(InvoiceReviewService.DocInvoiceReviewStatus)?.Value == InvoiceReviewService.ReviewStatusRejectedNotInvoice)
        {
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentRejected,
                "The document was rejected as not an invoice, so it cannot be submitted. Nothing was saved.");
        }

        // Task 094: the from-document route is keyed on (communication, document): the document must link exactly that communication.
        if (sourceCommunicationId is { } named
            && document.GetAttributeValue<EntityReference>(DocumentRelatedCommunication)?.Id != named)
        {
            _logger.LogInformation("Invoice submission refused: document {DocumentId} does not link communication {CommunicationId}", documentId, named);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.SourceCommunicationMismatch,
                "The document does not belong to the communication named, so it cannot be submitted from it. Nothing was saved.");
        }

        // D-140 (owner 2026-10-20, "follow the email's matter"): the document's EFFECTIVE core record, through its filing chain.
        IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)>? filed;
        try
        {
            filed = await ResolveFilingAsync(_entities, documentId, document, ct);
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
    /// <remarks>Task 094: static over the caller's reader, so step 1 (here), step 3 (the persister) and step 5 (<see cref="SubmissionCommitGate"/>)
    /// run the one chain (uac-r2 F1-f).</remarks>
    internal static async Task<IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)>?> ResolveFilingAsync(
        IGenericEntityService entities, Guid documentId, Entity document, CancellationToken ct)
    {
        var visited = new HashSet<Guid> { documentId };
        var current = document;
        for (var documents = 1; ; documents++)
        {
            var filed = CoreRecordsOf(current, DocumentCoreColumns, DocumentEntity);
            if (filed.Count == 0
                && current.GetAttributeValue<EntityReference>(DocumentRelatedCommunication) is { Id: var communicationId } && communicationId != Guid.Empty
                && await ReadChainRowAsync(entities, CommunicationEntity, communicationId, CommunicationHopColumns, ct) is { } communication)
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
                    && await ReadChainRowAsync(entities, ThreadEntity, threadId, ThreadHopColumns, ct) is { } thread)
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

            if (await ReadChainRowAsync(entities, DocumentEntity, parentId, FilingChainColumns, ct) is not { } parent)
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

    /// <summary>
    /// ebilling-r1 task 094 fix round: step A decides "unfiled" from the D-140 chain. True when the communication <paramref name="communicationId"/>
    /// is filed anywhere: any of its own filing columns (every regarding lookup and the resolver pair, <c>ReconciledEmailFiling.FilingColumns</c>,
    /// which include the D-140 core columns) or ANY parent its thread files its messages under (<see cref="RecordThreadFiling.ParentsOf"/>,
    /// the D-140 thread hop, every kind). Step A files only a communication for which this is false. A communication that is gone files
    /// nothing (the re-file core then answers it); a thread that cannot be found counts as filed. A read fault propagates.
    /// </summary>
    internal static async Task<bool> IsCommunicationFiledAsync(IGenericEntityService entities, Guid communicationId, CancellationToken ct)
    {
        string[] columns = [.. Services.Office.ReconciledEmailFiling.FilingColumns.Union(CommunicationHopColumns, StringComparer.Ordinal)];
        if (await ReadChainRowAsync(entities, CommunicationEntity, communicationId, columns, ct) is not { } communication)
        {
            return false;
        }

        if (Services.Office.ReconciledEmailFiling.FilingColumns.Any(column => communication.Attributes.TryGetValue(column, out var value) && value switch
            {
                null => false,
                string text => !string.IsNullOrWhiteSpace(text),
                EntityReference reference => reference.Id != Guid.Empty,
                _ => true,
            })
            || CoreRecordsOf(communication, CommunicationCoreColumns, CommunicationEntity).Count > 0)
        {
            return true;
        }

        if (communication.GetAttributeValue<EntityReference>(CommunicationThread) is not { Id: var threadId } || threadId == Guid.Empty)
        {
            return false;
        }

        // The thread with every column (its ownership parents are whatever lookups it carries, not only the core ones D-140 compares).
        // A thread that cannot be found counts as filed: step A never files a message whose thread's filing it cannot see (fail closed).
        try
        {
            return RecordThreadFiling.ParentsOf(await RecordThreadFiling.ReadThreadAsync(entities, threadId, ct).ConfigureAwait(false)).Count > 0;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>A row of the filing chain, or null when it no longer exists. A read fault propagates.</summary>
    private static async Task<Entity?> ReadChainRowAsync(IGenericEntityService entities, string table, Guid id, string[] columns, CancellationToken ct)
    {
        try
        {
            return await entities.RetrieveAsync(table, id, columns, ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Task 094 (procedure-and-file-model 9.1 steps 3 and 5): whether the Submit Invoice source <paramref name="documentId"/> is still filed
    /// under <paramref name="core"/> only, read again. In order: the document (gone is <see cref="SourceFilingVerdict.Gone"/>); the D-140
    /// filing chain (<see cref="ResolveFilingAsync"/>; another record, or a chain that cannot be placed, is
    /// <see cref="SourceFilingVerdict.Mismatch"/>); the source's Restricted, internal-only and privacy state (owner K: never a refusal ground
    /// and never stored, but read, so a source whose state cannot be read is never passed, uac-r2 conditions 1 and 2); then
    /// <see cref="ExistingDocumentIsolationCheck.CheckAsync"/> (another or an undetermined secure root is Mismatch, unreadable is
    /// <see cref="SourceFilingVerdict.Unavailable"/>). A Dataverse fault propagates; the caller decides what a fault means (step 3: a retry,
    /// nothing committed; step 5: a failure).
    /// </summary>
    internal static async Task<SourceFilingCheck> CheckSourceFilingAsync(
        IGenericEntityService entities, ExistingDocumentIsolationCheck isolation, Guid documentId, InvoiceCoreRecord core, CancellationToken ct)
    {
        var document = await ReadChainRowAsync(entities, DocumentEntity, documentId, SourceCheckColumns, ct).ConfigureAwait(false);
        if (document is null)
        {
            return new SourceFilingCheck(SourceFilingVerdict.Gone, "the source document no longer exists");
        }

        var filed = await ResolveFilingAsync(entities, documentId, document, ct).ConfigureAwait(false);
        if (filed is null)
        {
            return new SourceFilingCheck(SourceFilingVerdict.Mismatch, "the filing chain is too long or loops");
        }

        if (filed.FirstOrDefault(f => f.Kind != core.Kind || f.Id != core.RecordId) is { Column: not null } other)
        {
            return new SourceFilingCheck(SourceFilingVerdict.Mismatch, $"filed under {other.Column} {other.Id}");
        }

        var flags = await ReadSourceFlagsAsync(entities, document, ct).ConfigureAwait(false);

        var isolated = await isolation.CheckAsync(documentId, core, ct).ConfigureAwait(false);
        return isolated.Verdict switch
        {
            ExistingDocumentIsolationVerdict.Allowed => new SourceFilingCheck(SourceFilingVerdict.Allowed, null, isolated.Roots, flags),
            ExistingDocumentIsolationVerdict.Unavailable => new SourceFilingCheck(SourceFilingVerdict.Unavailable, isolated.Detail),
            _ => new SourceFilingCheck(SourceFilingVerdict.Mismatch, $"{isolated.Verdict}: {isolated.Detail}"),
        };
    }

    /// <summary>
    /// Task 094 (9.1 step 3, uac-r2 condition 2): reads the source's Restricted state (its own Access Permission, already on
    /// <paramref name="document"/>), its communication's internal-only flag and that communication's thread's privacy state. Under owner K
    /// none of them refuses and none is stored ((a′) derives them at read time, task 098), so the values are returned for the log and the
    /// tests only. A row that is gone has no flag; a read fault propagates (an unreadable flag is never a pass).
    /// </summary>
    internal static async Task<SourceIsolationFlags> ReadSourceFlagsAsync(IGenericEntityService entities, Entity document, CancellationToken ct)
    {
        var restricted = document.GetAttributeValue<OptionSetValue>(DocumentAccessPermission)?.Value == InheritedAccessPermission.Restricted;
        bool? internalOnly = null;
        int? privacy = null;
        if (document.GetAttributeValue<EntityReference>(DocumentRelatedCommunication) is { Id: var communicationId } && communicationId != Guid.Empty
            && await ReadChainRowAsync(entities, CommunicationEntity, communicationId, [CommunicationInternalOnly, CommunicationThread], ct)
                .ConfigureAwait(false) is { } communication)
        {
            internalOnly = communication.GetAttributeValue<bool?>(CommunicationInternalOnly);
            if (communication.GetAttributeValue<EntityReference>(CommunicationThread) is { Id: var threadId } && threadId != Guid.Empty
                && await ReadChainRowAsync(entities, ThreadEntity, threadId, [ThreadPrivacy], ct).ConfigureAwait(false) is { } thread)
            {
                privacy = thread.GetAttributeValue<OptionSetValue>(ThreadPrivacy)?.Value;
            }
        }

        return new SourceIsolationFlags(restricted, internalOnly, privacy);
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

    /// <summary>
    /// Fix round F1-b: a refusal answered AFTER step A filed the source communication must not say "nothing was saved": the filing is kept
    /// (it is the caller's own, made with their rights, and is not undone). Unchanged when nothing was filed or the outcome is Received.
    /// </summary>
    internal static InvoiceSubmissionOutcome AfterFiling(InvoiceSubmissionOutcome outcome, bool filed)
    {
        if (!filed || outcome.Kind == InvoiceSubmissionOutcomeKind.Received)
        {
            return outcome;
        }

        return outcome with { Detail = AfterFilingDetail(outcome.Detail), FilingKept = true };
    }

    /// <summary>
    /// The detail of a refusal after step A: "nothing was saved" restated and <see cref="FilingKeptSentence"/> appended (once). The one
    /// wording for every refusal that follows a filing, whatever its code field (<c>errorCode</c> or <c>reasonCode</c>).
    /// </summary>
    internal static string AfterFilingDetail(string? detail)
    {
        var text = (detail ?? string.Empty)
            .Replace("Nothing was saved", "No submission was saved", StringComparison.Ordinal)
            .Replace("nothing was saved", "no submission was saved", StringComparison.Ordinal)
            .TrimEnd();
        return text.EndsWith(FilingKeptSentence, StringComparison.Ordinal)
            ? text
            : (text.Length == 0 ? string.Empty : text + " ") + FilingKeptSentence;
    }

    /// <summary>Fix round F1-b: what a refusal after step A adds.</summary>
    internal const string FilingKeptSentence = "The communication was filed under the record first; that filing is kept.";

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
    private async Task<IntakeDecision> DecideAsync(
        InvoiceSubmissionRequest request, CallerPrincipal? spaCaller, CancellationToken ct, Guid? requesterSystemUserId = null)
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

        // UAC 194b R3b-2: the submission's question is built from the row as written so far (RecordOwnershipContext.ForChild: its core
        // record under its sprk_regarding{core} lookup, in sprk_invoicesubmission's primary order) — the question the reconciler asks it.
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForChild(row) with
            {
                RequestedBy = RecordRequester.Of(systemUserId: requesterSystemUserId ?? spaCaller?.SystemUserId, objectId: request.CallerObjectId),
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

    /// <summary>
    /// Task 094 (from-email, <see cref="SubmitInvoiceFromEmailOperation"/>): the checks every entry runs before it writes (<see cref="DecideAsync"/>:
    /// the firm active, the core record's fields, the firm link D-106, the owner), with the submission's creator person the caller's own
    /// <paramref name="callerSystemUserId"/> (OP-5; every generation, whoever made an earlier one). Reads only. The answer: a refusal, or the
    /// submission row (core lookup, owner and creator person set) and its resolver fields.
    /// </summary>
    internal async Task<(InvoiceSubmissionOutcome? Refusal, Entity? Row, InvoiceFamilyResolverFields? ResolverFields)> DecideForFromEmailAsync(
        InvoiceSubmissionRequest request, Guid callerSystemUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (callerSystemUserId == Guid.Empty || request.Channel != InvoiceSubmissionChannel.Internal)
        {
            throw new ArgumentException("A from-email submission is internal and names its caller's systemuser.", nameof(request));
        }

        if (ValidateFields(request) is { } invalid)
        {
            return (invalid, null, null);
        }

        var decision = await DecideAsync(request, spaCaller: null, ct, callerSystemUserId);
        return (decision.Refusal, decision.Row, decision.ResolverFields);
    }

    // ── ebilling-r1 task 097b: a Correction (model 9.1 "Existing invoice", EB-D-179) ──

    /// <summary>
    /// ebilling-r1 task 097b (model 9.1 "Existing invoice"; EB-D-179): may <paramref name="invoice"/> be corrected by a submission of
    /// <paramref name="firmId"/> for <paramref name="core"/> carrying <paramref name="firmInvoiceNumber"/>? Reads only; nothing is written.
    /// The caller's own Write on the invoice is the ROUTE's check and has already passed (a caller without it got the uniform not-found
    /// answer and never reaches here); <paramref name="invoice"/> is the route's app-only read of it with <see cref="CorrectionInvoiceColumns"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Order</b> (each a 409 <see cref="InvoiceSubmissionErrors.CorrectionNotAllowed"/> with its <c>reason</c>): the invoice's firm
    /// (<see cref="FromEmailCorrectionReasons.DifferentFirm"/>); its one core record (<see cref="FromEmailCorrectionReasons.DifferentCoreRecord"/>;
    /// an invoice that names none or several is refused the same way, fail closed); its status: only In Review (statecode Active) may be
    /// corrected, so Approved, Sent to AP, Paid and every inactive status, including one this lifecycle does not know, are
    /// <see cref="FromEmailCorrectionReasons.InvoiceStatus"/> with <c>invoiceStatus</c> (the status name; absent for an unknown value);
    /// the invoice's submission (EB-D-179 item 1: the one whose <see cref="InvoiceColumn"/> is the invoice AND whose id derives it,
    /// <see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>; none is <see cref="FromEmailCorrectionReasons.NoSubmission"/>), which must
    /// itself be of the same firm and core record; then the firm invoice number (EB-D-179 item 3, D-37: the submission's typed number, trimmed
    /// and compared ignoring case as the tracking sequence does; else <see cref="FromEmailCorrectionReasons.DifferentInvoiceNumber"/>); then
    /// that submission must have finished (<see cref="CorrectionTargetSubmissionFinished"/>; else
    /// <see cref="FromEmailCorrectionReasons.SubmissionInProgress"/>, task 097 second review F1).</para>
    /// <para>A read fault is 503 <see cref="InvoiceSubmissionErrors.CoreRecordUnavailable"/>, never a guess. No answer names another firm,
    /// record or number. The answer's <c>ResubmissionOf</c> is the submission to record in <see cref="ResubmissionOfColumn"/>.</para>
    /// </remarks>
    internal virtual async Task<(InvoiceSubmissionOutcome? Refusal, Guid? ResubmissionOf)> CheckCorrectionAsync(
        InvoiceCoreRecord core, Guid firmId, string firmInvoiceNumber, Entity invoice, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(invoice);

        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id is not { } invoiceFirm || invoiceFirm != firmId)
        {
            return (CorrectionRefused(invoice.Id, FromEmailCorrectionReasons.DifferentFirm,
                "The invoice to correct is from another firm, so this submission cannot replace it. Nothing was saved."), null);
        }

        if (!IsCore(EligibleApproverResolver.SingleCoreOf(FinanceBaseValues.InvoiceEntity, invoice), core))
        {
            return (DifferentCoreRecord(invoice.Id), null);
        }

        var state = invoice.GetAttributeValue<OptionSetValue>("statecode")?.Value;
        var statusCode = invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value;
        if (state != InvoiceStatusCodes.StateActive || statusCode != InvoiceStatusCodes.InReview)
        {
            var extensions = new Dictionary<string, object?> { ["reason"] = FromEmailCorrectionReasons.InvoiceStatus };
            if (statusCode is { } known && InvoiceStatusCodes.TryFromStatusCode(known, out var status))
            {
                extensions["invoiceStatus"] = status.ToString();
            }

            _logger.LogInformation("Invoice correction refused (409 {Reason}): invoice {InvoiceId} has statecode {State} statuscode {Status}",
                FromEmailCorrectionReasons.InvoiceStatus, invoice.Id, state, statusCode);
            return (InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.CorrectionNotAllowed,
                "Only an invoice in review can be corrected; this one has moved on, so nothing was saved.", extensions), null);
        }

        Entity? submission;
        try
        {
            // No TopCount: a cap could leave the invoice's own submission out when other rows also point at the invoice.
            var query = new QueryExpression(SubmissionEntity) { ColumnSet = new ColumnSet(CorrectionSubmissionColumns) };
            query.Criteria.AddCondition(InvoiceColumn, ConditionOperator.Equal, invoice.Id);
            var found = await _entities.RetrieveMultipleAsync(query, ct);
            // Only the submission the invoice was created from: its id derives the invoice's (the job's InvoiceIdFor). A row that merely points
            // at the invoice is not its submission.
            submission = found.Entities.SingleOrDefault(s => InvoiceSubmissionPersister.InvoiceIdFor(s.Id) == invoice.Id);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, ct))
        {
            _logger.LogWarning(ex, "Invoice correction refused: the submission of invoice {InvoiceId} could not be read", invoice.Id);
            return (InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                "The invoice to correct could not be checked right now. Nothing was saved; try again shortly."), null);
        }

        if (submission is null)
        {
            return (CorrectionRefused(invoice.Id, FromEmailCorrectionReasons.NoSubmission,
                "The invoice to correct has no submission record, so it cannot be corrected. Nothing was saved."), null);
        }

        // The earlier submission is the peer the new one will name (ResubmissionOfColumn): it must be of the same firm and core record, so the
        // lineage edge adds no other root.
        var earlier = ToView(submission);
        if (earlier.FirmId != firmId)
        {
            return (CorrectionRefused(invoice.Id, FromEmailCorrectionReasons.DifferentFirm,
                "The invoice to correct is from another firm, so this submission cannot replace it. Nothing was saved."), null);
        }

        if (earlier.CoreRecord != core)
        {
            return (DifferentCoreRecord(invoice.Id), null);
        }

        if (!SameFirmInvoiceNumber(earlier.FirmInvoiceNumber, firmInvoiceNumber))
        {
            return (CorrectionRefused(invoice.Id, FromEmailCorrectionReasons.DifferentInvoiceNumber,
                "A corrected invoice keeps the firm invoice number of the invoice it replaces; this one has another number, so nothing was saved."), null);
        }

        // Task 097 second review (F1): the invoice's own processing must have finished, or the correction could supersede an invoice whose
        // after-steps (or undo) are still running. Checked again at commit (InvoiceSubmissionPersister).
        if (!CorrectionTargetSubmissionFinished(submission))
        {
            return (CorrectionRefused(invoice.Id, FromEmailCorrectionReasons.SubmissionInProgress,
                "The invoice to correct is still being processed, so it cannot be replaced yet. Nothing was saved; try again once its processing has finished."), null);
        }

        return (null, submission.Id);
    }

    private static bool IsCore((string Entity, Guid Id)? found, InvoiceCoreRecord core) =>
        found is { } single && string.Equals(single.Entity, core.EntityLogicalName, StringComparison.Ordinal) && single.Id == core.RecordId;

    private InvoiceSubmissionOutcome DifferentCoreRecord(Guid invoiceId) =>
        CorrectionRefused(invoiceId, FromEmailCorrectionReasons.DifferentCoreRecord,
            "The invoice to correct is filed under another record, so this submission cannot replace it. Nothing was saved.");

    private InvoiceSubmissionOutcome CorrectionRefused(Guid invoiceId, string reason, string detail)
    {
        _logger.LogInformation("Invoice correction refused (409 {Reason}) for invoice {InvoiceId}", reason, invoiceId);
        return InvoiceSubmissionOutcome.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.CorrectionNotAllowed, detail,
            new Dictionary<string, object?> { ["reason"] = reason });
    }

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

    // ── ebilling-r1 task 095 (from-email route): the same firm and filing rules, reached from FromEmailSubmissionService ──

    /// <summary>Task 095: <see cref="IsActiveFirmAsync"/> for the from-email route (the same rule, one definition).</summary>
    internal virtual Task<bool?> IsActiveFirmForFromEmailAsync(Guid firmId, CancellationToken ct) => IsActiveFirmAsync(firmId, ct);

    /// <summary>
    /// Task 095 (model §9.1 step 1a): the core records an email's communication is filed under, by D-140's communication hop (its
    /// regarding columns together with its thread's, <see cref="ResolveFilingAsync"/>): what a document linked only to that communication
    /// would be filed under. Empty: unfiled; a fault propagates.
    /// </summary>
    internal virtual async Task<IReadOnlyList<(string Column, InvoiceCoreRecordKind Kind, Guid Id)>> ResolveCommunicationFilingAsync(
        Guid communicationId, CancellationToken ct)
    {
        // An in-memory probe of a document, never written: the walk reads only its columns. Task 146's create census lists it as a
        // Permanent waiver (095 review K2), rather than hiding it from the census by leaving out its logical name.
        var linkedOnly = new Entity(DocumentEntity) { [DocumentRelatedCommunication] = new EntityReference(CommunicationEntity, communicationId) };
        return await ResolveFilingAsync(_entities, Guid.Empty, linkedOnly, ct) ?? Array.Empty<(string, InvoiceCoreRecordKind, Guid)>();
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
        InvoiceSubmissionRequest request, string number, InvoiceSubmissionFile file, Entity row, InvoiceFamilyResolverFields resolverFields,
        CancellationToken ct)
    {
        var core = request.CoreRecord;
        var submissionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var spec = UploadFileSpec(submissionId, file.FileName, request.Channel);

        // True from the moment the stage is asked: a stage whose reply is lost may have written the blob, so compensation removes it by its
        // deterministic name whenever it MAY exist (an absent blob is ignored).
        var blobMayExist = false;
        // True from the moment the allocator is asked: a create whose reply is lost (a timeout) may have committed the row, so compensation
        // deletes it by its id whenever it MAY exist (a delete of an absent row is ignored).
        var submissionMayExist = false;

        // A caller already gone writes nothing; from here on no write observes the caller's token (see the remarks).
        ct.ThrowIfCancellationRequested();
        var write = CancellationToken.None;

        try
        {
            // 1. The bytes, staged under the submission's deterministic file id with the invoice the job will create as the parent
            //    (procedure-and-file-model 9.1 step 2). The row is written by the job, in its commit (step 4).
            StageResult stage;
            await using (var content = new MemoryStream(file.Content, writable: false))
            {
                blobMayExist = true;
                stage = await _files.StageAsync(spec, content, write);
            }

            if (!stage.IsStaged)
            {
                // A refusal wrote nothing (StoredFileBlobs.StageAsync). An unconfigured store keeps the 422 storage_not_configured the
                // route answered when no SPE container resolved (044), so the external route contract and 051's message are unchanged.
                _logger.LogWarning("Invoice submission {SubmissionId}: the file store refused the PDF ({Code})", submissionId, stage.RefusalCode);
                return stage.RefusalCode == StoredFileRefusal.NotConfigured
                    ? InvoiceSubmissionOutcome.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.StorageNotConfigured,
                        "Invoice file storage is not configured, so the invoice cannot be stored. Nothing was saved.")
                    : InvoiceSubmissionOutcome.Refused(StatusCodes.Status500InternalServerError, InvoiceSubmissionErrors.Failed,
                        "The invoice could not be stored. Nothing was saved; try again.");
            }

            // 2. The submission, created with its tracking number (D-87). It names no document: the job finds the file by the submission id.
            foreach (var (column, value) in resolverFields.ToFields())
            {
                row[column] = value;
            }

            row["sprk_name"] = number;
            row["sprk_processingstatus"] = new OptionSetValue((int)InvoiceSubmissionStatus.Received);
            row["sprk_channel"] = new OptionSetValue((int)request.Channel);
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
                await CompensateAsync(submissionId, submissionMayExist: false, spec);
                return InvoiceSubmissionOutcome.Refused(StatusCodes.Status503ServiceUnavailable, TrackingNumberReasons.Unavailable,
                    "A tracking number could not be assigned right now, so nothing was saved. Try again shortly.");
            }

            // 3. The job (ADR-004): payload { submissionId }, idempotency key = the submission id.
            await _jobs.SubmitJobAsync(BuildJob(jobId, submissionId, request.CorrelationId), write);

            _logger.LogInformation(
                "Invoice submission {SubmissionId} received through {Channel} for {Entity} {RecordId}; file {FileId} staged; job {JobId} enqueued",
                submissionId, request.Channel, core.EntityLogicalName, core.RecordId, spec.FileId, jobId);
            _logger.LogDebug("Invoice submission {SubmissionId} tracking number {TrackingNumber}", submissionId, allocation.TrackingNumber);

            return InvoiceSubmissionOutcome.Received(new InvoiceSubmissionReceipt(submissionId, jobId, allocation.TrackingNumber!, DocumentId: null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice submission {SubmissionId} failed; removing what it wrote", submissionId);
            await CompensateAsync(submissionId, submissionMayExist, blobMayExist ? spec : null);
            return InvoiceSubmissionOutcome.Refused(StatusCodes.Status500InternalServerError, InvoiceSubmissionErrors.Failed,
                "The invoice could not be submitted and nothing was saved. Try again; if it keeps failing, contact support.");
        }
    }

    /// <summary>
    /// The Stored File spec of the PDF uploaded with <paramref name="submissionId"/> (ebilling-r1 task 092): id
    /// <see cref="StoredFileIds.ForUpload"/>, purpose Invoice PDF, parent the invoice the job creates
    /// (<see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>), Firm-visible only when the firm uploaded it through the SPA (section 4.1).
    /// The intake stages under it and the job builds the row from it, so both derive the same id, parent and path.
    /// </summary>
    /// <param name="clientFileName">The client's file name (intake only). Neither the blob's name nor its metadata carries it, so the job,
    /// which never sees it, names the row by <paramref name="trackingNumber"/> instead.</param>
    internal static StoredFileRowSpec UploadFileSpec(
        Guid submissionId, string? clientFileName, InvoiceSubmissionChannel channel, string? trackingNumber = null) => new()
        {
            FileId = StoredFileIds.ForUpload(submissionId),
            Purpose = StoredFilePurpose.InvoicePdf,
            Parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceSubmissionPersister.InvoiceIdFor(submissionId)),
            Name = UploadName(clientFileName, trackingNumber),
            ContentType = PdfContentType,
            FirmVisible = channel == InvoiceSubmissionChannel.Spa,
            SubmissionId = submissionId,
        };

    internal static JobContract BuildJob(Guid jobId, Guid submissionId, string? correlationId) => new()
    {
        JobId = jobId,
        JobType = JobType,
        SubjectId = submissionId.ToString(),
        CorrelationId = correlationId ?? string.Empty,
        IdempotencyKey = submissionId.ToString(),
        Payload = JsonSerializer.SerializeToDocument(new { submissionId }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };

    /// <summary>
    /// The display name of the uploaded PDF (<c>sprk_storedfile.sprk_name</c>): the tracking number when given (the job, which writes the
    /// row), else the client's file name without its extension, else <c>invoice</c>; at most 100 characters, then <c>.pdf</c>.
    /// </summary>
    internal static string UploadName(string? clientFileName, string? trackingNumber)
    {
        var stem = StoredFileSchema.NameSegmentOf(trackingNumber)
            ?? Path.GetFileNameWithoutExtension(clientFileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "invoice";
        }

        if (stem.Length > 100)
        {
            stem = stem[..100];
        }

        return $"{stem}.pdf";
    }

    /// <summary>Best effort, reverse order, own token: a failed compensation is logged and never masks the original failure.</summary>
    /// <param name="stagedSpec">The spec a stage was attempted under (null when none was). Its blob is removed by its deterministic name, and
    /// only while no Stored File row references it (<see cref="StoredFileService.DeleteStagedAsync"/>).</param>
    private async Task CompensateAsync(Guid submissionId, bool submissionMayExist, StoredFileRowSpec? stagedSpec)
    {
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

        if (stagedSpec is not null)
        {
            try
            {
                await _files.DeleteStagedAsync(stagedSpec, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invoice submission {SubmissionId}: cleanup could not delete the staged file {FileId}", submissionId, stagedSpec.FileId);
            }
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
            ChannelValue = channel,
            FirmId = row.GetAttributeValue<EntityReference>(TrackingNumberAllocator.FirmColumn)?.Id,
            CoreRecord = core,
            CreatedOn = row.GetAttributeValue<DateTime?>("createdon"),
            InvoiceId = row.GetAttributeValue<EntityReference>(InvoiceColumn) is { Id: var invoiceId } && invoiceId != Guid.Empty
                ? invoiceId
                : null,
            ResubmissionOfId = row.GetAttributeValue<EntityReference>(ResubmissionOfColumn) is { Id: var earlierId } && earlierId != Guid.Empty
                ? earlierId
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
