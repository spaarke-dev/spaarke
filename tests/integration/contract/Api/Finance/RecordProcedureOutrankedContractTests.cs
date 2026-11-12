using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Api.Finance.RecordProcedureRig;

namespace Sprk.Bff.Api.Tests.Api.Finance;

/// <summary>
/// ebilling-r1 task 084e (EB-D-219; ontology-r1 R1-R6, O-6): the <c>Outranked</c> / <c>CapOutranked</c> mark on the record's rules-in-force list,
/// computed by the evaluator's own precedence function over the REAL <see cref="RecordProcedureReadService"/> and an in-memory Dataverse.
/// Matter M, firm F (<c>?firmId=</c>), 2026-02-01. Ranks: an All-records Procedure (All records, 0), a Procedure for firm F (Firm, 0,
/// firm-specific), a legacy Client default rule (All records, 0) and a legacy Firm rule (Firm, 0, firm-specific).
/// </summary>
public sealed class RecordProcedureOutrankedContractTests
{
    private const string Token = "caller-token";
    private const int NotAConsequence = 12345;
    private const string RateBodyTheGateRefuses = """{"type":"ApprovedRate","tolerancePercent":"not a number"}""";
    private const string CapOnly = """{"type":"ExpenseRule","maxAmountPerItem":500}""";
    private const string CodesOnly = """{"type":"ExpenseRule","nonReimbursableCodes":["E110"]}""";
    private const string CodesAndCap = """{"type":"ExpenseRule","nonReimbursableCodes":["E109"],"maxAmountPerItem":500}""";
    private const string ListsA101 = """{"type":"NonBillableActivity","codes":["A101"]}""";
    private const string LateFeeCap = """{"type":"AddedFee","feeKind":"lateFee","allowance":"cap","feeCodes":["E101"],"capAmount":100}""";
    private const string AdminFeeCap = """{"type":"AddedFee","feeKind":"administrativeFee","allowance":"cap","feeCodes":["E102"],"capAmount":50}""";

    private static async Task<RecordRulesInForceView> Rules(RecordProcedureRig rig)
    {
        var result = await rig.Service.ListRulesInForceAsync(Matter, M, F, Feb1, Token, CancellationToken.None);
        result.Status.Should().Be(RecordProcedureReadStatus.Done, string.Join("; ", rig.Log.Entries));
        return result.Value!;
    }

    private static RuleInForceView Rule(RecordRulesInForceView view, string code) => view.Rules.Single(r => r.Code == code && r.InForce);

    private static string Note(string codes, string verb = "is") =>
        $"applies unless {codes} {verb} refused when an invoice is checked; a line can differ (dates, fallback)";

