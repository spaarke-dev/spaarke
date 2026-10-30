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
/// ebilling-r1 task 101 fix round, part 3 (F-2): a correction is recorded as followed through (<c>appliedCorrections</c>) only after its last step; the next
/// correction of the field diffs from the value whose findings patch last LANDED (and a timekeeper or date correction from the roster match and rate it
/// patched with); the lines a landed patch touched are re-derived by the next status step that runs (<c>pendingStatusLines</c>); a repeat converges to the
/// state of an uninterrupted correction; and a touched line with no stored extraction keeps its status. Over <see cref="CorrectionWorld"/>'s in-memory
/// Dataverse and 072's real evaluator. The approval guard and the line-decision refusal are in <see cref="InvoiceCorrectionEndToEndTests"/>.
/// </summary>
public sealed class InvoiceCorrectionDurabilityTests
{
    private const int FeeIndex = 2; // CorrectionWorld's fee line is sequence 3

    private static readonly BillingRuleCandidate Rate = Kit.Version("BILL-RATE", RuleType.ApprovedRate);
    private static readonly BillingRuleCandidate Atk = Kit.Version("BILL-ATK", RuleType.ApprovedTimekeeper);
    private static readonly BillingRuleCandidate Age = Kit.Version("BILL-AGE", RuleType.InvoiceAge);

    private static CorrectionCommand Line(string field, string? value) => new(W.InvoiceId, W.FeeLineId, field, value, false, W.Reviewer);

    private static CorrectionCommand Header(string field, string? value) => new(W.InvoiceId, null, field, value, false, W.Reviewer);

    private static InvoiceCorrectionService Service(CorrectionWorld world, ILogger<InvoiceCorrectionReevaluation>? log = null, params BillingRuleCandidate[] rules) =>
        Service(world, log, null, rules);

    private static InvoiceCorrectionService Service(
        CorrectionWorld world, ILogger<InvoiceCorrectionReevaluation>? log, Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>>? recalculate,
        params BillingRuleCandidate[] rules)
    {
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            log ?? NullLogger<InvoiceCorrectionReevaluation>.Instance, recalculate: recalculate);
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

    private static List<Guid> Patched(JsonObject root) =>
        (root[InvoiceCorrectionReevaluation.PatchedCorrectionsMember] as JsonArray ?? []).Select(n => Guid.Parse(n!.GetValue<string>())).ToList();

    private static List<int> PendingStatus(JsonObject root) =>
        (root[InvoiceCorrectionReevaluation.PendingStatusLinesMember] as JsonArray ?? []).Select(n => n!.GetValue<int>()).ToList();

    private static Task<IReadOnlyList<InvoiceCorrectionReevaluation.UnappliedCorrection>> Unapplied(CorrectionWorld world) =>
        InvoiceCorrectionReevaluation.ReadUnappliedAsync(world.Entities.Object, W.InvoiceId, world.Lines, CancellationToken.None);

