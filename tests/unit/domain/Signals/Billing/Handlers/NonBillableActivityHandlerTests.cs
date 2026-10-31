using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 5 NonBillableActivity (design.md 6.1; NFR-05). Maintain-class (ADR-038): pure logic, no mocks.
/// </summary>
public class NonBillableActivityHandlerTests
{
    private static readonly BillingRuleDefinition NonBillable = Rule("""{"type":"NonBillableActivity","codes":["A101","L160"]}""");

    [Fact]
    public void MustFire_WhenTheActivityCodeIsListed_WithASuggestedDisallowOfTheLineAmount()
    {
        var facts = Facts(Fee(0) with { ActivityCode = "A101", TaskCode = "L110", Amount = 575.005m });

        var finding = Run(NonBillable, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.NonBillableCode);
        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Disallow, 575.01m), "rounded to the cent, midpoint away from zero");
        finding.FactValues.Should().Contain("line.activityCode", "A101").And.Contain("line.taskCode", "L110");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.NonBillableActivity));
    }

    [Fact]
    public void MustFire_WhenTheTaskCodeIsListed()
    {
        var facts = Facts(Fee(0) with { TaskCode = "L160", ActivityCode = "A102" });

        Run(NonBillable, facts).Should().ContainSingle();
    }

    [Fact]
    public void MustNotFire_WhenNeitherCodeIsListed()
    {
        Run(NonBillable, Facts(Fee(0) with { TaskCode = "L110", ActivityCode = "A102" })).Should().BeEmpty();
    }

    [Fact]
    public void ABothCodesListedLine_GivesOneFinding()
    {
        Run(NonBillable, Facts(Fee(0) with { TaskCode = "L160", ActivityCode = "A101" })).Should().ContainSingle();
    }

    [Fact]
    public void TheListedCodesMatchWithoutRegardToCaseOrSurroundingSpace()
    {
        Run(NonBillable, Facts(Fee(0) with { ActivityCode = " a101 " })).Should().ContainSingle();
    }

    [Fact]
    public void ALineTheAiDidNotCode_DoesNotFire_EvenIfTheFirmsOwnCodeIsListed()
    {
        // The rule reads the AI-assigned codes (design 6.1). The firm's code is RequiredData's, and a firm-versus-AI mismatch is its own finding.
        var facts = Facts(Fee(0) with { ActivityCode = null, TaskCode = null, BilledActivityCode = "A101", BilledTaskCode = "L160" });

        Run(NonBillable, facts).Should().BeEmpty();
    }

    [Fact]
    public void ALineWithNoAmount_StillFires_WithTheDisallowAmountUnknown()
    {
        var finding = Run(NonBillable, Facts(Fee(0) with { ActivityCode = "A101", Amount = null })).Single();

        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Disallow, null));
    }

    // ── D-105: a credit is never disallowed ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.0", "-575.00")] // negative amount
    [InlineData("-1.0", "575.00")] // negative hours
    [InlineData("-1.0", "-575.00")]
    public void ACreditOnAListedCode_StillFires_ButSuggestsNoAdjustment_NeverADisallowOfANegativeAmount_D105(string hours, string amount)
    {
        var facts = Facts(Fee(0) with { ActivityCode = "A101", Hours = D(hours), Amount = D(amount) });

        var finding = Run(NonBillable, facts).Should().ContainSingle("the credit stays visible to the reviewer").Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.NonBillableCode);
        finding.SuggestedAdjustment.Should().BeNull("disallowing a negative amount would raise the bill");
    }

    [Fact]
    public void MustFire_AChargeOnTheSameCode_StillSuggestsTheDisallow_NextToACredit_D105()
    {
        var facts = Facts(
            Fee(0) with { ActivityCode = "A101", Amount = 575m },
            Fee(1) with { ActivityCode = "A101", Amount = -575m });

        var findings = Run(NonBillable, facts);

        findings.Select(f => f.SuggestedAdjustment).Should().Equal(
            new BillingSuggestedAdjustment(BillingAdjustmentKind.Disallow, 575m), null);
    }

    [Theory]
    [InlineData("A101", null)]
    [InlineData(null, "L160")]
    public void D128_AReviewersManualDisallowOfACredit_DoesNotChangeTheAutomaticRule_ACreditIsStillNeverSuggested(string? activity, string? task)
    {
        // D-128 lets a reviewer disallow a credit by hand (016 sums it signed). It is a manual decision only: the automatic
        // suggestion of this rule stays as D-105 set it, for either listed code and even for the smallest credit.
        var facts = Facts(
            Fee(0) with { ActivityCode = activity, TaskCode = task, Amount = -0.01m },
            Fee(1) with { ActivityCode = activity, TaskCode = task, Amount = 0.01m });

        var findings = Run(NonBillable, facts);

        findings.Should().HaveCount(2, "the credit still fires, so the reviewer sees it");
        findings[0].SuggestedAdjustment.Should().BeNull("a negative amount is never suggested for disallow");
        findings[1].SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Disallow, 0.01m));
    }

    [Fact]
    public void AZeroAmount_IsNotACredit_AndStillSuggestsTheDisallow_D105()
    {
        var finding = Run(NonBillable, Facts(Fee(0) with { ActivityCode = "A101", Amount = 0m })).Single();

        finding.SuggestedAdjustment.Should().Be(new BillingSuggestedAdjustment(BillingAdjustmentKind.Disallow, 0m));
    }

    // ── NFR-05: low-confidence codes are flagged, never silently used ───────────────────────────────────────────

    [Fact]
    public void ALowConfidenceListedCode_IsNotUsedSilently_ItsConfidenceIsCarriedAndMarkedBelowTheThreshold()
    {
        var line = Fee(0) with { ActivityCode = "A101", FieldConfidence = Confidence().With(BillingFieldNames.ActivityCode, 0.42m) };

        var finding = Run(NonBillable, Facts(line)).Should().ContainSingle().Subject;

        var code = finding.Value("line.activityCode");
        code.Confidence.Should().Be(0.42m);
        code.BelowReliabilityThreshold.Should().BeTrue();
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void AConfidentListedCode_IsNotMarkedUnreliable()
    {
        var finding = Run(NonBillable, Facts(Fee(0) with { ActivityCode = "A101" })).Single();

        finding.Value("line.activityCode").BelowReliabilityThreshold.Should().BeFalse();
        finding.ReadsLowConfidenceValue.Should().BeFalse();
    }

    [Fact]
    public void ACodeWithNoKnownCodingConfidence_IsMarkedUnreliable()
    {
        var line = Fee(0) with { ActivityCode = "A101", FieldConfidence = Confidence().With(BillingFieldNames.ActivityCode, null) };

        Run(NonBillable, Facts(line)).Single().Value("line.activityCode").BelowReliabilityThreshold.Should().BeTrue();
    }

    [Fact]
    public void ALowConfidenceCodeThatIsNotListed_DoesNotFire()
    {
        var line = Fee(0) with { ActivityCode = "A102", FieldConfidence = Confidence().With(BillingFieldNames.ActivityCode, 0.10m) };

        Run(NonBillable, Facts(line)).Should().BeEmpty();
    }

    [Fact]
    public void ReadFields_AreBothCodes_TheAmount_TheDateAndTheTimekeeper_ThenTheHoursTheCreditTestRead()
    {
        Run(NonBillable, Facts(Fee(5) with { ActivityCode = "A101" })).Single().ReadFields.Should().Equal(
            new FieldRef(5, BillingFieldNames.ActivityCode), new FieldRef(5, BillingFieldNames.TaskCode), new FieldRef(5, BillingFieldNames.Amount),
            new FieldRef(5, BillingFieldNames.Date), new FieldRef(5, BillingFieldNames.TimekeeperAsBilled),
            new FieldRef(5, BillingFieldNames.Hours));
    }

    [Fact]
    public void ALowConfidenceHoursValue_IsCarriedOnTheFinding_BecauseTheCreditTestThatDecidesTheDisallowReadIt_F4()
    {
        // 1.0 hours at 0.40 could really be -1.0 (a credit): the disallow suggestion depends on it, so task 073 must see the doubt.
        var line = Fee(0) with { ActivityCode = "A101", FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.40m) };

        var finding = Run(NonBillable, Facts(line)).Single();

        finding.Value("line.hours").Confidence.Should().Be(0.40m);
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }
}
