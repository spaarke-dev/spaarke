using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>Request to add an invoice-level Adjustment line (FR-32). The server computes the amount (016); the body never carries one, nor a total.</summary>
/// <param name="AdjustmentId">The client's id for this adjustment: a retry of the same request carries the same id and adds nothing twice.</param>
/// <param name="Method"><c>percent</c> or <c>fixed</c>.</param>
/// <param name="Value">A percent above 0 and up to 100, or a fixed amount above 0 (at most 2 decimals).</param>
/// <param name="RuleId">Optional: the <c>sprk_policyversion</c> the reviewer cites, which must be one of the invoice's rule options (F-8, D-74).</param>
/// <param name="ReviewerSystemUserId">The authenticated caller's <c>systemuserid</c>, resolved by the endpoint from the caller's own token, never from the body.</param>
public sealed record AddAdjustmentCommand(
    Guid InvoiceId, Guid AdjustmentId, string? Method, decimal? Value, string? Reason, Guid? RuleId, Guid ReviewerSystemUserId);

/// <summary>Request to remove an Adjustment line. The row is deactivated, never deleted (NFR-09).</summary>
public sealed record RemoveAdjustmentCommand(Guid InvoiceId, Guid LineId, Guid ReviewerSystemUserId);

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
}

/// <summary>An Adjustment line as the reviewer needs it to update the screen without a second read.</summary>
public sealed record AdjustmentDto(
    Guid LineId, Guid AdjustmentId, string Method, decimal Value, decimal Amount, string Reason, Guid? RuleId, DateTimeOffset ReviewedOn);

