using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Request to add an invoice-level Adjustment line (FR-32). The server computes the amount (016); the body never carries one, nor a total.</summary>
/// <param name="AdjustmentId">The client's id for this adjustment: a retry of the same request carries the same id and adds nothing twice.</param>
/// <param name="Method"><c>percent</c> or <c>fixed</c>.</param>
/// <param name="Value">A percent above 0 and up to 100, or a fixed amount above 0 (at most 2 decimals).</param>
/// <param name="RuleId">Optional: the <c>sprk_policyversion</c> the reviewer cites, which must be one of the invoice's rule options (F-8, D-74).</param>
/// <param name="ReviewerSystemUserId">The authenticated caller's <c>systemuserid</c>, resolved by the endpoint from the caller's own token, never from the body.</param>
/// <param name="Note">Task 095f-2b (EB-D-241): an optional note, checked by <see cref="InvoiceCorrectionNote.Check"/>. It goes to the add's Decision Record
/// (<c>Values["note"]</c>) and to <c>sprk_invoice.sprk_decisionnote</c>; with none, the column takes the reason.</param>
public sealed record AddAdjustmentCommand(
    Guid InvoiceId, Guid AdjustmentId, string? Method, decimal? Value, string? Reason, Guid? RuleId, Guid ReviewerSystemUserId, string? Note = null);

/// <summary>Request to remove an Adjustment line. The row is deactivated, never deleted (NFR-09).</summary>
/// <param name="Note">Task 095f-2b (EB-D-241): an optional note, checked by <see cref="InvoiceCorrectionNote.Check"/>. It goes to the removal's Decision
/// Record and to <c>sprk_invoice.sprk_decisionnote</c>; with none, the column is left as it is (a removal has no reason).</param>
public sealed record RemoveAdjustmentCommand(Guid InvoiceId, Guid LineId, Guid ReviewerSystemUserId, string? Note = null);

/// <summary>Outcome class of an adjustment attempt. The endpoint maps each to one HTTP status and one stable code.</summary>
public enum AdjustmentStatus
{
    Applied,
    InvalidRequest,

    /// <summary>The approved total would be below zero (FR-32): 400, nothing written.</summary>
    NegativeTotal,
    InvoiceNotFound,
    AdjustmentNotFound,
    NotInReview,

    /// <summary>The totals could not be derived (016 refused for another reason): 409, nothing written.</summary>
    TotalsRefused,
    IdReused,
    Busy,
    Changed,
    OwnerUnresolved,

    /// <summary>A read, the lock or the write failed and NOTHING is known to have changed (or a repeat of the same request is safe).</summary>
    Unavailable,

    /// <summary>The adjustment row was written (or deactivated) but the totals step did not finish. Repeating the same request finishes it.</summary>
    Incomplete,

    /// <summary>Task 095c-4: the invoice has no firm, so it has no rules to list or cite (409, nothing written). Task 095d-0 (EB-D-197): an add
    /// or a remove of an adjustment - rule-less or rule-cited - is refused with this status too, while the invoice has no firm, is held for one,
    /// or its Set-firm re-run is pending (<see cref="InvoiceFirmGate"/>).</summary>
    FirmMissing,
}

/// <summary>Stable ProblemDetails codes (ADR-019). Callers branch on these, never on a message.</summary>
public static class AdjustmentCodes
{
    public const string InvalidRequest = LineDecisionCodes.InvalidRequest;
    public const string MethodInvalid = "ebilling.adjustment.method_invalid";
    public const string ValueInvalid = "ebilling.adjustment.value_invalid";
    public const string ReasonRequired = "ebilling.adjustment.reason_required";
    public const string ReasonTooLong = "ebilling.adjustment.reason_too_long";
    public const string RuleNotAllowed = "ebilling.adjustment.rule_not_allowed";
    public const string NegativeTotal = "ebilling.adjustment.negative_total";
    public const string IdReused = "ebilling.adjustment.id_reused";
    public const string NotFound = "ebilling.adjustment.not_found";
    public const string Incomplete = "ebilling.adjustment.incomplete";
    public const string NotInReview = LineDecisionCodes.NotInReview;
    public const string TotalsRefused = LineDecisionCodes.TotalsRefused;
    public const string Busy = LineDecisionCodes.Busy;
    public const string LineChanged = LineDecisionCodes.LineChanged;
    public const string OwnerUnresolved = LineDecisionCodes.OwnerUnresolved;
    public const string Unavailable = LineDecisionCodes.Unavailable;
    public const string ReviewerUnresolved = LineDecisionCodes.DeciderUnresolved;

    /// <summary>409 (task 095c-4): see <see cref="AdjustmentStatus.FirmMissing"/>.</summary>
    public const string FirmMissing = "ebilling.adjustment.firm_missing";

    /// <summary>The sentence a reviewer reads with <see cref="FirmMissing"/>.</summary>
    public const string FirmMissingDetail = "This invoice has no firm, so no billing rule can be cited. Add the firm first, or add the adjustment without a rule.";

    /// <summary>095c-5 review K2: the sentence with <see cref="FirmMissing"/> when the invoice shows a firm but is held for one
    /// (<see cref="InvoiceFirmNeed.Held"/>); the code is the same as for no firm, as approve does.</summary>
    public const string FirmHeldDetail = "This invoice is waiting for its firm to be assigned, so no billing rule can be cited. Add the adjustment without a rule, or cite one once the firm is assigned.";

    /// <summary>The <see cref="FirmMissing"/> sentence for <paramref name="need"/>.</summary>
    public static string FirmDetailFor(InvoiceFirmNeed need) => need == InvoiceFirmNeed.Held ? FirmHeldDetail : FirmMissingDetail;

    /// <summary>EB-D-197: the sentence with <see cref="FirmMissing"/> when an adjustment (rule-less or rule-cited) is added or removed on an invoice with no firm.</summary>
    public const string AdjustFirmMissingDetail = "This invoice has no firm. Add the firm before adjusting it; nothing was changed.";

    /// <summary>EB-D-197: as <see cref="AdjustFirmMissingDetail"/>, for an invoice held for its firm.</summary>
    public const string AdjustFirmHeldDetail = "This invoice is waiting for its firm to be assigned. It can be adjusted once the firm is assigned; nothing was changed.";

    /// <summary>EB-D-197: as <see cref="AdjustFirmMissingDetail"/>, while the Set-firm re-run is pending (the same sentence the line decisions give).</summary>
    public const string AdjustFirmRerunPendingDetail = "This invoice's firm was just assigned and its billing rules are being checked again. Try again when the check finishes; nothing was changed.";

    /// <summary>EB-D-197: the <see cref="FirmMissing"/> sentence of an add or a remove for <paramref name="gate"/> (<see cref="InvoiceFirmGateResult.Open"/> has none and gets the no-firm sentence).</summary>
    public static string AdjustFirmDetailFor(InvoiceFirmGateResult gate) => gate switch
    {
        InvoiceFirmGateResult.HeldForFirm => AdjustFirmHeldDetail,
        InvoiceFirmGateResult.RerunPending => AdjustFirmRerunPendingDetail,
        _ => AdjustFirmMissingDetail,
    };
}

/// <summary>An Adjustment line as the reviewer needs it to update the screen without a second read.</summary>
public sealed record AdjustmentDto(
    Guid LineId, Guid AdjustmentId, string Method, decimal Value, decimal Amount, string Reason, Guid? RuleId, DateTimeOffset ReviewedOn);

/// <summary>One rule a reviewer may cite on an adjustment (D-74): a published billing rule version in force on the invoice date. No document link (D-66).</summary>
// ebilling-r1 082c-2 fix round 2 (uac-r2 K4): PolicyCode (a record rule's is its BRR- code) sits next to RuleId on the INTERNAL adjustment route only.
// If any external module or SPA surface maps this DTO, it must go through ProcedureRulesOnDate.ForReaders() (base code + record-rule flag) first.
public sealed record AdjustmentRuleOptionDto(
    Guid RuleId, string PolicyCode, string? Headline, Guid RuleSetId, string? RuleSetName, string Scope, int Version, string? ClauseReference, string? ClauseLocation);

public sealed record AdjustmentResult(
    AdjustmentStatus Status,
    string Code,
    string? Detail = null,
    AdjustmentDto? Adjustment = null,
    Guid? RemovedLineId = null,
    ReviewTotalsDto? Totals = null,
    bool AlreadyApplied = false)
{
    public bool IsApplied => Status == AdjustmentStatus.Applied;
}

