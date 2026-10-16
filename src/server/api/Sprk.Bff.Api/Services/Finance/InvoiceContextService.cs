using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// Response contract (ebilling-r1 task 106, FR-33). Camel-cased on the wire by the minimal-API defaults.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The three states of one context section.</summary>
public static class ContextSectionStatus
{
    /// <summary><see cref="ContextSection{T}.Data"/> holds the section.</summary>
    public const string Available = "available";

    /// <summary>The section's source could not be read (or the section cannot be built for this invoice); <c>Code</c> and <c>Message</c> say why.</summary>
    public const string Unavailable = "unavailable";

    /// <summary>The section does not apply to this kind of core record (a Work Assignment has no budget rollup). Not an error.</summary>
    public const string NotTracked = "notTracked";
}

/// <summary>One section of the Invoice Review context panel: its data, or a stable code and a message saying why it is not there.</summary>
public sealed record ContextSection<T>(string Status, T? Data, string? Code, string? Message) where T : class
{
    public static ContextSection<T> Available(T data) => new(ContextSectionStatus.Available, data, null, null);

    public static ContextSection<T> Unavailable(string code, string message) => new(ContextSectionStatus.Unavailable, null, code, message);

    public static ContextSection<T> NotTracked(string code, string message) => new(ContextSectionStatus.NotTracked, null, code, message);
}

/// <summary>The Invoice Review context panel for one invoice (FR-33): five independent sections.</summary>
public sealed record InvoiceContextDto(
    Guid InvoiceId,
    ContextSection<CoreRecordContext> CoreRecord,
    ContextSection<BudgetContext> Budget,
    ContextSection<PriorInvoicesContext> PriorInvoices,
    ContextSection<DisallowHistoryContext> DisallowHistory,
    ContextSection<RateHistoryContext> RateHistory);

/// <param name="Type"><c>matter</c>, <c>project</c> or <c>workAssignment</c>.</param>
/// <param name="Status">The status reason's display label as Dataverse formats it (null when it did not).</param>
/// <param name="OpenedDate">Matter / project <c>sprk_openeddate</c>; always null for a Work Assignment (the table has no such column).</param>
public sealed record CoreRecordContext(
    string Type, Guid Id, string? Name, string? Number, int? StatusCode, string? Status, DateOnly? OpenedDate, DateOnly? ClosedDate);

/// <summary>Budget and spend exactly as the matter / project rollups store them (FinanceRollupService); nothing is recomputed here.</summary>
/// <param name="AsOf">The core record's <c>modifiedon</c>: the rollups carry no timestamp of their own, so this is the last write of any kind to the record.</param>
public sealed record BudgetContext(
    decimal? Budget, decimal? SpendToDate, decimal? Remaining, decimal? UtilizationPercent, int? InvoiceCount, DateTime? AsOf);

public sealed record PriorInvoiceItem(
    Guid Id, string? Number, DateOnly? InvoiceDate, decimal? Billed, decimal? Approved, string Status, int StatusCode);

/// <param name="HasMore">More invoices exist than the <paramref name="Limit"/> returned.</param>
public sealed record PriorInvoicesContext(int Limit, bool HasMore, IReadOnlyList<PriorInvoiceItem> Items);

public sealed record DisallowReason(string Reason, int Count);

/// <summary>Reduced and Disallowed lines on this firm's decided invoices (Approved, Sent to AP, Paid) on this core record, this invoice excluded.</summary>
public sealed record DisallowHistoryContext(
    int InvoicesConsidered, int ReducedCount, decimal ReducedAmount, int DisallowedCount, decimal DisallowedAmount, IReadOnlyList<DisallowReason> TopReasons);

/// <param name="Basis"><c>timekeeper</c> (a rate for this person) or <c>classification</c> (a rate for the person's LEDES classification).</param>
/// <param name="Scope"><c>coreRecord</c> (a card scoped to this core record) or <c>firm</c> (the firm's general card).</param>
public sealed record ApprovedRateItem(
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, decimal ApprovedRate, decimal? RackRate, string Basis, string Scope);

public sealed record BilledRateItem(decimal Rate, int LineCount, DateOnly? FirstSeen, DateOnly? LastSeen);

/// <param name="TimekeeperId">The roster timekeeper; null for a name on the invoice the roster did not match (no approved rates are looked up for it).</param>
public sealed record TimekeeperRateHistory(
    Guid? TimekeeperId,
    string Name,
    IReadOnlyList<ApprovedRateItem> ApprovedRates,
    IReadOnlyList<BilledRateItem> BilledOnThisInvoice,
    IReadOnlyList<BilledRateItem> BilledPreviously);

public sealed record RateHistoryContext(IReadOnlyList<TimekeeperRateHistory> Timekeepers);

