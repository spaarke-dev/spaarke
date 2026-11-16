using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Procedures;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060): a record-rule change copies the base version in force with the edited members; the base version's firm
/// description travels with the copy (the change edits no firm description), so a Signal raised under the record rule shows the same text.
/// Over the assignment service's in-memory Dataverse (the real service).
/// </summary>
public sealed partial class ProcedureAssignmentServiceTests
{
    [Fact(DisplayName = "130e-1: a record-rule change copies the base version's firm description onto the record rule's version")]
    public async Task Change_CopiesTheBaseFirmDescription()
    {
        var w = new World();
        var policy = w.Rule();
        w.H.Row("sprk_policyversion", CurrentVersionOf(w, policy))[RuleSetService.FirmDescriptionColumn] = "Travel is billed at half the agreed rate.";

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (_, ruleId) = RecordRuleOf(policy);
        w.H.Row("sprk_policyversion", RuleSetService.VersionIdFor(ruleId, 1)).GetAttributeValue<string>(RuleSetService.FirmDescriptionColumn)
            .Should().Be("Travel is billed at half the agreed rate.");
    }

    [Theory(DisplayName = "130e-1 F1 must-fire: a base version whose stored text now fails the checks gives a copy without it (code-only Warning); the change lands")]
    [InlineData("Rate {{line.rate}} too high.", FirmDescriptionRules.Braces)]
    [InlineData("<b>Half</b> rate.", FirmDescriptionRules.Markup)]
    public async Task Change_OfABaseWithAnInvalidText_DropsIt(string stored, string reason)
    {
        var w = new World();
        var policy = w.Rule();
        w.H.Row("sprk_policyversion", CurrentVersionOf(w, policy))[RuleSetService.FirmDescriptionColumn] = stored;

        var outcome = await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var (_, ruleId) = RecordRuleOf(policy);
        w.H.Row("sprk_policyversion", RuleSetService.VersionIdFor(ruleId, 1)).Contains(RuleSetService.FirmDescriptionColumn).Should().BeFalse();
        var entry = w.Log.Entries.Should().ContainSingle(e => e.Message.Contains(FirmDescriptionRules.DroppedLogCode)).Subject;
        entry.Message.Should().Contain(reason).And.NotContain(stored);
    }

    [Fact(DisplayName = "130e-1 must-not-fire: a base version with no firm description gives a copy with none (no empty column is written)")]
    public async Task Change_OfABaseWithoutAFirmDescription_WritesNone()
    {
        var w = new World();
        var policy = w.Rule();

        (await w.Service().ChangeRuleAsync(Matter, MatterId, Change(policy), CallerOid, Token, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);

        var (_, ruleId) = RecordRuleOf(policy);
        w.H.Row("sprk_policyversion", RuleSetService.VersionIdFor(ruleId, 1)).Contains(RuleSetService.FirmDescriptionColumn).Should().BeFalse();
    }
}
