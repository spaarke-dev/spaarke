using System.Collections.Frozen;
using System.Globalization;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The ONE definition of how a Matter's or Project's spend, budget and budget utilization are computed from their base rows
/// (<c>sprk_invoice</c> and <c>sprk_budget</c>). <see cref="FinanceRollupService"/> uses it to compute what it stores on the parent;
/// the Signal evaluator uses it to judge a Threshold rule on those stored columns from the base rows instead of from the stored
/// copies (task 025b, owner decision D-122). Read-only: nothing here writes.
/// </summary>
/// <remarks>
/// <para><b>Why the evaluator does not read the stored columns.</b> <c>sprk_matter</c> and <c>sprk_project</c> hold
/// <c>sprk_totalspendtodate</c>, <c>sprk_totalbudget</c> and <c>sprk_budgetutilizationpercent</c> as PLAIN stored columns (live
/// <c>spaarkedev1</c> metadata, <c>SourceType</c> 0, read 2026-10-12: not calculated, not rollup). Their only writer is
/// <see cref="FinanceRollupService"/>, which runs on a form subgrid edit or a never-enqueued job, so a stored value can be
/// arbitrarily stale. A rule that asserts "utilization has reached 90 percent" must not rest on a copy nothing keeps current.
/// No nightly mass write is added to refresh them (D-61).</para>
/// <para><b>The rules</b> (copied from <see cref="FinanceRollupService"/>, which now calls this class): spend = the sum of
/// <c>sprk_invoice.sprk_totalamount</c> over the parent's invoices whose status is one of
/// <see cref="CountedInvoiceStatuses"/>; budget = the sum of <c>sprk_budget.sprk_totalbudget</c> over the parent's budgets;
/// utilization = <c>spend / budget * 100</c> (a PERCENT: 100 means fully spent) when budget is above zero, otherwise undefined
/// (<see cref="FinanceRollupService"/> stores 0; the evaluator treats it as a missing input).</para>
/// <para>§11: <i>Existing</i> - the same sums and formula lived privately in <see cref="FinanceRollupService"/>. <i>Extension</i> -
/// extracted, not copied, so there is one formula. <i>Cost of doing nothing</i> - the evaluator would copy it, and a change to the
/// status set or the formula would make a Signal's number disagree with the matter form.</para>
/// </remarks>
public static class FinanceBaseValues
{
    public const string MatterEntity = "sprk_matter";
    public const string ProjectEntity = "sprk_project";

    public const string InvoiceEntity = "sprk_invoice";
    public const string InvoiceTotalAmount = "sprk_totalamount";
    /// <summary>The invoice status column: <c>statuscode</c>, never <c>statecode</c> (Paid is Inactive and still counts). The
    /// retired invoice-status choice column is gone (ebilling-r1 task 015, D-41).</summary>
    public const string InvoiceStatusCodeColumn = "statuscode";

    public const string BudgetEntity = "sprk_budget";
    public const string BudgetTotalBudget = "sprk_totalbudget";

    /// <summary>The parent columns this class derives (same names on <c>sprk_matter</c> and <c>sprk_project</c>).</summary>
    public const string SpendColumn = "sprk_totalspendtodate";
    public const string BudgetColumn = "sprk_totalbudget";
    public const string UtilizationColumn = "sprk_budgetutilizationpercent";

    /// <summary>The invoice <c>statuscode</c> values whose amount counts as spend: Approved, Sent to AP and Paid. In Review is NOT
    /// counted (it is pending, not actual spend), and neither are Rejected, Returned to Firm and Cancelled, so a Signal raised by an
    /// invoice that is later rejected can clear. Owner decision D-103 (ebilling-r1, 2026-10-17; the same decision as ontology-r1
    /// D-166), which supersedes D-98 and replaces ontology D-137 Q24 ("spend counts only Reviewed"). Paid is Inactive and still counts,
    /// which is why the filter is on <c>statuscode</c>. The numbers live once, in <see cref="InvoiceStatusCodes"/>.</summary>
    public static readonly IReadOnlyList<int> CountedInvoiceStatuses = new[]
    {
        InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid,
    };

    /// <summary>The parent column that stores Pending Spend (matter and project; ebilling-r1 owner decision D-104). Written only by
    /// <see cref="FinanceRollupService"/>. It is NOT in <see cref="IsDerived"/> yet: a Threshold cannot name it until ontology-r1
    /// adds it to its authorable columns, so the evaluator never asks <see cref="Needs"/> / <see cref="ValueOf"/> about it.</summary>
    public const string PendingSpendColumn = "sprk_pendingspend";

