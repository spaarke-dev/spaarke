using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 082c-1 (brief C2, C4-C9; ontology D-205): <see cref="InvoiceComplianceEvaluator"/> over the two streams, legacy rule sets and the
/// Procedure resolution. Precedence reads the rank only (C5) and the type's <see cref="BillingRuleCatalogue.PrecedenceOf"/> (C6), for legacy and
/// Procedure rules alike; coverage of a refusal follows the rank (C8); each subject date is resolved once (C9); the gaps are exposed.
/// </summary>
public class InvoiceComplianceEvaluatorProcedureTests
{
    private static readonly DateOnly EarlyLine = new(2026, 3, 2);
    private static readonly DateOnly LateLine = new(2026, 3, 20);

    /// <summary>A fee line of 0.3 hours (off a 0.5 increment, on a 0.1 one) with activity code A101 and no billed task code.</summary>
    private static BillingLineFacts Line(int index, DateOnly? date = null) =>
        Fee(index) with { Date = date ?? EarlyLine, Hours = 0.3m, Amount = 172.5m, BilledTaskCode = null };

    private const string HalfHour = """{"type":"TimeIncrement","incrementHours":0.5}""";
    private const string TenthHour = """{"type":"TimeIncrement","incrementHours":0.1}""";
    private const string ListsA101 = """{"type":"NonBillableActivity","codes":["A101"]}""";
    private const string ListsA102 = """{"type":"NonBillableActivity","codes":["A102"]}""";
    private const string CapOnly = """{"type":"ExpenseRule","maxAmountPerItem":500}""";

    private static BillingLineFacts BadExpense(int index) =>
        Expense(index) with { Date = EarlyLine, ExpenseCode = "E109", BilledExpenseCode = "E109", Amount = 600m };

    private static Func<BillingRuleDefinition, BillingRuleOrigin, BillingFactSet, IReadOnlyList<BillingFinding>> RefusingHandlerFor(string policyCode) =>
        (rule, origin, facts) => rule.PolicyCode == policyCode
            ? throw new BillingRuleRefusedException(BillingRuleRefusedException.ParameterMissing, rule.RuleType, "test")
            : BillingRuleHandlers.Evaluate(rule, origin, facts);

    private static IEnumerable<string> Codes(BillingEvaluationResult result) =>
        result.Findings.Select(f => f.PolicyCode).Distinct().Order(StringComparer.Ordinal);

