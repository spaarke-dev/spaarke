using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 101 fix round, part 3 (F-2): a correction whose findings step never finished is recorded as such (<c>appliedCorrections</c>), the next
/// correction of the field diffs from the last APPLIED value (and a timekeeper or date correction from the last applied roster match and rate), and a touched
/// line with no stored extraction keeps its status. Over <see cref="CorrectionWorld"/>'s in-memory Dataverse and 072's real evaluator. The approval guard
/// itself is in <see cref="InvoiceCorrectionEndToEndTests"/>.
/// </summary>
public sealed class InvoiceCorrectionDurabilityTests
{
    private const int FeeIndex = 2; // CorrectionWorld's fee line is sequence 3

    private static readonly BillingRuleCandidate Rate = Kit.Version("BILL-RATE", RuleType.ApprovedRate);
    private static readonly BillingRuleCandidate Atk = Kit.Version("BILL-ATK", RuleType.ApprovedTimekeeper);
    private static readonly BillingRuleCandidate Age = Kit.Version("BILL-AGE", RuleType.InvoiceAge);

    private static CorrectionCommand Line(string field, string? value) => new(W.InvoiceId, W.FeeLineId, field, value, false, W.Reviewer);

    private static InvoiceCorrectionService Service(CorrectionWorld world, ILogger<InvoiceCorrectionReevaluation>? log = null, params BillingRuleCandidate[] rules)
    {
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            log ?? NullLogger<InvoiceCorrectionReevaluation>.Instance);
        world.Seam = reevaluation.ApplyAsync;
        return world.Service();
    }

    private static JsonObject Root(params JsonObject[] findings)
    {
        var root = JsonNode.Parse(W.HeaderFindingsJson)!.AsObject();
        root["schema"] = 1;
        root["kind"] = "Accepted";
        root["invoiceId"] = W.InvoiceId.ToString("D");
        root["trackingNumber"] = "INV-1001.1";
        root["receivedOn"] = "2026-10-01";
        root["invoiceName"] = "INV-1001";
        root["findings"] = new JsonArray(findings.Select(f => (JsonNode)f).ToArray());
        root["pipelineFindings"] = new JsonArray();
        root["codingFindings"] = new JsonArray();
        root["lineFindings"] = new JsonArray();
        root["signalPlan"] = new JsonArray();
        return root;
    }

    private static void Store(CorrectionWorld world, JsonObject root) => world.Submission!["sprk_findingsjson"] = root.ToJsonString();

    private static JsonObject Stored(CorrectionWorld world) => JsonNode.Parse(world.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

    private static JsonObject Finding(int index, BillingRuleCandidate rule, RuleType type, int line, string reason) => new()
    {
        ["index"] = index,
        ["ruleType"] = type.ToString(),
        ["policyId"] = rule.PolicyId.ToString("D"),
        ["policyCode"] = rule.PolicyCode,
        ["policyVersionId"] = rule.PolicyVersionId.ToString("D"),
        ["subject"] = "Line",
        ["line"] = line,
        ["reasonCode"] = reason,
        ["consequence"] = "Flag",
    };

    private static IEnumerable<string> Reasons(JsonObject root) =>
        (root["findings"] as JsonArray ?? []).OfType<JsonObject>().Select(f => f["reasonCode"]!.GetValue<string>()).OrderBy(r => r);

    private static List<Guid> Applied(JsonObject root) =>
        (root[InvoiceCorrectionReevaluation.AppliedCorrectionsMember] as JsonArray ?? []).Select(n => Guid.Parse(n!.GetValue<string>())).ToList();

    private static int StatusOf(CorrectionWorld world) => world.Line().GetAttributeValue<OptionSetValue>("sprk_linestatus").Value;

    private static bool WritesFindings(IReadOnlyList<OrganizationRequest> requests) =>
        requests.OfType<UpdateRequest>().Any(u => u.Target.LogicalName == "sprk_invoicesubmission");

    /// <summary>The findings write fails ONCE (the next one goes through), as when the client gets the 503 Incomplete and never repeats.</summary>
    private static void FailNextFindingsWrite(CorrectionWorld world)
    {
        var failed = false;
        world.TransactionFault = r =>
        {
            if (failed || !WritesFindings(r))
            {
                return null;
            }

            failed = true;
            return new TimeoutException("simulated");
        };
    }

    // =========================================================================================
    // Item 1 - appliedCorrections
    // =========================================================================================

    [Fact(DisplayName = "101 fix 3 F-2: appliedCorrections is written even when the patch is empty, and a repeat writes nothing more")]
    public async Task AppliedCorrections_AreWritten_OnAnEmptyPatch_AndARepeatAddsNothing()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var service = Service(world);

        var result = await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        var stored = Stored(world);
        Applied(stored).Should().Equal(row.Id);
        Reasons(stored).Should().BeEmpty("nothing in the findings changed");
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(1, "the empty patch still records the row as followed through");

        var repeat = await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(1, "a repeat finds the row already listed");
        Applied(Stored(world)).Should().Equal(row.Id);
    }

    [Fact(DisplayName = "101 fix 3 F-2: a correction whose findings step failed is not listed; its repeat lists it")]
    public async Task AFailedFindingsStep_LeavesTheRowUnlisted_UntilTheRepeat()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var service = Service(world);
        FailNextFindingsWrite(world);

        var first = await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        Applied(Stored(world)).Should().BeEmpty();
        (await InvoiceCorrectionReevaluation.ReadUnappliedAsync(world.Entities.Object, W.InvoiceId, world.Lines, CancellationToken.None))
            .Should().ContainSingle().Which.Field.Should().Be("Narrative");

        (await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Applied(Stored(world)).Should().Equal(world.Corrections.Single().Id);
        (await InvoiceCorrectionReevaluation.ReadUnappliedAsync(world.Entities.Object, W.InvoiceId, world.Lines, CancellationToken.None)).Should().BeEmpty();
    }

    // =========================================================================================
    // Item 2 - the "before" side is the last APPLIED value
    // =========================================================================================

    [Fact(DisplayName = "101 fix 3 F-2 SEEDED: rate 150 (over the approved rate) -> 100 saved without its findings step, then 100 -> 105: the diff runs from 150, so the stale finding is removed")]
    public async Task ChainedRateCorrection_WhoseFirstFindingsStepFailed_DiffsFromTheLastAppliedValue()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        var line = world.Line();
        line["sprk_rate"] = new Money(150m); // as read: over the approved rate and its tolerance
        line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", lee.Id);
        line["sprk_approvedrate"] = new Money(110m);
        Store(world, Root(Finding(0, Rate, RuleType.ApprovedRate, FeeIndex, BillingReasonCodes.RateAboveApproved)));
        var service = Service(world, null, Rate);
        FailNextFindingsWrite(world);

        var first = await service.CorrectAsync(Line("Rate", "100"), CancellationToken.None);

        first.FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved);

        // 100 and 105 are both within the approved rate: diffed 100 -> 105 (the previous row, not the last applied one) nothing changes and the stale
        // finding stays; diffed from 150 (the value the stored findings reflect) it is removed.
        var second = await service.CorrectAsync(Line("Rate", "105"), CancellationToken.None);

        second.IsApplied.Should().BeTrue();
        var stored = Stored(world);
        Reasons(stored).Should().BeEmpty();
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Clean);
        Applied(stored).Should().BeEquivalentTo(world.Corrections.Select(c => c.Id), "the 100 row is followed through by the diff from 150");
    }

    [Fact(DisplayName = "101 fix 3 F-2: rate 100 -> 150 saved without its findings step, then 150 -> 160: the over-approved finding is added and both rows are followed through")]
    public async Task ChainedRateCorrection_WhoseFirstFindingsStepFailed_AddsTheFinding()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        var line = world.Line();
        line["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean);
        line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", lee.Id);
        line["sprk_approvedrate"] = new Money(110m); // billed 100: within the approved rate (and its tolerance), so the line is Clean; 150 and 160 are both over it
        Store(world, Root());
        var service = Service(world, null, Rate);
        FailNextFindingsWrite(world);

        var first = await service.CorrectAsync(Line("Rate", "150"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        Reasons(Stored(world)).Should().BeEmpty("the findings step of 100 -> 150 did not land");
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Clean);

        var second = await service.CorrectAsync(Line("Rate", "160"), CancellationToken.None);

        second.IsApplied.Should().BeTrue();
        var stored = Stored(world);
        Reasons(stored).Should().Equal(BillingReasonCodes.RateAboveApproved);
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Flagged, "the line is no longer one an approve would accept in bulk");
        Applied(stored).Should().BeEquivalentTo(world.Corrections.Select(c => c.Id), "the 150 row is followed through by the diff from 100");
        world.Corrections.Should().HaveCount(2);
    }

    [Fact(DisplayName = "101 fix 3 F-2: a timekeeper correction saved without its findings step, then a date correction on the same line: the date diff starts from the original roster match")]
    public async Task TimekeeperThenDate_OnOneLine_DiffsFromTheLastAppliedResolution()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        world.AddRate(lee.Id, 80m);
        Store(world, Root(
            Finding(0, Atk, RuleType.ApprovedTimekeeper, FeeIndex, BillingReasonCodes.TimekeeperNotOnRoster),
            Finding(1, Rate, RuleType.ApprovedRate, FeeIndex, BillingReasonCodes.RateTimekeeperUnmatched)));
        var service = Service(world, null, Atk, Rate);
        FailNextFindingsWrite(world);

        var timekeeper = await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None);

        timekeeper.Status.Should().Be(CorrectionStatus.Incomplete);
        Reasons(Stored(world)).Should().Equal(
            new[] { BillingReasonCodes.RateTimekeeperUnmatched, BillingReasonCodes.TimekeeperNotOnRoster }.OrderBy(r => r), "the timekeeper's findings step did not land");

        var date = await service.CorrectAsync(Line("Date", "2026-09-13"), CancellationToken.None);

        date.IsApplied.Should().BeTrue();
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved);
        var timekeeperRow = world.Corrections.Single(c => c.GetAttributeValue<string>("sprk_fieldname") == "TimekeeperAsBilled");
        (await InvoiceCorrectionReevaluation.ReadUnappliedAsync(world.Entities.Object, W.InvoiceId, world.Lines, CancellationToken.None))
            .Should().ContainSingle().Which.RowId.Should().Be(timekeeperRow.Id, "the timekeeper correction itself still has to be repeated");

        (await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None)).IsApplied.Should().BeTrue();

        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved);
        (await InvoiceCorrectionReevaluation.ReadUnappliedAsync(world.Entities.Object, W.InvoiceId, world.Lines, CancellationToken.None)).Should().BeEmpty();
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Flagged);
    }

    // =========================================================================================
    // Item 4 - a touched line with no stored extraction
    // =========================================================================================

    [Fact(DisplayName = "101 fix 3: a touched line with no stored extraction keeps its status (never re-derived as Pending) and is logged once")]
    public async Task ATouchedLineWithNoExtraction_KeepsItsStatus()
    {
        var world = new CorrectionWorld();
        world.Line().Attributes.Remove("sprk_extractionconfidence");
        world.Invoice["sprk_invoicedate"] = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc); // too old for the line of 2026-09-12
        Store(world, Root(Finding(0, Age, RuleType.InvoiceAge, FeeIndex, BillingReasonCodes.InvoiceTooOld)));
        var log = new W.ListLogger<InvoiceCorrectionReevaluation>();

        var result = await Service(world, log, Age).CorrectAsync(new CorrectionCommand(W.InvoiceId, null, "InvoiceDate", "2026-10-01", false, W.Reviewer), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Reasons(Stored(world)).Should().BeEmpty("the findings are still patched");
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Flagged, "the line's coding cannot be read, so its status is left as it was");
        world.ValueWrites.Should().NotContain(w => w.Id == W.FeeLineId && w.Fields.ContainsKey("sprk_linestatus"));
        log.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("no stored extraction", StringComparison.Ordinal)).Should().Be(1);
    }
}