    /// <summary>The invoice <c>statuscode</c> values whose amount is PENDING spend (D-104): In Review. Pending spend is kept apart
    /// from actual spend (<see cref="CountedInvoiceStatuses"/>, D-103), so the two sets never overlap. A pending invoice that is
    /// HELD (see <see cref="HeldSubmissionConditions"/>) is left out of the sum.</summary>
    public static readonly IReadOnlyList<int> PendingInvoiceStatuses = new[] { InvoiceStatusCodes.InReview };

    /// <summary>The submission table a held invoice is read from (<c>sprk_invoicesubmission.sprk_invoice</c> names the invoice the
    /// submission created, D-101).</summary>
    public const string SubmissionEntity = "sprk_invoicesubmission";
    internal const string SubmissionInvoiceLookup = "sprk_invoice";
    internal const string SubmissionKeyColumn = "sprk_invoicesubmissionid";
    internal const string InvoiceKeyColumn = "sprk_invoiceid";
    internal const string SubmissionStatusColumn = "sprk_processingstatus";
    internal const string SubmissionReasonColumn = "sprk_outcomereasoncode";

    /// <summary>The outcome reason code of a submission blocked by a rule and awaiting Override / Return (<see cref="SubmissionReasonCodes.Blocked"/>).</summary>
    public const string BlockedReasonCode = SubmissionReasonCodes.Blocked;

    /// <summary>
    /// What makes an In Review invoice HELD (owner decisions D-101, D-102, D-104): some submission linked to it
    /// (<c>sprk_invoicesubmission.sprk_invoice</c>) has <c>sprk_processingstatus</c> = Rejected AND <c>sprk_outcomereasoncode</c> =
    /// <see cref="BlockedReasonCode"/> - a blocked internal submission awaiting Override or Return. Override releases the invoice by
    /// setting the submission to Accepted; Return moves the invoice out of In Review. No column is added to the invoice: the hold is
    /// the state the Override / Return flow already keeps on the submission. This is the ONE definition; both pending-spend paths
    /// (the rollup query and the grouped FetchXML) build their exclusion from it.
    /// </summary>
    /// <remarks>
    /// Counted spend adds NO held predicate: a held invoice is In Review, which D-103 does not count. Only pending spend needs it.
    /// The exclusion is an anti-join written as a LEFT OUTER link with these conditions INSIDE the link plus a null check on the
    /// linked key in the where clause. An invoice can have several linked submissions (resubmissions); a plain join would repeat the
    /// invoice's amount once per linked row, but here a linked row exists only when it is a blocking one, and that invoice is then
    /// excluded, so no amount is ever counted twice.
    /// </remarks>
    public static IReadOnlyList<(string Column, object Value)> HeldSubmissionConditions { get; } = new (string, object)[]
    {
        (SubmissionStatusColumn, (int)InvoiceSubmissionStatus.Rejected),
        (SubmissionReasonColumn, BlockedReasonCode),
    };

    /// <summary>The alias of the held-submission link in both pending-spend queries.</summary>
    internal const string HeldAlias = "held";

    /// <summary>Parent ids per grouped query. Matches <c>PredicateCompiler.MaxInListLength</c>: the size of an <c>in</c> list
    /// the Signal code already sends to Dataverse.</summary>
    public const int ParentChunkSize = 200;

    private static readonly FrozenSet<string> DerivedColumns =
        new[] { SpendColumn, BudgetColumn, UtilizationColumn }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>True for a column of a matter or project that is derived here (and so must not be read from its stored copy).</summary>
    public static bool IsDerived(string entity, string column) =>
        entity is MatterEntity or ProjectEntity && DerivedColumns.Contains(column);

    /// <summary>The lookup on <c>sprk_invoice</c> AND on <c>sprk_budget</c> that names the parent: both are called after the
    /// parent table (verified in <c>CoreAncestorResolver</c>: invoice and budget carry <c>sprk_matter</c> and
    /// <c>sprk_project</c>).</summary>
    public static string ParentLookup(string parentEntity) =>
        parentEntity is MatterEntity or ProjectEntity
            ? parentEntity
            : throw new ArgumentOutOfRangeException(nameof(parentEntity), parentEntity, "Only a matter or a project has invoices and budgets.");

    /// <summary>The sum of a Money column over <paramref name="rows"/>; a null amount counts as 0.</summary>
    public static decimal SumMoney(IEnumerable<Entity> rows, string column) =>
        rows.Sum(e => e.GetAttributeValue<Money>(column)?.Value ?? 0m);

    /// <summary><c>spend / budget * 100</c> when <paramref name="budget"/> is above zero; otherwise null (there is no utilization of
    /// a zero or negative budget). Unrounded: this is what <see cref="FinanceRollupService"/> writes (Dataverse rounds it to the
    /// column's precision on store).</summary>
    public static decimal? UtilizationPercent(decimal spend, decimal budget) => budget > 0 ? spend / budget * 100m : null;

