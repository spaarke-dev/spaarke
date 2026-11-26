using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Services.Finance.InvoiceFindingsDiff;
using Kit = Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// ebilling-r1 task 164a-3: the added-line basis of <see cref="InvoiceFindingsDiff"/> (extracted from <see cref="InvoiceCorrectionReevaluation"/>),
/// over 072's real in-memory evaluator. The added line is a new subject of the "after" side; the stored lines' findings are left as stored when the
/// added line does not affect them; a rule that sums across lines (DailyHoursCap) counts the added line. The correction basis is proven by the
/// correction suites, which run unchanged.
/// </summary>
public sealed class InvoiceFindingsDiffTests
{
    private static readonly Guid InvoiceId = Guid.Parse("164a3000-0000-4000-8000-0000000000a1");
    private static readonly Guid SubmissionId = Guid.Parse("164a3000-0000-4000-8000-0000000000a2");
    private static readonly Guid Core = Guid.Parse("164a3000-0000-4000-8000-0000000000c0");
    private static readonly Guid StoredFeeId = Guid.Parse("164a3000-0000-4000-8000-0000000000b1");
    private static readonly Guid StoredExpenseId = Guid.Parse("164a3000-0000-4000-8000-0000000000b2");
    private static readonly Guid AddedId = Guid.Parse("164a3000-0000-4000-8000-0000000000b3");
    private static readonly DateTime Day = new(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);

    private static readonly BillingRuleCandidate Cap = Kit.Version("BILL-CAP", RuleType.DailyHoursCap); // 12 hours a day
    private static readonly BillingRuleCandidate HalfHours = Kit.Version("BILL-INC", RuleType.TimeIncrement); // 0.5

