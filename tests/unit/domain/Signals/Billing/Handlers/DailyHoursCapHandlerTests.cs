using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 4 DailyHoursCap (design.md 6.1; within the invoice in R1). Maintain-class (ADR-038): pure
/// logic, no mocks.
/// </summary>
public class DailyHoursCapHandlerTests
{
    private static readonly BillingRuleDefinition Cap12 = Rule("""{"type":"DailyHoursCap","maxHoursPerDay":12}""");

    private static readonly Guid Jane = Guid.Parse("07100000-0000-4000-8000-0000000000e1");
    private static readonly Guid Omar = Guid.Parse("07100000-0000-4000-8000-0000000000e2");

    private static BillingLineFacts Billed(int index, decimal hours, Guid? who = null, string name = "Jane Doe", DateOnly? date = null) =>
        Fee(index) with { Hours = hours, Timekeeper = Matched(who ?? Jane), TimekeeperName = name, Date = date ?? LineDate };

    [Fact]
    public void MustFire_WhenOneTimekeepersHoursOnADayExceedTheCap_AsOneFindingOnTheFirstLineOfTheDay()
    {
        var facts = Facts(Billed(0, 7m), Billed(1, 4m), Billed(2, 1.5m));

        var finding = Run(Cap12, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.DailyHoursOverCap);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.Value("dailyHours").Value.Should().Be("12.5");
        finding.FactValues.Should().Contain("dailyHours", "12.5").And.Contain("rule.maxHoursPerDay", "12");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.DailyHoursCap));
        finding.SuggestedAdjustment.Should().BeNull();
    }

    [Fact]
    public void MustNotFire_AtExactlyTheCap()
    {
        Run(Cap12, Facts(Billed(0, 8m), Billed(1, 4m))).Should().BeEmpty();
    }

    [Fact]
    public void MustFire_OneHundredthOfAnHourOverTheCap()
    {
        Run(Cap12, Facts(Billed(0, 8m), Billed(1, 4.01m))).Should().ContainSingle();
    }

    [Fact]
    public void ReadFields_AreTheHoursTheDateAndTheTimekeeperOfEveryLineOfTheDay()
    {
        var facts = Facts(Billed(0, 7m), Billed(3, 6m));

        Run(Cap12, facts).Single().ReadFields.Should().Equal(
            new FieldRef(0, BillingFieldNames.Hours), new FieldRef(0, BillingFieldNames.Date), new FieldRef(0, BillingFieldNames.TimekeeperAsBilled),
            new FieldRef(3, BillingFieldNames.Hours), new FieldRef(3, BillingFieldNames.Date), new FieldRef(3, BillingFieldNames.TimekeeperAsBilled));
    }

    [Fact]
    public void TwoTimekeepersWhoAreEachUnderTheCap_DoNotAddUp()
    {
        Run(Cap12, Facts(Billed(0, 8m, Jane), Billed(1, 8m, Omar, "Omar Haddad"))).Should().BeEmpty();
    }

    [Fact]
    public void TheSameTimekeeperOnTwoDays_DoesNotAddUp()
    {
        Run(Cap12, Facts(Billed(0, 8m), Billed(1, 8m, date: LineDate.AddDays(1)))).Should().BeEmpty();
    }

    [Fact]
    public void TwoSpellingsOfOneRosterTimekeeper_CountTogether()
    {
        var facts = Facts(Billed(0, 7m, Jane, "Jane Doe"), Billed(1, 6m, Jane, "J. Doe"));

        Run(Cap12, facts).Should().ContainSingle();
    }

    [Fact]
    public void UnmatchedLines_GroupByTheBilledName_IgnoringCaseAndWhitespace()
    {
        var unmatched = Unmatched(TimekeeperUnmatchedReason.NotOnRoster);
        var facts = Facts(
            Fee(0) with { Timekeeper = unmatched, TimekeeperName = "Pat  Kim", Hours = 7m },
            Fee(1) with { Timekeeper = unmatched, TimekeeperName = "  PAT KIM ", Hours = 6m });

        Run(Cap12, facts).Should().ContainSingle();
    }

    [Fact]
    public void TwoDifferentUnmatchedNames_DoNotAddUp()
    {
        var unmatched = Unmatched(TimekeeperUnmatchedReason.NotOnRoster);
        var facts = Facts(
            Fee(0) with { Timekeeper = unmatched, TimekeeperName = "Pat Kim", Hours = 8m },
            Fee(1) with { Timekeeper = unmatched, TimekeeperName = "Pam Kim", Hours = 8m });

        Run(Cap12, facts).Should().BeEmpty();
    }

    [Fact]
    public void LinesThatCannotBeGrouped_AreNotJudged()
    {
        var unmatched = Unmatched(TimekeeperUnmatchedReason.NameMissing);
        var facts = Facts(
            Billed(0, 20m) with { Date = null },
            Billed(1, 20m) with { Hours = null },
            Fee(2) with { Timekeeper = unmatched, TimekeeperName = null, Hours = 20m },
            Expense(3) with { Hours = 20m });

        Run(Cap12, facts).Should().BeEmpty();
    }

    [Fact]
    public void ACreditOnTheSameDay_OffsetsTheHours()
    {
        Run(Cap12, Facts(Billed(0, 13m), Billed(1, -2m))).Should().BeEmpty();
    }

    [Fact]
    public void TwoDaysOverTheCap_GiveTwoFindings_InTheOrderTheDaysFirstAppear()
    {
        var facts = Facts(
            Billed(0, 13m, date: LineDate.AddDays(1)),
            Billed(1, 13m, Omar, "Omar Haddad"),
            Billed(2, 1m, date: LineDate.AddDays(1)));

        var findings = Run(Cap12, facts);

        findings.Select(f => f.Subject.LineIndex).Should().Equal(0, 1);
    }
}
