using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 115, rule type 12 AddedFee (procedure-and-file-model.md 6.3, O-16): the cap form. Maintain-class (ADR-038): pure
/// logic, no mocks. The refusal of the other two allowances is in <see cref="BillingRuleCatalogueTests"/> (save and gate).
/// </summary>
public class AddedFeeHandlerTests
{
    // Properties, not fields: Corrupt changes a definition in place, so each use gets its own.
    private static BillingRuleDefinition AmountCap =>
        Rule("""{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""");

    private static BillingRuleDefinition PercentCap =>
        Rule("""{"type":"AddedFee","feeKind":"administrativeFee","allowance":"cap","feeCodes":["E120","E121"],"capPercent":5}""");

    private static BillingLineFacts LateFee(int index, decimal amount) =>
        Expense(index) with { BilledExpenseCode = "E120", ExpenseCode = "E120", Amount = amount, Narrative = "Late payment fee" };

    // ── Amount cap ───────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FeeLinesOverTheCap_GiveOneFinding_WithTheReasonCode_OnTheInvoice()
    {
        var facts = Facts(Fee(0), LateFee(1, 60m), Expense(2), LateFee(3, 60m));

        var finding = Run(AmountCap, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.AddedFeeOverCap);
        finding.RuleType.Should().Be(RuleType.AddedFee);
        finding.Subject.Should().Be(BillingSubject.ForInvoice(), "the cap is a property of the invoice (review F3-6)");
        finding.SuggestedAdjustment.Should().BeNull("an added fee is flagged, never adjusted");
        finding.FactValues.Should().Contain("feeTotal", "120.00").And.Contain("feeLimit", "100.00")
            .And.Contain("rule.feeKind", "late fee", "the kind's label, never the raw value (review F3-5)");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.AddedFee));
        finding.ReadFields.Should().Contain(new FieldRef(1, BillingFieldNames.Amount)).And.Contain(new FieldRef(3, BillingFieldNames.Amount))
            .And.NotContain(new FieldRef(2, BillingFieldNames.Amount), "only the fee lines are read for an amount cap");
    }

    [Theory]
    [InlineData("100")]
    [InlineData("99.99")]
    [InlineData("100.004")] // 100.00 to the cent: at the cap
    public void FeeLinesAtOrUnderTheCap_GiveNoFinding(string amount)
    {
        Run(AmountCap, Facts(Fee(0), LateFee(1, D(amount)))).Should().BeEmpty();
    }

    [Fact]
    public void AnInvoiceWithNoMatchingFeeLine_GivesNoFinding()
    {
        // A big expense under another code, and a fee LINE (not an expense line) that carries the code: neither is an added fee.
        var facts = Facts(Fee(0) with { BilledExpenseCode = "E120", Amount = 9999m }, Expense(1) with { Amount = 9999m });

        Run(AmountCap, facts).Should().BeEmpty();
    }

    [Fact]
    public void TheFeeCodesMatchWithoutRegardToCase_AndTheAiCodeIsTheFallback_D117()
    {
        Run(AmountCap, Facts(LateFee(0, 150m) with { BilledExpenseCode = "e120" })).Should().ContainSingle();

        var fallback = Run(AmountCap, Facts(LateFee(0, 150m) with { BilledExpenseCode = null })).Should().ContainSingle().Subject;
        fallback.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.ExpenseCode));
    }

    [Fact]
    public void ACreditedFee_LowersTheTotal()
    {
        Run(AmountCap, Facts(LateFee(0, 150m), LateFee(1, -60m))).Should().BeEmpty("150 - 60 = 90 is under the cap of 100");

        var finding = Run(AmountCap, Facts(LateFee(0, -10m), LateFee(1, 150m))).Should().ContainSingle().Subject;
        finding.FactValues["feeTotal"].Should().Be("140.00");
    }

    [Fact]
    public void AFeeLineWithNoAmount_IsLeftOutOfTheTotal()
    {
        Run(AmountCap, Facts(LateFee(0, 0m) with { Amount = null }, LateFee(1, 90m))).Should().BeEmpty();
    }

    // ── Percent cap ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void APercentCap_IsAPercentOfTheOtherLines()
    {
        // Other lines: 575 + 40 = 615; 5 percent = 30.75.
        var over = Run(PercentCap, Facts(Fee(0), Expense(1), LateFee(2, 20m), LateFee(3, 10.76m) with { BilledExpenseCode = "E121" }))
            .Should().ContainSingle().Subject;
        over.FactValues.Should().Contain("feeTotal", "30.76").And.Contain("feeLimit", "30.75");
        over.Value("feeBase").Value.Should().Be("615.00");

        Run(PercentCap, Facts(Fee(0), Expense(1), LateFee(2, 30.75m))).Should().BeEmpty("a total equal to the limit holds");
    }

    [Fact]
    public void APercentCap_WithABaseOfZeroOrLess_IsNotChecked_AndSaysSo()
    {
        // Nothing else on the invoice; other lines that total exactly 0; other lines that total below 0. Review F2-4.
        foreach (var facts in new[]
        {
            Facts(LateFee(0, 0.01m)),
            Facts(Expense(0) with { Amount = 0m }, LateFee(1, 5m)),
            Facts(Expense(0) with { Amount = -50m }, LateFee(1, 5m)),
        })
        {
            var finding = Run(PercentCap, facts).Should().ContainSingle().Subject;

            finding.ReasonCode.Should().Be(BillingReasonCodes.AddedFeeNotChecked);
            finding.Subject.Should().Be(BillingSubject.ForInvoice());
            finding.FactValues.Should().BeEmpty("the reason has its own message, which names no token");
            BillingRuleCatalogue.IsFlagOnlyReason(finding.ReasonCode).Should().BeTrue();
            BillingRuleCatalogue.ReasonTemplate(finding.ReasonCode).Should().Be(BillingRuleCatalogue.AddedFeeNotCheckedTemplate);
            BillingRuleCatalogue.ReasonHeadline(finding.ReasonCode).Should().Be("Added-fee limit not checked");
        }
    }

    [Fact]
    public void APercentCap_WhenAnotherLineHasNoAmount_IsNotChecked()
    {
        var finding = Run(PercentCap, Facts(Fee(0), Expense(1) with { Amount = null }, LateFee(2, 1m))).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.AddedFeeNotChecked);
        finding.Subject.Should().Be(BillingSubject.ForInvoice());
    }

    [Fact]
    public void TheNotCheckedFinding_NeedsAPercentCap_AndAFeeTotalAbove0()
    {
        // A fee line with no amount is only left out of the total; it does not make the base unusable.
        Run(PercentCap, Facts(Fee(0), Expense(1), LateFee(2, 0m) with { Amount = null }, LateFee(3, 1m))).Should().BeEmpty();

        // An amount cap needs no base: another line with no amount does not stop the check.
        Run(AmountCap, Facts(Fee(0) with { Amount = null }, LateFee(1, 150m))).Should().ContainSingle()
            .Which.ReasonCode.Should().Be(BillingReasonCodes.AddedFeeOverCap);

        // A fee total of 0 or less never fires, base or no base.
        Run(PercentCap, Facts(LateFee(0, -5m))).Should().BeEmpty();
    }

    // ── Fail closed ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARuleWithBothCapsOrNeither_IsRefused_NotEvaluated()
    {
        var both = Corrupt(AmountCap, nameof(BillingRuleDefinition.CapPercent), 5m);
        var neither = Corrupt(AmountCap, nameof(BillingRuleDefinition.CapAmount), null);

        foreach (var rule in new[] { both, neither })
        {
            var act = () => BillingRuleHandlers.Evaluate(rule, Origin, Facts(LateFee(0, 500m)));
            act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
        }
    }

    [Theory]
    [InlineData("notAllowed")]
    [InlineData("afterDays")]
    [InlineData(null)]
    public void ADefinitionThatIsNotACap_IsRefusedByTheHandlerToo(string? allowance)
    {
        var rule = Corrupt(AmountCap, nameof(BillingRuleDefinition.Allowance), allowance);

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, Facts(LateFee(0, 500m)));

        act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }
}