    private static Entity Invoice() => new("sprk_invoice", InvoiceId)
    {
        ["sprk_vendororg"] = new EntityReference("sprk_organization", Kit.FirmId),
        ["sprk_invoicenumber"] = "INV-164",
        ["sprk_invoicedate"] = new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc),
        ["sprk_currency"] = "USD",
        ["sprk_project"] = new EntityReference("sprk_project", Core),
    };

    private static Entity FeeRow(Guid id, int sequence, decimal hours, DateTime? date = null, string timekeeper = "A. Lee") => new("sprk_billingevent", id)
    {
        ["sprk_name"] = $"INV-164 line {sequence}",
        ["sprk_linesequence"] = sequence,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Fee),
        ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean),
        ["sprk_eventdate"] = date ?? Day,
        ["sprk_timekeeper"] = timekeeper,
        ["sprk_quantity"] = hours,
        ["sprk_rate"] = new Money(100m),
        ["sprk_amount"] = new Money(hours * 100m),
        ["sprk_description"] = "Drafted the motion",
    };

    private static Entity ExpenseRow(Guid id, int sequence, decimal amount) => new("sprk_billingevent", id)
    {
        ["sprk_name"] = $"INV-164 line {sequence}",
        ["sprk_linesequence"] = sequence,
        ["sprk_costtype"] = new OptionSetValue((int)InvoiceLineCostType.Expense),
        ["sprk_linestatus"] = new OptionSetValue((int)InvoiceLineStatus.Clean),
        ["sprk_amount"] = new Money(amount),
        ["sprk_description"] = "Court filing fee",
    };

    private static StoredLine Line(Entity row) => new(row.GetAttributeValue<int>("sprk_linesequence") - 1, row, Extraction: null, Decided: false);

    private static Dictionary<int, StoredLine> Stored(params Entity[] rows) => rows.Select(Line).ToDictionary(l => l.Index);

    private static RequestBasis Basis(IReadOnlyDictionary<int, StoredLine> lines, Entity added, JsonObject root) =>
        RequestBasis.ForAddedLine(Line(added), Invoice(), lines, provenance: null, new HashSet<(Guid?, string)>(), SubmissionId, root, ("sprk_project", Core));

    /// <summary>The stored findings as the persister wrote them for the stored lines: the entries and plan of the "before" evaluation.</summary>
    private static JsonObject Root(IReadOnlyDictionary<int, StoredLine> lines, params BillingRuleCandidate[] rules)
    {
        var root = new JsonObject
        {
            ["schema"] = 1,
            ["kind"] = "Accepted",
            ["trackingNumber"] = "INV-164.1",
            ["receivedOn"] = "2026-10-01",
            ["invoiceName"] = "INV-164",
            ["findings"] = new JsonArray(),
            ["signalPlan"] = new JsonArray(),
        };
        var before = Kit.Evaluator().Evaluate(
            RequestBasis.ForCorrection(
                new CorrectionApplied(InvoiceId, null, "Narrative", "Confirmed", false), Invoice(), lines, null, new HashSet<(Guid?, string)>(), SubmissionId,
                root, ("sprk_project", Core), null).Build(value: null, preVerified: false, before: true),
            Kit.Loaded(rules));
        var findings = (JsonArray)root["findings"]!;
        for (var i = 0; i < before.Findings.Count; i++)
        {
            findings.Add(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.FindingEntryJson(i, before.Findings[i], null, scrub: false));
        }

        var plan = (JsonArray)root["signalPlan"]!;
        foreach (var mapping in BillingFindingSignalMapper.MapAll(before, SubjectsOf(InvoiceId, root, lines)))
        {
            plan.Add(Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.SignalPlanEntryJson(mapping));
        }

        return root;
    }

    /// <summary>What the correction follow-through does with a basis (load once, evaluate twice, patch findings then plan), with the added-line sides.</summary>
    private static (BillingEvaluationResult Before, BillingEvaluationResult After, HashSet<int> Touched) Diff(JsonObject root, RequestBasis basis, params BillingRuleCandidate[] rules)
    {
        var loaded = Kit.Loaded(rules);
        var evaluator = Kit.Evaluator();
        var before = evaluator.Evaluate(basis.Build(before: true), loaded);
        var after = evaluator.Evaluate(basis.Build(before: false), loaded);
        var touched = new HashSet<int>();
        var decided = new HashSet<int>();
        var storedFindings = (root["findings"] as JsonArray)?.DeepClone() as JsonArray ?? [];
        PatchFindings(root, before, after, decided, touched);
        PatchPlan(root, before, after, storedFindings, SubjectsOf(InvoiceId, root, basis.LinesAfter), decided, touched);
        return (before, after, touched);
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? array) => (array as JsonArray ?? []).OfType<JsonObject>();

    private static string? Text(JsonNode? node, string name) => node?[name]?.GetValue<string>();

    // =========================================================================================
    // The added line is a new subject
    // =========================================================================================

    [Fact(DisplayName = "164a-3: the added line is on the 'after' side only, as a new Signal subject at index sequence - 1")]
    public void TheAddedLine_IsANewSubjectOfTheAfterSideOnly()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m), ExpenseRow(StoredExpenseId, 2, 40m));
        var basis = Basis(lines, FeeRow(AddedId, 3, 2.3m, Day.AddDays(1)), Root(lines));

        basis.Build(before: true).Lines.Select(l => l.Index).Should().Equal(0, 1);
        var after = basis.Build(before: false).Lines;
        after.Select(l => l.Index).Should().Equal(0, 1, 2);
        after[2].LineType.Should().Be(InvoiceLineType.Fee);
        after[2].Hours.Should().Be(2.3m);
        after[2].TimekeeperName.Should().Be("A. Lee");
        after[2].Date.Should().Be(new DateOnly(2026, 9, 13));

        var subjects = SubjectsOf(InvoiceId, Root(lines), basis.LinesAfter);
        subjects.LineIds.Should().ContainKey(2).WhoseValue.Should().Be(AddedId);
        subjects.LineDisplayNames![2].Should().Be("INV-164 line 3");
        SubjectsOf(InvoiceId, Root(lines), lines).LineIds.Should().NotContainKey(2, "the stored lines alone are the 'before' subjects");
    }

    [Fact(DisplayName = "164a-3: a rule on the added line adds its finding and its Signal plan entry on the added line, recorded as a raise")]
    public void ARuleOnTheAddedLine_AddsItsFindingAndPlanEntry_OnTheAddedLine()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m), ExpenseRow(StoredExpenseId, 2, 40m));
        var root = Root(lines, HalfHours);
        Objects(root["findings"]).Should().BeEmpty("8 hours is a multiple of 0.5");

        var (_, _, touched) = Diff(root, Basis(lines, FeeRow(AddedId, 3, 2.3m, Day.AddDays(1)), root), HalfHours);

        touched.Should().Equal(2);
        var finding = Objects(root["findings"]).Should().ContainSingle().Subject;
        finding["line"]!.GetValue<int>().Should().Be(2);
        Text(finding, "reasonCode").Should().Be(BillingReasonCodes.TimeNotAMultipleOfIncrement);
        var plan = Objects(root["signalPlan"]).Should().ContainSingle().Subject;
        Text(plan, "subjectEntity").Should().Be("sprk_billingevent");
        Text(plan, "subjectId").Should().Be(AddedId.ToString("D"));
        var raise = Objects(root[InvoiceCorrectionReevaluation.PendingSignalChangesMember]!["rule"]).Should().ContainSingle().Subject;
        Text(raise, "subjectId").Should().Be(AddedId.ToString("D"));
        raise["closeFingerprint"].Should().BeNull("nothing was stored for the added line");
        Text(raise, "raiseFingerprint").Should().Be(Text(plan, "fingerprint"));
    }

    // =========================================================================================
    // The stored lines' findings are unchanged when the added line does not affect them
    // =========================================================================================

    [Fact(DisplayName = "164a-3 must-not-fire: an added line no rule judges leaves the stored findings JSON byte-identical")]
    public void AnAddedLineNoRuleJudges_LeavesTheStoredFindingsByteIdentical()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 2.3m), ExpenseRow(StoredExpenseId, 2, 40m));
        var root = Root(lines, Cap, HalfHours);
        Objects(root["findings"]).Should().ContainSingle("the stored fee line's 2.3 hours is off the increment");
        var stored = root.ToJsonString();

        var (_, _, touched) = Diff(root, Basis(lines, ExpenseRow(AddedId, 3, 25m), root), Cap, HalfHours);

        root.ToJsonString().Should().Be(stored);
        touched.Should().BeEmpty();
    }

    [Fact(DisplayName = "164a-3: an added line with its own finding leaves the stored lines' findings and plan entries as stored")]
    public void AnAddedLineWithItsOwnFinding_LeavesTheStoredLinesEntriesAsStored()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 2.3m), ExpenseRow(StoredExpenseId, 2, 40m));
        var root = Root(lines, Cap, HalfHours);
        var storedFinding = Objects(root["findings"]).Single().ToJsonString();
        var storedPlan = Objects(root["signalPlan"]).Single().ToJsonString();

        var (_, _, touched) = Diff(root, Basis(lines, FeeRow(AddedId, 3, 1.3m, Day.AddDays(1)), root), Cap, HalfHours);

        touched.Should().Equal(2);
        Objects(root["findings"]).Where(e => e["line"]!.GetValue<int>() == 0).Select(e => e.ToJsonString()).Should().Equal(storedFinding);
        Objects(root["findings"]).Should().Contain(e => e["line"]!.GetValue<int>() == 2);
        Objects(root["signalPlan"]).Where(e => Text(e, "subjectId") == StoredFeeId.ToString("D")).Select(e => e.ToJsonString()).Should().Equal(storedPlan);
        Objects(root[InvoiceCorrectionReevaluation.PendingSignalChangesMember]!["rule"]).Should().OnlyContain(e => Text(e, "subjectId") == AddedId.ToString("D"));
    }

    // =========================================================================================
    // A rule that sums across lines sees the added line
    // =========================================================================================

    [Fact(DisplayName = "164a-3: DailyHoursCap sums the added line into its timekeeper-day, so the stored first line of the day takes the over-cap finding")]
    public void DailyHoursCap_CountsTheAddedLine()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m), ExpenseRow(StoredExpenseId, 2, 40m));
        var root = Root(lines, Cap);
        Objects(root["findings"]).Should().BeEmpty("8 hours is within the 12-hour cap");

        var (before, after, touched) = Diff(root, Basis(lines, FeeRow(AddedId, 3, 5m), root), Cap);

        before.Findings.Should().BeEmpty();
        after.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.DailyHoursOverCap);
        var finding = Objects(root["findings"]).Should().ContainSingle().Subject;
        Text(finding, "reasonCode").Should().Be(BillingReasonCodes.DailyHoursOverCap);
        finding["line"]!.GetValue<int>().Should().Be(0, "the finding sits on the first line of the timekeeper-day, a stored line");
        touched.Should().Contain(0);
        Objects(root["signalPlan"]).Should().ContainSingle(e => Text(e, "subjectId") == StoredFeeId.ToString("D") && Text(e, "policyCode") == "BILL-CAP");
    }

    [Fact(DisplayName = "164a-3 control: an added line on another day stays out of the stored line's timekeeper-day; nothing changes")]
    public void DailyHoursCap_AnAddedLineOnAnotherDay_ChangesNothing()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m), ExpenseRow(StoredExpenseId, 2, 40m));
        var root = Root(lines, Cap);
        var stored = root.ToJsonString();

        var (_, after, touched) = Diff(root, Basis(lines, FeeRow(AddedId, 3, 5m, Day.AddDays(1)), root), Cap);

        after.Findings.Should().BeEmpty();
        root.ToJsonString().Should().Be(stored);
        touched.Should().BeEmpty();
    }

    // =========================================================================================
    // The basis refuses a line it cannot add, and each kind is built its own way
    // =========================================================================================

    [Fact(DisplayName = "164a-3: an added line whose sequence is taken, or not after every stored line, is refused")]
    public void AnAddedLineWhoseSequenceIsNotTheNextOne_IsRefused()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m), ExpenseRow(StoredExpenseId, 2, 40m));

        ((Action)(() => Basis(lines, FeeRow(AddedId, 2, 1m), Root(lines)))).Should().Throw<ArgumentException>();
        ((Action)(() => Basis(lines, FeeRow(StoredFeeId, 3, 1m), Root(lines)))).Should().Throw<ArgumentException>("the id is a stored line's");
        ((Action)(() => RequestBasis.ForAddedLine(Line(FeeRow(AddedId, 3, 1m)) with { Index = 5 }, Invoice(), lines, null, new HashSet<(Guid?, string)>(),
            SubmissionId, Root(lines), ("sprk_project", Core)))).Should().Throw<ArgumentException>("the index is not the sequence - 1");
        ((Action)(() => RequestBasis.ForAddedLine(Line(FeeRow(AddedId, 3, 1m)) with { Decided = true }, Invoice(), lines, null, new HashSet<(Guid?, string)>(),
            SubmissionId, Root(lines), ("sprk_project", Core)))).Should().Throw<ArgumentException>("an added line is undecided");
    }

    [Fact(DisplayName = "164a-3: an added-line basis is not built as a correction, nor a correction basis as an added line")]
    public void EachBasisKind_IsBuiltItsOwnWay()
    {
        var lines = Stored(FeeRow(StoredFeeId, 1, 8m));
        var added = Basis(lines, FeeRow(AddedId, 2, 1m), Root(lines));
        var correction = RequestBasis.ForCorrection(
            new CorrectionApplied(InvoiceId, StoredFeeId, "Hours", "Corrected", true), Invoice(), lines, null, new HashSet<(Guid?, string)>(), SubmissionId,
            Root(lines), ("sprk_project", Core), null);

        ((Action)(() => added.Build("2", preVerified: true, before: false))).Should().Throw<InvalidOperationException>();
        ((Action)(() => correction.Build(before: false))).Should().Throw<InvalidOperationException>();
        correction.LinesAfter.Should().BeSameAs(lines);
        correction.Added.Should().BeNull();
        correction.Build("2", preVerified: true, before: false).Lines.Single().Hours.Should().Be(2m);
    }
}