    /// <summary>The value <c>sprk_budgetutilizationpercent</c> would hold after a recalculation: the percent rounded to the column's
    /// precision (2, live metadata), the way Dataverse stores it. The evaluator compares and states THIS number, so a Signal never
    /// shows a figure the column could not hold.</summary>
    public static decimal AsStoredPercent(decimal percent) => Math.Round(percent, 2, MidpointRounding.AwayFromZero);

    /// <summary>Which base rows a derived column needs.</summary>
    public static (bool Spend, bool Budget) Needs(string column) => column switch
    {
        SpendColumn => (true, false),
        BudgetColumn => (false, true),
        UtilizationColumn => (true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "Not a derived finance column."),
    };

    /// <summary>The value of a derived column from the parent's spend and budget, or null with the reason it has none.</summary>
    public static decimal? ValueOf(string column, decimal spend, decimal budget, out string? missingReason)
    {
        missingReason = null;
        switch (column)
        {
            case SpendColumn:
                return spend;
            case BudgetColumn:
                return budget;
            case UtilizationColumn:
                if (UtilizationPercent(spend, budget) is { } percent)
                {
                    return AsStoredPercent(percent);
                }

                missingReason = "no budget above zero (budget " + budget.ToString(CultureInfo.InvariantCulture) + "): utilization is undefined";
                return null;
            default:
                throw new ArgumentOutOfRangeException(nameof(column), column, "Not a derived finance column.");
        }
    }

    /// <summary>
    /// The base values of <paramref name="parentIds"/> read with GROUPED aggregate queries: per chunk of
    /// <see cref="ParentChunkSize"/> parents, one query for the invoices (when spend is needed) and one for the budgets (when
    /// budget is needed), never one query per parent. A parent with no rows has spend/budget 0 (a sum of nothing, as the rollup
    /// computes). A Dataverse failure propagates: the caller must treat the value as unknown.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, (decimal Spend, decimal Budget)>> ReadAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds,
        bool needSpend, bool needBudget, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        var result = new Dictionary<Guid, (decimal, decimal)>(parentIds.Count);
        foreach (var chunk in parentIds.Distinct().Chunk(ParentChunkSize))
        {
            var spend = needSpend
                ? ReadGroupedSums(await entities.RetrieveMultipleAsync(
                    new FetchExpression(BuildGroupedSumFetchXml(parentEntity, forInvoices: true, chunk)), ct).ConfigureAwait(false))
                : new Dictionary<Guid, decimal>();
            var budget = needBudget
                ? ReadGroupedSums(await entities.RetrieveMultipleAsync(
                    new FetchExpression(BuildGroupedSumFetchXml(parentEntity, forInvoices: false, chunk)), ct).ConfigureAwait(false))
                : new Dictionary<Guid, decimal>();
            foreach (var id in chunk)
            {
                result[id] = (spend.GetValueOrDefault(id), budget.GetValueOrDefault(id));
            }
        }

