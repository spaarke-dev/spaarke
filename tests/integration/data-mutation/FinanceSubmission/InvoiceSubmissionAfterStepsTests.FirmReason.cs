using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 130e-1 (DEF-060, uac-r2 C1): the after-commit steps write every billing rule Signal from the stored Signal plan, and the
/// plan never carries the firm description. Each replayed entry re-reads its policy version's <c>sprk_firmdescription</c> as stored now and
/// validates it with that version's own rule set and policy names; a <c>firmReason</c> member planted in the plan is ignored. Over the after
/// steps' in-memory Dataverse (the real steps; the Signal writes are recorded).
/// </summary>
public partial class InvoiceSubmissionAfterStepsTests
{
    /// <summary>The AutoRejected world with one rule Signal in its plan; <paramref name="plant"/> adds a firmReason member to that entry.</summary>
    private static async Task<(World World, Guid Version, SubmissionEvaluationOutcome Outcome)> FirmReasonWorldAsync(string? plant)
    {
        var outcome = await AutoRejected();
        var version = Guid.Empty;
        var world = new World(outcome, json =>
        {
            var entry = (JsonObject)json["signalPlan"]!.AsArray().Single()!;
            version = Guid.Parse(entry["policyVersionId"]!.GetValue<string>());
            if (plant is not null)
            {
                entry["firmReason"] = plant;
            }

            return json;
        });
        return (world, version, outcome);
    }

    [Fact(DisplayName = "130e-1 C1 must-fire: a replayed rule Signal carries its version's stored firm description, not a firmReason planted in the plan")]
    public async Task Replay_WritesTheVersionsText_NotThePlans()
    {
        var (world, version, outcome) = await FirmReasonWorldAsync(plant: "PLANTED text from the plan.");
        world.FirmDescriptions[version] = "  Late entries are not billable.  ";

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-130e");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Writes.Should().ContainSingle().Which.FirmReason.Should().Be("Late entries are not billable.");
    }

    [Fact(DisplayName = "130e-1 C1 must-not-fire: a version with no firm description gives a Signal with none, whatever the plan says")]
    public async Task Replay_WithNoStoredText_WritesNone()
    {
        var (world, _, outcome) = await FirmReasonWorldAsync(plant: "PLANTED text from the plan.");

        (await world.Build().RunAsync(Persisted(outcome), "corr-130e")).Status.Should().Be(SubmissionAfterStepsStatus.Completed);

        world.Writes.Should().ContainSingle().Which.FirmReason.Should().BeNull();
    }

    [Theory(DisplayName = "130e-1 C3/C4: a stored text that names the rule set or policy, or fails the rules, is dropped on replay; the Signal is still written, a code-only Warning logged")]
    [InlineData("Under the OUTSIDE COUNSEL GUIDELINES this is late.", "firm_description_names_rule")]
    [InlineData("See travel expenses.", "firm_description_names_rule")]
    [InlineData("Late {entry}.", "firm_description_braces")]
    public async Task Replay_DropsAnInvalidText_AndStillWritesTheSignal(string stored, string reason)
    {
        var (world, version, outcome) = await FirmReasonWorldAsync(plant: null);
        world.FirmDescriptions[version] = stored;

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-130e");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Writes.Should().ContainSingle().Which.FirmReason.Should().BeNull();
        world.Log.Entries.Where(e => e.Message.Contains(Sprk.Bff.Api.Services.Finance.FirmDescriptionRules.DroppedLogCode)).Should().ContainSingle(
            "fix round 1 F2b: the drop is logged once per entry (by the replay; the persister's mapping passes no logger)");
        world.Log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
            && e.Message.Contains(Sprk.Bff.Api.Services.Finance.FirmDescriptionRules.DroppedLogCode) && e.Message.Contains(reason) && !e.Message.Contains(stored));
    }

    /// <summary>The Blocked world, its one entry turned into a held group's preflight fallback: the no-value sentence, and the flag when
    /// <paramref name="flag"/> (a plan written before the flag existed carries the sentence only).</summary>
    private static async Task<(World World, Guid Version, SubmissionEvaluationOutcome Outcome)> HeldFallbackWorldAsync(bool flag)
    {
        var outcome = await Blocked();
        var version = Guid.Empty;
        var world = new World(outcome, json =>
        {
            var entry = (JsonObject)json["signalPlan"]!.AsArray().Single()!;
            version = Guid.Parse(entry["policyVersionId"]!.GetValue<string>());
            entry["template"] = BillingFindingSignalMapper.HeldSentenceNoValue;
            entry["sentence"] = BillingFindingSignalMapper.HeldSentenceNoValue;
            if (flag)
            {
                entry[InvoiceSubmissionPersister.HeldFallbackMember] = true;
            }

            return json;
        });
        return (world, version, outcome);
    }

    [Theory(DisplayName = "130e-1 fix round 1 O2 (ontology test 3): a held-fallback plan entry is written with no firm reason although its version has a valid one; old plans too")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Replay_OfAHeldFallbackEntry_WritesNoFirmReason(bool flag)
    {
        var (world, version, outcome) = await HeldFallbackWorldAsync(flag);
        world.FirmDescriptions[version] = "Late entries are not billable.";

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-130e");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        var written = world.Writes.Should().ContainSingle().Subject;
        written.FirmReason.Should().BeNull("the first write of a held fallback carried none; the replay writes the same Signal");
        written.MessageTemplate.Should().Be(BillingFindingSignalMapper.HeldSentenceNoValue);
        world.Entities.Invocations.Should().NotContain(i => i.Method.Name == "RetrieveAsync" && (string)i.Arguments[0] == BillingFactLoader.VersionEntity,
            "a held fallback never reads its version");
    }

    [Fact(DisplayName = "130e-1 fix round 1 O2 (ontology test 4) control: the same Blocked world's normal held entry still takes the version's text")]
    public async Task Replay_OfANormalHeldEntry_TakesTheVersionsText()
    {
        var outcome = await Blocked();
        var version = Guid.Empty;
        var world = new World(outcome, json =>
        {
            version = Guid.Parse(((JsonObject)json["signalPlan"]!.AsArray().Single()!)["policyVersionId"]!.GetValue<string>());
            return json;
        });
        world.FirmDescriptions[version] = "Late entries are not billable.";

        var result = await world.Build().RunAsync(Persisted(outcome), "corr-130e");

        result.Status.Should().Be(SubmissionAfterStepsStatus.Completed, result.ReasonCode);
        world.Writes.Should().ContainSingle().Which.FirmReason.Should().Be("Late entries are not billable.");
    }

    [Fact(DisplayName = "130e-1: the replay reads the version once per run, and the names only when there is a text to check")]
    public async Task Replay_ReadsOnlyWhatItNeeds()
    {
        var (world, version, outcome) = await FirmReasonWorldAsync(plant: null);

        await world.Build().RunAsync(Persisted(outcome), "corr-130e");

        world.Entities.Invocations.Count(i => i.Method.Name == "RetrieveAsync" && (string)i.Arguments[0] == BillingFactLoader.VersionEntity && (Guid)i.Arguments[1] == version)
            .Should().Be(1);
        world.Entities.Invocations.Should().NotContain(i => i.Method.Name == "RetrieveAsync" && (string)i.Arguments[0] == BillingFactLoader.PolicyEntity,
            "no text, so no name check and no policy read");
    }
}
