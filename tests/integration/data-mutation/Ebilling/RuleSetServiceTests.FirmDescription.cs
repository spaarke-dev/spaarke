using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060, uac-r2 C2 and C4, Q4): the firm description on a billing policy version, through the rule set service's
/// writes. It is set on a draft in place; on a published version an edit lands on the new draft it creates and the published row keeps its text (F1); a text
/// that fails <see cref="FirmDescriptionRules"/> or names the rule set or the policy is refused 422 at edit and at publish, never echoed;
/// a Duplicate rule takes none; a new version, a copy forward and a record-rule change carry it.
/// Over the rule set service's in-memory Dataverse (<see cref="Harness"/>, the real service).
/// </summary>
public sealed partial class RuleSetServiceTests
{
    private const string FirmText = "Travel is billed at half the agreed rate.";

    private static ProposalEdit FirmEdit(string? firmDescription, string? headline = null) =>
        new(null, null, null, headline, null, null, null, CallerOid, firmDescription);

    private static string? FirmDescriptionOf(Harness h, Guid version) =>
        h.Row("sprk_policyversion", version).GetAttributeValue<string>(RuleSetService.FirmDescriptionColumn);

    [Fact(DisplayName = "130e-1 C2 must-not-fire: a draft's firm description is set in place, trimmed; the read model returns it")]
    public async Task Draft_TakesTheFirmDescription_InPlace()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().EditAsync(set, policy, FirmEdit("  " + FirmText + "  "), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CreatedNewVersion.Should().BeFalse();
        outcome.Value.Proposal.FirmDescription.Should().Be(FirmText);
        FirmDescriptionOf(h, version).Should().Be(FirmText);
        h.WritesOf("sprk_policyversion").Should().ContainSingle().Which.Fields[RuleSetService.FirmDescriptionColumn].Should().Be(FirmText);
        (await h.Service().GetAsync(set, default)).Value!.Proposals.Single().FirmDescription.Should().Be(FirmText);
    }

    [Fact(DisplayName = "130e-1 C2: a blank firm description clears a draft's; the same text again is no change and writes nothing")]
    public async Task Draft_BlankClears_AndTheSameTextIsNoChange()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = FirmText;

        (await h.Service().EditAsync(set, policy, FirmEdit(FirmText), default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        h.WritesOf("sprk_policyversion").Should().AllSatisfy(w => w.Fields.Should().NotContainKey(RuleSetService.FirmDescriptionColumn));

        (await h.Service().EditAsync(set, policy, FirmEdit("   "), default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        FirmDescriptionOf(h, version).Should().BeNull();
    }

    [Theory(DisplayName = "130e-1 C2 + F1: a firm description change on a published version (a clear included) lands on the NEW draft; the published row is never written")]
    [InlineData("A different description.", null, "A different description.")]
    [InlineData("", "Changed too", null)]
    [InlineData("   ", null, null)]
    public async Task Published_FirmDescriptionChange_LandsOnTheNewDraft_AndThePublishedRowIsUntouched(string firm, string? headline, string? expected)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: true);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = FirmText;

        var outcome = await h.Service().EditAsync(set, policy, FirmEdit(firm, headline), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        outcome.Value.Proposal.FirmDescription.Should().Be(expected);
        var writes = h.WritesOf("sprk_policyversion");
        writes.Should().OnlyContain(w => w.Kind == Write.Create, "the published version is never updated");
        var created = writes.Single().Fields;
        if (expected is null)
        {
            created.Should().NotContainKey(RuleSetService.FirmDescriptionColumn);
        }
        else
        {
            created[RuleSetService.FirmDescriptionColumn].Should().Be(expected);
        }

        FirmDescriptionOf(h, version).Should().Be(FirmText, "C2: the published version's own text never changes");
    }

    [Theory(DisplayName = "130e-1 F1 must-fire: a published version's stored text that now fails the checks is left off the new draft (code-only Warning) and never blocks an edit that does not touch it")]
    [InlineData("Rate {{line.rate}} too high.", FirmDescriptionRules.Braces)]
    [InlineData("Per the ACME GUIDELINES.", FirmDescriptionRules.NamesTheRule)]
    [InlineData("Travel is billed at half rate.", null)]
    public async Task Published_InvalidStoredText_IsDroppedFromTheNewDraft_AndTheEditLands(string stored, string? dropped)
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: true);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = stored;
        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "A different headline"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        var created = h.WritesOf("sprk_policyversion").Single(w => w.Kind == Write.Create).Fields;
        FirmDescriptionOf(h, version).Should().Be(stored, "the published row keeps its text either way");
        if (dropped is null)
        {
            created[RuleSetService.FirmDescriptionColumn].Should().Be(stored, "must-not-fire: a valid text is carried forward");
            h.Logs.Should().NotContain(e => e.Message.Contains(FirmDescriptionRules.DroppedLogCode));
            return;
        }

        created.Should().NotContainKey(RuleSetService.FirmDescriptionColumn);
        outcome.Value!.Proposal.FirmDescription.Should().BeNull();
        var entry = h.Logs.Should().ContainSingle(e => e.Message.Contains(FirmDescriptionRules.DroppedLogCode)).Subject;
        entry.Message.Should().Contain(dropped).And.NotContain(stored);
    }

