using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 2 ApprovedRate (design.md 6.1). Maintain-class (ADR-038): pure logic, no mocks. The approved
/// rate in force (rate card, precedence, ambiguity) is task 046's; these tests pin the comparison, the tolerance, the suggested
/// reduction and what the rule does when no approved rate could be established.
/// </summary>
public class ApprovedRateHandlerTests
{
    private static BillingRuleDefinition RateRule(string tolerance = "0") => Rule($$"""{"type":"ApprovedRate","tolerancePercent":{{tolerance}}}""");

    private static BillingLineFacts Line(decimal billed, decimal approved = 575m, decimal? hours = 2.5m, int index = 0) =>
        Fee(index) with { BilledRate = billed, ApprovedRate = Rate(approved), Hours = hours };

    // ── The comparison and its boundaries ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustFire_WhenTheBilledRateIsAboveTheApprovedRate_WithTheExcessTimesHoursAsTheSuggestedReduction()
    {
        var finding = Run(RateRule(), Facts(Line(billed: 650m))).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.RateAboveApproved);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Reduce, 187.50m), "(650 - 575) x 2.5");
        finding.Value("line.rate").Value.Should().Be("650.00");
        finding.Value("approvedRate").Value.Should().Be("575.00");
        finding.FactValues.Should().Contain("line.rate", "650.00").And.Contain("approvedRate", "575.00").And.Contain("rule.tolerancePercent", "0");
    }

    [Fact]
    public void MustNotFire_WhenTheBilledRateEqualsTheApprovedRate()
    {
        Run(RateRule(), Facts(Line(billed: 575m))).Should().BeEmpty();
    }

    [Fact]
    public void MustNotFire_WhenTheBilledRateIsBelowTheApprovedRate()
    {
        Run(RateRule(), Facts(Line(billed: 500m))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("5", "603.75", false)] // 575 + 5% = 603.75 exactly: the boundary holds
    [InlineData("5", "603.76", true)] // one cent over
    [InlineData("0", "575.01", true)] // no tolerance: one cent over fires
    [InlineData("100", "1150.00", false)] // the largest tolerance doubles the limit
    [InlineData("100", "1150.01", true)]
    public void TheToleranceWidensTheLimit_AndARateEqualToApprovedPlusToleranceHolds(string tolerance, string billed, bool fires)
    {
        var findings = Run(RateRule(tolerance), Facts(Line(D(billed))));

        findings.Any().Should().Be(fires);
    }

    [Theory]
    [InlineData("333.33", "3", "343.33", false)] // 333.33 x 1.03 = 343.3299, rounded to the cent 343.33: a rate of 343.33 holds
    [InlineData("333.33", "3", "343.34", true)]
    [InlineData("100.10", "5", "105.11", false)] // 100.10 x 1.05 = 105.105, a midpoint: away from zero gives 105.11
    [InlineData("100.10", "5", "105.12", true)]
    public void TheLimitIsRoundedToTheCent_MidpointAwayFromZero(string approved, string tolerance, string billed, bool fires)
    {
        Run(RateRule(tolerance), Facts(Line(D(billed), D(approved)))).Any().Should().Be(fires);
    }

    [Fact]
    public void TheSuggestedReduction_IsTheExcessOverTheApprovedRate_NotOverTheToleranceLimit()
    {
        // 650 is over the 5% limit (603.75). The tolerance is the slack before the rule fires, not a rate the firm is entitled to.
        var finding = Run(RateRule("5"), Facts(Line(billed: 650m, hours: 1m))).Single();

        finding.SuggestedAdjustment!.Amount.Should().Be(75.00m);
        finding.Value("approvedRate.limit").Value.Should().Be("603.75");
    }

    [Theory]
    [InlineData("600.33", "0.15", "3.80")] // 25.33 x 0.15 = 3.7995
    [InlineData("100.15", "0.1", "0.01")] // 0.05 x 0.1 = 0.005, a midpoint: away from zero
    [InlineData("650.00", "0.33", "24.75")]
    public void TheSuggestedReduction_IsRoundedToTheCent_MidpointAwayFromZero(string billedText, string hoursText, string expectedText)
    {
        var billed = D(billedText);
        var approved = billed > 600 ? 575m : 100.10m;
        var finding = Run(RateRule(), Facts(Line(billed, approved, D(hoursText)))).Single();

        finding.SuggestedAdjustment!.Amount.Should().Be(D(expectedText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void WithNoPositiveHours_TheRuleStillFires_ButTheReductionAmountIsUnknown(double? hours)
    {
        var finding = Run(RateRule(), Facts(Line(650m, hours: (decimal?)hours))).Single();

        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Reduce, null));
    }

    // ── What the rule judges ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ALineThatBillsNoHourlyRate_IsNotJudged_EvenWithNoRateCard()
    {
        var facts = Facts(Fee(0) with { BilledRate = null, ApprovedRate = new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.NoRateInForce } });

        Run(RateRule(), facts).Should().BeEmpty();
    }

    [Fact]
    public void ExpenseLines_AreNotJudged()
    {
        Run(RateRule(), Facts(Expense(0) with { BilledRate = 9999m })).Should().BeEmpty();
    }

    // ── No approved rate (046 found none, several that disagree, or could not look one up) ──────────────────────

    [Theory]
    [InlineData(RateResolutionStatus.NotFound, RateNotFoundReason.NoRateInForce, BillingReasonCodes.RateNoRateInForce)]
    [InlineData(RateResolutionStatus.NotFound, RateNotFoundReason.TimekeeperUnmatched, BillingReasonCodes.RateTimekeeperUnmatched)]
    [InlineData(RateResolutionStatus.NotFound, RateNotFoundReason.LineDateMissing, BillingReasonCodes.RateLineDateMissing)]
    [InlineData(RateResolutionStatus.NotFound, RateNotFoundReason.None, BillingReasonCodes.RateNoRateInForce)]
    [InlineData(RateResolutionStatus.Ambiguous, RateNotFoundReason.None, BillingReasonCodes.RateAmbiguous)]
    [InlineData(RateResolutionStatus.NotApplicable, RateNotFoundReason.None, BillingReasonCodes.RateUnresolved)]
    public void WhenNoApprovedRateWasEstablished_TheRuleFiresWithTheReason_AndSuggestsNothing(
        RateResolutionStatus status, RateNotFoundReason reason, string expectedCode)
    {
        var facts = Facts(Fee(0) with { ApprovedRate = new RateResolution { Status = status, Reason = reason } });

        var finding = Run(RateRule(), facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(expectedCode);
        finding.SuggestedAdjustment.Should().BeNull();
        finding.FactValues.Should().NotContainKey("approvedRate", "there is none to show");
        finding.ReadFields.Should().Equal(
            new FieldRef(0, BillingFieldNames.Rate), new FieldRef(0, BillingFieldNames.Date), new FieldRef(0, BillingFieldNames.TimekeeperAsBilled));
    }

    [Fact]
    public void AMissingResolution_AndAFoundStatusWithoutARate_FailClosed()
    {
        var noResolution = Facts(Fee(0) with { ApprovedRate = null });
        var foundNoRate = Facts(Fee(0) with { ApprovedRate = new RateResolution { Status = RateResolutionStatus.Found, ApprovedRate = null } });

        Run(RateRule(), noResolution).Single().ReasonCode.Should().Be(BillingReasonCodes.RateUnresolved);
        Run(RateRule(), foundNoRate).Single().ReasonCode.Should().Be(BillingReasonCodes.RateUnresolved);
    }

    // ── Evidence ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadFields_AreTheRate_TheHours_TheDateAndTheTimekeeper_AndTheApprovedRateIsAReferenceValue()
    {
        var finding = Run(RateRule("5"), Facts(Line(700m, index: 4))).Single();

        finding.ReadFields.Should().Equal(
            new FieldRef(4, BillingFieldNames.Rate), new FieldRef(4, BillingFieldNames.Hours),
            new FieldRef(4, BillingFieldNames.Date), new FieldRef(4, BillingFieldNames.TimekeeperAsBilled));
        finding.Value("approvedRate").IsExtracted.Should().BeFalse();
        finding.Value("approvedRate.basis").Value.Should().Be("Timekeeper");
        finding.Value("approvedRate.scope").Value.Should().Be("Firm");
        finding.Value("rateCard.id").Value.Should().Be("07100000-0000-4000-8000-0000000000c1");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.ApprovedRate));
    }

    [Fact]
    public void ALowConfidenceRate_IsCarriedAndMarkedUnreliable_SoItCannotRejectLater()
    {
        var line = Line(700m) with { FieldConfidence = Confidence().With(BillingFieldNames.Rate, 0.60m) };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.Value("line.rate").Confidence.Should().Be(0.60m);
        finding.Value("line.rate").BelowReliabilityThreshold.Should().BeTrue();
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void ARateWithNoKnownConfidence_IsMarkedUnreliable()
    {
        var line = Line(700m) with { FieldConfidence = Confidence().With(BillingFieldNames.Rate, null) };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.Value("line.rate").Confidence.Should().BeNull();
        finding.Value("line.rate").BelowReliabilityThreshold.Should().BeTrue();
    }

    [Fact]
    public void ADifferentApprovedRatePerLine_IsComparedPerLine()
    {
        var facts = Facts(Line(650m, approved: 575m, index: 0), Line(650m, approved: 650m, index: 1), Line(700m, approved: 650m, hours: 1m, index: 2));

        var findings = Run(RateRule(), facts);

        findings.Select(f => f.Subject.LineIndex).Should().Equal(0, 2);
        findings[1].SuggestedAdjustment!.Amount.Should().Be(50m);
    }
}
