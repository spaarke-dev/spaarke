using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using ProcessorHarness = Sprk.Bff.Api.Tests.Services.Finance.Submission.InvoiceSubmissionProcessorTests.Harness;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 130e-1 fix round 1 (F2c, uac-r2 C1): the re-run's rule Signal step writes every committed plan entry again
/// (<c>InvoiceSubmissionRerun.RemapSignalsAsync</c>), so each entry's firm reason is re-derived from its policy version, never taken from the plan.
/// Removing that replay call fails <see cref="Rerun_WritesTheVersionsText_NotThePlans"/>. Over the re-run's in-memory world; the requests the
/// writer receives are recorded.
/// </summary>
public sealed partial class InvoiceSubmissionRerunTests
{
    private const string StoredFirmText = "Late entries are not billable.";

    /// <summary>A committed re-run whose plan holds the aged-invoice rule's entries, each with a planted firmReason (and the held-fallback flag
    /// when <paramref name="heldFallback"/>); their policy version stores <see cref="StoredFirmText"/>.</summary>
    private static async Task<(World World, Guid Version, List<SignalWriteRequest> Written)> FirmReasonRerunAsync(bool heldFallback)
    {
        var world = await World.SeedAsync([InvoiceSubmissionProcessorTests.FeeLine(date: new DateOnly(2026, 1, 20))]);
        world.Processor = new ProcessorHarness { Channel = InvoiceSubmissionChannel.Internal, Rules = [InvoiceSubmissionProcessorTests.InvoiceAgeReject()] };
        (await world.RunAsync()).Status.Should().Be(InvoiceSubmissionRerunStatus.Completed, "the setup: the commit landed");
        var plan = InvoiceSubmissionAfterSteps.StoredFindings.Parse(world.Findings().ToJsonString())!.Plan;
        plan.Should().NotBeEmpty("the setup: the rule's finding is in the committed plan");
        var version = plan[0].Request.PolicyVersionId;
        world.PutFirmRows(version, "  " + StoredFirmText + " ");
        world.EditFindings(f =>
        {
            foreach (var entry in f["signalPlan"]!.AsArray().OfType<JsonObject>())
            {
                entry["firmReason"] = "PLANTED text from the plan.";
                if (heldFallback)
                {
                    entry[InvoiceSubmissionPersister.HeldFallbackMember] = true;
                }
            }
        });

        var written = new List<SignalWriteRequest>();
        world.SignalWriteFault = request =>
        {
            written.Add(request);
            return null;
        };
        return (world, version, written);
    }

    [Fact(DisplayName = "130e-1 F2c must-fire: a re-run's rule Signal carries its version's stored firm description, not a firmReason planted in the plan")]
    public async Task Rerun_WritesTheVersionsText_NotThePlans()
    {
        var (world, version, written) = await FirmReasonRerunAsync(heldFallback: false);

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        written.Where(r => r.PolicyVersionId == version).Should().NotBeEmpty()
            .And.OnlyContain(r => r.FirmReason == StoredFirmText);
    }

    [Fact(DisplayName = "130e-1 F2c + O2: a re-run of a held-fallback plan entry writes no firm reason although its version has a valid one")]
    public async Task Rerun_OfAHeldFallbackEntry_WritesNone()
    {
        var (world, version, written) = await FirmReasonRerunAsync(heldFallback: true);

        (await world.Rerun().RemapSignalsAsync(SubmissionId)).Status.Should().Be(InvoiceSubmissionRerunSignalsStatus.Done);

        written.Where(r => r.PolicyVersionId == version).Should().NotBeEmpty().And.OnlyContain(r => r.FirmReason == null);
    }

    internal sealed partial class World
    {
        private static readonly Guid FirmPolicyRow = Guid.Parse("130e0000-0000-4000-8000-0000000003f1");
        private static readonly Guid FirmRuleSetRow = Guid.Parse("130e0000-0000-4000-8000-0000000003f2");

        /// <summary>130e-1: the policy version <paramref name="version"/> with <paramref name="text"/>, its policy and its rule set (the replay's reads).</summary>
        public void PutFirmRows(Guid version, string text)
        {
            _rows[version] = new Entity("sprk_policyversion", version)
            {
                ["sprk_firmdescription"] = text,
                ["sprk_policy"] = new EntityReference("sprk_policy", FirmPolicyRow),
            };
            _rows[FirmPolicyRow] = new Entity("sprk_policy", FirmPolicyRow)
            {
                ["sprk_name"] = "Invoice age",
                ["sprk_ruleset"] = new EntityReference("sprk_ruleset", FirmRuleSetRow),
            };
            _rows[FirmRuleSetRow] = new Entity("sprk_ruleset", FirmRuleSetRow) { ["sprk_name"] = "Acme guidelines" };
        }
    }
}
