using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 082b (procedure model 7.3; ontology D-172 Q7 "limits replace, checklists add"; <c>notes/rule-set-scope-design.md</c> 3.3):
/// <see cref="BillingRuleCatalogue.PrecedenceOf"/> classifies every billing type as Replace, Additive or Split, and throws on any other type.
/// Maintain-class (ADR-038, <c>tests/unit/domain/**</c>): pure logic. The pinned list makes a new <see cref="RuleType"/> member fail here
/// until it is classified on purpose.
/// </summary>
public class BillingRulePrecedenceTests
{
    /// <summary>The reviewed table (rule-set-scope-design.md 3.3), one row per billing member.</summary>
    public static readonly TheoryData<RuleType, BillingRulePrecedence> Pinned = new()
    {
        { RuleType.ApprovedTimekeeper, BillingRulePrecedence.Additive },
        { RuleType.ApprovedRate, BillingRulePrecedence.Replace },
        { RuleType.TimeIncrement, BillingRulePrecedence.Replace },
        { RuleType.DailyHoursCap, BillingRulePrecedence.Replace },
        { RuleType.NonBillableActivity, BillingRulePrecedence.Additive },
        { RuleType.ExpenseRule, BillingRulePrecedence.Split },
        { RuleType.InvoiceAge, BillingRulePrecedence.Replace },
        { RuleType.CoreRecordWindow, BillingRulePrecedence.Replace },
        { RuleType.Duplicate, BillingRulePrecedence.Replace },
        { RuleType.RequiredData, BillingRulePrecedence.Additive },
        { RuleType.AiObservation, BillingRulePrecedence.Additive },
        { RuleType.SubmissionIntegrity, BillingRulePrecedence.Additive },
        { RuleType.AddedFee, BillingRulePrecedence.Replace },
        { RuleType.Discount, BillingRulePrecedence.Replace },
    };

    [Theory(DisplayName = "082b precedence: every billing type has its reviewed classification")]
    [MemberData(nameof(Pinned))]
    public void PrecedenceOf_ABillingType_IsItsReviewedClass(RuleType type, BillingRulePrecedence expected) =>
        BillingRuleCatalogue.PrecedenceOf(type).Should().Be(expected);

    [Fact(DisplayName = "082b precedence: the pinned list is exactly the billing members of RuleType (a new member must be classified)")]
    public void PrecedenceOf_PinnedList_CoversEveryBillingMember()
    {
        var pinned = Pinned.Select(row => (RuleType)row[0]).ToList();
        var billing = Enum.GetValues<RuleType>().Where(RuleDomains.IsBilling).ToList();

        pinned.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(billing);
    }

    [Fact(DisplayName = "082b precedence: ExpenseRule is the only Split type (caps replace, the code checklist adds)")]
    public void PrecedenceOf_OnlyExpenseRule_IsSplit() =>
        Enum.GetValues<RuleType>().Where(RuleDomains.IsBilling)
            .Where(t => BillingRuleCatalogue.PrecedenceOf(t) == BillingRulePrecedence.Split)
            .Should().Equal(RuleType.ExpenseRule);

    [Theory(DisplayName = "082b precedence: a non-billing type throws (the switch is closed)")]
    [InlineData(RuleType.Threshold)]
    [InlineData(RuleType.Switch)]
    [InlineData(RuleType.Existence)]
    [InlineData((RuleType)99)]
    public void PrecedenceOf_ANonBillingType_Throws(RuleType type)
    {
        var classify = () => BillingRuleCatalogue.PrecedenceOf(type);

        classify.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*Not a billing rule type*");
    }

    [Fact(DisplayName = "082c C20: the overlap census script's pinned checklist types are exactly the non-Replace, non-system billing types of PrecedenceOf")]
    public void TheOverlapScriptsChecklistTypes_AreThoseOfPrecedenceOf()
    {
        var script = File.ReadAllText(FindFromRepoRoot("scripts", "ebilling", "Get-EbillingLegacyRuleOverlap.ps1"));
        var line = System.Text.RegularExpressions.Regex.Match(script, @"^\$ChecklistRuleTypes = @\{(?<pairs>[^}]*)\}", System.Text.RegularExpressions.RegexOptions.Multiline);
        line.Success.Should().BeTrue("the script pins the list on one line");
        var pinned = System.Text.RegularExpressions.Regex.Matches(line.Groups["pairs"].Value, @"(?<value>\d+)\s*=\s*'(?<name>\w+)'")
            .Select(m => (int.Parse(m.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups["name"].Value))
            .ToList();

        var expected = Enum.GetValues<RuleType>()
            .Where(t => RuleDomains.IsBilling(t) && !BillingRuleCatalogue.IsSystemType(t) && BillingRuleCatalogue.PrecedenceOf(t) != BillingRulePrecedence.Replace)
            .Select(t => ((int)t, t.ToString()));

        pinned.Should().BeEquivalentTo(expected);
    }

    private static string FindFromRepoRoot(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine([dir.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(Path.Combine(parts));
    }
}
