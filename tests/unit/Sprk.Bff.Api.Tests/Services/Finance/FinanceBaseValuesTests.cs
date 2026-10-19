using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// The one definition of spend, budget and utilization (task 025b, owner decision D-122) that <see cref="FinanceRollupService"/>
/// writes and the Signal evaluator reads. These pin the pieces both consumers share, so a change to the status set, the lookups or
/// the formula moves the matter form and the Signals together.
/// </summary>
/// <remarks>Maintain-class (ADR-038): the rollup formula decides what a user is told about their budget.</remarks>
public class FinanceBaseValuesTests
{
    private static readonly Guid MatterA = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988");
    private static readonly Guid MatterB = Guid.Parse("b68299c6-bafb-f011-8407-7c1e520aa4df");

    /// <summary>Both grouped queries counted spend sends (D-130: the approved total, and the billed total where there is none), parsed.</summary>
    private static IReadOnlyList<XElement> CountedSpendFetches(string parent) =>
        FinanceBaseValues.SpendAmountPartsInReadOrder
            .Select(part => XElement.Parse(FinanceBaseValues.BuildGroupedSpendFetchXml(parent, part, new[] { MatterA }))).ToList();

    [Fact]
    public void TheCountedInvoiceStatuses_AreTheThreeStatuscodeValues_ApprovedThroughPaid_AndNotInReview()
    {
        // Owner decision D-103 (= ontology D-166; supersedes D-98): Approved, Sent to AP, Paid - filtered on statuscode (Paid is Inactive).
        // In Review is pending, not actual spend.
        FinanceBaseValues.CountedInvoiceStatuses.Should().Equal(
            InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid);
        FinanceBaseValues.CountedInvoiceStatuses.Should().Equal(659490000, 659490001, 659490002);   // task 012's live values
        FinanceBaseValues.CountedInvoiceStatuses.Should().NotContain(new[]
        {
            InvoiceStatusCodes.InReview, InvoiceStatusCodes.Rejected, InvoiceStatusCodes.ReturnedToFirm, InvoiceStatusCodes.Cancelled,
        });
        FinanceBaseValues.InvoiceStatusCodeColumn.Should().Be("statuscode");
    }

    [Theory]
    [InlineData(InvoiceStatusCodes.InReview, false)]         // D-103: pending, not actual spend (both queries must leave it out)
    [InlineData(InvoiceStatusCodes.Approved, true)]
    [InlineData(InvoiceStatusCodes.SentToAp, true)]
    [InlineData(InvoiceStatusCodes.Paid, true)]              // Inactive (statecode 1) and still counted: the filter is statuscode
    [InlineData(InvoiceStatusCodes.Rejected, false)]
    [InlineData(InvoiceStatusCodes.ReturnedToFirm, false)]
    [InlineData(InvoiceStatusCodes.Cancelled, false)]
    public void AnInvoice_IsCountedAsSpend_ByItsStatuscodeAlone(int statusCode, bool counted)
    {
        // The rollup's query and the evaluator's grouped query admit exactly the counted statuscodes, on statuscode only.
        var rollup = FinanceRollupService.BuildInvoiceQuery(MatterA, "sprk_matter");
        rollup.Criteria.Conditions.Should().NotContain(c => c.AttributeName == "statecode");
        rollup.Criteria.Conditions.Single(c => c.AttributeName == "statuscode").Values.Cast<int>().Contains(statusCode)
            .Should().Be(counted);

        foreach (var grouped in CountedSpendFetches("sprk_matter"))
        {
            var conditions = grouped.Descendants("condition").ToList();
            conditions.Should().NotContain(c => (string)c.Attribute("attribute")! == "statecode");
            conditions.Single(c => (string)c.Attribute("attribute")! == "statuscode").Elements("value").Select(v => int.Parse(v.Value))
                .Contains(statusCode).Should().Be(counted);
        }
    }