    // ── C2: a Procedure rule is judged ──────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C2 must-fire: a rule of an Active Procedure Version raises a finding; with no Procedure stream it does not")]
    public void ARuleOfAnActiveProcedureVersion_RaisesAFinding_C2()
    {
        var kit = new ProcedureKit().Procedure("Guidelines");
        var rule = kit.Rule("Guidelines", "PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded());
        var control = Evaluator().Evaluate(Request(Line(0)), Loaded());

        var finding = result.Findings.Should().ContainSingle().Subject;
        finding.PolicyVersionId.Should().Be(rule.PolicyVersionId);
        var cited = result.RulesByVersion[rule.PolicyVersionId];
        cited.Source.Should().Be(BillingRuleSource.Procedure);
        cited.Scope.Should().BeNull();
        cited.ProcedureId.Should().Be(ProcedureKit.ProcedureId("Guidelines"));
        cited.ProcedureVersionId.Should().Be(ProcedureKit.ProcedureVersionId("Guidelines"));
        cited.CanBeInForce.Should().BeFalse("C9: the resolver chose it for the date; the legacy in-force test is never asked of it");
        control.Findings.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c C2: BillingLoadedFacts.Rules is the legacy stream only; a Procedure candidate there is a caller bug")]
    public void AProcedureCandidateInTheLegacyStream_Throws()
    {
        var kit = new ProcedureKit().Procedure("Guidelines");
        kit.Rule("Guidelines", "PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        var stray = BillingFactLoader.ProcedureCandidate(kit.Build().RulesOn(EarlyLine).Rules.Single());

        var evaluate = () => Evaluator().Evaluate(Request(Line(0)), Loaded(stray));

        evaluate.Should().Throw<InvalidOperationException>().WithMessage("*Procedure rules come from BillingLoadedFacts.Procedures*");
    }

    // ── C4: disjoint streams ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C4: the same policy version in both streams throws a resolution fault")]
    public void TheSamePolicyVersionInBothStreams_Faults_C4()
    {
        var kit = new ProcedureKit().Procedure("Guidelines");
        var rule = kit.Rule("Guidelines", "PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        var legacy = Version("LEGACY-INCREMENT", RuleType.TimeIncrement, TenthHour) with { PolicyVersionId = rule.PolicyVersionId };

        var evaluate = () => Evaluator().Evaluate(Request(Line(0)), kit.Loaded(legacy));

        evaluate.Should().Throw<ProcedureResolutionFaultException>().WithMessage("*both a legacy rule and a Procedure rule*");
    }

    // ── C5: the rank map ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C5: legacy ClientDefault (AllRecords, 0, false), legacy Firm (Firm, 0, true); a Procedure rule takes its AppliedProcedure's rank, a record rule its base rule's")]
    public void TheRankMap_C5()
    {
        BillingFactLoader.LegacyRank(RuleSetScope.ClientDefault).Should().Be(new ProcedureRank(ProcedureLevel.AllRecords, 0, false));
        BillingFactLoader.LegacyRank(RuleSetScope.Firm).Should().Be(new ProcedureRank(ProcedureLevel.Firm, 0, true));

        var kit = new ProcedureKit()
            .Procedure("All")
            .Procedure("FirmWide", ProcedureAppliesTo.Firm, firm: FirmId)
            .Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        kit.Rule("All", "ALL-1", RuleType.TimeIncrement, HalfHour);
        kit.Rule("FirmWide", "FIRM-1", RuleType.DailyHoursCap);
        kit.Rule("Assigned", "MANUAL-1", RuleType.InvoiceAge);
        var onDate = kit.Build().RulesOn(EarlyLine);

        var ranks = onDate.Rules.ToDictionary(r => r.Version.PolicyCode, r => BillingFactLoader.ProcedureCandidate(r).Rank);
        ranks["ALL-1"].Should().Be(new ProcedureRank(ProcedureLevel.AllRecords, 0, false));
        ranks["FIRM-1"].Should().Be(new ProcedureRank(ProcedureLevel.Firm, 0, true));
        ranks["MANUAL-1"].Should().Be(new ProcedureRank(ProcedureLevel.ManualOwn, 0, false));

        // A record rule sits in its base rule's slot: its Procedure is the base's, so its rank is the base's.
        var baseRule = onDate.Rules.Single(r => r.Version.PolicyCode == "MANUAL-1");
        var recordVersion = baseRule.Version with { PolicyId = Id("record-rule"), PolicyCode = "RECORD-1", PolicyVersionId = Id("record-rule:v1"), ProcedureVersionId = null };
        var recordRule = new ResolvedRule(recordVersion, baseRule.Procedure, new RecordRuleSlot(
            baseRule.Version.PolicyId, "MANUAL-1", baseRule.Version.PolicyVersionId, ProcedureKit.Matter, null));
        BillingFactLoader.ProcedureCandidate(recordRule).Rank.Should().Be(baseRule.Procedure.Rank);
    }

    [Fact(DisplayName = "082c C5: precedence reads Rank only, never Scope (a client-default row carrying the higher rank wins)")]
    public void Precedence_ReadsTheRankNotTheScope_C5()
    {
        // Scope and rank deliberately disagree: the "firm" row carries the client default's rank and the "client default" row the firm's.
        var labelledFirm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, HalfHour, RuleSetScope.Firm) with { Rank = BillingFactLoader.LegacyRank(RuleSetScope.ClientDefault) };
        var labelledDefault = Version("CD-INCREMENT", RuleType.TimeIncrement, TenthHour) with { Rank = BillingFactLoader.LegacyRank(RuleSetScope.Firm) };

        var result = Evaluator().Evaluate(Request(Line(0)), Loaded(labelledFirm, labelledDefault));

        result.Findings.Should().BeEmpty("the higher-ranked 0.1 increment holds and displaces the 0.5 one, whatever their scopes say");
    }

    // ── C6: Replace ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C6 Replace: a higher-ranked Procedure limit displaces the legacy firm's; a legacy rule displaces a lower Procedure one")]
    public void Replace_TheHigherRankDisplacesTheLower_AcrossStreams_C6()
    {
        var manual = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        manual.Rule("Assigned", "MANUAL-INCREMENT", RuleType.TimeIncrement, TenthHour);
        var general = new ProcedureKit().Procedure("All");
        var lowerProcedure = general.Rule("All", "ALL-INCREMENT", RuleType.TimeIncrement, HalfHour);
        var legacyFirm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, HalfHour, RuleSetScope.Firm);
        var legacyFirmHolds = Version("FIRM-INCREMENT", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm);

        var procedureOverLegacy = Evaluator().Evaluate(Request(Line(0)), manual.Loaded(legacyFirm));
        var legacyOverProcedure = Evaluator().Evaluate(Request(Line(0)), general.Loaded(legacyFirmHolds));

        procedureOverLegacy.Findings.Should().BeEmpty("the assigned Procedure's 0.1 increment (ManualOwn) displaces the legacy firm's 0.5");
        legacyOverProcedure.Findings.Should().BeEmpty("the legacy firm's 0.1 increment (Firm) displaces the all-records Procedure's 0.5");
        legacyOverProcedure.RulesByVersion.Should().NotContainKey(lowerProcedure.PolicyVersionId);
    }

    [Fact(DisplayName = "082c C6 Replace tie: a Procedure rule at the legacy rule's rank applies beside it (ties all apply)")]
    public void Replace_ATie_BothApply_C6()
    {
        var kit = new ProcedureKit().Procedure("FirmWide", ProcedureAppliesTo.Firm, firm: FirmId);
        kit.Rule("FirmWide", "PROC-INCREMENT", RuleType.TimeIncrement, HalfHour);
        var legacyFirm = Version("FIRM-INCREMENT", RuleType.TimeIncrement, HalfHour, RuleSetScope.Firm);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded(legacyFirm));

        Codes(result).Should().Equal("FIRM-INCREMENT", "PROC-INCREMENT");
    }

    [Fact(DisplayName = "082c C6 Replace: a handler-refused higher rank displaces nothing; the lower one judges")]
    public void Replace_ARefusedHigherRank_KeepsTheLower_C6()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        var higher = kit.Rule("Assigned", "MANUAL-INCREMENT", RuleType.TimeIncrement, TenthHour);
        var legacyDefault = Version("CD-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator(handlers: RefusingHandlerFor("MANUAL-INCREMENT")).Evaluate(Request(Line(0)), kit.Loaded(legacyDefault));

        Codes(result).Should().Equal("CD-INCREMENT");
        result.Refusals.Should().ContainSingle().Which.PolicyVersionId.Should().Be(higher.PolicyVersionId);
    }

    [Fact(DisplayName = "082c C6 Replace: a downgraded higher rank (a consequence its type may not carry) displaces nothing; it and the lower one both judge")]
    public void Replace_ADowngradedHigherRank_KeepsTheLower_C6()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        var higher = kit.Rule("Assigned", "MANUAL-INCREMENT", RuleType.TimeIncrement, HalfHour, consequence: (int)BillingConsequence.Reject);
        var legacyDefault = Version("CD-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded(legacyDefault));

        Codes(result).Should().Equal("CD-INCREMENT", "MANUAL-INCREMENT");
        result.RulesByVersion[higher.PolicyVersionId].Consequence.Should().Be((int)BillingConsequence.Flag);
    }

    // ── C6: Additive and Split ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C6 Additive (D-205 item 2): legacy firm and client-default checklists both apply (was: the firm's replaced the default's)")]
    public void Additive_LegacyFirmAndClientDefault_BothApply_C6()
    {
        var clientDefault = Version("CD-NONBILLABLE", RuleType.NonBillableActivity, ListsA101);
        var firm = Version("FIRM-NONBILLABLE", RuleType.NonBillableActivity, ListsA102, RuleSetScope.Firm);
        var defaultRequired = Version("CD-REQUIRED", RuleType.RequiredData);
        var firmRequired = Version("FIRM-REQUIRED", RuleType.RequiredData, """{"type":"RequiredData","fields":["activityCode"]}""", RuleSetScope.Firm);

        var result = Evaluator().Evaluate(Request(Line(0)), Loaded(clientDefault, firm, defaultRequired, firmRequired));

        Codes(result).Should().Equal("CD-NONBILLABLE", "CD-REQUIRED");
        result.RulesByVersion.Keys.Should().NotContain([firm.PolicyVersionId, firmRequired.PolicyVersionId], "their items hold on this line");
    }

    [Fact(DisplayName = "082c C6 Additive: a Procedure checklist and a higher-ranked one both apply")]
    public void Additive_ProcedureChecklists_AllApply_C6()
    {
        var kit = new ProcedureKit().Procedure("All").Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        kit.Rule("All", "ALL-NONBILLABLE", RuleType.NonBillableActivity, ListsA101);
        kit.Rule("Assigned", "MANUAL-NONBILLABLE", RuleType.NonBillableActivity, ListsA101);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded(Version("FIRM-NONBILLABLE", RuleType.NonBillableActivity, ListsA101, RuleSetScope.Firm)));

        Codes(result).Should().Equal("ALL-NONBILLABLE", "FIRM-NONBILLABLE", "MANUAL-NONBILLABLE");
    }