    // ═══════════════════════════════════════════════════════════════════════════
    // Must-fire
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084e must-fire (Replace): a valid higher-rank rule of the same key marks the lower one 'Outranked by X' with the R4 note; the higher one has no mark")]
    public async Task Replace_ValidHigher_MarksTheLowerOutranked()
    {
        var rig = new RecordProcedureRig();
        var firm = rig.Simple("FIRM-RATE", ProcedureAppliesTo.Firm, F);
        rig.Simple("GENERAL");

        var view = await Rules(rig);

        var mark = Rule(view, "GENERAL").Marks.Should().ContainSingle().Subject;
        mark.Kind.Should().Be("Outranked");
        mark.Text.Should().Be("Outranked by FIRM-RATE");
        mark.Note.Should().Be(Note("FIRM-RATE"));
        mark.By.Should().ContainSingle().Which.Should().Be(new OutrankingRuleView(
            firm.Rule, firm.RuleVersion, "FIRM-RATE", "Approved rate", 1, "Procedure", firm.Procedure, "Firm", 0));
        Rule(view, "GENERAL").InForce.Should().BeTrue("a record read never claims the rule does not run (R4)");
        Rule(view, "FIRM-RATE").Marks.Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-fire (Split): a capped higher ExpenseRule marks the lower one 'Cap outranked by X' (its code check still runs)")]
    public async Task Split_CappedHigher_MarksTheLowerCapOutranked()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-EXP", ProcedureAppliesTo.Firm, F, type: RuleType.ExpenseRule, body: CapOnly);
        rig.Simple("GENERAL-EXP", type: RuleType.ExpenseRule, body: CodesAndCap);

        var view = await Rules(rig);

        var mark = Rule(view, "GENERAL-EXP").Marks.Should().ContainSingle().Subject;
        mark.Kind.Should().Be("CapOutranked");
        mark.Text.Should().Be("Cap outranked by FIRM-EXP");
        mark.Note.Should().Be(Note("FIRM-EXP"));
        mark.By!.Select(b => b.Code).Should().Equal("FIRM-EXP");
        Rule(view, "FIRM-EXP").Marks.Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-fire (R6): a legacy Firm rule at a higher rank outranks a Procedure rule, and its policy, version and rule set are in the one batched check")]
    public async Task LegacyHigher_OutranksAProcedureRule_AndIsAsked()
    {
        var rig = new RecordProcedureRig();
        var legacy = rig.AddLegacyRule("LEGACY-RATE", RuleSetScope.Firm, F);
        rig.Simple("GENERAL");

        var view = await Rules(rig);

        var mark = Rule(view, "GENERAL").Marks.Should().ContainSingle().Subject;
        mark.Kind.Should().Be("Outranked");
        mark.By.Should().ContainSingle().Which.Should().Be(new OutrankingRuleView(
            legacy.Policy, legacy.Version, "LEGACY-RATE", "Approved rate", 1, "Legacy", null, "Firm", 0));
        view.Rules.Should().NotContain(r => r.Code == "LEGACY-RATE", "the list is the resolver's; a legacy rule appears only as the one that outranks");
        rig.Probe.Asked.Should().Contain(new[] { ("sprk_policies", legacy.Policy), ("sprk_policyversions", legacy.Version), ("sprk_rulesets", legacy.RuleSet) });
    }

    [Theory(DisplayName = "084e must-fire (EB-D-218): the legacy rule's policy, version or rule set unreadable is 503 with no list")]
    [InlineData("sprk_policies")]
    [InlineData("sprk_policyversions")]
    [InlineData("sprk_rulesets")]
    public async Task LegacyOutrankingRow_Unreadable_Is503(string set)
    {
        var rig = new RecordProcedureRig();
        var legacy = rig.AddLegacyRule("LEGACY-RATE", RuleSetScope.Firm, F);
        rig.Simple("GENERAL");
        await Rules(rig);
        rig.Probe.Denied.Add((set, set switch { "sprk_policies" => legacy.Policy, "sprk_policyversions" => legacy.Version, _ => legacy.RuleSet }));

        var result = await rig.Service.ListRulesInForceAsync(Matter, M, F, Feb1, Token, CancellationToken.None);

        result.Status.Should().Be(RecordProcedureReadStatus.Unavailable);
        result.Value.Should().BeNull();
    }

    [Fact(DisplayName = "084e must-fire (R3) and must-not-fire (tie): a tie at the top lists BOTH under 'by' for the lower rule, and marks neither of the tied rules")]
    public async Task TieAtTop_ListsBoth_MarksNeither()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-A", ProcedureAppliesTo.Firm, F);
        rig.Simple("FIRM-B", ProcedureAppliesTo.Firm, F);
        rig.Simple("GENERAL");

        var view = await Rules(rig);

        var mark = Rule(view, "GENERAL").Marks.Should().ContainSingle().Subject;
        mark.By!.Select(b => b.Code).Should().BeEquivalentTo("FIRM-A", "FIRM-B");
        mark.Text.Should().Be("Outranked by " + mark.By![0].Code + " and " + mark.By[1].Code);
        mark.Note.Should().Be(Note(mark.By[0].Code + " and " + mark.By[1].Code, "are"));
        Rule(view, "FIRM-A").Marks.Should().BeEmpty();
        Rule(view, "FIRM-B").Marks.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Must-not-fire
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact(DisplayName = "084e must-not-fire (R1): a higher rule the billing gate refuses leaves the lower one NOT outranked")]
    public async Task GateRefusedHigher_LeavesTheLowerUnmarked()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-RATE", ProcedureAppliesTo.Firm, F, body: RateBodyTheGateRefuses);
        rig.Simple("GENERAL");

        (await Rules(rig)).Rules.SelectMany(r => r.Marks).Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-not-fire (R1): a downgraded higher rule (a stored consequence its type may not carry) leaves the lower one NOT outranked")]
    public async Task DowngradedHigher_LeavesTheLowerUnmarked()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-RATE", ProcedureAppliesTo.Firm, F, consequence: NotAConsequence);
        rig.Simple("GENERAL");

        (await Rules(rig)).Rules.SelectMany(r => r.Marks).Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-not-fire (R1, R6): a downgraded or gate-refused LEGACY higher rule leaves the lower one NOT outranked, and is not asked")]
    public async Task InvalidLegacyHigher_LeavesTheLowerUnmarked()
    {
        var rig = new RecordProcedureRig();
        var downgraded = rig.AddLegacyRule("LEGACY-DOWN", RuleSetScope.Firm, F, consequence: NotAConsequence);
        var refused = rig.AddLegacyRule("LEGACY-BAD", RuleSetScope.Firm, F, type: RuleType.TimeIncrement, body: """{"type":"TimeIncrement"}""");
        rig.Simple("GENERAL");

        (await Rules(rig)).Rules.SelectMany(r => r.Marks).Should().BeEmpty();
        rig.Probe.Asked.Select(a => a.RecordId).Should().NotContain(new[] { downgraded.Policy, downgraded.Version, refused.Policy, refused.Version });
    }