        return result;
    }

    /// <summary>
    /// The live PENDING spend (D-104) of <paramref name="parentIds"/>: per chunk of <see cref="ParentChunkSize"/> parents, one
    /// grouped query over the In Review invoices that are not held. A parent with no such invoice has 0. A Dataverse failure or a
    /// result of a shape <see cref="ReadGroupedSums"/> does not know propagates: pending spend is never read as zero by default.
    /// </summary>
    /// <remarks><b>Call it only with an application identity</b> (ontology-r1 review K2, 2026-10-19). The held exclusion is an
    /// outer join to the user-owned <c>sprk_invoicesubmission</c>, and row security applies to the joined rows: a caller who cannot
    /// read the blocking submission would see a held invoice counted as pending (fail open). The ontology evaluator calls it with its
    /// own app identity; there is no user-identity caller.</remarks>
    public static async Task<IReadOnlyDictionary<Guid, decimal>> ReadPendingSpendAsync(
        IGenericEntityService entities, string parentEntity, IReadOnlyCollection<Guid> parentIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(parentIds);
        var result = new Dictionary<Guid, decimal>(parentIds.Count);
        foreach (var chunk in parentIds.Distinct().Chunk(ParentChunkSize))
        {
            var pending = ReadGroupedSums(await entities.RetrieveMultipleAsync(
                new FetchExpression(BuildGroupedPendingSumFetchXml(parentEntity, chunk)), ct).ConfigureAwait(false));
            foreach (var id in chunk)
            {
                result[id] = pending.GetValueOrDefault(id);
            }
        }

        return result;
    }

    internal const string ParentAlias = "parent";
    internal const string SumAlias = "total";

    /// <summary>The grouped aggregate query: the sum of the invoice amounts (counted statuses only) or of the budget totals,
    /// grouped by the parent lookup, over the given parents.</summary>
    internal static string BuildGroupedSumFetchXml(string parentEntity, bool forInvoices, IEnumerable<Guid> parentIds) =>
        BuildGroupedFetchXml(parentEntity, forInvoices ? InvoiceEntity : BudgetEntity, forInvoices ? InvoiceTotalAmount : BudgetTotalBudget,
            parentIds, forInvoices ? CountedInvoiceStatuses : null, excludeHeld: false);

    /// <summary>The grouped aggregate query for PENDING spend (D-104): the sum of the amounts of the parents' invoices whose status
    /// is in <see cref="PendingInvoiceStatuses"/> and that are not held (<see cref="HeldSubmissionConditions"/>), grouped by the
    /// parent lookup.</summary>
    internal static string BuildGroupedPendingSumFetchXml(string parentEntity, IEnumerable<Guid> parentIds) =>
        BuildGroupedFetchXml(parentEntity, InvoiceEntity, InvoiceTotalAmount, parentIds, PendingInvoiceStatuses, excludeHeld: true);

    private static string BuildGroupedFetchXml(
        string parentEntity, string entityName, string sumColumn, IEnumerable<Guid> parentIds, IReadOnlyList<int>? statuses, bool excludeHeld)
    {
        var lookup = ParentLookup(parentEntity);
        var entity = new XElement("entity", new XAttribute("name", entityName),
            new XElement("attribute",
                new XAttribute("name", sumColumn),
                new XAttribute("alias", SumAlias), new XAttribute("aggregate", "sum")),
            new XElement("attribute", new XAttribute("name", lookup), new XAttribute("alias", ParentAlias), new XAttribute("groupby", "true")));
        var filter = new XElement("filter", new XAttribute("type", "and"),
            new XElement("condition", new XAttribute("attribute", lookup), new XAttribute("operator", "in"),
                parentIds.Select(id => new XElement("value", id.ToString("D", CultureInfo.InvariantCulture)))));
        if (statuses is not null)
        {
            filter.Add(new XElement("condition", new XAttribute("attribute", InvoiceStatusCodeColumn), new XAttribute("operator", "in"),
                statuses.Select(s => new XElement("value", s.ToString(CultureInfo.InvariantCulture)))));
        }

        if (excludeHeld)
        {
            // LEFT OUTER link to the blocking submissions, then keep only the invoices that found none (see HeldSubmissionConditions).
            entity.Add(new XElement("link-entity",
                new XAttribute("name", SubmissionEntity), new XAttribute("from", SubmissionInvoiceLookup), new XAttribute("to", InvoiceKeyColumn),
                new XAttribute("link-type", "outer"), new XAttribute("alias", HeldAlias),
                new XElement("filter", new XAttribute("type", "and"),
                    HeldSubmissionConditions.Select(c => new XElement("condition",
                        new XAttribute("attribute", c.Column), new XAttribute("operator", "eq"),
                        new XAttribute("value", Convert.ToString(c.Value, CultureInfo.InvariantCulture)!))))));
            filter.Add(new XElement("condition",
                new XAttribute("entityname", HeldAlias), new XAttribute("attribute", SubmissionKeyColumn), new XAttribute("operator", "null")));
        }

        entity.Add(filter);
        return new XElement("fetch", new XAttribute("aggregate", "true"), entity).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Parent id to summed amount, from the aliased columns of a grouped aggregate result.</summary>
    /// <exception cref="InvalidOperationException">A parent or sum value of a shape this reader does not know. Fails closed: a row
    /// read as "no spend" would let a Threshold Signal close as cleared on a number nobody computed. A NULL sum (a group of rows
    /// whose amounts are all null) is 0, as <see cref="SumMoney"/> treats a null amount; a NULL parent is a row of no parent
    /// (the filter names parents, so it is skipped).</exception>
    internal static Dictionary<Guid, decimal> ReadGroupedSums(EntityCollection rows)
    {
        var sums = new Dictionary<Guid, decimal>();
        foreach (var row in rows.Entities)
        {
            var parentValue = Unwrap(row.Attributes.TryGetValue(ParentAlias, out var p) ? p : null);
            var parent = parentValue switch
            {
                null => Guid.Empty,
                EntityReference reference => reference.Id,
                Guid id => id,
                _ => throw new InvalidOperationException(
                    "Grouped aggregate row has a parent value of unexpected type " + parentValue.GetType().Name + "."),
            };
            if (parent == Guid.Empty)
            {
                continue;
            }

            var sumValue = Unwrap(row.Attributes.TryGetValue(SumAlias, out var s) ? s : null);
            var amount = sumValue switch
            {
                null => 0m,
                Money money => money.Value,
                decimal value => value,
                _ => throw new InvalidOperationException(
                    "Grouped aggregate row has a sum value of unexpected type " + sumValue.GetType().Name + "."),
            };
            sums[parent] = sums.GetValueOrDefault(parent) + amount;
        }

        return sums;
    }

    private static object? Unwrap(object? value) => value is AliasedValue aliased ? aliased.Value : value;
}
