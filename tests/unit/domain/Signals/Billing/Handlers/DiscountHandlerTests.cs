using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 115, rule type 13 Discount (procedure-and-file-model.md 6.3): a check only, "rate above the agreed discounted rate" (per line).
/// Maintain-class (ADR-038): pure logic, no mocks.
/// </summary>
public class DiscountHandlerTests
{
    // Rack 1000, 10 percent off = 900; with a 2 percent tolerance the limit is 918.
    private static BillingRuleDefinition TenOff => Rule("""{"type":"Discount","discountPercent":10}""");

    private static BillingRuleDefinition TenOffWithTolerance => Rule("""{"type":"Discount","discountPercent":10,"tolerancePercent":2}""");

    private static RateResolution Rack(decimal rack) => Rate(700m) with { RackRate = rack };

    private static BillingLineFacts Billed(int index, decimal rate, decimal rack = 1000m) =>
        Fee(index) with { BilledRate = rate, ApprovedRate = Rack(rack), Amount = rate };

    [Fact]
    public void AFeeLineBilledAboveTheDiscountedRackRate_GivesAFlagFinding()
    {
        var finding = Run(TenOff, Facts(Billed(0, 950m))).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DiscountRateAboveDiscounted);
        finding.RuleType.Should().Be(RuleType.Discount);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().BeNull("a discount rule is a check only: it never adjusts an amount");
        finding.FactValues.Should().Contain("line.rate", "950.00").And.Contain("rackRate", "1000.00")
            .And.Contain("discountedRate", "900.00").And.Contain("discountLimit", "900.00").And.Contain("rule.discountPercent", "10");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.Discount));
        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Rate));
    }

    [Theory]
    [InlineData("900")]
    [InlineData("850")]
    [InlineData("900.004")] // 900.00 to the cent
    public void AFeeLineAtOrBelowTheDiscountedRate_GivesNoFinding(string rate)
    {
        Run(TenOff, Facts(Billed(0, D(rate)))).Should().BeEmpty();
    }

    [Fact]
    public void TheToleranceIsSlackOverTheDiscountedRate()
    {
        Run(TenOffWithTolerance, Facts(Billed(0, 918m))).Should().BeEmpty("900 x 1.02 = 918 holds");
        var finding = Run(TenOffWithTolerance, Facts(Billed(0, 918.01m))).Should().ContainSingle().Subject;

        // Review F2-1: the discounted rate is rack less the discount, no tolerance; the limit the rate is compared with is its own value.
        finding.FactValues.Should().Contain("discountedRate", "900.00").And.Contain("discountLimit", "918.00");
        finding.Value("discountLimit").Value.Should().Be("918.00");
    }

    [Fact]
    public void LinesWithNoResolvedRackRate_GiveOneFlagOnlyNotCheckedFinding_OnTheInvoice_AndTheOtherLinesAreStillChecked()
    {
        var noRack = Billed(0, 5000m) with { ApprovedRate = Rack(1000m) with { RackRate = null } };
        var notFound = Billed(1, 5000m) with { ApprovedRate = new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.NoRateInForce } };
        var unresolved = Billed(2, 5000m) with { ApprovedRate = null };

        var findings = Run(TenOff, Facts(noRack, notFound, unresolved, Billed(3, 950m)));

        findings.Should().HaveCount(2);
        findings.Should().ContainSingle(f => f.ReasonCode == BillingReasonCodes.DiscountRateAboveDiscounted)
            .Which.Subject.Should().Be(BillingSubject.ForLine(3));
        var notChecked = findings.Should().ContainSingle(f => f.ReasonCode == BillingReasonCodes.DiscountNotCheckedNoRackRate).Subject;
        notChecked.Subject.Should().Be(BillingSubject.ForInvoice(), "one per invoice, not one per line");
        notChecked.FactValues.Should().BeEmpty("the reason has its own message, which names no token");
        notChecked.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Rate)).And.Contain(new FieldRef(1, BillingFieldNames.Rate))
            .And.Contain(new FieldRef(2, BillingFieldNames.Rate)).And.NotContain(new FieldRef(3, BillingFieldNames.Rate));
        BillingRuleCatalogue.IsFlagOnlyReason(notChecked.ReasonCode).Should().BeTrue();
        BillingRuleCatalogue.ReasonTemplate(notChecked.ReasonCode).Should().Be(BillingRuleCatalogue.DiscountNotCheckedTemplate);
        BillingRuleCatalogue.ReasonHeadline(notChecked.ReasonCode).Should().Be("Discount not checked: no rack rate");
    }

    [Fact]
    public void ExpenseLines_FeeLinesWithNoRate_AndCreditLines_AreNotJudged()
    {
        var expense = Expense(0) with { BilledRate = 5000m, ApprovedRate = Rack(1000m) };
        var noRate = Billed(1, 5000m) with { BilledRate = null };
        var credit = Billed(2, 5000m) with { Hours = -1m, Amount = -5000m };

        Run(TenOff, Facts(expense, noRate, credit)).Should().BeEmpty();

        // Out of scope with no rack rate either: still nothing, not even "not checked".
        Run(TenOff, Facts(expense with { ApprovedRate = null }, noRate with { ApprovedRate = null }, credit with { ApprovedRate = null }))
            .Should().BeEmpty();
    }

    [Fact]
    public void Discount_AndAddedFee_AreNeverRejectEligible_AndAllowOnlyFlag()
    {
        foreach (var type in new[] { RuleType.Discount, RuleType.AddedFee })
        {
            BillingRuleCatalogue.IsRejectEligible(type).Should().BeFalse();
            BillingRuleCatalogue.AllowedConsequences(type).Should().BeEquivalentTo([BillingConsequence.Flag]);
            BillingRuleCatalogue.RejectCapableReasons(type).Should().BeEmpty();
        }
    }
}
