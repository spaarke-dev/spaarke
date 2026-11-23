using System.Globalization;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 049, owner decisions D-102 (held invoices do not count toward spend, as amended by D-103), D-104 (Pending Spend: In Review
/// invoices that are not held, kept apart from actual spend) and D-130 (the amount rule). Task 165b-4 retired the two query paths (the rollup's
/// own queries and the grouped FetchXML) for one shared reader per figure, so the behaviour is tested through every reader in
/// <see cref="SpendServerWrittenSourcesTests"/>; this file keeps the shared definitions, the rollup's figures and the column guard.
/// </summary>
/// <remarks>Maintain-class (ADR-038): these decide what a Threshold Signal and the matter form say about a budget.</remarks>
public class SpendHeldAndPendingTests
{
    private static readonly Guid Matter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");

    [Fact(DisplayName = "D-104: the pending set is In Review only, apart from the counted set, and the held column check is one definition")]
    public void D104_PendingStatuses_AndHeldPredicate_AreDefinedOnceInFinanceBaseValues()
    {
        FinanceBaseValues.PendingInvoiceStatuses.Should().Equal(InvoiceStatusCodes.InReview);
        FinanceBaseValues.PendingInvoiceStatuses.Intersect(FinanceBaseValues.CountedInvoiceStatuses).Should().BeEmpty();
        FinanceBaseValues.PendingSpendColumn.Should().Be("sprk_pendingspend");
        FinanceBaseValues.HeldSubmissionConditions
            .Select(c => (c.Column, string.Join(",", c.Values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)!)))).Should().Equal(
                ("sprk_processingstatus", "100000001,100000003"), ("sprk_outcomereasoncode", "ebilling.submission.blocked"));
    }

    [Theory(DisplayName = "D-130: the row reads select both amount columns (a column left out would fall back to the billed total)")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void D130_TheRowReads_SelectBothAmountColumns_AndTheParentLookup(string parent)
    {
        foreach (var statuses in new[] { FinanceBaseValues.CountedInvoiceStatuses, FinanceBaseValues.PendingInvoiceStatuses })
        {
            var query = FinanceBaseValues.InvoiceRowQuery(parent, statuses, [Matter], ["sprk_invoicedate", "statuscode"]);
            query.ColumnSet.AllColumns.Should().BeFalse();
            query.ColumnSet.Columns.Should().Contain(new[] { "sprk_approvedtotal", "sprk_totalamount", parent, "sprk_invoicedate", "statuscode" });
            query.Criteria.Conditions.Single(c => c.AttributeName == "statuscode").Values.Cast<int>().Should().Equal(statuses);
            query.Criteria.Conditions.Single(c => c.AttributeName == parent).Operator.Should().Be(ConditionOperator.In);
            query.Criteria.Conditions.Should().HaveCount(2, "no other filter (in particular no statecode: Paid is Inactive)");
        }
    }

    [Fact(DisplayName = "D-130: the stored figures (total, current month, prior month, timeline, count) all use the approved-else-billed amount")]
    public void D130_TheRollupFigures_UseTheApprovedTotal_ForTotalMonthsTimelineVelocityAndAverage()
    {
        var now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        static Entity Dated(decimal billed, decimal? approved, DateTime date)
        {
            var e = new Entity("sprk_invoice", Guid.NewGuid()) { ["sprk_totalamount"] = new Money(billed), ["sprk_invoicedate"] = date };
            if (approved is { } a) e["sprk_approvedtotal"] = new Money(a);
            return e;
        }

        var invoices = new List<Entity>
        {
            Dated(1000m, 800m, new DateTime(2026, 10, 3)),     // this month, approved 800
            Dated(200m, null, new DateTime(2026, 10, 9)),      // this month, billed 200
            Dated(500m, 400m, new DateTime(2026, 9, 12)),      // last month, approved 400
            Dated(-100m, -100m, new DateTime(2026, 9, 20)),    // last month, credit memo
            Dated(300m, 0m, new DateTime(2026, 8, 1)),         // August, fully disallowed
        };

        var figures = FinanceRollupService.ComputeSpendFigures(invoices, now);

        figures.TotalSpend.Should().Be(1300m);                 // 800 + 200 + 400 - 100 + 0 (billed would be 1900)
        figures.CurrentMonthSpend.Should().Be(1000m);
        figures.PriorMonthSpend.Should().Be(300m);
        figures.InvoiceCount.Should().Be(5);
        figures.MonthlyBuckets["2026-10"].Should().Be(1000m);
        figures.MonthlyBuckets["2026-09"].Should().Be(300m);
        figures.MonthlyBuckets["2026-08"].Should().Be(0m);
        (figures.TotalSpend / figures.InvoiceCount).Should().Be(260m, "the stored average invoice is the total over the count");
        SpendSnapshotService.ComputeVelocityPct(figures.CurrentMonthSpend, figures.PriorMonthSpend)!.Value
            .Should().BeApproximately(233.33m, 0.01m, "(1000 - 300) / 300; on billed totals it would be a different number");
    }

    // ---- K1 (ontology-r1 re-check, 2026-10-19; task 165b-4): every column a spend or budget query touches is guarded ----

    /// <summary>Every (entity, column) a query selects, filters on, orders by or joins on, the linked side included (primary keys excluded).</summary>
    internal static IReadOnlyCollection<(string Entity, string Column)> ReferencedColumns(QueryExpression query)
    {
        var found = new HashSet<(string, string)>();
        void Add(string entity, string? column)
        {
            if (!string.IsNullOrEmpty(column) && column != entity + "id") found.Add((entity, column));
        }

        foreach (var column in query.ColumnSet.Columns) Add(query.EntityName, column);
        foreach (var condition in query.Criteria.Conditions) Add(query.EntityName, condition.AttributeName);
        foreach (var order in query.Orders) Add(query.EntityName, order.AttributeName);
        foreach (var link in query.LinkEntities)
        {
            Add(query.EntityName, link.LinkFromAttributeName);
            Add(link.LinkToEntityName, link.LinkToAttributeName);
            foreach (var column in link.Columns.Columns) Add(link.LinkToEntityName, column);
            foreach (var condition in link.LinkCriteria.Conditions) Add(link.LinkToEntityName, condition.AttributeName);
        }

        return found;
    }

    internal static IReadOnlyCollection<(string Entity, string Column)> ReferencedColumns(string fetchXml)
    {
        var found = new HashSet<(string, string)>();
        var entity = XElement.Parse(fetchXml).Element("entity")!;
        var name = (string)entity.Attribute("name")!;
        foreach (var column in entity.Elements("attribute").Select(a => (string)a.Attribute("name")!).Concat(
                     entity.Descendants("condition").Select(c => (string)c.Attribute("attribute")!)))
        {
            if (column != name + "id") found.Add((name, column));
        }

        return found;
    }

    private static IEnumerable<(string Entity, string Column)> Unlisted(IEnumerable<(string Entity, string Column)> referenced) =>
        referenced.Where(c => !FinanceBaseValues.DerivedBaseColumns.Contains(c) && !FinanceBaseValues.HoldSourceColumns.Contains(c));

    [Fact(DisplayName = "K1: every column the real spend, pending and budget reads touch is a derived base column or a listed hold source")]
    public async Task K1_EveryColumnTheQueriesTouch_IsListed()
    {
        var world = new SpendWorld();
        world.Approved(FinanceBaseValues.MatterEntity, Matter, 10m);
        world.Pending(FinanceBaseValues.MatterEntity, Matter, 5m);
        world.Budget(FinanceBaseValues.MatterEntity, Matter, 1m);
        await FinanceBaseValues.ReadAsync(world.Service().Object, FinanceBaseValues.MatterEntity, [Matter], true, true, SpendWorld.WriterId, CancellationToken.None);
        await FinanceBaseValues.ReadPendingSpendAsync(world.Service().Object, FinanceBaseValues.MatterEntity, [Matter], CancellationToken.None);

        world.Reads.Select(q => q.EntityName).Distinct().Should().BeEquivalentTo("sprk_invoice", "sprk_invoiceapproval", "sprk_decisionrecord", "sprk_invoicesubmission");
        var referenced = world.Reads.SelectMany(ReferencedColumns).Concat(world.Fetches.SelectMany(ReferencedColumns)).Distinct().ToList();

        Unlisted(referenced).Should().BeEmpty("a column the readers use must be guarded for field security (DerivedBaseColumns) or listed as a secured hold source");
        referenced.Should().Contain(FinanceBaseValues.HoldSourceColumns, "the hold batch reads the secured findings");
        FinanceBaseValues.HoldSourceColumns.Should().NotIntersectWith(FinanceBaseValues.DerivedBaseColumns, "a secured hold source is listed apart");
    }

    [Fact(DisplayName = "K1 control: the collector finds selected, filtered, ordered and linked columns, and an unlisted one is flagged")]
    public void K1_TheCollector_FindsEveryKindOfReference_AndFlagsAnUnlistedColumn()
    {
        var query = new QueryExpression("sprk_decisionrecord") { ColumnSet = new ColumnSet("sprk_steps") };
        query.Criteria.AddCondition("sprk_secret", ConditionOperator.Null);
        query.AddOrder("sprk_decisionrecordid", OrderType.Ascending);
        var link = query.AddLink("sprk_invoice", "sprk_regardinginvoice", "sprk_invoiceid", JoinOperator.Inner);
        link.LinkCriteria.AddCondition("sprk_matter", ConditionOperator.In, Matter);

        var referenced = ReferencedColumns(query);

        referenced.Should().BeEquivalentTo(new[]
        {
            ("sprk_decisionrecord", "sprk_steps"), ("sprk_decisionrecord", "sprk_secret"), ("sprk_decisionrecord", "sprk_regardinginvoice"),
            ("sprk_invoice", "sprk_matter"),
        });
        Unlisted(referenced).Should().Equal(("sprk_decisionrecord", "sprk_secret"));
    }
}
