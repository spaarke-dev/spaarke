using System.Data;
using System.Globalization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The server-side owner of an invoice's totals (FR-07, I-1, D-42; ADR-002 one owner per invariant). Callers (the submission job, line decisions,
/// adjustments, approval) call it after EVERY line or adjustment change. There is deliberately no overload that takes a total.
/// </summary>
public interface IInvoiceTotalsService
{
    /// <summary>
    /// Re-reads the invoice and its lines, computes the totals with <see cref="InvoiceTotalsCalculator"/> and stores them.
    /// A refused calculation (<see cref="InvoiceTotalsCalculation.IsRefused"/>, e.g. a negative approved total, FR-32) writes NOTHING.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The invoice does not exist.</exception>
    /// <exception cref="DBConcurrencyException">The invoice kept changing for <see cref="InvoiceTotalsService.MaxWriteAttempts"/> attempts; nothing was committed and the caller may retry.</exception>
    Task<InvoiceTotalsCalculation> RecalculateAsync(Guid invoiceId, CancellationToken ct = default);
}

/// <summary>
/// <see cref="IInvoiceTotalsService"/> over Dataverse. Reads <c>sprk_invoice</c> (billed total + <c>versionnumber</c>) and the invoice's
/// <c>sprk_billingevent</c> lines, then writes the Adjustment lines' computed <c>sprk_amount</c> and, as the commit point, the five totals
/// with <c>If-Match</c> on the version read.
/// </summary>
/// <remarks>
/// <para><b>Concurrency.</b> The invoice version is read BEFORE the lines, so a recalculation that committed in between makes this one's
/// conditional write fail (412 -> <see cref="DBConcurrencyException"/>); it then re-reads and recomputes, up to
/// <see cref="MaxWriteAttempts"/> times (the <see cref="InvoiceReviewService"/> retry pattern). Adjustment-line amounts are written first and are
/// idempotent, so a retry or a later recalculation repairs a partial write; the invoice write is the commit point.</para>
/// <para><b>Not covered here.</b> The totals columns are plain columns: a direct Dataverse / model-driven edit is blocked only by form
/// read-only settings (task 029) or a field-security profile, neither built here. Deactivated Fee and Expense lines are counted like active
/// ones: no task defines their deactivation, and silently dropping a row from a money total is the worse default. A deactivated ADJUSTMENT
/// line is not counted (ebilling-r1 task 102, NFR-09: removing an invoice-level adjustment deactivates its row, it never deletes it), so
/// removing one restores the totals; <see cref="ExcludeInactiveAdjustments"/> is the one definition every reader of an invoice's lines uses.</para>
/// </remarks>
public sealed class InvoiceTotalsService : IInvoiceTotalsService
{
    /// <summary>Attempts at the read-compute-write cycle before giving up (same count as <see cref="InvoiceReviewService"/>).</summary>
    public const int MaxWriteAttempts = 3;

    internal const string InvoiceEntity = "sprk_invoice";
    internal const string LineEntity = "sprk_billingevent";

    internal const string InvoiceBilledTotal = "sprk_totalamount";
    internal const string InvoiceFeesTotal = "sprk_feestotal";
    internal const string InvoiceExpensesTotal = "sprk_expensestotal";
    internal const string InvoiceLineAdjustmentsTotal = "sprk_lineadjustmentstotal";
    internal const string InvoiceInvoiceAdjustmentsTotal = "sprk_invoiceadjustmentstotal";
    internal const string InvoiceApprovedTotal = "sprk_approvedtotal";
    internal const string VersionNumber = "versionnumber";

    internal const string LineInvoiceLookup = "sprk_invoice";
    internal const string LineCostType = "sprk_costtype";
    internal const string LineAmount = "sprk_amount";
    internal const string LineApprovedAmount = "sprk_approvedamount";
    internal const string LineStatus = "sprk_linestatus";
    internal const string LineAdjustmentMethod = "sprk_adjustmentmethod";
    internal const string LineAdjustmentValue = "sprk_adjustmentvalue";
    internal const string LineState = "statecode";

    /// <summary><c>statecode</c> of an active row.</summary>
    internal const int StateActive = 0;

    private const int PageSize = 5000;

