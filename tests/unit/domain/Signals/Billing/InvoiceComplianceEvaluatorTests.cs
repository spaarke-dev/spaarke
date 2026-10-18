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
    public void ALineWithNoDate_TakesTheInvoiceDate_AndWithNoDateAtAll_TheCurrentVersion()
    {
        var supersededOn = new DateOnly(2026, 3, 10);
        var v1 = Increment("CD-INCREMENT", "0.1", from: RulesFrom, to: supersededOn, current: false, number: 1);
        var v2 = Increment("CD-INCREMENT", "0.5", from: supersededOn, current: true, number: 2);

        var onInvoiceDate = Evaluate(Request(Line(0, null)) with { InvoiceDate = EarlyLine }, Loaded(v1, v2));
        var undated = Evaluate(Request(Line(0, null)) with { InvoiceDate = null }, Loaded(v1, v2));

        onInvoiceDate.Findings.Should().BeEmpty("the undated line is judged on the invoice date, when v1 (0.1) was in force");
        undated.Findings.Should().ContainSingle().Which.PolicyVersionId.Should().Be(v2.PolicyVersionId);
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
        { BillingEvaluationReasons.CurrencyLowConfidence, "USD", 0.50m },
        { BillingEvaluationReasons.CurrencyLowConfidence, "USD", null },
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

        result.Findings.Should().BeEmpty("no ApprovedRate finding and no adjustment when the currency is not reliably USD");
        result.Skips.Should().ContainSingle().Which.Should().Be(new BillingRuleSkip(
            rate.PolicyId, "CD-RATE", rate.PolicyVersionId, RuleType.ApprovedRate, reason));
    }

    [Fact]
    public void AMoneyRule_IsJudged_OnAReliablyReadUsdInvoice()
    {
        var request = Request(Fee(0) with { BilledRate = 700m, Amount = 700m }) with { Currency = "usd" };

        var result = Evaluate(request, Loaded(Version("CD-RATE", RuleType.ApprovedRate)));

        result.Findings.Should().ContainSingle().Which.SuggestedAdjustment.Should().NotBeNull();
        result.Skips.Should().BeEmpty();
    }

    [Fact]
    public void AnExpenseRule_KeepsItsCodeCheck_ButDropsItsAmountCap_OnANonUsdInvoice_D20()
    {
        var rule = Version("CD-EXPENSE", RuleType.ExpenseRule);
        var expense = Expense(0) with { ExpenseCode = "E109", Amount = 600m };

        var usd = Evaluate(Request(expense), Loaded(rule));
        var eur = Evaluate(Request(expense) with { Currency = "EUR" }, Loaded(rule));

        usd.Findings.Select(f => f.ReasonCode).Should().BeEquivalentTo(
            [BillingReasonCodes.ExpenseCodeNotReimbursable, BillingReasonCodes.ExpenseAmountOverCap], "control: both checks fire in USD");
        eur.Findings.Select(f => f.ReasonCode).Should().Equal(BillingReasonCodes.ExpenseCodeNotReimbursable);
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