    [Fact(DisplayName = "084e must-not-fire (R2): a capless higher ExpenseRule leaves the lower cap in force (no mark)")]
    public async Task Split_CaplessHigher_LeavesTheLowerCap()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-EXP", ProcedureAppliesTo.Firm, F, type: RuleType.ExpenseRule, body: CodesOnly);
        rig.Simple("GENERAL-EXP", type: RuleType.ExpenseRule, body: CodesAndCap);

        (await Rules(rig)).Rules.SelectMany(r => r.Marks).Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-not-fire (O-6): a rule above the top that does not displace (capless under Split) gets no mark, and is not named under 'by'")]
    public async Task AboveTopNotDisplacing_GetsNoMark()
    {
        var rig = new RecordProcedureRig();
        rig.Assign(Matter, M, rig.Simple("MANUAL-EXP", ProcedureAppliesTo.AssignedRecordsOnly, type: RuleType.ExpenseRule, body: CodesOnly).Procedure);
        rig.Simple("FIRM-EXP", ProcedureAppliesTo.Firm, F, type: RuleType.ExpenseRule, body: CapOnly);
        rig.Simple("GENERAL-EXP", type: RuleType.ExpenseRule, body: CodesAndCap);

        var view = await Rules(rig);

        Rule(view, "MANUAL-EXP").Marks.Should().BeEmpty();
        Rule(view, "FIRM-EXP").Marks.Should().BeEmpty();
        Rule(view, "GENERAL-EXP").Marks.Should().ContainSingle().Which.By!.Select(b => b.Code).Should().Equal("FIRM-EXP");
    }

    [Fact(DisplayName = "084e must-not-fire (R2): an Additive key marks nothing at any rank")]
    public async Task Additive_MarksNothing()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-NB", ProcedureAppliesTo.Firm, F, type: RuleType.NonBillableActivity, body: ListsA101);
        rig.Simple("GENERAL-NB", type: RuleType.NonBillableActivity, body: ListsA101);

        (await Rules(rig)).Rules.SelectMany(r => r.Marks).Should().BeEmpty();
    }

    [Fact(DisplayName = "084e must-not-fire (O-4): an AddedFee late-fee cap and an administrative-fee cap are separate keys and never mark each other; the same kind does")]
    public async Task AddedFee_LateFeeAndAdminFee_AreSeparateKeys()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-LATE", ProcedureAppliesTo.Firm, F, type: RuleType.AddedFee, body: LateFeeCap);
        rig.Simple("GENERAL-ADMIN", type: RuleType.AddedFee, body: AdminFeeCap);
        rig.Simple("GENERAL-LATE", type: RuleType.AddedFee, body: LateFeeCap);

        var view = await Rules(rig);

        Rule(view, "GENERAL-ADMIN").Marks.Should().BeEmpty("an administrative-fee cap is not outranked by a late-fee cap");
        Rule(view, "GENERAL-LATE").Marks.Should().ContainSingle().Which.By!.Select(b => b.Code).Should().Equal("FIRM-LATE");
    }

    [Fact(DisplayName = "084e (R4): no 'displaced' wording reaches the answer; the mark is Outranked / CapOutranked with the fixed note")]
    public async Task NoDisplacedWording_ReachesTheDto()
    {
        var rig = new RecordProcedureRig();
        rig.Simple("FIRM-RATE", ProcedureAppliesTo.Firm, F);
        rig.Simple("GENERAL");
        rig.Simple("FIRM-EXP", ProcedureAppliesTo.Firm, F, type: RuleType.ExpenseRule, body: CapOnly);
        rig.Simple("GENERAL-EXP", type: RuleType.ExpenseRule, body: CodesAndCap);

        var view = await Rules(rig);

        view.Rules.SelectMany(r => r.Marks).Select(m => m.Kind).Should().BeEquivalentTo("Outranked", "CapOutranked");
        JsonOf(view).Should().NotContainEquivalentOf("displac");
    }

    [Fact(DisplayName = "084e: the Procedures route reads no legacy rule set (the mark is the rules route's only)")]
    public async Task ProceduresRoute_ReadsNoLegacyRules()
    {
        var rig = new RecordProcedureRig();
        rig.AddLegacyRule("LEGACY-RATE", RuleSetScope.Firm, F);
        rig.Simple("GENERAL");

        var result = await rig.Service.ListProceduresAsync(Matter, M, F, Feb1, Token, CancellationToken.None);

        result.Status.Should().Be(RecordProcedureReadStatus.Done);
        JsonOf(result.Value!).Should().NotContain("LEGACY-RATE");
        rig.Probe.Asked.Select(a => a.EntitySet).Should().NotContain(new[] { "sprk_policies", "sprk_policyversions", "sprk_rulesets" });
    }
}
