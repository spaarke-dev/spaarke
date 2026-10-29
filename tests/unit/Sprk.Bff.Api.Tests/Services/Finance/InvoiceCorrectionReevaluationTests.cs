using System.Text.Json.Nodes;
using FluentAssertions;
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
/// ebilling-r1 task 101 part B2b-1 (FR-25, FR-31, D-126): <see cref="InvoiceCorrectionReevaluation"/> wired into <see cref="InvoiceCorrectionService"/>'s seam
/// over the in-memory Dataverse of <see cref="CorrectionWorld"/> and 072's real in-memory evaluator: the diff of the two evaluations patches the stored
/// findings, the clears of item 2, the line status from the patched findings, the decided-line guard, the uncoded re-code and retry convergence.
/// </summary>
public sealed class InvoiceCorrectionReevaluationTests
{
    private const int FeeIndex = 2; // CorrectionWorld's fee line is sequence 3
    private const string CapFingerprint = BillingReasonCodes.DailyHoursOverCap + "|Flag";

    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap); // 12 hours a day
    private static readonly BillingRuleCandidate WholeHours = Kit.Version("BILL-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");
    private static readonly BillingRuleCandidate HalfHours = Kit.Version("BILL-INC", RuleType.TimeIncrement); // 0.5

    private static CorrectionCommand Line(string field, string? value, Guid? line = null) => new(W.InvoiceId, line ?? W.FeeLineId, field, value, false, W.Reviewer);

    private static InvoiceCorrectionService Service(CorrectionWorld world, params BillingRuleCandidate[] rules)
    {
        var evaluator = Kit.Evaluator();
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), evaluator.Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            NullLogger<InvoiceCorrectionReevaluation>.Instance);
        world.Seam = reevaluation.ApplyAsync;
        return world.Service();
    }

    /// <summary>The stored findings as 049 left them: the header provenance, plus the members a test names.</summary>
    private static void Store(CorrectionWorld world, JsonArray? findings = null, JsonArray? plan = null, JsonArray? coding = null, JsonArray? lineFindings = null)
    {
        var root = JsonNode.Parse(W.HeaderFindingsJson)!.AsObject();
        root["schema"] = 1;
        root["kind"] = "Accepted";
        root["invoiceId"] = W.InvoiceId.ToString("D");
        root["trackingNumber"] = "INV-1001.1";
        root["receivedOn"] = "2026-10-01";
        root["invoiceName"] = "INV-1001";
        root["findings"] = findings ?? [];
        root["pipelineFindings"] = new JsonArray();
        root["codingFindings"] = coding ?? [];
        root["lineFindings"] = lineFindings ?? [];
        root["signalPlan"] = plan ?? [];
        world.Submission!["sprk_findingsjson"] = root.ToJsonString();
    }

    /// <summary>The findings as the findings step wrote them (B2b-2's Signals step then reconciles and removes <c>pendingSignalChanges</c>).</summary>
    private static JsonObject FindingsPatch(CorrectionWorld world) =>
        JsonNode.Parse((string)world.ValueWrites.First(w => w.Entity == "sprk_invoicesubmission").Fields["sprk_findingsjson"])!.AsObject();

    private static JsonObject Stored(CorrectionWorld world) => JsonNode.Parse(world.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

    private static JsonObject Finding(int index, BillingRuleCandidate rule, int line, string reason) => new()
    {
        ["index"] = index,
        ["ruleType"] = RuleType.DailyHoursCap.ToString(),
        ["policyId"] = rule.PolicyId.ToString("D"),
        ["policyCode"] = rule.PolicyCode,
        ["policyVersionId"] = rule.PolicyVersionId.ToString("D"),
        ["subject"] = "Line",
        ["line"] = line,
        ["reasonCode"] = reason,
        ["consequence"] = "Flag",
    };

    private static JsonObject Plan(BillingRuleCandidate rule, Guid lineId, string fingerprint) => new()
    {
        ["policyId"] = rule.PolicyId.ToString("D"),
        ["policyCode"] = rule.PolicyCode,
        ["policyVersionId"] = rule.PolicyVersionId.ToString("D"),
        ["subjectEntity"] = "sprk_billingevent",
        ["subjectId"] = lineId.ToString("D"),
        ["fingerprint"] = fingerprint,
    };

    private static JsonObject LineFinding(int line, string code) => new() { ["line"] = line, ["code"] = code };

    private static int StatusOf(Entity line) => line.GetAttributeValue<OptionSetValue>("sprk_linestatus").Value;

    private static IEnumerable<JsonObject> Objects(JsonNode? array) => (array as JsonArray ?? []).OfType<JsonObject>();

    private static string? Text(JsonNode? node, string name) => node?[name]?.GetValue<string>();

    // =========================================================================================
    // The diff
    // =========================================================================================

    [Fact]
    public async Task HoursCorrection_RemovesTheFindingThatNoLongerFires_AndAddsTheOneThatNowDoes()
    {
        var world = new CorrectionWorld();
        Store(world, findings: [Finding(0, Cap, FeeIndex, BillingReasonCodes.DailyHoursOverCap)], plan: [Plan(Cap, W.FeeLineId, CapFingerprint)]);

        var result = await Service(world, Cap, WholeHours).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var root = Stored(world);
        var finding = Objects(root["findings"]).Should().ContainSingle().Subject; // 25 h over the cap is gone; 2.5 h is not a whole hour
        finding["reasonCode"]!.GetValue<string>().Should().Be(BillingReasonCodes.TimeNotAMultipleOfIncrement);
        finding["line"]!.GetValue<int>().Should().Be(FeeIndex);
        finding["index"]!.GetValue<int>().Should().Be(1, "a new entry never reuses a removed entry's index");
        finding["consequence"].Should().BeNull("the rule version states no consequence, as the persister writes it");
        finding["rejects"]!.GetValue<bool>().Should().BeFalse();
        var plan = Objects(root["signalPlan"]).Should().ContainSingle().Subject;
        plan["policyCode"]!.GetValue<string>().Should().Be("BILL-INC");
        plan["subjectId"]!.GetValue<string>().Should().Be(W.FeeLineId.ToString("D"));

        var pending = Objects(FindingsPatch(world)[InvoiceCorrectionReevaluation.PendingSignalChangesMember]!["rule"]).ToList();
        pending.Should().HaveCount(2);
        var close = pending.Single(p => Text(p, "policyCode") == "BILL-CAP");
        Text(close, "closeFingerprint").Should().Be(CapFingerprint);
        close["raiseFingerprint"].Should().BeNull();
        var raise = pending.Single(p => Text(p, "policyCode") == "BILL-INC");
        raise["closeFingerprint"].Should().BeNull();
        Text(raise, "raiseFingerprint").Should().Be(Text(plan, "fingerprint"));
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Flagged);
    }

    [Fact]
    public async Task HoursCorrection_ThatClearsTheLastFinding_MakesTheLineClean()
    {
        var world = new CorrectionWorld();
        Store(world, findings: [Finding(0, Cap, FeeIndex, BillingReasonCodes.DailyHoursOverCap)], plan: [Plan(Cap, W.FeeLineId, CapFingerprint)]);

        var result = await Service(world, Cap, HalfHours).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var root = Stored(world);
        Objects(root["findings"]).Should().BeEmpty();
        Objects(root["signalPlan"]).Should().BeEmpty();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    // =========================================================================================
    // The clears of item 2
    // =========================================================================================

    private static JsonObject CodingEntry(bool activityRefused) => new()
    {
        ["line"] = FeeIndex,
        ["refused"] = new JsonArray(
            new[] { new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = "NotInSet" } }
                .Concat(activityRefused ? [new JsonObject { ["kind"] = "Activity", ["code"] = "A999", ["reason"] = "NotInSet" }] : [])
                .Select(n => (JsonNode)n).ToArray()),
        ["task"] = new JsonObject { ["code"] = "L110", ["firmCode"] = "L100", ["mismatch"] = true },
        ["activity"] = new JsonObject { ["code"] = "A101", ["firmCode"] = null, ["mismatch"] = false },
        ["expense"] = new JsonObject { ["code"] = null, ["firmCode"] = null, ["mismatch"] = false },
    };

    [Fact]
    public async Task TaskCodeCorrection_RemovesThatKindsRefusedAndMismatchEntries_AndKeepsAnotherKindsRefusal()
    {
        var world = new CorrectionWorld();
        Store(world, coding: [CodingEntry(activityRefused: true)]);

        var result = await Service(world).CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var root = Stored(world);
        var entry = Objects(root["codingFindings"]).Should().ContainSingle().Subject;
        Objects(entry["refused"]).Select(r => Text(r, "kind")).Should().Equal("Activity");
        entry["task"]!["mismatch"]!.GetValue<bool>().Should().BeFalse();
        Objects(FindingsPatch(world)[InvoiceCorrectionReevaluation.PendingSignalChangesMember]!["system"])
            .Select(s => (s["line"]!.GetValue<int>(), Text(s, "kind"), Text(s, "reason")))
            .Should().BeEquivalentTo(new[] { (FeeIndex, "Task", "refused"), (FeeIndex, "Task", "firmCodeMismatch") });
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Flagged, "the activity code refusal still stands");
    }

    [Fact]
    public async Task CodeCorrection_OnANotReturnedLine_RemovesThatEntryAndTheEmptiedCodingEntry_AndTheLineIsClean()
    {
        var world = new CorrectionWorld();
        Store(world, coding: [CodingEntry(activityRefused: false)], lineFindings: [LineFinding(FeeIndex, "coding.not_returned")]);

        var result = await Service(world).CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var root = Stored(world);
        Objects(root["codingFindings"]).Should().BeEmpty();
        Objects(root["lineFindings"]).Should().BeEmpty();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    [Fact]
    public async Task NarrativeCorrection_RemovesNoDescription_AndTheLineIsClean()
    {
        var world = new CorrectionWorld();
        world.Line().Attributes.Remove("sprk_description");
        Store(world, lineFindings: [LineFinding(FeeIndex, "line.no_description")]);

        var result = await Service(world).CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Objects(Stored(world)["lineFindings"]).Should().BeEmpty();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    [Fact]
    public async Task RulesNotChecked_IsNeverRemovedByACorrection_SoTheLineStaysFlagged()
    {
        var world = new CorrectionWorld();
        world.Line().Attributes.Remove("sprk_description");
        Store(world, lineFindings: [LineFinding(FeeIndex, "rules.not_checked"), LineFinding(FeeIndex, "line.no_description")]);

        var result = await Service(world).CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Objects(Stored(world)["lineFindings"]).Select(f => Text(f, "code")).Should().Equal("rules.not_checked");
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Flagged);
    }

    // =========================================================================================
    // A decided line
    // =========================================================================================

    [Fact]
    public async Task ALineAReviewerDecided_KeepsItsFindingsPlanAndStatus_WhenTheDiffReachesIt()
    {
        var world = new CorrectionWorld();
        var decidedId = Guid.Parse("10100000-0000-4000-8000-0000000000b0");
        var decided = W.Copy(world.Line());
        decided.Id = decidedId;
        decided["sprk_linesequence"] = 1; // index 0: the daily-cap finding names the first line of the day
        decided["sprk_quantity"] = 5m;
        decided["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        world.Lines.Add(decided);
        Store(world, findings: [Finding(0, Cap, 0, BillingReasonCodes.DailyHoursOverCap)], plan: [Plan(Cap, decidedId, CapFingerprint)]);
        var stored = world.Submission!.GetAttributeValue<string>("sprk_findingsjson");

        var result = await Service(world, Cap).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None); // 30 h on the day becomes 7.5 h

        result.IsApplied.Should().BeTrue();
        world.Submission!.GetAttributeValue<string>("sprk_findingsjson").Should().Be(stored, "nothing on a decided line is patched");
        StatusOf(world.Line(decidedId)).Should().Be((int)InvoiceLineStatus.Accepted);
        world.ValueWrites.Should().NotContain(w => w.Id == decidedId);
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    // =========================================================================================
    // The uncoded re-code (D-126)
    // =========================================================================================

    private static CorrectionWorld Uncoded()
    {
        var world = new CorrectionWorld();
        world.Lines.Clear();
        var fee = W.FeeLine(status: (int)InvoiceLineStatus.Pending, extraction: W.ExtractionJson(coded: false));
        fee.Attributes.Remove("sprk_taskcodeasbilled"); // no firm task code: FR-22 step 1 does not apply
        world.Lines.Add(fee);
        Store(world);
        return world;
    }

    [Fact]
    public async Task UncodedInvoice_APendingLineIsReCoded_FromTheResolvedSet_AndReDerivesAsCoded()
    {
        var world = Uncoded();
        world.DefaultCodeSetId = W.CodeSetId;

        var result = await Service(world).CorrectAsync(Line("TaskCode", "l120"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var coding = JsonNode.Parse(world.Line().GetAttributeValue<string>("sprk_extractionconfidence"))!["coding"]!;
        coding["codeSetId"]!.GetValue<string>().Should().Be(W.CodeSetId.ToString("D"));
        coding["codeSetSource"]!.GetValue<string>().Should().Be("SettingsDefault");
        coding["status"]!.GetValue<string>().Should().Be("NotCoded", "no AI coded the line");
        coding["task"]!["code"]!.GetValue<string>().Should().Be("L120", "the code as the set spells it");
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    [Fact]
    public async Task UncodedInvoice_WithNoResolvableCodeSet_Is409_AndNothingIsWritten()
    {
        var world = Uncoded();
        world.CodeSets.Clear();

        var result = await Service(world).CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.CodeSetUnavailable);
        result.Code.Should().Be(CorrectionCodes.CodeSetUnavailable);
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Pending);
    }

    [Fact]
    public async Task UncodedInvoice_ANonCodeCorrectionOfAPendingLine_IsStillRefused()
    {
        var world = Uncoded();

        var result = await Service(world).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.Code.Should().Be(CorrectionCodes.LinePending);
        world.Corrections.Should().BeEmpty();
    }

    // =========================================================================================
    // Retry convergence
    // =========================================================================================

    private static bool WritesFindings(IReadOnlyList<OrganizationRequest> requests) =>
        requests.OfType<UpdateRequest>().Any(u => u.Target.LogicalName == "sprk_invoicesubmission");

    private static bool WritesStatus(IReadOnlyList<OrganizationRequest> requests) =>
        requests.OfType<UpdateRequest>().Any(u => u.Target.Contains("sprk_linestatus"));

    [Fact]
    public async Task AFailedFindingsStep_IsIncompleteWithFailedStepFindings_AndTheRepeatConverges()
    {
        var world = new CorrectionWorld();
        Store(world, findings: [Finding(0, Cap, FeeIndex, BillingReasonCodes.DailyHoursOverCap)], plan: [Plan(Cap, W.FeeLineId, CapFingerprint)]);
        world.TransactionFault = r => WritesFindings(r) ? new TimeoutException("simulated") : null;
        var service = Service(world, Cap, HalfHours);

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.FindingsStep);
        Objects(Stored(world)["findings"]).Should().ContainSingle("the patch did not land");
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Flagged);

        world.TransactionFault = null;
        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.Corrections.Should().ContainSingle();
        Objects(Stored(world)["findings"]).Should().BeEmpty();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }

    [Fact]
    public async Task AFailedStatusStep_IsIncompleteWithFailedStepStatus_AndTheRepeatConvergesWithoutASecondPatch()
    {
        var world = new CorrectionWorld();
        Store(world, findings: [Finding(0, Cap, FeeIndex, BillingReasonCodes.DailyHoursOverCap)], plan: [Plan(Cap, W.FeeLineId, CapFingerprint)]);
        world.TransactionFault = r => WritesStatus(r) ? new TimeoutException("simulated") : null;
        var service = Service(world, Cap, HalfHours);

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.StatusStep);
        var patched = world.Submission!.GetAttributeValue<string>("sprk_findingsjson");
        Objects(JsonNode.Parse(patched)!["findings"]).Should().BeEmpty("the findings step finished");
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Flagged);

        world.TransactionFault = null;
        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.Corrections.Should().ContainSingle();
        // B2b-2: the first attempt stopped at the status step, so the Signals step (which removes pendingSignalChanges) first ran on the repeat; the
        // findings patch itself was not written again.
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(2, "the patch once, then the Signals step's clear");
        var final = Stored(world);
        final["findings"]!.ToJsonString().Should().Be(JsonNode.Parse(patched)!["findings"]!.ToJsonString());
        final.ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
        StatusOf(world.Line()).Should().Be((int)InvoiceLineStatus.Clean);
    }
}
