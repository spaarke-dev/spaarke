// ebilling-r1 task 049 part C (spec FR-20, D-101, D-121, D-124, D-126; notes 049-submission-processing-job.md "Part C").
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceReviewService.ConfirmAsync creates an invoice for a confirmed document (a Web API PATCH, matter only, no lines,
//       no status); InvoiceSubmissionService creates the submission; InvoiceLifecycleService.BuildCreateRequest (014) builds the invoice
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
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="IInvoiceSubmissionPersister"/>: step 6 of the Processing Pipeline, all-or-nothing.
/// </summary>
/// <remarks>
/// <para><b>One transaction</b> (FR-20), in this order: the invoice create(s) from 014, one create per line, the approval portion (not for an
/// automatic rejection), the document's <c>sprk_invoice</c> link, and the submission update (<c>sprk_invoice</c>, the terminal status and
/// reason code, <c>sprk_findingsjson</c>). A linked <c>sprk_invoice</c> therefore proves the commit; it is the resume marker.</para>
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

    /// <summary><c>sprk_invoicesubmission.sprk_findingsjson</c> MaxLength (1,048,576).</summary>
    internal const int FindingsJsonMaxLength = 1_048_576;

    /// <summary><c>sprk_billingevent.sprk_extractionconfidence</c> MaxLength (100,000).</summary>
    internal const int ExtractionConfidenceMaxLength = 100_000;

    /// <summary>The request bound per transaction (part A: the bound <c>BulkUpdateAsync</c> uses).</summary>
    internal const int MaxRequestsPerTransaction = 1000;

    internal const string DocumentInvoice = "sprk_invoice";
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

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly IGenericEntityService _entities;
    private readonly ICommunicationDataverseService _recordTypes;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IInvoiceLifecycleService _lifecycle;
    private readonly IInvoiceTotalsService _totals;
    private readonly ILogger<InvoiceSubmissionPersister> _logger;

    public InvoiceSubmissionPersister(
        IGenericEntityService entities,
        ICommunicationDataverseService recordTypes,
        IRecordOwnershipResolver ownership,
        IInvoiceLifecycleService lifecycle,
        IInvoiceTotalsService totals,
        ILogger<InvoiceSubmissionPersister> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The submission columns the persister re-reads: the snapshot's (core record, link, status) and the correlation id.</summary>
    internal static string[] SubmissionColumns { get; } = [.. SubmissionSnapshot.Columns, SubmissionCorrelationId];

    /// <summary>The invoice id for a submission: deterministic, so a retry reuses it and can never create a second invoice.</summary>
    public static Guid InvoiceIdFor(Guid submissionId) => IdOf($"ebilling-invoice-{submissionId:D}");

    /// <summary>The id of line <paramref name="lineIndex"/> of a submission's invoice.</summary>
    public static Guid LineIdFor(Guid submissionId, int lineIndex) =>
        IdOf($"ebilling-line-{submissionId:D}-{lineIndex.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>The id of a submission's approval portion.</summary>
    public static Guid ApprovalIdFor(Guid submissionId) => IdOf($"ebilling-approval-{submissionId:D}");

    private static Guid IdOf(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));

    /// <inheritdoc />
    public async Task<SubmissionPersistResult> PersistAsync(SubmissionEvaluationOutcome outcome, string? correlationId, CancellationToken ct = default)
    {
        Validate(outcome);
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
            return await ResumeAsync(submissionId, linkedId, ct).ConfigureAwait(false);
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

        var owner = await _ownership.ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new(core.EntityLogicalName, core.RecordId) }), ct).ConfigureAwait(false);
        if (!owner.IsOwned)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: no owner resolved ({Code}); nothing persisted", submissionId, owner.RefusalCode);
            return Result(SubmissionPersistStatus.Refused, submissionId, owner.RefusalCode ?? OwnerUnresolvedCode);
        }

        // ── Rows ──
        var resolverFields = decided.ResolverFields.ToFields();
        var correlation = Cap(correlationId ?? current.GetAttributeValue<string>(SubmissionCorrelationId));
        var lines = BuildLines(outcome, invoiceId, core, resolverFields, owner, correlation);
        FillInvoice(invoice, outcome, resolverFields, owner, correlation);

        var initial = outcome.Kind == SubmissionEvaluationKind.AutoRejected ? InvoiceLifecycleStatus.Rejected : InvoiceLifecycleStatus.InReview;
        var create = _lifecycle.BuildCreateRequest(invoice, initial);
        if (!create.Succeeded)
        {
            throw new InvalidOperationException($"The lifecycle service refused the invoice create ({create.Reason}).");
        }

        Entity? approval = null;
        if (outcome.Kind != SubmissionEvaluationKind.AutoRejected)
        {
            approval = BuildApproval(outcome, invoiceId, core, resolverFields, owner);
        }

        var findingsJson = BuildFindingsJson(outcome, invoiceId);
        if (findingsJson.Length > FindingsJsonMaxLength)
        {
            _logger.LogWarning("Invoice submission {SubmissionId}: findings JSON is {Length} characters, over the column; nothing persisted",
                submissionId, findingsJson.Length);
            return Result(SubmissionPersistStatus.Refused, submissionId, FindingsTooLargeCode);
        }

        var requests = new List<OrganizationRequest>(create.Requests);
        requests.AddRange(lines.Select(line => new CreateRequest { Target = line }));
        if (approval is not null)
        {
            requests.Add(new CreateRequest { Target = approval });
        }

        requests.Add(new UpdateRequest
        {
            Target = new Entity(DocumentEntity, outcome.Submission!.DocumentId)
            {
                [DocumentInvoice] = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId),
            },
        });
        requests.Add(new UpdateRequest { Target = BuildSubmissionUpdate(outcome, invoiceId, findingsJson) });

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
            return Result(SubmissionPersistStatus.Retry, submissionId, TransactionFailedCode);
        }

        _logger.LogInformation("Invoice submission {SubmissionId}: committed invoice {InvoiceId} ({Kind}) with {LineCount} lines in {RequestCount} requests",
            submissionId, invoiceId, outcome.Kind, lines.Count, requests.Count);

        return await WithTotalsAsync(new SubmissionPersistResult
        {
            Status = SubmissionPersistStatus.Committed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            LineIds = lineIds,
            ApprovalId = approval?.Id,
        }, ct).ConfigureAwait(false);
    }

    // ── Resume (the submission already links an invoice) ──

    private async Task<SubmissionPersistResult> ResumeAsync(Guid submissionId, Guid invoiceId, CancellationToken ct)
    {
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
        return await WithTotalsAsync(new SubmissionPersistResult
        {
            Status = SubmissionPersistStatus.Resumed,
            SubmissionId = submissionId,
            InvoiceId = invoiceId,
            LineIds = lineIds,
        }, ct).ConfigureAwait(false);
    }

    /// <summary>016 after the commit (map row 6c). A fault is a retry that keeps the invoice id: the resume path recalculates again.</summary>
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

        invoice[InvoiceReviewService.InvName] = InvoiceReviewService.BuildInvoiceName(
            header.FirmName.Value, snapshot.FirmInvoiceNumber, header.InvoiceDate.Value?.ToDateTime(TimeOnly.MinValue));
        invoice[InvoiceReviewService.InvInvoiceNumber] = snapshot.FirmInvoiceNumber;
        invoice[InvoiceReviewService.InvTrackingNumber] = snapshot.TrackingNumber;
        invoice[InvoiceVendorOrg] = new EntityReference("sprk_organization", snapshot.FirmId);
        invoice[InvoiceDocument] = new EntityReference(DocumentEntity, snapshot.DocumentId);
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
                [LineName] = $"{snapshot.TrackingNumber} #{(index + 1).ToString(CultureInfo.InvariantCulture)}",
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
                || coding is { HasFirmCodeMismatch: true };
            row[InvoiceTotalsService.LineStatus] = new OptionSetValue(flagged ? LineStatusFlagged : LineStatusPending);

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

        owner.ApplyTo(row);
        RequireFiled(row);
        return row;
    }

    internal static Entity BuildSubmissionUpdate(SubmissionEvaluationOutcome outcome, Guid invoiceId, string findingsJson)
    {
        var status = outcome.Kind is SubmissionEvaluationKind.AutoRejected or SubmissionEvaluationKind.Blocked
            ? InvoiceSubmissionStatus.Rejected
            : InvoiceSubmissionStatus.Accepted;
        return new Entity(FinanceBaseValues.SubmissionEntity, outcome.SubmissionId)
        {
            [SubmissionInvoice] = new EntityReference(InvoiceReviewService.InvoiceEntity, invoiceId),
            [SubmissionStatus] = new OptionSetValue((int)status),
            [SubmissionReasonCode] = outcome.Kind is SubmissionEvaluationKind.AutoRejected or SubmissionEvaluationKind.Blocked ? outcome.ReasonCode : null,
            [SubmissionFindingsJson] = findingsJson,
        };
    }

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

        return new JsonObject { ["fields"] = fields, ["coding"] = codingJson }.ToJsonString(Json);
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
    internal static string BuildFindingsJson(SubmissionEvaluationOutcome outcome, Guid invoiceId)
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
            var entry = new JsonObject
            {
                ["index"] = i,
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
            }

            findingsJson.Add(entry);
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
        };
        return root.ToJsonString(Json);
    }

    // ── Helpers ──

    private static void Validate(SubmissionEvaluationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Kind is not (SubmissionEvaluationKind.Accepted or SubmissionEvaluationKind.AutoRejected
            or SubmissionEvaluationKind.Blocked or SubmissionEvaluationKind.AcceptedUncoded))
        {
            throw new ArgumentException($"A {outcome.Kind} outcome creates no invoice (D-101); only a read submission is persisted.", nameof(outcome));
        }

        var uncoded = outcome.Kind == SubmissionEvaluationKind.AcceptedUncoded;
        if (outcome.Submission is null || outcome.Read is null || outcome.Resolution is null
            || (!uncoded && (outcome.Coding is null || outcome.Evaluation is null || outcome.Decision is null)))
        {
            throw new ArgumentException($"A {outcome.Kind} outcome misses a member its kind requires.", nameof(outcome));
        }
    }

    /// <summary>The column on <paramref name="table"/> that names <paramref name="core"/> (013's map: typed or <c>sprk_regarding{core}</c>).</summary>
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