/// <summary>
/// Builds the Invoice Review context panel (FR-33): core-record status and dates, budget vs spend from the existing rollups, other invoices
/// from this firm on this core record, this firm's disallow history, and timekeeper rate history. Read-only and deterministic: no AI, no
/// ranking, and the same data gives the same response (every list has a total order).
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> The caller supplies one id, the invoice, and the route filter has already authorized Read on it as the caller. The firm
/// (<c>sprk_vendororg</c>) and the core record (exactly one of <c>sprk_matter</c> / <c>sprk_project</c> / <c>sprk_regardingworkassignment</c>)
/// are read from that invoice; no firm or core record id ever comes from the request. When either cannot be resolved the sections that need
/// it say so instead of querying (a null firm must never become a "match every invoice without a firm" filter). The reads here are app-only,
/// as every read in this namespace is; they are bounded by the invoice's own firm and core record.</para>
/// <para><b>Independence.</b> Each section is loaded by its own guarded task: any exception or the shared <see cref="Deadline"/> turns that
/// section into an unavailable message with a stable code (<c>context.&lt;section&gt;.unavailable</c> / <c>.timeout</c>) and the others still
/// return. Only a cancelled request propagates. The cause is logged with ids only (ADR-015).</para>
/// <para><b>Queries (independent of the invoice's line count; NFR-02).</b> One header read, then in parallel: core record (1), budget (1, none
/// for a Work Assignment), other invoices (1), disallow history (1, paged), rate history (1 fee-line read of this invoice, then in parallel the
/// roster, the approved rates and the previously billed rates; none of the last three when the invoice has no roster-matched timekeeper).
/// A paged read stops after <see cref="MaxPages"/> pages and the section says so rather than report a partial total.</para>
/// <para><b>Budget is never computed here.</b> It reads <c>sprk_totalbudget</c>, <c>sprk_totalspendtodate</c>, <c>sprk_remainingbudget</c>,
/// <c>sprk_budgetutilizationpercent</c> and <c>sprk_invoicecount</c> as <see cref="FinanceRollupService"/> wrote them. If none of them is
/// populated the section is unavailable (<c>context.budget.rollup-not-populated</c>) instead of falling back to a sum (task 106 escalation).</para>
/// </remarks>
public sealed class InvoiceContextService
{
    /// <summary>How many other invoices the panel lists.</summary>
    public const int PriorInvoiceLimit = 10;

    /// <summary>How many disallow reasons the panel lists.</summary>
    public const int TopReasonLimit = 5;

    /// <summary>Pages (of <see cref="PageSize"/>) a history read may take before the section reports it is too large.</summary>
    public const int MaxPages = 10;

    private const int PageSize = 5000;
    private const int MaxTimekeepers = 1000;
    private const int MaxReasonLength = 200;

    // Codes (stable; the UI keys on them).
    public const string CodeInvoiceUnavailable = "context.invoice.unavailable";
    public const string CodeInvoiceTimeout = "context.invoice.timeout";
    public const string CodeCoreUnresolved = "context.core-record.unresolved";
    public const string CodeCoreAmbiguous = "context.core-record.ambiguous";
    public const string CodeFirmUnresolved = "context.firm.unresolved";
    public const string CodeBudgetNotTracked = "context.budget.not-tracked";
    public const string CodeBudgetNotPopulated = "context.budget.rollup-not-populated";
    public const string CodeDisallowTooLarge = "context.disallow-history.too-large";
    public const string CodeRateTooLarge = "context.rate-history.too-large";

    internal const string SectionCoreRecord = "context.core-record";
    internal const string SectionBudget = "context.budget";
    internal const string SectionPriorInvoices = "context.prior-invoices";
    internal const string SectionDisallowHistory = "context.disallow-history";
    internal const string SectionRateHistory = "context.rate-history";

    internal const string MatterEntity = "sprk_matter";
    internal const string ProjectEntity = "sprk_project";
    internal const string WorkAssignmentEntity = "sprk_workassignment";
    internal const string TimekeeperEntity = InvoiceResolutionService.TimekeeperEntity;

    internal const string InvoiceFirm = "sprk_vendororg";
    internal const string InvoiceMatter = "sprk_matter";
    internal const string InvoiceProject = "sprk_project";
    internal const string InvoiceWorkAssignment = "sprk_regardingworkassignment";
    internal const string LineResolvedTimekeeper = "sprk_resolvedtimekeeper";
    internal const string LineTimekeeperAsBilled = "sprk_timekeeper";
    internal const string LineRate = "sprk_rate";
    internal const string LineEventDate = "sprk_eventdate";
    internal const string LineAdjustmentReason = "sprk_adjustmentreason";

    /// <summary>Statuses whose Reduced / Disallowed lines count as the firm's disallow history: the decision stands.</summary>
    internal static readonly int[] StandingInvoiceStatuses =
    [
        InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid,
    ];

    // The rate query reused from task 046 is bounded by dates; these bounds select every period.
    private static readonly DateOnly AllPeriodsFrom = new(1900, 1, 1);
    private static readonly DateOnly AllPeriodsTo = new(2999, 12, 31);