    private readonly IFieldMappingDataverseService _records;
    private readonly IGenericEntityService _entities;
    private readonly ILogger<InvoiceTotalsService> _logger;

    public InvoiceTotalsService(
        IFieldMappingDataverseService records,
        IGenericEntityService entities,
        ILogger<InvoiceTotalsService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<InvoiceTotalsCalculation> RecalculateAsync(Guid invoiceId, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Version first, lines second (see the class remarks).
            var invoice = await _records.RetrieveRecordFieldsAsync(
                InvoiceEntity, invoiceId, new[] { InvoiceBilledTotal, VersionNumber }, ct);

            if (invoice.Count == 0)
            {
                throw new KeyNotFoundException("Invoice was not found.");
            }

            var version = ReadVersion(invoice);
            var billedTotal = ReadMoney(invoice, InvoiceBilledTotal);
            var lines = await ReadLinesAsync(invoiceId, ct);

            var calculation = InvoiceTotalsCalculator.Calculate(billedTotal, lines);

            if (calculation.IsRefused)
            {
                _logger.LogWarning(
                    "Invoice {InvoiceId} totals refused ({ReasonCode}, line {LineId}); nothing was written",
                    invoiceId, calculation.Refusal!.Code, calculation.Refusal.LineId);
                return calculation;
            }

            if (lines.Count > 0 && billedTotal != calculation.FeesTotal + calculation.ExpensesTotal)
            {
                // I-1 uses the stored billed total. A difference (e.g. a firm-side discount that is not a line) is legitimate input, but it
                // is the one thing that would make the approved total surprising, so it is logged for support.
                _logger.LogWarning(
                    "Invoice {InvoiceId} billed total {Billed} differs from fees + expenses {LinesTotal}; the approved total is based on the billed total",
                    invoiceId,
                    billedTotal!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                    (calculation.FeesTotal + calculation.ExpensesTotal).ToString("0.00", CultureInfo.InvariantCulture));
            }

            try
            {
                foreach (var line in lines.Where(l => l.CostType == InvoiceLineCostType.Adjustment))
                {
                    var computed = calculation.AdjustmentLineAmounts[line.Id];
                    if (computed != line.Amount)
                    {
                        await _records.UpdateExistingRecordFieldsAsync(
                            LineEntity, line.Id, new Dictionary<string, object?> { [LineAmount] = computed }, ct);
                    }
                }
            }
            catch (KeyNotFoundException gone)
            {
                // An Adjustment line was deleted after the read: the same as the invoice changing in between. Re-read; this must not
                // surface as "invoice not found" (the interface documents KeyNotFoundException for the invoice only).
                _logger.LogWarning(gone,
                    "An adjustment line of invoice {InvoiceId} disappeared during recalculation (attempt {Attempt} of {MaxAttempts})",
                    invoiceId, attempt, MaxWriteAttempts);

                if (attempt >= MaxWriteAttempts)
                {
                    throw new DBConcurrencyException("The invoice's lines kept changing during recalculation.", gone);
                }

                continue;
            }

            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(
                    InvoiceEntity,
                    invoiceId,
                    new Dictionary<string, object?>
                    {
                        [InvoiceFeesTotal] = calculation.FeesTotal,
                        [InvoiceExpensesTotal] = calculation.ExpensesTotal,
                        [InvoiceLineAdjustmentsTotal] = calculation.LineAdjustmentsTotal,
                        [InvoiceInvoiceAdjustmentsTotal] = calculation.InvoiceAdjustmentsTotal,
                        [InvoiceApprovedTotal] = calculation.ApprovedTotal,
                    },
                    version,
                    ct);
                return calculation;
            }
            catch (DBConcurrencyException changed)
            {
                _logger.LogWarning(changed,
                    "Invoice {InvoiceId} changed while its totals were calculated (attempt {Attempt} of {MaxAttempts})",
                    invoiceId, attempt, MaxWriteAttempts);

                if (attempt >= MaxWriteAttempts)
                {
                    throw;
                }
            }
        }
    }

    private async Task<List<InvoiceLineInput>> ReadLinesAsync(Guid invoiceId, CancellationToken ct)
    {
        var query = new QueryExpression(LineEntity)
        {
            ColumnSet = new ColumnSet(LineCostType, LineAmount, LineApprovedAmount, LineStatus, LineAdjustmentMethod, LineAdjustmentValue, LineState),
            Criteria = new FilterExpression(LogicalOperator.And),
            PageInfo = new PagingInfo { PageNumber = 1, Count = PageSize },
        };
        query.Criteria.AddCondition(LineInvoiceLookup, ConditionOperator.Equal, invoiceId);
        ExcludeInactiveAdjustments(query);
        query.AddOrder($"{LineEntity}id", OrderType.Ascending);

        var rows = new List<InvoiceLineInput>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _entities.RetrieveMultipleAsync(query, ct);
            // Second layer, as 076 and 072 do: a deactivated Adjustment line the query should not have returned is dropped here.
            rows.AddRange(page.Entities.Where(IsCounted).Select(ToRow));

            if (!page.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }
    }

    /// <summary>
    /// NFR-09 / ebilling-r1 task 102: an invoice-level Adjustment line that was removed is deactivated, never deleted, and must no longer count.
    /// Adds "not an Adjustment line, or active" to <paramref name="query"/>'s criteria. A line with no cost type passes (the calculator refuses
    /// it, fail closed, rather than the query hiding it).
    /// </summary>
    internal static void ExcludeInactiveAdjustments(QueryExpression query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notAnInactiveAdjustment = new FilterExpression(LogicalOperator.Or);
        notAnInactiveAdjustment.AddCondition(LineCostType, ConditionOperator.NotEqual, (int)InvoiceLineCostType.Adjustment);
        notAnInactiveAdjustment.AddCondition(LineCostType, ConditionOperator.Null);
        notAnInactiveAdjustment.AddCondition(LineState, ConditionOperator.Equal, StateActive);
        query.Criteria.AddFilter(notAnInactiveAdjustment);
    }

    /// <summary>False only for an Adjustment line whose <c>statecode</c> is not Active. A row without a <c>statecode</c> counts as active.</summary>
    internal static bool IsCounted(Entity line) =>
        line.GetAttributeValue<OptionSetValue>(LineCostType)?.Value != (int)InvoiceLineCostType.Adjustment
        || (line.GetAttributeValue<OptionSetValue>(LineState)?.Value ?? StateActive) == StateActive;

    private static InvoiceLineInput ToRow(Entity line) => new(
        line.Id,
        OptionOf<InvoiceLineCostType>(line, LineCostType),
        line.GetAttributeValue<Money>(LineAmount)?.Value ?? 0m,
        line.GetAttributeValue<Money>(LineApprovedAmount)?.Value,
        OptionOf<InvoiceLineStatus>(line, LineStatus),
        OptionOf<InvoiceAdjustmentMethod>(line, LineAdjustmentMethod),
        line.GetAttributeValue<decimal?>(LineAdjustmentValue));

    /// <summary>The enum for an option-set column, or null for an empty column or a value the enum does not name (the calculator refuses it).</summary>
    private static T? OptionOf<T>(Entity line, string column) where T : struct, Enum =>
        line.GetAttributeValue<OptionSetValue>(column) is { } option && Enum.IsDefined(typeof(T), option.Value)
            ? (T)Enum.ToObject(typeof(T), option.Value)
            : null;

    private static long ReadVersion(Dictionary<string, object?> invoice)
    {
        if (!invoice.TryGetValue(VersionNumber, out var raw)
            || raw is null
            || !long.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            // Without a version the write cannot be conditional; refuse rather than overwrite a change we never saw.
            throw new InvalidOperationException($"The invoice's {VersionNumber} could not be read, so its totals cannot be saved safely. Nothing was saved.");
        }

        return version;
    }

    /// <summary>A Money value as <c>RetrieveRecordFieldsAsync</c> returns it: a <see cref="long"/> for a whole number, a <see cref="double"/>
    /// otherwise. Never converted through a culture-sensitive string (a double rendered as "1500,5" would parse as 15005).</summary>
    private static decimal? ReadMoney(Dictionary<string, object?> row, string column) =>
        !row.TryGetValue(column, out var raw) ? null : raw switch
        {
            decimal d => d,
            double d => (decimal)d,
            long l => l,
            int i => i,
            string s when decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
}
