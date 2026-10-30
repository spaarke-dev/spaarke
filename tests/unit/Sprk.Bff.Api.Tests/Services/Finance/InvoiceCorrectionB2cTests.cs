using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using W = Sprk.Bff.Api.Tests.Services.Finance.CorrectionWorld;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 101 part B2c (FR-31, FR-25, D-126): timekeeper and date corrections re-resolve the roster match and the approved rate through the
/// REAL <see cref="InvoiceResolutionService"/> (a one-line input) and feed the re-evaluation; the invoice totals are recomputed in their own step; a
/// currency correction flags lines whose currency disagrees; a re-coded line reads as coded; a refused write sets the administrator's attention. Over
/// <see cref="CorrectionWorld"/>'s in-memory Dataverse and 072's real evaluator.
/// </summary>
public sealed class InvoiceCorrectionB2cTests
{
    private const int FeeIndex = 2; // CorrectionWorld's fee line is sequence 3
    private const int ExpenseIndex = 3;

    private static readonly BillingRuleCandidate Atk = Kit.Version("BILL-ATK", RuleType.ApprovedTimekeeper);
    private static readonly BillingRuleCandidate Rate = Kit.Version("BILL-RATE", RuleType.ApprovedRate);
    private static readonly BillingRuleCandidate Age = Kit.Version("BILL-AGE", RuleType.InvoiceAge);

    private static CorrectionCommand Line(string field, string? value, Guid? line = null) => new(W.InvoiceId, line ?? W.FeeLineId, field, value, false, W.Reviewer);

    private static CorrectionCommand Header(string field, string? value) => new(W.InvoiceId, null, field, value, false, W.Reviewer);

    private static InvoiceTotalsCalculation Totals(InvoiceTotalsRefusal? refusal = null) =>
        new(0m, 0m, 0m, 0m, 0m, new Dictionary<Guid, decimal>(), refusal);

    private static InvoiceCorrectionService Service(
        CorrectionWorld world, Func<Guid, CancellationToken, Task<InvoiceTotalsCalculation>>? recalculate = null, params BillingRuleCandidate[] rules)
    {
        var loaded = Kit.Loaded(rules);
        var reevaluation = new InvoiceCorrectionReevaluation(
            world.Entities.Object, (_, _) => Task.FromResult(loaded), Kit.Evaluator().Evaluate, world.WriteSignalAsync, world.CloseSignalAsync,
            NullLogger<InvoiceCorrectionReevaluation>.Instance, recalculate: recalculate);
        world.Seam = reevaluation.ApplyAsync;
        return world.Service();
    }