    // D-103 (owner; = ontology D-166): In Review is pending, not actual spend. One test per sum path, named for what it proves, so
    // putting InReview back into CountedInvoiceStatuses fails each path on its own (mutation check recorded in notes/015).
    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void TheRollupQuery_FinanceRollupService_DoesNotCountAnInReviewInvoice(string parent)
    {
        var rollup = FinanceRollupService.BuildInvoiceQuery(MatterA, FinanceBaseValues.ParentLookup(parent));

        var admitted = rollup.Criteria.Conditions.Single(c => c.AttributeName == "statuscode").Values.Cast<int>().ToList();
        admitted.Should().NotContain(InvoiceStatusCodes.InReview);
        admitted.Should().BeEquivalentTo(new[] { InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid });
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void TheGroupedSumQuery_FinanceBaseValues_DoesNotCountAnInReviewInvoice(string parent)
    {
        foreach (var grouped in CountedSpendFetches(parent))
        {
            var admitted = grouped.Descendants("condition").Single(c => (string)c.Attribute("attribute")! == "statuscode")
                .Elements("value").Select(v => int.Parse(v.Value)).ToList();
            admitted.Should().NotContain(InvoiceStatusCodes.InReview);
            admitted.Should().BeEquivalentTo(new[] { InvoiceStatusCodes.Approved, InvoiceStatusCodes.SentToAp, InvoiceStatusCodes.Paid });
        }
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    public void TheRollupsInvoiceQuery_AndTheEvaluatorsGroupedQuery_AgreeOnLookupAndStatuses(string parent)
    {
        var rollup = FinanceRollupService.BuildInvoiceQuery(MatterA, FinanceBaseValues.ParentLookup(parent));
        var statusCondition = rollup.Criteria.Conditions.Single(c => c.AttributeName == "statuscode");
        statusCondition.Operator.Should().Be(ConditionOperator.In);

        rollup.Criteria.Conditions.Single(c => c.AttributeName == parent).Operator.Should().Be(ConditionOperator.Equal);
        foreach (var grouped in CountedSpendFetches(parent))
        {
            var conditions = grouped.Element("entity")!.Element("filter")!.Elements("condition").ToList();
            conditions.Single(c => (string)c.Attribute("attribute")! == "statuscode").Elements("value").Select(v => int.Parse(v.Value))
                .Should().Equal(statusCondition.Values.Cast<int>());
            conditions.Should().Contain(c => (string)c.Attribute("attribute")! == parent && (string)c.Attribute("operator")! == "in");
        }
    }

    [Theory]
    [InlineData(FinanceBaseValues.SpendAmountPart.ApprovedTotal, "sprk_approvedtotal", "not-null")]
    [InlineData(FinanceBaseValues.SpendAmountPart.BilledWhereNoApprovedTotal, "sprk_totalamount", "null")]
    public void TheGroupedInvoiceQuery_IsOneAggregateSumGroupedByTheParent_OverTheGivenParentsOnly_ForOneHalfOfTheAmountRule(
        FinanceBaseValues.SpendAmountPart part, string summed, string approvedTotalOperator)
    {
        // D-130: an aggregate cannot coalesce per row, so the amount rule is two grouped sums: the approved total where there is one,
        // the billed total where there is not. Each invoice is in exactly one half (not-null / null on the approved total).
        var fetch = XElement.Parse(FinanceBaseValues.BuildGroupedSpendFetchXml("sprk_project", part, new[] { MatterA, MatterB }));

        fetch.Attribute("aggregate")!.Value.Should().Be("true");
        var entity = fetch.Element("entity")!;
        entity.Attribute("name")!.Value.Should().Be("sprk_invoice");
        entity.Elements("attribute").Select(a => (a.Attribute("name")!.Value, a.Attribute("aggregate")?.Value, a.Attribute("groupby")?.Value))
            .Should().BeEquivalentTo(new (string, string?, string?)[] { (summed, "sum", null), ("sprk_project", null, "true") });
        entity.Descendants("condition").Single(c => c.Attribute("attribute")!.Value == "sprk_project").Elements("value").Select(v => Guid.Parse(v.Value))
            .Should().BeEquivalentTo(new[] { MatterA, MatterB });
        entity.Descendants("condition").Single(c => c.Attribute("attribute")!.Value == "sprk_approvedtotal").Attribute("operator")!.Value
            .Should().Be(approvedTotalOperator);
    }

    [Fact]
    public void TheAmountRule_IsTheApprovedTotalElseTheBilledTotal_AnEmptyApprovedTotalFallsBack_AZeroOneDoesNot()
    {
        static Entity Invoice(decimal? approved, decimal? billed)
        {
            var e = new Entity("sprk_invoice");
            if (approved is { } a) e["sprk_approvedtotal"] = new Money(a);
            if (billed is { } b) e["sprk_totalamount"] = new Money(b);
            return e;
        }

        FinanceBaseValues.SpendAmount(Invoice(80m, 100m)).Should().Be(80m);       // disallowed line: the payable total
        FinanceBaseValues.SpendAmount(Invoice(null, 100m)).Should().Be(100m);     // no approved total: the billed total
        FinanceBaseValues.SpendAmount(Invoice(0m, 100m)).Should().Be(0m);         // everything disallowed: 0 is a value, no fallback
        FinanceBaseValues.SpendAmount(Invoice(-40m, -30m)).Should().Be(-40m);     // credit memo (D-127): signed
        FinanceBaseValues.SpendAmount(Invoice(null, null)).Should().Be(0m);
        FinanceBaseValues.SumSpend(new[] { Invoice(80m, 100m), Invoice(null, 20m), Invoice(-5m, -5m) }).Should().Be(95m);
        FinanceBaseValues.SpendAmountColumns.Should().BeEquivalentTo("sprk_approvedtotal", "sprk_totalamount");
    }

    [Fact]
    public void TheGroupedBudgetQuery_SumsTheBudgetTotals_WithNoStatusFilter()
    {
        var entity = XElement.Parse(FinanceBaseValues.BuildGroupedBudgetFetchXml("sprk_matter", new[] { MatterA })).Element("entity")!;

        entity.Attribute("name")!.Value.Should().Be("sprk_budget");
        entity.Elements("attribute").Select(a => a.Attribute("name")!.Value).Should().BeEquivalentTo("sprk_totalbudget", "sprk_matter");
        entity.Descendants("condition").Select(c => c.Attribute("attribute")!.Value).Should().Equal("sprk_matter");
    }

    [Fact]
    public void ParentLookup_IsDefinedForMatterAndProjectOnly()
    {
        FinanceBaseValues.ParentLookup("sprk_matter").Should().Be("sprk_matter");
        FinanceBaseValues.ParentLookup("sprk_project").Should().Be("sprk_project");
        FluentActions.Invoking(() => FinanceBaseValues.ParentLookup("sprk_event")).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(150000, 250000, 166.6666666666666666666666667)]   // 250000 / 150000 is the #1663 matter: over budget, 166.67 percent
    [InlineData(100, 50, 50)]
    [InlineData(100, 100, 100)]
    [InlineData(100, 0, 0)]
    public void UtilizationPercent_IsSpendOverBudgetTimes100(double budget, double spend, double expected) =>
        FinanceBaseValues.UtilizationPercent((decimal)spend, (decimal)budget)!.Value.Should().BeApproximately((decimal)expected, 0.0000001m);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void UtilizationPercent_OfAZeroOrNegativeBudget_IsUndefined_AndTheRollupStoresZero(int budget)
    {
        FinanceBaseValues.UtilizationPercent(1000m, budget).Should().BeNull();
        (FinanceBaseValues.UtilizationPercent(1000m, budget) ?? 0m).Should().Be(0m, "FinanceRollupService stores 0 here; behavior unchanged");
    }

    [Fact]
    public void ValueOf_UtilizationRoundsToTheColumnsTwoDecimals_AndSpendAndBudgetPassThrough()
    {
        FinanceBaseValues.ValueOf("sprk_budgetutilizationpercent", 84.996m, 100m, out var reason).Should().Be(85.00m);
        reason.Should().BeNull();
        FinanceBaseValues.ValueOf("sprk_budgetutilizationpercent", 1m, 3m, out _).Should().Be(33.33m);
        FinanceBaseValues.ValueOf("sprk_totalspendtodate", 12.5m, 0m, out _).Should().Be(12.5m);
        FinanceBaseValues.ValueOf("sprk_totalbudget", 12.5m, 7m, out _).Should().Be(7m);
        FinanceBaseValues.ValueOf("sprk_budgetutilizationpercent", 5m, 0m, out var why).Should().BeNull();
        why.Should().Contain("no budget above zero");
    }

    [Fact]
    public void SumMoney_AddsTheMoneyColumn_AndTreatsANullAsZero()
    {
        var rows = new[]
        {
            new Entity("sprk_invoice") { ["sprk_totalamount"] = new Money(10.25m) },
            new Entity("sprk_invoice") { ["sprk_totalamount"] = new Money(4.75m) },
            new Entity("sprk_invoice"),
        };

        FinanceBaseValues.SumMoney(rows, "sprk_totalamount").Should().Be(15m);
    }

    [Fact]
    public void ReadGroupedSums_ReadsTheAliasedParentAndMoneyTotal()
    {
        var result = new EntityCollection();
        result.Entities.Add(new Entity("sprk_invoice")
        {
            ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", new EntityReference("sprk_matter", MatterA)),
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", new Money(42m)),
        });

        FinanceBaseValues.ReadGroupedSums(result).Should().Equal(new Dictionary<Guid, decimal> { [MatterA] = 42m });
    }

    [Fact]
    public void ReadGroupedSums_ANullSumIsZero_ANullParentIsSkipped()
    {
        var result = new EntityCollection();
        result.Entities.Add(new Entity("sprk_invoice")
        {
            ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", new EntityReference("sprk_matter", MatterA)),
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", null),
        });
        result.Entities.Add(new Entity("sprk_invoice")
        {
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", new Money(9m)),
        });

        FinanceBaseValues.ReadGroupedSums(result).Should().Equal(new Dictionary<Guid, decimal> { [MatterA] = 0m });
    }

    [Fact]
    public void ReadGroupedSums_AParentOfAnUnexpectedShape_Throws_NotZeroSpend()
    {
        var result = new EntityCollection();
        result.Entities.Add(new Entity("sprk_invoice")
        {
            ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", "not-a-guid"),
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", new Money(42m)),
        });

        FluentActions.Invoking(() => FinanceBaseValues.ReadGroupedSums(result)).Should().Throw<InvalidOperationException>()
            .WithMessage("*parent*unexpected type*String*");
    }

    [Fact]
    public void ReadGroupedSums_ASumOfAnUnexpectedShape_Throws_NotZeroSpend()
    {
        var result = new EntityCollection();
        result.Entities.Add(new Entity("sprk_invoice")
        {
            ["parent"] = new AliasedValue("sprk_invoice", "sprk_matter", new EntityReference("sprk_matter", MatterA)),
            ["total"] = new AliasedValue("sprk_invoice", "sprk_totalamount", "42"),
        });

        FluentActions.Invoking(() => FinanceBaseValues.ReadGroupedSums(result)).Should().Throw<InvalidOperationException>()
            .WithMessage("*sum*unexpected type*String*");
    }

    [Theory]
    [InlineData("sprk_matter", "sprk_totalspendtodate", true)]
    [InlineData("sprk_project", "sprk_totalbudget", true)]
    [InlineData("sprk_project", "sprk_budgetutilizationpercent", true)]
    [InlineData("sprk_event", "sprk_totalspendtodate", false)]
    [InlineData("sprk_matter", "sprk_mattername", false)]
    public void IsDerived_NamesTheThreeColumnsOnMatterAndProject(string entity, string column, bool derived) =>
        FinanceBaseValues.IsDerived(entity, column).Should().Be(derived);
}