    [Fact(DisplayName = "130e-1 F2d: the Rule Review view and accept check the rule set's own name, so a draft naming it shows the error before publish")]
    public async Task ViewAndAccept_CheckTheRuleSetName()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Proposed);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = "Per the acme GUIDELINES section 4.";

        var view = (await h.Service().GetAsync(set, default)).Value!.Proposals.Single();
        view.ValidationErrors.Should().ContainSingle().Which.Should().NotContain("acme GUIDELINES", "the text is never echoed");

        var accepted = await h.Service().DecideAsync(set, policy, true, h.WorkingToken(policy), default);
        accepted.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        accepted.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        accepted.Errors.Should().ContainSingle(e => e.Field == "firmDescription").Which.Reason.Should().Be(FirmDescriptionRules.NamesTheRule);
        h.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "130e-1 C2: an edit of a published version that leaves the firm description alone creates the new version carrying it forward")]
    public async Task Published_OtherEdit_CarriesTheFirmDescriptionToTheNewVersion()
    {
        var h = new Harness();
        var set = h.RuleSet(RuleSetService.RuleSetPublished);
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted, current: true);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = FirmText;

        var outcome = await h.Service().EditAsync(set, policy, Edit(headline: "A different headline"), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Done, outcome.Detail);
        outcome.Value!.CreatedNewVersion.Should().BeTrue();
        outcome.Value.Proposal.FirmDescription.Should().Be(FirmText);
        h.WritesOf("sprk_policyversion").Single(w => w.Kind == Write.Create).Fields[RuleSetService.FirmDescriptionColumn].Should().Be(FirmText);
        FirmDescriptionOf(h, version).Should().Be(FirmText, "the published version is untouched");
    }

    [Theory(DisplayName = "130e-1 C4/Q4: a draft edit whose firm description fails the rules or names the rule set or policy is refused 422 with the reason code, text never echoed, nothing written")]
    [InlineData("Billed against Acme Guidelines policy.", FirmDescriptionRules.NamesTheRule)]   // the rule set's own name, another case
    [InlineData("See RULE bill-a for the cap.", FirmDescriptionRules.NamesTheRule)]             // the policy's own name, another case
    [InlineData("Rate {{line.rate}} too high.", FirmDescriptionRules.Braces)]
    [InlineData("<script>x</script>", FirmDescriptionRules.Markup)]
    [InlineData("Rate\u200B too high.", FirmDescriptionRules.InvisibleCharacter)]
    public async Task Draft_InvalidFirmDescription_IsRefused422(string text, string reason)
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted);

        var outcome = await h.Service().EditAsync(set, policy, FirmEdit(text), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        outcome.ErrorCode.Should().Be(RuleSetService.CodeInvalid);
        var error = outcome.Errors.Should().ContainSingle(e => e.Field == "firmDescription").Subject;
        error.Reason.Should().Be(reason);
        error.Message.Should().NotContain(text);
        h.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "130e-1 Q4: publish refuses a version whose stored firm description names its rule set: 422 publish_invalid with the reason code; nothing is put in force")]
    public async Task Publish_ANameBearingFirmDescription_IsRefused422()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (_, version) = h.Rule(set, state: ProposalState.Accepted);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = "Per the acme GUIDELINES section 4.";

        var outcome = await h.Service().PublishAsync(set, Effective, default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        outcome.ErrorCode.Should().Be(RuleSetService.CodePublishInvalid);
        outcome.Errors.Should().ContainSingle(e => e.Field == "firmDescription").Which.Reason.Should().Be(FirmDescriptionRules.NamesTheRule);
        outcome.Errors.Should().OnlyContain(e => !e.Message.Contains("acme GUIDELINES", StringComparison.OrdinalIgnoreCase));
        h.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "130e-1 Q4 must-not-fire: a publish whose only problem is not the firm description stays 400; a clean firm description publishes")]
    public async Task Publish_OtherErrorsStay400_AndACleanDescriptionPublishes()
    {
        var bad = new Harness();
        var badSet = bad.RuleSet();
        bad.Rule(badSet, state: ProposalState.Accepted, body: """{"type":"DailyHoursCap","maxHoursPerDay":99}""");
        var refused = await bad.Service().PublishAsync(badSet, Effective, default);
        refused.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        refused.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        var h = new Harness();
        var set = h.RuleSet();
        var (_, version) = h.Rule(set, state: ProposalState.Accepted);
        h.Row("sprk_policyversion", version)[RuleSetService.FirmDescriptionColumn] = FirmText;
        (await h.Service().PublishAsync(set, Effective, default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        FirmDescriptionOf(h, version).Should().Be(FirmText, "publish freezes the text with the version; it never rewrites it");
    }

    [Fact(DisplayName = "130e-1 design 5: a Duplicate rule takes no firm description (firm_description_duplicate, 422)")]
    public async Task Duplicate_TakesNoFirmDescription()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, _) = h.Rule(set, state: ProposalState.Accepted, type: "Duplicate",
            body: """{"type":"Duplicate","matchFields":["firmInvoiceNumber","timekeeper","lineDate","hours"]}""");

        var outcome = await h.Service().EditAsync(set, policy, FirmEdit(FirmText), default);

        outcome.Kind.Should().Be(RuleSetOutcomeKind.Invalid);
        outcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        outcome.Errors.Should().Contain(e => e.Field == "firmDescription" && e.Reason == "firm_description_duplicate");
        h.Writes.Should().BeEmpty();
    }

    [Fact(DisplayName = "130e-1: a copy forward to the next Procedure Version carries each published rule's firm description onto its Draft copy")]
    public async Task CopyForward_CarriesTheFirmDescription()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, version) = h.Rule(set, state: ProposalState.Accepted);
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var row = h.Row("sprk_policyversion", version);
        row[RuleSetService.ProcedureVersionColumn] = new EntityReference("sprk_procedureversion", from);
        row[RuleSetService.PublishStateColumn] = new OptionSetValue(RuleSetService.PublishStatePublished);
        row[RuleSetService.FirmDescriptionColumn] = FirmText;
        h.Row("sprk_policy", policy)["sprk_enabled"] = true; // a switched-off rule is never copied

        var plan = await h.Service().PlanCopyForwardAsync(set, from, to, RecordOwnerResolution.Owned(TeamId), default);

        plan.Errors.Should().BeEmpty();
        var copy = plan.Creates.Should().ContainSingle().Which.Should().BeOfType<CreateRequest>().Subject.Target;
        copy.GetAttributeValue<EntityReference>("sprk_policy").Id.Should().Be(policy);
        copy.GetAttributeValue<string>(RuleSetService.FirmDescriptionColumn).Should().Be(FirmText);
    }

    [Fact(DisplayName = "130e-1 EB-D-216: an inactive draft version is never read: its firm description is not the rule's, and an edit never reaches it")]
    public async Task AnInactiveDraft_IsNotRead()
    {
        var h = new Harness();
        var set = h.RuleSet();
        var (policy, active) = h.Rule(set, state: ProposalState.Accepted);
        var inactive = h.AddVersion(policy, 2, ProposalState.Accepted);
        h.Row("sprk_policyversion", inactive)[RuleSetService.FirmDescriptionColumn] = "INACTIVE text.";
        h.Row("sprk_policyversion", inactive)["statecode"] = new OptionSetValue(1);

        var proposal = (await h.Service().GetAsync(set, default)).Value!.Proposals.Single();
        proposal.VersionId.Should().Be(active);
        proposal.FirmDescription.Should().BeNull();

        (await h.Service().EditAsync(set, policy, FirmEdit(FirmText), default)).Kind.Should().Be(RuleSetOutcomeKind.Done);
        FirmDescriptionOf(h, active).Should().Be(FirmText);
        FirmDescriptionOf(h, inactive).Should().Be("INACTIVE text.");
    }
}