public sealed record AdjustmentRulesResult(
    AdjustmentStatus Status, string Code, string? Detail = null, DateOnly? AsOf = null, IReadOnlyList<AdjustmentRuleOptionDto>? Rules = null)
{
    public bool IsApplied => Status == AdjustmentStatus.Applied;
}

/// <summary>
/// The Finance-side owner of invoice-level adjustments (ebilling-r1 task 102; FR-32, FR-07, D-42, D-74, I-1, NFR-09): adds and removes the
/// Adjustment lines of an invoice that is In Review, has 016 recompute the totals, and lists the rules a reviewer may cite.
/// </summary>
/// <remarks>
/// <para><b>One owner per invariant.</b> The amount of an adjustment and the invoice totals are 016's (<see cref="InvoiceTotalsCalculator"/>,
/// <see cref="IInvoiceTotalsService"/>); this service never computes one. It only DRY-RUNS the calculator before the first write, so a change
/// that would make the approved total negative (FR-32, <c>ebilling.adjustment.negative_total</c>) writes nothing, then asks 016 to store the
/// totals after the write. The request carries method, value, reason and an optional rule, never an amount or a total.</para>
/// <para><b>Serialisation.</b> The same per-invoice lock as a line decision (<see cref="InvoiceLineDecisionService.LockKeyPrefix"/>): an
/// adjustment and a line decision on one invoice cannot interleave, so each dry run and each recalculation sees the other's result. The loser
/// gets 409 <c>ebilling.invoice.busy</c>.</para>
/// <para><b>Idempotence.</b> The client's adjustment id is hashed with the invoice id into the row id, so a retry finds its own row. A repeat
/// with the same content finishes the totals step and answers <c>alreadyApplied</c>; the same id with other content, or after the row was
/// removed, is 409 <c>ebilling.adjustment.id_reused</c>. A failure after the row was written answers 503 <c>ebilling.adjustment.incomplete</c>:
/// repeat the same request. Remove is idempotent too (an already-removed adjustment re-runs the recalculation and answers <c>alreadyApplied</c>).</para>
/// <para><b>Removal.</b> Deactivates the row (statecode Inactive), conditionally on the row version read, and stamps the remover on
/// <c>sprk_reviewedby / on</c>; never deletes (NFR-09). Inactive Adjustment lines are excluded from the totals and the review read model by
/// <see cref="InvoiceTotalsService.ExcludeInactiveAdjustments"/>.</para>
/// <para><b>Rule options (F-8, D-74).</b> The published billing rules of the invoice's applicable rule sets (the client default plus the
/// invoice firm's, exactly the evaluator's own load, <see cref="BillingFactLoader.LoadRulesAsync"/>) in force on the invoice date: one version
/// per policy (<see cref="Signals.Billing.InvoiceComplianceEvaluator"/>'s selection), a billing type the catalogue knows and that is not a system
/// type (an unknown type is refused fail-closed, FR-24). The invoice date is <c>sprk_invoicedate</c>, else the UTC day the invoice row was created.
/// A cited rule must be one of these options (400 otherwise).</para>
/// <para><b>Decision Records (task 095f-2b, EB-D-241; design 3 option A).</b> Every add files ONE <c>add-adjustment</c> record and every
/// removal ONE <c>remove-adjustment</c> record, through <see cref="BillingDecisionRecorder"/>, filed under the invoice, AFTER the line write and the
/// totals land. The decision id of an add is <see cref="AddDecisionIdFor"/> (the invoice and the client's adjustment id), of a removal <see cref="RemoveDecisionIdFor"/> (the invoice and the line): namespaced hashes like every other billing decision id, never the raw client id. Values hold
/// the declared codes only (<c>method</c>, <c>value</c>, <c>rule</c> when cited, <c>adjustment</c>, and <c>note</c> only when a note is given, never
/// with a null value); the add's reason is the record's reason. The note (or, with none, an add's reason; a removal with none leaves it) goes to
/// <c>sprk_invoice.sprk_decisionnote</c> in the totals' own conditional update (<see cref="IInvoiceTotalsService.RecalculateAsync(Guid, string?, CancellationToken)"/>).
/// A record step that fails answers 503 <c>ebilling.adjustment.incomplete</c>; repeating the same request finishes it with one record.</para>
/// <para><b>Repeats and the record.</b> A repeat's row must be the same adjustment by the same reviewer (else 409 <c>id_reused</c>, nothing
/// written: the record names its decider, so another reviewer can never finish it as theirs). The stored record is looked up first: when it
/// exists the totals run WITHOUT the note column (the column keeps what the first attempt wrote), and the writer's hash then decides. The writer's
/// "same id, other content" refusal (an edited or dropped note) is answered 409 <c>id_reused</c>, never a 503 a repeat cannot complete (ISS-029).
/// When no record exists yet (a strand after the line or the totals), the repeat's own note goes to the column and the record. A first add whose
/// adjustment id already has a record (another decision used it) is 409 <c>id_reused</c> before anything is written.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="InvoiceLineDecisionService"/> (decides Fee / Expense
/// lines, refuses Adjustment lines), <see cref="InvoiceTotalsService"/> (totals only), <see cref="BillingFactLoader"/> (reads rules for the
/// evaluator). Extension: none can create or deactivate an Adjustment line without becoming a second owner of the line write; this service
/// composes the three and adds nothing to 016's formula. Cost of doing nothing: a reviewer cannot apply a volume discount or fixed write-down
/// (FR-32, success criterion 4).</para>
/// </remarks>
public sealed class InvoiceAdjustmentService
{
    public const int ReasonMaxLength = InvoiceLineDecisionService.ReasonMaxLength;

    /// <summary>The largest value <c>sprk_adjustmentvalue</c> accepts (the column's maximum).</summary>
    public const decimal MaxValue = 1_000_000_000m;

    public const string ReviewerRole = InvoiceLineDecisionService.DeciderRole;

    /// <summary>Task 095f-2b: the billing code of an add's Decision Record.</summary>
    public const string AddActionCode = "add-adjustment";

    /// <summary>Task 095f-2b: the billing code of a removal's Decision Record.</summary>
    public const string RemoveActionCode = "remove-adjustment";

    internal const string OutcomeAdded = "Added";
    internal const string OutcomeRemoved = "Removed";
    private const string TotalsStep = "totals";
    private const string RecordStep = "record";
    private const string UndoStep = "undo";

    internal const string SystemUserEntity = InvoiceLineDecisionService.SystemUserEntity;
    internal const string PolicyVersionEntity = "sprk_policyversion";
    internal const string RuleSetEntity = BillingFactLoader.RuleSetEntity;

    internal const string LineVendorOrg = InvoiceSubmissionPersister.InvoiceVendorOrg;
    internal const string LineRule = "sprk_adjustmentrule";
    internal const string InvoiceDateColumn = InvoiceReviewService.InvInvoiceDate;
    internal const string StatusReasonColumn = "statuscode";
    internal const int StatusReasonInactive = 2;
    internal const int StatusReasonActive = 1;
    internal const int StateInactive = 1;

    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    private static readonly string[] InvoiceColumns =
    [
        "statuscode", InvoiceTotalsService.InvoiceBilledTotal, InvoiceDateColumn, "createdon", InvoiceSubmissionPersister.InvoiceVendorOrg,
        "sprk_matter", "sprk_project", "sprk_regardingworkassignment",
        CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
        "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
    ];

    private static readonly string[] RowColumns =
    [
        InvoiceTotalsService.LineInvoiceLookup, InvoiceTotalsService.LineCostType, InvoiceTotalsService.LineState,
        InvoiceTotalsService.LineAmount, InvoiceTotalsService.LineAdjustmentMethod, InvoiceTotalsService.LineAdjustmentValue,
        InvoiceReviewReadService.LineReason, LineRule, InvoiceReviewReadService.LineReviewedOn, InvoiceTotalsService.VersionNumber,
        InvoiceReviewReadService.LineReviewedBy, // 095f-2b: a repeat is the same reviewer's (the record names its decider)
    ];

