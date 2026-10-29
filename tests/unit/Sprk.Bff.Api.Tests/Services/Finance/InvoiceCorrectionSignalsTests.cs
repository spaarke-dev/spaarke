using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 101 part B2b-2: the Signals step of <see cref="InvoiceCorrectionReevaluation"/> over <see cref="CorrectionWorld"/>'s in-memory Dataverse
/// and Signal store and 072's real evaluator: rule Signals closed (ConditionCleared, record-free) and raised (episode n+1), a refresh, the system Signals of a
/// line, the D-125 coding hold, the 073 presentation guard, the plan refusals, and retry convergence.
/// </summary>
public sealed class InvoiceCorrectionSignalsTests
{
    private const int FeeIndex = 2; // CorrectionWorld's fee line is sequence 3
    private const string LineEntity = BillingFindingSignalMapper.LineEntity;
    private const string CapFingerprint = BillingReasonCodes.DailyHoursOverCap + "|Flag";

    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap); // 12 hours a day
    private static readonly BillingRuleCandidate WholeHours = Kit.Version("BILL-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");
    private static readonly BillingRuleCandidate HalfHours = Kit.Version("BILL-INC", RuleType.TimeIncrement); // 0.5
    private static readonly BillingRuleCandidate SystemPolicy = Kit.Version(BillingRuleCatalogue.SystemPolicyCode, RuleType.SubmissionIntegrity, "{}");

    private static CorrectionCommand Line(string field, string? value, Guid? line = null) => new(W.InvoiceId, line ?? W.FeeLineId, field, value, false, W.Reviewer);

    private static InvoiceCorrectionService Service(CorrectionWorld world, params BillingRuleCandidate[] rules)
    {
        var evaluator = Kit.Evaluator();
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), evaluator.Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            NullLogger<InvoiceCorrectionReevaluation>.Instance,
            (_, _) => Task.FromResult<(BillingRuleCandidate?, string)>((SystemPolicy, "in_force")));
        world.Seam = reevaluation.ApplyAsync;
        return world.Service();
    }

    private static JsonObject Root(string kind = "Accepted")
    {
        var root = JsonNode.Parse(W.HeaderFindingsJson)!.AsObject();
        root["schema"] = 1;
        root["kind"] = kind;
        root["invoiceId"] = W.InvoiceId.ToString("D");
        root["trackingNumber"] = "INV-1001.1";
        root["receivedOn"] = "2026-10-01";
        root["invoiceName"] = "INV-1001";
        root["findings"] = new JsonArray();
        root["pipelineFindings"] = new JsonArray();
        root["codingFindings"] = new JsonArray();
        root["lineFindings"] = new JsonArray();
        root["signalPlan"] = new JsonArray();
        return root;
    }

    private static void Store(CorrectionWorld world, JsonObject root) => world.Submission!["sprk_findingsjson"] = root.ToJsonString();

    private static JsonObject Stored(CorrectionWorld world) => JsonNode.Parse(world.Submission!.GetAttributeValue<string>("sprk_findingsjson"))!.AsObject();

    private static JsonObject CapFinding(string consequence = "Flag", bool rejects = false) => new()
    {
        ["index"] = 0,
        ["ruleType"] = RuleType.DailyHoursCap.ToString(),
        ["policyId"] = Cap.PolicyId.ToString("D"),
        ["policyCode"] = Cap.PolicyCode,
        ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"),
        ["subject"] = "Line",
        ["line"] = FeeIndex,
        ["reasonCode"] = BillingReasonCodes.DailyHoursOverCap,
        ["consequence"] = consequence,
        ["rejects"] = rejects,
    };

    private static JsonObject CapPlan(string? decision = null)
    {
        var entry = new JsonObject
        {
            ["policyId"] = Cap.PolicyId.ToString("D"),
            ["policyCode"] = Cap.PolicyCode,
            ["policyVersionId"] = Cap.PolicyVersionId.ToString("D"),
            ["subjectEntity"] = LineEntity,
            ["subjectId"] = W.FeeLineId.ToString("D"),
            ["fingerprint"] = CapFingerprint,
        };
        if (decision is not null)
        {
            entry["decision"] = decision;
        }

        return entry;
    }

    private static JsonObject CapStored(CorrectionWorld world)
    {
        var root = Root();
        root["findings"] = new JsonArray(CapFinding());
        root["signalPlan"] = new JsonArray(CapPlan());
        Store(world, root);
        return root;
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? array) => (array as JsonArray ?? []).OfType<JsonObject>();

    private static string? Text(JsonNode? node, string name) => node?[name]?.GetValue<string>();

    private static int StatusOf(CorrectionWorld world, Guid line) =>
        world.Line(line).GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sprk_linestatus").Value;

    // =========================================================================================
    // Item 1 - rule Signals (and item 7's coded path: clear, then re-raise as episode n+1)
    // =========================================================================================

    [Fact(DisplayName = "101 B2b-2: a correction that clears a rule finding closes its Signal as ConditionCleared (no Decision Record), and one that re-raises it opens episode 2")]
    public async Task CodedInvoice_ClearThenReRaise_ClosesTheSignal_ThenOpensTheNextEpisode()
    {
        var world = new CorrectionWorld();
        CapStored(world);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);
        var service = Service(world, Cap, HalfHours);

        (await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal((1, true, SignalWriter.ResolutionConditionCleared, CapFingerprint));
        world.SignalWrites.Should().BeEmpty();
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse("the reconciled changes are cleared");
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Clean);

        (await service.CorrectAsync(Line("Hours", "25"), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal(
            (1, true, SignalWriter.ResolutionConditionCleared, CapFingerprint),
            (2, false, (int?)null, CapFingerprint));
        world.SignalCloses.Should().ContainSingle("the second correction closes nothing");
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Flagged);
    }

    [Fact(DisplayName = "101 B2b-2: equal close and raise fingerprints are a refresh - nothing is closed and the open episode is written again")]
    public async Task SameFingerprint_IsARefresh_NotACloseAndRaise()
    {
        var world = new CorrectionWorld();
        CapStored(world);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);

        (await Service(world, Cap).CorrectAsync(Line("Hours", "24"), CancellationToken.None)).IsApplied.Should().BeTrue(); // still over the cap

        world.SignalCloses.Should().BeEmpty();
        world.SignalWrites.Should().Equal((Cap.PolicyCode, W.FeeLineId, 1));
        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal((1, false, (int?)null, CapFingerprint));
    }

    // =========================================================================================
    // Item 2 - system Signals of a line
    // =========================================================================================

    private static JsonObject CodingEntry(bool activityRefused) => new()
    {
        ["line"] = FeeIndex,
        ["refused"] = new JsonArray(
            new[] { new JsonObject { ["kind"] = "Task", ["code"] = "L999", ["reason"] = "OutsideCodeSet" } }
                .Concat(activityRefused ? [new JsonObject { ["kind"] = "Activity", ["code"] = "A999", ["reason"] = "OutsideCodeSet" }] : [])
                .Select(n => (JsonNode)n).ToArray()),
        ["task"] = new JsonObject { ["code"] = "L110", ["firmCode"] = "L100", ["mismatch"] = true },
        ["activity"] = new JsonObject { ["code"] = "A101", ["firmCode"] = null, ["mismatch"] = false },
        ["expense"] = new JsonObject { ["code"] = null, ["firmCode"] = null, ["mismatch"] = false },
    };

    [Theory(DisplayName = "101 B2b-2: a code correction closes the line's system Signal when no system finding is left, and refreshes it (episode 2) when one is")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TaskCodeCorrection_ClosesOrRefreshesTheLinesSystemSignal(bool activityStillRefused)
    {
        var world = new CorrectionWorld();
        var root = Root();
        root["codingFindings"] = new JsonArray(CodingEntry(activityStillRefused));
        Store(world, root);
        world.AddSignal(SystemPolicy.PolicyCode, LineEntity, W.FeeLineId, 1, "as-written-by-049");

        (await Service(world).CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var signals = world.SignalsOf(SystemPolicy.PolicyCode, W.FeeLineId);
        if (activityStillRefused)
        {
            signals.Should().HaveCount(2);
            signals[0].Should().Be((1, true, SignalWriter.ResolutionSuperseded, "as-written-by-049"));
            signals[1].Episode.Should().Be(2);
            signals[1].Resolved.Should().BeFalse();
        }
        else
        {
            signals.Should().Equal((1, true, SignalWriter.ResolutionConditionCleared, "as-written-by-049"));
            world.SignalWrites.Should().BeEmpty();
        }

        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
    }

    // =========================================================================================
    // Item 4 - the D-125 coding hold
    // =========================================================================================

    [Fact(DisplayName = "101 B2b-2 D-125: the hold stays while a line is uncoded; when the last line is re-coded its entry goes, its Signal closes ConditionCleared and the lines are re-derived")]
    public async Task CodingHold_ClosesWhenTheLastUncodedLineIsReCoded()
    {
        var world = new CorrectionWorld();
        world.DefaultCodeSetId = W.CodeSetId;
        world.Lines.Clear();
        var fee = W.FeeLine(status: (int)InvoiceLineStatus.Pending, extraction: W.ExtractionJson(coded: false));
        fee.Attributes.Remove("sprk_taskcodeasbilled");
        world.Lines.Add(fee);
        var expense = W.ExpenseLine();
        expense["sprk_linestatus"] = new Microsoft.Xrm.Sdk.OptionSetValue((int)InvoiceLineStatus.Pending);
        expense["sprk_extractionconfidence"] = W.ExtractionJson(coded: false);
        world.Lines.Add(expense);
        var root = Root("AcceptedUncoded");
        root["reasonCode"] = Sprk.Bff.Api.Services.Finance.Submission.SubmissionReasonCodes.CodingNoCodeSetHold;
        root["pipelineFindings"] = new JsonArray(new JsonObject
        {
            ["reasonCode"] = Sprk.Bff.Api.Services.Finance.Submission.SubmissionReasonCodes.CodingNoCodeSetHold, ["values"] = new JsonObject(),
        });
        Store(world, root);
        world.AddSignal(SystemPolicy.PolicyCode, "sprk_invoice", W.InvoiceId, 1, "coding.no_code_set|Flag");
        var service = Service(world);

        (await service.CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["pipelineFindings"]).Should().ContainSingle("the expense line is still uncoded");
        world.SignalsOf(SystemPolicy.PolicyCode, W.InvoiceId).Single().Resolved.Should().BeFalse();
        StatusOf(world, W.ExpenseLineId).Should().Be((int)InvoiceLineStatus.Pending);

        (await service.CorrectAsync(Line("ExpenseCode", "E101", W.ExpenseLineId), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["pipelineFindings"]).Should().BeEmpty();
        world.SignalsOf(SystemPolicy.PolicyCode, W.InvoiceId).Should().Equal((1, true, SignalWriter.ResolutionConditionCleared, "coding.no_code_set|Flag"));
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Clean);
        StatusOf(world, W.ExpenseLineId).Should().Be((int)InvoiceLineStatus.Clean);
    }

    // =========================================================================================
    // Item 5 - 073 presentation: a correction never drops a hold
    // =========================================================================================

    private static CorrectionWorld Blocked()
    {
        var world = new CorrectionWorld();
        var root = Root("Blocked");
        root["decision"] = new JsonObject { ["outcome"] = "Rejected", ["deciding"] = new JsonArray(0) };
        root["findings"] = new JsonArray(CapFinding("Reject", rejects: true));
        root["signalPlan"] = new JsonArray(CapPlan(nameof(BillingGroupDecision.Blocked)));
        Store(world, root);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);
        return world;
    }

    [Fact(DisplayName = "101 B2b-2 SEEDED: a correction that changes a Blocked group re-applies 073's stored decision, so the entry is still presented as held")]
    public async Task BlockedGroup_ChangedByACorrection_KeepsTheHeldPresentation()
    {
        var world = Blocked();

        (await Service(world, Cap).CorrectAsync(Line("Hours", "24"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var entry = Objects(Stored(world)["signalPlan"]).Should().ContainSingle().Subject;
        Text(entry, "decision").Should().Be(nameof(BillingGroupDecision.Blocked));
        Text(entry, "headline").Should().Be(BillingFindingSignalMapper.HeldHeadline, "the replacement is mapped with 073's decision, not without it");
        entry.ContainsKey(InvoiceCorrectionReevaluation.CorrectionReviewMember).Should().BeFalse();
        Objects(Stored(world)["findings"]).Single()["rejects"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact(DisplayName = "101 B2b-2 SEEDED: a correction that removes a Blocked group's reason keeps the stored entry and its Signal, marked for review")]
    public async Task BlockedGroup_WhoseReasonACorrectionRemoves_IsKeptAndMarked()
    {
        var world = Blocked();

        (await Service(world, Cap).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var entry = Objects(Stored(world)["signalPlan"]).Should().ContainSingle().Subject;
        Text(entry, "decision").Should().Be(nameof(BillingGroupDecision.Blocked));
        entry[InvoiceCorrectionReevaluation.CorrectionReviewMember]!.GetValue<bool>().Should().BeTrue();
        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().Equal([(1, false, (int?)null, CapFingerprint)], "the held Signal is not closed by a correction");
        world.SignalCloses.Should().BeEmpty();
    }

    // =========================================================================================
    // Item 6 - plan entries the writer would refuse
    // =========================================================================================

    [Fact(DisplayName = "101 B2b-2: a new plan entry the writer would refuse is left out of the plan and recorded in signalPlanRefusals, once")]
    public async Task RefusedReplacement_IsRecordedInSignalPlanRefusals()
    {
        var world = new CorrectionWorld();
        CapStored(world);
        var longCode = Kit.Version("BILL-" + new string('X', 60), RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");

        (await Service(world, Cap, longCode).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var root = Stored(world);
        Objects(root["signalPlan"]).Should().BeEmpty();
        var refusal = Objects(root["signalPlanRefusals"]).Should().ContainSingle().Subject;
        Text(refusal, "policyCode").Should().Be(longCode.PolicyCode);
        Text(refusal["subject"], "id").Should().Be(W.FeeLineId.ToString("D"));
        Text(refusal, "code").Should().Be(SignalWriteRefusal.PolicyCodeTooLong);
        world.SignalWrites.Should().BeEmpty("a refused entry is never raised");
    }

    // =========================================================================================
    // Item 3 - retry convergence
    // =========================================================================================

    [Fact(DisplayName = "101 B2b-2 SEEDED: a write that fails at the signals step is Incomplete (failedStep signals); the repeat closes nothing twice and raises episode 1 once")]
    public async Task AFailedSignalsStep_IsIncomplete_AndTheRepeatConverges()
    {
        var world = new CorrectionWorld();
        CapStored(world);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);
        world.SignalWriteFault = r => r.PolicyCode == WholeHours.PolicyCode ? new TimeoutException("simulated") : null;
        var service = Service(world, Cap, WholeHours);

        var first = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.SignalsStep);
        Objects(Stored(world)[InvoiceCorrectionReevaluation.PendingSignalChangesMember]!["rule"]).Should().HaveCount(2, "a failure part-way leaves the changes for the repeat");
        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Single().Resolved.Should().BeTrue("the close ran before the failed raise");

        world.SignalWriteFault = null;
        var repeat = await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.SignalCloses.Should().ContainSingle("the Signal already closed is left as it is");
        world.SignalsOf(WholeHours.PolicyCode, W.FeeLineId).Should().ContainSingle().Which.Should().Match<(int Episode, bool Resolved, int? Resolution, string? Fingerprint)>(s => s.Episode == 1 && !s.Resolved);
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
    }

    [Fact(DisplayName = "101 B2b-2: the clear that fails after every Signal is done is Incomplete; the repeat refreshes the raised episode in place (no episode 3) and clears")]
    public async Task AFailedClear_TheRepeatDoesNotOpenAnotherEpisode()
    {
        var world = new CorrectionWorld();
        CapStored(world);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint);
        var service = Service(world, Cap, HalfHours);
        (await service.CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var clears = 0;
        world.TransactionFault = r => r.OfType<UpdateRequest>().Any(u => u.Target.LogicalName == "sprk_invoicesubmission"
            && !u.Target.GetAttributeValue<string>("sprk_findingsjson").Contains(InvoiceCorrectionReevaluation.PendingSignalChangesMember)
            && clears++ == 0) ? new TimeoutException("simulated") : null;
        var first = await service.CorrectAsync(Line("Hours", "25"), CancellationToken.None);

        first.FailedStep.Should().Be(CorrectionCodes.SignalsStep);
        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Should().HaveCount(2, "episode 2 was raised before the clear failed");

        var repeat = await service.CorrectAsync(Line("Hours", "25"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        world.SignalsOf(Cap.PolicyCode, W.FeeLineId).Select(s => (s.Episode, s.Resolved)).Should().Equal((1, true), (2, false));
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
    }

    [Fact(DisplayName = "101 B2b-2: a half-cleared pendingSignalChanges (no system member, an entry already reconciled) converges without a second close")]
    public async Task AHalfClearedPendingMember_Converges()
    {
        var world = new CorrectionWorld();
        var root = Root();
        root[InvoiceCorrectionReevaluation.PendingSignalChangesMember] = new JsonObject
        {
            ["rule"] = new JsonArray(new JsonObject
            {
                ["policyCode"] = Cap.PolicyCode, ["subjectEntity"] = LineEntity, ["subjectId"] = W.FeeLineId.ToString("D"),
                ["closeFingerprint"] = CapFingerprint, ["raiseFingerprint"] = null,
            }),
        };
        Store(world, root);
        world.AddSignal(Cap.PolicyCode, LineEntity, W.FeeLineId, 1, CapFingerprint, resolved: true);

        (await Service(world).CorrectAsync(Line("Narrative", "Prepared the brief"), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.SignalCloses.Should().BeEmpty("the Signal was already closed");
        world.SignalWrites.Should().BeEmpty();
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();
    }
}
