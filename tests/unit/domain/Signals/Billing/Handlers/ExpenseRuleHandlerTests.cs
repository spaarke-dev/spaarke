using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 6 ExpenseRule (design.md 6.1). Maintain-class (ADR-038): pure logic, no mocks.
/// </summary>
public class ExpenseRuleHandlerTests
{
    private static readonly BillingRuleDefinition Both = Rule("""{"type":"ExpenseRule","nonReimbursableCodes":["E109","E110"],"maxAmountPerItem":500}""");
    private static readonly BillingRuleDefinition CodesOnly = Rule("""{"type":"ExpenseRule","nonReimbursableCodes":["E109"]}""");
    private static readonly BillingRuleDefinition CapOnly = Rule("""{"type":"ExpenseRule","maxAmountPerItem":500}""");

    // ── D-117: the code check judges the code the firm billed ────────────────────────────────────────────────────

    [Fact]
    public void TheCodeCheck_JudgesTheFirmBilledCode_NotTheAiAssignedOne_D117()
    {
        // The firm billed E101; the coding Action assigned E109.
        var line = Expense(0) with { BilledExpenseCode = "E101", ExpenseCode = "E109" };

        Run(CodesOnly, Facts(line)).Should().BeEmpty("the firm did not bill E109");

        var onE101 = Rule("""{"type":"ExpenseRule","nonReimbursableCodes":["E101"]}""");
        var finding = Run(onE101, Facts(line)).Should().ContainSingle().Subject;
        finding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);
        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.BilledExpenseCode))
            .And.NotContain(new FieldRef(0, BillingFieldNames.ExpenseCode));
        finding.Value("line.expenseCode").Value.Should().Be("E101");
    }

    [Fact]
    public void WithNoBilledCode_TheAiAssignedCodeIsTheFallback_AndTheFindingReadsIt_D117()
    {
        var line = Expense(0) with { BilledExpenseCode = null, ExpenseCode = "E109" };

        var finding = Run(CodesOnly, Facts(line)).Should().ContainSingle().Subject;

        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.ExpenseCode), "the fallback reads the AI-assigned field, so it can only flag")
            .And.NotContain(new FieldRef(0, BillingFieldNames.BilledExpenseCode));
        finding.Value("line.expenseCode").Value.Should().Be("E109");
    }

    [Fact]
    public void TheCapFinding_NamesTheBilledCode_D117()
    {
        var line = Expense(0) with { BilledExpenseCode = "E101", ExpenseCode = "E109", Amount = 900m };

        var finding = Run(CapOnly, Facts(line)).Should().ContainSingle().Subject;

        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.BilledExpenseCode)).And.NotContain(new FieldRef(0, BillingFieldNames.ExpenseCode));
    }

    [Fact]
    public void MustFire_ForANonReimbursableCode()
    {
        var finding = Run(Both, Facts(Expense(0) with { BilledExpenseCode = "E109" })).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().BeNull("design 6.1 allows Reject or Flag only");
        finding.FactValues.Should().Contain("line.expenseCode", "E109").And.Contain("line.amount", "40.00");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.ExpenseRule));
    }

    [Fact]
    public void MustFire_ForAnAmountOverTheCapPerItem()
    {
        var finding = Run(Both, Facts(Expense(0) with { Amount = 500.01m })).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseAmountOverCap);
    }

    [Fact]
    public void MustNotFire_ForAnAmountExactlyAtTheCap_OrAnAllowedCode()
    {
        Run(Both, Facts(Expense(0) with { Amount = 500m }, Expense(1) with { BilledExpenseCode = "E101", Amount = 12m })).Should().BeEmpty();
    }

    [Fact]
    public void ALineThatBreaksBothChecks_GetsTwoFindings_TheCodeFirst()
    {
        var facts = Facts(Expense(0) with { BilledExpenseCode = "E110", Amount = 900m });

        Run(Both, facts).Select(f => f.ReasonCode).Should().Equal(
            BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap);
    }

    [Fact]
    public void ACodesOnlyRule_IgnoresTheAmount_AndACapOnlyRule_IgnoresTheCode()
    {
        Run(CodesOnly, Facts(Expense(0) with { Amount = 9999m })).Should().BeEmpty();
        Run(CapOnly, Facts(Expense(0) with { BilledExpenseCode = "E109" })).Should().BeEmpty();
    }

    [Fact]
    public void TheListedCodesMatchWithoutRegardToCase()
    {
        Run(CodesOnly, Facts(Expense(0) with { BilledExpenseCode = "e109" })).Should().ContainSingle();
    }

    [Fact]
    public void ALineWithNoCodeOrNoAmount_IsNotJudgedOnThatCheck()
    {
        Run(Both, Facts(Expense(0) with { BilledExpenseCode = null, Amount = null })).Should().BeEmpty();
    }

    [Fact]
    public void FeeLines_AreNotJudged()
    {
        Run(Both, Facts(Fee(0) with { BilledExpenseCode = "E109", Amount = 99999m })).Should().BeEmpty();
    }

    // ── D-105: a credit line is never judged as a charge ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("E109", "-50.00")] // a refund of a non-reimbursable expense
    [InlineData("E110", "-0.01")]
    [InlineData("e109", "-900.00")]
    public void MustNotFire_ForACreditOnANonReimbursableCode_D105(string code, string amount)
    {
        Run(Both, Facts(Expense(0) with { BilledExpenseCode = code, Amount = D(amount) })).Should().BeEmpty("a credit is a refund, not a charge");
        Run(CodesOnly, Facts(Expense(0) with { BilledExpenseCode = code, Amount = D(amount) })).Should().BeEmpty();
    }

    [Fact]
    public void MustNotFire_ForACreditUnderTheCapOnlyRule_D105()
    {
        Run(CapOnly, Facts(Expense(0) with { Amount = -5000m })).Should().BeEmpty();
    }

    [Fact]
    public void MustFire_ForTheChargeLinesAroundACredit_TheCreditIsSkippedAndTheRestAreStillJudged_D105()
    {
        var facts = Facts(
            Expense(0) with { BilledExpenseCode = "E109", Amount = 50m },
            Expense(1) with { BilledExpenseCode = "E109", Amount = -50m },
            Expense(2) with { BilledExpenseCode = "E101", Amount = 600m });

        var findings = Run(Both, facts);

        findings.Select(f => (f.Subject.LineIndex, f.ReasonCode)).Should().Equal(
            (0, BillingReasonCodes.ExpenseCodeNotReimbursable), (2, BillingReasonCodes.ExpenseAmountOverCap));
    }

    [Fact]
    public void AZeroAmountOnANonReimbursableCode_IsNotACredit_AndStillFires_D105()
    {
        Run(Both, Facts(Expense(0) with { BilledExpenseCode = "E109", Amount = 0m })).Should().ContainSingle()
            .Which.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);
    }

    [Fact]
    public void ReadFields_AreTheBilledExpenseCode_TheAmountAndTheDate_AndTheHoursWhenTheLineHasThem_D117()
    {
        Run(Both, Facts(Expense(7) with { BilledExpenseCode = "E109" })).Single().ReadFields.Should().Equal(
            new FieldRef(7, BillingFieldNames.BilledExpenseCode), new FieldRef(7, BillingFieldNames.Amount), new FieldRef(7, BillingFieldNames.Date));

        // The credit test reads the hours too, so an expense line that carries them lists them (F4).
        Run(Both, Facts(Expense(7) with { BilledExpenseCode = "E109", Hours = 1m })).Single().ReadFields.Should().Equal(
            new FieldRef(7, BillingFieldNames.BilledExpenseCode), new FieldRef(7, BillingFieldNames.Amount), new FieldRef(7, BillingFieldNames.Date),
            new FieldRef(7, BillingFieldNames.Hours));
    }

    [Fact]
    public void AnExpenseLineWithLowConfidenceHours_CarriesThemOnTheFinding_BecauseTheCreditTestReadThem_F4()
    {
        var line = Expense(0) with
        {
            BilledExpenseCode = "E109",
            Hours = 1m,
            FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.40m),
        };

        var finding = Run(Both, Facts(line)).Single();

        finding.Value("line.hours").Confidence.Should().Be(0.40m);
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void ALineWithNegativeHoursAndAPositiveAmount_IsSkippedAsACredit_NeitherCheckFires_K2()
    {
        // Negative hours alone make a credit (D-105): the amount is positive and over the cap, on a non-reimbursable code.
        Run(Both, Facts(Expense(0) with { BilledExpenseCode = "E109", Hours = -1m, Amount = 900m })).Should().BeEmpty();
    }

    [Fact]
    public void ALowConfidenceExpenseCode_IsCarriedAndFlagged()
    {
        var line = Expense(0) with { BilledExpenseCode = "E109", FieldConfidence = Confidence().With(BillingFieldNames.BilledExpenseCode, 0.55m) };

        var finding = Run(Both, Facts(line)).Single();

        finding.Value("line.expenseCode").Confidence.Should().Be(0.55m);
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void ARuleWithNeitherParameter_IsRefused_NotTreatedAsHolding()
    {
        // A private copy: Corrupt changes the instance in place.
        var rule = Corrupt(Rule("""{"type":"ExpenseRule","maxAmountPerItem":500}"""), nameof(BillingRuleDefinition.MaxAmountPerItem), null);

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, Facts(Expense(0)));

        act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }
}