/// <summary>One rule a reviewer may cite on an adjustment (D-74): a published billing rule version in force on the invoice date. No document link (D-66).</summary>
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
/// <para><b>No Decision Record.</b> FR-30's list is line accept / reduce / disallow and invoice approve / reject / return / override; an
/// adjustment is not on it (D-75 files one record per invoice DECISION, which is task 103's approval). An adjustment is attributable through
/// <c>sprk_reviewedby / on</c> and the table's audit (NFR-09).</para>
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

    internal const string SystemUserEntity = InvoiceLineDecisionService.SystemUserEntity;
    internal const string PolicyVersionEntity = "sprk_policyversion";
    internal const string RuleSetEntity = BillingFactLoader.RuleSetEntity;

    internal const string LineVendorOrg = InvoiceSubmissionPersister.InvoiceVendorOrg;
    internal const string LineRule = "sprk_adjustmentrule";
    internal const string InvoiceDateColumn = InvoiceReviewService.InvInvoiceDate;
    internal const string StatusReasonColumn = "statuscode";
    internal const int StatusReasonInactive = 2;
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
    ];

    /// <summary>The seam the unit tests use: the rules of an invoice's firm, as the evaluator loads them (<see cref="BillingFactLoader.LoadRulesAsync"/>).</summary>
    internal delegate Task<IReadOnlyList<BillingRuleCandidate>> LoadRules(Guid firmId, CancellationToken ct);

    private readonly IGenericEntityService _entities;
    private readonly IInvoiceTotalsService _totals;
    private readonly IIdempotencyService _locks;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly LoadRules _loadRules;
    private readonly ILogger<InvoiceAdjustmentService> _logger;
    private readonly TimeProvider _time;

    public InvoiceAdjustmentService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        BillingFactLoader rules,
        ILogger<InvoiceAdjustmentService> logger)
        : this(entities, totals, locks, ownership, (rules ?? throw new ArgumentNullException(nameof(rules))).LoadRulesAsync, logger)
    {
    }

    internal InvoiceAdjustmentService(
        IGenericEntityService entities,
        IInvoiceTotalsService totals,
        IIdempotencyService locks,
        IRecordOwnershipResolver ownership,
        LoadRules loadRules,
        ILogger<InvoiceAdjustmentService> logger,
        TimeProvider? time = null)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _totals = totals ?? throw new ArgumentNullException(nameof(totals));
        _locks = locks ?? throw new ArgumentNullException(nameof(locks));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _loadRules = loadRules ?? throw new ArgumentNullException(nameof(loadRules));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The row id of an adjustment: the client's id hashed with the invoice, so another invoice's id can never collide with it.</summary>
    internal static Guid LineIdFor(Guid invoiceId, Guid adjustmentId) =>
        InvoiceLineDecisionService.IdOf($"ebilling-adjustment-{invoiceId:D}-{adjustmentId:D}");

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

        var firm = invoice.GetAttributeValue<EntityReference>(InvoiceSubmissionPersister.InvoiceVendorOrg)?.Id ?? Guid.Empty;
        var candidates = await _loadRules(firm, ct).ConfigureAwait(false);

        // One version per policy, the one in force on the invoice date (the evaluator's own selection). A type the catalogue does not know, a
        // non-billing type and a system type are never offered: fail closed (FR-24).
        var chosen = candidates
            .Where(IsCitableType)
            .GroupBy(c => c.PolicyId)
            .Select(g => InvoiceComplianceEvaluator.SelectVersion(g.ToList(), date))
            .Where(v => v is not null)
            .Select(v => v!)
            .ToList();

        var names = await ReadRuleSetNamesAsync(chosen.Select(c => c.RuleSetId).Distinct().ToList(), ct).ConfigureAwait(false);
        var options = chosen
            .Select(c => new AdjustmentRuleOptionDto(
                c.PolicyVersionId, c.PolicyCode, c.ShortHeadline, c.RuleSetId, names.GetValueOrDefault(c.RuleSetId),
                c.Scope.ToString(), c.VersionNumber, c.ClauseReference, c.ClauseLocation))
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
        if (ShapeError(command, out var method, out var value, out var reason) is { } invalid)
        {
            return invalid;
        }

        return await WithLockAsync(command.InvoiceId, () => ApplyAddAsync(command, method, value, reason, ct), ct).ConfigureAwait(false);
    }

    /// <summary>The request-shape rules that need no data (400).</summary>
    internal static AdjustmentResult? ShapeError(AddAdjustmentCommand command, out InvoiceAdjustmentMethod method, out decimal value, out string reason)
    {
        static AdjustmentResult Bad(string code, string detail) => new(AdjustmentStatus.InvalidRequest, code, detail);

        method = default;
        value = 0m;
        reason = string.Empty;

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
        return null;
    }

    private async Task<AdjustmentResult> ApplyAddAsync(AddAdjustmentCommand command, InvoiceAdjustmentMethod method, decimal value, string reason, CancellationToken ct)
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

            lineRows = await InvoiceReviewReadService.ReadPagedAsync(_entities, InvoiceReviewReadService.LineQuery(command.InvoiceId), ct).ConfigureAwait(false);
            existing = await ReadRowAsync(lineId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return ReadFailed(command.InvoiceId, ex);
        }

        // ── A repeat of the same request: finish the totals step, add nothing. ──
        if (existing is not null)
        {
            if (!IsSameAdjustment(existing, command.InvoiceId, method, value, reason, command.RuleId))
            {
                return new AdjustmentResult(
                    AdjustmentStatus.IdReused, AdjustmentCodes.IdReused,
                    "This adjustment id was used for a different or a removed adjustment. Use a new adjustment id.");
            }

            return await FinishAddAsync(command, lineId, method, value, reason, existing.GetAttributeValue<DateTime?>(InvoiceReviewReadService.LineReviewedOn), invoice, alreadyApplied: true, ct)
                .ConfigureAwait(false);
        }

        // ── F-8 / D-74: a cited rule must be one of this invoice's options. ──
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

        // ── The row: the invoice's core record and resolver fields (013), its owner (uac-r2), the firm. ──
        var core = EligibleApproverResolver.SingleCoreOf(InvoiceReviewReadService.InvoiceEntity, invoice);
        if (core is null)
        {
            return new AdjustmentResult(AdjustmentStatus.OwnerUnresolved, AdjustmentCodes.OwnerUnresolved, "The invoice names no single core record.");
        }

        var reviewedOn = _time.GetUtcNow();
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

        return await FinishAddAsync(command, lineId, method, value, reason, reviewedOn.UtcDateTime, invoice, alreadyApplied: false, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Step 2 of an add: 016 stores the totals. A refusal here (the dry run passed, so another writer changed the invoice) deactivates the new row.</summary>
    private async Task<AdjustmentResult> FinishAddAsync(
        AddAdjustmentCommand command, Guid lineId, InvoiceAdjustmentMethod method, decimal value, string reason, DateTime? reviewedOnUtc,
        Entity invoice, bool alreadyApplied, CancellationToken ct)
    {
        InvoiceTotalsCalculation calculation;
        try
        {
            calculation = await _totals.RecalculateAsync(command.InvoiceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Incomplete(command.InvoiceId, ex.GetType().Name);
        }

        if (calculation.IsRefused && alreadyApplied)
        {
            // A repeat: the line was applied by an earlier request and is not this one's to undo; the refusal is reported, nothing is written.
            return Refused(command.InvoiceId, calculation);
        }

        if (calculation.IsRefused)
        {
            // Undo the line the refused calculation already counts (016 notes section 3): deactivate it so nothing stays half applied.
            try
            {
                await _entities.UpdateAsync(
                    InvoiceReviewReadService.LineEntity, lineId, DeactivationFields(command.ReviewerSystemUserId, _time.GetUtcNow().UtcDateTime), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return Incomplete(command.InvoiceId, "undo:" + ex.GetType().Name);
            }

            return Refused(command.InvoiceId, calculation);
        }

        var on = reviewedOnUtc is { } at ? new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)) : _time.GetUtcNow();
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

    // ── Remove ──

    public async Task<AdjustmentResult> RemoveAsync(RemoveAdjustmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.InvoiceId == Guid.Empty || command.LineId == Guid.Empty || command.ReviewerSystemUserId == Guid.Empty)
        {
            return new AdjustmentResult(AdjustmentStatus.InvalidRequest, AdjustmentCodes.InvalidRequest, "An invoice id and an adjustment id are required.");
        }

        return await WithLockAsync(command.InvoiceId, () => ApplyRemoveAsync(command, ct), ct).ConfigureAwait(false);
    }

    private async Task<AdjustmentResult> ApplyRemoveAsync(RemoveAdjustmentCommand command, CancellationToken ct)
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
        if (!alreadyRemoved)
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

            try
            {
                await _entities.BulkUpdateIfRowVersionsMatchAsync(
                    InvoiceReviewReadService.LineEntity,
                    [(command.LineId, DeactivationFields(command.ReviewerSystemUserId, _time.GetUtcNow().UtcDateTime), version)],
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
            // The row is (or already was) deactivated: finish the totals whatever happens to the request.
            calculation = await _totals.RecalculateAsync(command.InvoiceId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Incomplete(command.InvoiceId, ex.GetType().Name, removal: true);
        }

        if (calculation.IsRefused)
        {
            return Refused(command.InvoiceId, calculation);
        }

        return new AdjustmentResult(AdjustmentStatus.Applied, "ok", RemovedLineId: command.LineId, Totals: TotalsOf(invoice, calculation), AlreadyApplied: alreadyRemoved);
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

    private AdjustmentResult Incomplete(Guid invoiceId, string detail, bool removal = false)
    {
        _logger.LogError(
            "Adjustment on invoice {InvoiceId}: the line was written but the totals step failed ({Detail}); the caller repeats the same request to finish it.",
            invoiceId, detail);
        return new AdjustmentResult(
            AdjustmentStatus.Incomplete, AdjustmentCodes.Incomplete,
            removal
                ? "The adjustment was removed but the invoice totals were not updated. Repeat the same request to complete it."
                : "The adjustment was saved but the invoice totals were not updated. Repeat the same request to complete it.");
    }

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
            row.GetAttributeValue<decimal?>(InvoiceTotalsService.LineAdjustmentValue));
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
