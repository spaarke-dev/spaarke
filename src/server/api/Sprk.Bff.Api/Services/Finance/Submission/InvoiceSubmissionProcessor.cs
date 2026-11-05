using System.Globalization;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// <see cref="IInvoiceSubmissionProcessor"/>: Processing Pipeline steps 1-5 (ebilling-r1 task 049 part B; spec FR-20, FR-21, FR-26).
/// Composes the read (043), resolution (046), coding (047), evaluation (072) and decision (073) contracts unchanged, and owns only the
/// mapping between them, the D-91 comparison and the classification (notes 049 sections 1-2).
/// </summary>
/// <remarks>
/// <para><b>No AI output alone rejects (I-5).</b> A rejection comes only from <see cref="SubmissionRejectionRule"/>; the processor never
/// turns a read, a coding result or an observation into a rejection itself. The D-91 mismatch holds and never rejects.</para>
/// <para><b>Fail closed.</b> A submission without a tracking number (D-87) or <c>createdon</c> is Failed before anything is read; the
/// received date is never defaulted (072 part W).</para>
/// <para><b>Logs (ADR-015)</b> carry the submission id, counts and reason codes, never extracted values or narratives.</para>
/// </remarks>
public sealed class InvoiceSubmissionProcessor : IInvoiceSubmissionProcessor
{
    private readonly IInvoiceAi _ai;
    private readonly IInvoiceResolutionService _resolution;
    private readonly ILineCodingService _coding;
    private readonly Func<BillingEvaluationRequest, CancellationToken, Task<BillingEvaluationResult>> _evaluate;
    private readonly SubmissionRejectionRule _decider;
    private readonly IOptionsMonitor<EbillingOptions> _ebilling;
    private readonly ILogger<InvoiceSubmissionProcessor> _logger;

    /// <summary>Task 097b: reads a Correction's replaced invoice (<see cref="SubmissionSnapshot.ReadCorrectedInvoiceAsync"/>). Null only in
    /// tests built on the evaluation seam.</summary>
    private readonly IGenericEntityService? _entities;

    public InvoiceSubmissionProcessor(
        IInvoiceAi ai,
        IInvoiceResolutionService resolution,
        ILineCodingService coding,
        InvoiceComplianceEvaluator evaluator,
        SubmissionRejectionRule decider,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionProcessor> logger,
        IGenericEntityService entities)
        : this(ai, resolution, coding, EvaluateWith(evaluator), decider, ebilling, logger,
            entities ?? throw new ArgumentNullException(nameof(entities)))
    {
    }