    /// <summary>
    /// The seam the unit tests use: the rules in force on <paramref name="date"/> for an invoice of <paramref name="firmId"/> on <paramref name="core"/>,
    /// one version per policy, legacy and Procedure alike (<see cref="BillingFactLoader.LoadRulesOnAsync"/>, ebilling-r1 082c).
    /// <paramref name="latestWorkDate"/> is the latest dated line's work date (082c K1, rule (c)), or <see langword="null"/> when no line is dated.
    /// </summary>
    internal delegate Task<IReadOnlyList<BillingRuleCandidate>> LoadRules(
        Guid firmId, CoreRecordReference? core, DateOnly date, DateOnly? latestWorkDate, CancellationToken ct);

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceTotalsService _totals;
    private readonly IIdempotencyService _locks;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly LoadRules _loadRules;
    private readonly BillingDecisionRecorder _recorder;
    private readonly ILogger<InvoiceAdjustmentService> _logger;
    private readonly TimeProvider _time;
    private readonly JobSubmissionService? _jobs;

    public InvoiceAdjustmentService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        BillingFactLoader rules,
        BillingDecisionRecorder recorder,
        ILogger<InvoiceAdjustmentService> logger,
        JobSubmissionService? jobs = null)
        : this(entities, totals, locks, ownership, (rules ?? throw new ArgumentNullException(nameof(rules))).LoadRulesOnAsync, recorder, logger, time: null, jobs)
    {
    }

    internal InvoiceAdjustmentService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        LoadRules loadRules,
        BillingDecisionRecorder recorder,
        ILogger<InvoiceAdjustmentService> logger,
        TimeProvider? time = null,
        JobSubmissionService? jobs = null)
    {
        _jobs = jobs; // Task 152: null in a test seam; the DI path always supplies it (JobSubmissionService is a singleton).
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _loadRules = loadRules ?? throw new ArgumentNullException(nameof(loadRules));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The row id of an adjustment: the client's id hashed with the invoice, so another invoice's id can never collide with it.</summary>
    internal static Guid LineIdFor(Guid invoiceId, Guid adjustmentId) =>
        InvoiceLineDecisionService.IdOf($"ebilling-adjustment-{invoiceId:D}-{adjustmentId:D}");

    /// <summary>Task 095f-2b (review round 1): the decision id of an add's Decision Record: the client's adjustment id hashed with the invoice, in
    /// the same namespaced form as every other billing decision id (as Add line's <c>DecisionIdFor</c>). The writer keys records by subject and decision
    /// id, so the raw client id could coincide with another decision's id on the invoice.</summary>
    internal static Guid AddDecisionIdFor(Guid invoiceId, Guid adjustmentId) =>
        InvoiceLineDecisionService.IdOf($"ebilling-add-adjustment-{invoiceId:D}-{adjustmentId:D}");

    /// <summary>Task 095f-2b (design 3; review round 1): the decision id of a removal's Decision Record, namespaced like <see cref="AddDecisionIdFor"/>.
    /// A removed adjustment cannot be re-added under its id (409 <c>id_reused</c>), so the id names one removal.</summary>
    internal static Guid RemoveDecisionIdFor(Guid invoiceId, Guid lineId) =>
        InvoiceLineDecisionService.IdOf($"ebilling-remove-adjustment-{invoiceId:D}-{lineId:D}");

    // ── The rule options (F-8, D-74) ──

    /// <summary>The rules a reviewer may cite on this invoice. Read-only; allowed in any invoice status.</summary>
    public async Task<AdjustmentRulesResult> ListRulesAsync(Guid invoiceId, CancellationToken ct)
    {
        if (invoiceId == Guid.Empty)
        {
            return new AdjustmentRulesResult(AdjustmentStatus.InvalidRequest, AdjustmentCodes.InvalidRequest, "An invoice id is required.");
        }

        try
        {
            var invoice = await ReadInvoiceAsync(invoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new AdjustmentRulesResult(AdjustmentStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            // Task 095c-4: the options are the firm's rules; with no firm there are none to offer (never the empty firm's).
            // Task 095c-5 review K2: nor for a held invoice, whatever its firm column says now (read app-only; a fault is Unavailable below).
            if (await InvoiceFirmHold.CheckAsync(_entities, invoice, ct).ConfigureAwait(false) is not InvoiceFirmNeed.None and var need)
            {
                return new AdjustmentRulesResult(AdjustmentStatus.FirmMissing, AdjustmentCodes.FirmMissing, AdjustmentCodes.FirmDetailFor(need));
            }

            var (asOf, rules) = await ReadOptionsAsync(invoice, ct).ConfigureAwait(false);
            return new AdjustmentRulesResult(AdjustmentStatus.Applied, "ok", AsOf: asOf, Rules: rules);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Adjustment rules of invoice {InvoiceId}: a read failed ({ExceptionType}).", invoiceId, ex.GetType().Name);
            return new AdjustmentRulesResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The rules could not be read. Try again.");
        }
    }

    private async Task<(DateOnly? AsOf, IReadOnlyList<AdjustmentRuleOptionDto> Rules)> ReadOptionsAsync(Entity invoice, CancellationToken ct)
    {
        var asOf = InvoiceDateOf(invoice);
        if (asOf is not { } date)
        {
            return (null, []);
        }

        // Task 095c-4: both callers refuse a no-firm invoice first; the empty firm's rules are never loaded.
        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id is not { } firm || firm == Guid.Empty)
        {
            throw new InvalidOperationException("The invoice names no firm; its rule options cannot be read.");
        }

        // ebilling-r1 082c: the Procedures that apply are those of the invoice's core record, so it must name exactly one (013); none or several
        // is a read failure (Unavailable), never a guess at one.
        if (InvoiceLineDecisionService.SingleCoreOf(invoice) is not { } single)
        {
            throw new InvalidOperationException("The invoice names no single core record; its rule options cannot be read.");
        }

        var core = new CoreRecordReference(
            single.Entity switch
            {
                "sprk_matter" => CoreRecordKind.Matter,
                "sprk_project" => CoreRecordKind.Project,
                "sprk_workassignment" => CoreRecordKind.WorkAssignment,
                _ => throw new InvalidOperationException("The invoice's core record is of an unknown table."),
            },
            single.Id);

        // One version per policy in force on the invoice date: legacy by the evaluator's own selection, Procedure by the resolver (082c). No
        // precedence: a reviewer may cite any rule in force. A type the catalogue does not know, a non-billing type and a system type are never
        // offered: fail closed (FR-24).
        // 082c K1: a Procedure with no version on the invoice date is judged (and so citable) by its version on the latest dated line's work date,
        // as the evaluator judges the invoice subject; the line dates are the stored columns, which already hold any correction.
        var lineRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(invoice.Id), ct).ConfigureAwait(false);
        var workDates = lineRows
            .Where(r => r.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value != (int)InvoiceLineCostType.Adjustment)
            .Select(r => r.GetAttributeValue<DateTime?>(InvoiceSubmissionPersister.LineEventDate))
            .OfType<DateTime>()
            .Select(DateOnly.FromDateTime)
            .ToList();
        DateOnly? latestWorkDate = workDates.Count > 0 ? workDates.Max() : null;

        var chosen = (await _loadRules(firm, core, date, latestWorkDate, ct).ConfigureAwait(false))
            .Where(IsCitableType)
            .ToList();

        var names = await ReadRuleSetNamesAsync(chosen.Select(c => c.RuleSetId).Distinct().ToList(), ct).ConfigureAwait(false);
        var options = chosen
            .Select(c => new AdjustmentRuleOptionDto(
                c.PolicyVersionId, c.PolicyCode, c.ShortHeadline, c.RuleSetId, names.GetValueOrDefault(c.RuleSetId),
                c.Source == BillingRuleSource.Procedure ? "Procedure" : c.Scope?.ToString() ?? "Procedure", c.VersionNumber, c.ClauseReference, c.ClauseLocation))
            .OrderBy(o => o.RuleSetName, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.PolicyCode, StringComparer.Ordinal).ThenBy(o => o.RuleId)
            .ToList();
        return (date, options);
    }

    private static bool IsCitableType(BillingRuleCandidate candidate) =>
        RuleBodySchemaValidator.TryParseRuleType(candidate.RuleType, out var type)
        && RuleDomains.IsBilling(type)
        && !BillingRuleCatalogue.IsSystemType(type);

    /// <summary>The date the rules are judged on: the invoice date, else the UTC day the invoice row was created (the evaluator's received-on fallback).</summary>
    internal static DateOnly? InvoiceDateOf(Entity invoice)
    {
        if (invoice.GetAttributeValue<DateTime?>(InvoiceDateColumn) is { } invoiceDate)
        {
            return DateOnly.FromDateTime(invoiceDate);
        }

        return invoice.GetAttributeValue<DateTime?>("createdon") is { } created
            ? DateOnly.FromDateTime(created.Kind == DateTimeKind.Local ? created.ToUniversalTime() : created)
            : null;
    }

    private async Task<Dictionary<Guid, string?>> ReadRuleSetNamesAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>(ids.Count);
        foreach (var chunk in ids.Chunk(BillingFactLoader.MaxInValues))
        {
            var query = new QueryExpression(RuleSetEntity)
            {
                ColumnSet = new ColumnSet("sprk_name"),
                Criteria = new FilterExpression(LogicalOperator.And),
                NoLock = true,
            };
            query.Criteria.AddCondition($"{RuleSetEntity}id", ConditionOperator.In, chunk.Cast<object>().ToArray());
            foreach (var row in await InvoiceReviewReadService.ReadPagedAsync(_entities, query, ct).ConfigureAwait(false))
            {
                names[row.Id] = row.GetAttributeValue<string>("sprk_name");
            }
        }

        return names;
    }

    // ── Add ──

    public async Task<AdjustmentResult> AddAsync(AddAdjustmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ShapeError(command, out var method, out var value, out var reason, out var note) is { } invalid)
        {
            return invalid;
        }

        return await WithLockAsync(command.InvoiceId, () => ApplyAddAsync(command, method, value, reason, note, ct), ct).ConfigureAwait(false);
    }

    /// <summary>The request-shape rules that need no data (400). Task 095f-2b: the note is checked last (after the reason), by
    /// <see cref="InvoiceCorrectionNote.Check"/>; <paramref name="note"/> is the checked, trimmed note, or null for none.</summary>
    internal static AdjustmentResult? ShapeError(
        AddAdjustmentCommand command, out InvoiceAdjustmentMethod method, out decimal value, out string reason, out string? note)
    {
        static AdjustmentResult Bad(string code, string detail) => new(AdjustmentStatus.InvalidRequest, code, detail);

        method = default;
        value = 0m;
        reason = string.Empty;
        note = null;

        if (command.InvoiceId == Guid.Empty || command.AdjustmentId == Guid.Empty || command.ReviewerSystemUserId == Guid.Empty)
        {
            return Bad(AdjustmentCodes.InvalidRequest, "An invoice id and an adjustment id are required.");
        }

        if (command.RuleId == Guid.Empty)
        {
            return Bad(AdjustmentCodes.InvalidRequest, "The rule, when given, must be a rule id.");
        }

        switch (command.Method?.Trim().ToLowerInvariant())
        {
            case "percent":
                method = InvoiceAdjustmentMethod.Percent;
                break;
            case "fixed":
                method = InvoiceAdjustmentMethod.Fixed;
                break;
            default:
                return Bad(AdjustmentCodes.MethodInvalid, "The method is percent or fixed.");
        }

        if (command.Value is not { } given || given <= 0m || decimal.Round(given, 2, MidpointRounding.AwayFromZero) != given || given > MaxValue
            || (method == InvoiceAdjustmentMethod.Percent && given > 100m))
        {
            return Bad(
                AdjustmentCodes.ValueInvalid,
                method == InvoiceAdjustmentMethod.Percent
                    ? "A percent adjustment is above 0 and at most 100, with at most 2 decimals."
                    : "A fixed adjustment is an amount above 0, with at most 2 decimals.");
        }

        value = given;

        var trimmed = command.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Bad(AdjustmentCodes.ReasonRequired, "A reason is required for an adjustment.");
        }

        if (trimmed.Length > ReasonMaxLength)
        {
            return Bad(AdjustmentCodes.ReasonTooLong, $"The reason is at most {ReasonMaxLength} characters.");
        }

        reason = trimmed;
        return NoteError(command.Note, out note);
    }

    /// <summary>Task 095f-2b (design 1.1): the note rule of every decision route, before any read. A refusal is a coded 400 whose detail never
    /// echoes the note.</summary>
    private static AdjustmentResult? NoteError(string? raw, out string? note)
    {
        var checkedNote = InvoiceCorrectionNote.Check(raw);
        note = checkedNote.Value;
        return InvoiceCorrectionNote.DecisionRefusalOf(checkedNote.Refusal) is { } refusal
            ? new AdjustmentResult(AdjustmentStatus.InvalidRequest, refusal.Code, refusal.Detail)
            : null;
    }

    private async Task<AdjustmentResult> ApplyAddAsync(
        AddAdjustmentCommand command, InvoiceAdjustmentMethod method, decimal value, string reason, string? note, CancellationToken ct)
    {
        var lineId = LineIdFor(command.InvoiceId, command.AdjustmentId);
        Entity? invoice;
        List<Entity> lineRows;
        Entity? existing;
        try
        {
            invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new AdjustmentResult(AdjustmentStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
            {
                return new AdjustmentResult(AdjustmentStatus.NotInReview, AdjustmentCodes.NotInReview, "Adjustments can be changed only while the invoice is In Review.");
            }

            // Task 095d-0 (EB-D-197, owner): a firm comes first for an adjustment too - rule-less or rule-cited, exactly as for a line decision.
            // No firm, held for one, or the Set-firm re-run pending is 409 (the route's 403 came first), before any read of the lines and every
            // write. It is read once, app-only (InvoiceFirmGate; a fault is Unavailable below), and it also means the firm copied onto the line is
            // always a CONFIRMED one.
            if (await InvoiceFirmGate.CheckAsync(_entities, invoice, ct).ConfigureAwait(false) is not InvoiceFirmGateResult.Open and var gate)
            {
                return new AdjustmentResult(AdjustmentStatus.FirmMissing, AdjustmentCodes.FirmMissing, AdjustmentCodes.AdjustFirmDetailFor(gate));
            }

            lineRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(command.InvoiceId), ct).ConfigureAwait(false);
            existing = await ReadRowAsync(lineId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        // ── A repeat of the same request: finish the totals and the record steps, add nothing. ──
        if (existing is not null)
        {
            // 095f-2b: the same reviewer too, since the record names its decider (a repeat never makes another reviewer the decider).
            if (!IsSameAdjustment(existing, command.InvoiceId, method, value, reason, command.RuleId) || !IsReviewedBy(existing, command.ReviewerSystemUserId))
            {
                return IdReused("This adjustment id was used for a different or a removed adjustment, or by another reviewer. Use a new adjustment id.");
            }

            // 095f-2b: once the record exists the first attempt's note is on the column already; the totals then run without it, and the
            // writer's hash decides whether this repeat is the recorded decision.
            bool recorded;
            try
            {
                recorded = await IsRecordedAsync(AddActionCode, command.InvoiceId, AddDecisionIdFor(command.InvoiceId, command.AdjustmentId), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return LookupFailed(command.InvoiceId, DetailOf(ex));
            }

            return await FinishAddAsync(
                    command, lineId, method, value, reason, note, existing.GetAttributeValue<DateTime?>(InvoiceReviewReadService.LineReviewedOn), invoice,
                    alreadyApplied: true, writeColumn: !recorded, ct)
                .ConfigureAwait(false);
        }

        // ── 095f-2b: a first add whose id already has a Decision Record (another decision used it) would file a second, contradictory record
        // that no repeat could finish: refused before anything is written. ──
        try
        {
            if (await IsRecordedAsync(AddActionCode, command.InvoiceId, AddDecisionIdFor(command.InvoiceId, command.AdjustmentId), ct).ConfigureAwait(false))
            {
                return IdReused("This adjustment id is already recorded for another decision on this invoice. Use a new adjustment id.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        // ── F-8 / D-74: a cited rule must be one of this invoice's options (the firm gate above already proved the invoice has a confirmed firm). ──
        if (command.RuleId is { } ruleId)
        {
            IReadOnlyList<AdjustmentRuleOptionDto> options;
            try
            {
                options = (await ReadOptionsAsync(invoice, ct).ConfigureAwait(false)).Rules;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return ReadFailed(command.InvoiceId, ex);
            }

            if (options.All(o => o.RuleId != ruleId))
            {
                return new AdjustmentResult(
                    AdjustmentStatus.InvalidRequest, AdjustmentCodes.RuleNotAllowed,
                    "The rule is not one of the rules that apply to this invoice. Choose one from the list of rules.");
            }
        }

        // ── Dry-run the totals (FR-32): a refusal changes nothing. ──
        var inputs = lineRows.Select(ToInput).ToList();
        inputs.Add(new InvoiceLineInput(lineId, InvoiceLineCostType.Adjustment, 0m, null, null, method, value));
        var dryRun = InvoiceTotalsCalculator.Calculate(invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value, inputs);
        if (dryRun.IsRefused)
        {
            return Refused(command.InvoiceId, dryRun);
        }

        // ── 095f-2b: the Decision Record's no-I/O refusals, before any write. ──
        var reviewedOn = _time.GetUtcNow();
        if (Preflight(AddDecision(command, lineId, method, value, reason, note, dryRun.AdjustmentLineAmounts[lineId], dryRun.ApprovedTotal, reviewedOn), command.InvoiceId) is { } invalidRecord)
        {
            return invalidRecord;
        }

        // ── The row: the invoice's core record and resolver fields (013), its owner (uac-r2), the firm. ──
        var core = EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice);
        if (core is null)
        {
            return new AdjustmentResult(AdjustmentStatus.OwnerUnresolved, AdjustmentCodes.OwnerUnresolved, "The invoice names no single core record.");
        }

        var nextSequence = lineRows.Select(r => r.GetAttributeValue<int?>(InvoiceSubmissionPersister.LineSequence) ?? 0).DefaultIfEmpty(0).Max() + 1;
        var row = BuildRow(command, lineId, method, value, reason, dryRun.AdjustmentLineAmounts[lineId], nextSequence, reviewedOn.UtcDateTime, invoice, core.Value);

        // UAC 194b R3b-2: the line's question is built from the row itself (RecordOwnershipContext.ForChild: its invoice, its core record and
        // the other lookups it holds, in sprk_billingevent's primary order), the question the secure-child reconciler asks the written row.
        RecordOwnerResolution owner;
        try
        {
            owner = await _ownership.ResolveOwnerAsync(RecordOwnershipContext.ForChild(row), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        if (!owner.IsOwned)
        {
            _logger.LogWarning("Adjustment on invoice {InvoiceId}: no owner for the new line ({Code}); nothing changed.", command.InvoiceId, owner.RefusalCode);
            return new AdjustmentResult(AdjustmentStatus.OwnerUnresolved, AdjustmentCodes.OwnerUnresolved, "The adjustment line has no owner; nothing was changed.");
        }

        owner.ApplyTo(row);
        RecordCreatorPerson.Stamp(row, command.ReviewerSystemUserId);
        if (InvoiceFamilyCoreRecordGuard.Check(row) is { IsRefused: true } refused)
        {
            // A builder defect, never sent (013's guard decides every invoice-family row as built).
            throw new InvalidOperationException($"The adjustment line names no single core record ({refused.RefusalCode}).");
        }

        try
        {
            // From the first write on the request's cancellation no longer applies: a cancelled request must not leave a committed line without its
            // totals (D-130), so the write and the totals step that follow run to the end.
            await _entities.ExecuteTransactionAsync([new CreateRequest { Target = row }], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A timeout can land after the commit (outcome unknown): a repeat of the same request is safe.
            _logger.LogError("Adjustment on invoice {InvoiceId}: the line create failed ({ExceptionType}); the outcome may be unknown.", command.InvoiceId, ex.GetType().Name);
            return new AdjustmentResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The adjustment could not be saved. Repeat the same request to be sure.");
        }

        return await FinishAddAsync(
                command, lineId, method, value, reason, note, reviewedOn.UtcDateTime, invoice, alreadyApplied: false, writeColumn: true, CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Step 2 of an add: 016 stores the totals. A refusal here (the dry run passed, so another writer changed the invoice) deactivates the new row.
    /// Step 3 (095f-2b): the <c>add-adjustment</c> Decision Record, after the line and the totals. <paramref name="writeColumn"/>: the totals' update
    /// carries the note (or, with none, the reason) to <c>sprk_decisionnote</c>; false on a repeat whose record already exists.
    /// </summary>
    private async Task<AdjustmentResult> FinishAddAsync(
        AddAdjustmentCommand command, Guid lineId, InvoiceAdjustmentMethod method, decimal value, string reason, string? note, DateTime? reviewedOnUtc,
        Entity invoice, bool alreadyApplied, bool writeColumn, CancellationToken ct)
    {
        InvoiceTotalsCalculation calculation;
        try
        {
            calculation = await _totals.RecalculateAsync(command.InvoiceId, writeColumn ? note ?? reason : null, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(command.InvoiceId, TotalsStep, ex.GetType().Name);
        }

        if (calculation.IsRefused && alreadyApplied && !writeColumn)
        {
            // A repeat whose add-adjustment record exists: the add finished (the record follows the totals), so it is not this request's to undo;
            // the refusal is reported, nothing is written.
            return Refused(command.InvoiceId, calculation);
        }

        if (calculation.IsRefused)
        {
            // Undo the line the refused calculation already counts (016 notes section 3): deactivate it so nothing stays half applied. ISS-030
            // (F1): this also covers a repeat that finds the row active with no record, i.e. an add whose earlier undo failed (a record is
            // written only after the totals succeed, so no record means the add never finished).
            if (alreadyApplied)
            {
                _logger.LogWarning(
                    "Adjustment on invoice {InvoiceId}: an earlier add of line {LineId} with no Decision Record was undone on a repeat (the totals are still refused); the stamp is left as it is.",
                    command.InvoiceId, lineId);
            }

            try
            {
                await _entities.UpdateAsync(
                    InvoiceReviewReadService.LineEntity, lineId, DeactivationFields(command.ReviewerSystemUserId, _time.GetUtcNow().UtcDateTime), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Incomplete(command.InvoiceId, UndoStep, ex.GetType().Name);
            }

            return Refused(command.InvoiceId, calculation);
        }

        // Task 152 (FR-43): the adjustment line and the invoice's approved total changed, so the index document is rebuilt. The change is the
        // client's adjustment id: a repeat of the request reuses it (one re-index). Best effort; never fails the add.
        await InvoiceIndexingJobHandler.TryEnqueueAsync(
            _jobs, _logger, command.InvoiceId, $"a{command.AdjustmentId.ToString("N", CultureInfo.InvariantCulture)}").ConfigureAwait(false);

        var on = reviewedOnUtc is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : _time.GetUtcNow();

        // ── Step 3 (095f-2b): ONE add-adjustment record, idempotent on the adjustment id. ──
        var decision = AddDecision(command, lineId, method, value, reason, note, calculation.AdjustmentLineAmounts.GetValueOrDefault(lineId), calculation.ApprovedTotal, on);
        if (await RecordAsync(decision, command.InvoiceId, removal: false, ct).ConfigureAwait(false) is { } unrecorded)
        {
            return unrecorded;
        }

        return new AdjustmentResult(
            AdjustmentStatus.Applied, "ok",
            Adjustment: new AdjustmentDto(
                lineId, command.AdjustmentId, method.ToString(), value, calculation.AdjustmentLineAmounts.GetValueOrDefault(lineId), reason, command.RuleId, on),
            Totals: TotalsOf(invoice, calculation),
            AlreadyApplied: alreadyApplied);
    }

    private static Entity BuildRow(
        AddAdjustmentCommand command, Guid lineId, InvoiceAdjustmentMethod method, decimal value, string reason, decimal amount, int sequence,
        DateTime reviewedOnUtc, Entity invoice, (string Entity, Guid Id) core)
    {
        var row = new Entity(InvoiceReviewReadService.LineEntity, lineId)
        {
            [InvoiceFamilyCoreRecordGuard.LineInvoiceColumn] = new EntityReference(InvoiceReviewService.InvoiceEntity, command.InvoiceId),
            [InvoiceSubmissionPersister.LineName] = $"Adjustment - {method} {value.ToString("0.##", CultureInfo.InvariantCulture)}",
            [InvoiceSubmissionPersister.LineSequence] = sequence,
            [InvoiceTotalsService.LineCostType] = new OptionSetValue((int)InvoiceLineCostType.Adjustment),
            // The server's number (016), kept current by every recalculation; never the client's.
            [InvoiceTotalsService.LineAmount] = new Money(amount),
            [InvoiceTotalsService.LineAdjustmentMethod] = new OptionSetValue((int)method),
            [InvoiceTotalsService.LineAdjustmentValue] = value,
            [InvoiceReviewReadService.LineReason] = reason,
            // An applied adjustment is decided: it never reads as Pending (no undecided-line check may count it) or as Unknown.
            [InvoiceTotalsService.LineStatus] = new OptionSetValue((int)InvoiceLineStatus.Accepted),
            [InvoiceReviewReadService.LineReviewedBy] = new EntityReference(SystemUserEntity, command.ReviewerSystemUserId),
            [InvoiceReviewReadService.LineReviewedOn] = reviewedOnUtc,
        };

        if (command.RuleId is { } ruleId)
        {
            row[LineRule] = new EntityReference(PolicyVersionEntity, ruleId);
        }

        // 095c-5 pass-2 review, EB-D-197: only a CONFIRMED firm is copied onto the line. The firm gate has proven the invoice is not held for one
        // (a firm written to the column directly while held is refused above), so the firm on the invoice is the confirmed one.
        if (invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg) is { } firm && firm.Id != Guid.Empty)
        {
            row[LineVendorOrg] = new EntityReference("sprk_organization", firm.Id);
        }

        // 013: exactly one core-record lookup, plus the resolver fields, copied from the invoice (a line shares its invoice's core record).
        row[InvoiceFamilyCoreRecordGuard.CoreRecordLookups[InvoiceReviewReadService.LineEntity].Single(l => l.CoreEntity == core.Entity).Column] = new EntityReference(core.Entity, core.Id);
        foreach (var column in new[]
        {
            CoreAncestorResolver.RegardingRecordTypeColumn, CoreAncestorResolver.RegardingRecordIdColumn,
            "sprk_regardingrecordname", "sprk_regardingrecordurl", "sprk_regardingrecordnumber",
        })
        {
            if (invoice.Contains(column) && invoice[column] is { } carried)
            {
                row[column] = carried;
            }
        }

        return row;
    }

    private static bool IsSameAdjustment(Entity existing, Guid invoiceId, InvoiceAdjustmentMethod method, decimal value, string reason, Guid? ruleId) =>
        existing.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup)?.Id == invoiceId
        && existing.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value == (int)InvoiceLineCostType.Adjustment
        && (existing.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineState)?.Value ?? InvoiceTotalsService.StateActive) == InvoiceTotalsService.StateActive
        && existing.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineAdjustmentMethod)?.Value == (int)method
        && existing.GetAttributeValue<decimal?>(InvoiceTotalsService.LineAdjustmentValue) == value
        && string.Equals(existing.GetAttributeValue<string>(InvoiceReviewReadService.LineReason)?.Trim(), reason, StringComparison.Ordinal)
        && existing.GetAttributeValue<EntityReference>(LineRule)?.Id == ruleId;

    /// <summary>095f-2b: the row was added (or, once removed, removed) by <paramref name="reviewer"/>: <c>sprk_reviewedby</c>, which the add and the
    /// removal stamp. ISS-030: a refused removal's undo puts the prior stamp back (the adder's), and an undo on a repeat leaves the remover's; nothing
    /// else writes it on an Adjustment line.</summary>
    private static bool IsReviewedBy(Entity row, Guid reviewer) =>
        row.GetAttributeValue<EntityReference>(InvoiceReviewReadService.LineReviewedBy)?.Id == reviewer;

    /// <summary>095f-2b: the add's Decision Record. Values are the declared codes; <c>rule</c> only when cited and <c>note</c> only when given,
    /// never with a null value (the hash covers every key, so a null key would make a no-note add hash differently).</summary>
    private static BillingDecision AddDecision(
        AddAdjustmentCommand command, Guid lineId, InvoiceAdjustmentMethod method, decimal value, string reason, string? note, decimal amount,
        decimal approvedTotal, DateTimeOffset decidedOn)
    {
        var values = new Dictionary<string, object?>
        {
            ["method"] = method == InvoiceAdjustmentMethod.Percent ? "percent" : "fixed",
            ["value"] = value,
        };
        if (command.RuleId is { } rule)
        {
            values["rule"] = rule;
        }

        if (note is not null)
        {
            values["note"] = note;
        }

        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = command.InvoiceId,
            ["adjustmentLineId"] = lineId,
            ["amount"] = amount,
            ["approvedTotal"] = approvedTotal,
        };
        return new BillingDecision(
            AddDecisionIdFor(command.InvoiceId, command.AdjustmentId), AddActionCode, command.InvoiceId, BillingDecider.Human(command.ReviewerSystemUserId, ReviewerRole), facts, OutcomeAdded,
            Reason: reason, Values: values, Written: [new DecisionRecordRef(InvoiceReviewReadService.LineEntity, lineId)], DecidedOn: decidedOn);
    }

    /// <summary>095f-2b: the removal's Decision Record. No reason (the route takes none); <c>note</c> only when given.</summary>
    private static BillingDecision RemoveDecision(RemoveAdjustmentCommand command, string? note, decimal approvedTotal, DateTimeOffset decidedOn)
    {
        var values = new Dictionary<string, object?> { ["adjustment"] = command.LineId };
        if (note is not null)
        {
            values["note"] = note;
        }

        var facts = new Dictionary<string, object?>
        {
            ["invoiceId"] = command.InvoiceId,
            ["adjustmentLineId"] = command.LineId,
            ["approvedTotal"] = approvedTotal,
        };
        return new BillingDecision(
            RemoveDecisionIdFor(command.InvoiceId, command.LineId), RemoveActionCode, command.InvoiceId, BillingDecider.Human(command.ReviewerSystemUserId, ReviewerRole), facts,
            OutcomeRemoved, Reason: null, Values: values, Written: [new DecisionRecordRef(InvoiceReviewReadService.LineEntity, command.LineId)],
            DecidedOn: decidedOn);
    }

    // ── Remove ──

    public async Task<AdjustmentResult> RemoveAsync(RemoveAdjustmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.InvoiceId == Guid.Empty || command.LineId == Guid.Empty || command.ReviewerSystemUserId == Guid.Empty)
        {
            return new AdjustmentResult(AdjustmentStatus.InvalidRequest, AdjustmentCodes.InvalidRequest, "An invoice id and an adjustment id are required.");
        }

        // 095f-2b (design 1.1): the note is checked before the lock and any read.
        if (NoteError(command.Note, out var note) is { } invalidNote)
        {
            return invalidNote;
        }

        return await WithLockAsync(command.InvoiceId, () => ApplyRemoveAsync(command, note, ct), ct).ConfigureAwait(false);
    }

    private async Task<AdjustmentResult> ApplyRemoveAsync(RemoveAdjustmentCommand command, string? note, CancellationToken ct)
    {
        Entity? invoice;
        Entity? row;
        List<Entity> lineRows;
        try
        {
            invoice = await ReadInvoiceAsync(command.InvoiceId, ct).ConfigureAwait(false);
            if (invoice is null)
            {
                return new AdjustmentResult(AdjustmentStatus.InvoiceNotFound, "ebilling.invoice.not_found");
            }

            if (invoice.GetAttributeValue<OptionSetValue>("statuscode")?.Value != InvoiceStatusCodes.InReview)
            {
                return new AdjustmentResult(AdjustmentStatus.NotInReview, AdjustmentCodes.NotInReview, "Adjustments can be changed only while the invoice is In Review.");
            }

            // Task 095d-0 (EB-D-197): removing an adjustment is a change to a reviewer's adjustment too - "only Return and Set firm stay available".
            if (await InvoiceFirmGate.CheckAsync(_entities, invoice, ct).ConfigureAwait(false) is not InvoiceFirmGateResult.Open and var gate)
            {
                return new AdjustmentResult(AdjustmentStatus.FirmMissing, AdjustmentCodes.FirmMissing, AdjustmentCodes.AdjustFirmDetailFor(gate));
            }

            row = await ReadRowAsync(command.LineId, ct).ConfigureAwait(false);
            lineRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(command.InvoiceId), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        // Only an Adjustment line of THIS invoice is removable; anything else is the same uniform answer as a missing one.
        if (row is null
            || row.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup)?.Id != command.InvoiceId
            || row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value != (int)InvoiceLineCostType.Adjustment)
        {
            return new AdjustmentResult(AdjustmentStatus.AdjustmentNotFound, AdjustmentCodes.NotFound);
        }

        var alreadyRemoved = (row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineState)?.Value ?? InvoiceTotalsService.StateActive) != InvoiceTotalsService.StateActive;
        var writeColumn = true;
        DateTimeOffset removedOn;
        if (alreadyRemoved)
        {
            // 095f-2b: a repeated removal is the same reviewer's (the removal stamped them on sprk_reviewedby; the record names its decider).
            if (!IsReviewedBy(row, command.ReviewerSystemUserId))
            {
                return IdReused("This adjustment was already removed by another reviewer.");
            }

            // 095f-2b: once the removal's record exists the column already has the first attempt's note; the totals then run without it.
            try
            {
                writeColumn = !await IsRecordedAsync(RemoveActionCode, command.InvoiceId, RemoveDecisionIdFor(command.InvoiceId, command.LineId), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return LookupFailed(command.InvoiceId, DetailOf(ex), removal: true);
            }

            removedOn = row.GetAttributeValue<DateTime?>(InvoiceReviewReadService.LineReviewedOn) is { } at
                ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))
                : _time.GetUtcNow();
        }
        else
        {
            // ── Dry-run the totals without the line (a removal that cannot be totalled is refused, nothing changes). ──
            var inputs = lineRows.Where(r => r.Id != command.LineId).Select(ToInput).ToList();
            var dryRun = InvoiceTotalsCalculator.Calculate(invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value, inputs);
            if (dryRun.IsRefused)
            {
                return Refused(command.InvoiceId, dryRun);
            }

            if (!row.TryGetAttributeValue<long>(InvoiceTotalsService.VersionNumber, out var version) || version <= 0)
            {
                return new AdjustmentResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The adjustment version could not be read; nothing was changed.");
            }

            // ── 095f-2b: the Decision Record's no-I/O refusals, before the write. ──
            removedOn = _time.GetUtcNow();
            if (Preflight(RemoveDecision(command, note, dryRun.ApprovedTotal, removedOn), command.InvoiceId) is { } invalidRecord)
            {
                return invalidRecord;
            }

            try
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(
                    InvoiceReviewReadService.LineEntity,
                    [(command.LineId, DeactivationFields(command.ReviewerSystemUserId, removedOn.UtcDateTime), version)],
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (System.Data.DBConcurrencyException)
            {
                return new AdjustmentResult(AdjustmentStatus.Changed, AdjustmentCodes.LineChanged, "The adjustment changed while it was removed; nothing was changed. Reload and try again.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError("Adjustment removal on invoice {InvoiceId}: the update failed ({ExceptionType}); the outcome may be unknown.", command.InvoiceId, ex.GetType().Name);
                return new AdjustmentResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The adjustment could not be removed. Repeat the request to be sure.");
            }
        }

        InvoiceTotalsCalculation calculation;
        try
        {
            // The row is (or already was) deactivated: finish the totals whatever happens to the request. 095f-2b: a removal's note (none means
            // the column is left as it is: a removal has no reason) rides in the totals' own conditional update.
            calculation = await _totals.RecalculateAsync(command.InvoiceId, writeColumn ? note : null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Incomplete(command.InvoiceId, TotalsStep, ex.GetType().Name, removal: true);
        }

        if (calculation.IsRefused)
        {
            if (alreadyRemoved && !writeColumn)
            {
                // A repeat of a removal whose Decision Record exists: that removal finished, so it is removed for good and not this request's to
                // undo (the add path's rule for a repeat). The refusal is reported, nothing is written.
                return Refused(command.InvoiceId, calculation);
            }

            // ISS-030: this request deactivated the row (or an earlier one did and was refused without being undone: no record exists). The refused
            // calculation wrote no totals, so put the row back; nothing stays half applied. No Decision Record and no note are written.
            return await UndoRemovalAsync(command.InvoiceId, command.LineId, alreadyRemoved ? null : row, calculation).ConfigureAwait(false);
        }

        // Task 152 (FR-43): the removal changed the approved total; the change is the removed line's id (unique per adjustment line). Best effort.
        await InvoiceIndexingJobHandler.TryEnqueueAsync(
            _jobs, _logger, command.InvoiceId, $"r{command.LineId.ToString("N", CultureInfo.InvariantCulture)}").ConfigureAwait(false);

        // ── 095f-2b: ONE remove-adjustment record, after the deactivation and the totals; idempotent on remove-{lineId}. ──
        if (await RecordAsync(RemoveDecision(command, note, calculation.ApprovedTotal, removedOn), command.InvoiceId, removal: true, CancellationToken.None)
                .ConfigureAwait(false) is { } unrecorded)
        {
            return unrecorded;
        }

        return new AdjustmentResult(AdjustmentStatus.Applied, "ok", RemovedLineId: command.LineId, Totals: TotalsOf(invoice, calculation), AlreadyApplied: alreadyRemoved);
    }

    /// <summary>
    /// ISS-030: a removal's totals were refused after the row was deactivated, so the row is reactivated and the refusal answered. The write is the
    /// add path's own undo (a plain update: the platform returns no row version for the deactivation, and the invoice lock is held); it runs on
    /// <see cref="CancellationToken.None"/> because the deactivation already happened and must not be left behind by a cancelled request. A failed
    /// reactivation is 503 <c>incomplete</c> at the <see cref="UndoStep"/> step; the same request repeated finds the row inactive with no record,
    /// recalculates, and (still refused) undoes again.
    /// </summary>
    private async Task<AdjustmentResult> UndoRemovalAsync(Guid invoiceId, Guid lineId, Entity? priorRow, InvoiceTotalsCalculation calculation)
    {
        if (priorRow is null)
        {
            _logger.LogWarning(
                "Adjustment on invoice {InvoiceId}: an earlier removal of line {LineId} with no Decision Record was reverted on a repeat (the totals are still refused); the stamp is left as it is.",
                invoiceId, lineId);
        }

        try
        {
            await _entities.UpdateAsync(InvoiceReviewReadService.LineEntity, lineId, ReactivationFields(priorRow), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Incomplete(invoiceId, UndoStep, ex.GetType().Name, removal: true);
        }

        return Refused(invoiceId, calculation);
    }

    /// <summary>
    /// ISS-030: the reverse of <see cref="DeactivationFields"/> (statecode and statuscode travel together). <paramref name="priorRow"/> is the row as
    /// read before THIS request's deactivation: the remover's stamp is replaced by the reviewer and time the row held (cleared when it held none).
    /// On a repeat there is no prior row (an earlier request overwrote the stamp and it is not recoverable), so the stamp is left as the removal wrote it.
    /// </summary>
    private static Dictionary<string, object> ReactivationFields(Entity? priorRow)
    {
        var fields = new Dictionary<string, object>
        {
            [InvoiceTotalsService.LineState] = new OptionSetValue(InvoiceTotalsService.StateActive),
            [StatusReasonColumn] = new OptionSetValue(StatusReasonActive),
        };

        if (priorRow is not null)
        {
            fields[InvoiceReviewReadService.LineReviewedBy] =
                priorRow.GetAttributeValue<EntityReference>(InvoiceReviewReadService.LineReviewedBy) is { } by ? by : DBNull.Value;
            fields[InvoiceReviewReadService.LineReviewedOn] =
                priorRow.GetAttributeValue<DateTime?>(InvoiceReviewReadService.LineReviewedOn) is { } on ? on : DBNull.Value;
        }

        return fields;
    }

    // ── Shared ──

    private async Task<AdjustmentResult> WithLockAsync(Guid invoiceId, Func<Task<AdjustmentResult>> work, CancellationToken ct)
    {
        var key = InvoiceLineDecisionService.LockKeyPrefix + invoiceId.ToString("N", CultureInfo.InvariantCulture);
        var owner = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        bool acquired;
        try
        {
            acquired = await _locks.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Fail closed: without the lock the one-at-a-time guarantee is gone.
            _logger.LogWarning("Adjustment on invoice {InvoiceId}: the processing lock could not be taken ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
            return new AdjustmentResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The change could not be started; nothing was changed. Try again.");
        }

        if (!acquired)
        {
            return new AdjustmentResult(AdjustmentStatus.Busy, AdjustmentCodes.Busy, "Another change to this invoice is in progress. Try again.");
        }

        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _locks.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The lock expires by itself (LockDuration); a failed release must not hide the change's own outcome.
                _logger.LogWarning("Adjustment on invoice {InvoiceId}: the processing lock could not be released ({ExceptionType}); it expires.", invoiceId, ex.GetType().Name);
            }
        }
    }

    private AdjustmentResult ReadFailed(Guid invoiceId, Exception ex)
    {
        _logger.LogWarning("Adjustment on invoice {InvoiceId}: a read failed ({ExceptionType}); nothing changed.", invoiceId, ex.GetType().Name);
        return new AdjustmentResult(AdjustmentStatus.Unavailable, AdjustmentCodes.Unavailable, "The invoice could not be read; nothing was changed.");
    }

    /// <summary>The line was written (or deactivated) but <paramref name="step"/> did not finish: <c>totals</c>, 095f-2b <c>record</c>, or ISS-030
    /// <c>undo</c> (a refused add or removal that could not be taken back): 503, and a repeat of the same request finishes it.
    /// <paramref name="detail"/> is an exception type name or a refusal reason, never request text.</summary>
    private AdjustmentResult Incomplete(Guid invoiceId, string step, string detail, bool removal = false)
    {
        _logger.LogError(
            "Adjustment on invoice {InvoiceId}: the {Step} step did not finish ({Detail}); the caller repeats the same request to finish it.",
            invoiceId, step, detail);
        return new AdjustmentResult(
            AdjustmentStatus.Incomplete, AdjustmentCodes.Incomplete,
            (removal, step) switch
            {
                (true, RecordStep) => "The adjustment was removed and the invoice totals were updated, but the removal was not recorded. Repeat the same request to complete it.",
                (false, RecordStep) => "The adjustment and the invoice totals were updated, but the adjustment was not recorded. Repeat the same request to complete it.",
                (true, UndoStep) => "The adjustment removal was refused, but the adjustment could not be put back. Repeat the same request to complete it.",
                (false, UndoStep) => "The adjustment was refused, but it could not be taken back. Repeat the same request to complete it.",
                (true, _) => "The adjustment was removed but the invoice totals were not updated. Repeat the same request to complete it.",
                _ => "The adjustment was saved but the invoice totals were not updated. Repeat the same request to complete it.",
            });
    }

    /// <summary>095f-2b (review round 1): the stored-record lookup of a repeat faulted, so how far the first attempt got (the totals, the record) is not
    /// known: 503 <c>incomplete</c> with neutral wording, and the same repeat finishes whatever is left (unlike <see cref="Incomplete"/>, it never says
    /// the totals were updated).</summary>
    private AdjustmentResult LookupFailed(Guid invoiceId, string detail, bool removal = false)
    {
        _logger.LogError(
            "Adjustment on invoice {InvoiceId}: the stored record could not be looked up on a repeat ({Detail}); the caller repeats the same request to finish it.",
            invoiceId, detail);
        return new AdjustmentResult(
            AdjustmentStatus.Incomplete, AdjustmentCodes.Incomplete,
            removal
                ? "The removal is not finished. Repeat the same request to complete it."
                : "The adjustment is not finished. Repeat the same request to complete it.");
    }

    private static AdjustmentResult IdReused(string detail) => new(AdjustmentStatus.IdReused, AdjustmentCodes.IdReused, detail);

    private static string DetailOf(Exception ex) => (ex as DecisionRecordRefusedException)?.Reason ?? ex.GetType().Name;

    /// <summary>095f-2b: whether a Decision Record already exists under <paramref name="decisionId"/> on the invoice (read only). Throws
    /// <see cref="DecisionRecordRefusedException"/> when the lookup faults or is ambiguous.</summary>
    private async Task<bool> IsRecordedAsync(string actionCode, Guid invoiceId, Guid decisionId, CancellationToken ct) =>
        await _recorder.FindStoredResolvedSetAsync(actionCode, invoiceId, decisionId, ct).ConfigureAwait(false) is not null;

    /// <summary>095f-2b: every refusal the record can give without I/O, before the service's own first write (400, nothing written).</summary>
    private AdjustmentResult? Preflight(BillingDecision decision, Guid invoiceId)
    {
        try
        {
            _recorder.Preflight(decision);
            return null;
        }
        catch (DecisionRecordRefusedException ex)
        {
            _logger.LogWarning("Adjustment on invoice {InvoiceId}: the Decision Record would be refused ({Reason}); nothing changed.", invoiceId, ex.Reason);
            return new AdjustmentResult(AdjustmentStatus.InvalidRequest, LineDecisionCodes.DecisionInvalid, ex.Reason);
        }
    }

    /// <summary>
    /// 095f-2b: writes the decision's one record (or finds it). The writer's "same decision id, same billing action, other content" refusal (a
    /// repeat whose note differs from the recorded one) is 409 <c>id_reused</c>, nothing more written; it is never a 503 that a repeat can never
    /// complete (ISS-029). Any other failure is 503 <c>incomplete</c> at the record step, finished by a repeat.
    /// </summary>
    private async Task<AdjustmentResult?> RecordAsync(BillingDecision decision, Guid invoiceId, bool removal, CancellationToken ct)
    {
        try
        {
            await _recorder.RecordAsync(decision, ct).ConfigureAwait(false);
            return null;
        }
        catch (DecisionRecordRefusedException ex) when (IsOtherContent(ex))
        {
            _logger.LogInformation(
                "Adjustment on invoice {InvoiceId}: decision {DecisionId} is recorded with other content; the repeat is refused.", invoiceId, decision.DecisionId);
            return IdReused(removal
                ? "This adjustment was already removed with a different note."
                : "This adjustment id was already used for this adjustment with a different note. Use a new adjustment id.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(invoiceId, RecordStep, DetailOf(ex), removal);
        }
    }

    /// <summary>The writer's "same id, same billing action, other content" refusal: the only record refusal answered <c>id_reused</c> (as Add line,
    /// 164a fix round K2). A recorded content that cannot be read, or another billing action under the id, is not proof the caller changed its request.</summary>
    private static bool IsOtherContent(DecisionRecordRefusedException ex) =>
        ex.Reason == DecisionRecordRefusalReason.ReviewInvalid && ex.IsOtherContent;

    private AdjustmentResult Refused(Guid invoiceId, InvoiceTotalsCalculation calculation)
    {
        var refusal = calculation.Refusal!;
        _logger.LogInformation("Adjustment on invoice {InvoiceId}: the totals would be refused ({ReasonCode}); nothing changed.", invoiceId, refusal.Code);
        return refusal.Code == InvoiceTotalsRefusalCodes.ApprovedTotalNegative
            ? new AdjustmentResult(AdjustmentStatus.NegativeTotal, AdjustmentCodes.NegativeTotal, refusal.Message)
            : new AdjustmentResult(AdjustmentStatus.TotalsRefused, AdjustmentCodes.TotalsRefused, refusal.Code);
    }

    /// <summary>NFR-09: the row is deactivated (statecode and statuscode travel together), never deleted, and the remover is stamped.</summary>
    private static Dictionary<string, object> DeactivationFields(Guid reviewer, DateTime atUtc) => new()
    {
        [InvoiceTotalsService.LineState] = new OptionSetValue(StateInactive),
        [StatusReasonColumn] = new OptionSetValue(StatusReasonInactive),
        [InvoiceReviewReadService.LineReviewedBy] = new EntityReference(SystemUserEntity, reviewer),
        [InvoiceReviewReadService.LineReviewedOn] = atUtc,
    };

    internal static InvoiceLineInput ToInput(Entity row)
    {
        var type = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineCostType)?.Value;
        var status = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value;
        var method = row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineAdjustmentMethod)?.Value;
        return new InvoiceLineInput(
            row.Id,
            type is { } t && Enum.IsDefined(typeof(InvoiceLineCostType), t) ? (InvoiceLineCostType)t : null,
            row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value ?? 0m,
            row.GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount)?.Value,
            status is { } s && Enum.IsDefined(typeof(InvoiceLineStatus), s) ? (InvoiceLineStatus)s : null,
            method is { } m && Enum.IsDefined(typeof(InvoiceAdjustmentMethod), m) ? (InvoiceAdjustmentMethod)m : null,
            row.GetAttributeValue<decimal?>(InvoiceTotalsService.LineAdjustmentValue),
            InvoiceTotalsService.ReviewerAddedOf(row));
    }

    private static ReviewTotalsDto TotalsOf(Entity invoice, InvoiceTotalsCalculation calculation) => new(
        invoice.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
        calculation.FeesTotal, calculation.ExpensesTotal, calculation.LineAdjustmentsTotal, calculation.InvoiceAdjustmentsTotal, calculation.ApprovedTotal);

    private async Task<Entity?> ReadInvoiceAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.InvoiceEntity}id", ConditionOperator.Equal, invoiceId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault();
    }

    /// <summary>One line row in ANY state (the totals' reads leave out a removed Adjustment line, which a repeat and a remove still need to see).</summary>
    private async Task<Entity?> ReadRowAsync(Guid lineId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceReviewReadService.LineEntity)
        {
            ColumnSet = new ColumnSet(RowColumns),
            Criteria = new FilterExpression(LogicalOperator.And),
            TopCount = 1,
        };
        query.Criteria.AddCondition($"{InvoiceReviewReadService.LineEntity}id", ConditionOperator.Equal, lineId);
        var page = await _entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        return page.Entities.FirstOrDefault();
    }
}