    private static JsonObject Root()
    {
        var root = JsonNode.Parse(W.HeaderFindingsJson)!.AsObject();
        root["schema"] = 1;
        root["kind"] = "Accepted";
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

    private static IEnumerable<JsonObject> Objects(JsonNode? array) => (array as JsonArray ?? []).OfType<JsonObject>();

    private static IEnumerable<string> Reasons(JsonObject root) => Objects(root["findings"]).Select(f => f["reasonCode"]!.GetValue<string>()).OrderBy(r => r);

    private static int StatusOf(CorrectionWorld world, Guid line) => world.Line(line).GetAttributeValue<OptionSetValue>("sprk_linestatus").Value;

    private static string Extraction(CorrectionWorld world) => world.Line().GetAttributeValue<string>("sprk_extractionconfidence");

    // =========================================================================================
    // Item 1 - timekeeper corrections
    // =========================================================================================

    [Fact(DisplayName = "101 B2c: a timekeeper correction re-resolves the rate through 046 with a one-line input and feeds the re-evaluation")]
    public async Task TimekeeperCorrection_ThatMatches_ReResolvesTheRate_AndReEvaluates()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        world.AddRate(lee.Id, 80m);
        var root = Root();
        root["findings"] = new JsonArray(
            Finding(0, Atk, RuleType.ApprovedTimekeeper, FeeIndex, BillingReasonCodes.TimekeeperNotOnRoster),
            Finding(1, Rate, RuleType.ApprovedRate, FeeIndex, BillingReasonCodes.RateTimekeeperUnmatched));
        Store(world, root);

        var result = await Service(world, null, Atk, Rate).CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        var input = world.ResolveInputs.Should().ContainSingle("a one-line input, not the whole invoice").Subject;
        input.FirmId.Should().Be(W.Firm);
        input.CoreRecord.Should().Be(new CoreRecordReference(CoreRecordKind.Project, W.Core));
        var line = input.Lines.Should().ContainSingle().Subject;
        line.TimekeeperAsBilled.Value.Should().Be("Alexandra Lee");
        line.Date.Value.Should().Be(new DateOnly(2026, 9, 12));

        var row = world.Line();
        row.GetAttributeValue<string>("sprk_timekeeper").Should().Be("Alexandra Lee");
        row.GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper").Id.Should().Be(lee.Id);
        row.GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(80m);
        var label = world.Corrections.Single();
        label.GetAttributeValue<string>("sprk_aivalue").Should().Be("A. Lee");
        label.GetAttributeValue<string>("sprk_finalvalue").Should().Be("Alexandra Lee");
        label.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(InvoiceCorrectionService.OutcomeCorrected);

        // The re-evaluation took the re-resolved facts: the unmatched-timekeeper finding is gone, the rate finding is now "above the approved rate".
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved);
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Flagged);
        var change = JsonNode.Parse(Extraction(world))![InvoiceCorrectionReevaluation.ResolutionChangeMember]!;
        change["before"]!["timekeeperId"].Should().BeNull("the line had no roster match before");
    }

    [Theory(DisplayName = "101 B2c SEEDED: a roster entry the resolution does not match for the line is 400 timekeeper_mismatch and nothing is written")]
    [InlineData("pending")]
    [InlineData("same-name-twice")]
    public async Task TimekeeperCorrection_ThatTheResolutionDoesNotMatch_IsRefused_AndWritesNothing(string scenario)
    {
        var world = new CorrectionWorld();
        var picked = scenario == "pending"
            ? world.AddPerson("Alexandra Lee", status: (int)TimekeeperStatus.Pending)
            : world.AddPerson("Alexandra Lee");
        if (scenario == "same-name-twice")
        {
            world.AddPerson("Alexandra Lee"); // two approved people fit the name: Ambiguous, none is picked
        }

        var result = await Service(world, null, Atk).CorrectAsync(Line("TimekeeperAsBilled", picked.Id.ToString("D")), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.TimekeeperMismatch);
        result.Code.Should().Be("ebilling.correction.timekeeper_mismatch");
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();
        world.RowTransactions.Should().Be(0);
        world.Line().Contains("sprk_resolvedtimekeeper").Should().BeFalse();
    }

    [Fact(DisplayName = "101 B2c: a timekeeper of another firm, or one that is not on the roster, is a 400 value_invalid and nothing is resolved")]
    public async Task TimekeeperCorrection_ToAnEntryOfAnotherFirm_IsRefused_BeforeResolving()
    {
        var world = new CorrectionWorld();
        var foreign = world.AddPerson("Alexandra Lee", firm: Guid.NewGuid());

        var other = await Service(world, null, Atk).CorrectAsync(Line("TimekeeperAsBilled", foreign.Id.ToString("D")), CancellationToken.None);
        var missing = await Service(world, null, Atk).CorrectAsync(Line("TimekeeperAsBilled", Guid.NewGuid().ToString("D")), CancellationToken.None);

        other.Code.Should().Be(CorrectionCodes.ValueInvalid);
        missing.Code.Should().Be(CorrectionCodes.ValueInvalid);
        world.ResolveInputs.Should().BeEmpty();
        world.Corrections.Should().BeEmpty();
    }

    [Fact(DisplayName = "101 B2c: repeating a timekeeper correction writes no second row and does not resolve again")]
    public async Task TimekeeperCorrection_Repeat_IsAlreadyApplied()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        world.AddRate(lee.Id, 80m);
        Store(world, Root());
        var service = Service(world, null, Atk, Rate);
        (await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None)).IsApplied.Should().BeTrue();
        var lineWrites = world.ValueWrites.Count(w => w.Entity == "sprk_billingevent");

        var repeat = await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        repeat.AlreadyApplied.Should().BeTrue();
        world.Corrections.Should().ContainSingle();
        world.ResolveInputs.Should().ContainSingle("the stored line already holds the result");
        world.ValueWrites.Count(w => w.Entity == "sprk_billingevent").Should().Be(lineWrites, "the line is not written again");
    }

    [Fact(DisplayName = "101 B2c: a timekeeper correction to the name already stored still writes the roster match and the rate")]
    public async Task TimekeeperCorrection_ToTheStoredName_StillWritesTheMatch()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_timekeeper"] = "Alexandra Lee"; // read correctly, but the person was not matched (e.g. the roster entry was approved later)
        var lee = world.AddPerson("Alexandra Lee");
        world.AddRate(lee.Id, 80m);
        Store(world, Root());

        var result = await Service(world, null, Atk, Rate).CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.Corrections.Single().GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(InvoiceCorrectionService.OutcomeConfirmed, "the name is as read");
        world.Line().GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper").Id.Should().Be(lee.Id);
        world.Line().GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(80m);
    }

    // =========================================================================================
    // Item 1 - date corrections
    // =========================================================================================

    [Fact(DisplayName = "101 B2c: a line-date correction re-resolves the match and rate for the new date and re-evaluates date-dependent rules")]
    public async Task LineDateCorrection_ReResolves_AndReEvaluates()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_timekeeper"] = "Alexandra Lee";
        world.Invoice["sprk_invoicedate"] = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var lee = world.AddPerson("Alexandra Lee", from: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)); // not approved on 2026-09-12
        world.AddRate(lee.Id, 80m);
        var root = Root();
        root["findings"] = new JsonArray(
            Finding(0, Atk, RuleType.ApprovedTimekeeper, FeeIndex, BillingReasonCodes.TimekeeperNotApprovedOnLineDate),
            Finding(1, Rate, RuleType.ApprovedRate, FeeIndex, BillingReasonCodes.RateTimekeeperUnmatched),
            Finding(2, Age, RuleType.InvoiceAge, FeeIndex, BillingReasonCodes.InvoiceTooOld));
        Store(world, root);

        var result = await Service(world, null, Atk, Rate, Age).CorrectAsync(Line("Date", "2026-12-20"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.ResolveInputs.Single().Lines.Single().Date.Value.Should().Be(new DateOnly(2026, 12, 20));
        world.Line().GetAttributeValue<DateTime>("sprk_eventdate").Should().Be(new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc));
        world.Line().GetAttributeValue<EntityReference>("sprk_resolvedtimekeeper").Id.Should().Be(lee.Id);
        world.Line().GetAttributeValue<Money>("sprk_approvedrate").Value.Should().Be(80m);
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.RateAboveApproved); // not-approved and too-old are gone
    }

    [Fact(DisplayName = "101 B2c: a line date that leaves the timekeeper unmatched is not refused; the finding keeps 046's reason for the new date")]
    public async Task LineDateCorrection_ThatStaysUnmatched_IsApplied_WithTheNewReason()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_timekeeper"] = "Nobody Known";
        var root = Root();
        root["findings"] = new JsonArray(Finding(0, Atk, RuleType.ApprovedTimekeeper, FeeIndex, BillingReasonCodes.TimekeeperNotOnRoster));
        Store(world, root);

        var result = await Service(world, null, Atk).CorrectAsync(Line("Date", "2026-09-13"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Reasons(Stored(world)).Should().Equal(BillingReasonCodes.TimekeeperNotOnRoster);
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Flagged);
    }

    [Fact(DisplayName = "101 B2c: an expense line's date is corrected without resolving a rate")]
    public async Task ExpenseLineDateCorrection_ResolvesNothing()
    {
        var world = new CorrectionWorld();
        Store(world, Root());

        var result = await Service(world, null, Age).CorrectAsync(Line("Date", "2026-09-20", W.ExpenseLineId), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.ResolveInputs.Should().BeEmpty();
        world.Line(W.ExpenseLineId).GetAttributeValue<DateTime>("sprk_eventdate").Should().Be(new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact(DisplayName = "101 B2c: a resolution that fails writes nothing; the repeat resolves again")]
    public async Task AResolutionFailure_WritesNothing_AndTheRepeatConverges()
    {
        var world = new CorrectionWorld();
        var lee = world.AddPerson("Alexandra Lee");
        world.AddRate(lee.Id, 80m);
        Store(world, Root());
        var service = Service(world, null, Atk, Rate);
        world.ResolveFault = new TimeoutException("simulated");

        var first = await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Unavailable);
        world.Corrections.Should().BeEmpty();
        world.ValueWrites.Should().BeEmpty();

        world.ResolveFault = null;
        (await service.CorrectAsync(Line("TimekeeperAsBilled", lee.Id.ToString("D")), CancellationToken.None)).IsApplied.Should().BeTrue();
    }

    [Fact(DisplayName = "101 B2c: an invoice-date correction re-evaluates the date-dependent rules through the same seam")]
    public async Task InvoiceDateCorrection_ReEvaluates()
    {
        var world = new CorrectionWorld();
        world.Invoice["sprk_invoicedate"] = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc); // 110 days after the line: too old
        var root = Root();
        root["findings"] = new JsonArray(Finding(0, Age, RuleType.InvoiceAge, FeeIndex, BillingReasonCodes.InvoiceTooOld));
        Store(world, root);

        var result = await Service(world, null, Age).CorrectAsync(Header("InvoiceDate", "2026-10-01"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        world.Invoice.GetAttributeValue<DateTime>("sprk_invoicedate").Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        Reasons(Stored(world)).Should().BeEmpty();
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Clean);
    }

    // =========================================================================================
    // Item 2 - totals
    // =========================================================================================

    [Theory(DisplayName = "101 B2c: an Amount, Hours, Rate or TotalDue correction recomputes the totals; a Narrative correction does not")]
    [InlineData("Amount", "2400", true, 1)]
    [InlineData("Hours", "20", true, 1)]
    [InlineData("Rate", "120", true, 1)]
    [InlineData("TotalDue", "2400", false, 1)]
    [InlineData("Narrative", "Prepared the brief", true, 0)]
    public async Task TheTotalsStep_RunsForTheBilledFigures(string field, string value, bool onLine, int expectedCalls)
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var calls = new List<Guid>();
        var service = Service(world, (id, _) =>
        {
            calls.Add(id);
            return Task.FromResult(Totals());
        });

        var result = await service.CorrectAsync(onLine ? Line(field, value) : Header(field, value), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        calls.Should().HaveCount(expectedCalls);
        calls.All(id => id == W.InvoiceId).Should().BeTrue();
    }

    [Fact(DisplayName = "101 B2c SEEDED: a totals failure is Incomplete (failedStep totals); the repeat writes no second row and recomputes once more")]
    public async Task ATotalsFailure_IsIncomplete_AndTheRepeatConverges()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var fail = true;
        var calls = 0;
        var service = Service(world, (_, _) =>
        {
            calls++;
            return fail ? Task.FromException<InvoiceTotalsCalculation>(new TimeoutException("simulated")) : Task.FromResult(Totals());
        });

        var first = await service.CorrectAsync(Line("Amount", "2400"), CancellationToken.None);

        first.Status.Should().Be(CorrectionStatus.Incomplete);
        first.FailedStep.Should().Be(CorrectionCodes.TotalsStep);
        world.Corrections.Should().ContainSingle("the value and the label are saved");
        world.Line().GetAttributeValue<Money>("sprk_amount").Value.Should().Be(2400m);

        fail = false;
        var repeat = await service.CorrectAsync(Line("Amount", "2400"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue();
        repeat.AlreadyApplied.Should().BeTrue();
        world.Corrections.Should().ContainSingle("no second row");
        calls.Should().Be(2);
    }

    [Fact(DisplayName = "101 B2c: a refused totals calculation is Incomplete at the totals step, like a line decision")]
    public async Task ARefusedCalculation_IsIncomplete()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var service = Service(world, (_, _) => Task.FromResult(Totals(new InvoiceTotalsRefusal(InvoiceTotalsRefusalCodes.ApprovedTotalNegative, "below zero"))));

        var result = await service.CorrectAsync(Header("TotalDue", "10"), CancellationToken.None);

        result.Status.Should().Be(CorrectionStatus.Incomplete);
        result.FailedStep.Should().Be(CorrectionCodes.TotalsStep);
    }

    [Fact(DisplayName = "101 B2c: the totals step runs after the status step")]
    public async Task TheTotalsStep_RunsAfterTheStatusStep()
    {
        var world = new CorrectionWorld();
        var cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap);
        var root = Root();
        root["findings"] = new JsonArray(Finding(0, cap, RuleType.DailyHoursCap, FeeIndex, BillingReasonCodes.DailyHoursOverCap));
        Store(world, root);
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Flagged);
        var statusAtTotals = -1;
        var service = Service(world, (_, _) =>
        {
            statusAtTotals = StatusOf(world, W.FeeLineId);
            return Task.FromResult(Totals());
        }, cap);

        (await service.CorrectAsync(Line("Hours", "2"), CancellationToken.None)).IsApplied.Should().BeTrue();

        statusAtTotals.Should().Be((int)InvoiceLineStatus.Clean, "the status step had already re-derived the line when the totals were recomputed");
    }

    // =========================================================================================
    // Item 3 - currency
    // =========================================================================================

    private static void GiveLinesACurrency(CorrectionWorld world)
    {
        world.Line()["sprk_currency"] = "USD";
        world.Line(W.ExpenseLineId)["sprk_currency"] = "USD";
    }

    [Fact(DisplayName = "101 B2c: a currency correction updates the invoice only; lines that now disagree get a currency.mismatch line finding, flagged, with no Signal; agreeing again clears them")]
    public async Task CurrencyCorrection_FlagsDisagreeingLines_ThenClearsThem()
    {
        var world = new CorrectionWorld();
        GiveLinesACurrency(world);
        Store(world, Root());
        var service = Service(world);

        (await service.CorrectAsync(Header("Currency", "EUR"), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.Invoice.GetAttributeValue<string>("sprk_currency").Should().Be("EUR");
        world.Line().GetAttributeValue<string>("sprk_currency").Should().Be("USD", "the line currencies are left as read");
        var findings = Objects(Stored(world)["lineFindings"]).ToList();
        findings.Should().HaveCount(2);
        findings.Should().OnlyContain(f => f["code"]!.GetValue<string>() == InvoiceSubmissionPersister.LineFindingCurrencyMismatch);
        findings.Select(f => f["line"]!.GetValue<int>()).Should().BeEquivalentTo([FeeIndex, ExpenseIndex]);
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Flagged);
        StatusOf(world, W.ExpenseLineId).Should().Be((int)InvoiceLineStatus.Flagged, "flagged like any other line finding");
        world.Signals.Should().BeEmpty("a currency mismatch is a finding only");
        world.SignalWrites.Should().BeEmpty();
        Stored(world).ContainsKey(InvoiceCorrectionReevaluation.PendingSignalChangesMember).Should().BeFalse();

        (await service.CorrectAsync(Header("Currency", "USD"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["lineFindings"]).Should().BeEmpty();
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Clean);
        StatusOf(world, W.ExpenseLineId).Should().Be((int)InvoiceLineStatus.Clean);
    }

    [Fact(DisplayName = "101 B2c: a decided line is not given a currency finding or a new status")]
    public async Task CurrencyCorrection_LeavesADecidedLine()
    {
        var world = new CorrectionWorld();
        GiveLinesACurrency(world);
        world.Line(W.ExpenseLineId)["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Accepted);
        Store(world, Root());

        (await Service(world).CorrectAsync(Header("Currency", "EUR"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["lineFindings"]).Select(f => f["line"]!.GetValue<int>()).Should().Equal(FeeIndex);
        StatusOf(world, W.ExpenseLineId).Should().Be((int)InvoiceLineStatus.Accepted);
    }

    [Fact(DisplayName = "101 B2c: lines with no currency, or the same one in another spelling, are not flagged; a repeat writes nothing more")]
    public async Task CurrencyCorrection_DoesNotFlagAnAgreeingLine()
    {
        var world = new CorrectionWorld();
        world.Line()["sprk_currency"] = "$"; // D-119: a bare dollar sign is USD
        Store(world, Root());
        var service = Service(world);

        (await service.CorrectAsync(Header("Currency", "USD"), CancellationToken.None)).IsApplied.Should().BeTrue();
        (await service.CorrectAsync(Header("Currency", "USD"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["lineFindings"]).Should().BeEmpty();
        world.ValueWrites.Count(w => w.Entity == "sprk_invoicesubmission").Should().Be(0, "nothing to patch, so the findings are not written");
    }

    // =========================================================================================
    // Item 4 - a re-coded line reads as coded
    // =========================================================================================

    private static CorrectionWorld Uncoded()
    {
        var world = new CorrectionWorld();
        world.Lines.Clear();
        var fee = W.FeeLine(status: (int)InvoiceLineStatus.Pending, extraction: W.ExtractionJson(coded: false));
        fee.Attributes.Remove("sprk_taskcodeasbilled");
        world.Lines.Add(fee);
        Store(world, Root());
        world.DefaultCodeSetId = W.CodeSetId;
        return world;
    }

    [Fact(DisplayName = "101 B2c: a re-coded line reads as coded in the review read model (the status the review page shows), not as NotCoded")]
    public async Task AReCodedLine_ReadsAsCoded_InTheReviewReadModel()
    {
        var world = Uncoded();
        LineExtraction.Parse(Extraction(world), 0.9m).NotCoded.Should().BeTrue("before the re-code the page says not coded");

        (await Service(world).CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var read = LineExtraction.Parse(Extraction(world), 0.9m);
        read.NotCoded.Should().BeFalse();
        read.CodingStatus.Should().Be("Coded");
        read.Task.Code.Should().Be("L120");
    }

    [Fact(DisplayName = "101 B2c: a re-code whose status step failed can be repeated (the line is still Pending but already reads as re-coded)")]
    public async Task AReCode_WhoseStatusStepFailed_CanBeRepeated()
    {
        var world = Uncoded();
        var service = Service(world);
        world.TransactionFault = r => r.OfType<UpdateRequest>().Any(u => u.Target.Contains("sprk_linestatus")) ? new TimeoutException("simulated") : null;

        var first = await service.CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None);

        first.FailedStep.Should().Be(CorrectionCodes.StatusStep);
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Pending);

        world.TransactionFault = null;
        var repeat = await service.CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None);

        repeat.IsApplied.Should().BeTrue($"{repeat.Code} {repeat.Detail} {repeat.FailedStep}");
        StatusOf(world, W.FeeLineId).Should().Be((int)InvoiceLineStatus.Clean);
        world.Corrections.Should().ContainSingle();
    }

    [Fact(DisplayName = "101 B2c: a reviewer's own code can be corrected again; the row names no AI source and is a Corrected label")]
    public async Task AReCodedLine_CanBeCorrectedAgain_WithNoAiSource()
    {
        var world = Uncoded();
        var service = Service(world);
        (await service.CorrectAsync(Line("TaskCode", "L120"), CancellationToken.None)).IsApplied.Should().BeTrue();

        var second = await service.CorrectAsync(Line("TaskCode", "L110"), CancellationToken.None);

        second.IsApplied.Should().BeTrue($"{second.Code} {second.Detail}");
        var rows = world.Corrections.Where(c => c.GetAttributeValue<string>("sprk_finalvalue") == "L110").ToList();
        var row = rows.Should().ContainSingle().Subject;
        row.GetAttributeValue<OptionSetValue>("sprk_outcometype").Value.Should().Be(InvoiceCorrectionService.OutcomeCorrected, "the AI offered no code");
        row.Contains("sprk_sourcecode").Should().BeFalse();
        row.Contains("sprk_aivalue").Should().BeFalse();
    }

    // =========================================================================================
    // Item 5 - admin attention for a refused write
    // =========================================================================================

    [Fact(DisplayName = "101 B2c: a plan entry the writer would refuse sets sprk_adminattention (the persister's text), and keeps the other segments")]
    public async Task ARefusedPlanEntry_SetsAdminAttention()
    {
        var world = new CorrectionWorld();
        world.Submission!["sprk_adminattention"] = "System billing policy BILL-SYSTEM-SUBMISSION is missing: system Signals were not created";
        Store(world, Root());
        var longCode = Kit.Version("BILL-" + new string('X', 60), RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");

        (await Service(world, null, longCode).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        Objects(Stored(world)["signalPlanRefusals"]).Should().ContainSingle();
        var attention = world.Submission!.GetAttributeValue<string>("sprk_adminattention");
        attention.Should().StartWith("1 Signal could not be created: ").And.Contain(SignalWriteRefusal.PolicyCodeTooLong);
        attention.Should().Contain("System billing policy BILL-SYSTEM-SUBMISSION is missing", "the other segments stay");
        attention.Should().NotContain("2.5", "codes only (ADR-015)");
    }

    [Fact(DisplayName = "101 B2c SEEDED: a Signal write refused permanently sets sprk_adminattention with the findings write")]
    public async Task APermanentlyRefusedSignalWrite_SetsAdminAttention()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var wholeHours = Kit.Version("BILL-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");
        world.SignalWriteFault = _ => new ArgumentException("simulated permanent refusal");

        var result = await Service(world, null, wholeHours).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None);

        result.IsApplied.Should().BeTrue();
        Objects(Stored(world)["signalWriteRefusals"]).Should().ContainSingle();
        world.Submission!.GetAttributeValue<string>("sprk_adminattention").Should().StartWith("1 Signal could not be created: ");
    }

    [Fact(DisplayName = "101 B2c: with no refusal sprk_adminattention is not written")]
    public async Task WithNoRefusal_AdminAttentionIsNotWritten()
    {
        var world = new CorrectionWorld();
        Store(world, Root());
        var wholeHours = Kit.Version("BILL-INC", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":1}""");

        (await Service(world, null, wholeHours).CorrectAsync(Line("Hours", "2.5"), CancellationToken.None)).IsApplied.Should().BeTrue();

        world.Submission!.Contains("sprk_adminattention").Should().BeFalse();
        world.ValueWrites.Where(w => w.Entity == "sprk_invoicesubmission").Should().OnlyContain(w => !w.Fields.ContainsKey("sprk_adminattention"));
    }
}