    /// <summary>Test seam: the evaluation step. 072's loader reads Dataverse, so tests run the real in-memory evaluation on loaded facts.</summary>
    internal InvoiceSubmissionProcessor(
        IInvoiceAi ai,
        IInvoiceResolutionService resolution,
        ILineCodingService coding,
        Func<BillingEvaluationRequest, CancellationToken, Task<BillingEvaluationResult>> evaluate,
        SubmissionRejectionRule decider,
        IOptionsMonitor<EbillingOptions> ebilling,
        ILogger<InvoiceSubmissionProcessor> logger,
        IGenericEntityService? entities = null)
    {
        _entities = entities;
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        _coding = coding ?? throw new ArgumentNullException(nameof(coding));
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
        _decider = decider ?? throw new ArgumentNullException(nameof(decider));
        _ebilling = ebilling ?? throw new ArgumentNullException(nameof(ebilling));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static Func<BillingEvaluationRequest, CancellationToken, Task<BillingEvaluationResult>> EvaluateWith(InvoiceComplianceEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        return evaluator.EvaluateAsync;
    }

    public async Task<SubmissionEvaluationOutcome> EvaluateAsync(
        Entity submission,
        Func<SubmissionSnapshot, CancellationToken, Task<InvoiceReadRequest>> loadDocument,
        bool finalDelivery,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(loadDocument);

        // ADR-018: the flag first (the handler checks it too; this keeps the processor safe on its own).
        if (!_ebilling.CurrentValue.Enabled)
        {
            return Log(SubmissionEvaluationOutcome.Failed(submission.Id, SubmissionReasonCodes.EbillingDisabled));
        }

        var (snapshot, failure) = SubmissionSnapshot.Read(submission);
        if (snapshot is null)
        {
            return Log(SubmissionEvaluationOutcome.Failed(submission.Id, failure!));
        }

        // Step 1: read (043). Document-level failures reject without an invoice (FR-21, D-101); service_failed and anything
        // transient propagate for the job to retry.
        ExtractedInvoice read;
        try
        {
            var document = await loadDocument(snapshot, ct).ConfigureAwait(false);
            read = await _ai.ReadInvoiceAsync(document, ct).ConfigureAwait(false);
        }
        catch (InvoiceAiException ex) when (SubmissionReasonCodes.IsDocumentLevelReadFailure(ex.ReasonCode))
        {
            return Log(new SubmissionEvaluationOutcome
            {
                Kind = SubmissionEvaluationKind.RejectedUnreadable,
                SubmissionId = snapshot.SubmissionId,
                ReasonCode = ex.ReasonCode,
                Submission = snapshot,
            });
        }
        catch (FeatureDisabledException ex)
        {
            return Log(SubmissionEvaluationOutcome.Failed(snapshot.SubmissionId, ex.ErrorCode, snapshot));
        }

        // Step 1c: D-91. A difference holds for review; it never rejects and never changes the tracking number.
        var numberCheck = InvoiceNumberCheck.Compare(snapshot.FirmInvoiceNumber, read.Header.FirmInvoiceNumber);
        IReadOnlyList<SubmissionPipelineFinding> pipelineFindings = numberCheck.Result == InvoiceNumberMatch.Mismatch
            ? [new SubmissionPipelineFinding(
                SubmissionReasonCodes.InvoiceNumberMismatch,
                new Dictionary<string, string?> { ["typed"] = numberCheck.Typed, ["read"] = numberCheck.Read })]
            : [];

        // Task 095c-3 (owner 2026-11-03): an Internal submission taken without a firm is held "firm needed" (a pipeline finding, as D-91's),
        // never rejected. It SKIPS every firm-scoped step rather than half-running it: resolution reads no roster and no rates (its lines are
        // unresolved), coding skips the firm-code step (the record's code set still applies), and steps 4 and 5 do not run at all, so no
        // client-default rule set, no Duplicate prior read and no partial evaluation marks a rule as checked (the persister flags every line
        // rules.not_checked). Task 095d's assign-firm runs the full pipeline once the firm is set.
        var noFirm = snapshot.HasNoFirm;
        if (noFirm)
        {
            pipelineFindings = [.. pipelineFindings, new SubmissionPipelineFinding(SubmissionReasonCodes.FirmMissing, new Dictionary<string, string?>())];
        }

        InvoiceResolutionResult resolution;
        try
        {
            // Step 2: resolve (046).
            resolution = await _resolution.ResolveAsync(BuildResolutionInput(snapshot, read), ct).ConfigureAwait(false);
        }
        catch (FeatureDisabledException ex)
        {
            return Log(SubmissionEvaluationOutcome.Failed(snapshot.SubmissionId, ex.ErrorCode, snapshot, read));
        }

        // Step 3: code (047). D-126 / D-130: coding that cannot run after a successful read is not a failure, and it does not skip the
        // rules. The lines stay uncoded and the invoice is held by a pipeline finding, but 072 and 073 still judge the firm's billed data:
        // a rule-based rejection rejects as usual (D-101); otherwise the invoice is held by the coding finding plus any rule findings.
        // Uncoded lines carry no AI-assigned code, so a rule that would read one holds as ai_assigned_input (D-117), never rejects.
        // The coding InvoiceAiException (a model fault) still propagates for the job to retry.
        InvoiceCodingResult? coding = null;
        string? codingHold = null;
        try
        {
            coding = await _coding.CodeLinesAsync(BuildCodingRequest(snapshot, read), ct).ConfigureAwait(false);
        }
        catch (FeatureDisabledException ex)
        {
            codingHold = SubmissionReasonCodes.CodingUnavailableHold;
            pipelineFindings = [.. pipelineFindings, CodingHoldFinding(codingHold, ex.ErrorCode)];
        }
        catch (LineCodingException ex)
        {
            codingHold = ex.ReasonCode == SubmissionReasonCodes.NoCodeSet
                ? SubmissionReasonCodes.CodingNoCodeSetHold
                : SubmissionReasonCodes.CodingUnavailableHold;
            pipelineFindings = [.. pipelineFindings, CodingHoldFinding(codingHold, ex.ReasonCode)];
        }

        if (noFirm)
        {
            // Steps 4 and 5 skipped (above). Accepted (coded) or AcceptedUncoded (D-126), both with the firm hold as the reason: it is the hold
            // that decides approval; a coding hold stays a pipeline finding beside it.
            return Log(new SubmissionEvaluationOutcome
            {
                Kind = coding is null ? SubmissionEvaluationKind.AcceptedUncoded : SubmissionEvaluationKind.Accepted,
                SubmissionId = snapshot.SubmissionId,
                ReasonCode = SubmissionReasonCodes.FirmMissing,
                Submission = snapshot,
                Read = read,
                InvoiceNumber = numberCheck,
                PipelineFindings = pipelineFindings,
                Resolution = resolution,
                Coding = coding,
            });
        }

        // Step 4: evaluate (072), on the core record and received date of this same submission read.
        var request = BuildEvaluationRequest(snapshot, read, resolution, coding) with
        {
            ReplacedInvoiceId = await ReplacedInvoiceAsync(snapshot, ct).ConfigureAwait(false),
            SubmittedFirmInvoiceNumber = snapshot.FirmInvoiceNumber,
        };
        BillingEvaluationResult evaluation;
        try
        {
            evaluation = await _evaluate(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (finalDelivery && UnresolvedCauseOf(ex) is { } cause)
        {
            // 082c-2 (brief C14, ontology-r1 ruling; O2): the Procedures could not be resolved and this is the job's LAST delivery, so the
            // invoice is held, never judged on a partial set (not on [W0] alone) and never passed. No rule finding at all (no evaluation, no
            // decision); every line is rules.not_checked with this code as its cause (the persister). Any earlier delivery does not reach
            // here: the filter is false, the fault propagates as it is, the job retries, and nothing is persisted. The log names the type
            // only (C16: never ex.Message, which names row ids).
            _logger.LogWarning("Invoice submission {SubmissionId}: the Procedures could not be resolved on the final delivery ({FaultType}, {Cause}); held",
                snapshot.SubmissionId, ex.GetType().Name, cause);
            var unresolved = new SubmissionPipelineFinding(
                SubmissionReasonCodes.ProceduresUnresolved,
                new Dictionary<string, string?> { [SubmissionReasonCodes.ProcedureCauseValue] = cause });
            var (heldKind, heldReason) = HoldFor(snapshot.Channel, SubmissionReasonCodes.ProceduresUnresolved, coded: coding is not null);
            return Log(new SubmissionEvaluationOutcome
            {
                Kind = heldKind,
                SubmissionId = snapshot.SubmissionId,
                ReasonCode = heldReason,
                Submission = snapshot,
                Read = read,
                InvoiceNumber = numberCheck,
                PipelineFindings = [.. pipelineFindings, unresolved],
                Resolution = resolution,
                Coding = coding,
                EvaluationRequest = request,
            });
        }

        // 082c-2 (brief C11, C13): one pipeline finding per gapped Procedure (its name, first gapped date and kind; never an id). They hold the
        // invoice (Classify below) and never reach 073: a gap is not a rule finding.
        pipelineFindings = [.. pipelineFindings, .. ProcedureGapFindings(evaluation)];
        var gapped = evaluation.Gaps.Count > 0;

        // Step 5: decide (073). The only source of a rejection.
        var decision = await _decider.DecideAsync(evaluation, ct).ConfigureAwait(false);

        var (kind, reasonCode) = Classify(decision, snapshot.Channel, gapped);
        if (kind == SubmissionEvaluationKind.Accepted && codingHold is not null)
        {
            // D-130 (b): accepted but uncoded; the reason code carries the coding code. 082c-2: the gap reason wins over a coding code (it is
            // the hold that decides approval); the coding hold stays a pipeline finding beside it.
            kind = SubmissionEvaluationKind.AcceptedUncoded;
            reasonCode = gapped ? reasonCode : codingHold;
        }

        return Log(new SubmissionEvaluationOutcome
        {
            Kind = kind,
            SubmissionId = snapshot.SubmissionId,
            ReasonCode = reasonCode,
            Submission = snapshot,
            Read = read,
            InvoiceNumber = numberCheck,
            PipelineFindings = pipelineFindings,
            Resolution = resolution,
            Coding = coding,
            EvaluationRequest = request,
            Evaluation = evaluation,
            Decision = decision,
        });
    }

    /// <summary>D-126: the hold finding of lines that were read and resolved but not coded; it names its cause.</summary>
    private static SubmissionPipelineFinding CodingHoldFinding(string holdCode, string cause) =>
        new(holdCode, new Dictionary<string, string?> { ["cause"] = cause });

    /// <summary>Step 5a (notes 049 section 2): Accepted; Rejected on SPA is an automatic rejection; Rejected on Internal is blocked.
    /// 082c-2 (brief C11): with a Procedure gap, Internal is Blocked and SPA is Accepted with <see cref="SubmissionReasonCodes.ProcedureGap"/>,
    /// whatever 073 decided: never AutoRejected, even over a 073 rejection (the gap reason wins).</summary>
    internal static (SubmissionEvaluationKind Kind, string? ReasonCode) Classify(SubmissionDecision decision, InvoiceSubmissionChannel channel, bool procedureGap)
    {
        if (procedureGap)
        {
            return HoldFor(channel, SubmissionReasonCodes.ProcedureGap, coded: true);
        }

        if (decision.Outcome != SubmissionOutcome.Rejected)
        {
            return (SubmissionEvaluationKind.Accepted, null);
        }

        // A Rejected decision always has a deciding finding (073: Rejected iff a finding rejects).
        var deciding = decision.DecidingFindings;
        if (deciding.Count == 0)
        {
            throw new InvalidOperationException("The rejection rule returned Rejected with no deciding finding.");
        }

        return channel == InvoiceSubmissionChannel.Spa
            ? (SubmissionEvaluationKind.AutoRejected, deciding[0].Finding.ReasonCode)
            : (SubmissionEvaluationKind.Blocked, SubmissionReasonCodes.Blocked);
    }

    /// <summary>082c-2: a Procedure hold (<paramref name="holdCode"/>: the gap or the unresolved hold). Internal is Blocked (the FR-20 hold);
    /// SPA is Accepted, or AcceptedUncoded when the lines were not coded, with the hold as its reason. Never a rejection.</summary>
    internal static (SubmissionEvaluationKind Kind, string? ReasonCode) HoldFor(InvoiceSubmissionChannel channel, string holdCode, bool coded) =>
        channel == InvoiceSubmissionChannel.Spa
            ? (coded ? SubmissionEvaluationKind.Accepted : SubmissionEvaluationKind.AcceptedUncoded, holdCode)
            : (SubmissionEvaluationKind.Blocked, SubmissionReasonCodes.Blocked);

    /// <summary>
    /// 082c-2 (brief C14, C15): the cause of a Procedure resolution fault, or null for any other exception (which keeps today's path: it
    /// propagates, and on the final delivery the job marks the submission Failed; never a pass).
    /// </summary>
    internal static string? UnresolvedCauseOf(Exception ex) => ex switch
    {
        // K1 (fix round 1): a chain a failed READ could not complete is an outage, not stored data that is wrong, so it reads as the cause whose
        // text says it may clear (a retry will not help applies to chain_unreadable only).
        CoreRecordChainUnreadableException { Transient: true } => SubmissionReasonCodes.CauseRowsInconsistent,
        CoreRecordChainUnreadableException => SubmissionReasonCodes.CauseChainUnreadable,
        ProcedureResolutionFaultException => SubmissionReasonCodes.CauseRowsInconsistent,
        _ => null,
    };

    /// <summary>
    /// 082c-2 (brief C11, C13): one <see cref="SubmissionReasonCodes.ProcedureGap"/> pipeline finding per gapped Procedure: its name, its first
    /// gapped date and that date's kind. Ordered by date, then name, so the stored copy is stable. Names no id (C16). The kind is checked against
    /// the closed map here, so an unmapped kind fails the evaluation rather than reaching a reader.
    /// </summary>
    internal static IReadOnlyList<SubmissionPipelineFinding> ProcedureGapFindings(BillingEvaluationResult evaluation) =>
        evaluation.Gaps
            .GroupBy(g => g.Gap.ProcedureId)
            .Select(group => group.Select(g => g.Gap).OrderBy(g => g.WorkDate).ThenBy(g => g.Kind).First())
            .OrderBy(g => g.WorkDate).ThenBy(g => g.ProcedureName, StringComparer.Ordinal)
            .Select(g =>
            {
                _ = BillingSystemFindings.GapKindText(g.Kind);
                return new SubmissionPipelineFinding(SubmissionReasonCodes.ProcedureGap, new Dictionary<string, string?>
                {
                    [SubmissionReasonCodes.ProcedureNameValue] = g.ProcedureName,
                    [SubmissionReasonCodes.ProcedureWorkDateValue] = g.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    [SubmissionReasonCodes.ProcedureGapKindValue] = g.Kind.ToString(),
                });
            })
            .ToList();

    // ── 043 → 046 ──

    internal static InvoiceResolutionInput BuildResolutionInput(SubmissionSnapshot snapshot, ExtractedInvoice read)
    {
        var header = read.Header;
        return new InvoiceResolutionInput
        {
            FirmId = snapshot.FirmId,
            CoreRecord = ToResolutionCore(snapshot.CoreRecord),
            Header = new InvoiceResolutionHeader
            {
                FeesTotal = Field(header.FeesTotal),
                ExpensesTotal = Field(header.ExpensesTotal),
                AdjustmentsTotal = Field(header.AdjustmentsTotal),
                TotalDue = Field(header.TotalDue),
                OtherFields = new Dictionary<string, FieldReading>(StringComparer.Ordinal)
                {
                    [BillingFieldNames.FirmInvoiceNumber] = Reading(header.FirmInvoiceNumber),
                    [BillingFieldNames.InvoiceDate] = Reading(header.InvoiceDate),
                    [BillingFieldNames.BillingPeriodStart] = Reading(header.BillingPeriodStart),
                    [BillingFieldNames.BillingPeriodEnd] = Reading(header.BillingPeriodEnd),
                    [BillingFieldNames.Currency] = Reading(header.Currency),
                },
            },
            Lines = read.Lines.Select(line => new InvoiceResolutionLine
            {
                LineType = line.LineType,
                Date = Field(line.Date),
                TimekeeperAsBilled = Field(line.TimekeeperAsBilled),
                Hours = Field(line.Hours),
                Rate = Field(line.Rate),
                Quantity = Field(line.Quantity),
                UnitCost = Field(line.UnitCost),
                Amount = Field(line.Amount),
                OtherFields = new Dictionary<string, FieldReading>(StringComparer.Ordinal)
                {
                    [BillingFieldNames.Narrative] = Reading(line.Narrative),
                    [BillingFieldNames.BilledTaskCode] = Reading(line.TaskCodeAsBilled),
                    [BillingFieldNames.BilledActivityCode] = Reading(line.ActivityCodeAsBilled),
                    [BillingFieldNames.BilledExpenseCode] = Reading(line.ExpenseCodeAsBilled),
                },
            }).ToList(),
            TimekeeperSummaryNames = read.TimekeeperSummary
                .Select(row => row.Name.Value)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList(),
        };
    }

    // ── 043 → 047 ──

    internal static LineCodingRequest BuildCodingRequest(SubmissionSnapshot snapshot, ExtractedInvoice read) => new()
    {
        CoreRecord = new CodingCoreRecord(snapshot.CoreRecord.EntityLogicalName, snapshot.CoreRecord.RecordId),

        // Task 095c-3: with no firm, the firm-code step (FR-22 step 1) is skipped; the record's code set (steps 2 to 5) still applies.
        IncludeFirmCodes = !snapshot.HasNoFirm,
        Lines = read.Lines.Select((line, index) => new InvoiceLineToCode
        {
            LineKey = LineKey(index),
            Date = line.Date.Value,
            LineType = line.LineType,
            Narrative = line.Narrative.Value,
            Hours = line.Hours.Value,
            Amount = line.Amount.Value,
            BilledTaskCode = line.TaskCodeAsBilled.Value,
            BilledActivityCode = line.ActivityCodeAsBilled.Value,
            BilledExpenseCode = line.ExpenseCodeAsBilled.Value,
        }).ToList(),
    };

    /// <summary>
    /// Task 097b: the invoice a Correction replaces, derived here on the server only, from this submission's <c>sprk_resubmissionof</c> and
    /// that submission's <c>sprk_invoice</c> (checked against <see cref="InvoiceSubmissionPersister.InvoiceIdFor"/>); the evaluation's
    /// loader leaves it out of the priors (<see cref="BillingEvaluationRequest.ReplacedInvoiceId"/>). Null for New. A target that does not
    /// resolve gives null (no exclusion: the correction is judged against every prior, fail closed) with a warning. A read fault propagates
    /// (the job retries), as the other reference reads of the pipeline do.
    /// </summary>
    private async Task<Guid?> ReplacedInvoiceAsync(SubmissionSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.ResubmissionOfSubmissionId is not { } earlier)
        {
            return null;
        }

        if (_entities is null)
        {
            _logger.LogWarning("Submission {SubmissionId}: a Correction judged without the replaced invoice's read; no prior is excluded",
                snapshot.SubmissionId);
            return null;
        }

        var target = await snapshot.ReadCorrectedInvoiceAsync(_entities, ct).ConfigureAwait(false);
        if (target.CorrectedInvoiceId is null)
        {
            _logger.LogWarning("Submission {SubmissionId}: the replaced submission {ResubmissionOf} names no invoice it created; no prior is excluded",
                snapshot.SubmissionId, earlier);
        }

        return target.CorrectedInvoiceId;
    }

    // ── 043 + 046 + 047 → 072 ──

    internal static BillingEvaluationRequest BuildEvaluationRequest(
        SubmissionSnapshot snapshot, ExtractedInvoice read, InvoiceResolutionResult resolution, InvoiceCodingResult? coding)
    {
        // Task 095c-3: the rules are never judged without a firm (EvaluateAsync skips steps 4 and 5); a null here is a caller defect.
        if (snapshot.FirmId is not { } firmId)
        {
            throw new InvalidOperationException("A submission with no firm is not evaluated against billing rules.");
        }

        var resolvedByIndex = resolution.Lines.ToDictionary(l => l.LineIndex);
        var codedByKey = coding?.Lines.ToDictionary(l => l.LineKey, StringComparer.Ordinal);
        var header = read.Header;

        var lines = new List<BillingLineFacts>(read.Lines.Count);
        for (var index = 0; index < read.Lines.Count; index++)
        {
            var line = read.Lines[index];
            if (!resolvedByIndex.TryGetValue(index, out var resolved))
            {
                throw new InvalidOperationException($"The resolution returned no result for line {index}.");
            }

            // D-130: coding is absent for an uncoded submission (null codes, no observations, no coding confidence).
            LineCodingResult? coded = null;
            if (codedByKey is not null && !codedByKey.TryGetValue(LineKey(index), out coded))
            {
                throw new InvalidOperationException($"Line coding returned no result for line {index}.");
            }

            lines.Add(new BillingLineFacts
            {
                Index = index,
                LineType = line.LineType,
                Date = line.Date.Value,
                TimekeeperName = line.TimekeeperAsBilled.Value,
                Timekeeper = resolved.Timekeeper,
                ApprovedRate = resolved.Rate,
                Hours = line.Hours.Value,
                BilledRate = line.Rate.Value,
                Amount = line.Amount.Value,
                Narrative = line.Narrative.Value,
                TaskCode = coded?.Task.Code,
                ActivityCode = coded?.Activity.Code,
                ExpenseCode = coded?.Expense.Code,
                BilledTaskCode = line.TaskCodeAsBilled.Value,
                BilledActivityCode = line.ActivityCodeAsBilled.Value,
                BilledExpenseCode = line.ExpenseCodeAsBilled.Value,
                Observations = coded?.Observations ?? [],
                FieldConfidence = LineConfidence(resolved, coded),
            });
        }

        return new BillingEvaluationRequest
        {
            SubmissionId = snapshot.SubmissionId,
            FirmId = firmId,
            CoreRecord = ToResolutionCore(snapshot.CoreRecord),
            TrackingNumber = snapshot.TrackingNumber,
            FirmInvoiceNumber = header.FirmInvoiceNumber.Value,
            InvoiceDate = header.InvoiceDate.Value,
            ReceivedOn = snapshot.ReceivedOn,
            BillingPeriodStart = header.BillingPeriodStart.Value,
            BillingPeriodEnd = header.BillingPeriodEnd.Value,

            // As read: the evaluator normalises it through the one shared BillingCurrency (D-119), never here.
            Currency = header.Currency.Value,

            // 046's combined confidence per header field, under the BillingFieldNames spellings (currency under exactly "Currency").
            HeaderConfidence = resolution.HeaderFields.ToDictionary(f => f.Key, f => f.Value.Combined, StringComparer.Ordinal),
            Lines = lines,
        };
    }

    /// <summary>
    /// A line's confidences as the handlers read them: 046's combined confidence per field; the coding confidence (the model's own) under
    /// TaskCode / ActivityCode / ExpenseCode; each observation kind's own confidence (the highest of that kind) under Observation.*.
    /// </summary>
    private static IReadOnlyDictionary<string, decimal> LineConfidence(ResolvedInvoiceLine resolved, LineCodingResult? coded)
    {
        var confidence = resolved.Fields.ToDictionary(f => f.Key, f => f.Value.Combined, StringComparer.Ordinal);
        if (coded is null)
        {
            // Uncoded: no coding or observation confidence exists, so a rule that reads one finds it unknown (and holds, I-5).
            return confidence;
        }

        confidence[BillingFieldNames.TaskCode] = coded.Confidence;
        confidence[BillingFieldNames.ActivityCode] = coded.Confidence;
        confidence[BillingFieldNames.ExpenseCode] = coded.Confidence;
        foreach (var kind in coded.Observations.GroupBy(o => o.Type))
        {
            confidence[BillingFieldNames.ObservationPrefix + kind.Key] = kind.Max(o => o.Confidence);
        }

        return confidence;
    }

    private static CoreRecordReference ToResolutionCore(InvoiceCoreRecord core) => new(
        core.Kind switch
        {
            InvoiceCoreRecordKind.Matter => CoreRecordKind.Matter,
            InvoiceCoreRecordKind.Project => CoreRecordKind.Project,
            _ => CoreRecordKind.WorkAssignment,
        },
        core.RecordId);

    private static string LineKey(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static ResolutionField<T> Field<T>(ExtractedInvoiceField<T> field) => new(field.Value!, field.Confidence);

    private static FieldReading Reading<T>(ExtractedInvoiceField<T> field) => new(field.Confidence);

    private SubmissionEvaluationOutcome Log(SubmissionEvaluationOutcome outcome)
    {
        _logger.LogInformation(
            "Invoice submission {SubmissionId}: steps 1-5 gave {Kind} ({ReasonCode}); {LineCount} lines, {FindingCount} findings, {DecidingCount} deciding, {PipelineFindingCount} pipeline findings.",
            outcome.SubmissionId,
            outcome.Kind,
            outcome.ReasonCode,
            outcome.Read?.Lines.Count ?? 0,
            outcome.Evaluation?.Findings.Count ?? 0,
            outcome.Decision?.DecidingFindings.Count ?? 0,
            outcome.PipelineFindings.Count);
        return outcome;
    }
}