    private readonly IGenericEntityService _entities;
    private readonly ILogger<InvoiceContextService> _logger;

    public InvoiceContextService(IGenericEntityService entities, ILogger<InvoiceContextService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The time the whole response may take, from the start of the request. NFR-02 is a 3 s p90; this is the hard stop behind it. A section
    /// not loaded by then reports <c>.timeout</c>.
    /// </summary>
    internal TimeSpan Deadline { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The panel for <paramref name="invoiceId"/>, or <c>null</c> when the invoice does not exist.</summary>
    public async Task<InvoiceContextDto?> GetAsync(Guid invoiceId, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Deadline);

        InvoiceScope? scope;
        try
        {
            scope = await ReadScopeAsync(invoiceId, deadline.Token);
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            var timedOut = deadline.IsCancellationRequested;
            _logger.LogWarning(ex, "Invoice context: the header of invoice {InvoiceId} could not be read (timedOut={TimedOut})", invoiceId, timedOut);
            return AllUnavailable(invoiceId, timedOut ? CodeInvoiceTimeout : CodeInvoiceUnavailable, "The invoice could not be read, so no context is available.");
        }

        if (scope is null)
        {
            return null;
        }

        var token = deadline.Token;
        var coreRecord = GuardAsync(invoiceId, SectionCoreRecord, token, ct, () => LoadCoreRecordAsync(scope, token), "Core record details");
        var budget = GuardAsync(invoiceId, SectionBudget, token, ct, () => LoadBudgetAsync(scope, token), "Budget");
        var prior = GuardAsync(invoiceId, SectionPriorInvoices, token, ct, () => LoadPriorInvoicesAsync(scope, token), "Prior invoices");
        var disallow = GuardAsync(invoiceId, SectionDisallowHistory, token, ct, () => LoadDisallowHistoryAsync(scope, token), "Disallow history");
        var rates = GuardAsync(invoiceId, SectionRateHistory, token, ct, () => LoadRateHistoryAsync(scope, token), "Rate history");

        await Task.WhenAll(coreRecord, budget, prior, disallow, rates);

        return new InvoiceContextDto(invoiceId, await coreRecord, await budget, await prior, await disallow, await rates);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Scope: the invoice's own firm and core record
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record InvoiceScope(Guid InvoiceId, Guid? FirmId, CoreRecordReference? Core, string? CoreProblemCode);

    private async Task<InvoiceScope?> ReadScopeAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceTotalsService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet(InvoiceFirm, InvoiceMatter, InvoiceProject, InvoiceWorkAssignment),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition("sprk_invoiceid", ConditionOperator.Equal, invoiceId);

        var page = await _entities.RetrieveMultipleAsync(query, ct);
        var invoice = page.Entities.FirstOrDefault();
        if (invoice is null)
        {
            return null;
        }

        var cores = new List<CoreRecordReference>();
        if (invoice.GetAttributeValue<EntityReference>(InvoiceMatter) is { } matter)
        {
            cores.Add(new CoreRecordReference(CoreRecordKind.Matter, matter.Id));
        }

        if (invoice.GetAttributeValue<EntityReference>(InvoiceProject) is { } project)
        {
            cores.Add(new CoreRecordReference(CoreRecordKind.Project, project.Id));
        }

        if (invoice.GetAttributeValue<EntityReference>(InvoiceWorkAssignment) is { } workAssignment)
        {
            cores.Add(new CoreRecordReference(CoreRecordKind.WorkAssignment, workAssignment.Id));
        }

        // FR-05: exactly one core record. Two set at once is not resolved by guessing which one counts.
        var firm = invoice.GetAttributeValue<EntityReference>(InvoiceFirm)?.Id;
        return cores.Count switch
        {
            1 => new InvoiceScope(invoiceId, firm, cores[0], null),
            0 => new InvoiceScope(invoiceId, firm, null, CodeCoreUnresolved),
            _ => new InvoiceScope(invoiceId, firm, null, CodeCoreAmbiguous),
        };
    }

    private static string CoreProblemMessage(string code) => code == CodeCoreAmbiguous
        ? "This invoice is filed under more than one core record, so no context can be chosen."
        : "This invoice has no core record, so no context is available.";

    private static InvoiceContextDto AllUnavailable(Guid invoiceId, string code, string message) => new(
        invoiceId,
        ContextSection<CoreRecordContext>.Unavailable(code, message),
        ContextSection<BudgetContext>.Unavailable(code, message),
        ContextSection<PriorInvoicesContext>.Unavailable(code, message),
        ContextSection<DisallowHistoryContext>.Unavailable(code, message),
        ContextSection<RateHistoryContext>.Unavailable(code, message));

    private async Task<ContextSection<T>> GuardAsync<T>(
        Guid invoiceId, string section, CancellationToken deadlineToken, CancellationToken requestToken,
        Func<Task<ContextSection<T>>> load, string title) where T : class
    {
        try
        {
            return await load();
        }
        catch (Exception ex)
        {
            if (requestToken.IsCancellationRequested)
            {
                throw;
            }

            var timedOut = deadlineToken.IsCancellationRequested;
            _logger.LogWarning(ex, "Invoice context: section {Section} of invoice {InvoiceId} is unavailable (timedOut={TimedOut})", section, invoiceId, timedOut);
            return ContextSection<T>.Unavailable(
                section + (timedOut ? ".timeout" : ".unavailable"),
                timedOut ? $"{title} took too long to load." : $"{title} could not be loaded.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Core record
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record CoreShape(string Type, string Entity, string IdColumn, string NameColumn, string NumberColumn, string? OpenedColumn, string? ClosedColumn);

    private static CoreShape ShapeOf(CoreRecordKind kind) => kind switch
    {
        CoreRecordKind.Matter => new("matter", MatterEntity, "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_openeddate", "sprk_closeddate"),
        CoreRecordKind.Project => new("project", ProjectEntity, "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_openeddate", "sprk_closeddate"),
        CoreRecordKind.WorkAssignment => new("workAssignment", WorkAssignmentEntity, "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown core record kind."),
    };

    private async Task<ContextSection<CoreRecordContext>> LoadCoreRecordAsync(InvoiceScope scope, CancellationToken ct)
    {
        if (scope.Core is not { } core)
        {
            return ContextSection<CoreRecordContext>.Unavailable(scope.CoreProblemCode!, CoreProblemMessage(scope.CoreProblemCode!));
        }

        var shape = ShapeOf(core.Kind);
        var columns = new List<string> { shape.NameColumn, shape.NumberColumn, "statuscode" };
        if (shape.OpenedColumn is not null)
        {
            columns.Add(shape.OpenedColumn);
            columns.Add(shape.ClosedColumn!);
        }

        var row = await ReadByIdAsync(shape.Entity, shape.IdColumn, core.Id, columns.ToArray(), ct);
        if (row is null)
        {
            return ContextSection<CoreRecordContext>.Unavailable(SectionCoreRecord + ".unavailable", "The core record could not be found.");
        }

        var status = row.GetAttributeValue<OptionSetValue>("statuscode");
        return ContextSection<CoreRecordContext>.Available(new CoreRecordContext(
            shape.Type,
            core.Id,
            row.GetAttributeValue<string>(shape.NameColumn),
            row.GetAttributeValue<string>(shape.NumberColumn),
            status?.Value,
            row.FormattedValues.TryGetValue("statuscode", out var label) ? label : null,
            shape.OpenedColumn is null ? null : ToDate(row.GetAttributeValue<DateTime?>(shape.OpenedColumn)),
            shape.ClosedColumn is null ? null : ToDate(row.GetAttributeValue<DateTime?>(shape.ClosedColumn))));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Budget (the rollups, as stored)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<ContextSection<BudgetContext>> LoadBudgetAsync(InvoiceScope scope, CancellationToken ct)
    {
        if (scope.Core is not { } core)
        {
            return ContextSection<BudgetContext>.Unavailable(scope.CoreProblemCode!, CoreProblemMessage(scope.CoreProblemCode!));
        }

        if (core.Kind == CoreRecordKind.WorkAssignment)
        {
            // FinanceRollupService writes budget fields for Matter and Project only. Not an error.
            return ContextSection<BudgetContext>.NotTracked(CodeBudgetNotTracked, "Budget is not tracked for work assignments.");
        }

        var shape = ShapeOf(core.Kind);
        var row = await ReadByIdAsync(
            shape.Entity, shape.IdColumn, core.Id,
            ["sprk_totalbudget", "sprk_totalspendtodate", "sprk_remainingbudget", "sprk_budgetutilizationpercent", "sprk_invoicecount", "modifiedon"],
            ct);
        if (row is null)
        {
            return ContextSection<BudgetContext>.Unavailable(SectionBudget + ".unavailable", "The core record could not be found.");
        }

        var budget = row.GetAttributeValue<Money>("sprk_totalbudget")?.Value;
        var spend = row.GetAttributeValue<Money>("sprk_totalspendtodate")?.Value;
        var remaining = row.GetAttributeValue<Money>("sprk_remainingbudget")?.Value;
        var utilization = row.GetAttributeValue<decimal?>("sprk_budgetutilizationpercent");
        var count = row.GetAttributeValue<int?>("sprk_invoicecount");

        if (budget is null && spend is null && remaining is null && utilization is null && count is null)
        {
            return ContextSection<BudgetContext>.Unavailable(
                CodeBudgetNotPopulated, "Budget and spend have not been calculated for this record yet.");
        }

        return ContextSection<BudgetContext>.Available(new BudgetContext(
            budget, spend, remaining, utilization, count, row.GetAttributeValue<DateTime?>("modifiedon")));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Other invoices from this firm on this core record
    // ═══════════════════════════════════════════════════════════════════════════

    private static string InvoiceCoreColumn(CoreRecordKind kind) => kind switch
    {
        CoreRecordKind.Matter => InvoiceMatter,
        CoreRecordKind.Project => InvoiceProject,
        CoreRecordKind.WorkAssignment => InvoiceWorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown core record kind."),
    };

    /// <summary>Adds "this firm, this core record, not this invoice" to <paramref name="filter"/> (an invoice filter or an invoice link's criteria).</summary>
    private static void AddInvoiceScope(FilterExpression filter, InvoiceScope scope, Guid firmId, CoreRecordReference core)
    {
        filter.AddCondition(InvoiceFirm, ConditionOperator.Equal, firmId);
        filter.AddCondition(InvoiceCoreColumn(core.Kind), ConditionOperator.Equal, core.Id);
        filter.AddCondition("sprk_invoiceid", ConditionOperator.NotEqual, scope.InvoiceId);
    }

    /// <summary>The firm and core record every history section needs, or the unavailable section to return when either is missing.</summary>
    private static bool TryRequireFirmAndCore<T>(
        InvoiceScope scope, out Guid firmId, out CoreRecordReference core, out ContextSection<T>? unavailable) where T : class
    {
        firmId = Guid.Empty;
        core = null!;
        unavailable = null;

        if (scope.Core is not { } resolvedCore)
        {
            unavailable = ContextSection<T>.Unavailable(scope.CoreProblemCode!, CoreProblemMessage(scope.CoreProblemCode!));
            return false;
        }

        if (scope.FirmId is not { } resolvedFirm)
        {
            unavailable = ContextSection<T>.Unavailable(CodeFirmUnresolved, "This invoice has no firm, so the firm's history cannot be shown.");
            return false;
        }

        firmId = resolvedFirm;
        core = resolvedCore;
        return true;
    }

    private async Task<ContextSection<PriorInvoicesContext>> LoadPriorInvoicesAsync(InvoiceScope scope, CancellationToken ct)
    {
        if (!TryRequireFirmAndCore<PriorInvoicesContext>(scope, out var firmId, out var core, out var unavailable))
        {
            return unavailable!;
        }

        var query = new QueryExpression(InvoiceTotalsService.InvoiceEntity)
        {
            ColumnSet = new ColumnSet("sprk_invoicenumber", "sprk_invoicedate", InvoiceTotalsService.InvoiceBilledTotal, InvoiceTotalsService.InvoiceApprovedTotal, "statuscode"),
            TopCount = PriorInvoiceLimit + 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        AddInvoiceScope(query.Criteria, scope, firmId, core);

        // Most recent first; the id is the tiebreak so equal dates keep one order.
        query.AddOrder("sprk_invoicedate", OrderType.Descending);
        query.AddOrder("createdon", OrderType.Descending);
        query.AddOrder("sprk_invoiceid", OrderType.Ascending);

        var page = await _entities.RetrieveMultipleAsync(query, ct);
        var items = page.Entities
            .Take(PriorInvoiceLimit)
            .Select(row =>
            {
                var statusCode = row.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 0;
                return new PriorInvoiceItem(
                    row.Id,
                    row.GetAttributeValue<string>("sprk_invoicenumber"),
                    ToDate(row.GetAttributeValue<DateTime?>("sprk_invoicedate")),
                    row.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceBilledTotal)?.Value,
                    row.GetAttributeValue<Money>(InvoiceTotalsService.InvoiceApprovedTotal)?.Value,
                    InvoiceStatusCodes.TryFromStatusCode(statusCode, out var status) ? status.ToString() : "Unknown",
                    statusCode);
            })
            .ToList();

        return ContextSection<PriorInvoicesContext>.Available(
            new PriorInvoicesContext(PriorInvoiceLimit, page.Entities.Count > PriorInvoiceLimit, items));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Disallow history
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<ContextSection<DisallowHistoryContext>> LoadDisallowHistoryAsync(InvoiceScope scope, CancellationToken ct)
    {
        if (!TryRequireFirmAndCore<DisallowHistoryContext>(scope, out var firmId, out var core, out var unavailable))
        {
            return unavailable!;
        }

        var query = new QueryExpression(InvoiceTotalsService.LineEntity)
        {
            ColumnSet = new ColumnSet(
                InvoiceTotalsService.LineInvoiceLookup, InvoiceTotalsService.LineStatus, InvoiceTotalsService.LineAmount,
                InvoiceTotalsService.LineApprovedAmount, LineAdjustmentReason),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition(InvoiceTotalsService.LineStatus, ConditionOperator.In, new object[] { (int)InvoiceLineStatus.Reduced, (int)InvoiceLineStatus.Disallowed });
        query.Criteria.AddCondition(InvoiceTotalsService.LineCostType, ConditionOperator.In, new object[] { (int)InvoiceLineCostType.Fee, (int)InvoiceLineCostType.Expense });
        query.AddOrder("sprk_billingeventid", OrderType.Ascending);

        var invoice = query.AddLink(InvoiceTotalsService.InvoiceEntity, InvoiceTotalsService.LineInvoiceLookup, "sprk_invoiceid", JoinOperator.Inner);
        invoice.LinkCriteria = new FilterExpression(LogicalOperator.And);
        AddInvoiceScope(invoice.LinkCriteria, scope, firmId, core);
        invoice.LinkCriteria.AddCondition("statuscode", ConditionOperator.In, StandingInvoiceStatuses.Cast<object>().ToArray());

        var (rows, complete) = await ReadPagedAsync(query, ct);
        if (!complete)
        {
            return ContextSection<DisallowHistoryContext>.Unavailable(CodeDisallowTooLarge, "This firm's disallow history is too large to summarise here.");
        }

        var reduced = 0;
        var reducedAmount = 0m;
        var disallowed = 0;
        var disallowedAmount = 0m;
        var invoices = new HashSet<Guid>();
        var reasons = new Dictionary<string, (int Count, string Display)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            if (row.GetAttributeValue<EntityReference>(InvoiceTotalsService.LineInvoiceLookup) is { } owner)
            {
                invoices.Add(owner.Id);
            }

            var billed = row.GetAttributeValue<Money>(InvoiceTotalsService.LineAmount)?.Value ?? 0m;
            switch ((InvoiceLineStatus?)row.GetAttributeValue<OptionSetValue>(InvoiceTotalsService.LineStatus)?.Value)
            {
                case InvoiceLineStatus.Reduced:
                    reduced++;
                    // Billed minus approved, as InvoiceTotalsCalculator defines a Reduced line's adjustment; a line with no approved amount adds none.
                    reducedAmount += row.GetAttributeValue<Money>(InvoiceTotalsService.LineApprovedAmount) is { } approved
                        ? Math.Max(billed - approved.Value, 0m)
                        : 0m;
                    break;
                case InvoiceLineStatus.Disallowed:
                    disallowed++;
                    disallowedAmount += billed;
                    break;
                default:
                    continue;
            }

            var reason = NormalizeReason(row.GetAttributeValue<string>(LineAdjustmentReason));
            if (reason is not null)
            {
                reasons[reason] = reasons.TryGetValue(reason, out var seen)
                    ? (seen.Count + 1, string.CompareOrdinal(reason, seen.Display) < 0 ? reason : seen.Display)
                    : (1, reason);
            }
        }

        var top = reasons.Values
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.Display, StringComparer.Ordinal)
            .Take(TopReasonLimit)
            .Select(r => new DisallowReason(r.Display, r.Count))
            .ToList();

        return ContextSection<DisallowHistoryContext>.Available(
            new DisallowHistoryContext(invoices.Count, reduced, reducedAmount, disallowed, disallowedAmount, top));
    }

    /// <summary>Trimmed, whitespace-collapsed, capped; null for an empty reason. Grouping is case-insensitive, the display is the ordinal-smallest spelling.</summary>
    internal static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var collapsed = string.Join(' ', reason.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length > MaxReasonLength ? collapsed[..MaxReasonLength] : collapsed;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Rate history
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record FeeLine(Guid? TimekeeperId, string? AsBilled, decimal? Rate, DateOnly? Date);

    private async Task<ContextSection<RateHistoryContext>> LoadRateHistoryAsync(InvoiceScope scope, CancellationToken ct)
    {
        if (!TryRequireFirmAndCore<RateHistoryContext>(scope, out var firmId, out var core, out var unavailable))
        {
            return unavailable!;
        }

        // 1. The fee lines of THIS invoice: who billed, at what rate.
        var lineQuery = new QueryExpression(InvoiceTotalsService.LineEntity)
        {
            ColumnSet = new ColumnSet(LineResolvedTimekeeper, LineTimekeeperAsBilled, LineRate, LineEventDate),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        lineQuery.Criteria.AddCondition(InvoiceTotalsService.LineInvoiceLookup, ConditionOperator.Equal, scope.InvoiceId);
        lineQuery.Criteria.AddCondition(InvoiceTotalsService.LineCostType, ConditionOperator.Equal, (int)InvoiceLineCostType.Fee);
        lineQuery.AddOrder("sprk_billingeventid", OrderType.Ascending);

        var (lineRows, linesComplete) = await ReadPagedAsync(lineQuery, ct);
        if (!linesComplete)
        {
            return ContextSection<RateHistoryContext>.Unavailable(CodeRateTooLarge, "This invoice is too large to summarise its rate history.");
        }

        var lines = lineRows
            .Select(r => new FeeLine(
                r.GetAttributeValue<EntityReference>(LineResolvedTimekeeper)?.Id,
                r.GetAttributeValue<string>(LineTimekeeperAsBilled),
                r.GetAttributeValue<Money>(LineRate)?.Value,
                ToDate(r.GetAttributeValue<DateTime?>(LineEventDate))))
            .ToList();

        var timekeeperIds = lines.Where(l => l.TimekeeperId is not null).Select(l => l.TimekeeperId!.Value).Distinct().OrderBy(id => id).ToList();
        if (timekeeperIds.Count > MaxTimekeepers)
        {
            return ContextSection<RateHistoryContext>.Unavailable(CodeRateTooLarge, "This invoice names too many timekeepers to summarise their rate history.");
        }

        // 2. In parallel, for the roster-matched timekeepers only: the roster rows, the firm's approved rates, the rates billed before.
        var roster = new Dictionary<Guid, (string? Name, int? Classification)>();
        var rateRows = new List<Entity>();
        var previousLines = new List<Entity>();
        if (timekeeperIds.Count > 0)
        {
            var rosterTask = ReadRosterAsync(timekeeperIds, ct);
            var ratesTask = ReadApprovedRatesAsync(firmId, core, timekeeperIds, ct);
            var previousTask = ReadPreviousLinesAsync(scope, firmId, core, timekeeperIds, ct);
            await Task.WhenAll(rosterTask, ratesTask, previousTask);

            roster = await rosterTask;
            var (rates, ratesComplete) = await ratesTask;
            if (!ratesComplete)
            {
                return ContextSection<RateHistoryContext>.Unavailable(CodeRateTooLarge, "This firm's rate cards are too large to summarise its rate history.");
            }

            rateRows = rates;
            var (previous, previousComplete) = await previousTask;
            if (!previousComplete)
            {
                return ContextSection<RateHistoryContext>.Unavailable(CodeRateTooLarge, "This firm's billing history is too large to summarise its rate history.");
            }

            previousLines = previous;
        }

        // 3. Assemble: one entry per roster timekeeper, then one per unmatched billed name. Ordered by name, then id.
        var entries = new List<TimekeeperRateHistory>();
        foreach (var id in timekeeperIds)
        {
            roster.TryGetValue(id, out var person);
            var onInvoice = lines.Where(l => l.TimekeeperId == id).ToList();
            var name = person.Name ?? onInvoice.Select(l => l.AsBilled).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)) ?? string.Empty;

            entries.Add(new TimekeeperRateHistory(
                id,
                name,
                ApprovedRatesFor(id, person.Classification, rateRows),
                BilledRates(onInvoice.Select(l => (l.Rate, l.Date))),
                BilledRates(previousLines
                    .Where(r => r.GetAttributeValue<EntityReference>(LineResolvedTimekeeper)?.Id == id)
                    .Select(r => (r.GetAttributeValue<Money>(LineRate)?.Value,
                        ToDate(r.GetAttributeValue<DateTime?>(LineEventDate)))))));
        }

        foreach (var group in lines
                     .Where(l => l.TimekeeperId is null && !string.IsNullOrWhiteSpace(l.AsBilled))
                     .Select(l => (Name: string.Join(' ', l.AsBilled!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), Line: l))
                     .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            // The ordinal-smallest spelling, so the display name does not depend on the order Dataverse returned the lines in.
            var name = group.Select(x => x.Name).OrderBy(n => n, StringComparer.Ordinal).First();
            entries.Add(new TimekeeperRateHistory(null, name, [], BilledRates(group.Select(x => (x.Line.Rate, x.Line.Date))), []));
        }

        var ordered = entries
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.TimekeeperId)
            .ToList();

        return ContextSection<RateHistoryContext>.Available(new RateHistoryContext(ordered));
    }

    private async Task<Dictionary<Guid, (string? Name, int? Classification)>> ReadRosterAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var query = new QueryExpression(TimekeeperEntity)
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_classification"),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition("sprk_timekeeperid", ConditionOperator.In, ids.Cast<object>().ToArray());
        query.AddOrder("sprk_timekeeperid", OrderType.Ascending);

        var (rows, _) = await ReadPagedAsync(query, ct);
        return rows.ToDictionary(
            r => r.Id,
            r => (Name: r.GetAttributeValue<string?>("sprk_name"), Classification: r.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value));
    }

    private async Task<(List<Entity> Rows, bool Complete)> ReadApprovedRatesAsync(Guid firmId, CoreRecordReference core, IReadOnlyList<Guid> timekeeperIds, CancellationToken ct)
    {
        // Task 046's query: active rates of the firm's Published cards, firm-wide cards plus cards scoped to this core record. All periods.
        var query = InvoiceResolutionService.BuildRateQuery(firmId, core, AllPeriodsFrom, AllPeriodsTo);

        // This invoice's timekeepers' own rates, plus every classification rate (no timekeeper) of those cards: which classification applies to
        // which person needs the roster, which is read in parallel with this, so the narrowing happens per person in ApprovedRatesFor.
        var either = new FilterExpression(LogicalOperator.Or);
        either.AddCondition("sprk_timekeeper", ConditionOperator.In, timekeeperIds.Cast<object>().ToArray());
        either.AddCondition("sprk_timekeeper", ConditionOperator.Null);
        query.Criteria.AddFilter(either);

        return await ReadPagedAsync(query, ct);
    }

    private async Task<(List<Entity> Rows, bool Complete)> ReadPreviousLinesAsync(
        InvoiceScope scope, Guid firmId, CoreRecordReference core, IReadOnlyList<Guid> timekeeperIds, CancellationToken ct)
    {
        var query = new QueryExpression(InvoiceTotalsService.LineEntity)
        {
            ColumnSet = new ColumnSet(LineResolvedTimekeeper, LineRate, LineEventDate),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition(LineResolvedTimekeeper, ConditionOperator.In, timekeeperIds.Cast<object>().ToArray());
        query.Criteria.AddCondition(InvoiceTotalsService.LineCostType, ConditionOperator.Equal, (int)InvoiceLineCostType.Fee);
        query.AddOrder("sprk_billingeventid", OrderType.Ascending);

        var invoice = query.AddLink(InvoiceTotalsService.InvoiceEntity, InvoiceTotalsService.LineInvoiceLookup, "sprk_invoiceid", JoinOperator.Inner);
        invoice.LinkCriteria = new FilterExpression(LogicalOperator.And);
        AddInvoiceScope(invoice.LinkCriteria, scope, firmId, core);

        return await ReadPagedAsync(query, ct);
    }

    private static IReadOnlyList<ApprovedRateItem> ApprovedRatesFor(Guid timekeeperId, int? classification, IEnumerable<Entity> rateRows)
    {
        var items = new List<(ApprovedRateItem Item, Guid RateId)>();
        foreach (var row in rateRows)
        {
            var rowTimekeeper = row.GetAttributeValue<EntityReference>("sprk_timekeeper")?.Id;
            string basis;
            if (rowTimekeeper == timekeeperId)
            {
                basis = "timekeeper";
            }
            else if (rowTimekeeper is null && classification is not null
                     && row.GetAttributeValue<OptionSetValue>("sprk_classification")?.Value == classification)
            {
                basis = "classification";
            }
            else
            {
                continue;
            }

            if (row.GetAttributeValue<Money>("sprk_approvedrate")?.Value is not { } approved)
            {
                continue;
            }

            var coreScoped = new[] { "sprk_matter", "sprk_project", "sprk_workassignment" }
                .Any(c => row.GetAttributeValue<AliasedValue>($"{InvoiceResolutionService.CardAlias}.{c}")?.Value is not null);

            items.Add((new ApprovedRateItem(
                ToDate(row.GetAttributeValue<DateTime?>("sprk_effectivefrom")),
                ToDate(row.GetAttributeValue<DateTime?>("sprk_effectiveto")),
                approved,
                row.GetAttributeValue<Money>("sprk_rackrate")?.Value,
                basis,
                coreScoped ? "coreRecord" : "firm"), row.Id));
        }

        // Newest period first (open-ended from-dates last); the row id makes the order total.
        return items
            .OrderByDescending(i => i.Item.EffectiveFrom ?? DateOnly.MinValue)
            .ThenBy(i => i.Item.Basis, StringComparer.Ordinal)
            .ThenBy(i => i.Item.Scope, StringComparer.Ordinal)
            .ThenBy(i => i.RateId)
            .Select(i => i.Item)
            .ToList();
    }

    private static IReadOnlyList<BilledRateItem> BilledRates(IEnumerable<(decimal? Rate, DateOnly? Date)> lines) => lines
        .Where(l => l.Rate is not null)
        .GroupBy(l => l.Rate!.Value)
        .Select(g => new BilledRateItem(
            g.Key,
            g.Count(),
            g.Where(l => l.Date is not null).Select(l => l.Date).Min(),
            g.Where(l => l.Date is not null).Select(l => l.Date).Max()))
        .OrderByDescending(b => b.LastSeen ?? DateOnly.MinValue)
        .ThenBy(b => b.Rate)
        .ToList();

    // ═══════════════════════════════════════════════════════════════════════════
    // Reads
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task<Entity?> ReadByIdAsync(string entity, string idColumn, Guid id, string[] columns, CancellationToken ct)
    {
        var query = new QueryExpression(entity)
        {
            ColumnSet = new ColumnSet(columns),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And),
        };
        query.Criteria.AddCondition(idColumn, ConditionOperator.Equal, id);

        var page = await _entities.RetrieveMultipleAsync(query, ct);
        return page.Entities.FirstOrDefault();
    }

    /// <summary>Reads pages until done or <see cref="MaxPages"/> is reached; <c>Complete</c> is false when rows remained.</summary>
    private async Task<(List<Entity> Rows, bool Complete)> ReadPagedAsync(QueryExpression query, CancellationToken ct)
    {
        var rows = new List<Entity>();
        for (var pages = 1; ; pages++)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct);
            rows.AddRange(page.Entities);

            if (!page.MoreRecords)
            {
                return (rows, true);
            }

            if (pages >= MaxPages)
            {
                return (rows, false);
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    private static DateOnly? ToDate(DateTime? value) => value is { } v ? DateOnly.FromDateTime(v) : null;
}