    private static InvoiceTotalsCalculation Totals() => new(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), null);

    /// <summary>The totals step fails ONCE (the next recalculation goes through).</summary>
    private static Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>> TotalsFailingOnce()
    {
        var failed = false;
        return (_, _) =>
        {
            if (failed)
            {
                return Task.FromResult(Totals());
            }

            failed = true;
            return Task.FromException<InvoiceTotalsCalculation>(new TimeoutException("simulated"));
        };
    }

    /// <summary>The first line-status write fails ONCE: the findings patch has landed, the status step has not.</summary>
    private static void FailNextStatusWrite(CorrectionWorld world)
    {
        var failed = false;
        world.TransactionFault = r =>
        {
            if (failed || !r.OfType<UpdateRequest>().Any(u => u.Target.Contains("sprk_linestatus")))
            {
                return null;
            }

            failed = true;
            return new TimeoutException("simulated");
        };
    }

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

    [Fact(DisplayName = "101 fix 3 F-2: an empty patch still records what the findings reflect, the final write records the row as followed through, and a repeat writes nothing more")]
    public async Task AppliedCorrections_AreWritten_OnAnEmptyPatch_AndARepeatAddsNothing()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var service = Service(world);

        var result = await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var row = world.Corrections.Should().ContainSingle().Subject;
        var stored = Stored(world);
        Patched(stored).Should().Equal(row.Id);
        Applied(stored).Should().Equal(row.Id);
        stored.ContainsKey(InvoiceCorrectionReevaluation.PendingStatusLinesMember).Should().BeFalse("the empty patch changed no finding");
        Reasons(stored).Should().BeEmpty("nothing in the findings changed");
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(2, "the patch (patchedCorrections), then the final write (appliedCorrections)");

        var repeat = await service.CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(2, "a repeat finds the row already listed in both");
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
    // Round 2 - "applied" means the WHOLE follow-through finished
    // =========================================================================================

    /// <summary>A Clean fee line billed at 100 against an approved rate of 110, with the rate rule.</summary>
    private static CorrectionWorld RateWorld()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        var line = world.Line();
        line["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean);
        line["sprk_resolvedtimekeeper"] = new EntityReference("sprk_timekeeper", lee.Id);
        line["sprk_approvedrate"] = new Money(110m);
        Store(world, Root());
        return world;
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2: a repeat after a failed totals step converges to the state of an uninterrupted correction; its findings step writes nothing and no Signal is written twice")]
    public async Task ARepeatAfterAFailedTotalsStep_ConvergesToTheUninterruptedState()
    {
        var clean = RateWorld();
        (await Service(clean, null, (_, _) => Task.FromResult(Totals()), Rate).CorrectAsync(Line("Rate", "150"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var world = RateWorld();
        var service = Service(world, null, TotalsFailingOnce(), Rate);

        var first = await service.CorrectAsync(Line("Rate", "150"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.TotalsStep);
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved);
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Flagged);
        Applied(Stored(world)).Should().BeEmpty("the totals step did not finish");
        (await Unapplied(world)).Should().ContainSingle().Which.Field.Should().Be("Rate");
        var signalWrites = world.SignalWrites.Count;
        var submissionWrites = world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission");

        var repeat = await service.CorrectAsync(Line("Rate", "150"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(submissionWrites + 1, "only the final write: the findings patch is idempotent");
        world.SignalWrites.Count.Should().Be(signalWrites, "the plan entry already landed, so its Signal is not written again");
        (await Unapplied(world)).Should().BeEmpty();

        var converged = Stored(world);
        var expected = Stored(clean);
        Applied(converged).Should().Equal(world.Corrections.Single().Id);
        Applied(expected).Should().Equal(clean.Corrections.Single().Id);
        foreach (var member in new[] { InvoiceCorrectionReevaluation.AppliedCorrectionsMember, InvoiceCorrectionReevaluation.PatchedCorrectionsMember })
        {
            converged.Remove(member);
            expected.Remove(member);
        }

        converged.ToJsonString().Should().Be(expected.ToJsonString(), "the repeat ends where an uninterrupted correction ends");
        StatusOf(world).Should().Be(StatusOf(clean));
        world.SignalWrites.Count.Should().Be(clean.SignalWrites.Count);
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2 SEEDED: rate 100 -> 150 whose patch landed but whose status step failed, then back to 100: the diff runs from 150 (what the findings reflect), so the finding is removed")]
    public async Task ARateChain_WhoseMiddlePatchLandedButWhoseStatusFailed_ConvergesWhenCorrectedBack()
    {
        var world = RateWorld();
        var service = Service(world, null, Rate);
        FailNextStatusWrite(world);

        var first = await service.CorrectAsync(Line("Rate", "150"), CancellationToken.None);

        first.FailedStep.Should().Be(CorrectionCodes.StatusStep);
        var stored = Stored(world);
        Reasons(stored).Should().Equal(BillingReasonCodes.RateAboveApproved);
        PendingStatus(stored).Should().Equal(FeeIndex);
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Clean, "the status step did not finish");
        Applied(stored).Should().BeEmpty();

        // Diffed from 100 (the last FULLY followed-through value) both sides agree and the 150 finding would stay; diffed from 150 it is removed.
        var second = await service.CorrectAsync(Line("Rate", "100"), CancellationToken.None);

        second.IsApplied.Should().BeTrue();
        stored = Stored(world);
        Reasons(stored).Should().BeEmpty();
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Clean);
        stored.ContainsKey(InvoiceCorrectionReevaluation.PendingStatusLinesMember).Should().BeFalse();
        Applied(stored).Should().BeEquivalentTo(world.Corrections.Select(c => c.Id), "both rows are followed through");
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2: a line left waiting by a status step that failed is re-derived by the next correction of any field; the unfinished row stays unapplied")]
    public async Task ALineLeftWaitingForItsStatus_IsReDerivedByTheNextCorrection()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean);
        world.Invoice["sprk_invoicedate"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        Store(world, Root());
        var service = Service(world, null, Age);
        FailNextStatusWrite(world);

        var first = await service.CorrectAsync(Header("InvoiceDate", "2026-12-31"), CancellationToken.None); // too old for the line of 2026-09-12

        first.FailedStep.Should().Be(CorrectionCodes.StatusStep);
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.InvoiceTooOld);
        PendingStatus(Stored(world)).Should().Equal(FeeIndex);
        StatusOf(world).Should().Be((int)InvoiceLineStatus.Clean);

        // The firm's invoice number touches no line, yet its status step re-derives the waiting line.
        (await service.CorrectAsync(Header("FirmInvoiceNumber", "INV-1002"), CancellationToken.None)).IsApplied.Should().BeTrue();

        StatusOf(world).Should().Be((int)InvoiceLineStatus.Flagged);
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingStatusLinesMember).Should().BeFalse();
        (await Unapplied(world)).Should().ContainSingle().Which.Field.Should().Be("InvoiceDate", "the date correction itself still has to be repeated");
    }

    [Fact(DisplayName = "101 fix 3 F-2 round 2: a TotalDue correction (no findings step) is recorded after its totals step, and is unapplied while that step has not finished")]
    public async Task ATotalDueCorrection_IsRecordedAfterItsTotalsStep()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var service = Service(world, null, TotalsFailingOnce());

        var first = await service.CorrectAsync(Header("TotalDue", "2400"), CancellationToken.None);

        first.FailedStep.Should().Be(CorrectionCodes.TotalsStep);
        var unapplied = (await Unapplied(world)).Should().ContainSingle().Subject;
        unapplied.Field.Should().Be("TotalDue");
        unapplied.LineId.Should().BeNull();

        (await service.CorrectAsync(Header("TotalDue", "2400"), CancellationToken.None)).IsApplied.Should().BeTrue();

        (await Unapplied(world)).Should().BeEmpty();
        Applied(Stored(world)).Should().Equal(world.Corrections.Single().Id);
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
