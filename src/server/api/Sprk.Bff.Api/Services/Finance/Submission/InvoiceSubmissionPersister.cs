// ebilling-r1 task 049 part C (spec FR-20, D-101, D-121, D-124, D-126; notes 049-submission-processing-job.md "Part C").
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceReviewService.ConfirmAsync created an invoice for a confirmed document (a Web API PATCH, matter only, no lines,
//       no status; since 050a / D-134 confirm submits the document through InvoiceSubmissionService instead); InvoiceSubmissionService creates the submission; InvoiceLifecycleService.BuildInvoiceCreateRequest (014) builds the invoice
//       create in its status; IGenericEntityService.ExecuteTransactionAsync (part A) sends one transaction. Nothing turns an evaluated
//       submission into an invoice with lines, an approval portion and the submission's link in one unit.
//   (2) Extension — the processor (part B) is pure evaluation and writes nothing by contract, and the handler (part E) owns delivery,
//       the lock and the download; folding persistence into either mixes a write owner into a reader or the job plumbing. Every rule it
//       applies is delegated unchanged: status to 014, core-record fields to 013's guard, owner to IRecordOwnershipResolver, totals to 016.
//   (3) Cost of doing nothing — FR-20 / D-101 fail: a read submission gets no invoice, and the handler would have to build ~6 row shapes
//       and the commit / resume rules inline.
//
// Placement (bff-extensions.md): Services/Finance/Submission beside the processor; an in-process step of an ADR-004 job that already
// lives in the BFF (ADR-052: no Function / Container Apps Job signal). No endpoint, no package, no AI call. DI: registered by part E
// with the handler (FinanceModule is edited in parallel), as the processor is.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="IInvoiceSubmissionPersister"/>: step 6 of the Processing Pipeline, all-or-nothing.
/// </summary>
/// <remarks>
/// <para><b>One transaction</b> (FR-20), in this order: the invoice create(s) from 014, the invoice PDF's Stored File row(s), Not yet
/// servable (<see cref="StoredFileService.BuildRow"/>; ADR-002: this transaction is the row's one writer): for an upload (task 092) built
/// from the blob the intake staged, for a Submit Invoice source (task 094, procedure-and-file-model 9.1 step 4) from the copy the job staged
/// (<see cref="SubmissionEvaluationOutcome.StagedFiles"/>; Firm-visible No, the source ids, version and SHA), then one create per line, the
/// approval portion (not for an automatic rejection), for a source document a guard on its live row at the STAGED version
/// (<see cref="BuildExistingDocumentGuard"/>; it writes no link, no owner and no share: the source never moves, the 050a re-file is
/// retired), and the submission update (<c>sprk_invoice</c>, the reason code, <c>sprk_findingsjson</c>). <b>Right after the commit, before
/// the totals and every after-step</b>, the commit gate runs (<see cref="SubmissionCommitGate"/>; model 9.1 steps 5-6: the source re-run,
/// then the flip of every file; on a failure the commit is undone). A linked <c>sprk_invoice</c> therefore proves the commit; it is the
/// resume marker. <b>The status is not written here (D-130 a)</b>, except by the gate's undo (Failed): the submission stays Processing,
/// and part D writes <see cref="SubmissionPersistResult.TerminalStatus"/> last.
/// Every created row carries the submitter as <c>sprk_createdbyperson</c> (D-130 3), read from the submission's own stamp, and the invoice
/// also carries the external submitter's contact as <c>sprk_submittedby</c> (D-131), read from the submission's <c>sprk_submitter</c>.
/// A submission that records a contact but no linked user persists without <c>sprk_createdbyperson</c> (the rule for every BFF-created
/// row: an unknown person refuses nothing, the row records nobody); one that records neither is refused.</para>
/// <para><b>Deterministic ids.</b> The invoice, line and approval ids derive from the submission id, so a retry after an unknown outcome can
/// never create a second invoice (a second create of the same id fails), and part D keys Signals before reading anything back.</para>
/// <para><b>Faults.</b> A transaction fault is <see cref="SubmissionPersistStatus.Retry"/> with nothing persisted and the submission
/// untouched (never Accepted). An unknown outcome re-reads the link: linked is committed, not linked is a retry.</para>
/// <para><b>Logs (ADR-015)</b> carry ids, counts and codes, never extracted values.</para>
/// </remarks>
public sealed class InvoiceSubmissionPersister : IInvoiceSubmissionPersister
{
    // ── Tables and columns not already named by their owners ──
    internal const string LineEntity = InvoiceTotalsService.LineEntity;
    internal const string ApprovalEntity = "sprk_invoiceapproval";
    internal const string DocumentEntity = InvoiceReviewService.DocumentEntity;

    internal const string SubmissionInvoice = "sprk_invoice";
    internal const string SubmissionStatus = "sprk_processingstatus";
    internal const string SubmissionReasonCode = "sprk_outcomereasoncode";
    internal const string SubmissionFindingsJson = "sprk_findingsjson";
    internal const string SubmissionCorrelationId = "sprk_correlationid";

    /// <summary><c>sprk_invoicesubmission.sprk_adminattention</c> ("Needs Admin Attention", D-133; Text, 500): why an administrator should
    /// review the submission. Set with the refusals (<see cref="AdminAttentionText"/>); not cleared from code, except that the after-steps withdraw the "Processing stopped" segment when the
    /// submission completes (<see cref="AdminAttentionText.WithoutStopped"/>).</summary>
    internal const string SubmissionAdminAttention = "sprk_adminattention";

    /// <summary><c>sprk_invoicesubmission.sprk_adminattention</c> MaxLength (500).</summary>
    internal const int AdminAttentionMaxLength = 500;

    /// <summary><c>sprk_invoicesubmission.sprk_submitter</c> (lookup to contact): the external submitter, set at intake for the SPA channel.</summary>
    internal const string SubmissionSubmitter = "sprk_submitter";

    /// <summary><c>sprk_invoice.sprk_submittedby</c> (lookup to contact, "Submitted By", D-131): the invoice only, never a line or the approval.</summary>
    internal const string InvoiceSubmittedBy = "sprk_submittedby";

    internal const string ContactEntity = "contact";

    /// <summary><c>sprk_invoicesubmission.sprk_findingsjson</c> MaxLength (1,048,576).</summary>
    internal const int FindingsJsonMaxLength = 1_048_576;

    /// <summary><c>sprk_billingevent.sprk_extractionconfidence</c> MaxLength (100,000).</summary>
    internal const int ExtractionConfidenceMaxLength = 100_000;

    /// <summary>The request bound per transaction (part A: the bound <c>BulkUpdateAsync</c> uses).</summary>
    internal const int MaxRequestsPerTransaction = 1000;

    internal const string DocumentInvoice = "sprk_invoice";

    /// <summary>The communication a document was archived from (review round 5: step 3 compares it with a from-email part's staged one).</summary>
    internal const string DocumentRelatedCommunication = "sprk_relatedcommunication";
    internal const string InvoiceDocument = "sprk_document";
    internal const string InvoiceVendorOrg = "sprk_vendororg";
    internal const string InvoiceCurrency = "sprk_currency";
    internal const string InvoiceCorrelationId = "sprk_correlationid";
    internal const string InvoiceBillingPeriodStart = "sprk_billingperiodstart";
    internal const string InvoiceBillingPeriodEnd = "sprk_billingperiodend";

    internal const string LineName = "sprk_name";
    internal const string LineSequence = "sprk_linesequence";
    internal const string LineEventDate = "sprk_eventdate";
    internal const string LineTimekeeper = "sprk_timekeeper";
    internal const string LineResolvedTimekeeper = "sprk_resolvedtimekeeper";
    internal const string LineQuantity = "sprk_quantity";
    internal const string LineRate = "sprk_rate";
    internal const string LineApprovedRate = "sprk_approvedrate";
    internal const string LineDescription = "sprk_description";
    internal const string LineTaskCodeAsBilled = "sprk_taskcodeasbilled";
    internal const string LineActivityCodeAsBilled = "sprk_activitycodeasbilled";
    internal const string LineExpenseCodeAsBilled = "sprk_expensecodeasbilled";
    internal const string LineCodingConfidence = "sprk_codingconfidence";
    internal const string LineExtractionConfidence = "sprk_extractionconfidence";
    internal const string LineVendorOrg = "sprk_vendororg";
    internal const string LineCurrency = "sprk_currency";
    internal const string LineCorrelationId = "sprk_correlationid";

    internal const string ApprovalName = "sprk_name";
    internal const string ApprovalStatus = "sprk_approvalstatus";
    internal const string ApprovalInvoice = "sprk_invoice";

    internal const int CostTypeFee = 100000000;
    internal const int CostTypeExpense = 100000001;
    internal const int LineStatusPending = 100000000;
    internal const int LineStatusClean = 100000001;
    internal const int LineStatusFlagged = 100000002;
    internal const int ApprovalStatusPending = 100000000;

    private const int TextMax = 100;

    // ── Reason codes of this step (Retry / Refused) ──
    internal const string TransactionFailedCode = "ebilling.submission.transaction_failed";
    internal const string TransactionOutcomeUnknownCode = "ebilling.submission.transaction_outcome_unknown";
    internal const string TotalsFailedCode = "ebilling.submission.totals_failed";
    internal const string TooManyLinesCode = "ebilling.submission.too_many_lines";
    internal const string FindingsTooLargeCode = "ebilling.submission.findings_too_large";
    internal const string OwnerUnresolvedCode = "ebilling.submission.owner_unresolved";
    internal const string SubmitterMissingCode = "ebilling.submission.submitter_missing";

    /// <summary>
    /// 050a review #2: a confirm submission's document was rejected as not an invoice (<c>sprk_invoicereviewstatus</c> = RejectedNotInvoice)
    /// before the job persisted it: the reject stands and the submission is Failed with this code, no invoice.
    /// </summary>
    internal const string DocumentRejectedCode = "ebilling.submission.document_rejected";

    /// <summary>The document's link, its review decision column (<see cref="InvoiceReviewService.DocInvoiceReviewStatus"/>) and its row
    /// version: the transaction's guard on the document (<see cref="BuildExistingDocumentGuard"/>) is conditional on the version read here.</summary>
    private static readonly string[] ConfirmedDocumentColumns =
        [DocumentInvoice, InvoiceReviewService.DocInvoiceReviewStatus, InvoiceReviewService.VersionNumber, DocumentRelatedCommunication];

    internal const string ApprovalRecordNumber = "sprk_regardingrecordnumber";

    /// <summary><c>sprk_invoiceapproval.sprk_regardingrecordnumber</c> MaxLength (Text 100).</summary>
    internal const int ApprovalRecordNumberMaxLength = 100;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IGenericEntityService _entities;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly IInvoiceTotalsService _totals;
    private readonly ExistingDocumentIsolationCheck _isolation;
    private readonly StoredFileService _files;
    private readonly SubmissionCommitGate _gate;
    private readonly ILogger<InvoiceSubmissionPersister> _logger;

    /// <summary>
    /// Task 094: the step-3 source check could not read the source's isolation or flags (nothing committed; the job retries). The string
    /// 050a's re-file used for the same condition, kept so stored reason codes and dashboards stay comparable.
    /// </summary>
    internal const string IsolationUnreadableCode = "ebilling.submission.document_isolation_unreadable";

    public InvoiceSubmissionPersister(
        IGenericEntityService entities,
        ICommunicationDataverseService recordTypes,
        IRecordOwnershipResolver ownership,
        IInvoiceLifecycleService lifecycle,
        IInvoiceTotalsService totals,
        ExistingDocumentIsolationCheck isolation,
        StoredFileService files,
        SubmissionCommitGate gate,
        ILogger<InvoiceSubmissionPersister> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _isolation = isolation ?? throw new ArgumentNullException(nameof(isolation));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The submission columns the persister re-reads: the snapshot's (core record, link, status), the correlation id and the submitters.</summary>
    internal static string[] SubmissionColumns { get; } = [.. SubmissionSnapshot.Columns, SubmissionCorrelationId, SubmissionSubmitter, RecordCreatorPerson.Column];

    /// <summary>The invoice id for a submission: deterministic, so a retry reuses it and can never create a second invoice.</summary>
    public static Guid InvoiceIdFor(Guid submissionId) => IdOf($"ebilling-invoice-{submissionId:D}");

    /// <summary>The id of line <paramref name="lineIndex"/> of a submission's invoice.</summary>
    public static Guid LineIdFor(Guid submissionId, int lineIndex) =>
        IdOf($"ebilling-line-{submissionId:D}-{lineIndex.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>The id of a submission's approval portion.</summary>
    public static Guid ApprovalIdFor(Guid submissionId) => IdOf($"ebilling-approval-{submissionId:D}");

    /// <summary>A deterministic id: the first 16 bytes of the key's SHA-256 (as <c>DecisionRecordWriter.IdOf</c>). Part D's decision id uses it too.</summary>
    internal static Guid IdOf(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));

    /// <summary>The invoice's <c>sprk_name</c> (<see cref="InvoiceReviewService.BuildInvoiceName"/>), also the Signals' subject display name.</summary>
    internal static string InvoiceNameFor(SubmissionEvaluationOutcome outcome) => InvoiceReviewService.BuildInvoiceName(
        outcome.Read!.Header.FirmName.Value, outcome.Submission!.FirmInvoiceNumber, outcome.Read.Header.InvoiceDate.Value?.ToDateTime(TimeOnly.MinValue));

    /// <summary>A line's <c>sprk_name</c>: the tracking number and the 1-based line number, also the Signals' subject display name.</summary>
    internal static string LineNameFor(string trackingNumber, int lineIndex) =>
        $"{trackingNumber} #{(lineIndex + 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The rows the Signals are written on (part D): the invoice and every line by index, with the ids this persister creates them with
    /// (deterministic, so no read is needed) and their display names.
    /// </summary>
    internal static BillingSignalSubjects SignalSubjectsFor(SubmissionEvaluationOutcome outcome, Guid invoiceId)
    {
        var tracking = outcome.Submission!.TrackingNumber;
        var count = outcome.Read!.Lines.Count;
        return new BillingSignalSubjects(
            invoiceId,
            InvoiceNameFor(outcome),
            Enumerable.Range(0, count).ToDictionary(i => i, i => LineIdFor(outcome.SubmissionId, i)),
            Enumerable.Range(0, count).ToDictionary(i => i, i => LineNameFor(tracking, i)));
    }

    /// <inheritdoc />
    public async Task<SubmissionPersistResult> PersistAsync(SubmissionEvaluationOutcome outcome, string? correlationId, CancellationToken ct = default)
    {
        Validate(outcome);
        var result = await PersistCoreAsync(outcome, correlationId, ct).ConfigureAwait(false);

        // Task 094 (9.1 step 4): a source copy the job staged that no commit wrote a row for is deleted (never the upload's blob, which is the
        // intake's and stays for the retry). Not after an outcome-unknown Retry: the commit may yet be proved by the link on the next read.
        if (outcome.StagedFiles.Count > 0 && result.InvoiceId is null
            && result.Status is not (SubmissionPersistStatus.Committed or SubmissionPersistStatus.Resumed or SubmissionPersistStatus.Undone)
            && result.ReasonCode != TransactionOutcomeUnknownCode)
        {
            await DiscardStagedAsync(outcome, ct).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>Deletes the staged source copies (only those with no row: <see cref="StoredFileService.DeleteStagedAsync"/>). Best effort.</summary>
    private async Task DiscardStagedAsync(SubmissionEvaluationOutcome outcome, CancellationToken ct)
    {
        foreach (var copy in outcome.StagedFiles)
        {
            try
            {
                await _files.DeleteStagedAsync(copy.Spec, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: its staged copy {FileId} could not be deleted ({Fault}); the file store's detector finds it",
                    outcome.SubmissionId, copy.Spec.FileId, ex.GetType().Name);
            }
        }
    }

    private async Task<SubmissionPersistResult> PersistCoreAsync(SubmissionEvaluationOutcome outcome, string? correlationId, CancellationToken ct)
    {
        var submissionId = outcome.SubmissionId;

        // ── Re-read: the resume marker and the core record the invoice is filed under ──
        var current = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
        if (current is null)
        {
            _logger.LogInformation("Invoice submission {SubmissionId}: the row no longer exists; nothing persisted", submissionId);
            return new SubmissionPersistResult { Status = SubmissionPersistStatus.SubmissionMissing, SubmissionId = submissionId };
        }

        if (current.GetAttributeValue<EntityReference>(SubmissionInvoice) is { Id: var linkedId } && linkedId != Guid.Empty)
        {
            return await ResumeAsync(outcome, linkedId, ct).ConfigureAwait(false);
        }

        // 072 part L: never persist findings judged under a different core record.
        var stored = InvoiceSubmissionService.ToView(current).CoreRecord;
        if (stored != outcome.CoreRecord)
        {
            _logger.LogWarning(
                "Invoice submission {SubmissionId}: the core record changed since the evaluation; nothing persisted, re-evaluate", submissionId);
            return new SubmissionPersistResult
            {
                Status = SubmissionPersistStatus.ReEvaluate,
                SubmissionId = submissionId,
                CurrentSubmission = current,
            };
        }

        // Task 094 (procedure-and-file-model 9.1 steps 3-4): a Submit Invoice submission names its SOURCE document, which other writers can
        // file, decide or change while the job runs. Step 3, here, before anything is built or written: the source is read again (linked to
        // another invoice, rejected as not an invoice, gone, filed under another core record by the D-140 chain, or isolated elsewhere is a
        // refusal; its flags or isolation unreadable is a Retry), and its live row version must still be the one the job staged its copy
        // from (else SourceChanged: the job stages the new version). Step 4: the transaction carries a guard on the source at that version
        // (BuildExistingDocumentGuard), so a change between this check and the commit rolls the whole transaction back. Nothing in this
        // transaction, or after it, writes the source's link, owner or shares (the 050a re-file is retired).
        var documentId = outcome.Submission!.DocumentId;
        var sourceDocument = outcome.Submission.FileSource == SubmissionFileSource.SourceDocument;
        if (sourceDocument != (documentId is not null))
        {
            throw new ArgumentException("A source-document submission names its document, and only it does.", nameof(outcome));
        }

        if (sourceDocument && !InvoiceSubmissionService.IsExistingDocumentSubmissionIdOf(documentId!.Value, submissionId))
        {
            // Fix round F1-1 (defence in depth; SubmissionSnapshot.Read refuses it first): a document is a source only for the submission
            // derived from it. Nothing is read or written.
            _logger.LogWarning("Invoice submission {SubmissionId}: it names a document it was not created for; refused", submissionId);
            return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.DocumentUnavailable);
        }

        Entity? checkedDocument = null;
        if (sourceDocument)
        {
            if (outcome.StagedFiles.Count == 0 || outcome.StagedFiles.Any(c => c.Spec.SourceDocumentId != documentId))
            {
                throw new ArgumentException("A source-document submission is persisted with the copies the job staged from that document.", nameof(outcome));
            }

            var (documentRefusal, document) = await CheckExistingDocumentAsync(
                submissionId, documentId!.Value, InvoiceIdFor(submissionId), outcome.CoreRecord!, ct).ConfigureAwait(false);
            if (documentRefusal is not null)
            {
                return documentRefusal;
            }

            var live = VersionOf(document!);
            if (outcome.StagedFiles.Any(c => c.SourceVersion != live))
            {
                _logger.LogInformation("Invoice submission {SubmissionId}: its source {DocumentId} is at version {Live}, not the staged one; nothing persisted, re-stage",
                    submissionId, documentId, live);
                return Result(SubmissionPersistStatus.SourceChanged, submissionId, SubmissionReasonCodes.SourceChanged);
            }

            checkedDocument = document;
        }

        // Task 094 (from-email, ISubmitInvoiceFromEmail): the submission names no document (092), and its Invoice part is staged under
        // ForUpload like an upload's PDF; the stage recorded its SOURCE (the marked .eml, its communication, the version the parts were read
        // at) in the blob's metadata, so that record decides. Its .eml gets step 3 here exactly as a source document does (filed under the
        // core record only by the D-140 chain, isolated nowhere else, not linked to another invoice), and the transaction carries the guard
        // on the .eml at the version THIS check read (BuildExistingDocumentGuard). The parts are the bytes the caller marked, read out of
        // the .eml at intake and recorded with their SHA-256, so a later version of the .eml row (its finalization writes it) does not make
        // them stale: the guard pins the filing and isolation this check judged, not the bytes.
        StagedSource? fromEmail = null;
        if (!sourceDocument)
        {
            var staged = await _files.ReadStagedSourceAsync(
                StoredFilePurpose.InvoicePdf, new StoredFileParent(StoredFileParentKind.Invoice, InvoiceIdFor(submissionId)),
                StoredFileIds.ForUpload(submissionId), ct).ConfigureAwait(false);
            if (staged is { SourceUnreadable: true })
            {
                // Review round 5: a source the stage recorded but that cannot be read is not an upload; refuse rather than persist as one.
                _logger.LogWarning("Invoice submission {SubmissionId}: its staged file records a source that cannot be read; nothing persisted", submissionId);
                return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.DocumentUnavailable);
            }

            if (staged is { SourceDocumentId: { } emlId })
            {
                if (outcome.Submission.Channel != InvoiceSubmissionChannel.Internal || staged.SourceVersion is null)
                {
                    _logger.LogWarning("Invoice submission {SubmissionId}: its staged email part has no usable source record; nothing persisted", submissionId);
                    return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.DocumentUnavailable);
                }

                var (emlRefusal, emlRow) = await CheckExistingDocumentAsync(submissionId, emlId, InvoiceIdFor(submissionId), outcome.CoreRecord!, ct)
                    .ConfigureAwait(false);
                if (emlRefusal is not null)
                {
                    return emlRefusal;
                }

                // Review round 5 (pass-2 K5): the .eml must still belong to the communication its parts were staged from. Unlike a new
                // version (SourceChanged, which a later delivery can commit), another delivery reads the same link, so it is Refused with
                // source_changed: the job ends the submission Failed at once instead of retrying it until its attempts run out.
                if (emlRow!.GetAttributeValue<EntityReference>(DocumentRelatedCommunication)?.Id != staged.SourceCommunicationId)
                {
                    _logger.LogInformation("Invoice submission {SubmissionId}: its .eml no longer names the communication its parts were staged from; nothing persisted",
                        submissionId);
                    return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.SourceChanged);
                }

                checkedDocument = emlRow;
                fromEmail = staged;
            }
        }

        // D-130 (3) / D-131: the submitter, recorded at intake on the submission as sprk_createdbyperson (a systemuser: the SPA caller's linked
        // user or the internal caller) and, for the SPA channel, as sprk_submitter (the caller's contact). Read before anything is built.
        //   person present                  -> stamped on the invoice, every line and the approval (D-130 3);
        //   person absent, contact present  -> an external user with no linked Dataverse user: persist WITHOUT sprk_createdbyperson (the rule of
        //                                      RecordOwnershipResolver for BFF-created rows: an unknown person refuses nothing), and warn once;
        //   both absent                     -> intake recorded nobody at all, a data fault: refused before any write.
        // The contact, when present, is also stamped on the invoice only as sprk_submittedby (D-131).
        var submitter = current.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column)?.Id is { } person && person != Guid.Empty
            ? person
            : (Guid?)null;
        var submittedBy = current.GetAttributeValue<EntityReference>(SubmissionSubmitter)?.Id is { } contact && contact != Guid.Empty
            ? contact
            : (Guid?)null;
        if (submitter is null && submittedBy is null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: it records no submitter ({Column} and {ContactColumn} are empty); nothing persisted",
                submissionId, RecordCreatorPerson.Column, SubmissionSubmitter);
            return Result(SubmissionPersistStatus.Refused, submissionId, SubmitterMissingCode);
        }

        if (submitter is null)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: it records a contact but no linked user ({Column} is empty); persisting without it",
                submissionId, RecordCreatorPerson.Column);
        }

        // ── Core-record fields (013) and owner (uac-r2), once: every row is filed under the same core record ──
        var core = outcome.CoreRecord!;
        var invoiceId = InvoiceIdFor(submissionId);
        var invoice = new Entity(InvoiceReviewService.InvoiceEntity, invoiceId);
        invoice[CoreColumn(invoice.LogicalName, core)] = new EntityReference(core.EntityLogicalName, core.RecordId);
        var decided = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
            InvoiceFamilyCoreRecordGuard.Check(invoice), _entities, _recordTypes.QueryRecordTypeRefAsync, ct).ConfigureAwait(false);
        if (decided.IsRefused || decided.ResolverFields is null)
        {
            var code = decided.RefusalCode ?? InvoiceFamilyCoreRecordGuard.ResolverFieldsIncompleteCode;
            _logger.LogWarning("Invoice submission {SubmissionId}: core-record fields could not be derived ({Code}); nothing persisted", submissionId, code);
            return code == InvoiceFamilyCoreRecordGuard.ReadFailedCode
                ? Result(SubmissionPersistStatus.Retry, submissionId, code)
                : Result(SubmissionPersistStatus.Refused, submissionId, code);
        }

        // UAC 194b R3b-2: the invoice's question through the one builder — its table and the core lookup it holds (the column its row
        // writes). Its lines and approval are filed under the invoice this transaction creates, so they take its answer.
        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForRow(
                InvoiceReviewService.InvoiceEntity,
                new[] { KeyValuePair.Create(CoreColumn(invoice.LogicalName, core), (RecordOwnershipParent?)new RecordOwnershipParent(core.EntityLogicalName, core.RecordId)) }),
            ct).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: no owner resolved ({Code}); nothing persisted", submissionId, owner.RefusalCode);
            return Result(SubmissionPersistStatus.Refused, submissionId, owner.RefusalCode ?? OwnerUnresolvedCode);
        }

        // ── The upload's file (task 092; model 9.1 step 4): the blob the intake staged, found by its deterministic name ──
        // The job runs in another process than the intake, so the stage's answer is read back from the blob (its recorded hash and length;
        // the flip after the commit recomputes the hash by streaming). No usable blob: no invoice (nothing written yet).
        StoredFileRowSpec? fileSpec = null;
        StagedBlob? stagedFile = null;
        IReadOnlyList<(StoredFileRowSpec Spec, StagedBlob Blob)> supporting = [];
        if (!sourceDocument)
        {
            fileSpec = InvoiceSubmissionService.UploadFileSpec(
                submissionId, clientFileName: null, outcome.Submission.Channel, outcome.Submission.TrackingNumber);
            if (fromEmail is not null)
            {
                // From-email: the Invoice part's row records its source (Firm-visible No: a copy with a source never is), and every Supporting
                // part staged with it gets its row in this transaction too.
                fileSpec = FromEmailPartSpec(fileSpec, fromEmail);
                stagedFile = fromEmail.Blob;
                var parts = await ReadFromEmailSupportingAsync(outcome.Submission, fromEmail, ct).ConfigureAwait(false);
                if (parts is null)
                {
                    return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.FileUnservable);
                }

                supporting = parts;
            }
            else
            {
                stagedFile = await _files.GetStagedAsync(fileSpec, ct).ConfigureAwait(false);
            }

            if (stagedFile is null)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: its staged PDF {FileId} is missing or unusable; nothing persisted",
                    submissionId, fileSpec.FileId);
                return Result(SubmissionPersistStatus.Refused, submissionId, SubmissionReasonCodes.FileUnservable);
            }
        }

        // ── Rows ──
        var resolverFields = decided.ResolverFields.ToFields();
        var correlation = Cap(correlationId ?? current.GetAttributeValue<string>(SubmissionCorrelationId));
        var lines = BuildLines(outcome, invoiceId, core, resolverFields, owner, correlation);
        FillInvoice(invoice, outcome, resolverFields, owner, correlation);
        if (submitter is { } creator)
        {
            RecordCreatorPerson.Stamp(invoice, creator);
            foreach (var line in lines)
            {
                RecordCreatorPerson.Stamp(line, creator);
            }
        }

        if (submittedBy is { } contactId)
        {
            invoice[InvoiceSubmittedBy] = new EntityReference(ContactEntity, contactId);
        }

        var initial = outcome.Kind == SubmissionEvaluationKind.AutoRejected ? InvoiceLifecycleStatus.Rejected : InvoiceLifecycleStatus.InReview;
        var create = _lifecycle.BuildInvoiceCreateRequest(invoice, initial);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException($"The lifecycle service refused the invoice create ({create.Reason}).");
        }

        Entity? approval = null;
        if (outcome.Kind != SubmissionEvaluationKind.AutoRejected)
        {
            approval = BuildApproval(outcome, invoiceId, core, resolverFields, owner);
            if (submitter is { } approvalCreator)
            {
                RecordCreatorPerson.Stamp(approval, approvalCreator);
            }
        }

        var findingsJson = BuildFindingsJson(outcome, invoiceId, out var planRefusals);
        if (planRefusals.Count > 0)
        {
            // 049 F1-3: the writer would refuse these on every attempt; they are left out of the plan and recorded (codes only, ADR-015).
            _logger.LogWarning("Invoice submission {SubmissionId}: {Count} Signal plan entries the writer would refuse are left out of the plan ({Refusals})",
                submissionId, planRefusals.Count, string.Join(", ", planRefusals.Select(r => r.PolicyCode + ":" + r.Code)));
        }

        if (findingsJson.Length > FindingsJsonMaxLength)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: findings JSON is {Length} characters, over the column; nothing persisted",
                submissionId, findingsJson.Length);
            return Result(SubmissionPersistStatus.Refused, submissionId, FindingsTooLargeCode);
        }

        // EB-D-168 / EB-D-168a (UAC 194b R4b-2): a Stored File is a HARD-DENY row with its own question, never the invoice's team. Its
        // parent, the invoice, is created by this same transaction and cannot be read yet, so the question names what the invoice is filed
        // under — its core record — for the sprk_storedfile table: the same business unit and the same secure-if-any as the invoice's own
        // answer, so an ordinary invoice's files go to the customer's restricted-content owner team and a Secure one's to the Secure team.
        // Asked only when a file row is written; a refusal (fail closed: the answer in the root business unit, a team that cannot be used)
        // persists nothing.
        RecordOwnerResolution? fileOwner = null;
        if (fileSpec is not null || supporting.Count > 0 || outcome.StagedFiles.Count > 0)
        {
            fileOwner = await _ownership.ResolveOwnerAsync(StoredFileOwnerContext(core), ct).ConfigureAwait(false);
            if (!fileOwner.IsOwned)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: no owner resolved for its stored files ({Code}); nothing persisted",
                    submissionId, fileOwner.RefusalCode);
                return Result(SubmissionPersistStatus.Refused, submissionId, fileOwner.RefusalCode ?? OwnerUnresolvedCode);
            }
        }

        var requests = new List<OrganizationRequest>(create.Requests);
        if (fileSpec is not null)
        {
            // Task 092 (model 4.1, 9.1 step 4): the invoice PDF's row, right after the invoice it hangs under. Not yet servable; owned by the
            // Stored File answer (above); the creator person is the submitter, none for a contact (K6).
            requests.Add(_files.BuildRow(fileSpec, stagedFile!, fileOwner!, submitter));
        }

        foreach (var (partSpec, partBlob) in supporting)
        {
            // From-email (task 094): a Supporting part's row, the same rules (Not yet servable, the Stored File answer, Firm-visible No).
            requests.Add(_files.BuildRow(partSpec, partBlob, fileOwner!, submitter));
        }

        foreach (var copy in outcome.StagedFiles)
        {
            // Task 094 (9.1 step 4): the source copy's row, the same rules (Not yet servable, the Stored File answer), Firm-visible No.
            requests.Add(_files.BuildRow(copy.Spec, copy.Blob, fileOwner!, submitter));
        }

        requests.AddRange(lines.Select(line => new CreateRequest { Target = line }));
        if (approval is not null)
        {
            requests.Add(new CreateRequest { Target = approval });
        }

        var guardIndex = -1;
        var guardedSource = sourceDocument ? documentId : fromEmail?.SourceDocumentId;
        if (guardedSource is not null)
        {
            // The guard: no link, only the version check at the staged version (see BuildExistingDocumentGuard; the check above proved the
            // version read is the staged one).
            guardIndex = requests.Count;
            requests.Add(BuildExistingDocumentGuard(checkedDocument!));
        }

        // D-133: a plan entry left out is a Signal the invoice will never have; mark the submission for the administrator in the same write.
        var adminAttention = planRefusals.Count == 0
            ? null
            : AdminAttentionText.Build(planRefusals.Select(r => new AdminAttentionText.Item(r.PolicyCode, r.SubjectEntity, r.SubjectId.ToString("D"), r.Code)));
        requests.Add(new UpdateRequest { Target = BuildSubmissionUpdate(outcome, invoiceId, findingsJson, adminAttention) });

        if (requests.Count > MaxRequestsPerTransaction)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: {Count} requests exceed the transaction bound; nothing persisted",
                submissionId, requests.Count);
            return Result(SubmissionPersistStatus.Refused, submissionId, TooManyLinesCode);
        }

        var lineIds = lines.Select((line, index) => (index, line.Id)).ToDictionary(p => p.index, p => p.Id);

        // ── Commit ──
        try
        {
            await _entities.ExecuteTransactionAsync(requests, ct).ConfigureAwait(false);
        }
        catch (DataverseTransactionException ex) when (ex.OutcomeUnknown)
        {
            // A timeout can land after the commit: the link decides (part A), never "nothing landed".
            bool linked;
            try
            {
                var reread = await ReadSubmissionAsync(submissionId, ct).ConfigureAwait(false);
                linked = reread?.GetAttributeValue<EntityReference>(SubmissionInvoice)?.Id == invoiceId;
            }
            catch (Exception readFault) when (readFault is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: transaction outcome unknown and the link could not be re-read ({Fault}); retry",
                    submissionId, readFault.GetType().Name);
                return Result(SubmissionPersistStatus.Retry, submissionId, TransactionOutcomeUnknownCode);
            }

            if (!linked)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: transaction outcome unknown and the submission is not linked; retry", submissionId);
                return Result(SubmissionPersistStatus.Retry, submissionId, TransactionOutcomeUnknownCode);
            }

            _logger.LogInformation("Invoice submission {SubmissionId}: transaction outcome unknown but the link proves the commit of invoice {InvoiceId}",
                submissionId, invoiceId);
        }
        catch (DataverseTransactionException ex)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: transaction failed at request {Index} of {Count}; nothing persisted, retry",
                submissionId, ex.FailedRequestIndex, requests.Count);

            // 050a review round 2, item 2(a): the transaction rolled back. For a source document the likely cause is the guard (a filing, a
            // reject, a delete or any other change since the check), so the live row is checked again now: a refusal is answered in this
            // delivery (the submission is Failed, no invoice). Task 094 (9.1 step 4): a guard fault otherwise means the source changed, so the
            // job re-stages it under the new version and commits once more (SourceChanged); any other fault is the transaction's own Retry.
            // Either way the staged copy is deleted (PersistAsync); the upload's blob never is.
            if (guardedSource is { } guarded && await RecheckExistingDocumentAsync(submissionId, guarded, invoiceId, core, ct).ConfigureAwait(false) is { } refusedNow)
            {
                return refusedNow;
            }

            if (guardedSource is not null && ex.FailedRequestIndex == guardIndex)
            {
                // A from-email submission has no job-staged copy, so the job retries the delivery and step 3 reads the .eml again.
                _logger.LogInformation("Invoice submission {SubmissionId}: the guard on its source {DocumentId} failed; nothing persisted, re-stage",
                    submissionId, guardedSource);
                return Result(SubmissionPersistStatus.SourceChanged, submissionId, SubmissionReasonCodes.SourceChanged);
            }

            return Result(SubmissionPersistStatus.Retry, submissionId, TransactionFailedCode);
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: committed invoice {InvoiceId} ({Kind}) with {LineCount} lines in {RequestCount} requests",
            submissionId, invoiceId, outcome.Kind, lines.Count, requests.Count);

        // Model 9.1 steps 5-6 (tasks 092, 094): the commit gate, immediately after the commit and before the totals and every after-step, on
        // every channel: the source re-run, then the flip. A fault throws (the job retries; the submission now links its invoice, so the retry
        // is a resume, which runs the gate first). A failure undoes the commit (the submission is Failed) and nothing else runs.
        var gate = await _gate.RunAsync(submissionId, ct).ConfigureAwait(false);
        if (gate.Status != SubmissionCommitGateStatus.Passed)
        {
            return new SubmissionPersistResult
            {
                Status = SubmissionPersistStatus.Undone,
                SubmissionId = submissionId,
                InvoiceId = invoiceId,
                ReasonCode = gate.ReasonCode,
                UndoHeld = gate.Status == SubmissionCommitGateStatus.UndoHeld,
            };
        }

        var committedTerminal = TerminalFor(outcome);
        return await WithTotalsAsync(new SubmissionPersistResult
        {
            Status = SubmissionPersistStatus.Committed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            LineIds = lineIds,
            ApprovalId = approval?.Id,
            TerminalStatus = committedTerminal.Status,
            TerminalReasonCode = committedTerminal.ReasonCode,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 050a review round 2, item 2(a): the transaction's guard on a confirm submission's EXISTING document. An update of the document
    /// conditional on the row version <see cref="CheckExistingDocumentAsync"/> read (<c>ConcurrencyBehavior.IfRowVersionMatches</c>), so a
    /// filing, a reject or a delete that landed since the check fails the transaction and NOTHING commits (the persister's
    /// <see cref="DataverseTransactionException"/> path). It writes NOTHING to <c>sprk_invoice</c>: the re-file stays the link's one writer.
    /// <para>Its one column is <c>sprk_invoicereviewstatus</c>, written back with the value the check read (null when it read none). The
    /// version check guarantees the row still holds exactly that value, so the write changes no value. A column is written at all so that
    /// the request is a real update of the row: Dataverse then evaluates the version condition and moves the row version, so a stalled
    /// conditional write made against the pre-commit version (a reject that read the row before the confirm's submission existed) is
    /// refused too. Dataverse does not document how an update that names no column behaves under <c>IfRowVersionMatches</c>, so the guard
    /// does not rely on one. This column was chosen because the check already reads it (no second read, no race between two reads), it is
    /// not the link, and the only writers of it (confirm's mark, reject) are themselves conditional on the row version, so a guard that
    /// moves the version makes them re-read and decide again rather than overwrite.</para>
    /// </summary>
    internal static UpdateRequest BuildExistingDocumentGuard(Entity checkedDocument)
    {
        var version = VersionOf(checkedDocument);
        return new UpdateRequest
        {
            Target = new Entity(DocumentEntity, checkedDocument.Id)
            {
                RowVersion = version.ToString(CultureInfo.InvariantCulture),
                [InvoiceReviewService.DocInvoiceReviewStatus] = checkedDocument.GetAttributeValue<OptionSetValue>(InvoiceReviewService.DocInvoiceReviewStatus),
            },
            ConcurrencyBehavior = ConcurrencyBehavior.IfRowVersionMatches,
        };
    }

    /// <summary>The from-email attachment identity of the part at <paramref name="ordinal"/> (its Supporting file id derives from it too).</summary>
    internal static string EmlPartIdentity(int ordinal) => "eml-part-" + ordinal.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Task 094 (from-email): <paramref name="spec"/> with the source its stage recorded: the marked <c>.eml</c>, its communication, the part's
    /// attachment identity, the version the parts were read at and the part's SHA-256 (a byte copy: the blob's own). Firm-visible No.
    /// </summary>
    private static StoredFileRowSpec FromEmailPartSpec(StoredFileRowSpec spec, StagedSource staged) => spec with
    {
        FirmVisible = false,
        SourceDocumentId = staged.SourceDocumentId,
        SourceCommunicationId = staged.SourceCommunicationId,
        SourceAttachmentIdentity = staged.SourceAttachmentIdentity,
        SourceVersion = staged.SourceVersion,
        SourceSha256 = staged.Blob.Sha256,
    };

    /// <summary>
    /// Task 094 (from-email): the Supporting parts the Invoice part's stage listed, each read back from its own stage, which must record the
    /// same <c>.eml</c>. Null when one is missing or records another source (nothing is written: the submission is Failed
    /// <see cref="SubmissionReasonCodes.FileUnservable"/>). Names carry the tracking number and the ordinal, never the attachment's name.
    /// </summary>
    private async Task<IReadOnlyList<(StoredFileRowSpec Spec, StagedBlob Blob)>?> ReadFromEmailSupportingAsync(
        SubmissionSnapshot submission, StagedSource invoicePart, CancellationToken ct)
    {
        var parent = new StoredFileParent(StoredFileParentKind.Invoice, InvoiceIdFor(submission.SubmissionId));
        var parts = new List<(StoredFileRowSpec, StagedBlob)>(invoicePart.SupportingOrdinals.Count);
        foreach (var ordinal in invoicePart.SupportingOrdinals.Distinct())
        {
            var identity = EmlPartIdentity(ordinal);
            var fileId = StoredFileIds.ForAttachment(submission.SubmissionId, identity);
            var staged = await _files.ReadStagedSourceAsync(StoredFilePurpose.InvoiceSupportingFile, parent, fileId, ct).ConfigureAwait(false);
            if (staged is null || staged.SourceUnreadable || staged.SourceDocumentId != invoicePart.SourceDocumentId
                || staged.SourceAttachmentIdentity != identity)
            {
                _logger.LogWarning("Invoice submission {SubmissionId}: its staged email part {Ordinal} is missing or records another source; nothing persisted",
                    submission.SubmissionId, ordinal);
                return null;
            }

            var name = $"{StoredFileSchema.NameSegmentOf(submission.TrackingNumber) ?? "invoice"}-{ordinal.ToString(CultureInfo.InvariantCulture)}";
            var spec = FromEmailPartSpec(new StoredFileRowSpec
            {
                FileId = fileId,
                Purpose = StoredFilePurpose.InvoiceSupportingFile,
                Parent = parent,
                Name = (name.Length > 100 ? name[..100] : name) + (staged.Extension ?? string.Empty),
                ContentType = staged.ContentType is { Length: > 0 and <= StoredFileSchema.ContentTypeMaxLength } type ? type : "application/octet-stream",
                SubmissionId = submission.SubmissionId,
            }, staged);
            parts.Add((spec, staged.Blob));
        }

        return parts;
    }

    /// <summary>
    /// The row version of a source document as read (<c>versionnumber</c>, else the entity's <c>RowVersion</c>); throws when neither is there,
    /// so nothing is committed against a version that was not read.
    /// </summary>
    internal static long VersionOf(Entity document) =>
        document.GetAttributeValue<long?>(InvoiceReviewService.VersionNumber)
        ?? (StoredFileSourceVersion.TryParseRowVersion(document.RowVersion, out var rowVersion) ? rowVersion : (long?)null)
        ?? throw new InvalidOperationException(
            "The source document's versionnumber could not be read, so the invoice cannot be committed against it safely. Nothing was written.");

    /// <summary>
    /// 050a review round 2, item 2(a): after a failed transaction, whether the confirm submission's document now refuses the submission (the
    /// guard is the likely cause). A refusal, or null: nothing changed that refuses it, or the row (or its isolation, review round 3) could
    /// not be read; the caller retries.
    /// </summary>
    private async Task<SubmissionPersistResult?> RecheckExistingDocumentAsync(
        Guid submissionId, Guid documentId, Guid invoiceId, InvoiceCoreRecord core, CancellationToken ct)
    {
        try
        {
            var refusal = (await CheckExistingDocumentAsync(submissionId, documentId, invoiceId, core, ct).ConfigureAwait(false)).Refusal;
            return refusal?.Status == SubmissionPersistStatus.Refused ? refusal : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} could not be read again after the failed transaction ({Fault}); retry",
                submissionId, documentId, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// 050a review #1 / #2: a null refusal (with the row read) when the confirm submission's document may be persisted (unlinked, or already
    /// linked to <paramref name="invoiceId"/>, this submission's own invoice; not rejected; review round 3: not isolated under any secure
    /// root other than <paramref name="core"/>); otherwise the refusal, nothing written. A missing document is refused too. A read fault of
    /// the row propagates (the job retries); an isolation that cannot be read is a Retry result.
    /// </summary>
    private async Task<(SubmissionPersistResult? Refusal, Entity? Document)> CheckExistingDocumentAsync(
        Guid submissionId, Guid documentId, Guid invoiceId, InvoiceCoreRecord core, CancellationToken ct)
    {
        Entity document;
        try
        {
            document = await _entities.RetrieveAsync(DocumentEntity, documentId, ConfirmedDocumentColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} no longer exists; nothing persisted", submissionId, documentId);
            return (Result(SubmissionPersistStatus.Refused, submissionId, InvoiceSubmissionErrors.DocumentNotFound), null);
        }

        if (document.GetAttributeValue<EntityReference>(DocumentInvoice) is { Id: var linked } && linked != Guid.Empty && linked != invoiceId)
        {
            // Filed under another invoice since the confirm (e.g. a document PUT): never overwritten, never moved from under it.
            _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} is linked to invoice {LinkedId}; nothing persisted",
                submissionId, documentId, linked);
            return (Result(SubmissionPersistStatus.Refused, submissionId, InvoiceSubmissionErrors.DocumentAlreadyInvoiced), document);
        }

        if (document.GetAttributeValue<OptionSetValue>(InvoiceReviewService.DocInvoiceReviewStatus)?.Value == InvoiceReviewService.ReviewStatusRejectedNotInvoice)
        {
            // A reject that landed between the confirm's create and its mark (the confirm answered 409): the reject stands.
            _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} was rejected as not an invoice; nothing persisted",
                submissionId, documentId);
            return (Result(SubmissionPersistStatus.Refused, submissionId, DocumentRejectedCode), document);
        }

        // 050a review round 3 (uac-r2 F1-b) and task 094 (9.1 step 3): the step-1 source check runs again here, at commit, and again after a
        // rolled-back transaction (InvoiceSubmissionService.CheckSourceFilingAsync: the D-140 filing chain, the source's Restricted,
        // internal-only and privacy flags, its isolation). Filed or isolated under another record, or gone, is refused (the submission is
        // Failed, no invoice); isolation that cannot be read now is retried, never committed. A fault reading the chain or a flag propagates
        // (a Retry: an unreadable flag never passes). The flags are logged, never stored, never a refusal ground (owner K).
        var source = await InvoiceSubmissionService.CheckSourceFilingAsync(_entities, _isolation, documentId, core, ct).ConfigureAwait(false);
        switch (source.Verdict)
        {
            case SourceFilingVerdict.Allowed:
                _logger.LogInformation(
                    "Invoice submission {SubmissionId}: source {DocumentId} checked (restricted {Restricted}, internal-only {InternalOnly}, privacy {Privacy}; not stored)",
                    submissionId, documentId, source.Flags?.Restricted, source.Flags?.InternalOnly, source.Flags?.Privacy);
                return (null, document);
            case SourceFilingVerdict.Unavailable:
                _logger.LogWarning("Invoice submission {SubmissionId}: the isolation of its document {DocumentId} could not be read; nothing persisted, retry",
                    submissionId, documentId);
                return (Result(SubmissionPersistStatus.Retry, submissionId, IsolationUnreadableCode), document);
            case SourceFilingVerdict.Gone:
                _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} no longer exists; nothing persisted", submissionId, documentId);
                return (Result(SubmissionPersistStatus.Refused, submissionId, InvoiceSubmissionErrors.DocumentNotFound), null);
            default:
                _logger.LogWarning("Invoice submission {SubmissionId}: its document {DocumentId} is filed or restricted elsewhere ({Detail}); nothing persisted",
                    submissionId, documentId, source.Detail);
                return (Result(SubmissionPersistStatus.Refused, submissionId, InvoiceSubmissionErrors.DocumentCoreRecordMismatch), document);
        }
    }

    // ── Resume (the submission already links an invoice) ──

    private async Task<SubmissionPersistResult> ResumeAsync(SubmissionEvaluationOutcome outcome, Guid invoiceId, CancellationToken ct)
    {
        var submissionId = outcome.SubmissionId;
        var terminal = TerminalFor(outcome);
        var query = new QueryExpression(LineEntity) { ColumnSet = new ColumnSet(LineSequence), NoLock = true };
        query.Criteria.AddCondition(InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, ConditionOperator.Equal, invoiceId);
        var rows = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var lineIds = new Dictionary<int, Guid>();
        foreach (var row in rows.Entities)
        {
            if (row.GetAttributeValue<int?>(LineSequence) is { } sequence and > 0)
            {
                lineIds[sequence - 1] = row.Id;
            }
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: already linked to invoice {InvoiceId} ({LineCount} lines); resuming, nothing created",
            submissionId, invoiceId, lineIds.Count);

        // R2: NO totals here. A Resumed result is always answered from the stored state (the handler calls the after-steps' ResumeAsync, which
        // recalculates the totals itself); a totals fault turned into a Retry that carries the invoice id would be indistinguishable from this
        // delivery's own commit and be run with this run's re-evaluated terminal.
        return new SubmissionPersistResult
        {
            Status = SubmissionPersistStatus.Resumed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            LineIds = lineIds,
            TerminalStatus = terminal.Status,
            TerminalReasonCode = terminal.ReasonCode,
        };
    }

    /// <summary>016 after the commit (map row 6c); only a run that committed in THIS delivery calls it. A fault is a retry that keeps the invoice id.</summary>
    private async Task<SubmissionPersistResult> WithTotalsAsync(SubmissionPersistResult committed, CancellationToken ct)
    {
        try
        {
            var totals = await _totals.RecalculateAsync(committed.InvoiceId!.Value, ct).ConfigureAwait(false);
            return committed with { Totals = totals };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: totals for invoice {InvoiceId} failed after the commit ({Fault}); retry resumes",
                committed.SubmissionId, committed.InvoiceId, ex.GetType().Name);
            return committed with { Status = SubmissionPersistStatus.Retry, ReasonCode = TotalsFailedCode };
        }
    }

    private async Task<Entity?> ReadSubmissionAsync(Guid submissionId, CancellationToken ct)
    {
        try
        {
            return await _entities.RetrieveAsync(FinanceBaseValues.SubmissionEntity, submissionId, SubmissionColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRecordNotFound(ex))
        {
            return null;
        }
    }

    private static bool IsRecordNotFound(Exception ex)
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

    // ── Row builders (internal for tests) ──

    internal static void FillInvoice(
        Entity invoice, SubmissionEvaluationOutcome outcome, IReadOnlyDictionary<string, object> resolverFields, RecordOwnerResolution owner,
        string? correlationId)
    {
        var snapshot = outcome.Submission!;
        var header = outcome.Read!.Header;
        foreach (var (column, value) in resolverFields)
        {
            invoice[column] = value;
        }

        invoice[InvoiceReviewService.InvName] = InvoiceNameFor(outcome);
        invoice[InvoiceReviewService.InvInvoiceNumber] = snapshot.FirmInvoiceNumber;
        invoice[InvoiceReviewService.InvTrackingNumber] = snapshot.TrackingNumber;
        invoice[InvoiceVendorOrg] = new EntityReference("sprk_organization", snapshot.FirmId);

        // Task 094: the invoice never names a document (sprk_document): its PDF is the invoice's Stored File on every channel (an upload, a
        // Submit Invoice copy), and the source stays where it is filed (the copy rule; owner K keeps the copy out of indexing).
        if (header.InvoiceDate.Value is { } invoiceDate)
        {
            invoice[InvoiceReviewService.InvInvoiceDate] = Utc(invoiceDate);
        }

        if (header.BillingPeriodStart.Value is { } start)
        {
            invoice[InvoiceBillingPeriodStart] = Utc(start);
        }

        if (header.BillingPeriodEnd.Value is { } end)
        {
            invoice[InvoiceBillingPeriodEnd] = Utc(end);
        }

        // The billed total as read; when the document shows none, the signed sum of its lines (D-121: a credit memo is negative, D-127).
        var billed = header.TotalDue.Value ?? outcome.Read.Lines.Sum(l => l.Amount.Value ?? 0m);
        invoice[InvoiceReviewService.InvTotalAmount] = new Money(billed);
        if (Cap(header.Currency.Value) is { } currency)
        {
            invoice[InvoiceCurrency] = currency;
        }

        if (correlationId is not null)
        {
            invoice[InvoiceCorrelationId] = correlationId;
        }

        owner.ApplyTo(invoice);
        RequireFiled(invoice);
    }

    internal static List<Entity> BuildLines(
        SubmissionEvaluationOutcome outcome, Guid invoiceId, InvoiceCoreRecord core, IReadOnlyDictionary<string, object> resolverFields,
        RecordOwnerResolution owner, string? correlationId)
    {
        var read = outcome.Read!;
        var snapshot = outcome.Submission!;
        var resolved = outcome.Resolution!.Lines.ToDictionary(l => l.LineIndex);
        var coded = outcome.Coding?.Lines.ToDictionary(l => l.LineKey, StringComparer.Ordinal);
        var flaggedByFinding = (outcome.Evaluation?.Findings ?? [])
            .Where(f => f.Subject.Kind == BillingSubjectKind.Line && f.Subject.LineIndex is not null)
            .Select(f => f.Subject.LineIndex!.Value)
            .ToHashSet();

        var rows = new List<Entity>(read.Lines.Count);
        for (var index = 0; index < read.Lines.Count; index++)
        {
            var line = read.Lines[index];
            if (!resolved.TryGetValue(index, out var resolution))
            {
                throw new InvalidOperationException($"The resolution has no result for line {index}.");
            }

            LineCodingResult? coding = null;
            if (coded is not null && !coded.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out coding))
            {
                throw new InvalidOperationException($"Line coding has no result for line {index}.");
            }

            var row = new Entity(LineEntity, LineIdFor(snapshot.SubmissionId, index))
            {
                [InvoiceFamilyCoreRecordGuard.LineInvoiceColumn] = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId),
                [CoreColumn(LineEntity, core)] = new EntityReference(core.EntityLogicalName, core.RecordId),
                [LineName] = LineNameFor(snapshot.TrackingNumber, index),
                [LineSequence] = index + 1,
                [InvoiceTotalsService.LineCostType] = new OptionSetValue(line.LineType == InvoiceLineType.Fee ? CostTypeFee : CostTypeExpense),
                [LineVendorOrg] = new EntityReference("sprk_organization", snapshot.FirmId),
            };
            foreach (var (column, value) in resolverFields)
            {
                row[column] = value;
            }

            // Billed values as read (FR-04). Signed: a credit line keeps its negative amount (D-121).
            if (line.Amount.Value is { } amount)
            {
                row[InvoiceTotalsService.LineAmount] = new Money(amount);
            }

            var isFee = line.LineType == InvoiceLineType.Fee;
            if ((isFee ? line.Hours.Value : line.Quantity.Value) is { } quantity)
            {
                row[LineQuantity] = quantity;
            }

            if ((isFee ? line.Rate.Value : line.UnitCost.Value) is { } rate)
            {
                row[LineRate] = new Money(rate);
            }

            if ((line.Date.Value ?? read.Header.InvoiceDate.Value) is { } date)
            {
                row[LineEventDate] = Utc(date);
            }

            SetText(row, LineTimekeeper, line.TimekeeperAsBilled.Value);
            if (!string.IsNullOrWhiteSpace(line.Narrative.Value))
            {
                row[LineDescription] = line.Narrative.Value;
            }

            SetText(row, LineTaskCodeAsBilled, line.TaskCodeAsBilled.Value);
            SetText(row, LineActivityCodeAsBilled, line.ActivityCodeAsBilled.Value);
            SetText(row, LineExpenseCodeAsBilled, line.ExpenseCodeAsBilled.Value);
            SetText(row, LineCurrency, read.Header.Currency.Value);

            // 046: the roster timekeeper and the approved rate.
            if (resolution.Timekeeper?.TimekeeperId is { } timekeeperId && timekeeperId != Guid.Empty)
            {
                row[LineResolvedTimekeeper] = new EntityReference("sprk_timekeeper", timekeeperId);
            }

            if (resolution.Rate.ApprovedRate is { } approvedRate)
            {
                row[LineApprovedRate] = new Money(approvedRate);
            }

            // 047: the coding confidence; the code-set source / review flag live in the confidence JSON (no column, notes section 5).
            if (coding is not null)
            {
                row[LineCodingConfidence] = coding.Confidence;
            }

            var flagged = flaggedByFinding.Contains(index)
                || coding is { RefusedCodes.Count: > 0 }
                || coding is { HasFirmCodeMismatch: true }
                || LineFindingCodeFor(line.Narrative.Value, coding) is not null; // EB-D-175 / EB-D-176
            row[InvoiceTotalsService.LineStatus] = new OptionSetValue(LineStatusFor(flagged, coded: coding is not null));

            var confidence = BuildExtractionConfidence(line, resolution, coding, outcome);
            if (confidence.Length <= ExtractionConfidenceMaxLength)
            {
                row[LineExtractionConfidence] = confidence;
            }

            if (correlationId is not null)
            {
                row[LineCorrelationId] = correlationId;
            }

            owner.ApplyTo(row);
            RequireFiled(row);
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// The line status a persisted line starts in (103 Q2: nothing used to write Clean, so no invoice could ever be approved, I-2). A line a
    /// finding, a refused code, a firm-code mismatch, a line the coding Action did not return (EB-D-175) or a line with no description (EB-D-176)
    /// flagged is Flagged. Any other line of a CODED invoice is Clean: every check that can
    /// flag a line has run. A line of an UNCODED invoice (D-126: coding was unavailable, so a hold finding stands) stays Pending, shown as
    /// Processing (D-58): its codes are still owed, and a Pending line is neither decidable nor approvable.
    /// </summary>
    internal static int LineStatusFor(bool flagged, bool coded) => flagged ? LineStatusFlagged : coded ? LineStatusClean : LineStatusPending;

    /// <summary>The finding code of a line the AI did not check because the coding Action returned nothing usable for it (EB-D-175).</summary>
    internal const string LineFindingNotReturned = "coding.not_returned";

    /// <summary>The finding code of a line with a blank narrative (EB-D-176): it has no description to code or to read.</summary>
    internal const string LineFindingNoDescription = "line.no_description";

    /// <summary>The findings JSON member listing the lines flagged for being unchecked or undescribed: <c>[{ line, code, message }]</c>, no values.</summary>
    internal const string LineFindingsMember = "lineFindings";

    /// <summary>
    /// The finding a line carries for lacking a check, or null (owner decisions EB-D-175 and EB-D-176: such a line is Flagged, not Clean, so a
    /// reviewer must accept or correct it before approval). A blank narrative (a coded line that was never sent, <see cref="LineCodingStatus.NoNarrative"/>,
    /// or any line of an uncoded invoice) is <see cref="LineFindingNoDescription"/>; a line sent to the model that came back unusable
    /// (<see cref="LineCodingStatus.NotReturned"/>) is <see cref="LineFindingNotReturned"/>. The firm's codes are kept either way. These are
    /// reviewer-facing findings stored in <c>sprk_findingsjson</c>; they raise no Signal, and a Flagged line is decided by status alone.
    /// </summary>
    internal static string? LineFindingCodeFor(string? narrative, LineCodingResult? coding) =>
        string.IsNullOrWhiteSpace(narrative) || coding is { Status: LineCodingStatus.NoNarrative } ? LineFindingNoDescription
        : coding is { Status: LineCodingStatus.NotReturned } ? LineFindingNotReturned
        : null;

    internal static Entity BuildApproval(
        SubmissionEvaluationOutcome outcome, Guid invoiceId, InvoiceCoreRecord core, IReadOnlyDictionary<string, object> resolverFields,
        RecordOwnerResolution owner)
    {
        // The approvers are task 103's (D-78): none is set here.
        var row = new Entity(ApprovalEntity, ApprovalIdFor(outcome.SubmissionId))
        {
            [ApprovalName] = $"Approval {outcome.Submission!.TrackingNumber}",
            [ApprovalStatus] = new OptionSetValue(ApprovalStatusPending),
            [ApprovalInvoice] = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId),
            [CoreColumn(ApprovalEntity, core)] = new EntityReference(core.EntityLogicalName, core.RecordId),
        };
        foreach (var (column, value) in resolverFields)
        {
            row[column] = value;
        }

        // The approval's record number is Text(100); the resolver does not cut it, so a longer one would fault the whole transaction.
        if (row.GetAttributeValue<string>(ApprovalRecordNumber) is { Length: > ApprovalRecordNumberMaxLength } number)
        {
            row[ApprovalRecordNumber] = number[..ApprovalRecordNumberMaxLength];
        }

        owner.ApplyTo(row);
        RequireFiled(row);
        return row;
    }

    /// <summary>
    /// The submission update inside the transaction: the invoice link (the commit marker), the findings JSON and the reason code. It does NOT
    /// write <c>sprk_processingstatus</c> (D-130 a): the submission stays Processing until part D's after-commit steps are done, and part D
    /// writes <see cref="SubmissionPersistResult.TerminalStatus"/> as its last step, so Accepted / Rejected means complete.
    /// </summary>
    internal static Entity BuildSubmissionUpdate(SubmissionEvaluationOutcome outcome, Guid invoiceId, string findingsJson, string? adminAttention = null)
    {
        var update = new Entity(FinanceBaseValues.SubmissionEntity, outcome.SubmissionId)
        {
            [SubmissionInvoice] = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId),
            [SubmissionReasonCode] = TerminalFor(outcome).ReasonCode,
            [SubmissionFindingsJson] = findingsJson,
        };

        // D-133: only ever set, never written null or empty (the administrator clears it).
        if (adminAttention is { Length: > 0 })
        {
            update[SubmissionAdminAttention] = adminAttention;
        }

        return update;
    }

    /// <summary>
    /// The terminal <c>sprk_processingstatus</c> and <c>sprk_outcomereasoncode</c> of a persisted outcome (notes 049 section 2, D-130):
    /// Accepted / null; AcceptedUncoded Accepted + the coding code (b); AutoRejected Rejected + the first deciding code; Blocked
    /// Rejected + <see cref="SubmissionReasonCodes.Blocked"/> (the held representation).
    /// </summary>
    internal static (InvoiceSubmissionStatus Status, string? ReasonCode) TerminalFor(SubmissionEvaluationOutcome outcome) =>
        TerminalFor(outcome.Kind, outcome.ReasonCode);

    /// <summary>
    /// The one terminal mapping, from a kind and its root reason code: the outcome's (above) or the STORED findings JSON's <c>kind</c> and
    /// <c>reasonCode</c> (part D derives the terminal from the stored state, so a resume can never write a status the stored invoice and
    /// record contradict; notes section 13a).
    /// </summary>
    internal static (InvoiceSubmissionStatus Status, string? ReasonCode) TerminalFor(SubmissionEvaluationKind kind, string? reasonCode) => kind switch
    {
        SubmissionEvaluationKind.AutoRejected or SubmissionEvaluationKind.Blocked => (InvoiceSubmissionStatus.Rejected, reasonCode),
        SubmissionEvaluationKind.AcceptedUncoded => (InvoiceSubmissionStatus.Accepted, reasonCode),
        _ => (InvoiceSubmissionStatus.Accepted, null),
    };

    // ── Stored shapes ──

    /// <summary>
    /// The line's <c>sprk_extractionconfidence</c> (FR-04, notes section 5): per field its analyzer confidence, 046's combined confidence,
    /// page, box and repair; plus the <c>coding</c> block (code set id / name / source / needs review, codes, refused codes, firm-code
    /// mismatch). No extracted value is repeated here except a repaired value's original.
    /// </summary>
    internal static string BuildExtractionConfidence(
        ExtractedInvoiceLine line, ResolvedInvoiceLine resolution, LineCodingResult? coding, SubmissionEvaluationOutcome outcome)
    {
        var fields = new JsonObject
        {
            [ResolutionFieldNames.Date] = FieldJson(line.Date, resolution, ResolutionFieldNames.Date),
            [ResolutionFieldNames.TimekeeperAsBilled] = FieldJson(line.TimekeeperAsBilled, resolution, ResolutionFieldNames.TimekeeperAsBilled),
            ["TitleAsBilled"] = FieldJson(line.TitleAsBilled, resolution, "TitleAsBilled"),
            [ResolutionFieldNames.Hours] = FieldJson(line.Hours, resolution, ResolutionFieldNames.Hours),
            [ResolutionFieldNames.Rate] = FieldJson(line.Rate, resolution, ResolutionFieldNames.Rate),
            [ResolutionFieldNames.Quantity] = FieldJson(line.Quantity, resolution, ResolutionFieldNames.Quantity),
            [ResolutionFieldNames.UnitCost] = FieldJson(line.UnitCost, resolution, ResolutionFieldNames.UnitCost),
            [ResolutionFieldNames.Amount] = FieldJson(line.Amount, resolution, ResolutionFieldNames.Amount),
            [BillingFieldNames.Narrative] = FieldJson(line.Narrative, resolution, BillingFieldNames.Narrative),
            [BillingFieldNames.BilledTaskCode] = FieldJson(line.TaskCodeAsBilled, resolution, BillingFieldNames.BilledTaskCode),
            [BillingFieldNames.BilledActivityCode] = FieldJson(line.ActivityCodeAsBilled, resolution, BillingFieldNames.BilledActivityCode),
            [BillingFieldNames.BilledExpenseCode] = FieldJson(line.ExpenseCodeAsBilled, resolution, BillingFieldNames.BilledExpenseCode),
        };

        JsonObject codingJson;
        if (coding is null)
        {
            // D-126: not coded; the hold finding names why.
            codingJson = new JsonObject
            {
                ["status"] = "NotCoded",
                ["hold"] = outcome.PipelineFindings.LastOrDefault(f =>
                    f.ReasonCode is SubmissionReasonCodes.CodingNoCodeSetHold or SubmissionReasonCodes.CodingUnavailableHold)?.ReasonCode,
            };
        }
        else
        {
            codingJson = new JsonObject
            {
                ["status"] = coding.Status.ToString(),
                ["codeSetId"] = coding.CodeSetId.ToString("D"),
                ["codeSetName"] = coding.CodeSetName,
                ["codeSetSource"] = coding.CodeSetSource.ToString(),
                ["needsReview"] = coding.CodeSetNeedsReview,
                ["confidence"] = coding.Confidence,
                ["task"] = CodeJson(coding.Task),
                ["activity"] = CodeJson(coding.Activity),
                ["expense"] = CodeJson(coding.Expense),
                ["firmCodeMismatch"] = coding.HasFirmCodeMismatch,
                ["refused"] = new JsonArray(coding.RefusedCodes.Select(r => (JsonNode)new JsonObject
                {
                    ["kind"] = r.Kind.ToString(),
                    ["code"] = r.Code,
                    ["reason"] = r.Reason.ToString(),
                }).ToArray()),
                ["observations"] = new JsonArray(coding.Observations.Select(o => (JsonNode)new JsonObject
                {
                    ["type"] = o.Type.ToString(),
                    ["confidence"] = o.Confidence,
                }).ToArray()),
            };
        }

        // EB-D-170: the analyzer and API version that read the line, and the Action and version that coded it, so a correction names its source.
        return new JsonObject
        {
            ["fields"] = fields,
            ["coding"] = codingJson,
            [ExtractionProvenance.Member] = ExtractionProvenance.ForLine(outcome.Read!, coding is not null),
        }.ToJsonString(Json);
    }

    private static JsonObject FieldJson<T>(ExtractedInvoiceField<T> field, ResolvedInvoiceLine resolution, string name)
    {
        var json = new JsonObject
        {
            ["confidence"] = field.Confidence,
            ["combined"] = resolution.Fields.TryGetValue(name, out var combined) ? combined.Combined : null,
            ["page"] = field.Page,
            ["repair"] = field.Repair.ToString(),
        };
        if (field.BoundingBox is { } box)
        {
            json["box"] = new JsonObject { ["page"] = box.Page, ["x"] = box.X, ["y"] = box.Y, ["w"] = box.W, ["h"] = box.H };
        }

        if (field.Repair != ExtractedValueRepair.None)
        {
            json["returned"] = field.ReturnedValue switch
            {
                null => null,
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString(),
            };
        }

        return json;
    }

    private static JsonObject CodeJson(LineCodeValue value) => new()
    {
        ["code"] = value.Code,
        ["firmCode"] = value.FirmCode,
        ["mismatch"] = value.Mismatch,
    };

    /// <summary>
    /// The submission's <c>sprk_findingsjson</c> (for 130 / 133 and the resume path): every billing finding with its line, values read, rule
    /// type, policy, version and clause, and how 073 decided it; the pipeline findings; the line-level coding findings; the decision.
    /// <para><b>D-112.</b> In a (policy, subject) group that holds a withheld finding no finding carries values read or fact values, so the
    /// stored copy can never show a prior's detail the Signal hides.</para>
    /// </summary>
    internal static string BuildFindingsJson(SubmissionEvaluationOutcome outcome, Guid invoiceId) => BuildFindingsJson(outcome, invoiceId, out _);

    /// <summary>The findings JSON member that records the plan entries the writer's preflight refused (049 F1-3): <c>[{ policyCode, subject:
    /// { entity, id }, code }]</c>, no values. Present only when an entry was refused.</summary>
    internal const string SignalPlanRefusalsMember = "signalPlanRefusals";

    /// <summary>One plan entry <see cref="SignalWriter.PreflightRefusal"/> refused at persist (049 F1-3): it is not in the plan.</summary>
    internal sealed record SignalPlanRefusal(string PolicyCode, string SubjectEntity, Guid SubjectId, string Code);

    /// <inheritdoc cref="BuildFindingsJson(SubmissionEvaluationOutcome, Guid)"/>
    /// <param name="planRefusals">The plan entries left out because the writer would refuse them (<see cref="SignalPlanJson(SubmissionEvaluationOutcome, Guid, out IReadOnlyList{SignalPlanRefusal})"/>).</param>
    internal static string BuildFindingsJson(SubmissionEvaluationOutcome outcome, Guid invoiceId, out IReadOnlyList<SignalPlanRefusal> planRefusals)
    {
        var findings = outcome.Evaluation?.Findings ?? [];
        var decisions = (outcome.Decision?.Findings ?? []).ToDictionary(d => d.Index);
        var withheldGroups = findings.Where(f => f.DetailWithheld).Select(f => (f.PolicyId, f.Subject)).ToHashSet();

        var findingsJson = new JsonArray();
        for (var i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            var scrub = withheldGroups.Contains((f.PolicyId, f.Subject));
            decisions.TryGetValue(i, out var decided);
            findingsJson.Add(FindingEntryJson(i, f, decided, scrub));
        }

        var pipeline = new JsonArray(outcome.PipelineFindings.Select(p =>
        {
            var values = new JsonObject();
            foreach (var (name, value) in p.Values)
            {
                values[name] = value;
            }

            return (JsonNode)new JsonObject { ["reasonCode"] = p.ReasonCode, ["values"] = values };
        }).ToArray());

        var coding = new JsonArray();
        foreach (var line in outcome.Coding?.Lines ?? [])
        {
            if (line.RefusedCodes.Count == 0 && !line.HasFirmCodeMismatch)
            {
                continue;
            }

            coding.Add(new JsonObject
            {
                ["line"] = int.Parse(line.LineKey, CultureInfo.InvariantCulture),
                ["refused"] = new JsonArray(line.RefusedCodes.Select(r => (JsonNode)new JsonObject
                {
                    ["kind"] = r.Kind.ToString(),
                    ["code"] = r.Code,
                    ["reason"] = r.Reason.ToString(),
                }).ToArray()),
                ["task"] = CodeJson(line.Task),
                ["activity"] = CodeJson(line.Activity),
                ["expense"] = CodeJson(line.Expense),
            });
        }

        // EB-D-175 / EB-D-176: the lines left Flagged for lacking a check or a description, each with a stable code. Additive (schema 1 readers
        // ignore it); no Signal, no value: the reviewer decides the line from its Flagged status.
        var codedByKey = outcome.Coding?.Lines.ToDictionary(l => l.LineKey, StringComparer.Ordinal);
        var lineFindings = new JsonArray();
        for (var index = 0; index < outcome.Read!.Lines.Count; index++)
        {
            LineCodingResult? lineCoding = null;
            codedByKey?.TryGetValue(index.ToString(CultureInfo.InvariantCulture), out lineCoding);
            if (LineFindingCodeFor(outcome.Read.Lines[index].Narrative.Value, lineCoding) is { } lineCode)
            {
                lineFindings.Add(LineFindingEntryJson(index, lineCode));
            }
        }

        var root = new JsonObject
        {
            ["schema"] = 1,
            ["kind"] = outcome.Kind.ToString(),
            ["reasonCode"] = outcome.ReasonCode,
            ["invoiceId"] = invoiceId.ToString("D"),
            ["trackingNumber"] = outcome.Submission!.TrackingNumber,
            ["receivedOn"] = outcome.ReceivedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["decision"] = outcome.Decision is { } decision
                ? new JsonObject
                {
                    ["outcome"] = decision.Outcome.ToString(),
                    ["reliabilityThreshold"] = decision.ReliabilityThreshold,
                    ["deciding"] = new JsonArray(decision.DecidingFindings.Select(d => (JsonNode)JsonValue.Create(d.Index)!).ToArray()),
                }
                : null,
            ["codeSet"] = outcome.Coding is { } coded
                ? new JsonObject
                {
                    ["id"] = coded.CodeSet.CodeSetId.ToString("D"),
                    ["name"] = coded.CodeSet.CodeSetName,
                    ["source"] = coded.CodeSet.Source.ToString(),
                    ["needsReview"] = coded.CodeSet.Source == CodeSetSource.AiSuggestion,
                }
                : null,
            ["findings"] = findingsJson,
            ["pipelineFindings"] = pipeline,
            ["codingFindings"] = coding,
            [LineFindingsMember] = lineFindings,
            ["invoiceName"] = InvoiceNameFor(outcome),
            ["invoiceNumberReadConfidence"] = outcome.InvoiceNumber?.ReadConfidence,
            ["signalPlan"] = SignalPlanJson(outcome, invoiceId, out planRefusals),

            // EB-D-170: the header's home for provenance: the pair, and the header fields as read (a correction of one still names what the AI read).
            [ExtractionProvenance.Member] = ExtractionProvenance.ForSubmission(outcome.Read!, outcome.Coding is not null, outcome.Resolution),
        };
        if (planRefusals.Count > 0)
        {
            root[SignalPlanRefusalsMember] = new JsonArray(planRefusals.Select(r => (JsonNode)new JsonObject
            {
                ["policyCode"] = r.PolicyCode,
                ["subject"] = new JsonObject { ["entity"] = r.SubjectEntity, ["id"] = r.SubjectId.ToString("D") },
                ["code"] = r.Code,
            }).ToArray());
        }

        return root.ToJsonString(Json);
    }

    /// <summary>
    /// The Signal plan (notes section 5; part D): the billing findings mapped by <see cref="BillingFindingSignalMapper.MapAll(BillingEvaluationResult, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>
    /// with 073's decisions and the outcome kind (the deciding reason is the primary one; a held Reject group carries the Held line; under a
    /// Blocked outcome a deciding group is presented as held for review), one entry per
    /// (policy, subject): the merged, scrubbed request (headline, lane, severity, template, facts, evidence), its rendered sentence, its
    /// reason codes, consequence, fingerprint and 073's group decision. Never <see cref="BillingSignalMapping.Findings"/> (072 part E
    /// re-check, D-112). Part D writes the rule Signals from this stored copy on every run, the first and a resume alike, so it never
    /// re-runs AI or the evaluation. The system findings are not in the plan: part D maps them from the pipeline and coding findings under
    /// the system policy it reads at run time (D-125).
    /// </summary>
    internal static JsonArray SignalPlanJson(SubmissionEvaluationOutcome outcome, Guid invoiceId) => SignalPlanJson(outcome, invoiceId, out _);

    /// <inheritdoc cref="SignalPlanJson(SubmissionEvaluationOutcome, Guid)"/>
    /// <remarks>049 F1-3: every entry is first checked by <see cref="SignalWriter.PreflightRefusal"/>, the writer's own I/O-free checks. An
    /// entry it refuses (a sentence over 2,000 characters, a headline over 200, an unrenderable template, ...) would be refused on every
    /// write attempt, so it is left out of the plan and returned in <paramref name="refusals"/>; the rest of the plan is kept.</remarks>
    internal static JsonArray SignalPlanJson(SubmissionEvaluationOutcome outcome, Guid invoiceId, out IReadOnlyList<SignalPlanRefusal> refusals)
    {
        var plan = new JsonArray();
        var refused = new List<SignalPlanRefusal>();
        refusals = refused;
        if (outcome.Evaluation is not { } evaluation)
        {
            return plan;
        }

        var mappings = BillingFindingSignalMapper.MapAll(evaluation, SignalSubjectsFor(outcome, invoiceId), outcome.Decision?.Findings, outcome.Kind);
        foreach (var mapping in mappings)
        {
            var request = mapping.Request;
            if (SignalWriter.PreflightRefusal(request) is { } code)
            {
                refused.Add(new SignalPlanRefusal(request.PolicyCode, request.SubjectEntityLogicalName, request.SubjectId, code));
                continue;
            }

            plan.Add(SignalPlanEntryJson(mapping));
        }

        return plan;
    }

    /// <summary>
    /// One <c>signalPlan[]</c> entry from a mapped (policy, subject) group (B2a: a per-entry builder so a correction's re-evaluation can replace ONE entry
    /// of the stored plan). The caller has already passed the request through <see cref="SignalWriter.PreflightRefusal"/>.
    /// </summary>
    internal static JsonObject SignalPlanEntryJson(BillingSignalMapping mapping)
    {
        var request = mapping.Request;
        var facts = new JsonObject();
        foreach (var (name, value) in request.FactValues)
        {
            facts[name] = value?.ToString();
        }

        return new JsonObject
        {
            ["policyId"] = request.PolicyId.ToString("D"),
            ["policyCode"] = request.PolicyCode,
            ["policyVersionId"] = request.PolicyVersionId.ToString("D"),
            ["subjectEntity"] = request.SubjectEntityLogicalName,
            ["subjectId"] = request.SubjectId.ToString("D"),
            ["subjectName"] = request.SubjectDisplayName,
            ["headline"] = request.ShortHeadline,
            ["lane"] = request.Lane,
            ["severity"] = request.Severity,
            ["template"] = request.MessageTemplate,
            ["facts"] = facts,
            ["sentence"] = SignalWriter.RenderSentence(request.MessageTemplate, request.FactValues),
            ["evidence"] = new JsonArray((request.EvidenceRefs ?? []).Select(e => (JsonNode)new JsonObject
            {
                ["kind"] = e.Kind,
                ["ref"] = e.Ref,
                ["tier"] = e.Tier,
                ["confidence"] = e.Confidence,
                ["text"] = e.Text,
                ["source"] = e.Source,
                ["asOf"] = e.AsOf,
                ["clause"] = e.Clause,
            }).ToArray()),
            ["reasonCodes"] = new JsonArray(mapping.ReasonCodes.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
            ["consequence"] = mapping.Consequence.ToString(),
            ["fingerprint"] = mapping.Fingerprint,
            ["decision"] = mapping.Decision.ToString(),
        };
    }

    /// <summary>
    /// One <c>findings[]</c> entry (B2a: a per-entry builder so a correction's re-evaluation can patch ONE entry of the stored copy). <paramref name="scrub"/>
    /// is D-112: the (policy, subject) group holds a withheld finding, so this entry carries no values read and no suggestion.
    /// </summary>
    internal static JsonObject FindingEntryJson(int index, BillingFinding f, FindingDecision? decided, bool scrub)
    {
        var entry = new JsonObject
        {
            ["index"] = index,
            ["ruleType"] = f.RuleType.ToString(),
            ["policyId"] = f.PolicyId.ToString("D"),
            ["policyCode"] = f.PolicyCode,
            ["policyVersionId"] = f.PolicyVersionId.ToString("D"),
            ["clause"] = f.ClauseReference,
            ["clauseLocation"] = f.ClauseLocation,
            ["subject"] = f.Subject.Kind.ToString(),
            ["line"] = f.Subject.LineIndex,
            ["reasonCode"] = f.ReasonCode,
            ["detailWithheld"] = f.DetailWithheld || scrub,
            ["consequence"] = decided?.Consequence?.ToString(),
            ["rejects"] = decided?.Rejects ?? false,
            ["heldBecause"] = decided?.HeldBecause,
        };
        if (!scrub)
        {
            entry["valuesRead"] = new JsonArray(f.ValuesRead.Select(v => (JsonNode)new JsonObject
            {
                ["name"] = v.Name,
                ["value"] = v.Value,
                ["line"] = v.ExtractedField?.LineIndex,
                ["field"] = v.ExtractedField?.Field,
                ["confidence"] = v.Confidence,
                ["belowReliabilityThreshold"] = v.BelowReliabilityThreshold,
            }).ToArray());

            // Task 100 (the Invoice Review read model, FR-31): the adjustment the rule suggests, for the reviewer's "apply suggestion"
            // click. An additive member of the stored copy (schema 1 readers ignore it); a reviewer decision applies it, never a handler.
            // Left out of a scrubbed group with the rest of the detail (D-112), and when the rule suggests none.
            if (f.SuggestedAdjustment is { } suggestion)
            {
                entry["suggestedAdjustment"] = new JsonObject
                {
                    ["kind"] = suggestion.Kind.ToString(),
                    ["amount"] = suggestion.Amount,
                };
            }
        }

        return entry;
    }

    /// <summary>One <c>lineFindings[]</c> entry (EB-D-175 / EB-D-176; B2a: a per-entry builder). <paramref name="code"/> is <see cref="LineFindingCodeFor"/>'s.</summary>
    internal static JsonObject LineFindingEntryJson(int lineIndex, string code) => new()
    {
        ["line"] = lineIndex,
        ["code"] = code,
        ["message"] = code == LineFindingNotReturned ? "The line was not checked by the AI." : "The line has no description.",
    };

    // ── Helpers ──

    private static void Validate(SubmissionEvaluationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Kind is not (SubmissionEvaluationKind.Accepted or SubmissionEvaluationKind.AutoRejected
            or SubmissionEvaluationKind.Blocked or SubmissionEvaluationKind.AcceptedUncoded))
        {
            throw new ArgumentException($"A {outcome.Kind} outcome creates no invoice (D-101); only a read submission is persisted.", nameof(outcome));
        }

        // D-130 (1): the rules run on uncoded submissions too, so every persistable kind carries its evaluation and decision. Only Accepted
        // proves its lines were coded; an AutoRejected / Blocked / AcceptedUncoded outcome may carry no coding (and then a hold finding).
        if (outcome.Submission is null || outcome.Read is null || outcome.Resolution is null
            || outcome.Evaluation is null || outcome.Decision is null
            || (outcome.Kind == SubmissionEvaluationKind.Accepted && outcome.Coding is null))
        {
            throw new ArgumentException($"A {outcome.Kind} outcome misses a member its kind requires.", nameof(outcome));
        }
    }

    /// <summary>The column on <paramref name="table"/> that names <paramref name="core"/> (013's map: typed or <c>sprk_regarding{core}</c>).</summary>
    /// <summary>
    /// EB-D-168a: the ownership question for the Stored File rows this transaction writes under the invoice it creates — the
    /// <c>sprk_storedfile</c> table (a hard-deny table) through the one builder, filed under the invoice's core record (see the call site).
    /// </summary>
    internal static RecordOwnershipContext StoredFileOwnerContext(InvoiceCoreRecord core) =>
        RecordOwnershipContext.ForRow(
            StoredFileSchema.Entity,
            new[] { KeyValuePair.Create(core.EntityLogicalName, (RecordOwnershipParent?)new RecordOwnershipParent(core.EntityLogicalName, core.RecordId)) });

    internal static string CoreColumn(string table, InvoiceCoreRecord core) =>
        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Single(l => l.CoreEntity == core.EntityLogicalName).Column;

    /// <summary>Every row is decided by 013's guard as built (FR-05): a refusal here is a builder defect, never sent.</summary>
    private static void RequireFiled(Entity row)
    {
        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
        {
            throw new InvalidOperationException($"The {row.LogicalName} row names no single core record ({refused.RefusalCode}).");
        }
    }

    private static SubmissionPersistResult Result(SubmissionPersistStatus status, Guid submissionId, string code) =>
        new() { Status = status, SubmissionId = submissionId, ReasonCode = code };

    private static DateTime Utc(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static string? Cap(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > TextMax ? trimmed[..TextMax] : trimmed;
    }

    private static void SetText(Entity row, string column, string? value)
    {
        if (Cap(value) is { } text)
        {
            row[column] = text;
        }
    }
}
