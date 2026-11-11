using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 3 TimeIncrement (design.md 6.1). Maintain-class (ADR-038): pure logic, no mocks.
/// </summary>
public class TimeIncrementHandlerTests
{
    private static BillingRuleDefinition IncrementRule(string increment = "0.1") =>
        Rule($$"""{"type":"TimeIncrement","incrementHours":{{increment}}}""");

    private static BillingFactSet FactsWithHours(decimal? hours) => Facts(Fee(0) with { Hours = hours });

    [Fact]
    public void MustFire_WhenHoursAreNotAMultipleOfTheIncrement()
    {
        var finding = Run(IncrementRule(), FactsWithHours(0.25m)).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.TimeNotAMultipleOfIncrement);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().BeNull("design 6.1 allows a flag only");
        finding.FactValues.Should().Contain("line.hours", "0.25").And.Contain("rule.incrementHours", "0.1");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.TimeIncrement));
    }

    [Theory]
    [InlineData("0.1", "0.1")]
    [InlineData("0.1", "0.3")] // 0.3 % 0.1 is 0.0999... in floating point, exactly 0 in decimal
    [InlineData("0.1", "0.7")]
    [InlineData("0.1", "1.0")]
    [InlineData("0.1", "24.0")]
    [InlineData("0.1", "0.0")]
    [InlineData("0.25", "0.5")]
    [InlineData("0.25", "1.75")]
    [InlineData("0.05", "0.15")]
    [InlineData("1", "8.0")]
    public void MustNotFire_ForAnExactMultiple(string increment, string hours)
    {
        Run(IncrementRule(increment), FactsWithHours(D(hours))).Should().BeEmpty();
    }

    [Theory]
    [InlineData("0.1", "0.11")]
    [InlineData("0.1", "0.05")]
    [InlineData("0.1", "1.01")]
    [InlineData("0.25", "0.3")]
    [InlineData("0.25", "0.1")]
    [InlineData("1", "0.5")]
    [InlineData("0.05", "0.12")]
    public void MustFire_ForAnyHoursOffTheGrid_ByTheSmallestMargin(string increment, string hours)
    {
        Run(IncrementRule(increment), FactsWithHours(D(hours))).Should().ContainSingle();
    }

    [Fact]
    public void ACreditThatIsAMultiple_Holds_AndOneThatIsNot_Fires()
    {
        Run(IncrementRule(), FactsWithHours(-0.3m)).Should().BeEmpty();
        Run(IncrementRule(), FactsWithHours(-0.35m)).Should().ContainSingle();
    }

    [Fact]
    public void ALineWithNoHours_IsNotJudged_AndExpenseLinesAreNotJudged()
    {
        Run(IncrementRule(), FactsWithHours(null)).Should().BeEmpty();
        Run(IncrementRule(), Facts(Expense(0) with { Hours = 0.37m })).Should().BeEmpty();
    }

    [Fact]
    public void ReadFields_AreTheHours_TheDateAndTheTimekeeper()
    {
        Run(IncrementRule(), Facts(Fee(2) with { Hours = 0.25m })).Single().ReadFields.Should().Equal(
            new FieldRef(2, BillingFieldNames.Hours), new FieldRef(2, BillingFieldNames.Date), new FieldRef(2, BillingFieldNames.TimekeeperAsBilled));
    }

    [Fact]
    public void ALowConfidenceHours_IsFlagged()
    {
        var line = Fee(0) with { Hours = 0.25m, FieldConfidence = Confidence().With(BillingFieldNames.Hours, 0.70m) };

        Run(IncrementRule(), Facts(line)).Single().Value("line.hours").BelowReliabilityThreshold.Should().BeTrue();
    }

    [Fact]
    public void ARuleWithAnUnusableIncrement_IsRefused_NotDivided()
    {
        var rule = Corrupt(IncrementRule(), nameof(BillingRuleDefinition.IncrementHours), 0m);

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, FactsWithHours(0.3m));

        act.Should().Throw<BillingRuleRefusedException>().Which.ReasonCode.Should().Be(BillingRuleRefusedException.ParameterMissing);
    }
}