    [Fact(DisplayName = "082c C6 Split: a higher-ranked Procedure cap displaces the legacy cap; the legacy code check adds")]
    public void Split_AHigherProcedureCap_DisplacesTheLegacyCap_TheCodesAdd_C6()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        kit.Rule("Assigned", "MANUAL-EXPENSE", RuleType.ExpenseRule, """{"type":"ExpenseRule","maxAmountPerItem":1000}""");
        var legacyDefault = Version("CD-EXPENSE", RuleType.ExpenseRule); // E109 non-reimbursable, cap 500

        var result = Evaluator().Evaluate(Request(BadExpense(0)), kit.Loaded(legacyDefault));

        result.Findings.Select(f => (f.PolicyCode, f.ReasonCode)).Should().Equal(("CD-EXPENSE", BillingReasonCodes.ExpenseCodeNotReimbursable));
    }

    [Fact(DisplayName = "082c C6 Split control: with no higher-ranked version the legacy cap still fires")]
    public void Split_Alone_TheCapFires_Control()
    {
        var result = Evaluator().Evaluate(Request(BadExpense(0)), new ProcedureKit().Loaded(Version("CD-EXPENSE", RuleType.ExpenseRule, CapOnly)));

        result.Findings.Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.ExpenseAmountOverCap);
    }

    // ── C7 ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C7 (K3): a stored non-billing rule type is a GateRefused refusal, never an exception from PrecedenceOf; the other rules run")]
    public void ANonBillingType_IsGateRefused_NotThrown_C7()
    {
        var kit = new ProcedureKit().Procedure("All");
        var threshold = kit.Rule("All", "ALL-THRESHOLD", RuleType.Threshold, """{"type":"Threshold"}""");
        var legacySwitch = Version("CD-SWITCH", RuleType.Switch, """{"type":"Switch"}""");
        var increment = Version("CD-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded(legacySwitch, increment));

        result.Refusals.Select(r => (r.PolicyVersionId, r.ReasonCode)).Should().BeEquivalentTo(new[]
        {
            (threshold.PolicyVersionId, BillingEvaluationReasons.GateRefused),
            (legacySwitch.PolicyVersionId, BillingEvaluationReasons.GateRefused),
        });
        Codes(result).Should().Equal("CD-INCREMENT");
    }

    // ── C8: coverage ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C8: a refused Additive version stays unchecked although a version of its key judged the line")]
    public void ARefusedAdditiveVersion_StaysUnchecked_C8()
    {
        var kit = new ProcedureKit().Procedure("All");
        var refused = kit.Rule("All", "ALL-NONBILLABLE", RuleType.NonBillableActivity, ListsA102);
        var firm = Version("FIRM-NONBILLABLE", RuleType.NonBillableActivity, ListsA101, RuleSetScope.Firm);

        var result = Evaluator(handlers: RefusingHandlerFor("ALL-NONBILLABLE")).Evaluate(Request(Line(0)), kit.Loaded(firm));

        Codes(result).Should().Equal("FIRM-NONBILLABLE");
        var scope = result.RefusalScopes.Should().ContainSingle().Which;
        scope.PolicyVersionId.Should().Be(refused.PolicyVersionId);
        scope.LineIndexes.Should().Equal(0);
    }

    [Fact(DisplayName = "082c C8: a refused Split (ExpenseRule) version stays unchecked although a higher-ranked one judged the line")]
    public void ARefusedSplitVersion_StaysUnchecked_C8()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        kit.Rule("Assigned", "MANUAL-EXPENSE", RuleType.ExpenseRule, CapOnly);
        var legacyDefault = Version("CD-EXPENSE", RuleType.ExpenseRule);

        var result = Evaluator(handlers: RefusingHandlerFor("CD-EXPENSE")).Evaluate(Request(BadExpense(0)), kit.Loaded(legacyDefault));

        var scope = result.RefusalScopes.Should().ContainSingle().Which;
        scope.PolicyVersionId.Should().Be(legacyDefault.PolicyVersionId);
        scope.LineIndexes.Should().Equal(new[] { 0 }, "its code check was not made anywhere");
    }

    [Fact(DisplayName = "082c C8 (D-205 tie): a refused Replace version is NOT covered by a tie-mate that judged the line; its lines are rules.not_checked")]
    public void ARefusedReplaceVersion_IsNotCoveredByATieMate_C8()
    {
        // Two Firm-level TimeIncrement versions tie (ties all apply): 0.1 holds on the 0.3 line, the 0.5 one is refused. Its 0.5 limit was not checked.
        var tenth = Version("FIRM-INCREMENT-A", RuleType.TimeIncrement, TenthHour, RuleSetScope.Firm);
        var half = Version("FIRM-INCREMENT-B", RuleType.TimeIncrement, HalfHour, RuleSetScope.Firm);

        var result = Evaluator(handlers: RefusingHandlerFor("FIRM-INCREMENT-B")).Evaluate(Request(Line(0)), new ProcedureKit().Loaded(tenth, half));

        var scope = result.RefusalScopes.Should().ContainSingle().Which;
        scope.PolicyVersionId.Should().Be(half.PolicyVersionId);
        scope.LineIndexes.Should().Equal(new[] { 0 }, "a tie does not cover the refused version's limit");
        Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister.RulesNotChecked(result, 1).Should().ContainKey(0);
    }

    [Fact(DisplayName = "082c C8: a refused lower-ranked Replace version is covered where a higher-ranked Procedure version of its key judged the line")]
    public void ARefusedLowerReplaceVersion_IsCoveredByAHigherProcedureVersion_C8()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: true);
        kit.Rule("Assigned", "MANUAL-INCREMENT", RuleType.TimeIncrement, TenthHour);
        var legacyDefault = Version("CD-INCREMENT", RuleType.TimeIncrement, """{"type":"TimeIncrement","incrementHours":"x"}""");

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded(legacyDefault));

        var scope = result.RefusalScopes.Should().ContainSingle().Which;
        scope.PolicyVersionId.Should().Be(legacyDefault.PolicyVersionId);
        scope.AppliesToInvoice.Should().BeFalse();
        scope.LineIndexes.Should().BeEmpty();
    }

    // ── C9: dates, and the gaps ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c C9: RulesOn runs once per distinct subject date (an undated line takes the invoice date), whatever the line count")]
    public void RulesOn_RunsOncePerDistinctSubjectDate_C9()
    {
        var kit = new ProcedureKit().Procedure("All");
        kit.Rule("All", "ALL-INCREMENT", RuleType.TimeIncrement, HalfHour);
        var procedures = kit.Build();
        var lines = Enumerable.Range(0, 150).Select(i => Line(i) with { Date = i % 5 == 0 ? null : i % 3 == 0 ? EarlyLine : LateLine }).ToArray();

        var result = Evaluator().Evaluate(Request(lines), Loaded() with { Procedures = procedures });

        procedures.DatesResolved.Should().Be(3, "the invoice date (also every undated line's), the early date and the late date");
        result.Findings.Should().HaveCount(150, "every line is off the 0.5 increment, undated ones on the invoice date too");
    }

    [Fact(DisplayName = "082c: a gap on the invoice date is exposed with the undated line, not the dated ones; the invoice subject falls back to the dated lines' version (K4 rule (c))")]
    public void AGapOnTheInvoiceDateOnly_IsExposedWithTheSubjectsOnThatDate()
    {
        // The Procedure's only version ends on 10 March: the lines of 2 March are judged, the invoice date (31 March) is after its last version.
        // K4 rule (c): the invoice subject is judged by the version in force on the latest dated line (2 March), so only the undated line,
        // which takes the invoice date, holds the gap.
        var kit = new ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator().Evaluate(Request(Line(0), Line(1) with { Date = null }, Line(2)), kit.Loaded());

        var gap = result.Gaps.Should().ContainSingle().Subject;
        gap.Gap.ProcedureName.Should().Be("Ended");
        gap.Gap.WorkDate.Should().Be(InvoiceDate);
        gap.Gap.Kind.Should().Be(ProcedureGapKind.AfterLastVersion);
        gap.AppliesToInvoice.Should().BeFalse("K4 rule (c): the invoice subject takes the version in force on the latest dated line");
        gap.LineIndexes.Should().Equal(1);
        result.Findings.Select(f => f.Subject.LineIndex).Should().Equal([0, 2], "the dated lines were judged by the version in force on their date");
    }

    // ── K4, ontology D-205 rule (c): the invoice subject falls back, per Procedure, to the latest dated line's version ─────────────────

    private const string NeedsPeriodStart = """{"type":"RequiredData","fields":["billingPeriodStart"]}""";

    /// <summary>An invoice with no billing period start: any RequiredData rule that lists it raises an INVOICE-subject finding.</summary>
    private static BillingEvaluationRequest InvoiceMissingPeriod(params BillingLineFacts[] lines) => Request(lines) with { BillingPeriodStart = null };

    private static IEnumerable<string> InvoiceLevelCodes(BillingEvaluationResult result) =>
        result.Findings.Where(f => f.Subject.Kind == BillingSubjectKind.Invoice).Select(f => f.PolicyCode);

    [Fact(DisplayName = "082c K4 rule (c) must-fire: an invoice dated after the last version's end, every line inside the window, has no gap; the window's version judges the invoice")]
    public void AnInvoiceAfterTheLastVersion_WithEveryLineInsideTheWindow_FallsBackToTheLatestLinesVersion_K4()
    {
        var kit = new ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);

        var result = Evaluator().Evaluate(InvoiceMissingPeriod(Line(0)), kit.Loaded());

        result.Gaps.Should().BeEmpty("the latest line's work date has a version, so the invoice-date gap does not stand");
        InvoiceLevelCodes(result).Should().Equal("ENDED-REQUIRED");
    }

    [Fact(DisplayName = "082c K4 rule (c): with one line dated after the end, that line's gap holds and so does the invoice-date gap")]
    public void ALineAfterTheEnd_KeepsItsGap_AndTheInvoiceDateGapStands_K4()
    {
        var kit = new ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);

        var result = Evaluator().Evaluate(InvoiceMissingPeriod(Line(0), Line(1, LateLine)), kit.Loaded());

        result.Gaps.Select(g => (g.Gap.WorkDate, g.AppliesToInvoice, string.Join(",", g.LineIndexes))).Should().Equal(
            (LateLine, false, "1"), (InvoiceDate, true, string.Empty));
        InvoiceLevelCodes(result).Should().BeEmpty("the latest line's date has no version either: nothing judges the invoice subject");
    }

    [Fact(DisplayName = "082c K4 rule (c): an invoice date in a hole between versions is judged by the version in force on the latest dated line")]
    public void AnInvoiceDateInAHole_IsJudgedByTheLatestLinesVersion_K4()
    {
        var kit = new ProcedureKit().Procedure("Holey", to: new DateOnly(2026, 3, 10)).NextVersion("Holey", 2, new DateOnly(2026, 4, 10));
        kit.Rule("Holey", "V1-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);
        kit.Rule("Holey", "V2-REQUIRED", RuleType.RequiredData, NeedsPeriodStart, versionNumber: 2);

        var result = Evaluator().Evaluate(InvoiceMissingPeriod(Line(0)), kit.Loaded());

        result.Gaps.Should().BeEmpty();
        InvoiceLevelCodes(result).Should().Equal("V1-REQUIRED");
    }

    [Fact(DisplayName = "082c K4 rule (c): no dated line and an invoice date after the end leaves the gap standing")]
    public void NoDatedLines_AnInvoiceDateAfterTheEnd_KeepsTheGap_K4()
    {
        var kit = new ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);

        var result = Evaluator().Evaluate(InvoiceMissingPeriod(Line(0) with { Date = null }), kit.Loaded());

        var gap = result.Gaps.Should().ContainSingle().Subject;
        gap.Gap.Kind.Should().Be(ProcedureGapKind.AfterLastVersion);
        gap.AppliesToInvoice.Should().BeTrue();
        gap.LineIndexes.Should().Equal(0);
    }

    [Fact(DisplayName = "082c K4 rule (c) control: a version in force on the invoice date wins over the line's date")]
    public void AVersionInForceOnTheInvoiceDate_WinsOverTheLineDate_K4()
    {
        var kit = new ProcedureKit().Procedure("Two", to: new DateOnly(2026, 3, 10)).NextVersion("Two", 2, new DateOnly(2026, 3, 11));
        kit.Rule("Two", "V1-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);
        kit.Rule("Two", "V2-REQUIRED", RuleType.RequiredData, NeedsPeriodStart, versionNumber: 2);

        var result = Evaluator().Evaluate(InvoiceMissingPeriod(Line(0)), kit.Loaded());

        result.Gaps.Should().BeEmpty();
        InvoiceLevelCodes(result).Should().Equal("V2-REQUIRED");
    }

    [Fact(DisplayName = "082c K4 rule (c), C9: the fallback adds no resolution (one RulesOn per distinct subject date)")]
    public void TheInvoiceFallback_AddsNoResolution_K4()
    {
        var kit = new ProcedureKit().Procedure("Ended", to: new DateOnly(2026, 3, 10));
        kit.Rule("Ended", "ENDED-REQUIRED", RuleType.RequiredData, NeedsPeriodStart);
        var procedures = kit.Build();

        Evaluator().Evaluate(InvoiceMissingPeriod(Line(0), Line(1)), Loaded() with { Procedures = procedures });

        procedures.DatesResolved.Should().Be(2, "the invoice date and the one line date");
    }

    [Fact(DisplayName = "082c: no gap, no Gaps entry (must-not-fire control)")]
    public void NoGap_NoGapsEntry()
    {
        var kit = new ProcedureKit().Procedure("All");
        kit.Rule("All", "ALL-INCREMENT", RuleType.TimeIncrement, HalfHour);

        Evaluator().Evaluate(Request(Line(0)), kit.Loaded()).Gaps.Should().BeEmpty();
    }

    [Fact(DisplayName = "082c C3: an Assigned-records-only Procedure with no assignment on the chain gives no finding and no gap")]
    public void AnUnassignedProcedure_GivesNoFindingAndNoGap_C3()
    {
        var kit = new ProcedureKit().Procedure("Assigned", ProcedureAppliesTo.AssignedRecordsOnly, assigned: false, to: new DateOnly(2026, 3, 10));
        kit.Rule("Assigned", "MANUAL-INCREMENT", RuleType.TimeIncrement, HalfHour);

        var result = Evaluator().Evaluate(Request(Line(0)), kit.Loaded());

        result.Findings.Should().BeEmpty();
        result.Gaps.Should().BeEmpty("not applicable is not a gap");
    }

    // ── Determinism ─────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "082c: the result does not depend on the order of the legacy rules or of the Procedure rows")]
    public void TheResult_DoesNotDependOnInputOrder()
    {
        static (BillingEvaluationResult, string[]) Run(bool swap)
        {
            var kit = new ProcedureKit();
            var names = swap ? new[] { "Assigned", "All" } : new[] { "All", "Assigned" };
            foreach (var name in names)
            {
                kit.Procedure(name, name == "All" ? ProcedureAppliesTo.AllRecords : ProcedureAppliesTo.AssignedRecordsOnly, assigned: name == "Assigned");
            }

            foreach (var name in names)
            {
                kit.Rule(name, name.ToUpperInvariant() + "-INCREMENT", RuleType.TimeIncrement, name == "All" ? HalfHour : TenthHour);
                kit.Rule(name, name.ToUpperInvariant() + "-NONBILLABLE", RuleType.NonBillableActivity, ListsA101);
                kit.Rule(name, name.ToUpperInvariant() + "-EXPENSE", RuleType.ExpenseRule);
            }

            BillingRuleCandidate[] legacy =
            [
                Version("CD-INCREMENT", RuleType.TimeIncrement, HalfHour),
                Version("FIRM-NONBILLABLE", RuleType.NonBillableActivity, ListsA101, RuleSetScope.Firm),
                Version("CD-EXPENSE", RuleType.ExpenseRule),
            ];
            var result = Evaluator().Evaluate(Request(Line(0), BadExpense(1), Line(2, LateLine)), kit.Loaded(swap ? [.. legacy.Reverse()] : legacy));
            return (result, result.Findings.Select(f => $"{f.Subject.LineIndex}:{f.PolicyCode}:{f.ReasonCode}").ToArray());
        }

        var (_, forward) = Run(swap: false);
        var (_, swapped) = Run(swap: true);

        forward.Should().NotBeEmpty().And.Equal(swapped);
    }
}
