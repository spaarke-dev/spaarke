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

    [Theory]
    [InlineData("575.004", "0", false)] // rounds to 575.00: equal to the approved rate, which holds
    [InlineData("575.0049", "0", false)]
    [InlineData("575.005", "0", true)] // the midpoint rounds away from zero to 575.01: one cent over
    [InlineData("603.754", "5", false)] // 575 + 5% = 603.75; 603.754 is 603.75
    [InlineData("603.755", "5", true)] // 603.76
    public void TheBilledRateIsComparedAtTheCent_ASubCentRateNeverFiresAgainstALimitThatShowsAsEqual(string billed, string tolerance, bool fires)
    {
        var findings = Run(RateRule(tolerance), Facts(Line(D(billed))));

        findings.Any().Should().Be(fires);
    }

    [Fact]
    public void TheReduction_IsComputedFromTheRateAsShown_SoTheFindingAddsUp()
    {
        // 650.004 is shown as 650.00; (650.00 - 575.00) x 2.5 = 187.50, not 187.51 from the unrounded rate.
        var finding = Run(RateRule(), Facts(Line(650.004m))).Single();

        finding.Value("line.rate").Value.Should().Be("650.00");
        finding.SuggestedAdjustment!.Amount.Should().Be(187.50m);
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
    public void WithNoHoursOrZeroHours_TheRuleStillFires_ButTheReductionAmountIsUnknown(double? hours)
    {
        var finding = Run(RateRule(), Facts(Line(650m, hours: (decimal?)hours))).Single();

        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Reduce, null));
    }

    // ── D-105: a credit line is never judged as a charge ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("-1.0", "575.00")] // negative hours
    [InlineData("-2.5", null)]
    [InlineData("2.5", "-1625.00")] // negative amount
    [InlineData(null, "-1625.00")]
    [InlineData("-2.5", "-1625.00")]
    public void MustNotFire_ForACreditLine_AtARateAboveTheApprovedRate_D105(string? hours, string? amount)
    {
        var credit = Line(650m, hours: hours is null ? null : D(hours)) with { Amount = amount is null ? null : D(amount) };

        Run(RateRule(), Facts(credit)).Should().BeEmpty("a reversal is not billed at a rate");
    }

    [Fact]
    public void MustNotFire_ForACreditLine_WhoseApprovedRateWasNotEstablished_D105()
    {
        var credit = Fee(0) with { Hours = -1m, ApprovedRate = new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.NoRateInForce } };

        Run(RateRule(), Facts(credit)).Should().BeEmpty("the no-approved-rate finding is a charge finding too");
    }

    [Fact]
    public void MustFire_ForTheChargeLinesAroundACreditLine_TheCreditIsSkippedAndTheRestAreStillJudged_D105()
    {
        var facts = Facts(Line(650m, index: 0), Line(650m, hours: -2.5m, index: 1), Line(650m, index: 2));

        var findings = Run(RateRule(), facts);

        findings.Select(f => f.Subject.LineIndex).Should().Equal(0, 2);
        findings.Should().OnlyContain(f => f.ReasonCode == BillingReasonCodes.RateAboveApproved);
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
            new FieldRef(0, BillingFieldNames.Rate), new FieldRef(0, BillingFieldNames.Date), new FieldRef(0, BillingFieldNames.TimekeeperAsBilled),
            new FieldRef(0, BillingFieldNames.Hours), new FieldRef(0, BillingFieldNames.Amount));
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
    public void ReadFields_AreTheRate_TheHours_TheDateAndTheTimekeeper_ThenTheAmountTheCreditTestRead_AndTheApprovedRateIsAReferenceValue()
    {
        var finding = Run(RateRule("5"), Facts(Line(700m, index: 4))).Single();

        finding.ReadFields.Should().Equal(
            new FieldRef(4, BillingFieldNames.Rate), new FieldRef(4, BillingFieldNames.Hours),
            new FieldRef(4, BillingFieldNames.Date), new FieldRef(4, BillingFieldNames.TimekeeperAsBilled),
            new FieldRef(4, BillingFieldNames.Amount));
        finding.Value("approvedRate").IsExtracted.Should().BeFalse();
        finding.Value("approvedRate.basis").Value.Should().Be("Timekeeper");
        finding.Value("approvedRate.scope").Value.Should().Be("Firm");
        finding.Value("rateCard.id").Value.Should().Be("07100000-0000-4000-8000-0000000000c1");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.ApprovedRate));
    }

    [Fact]
    public void WhenTheLineHasNoHours_TheHoursAreNotRead_SoALowConfidenceHoursValueCannotHoldAFindingThatNeverSawIt()
    {
        var line = Line(700m, hours: null) with { FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.20m) };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.ReadFields.Should().Equal(
            new FieldRef(0, BillingFieldNames.Rate), new FieldRef(0, BillingFieldNames.Date), new FieldRef(0, BillingFieldNames.TimekeeperAsBilled),
            new FieldRef(0, BillingFieldNames.Amount));
        finding.ValuesRead.Select(v => v.Name).Should().NotContain("line.hours");
        finding.ReadsLowConfidenceValue.Should().BeFalse("there were no hours to read");
    }

    [Fact]
    public void WhenTheHoursAreZero_TheReductionIsNotComputed_ButTheCreditTestStillReadTheHours_F4()
    {
        var line = Line(700m, hours: 0m) with { FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.20m) };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.SuggestedAdjustment!.Amount.Should().BeNull();
        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Hours), "IsCreditLine tested the hours");
        finding.ReadsLowConfidenceValue.Should().BeTrue("task 073 must see that the hours that cleared the credit test are doubtful");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCreditTestReads_AreOnTheFindingWhateverTheRateRuleFoundOnTheLine_F4(bool rateEstablished)
    {
        // A reversal printed "(1,437.50)" but extracted as +1437.50 at 0.50 is judged as a charge only because the amount was
        // trusted. The finding must list the Amount (and carry its confidence) for both the above-approved and the no-rate finding.
        var line = Line(700m) with
        {
            Amount = 1437.50m,
            ApprovedRate = rateEstablished ? Rate(575m) : new RateResolution { Status = RateResolutionStatus.NotFound, Reason = RateNotFoundReason.NoRateInForce },
            FieldConfidence = Confidence().With(BillingFieldNames.Amount, 0.50m),
        };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Amount));
        finding.Value("line.amount").Confidence.Should().Be(0.50m);
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void WhenTheReductionIsComputed_TheHoursAreRead_AndALowConfidenceHoursValueIsFlagged()
    {
        var line = Line(700m, hours: 2m) with { FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.20m) };

        var finding = Run(RateRule(), Facts(line)).Single();

        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Hours));
        finding.Value("line.hours").BelowReliabilityThreshold.Should().BeTrue();
        finding.ReadsLowConfidenceValue.Should().BeTrue();
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
