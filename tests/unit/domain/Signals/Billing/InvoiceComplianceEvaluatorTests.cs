using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Sprk.Bff.Api.Telemetry;
using Xunit;
using Xunit.Abstractions;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 072: <see cref="InvoiceComplianceEvaluator"/>'s in-memory evaluation (D-23). Rule selection per line date
/// (task 076 K3), firm over client default (D-39), the billing gate and handler refusals (FR-24), currency (D-20), ordering and
/// determinism, and the 150-line timing. The handlers are 071's, run for real; only the facts the loader would read are given here.
/// </summary>
public class InvoiceComplianceEvaluatorTests
{
    private readonly ITestOutputHelper _output;

    public InvoiceComplianceEvaluatorTests(ITestOutputHelper output) => _output = output;

    private static readonly DateOnly EarlyLine = new(2026, 3, 2);
    private static readonly DateOnly LateLine = new(2026, 3, 20);
    private static readonly DateOnly MidMarch = new(2026, 3, 15);

    /// <summary>A fee line of 0.3 hours: a multiple of 0.1, not of 0.5. Its activity code is not listed by any test rule below.</summary>
    private static BillingLineFacts Line(int index, DateOnly? date) =>
        Fee(index) with { Date = date, Hours = 0.3m, Amount = 172.5m, ActivityCode = "A102", BilledActivityCode = "A102" };

    private static BillingRuleCandidate Increment(string code, string hours, RuleSetScope scope = RuleSetScope.ClientDefault,
        DateOnly? from = null, DateOnly? to = null, bool current = true, int number = 1) =>
        Version(code, RuleType.TimeIncrement, $$"""{"type":"TimeIncrement","incrementHours":{{hours}}}""", scope, from, to, current, number);

    private static BillingEvaluationResult Evaluate(BillingEvaluationRequest request, BillingLoadedFacts loaded) =>
        Evaluator().Evaluate(request, loaded);

    // ── D-39: firm over client default ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AFirmRule_OverridesTheClientDefaultOfTheSameType_OnlyTheFirmsIsEvaluated_D39()
    {
        var clientDefault = Increment("CD-INCREMENT", "0.5");   // fires on 0.3 hours
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm); // holds

