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

    [Fact]
    public void MustFire_ForANonReimbursableCode()
    {
        var finding = Run(Both, Facts(Expense(0) with { ExpenseCode = "E109" })).Should().ContainSingle().Subject;

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
        Run(Both, Facts(Expense(0) with { Amount = 500m }, Expense(1) with { ExpenseCode = "E101", Amount = 12m })).Should().BeEmpty();
    }

    [Fact]
    public void ALineThatBreaksBothChecks_GetsTwoFindings_TheCodeFirst()
    {
        var facts = Facts(Expense(0) with { ExpenseCode = "E110", Amount = 900m });

        Run(Both, facts).Select(f => f.ReasonCode).Should().Equal(
            BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap);
    }

    [Fact]
    public void ACodesOnlyRule_IgnoresTheAmount_AndACapOnlyRule_IgnoresTheCode()
    {
        Run(CodesOnly, Facts(Expense(0) with { Amount = 9999m })).Should().BeEmpty();
        Run(CapOnly, Facts(Expense(0) with { ExpenseCode = "E109" })).Should().BeEmpty();
    }

    [Fact]
    public void TheListedCodesMatchWithoutRegardToCase()
    {
        Run(CodesOnly, Facts(Expense(0) with { ExpenseCode = "e109" })).Should().ContainSingle();
    }

    [Fact]
    public void ALineWithNoCodeOrNoAmount_IsNotJudgedOnThatCheck()
    {
        Run(Both, Facts(Expense(0) with { ExpenseCode = null, Amount = null })).Should().BeEmpty();
    }

    [Fact]
    public void FeeLines_AreNotJudged()
    {
        Run(Both, Facts(Fee(0) with { ExpenseCode = "E109", Amount = 99999m })).Should().BeEmpty();
    }

    [Fact]
    public void ReadFields_AreTheExpenseCode_TheAmountAndTheDate()
    {
        Run(Both, Facts(Expense(7) with { ExpenseCode = "E109" })).Single().ReadFields.Should().Equal(
            new FieldRef(7, BillingFieldNames.ExpenseCode), new FieldRef(7, BillingFieldNames.Amount), new FieldRef(7, BillingFieldNames.Date));
    }

    [Fact]
    public void ALowConfidenceExpenseCode_IsCarriedAndFlagged()
    {
        var line = Expense(0) with { ExpenseCode = "E109", FieldConfidence = Confidence().With(BillingFieldNames.ExpenseCode, 0.55m) };

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
