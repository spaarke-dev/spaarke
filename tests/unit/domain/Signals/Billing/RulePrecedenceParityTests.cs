using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 084e, ontology-r1 O-1 and O-5: the evaluator's precedence step (step 4) gives the same answer before and after it was extracted
/// into <c>RulePrecedence.Resolve</c>. Each fixture states the evaluator's full outcome as a literal snapshot: per version the subjects it judged
/// (<c>subjectsByVersion</c>), was displaced outright on (<c>displacedOutright</c>) and lost its cap on (<c>capDisplaced</c>), the refusals,
/// how many times each version's handler ran, the <c>billing.rule.*</c> meter calls and the warning/error log entries. The snapshots were taken
/// against the evaluator BEFORE the extraction (the test passed there first) and are unchanged after it, so they are the parity proof.
/// Handler calls are compared as counts per version: the order in which a tied top-rank version and a lower Split version are judged may differ
/// (O-1 allows it: both are judged either way, and the result is cached), which the Split tie fixture states.
/// </summary>
public class RulePrecedenceParityTests
{
    private const string HalfHour = """{"type":"TimeIncrement","incrementHours":0.5}""";
    private const string TenthHour = """{"type":"TimeIncrement","incrementHours":0.1}""";
    private const string CapOnly = """{"type":"ExpenseRule","maxAmountPerItem":500}""";
    private const string CodesOnly = """{"type":"ExpenseRule","nonReimbursableCodes":["E110"]}""";
    private const string CodesAndCap = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""";
    private const string ListsA101 = """{"type":"NonBillableActivity","codes":["A101"]}""";
    private const string ListsA102 = """{"type":"NonBillableActivity","codes":["A102"]}""";
    private const int NotAConsequence = 12345;

    private static readonly DateOnly LineDate = new(2026, 3, 2);
    private static readonly AsyncLocal<Guid> MetricScope = new();

    /// <summary>A fee line of 0.3 hours (off a 0.5 increment, on a 0.1 one) with activity code A101.</summary>
    private static BillingLineFacts Line(int index) => Fee(index) with { Date = LineDate, Hours = 0.3m, Amount = 172.5m, BilledTaskCode = null };

    /// <summary>An expense line of 600 with code E109: over a 500 cap, and non-reimbursable where E109 is listed.</summary>
    private static BillingLineFacts BadExpense(int index) =>
        Expense(index) with { Date = LineDate, ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m };

    // ── O-1: valid stays lazy ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "084e O-1 must-fire parity: Replace, a valid higher version and a lower one whose handler would refuse: the lower handler never runs, no refusal, no meter call, no log")]
    public void Replace_ValidHigher_LowerWhoseHandlerRefuses_IsNeverRun()
    {
        var higher = Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm);
        var lower = Version("CD-INC", RuleType.TimeIncrement, HalfHour);

        var run = Run(Request(Line(0)), Loaded(higher, lower), refuse: "CD-INC");

        run.Should().Be(Lines(
            "apply CD-INC applied=[] displaced=[invoice,line0] cap=[]",
            "apply FIRM-INC applied=[invoice,line0] displaced=[] cap=[]",
            "handler FIRM-INC x1"));
    }

    // ── O-5 fixtures ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "084e O-5 parity: Replace, a valid higher version displaces the lower one outright (it is never judged)")]
    public void Replace_ValidHigher_DisplacesTheLower()
    {
        var run = Run(Request(Line(0)), Loaded(
            Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm), Version("CD-INC", RuleType.TimeIncrement, HalfHour)));

        run.Should().Be(Lines(
            "apply CD-INC applied=[] displaced=[invoice,line0] cap=[]",
            "apply FIRM-INC applied=[invoice,line0] displaced=[] cap=[]",
            "handler FIRM-INC x1"));
    }

    [Fact(DisplayName = "084e O-5 parity: Replace, the higher version is refused by its handler, so the lower one stays in force")]
    public void Replace_HigherRefusedByItsHandler_LowerStaysInForce()
    {
        var run = Run(Request(Line(0)), Loaded(
            Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm), Version("CD-INC", RuleType.TimeIncrement, HalfHour)),
            refuse: "FIRM-INC");

        run.Should().Be(Lines(
            "apply CD-INC applied=[invoice,line0] displaced=[] cap=[]",
            "finding CD-INC line0 " + BillingReasonCodes.TimeNotAMultipleOfIncrement,
            "handler CD-INC x1",
            "handler FIRM-INC x1",
            "log Error " + OntologyWriterEvents.PolicyVersionInvalid.Id,
            "metric billing.rule.invalid " + OntologyWriterFailureReason.SchemaInvalid,
            "refusal FIRM-INC " + BillingRuleRefusedException.ParameterMissing));
    }

    [Fact(DisplayName = "084e O-5 parity: Replace, the higher version is downgraded, so the lower one stays in force beside it")]
    public void Replace_HigherDowngraded_LowerStaysInForce()
    {
        var run = Run(Request(Line(0)), Loaded(
            Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm) with { Consequence = NotAConsequence },
            Version("CD-INC", RuleType.TimeIncrement, HalfHour)));

        run.Should().Be(Lines(
            "apply CD-INC applied=[invoice,line0] displaced=[] cap=[]",
            "apply FIRM-INC applied=[invoice,line0] displaced=[] cap=[]",
            "finding CD-INC line0 " + BillingReasonCodes.TimeNotAMultipleOfIncrement,
            "handler CD-INC x1",
            "handler FIRM-INC x1",
            "log Warning " + OntologyWriterEvents.PolicyVersionConsequenceDowngraded.Id,
            "metric billing.rule.consequence_downgraded " + RuleType.TimeIncrement));
    }

    [Fact(DisplayName = "084e O-5 parity: Split, a capped higher version makes the lower one CapDisplaced; the lower one is still judged (its code check)")]
    public void Split_CappedHigher_LowerIsCapDisplaced_AndStillJudged()
    {
        var run = Run(Request(BadExpense(0)), Loaded(
            Version("FIRM-EXP", RuleType.ExpenseRule, CapOnly, RuleSetScope.Firm), Version("CD-EXP", RuleType.ExpenseRule, CodesAndCap)));

        run.Should().Be(Lines(
            "apply CD-EXP applied=[invoice,line0] displaced=[] cap=[invoice,line0]",
            "apply FIRM-EXP applied=[invoice,line0] displaced=[] cap=[]",
            "finding CD-EXP line0 " + BillingReasonCodes.ExpenseCodeNotReimbursable,
            "finding FIRM-EXP line0 " + BillingReasonCodes.ExpenseAmountOverCap,
            "handler CD-EXP x1",
            "handler FIRM-EXP x1"));
    }

    [Fact(DisplayName = "084e O-5 parity must-not-fire: Split, a capless higher version leaves the lower cap in force")]
    public void Split_CaplessHigher_LeavesTheLowerCapInForce()
    {
        var run = Run(Request(BadExpense(0)), Loaded(
            Version("FIRM-EXP", RuleType.ExpenseRule, CodesOnly, RuleSetScope.Firm), Version("CD-EXP", RuleType.ExpenseRule, CodesAndCap)));

        run.Should().Be(Lines(
            "apply CD-EXP applied=[invoice,line0] displaced=[] cap=[]",
            "apply FIRM-EXP applied=[invoice,line0] displaced=[] cap=[]",
            "finding CD-EXP line0 " + BillingReasonCodes.ExpenseAmountOverCap,
            "finding CD-EXP line0 " + BillingReasonCodes.ExpenseCodeNotReimbursable,
            "handler CD-EXP x1",
            "handler FIRM-EXP x1"));
    }

    [Fact(DisplayName = "084e O-5 parity: a tie at the top rank (a legacy firm rule and a Firm Procedure's rule): both apply, the lower one is displaced")]
    public void TieAtTop_BothApply_TheLowerIsDisplaced()
    {
        var kit = new ProcedureKit().Procedure("Firm guideline", Sprk.Bff.Api.Services.Finance.Procedures.ProcedureAppliesTo.Firm, FirmId);
        kit.Rule("Firm guideline", "PROC-INC", RuleType.TimeIncrement, HalfHour);

        var run = Run(Request(Line(0)), kit.Loaded(
            Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm), Version("CD-INC", RuleType.TimeIncrement, HalfHour)));

        run.Should().Be(Lines(
            "apply CD-INC applied=[] displaced=[invoice,line0] cap=[]",
            "apply FIRM-INC applied=[invoice,line0] displaced=[] cap=[]",
            "apply PROC-INC applied=[invoice,line0] displaced=[] cap=[]",
            "finding PROC-INC line0 " + BillingReasonCodes.TimeNotAMultipleOfIncrement,
            "handler FIRM-INC x1",
            "handler PROC-INC x1"));
    }

    [Fact(DisplayName = "084e O-1 parity: Split, a tie at the top and a lower version ordered before them: the same versions are judged once each (only their order may differ), the same refusals and meter calls")]
    public void Split_TieAtTop_WithALowerRefusingVersion_SameCountsAndRefusals()
    {
        var kit = new ProcedureKit().Procedure("Firm guideline", Sprk.Bff.Api.Services.Finance.Procedures.ProcedureAppliesTo.Firm, FirmId);
        kit.Rule("Firm guideline", "PROC-EXP", RuleType.ExpenseRule, CapOnly);

        // "A-CD-EXP" sorts before "FIRM-EXP", so the lower version comes first in its key's input order.
        var run = Run(Request(BadExpense(0)), kit.Loaded(
            Version("FIRM-EXP", RuleType.ExpenseRule, CapOnly, RuleSetScope.Firm), Version("A-CD-EXP", RuleType.ExpenseRule, CodesAndCap)),
            refuse: "A-CD-EXP");

        run.Should().Be(Lines(
            "apply FIRM-EXP applied=[invoice,line0] displaced=[] cap=[]",
            "apply PROC-EXP applied=[invoice,line0] displaced=[] cap=[]",
            "finding FIRM-EXP line0 " + BillingReasonCodes.ExpenseAmountOverCap,
            "finding PROC-EXP line0 " + BillingReasonCodes.ExpenseAmountOverCap,
            "handler A-CD-EXP x1",
            "handler FIRM-EXP x1",
            "handler PROC-EXP x1",
            "log Error " + OntologyWriterEvents.PolicyVersionInvalid.Id,
            "metric billing.rule.invalid " + OntologyWriterFailureReason.SchemaInvalid,
            "refusal A-CD-EXP " + BillingRuleRefusedException.ParameterMissing));
    }

    [Fact(DisplayName = "084e O-5 parity must-not-fire: Additive, nothing is displaced at any rank")]
    public void Additive_NothingIsDisplaced()
    {
        var run = Run(Request(Line(0)), Loaded(
            Version("FIRM-NB", RuleType.NonBillableActivity, ListsA102, RuleSetScope.Firm), Version("CD-NB", RuleType.NonBillableActivity, ListsA101)));

        run.Should().Be(Lines(
            "apply CD-NB applied=[invoice,line0] displaced=[] cap=[]",
            "apply FIRM-NB applied=[invoice,line0] displaced=[] cap=[]",
            "finding CD-NB line0 " + BillingReasonCodes.NonBillableCode,
            "handler CD-NB x1",
            "handler FIRM-NB x1"));
    }

    [Fact(DisplayName = "084e O-5 parity (R6): a legacy policy version is in the group: a legacy firm rule outranks a Procedure rule of the same key")]
    public void LegacyVersion_IsInTheGroup_AndOutranksAProcedureRule()
    {
        var kit = new ProcedureKit().Procedure("General");
        kit.Rule("General", "PROC-INC", RuleType.TimeIncrement, HalfHour);

        var run = Run(Request(Line(0)), kit.Loaded(Version("FIRM-INC", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm)));

        run.Should().Be(Lines(
            "apply FIRM-INC applied=[invoice,line0] displaced=[] cap=[]",
            "apply PROC-INC applied=[] displaced=[invoice,line0] cap=[]",
            "handler FIRM-INC x1"));
    }

    // ── The run and its snapshot ───────────────────────────────────────────────────────────────────────────────────────────────

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>Evaluates once, with the handler of <paramref name="refuse"/> (if any) throwing <see cref="BillingRuleRefusedException"/>, and
    /// prints everything precedence can change, sorted so the text compares.</summary>
    private static string Run(BillingEvaluationRequest request, BillingLoadedFacts loaded, string? refuse = null)
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var metrics = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name.StartsWith("billing.rule.", StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
            {
                if (tag.Key is "reason" or "rule_type" && tag.Value is string value)
                {
                    lock (metrics) metrics.Add($"metric {instrument.Name} {value}");
                }
            }
        });
        listener.Start();

        var invocations = new List<string>();
        var logger = new CapturingLogger<InvoiceComplianceEvaluator>();
        var evaluator = Evaluator(logger, (rule, origin, facts) =>
        {
            invocations.Add(rule.PolicyCode);
            return rule.PolicyCode == refuse
                ? throw new BillingRuleRefusedException(BillingRuleRefusedException.ParameterMissing, rule.RuleType, "test")
                : BillingRuleHandlers.Evaluate(rule, origin, facts);
        });

        var result = evaluator.Evaluate(request, loaded);

        var codes = loaded.Rules.ToDictionary(r => r.PolicyVersionId, r => r.PolicyCode);
        foreach (var version in loaded.Procedures.PolicyVersions)
        {
            codes[version.PolicyVersionId] = version.PolicyCode;
        }

        var lines = new List<string>();
        lines.AddRange(result.ProcedureBasis!.Applications.Select(a =>
            $"apply {codes[a.PolicyVersionId]} applied=[{Subjects(a.Applied)}] displaced=[{Subjects(a.Displaced)}] cap=[{Subjects(a.CapDisplaced)}]"));
        lines.AddRange(result.Findings.Select(f => $"finding {f.PolicyCode} {Subject(f.Subject)} {f.ReasonCode}"));
        lines.AddRange(invocations.GroupBy(c => c).Select(g => $"handler {g.Key} x{g.Count()}"));
        lines.AddRange(logger.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => $"log {e.Level} {e.EventId.Id}"));
        lock (metrics) lines.AddRange(metrics);
        lines.AddRange(result.Refusals.Select(r => $"refusal {r.PolicyCode} {r.ReasonCode}"));
        return string.Join("\n", lines.Order(StringComparer.Ordinal));
    }

    private static string Subjects(IReadOnlyList<BillingSubject> subjects) => string.Join(",", subjects.Select(Subject));

    private static string Subject(BillingSubject subject) =>
        subject.Kind == BillingSubjectKind.Invoice ? "invoice" : "line" + subject.LineIndex;
}