        var alone = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault));
        var both = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, firm));

        alone.Findings.Should().ContainSingle("control: the client default alone fires").Which.PolicyCode.Should().Be("CD-INCREMENT");
        both.Findings.Should().BeEmpty("the firm's TimeIncrement replaces the client default's for this line");
    }

    [Fact]
    public void AFirmRule_DoesNotOverride_AClientDefaultOfAnotherType()
    {
        var clientDefault = Version("CD-NONBILLABLE", RuleType.NonBillableActivity, """{"type":"NonBillableActivity","codes":["A102"]}""");
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm);

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, firm));

        result.Findings.Should().ContainSingle().Which.PolicyCode.Should().Be("CD-NONBILLABLE");
    }

    [Fact]
    public void Precedence_IsDecidedPerLine_OnEachLinesOwnDate()
    {
        // The firm rule is in force only from mid-March: the early line is judged by the client default, the late one by the firm.
        var clientDefault = Increment("CD-INCREMENT", "0.5");
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm, from: MidMarch);

        var result = Evaluate(Request(Line(0, EarlyLine), Line(1, LateLine)), Loaded(clientDefault, firm));

        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.PolicyCode.Should().Be("CD-INCREMENT");
        finding.Subject.Should().Be(BillingSubject.ForLine(0), "the client default's finding on the late line is dropped: the firm rule applies there");
    }

    [Fact]
    public void ARefusedFirmVersion_DoesNotRemoveTheClientDefault()
    {
        var clientDefault = Increment("CD-INCREMENT", "0.5");
        var brokenFirm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement"}""", RuleSetScope.Firm);

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, brokenFirm));

        result.Findings.Should().ContainSingle().Which.PolicyCode.Should().Be("CD-INCREMENT");
        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            brokenFirm.PolicyId, "FIRM-INCREMENT", brokenFirm.PolicyVersionId, BillingEvaluationReasons.GateRefused));
    }

    [Fact]
    public void AFirmVersionItsHandlerRefuses_LeavesTheClientDefaultInForce_B2()
    {
        // Owner, 2026-10-18 (072 review B2): the client default applies when the firm version cannot be evaluated, whether the gate
        // or the handler refuses it.
        var clientDefault = Increment("CD-INCREMENT", "0.5");   // fires on 0.3 hours
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm);
        var evaluator = Evaluator(handlers: (rule, origin, facts) => rule.PolicyCode == "FIRM-INCREMENT"
            ? throw new BillingRuleRefusedException(BillingRuleRefusedException.ParameterMissing, rule.RuleType, "test")
            : BillingRuleHandlers.Evaluate(rule, origin, facts));

        var result = evaluator.Evaluate(Request(Line(0, EarlyLine), Line(1, LateLine)), Loaded(clientDefault, firm));

        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.PolicyCode == "CD-INCREMENT",
            "the refused firm version does not displace the client default on any line");
        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            firm.PolicyId, "FIRM-INCREMENT", firm.PolicyVersionId, BillingRuleRefusedException.ParameterMissing));
    }

    [Fact]
    public void AClientDefaultDisplacedByAValidFirmVersion_IsNotRun()
    {
        // Control for B2's lazy handler run: a displaced client default is never judged, so its handler cannot refuse or log.
        var clientDefault = Increment("CD-INCREMENT", "0.5");
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm);
        var ran = new List<string>();
        var evaluator = Evaluator(handlers: (rule, origin, facts) =>
        {
            ran.Add(rule.PolicyCode);
            return BillingRuleHandlers.Evaluate(rule, origin, facts);
        });

        var result = evaluator.Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, firm));

        result.Findings.Should().BeEmpty();
        ran.Should().Equal("FIRM-INCREMENT");
    }

    // ── In force (task 076 K3, spec MUST: only published versions) ───────────────────────────────────────────────

    [Fact]
    public void AVersionInForceOnlyAfterTheLineDate_DoesNotApplyToThatLine()
    {
        var rule = Increment("CD-INCREMENT", "0.5", from: MidMarch);

        var result = Evaluate(Request(Line(0, EarlyLine), Line(1, LateLine)), Loaded(rule));

        result.Findings.Should().ContainSingle().Which.Subject.Should().Be(BillingSubject.ForLine(1));
    }

    [Fact]
    public void ASupersededVersion_AppliesInsideItsWindow_TheEndDateIsExclusive()
    {
        var supersededOn = new DateOnly(2026, 3, 10);
        var v1 = Increment("CD-INCREMENT", "0.5", from: RulesFrom, to: supersededOn, current: false, number: 1);
        var v2 = Increment("CD-INCREMENT", "0.1", from: supersededOn, current: true, number: 2);

        var result = Evaluate(Request(Line(0, EarlyLine), Line(1, supersededOn)), Loaded(v1, v2));

        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.PolicyVersionId.Should().Be(v1.PolicyVersionId, "the line dated before the publish is judged by the version then in force");
    }

    [Theory]
    [InlineData("a draft (an edit not yet published)")]
    [InlineData("a rejected version")]
    public void AVersionThatIsNotCurrentAndHasNoEndDate_IsNeverEvaluated_K3(string what)
    {
        // A draft and a rejected version look the same to the evaluator: not the policy's current version, no end date, and an
        // in-force-from in the past (an edit creates its version with sprk_inforcefrom = now).
        var published = Increment("CD-INCREMENT", "0.1", number: 1);
        var notPublished = Increment("CD-INCREMENT", "0.5", from: RulesFrom, to: null, current: false, number: 2);
        var orphan = Increment("CD-ORPHAN", "0.5", from: RulesFrom, to: null, current: false, number: 1);

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(published, notPublished, orphan));

        result.Findings.Should().BeEmpty($"{what} is never in force");
        result.Refusals.Should().BeEmpty("it is not even selected, so the gate never sees it");
    }

    [Fact]
    public void IsInForceOn_AVersionThatIsNotCurrentAndHasNoEndDate_IsNeverInForce_OnAnyDate_K3()
    {
        // The second K3 layer on its own: whatever the loader returns, such a version never applies.
        var draft = Increment("CD-INCREMENT", "0.5", from: RulesFrom, to: null, current: false, number: 2);
        var ended = draft with { InForceTo = MidMarch };
        var current = draft with { IsCurrent = true };

        new[] { RulesFrom, EarlyLine, LateLine, new DateOnly(2030, 1, 1) }.Should().OnlyContain(d => !draft.IsInForceOn(d));
        draft.CanBeInForce.Should().BeFalse();
        ended.IsInForceOn(EarlyLine).Should().BeTrue();
        ended.IsInForceOn(MidMarch).Should().BeFalse("the end date is exclusive");
        current.IsInForceOn(LateLine).Should().BeTrue();
        current.IsInForceOn(RulesFrom.AddDays(-1)).Should().BeFalse("not before its in-force-from");
    }

    [Fact]
    public void TheCurrentVersion_StillApplies_WhenAFailedPublishLeftAnEndDateOnIt()
    {
        var current = Increment("CD-INCREMENT", "0.5", to: new DateOnly(2026, 2, 1), current: true);

        Evaluate(Request(Line(0, EarlyLine)), Loaded(current)).Findings.Should().ContainSingle("the pointer alone decides (task 076)");
    }

    [Fact]
    public void TwoVersionsInForceOnOneDate_TheCurrentOneApplies()
    {
        var ended = Increment("CD-INCREMENT", "0.5", from: RulesFrom, to: new DateOnly(2026, 12, 31), current: false, number: 1);
        var current = Increment("CD-INCREMENT", "0.1", from: new DateOnly(2026, 3, 1), current: true, number: 2);

        Evaluate(Request(Line(0, EarlyLine)), Loaded(ended, current)).Findings.Should().BeEmpty();
    }

    [Fact]
    public void ALineWithNoDate_TakesTheInvoiceDate_AndWithNoDateAtAll_TheReceivedDate()
    {
        var supersededOn = new DateOnly(2026, 3, 10);
        var v1 = Increment("CD-INCREMENT", "0.1", from: RulesFrom, to: supersededOn, current: false, number: 1);
        var v2 = Increment("CD-INCREMENT", "0.5", from: supersededOn, current: true, number: 2);

        var onInvoiceDate = Evaluate(Request(Line(0, null)) with { InvoiceDate = EarlyLine }, Loaded(v1, v2));
        var undated = Evaluate(Request(Line(0, null)) with { InvoiceDate = null }, Loaded(v1, v2));

        onInvoiceDate.Findings.Should().BeEmpty("the undated line is judged on the invoice date, when v1 (0.1) was in force");
        undated.Findings.Should().ContainSingle("judged on the received date, 2026-04-02, when v2 is in force")
            .Which.PolicyVersionId.Should().Be(v2.PolicyVersionId);
    }

    private static readonly DateOnly FutureEffective = new(2026, 5, 1);

    [Fact]
    public void AnUndatedInvoice_ReceivedBeforeAFutureEffectiveCurrentVersion_IsJudgedByTheEarlierVersion_A3()
    {
        // 072 review A3: the current version was published with a later effective date. A subject with no line date and no invoice
        // date is judged on the received date (data from 049), so the version in force then applies, not the current pointer.
        var earlier = Increment("CD-INCREMENT", "0.5", from: RulesFrom, to: FutureEffective, current: false, number: 1);
        var future = Increment("CD-INCREMENT", "0.1", from: FutureEffective, current: true, number: 2);
        var undated = Request(Line(0, null)) with { InvoiceDate = null, ReceivedOn = new DateOnly(2026, 4, 2) };
        var receivedLater = undated with { ReceivedOn = new DateOnly(2026, 5, 2) };

        var result = Evaluate(undated, Loaded(earlier, future));
        var control = Evaluate(receivedLater, Loaded(earlier, future));

        result.Findings.Should().ContainSingle("v1 (0.5) was in force when the invoice was received")
            .Which.PolicyVersionId.Should().Be(earlier.PolicyVersionId);
        control.Findings.Should().BeEmpty("control: received after the effective date, the current version (0.1) judges it");
    }

    [Fact]
    public void AnUndatedInvoice_ReceivedBeforeTheOnlyVersionTakesEffect_IsJudgedByNone_A3()
    {
        var future = Increment("CD-INCREMENT", "0.5", from: FutureEffective, current: true, number: 1);
        var undated = Request(Line(0, null)) with { InvoiceDate = null, ReceivedOn = new DateOnly(2026, 4, 2) };

        var result = Evaluate(undated, Loaded(future));

        result.Findings.Should().BeEmpty("no version of the policy was in force on the received date");
        result.Refusals.Should().BeEmpty();
        result.RulesByVersion.Should().BeEmpty();
    }

    // ── FR-24: refusals ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AVersionTheGateRefuses_ProducesNoFinding_AndTheOtherRulesStillRun()
    {
        var broken = Version("CD-BROKEN", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":"x"}""");
        var good = Version("CD-NONBILLABLE", RuleType.NonBillableActivity, """{"type":"NonBillableActivity","codes":["A102"]}""");

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(broken, good));

        result.Findings.Should().ContainSingle().Which.PolicyCode.Should().Be("CD-NONBILLABLE");
        result.Refusals.Should().ContainSingle().Which.PolicyVersionId.Should().Be(broken.PolicyVersionId);
    }

    // ── 101 B2a F2 review (ontology-r1): a refused version's scope drops a subject a same-key version judged (D-39) ──

    [Fact]
    public void ARefusedFirmVersion_WithNoClientDefaultOfItsKey_IsScopedToItsSubjects_FailsClosed_F2()
    {
        var firm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":"x"}""", RuleSetScope.Firm);

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(firm));

        var scope = result.RefusalScopes.Should().ContainSingle().Which;
        scope.PolicyVersionId.Should().Be(firm.PolicyVersionId);
        scope.RuleType.Should().Be(RuleType.TimeIncrement);
        scope.AppliesToInvoice.Should().BeTrue();
        scope.LineIndexes.Should().Equal(0);
    }

    [Fact]
    public void ARefusedFirmVersion_WhoseClientDefaultOfTheSameTypeJudgedTheSubjects_LeavesNoSubjectUnchecked_D39_F2()
    {
        var clientDefault = Increment("CD-INCREMENT", "0.5");
        var firm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":"x"}""", RuleSetScope.Firm);

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, firm));

        result.Findings.Should().ContainSingle(f => f.PolicyCode == "CD-INCREMENT", "the client default stays in force (D-39) and judged the line");
        result.Refusals.Should().ContainSingle().Which.PolicyVersionId.Should().Be(firm.PolicyVersionId);
        var scope = result.RefusalScopes.Should().ContainSingle("the scopes stay aligned 1:1 with the refusals").Which;
        scope.PolicyVersionId.Should().Be(firm.PolicyVersionId);
        scope.RuleType.Should().Be(RuleType.TimeIncrement);
        scope.AppliesToInvoice.Should().BeFalse();
        scope.LineIndexes.Should().BeEmpty();
    }

    private static readonly AsyncLocal<Guid> MetricScope = new();

    [Fact]
    public void AHandlerRefusal_IsLoggedAndMetered_ProducesNoFinding_AndTheOtherRulesStillRun()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var reasons = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName && instrument.Name == "billing.rule.invalid") l.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            if (MetricScope.Value != scope) return;
            foreach (var tag in tags)
                if (tag.Key == "reason" && tag.Value is string reason)
                    lock (reasons) reasons.Add(reason);
        });
        listener.Start();

        var refused = Increment("CD-INCREMENT", "0.5");
        var good = Version("CD-NONBILLABLE", RuleType.NonBillableActivity, """{"type":"NonBillableActivity","codes":["A102"]}""");
        var logger = new CapturingLogger<InvoiceComplianceEvaluator>();
        var evaluator = Evaluator(logger, (rule, origin, facts) => rule.PolicyCode == "CD-INCREMENT"
            ? throw new BillingRuleRefusedException(BillingRuleRefusedException.ParameterMissing, rule.RuleType, "test")
            : BillingRuleHandlers.Evaluate(rule, origin, facts));

        var result = evaluator.Evaluate(Request(Line(0, EarlyLine)), Loaded(refused, good));

        result.Findings.Should().ContainSingle().Which.PolicyCode.Should().Be("CD-NONBILLABLE");
        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            refused.PolicyId, "CD-INCREMENT", refused.PolicyVersionId, BillingRuleRefusedException.ParameterMissing));
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.EventId.Id == OntologyWriterEvents.PolicyVersionInvalid.Id);
        lock (reasons) reasons.Should().Equal(OntologyWriterFailureReason.SchemaInvalid);
    }

    // ── 073 review K2: a stored consequence the type may not carry is downgraded, the check survives ──────────────

    [Fact]
    public void AStoredApprovedTimekeeperRejectClientDefault_IsEvaluatedAsFlag_TheCheckSurvives_K2()
    {
        var scope = Guid.NewGuid();
        MetricScope.Value = scope;
        var downgrades = new List<string>();
        var invalid = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == OntologyWriterTelemetry.MeterName
                    && instrument.Name is "billing.rule.consequence_downgraded" or "billing.rule.invalid")
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
                if (tag.Value is not string value) continue;
                if (instrument.Name == "billing.rule.consequence_downgraded" && tag.Key == "rule_type") lock (downgrades) downgrades.Add(value);
                if (instrument.Name == "billing.rule.invalid") lock (invalid) invalid.Add(value);
            }
        });
        listener.Start();

        var stored = Version("CD-TIMEKEEPER", RuleType.ApprovedTimekeeper) with { Consequence = (int)BillingConsequence.Reject };
        var unlisted = Fee(0) with { Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NotOnRoster) };
        var logger = new CapturingLogger<InvoiceComplianceEvaluator>();

        var result = Evaluator(logger).Evaluate(Request(unlisted), Loaded(stored));

        result.Findings.Should().ContainSingle("the client default's check survives").Which.RuleType.Should().Be(RuleType.ApprovedTimekeeper);
        result.RulesByVersion[stored.PolicyVersionId].Should().Be(stored with { Consequence = (int)BillingConsequence.Flag });
        result.Refusals.Should().BeEmpty();
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("evaluated with Flag"))
            .Which.EventId.Id.Should().Be(OntologyWriterEvents.PolicyVersionConsequenceDowngraded.Id)
            .And.NotBe(OntologyWriterEvents.PolicyVersionInvalid.Id, "an alert on the refusal id must not count a version that is still evaluated");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        lock (downgrades) downgrades.Should().Equal(nameof(RuleType.ApprovedTimekeeper));
        lock (invalid) invalid.Should().BeEmpty("the gate did not refuse it");
    }

    private const string ExpenseCodesOnly = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"]}""";

    private static BillingLineFacts BadExpense(int index) =>
        Expense(index) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m };

    [Fact]
    public void ADowngradedFirmVersion_DoesNotDisplaceTheClientDefault_D39_D116_K2()
    {
        // 072 W review F1-b. A firm ExpenseRule stored as SuggestAdjustment (a type that CAN reject, but not suggest) is evaluated as
        // Flag. D-116: "a firm rule version that cannot be evaluated leaves the client default in force", so the client default's
        // Reject still runs; the downgraded firm version still produces its own Flag finding.
        var clientDefault = Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = (int)BillingConsequence.Reject };
        var firm = Version("FIRM-EXPENSE", RuleType.ExpenseRule, ExpenseCodesOnly, RuleSetScope.Firm)
            with { Consequence = (int)BillingConsequence.SuggestAdjustment };

        var result = Evaluate(Request(BadExpense(0)), Loaded(clientDefault, firm));

        result.Refusals.Should().BeEmpty();
        var client = result.Findings.Where(f => f.PolicyVersionId == clientDefault.PolicyVersionId).ToList();
        client.Select(f => f.ReasonCode).Should().BeEquivalentTo([BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap],
            "the client default's Reject still runs");
        result.RulesByVersion[clientDefault.PolicyVersionId].Consequence.Should().Be((int)BillingConsequence.Reject);
        var firmFinding = result.Findings.Where(f => f.PolicyVersionId == firm.PolicyVersionId).Should().ContainSingle("the firm version still runs").Subject;
        firmFinding.ReasonCode.Should().Be(BillingReasonCodes.ExpenseCodeNotReimbursable);
        result.RulesByVersion[firm.PolicyVersionId].Consequence.Should().Be((int)BillingConsequence.Flag, "the downgraded version can never reject");
    }

    [Fact]
    public void AValidFirmVersion_StillDisplacesTheClientDefault_Control_D39()
    {
        // Control for the test above: a firm version with an allowed consequence displaces the client default, as ever.
        var clientDefault = Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = (int)BillingConsequence.Reject };
        var firm = Version("FIRM-EXPENSE", RuleType.ExpenseRule, ExpenseCodesOnly, RuleSetScope.Firm)
            with { Consequence = (int)BillingConsequence.Flag };

        var result = Evaluate(Request(BadExpense(0)), Loaded(clientDefault, firm));

        result.Findings.Should().ContainSingle().Which.PolicyVersionId.Should().Be(firm.PolicyVersionId);
    }

    [Fact]
    public void ANullConsequenceFirmVersion_StillDisplacesTheClientDefaultsReject_KnownLimit_073()
    {
        // Pinned, not endorsed as ideal: null means "the type default" and is a valid authored row, so the firm version counts and
        // the client default's Reject for the same type does not run. 073's known limit; see the 072 notes, section 13.
        var clientDefault = Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = (int)BillingConsequence.Reject };
        var firm = Version("FIRM-EXPENSE", RuleType.ExpenseRule, ExpenseCodesOnly, RuleSetScope.Firm) with { Consequence = null };

        var result = Evaluate(Request(BadExpense(0)), Loaded(clientDefault, firm));

        result.Findings.Should().ContainSingle().Which.PolicyVersionId.Should().Be(firm.PolicyVersionId);
    }

    [Fact]
    public void ADowngradedFirmVersion_OfATypeThatOnlyFlags_LeavesTheClientDefaultInForce_K2()
    {
        // A firm TimeIncrement stored as Reject is evaluated as Flag (and holds here, 0.3 is a multiple of 0.1); the client default
        // stays in force and fires on the line.
        var clientDefault = Increment("CD-INCREMENT", "0.5");
        var firm = Increment("FIRM-INCREMENT", "0.1", RuleSetScope.Firm) with { Consequence = (int)BillingConsequence.Reject };

        var result = Evaluate(Request(Line(0, EarlyLine)), Loaded(clientDefault, firm));

        result.Findings.Should().ContainSingle().Which.PolicyCode.Should().Be("CD-INCREMENT");
        result.Refusals.Should().BeEmpty();
    }

    [Fact]
    public void AVersionWithAnInvalidBody_AndADisallowedConsequence_IsStillRefused_AndDropped_K2()
    {
        var broken = Version("CD-TIMEKEEPER", RuleType.ApprovedTimekeeper, """{"type":"ApprovedTimekeeper","extra":1}""")
            with { Consequence = (int)BillingConsequence.Reject };
        var unlisted = Fee(0) with { Timekeeper = Unmatched(Sprk.Bff.Api.Services.Finance.Resolution.TimekeeperUnmatchedReason.NotOnRoster) };

        var result = Evaluate(Request(unlisted), Loaded(broken));

        result.Findings.Should().BeEmpty();
        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            broken.PolicyId, "CD-TIMEKEEPER", broken.PolicyVersionId, BillingEvaluationReasons.GateRefused));
    }

    // ── ReceivedOn is required data, never the default date (072 part W) ─────────────────────────────────────────

    [Fact]
    public async Task ADefaultReceivedOn_IsRefused_BeforeAnythingIsReadOrJudged()
    {
        var request = Request(Line(0, null)) with { InvoiceDate = null, ReceivedOn = default };

        var evaluate = () => Evaluate(request, Loaded(Increment("CD-INCREMENT", "0.5")));
        var evaluateAsync = () => Evaluator().EvaluateAsync(request, CancellationToken.None);

        evaluate.Should().Throw<ArgumentException>().WithParameterName("request").WithMessage("*ReceivedOn*");
        // The kit's loader is a strict mock with no setup: reaching it would throw a MockException, not this.
        await evaluateAsync.Should().ThrowAsync<ArgumentException>().WithParameterName("request");
    }

    [Fact]
    public void ARealReceivedOn_JudgesAnUndatedSubject_Control()
    {
        var request = Request(Line(0, null)) with { InvoiceDate = null };

        Evaluate(request, Loaded(Increment("CD-INCREMENT", "0.5"))).Findings.Should().ContainSingle();
    }

    [Fact]
    public void AnyOtherHandlerException_IsADefect_AndPropagates()
    {
        var evaluator = Evaluator(handlers: (_, _, _) => throw new InvalidOperationException("defect"));

        var act = () => evaluator.Evaluate(Request(Line(0, EarlyLine)), Loaded(Increment("CD-INCREMENT", "0.5")));

        act.Should().Throw<InvalidOperationException>().WithMessage("defect");
    }

    // ── D-20: currency ───────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string, string?, decimal?> UnsupportedCurrencies => new()
    {
        { BillingEvaluationReasons.CurrencyNotUsd, "EUR", 0.95m },
        { BillingEvaluationReasons.CurrencyMissing, null, null },
        { BillingEvaluationReasons.CurrencyMissing, "  ", 0.95m },
        { BillingEvaluationReasons.CurrencyUnreliable, "USD", 0.50m },
        { BillingEvaluationReasons.CurrencyUnreliable, "USD", null },
        { BillingEvaluationReasons.CurrencyUnreliable, "$", 0.50m },
        // 072 W review: confidence is checked BEFORE the not-USD test, so a foreign code read with doubt is "unclear", not "not USD".
        { BillingEvaluationReasons.CurrencyUnreliable, "EUR", 0.50m },
        { BillingEvaluationReasons.CurrencyUnreliable, "EUR", null },
    };

    [Theory]
    [MemberData(nameof(UnsupportedCurrencies))]
    public void AMoneyRule_IsNotJudged_WhenTheCurrencyIsNotUsd_IsMissing_OrIsBelowItsConfidenceFloor_D20(
        string reason, string? currency, decimal? confidence)
    {
        var rate = Version("CD-RATE", RuleType.ApprovedRate);
        var overRate = Fee(0) with { BilledRate = 700m, Amount = 700m }; // approved 575
        var request = Request(overRate) with
        {
            Currency = currency,
            HeaderConfidence = Confidence().With(BillingFieldNames.Currency, confidence),
        };

        var result = Evaluate(request, Loaded(rate));

        // No ApprovedRate finding and no adjustment; only D-115's flag-only "not checked" finding on the invoice.
        var notChecked = result.Findings.Should().ContainSingle().Subject;
        notChecked.ReasonCode.Should().Be(reason);
        notChecked.Subject.Should().Be(BillingSubject.ForInvoice());
        notChecked.SuggestedAdjustment.Should().BeNull();
        BillingRuleCatalogue.IsFlagOnlyReason(notChecked.ReasonCode).Should().BeTrue();
        result.Skips.Should().ContainSingle().Which.Should().Be(new BillingRuleSkip(
            rate.PolicyId, "CD-RATE", rate.PolicyVersionId, RuleType.ApprovedRate, reason));
    }

    [Theory]
    [InlineData("usd")]
    [InlineData("$")]
    [InlineData(" $ ")]
    [InlineData("US$")] // 072 W review: unambiguous
    [InlineData("us$")]
    [InlineData("USD$")]
    public void AMoneyRule_IsJudged_OnAReliablyReadUsdInvoice(string currency)
    {
        // 049 caller obligation: "$" is read as USD.
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }) with { Currency = currency };

        var result = Evaluate(request, Loaded(Version("CD-RATE", RuleType.ApprovedRate)));

        result.Findings.Should().ContainSingle().Which.SuggestedAdjustment.Should().NotBeNull();
        result.Skips.Should().BeEmpty();
    }

    [Fact]
    public void TheCurrencyConfidence_IsReadUnderExactlyTheKeyCurrency()
    {
        var misKeyed = new Dictionary<string, decimal>(Confidence().Where(kv => kv.Key != BillingFieldNames.Currency)) { ["currency"] = 0.99m };
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }) with { HeaderConfidence = misKeyed };

        var result = Evaluate(request, Loaded(Version("CD-RATE", RuleType.ApprovedRate)));

        result.Skips.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingEvaluationReasons.CurrencyUnreliable);
    }

    // ── D-115: rates and expense caps not checked ────────────────────────────────────────────────────────────────

    [Fact]
    public void RatesNotChecked_IsOneInvoiceLevelFinding_OnTheSkippedApprovedRatePolicy_CitingEverySkippedVersion_D115()
    {
        var rate = Version("CD-RATE", RuleType.ApprovedRate);
        var expense = Version("AA-EXPENSE", RuleType.ExpenseRule);     // ordinal first, but not ApprovedRate (072 part W)
        var increment = Increment("CD-INCREMENT", "0.1");             // not a money rule
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }, Expense(1) with { ExpenseCode = "E101", Amount = 600m }) with
        {
            Currency = "EUR",
        };

        var result = Evaluate(request, Loaded(rate, expense, increment));

        var notChecked = result.Findings.Should().ContainSingle("one finding, not one per skipped rule").Subject;
        notChecked.ReasonCode.Should().Be(BillingEvaluationReasons.CurrencyNotUsd);
        notChecked.PolicyCode.Should().Be("CD-RATE", "ApprovedRate is preferred: it is not run at all, so its group holds this reason alone");
        notChecked.PolicyVersionId.Should().Be(rate.PolicyVersionId);
        notChecked.RuleType.Should().Be(RuleType.ApprovedRate);
        notChecked.ClauseReference.Should().Be(rate.ClauseReference);
        notChecked.FactValues.Should().BeEmpty();
        notChecked.ValuesRead.Where(v => v.Name == "skippedPolicyVersion").Select(v => v.Value).Should().Equal(
            "AA-EXPENSE:" + expense.PolicyVersionId.ToString("D").ToLowerInvariant(),
            "CD-RATE:" + rate.PolicyVersionId.ToString("D").ToLowerInvariant());
        var currencyRead = notChecked.ValuesRead.Single(v => v.IsExtracted);
        currencyRead.Value.Should().Be("EUR");
        currencyRead.ExtractedField.Should().Be(new Sprk.Bff.Api.Services.Finance.Resolution.FieldRef(null, BillingFieldNames.Currency));
        currencyRead.Confidence.Should().Be(0.95m);
        result.RulesByVersion[notChecked.PolicyVersionId].Should().Be(rate, "the mapper and 073 find the version");
    }

    [Fact]
    public void RatesNotChecked_FallsBackToTheSkippedExpenseRule_OnlyWhenNoApprovedRateVersionIsInForce_D115()
    {
        var notYetInForce = Version("AA-RATE", RuleType.ApprovedRate, from: new DateOnly(2027, 1, 1));
        var expense = Version("ZZ-EXPENSE", RuleType.ExpenseRule);
        var request = Request(Expense(0) with { Amount = 600m }) with { Currency = "EUR" };

        var notChecked = Evaluate(request, Loaded(notYetInForce, expense)).Findings.Should().ContainSingle().Subject;

        notChecked.PolicyCode.Should().Be("ZZ-EXPENSE");
        notChecked.RuleType.Should().Be(RuleType.ExpenseRule);
    }

    [Theory]
    [InlineData("USD", true)]
    [InlineData("EUR", false)]
    public void Discount_AndAnAddedFeeAmountCap_AreMoney_SoNotJudgedOnANonUsdInvoice_ButAPercentCapIs_D20_Task115(string currency, bool judged)
    {
        var discount = Version("DISCOUNT", RuleType.Discount);
        var amountCap = Version("FEE-AMOUNT", RuleType.AddedFee,
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capAmount":100}""");
        var percentCap = Version("FEE-PERCENT", RuleType.AddedFee,
            """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E120"],"capPercent":5}""");
        // The fee line bills 700 against a rack rate of 675 less 10 percent (607.50); the E120 late fee of 150 is over 100 and over
        // 5 percent of the other lines (700 + 40 = 740, so 37.00).
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }, Expense(1), Expense(2) with { BilledExpenseCode = "E120", Amount = 150m })
            with { Currency = currency };

        var result = Evaluate(request, Loaded(discount, amountCap, percentCap));
        var byPolicy = result.Findings.ToLookup(f => f.PolicyCode);

        byPolicy["FEE-PERCENT"].Should().ContainSingle("a percent cap compares the invoice's own amounts, in any currency")
            .Which.ReasonCode.Should().Be(BillingReasonCodes.AddedFeeOverCap);
        byPolicy["DISCOUNT"].Any(f => f.ReasonCode == BillingReasonCodes.DiscountRateAboveDiscounted).Should().Be(judged);
        byPolicy["FEE-AMOUNT"].Any(f => f.ReasonCode == BillingReasonCodes.AddedFeeOverCap).Should().Be(judged);
        result.Skips.Select(s => s.PolicyCode).Should().BeEquivalentTo(judged ? [] : new[] { "DISCOUNT", "FEE-AMOUNT" });
        result.Findings.Count(f => f.ReasonCode == BillingEvaluationReasons.CurrencyNotUsd).Should().Be(judged ? 0 : 1, "D-115: one 'not checked' finding");
    }

    // ── 049 rule 5: the D-115 reason is never merged into a Reject (072 part W, A5 placement) ────────────────────

    /// <summary>The money rules a combination draws from, each with the strongest consequence its type may carry.</summary>
    private static readonly BillingRuleCandidate[] MoneyRules =
    [
        Version("CD-RATE", RuleType.ApprovedRate) with { Consequence = (int)BillingConsequence.Reject },
        Version("FIRM-RATE", RuleType.ApprovedRate, scope: RuleSetScope.Firm) with { Consequence = (int)BillingConsequence.Reject },
        Version("CD-EXPENSE", RuleType.ExpenseRule) with { Consequence = (int)BillingConsequence.Reject },
        Version("FIRM-EXPENSE", RuleType.ExpenseRule, scope: RuleSetScope.Firm) with { Consequence = (int)BillingConsequence.Reject },
        Version("AA-EXPENSE-CODES", RuleType.ExpenseRule, """{"type":"ExpenseRule","nonReimbursableCodes":["E109"]}""")
            with { Consequence = (int)BillingConsequence.Reject },
    ];

    public static TheoryData<int, string?, decimal?> MoneyRuleCombinations()
    {
        var data = new TheoryData<int, string?, decimal?>();
        for (var mask = 1; mask < 1 << MoneyRules.Length; mask++)
        {
            data.Add(mask, "EUR", 0.95m);
            data.Add(mask, null, null);
            data.Add(mask, "USD", 0.50m);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(MoneyRuleCombinations))]
    public void TheSignalCarryingTheCurrencyReason_IsNeverStrongerThanFlag_OverEveryCombinationOfMoneyRules_MustFire(
        int mask, string? currency, decimal? confidence)
    {
        // Every other rule that can reject at invoice level is in force too, and fires there: RequiredData (no billing period start)
        // and Duplicate (the firm number repeats a prior). Each line fails a reject-capable money reason in USD.
        var rules = MoneyRules.Where((_, i) => (mask & (1 << i)) != 0).ToList();
        var others = new[]
        {
            Version("CD-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["billingPeriodStart"]}""")
                with { Consequence = (int)BillingConsequence.Reject },
            Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""")
                with { Consequence = (int)BillingConsequence.Reject },
        };
        var request = Request(
            Fee(0) with { BilledRate = 700m, Amount = 700m },
            Expense(1) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m }) with
        {
            Currency = currency,
            BillingPeriodStart = null,
            HeaderConfidence = Confidence().With(BillingFieldNames.Currency, confidence),
        };
        var loaded = Loaded([.. rules, .. others]) with { PriorSubmissions = [Prior("INV-100.1", "INV-100", 1)] };
        var subjects = new BillingSignalSubjects(Id("invoice-row"), "INV-100", request.Lines.ToDictionary(l => l.Index, l => Id("line-row:" + l.Index)));

        var result = Evaluate(request, loaded);
        var mappings = BillingFindingSignalMapper.MapAll(result, subjects);

        var anyRate = rules.Any(r => r.RuleType == ((int)RuleType.ApprovedRate).ToString(System.Globalization.CultureInfo.InvariantCulture));
        var anyCap = rules.Any(r => r.RuleType == ((int)RuleType.ExpenseRule).ToString(System.Globalization.CultureInfo.InvariantCulture)
                                   && r.RuleBody!.Contains("maxAmountPerItem", StringComparison.Ordinal));
        var carrying = mappings.Where(m => m.Findings.Any(f => f.ReasonCode.StartsWith("billing.currency.", StringComparison.Ordinal))).ToList();
        if (!anyRate && !anyCap)
        {
            carrying.Should().BeEmpty("no money was skipped, so there is no currency reason");
            return;
        }

        var mapping = carrying.Should().ContainSingle("one D-115 finding per invoice").Subject;
        mapping.Consequence.Should().Be(BillingConsequence.Flag, "049 rule 5: the currency reason is never merged into a Reject");
        mapping.ReasonCodes.Should().OnlyContain(c => BillingRuleCatalogue.IsFlagOnlyReason(c), "its group holds no other reason");
        mapping.Request.SubjectEntityLogicalName.Should().Be("sprk_invoice");
        var host = result.RulesByVersion[mapping.Findings.Single().PolicyVersionId];
        host.RuleType.Should().Be(((int)(anyRate ? RuleType.ApprovedRate : RuleType.ExpenseRule)).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Control: the invoice-level Reject reasons of the other rules are in force and do reject, in Signals of their own.
        mappings.Should().Contain(m => m.Consequence == BillingConsequence.Reject && m.Request.SubjectEntityLogicalName == "sprk_invoice");
    }

    [Fact]
    public void RatesNotChecked_IsRaised_WithoutAnyRequiredDataRule_AndAlongsideRequiredDatasOwnCurrencyFinding_D115()
    {
        var rate = Version("CD-RATE", RuleType.ApprovedRate);
        var required = Version("CD-REQUIRED", RuleType.RequiredData);
        var request = Request(Fee(0)) with { Currency = "EUR" };

        Evaluate(request, Loaded(rate)).Findings.Select(f => f.ReasonCode).Should().Equal(BillingEvaluationReasons.CurrencyNotUsd);
        Evaluate(request, Loaded(rate, required)).Findings.Select(f => f.ReasonCode)
            .Should().BeEquivalentTo([BillingEvaluationReasons.CurrencyNotUsd, BillingReasonCodes.CurrencyNotUsd]);
    }

    // ── D-119: one currency normaliser for the money skip and RequiredData ───────────────────────────────────────

    public static TheoryData<string?, string?, bool> CurrencyPaths => new()
    {
        // currency read, the expected D-115 reason (null = rates checked), RequiredData's currency_not_usd fires
        { "$", null, false },
        { " $ ", null, false },
        { "usd", null, false },
        { "EUR", BillingEvaluationReasons.CurrencyNotUsd, true },
        { "US$", null, false },
        { "USD$", null, false },
        { "CA$", BillingEvaluationReasons.CurrencyNotUsd, true },
        { null, BillingEvaluationReasons.CurrencyMissing, false },
        { "  ", BillingEvaluationReasons.CurrencyMissing, false },
    };

    [Theory]
    [MemberData(nameof(CurrencyPaths))]
    public void TheMoneySkip_AndRequiredData_AgreeOnTheCurrency_D119(string? currency, string? notCheckedReason, bool notUsdFlag)
    {
        var rate = Version("CD-RATE", RuleType.ApprovedRate);
        var required = Version("CD-REQUIRED", RuleType.RequiredData);
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }) with { Currency = currency };

        var codes = Evaluate(request, Loaded(rate, required)).Findings.Select(f => f.ReasonCode).ToList();

        var currencyReasons = new[]
        {
            BillingEvaluationReasons.CurrencyMissing, BillingEvaluationReasons.CurrencyUnreliable, BillingEvaluationReasons.CurrencyNotUsd,
        };
        codes.Where(currencyReasons.Contains).Should().Equal(notCheckedReason is null ? [] : [notCheckedReason]);
        codes.Contains(BillingReasonCodes.CurrencyNotUsd).Should().Be(notUsdFlag, "RequiredData uses the same normaliser");
        codes.Contains(BillingReasonCodes.RateAboveApproved).Should().Be(notCheckedReason is null, "rates are checked exactly when USD");
    }

    [Theory]
    [InlineData(0.50, BillingEvaluationReasons.CurrencyUnreliable, false)]
    [InlineData(null, BillingEvaluationReasons.CurrencyUnreliable, false)]
    [InlineData(0.95, BillingEvaluationReasons.CurrencyNotUsd, true)]
    public void ALowConfidenceEur_GetsOnlyD115sUnclear_AReliableEurGetsBothNotUsdFlags_D123(
        double? confidence, string notChecked, bool requiredDataFlag)
    {
        var rate = Version("CD-RATE", RuleType.ApprovedRate);
        var required = Version("CD-REQUIRED", RuleType.RequiredData);
        var request = Request(Fee(0)) with
        {
            Currency = "EUR",
            HeaderConfidence = Confidence().With(BillingFieldNames.Currency, (decimal?)confidence),
        };

        var codes = Evaluate(request, Loaded(rate, required)).Findings.Select(f => f.ReasonCode).ToList();

        codes.Should().Contain(notChecked);
        codes.Contains(BillingReasonCodes.CurrencyNotUsd).Should().Be(requiredDataFlag, "RequiredData uses the evaluator's floor");
    }

    [Fact]
    public void RatesNotChecked_IsNotRaised_WhenNoMoneyRuleIsInForce_D115()
    {
        var request = Request(Line(0, EarlyLine)) with { Currency = "EUR" };
        var notYetInForce = Version("CD-RATE", RuleType.ApprovedRate, from: new DateOnly(2027, 1, 1));
        var codesOnly = Version("CD-EXPENSE", RuleType.ExpenseRule, """{"type":"ExpenseRule","nonReimbursableCodes":["E109"]}""");

        Evaluate(request, Loaded(Increment("CD-INCREMENT", "0.1"))).Findings.Should().BeEmpty("no money rule at all");
        Evaluate(request, Loaded(notYetInForce)).Findings.Should().BeEmpty("the money rule is not in force on the invoice's dates");
        Evaluate(request, Loaded(codesOnly)).Findings.Should().BeEmpty("an ExpenseRule with no cap compares no money");
        Evaluate(request, Loaded(notYetInForce, codesOnly)).Skips.Should().BeEmpty();
    }

    [Fact]
    public void AnExpenseRule_KeepsItsCodeCheck_ButDropsItsAmountCap_OnANonUsdInvoice_D20()
    {
        var rule = Version("CD-EXPENSE", RuleType.ExpenseRule);
        var expense = Expense(0) with { ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m }; // D-117: the billed code is judged

        var usd = Evaluate(Request(expense), Loaded(rule));
        var eur = Evaluate(Request(expense) with { Currency = "EUR" }, Loaded(rule));

        usd.Findings.Select(f => f.ReasonCode).Should().BeEquivalentTo(
            [BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap], "control: both checks fire in USD");
        eur.Findings.Select(f => f.ReasonCode).Should().Equal(BillingEvaluationReasons.CurrencyNotUsd, BillingReasonCodes.ExpenseCodeNotReimbursable);
        eur.Skips.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingEvaluationReasons.CurrencyNotUsd);
    }

    // ── Priors ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSubmissionBeingJudged_IsNeverOneOfItsOwnPriors_K1()
    {
        var self = Prior("INV-100.2", "INV-100", 2) with { SubmissionId = SubmissionId };
        var loaded = Loaded(Version("CD-DUPLICATE", RuleType.Duplicate, """{"type":"Duplicate","matchFields":["firmInvoiceNumber"]}""")) with
        {
            PriorSubmissions = [self],
        };

        var result = Evaluate(Request(Line(0, EarlyLine)), loaded);

        result.Findings.Should().BeEmpty();
        result.Facts.PriorSubmissions.Should().BeEmpty();
    }

    // ── Citation, order, determinism, timing ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryFinding_CitesItsPolicyVersionAndClause_AndItsVersionIsReturned()
    {
        var (request, loaded) = EverythingWrong(20);

        var result = Evaluate(request, loaded);

        result.Findings.Should().NotBeEmpty();
        foreach (var finding in result.Findings)
        {
            var version = result.RulesByVersion[finding.PolicyVersionId];
            loaded.Rules.Should().Contain(version);
            finding.PolicyId.Should().Be(version.PolicyId);
            finding.PolicyCode.Should().Be(version.PolicyCode);
            finding.ClauseReference.Should().Be(version.ClauseReference).And.NotBeNullOrWhiteSpace();
            finding.ClauseLocation.Should().Be(version.ClauseLocation);
        }
    }

    [Fact]
    public void EqualInputs_GiveEqualFindings_InEqualOrder_WhateverTheOrderTheRulesWereRead()
    {
        var (request, loaded) = EverythingWrong(30);
        var reversed = loaded with { Rules = loaded.Rules.Reverse().ToList() };

        var first = Evaluate(request, loaded);
        var second = Evaluate(request, reversed);

        second.Findings.Should().BeEquivalentTo(first.Findings, o => o.WithStrictOrdering());
        first.Findings.Select(f => f.Subject.Kind == BillingSubjectKind.Invoice ? -1 : f.Subject.LineIndex!.Value)
            .Should().BeInAscendingOrder("the invoice first, then by line index");
        first.Findings.Should().Contain(f => f.Subject.Kind == BillingSubjectKind.Invoice, "the fixture has an invoice-level finding");
        foreach (var subject in first.Findings.GroupBy(f => f.Subject))
        {
            subject.Select(f => BillingRuleCatalogue.CataloguePosition(f.RuleType)).Should().BeInAscendingOrder();
        }
    }

    [Fact]
    public void A150LineInvoice_WithAllElevenTypes_IsEvaluatedInUnderFiveSeconds()
    {
        var (request, loaded) = EverythingWrong(150);
        var evaluator = Evaluator();
        evaluator.Evaluate(request, loaded); // warm-up: JIT and the schema cache are not the evaluation

        var clock = Stopwatch.StartNew();
        var result = evaluator.Evaluate(request, loaded);
        clock.Stop();

        _output.WriteLine($"150 lines, 11 rule types: {result.Findings.Count} findings in {clock.Elapsed.TotalMilliseconds:F1} ms");
        result.Findings.Select(f => f.RuleType).Distinct().Should().BeEquivalentTo(Bodies.Keys, "every type fires on this fixture");
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
